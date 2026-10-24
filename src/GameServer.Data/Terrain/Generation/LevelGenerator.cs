namespace GameServer.Data.Terrain.Generation;

/// <summary>A topology an area can use (Topologies.dat): the graph file, its allowed orientations (+0x1c) and its
/// variant (+0x2c; 60 = default).</summary>
public sealed record TopologyInfo(string Graph, IReadOnlyList<int> Orientations, int Variant = 60);

/// <summary>What the generator needs to know about the area, read at the client's offsets of its WorldAreas row
/// (Agents/docs/re/011-level-generation.md §1): +0x14 town, +0x2a HASH16, +0x68 topologies, +0xe4 map area, +0x14e
/// randomness off (hideouts), +0x1e4 area type, +0x22a league chances (a WorldAreaLeagueChances row, read by byte
/// offset), and the Hideouts row discoverable in it (+0x42 enabled, +0x43 chance in 10 000). <see cref="League"/> is a
/// CoreLeagues row by index: +0x8a most per area, +0x8e needs a chance, +0xa0 not rolled here. <see cref="HideoutClaim"/>
/// is that Hideouts row's claim subarea (+0x2a, a WorldAreas id), <see cref="SubArea"/> the topologies of a WorldAreas
/// row by id (a graph node's "subarea=…"). +0x171 marks a Labyrinth area, +0x1bb a Labyrinth boss area.</summary>
public sealed record AreaInfo(string Id, bool IsTown, IReadOnlyList<TopologyInfo> Topologies, bool RandomnessDisabled = false,
    ushort Hash16 = 0, bool IsMapArea = false, int AreaType = 0, int Level = 1, Func<int, int>? LeagueChance = null,
    (bool Enabled, int Chance)? DiscoverableHideout = null, Func<int, (int Max, bool NeedsChance, bool Skip)>? League = null,
    string? HideoutClaim = null, Func<string, IReadOnlyList<TopologyInfo>?>? SubArea = null, bool IsLabyrinth = false,
    bool IsLabyrinthBoss = false)
{
    /// <summary>The area's row and the rows it points to, from the game's tables.</summary>
    public static AreaInfo Load(Func<string, byte[]?> read, string areaId, int level)
    {
        RawDat Dat(string name) => new(read($"Data/{name}.datc64") ?? throw new FileNotFoundException($"Data/{name}.datc64"));
        var areas = Dat("WorldAreas");
        var topologies = Dat("Topologies");
        int row = areas.Find(r => areas.String(r, 0) == areaId);
        if (row < 0) throw new ArgumentException($"no WorldAreas row {areaId}");
        List<TopologyInfo> Tops(int r) => areas.RefList(r, 0x68)
            .Select(t => new TopologyInfo(topologies.String(t, 8), topologies.I32List(t, 0x1c), topologies.I32(t, 0x2c))).ToList();
        var tops = Tops(row);
        int chancesRow = areas.Ref(row, 0x22a);
        Func<int, int>? chance = null;
        if (chancesRow >= 0)
        {
            var chances = Dat("WorldAreaLeagueChances");
            chance = at => chances.I32(chancesRow, at);
        }
        var leagues = Dat("CoreLeagues");
        var hideouts = Dat("Hideouts");
        int hideout = hideouts.Find(h => hideouts.RefList(h, 0x1a).Contains(row));
        int claim = hideout < 0 ? -1 : hideouts.Ref(hideout, 0x2a);
        return new AreaInfo(areaId, areas.Bool(row, 0x14), tops, areas.Bool(row, 0x14e), areas.U16(row, 0x2a), areas.Bool(row, 0xe4),
            areas.I32(row, 0x1e4), level, chance, hideout < 0 ? null : (hideouts.Bool(hideout, 0x42), hideouts.I32(hideout, 0x43)),
            i => (leagues.I32(i, 0x8a), leagues.Bool(i, 0x8e), leagues.Bool(i, 0xa0)),
            claim < 0 ? null : areas.String(claim, 0),
            id => areas.Find(r => areas.String(r, 0) == id) is var r and >= 0 ? Tops(r) : null, areas.Bool(row, 0x171), areas.Bool(row, 0x1bb));
    }
}

/// <summary>The generated tile grid and the subgraph records (rectangle, master, placed rooms) the doodad stage
/// (<see cref="DoodadStage"/>) runs over.</summary>
public sealed record GenerationResult(TileGrid Grid, LevelRandom Random, TopologyInfo Topology, IReadOnlyList<string> Notes,
    IReadOnlyList<DoodadRecord> Records);

/// <summary>The server's port of the client's level generator (<c>0x14204d820</c>, Agents/docs/re/011-level-generation.md).
/// Ported so far: .dgr layouts (one node: towns; several: dungeons, Agents/docs/re/011-level-generation.md §3c2) and
/// .tgr tile graphs, up to the doodads. Every step names the client function it follows.</summary>
public static class LevelGenerator
{
    /// <summary>Diagnostics: called with the tile grid and the view (x, y, width, height) as a tile graph's fill starts
    /// (the trace's <c>fillVertices</c>).</summary>
    public static Action<TileGrid, int, int, int, int>? FillStarting;

    /// <summary>Diagnostics: called with the tile grid and the view (x, y, width, height) as the level painting
    /// (<c>0x142110d60</c>) starts (compared with the trace's <c>paint.after</c>).</summary>
    public static Action<TileGrid, int, int, int, int>? PaintStarting;

    /// <summary>Diagnostics: called with a dungeon layout stage's name (as the trace's: "fill&lt;", "fill&gt;",
    /// "connect&lt;", …), the draws so far and its room-unit cells (0 free, 1 reserved, k ≥ 2 room k − 2; compared with the trace's
    /// <c>dungeon.stages</c>).</summary>
    public static Action<string, int, int[]>? DungeonStage;

    /// <summary>Diagnostics: called with the dungeon layout's rooms once each has its room (compared with the trace's
    /// <c>dungeon.rooms</c>).</summary>
    public static Action<List<string>>? DungeonRooms;

    /// <summary>Generates the area. Pass <paramref name="rng"/> (seeded, with the area's randomness flag) to keep its draw
    /// log when generation stops at a part that is not ported, <paramref name="notes"/> to keep its notes and
    /// <paramref name="onStage"/> to see a tile graph at the stage boundaries the client trace records.</summary>
    public static GenerationResult Generate(TerrainCache files, AreaInfo area, uint seed, int topologyVariant = 60, LevelRandom? rng = null,
        List<string>? notes = null, Action<string, TileGraph>? onStage = null)
    {
        rng ??= new LevelRandom(seed, area.RandomnessDisabled) { Log = [] };
        notes ??= [];
        var features = ExtraTerrainFeatures.Roll(area, rng);
        if (features.Count > 0) notes.Add("extra terrain features: " + string.Join(", ", features.Select(f => $"{f.Name} x{f.Count}")));

        // 0x142046c60 → 0x142027a20: the area's topologies of this variant, else those of 60, else of 1.
        var choices = Choices(area.Topologies, topologyVariant, area.Id);
        // 0x142046a70: the index, then the orientation from the picked topology's list (same LCG seed).
        var (index, orientation) = rng.PickIndexAndOrientation(choices.Count, i => LevelRandom.OrientationMask(choices[i].Orientations));
        var topology = choices[index];
        notes.Add($"topology {topology.Graph} (#{index} of {choices.Count}), orientation {orientation}");

        // 0x14204a570's adds: the rolled features' rows wait in the extra room pool (generator +0x40).
        var pool = ExtraRoomPool.Create(files.Raw, features);
        bool hideout = features.Any(f => f.Name == ExtraTerrainFeatures.DiscoverableHideout);
        var secondaries = SubAreas(files, area, topology.Graph, hideout, topologyVariant, rng, notes);
        var labyrinth = area.IsLabyrinth ? new LabyrinthPass(files.Raw, area.Id, area.IsLabyrinthBoss) : null;
        bool dgr = topology.Graph.EndsWith(".dgr", StringComparison.OrdinalIgnoreCase);
        if (!dgr && secondaries.Count != 0)
            throw new NotSupportedException($"{topology.Graph}: sub-area subgraphs beside a tile graph (0x142047540) are not ported");
        TileGrid grid;
        IReadOnlyList<DoodadRecord> records;
        if (dgr)
        {
            var tiler = new DungeonTiler(files, rng, notes, pool) { Hideout = hideout, Labyrinth = labyrinth, Secondaries = secondaries };
            grid = tiler.Generate(topology.Graph, orientation);
            records = tiler.Records;
        }
        else
        {
            var gen = new TileGraphGenerator(files, rng, notes, features.Any(f => f.Name == ExtraTerrainFeatures.VaalSideArea), onStage) { Hideout = hideout, Labyrinth = labyrinth };
            grid = gen.Generate(topology.Graph, orientation);
            records = gen.Records;
        }
        return new GenerationResult(grid, rng, topology, notes, records);
    }

    static List<TopologyInfo> Choices(IReadOnlyList<TopologyInfo> topologies, int variant, string areaId)
    {
        var choices = topologies.Where(t => t.Variant == variant).ToList();
        if (choices.Count == 0) choices = topologies.Where(t => t.Variant == 60).ToList();
        if (choices.Count == 0) choices = topologies.Where(t => t.Variant == 1).ToList();
        if (choices.Count == 0) throw new InvalidOperationException($"no topologies for {areaId}");
        return choices;
    }

    /// <summary>The sub-areas' subgraphs (graph, orientation). <c>0x1420457f0</c> → <c>0x14204f590</c> on the primary
    /// graph, before the records are counted: a node description "subarea=X" (case-insensitive prefix) queues the world
    /// area X (generator +0x1f0; "Unknown sub world area"); "subarea=hideout" queues the hideout's claim subarea
    /// (Hideouts +0x2a) when the discoverable hideout rolled, else skips the node's remaining descriptions. Then
    /// <c>0x142047540</c> → <c>0x142047090</c> → <c>0x142046340</c> for each in order: its topologies (chosen as the
    /// area's), <c>Next(count)</c>, then <c>Next(840)</c> → <c>0x14299da50</c> with the picked topology's orientation
    /// mask. The node filter <c>0x142036490</c> (league= nodes) is left to <see cref="TileGraphGenerator.Prepare"/>,
    /// which stops on them.</summary>
    static List<(string Graph, int Orientation)> SubAreas(TerrainCache files, AreaInfo area, string graphPath, bool hideout, int variant,
        LevelRandom rng, List<string> notes)
    {
        var graph = TileGraph.Parse(files.Text(graphPath) ?? throw new FileNotFoundException(graphPath));
        var ids = new List<string>();
        foreach (var n in graph.Nodes)
            foreach (var d in n.Descriptions)
            {
                if (d.Length < 8 || !d.StartsWith("subarea=", StringComparison.OrdinalIgnoreCase)) continue;
                string id = d[8..];
                if (id == "hideout")
                {
                    if (!hideout) break;
                    ids.Add(area.HideoutClaim ?? throw new InvalidOperationException("Cannot find Hideout claim subarea"));
                }
                else ids.Add(id);
            }
        var result = new List<(string, int)>();
        foreach (var id in ids)
        {
            var choices = Choices(area.SubArea?.Invoke(id) ?? throw new InvalidOperationException($"Unknown sub world area: {id}"), variant, id);
            var topology = choices[(int)rng.Next((uint)choices.Count)];
            int orientation = LevelRandom.OrientationFromMask(LevelRandom.OrientationMask(topology.Orientations), rng.Next(840u));
            notes.Add($"sub-area {id}: {topology.Graph}, orientation {orientation}");
            result.Add((topology.Graph, orientation));
        }
        return result;
    }
}

/// <summary>A dungeon graph (.dgr) subgraph: <c>0x14203c900</c> → rooms placed (<c>0x1421038d0</c>, one node here,
/// several in <see cref="DungeonLayout"/>), tilised (<c>0x1420f3480</c>: <c>0x1420ef310</c> per room, the shared
/// border slots, the fill, <c>0x142025440</c> seams), then the outer ground (<c>0x14201db80</c>).</summary>
sealed class DungeonTiler(TerrainCache files, LevelRandom rng, List<string> notes, ExtraRoomPool pool)
{
    /// <summary>The discoverable hideout rolled (generator +0xb).</summary>
    public bool Hideout { get; init; }

    /// <summary>The Labyrinth's graph pass, for a Labyrinth area.</summary>
    public LabyrinthPass? Labyrinth { get; init; }

    /// <summary>The sub-areas' subgraphs (graph, orientation), placed beside the primary (<c>0x142047e50</c>).</summary>
    public List<(string Graph, int Orientation)> Secondaries { get; init; } = [];

    TileGrid _grid = null!;
    TileStore _store = null!;
    Dictionary<string, List<(int Weight, string Path)>> _fills = null!;
    readonly List<(int X, int Y, string? Ground, short Height)> _pendingFills = [];
    int _overlap;
    bool _multi;

    /// <summary>A slot on a room's shared border (RoomOverlap, <c>0x1420eeec0</c>'s 0x80-byte entries at +0x90, by
    /// position): the key (merged when a second room reaches it), height (+0x58), the sides the rooms cover and the
    /// accessible bit (+0x5c), the side flags (+0x5d), the slot tags (+0x60) and descriptions (+0x78).</summary>
    sealed class BorderSlot
    {
        public TileKey Key = null!;
        public int Height;
        public byte Flags, Sides;
        public List<string> Tags = [];
        public List<string>? Descriptions;
    }

    readonly Dictionary<(int X, int Y), BorderSlot> _borders = [];
    EdgeCombiner? _combiner;

    /// <summary>The TileSet has "door" tiles (tiler context +0x38, <c>0x1420f0b60</c>): rooms mark their doorways and
    /// the shared doorways get door tiles (<c>0x1420f4010</c>, <c>0x1420f4ae0</c>).</summary>
    bool _doorTiles;
    int _unit, _ox, _oy;
    (int X0, int Y0, int X1, int Y1) _view;
    /// <summary><c>0x1420ed9f0</c>'s marks (tiler +8, by position, byte +0x18): the side of its room a doorway lies on
    /// (4 top, 0x10 right, 2 bottom, 8 left).</summary>
    readonly Dictionary<(int X, int Y), byte> _doorMarks = [];
    /// <summary><c>0x1420edf80</c>: doorway positions joined into doorways (+0x48) through their doorway number per
    /// entry (+0xa8); larger door slots at +0x60.</summary>
    readonly List<List<(int X, int Y)>> _doorways = [];
    readonly List<(int X, int Y)> _bigDoors = [];
    int[] _doorwayOf = [];

    /// <summary>A subgraph (<c>0x1420457f0</c>'s 0x98-byte records, generator +0x30 → +0x10): graph (+0x20), rectangle
    /// (+0x40 x, +0x44 y, +0x48 w, +0x4c h: (unit − overlap) × the graph's size + overlap, turned with the orientation,
    /// plus the margins), master (+0x50), parent (+0x92), orientation (+0x97).</summary>
    sealed record Subgraph(string Path, int X, int Y, int W, int H, string MasterPath, Dictionary<string, string> Master,
        (int Top, int Right, int Bottom, int Left) Margins, int Orientation, int Parent);

    /// <summary>The master's OuterBufferSize (<c>0x142a06390</c>): up to four numbers, top (+0x170), right (+0x171, else
    /// top), bottom (+0x172, else top), left (+0x173, else right): the subgraph's margins.</summary>
    static (int Top, int Right, int Bottom, int Left) Margins(Dictionary<string, string> master)
    {
        var n = master.GetValueOrDefault("OuterBufferSize", "0").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
        int top = n.Count > 0 ? n[0] : 0, right = n.Count > 1 ? n[1] : top, bottom = n.Count > 2 ? n[2] : top, left = n.Count > 3 ? n[3] : right;
        return (top, right, bottom, left);
    }

    /// <summary><c>0x1420457f0</c>: the record of the graph at (x, y): a .tgr's size is its graph's (+0x40 / +0x48), a
    /// .dgr's its graph's in room units. Anchoring by the orientation's corner is ported for orientation 0 only (no
    /// offset).</summary>
    Subgraph Record(string graphPath, int x, int y, int orientation, int parent)
    {
        var graph = GraphFile.Parse(files.Text(graphPath) ?? throw new FileNotFoundException(graphPath));
        string masterPath = TerrainText.Resolve(graphPath, graph.MasterFile);
        var master = TerrainText.KeyValues(files.Text(masterPath) ?? throw new FileNotFoundException(masterPath));
        int unit = int.Parse(master.GetValueOrDefault("RoomUnitSize", "1")), overlap = int.Parse(master.GetValueOrDefault("RoomOverlap", "1"));
        int w = (unit - overlap) * graph.Width + overlap, h = (unit - overlap) * graph.Height + overlap;
        if (graphPath.EndsWith(".tgr", StringComparison.OrdinalIgnoreCase)) (w, h) = (graph.Width, graph.Height);
        if ((orientation & 1) != 0 && orientation < 8) (w, h) = (h, w);
        var m = Margins(master);
        if (parent >= 0 && orientation != 0) throw new NotSupportedException($"{graphPath}: subgraphs turned by orientation {orientation} (0x1420457f0's anchor) are not ported");
        return new Subgraph(graphPath, x, y, w + m.Left + m.Right, h + m.Top + m.Bottom, masterPath, master, m, orientation, parent);
    }

    /// <summary>The subgraph records with their placed rooms, once generated.</summary>
    public List<DoodadRecord> Records { get; private set; } = [];

    /// <summary>The rooms <see cref="Tilise"/> placed, in layout order.</summary>
    List<PlacedRoom> _placed = [];

    public TileGrid Generate(string graphPath, int graphOrientation)
    {
        var graph = GraphFile.Parse(files.Text(graphPath) ?? throw new FileNotFoundException(graphPath));
        if (graph.Nodes.Count != 1 || Secondaries.Count != 0) return Composite(graphPath, graphOrientation);
        string masterPath = TerrainText.Resolve(graphPath, graph.MasterFile);
        var master = TerrainText.KeyValues(files.Text(masterPath) ?? throw new FileNotFoundException(masterPath));
        int unit = int.Parse(master.GetValueOrDefault("RoomUnitSize", "1"));
        _grid = new TileGrid(graph.Width * unit, graph.Height * unit) { CheckGrounds = false, GroundFlags = g => files.Ground(g)?.Flags ?? 0, Unmirrored = files.Unmirrored };
        _store = new TileStore(files, files.TileList(TerrainText.Resolve(masterPath, master["TileSet"])));
        _fills = master.TryGetValue("FillTiles", out var gft) ? files.FillTiles(TerrainText.Resolve(masterPath, gft)) : [];
        var rooms = RoomSetPaths(TerrainText.Resolve(masterPath, master["RoomSet"]));

        var node = graph.Nodes[0];
        // 0x142105a70: the room among the room set's rooms with the node's tag.
        var candidates = rooms.Select(r => (r.Weight, Room: files.Room(r.Path))).Where(r => r.Room?.Tag == node.Tag).ToList();
        int total = candidates.Sum(c => c.Weight);
        int pick = (int)rng.Next((uint)total);
        var room = candidates.First(c => (pick -= c.Weight) < 0).Room!;
        int code = Compose(graphOrientation, Orientation.Parse(node.Rotation));
        notes.Add($"room {room.Path} ({room.Width}x{room.Height}) orientation {code}");

        TiliseRoom(room, code, 0, 0);
        Fill();
        // 0x142025440 over the whole map, the room in its room list (its vertex grounds are not ported here).
        var (rw, rh) = Orientation.Size(code, room.Width, room.Height);
        var tg = TileGraph.Parse(files.Text(graphPath)!);
        new Seams(files, rng, _grid, 0, 0, _grid.Width, _grid.Height, _store, [new PlacedRoom(room, 0, 0, rw, rh, [], code)], new HashSet<(int X, int Y)>(),
            (Ground(tg.TopGround), Ground(tg.BottomGround), Ground(tg.DefaultGround)), notes).Run();
        // 0x142044f30: the walkability pass (0x1420289a0) over the one record, the whole map.
        new GroundStages(_grid, 0, 0, _grid.Width, _grid.Height, files).Walkability(TileGraphGenerator.ImpassableGround, files.HasTransition);
        Records = [new DoodadRecord(0, 0, _grid.Width, _grid.Height, master, masterPath, [new PlacedRoom(room, 0, 0, rw, rh, [], code)])];
        return _grid;
        string? Ground(string g) => g.Length == 0 ? null : TerrainText.Resolve(graphPath, g);
    }

    /// <summary><c>0x142049d10</c>: the subgraph records in order, each laid out and tilised on one tile map
    /// (<c>0x1420452e0</c> → <c>0x14203c900</c>; a layout's graph nodes add their subgraphs, <c>0x14204f030</c>); then,
    /// last subgraph first, each one's outer ground and outer buffer (<c>0x142044f30</c>); then StoreyHeight. A .tgr
    /// subgraph is laid out on the same map at its view (<c>0x14203f890</c>, <see cref="TileGraphGenerator.Subgraph"/>);
    /// the first record (generator +0x30 +9, taken to be 1) is the primary one.</summary>
    TileGrid Composite(string graphPath, int graphOrientation)
    {
        var records = new List<Subgraph> { Record(graphPath, 0, 0, graphOrientation, -1) };
        var (gw, gh) = PlaceSecondaries(records);
        var grid = new TileGrid(gw, gh) { CheckGrounds = false, GroundFlags = g => files.Ground(g)?.Flags ?? 0, Unmirrored = files.Unmirrored };
        var outers = new List<Action>();
        var roomsOf = new List<IReadOnlyList<PlacedRoom>>();
        // 0x142049d10: with rows in the pool, the weight to share them by is the sum of the subgraphs' masters' +0x152
        // (0x14204bfa0); +0x152 is taken to default to 0 (no trace drew 0x141d8bba0's remainder roll), and a master
        // that sets ExtraFeatureWeight (the key taken to be +0x152) is not ported.
        if (pool.Rows.Count != 0 && records[0].Master.ContainsKey("ExtraFeatureWeight"))
            throw new NotSupportedException("ExtraFeatureWeight (master +0x152, 0x14204bfa0) is not ported");
        for (int i = 0; i < records.Count; i++)
        {
            var r = records[i];
            // 0x14203c900 / 0x14203f890: a subgraph whose parent is the first (+0x92 = 0) takes its share of the rows.
            if (r.Parent <= 0) pool.Spread(0, rng);
            notes.Add($"subgraph {i}: {r.Path} at ({r.X}, {r.Y}) {r.W}x{r.H}");
            int vx = r.X + r.Margins.Left, vy = r.Y + r.Margins.Top, vw = r.W - r.Margins.Left - r.Margins.Right, vh = r.H - r.Margins.Top - r.Margins.Bottom;
            TileStore store;
            if (r.Path.EndsWith(".tgr", StringComparison.OrdinalIgnoreCase))
            {
                // The tile graph's special nodes do not take the pool's rows (not ported).
                if (pool.Share != 0) throw new NotSupportedException($"{r.Path}: extra feature rooms in a tile graph subgraph are not ported");
                var gen = new TileGraphGenerator(files, rng, notes, false) { Hideout = Hideout, Labyrinth = Labyrinth };
                gen.Subgraph(r.Path, r.Orientation, grid, vx, vy, primary: i == 0);
                store = gen.Store;
                roomsOf.Add(gen.Rooms);
                outers.Add(() => OuterTileGraph(r));
            }
            else
            {
                var t = i == 0 ? this : new DungeonTiler(files, rng, notes, pool) { Hideout = Hideout, Labyrinth = Labyrinth };
                t.Tilise(r, grid, records, i);
                store = t._store;
                roomsOf.Add(t._placed);
                outers.Add(() => t.Outer(r));
            }
            // 0x1420452e0 after the layout: the TileSet's post-unique tiles over the subgraph's view (0x14203ece0).
            PostUniqueTiles.Run(rng, grid, vx, vy, vw, vh, store, notes);
        }
        for (int i = records.Count - 1; i >= 0; i--) outers[i]();
        // 0x142044f30 then: the walkability pass (0x1420289a0) over the rectangle of each primary record (the first,
        // generator +0x30 +9 taken to be 1) and of each later one whose master has CountAsPrimary (+0x164), then
        // 0x142028870 flags the map's border vertices. Only grounds and vertex flags change.
        for (int i = 0; i < records.Count; i++)
            if (i == 0 || records[i].Master.ContainsKey("CountAsPrimary"))
                new GroundStages(grid, records[i].X, records[i].Y, records[i].W, records[i].H, files).Walkability(TileGraphGenerator.ImpassableGround, files.HasTransition);
        new GroundStages(grid, 0, 0, grid.Width, grid.Height, files).FlagBorder();
        // 0x14204cbd0: tile heights in levels become the client's units, each tile origin by the StoreyHeight of the
        // last record whose rectangle holds it (0x141d77a90), else the first's.
        var storeys = records.Select(r => int.Parse(r.Master.GetValueOrDefault("StoreyHeight", "0"))).ToList();
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                if (grid[x, y] is { IsOrigin: true, HeightOffset: not 0 })
                {
                    int k = records.FindLastIndex(r => x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H);
                    grid.ScaleHeight(x, y, storeys[Math.Max(k, 0)]);
                }
        Records = [.. records.Select((r, i) => new DoodadRecord(r.X, r.Y, r.W, r.H, r.Master, r.MasterPath, roomsOf[i]))];
        return grid;
    }

    /// <summary><c>0x142047e50</c>: each secondary record (parent +0x92 = its index) goes to the corner, among those
    /// right of (x + w + 1, y) and below (x, y + h + 1) each earlier record, that keeps a tile clear of every placed
    /// rectangle (<c>0x1420487f0</c>) and gives the smallest bounding area: max(threshold, map width, its right) ×
    /// max(threshold, map height, its bottom), the first of equals (a stable sort). The threshold is 0, or with more
    /// than 10 records 4/5 of the integer square root of their total area (<c>0x141937840</c>). Returns the map's size
    /// (<c>gen[5]</c> vtable +8).</summary>
    (int W, int H) PlaceSecondaries(List<Subgraph> records)
    {
        for (int i = 0; i < Secondaries.Count; i++)
            records.Add(Record(Secondaries[i].Graph, 0, 0, Secondaries[i].Orientation, i + 1));
        int threshold = records.Count > 10 ? (int)Math.Sqrt(records.Sum(r => r.W * r.H)) * 4 / 5 : 0;
        var placed = new List<(int X0, int Y0, int X1, int Y1)> { (records[0].X, records[0].Y, records[0].X + records[0].W, records[0].Y + records[0].H) };
        int mw = records[0].W, mh = records[0].H;
        for (int i = 1; i < records.Count; i++)
        {
            var r = records[i];
            var candidates = new List<(long Score, int X, int Y)>();
            void Try(int cx, int cy)
            {
                if (placed.Any(p => p.X0 < cx + r.W + 1 && cx - 1 < p.X1 && p.Y0 < cy + r.H + 1 && cy - 1 < p.Y1)) return;
                long w = Math.Max(threshold, Math.Max(mw, cx + r.W)), h = Math.Max(threshold, Math.Max(mh, cy + r.H));
                candidates.Add((w * h, cx, cy));
            }
            for (int j = 0; j < i; j++)
            {
                Try(records[j].X + records[j].W + 1, records[j].Y);
                Try(records[j].X, records[j].Y + records[j].H + 1);
            }
            var best = candidates.OrderBy(c => c.Score).First();
            records[i] = r = r with { X = best.X, Y = best.Y };
            placed.Add((r.X, r.Y, r.X + r.W, r.Y + r.H));
            mw = Math.Max(mw, r.X + r.W);
            mh = Math.Max(mh, r.Y + r.H);
        }
        return (mw, mh);
    }

    /// <summary><c>0x14203c900</c> for one subgraph of several nodes on the shared map: the layout, at the record's
    /// corner inside its margins; the rooms tilised; then each room without a room but with descriptions (a graph
    /// node's, <c>0x14204f030</c> → <c>0x1420468e0</c>) adds the subgraph its first description names, at the room's
    /// corner (orientation 0: no offset).</summary>
    void Tilise(Subgraph r, TileGrid grid, List<Subgraph> records, int index)
    {
        var master = r.Master;
        string masterPath = r.MasterPath;
        _grid = grid;
        _store = new TileStore(files, files.TileList(TerrainText.Resolve(masterPath, master["TileSet"])));
        _fills = master.TryGetValue("FillTiles", out var gft) ? files.FillTiles(TerrainText.Resolve(masterPath, gft)) : [];
        int unit = int.Parse(master.GetValueOrDefault("RoomUnitSize", "1"));
        var map = Layout(r.Path, r.Orientation, masterPath, master, unit, index == 0);
        // Room k starts at the view's corner plus (unit − overlap) × its cell.
        int step = unit - map.Overlap;
        _ox = r.X + r.Margins.Left;
        _oy = r.Y + r.Margins.Top;
        if (_ox < 0 || _oy < 0 || _ox + step * map.Width + map.Overlap > grid.Width || _oy + step * map.Height + map.Overlap > grid.Height)
            throw new InvalidOperationException($"Subgraph {r.Path} extends outside the TileMap!");
        _overlap = map.Overlap;
        _multi = true;
        _unit = unit;
        _doorTiles = _store.HasTag("door");
        _doorwayOf = new int[_grid.Width * _grid.Height];
        if (master.TryGetValue("EdgeCombination", out var ecf))
            _combiner = EdgeCombiner.Parse(files.Text(TerrainText.Resolve(masterPath, ecf)) ?? "", masterPath);
        // 0x1420f3480: 0x1420ef310 per room.
        foreach (var p in map.Rooms)
        {
            if (p.Room?.Def is not { } def) continue;
            if (_doorTiles && _overlap != 0) MarkDoorways(p, _ox + step * p.X, _oy + step * p.Y);
            TiliseRoom(def, p.Orientation, _ox + step * p.X, _oy + step * p.Y, p.Level, (p.Flags & 1) != 0, p.Descriptions);
        }
        notes.Add($"dungeon tilising: {_borders.Count} shared border slots");
        TiliseBorders();
        // With RoomOverlap (and master +0x167 clear, taken to be so) the door tiles; without, 0x1420f4d90 (master
        // +0x250), not ported. 0x1420f7960 (master +0x248) is not ported either. Then 0x141ffbd70 per room: its
        // inner vertex grounds, turned like a tile's.
        if (_overlap != 0 && _doorTiles)
        {
            DoorTiles();
            BigDoorTiles();
        }
        else if (_doorTiles && map.Rooms.Any(p => p.Room is not null))
            throw new NotSupportedException("door tiles without RoomOverlap (0x1420edd50, 0x1420f4d90) are not ported");
        var roomVertices = new HashSet<(int X, int Y)>();
        foreach (var p in map.Rooms)
            if (p.Room?.Def is { VertexGrounds.Length: > 0 } def)
                for (int v = 1; v < def.Height; v++)
                    for (int u = 1; u < def.Width; u++)
                        if (def.VertexGrounds[(v - 1) * (def.Width - 1) + u - 1] is { } g)
                        {
                            var (dx, dy) = Orientation.MapVertex(p.Orientation, u, v, def.Width, def.Height);
                            _grid.ForceGround(_ox + step * p.X + dx, _oy + step * p.Y + dy, g);
                            roomVertices.Add((_ox + step * p.X + dx, _oy + step * p.Y + dy));
                        }
        RoomGrounds(map, step);
        FillRecords(master, masterPath);
        // 0x1420f8f20 (draws only when +0xb0 lists cells, not ported), then the mirror seams (0x142025440) over the
        // layout's view, with the placed rooms as its room list.
        var placed = _placed = [];
        foreach (var p in map.Rooms)
            if (p.Room?.Def is { } def)
            {
                var (w, h) = Orientation.Size(p.Orientation, def.Width, def.Height);
                placed.Add(new PlacedRoom(def, _ox + step * p.X, _oy + step * p.Y, w, h, p.Descriptions, p.Orientation));
            }
        new Seams(files, rng, _grid, _ox, _oy, step * map.Width + map.Overlap, step * map.Height + map.Overlap, _store, placed, roomVertices,
            _graphGrounds, notes).Run();
        // The fill's rooms (.gft lines naming an .arm) join the record's rooms after the layout's, for the doodad stage.
        placed.AddRange(_fillRooms);
        foreach (var p in map.Rooms)
            if (p.Room is null && p.Descriptions.Count != 0)
                records.Add(Record(p.Descriptions[0], _ox + step * p.X, _oy + step * p.Y, r.Orientation, index));
    }

    /// <summary><c>0x142044f30</c> for a .tgr subgraph on the shared map: an OuterGroundType (<c>0x14201db80</c>) and
    /// margins (<c>0x14201ffa0</c>) are not ported for one.</summary>
    static void OuterTileGraph(Subgraph r)
    {
        if (r.Master.ContainsKey("OuterGroundType")) throw new NotSupportedException($"{r.Path}: the outer ground of a tile graph subgraph (0x14201db80) is not ported");
        if (r.Margins != (0, 0, 0, 0)) throw new NotSupportedException($"{r.Path}: the outer buffer of a tile graph subgraph (0x14201ffa0) is not ported");
    }

    /// <summary><c>0x142044f30</c> for one subgraph: its outer ground (<c>0x14201db80</c>) over its view, then its outer
    /// buffer (<c>0x14201ffa0</c>) in its margins.</summary>
    void Outer(Subgraph r)
    {
        _view = (r.X + r.Margins.Left, r.Y + r.Margins.Top, r.X + r.W - r.Margins.Right, r.Y + r.H - r.Margins.Bottom);
        OuterGround(r.Master, r.MasterPath);
        if (r.Margins == (0, 0, 0, 0)) return;
        if (r.X != 0 || r.Y != 0 || r.W != _grid.Width || r.H != _grid.Height)
            throw new NotSupportedException($"{r.Path}: the outer buffer of a subgraph that is not the whole map (0x14201ffa0) is not ported");
        new OuterBuffer(files, rng, _grid, _view.X0, _view.Y0, _view.X1, _view.Y1, _store, _fills, notes).Run();
    }

    /// <summary><c>0x14203c900</c> for a graph of several nodes: the exist rolls (as for tile graphs), then the rooms
    /// on a grid of the graph's size in room units (<c>0x1421038d0</c>).</summary>
    DungeonMap Layout(string graphPath, int graphOrientation, string masterPath, Dictionary<string, string> master, int unit, bool primary)
    {
        var graph = TileGraph.Parse(files.Text(graphPath) ?? throw new FileNotFoundException(graphPath));
        TileGraphGenerator.Prepare(graph, rng, false, Hideout, primary, Labyrinth);
        string? Ground(string g) => g.Length == 0 ? null : TerrainText.Resolve(graphPath, g);
        _graphGrounds = (Ground(graph.TopGround), Ground(graph.BottomGround), Ground(graph.DefaultGround));
        notes.Add($"dungeon graph {graphPath}: {graph.Nodes.Count} nodes, {graph.Edges.Count} edges after exist rolls");
        var roomSet = new DungeonRoomSet(files, TerrainText.Resolve(masterPath, master["RoomSet"]));
        var map = new DungeonMap(graph.Width, graph.Height, unit, int.Parse(master.GetValueOrDefault("RoomOverlap", "1")));
        var swap = (master.TryGetValue("SwapDungeonDoorsFrom", out var from) ? TerrainText.Resolve(masterPath, from) : null,
            master.TryGetValue("SwapDungeonDoorsTo", out var to) ? TerrainText.Resolve(masterPath, to) : null);
        var groups = master.TryGetValue("FileGroups", out var fgp) ? FileGroups(TerrainText.Resolve(masterPath, fgp)) : null;
        if (master.ContainsKey("DungeonFillRoomHeightOverride"))
            throw new NotSupportedException("DungeonFillRoomHeightOverride (0x142108f70's fixed level) is not ported");
        var outerGround = master.TryGetValue("OuterGroundType", out var og) ? TerrainText.Resolve(masterPath, og) : null;
        new DungeonLayout(files, rng, notes, graph, roomSet, map, graphOrientation, swap, LevelGenerator.DungeonStage,
            groups is null ? null : g => groups.GetValueOrDefault(g), outerGround, pool).Run();
        return map;
    }

    /// <summary>A FileGroups file (.fgp, master +0x268, <c>0x141fe83e0</c>): after the version line, a quoted group name,
    /// then its files (relative to the .fgp) until the next name.</summary>
    Dictionary<string, IReadOnlyList<string>> FileGroups(string path)
    {
        var groups = new Dictionary<string, IReadOnlyList<string>>();
        List<string>? current = null;
        foreach (var line in TerrainText.Lines(files.Text(path) ?? throw new FileNotFoundException(path)))
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith("version", StringComparison.OrdinalIgnoreCase)) continue;
            if (t.StartsWith('"')) groups[t.Trim('"')] = current = [];
            else current?.Add(TerrainText.Resolve(path, t));
        }
        return groups;
    }

    List<(int Weight, string Path)> RoomSetPaths(string path)
    {
        var list = new List<(int, string)>();
        foreach (var line in TerrainText.Lines(files.Text(path) ?? ""))
        {
            var t = TerrainText.Tokenize(line);
            int q = t.FindIndex(x => x.Quoted);
            if (q < 0) continue;
            list.Add((q > 0 && t[0].IsNumber ? t[0].Int : 1, TerrainText.Resolve(path, t[q].Text)));
        }
        return list;
    }

    // 0x141d75980.
    static readonly byte[] ComposeTable =
    [
        0, 1, 2, 3, 4, 5, 6, 7, 0,  1, 2, 3, 0, 7, 4, 5, 6, 1,  2, 3, 0, 1, 6, 7, 4, 5, 2,
        3, 0, 1, 2, 5, 6, 7, 4, 3,  4, 5, 6, 7, 0, 1, 2, 3, 4,  5, 6, 7, 4, 3, 0, 1, 2, 5,
        6, 7, 4, 5, 2, 3, 0, 1, 6,  7, 4, 5, 6, 1, 2, 3, 0, 7,  8, 8, 8, 8, 8, 8, 8, 8, 8,
    ];

    internal static int Compose(int a, int b) => ComposeTable[Math.Min(a, 8) * 9 + Math.Min(b, 8)];

    /// <summary><c>0x1420ef310</c>: every 'k' slot of the room, row by row. A slot without edges, tag or heights whose
    /// corners share one ground is left for the fill (<c>0x1420eec10</c>); any other picks a tile from the store.</summary>
    void TiliseRoom(RoomDef room, int code, int originX, int originY, int level = 0, bool accessible = false, List<string>? descriptions = null)
    {
        var (roomW, roomH) = Orientation.Size(code, room.Width, room.Height);
        for (int row = 0; row < room.Height; row++)
            for (int col = 0; col < room.Width; col++)
            {
                var slot = room.Slots[col, row];
                if (slot.Kind is not ('k' or 'f' or 's') || slot.Key is null) continue;
                var key = slot.Key;
                var (w, h) = Orientation.Size(code, key.Width, key.Height);
                var (x, y) = SlotOrigin(code, col + slot.ShiftX, row + slot.ShiftY, key.Width, key.Height, room.Width, room.Height);
                x += originX;
                y += originY;
                // Slot height (+0x72) plus the room's level (+0x12).
                short height = (short)(slot.Height + level);
                if (slot.Tag.Length == 0 && key.Edges.All(e => e is null) && key.Heights.All(v => v == 0) &&
                    key.Grounds.Distinct().Count() == 1)
                {
                    if (_multi)
                    {
                        PendingFill(x, y, key.Rotate(code), height, accessible);
                        continue;
                    }
                    for (int dy = 0; dy < h; dy++)
                        for (int dx = 0; dx < w; dx++)
                            _pendingFills.Add((x + dx, y + dy, key.Grounds[0], height));
                    continue;
                }
                // 0x1420f0a70, after the plain test (a plain slot goes to the fill even on the room's outer row): with
                // RoomOverlap, a key of odd size centred on the room's outer row or column is shared with the neighbour
                // (not for "missiontile").
                int lx = x - originX, ly = y - originY;
                int bx = lx < 1 ? 1 : roomW <= lx + w ? 2 : 0, by = ly < 1 ? 1 : roomH <= ly + h ? 2 : 0;
                if (_overlap != 0 && slot.Tag != "missiontile" &&
                    ((bx == 1 && w % 2 == 1 && lx + w / 2 == 0) || (bx == 2 && w % 2 == 1 && lx + w / 2 == roomW - 1) ||
                     (by == 1 && h % 2 == 1 && ly + h / 2 == 0) || (by == 2 && h % 2 == 1 && ly + h / 2 == roomH - 1)))
                {
                    Border(x, y, bx, by, height, key.Rotate(code), slot.Tag, accessible, descriptions);
                    continue;
                }
                var list = _store.Candidates(slot.Tag, key.Rotate(code));
                if (list.Count == 0 && slot.Tag == "missiontile")
                {
                    // 0x1420ef310: a mission tile without a tile takes an injected room of its size (0x141d8b450).
                    var injected = pool.Take(w, h, rng) ?? throw new NotSupportedException($"{room.Path}: a mission tile without an injected room is not ported");
                    TiliseInjected(injected.Room.Def!, injected.Orientation, x, y, height);
                    continue;
                }
                if (list.Count == 0) throw new InvalidOperationException($"{room.Path}: no tile for slot ({col}, {row}) {key.Rotate(code)} tag '{slot.Tag}'");
                var (tile, orientation) = Weighted(list, (int)rng.Next((uint)list.Sum(c => c.Weight)));
                // 0x1420ef310 places the tile (0x141ffb870), then sets its height (0x141d82d00): its vertices take the
                // slot height plus the tile's own, whatever grounds they have.
                _grid.Place(tile, orientation, x, y, height);
                _grid.SetHeight(x, y, height);
            }
    }

    // 0x141fe89e0's offset of a slot's tile by (1 − w, 1 − h) per orientation, as 0x141fea780's (SpecialNodes).
    static readonly int[] SlotShift = [0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 1, 0, 1, 0, 0, 1, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0];

    /// <summary><c>0x1420ef310</c>'s mission tile at (x, y): its w × h vertices take the slot height (flag 2), then
    /// <c>0x141fe89e0</c> tilises the injected room there: every 'k' / 'f' slot column by column, whose entry is still
    /// empty, takes a tile of its tag and key (<c>Next(total)</c>, up to five tries until it fits, <c>0x141ffbbe0</c>) at
    /// the slot height plus the room's; then the room's vertex grounds, and every vertex of the room still without a
    /// ground takes the ground at the mission tile's corner. Plain slots (the fill rooms' path), nested mission tiles
    /// and the pool's own tiles (+0x90) are not ported.</summary>
    void TiliseInjected(RoomDef room, int o, int x, int y, int height)
    {
        var (w, h) = Orientation.Size(o, room.Width, room.Height);
        for (int v = 0; v < h; v++)
            for (int u = 0; u < w; u++)
            {
                ref var c = ref _grid.Corner(x + u, y + v);
                c.Height = (sbyte)height;
                c.Flags |= 2;
            }
        _grid.MarkHeights(height != 0);
        string? ground = _grid.Corner(x, y).Ground;
        for (int col = 0; col < room.Width; col++)
            for (int row = 0; row < room.Height; row++)
            {
                var slot = room.Slots[col, row];
                if (slot is null || slot.Kind is not ('k' or 'f' or 's') || slot.Key is null) continue;
                var (gx, gy) = Orientation.Map(o, col, row, room.Width, room.Height);
                int sw = 1 - slot.Key.Width, sh = 1 - slot.Key.Height, so = Math.Min(o, 7) * 4;
                int tx = x + gx + sw * SlotShift[so] + sh * SlotShift[so + 2];
                int ty = y + gy + sw * SlotShift[so + 1] + sh * SlotShift[so + 3];
                var key = slot.Key.Rotate(o);
                if (slot.Tag.Length == 0 && key.Edges.All(e => e is null) && key.Grounds.Distinct().Count() == 1)
                    throw new NotSupportedException($"{room.Path}: plain slots of an injected room (0x141fe89e0's fill rooms) are not ported");
                var list = _store.Candidates(slot.Tag, key);
                if (list.Count == 0) throw new NotSupportedException($"{room.Path}: slot ({col}, {row}) {key} tag '{slot.Tag}' has no tile (mission tiles and the pool's tiles are not ported here)");
                if (_grid[tx, ty] is not null) continue;
                for (int tries = 0; ; tries++)
                {
                    if (tries > 4) throw new InvalidOperationException($"No tile could fit at ({tx}, {ty}) for the fill room here");
                    var (tile, to) = Weighted(list, (int)rng.Next((uint)list.Sum(c => c.Weight)));
                    var (tw, th) = Orientation.Size(to, tile.Width, tile.Height);
                    bool fits = true;
                    for (int v = 0; v < th && fits; v++)
                        for (int u = 0; u < tw && fits; u++)
                            fits = _grid.Inside(tx + u, ty + v) && _grid[tx + u, ty + v] is null;
                    if (!fits) continue;
                    _grid.Place(tile, to, tx, ty, (short)(slot.Height + height));
                    break;
                }
            }
        if (room.VertexGrounds.Length > 0)
            for (int v = 1; v < room.Height; v++)
                for (int u = 1; u < room.Width; u++)
                    if (room.VertexGrounds[(v - 1) * (room.Width - 1) + u - 1] is { } g)
                    {
                        var (dx, dy) = Orientation.MapVertex(o, u, v, room.Width, room.Height);
                        _grid.ForceGround(x + dx, y + dy, g);
                    }
        bool check = _grid.CheckGrounds;
        _grid.CheckGrounds = true;
        for (int v = 0; v <= h; v++)
            for (int u = 0; u <= w; u++)
                if (_grid.Corner(x + u, y + v).Ground is null) _grid.SetGround(x + u, y + v, ground);
        _grid.CheckGrounds = check;
    }

    /// <summary><c>0x1420eeec0</c>: the first room at a shared border position stores its slot; the sides it covers
    /// (all but the one outside it) and the accessible bit go to +0x5c, the side it lies on (not at a corner) to +0x5d.
    /// A second room's slot is merged into it (<c>0x1420ee240</c>); the key's heights then start at 0 again, the
    /// lowest going to the slot's height.</summary>
    void Border(int x, int y, int bx, int by, int height, TileKey key, string tag, bool accessible, List<string>? descriptions)
    {
        byte flags = (byte)((accessible ? 1 : 0) | (bx != 2 ? 0x10 : 0) | (bx != 1 ? 8 : 0) | (by != 2 ? 2 : 0) | (by != 1 ? 4 : 0));
        if (_borders.TryGetValue((x, y), out var b))
        {
            b.Key = Merge(b, key, height, flags, x, y);
            int low = b.Key.Heights.Min();
            for (int i = 0; i < 4; i++) b.Key.Heights[i] -= (sbyte)low;
            b.Height += low;
            b.Flags |= flags;
        }
        else _borders[(x, y)] = b = new BorderSlot { Key = key, Height = height, Flags = flags };
        if (tag.Length != 0 && !b.Tags.Contains(tag)) b.Tags.Add(tag);
        if (descriptions is { Count: > 0 }) b.Descriptions = b.Descriptions is null ? descriptions : [];
        b.Sides |= (byte)((bx, by) switch { (1, 0) => 0x10, (2, 0) => 8, (0, 1) => 2, (0, 2) => 4, _ => 0 });
    }

    /// <summary><c>0x1420f6b70</c>: the shared border slots by (x, y). A slot without tags whose key is plain
    /// (<c>0x1420ee9e0</c>) is left for the fill (<c>0x1420eec10</c>). Otherwise its footprint must be all free or all
    /// taken ("partial overlap"); where free, the sides only one room covered are merged with themselves
    /// (<c>0x1420ee240</c>, which changes nothing with this port's combiner and is skipped). Its tags are tried in
    /// <c>Next(total)</c> order, each weighted by its store total (1 without tiles), until one places
    /// (<c>0x1420f5fb0</c>); else a key that is not plain must place untagged ("Could not find matching overlap tile"),
    /// a plain one goes to the fill.</summary>
    void TiliseBorders()
    {
        foreach (var ((x, y), b) in _borders.OrderBy(e => e.Key.X).ThenBy(e => e.Key.Y))
        {
            var key = b.Key;
            if (b.Tags.Count == 0 && Plain(key))
            {
                PendingFill(x, y, key, b.Height, (b.Flags & 1) != 0);
                continue;
            }
            int taken = 0;
            for (int v = 0; v < key.Height; v++)
                for (int u = 0; u < key.Width; u++)
                    if (_grid.Inside(x + u, y + v) && _grid[x + u, y + v] is not null) taken++;
            if (taken != 0 && taken != key.Width * key.Height)
                throw new InvalidOperationException($"Failed to create overlap tile at ({x}, {y}) because of partial overlap");
            if (taken != 0) throw new NotSupportedException($"overlap tile at ({x}, {y}) over a placed tile (0x1420f6b70 with its key) is not ported");
            var tags = b.Tags.Select(t => (Tag: t, Weight: Math.Max(1, _store.Candidates(t, key).Sum(c => c.Weight)))).ToList();
            bool placed = false;
            while (tags.Count != 0 && !placed)
            {
                int r = (int)rng.Next((uint)tags.Sum(t => t.Weight)), i = 0;
                while (i < tags.Count - 1 && r >= tags[i].Weight) r -= tags[i++].Weight;
                placed = PlaceOverlap(key, tags[i].Tag, x, y, b.Height);
                // A 1 × 1 slot placed with the tag "door" is a doorway position.
                if (placed && key.Width == 1 && key.Height == 1 && tags[i].Tag == "door") AddDoorway(x, y, 1, 1);
                if (!placed) tags.RemoveAt(i);
            }
            if (placed) continue;
            if (Plain(key))
            {
                PendingFill(x, y, key, b.Height, (b.Flags & 1) != 0);
                continue;
            }
            if (!PlaceOverlap(key, "", x, y, b.Height)) throw new InvalidOperationException($"Could not find matching overlap tile at ({x}, {y})");
            b.Flags |= 0x20;
            UntaggedDoorway(x, y, key);
        }
    }

    /// <summary><c>0x1420f6b70</c> after an untagged shared tile: a 3 × 3 key with a doorway mark across a side
    /// (6 or 0x18) in its footprint is a large door slot; any other is a doorway when its position is marked across
    /// left and right (0x18) and the key is a passage from top to bottom (the same edge at top and bottom, none at the
    /// sides, grounds and heights mirrored left to right), or across top and bottom (6) and the key is one from left
    /// to right.</summary>
    void UntaggedDoorway(int x, int y, TileKey key)
    {
        if (key.Width == 3 && key.Height == 3)
        {
            for (int u = 0; u < 3; u++)
                for (int v = 0; v < 3; v++)
                    if (_doorMarks.TryGetValue((x + u, y + v), out var m) && ((m & 6) == 6 || (m & 0x18) == 0x18))
                    {
                        AddDoorway(x, y, 3, 3);
                        return;
                    }
            return;
        }
        if (!_doorMarks.TryGetValue((x, y), out var mark)) return;
        static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        var (e, g, h) = (key.Edges, key.Grounds, key.Heights);
        bool vertical = (mark & 0x18) == 0x18 && e[0] is not null && Same(e[0], e[2]) && e[1] is null && e[3] is null &&
            Same(g[0], g[3]) && Same(g[1], g[2]) && h[0] == h[3] && h[1] == h[2];
        bool horizontal = (mark & 6) == 6 && e[1] is not null && Same(e[1], e[3]) && e[2] is null && e[0] is null &&
            Same(g[1], g[0]) && Same(g[2], g[3]) && h[1] == h[0] && h[2] == h[3];
        if (!vertical && !horizontal) return;
        for (int v = 0; v < key.Height; v++)
            for (int u = 0; u < key.Width; u++)
                AddDoorway(x + u, y + v, 1, 1);
    }

    /// <summary><c>0x1420ed9f0</c> (RoomOverlap): each exit of a placed room with a door type marks its doorway on the
    /// room's outer row or column: 3 entries about the exit's middle (unit / 2 into its unit), or 1 when the room or
    /// the unit is under 5 tiles.</summary>
    void MarkDoorways(DungeonPlacement p, int x0, int y0)
    {
        int step = _unit - _overlap, w = p.Width, h = p.Height;
        int tw = step * w + _overlap, th = step * h + _overlap;
        int across = tw < 5 || _unit < 5 ? 1 : 3, down = th < 5 || _unit < 5 ? 1 : 3;
        void Mark(int x, int y, byte side) => _doorMarks[(x, y)] = (byte)(_doorMarks.GetValueOrDefault((x, y)) | side);
        for (int i = 0; i < p.Exits.Length; i++)
        {
            if (p.Exits[i].Type is null) continue;
            if (i < w)
                for (int k = 0; k < across; k++) Mark(x0 + k + step * i - across / 2 + _unit / 2, y0, 4);
            else if (i - w < h)
                for (int k = 0; k < down; k++) Mark(x0 + tw - 1, y0 + k + step * (i - w) - down / 2 + _unit / 2, 0x10);
            else if (i - h - w < w)
                for (int k = 0; k < across; k++) Mark(x0 + k + step * (2 * w - i - 1 + h) - across / 2 + _unit / 2, y0 + th - 1, 2);
            else
                for (int k = 0; k < down; k++) Mark(x0, y0 + k + step * (2 * (h + w) - i - 1) - down / 2 + _unit / 2, 8);
        }
    }

    /// <summary><c>0x1420edf80</c>: a 1 × 1 position joins the doorway of a neighbour (above, else below, else left,
    /// else right) or starts one; anything larger goes to the large door slots.</summary>
    void AddDoorway(int x, int y, int w, int h)
    {
        if (w != 1 || h != 1)
        {
            _bigDoors.Add((x, y));
            return;
        }
        int Of(int u, int v) => _grid.Inside(u, v) ? _doorwayOf[v * _grid.Width + u] : 0;
        int g = Of(x + 1, y);
        if (Of(x - 1, y) is var l and not 0) g = l;
        if (Of(x, y + 1) is var d and not 0) g = d;
        if (Of(x, y - 1) is var t and not 0) g = t;
        if (g == 0)
        {
            _doorways.Add([(x, y)]);
            g = _doorways.Count;
        }
        else _doorways[g - 1].Add((x, y));
        _doorwayOf[y * _grid.Width + x] = g;
    }

    /// <summary><c>0x1420f4010</c>: each doorway without a "door" tile yet gets one. It runs down (its first two
    /// positions share x, or every position is marked across left and right) or across; at its first lowest
    /// position, the tile there gives the key, and the door tiles with that key's edges give the sizes that fit
    /// ("No door found that fits"). <c>Next(count)</c>, then a shuffle; each size in turn, its key with that size
    /// (edge points left at none), until one has door tiles ("No matching door tile found"). A door one entry long
    /// goes to the position at the unit's middle when the unit is odd, else <c>Next(length − size + 1)</c> picks
    /// the offset; <c>Next(total)</c> the tile, placed at the height there.</summary>
    void DoorTiles()
    {
        foreach (var door in _doorways)
        {
            if (door.Any(q => _grid[q.X, q.Y] is { } t && t.Tile.Tag == "door")) continue;
            bool down = door.Count >= 2 && door[0].X == door[1].X ||
                door.All(q => _doorMarks.TryGetValue(q, out var m) && (m & 0x18) == 0x18);
            int first = 0;
            for (int i = 1; i < door.Count; i++)
                if ((down ? door[i].Y : door[i].X) < (down ? door[first].Y : door[first].X)) first = i;
            int width = down ? 1 : door.Count, height = down ? door.Count : 1;
            var (x0, y0) = door[first];
            if (_grid[x0, y0] is not { } at) continue;
            var key = at.Tile.Key.Rotate(at.Orientation);
            var all = _store.Sizes("door", key.Edges);
            if (all.Count == 0) continue;
            var sizes = all.Where(s => s.W <= width && s.H <= height).ToList();
            if (sizes.Count == 0) throw new InvalidOperationException($"No door found that fits {width}x{height} at ({x0}, {y0})");
            rng.Next((uint)sizes.Count);
            rng.Shuffle(sizes);
            List<(TileDef Tile, int Orientation, int Weight)>? list = null;
            int sw = 0, sh = 0;
            foreach (var (w, h) in sizes)
            {
                var k = key.Clone();
                k.Width = (byte)w;
                k.Height = (byte)h;
                for (int i = 0; i < 4; i++)
                {
                    byte length = (byte)(3 * (i % 2 == 0 ? w : h));
                    if (k.PointA[i] == 3) k.PointA[i] = length;
                    k.PointB[i] = length;
                }
                list = _store.Candidates("door", k);
                (sw, sh) = (w, h);
                if (list.Count != 0) break;
            }
            if (list is null || list.Count == 0) throw new InvalidOperationException($"No matching door tile found in tile list at ({x0}, {y0})");
            int size = down ? sh : sw, offset = -1;
            if (size == 1 && (_unit & 1) != 0)
                for (int i = 0; i < door.Count && offset < 0; i++)
                    if ((down ? door[i].Y - _oy : door[i].X - _ox) % (_unit - _overlap) == _unit >> 1) offset = i;
            if (offset < 0) offset = (int)rng.Next((uint)(door.Count - size + 1));
            int px = x0 + (down ? 0 : offset), py = y0 + (down ? offset : 0);
            var (tile, orientation) = Weighted(list, (int)rng.Next((uint)list.Sum(c => c.Weight)));
            _grid.Place(tile, orientation, px, py, _grid[px, py]?.HeightOffset ?? 0);
        }
    }

    /// <summary><c>0x1420f4ae0</c>: each large door slot whose entry starts an untagged tile takes a door tile of that
    /// tile's key (<c>Next(total)</c>), placed over it.</summary>
    void BigDoorTiles()
    {
        foreach (var (x, y) in _bigDoors)
        {
            if (_grid[x, y] is not { IsOrigin: true } e || e.Tile.Tag.Length != 0) continue;
            var list = _store.Candidates("door", e.Tile.Key.Rotate(e.Orientation));
            if (list.Count == 0) continue;
            var (tile, orientation) = Weighted(list, (int)rng.Next((uint)list.Sum(c => c.Weight)));
            _grid.Place(tile, orientation, x, y, e.HeightOffset, force: true);
        }
    }

    /// <summary><c>0x1420eec10</c>: the footprint's vertices without flag 2 take the height (and flag 2); each of its
    /// entries is recorded for the fill with the key's first corner ground, the room's accessible bit and the height.</summary>
    void PendingFill(int x, int y, TileKey key, int height, bool accessible = false)
    {
        for (int v = 0; v <= key.Height; v++)
            for (int u = 0; u <= key.Width; u++)
            {
                if (!_grid.InsideCorners(x + u, y + v)) continue;
                ref var c = ref _grid.Corner(x + u, y + v);
                if ((c.Flags & 2) != 0) continue;
                c.Height = (sbyte)height;
                c.Flags |= 2;
            }
        for (int v = 0; v < key.Height; v++)
            for (int u = 0; u < key.Width; u++)
                if (_grid.Inside(x + u, y + v)) _fillRecords.Add((x + u, y + v, key.Grounds[0], (short)height, accessible));
    }

    readonly List<(int X, int Y, string? Ground, short Height, bool Accessible)> _fillRecords = [];
    readonly List<PlacedRoom> _fillRooms = [];

    /// <summary><c>0x142044f30</c> → <c>0x14201db80</c>: with the master's OuterGroundType, the vertices with a ground
    /// get flag 2, then the empty entries take that ground's first fill tile (<c>0x141feffb0</c>; "any" orientation
    /// becomes 0), no draw. For a dungeon whose outer ground has no flag 1 (and master +0x161 clear, taken to be so)
    /// the heights are fixed: row by row, an empty entry with a written height at a corner takes the middle of its
    /// corners' heights (<c>0x14201ec30</c> / <c>0x14201e930</c>), the others wait for the next round ("Could not fix
    /// heights in dungeon" when a round settles none); then, up to 15 times, every outer tile (also those the fill
    /// laid) whose corners' middle differs takes it (<c>0x14201ed90</c>, <c>0x141d82d00</c>), until no tile moved by
    /// more than one level. Only 1 × 1 outer tiles are ported (<c>0x14201e4f0</c> also lays 3 × 3 ones on a grid of
    /// 3), the subgraph margins (master +0x170..+0x173) are taken as 0 and the plain height (master +0x153) as 0.</summary>
    void OuterGround(Dictionary<string, string> master, string masterPath)
    {
        if (!master.TryGetValue("OuterGroundType", out var gt)) return;
        var ground = files.Ground(TerrainText.Resolve(masterPath, gt)) ?? throw new FileNotFoundException(gt);
        // The view is the subgraph's rectangle (0x141ffb6c0): the layout inside the outer buffer, which 0x14201ffa0 fills.
        var (x0, y0, x1, y1) = _view;
        if (!_fills.TryGetValue(ground.Name, out var list) || list.Count == 0)
            throw new InvalidOperationException("No fill tile found for outer fill ground type");
        var tile = files.Tile(list[0].Path) ?? throw new FileNotFoundException(list[0].Path);
        int tw = tile.Width, th = tile.Height, vw = x1 - x0, vh = y1 - y0;
        // A 3 × 3 tile on a view whose origin and size are all 1 mod 3 goes on a grid of 3 that starts one entry out.
        int a = tw == 3 && th == 3 && x0 % 3 == 1 && y0 % 3 == 1 && vw % 3 == 1 && vh % 3 == 1 ? 1 : 0;
        // 0x14201e4f0's test: inside the view (one entry more on each side on the grid of 3) on free entries.
        bool Fits(int x, int y)
        {
            int rx = x - x0, ry = y - y0;
            if (a == 0 ? rx < 0 || ry < 0 || rx + tw > vw || ry + th > vh : rx < -1 || ry < -1 || rx + tw > vw + 1 || ry + th > vh + 1)
                return false;
            for (int v = 0; v < th; v++)
                for (int u = 0; u < tw; u++)
                    if (!_grid.Inside(x + u, y + v) || _grid[x + u, y + v] is not null) return false;
            return true;
        }
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if ((_grid.Corner(x, y).Flags & 1) != 0) _grid.Corner(x, y).Flags |= 2;
        if ((ground.Flags & 1) != 0)
        {
            for (int y = y0 - a; y < y1 - vh % th; y++)
                for (int x = x0 - a; x < x1 - vw % tw; x++)
                    if (Fits(x, y)) _grid.Place(tile, 0, x, y, 0);
            return;
        }
        // 0x14201e930: the written heights at the four corners of the tile covering the entry (1 × 1 when empty).
        int? Middle(int x, int y)
        {
            int ox = x, oy = y, w = 1, h = 1;
            if (_grid[x, y] is { } e)
            {
                (ox, oy) = (x - e.OffsetX, y - e.OffsetY);
                (w, h) = Orientation.Size(e.Orientation, e.Tile.Width, e.Tile.Height);
            }
            int lo = int.MaxValue, hi = int.MinValue;
            foreach (var (vx, vy) in new[] { (ox, oy), (ox + w, oy), (ox, oy + h), (ox + w, oy + h) })
                if ((_grid.Corner(vx, vy).Flags & 2) != 0)
                {
                    int v = _grid.HeightAt(vx, vy);
                    lo = Math.Min(lo, v);
                    hi = Math.Max(hi, v);
                }
            return lo == int.MaxValue ? null : hi - (hi - lo) / 2;
        }
        var outer = new List<(int X, int Y)>();
        var ready = new List<(int X, int Y, int H)>();
        var waiting = new List<(int X, int Y)>();
        // 0x14201ec30: only an entry on the tile's grid settles.
        void Settle((int X, int Y) c)
        {
            if ((c.X - x0 + a) % tw != 0 || (c.Y - y0 + a) % th != 0) return;
            if (Middle(c.X, c.Y) is { } m) ready.Add((c.X, c.Y, m));
            else waiting.Add(c);
        }
        // 0x14201e4f0: where the tile fits, the footprint's (w + 1) × (h + 1) vertices take the height (and flag 2),
        // then the tile goes in at it. The entry joins the outer tiles either way.
        void Lay()
        {
            foreach (var (x, y, h) in ready)
            {
                if (Fits(x, y))
                {
                    for (int v = 0; v <= th; v++)
                        for (int u = 0; u <= tw; u++)
                        {
                            ref var c = ref _grid.Corner(x + u, y + v);
                            c.Height = (sbyte)h;
                            c.Flags |= 2;
                        }
                    _grid.Place(tile, 0, x, y, (short)h);
                }
                outer.Add((x, y));
            }
            ready.Clear();
        }
        for (int y = y0 - a; y < y1; y++)
            for (int x = x0 - a; x < x1; x++)
            {
                if (!_grid.Inside(x, y)) continue;
                if (_grid[x, y] is not { } e) Settle((x, y));
                else if (e.Tile == tile) outer.Add((x, y));
            }
        while (waiting.Count != 0)
        {
            Lay();
            var before = waiting;
            waiting = [];
            foreach (var c in before) Settle(c);
            if (waiting.Count == before.Count) throw new InvalidOperationException("Could not fix heights in dungeon");
        }
        Lay();
        for (int round = 0; round < 15; round++)
        {
            bool settled = true;
            var moves = new List<(int X, int Y, int H)>();
            foreach (var (x, y) in outer)
            {
                int m = Middle(x, y) ?? 0, now = _grid[x, y]?.HeightOffset ?? 0;
                if (m == now) continue;
                moves.Add((x, y, m));
                if (Math.Abs(m - now) > 1) settled = false;
            }
            if (moves.Count == 0) break;
            foreach (var (x, y, h) in moves)
                if (_grid[x, y] is { IsOrigin: true }) _grid.SetHeight(x, y, (short)h);
            if (settled) break;
        }
    }

    /// <summary>The graph's grounds (+0xa0 top, +0xa8 bottom, +0xb0 default), the tiler's +0x30 / +0x28 / +0x20.</summary>
    (string? Top, string? Bottom, string? Default) _graphGrounds;

    /// <summary><c>0x1420f0e10</c> per room, after the room vertex grounds: every vertex of the room (with RoomOverlap,
    /// not its left or top column when the room is not at the map's left or top, nor its right or bottom column when
    /// its cell is not the map's last) without a ground takes the graph's default ground; the top ground instead when
    /// its height is the highest of the layout (<c>0x141ffc700</c>), else the bottom ground when the lowest
    /// (<c>0x141ffc7d0</c>), unless the default is impassable_ground.gt. Written without the ground checks
    /// (<c>0x141d85640</c> with map +0x10 clear). Not ported: room data +0xa with +0x13a (the room's own ground in place
    /// of the default; taken as clear) and the master's level overrides +0x146 / +0x144.</summary>
    void RoomGrounds(DungeonMap map, int step)
    {
        const string impassable = "Metadata/Terrain/impassable_ground.gt";
        int x1 = _ox + step * map.Width + map.Overlap, y1 = _oy + step * map.Height + map.Overlap;
        int hi = int.MinValue, lo = int.MaxValue;
        for (int y = _oy; y <= y1; y++)
            for (int x = _ox; x <= x1; x++)
            {
                int h = _grid.InsideCorners(x, y) ? _grid.Corner(x, y).Height : 0;
                hi = Math.Max(hi, h);
                lo = Math.Min(lo, h);
            }
        bool check = _grid.CheckGrounds;
        _grid.CheckGrounds = false;
        var (top, bottom, fallback) = _graphGrounds;
        foreach (var p in map.Rooms)
        {
            if (p.Room?.Def is not { } def) continue;
            var (w, h) = Orientation.Size(p.Orientation, def.Width, def.Height);
            int x0 = 0, y0 = 0, xn = w + 1, yn = h + 1;
            if (map.Overlap != 0)
            {
                x0 = p.X != 0 ? 1 : 0;
                y0 = p.Y != 0 ? 1 : 0;
                xn -= p.X < map.Width - 1 ? 1 : 0;
                yn -= p.Y < map.Height - 1 ? 1 : 0;
            }
            int ox = _ox + step * p.X, oy = _oy + step * p.Y;
            for (int v = y0; v < yn; v++)
                for (int u = x0; u < xn; u++)
                {
                    int x = ox + u, y = oy + v;
                    if (!_grid.InsideCorners(x, y)) continue;
                    ref var c = ref _grid.Corner(x, y);
                    if ((c.Flags & 1) != 0 && c.Ground is not null) continue;
                    string? g = fallback;
                    if (!string.Equals(g, impassable, StringComparison.OrdinalIgnoreCase))
                    {
                        if (top is not null && c.Height == hi) g = top;
                        else if (bottom is not null && c.Height == lo) g = bottom;
                    }
                    _grid.SetGround(x, y, g);
                }
        }
        _grid.CheckGrounds = check;
    }

    /// <summary><c>0x1420f3480</c>'s fill: the records sorted by (x, y) (<c>0x14212d520</c>); each still-empty one is
    /// filled (<c>0x1420f3de0</c> → <c>0x141feb970</c>) for its ground, or, without one, its top-left vertex's ground
    /// when that vertex has flag 1. Not ported before it: <c>0x1420f1590</c>, <c>0x1420f2340</c>, <c>0x1420f3100</c>
    /// (unique, chest and pre-unique rooms; no draws in the 1_1_9a trace) and the overlay passes.</summary>
    void FillRecords(Dictionary<string, string> master, string masterPath)
    {
        if (!master.TryGetValue("FillTiles", out var gft)) return;
        var fill = new Fill(files, rng, _grid, 0, 0, _grid.Width, _grid.Height, _store, files.FillTiles(TerrainText.Resolve(masterPath, gft)), _fillRooms, notes);
        foreach (var (x, y, ground, height, accessible) in _fillRecords.OrderBy(r => r.X).ThenBy(r => r.Y))
        {
            if (_grid[x, y] is not null) continue;
            var g = ground ?? ((_grid.Corner(x, y).Flags & 1) != 0 ? _grid.Corner(x, y).Ground : null);
            fill.FillEntry(x, y, g, accessible, height);
        }
    }

    /// <summary><c>0x1420ee9e0</c>: no edges, every corner the first corner's ground (grounds left out of keys count as
    /// none), and a height only on a corner whose ground has no flag 1.</summary>
    bool Plain(TileKey key)
    {
        string? G(int i) => key.Grounds[i] is { } g && files.Ground(g) is { IgnoredInKeys: true } ? null : key.Grounds[i];
        for (int i = 0; i < 4; i++)
        {
            if (key.Heights[i] != 0 && (key.Grounds[i] is null || ((files.Ground(key.Grounds[i])?.Flags ?? 0) & 1) != 0)) return false;
            if (key.Edges[i] is not null) return false;
            if (i != 0 && !string.Equals(G(i), G(0), StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    /// <summary><c>0x1420f5fb0</c>: the tiles of the key and tag, <c>Next(total weight)</c>, placed at the slot's height
    /// and then set to it (<c>0x141d82d00</c>, vertices included). Without a direct match, in order until one is found:
    /// the movable sides' points go to the middle one side at a time (<c>0x1420f69c0</c>; a side is movable when its
    /// edge is not virtual (.et flag 4) nor N2 (flag 8), its point is not the middle (3 × length / 2) and the slot
    /// beyond it, at y − size, x + w, y + h or x − size (.et +0x4b), is a shared border slot (master +0x162 taken as
    /// clear)); then, when the key has a height, from the key again: one corner whose ground lacks flag 1 at a time at
    /// height 0 (<c>0x1420f6ab0</c>), the points again, both again; all those corners at 0, the points again; and when
    /// the other corners have one height, all at 0 with that height added to the tile's. A tile placed with moved
    /// points (or for a neighbour) is forced; then the neighbours across the moved sides take the middle point too
    /// (<c>0x1420f5b20</c>). <paramref name="adjacent"/> is that call's flag: no point moves, no neighbours.
    /// Not ported: the descriptions' objects (<c>0x141d83f10</c>) and the edge-type records of <c>0x1420f8df0</c>.</summary>
    bool PlaceOverlap(TileKey key, string tag, int x, int y, int height, bool adjacent = false)
    {
        var work = key.Clone();
        var moved = new List<int>();
        var found = _store.Candidates(tag, work);
        if (found.Count == 0)
        {
            byte[] middle = [(byte)(key.Width * 3 >> 1), (byte)(key.Height * 3 >> 1)];
            var movable = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                if (files.Edge(key.Edges[i]) is not { } et || et.Virtual || et.N2 || key.PointA[i] == middle[i & 1]) continue;
                var (nx, ny) = Beyond(key, i, x, y, et.Size);
                if ((nx, ny) == (x, y) || !_borders.ContainsKey((nx, ny))) continue;
                movable.Add(i);
            }
            bool canMove = movable.Count != 0 && !adjacent;
            bool Flag1(string? g) => g is null || ((files.Ground(g)?.Flags ?? 0) & 1) != 0;
            bool MovePoints()
            {
                foreach (int i in movable)
                {
                    work.PointA[i] = middle[i & 1];
                    moved.Add(i);
                    if ((found = _store.Candidates(tag, work)).Count != 0) return true;
                }
                return false;
            }
            bool FlattenOne()
            {
                for (int c = 0; c < 4; c++)
                {
                    if (Flag1(work.Grounds[c])) continue;
                    var old = work.Heights[c];
                    work.Heights[c] = 0;
                    if ((found = _store.Candidates(tag, work)).Count != 0) return true;
                    work.Heights[c] = old;
                }
                return false;
            }
            if (canMove) MovePoints();
            if (found.Count == 0 && key.Heights.Any(h => h != 0))
            {
                work = key.Clone();
                moved.Clear();
                bool ok = FlattenOne();
                if (!ok && canMove)
                {
                    ok = MovePoints() || FlattenOne();
                    if (!ok)
                    {
                        work = key.Clone();
                        moved.Clear();
                    }
                }
                if (!ok)
                {
                    var heights = new SortedSet<sbyte>();
                    for (int c = 0; c < 4; c++)
                        if (Flag1(work.Grounds[c])) heights.Add(work.Heights[c]);
                        else work.Heights[c] = 0;
                    found = _store.Candidates(tag, work);
                    if (found.Count == 0 && canMove) MovePoints();
                    if (found.Count == 0 && heights.Count == 1)
                    {
                        Array.Clear(work.Heights);
                        height += heights.Min;
                        found = _store.Candidates(tag, work);
                    }
                }
            }
            if (found.Count == 0) return false;
        }
        var (tile, orientation) = Weighted(found, (int)rng.Next((uint)found.Sum(c => c.Weight)));
        _grid.Place(tile, orientation, x, y, (short)height, force: moved.Count != 0 || adjacent);
        _grid.SetHeight(x, y, (short)height);
        if (!adjacent) Adjacent(work, x, y, moved);
        return true;
    }

    /// <summary>The slot beyond side i of a key at (x, y) (<c>0x1420f5fb0</c>, <c>0x1420f5b20</c>): above and to the
    /// left by the edge's size, to the right and below by the key's.</summary>
    static (int X, int Y) Beyond(TileKey key, int side, int x, int y, int size) => side switch
    {
        0 => (x, y - size),
        1 => (x + key.Width, y),
        2 => (x, y + key.Height),
        _ => (x - size, y),
    };

    /// <summary><c>0x1420f5b20</c>: for each side whose point moved, the slot beyond it: a shared border slot not
    /// placed untagged (flag 0x20) takes the middle point on its facing side when that side has the same edge and
    /// length; else a placed tile there whose turned key has them and another point is placed again with the middle
    /// point (grounds left out of keys dropped, its tag, its height, forced, <see cref="PlaceOverlap"/> with the flag),
    /// or "Could not find matching adjacent tile".</summary>
    void Adjacent(TileKey key, int x, int y, List<int> moved)
    {
        foreach (int i in moved)
        {
            if (key.Edges[i] is not { } edge) continue;
            var (nx, ny) = Beyond(key, i, x, y, files.Edge(edge)?.Size ?? 0);
            if (!_grid.Inside(nx, ny)) continue;
            int facing = (i + 2) & 3, length = (i & 1) == 0 ? key.Width : key.Height;
            byte middle = (byte)(length * 3 >> 1);
            bool Same(TileKey k) => ((facing & 1) == 0 ? k.Width : k.Height) == length &&
                string.Equals(k.Edges[facing], edge, StringComparison.OrdinalIgnoreCase);
            if (_borders.TryGetValue((nx, ny), out var b) && (b.Flags & 0x20) == 0)
            {
                if (Same(b.Key)) b.Key.PointA[facing] = middle;
                continue;
            }
            if (_grid[nx, ny] is not { } e) continue;
            var turned = e.Tile.Key.Rotate(e.Orientation);
            if (!Same(turned) || turned.PointA[facing] == middle) continue;
            for (int c = 0; c < 4; c++)
                if (files.Ground(turned.Grounds[c]) is { IgnoredInKeys: true }) turned.Grounds[c] = null;
            turned.PointA[facing] = middle;
            if (!PlaceOverlap(turned, e.Tile.Tag, nx, ny, e.HeightOffset, adjacent: true))
                throw new InvalidOperationException($"Could not find matching adjacent tile at ({nx}, {ny})");
        }
    }

    /// <summary><c>0x1420ee240</c>: the stored key with the new slot's parts where the new room covers them. A corner
    /// both of whose sides it covers (<c>0x1420ee460</c>; masks top left 8|4, top right 0x10|4, bottom right 0x10|2,
    /// bottom left 8|2) takes its ground (none for a ground left out of keys) and its height, relative to the stored
    /// slot's. A side it covers (<c>0x1420ee900</c>; top 4, right 0x10, bottom 2, left 8) takes its edge and points, or,
    /// when the stored slot covers it too, the two edges combined (<c>0x1420ee580</c>) and the side is marked shared.
    /// Points stay in order.</summary>
    TileKey Merge(BorderSlot b, TileKey key, int height, byte flags, int x, int y)
    {
        var old = b.Key;
        if (key.Width != old.Width || key.Height != old.Height)
            throw new InvalidOperationException($"Cannot combine tile keys of different sizes at ({x}, {y})");
        var k = old.Clone();
        ReadOnlySpan<byte> corners = [0xc, 0x14, 0x12, 0xa], sides = [4, 0x10, 2, 8];
        for (int i = 0; i < 4; i++)
        {
            if ((flags & corners[i]) != corners[i]) continue;
            var g = key.Grounds[i];
            k.Grounds[i] = g is null || files.Ground(g) is { IgnoredInKeys: true } ? null : g;
            k.Heights[i] = (sbyte)(key.Heights[i] - b.Height + height);
        }
        for (int i = 0; i < 4; i++)
        {
            if ((flags & sides[i]) == 0) continue;
            if ((b.Flags & sides[i]) == 0)
            {
                k.PointA[i] = key.PointA[i];
                k.PointB[i] = key.PointB[i];
                k.Edges[i] = key.Edges[i];
            }
            else
            {
                CombineSide(old, key, k, i);
                b.Sides |= sides[i];
            }
            if (k.PointB[i] < k.PointA[i]) (k.PointA[i], k.PointB[i]) = (k.PointB[i], k.PointA[i]);
        }
        return k;
    }

    /// <summary><c>0x1420ee580</c>: two edges on one side become the master's EdgeCombination's (.ecf) combination for
    /// the height step along the side (<c>0x1420f9880</c>): one edge alone stays, two without a combination give none.
    /// No edge: points at the side's length (3 per tile); one: its points; two: each point where both agree, else the
    /// middle.</summary>
    void CombineSide(TileKey old, TileKey key, TileKey k, int i)
    {
        string? a = old.Edges[i], b = key.Edges[i];
        if (a is null && b is null) return;
        string? r;
        if (_combiner is null)
        {
            if (a is not null && b is not null) throw new InvalidOperationException("No EdgeTypeCombiner (.ecf) specified");
            r = a ?? b;
        }
        else r = _combiner.Combine(a, b, Math.Abs(k.Heights[i] - k.Heights[(i + 1) & 3]));
        byte length = (byte)(((i & 1) == 0 ? k.Width : k.Height) * 3);
        k.Edges[i] = r;
        if (r is null) k.PointA[i] = k.PointB[i] = length;
        else if (a is null) (k.PointA[i], k.PointB[i]) = (key.PointA[i], key.PointB[i]);
        else if (b is null) (k.PointA[i], k.PointB[i]) = (old.PointA[i], old.PointB[i]);
        else
        {
            k.PointA[i] = key.PointA[i] == old.PointA[i] ? key.PointA[i] : (byte)(length >> 1);
            k.PointB[i] = key.PointB[i] == old.PointB[i] ? key.PointB[i] : (byte)(length >> 1);
        }
    }

    /// <summary>The grid position of a room slot's tile: the turned footprint's top-left corner.</summary>
    static (int X, int Y) SlotOrigin(int code, int col, int row, int w, int h, int roomW, int roomH)
    {
        int minX = int.MaxValue, minY = int.MaxValue;
        foreach (var (cx, cy) in new[] { (col, row), (col + w - 1, row), (col, row + h - 1), (col + w - 1, row + h - 1) })
        {
            var (gx, gy) = Orientation.Map(code, cx, cy, roomW, roomH);
            minX = Math.Min(minX, gx);
            minY = Math.Min(minY, gy);
        }
        return (minX, minY);
    }

    static (TileDef Tile, int Orientation) Weighted(List<(TileDef Tile, int Orientation, int Weight)> list, int r)
    {
        foreach (var c in list)
        {
            if (r < c.Weight) return (c.Tile, c.Orientation);
            r -= c.Weight;
        }
        return (list[^1].Tile, list[^1].Orientation);
    }

    /// <summary><c>0x1420f3480</c>'s fill: the slots left for it and every other empty entry ("default" ground), sorted
    /// by (x, y) (<c>0x14212d520</c>), each filled from the master's fill tiles for its ground (<c>0x141feb970</c>):
    /// <c>Next(total weight)</c>, then the orientation (<c>0x141fecd00</c>).</summary>
    void Fill()
    {
        var cells = new List<(int X, int Y, string? Ground, short Height)>(_pendingFills);
        for (int y = 0; y < _grid.Height; y++)
            for (int x = 0; x < _grid.Width; x++)
                if (_grid[x, y] is null && !_pendingFills.Any(p => p.X == x && p.Y == y)) cells.Add((x, y, null, 0));
        foreach (var (x, y, ground, height) in cells.OrderBy(c => c.X).ThenBy(c => c.Y))
        {
            if (_grid[x, y] is not null) continue;
            string name = ground is null ? "default" : files.Ground(ground)?.Name ?? "default";
            if (!_fills.TryGetValue(name, out var list) || list.Count == 0) throw new InvalidOperationException($"no fill tiles for ground '{name}' at ({x}, {y})");
            int r = (int)rng.Next((uint)list.Sum(e => e.Weight));
            var entry = list.First(e => (r -= e.Weight) < 0);
            var tile = files.Tile(entry.Path) ?? throw new FileNotFoundException(entry.Path);
            if (tile.Width != tile.Height) throw new NotSupportedException($"fill tile {tile.Path} is not square (0x141fecd00's mask pick is not ported)");
            if (!Fits(tile, x, y)) throw new NotSupportedException($"fill tile {tile.Path} does not fit at ({x}, {y}) (the retry is not ported)");
            int orientation = (int)rng.Next(8u);
            _grid.Place(tile, orientation, x, y, height);
        }
    }

    bool Fits(TileDef tile, int x, int y)
    {
        for (int v = 0; v < tile.Height; v++)
            for (int u = 0; u < tile.Width; u++)
                if (!_grid.Inside(x + u, y + v) || _grid[x + u, y + v] is not null) return false;
        return true;
    }
}

/// <summary>The tiles of a master's TileSet, indexed the way <c>0x141d81590</c> files them: for each tile in list
/// order and each orientation 0..7, under (tag, key turned by the orientation).</summary>
sealed class TileStore
{
    readonly Dictionary<(string Tag, TileKey Key), List<(TileDef Tile, int Orientation, int Weight)>> _byKey = [];
    readonly HashSet<(string Tag, string? Top, string? Right, string? Bottom, string? Left)> _edges = [];
    readonly Dictionary<(string Tag, string? Top, string? Right, string? Bottom, string? Left), List<(int W, int H)>> _sizes = [];
    readonly Dictionary<(string? Top, string? Right, string? Bottom, string? Left, int W, int H), List<(TileDef Tile, int Orientation, int Weight)>> _bySize = [];

    readonly TerrainCache _files;

    /// <summary>The TileSet's post-unique tiles (tag "postunique", +0x80): not filed, placed by <c>0x14203ece0</c>
    /// with their orientation mask.</summary>
    public readonly List<(TileDef Tile, int Mask)> PostUnique = [];

    public TileStore(TerrainCache files, IEnumerable<(int Weight, string Path, int Mask)> tiles)
    {
        _files = files;
        foreach (var (weight, path, lineMask) in tiles)
        {
            if (files.Tile(path) is not { } tile) continue;
            // 0x141d81590: the line's orientations, kept to the tile's own restriction mask (tile data +0x171).
            int mask = tile.AllowedOrientations(lineMask);
            if (tile.Tag == "postunique")
            {
                PostUnique.Add((tile, mask));
                continue;
            }
            for (int o = 0; o < 8; o++)
            {
                if ((mask & 1 << o) == 0) continue;
                var key = (tile.Tag, tile.Key.Rotate(o));
                if (!_byKey.TryGetValue(key, out var list)) _byKey[key] = list = [];
                list.Add((tile, o, weight));
                var k = key.Item2.Edges;
                _edges.Add((tile.Tag, k[0], k[1], k[2], k[3]));
                var sizes = _sizes.TryGetValue((tile.Tag, k[0], k[1], k[2], k[3]), out var sl) ? sl : _sizes[(tile.Tag, k[0], k[1], k[2], k[3])] = [];
                var size = ((int)key.Item2.Width, (int)key.Item2.Height);
                if (!sizes.Contains(size)) sizes.Add(size);
                var bySize = (k[0], k[1], k[2], k[3], size.Item1, size.Item2);
                if (!_bySize.TryGetValue(bySize, out var bl)) _bySize[bySize] = bl = [];
                bl.Add((tile, o, weight));
            }
        }
    }

    /// <summary><c>0x141d81f70</c>: the tiles (with their orientation) whose key is this one, its grounds left out of
    /// keys dropped first (<c>0x141d813c0</c>). With none, the corners whose ground has no flag 1 lose their height
    /// and the lookup repeats.</summary>
    public List<(TileDef Tile, int Orientation, int Weight)> Candidates(string tag, TileKey key)
    {
        var k = key.Clone();
        for (int c = 0; c < 4; c++)
            if (_files.Ground(k.Grounds[c]) is { IgnoredInKeys: true }) k.Grounds[c] = null;
        if (_byKey.TryGetValue((tag, k), out var list) && list.Count != 0) return list;
        bool changed = false;
        for (int c = 0; c < 4; c++)
            if (k.Heights[c] != 0 && k.Grounds[c] is { } g && ((_files.Ground(g)?.Flags ?? 0) & 1) == 0)
            {
                k.Heights[c] = 0;
                changed = true;
            }
        return changed ? Candidates(tag, k) : [];
    }

    /// <summary>Some tile has the tag (the tag map holds it).</summary>
    public bool HasTag(string tag) => _byKey.Keys.Any(k => k.Tag == tag);

    /// <summary><c>0x141d811c0</c> found sizes: some tile of the tag has these edges (top, right, bottom, left) in some
    /// orientation.</summary>
    public bool HasEdges(string tag, string?[] edges) => _edges.Contains((tag, edges[0], edges[1], edges[2], edges[3]));

    /// <summary><c>0x141d811c0</c>: the sizes of the tag's tiles (in any orientation) with these edges.</summary>
    public IReadOnlyList<(int W, int H)> Sizes(string tag, string?[] edges) =>
        _sizes.TryGetValue((tag, edges[0], edges[1], edges[2], edges[3]), out var l) ? l : [];

    /// <summary><c>0x141d82280</c>: every tile of any tag, in each orientation, with these edges and this size.</summary>
    public List<(TileDef Tile, int Orientation, int Weight)>? ByEdgesAndSize(string?[] edges, int w, int h) =>
        _bySize.TryGetValue((edges[0], edges[1], edges[2], edges[3], w, h), out var l) ? l : null;
}

/// <summary>A master's EdgeCombination (.ecf, <c>0x1420f9880</c>'s map at master +0x208): what two edge types on one
/// side of a shared border tile become. Read as "version", then lines of three quoted paths (a, b, result; "" = none);
/// the client keys its map by the height step and a flag too, which this reader does not know yet.</summary>
sealed class EdgeCombiner
{
    readonly Dictionary<(string, string), string?> _pairs = [];

    public static EdgeCombiner Parse(string text, string relativeTo)
    {
        var c = new EdgeCombiner();
        foreach (var line in TerrainText.Lines(text))
        {
            var t = TerrainText.Tokenize(line).Where(x => x.Quoted).ToList();
            if (t.Count < 3) continue;
            string a = TerrainText.Resolve(relativeTo, t[0].Text), b = TerrainText.Resolve(relativeTo, t[1].Text);
            string? r = t[2].Text.Length == 0 ? null : TerrainText.Resolve(relativeTo, t[2].Text);
            c._pairs[(a.ToLowerInvariant(), b.ToLowerInvariant())] = r;
        }
        return c;
    }

    /// <summary>The combination of a and b (either order), else b without a, a without b, none for two.</summary>
    public string? Combine(string? a, string? b, int heightStep)
    {
        if (a is not null && b is not null)
        {
            if (_pairs.TryGetValue((a.ToLowerInvariant(), b.ToLowerInvariant()), out var r)) return r;
            if (_pairs.TryGetValue((b.ToLowerInvariant(), a.ToLowerInvariant()), out r)) return r;
            return null;
        }
        return a ?? b;
    }
}
