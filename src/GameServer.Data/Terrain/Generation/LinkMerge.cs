namespace GameServer.Data.Terrain.Generation;

/// <summary><c>0x1421148c0</c>: the logical links are resolved. A link's live end nodes that lie on one spot are
/// joined (<c>0x142113ec0</c> pairs them, <c>0x142114aa0</c> moves one node's edge onto the other); a link with no
/// such pair reconnects its lone end to the nearer of the other end's two split nodes (<c>0x142114270</c>). Nodes
/// left with more than two edges join the plain list (<c>0x142114de0</c>), emptied nodes are removed
/// (<c>0x142036640</c>).</summary>
sealed class LinkMerge(TileGraph graph, List<GraphNode> plain)
{
    readonly List<GraphNode> _remove = [];

    public void Run()
    {
        foreach (var link in graph.Links) Resolve(link);
        foreach (var link in graph.Links)
            foreach (var n in new[] { link.A, link.B, link.A2, link.B2 })
                if (n is not null && !n.Dead && n.Degree > 2 && !plain.Contains(n)) plain.Add(n);
        graph.Links.Clear();
        foreach (var n in graph.Nodes) n.Link1 = n.Link2 = -1;
        foreach (var n in _remove) graph.RemoveNode(n);
    }

    void Resolve(LogicalLink link)
    {
        var pairs = Pairs(link);
        if (pairs.Count == 0)
        {
            Reconnect(link);
            return;
        }
        foreach (var (n1, n2) in pairs)
        {
            var (drop, keep) = n1.Degree == 1 ? (n1, n2) : (n2, n1);
            var e = drop.Slots.First(s => s is not null)!;
            foreach (int index in new[] { n1.Link1, n1.Link2, n2.Link1, n2.Link2 })
            {
                if (index == -1) continue;
                var l = graph.Links[index];
                if (l.A == drop) l.A = keep;
                if (l.B == drop) l.B = keep;
                if (l.A2 == drop) l.A2 = keep;
                if (l.B2 == drop) l.B2 = keep;
            }
            Move(e, drop, keep);
            if (drop.Degree == 0)
            {
                drop.Dead = true;
                if (!_remove.Contains(drop)) _remove.Add(drop);
            }
        }
    }

    // Moves edge e's end at `from` to `to`.
    static void Move(GraphEdge e, GraphNode from, GraphNode to)
    {
        bool atA = e.A == from;
        int slot = Array.IndexOf(from.Slots, e);
        if (slot >= 0) from.Slots[slot] = null;
        if (atA) e.A = to; else e.B = to;
        TileGraph.Slot(to, e);
    }

    // 0x142113ec0.
    static List<(GraphNode, GraphNode)> Pairs(LogicalLink link)
    {
        if (link.A is null || link.B is null || link.A == link.A2 || link.B == link.B2)
            throw new InvalidOperationException("Bad logical link. Cannot find node pairs");
        var live = new List<GraphNode>();
        if (!link.A.Dead) live.Add(link.A);
        if (!link.B.Dead) live.Add(link.B);
        if (link.A2 is { Dead: false } a2) live.Add(a2);
        if (link.B2 is { Dead: false } b2) live.Add(b2);
        var pairs = new List<(GraphNode, GraphNode)>();
        int count = live.Count;
        for (int i = 0; i < count - 1; i++)
            for (int j = i + 1; j < count; j++)
            {
                if (live[i].X != live[j].X || live[i].Y != live[j].Y) continue;
                pairs.Add((live[i], live[j]));
                if (count != 4) continue;
                int u = 0;
                while (u == i || u == j) u++;
                int v = u;
                do v++; while (v == i || v == j);
                if (u > 3 || v > 3) throw new InvalidOperationException("Side indices greater than three");
                if (live[u] == live[v]) pairs.Add((live[u], live[v]));
            }
        return pairs;
    }

    // 0x142114270.
    void Reconnect(LogicalLink link)
    {
        GraphNode lone, p, p2;
        if (link.A2 is null)
        {
            if (link.B2 is null) throw new InvalidOperationException("No split node(2) found during impure virtual link resolution");
            (lone, p, p2) = (link.A, link.B, link.B2);
        }
        else
        {
            if (link.B2 is not null) throw new InvalidOperationException("Unexpected split node(2) found during impure virtual link resolution");
            (lone, p, p2) = (link.B, link.A, link.A2);
        }
        bool pickP;
        int degree = lone.Degree;
        if (degree != 1)
        {
            if (degree > 2) return;
            pickP = (uint)(Sq(p.X - lone.X) + Sq(p.Y - lone.Y)) < (uint)(Sq(p2.X - lone.X) + Sq(p2.Y - lone.Y));
        }
        else
        {
            var e = lone.Slots.First(s => s is not null)!;
            if (p.Dead && p2.Dead) return;
            var q = lone == e.A ? (e.Points.Count == 0 ? (e.B.X, e.B.Y) : e.Points[0]) : (e.Points.Count == 0 ? (e.A.X, e.A.Y) : e.Points[^1]);
            if (!p2.Dead)
            {
                pickP = false;
                if (!p.Dead)
                {
                    var (n1, d1) = EdgeSplitting.DistanceToSegment((p.X, p.Y), q, (lone.X, lone.Y));
                    var (n2, d2) = EdgeSplitting.DistanceToSegment((p2.X, p2.Y), q, (lone.X, lone.Y));
                    pickP = n1 * d2 < n2 * d1;
                }
            }
            else pickP = true;
        }
        var chosen = pickP ? p : p2;
        for (int k = 0; k < 4; k++)
            if (chosen.Slots[k] is { } f) Move(f, chosen, lone);
        (lone.X, lone.Y) = (chosen.X, chosen.Y);
        if (chosen.Degree != 0) throw new InvalidOperationException("Reconnecting node still has edges");
        chosen.Dead = true;
        lone.OnVirtual = false;
        if (!_remove.Contains(chosen)) _remove.Add(chosen);
    }

    static int Sq(int v) => v * v;
}
