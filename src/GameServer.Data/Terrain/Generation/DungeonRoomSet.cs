namespace GameServer.Data.Terrain.Generation;

/// <summary>One exit of a dungeon room: a unit side's door type (an .et, null = no door), height and chirality. A
/// room of w × h units has 2(w + h) exits, clockwise from the top-left: top left to right, right top to bottom, bottom
/// right to left, left bottom to top.</summary>
public readonly record struct RoomExit(string? Type, sbyte Height, bool Chirality);

/// <summary>A room (.arm) as the dungeon generator sees it (<c>0x142a0f3a0</c>): unit size, size in units, tag,
/// uniqueness and exits, with its RoomSet weight and orientation mask, and the parsed slots for tilising.</summary>
public sealed record DungeonRoom(string Path, int Unit, int Width, int Height, string Tag, bool Unique, RoomExit[] Exits,
    int Weight, int Mask, RoomDef? Def)
{
    /// <summary>The .arm reader's special flag (room data +8, <c>0x142a0f3a0</c>): the tag starts with "special".</summary>
    public bool Special => Tag.StartsWith("special", StringComparison.Ordinal);

    /// <summary>Room data +4: the number after "special" (its list at room set +0x1c0), else −1.</summary>
    public int Number => Special && !Tag.StartsWith("specialchance", StringComparison.Ordinal) && Tag.Length > 7 &&
        int.TryParse(Tag.AsSpan(7), out int n) ? n : -1;

    /// <summary>Room data +5: the chance in percent of a "specialchance…" room (the number after it), else 100.</summary>
    public int Chance => Tag.StartsWith("specialchance", StringComparison.Ordinal) && Tag.Length > 13 &&
        int.TryParse(Tag.AsSpan(13), out int c) ? c : 100;
}

/// <summary>A master's RoomSet (.rs) for the dungeon generator (<c>0x141ff7930</c>): its rooms in file order, and
/// per door type the room sizes the grid fill may use (<c>0x141ff5760</c>, <c>0x141ff6350</c>).</summary>
sealed class DungeonRoomSet
{
    /// <summary>The door type the generator uses where none is given (the string at 0x1445c0fa8).</summary>
    public const string Rooms = "Metadata/Terrain/Dungeon/rooms.et";

    public readonly List<DungeonRoom> All = [];
    readonly Dictionary<string, List<(int W, int H, int Weight)>> _sizes = new(StringComparer.OrdinalIgnoreCase);

    public DungeonRoomSet(TerrainCache files, string path)
    {
        foreach (var (weight, roomPath, mask) in files.TileList(path))
            if (Parse(files, roomPath, weight, mask) is { } room) All.Add(room);

        // 0x141ff6960: rooms with these tags go to lists of their own; every other room is filed under each door type
        // of its exits other than rooms.et, or under rooms.et (room data +0x180) when it has no other.
        var weights = new Dictionary<string, SortedDictionary<(int, int), int>>(StringComparer.OrdinalIgnoreCase);
        var allowed = new Dictionary<string, HashSet<(int, int)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in All)
        {
            if (Apart(r.Tag)) continue;
            if (r.Tag.StartsWith("league_", StringComparison.Ordinal))
                throw new NotSupportedException($"{r.Path}: room set tag '{r.Tag}' (0x141ff6960) is not ported");
            var types = r.Exits.Select(e => e.Type).Where(t => t is not null && !t.Equals(Rooms, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // Room data +0x180, taken to be rooms.et for a room with a rooms.et exit and none for a room without
            // exits: 1_5_7's exitless fills, abyss_forceblank and vault_blocked_01 are in no size table.
            if (types.Count == 0 && r.Exits.Any(e => e.Type is not null)) types.Add(Rooms);
            foreach (var t in types) File(r, t!, weights, allowed);
        }
        // 0x141ff6350: a door type's sizes are those with a weight that some room of the type allows.
        foreach (var (type, sizes) in weights)
        {
            var ok = allowed.GetValueOrDefault(type) ?? [];
            _sizes[type] = [.. sizes.Where(s => s.Value != 0 && ok.Contains(s.Key)).Select(s => (s.Key.Item1, s.Key.Item2, s.Value))];
        }
    }

    /// <summary><c>0x141ff5760</c>'s size maps: the room's weight at (w, h) when its mask allows an even orientation,
    /// and at (h, w) for a room that is not square and allows an odd one. A size is allowed for a type by a room that is
    /// not unique (room data +6) and, for rooms.et, whose exits all have a door at height 0.</summary>
    static void File(DungeonRoom r, string type, Dictionary<string, SortedDictionary<(int, int), int>> weights,
        Dictionary<string, HashSet<(int, int)>> allowed)
    {
        if (!weights.TryGetValue(type, out var map)) weights[type] = map = [];
        bool even = (r.Mask & 0x55) != 0, odd = (r.Mask & 0xaa) != 0;
        if (even) map[(r.Width, r.Height)] = map.GetValueOrDefault((r.Width, r.Height)) + r.Weight;
        if (r.Width != r.Height && odd) map[(r.Height, r.Width)] = map.GetValueOrDefault((r.Height, r.Width)) + r.Weight;
        if (r.Unique || type.Equals(Rooms, StringComparison.OrdinalIgnoreCase) &&
            r.Exits.Any(e => e.Height != 0 || !string.Equals(e.Type, type, StringComparison.OrdinalIgnoreCase)))
            return;
        if (!allowed.TryGetValue(type, out var set)) allowed[type] = set = [];
        if (even) set.Add((r.Width, r.Height));
        if (r.Width != r.Height && odd) set.Add((r.Height, r.Width));
    }

    /// <summary>The sizes (w, h, weight) the fill may give a blank room of this door type, by width then height.</summary>
    public IReadOnlyList<(int W, int H, int Weight)> Sizes(string type) => _sizes.GetValueOrDefault(type) ?? [];

    /// <summary><c>0x141ff5760</c>'s per-tag lists (0x60-byte entries): every room of the tag in every orientation its
    /// mask allows, with its turned size and exits (+0x38, <c>0x141ff2ca0</c>).</summary>
    public IEnumerable<(DungeonRoom Room, int Orientation, int W, int H, RoomExit[] Exits)> Variants(string tag, Func<string, bool> directional)
    {
        foreach (var r in All)
        {
            if (r.Tag != tag) continue;
            for (int o = 0; o < 8; o++)
            {
                if ((r.Mask >> o & 1) == 0) continue;
                var (w, h) = o % 2 == 1 ? (r.Height, r.Width) : (r.Width, r.Height);
                yield return (r, o, w, h, TurnExits(r.Exits, r.Width, r.Height, o, directional));
            }
        }
    }

    /// <summary><c>0x141ff5760</c>'s per-size lists (0x40-byte entries, looked up by <c>0x141ff8300</c>): every room
    /// that is not unique (room +6) and not filed apart for its tag, in every orientation its mask allows that gives
    /// it this size, with its turned exits.</summary>
    public IEnumerable<(DungeonRoom Room, int Orientation, RoomExit[] Exits)> BySize(int w, int h, Func<string, bool> directional)
    {
        foreach (var r in All)
        {
            if (r.Unique || Apart(r.Tag)) continue;
            for (int o = 0; o < 8; o++)
            {
                if ((r.Mask >> o & 1) == 0) continue;
                var (tw, th) = o % 2 == 1 ? (r.Height, r.Width) : (r.Width, r.Height);
                if (tw == w && th == h) yield return (r, o, TurnExits(r.Exits, r.Width, r.Height, o, directional));
            }
        }
    }

    /// <summary>Room data +9 (<c>0x141ff4e70</c>): a fill room, its key's four corner grounds the same, no exits and
    /// room data +0x40 clear (unidentified; taken to be "no tag and not unique": the Prison's unique boss_exit.arm,
    /// tag "boss", one ground and no exits, is not filed in the client).</summary>
    static bool IsFill(DungeonRoom r) => r.Def is { } d && r.Tag.Length == 0 && !r.Unique && r.Exits.All(e => e.Type is null) &&
        d.Key.Grounds.All(g => string.Equals(g, d.Key.Grounds[0], StringComparison.OrdinalIgnoreCase));

    static bool SameGround(DungeonRoom r, string? ground) => string.Equals(r.Def!.Key.Grounds[0], ground, StringComparison.OrdinalIgnoreCase);

    /// <summary><c>0x141ff5760</c>'s per-ground size lists (room set +0x98, keyed by room data +0x13a): each fill
    /// room of the ground adds (w, h) and, when not square, (h, w), each at its weight, in room set order.</summary>
    public IEnumerable<(int W, int H, int Weight)> FillSizes(string? ground)
    {
        foreach (var r in All)
        {
            if (r.Weight == 0 || !IsFill(r) || !SameGround(r, ground)) continue;
            yield return (r.Width, r.Height, r.Weight);
            if (r.Width != r.Height) yield return (r.Height, r.Width, r.Weight);
        }
    }

    /// <summary><c>0x141ff5760</c>'s per-ground, per-size room lists (room set +0xd8, <c>0x141ff9110</c>): the fill rooms
    /// of the ground with that size, with their orientation mask (square: all of it; else the even orientations at
    /// (w, h), the odd ones at (h, w)) and weight.</summary>
    public IEnumerable<(DungeonRoom Room, int Mask, int Weight)> FillRooms(string? ground, int w, int h)
    {
        foreach (var r in All)
        {
            if (r.Weight == 0 || !IsFill(r) || !SameGround(r, ground)) continue;
            if (r.Width == r.Height) { if (r.Width == w && r.Height == h) yield return (r, r.Mask, r.Weight); }
            else if (r.Width == w && r.Height == h) yield return (r, r.Mask & 0x55, r.Weight);
            else if (r.Height == w && r.Width == h) yield return (r, r.Mask & 0xaa, r.Weight);
        }
    }

    /// <summary>The room set has rooms <c>0x141ff6960</c> files apart for their tag.</summary>
    public bool HasSpecialRooms => All.Any(r => Apart(r.Tag));

    /// <summary>The tags <c>0x141ff6960</c> files in lists of their own. Tags starting with "special" (also
    /// "specialchance…") give the room flag +8 in the .arm reader (<c>0x142a0f3a0</c>, with the number after "special"
    /// at +4, else −1); such rooms go to room set +0x1c0[+4] or, at −1, to a list of their own.</summary>
    static bool Apart(string tag) =>
        tag is "shrine" or "mission" or "stonecircle" or "preunique" || tag.StartsWith("special", StringComparison.Ordinal) || tag.StartsWith("telepad_injector", StringComparison.Ordinal) ||
        tag.StartsWith("room_injector", StringComparison.Ordinal) || tag.StartsWith("tile_injector", StringComparison.Ordinal);

    /// <summary><c>0x141ff2ca0</c> (and <c>0x141d75a50</c> for one exit): exit i of a w × h room turned by the
    /// orientation goes to (j + a·w + b·h) mod 2(w + h), j = i, or 2(w + h) − 1 − i for the mirrored orientations 4..7,
    /// which also flip the chirality of a type with a direction.</summary>
    public static RoomExit[] TurnExits(RoomExit[] exits, int w, int h, int orientation, Func<string, bool> directional)
    {
        ReadOnlySpan<byte> a = [0, 0, 1, 1, 1, 1, 2, 2, 0], b = [0, 1, 1, 2, 0, 1, 1, 2, 0];
        int o = Math.Min(orientation, 8), n = 2 * (w + h);
        bool mirrored = o is >= 4 and < 8;
        var turned = new RoomExit[n];
        for (int i = 0; i < n; i++)
        {
            int j = mirrored ? n - i - 1 : i;
            var x = exits[i];
            // 0x141ff2ca0 tests the type's fields for a direction; EdgeSplitting.IsDirectional (0x141d7bcf0) stands in.
            if (mirrored && x.Type is not null && directional(x.Type)) x = x with { Chirality = !x.Chirality };
            turned[(j + a[o] * w + b[o] * h) % n] = x;
        }
        return turned;
    }

    /// <summary><c>0x142a0f3a0</c>'s header: version, dictionary, unit size (x, then y before version 31, then the unit
    /// overlap after version 21), size in units, tag ("specialchance…" takes a number), uniqueness, the random monster
    /// flag (after version 15), the room's own key line, then the exits: door type index (0 = none; version 11 has a
    /// flag for rooms.et), height, and later the height override flag (after version 29) and, for a type with a
    /// direction, the chirality (from version 21) and its override flag (after version 29).</summary>
    internal static DungeonRoom? Parse(TerrainCache files, string path, int weight, int mask)
    {
        var lines = TerrainText.Lines(files.Text(path) ?? "").Select(TerrainText.Tokenize).Where(l => l.Count > 0).ToList();
        if (lines.Count == 0) return null;
        var tokens = lines.SelectMany((l, i) => l.Select(t => (Token: t, Line: i))).ToList();
        int at = 0;
        TerrainText.Token Next() => at < tokens.Count ? tokens[at++].Token : throw new InvalidDataException($"{path}: room ends early");
        int Int() => Next().Int;
        if (Next().Text != "version") throw new InvalidDataException($"{path}: Bad start of file");
        int version = Int();
        var dict = new List<string>();
        int count = Int();
        for (int i = 0; i < count; i++) dict.Add(Next().Text);
        int unit = Int();
        if (version < 0x1f && Int() != unit) throw new InvalidDataException($"{path}: Room unit sizes must be equal");
        if (version > 0x15) Int();
        int w = Int(), h = Int();
        string tag = Next().Text;
        if (tag.StartsWith("specialchance", StringComparison.Ordinal)) Int();
        bool unique = Int() != 0;
        if (version > 0xf) Int();
        // The room's own key: one slot line (0x142a0e2a0 reads any slot: "k", or "s" in shrine_1_1.arm).
        if (at >= tokens.Count || tokens[at].Token.Text is not ("k" or "n" or "s" or "f" or "o")) throw new InvalidDataException($"{path}: no room key after the header");
        int keyLine = tokens[at].Line;
        while (at < tokens.Count && tokens[at].Line == keyLine) at++;
        var exits = new RoomExit[2 * (w + h)];
        for (int i = 0; i < exits.Length; i++)
        {
            string? type;
            if (version == 0xb) type = Int() != 0 ? Rooms : null;
            else
            {
                int index = Int();
                type = index == 0 ? null : TerrainText.Resolve(path, dict[index - 1]);
            }
            var height = (sbyte)Int();
            if (version == 0xb) { exits[i] = new RoomExit(type, height, false); continue; }
            if (version > 0x1d) Int();
            bool chirality = false;
            bool directional = version >= 0x15 && type is not null && files.Edge(type) is { } et &&
                EdgeSplitting.IsDirectional(et, t => files.Edge(t) ?? throw new FileNotFoundException(t));
            if (directional) chirality = Int() != 0;
            if (version > 0x1d && directional) Int();
            exits[i] = new RoomExit(type, height, chirality);
        }
        return new DungeonRoom(path, unit, w, h, tag, unique, exits, weight, mask, files.Room(path));
    }
}
