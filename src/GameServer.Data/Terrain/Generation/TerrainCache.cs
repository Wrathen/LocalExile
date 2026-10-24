using System.Buffers.Binary;
using System.Text;

namespace GameServer.Data.Terrain.Generation;

/// <summary>A ground type (.gt): its name (fill tile lists name grounds) and the five flag numbers on its second line
/// as bits (number i → bit i, <c>0x1429f6a80</c>; numbers left out are 1, 1, 1, 1, 0; bit 4 = the ground is left out
/// of tile keys, <c>0x141d813c0</c>).</summary>
public sealed record GroundType(string Path, string Name, int Flags)
{
    public bool IgnoredInKeys => (Flags & 0x10) != 0;
}

/// <summary>A tile (.tdt, version 10) as the generator uses it: its key (after the table: u32 "", u32 .tgt, u32 tag,
/// 4 × u32 edges, u8 w, u8 h, 4 × u32 grounds, 4 × i8 heights, 4 × u8 points a, 4 × u8 points b).
/// <see cref="VertexGrounds"/> are the grounds of all (w + 1) × (h + 1) vertices, row by row (the tile data's +0xa8,
/// written to the map's corner grid on placement by <c>0x141d833e0</c>), as the file has them (grounds left out of
/// keys included); empty when the file has none. <see cref="VertexHeights"/> likewise (empty = all 0).</summary>
public sealed record TileDef(string Path, string Tag, TileKey Key)
{
    public int Width => Key.Width;
    public int Height => Key.Height;
    public string?[] VertexGrounds { get; init; } = [];
    public sbyte[] VertexHeights { get; init; } = [];
    /// <summary>The tile's mesh (.tgt, u32 4 of the header; tile data +0x20, <c>0x141d7d700</c>), null for none.</summary>
    public string? Mesh { get; init; }
    /// <summary>The second objects file (tile data +0x180): the .tmd the file names, the first of the two strings after the
    /// vertex block. Found as the string table's .tmd string (the table holds only the header's strings).</summary>
    public string? ObjectsFile { get; init; }
    /// <summary>The orientations the tile may take (tile data +0x171; 0 or 0xff = no restriction).</summary>
    public byte Restriction { get; init; }

    /// <summary><c>0x141d81590</c>: a TileSet line's orientation mask (0 = none given) with the tile's restriction.</summary>
    public int AllowedOrientations(int mask) =>
        Restriction is 0 or 0xff ? mask : mask == 0 ? Restriction : mask & Restriction;

    public string? VertexGround(int u, int v) => VertexGrounds.Length == 0 ? null : VertexGrounds[v * (Width + 1) + u];
    public sbyte VertexHeight(int u, int v) => VertexHeights.Length == 0 ? (sbyte)0 : VertexHeights[v * (Width + 1) + u];

    /// <summary><c>0x141d7ff50</c>: the height of vertex (u, v) of the tile turned by an orientation code (the vertex
    /// in the turned tile's frame, mapped back through the inverse code, table 0x142fb7f98).</summary>
    public int VertexHeightAt(int code, int u, int v)
    {
        var (tu, tv) = Unturn(code, u, v);
        return VertexHeight(tu, tv);
    }

    /// <summary><c>0x141d80030</c>: the ground of vertex (u, v) of the turned tile.</summary>
    public string? VertexGroundAt(int code, int u, int v)
    {
        var (tu, tv) = Unturn(code, u, v);
        return VertexGround(tu, tv);
    }

    static readonly byte[] InverseCode = [0, 3, 2, 1, 4, 5, 6, 7, 8];

    (int U, int V) Unturn(int code, int u, int v)
    {
        var (w, h) = Orientation.Size(code, Width, Height);
        return Orientation.MapVertex(InverseCode[Math.Min(code, 8)], u, v, w, h);
    }
}

/// <summary>An edge type (.et, parser <c>0x1429f78f0</c>, built by <c>0x141d7b3b0</c> / <c>0x141d7b570</c> /
/// <c>0x141d7b6e0</c>): its name (first word of line 1), the ground on each side (null = "wildcard"), then
/// "n0 size n2" and either "virtual" with two (sub-edge, chirality) lines (chirality: flag bits 1 and 2) and the default min / max width, or the
/// wiggle, can_end, force_height_zero and a centre ground. <see cref="Wildcard"/> is flag 0x80 (the name starts with
/// "Wildcard": such edges are left out of tile edge keys and removed from the graph after the special nodes).</summary>
public sealed record EdgeType(string Path, string Name, string? Ground1, string? Ground2, int N0, int Size, bool N2, bool Virtual,
    string?[] SubEdges, bool[] SubChirality, int MinWidth, int MaxWidth, int Wiggle = 0, bool CanEnd = false)
{
    public bool Wildcard => Name.StartsWith("Wildcard", StringComparison.Ordinal);
}

/// <summary>One slot of a room's grid: 'k' (a key at its top-left slot), 'n' (covered by a larger key or nothing),
/// 'f' (a 1 × 1 key with only a tag), 's' (a 1 × 1 key with nothing) or 'o'.</summary>
public sealed record RoomSlot(char Kind, int Column, int Row, TileKey? Key, string Tag, int Height)
{
    /// <summary>The key's last number (slot +0x73, version > 18): the corner of the key at the slot's cell, 0 top left,
    /// 1 top right, 2 bottom right, 3 bottom left. <c>0x1420ef310</c> moves the key by 1 − w for 1 and 2 (any value
    /// not a multiple of 3) and by 1 − h for 2 and 3 (above 1), in the room's own (unturned) cells.</summary>
    public int Corner { get; init; }

    public int ShiftX => Key is null || Corner % 3 == 0 ? 0 : 1 - Key.Width;
    public int ShiftY => Key is null || Corner <= 1 ? 0 : 1 - Key.Height;
}

/// <summary>A room (.arm): its tag, its own key (the first "k" line: size in tiles, outer edges, corners) and slot grid.</summary>
public sealed record RoomDef(string Path, string Tag, TileKey Key, RoomSlot[,] Slots)
{
    /// <summary>How many named points the room has (room data +0x188, 0x30 bytes each).</summary>
    public int PointCount { get; init; }

    /// <summary>The room's unit overlap (room data +1, header after version 21).</summary>
    public int Overlap { get; init; }

    /// <summary>The grounds of the room's inner vertices (u = 1 … w − 1, v = 1 … h − 1, row by row; room data +0xe8),
    /// null where none: the file's last numbers, each a dictionary index (<c>0x142a0f3a0</c> reads them to the end of
    /// the file); empty when the file has none.</summary>
    public string?[] VertexGrounds { get; init; } = [];

    /// <summary>The room's doodads and objects (room data +0x70, 0x58 bytes each), in file order.</summary>
    public IReadOnlyList<RoomDoodad> Doodads { get; init; } = [];

    public int Width => Key.Width;
    public int Height => Key.Height;
}

/// <summary>Reads and caches the terrain files the generator needs from any file source (the GGPK, or an
/// extracted copy).</summary>
public sealed class TerrainCache(Func<string, byte[]?> read)
{
    readonly Dictionary<string, object?> _cache = new(StringComparer.OrdinalIgnoreCase);

    T? Get<T>(string kind, string path, Func<byte[], T?> parse) where T : class
    {
        string key = kind + ":" + path;
        if (_cache.TryGetValue(key, out var hit)) return (T?)hit;
        var data = read(path);
        var value = data is null ? null : parse(data);
        _cache[key] = value;
        return value;
    }

    public string? Text(string path) => Get("text", path, TerrainText.Decode);

    /// <summary>A file's bytes as they are (tables), uncached.</summary>
    public byte[]? Raw(string path) => read(path);

    public GroundType? Ground(string? path) => path is null ? null : Get("gt", path, d =>
    {
        var lines = TerrainText.Lines(TerrainText.Decode(d)).Where(l => l.Trim().Length > 0).ToArray();
        // 0x1429f6a80 reads five numbers in turn; one the file leaves out keeps its default (1, 1, 1, 1, 0).
        int flags = 0b01111;
        if (lines.Length > 1)
        {
            var numbers = lines[1].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < Math.Min(numbers.Length, 5); i++)
                flags = numbers[i] != "0" ? flags | 1 << i : flags & ~(1 << i);
        }
        return new GroundType(path, lines.Length > 0 ? lines[0].Trim() : "", flags);
    });

    public EdgeType? Edge(string? path) => path is null ? null : Get("et", path, d =>
    {
        var words = TerrainText.Decode(d).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 3) return null;
        // Line 1 is "Name [#colour]": the colour, if any, is skipped before the grounds.
        int at = 1;
        if (at < words.Length && words[at].StartsWith('#')) at++;
        string? Ground(string w) => w == "wildcard" ? null : w.Trim('"');
        string? g1 = Ground(words[at++]), g2 = Ground(words[at++]);
        int Number(int fallback) => at < words.Length && int.TryParse(words[at], out int v) ? (at++, v).v : fallback;
        int n0 = Number(0), size = Number(1), n2 = Number(0);
        bool isVirtual = at < words.Length && words[at] == "virtual";
        var sub = new string?[2];
        var chirality = new bool[2];
        int minW = 0, maxW = 0;
        if (isVirtual)
        {
            at++;
            for (int i = 0; i < 2 && at + 1 < words.Length; i++, at += 2)
            {
                sub[i] = words[at].Trim('"');
                chirality[i] = words[at + 1] != "0";
            }
            minW = Number(0);
            maxW = Number(0);
        }
        int wiggle = !isVirtual ? Number(0) : 0;
        // Then can_end (flag 0x10, 0x141d7b6e0) and force_height_zero.
        bool canEnd = !isVirtual && Number(0) != 0;
        return new EdgeType(path, words[0], g1, g2, n0, size, n2 != 0, isVirtual, sub, chirality, minW, maxW, wiggle, canEnd);
    });

    public TileDef? Tile(string path) => Get("tdt", path, d => ParseTile(path, d));

    /// <summary>A tile's walkability data (<see cref="TileWalk.Parse"/>), null for a tile with a base tile.</summary>
    public TileWalk? Walk(string path) => Get("walk", path, TileWalk.Parse);

    readonly Dictionary<string, int> _tileFlags = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The u16 flags word after a .tdt's key (tile data +0x16c / +0x16d; bit 4: doodads may go on the tile;
    /// bit 0x200: made of sub-tiles). The reader (<c>0x1429f31a0</c>) clears 0x100 before version 14 and 0x200 before 13.</summary>
    public int TileFlags(string path)
    {
        if (_tileFlags.TryGetValue(path, out int known)) return known;
        var data = read(path);
        int flags = 0;
        if (data is not null)
        {
            int version = (int)BinaryPrimitives.ReadUInt32LittleEndian(data);
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
            int at = 8 + 2 * length + 58;
            if (at + 2 <= data.Length) flags = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));
            if (version < 14) flags &= ~0x100;
            if (version < 13) flags &= ~0x200;
        }
        return _tileFlags[path] = flags;
    }

    readonly Dictionary<string, bool> _transitionTiles = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, bool> _transitionTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tile data +0x16f bit 0 (<c>0x141d7cc50</c>, run on both object files as they load): one of the tile's
    /// objects (<see cref="TileObjects"/> or <see cref="SecondObjects"/>) is of a type with an AreaTransition component
    /// or the "waypoint" interaction (<c>0x141ebedf0</c>).</summary>
    public bool HasTransition(TileDef tile)
    {
        if (_transitionTiles.TryGetValue(tile.Path, out bool known)) return known;
        return _transitionTiles[tile.Path] = TileObjects(tile).Concat(SecondObjects(tile)).Any(o => IsTransitionType(o.Type));
    }

    /// <summary><c>0x141ffb870</c>: a tile with tile data +0x16f bit 1 (<c>0x141d7cc50</c>: one of its objects' type path
    /// contains "Spawner") and no key edge other than none or a wildcard (.et +0x48 bit 7) is placed at its orientation
    /// &amp; 3, its mirror dropped (the Labyrinth's statue spawners).</summary>
    public bool Unmirrored(TileDef tile) =>
        tile.Key.Edges.All(e => e is null || Edge(e)?.Wildcard == true) &&
        TileObjects(tile).Concat(SecondObjects(tile)).Any(o => o.Type.Contains("Spawner", StringComparison.Ordinal));

    readonly Dictionary<string, IReadOnlyList<TileObject>?> _objectFiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A .tmd's records, null when the file does not exist.</summary>
    IReadOnlyList<TileObject>? ObjectFile(string path)
    {
        if (_objectFiles.TryGetValue(path, out var known)) return known;
        var data = read(path);
        return _objectFiles[path] = data is null ? null : TileObjectFile.Parse(data);
    }

    /// <summary><c>0x141d7fcb0</c>: the .tmd of mesh <paramref name="index"/> (<c>0x141d7f990</c>: the index-th part of the
    /// ';' list for a tile made of sub-tiles, else the whole mesh string), its last four characters replaced by ".tmd".</summary>
    string? MeshObjectsPath(TileDef tile, int index)
    {
        string? mesh = tile.Mesh;
        if ((TileFlags(tile.Path) & 0x200) != 0 && mesh is not null)
        {
            var parts = mesh.Split(';');
            mesh = index < parts.Length ? parts[index] : null;
        }
        return mesh is { Length: >= 4 } ? mesh[..^4] + ".tmd" : null;
    }

    /// <summary>Tile data +0x178 (<c>0x141d7fb60</c>): for each part of the mesh's ';' list, the .tmd beside it; the last
    /// one that exists. None when the flags word has bit 3.</summary>
    public IReadOnlyList<TileObject> TileObjects(TileDef tile)
    {
        if ((TileFlags(tile.Path) & 8) != 0 || tile.Mesh is null) return [];
        IReadOnlyList<TileObject>? objects = null;
        for (int i = 0, n = tile.Mesh.Count(c => c == ';') + 1; i < n; i++)
            if (MeshObjectsPath(tile, i) is { } p && ObjectFile(p) is { } o) objects = o;
        return objects ?? [];
    }

    /// <summary>Tile data +0x180 (<c>0x141d7e060</c>): the .tmd the .tdt names (<see cref="TileDef.ObjectsFile"/>), e.g.
    /// a goat spawner's or a wall chest's objects. Placed after +0x178's, with no Reskin.</summary>
    public IReadOnlyList<TileObject> SecondObjects(TileDef tile) =>
        tile.ObjectsFile is { } p ? ObjectFile(p) ?? [] : [];

    /// <summary><c>0x141d7f430</c>: the objects of a sub-tile's mesh <paramref name="index"/>, from the master's Reskin copy
    /// when there is one; null when neither exists.</summary>
    public IReadOnlyList<TileObject>? SubTileObjects(TileDef tile, int index, string? reskin)
    {
        if (MeshObjectsPath(tile, index) is not { } p) return null;
        return (reskin is { Length: > 0 } ? ReskinObjects(p, tile.Path, reskin) : null) ?? ObjectFile(p);
    }

    /// <summary><c>0x141d7f700</c> with a master's Reskin: the tile's .tmd under "alttiles/&lt;reskin&gt;/&lt;reskin&gt;_"
    /// beside it (<c>0x141d88810</c>), null when there is none.</summary>
    public IReadOnlyList<TileObject>? ReskinObjects(TileDef tile, string reskin) =>
        ReskinObjects(tile.Mesh is { } m && m.EndsWith(".tgt", StringComparison.OrdinalIgnoreCase) ? m : tile.Path, tile.Path, reskin);

    IReadOnlyList<TileObject>? ReskinObjects(string p, string tdtPath, string reskin)
    {
        int slash = p.LastIndexOf('/');
        if (slash < 0) return null;
        var data = read(p[..(slash + 1)] + "alttiles/" + reskin + "/" + reskin + "_" + p[(slash + 1)..^4] + ".tmd");
        if (data is null) return null;
        // The copy's records are named "<reskin>_<name>". The captures place "<.tdt folder>/<name>.ao" (its Hull) when
        // the record starts with the Reskin as the master writes it (case counts) and that .ao exists, else the
        // reskinned name, a virtual .ao: 2_9_5's foothills ramps and 2_9_10_2's boss platform strip, 2_9_10_2's
        // rot_…_assHole02 / rot_…_bellyfx (no such files) and 2_10_6's blood_…_cartblocker (master "Blood") keep it.
        // The client code that does it was not found.
        string prefix = reskin + "_";
        return TileObjectFile.Parse(data)
            .Select(o => o.Name.StartsWith(prefix, StringComparison.Ordinal) && o with { Name = o.Name[prefix.Length..] } is var original &&
                Text(original.VirtualModel(tdtPath)) is not null ? original : o)
            .ToList();
    }

    bool IsTransitionType(string type)
    {
        if (_transitionTypes.TryGetValue(type, out bool known)) return known;
        bool result = false;
        string? t = type;
        for (int depth = 0; t is { Length: > 0 } && t != "nothing" && depth < 16 && !result; depth++)
        {
            var text = Text(t + ".ot");
            if (text is null) break;
            var lines = TerrainText.Lines(text).Select(l => l.Trim()).ToList();
            result = lines.Contains("AreaTransition") || lines.Any(l => l.Replace(" ", "") == "action_name=\"waypoint\"");
            t = lines.Where(l => l.StartsWith("extends ", StringComparison.Ordinal)).Select(l => TerrainText.Tokenize(l).Skip(1).FirstOrDefault().Text).FirstOrDefault();
        }
        return _transitionTypes[type] = result;
    }

    TileDef ParseTile(string path, byte[] data)
    {
        int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        string table = Encoding.Unicode.GetString(data, 8, 2 * length);
        byte[] h = data[(8 + 2 * length)..];
        string? Str(int at)
        {
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(at));
            if (offset == uint.MaxValue || offset >= table.Length) return null;
            int end = table.IndexOf('\0', (int)offset);
            string s = table[(int)offset..(end < 0 ? table.Length : end)];
            return s.Length == 0 ? null : s;
        }
        var key = new TileKey { Width = h[28], Height = h[29] };
        for (int i = 0; i < 4; i++)
        {
            key.Edges[i] = Str(12 + 4 * i);
            string? ground = Str(30 + 4 * i);
            key.Grounds[i] = Ground(ground) is { IgnoredInKeys: true } ? null : ground;
            key.Heights[i] = (sbyte)h[46 + i];
            key.PointA[i] = h[50 + i];
            key.PointB[i] = h[54 + i];
        }
        // The u16 after the key says which vertex blocks the tile has (bit 2 grounds, bit 1 heights); a tile with a base
        // tile (u32 0 names one) is laid out differently and not ported.
        ushort flags = Str(0) is null && h.Length >= 60 ? BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(58)) : (ushort)0;
        var (grounds, heights) = (flags & 2) != 0 ? VertexBlock(h, table.Length, key.Width, key.Height, (flags & 1) != 0)
            : (flags & 1) != 0 ? (null, HeightBlock(h, table.Length, key)) : (null, null);
        // 0x1429f31a0: from version 10, a tile without a base tile has one more byte after the flags word, its
        // orientation restriction mask (tile data +0x171, set by 0x141d7d7e0).
        int version = (int)BinaryPrimitives.ReadUInt32LittleEndian(data);
        byte restriction = Str(0) is null && version > 9 && h.Length >= 61 ? h[60] : (byte)0;
        return new TileDef(path, Str(8) ?? "", key)
        {
            Restriction = restriction,
            VertexGrounds = grounds is null ? [] : [.. grounds.Select(o => o == uint.MaxValue ? null : TableString(table, o))],
            VertexHeights = heights ?? [],
            Mesh = Str(4),
            ObjectsFile = table.Split('\0').FirstOrDefault(x => x.EndsWith(".tmd", StringComparison.OrdinalIgnoreCase)),
        };
    }

    static string? TableString(string table, uint offset)
    {
        if (offset >= table.Length) return null;
        int end = table.IndexOf('\0', (int)offset);
        string s = table[(int)offset..(end < 0 ? table.Length : end)];
        return s.Length == 0 ? null : s;
    }

    /// <summary>The vertex block of a .tdt (<c>0x1429f31a0</c>: (w + 1) × (h + 1) u32 ground strings when the flags
    /// word has bit 2, then as many i8 heights with bit 1, then two u32 strings and a byte). It follows the sub-tile
    /// records, whose packing (<c>0x1429f59a0</c>) is not ported, so the block is found by a scan from the end: the
    /// last place where every value is a string offset and the four corners are the key's corner grounds (the header's,
    /// grounds left out of keys included), followed by two string offsets (after the heights when
    /// <paramref name="hasHeights"/>).</summary>
    static (uint[]? Grounds, sbyte[]? Heights) VertexBlock(byte[] h, int tableLength, int w, int ht, bool hasHeights)
    {
        int n = (w + 1) * (ht + 1);
        uint U(int at) => BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(at));
        bool Valid(uint o) => o == uint.MaxValue || o < tableLength;
        uint c0 = U(30), c1 = U(34), c2 = U(38), c3 = U(42);
        for (int p = h.Length - 4 * n - 8; p >= 58; p--)
        {
            if (U(p) != c0 || U(p + 4 * w) != c1 || U(p + 4 * (n - 1)) != c2 || U(p + 4 * (n - 1 - w)) != c3) continue;
            bool ok = true;
            for (int i = 0; i < n && ok; i++) ok = Valid(U(p + 4 * i));
            if (!ok) continue;
            var grounds = new uint[n];
            for (int i = 0; i < n; i++) grounds[i] = U(p + 4 * i);
            int after = p + 4 * n, tail = after + (hasHeights ? n : 0);
            if (tail + 8 > h.Length || !Valid(U(tail)) || !Valid(U(tail + 4))) continue;
            return (grounds, hasHeights ? [.. h.AsSpan(after, n).ToArray().Select(b => (sbyte)b)] : null);
        }
        return (null, null);
    }

    /// <summary>The vertex block of a .tdt with heights and no grounds (flags word bit 1 only): (w + 1) × (h + 1) i8
    /// heights, then two u32 strings and a byte. Found like <see cref="VertexBlock"/>: the last place whose four corners
    /// are the key's corner heights, followed by two string offsets.</summary>
    static sbyte[]? HeightBlock(byte[] h, int tableLength, TileKey key)
    {
        int w = key.Width, n = (w + 1) * (key.Height + 1);
        bool Valid(int at) => BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(at)) is var o && (o == uint.MaxValue || o < tableLength);
        for (int p = h.Length - n - 8; p >= 58; p--)
        {
            if ((sbyte)h[p] != key.Heights[0] || (sbyte)h[p + w] != key.Heights[1] || (sbyte)h[p + n - 1] != key.Heights[2] ||
                (sbyte)h[p + n - 1 - w] != key.Heights[3]) continue;
            if (!Valid(p + n) || !Valid(p + n + 4)) continue;
            return [.. h.AsSpan(p, n).ToArray().Select(b => (sbyte)b)];
        }
        return null;
    }

    /// <summary>A tile list (.tst, <c>0x1429f7040</c>): per line an optional weight (1), the .tdt and orientation
    /// words (I, R90, FR180, …) as a mask of allowed orientations (none: all, 0xff); nested lists through "include"
    /// lines.</summary>
    public List<(int Weight, string Path, int Mask)> TileList(string path)
    {
        var result = new List<(int, string, int)>();
        foreach (var line in TerrainText.Lines(Text(path) ?? ""))
        {
            var t = TerrainText.Tokenize(line);
            int q = t.FindIndex(x => x.Quoted);
            if (q < 0) continue;
            string p = TerrainText.Resolve(path, t[q].Text);
            if (t.Count > 0 && t[0].Text == "include") { result.AddRange(TileList(p)); continue; }
            int weight = q > 0 && t[0].IsNumber ? t[0].Int : 1;
            int mask = 0;
            foreach (var word in t.Skip(q + 1))
            {
                int o = Orientation.Parse(word.Text);
                if (o == 8) throw new InvalidDataException($"{path}: \"{word.Text}\" is not a valid Orientation");
                mask |= 1 << o;
            }
            result.Add((weight, p, mask == 0 ? 0xff : mask));
        }
        return result;
    }

    /// <summary>A fill tile list (.gft, version 2): a quoted ground name, then "weight path" lines.</summary>
    public Dictionary<string, List<(int Weight, string Path)>> FillTiles(string path) => Get("gft", path, d =>
    {
        var groups = new Dictionary<string, List<(int, string)>>(StringComparer.Ordinal);
        List<(int, string)>? current = null;
        foreach (var line in TerrainText.Lines(TerrainText.Decode(d)))
        {
            var t = TerrainText.Tokenize(line);
            if (t.Count == 1 && t[0].Quoted) groups[t[0].Text] = current = [];
            else if (t.Count >= 2 && t[0].IsNumber && t[1].Quoted && current is not null) current.Add((t[0].Int, TerrainText.Resolve(path, t[1].Text)));
        }
        return groups;
    }) ?? [];

    /// <summary>A room (.arm): its dictionary, tag, size and slot grid. Slot keys are resolved to paths.</summary>
    public RoomDef? Room(string path) => Get<RoomDef>("arm", path, d => ParseRoom(path, TerrainText.Decode(d)));

    /// <summary><c>0x142a0f3a0</c>'s header up to the unit overlap (room data +1): version, dictionary, unit size (x, then
    /// y before version 31), then the overlap after version 21 (0 before).</summary>
    static int HeaderOverlap(string text)
    {
        var t = TerrainText.Lines(text).SelectMany(TerrainText.Tokenize).ToList();
        if (t.Count < 3 || t[0].Text != "version" || !t[1].IsNumber || !t[2].IsNumber) return 0;
        int version = t[1].Int, at = 3 + t[2].Int + 1;
        if (version < 0x1f) at++;
        return version > 0x15 && at < t.Count && t[at].IsNumber ? t[at].Int : 0;
    }

    static RoomDef? ParseRoom(string path, string text)
    {
        var file = RoomFile.Parse(text);
        if (file.RoomKey is null) return null;
        var lines = TerrainText.Lines(text).Select(TerrainText.Tokenize).ToArray();
        int w = file.Width, h = file.Height, at = -1;
        // Values per key: as many as the room's own key line has (24; 23 in older versions, without the extra).
        int values = lines.FirstOrDefault(l => l.Count > 3 && l[0].Text == "k")?.Skip(1).TakeWhile(x => x.IsNumber).Count() ?? 24;
        // The slot grid: the first run of h lines that each hold exactly w slots after the room's own key line (the
        // first slot line after the tag, as RoomFile finds it; a 1 × 1 room's "s" key line would pass as a grid).
        int tagLine = Array.FindIndex(lines, l => l.Count == 1 && l[0].Quoted);
        int keyLine = Array.FindIndex(lines, tagLine + 1, l => l.Count > 0 && !l[0].Quoted && l[0].Text is "k" or "n" or "s" or "f" or "o");
        for (int k = keyLine + 1; k + h <= lines.Length && at < 0; k++)
            if (lines[k].Count > 0 && !lines[k][0].Quoted && lines[k][0].Text is "k" or "n" or "s" or "f" or "o" && Grid(lines, k, w, h, values) is not null)
                at = k;
        if (at < 0) return null;
        var slots = Grid(lines, at, w, h, values)!;
        var grid = new RoomSlot[w, h];
        foreach (var (kind, col, row, v) in slots)
        {
            TileKey? key = null;
            string tag = "";
            int extra = 0, corner = 0;
            if (v is not null)
            {
                key = new TileKey { Width = (byte)v[0], Height = (byte)v[1] };
                for (int i = 0; i < 4; i++)
                {
                    key.Edges[i] = Path(file, v[2 + i]);
                    key.PointA[i] = (byte)v[6 + 2 * i];
                    key.PointB[i] = (byte)v[7 + 2 * i];
                    key.Grounds[i] = Path(file, v[14 + i]);
                    key.Heights[i] = (sbyte)v[18 + i];
                }
                // 0x142a0e2a0: the corners keep their heights above the lowest, which becomes the slot's height (+0x132).
                // The last number (version > 18, "corner") is not the height but the key's anchor corner.
                int low = key.Heights.Min();
                for (int i = 0; i < 4; i++) key.Heights[i] -= (sbyte)low;
                tag = file.Dict(v[22]);
                extra = low;
                if (kind == 'k' && values > 23) corner = v[23];
            }
            grid[col, row] = new RoomSlot(kind, col, row, key, tag, extra) { Corner = corner };
        }
        var rk = file.RoomKey;
        var roomKey = new TileKey { Width = (byte)rk.Width, Height = (byte)rk.Height };
        for (int i = 0; i < 4; i++)
        {
            roomKey.Edges[i] = Path(file, rk.Edges[i]);
            roomKey.PointA[i] = (byte)rk.Points[2 * i];
            roomKey.PointB[i] = (byte)rk.Points[2 * i + 1];
            roomKey.Grounds[i] = Path(file, rk.Grounds[i]);
            roomKey.Heights[i] = (sbyte)rk.Heights[i];
        }
        // As for the slots, the corners keep their heights above the lowest (0x142a0e2a0).
        int roomLow = roomKey.Heights.Min();
        for (int i = 0; i < 4; i++) roomKey.Heights[i] -= (sbyte)roomLow;
        // The inner vertex grounds: the file's last line, when it holds exactly (w − 1) × (h − 1) whole numbers (rooms
        // without them end with the single counts of the sections before).
        int inner = Math.Max(0, w - 1) * Math.Max(0, h - 1);
        var last = lines.LastOrDefault(l => l.Count > 0);
        string?[] vertexGrounds = [];
        if (inner > 0 && last is not null && last.Count == inner && last.All(t => t.IsNumber && !t.Text.Contains('.')))
            vertexGrounds = [.. last.Select(t => Path(file, t.Int))];
        return new RoomDef(path, file.Tag, roomKey, grid) { PointCount = file.Points.Count, VertexGrounds = vertexGrounds, Overlap = HeaderOverlap(text), Doodads = file.Doodads };
    }

    static string? Path(RoomFile file, int index) => file.Dict(index) is { Length: > 0 } s ? s : null;

    static List<(char, int, int, int[]?)>? Grid(List<TerrainText.Token>[] lines, int first, int w, int h, int values)
    {
        var slots = new List<(char, int, int, int[]?)>();
        for (int row = 0; row < h; row++)
        {
            var t = lines[first + row];
            int col = 0, i = 0;
            while (i < t.Count)
            {
                switch (t[i].Text)
                {
                    case "k":
                        if (i + values >= t.Count) return null;
                        var v = new int[24];
                        for (int k = 0; k < values; k++)
                        {
                            if (!t[i + 1 + k].IsNumber) return null;
                            v[k] = t[i + 1 + k].Int;
                        }
                        slots.Add(('k', col, row, v));
                        i += values + 1;
                        break;
                    case "f":
                        // "f N": a 1 × 1 slot with no edges or grounds and tag N (forced tiles such as "forcedblank").
                        if (i + 1 >= t.Count || !t[i + 1].IsNumber) return null;
                        var f = new int[24];
                        f[0] = f[1] = 1;
                        for (int k = 6; k < 14; k++) f[k] = 3; // edge points: none (the edge's length)
                        f[22] = t[i + 1].Int;
                        slots.Add(('f', col, row, f));
                        i += 2;
                        break;
                    case "s":
                        // "s": a 1 × 1 slot with no edges, grounds or tag (the fill takes it, 0x141fe89e0).
                        var e = new int[24];
                        e[0] = e[1] = 1;
                        for (int k = 6; k < 14; k++) e[k] = 3;
                        slots.Add(('s', col, row, e));
                        i++;
                        break;
                    case "n" or "o":
                        slots.Add((t[i].Text[0], col, row, null));
                        i++;
                        break;
                    default:
                        return null;
                }
                col++;
            }
            if (col != w) return null;
        }
        return slots;
    }
}
