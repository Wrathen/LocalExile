using LibGGPK3;
using LibGGPK3.Records;

namespace GameServer.Data.Ggpk;

/// <summary>
/// Read-only view over a standalone <c>Content.ggpk</c>: resolves files through
/// the bundle index and decompresses bundle chunks with managed Oodle (OozSharp).
/// No native Oodle library is required. Thread-safe: reads are serialized by one lock (the caches are not concurrent).
/// </summary>
public sealed class GameFileSystem : IDisposable
{
    /// <summary>The default budget of the compressed-bundle cache.</summary>
    public const long DefaultBundleCacheBytes = 1L << 30; // 1 GiB

    private readonly long _bundleCacheBudgetBytes;

    private readonly GGPK _ggpk;
    private readonly IOodleDecoder _oodle;
    private readonly Dictionary<int, byte[]> _bundleCache = new();
    private readonly LinkedList<int> _lru = new();
    private readonly Dictionary<int, LinkedListNode<int>> _lruNodes = new();
    private long _cacheBytes;
    // Decompressed chunks, most recent first: neighbouring small files (a level's terrain files) share 256 KB chunks,
    // and decompressing one costs a round trip to the Node decoder. Their content never changes, so a chunk stays valid
    // when its bundle leaves the bundle cache.
    private const long ChunkCacheBudgetBytes = 64L << 20;
    private readonly Dictionary<(int Bundle, int Chunk), LinkedListNode<((int Bundle, int Chunk) Key, byte[] Data)>> _chunks = new();
    private readonly LinkedList<((int Bundle, int Chunk) Key, byte[] Data)> _chunkLru = new();
    private long _chunkBytes;
    // Each read bundle's layout (a few numbers): a file whose chunks are all cached needs no bundle at all.
    private readonly Dictionary<int, (int DecompressedSize, int Granularity)> _layouts = new();
    private readonly object _gate = new();
    // How many bundles were read from the GGPK (the perf benchmarks read it).
    private long _bundleReads;

    public GameIndex Index { get; }

    private GameFileSystem(GGPK ggpk, GameIndex index, IOodleDecoder oodle, long bundleCacheBytes)
    {
        _bundleCacheBudgetBytes = bundleCacheBytes;
        _ggpk = ggpk;
        Index = index;
        _oodle = oodle;
    }

    /// <param name="bundleCacheBytes">How many bytes of compressed bundles to keep (most recently used first).</param>
    public static GameFileSystem Open(string ggpkPath, IOodleDecoder? oodle = null, long bundleCacheBytes = DefaultBundleCacheBytes)
    {
        oodle ??= new ManagedOodleDecoder();
        // Read-only and shared, so the data can be read while the game client has the GGPK open.
        var ggpk = new GGPK(new FileStream(ggpkPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), false);
        try
        {
            byte[] indexRaw = ReadGgpkFile(ggpk, "Bundles2/_.index.bin");
            byte[] indexContent = BundleFile.DecompressWhole(indexRaw, oodle);
            return new GameFileSystem(ggpk, GameIndex.Parse(indexContent), oodle, bundleCacheBytes);
        }
        catch
        {
            var stream = ggpk.UnsafeGetStream();
            ggpk.Dispose();
            stream.Dispose(); // LibGGPK3's Dispose returns early for a read-only stream.
            throw;
        }
    }

    public bool TryReadFile(string path, out byte[] data)
    {
        if (!Index.TryGetFile(path, out var loc))
        {
            data = Array.Empty<byte>();
            return false;
        }
        int bundle = loc.BundleIndex;
        lock (_gate)
        {
            if (_layouts.TryGetValue(bundle, out var layout) && TryCachedSlice(bundle, layout, loc.Offset, loc.Size, out data))
                return true;
            byte[] raw = GetBundle(bundle);
            _layouts[bundle] = BundleFile.Layout(raw);
            data = BundleFile.DecompressSlice(raw, loc.Offset, loc.Size,
                (chunk, compressed, size) => DecompressChunk(bundle, chunk, compressed, size));
        }
        return true;
    }

    // The slice from the decompressed-chunk cache alone, when every chunk it covers is there.
    private bool TryCachedSlice(int bundle, (int DecompressedSize, int Granularity) layout, int offset, int size, out byte[] data)
    {
        data = [];
        if (size <= 0 || layout.Granularity <= 0 || offset < 0 || offset + size > layout.DecompressedSize) return false;
        int first = offset / layout.Granularity, last = (offset + size - 1) / layout.Granularity;
        for (int chunk = first; chunk <= last; chunk++)
            if (!_chunks.ContainsKey((bundle, chunk))) return false;
        data = new byte[size];
        int written = 0;
        for (int chunk = first; chunk <= last; chunk++)
        {
            var node = _chunks[(bundle, chunk)];
            _chunkLru.Remove(node);
            _chunkLru.AddFirst(node);
            int start = chunk * layout.Granularity;
            int begin = Math.Max(offset - start, 0), end = Math.Min(offset + size - start, layout.Granularity);
            // The same checks as BundleFile.DecompressSlice's.
            if (node.Value.Data.Length < end)
                throw new GgpkFormatException($"chunk produced {node.Value.Data.Length} bytes, need {end}");
            node.Value.Data.AsSpan(begin, end - begin).CopyTo(data.AsSpan(written));
            written += end - begin;
        }
        if (written != size)
            throw new GgpkFormatException($"bundle slice incomplete: wrote {written} of {size} bytes");
        return true;
    }

    private byte[] DecompressChunk(int bundle, int chunk, ReadOnlySpan<byte> compressed, int size)
    {
        if (_chunks.TryGetValue((bundle, chunk), out var node))
        {
            _chunkLru.Remove(node);
            _chunkLru.AddFirst(node);
            return node.Value.Data;
        }
        byte[] data = _oodle.Decompress(compressed.ToArray(), size);
        _chunks[(bundle, chunk)] = _chunkLru.AddFirst(((bundle, chunk), data));
        _chunkBytes += data.Length;
        while (_chunkBytes > ChunkCacheBudgetBytes && _chunkLru.Last is { } last)
        {
            _chunkLru.RemoveLast();
            _chunks.Remove(last.Value.Key);
            _chunkBytes -= last.Value.Data.Length;
        }
        return data;
    }

    /// <summary>Every file path in the bundle index (decompresses the path table once per call).</summary>
    public IEnumerable<string> EnumeratePaths() => Index.Paths(BundleFile.DecompressWhole(Index.PathTableBundle, _oodle));

    public byte[] ReadFile(string path)
    {
        if (!TryReadFile(path, out var data))
            throw new FileNotFoundException($"file not present in bundle index: {path}", path);
        return data;
    }

    /// <summary>Drops every cached bundle, decompressed chunk and bundle layout.</summary>
    public void ClearBundleCache()
    {
        lock (_gate)
        {
            _bundleCache.Clear();
            _lru.Clear();
            _lruNodes.Clear();
            _cacheBytes = 0;
            _chunks.Clear();
            _chunkLru.Clear();
            _chunkBytes = 0;
            _layouts.Clear();
        }
    }

    private byte[] GetBundle(int index)
    {
        if (_bundleCache.TryGetValue(index, out var cached))
        {
            Touch(index);
            return cached;
        }

        byte[] raw = ReadGgpkFile(_ggpk, Index.BundleFiles[index]);
        _bundleReads++;
        _bundleCache[index] = raw;
        _lruNodes[index] = _lru.AddFirst(index);
        _cacheBytes += raw.Length;

        while (_cacheBytes > _bundleCacheBudgetBytes && _lru.Last is { } last)
        {
            int evict = last.Value;
            _lru.RemoveLast();
            _lruNodes.Remove(evict);
            if (_bundleCache.Remove(evict, out var evicted))
                _cacheBytes -= evicted.Length;
        }

        return raw;
    }

    private void Touch(int index)
    {
        if (_lruNodes.TryGetValue(index, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
        }
    }

    private static byte[] ReadGgpkFile(GGPK ggpk, string path)
    {
        if (ggpk.Root.TryFindNode(path, out var node) && node is FileRecord fr)
            return fr.Read();
        throw new FileNotFoundException($"file not found in GGPK: {path}", path);
    }

    /// <summary>Read a raw (undecoded) file straight from the GGPK container, bypassing bundles.</summary>
    public static byte[] ReadRawGgpkFile(string ggpkPath, string path)
    {
        using var ggpk = new GGPK(ggpkPath);
        return ReadGgpkFile(ggpk, path);
    }

    public void Dispose()
    {
        var stream = _ggpk.UnsafeGetStream();
        try { _ggpk.Dispose(); }
        finally { stream.Dispose(); }
    }
}
