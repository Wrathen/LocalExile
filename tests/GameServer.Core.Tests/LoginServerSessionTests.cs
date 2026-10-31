using GameServer.Core.Accounts;
using GameServer.Core.Protocol.Login;
using GameServer.Storage;

namespace GameServer.Core.Tests;

public class LoginServerSessionTests
{
    private static AccountLogin Request(string name, string password) =>
        new(2, name, new byte[32], AccountService.ClientPasswordDigest(password), new byte[32], false, false, 0x40, 0);

    // Through the real codec both ways, as GameServer.Auth does it (minus encryption).
    private static LoginResult RoundTrip(LoginServerSession session, AccountLogin request)
    {
        var up = new LoginMessageStream(LoginDirection.ClientToServer).Push(LoginMessage.Encode(request));
        var replies = session.Handle(Assert.Single(up));
        var down = new LoginMessageStream(LoginDirection.ServerToClient).Push([.. replies.SelectMany(LoginMessage.Encode)]);
        Assert.Equal(replies.Count, down.Count);
        if (down.Count > 1) Assert.IsType<LeagueList>(down[1]);
        return Assert.IsType<LoginResult>(down[0]);
    }

    [Fact]
    public void Client_digest_matches_sha256_of_utf16le()
    {
        byte[] utf16le = [(byte)'p', 0, (byte)'w', 0, 0xe9, 0];
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(utf16le), AccountService.ClientPasswordDigest("pwé"));
    }

    [Fact]
    public void Correct_password_logs_in_and_wrong_one_is_rejected()
    {
        var accounts = new AccountService(new JsonAccountStore());
        Assert.True(accounts.Register("alice", "pw1234").Ok);

        var good = new LoginServerSession(accounts);
        var ok = RoundTrip(good, Request("ALICE", "pw1234"));
        Assert.Equal(LoginServerSession.ResultOk, ok.Result);
        Assert.Equal("", ok.AccountName); // shown to the player as a message box
        Assert.Equal("alice", good.Account!.Name);
        Assert.NotNull(good.Session);

        var bad = new LoginServerSession(accounts);
        Assert.Equal(LoginServerSession.ResultInvalidPassword, RoundTrip(bad, Request("alice", "nope")).Result);
        Assert.True(bad.Failed);
        Assert.Null(bad.Session);
        Assert.Equal(LoginServerSession.ResultAccountDoesNotExist, RoundTrip(new LoginServerSession(accounts), Request("nobody", "pw1234")).Result);
    }

    [Fact]
    public void Account_without_digest_verifier_gets_one_on_next_dev_login()
    {
        var store = new JsonAccountStore();
        var accounts = new AccountService(store);
        var acc = accounts.Register("bob", "secret").Value!;
        store.UpdateAccount(acc with { ClientDigestHash = null }); // as stored before the verifier existed

        Assert.False(accounts.LoginWithClientDigest("bob", AccountService.ClientPasswordDigest("secret")).Ok);
        Assert.True(accounts.Login("bob", "secret").Ok);
        Assert.True(accounts.LoginWithClientDigest("bob", AccountService.ClientPasswordDigest("secret")).Ok);
    }

    [Fact]
    public void Remembered_login_survives_service_restart_and_is_bound_to_account_device_and_password()
    {
        var store = new JsonAccountStore();
        var accounts = new AccountService(store);
        accounts.Register("remembered", "pw1234");
        accounts.Register("other", "pw1234");
        var request = Request("remembered", "pw1234") with { Flag94 = true,
            MachineIdA = Enumerable.Repeat((byte)1, 32).ToArray(), MachineIdB = Enumerable.Repeat((byte)2, 32).ToArray() };
        var result = RoundTrip(new LoginServerSession(accounts), request);
        Assert.Equal(LoginServerSession.ResultOk, result.Result);
        Assert.NotEqual(new byte[32], result.Digest);
        Assert.NotEqual(request.PasswordDigest, result.Digest);
        var saved = request with { Flag95 = true, PasswordDigest = result.Digest };
        var restarted = new AccountService(store);
        Assert.Equal(LoginServerSession.ResultOk, RoundTrip(new LoginServerSession(restarted), saved).Result);
        foreach (var invalid in new[] { saved with { Account = "other" },
            saved with { MachineIdA = new byte[32] }, saved with { MachineIdB = new byte[32] },
            saved with { PasswordDigest = new byte[32] }, saved with { PasswordDigest = request.PasswordDigest },
            saved with { Flag95 = false } })
            Assert.Equal(LoginServerSession.ResultInvalidPassword, RoundTrip(new LoginServerSession(restarted), invalid).Result);
        Assert.Equal(new byte[32], RoundTrip(new LoginServerSession(accounts), request with { Flag94 = false }).Digest);
        accounts.SetPassword("remembered", "changed");
        Assert.Equal(LoginServerSession.ResultInvalidPassword, RoundTrip(new LoginServerSession(restarted), saved).Result);
        Assert.Equal(LoginServerSession.ResultOk, RoundTrip(new LoginServerSession(restarted), Request("remembered", "changed")).Result);
    }

    [Fact]
    public void Saved_credential_works_after_closing_and_reopening_the_account_database()
    {
        string folder = Directory.CreateTempSubdirectory("remember-login-").FullName;
        string path = Path.Combine(folder, "saves.db");
        try
        {
            var request = Request("persisted", "pw1234") with { Flag94 = true };
            byte[] credential;
            using (var database = new SaveDatabase(path))
            {
                var service = new AccountService(new SqliteAccountStore(database));
                service.Register("persisted", "pw1234");
                credential = RoundTrip(new LoginServerSession(service), request).Digest;
            }
            using (var database = new SaveDatabase(path))
            {
                var service = new AccountService(new SqliteAccountStore(database));
                Assert.Equal(LoginServerSession.ResultOk, RoundTrip(new LoginServerSession(service),
                    request with { Flag95 = true, PasswordDigest = credential }).Result);
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    // The flow a real 3.29.3b client went through on 2026-10-16: 0x03 → (0x04, 0x1b), 0x18 → 0x15, 0x0e → 0x14.
    [Fact]
    public void Live_verified_flow_lists_characters_and_assigns_an_instance()
    {
        var accounts = new AccountService(new JsonAccountStore());
        var acc = accounts.Register("carol", "pw1234").Value!;
        accounts.CreateCharacter(acc.Id, "CarolWitch", CharacterClass.Witch);
        var session = new LoginServerSession(accounts);

        var login = session.Handle(Request("carol", "pw1234"));
        var result = Assert.IsType<LoginResult>(login[0]);
        Assert.Equal(LoginServerSession.FlagVerified, result.Flags0);
        Assert.IsType<LeagueList>(login[1]);

        var list = Assert.IsType<List15>(Assert.Single(session.Handle(new EmptyMessage(0x18))));
        Assert.Equal(24, list.B29); // character slots: the Create Character button needs a free one
        var e = Assert.Single(list.Entries);
        Assert.Equal(("CarolWitch", "Standard", (byte)1, (byte)CharacterClass.Witch), (e.Name, e.Text, e.B48, e.B4a));

        var info = Assert.IsType<InstanceInfo>(Assert.Single(session.Handle(new U8U32(0, 0))));
        Assert.Equal(("127.0.0.1", (ushort)6112, (ushort)0x235c), (info.Host, info.U16_30, info.AreaHash));

        // Every reply survives the S→C codec.
        var all = login.Concat([list, info]).ToList();
        var decoded = new LoginMessageStream(LoginDirection.ServerToClient).Push([.. all.SelectMany(LoginMessage.Encode)]);
        Assert.Equal(all.Count, decoded.Count);
    }

    [Fact]
    public void Login_sends_the_leagues_with_their_variants_linked()
    {
        var accounts = new AccountService(new JsonAccountStore());
        accounts.Register("dave", "pw1234");
        var session = new LoginServerSession(accounts);
        var leagues = Assert.IsType<LeagueList>(session.Handle(Request("dave", "pw1234"))[1]).Leagues;
        Assert.Equal(16, leagues.Count);
        Assert.All(leagues, l => Assert.NotEmpty(l.Description)); // an empty one crashes the client's league card
        var main = leagues.Where(l => (l.Flags0 & LeagueEntry.Main) != 0).Select(l => l.Name);
        Assert.Equal(["Allflame", "Hardcore Allflame", "Standard"], main.Order());
        var hc = Assert.Single(leagues, l => l.Name == "Hardcore");
        Assert.Equal(("Standard", LeagueEntry.Hardcore, LeagueEntry.Variant), (hc.HardcoreParent, hc.Flags0, hc.Flags1));
        Assert.Equal("Hardcore", Assert.Single(leagues, l => l.Name == "SSF Hardcore").SsfParent);
        Assert.Equal("HC SSF Allflame", Assert.Single(leagues, l => l.Name == "HC SSF R Allflame").RuthlessParent);
        Assert.Equal(LeagueEntry.Variant | LeagueEntry.Ruthless, Assert.Single(leagues, l => l.Name == "Ruthless").Flags1);
    }

    [Theory]
    [InlineData("Allflame", false, false, false)]
    public void Saved_default_family_modes_resolve_when_the_creation_catalog_is_narrowed(
        string leagueName, bool hardcore, bool ssf, bool ruthless)
    {
        var accounts = new AccountService(new JsonAccountStore());
        var account = accounts.Register("legacy", "pw1234").Value!;
        accounts.CreateCharacter(account.Id, "LegacyRanger", CharacterClass.Ranger, leagueName);
        var session = new LoginServerSession(accounts) { Leagues = new(LeagueCatalog.FromEnvironment().Loaded.Select(l => l with { Creatable = l.Base != leagueName })) };
        var offered = Assert.IsType<LeagueList>(session.Handle(Request("legacy", "pw1234"))[1]);
        Assert.DoesNotContain(offered.Leagues, l => l.Name == leagueName);
        Assert.Null(session.Leagues.Find(leagueName));

        session.Handle(new U8U32(0, 0));
        var resolved = Assert.IsType<League>(session.CharacterLeague);
        Assert.Equal((leagueName, hardcore, ssf, ruthless),
            (resolved.Name, resolved.Hardcore, resolved.Ssf, resolved.Form is not null));
        Assert.Equal(leagueName, Assert.Single(accounts.Characters(account.Id)).League);

        var rejected = Assert.IsType<CreateCharacterResult>(Assert.Single(session.Handle(
            new CreateCharacter("NewLegacyRanger", leagueName, 0, 0, "DexDefault", new byte[32]))));
        Assert.Equal(LoginServerSession.ResultLeagueDoesNotExist, rejected.Result);
        Assert.Single(accounts.Characters(account.Id));
    }

    [Fact]
    public void Create_character_stores_it_in_the_league_and_lists_it()
    {
        var accounts = new AccountService(new JsonAccountStore());
        var acc = accounts.Register("erin", "pw1234").Value!;
        var session = new LoginServerSession(accounts);
        session.Handle(Request("erin", "pw1234"));

        // As the 3.29.3b client sent it (live 2026-10-18): the start state names the class.
        var replies = session.Handle(new CreateCharacter("ErinRanger", "Hardcore Allflame", 0, 0, "DexDefault", new byte[32]));
        // The list goes first: the client looks the new character up in it when 0x13 arrives, then sends Play.
        var e = Assert.Single(Assert.IsType<List15>(replies[0]).Entries);
        Assert.Equal(LoginServerSession.ResultOk, Assert.IsType<CreateCharacterResult>(replies[1]).Result);
        Assert.Equal(("ErinRanger", "Hardcore Allflame", (byte)CharacterClass.Ranger), (e.Name, e.Text, e.B4a));
        Assert.Equal("Hardcore Allflame", Assert.Single(accounts.Characters(acc.Id)).League);

        Result(LoginServerSession.ResultCharacterNameTaken, new CreateCharacter("ErinRanger", "Standard", 0, 0, "Metadata/Characters/Dex/Dex", new byte[32]));
        Result(LoginServerSession.ResultLeagueDoesNotExist, new CreateCharacter("Other", "Nowhere", 0, 0, "Metadata/Characters/Dex/Dex", new byte[32]));
        Result(LoginServerSession.ResultCharacterNameTooShort, new CreateCharacter("ab", "Standard", 0, 0, "Metadata/Characters/Dex/Dex", new byte[32]));
        Result(LoginServerSession.ResultInvalidClass, new CreateCharacter("Other", "Standard", 99, 99, "?", new byte[32]));

        // Play then knows the character's league (the Gateway gets it through Auth's resolve).
        session.Handle(new U8U32(0, 0));
        Assert.True(session.CharacterLeague!.Hardcore);
        Assert.Equal(CharacterClass.Ranger, session.CharacterClass);

        void Result(ushort code, CreateCharacter m) =>
            Assert.Equal(code, Assert.IsType<CreateCharacterResult>(Assert.Single(session.Handle(m))).Result);
    }

    [Fact]
    public void Delete_character_removes_it_and_lists_the_rest()
    {
        var accounts = new AccountService(new JsonAccountStore());
        var acc = accounts.Register("fay", "pw1234").Value!;
        accounts.CreateCharacter(acc.Id, "FayOne", CharacterClass.Witch);
        accounts.CreateCharacter(acc.Id, "FayTwo", CharacterClass.Templar);
        string? deleted = null;
        var session = new LoginServerSession(accounts) { CharacterDeleted = n => deleted = n };
        session.Handle(Request("fay", "pw1234"));

        var replies = session.Handle(new WStringU8Message(LoginServerSession.DeleteCharacterId, "fayone", 0));
        Assert.Equal("FayTwo", Assert.Single(Assert.IsType<List15>(replies[0]).Entries).Name);
        Assert.Equal(new U16Message(LoginServerSession.DeleteCharacterResultId, 0), replies[1]);
        Assert.Equal("FayOne", deleted);
        Assert.Equal("FayTwo", Assert.Single(accounts.Characters(acc.Id)).Name);

        var missing = Assert.IsType<U16Message>(Assert.Single(session.Handle(new WStringU8Message(LoginServerSession.DeleteCharacterId, "Nobody", 0))));
        Assert.Equal(LoginServerSession.ResultCharacterDoesNotExist, missing.Value);
    }

    [Fact]
    public void Nothing_but_login_is_answered_before_logging_in()
    {
        var session = new LoginServerSession(new AccountService(new JsonAccountStore()));
        Assert.Empty(session.Handle(new EmptyMessage(0x18)).OfType<List15>().SelectMany(l => l.Entries));
        Assert.Empty(session.Handle(new U8U32(0, 0)));
    }
}
