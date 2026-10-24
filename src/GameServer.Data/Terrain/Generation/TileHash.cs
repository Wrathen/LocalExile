using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace GameServer.Data.Terrain.Generation;

/// <summary>The "Tile hash" the client logs after generating an area and sends in C→S 0x7d (<c>0x141d8a460</c>,
/// Agents/docs/re/011-level-generation.md §6). Equal hashes mean the same tiles in the same places.</summary>
public static class TileHash
{
    const uint M = 0x5bd1e995, Seed = 0xf36b12;

    /// <summary>The hash of a tile grid. <paramref name="at"/> gives the tile whose entry is at (x, y), or null for
    /// an empty entry or one the client skips (the +0x34 / +0x35 flags). Entries are visited column by column.</summary>
    public static uint Compute(int width, int height, Func<int, int, (string Path, short Height, byte Rotation)?> at)
    {
        var parts = new List<uint>(width * height);
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (at(x, y) is { } t) parts.Add(Tile(t.Path, t.Height, t.Rotation, (uint)x, (uint)y));
        return Murmur2(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(parts)), Seed);
    }

    /// <summary>One tile's part: MurmurHash2 of the five u32 (height, rotation, x, y, hash of the tile's path). The
    /// client inlines it (its constant 0x9bfbc07e is (seed ^ 20) · m).</summary>
    public static uint Tile(string path, short height, byte rotation, uint x, uint y)
    {
        Span<byte> b = stackalloc byte[20];
        BinaryPrimitives.WriteInt32LittleEndian(b, height);
        BinaryPrimitives.WriteUInt32LittleEndian(b[4..], rotation);
        BinaryPrimitives.WriteUInt32LittleEndian(b[8..], x);
        BinaryPrimitives.WriteUInt32LittleEndian(b[12..], y);
        BinaryPrimitives.WriteUInt32LittleEndian(b[16..], Murmur2(MemoryMarshal.AsBytes(path.AsSpan()), Seed));
        return Murmur2(b, Seed);
    }

    static uint K(uint k)
    {
        k *= M;
        return (k >> 24 ^ k) * M;
    }

    /// <summary>MurmurHash2 (32-bit) over bytes: <c>0x140df8570</c>; over a UTF-16 string <c>0x1406db330</c> is the
    /// same function on its bytes.</summary>
    public static uint Murmur2(ReadOnlySpan<byte> data, uint seed)
    {
        uint h = seed ^ (uint)data.Length;
        int i = 0;
        for (; data.Length - i >= 4; i += 4) h = h * M ^ K(BinaryPrimitives.ReadUInt32LittleEndian(data[i..]));
        switch (data.Length - i)
        {
            case 3: h ^= (uint)data[i + 2] << 16; goto case 2;
            case 2: h ^= (uint)data[i + 1] << 8; goto case 1;
            case 1: h = (data[i] ^ h) * M; break;
        }
        h = (h >> 13 ^ h) * M;
        return h >> 15 ^ h;
    }
}
