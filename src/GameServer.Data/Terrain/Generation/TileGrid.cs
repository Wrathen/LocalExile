namespace GameServer.Data.Terrain.Generation;

/// <summary>One grid entry (the client's 0x38-byte entries of the tile map, <c>0x141d833e0</c>): the tile covering it,
/// the tile's orientation and height offset, and the entry's offset inside the tile (0, 0 at the tile's origin).</summary>
public readonly record struct TileEntry(TileDef Tile, byte Orientation, short HeightOffset, byte OffsetX, byte OffsetY)
{
    public bool IsOrigin => OffsetX == 0 && OffsetY == 0;
}

/// <summary>One vertex of the tile map (the client's 3-byte corners at map +0x50: ground index into the map's ground
/// list +0x68, height, flags). Flag 1: a ground was written; 2: a height was written; 4: written before the ground
/// flood fill (<c>0x142116600</c> sets it from flag 1 and clears it after the edge points).</summary>
public struct TileCorner
{
    public string? Ground;
    public sbyte Height;
    public byte Flags;
}

/// <summary>The generated tile map: Width × Height entries (one per tile slot of 23 × 23 cells) and their
/// (Width + 1) × (Height + 1) vertices.</summary>
public sealed class TileGrid(int width, int height)
{
    readonly TileEntry?[] _entries = new TileEntry?[width * height];
    readonly TileCorner[] _corners = new TileCorner[(width + 1) * (height + 1)];

    public int Width { get; } = width;
    public int Height { get; } = height;

    /// <summary>Map +0x10: ground writes over written vertices are checked (<c>0x141d85640</c>). Room tilising
    /// (<c>0x141fea780</c>) and the edge points (<c>0x141feed20</c>) turn it off while they run.</summary>
    public bool CheckGrounds = true;

    /// <summary>View +0x20: a tile went in with a height offset (<c>0x141ffb870</c>).</summary>
    public bool HasHeights { get; private set; }

    /// <summary>The ground flags (.gt line 2 numbers as bits) by ground path; null for the null ground.</summary>
    public Func<string?, int> GroundFlags = _ => 0;

    /// <summary><c>0x141ffb870</c>'s test: the tile keeps only its turn, not its mirror (<see cref="TerrainCache.Unmirrored"/>).</summary>
    public Func<TileDef, bool> Unmirrored = _ => false;

    public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public TileEntry? this[int x, int y] => Inside(x, y) ? _entries[y * Width + x] : null;

    public bool InsideCorners(int x, int y) => x >= 0 && y >= 0 && x <= Width && y <= Height;

    public ref TileCorner Corner(int x, int y)
    {
        if (!InsideCorners(x, y)) throw new InvalidOperationException($"vertex ({x}, {y}) is outside the tile map");
        return ref _corners[y * (Width + 1) + x];
    }

    /// <summary><c>0x141d833e0</c>: writes every entry the tile covers under its orientation, then every vertex's
    /// ground (<see cref="SetGround"/>, or <see cref="ForceGround"/> when <paramref name="force"/>) and, where the
    /// vertex ground has no ground or one with flag 1, the vertex height plus the height offset.</summary>
    public void Place(TileDef tile, int orientation, int x, int y, short heightOffset, bool force = false)
    {
        HasHeights |= heightOffset != 0;
        // 0x141ffb870, before the map's place (0x141d833e0): such a tile goes in at its orientation & 3.
        if (Unmirrored(tile)) orientation &= 3;
        for (int v = 0; v < tile.Height; v++)
            for (int u = 0; u < tile.Width; u++)
            {
                var (dx, dy) = Orientation.Map(orientation, u, v, tile.Width, tile.Height);
                if (!Inside(x + dx, y + dy)) throw new InvalidOperationException($"tile {tile.Path} placed out of bounds at ({x + dx}, {y + dy})");
                _entries[(y + dy) * Width + x + dx] = new TileEntry(tile, (byte)orientation, heightOffset, (byte)dx, (byte)dy);
            }
        for (int v = 0; v <= tile.Height; v++)
            for (int u = 0; u <= tile.Width; u++)
            {
                var (dx, dy) = Orientation.MapVertex(orientation, u, v, tile.Width, tile.Height);
                string? g = tile.VertexGround(u, v);
                ref var c = ref Corner(x + dx, y + dy);
                string? before = c.Ground;
                if (force) ForceGround(x + dx, y + dy, g); else SetGround(x + dx, y + dy, g);
                // The height goes in when the tile's vertex has no ground or a flag-1 ground, or the vertex had none.
                if (g is null || (GroundFlags(g) & 1) != 0 || before is null || (GroundFlags(before) & 1) != 0)
                {
                    // 0x141d7ff50 with code 0: the file's vertex height (0 without heights).
                    c.Height = (sbyte)(tile.VertexHeight(u, v) + heightOffset);
                    c.Flags |= 2;
                }
            }
    }

    /// <summary>View +0x20 is also set by the height levels (<c>0x142118470</c>, <c>0x142118790</c>).</summary>
    public void MarkHeights(bool any) => HasHeights |= any;

    /// <summary><c>0x141d82d00</c>: the tile covering entry (x, y) (an empty entry counts as 1 × 1) takes a
    /// height offset: every entry it covers, then every vertex of it: the offset plus the tile's own vertex height.</summary>
    public void SetHeight(int x, int y, short height)
    {
        var e = this[x, y];
        if (e is { } o) (x, y) = (x - o.OffsetX, y - o.OffsetY);
        var (w, h) = e is { } t ? Orientation.Size(t.Orientation, t.Tile.Width, t.Tile.Height) : (1, 1);
        if (e is { } tile)
            for (int v = 0; v < h; v++)
                for (int u = 0; u < w; u++)
                    if (_entries[(y + v) * Width + x + u] is { } c) _entries[(y + v) * Width + x + u] = c with { HeightOffset = height };
        for (int v = 0; v <= h; v++)
            for (int u = 0; u <= w; u++)
                Corner(x + u, y + v).Height = (sbyte)(height + (e is { } t2 ? t2.Tile.VertexHeightAt(t2.Orientation, u, v) : 0));
    }

    /// <summary><c>0x141d83060</c>: the tile whose origin is entry (x, y) has its height offset multiplied by
    /// <paramref name="storey"/>; every vertex of it becomes the new offset plus the tile's own vertex height times
    /// <paramref name="storey"/> (a byte, wrapping).</summary>
    public void ScaleHeight(int x, int y, int storey)
    {
        var e = this[x, y];
        var (w, h) = e is { } t ? Orientation.Size(t.Orientation, t.Tile.Width, t.Tile.Height) : (1, 1);
        short height = (short)((e?.HeightOffset ?? 0) * storey);
        for (int v = 0; v < h; v++)
            for (int u = 0; u < w; u++)
                if (_entries[(y + v) * Width + x + u] is { } c) _entries[(y + v) * Width + x + u] = c with { HeightOffset = height };
        for (int v = 0; v <= h; v++)
            for (int u = 0; u <= w; u++)
                Corner(x + u, y + v).Height = unchecked((sbyte)(height + (e is { } t2 ? t2.Tile.VertexHeightAt(t2.Orientation, u, v) * storey : 0)));
    }

    /// <summary>A vertex height as the client reads it back (a byte, values below −120 wrapping to 136 … 255).</summary>
    public int HeightAt(int x, int y)
    {
        if (!InsideCorners(x, y)) return 0;
        int h = Corner(x, y).Height;
        return h < -0x78 ? h + 0x100 : h;
    }

    /// <summary><c>0x141d83d60</c>: the tile covering entry (x, y) leaves every entry it covers.</summary>
    public void Remove(int x, int y)
    {
        if (this[x, y] is not { } e) return;
        int ox = x - e.OffsetX, oy = y - e.OffsetY;
        var (w, h) = Orientation.Size(e.Orientation, e.Tile.Width, e.Tile.Height);
        for (int v = 0; v < h; v++)
            for (int u = 0; u < w; u++)
                if (Inside(ox + u, oy + v)) _entries[(oy + v) * Width + ox + u] = null;
    }

    /// <summary><c>0x141d85640</c>: a vertex's ground, unless the vertex already has a different one that the checks
    /// keep ("Mismatching ground types"): with <see cref="CheckGrounds"/> or flag 4 set, a different ground goes in only
    /// when one of the two is null-or-flag-8 against null-or-flag-8, or neither has flag 1 or 4.</summary>
    public void SetGround(int x, int y, string? ground)
    {
        ref var c = ref Corner(x, y);
        string? old = c.Ground;
        bool free = ((!CheckGrounds || (c.Flags & 1) == 0) && (c.Flags & 4) == 0) || old == ground;
        if (!free)
        {
            bool Wild(string? g) => g is null || (GroundFlags(g) & 8) != 0;
            bool Plain(string? g) => g is not null && (GroundFlags(g) & 5) == 0;
            if (ground is null || (GroundFlags(ground) & 8) != 0) free = Wild(old);
            if (!free && Plain(ground) && Plain(old)) free = true;
            if (!free)
                throw new InvalidOperationException($"Mismatching ground types at ({x}, {y}): Cannot place {ground ?? "null"} over {old ?? "null"}");
        }
        if (ground is null) c.Flags |= 1;
        else ForceGround(x, y, ground);
    }

    /// <summary><c>0x141d85840</c>: the vertex takes the ground (flag 1).</summary>
    public void ForceGround(int x, int y, string? ground)
    {
        ref var c = ref Corner(x, y);
        c.Flags |= 1;
        c.Ground = ground;
    }

    /// <summary>The client's tile hash of this grid (<see cref="TileHash"/>).</summary>
    public uint Hash() => TileHash.Compute(Width, Height, (x, y) =>
        this[x, y] is { IsOrigin: true } e ? (e.Tile.Path, e.HeightOffset, e.Orientation) : null);
}
