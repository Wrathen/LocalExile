namespace GameServer.Data.Terrain.Generation;

/// <summary>A node's room or tile as <c>0x1421203f0</c> places it (the client's 0x98-byte record): the room (.arm) or
/// tile (.tdt), its orientation and key, the position in graph tiles (+0x70), the node (+0x78), the side each of the
/// node's edge slots leaves by (+0x80), the alignment (+0x91) and whether it must stay reachable (+0x92).</summary>
sealed class NodePlacement
{
    public required string Path;
    public RoomDef? Room;
    public TileDef? Tile;
    public int Orientation;
    public required TileKey Key;
    public int X, Y;
    public required GraphNode Node;
    public required int[] Sides;
    public byte Align;
    public bool Accessible;

    public override string ToString() => $"{System.IO.Path.GetFileName(Path)} o{Orientation} at ({X}, {Y}) for {Node}";
}

/// <summary>The special-node stage of the tile graph layout (<c>0x142116600</c> after the Voronoi jitter): nodes are
/// sorted into lists (<c>0x14211f100</c>), tagged nodes and nodes on virtual edges get a room or a tile
/// (<c>0x1421225d0</c> → <c>0x1421203f0</c>), and the placed rooms are tilised (<c>0x142121610</c>).</summary>
/// <summary>A tilised room as <c>0x141fea780</c> records it (0x38 bytes on the layout context's +0x58 list): the
/// room, its footprint in map entries, the node's descriptions (+0x20) and its orientation code (+0x1b).</summary>
public sealed record PlacedRoom(RoomDef Room, int X, int Y, int Width, int Height, IReadOnlyList<string> Descriptions, int Orientation = 0);

sealed class SpecialNodes(TerrainCache files, LevelRandom rng, List<string> notes, TileGraph graph, TileGrid grid,
    int offsetX, int offsetY, TileStore store, RoomSetIndex rooms)
{
    // 0x14211f100's lists (the layout object's +0, +0x18, +0x30, +0x48).
    public readonly List<GraphNode> Virtual = [], Plain = [], Tagged = [], Lonely = [];
    /// <summary>Nodes of <see cref="Virtual"/> that found no room or tile (+0x60).</summary>
    public readonly List<GraphNode> Failed = [];
    public readonly List<NodePlacement> Placed = [];
    /// <summary>The rooms tilised, in order (the layout context's +0x58 list, <c>0x141fea780</c>), in map entries.</summary>
    public readonly List<PlacedRoom> Rooms = [];
    /// <summary>The map vertices that took a room's own inner vertex ground (<c>0x141ffbd70</c>); the seams leave tiles
    /// touching them alone.</summary>
    public readonly HashSet<(int X, int Y)> RoomVertices = [];

    /// <summary>The listeners <c>0x141fea780</c> calls after each slot tile (tile, orientation, view x, y).</summary>
    public Action<TileDef, int, int, int>? TilePlaced;
    /// <summary>Called as each relaxation starts (the trace's "relax" graph snapshot).</summary>
    public Action? BeforeRelax;

    const int GraphOrientation = 0;

    EdgeType Type(GraphEdge e) => files.Edge(e.Type) ?? throw new FileNotFoundException(e.Type);

    /// <summary><c>0x1421225d0</c>.</summary>
    public void Run()
    {
        foreach (var n in graph.Nodes) Classify(n);
        foreach (var n in Tagged)
            Placed.Add(Place(n) ?? throw new InvalidOperationException($"Could not tilise the special node at ({n.X}, {n.Y}) with type \"{n.Tag}\""));
        // 0x142121d80: rooms the room set marks as required. None of the ported room sets has any.
        foreach (var n in Virtual)
        {
            if (Place(n) is { } p) Placed.Add(p);
            else Failed.Add(n);
        }
        notes.Add("special nodes: " + string.Join("; ", Placed));
        Relax(Placed, all: true);
        foreach (var p in Placed) Tilise(p);
    }

    /// <summary><c>0x142122970</c>: every plain node (the list has grown since) not on the border with its one edge
    /// gets a room or tile (one that fails is fatal); a node with one edge within its edge's size of the border moves
    /// off the map past it, its edge's end offset pointing back in. Then the relaxation (nodes with one edge left out)
    /// and the tilising, as for the special nodes.</summary>
    public void RunPlain()
    {
        var placed = new List<NodePlacement>();
        foreach (var n in Plain.ToList())
        {
            if (!AtBorder(n))
            {
                placed.Add(Place(n) ?? throw new InvalidOperationException($"Failed to tilise the non-virtual node at ({n.X}, {n.Y})"));
                continue;
            }
            var e = n.Slots.First(s => s is not null)!;
            int tx = Math.Min(n.X / 23, graph.Width - 1), ty = Math.Min(n.Y / 23, graph.Height - 1);
            int size = Type(e).Size;
            bool atA = e.A == n;
            void SetOffset(int? x, int? y)
            {
                var o = atA ? e.StartOffset : e.EndOffset;
                o = (x ?? o.X, y ?? o.Y);
                if (atA) e.StartOffset = o; else e.EndOffset = o;
            }
            if (tx < size)
            {
                n.X = -1;
                SetOffset(1, null);
            }
            else if (ty < size)
            {
                n.Y = -1;
                SetOffset(null, 1);
            }
            if (tx < graph.Width - size)
            {
                if (graph.Height - size <= ty)
                {
                    n.Y = graph.Height * 23;
                    SetOffset(null, -1);
                }
            }
            else
            {
                n.X = graph.Width * 23;
                SetOffset(-1, null);
            }
            n.Placed = true;
        }
        notes.Add("plain nodes: " + string.Join("; ", placed));
        Relax(placed, all: false);
        foreach (var p in placed) Tilise(p);
    }

    /// <summary><c>0x142116600</c> after the edge tiling: every node without edges but with descriptions gets a room or
    /// tile (<c>0x1421203f0</c>) and is tilised (<c>0x142121610</c>) when one fits; no relaxation.</summary>
    public void RunLonely()
    {
        foreach (var n in Lonely)
            if (Place(n) is { } p) Tilise(p);
    }

    /// <summary><c>0x1421239d0</c> (with all nodes): every placed room becomes a rectangle grown by its edges' room
    /// (<c>0x142123000</c>); every node and then every internal point is pushed away from the rectangles
    /// (<c>0x142123310</c>), a node also from its own room's four border probes (<c>0x142124210</c>); the pushes are
    /// applied (<c>0x142124480</c>, <c>0x1421327c0</c>) and each room is centred again on its node where it fits.</summary>
    void Relax(List<NodePlacement> records, bool all)
    {
        BeforeRelax?.Invoke();
        var rects = new List<(int X0, int Y0, int X1, int Y1, GraphNode Node)>();
        foreach (var p in records)
        {
            var bySide = new[] { 4, 4, 4, 4 };
            for (int i = 0; i < 4; i++) if (p.Sides[i] < 4) bySide[p.Sides[i]] = i;
            int Grow(int side)
            {
                if (bySide[side] == 4) return 0;
                var t = Type(p.Node.Slots[bySide[side]]!);
                return (t.Virtual ? 2 : 1) * t.Size;
            }
            rects.Add(((p.X - Grow(3)) * 23, (p.Y - Grow(0)) * 23, (p.Key.Width + Grow(1) + p.X) * 23,
                (p.Key.Height + Grow(2) + p.Y) * 23, p.Node));
        }
        var pushes = new List<(int X, int Y)>();
        foreach (var n in graph.Nodes)
        {
            if (!all && n.Degree == 1) continue;
            int ax = 0, ay = 0;
            foreach (var r in rects)
            {
                if (r.Node == n)
                {
                    // Only the special nodes' relaxation (all) probes a node's own room.
                    if (!all) continue;
                    int cx = (r.X0 + r.X1) / 2, cy = (r.Y1 + r.Y0) / 2;
                    var f1 = Push(r, (0, cy));
                    var f2 = Push(r, (graph.CellWidth - 1, cy));
                    var f3 = Push(r, (cx, 0));
                    var f4 = Push(r, (cx, graph.CellHeight - 1));
                    ax -= f4.X + f1.X + f2.X + f3.X;
                    ay -= f4.Y + f1.Y + f2.Y + f3.Y;
                    continue;
                }
                var f = Push(r, (n.X, n.Y));
                ax += f.X;
                ay += f.Y;
            }
            pushes.Add((ax, ay));
        }
        foreach (var e in graph.Edges)
            foreach (var q in e.Points)
            {
                int ax = 0, ay = 0;
                foreach (var r in rects)
                {
                    var f = Push(r, q);
                    ax += f.X;
                    ay += f.Y;
                }
                pushes.Add((ax, ay));
            }
        int at = 0, w = graph.Width * 23, h = graph.Height * 23;
        foreach (var n in graph.Nodes)
        {
            if (!all && n.Degree == 1) continue;
            bool inside = n.X >= 0 && n.Y >= 0 && n.X < w && n.Y < h;
            var f = pushes[at++];
            if (!n.LockX) n.X += f.X;
            if (!n.LockY) n.Y += f.Y;
            if (inside)
            {
                n.X = Math.Min(Math.Max(n.X, 0), w - 1);
                n.Y = Math.Min(Math.Max(n.Y, 0), h - 1);
            }
            else
            {
                n.X = Math.Min(n.X < -1 ? -1 : n.X, w);
                n.Y = Math.Min(n.Y < -1 ? -1 : n.Y, h);
            }
        }
        foreach (var e in graph.Edges)
            for (int i = 0; i < e.Points.Count; i++)
            {
                var f = pushes[at++];
                e.Points[i] = (Math.Min(Math.Max(e.Points[i].X + f.X, 0), w - 1), Math.Min(Math.Max(e.Points[i].Y + f.Y, 0), h - 1));
            }
        foreach (var p in records)
        {
            int tx = Math.Min(p.Node.X / 23, graph.Width - 1), ty = Math.Min(p.Node.Y / 23, graph.Height - 1);
            var (x, y) = Centred(tx, ty, p.Key.Width, p.Key.Height);
            x = Math.Min(Math.Max(x, 0), graph.Width - p.Key.Width);
            y = Math.Min(Math.Max(y, 0), graph.Height - p.Key.Height);
            (x, y) = Snap(x, y, p.Align, GraphOrientation);
            if (x < graph.Width && x + p.Key.Width <= graph.Width && y < graph.Height && y + p.Key.Height <= graph.Height &&
                CellsFree(x + offsetX, y + offsetY, p.Key.Width, p.Key.Height))
                (p.X, p.Y) = (x, y);
            (p.X, p.Y) = KeepEdgesInside(p.X, p.Y, p.Align, p.Key);
        }
    }

    // The top-left corner of a w × h footprint centred on tile (tx, ty), odd sizes by the placement table (as
    // 0x1421203f0 and 0x1421239d0 centre a room).
    static (int X, int Y) Centred(int tx, int ty, int w, int h)
    {
        int t = Math.Min(GraphOrientation, 8) * 3;
        int[] odd = [1, 0, 1, 0];
        int y = ty - ((h >> 1) - ((h - 1) & 1) * odd[(Orientation.Table[t] == 0 ? 2 : 0) + Orientation.Table[t + 2]]);
        int x = ((w - 1) & 1) * odd[Orientation.Table[t + 1] + Orientation.Table[t] * 2] - (w >> 1) + tx;
        return (Math.Max(x, 0), Math.Max(y, 0));
    }

    /// <summary><c>0x142123310</c>: the push a room rectangle gives a point: inside, out through the side nearer the
    /// room's node (scaled by the rectangle's half size); outside, falling off over 138 positions from a side or a
    /// corner.</summary>
    static (int X, int Y) Push((int X0, int Y0, int X1, int Y1, GraphNode Node) r, (int X, int Y) p)
    {
        int px = p.X, py = p.Y, x0 = r.X0, y0 = r.Y0, x1 = r.X1, y1 = r.Y1;
        const int F = 0x8a;
        static (int, int) Corner(int vx, int vy, int t)
        {
            int len = vy * vy + vx * vx;
            return ((int)((long)(vx * t) / len), (int)((long)(vy * t) / len));
        }
        if (y0 < py)
        {
            if (py < y1)
            {
                if (x0 < px)
                {
                    if (px < x1)
                    {
                        int ny = r.Node.Y, nx = r.Node.X;
                        int dy = py - ny, hy = (y1 - y0) / 2, ry;
                        if (py < ny) ry = y0 - (int)((long)(int)((long)(dy * hy * F) / (y0 - ny)) / (hy + F)) - py;
                        else ry = (int)((long)(int)((long)(dy * hy * F) / (y1 - ny)) / (hy + F)) - py + y1;
                        int dx = px - nx, hx = (x1 - x0) / 2, rx;
                        if (px < nx) rx = x0 - (int)((long)(int)((long)(dx * hx * F) / (x0 - nx)) / (hx + F)) - px;
                        else
                        {
                            rx = (int)((long)(int)((long)(dx * hx * F) / (x1 - nx)) / (hx + F)) - px + x1;
                            if (px == nx && py == ny)
                            {
                                int[] unit = [-1, 1, 0, 0];
                                int t = Math.Min(GraphOrientation, 8) * 3;
                                dx = unit[Orientation.Table[t + 1] + Orientation.Table[t] * 2];
                                dy = unit[Orientation.Table[t + 2] + (Orientation.Table[t] == 0 ? 2 : 0)];
                            }
                        }
                        int dot = ry * dy + dx * rx, len = dy * dy + dx * dx;
                        return (dx * dot / len, dot * dy / len);
                    }
                    if (px < x1 + F)
                    {
                        int hx = (x1 - x0) / 2;
                        return ((x1 - px + F) * hx / (hx + F), 0);
                    }
                    return (0, 0);
                }
                if (x0 - F < px)
                {
                    int hx = (x0 - x1) / 2;
                    return ((px - x0 + F) * hx / (F - hx), 0);
                }
                return (0, 0);
            }
            if (px <= x0)
            {
                int d = Math.Abs(x0 + 1 - px), e = Math.Abs(y1 - 1 - py);
                if ((uint)(e * e + d * d) > 0x4a64) return (0, 0);
                int vy = py - (y1 - 1), vx = px - (x0 + 1), a = 0, b = 0;
                if (py < y1 + F) { int hy = (y1 - y0) / 2; a = (y1 - py + F) * hy / (hy + F); }
                if (x0 - F < px) { int hx = (x0 - x1) / 2; b = (px - x0 + F) * hx / (F - hx); }
                return Corner(vx, vy, vy * a + vx * b);
            }
            if (px < x1)
            {
                if (py < y1 + F) { int hy = (y1 - y0) / 2; return (0, (y1 - py + F) * hy / (hy + F)); }
                return (0, 0);
            }
            {
                int d = Math.Abs(x1 - px), e = Math.Abs(y1 - py);
                if ((uint)(e * e + d * d) > 0x4a64) return (0, 0);
                int vy = py - (y1 - 1), vx = px - (x1 - 1), a = 0, b = 0;
                if (py < y1 + F) { int hy = (y1 - y0) / 2; a = (y1 - py + F) * hy / (hy + F); }
                if (px < x1 + F) { int hx = (x1 - x0) / 2; b = (x1 - px + F) * hx / (hx + F); }
                return Corner(vx, vy, vy * a + vx * b);
            }
        }
        if (px <= x0)
        {
            int d = Math.Abs(x0 - px), e = Math.Abs(y0 - py);
            if ((uint)(e * e + d * d) > 0x4a64) return (0, 0);
            int vy = py - (y0 + 1), vx = px - (x0 + 1), a = 0, b = 0;
            if (y0 - F < py) { int hy = (y0 - y1) / 2; a = (py - y0 + F) * hy / (F - hy); }
            if (x0 - F < px) { int hx = (x0 - x1) / 2; b = (px - x0 + F) * hx / (F - hx); }
            return Corner(vx, vy, vy * a + vx * b);
        }
        if (px < x1)
        {
            if (y0 - F < py) { int hy = (y0 - y1) / 2; return (0, (py - y0 + F) * hy / (F - hy)); }
            return (0, 0);
        }
        {
            int d = Math.Abs(x1 - 1 - px), e = Math.Abs(y0 + 1 - py);
            if ((uint)(e * e + d * d) > 0x4a64) return (0, 0);
            int vy = py - (y0 + 1), vx = px - (x1 - 1), a = 0, b = 0;
            if (y0 - F < py) { int hy = (y0 - y1) / 2; a = (py - y0 + F) * hy / (F - hy); }
            if (px < x1 + F) { int hx = (x1 - x0) / 2; b = (x1 - px + F) * hx / (hx + F); }
            return Corner(vx, vy, vy * a + vx * b);
        }
    }

    /// <summary><c>0x14211e9b0</c>: a node of the virtual list that got no room or tile. With three or more edges, the
    /// edges lose their points within the largest max width of the node (<c>0x14211e860</c>), and each pair of
    /// neighbouring edges (in slot order, around) with a virtual one gets that edge's end point here: a width
    /// <c>Next(max − min + 1) + min</c> toward the other edge (<c>0x14211e100</c>), or between two virtual edges the
    /// corner point (<c>0x14211e1d0</c>). Then the node splits into one node per edge (<c>0x14211dbc0</c>).</summary>
    public void TileLessVirtual(GraphNode n)
    {
        int degree = n.Degree;
        if (degree < 2) throw new InvalidOperationException($"Unexpected number of edges on tile-less virtual node at ({n.X}, {n.Y})");
        int i0 = Array.FindIndex(n.Slots, s => s is not null);
        int i1 = Array.FindIndex(n.Slots, i0 + 1, s => s is not null);
        if (i1 < 0) return;
        if (degree == 2)
        {
            if (Type(n.Slots[i1]!).Virtual != Type(n.Slots[i0]!).Virtual)
                throw new InvalidOperationException($"Trying to handle inappropriate tile-less virtual node at ({n.X}, {n.Y})");
        }
        else
        {
            int maxWidth = n.Slots.Where(e => e is not null).Max(e => e!.MaxWidth);
            foreach (var e in n.Slots.Where(e => e is not null).ToList()) TrimNear(n, e!, maxWidth);
            int i2 = i1 + 1 < 4 ? Array.FindIndex(n.Slots, i1 + 1, s => s is not null) : -1;
            if (i2 < 0) return;
            int a = i0, b = i1, c = i2;
            for (int k = 0; k < degree; k++)
            {
                var prev = n.Slots[a]!;
                var cur = n.Slots[b]!;
                int prevEnd = prev.A != n ? 1 : 0, curEnd = cur.A != n ? 1 : 0;
                var pPrev = Next(n, prev);
                var pCur = Next(n, cur);
                if (!Type(prev).Virtual)
                {
                    if (Type(cur).Virtual)
                    {
                        int w = (int)rng.Next((uint)(cur.MaxWidth - cur.MinWidth + 1)) + cur.MinWidth;
                        SetVirtualEnd(cur, curEnd, Toward((n.X, n.Y), pPrev, w));
                    }
                }
                else
                {
                    int w = (int)rng.Next((uint)(prev.MaxWidth - prev.MinWidth + 1)) + prev.MinWidth;
                    if (!Type(cur).Virtual) SetVirtualEnd(prev, prevEnd, Toward((n.X, n.Y), pCur, w));
                    else
                    {
                        if (prev.Type != cur.Type) throw new InvalidOperationException($"Mismatched edge-types while handling tile-less virtual node at ({n.X}, {n.Y})");
                        var corner = Between(pPrev, (n.X, n.Y), pCur, Next(n, n.Slots[c]!), w);
                        SetVirtualEnd(prev, prevEnd, corner);
                        SetVirtualEnd(cur, curEnd, corner);
                    }
                }
                int d = c;
                do d = (d + 1) & 3; while (n.Slots[d] is null);
                a = b;
                b = c;
                c = d;
            }
        }
        SplitNode(n);
    }

    // 0x14211e860: an edge's internal points within r of the node go, from the node's end.
    static void TrimNear(GraphNode n, GraphEdge e, int r)
    {
        if (e.Points.Count == 0) return;
        bool Near((int X, int Y) q) => (uint)((q.Y - n.Y) * (q.Y - n.Y) + (q.X - n.X) * (q.X - n.X)) < (uint)(r * r);
        if (n == e.A)
        {
            int k = e.Points.FindLastIndex(Near);
            if (k >= 0) e.Points.RemoveRange(0, k + 1);
        }
        else
        {
            int k = e.Points.FindIndex(Near);
            if (k >= 0) e.Points.RemoveRange(k, e.Points.Count - k);
        }
    }

    /// <summary><c>0x14211e100</c>: the point w from a toward b (nine tenths of the way when b is nearer than
    /// 10/9 · w).</summary>
    static (int X, int Y) Toward((int X, int Y) a, (int X, int Y) b, int w)
    {
        int dx = b.X - a.X, dy = b.Y - a.Y, d2 = dy * dy + dx * dx;
        if ((uint)(w * w * 100) < (uint)(d2 * 0x51))
        {
            int len = (int)FortuneVoronoi.IntSqrt((uint)d2);
            return (dx * w / len + a.X, dy * w / len + a.Y);
        }
        return (dx * 9 / 10 + a.X, dy * 9 / 10 + a.Y);
    }

    /// <summary><c>0x14211e1d0</c>: where two virtual edges meeting at a corner b (coming from a, going to c) get their
    /// shared end point: the crossing of the two lines offset by w, on the side away from d (the next edge's point)
    /// unless the two edges' sides disagree.</summary>
    static (int X, int Y) Between((int X, int Y) a, (int X, int Y) b, (int X, int Y) c, (int X, int Y) d, int w)
    {
        int bx = b.X, by = b.Y, ax = a.X, ay = a.Y;
        int dy1 = by - ay, dx1 = bx - ax, dx2 = c.X - bx, dy2 = c.Y - by;
        int mx = (c.X + ax) / 2, my = (c.Y + ay) / 2;
        int side2 = (my - by) * dx2 - (mx - bx) * dy2;
        bool side1 = 0 < (my - ay) * dx1 - (mx - ax) * dy1;
        bool flip = !(side1 == (0 < (d.Y - ay) * dx1 - (d.X - ax) * dy1) &&
                      (0 < side2) == (0 < (d.Y - by) * dx2 - (d.X - bx) * dy2));
        int l1 = (int)FortuneVoronoi.IntSqrt((uint)(dy1 * dy1 + dx1 * dx1));
        int l2 = (int)FortuneVoronoi.IntSqrt((uint)(dy2 * dy2 + dx2 * dx2));
        (int X, int Y) p, q;
        int ox = -dy1 * w / l1, oy = w * dx1 / l1;
        if (dx2 * -dy1 == -dy2 * dx1)
        {
            p = (bx + ox, by + oy);
            q = (bx - ox, by - oy);
        }
        else
        {
            int x1 = ox + ax, x2 = ax - ox, y2 = ay - oy, y1 = ay + oy;
            int ox2 = -dy2 * w / l2, oy2 = dx2 * w / l2;
            int det = dx1 * dy2 - dy1 * dx2;
            int t1 = ((y1 - by - oy2) * dx2 - (x1 - bx - ox2) * dy2) / det;
            int t2 = ((y2 - by + oy2) * dx2 - (x2 - bx + ox2) * dy2) / det;
            p = (x1 + t1 * dx1, t1 * dy1 + y1);
            q = (t2 * dx1 + x2, t2 * dy1 + y2);
        }
        bool pOnSide = side1 == (0 < (p.Y - ay) * dx1 - (p.X - ax) * dy1) && (0 < side2) == (0 < (p.Y - by) * dx2 - (p.X - bx) * dy2);
        if (pOnSide) return flip ? p : q;
        return flip ? q : p;
    }

    /// <summary><c>0x14211dbc0</c>: every edge after the node's first gets its own node at the same place (with the
    /// node's height); neighbouring edge ends (around the node) where either edge is virtual become logical
    /// links.</summary>
    void SplitNode(GraphNode n)
    {
        if (n.Degree < 2) throw new InvalidOperationException("Trying to create inappropriate logical link!");
        var ends = new List<(GraphEdge Edge, int End)>();
        foreach (var e in n.Slots)
            if (e is not null) ends.Add((e, e.A != n ? 1 : 0));
        var links = new List<(GraphEdge E1, int End1, GraphEdge E2, int End2)>();
        for (int i = 0; i < ends.Count; i++)
        {
            var next = ends[(i + 1) % ends.Count];
            if (Type(ends[i].Edge).Virtual || Type(next.Edge).Virtual) links.Add((ends[i].Edge, ends[i].End, next.Edge, next.End));
        }
        for (int i = 1; i < ends.Count; i++)
        {
            var created = new GraphNode { X = n.X, Y = n.Y, Height = n.Height, Exist = 100 };
            var (e, end) = ends[i];
            var old = end == 0 ? e.A : e.B;
            int slot = Array.IndexOf(old.Slots, e);
            if (slot >= 0) old.Slots[slot] = null;
            if (end == 0) e.A = created; else e.B = created;
            created.Slots[0] = e;
            graph.Nodes.Add(created);
        }
        foreach (var (e1, end1, e2, end2) in links)
        {
            var a = end1 == 0 ? e1.A : e1.B;
            var b = end2 == 0 ? e2.A : e2.B;
            int index = graph.Links.Count;
            AddLink(a, index);
            AddLink(b, index);
            graph.Links.Add(new LogicalLink(a, b));
        }
    }

    // 0x14211f010.
    static void AddLink(GraphNode n, int index)
    {
        if (n.Link2 != -1) throw new InvalidOperationException($"Too many logical links for node at ({n.X}, {n.Y})");
        if (n.Link1 == -1) n.Link1 = index;
        else n.Link2 = index;
    }

    /// <summary><c>0x14211f100</c>.</summary>
    void Classify(GraphNode n)
    {
        int degree = n.Degree;
        bool hasVirtual = n.Slots.Any(e => e is not null && Type(e).Virtual);
        n.OnVirtual = hasVirtual;
        if (n.Tag.Length != 0) { Tagged.Add(n); return; }
        switch (degree)
        {
            case 0:
                if (n.Descriptions.Count != 0) Lonely.Add(n);
                return;
            case 1:
                (!hasVirtual || AtBorder(n) ? Plain : Virtual).Add(n);
                return;
            case 2:
                var two = n.Slots.Where(e => e is not null).ToList();
                if (two[0]!.Type == two[1]!.Type) return;
                break;
        }
        (hasVirtual ? Virtual : Plain).Add(n);
    }

    /// <summary><c>0x14211f2b0</c>: a node with one edge that lies within the edge's size of the graph's border.</summary>
    bool AtBorder(GraphNode n)
    {
        var first = n.Slots.First(e => e is not null)!;
        if (n.Degree != 1) return false;
        int size = Type(first).Size, tx = n.X / 23, ty = n.Y / 23;
        return !(size <= tx && size <= ty && tx < graph.Width - size && ty < graph.Height - size);
    }

    // The point next to n along e: its first internal point, else its other node.
    static (int X, int Y) Next(GraphNode n, GraphEdge e)
    {
        if (n == e.A) return e.Points.Count == 0 ? (e.B.X, e.B.Y) : e.Points[0];
        return e.Points.Count == 0 ? (e.A.X, e.A.Y) : e.Points[^1];
    }

    static readonly (int X, int Y)[] SideVectors = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    /// <summary><c>0x1421299f0</c>: the side (0 up, 1 right, 2 down, 3 left; 4 none) each edge slot leaves the node by.
    /// Each edge takes the side its direction points to; while a side holds more than one edge, the edge that turns
    /// least (<see cref="GraphGeometry.Angle"/>) moves to a free neighbouring side, else to one holding a single edge. Null when a side
    /// keeps two edges.</summary>
    int[]? EdgeSides(GraphNode n)
    {
        var sides = new List<int>[4];
        for (int s = 0; s < 4; s++) sides[s] = [];
        for (int i = 0; i < 4; i++)
        {
            var e = n.Slots[i];
            if (e is null || Type(e).Wildcard) continue;
            var p = Next(n, e);
            int dx = p.X - n.X, dy = p.Y - n.Y;
            int side = Math.Abs(dy) < Math.Abs(dx) ? (dx > 0 ? 1 : 3) : (dy > 0 ? 2 : 0);
            sides[side].Add(i);
        }
        for (int guard = 0; ; guard++)
        {
            if (guard > 64) throw new InvalidOperationException($"{n}: edge sides do not settle");
            var moves = Moves(n, sides, 0);
            if (moves.Count == 0) moves = Moves(n, sides, 1);
            if (moves.Count == 0) break;
            var best = moves[0];
            foreach (var m in moves.Skip(1)) if (m.Cost < best.Cost) best = m;
            sides[best.To].Add(best.Edge);
            sides[best.From].Remove(best.Edge);
        }
        var result = new[] { 4, 4, 4, 4 };
        for (int s = 0; s < 4; s++)
        {
            if (sides[s].Count > 1) return null;
            if (sides[s].Count == 1) result[sides[s][0]] = s;
        }
        return result;
    }

    // 0x142129d80 / 0x142129e90: for every side with more than one edge, each edge's move to a neighbouring side
    // (side − 1, then side + 1) that holds `holding` edges.
    List<(ushort Cost, int From, int To, int Edge)> Moves(GraphNode n, List<int>[] sides, int holding)
    {
        var moves = new List<(ushort, int, int, int)>();
        for (int u = 0; u < 4; u++)
        {
            if (sides[u].Count < 2) continue;
            foreach (int edge in sides[u])
                foreach (int to in new[] { (u - 1) & 3, (u + 1) & 3 })
                {
                    if (sides[to].Count != holding) continue;
                    var p = Next(n, n.Slots[edge]!);
                    var v = SideVectors[to];
                    moves.Add((GraphGeometry.Angle(v.X * 10, v.Y * 10, p.X - n.X, p.Y - n.Y), u, to, edge));
                }
        }
        return moves;
    }

    /// <summary><c>0x1421203f0</c>: the node's edges as a key, its candidates (<see cref="Candidates"/>), then a weighted
    /// pick: the pick is centred on the node, snapped, clamped to the graph and must fit (<c>0x141d84e10</c>,
    /// <c>0x14211c820</c>); one that does not is dropped and the pick repeats.</summary>
    NodePlacement? Place(GraphNode n)
    {
        int tx = Math.Min(n.X / 23, graph.Width - 1), ty = Math.Min(n.Y / 23, graph.Height - 1);
        var sides = EdgeSides(n);
        if (sides is null)
        {
            notes.Add($"{n}: failed to convert graph edges to TileEdgeKey");
            return null;
        }
        // 0x14212b5f0.
        var key = new TileKey();
        for (int i = 0; i < 4; i++)
            if (sides[i] < 4) key.Edges[sides[i]] = n.Slots[i]!.Type;
        var list = Candidates(n, key, sides);
        if (list.Count == 0)
        {
            notes.Add($"{n}: no rooms or tiles match its edge key {key}");
            return null;
        }
        byte align = Align(n);
        bool accessible = Accessible(n);
        int total = list.Sum(c => c.Weight);
        while (true)
        {
            int r = (int)rng.Next((uint)total);
            if (total == 0 || list.Count == 0) return null;
            int k = 0;
            while (k < list.Count - 1 && r >= list[k].Weight) r -= list[k++].Weight;
            var c = list[k];
            int w = c.Key.Width, h = c.Key.Height;
            int t = Math.Min(GraphOrientation, 8) * 3;
            int[] odd = [1, 0, 1, 0];
            int y = ty - ((h >> 1) - ((h - 1) & 1) * odd[(Orientation.Table[t] == 0 ? 2 : 0) + Orientation.Table[t + 2]]);
            if (y < 0) y = 0;
            int x = ((w - 1) & 1) * odd[Orientation.Table[t + 1] + Orientation.Table[t] * 2] - (w >> 1) + tx;
            if (x < 0) x = 0;
            (x, y) = Snap(x, y, align, GraphOrientation);
            x = Math.Min(Math.Max(x, 0), graph.Width - w);
            y = Math.Min(Math.Max(y, 0), graph.Height - h);
            if (x < graph.Width && x + w <= graph.Width && y < graph.Height && y + h <= graph.Height &&
                CellsFree(x + offsetX, y + offsetY, w, h) && !Misaligned(x, y, c.Key))
            {
                int size = n.Slots.Where(e => e is not null).Select(e => Type(e!).Size).DefaultIfEmpty(0).Max();
                (x, y) = KeepEdgesInside(x, y, size, c.Key);
                var placement = new NodePlacement
                {
                    Path = c.Path, Room = c.Room, Tile = c.Tile, Orientation = c.Orientation, Key = c.Key, X = x, Y = y,
                    Node = n, Sides = sides, Align = align, Accessible = accessible,
                };
                if (n.Orientation == 8) TurnToSides(placement);
                else FitSides(placement);
                return placement;
            }
            // Drop the first candidate with this path and orientation.
            int drop = list.FindIndex(o => o.Path == c.Path && o.Orientation == c.Orientation);
            total -= list[drop].Weight;
            list.RemoveAt(drop);
            if (list.Count == 0)
            {
                notes.Add($"{n}: no matching room or tile fits into the tile map");
                return null;
            }
        }
    }

    // "align<n>" among the node's descriptions, else the largest size of its edge types (0x142126df0).
    byte Align(GraphNode n)
    {
        foreach (var d in n.Descriptions)
            if (d.Length > 5 && d.StartsWith("align", StringComparison.Ordinal) && char.IsDigit(d[5]))
            {
                int v = int.TryParse(new string(d.Skip(5).TakeWhile(char.IsDigit).ToArray()), out int a) ? a : 0;
                if (v != 0) return (byte)v;
                break;
            }
        return (byte)n.Slots.Where(e => e is not null).Select(e => Type(e!).Size).DefaultIfEmpty(0).Max();
    }

    // +0x92: "accessible", "default", "entrance…" or "sideentrance…" among the descriptions (the edge-type test
    // 0x142120310 is not ported).
    static bool Accessible(GraphNode n) => n.Descriptions.Any(d => d == "accessible" || d == "default" ||
        (d.Length > 7 && d.StartsWith("entrance", StringComparison.Ordinal)) ||
        (d.Length > 11 && d.StartsWith("sideentrance", StringComparison.Ordinal)));

    /// <summary><c>0x14202ecc0</c>: a position snapped to the alignment grid (to the nearer multiple, by the orientation's
    /// rounding for a middle remainder).</summary>
    static (int X, int Y) Snap(int x, int y, int align, int orientation)
    {
        if (align <= 1) return (x, y);
        int t = Math.Min(orientation, 8) * 3;
        int[] f = [1, 0, 1, 0];
        int half = align >> 1;
        int u6 = Orientation.Table[t] == 0 ? 2 : 0;
        int addY = align;
        uint ry = (uint)y % (uint)align, rx = (uint)x % (uint)align;
        if (half < ry) u6 = align & 1;
        else if (f[u6 + Orientation.Table[t + 2]] == 0 || (align & 1) != 0 || (u6 = 0) != 0 || ry != half)
        {
            u6 = align & 1;
            addY = 0;
        }
        int addX = 0;
        if (half < rx || (f[Orientation.Table[t + 1] + Orientation.Table[t] * 2] != 0 && u6 == 0 && rx == half)) addX = align;
        return (x - x % align + addX, y - y % align + addY);
    }

    bool CellsFree(int x, int y, int w, int h)
    {
        if (x < 0 || y < 0 || x + w > grid.Width || y + h > grid.Height) return false;
        for (int v = 0; v < h; v++)
            for (int u = 0; u < w; u++)
                if (grid[x + u, y + v] is not null) return false;
        return true;
    }

    /// <summary><c>0x14211c820</c>: a placed tile just outside the candidate's footprint, across one of its edges, whose
    /// edge points do not line up with the candidate's.</summary>
    bool Misaligned(int x, int y, TileKey key)
    {
        if (x > 0 && key.Edges[3] is not null && Neighbour(x - 1, y + key.Height - 1 - key.PointA[3] / 3) is { } left)
        {
            int row = y + key.Height - 1 - key.PointA[3] / 3;
            if (left.Key.PointA[1] % 3 != 2 - key.PointA[3] % 3 || row - left.OffsetY + left.Key.PointA[1] / 3 != row) return true;
        }
        if (x + key.Width < graph.Width && key.Edges[1] is not null && Neighbour(x + key.Width, y + key.PointA[1] / 3) is { } right)
        {
            int row = y + key.PointA[1] / 3;
            if (2 - right.Key.PointA[3] % 3 != key.PointA[1] % 3 || right.Key.Height - 1 + row - right.OffsetY - right.Key.PointA[3] / 3 != row) return true;
        }
        if (y + key.Height < graph.Height && key.Edges[2] is not null && Neighbour(x + key.Width - 1 - key.PointA[2] / 3, y + key.Height) is { } down)
        {
            int col = x + key.Width - 1 - key.PointA[2] / 3;
            if (down.Key.PointA[0] % 3 != 2 - key.PointA[2] % 3 || col - down.OffsetX + down.Key.PointA[0] / 3 != col) return true;
        }
        if (y > 0 && key.Edges[0] is not null && Neighbour(x + key.PointA[0] / 3, y - 1) is { } up)
        {
            int col = x + key.PointA[0] / 3;
            if (2 - up.Key.PointA[2] % 3 != key.PointA[0] % 3 || up.Key.Width - 1 + col - up.OffsetX - up.Key.PointA[2] / 3 != col) return true;
        }
        return false;
    }

    // The placed tile at a graph position, its key turned by its orientation, and the entry's offset inside it.
    (TileKey Key, int OffsetX, int OffsetY)? Neighbour(int x, int y) =>
        grid[x + offsetX, y + offsetY] is { } e ? (e.Tile.Key.Rotate(e.Orientation), e.OffsetX, e.OffsetY) : null;

    /// <summary><c>0x14211d6b0</c>: keeps room on each side that has an edge for the edge (its size, twice for a virtual
    /// edge).</summary>
    (int X, int Y) KeepEdgesInside(int x, int y, int size, TileKey key)
    {
        int Room(string? edge) => ((files.Edge(edge)?.Virtual ?? false) ? 2 : 1) * size;
        if (key.Edges[0] is not null) y = Math.Min(Math.Max(Room(key.Edges[0]), y), graph.Height - key.Height);
        if (key.Edges[3] is not null) x = Math.Min(Math.Max(Room(key.Edges[3]), x), graph.Width - key.Width);
        if (key.Edges[2] is not null) y = Math.Max(0, Math.Min(graph.Height - Room(key.Edges[2]) - key.Height, y));
        if (key.Edges[1] is not null) x = Math.Max(0, Math.Min(graph.Width - Room(key.Edges[1]) - key.Width, x));
        return (x, y);
    }

    /// <summary><c>0x14211f3c0</c>: for a tagged node, a key some room or tile of its tag has (<c>0x14211ce90</c>); then
    /// the rooms of the node's tag whose turned outer edges are the key's (<c>0x142012a40</c>), each turned by the node's
    /// orientation (<c>0x14211fb20</c>); with none, tiles (not ported).</summary>
    List<(string Path, RoomDef? Room, TileDef? Tile, int Orientation, TileKey Key, int Weight)> Candidates(GraphNode n, TileKey nodeKey, int[] sides)
    {
        var result = new List<(string, RoomDef?, TileDef?, int, TileKey, int)>();
        var key = nodeKey.Clone();
        if (n.Tag.Length != 0 && !TagHasKey(n, key, sides)) return result;
        var matches = rooms.Variants(n.Tag).Where(v => v.Edges.AsSpan().SequenceEqual(key.Edges)).ToList();
        if (n.Orientation < 8)
        {
            foreach (var v in matches)
                if (v.Weight != 0) result.Add((v.Room.Path, v.Room, null, n.Orientation, v.Room.Key.Rotate(n.Orientation), v.Weight));
        }
        else
        {
            // 0x14211fcc0: a variant whose turned key suits the node's sides, strictly, else loosely.
            foreach (bool strict in new[] { true, false })
            {
                foreach (var v in matches)
                {
                    var k = v.Room.Key.Rotate(v.Orientation);
                    if (v.Weight != 0 && SidesFit(k, sides, n, strict)) result.Add((v.Room.Path, v.Room, null, v.Orientation, k, v.Weight));
                }
                if (result.Count != 0) break;
            }
        }
        if (result.Count != 0) return result;
        // 0x141d811c0 / 0x141d82280: the sizes of the tag's tiles with these edges, then per size every tile (of any
        // tag, in each orientation) with these edges and size; the node's tag picks among them (0x14211fe80 for a fixed
        // orientation, 0x142120090 with the strict, then the loose, side check otherwise).
        foreach (var (w, h) in store.Sizes(n.Tag, key.Edges))
        {
            var list = store.ByEdgesAndSize(key.Edges, w, h);
            if (list is null && w != h) list = store.ByEdgesAndSize(key.Edges, h, w);
            if (list is null) continue;
            if (n.Orientation < 8)
            {
                foreach (var (tile, _, weight) in list)
                    if (tile.Tag == n.Tag && weight != 0) result.Add((tile.Path, null, tile, n.Orientation, tile.Key.Rotate(n.Orientation), weight));
                continue;
            }
            foreach (bool strict in new[] { true, false })
            {
                if (!strict && result.Count != 0) break;
                foreach (var (tile, o, weight) in list)
                {
                    if (tile.Tag != n.Tag) continue;
                    var k = tile.Key.Rotate(o);
                    if (weight != 0 && SidesFit(k, sides, n, strict)) result.Add((tile.Path, null, tile, o, k, weight));
                }
            }
        }
        return result;
    }

    /// <summary><c>0x14211ce90</c> / <c>0x14211d240</c>: some tile or room of the node's tag has these edges; for a node
    /// with two edges the first one may move to the free neighbouring side; with three, the edge after the free side
    /// moves into it, or else (moved back) the edge before it.</summary>
    bool TagHasKey(GraphNode n, TileKey key, int[] sides)
    {
        bool Has() => store.HasEdges(n.Tag, key.Edges) || rooms.Variants(n.Tag).Any(v => v.Edges.AsSpan().SequenceEqual(key.Edges));
        if (Has()) return true;
        var real = Enumerable.Range(0, 4).Where(i => n.Slots[i] is { } e && !Type(e).Wildcard).ToList();
        if (real.Count == 2)
        {
            int i = real[0], s = sides[i];
            int to = (s + (key.Edges[(s + 1) & 3] is null ? 1 : -1)) & 3;
            key.Edges[to] = key.Edges[s];
            key.Edges[s] = null;
            sides[i] = to;
            return Has();
        }
        if (real.Count != 3) return false;
        // Side → slot (4 = free).
        var at = new[] { 4, 4, 4, 4 };
        for (int i = 0; i < 4; i++) if (sides[i] != 4) at[sides[i]] = i;
        int free = Array.IndexOf(at, 4);
        void Move(int from, int to)
        {
            int slot = at[from];
            key.Edges[to] = key.Edges[from];
            key.Edges[from] = null;
            at[to] = slot;
            at[from] = 4;
            sides[slot] = to;
        }
        Move((free + 1) & 3, free);
        if (Has()) return true;
        Move(free, (free + 1) & 3);
        Move((free - 1) & 3, free);
        return Has();
    }

    /// <summary><c>0x142110020</c>: the sides must suit the placed key (<c>0x14210fc70</c>); else they turn by one, then
    /// back by two, then are mirrored, then turned.</summary>
    void FitSides(NodePlacement p)
    {
        var s = p.Sides;
        if (SidesFit(p.Key, s, p.Node)) return;
        for (int i = 0; i < 4; i++) if (s[i] != 4) s[i] = (s[i] + 1) & 3;
        if (SidesFit(p.Key, s, p.Node)) return;
        for (int i = 0; i < 4; i++) if (s[i] != 4) s[i] = (s[i] - 2) & 3;
        if (SidesFit(p.Key, s, p.Node)) return;
        byte[] mirror = [3, 2, 1, 0, 4];
        for (int i = 0; i < 4; i++) s[i] = mirror[s[i] == 4 ? 4 : (s[i] + 1) & 3];
        if (SidesFit(p.Key, s, p.Node)) return;
        byte[] turn = [1, 0, 3, 2, 4];
        for (int i = 0; i < 4; i++) s[i] = turn[mirror[s[i]]];
        if (SidesFit(p.Key, s, p.Node)) return;
        throw new InvalidOperationException($"Edge to side doesn't work for node at ({p.Node.X}, {p.Node.Y})");
    }

    /// <summary><c>0x142121910</c>: an any-orientation placement turns a quarter either way when that brings its edges'
    /// sides nearer their directions (<see cref="SideCost"/>) without pointing one off a map border it touches.</summary>
    void TurnToSides(NodePlacement p)
    {
        var plus = p.Sides.Select(s => s == 4 ? 4 : (s + 1) & 3).ToArray();
        var minus = p.Sides.Select(s => s == 4 ? 4 : (s - 1) & 3).ToArray();
        int cost = SideCost(p.Sides, p.Node), costMinus = SideCost(minus, p.Node);
        bool Allowed(int[] sides) =>
            !(p.X == 0 && sides.Contains(3)) && !(p.Y == 0 && sides.Contains(0)) &&
            !(p.Key.Width + p.X == graph.Width && sides.Contains(1)) && !(p.Key.Height + p.X == graph.Width && sides.Contains(2));
        bool canMinus = Allowed(minus), canPlus = Allowed(plus);
        void Turn(int[] sides, int code)
        {
            Array.Copy(sides, p.Sides, 4);
            p.Orientation = DungeonTiler.Compose(p.Orientation, code);
            p.Key = p.Key.Rotate(code);
        }
        if (canMinus && costMinus < cost)
        {
            if (canPlus && SideCost(plus, p.Node) < costMinus) Turn(plus, 1);
            else Turn(minus, 3);
        }
        else if (canPlus && SideCost(plus, p.Node) < cost) Turn(plus, 1);
    }

    /// <summary><c>0x1421102d0</c>: the sum over the node's edges of the angle (in degrees) between the side each
    /// leaves by and its direction.</summary>
    int SideCost(int[] sides, GraphNode n)
    {
        int total = 0;
        for (int i = 0; i < 4; i++)
        {
            var e = n.Slots[i];
            if (e is null || sides[i] >= 4) continue;
            var q = Next(n, e);
            var v = SideVectors[sides[i]];
            int a = (short)GraphGeometry.Angle(v.X, v.Y, q.X - n.X, q.Y - n.Y);
            total += Math.Abs(a) / 0xb6;
        }
        return total;
    }

    /// <summary><c>0x14210fc70</c> (not strict): each edge's type is the key's on its side, and the key's corners at
    /// that side carry the edge type's grounds (in the edge's direction) and heights.</summary>
    bool SidesFit(TileKey key, int[] sides, GraphNode n, bool strict = false)
    {
        for (int i = 0; i < 4; i++)
            if (sides[i] < 4 && key.Edges[sides[i]] != n.Slots[i]?.Type) return false;
        var flip = new int[4];
        var rise = new int[4];
        for (int i = 0; i < 4; i++)
        {
            var e = n.Slots[i];
            if (e is null || Type(e).Wildcard) continue;
            flip[sides[i]] = (e.A == n ? 1 : 0) ^ e.Chirality;
            rise[sides[i]] = Math.Max(0, (e.A == n ? e.B : e.A).Height - n.Height);
        }
        for (int s = 0; s < 4; s++)
        {
            var et = files.Edge(key.Edges[s]);
            if (et is null || et.Wildcard) continue;
            int c1 = (s - flip[s] + 1) & 3, c2 = (flip[s] + s) & 3;
            int low = Math.Min(key.Heights[c1], key.Heights[c2]);
            bool Ground(string? want, string? corner) =>
                corner == want || (want is null && (corner is null || (files.Ground(corner)?.Flags & 8) != 0));
            bool wrongGrounds = !Ground(et.Ground1, key.Grounds[c2]) || !Ground(et.Ground2, key.Grounds[c1]);
            bool open = OpenGround(key.Grounds[c2]) && OpenGround(key.Grounds[c1]);
            bool wrongHeights = !(key.Heights[c2] == low && key.Heights[c1] - low == et.N0);
            bool wrongRise = et.N0 == 0 && Math.Max(key.Heights[c1], key.Heights[c2]) != rise[s];
            if (wrongGrounds || (open && wrongHeights) || (strict && wrongRise)) return false;
        }
        return true;
    }

    bool OpenGround(string? g) => g is null || (files.Ground(g)?.Flags & 1) != 0;

    /// <summary><c>0x142121610</c>: a room's tiles (<c>0x142120fa0</c> → <c>0x141fea780</c>) at its snapped position.</summary>
    void Tilise(NodePlacement p)
    {
        if (p.Align > 1) (p.X, p.Y) = Snap(p.X, p.Y, p.Align, GraphOrientation);
        if (!FindRoomForTiles(p)) throw new InvalidOperationException($"Overlapping node tiles at ({p.X}, {p.Y})");
        if (p.Tile is { } tile)
        {
            // A 1 × 1 tile for a node without edges or tag goes only on an empty entry.
            var n = p.Node;
            if (p.Key.Width >= 2 || p.Key.Height >= 2 || n.Degree != 0 || n.Tag.Length != 0 || grid[p.X + offsetX, p.Y + offsetY] is null)
                grid.Place(tile, p.Orientation, p.X + offsetX, p.Y + offsetY, 0);
        }
        else TiliseRoom(p.Room!, p.X, p.Y, p.Orientation, fillGrounds: true, p.Node.Descriptions);
        TrimPoints(p);
        var exits = Exits(p);
        MoveToExits(p, exits);
    }

    /// <summary><c>0x142121380</c>: before tilising, the footprint plus a margin for its edges (the align on the top and
    /// left, the edge type's size on the right and bottom) must be free and the corners must suit the map
    /// (<see cref="CornersFit"/>). Else a spiral (<c>0x142121210</c>: runs of 1, 1, 2, 2 … 9, 9 steps of the align,
    /// turning up, right, down, left) looks for such a place from the margin's corner (<c>0x142132b80</c>), then for the
    /// bare footprint from the room's position (<c>0x142132a50</c>). False when neither finds one.</summary>
    bool FindRoomForTiles(NodePlacement p)
    {
        var k = p.Key;
        int left = k.Edges[3] is not null ? p.Align : 0, top = k.Edges[0] is not null ? p.Align : 0;
        int w = k.Width + (files.Edge(k.Edges[1])?.Size ?? 0) + left, h = k.Height + (files.Edge(k.Edges[2])?.Size ?? 0) + top;
        bool Free(int x, int y, int fw, int fh) =>
            x < graph.Width && x + fw <= graph.Width && y < graph.Height && y + fh <= graph.Height && CellsFree(x + offsetX, y + offsetY, fw, fh);
        int mx = Math.Max(p.X - left, 0), my = Math.Max(p.Y - top, 0);
        if (Free(mx, my, w, h) && CornersFit(p.X, p.Y, k)) return true;
        bool Spiral(int x, int y, Func<int, int, bool> at)
        {
            for (int run = 1, dir = 0; run <= 9; run += dir & 1, dir = (dir + 1) & 3)
                for (int i = 0; i < run; i++)
                {
                    x += p.Align * SideVectors[dir].X;
                    y += p.Align * SideVectors[dir].Y;
                    if (at(Math.Max(x, 0), Math.Max(y, 0))) return true;
                }
            return false;
        }
        bool Take(int x, int y)
        {
            if (!CornersFit(x, y, k)) return false;
            (p.X, p.Y) = (x, y);
            return true;
        }
        return Spiral(mx, my, (x, y) => Free(x, y, w, h) && Take(left + x, top + y)) ||
               Spiral(p.X, p.Y, (x, y) => Free(x, y, k.Width, k.Height) && Take(x, y));
    }

    /// <summary><c>0x141ffba10</c>: no side with a real edge on the map border, and each corner's ground agrees with a
    /// map vertex that has one (<c>0x141d85570</c>: equal, or both null or flag 8).</summary>
    bool CornersFit(int x, int y, TileKey k)
    {
        bool Real(string? e) => e is not null && files.Edge(e)?.Wildcard != true;
        if ((x == 0 && Real(k.Edges[3])) || (y == 0 && Real(k.Edges[0])) ||
            (x + k.Width == graph.Width && Real(k.Edges[1])) || (y + k.Height == graph.Height && Real(k.Edges[2])))
            return false;
        bool Loose(string? g) => g is null || (files.Ground(g)?.Flags & 8) != 0;
        bool Fits(int vx, int vy, string? g)
        {
            ref var c = ref grid.Corner(vx + offsetX, vy + offsetY);
            return (c.Flags & 1) == 0 || c.Ground == g || (Loose(g) && Loose(c.Ground));
        }
        return Fits(x, y, k.Grounds[0]) && Fits(x + k.Width, y, k.Grounds[1]) &&
               Fits(x + k.Width, y + k.Height, k.Grounds[2]) && Fits(x, y + k.Height, k.Grounds[3]);
    }

    /// <summary><c>0x14211d420</c>: an edge's internal points inside the room's footprint go (from its node's end).</summary>
    static void TrimPoints(NodePlacement p)
    {
        int x0 = p.X * 23, y0 = p.Y * 23, x1 = Math.Max(x0, x0 + p.Key.Width * 23), y1 = Math.Max(y0, y0 + p.Key.Height * 23);
        bool Inside((int X, int Y) q) => q.X >= x0 && q.X < x1 && q.Y >= y0 && q.Y < y1;
        foreach (var e in p.Node.Slots)
        {
            if (e is null) continue;
            if (e.A == p.Node)
            {
                int k = 0;
                while (k < e.Points.Count && Inside(e.Points[k])) k++;
                e.Points.RemoveRange(0, k);
            }
            else
            {
                int k = 0;
                while (k < e.Points.Count && !Inside(e.Points[k])) k++;
                e.Points.RemoveRange(k, e.Points.Count - k);
            }
        }
    }

    // 0x142fa09b8: where edge points 0, 1, 2 of a tile side lie along its 23 positions.
    static readonly int[] PointAlong = [3, 11, 19];

    // The position of edge point `point` on side `side` of a w × h footprint at (x, y) (in graph tiles).
    static (int X, int Y) SidePoint(int side, int point, int x, int y, int w, int h)
    {
        int u = point / 3, along = PointAlong[point % 3] + u * 23;
        var (px, py) = side switch
        {
            0 => (along, 0),
            1 => (w * 23 - 1, along),
            2 => (w * 23 - along - 1, h * 23 - 1),
            3 => (0, h * 23 - along - 1),
            _ => (0, 0),
        };
        return (px + x * 23, py + y * 23);
    }

    /// <summary><c>0x14211d830</c>: per edge slot, where the edge leaves the room: the key's edge point on the edge's
    /// side (a virtual edge: both its points, kept as the edge's end points, <c>0x14211e540</c>, and their middle). The
    /// edge's end offset becomes the side's direction.</summary>
    (int X, int Y)[] Exits(NodePlacement p)
    {
        var exits = new (int X, int Y)[4];
        for (int i = 0; i < 4; i++)
        {
            var e = p.Node.Slots[i];
            if (e is null || Type(e).Wildcard) continue;
            int s = p.Sides[i];
            int end = e.A != p.Node ? 1 : 0;
            var v = s < 4 ? SideVectors[s] : (0, 0);
            if (end == 0) e.StartOffset = v; else e.EndOffset = v;
            int w = p.Key.Width, h = p.Key.Height;
            if (!Type(e).Virtual)
            {
                exits[i] = s < 4 ? SidePoint(s, p.Key.PointA[s], p.X, p.Y, w, h) : (p.X * 23, p.Y * 23);
                continue;
            }
            SetVirtualEnd(e, end, s < 4 ? SidePoint(s, p.Key.PointB[s], p.X, p.Y, w, h) : (p.X * 23, p.Y * 23));
            SetVirtualEnd(e, end, s < 4 ? SidePoint(s, p.Key.PointA[s], p.X, p.Y, w, h) : (p.X * 23, p.Y * 23));
            var a = e.VirtualEnd1[end];
            var b = e.VirtualEnd2[end];
            var mid = ((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            exits[i] = mid;
            if ((mid.Item1 - (v.Item1 + mid.Item1)) * (a.Y - (v.Item2 + mid.Item2)) -
                (a.X - (v.Item1 + mid.Item1)) * (mid.Item2 - (v.Item2 + mid.Item2)) < 0)
                (e.VirtualEnd1[end], e.VirtualEnd2[end]) = (b, a);
        }
        return exits;
    }

    /// <summary><c>0x14211e540</c>: the first call sets end point 1, the second end point 2, then orders the two by the
    /// side of the edge's line they lie on.</summary>
    static void SetVirtualEnd(GraphEdge e, int end, (int X, int Y) q)
    {
        if (!e.VirtualEndSet[end])
        {
            if (e.VirtualEnd1[end] != (-1, -1) || e.VirtualEnd2[end] != (-1, -1)) throw new InvalidOperationException("Corrupt V-End points!");
            e.VirtualEnd1[end] = q;
            e.VirtualEndSet[end] = true;
            return;
        }
        var p1 = e.VirtualEnd1[end];
        if (p1 == (-1, -1) || e.VirtualEnd2[end] != (-1, -1)) throw new InvalidOperationException("Corrupt V-End points!");
        e.VirtualEnd2[end] = q;
        if (p1 == q) throw new InvalidOperationException("Virtual-End-Points are identical!");
        var node = end == 0 ? e.A : e.B;
        var r = Next(node, e);
        int cross = (p1.Y - r.Y) * (node.X - r.X) - (p1.X - r.X) * (node.Y - r.Y);
        int other = (node.X - r.X) * (q.Y - r.Y) - (node.Y - r.Y) * (q.X - r.X);
        if ((other < 0) == (cross < 0))
            cross = ((p1.X + q.X) / 2 - node.X) * (p1.Y - node.Y) - (p1.X - node.X) * ((p1.Y + q.Y) / 2 - node.Y);
        if (cross < 0) (e.VirtualEnd1[end], e.VirtualEnd2[end]) = (q, p1);
    }

    /// <summary><c>0x142126310</c>: the node moves to its first edge's exit; every other edge gets a new node at its
    /// exit (appended to the graph, with the node's height).</summary>
    void MoveToExits(NodePlacement p, (int X, int Y)[] exits)
    {
        var n = p.Node;
        n.Placed = true;
        bool moved = false;
        for (int i = 0; i < 4; i++)
        {
            var e = n.Slots[i];
            if (e is null) continue;
            if (!moved)
            {
                (n.X, n.Y) = exits[i];
                moved = true;
                continue;
            }
            var created = new GraphNode { X = exits[i].X, Y = exits[i].Y, Height = n.Height, Placed = true, Exist = 100 };
            if (e.A == n) e.A = created; else e.B = created;
            created.Slots[0] = e;
            n.Slots[i] = null;
            graph.Nodes.Add(created);
        }
    }

    /// <summary><c>0x142122d90</c>: the node's edges lose their internal points within 46 of it (<c>0x14211e860</c>), then
    /// each gets a point one tile out on its side (<see cref="EdgeSides"/>), clamped to the map.</summary>
    public void Explode(GraphNode n)
    {
        foreach (var e in n.Slots)
        {
            if (e is null || e.Points.Count == 0) continue;
            bool Near((int X, int Y) q) => (uint)((q.Y - n.Y) * (q.Y - n.Y) + (q.X - n.X) * (q.X - n.X)) < 46u * 46u;
            if (n == e.A)
            {
                int k = e.Points.FindLastIndex(Near);
                if (k >= 0) e.Points.RemoveRange(0, k + 1);
            }
            else
            {
                int k = e.Points.FindIndex(Near);
                if (k >= 0) e.Points.RemoveRange(k, e.Points.Count - k);
            }
        }
        var sides = EdgeSides(n) ?? throw new InvalidOperationException("Could not explode node");
        for (int i = 0; i < 4; i++)
        {
            var e = n.Slots[i];
            if (e is null) continue;
            if (sides[i] > 3) throw new InvalidOperationException("Could not explode node; invalid edge to side value");
            var v = SideVectors[sides[i]];
            int x = Math.Min(Math.Max(n.X + v.X * 23, 0), graph.Width * 23 - 1);
            int y = Math.Min(Math.Max(n.Y + v.Y * 23, 0), graph.Height * 23 - 1);
            if (e.A == n) e.Points.Insert(0, (x, y)); else e.Points.Add((x, y));
        }
    }

    // 0x141fea780's per-orientation offset of a slot's tile by (1 − w, 1 − h): x += a·(1 − w) + c·(1 − h),
    // y += b·(1 − w) + d·(1 − h).
    static readonly int[] SlotShift = [0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 1, 0, 1, 0, 0, 1, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0];

    /// <summary><c>0x141fea780</c>: every 'k' and 'f' slot, column by column. A slot without tag or edges whose corners share
    /// one ground is left for the fill (its entries remember the ground); any other picks a tile from the store:
    /// <c>Next(total weight)</c>.</summary>
    public void TiliseRoom(RoomDef room, int x0, int y0, int o, bool fillGrounds, IReadOnlyList<string> descriptions)
    {
        // Map +0x10 is off while a room is tilised.
        bool check = grid.CheckGrounds;
        grid.CheckGrounds = false;
        TiliseSlots(room, x0, y0, o, fillGrounds);
        // 0x141ffbd70: the room's own inner vertex grounds go in unchecked (0x141d85840), turned like a tile's.
        if (room.VertexGrounds.Length > 0)
            for (int v = 1; v < room.Height; v++)
                for (int u = 1; u < room.Width; u++)
                    if (room.VertexGrounds[(v - 1) * (room.Width - 1) + u - 1] is { } g)
                    {
                        var (dx, dy) = Orientation.MapVertex(o, u, v, room.Width, room.Height);
                        grid.ForceGround(x0 + dx + offsetX, y0 + dy + offsetY, g);
                        RoomVertices.Add((x0 + dx + offsetX, y0 + dy + offsetY));
                    }
        grid.CheckGrounds = check;
        var (w, h) = Orientation.Size(o, room.Width, room.Height);
        Rooms.Add(new PlacedRoom(room, x0 + offsetX, y0 + offsetY, w, h, descriptions, o));
    }

    void TiliseSlots(RoomDef room, int x0, int y0, int o, bool fillGrounds)
    {
        for (int col = 0; col < room.Width; col++)
            for (int row = 0; row < room.Height; row++)
            {
                var slot = room.Slots[col, row];
                if (slot is null || slot.Kind is not ('k' or 'f' or 's') || slot.Key is null) continue;
                var (gx, gy) = Orientation.Map(o, col + slot.ShiftX, row + slot.ShiftY, room.Width, room.Height);
                int sw = 1 - slot.Key.Width, sh = 1 - slot.Key.Height, so = Math.Min(o, 7) * 4;
                int x = x0 + gx + sw * SlotShift[so] + sh * SlotShift[so + 2];
                int y = y0 + gy + sw * SlotShift[so + 1] + sh * SlotShift[so + 3];
                var key = slot.Key.Rotate(o);
                if (slot.Tag.Length == 0 && key.Edges.All(e => e is null) && key.Grounds.Distinct().Count() == 1)
                {
                    // The slot's vertices take its ground (as the client loops: w columns, h + 1 rows).
                    if (fillGrounds && key.Grounds[0] is { } ground)
                        for (int v = 0; v <= key.Height; v++)
                            for (int u = 0; u < key.Width; u++)
                                grid.SetGround(x + u + offsetX, y + v + offsetY, ground);
                    continue;
                }
                var list = store.Candidates(slot.Tag, key);
                if (list.Count == 0)
                    throw new NotSupportedException($"{room.Path}: slot ({col}, {row}) {key} tag '{slot.Tag}' has no tile in the tile set (the second store and mission tiles are not ported)");
                int r = (int)rng.Next((uint)list.Sum(c => c.Weight));
                var pick = list[^1];
                foreach (var c in list)
                {
                    if (r < c.Weight) { pick = c; break; }
                    r -= c.Weight;
                }
                grid.Place(pick.Tile, pick.Orientation, x + offsetX, y + offsetY, (short)slot.Height);
                TilePlaced?.Invoke(pick.Tile, pick.Orientation, x, y);
            }
    }
}

/// <summary>A master's RoomSet as <c>0x14211f3c0</c> looks it up (master +0x1f8): per tag, every room in list order
/// in its eight orientations, each with its outer edges turned (0x60-byte entries, weight +0x58).</summary>
sealed class RoomSetIndex
{
    public readonly record struct Variant(RoomDef Room, int Orientation, string?[] Edges, int Weight);

    readonly Dictionary<string, List<Variant>> _byTag = new(StringComparer.Ordinal);

    public RoomSetIndex(TerrainCache files, string roomSetPath)
    {
        foreach (var line in TerrainText.Lines(files.Text(roomSetPath) ?? ""))
        {
            var t = TerrainText.Tokenize(line);
            int q = t.FindIndex(x => x.Quoted);
            if (q < 0) continue;
            int weight = q > 0 && t[0].IsNumber ? t[0].Int : 1;
            if (files.Room(TerrainText.Resolve(roomSetPath, t[q].Text)) is not { } room) continue;
            if (!_byTag.TryGetValue(room.Tag, out var list)) _byTag[room.Tag] = list = [];
            for (int o = 0; o < 8; o++) list.Add(new Variant(room, o, room.Key.Rotate(o).Edges, weight));
        }
    }

    public IReadOnlyList<Variant> Variants(string tag) => _byTag.TryGetValue(tag, out var l) ? l : [];
}

/// <summary>Integer geometry of the tile graph layout.</summary>
public static partial class GraphGeometry
{
    // 0x142fe0b10: acos((i − 127) / 127) as a fraction of 32768 = π.
    static readonly ushort[] AcosTable = Enumerable.Range(0, 255)
        .Select(i => (ushort)Math.Round(Math.Acos(Math.Clamp((i - 127) / 127.0, -1, 1)) / Math.PI * 32768)).ToArray();

    /// <summary><c>0x14299d730</c>: the angle between two vectors (0..32768 = 0..π), through the integer cosine.</summary>
    public static ushort Angle(int ax, int ay, int bx, int by)
    {
        if ((ax == 0 && ay == 0) || (bx == 0 && by == 0)) return 0;
        int la = ay * ay + ax * ax, lb = bx * bx + by * by;
        int root = (int)FortuneVoronoi.IntSqrt(unchecked((uint)(lb * la)));
        if (root == 0) root = 1;
        int twiceDot = unchecked(lb - (by - ay) * (by - ay) - (bx - ax) * (bx - ax) + la);
        int index = (sbyte)(twiceDot * 0x7f / root / 2) + 0x7f;
        return AcosTable[Math.Clamp(index, 0, 0xfe)];
    }
}
