using System.Text.Json;
using GameServer.Core.Protocol.Instance;
using SaveFiles = GameServer.Core.Saves.SaveFiles;

/// <summary>What the Gateway knows about an area's layout: the terrain grid (for movement estimates), where the player
/// appears and the area transitions. Towns are fixed; other areas are generated from the seed, so their walkability only exists as
/// a dump of the client's walkability grid per (area, seed).</summary>
sealed record AreaLayout(ushort Hash, string Id, int GridW, int GridH, int SpawnX, int SpawnY,
    Func<int, int, bool>? Walkable, (uint Id, AreaTransitionObject Transition)[] AreaTransitions)
{
    /// <summary>The area's waypoint (WaypointCatalog), if it has one and its place is known.</summary>
    public WaypointObject? Waypoint { get; init; }
    /// <summary>The area transitions' effects (<see cref="AreaTransitionCatalog.Effects"/>).</summary>
    public (uint Id, TransitionEffect Effect)[] Effects { get; init; } = [];
    public Func<int, int, int, bool>? Clearance { get; init; }

    /// <summary>Every cell's ground height, row by row (GridW × GridH), when the Gateway generated the layout itself
    /// (<see cref="ServerLevels"/>); null for a capture.</summary>
    public short[]? CellHeights { get; init; }

    /// <summary>The level the Gateway generated this layout from (<see cref="ServerLevels"/>); holding it keeps it
    /// available to the projectile and height lookups for as long as a connection plays the layout.</summary>
    public ServerLevels.Level? Level { get; init; }

    /// <summary>Whether the Gateway generated this layout from the seed (<see cref="ServerLevels"/>).</summary>
    public bool ServerGenerated => CellHeights is not null;

    /// <summary>The ground height at a cell in world units (the client's 0x141d84be0 at the cell's centre: the cell's
    /// height × 7.8125), or null without heights or outside the grid.</summary>
    public float? GroundHeight(int x, int y) =>
        CellHeights is { } h && x >= 0 && y >= 0 && x < GridW && y < GridH ? h[y * GridW + x] * 7.8125f : null;
    public bool WalkableFor(int x, int y, int size) => Clearance?.Invoke(x, y, size)
        ?? (Walkable?.Invoke(x, y) != false);

    public string Describe() =>
        $"area {Id} (0x{Hash:x4}) grid {GridW}x{GridH} spawn ({SpawnX}, {SpawnY}), {(Walkable is null ? "no walkability" : "walkability")}, {AreaTransitions.Length} area transition(s)" +
        (Waypoint is { } w ? $", waypoint at ({w.GridX}, {w.GridY})" : "");

    // One-way entries have no exit back to stand next to: the arrival is the layout's named entrance point
    // (08-area-transitions.md §3). Lioneye's Watch from The Twilight Strand: "entrance1" (disabled in beachtown.dgr).
    static readonly Dictionary<(ushort Area, ushort From), (int X, int Y)> OneWayArrivals = new()
    {
        [(AreaLayouts.LioneyesWatch, 0xf00a /* 1_1_1 The Twilight Strand */)] = (123, 308),
    };

    /// <summary>Where a player entering this area appears: next to the exit leading back to <paramref name="cameFrom"/>
    /// (the area they left through an exit), else the spawn (logins, Resurrect in Town, no exit back).</summary>
    public (int X, int Y) Arrival(ushort? cameFrom)
    {
        if (cameFrom is not { } from) return (SpawnX, SpawnY);
        if (OneWayArrivals.TryGetValue((Hash, from), out var point)) return point;
        foreach (var (_, t) in AreaTransitions)
            if (t.DestinationHash16 == from) return Near((t.GridX, t.GridY));
        return (SpawnX, SpawnY);
    }

    /// <summary>Where "Resurrect at Checkpoint" revives: next to the area's waypoint once the character has activated it
    /// (waypoints are checkpoints), else where the player entered the area (the spawn, set to the arrival on entry).</summary>
    public (int X, int Y) Checkpoint(bool waypointActivated) =>
        waypointActivated && Waypoint is { } w ? Near((w.GridX, w.GridY)) : (SpawnX, SpawnY);

    /// <summary>A walkable cell a few cells from (x, y), nearest first; (x + 8, y) without a walk map.</summary>
    public (int X, int Y) Near((int X, int Y) at)
    {
        if (Walkable is not { } walkable) return (at.X + 8, at.Y);
        for (int r = 6; r <= 20; r++)
            for (int dy = -r; dy <= r; dy++)
                foreach (int dx in new[] { -r, r })
                {
                    if (walkable(at.X + dx, at.Y + dy)) return (at.X + dx, at.Y + dy);
                    if (walkable(at.X + dy, at.Y + dx)) return (at.X + dy, at.Y + dx);
                }
        return at;
    }
}

static class AreaLayouts
{
    public const ushort LioneyesWatch = 0x235c, TheCoast = 0x7ad2;

    /// <summary>The area each transfer's destination ticket left through an exit; the new connection arrives next to the
    /// exit back (<see cref="AreaLayout.Arrival"/>). In memory: a Gateway restart drops the client to the login anyway.</summary>
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, ushort> CameFrom = new();

    // Lioneye's Watch is one room, Metadata/Terrain/Act1/Town/Rooms/beach_town_unique_normal.arm (18 x 30 tiles), placed
    // R270 by Graphs/beachtown.dgr ("default" "entrance2" "entrance1disabled"). Spawn (197, 261): the walkable cell farthest
    // from any edge (Agents/docs/protocol/03 §7).
    // Area transitions come from AreaTransitionCatalog (the atlas, or the client's generated layout captured by
    // scripts/walkmaps). Without the atlas the town still gets its gate to The Coast: the town tile's .tmd record
    // beachencampment_v01_01_area_transition2 as the client places it (grid (388, 231), 3π/2, z −351.04) with its
    // virtual .ao (the arch highlighted on hover). Agents/docs/protocol/08-area-transitions.md §5.
    static readonly (uint Id, AreaTransitionObject Transition)[] TownFallback =
        [(AreaTransitionCatalog.FirstId, new AreaTransitionObject(GridX: 388, GridY: 231, DestinationHash16: TheCoast,
            Model: "Metadata/Terrain/Beach/Town_v2/beachencampment_v01_01_area_transition2.ao",
            Rotation: 3 * MathF.PI / 2, Height: -351.04f))];

    static readonly Dictionary<ushort, string> Ids = new() { [LioneyesWatch] = "1_1_town", [TheCoast] = "1_1_2" };

    const string Folder = "data/walkability";

    static string IdOf(ushort hash) =>
        Ids.GetValueOrDefault(hash) ?? AreaTransitionCatalog.IdOf(hash) ?? HideoutCatalog.ByArea(hash)?.AreaId ?? $"0x{hash:x4}";

    /// <summary>The seed the instance's layout is generated and played at (<see cref="ServerLevels.PlayableSeed"/>).</summary>
    public static uint PlayableSeed(ushort hash, uint seed) => ServerLevels.PlayableSeed(IdOf(hash), seed);

    public static AreaLayout For(ushort hash, uint seed)
    {
        var hideout = HideoutCatalog.ByArea(hash);
        string id = IdOf(hash);
        // A hideout's waypoint is one of its decoration's doodads (21-hideouts.md §2), not a tile object.
        var layout = WithoutWaypoint(hash, id, seed);
        var waypoint = hideout is null ? WaypointCatalog.For(hash, id, seed) : HideoutCatalog.Waypoint(hideout);
        // League hubs whose tiles place no waypoint (the Rogue Harbour, the Monastery of the Keepers): one by the arrival,
        // so the player can travel back (the Waypoint type's own model).
        if (waypoint is null && id is WaypointCatalog.HeistHub or WaypointCatalog.ChayulaHub) waypoint = new WaypointObject(layout.SpawnX, layout.SpawnY - 12, Active: 1);
        return layout with { Waypoint = waypoint };
    }

    static AreaLayout WithoutWaypoint(ushort hash, string id, uint seed)
    {
        // An area the port reproduces is generated here from the seed; any other needs the client's capture.
        if (ServerLevels.For(id, seed) is { } generated)
            return WithCells(hash, id, seed, generated.Walk, generated.Width, generated.Height) with { CellHeights = generated.Heights, Level = generated };
        byte[]? cells = LoadCells(id, seed, out int w, out int h);
        return WithCells(hash, id, seed, cells, w, h);
    }

    static AreaLayout WithCells(ushort hash, string id, uint seed, byte[]? cells, int w, int h)
    {
        var layout = WithTransitions(hash, id, seed, cells, w, h);
        return layout with { Effects = AreaTransitionCatalog.Effects(id, seed) };
    }

    static AreaLayout WithTransitions(ushort hash, string id, uint seed, byte[]? cells, int w, int h)
    {
        var notes = new List<string>();
        var transitions = AreaTransitionCatalog.For(hash, id, seed, notes);
        foreach (var n in notes) Console.WriteLine($"[gateway] area {id} seed {seed}: {n}");
        if (hash == LioneyesWatch)
        {
            if (transitions.Length == 0) transitions = TownFallback;
            // Older dumps were saved as data/walkability/1_1_town.bin without a .json.
            if (cells is null && File.Exists($"{Folder}/1_1_town.bin")) { cells = File.ReadAllBytes($"{Folder}/1_1_town.bin"); (w, h) = (690, 414); }
            bool fits = cells is not null && cells.Length == 690 * 414 && (w, h) == (690, 414);
            return new AreaLayout(hash, id, 690, 414, 197, 261, fits ? Walkable(cells!, w, h) : null, transitions)
            { Clearance = fits ? Clearance(cells!, w, h) : null };
        }
        if (cells is not null)
        {
            // The area's start point (a room's "default", e.g. The Twilight Strand's beach), else the deepest cell.
            var start = AreaPopulation.LoadPoints(id, seed, []).FirstOrDefault(p => p.Name == "default");
            var (sx, sy) = start is null ? DeepestCell(cells, w, h) : (start.X, start.Y);
            return new AreaLayout(hash, id, w, h, sx, sy, Walkable(cells, w, h), transitions) { Clearance = Clearance(cells, w, h) };
        }
        // No walk map yet: a position inside any grid; the client may place the player on unwalkable ground.
        return new AreaLayout(hash, id, 0, 0, 197, 261, null, transitions);
    }

    /// <summary>Dev: the walk-map recorder runs beside the Gateway (live.ps1 -Walkmaps sets Game_WALKMAPS=1). Only then does
    /// the Gateway write anything for an area load (<see cref="Announce"/>) and run capture tours (/tour, CaptureTour,
    /// TourRecords); by default it records nothing.</summary>
    public static readonly bool Recording = Environment.GetEnvironmentVariable("Game_WALKMAPS") == "1";

    /// <summary>Tells scripts/walkmaps which area the next generated grid belongs to (data/walkability/current.json),
    /// when it runs (<see cref="Recording"/>).</summary>
    public static void Announce(AreaLayout layout, uint seed)
    {
        if (!Recording) return;
        // A read-only data folder (a container image) has no recorder to tell; stop instead of waiting on every entry.
        if (!folderWritable.Value) return;
        try
        {
            string json = JsonSerializer.Serialize(new
            {
                id = layout.Id, seed, hash = layout.Hash, written = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                nonce = Guid.NewGuid().ToString("N"),
            });
            SaveFiles.Write($"{Folder}/current.json", json);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // A fresh file name nobody else holds, so a denial means the folder itself is not writable.
    static readonly Lazy<bool> folderWritable = new(() =>
    {
        try
        {
            Directory.CreateDirectory(Folder);
            using (new FileStream(Path.Combine(Folder, $"probe.{Guid.NewGuid():N}.tmp"), FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            Console.WriteLine($"[gateway] {Folder} is not writable ({e.Message}): the walk-map recorder is not told about areas");
            return false;
        }
    });

    /// <summary>When the client reports the area generated (0x7d): the walk map scripts/walkmaps saved meanwhile, if
    /// any. Waits up to 2 s for the file.</summary>
    public static async Task<AreaLayout?> WaitFor(AreaLayout layout, uint seed)
    {
        string bin = $"{Folder}/{layout.Id}_seed{seed}.bin";
        for (int i = 0; i < 20; i++)
        {
            if (File.Exists(bin) && File.Exists(Path.ChangeExtension(bin, ".json")))
            {
                var better = For(layout.Hash, seed);
                return better.Walkable is null ? null : better;
            }
            await Task.Delay(100);
        }
        return null;
    }

    /// <summary>When the client reports the area generated (0x7d) and the layout has no area transitions yet: the ones
    /// scripts/walkmaps captured meanwhile, if any. Waits up to 1 s for the file.</summary>
    public static async Task<AreaLayout?> WaitForTransitions(AreaLayout layout, uint seed)
    {
        for (int i = 0; i < 10; i++)
        {
            if (AreaTransitionCatalog.Captured(layout.Id, seed))
            {
                await Task.Delay(200);   // the capture is rewritten per object; let the last ones land
                var better = For(layout.Hash, seed);
                return better.AreaTransitions.Length > 0 || better.Waypoint is not null ? better : null;
            }
            await Task.Delay(100);
        }
        return null;
    }

    // data/walkability/<id>_seed<seed>.bin (1 byte per cell) + .json {"w":..,"h":..}, written by scripts/walkmaps.
    static byte[]? LoadCells(string id, uint seed, out int w, out int h)
    {
        (w, h) = (0, 0);
        string bin = $"{Folder}/{id}_seed{seed}.bin", meta = Path.ChangeExtension(bin, ".json");
        if (!File.Exists(bin) || !File.Exists(meta)) return null;
        try
        {
            var m = JsonDocument.Parse(File.ReadAllText(meta)).RootElement;
            (w, h) = (m.GetProperty("w").GetInt32(), m.GetProperty("h").GetInt32());
            byte[] cells = File.ReadAllBytes(bin);
            return cells.Length == w * h ? cells : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    static Func<int, int, int, bool> Clearance(byte[] cells, int w, int h) =>
        (x, y, size) => x >= 0 && y >= 0 && x < w && y < h && cells[y * w + x] >= size + 1;

    // 1 byte per cell; the client walks on values >= 2 (Agents/docs/protocol/07 §5).
    static Func<int, int, bool> Walkable(byte[] cells, int w, int h) =>
        (x, y) => x >= 0 && y >= 0 && x < w && y < h && cells[y * w + x] >= 2;

    static readonly (int Dx, int Dy)[] Neighbours = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>The walkable cell farthest from any blocked cell (multi-source BFS from blocked cells).</summary>
    static (int X, int Y) DeepestCell(byte[] cells, int w, int h)
    {
        var dist = new int[w * h];
        var q = new Queue<int>();
        for (int i = 0; i < cells.Length; i++)
            if (cells[i] < 2) { dist[i] = 0; q.Enqueue(i); } else dist[i] = int.MaxValue;
        while (q.Count > 0)
        {
            int i = q.Dequeue(), x = i % w, y = i / w;
            foreach (var (dx, dy) in Neighbours)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                int j = ny * w + nx;
                if (dist[j] > dist[i] + 1) { dist[j] = dist[i] + 1; q.Enqueue(j); }
            }
        }
        int best = Array.IndexOf(dist, dist.Where(d => d != int.MaxValue).Max());
        return (best % w, best / w);
    }

}
