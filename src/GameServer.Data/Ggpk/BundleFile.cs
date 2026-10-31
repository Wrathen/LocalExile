using System.Buffers.Binary;

namespace GameServer.Data.Ggpk;

/// <summary>
/// Reader for the Game bundle container (<c>*.bundle.bin</c>), including the
/// special <c>_.index.bin</c>. Format per Game-tool-dev/ggpk.discussion wiki.
///
/// Header:
///   i32 @ 0   decompressed data size
///   i32 @ 36  chunk count
///   i32 @ 40  compression granularity (uncompressed bytes per chunk)
///   i32 @ 60  chunk compressed sizes[chunkCount]
///   data      starts at 60 + chunkCount*4
/// Each chunk is Oodle compressed (Kraken/Mermaid/Selkie/...); the last chunk holds the remainder.
/// </summary>
public static class BundleFile
{
    private const int DecompressedSizeOffset = 0;
    private const int ChunkCountOffset = 36;
    private const int GranularityOffset = 40;
    private const int ChunkSizesOffset = 60;

    public static int DecompressedSize(ReadOnlySpan<byte> bundle)
        => BinaryPrimitives.ReadInt32LittleEndian(bundle[DecompressedSizeOffset..]);

    /// <summary>A bundle's decompressed size and its chunk size (every chunk but the last decompresses to it).</summary>
    public static (int DecompressedSize, int Granularity) Layout(ReadOnlySpan<byte> bundle)
        => (DecompressedSize(bundle), BinaryPrimitives.ReadInt32LittleEndian(bundle[GranularityOffset..]));

    public static byte[] DecompressWhole(ReadOnlySpan<byte> bundle, IOodleDecoder decoder)
    {
        int size = DecompressedSize(bundle);
        return DecompressSlice(bundle, 0, size, decoder);
    }

    /// <summary>Decompresses chunk <paramref name="chunk"/> of a bundle (its compressed bytes) to
    /// <paramref name="decompressedSize"/> bytes; the result may be a cached array the caller must not change.</summary>
    public delegate byte[] ChunkDecoder(int chunk, ReadOnlySpan<byte> compressed, int decompressedSize);

    public static byte[] DecompressSlice(ReadOnlySpan<byte> bundle, int sliceOffset, int sliceSize, IOodleDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        return DecompressSlice(bundle, sliceOffset, sliceSize, (_, compressed, size) => decoder.Decompress(compressed.ToArray(), size));
    }

    public static byte[] DecompressSlice(ReadOnlySpan<byte> bundle, int sliceOffset, int sliceSize, ChunkDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        if (sliceSize < 0)
            throw new GgpkFormatException($"negative slice size {sliceSize}");

        int decompressedSize = BinaryPrimitives.ReadInt32LittleEndian(bundle[DecompressedSizeOffset..]);
        int chunkCount = BinaryPrimitives.ReadInt32LittleEndian(bundle[ChunkCountOffset..]);
        int granularity = BinaryPrimitives.ReadInt32LittleEndian(bundle[GranularityOffset..]);

        var result = new byte[sliceSize];
        int chunkBegin = ChunkSizesOffset + (chunkCount * 4);
        int decompOffset = 0;
        int destOffset = 0;

        // Only the chunks the slice overlaps are copied and decompressed: a terrain file is a few KB of a bundle of
        // hundreds of chunks, and copying every chunk for each read was most of a level generation's time.
        for (int idx = 0; idx < chunkCount && decompOffset < sliceOffset + sliceSize; idx++)
        {
            int chunkSize = BinaryPrimitives.ReadInt32LittleEndian(bundle.Slice(ChunkSizesOffset + (idx * 4), 4));

            int decompChunkSize = (idx == chunkCount - 1)
                ? decompressedSize - decompOffset
                : granularity;

            if (decompChunkSize > 0
                && Math.Max(decompOffset, sliceOffset) < Math.Min(decompOffset + decompChunkSize, sliceOffset + sliceSize))
            {
                int begin = Math.Max(sliceOffset - decompOffset, 0);
                int end = Math.Min(sliceOffset + sliceSize - decompOffset, decompChunkSize);

                byte[] raw = decoder(idx, bundle.Slice(chunkBegin, chunkSize), decompChunkSize);
                if (raw.Length < end)
                    throw new GgpkFormatException($"chunk produced {raw.Length} bytes, need {end}");

                raw.AsSpan(begin, end - begin).CopyTo(result.AsSpan(destOffset));
                destOffset += end - begin;
            }

            decompOffset += decompChunkSize;
            chunkBegin += chunkSize;
        }

        if (destOffset != sliceSize)
            throw new GgpkFormatException($"bundle slice incomplete: wrote {destOffset} of {sliceSize} bytes");

        return result;
    }
}
