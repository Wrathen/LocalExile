namespace GameServer.Data.Terrain.Generation;

/// <summary>What a tile slot asks for and what a tile offers (the client's 0x4e-byte key, built by <c>0x141d813c0</c>
/// from a .tdt and by the room parser from an .arm slot): per edge (top, right, bottom, left) the edge type (.et path)
/// and two edge points; the size in tiles; per corner ((0,0), (W,0), (W,H), (0,H)) the ground type (.gt path) and a
/// height. A tile fits a slot when its key turned by an orientation equals the slot's key turned by the room's.</summary>
public sealed class TileKey : IEquatable<TileKey>
{
    public readonly string?[] Edges = new string?[4];
    public readonly string?[] Grounds = new string?[4];
    public readonly sbyte[] Heights = new sbyte[4];
    public readonly byte[] PointA = new byte[4];
    public readonly byte[] PointB = new byte[4];
    public byte Width, Height;

    // 0x142fbe3b8 (edges) and 0x142fbe3d8 (corners): for orientation code c, new index i takes old index T[c][i].
    static readonly byte[] EdgeFrom = [0, 1, 2, 3, 3, 0, 1, 2, 2, 3, 0, 1, 1, 2, 3, 0, 0, 3, 2, 1, 1, 0, 3, 2, 2, 1, 0, 3, 3, 2, 1, 0];
    static readonly byte[] CornerFrom = [0, 1, 2, 3, 3, 0, 1, 2, 2, 3, 0, 1, 1, 2, 3, 0, 1, 0, 3, 2, 2, 1, 0, 3, 3, 2, 1, 0, 0, 3, 2, 1];

    public TileKey Clone()
    {
        var k = new TileKey { Width = Width, Height = Height };
        Edges.CopyTo(k.Edges, 0);
        Grounds.CopyTo(k.Grounds, 0);
        Heights.CopyTo(k.Heights, 0);
        PointA.CopyTo(k.PointA, 0);
        PointB.CopyTo(k.PointB, 0);
        return k;
    }

    /// <summary><c>0x141d80480</c>: the key turned by orientation code 0..7 (odd codes swap the size; codes 4..7 mirror
    /// the edge points, a point equal to the edge's length (3 per tile) meaning none).</summary>
    public TileKey Rotate(int code)
    {
        if (code is 0 or >= 8) return Clone();
        var k = new TileKey { Width = Width, Height = Height };
        if ((code & 1) != 0) (k.Width, k.Height) = (Height, Width);
        for (int i = 0; i < 4; i++)
        {
            int e = EdgeFrom[code * 4 + i], c = CornerFrom[code * 4 + i];
            k.Edges[i] = Edges[e];
            k.PointA[i] = PointA[e];
            k.PointB[i] = PointB[e];
            k.Grounds[i] = Grounds[c];
            k.Heights[i] = Heights[c];
        }
        if (code >= 4)
            for (int i = 0; i < 4; i++)
            {
                int length = ((i & 1) == 0 ? k.Width : k.Height) * 3;
                if (k.PointA[i] == length) continue;
                byte a = (byte)(length - k.PointA[i] - 1);
                k.PointA[i] = a;
                if (k.PointB[i] != length)
                {
                    k.PointA[i] = (byte)(length - k.PointB[i] - 1);
                    k.PointB[i] = a;
                }
            }
        return k;
    }

    public bool Equals(TileKey? o) => o is not null && Width == o.Width && Height == o.Height &&
        Edges.AsSpan().SequenceEqual(o.Edges) && Grounds.AsSpan().SequenceEqual(o.Grounds) &&
        Heights.AsSpan().SequenceEqual(o.Heights) && PointA.AsSpan().SequenceEqual(o.PointA) && PointB.AsSpan().SequenceEqual(o.PointB);

    public override bool Equals(object? obj) => Equals(obj as TileKey);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Width); h.Add(Height);
        for (int i = 0; i < 4; i++)
        {
            h.Add(Edges[i]); h.Add(Grounds[i]); h.Add(Heights[i]); h.Add(PointA[i]); h.Add(PointB[i]);
        }
        return h.ToHashCode();
    }

    public override string ToString() =>
        $"{Width}x{Height} E[{string.Join(",", Edges.Select(Short))}] G[{string.Join(",", Grounds.Select(Short))}] " +
        $"H[{string.Join(",", Heights)}] A[{string.Join(",", PointA)}] B[{string.Join(",", PointB)}]";

    static string Short(string? path) => path is null ? "-" : Path.GetFileNameWithoutExtension(path);
}

/// <summary>Orientation codes (the client's placement codes, <c>0x142fa04b0</c>): 0..3 turn by quarter turns, 4..7
/// mirror. <see cref="Map"/> places cell (x, y) of a w × h block.</summary>
public static class Orientation
{
    /// <summary>Per code, three bytes: x and y swapped, x mirrored, y mirrored (as <see cref="Map"/> reads them).</summary>
    public static readonly byte[] Table = [0, 1, 1, 1, 0, 1, 0, 0, 0, 1, 1, 0, 0, 0, 1, 1, 0, 0, 0, 1, 0, 1, 1, 1, 0, 1, 1];

    /// <summary>Where cell (x, y) of a w × h block lands, relative to the turned block's origin.</summary>
    public static (int X, int Y) Map(int code, int x, int y, int w, int h)
    {
        int c = Math.Min(code, 8) * 3;
        Span<int> a = [w - x - 1, x, h - y - 1, y];
        return (a[Table[c + 1] + 2 * Table[c]], a[(Table[c] == 0 ? 2 : 0) + Table[c + 2]]);
    }

    /// <summary>Where vertex (u, v) of a w × h block lands, relative to the turned block's origin (<c>0x141d833e0</c>).</summary>
    public static (int X, int Y) MapVertex(int code, int u, int v, int w, int h)
    {
        int c = Math.Min(code, 8) * 3;
        Span<int> a = [w - u, u, h - v, v];
        return (a[Table[c + 1] + 2 * Table[c]], a[(Table[c] == 0 ? 2 : 0) + Table[c + 2]]);
    }

    /// <summary>The size of a w × h block turned by a code.</summary>
    public static (int W, int H) Size(int code, int w, int h) => (code & 1) != 0 && code < 8 ? (h, w) : (w, h);

    /// <summary>A graph node's rotation word (I, R90, R180, R270, F…) as a code.</summary>
    public static int Parse(string word) => word switch
    {
        "I" or "R0" => 0, "R90" => 1, "R180" => 2, "R270" => 3,
        "F" or "FI" or "F0" or "FR0" => 4, "FR90" => 5, "FR180" => 6, "FR270" => 7,
        _ => 8,
    };

    // 0x142025440 / 0x142026100: the orientation that mirrors o across a vertical seam [0] (left/right neighbour) or a
    // horizontal one [1] (up/down neighbour).
    static readonly byte[] Mirror = [4, 6, 7, 5, 6, 4, 5, 7, 0, 2, 3, 1, 2, 0, 1, 3, 8, 8];

    public static int MirrorAcross(int o, int direction) => Mirror[Math.Min(o, 8) * 2 + (~direction & 1)];

    /// <summary>Neighbour directions as the seam passes walk them: up, right, down, left.</summary>
    public static readonly (int X, int Y)[] Directions = [(0, -1), (1, 0), (0, 1), (-1, 0)];
}
