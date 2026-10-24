namespace GameServer.Data.Terrain.Generation;

/// <summary><c>0x142113c10</c>: every virtual edge becomes its two sub-edges side by side. Ends on the map border get
/// their two end points first (<c>0x142111880</c>), then each chain of virtual edges is split from an end with end
/// points (<c>0x142112d00</c>): the edge keeps one sub-edge type, a copy takes the other, the internal points move
/// apart by a random width (<c>0x142111f60</c>), and new nodes join the copies.</summary>
sealed class EdgeSplitting(TileGraph graph, LevelRandom rng, Func<GraphEdge, EdgeType> type,
    List<GraphNode> plain, Func<string, EdgeType> edgeType)
{
    public void Run()
    {
        foreach (var e in graph.Edges.ToList())
            if (type(e).Virtual)
            {
                BorderEnd(e, 0);
                BorderEnd(e, 1);
            }
        var withEnd = new List<(GraphEdge Edge, int End)>();
        var without = new List<(GraphEdge Edge, int End)>();
        foreach (var e in graph.Edges)
        {
            if (!type(e).Virtual) continue;
            if (e.Split) throw new InvalidOperationException("Edge already split");
            if (e.Flag4a) throw new InvalidOperationException("Corrupt v-end points");
            if (e.VirtualEndSet[0]) withEnd.Add((e, 0));
            else if (e.VirtualEndSet[1]) withEnd.Add((e, 1));
            else without.Add((e, 0));
        }
        foreach (var (e, end) in withEnd.Concat(without))
            if (!e.Split && !e.Flag4a) Split(e, end == 0 ? e.A : e.B);
    }

    /// <summary><c>0x142111880</c>: a virtual edge's end at a node with only this edge, on the map border, gets its two
    /// end points along the border, a random width (<c>Next(max − min + 1) + min</c>) either side of the node (in a
    /// corner, along both borders).</summary>
    void BorderEnd(GraphEdge e, int end)
    {
        var n = end == 0 ? e.A : e.B;
        if (n.Degree != 1 || e.VirtualEndSet[end]) return;
        int ty = n.Y / 23, tx = n.X / 23, h23 = graph.Height * 23, w23 = graph.Width * 23;
        int w = (int)rng.Next((uint)(e.MaxWidth - e.MinWidth + 1)) + e.MinWidth;
        if (w == 0) throw new InvalidOperationException("Virtual edge off map has zero width!");
        ref var v1 = ref e.VirtualEnd1[end];
        ref var v2 = ref e.VirtualEnd2[end];
        if (tx == 0)
        {
            if (ty == 0)
            {
                v1 = (w, 0);
                v2 = (0, w);
                int a = Math.Abs(w / 23), b = Math.Abs(v1.Y / 23 - w / 23);
                if (a < 2 && b < 2)
                {
                    v1.X = (2 - a) * 23 + w;
                    v2.Y += (2 - b) * 23;
                }
            }
            else if (ty == graph.Height - 1)
            {
                v1 = (0, h23 - w - 1);
                v2 = (w, h23 - 1);
                int a = Math.Abs(w / 23), b = Math.Abs(v1.Y / 23 - v2.Y / 23);
                if (a < 2 && b < 2)
                {
                    v1.Y = (b - 2) * 23 + v1.Y;
                    v2.X += (2 - a) * 23;
                }
            }
            else
            {
                v1 = (0, n.Y - w);
                v2 = (0, n.Y + w);
            }
        }
        else if (tx == graph.Width - 1)
        {
            if (ty == 0)
            {
                v1 = (w23 - 1, w);
                v2 = (w23 - w - 1, 0);
                int a = Math.Abs((w23 - 1) / 23 - v2.X / 23), b = Math.Abs(v1.Y / 23);
                if (a < 2 && b < 2)
                {
                    v1.Y = (2 - b) * 23 + v1.Y;
                    v2.X += (a - 2) * 23;
                }
            }
            else if (ty == graph.Height - 1)
            {
                int x = w23 - w - 1;
                v1 = (x, h23 - 1);
                v2 = (w23 - 1, h23 - w - 1);
                int a = Math.Abs(x / 23 - (w23 - 1) / 23), b = Math.Abs(v1.Y / 23 - v2.Y / 23);
                if (a < 2 && b < 2)
                {
                    v1.X = (a - 2) * 23 + x;
                    v2.Y = (b - 2) * 23 + v2.Y;
                }
            }
            else
            {
                v1 = (w23 - 1, n.Y + w);
                v2 = (w23 - 1, n.Y - w);
            }
        }
        else if (ty == 0)
        {
            v1 = (n.X + w, 0);
            v2 = (n.X - w, 0);
        }
        else if (ty == graph.Height - 1)
        {
            v1 = (n.X - w, h23 - 1);
            v2 = (n.X + w, h23 - 1);
        }
        else return;
        e.VirtualEndSet[end] = true;
        n.OnVirtual = false;
    }

    static (int X, int Y) Next(GraphNode n, GraphEdge e)
    {
        if (n == e.A) return e.Points.Count == 0 ? (e.B.X, e.B.Y) : e.Points[0];
        return e.Points.Count == 0 ? (e.A.X, e.A.Y) : e.Points[^1];
    }

    /// <summary><c>0x142112d00</c>: splits the chain of virtual edges starting at <paramref name="node"/>.</summary>
    void Split(GraphEdge e, GraphNode node)
    {
        GraphEdge? prevOrig = null, prevCopy = null;
        GraphNode? firstSplitNode = null;
        (int X, int Y) firstMid = (-1, -1);
        bool loop = false, firstJoin = true;
        while (true)
        {
            if (!type(e).Virtual) throw new InvalidOperationException("Trying to split non-virtual edge!");
            int r = e.A != node ? 1 : 0, far = 1 - r;
            var farNode = far == 0 ? e.A : e.B;
            var nextEdge = farNode.Slots.FirstOrDefault(s => s is not null && s != e);
            var copy = e.Copy();
            GraphNode created;
            if (!e.VirtualEndSet[r])
            {
                (int X, int Y) pt;
                int w;
                if (prevOrig is null)
                {
                    if ((r == 0 ? e.A : e.B) != node) throw new InvalidOperationException("Cannot split edge: edge loop does not loop");
                    loop = true;
                    var o = node.Slots.FirstOrDefault(s => s is not null && s != e);
                    var widthOf = o ?? e;
                    pt = o is not null ? Next(node, o) : Next(farNode, e);
                    w = (int)rng.Next((uint)(widthOf.MaxWidth + e.MaxWidth - widthOf.MinWidth - e.MinWidth) / 2 + 1) +
                        (int)((uint)(widthOf.MinWidth + e.MinWidth) / 2);
                }
                else
                {
                    if (prevCopy is null) throw new InvalidOperationException("Cannot split edge: no previous edge(2) found");
                    w = (int)rng.Next((uint)(prevOrig.MaxWidth + e.MaxWidth - e.MinWidth - prevOrig.MinWidth) / 2 + 1) +
                        (int)((uint)(e.MinWidth + prevOrig.MinWidth) / 2);
                    var p1 = Next(node, prevOrig);
                    var p2 = Next(node, prevCopy);
                    pt = ((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
                }
                if (pt == (-1, -1)) throw new InvalidOperationException("Cannot split edge: previous point is invalid");
                var toward = Next(node, e);
                if (w == 0) throw new InvalidOperationException("Cannot split edge: edge has zero width");
                var (v1, v2) = OffsetPoints(pt, (node.X, node.Y), toward, w);
                e.VirtualEnd1[r] = v1;
                e.VirtualEnd2[r] = v2;
                if (v1.X < 0) throw new InvalidOperationException("Cannot split edge: bad virtual points encountered");
                created = graph.AddNode(v2);
                if (firstSplitNode is null && loop)
                {
                    firstMid = ((v1.X + v2.X) / 2, (v1.Y + v2.Y) / 2);
                    firstSplitNode = created;
                }
            }
            else
            {
                if (e.VirtualEnd1[r] == (-1, -1) || e.VirtualEnd2[r] == (-1, -1)) throw new InvalidOperationException("Cannot split edge: corrupt V-end points");
                created = graph.AddNode(e.VirtualEnd2[r]);
                e.Flag4a = true;
            }
            if (r == 0) copy.A = created; else copy.B = created;
            graph.AddEdge(copy.A, copy.B, copy);
            var endNode = r == 0 ? e.A : e.B;
            (endNode.X, endNode.Y) = e.VirtualEnd1[r];
            copy.Split = e.Split = true;
            created.Placed = node.Placed;
            created.Height = node.Height;
            created.OnVirtual = node.OnVirtual;
            created.Link1 = node.Link1;
            created.Link2 = node.Link2;
            if (plain.Contains(endNode)) plain.Add(created);
            SubTypes(e, copy, r);
            if (node.Link1 != -1) Join(node, created, node.Link1);
            if (node.Link2 != -1) Join(node, created, node.Link2);
            if (prevOrig is not null)
            {
                if (prevCopy is null) throw new InvalidOperationException("Cannot split edge: previous edge(2) missing");
                MoveToNew(prevOrig, prevCopy, node, created, firstJoin);
                firstJoin = false;
            }
            if (e.VirtualEndSet[far] || (nextEdge is not null && nextEdge.Split))
            {
                var f = far == 0 ? e.A : e.B;
                int fx = f.X / 23, fy = f.Y / 23;
                e.Flag4a = true;
                GraphNode copyFar;
                if (loop)
                {
                    if (firstSplitNode is null) throw new InvalidOperationException("Cannot split edge: no original split node");
                    copyFar = far == 0 ? copy.A : copy.B;
                }
                else
                {
                    if (e.VirtualEnd1[far] == (-1, -1) || e.VirtualEnd2[far] == (-1, -1)) throw new InvalidOperationException("Cannot split edge: corrupt V-end points");
                    if (firstSplitNode is not null) throw new InvalidOperationException("Cannot split edge: unexpected original split node");
                    (f.X, f.Y) = e.VirtualEnd2[far];
                    copyFar = graph.AddNode(e.VirtualEnd1[far]);
                    for (int k = 1; k < 4; k++)
                        if (f.Slots[k] == copy)
                        {
                            f.Slots[k] = null;
                            break;
                        }
                    TileGraph.Slot(copyFar, copy);
                    if (far == 0) copy.A = copyFar; else copy.B = copyFar;
                    copyFar.Link1 = f.Link1;
                    copyFar.Link2 = f.Link2;
                }
                if (f.Link1 != -1) Join(f, copyFar, f.Link1);
                if (f.Link2 != -1) Join(f, copyFar, f.Link2);
                bool inner = !(fx == 0 || fy == 0 || fx == graph.Width - 1 || fy == graph.Height - 1);
                f.OnVirtual = inner;
                copyFar.OnVirtual = inner;
                copyFar.Placed = f.Placed;
                copyFar.Height = f.Height;
                SplitPoints(e, copy, r, true, firstMid);
                if (loop) MoveToNew(e, copy, f, firstSplitNode!, true);
                if (plain.Contains(f)) plain.Add(copyFar);
                return;
            }
            SplitPoints(e, copy, r, false, (-1, -1));
            copy.Split = e.Split = true;
            node = far == 0 ? e.A : e.B;
            prevOrig = e;
            prevCopy = copy;
            if (nextEdge is null) return;
            e = nextEdge;
        }
    }

    /// <summary><c>0x142112480</c>: the node's logical link gains the node the split put next to it.</summary>
    void Join(GraphNode n, GraphNode created, int index)
    {
        var link = graph.Links[index];
        if (link.A == n)
        {
            if (link.A2 is not null) throw new InvalidOperationException("Too many logical links");
            link.A2 = created;
            return;
        }
        if (link.B != n) throw new InvalidOperationException("Bad logical link");
        if (link.B2 is not null) throw new InvalidOperationException("Too many logical links");
        link.B2 = created;
    }

    /// <summary><c>0x141d7bcf0</c>: a type has a direction unless it has n0 0 and one ground both sides and is not virtual,
    /// or is virtual with one sub-edge type twice that has none or whose two chirality bits differ.</summary>
    bool Directional(EdgeType t) => IsDirectional(t, edgeType);

    internal static bool IsDirectional(EdgeType t, Func<string, EdgeType> edgeType)
    {
        if (t.N0 != 0 || t.Ground1 != t.Ground2) return true;
        if (!t.Virtual) return false;
        if (t.SubEdges[0] != t.SubEdges[1]) return true;
        var sub = edgeType(t.SubEdges[0]!);
        return IsDirectional(sub, edgeType) && t.SubChirality[0] == t.SubChirality[1];
    }

    /// <summary><c>0x142112600</c>: the split edge and its copy take the virtual type's two sub-edges; which gets which,
    /// and their chirality, follow the end the split started from and the edge's chirality.</summary>
    void SubTypes(GraphEdge e, GraphEdge copy, int r)
    {
        var t = type(e);
        int flags = (t.SubChirality[0] ? 1 : 0) | (t.SubChirality[1] ? 2 : 0);
        if (!Directional(t))
        {
            // A symmetric type: the copy takes sub-edge 1, the edge sub-edge 0, each with a chirality set from the type's
            // bit for it against the end (0 when that sub-type is itself symmetric).
            copy.Type = t.SubEdges[1]!;
            copy.Chirality = (byte)(Directional(type(copy)) && ((flags >> 1) & 1) != (r != 0 ? 1 : 0) ? 1 : 0);
            e.Type = t.SubEdges[0]!;
            e.Chirality = (byte)(Directional(type(e)) && (flags & 1) != (r != 0 ? 1 : 0) ? 1 : 0);
            return;
        }
        if ((e.Chirality == 0) == (r != 0))
        {
            copy.Type = t.SubEdges[0]!;
            copy.Chirality ^= (byte)(flags & 1);
            e.Type = t.SubEdges[1]!;
            e.Chirality ^= (byte)((flags >> 1) & 1);
        }
        else
        {
            copy.Type = t.SubEdges[1]!;
            copy.Chirality ^= (byte)((flags >> 1) & 1);
            e.Type = t.SubEdges[0]!;
            e.Chirality ^= (byte)(flags & 1);
        }
    }

    /// <summary><c>0x142111170</c>: the two points w either side of q across the path p → q → r (on the bisector of
    /// the corner, or square to p → q when the path is straight or turns the other way), clamped inside the map.</summary>
    (( int X, int Y), (int X, int Y)) OffsetPoints((int X, int Y) p, (int X, int Y) q, (int X, int Y) r, int w)
    {
        if (p == q || q == r || p == r) throw new InvalidOperationException("Virtual points are the same!");
        if (w == 0) throw new InvalidOperationException("Virtual edge has zero width!");
        int px = p.X, py = p.Y, qx = q.X, qy = q.Y, rx = r.X, ry = r.Y;
        int dy1 = py - qy, dx1 = qx - px, dy2 = qy - ry, dx2 = rx - qx;
        int l1 = (int)FortuneVoronoi.IntSqrt((uint)(dx1 * dx1 + dy1 * dy1));
        int a = dy1 * w, b = dx1 * w, ox, oy;
        if (dx2 * dy1 - dy2 * dx1 < 2)
        {
            ox = a / -l1;
            oy = b / -l1;
        }
        else
        {
            int l2 = (int)FortuneVoronoi.IntSqrt((uint)(dx2 * dx2 + dy2 * dy2));
            int sx = dy2 * w / l2, sy = dx2 * w / l2;
            int x1 = a / l1 + px, y1 = py + b / l1, x2 = rx + sx, y2 = ry + sy;
            int ey = b / l1 - y1 + qy, ex2 = sx - x2 + qx, ex1 = a / l1 - x1 + qx, ey2 = sy - y2 + qy;
            int det = ey2 * ex1 - ex2 * ey;
            if (det < 2)
            {
                ox = a / -l1;
                oy = b / -l1;
            }
            else
            {
                int t = (y1 - y2) * ex2 - (x1 - x2) * ey2;
                ox = qx - ex1 * t / det - x1;
                oy = qy - t * ey / det - y1;
            }
        }
        int maxX = graph.Width * 23 - 2, maxY = graph.Height * 23 - 2;
        (int, int) Clamp(int x, int y) => (Math.Max(Math.Min(x, maxX), 0), Math.Max(Math.Min(y, maxY), 0));
        return (Clamp(qx + ox, qy + oy), Clamp(qx - ox, qy - oy));
    }

    /// <summary><c>0x142111f60</c>: from the split's start, every internal point of the edge and its copy moves apart:
    /// a width <c>Next(max − min + 1) + min</c> either side of the path through the points' middles (toward
    /// <paramref name="end"/> or the far end points' middle at the last one).</summary>
    void SplitPoints(GraphEdge e, GraphEdge copy, int r, bool last, (int X, int Y) end)
    {
        int n = e.Points.Count;
        if (n != copy.Points.Count || e.MinWidth != copy.MinWidth || e.MaxWidth != copy.MaxWidth)
            throw new InvalidOperationException("Could not split internal nodes!");
        int k = (n - 1) * r + 1, stop = (n + 1) * (1 - r), step = 1 - 2 * r;
        var finalMid = end;
        if (e.VirtualEndSet[1 - r])
        {
            var a = e.VirtualEnd1[1 - r];
            var b = e.VirtualEnd2[1 - r];
            finalMid = ((b.X + a.X) / 2, (b.Y + a.Y) / 2);
        }
        if (finalMid == (-1, -1) && last) throw new InvalidOperationException("Could not split internal nodes!");
        static (int X, int Y) At(GraphEdge g, int i) => i == 0 ? (g.A.X, g.A.Y) : i > g.Points.Count ? (g.B.X, g.B.Y) : g.Points[i - 1];
        while (k != stop)
        {
            var pe = At(e, k - step);
            var pc = At(copy, k - step);
            (int X, int Y) next;
            if (!last || k != stop - step)
            {
                var ne = At(e, k + step);
                var nc = At(copy, k + step);
                next = ((nc.X + ne.X) / 2, (nc.Y + ne.Y) / 2);
            }
            else next = finalMid;
            int w = (int)rng.Next((uint)(e.MaxWidth - e.MinWidth + 1)) + e.MinWidth;
            if (w == 0) throw new InvalidOperationException("Virtual edge in internal node splitter has zero width");
            var cur = At(e, k);
            var mid = ((pe.X + pc.X) / 2, (pe.Y + pc.Y) / 2);
            var (v1, v2) = OffsetPoints(mid, cur, next, w);
            if (v1.X < 0) throw new InvalidOperationException("Bad virtual points found in internal node splitter");
            int mx = mid.Item1, my = mid.Item2;
            if (0 < ((v2.X + v1.X) / 2 - mx) * (v1.Y - my) - ((v2.Y + v1.Y) / 2 - my) * (v1.X - mx)) (v1, v2) = (v2, v1);
            e.Points[k - 1] = v1;
            copy.Points[k - 1] = v2;
            k += step;
        }
    }

    /// <summary><c>0x142111590</c>: which of the two parallel edges at <paramref name="node"/> moves over to
    /// <paramref name="created"/>: the one whose last segment would cross the other's, else (when
    /// <paramref name="decide"/>) the one whose segment passes nearer.</summary>
    static void MoveToNew(GraphEdge e1, GraphEdge e2, GraphNode node, GraphNode created, bool decide)
    {
        var p1 = Next(node, e1);
        var p2 = Next(node, e2);
        (int X, int Y) nn = (node.X, node.Y), m = (created.X, created.Y);
        static bool Cross((int X, int Y) a1, (int X, int Y) a2, (int X, int Y) b1, (int X, int Y) b2)
        {
            int d1x = a2.X - a1.X, d1y = a2.Y - a1.Y, d2x = b2.X - b1.X, d2y = b2.Y - b1.Y;
            int den = d1x * d2y - d1y * d2x;
            if (den == 0) return false;
            int t = (a1.Y - b1.Y) * d2x - (a1.X - b1.X) * d2y, u = (a1.Y - b1.Y) * d1x - (a1.X - b1.X) * d1y;
            return den < 1 ? t < 1 && den <= t && u < 1 && den <= u : t >= 0 && t <= den && u >= 0 && u <= den;
        }
        var moved = e1;
        if (!Cross(p1, nn, p2, m))
        {
            moved = e2;
            if (decide && !Cross(p1, m, p2, nn))
            {
                var (n1, d1) = DistanceToSegment(m, p1, nn);
                var (n2, d2) = DistanceToSegment(m, p2, nn);
                moved = d1 * n2 <= d2 * n1 ? e2 : e1;
            }
        }
        int slot = Array.IndexOf(node.Slots, moved);
        if (slot >= 0) node.Slots[slot] = null;
        if (moved.A == node) moved.A = created; else moved.B = created;
        TileGraph.Slot(created, moved);
    }

    /// <summary><c>0x140df8cc0</c>: the squared distance from p to the segment a–b as a fraction.</summary>
    internal static (long Num, long Den) DistanceToSegment((int X, int Y) p, (int X, int Y) a, (int X, int Y) b)
    {
        if (a.X != b.X)
        {
            long dx = b.X - a.X, dy = b.Y - a.Y, len = dx * dx + dy * dy;
            long t = (p.X - a.X) * dx + (p.Y - a.Y) * dy;
            if (t < 1) return (Sq(p.X - a.X) + Sq(p.Y - a.Y), 1);
            if (t < len)
            {
                long cross = dy * (p.X - a.X) - dx * (p.Y - a.Y);
                return (cross * cross, len);
            }
            return (Sq(p.X - b.X) + Sq(p.Y - b.Y), 1);
        }
        var (lo, hi) = a.Y <= b.Y ? (a, b) : (b, a);
        if (p.Y < lo.Y) return (Sq(p.X - lo.X) + Sq(p.Y - lo.Y), 1);
        if (p.Y < hi.Y) return (Sq(p.X - lo.X), 1);
        return (Sq(p.X - hi.X) + Sq(p.Y - hi.Y), 1);
    }

    static long Sq(long v) => v * v;
}
