using System.Text.Json;

/// <summary>
/// Shared parses of the big game tables (data/tables/*.json) that several catalogues read: Stats.json (the stat and mod
/// catalogues), MonsterVarieties.json (the monster and monster-mod catalogues), GrantedEffects.json (the skill
/// catalogue's tables, for the standard and the Ruthless catalogue). They load in bursts (the first player, the first
/// area with monsters), so a table opened again within <see cref="Keep"/> of its last use is not parsed again. A lease
/// is a document's user: dispose it like the document (<c>using var d = GameTables.Open(path)</c>); the document is
/// disposed when nobody has used it for <see cref="Keep"/>, or by <see cref="Release"/>. A file written since is parsed
/// again (the key holds its size and write time).
/// </summary>
static class GameTables
{
    /// <summary>How long an unused parse is kept for the next catalogue of the burst.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromSeconds(2);

    internal sealed class Entry(JsonDocument document)
    {
        public readonly JsonDocument Document = document;
        public int Users;
        public Timer? Expiry;
    }

    static readonly Lock Gate = new();
    static readonly Dictionary<(string Path, long Length, DateTime Written), Entry> Entries = [];
    static long _parses, _reuses;

    /// <summary>How many tables were parsed, and how many opens reused a parse (the perf benchmarks read them).</summary>
    public static (long Parses, long Reuses) Counts => (Interlocked.Read(ref _parses), Interlocked.Read(ref _reuses));

    /// <summary>A use of the table's parse; dispose it when done with its elements.</summary>
    public sealed class Lease : IDisposable
    {
        readonly (string, long, DateTime) key;
        Entry? entry;

        internal Lease((string, long, DateTime) key, Entry entry) { this.key = key; this.entry = entry; }

        public JsonElement RootElement => (entry ?? throw new ObjectDisposedException(nameof(Lease))).Document.RootElement;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref entry, null) is not { } used) return;
            lock (Gate)
                if (--used.Users == 0)
                    used.Expiry = new Timer(_ => Expire(key, used), null, Keep, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>The table at <paramref name="path"/>, parsed now or shared with another user of the same file.</summary>
    public static Lease Open(string path)
    {
        var file = new FileInfo(path);
        var key = (file.FullName, file.Length, file.LastWriteTimeUtc);
        lock (Gate)
            if (Entries.TryGetValue(key, out var shared))
            {
                shared.Users++;
                shared.Expiry?.Dispose();
                shared.Expiry = null;
                Interlocked.Increment(ref _reuses);
                return new(key, shared);
            }
        // Parsed outside the lock; a concurrent parse of the same file keeps the first one.
        var document = JsonDocument.Parse(File.ReadAllBytes(path));
        Interlocked.Increment(ref _parses);
        lock (Gate)
        {
            if (Entries.TryGetValue(key, out var raced))
            {
                document.Dispose();
                raced.Users++;
                raced.Expiry?.Dispose();
                raced.Expiry = null;
                return new(key, raced);
            }
            var entry = new Entry(document) { Users = 1 };
            Entries[key] = entry;
            return new(key, entry);
        }
    }

    /// <summary>Disposes every parse nobody is using now (the Gateway calls it once its startup loads are done).</summary>
    public static void Release()
    {
        lock (Gate)
            foreach (var (key, entry) in Entries.Where(e => e.Value.Users == 0).ToList())
            {
                entry.Expiry?.Dispose();
                Entries.Remove(key);
                entry.Document.Dispose();
            }
    }

    static void Expire((string, long, DateTime) key, Entry entry)
    {
        lock (Gate)
        {
            if (entry.Users > 0 || !Entries.TryGetValue(key, out var current) || !ReferenceEquals(current, entry)) return;
            Entries.Remove(key);
            entry.Expiry?.Dispose();
            entry.Document.Dispose();
        }
    }
}
