namespace GameServer.Data.Terrain.Generation;

/// <summary>The vertex ground stages of <c>0x142116600</c> between the edge tiling and the overlays, on the map's
/// view (the graph's area, <paramref name="offsetX"/>, <paramref name="offsetY"/> in from the map's corner): written vertices are locked, the grounds
/// of placed tiles flood out over unwritten vertices (<c>0x142110a20</c>), the vertices still unwritten take the
/// graph's ground (<c>0x142110d60</c>), and the edge tiles of types listed with the fill tiles get their open sides
/// filled (<c>0x141feed20</c>); then the locks go.</summary>
sealed class GroundStages(TileGrid grid, int offsetX, int offsetY, int width, int height, TerrainCache files)
{
    static readonly (int X, int Y)[] Dir = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    int Flags(string? g) => files.Ground(g)?.Flags ?? 0;

    ref TileCorner Corner(int x, int y) => ref grid.Corner(x + offsetX, y + offsetY);

    TileEntry? Entry(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height ? grid[x + offsetX, y + offsetY] : null;

    static (int W, int H) Size(TileEntry e) => Orientation.Size(e.Orientation, e.Tile.Width, e.Tile.Height);

    /// <summary>0x142116600 before 0x142110a20: flag 4 on every vertex with a ground written, off on the others.</summary>
    public void Lock()
    {
        for (int y = 0; y <= grid.Height; y++)
            for (int x = 0; x <= grid.Width; x++)
            {
                ref var c = ref grid.Corner(x, y);
                c.Flags = (byte)((c.Flags & 1) != 0 ? c.Flags | 4 : c.Flags & ~4);
            }
    }

    /// <summary>0x142116600 after 0x141feed20: flag 4 off everywhere.</summary>
    public void Unlock()
    {
        for (int y = 0; y <= grid.Height; y++)
            for (int x = 0; x <= grid.Width; x++) grid.Corner(x, y).Flags &= unchecked((byte)~4);
    }

    /// <summary><c>0x142110a20</c>: for every tile in the view (origin entries, x outer, y inner), every vertex on its
    /// border floods outwards: the top and bottom rows upwards and downwards, then the left and right columns.</summary>
    public void Flood()
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (Entry(x, y) is not { IsOrigin: true } e) continue;
                var (w, h) = Size(e);
                for (int i = 0; i <= w; i++)
                {
                    FloodFrom(x + i, y, Corner(x + i, y).Ground, 0);
                    FloodFrom(x + i, y + h, Corner(x + i, y + h).Ground, 2);
                }
                for (int i = 0; i <= h; i++)
                {
                    FloodFrom(x, y + i, Corner(x, y + i).Ground, 3);
                    FloodFrom(x + w, y + i, Corner(x + w, y + i).Ground, 1);
                }
            }
    }

    /// <summary><c>0x142110630</c>: a ground with flag 2 spreads from vertex (x, y) in direction d, breadth first
    /// (each step goes on straight or turns, never back), over vertices without a ground written (a flag-8 ground
    /// also over ones written with none), stopping where a tile's edge or interior is in the way.</summary>
    void FloodFrom(int x, int y, string? ground, int d)
    {
        if (ground is null || (Flags(ground) & 2) == 0) return;
        var queue = new Queue<(int X, int Y, int D)>();
        void Push(int fx, int fy, int nd)
        {
            int nx = fx + Dir[nd].X, ny = fy + Dir[nd].Y;
            if (nx >= 0 && ny >= 0 && nx <= width && ny <= height && !InTheWay(fx, fy, nx, ny, nd)) queue.Enqueue((nx, ny, nd));
        }
        Push(x, y, d);
        bool wild = (Flags(ground) & 8) != 0;
        while (queue.Count > 0)
        {
            var (vx, vy, vd) = queue.Dequeue();
            ref var c = ref Corner(vx, vy);
            if ((c.Flags & 1) != 0 && !(wild && c.Ground is null)) continue;
            grid.SetGround(vx + offsetX, vy + offsetY, ground);
            Push(vx, vy, vd);
            Push(vx, vy, (vd + 1) & 3);
            Push(vx, vy, (vd - 1) & 3);
        }
    }

    /// <summary><c>0x1420281e0</c> ("EdgeInTheWay"): from a tile's interior only into its interior; into an interior
    /// always; otherwise not across a tile edge on the segment between the two vertices.</summary>
    bool InTheWay(int fx, int fy, int tx, int ty, int d)
    {
        if (Interior(fx, fy)) return !Interior(tx, ty);
        if (Interior(tx, ty)) return false;
        return d switch
        {
            0 => Crosses(tx, ty, 3) || Crosses(tx - 1, ty, 1),
            1 => Crosses(fx, fy, 0) || Crosses(fx, fy - 1, 2),
            2 => Crosses(fx, fy, 3) || Crosses(fx - 1, fy, 1),
            _ => Crosses(tx, ty, 0) || Crosses(tx, ty - 1, 2),
        };
    }

    /// <summary><c>0x142027df0</c>: the vertex lies strictly inside a tile (the entry it is the top-left of belongs
    /// to a tile that also covers the entries above and to the left).</summary>
    bool Interior(int x, int y) => Entry(x, y) is { } e && e.OffsetX > 0 && e.OffsetY > 0;

    /// <summary><c>0x142027bd0</c>: the tile at entry (x, y) has an edge (not a wildcard) whose edge point on side s
    /// lies on this entry (point / 3 = the entry's place along the side; a virtual edge either of its two points).</summary>
    bool Crosses(int x, int y, int s)
    {
        if (Entry(x, y) is not { } e) return false;
        var key = e.Tile.Key.Rotate(e.Orientation);
        if (key.Edges[s] is not { } edge) return false;
        var type = files.Edge(edge);
        if (type is null || type.Wildcard) return false;
        var (w, h) = Size(e);
        int along = s switch { 0 => e.OffsetX, 1 => e.OffsetY, 2 => w - 1 - e.OffsetX, _ => h - 1 - e.OffsetY };
        if (key.PointA[s] / 3 == along) return true;
        return type.Virtual && key.PointB[s] / 3 == along;
    }

    /// <summary><c>0x142110d60</c>: with heights, the vertices on the highest level take the graph's first ground (+0xa0)
    /// and those on the lowest its second (+0xa8), where they have no ground written (<c>0x142110f70</c>; a first ground
    /// with the highest level 0 skips both; without a first ground the second needs a highest level of 1 or more).
    /// Then every vertex of the view with no ground written, or written with none, takes <paramref name="ground"/>
    /// (the graph's third ground, +0xb0).</summary>
    public void FillUnset(string? ground, string? high, string? low, (int Low, int High) levels)
    {
        if (grid.HasHeights)
        {
            if (high is not null)
            {
                if (levels.High != 0)
                {
                    Paint(levels.High, high);
                    if (low is not null) Paint(levels.Low, low);
                }
            }
            else if (low is not null && levels.High >= 1) Paint(levels.Low, low);
        }
        for (int y = 0; y <= height; y++)
            for (int x = 0; x <= width; x++)
            {
                ref var c = ref Corner(x, y);
                if ((c.Flags & 1) == 0 || c.Ground is null) grid.SetGround(x + offsetX, y + offsetY, ground);
            }
    }

    /// <summary><c>0x1420289a0</c> (the primary subgraph): the view's unwalkable part, vertex flag 8. From every border
    /// vertex inwards (<c>0x142029d30</c>, a vertex with a walkable ground — no flag 4, not
    /// <paramref name="impassable"/> — stops it) a breadth-first flood (<c>0x142029ab0</c>) gives vertices without a
    /// ground written the impassable ground, flags them and goes on straight or turning (<c>0x142029770</c>), never across
    /// a tile edge (<see cref="InTheWay"/>) nor into the interior of a tile with an AreaTransition
    /// (<paramref name="transition"/>); a walkable vertex it meets is flagged and its four neighbours with walkable
    /// grounds too (<c>0x142029990</c>). Then the border is flagged; every inner vertex already impassable starts a flood
    /// of its own (unless it touches a transition tile, <c>0x142027ee0</c>); inner walkable vertices beside a flagged one
    /// are flagged; a tile whose border is all flagged, without a tag (tile data +0x10), real edges or different corner grounds, is
    /// flagged whole (<c>0x142028510</c>). "No walkable area in the level" with fewer than max(10, w · h / 500) inner
    /// open vertices left.</summary>
    /// <summary><c>0x142028870</c>: flag 8 on the view's border vertices.</summary>
    public void FlagBorder()
    {
        for (int x = 0; x <= width; x++)
        {
            Corner(x, 0).Flags |= 8;
            Corner(x, height).Flags |= 8;
        }
        for (int y = 0; y <= height; y++)
        {
            Corner(0, y).Flags |= 8;
            Corner(width, y).Flags |= 8;
        }
    }

    public void Walkability(string impassable, Func<TileDef, bool> transition)
    {
        var visited = new bool[(width + 1) * (height + 1)];
        var queue = new Queue<(int X, int Y, int D)>();
        var met = new List<(int X, int Y)>();
        bool Walkable(string? g) => g is not null && (Flags(g) & 4) == 0 && g != impassable;
        bool InVertices(int x, int y) => x >= 0 && y >= 0 && x <= width && y <= height;
        bool Flagged(int x, int y) => (Corner(x, y).Flags & 8) != 0;
        void Flag(int x, int y)
        {
            if (InVertices(x, y)) Corner(x, y).Flags |= 8;
        }
        bool InsideTransition(int x, int y) => Interior(x, y) && Entry(x, y) is { } e && transition(e.Tile);
        // 0x142029770.
        void Step(int x, int y, int d)
        {
            int nx = x + Dir[d].X, ny = y + Dir[d].Y;
            if (!InVertices(nx, ny) || InsideTransition(nx, ny)) return;
            if (Walkable(Corner(nx, ny).Ground))
            {
                Flag(nx, ny);
                met.Add((nx, ny));
                return;
            }
            if (!InTheWay(x, y, nx, ny, d)) queue.Enqueue((nx, ny, d));
        }
        // 0x142029ab0.
        void Flood()
        {
            while (queue.Count > 0)
            {
                var (x, y, d) = queue.Dequeue();
                int i = y * (width + 1) + x;
                if (visited[i]) continue;
                visited[i] = true;
                ref var c = ref Corner(x, y);
                if (c.Ground is null || (c.Flags & 1) == 0) grid.SetGround(x + offsetX, y + offsetY, impassable);
                Flag(x, y);
                Step(x, y, d);
                Step(x, y, (d - 1) & 3);
                Step(x, y, (d + 1) & 3);
            }
            // 0x142029990.
            foreach (var (x, y) in met)
                foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                    if (InVertices(x + dx, y + dy) && Corner(x + dx, y + dy).Ground is { } g && (Flags(g) & 4) == 0)
                        Flag(x + dx, y + dy);
            met.Clear();
        }
        // 0x142029d30.
        void Seed(int x, int y, int d)
        {
            var g = Corner(x, y).Ground;
            if (g is not null && (Flags(g) & 4) == 0 && g != impassable) return;
            if (InsideTransition(x + Dir[d].X, y + Dir[d].Y)) return;
            queue.Enqueue((x, y, d));
            Flood();
        }
        for (int x = 0; x <= width; x++)
        {
            Seed(x, 0, 2);
            Seed(x, height, 0);
        }
        for (int y = 0; y <= height; y++)
        {
            Seed(0, y, 1);
            Seed(width, y, 3);
        }
        for (int x = 0; x <= width; x++)
        {
            Flag(x, 0);
            Flag(x, height);
        }
        for (int y = 0; y <= height; y++)
        {
            Flag(0, y);
            Flag(width, y);
        }
        // 0x142027ee0: one of the entries around the vertex holds a transition tile.
        bool TouchesTransition(int x, int y) =>
            new[] { (x, y), (x - 1, y), (x, y - 1), (x - 1, y - 1) }.Any(p => Entry(p.Item1, p.Item2) is { } e && transition(e.Tile));
        for (int y = 1; y < height; y++)
            for (int x = 1; x < width; x++)
            {
                if (Corner(x, y).Ground != impassable || Flagged(x, y) || TouchesTransition(x, y)) continue;
                Flag(x, y);
                visited[y * (width + 1) + x] = true;
                for (int d = 0; d < 4; d++) Step(x, y, d);
                Flood();
            }
        var beside = new List<(int X, int Y)>();
        for (int y = 1; y < height; y++)
            for (int x = 1; x < width; x++)
            {
                var g = Corner(x, y).Ground;
                if (g is null || (Flags(g) & 4) != 0 || Flagged(x, y)) continue;
                if (!Flagged(x, y - 1) && !Flagged(x - 1, y) && !Flagged(x + 1, y) && !Flagged(x, y + 1)) continue;
                beside.Add((x, y));
            }
        foreach (var (x, y) in beside) Flag(x, y);
        // 0x142028510 (with 0x142028380 and 0x14201d800).
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (Entry(x, y) is not { IsOrigin: true } e) continue;
                var (w, h) = Size(e);
                bool all = true;
                for (int i = 0; i <= w && all; i++) all = Flagged(x + i, y) && Flagged(x + i, y + h);
                for (int j = 1; j < h && all; j++) all = Flagged(x, y + j) && Flagged(x + w, y + j);
                if (!all) continue;
                if (transition(e.Tile)) throw new InvalidOperationException($"Area transition tile is inaccessible at ({x}, {y})");
                var key = e.Tile.Key;
                if (e.Tile.Tag.Length != 0 || key.Edges.Any(t => t is not null && files.Edge(t)?.Wildcard != true) ||
                    key.Grounds.Any(g => g != key.Grounds[0])) continue;
                for (int v = 0; v <= h; v++)
                    for (int u = 0; u <= w; u++) Flag(x + u, y + v);
            }
        // Master +0x165 with +0x230 marks more (not ported; no ported master sets it).
        int open = 0, need = Math.Max(10, width * height / 500);
        for (int y = 1; y < height; y++)
            for (int x = 1; x < width; x++)
            {
                var g = Corner(x, y).Ground;
                if ((g is null || (Flags(g) & 4) != 0) && !Flagged(x, y) && ++open >= need) return;
            }
        throw new InvalidOperationException("No walkable area in the level");
    }

    /// <summary><c>0x141fef240</c>, the fill object's tile listener (called after each tile that room tilising and the
    /// fill place, once <c>0x142116600</c> has stored the levels at +0x90 / +0x94, just before the overlays): with
    /// heights, a highest level other than 0 and a top or bottom ground, every vertex of the placed tile (view
    /// coordinates) with no ground or a flag-8 one takes the top ground at or above the highest level, else the bottom
    /// ground at or below the lowest.</summary>
    public Action<TileDef, int, int, int>? LevelPainter(string? high, string? low, (int Low, int High) levels)
    {
        if (!grid.HasHeights || levels.High == 0 || (high is null && low is null)) return null;
        return (tile, o, x, y) =>
        {
            var (w, h) = Orientation.Size(o, tile.Width, tile.Height);
            for (int v = 0; v <= h; v++)
                for (int u = 0; u <= w; u++)
                {
                    var g = Corner(x + u, y + v).Ground;
                    if (g is not null && (Flags(g) & 8) == 0) continue;
                    int at = grid.HeightAt(x + u + offsetX, y + v + offsetY);
                    if (at >= levels.High && high is not null) grid.SetGround(x + u + offsetX, y + v + offsetY, high);
                    else if (at <= levels.Low && low is not null) grid.SetGround(x + u + offsetX, y + v + offsetY, low);
                }
        };
    }

    void Paint(int level, string ground)
    {
        for (int y = 0; y <= height; y++)
            for (int x = 0; x <= width; x++)
            {
                if (grid.HeightAt(x + offsetX, y + offsetY) != level) continue;
                ref var c = ref Corner(x, y);
                if ((c.Flags & 1) == 0 || c.Ground is null) grid.SetGround(x + offsetX, y + offsetY, ground);
            }
    }

    /// <summary><c>0x141feed20</c>: each edge tile recorded for an EdgePoints edge type (<see cref="EdgePoint"/>)
    /// whose key has that type on exactly two sides: on each side without an edge, the neighbouring entry gets a fill
    /// tile for the side's ground (mode 2: the side's first corner; otherwise the type's first (mode 0) or second
    /// ground, when both corners of the side have it), unless the entry has a tile (<c>0x141fed640</c>, not ported).</summary>
    public void EdgePoints(IReadOnlyList<(int X, int Y, int Index)> records, IReadOnlyList<EdgePoint> points)
    {
        bool check = grid.CheckGrounds;
        grid.CheckGrounds = false;
        foreach (var (x, y, index) in records)
        {
            if (Entry(x, y) is not { } e) continue;
            var point = points[index];
            if (e.Tile.Key.Edges.Count(t => string.Equals(t, point.Edge, StringComparison.OrdinalIgnoreCase)) != 2) continue;
            var key = e.Tile.Key.Rotate(e.Orientation);
            var (w, h) = Size(e);
            (int X, int Y)[] next = [(0, -1), (w, 0), (0, h), (-1, 0)];
            var type = files.Edge(point.Edge);
            string? want = point.Mode == 0 ? type?.Ground1 : type?.Ground2;
            for (int s = 0; s < 4; s++)
            {
                if (key.Edges[s] is not null) continue;
                int nx = x + next[s].X, ny = y + next[s].Y;
                if (point.Mode != 2)
                {
                    if (key.Grounds[s] != want || key.Grounds[(s + 1) & 3] != want) continue;
                    if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) continue;
                }
                if (Entry(nx, ny) is null)
                    throw new NotSupportedException($"edge point fill at ({nx}, {ny}) next to {Path.GetFileName(e.Tile.Path)} o{e.Orientation} at ({x}, {y}) side {s} mode {point.Mode} for {point.Edge} (0x141fed640) is not ported");
            }
        }
        grid.CheckGrounds = check;
    }
}

/// <summary>One edge type of the FillTiles object's +0x140 list (master +0x218): the type and its mode.</summary>
public sealed record EdgePoint(string Edge, int Mode);
