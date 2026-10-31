partial class Program
{
    /// <summary>
    /// What a chat command's argument of a kind can be (<see cref="ChatCommands.Add"/>'s arguments), from the catalogs the
    /// commands look it up in, for the launcher's console (`GameServer.Gateway values &lt;kind&gt;`). Each value is what
    /// the command finds: a name where only one row has it, else the row's Id. Rows the server cannot create are left
    /// out. Null for a kind there is none of.
    /// </summary>
    internal static IEnumerable<(string Value, string Detail)>? CompletionValues(string kind) => kind switch
    {
        "area" => AreaTransitionCatalog.Areas.Where(a => a.Id != "NULL").OrderBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
            .Select(a => (a.Id, $"{a.Name} · {AreaSupport(a.Id)}")),
        "monster" => Named(Monsters.All.Where(v => v.Supported), v => v.Name, v => v.Id),
        "item" => Uniques.All.Select(u => (u.Name, "unique"))
            .Concat(Named(Items.All.Where(t => t.Supported && !t.Name.StartsWith('[') && Items.Gem(t) is null), t => t.Name, t => t.Id)),
        "gem" => Items.GemNames().Order(StringComparer.OrdinalIgnoreCase).Select(n => (n, "")),
        "stat" => Stats.Names.Select((n, i) => (n, $"key {i + 1}")),
        "npc" => Named(Npcs.Value.Rows.Where(n => n.Supported), n => n.Name, n => n.Id),
        "quest_flag" => File.Exists(Path.Combine("data", "tables", "QuestFlags.json"))
            ? Quests.ParseRows(File.ReadAllText(Path.Combine("data", "tables", "QuestFlags.json"))).Keys.Select(id => (id, ""))
            : [],
        _ => null,
    };

    /// <summary>Rows by name where the name is theirs alone, else by Id (the lookups take either); the other as detail.</summary>
    internal static IEnumerable<(string Value, string Detail)> Named<T>(IEnumerable<T> rows, Func<T, string> name, Func<T, string> id)
    {
        var list = rows.Where(r => name(r).Length > 0).ToList();
        var counts = list.GroupBy(name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        return list.Select(r => counts[name(r)] == 1 ? (name(r), id(r)) : (id(r), name(r)))
            .OrderBy(v => v.Item1, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>How the Gateway lays an area out: it generates it, a walk-map capture has it, or only the client can.</summary>
    internal static string AreaSupport(string id) =>
        ServerLevels.IsSupported(id) ? "server-built"
        : Directory.Exists(Path.Combine("data", "walkability"))
            && Directory.EnumerateFiles(Path.Combine("data", "walkability"), $"{id}_seed*.bin").Any(f => !f.EndsWith(".projectile.bin", StringComparison.Ordinal))
            ? "captured" : "client-only";
}
