using System.Buffers.Binary;
using System.Text;

namespace GameServer.Data.Terrain.Generation;

/// <summary>A .datc64 table read by byte offset, the way the client reads its rows in memory (an in-memory row is the
/// file's row: strings and lists are 8/16 bytes there too). The generator's port quotes the client's offsets, so it
/// reads them directly instead of through a schema.</summary>
public sealed class RawDat
{
    readonly byte[] _data;
    readonly int _rowsAt, _varAt;

    public RawDat(byte[] data)
    {
        _data = data;
        Count = (int)BinaryPrimitives.ReadUInt32LittleEndian(data);
        _rowsAt = 4;
        _varAt = data.AsSpan().IndexOf(stackalloc byte[] { 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb });
        RowSize = Count == 0 ? 0 : (_varAt - 4) / Count;
    }

    public int Count { get; }
    public int RowSize { get; }

    ReadOnlySpan<byte> Row(int row) => _data.AsSpan(_rowsAt + row * RowSize, RowSize);

    public byte U8(int row, int at) => Row(row)[at];
    public bool Bool(int row, int at) => Row(row)[at] != 0;
    public int I32(int row, int at) => BinaryPrimitives.ReadInt32LittleEndian(Row(row)[at..]);
    public ushort U16(int row, int at) => BinaryPrimitives.ReadUInt16LittleEndian(Row(row)[at..]);

    /// <summary>A foreign row (16 bytes: u64 index, u64): the index, or -1 for none.</summary>
    public int Ref(int row, int at)
    {
        ulong v = BinaryPrimitives.ReadUInt64LittleEndian(Row(row)[at..]);
        return v == 0xfefefefefefefefe || v > int.MaxValue ? -1 : (int)v;
    }

    /// <summary>A row of the same table (8 bytes: u64 index): the index, or -1 for none.</summary>
    public int Ref8(int row, int at)
    {
        ulong v = BinaryPrimitives.ReadUInt64LittleEndian(Row(row)[at..]);
        return v > int.MaxValue ? -1 : (int)v;
    }

    public string String(int row, int at) => StringAt(BinaryPrimitives.ReadUInt64LittleEndian(Row(row)[at..]));

    /// <summary>A list (u64 count, u64 offset) of strings.</summary>
    public string[] StringList(int row, int at) => List(row, at, 8, (d, i) => BinaryPrimitives.ReadUInt64LittleEndian(d[i..])).Select(StringAt).ToArray();

    string StringAt(ulong offset)
    {
        var sb = new StringBuilder();
        for (int i = _varAt + (int)offset; i + 1 < _data.Length; i += 2)
        {
            char c = (char)BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(i));
            if (c == 0) break;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>A list (u64 count, u64 offset) of i32.</summary>
    public int[] I32List(int row, int at) => List(row, at, 4, (d, i) => BinaryPrimitives.ReadInt32LittleEndian(d[i..]));

    /// <summary>A list of foreign rows (16 bytes each): their indexes.</summary>
    public int[] RefList(int row, int at) => List(row, at, 16, (d, i) => (int)BinaryPrimitives.ReadUInt64LittleEndian(d[i..]));

    T[] List<T>(int row, int at, int size, Func<ReadOnlySpan<byte>, int, T> read)
    {
        var r = Row(row);
        int count = (int)BinaryPrimitives.ReadUInt64LittleEndian(r[at..]);
        int offset = (int)BinaryPrimitives.ReadUInt64LittleEndian(r[(at + 8)..]);
        var result = new T[count];
        var d = _data.AsSpan(_varAt + offset);
        for (int i = 0; i < count; i++) result[i] = read(d, i * size);
        return result;
    }

    public int Find(Func<int, bool> match)
    {
        for (int i = 0; i < Count; i++) if (match(i)) return i;
        return -1;
    }
}
