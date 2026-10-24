namespace GameServer.Data.Terrain.Generation;

/// <summary>The outer buffer of a tile graph layout (<c>0x142044f30</c> → <c>0x14201ffa0</c>, after all subgraphs): the
/// map around the graph's area (inner rectangle left/top = the buffer, right/bottom = buffer + graph size). The vertex
/// grounds and heights on the inner rectangle's border spread outwards (vertical first, then horizontal; vertex flag 2
/// marks vertices that have a ground here), then four passes (top, bottom, left, right) walk the border: where a tile's
/// edge crosses outwards, an edge tile of that type with its point continued is drawn (<c>Next(total)</c>) and repeated
/// out to the map's edge; elsewhere, where the two border vertices of the step agree, the first fill tile of the
/// ground (<c>0x141feffb0</c>, orientation 8 taken as 0) is repeated outwards where it fits, at the height of the
/// step's first border vertex (an edge tile: the lower of its two border vertices).</summary>
sealed class OuterBuffer(TerrainCache files, LevelRandom rng, TileGrid grid, int left, int top, int right, int bottom,
    TileStore store, Dictionary<string, List<(int Weight, string Path)>> fills, List<string> notes)
{
    int W => grid.Width;
    int H => grid.Height;

    int Flags(string? g) => files.Ground(g)?.Flags ?? 0;

    public void Run()
    {
        for (int y = 0; y <= H; y++)
            for (int x = 0; x <= W; x++)
            {
                ref var c = ref grid.Corner(x, y);
                if ((c.Flags & 1) != 0) c.Flags |= 2;
            }
        bool check = grid.CheckGrounds;
        grid.CheckGrounds = false;
        SpreadVertical();
        SpreadHorizontal();
        Top();
        Bottom();
        Left();
        Right();
        grid.CheckGrounds = check;
    }

    bool Marked(int x, int y) => (grid.Corner(x, y).Flags & 2) != 0;

    // 0x141ffbd30: vertex flag 8 (unwalkable).
    void Flag(int x, int y) => grid.Corner(x, y).Flags |= 8;

    // Spreads the ground and height of vertex (fx, fy) to every vertex in the list without a ground.
    void Spread(int fx, int fy, IEnumerable<(int X, int Y)> to)
    {
        var from = grid.Corner(fx, fy);
        foreach (var (x, y) in to)
        {
            ref var c = ref grid.Corner(x, y);
            if ((c.Flags & 1) != 0) continue;
            c.Height = from.Height;
            c.Flags |= 2;
            grid.SetGround(x, y, from.Ground);
        }
    }

    static IEnumerable<(int, int)> Column(int x, int y0, int y1) { for (int y = y0; y < y1; y++) yield return (x, y); }
    static IEnumerable<(int, int)> Row(int y, int x0, int x1) { for (int x = x0; x < x1; x++) yield return (x, y); }

    // Each spread also flags its source vertex and every buffer vertex on that side of the column or row, written or
    // not: the whole outer buffer becomes unwalkable for the walkability pass after it.
    void SpreadVertical()
    {
        for (int x = left; x <= right; x++)
        {
            int y = bottom;
            while (y < H && Marked(x, y + 1)) y++;
            Flag(x, y);
            foreach (var (cx, cy) in Column(x, bottom + 1, H + 1)) Flag(cx, cy);
            Spread(x, y, Column(x, y + 1, H + 1));
            y = top;
            while (y != 0 && Marked(x, y - 1)) y--;
            Flag(x, y);
            Spread(x, y, Column(x, 0, y));
            foreach (var (cx, cy) in Column(x, 0, top)) Flag(cx, cy);
        }
    }

    void SpreadHorizontal()
    {
        for (int y = 0; y <= H; y++)
        {
            int x = left;
            while (x != 0 && Marked(x - 1, y)) x--;
            Flag(x, y);
            Spread(x, y, Row(y, 0, x));
            foreach (var (cx, cy) in Row(y, 0, left)) Flag(cx, cy);
            x = right;
            while (x < W && Marked(x + 1, y)) x++;
            Flag(x, y);
            foreach (var (cx, cy) in Row(y, right + 1, W + 1)) Flag(cx, cy);
            Spread(x, y, Row(y, x + 1, W + 1));
        }
    }

    // 0x142025000: a ground with flag 8 counts as none.
    string? Ground(int x, int y)
    {
        string? g = grid.Corner(x, y).Ground;
        return g is not null && (Flags(g) & 8) != 0 ? null : g;
    }

    /// <summary><c>0x141d77670</c> negated: the two grounds agree (equal, both none-or-flag-8, or both without flags 1
    /// and 4).</summary>
    bool Agree(string? a, string? b)
    {
        if (a == b) return true;
        bool Wild(string? g) => g is null || (Flags(g) & 8) != 0;
        if (Wild(a) && Wild(b)) return true;
        return a is not null && b is not null && (Flags(a) & 5) == 0 && (Flags(b) & 5) == 0;
    }

    /// <summary><c>0x141feffb0</c>: the first tile of the ground's fill list (no ground: "default").</summary>
    TileDef FirstFill(string? ground)
    {
        string name = ground is null ? "default" : files.Ground(ground)?.Name ?? "default";
        if (!fills.TryGetValue(name, out var list) || list.Count == 0) throw new InvalidOperationException($"No fill tiles found for {name}");
        var path = list[0].Path;
        if (path.EndsWith(".arm", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException($"outer buffer fill with a room first ({name}) is not ported");
        return files.Tile(path) ?? throw new FileNotFoundException(path);
    }

    /// <summary><c>0x141ffbbe0</c> on the whole map: within it give or take one entry, on free entries.</summary>
    bool Fits(TileDef tile, int x, int y)
    {
        if (x < -1 || y < -1 || x + tile.Width > W + 1 || y + tile.Height > H + 1) return false;
        if (x < 0 || y < 0 || x + tile.Width > W || y + tile.Height > H) return false;
        for (int v = 0; v < tile.Height; v++)
            for (int u = 0; u < tile.Width; u++)
                if (grid[x + u, y + v] is not null) return false;
        return true;
    }

    /// <summary><c>0x14201d890</c>: the (non-wildcard) edge type of entry e's tile on side s when its edge point lies
    /// in the size-wide span starting at the entry (a virtual edge gives the sub-edge of the point that does).</summary>
    EdgeType? Crossing(TileEntry e, int s)
    {
        var key = e.Tile.Key.Rotate(e.Orientation);
        if (key.Edges[s] is not { } edge) return null;
        var type = files.Edge(edge) ?? throw new FileNotFoundException(edge);
        int off = (s & 1) == 0 ? e.OffsetX : e.OffsetY;
        if (s >= 2) off = ((s & 1) == 0 ? key.Width : key.Height) - type.Size - off;
        bool In(int p) => off <= p / 3 && p / 3 < off + type.Size;
        EdgeType? result = null;
        if (!type.Virtual) result = In(key.PointA[s]) ? type : null;
        else if (In(key.PointA[s])) result = files.Edge(type.SubEdges[0]);
        else if (In(key.PointB[s])) result = files.Edge(type.SubEdges[1]);
        return result is { Wildcard: false } ? result : null;
    }

    /// <summary>The edge tile continuing a crossing: the type on side s and its opposite, size × size, the point moved
    /// to the entry (point − 3 × offset) and mirrored on the far side, grounds and heights (above the lower one, which
    /// the tile goes in at) from the border vertices v0 and v1.</summary>
    (TileDef Tile, int Orientation, int Height) EdgeTile(TileEntry e, int s, EdgeType type, (int X, int Y) v0, (int X, int Y) v1, string what)
    {
        var (x, y) = v0;
        string? g0 = Ground(v0.X, v0.Y), g1 = Ground(v1.X, v1.Y);
        int h0 = (sbyte)grid.HeightAt(v0.X, v0.Y), h1 = (sbyte)grid.HeightAt(v1.X, v1.Y), low = Math.Min(h0, h1);
        int size = type.Size;
        var rotated = e.Tile.Key.Rotate(e.Orientation);
        int off = (s & 1) == 0 ? e.OffsetX : e.OffsetY;
        if (s >= 2) off = ((s & 1) == 0 ? rotated.Width : rotated.Height) - size - off;
        int p = rotated.PointA[s] - 3 * off;
        var key = new TileKey { Width = (byte)size, Height = (byte)size };
        for (int i = 0; i < 4; i++) key.PointA[i] = key.PointB[i] = (byte)(3 * size);
        int o = (s + 2) & 3;
        key.Edges[s] = key.Edges[o] = type.Path;
        key.PointA[s] = (byte)p;
        key.PointA[o] = (byte)(3 * size - p - 1);
        // Corners: along a horizontal border g0 is the left vertex, g1 the right one; along a vertical one top/bottom.
        int[] first = (s & 1) == 0 ? [0, 3] : [0, 1], second = (s & 1) == 0 ? [1, 2] : [2, 3];
        foreach (int c in first) (key.Grounds[c], key.Heights[c]) = (g0, (sbyte)(h0 - low));
        foreach (int c in second) (key.Grounds[c], key.Heights[c]) = (g1, (sbyte)(h1 - low));
        var list = store.Candidates("", key);
        if (list.Count == 0)
        {
            if (type.N0 != 0) throw new NotSupportedException("outer buffer edge tiles with heights (edge +0x4c) are not ported");
            throw new InvalidOperationException($"Could not find edge tile to expand outer buffer {what} at ({x}, {y}): {key}");
        }
        int r = (int)rng.Next((uint)list.Sum(c => c.Weight));
        foreach (var c in list)
        {
            if (r < c.Weight) return (c.Tile, c.Orientation, low);
            r -= c.Weight;
        }
        return (list[^1].Tile, list[^1].Orientation, low);
    }

    // 0x141ffb870 (not forced).
    void Place(TileDef tile, int o, int x, int y, int height)
    {
        grid.Place(tile, o, x, y, (short)height);
    }

    void Top()
    {
        int x = left;
        while (x != 0 && grid[x - 1, top] is not null) x--;
        while (x <= right)
        {
            int y = top;
            while (y != 0 && grid[x, y - 1] is not null) y--;
            if (y == 0 || grid[x, y] is not { } e || Crossing(e, 0) is not { } type)
            {
                if (y != 0 && Agree(Ground(x, y), Ground(x + 1, y)))
                {
                    var tile = FirstFill(Ground(x, y));
                    if (tile.Width + x <= W)
                        for (int yy = 0; yy < y; yy += tile.Height)
                            if (Fits(tile, x, yy)) Place(tile, 0, x, yy, grid.HeightAt(x, y));
                    x += tile.Width;
                    continue;
                }
                x++;
                continue;
            }
            notes.Add($"outer buffer up at ({x}, {y}): {type.Name}");
            var (t, o, h) = EdgeTile(e, 0, type, (x, y), (x + type.Size, y), "up");
            for (int yy = 0; yy < y; yy += type.Size) Place(t, o, x, yy, h);
            x += type.Size;
        }
    }

    void Bottom()
    {
        int x = left;
        while (x != 0 && grid[x - 1, bottom - 1] is not null) x--;
        while (x < right)
        {
            int y = bottom;
            while (y < H && grid[x, y] is not null) y++;
            if (y == H || grid[x, y - 1] is not { } e || Crossing(e, 2) is not { } type)
            {
                if (y != H && Agree(Ground(x, y), Ground(x + 1, y)))
                {
                    var tile = FirstFill(Ground(x, y));
                    if (tile.Width + x <= W)
                        for (int yy = y; yy + tile.Height <= H; yy += tile.Height)
                            if (Fits(tile, x, yy)) Place(tile, 0, x, yy, grid.HeightAt(x, y));
                    x += tile.Width;
                    continue;
                }
                x++;
                continue;
            }
            notes.Add($"outer buffer down at ({x}, {y}): {type.Name}");
            var (t, o, h) = EdgeTile(e, 2, type, (x, y), (x + type.Size, y), "down");
            for (int yy = y; yy + type.Size <= H; yy += type.Size) Place(t, o, x, yy, h);
            x += type.Size;
        }
    }

    void Left()
    {
        for (int y = 0; y < H;)
        {
            int x = left;
            while (x != 0 && grid[x - 1, y] is not null) x--;
            if (x == 0 || grid[x, y] is not { } e || Crossing(e, 3) is not { } type)
            {
                if (x != 0 && Agree(Ground(x, y), Ground(x, y + 1)))
                {
                    var tile = FirstFill(Ground(x, y));
                    if (tile.Height + y <= H)
                        for (int xx = 0; xx < x; xx += tile.Width)
                            if (Fits(tile, xx, y)) Place(tile, 0, xx, y, grid.HeightAt(x, y));
                    y += tile.Height;
                    continue;
                }
                y++;
                continue;
            }
            notes.Add($"outer buffer left at ({x}, {y}): {type.Name}");
            var (t, o, h) = EdgeTile(e, 3, type, (x, y), (x, y + type.Size), "left");
            for (int xx = 0; xx < x; xx += type.Size) Place(t, o, xx, y, h);
            y += type.Size;
        }
    }

    void Right()
    {
        for (int y = 0; y < H;)
        {
            int x = right;
            while (x < W && grid[x, y] is not null) x++;
            if (x == W || grid[x - 1, y] is not { } e || Crossing(e, 1) is not { } type)
            {
                if (x != W && Agree(Ground(x, y), Ground(x, y + 1)))
                {
                    var tile = FirstFill(Ground(x, y));
                    if (tile.Height + y <= H)
                        for (int xx = x; xx + tile.Width <= W; xx += tile.Width)
                            if (Fits(tile, xx, y)) Place(tile, 0, xx, y, grid.HeightAt(x, y));
                    y += tile.Height;
                    continue;
                }
                y++;
                continue;
            }
            notes.Add($"outer buffer right at ({x}, {y}): {type.Name}");
            var (t, o, h) = EdgeTile(e, 1, type, (x, y), (x, y + type.Size), "right");
            for (int xx = x; xx + type.Size <= W; xx += type.Size) Place(t, o, xx, y, h);
            y += type.Size;
        }
    }
}
