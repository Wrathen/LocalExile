namespace GameServer.Data.Terrain.Generation;

/// <summary><c>0x1421193d0</c> (after the second overlay pass, before the mirror seams; the height levels' view and
/// state object): the plain tiles take their height from the tiles around them. A tile is plain for ground g
/// (<c>0x142119260</c>) when it has no tag, no real edge (only none or wildcard ones) and all four key corners have
/// ground g (a ground left out of keys counting as none); for <c>0x142119060</c> g is the key's first corner. Every
/// cell of a tile that is not plain is marked done. Then, row by row, stepping by the width of the tile at each cell,
/// a plain tile with a ground g that is not flag 1 collects (<c>0x142119600</c>) each height a neighbour that is not
/// plain offers: the tile left of the cell (its turned key's corners on its right side), right of the tile, above the
/// cell and below the tile, each corner whose ground is g and that lies along this tile's side giving the neighbour's
/// offset plus the corner's height. One distinct height: the tile takes it (<c>0x1421174f0</c>) and is done. Then
/// (<c>0x14211aca0</c>), until a sweep changes nothing, each plain tile not done takes the one distinct offset of the
/// done neighbours (left, right, above, below) plain for its ground. No draws.</summary>
sealed class Storeys(TileGrid grid, int offsetX, int offsetY, int width, int height, TerrainCache files)
{
    // +0x38: the entries given a height (or not plain).
    readonly bool[] _done = new bool[width * height];

    TileEntry? Entry(int x, int y) => grid[x + offsetX, y + offsetY];

    static (int W, int H) Size(TileEntry? e) => e is { } t ? Orientation.Size(t.Orientation, t.Tile.Width, t.Tile.Height) : (1, 1);

    bool Plain(TileEntry e, string? ground) => e.Tile.Tag.Length == 0 &&
        e.Tile.Key.Edges.All(edge => edge is null || files.Edge(edge)?.Wildcard == true) &&
        e.Tile.Key.Grounds.All(g => g == ground);

    bool Plain(TileEntry e) => Plain(e, e.Tile.Key.Grounds[0]);

    // A plain tile whose ground storeys apply to: its first corner's ground, not none and not flag 1.
    string? StoreyGround(TileEntry e) =>
        e.Tile.Key.Grounds[0] is { } g && Plain(e, g) && ((files.Ground(g)?.Flags ?? 0) & 1) == 0 ? g : null;

    void MarkDone(int x, int y, int w, int h)
    {
        for (int v = 0; v < h; v++)
            for (int u = 0; u < w; u++)
                if ((uint)(x + u) < width && (uint)(y + v) < height) _done[(y + v) * width + x + u] = true;
    }

    bool Done(int x, int y) => _done[y * width + x];

    public void Run()
    {
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x += Size(Entry(x, y)).W)
                if (Entry(x, y) is { } e && !Plain(e))
                {
                    var (w, h) = Size(e);
                    MarkDone(x - e.OffsetX, y - e.OffsetY, w, h);
                }
        FromSteps();
        while (FromPlain() != 0) { }
    }

    // 0x1421174f0: the tile covering (x, y) takes the height and is done.
    void Apply(int x, int y, int value)
    {
        var e = Entry(x, y);
        var (w, h) = Size(e);
        int ox = x - (e?.OffsetX ?? 0), oy = y - (e?.OffsetY ?? 0);
        grid.MarkHeights(value != 0);
        grid.SetHeight(ox + offsetX, oy + offsetY, (short)value);
        MarkDone(ox, oy, w, h);
    }

    // 0x142119600.
    void FromSteps()
    {
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x += Size(Entry(x, y)).W)
            {
                if (Entry(x, y) is not { } e || StoreyGround(e) is not { } g) continue;
                var (w, h) = Size(e);
                var found = new SortedSet<int>();
                // A neighbour that is not plain: its turned key, origin and offset.
                void Offer(int nx, int ny, Action<TileKey, int, int, int> take)
                {
                    if (Entry(nx, ny) is not { } n || Plain(n)) return;
                    take(n.Tile.Key.Rotate(n.Orientation), nx - n.OffsetX, ny - n.OffsetY, n.HeightOffset);
                }
                bool Along(int v, int from, int length) => from <= v && v < from + length;
                if (x > 0)
                    Offer(x - 1, y, (k, nx, ny, o) =>
                    {
                        if (k.Grounds[1] == g && Along(ny, y, h)) found.Add(o + k.Heights[1]);
                        if (k.Grounds[2] == g && Along(ny + k.Height, y, h)) found.Add(o + k.Heights[2]);
                    });
                if (x + w < width)
                    Offer(x + w, y, (k, nx, ny, o) =>
                    {
                        if (k.Grounds[0] == g && Along(ny, y, h)) found.Add(o + k.Heights[0]);
                        if (k.Grounds[3] == g && Along(ny + k.Height, y, h)) found.Add(o + k.Heights[3]);
                    });
                if (y > 0)
                    Offer(x, y - 1, (k, nx, ny, o) =>
                    {
                        if (k.Grounds[3] == g && Along(nx, x, w)) found.Add(o + k.Heights[3]);
                        if (k.Grounds[2] == g && Along(nx + k.Width, x, w)) found.Add(o + k.Heights[2]);
                    });
                if (y + h < height)
                    Offer(x, y + h, (k, nx, ny, o) =>
                    {
                        if (k.Grounds[0] == g && Along(nx, x, w)) found.Add(o + k.Heights[0]);
                        if (k.Grounds[1] == g && Along(nx + k.Width, x, w)) found.Add(o + k.Heights[1]);
                    });
                if (found.Count == 1) Apply(x, y, found.Min);
            }
    }

    // 0x14211aca0: one sweep; how many tiles took a height.
    int FromPlain()
    {
        int changed = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x += Size(Entry(x, y)).W)
            {
                if (Entry(x, y) is not { } e || Done(x, y) || StoreyGround(e) is not { } g) continue;
                var (w, h) = Size(e);
                var found = new SortedSet<int>();
                void Offer(int nx, int ny)
                {
                    if (Done(nx, ny) && Entry(nx, ny) is { } n && Plain(n, g)) found.Add(n.HeightOffset);
                }
                if (x > 0) Offer(x - 1, y);
                if (x + w < width) Offer(x + w, y);
                if (y > 0) Offer(x, y - 1);
                if (y + h < height) Offer(x, y + h);
                if (found.Count == 1)
                {
                    Apply(x, y, found.Min);
                    changed++;
                }
            }
        return changed;
    }
}
