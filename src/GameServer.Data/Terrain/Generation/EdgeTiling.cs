namespace GameServer.Data.Terrain.Generation;

/// <summary>Edge tiling (<c>0x14210de80</c> and below): every edge of the tile graph is laid as a chain of edge tiles.
/// A cost grid of one cell per map tile (<c>0x141f247e0</c>: cells under placed tiles are blocked) is raised along every
/// edge (<c>0x141f25c90</c>); each chain of edges through nodes with two edges becomes a path record
/// (<c>0x14210b950</c>), its cells are found by a best-first search from the path's later points back to its current
/// one (<c>0x14210c600</c> → <c>0x141f24d60</c>, with backtracking), and one tile is picked per cell for the key the
/// path asks there (<c>0x14210f470</c>).</summary>
sealed class EdgeTiling(TileGraph graph, TileGrid grid, int offsetX, int offsetY, LevelRandom rng, Func<GraphEdge, EdgeType> type,
    TerrainCache files, TileStore store, List<string> notes, IReadOnlyList<EdgePoint> edgePoints)
{
    /// <summary>The edge tiles placed for an edge type of the master's EdgePoints (edge tiling +0xa8): cell and the
    /// type's first index in the list, for <see cref="GroundStages.EdgePoints"/>.</summary>
    public readonly List<(int X, int Y, int Index)> EdgePointTiles = [];

    int W => graph.Width;
    int H => graph.Height;
    int[] _cost = [];
    bool[] _blocked = [];
    // Search state (0x20-byte cells: +4 parent + 1, +8 f, +0xc g, +0x10 h, +0x14 closed, +0x18 open-set node).
    int[] _parent = [];
    uint[] _f = [], _g = [], _h = [];
    bool[] _closed = [], _open = [];
    int _size;
    (int X, int Y) _endCell;

    sealed class PathRecord
    {
        public readonly List<(int X, int Y)> Cells = [];
        public List<(int X, int Y)> Points = [];
        public List<bool> Bits = [];
        public GraphNode LastNode = null!;
        public GraphEdge Edge = null!;
        public int End;
        public bool Loop;
        public (int X, int Y) OffStart = (-1, -1), OffEnd = (-1, -1);
        public readonly List<GraphEdge> Edges = [];
    }

    readonly List<(GraphEdge Edge, int End)> _placedEnds = [], _freeEnds = [];
    readonly List<PathRecord> _failed = [], _done = [];

    public void Run()
    {
        int count = W * H;
        _cost = new int[count];
        _blocked = new bool[count];
        _parent = new int[count];
        _f = new uint[count];
        _g = new uint[count];
        _h = new uint[count];
        _closed = new bool[count];
        _open = new bool[count];
        // 0x141f247e0.
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (Entry(x, y) is { IsOrigin: true } e)
                {
                    var (w, h) = Dims(e);
                    for (int v = 0; v < h; v++)
                        for (int u = 0; u < w; u++)
                            _blocked[(y + v) * W + x + u] = true;
                }
        foreach (var e in graph.Edges) Prepare(e);
        Distribute();
    }

    TileEntry? Entry(int x, int y) => (uint)x < (uint)W && (uint)y < (uint)H ? grid[x + offsetX, y + offsetY] : null;

    static (int W, int H) Dims(TileEntry e) =>
        (e.Orientation & 1) == 0 || e.Orientation > 7 ? (e.Tile.Width, e.Tile.Height) : (e.Tile.Height, e.Tile.Width);

    // The cell of a position, aligned down to the size.
    (int X, int Y) Aligned((int X, int Y) p) => (p.X / 23 - p.X / 23 % _size, p.Y / 23 - p.Y / 23 % _size);

    static (int X, int Y) Aligned((int X, int Y) p, int s) => (p.X / 23 - p.X / 23 % s, p.Y / 23 - p.Y / 23 % s);

    // 0x141f24b10: a cell under a placed tile becomes the tile's origin.
    (int X, int Y) ToOrigin((int X, int Y) c) => Entry(c.X, c.Y) is { } e ? (c.X - e.OffsetX, c.Y - e.OffsetY) : c;

    static (int X, int Y) Get(GraphEdge e, int i) =>
        i == 0 ? (e.A.X, e.A.Y) : i < 0 || i > e.Points.Count ? (e.B.X, e.B.Y) : e.Points[i - 1];

    static (int X, int Y) Next(GraphNode n, GraphEdge e)
    {
        if (n == e.A) return e.Points.Count == 0 ? (e.B.X, e.B.Y) : e.Points[0];
        return e.Points.Count == 0 ? (e.A.X, e.A.Y) : e.Points[^1];
    }

    /// <summary><c>0x14210db90</c>: an end offset becomes a point one step outside the end; the edge raises the cost
    /// grid; an edge with a placed end is tiled from it (its end cells are blocked), else from its end with one edge.</summary>
    void Prepare(GraphEdge e)
    {
        if (e.StartOffset != (0, 0)) e.Points.Insert(0, (e.A.X + e.StartOffset.X, e.A.Y + e.StartOffset.Y));
        if (e.EndOffset != (0, 0)) e.Points.Add((e.B.X + e.EndOffset.X, e.B.Y + e.EndOffset.Y));
        MarkEdge(e);
        if (e.A.Placed)
        {
            _placedEnds.Add((e, 0));
            BlockEnd(e, 0);
            if (e.B.Placed) BlockEnd(e, 1);
        }
        else if (e.B.Placed)
        {
            _placedEnds.Add((e, 1));
            BlockEnd(e, 1);
        }
        else _freeEnds.Add((e, e.A.Degree == 1 ? 0 : 1));
    }

    // Bresenham over positions (the generator's line walk), calling `cell` once per new aligned cell inside the map.
    void WalkCells((int X, int Y) a, (int X, int Y) b, int s, ref (int X, int Y) last, Action<(int X, int Y)> cell)
    {
        int adx = Math.Abs(b.X - a.X), ady = Math.Abs(b.Y - a.Y);
        bool steep = adx < ady;
        int major = steep ? a.Y : a.X, majorEnd = steep ? b.Y : b.X;
        int minor = steep ? a.X : a.Y, minorEnd = steep ? b.X : b.Y;
        int dMajor = Math.Abs(majorEnd - major), dMinor = Math.Abs(minorEnd - minor);
        int minorStep = minor < minorEnd ? 1 : minor == minorEnd ? 0 : -1;
        int majorStep = major < majorEnd ? 1 : -1;
        int err = -(dMajor / 2);
        while (major != majorEnd + majorStep)
        {
            var (x, y) = steep ? (minor, major) : (major, minor);
            if (x >= 0 && y >= 0 && x < W * 23 && y < H * 23)
            {
                var c = Aligned((x, y), s);
                if (c != last)
                {
                    last = c;
                    cell(c);
                }
            }
            int e2 = err + dMinor;
            major += majorStep;
            err = e2 - dMajor;
            if (e2 < 1) err = e2;
            else minor += minorStep;
        }
    }

    void Block((int X, int Y) c, int s, Action<int> each)
    {
        for (int v = 0; v < s; v++)
            for (int u = 0; u < s; u++)
                each((c.Y + v) * W + c.X + u);
    }

    /// <summary><c>0x141f25c90</c> / <c>0x141f4f5a0</c>: every block the edge's segments cross costs 80 more.</summary>
    void MarkEdge(GraphEdge e)
    {
        int s = type(e).Size;
        (int, int) last = (-1, -1);
        for (int k = 0; k <= e.Points.Count; k++)
            WalkCells(Get(e, k), Get(e, k + 1), s, ref last, c => Block(c, s, i => _cost[i] += 0x50));
    }

    /// <summary><c>0x141f25dc0</c> / <c>0x141f26060</c>: the edge's own raise comes off (60 per block, where above 79).</summary>
    void UnmarkEdge(GraphEdge e)
    {
        int s = type(e).Size;
        (int, int) last = (-1, -1);
        for (int k = 0; k <= e.Points.Count; k++)
            WalkCells(Get(e, k), Get(e, k + 1), s, ref last, c => Block(c, s, i => { if (_cost[i] > 0x4f) _cost[i] -= 0x3c; }));
    }

    /// <summary><c>0x141f261a0</c> / <c>0x141f26350</c>: a path segment's blocks get 20 cheaper (walked in the path's
    /// direction).</summary>
    void EaseSegment((int X, int Y) a, (int X, int Y) b, int s, bool reverse)
    {
        var (from, to) = reverse ? (b, a) : (a, b);
        (int, int) last = (-1, -1);
        WalkCells(from, to, s, ref last, c => Block(c, s, i => { if (_cost[i] > 0x13) _cost[i] -= 0x14; }));
    }

    /// <summary><c>0x141f25970</c>: the block of the edge's first point next to a placed end is blocked and dearer.</summary>
    void BlockEnd(GraphEdge e, int end)
    {
        int s = type(e).Size;
        _size = s;
        var p = Next(end == 0 ? e.A : e.B, e);
        int x = p.X / 23 - p.X / 23 % s, y = p.Y / 23 - p.Y / 23 % s;
        if ((uint)x >= (uint)W || (uint)y >= (uint)H) return;
        Block((x, y), s, i =>
        {
            _blocked[i] = true;
            _cost[i] += 0x50;
        });
    }

    /// <summary><c>0x14210de80</c>.</summary>
    void Distribute()
    {
        rng.Next((uint)_placedEnds.Count);
        for (int k = 0, left = _placedEnds.Count; left != 0; k++, left--)
        {
            int j = (int)rng.Uniform((uint)left);
            (_placedEnds[k], _placedEnds[k + j]) = (_placedEnds[k + j], _placedEnds[k]);
        }
        var sorted = _placedEnds.OrderByDescending(x => (sbyte)x.Edge.Priority).ToList();
        var ordered = sorted.Where(x => x.Edge.Split).Concat(sorted.Where(x => !x.Edge.Split)).ToList();
        foreach (var (e, end) in ordered) Process(e, end);
        foreach (var (e, end) in _freeEnds) Process(e, end);
        while (_failed.Count != 0)
        {
            foreach (var r in _done)
            {
                int s = type(r.Edge).Size;
                for (int k = 2; k < r.Cells.Count - 2; k++) Block(r.Cells[k], s, i => _blocked[i] = false);
            }
            foreach (var r in _failed)
            {
                if (!Tilise(r)) throw new InvalidOperationException($"Could not tilise edge {r.Edge}");
                Place(r);
            }
            _failed.Clear();
            foreach (var r in _done.ToList())
            {
                bool hit = false;
                for (int k = 1; k < r.Cells.Count - 1 && !hit; k++) hit = _blocked[r.Cells[k].Y * W + r.Cells[k].X];
                if (!hit) continue;
                for (int k = 1; k < r.Cells.Count - 1; k++)
                {
                    int i = r.Cells[k].Y * W + r.Cells[k].X;
                    if (_cost[i] > 0x4f) _cost[i] -= 0x50;
                }
                if (Tilise(r))
                {
                    for (int k = 1; k < r.Cells.Count - 1; k++) _cost[r.Cells[k].Y * W + r.Cells[k].X] += 0x50;
                    continue;
                }
                foreach (var e in r.Edges) MarkEdge(e);
                _failed.Add(r);
                _done.Remove(r);
            }
        }
        foreach (var r in _done) Place(r);
        notes.Add($"edge tiling: {_done.Count} paths, {_done.Sum(r => r.Cells.Count)} cells");
    }

    /// <summary><c>0x14210d2e0</c>: the chain from this end (through nodes with two edges) is tiled unless it lies
    /// within two neighbouring placed tiles (<c>0x14210b530</c>).</summary>
    void Process(GraphEdge edge, int end)
    {
        if (edge.Done) return;
        var start = end == 0 ? edge.A : edge.B;
        var node = start;
        var cur = edge;
        while (true)
        {
            node = cur.A == node ? cur.B : cur.A;
            if (node.Degree != 2 || node == start) break;
            var next = node.Slots.FirstOrDefault(s => s is not null && s != cur);
            if (next is null) break;
            cur = next;
            if (next.Done) break;
        }
        int size = type(edge).Size;
        if (WithinRooms(edge, end, node, size)) return;
        ExtraPoint(start);
        ExtraPoint(node);
        var rec = Record(edge, end, node);
        if (rec.Points.Count == 0) return;
        if (rec.Loop) throw new NotSupportedException("tiling a loop edge (0x14210e640, the PathInfo split) is not ported");
        if (!Tilise(rec))
        {
            _failed.Add(rec);
            foreach (var e in rec.Edges) MarkEdge(e);
            return;
        }
        _done.Add(rec);
        for (int k = 1; k < rec.Cells.Count - 1; k++) _cost[rec.Cells[k].Y * W + rec.Cells[k].X] += 0x50;
    }

    /// <summary><c>0x14210b530</c> with <c>0x14210b340</c>: both ends' cells hold neighbouring placed tiles and every
    /// point of the chain lies in one of the two.</summary>
    bool WithinRooms(GraphEdge edge, int end, GraphNode far, int s)
    {
        var start = end == 0 ? edge.A : edge.B;
        var a = Aligned((start.X, start.Y), s);
        var b = Aligned((far.X, far.Y), s);
        if (!Adjacent(a, b)) return false;
        bool rev = edge.A != start;
        int i = rev ? edge.Points.Count : 1;
        var node = start;
        var cur = edge;
        while (true)
        {
            if ((i == cur.Points.Count + 1 && !rev) || (i == 0 && rev))
            {
                node = cur.A == node ? cur.B : cur.A;
                if (node.Degree != 2 || node == start) return true;
                var next = node.Slots.FirstOrDefault(sl => sl is not null && sl != cur);
                if (next is null) return true;
                rev = next.A != node;
                i = rev ? next.Points.Count : 1;
                if (next.Done) return true;
                cur = next;
            }
            else i += rev ? -1 : 1;
            var c = Aligned(Get(cur, i), s);
            if (c != a && c != b) return false;
        }
    }

    bool Adjacent((int X, int Y) a, (int X, int Y) b)
    {
        if (Entry(a.X, a.Y) is not { } ea || Entry(b.X, b.Y) is not { } eb) return false;
        int ax = a.X - ea.OffsetX, ay = a.Y - ea.OffsetY, bx = b.X - eb.OffsetX, by = b.Y - eb.OffsetY;
        var (wa, ha) = Dims(ea);
        var (wb, hb) = Dims(eb);
        if (ax + wa != bx && bx + wb != ax)
            return (ha + ay == by || hb + by == ay) && ax <= bx + wb && bx <= ax + wa;
        return ay <= by + hb && by <= ha + ay;
    }

    /// <summary><c>0x14210b7f0</c>: a free inner node with one edge doubles itself as the edge's end point.</summary>
    void ExtraPoint(GraphNode n)
    {
        if (n.Placed || n.OnVirtual || n.Degree != 1) return;
        int tx = n.X / 23, ty = n.Y / 23;
        if (tx <= 0 || tx >= graph.Width - 1 || ty <= 0 || ty >= graph.Height - 1) return;
        var e = n.Slots.First(s => s is not null)!;
        if (e.A == n) e.Points.Insert(0, (n.X, n.Y));
        else e.Points.Add((n.X, n.Y));
    }

    /// <summary><c>0x14210b950</c>: the chain's positions from this end (walker <c>0x14211c690</c>; the chain's edges are
    /// marked done), cut back where it comes again into a cell it passed; a bit per step for its direction. Ends off the
    /// map are clamped, remembering their cells.</summary>
    PathRecord Record(GraphEdge edge, int end, GraphNode last)
    {
        var rec = new PathRecord { LastNode = last, Edge = edge, End = end };
        int s = type(edge).Size;
        var start = end == 0 ? edge.A : edge.B;
        // The walker: node, start node, edge, direction, index.
        var wNode = start;
        var wEdge = edge;
        bool wRev = edge.A != start;
        int wIdx = wRev ? edge.Points.Count : 1;
        var cur = (start.X, start.Y);
        var seen = new Dictionary<(int, int), int>();
        (int, int) Key((int X, int Y) p)
        {
            int cx = (int)((uint)(p.X + s * 23) / 23), cy = (int)((uint)(p.Y + s * 23) / 23);
            return (cx - cx % s - s, cy - cy % s - s);
        }
        var stepEdge = edge;
        int stepIdx = wIdx;
        bool stepRev = wRev;
        for (int step = 0; ; step++)
        {
            if (step < 2) rec.Points.Add(cur);
            else
            {
                var key = Key(cur);
                if (!seen.TryGetValue(key, out int at))
                {
                    seen[key] = rec.Points.Count;
                    rec.Points.Add(cur);
                }
                else
                {
                    for (int k = at + 1; k < rec.Points.Count; k++) seen.Remove(Key(rec.Points[k]));
                    rec.Points.RemoveRange(at + 1, rec.Points.Count - at - 1);
                    rec.Bits.RemoveRange(at, rec.Bits.Count - at);
                }
            }
            rec.Bits.Add(stepRev);
            cur = Get(stepEdge, stepIdx);
            if (!rec.Edges.Contains(stepEdge)) rec.Edges.Add(stepEdge);
            // 0x14211c690.
            bool advanced;
            if ((wIdx != wEdge.Points.Count + 1 || wRev) && (wIdx != 0 || !wRev))
            {
                wIdx += wRev ? -1 : 1;
                advanced = true;
            }
            else
            {
                wNode = wEdge.A == wNode ? wEdge.B : wEdge.A;
                advanced = false;
                if (wNode.Degree == 2 && wNode != start)
                {
                    wEdge.Done = true;
                    var next = wNode.Slots.FirstOrDefault(sl => sl is not null && sl != wEdge);
                    if (next is not null)
                    {
                        wEdge = next;
                        wRev = next.A != wNode;
                        wIdx = wRev ? next.Points.Count : 1;
                        advanced = !next.Done;
                    }
                }
            }
            if (!advanced)
            {
                rec.Points.Add(Get(wEdge, wIdx));
                wEdge.Done = true;
                break;
            }
            stepIdx = wIdx;
            stepRev = wRev;
            stepEdge = wEdge;
        }
        if (rec.Points.Count != 0)
        {
            var first = rec.Points[0];
            var lastPt = rec.Points[^1];
            int dx = first.X - lastPt.X, dy = first.Y - lastPt.Y;
            if ((float)(uint)(dy * dy + dx * dx) == 1.0f) rec.Points.Clear();
            else
            {
                bool Off((int X, int Y) p) => p.X < 0 || p.Y < 0 || p.X >= W * 23 || p.Y >= H * 23;
                (int, int) Clamp((int X, int Y) p) => (Math.Min(Math.Max(p.X, 0), W * 23 - 1), Math.Min(Math.Max(p.Y, 0), H * 23 - 1));
                if (Off(first))
                {
                    rec.OffStart = Key(first);
                    rec.Points[0] = Clamp(first);
                }
                if (Off(rec.Points[^1]))
                {
                    rec.OffEnd = Key(rec.Points[^1]);
                    rec.Points[^1] = Clamp(rec.Points[^1]);
                }
                rec.Loop = rec.Points[0] == rec.Points[^1];
            }
        }
        return rec;
    }

    (int X, int Y) ClampCell(GraphNode n, int s)
    {
        int x = Math.Min(Math.Max(n.X, 0), W * 23 - 1), y = Math.Min(Math.Max(n.Y, 0), H * 23 - 1);
        return ToOrigin((x / 23 - x / 23 % s, y / 23 - y / 23 % s));
    }

    /// <summary><c>0x14210c600</c>: the path's cells, segment by segment (<see cref="Search"/>), backing up when a
    /// segment finds no way (up to the last ten steps).</summary>
    bool Tilise(PathRecord r)
    {
        if (r.Points.Count < 2 || r.Bits.Count == 0) return false;
        int s = type(r.Edge).Size;
        r.Cells.Clear();
        var steps = new List<int>();
        var marks = new List<int>();
        int at = 0;
        if (!r.Loop)
        {
            var startNode = r.End == 0 ? r.Edge.A : r.Edge.B;
            if (r.OffStart != (-1, -1)) r.Cells.Add(r.OffStart);
            r.Cells.Add(ClampCell(startNode, s));
            at = 1;
            steps.Add(1);
            marks.Add(r.Cells.Count);
        }
        _size = s;
        _endCell = ToOrigin(Aligned(r.Points[r.Points.Count - at - 1]));
        foreach (var e in r.Edges) UnmarkEdge(e);
        int i = at + 1;
        EaseSegment(r.Points[i - 1], r.Points[i], s, r.Bits[at]);
        int floor = 0;
        bool found;
        do
        {
            int stop = !r.Loop || i != r.Points.Count - 1 ? r.Points.Count - 1 : r.Points.Count;
            found = Search(r, r.Points[at], i, stop);
            if (!found)
            {
                if (steps.Count == 0) return false;
                steps.RemoveAt(steps.Count - 1);
                if (marks.Count - 1 <= floor)
                {
                    marks.RemoveAt(marks.Count - 1);
                    for (int k = 2; k < r.Cells.Count; k++) ClearBlock(r.Cells[k], s);
                    r.Cells.Clear();
                    return false;
                }
                int keep = marks[^2];
                for (int k = keep; k < r.Cells.Count; k++) ClearBlock(r.Cells[k], s);
                at = steps[^1];
                marks.RemoveAt(marks.Count - 1);
                r.Cells.RemoveRange(keep, r.Cells.Count - keep);
            }
            else
            {
                int reached;
                while (true)
                {
                    var lastCell = r.Cells[^1];
                    var c = Aligned(r.Points[at + 1]);
                    if (lastCell == c) { reached = at; break; }
                    reached = at + 1;
                    if (r.Bits.Count <= at + 2) break;
                    EaseSegment(r.Points[reached], r.Points[reached + 1], s, r.Bits[reached]);
                    at = reached;
                }
                at = Math.Max(i, reached + 1);
                if (marks.Count == 0 || r.Cells.Count != marks[^1])
                {
                    steps.Add(at);
                    marks.Add(r.Cells.Count);
                    if (steps.Count > 10) floor = steps.Count - 10;
                }
                i = at + 1;
                if (i < r.Points.Count) EaseSegment(r.Points[i - 1], r.Points[i], s, r.Bits[at]);
            }
        } while ((r.Loop ? 0 : 1) + at < r.Bits.Count);
        if (!found)
        {
            r.Cells.Clear();
            return false;
        }
        if (!r.Loop)
        {
            var c = ClampCell(r.LastNode, s);
            if (r.Cells[^1] != c) r.Cells.Add(c);
            if (r.OffEnd != (-1, -1)) r.Cells.Add(r.OffEnd);
        }
        for (int k = 2; k < r.Cells.Count;)
        {
            if (r.Cells[k - 2] == r.Cells[k]) r.Cells.RemoveRange(k - 1, 2);
            else k++;
        }
        return r.Cells.Count != 0;
    }

    void ClearBlock((int X, int Y) c, int s) => Block(c, s, i =>
    {
        _cost[i] = 0;
        _blocked[i] = false;
    });

    bool FreeBlock((int X, int Y) c, int s)
    {
        for (int v = 0; v < s; v++)
            for (int u = 0; u < s; u++)
                if (grid[c.X + u + offsetX, c.Y + v + offsetY] is not null) return false;
        return true;
    }

    bool NoBlocked(int index, int s)
    {
        for (int v = 0; v < s; v++)
            for (int u = 0; u < s; u++)
                if (_blocked[index + v * W + u]) return false;
        return true;
    }

    // 0x141f24b90.
    uint BlockCost(int index, int s)
    {
        uint total = 1;
        for (int v = 0; v < s; v++)
            for (int u = 0; u < s; u++)
                total += (uint)_cost[index + v * W + u];
        return total / (uint)(s * s);
    }

    /// <summary><c>0x141f24d60</c>: from the path's points [first, stop) (seeds, g = 100 × block cost + points left;
    /// the first free) back to the start cell; a step costs 100 × (block cost + a pseudo-random 0..6 from two random
    /// weights + size), the estimate is the distance (100 × at the seeds). The path found is appended from the start
    /// (<c>0x141f25ae0</c>), its blocks blocked and 80 dearer.</summary>
    bool Search(PathRecord r, (int X, int Y) from, int first, int stop)
    {
        int s = _size;
        var start = ToOrigin(Aligned(from));
        var firstCell = ToOrigin(Aligned(r.Points[first]));
        Array.Clear(_parent);
        Array.Clear(_g);
        Array.Clear(_closed);
        Array.Clear(_open);
        var open = new SortedSet<(uint F, int Index)>();
        if (start == firstCell)
        {
            Reconstruct(r, start);
            return true;
        }
        int startIndex = start.Y * W + start.X;
        for (int k = first; k < stop; k++)
        {
            var c = ToOrigin(Aligned(r.Points[k]));
            int idx = c.Y * W + c.X;
            if ((uint)c.X >= (uint)W || (uint)c.Y >= (uint)H || c.X + s > W || c.Y + s > H || !FreeBlock(c, s)) continue;
            if (c != _endCell && !NoBlocked(idx, s)) continue;
            uint g = k == first ? 0 : BlockCost(idx, s) * 100 + (uint)(stop - k);
            int dx = Math.Abs(start.X - c.X), dy = Math.Abs(start.Y - c.Y);
            uint h = FortuneVoronoi.IntSqrt((uint)(dx * dx + dy * dy)) * 100;
            if (_open[idx])
            {
                if (_f[idx] <= h + g) continue;
                open.Remove((_f[idx], idx));
            }
            _g[idx] = g;
            _h[idx] = h;
            _f[idx] = h + g;
            open.Add((_f[idx], idx));
            _open[idx] = true;
        }
        uint e1 = (uint)rng.Next(0x4000u) + 20000;
        ulong wa = ((ulong)e1 + 1) * ((ulong)e1 + 1) + (ulong)e1 * e1;
        uint e2 = (uint)rng.Next(0x4000u) + 20000;
        ulong wb = ((ulong)e2 + 1) * ((ulong)e2 + 1) + (ulong)e2 * e2;
        while (open.Count != 0)
        {
            var top = open.Min;
            int cur = top.Index;
            if (cur == startIndex)
            {
                Reconstruct(r, start);
                return true;
            }
            open.Remove(top);
            _open[cur] = false;
            _closed[cur] = true;
            foreach (var nb in Neighbours((cur % W, cur / W), s)) Relax(cur, nb, startIndex, start, wa, wb, open);
        }
        return false;
    }

    // 0x141f256c0.
    List<(int X, int Y)> Neighbours((int X, int Y) c, int s)
    {
        var (w, h) = grid[c.X + offsetX, c.Y + offsetY] is { } e ? Dims(e) : (s, s);
        var list = new List<(int X, int Y)>();
        for (int x = 0; x < w; x += s)
        {
            list.Add((c.X + x, c.Y - s));
            list.Add((c.X + x, c.Y + h));
        }
        for (int y = 0; y < h; y += s)
        {
            list.Add((c.X - s, c.Y + y));
            list.Add((c.X + w, c.Y + y));
        }
        for (int k = 0; k < list.Count; k++) list[k] = ToOrigin(list[k]);
        return list;
    }

    // 0x141f25420.
    void Relax(int cur, (int X, int Y) nb, int startIndex, (int X, int Y) start, ulong wa, ulong wb, SortedSet<(uint F, int Index)> open)
    {
        int s = _size;
        if ((uint)nb.X >= (uint)W || (uint)nb.Y >= (uint)H) return;
        int idx = nb.Y * W + nb.X;
        if (_closed[idx] || _parent[cur] == idx + 1 || nb.X + s > W || nb.Y + s > H || !FreeBlock(nb, s)) return;
        if (idx != startIndex && !NoBlocked(idx, s)) return;
        uint c = BlockCost(idx, s);
        ulong mix = (ulong)(long)(nb.X + 1) * wa + (ulong)(long)(nb.Y + 1) * wb;
        uint q = (uint)(mix % 707 / 101);
        uint g = unchecked((c + q + (uint)s) * 100 + _g[cur]);
        if (!_open[idx])
        {
            int dx = Math.Abs(start.X - nb.X), dy = Math.Abs(start.Y - nb.Y);
            _h[idx] = FortuneVoronoi.IntSqrt((uint)(dy * dy + dx * dx));
        }
        else
        {
            if (_g[idx] <= g) return;
            open.Remove((_f[idx], idx));
        }
        _g[idx] = g;
        _f[idx] = _h[idx] + g;
        open.Add((_f[idx], idx));
        _open[idx] = true;
        _parent[idx] = cur + 1;
    }

    void Reconstruct(PathRecord r, (int X, int Y) start)
    {
        int s = _size;
        void Take(int index) => Block((index % W, index / W), s, i =>
        {
            _blocked[i] = true;
            _cost[i] += 0x50;
        });
        int at = start.Y * W + start.X;
        if (r.Cells.Count == 0 || r.Cells[^1] != start)
        {
            r.Cells.Add(start);
            Take(at);
        }
        while (_parent[at] != 0)
        {
            at = _parent[at] - 1;
            Take(at);
            r.Cells.Add((at % W, at / W));
        }
    }

    /// <summary><c>0x14210f470</c>: one tile per cell: the key has the edge type on the sides toward the previous and next
    /// cell (<c>0x14210ee30</c>), edge points matching a tile already there (<c>0x141ffbf40</c>) or from the path's
    /// smoothed curve (<c>0x14210e9f0</c>, <c>0x14210f0f0</c>), corner grounds from the type (<c>0x14210f2b0</c>), size
    /// × size; <c>Next(total weight)</c> among the TileSet's untagged tiles for it.</summary>
    void Place(PathRecord r)
    {
        var et = type(r.Edge);
        int s = et.Size;
        bool fixedPoints = et.N2;
        List<(float X, float Y)>? curve = null;
        if (!fixedPoints)
        {
            if (et.Wiggle != 0) throw new NotSupportedException($"edge wiggle ({et.Path}, 0x140df93f0) is not ported");
            if (r.Loop) throw new NotSupportedException("the loop curve (0x14210e640) is not ported");
            curve = Curve(r.Cells, s);
        }
        int pointIndex = -1;
        for (int i = 0; i < edgePoints.Count && pointIndex < 0; i++)
            if (string.Equals(edgePoints[i].Edge, r.Edge.Type, StringComparison.OrdinalIgnoreCase)) pointIndex = i;
        int first = r.Loop ? 0 : 1, last = r.Cells.Count - first;
        int n = r.Cells.Count;
        for (int u = first; u < last; u++)
        {
            var cell = r.Cells[u];
            var prev = r.Cells[(n - 1 + u) % n];
            var next = r.Cells[(u + 1) % n];
            int sPrev = Side(cell, prev, s), sNext = Side(cell, next, s);
            var key = new TileKey { Width = (byte)s, Height = (byte)s };
            for (int k = 0; k < 4; k++) key.PointA[k] = key.PointB[k] = (byte)(3 * s);
            key.Edges[sNext] = r.Edge.Type;
            key.Edges[sPrev] = r.Edge.Type;
            if (!fixedPoints)
            {
                key.PointA[sPrev] = (byte)(Entry(prev.X, prev.Y) is not null ? Match(cell, prev, sPrev, s) : CurvePoint(sPrev, curve![u - first], s));
                key.PointA[sNext] = (byte)(Entry(next.X, next.Y) is not null ? Match(cell, next, sNext, s) : CurvePoint(sNext, curve![u - first + 1], s));
            }
            else key.PointA[sNext] = key.PointA[sPrev] = (byte)(3 * s / 2);
            Grounds(key, sPrev, sNext, r.Edge, r.End != 0);
            var list = store.Candidates("", key);
            if (list.Count == 0) throw new InvalidOperationException($"Failed to tilise edge: Could not find matching tile at ({cell.X}, {cell.Y}) for edge type {r.Edge.Type}: {key}");
            int pick = (int)rng.Next((uint)list.Sum(c => c.Weight));
            var chosen = list[^1];
            foreach (var c in list)
            {
                if (pick < c.Weight) { chosen = c; break; }
                pick -= c.Weight;
            }
            grid.Place(chosen.Tile, chosen.Orientation, cell.X + offsetX, cell.Y + offsetY, 0);
            if (pointIndex >= 0) EdgePointTiles.Add((cell.X, cell.Y, pointIndex));
        }
    }

    /// <summary><c>0x14210ee30</c>: which side of the tile at a the cell b lies on.</summary>
    int Side((int X, int Y) a, (int X, int Y) b, int s)
    {
        var (wa, ha) = Entry(a.X, a.Y) is { } ea ? Dims(ea) : (s, s);
        if (b.X == a.X + wa) return 1;
        if (b.Y == a.Y + ha) return 2;
        var (wb, hb) = Entry(b.X, b.Y) is { } eb ? Dims(eb) : (s, s);
        if (a.Y == b.Y + hb) return 0;
        if (a.X != b.X + wb)
        {
            if (b.X == a.X + s) return 1;
            if (b.Y == a.Y + s) return 2;
            if (a.Y == b.Y + s) return 0;
            if (a.X != b.X + s) throw new InvalidOperationException($"Cannot find side: tiles are not adjacent ({a.X}, {a.Y}) ({b.X}, {b.Y})");
        }
        return 3;
    }

    /// <summary><c>0x14210e9f0</c>: per step between cells, where a 10-point Bézier (t = ½) through the cell centres (the
    /// first and last repeated four times) passes, relative to the step's middle.</summary>
    static List<(float X, float Y)> Curve(List<(int X, int Y)> cells, int s)
    {
        uint half = (uint)(s >> 1) & 0x7fffffff;
        (float X, float Y) Centre((int X, int Y) c) =>
            (((float)(uint)(c.X + half) + 0.5f) * 23f, ((float)(uint)(c.Y + half) + 0.5f) * 23f);
        var pts = new List<(float X, float Y)>();
        var c0 = Centre(cells[0]);
        for (int k = 0; k < 4; k++) pts.Add(c0);
        foreach (var c in cells) pts.Add(Centre(c));
        var cl = pts[^1];
        for (int k = 0; k < 4; k++) pts.Add(cl);
        var result = new List<(float X, float Y)>();
        for (int i = 0; i < cells.Count - 1; i++)
        {
            var w = new (float X, float Y)[10];
            for (int k = 0; k < 10; k++) w[k] = pts[i + k];
            var mid = ((w[4].X + w[5].X) * (1f / 2f), (w[4].Y + w[5].Y) * (1f / 2f));
            for (int k = 0; k < 10; k++)
                for (int j = 0; j < 10 - k - 1; j++)
                {
                    var a = (0.5f * w[j].X, 0.5f * w[j].Y);
                    var b = (0.5f * w[j + 1].X, 0.5f * w[j + 1].Y);
                    w[j] = (b.Item1 + a.Item1, b.Item2 + a.Item2);
                }
            result.Add((w[0].X - mid.Item1, w[0].Y - mid.Item2));
        }
        return result;
    }

    /// <summary><c>0x14210f0f0</c>: the edge point on a side from the curve's lean: one of three positions.</summary>
    static int CurvePoint(int side, (float X, float Y) d, int s)
    {
        float f = (float)(ulong)(s * 23) / 6.0f;
        int half = s >> 1;
        switch (side)
        {
            case 0:
            case 1:
            {
                float v = side == 0 ? d.X : d.Y;
                int k = 0;
                if (-f <= v) k = (f < v ? 1 : 0) + 1;
                return k * s + half;
            }
            case 2:
            case 3:
            {
                float v = side == 2 ? d.X : d.Y;
                if (-f <= v) return (v <= f ? 1 : 0) * s + half;
                return s * 2 + half;
            }
            default:
                return s * 3;
        }
    }

    static readonly byte[] TurnBack = [0, 3, 2, 1, 4, 5, 6, 7, 8];
    static readonly byte[] SideShiftW = [0, 0, 1, 1, 1, 1, 2, 2, 0], SideShiftH = [0, 1, 1, 2, 0, 1, 1, 2, 0];

    // 0x141d75a50 (1, 1, side, code): a world side in a turned tile's own frame.
    static int LocalSide(int side, int code)
    {
        code = Math.Min(code, 8);
        if (code >= 4 && code < 8) side = 4 - side - 1;
        return (side + SideShiftW[code] + SideShiftH[code]) % 4;
    }

    /// <summary><c>0x141ffbf40</c>: the edge point on our side that meets the neighbouring tile's edge point (read on the
    /// neighbour's facing side, in its own frame).</summary>
    int Match((int X, int Y) cell, (int X, int Y) nb, int side, int s)
    {
        var e = Entry(nb.X, nb.Y) ?? throw new InvalidOperationException("Neighbour does not exist");
        side = (side - 2) & 3; // the neighbour's side facing us
        var (wr, hr) = Dims(e);
        var tile = e.Tile;
        int o = e.Orientation;
        int local = LocalSide(side, TurnBack[Math.Min(o, 8)]);
        int p = tile.Key.PointA[local];
        if (o >= 4 && o < 8) p = 3 * ((local & 1) == 0 ? tile.Width : tile.Height) - p - 1;
        int PointB()
        {
            int l = LocalSide(side, TurnBack[Math.Min(o, 8)]);
            int v = tile.Key.PointB[l];
            if (o >= 4 && o < 8)
            {
                int dim = 3 * ((l & 1) == 0 ? tile.Width : tile.Height);
                if (v < dim) return dim - v - 1;
            }
            return v;
        }
        int ox = nb.X - e.OffsetX, oy = nb.Y - e.OffsetY;
        switch (side)
        {
            case 0:
            {
                int c = p / 3 + ox;
                if (cell.X <= c && c < cell.X + s) return (cell.X - ox + s) * 3 - p - 1;
                int q = PointB();
                if (q == wr * 3) throw new InvalidOperationException($"Could not connect edge tiles from ({cell.X}, {cell.Y}) to ({nb.X}, {nb.Y})");
                return (cell.X - ox + s) * 3 - q - 1;
            }
            case 1:
            {
                int c = p / 3 + oy;
                if (cell.Y <= c && c < cell.Y + s) return (cell.Y - oy + s) * 3 - p - 1;
                int q = PointB();
                if (q == hr * 3) throw new InvalidOperationException($"Could not connect edge tiles from ({cell.X}, {cell.Y}) to ({nb.X}, {nb.Y})");
                return (cell.Y - oy + s) * 3 - q - 1;
            }
            case 2:
            {
                int c = ox - p / 3 - 1 + wr;
                if (cell.X <= c && c < cell.X + s) return (ox - cell.X + wr) * 3 - p - 1;
                int q = PointB();
                if (q == wr * 3) throw new InvalidOperationException($"Could not connect edge tiles from ({cell.X}, {cell.Y}) to ({nb.X}, {nb.Y})");
                return (ox - cell.X + wr) * 3 - q - 1;
            }
            case 3:
            {
                int c = oy - p / 3 - 1 + hr;
                if (cell.Y <= c && c < cell.Y + s) return (oy - cell.Y + hr) * 3 - p - 1;
                int q = PointB();
                if (q == hr * 3) throw new InvalidOperationException($"Could not connect edge tiles from ({cell.X}, {cell.Y}) to ({nb.X}, {nb.Y})");
                return (oy - cell.Y + hr) * 3 - q - 1;
            }
            default:
                return s * 3;
        }
    }

    // 0x142fc00b0: the corner left between two sides that are not opposite (4 = none).
    static readonly int[] MissingCorner = [4, 3, 4, 2, 3, 4, 0, 4, 4, 0, 4, 1, 2, 4, 1, 4];

    /// <summary><c>0x14210f2b0</c> / <c>0x14210f1b0</c>: the corners along each edge side take the type's two grounds
    /// (heights 0 and the type's rise) by the edge's chirality; at a turn the remaining corner copies its neighbour.</summary>
    void Grounds(TileKey key, int sPrev, int sNext, GraphEdge e, bool fromB)
    {
        if (sPrev == sNext) throw new InvalidOperationException("Edge tilization failure: side indices are equal!?");
        Side(key, sPrev, e, fromB);
        Side(key, sNext, e, !fromB);
        if (((sPrev - 2) & 3) != sNext)
        {
            int c = MissingCorner[(sNext & 3) + (sPrev & 3) * 4];
            if (c == 4) throw new InvalidOperationException("Bad corner index for missing corner");
            int from = (c + 1) & 3;
            key.Grounds[c] = key.Grounds[from];
            key.Heights[c] = key.Heights[from];
        }
    }

    void Side(TileKey key, int side, GraphEdge e, bool flag)
    {
        int a = side, b = (side + 1) & 3;
        if ((e.Chirality != 0) != flag) (a, b) = (b, a);
        var et = type(e);
        key.Grounds[a] = KeyGround(et.Ground1);
        key.Heights[a] = 0;
        key.Grounds[b] = KeyGround(et.Ground2);
        key.Heights[b] = (sbyte)et.N0;
    }

    // Grounds flagged as left out of keys are left out here too (as the tile keys have them).
    string? KeyGround(string? g) => files.Ground(g) is { IgnoredInKeys: true } ? null : g;
}
