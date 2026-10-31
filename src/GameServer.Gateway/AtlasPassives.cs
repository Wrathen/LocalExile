using System.Text.Json;

/// <summary>
/// The 3.29 Atlas passive tree's nodes. PassiveSkills.dat holds the Atlas nodes next to the character's tree; their Ids
/// start with <c>atlas_</c>. The client supplies the tree's geometry and connectivity, so the server needs the node ids
/// (PassiveSkillGraphId) for the S→C 0x3d preset lists and the C→S 0x3e/0x3f allocation requests.
/// </summary>
sealed class AtlasPassives(string folder = "data")
{
    public sealed record Node(ushort GraphId, string Id, string Name, bool IsNotable, bool IsKeystone,
        IReadOnlyList<(uint Key, int Value)> Stats);

    readonly Lazy<Dictionary<ushort, Node>> _nodes = new(() => LoadNodes(folder));

    public IReadOnlyCollection<Node> Nodes => _nodes.Value.Values;

    public int Count => _nodes.Value.Count;

    public bool Loaded => _nodes.Value.Count > 0;

    public Node? Find(ushort graphId) => _nodes.Value.GetValueOrDefault(graphId);

    public Node? FindById(string id) => _nodes.Value.Values.FirstOrDefault(n => n.Id == id);

    /// <summary>The allocated nodes' increased map drop chance (stat row 8891, key 8892).</summary>
    public int MapDropChancePercent(IEnumerable<ushort> nodes) =>
        nodes.Sum(n => Find(n)?.Stats.Where(s => s.Key == 8892).Sum(s => s.Value) ?? 0);

    static Dictionary<ushort, Node> LoadNodes(string folder)
    {
        var nodes = new Dictionary<ushort, Node>();
        string path = Path.Combine(folder, "tables", "PassiveSkills.json");
        if (!File.Exists(path)) return nodes;
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            string id = row.GetProperty("Id").GetString() ?? "";
            if (!id.StartsWith("atlas_", StringComparison.Ordinal)) continue;
            int graphId = row.GetProperty("PassiveSkillGraphId").GetInt32();
            if (graphId is <= 0 or > ushort.MaxValue) continue;
            var stats = new List<(uint Key, int Value)>();
            var keys = row.GetProperty("Stats").EnumerateArray().Select(s => s.GetInt32()).ToList();
            for (int i = 0; i < keys.Count && i < 5; i++)
                stats.Add(((uint)keys[i] + 1, row.GetProperty($"Stat{i + 1}Value").GetInt32()));
            nodes.TryAdd((ushort)graphId, new Node((ushort)graphId, id, row.GetProperty("Name").GetString() ?? "",
                row.GetProperty("IsNotable").GetBoolean(), row.GetProperty("IsKeystone").GetBoolean(), stats));
        }
        return nodes;
    }
}
