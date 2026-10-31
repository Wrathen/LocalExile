using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using GameServer.Data.Ggpk;
using GameServer.Data.Terrain.Generation;
using GgDump;

/// <summary>Server-side level generation (GameServer.Data/Terrain/Generation, Agents/docs/re/011-level-generation.md):
/// for an area the port reproduces 1:1, the Gateway generates the layout itself from the seed instead of reading the
/// client's capture: the walkability grid (doodad collision included), every cell's ground height, and the area
/// transitions and waypoint. Supported areas are the ones whose seed-1 trace passes every check of the level
/// generator's gate (scripts/re/levelgen/gate-baseline.json, committed with each generator change) and whose logged
/// loads all match. Needs the game files (Client/Content.ggpk) and Node for their Oodle blocks (Tools/ooz);
/// Game_SERVER_LEVELS=0 turns it off.</summary>
static class ServerLevels
{
    public const string UnsupportedMessage = "This map is generated on client-side and is not supported yet by the Server.";

    /// <summary>A generated layout: Width × Height cells, one walk value (0 … 5, walkable from 2) and one ground height
    /// (× 7.8125 for world units) per cell, the tile objects the server creates (area transitions, the waypoint), and
    /// the client's doodads and placed .arm rooms (what the recorder's .doodads.json and .rooms.json hold: the rooms'
    /// monster points and quest objects are fitted to them), the quest tile objects (its .objects.json) and the
    /// projectile grid (its .projectile.bin: 0 stops projectiles).</summary>
    public sealed record Level(int Width, int Height, byte[] Walk, short[] Heights, IReadOnlyList<PlacedTileObject> Objects,
        IReadOnlyList<PlacedDoodad> Doodads, IReadOnlyList<PlacedArmRoom> Rooms, IReadOnlyList<PlacedTileObject> QuestObjects,
        byte[] Projectile)
    {
        /// <summary>The client's tile hash of the generated grid, and how long the generation took.</summary>
        public uint TileHash { get; init; }
        public long Milliseconds { get; init; }
    }

    /// <summary>Where the supported list comes from (the repository root is the Gateway's directory; tests replace it).</summary>
    public static string BaselineFile { get; set; } = GameServer.Core.World.ServerLevelAreas.BaselineFile;

    static readonly bool Enabled = Environment.GetEnvironmentVariable("Game_SERVER_LEVELS") != "0";
    static readonly object Gate = new();
    static HashSet<string>? _supported;
    static TerrainCache? _files;
    static GameFileSystem? _fs;
    static readonly ConcurrentDictionary<(string Area, uint Seed, int Level), Level?> Generated = new();
    // The order they were generated in: with random seeds every new instance is a new (area, seed), so only the last
    // Kept stay (a level is several MB); an older one is generated again when asked for.
    static readonly Queue<(string Area, uint Seed, int Level)> Order = new();
    const int Kept = 24;
    // Why the last generations that failed did (the exception in full), for the capture tour's records.
    static readonly ConcurrentDictionary<(string Area, uint Seed, int Level), string> Errors = new();

    /// <summary>The exception of a failed generation of (area, seed), while it is among the last ones asked for.</summary>
    public static string? Error(string areaId, uint seed) => Errors.GetValueOrDefault((areaId, seed, 0));

    /// <summary>Whether the Gateway generates this area itself (<see cref="BaselineFile"/>).</summary>
    public static bool IsSupported(string areaId)
    {
        if (!Enabled) return false;
        lock (Gate) return (_supported ??= LoadSupported(BaselineFile)).Contains(areaId);
    }

    /// <summary>Drops the supported list, so the next check reads <see cref="BaselineFile"/> again (tests).</summary>
    internal static void Reload() { lock (Gate) _supported = null; }

    /// <summary>The supported areas of a gate baseline (<see cref="GameServer.Core.World.ServerLevelAreas.Load"/>).</summary>
    internal static HashSet<string> LoadSupported(string file) =>
        GameServer.Core.World.ServerLevelAreas.Load(file, m => Console.WriteLine($"[gateway] server levels: {m}"));

    /// <summary>The generated layout of (area, seed), or null when the area is not supported or generation failed
    /// (the Gateway then falls back to the capture). Generated once per (area, seed) and kept while among the last
    /// <see cref="Kept"/>.</summary>
    public static Level? For(string areaId, uint seed, int? level = null)
    {
        if (!IsSupported(areaId)) return null;
        int keyLevel = level ?? 0;
        if (Generated.TryGetValue((areaId, seed, keyLevel), out var known)) return known;
        lock (Gate)
        {
            if (Generated.TryGetValue((areaId, seed, keyLevel), out known)) return known;
            var generated = Generated[(areaId, seed, keyLevel)] = Generate(areaId, seed, level);
            Order.Enqueue((areaId, seed, keyLevel));
            while (Order.Count > Kept)
            {
                var old = Order.Dequeue();
                Generated.TryRemove(old, out _);
                Errors.TryRemove(old, out _);
            }
            return generated;
        }
    }

    /// <summary>The seed an instance of a supported area is played at: its own, unless the port cannot generate it (a
    /// few random seeds per area, e.g. 1_1_2 at 32124476: "Mismatching ground types"), then the first of
    /// <see cref="Replacements"/> it can. Without a level the Gateway would have no walk map, area transitions or
    /// waypoint while the client still builds the map. The replacement depends only on the seed, so everyone entering
    /// the instance gets the same one. Kept as it is for the capture tour (<see cref="AreaLayouts.Recording"/>) and
    /// Game_SEED, which ask for that seed's trace.</summary>
    public static uint PlayableSeed(string areaId, uint seed)
    {
        if (seed == 1 || AreaLayouts.Recording || Environment.GetEnvironmentVariable("Game_SEED") is { Length: > 0 }
            || !IsSupported(areaId) || For(areaId, seed) is not null) return seed;
        foreach (uint other in Replacements(seed))
            if (For(areaId, other) is not null)
            {
                Console.WriteLine($"[gateway] server levels: {areaId} seed {seed} not generated; played at seed {other}");
                return other;
            }
        return seed;
    }

    /// <summary>The seeds tried in place of one the port cannot generate, in order (1 … int.MaxValue, as Auth's).</summary>
    internal static IEnumerable<uint> Replacements(uint seed)
    {
        for (uint k = 1; k <= 8; k++)
        {
            uint other = unchecked(seed * 0x9e3779b1u + k * 0x85ebca77u) & 0x7fffffff;
            yield return other is 0 or 1 ? k + 1 : other;
        }
    }

    /// <summary>The generated layout of (area, seed) standing in for a capture missing from <paramref name="folder"/>:
    /// only for the Gateway's own capture folder (data/walkability), never for a test's.</summary>
    public static Level? InPlaceOf(string folder, string areaId, uint seed) =>
        Path.GetFullPath(folder) == Path.GetFullPath("data/walkability") ? For(areaId, seed) : null;

    static Level? Generate(string areaId, uint seed, int? level = null)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var files = Files();
            var area = AreaInfo.Load(Read, areaId, level ?? AreaLevel(areaId));
            var result = LevelGenerator.Generate(files, area, seed);
            var walk = Walkability.Build(result.Grid, t => files.Walk(t.Path));
            // The doodad stage stamps the doodads' collision into the grids (re/011 §6c).
            var doodads = new DoodadStage(files, result.Random, result.Grid, walk, []);
            doodads.Run(result.Records);
            var heights = CellHeights.Build(result.Grid, t => files.Walk(t.Path));
            var objects = AreaObjects.Transitions(files, result);
            // A room's waypoint line when no tile places one (The Ledge).
            if (!objects.Any(o => o.Type == GameServer.Core.Protocol.Instance.WaypointObject.MetadataPath)) objects.AddRange(AreaObjects.RoomWaypoints(result));
            Console.WriteLine($"[gateway] server levels: generated {areaId} seed {seed} ({walk.Width}x{walk.Height} cells, " +
                $"{objects.Count} tile object(s), {doodads.Placed.Count} doodads) in {clock.ElapsedMilliseconds} ms");
            ConnectionPerformance.Server("LevelGen", clock.Elapsed, areaId);
            return new Level(walk.Width, walk.Height, walk.Walk, heights, objects, doodads.Placed, AreaObjects.Rooms(result),
                AreaObjects.QuestObjects(files, result), walk.Projectile) { TileHash = result.Grid.Hash(), Milliseconds = clock.ElapsedMilliseconds };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[gateway] server levels: {areaId} seed {seed} not generated ({ex.GetType().Name}: {ex.Message.Split('\n')[0]})");
            Errors[(areaId, seed, level ?? 0)] = ex.ToString();
            return null;
        }
    }

    // Opened on the first generation and kept.
    static TerrainCache Files()
    {
        if (_files is not null) return _files;
        var oodle = new NodeOodleDecoder("node", Path.GetFullPath(Path.Combine("Tools", "ooz", "ooz_worker.mjs")));
        _fs = GameFileSystem.Open(Path.GetFullPath(Path.Combine("Client", "Content.ggpk")), oodle);
        return _files = new TerrainCache(Read);
    }

    static byte[]? Read(string path) => _fs!.TryReadFile(path, out var d) ? d : null;

    static readonly Lazy<Dictionary<string, int>> Levels = new(() =>
    {
        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine("data", "tables", "WorldAreas.json")));
            foreach (var row in doc.RootElement.EnumerateArray())
                levels[row.GetProperty("Id").GetString() ?? ""] = row.GetProperty("AreaLevel").GetInt32();
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or KeyNotFoundException) { }
        return levels;
    });

    // The WorldAreas row's area level (the generator's AreaInfo reads level-dependent rows); 1 without the table.
    static int AreaLevel(string areaId) => Math.Max(1, Levels.Value.GetValueOrDefault(areaId, 1));
}
