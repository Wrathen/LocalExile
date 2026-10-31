using GameServer.Core.Protocol.Instance;

/// <summary>What an item is besides its mods, for /item_state: the states Agents/docs/protocol/13-items.md §2 places in
/// the Base and Mods components. Currency that creates them in play is separate work.</summary>
static class ItemStates
{
    /// <summary>The item with a state switched, or null when there is no such state. "clear" removes them all.</summary>
    public static Item? Toggle(Item item, string state) => state.ToLowerInvariant() switch
    {
        "corrupted" => item with { Corrupted = !item.Corrupted },
        "mirrored" => item with { Mirrored = !item.Mirrored },
        "split" => item with { Split = !item.Split },
        "synthesised" or "synthesized" => item with { Synthesised = !item.Synthesised },
        "clear" => item with
        {
            Corrupted = false, Mirrored = false, Split = false, Synthesised = false, Influences = ItemInfluence.None,
        },
        _ => char.IsLetter(state[0]) && Enum.TryParse<ItemInfluence>(state, true, out var influence)
            && influence != ItemInfluence.None ? item with { Influences = item.Influences ^ influence } : null,
    };

    public static string Describe(Item item)
    {
        var states = new List<string>();
        if (item.Corrupted) states.Add("corrupted");
        if (item.Mirrored) states.Add("mirrored");
        if (item.Split) states.Add("split");
        if (item.Synthesised) states.Add("synthesised");
        states.AddRange(Enum.GetValues<ItemInfluence>().Where(i => i != ItemInfluence.None && item.Influences.HasFlag(i))
            .Select(i => i.ToString().ToLowerInvariant()));
        return states.Count == 0 ? "No item states" : "Item states: " + string.Join(", ", states);
    }
}
