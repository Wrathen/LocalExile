using GameServer.Core.Protocol.Instance;

/// <summary>
/// What a dying monster drops, rolled in stages like the game's (Agents/docs/protocol/15-progression.md §3); the numbers
/// are our own, chosen to feel like it, until the game's are known:
/// 1. How many items: normal monsters 1 at 35%, magic 1–2, rare 2–4, unique 4–6, times the item quantity (a league's,
///    a mod's, the player's increased quantity).
/// 2. For each, what kind: currency (35%: <see cref="Currency"/>'s weights, from Scrolls of Wisdom down to a Mirror), a
///    gem (4%: an obtainable gem whose DropLevel the monster's level meets, unlevelled like a vendor's), a life or mana
///    flask (8%) or equipment (53%).
/// 3. Equipment: which slot (<see cref="Slots"/>: armour often, jewellery seldom), then any base of it whose DropLevel the
///    monster's level meets, alike. Bases the game keeps out of the drop pools are left out: those tagged not_for_sale
///    (unique-only bases such as "Ring", Stygian Vise, league and Heist bases), except the atlas bases, which drop in maps.
/// 4. Its rarity (<see cref="RarityChances"/>, raised by item rarity): unique (one of that base's uniques by
///    <see cref="UniqueWeight"/>, else rare), rare, magic or normal, in that order; magic, rare and unique items drop
///    unidentified, at the monster's level plus <see cref="ItemLevelBonus"/>.
/// </summary>
sealed class ItemDrops(ItemCatalog catalog, ItemRolls? rolls = null, string folder = "data", bool ruthless = false, AtlasCatalog? maps = null,
    UniqueCatalog? uniques = null, UniqueWeightProvider? uniqueWeights = null)
{
    readonly UniqueWeightProvider _uniqueWeightProvider = uniqueWeights ?? UniqueWeightProvider.Default;

    /// <summary>A dropped piece of equipment's chances by the monster's rarity, each of what the earlier ones left: first
    /// unique, then rare, then magic.</summary>
    public readonly record struct Chances(double Unique, double Rare, double Magic);

    /// <summary>What a monster's rarity adds to the item level of its drops (the project's rule): magic 1, rare 2, unique 3.
    /// The level that decides which bases and currency can drop stays the monster's.</summary>
    public static int ItemLevelBonus(MonsterRarity monster) => monster switch
    {
        MonsterRarity.Magic => 1,
        MonsterRarity.Rare => 2,
        MonsterRarity.Unique => 3,
        _ => 0,
    };

    public static Chances RarityChances(MonsterRarity monster) => monster switch
    {
        MonsterRarity.Magic => new(0.006, 0.10, 0.35),
        MonsterRarity.Rare => new(0.015, 0.25, 0.40),
        MonsterRarity.Unique => new(0.05, 0.50, 0.30),
        _ => new(0.003, 0.05, 0.25),
    };

    /// <summary>The chances with item rarity: each × (1 + increased / 100) × the multiplier (a league's, a mod's), at most
    /// 1. As in the game, more rarity makes every better rarity likelier, uniques too; past 1 the better one takes all.</summary>
    public static Chances WithIncreasedRarity(Chances chances, int increased, double multiplier = 1)
    {
        double factor = Math.Max(0, 100 + increased) / 100.0 * (double.IsFinite(multiplier) ? Math.Max(0, multiplier) : 1);
        return new(Math.Min(1, chances.Unique * factor), Math.Min(1, chances.Rare * factor), Math.Min(1, chances.Magic * factor));
    }

    // (BaseItemTypes Id, weight), each only from its own DropLevel: Scrolls of Wisdom most, then the basic orbs, down to the
    // Exalted and Divine Orbs and the Mirror of Kalandra.
    static readonly (string Id, double Weight)[] Currency =
    [
        ("Metadata/Items/Currency/CurrencyIdentification", 1000), ("Metadata/Items/Currency/CurrencyPortal", 300),
        ("Metadata/Items/Currency/CurrencyUpgradeToMagic", 500), ("Metadata/Items/Currency/CurrencyArmourQuality", 450),
        ("Metadata/Items/Currency/CurrencyWeaponQuality", 300), ("Metadata/Items/Currency/CurrencyAddModToMagic", 400),
        ("Metadata/Items/Currency/CurrencyRerollMagic", 250), ("Metadata/Items/Currency/CurrencyRerollSocketColours", 200),
        ("Metadata/Items/Currency/CurrencyRerollSocketNumbers", 150), ("Metadata/Items/Currency/CurrencyUpgradeRandomly", 120),
        ("Metadata/Items/Currency/CurrencyUpgradeToRare", 90), ("Metadata/Items/Currency/CurrencyFlaskQuality", 60),
        ("Metadata/Items/Currency/CurrencyRerollSocketLinks", 50), ("Metadata/Items/Currency/CurrencyRerollRare", 40),
        ("Metadata/Items/Currency/CurrencyConvertToNormal", 30), ("Metadata/Items/Currency/CurrencyPassiveRefund", 25),
        ("Metadata/Items/Currency/CurrencyCorrupt", 25), ("Metadata/Items/Currency/CurrencyUpgradeMagicToRare", 20),
        ("Metadata/Items/Currency/CurrencyGemQuality", 20), ("Metadata/Items/Currency/CurrencyRerollImplicit", 15),
        ("Metadata/Items/Currency/CurrencyAddModToRare", 2), ("Metadata/Items/Currency/CurrencyRemoveMod", 1.5),
        ("Metadata/Items/Currency/CurrencyModValues", 1), ("Metadata/Items/Currency/CurrencyDuplicate", 0.01),
    ];

    // Equipment slots (ItemClasses Id) and how often each is picked: armour pieces most, then weapons, jewellery seldom.
    static readonly (string Class, double Weight)[] Slots =
    [
        ("Body Armour", 10), ("Helmet", 10), ("Gloves", 10), ("Boots", 10), ("Shield", 6),
        ("One Hand Sword", 2), ("Thrusting One Hand Sword", 1.5), ("One Hand Axe", 2), ("One Hand Mace", 2), ("Sceptre", 2),
        ("Dagger", 2), ("Rune Dagger", 1), ("Claw", 2), ("Wand", 2),
        ("Two Hand Sword", 2), ("Two Hand Axe", 2), ("Two Hand Mace", 2), ("Staff", 2), ("Warstaff", 1), ("Bow", 3), ("Quiver", 2.5),
        ("Ring", 3), ("Amulet", 1.5), ("Belt", 2.5),
    ];
    internal static readonly HashSet<string> EquipmentClasses = [.. Slots.Select(s => s.Class)];

    // Bases that exist in the tables but never drop (event, league and test items) and carry no not_for_sale tag.
    static readonly string[] NotDropped = ["Royale", "Demigod", "Talisman", "Breach", "Synthesis", "Fishing", "Test", "Unarmed", "Atlas",
        "Expedition", "Heist", "Relic", "Basilisk", "Crusader", "Eyrie", "Elder", "Shaper", "Necropolis", "Sanctum", "Ultimatum", "Legion"];

    /// <summary>Whether a base can be in the general drop table: not tagged not_for_sale (the atlas bases aside), not one
    /// of the event and league bases, and with a name and a drop level.</summary>
    public static bool Droppable(ItemCatalog.BaseType type) =>
        type.Supported && type.DropLevel >= 1 && !string.IsNullOrEmpty(type.Name)
        && (!type.Tags.Contains("not_for_sale") || type.Tags.Contains("atlas_base_type"))
        && !NotDropped.Any(n => type.Id.Contains(n) && !(n == "Atlas" && type.Tags.Contains("atlas_base_type")));

    sealed record Pools(List<(ItemCatalog.BaseType Type, double Weight)> Currency,
        Dictionary<string, List<ItemCatalog.BaseType>> Equipment, List<ItemCatalog.BaseType> Flasks, List<ItemCatalog.BaseType> Gems);

    private readonly Lazy<Pools> _pools = new(() => Build(catalog, folder, ruthless));

    // The uniques by base type Id, for the catalogue they were built from (the database may replace it while running).
    sealed record UniqueIndex(IReadOnlyList<UniqueCatalog.Definition> From, Dictionary<string, List<UniqueCatalog.Definition>> ByBase);
    volatile UniqueIndex? _uniques;

    public sealed record Drop(Item Item, ItemCatalog.BaseType Type);

    /// <summary>The items a monster of this level and rarity drops (possibly none). The quantity multiplier scales how
    /// many: each of the monster's rolls happens floor(multiplier) times, and once more with the fraction's chance (below
    /// 1, that chance alone: Ruthless's 0.2). The rarity multiplier scales every rarity chance, as increased rarity does.</summary>
    public IReadOnlyList<Drop> Roll(int monsterLevel, MonsterRarity rarity, Random rng, int increasedRarity = 0,
        double quantityMultiplier = 1, double rarityMultiplier = 1)
    {
        double quantity = double.IsFinite(quantityMultiplier) ? Math.Max(0, quantityMultiplier) : 1;
        int whole = (int)Math.Min(Math.Floor(quantity), int.MaxValue);
        double part = quantity < 1 ? quantity : quantity - Math.Floor(quantity);
        int count = rarity switch
        {
            MonsterRarity.Magic => rng.Next(1, 3),
            MonsterRarity.Rare => rng.Next(2, 5),
            MonsterRarity.Unique => rng.Next(4, 7),
            _ => rng.NextDouble() < 0.35 ? 1 : 0,
        };
        var chances = WithIncreasedRarity(RarityChances(rarity), increasedRarity,
            rarityMultiplier * SystemSettings.Default.DropMultiplier("unique_rarity"));
        var drops = new List<Drop>();
        for (int i = 0; i < count; i++)
        {
            // No draw for a whole multiplier, so the ordinary 1 rolls exactly as before.
            int times = whole + ((quantity < 1 || part > 0) && rng.NextDouble() < part ? 1 : 0);
            for (int r = 0; r < times; r++)
                if (RollOne(monsterLevel, monsterLevel + ItemLevelBonus(rarity), chances, rng) is { } d) drops.Add(d);
        }
        return drops;
    }

    /// <summary>The gold item (ItemClasses "Gold"): on the ground a pile of <see cref="Item.StackSize"/> gold, which a pickup
    /// adds to the stash's gold instead of the inventory.</summary>
    public const string GoldId = "Metadata/Items/Currency/GoldCoin", GoldClass = "Gold";

    /// <summary>The gold a dying monster drops, 0 for none: normal monsters 40% of the time, magic 75%, rare and unique
    /// always; (3 + 1.2 × level) × 1 / 2 / 5 / 15 by rarity, ±25%. Our own numbers until the game's are known.</summary>
    public static int GoldAmount(int monsterLevel, MonsterRarity rarity, Random rng)
    {
        double chance = rarity switch { MonsterRarity.Normal => 0.40, MonsterRarity.Magic => 0.75, _ => 1 };
        chance = Math.Min(1, chance * SystemSettings.Default.GoldChanceMultiplier);
        if (chance <= 0 || rng.NextDouble() >= chance) return 0;
        double times = rarity switch { MonsterRarity.Magic => 2, MonsterRarity.Rare => 5, MonsterRarity.Unique => 15, _ => 1 };
        return Math.Max(1, (int)Math.Round((3 + 1.2 * Math.Max(1, monsterLevel)) * times * (0.75 + 0.5 * rng.NextDouble())));
    }

    /// <summary>A pile of gold for a dying monster (<see cref="GoldAmount"/>), or null for none.</summary>
    public Drop? Gold(int monsterLevel, MonsterRarity rarity, Random rng, double multiplier = 1) =>
        (int)Math.Floor(GoldAmount(monsterLevel, rarity, rng) * Math.Max(0, multiplier)) is int amount and > 0 && catalog.Find(GoldId) is { Supported: true } type
            ? new Drop(ItemCatalog.Create(type) with { StackSize = (ushort)Math.Min(amount, ushort.MaxValue) }, type) : null;

    /// <summary>The King in the Mists' four guaranteed uniques, by the wiki's crowdsourced 3.26 shares (n=576). They are
    /// absolute weights in one pick, so the sum is the distribution and the boss always drops exactly one of them.</summary>
    public static readonly IReadOnlyList<(string Name, double Weight)> KingInMistsUniques =
    [
        ("The Untouched Soul", 40), ("Pragmatism", 35), ("The Light of Meaning", 20), ("The Burden of Shadows", 5),
    ];

    /// <summary>The boss-only gem drops (wiki 3.28: Hextoad 6%, Hexpass 10%) and the drop-restricted card.</summary>
    public static readonly IReadOnlyList<(string Id, double Chance)> KingInMistsExtras =
    [
        ("Metadata/Items/Gems/SupportGemHextoad", 0.06),
        ("Metadata/Items/Gems/SupportGemHexpass", 0.10),
        ("Metadata/Items/DivinationCards/DivinationCardTheEndlessDarkness", 0.15),
    ];

    /// <summary>The King in the Mists' guaranteed unique, then its support gems and card. Corpse items (the 50% roll)
    /// are not modelled yet: the item system has no itemised-corpse consumer.</summary>
    public IReadOnlyList<Drop> KingInMists(int level, Random rng)
    {
        var drops = new List<Drop>();
        if (uniques is not null && KingInMistsUniques.Sum(u => u.Weight) is > 0 and var total)
        {
            double pick = rng.NextDouble() * total;
            var chosen = KingInMistsUniques[^1].Name;
            foreach (var (name, weight) in KingInMistsUniques)
            {
                if (pick < weight) { chosen = name; break; }
                pick -= weight;
            }
            if (uniques.Create(chosen, rng) is { } item && catalog.ByHash(item.TypeHash) is { } type)
                drops.Add(new(item with { ItemLevel = (byte)Math.Clamp(level, 1, 100), Identified = false }, type));
        }
        foreach (var (id, chance) in KingInMistsExtras)
            if (rng.NextDouble() < chance && catalog.Find(id) is { Supported: true } type)
                drops.Add(new(ItemCatalog.Create(type) with { ItemLevel = (byte)Math.Clamp(level, 1, 100) }, type));
        return drops;
    }

    /// <summary>An approximate generic-map drop: a map of the current map tier (or tier 1 in the campaign). Our own
    /// rates until the game pools are known.</summary>
    public Drop? Map(int monsterLevel, MonsterRarity monster, int mapTier, Random rng)
    {
        double chance = monster switch { MonsterRarity.Normal => 0.004, MonsterRarity.Magic => 0.012, MonsterRarity.Rare => 0.05, _ => 0.20 };
        chance = Math.Min(1, chance * SystemSettings.Default.DropMultiplier("maps"));
        if (maps is null || chance <= 0 || rng.NextDouble() >= chance) return null;
        int tier = Math.Clamp(mapTier > 0 ? mapTier : 1, 1, 16);
        if (maps.KeyOfTier(tier) is not uint hash || catalog.ByHash(hash) is not { Supported: true } type) return null;
        var item = ItemCatalog.Create(type) with { ItemLevel = (byte)Math.Clamp(monsterLevel + ItemLevelBonus(monster), 1, 100) };
        if (rolls is not null && rng.NextDouble() < 0.10) item = rolls.Roll(item, ItemRarity.Magic, rng) with { Identified = true };
        return new(item, type);
    }

    // One item: its kind, for equipment its slot and base, then its rarity (the class summary's stages 2 to 4).
    Drop? RollOne(int level, int itemLevel, Chances chances, Random rng)
    {
        var pools = _pools.Value;
        double currencyWeight = 0.35 * SystemSettings.Default.DropMultiplier("currency");
        double gemWeight = 0.04 * SystemSettings.Default.DropMultiplier("gems");
        double flaskWeight = 0.08 * SystemSettings.Default.DropMultiplier("flasks");
        double equipmentWeight = 0.53 * SystemSettings.Default.DropMultiplier("equipment");
        double totalWeight = currencyWeight + gemWeight + flaskWeight + equipmentWeight;
        if (totalWeight <= 0) return null;
        double kind = rng.NextDouble() * totalWeight;
        ItemCatalog.BaseType? type = null;
        if (kind < currencyWeight)
            type = Pick(pools.Currency.Where(c => c.Type.DropLevel <= level).ToList(), rng);
        else if (kind < currencyWeight + gemWeight && pools.Gems.Where(g => g.DropLevel <= level).ToList() is { Count: > 0 } g)
            type = g[rng.Next(g.Count)];
        else if (kind < currencyWeight + gemWeight + flaskWeight && pools.Flasks.Where(f => f.DropLevel <= level).ToList() is { Count: > 0 } f)
            type = f[rng.Next(f.Count)];
        else
        {
            // The slot first (among those with a base this level allows), then one of its bases.
            var slots = Slots.Where(s => pools.Equipment.GetValueOrDefault(s.Class)?.Any(t => t.DropLevel <= level) == true).ToList();
            if (Pick(slots, rng) is { } slot && pools.Equipment[slot].Where(t => t.DropLevel <= level).ToList() is { Count: > 0 } bases)
                type = bases[rng.Next(bases.Count)];
        }
        if (type is null) return null;
        var (sockets, links) = catalog.DefaultSockets(type, itemLevel);
        var item = ItemCatalog.Create(type, sockets, links, random: rng) with
        {
            // A dropped gem is unlevelled, like a vendor's: its level comes from its experience (ItemCatalog.GemLevel).
            ItemLevel = catalog.Gem(type) is not null ? (byte)1 : (byte)Math.Clamp(itemLevel, 1, 100),
        };
        if (rolls is not null && EquipmentClasses.Contains(type.Class))
        {
            // Magic, rare and unique items drop unidentified (a Scroll of Wisdom identifies them, C→S 0x71).
            bool rolledUnique = rng.NextDouble() < chances.Unique;
            if (rolledUnique && Unique(type, itemLevel, rng) is { } unique)
            {
                // The unique's own base row (an Id can name another row than the one rolled).
                item = unique with { Identified = false };
                type = catalog.ByHash(item.TypeHash) ?? type;
            }
            else if (rolledUnique || rng.NextDouble() < chances.Rare) item = rolls.Roll(item, ItemRarity.Rare, rng) with { Identified = false };
            else if (rng.NextDouble() < chances.Magic) item = rolls.Roll(item, ItemRarity.Magic, rng) with { Identified = false };
        }
        return new Drop(item, type);
    }

    // A unique of this base at the monster's level, or null when the base has none the server can create (then the item
    // rolls on as rare or magic, as the game makes it rare).
    Item? Unique(ItemCatalog.BaseType type, int level, Random rng)
    {
        if (uniques is null) return null;
        var all = uniques.All;
        var index = _uniques;
        if (index is null || !ReferenceEquals(index.From, all))
        {
            var byBase = new Dictionary<string, List<UniqueCatalog.Definition>>(StringComparer.Ordinal);
            foreach (var definition in all)
            {
                if (!byBase.TryGetValue(definition.BaseTypeId, out var list)) byBase[definition.BaseTypeId] = list = [];
                list.Add(definition);
            }
            _uniques = index = new(all, byBase);
        }
        if (!index.ByBase.TryGetValue(type.Id, out var candidates) || PickUnique(candidates, rng) is not { } chosen) return null;
        return uniques.Create(chosen.Name, rng) is { } created ? created with { ItemLevel = (byte)Math.Clamp(level, 1, 100) } : null;
    }

    /// <summary>One of a base's uniques by weight, or null. The weights are absolute: while a base's add up to less than 1
    /// the rest is no unique, so a base whose only unique is a Mageblood still gives one seldom.</summary>
    internal UniqueCatalog.Definition? PickUnique(IReadOnlyList<UniqueCatalog.Definition> candidates, Random rng)
    {
        double pick = rng.NextDouble() * Math.Max(1, candidates.Sum(d => UniqueWeight(d.Name)));
        foreach (var definition in candidates)
        {
            double weight = UniqueWeight(definition.Name);
            if (pick < weight) return definition;
            pick -= weight;
        }
        return null;
    }

    /// <summary>How likely a unique is once its base rolled one: the inverse of its disenchant value
    /// (VillageUniqueDisenchantValues, Ruthless has its own column), the client's own measure of how rare a unique is:
    /// Wanderlust 1.06, Kaom's Heart 43, Mageblood 891. The table leaves out old names and some non-equipment; those do not
    /// drop. The Gateway publishes the seeded/overridden weights through <see cref="UniqueWeightProvider"/>; without a
    /// reference table the extracted JSON is the fallback and every unique weighs 1.</summary>
    internal double UniqueWeight(string name) =>
        _uniqueWeightProvider.HasTable
            ? _uniqueWeightProvider.Weight(name, ruthless)
            : _uniqueWeightsFile.Value is { Count: > 0 } weights ? weights.GetValueOrDefault(name) : 1;

    private readonly Lazy<Dictionary<string, double>> _uniqueWeightsFile = new(() => LoadUniqueWeights(folder, ruthless));

    static Dictionary<string, double> LoadUniqueWeights(string folder, bool ruthless)
    {
        var weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        string values = Path.Combine(folder, "tables", "VillageUniqueDisenchantValues.json"), words = Path.Combine(folder, "tables", "Words.json");
        if (!File.Exists(values) || !File.Exists(words)) return weights;
        using var wordsDoc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(words));
        using var valuesDoc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(values));
        foreach (var row in valuesDoc.RootElement.EnumerateArray())
        {
            double value = row.GetProperty(ruthless ? "RuthlessValue" : "Value").GetDouble();
            if (value > 0 && wordsDoc.RootElement[row.GetProperty("UniqueName").GetInt32()].GetProperty("Text").GetString() is { Length: > 0 } text)
                weights[text] = Math.Min(1, 1 / value);
        }
        return weights;
    }

    static T? Pick<T>(List<(T Value, double Weight)> weighted, Random rng) where T : class
    {
        double total = weighted.Sum(w => Math.Max(0, w.Weight));
        if (total <= 0) return null;
        double pick = rng.NextDouble() * total;
        foreach (var (value, weight) in weighted)
        {
            if (pick < weight) return value;
            pick -= Math.Max(0, weight);
        }
        return weighted[^1].Value;
    }

    private static Pools Build(ItemCatalog catalog, string folder, bool ruthless)
    {
        var currency = new List<(ItemCatalog.BaseType, double)>();
        foreach (var (id, weight) in Currency)
            if (catalog.Find(id) is { Supported: true } t && t.Id == id) currency.Add((t, weight));
        var equipment = new Dictionary<string, List<ItemCatalog.BaseType>>(StringComparer.Ordinal);
        var flasks = new List<ItemCatalog.BaseType>();
        for (int row = 0; catalog.Row(row) is { } t; row++)
        {
            if (!Droppable(t)) continue;
            if (EquipmentClasses.Contains(t.Class))
            {
                if (!equipment.TryGetValue(t.Class, out var list)) equipment[t.Class] = list = [];
                list.Add(t);
            }
            else if (t.Class is "LifeFlask" or "ManaFlask") flasks.Add(t);
        }
        // Obtainable gems only (GemSources): the data's Graft, legacy and placeholder rows are not normal drops.
        var pool = GemSources.DropPool(catalog, folder);
        var gems = new List<ItemCatalog.BaseType>();
        foreach (int row in pool)
            if (catalog.Row(row) is { DropLevel: >= 1 } type && (!ruthless || RuthlessGemRules.AllowsBase(type.Id)))
                gems.Add(type);
        return new(currency, equipment, flasks, gems);
    }
}
