namespace GameServer.Data.Terrain.Generation;

/// <summary><c>0x142114f00</c> (after the edge routing, no draws): virtual edges that leave a room bend into an arc
/// toward the room's side (<c>0x14212a340</c> at the first node, <c>0x14212ac40</c> at the second; up to five rounds
/// while one bends), then corners are rounded (<c>0x14210aeb0</c>, up to five rounds while one changes).</summary>
sealed class EdgeShaping(TileGraph graph, Func<GraphEdge, EdgeType> type)
{
    bool _changed;
    readonly HashSet<GraphEdge> _edgesDone = [];
    readonly HashSet<GraphNode> _nodesDone = [];

    public void Run()
    {
        var starts = graph.Edges.Where(e => type(e).Virtual && e.StartOffset != (0, 0)).ToList();
        var ends = graph.Edges.Where(e => type(e).Virtual && e.EndOffset != (0, 0)).ToList();
        for (int round = 0; ;)
        {
            _changed = false;
            foreach (var e in starts) Arc(e, 0);
            foreach (var e in ends) Arc(e, 1);
            if (!_changed || ++round >= 5) break;
        }
        RoundAll();
    }

    /// <summary><c>0x14210aeb0</c> up to five rounds, while a round changes something.</summary>
    public void RoundAll()
    {
        for (int round = 0; round < 5; round++)
            if (!RoundCorners()) break;
    }

    /// <summary><c>0x14212a340</c> (end 0) / <c>0x14212ac40</c> (end 1): where the edge leaves its node across the
    /// offset's axis at a slant of more than 30°, points on a circle tangent to the offset (one per 45°) lead into
    /// it.</summary>
    void Arc(GraphEdge e, int end)
    {
        var node = end == 0 ? e.A : e.B;
        var p = end == 0 ? (e.Points.Count == 0 ? (e.B.X, e.B.Y) : e.Points[0]) : (e.Points.Count == 0 ? (e.A.X, e.A.Y) : e.Points[^1]);
        var (ox, oy) = end == 0 ? e.StartOffset : e.EndOffset;
        if (ox == 0 ? node.X == p.X : node.Y == p.Y) return;
        int ax = node.X, ay = node.Y, px = p.X, py = p.Y, dx = px - ax;
        int k = ox != 0 ? ox : -oy;
        int r = k * ((ay - (ay + py) / 2) * (ay - py) - (ax - (ax + px) / 2) * dx) * 3 / (dx * -oy - ox * (ay - py)) / 2;
        int cx = ox == 0 ? ax + r : ax, cy = ox == 0 ? ay : ay + r;
        if ((ax == cx && ay == cy) || (px == cx && py == cy)) return;
        int dy = py - ay;
        ulong lo = (ulong)(uint)(oy * oy + ox * ox), ld = (uint)(dx * dx + dy * dy);
        long dot2 = (long)(ld - (uint)((dx - ox) * (dx - ox) + (dy - oy) * (dy - oy)) + lo);
        if (dot2 * dot2 >= (long)(ld * lo * 3)) return;
        int ang = GraphGeometry.Angle(ax - cx, ay - cy, px - cx, py - cy);
        int radius = Math.Abs(r);
        var arc = new List<(int X, int Y)>();
        void Add(int a)
        {
            var v = GraphGeometry.Polar(radius, a & 0xffff);
            arc.Add((cx + v.X, cy + v.Y));
        }
        static int Neg(int a) => (ushort)-a;
        if (ox == 1)
        {
            if (py < ay)
            {
                if (cx <= px) ang = Neg(ang);
                for (uint a = (ushort)(ang + 0x4000) < 0x4001 ? 0x2000u : 0x12000u; (ushort)(ang + 0x4000) < a; a -= 0x2000) Add((int)a);
            }
            else
            {
                if (px < cx) ang = Neg(ang);
                ang = (ushort)(ang + 0xc000);
                uint limit = ang > 0xbfff ? (uint)ang : (uint)ang + 0x10000;
                for (uint a = 0xe000; a < limit; a += 0x2000) Add((int)a);
            }
        }
        else if (ox == -1)
        {
            if (py < ay)
            {
                if (cx < px) ang = Neg(ang);
                ang = (ushort)(ang + 0x4000);
                uint limit = ang > 0x3fff ? (uint)ang : (uint)ang + 0x10000;
                for (uint a = 0x6000; a < limit; a += 0x2000) Add((int)a);
            }
            else
            {
                if (px <= cx) ang = Neg(ang);
                for (uint a = (ushort)(ang + 0xc000) < 0xc001 ? 0xa000u : 0x1a000u; (ushort)(ang + 0xc000) < a; a -= 0x2000) Add((int)a);
            }
        }
        else if (oy == 1)
        {
            if (px < ax)
            {
                if (py < cy) ang = Neg(ang);
                for (uint a = 0x2000; a < (uint)ang; a += 0x2000) Add((int)a);
            }
            else
            {
                if (cy <= py) ang = Neg(ang);
                for (uint a = (ushort)(ang + 0x8000) < 0x8001 ? 0x6000u : 0x16000u; (ushort)(ang + 0x8000) < a; a -= 0x2000) Add((int)a);
            }
        }
        else
        {
            if (px < ax)
            {
                if (py <= cy) ang = Neg(ang);
                for (uint a = ang == 0 ? 0xffffe000u : 0xe000u; (uint)ang < a; a -= 0x2000) Add((int)a);
            }
            else
            {
                if (cy < py) ang = Neg(ang);
                ang = (ushort)(ang + 0x8000);
                uint limit = ang > 0x7fff ? (uint)ang : (uint)ang + 0x10000;
                for (uint a = 0xa000; a < limit; a += 0x2000) Add((int)a);
            }
        }
        if (end == 0) e.Points.InsertRange(0, arc);
        else
        {
            arc.Reverse();
            e.Points.AddRange(arc);
        }
        _changed = true;
    }

    /// <summary><c>0x14210aeb0</c>: from every node with one edge, then every node with more, each edge not rounded yet
    /// is rounded from that end (<see cref="Round"/>). True when something changed.</summary>
    bool RoundCorners()
    {
        _changed = false;
        _edgesDone.Clear();
        _nodesDone.Clear();
        var single = graph.Nodes.Where(n => n.Degree == 1).ToList();
        var multi = graph.Nodes.Where(n => n.Degree > 1).ToList();
        foreach (var n in single)
        {
            var e = n.Slots.First(s => s is not null)!;
            if (!_edgesDone.Contains(e)) Round(e, e.A != n ? 1 : 0);
        }
        foreach (var n in multi)
            foreach (var e in n.Slots)
                if (e is not null && !_edgesDone.Contains(e)) Round(e, e.A != n ? 1 : 0);
        return _changed;
    }

    // An edge's position i: 0 = its first node, 1..n = internal points, n + 1 (or beyond, or below 0) = its second node.
    static (int X, int Y) Get(GraphEdge e, int i) =>
        i == 0 ? (e.A.X, e.A.Y) : i < 0 || i > e.Points.Count ? (e.B.X, e.B.Y) : e.Points[i - 1];

    static void Set(GraphEdge e, int i, (int X, int Y) p)
    {
        if (i == 0) (e.A.X, e.A.Y) = p;
        else if (i == e.Points.Count + 1) (e.B.X, e.B.Y) = p;
        else e.Points[i - 1] = p;
    }

    /// <summary><c>0x14210a240</c>: walks the edge from one end (and on through nodes with two edges) and rounds each
    /// corner sharper than about 140°: the corner is replaced by two points (max width + 10)-ish away along its two
    /// segments, or moved to the middle when its segments are too short; repeated points are removed
    /// (<c>0x142109fb0</c>).</summary>
    void Round(GraphEdge e, int back)
    {
        int step = back == 0 ? 1 : -1;
        int pos = back == 0 ? 1 : e.Points.Count;
        int Start() => back == 0 ? 0 : e.Points.Count + 1;
        int End() => back == 0 ? e.Points.Count + 2 : -1;
        for (int guard = 0; ; guard++)
        {
            if (guard > 100000) throw new InvalidOperationException("Error rounding corners: no end");
            if (_edgesDone.Contains(e)) return;
            if (pos == End())
            {
                var node = back == 0 ? e.B : e.A;
                _nodesDone.Add(node);
                _edgesDone.Add(e);
                var next = node.Slots.FirstOrDefault(s => s is not null && s != e);
                if (next is null) return;
                e = next;
                back = e.A != node ? 1 : 0;
                step = back == 0 ? 1 : -1;
                pos = back == 0 ? 1 : e.Points.Count;
                if ((back == 0 ? e.A : e.B) != node) throw new InvalidOperationException("Error rounding corner: mismatching edge ends");
            }
            int prev = pos - step;
            var p0 = Get(e, prev);
            var pc = Get(e, pos);
            GraphEdge? other = null;
            int otherBack = 0;
            int nextIdx = pos + step;
            (int X, int Y) p2;
            if (nextIdx == End())
            {
                var far = back == 0 ? e.B : e.A;
                if (_nodesDone.Contains(far))
                {
                    pos += step;
                    continue;
                }
                if (far.Degree != 2)
                {
                    _edgesDone.Add(e);
                    return;
                }
                other = far.Slots.FirstOrDefault(s => s is not null && s != e);
                if (other is null) return;
                otherBack = other.A != far ? 1 : 0;
                if (otherBack == 1)
                    p2 = other.Points.Count == 0 ? (other.A.X, other.A.Y) : other.Points[^1];
                else
                    p2 = other.Points.Count >= 1 ? other.Points[0] : (other.B.X, other.B.Y);
            }
            else p2 = Get(e, nextIdx);

            int x0 = p0.X, y0 = p0.Y, xc = pc.X, yc = pc.Y, x2 = p2.X, y2 = p2.Y;
            if (p0 == pc || pc == p2 || p2 == p0)
            {
                int after = nextIdx;
                if (!RemovePoint(e, back, other, otherBack, pos))
                    Set(e, pos, ((x0 + x2) / 2, (y0 + y2) / 2));
                else
                    after = back - 1 + pos;
                pos = after == Start() ? after + step : after;
                continue;
            }
            uint d02 = (uint)((x2 - x0) * (x2 - x0) + (y2 - y0) * (y2 - y0));
            int d0c = (xc - x0) * (xc - x0) + (yc - y0) * (yc - y0);
            int dc2 = (x2 - xc) * (x2 - xc) + (y2 - yc) * (y2 - yc);
            int reach = e.MaxWidth + 10;
            int a = (int)FortuneVoronoi.IntSqrt((uint)d0c), b = (int)FortuneVoronoi.IntSqrt((uint)dc2);
            uint s1 = (uint)((b + a) * (b + a)), s2 = (uint)((a - b) * (a - b));
            uint num = s1 < d02 ? 1 : s1 - d02;
            uint den = d02 <= s2 ? 1 : d02 - s2;
            uint q = unchecked((uint)((int)num * reach * reach)) / den;
            int len = (int)FortuneVoronoi.IntSqrt(q);
            var pb = (xc + (x2 - xc) * len / b, yc + (y2 - yc) * len / b);
            var pa = (xc + (x0 - xc) * len / a, yc + (y0 - yc) * len / a);
            bool Free((int X, int Y) t) => t != p0 && t != pc && t != p2;
            if (q > 3 && b * a * -0x4c < unchecked((int)(((uint)dc2 - d02 + (uint)d0c) * 0x32u)))
            {
                if (b - 6 <= len)
                {
                    _changed = true;
                    if (len < a - 6)
                    {
                        Set(e, pos, pa);
                        if (pos == Start()) throw new InvalidOperationException("Error rounding corners: bad round_pt or pos");
                        if (Free(pa))
                        {
                            pos = prev;
                            if (prev == Start()) pos = prev + step;
                            continue;
                        }
                        pos += step;
                        continue;
                    }
                    if (!RemovePoint(e, back, other, otherBack, pos))
                    {
                        Set(e, pos, ((x0 + x2) / 2, (y0 + y2) / 2));
                        pos = nextIdx;
                    }
                    else pos = pos - 1 + back;
                    if (pos == Start()) pos += step;
                    continue;
                }
                if (Free(pb))
                {
                    if (a - 6 <= len)
                    {
                        Set(e, pos, pb);
                        if (pos == Start()) throw new InvalidOperationException("Error rounding corners: pos at start");
                        _changed = true;
                        pos = prev;
                        if (prev == Start()) pos = prev + step;
                        continue;
                    }
                    if (Free(pa))
                    {
                        int ddx = pb.Item1 - pa.Item1, ddy = pb.Item2 - pa.Item2;
                        if ((uint)(ddy * ddy + ddx * ddx) > 0x100)
                        {
                            Set(e, pos, pa);
                            Insert(e, (step + 1) / 2 + pos, pb);
                            if (pos == Start()) throw new InvalidOperationException("Error rounding corners: pos at start");
                            pos -= (back + 1) * step;
                            _changed = true;
                            if (pos == Start()) pos += step;
                            continue;
                        }
                    }
                }
            }
            pos += step;
        }
    }

    /// <summary><c>0x142109fb0</c>: removes the corner at pos; when the next point is the far node (joining
    /// <paramref name="other"/>), the node takes the nearest internal point instead (of this edge, else of the other).
    /// False when there is nothing to remove.</summary>
    static bool RemovePoint(GraphEdge e, int back, GraphEdge? other, int otherBack, int pos)
    {
        if (other is null)
        {
            if (pos < 1 || pos > e.Points.Count) throw new InvalidOperationException("Error rounding corners: position out of bounds");
            e.Points.RemoveAt(pos - 1);
            return true;
        }
        var far = back == 0 ? e.B : e.A;
        if (e.Points.Count == 0)
        {
            if (other.Points.Count == 0) return false;
            if ((otherBack == 0 ? other.A : other.B) != far) throw new InvalidOperationException("Error rounding corners: 'edge' and 'other edge' do not connect");
            int k = otherBack != 0 ? other.Points.Count - 1 : 0;
            (far.X, far.Y) = other.Points[k];
            other.Points.RemoveAt(k);
            return true;
        }
        if ((otherBack == 0 ? other.A : other.B) != far) throw new InvalidOperationException("Error rounding corners: 'edge' and 'other edge' do not connect");
        int j = back != 0 ? 0 : e.Points.Count - 1;
        (far.X, far.Y) = e.Points[j];
        e.Points.RemoveAt(back == 1 ? 0 : e.Points.Count - 1);
        return true;
    }

    /// <summary><c>0x1421275a0</c>: inserts a position at index i (0: before the first node, which takes it; n + 2: after
    /// the second node, likewise; else among the internal points).</summary>
    static void Insert(GraphEdge e, int i, (int X, int Y) p)
    {
        if (i == 0)
        {
            e.Points.Insert(0, (e.A.X, e.A.Y));
            (e.A.X, e.A.Y) = p;
        }
        else if (i == e.Points.Count + 2)
        {
            e.Points.Add((e.B.X, e.B.Y));
            (e.B.X, e.B.Y) = p;
        }
        else e.Points.Insert(i - 1, p);
    }
}

public static partial class GraphGeometry
{
    // 0x142fe0a10: a sine table of 256 steps (127 = 1).
    static readonly sbyte[] Sine =
    [
        0, 3, 6, 9, 12, 16, 19, 22, 25, 28, 31, 34, 37, 40, 43, 46, 49, 51, 54, 57, 60, 63, 65, 68, 71, 73, 76, 78, 81, 83,
        85, 88, 90, 92, 94, 96, 98, 100, 102, 104, 106, 107, 109, 111, 112, 113, 115, 116, 117, 118, 120, 121, 122, 122,
        123, 124, 125, 125, 126, 126, 126, 127, 127, 127, 127, 127, 127, 127, 126, 126, 126, 125, 125, 124, 123, 122, 122,
        121, 120, 118, 117, 116, 115, 113, 112, 111, 109, 107, 106, 104, 102, 100, 98, 96, 94, 92, 90, 88, 85, 83, 81, 78,
        76, 73, 71, 68, 65, 63, 60, 57, 54, 51, 49, 46, 43, 40, 37, 34, 31, 28, 25, 22, 19, 16, 12, 9, 6, 3, 0, -3, -6, -9,
        -12, -16, -19, -22, -25, -28, -31, -34, -37, -40, -43, -46, -49, -51, -54, -57, -60, -63, -65, -68, -71, -73, -76,
        -78, -81, -83, -85, -88, -90, -92, -94, -96, -98, -100, -102, -104, -106, -107, -109, -111, -112, -113, -115, -116,
        -117, -118, -120, -121, -122, -122, -123, -124, -125, -125, -126, -126, -126, -127, -127, -127, -127, -127, -127,
        -127, -126, -126, -126, -125, -125, -124, -123, -122, -122, -121, -120, -118, -117, -116, -115, -113, -112, -111,
        -109, -107, -106, -104, -102, -100, -98, -96, -94, -92, -90, -88, -85, -83, -81, -78, -76, -73, -71, -68, -65, -63,
        -60, -57, -54, -51, -49, -46, -43, -40, -37, -34, -31, -28, -25, -22, -19, -16, -12, -9, -6, -3,
    ];

    // The client's interpolation weighs the two table entries the other way round.
    static int SineOf(int angle)
    {
        int hi = (angle >> 8) & 0xff, lo = angle & 0xff;
        int v = (Sine[(hi + 1) & 0xff] * (0xff - lo) + Sine[hi] * lo) / 0xff;
        return Math.Clamp(v, -0x7f, 0x7f);
    }

    /// <summary><c>0x14299d820</c>: a vector of length r at an angle (0x10000 = a full turn).</summary>
    public static (int X, int Y) Polar(int r, int angle)
    {
        int s = SineOf(angle & 0xffff), c = SineOf((0x4000 - angle) & 0xffff);
        return (c * r / 0xff, s * r / 0xff);
    }
}
