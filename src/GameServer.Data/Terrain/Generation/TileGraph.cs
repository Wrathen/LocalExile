namespace GameServer.Data.Terrain.Generation;

/// <summary>A node of a tile graph as the generator holds it (the client's 0x98-byte node, built from the .tgr record by
/// <c>0x142126b40</c>): position in cells (+0x4c), tag (+0x20), orientation (+0x40), descriptions (+8), exist % (+0x44),
/// height (+0x48), priority (+0x42), random offset rectangle (+0x78), x / y locked (+0x8b / +0x8c) and up to four edges
/// (+0x58), in the order the file lists them.</summary>
public sealed class GraphNode
{
    public int X, Y;
    public string Tag = "";
    public byte Orientation = 8;
    public List<string> Descriptions = [];
    public int Exist = 100;
    public int Height;
    public sbyte Priority;
    public byte Deletion;
    public bool LockOrientation;
    public int OffsetX1, OffsetY1, OffsetX2, OffsetY2;
    public bool LockX, LockY;
    /// <summary>A dungeon graph's node branching (version &gt; 21).</summary>
    public int Branching;
    /// <summary>A room or tile took the node (+0x88). For a dungeon graph (.dgr): the node is void ("V"), it takes no
    /// room.</summary>
    public bool Placed;
    /// <summary>One of the node's edges is virtual (+0x89, <c>0x14211f100</c>; cleared or set again by the split).</summary>
    public bool OnVirtual;
    /// <summary>Emptied by the link merge, to be removed (+0x8a).</summary>
    public bool Dead;
    /// <summary>Indexes of the node's logical links in <see cref="TileGraph.Links"/> (+0x90 / +0x94, −1 = none).</summary>
    public int Link1 = -1, Link2 = -1;
    public readonly GraphEdge?[] Slots = new GraphEdge?[4];

    public int Degree => Slots.Count(s => s is not null);

    public override string ToString() => $"node ({X}, {Y}) '{Tag}'";
}

/// <summary>An edge of a tile graph (<c>0x142126c40</c>): its two nodes (+0x20 / +0x28), internal points (+8), exist %
/// (+0x30), chirality (+0x41), priority (+0x40), edge type (.et, +0x38) and widths (+0x80 / +0x84).</summary>
public sealed class GraphEdge
{
    public GraphNode A = null!, B = null!;
    public List<(int X, int Y)> Points = [];
    public int Exist = 100;
    public byte Chirality;
    public byte Priority;
    public string Type = "";
    public int MinWidth, MaxWidth;
    /// <summary>Cell offsets of the edge's ends (+0x70 / +0x78), set where a room or tile takes the edge.</summary>
    public (int X, int Y) StartOffset, EndOffset;
    /// <summary>Per end (0 = A, 1 = B), a virtual edge's two end points where a room takes it (+0x48 set, +0x4c / +0x54).</summary>
    public bool[] VirtualEndSet = new bool[2];
    public (int X, int Y)[] VirtualEnd1 = [(-1, -1), (-1, -1)], VirtualEnd2 = [(-1, -1), (-1, -1)];
    /// <summary>+0x4a (must be clear for the split), +0x6c (tiled, or part of a tiled chain) and +0x6d (the edge was
    /// split).</summary>
    public bool Flag4a, Done, Split;
    /// <summary>A dungeon graph's (.dgr) edge fields (<c>0x142a0c0a0</c>).</summary>
    public DungeonEdge? Dungeon;

    public override string ToString() => $"edge {A} -> {B} ({Path.GetFileNameWithoutExtension(Type)})";

    /// <summary><c>0x141d942e0</c>: a copy with the same nodes, points, type and fields.</summary>
    public GraphEdge Copy()
    {
        var c = (GraphEdge)MemberwiseClone();
        c.Points = [.. Points];
        c.VirtualEndSet = (bool[])VirtualEndSet.Clone();
        c.VirtualEnd1 = ((int, int)[])VirtualEnd1.Clone();
        c.VirtualEnd2 = ((int, int)[])VirtualEnd2.Clone();
        return c;
    }
}

/// <summary>The fields a dungeon graph's (.dgr) edge record adds after the type (<c>0x142a0c0a0</c>), with their
/// offsets in the client's edge: branching chances (+0x48, +0x4c), depth limit (+0x50, version &gt; 20), branch edge
/// type (+0x68, version &gt; 17), extra door % (+0x5c) and type (+0x70, version &gt; 19; default rooms.et), fuzzy %
/// (+0x58, how much the room path search follows each room's random number), impenetrability (+0x54), impenetrable
/// ('I', +0x60), void ('V', +0x61: the edge only reserves its cells) and accessible (+0x62).</summary>
public sealed class DungeonEdge
{
    public int BranchChance1, BranchChance2, BranchDepth;
    public string BranchType = "";
    public int ExtraDoor;
    public string ExtraDoorType = "Metadata/Terrain/Dungeon/rooms.et";
    /// <summary>+0x58 fuzzy; +0x5c, the last number before I/P: the path search bias of the rooms the edge's path takes
    /// (<c>0x1420facb0</c>) and the edge lists' key (<c>0x1420014a0</c>). <see cref="ExtraDoor"/> (+0x54) is their
    /// extra door chance (room +0x10).</summary>
    public int Fuzzy, Bias;
    public bool Impenetrable, Void, Accessible;
}

/// <summary>A logical link (the generator's 0x20-byte entries): two node ends that belong together, and the nodes
/// the split adds next to them (+0x10 / +0x18, <c>0x142112480</c>).</summary>
public sealed class LogicalLink(GraphNode a, GraphNode b)
{
    public GraphNode A = a, B = b;
    public GraphNode? A2, B2;
}

/// <summary>A tile graph (.tgr): size in tiles, master file, nodes and edges in file order (parsers <c>0x142a157c0</c>,
/// <c>0x142bb2a10</c> header, <c>0x142bb34c0</c> nodes, <c>0x142bb3f20</c> edges).</summary>
public sealed class TileGraph
{
    public int Version;
    public int Width, Height;
    public string MasterFile = "";
    /// <summary>The three ground lines after the counts (<c>0x142bb2a10</c>: "top", "default", "bottom" ground type).</summary>
    public string TopGround = "", DefaultGround = "", BottomGround = "";
    /// <summary>A dungeon graph (.dgr): its "Default%:" line follows the grounds.</summary>
    public bool IsDungeon;
    public int DefaultBranching, DefaultExtraDoor, DefaultFuzzy;
    public bool ExtraDoorsIntoImpenetrable;
    public List<GraphNode> Nodes = [];
    public List<GraphEdge> Edges = [];
    /// <summary>Logical links between the ends of a split node's edges (the generator's list at +0, 0x20 bytes each,
    /// <c>0x14211dbc0</c>), merged back by <c>0x1421148c0</c>.</summary>
    public List<LogicalLink> Links = [];

    /// <summary><c>0x141d94560</c>: a new node at a position, appended.</summary>
    public GraphNode AddNode((int X, int Y) at)
    {
        var n = new GraphNode { X = at.X, Y = at.Y, Exist = 100 };
        Nodes.Add(n);
        return n;
    }

    /// <summary><c>0x141d94390</c>: an edge between two nodes, slotted into both, appended.</summary>
    public void AddEdge(GraphNode a, GraphNode b, GraphEdge e)
    {
        e.A = a;
        e.B = b;
        Slot(a, e);
        Slot(b, e);
        Edges.Add(e);
    }

    /// <summary>Size in cells (+0x50 / +0x54: tiles × 23).</summary>
    public int CellWidth => Width * 23;
    public int CellHeight => Height * 23;

    public static TileGraph Parse(string text)
    {
        var tokens = new List<TerrainText.Token>();
        foreach (var line in TerrainText.Lines(text)) tokens.AddRange(TerrainText.Tokenize(line));
        int at = 0;
        TerrainText.Token Next() => at < tokens.Count ? tokens[at++] : throw new InvalidDataException("graph ends early");
        int Int() => Next().Int;
        void Expect(string word)
        {
            var t = Next();
            if (t.Quoted || t.Text != word) throw new InvalidDataException($"expected '{word}', got '{t.Text}'");
        }

        var g = new TileGraph();
        Expect("version");
        g.Version = Int();
        Expect("Size:");
        g.Width = Int();
        g.Height = Int();
        if (tokens[at].Text == "Resolution:") { at++; Int(); Int(); }
        Expect("MasterFile:");
        g.MasterFile = Next().Text;
        Expect("Nodes:");
        int nodes = Int();
        Expect("Edges:");
        int edges = Int();
        if (tokens[at].Text == "EnvironmentOverride:") { at++; Next(); }
        g.TopGround = Next().Text;
        g.DefaultGround = Next().Text;
        g.BottomGround = Next().Text;
        if (g.Version < 0x13) throw new NotSupportedException($"graph version {g.Version} scales coordinates (not ported)");
        // A dungeon graph (.dgr, 0x142a0c0a0): "Default%:" branching, extra door and fuzzy percents (and, after version
        // 22, whether extra doors may open into impenetrable rooms).
        if (at < tokens.Count && tokens[at].Text == "Default%:")
        {
            at++;
            g.IsDungeon = true;
            g.DefaultBranching = Int();
            g.DefaultExtraDoor = Int();
            g.DefaultFuzzy = Int();
            if (g.Version > 0x16) g.ExtraDoorsIntoImpenetrable = Int() != 0;
        }
        bool Word(string yes, string no, string what)
        {
            var t = Next();
            return t.Text == yes ? true : t.Text == no ? false : throw new InvalidDataException($"Invalid {what} value read: {t.Text}");
        }

        var slots = new List<int[]>();
        for (int i = 0; i < nodes; i++)
        {
            var n = new GraphNode { X = Int(), Y = Int() };
            int count = Int();
            var s = new int[4];
            for (int k = 0; k < count; k++) s[k] = Int() + 1;
            slots.Add(s);
            n.Tag = Next().Text;
            n.Orientation = ParseOrientation(Next().Text);
            int descriptions = Int();
            for (int k = 0; k < descriptions; k++) n.Descriptions.Add(Next().Text);
            n.Exist = Int();
            n.Height = Int();
            if (g.Version > 0x15) { n.Priority = (sbyte)Int(); n.Deletion = (byte)Int(); }
            if (g.Version > 0x17) n.LockOrientation = Int() != 0;
            if (g.Version > 0x18) { n.OffsetX1 = Int(); n.OffsetY1 = Int(); n.OffsetX2 = Int(); n.OffsetY2 = Int(); }
            if (g.IsDungeon)
            {
                n.Placed = Word("V", "N", "voidness");
                if (g.Version > 0x15) n.Branching = Int();
            }
            else
            {
                n.LockX = Int() != 0;
                n.LockY = Int() != 0;
            }
            g.Nodes.Add(n);
        }
        for (int i = 0; i < edges; i++)
        {
            var e = new GraphEdge { A = g.Nodes[Int()], B = g.Nodes[Int()] };
            int points = Int();
            for (int k = 0; k < points; k++) e.Points.Add((Int(), Int()));
            e.Exist = Int();
            if (g.Version >= 0xf)
            {
                e.Chirality = (byte)Int();
                if (g.Version > 0x15) e.Priority = (byte)Int();
            }
            e.Type = Next().Text;
            if (g.IsDungeon)
            {
                var d = e.Dungeon = new DungeonEdge { BranchChance2 = Int() };
                d.BranchChance1 = d.BranchChance2 != 0 ? 100 : 0;
                if (g.Version >= 0x10)
                {
                    d.BranchChance1 = Int();
                    if (g.Version > 0x14) d.BranchDepth = Int();
                    if (g.Version > 0x11) d.BranchType = Next().Text;
                }
                d.ExtraDoor = Int();
                if (g.Version < 0x11) d.ExtraDoor = 0;
                // An empty type ("") is read over the default; the client then holds rooms.et for it (taken from
                // 2_8_5 seed 1: exit_1.arm's extra door into a rooms.et exit).
                else if (g.Version > 0x13 && Next().Text is { Length: > 0 } extraType) d.ExtraDoorType = extraType;
                d.Fuzzy = Int();
                d.Bias = Int();
                d.Impenetrable = Word("I", "P", "impenetrable");
                d.Void = Word("V", "N", "void");
                d.Accessible = Int() != 0;
            }
            else
            {
                e.MinWidth = Int();
                e.MaxWidth = Int();
                if (g.Version < 0xf) Next();
            }
            g.Edges.Add(e);
        }
        // 0x142126550: each node's edge slots in the order its record lists them.
        for (int i = 0; i < nodes; i++)
            for (int k = 0; k < 4; k++)
                g.Nodes[i].Slots[k] = slots[i][k] == 0 ? null : g.Edges[slots[i][k] - 1];
        return g;
    }

    /// <summary><c>0x14299d980</c>: an orientation word; "(any)" and unknown words are 8 (any).</summary>
    static byte ParseOrientation(string word) => (byte)Orientation.Parse(word);

    /// <summary><c>0x141f4f130</c>: takes an edge out of its nodes' slots and the edge list.</summary>
    public void RemoveEdge(GraphEdge e)
    {
        Unslot(e.A, e);
        Unslot(e.B, e);
        Edges.Remove(e);
    }

    static void Unslot(GraphNode n, GraphEdge e)
    {
        int i = Array.IndexOf(n.Slots, e);
        if (i >= 0) n.Slots[i] = null;
    }

    internal static void Slot(GraphNode n, GraphEdge e)
    {
        int i = Array.IndexOf(n.Slots, null);
        if (i >= 0) n.Slots[i] = e;
    }

    /// <summary><c>0x142036640</c>: removes a node. A node between two edges of one type whose other ends share its
    /// height is merged away (<c>0x1420371a0</c>: the first edge takes over the second's far node, the node's position
    /// and the second edge's points). A tagged node with two or more edges stays as a plain node: tag and descriptions
    /// cleared, any orientation. Otherwise each of its edges moves its end to its nearest internal point (a new node
    /// there) or, without points, goes, and the node goes.</summary>
    public void RemoveNode(GraphNode node)
    {
        var edges = node.Slots.Where(s => s is not null).Cast<GraphEdge>().ToList();
        if (edges.Count == 2 && edges[0].Type == edges[1].Type)
        {
            if (edges[0] == edges[1]) throw new NotSupportedException($"removing {node} on a loop edge (0x142036640) is not ported");
            var other0 = edges[0].A == node ? edges[0].B : edges[0].A;
            var other1 = edges[1].A == node ? edges[1].B : edges[1].A;
            if (other0.Height == node.Height && other1.Height == node.Height)
            {
                MergeThrough(node, edges[0], edges[1]);
                return;
            }
        }
        if (edges.Count >= 2 && node.Tag.Length != 0)
        {
            node.Tag = "";
            node.Descriptions.Clear();
            node.Orientation = 8;
            return;
        }
        var gone = new List<GraphEdge>();
        foreach (var e in Edges)
        {
            if (e.A == node)
            {
                if (e.Points.Count == 0) { gone.Add(e); continue; }
                e.A = AddNode(e.Points[0]);
                Slot(e.A, e);
                e.Points.RemoveAt(0);
            }
            else if (e.B == node)
            {
                if (e.Points.Count == 0) { gone.Add(e); continue; }
                e.B = AddNode(e.Points[^1]);
                Slot(e.B, e);
                e.Points.RemoveAt(e.Points.Count - 1);
            }
        }
        foreach (var e in gone) RemoveEdge(e);
        Nodes.Remove(node);
    }

    void MergeThrough(GraphNode node, GraphEdge first, GraphEdge second)
    {
        bool nodeAtFirstB = first.A != node;
        bool secondFromNode = second.A == node;
        var far = secondFromNode ? second.B : second.A;
        if (nodeAtFirstB) first.B = far; else first.A = far;
        Unslot(far, second);
        Slot(far, first);
        Unslot(node, first);
        Unslot(node, second);
        // The node's position goes in at the merged end, then the second edge's points, turned to run the same way.
        if (nodeAtFirstB)
        {
            first.Points.Add((node.X, node.Y));
            var pts = secondFromNode ? second.Points : Enumerable.Reverse(second.Points).ToList();
            first.Points.AddRange(pts);
        }
        else
        {
            var pts = secondFromNode ? Enumerable.Reverse(second.Points).ToList() : second.Points;
            first.Points.InsertRange(0, pts);
            first.Points.Insert(pts.Count, (node.X, node.Y));
        }
        Unslot(second.A, second);
        Unslot(second.B, second);
        Edges.Remove(second);
        Nodes.Remove(node);
    }
}
