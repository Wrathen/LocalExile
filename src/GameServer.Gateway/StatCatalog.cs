using System.Text.Json;

/// <summary>
/// Stat names and the client's stat keys: key = Stats.dat row + 1 (Agents/docs/protocol/11-skills.md §5). Reads
/// data/tables/Stats.json (game data, loaded on first use); without it only numeric keys work.
/// </summary>
sealed class StatCatalog(string folder = "data")
{
    private readonly Lazy<List<string>> _names = new(() =>
    {
        string path = Path.Combine(folder, "tables", "Stats.json");
        if (!File.Exists(path)) return [];
        using var d = GameTables.Open(path);
        return [.. d.RootElement.EnumerateArray().Select(r => r.GetProperty("Id").GetString() ?? "")];
    });

    // Name -> key, built once; also caches names that are absent (null), because DamageStats asks for ~100 names per
    // hit and a repeated scan of 23 000 names would be wasteful.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, uint?> _keys = new(StringComparer.Ordinal);
    private bool _keysBuilt;

    /// <summary>A key from a name ("attack_speed_+%"), a decimal key or a 0x hex key.</summary>
    public uint? Key(string text)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, null, out uint hex)) return hex;
        if (uint.TryParse(text, out uint key)) return key;
        if (_keys.TryGetValue(text, out uint? found)) return found;
        if (!_keysBuilt)
            lock (_keys)
                if (!_keysBuilt)
                {
                    for (int i = 0; i < _names.Value.Count; i++) _keys.TryAdd(_names.Value[i], (uint)i + 1);
                    _keysBuilt = true;
                }
        if (_keys.TryGetValue(text, out found)) return found;
        _keys.TryAdd(text, null);
        return null;
    }

    public string Name(uint key) => key >= 1 && key <= _names.Value.Count ? _names.Value[(int)key - 1] : $"0x{key:x}";
}
