using System.Net;
using System.Net.Sockets;
using GameServer.Core.Accounts;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Storage;

// GameServer.Auth — the login server the official client talks to (custom TCP, see Agents/docs/re/007 and 008).
//   keygen <Game.exe> [keys.json]          new signing + static keys over the client's own DSA domain
//   patch  <Game.exe> <out.exe> [keys.json] write a copy of the client that trusts those keys
//   passwd <account> <password>            set a dev account's password (any length; restart Auth afterwards)
//   [serve] [keys.json]                    accept logins: 0x0002 key exchange, then answer account login 0x03 with 0x04
//                                          and log every decoded message (Agents/docs/re/009)
// Port: Game_LOGIN_PORT (default 20493). Keys default to data/login-keys.json. Accounts: the save database
// (Game_SAVES_DB, default data/saves.db, shared with GameServer.Host and the Gateway; register through the Host), with an
// older data/accounts.json (Game_DEV_DB) imported once. Instance keys: data/instance-secret.bin (Game_INSTANCE_SECRET),
// shared with GameServer.Gateway. Instance control for the Gateway: 127.0.0.1:Game_AUTH_CONTROL_PORT (20495). Play sends
// the client to the Gateway at Game_GATEWAY_HOST:Game_GATEWAY_PORT (127.0.0.1:6112, GatewayEndpoint).

const string DefaultKeys = "data/login-keys.json";
string cmd = args.Length > 0 ? args[0] : "serve";

try
{
switch (cmd)
{
    case "passwd" when args.Length >= 3:
    {
        var service = new AccountService(SqliteAccountStore.Open());
        var r = service.SetPassword(args[1], args[2]);
        Console.WriteLine(r.Ok ? $"[auth] password of {args[1]} changed; restart Auth (live.ps1 -Only Auth) to use it"
            : $"[auth] {args[1]}: {r.Error}");
        return r.Ok ? 0 : 1;
    }
    case "keygen" when args.Length >= 2:
    {
        string outPath = args.Length > 2 ? args[2] : DefaultKeys;
        if (File.Exists(outPath)) { Console.Error.WriteLine($"[auth] {outPath} exists; delete it to regenerate."); return 1; }
        var keys = LoginServerKeys.Generate(new ClientImage(File.ReadAllBytes(args[1])));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, keys.ToJson());
        Console.WriteLine($"[auth] wrote {outPath}");
        return 0;
    }
    case "patch" when args.Length >= 3:
    {
        if (Path.GetFullPath(args[1]).Equals(Path.GetFullPath(args[2]), StringComparison.OrdinalIgnoreCase))
        { Console.Error.WriteLine("[auth] refusing to overwrite the input; give a different output path."); return 1; }
        var keys = LoginServerKeys.FromJson(File.ReadAllText(args.Length > 3 ? args[3] : DefaultKeys));
        var image = new ClientImage(File.ReadAllBytes(args[1]));
        Console.WriteLine($"[auth] login key blobs at file offset 0x{image.Offset:x}");
        image.Patch(keys);
        File.WriteAllBytes(args[2], image.Bytes);
        Console.WriteLine($"[auth] wrote {args[2]}");
        return 0;
    }
    case "serve":
        await Serve(LoginServerKeys.FromJson(File.ReadAllText(args.Length > 1 ? args[1] : DefaultKeys)));
        return 0;
    default:
        Console.Error.WriteLine("usage: GameServer.Auth keygen <Game.exe> [keys.json] | patch <Game.exe> <out.exe> [keys.json] | serve [keys.json]");
        return 2;
}
}
catch (FormatException ex)
{
    Console.Error.WriteLine($"[auth] {ex.Message}: the file is not the 3.29.3b client executable (PathOfExile.exe copied to Client/Game.exe).");
    return 1;
}
catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
{
    Console.Error.WriteLine($"[auth] file not found: {ex.Message} (run from the repository root; see Agents/GettingStarted.md)");
    return 1;
}

static async Task Serve(LoginServerKeys keys)
{
    var accounts = new AccountService(SqliteAccountStore.Open());
    Console.OutputEncoding = System.Text.Encoding.UTF8; // decoded wide strings
    GameServer.Core.Diagnostics.ServerLog.Install("auth");   // after the encoding: it wraps the console writer as it is
    int port = int.TryParse(Environment.GetEnvironmentVariable("Game_LOGIN_PORT"), out var p) ? p : 20493;
    // Local play only by default; Game_BIND=0.0.0.0 to accept other machines.
    var bind = IPAddress.Parse(Environment.GetEnvironmentVariable("Game_BIND") ?? "127.0.0.1");
    var listener = new TcpListener(bind, port);
    listener.Start();
    var keyring = InstanceKeyring.LoadOrCreate(Environment.GetEnvironmentVariable("Game_INSTANCE_SECRET") ?? Path.Combine("data", "instance-secret.bin"));
    // Every town of the game data (one shared instance each, seed 1); only Lioneye's Watch without data/tables.
    var towns = GameServer.Core.World.WorldAreaTowns.Load(Path.Combine("data", "tables", "WorldAreas.json"));
    // A new instance of an area the Gateway generates itself (ServerLevelAreas) gets a random seed; every other area
    // keeps seed 1, the seed its captures were dumped at. Game_RANDOM_SEEDS=0 (or Game_SERVER_LEVELS=0, which turns
    // the Gateway's generation off) pins them all to 1. Hideouts have a fixed layout: always 1.
    var hideouts = GameServer.Core.World.WorldAreaHideouts.Load(Path.Combine("data", "tables", "WorldAreas.json"));
    // Game_SEED=<n> (dev) gives every new non-hideout instance that seed instead, for generation traces of one seed.
    uint? fixedSeed = uint.TryParse(Environment.GetEnvironmentVariable("Game_SEED"), out var s) ? s : null;
    var hideoutAreas = GameServer.Core.World.WorldAreaHideouts.LoadAreas(Path.Combine("data", "tables", "WorldAreas.json"));
    var randomAreas = Environment.GetEnvironmentVariable("Game_RANDOM_SEEDS") == "0" || Environment.GetEnvironmentVariable("Game_SERVER_LEVELS") == "0"
        ? new HashSet<ushort>()
        : GameServer.Core.World.ServerLevelAreas.Hashes(Path.Combine("data", "tables", "WorldAreas.json"),
            GameServer.Core.World.ServerLevelAreas.Load(GameServer.Core.World.ServerLevelAreas.BaselineFile, m => Console.WriteLine($"[auth] {m}")));
    Console.WriteLine($"[auth] random seeds for {randomAreas.Count} server-generated area(s)");
    using var progress = new SaveDatabase(SaveDatabase.DefaultPath);
    var registry = new GameServer.Core.World.InstanceRegistry
    {
        SeedFor = fixedSeed is { } seed
            ? area => hideouts.Contains(area) ? 1u : seed
            : area => randomAreas.Contains(area) && !hideouts.Contains(area) ? (uint)Random.Shared.Next(1, int.MaxValue) : 1u,
        Towns = towns.Select(t => t.Hash16).ToHashSet(),
    };
    Console.WriteLine($"[auth] towns: {towns.Count} ({string.Join(", ", towns.Select(t => t.Id))})");
    var directory = new InstanceDirectory(registry, accounts, towns, character =>
        CharacterHome.SelectedHideout(progress, character) is { } area && hideoutAreas.TryGetValue(area, out ushort hash) ? hash : null);
    _ = directory.ServeControlAsync(InstanceControl.Port);
    Console.WriteLine($"[auth] login server on tcp/{port}");
    // Game_LEAGUES: comma-separated base leagues (default Standard, Allflame), each with its Hardcore / SSF / Ruthless variants.
    Console.WriteLine($"[auth] leagues: {string.Join(", ", LeagueCatalog.FromEnvironment().All.Select(l => l.Name))}");
    while (true)
    {
        var tcp = await listener.AcceptTcpClientAsync();
        _ = Task.Run(() => HandleConnection(tcp, keys, accounts, keyring, directory));
    }
}

static async Task HandleConnection(TcpClient tcp, LoginServerKeys keys, AccountService accounts, InstanceKeyring keyring, InstanceDirectory directory)
{
    string peer = tcp.Client.RemoteEndPoint?.ToString() ?? "?";
    Console.WriteLine($"[auth] {peer} connected");
    try
    {
        using (tcp)
        {
            var stream = tcp.GetStream();
            byte[] buf = new byte[64 * 1024];
            int have = 0;
            KeyExchange? hello;
            int used;
            while (!KeyExchange.TryDecode(buf.AsSpan(0, have), out hello, out used))
            {
                int n = await stream.ReadAsync(buf.AsMemory(have));
                if (n == 0) { Console.WriteLine($"[auth] {peer} closed before key exchange"); return; }
                have += n;
            }
            Console.WriteLine($"[auth] {peer} 0x0002 client key {hello!.Key.Length} bytes, sig {hello.Signature.Length} bytes");

            var (reply, ciphers) = new ServerHandshake(keys).Respond(hello);
            await stream.WriteAsync(reply);
            Console.WriteLine($"[auth] {peer} key exchange answered; traffic is now Salsa20");

            // Anything the client sent after its 0x0002 is already ciphertext.
            var incoming = new LoginMessageStream(LoginDirection.ClientToServer);
            InstanceDirectory.LoginLink? link = null;
            var session = new LoginServerSession(accounts) { InstanceHost = GatewayEndpoint.Host, InstancePort = GatewayEndpoint.Port, Keyring = keyring, Leagues = LeagueCatalog.FromEnvironment(), CharacterDeleted = SetSaveAside, Assign = s => directory.AssignStart(s, link!) };
            link = new InstanceDirectory.LoginLink(stream, ciphers, session, peer);
            byte[] rest = buf[used..have];
            try
            {
                if (rest.Length > 0) await Reply(link, Receive(peer, ciphers, incoming, rest));
                while (!session.Failed)
                {
                    int n = await stream.ReadAsync(buf);
                    if (n == 0) break;
                    await Reply(link, Receive(peer, ciphers, incoming, buf[..n]));
                }
            }
            finally { directory.Forget(link); }
            if (session.Failed) Console.WriteLine($"[auth] {peer} login failed; closing so the client can retry");
            if (incoming.Undecoded.Count > 0 && !incoming.Desynced)
                Console.WriteLine($"[auth] {peer} closed with {incoming.Undecoded.Count} bytes of an incomplete message");
        }
    }
    catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException or System.Security.Cryptography.CryptographicException)
    {
        Console.WriteLine($"[auth] {peer} error: {GameServer.Core.Diagnostics.ServerLog.Describe(ex)}");
    }
    Console.WriteLine($"[auth] {peer} disconnected");
}

// Decrypts a chunk and logs each complete message. After an unknown id the stream can't be resynchronized (no length
// prefix), so the rest is hex-dumped as it arrives.
static List<ILoginMessage> Receive(string peer, ConnectionCiphers ciphers, LoginMessageStream incoming, byte[] data)
{
    ciphers.Receive.Process(data);
    bool wasDesynced = incoming.Desynced;
    var messages = incoming.Push(data);
    foreach (var m in messages)
        Console.WriteLine($"[auth] {peer} <= {LoginMessageFormat.Describe(Redact(m))}");
    if (!incoming.Desynced) return messages;
    if (!wasDesynced)
    {
        Console.WriteLine($"[auth] {peer} cannot decode further: {incoming.Error}");
        Dump([.. incoming.Undecoded]);
    }
    else Dump(data);
    return messages;
}

// Password digests, saved-login tokens/tickets and machine ids stay out of logs: a client with saved login details
// sends a token for the real account it remembers.
static ILoginMessage Redact(ILoginMessage m) => m switch
{
    AccountLogin l => l with { PasswordDigest = [], MachineIdA = [], MachineIdB = [] },
    TokenLogin t => t with { Token = [], MachineIdA = [], MachineIdB = [] },
    TicketLogin t => t with { Ticket = "<redacted>", MachineIdA = [], MachineIdB = [] },
    EpicLogin e => e with { Token = [] },
    TwoDigests d => d with { DigestA = [], DigestB = [] },
    CreateCharacter r => r with { PasswordDigest = [] },
    _ => m,
};

// A deleted character's save in the database went to deleted_characters with the deletion itself (SqliteAccountStore). A
// save from before the database (data/saves/<name>.json, the Gateway hasn't imported it yet) moves to data/saves/deleted/,
// so a new character with that name starts with its class's kit and the old one can still be recovered by hand.
static void SetSaveAside(string character)
{
    Console.WriteLine($"[auth] {character} deleted; its save, if it had one, is in deleted_characters ({SaveDatabase.DefaultPath})");
    string folder = Path.Combine("data", "saves");
    string safe = string.Concat(character.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));
    string path = Path.Combine(folder, safe + ".json");
    if (!File.Exists(path)) return;
    string deleted = Path.Combine(folder, "deleted");
    Directory.CreateDirectory(deleted);
    string to = Path.Combine(deleted, $"{safe}-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
    File.Move(path, to);
    Console.WriteLine($"[auth] {character} deleted; save moved to {to}");
}

static async Task Reply(InstanceDirectory.LoginLink link, List<ILoginMessage> received)
{
    foreach (var m in received)
        foreach (var reply in link.Session.Handle(m))
            await link.SendAsync(reply);
}

static void Dump(byte[] data)
{
    for (int i = 0; i < data.Length; i += 16)
    {
        var row = data.AsSpan(i, Math.Min(16, data.Length - i));
        string ascii = new(row.ToArray().Select(b => b is >= 0x20 and < 0x7f ? (char)b : '.').ToArray());
        Console.WriteLine($"        {i:x4}  {Convert.ToHexString(row).ToLowerInvariant(),-32}  {ascii}");
    }
}
