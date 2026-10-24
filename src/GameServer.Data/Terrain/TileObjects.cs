using System.Buffers.Binary;
using System.Text;

namespace GameServer.Data.Terrain;

/// <summary>One placed object of a terrain tile, from the tile's <c>.tmd</c> file (next to its <c>.tgt</c>).
/// Positions are tile-local world units (<see cref="TilePlacement.WorldPerCell"/> per cell); z negative is up.</summary>
public sealed record TileObject(float X, float Y, float Z, uint Flags, float Angle, string Name, string Mesh, string Type, string? Animation,
    (int X, int Y)? Cell = null)
{
    /// <summary>The tile-local cell the client uses (it stores the record as i32 x, i32 y cells): the text form's own
    /// cell fields, else the world position's cell.</summary>
    public int CellX => Cell?.X ?? (int)MathF.Floor(X / TilePlacement.WorldPerCell);
    public int CellY => Cell?.Y ?? (int)MathF.Floor(Y / TilePlacement.WorldPerCell);

    /// <summary>The virtual model the client registers for the record: "&lt;.tdt folder&gt;/&lt;record name&gt;.ao"
    /// (not in the bundle index; Agents/docs/protocol/08-area-transitions.md §5).</summary>
    public string VirtualModel(string tdtPath)
    {
        int slash = tdtPath.LastIndexOf('/');
        return (slash < 0 ? "" : tdtPath[..(slash + 1)]) + Name + ".ao";
    }
}

/// <summary><c>.tmd</c> (tile metadata, version 5): u8 version, u32 count, count × 36-byte records (f32 x, f32 y, f32 z,
/// u32 flags (0x80000000 in every record seen), f32 angle (radians; 0 for most, π/2 for Highgate's AscentTransition), u32 string offsets: name, mesh, object type, .ao or 0xffffffff), then u32 length
/// in UTF-16 units and the UTF-16 string table (offsets count UTF-16 units from its start).</summary>
public static partial class TileObjectFile
{
    public const int RecordSize = 36;

    public static IReadOnlyList<TileObject> Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 2 && data[0] == 0xff && data[1] == 0xfe) return ParseText(TerrainText.Decode(data.ToArray()));
        if (data.Length < 5) throw new InvalidDataException(".tmd too short");
        byte version = data[0];
        if (version != 5) throw new InvalidDataException($".tmd version {version} (only 5 is known)");
        int count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[1..]));
        int tableAt = 5 + count * RecordSize;
        if (count < 0 || tableAt + 4 > data.Length) throw new InvalidDataException($".tmd count {count} does not fit");
        int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[tableAt..]));
        if (tableAt + 4 + 2 * length > data.Length) throw new InvalidDataException(".tmd string table does not fit");
        string table = Encoding.Unicode.GetString(data.Slice(tableAt + 4, 2 * length));

        string? Str(uint offset)
        {
            if (offset == uint.MaxValue) return null;
            if (offset >= table.Length) throw new InvalidDataException($".tmd string offset {offset} past the table ({table.Length})");
            int end = table.IndexOf('\0', (int)offset);
            return table[(int)offset..(end < 0 ? table.Length : end)];
        }

        var objects = new TileObject[count];
        for (int i = 0; i < count; i++)
        {
            byte[] r = data.Slice(5 + i * RecordSize, RecordSize).ToArray();
            float F(int o) => BinaryPrimitives.ReadSingleLittleEndian(r.AsSpan(o));
            uint U(int o) => BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(o));
            objects[i] = new TileObject(F(0), F(4), F(8), U(12), F(16), Str(U(20)) ?? "", Str(U(24)) ?? "", Str(U(28)) ?? "", Str(U(32)));
        }
        return objects;
    }
}

public static partial class TileObjectFile
{
    /// <summary>The older text form (UTF-16, "version 8"): one record per line, <c>cellX cellY hasPos [worldX worldY] z
    /// angle qx qy qz qw ? scale "ao" "name" "mesh" "type" [n]</c> (every one of the 3 345 records in the game files).
    /// The cell is the record's own (world / (250/23) can land one further: 2_10_6's cart blocker at world y 500 is cell
    /// 45, as the client places it); without a world position the cell's middle is the world position. An empty "ao" is
    /// none.</summary>
    public static IReadOnlyList<TileObject> ParseText(string text)
    {
        var lines = TerrainText.Lines(text);
        if (lines.Length == 0 || !lines[0].StartsWith("version")) throw new InvalidDataException("text .tmd without a version line");
        var objects = new List<TileObject>();
        foreach (var line in lines.Skip(1))
        {
            var t = TerrainText.Tokenize(line);
            if (t.Count == 0) continue;
            int q = t.FindIndex(x => x.Quoted);
            bool hasPos = q > 2 && t[2].Float != 0;
            if (q != (hasPos ? 13 : 11) || t.Count < q + 4) throw new InvalidDataException($"text .tmd record: {line}");
            var (x, y) = hasPos
                ? (t[3].Float, t[4].Float)
                : ((t[0].Float + 0.5f) * TilePlacement.WorldPerCell, (t[1].Float + 0.5f) * TilePlacement.WorldPerCell);
            int z = hasPos ? 5 : 3;
            objects.Add(new TileObject(x, y, t[z].Float, 0, t[z + 1].Float, t[q + 1].Text, t[q + 2].Text, t[q + 3].Text,
                t[q].Text.Length > 0 ? t[q].Text : null, ((int)t[0].Float, (int)t[1].Float)));
        }
        return objects;
    }
}

/// <summary>How the client places tile-local cells and angles (0x14202f1c0, verified live on Lioneye's Watch). A
/// placement code 0..7 is a rotation (code &amp; 3) quarter turns, after a mirror of x for codes 4..7.</summary>
public static class TilePlacement
{
    public const int CellsPerTile = 23;
    public const float WorldPerCell = 250f / 23f;

    // The client's 3-byte rows per code (0x142fa04b0).
    private static readonly byte[] Table = [0, 1, 1, 1, 0, 1, 0, 0, 0, 1, 1, 0, 0, 0, 1, 1, 0, 0, 0, 1, 0, 1, 1, 1];

    /// <summary>Whether the code swaps width and height.</summary>
    public static bool Swaps(int code) => Table[code * 3] == 1;

    /// <summary>The code's row of the table (the same bytes at <c>0x142fb7f78</c>, used for sub-tile objects).</summary>
    public static (int T0, int T1, int T2) Row(int code) => (Table[code * 3], Table[code * 3 + 1], Table[code * 3 + 2]);

    /// <summary>A cell (x, y) of a w × h cell area placed with <paramref name="code"/>, relative to the placed origin:
    /// a = [w - x - 1, x, h - y - 1, y]; x' = a[T1 + 2·T0], y' = a[(T0 == 0 ? 2 : 0) + T2].</summary>
    public static (int X, int Y) Cell(int code, int x, int y, int width, int height)
    {
        int t0 = Table[code * 3], t1 = Table[code * 3 + 1], t2 = Table[code * 3 + 2];
        Span<int> a = [width - x - 1, x, height - y - 1, y];
        return (a[t1 + 2 * t0], a[(t0 == 0 ? 2 : 0) + t2]);
    }

    /// <summary>An angle placed with the code: 2π − angle for codes 4..7, then + (code &amp; 3)·π/2, wrapped to [0, 2π].</summary>
    public static float Angle(int code, float angle)
    {
        const float TwoPi = MathF.PI * 2;
        if (code >= 4) angle = TwoPi - angle;
        angle += (code & 3) * (MathF.PI / 2);
        while (angle < 0) angle += TwoPi;
        while (angle > TwoPi) angle -= TwoPi;
        return angle;
    }

    /// <summary>The single code equal to placing with <paramref name="inner"/> first and then <paramref name="outer"/>.</summary>
    public static int Compose(int outer, int inner)
    {
        const int w = 5, h = 3;   // an asymmetric probe area
        var (iw, ih) = Swaps(inner) ? (h, w) : (w, h);
        for (int c = 0; c < 8; c++)
        {
            bool same = true;
            foreach (var (x, y) in new[] { (0, 0), (1, 0), (0, 1), (3, 2) })
            {
                var (ix, iy) = Cell(inner, x, y, w, h);
                if (Cell(outer, ix, iy, iw, ih) != Cell(c, x, y, w, h)) { same = false; break; }
            }
            if (same) return c;
        }
        throw new InvalidOperationException("no composed code");
    }

    /// <summary>A graph node's rotation token as a code: I, R90, R180, R270 → 0..3 and the mirrored F, FI, FR90, FR180,
    /// FR270 → 4..7 (mirror first, as codes 4..7 do). Null for a token that leaves the choice to the generator, e.g. "(any)".</summary>
    public static int? FromGraphRotation(string token)
    {
        bool flip = token.StartsWith('F');
        string rest = flip ? token[1..] : token;
        int? quarter = rest switch
        {
            "" or "I" => 0,
            "R90" => 1,
            "R180" => 2,
            "R270" => 3,
            _ => null,
        };
        return quarter is null ? null : quarter + (flip ? 4 : 0);
    }
}
