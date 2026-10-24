namespace GameServer.Data.Terrain.Generation;

/// <summary>A tile graph (.tgr) subgraph: <c>0x14203f890</c> prepares the graph (tag choices, exist rolls, removals),
/// then <c>0x142116600</c> lays it out (Voronoi jitter first) on a view of the tile map: the primary subgraph on a map
/// of its own, a dungeon's subgraph at its record's corner on the shared map. Ported in client order, stage by stage;
/// the next stage that is not ported throws <see cref="NotSupportedException"/>.</summary>
sealed class TileGraphGenerator(TerrainCache files, LevelRandom rng, List<string> notes, bool vaalSideArea,
    Action<string, TileGraph>? onStage = null)
{
    public TileGraph Graph { get; private set; } = null!;

    /// <summary>The discoverable hideout rolled (generator +0xb).</summary>
    public bool Hideout { get; init; }

    /// <summary>The Labyrinth's graph pass, for a Labyrinth area.</summary>
    public LabyrinthPass? Labyrinth { get; init; }

    TileGrid? _grid;
    int _ox, _oy;

    /// <summary>The rooms placed in the subgraph (special nodes, overlays, fill rooms), in placing order.</summary>
    public List<PlacedRoom> Rooms { get; private set; } = [];

    /// <summary>The primary subgraph's record (the whole map) once <see cref="Generate"/> has run.</summary>
    public List<DoodadRecord> Records { get; private set; } = [];

    /// <summary>The subgraph's TileSet store (master +0x228), for the stages after the layout.</summary>
    public TileStore Store { get; private set; } = null!;

    /// <summary>The ground <c>0x1420289a0</c> floods the unwalkable border with (the string at 0x1435c4c50).</summary>
    internal const string ImpassableGround = "Metadata/Terrain/impassable_ground.gt";

    /// <summary>The finished grid. A stage that is not ported throws <see cref="PartialGenerationException"/> with the
    /// grid so far once the grid exists.</summary>
    public TileGrid Generate(string graphPath, int orientation)
    {
        try
        {
            return Layout(graphPath, orientation);
        }
        catch (NotSupportedException e) when (e is not PartialGenerationException && _grid is not null)
        {
            throw new PartialGenerationException(e.Message, _grid);
        }
    }

    /// <summary>The primary subgraph on a tile map of its own: the graph's size plus the outer buffer on every side,
    /// graph positions at the buffer; after the layout the post-unique tiles, the outer buffer and StoreyHeight.</summary>
    TileGrid Layout(string graphPath, int orientation)
    {
        var (masterPath, master, fills) = Stages(graphPath, orientation, null, 0, 0, primary: true);
        var grid = _grid!;
        int buffer = _ox;
        // 0x14203ece0: the TileSet's post-unique tiles.
        PostUniqueTiles.Run(rng, grid, buffer, buffer, Graph.Width, Graph.Height, Store, notes);
        // 0x142049d10 → 0x142044f30: per subgraph the outer ground (0x14201db80, needs the master's OuterGroundType) and
        // the outer buffer (0x14201ffa0).
        if (master.ContainsKey("OuterGroundType")) throw new NotSupportedException("OuterGroundType (0x14201db80) is not ported");
        if (buffer != 0)
            new OuterBuffer(files, rng, grid, buffer, buffer, buffer + Graph.Width, buffer + Graph.Height, Store, fills, notes).Run();
        // 0x142044f30 then runs the walkability pass (0x1420289a0) again for the primary record, over its whole
        // rectangle (the buffer included), and 0x142028870 flags the map's border vertices (the pass's border flags).
        // Only grounds and vertex flags change: the walkability grids read flag 8.
        new GroundStages(grid, 0, 0, grid.Width, grid.Height, files).Walkability(ImpassableGround, files.HasTransition);
        // 0x14204cbd0 (after the layout 0x142049d10): every tile origin with a height offset has it multiplied by the
        // master's StoreyHeight (+0x14a, default 0), row by row (0x141d83060). Levels become the client's units here.
        int storey = int.Parse(master.GetValueOrDefault("StoreyHeight", "0"));
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                if (grid[x, y] is { IsOrigin: true, HeightOffset: not 0 }) grid.ScaleHeight(x, y, storey);
        // The tile grid is final here (0x1420289a0 / 0x142028870 set grounds and entry flags only); the doodads
        // (0x1420425d0) run over the record afterwards (DoodadStage).
        Records = [new DoodadRecord(0, 0, grid.Width, grid.Height, master, masterPath, Rooms)];
        return grid;
    }

    /// <summary><c>0x14203f890</c> for a subgraph on a shared tile map (a dungeon's graph node, <c>0x14204f030</c>): the
    /// layout on the view at (<paramref name="x"/>, <paramref name="y"/>) of the graph's size. Map +0x10 (ground checks)
    /// goes on first. The walkability pass runs for the primary subgraphs and for a master that sets CountAsPrimary
    /// (taken to be +0x164). The post-unique tiles, outer ground and buffer and StoreyHeight are the caller's
    /// (<c>0x1420452e0</c>, <c>0x142044f30</c>, <c>0x14204cbd0</c>).</summary>
    internal (string MasterPath, Dictionary<string, string> Master, Dictionary<string, List<(int Weight, string Path)>> Fills) Subgraph(
        string graphPath, int orientation, TileGrid grid, int x, int y, bool primary)
    {
        grid.CheckGrounds = true;
        try
        {
            return Stages(graphPath, orientation, grid, x, y, primary);
        }
        catch (NotSupportedException e) when (e is not PartialGenerationException)
        {
            throw new PartialGenerationException(e.Message, grid);
        }
    }

    /// <summary><c>0x14203f890</c> → <c>0x142116600</c> on the view at (<paramref name="x"/>, <paramref name="y"/>) of
    /// <paramref name="shared"/>, or on a grid of its own (the view at the outer buffer) when null.</summary>
    (string MasterPath, Dictionary<string, string> Master, Dictionary<string, List<(int Weight, string Path)>> Fills) Stages(
        string graphPath, int orientation, TileGrid? shared, int x, int y, bool primary)
    {
        Graph = TileGraph.Parse(files.Text(graphPath) ?? throw new FileNotFoundException(graphPath));
        if (orientation != 0) throw new NotSupportedException($"turning a tile graph (orientation {orientation}) is not ported");
        Prepare(Graph, rng, vaalSideArea, Hideout, primary, Labyrinth);
        notes.Add($"graph {graphPath}: {Graph.Nodes.Count} nodes, {Graph.Edges.Count} edges after exist rolls");
        var cells = new VoronoiCells(Graph);
        cells.Jitter(rng);

        string masterPath = TerrainText.Resolve(graphPath, Graph.MasterFile);
        var master = TerrainText.KeyValues(files.Text(masterPath) ?? throw new FileNotFoundException(masterPath));
        TileGrid grid;
        if (shared is null)
        {
            // The tile map has the graph's size plus the outer buffer on every side; graph positions sit at the buffer.
            int buffer = int.Parse(master.GetValueOrDefault("OuterBufferSize", "0"));
            grid = new TileGrid(Graph.Width + 2 * buffer, Graph.Height + 2 * buffer) { GroundFlags = g => files.Ground(g)?.Flags ?? 0, Unmirrored = files.Unmirrored };
            (_ox, _oy) = (buffer, buffer);
        }
        else
        {
            grid = shared;
            (_ox, _oy) = (x, y);
            if (x < 0 || y < 0 || x + Graph.Width > grid.Width || y + Graph.Height > grid.Height)
                throw new InvalidOperationException($"Subgraph {graphPath} extends outside the TileMap!");
        }
        _grid = grid;
        var store = Store = new TileStore(files, files.TileList(TerrainText.Resolve(masterPath, master["TileSet"])));
        var rooms = new RoomSetIndex(files, TerrainText.Resolve(masterPath, master["RoomSet"]));
        var special = new SpecialNodes(files, rng, notes, Graph, grid, _ox, _oy, store, rooms) { BeforeRelax = () => onStage?.Invoke("relax", Graph) };
        special.Run();
        Rooms = special.Rooms;
        onStage?.Invoke("special", Graph);
        // Edges whose type is a wildcard leave the graph (0x141f4f130).
        foreach (var e in Graph.Edges.Where(e => files.Edge(e.Type)?.Wildcard == true).ToList()) Graph.RemoveEdge(e);
        var router = new EdgeRouter(Graph, grid, _ox, _oy, rng, e => files.Edge(e.Type) ?? throw new FileNotFoundException(e.Type));
        // 0x142122d90: the special stage's failed nodes, then plain nodes with more than two edges, are exploded.
        foreach (var n in special.Failed) special.Explode(n);
        foreach (var n in special.Plain.Where(n => n.Degree > 2)) special.Explode(n);
        onStage?.Invoke("beforeRoute", Graph);
        router.Run();
        onStage?.Invoke("route", Graph);
        new EdgeShaping(Graph, e => files.Edge(e.Type) ?? throw new FileNotFoundException(e.Type)).Run();
        onStage?.Invoke("corners", Graph);
        foreach (var n in special.Failed)
        {
            special.TileLessVirtual(n);
            onStage?.Invoke("virtualNode", Graph);
        }
        new EdgeSplitting(Graph, rng, e => files.Edge(e.Type) ?? throw new FileNotFoundException(e.Type), special.Plain,
            t => files.Edge(t) ?? throw new FileNotFoundException(t)).Run();
        onStage?.Invoke("split", Graph);
        new LinkMerge(Graph, special.Plain).Run();
        onStage?.Invoke("merge", Graph);
        // 0x142003bc0, then another corner rounding (0x14210aeb0, up to five rounds), then the plain nodes.
        Graph.Nodes = [.. Graph.Nodes.OrderByDescending(n => n.Priority)];
        var shaping = new EdgeShaping(Graph, e => files.Edge(e.Type) ?? throw new FileNotFoundException(e.Type));
        shaping.RoundAll();
        special.RunPlain();
        onStage?.Invoke("plain", Graph);
        // 0x142114ea0: every node's edge slots move to the front, in order; then another corner rounding.
        foreach (var n in Graph.Nodes)
        {
            var edges = n.Slots.Where(e => e is not null).ToArray();
            for (int i = 0; i < 4; i++) n.Slots[i] = i < edges.Length ? edges[i] : null;
        }
        shaping.RoundAll();
        onStage?.Invoke("beforeTiling", Graph);
        // The edge types whose tiles get their open sides filled (0x141feed20) are listed in the FillTiles object
        // (master +0x218, +0x140); a .gft that names edge types is not ported.
        string? gftPath = master.TryGetValue("FillTiles", out var gft) ? TerrainText.Resolve(masterPath, gft) : null;
        if (gftPath is not null && (files.Text(gftPath) ?? "").Contains(".et\"", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"{gftPath}: fill tiles with edge types (FillTiles +0x140, 0x141feed20) are not ported");
        List<EdgePoint> edgePoints = [];
        var tiling = new EdgeTiling(Graph, grid, _ox, _oy, rng, e => files.Edge(e.Type) ?? throw new FileNotFoundException(e.Type), files, store, notes, edgePoints);
        tiling.Run();
        special.RunLonely();
        // The vertex grounds (0x142110a20, 0x142118470 heights, 0x142110d60, 0x141feed20).
        var grounds = new GroundStages(grid, _ox, _oy, Graph.Width, Graph.Height, files);
        grounds.Lock();
        grounds.Flood();
        var heights = new HeightLevels(grid, _ox, _oy, Graph.Width, Graph.Height, files);
        heights.Run();
        // 0x1420289a0: the primary subgraphs (generator +0x30 +9) and those whose master has +0x164.
        if (primary || master.ContainsKey("CountAsPrimary")) grounds.Walkability(ImpassableGround, files.HasTransition);
        // 0x142110d60's levels: the master's +0x146 / +0x144 when set (not ported: no ported master sets them), else
        // the view's highest and lowest vertex heights, 0 without heights.
        var levels = grid.HasHeights ? heights.Range() : (0, 0);
        string? GraphGround(string g) => g.Length == 0 ? null : TerrainText.Resolve(graphPath, g);
        // The graph's top ground (+0xa0) goes on the highest level, its bottom ground (+0xa8) on the lowest, its
        // default ground (+0xb0) on every vertex still without one.
        LevelGenerator.PaintStarting?.Invoke(grid, _ox, _oy, Graph.Width, Graph.Height);
        grounds.FillUnset(GraphGround(Graph.DefaultGround), GraphGround(Graph.TopGround), GraphGround(Graph.BottomGround), levels);
        grounds.EdgePoints(tiling.EdgePointTiles, edgePoints);
        grounds.Unlock();
        // +0x90 / +0x94 = the levels: from here on the fill object's listener (0x141fef240) repaints the level grounds
        // under every tile that room tilising and the fill place.
        var painter = grounds.LevelPainter(GraphGround(Graph.TopGround), GraphGround(Graph.BottomGround), levels);
        special.TilePlaced = painter;
        // 0x142115390 (master +0x92 off, area unique rooms), 0x142115cc0 (chests), 0x142115140 / 0x1421162d0
        // (pre-unique rooms): none draws for the ported areas; not ported.
        // 0x141ffe240, then the first overlay pass (0x141fff1a0 with bit 1).
        var passes = master.TryGetValue("Overlays", out var toy) ? Overlays.Parse(files, TerrainText.Resolve(masterPath, toy)) : [];
        var overlays = new Overlays(files, rng, notes, grid, _ox, _oy, Graph.Width, Graph.Height, special, [.. special.Rooms],
            int.Parse(master.GetValueOrDefault("RoomOverlap", "1")));
        foreach (var pass in passes.Where(p => (p.Passes & 1) != 0)) overlays.Run(pass, afterFill: false);
        onStage?.Invoke("overlays", Graph);
        // 0x141fef070, then the second overlay pass (bit 2, after the fill).
        if (gftPath is null) throw new InvalidOperationException("Cannot tilise blank: Fill tile list is not specified");
        LevelGenerator.FillStarting?.Invoke(grid, _ox, _oy, Graph.Width, Graph.Height);
        var fills = files.FillTiles(gftPath);
        new Fill(files, rng, grid, _ox, _oy, Graph.Width, Graph.Height, store, fills, special.Rooms, notes) { TilePlaced = painter }.Run();
        foreach (var pass in passes.Where(p => (p.Passes & 2) != 0)) overlays.Run(pass, afterFill: true);
        // 0x1421193d0 (storeys: plain tiles take their neighbours' heights), then the mirror seams (0x142025440).
        new Storeys(grid, _ox, _oy, Graph.Width, Graph.Height, files).Run();
        new Seams(files, rng, grid, _ox, _oy, Graph.Width, Graph.Height, store, special.Rooms, special.RoomVertices,
            (GraphGround(Graph.TopGround), GraphGround(Graph.BottomGround), GraphGround(Graph.DefaultGround)), notes).Run();
        return (masterPath, master, fills);
    }

    /// <summary><c>0x14203f890</c> up to the layout (the dungeon graphs' <c>0x14203c900</c> makes the same draws), for
    /// the subgraph record <paramref name="primary"/> says is the first (the passes that take the record index).</summary>
    internal static void Prepare(TileGraph graph, LevelRandom rng, bool vaalSideArea, bool hideout, bool primary, LabyrinthPass? labyrinth = null)
    {
        foreach (var n in graph.Nodes)
        {
            // 0x14203a600: a random offset inside the node's rectangle.
            if (n.OffsetX1 != n.OffsetX2 || n.OffsetY1 != n.OffsetY2 || n.OffsetX1 != 0 || n.OffsetY1 != 0)
                throw new NotSupportedException("node random offsets (0x14203a600) are not ported");
            // 0x14203a180 → 0x142036490: a node whose "league=" names another league than the instance's (world
            // +0xd0) is dropped; the port has no instance league. "shuffle…" and "unique…" are read after the exist
            // rolls (ShuffleGroups, UniqueGroups), "subarea=…" when the record is added (LevelGenerator.SubAreas).
            if (n.Descriptions.Any(d => d.StartsWith("league=", StringComparison.OrdinalIgnoreCase)))
                throw new NotSupportedException($"{n}: league= nodes (0x142036490, the instance's league) are not ported");
            if (n.Tag == "league") n.Exist = 0;
        }
        // 0x14203a480: a tag listing alternatives "a,b,c" keeps one of them.
        foreach (var n in graph.Nodes)
        {
            if (!n.Tag.Contains(',')) continue;
            var parts = n.Tag.Split(',');
            if (parts.Length > 1) n.Tag = parts[(int)rng.Next((uint)parts.Length)];
        }
        SideAreas(graph, rng, vaalSideArea);
        // 0x142032760 / 0x142033e30: the Labyrinth's pass (secret nodes, the gold door, boss overrides).
        labyrinth?.Run(graph, rng);
        // 0x142035350 / 0x142034e60, for the first record only: a node with "subarea=hideout…" or
        // "subareatype=Hideout…" is dropped (exist 0, no descriptions) when no discoverable hideout rolled; with one,
        // that description names the hideout's claim subarea instead (Hideouts.dat; no draw or tile reads it, left as
        // it is).
        if (primary && !hideout)
            foreach (var n in graph.Nodes)
                if (n.Descriptions.Any(d => d.StartsWith("subarea=hideout", StringComparison.Ordinal) || d.StartsWith("subareatype=Hideout", StringComparison.Ordinal)))
                {
                    n.Exist = 0;
                    n.Descriptions.Clear();
                }
        // 0x142031390: exist rolls, nodes then edges; failed edges go first, then failed nodes.
        var nodesOut = new List<GraphNode>();
        foreach (var n in graph.Nodes)
        {
            if (n.Exist != 0)
            {
                if (n.Exist > 99) continue;
                if (rng.Next(100u) < (uint)n.Exist) continue;
            }
            nodesOut.Add(n);
        }
        var edgesOut = new List<GraphEdge>();
        foreach (var e in graph.Edges)
            if (e.Exist == 0 || rng.Next(100u) >= (uint)e.Exist) edgesOut.Add(e);
        foreach (var e in edgesOut) graph.RemoveEdge(e);
        foreach (var n in nodesOut) graph.RemoveNode(n);
        ShuffleGroups(graph, rng);
        UniqueGroups(graph, rng);
        // 0x142003bc0: stable sort by priority, highest first.
        graph.Nodes = [.. graph.Nodes.OrderByDescending(n => n.Priority)];
    }

    /// <summary><c>0x142030790</c> after the exist rolls, with <c>0x14203a720</c>: a node's first description starting
    /// "shuffle" puts it in group N (the number after it, none = 0; not a number or below 0: no group). Per group in
    /// order of N, while it holds two or more: <c>Next(size)</c> twice, both nodes leave the group, and two different
    /// nodes swap tags (+0x20) and descriptions (+8).</summary>
    static void ShuffleGroups(TileGraph graph, LevelRandom rng)
    {
        var groups = new List<List<GraphNode>>();
        foreach (var n in graph.Nodes)
        {
            var d = n.Descriptions.FirstOrDefault(d => d.StartsWith("shuffle", StringComparison.Ordinal));
            if (d is null) continue;
            int g = d.Length == 7 ? 0 : LeadingInt(d[7..]) ?? -1;
            if (g < 0) continue;
            while (groups.Count <= g) groups.Add([]);
            groups[g].Add(n);
        }
        foreach (var group in groups)
            while (group.Count > 1)
            {
                var a = group[(int)rng.Next((uint)group.Count)];
                var b = group[(int)rng.Next((uint)group.Count)];
                group.Remove(a);
                group.Remove(b);
                if (a == b) continue;
                (a.Tag, b.Tag) = (b.Tag, a.Tag);
                (a.Descriptions, b.Descriptions) = (b.Descriptions, a.Descriptions);
            }
    }

    /// <summary><c>0x142030790</c> after the shuffle groups, with <c>0x142031100</c> / <c>0x142035c70</c>: a node's first
    /// description starting "unique" reads "unique[letter][N][#count]" (none = group 0, count 1; N not a number: no
    /// group; a count that is not a number: 1). Per group in order of N, keeping the largest count: with more nodes than
    /// the count, <c>Next(size)</c> takes one to keep, count times, and the nodes left are removed
    /// (<c>0x142035f10</c>, as a failed node). Lettered groups (keep count of the letter's N groups, shared across the
    /// level's records) are not ported.</summary>
    static void UniqueGroups(TileGraph graph, LevelRandom rng)
    {
        var groups = new List<(int Count, List<GraphNode> Nodes)>();
        foreach (var n in graph.Nodes)
        {
            var d = n.Descriptions.FirstOrDefault(d => d.StartsWith("unique", StringComparison.Ordinal));
            if (d is null) continue;
            int count = 1, g = 0;
            if (d.Length > 6)
            {
                string rest = d[6..];
                if (char.IsAsciiLetter(rest[0])) throw new NotSupportedException($"{n}: lettered unique groups ('{d}', 0x142030790) are not ported");
                int hash = rest.IndexOf('#');
                if (hash >= 0)
                {
                    if (hash + 1 < rest.Length) count = LeadingInt(rest[(hash + 1)..]) ?? 1;
                    rest = rest[..hash];
                }
                if (LeadingInt(rest) is not { } v || v < 0) continue;
                g = v;
            }
            while (groups.Count <= g) groups.Add((0, []));
            groups[g] = (Math.Max(groups[g].Count, count), groups[g].Nodes);
            groups[g].Nodes.Add(n);
        }
        foreach (var (count, nodes) in groups)
        {
            if (count >= nodes.Count) continue;
            for (int k = 0; k < count; k++) nodes.RemoveAt((int)rng.Next((uint)nodes.Count));
            foreach (var n in nodes) graph.RemoveNode(n);
        }
    }

    /// <summary>A number as <c>std::wistream &gt;&gt; int</c> reads it: leading blanks, a sign, digits; null when none.</summary>
    static int? LeadingInt(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        int start = i;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
        int digits = i;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
        return i > digits && int.TryParse(s[start..i], out int v) ? v : null;
    }

    /// <summary><c>0x142032320</c>: nodes with a description "sidearea&lt;N&gt;" (the first such, N from '1'; none = 1) are
    /// Vaal side area entrances, grouped by N. When the area rolled a Vaal side area (generator +0xa), one node of a
    /// random group keeps its entrance: <c>Next(groups)</c>, <c>Next(group size)</c> (unused), the group shuffled, its
    /// first node loses its "unique…" descriptions, gets exist 100 and "accessible". Every other grouped node gets exist
    /// 0 with its descriptions cleared, so the exist rolls remove it (a tagged one stays as a plain node).</summary>
    static void SideAreas(TileGraph graph, LevelRandom rng, bool vaalSideArea)
    {
        var groups = new List<List<GraphNode>>();
        foreach (var n in graph.Nodes)
        {
            var d = n.Descriptions.FirstOrDefault(d => d.Length > 7 && d.StartsWith("sidearea", StringComparison.Ordinal));
            if (d is null) continue;
            int g = d.Length < 9 ? 0 : d[8] - '1';
            if (g < 0) throw new NotSupportedException($"{n}: side area group '{d}'");
            while (groups.Count <= g) groups.Add([]);
            groups[g].Add(n);
        }
        groups.RemoveAll(g => g.Count == 0);
        if (groups.Count == 0)
        {
            // The generator then adds the VaalSideArea extra terrain feature row itself (not ported).
            if (vaalSideArea) throw new NotSupportedException("a Vaal side area without sidearea nodes is not ported");
            return;
        }
        if (vaalSideArea)
        {
            var group = groups[(int)rng.Next((uint)groups.Count)];
            rng.Next((uint)group.Count);
            rng.Shuffle(group);
            var keep = group[0];
            keep.Descriptions.RemoveAll(d => d.StartsWith("unique", StringComparison.Ordinal));
            keep.Exist = 100;
            keep.Descriptions.Add("accessible");
            group.RemoveAt(0);
        }
        foreach (var n in groups.SelectMany(g => g))
        {
            n.Exist = 0;
            n.Descriptions.Clear();
        }
    }
}

/// <summary>Generation stopped at a part that is not ported; <see cref="Grid"/> holds the tiles placed so far.</summary>
public sealed class PartialGenerationException(string message, TileGrid grid) : NotSupportedException(message)
{
    public TileGrid Grid { get; } = grid;
}
