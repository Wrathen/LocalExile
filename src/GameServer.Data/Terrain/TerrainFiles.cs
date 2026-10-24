using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace GameServer.Data.Terrain;

/// <summary>Helpers for the text terrain files (.arm, .tgr, .dgr, .tsi, .rs, .tst): UTF-16 LE with a BOM, one record
/// per line, space-separated tokens with "quoted" strings.</summary>
public static class TerrainText
{
    public static string Decode(byte[] data)
    {
        if (data.Length >= 2 && data[0] == 0xff && data[1] == 0xfe) return Encoding.Unicode.GetString(data, 2, data.Length - 2);
        if (data.Length >= 3 && data[0] == 0xef && data[1] == 0xbb && data[2] == 0xbf) return Encoding.UTF8.GetString(data, 3, data.Length - 3);
        // Without a BOM: UTF-16 when every other byte of the start is zero.
        if (data.Length >= 4 && data[1] == 0 && data[3] == 0) return Encoding.Unicode.GetString(data);
        return Encoding.UTF8.GetString(data);
    }

    public static string[] Lines(string text) => text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

    /// <summary>A token: its text and whether it was quoted.</summary>
    public readonly record struct Token(string Text, bool Quoted)
    {
        public bool IsNumber => !Quoted && double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        public int Int => int.Parse(Text, CultureInfo.InvariantCulture);
        public float Float => float.Parse(Text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    public static List<Token> Tokenize(string line)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '"')
            {
                int end = line.IndexOf('"', i + 1);
                if (end < 0) end = line.Length;
                tokens.Add(new Token(line[(i + 1)..end], true));
                i = end + 1;
                continue;
            }
            int start = i;
            while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;
            tokens.Add(new Token(line[start..i], false));
        }
        return tokens;
    }

    /// <summary>Every quoted string of a list file (.rs, .tst, .gft): one path per line, optional weight and flags.</summary>
    public static List<string> QuotedPaths(string text) =>
        Lines(text).Select(Tokenize).SelectMany(t => t.Where(x => x.Quoted && x.Text.Length > 0).Take(1)).Select(t => t.Text).ToList();

    /// <summary>A "key value" file (.tsi): the first token is the key, the first quoted token (or the second token) the value.</summary>
    public static Dictionary<string, string> KeyValues(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in Lines(text))
        {
            var t = Tokenize(line);
            if (t.Count < 2 || t[0].Quoted) continue;
            var value = t.Skip(1).FirstOrDefault(x => x.Quoted);
            map[t[0].Text] = value.Text ?? t[1].Text;
        }
        return map;
    }

    /// <summary>A path written in a terrain file: absolute ("Metadata/…", "Art/…") or relative to the referencing file's folder.</summary>
    public static string Resolve(string referencingFile, string path)
    {
        path = path.Replace('\\', '/');
        if (path.Contains('/') && (path.StartsWith("Metadata/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("Art/", StringComparison.OrdinalIgnoreCase)))
            return path;
        int slash = referencingFile.LastIndexOf('/');
        return (slash < 0 ? "" : referencingFile[..(slash + 1)]) + path;
    }
}

/// <summary>A node of a room graph (.dgr: fixed layout, .tgr: macro graph for the generator).
/// Line: x y edgeCount edge… "tag" rotation nameCount "name"… (more numbers).</summary>
public sealed record GraphNode(int Index, int X, int Y, string Tag, string Rotation, IReadOnlyList<string> Names);

public sealed record GraphFile(int Version, int Width, int Height, string MasterFile, int NodeCount, int EdgeCount,
    IReadOnlyList<GraphNode> Nodes, IReadOnlyList<string> Problems)
{
    public static GraphFile Parse(string text)
    {
        var lines = TerrainText.Lines(text);
        int version = 0, w = 0, h = 0, nodes = 0, edges = 0, at = 0;
        string master = "";
        var problems = new List<string>();
        for (; at < lines.Length; at++)
        {
            var t = TerrainText.Tokenize(lines[at]);
            if (t.Count == 0) continue;
            switch (t[0].Text)
            {
                case "version" when t.Count > 1: version = t[1].Int; continue;
                case "Size:" when t.Count > 2: w = t[1].Int; h = t[2].Int; continue;
                case "MasterFile:" when t.Count > 1: master = t[1].Text; continue;
                case "Nodes:" when t.Count > 1: nodes = t[1].Int; continue;
                case "Edges:" when t.Count > 1: edges = t[1].Int; at++; goto body;
            }
        }
    body:
        var list = new List<GraphNode>();
        for (; at < lines.Length && list.Count < nodes; at++)
        {
            var node = TryNode(list.Count, TerrainText.Tokenize(lines[at]));
            if (node is not null) list.Add(node);
        }
        if (list.Count != nodes) problems.Add($"parsed {list.Count} of {nodes} nodes");
        return new GraphFile(version, w, h, master, nodes, edges, list, problems);
    }

    private static GraphNode? TryNode(int index, List<TerrainText.Token> t)
    {
        if (t.Count < 6 || !t[0].IsNumber || !t[1].IsNumber || !t[2].IsNumber) return null;
        int e = t[2].Int, i = 3 + e;
        if (i + 2 >= t.Count || !t[i].Quoted || t[i + 1].Quoted || !t[i + 2].IsNumber) return null;
        for (int k = 3; k < i; k++) if (!t[k].IsNumber) return null;
        string tag = t[i].Text, rotation = t[i + 1].Text;
        int n = t[i + 2].Int;
        if (i + 3 + n > t.Count) return null;
        var names = new List<string>();
        for (int k = 0; k < n; k++)
        {
            if (!t[i + 3 + k].Quoted) return null;
            names.Add(t[i + 3 + k].Text);
        }
        return new GraphNode(index, t[0].Int, t[1].Int, tag, rotation, names);
    }
}

/// <summary>A tile key of a room (.arm "k" slot): size in tiles, 4 edge types, 8 edge points, 4 corner ground types,
/// 4 corner heights, tag (all edge/ground/tag values index the room's string dictionary, 1-based, 0 = none).</summary>
public sealed record TileKey(int Width, int Height, int[] Edges, int[] Points, int[] Grounds, int[] Heights, int Tag, int[] Rest)
{
    public static TileKey? From(IReadOnlyList<int> v)
    {
        if (v.Count < 23) return null;
        return new TileKey(v[0], v[1], v.Skip(2).Take(4).ToArray(), v.Skip(6).Take(8).ToArray(), v.Skip(14).Take(4).ToArray(),
            v.Skip(18).Take(4).ToArray(), v[22], v.Skip(23).ToArray());
    }
}

public sealed record RoomSlot(int Column, int Row, TileKey Key);
public sealed record NamedPoint(float X, float Y, float Angle, string Name);
/// <summary>A doodad or server object of a room: cell, model, object type, angle (radians, as the client's placed doodads
/// report it), scale (The Twilight Strand's force wall has 2.7), the flip flag (runtime +0x1c) and the fixed height
/// (+0x10, null without one).</summary>
public sealed record RoomDoodad(float X, float Y, string Model, string Type, float Angle = 0, float Scale = 1, bool Flip = false,
    float? FixedHeight = null);

/// <summary>A room (.arm): string dictionary, tag, size (from its own key line), named points (room cells), the tile
/// slot grid (one row per line; k = tile key at its top-left slot, n = covered/none, s, f N) and doodads.</summary>
public sealed record RoomFile(int Version, IReadOnlyList<string> Dictionary, string Tag, TileKey? RoomKey,
    IReadOnlyList<NamedPoint> Points, IReadOnlyList<RoomSlot> Slots, IReadOnlyList<RoomDoodad> Doodads, string? Problem)
{
    public int Width => RoomKey?.Width ?? 0;
    public int Height => RoomKey?.Height ?? 0;

    /// <summary>The dictionary string for a 1-based index (0 or out of range → "").</summary>
    public string Dict(int index) => index >= 1 && index <= Dictionary.Count ? Dictionary[index - 1] : "";

    public static RoomFile Parse(string text)
    {
        var lines = TerrainText.Lines(text).Select(TerrainText.Tokenize).ToArray();
        int at = 0, version = 0;
        string? problem = null;
        while (at < lines.Length && lines[at].Count == 0) at++;
        if (at < lines.Length && lines[at].Count > 1 && lines[at][0].Text == "version") { version = lines[at][1].Int; at++; }
        var dict = new List<string>();
        if (at < lines.Length && lines[at].Count == 1 && lines[at][0].IsNumber)
        {
            int n = lines[at][0].Int; at++;
            for (int k = 0; k < n && at < lines.Length; k++, at++) dict.Add(lines[at].Count > 0 ? lines[at][0].Text : "");
        }
        // The room's tag: the first line that is a single quoted string; the room key: the first slot line after it
        // (0x142a0e2a0 reads any slot: "k" with its values, or a 1 × 1 "s" (nothing) or "f N" (tag N) in fill rooms).
        string tag = "";
        for (int k = at; k < lines.Length; k++)
            if (lines[k].Count == 1 && lines[k][0].Quoted) { tag = lines[k][0].Text; at = k + 1; break; }
        TileKey? roomKey = null;
        int keyValues = 0;
        for (; at < lines.Length; at++)
            if (lines[at].Count > 3 && lines[at][0].Text == "k")
            {
                var v = lines[at].Skip(1).TakeWhile(x => x.IsNumber).Select(x => x.Int).ToList();
                keyValues = v.Count;
                roomKey = TileKey.From(v);
                at++;
                break;
            }
            else if ((lines[at].Count == 1 && lines[at][0].Text == "s") ||
                     (lines[at].Count == 2 && lines[at][0].Text == "f" && lines[at][1].IsNumber))
            {
                roomKey = new TileKey(1, 1, new int[4], [3, 3, 3, 3, 3, 3, 3, 3], new int[4], new int[4],
                    lines[at].Count == 2 ? lines[at][1].Int : 0, []);
                at++;
                break;
            }
        if (roomKey is null) return new RoomFile(version, dict, tag, null, [], [], [], "no room key line");

        // The slot grid: Height consecutive lines that each parse to exactly Width slots.
        int gridAt = -1;
        List<RoomSlot>? slots = null;
        for (int k = at; k + roomKey.Height <= lines.Length && gridAt < 0; k++)
        {
            if (lines[k].Count == 0 || lines[k][0].Quoted || !IsSlotToken(lines[k][0].Text)) continue;
            slots = TryGrid(lines, k, roomKey.Width, roomKey.Height, keyValues);
            if (slots is not null) gridAt = k;
        }
        if (gridAt < 0) problem = "slot grid not found";

        // Named points before the grid: x y angle "name".
        var points = new List<NamedPoint>();
        int pointsEnd = gridAt < 0 ? lines.Length : gridAt;
        for (int k = at; k < pointsEnd; k++)
        {
            var t = lines[k];
            if (t.Count == 4 && t[0].IsNumber && t[1].IsNumber && t[2].IsNumber && t[3].Quoted)
                points.Add(new NamedPoint(t[0].Float, t[1].Float, t[2].Float, t[3].Text));
        }
        // Doodads after the grid (0x142a0f3a0): x y, [has position, x y] (version ≥ 34), angle, [quaternion x y z w]
        // (≥ 17), flip, [wake group] (> 24), has fixed height, [height], [scale] (> 13), "model.ao", "Metadata/…type"
        // (before 13 a type number), then the rest of the line.
        var doodads = new List<RoomDoodad>();
        for (int k = gridAt < 0 ? at : gridAt + roomKey.Height; k < lines.Length; k++)
        {
            var t = lines[k];
            if (t.Count < 4 || !t[0].IsNumber || !t[1].IsNumber) continue;
            int q = t.FindIndex(x => x.Quoted && x.Text.EndsWith(".ao", StringComparison.OrdinalIgnoreCase));
            if (q < 0 || q + 1 >= t.Count || !t[q + 1].Quoted) continue;
            int i = 2;
            if (version >= 34 && t[i++].Int != 0) i += 2;
            float angle = t[i++].Float;
            if (version >= 17) i += 4;
            bool flip = t[i++].Int != 0;
            if (version > 24) i++;
            float? height = t[i++].Int != 0 ? t[i++].Float : null;
            float scale = version > 13 ? t[i++].Float : 1;
            if (i != q)
            {
                problem ??= $"doodad line {k + 1}: {q} fields before the model, expected {i}";
                continue;
            }
            doodads.Add(new RoomDoodad(t[0].Float, t[1].Float, t[q].Text, t[q + 1].Text, angle, scale, flip, height));
        }
        return new RoomFile(version, dict, tag, roomKey, points, slots ?? [], doodads, problem);
    }

    private static bool IsSlotToken(string s) => s is "k" or "n" or "s" or "f" or "o";

    private static List<RoomSlot>? TryGrid(List<TerrainText.Token>[] lines, int first, int width, int height, int keyValues)
    {
        var slots = new List<RoomSlot>();
        for (int row = 0; row < height; row++)
        {
            var t = lines[first + row];
            int col = 0, i = 0;
            while (i < t.Count)
            {
                switch (t[i].Text)
                {
                    case "k":
                        if (i + keyValues >= t.Count) return null;
                        var v = new List<int>(keyValues);
                        for (int k = 1; k <= keyValues; k++)
                        {
                            if (!t[i + k].IsNumber) return null;
                            v.Add(t[i + k].Int);
                        }
                        var key = TileKey.From(v);
                        if (key is null) return null;
                        slots.Add(new RoomSlot(col, row, key));
                        i += keyValues + 1;
                        break;
                    case "f":
                        i += 2;
                        break;
                    case "n" or "s" or "o":
                        i++;
                        break;
                    default:
                        return null;
                }
                col++;
            }
            if (col != width) return null;
        }
        return slots;
    }
}

/// <summary>The header of a tile definition (.tdt, version 10): u32 version, u32 string table length (UTF-16 units),
/// the table, then u32 (an empty string), u32 .tgt, u32 tag, 4 × u32 edge types, u8 width, u8 height (tiles), 4 × u32
/// corner ground types (string offsets; 0xffffffff = none), u32, then 8 bytes of edge points: a0..a3, b0..b3 (one (a, b)
/// pair per edge; a room key writes the same pairs interleaved, a0 b0 a1 b1 …).</summary>
public sealed record TileDefinition(int Version, string Tgt, string Tag, string[] Edges, int Width, int Height, string[] Grounds, int[] Points)
{
    public static TileDefinition Parse(ReadOnlySpan<byte> data)
    {
        int version = (int)BinaryPrimitives.ReadUInt32LittleEndian(data);
        int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        string table = Encoding.Unicode.GetString(data.Slice(8, 2 * length));
        byte[] h = data[(8 + 2 * length)..].ToArray();
        string Str(uint offset)
        {
            if (offset == uint.MaxValue || offset >= table.Length) return "";
            int end = table.IndexOf('\0', (int)offset);
            return table[(int)offset..(end < 0 ? table.Length : end)];
        }
        uint U(int o) => BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(o));
        return new TileDefinition(version, Str(U(4)), Str(U(8)),
            [Str(U(12)), Str(U(16)), Str(U(20)), Str(U(24))], h[28], h[29],
            [Str(U(30)), Str(U(34)), Str(U(38)), Str(U(42))],
            h.Length >= 58 ? [h[50], h[54], h[51], h[55], h[52], h[56], h[53], h[57]] : []);
    }
}
