namespace GameServer.Data.Terrain.Generation;

/// <summary>The edge router of the tile graph layout (<c>0x142124600</c> builds it, <c>0x142125790</c> runs it): a cost
/// grid of one cell per graph tile (23 × 23 positions), random costs with a dearer border, then every edge routed
/// through it.</summary>
sealed class EdgeRouter
{
    const int Cell = 23;
    readonly TileGraph _graph;
    readonly TileGrid _grid;
    readonly int _offsetX, _offsetY;
    readonly LevelRandom _rng;
    public readonly int Width, Height;
    /// <summary>Per cell (row by row, 12 bytes in the client): cost, corridor count, occupied by a placed tile.</summary>
    public readonly int[] Cost;
    public readonly int[] Corridor;
    public readonly bool[] Occupied;

    readonly Func<GraphEdge, EdgeType> _type;

    public EdgeRouter(TileGraph graph, TileGrid grid, int offsetX, int offsetY, LevelRandom rng, Func<GraphEdge, EdgeType> type)
    {
        _type = type;
        _graph = graph;
        _grid = grid;
        _offsetX = offsetX;
        _offsetY = offsetY;
        _rng = rng;
        Width = graph.CellWidth / Cell;
        Height = graph.CellHeight / Cell;
        Cost = new int[Width * Height];
        Corridor = new int[Width * Height];
        Occupied = new bool[Width * Height];
    }

    /// <summary><c>0x142125790</c> up to the routing: every cell's cost is <c>Next(1024)</c>, the border cells' 1024 more;
    /// every edge's straight corridors are marked (<see cref="Mark"/>); cells under a placed tile are occupied.</summary>
    public void Prepare()
    {
        for (int i = 0; i < Cost.Length; i++)
        {
            Cost[i] = (int)_rng.Next(0x400u);
            Corridor[i] = 0;
        }
        for (int x = 0; x < Width; x++)
        {
            Cost[x] += 0x400;
            Cost[(Height - 1) * Width + x] += 0x400;
        }
        for (int y = 1; Height != 1 && Height != 2 && y < Height - 1; y++)
        {
            Cost[y * Width] += 0x400;
            Cost[(y + 1) * Width - 1] += 0x400;
        }
        foreach (var e in _graph.Edges) Mark(e);
        for (int y = 0; y < _graph.Height; y++)
            for (int x = 0; x < _graph.Width; x++)
                Occupied[y * Cell / Cell * Width + x * Cell / Cell] = _grid[x + _offsetX, y + _offsetY] is not null;
    }

    /// <summary><c>0x142125790</c>: the cost grid is prepared (<see cref="Prepare"/>), then every edge routed (<see cref="Route"/>); edges that fail are marked again and retried until a round fixes none.</summary>
    public void Run()
    {
        Prepare();

        var failed = new List<GraphEdge>();
        foreach (var e in _graph.Edges)
            if (!Route(e))
            {
                failed.Add(e);
                Mark(e);
            }
        while (failed.Count > 0)
        {
            var again = new List<GraphEdge>();
            foreach (var e in failed)
                if (!Route(e))
                {
                    again.Add(e);
                    Mark(e);
                }
            if (again.Count == failed.Count) throw new InvalidOperationException("Could not permute edges: " + string.Join(", ", again));
            failed = again;
        }
    }

    // The corridor width of an edge: its type's size + 2, or for a virtual edge its max width in cells + 3.
    int CorridorWidth(GraphEdge e) => _type(e) is { Virtual: true } ? e.MaxWidth / Cell + 3 : _type(e).Size + 2;

    // Point i of an edge in cells: 0 = its first node (+ start offset), last = its second node (+ end offset), else
    // an internal point.
    static (int X, int Y) Point(GraphEdge e, int i)
    {
        int n = e.Points.Count;
        if (i == 0) return (e.A.X / Cell + e.StartOffset.X, e.A.Y / Cell + e.StartOffset.Y);
        if (i == n + 1) return (e.B.X / Cell + e.EndOffset.X, e.B.Y / Cell + e.EndOffset.Y);
        var p = i > n ? (e.B.X, e.B.Y) : e.Points[i - 1];
        return (p.X / Cell, p.Y / Cell);
    }

    // Point i as the edge holds it (positions, not cells).
    static (int X, int Y) Raw(GraphEdge e, int i) => i > e.Points.Count ? (e.B.X, e.B.Y) : e.Points[i - 1];

    /// <summary><c>0x142125eb0</c>: the straight corridor of each of the edge's segments.</summary>
    void Mark(GraphEdge e)
    {
        int w = CorridorWidth(e);
        for (int k = 0; k <= e.Points.Count; k++) Line(Point(e, k), Point(e, k + 1), 1, w);
    }

    /// <summary><c>0x142124920</c>: walks the line from a to b (Bresenham, along the longer axis), adding
    /// <paramref name="delta"/> to each cell's corridor count (<c>0x1421247e0</c>) and to the cost of the cells beside it
    /// (up to <paramref name="width"/> away, across the line) a penalty falling from 2000 · delta.</summary>
    void Line((int X, int Y) a, (int X, int Y) b, int delta, int width)
    {
        int adx = Math.Abs(b.X - a.X), ady = Math.Abs(b.Y - a.Y);
        bool steep = adx < ady;
        int major = steep ? a.Y : a.X, majorEnd = steep ? b.Y : b.X;
        int minor = steep ? a.X : a.Y, minorEnd = steep ? b.X : b.Y;
        int dMajor = Math.Abs(majorEnd - major), dMinor = Math.Abs(minorEnd - minor);
        int minorStep = minor < minorEnd ? 1 : minor == minorEnd ? 0 : -1;
        int majorStep = major < majorEnd ? 1 : -1;
        int sx = steep ? 1 : 0, sy = steep ? 0 : 1;
        int err = -(dMajor / 2);
        while (major != majorEnd + majorStep)
        {
            var (x, y) = steep ? (minor, major) : (major, minor);
            if (x >= 0 && y >= 0 && x < Width && y < Height) Corridor[y * Width + x] += delta;
            for (uint k = 1; k <= (uint)width; k++)
            {
                int amount = (int)(((uint)width - k + 1) * 200000u / (uint)width) * delta / 100;
                int lx = x - (int)k * sx, ly = y - (int)k * sy, rx = x + (int)k * sx, ry = y + (int)k * sy;
                if (lx >= 0 && ly >= 0 && lx < Width && ly < Height) Cost[ly * Width + lx] += amount;
                if (rx >= 0 && ry >= 0 && rx < Width && ry < Height) Cost[ry * Width + rx] += amount;
            }
            int e2 = err + dMinor;
            major += majorStep;
            err = e2 - dMajor;
            if (e2 < 1) err = e2;
            else minor += minorStep;
        }
    }

    // 0x142124c70's per-cell state (0x18 bytes in the client): g, h, parent index + 1, closed, in the open set.
    int[] _g = [], _h = [], _parent = [];
    bool[] _closed = [], _open = [];

    /// <summary><c>0x142124c70</c>: A* over the cells from start to goal, neighbours left, up, right, down. A cell costs
    /// 100 + its cost (the client does not add the path so far), the estimate is 100 per cell of Manhattan distance;
    /// the open set is ordered by g + h, then cell index. Cells with a corridor or a placed tile are closed except the
    /// goal.</summary>
    bool Search((int X, int Y) start, (int X, int Y) goal)
    {
        int count = Width * Height;
        if (_g.Length != count)
        {
            _g = new int[count];
            _h = new int[count];
            _parent = new int[count];
            _closed = new bool[count];
            _open = new bool[count];
        }
        Array.Clear(_g);
        Array.Clear(_h);
        Array.Clear(_parent);
        Array.Clear(_closed);
        Array.Clear(_open);
        if (start.X < 0 || start.Y < 0 || start.X >= Width || start.Y >= Height)
            throw new InvalidOperationException("Initial A* node is out-of-bounds!");
        var open = new SortedSet<(uint F, uint Index)>();
        void Insert(int index)
        {
            if (open.Add(((uint)(_g[index] + _h[index]), (uint)index))) _open[index] = true;
        }
        Insert(start.Y * Width + start.X);
        Span<(int X, int Y)> next = stackalloc (int, int)[4];
        while (open.Count > 0)
        {
            var top = open.Min;
            int cur = (int)top.Index, cx = cur % Width, cy = cur / Width;
            if (cx == goal.X && cy == goal.Y) return true;
            open.Remove(top);
            _open[cur] = false;
            _closed[cur] = true;
            int k = 0;
            if (cx > 0) next[k++] = (cx - 1, cy);
            if (cy > 0) next[k++] = (cx, cy - 1);
            if (cx + 1 < Width) next[k++] = (cx + 1, cy);
            if (cy + 1 < Height) next[k++] = (cx, cy + 1);
            for (int j = 0; j < k; j++)
            {
                var (nx, ny) = next[j];
                int i = ny * Width + nx;
                if (_closed[i]) continue;
                bool isGoal = nx == goal.X && ny == goal.Y;
                if (!isGoal && (_parent[i] == i + 1 || Corridor[i] + (Occupied[i] ? 1 : 0) != 0)) continue;
                int g = unchecked(_g[i] + 100 + Cost[i]);
                if (!_open[i]) _h[i] = (Math.Abs(goal.X - nx) + Math.Abs(goal.Y - ny)) * 100;
                else
                {
                    if ((uint)g >= (uint)_g[i]) continue;
                    open.Remove(((uint)(_g[i] + _h[i]), (uint)i));
                    _open[i] = false;
                }
                _g[i] = g;
                Insert(i);
                _parent[i] = cur + 1;
            }
        }
        return false;
    }

    /// <summary><c>0x142124fc0</c>: each segment's straight corridor is taken away and the segment searched from its end
    /// back to its start; the path's cells get the corridor instead, and every sixth cell becomes a new internal point
    /// at a random position in it (<c>Next(23)</c> twice). A segment with no path is skipped (its end point is dropped);
    /// the last one failing undoes the edge's corridors and fails.</summary>
    bool Route(GraphEdge e)
    {
        int w = CorridorWidth(e), n = e.Points.Count;
        var points = new List<(int X, int Y)>();
        var path = new List<(int X, int Y)>();
        int last = 0;
        for (int i = 1; i < n + 2; i++)
        {
            var from = Point(e, last);
            var to = Point(e, i);
            if (from == to)
            {
                if (last != 0) points.Add(Raw(e, last));
                continue;
            }
            Line(Point(e, i - 1), to, -1, w);
            if (!Search(to, from))
            {
                if (i == n + 1)
                {
                    for (int k = 1; k < path.Count; k++) Line(path[k - 1], path[k], -1, w);
                    return false;
                }
                continue;
            }
            if (last != 0)
            {
                var raw = Raw(e, last);
                if (points.Count == 0 || points[^1] != raw) points.Add(raw);
            }
            last = i;
            int target = to.Y * Width + to.X;
            int at = _parent[from.Y * Width + from.X] - 1, steps = 0;
            var prev = from;
            while (at != target)
            {
                (int X, int Y) c = (at % Width, at / Width);
                path.Add(c);
                Line(prev, c, 1, w);
                steps++;
                prev = c;
                if (steps % 6 == 0)
                {
                    int rx = (int)_rng.Next(Math.Min((uint)Cell, (uint)(_graph.CellWidth - c.X * Cell)));
                    int ry = (int)_rng.Next(Math.Min((uint)Cell, (uint)(_graph.CellHeight - c.Y * Cell)));
                    var p = (c.X * Cell + rx, c.Y * Cell + ry);
                    if (points.Count == 0 || points[^1] != p) points.Add(p);
                }
                at = _parent[at] - 1;
            }
            path.Add(to);
            Line(to, prev, 1, w);
        }
        e.Points = points;
        return true;
    }
}
