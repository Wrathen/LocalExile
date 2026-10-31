using System.Net;
using System.Net.Sockets;
using GameServer.Core.Accounts;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;
using GameServer.Storage;

namespace GameServer.Core.Tests;

public class HardcoreAuthIntegrationTests
{
    [Theory]
    [InlineData("Hardcore", "Standard", false)]
    public async Task Committed_death_invalidates_resolve_and_transfer_but_allows_softcore_reentry(
        string source, string destination, bool ssf)
    {
        using var fixture = await Fixture.Create(source);
        var first = Assert.IsType<InstanceInfo>(Assert.Single(fixture.Session.Handle(new U8U32(0, 0))));
        var resolved = fixture.Directory.Handle(new() { Op = "resolve", Ticket = first.U32_38 });
        Assert.True(resolved.Ok);
        Assert.Equal((source, true, ssf, false), (resolved.League, resolved.Hardcore, resolved.Ssf, resolved.LeagueForm is not null));

        var original = fixture.Accounts.FindCharacter("DeathProbe")!;
        fixture.Store.UpdateCharacter(HardcoreDeathRules.Apply(original,
            fixture.Session.Leagues.ResolveSavedLeague(source)!, DateTimeOffset.UtcNow));

        foreach (string op in new[] { "resolve", "transfer", "list", "set_level" })
            Assert.False(fixture.Directory.Handle(new() { Op = op, Ticket = first.U32_38, Area = 0x235c, Level = 30 }).Ok);
        Assert.True(fixture.Directory.Handle(new() { Op = "leave", Ticket = first.U32_38 }).Ok);
        var next = Assert.IsType<InstanceInfo>(Assert.Single(fixture.Session.Handle(new U8U32(0, 0))));
        Assert.NotEqual(first.U32_38, next.U32_38);
        var entered = fixture.Directory.Handle(new() { Op = "resolve", Ticket = next.U32_38 });
        Assert.True(entered.Ok);
        Assert.Equal((destination, false, ssf, false), (entered.League, entered.Hardcore, entered.Ssf, entered.LeagueForm is not null));
        Assert.NotEqual(resolved.Instance, entered.Instance);
        Assert.Equal(original.Id, entered.CharacterId);
    }

    sealed class Fixture : IDisposable
    {
        readonly string _folder = Path.Combine(Path.GetTempPath(), $"hardcore-auth-{Guid.NewGuid():N}");
        readonly SaveDatabase _db;
        readonly TcpClient _client;
        readonly TcpClient _peer;
        public SqliteAccountStore Store { get; }
        public AccountService Accounts { get; }
        public LoginServerSession Session { get; }
        public InstanceDirectory Directory { get; }

        Fixture(TcpClient client, TcpClient peer, string league)
        {
            _client = client;
            _peer = peer;
            _db = new SaveDatabase(Path.Combine(_folder, "saves.db"));
            Store = new SqliteAccountStore(_db);
            Accounts = new AccountService(Store);
            var account = Accounts.Register("DeathAccount", "pw1234").Value!;
            Accounts.CreateCharacter(account.Id, "DeathProbe", CharacterClass.Witch, league);
            Directory = new InstanceDirectory(new InstanceRegistry(), Accounts);
            InstanceDirectory.LoginLink? link = null;
            Session = new LoginServerSession(Accounts) { Assign = session => Directory.AssignStart(session, link!) };
            link = new InstanceDirectory.LoginLink(peer.GetStream(), Session.Keyring.Ciphers(1), Session, "test");
            Session.Handle(new AccountLogin(2, account.Name, new byte[32], AccountService.ClientPasswordDigest("pw1234"),
                new byte[32], false, false, 0x40, 0));
        }

        public static async Task<Fixture> Create(string league)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var client = new TcpClient();
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
            var peer = await listener.AcceptTcpClientAsync();
            return new Fixture(client, peer, league);
        }

        public void Dispose()
        {
            _client.Dispose();
            _peer.Dispose();
            _db.Dispose();
            System.IO.Directory.Delete(_folder, recursive: true);
        }
    }
}
