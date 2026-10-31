using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

/// <summary>
/// Exercises the Gateway's actual private mutation/load path, durable saves and encrypted network sender. No listening
/// game server or game process is started. The real extracted client data is required, as for the other data-backed tests.
/// </summary>
[CollectionDefinition("Ascendancy Gateway integration", DisableParallelization = true)]
public sealed class AscendancyGatewayIntegrationCollection;

[Collection("Ascendancy Gateway integration")]
public sealed partial class AscendancyGatewayIntegrationTests
{
    [Fact]
    public async Task Ascendancy_allocation_grants_ordinary_points_and_both_pools_survive_a_new_connection()
    {
        if (!DataAvailable()) return;
        using var game = new GatewayCharacter();
        var first = await game.Change([54114, 48846], allocate: true);
        Assert.Null(first.Error);
        Assert.Equal(new ushort[] { 54114, 48846 }, game.State.MainAscendancy.Allocated);
        Assert.Empty(game.State.Passives);
        Assert.Equal(8, game.State.MainAscendancy.EarnedPoints);
        Assert.Equal((ushort)20, game.State.RefundPoints);
        var initialPacket = PassivePacket(first.Bytes, 2);
        Assert.Equal((ushort)8, initialPacket.AscendancyPoints);
        Assert.Equal((ushort)0, initialPacket.QuestPoints);
        Assert.Equal((byte)1, initialPacket.Level);
        Assert.Equal((byte)0x44, initialPacket.ClassByte); // Duelist IntegerId 4, ascendancy ClassNo 1.
        Assert.Equal(new ushort[] { 48846, 54114 }, initialPacket.Nodes);

        ushort[] ordinary = OrdinaryDuelistNodes(6);
        var ordinaryChange = await game.Change(ordinary, allocate: true);
        Assert.Null(ordinaryChange.Error); // level 1 has no points of its own; the ascendancy grants exactly six.
        Assert.Equal(ordinary, game.State.Passives);
        Assert.Equal((ushort)0, game.State.ExtraPassivePoints);
        var packet = PassivePacket(ordinaryChange.Bytes, 8);
        Assert.Equal((ushort)8, packet.AscendancyPoints);
        Assert.Equal((ushort)0, packet.QuestPoints);
        Assert.Equal(ordinary.Concat(new ushort[] { 54114, 48846 }).Order(), packet.Nodes);

        var reconnected = game.Reload();
        Assert.Equal(ordinary, reconnected.Passives);
        Assert.Equal(new ushort[] { 54114, 48846 }, reconnected.MainAscendancy.Allocated);
        Assert.Equal(8, reconnected.MainAscendancy.EarnedPoints);
        Assert.Equal((ushort)20, reconnected.RefundPoints);
        Assert.Equal(AscendancyFamily.Phrecia, reconnected.MainAscendancy.Selection!.Family);
    }

    static bool DataAvailable()
    {
        // Data-backed tests can run in a fresh clone. When the extraction is present, execute the integration path.
        return File.Exists(Path.Combine("data", AscendancyCatalog.GraphFile)) && File.Exists("data/tables/PassiveSkills.json");
    }

    static ushort[] OrdinaryDuelistNodes(int count)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine("data", AscendancyCatalog.GraphFile)));
        var graph = document.RootElement.GetProperty("normal").GetProperty("nodes").EnumerateArray()
            .ToDictionary(n => checked((ushort)n.GetProperty("id").GetInt32()),
                n => n.GetProperty("adjacent").EnumerateArray().Select(a => checked((ushort)a.GetInt32())).ToArray());
        var result = new List<ushort>();
        var visited = new HashSet<ushort> { 50986 }; // actual Duelist start, shared with Aristocrat's start connection.
        var queue = new Queue<ushort>();
        queue.Enqueue(50986);
        while (queue.TryDequeue(out ushort current) && result.Count < count)
            foreach (ushort next in graph[current])
                if (Program.Passives.Find(next) is { IsAscendancy: false, IsStart: false, MasteryGroup: < 0 } && visited.Add(next))
                {
                    result.Add(next);
                    queue.Enqueue(next);
                    if (result.Count == count) break;
                }
        Assert.Equal(count, result.Count);
        return result.ToArray();
    }

    static ushort[] PathTo(AscendancyCatalog.Definition definition, ushort end)
    {
        var queue = new Queue<(ushort Id, ushort[] Path)>();
        var seen = new HashSet<ushort> { definition.StartNode };
        queue.Enqueue((definition.StartNode, []));
        while (queue.TryDequeue(out var current))
        {
            if (current.Id == end) return current.Path;
            foreach (ushort next in definition.Nodes[current.Id].Adjacent)
                if (seen.Add(next)) queue.Enqueue((next, [.. current.Path, next]));
        }
        throw new InvalidDataException($"No path to {end}");
    }

    static TreePacket PassivePacket(byte[] stream, int nodeCount)
    {
        int length = 22 + nodeCount * 2; // these scenarios have no selected ordinary mastery effects.
        Assert.True(stream.Length >= length);
        var packet = stream.AsSpan(stream.Length - length);
        Assert.Equal((ushort)0x33, BinaryPrimitives.ReadUInt16BigEndian(packet));
        Assert.Equal(nodeCount, BinaryPrimitives.ReadUInt16BigEndian(packet[12..]));
        var nodes = new ushort[nodeCount];
        for (int i = 0; i < nodeCount; i++) nodes[i] = BinaryPrimitives.ReadUInt16LittleEndian(packet[(14 + i * 2)..]);
        return new(BinaryPrimitives.ReadUInt16BigEndian(packet[2..]), BinaryPrimitives.ReadUInt16BigEndian(packet[4..]),
            BinaryPrimitives.ReadUInt16BigEndian(packet[6..]), packet[8], packet[9], nodes);
    }

    sealed record TreePacket(ushort RefundPoints, ushort QuestPoints, ushort AscendancyPoints, byte Level, byte ClassByte, ushort[] Nodes);

    static T ProgramValue<T>(string name, params object?[] args) => (T)typeof(Program)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;

    sealed partial class GatewayCharacter : IDisposable
    {
        static readonly MethodInfo Load = GatewayMethod("LoadProgress");
        static readonly MethodInfo ChangeMethod = GatewayMethod("ChangePassives");
        static readonly MethodInfo UseMethod = GatewayMethod("UseItem");
        static readonly MethodInfo SetLevelMethod = GatewayMethod("SetLevel");
        readonly int _classRow;
        readonly bool _god;
        readonly bool _hardcore;
        readonly bool _ruthless;
        public string Character { get; } = $"AscInt{Guid.NewGuid():N}"[..22];
        public ClientState State { get; }
        public byte[] LastBytes { get; private set; } = [];
        public string SavedJson => Saves.Get(GameServer.Storage.SaveDatabase.Table.Characters, Character)!;
        public PlayerInventories Items => PlayerInventories.For(Character, Program.Items);
        public AccountStash Stash => AccountStash.For(AccountStash.Key(State.Account ?? Character, State.League), Program.Items);

        static MethodInfo GatewayMethod(string name) => typeof(Program).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == name || m.Name.Contains($"g__{name}|", StringComparison.Ordinal));

        public GatewayCharacter(IReadOnlyList<string>? awards = null, IReadOnlyList<ushort>? nodes = null,
            int classRow = 4, byte baseClass = 4, string choice = "Aristocrat", ushort refundPoints = 20, int level = 1,
            bool god = false, AscendancyFamily family = AscendancyFamily.Phrecia,
            bool hardcore = false, bool ruthless = false)
        {
            TestSaves.Ensure();
            _classRow = classRow;
            _god = god;
            _hardcore = hardcore;
            _ruthless = ruthless;
            Assert.NotNull(Program.Stats.Key("active_skill_base_area_of_effect_radius"));
            var definition = Program.Ascendancies.Find(baseClass, family, choice)!;
            Assert.NotNull(definition);
            CharacterSaves.Write(Character, new([], Experience: CharacterStats.ExperienceFor(level), RefundPoints: refundPoints,
                MainAscendancy: new(definition.Identity, nodes ?? [], awards ?? AscendancyRules.AwardKeys)));
            State = Reload();
            Assert.Equal(level, State.Pools.Level);
        }

        public ClientState Reload()
        {
            var state = new ClientState(new(0, "AscendancyIntegration", 128, 128, 32, 32, null, []), 0, 1)
            { Character = Character, ClassRow = _classRow, God = _god, Hardcore = _hardcore, LeagueForm = _ruthless ? "ruthless" : null };
            try { Load.Invoke(null, [state, 1]); }
            catch (TargetInvocationException e) when (e.InnerException is not null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); }
            (state.PlayerLife, state.PlayerMana) = (state.Pools.Life, state.Pools.Mana);
            return state;
        }

        public Task<(string? Error, byte[] Bytes)> Dispatch(byte[] bytes) => Exchange(async (stream, cipher) =>
        {
            var messages = InstanceProtocol.SplitClientMessages(bytes, out var partial, out var unknown);
            Assert.Empty(partial);
            Assert.Empty(unknown);
            await Program.OnClientMessages("ascendancy-integration", stream, cipher, messages, unknown, State);
            return null;
        });

        public Task<(string? Error, byte[] Bytes)> Command(string line) => Exchange(async (stream, cipher) =>
        {
            State.Commands = ProgramValue<ChatCommands>("Commands", "ascendancy-integration", stream, cipher, State);
            Assert.True(await State.Commands.RunAsync(line));
            return null;
        });

        public Task<(string? Error, byte[] Bytes)> UseItem(uint itemId) => Exchange(async (stream, cipher) =>
        {
            await (Task)UseMethod.Invoke(null, ["ascendancy-integration", stream, cipher, State,
                new InstanceItems.UseItemRequest(PlayerInventories.InventoryId(PlayerInventories.MainInventory), itemId), "integration test"])!;
            return null;
        });

        public Task<(string? Error, byte[] Bytes)> Change(ushort[] nodes, bool allocate, bool refundPoints = false,
            ClientState? state = null, bool goldRespec = false, ushort? masteryEffect = null) => Exchange((stream, cipher) =>
            (Task<string?>)ChangeMethod.Invoke(null,
                ["ascendancy-integration", stream, cipher, state ?? State, nodes, allocate, "integration test", masteryEffect, goldRespec, refundPoints])!);

        public Task<(string? Error, byte[] Bytes)> SetLevel(int level) => Exchange(async (stream, cipher) =>
        {
            await (Task)SetLevelMethod.Invoke(null, ["ascendancy-integration", stream, cipher, State, level])!;
            return null;
        });

        async Task<(string? Error, byte[] Bytes)> Exchange(Func<NetworkStream, ConnectionCiphers, Task<string?>> work)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var client = new TcpClient();
            var accept = listener.AcceptTcpClientAsync();
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
            using var server = await accept;
            using var capture = new MemoryStream();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var reading = client.GetStream().CopyToAsync(capture, timeout.Token);
            byte[] key = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
            var serverCipher = new ConnectionCiphers(key, isClient: false);
            var clientCipher = new ConnectionCiphers(key, isClient: true);
            string? error;
            try
            {
                var task = work(server.GetStream(), serverCipher);
                error = await task.WaitAsync(timeout.Token);
            }
            finally
            {
                server.Client.Shutdown(SocketShutdown.Send);
                await reading;
                LastBytes = capture.ToArray();
                clientCipher.Receive.Process(LastBytes);
            }
            return (error, LastBytes);
        }

        public void Dispose()
        {
            PlayerInventories.Forget(Character);
            AccountStash.Forget(AccountStash.Key(State.Account ?? Character, State.League));
        }
    }
}
