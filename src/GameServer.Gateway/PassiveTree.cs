using System.Text.Json;

/// <summary>
/// The passive skill tree's nodes: PassiveSkills.dat rows by PassiveSkillGraphId (the id the client allocates and S→C
/// 0x3d lists), with their stats (Stats.dat rows + 1 = the client's stat keys) and the skill points they grant. Game data:
/// data/tables/PassiveSkills.json, loaded on first use. OrdinaryPassiveRules validates ordinary connectivity against
/// the selected generated PSG topology; AscendancyRules validates the separate ascendancy allocation.
/// Masteries: PassiveSkillMasteryGroups.dat groups a mastery node's options and PassiveSkillMasteryEffects.dat holds the
/// effects' stats. The client's 0x3d list carries the selected effects as node | effect &lt;&lt; 16; the effect is the
/// effect row's HASH16 (the value in the client's passive tree data), not its data-table row index.
/// Gold respec (Bestel's Respecialisation): a node costs VillageBalancePerLevelShared.dat's GoldRespecCost at the
/// character's level times the node's multiplier, as the client's per-node cost 0x141efb140 computes it.
/// </summary>
sealed class PassiveTree(string folder = "data", bool hardMode = false)
{
    public sealed record Node(ushort GraphId, string Id, string Name, IReadOnlyList<(uint Key, int Value)> Stats, int PointsGranted,
        bool IsAscendancy, bool IsStart, int MasteryGroup, int RespecMultiplier = 1, bool IsKeystone = false,
        bool IsNotable = false, bool IsJewelSocket = false, bool IsProxy = false, bool IsExpansion = false)
    {
        public bool IsMastery => MasteryGroup >= 0;
    }

    public sealed record MasteryEffect(int Row, ushort Hash, string Id, IReadOnlyList<(uint Key, int Value)> Stats);

    private readonly Lazy<Dictionary<ushort, Node>> _nodes = new(() => LoadNodes(folder, hardMode));
    private readonly Lazy<Dictionary<int, ushort>> _rows = new(() => LoadRows(folder));
    private readonly Lazy<Dictionary<string, Node>> _nodesById = new(() =>
        LoadNodes(folder, hardMode).Values.GroupBy(n => n.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal));
    private readonly Lazy<Dictionary<int, MasteryEffect>> _effects = new(() => LoadEffects(folder));
    private readonly Lazy<Dictionary<int, List<MasteryEffect>>> _groups = new(() => LoadGroups(folder, LoadEffects(folder)));
    private readonly Lazy<Dictionary<int, int>> _respecCosts = new(() => LoadRespecCosts(folder));
    private readonly Lazy<JewelSockets> _jewelSockets = new(() => LoadJewelSockets(folder));
    private readonly Lazy<Dictionary<ushort, (double X, double Y)>> _positions = new(() => LoadPositions(folder));

    /// <summary>The PassiveJewels1 pane cells: the ordinary sockets (0..20, six of which also take large cluster
    /// jewels), the cluster expansion cells and the parent/child links between them.</summary>
    public sealed record JewelSockets(Dictionary<int, ushort> BySlot, Dictionary<ushort, int> ByNode,
        HashSet<int> Large, Dictionary<int, ushort> Cluster, Dictionary<int, int[]> Children, Dictionary<int, int> Parents,
        Dictionary<int, int> Sizes, Dictionary<int, int[]> Starts, Dictionary<int, int> ClusterIndexes);

    public Node? Find(ushort graphId) => _nodes.Value.GetValueOrDefault(graphId);

    /// <summary>The graph id of a PassiveSkills.dat row (the 0-based table row used by the client's keystone
    /// tables), or null.</summary>
    public ushort? GraphIdForRow(int row) =>
        _rows.Value.TryGetValue(row, out ushort graphId) ? graphId : null;

    /// <summary>The node with this PassiveSkills.dat Id (<c>affliction_notable_*</c> …), or null.</summary>
    public Node? FindById(string id) => _nodesById.Value.GetValueOrDefault(id);

    /// <summary>The PassiveJewels1 pane slot (0..59) of a normal passive tree jewel socket, or null. Cluster expansion
    /// rows and the three ascendancy charm sockets are not ordinary jewel sockets.</summary>
    public ushort? JewelNodeForSlot(int slot) =>
        _jewelSockets.Value.BySlot.TryGetValue(slot, out ushort node) ? node : null;

    /// <summary>The PassiveJewels1 pane slot that holds the jewel of this passive node, or null when it is not a normal
    /// jewel socket.</summary>
    public int? JewelSlotForNode(ushort graphId) =>
        _jewelSockets.Value.ByNode.TryGetValue(graphId, out int slot) ? slot : null;

    public bool IsJewelSocket(ushort graphId) => _jewelSockets.Value.ByNode.ContainsKey(graphId);

    /// <summary>A tree socket that accepts a large cluster jewel (PassiveJewelSlots' ClusterJewelSize 2), not only a
    /// normal jewel.</summary>
    public bool IsLargeJewelSlot(int slot) => _jewelSockets.Value.Large.Contains(slot);

    /// <summary>The expansion pane cell's node (the cluster jewel's own socket/notable/small nodes), or null for an
    /// ordinary cell.</summary>
    public ushort? ClusterNodeForSlot(int slot) =>
        _jewelSockets.Value.Cluster.TryGetValue(slot, out ushort node) ? node : null;

    /// <summary>The expansion pane cells a socket creates, in cluster index order (a large cluster jewel's three
    /// medium sockets, a medium one's single small socket); empty for the rest.</summary>
    public IReadOnlyList<int> ClusterSlots(int slot) =>
        _jewelSockets.Value.Children.TryGetValue(slot, out var children) ? children : [];

    /// <summary>The pane cell whose cluster jewel creates this expansion cell, or null for a tree socket.</summary>
    public int? ClusterParent(int cell) =>
        _jewelSockets.Value.Parents.TryGetValue(cell, out int parent) ? parent : null;

    /// <summary>The cluster jewel size the pane cell takes: 2 large, 1 medium, 0 small, -1 for a basic tree socket that
    /// takes normal jewels only.</summary>
    public int ClusterCellSize(int cell) =>
        _jewelSockets.Value.Sizes.TryGetValue(cell, out int size) ? size : -1;

    /// <summary>PassiveJewelSlots.dat's StartIndices for the cell: the orbit offset the client applies to the
    /// cluster template for each size (0 small, 1 medium, 2 large), i.e. the proxy node's orbit position.</summary>
    public IReadOnlyList<int> ClusterStartIndices(int cell) =>
        _jewelSockets.Value.Starts.TryGetValue(cell, out int[]? starts) ? starts : [];

    /// <summary>PassiveJewelSlots.dat's ClusterIndex for the cell: the position among the cell's siblings (0-based),
    /// which the client turns into the cluster graph's id block.</summary>
    public int ClusterIndex(int cell) =>
        _jewelSockets.Value.ClusterIndexes.TryGetValue(cell, out int index) ? index : 0;

    /// <summary>The first id of a tree socket's cluster jewel block: the six tree sockets take 12 ids each from 135 in
    /// ClusterIndex order (measured: cell 20, ClusterIndex 5, uses 195..206). Null for an expansion cell, whose block is
    /// not known, so its ids are the ones outside these blocks.</summary>
    public int? ClusterBlockBase(int cell) =>
        IsLargeJewelSlot(cell) ? 135 + 12 * ClusterIndex(cell) : null;

    /// <summary>The client's socket node ids of a tree socket's cluster jewel block, for the first `count` sockets: the
    /// large template's SocketIndices (4, 8, 6) moved by the cell's StartIndices for that size (cell 20: 206 and 198).
    /// The six tree sockets only take large jewels, so the template is fixed.</summary>
    public IReadOnlyList<ushort> ClusterSocketIds(int cell, int count)
    {
        if (ClusterBlockBase(cell) is not int block) return [];
        var starts = ClusterStartIndices(cell);
        int start = starts.Count > 2 ? starts[2] : 0;
        int[] template = [4, 8, 6];
        var ids = new List<ushort>();
        for (int i = 0; i < Math.Min(count, template.Length); i++)
            ids.Add((ushort)(block + (template[i] + start) % 12));
        return ids;
    }

    public bool Loaded => _nodes.Value.Count > 0;

    /// <summary>Whether the extracted passive graph carries group and orbit geometry (passive-graphs v5).</summary>
    public bool HasGeometry => _positions.Value.Count > 0;

    /// <summary>The client's tree position of a node: its group's centre plus its orbit radius at the orbit angle. Null
    /// for a node the graph does not carry (cluster expansion nodes are not in the ordinary PSG).</summary>
    public (double X, double Y)? Position(ushort graphId) =>
        _positions.Value.TryGetValue(graphId, out var position) ? position : null;

    /// <summary>The squared distance between two tree nodes, or null when either has no graph position.</summary>
    public double? DistanceSquared(ushort from, ushort to) =>
        Position(from) is { } a && Position(to) is { } b
            ? (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) : null;

    /// <summary>All graph nodes within this radius of a socket node (the socket itself excluded). The client's jewel
    /// radius check is <c>distanceSquared &lt;= radiusSquared</c>; filtering to the passive types a jewel may conquer is
    /// left to the caller.</summary>
    public IReadOnlyList<ushort> NodesInRadius(ushort socket, double radius)
    {
        if (Position(socket) is not { } centre) return [];
        double radiusSquared = radius * radius;
        var result = new List<ushort>();
        foreach (var (id, position) in _positions.Value)
        {
            if (id == socket) continue;
            double dx = position.X - centre.X, dy = position.Y - centre.Y;
            if (dx * dx + dy * dy <= radiusSquared) result.Add(id);
        }
        return result;
    }

    /// <summary>All graph nodes in an annulus around a socket node: Path of Building's Thread of Hope check is
    /// <c>innerSquared &lt;= distanceSquared &lt;= outerSquared</c>. The socket itself is excluded.</summary>
    public IReadOnlyList<ushort> NodesInRing(ushort socket, double inner, double outer)
    {
        if (Position(socket) is not { } centre) return [];
        double innerSquared = inner * inner, outerSquared = outer * outer;
        var result = new List<ushort>();
        foreach (var (id, position) in _positions.Value)
        {
            if (id == socket) continue;
            double dx = position.X - centre.X, dy = position.Y - centre.Y;
            double distanceSquared = dx * dx + dy * dy;
            if (distanceSquared >= innerSquared && distanceSquared <= outerSquared) result.Add(id);
        }
        return result;
    }

    /// <summary>The effects a mastery node offers, in the table's order (the client shows them in this order).</summary>
    public IReadOnlyList<MasteryEffect> EffectsFor(ushort graphId) =>
        Find(graphId) is { MasteryGroup: >= 0 } node && _groups.Value.TryGetValue(node.MasteryGroup, out var effects)
            ? effects : [];

    /// <summary>The effect by the client's HASH16, falling back to the data-table row index.</summary>
    public MasteryEffect? FindEffect(int id) =>
        _effects.Value.Values.FirstOrDefault(e => e.Hash == id) ?? _effects.Value.GetValueOrDefault(id);

    /// <summary>The skill points the allocated nodes grant themselves (the client counts them as extra points).</summary>
    public int PointsGranted(IEnumerable<ushort> nodes) => nodes.Sum(n => Find(n)?.PointsGranted ?? 0);

    /// <summary>
    /// The gold a respec of these nodes costs at the character's level (client 0x141efb140): GoldRespecCost of the
    /// VillageBalancePerLevelShared.dat row for the level, times each node's multiplier (1; 0 for a multiple-choice
    /// node; an expansion node's own value). Ascendancy nodes are not part of a gold respec (the client's full cost
    /// skips them too). Unknown nodes cost nothing.
    /// </summary>
    public long GoldRespecCost(IEnumerable<ushort> nodes, int level)
    {
        long perNode = _respecCosts.Value.GetValueOrDefault(level);
        return nodes.Sum(n => Find(n) is { IsAscendancy: false } node ? perNode * node.RespecMultiplier : 0);
    }

    /// <summary>The allocated nodes' stats, with their selected mastery effects, summed by key.</summary>
    public Dictionary<uint, int> Stats(IEnumerable<ushort> nodes, IReadOnlyDictionary<ushort, ushort>? masteries = null)
    {
        var sum = new Dictionary<uint, int>();
        foreach (var n in nodes)
        {
            if (Find(n) is { } node)
                foreach (var (k, v) in node.Stats) sum[k] = ArcaneMight.Combine(k, sum.GetValueOrDefault(k), v);
            if (masteries is not null && masteries.TryGetValue(n, out var effect) && FindEffect(effect) is { } mastery)
                foreach (var (k, v) in mastery.Stats) sum[k] = ArcaneMight.Combine(k, sum.GetValueOrDefault(k), v);
        }
        return sum;
    }

    /// <summary>Whether the effect belongs to the mastery node's group (the client only offers those).</summary>
    public bool IsEffectFor(ushort graphId, MasteryEffect effect) =>
        EffectsFor(graphId).Any(e => e.Row == effect.Row && e.Hash == effect.Hash);

    static bool Flag(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static Dictionary<int, ushort> LoadRows(string folder)
    {
        var rows = new Dictionary<int, ushort>();
        string path = Path.Combine(folder, "tables", "PassiveSkills.json");
        if (!File.Exists(path)) return rows;
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        int row = 0;
        foreach (var r in d.RootElement.EnumerateArray())
        {
            int graphId = r.GetProperty("PassiveSkillGraphId").GetInt32();
            if (graphId is > 0 and <= ushort.MaxValue) rows[row] = (ushort)graphId;
            row++;
        }
        return rows;
    }

    private static Dictionary<ushort, Node> LoadNodes(string folder, bool hardMode)
    {
        string path = Path.Combine(folder, "tables", "PassiveSkills.json");
        var nodes = new Dictionary<ushort, Node>();
        if (!File.Exists(path)) return nodes;
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var r in d.RootElement.EnumerateArray())
        {
            int graphId = r.GetProperty("PassiveSkillGraphId").GetInt32();
            if (graphId is <= 0 or > ushort.MaxValue) continue;
            // The mode flag is per catalog, never global: regular and HardMode characters may be online together.
            // A marked alternate replaces the entire vector, including an empty vector or explicit zero values.
            bool alternate = hardMode && r.TryGetProperty("HasHardmodeAlternate", out var hardmode) && hardmode.GetBoolean();
            var keys = r.GetProperty(alternate ? "StatsHardmode" : "Stats").EnumerateArray().Select(s => s.GetInt32()).ToList();
            var stats = new List<(uint, int)>();
            for (int i = 0; i < keys.Count && i < 5; i++)
                stats.Add(((uint)keys[i] + 1, r.GetProperty($"Stat{i + 1}Value{(alternate ? "Hardmode" : "")}").GetInt32()));
            int masteryGroup = r.TryGetProperty("MasteryGroup", out var group) && group.ValueKind == JsonValueKind.Number
                ? group.GetInt32() : -1;
            // The client's multiplier: an expansion node's own value (unnamed column at +0xb2), 0 for a multiple-choice node.
            int respec = Flag(r, "IsExpansion") ? r.GetProperty("__col26").GetInt32()
                : Flag(r, "IsMultipleChoice") ? 0 : 1;
            string name = r.GetProperty("Name").GetString() ?? "";
            if (alternate && r.GetProperty("NameHardmode").GetString() is { Length: > 0 } alternateName) name = alternateName;
            nodes.TryAdd((ushort)graphId, new Node((ushort)graphId, r.GetProperty("Id").GetString() ?? "", name,
                stats, r.GetProperty(alternate ? "SkillPointsGrantedHardmode" : "SkillPointsGranted").GetInt32(), r.GetProperty("AscendancyKey").ValueKind == JsonValueKind.Number,
                r.GetProperty("Characters").GetArrayLength() > 0, masteryGroup, respec,
                IsKeystone: Flag(r, "IsKeystone"),
                IsNotable: Flag(r, "IsNotable"),
                IsJewelSocket: Flag(r, "IsJewelSocket"),
                IsProxy: Flag(r, "IsProxyPassive"),
                IsExpansion: Flag(r, "IsExpansion")));
        }
        return nodes;
    }

    // The client's tree constants (Path of Building's TreeData/3_29/tree.lua): the number of positions on each orbit
    // comes from the PSG's own orbit list, and these are the orbit radii in tree units. They are the same units as
    // PassiveJewelRadii (Large = 1500), which is why the radius check can use them directly.
    static readonly double[] OrbitRadii = [0, 82, 162, 335, 493, 662, 846];

    private static Dictionary<ushort, (double X, double Y)> LoadPositions(string folder)
    {
        var positions = new Dictionary<ushort, (double X, double Y)>();
        string path = Path.Combine(folder, AscendancyCatalog.GraphFile);
        if (!File.Exists(path)) return positions;
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        if (!d.RootElement.TryGetProperty("normal", out var normal)
            || !normal.TryGetProperty("orbits", out var orbitList)
            || !normal.TryGetProperty("groups", out var groups)) return positions;
        int[] orbits = [.. orbitList.EnumerateArray().Select(v => v.GetInt32())];
        foreach (var group in groups.EnumerateArray())
        {
            double gx = group.GetProperty("x").GetDouble(), gy = group.GetProperty("y").GetDouble();
            foreach (var node in group.GetProperty("nodes").EnumerateArray())
            {
                int id = node.GetProperty("id").GetInt32();
                int orbit = node.GetProperty("orbit").GetInt32();
                int index = node.GetProperty("orbitIndex").GetInt32();
                if (id is <= 0 or > ushort.MaxValue || orbit < 0 || orbit >= orbits.Length
                    || orbit >= OrbitRadii.Length || orbits[orbit] <= 0) continue;
                double angle = 2 * Math.PI * index / orbits[orbit];
                positions.TryAdd((ushort)id,
                    (gx + Math.Sin(angle) * OrbitRadii[orbit], gy - Math.Cos(angle) * OrbitRadii[orbit]));
            }
        }
        return positions;
    }

    private static Dictionary<int, int> LoadRespecCosts(string folder)
    {
        string path = Path.Combine(folder, "tables", "VillageBalancePerLevelShared.json");
        var costs = new Dictionary<int, int>();
        if (!File.Exists(path)) return costs;
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        // The client looks the row up by its first column (the level, 1–100).
        foreach (var r in d.RootElement.EnumerateArray())
            costs[r.GetProperty("MaxAccountLevel").GetInt32()] = r.GetProperty("GoldRespecCost").GetInt32();
        return costs;
    }

    /// <summary>
    /// The PassiveJewels1 pane slots (0..59) and the PassiveSkills.dat row each one belongs to. PassiveJewelSlots.dat's
    /// first column (<c>Slot</c>) is that row index; the ordinary tree's 21 jewel sockets are slots 0..20 (six of them
    /// large, which also accept a large cluster jewel), and the rows with a <c>ReplacesSlot</c> are the expansion cells
    /// a cluster jewel in the parent socket creates (large 11/12/13/15/17/20 -> three medium sockets each, a medium ->
    /// one small). The three ascendancy charm sockets (`__col6`) are not part of this pane's tree sockets.
    /// </summary>
    private static JewelSockets LoadJewelSockets(string folder)
    {
        var bySlot = new Dictionary<int, ushort>();
        var byNode = new Dictionary<ushort, int>();
        var large = new HashSet<int>();
        var cluster = new Dictionary<int, ushort>();
        var sizes = new Dictionary<int, int>();
        var children = new Dictionary<int, List<(int Index, int Cell)>>();
        var starts = new Dictionary<int, int[]>();
        var clusterIndexes = new Dictionary<int, int>();
        string slotsPath = Path.Combine(folder, "tables", "PassiveJewelSlots.json");
        string passivesPath = Path.Combine(folder, "tables", "PassiveSkills.json");
        if (!File.Exists(slotsPath) || !File.Exists(passivesPath))
            return new(bySlot, byNode, large, cluster, [], [], [], [], []);
        using var slots = JsonDocument.Parse(File.ReadAllBytes(slotsPath));
        using var passives = JsonDocument.Parse(File.ReadAllBytes(passivesPath));
        var rows = passives.RootElement.EnumerateArray().ToArray();
        int slot = 0;
        foreach (var row in slots.RootElement.EnumerateArray())
        {
            int index = slot++;
            int passiveRow = row.GetProperty("Slot").GetInt32();
            if (passiveRow < 0 || passiveRow >= rows.Length) continue;
            var passive = rows[passiveRow];
            if (!passive.GetProperty("IsJewelSocket").GetBoolean()) continue;
            int graphId = passive.GetProperty("PassiveSkillGraphId").GetInt32();
            if (graphId is <= 0 or > ushort.MaxValue) continue;
            if (row.TryGetProperty("StartIndices", out var startIndices) && startIndices.ValueKind == JsonValueKind.Array)
                starts[index] = [.. startIndices.EnumerateArray().Select(v => v.GetInt32())];
            if (row.TryGetProperty("ClusterIndex", out var clusterIndex) && clusterIndex.ValueKind == JsonValueKind.Number)
                clusterIndexes[index] = clusterIndex.GetInt32();
            var replaces = row.GetProperty("ReplacesSlot");
            if (replaces.ValueKind != JsonValueKind.Null)
            {
                cluster[index] = (ushort)graphId;
                if (row.GetProperty("ClusterJewelSize").ValueKind == JsonValueKind.Number)
                    sizes[index] = row.GetProperty("ClusterJewelSize").GetInt32();
                int parent = replaces.GetInt32();
                int order = row.GetProperty("ClusterIndex").GetInt32();
                if (!children.TryGetValue(parent, out var list)) children[parent] = list = [];
                list.Add((order, index));
                continue;
            }
            if (row.GetProperty("__col6").GetBoolean()) continue;
            bySlot[index] = (ushort)graphId;
            byNode[(ushort)graphId] = index;
            var size = row.GetProperty("ClusterJewelSize");
            int cellSize = size.ValueKind == JsonValueKind.Number ? size.GetInt32() : -1;
            sizes[index] = cellSize;
            if (cellSize == 2) large.Add(index);
        }
        return new(bySlot, byNode, large, cluster,
            children.ToDictionary(kv => kv.Key, kv => kv.Value.OrderBy(c => c.Index).Select(c => c.Cell).ToArray()),
            children.SelectMany(kv => kv.Value.Select(c => (Cell: c.Cell, Parent: kv.Key)))
                .ToDictionary(x => x.Cell, x => x.Parent),
            sizes, starts, clusterIndexes);
    }

    private static Dictionary<int, MasteryEffect> LoadEffects(string folder)
    {
        string path = Path.Combine(folder, "tables", "PassiveSkillMasteryEffects.json");
        var effects = new Dictionary<int, MasteryEffect>();
        if (!File.Exists(path)) return effects;
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        int row = 0;
        foreach (var r in d.RootElement.EnumerateArray())
        {
            var keys = r.GetProperty("Stats").EnumerateArray().Select(s => s.GetInt32()).ToList();
            var stats = new List<(uint, int)>();
            for (int i = 0; i < keys.Count && i < 3; i++)
                stats.Add(((uint)keys[i] + 1, r.GetProperty($"Stat{i + 1}Value").GetInt32()));
            int hash = r.GetProperty("HASH16").GetInt32();
            effects[row] = new MasteryEffect(row, (ushort)hash, r.GetProperty("Id").GetString() ?? "", stats);
            row++;
        }
        return effects;
    }

    private static Dictionary<int, List<MasteryEffect>> LoadGroups(string folder, Dictionary<int, MasteryEffect> effects)
    {
        string path = Path.Combine(folder, "tables", "PassiveSkillMasteryGroups.json");
        var groups = new Dictionary<int, List<MasteryEffect>>();
        if (!File.Exists(path)) return groups;
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        int row = 0;
        foreach (var r in d.RootElement.EnumerateArray())
        {
            var list = new List<MasteryEffect>();
            foreach (var e in r.GetProperty("MasteryEffects").EnumerateArray())
                if (effects.TryGetValue(e.GetInt32(), out var effect)) list.Add(effect);
            groups[row++] = list;
        }
        return groups;
    }
}
