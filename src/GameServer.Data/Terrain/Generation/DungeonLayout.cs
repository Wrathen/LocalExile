namespace GameServer.Data.Terrain.Generation;

/// <summary>One exit slot of a placed dungeon room (0x10 bytes): door type (+0, null = none), height (+8, relative to
/// the room), chirality (+9).</summary>
sealed class DungeonDoor
{
    public string? Type;
    public sbyte Height;
    public bool Chirality;
}

/// <summary>A room the dungeon layout placed (the layout's 0x60-byte entries, <c>0x1420fe780</c> / <c>0x1420fe360</c>):
/// the room (+0; null = a blank the fill made, to get a room at the end), position and size in room units (+8, +0xc,
/// +0x20, +0x24), +0x10, height level (+0x12), orientation (+0x13), kind (+0x14: 1 a graph node's room, 3 a blank),
/// flags (+0x15: 1 accessible, 2, 4 kept by the prune, 8 blank when the fill started, 0x10 teleport or entrance),
/// descriptions (+0x18), exits (+0x28) and two door types (+0x40, +0x48).</summary>
sealed class DungeonPlacement
{
    public DungeonRoom? Room;
    public int X, Y, Width, Height;
    public byte B10;
    /// <summary>+0x11: how many doors the entry had when its room was picked (<c>0x1420fe360</c>).</summary>
    public byte Doors;
    public sbyte Level;
    public int Orientation;
    public byte Kind, Flags;
    public List<string> Descriptions = [];
    public DungeonDoor[] Exits = [];
    public string? Type40, Type48;

    public override string ToString() => $"{(Room is null ? "blank" : Path.GetFileName(Room.Path))} ({X}, {Y}) {Width}x{Height}";
}

/// <summary>The layout's room-unit map (<c>0x14203c900</c>'s 0x70-byte object): size (+0x28 / +0x2c), cells (+0x40: 0
/// free, 1 reserved, k ≥ 2 = room k − 2) and rooms (+0x58).</summary>
sealed class DungeonMap(int width, int height, int unit, int overlap)
{
    /// <summary>Size in room units, the master's RoomUnitSize (+0x30, master +0x149) and RoomOverlap (+0x34, +0x14b).</summary>
    public readonly int Width = width, Height = height, Unit = unit, Overlap = overlap;
    public readonly int[] Cells = new int[width * height];
    public readonly List<DungeonPlacement> Rooms = [];

    /// <summary>A door type has a direction (its chirality counts, and mirroring a room flips it).</summary>
    public Func<string, bool> Directional = _ => false;

    public int this[int x, int y]
    {
        get => Cells[y * Width + x];
        set => Cells[y * Width + x] = value;
    }

    public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public DungeonPlacement RoomAt(int cell) => Rooms[cell - 2];

    /// <summary><c>0x1420fe780</c>: a room at (x, y) over free or reserved cells (else "Room overlap detected"), its
    /// cells set to its number; then <c>0x1420fe360</c> fills the entry.</summary>
    public DungeonPlacement Place(DungeonRoom? room, int orientation, int w, int h, int x, int y, sbyte level, List<string> descriptions,
        string? type40, bool accessible, bool flag2, byte kind, byte b10, string? type48)
    {
        for (int v = 0; v < h; v++)
            for (int u = 0; u < w; u++)
                if (this[x + u, y + v] > 1) throw new InvalidOperationException($"Room overlap detected at room ({u}, {v})");
        var p = new DungeonPlacement
        {
            Room = room, X = x, Y = y, Width = w, Height = h, B10 = b10, Level = level, Orientation = orientation, Kind = kind,
            Descriptions = [.. descriptions], Type40 = type40, Type48 = type48,
            Flags = (byte)((accessible ? 1 : 0) | (flag2 ? 2 : 0)),
        };
        p.Exits = new DungeonDoor[2 * (w + h)];
        for (int i = 0; i < p.Exits.Length; i++) p.Exits[i] = new DungeonDoor();
        if (room is not null)
        {
            if (room.Unit != Unit) throw new InvalidOperationException($"Cannot place room '{room.Path}': it has the wrong unit size");
            // 0x141d75a50 with the table at 0x142fbe348 maps a turned exit to the room's own.
            var turned = DungeonRoomSet.TurnExits(room.Exits, room.Width, room.Height, orientation, Directional);
            for (int i = 0; i < p.Exits.Length; i++)
                p.Exits[i] = new DungeonDoor { Type = turned[i].Type, Height = turned[i].Height, Chirality = turned[i].Chirality };
        }
        Rooms.Add(p);
        int k = Rooms.Count + 1;
        for (int v = 0; v < h; v++)
            for (int u = 0; u < w; u++)
                this[x + u, y + v] = k;
        return p;
    }

    /// <summary><c>0x1420fdf60</c>: the cells again from the rooms (reserved cells stay).</summary>
    public void Rebuild()
    {
        for (int i = 0; i < Cells.Length; i++)
            if (Cells[i] > 1) Cells[i] = 0;
        for (int k = 0; k < Rooms.Count; k++)
        {
            var r = Rooms[k];
            for (int v = 0; v < r.Height; v++)
                for (int u = 0; u < r.Width; u++)
                    this[r.X + u, r.Y + v] = k + 2;
        }
    }
}

/// <summary>The dungeon layout (<c>0x1421038d0</c>): a .dgr graph's rooms on a grid of room units. Its graph nodes'
/// rooms go first; then, edge group by edge group, the free cells fill with blank rooms, each edge's two rooms are
/// joined by a path of rooms and doors, and the blanks off every path go again; the blanks get rooms at the end.
/// Ported in client order; the next part that is not ported throws <see cref="NotSupportedException"/>.</summary>
sealed class DungeonLayout(TerrainCache files, LevelRandom rng, List<string> notes, TileGraph graph, DungeonRoomSet roomSet,
    DungeonMap map, int graphOrientation, (string? From, string? To) swapDoors, Action<string, int, int[]>? onCells = null,
    Func<string, IReadOnlyList<string>?>? fileGroup = null, string? outerGround = null, ExtraRoomPool? pool = null)
{
    /// <summary>The layout's +0x40: per file group, the files its graph nodes took (<c>0x1421052e0</c>).</summary>
    readonly Dictionary<string, List<string>> _groupsUsed = [];

    /// <summary>The fill's per-room state (0x30 bytes, <c>0x1420fb310</c>): impenetrable (+0), on a path (+1), the path
    /// search's predecessor (+4), extra door bias (+8), random number (+0xc, <c>Next(128)</c>), cost so far (+0x10) and
    /// estimate (+0x14), and the levels of the paths through it (+0x18, sorted).</summary>
    sealed class Info
    {
        public bool Impenetrable, Used;
        public int Pred, Bias, Random, G, H;
        public List<sbyte> Levels = [];
    }

    /// <summary>A path the connections made (0x28 bytes, per door type): its rooms from the far end, its edge and level.</summary>
    sealed record PathRecord(List<int> Rooms, GraphEdge Edge, sbyte Level);

    readonly List<Info> _info = [];
    readonly Dictionary<string, List<PathRecord>> _paths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The void edges and the other edges by priority (three slots each, each slot's edges in lists by key
    /// in key order), as <see cref="Run"/> sorts them (<c>0x142002b50</c>); a template node takes its edges out of
    /// them, leaving its key's list (empty, it still draws).</summary>
    readonly List<GraphEdge> _voids = [];
    readonly SortedDictionary<sbyte, List<List<GraphEdge>>[]> _groups = [];

    /// <summary>A room entry as levelgen prints the trace's <c>dungeon.rooms</c>: room, position, size, orientation,
    /// level, kind, flags and exits (type, height, chirality).</summary>
    internal static string RoomText(DungeonPlacement r) =>
        $"{(r.Room is null ? "blank" : Path.GetFileName(r.Room.Path))} ({r.X}, {r.Y}) {r.Width}x{r.Height} o{r.Orientation} l{r.Level} k{r.Kind} f{r.Flags} " +
        $"[{string.Join(" ", r.Exits.Select(e => e.Type is null ? "-" : $"{Path.GetFileNameWithoutExtension(e.Type)}:{e.Height}:{(e.Chirality ? 1 : 0)}"))}]";

    /// <summary>The cells at a stage, named as the trace's <c>dungeon.stages</c> (function, then "&lt;" entry or "&gt;" exit).</summary>
    void Cells(string stage) => onCells?.Invoke(stage, rng.Log?.Count ?? 0, (int[])map.Cells.Clone());

    EdgeType Edge(string path) => files.Edge(path) ?? throw new FileNotFoundException(path);

    bool Directional(string? type) => type is not null && EdgeSplitting.IsDirectional(Edge(type), Edge);

    (int X, int Y) Cell(int x, int y) => (map.Width * x / graph.CellWidth, map.Height * y / graph.CellHeight);

    public void Run()
    {
        map.Directional = t => Directional(t);
        // 0x1420127e0 → 0x142002b50: void edges to their own list, every other edge into the group of its priority:
        // rooms.et edges in its third slot, edges whose type has one room size in the first, the rest in the second.
        var voids = _voids;
        var groups = _groups;
        foreach (var e in graph.Edges)
        {
            if (e.Dungeon!.Void) { voids.Add(e); continue; }
            var key = (sbyte)e.Priority;
            if (!groups.TryGetValue(key, out var slots)) groups[key] = slots = [[], [], []];
            int slot = e.Type.Equals(DungeonRoomSet.Rooms, StringComparison.OrdinalIgnoreCase) ? 2 : roomSet.Sizes(e.Type).Count == 1 ? 0 : 1;
            var k = ListKey(e);
            int at = slots[slot].FindIndex(l => CompareKeys(ListKey(l[0]), k) >= 0);
            if (at < 0) slots[slot].Add([e]);
            else if (CompareKeys(ListKey(slots[slot][at][0]), k) == 0) slots[slot][at].Add(e);
            else slots[slot].Insert(at, [e]);
        }
        if (groups.Count > 1) throw new NotSupportedException("dungeon edges of several priorities (0x1420039e0's order) are not ported");
        // 0x142002790: the void edges by priority (a stable insertion sort).
        var byPriority = voids.OrderBy(e => (sbyte)e.Priority).ToList();
        voids.Clear();
        voids.AddRange(byPriority);
        // 0x142104710 → 0x142002460: each void edge's line through its points reserves its cells.
        foreach (var e in voids) Reserve(e);
        // 0x142101890: the tagged nodes, then the untagged ones, each by priority (highest first), take their rooms.
        foreach (var n in graph.Nodes.Where(n => n.Tag.Length != 0).OrderByDescending(n => n.Priority)
                     .Concat(graph.Nodes.Where(n => n.Tag.Length == 0).OrderByDescending(n => n.Priority)).ToList())
            PlaceNode(n);
        Teleports();
        if (groups.Values.Any(s => s.Any(lists => lists.Any(l => l.Count != 0)))) Connections(groups);
        Finish();
    }

    /// <summary><c>0x1420014a0</c> (<c>0x142002ca0</c>): a slot keys its edges by impenetrable (I) first, then the bias
    /// (+0x5c, the number before I/P; descending, unsigned), then the door type's n0 (.et +0x4c; descending), then the
    /// type's path (<c>0x142006570</c>, ordinal); edges with the same key share a list, lists in key order.</summary>
    (bool Impenetrable, uint Bias, byte N0, string Type) ListKey(GraphEdge e) =>
        (e.Dungeon!.Impenetrable, (uint)e.Dungeon.Bias, (byte)Edge(e.Type).N0, e.Type);

    static int CompareKeys((bool Impenetrable, uint Bias, byte N0, string Type) a, (bool Impenetrable, uint Bias, byte N0, string Type) b)
    {
        if (a.Impenetrable != b.Impenetrable) return a.Impenetrable ? -1 : 1;
        if (a.Bias != b.Bias) return b.Bias.CompareTo(a.Bias);
        if (a.N0 != b.N0) return b.N0.CompareTo(a.N0);
        return string.CompareOrdinal(a.Type, b.Type);
    }

    /// <summary><c>0x1421038d0</c> from the fill's state to the room picks (skipped when no edge is left to join).</summary>
    void Connections(SortedDictionary<sbyte, List<List<GraphEdge>>[]> groups)
    {

        // 0x1420fb310: the fill's state for the rooms so far, all of them on a path; their blanks get flag 8.
        Resize();
        for (int k = 0; k < map.Rooms.Count; k++)
        {
            _info[k].Used = true;
            if ((map.Rooms[k].Flags & 4) == 0 && map.Rooms[k].Room is null) map.Rooms[k].Flags |= 8;
        }
        // 0x1420028c0: every edge list (per group, slot and key; an emptied one too) shuffled, then by priority
        // (0x142012820).
        var lists = new List<List<GraphEdge>>();
        foreach (var slots in groups.Values)
            foreach (var slot in slots)
                foreach (var list in slot)
                {
                    rng.Next((uint)list.Count);
                    rng.Shuffle(list);
                    lists.Add([.. list.OrderBy(e => (sbyte)e.Priority)]);
                }
        // 0x1420f9f00 → 0x1421040c0 per list.
        foreach (var list in lists) Group(list);
        Branches();
        Cells("100d60<");
        ExtraDoors();
        Cells("100d60>");
        Cells("1012c0<");
        SwapDoors();
        Cells("1012c0>");
        // Blanks on a path lose flag 8; then each room on a path gets its room.
        for (int k = 0; k < _info.Count; k++)
            if (_info[k].Used && map.Rooms[k].Room is null) map.Rooms[k].Flags &= unchecked((byte)~8);
        for (int k = 0; k < _info.Count; k++)
            if (_info[k].Used)
            {
                Cells("pick<");
                Pick(k + 2);
                Cells("pick>");
            }
    }

    /// <summary><c>0x1421038d0</c>'s end (0x142103f31).</summary>
    void Finish()
    {
        // 0x142100910: entries without doors counted (+0x11 still 0) count the rooms they are joined to.
        for (int k = 0; k < map.Rooms.Count; k++)
        {
            var r = map.Rooms[k];
            if (r.Doors != 0) continue;
            var joined = new HashSet<DungeonPlacement>();
            for (int e = 0; e < r.Exits.Length; e++)
                if (Across(k + 2, e) is { } n && Joined(r, n)) joined.Add(n);
            r.Doors = (byte)joined.Count;
        }
        Cells("108dc0<");
        FillRooms();
        Cells("108dc0>");
        // 0x142101aa0, 0x142101e50, 0x142102170, 0x142103490: the special rooms. Only 0x142101aa0's unnumbered
        // "special" rooms are ported; the others drew nothing in the 1_1_9a and 1_1_7_1 traces.
        SpecialRooms();
        if (pool is not null)
        {
            // 0x142101e50: oversize rows go into the room set's telepad injector rooms (+0x190), not ported.
            if (pool.Rows.Any(r => r.Oversize) && roomSet.All.Any(r => r.Tag.StartsWith("telepad_injector", StringComparison.Ordinal)))
                throw new NotSupportedException("oversize extra features in telepad injector rooms (0x142101e50) are not ported");
            Inject();
            ShrineRooms();
        }
        // Reserved cells become free; then the fill rooms once more.
        for (int i = 0; i < map.Cells.Length; i++)
            if (map.Cells[i] == 1) map.Cells[i] = 0;
        FillRooms();
        Cells("done");
        LevelGenerator.DungeonRooms?.Invoke(map.Rooms.Select(RoomText).ToList());
    }

    /// <summary><c>0x142108dc0</c>: <c>0x142108f70</c> while it places a room, for the grounds with .gt flag 2 first,
    /// then for the others. (Master +0x238 / +0x240 ground overrides over a flooded region, <c>0x142108a40</c>, are
    /// not ported.)</summary>
    void FillRooms()
    {
        while (FillRoomPass(true)) { }
        while (FillRoomPass(false)) { }
    }

    /// <summary><c>0x142108f70</c>: every placed room (rooms placed during the pass included) whose two corner grounds
    /// on a side are the same (room data +0x13a, the room key's corners turned by the orientation) offers each cell
    /// across that side, at the level of the exit there, to <see cref="FillCell"/> with that ground, when the
    /// ground's .gt flag 2 equals <paramref name="flag2"/>. Sides in the order right, bottom, left, top.</summary>
    bool FillRoomPass(bool flag2)
    {
        ReadOnlySpan<byte> inverse = [0, 3, 2, 1, 4, 5, 6, 7, 8];
        ReadOnlySpan<byte> corner = [0, 1, 2, 3, 1, 2, 3, 0, 2, 3, 0, 1, 3, 0, 1, 2, 1, 0, 3, 2, 2, 1, 0, 3, 3, 2, 1, 0, 0, 3, 2, 1, 0, 1, 2, 3];
        bool placed = false;
        for (int k = 0; k < map.Rooms.Count; k++)
        {
            var p = map.Rooms[k];
            if (p.Room?.Def is not { } def) continue;
            int t = inverse[Math.Min(p.Orientation, 8)];
            var c = new string?[4];
            for (int i = 0; i < 4; i++) c[i] = def.Key.Grounds[corner[4 * t + i]];
            int w = p.Width, h = p.Height, n = p.Exits.Length;
            bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            bool Wanted(string? g) => (g is not null && (files.Ground(g)!.Flags & 2) != 0) == flag2;
            sbyte Level(int e) => (sbyte)(p.Level + p.Exits[e].Height);
            if (p.X + w < map.Width && Same(c[1], c[2]) && Wanted(c[1]))
                for (int v = 0; v < h; v++) placed |= FillCell(p.X + w, p.Y + v, c[1], Level(w + v));
            if (p.Y + h < map.Height && Same(c[2], c[3]) && Wanted(c[2]))
                for (int u = 0; u < w; u++) placed |= FillCell(p.X + u, p.Y + h, c[2], Level(2 * w + h - 1 - u));
            if (p.X != 0 && Same(c[3], c[0]) && Wanted(c[3]))
                for (int v = 0; v < h; v++) placed |= FillCell(p.X - 1, p.Y + v, c[3], Level(n - 1 - v));
            if (p.Y != 0 && Same(c[0], c[1]) && Wanted(c[0]))
                for (int u = 0; u < w; u++) placed |= FillCell(p.X + u, p.Y - 1, c[0], Level(u));
        }
        return placed;
    }

    /// <summary><c>0x1421096f0</c>: a free cell takes a fill room of the ground (room set +0x98: per ground the sizes
    /// of the rooms filed under it, <c>0x141ff5760</c>). <c>Next(total)</c> picks a size; when it fits nowhere around the
    /// cell (<c>0x142109e90</c>) that entry goes and <c>Next(total)</c> picks again until one fits or none is left.</summary>
    bool FillCell(int x, int y, string? ground, sbyte level)
    {
        if (map[x, y] != 0) return false;
        var sizes = roomSet.FillSizes(ground).ToList();
        if (sizes.Count == 0) return false;
        // Accessible: no ground or one with .gt flag 4, and not the master's OuterGroundType (+0x230, taken to be it).
        bool accessible = (ground is null || (files.Ground(ground)!.Flags & 4) != 0) &&
            !string.Equals(ground, outerGround, StringComparison.OrdinalIgnoreCase);
        var (sw, sh) = Weighted(sizes, (int)rng.Next((uint)sizes.Sum(s => s.Weight)));
        if (FillAround(x, y, sw, sh, ground, level, accessible)) return true;
        if (sizes.Count < 2) return false;
        sizes.RemoveAt(sizes.FindIndex(s => s.W == sw && s.H == sh));
        while (sizes.Count != 0)
        {
            (sw, sh) = Weighted(sizes, (int)rng.Next((uint)sizes.Sum(s => s.Weight)));
            sizes.RemoveAt(sizes.FindIndex(s => s.W == sw && s.H == sh));
            if (FillAround(x, y, sw, sh, ground, level, accessible)) return true;
        }
        return false;

        static (int W, int H) Weighted(List<(int W, int H, int Weight)> list, int r)
        {
            int i = 0;
            while (i < list.Count - 1 && r >= list[i].Weight) r -= list[i++].Weight;
            return (list[i].W, list[i].H);
        }
    }

    /// <summary><c>0x142109e90</c>: the size placed with the cell at its top left, then (for a width or height over 1)
    /// at its top right, bottom left, bottom right.</summary>
    bool FillAround(int x, int y, int w, int h, string? ground, sbyte level, bool accessible)
    {
        int left = x - w + 1, top = y - h + 1;
        if (PlaceFill(x, y, w, h, ground, level, accessible)) return true;
        if (w < 2) return h >= 2 && PlaceFill(x, top, w, h, ground, level, accessible);
        if (h < 2) return PlaceFill(left, y, w, h, ground, level, accessible);
        return PlaceFill(left, y, w, h, ground, level, accessible) || PlaceFill(x, top, w, h, ground, level, accessible) ||
            PlaceFill(left, top, w, h, ground, level, accessible);
    }

    /// <summary><c>0x142109c10</c>: inside the map on free cells (<c>0x1420fe050</c>), <c>Next(total)</c> picks a room
    /// of the ground and size (room set +0xd8, <c>0x141ff9110</c>) and <c>Next(840)</c> one of its orientations; it is
    /// placed (<c>0x1420fe780</c>) at the level as kind 6.</summary>
    bool PlaceFill(int x, int y, int w, int h, string? ground, sbyte level, bool accessible)
    {
        if (x < 0 || y < 0 || x + w > map.Width || y + h > map.Height) return false;
        for (int v = 0; v < h; v++)
            for (int u = 0; u < w; u++)
                if (map[x + u, y + v] != 0) return false;
        var rooms = roomSet.FillRooms(ground, w, h).ToList();
        int r = (int)rng.Next((uint)rooms.Sum(e => e.Weight)), i = 0;
        while (i < rooms.Count - 1 && r >= rooms[i].Weight) r -= rooms[i++].Weight;
        var (room, mask, _) = rooms[i];
        int orientation = OrientationOf(mask, rng.Next(840u));
        var (rw, rh) = (orientation & 1) != 0 && orientation < 8 ? (room.Height, room.Width) : (room.Width, room.Height);
        var p = map.Place(room, orientation, rw, rh, x, y, level, [], null, accessible, false, 6, 0, DungeonRoomSet.Rooms);
        notes.Add($"dungeon fill room: {p} o{orientation} l{level}");
        return true;
    }

    /// <summary><c>0x142101aa0</c>: room set +0x1c0 holds the special rooms by number (room data +4, <c>0x141ff6960</c>);
    /// each numbered list but the last places one of its rooms (<c>Next(count)</c>, then a shuffle; not ported), the
    /// last, taken here to be the unnumbered rooms, places each room whose chance (room data +5) passes
    /// (<c>Next(100)</c> below 100), else "Failed to place special room".</summary>
    void SpecialRooms()
    {
        var special = roomSet.All.Where(r => r.Special).ToList();
        if (special.Any(r => r.Number >= 0)) throw new NotSupportedException("numbered special rooms (0x142101aa0's lists) are not ported");
        foreach (var r in special)
        {
            if (r.Chance < 100 && (r.Chance == 0 || rng.Next(100u) >= r.Chance)) continue;
            if (PlaceSpecial([(r, r.Mask)]) is null) throw new InvalidOperationException($"Failed to place special room {r.Path}");
        }
    }

    /// <summary><c>0x142102170</c>: the pool's rows (not oversize) while the subgraph's share lasts. A row's room (one of
    /// its rooms: <c>Next(count)</c> and a shuffle when it has several, the first that goes, <c>0x142102ed0</c>) of 3 × 3
    /// tiles waits for a mission room when the room set has some (+0x130: "mission" rooms by their count of
    /// "missiontile" slots, ascending, those of the layout's unit size); any other room of the unit size replaces a
    /// placed room as a special room. Then the waiting rooms (at most the share; more is not ported) need mission rooms:
    /// per mission tile count n in turn, k = 1 … waiting / n of them (each <c>Next(count)</c>, a shuffle and
    /// <see cref="PlaceSpecial"/>) from the layout as it was, then smaller counts from the largest down and any count
    /// from the smallest up for the rest, until every waiting room has a mission tile; each then gets an orientation
    /// (<c>Next(840)</c>) and goes to the pool's injected rooms. Without such a placement, the last try's rooms stay and
    /// as many waiting rooms as it had tiles for go.</summary>
    void Inject()
    {
        var groups = roomSet.All.Where(r => r.Tag == "mission" && r.Unit == map.Unit && r.Def is not null)
            .GroupBy(r => MissionTiles(r.Def!)).OrderBy(g => g.Key).Select(g => (N: g.Key, Rooms: g.Select(r => (r, r.Mask)).ToList())).ToList();
        if (groups.Any(g => g.N == 0)) throw new InvalidDataException($"{groups.First(g => g.N == 0).Rooms[0].r.Path} contains no mission tiles");
        var waiting = new List<(DungeonRoom Room, int Mask, ExtraFeatureRow Row)>();
        var used = new List<ExtraFeatureRow>();
        // A copy of the share (local_res20) counts the rooms placed here; removing a row counts the pool's own down.
        int share = pool!.Share;
        foreach (var row in pool.Rows.ToList())
        {
            if (share == 0) break;
            if (row.Oversize) continue;
            var arms = row.Rooms.ToList();
            if (arms.Count > 1)
            {
                rng.Next((uint)arms.Count);
                rng.Shuffle(arms);
            }
            foreach (var arm in arms)
                if (Feature(arm, row)) break;
        }
        foreach (var row in used) pool.Remove(row, true);
        if (waiting.Count == 0 || share == 0) return;
        if (share < waiting.Count) throw new NotSupportedException("more extra feature rooms than the share (0x14212dec0's shuffle) is not ported");
        if (waiting.Any(w => w.Row.Tiles.Count != 0)) throw new NotSupportedException("extra feature tiles (the pool's tile store, +0x90) are not ported");

        var saved = Save();
        int left = waiting.Count;
        foreach (var g in groups)
        {
            int most = waiting.Count / g.N;
            for (int k = Math.Min(left, most); k <= most; k++)
            {
                Restore(saved);
                left = waiting.Count;
                bool ok = true;
                for (int i = 0; i < k && left >= g.N; i++)
                {
                    if (!PlaceMission(g.Rooms)) { ok = false; break; }
                    left -= g.N;
                }
                // The rest: smaller counts from the largest down while one goes, then any count from the smallest up.
                while (left != 0 && g.N > 1)
                {
                    int j = groups.FindLastIndex(o => o.N <= left && o.N <= g.N - 1 && PlaceMission(o.Rooms));
                    if (j < 0) break;
                    left -= groups[j].N;
                }
                while (left != 0)
                {
                    int j = groups.FindIndex(o => o.N <= left && PlaceMission(o.Rooms));
                    if (j < 0) break;
                    left -= groups[j].N;
                }
                if (left == 0)
                {
                    Commit(waiting.Count);
                    return;
                }
                if (!ok) break;
            }
        }
        Commit(waiting.Count - left);

        bool PlaceMission(List<(DungeonRoom, int)> rooms)
        {
            rng.Next((uint)rooms.Count);
            rng.Shuffle(rooms);
            return PlaceSpecial(rooms) is not null;
        }

        void Commit(int count)
        {
            foreach (var (room, mask, row) in waiting.Take(count))
            {
                pool.Injected.Add((room, OrientationOf(mask, rng.Next(840u)), row));
                pool.Remove(row, true);
            }
        }

        // 0x142102ed0.
        bool Feature(string arm, ExtraFeatureRow row)
        {
            var room = DungeonRoomSet.Parse(files, arm, 1, row.Mask) ?? throw new FileNotFoundException(arm);
            if (groups.Count != 0 && room.Def is { Width: 3, Height: 3 })
            {
                waiting.Add((room, row.Mask, row));
                return true;
            }
            if (room.Unit != map.Unit) return false;
            if (PlaceSpecial([(room, row.Mask)]) is null) return false;
            // 0x141d8abb0 (the pool's placed rooms, by position) is left out: nothing in the layout reads it.
            used.Add(row);
            share--;
            return true;
        }
    }

    /// <summary><c>0x142103490</c>: with shrines rolled (pool +0x148), as many tries as the count: the room set's
    /// "shrine" rooms (+0x118) of the layout's unit size (master +0x149), any orientation, go to
    /// <see cref="PlaceSpecial"/>; a placed room with a shrine object (room data +0x70, the object type taken to be
    /// Metadata/Shrines/Shrine) counts a shrine placed (<c>0x141d8b9a0</c>).</summary>
    void ShrineRooms()
    {
        var rooms = roomSet.All.Where(r => r.Tag == "shrine").ToList();
        if (rooms.Count == 0 || pool!.Shrines == 0) return;
        var list = rooms.Where(r => r.Unit == map.Unit).Select(r => (r, 0)).ToList();
        if (list.Count == 0) return;
        for (int i = 0, n = pool.Shrines; i < n; i++)
            if (PlaceSpecial(list) is { Room: { } room } && HasShrine(room.Path)) pool.ShrinePlaced();
    }

    /// <summary>The .arm's objects (its last section: x, y, …, .ao, object type) hold a shrine.</summary>
    bool HasShrine(string path) =>
        TerrainText.Lines(files.Text(path) ?? "").Select(TerrainText.Tokenize)
            .Any(l => l.Count > 2 && l[^1].Text == "Metadata/Shrines/Shrine");

    /// <summary><c>0x141ff6960</c>'s count for a "mission" room: its 'k' slots tagged "missiontile".</summary>
    static int MissionTiles(RoomDef def)
    {
        int n = 0;
        foreach (var slot in def.Slots)
            if (slot is { Kind: 'k', Tag: "missiontile" }) n++;
        return n;
    }

    /// <summary>The layout as <c>0x142102170</c> copies it before each try (the entries' room, orientation and kind, the
    /// entry count and cells).</summary>
    (List<(DungeonRoom? Room, int Orientation, byte Kind)> Entries, int[] Cells) Save() =>
        (map.Rooms.Select(p => (p.Room, p.Orientation, p.Kind)).ToList(), (int[])map.Cells.Clone());

    void Restore((List<(DungeonRoom? Room, int Orientation, byte Kind)> Entries, int[] Cells) saved)
    {
        map.Rooms.RemoveRange(saved.Entries.Count, map.Rooms.Count - saved.Entries.Count);
        for (int i = 0; i < saved.Entries.Count; i++)
            (map.Rooms[i].Room, map.Rooms[i].Orientation, map.Rooms[i].Kind) = saved.Entries[i];
        saved.Cells.CopyTo(map.Cells, 0);
    }

    /// <summary><c>0x1420ff140</c> for a list of rooms (with their orientation masks, 0 = any): an accessible placed
    /// room of no tag whose room is not special can take one when one of its orientations has the same size and exits
    /// (<c>0x1420ffb90</c>, the first such orientation). The candidates are kept by entry index (each entry with the
    /// rooms that fit it, in list order); <c>Next(entries)</c>, then <c>Next(its rooms)</c>; the entry takes the room
    /// and orientation, kind 8. Without one, the room would go over several rooms at their corners
    /// (<c>0x1420ffcf0</c>), not ported. Room data +9 (rooms paired by +0x13a) is taken to be clear.</summary>
    DungeonPlacement? PlaceSpecial(IReadOnlyList<(DungeonRoom Room, int Mask)> rooms)
    {
        var candidates = new SortedDictionary<int, List<(DungeonRoom Room, int Orientation)>>();
        foreach (var (r, mask) in rooms)
        {
            if (r.Tag.Contains("injector", StringComparison.Ordinal)) throw new NotSupportedException($"{r.Path}: injector special rooms are not ported");
            for (int i = 0; i < map.Rooms.Count; i++)
            {
                var p = map.Rooms[i];
                if ((p.Flags & 1) == 0 || p.Room is null || p.Room.Special || p.Room.Tag.Length != 0) continue;
                for (int o = 0; o < 8; o++)
                {
                    if (mask != 0 && (mask >> o & 1) == 0) continue;
                    var (w, h) = o % 2 == 1 ? (r.Height, r.Width) : (r.Width, r.Height);
                    if (w != p.Width || h != p.Height) continue;
                    if (!ExitsFit(p.Exits, DungeonRoomSet.TurnExits(r.Exits, r.Width, r.Height, o, Directional))) continue;
                    if (!candidates.TryGetValue(i, out var list)) candidates[i] = list = [];
                    list.Add((r, o));
                    break;
                }
            }
        }
        if (candidates.Count == 0) throw new NotSupportedException($"{rooms[0].Room.Path}: special rooms over several rooms (0x1420ffcf0) are not ported");
        var (index, options) = candidates.ElementAt((int)rng.Next((uint)candidates.Count));
        var (room, orientation) = options[(int)rng.Next((uint)options.Count)];
        var q = map.Rooms[index];
        q.Room = room;
        q.Orientation = orientation;
        q.Kind = 8;
        notes.Add($"dungeon special room: {q} o{orientation}");
        return q;
    }

    /// <summary><c>0x1420feff0</c>: every exit of the room equals the entry's, except that where the room's door type
    /// can end the entry may have no door; when the room has such an exit, some exit of the entry must have a door
    /// type that can end.</summary>
    bool ExitsFit(DungeonDoor[] placed, RoomExit[] room)
    {
        bool endable = false;
        int ends = 0;
        for (int i = 0; i < placed.Length; i++)
        {
            var (a, b) = (placed[i], room[i]);
            bool same = string.Equals(a.Type, b.Type, StringComparison.OrdinalIgnoreCase) && a.Height == b.Height && a.Chirality == b.Chirality;
            if (!CanEnd(b.Type))
            {
                if (!same) return false;
            }
            else
            {
                if (!same && a.Type is not null) return false;
                endable = true;
            }
            if (CanEnd(a.Type)) ends++;
        }
        return !endable || ends != 0;
    }

    /// <summary><c>0x1420fd3d0</c>: states for the new rooms; rooms with a room, and blanks with descriptions, are on a
    /// path (the second also impenetrable). Its list at +0x58 (<c>0x1420fa120</c> per cell) is not ported.</summary>
    void PrepareInfo()
    {
        Resize();
        for (int k = 0; k < map.Rooms.Count; k++)
        {
            var r = map.Rooms[k];
            if (r.Room is not null) _info[k].Used = true;
            else if (r.Descriptions.Count != 0) _info[k].Used = _info[k].Impenetrable = true;
        }
    }

    /// <summary>A room a branch may grow into (<c>0x1420fbfe0</c>'s 0x18-byte entries): the room (+0), the room it
    /// grows from (+4), the edge's second branching chance (+8), the depth left (+9), impenetrability (+0xa), level
    /// (+0xb), accessible (+0xc), impenetrable (+0xd) and extra door type (+0x10).</summary>
    sealed record Candidate(int Room, int From, int Chance, sbyte Depth, int ExtraDoor, sbyte Level, bool Accessible,
        bool Impenetrable, string? Type);

    /// <summary><c>0x1420fc690</c>: the door types of the paths, sorted, then <c>Next(count)</c> and shuffled; rooms.et
    /// last and, before it, types with one room size first (both stable). For each type with a path whose edge has a
    /// first branching chance, the free cells fill again with blanks of the type, and branches grow from its paths
    /// (<c>0x1420fca30</c>); then the prune.</summary>
    void Branches()
    {
        Cells("fc690<");
        if (_paths.Count == 0) return;
        var keys = _paths.Keys.ToList();
        keys.Sort(StringComparer.Ordinal);
        rng.Next((uint)keys.Count);
        rng.Shuffle(keys);
        bool IsRooms(string k) => k.Equals(DungeonRoomSet.Rooms, StringComparison.OrdinalIgnoreCase);
        // 0x142134760 then 0x14212db40 (on the part before rooms.et).
        keys = [.. keys.Where(k => !IsRooms(k)).OrderBy(k => roomSet.Sizes(k).Count == 1 ? 0 : 1), .. keys.Where(IsRooms)];
        foreach (var key in keys)
        {
            if (!_paths[key].Any(r => r.Edge.Dungeon!.BranchChance1 != 0)) continue;
            Cells("fill<");
            Fill(key);
            Cells("fill>");
            PrepareInfo();
            Cells("fca30<");
            Grow(key);
            Cells("fca30>");
            Cells("prune<");
            Prune();
            Cells("prune>");
        }
        Cells("fc690>");
    }

    /// <summary><c>0x1420fca30</c>: next to the end rooms of nodes that branch, the blanks learn the path's level
    /// (<c>0x1420fa120</c>); the paths' rooms offer their neighbours (<c>0x1420fc240</c>) and each is taken
    /// (<c>0x1420fcf40</c>). Then, round by round, the rooms the last ones offered are shuffled (<c>Next(count)</c> and
    /// <c>0x142129230</c>), grouped by chance (lowest first), and of each group of n the first
    /// <see cref="Share"/>(chance, n) are taken, offering theirs.</summary>
    void Grow(string key)
    {
        foreach (var rec in _paths[key])
        {
            // 0x1420fa240 for every three rooms in a row (its type and chirality arguments are taken to be the edge's).
            for (int i = 1; i + 1 < rec.Rooms.Count; i++)
                MiddleRoom(rec.Edge.Type, rec.Rooms[i + 1], rec.Rooms[i], rec.Rooms[i - 1], rec.Edge.Chirality != 0);
            if (rec.Edge.B.Branching != 0) Around(rec.Rooms[0], (x, y) => AddLevel(x, y, rec.Level));
            if (rec.Edge.A.Branching != 0) Around(rec.Rooms[^1], (x, y) => AddLevel(x, y, rec.Level));
        }
        var pending = new List<Candidate>();
        foreach (var c in Offered(key)) Take(c, key, pending);
        while (pending.Count != 0)
        {
            rng.Next((uint)pending.Count);
            rng.Shuffle(pending);
            var groups = pending.GroupBy(c => c.Chance).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
            pending = [];
            foreach (var g in groups)
            {
                int take = Share(g[0].Chance, g.Count);
                for (int i = 0; i < take; i++) Take(g[i], key, pending);
            }
        }
    }

    /// <summary>chance % of n, rounded down, plus one when <c>Next(100)</c> falls under the remainder; at most n.</summary>
    int Share(int chance, int n)
    {
        int product = Math.Min(chance, 100) * n, share = product / 100;
        if (rng.Next(100u) < product - share * 100) share++;
        return Math.Min(share, n);
    }

    /// <summary><c>0x1420fee60</c> with <c>0x1421293c0</c> / <c>0x142129490</c>: the cells along the room's top, bottom,
    /// left and right sides; past a cell of a room the walk jumps to that room's far end.</summary>
    void Around(int room, Action<int, int> visit)
    {
        var r = map.RoomAt(room);
        void Row(int x, int y, int length)
        {
            for (int u = 0; u < length;)
            {
                visit(x + u, y);
                int k = map[x + u, y];
                u = k < 2 ? u + 1 : map.RoomAt(k).X + map.RoomAt(k).Width - x;
            }
        }
        void Column(int x, int y, int length)
        {
            for (int v = 0; v < length;)
            {
                visit(x, y + v);
                int k = map[x, y + v];
                v = k < 2 ? v + 1 : map.RoomAt(k).Y + map.RoomAt(k).Height - y;
            }
        }
        if (r.Y != 0) Row(r.X, r.Y - 1, r.Width);
        if (r.Y + r.Height < map.Height) Row(r.X, r.Y + r.Height, r.Width);
        if (r.X != 0) Column(r.X - 1, r.Y, r.Height);
        if (r.X + r.Width < map.Width) Column(r.X + r.Width, r.Y, r.Height);
    }

    /// <summary><c>0x1420fa240</c> for the middle of three rooms on a path (<paramref name="next"/> is the room the walk
    /// reached after it, <paramref name="before"/> the one before): with the master's RoomOverlap, the blanks across its
    /// exits without a door learn the level of that exit (its height plus the room's level), side by side: top, bottom,
    /// left, right. For a door type with a direction (RoomOverlap not tested): the sides of its first doors to
    /// <paramref name="next"/> (chirality flipped) and to <paramref name="before"/> (<c>0x1420017a0</c>); for each of
    /// the type's two grounds that is null or has .gt flag 1 (+0x24 bit 0), the side one or three steps clockwise from
    /// each door's side (ground 1: 1 + 2 × flipped chirality, ground 2: 1 + 2 × chirality) goes to
    /// <c>0x1420fa750</c>.</summary>
    void MiddleRoom(string? type, int next, int middle, int before, bool chirality)
    {
        if (type is null) return;
        var m = map.RoomAt(middle);
        int w = m.Width, h = m.Height, n = m.Exits.Length;
        void At(int e, int x, int y)
        {
            if (m.Exits[e].Type is null) AddLevel(x, y, (sbyte)(m.Exits[e].Height + m.Level));
        }
        void Top() { if (m.Y != 0) for (int u = 0; u < w; u++) At(u, m.X + u, m.Y - 1); }
        void Bottom() { if (m.Y + h < map.Height) for (int u = 0; u < w; u++) At(h - 1 + 2 * w - u, m.X + u, m.Y + h); }
        void Left() { if (m.X != 0) for (int v = 0; v < h; v++) At(n - v - 1, m.X - 1, m.Y + v); }
        void Right() { if (m.X + w < map.Width) for (int v = 0; v < h; v++) At(v + w, m.X + w, m.Y + v); }
        if (!Directional(type))
        {
            if (map.Overlap == 0) return;
            Top();
            Bottom();
            Left();
            Right();
            return;
        }
        int SideOf(int e) => e < w ? 0 : e < w + h ? 1 : e < 2 * w + h ? 2 : 3;
        // 0x1420fa240 reads the first door of each list without testing it is there.
        int toNext = SideOf(DoorCandidates(m, map.RoomAt(next), type, !chirality)[0]);
        int toBefore = SideOf(DoorCandidates(m, map.RoomAt(before), type, chirality)[0]);
        var et = Edge(type);
        bool Flag1(string? ground) => ground is null || (files.Ground(ground)!.Flags & 1) != 0;
        if (Flag1(et.Ground1))
        {
            int c = (!chirality ? 2 : 0) + 1;
            Side((c + toNext) & 3);
            Side((c + toBefore) & 3);
        }
        if (Flag1(et.Ground2))
        {
            int c = (chirality ? 2 : 0) + 1;
            Side((c + toNext) & 3);
            Side((c + toBefore) & 3);
        }

        // 0x1420fa750: when side s (top, right, bottom, left) has no door, the blanks across the next side in the order
        // top, bottom, left, right learn their levels (side 0 → top, 1 → bottom, 2 → left, 3 → right, as the client).
        void Side(int s)
        {
            var (from, to) = s switch { 0 => (0, w), 1 => (w, w + h), 2 => (w + h, 2 * w + h), _ => (2 * w + h, n) };
            for (int e = from; e < to; e++)
                if (m.Exits[e].Type is not null) return;
            switch (s)
            {
                case 0: Top(); break;
                case 1: Bottom(); break;
                case 2: Left(); break;
                default: Right(); break;
            }
        }
    }

    /// <summary><c>0x1420fec10</c>'s corners: each diagonal cell off the room's corners whose number differs from both
    /// cells it touches along the room's sides (top left, top right, bottom right, bottom left).</summary>
    void Corners(int room, Action<int, int> visit)
    {
        var r = map.RoomAt(room);
        int Cell(int x, int y) => map[x, y];
        int x0 = r.X - 1, y0 = r.Y - 1, x1 = r.X + r.Width, y1 = r.Y + r.Height;
        if (x0 >= 0 && y0 >= 0 && Cell(x0, y0) != Cell(r.X, y0) && Cell(x0, y0) != Cell(x0, r.Y)) visit(x0, y0);
        if (x1 < map.Width && y0 >= 0 && Cell(x1, y0) != Cell(x1 - 1, y0) && Cell(x1, y0) != Cell(x1, r.Y)) visit(x1, y0);
        if (x1 < map.Width && y1 < map.Height && Cell(x1, y1) != Cell(x1 - 1, y1) && Cell(x1, y1) != Cell(x1, y1 - 1)) visit(x1, y1);
        if (x0 >= 0 && y1 < map.Height && Cell(x0, y1) != Cell(r.X, y1) && Cell(x0, y1) != Cell(x0, y1 - 1)) visit(x0, y1);
    }

    /// <summary><c>0x1420fa120</c>: a blank off every path at the cell learns the level (its sorted level list).</summary>
    void AddLevel(int x, int y, sbyte level)
    {
        int k = map[x, y];
        if (k < 2 || _info[k - 2].Used || map.RoomAt(k).Room is not null) return;
        var levels = _info[k - 2].Levels;
        int at = levels.BinarySearch(level);
        if (at < 0) levels.Insert(~at, level);
    }

    /// <summary>The room's levels allow a branch at this level: none and level 0, or exactly this one.</summary>
    bool LevelFits(int k, sbyte level)
    {
        var levels = _info[k - 2].Levels;
        return levels.Count == 0 ? level == 0 : levels.Count == 1 && levels[0] == level;
    }

    /// <summary><c>0x1420fc240</c>: for each path of the type whose edge has a first branching chance, its rooms (the
    /// end rooms only for nodes that branch) offer their neighbours with the edge's fields. Below 100 % the offers of a
    /// path are cut to <see cref="Share"/>(chance, n) after <c>Next(n)</c> and a shuffle.</summary>
    List<Candidate> Offered(string key)
    {
        var list = new List<Candidate>();
        foreach (var rec in _paths[key])
        {
            var d = rec.Edge.Dungeon!;
            if (d.BranchChance1 == 0) continue;
            int from = rec.Edge.B.Branching == 0 ? 1 : 0, to = rec.Rooms.Count - (rec.Edge.A.Branching == 0 ? 1 : 0);
            var target = d.BranchChance1 < 100 ? [] : list;
            for (int i = from; i < to; i++)
                Offer(target, rec.Rooms[i], (sbyte)(d.BranchDepth - 1), d.BranchChance2, rec.Level, d.Accessible, d.Impenetrable, key,
                    d.ExtraDoor, d.ExtraDoorType);
            if (d.BranchChance1 >= 100) continue;
            int take = Share(d.BranchChance1, target.Count);
            rng.Next((uint)target.Count);
            rng.Shuffle(target);
            list.AddRange(target.Take(take));
        }
        // 0x1420fc240 sets every offer accessible when a flag at the fill state's +0x38 object (+0xc0) is set; not
        // identified, so not ported.
        return list;
    }

    /// <summary><c>0x1420fbee0</c> → <c>0x1420fbfe0</c> per neighbouring cell: a room off every path and not
    /// impenetrable, whose levels fit, that a door of the type could join to the room (<c>0x1420fb480</c>).</summary>
    void Offer(List<Candidate> list, int from, sbyte depth, int chance, sbyte level, bool accessible, bool impenetrable, string key,
        int impenetrability, string? type)
    {
        Around(from, (x, y) =>
        {
            int k = map[x, y];
            if (k < 2 || !LevelFits(k, level) || _info[k - 2].Used || _info[k - 2].Impenetrable) return;
            if (!CanBranch(map.RoomAt(from), level, key, map.RoomAt(k))) return;
            list.Add(new Candidate(k, from, chance, depth, impenetrability, level, accessible, impenetrable, type));
        });
    }

    /// <summary><c>0x1420fb480</c>: among the doors between the rooms (<c>0x1420017a0</c>), an empty exit of a blank
    /// that some untagged room in some orientation of the blank's size could have with that door (<c>0x141ff89d0</c>),
    /// or an exit of the type at the level.</summary>
    bool CanBranch(DungeonPlacement a, sbyte level, string key, DungeonPlacement b)
    {
        foreach (int e in DoorCandidates(a, b, key, false))
        {
            var x = a.Exits[e];
            if (x.Type is null && a.Room is null)
            {
                var wanted = a.Exits.Select(d => (d.Type, d.Height, d.Chirality)).ToArray();
                wanted[e] = (key, (sbyte)(level - a.Level), x.Chirality);
                if (SomeRoomFits(a.Width, a.Height, wanted)) return true;
            }
            if (string.Equals(x.Type, key, StringComparison.OrdinalIgnoreCase) && x.Height + a.Level == level) return true;
        }
        return false;
    }

    /// <summary>Some untagged room in some orientation (<c>0x141ff5760</c>'s "" list) has the size and every exit
    /// given (type, height and chirality; an exit without a type matches any, <c>0x141ff89d0</c>).</summary>
    bool SomeRoomFits(int w, int h, (string? Type, sbyte Height, bool Chirality)[] exits) =>
        roomSet.Variants("", Directional).Any(v => v.W == w && v.H == h && exits.Select((x, i) => x.Type is null ||
            (string.Equals(v.Exits[i].Type, x.Type, StringComparison.OrdinalIgnoreCase) && v.Exits[i].Height == x.Height &&
             v.Exits[i].Chirality == x.Chirality)).All(ok => ok));

    /// <summary><c>0x1420fe170</c>: the room across exit e of room k (top, right, bottom, left side), if any.</summary>
    DungeonPlacement? Across(int k, int e)
    {
        var r = map.RoomAt(k);
        int w = r.Width, h = r.Height;
        var (x, y) = e < w ? (r.X + e, r.Y - 1)
            : e < w + h ? (r.X + w, r.Y + e - w)
            : e < 2 * w + h ? (r.X + 2 * w + h - 1 - e, r.Y + h)
            : (r.X - 1, r.Y - 1 + 2 * (w + h) - e);
        return map.Inside(x, y) && map[x, y] > 1 ? map.RoomAt(map[x, y]) : null;
    }

    /// <summary><c>0x142002070</c>: some exit of room a on the side it shares with room b has a type the facing exit of
    /// b has too.</summary>
    static bool Joined(DungeonPlacement a, DungeonPlacement b) =>
        SharedExits(a, b).Any(e => a.Exits[e].Type is { } t && string.Equals(b.Exits[Facing(a, b, e)].Type, t, StringComparison.OrdinalIgnoreCase));

    /// <summary><c>0x142100d60</c>: each room with an extra door chance (+0x10) lists its exits without a door or with
    /// its extra door type (+0x48) that face a room not yet joined to it (not impenetrable, unless the graph's
    /// open_extra_doors_into_impenetrable_rooms, and one of the two accessible); <c>Next(count)</c> and shuffled. Down
    /// the list, where a door of the extra door type could join both at the higher level (<c>0x1420fb480</c> both
    /// ways), <c>Next(100)</c> under the chance opens it (<c>0x142001e80</c>, blank heights); a chance over 100 goes on
    /// with 100 less. Rooms joined so with an accessible one become accessible.</summary>
    void ExtraDoors()
    {
        var accessible = new SortedSet<int>();
        for (int i = 0; i < map.Rooms.Count; i++)
        {
            var r = map.Rooms[i];
            if (r.B10 == 0) continue;
            var list = new List<int>();
            for (int e = 0; e < r.Exits.Length; e++)
            {
                var t = r.Exits[e].Type;
                if (t is not null && !string.Equals(t, r.Type48, StringComparison.OrdinalIgnoreCase)) continue;
                // +0xc1 of the object 0x142100d60 gets as its second argument, taken to be the graph's flag.
                if (Across(i + 2, e) is not { } n || (!graph.ExtraDoorsIntoImpenetrable && (n.Flags & 2) != 0) ||
                    ((r.Flags & 1) == 0 && (n.Flags & 1) == 0) || Joined(r, n))
                    continue;
                list.Add(map.Rooms.IndexOf(n) + 2);
            }
            rng.Next((uint)list.Count);
            rng.Shuffle(list);
            int chance = r.B10;
            foreach (int k in list)
            {
                var n = map.RoomAt(k);
                var level = Math.Max(r.Level, n.Level);
                if (r.Type48 is null || !CanBranch(r, level, r.Type48, n) || !CanBranch(n, level, r.Type48, r)) continue;
                if (rng.Next(100u) >= chance) continue;
                OpenDoor(r, n, r.Type48, false, true);
                if ((r.Flags & 1) != 0 || (n.Flags & 1) != 0)
                {
                    accessible.Add(i);
                    accessible.Add(k - 2);
                }
                if (chance <= 100) break;
                chance -= 100;
            }
        }
        foreach (int i in accessible) map.Rooms[i].Flags |= 1;
    }

    /// <summary><c>0x1421012c0</c>: with the master's SwapDungeonDoorsFrom / To (taken to be the room set's +0x258 /
    /// +0x260), each room's doors of the first type (<c>Next(count)</c>, shuffled) facing a door of the same type take
    /// the second on both sides, where some untagged room could still have each side's exits.</summary>
    void SwapDoors()
    {
        if (swapDoors.From is not { } from || swapDoors.To is not { } to) return;
        for (int i = 0; i < map.Rooms.Count; i++)
        {
            var r = map.Rooms[i];
            var list = Enumerable.Range(0, r.Exits.Length).Where(e => string.Equals(r.Exits[e].Type, from, StringComparison.OrdinalIgnoreCase)).ToList();
            rng.Next((uint)list.Count);
            rng.Shuffle(list);
            foreach (int e in list)
            {
                if (Across(i + 2, e) is not { } n) continue;
                int f = Facing(r, n, e);
                if (!string.Equals(n.Exits[f].Type, from, StringComparison.OrdinalIgnoreCase)) continue;
                var mine = r.Exits.Select(d => (d.Type, d.Height, d.Chirality)).ToArray();
                mine[e].Type = to;
                if (!SomeRoomFits(r.Width, r.Height, mine)) continue;
                var theirs = n.Exits.Select(d => (d.Type, d.Height, d.Chirality)).ToArray();
                theirs[f].Type = to;
                if (!SomeRoomFits(n.Width, n.Height, theirs)) continue;
                r.Exits[e].Type = to;
                n.Exits[f].Type = to;
            }
        }
    }

    /// <summary>A room the pick may give a blank (<c>0x141ff84e0</c>'s 0x18-byte entries): the room, the orientations
    /// that fit (a mask) and its weight.</summary>
    sealed record Choice(DungeonRoom Room, int Mask, int Weight);

    bool CanEnd(string? type) => type is not null && Edge(type).CanEnd;

    /// <summary><c>0x1421083e0</c>: a blank with a door type (+0x40), no flag 8 and no descriptions takes a room of its
    /// size whose exits fit (<see cref="Choices"/>; strict unless the blank has neither flag 4 nor a chance at +0x10 and
    /// its type can end: then strict first, else loose). <c>Next(total)</c> picks one, <c>Next(840)</c> the orientation
    /// among those that fit (<c>0x14299da50</c>); with more than one choice and a neighbour holding the same room
    /// (<c>0x142108920</c>) both draws again. <c>0x1420fe360</c> fills the entry.</summary>
    void Pick(int k)
    {
        var p = map.RoomAt(k);
        if (!(((p.Flags & 4) == 0 || p.Type40 is not null) && p.Room is null && p.Type40 is not null && (p.Flags & 8) == 0 &&
              p.Descriptions.Count == 0))
            return;
        bool strict = !((p.Flags & 4) == 0 && p.B10 == 0 && CanEnd(p.Type40));
        var list = Choices(p, strict);
        if (list.Count == 0 && strict) list = Choices(p, false);
        if (list.Count == 0) throw new InvalidOperationException($"Failed to find room that fits at room ({p.X}, {p.Y})");
        int total = list.Sum(c => c.Weight);
        (DungeonRoom Room, int Orientation) Draw()
        {
            int r = (int)rng.Next((uint)total);
            var c = list.FirstOrDefault(c => (r -= c.Weight) < 0) ?? list[^1];
            return (c.Room, OrientationOf(c.Mask, rng.Next(840u)));
        }
        var (room, orientation) = Draw();
        if (list.Count > 1 && SameNeighbour(room, p)) (room, orientation) = Draw();
        FillRoom(p, room, orientation);
        notes.Add($"dungeon pick ({p.X}, {p.Y}) {p.Width}x{p.Height}: {Path.GetFileName(room.Path)} o{orientation}");
    }

    /// <summary><c>0x14299da50</c>: the r-th (mod count) orientation of the mask, or r &amp; 7 for none or all.</summary>
    static int OrientationOf(int mask, uint r)
    {
        if (mask is 0 or 0xff) return (int)(r & 7);
        var bits = Enumerable.Range(0, 8).Where(o => (mask >> o & 1) != 0).ToList();
        return bits[(int)(r % (uint)bits.Count)];
    }

    /// <summary><c>0x141ff8300</c> → <c>0x141ff84e0</c>: the rooms of the blank's size (<see cref="DungeonRoomSet.BySize"/>)
    /// whose exits have each of the blank's doors (type, height, and chirality for a type with a direction); a room exit
    /// where the blank has none is allowed only when not strict, and on the first pass only for a type that can end
    /// (the loose search makes a second pass that allows any when the first finds nothing). Each room once, with the
    /// orientations that fit and weight (10 e² + 1) × its weight, e = the empty exits both have in its first
    /// orientation that fits.</summary>
    List<Choice> Choices(DungeonPlacement p, bool strict)
    {
        List<Choice> Pass(bool first)
        {
            var list = new List<Choice>();
            DungeonRoom? current = null;
            int mask = 0, weight = 0;
            void Flush()
            {
                if (current is not null && weight != 0) list.Add(new Choice(current, mask, weight));
            }
            foreach (var (room, o, exits) in roomSet.BySize(p.Width, p.Height, Directional))
            {
                int empty = 0;
                bool ok = true;
                for (int i = 0; i < exits.Length && ok; i++)
                {
                    var want = p.Exits[i];
                    var have = exits[i];
                    if (want.Type is null)
                    {
                        if (have.Type is null) empty++;
                        else if (strict || (first && !CanEnd(have.Type))) ok = false;
                    }
                    else ok = string.Equals(have.Type, want.Type, StringComparison.OrdinalIgnoreCase) && have.Height == want.Height &&
                              (!Directional(have.Type) || have.Chirality == want.Chirality);
                }
                if (!ok) continue;
                if (!ReferenceEquals(room, current))
                {
                    Flush();
                    current = room;
                    weight = (empty * empty * 10 + 1) * room.Weight;
                    mask = 0;
                }
                mask |= 1 << o;
            }
            Flush();
            return list;
        }
        var result = Pass(true);
        if (result.Count == 0 && !strict) result = Pass(false);
        return result;
    }

    /// <summary><c>0x142108920</c>: a room along the sides of the blank (anchored as a node room is) holds the same room.</summary>
    bool SameNeighbour(DungeonRoom room, DungeonPlacement p) => SameNeighbour(room, p.X, p.Y, p.Width, p.Height);

    bool SameNeighbour(DungeonRoom room, int cellX, int cellY, int w, int h)
    {
        var (x, y) = Anchor(graphOrientation, cellX, cellY, w, h);
        bool same = false;
        void Visit(int cx, int cy)
        {
            int k = map[cx, cy];
            if (k > 1 && ReferenceEquals(map.RoomAt(k).Room, room)) same = true;
        }
        void Row(int ry, int length)
        {
            for (int u = 0; u < length;)
            {
                Visit(x + u, ry);
                int k = map[x + u, ry];
                u = k < 2 ? u + 1 : map.RoomAt(k).X + map.RoomAt(k).Width - x;
            }
        }
        void Column(int cx, int length)
        {
            for (int v = 0; v < length;)
            {
                Visit(cx, y + v);
                int k = map[cx, y + v];
                v = k < 2 ? v + 1 : map.RoomAt(k).Y + map.RoomAt(k).Height - y;
            }
        }
        if (y > 0) Row(y - 1, w);
        if (y + h < map.Height) Row(y + h, w);
        if (x > 0) Column(x - 1, h);
        if (x + w < map.Width) Column(x + w, h);
        return same;
    }

    /// <summary><c>0x1420fe360</c> for a picked room: the entry takes the room and orientation; +0x11 counts its doors so
    /// far; its exits become the room's own, turned (<c>0x141d75a50</c>, table 0x142fbe348); flags 4 and 8 clear.</summary>
    void FillRoom(DungeonPlacement p, DungeonRoom room, int orientation)
    {
        if (room.Unit != map.Unit) throw new InvalidOperationException($"Cannot place room '{room.Path}': it has the wrong unit size");
        p.Room = room;
        p.Orientation = orientation;
        p.Doors = (byte)p.Exits.Count(e => e.Type is not null);
        p.Flags &= 0xf3;
        var turned = DungeonRoomSet.TurnExits(room.Exits, room.Width, room.Height, orientation, Directional);
        for (int i = 0; i < p.Exits.Length; i++)
            p.Exits[i] = new DungeonDoor { Type = turned[i].Type, Height = turned[i].Height, Chirality = turned[i].Chirality };
    }

    /// <summary><c>0x1420fcf40</c>: a room off every path whose levels fit joins the branch: it takes the level, the
    /// type, the flags and the extra door type, kind 5 (the room it grew from, if kind 5, becomes 4), a door to the room
    /// it grew from, and offers its own neighbours while depth is left.</summary>
    void Take(Candidate c, string key, List<Candidate> pending)
    {
        var info = _info[c.Room - 2];
        if (info.Used || !LevelFits(c.Room, c.Level)) return;
        var p = map.RoomAt(c.Room);
        p.Level = c.Level;
        p.Type40 = key;
        p.Flags = (byte)((p.Flags & ~3) | (c.Accessible ? 1 : 0) | (c.Impenetrable ? 2 : 0));
        p.B10 = (byte)Math.Max(p.B10, c.ExtraDoor);
        p.Kind = 5;
        if (c.Type is not null && (p.Type48 is null || p.Type48.Equals(DungeonRoomSet.Rooms, StringComparison.OrdinalIgnoreCase))) p.Type48 = c.Type;
        var from = map.RoomAt(c.From);
        if (from.Kind == 5) from.Kind = 4;
        info.Used = true;
        info.Impenetrable = c.Impenetrable;
        OpenDoor(p, from, key, false, false);
        // 0x1420fec10 with 0x1420fa120 for RoomOverlap: the blanks around it and across its corners learn the level.
        if (map.Overlap != 0)
        {
            Around(c.Room, (x, y) => AddLevel(x, y, c.Level));
            Corners(c.Room, (x, y) => AddLevel(x, y, c.Level));
        }
        if (c.Depth != 0)
            Offer(pending, c.Room, (sbyte)(c.Depth - 1), c.Chance, c.Level, c.Accessible, c.Impenetrable, key, c.ExtraDoor, c.Type);
    }

    /// <summary><c>0x142002460</c>: the edge's line (its void ends, then its points, scaled to cells) drawn cell by cell
    /// (<c>0x1420fe2e0</c>: free cells become reserved).</summary>
    void Reserve(GraphEdge e)
    {
        var points = new List<(int X, int Y)>();
        if (e.A.Placed) points.Add(Cell(e.A.X, e.A.Y));
        points.AddRange(e.Points.Select(p => Cell(p.X, p.Y)));
        if (e.B.Placed) points.Add(Cell(e.B.X, e.B.Y));
        for (int i = 1; i < points.Count; i++)
        {
            var (x0, y0) = points[i - 1];
            var (x1, y1) = points[i];
            bool steep = Math.Abs(x1 - x0) < Math.Abs(y1 - y0);
            int a0 = steep ? y0 : x0, a1 = steep ? y1 : x1, b = steep ? x0 : y0, b1 = steep ? x1 : y1;
            int da = Math.Abs(a1 - a0), db = Math.Abs(b1 - b), stepB = b < b1 ? 1 : b == b1 ? 0 : -1, stepA = a0 < a1 ? 1 : -1;
            int error = -(da / 2);
            for (int a = a0; a != a1 + stepA; a += stepA)
            {
                var (x, y) = steep ? (b, a) : (a, b);
                if (map.Inside(x, y) && map[x, y] == 0) map[x, y] = 1;
                error += db;
                if (error > 0)
                {
                    b += stepB;
                    error -= da;
                }
            }
        }
    }

    /// <summary><c>0x142106b00</c>: a node that is not void takes a room of its tag at its cell.</summary>
    void PlaceNode(GraphNode n)
    {
        if (n.Placed) return;
        var edges = n.Slots.Where(e => e is not null).Select(e => e!).ToList();
        bool flag2 = n.Tag.Length != 0 || n.Descriptions.Count != 0 || edges.Any(e => e.Dungeon!.Impenetrable);
        if (n.Descriptions.Contains("template"))
        {
            PlaceTemplate(n, edges, flag2);
            return;
        }
        if (n.Tag.Length == 0)
        {
            PlaceBlank(n, edges, flag2);
            return;
        }
        if (n.Tag == "graph")
        {
            PlaceSubgraphNode(n, edges, flag2);
            return;
        }
        var (x, y) = Cell(n.X, n.Y);
        var (room, orientation, w, h) = PickRoom(n, ref x, ref y);
        byte b10 = (byte)edges.Select(e => e.Dungeon!.ExtraDoor).DefaultIfEmpty(0).Max();
        var p = map.Place(room, orientation, w, h, x, y, (sbyte)n.Height, n.Descriptions, null, Accessible(n, edges), flag2, 1, b10,
            NodeDoorType(n));
        notes.Add($"dungeon node {n}: {p}");
    }

    /// <summary><c>0x142105a70</c> → <c>0x1421052e0</c> for a "graph" node: its descriptions are a graph (.dgr / .tgr) or
    /// a FileGroups group, a width and a height (each at most 1000, swapped for an odd graph orientation). For a group,
    /// <c>Next(count)</c> picks one of its files; when the group already gave files in this layout, those are left out
    /// and, if any are left, <c>Next(left)</c> picks again; the file replaces the first description. The node then
    /// takes a w × h entry without a room (kind 1) at its anchor (<c>0x1421062e0</c>); the subgraph comes later
    /// (<c>0x14204f030</c>).</summary>
    void PlaceSubgraphNode(GraphNode n, List<GraphEdge> edges, bool flag2)
    {
        var d = n.Descriptions;
        if (d.Count < 3) throw new InvalidOperationException($"Could not parse special graph node at location {n}");
        int w = int.Parse(d[1]), h = int.Parse(d[2]);
        if (w > 1000 || h > 1000) throw new InvalidOperationException($"Bad special graph node size at location {n}");
        if ((graphOrientation & 1) != 0 && graphOrientation < 8) (w, h) = (h, w);
        var descriptions = d.ToList();
        string file = d[0];
        if (!file.EndsWith(".dgr", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".tgr", StringComparison.OrdinalIgnoreCase))
        {
            if (fileGroup is null) throw new InvalidOperationException("Cannot find random subgraph: No FileGroups listed in master file.");
            var all = fileGroup(file) ?? throw new InvalidOperationException($"Cannot find random subgraph: No files listed for group \"{file}\"");
            string pick = all[(int)rng.Next((uint)all.Count)];
            if (_groupsUsed.TryGetValue(file, out var used))
            {
                var left = all.Where(f => !used.Contains(f)).ToList();
                if (left.Count != 0) pick = left[(int)rng.Next((uint)left.Count)];
            }
            else _groupsUsed[file] = used = [];
            used.Add(pick);
            descriptions[0] = pick;
        }
        var (x, y) = Cell(n.X, n.Y);
        var (ax, ay) = Anchor(graphOrientation, x, y, w, h);
        byte b10 = (byte)edges.Select(e => e.Dungeon!.ExtraDoor).DefaultIfEmpty(0).Max();
        var p = map.Place(null, 0, w, h, ax, ay, (sbyte)n.Height, descriptions, null, Accessible(n, edges), flag2, 1, b10, NodeDoorType(n));
        notes.Add($"dungeon node {n}: subgraph {descriptions[0]} {p}");
    }

    /// <summary><c>0x142107840</c>: a "template" node takes a blank of a room's size with that room's exits. Each slot
    /// edge goes to a side (<see cref="TemplateSides"/>); a side's exit is its edge's type at height 0, with the
    /// direction of <see cref="NodeExits"/> (<c>0x141ff2ee0</c>), or no door. An edge whose type is not the default
    /// (rooms.et, layout +0x20) leaves the void and connection lists (the room stands for it) and its type joins the
    /// layout's types (<c>0x142127400</c>, ordered by the handle's address; slot order here). The node's
    /// tag's room-set entries with that exit on every side (<c>0x141ff81b0</c> → <c>0x142012ab0</c>), kept to a
    /// "size:" description's size, else all of one size ("Multiple room sizes found for template node");
    /// <c>Next(total weight)</c> picks one, anchored as a room (<c>0x1421062e0</c>), then the node door type. The blank
    /// (<c>0x142100650</c>: no room, orientation 8, kind 0, type +0x40 the first such type or rooms.et, flag 4) takes
    /// the picked room's exits whose being rooms.et matches the type's. A node without edges (a fixed-size blank at
    /// its clamped corner, <c>0x1421069d0</c>) is not ported.</summary>
    void PlaceTemplate(GraphNode n, List<GraphEdge> edges, bool flag2)
    {
        if (edges.Count == 0) throw new NotSupportedException($"{n}: template nodes without edges (0x142107840) are not ported");
        var sides = TemplateSides(n) ?? throw new InvalidOperationException($"Failed to calculate edge layout for template node at location {n}");
        var template = new RoomExit[4];
        var types = new List<string>();
        for (int k = 0; k < 4; k++)
        {
            if (sides[k] >= 4) continue;
            var e = n.Slots[k]!;
            template[sides[k]] = new RoomExit(e.Type, 0, Directional(e.Type) && ((e.A == n) ^ (e.Chirality != 0)));
            if (e.Type.Equals(DungeonRoomSet.Rooms, StringComparison.OrdinalIgnoreCase)) continue;
            if (!_voids.Remove(e))
                foreach (var list in _groups.Values.SelectMany(s => s).SelectMany(l => l)) list.Remove(e);
            if (!types.Contains(e.Type, StringComparer.OrdinalIgnoreCase)) types.Add(e.Type);
        }
        // Several types: the client keeps them ordered by handle address; slot order is taken (1_3_18_2 seed 1 agrees
        // either way up to its stop).
        string type40 = types.Count == 0 ? DungeonRoomSet.Rooms : types[0];
        var entries = roomSet.Variants(n.Tag, t => Directional(t)).Where(v => SidesHold(v.Exits, v.W, v.H, template)).ToList();
        if (entries.Count == 0) throw new InvalidOperationException($"No matching rooms found for template node at location {n}");
        if (SizeDescription(n) is { } size)
        {
            entries.RemoveAll(v => v.W != size.W || v.H != size.H);
            if (entries.Count == 0) throw new InvalidOperationException($"No matching rooms of size {size.W}x{size.H} found for template node at location {n}");
        }
        else if (entries.Any(v => v.W != entries[0].W || v.H != entries[0].H))
            throw new InvalidOperationException($"Multiple room sizes found for template node at location {n}");
        int pick = (int)rng.Next((uint)entries.Sum(v => v.Room.Weight)), i = 0;
        while (i < entries.Count - 1 && pick >= entries[i].Room.Weight) pick -= entries[i++].Room.Weight;
        var chosen = entries[i];
        var (x, y) = Cell(n.X, n.Y);
        var (ax, ay) = Anchor(graphOrientation, x, y, chosen.W, chosen.H);
        string doorType = NodeDoorType(n);
        byte b10 = (byte)edges.Select(e => e.Dungeon!.ExtraDoor).DefaultIfEmpty(0).Max();
        var p = map.Place(null, 8, chosen.W, chosen.H, ax, ay, (sbyte)n.Height, [], type40, Accessible(n, edges), flag2, 0, b10, doorType);
        p.Flags |= 4;
        bool rooms = type40.Equals(DungeonRoomSet.Rooms, StringComparison.OrdinalIgnoreCase);
        for (int k = 0; k < p.Exits.Length; k++)
        {
            var x2 = chosen.Exits[k];
            if (rooms != string.Equals(x2.Type, DungeonRoomSet.Rooms, StringComparison.OrdinalIgnoreCase)) continue;
            p.Exits[k] = new DungeonDoor { Type = x2.Type, Height = x2.Height, Chirality = x2.Chirality };
        }
        notes.Add($"dungeon node {n}: template {Path.GetFileName(chosen.Room.Path)} o{chosen.Orientation} {p}");
    }

    /// <summary><c>0x142012ab0</c>: every side (top w exits, right h, bottom w, left h) has an exit equal to the
    /// template's for that side (type, height and direction; no door where the template has none).</summary>
    static bool SidesHold(RoomExit[] exits, int w, int h, RoomExit[] template)
    {
        int[] bounds = [0, w, w + h, 2 * w + h, exits.Length];
        for (int s = 0; s < 4; s++)
        {
            var t = template[s];
            bool found = false;
            for (int k = bounds[s]; k < bounds[s + 1] && !found; k++)
                found = string.Equals(exits[k].Type, t.Type, StringComparison.OrdinalIgnoreCase) && exits[k].Height == t.Height && exits[k].Chirality == t.Chirality;
            if (!found) return false;
        }
        return true;
    }

    /// <summary><c>0x1421299f0</c>: the side (0 top, 1 right, 2 bottom, 3 left) of each edge slot, 4 for none. A slot's
    /// edge, unless its type is a wildcard (.et flag 0x80), goes to the side its direction leans to: towards its first
    /// internal point from this end (the other node without points), the larger of |dx| and |dy| deciding, a tie
    /// vertical. While a side holds two or more, a slot of it may move to a neighbouring side holding none
    /// (<c>0x142129d80</c> with 0), else one (with 1): of all such moves the one whose direction is nearest the new
    /// side's (<c>0x14299d730</c>, the first of equals; the side before then the side after, slots in side order).
    /// Null when a side is still shared.</summary>
    int[]? TemplateSides(GraphNode n)
    {
        var bySide = new List<int>[4];
        for (int s = 0; s < 4; s++) bySide[s] = [];
        (int X, int Y) Direction(int k)
        {
            var e = n.Slots[k]!;
            var (px, py) = e.A == n ? (e.Points.Count == 0 ? (e.B.X, e.B.Y) : e.Points[0]) : (e.Points.Count == 0 ? (e.A.X, e.A.Y) : e.Points[^1]);
            return (px - n.X, py - n.Y);
        }
        for (int k = 0; k < 4; k++)
        {
            if (n.Slots[k] is not { } e || Edge(e.Type).Wildcard) continue;
            var (dx, dy) = Direction(k);
            int side = Math.Abs(dy) < Math.Abs(dx) ? (dx > 0 ? 1 : 3) : (dy > 0 ? 2 : 0);
            bySide[side].Add(k);
        }
        (int X, int Y)[] axes = [(0, -1), (1, 0), (0, 1), (-1, 0)];
        while (true)
        {
            var moves = Moves(0);
            if (moves.Count == 0) moves = Moves(1);
            if (moves.Count == 0) break;
            var best = moves[0];
            foreach (var m in moves)
                if (m.Closeness > best.Closeness) best = m;
            bySide[best.To].Add(best.Slot);
            bySide[best.From].Remove(best.Slot);
        }
        var sides = new[] { 4, 4, 4, 4 };
        for (int s = 0; s < 4; s++)
        {
            if (bySide[s].Count > 1) return null;
            if (bySide[s].Count == 1) sides[bySide[s][0]] = s;
        }
        return sides;

        List<(int Closeness, int From, int To, int Slot)> Moves(int holding)
        {
            var moves = new List<(int, int, int, int)>();
            for (int s = 0; s < 4; s++)
            {
                if (bySide[s].Count < 2) continue;
                foreach (int k in bySide[s])
                    foreach (int to in (ReadOnlySpan<int>)[(s + 3) & 3, (s + 1) & 3])
                        if (bySide[to].Count == holding)
                            moves.Add((Closeness(axes[to].X * 10, axes[to].Y * 10, Direction(k)), s, to, k));
            }
            return moves;
        }

        // 0x14299d730: the index into its arc-cosine table (255 steps, 0 = opposite, 254 = the same direction); the
        // table falls with the index, so the smallest angle is the largest index.
        static int Closeness(int ax, int ay, (int X, int Y) b)
        {
            if ((ax == 0 && ay == 0) || (b.X == 0 && b.Y == 0)) return 254;
            int la = ax * ax + ay * ay, lb = b.X * b.X + b.Y * b.Y;
            int root = (int)FortuneVoronoi.IntSqrt((uint)(lb * la));
            if (root == 0) root = 1;
            int v = (sbyte)((lb - (b.Y - ay) * (b.Y - ay) - (b.X - ax) * (b.X - ax) + la) * 0x7f / root / 2) + 0x7f;
            return Math.Clamp(v, 0, 0xfe);
        }
    }

    /// <summary><c>0x142107130</c>: an untagged node takes a blank (kind 3, no room yet) of one of its door type's room
    /// sizes (<c>0x142106db0</c>: the first edge's type, kept to a "size:" description's size). <c>Next(total)</c> picks
    /// a size; it must fit on free cells at the node's anchor (graph orientation, not kept inside the map) and hold no
    /// other node's cell (<c>0x1421069d0</c>), else that size is dropped and the next one drawn.</summary>
    void PlaceBlank(GraphNode n, List<GraphEdge> edges, bool flag2)
    {
        var (x, y) = Cell(n.X, n.Y);
        if (x < 0 || y < 0 || x + 1 > map.Width || y + 1 > map.Height)
            throw new InvalidOperationException($"Non-feature node out-of-bounds at room ({x}, {y})");
        if (map[x, y] == 1) map[x, y] = 0;
        if (map[x, y] != 0) throw new InvalidOperationException($"Non-feature node overlapping another node at room ({x}, {y})");
        // 0x142106db0: the first slot edge whose type is not the default (layout +0x20, rooms.et), else the default.
        string type = n.Slots.FirstOrDefault(e => e is not null && !e.Type.Equals(DungeonRoomSet.Rooms, StringComparison.OrdinalIgnoreCase))?.Type ??
            DungeonRoomSet.Rooms;
        var sizes = roomSet.Sizes(type).ToList();
        if (SizeDescription(n) is { } only) sizes = [.. sizes.Where(s => s.W == only.W && s.H == only.H)];
        if (sizes.Count == 0) throw new InvalidOperationException($"No rooms found for non-feature node with edge type {type} at room ({x}, {y})");
        byte b10 = (byte)edges.Select(e => e.Dungeon!.ExtraDoor).DefaultIfEmpty(0).Max();
        notes.Add($"dungeon node {n}: sizes of {Path.GetFileName(type)}: {string.Join(", ", sizes.Select(z => $"{z.W}x{z.H}:{z.Weight}"))}");
        while (true)
        {
            int r = (int)rng.Next((uint)Math.Max(1, sizes.Sum(s => s.Weight))), i = 0;
            while (i < sizes.Count - 1 && r >= sizes[i].Weight) r -= sizes[i++].Weight;
            var (w, h, _) = sizes[i];
            var (ax, ay) = Corner(graphOrientation, x, y, w, h);
            bool ok = ax >= 0 && ay >= 0 && ax + w <= map.Width && ay + h <= map.Height;
            for (int v = 0; v < h && ok; v++)
                for (int u = 0; u < w && ok; u++)
                    ok = map[ax + u, ay + v] == 0;
            if (ok && !graph.Nodes.Any(o => o != n && Cell(o.X, o.Y) is var (ox, oy) && ox >= ax && ox < ax + w && oy >= ay && oy < ay + h))
            {
                var p = map.Place(null, 0, w, h, ax, ay, (sbyte)n.Height, [], type, Accessible(n, edges), flag2, 3, b10, NodeDoorType(n));
                notes.Add($"dungeon node {n}: {p}");
                return;
            }
            sizes.RemoveAt(i);
            if (sizes.Count == 0) throw new InvalidOperationException($"Non-feature node cannot find room to place at room ({x}, {y})");
        }
    }

    /// <summary><c>0x142127f90</c>: a "size:WxH" or "size:N" (N × N) description.</summary>
    static (int W, int H)? SizeDescription(GraphNode n)
    {
        foreach (var d in n.Descriptions)
        {
            if (!d.StartsWith("size:", StringComparison.Ordinal)) continue;
            var parts = d[5..].Split('x');
            int w = int.Parse(parts[0]);
            return (w, parts.Length > 1 ? int.Parse(parts[1]) : w);
        }
        return null;
    }

    /// <summary><c>0x1420f9d50</c>: one of the node's edges is accessible, or a description says "accessible",
    /// "default", "entrance…" or "sideentrance…".</summary>
    static bool Accessible(GraphNode n, List<GraphEdge> edges) =>
        edges.Any(e => e.Dungeon!.Accessible) || n.Descriptions.Any(d => d is "accessible" or "default" ||
            d.StartsWith("entrance", StringComparison.Ordinal) || d.StartsWith("sideentrance", StringComparison.Ordinal));

    /// <summary><c>0x142003740</c> (the node room's +0x48 door type): the node's edge slots (+0x58) with an extra door
    /// chance (+0x54), weighted by it, <c>Next(total)</c> picks one's extra door type (+0x70). With none, no draw and
    /// the default (the client's global string, taken as rooms.et).</summary>
    string NodeDoorType(GraphNode n)
    {
        var slots = n.Slots.Where(e => e?.Dungeon is { ExtraDoor: not 0 }).Select(e => e!.Dungeon!).ToList();
        if (slots.Count == 0) return DungeonRoomSet.Rooms;
        int r = (int)rng.Next((uint)slots.Sum(d => d.ExtraDoor));
        foreach (var d in slots)
        {
            if (r < d.ExtraDoor) return d.ExtraDoorType;
            r -= d.ExtraDoor;
        }
        return slots[^1].ExtraDoorType;
    }

    /// <summary><c>0x142105a70</c>: the room set's (room, orientation) entries of the node's tag whose exits hold the
    /// node's edges (<c>0x142104990</c> → <c>0x141ff8ff0</c>); of those, the ones in the node's orientation (any for
    /// orientation 8) that fit at its anchor with enough doors into the map (<c>0x1421298e0</c> →
    /// <c>0x1421065a0</c>). Entries with a door a placed neighbour room does not answer go only when every entry has
    /// one. <c>Next(total weight)</c> picks one, anchored by <c>0x1421062e0</c>; when a side neighbour already holds
    /// that room, <c>Next(total)</c> once more (<c>0x142108920</c>).</summary>
    (DungeonRoom Room, int Orientation, int W, int H) PickRoom(GraphNode n, ref int x, ref int y)
    {
        if (n.Orientation == 9) throw new NotSupportedException($"{n}: dungeon nodes of any orientation with edge directions (0x142104990) are not ported");
        var wanted = NodeExits(n);
        var candidates = roomSet.Variants(n.Tag, t => Directional(t)).Where(v => v.Room.Weight != 0 && Holds(v.Exits, wanted)).ToList();
        if (candidates.Count == 0)
            // 0x141d8b750: the generator's extra room pool (+0x40, filled per graph by 0x14204bfa0 / 0x141d8bba0;
            // the Labyrinth's "secret" rooms), every orientation at weight 1.
            throw new NotSupportedException($"{n}: no room of tag '{n.Tag}' holds the node's exits; the extra room pool (0x141d8b750) is not ported");
        int nx = x, ny = y;
        var fits = new List<(DungeonRoom Room, int Orientation, int W, int H, int X, int Y, bool Mismatch)>();
        foreach (var (r, o, w, h, _) in candidates)
        {
            if (n.Orientation != 8 && o != n.Orientation) continue;
            // 0x1421062e0 reads the graph's orientation (+0x58), not the node's.
            var (ax, ay) = Anchor(graphOrientation, nx, ny, w, h);
            if (RoomFits(r, o, w, h, ax, ay, wanted.Count, out bool mismatch)) fits.Add((r, o, w, h, ax, ay, mismatch));
        }
        if (fits.Any(f => !f.Mismatch)) fits.RemoveAll(f => f.Mismatch);
        if (fits.Count == 0) throw new InvalidOperationException($"No suitable room found for room at ({nx}, {ny}) with type \"{n.Tag}\"");
        int total = fits.Sum(f => f.Room.Weight);
        var chosen = Weighted((int)rng.Next((uint)total));
        if (SameNeighbour(chosen.Room, nx, ny, chosen.W, chosen.H)) chosen = Weighted((int)rng.Next((uint)total));
        x = chosen.X;
        y = chosen.Y;
        return (chosen.Room, chosen.Orientation, chosen.W, chosen.H);

        // 0x141f4f970: the entry the pick falls in, else the last.
        (DungeonRoom Room, int Orientation, int W, int H, int X, int Y, bool Mismatch) Weighted(int pick)
        {
            int i = 0;
            while (i < fits.Count - 1 && pick >= fits[i].Room.Weight) pick -= fits[i++].Room.Weight;
            return fits[i];
        }
    }

    /// <summary><c>0x142104990</c> for a node of a fixed orientation: one exit per edge slot in slot order (type,
    /// the higher of the edge's two nodes' heights less the node's, and for a door type with a direction, whether the
    /// node is the edge's first end, flipped by the edge's chirality; <c>0x141ff2ee0</c>).</summary>
    List<RoomExit> NodeExits(GraphNode n)
    {
        var exits = new List<RoomExit>();
        foreach (var e in n.Slots)
        {
            if (e is null) continue;
            bool chirality = Directional(e.Type) && ((e.A == n) ^ (e.Chirality != 0));
            exits.Add(new RoomExit(e.Type, (sbyte)(Math.Max(e.A.Height, e.B.Height) - n.Height), chirality));
        }
        return exits;
    }

    /// <summary><c>0x141ff8900</c>: every wanted exit takes one of the room's exits with the same type, height and
    /// chirality.</summary>
    static bool Holds(RoomExit[] exits, List<RoomExit> wanted)
    {
        var left = exits.GroupBy(Key).ToDictionary(g => g.Key, g => g.Count());
        foreach (var w in wanted)
        {
            var k = Key(w);
            if (left.GetValueOrDefault(k) == 0) return false;
            left[k]--;
        }
        return true;

        static (string?, sbyte, bool) Key(RoomExit x) => (x.Type?.ToLowerInvariant(), x.Height, x.Chirality);
    }

    /// <summary><c>0x1421065a0</c>: the room's cells are free or reserved (<c>0x1420fe0e0</c>); unless it is the size of
    /// the map, its doors that lead to a cell inside the map must be at least the node's edges. A door into a placed
    /// room whose exit facing it has another type (rooms.et and door types with flag 0x10 aside) is a mismatch.</summary>
    bool RoomFits(DungeonRoom room, int orientation, int w, int h, int ax, int ay, int edges, out bool mismatch)
    {
        mismatch = false;
        if (ax < 0 || ay < 0 || ax + w > map.Width || ay + h > map.Height) return false;
        for (int v = 0; v < h; v++)
            for (int u = 0; u < w; u++)
                if (map[ax + u, ay + v] > 1) return false;
        if (w == map.Width && h == map.Height) return true;
        int n = 2 * (room.Width + room.Height), inside = 0;
        bool doors = false;
        ReadOnlySpan<byte> a = [0, 0, 1, 1, 1, 1, 2, 2, 0], b = [0, 1, 1, 2, 0, 1, 1, 2, 0];
        for (int i = 0; i < n; i++)
        {
            var exit = room.Exits[i];
            if (exit.Type is null) continue;
            doors = true;
            // 0x141d75a50: the room's exit i in the turned room.
            int o = Math.Min(orientation, 8);
            int j = (o is >= 4 and < 8 ? n - i - 1 : i);
            j = (j + a[o] * room.Width + b[o] * room.Height) % n;
            var (cx, cy) = j < w ? (ax + j, ay - 1) : j < w + h ? (ax + w, ay + j - w) : j < 2 * w + h ? (ax + 2 * w + h - 1 - j, ay + h) :
                (ax - 1, ay + 2 * (w + h) - 1 - j);
            if (!map.Inside(cx, cy)) continue;
            inside++;
            if (mismatch || exit.Type.Equals(DungeonRoomSet.Rooms, StringComparison.OrdinalIgnoreCase)) continue;
            // Door type flag 0x10 (.et +0x48) is not read by the port; taken to be clear.
            int cell = map[cx, cy];
            if (cell <= 1 || map.RoomAt(cell) is not { Room: not null } other) continue;
            int k = j < w ? other.Width + other.Height + (other.X + other.Width - 1 - cx) : j < w + h ? 2 * other.Width + other.Height + (other.Y + other.Height - 1 - cy) :
                j < 2 * w + h ? cx - other.X : other.Width + (cy - other.Y);
            if (!string.Equals(other.Exits[k].Type, exit.Type, StringComparison.OrdinalIgnoreCase)) mismatch = true;
        }
        return !doors || inside >= edges;
    }

    /// <summary><c>0x1421062e0</c>: the cell the node's room starts at: the node's cell less the corner the orientation
    /// puts there (table 0x142fa04b0), kept inside the map.</summary>
    (int X, int Y) Anchor(int orientation, int x, int y, int w, int h)
    {
        if (w > map.Width || h > map.Height) throw new InvalidOperationException($"a {w}x{h} room is bigger than the graph");
        var (cx, cy) = Corner(orientation, x, y, w, h);
        return (Math.Min(Math.Max(0, cx), map.Width - w), Math.Min(Math.Max(0, cy), map.Height - h));
    }

    /// <summary>The node's cell less the corner the orientation puts there (table 0x142fa04b0).</summary>
    static (int X, int Y) Corner(int orientation, int x, int y, int w, int h)
    {
        ReadOnlySpan<byte> table = [0, 1, 1, 1, 0, 1, 0, 0, 0, 1, 1, 0, 0, 0, 1, 1, 0, 0, 0, 1, 0, 1, 1, 1, 0, 1, 1];
        int o = Math.Min(orientation, 8);
        int a = w, b = h;
        if ((o & 1) != 0 && o < 8) (a, b) = (b, a);
        Span<int> corner = [a - 1, 0, b - 1, 0];
        int ox = corner[table[3 * o + 1] + table[3 * o] * 2];
        int oy = corner[(table[3 * o] == 0 ? 2 : 0) + table[3 * o + 2]];
        return (x - ox, y - oy);
    }

    /// <summary><c>0x142100be0</c>: node rooms whose descriptions say "default" or hold "telepad", "teleport" or
    /// "entrance" get flag 0x10.</summary>
    void Teleports()
    {
        foreach (var r in map.Rooms)
        {
            bool t = r.Kind is 1 or 2 && r.Descriptions.Any(d => d == "default" || d.Contains("telepad", StringComparison.Ordinal) ||
                d.Contains("teleport", StringComparison.Ordinal) || d.Contains("entrance", StringComparison.Ordinal));
            r.Flags = (byte)(t ? r.Flags | 0x10 : r.Flags & ~0x10);
        }
    }

    /// <summary><c>0x1420fd1f0</c>: one fill state per room; each new one draws its random number (<c>Next(128)</c>).
    /// Every predecessor is cleared.</summary>
    void Resize()
    {
        if (_info.Count > map.Rooms.Count) _info.RemoveRange(map.Rooms.Count, _info.Count - map.Rooms.Count);
        while (_info.Count < map.Rooms.Count) _info.Add(new Info { Random = (int)rng.Next(0x80u) });
        foreach (var i in _info) i.Pred = 0;
    }

    /// <summary><c>0x1421040c0</c>: the free cells fill with blanks for the first edge's type, then each edge is
    /// connected (else "Failed to connect edge"), then the prune.</summary>
    void Group(List<GraphEdge> edges)
    {
        if (edges.Count == 0) return;
        Cells("fill<");
        Fill(edges[0].Type);
        Cells("fill>");
        PrepareInfo();
        Cells("connect<");
        foreach (var e in edges)
            if (!Connect(e)) throw new InvalidOperationException($"Failed to connect edge {e}");
        Cells("connect>");
        Cells("prune<");
        Prune();
        Cells("prune>");
    }

    /// <summary><c>0x1420fd4e0</c>: row by row, each free cell gets a blank of one of the type's room sizes: up to five
    /// <c>Next(total weight)</c> picks until one fits, else the first size, else 1 × 1 (with flag 2). The column then
    /// moves on by the size's width, the first size's for the 1 × 1 (so the cell after it stays free).</summary>
    void Fill(string type)
    {
        var sizes = roomSet.Sizes(type);
        if (sizes.Count == 0) return;
        int total = sizes.Sum(s => s.Weight);
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width;)
            {
                if (map[x, y] != 0) { x++; continue; }
                (int W, int H)? chosen = null;
                for (int t = 0; t < 5 && chosen is null; t++)
                {
                    int r = (int)rng.Next((uint)total);
                    var s = sizes.First(s => (r -= s.Weight) < 0);
                    if (Fits(x, y, s.W, s.H)) chosen = (s.W, s.H);
                }
                bool small = false;
                if (chosen is null)
                {
                    chosen = (sizes[0].W, sizes[0].H);
                    if (!Fits(x, y, sizes[0].W, sizes[0].H)) { chosen = (1, 1); small = true; }
                }
                var (w, h) = chosen.Value;
                map.Place(null, 0, w, h, x, y, 0, [], type, false, small, 3, 0, DungeonRoomSet.Rooms);
                x += small ? sizes[0].W : w;
            }
    }

    bool Fits(int x, int y, int w, int h)
    {
        if (x + w > map.Width || y + h > map.Height) return false;
        for (int v = 0; v < h; v++)
            for (int u = 0; u < w; u++)
                if (map[x + u, y + v] != 0) return false;
        return true;
    }

    /// <summary><c>0x1420facb0</c>: the path search from the edge's first node's room to the second's; along the path
    /// found, from the far end back, every room is marked as on a path at the edge's level, blanks take the edge's
    /// fields, and each step opens a door (<c>0x142001e80</c>).</summary>
    bool Connect(GraphEdge e)
    {
        var (ax, ay) = Cell(e.A.X, e.A.Y);
        var (bx, by) = Cell(e.B.X, e.B.Y);
        bool inA = map.Inside(ax, ay), inB = map.Inside(bx, by);
        if (!inA && !inB) return true;
        if (!inA || !inB) throw new NotSupportedException($"{e}: dungeon edges leaving the map (0x1420faa00) are not ported");
        int start = map[ax, ay], target = map[bx, by];
        if (start == 1 || target == 1) throw new InvalidOperationException("Tried to connect from a void room");
        var level = Math.Max(map.RoomAt(target).Level, map.RoomAt(start).Level);
        var d = e.Dungeon!;
        if (!Search(start, target, level, d.Fuzzy, e.Type, e.Chirality != 0, d.Impenetrable))
        {
            _paths.Clear();
            return false;
        }
        string key = d.BranchType.Length != 0 ? d.BranchType : e.Type;
        if (!_paths.TryGetValue(key, out var records)) _paths[key] = records = [];
        var record = new PathRecord([], e, level);
        records.Add(record);
        int prev = 0, prevPrev = 0;
        for (int cur = target; cur != 0;)
        {
            record.Rooms.Add(cur);
            var info = _info[cur - 2];
            int at = info.Levels.BinarySearch(level);
            if (at < 0) info.Levels.Insert(~at, level);
            info.Used = true;
            var room = map.RoomAt(cur);
            if (room.Room is null)
            {
                info.Bias = d.Bias;
                info.Impenetrable = d.Impenetrable;
                room.Flags = (byte)(d.Impenetrable ? room.Flags | 2 : room.Flags & ~2);
                if (cur != start && cur != target)
                {
                    room.Level = level;
                    room.B10 = (byte)d.ExtraDoor;
                    room.Type48 = d.ExtraDoorType;
                    if (room.Type40 is null || room.Type40.Equals(DungeonRoomSet.Rooms, StringComparison.OrdinalIgnoreCase)) room.Type40 = e.Type;
                }
            }
            if (prev != 0)
            {
                var other = map.RoomAt(prev);
                bool blank = room.Room is null || other.Room is null;
                OpenDoor(room, other, e.Type, e.Chirality != 0, blank);
                if (cur != start && cur != target && d.Accessible) room.Flags |= 1;
                if (prevPrev != 0) MiddleRoom(e.Type, cur, prev, prevPrev, e.Chirality != 0);
            }
            prevPrev = prev;
            prev = cur;
            cur = info.Pred;
        }
        foreach (var i in _info) i.Pred = 0;
        return true;
    }

    /// <summary><c>0x1420fb720</c>: A* over the rooms. A step costs half the distance between the rooms' centres (in
    /// half units, <c>0x141937840</c>); a room's estimate, set when it is first reached, is its half distance to the
    /// target × (100 − fuzzy) + bias × 128 / 100 + fuzzy × its random number. The open set is ordered by cost plus
    /// estimate, then room number. A room already on a path can only be passed when neither it nor the edge is
    /// impenetrable.</summary>
    bool Search(int start, int target, sbyte level, int fuzzy, string type, bool chirality, bool impenetrable)
    {
        Info I(int k) => _info[k - 2];
        I(start).G = 0;
        I(start).Pred = 0;
        I(target).H = 0;
        var open = new List<int> { start };
        var closed = new HashSet<int>();
        static int Centre(DungeonPlacement r, bool y) => y ? 2 * r.Y + r.Height : 2 * r.X + r.Width;
        int Half(DungeonPlacement a, DungeonPlacement b)
        {
            int dx = Centre(a, false) - Centre(b, false), dy = Centre(a, true) - Centre(b, true);
            return (int)(FortuneVoronoi.IntSqrt(unchecked((uint)(dx * dx + dy * dy))) >> 1);
        }
        while (open.Count != 0)
        {
            int best = 0;
            for (int i = 1; i < open.Count; i++)
            {
                int a = I(open[i]).G + I(open[i]).H, b = I(open[best]).G + I(open[best]).H;
                if (a < b || (a == b && open[i] < open[best])) best = i;
            }
            int cur = open[best];
            open.RemoveAt(best);
            if (cur == target) return true;
            closed.Add(cur);
            foreach (int n in Neighbours(cur, level, type, chirality))
            {
                if (n != target && !((!I(n).Used || (!I(n).Impenetrable && !impenetrable)) && !closed.Contains(n) && I(cur).Pred != n)) continue;
                if (!DoorPossible(n, cur, level, type, !chirality)) continue;
                var rc = map.RoomAt(cur);
                var rn = map.RoomAt(n);
                int g = Half(rc, rn) + I(cur).G;
                if (!open.Contains(n))
                    I(n).H = unchecked((int)((uint)Half(rn, map.RoomAt(target)) * (uint)(100 - fuzzy) + (uint)(I(n).Bias << 7) / 100u +
                        (uint)(fuzzy * I(n).Random)));
                else if (I(n).G <= g) continue;
                else open.Remove(n);
                I(n).G = g;
                open.Add(n);
                I(n).Pred = cur;
            }
        }
        return false;
    }

    /// <summary><c>0x1420fee60</c> with the search's filter: the rooms along the room's top, bottom, left and right
    /// sides, cell by cell (a room as often as it touches), that a door could join to it.</summary>
    IEnumerable<int> Neighbours(int cur, sbyte level, string type, bool chirality)
    {
        var r = map.RoomAt(cur);
        var cells = new List<(int X, int Y)>();
        if (r.Y != 0) for (int u = 0; u < r.Width; u++) cells.Add((r.X + u, r.Y - 1));
        if (r.Y + r.Height < map.Height) for (int u = 0; u < r.Width; u++) cells.Add((r.X + u, r.Y + r.Height));
        if (r.X != 0) for (int v = 0; v < r.Height; v++) cells.Add((r.X - 1, r.Y + v));
        if (r.X + r.Width < map.Width) for (int v = 0; v < r.Height; v++) cells.Add((r.X + r.Width, r.Y + v));
        var list = new List<int>();
        foreach (var (x, y) in cells)
        {
            int k = map[x, y];
            // 0x1421337a0: the door from this room to the neighbour (0x1420fb650(cur, k, …, chirality)).
            if (k > 1 && DoorPossible(cur, k, level, type, chirality)) list.Add(k);
        }
        return list;
    }

    /// <summary><c>0x1420fb650</c>: some door between room a and room b would serve the level: an empty exit of a blank,
    /// or an exit of the type at that level.</summary>
    bool DoorPossible(int a, int b, sbyte level, string type, bool chirality)
    {
        var ra = map.RoomAt(a);
        foreach (int e in DoorCandidates(ra, map.RoomAt(b), type, chirality))
        {
            var x = ra.Exits[e];
            if ((x.Type is null && ra.Room is null) ||
                (string.Equals(x.Type, type, StringComparison.OrdinalIgnoreCase) && x.Height + ra.Level == level))
                return true;
        }
        return false;
    }

    /// <summary><c>0x142001e80</c>: the possible doors between two rooms (else "Could not open doors!?"):
    /// <c>Next(count)</c>, shuffled, the first opened (<c>0x142001d10</c>).</summary>
    void OpenDoor(DungeonPlacement a, DungeonPlacement b, string type, bool chirality, bool blank)
    {
        var doors = DoorCandidates(a, b, type, chirality);
        if (doors.Count == 0) throw new InvalidOperationException("Could not open doors!?");
        rng.Next((uint)doors.Count);
        rng.Shuffle(doors);
        int ea = doors[0], eb = Facing(a, b, ea);
        SetDoor(a.Exits[ea], type, !chirality);
        SetDoor(b.Exits[eb], type, chirality);
        if (blank)
        {
            if (a.Room is null) a.Exits[ea].Height = (sbyte)(a.Level < b.Level ? b.Level - a.Level : 0);
            if (b.Room is null) b.Exits[eb].Height = (sbyte)(b.Level < a.Level ? a.Level - b.Level : 0);
        }
    }

    /// <summary><c>0x141ff2ee0</c>: the exit takes the type; its chirality only counts for a type with a direction.</summary>
    void SetDoor(DungeonDoor d, string? type, bool chirality)
    {
        d.Type = type;
        d.Chirality = Directional(type) && chirality;
    }

    /// <summary><c>0x1420017a0</c>: the exits of room a on the side it shares with room b, unit by unit, where both
    /// rooms could take a door of the type (<c>0x142001640</c>); an exit of another type with a direction rules out
    /// every door.</summary>
    List<int> DoorCandidates(DungeonPlacement a, DungeonPlacement b, string type, bool chirality)
    {
        var list = new List<int>();
        bool abort = false;
        foreach (int e in SharedExits(a, b))
        {
            if (abort) break;
            if (CanTake(a, e, type, !chirality, ref abort) && CanTake(b, Facing(a, b, e), type, chirality, ref abort)) list.Add(e);
        }
        if (abort) list.Clear();
        return list;
    }

    bool CanTake(DungeonPlacement r, int e, string type, bool chirality, ref bool abort)
    {
        var x = r.Exits[e];
        if (x.Type is null || x.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
        {
            if (r.Room is null && x.Type is null) return true;
            if (!string.Equals(x.Type, type, StringComparison.OrdinalIgnoreCase)) return false;
            return !Directional(type) || x.Chirality == chirality;
        }
        if (Directional(x.Type)) abort = true;
        return false;
    }

    /// <summary>The exits of room a facing room b, along their shared side (b above, below, left of, right of a).</summary>
    static IEnumerable<int> SharedExits(DungeonPlacement a, DungeonPlacement b)
    {
        if (b.Y + b.Height == a.Y || a.Y + a.Height == b.Y)
        {
            int from = Math.Max(a.X, b.X), to = Math.Min(a.X + a.Width, b.X + b.Width);
            bool above = b.Y + b.Height == a.Y;
            for (int c = from; c < to; c++) yield return above ? c - a.X : 2 * a.Width - 1 + a.Height + a.X - c;
        }
        else
        {
            int from = Math.Max(a.Y, b.Y), to = Math.Min(a.Y + a.Height, b.Y + b.Height);
            bool left = b.X + b.Width == a.X;
            for (int r = from; r < to; r++) yield return left ? a.Y - 1 + 2 * (a.Width + a.Height) - r : r - a.Y + a.Width;
        }
    }

    /// <summary><c>0x142001560</c>: the exit of room b that faces exit e of room a.</summary>
    static int Facing(DungeonPlacement a, DungeonPlacement b, int e)
    {
        int w = a.Width, h = a.Height;
        if (e < w) return 2 * b.Width - 1 + b.Height + b.X - (a.X + e);            // a's top: b's bottom
        if (e < w + h) return 2 * (b.Width + b.Height) - 1 + b.Y - (a.Y + e - w);  // a's right: b's left
        if (e < 2 * w + h) return 2 * w - 1 + h + a.X - e - b.X;                   // a's bottom: b's top
        return a.Y - 1 + 2 * (w + h) - e - b.Y + b.Width;                          // a's left: b's right
    }

    /// <summary><c>0x1420fd9c0</c>: rooms off every path (and without flag 4) swap with the last kept ones and go; the
    /// paths' room numbers follow, and the cells are made again.</summary>
    void Prune()
    {
        int count = map.Rooms.Count;
        if (count == 0) return;
        var order = Enumerable.Range(0, count).ToArray();
        int i = 0, j = count - 1;
        if (j != 0)
            while (i < j)
            {
                if (!_info[i].Used && (map.Rooms[i].Flags & 4) == 0)
                {
                    (map.Rooms[i], map.Rooms[j]) = (map.Rooms[j], map.Rooms[i]);
                    (_info[i], _info[j]) = (_info[j], _info[i]);
                    (order[i], order[j]) = (order[j], order[i]);
                    j--;
                }
                else i++;
            }
        if (_info[i].Used || (map.Rooms[i].Flags & 4) != 0) i++;
        map.Rooms.RemoveRange(i, count - i);
        _info.RemoveRange(i, count - i);
        var inverse = new int[count];
        for (int k = 0; k < count; k++) inverse[order[k]] = k;
        foreach (var records in _paths.Values)
            foreach (var r in records)
                for (int k = 0; k < r.Rooms.Count; k++) r.Rooms[k] = inverse[r.Rooms[k] - 2] + 2;
        map.Rebuild();
    }
}
