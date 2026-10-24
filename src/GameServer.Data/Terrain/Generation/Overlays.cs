namespace GameServer.Data.Terrain.Generation;

/// <summary>A pass of a master's Overlays file (.toy, <c>0x141ffca30</c>): which overlay passes run it (bits 1 and 2,
/// default both; leading "0" / "1" words), the order (Random 0, FileOrder 1, SizeOrder 2) and its groups: each
/// room line in each allowed orientation adds its room's key turned by it to the group with that key and overlay
/// flags (0x78-byte records: key, flags +0x4e, rooms +0x50, total weight +0x70).</summary>
sealed record OverlayPass(byte Passes, int Order, List<OverlayGroup> Groups);

sealed record OverlayGroup(TileKey Key, byte Flags, List<OverlayRoom> Rooms)
{
    public int Weight => Rooms.Sum(r => r.Weight);
}

/// <summary>One room of a group (0x30 bytes): the room, its limit ("limit=", +8), the orientations under which its key
/// is the group's (+0xa, a mask) and the line's weight (+0x28).</summary>
sealed record OverlayRoom(RoomDef Room, int Limit, byte OrientationMask, int Weight);

/// <summary>The overlays of <c>0x142116600</c> (<c>0x141ffe240</c> sets up, <c>0x141fff1a0</c> runs a pass): rooms
/// whose one key matches the map at a spot (its corner grounds and heights, <c>0x141ffa7b0</c>) replace the tiles
/// there.</summary>
sealed class Overlays
{
    // 0x142fbfd60: the overlay flag names (bit i). The exclusion grid uses the same bits: a cell with bit i set takes
    // only overlays whose flags have bit i.
    static readonly string[] FlagNames =
        ["OverlayTileOverlays", "OverlayFeatureTiles", "OverlayFeatureRooms", "OverlayFeatureRoomOverlap",
         "OverlayDescriptionNodes", "OverlayExtraFeatures", "MatchAllTileKeys"];

    readonly TerrainCache _files;
    readonly LevelRandom _rng;
    readonly List<string> _notes;
    readonly TileGrid _grid;
    readonly int _offsetX, _offsetY, _width, _height;
    readonly SpecialNodes _special;
    readonly byte[] _excluded;
    readonly Dictionary<OverlayRoom, int> _placed = [];

    public Overlays(TerrainCache files, LevelRandom rng, List<string> notes, TileGrid grid, int offsetX, int offsetY, int width, int height,
        SpecialNodes special, IReadOnlyList<PlacedRoom> rooms, int roomOverlap)
    {
        (_files, _rng, _notes, _grid, _offsetX, _offsetY, _width, _height, _special) = (files, rng, notes, grid, offsetX, offsetY, width, height, special);
        // 0x141ffe240: rooms of nodes with descriptions 0x10; rooms 4 (shrunk by the master's RoomOverlap when the room's
        // own overlap, room data +1, is the same) and 8; tiles with a tag 2; extra features 0x20 (none in the ported areas).
        _excluded = new byte[width * height];
        foreach (var r in rooms)
            if (r.Descriptions.Count != 0) Mark(r.X, r.Y, r.X + r.Width, r.Y + r.Height, 0x10);
        foreach (var r in rooms)
        {
            if (r.Room.Overlap == roomOverlap)
            {
                int ov = roomOverlap;
                Mark(r.X + ov, r.Y + ov, Math.Max(r.X + ov, r.X + r.Width - ov), Math.Max(r.Y + ov, r.Y + r.Height - ov), 4);
            }
            else Mark(r.X, r.Y, r.X + r.Width, r.Y + r.Height, 4);
            Mark(r.X, r.Y, r.X + r.Width, r.Y + r.Height, 8);
        }
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (Entry(x, y) is { IsOrigin: true } e && e.Tile.Tag.Length != 0)
                {
                    var (w, h) = Size(e);
                    Mark(x + offsetX, y + offsetY, x + offsetX + w, y + offsetY + h, 2);
                }
    }

    TileEntry? Entry(int x, int y) => (uint)x < (uint)_width && (uint)y < (uint)_height ? _grid[x + _offsetX, y + _offsetY] : null;

    static (int W, int H) Size(TileEntry e) => Orientation.Size(e.Orientation, e.Tile.Width, e.Tile.Height);

    int Flags(string? g) => _files.Ground(g)?.Flags ?? 0;

    /// <summary><c>0x141ffe650</c>: a rectangle of map entries, clamped to the view, gets a bit.</summary>
    void Mark(int x0, int y0, int x1, int y1, byte bit)
    {
        x0 = Math.Clamp(x0 - _offsetX, 0, _width); x1 = Math.Clamp(x1 - _offsetX, 0, _width);
        y0 = Math.Clamp(y0 - _offsetY, 0, _height); y1 = Math.Clamp(y1 - _offsetY, 0, _height);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++) _excluded[y * _width + x] |= bit;
    }

    /// <summary>Parses a .toy file: "version", then pass lines, each followed by its "weight "path" args" room lines.</summary>
    public static List<OverlayPass> Parse(TerrainCache files, string path)
    {
        var passes = new List<OverlayPass>();
        string text = files.Text(path) ?? throw new FileNotFoundException(path);
        List<(int Weight, string Path, List<string> Args)>? rooms = null;
        List<string>? header = null;
        void Flush()
        {
            if (header is not null) passes.Add(BuildPass(files, header, rooms!));
        }
        foreach (var line in TerrainText.Lines(text))
        {
            var t = TerrainText.Tokenize(line);
            if (t.Count == 0 || t[0].Text == "version") continue;
            int q = t.FindIndex(x => x.Quoted);
            if (q == 1 && t[0].IsNumber)
            {
                if (rooms is null) throw new InvalidOperationException($"{path}: a room line before the first pass");
                rooms.Add((t[0].Int, TerrainText.Resolve(path, t[1].Text), [.. t.Skip(2).Select(x => x.Text)]));
                continue;
            }
            Flush();
            header = [.. t.Select(x => x.Text)];
            rooms = [];
        }
        Flush();
        return passes;
    }

    /// <summary><c>0x141ffca30</c> for one pass.</summary>
    static OverlayPass BuildPass(TerrainCache files, List<string> header, List<(int Weight, string Path, List<string> Args)> lines)
    {
        byte passes = 3, flags = 0x18;
        int order = 0, bit = 0;
        foreach (var word in header)
        {
            if (bit < 3 && word is "0" or "1")
            {
                passes = (byte)(word == "1" ? passes | (1 << bit) : passes & ~(1 << bit));
                bit++;
                continue;
            }
            if (word is "SizeOrder") order = 2;
            else if (word is "FileOrder") order = 1;
            else if (word is "Random") order = 0;
            else if (word.Contains("SuppressUnderlying")) continue;
            else if (word.Length > 1 && word[0] is '+' or '-') flags = Flag(flags, word);
            else throw new InvalidOperationException($"Unknown overlay pass argument: \"{word}\"");
        }
        var groups = new List<OverlayGroup>();
        foreach (var (weight, path, args) in lines)
        {
            var room = files.Room(path) ?? throw new FileNotFoundException(path);
            byte roomFlags = flags, mask = 0;
            int limit = 0;
            foreach (var word in args)
            {
                if (word.Contains("SuppressUnderlying")) continue;
                if (word.StartsWith("limit=", StringComparison.Ordinal)) { limit = Math.Min(255, int.Parse(word[6..])); continue; }
                if (word.Length > 1 && word[0] is '+' or '-') { roomFlags = Flag(roomFlags, word); continue; }
                int o = Orientation.Parse(word);
                if (o < 8) { mask |= (byte)(1 << o); continue; }
                throw new NotSupportedException($"overlay room argument \"{word}\" (required=, conditions) is not ported");
            }
            if (mask == 0) mask = 0xff;
            if (weight == 0) continue;
            for (int o = 0; o < 8; o++)
            {
                if ((mask & (1 << o)) == 0) continue;
                var key = room.Key.Rotate(o);
                var group = groups.FirstOrDefault(g => g.Flags == roomFlags && g.Key.Equals(key));
                if (group is null) groups.Add(group = new OverlayGroup(key, roomFlags, []));
                // The same room under another orientation with this key only adds the orientation.
                int at = group.Rooms.FindIndex(r => r.Room == room);
                if (at >= 0) group.Rooms[at] = group.Rooms[at] with { OrientationMask = (byte)(group.Rooms[at].OrientationMask | 1 << o) };
                else group.Rooms.Add(new OverlayRoom(room, limit, (byte)(1 << o), weight));
            }
        }
        if (order == 2) throw new NotSupportedException("overlay SizeOrder is not ported");
        return new OverlayPass(passes, order, groups);
    }

    // 0x141ffc8a0.
    static byte Flag(byte flags, string word)
    {
        int i = Array.IndexOf(FlagNames, word[1..]);
        if (i < 0) throw new InvalidOperationException($"Unknown OverlayFlag: \"{word}\"");
        return (byte)(word[0] == '-' ? flags & ~(1 << i) : flags | (1 << i));
    }

    /// <summary><c>0x141fff290</c>: a pass: with FileOrder each group's spots are found and placed in turn; with Random all
    /// groups' spots together.</summary>
    public void Run(OverlayPass pass, bool afterFill)
    {
        if (pass.Groups.Count == 0) return;
        if (pass.Order == 0)
        {
            // Random: one list of spots for every group in file order, then one draw sequence over it.
            var pooled = new List<Spot>();
            foreach (var g in pass.Groups) Spots(g, pooled);
            Place(pooled, afterFill);
            return;
        }
        foreach (var g in pass.Groups)
        {
            var spots = new List<Spot>();
            Spots(g, spots);
            Place(spots, afterFill);
        }
    }

    sealed class Spot(int x, int y)
    {
        public readonly int X = x, Y = y;
        public int Weight;
        public readonly List<OverlayGroup> Groups = [];
    }

    /// <summary><c>0x141fff5f0</c>: every view position (every third one, both ways, when the master's +0x90 bit 1 is off
    /// and an edge of the key has size 3) where the group fits, sorted by (x, y) (<c>0x141fff840</c>).</summary>
    void Spots(OverlayGroup g, List<Spot> spots)
    {
        if (g.Rooms.Count == 0) return;
        void Add(int x, int y)
        {
            if (!Fits(g, x, y, g.Flags)) return;
            int i = spots.FindIndex(s => s.X * 0x10000 + s.Y >= x * 0x10000 + y);
            if (i < 0 || spots[i].X != x || spots[i].Y != y) spots.Insert(i < 0 ? spots.Count : i, new Spot(x, y));
            var spot = spots[i < 0 ? spots.Count - 1 : i];
            spot.Weight += g.Weight;
            spot.Groups.Add(g);
        }
        if (g.Key.Edges.Any(e => e is not null && _files.Edge(e)?.Size == 3))
        {
            for (int y = 0; y <= _height - 3; y += 3)
                for (int x = 0; x <= _width - 3; x += 3) Add(x, y);
            return;
        }
        for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++) Add(x, y);
    }

    /// <summary><c>0x141fffb70</c>: spots are drawn by weight until none is left; at each, a group that still fits
    /// (<c>Next(total)</c>), then one of its rooms under its limit, padded with nothing to 100 (<c>Next</c>), and its
    /// orientation (<c>Next(840)</c>, <c>0x14299da50</c>). The tiles under the spot leave, the spot's vertices lose their
    /// grounds and the room is tilised (<c>0x141fea780</c>); the spot is closed to later overlays (bit 1) either way.</summary>
    void Place(List<Spot> spots, bool afterFill)
    {
        int total = spots.Sum(s => s.Weight);
        while (spots.Count > 0)
        {
            int r = (int)_rng.Next((uint)total);
            int i = 0;
            while (i < spots.Count - 1 && spots[i].Weight <= r) r -= spots[i++].Weight;
            var spot = spots[i];
            var fits = spot.Groups.Where(g => g.Weight != 0 && Fits(g, spot.X, spot.Y, g.Flags)).ToList();
            total -= spot.Weight;
            spots.RemoveAt(i);
            if (fits.Count == 0) continue;
            var group = Pick(fits, g => g.Weight, (int)_rng.Next((uint)fits.Sum(g => g.Weight)));
            int kw = group.Key.Width, kh = group.Key.Height;
            var rooms = new List<OverlayRoom?>();
            foreach (var room in group.Rooms)
            {
                if (room.Limit != 0 && _placed.GetValueOrDefault(room) >= room.Limit) continue;
                if ((group.Flags & 0x40) != 0) throw new NotSupportedException("MatchAllTileKeys overlays (0x142000f60) are not ported");
                if (room.Weight != 0) rooms.Add(room);
            }
            if (rooms.Count == 0) continue;
            int sum = rooms.Sum(x => x!.Weight);
            var weights = rooms.Select(x => x!.Weight).ToList();
            if (sum < 100) { rooms.Add(null); weights.Add(100 - sum); sum = 100; }
            int pr = (int)_rng.Next((uint)sum), k = 0;
            while (k < rooms.Count - 1 && pr >= weights[k]) pr -= weights[k++];
            var chosen = rooms[k];
            if (chosen is null)
            {
                Mark(spot.X + _offsetX, spot.Y + _offsetY, spot.X + _offsetX + kw, spot.Y + _offsetY + kh, 1);
                continue;
            }
            int o = LevelRandom.OrientationFromMask(chosen.OrientationMask, _rng.Next(840u));
            if (!Height(chosen.Room, spot.X, spot.Y, o)) continue;
            if (afterFill) throw new NotSupportedException("overlays after the fill (0x141fffb70 pass 2) are not ported");
            for (int y = spot.Y; y < spot.Y + kh; y++)
                for (int x = spot.X; x < spot.X + kw; x++) _grid.Remove(x + _offsetX, y + _offsetY);
            for (int v = 0; v <= kh; v++)
                for (int u = 0; u <= kw; u++)
                {
                    ref var c = ref _grid.Corner(spot.X + u + _offsetX, spot.Y + v + _offsetY);
                    c.Ground = null;
                    c.Flags &= unchecked((byte)~1);
                }
            _special.TiliseRoom(chosen.Room, spot.X, spot.Y, o, fillGrounds: true, []);
            Mark(spot.X + _offsetX, spot.Y + _offsetY, spot.X + _offsetX + kw, spot.Y + _offsetY + kh, 1);
            _placed[chosen] = _placed.GetValueOrDefault(chosen) + 1;
            _notes.Add($"overlay {Path.GetFileName(chosen.Room.Path)} o{o} at ({spot.X + _offsetX}, {spot.Y + _offsetY})");
        }
    }

    static T Pick<T>(List<T> list, Func<T, int> weight, int r)
    {
        foreach (var x in list)
        {
            if (r < weight(x)) return x;
            r -= weight(x);
        }
        return list[^1];
    }

    /// <summary><c>0x141ffe7e0</c>: the room's height offset from the map's vertex heights under its border slots. Only
    /// the all-zero case is ported (offset 0).</summary>
    bool Height(RoomDef room, int x, int y, int o)
    {
        for (int v = 0; v <= room.Height; v++)
            for (int u = 0; u <= room.Width; u++)
            {
                var (dx, dy) = Orientation.MapVertex(o, u, v, room.Width, room.Height);
                if (_grid.Corner(x + dx + _offsetX, y + dy + _offsetY).Height != 0)
                    throw new NotSupportedException("overlay height offsets (0x141ffe7e0) are not ported");
            }
        return true;
    }

    /// <summary><c>0x141ffef60</c>: the key fits at view position (x, y): inside the view, no cell closed to the flags,
    /// the area holds tiles that all lie inside it (<c>0x141d850b0</c>) and the map matches the key there
    /// (<c>0x141ffa7b0</c>).</summary>
    bool Fits(OverlayGroup g, int x, int y, byte flags)
    {
        int kw = g.Key.Width, kh = g.Key.Height;
        if ((uint)x >= (uint)_width || (uint)y >= (uint)_height || (uint)(x + kw - 1) >= (uint)_width || (uint)(y + kh - 1) >= (uint)_height) return false;
        for (int v = y; v < y + kh; v++)
            for (int u = x; u < x + kw; u++)
                if ((_excluded[v * _width + u] & ~flags) != 0) return false;
        int ax = x + _offsetX, ay = y + _offsetY;
        if (ax + kw > _grid.Width || ay + kh > _grid.Height) return false;
        return TilesInside(ax, ay, kw, kh) && KeyMatch(ax, ay, g.Key) == 0;
    }

    bool TilesInside(int x0, int y0, int w, int h)
    {
        bool any = false;
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (_grid[x, y] is not { } e) continue;
                any = true;
                int ox = x - e.OffsetX, oy = y - e.OffsetY;
                var (tw, th) = Size(e);
                if (ox < x0 || oy < y0 || ox + tw > x0 + w || oy + th > y0 + h) return false;
            }
        return any;
    }

    /// <summary><c>0x141ffa7b0</c> (with param 4 and 9 at 0): 0 when the key fits at map entry (x, y): the four corner
    /// grounds match (<c>0x141ffa470</c>; not all four only "different"), the height differences at the corners whose
    /// grounds take heights agree, and every side inside the map matches the edges across it (<c>0x141ffa390</c>).</summary>
    int KeyMatch(int x, int y, TileKey key)
    {
        int w = key.Width, h = key.Height;
        if (x < 0 || y < 0 || x + w > _grid.Width || y + h > _grid.Height) return 5;
        (int X, int Y)[] at = [(x, y), (x + w, y), (x + w, y + h), (x, y + h)];
        int different = 0;
        for (int c = 0; c < 4; c++)
            if (!CornerMatch(_grid.Corner(at[c].X, at[c].Y).Ground, key.Grounds[c], ref different)) return 6 + c;
        if (different == 4) return 6;
        var diffs = new SortedSet<int>();
        for (int c = 0; c < 4; c++)
        {
            ref var corner = ref _grid.Corner(at[c].X, at[c].Y);
            bool keyOpen = key.Grounds[c] is not { } kg || (Flags(kg) & 1) != 0;
            bool mapOpen = corner.Ground is not { } mg || (Flags(mg) & 1) != 0;
            if (keyOpen && mapOpen) diffs.Add(key.Heights[c] - corner.Height);
        }
        if (diffs.Count >= 2) return 10;
        if (0 < y && !SideMatch(x, y, key, 0)) return 1;
        if (0 < x && !SideMatch(x, y, key, 3)) return 4;
        if (x + w < _grid.Width && !SideMatch(x, y, key, 1)) return 2;
        if (y + h < _grid.Height && !SideMatch(x, y, key, 2)) return 3;
        return 0;
    }

    /// <summary><c>0x141ffa470</c>: the map's ground at a corner against the key's (grounds with flag 0x10 count as none):
    /// equal, or a flag-8 ground on the map, or one on the key over a map ground, or the map ground has flag 0x100, fit;
    /// two grounds that both lack flags 1 and 4 fit but count as different.</summary>
    bool CornerMatch(string? map, string? key, ref int different)
    {
        if (map is not null && (Flags(map) & 0x10) != 0) map = null;
        if (key is not null && (Flags(key) & 0x10) != 0) key = null;
        if (map == key) return true;
        if (map is null) return false;
        if ((Flags(map) & 8) != 0 && (key is null || (Flags(key) & 8) != 0)) return true;
        if ((Flags(map) & 0x100) != 0) return true;
        if ((Flags(map) & 5) == 0 && key is not null && (Flags(key) & 5) == 0)
        {
            different++;
            return true;
        }
        return false;
    }

    /// <summary><c>0x141ffa390</c>: a side without an edge fits when no edge of a tile outside crosses into it
    /// (<c>0x141ff9cb0</c>); a side with an edge (not a wildcard) fits when the tile outside has a matching edge at the
    /// key's edge point (<c>0x141ff9ee0</c>, both points of a virtual edge).</summary>
    bool SideMatch(int x, int y, TileKey key, int side)
    {
        if (key.Edges[side] is not { } edge) return !CrossesInto(x, y, key, side);
        var type = _files.Edge(edge) ?? throw new FileNotFoundException(edge);
        if (type.Wildcard) return true;
        if (!EdgeMeets(x, y, key, side, false)) return false;
        return !type.Virtual || EdgeMeets(x, y, key, side, true);
    }

    // The entry just outside side s of a w × h area at (x, y), i along the side.
    static (int X, int Y) Outside(int x, int y, int w, int h, int s, int i) => s switch
    {
        0 => (x + i, y - 1),
        1 => (x + w, y + i),
        2 => (x + i, y + h),
        _ => (x - 1, y + i),
    };

    /// <summary><c>0x141ff98c0</c>: the edge on map side s of the tile at entry e, with its points along the side in
    /// map order (left to right, top to bottom).</summary>
    (EdgeType? Type, int A, int B) EdgeOn(TileEntry e, int s)
    {
        var key = e.Tile.Key.Rotate(e.Orientation);
        if (key.Edges[s] is not { } edge) return (null, 0, 0);
        var type = _files.Edge(edge);
        int len = 3 * ((s & 1) == 0 ? key.Width : key.Height);
        int a = key.PointA[s], b = type?.Virtual == true ? key.PointB[s] : 0;
        if (s > 1) { a = len - a - 1; if (type?.Virtual == true) b = len - b - 1; }
        return (type, a, b);
    }

    /// <summary><c>0x141ff9cb0</c>: an edge (not a wildcard or flag 0x10) of a tile just outside side s crosses into the
    /// span of the side.</summary>
    bool CrossesInto(int x, int y, TileKey key, int side)
    {
        int len = (side & 1) == 0 ? key.Width : key.Height, start = (side & 1) == 0 ? x : y;
        for (int i = 0; i < len; i++)
        {
            var (nx, ny) = Outside(x, y, key.Width, key.Height, side, i);
            if (!_grid.Inside(nx, ny)) return false;
            if (_grid[nx, ny] is not { } e) continue;
            var (type, a, b) = EdgeOn(e, (side + 2) & 3);
            if (type is null || type.Wildcard || type.CanEnd) continue;
            int origin = (side & 1) == 0 ? nx - e.OffsetX : ny - e.OffsetY;
            int pa = a / 3 + origin;
            if (pa >= start && pa < start + len) return true;
            if (type.Virtual && b / 3 + origin >= start && b / 3 + origin < start + len) return true;
        }
        return false;
    }

    /// <summary><c>0x141ff9ee0</c>: the tile just outside side s at the key's edge point (A, or B when
    /// <paramref name="second"/>) has an edge meeting it at the same point, of a matching type (<c>0x141ff9af0</c>), or
    /// has no edge there while the key's type has flag 0x10; outside the map only the flag counts. Then no other tile
    /// outside the side (besides the one at the other point of a virtual edge) may have an edge entering its span.</summary>
    bool EdgeMeets(int x, int y, TileKey key, int side, bool second)
    {
        int len = (side & 1) == 0 ? key.Width : key.Height, start = (side & 1) == 0 ? x : y;
        int Absolute(int point) => side > 1 ? 3 * len - point - 1 : point;
        int p = Absolute(second ? key.PointB[side] : key.PointA[side]);
        var mine = _files.Edge(key.Edges[side])!;
        var (nx, ny) = Outside(x, y, key.Width, key.Height, side, p / 3);
        if (!_grid.Inside(nx, ny)) return mine.CanEnd;
        if (_grid[nx, ny] is not { } e) return false;
        int Along(TileEntry t, int cx, int cy) => (side & 1) == 0 ? cx - t.OffsetX : cy - t.OffsetY;
        var (theirs, a, b) = EdgeOn(e, (side + 2) & 3);
        bool ok;
        if (theirs is null) ok = mine.CanEnd;
        else if (theirs.Wildcard) ok = false;
        else
        {
            int along = Along(e, nx, ny), at = p + 3 * start;
            if (a + 3 * along != at && !(theirs.Virtual && b + 3 * along == at)) return false;
            ok = SameType(theirs, mine);
        }
        if (!ok) return false;
        var matched = new List<(int X, int Y)> { (nx - e.OffsetX, ny - e.OffsetY) };
        if (mine.Virtual)
        {
            int q = Absolute(second ? key.PointA[side] : key.PointB[side]);
            var (qx, qy) = Outside(x, y, key.Width, key.Height, side, q / 3);
            if (_grid.Inside(qx, qy) && _grid[qx, qy] is { } eq) matched.Add((qx - eq.OffsetX, qy - eq.OffsetY));
        }
        for (int i = 0; i < len; i++)
        {
            var (cx, cy) = Outside(x, y, key.Width, key.Height, side, i);
            if (!_grid.Inside(cx, cy) || _grid[cx, cy] is not { } t) continue;
            if (matched.Contains((cx - t.OffsetX, cy - t.OffsetY))) continue;
            var (other, oa, ob) = EdgeOn(t, (side + 2) & 3);
            if (other is null || other.Wildcard) continue;
            int origin = Along(t, cx, cy);
            int ia = oa / 3 - start + origin, ib = ob / 3 - start + origin;
            if ((ia >= 0 && ia < len) || (other.Virtual && ib >= 0 && ib < len)) return false;
        }
        return true;
    }

    /// <summary><c>0x141ff9af0</c>: two edge types match when equal, or one is virtual and the other is one of its two
    /// sub-edges, or both are virtual and share a sub-edge.</summary>
    static bool SameType(EdgeType a, EdgeType b)
    {
        static bool Is(string? x, EdgeType t) => x is not null && string.Equals(x, t.Path, StringComparison.OrdinalIgnoreCase);
        if (!a.Virtual && !b.Virtual) return string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
        if (!a.Virtual) return Is(b.SubEdges[0], a) || Is(b.SubEdges[1], a);
        if (!b.Virtual) return Is(a.SubEdges[0], b) || Is(a.SubEdges[1], b);
        return a.SubEdges.Any(s => s is not null && b.SubEdges.Any(t => string.Equals(s, t, StringComparison.OrdinalIgnoreCase)));
    }
}
