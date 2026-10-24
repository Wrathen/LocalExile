namespace GameServer.Data.Terrain.Generation;

/// <summary><c>0x142118470</c> (the state built by <c>0x1421173d0</c>): the height fix-ups of the map's view (the
/// graph's area, <paramref name="offsetX"/>, <paramref name="offsetY"/> in from the map's corner). Every tile not yet reached starts a breadth-first
/// walk (<c>0x142118100</c>) that gives each tile a height offset agreeing with the tiles already done around it
/// (<see cref="Candidates"/>), writes it (<c>0x1421174f0</c>) and goes on to the tiles it is open to
/// (<see cref="Neighbours"/>). With any height in the view, everything is then lowered to the lowest tile offset, the
/// vertices still without a height take a lone done neighbour's (<c>0x142118790</c>). No draws.</summary>
sealed class HeightLevels(TileGrid grid, int offsetX, int offsetY, int width, int height, TerrainCache files)
{
    // Bit sets +0x18 (vertices, (width + 1) × (height + 1)) and +0x38 (entries, width × height): already given a height.
    readonly bool[] _vertexDone = new bool[(width + 1) * (height + 1)];
    readonly bool[] _tileDone = new bool[width * height];

    TileEntry? Entry(int x, int y) => grid[x + offsetX, y + offsetY];

    (int X, int Y) Origin(int x, int y) => Entry(x, y) is { } e ? (x - e.OffsetX, y - e.OffsetY) : (x, y);

    static (int W, int H) Size(TileEntry? e) => e is { } t ? Orientation.Size(t.Orientation, t.Tile.Width, t.Tile.Height) : (1, 1);

    int VertexHeight(int x, int y) => grid.HeightAt(x + offsetX, y + offsetY);

    bool VertexDone(int x, int y) => _vertexDone[y * (width + 1) + x];

    // A ground that lets heights through: none, or flag 1.
    bool Open(string? ground) => ground is null || (files.Ground(ground)?.Flags & 1) != 0;

    bool Real(string? edge) => edge is not null && files.Edge(edge)?.Wildcard != true;

    public void Run()
    {
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width;)
            {
                if (Entry(x, y) is not { IsOrigin: true } e)
                {
                    x++;
                    continue;
                }
                if (!_tileDone[y * width + x]) Spread(x, y);
                x += Size(e).W;
            }
        if (!grid.HasHeights) return;

        int lowest = int.MaxValue;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (Entry(x, y) is { IsOrigin: true } e) lowest = Math.Min(lowest, e.HeightOffset);
        for (int y = 0; y <= height; y++)
            for (int x = 0; x <= width; x++)
            {
                int v = VertexHeight(x, y) - lowest;
                grid.MarkHeights(v != 0);
                ref var c = ref grid.Corner(x + offsetX, y + offsetY);
                c.Flags |= 2;
                c.Height = unchecked((sbyte)v);
            }
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width;)
            {
                if (Entry(x, y) is not { } e)
                {
                    x++;
                    continue;
                }
                if (e.IsOrigin)
                {
                    grid.MarkHeights(e.HeightOffset != lowest);
                    grid.SetHeight(x + offsetX, y + offsetY, (short)(e.HeightOffset - lowest));
                }
                x += Size(e).W;
            }
        FillVertices();
        // 0x142118900 then gives every room of the subgraph the height of one of its tiles (room +0x18); nothing ported
        // reads it yet.
    }

    /// <summary><c>0x142118100</c>: the walk from one tile. A tile with several candidate heights that cannot be
    /// reconciled is fatal ("Mismatched heights found during height fix-ups").</summary>
    void Spread(int sx, int sy)
    {
        var queue = new LinkedList<(int X, int Y)>();
        queue.AddLast((sx, sy));
        while (queue.Count > 0)
        {
            var p = queue.First!.Value;
            queue.RemoveFirst();
            var heights = Candidates(p.X, p.Y);
            if (heights.Count > 1)
                throw new InvalidOperationException($"Mismatched heights found during height fix-ups at ({p.X}, {p.Y}): {string.Join(", ", heights)}");
            SetTileHeight(p.X, p.Y, heights.Count == 1 ? heights[0] : 0);
            foreach (var n in Neighbours(p.X, p.Y))
                if (!_tileDone[n.Y * width + n.X]) queue.AddLast(n);
            while (queue.Count > 0)
            {
                var f = queue.First!.Value;
                int i = f.Y * width + f.X;
                if ((uint)i < (uint)_tileDone.Length && !_tileDone[i]) break;
                queue.RemoveFirst();
            }
        }
    }

    /// <summary><c>0x1421176c0</c>: the heights the tile at (x, y) could take. An empty entry: the heights of its done
    /// vertices with an open ground. A tile: each done border vertex where the tile's ground is open gives the vertex
    /// height less the tile's own; else each real edge whose neighbour across it is done gives the neighbour's offset
    /// plus the height step between the two tiles at the edge point; else the done corners as for the border. Several
    /// heights for a tile keep the lowest.</summary>
    List<int> Candidates(int x, int y)
    {
        var list = new List<int>();
        var (ox, oy) = Origin(x, y);
        void AddDistinct(int h)
        {
            if (!list.Contains(h)) list.Add(h);
        }
        if (Entry(ox, oy) is not { } e)
        {
            // 0x142117e10.
            foreach (var (dx, dy) in new[] { (0, 0), (0, 1), (1, 0), (1, 1) })
                if (VertexDone(ox + dx, oy + dy) && Open(grid.Corner(ox + dx + offsetX, oy + dy + offsetY).Ground))
                    AddDistinct(VertexHeight(ox + dx, oy + dy));
            return list;
        }
        var tile = e.Tile;
        int o = e.Orientation;
        var (w, h) = Size(e);
        // 0x142117fb0.
        void Relative(int u, int v)
        {
            if (VertexDone(ox + u, oy + v)) AddDistinct(VertexHeight(ox + u, oy + v) - tile.VertexHeightAt(o, u, v));
        }
        void Border(int u, int v)
        {
            if (Open(tile.VertexGroundAt(o, u, v))) Relative(u, v);
        }
        for (int i = 0; i <= w; i++)
        {
            Border(i, 0);
            Border(i, h);
        }
        for (int j = 1; j < h; j++)
        {
            Border(0, j);
            Border(w, j);
        }
        if (list.Count == 0)
        {
            var key = tile.Key.Rotate(o);
            for (int s = 0; s < 4; s++)
            {
                if (!Real(key.Edges[s])) continue;
                int a = key.PointA[s] / 3;
                var (nx, ny) = s switch
                {
                    0 => (ox + a, oy - 1),
                    1 => (ox + w, oy + a),
                    2 => (ox + w - 1 - a, oy + h),
                    _ => (ox - 1, oy + h - 1 - a),
                };
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) continue;
                if (Entry(nx, ny) is not { } n || !_tileDone[ny * width + nx]) continue;
                // The edge point's vertex in this tile's frame and in the neighbour's.
                int nox = n.OffsetX, noy = n.OffsetY;
                var (tu, tv, nu, nv) = s switch
                {
                    0 => (a, 0, nox, noy + 1),
                    1 => (w, a, nox, noy),
                    2 => (w - 1 - a, h, nox, noy),
                    _ => (0, h - 1 - a, nox + 1, noy),
                };
                list.Add(n.HeightOffset - tile.VertexHeightAt(o, tu, tv) + n.Tile.VertexHeightAt(n.Orientation, nu, nv));
            }
            if (list.Count == 0)
            {
                Relative(0, 0);
                Relative(w, 0);
                Relative(0, h);
                Relative(w, h);
            }
        }
        if (list.Count > 1) list = [list.Min()];
        return list;
    }

    /// <summary><c>0x1421174f0</c>: the tile (or empty entry) at (x, y) takes the height; its vertices and entries are
    /// done.</summary>
    void SetTileHeight(int x, int y, int h)
    {
        var (ox, oy) = Origin(x, y);
        var (w, ht) = Size(Entry(ox, oy));
        grid.MarkHeights(h != 0);
        grid.SetHeight(ox + offsetX, oy + offsetY, (short)h);
        for (int v = 0; v <= ht; v++)
            for (int u = 0; u <= w; u++)
                if (ox + u <= width && oy + v <= height) _vertexDone[(oy + v) * (width + 1) + ox + u] = true;
        for (int v = 0; v < ht; v++)
            for (int u = 0; u < w; u++)
                if (ox + u < width && oy + v < height) _tileDone[(oy + v) * width + ox + u] = true;
    }

    /// <summary><c>0x14211b4a0</c>: the origins of the tiles a tile is open to, ordered by x · 65536 + y. An empty
    /// entry: its four neighbours. A tile: along each side, the entries beside a cell whose two side vertices have a
    /// ground open on the tile (either one), and the entry across each real edge at its edge point.</summary>
    IEnumerable<(int X, int Y)> Neighbours(int x, int y)
    {
        var set = new SortedSet<(int X, int Y)>(Comparer<(int X, int Y)>.Create((a, b) => (a.X * 0x10000 + a.Y).CompareTo(b.X * 0x10000 + b.Y)));
        var (ox, oy) = Origin(x, y);
        if (Entry(ox, oy) is not { } e)
        {
            if (ox > 0) set.Add(Origin(ox - 1, oy));
            if (oy > 0) set.Add(Origin(ox, oy - 1));
            if (ox + 1 < width) set.Add(Origin(ox + 1, oy));
            if (oy + 1 < height) set.Add(Origin(ox, oy + 1));
            return set;
        }
        var tile = e.Tile;
        int o = e.Orientation;
        var (w, h) = Size(e);
        var key = tile.Key.Rotate(o);
        bool OpenAt(int u, int v) => Open(tile.VertexGroundAt(o, u, v));
        void Across(int nx, int ny)
        {
            if ((uint)nx < (uint)width && (uint)ny < (uint)height && Entry(nx, ny) is not null) set.Add(Origin(nx, ny));
        }
        if (ox > 0)
        {
            for (int j = 0; j < h; j++)
                if (OpenAt(0, j) || OpenAt(0, j + 1)) set.Add(Origin(ox - 1, oy + j));
            if (Real(key.Edges[3])) Across(ox - 1, oy + h - 1 - key.PointA[3] / 3);
        }
        if (ox + w < width)
        {
            for (int j = 0; j < h; j++)
                if (OpenAt(w, j) || OpenAt(w, j + 1)) set.Add(Origin(ox + w, oy + j));
            if (Real(key.Edges[1])) Across(ox + w, oy + key.PointA[1] / 3);
        }
        if (oy + h < height)
        {
            for (int i = 0; i < w; i++)
                if (OpenAt(i, h) || OpenAt(i + 1, h)) set.Add(Origin(ox + i, oy + h));
            if (Real(key.Edges[2])) Across(ox + w - 1 - key.PointA[2] / 3, oy + h);
        }
        if (oy > 0)
        {
            for (int i = 0; i < w; i++)
                if (OpenAt(i, 0) || OpenAt(i + 1, 0)) set.Add(Origin(ox + i, oy - 1));
            if (Real(key.Edges[0])) Across(ox + key.PointA[0] / 3, oy - 1);
        }
        return set;
    }

    /// <summary><c>0x142118790</c> with <c>0x142118d50</c>: until a pass changes nothing, every vertex not done whose
    /// done neighbours (left, up, right, down) are exactly one takes that one's height.</summary>
    void FillVertices()
    {
        int changed;
        do
        {
            changed = 0;
            for (int y = 0; y <= height; y++)
                for (int x = 0; x <= width; x++)
                {
                    if (VertexDone(x, y)) continue;
                    var found = new List<int>();
                    if (x > 0 && VertexDone(x - 1, y)) found.Add(VertexHeight(x - 1, y));
                    if (y > 0 && VertexDone(x, y - 1)) found.Add(VertexHeight(x, y - 1));
                    if (x < width && VertexDone(x + 1, y)) found.Add(VertexHeight(x + 1, y));
                    if (y < height && VertexDone(x, y + 1)) found.Add(VertexHeight(x, y + 1));
                    if (found.Count != 1) continue;
                    grid.MarkHeights(found[0] != 0);
                    ref var c = ref grid.Corner(x + offsetX, y + offsetY);
                    c.Flags |= 2;
                    c.Height = unchecked((sbyte)found[0]);
                    _vertexDone[y * (width + 1) + x] = true;
                    changed++;
                }
        }
        while (changed != 0);
    }

    /// <summary><c>0x141ffc700</c> / <c>0x141ffc7d0</c>: the highest and lowest vertex height of the view.</summary>
    public (int Low, int High) Range()
    {
        int lo = VertexHeight(0, 0), hi = lo;
        for (int y = 0; y <= height; y++)
            for (int x = 0; x <= width; x++)
            {
                int v = VertexHeight(x, y);
                lo = Math.Min(lo, v);
                hi = Math.Max(hi, v);
            }
        return (lo, hi);
    }
}
