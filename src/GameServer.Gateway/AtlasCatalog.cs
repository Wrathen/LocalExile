using System.Text.Json;

/// <summary>
/// The 3.29 Atlas layouts (AtlasNode.dat + WorldAreas.dat) and the generic map keys (MapKeys.dat). A generic map item
/// names only its tier; the layout is selected on the Atlas. Until the native Atlas panel is implemented, the first
/// starting node is the default layout.
/// </summary>
sealed class AtlasCatalog(ItemCatalog catalog, string folder = "data")
{
    public sealed record Layout(string Id, ushort Hash16, string Name, int Tier, bool Starting,
        IReadOnlyList<ushort> Connections);

    readonly Lazy<IReadOnlyList<Layout>> _layouts = new(() => LoadLayouts(folder));
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, Layout> _selected = new(StringComparer.OrdinalIgnoreCase);
    readonly Lazy<IReadOnlyDictionary<uint, int>> _keyTiers = new(() => LoadKeyTiers(catalog, folder));

    public IReadOnlyList<Layout> Layouts => _layouts.Value;

    /// <summary>The current layout for a character. The Atlas panel will replace this with saved progress.</summary>
    public Layout? Selected(string? character) => character is not null && _selected.TryGetValue(character, out var chosen) ? chosen : Default;

    public void Select(string character, Layout layout) => _selected[character] = layout;

    public Layout? Default => Layouts.FirstOrDefault(l => l.Starting) ?? Layouts.FirstOrDefault();

    public Layout? ByHash16(ushort hash16) => Layouts.FirstOrDefault(l => l.Hash16 == hash16);

    /// <summary>The layouts a character can enter: the starting nodes and the nodes connected to a completed layout.
    /// The client's Atlas progress message is not implemented yet, so this is the server's progress model.</summary>
    public IReadOnlySet<ushort> Unlocked(IReadOnlySet<string> completed)
    {
        var unlocked = Layouts.Where(l => l.Starting).Select(l => l.Hash16).ToHashSet();
        foreach (string id in completed)
            if (Find(id) is { } done)
                unlocked.UnionWith(done.Connections);
        return unlocked;
    }

    public bool IsUnlocked(Layout layout, IReadOnlySet<string> completed) => Unlocked(completed).Contains(layout.Hash16);

    public Layout? Find(string idOrName) => Layouts.FirstOrDefault(l =>
        l.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase) || l.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The map tier of a generic MapKey base item, or null for an old area-named map.</summary>
    public int? TierOf(uint itemType) => _keyTiers.Value.TryGetValue(itemType, out int tier) ? tier : null;

    public uint? KeyOfTier(int tier) => _keyTiers.Value.FirstOrDefault(kv => kv.Value == tier) is { Key: not 0 } hit ? hit.Key : null;

    static IReadOnlyList<Layout> LoadLayouts(string folder)
    {
        string nodesPath = Path.Combine(folder, "tables", "AtlasNode.json"), areas = Path.Combine(folder, "tables", "WorldAreas.json");
        if (!File.Exists(nodesPath) || !File.Exists(areas)) return [];
        using var n = JsonDocument.Parse(File.ReadAllBytes(nodesPath));
        using var a = JsonDocument.Parse(File.ReadAllBytes(areas));
        var nodes = n.RootElement.EnumerateArray().ToArray();
        var byRow = new Dictionary<int, (ushort Hash16, IReadOnlyList<int> Connections)>();
        var result = new List<(int Row, Layout Layout)>();
        for (int rowIndex = 0; rowIndex < nodes.Length; rowIndex++)
        {
            var row = nodes[rowIndex];
            if (!row.GetProperty("IsNormalMap").GetBoolean() || row.GetProperty("NotOnAtlas").GetBoolean() ||
                row.GetProperty("Area1") is not { ValueKind: JsonValueKind.Number } area) continue;
            int index = area.GetInt32();
            if (index < 0 || index >= a.RootElement.GetArrayLength()) continue;
            var wa = a.RootElement[index];
            if (wa.GetProperty("HASH16") is not { ValueKind: JsonValueKind.Number } hash) continue;
            var connections = row.GetProperty("Connections").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            byRow[rowIndex] = ((ushort)hash.GetInt32(), connections);
            result.Add((rowIndex, new(row.GetProperty("Id").GetString() ?? "", (ushort)hash.GetInt32(),
                wa.GetProperty("Name").GetString() ?? "", row.GetProperty("Tier").GetInt32(),
                row.GetProperty("StartingNode").GetBoolean(), [])));
        }
        return [.. result.Select(x => x.Layout with
        {
            Connections = [.. byRow[x.Row].Connections.Where(byRow.ContainsKey).Select(c => byRow[c].Hash16)],
        })];
    }

    static IReadOnlyDictionary<uint, int> LoadKeyTiers(ItemCatalog catalog, string folder)
    {
        string path = Path.Combine(folder, "tables", "MapKeys.json");
        if (!File.Exists(path)) return new Dictionary<uint, int>();
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var result = new Dictionary<uint, int>();
        foreach (var row in doc.RootElement.EnumerateArray())
            if (row.GetProperty("BaseItemType") is { ValueKind: JsonValueKind.Number } baseRow &&
                catalog.Row(baseRow.GetInt32()) is { } type && row.GetProperty("MapTier") is { ValueKind: JsonValueKind.Number } tier)
                result.TryAdd(type.Hash, tier.GetInt32());
        return result;
    }
}
