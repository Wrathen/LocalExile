using GameServer.Core.Protocol.Instance;

/// <summary>
/// 3.29 town gem vendors (17-npcs.md §5, 19-gems.md §5). A vendor sells the level-1 gems that a completed quest offered
/// to the character's class: a quest is complete when its reward is available (the NPCTalk row is shown) or its reward
/// was taken. The 3.29 exceptions are folded in: Siosa (after "A Fixture of Fate") and Lilly Roth (after her gem-shop
/// unlock) ignore the class restriction, and every class can buy Faster Attacks from Nessa after The Caged Brute and
/// Faster Casting from Yeena after Sharp and Cruel. Only gems the client's shop list has (<see cref="ItemCatalog.ShopGems"/>)
/// are sold, so Vaal, awakened and placeholder gems stay out.
/// </summary>
sealed class GemVendorStock(ItemCatalog items, QuestRewardCatalog rewards, NpcCatalog npcs, SkillCatalog skills, string folder = "data")
{
    static readonly string[] CurrencyIds =
    [
        "Metadata/Items/Currency/CurrencyIdentification", "Metadata/Items/Currency/CurrencyUpgradeToMagic",
        "Metadata/Items/Currency/CurrencyRerollMagic", "Metadata/Items/Currency/CurrencyUpgradeRandomly",
        "Metadata/Items/Currency/CurrencyUpgradeToRare",
    ];
    /// <summary>Lilly's gem shop opens with this flag (a6q4 "Fallen from Grace").</summary>
    public const string LillyUnlock = "UnlockedLillyGemShop";

    /// <summary>Siosa sells after a3q12 "A Fixture of Fate".</summary>
    public const string SiosaUnlock = "a3q12-DeliveredPages";

    /// <summary>The currency/flask/jewellery vendors that also sell gems (poedb "Vendor" roles), by NPCs.dat Id.</summary>
    static readonly HashSet<string> Vendors = new(StringComparer.OrdinalIgnoreCase)
    {
        "Metadata/NPC/Act1/Nessa", "Metadata/NPC/Act2/Yeena", "Metadata/NPC/Act3/ClarissaTown",
        "Metadata/NPC/Act4/PetarusVanja", "Metadata/NPC/Act5/Lani", "Metadata/NPC/Act6/Lilly",
        "Metadata/NPC/Act7/YeenaTown", "Metadata/NPC/Act8/ClarissaTown", "Metadata/NPC/Act9/PetarusVanja",
        "Metadata/NPC/Act10/Lani", "Metadata/NPC/Act10/Lilly", "Metadata/NPC/Epilogue/Lani",
        "Metadata/NPC/Epilogue/Lilly", "Metadata/NPC/Epilogue/Lilly2", "Metadata/NPC/Act3/Siosa",
    };

    public static bool Sells(string npcId) => Vendors.Contains(npcId);

    readonly HashSet<int> sellable =
    [
        .. new[] { false, true }.SelectMany(support => new[] { 1, 2, 3, 4 }
            .SelectMany(colour => items.ShopGems(colour, support))).Select(t => t.Row),
    ];

    /// <summary>The NPC's gem stock at character level/quest state: BaseItemTypes rows, in row order. Empty before the
    /// character has completed a quest that offers gems (or when Lilly/Siosa's own unlock is missing).</summary>
    public IReadOnlyList<int> Stock(string npcId, int classRow, IReadOnlySet<string> flags, bool hardMode = false, int level = 100, LeagueRules? rules = null)
    {
        bool lilly = npcId.EndsWith("/Lilly", StringComparison.OrdinalIgnoreCase)
            || npcId.EndsWith("/Lilly2", StringComparison.OrdinalIgnoreCase);
        bool siosa = npcId.EndsWith("/Siosa", StringComparison.OrdinalIgnoreCase);
        if ((lilly && !flags.Contains(LillyUnlock)) || (siosa && !flags.Contains(SiosaUnlock))) return [];
        rules ??= LeagueRules.Standard;
        if (!rules.SellsGemsAt(npcId)) return [];
        // The HardMode index lists what its vendor exchanges.
        if (hardMode)
            return [.. sellable.Intersect(HardModeGemRows(folder)).Where(row => items.Row(row) is { } type
                && rules.AllowsGemBase(type.Id)
                && items.Gem(type) is { Variants.Count: > 0 } gem && (rules.SellsSupportGems || !gem.IsSupport)
                && skills.PlayerLevelReq(gem.Variant(0).Effect, 1) <= level).Order()];
        bool ignoreClass = lilly || siosa;
        var available = npcs.AvailableRewards(flags);
        bool Done(QuestRewardCatalog.Offer offer) => flags.Contains(offer.TakenFlag) || available.Contains(offer.Row);
        var rows = new SortedSet<int>();
        foreach (var offer in rewards.Offers)
        {
            // Lilly sells the whole game's quest-reward gems once her shop opens; the others need the quest completed.
            if (!lilly && !Done(offer)) continue;
            foreach (var choice in offer.Choices)
            {
                if (choice.Level != 1) continue;
                if (!ignoreClass && choice.Classes.Count > 0 && !choice.Classes.Contains(classRow)) continue;
                if (items.Row(choice.ItemRow) is { } type && items.Gem(type) is not null && sellable.Contains(type.Row))
                    rows.Add(type.Row);
            }
        }
        if (npcId.EndsWith("/Nessa", StringComparison.OrdinalIgnoreCase) && rewards.Find(7) is { } brutus && Done(brutus))
            Add("Metadata/Items/Gems/SupportGemFasterAttack", rows);
        if (npcId.EndsWith("/Yeena", StringComparison.OrdinalIgnoreCase) && rewards.Find(15) is { } weaver && Done(weaver))
            Add("Metadata/Items/Gems/SupportGemFasterCast", rows);
        return [.. rows];
    }

    static IReadOnlySet<int> HardModeGemRows(string folder)
    {
        using var index = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "tables/IndexableSkillGems.json")));
        using var gems = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "tables/SkillGems.json")));
        return index.RootElement.EnumerateArray().Where(r => r.GetProperty("IndexRuthless").ValueKind == System.Text.Json.JsonValueKind.Number)
            .SelectMany(r => r.GetProperty("SkillGem2").EnumerateArray().Select(x => x.GetInt32()))
            .Where(row => row >= 0 && row < gems.RootElement.GetArrayLength())
            .Select(row => gems.RootElement[row].GetProperty("BaseItemTypesKey").GetInt32()).ToHashSet();
    }

    // The 3.29 cross-class supports are vendor-only additions, so they are named by their BaseItemTypes Id.
    void Add(string id, SortedSet<int> rows)
    {
        if (items.Find(id) is { } type && sellable.Contains(type.Row)) rows.Add(type.Row);
    }

    /// <summary>A vendor's price for one unlevelled gem: the currency by the gem's level-1 character level requirement
    /// (the official vendor tiers: 1–7 Scroll of Wisdom, 8–15 Orb of Transmutation, 16–27 Orb of Alteration, 28–37 Orb of
    /// Chance, 38+ Orb of Alchemy), one of it.</summary>
    public (string Currency, int Amount) Price(ItemCatalog.BaseType type)
    {
        int required = items.Gem(type) is { Variants.Count: > 0 } gem ? skills.PlayerLevelReq(gem.Variant(0).Effect, 1) : 0;
        return (CurrencyIds[Tier(required)], 1);
    }

    public static int Tier(int requiredLevel) =>
        requiredLevel <= 7 ? 0 : requiredLevel <= 15 ? 1 : requiredLevel <= 27 ? 2 : requiredLevel <= 37 ? 3 : 4;
}
