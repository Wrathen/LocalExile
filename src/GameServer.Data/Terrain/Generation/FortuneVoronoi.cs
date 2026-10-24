namespace GameServer.Data.Terrain.Generation;

/// <summary>The client's Voronoi diagram (<c>0x141f27200</c> and helpers): Steven Fortune's sweep2 rewritten in 32-bit
/// integers (no normalised lines, integer square roots, integer hash buckets). Ported operation for operation so the
/// output, its order included, is the client's. Output: the clipped segments (newest first, <c>0x141f26d70</c>) and the
/// vertices that end one or three segments.</summary>
public sealed class FortuneVoronoi
{
    sealed class Site
    {
        public int X, Y, Number;
    }

    sealed class Edge
    {
        public int C, B, A; // A·x − B·y = C with A = dx, B = −dy (+8, +4, +0)
        public Site?[] Ep = new Site?[2];
        public Site[] Reg = new Site[2];
        public int Number;
    }

    sealed class HalfEdge
    {
        public HalfEdge? Left, Right;
        public Edge? Edge;
        public bool Deleted;
        public int RefCount;
        public int Pm;
        public Site? Vertex;
        public int YStar;
        public HalfEdge? PqNext;
    }

    /// <summary>One clipped segment: its ends and the numbers of the two sites it separates.</summary>
    public readonly record struct Segment(int X1, int Y1, int X2, int Y2, int Site1, int Site2);

    readonly List<Site> _sites = [];
    int _siteIndex;
    int _xmin, _xmax, _ymin, _ymax, _deltaX, _deltaY, _sqrt;
    readonly int _clipXMin, _clipYMin, _clipXMax, _clipYMax;
    HalfEdge?[] _elHash = [];
    int _elHashSize;
    HalfEdge _elLeftEnd = null!, _elRightEnd = null!;
    HalfEdge[] _pqHash = [];
    int _pqHashSize, _pqCount, _pqMin;
    Site? _bottomSite;
    int _edgeCount;
    readonly List<Site> _vertices = [];
    readonly List<(int A, int B, int C, int Count)> _adjacency = [];

    public List<Segment> Segments { get; } = [];

    /// <summary>Vertices with one or three segments (border ends and proper Voronoi vertices), in vertex order.</summary>
    public List<(int X, int Y)> EndVertices { get; } = [];

    /// <summary>Builds the diagram of the points, clipped to [0, width] × [0, height] (cells).</summary>
    public FortuneVoronoi(IReadOnlyList<(int X, int Y)> points, int width, int height)
    {
        _clipXMin = 0;
        _clipYMin = 0;
        _clipXMax = width;
        _clipYMax = height;
        if (points.Count == 0) return;
        for (int i = 0; i < points.Count; i++) _sites.Add(new Site { X = points[i].X, Y = points[i].Y, Number = i });
        _xmin = _xmax = points[0].X;
        _ymin = _ymax = points[0].Y;
        foreach (var s in _sites)
        {
            if (s.X < _xmin) _xmin = s.X; else if (s.X > _xmax) _xmax = s.X;
            if (s.Y < _ymin) _ymin = s.Y; else if (s.Y > _ymax) _ymax = s.Y;
        }
        // 0x141f52490 with 0x141f4f830: by y, then x (std::sort; sites are distinct points, so the order is unique).
        _sites.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        _sqrt = (int)IntSqrt((uint)(_sites.Count + 4));
        _deltaY = _ymax - _ymin;
        _deltaX = _xmax - _xmin;
        Sweep();
    }

    /// <summary>Debug: vertices with their neighbour counts.</summary>
    public string Describe() => string.Join(" ", _vertices.Select((v, i) => $"#{i}({v.X},{v.Y})x{(i < _adjacency.Count ? _adjacency[i].Count : 0)}"));

    Site? NextSite() => _siteIndex < _sites.Count ? _sites[_siteIndex++] : null;

    void Sweep()
    {
        _pqHashSize = _sqrt * 4;
        _pqHash = new HalfEdge[_pqHashSize];
        for (int i = 0; i < _pqHashSize; i++) _pqHash[i] = new HalfEdge();
        _bottomSite = NextSite();
        _elHashSize = _sqrt * 2;
        _elHash = new HalfEdge?[_elHashSize];
        _elLeftEnd = new HalfEdge();
        _elRightEnd = new HalfEdge();
        _elLeftEnd.Right = _elRightEnd;
        _elRightEnd.Left = _elLeftEnd;
        _elHash[0] = _elLeftEnd;
        _elHash[_elHashSize - 1] = _elRightEnd;

        var newSite = NextSite();
        int starY = 0, starX = 0;
        while (true)
        {
            if (_pqCount != 0)
            {
                while (_pqHash[_pqMin].PqNext is null) _pqMin++;
                var min = _pqHash[_pqMin].PqNext!;
                starY = min.YStar;
                starX = min.Vertex!.X;
            }
            if (newSite is not null && (_pqCount == 0 || newSite.Y < starY || (newSite.Y == starY && newSite.X < starX)))
            {
                SiteEvent(newSite);
                newSite = NextSite();
            }
            else if (_pqCount != 0)
                CircleEvent();
            else
                break;
        }
        for (var lbnd = _elLeftEnd.Right!; lbnd != _elRightEnd; lbnd = lbnd.Right!) ClipLine(lbnd.Edge!);
        for (int i = 0; i < _adjacency.Count; i++)
            if (_adjacency[i].Count == 1 || (_adjacency[i].Count == 3 && i < _vertices.Count))
                EndVertices.Add((_vertices[i].X, _vertices[i].Y));
        if (_vertices.Count == 0 && _adjacency.Count != 0)
            throw new NotSupportedException("Voronoi without vertices (0x141f281d0) is not ported");
    }

    void SiteEvent(Site site)
    {
        var lbnd = LeftBoundary(site);
        var rbnd = lbnd.Right!;
        var bot = RightRegion(lbnd);
        var e = Bisect(bot, site);
        var bisector = new HalfEdge { Edge = e, Pm = 0 };
        Insert(lbnd, bisector);
        var p = Intersect(lbnd, bisector);
        if (p is not null)
        {
            PqDelete(lbnd);
            PqInsert(lbnd, p, IntSqrtDist(p, site));
        }
        lbnd = bisector;
        bisector = new HalfEdge { Edge = e, Pm = 1 };
        Insert(lbnd, bisector);
        p = Intersect(bisector, rbnd);
        if (p is not null) PqInsert(bisector, p, IntSqrtDist(p, site));
    }

    void CircleEvent()
    {
        var lbnd = _pqHash[_pqMin].PqNext!;
        _pqHash[_pqMin].PqNext = lbnd.PqNext;
        _pqCount--;
        var llbnd = lbnd.Left!;
        var rbnd = lbnd.Right!;
        var rrbnd = rbnd.Right!;
        var bot = LeftRegion(lbnd);
        var top = RightRegion(rbnd);
        var v = lbnd.Vertex!;
        _vertices.Add(v);
        v.Number = _vertices.Count - 1;
        Endpoint(lbnd.Edge!, lbnd.Pm, v);
        Endpoint(rbnd.Edge!, rbnd.Pm, v);
        lbnd.Left!.Right = lbnd.Right;
        lbnd.Right!.Left = lbnd.Left;
        lbnd.Deleted = true;
        PqDelete(rbnd);
        rbnd.Left!.Right = rbnd.Right;
        rbnd.Right!.Left = rbnd.Left;
        rbnd.Deleted = true;
        int pm = 0;
        var (s1, s2) = (bot, top);
        if (top.Y < bot.Y)
        {
            (s1, s2) = (top, bot);
            pm = 1;
        }
        var e = Bisect(s1, s2);
        var bisector = new HalfEdge { Edge = e, Pm = pm };
        Insert(llbnd, bisector);
        Endpoint(e, 1 - pm, v);
        var p = Intersect(llbnd, bisector);
        if (p is not null)
        {
            PqDelete(llbnd);
            PqInsert(llbnd, p, IntSqrtDist(p, s1));
        }
        p = Intersect(bisector, rrbnd);
        if (p is not null) PqInsert(bisector, p, IntSqrtDist(p, s1));
    }

    static int IntSqrtDist(Site p, Site s) =>
        (int)IntSqrt(unchecked((uint)((p.X - s.X) * (p.X - s.X) + (p.Y - s.Y) * (p.Y - s.Y))));

    Site LeftRegion(HalfEdge he) => he.Edge is null ? _bottomSite! : he.Pm == 0 ? he.Edge.Reg[0] : he.Edge.Reg[1];

    Site RightRegion(HalfEdge he) => he.Edge is null ? _bottomSite! : he.Pm == 0 ? he.Edge.Reg[1] : he.Edge.Reg[0];

    Edge Bisect(Site s1, Site s2)
    {
        int dx = s2.X - s1.X, dy = s2.Y - s1.Y;
        var e = new Edge { A = dx, B = -dy, C = unchecked((dx * dx + dy * dy) / 2 + s1.X * dx + s1.Y * dy), Number = _edgeCount++ };
        e.Reg[0] = s1;
        e.Reg[1] = s2;
        return e;
    }

    static void Insert(HalfEdge lb, HalfEdge he)
    {
        he.Left = lb;
        he.Right = lb.Right;
        lb.Right!.Left = he;
        lb.Right = he;
    }

    /// <summary><c>0x141f26780</c>.</summary>
    Site? Intersect(HalfEdge el1, HalfEdge el2)
    {
        var e1 = el1.Edge;
        var e2 = el2.Edge;
        if (e1 is null || e2 is null) return null;
        if (e1.Reg[1] == e2.Reg[1]) return null;
        unchecked
        {
            int d = e1.B * e2.A - e1.A * e2.B;
            if (d == 0) return null;
            var r1 = e1.Reg[1];
            var r2 = e2.Reg[1];
            bool e1Below = r1.Y < r2.Y || (r1.Y == r2.Y && r1.X < r2.X);
            int sy = r1.Y + e1.Reg[0].Y;
            int sx = e1.Reg[0].X + r1.X;
            long t = (long)(sy - e2.Reg[0].Y - r2.Y) * e2.B - (long)(sx - e2.Reg[0].X - r2.X) * e2.A;
            int x = ((int)(e1.B * t / d) + sx) / 2;
            int a1 = e1.A;
            HalfEdge el;
            Edge e;
            if (e1Below) { el = el1; e = e1; } else { el = el2; e = e2; }
            bool rightOfSite = x >= e.Reg[1].X;
            if (rightOfSite ? el.Pm == 0 : el.Pm == 1) return null;
            return new Site { X = x, Y = ((int)(a1 * t / d) + sy) / 2 };
        }
    }

    /// <summary><c>0x141f268d0</c>: whether p is right of the half-edge.</summary>
    static bool RightOf(HalfEdge el, Site p)
    {
        var e = el.Edge!;
        var top = e.Reg[1];
        int px = p.X;
        if (top.X < px) { if (el.Pm == 0) return true; }
        else if (el.Pm == 1) return false;
        unchecked
        {
            int b = e.B, a = e.A, dxp = px - top.X;
            bool above;
            if (Math.Abs(b) < Math.Abs(a))
            {
                int dyp = p.Y - top.Y;
                bool negSlope = ((a ^ -b) < 0);
                bool caseFlag = px <= top.X ? negSlope : !negSlope;
                bool done = false;
                above = false;
                if (!caseFlag)
                {
                    int t = px * a - p.Y * b;
                    bool aboveC = a < 0 ? t < e.C : e.C < t;
                    bool fast = negSlope ? !aboveC : aboveC;
                    if (!fast) done = true;
                }
                else
                {
                    above = a < 0 ? a * dyp <= -(b * dxp) : -(b * dxp) <= a * dyp;
                    if (above) done = true;
                }
                if (!done)
                {
                    int lhs = (dyp * dyp - dxp * dxp) * b;
                    int dxs = top.X - e.Reg[0].X;
                    int rhs = ((a * dxp * 2) / dxs + a + (b * b) / a) * dxs * dyp;
                    above = a < 0 ? rhs < lhs : lhs < rhs;
                    if (negSlope) above = !above;
                }
            }
            else
            {
                int yl = (px * a - e.C) / b;
                int t1 = p.Y - yl, t3 = yl - top.Y;
                above = t3 * t3 + dxp * dxp < t1 * t1;
            }
            return el.Pm != 0 ? !above : above;
        }
    }

    /// <summary><c>0x141f26580</c>: the half-edge left of p.</summary>
    HalfEdge LeftBoundary(Site p)
    {
        int bucket = Math.Clamp(unchecked((p.X - _xmin) / _deltaX * _elHashSize), 0, _elHashSize - 1);
        var he = HashGet(bucket);
        for (int i = 1; he is null; i++)
        {
            he = HashGet(bucket - i);
            if (he is null) he = HashGet(bucket + i);
        }
        if (he == _elLeftEnd || (he != _elRightEnd && RightOf(he, p)))
        {
            do he = he.Right!; while (he != _elRightEnd && RightOf(he, p));
            he = he.Left!;
        }
        else
        {
            do he = he.Left!; while (he != _elLeftEnd && !RightOf(he, p));
        }
        if (bucket > 0 && bucket < _elHashSize - 1)
        {
            if (_elHash[bucket] is { } old) old.RefCount--;
            _elHash[bucket] = he;
            he.RefCount++;
        }
        return he;
    }

    HalfEdge? HashGet(int b)
    {
        if (b < 0 || b >= _elHashSize) return null;
        var he = _elHash[b];
        if (he is null || !he.Deleted) return he;
        _elHash[b] = null;
        he.RefCount--;
        return null;
    }

    int PqBucket(int ystar) => Math.Clamp(unchecked((ystar - _ymin) / _deltaY * _pqHashSize), 0, _pqHashSize - 1);

    void PqInsert(HalfEdge he, Site v, int offset)
    {
        he.Vertex = v;
        he.YStar = v.Y + offset;
        int b = PqBucket(he.YStar);
        if (b < _pqMin) _pqMin = b;
        var last = _pqHash[b];
        while (last.PqNext is { } next && (he.YStar > next.YStar || (he.YStar == next.YStar && v.X > next.Vertex!.X))) last = next;
        he.PqNext = last.PqNext;
        last.PqNext = he;
        _pqCount++;
    }

    void PqDelete(HalfEdge he)
    {
        if (he.Vertex is null) return;
        int b = PqBucket(he.YStar);
        if (b < _pqMin) _pqMin = b;
        var last = _pqHash[b];
        while (last.PqNext != he) last = last.PqNext!;
        last.PqNext = he.PqNext;
        _pqCount--;
        he.Vertex = null;
    }

    void Endpoint(Edge e, int lr, Site s)
    {
        e.Ep[lr] = s;
        if (e.Ep[1 - lr] is null) return;
        ClipLine(e);
    }

    /// <summary><c>0x141f26d70</c>: clips an edge to the bounds and records it.</summary>
    void ClipLine(Edge e)
    {
        int ddx = e.Reg[1].X - e.Reg[0].X, ddy = e.Reg[1].Y - e.Reg[0].Y;
        if (unchecked(ddx * ddx + ddy * ddy) < 2) return;
        int xmin = _clipXMin, ymin = _clipYMin, xmax = _clipXMax, ymax = _clipYMax;
        int a = e.A, b = e.B, c = e.C;
        bool clipped1 = false, clipped2 = false;
        int x1, y1, x2, y2;
        Site? s1, s2;
        unchecked
        {
            if (Math.Abs(b) < Math.Abs(a) && (a < 0) == (-b < 0))
            {
                s1 = e.Ep[1];
                s2 = e.Ep[0];
                goto steep;
            }
            s1 = e.Ep[0];
            s2 = e.Ep[1];
            if (Math.Abs(b) < Math.Abs(a)) goto steep;

            // Mostly horizontal: x from y.
            x1 = s1 is null || s1.X <= xmin ? xmin : s1.X;
            if (s1 is null || s1.X <= xmin) clipped1 = true;
            if (xmax < x1) { clipped1 = true; x1 = xmax; }
            y1 = (a * x1 - c) / b;
            x2 = s2 is null || xmax <= s2.X ? xmax : s2.X;
            if (s2 is null || xmax <= s2.X) clipped2 = true;
            if (x2 < xmin) { clipped2 = true; x2 = xmin; }
            y2 = (a * x2 - c) / b;
            if ((ymax < y2 && ymax < y1) || (y2 < ymin && y1 < ymin)) return;
            if (a != 0)
            {
                if (ymax < y1) { clipped1 = true; x1 = (b * ymax + c) / a; y1 = ymax; }
                if (y1 < ymin) { clipped1 = true; x1 = (b * ymin + c) / a; y1 = ymin; }
                if (ymax < y2) { clipped2 = true; x2 = (b * ymax + c) / a; y2 = ymax; }
                if (y2 < ymin) { clipped2 = true; x2 = (b * ymin + c) / a; y2 = ymin; }
            }
            goto emit;

        steep:
            // Mostly vertical: y from x.
            y1 = s1 is null || s1.Y <= ymin ? ymin : s1.Y;
            if (s1 is null || s1.Y <= ymin) clipped1 = true;
            if (ymax < y1) { clipped1 = true; y1 = ymax; }
            x1 = (b * y1 + c) / a;
            y2 = s2 is null || ymax <= s2.Y ? ymax : s2.Y;
            if (s2 is null || ymax <= s2.Y) clipped2 = true;
            if (y2 < ymin) { clipped2 = true; y2 = ymin; }
            x2 = (b * y2 + c) / a;
            if ((x2 < xmin && x1 < xmin) || (xmax < x2 && xmax < x1)) return;
            if (b != 0)
            {
                if (xmax < x1) { clipped1 = true; y1 = (a * xmax - c) / b; x1 = xmax; }
                if (x1 < xmin) { clipped1 = true; y1 = (a * xmin - c) / b; x1 = xmin; }
                if (xmax < x2) { clipped2 = true; y2 = (a * xmax - c) / b; x2 = xmax; }
                if (x2 < xmin) { clipped2 = true; y2 = (a * xmin - c) / b; x2 = xmin; }
            }
        }
    emit:
        // A segment collapsed onto a border line is dropped.
        if (x1 == x2 && (x2 == xmin || x2 == xmax)) return;
        if (y1 == y2 && (y2 == ymin || y2 == ymax)) return;
        Segments.Insert(0, new Segment(x1, y1, x2, y2, e.Reg[0].Number, e.Reg[1].Number));
        if (clipped1) s1 = NewVertex(x1, y1);
        if (clipped2) s2 = NewVertex(x2, y2);
        Adjacent(s1!.Number, s2!.Number);
    }

    Site NewVertex(int x, int y)
    {
        var v = new Site { X = x, Y = y, Number = _vertices.Count };
        _vertices.Add(v);
        return v;
    }

    /// <summary><c>0x141f28010</c>: records that two vertices are joined (up to three neighbours each).</summary>
    void Adjacent(int a, int b)
    {
        while (_adjacency.Count <= Math.Max(a, b)) _adjacency.Add((-1, -1, -1, 0));
        _adjacency[a] = Add(_adjacency[a], b);
        _adjacency[b] = Add(_adjacency[b], a);
    }

    static (int, int, int, int) Add((int A, int B, int C, int Count) v, int n) =>
        v.A == -1 ? (n, v.B, v.C, 1) : v.B == -1 ? (v.A, n, v.C, 2) : v.C == -1 ? (v.A, v.B, n, 3) : v;

    /// <summary><c>0x141937840</c>: integer square root, rounded down.</summary>
    public static uint IntSqrt(uint n) => (uint)Math.Sqrt(n) is var r && (ulong)(r + 1) * (r + 1) <= n ? r + 1 : (ulong)r * r > n ? r - 1 : r;
}
