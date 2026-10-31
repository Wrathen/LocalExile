using GameServer.Core.Accounts;
using GameServer.Core.Protocol.Instance;

namespace GameServer.Core.Protocol.Login;

/// <summary>
/// Server-side state of one official-client login connection after the key exchange: decoded C→S messages in,
/// S→C replies out. No sockets or crypto here (GameServer.Auth does those).
/// </summary>
public sealed class LoginServerSession(AccountService accounts)
{
    /// <summary>0x04 result codes. 0 moves the client to logged-in state 3 (0x14213f190 case 4). Non-zero codes are
    /// BackendErrors rows + 1: the client showed 5 as "The specified account does not exist." (row 4, live 2026-10-16),
    /// so 6 is row 5 InvalidPassword.</summary>
    public const ushort ResultOk = 0, ResultAccountDoesNotExist = 5, ResultInvalidPassword = 6;

    /// <summary>Set after a failed login. The client keeps the connection open and ignores Log In until the server closes
    /// it (live 2026-10-16), so the transport should disconnect once the reply is sent.</summary>
    public bool Failed { get; private set; }

    /// <summary>0x04 Flags0 bits (0x14213f190 case 4).</summary>
    public const byte FlagVerified = 0x20;

    /// <summary>The leagues sent in 0x1b after login: the create screen offers them (with none its Create button stays
    /// disabled).</summary>
    public LeagueCatalog Leagues { get; init; } = LeagueCatalog.Startup;

    public Session? Session { get; private set; }
    public Account? Account { get; private set; }

    public IReadOnlyList<ILoginMessage> Handle(ILoginMessage message) => message switch
    {
        AccountLogin m => Login(m),
        // Sent when the character screen opens (0x140d13060, only in logged-in state 3). Answered with the character
        // list 0x15 (stored at login+0xe68 and shown by the character screen 0x140d12590; live-tested).
        EmptyMessage { MessageId: 0x18 } => [CharacterList()],
        // Play on the character screen sends 0x0e (u8 0, u32 0: character index?; captured 2026-10-16). It waits
        // ("Contacting Server...") for the instance assignment 0x14.
        U8U32 m => EnterGame(m),
        CreateCharacter m => Create(m),
        // Delete on the character screen, after its confirmation (0x140cfafa0): the character's name and a byte (0 live).
        WStringU8Message { MessageId: DeleteCharacterId } m => Delete(m.Text),
        // "Leagues with progress" (0x140d166a0): the leagues a character could migrate progress from. None yet; the
        // client then shows "There are no available leagues to migrate progress from."
        EmptyMessage { MessageId: 0x19 } => Session is null ? [] : [new LeaguesWithProgress(0, [])],
        _ => [],
    };

    /// <summary>Where 0x14 sends the client: the instance (game) server, GameServer.Gateway.</summary>
    public string InstanceHost { get; init; } = "127.0.0.1";
    public ushort InstancePort { get; init; } = 6112;

    /// <summary>Derives the 0x14 instance key from the ticket; GameServer.Gateway must use the same secret.</summary>
    public InstanceKeyring Keyring { get; init; } = InstanceKeyring.Random();

    /// <summary>
    /// 0x14 for the account's first character. The area hash is WorldAreas.HASH16 (Lioneye's Watch, 1_1_town = 0x235c
    /// in 3.29.3b); an unknown hash makes the client throw "Area reported by instance does not exist". +0x30 is the port
    /// (live-verified). The client echoes +0x34 (here the IPv4 address) in instance 0x0E and the ticket at +0x38 in
    /// instance 0x0F. The 64 key bytes become the instance connection's Salsa20 key material (Agents/docs/re/010). Data8 goes
    /// into the 0x0E digest and stays zero.
    /// </summary>
    private ILoginMessage[] EnterGame(U8U32 play)
    {
        if (Session is null) return [];
        var characters = accounts.Characters(Session.AccountId);
        if (characters.Count == 0) return [];
        // The u32 is probably the character's index on the character screen (0 for the first, the only one tested).
        var chosen = characters[(int)Math.Min(play.Value, (uint)characters.Count - 1)];
        // Void characters are listed and cannot be entered, nor can those of a league that has ended or is not loaded.
        if (!HardcoreDeathRules.CanEnter(chosen) || !Leagues.CanEnter(chosen.League))
            return [CharacterList(), new U16Message(0x0f, ResultLeagueDoesNotExist)];
        // Last played now: the character screen selects it next time (CharacterList's B28).
        accounts.Touch(Session.AccountId, chosen.Id, chosen.AreaId);
        CharacterName = chosen.Name;
        CharacterId = chosen.Id;
        CharacterLeague = Leagues.ResolveSavedLeague(chosen.League);
        CharacterClass = chosen.Class;
        CharacterLevel = chosen.Level;
        CharacterLastTown = chosen.LastTown;
        if (Assign is not null)
        {
            var (area, assigned) = Assign(this);
            return [InstanceAssignment(area, assigned)];
        }
        return [InstanceAssignment(0x235c, Keyring.NewTicket())];
    }

    /// <summary>The character chosen with Play (0x0e), once in game.</summary>
    public string? CharacterName { get; private set; }
    public Guid? CharacterId { get; private set; }

    /// <summary>The chosen character's offered or historical league (null if neither catalog knows it).</summary>
    public League? CharacterLeague { get; private set; }

    /// <summary>The chosen character's class.</summary>
    public CharacterClass CharacterClass { get; private set; } = CharacterClass.Witch;

    /// <summary>The chosen character's level (the Auth server changes it for /set_level, so later tickets carry it).</summary>
    public int CharacterLevel { get; set; } = 1;

    /// <summary>The chosen character's last town (WorldAreas Id, null = none yet): where Play puts it.</summary>
    public string? CharacterLastTown { get; private set; }

    /// <summary>Picks the area and ticket for Play (GameServer.Auth: the instance registry). Without it: Lioneye's
    /// Watch with a fresh ticket.</summary>
    public Func<LoginServerSession, (ushort Area, uint Ticket)>? Assign { get; init; }

    /// <summary>0x14 sending the client to <paramref name="areaHash16"/> on the instance server with
    /// <paramref name="ticket"/>. Only at character select: in game the client ignores login 0x14 and area changes use
    /// instance 0x27 (Agents/docs/protocol/09-area-change.md).</summary>
    public InstanceInfo InstanceAssignment(ushort areaHash16, uint ticket)
    {
        uint ip = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(System.Net.IPAddress.Parse(InstanceHost).GetAddressBytes());
        return new InstanceInfo(InstanceHost, InstancePort, ip, areaHash16, ticket, Keyring.Key(ticket), new byte[8], 0);
    }

    private ILoginMessage[] Login(AccountLogin m)
    {
        var r = m.Flag95
            ? accounts.LoginWithSavedClientCredential(m.Account, m.PasswordDigest, m.MachineIdA, m.MachineIdB)
            : accounts.LoginWithClientDigest(m.Account, m.PasswordDigest);
        if (!r.Ok)
        {
            Failed = true;
            ushort code = r.Error == AuthError.NotFound ? ResultAccountDoesNotExist : ResultInvalidPassword;
            return [new LoginResult(code, "", new byte[32], 0, 0, [])];
        }
        Session = r.Value;
        Account = accounts.GetAccount(Session!.AccountId);
        // The wide string is shown to the player in a message box (live test 2026-10-16: sending the account name popped up
        // "devtester" with an OK button), so it stays empty. Digest is the 32-byte saved credential;
        // Flag94 requests remembering it; Flag95 identifies a subsequent login using it instead of a password digest.
        // The real client waits for a push after 0x04; an empty 0x1b moves it on to the character screen, which then
        // asks for the character list with 0x18 (captured 2026-10-16). What 0x1b's own entries are is still unknown.
        // Flags0 bit 5 → login+0xea8: the account's email is verified. With it clear the character screen builds the
        // "Your account is not verified" panel (0x140d195d0) and chat/trade features are locked.
        byte[] remembered = m.Flag94 ? accounts.SavedClientCredential(Session.AccountId, m.MachineIdA, m.MachineIdB) : new byte[32];
        return [new LoginResult(ResultOk, "", remembered, FlagVerified, 0, []),
                new LeagueList(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Leagues.Entries(), false, false)];
    }

    /// <summary>0x15: the characters, then B28 (→ character screen +0x280: the index of the character it selects, the
    /// last played one here; verified live 2026-10-22) and B29, the character slots (login+0xe81 → character screen
    /// +0x281): its Create Character button is enabled only while the account has fewer characters than that
    /// (0x140d17cc0; 0 kept it greyed out, live 2026-10-18).</summary>
    private List15 CharacterList() => new(CharacterEntries(), LastPlayedIndex(), (byte)Math.Clamp(accounts.MaxCharacters, 0, 255), []);

    /// <summary>The list index of the account's most recently played character (Play updates LastPlayedUtc), 0 for none.</summary>
    private byte LastPlayedIndex()
    {
        if (Session is null) return 0;
        var characters = accounts.Characters(Session.AccountId);
        int best = 0;
        for (int i = 1; i < characters.Count; i++)
            if (characters[i].LastPlayedUtc > characters[best].LastPlayedUtc) best = i;
        return (byte)Math.Min(best, 255);
    }

    /// <summary>C→S 0x0c: delete a character; answered with S→C 0x0d (u16 result).</summary>
    public const ushort DeleteCharacterId = 0x0c, DeleteCharacterResultId = 0x0d;

    /// <summary>BackendErrors row 15 + 1 ("CharacterDoesNotExist"); the delete screen shows any non-zero code's text.</summary>
    public const ushort ResultCharacterDoesNotExist = 16;

    /// <summary>Called with the deleted character's name (GameServer.Auth: sets its save aside).</summary>
    public Action<string>? CharacterDeleted { get; init; }

    /// <summary>
    /// Delete character (0x0c) → the new character list (0x15), then 0x0d result 0, on which the delete screen
    /// (0x140cfb5f0, listener at +0xa90) closes back to the list; non-zero shows the BackendErrors text.
    /// </summary>
    private ILoginMessage[] Delete(string name)
    {
        if (Session is null) return [];
        var c = accounts.Characters(Session.AccountId).FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (c is null || !accounts.DeleteCharacter(Session.AccountId, c.Id).Ok)
            return [new U16Message(DeleteCharacterResultId, ResultCharacterDoesNotExist)];
        CharacterDeleted?.Invoke(c.Name);
        return [CharacterList(), new U16Message(DeleteCharacterResultId, ResultOk)];
    }

    /// <summary>0x13 result codes: BackendErrors rows + 1, as for 0x04.</summary>
    public const ushort ResultCharacterNameTaken = 21, ResultCharacterNameInvalid = 22, ResultCharacterNameTooShort = 23,
        ResultCharacterNameTooLong = 24, ResultInvalidClass = 25, ResultLeagueDoesNotExist = 26, ResultTooManyCharacters = 49;

    /// <summary>
    /// Create character (0x12) → 0x13 with the result; on success the new character list (0x15) goes first, and the
    /// client then enters the game with the new character (Play, 0x0e). The class comes from <see cref="ClassOf"/>.
    /// </summary>
    private ILoginMessage[] Create(CreateCharacter m)
    {
        if (Session is null) return [];
        var league = Leagues.Find(m.League);
        if (league is null) return [new CreateCharacterResult(ResultLeagueDoesNotExist, false)];
        if (ClassOf(m) is not { } cls) return [new CreateCharacterResult(ResultInvalidClass, false)];
        string name = m.Name.Trim();
        if (name.Length < 3) return [new CreateCharacterResult(ResultCharacterNameTooShort, false)];
        if (name.Length > 23) return [new CreateCharacterResult(ResultCharacterNameTooLong, false)];
        var r = accounts.CreateCharacter(Session.AccountId, name, cls, league.Name);
        if (!r.Ok)
            return [new CreateCharacterResult(r.Error switch
            {
                AuthError.CharacterNameTaken => ResultCharacterNameTaken,
                AuthError.LimitReached => ResultTooManyCharacters,
                AuthError.InvalidClass => ResultInvalidClass,
                _ => ResultCharacterNameInvalid,
            }, false)];
        // The list first: on 0 the create screen (0x140cf8010) looks the new character up by name in the character list
        // and sends Play (0x0e) with its index; with the list sent after 0x13 it was not there yet and the client waited
        // on "Contacting Server..." (live 2026-10-18).
        return [CharacterList(), new CreateCharacterResult(ResultOk, false)];
    }

    /// <summary>Characters.dat Id → class (CharacterClass is Characters.dat IntegerId).</summary>
    private static readonly Dictionary<string, CharacterClass> ClassIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Metadata/Characters/StrDexInt/StrDexInt"] = CharacterClass.Scion, ["Metadata/Characters/Str/Str"] = CharacterClass.Marauder,
        ["Metadata/Characters/Dex/Dex"] = CharacterClass.Ranger, ["Metadata/Characters/Int/Int"] = CharacterClass.Witch,
        ["Metadata/Characters/StrDex/StrDex"] = CharacterClass.Duelist, ["Metadata/Characters/StrInt/StrInt"] = CharacterClass.Templar,
        ["Metadata/Characters/DexInt/DexInt"] = CharacterClass.Shadow,
    };

    /// <summary>CharacterStartStates.dat Ids start with the class's attributes (DexDefault: the Ranger), longest first.</summary>
    private static readonly (string Prefix, CharacterClass Class)[] StartStatePrefixes =
    [
        ("StrDexInt", CharacterClass.Scion), ("StrDex", CharacterClass.Duelist), ("StrInt", CharacterClass.Templar),
        ("DexInt", CharacterClass.Shadow), ("Str", CharacterClass.Marauder), ("Dex", CharacterClass.Ranger), ("Int", CharacterClass.Witch),
    ];

    /// <summary>The class of a 0x12 request. S78 is the chosen CharacterStartStates.dat Id ("DexDefault" for a Ranger,
    /// live 2026-10-18; the u32s were 0); a Characters.dat Id or class name is accepted too, then a class number.</summary>
    public static CharacterClass? ClassOf(CreateCharacter m)
    {
        if (ClassIds.TryGetValue(m.S78, out var c)) return c;
        if (Enum.TryParse<CharacterClass>(m.S78, true, out c) && Enum.IsDefined(c)) return c;
        foreach (var (prefix, cls) in StartStatePrefixes)
            if (m.S78.StartsWith(prefix, StringComparison.Ordinal)) return cls;
        foreach (uint v in (ReadOnlySpan<uint>)[m.U50, m.U54])
            if (v <= (uint)CharacterClass.Shadow) return (CharacterClass)v;
        return null;
    }

    /// <summary>
    /// The account's characters as 0x15 entries. Mapped live (2026-10-16) with one probe character per field set to 3:
    /// +0x48 is the level ("Level 3"), +0x4a the class (3 showed "Witch"; our enum has the same order with Scion = 0),
    /// +0x44 / +0x46 change the portrait, +0x49 adds a red marker. The others showed no visible change.
    /// </summary>
    private ListEntry15[] CharacterEntries() =>
        Session is null ? [] :
        [.. accounts.Characters(Session.AccountId).Select(c =>
            // U46 (+0x46) is the portrait variation the character screen draws (0x14082e210 → 0x142426b00).
            new ListEntry15(c.Name, c.League, (byte)(c.HardcoreDeath?.PermanentlyDead == true || !HardcoreDeathRules.CanEnter(c) || !Leagues.CanEnter(c.League) ? 1 : 0), 0,
                (byte)Math.Clamp(c.Level, 0, 255), 0, (byte)c.Class, 0, c.Portrait, 0, 0))];
}
