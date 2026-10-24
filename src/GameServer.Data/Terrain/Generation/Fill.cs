namespace GameServer.Data.Terrain.Generation;

/// <summary>The fill of <c>0x142116600</c> (<c>0x141fef070</c>): every empty entry of the view, row by row, gets a fill
/// tile or fill room for the ground of its top-left vertex from the master's FillTiles (.gft, master +0x218: per ground
/// name, tiles and rooms with weights; no ground or one left out of keys is "default"), <c>0x141feb970</c>.</summary>
sealed class Fill(TerrainCache files, LevelRandom rng, TileGrid grid, int offsetX, int offsetY, int width, int height, TileStore store,
    Dictionary<string, List<(int Weight, string Path)>> fills, List<PlacedRoom> rooms, List<string> notes)
{
    /// <summary>A fill choice: a tile or a room, its orientation (8 = any) and weight (0x18 / 0x20-byte entries).</summary>
    sealed record Choice(TileDef? Tile, RoomDef? Room, int Orientation, int Weight);

    int Flags(string? g) => files.Ground(g)?.Flags ?? 0;

    /// <summary>The listeners <c>0x141feb970</c> / <c>0x141fe89e0</c> call after each tile (tile, orientation, view
    /// x, y).</summary>
    public Action<TileDef, int, int, int>? TilePlaced { get; init; }

    TileEntry? Entry(int x, int y) => grid[x + offsetX, y + offsetY];

    public void Run()
    {
        grid.CheckGrounds = false;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (Entry(x, y) is not null) continue;
                string? g = grid.Corner(x + offsetX, y + offsetY).Ground;
                // Param 5: the vertex has no ground or a flag-4 one, and is not unwalkable (vertex flag 8).
                bool open = (g is null || (Flags(g) & 4) != 0) && (grid.Corner(x + offsetX, y + offsetY).Flags & 8) == 0;
                x += FillEntry(x, y, g, open);
            }
        grid.CheckGrounds = true;
    }

    (List<Choice> Tiles, List<Choice> Rooms)? Group(string? ground)
    {
        if (ground is not null && (Flags(ground) & 0x10) != 0) ground = null;
        string name = ground is null ? "default" : files.Ground(ground)?.Name ?? "default";
        // 0x141fefa40: a ground without fill tiles or rooms of its own takes the default ones.
        if (!fills.TryGetValue(name, out var list) && !fills.TryGetValue("default", out list)) return null;
        var tiles = new List<Choice>();
        var rooms = new List<Choice>();
        foreach (var (weight, path) in list)
        {
            if (path.EndsWith(".arm", StringComparison.OrdinalIgnoreCase))
                rooms.Add(new Choice(null, files.Room(path) ?? throw new FileNotFoundException(path), 8, weight));
            else tiles.Add(new Choice(files.Tile(path) ?? throw new FileNotFoundException(path), null, 8, weight));
        }
        return (tiles, rooms);
    }

    static Choice Pick(List<Choice> list, int r)
    {
        foreach (var c in list)
        {
            if (r < c.Weight) return c;
            r -= c.Weight;
        }
        return list[^1];
    }

    /// <summary><c>0x141feb970</c>: returns how many more entries of the row the placed tile covers. A given height
    /// (the dungeon fill's, <c>0x1420f3de0</c>) is used as it is; without one (0x80000000) the area's corners give it
    /// (<c>0x141fec8f0</c>).</summary>
    internal int FillEntry(int x, int y, string? ground, bool open, short? height = null)
    {
        if (ground is not null && (Flags(ground) & 0x10) != 0) ground = null;
        var group = Group(ground) ?? throw new InvalidOperationException(
            $"No fill tiles or fill rooms for ground type \"{(ground is null ? "default" : files.Ground(ground)?.Name)}\" at ({x}, {y})");
        Choice? chosen = null;
        int o = 8;
        if (group.Rooms.Count == 0)
        {
            var list = group.Tiles;
            int total = list.Sum(c => c.Weight);
            var c = Pick(list, (int)rng.Next((uint)total));
            o = TileOrientation(c, x, y);
            if (o == 8)
            {
                list = [.. list];
                Without(list, c, ref total);
                while (list.Count > 0)
                {
                    c = Pick(list, (int)rng.Next((uint)total));
                    o = TileOrientation(c, x, y);
                    if (o != 8) break;
                    Without(list, c, ref total);
                }
            }
            if (o != 8) chosen = c;
        }
        else
        {
            // 0x142012f70 / 0x141fed2d0: the tiles, then the rooms (a room with named points only when open). A room with
            // a real edge or a corner ground that does not suit the vertex's (0x141d77670) is fatal.
            var list = new List<Choice>(group.Tiles.Where(c => c.Weight != 0));
            foreach (var c in group.Rooms)
            {
                var k = c.Room!.Key;
                if (k.Edges.Any(e => e is not null && files.Edge(e)?.Wildcard != true) || k.Grounds.Any(rg => Unsuited(rg, ground)))
                    throw new InvalidOperationException($"Fill room {c.Room.Path} is not compatable with ground type {ground ?? "(default)"}");
                if (!open && c.Room.PointCount != 0) continue;
                if (c.Weight != 0) list.Add(c);
            }
            int total = list.Sum(c => c.Weight);
            while (list.Count > 0)
            {
                var c = Pick(list, (int)rng.Next((uint)total));
                o = c.Tile is not null ? TileOrientation(c, x, y) : RoomOrientation(c.Room!, c.Orientation, x, y);
                if (o != 8) { chosen = c; break; }
                Without(list, c, ref total);
            }
        }
        if (chosen is null) throw new InvalidOperationException($"Failed to find suitable fill tile/room. ({x}, {y})");
        if (chosen.Tile is { } tile)
        {
            var (w, h) = Orientation.Size(o, tile.Width, tile.Height);
            grid.Place(tile, o, x + offsetX, y + offsetY, height ?? AreaHeight(x, y, w, h));
            TilePlaced?.Invoke(tile, o, x, y);
            return w - 1;
        }
        var room = chosen.Room!;
        var (rw, rh) = Orientation.Size(o, room.Width, room.Height);
        notes.Add($"fill room {Path.GetFileName(room.Path)} o{o} at ({x + offsetX}, {y + offsetY})");
        FillRoom(room, x, y, o, height ?? AreaHeight(x, y, rw, rh), ground, open);
        return Entry(x, y) is null ? FillEntry(x, y, ground, open, height) : 0;
    }

    /// <summary><c>0x141d77670</c>: two grounds that do not suit each other: different, unless either is null or flag 8
    /// against a null or flag-8 one, or both lack flags 1 and 4.</summary>
    bool Unsuited(string? a, string? b)
    {
        if (a == b) return false;
        bool Loose(string? g) => g is null || (Flags(g) & 8) != 0;
        if (Loose(a) && Loose(b)) return false;
        return !(a is not null && b is not null && (Flags(a) & 5) == 0 && (Flags(b) & 5) == 0);
    }

    static void Without(List<Choice> list, Choice c, ref int total)
    {
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i].Tile == c.Tile && list[i].Room == c.Room && list[i].Orientation == c.Orientation)
            {
                total -= list[i].Weight;
                list.RemoveAt(i);
            }
    }

    /// <summary><c>0x141fec8f0</c> with no height given: the middle (rounded up) of the heights at the area's four corner
    /// vertices that have one written (flag 2); 0 when none has.</summary>
    short AreaHeight(int x, int y, int w, int h)
    {
        var heights = new List<int>();
        foreach (var (vx, vy) in new[] { (x, y), (x + w, y), (x, y + h), (x + w, y + h) })
            if ((grid.Corner(vx + offsetX, vy + offsetY).Flags & 2) != 0) heights.Add(grid.HeightAt(vx + offsetX, vy + offsetY));
        if (heights.Count == 0) return 0;
        int lo = heights.Min(), hi = heights.Max();
        return (short)(hi - (hi - lo) / 2);
    }

    /// <summary><c>0x141fecd00</c>: a fixed orientation if the tile fits so; a square tile <c>Next(8)</c> when it fits;
    /// any other the orientations it fits in (even codes for its own size, odd for the turned one), <c>Next(840)</c>.</summary>
    int TileOrientation(Choice c, int x, int y)
    {
        var tile = c.Tile!;
        if (c.Orientation != 8) return TileFits(tile, x, y, c.Orientation) ? c.Orientation : 8;
        if (tile.Width == tile.Height) return TileFits(tile, x, y, 0) ? (int)rng.Next(8u) : 8;
        byte mask = (byte)((TileFits(tile, x, y, 0) ? 0x55 : 0) | (TileFits(tile, x, y, 1) ? 0xaa : 0));
        return mask == 0 ? 8 : LevelRandom.OrientationFromMask(mask, rng.Next(840u));
    }

    /// <summary><c>0x141fecc10</c> → <c>0x141ffbbe0</c>: within the view give or take one entry, and the map area is free
    /// (<c>0x141d84ed0</c> with the ground checks off; <c>0x141feb800</c> only looks at the entry itself, which is empty).</summary>
    bool TileFits(TileDef tile, int x, int y, int o)
    {
        var (w, h) = Orientation.Size(o, tile.Width, tile.Height);
        if (x < -1 || y < -1 || x + w > width + 1 || y + h > height + 1) return false;
        return AreaFree(x + offsetX, y + offsetY, w, h);
    }

    /// <summary><c>0x141d84e10</c>: inside the map and every entry empty.</summary>
    bool AreaFree(int x, int y, int w, int h)
    {
        if (x < 0 || y < 0 || x + w > grid.Width || y + h > grid.Height) return false;
        for (int v = 0; v < h; v++)
            for (int u = 0; u < w; u++)
                if (grid[x + u, y + v] is not null) return false;
        return true;
    }

    /// <summary><c>0x141fecf80</c>: a fixed orientation if the room fits so; a square room <c>Next(8)</c> when it fits;
    /// any other the orientations it fits in, <c>Next(840)</c>.</summary>
    int RoomOrientation(RoomDef room, int fixedO, int x, int y)
    {
        if (fixedO != 8) return RoomFits(room, x, y, fixedO) ? fixedO : 8;
        if (room.Width == room.Height) return RoomFits(room, x, y, 0) ? (int)rng.Next(8u) : 8;
        byte mask = (byte)((RoomFits(room, x, y, 0) ? 0x55 : 0) | (RoomFits(room, x, y, 1) ? 0xaa : 0));
        return mask == 0 ? 8 : LevelRandom.OrientationFromMask(mask, rng.Next(840u));
    }

    /// <summary><c>0x141fece90</c>: the turned room lies inside the view on free entries.</summary>
    bool RoomFits(RoomDef room, int x, int y, int o)
    {
        var (w, h) = Orientation.Size(o, room.Width, room.Height);
        return x < width && x + w <= width && y < height && y + h <= height && AreaFree(x + offsetX, y + offsetY, w, h);
    }

    // 0x141fea4d0's per-orientation shift of a tile in a slot by (1 − w, 1 − h).
    static readonly int[] SlotShift = [0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 1, 0, 1, 0, 0, 1, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0];

    static (int X, int Y) Shift(int o, int w, int h)
    {
        int so = Math.Min(o, 7) * 4;
        return ((1 - w) * SlotShift[so] + (1 - h) * SlotShift[so + 2], (1 - w) * SlotShift[so + 1] + (1 - h) * SlotShift[so + 3]);
    }

    static bool IsFillSlot(RoomSlot s) => s.Tag.Length == 0 && s.Key!.Edges.All(e => e is null) && s.Key.Grounds.Distinct().Count() == 1;

    bool Wild(string? g) => g is null || (Flags(g) & 8) != 0;

    /// <summary><c>0x141fe89e0</c>: a fill room's slots, column by column: a fill slot (no tag or edges, one ground) on an
    /// empty entry takes a fill tile for its ground that fits the map and the room's fill slots (<c>0x141fea4d0</c>,
    /// <c>0x141fe8660</c>), turned by the room, its vertices taking the slot's ground; any other slot (or a fill slot with
    /// no fitting tile) takes a tile from the TileSet for its key and tag (up to five draws until one fits). Then the
    /// room's own vertex grounds go in (<c>0x141ffbd70</c>) and every vertex of the room without a ground takes the fill
    /// ground.</summary>
    void FillRoom(RoomDef room, int x, int y, int o, short h0, string? fillGround, bool open)
    {
        bool check = grid.CheckGrounds;
        grid.CheckGrounds = false;
        for (int col = 0; col < room.Width; col++)
            for (int row = 0; row < room.Height; row++)
            {
                var slot = room.Slots[col, row];
                if (slot is null || slot.Kind is not ('k' or 'f' or 's') || slot.Key is null) continue;
                var key = slot.Key;
                var (gx, gy) = Orientation.Map(o, col + slot.ShiftX, row + slot.ShiftY, room.Width, room.Height);
                var (kw, kh) = (key.Width, key.Height);
                var (sx, sy) = Shift(o, kw, kh);
                int bx = x + gx + sx, by = y + gy + sy;
                short h = (short)(slot.Height + h0);
                if (IsFillSlot(slot))
                {
                    if (Entry(bx, by) is not null) continue;
                    string? g = key.Grounds[0];
                    if (Group(g) is { } group && group.Tiles.Count != 0)
                    {
                        var (tile, to) = FillSlotTile(group.Tiles, room, col, row, o, x + gx, y + gy);
                        if (tile is not null)
                        {
                            int placed = DungeonTiler.Compose(to, o);
                            var (tw, th) = Orientation.Size(to, tile.Width, tile.Height);
                            var (tx, ty) = Shift(o, tw, th);
                            int px = x + gx + tx, py = y + gy + ty;
                            grid.Place(tile, placed, px + offsetX, py + offsetY, h);
                            TilePlaced?.Invoke(tile, placed, px, py);
                            for (int v = 0; v <= th; v++)
                                for (int u = 0; u <= tw; u++)
                                {
                                    ref var c = ref grid.Corner(px + u + offsetX, py + v + offsetY);
                                    if ((c.Flags & 1) == 0 || c.Ground == g || (Wild(c.Ground) && Wild(g)))
                                        grid.SetGround(px + u + offsetX, py + v + offsetY, g);
                                }
                            row += th - 1;
                            continue;
                        }
                    }
                }
                var list = store.Candidates(slot.Tag, key.Rotate(o));
                if (list.Count == 0)
                    throw new NotSupportedException($"{room.Path}: slot ({col}, {row}) tag '{slot.Tag}' has no tile in the tile set (mission tiles and the second store are not ported)");
                if (Entry(bx, by) is not null) continue;
                (TileDef Tile, int Orientation, int Weight)? pick = null;
                for (int tries = 0; tries <= 4 && pick is null; tries++)
                {
                    int r = (int)rng.Next((uint)list.Sum(c => c.Weight));
                    var c = list[^1];
                    foreach (var e in list)
                    {
                        if (r < e.Weight) { c = e; break; }
                        r -= e.Weight;
                    }
                    if (TileFits(c.Tile, bx, by, c.Orientation)) pick = c;
                }
                if (pick is not { } p) throw new InvalidOperationException($"No tile could fit at ({bx}, {by}) for the fill room here");
                grid.Place(p.Tile, p.Orientation, bx + offsetX, by + offsetY, h);
                TilePlaced?.Invoke(p.Tile, p.Orientation, bx, by);
            }
        var (rw, rh) = Orientation.Size(o, room.Width, room.Height);
        rooms.Add(new PlacedRoom(room, x + offsetX, y + offsetY, rw, rh, [], o));
        // 0x141ffbd70: the room's own inner vertex grounds (the .arm's), turned with it, before the fill ground.
        if (room.VertexGrounds.Length > 0)
            for (int v = 1; v < room.Height; v++)
                for (int u = 1; u < room.Width; u++)
                    if (room.VertexGrounds[(v - 1) * (room.Width - 1) + u - 1] is { } vg)
                    {
                        var (dx, dy) = Orientation.MapVertex(o, u, v, room.Width, room.Height);
                        grid.ForceGround(x + dx + offsetX, y + dy + offsetY, vg);
                    }
        for (int v = 0; v <= rh; v++)
            for (int u = 0; u <= rw; u++)
                if (grid.Corner(x + u + offsetX, y + v + offsetY).Ground is null) grid.SetGround(x + u + offsetX, y + v + offsetY, fillGround);
        grid.CheckGrounds = check;
    }

    /// <summary>The fill tile of a fill slot (<c>0x141fe89e0</c> with <c>0x141fea670</c>): <c>Next(total)</c>, its
    /// orientation (relative to the room) as for a fill tile, retried without the failed choice.</summary>
    (TileDef? Tile, int Orientation) FillSlotTile(List<Choice> tiles, RoomDef room, int col, int row, int o, int sx, int sy)
    {
        int Orient(Choice c)
        {
            var t = c.Tile!;
            if (c.Orientation != 8) return SlotFits(t, c.Orientation, room, col, row, o, sx, sy) ? c.Orientation : 8;
            if (t.Width == t.Height) return SlotFits(t, 0, room, col, row, o, sx, sy) ? (int)rng.Next(8u) : 8;
            byte mask = (byte)((SlotFits(t, 0, room, col, row, o, sx, sy) ? 0x55 : 0) | (SlotFits(t, 1, room, col, row, o, sx, sy) ? 0xaa : 0));
            return mask == 0 ? 8 : LevelRandom.OrientationFromMask(mask, rng.Next(840u));
        }
        var list = tiles;
        int total = list.Sum(c => c.Weight);
        var first = Pick(list, (int)rng.Next((uint)total));
        int to = Orient(first);
        if (to != 8) return (first.Tile, to);
        list = [.. list];
        Without(list, first, ref total);
        while (list.Count > 0)
        {
            var c = Pick(list, (int)rng.Next((uint)total));
            to = Orient(c);
            if (to != 8) return (c.Tile, to);
            Without(list, c, ref total);
        }
        return (null, 8);
    }

    /// <summary><c>0x141fea4d0</c>: the tile turned by its orientation and the room's fits the map at the slot (shifted
    /// for its size, <c>0x141ffbbe0</c>) and covers only fill slots of the room with its ground and height
    /// (<c>0x141fe8660</c>).</summary>
    bool SlotFits(TileDef tile, int to, RoomDef room, int col, int row, int o, int sx, int sy)
    {
        var (tw, th) = Orientation.Size(to, tile.Width, tile.Height);
        var (dx, dy) = Shift(o, tw, th);
        if (!TileFits(tile, sx + dx, sy + dy, DungeonTiler.Compose(to, o))) return false;
        if (col + tw > room.Width || row + th > room.Height) return false;
        var first = room.Slots[col, row];
        if (first is null || first.Kind == 'n') return false;
        string? tg = tile.VertexGround(0, 0);
        for (int j = 0; j < th; j++)
            for (int i = 0; i < tw; i++)
            {
                var s = room.Slots[col + i, row + j];
                if (s is null || s.Kind == 'n' || s.Key is null) return false;
                string? g = s.Key.Grounds[0];
                if (g != tg && !(Wild(g) && Wild(tg))) return false;
                if (!IsFillSlot(s) || s.Height != first.Height) return false;
            }
        return true;
    }
}
