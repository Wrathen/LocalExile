namespace GameServer.Data.Terrain.Generation;

/// <summary>The mirror seams (<c>0x142025440</c> on the view: a tile graph's after the overlays, a dungeon's after its
/// fill, <c>0x1420f3480</c>, with its placed rooms as the room list): row by row, a
/// tile larger than one entry, without a tag and not a plain fill tile, none of whose vertices took a room's own
/// vertex ground (room data +0xe8, <c>0x142025fb0</c>), whose neighbour one tile size away is the same tile mirrored is re-picked among
/// its key's tiles that make no seam with their up and left neighbours (<c>0x142027670</c>). With none, outside rooms,
/// a neighbour's shared edge point moves instead (<c>0x142026bc0</c>).</summary>
sealed class Seams(TerrainCache files, LevelRandom rng, TileGrid grid, int offsetX, int offsetY, int width, int height, TileStore store,
    IReadOnlyList<PlacedRoom> rooms, IReadOnlySet<(int X, int Y)> roomVertices, (string? A0, string? A8, string? B0) grounds, List<string> notes)
{
    readonly bool[] _roomCells = new bool[width * height];

    TileEntry? Entry(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height ? grid[x + offsetX, y + offsetY] : null;

    static (int W, int H) Size(TileEntry e) => Orientation.Size(e.Orientation, e.Tile.Width, e.Tile.Height);

    public void Run()
    {
        foreach (var r in rooms)
            for (int y = r.Y; y < r.Y + r.Height; y++)
                for (int x = r.X; x < r.X + r.Width; x++)
                    if ((uint)(x - offsetX) < (uint)width && (uint)(y - offsetY) < (uint)height) _roomCells[(y - offsetY) * width + x - offsetX] = true;
        bool check = grid.CheckGrounds;
        grid.CheckGrounds = false;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (Entry(x, y) is not { IsOrigin: true } e) continue;
                var (w, h) = Size(e);
                if (w == 1 && h == 1) continue;
                if (e.Tile.Tag.Length != 0 || PlainFill(e.Tile.Key)) continue;
                if (TouchesRoomVertex(roomVertices, x + offsetX, y + offsetY, w, h)) continue;
                for (int d = 0; d < 4; d++)
                {
                    var (dx, dy) = Orientation.Directions[d];
                    if (Entry(x + w * dx, y + h * dy) is not { IsOrigin: true } n || n.Tile != e.Tile) continue;
                    if (Orientation.MirrorAcross(e.Orientation, d) != n.Orientation) continue;
                    Repair(x, y, e, d, n);
                    break;
                }
            }
        grid.CheckGrounds = check;
    }

    /// <summary><c>0x142025fb0</c>: some vertex of the w × h area at map (x, y) took a room's own vertex ground.</summary>
    internal static bool TouchesRoomVertex(IReadOnlySet<(int X, int Y)> roomVertices, int x, int y, int w, int h)
    {
        for (int v = y; v <= y + h; v++)
            for (int u = x; u <= x + w; u++)
                if (roomVertices.Contains((u, v))) return true;
        return false;
    }

    // 0x14201d800: no edge types but wildcard ones (.et +0x48 bit 7) and one ground on every corner.
    bool PlainFill(TileKey key) => PlainFill(files, key);

    internal static bool PlainFill(TerrainCache files, TileKey key) =>
        key.Edges.All(e => e is null || files.Edge(e)?.Wildcard == true) && key.Grounds.Distinct().Count() == 1;

    /// <summary><c>0x142025e60</c>: some entry of the area belongs to a tilised room.</summary>
    bool InRoom(int x, int y, int w, int h)
    {
        for (int v = y; v < y + h; v++)
            for (int u = x; u < x + w; u++)
                if ((uint)u < (uint)width && (uint)v < (uint)height && _roomCells[v * width + u]) return true;
        return false;
    }

    /// <summary><c>0x142027670</c>.</summary>
    void Repair(int x, int y, TileEntry e, int direction, TileEntry neighbour)
    {
        var key = e.Tile.Key.Rotate(e.Orientation);
        var list = new List<(TileDef Tile, int Orientation, int Weight)>();
        foreach (var c in store.Candidates("", key))
        {
            if (Seam(x, y, c.Tile, c.Orientation, allDirections: false)) continue;
            if (c.Tile == neighbour.Tile && Orientation.MirrorAcross(c.Orientation, direction) == neighbour.Orientation) continue;
            if (c.Weight != 0) list.Add(c);
        }
        if (list.Count != 0)
        {
            var pick = Pick(list, (int)rng.Next((uint)list.Sum(c => c.Weight)));
            Replace(x, y, pick.Tile, pick.Orientation);
            return;
        }
        var (w, h) = Size(e);
        if (!InRoom(x, y, w, h)) Fallback(x, y, w, h, key, direction);
    }

    static (TileDef Tile, int Orientation, int Weight) Pick(List<(TileDef Tile, int Orientation, int Weight)> list, int r)
    {
        foreach (var c in list)
        {
            if (r < c.Weight) return c;
            r -= c.Weight;
        }
        return list[^1];
    }

    /// <summary><c>0x142026100</c>: the tile at (x, y) would face a mirrored copy of itself (up and left only, unless all).</summary>
    bool Seam(int x, int y, TileDef tile, int o, bool allDirections)
    {
        var (w, h) = Orientation.Size(o, tile.Width, tile.Height);
        foreach (int d in allDirections ? [0, 1, 2, 3] : new[] { 0, 3 })
        {
            var (dx, dy) = Orientation.Directions[d];
            if (Entry(x + w * dx, y + h * dy) is { IsOrigin: true } n && n.Tile == tile && n.Orientation == Orientation.MirrorAcross(o, d))
                return true;
        }
        return false;
    }

    /// <summary><c>0x142026bc0</c>: for each other side with a (non-virtual) edge whose neighbour has the same size, no tag
    /// and lies outside rooms, every other edge point on that side gives the tiles of the changed key that make no seam
    /// up or left; drawn by weight (<c>Next(total)</c>), a choice stands when the neighbour, its key's opposite point
    /// moved to meet it, has tiles making no seam in any direction other than the choice mirrored: both are replaced
    /// (the neighbour by <c>Next(total)</c>). A choice that fails leaves the list.</summary>
    void Fallback(int x, int y, int w, int h, TileKey key, int direction)
    {
        var choices = new List<(TileDef Tile, int Orientation, int Side, int Weight)>();
        for (int s = 0; s < 4; s++)
        {
            if (s == direction || key.Edges[s] is not { } edge || files.Edge(edge)?.Virtual != false) continue;
            var (dx, dy) = Orientation.Directions[s];
            int nx = x + dx * w, ny = y + dy * h;
            if (Entry(nx, ny) is not { IsOrigin: true } n || Size(n) != (w, h) || n.Tile.Tag.Length != 0) continue;
            if (InRoom(nx, ny, w, h)) continue;
            int len = 3 * ((s & 1) == 0 ? key.Width : key.Height);
            for (int p = 0; p < len; p++)
            {
                if (p == key.PointA[s]) continue;
                var k2 = key.Clone();
                k2.PointA[s] = (byte)p;
                foreach (var c in store.Candidates("", k2))
                    if (!Seam(x, y, c.Tile, c.Orientation, allDirections: false) && c.Weight != 0)
                        choices.Add((c.Tile, c.Orientation, s, c.Weight));
            }
        }
        int total = choices.Sum(c => c.Weight);
        while (choices.Count > 0)
        {
            int r = (int)rng.Next((uint)total);
            var ch = choices[^1];
            foreach (var c in choices)
            {
                if (r < c.Weight) { ch = c; break; }
                r -= c.Weight;
            }
            var chKey = ch.Tile.Key.Rotate(ch.Orientation);
            int cv = (ch.Side & 1) == 0 ? chKey.Width : chKey.Height;
            var (dx, dy) = Orientation.Directions[ch.Side];
            int nx = x + dx * w, ny = y + dy * h;
            var n = Entry(nx, ny)!.Value;
            var nKey = n.Tile.Key.Rotate(n.Orientation);
            nKey.PointA[(ch.Side + 2) & 3] = (byte)(cv * 3 - chKey.PointA[ch.Side] - 1);
            var list = new List<(TileDef Tile, int Orientation, int Weight)>();
            foreach (var c in store.Candidates("", nKey))
            {
                if (Seam(nx, ny, c.Tile, c.Orientation, allDirections: true)) continue;
                if (c.Tile == ch.Tile && Orientation.MirrorAcross(ch.Orientation, ch.Side) == c.Orientation) continue;
                if (c.Weight != 0) list.Add(c);
            }
            if (list.Count != 0)
            {
                Replace(x, y, ch.Tile, ch.Orientation);
                var pick = Pick(list, (int)rng.Next((uint)list.Sum(c => c.Weight)));
                Replace(nx, ny, pick.Tile, pick.Orientation);
                notes.Add($"seam at ({x + offsetX}, {y + offsetY}): edge point fallback, side {ch.Side}");
                return;
            }
            for (int i = choices.Count - 1; i >= 0; i--)
                if (choices[i].Tile == ch.Tile && choices[i].Orientation == ch.Orientation && choices[i].Side == ch.Side)
                {
                    total -= choices[i].Weight;
                    choices.RemoveAt(i);
                }
        }
    }

    /// <summary><c>0x141d80030</c>: the ground of a tile's vertex at (u, v) relative to its origin on the map, under its
    /// orientation.</summary>
    static string? VertexAt(TileDef tile, int o, int u, int v)
    {
        for (int tv = 0; tv <= tile.Height; tv++)
            for (int tu = 0; tu <= tile.Width; tu++)
                if (Orientation.MapVertex(o, tu, tv, tile.Width, tile.Height) == (u, v)) return tile.VertexGround(tu, tv);
        return null;
    }

    /// <summary><c>0x142026370</c>: no other tile around vertex (x, y) has a ground there.</summary>
    bool Unowned(int x, int y, int ox, int oy)
    {
        foreach (var (dx, dy) in new[] { (0, 0), (-1, 0), (-1, -1), (0, -1) })
        {
            if (Entry(x + dx, y + dy) is not { } e) continue;
            int tx = x + dx - e.OffsetX, ty = y + dy - e.OffsetY;
            if (tx == ox && ty == oy) continue;
            if (VertexAt(e.Tile, e.Orientation, x - tx, y - ty) is not null) return false;
        }
        return true;
    }

    /// <summary><c>0x1420264b0</c>: the tile at (x, y) is replaced (same height offset). Vertices only it gives a ground
    /// that the new tile leaves without one take the most common ground among the old tile's groundless vertices (or,
    /// with none, among their outside neighbours; with none either, by height: the graph's grounds).</summary>
    void Replace(int x, int y, TileDef tile, int o)
    {
        var old = Entry(x, y)!.Value;
        var (w, h) = Orientation.Size(o, tile.Width, tile.Height);
        var counts = new Dictionary<string, int>();
        string? best = null;
        bool any = false;
        void Count(string? g)
        {
            any = true;
            string k = g ?? "";
            string b = best ?? "";
            int before = counts.GetValueOrDefault(b);
            counts[k] = counts.GetValueOrDefault(k) + 1;
            if (before < counts[k]) best = g;
        }
        var lost = new List<(int X, int Y)>();
        for (int vy = y; vy <= y + h; vy++)
            for (int vx = x; vx <= x + w; vx++)
            {
                if (!Unowned(vx, vy, x, y)) continue;
                if (VertexAt(old.Tile, old.Orientation, vx - x, vy - y) is null) Count(grid.Corner(vx + offsetX, vy + offsetY).Ground);
                else if (VertexAt(tile, o, vx - x, vy - y) is null) lost.Add((vx, vy));
            }
        grid.Place(tile, o, x + offsetX, y + offsetY, old.HeightOffset);
        if (lost.Count != 0 && !any)
            foreach (var (vx, vy) in lost)
                foreach (var (dx, dy) in Orientation.Directions)
                {
                    int nx = vx + dx, ny = vy + dy;
                    bool outside = nx < x || nx >= x + w + 1 || ny < y || ny >= y + h + 1;
                    if (outside && nx >= 0 && nx < width + 1 && ny >= 0 && ny < height + 1 && Unowned(nx, ny, x, y))
                        Count(grid.Corner(nx + offsetX, ny + offsetY).Ground);
                }
        foreach (var (vx, vy) in lost)
        {
            string? g = best;
            if (!any)
            {
                int vh = grid.Corner(vx + offsetX, vy + offsetY).Height;
                g = grounds.A0 is not null && vh >= 0 ? grounds.A0 : grounds.A8 is not null && vh <= 0 ? grounds.A8 : grounds.B0;
            }
            grid.ForceGround(vx + offsetX, vy + offsetY, g);
        }
    }
}
