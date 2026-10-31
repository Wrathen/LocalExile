using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    // Mageblood (UniqueCatalog): the belt's unique word and the mod that says how many magic utility flasks always apply.
    // The client shows the item and enforces "cannot be used" itself (the mod); the server is the backstop here.
    // The flask buffs are kept in step by RunUniqueBeltEffects (Program.UniqueBelts.cs). Not modelled yet: the "effects
    // cannot be removed" backstop (nothing here removes a flask's effect) and the Foulborn rightmost variant.
    internal const uint MagebloodWord = 3562871479;
    internal const string MagebloodFlasksMod = "MagicUtilityFlasksAlwaysApplyUnique__1";

    /// <summary>The Mageblood the character wears (belt row 10), or null when it is not worn or its requirements are unmet.</summary>
    internal static Item? MagebloodBelt(ClientState conn)
    {
        var belt = ItemsOf(conn).Items(PlayerInventories.Belt).FirstOrDefault();
        return belt is { } worn && !IsInactive(conn, worn.Id) && IsMageblood(worn.Item) ? worn.Item : null;
    }

    internal static bool IsMageblood(Item item) =>
        item.Rarity == ItemRarity.Unique && item.NameWords.Contains(MagebloodWord);

    /// <summary>Whether the item is a magic utility flask: the flasks Mageblood's mod applies to (not normal or unique
    /// flasks, and not life/mana flasks).</summary>
    internal static bool IsMagicUtilityFlask(Item item) => IsMagicUtilityFlask(item, Items);

    internal static bool IsMagicUtilityFlask(Item item, ItemCatalog items) =>
        item.Rarity == ItemRarity.Magic && items.ByHash(item.TypeHash) is { } type && items.UtilityFlaskOf(type) is not null;

    /// <summary>How many magic utility flasks Mageblood applies: its mod's value (2-4 in 3.29).</summary>
    internal static int MagebloodFlaskCount(Item belt) => MagebloodFlaskCount(belt, Items);

    internal static int MagebloodFlaskCount(Item belt, ItemCatalog items)
    {
        foreach (var mod in belt.Mods)
            if (items.Mods.ByHash(mod.Hash) is { Id: MagebloodFlasksMod } && mod.Values.Count > 0)
                return Math.Clamp(mod.Values[0], 0, 5);
        return 0;
    }

    /// <summary>The flasks Mageblood keeps applied: the leftmost <paramref name="count"/> magic utility flasks of the
    /// flask bar, in bar order.</summary>
    internal static IReadOnlyList<InventoryItem> MagebloodFlasks(IEnumerable<InventoryItem> bar, int count) =>
        MagebloodFlasks(bar, count, Items);

    internal static IReadOnlyList<InventoryItem> MagebloodFlasks(IEnumerable<InventoryItem> bar, int count, ItemCatalog items) =>
        [.. bar.Where(i => IsMagicUtilityFlask(i.Item, items)).OrderBy(i => i.X).Take(Math.Max(0, count))];

    /// <summary>The sources of the magic utility flasks Mageblood keeps applied, or none when it is not worn. The flasks'
    /// own effect mods count; <paramref name="character"/> is the character's stats for increased flask effect/duration.</summary>
    internal static IReadOnlyList<UtilityFlasks.Source> MagebloodSources(ClientState conn, Func<string, int> character,
        int? playerLevel = null)
    {
        if (MagebloodBelt(conn) is not { } belt) return [];
        var items = ItemsFor(conn);
        int count = MagebloodFlaskCount(belt, items);
        if (count == 0) return [];
        List<UtilityFlasks.Source> sources = [];
        foreach (var flask in MagebloodFlasks(ItemsOf(conn).Items(PlayerInventories.Flask), count, items))
            if (items.ByHash(flask.Item.TypeHash) is { } type && items.UtilityFlaskOf(type) is { } utility)
                sources.AddRange(UtilityFlasks.For(utility, flask.Item, ItemModStats.Local(flask.Item, items.Mods), character, playerLevel ?? conn.Pools.Level).Stats);
        return sources;
    }

    /// <summary>A running flask and Mageblood's permanent one can be the same effect: combine both source sets before
    /// one strongest-per-(effect, stat) pass, so the effect counts once (a Granite Flask gives 1500 armour, not 3000)
    /// while different effects still stack.</summary>
    internal static IReadOnlyList<(string Stat, int Value)> CombineUtilitySources(
        IEnumerable<UtilityFlasks.Source> running, IEnumerable<UtilityFlasks.Source> permanent) =>
        UtilityFlasks.Strongest(running.Concat(permanent));

    /// <summary>The utility flask stats that count now: the running effects deduplicated against Mageblood's permanent
    /// ones. Callers apply this once, not the two sets separately.</summary>
    internal static IReadOnlyList<(string Stat, int Value)> UtilityStats(ClientState conn, Func<string, int> character,
        int? playerLevel = null) =>
        CombineUtilitySources(conn.UtilityFlaskEffects.Sources(), MagebloodSources(conn, character, playerLevel));

    /// <summary>Whether Mageblood forbids using this flask: magic utility flasks cannot be used while it is worn (normal and
    /// unique ones still can).</summary>
    internal static bool MagebloodBlocksFlaskUse(ClientState conn, Item flask) =>
        MagebloodBelt(conn) is not null && IsMagicUtilityFlask(flask, ItemsFor(conn));

    /// <summary>After the flask bar changed while Mageblood is worn: apply the new permanent flask stats now (the pools, the
    /// walk speed and the client's stats), instead of waiting for an equipment change or a relogin.</summary>
    internal static async Task RefreshMagebloodFlasks(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        if (MagebloodBelt(conn) is null) return;
        lock (conn.Monsters) RecomputePoolsAndSpeed(conn);
        await SendPlayerStatSnapshot(remote, stream, ciphers, conn,
            () => PlayerStatsUpdateAtGate(conn, PlayerStatsWithCleared(conn)),
            "0x01b3 player stats (Mageblood flasks)");
    }
}
