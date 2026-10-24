namespace GameServer.Data.Terrain.Generation;

/// <summary>The Voronoi graph <c>0x141f230e0</c> builds around a tile graph's points (its nodes, then every edge's
/// internal points): vertices as nodes, segments and border pieces as edges, and per site point the edges around its
/// cell (in the order the client appends them). <see cref="Jitter"/> is <c>0x141f22460</c>.</summary>
public sealed class VoronoiCells
{
    public sealed class VNode(int x, int y)
    {
        public int X = x, Y = y;
        public readonly VEdge?[] Slots = new VEdge?[4];
        public int Degree => Slots.Count(s => s is not null);
    }

    public sealed class VEdge(VNode a, VNode b)
    {
        public VNode A = a, B = b;
    }

    readonly TileGraph _graph;
    public readonly List<VNode> Nodes = [];
    public readonly List<VEdge> Edges = [];
    /// <summary>Per site point, the edges of its cell (<c>param_1 + 0x10</c>).</summary>
    public readonly Dictionary<(int X, int Y), List<VEdge>> Cells = [];
    // Per Voronoi node, the site points around it, ordered by x · 65536 + y (std::set, 0x141f4f390).
    readonly Dictionary<VNode, SortedSet<(int X, int Y)>> _nodeSites = [];

    static readonly IComparer<(int X, int Y)> SiteOrder =
        Comparer<(int X, int Y)>.Create((a, b) => unchecked(a.X * 0x10000 + a.Y).CompareTo(unchecked(b.X * 0x10000 + b.Y)));

    public VoronoiCells(TileGraph graph)
    {
        _graph = graph;
        var points = new List<(int X, int Y)>();
        foreach (var n in graph.Nodes) points.Add((n.X, n.Y));
        foreach (var e in graph.Edges) points.AddRange(e.Points);
        if (graph.Nodes.Count == 0) return;
        int w = graph.CellWidth, h = graph.CellHeight;
        var v = new FortuneVoronoi(points, w, h);

        foreach (var p in v.EndVertices)
            if (!Nodes.Any(n => n.X == p.X && n.Y == p.Y)) Nodes.Add(new VNode(p.X, p.Y));
        foreach (var s in v.Segments)
        {
            var a = Find(s.X1, s.Y1) ?? throw new InvalidOperationException($"Voronoi creation failed!? no vertex at ({s.X1}, {s.Y1}) of {s}; " + v.Describe());
            var b = Find(s.X2, s.Y2) ?? throw new InvalidOperationException($"Voronoi creation failed!? no vertex at ({s.X2}, {s.Y2}) of {s}; " + v.Describe());
            if (a == b) continue;
            if (a.Slots.Any(e => e is not null && (e.A == b || e.B == b))) continue;
            var edge = Connect(a, b);
            var s1 = points[s.Site1];
            var s2 = points[s.Site2];
            Cell(s1).Add(edge);
            Cell(s2).Add(edge);
            Sites(a).Add(s1);
            Sites(a).Add(s2);
            Sites(b).Add(s1);
            Sites(b).Add(s2);
        }
        Nodes.Add(new VNode(0, 0));
        Nodes.Add(new VNode(0, h - 1));
        Nodes.Add(new VNode(w - 1, 0));
        Nodes.Add(new VNode(w - 1, h - 1));

        // 0x141f58e30: nodes with at most two edges on the border.
        List<VNode> right = [], left = [], bottom = [], top = [];
        foreach (var n in Nodes)
        {
            if (n.Degree > 2) continue;
            if (n.X >= w - 1) right.Add(n); else if (n.X == 0) left.Add(n);
            if (n.Y >= h - 1) bottom.Add(n); else if (n.Y == 0) top.Add(n);
        }
        left = [.. left.OrderBy(n => n.Y)];
        right = [.. right.OrderBy(n => n.Y)];
        top = [.. top.OrderBy(n => n.X)];
        bottom = [.. bottom.OrderBy(n => n.X)];
        Corner(left[0], left[1], -1);
        Corner(left[^1], left[^2], 1);
        Corner(right[0], right[1], 1);
        Corner(right[^1], right[^2], -1);
        Corner(top[0], top[1], 1);
        Corner(top[^1], top[^2], -1);
        Corner(bottom[0], bottom[1], -1);
        Corner(bottom[^1], bottom[^2], 1);
        Border(left, 1);
        Border(right, -1);
        Border(top, -1);
        Border(bottom, 1);
    }

    // 0x141f4f4b0 with 0x141f5bf30: the node at (x, y), else the nearest one (squared distance, first of equals).
    VNode? Find(int x, int y)
    {
        VNode? found = null;
        uint best = uint.MaxValue;
        foreach (var n in Nodes)
        {
            if (n.X == x && n.Y == y)
            {
                found = n;
                best = 0;
                continue;
            }
            int dx = Math.Abs(x - n.X), dy = Math.Abs(y - n.Y);
            uint d = unchecked((uint)(dy * dy + dx * dx));
            if (d < best)
            {
                best = d;
                found = n;
            }
        }
        return found;
    }

    List<VEdge> Cell((int, int) site) => Cells.TryGetValue(site, out var l) ? l : Cells[site] = [];

    SortedSet<(int X, int Y)> Sites(VNode n) => _nodeSites.TryGetValue(n, out var s) ? s : _nodeSites[n] = new(SiteOrder);

    /// <summary><c>0x141f4e930</c>.</summary>
    VEdge Connect(VNode a, VNode b)
    {
        var e = new VEdge(a, b);
        int i = Array.IndexOf(a.Slots, null);
        int j = Array.IndexOf(b.Slots, null);
        if (i < 0 || j < 0) throw new InvalidOperationException("Too many edges for node");
        a.Slots[i] = e;
        b.Slots[j] = e;
        Edges.Add(e);
        return e;
    }

    // The point next along the first edge of n (an edge with no internal points: its other node).
    static (int X, int Y) NextAlong(VNode n)
    {
        var e = n.Slots.First(s => s is not null)!;
        var o = n == e.A ? e.B : e.A;
        return (o.X, o.Y);
    }

    /// <summary><c>0x141f22e50</c>: a border corner takes, from its neighbour on the border, the neighbour's site on the
    /// given side of the neighbour's Voronoi edge.</summary>
    void Corner(VNode to, VNode from, int sign)
    {
        if (!_nodeSites.TryGetValue(from, out var sites) || from.Degree != 1) return;
        var p = NextAlong(from);
        foreach (var s in sites)
        {
            int cross = unchecked((s.Y - from.Y) * (p.X - from.X) - (s.X - from.X) * (p.Y - from.Y));
            if ((cross > 0) == (sign > 0))
            {
                Sites(to).Add(s);
                return;
            }
        }
    }

    /// <summary><c>0x141f227c0</c>: edges along one border between its nodes in order, each added to the cell on the
    /// inner side.</summary>
    void Border(List<VNode> list, int sign)
    {
        var first = Connect(list[0], list[1]);
        if (list.Count == 2)
        {
            var other = Sites(list[1]);
            foreach (var s in Sites(list[0]))
                if (other.Contains(s))
                {
                    Cell(s).Add(first);
                    return;
                }
            return;
        }
        {
            var n = list[1];
            var p = NextAlong(n);
            foreach (var s in Sites(n))
            {
                int cross = unchecked((s.Y - p.Y) * (n.X - p.X) - (s.X - p.X) * (n.Y - p.Y));
                if ((cross > 0) == (sign > 0))
                {
                    Cell(s).Add(first);
                    break;
                }
            }
        }
        for (int i = 1; i < list.Count - 1; i++)
        {
            var e = Connect(list[i], list[i + 1]);
            var n = list[i];
            var p = NextAlong(n);
            foreach (var s in Sites(n))
            {
                int cross = unchecked((s.Y - n.Y) * (p.X - n.X) - (s.X - n.X) * (p.Y - n.Y));
                if ((cross > 0) == (sign > 0))
                {
                    Cell(s).Add(e);
                    break;
                }
            }
        }
    }

    /// <summary><c>0x141f22460</c>: every unlocked node moves to a random point of its cell, then every edge's internal
    /// points.</summary>
    public void Jitter(LevelRandom rng)
    {
        foreach (var n in _graph.Nodes)
        {
            var (dx, dy) = RandomOffset(rng, Cells.GetValueOrDefault((n.X, n.Y)) ?? [], n.X, n.Y);
            if (!n.LockX) n.X += dx;
            if (!n.LockY) n.Y += dy;
        }
        foreach (var e in _graph.Edges)
            for (int i = 0; i < e.Points.Count; i++)
                if (Cells.TryGetValue(e.Points[i], out var cell))
                {
                    var (dx, dy) = RandomOffset(rng, cell, e.Points[i].X, e.Points[i].Y);
                    e.Points[i] = (e.Points[i].X + dx, e.Points[i].Y + dy);
                }
    }

    /// <summary><c>0x141f21dd0</c>: a triangle of the cell fan around (cx, cy) by area (<c>0x141f21c00</c>), then a random
    /// point in it, pulled in to 80 %.</summary>
    (int X, int Y) RandomOffset(LevelRandom rng, List<VEdge> cell, int cx, int cy)
    {
        if (cell.Count == 0) return (0, 0);
        var weights = new uint[cell.Count];
        int col = (int)((long)cx * _graph.Width / (uint)_graph.CellWidth);
        int row = (int)((long)cy * _graph.Height / (uint)_graph.CellHeight);
        for (int i = 0; i < cell.Count; i++)
        {
            var (ax, ay, bx, by) = (cell[i].A.X, cell[i].A.Y, cell[i].B.X, cell[i].B.Y);
            int area = unchecked((by - cy) * (ax - cx) - (ay - cy) * (bx - cx));
            weights[i] = (uint)(Math.Abs(area) / 2);
            bool? whole = null;
            if (col == 0) { if (ax == 0) whole = bx == 0; }
            else if (row == 0) { if (ay == 0) whole = by == 0; }
            else if (col == _graph.Width - 1) { if (ax == _graph.CellWidth - 1) whole = bx == ax; }
            else if (row == _graph.Height - 1 && ay == _graph.CellHeight - 1) whole = by == ay;
            if (whole == true)
            {
                Array.Clear(weights);
                weights[i] = 1;
                break;
            }
        }
        uint total = 0;
        foreach (var x in weights) total += x;
        uint r = rng.Next(total);
        int k = 0;
        if (weights[0] <= r)
            do r -= weights[k++]; while (weights[k] <= r);
        var t = cell[k];
        int a = (int)rng.Next(0x80u), b = (int)rng.Next(0x80u);
        if (a + b > 0x80)
        {
            a = 0x80 - a;
            b = 0x80 - b;
        }
        return (((t.B.X - cx) * b + (t.A.X - cx) * a) / 0xa0, ((t.B.Y - cy) * b + (t.A.Y - cy) * a) / 0xa0);
    }
}
