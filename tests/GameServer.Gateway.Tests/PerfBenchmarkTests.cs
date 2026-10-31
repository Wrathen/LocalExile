using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using Xunit.Abstractions;

namespace GameServer.Gateway.Tests;

/// <summary>
/// The Gateway's repeatable measurements (Agents/docs/dev/performance.md): the first-player catalogue loads, an area
/// instance's creation (server level generation, layout, monster population) and a busy instance's monster AI with
/// its swings and projectiles. They only run with Game_PERF=1 (otherwise they are reported as skipped):
/// <c>$env:Game_PERF=1; dotnet test tests/GameServer.Gateway.Tests -c Release --filter FullyQualifiedName~PerfBenchmark</c>.
/// Results go to the test output and as JSON lines to artifacts/perf/bench.jsonl (Game_PERF_LABEL names the run).
/// The level generation needs Client/Content.ggpk and Node (Tools/ooz), as the Gateway's own.
/// </summary>
[Collection("PerfBenchmark")]
public class PerfBenchmarkTests(ITestOutputHelper output)
{
    static readonly string Label = Environment.GetEnvironmentVariable("Game_PERF_LABEL") ?? "run";

    void Record(string scenario, string metric, double value, string unit)
    {
        output.WriteLine($"{scenario,-24} {metric,-48} {value,12:0.###} {unit}");
        Directory.CreateDirectory(Path.Combine("artifacts", "perf"));
        File.AppendAllText(Path.Combine("artifacts", "perf", "bench.jsonl"), JsonSerializer.Serialize(new
        {
            label = Label, scenario, metric, value = Math.Round(value, 3), unit,
            at = DateTimeOffset.Now.ToString("O"),
        }) + "\n");
    }

    sealed class Wire : IDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        public readonly TcpClient Writer = new(), Reader;
        public readonly ConnectionCiphers Ciphers = new(new byte[64], isClient: false);
        readonly CancellationTokenSource stop = new();
        readonly Task draining;
        public long Received;

        public Wire()
        {
            listener.Start();
            var accepting = listener.AcceptTcpClientAsync();
            Writer.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            Reader = accepting.GetAwaiter().GetResult();
            // The client side reads everything, as a client does, so the server's sends never block on a full buffer.
            draining = Task.Run(async () =>
            {
                byte[] buffer = new byte[64 * 1024];
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        int n = await Reader.GetStream().ReadAsync(buffer, stop.Token);
                        if (n == 0) break;
                        Interlocked.Add(ref Received, n);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
            });
        }

        public NetworkStream Stream => Writer.GetStream();

        public void Dispose()
        {
            stop.Cancel();
            Writer.Dispose(); Reader.Dispose(); listener.Stop();
            try { draining.Wait(2000); } catch (AggregateException) { }
        }
    }

    // The Gateway's log as a server writes it (live.ps1 redirects stdout to a file; ServerLog stamps every line), so the
    // logging a scenario does costs what it costs in a server. Once per test process (perf runs only).
    static readonly Lazy<string> ConsoleLog = new(() =>
    {
        string path = Path.GetFullPath(Path.Combine("artifacts", "perf", $"console-{Environment.ProcessId}.log"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Console.SetOut(new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true });
        GameServer.Core.Diagnostics.ServerLog.Install("gateway");
        return path;
    });

    static long LogLines()
    {
        using var reader = new StreamReader(new FileStream(ConsoleLog.Value, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        long lines = 0;
        while (reader.ReadLine() is not null) lines++;
        return lines;
    }

    static ClientState Player(AreaLayout layout, uint seed)
    {
        TestSaves.Ensure();
        var conn = new ClientState(layout, 1, seed)
        {
            Character = "PerfBench" + Guid.NewGuid().ToString("N"), ClassRow = 1, PlayerSent = true, Instance = new(),
        };
        Program.ConnectionMetrics.Add(new ConnectionCiphers(new byte[64], false), conn.Performance);
        // Nothing the monsters do ends the measurement: a large life pool, and no death screen.
        conn.Pools = Program.PoolsFor(conn, 40) with { Life = 100_000_000, Mana = 1000 };
        conn.PlayerLife = conn.Pools.Life;
        conn.PlayerMana = 1000;
        return conn;
    }

    static Task PopulateArea(Wire wire, ClientState conn) => (Task)typeof(Program)
        .GetMethod("PopulateArea", BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, ["perf", wire.Stream, wire.Ciphers, conn])!;

    static long Allocated() => GC.GetTotalAllocatedBytes(precise: false);

    static double PrivateMegabytes()
    {
        using var self = Process.GetCurrentProcess();
        return self.PrivateMemorySize64 / 1048576.0;
    }

    // What the first player in a fresh Gateway loads: the catalogues behind an item, a skill, a stat key, a monster
    // and an area (each is loaded once per process, so the order matters and is fixed).
    [PerfFact]
    public void Catalogue_loads()
    {
        var steps = new (string Name, Action Load)[]
        {
            ("Program statics", () => _ = Program.Items),
            ("Stats", () => Program.Stats.Key("base_maximum_life")),
            ("Items (BaseItemTypes)", () => Program.Items.Find("Iron Ring")),
            ("Items.Mods (Mods.json)", () => _ = Program.Items.Mods.All.Count),
            ("Skills", () => Program.Skills.RowOfClass(1)),
            // The ActiveSkillType sets (instant, attack, aura …) a player's and the monsters' skills ask for.
            ("Skill type sets", () =>
            {
                var skills = new SkillCatalog();
                _ = skills.IsInstant(0) | skills.IsMovement(0) | skills.IsRangedAttack(0) | skills.IsSpell(0) | skills.IsWarcry(0)
                    | skills.IsAura(0, [0]) | skills.IsBrand(0);
            }),
            ("Monsters", () => Program.Monsters.Find("Metadata/Monsters/Zombies/ZombieBiteRaise")),
            ("Packs", () => Program.Packs.For("1_1_2")),
            ("Area transitions", () => AreaTransitionCatalog.HashOf("1_1_2")),
            ("Waypoints", () => _ = WaypointCatalog.Areas),
            ("Passive tree", () => Program.Passives.Find(0)),
        };
        double total = 0;
        foreach (var (name, load) in steps)
        {
            long live = GC.GetTotalMemory(forceFullCollection: true);
            long before = Allocated();
            var clock = Stopwatch.StartNew();
            load();
            total += clock.Elapsed.TotalMilliseconds;
            Record("catalogue", $"{name} ms", clock.Elapsed.TotalMilliseconds, "ms");
            Record("catalogue", $"{name} allocated MB", (Allocated() - before) / 1048576.0, "MB");
            Record("catalogue", $"{name} retained MB", (GC.GetTotalMemory(forceFullCollection: true) - live) / 1048576.0, "MB");
        }
        Record("catalogue", "total ms", total, "ms");
        if (typeof(Program).Assembly.GetType("GameTables") is { } tables
            && tables.GetProperty("Counts")?.GetValue(null) is ValueTuple<long, long> counts)
        {
            Record("catalogue", "shared table parses", counts.Item1, "");
            Record("catalogue", "shared table reuses", counts.Item2, "");
        }
        // A per-gem question the skill panel and every hit ask (three type sets per call).
        {
            const int Calls = 1_000_000;
            bool any = false;
            long before = Allocated();
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < Calls; i++) any |= Program.Skills.IsRangedAttack(i & 0x3ff);
            Record("catalogue", "IsRangedAttack ns per call", clock.Elapsed.TotalMilliseconds * 1e6 / Calls, "ns");
            Record("catalogue", "IsRangedAttack bytes per call", (Allocated() - before) / (double)Calls, "B");
            GC.KeepAlive(any);
        }
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        Record("catalogue", "live heap after full GC MB", GC.GetTotalMemory(false) / 1048576.0, "MB");
        Record("catalogue", "process private MB", PrivateMegabytes(), "MB");
    }

    // An area instance as a player's entry creates it: the layout (generated by the server for a supported area, at a
    // seed nobody asked for before), then the area's monster packs placed and created over a connection.
    [PerfFact]
    public async Task Area_instance_creation()
    {
        _ = ConsoleLog.Value;
        string[] areas = ["1_1_2", "1_1_3", "1_1_4_1", "1_1_5", "1_1_7_1", "1_2_1"];
        // Game_PERF_MORE_AREAS=N: then the layouts of N more supported areas (no monsters), as a long session visits
        // them, for what the caches grow to.
        int more = int.TryParse(Environment.GetEnvironmentVariable("Game_PERF_MORE_AREAS"), out int n) ? n : 0;
        if (more > 0)
            areas = [.. areas, .. ServerLevels.LoadSupported(ServerLevels.BaselineFile).Except(areas).Order(StringComparer.Ordinal)
                .Where(a => AreaTransitionCatalog.HashOf(a) is not null).Take(more)];
        bool first = true;
        AreaLayout? held = null;   // as a connection holds its layout while the others are generated
        foreach (string id in areas)
        {
            if (AreaTransitionCatalog.HashOf(id) is not ushort hash || !ServerLevels.IsSupported(id)) continue;
            if (Array.IndexOf(areas, id) >= 6)
            {
                var timer = Stopwatch.StartNew();
                AreaLayouts.For(hash, (uint)(1000 + Array.IndexOf(areas, id)));
                Record("area", "more: layout ms", timer.Elapsed.TotalMilliseconds, "ms");
                continue;
            }
            // A seed per area and run that no earlier generation cached.
            uint seed = (uint)(1000 + Array.IndexOf(areas, id));
            long live = GC.GetTotalMemory(forceFullCollection: true);
            long before = Allocated();
            var clock = Stopwatch.StartNew();
            var layout = AreaLayouts.For(hash, seed);
            double layoutMs = clock.Elapsed.TotalMilliseconds;
            held ??= layout;
            Record("area", $"{id} layout{(first ? " (first: opens the game files)" : "")} ms", layoutMs, "ms");
            Record("area", $"{id} layout allocated MB", (Allocated() - before) / 1048576.0, "MB");
            Record("area", $"{id} layout retained MB", (GC.GetTotalMemory(forceFullCollection: true) - live) / 1048576.0, "MB");
            first = false;
            using var wire = new Wire();
            var conn = Player(layout, seed);
            before = Allocated();
            clock.Restart();
            await PopulateArea(wire, conn);
            Record("area", $"{id} populate ms", clock.Elapsed.TotalMilliseconds, "ms");
            Record("area", $"{id} populate allocated MB", (Allocated() - before) / 1048576.0, "MB");
            Record("area", $"{id} monsters", conn.Monsters.Count, "");
            conn.Closed = true;
        }
        // What the Gateway keeps afterwards: the generated levels (ServerLevels keeps the last few), the game files'
        // caches and the catalogues.
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        Record("area", "live heap after full GC MB", GC.GetTotalMemory(false) / 1048576.0, "MB");
        Record("area", "process private MB", PrivateMegabytes(), "MB");
        if (typeof(ServerLevels).GetField("_fs", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null) is { } fs)
        {
            foreach (string field in new[] { "_cacheBytes", "_chunkBytes" })
                if (fs.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance) is { } f)
                    Record("area", $"game files {field} MB", (long)f.GetValue(fs)! / 1048576.0, "MB");
            if (fs.GetType().GetField("_bundleReads", BindingFlags.NonPublic | BindingFlags.Instance) is { } reads)
                Record("area", "game files bundle reads", (long)reads.GetValue(fs)!, "");
        }
        // The first layout's level, held all along: still there, not generated again (1 = the same level).
        if (held?.Level is { } level)
        {
            var timer = Stopwatch.StartNew();
            bool same = ReferenceEquals(ServerLevels.For(held.Id, 1000u), level);
            Record("area", "held level kept", same ? 1 : 0, "");
            Record("area", "held level lookup ms", timer.Elapsed.TotalMilliseconds, "ms");
            // What a projectile step's grid and height lookups pay per call (ProjectileGrid, TerrainHeights).
            const int Calls = 1_000_000;
            timer.Restart();
            for (int i = 0; i < Calls; i++) ProjectileGrid.Blocks(held.Id, 1000u, i & 0xff, 100);
            Record("area", "ProjectileGrid.Blocks ns per call (generated level)", timer.Elapsed.TotalMilliseconds * 1e6 / Calls, "ns");
            timer.Restart();
            for (int i = 0; i < Calls; i++) TerrainHeights.At(held.Id, 1000u, i & 0xff, 100);
            Record("area", "TerrainHeights.At ns per call (generated level)", timer.Elapsed.TotalMilliseconds * 1e6 / Calls, "ns");
        }
        // The generated levels ServerLevels keeps for the next entry (their grids; the doodad lists are small beside them).
        if (typeof(ServerLevels).GetField("Generated", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
            is System.Collections.IDictionary kept)
        {
            long bytes = kept.Values.Cast<object>().Select(v => v.GetType().GetField("Level")?.GetValue(v)).OfType<ServerLevels.Level>()
                .Sum(l => (long)l.Walk.Length + l.Heights.Length * 2L + l.Projectile.Length);
            Record("area", "kept levels", kept.Count, "");
            Record("area", "kept levels' grids MB", bytes / 1048576.0, "MB");
        }
        if (typeof(Program).Assembly.GetType("GameTables") is { } tables
            && tables.GetProperty("Counts")?.GetValue(null) is ValueTuple<long, long> counts)
        {
            Record("area", "shared table parses", counts.Item1, "");
            Record("area", "shared table reuses", counts.Item2, "");
        }
        await Hold("area");
    }

    // Game_PERF_HOLD=1: the test waits (up to 2 minutes) with its state alive until artifacts/perf/<name>.release
    // exists, after writing its process id to artifacts/perf/<name>.pid, for `dotnet-gcdump collect -p <pid>`.
    static async Task Hold(string name)
    {
        if (Environment.GetEnvironmentVariable("Game_PERF_HOLD") != "1") return;
        string folder = Path.Combine("artifacts", "perf"), release = Path.Combine(folder, name + ".release");
        File.Delete(release);
        File.WriteAllText(Path.Combine(folder, name + ".pid"), Environment.ProcessId.ToString());
        for (var clock = Stopwatch.StartNew(); !File.Exists(release) && clock.Elapsed < TimeSpan.FromMinutes(2);) await Task.Delay(200);
    }

    // A busy instance, the same in every run: the kinds of the area's scattered packs (and a Sand Spitter, for
    // projectiles), Crowd of them around a player who keeps walking, so they chase, surround, swing and shoot, and Idle
    // more farther away, as the rest of an area's population. (PopulateArea is not used: its placement is seeded with
    // the process's randomized HashCode.)
    async Task<(Wire Wire, ClientState Conn, (int X, int Y)[] Path)> BusyInstance()
    {
        _ = ConsoleLog.Value;
        var wire = new Wire();
        const string id = "1_1_2";
        const int Crowd = 60, Idle = 140;
        ushort hash = AreaTransitionCatalog.HashOf(id)!.Value;
        var layout = AreaLayouts.For(hash, 1);
        var conn = Player(layout, 1);
        var kinds = Program.Packs.For(id)!.Scattered.SelectMany(p => p.Pack.Entries).Select(e => Program.Monsters.Find(e.Variety.ToString()))
            .OfType<MonsterCatalog.Variety>().Where(v => v.Supported).DistinctBy(v => v.Row)
            // Monsters that wait buried rise on a wall-clock watch (MonsterEmerge): left out, so every run is the same.
            .Where(v => Program.Emerges.Kind(v, MonsterRarity.Normal) == EmergeKind.None).ToList();
        if (Program.Monsters.Find("Metadata/Monsters/SandSpitters/SandSpitterUntainted") is { Supported: true } spitter
            && Program.Emerges.Kind(spitter, MonsterRarity.Normal) == EmergeKind.None) kinds.Add(spitter);
        Assert.NotEmpty(kinds);
        var random = new Random(7);
        var (px, py) = (layout.SpawnX, layout.SpawnY);
        int placed = 0;
        for (int attempt = 0; placed < Crowd + Idle && attempt < (Crowd + Idle) * 50; attempt++)
        {
            int spread = placed < Crowd ? 40 : 400;
            int x = px + random.Next(-spread, spread + 1), y = py + random.Next(-spread, spread + 1);
            if (placed >= Crowd && Math.Max(Math.Abs(x - px), Math.Abs(y - py)) < 150) continue;
            if (!layout.WalkableFor(x, y, 2)) continue;
            await Program.SpawnMonster("perf", wire.Stream, wire.Ciphers, conn, kinds[placed % kinds.Count], 5,
                MonsterRarity.Normal, x, y, log: false);
            placed++;
        }
        Record("busy", "monsters", conn.Monsters.Count, "");
        Record("busy", "kinds", kinds.Count, "");

        // The player walks between points around the spawn, as a player kiting the crowd.
        var path = new[] { (px + 12, py), (px, py + 12), (px - 12, py), (px, py - 12) }
            .Where(p => layout.WalkableFor(p.Item1, p.Item2, 0)).DefaultIfEmpty((px, py)).ToArray();
        return (wire, conn, path);
    }

    // The real AI loop runs over the busy instance (RunMonsterAi, its swings, contacts and projectiles, every packet
    // encrypted and sent); the measure is the process CPU it costs per second of game time.
    [PerfFact]
    public async Task Busy_instance_ticks()
    {
        const double Seconds = 10;
        var (wire, conn, path) = await BusyInstance();
        using var _ = wire;
        using var stop = new CancellationTokenSource();
        var walking = Task.Run(async () =>
        {
            for (int i = 0; !stop.IsCancellationRequested; i++)
            {
                var (x, y) = path[i % path.Length];
                lock (conn.Monsters) conn.Walk.Order(x, y, conn.Clock.Elapsed.TotalSeconds);
                await Task.Delay(700);
            }
        });
        using var self = Process.GetCurrentProcess();
        var ai = Program.RunMonsterAi("perf", wire.Stream, wire.Ciphers, conn);
        // A warm-up first: the JIT's tier-1 recompilation of the hot methods is a one-time cost of a fresh process,
        // not of a running server's instance.
        await Task.Delay(TimeSpan.FromSeconds(5));
        self.Refresh();
        var cpuBefore = self.TotalProcessorTime;
        long allocatedBefore = Allocated();
        int gen0 = GC.CollectionCount(0), gen2 = GC.CollectionCount(2);
        long sentBefore = Interlocked.Read(ref wire.Received);
        long linesBefore = LogLines();
        var clock = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(Seconds));
        double elapsed = clock.Elapsed.TotalSeconds;
        self.Refresh();
        double cpuMs = (self.TotalProcessorTime - cpuBefore).TotalMilliseconds;
        lock (conn.Monsters) conn.Closed = true;
        stop.Cancel();
        await ai.WaitAsync(TimeSpan.FromSeconds(10));
        await walking;
        Record("busy", "process CPU ms per game second", cpuMs / elapsed, "ms/s");
        Record("busy", "allocated MB per game second", (Allocated() - allocatedBefore) / 1048576.0 / elapsed, "MB/s");
        Record("busy", "gen0 GCs per game second", (GC.CollectionCount(0) - gen0) / elapsed, "/s");
        Record("busy", "gen2 GCs", GC.CollectionCount(2) - gen2, "");
        Record("busy", "bytes sent per game second", (Interlocked.Read(ref wire.Received) - sentBefore) / elapsed, "B/s");
        Record("busy", "aggro monsters", conn.Monsters.Values.Count(m => m.Aggro), "");
        Record("busy", "log lines per game second", (LogLines() - linesBefore) / elapsed, "/s");
    }

    // The planner alone, single-threaded, over the same crowd: one decision pass per AI step (no sends).
    [PerfFact]
    public async Task Monster_planner()
    {
        var (wire, conn, path) = await BusyInstance();
        using var _ = wire;
        var clock = new Stopwatch();
        var cadence = new MonsterAiCadence();
        const int Steps = 2000;
        double now = conn.Clock.Elapsed.TotalSeconds;
        for (int i = 0; i < 50; i++) Program.PlanMonsterAi(conn, cadence, now += 0.05);   // warm-up
        var surroundBefore = conn.Performance.Read(LatencyPhase.Surround);
        var blockersBefore = conn.Performance.Read(LatencyPhase.BlockerBuild);
        long planAllocated = GC.GetAllocatedBytesForCurrentThread();
        using var self = Process.GetCurrentProcess();
        var cpuBefore = self.TotalProcessorTime;
        clock.Restart();
        int moves = 0;
        for (int i = 0; i < Steps; i++)
        {
            if (i % 14 == 0) { var (x, y) = path[i / 14 % path.Length]; lock (conn.Monsters) conn.Walk.Order(x, y, now); }
            var plan = Program.PlanMonsterAi(conn, cadence, now += 0.05);
            moves += plan.Moves.Count;
            // Swings complete at once here: the planner keeps reserving them.
            lock (conn.Monsters) foreach (var (_, m, action) in plan.Swings) cadence.CompleteSwing(m, action);
        }
        Record("planner", "PlanMonsterAi µs per step", clock.Elapsed.TotalMilliseconds * 1000 / Steps, "µs");
        self.Refresh();
        // Less disturbed by other processes on a shared machine than the wall clock.
        Record("planner", "PlanMonsterAi process CPU µs per step", (self.TotalProcessorTime - cpuBefore).TotalMilliseconds * 1000 / Steps, "µs");
        Record("planner", "PlanMonsterAi allocated KB per step", (GC.GetAllocatedBytesForCurrentThread() - planAllocated) / 1024.0 / Steps, "KB");
        Record("planner", "PlanMonsterAi moves per step", moves / (double)Steps, "");
        // The planner's phases (ConnectionPerformance): the surround's slot search and the blocker snapshot.
        Record("planner", "MonsterSurround.Assign µs per step",
            (conn.Performance.Read(LatencyPhase.Surround).TotalMilliseconds - surroundBefore.TotalMilliseconds) * 1000 / Steps, "µs");
        Record("planner", "blocker frame µs per step",
            (conn.Performance.Read(LatencyPhase.BlockerBuild).TotalMilliseconds - blockersBefore.TotalMilliseconds) * 1000 / Steps, "µs");
        conn.Closed = true;
    }
}

/// <summary>A benchmark: skipped unless Game_PERF=1.</summary>
public sealed class PerfFactAttribute : FactAttribute
{
    public PerfFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("Game_PERF") != "1") Skip = "a benchmark: set Game_PERF=1 to run it";
    }
}

[CollectionDefinition("PerfBenchmark", DisableParallelization = true)]
public class PerfBenchmarkCollection;
