using System.Text.Json;
using GameServer.Core.Protocol.Instance;

/// <summary>
/// Magic and rare items (Agents/docs/protocol/13-items.md §2b). Affixes come from Mods.dat: the base's ModDomain,
/// GenerationType 1 (prefix) or 2 (suffix), Level at most the item level, not essence-only, a weight above 0 for the
/// item's tags (<see cref="ModCatalog.Mod.WeightFor"/>) and no family already on the item. Each affix is drawn by weight
/// from every mod still allowed, so prefixes and suffixes compete. Magic: 1-2 affixes, at most one of each. Rare: 4-6,
/// at most three of each, and a name from Words.dat word lists 1 (prefix) and 2 (suffix), drawn by the same tag weights.
/// </summary>
sealed class ItemRolls(ItemCatalog catalog, string folder = "data")
{
    public const int Prefix = 1, Suffix = 2;

    /// <summary>Mods.dat GenerationType for the two Eldritch influences: Searing Exarch and Eater of Worlds.</summary>
    public const int ExarchImplicit = 28, EaterImplicit = 29;

    /// <summary>A Words.dat row: the key the client looks it up by (HASH32), its text and spawn weights.</summary>
    public sealed record Word(uint Key, string Text, int List, IReadOnlyList<(string Tag, int Weight)> SpawnWeights);

    private readonly Lazy<List<Word>> _words = new(() => LoadWords(folder));
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int Row, int Level), IReadOnlyList<(ModCatalog.Mod, int)>> _pools = new();

    /// <summary>The Enkindling enchantments a utility flask can receive (Mods.dat GenerationType 21).</summary>
    public IReadOnlyList<(ModCatalog.Mod Mod, int Weight)> EnkindlingPool(ItemCatalog.BaseType type) =>
        FlaskEnchantPool(type, 21);

    /// <summary>The Instilling trigger enchantments a utility flask can receive (Mods.dat GenerationType 22).</summary>
    public IReadOnlyList<(ModCatalog.Mod Mod, int Weight)> InstillingPool(ItemCatalog.BaseType type) =>
        FlaskEnchantPool(type, 22);

    /// <summary>Enkindling Orb: one of the five official enchantments, replacing an existing Enkindling (and any
    /// Harvest/bench enchantment) while keeping an existing Instilling.</summary>
    public Item? Enkindling(Item item, Random random) => FlaskEnchant(item, 21, random);

    /// <summary>Instilling Orb: one of the fifteen spawnable trigger enchantments, replacing an existing Instilling (and
    /// any Harvest/bench enchantment) while keeping an existing Enkindling.</summary>
    public Item? Instilling(Item item, Random random) => FlaskEnchant(item, 22, random);

    /// <summary>Flask enchantment pool: Mods.dat Domain 1, the Enkindling (21) or Instilling (22) generation type, and a
    /// positive spawn weight for the flask's tags (the game gives the "flask" tag; zero-weight rows never spawn).</summary>
    IReadOnlyList<(ModCatalog.Mod Mod, int Weight)> FlaskEnchantPool(ItemCatalog.BaseType type, int generationType) =>
        [.. catalog.Mods.All
            .Where(m => m.Domain == 1 && m.GenerationType == generationType)
            .Select(m => (Mod: m, Weight: m.WeightFor(type.Tags))).Where(p => p.Weight > 0)];

    /// <summary>Official flask enchantment: only utility flasks (not life or mana flasks), any rarity, unmodifiable,
    /// corrupted and mirrored targets refused. Enkindling (21) and Instilling (22) are separate enchantments and keep
    /// each other; every other enchantment (the Harvest/bench family, other enchant-list mods) is replaced. The rest of
    /// the item stays as it was.</summary>
    Item? FlaskEnchant(Item item, int generationType, Random random)
    {
        if (!item.Components.Contains("Flask") || !TryModifiable(item, out var type) || !type.Tags.Contains("utility_flask"))
            return null;
        var pool = FlaskEnchantPool(type, generationType);
        if (pool.Count == 0) return null;
        int otherType = generationType == 21 ? 22 : 21;
        var kept = item.Mods.Where(m =>
        {
            if (catalog.Mods.ByHash(m.Hash) is not { } mod) return m.Section != ModSection.Enchant;
            if (m.Section == ModSection.Enchant) return mod.GenerationType == otherType;
            return !(mod.ModType.Contains("FlaskEnchantment", StringComparison.Ordinal) && mod.GenerationType != otherType);
        }).ToList();
        kept.Add(Pick(pool, random).Roll(ModSection.Enchant, random));
        return item with { Mods = kept };
    }

    /// <summary>The affixes that can roll on this base at this item level.</summary>
    public IReadOnlyList<(ModCatalog.Mod Mod, int Weight)> Pool(ItemCatalog.BaseType t, int itemLevel) =>
        _pools.GetOrAdd((t.Row, itemLevel), _ => [.. catalog.Mods.All
            .Where(m => m.Domain == t.ModDomain && m.GenerationType is Prefix or Suffix && m.Level <= itemLevel && !m.EssenceOnly)
            .Select(m => (m, m.WeightFor(t.Tags))).Where(p => p.Item2 > 0)]);

    /// <summary>The item and its base accept affix changes: the item is not corrupted or mirrored, has Mods, and its
    /// base is known, serializable, has Mods and is not unmodifiable.</summary>
    bool TryModifiable(Item item, out ItemCatalog.BaseType type)
    {
        type = null!;
        if (item.Corrupted || item.Mirrored || !item.Components.Contains("Mods") ||
            catalog.ByHash(item.TypeHash) is not { } found || !found.Supported || !found.Components.Contains("Mods") ||
            found.Unmodifiable) return false;
        type = found;
        return true;
    }

    /// <summary>Transmutation's validated normal-to-magic upgrade. Refused targets return null before rolling or
    /// spending: no unknown/unmodifiable bases or empty ordinary affix pools. Ordinary magic counts stay 50/50.</summary>
    public Item? Transmute(Item item, Random random)
    {
        if (item.Rarity != ItemRarity.Normal || !TryModifiable(item, out var type) ||
            Pool(type, item.ItemLevel).Count == 0) return null;
        return Roll(item, ItemRarity.Magic, random, respectMetamods: true) with { Identified = true };
    }

    /// <summary>Augmentation's validated one-affix addition to a magic item. Refused targets return null before
    /// rolling or spending: no unknown/unmodifiable bases, and no free weighted affix (a full item, or an open side
    /// with no eligible mods).</summary>
    public Item? Augment(Item item, Random random) =>
        item.Rarity == ItemRarity.Magic && TryModifiable(item, out _) ? AddAffix(item, 1, random) : null;

    /// <summary>Regal Orb's validated magic-to-rare upgrade: the magic affixes stay, one more comes from the rare
    /// pool (at most three of each side), and a rare name is drawn. Flasks are magic-only and cannot become rare.</summary>
    public Item? Regal(Item item, Random random)
    {
        if (item.Rarity != ItemRarity.Magic || item.Components.Contains("Flask") || !TryModifiable(item, out var type))
            return null;
        return AddAffix(item, MaxAffixesPerSide(type), random) is not { } added ? null
            : added with { Rarity = ItemRarity.Rare, Identified = true, NameWords = RareName(type, random) };
    }

    /// <summary>Alteration's validated magic reroll: 1-2 new affixes, at most one of each side, each drawn by weight.
    /// Refused targets return null before rolling or spending: no unknown/unmodifiable bases and no empty ordinary
    /// pool (which would otherwise leave a magic item with no affixes).</summary>
    public Item? Alter(Item item, Random random)
    {
        if (item.Rarity != ItemRarity.Magic || !TryModifiable(item, out var type) ||
            Pool(type, item.ItemLevel).Count == 0) return null;
        return Roll(item, ItemRarity.Magic, random, respectMetamods: true) with { Identified = true };
    }

    /// <summary>Alchemy's validated normal-to-rare upgrade: 4-6 weighted affixes and a rare name. Flasks are
    /// magic-only and cannot become rare.</summary>
    public Item? Alchemy(Item item, Random random)
    {
        if (item.Rarity != ItemRarity.Normal || item.Components.Contains("Flask") || !TryModifiable(item, out var type) ||
            Pool(type, item.ItemLevel).Count == 0) return null;
        return Roll(item, ItemRarity.Rare, random) with { Identified = true };
    }

    /// <summary>Exalted Orb's validated rare addition: the affixes and name stay and one more weighted affix is
    /// added within the base's rare limits. Flasks are magic-only and cannot be rare.</summary>
    public Item? Exalted(Item item, Random random)
    {
        if (item.Rarity != ItemRarity.Rare || item.Components.Contains("Flask") || !TryModifiable(item, out var type))
            return null;
        return AddAffix(item, MaxAffixesPerSide(type), random);
    }

    /// <summary>Chaos Orb's validated rare reroll: every unprotected explicit affix is replaced by 4-6 new weighted
    /// ones, a new rare name is drawn, and metamod-protected affixes stay. Flasks are magic-only and cannot be
    /// rare.</summary>
    public Item? Chaos(Item item, Random random)
    {
        if (item.Rarity != ItemRarity.Rare || item.Components.Contains("Flask") || !TryModifiable(item, out var type) ||
            Pool(type, item.ItemLevel).Count == 0) return null;
        return Roll(item, ItemRarity.Rare, random, respectMetamods: true) with { Identified = true };
    }

    /// <summary>The rare per-side limit: jewel classes allow two prefixes and two suffixes, other rare bases three.</summary>
    static int MaxAffixesPerSide(ItemCatalog.BaseType type) => type.Class is "Jewel" or "AbyssJewel" ? 2 : 3;

    /// <summary>The Eater of Worlds implicits an Eldritch Ichor of this tier can add.</summary>
    public IReadOnlyList<(ModCatalog.Mod Mod, int Weight)> EldritchIchorPool(ItemCatalog.BaseType type, int tier) =>
        EldritchPool(type, EaterImplicit, tier);

    /// <summary>The Searing Exarch implicits an Eldritch Ember of this tier can add.</summary>
    public IReadOnlyList<(ModCatalog.Mod Mod, int Weight)> EldritchEmberPool(ItemCatalog.BaseType type, int tier) =>
        EldritchPool(type, ExarchImplicit, tier);

    /// <summary>The tier's ordinary and Unique/Pinnacle Presence variants of one Eldritch influence that the base's
    /// tags allow.</summary>
    IReadOnlyList<(ModCatalog.Mod Mod, int Weight)> EldritchPool(ItemCatalog.BaseType type, int influence, int tier)
    {
        if (tier is < 1 or > 4) return [];
        return [.. catalog.Mods.All
            .Where(m => m.GenerationType == influence && IsEldritchTier(m.Id, tier))
            .Select(m => (m, m.WeightFor(type.Tags))).Where(p => p.Item2 > 0)];
    }

    static bool IsEldritchTier(string id, int tier) =>
        id.EndsWith($"EldritchImplicit{tier}") || id.EndsWith($"UniquePresence{tier}") ||
        id.EndsWith($"PinnaclePresence{tier}");

    /// <summary>Eldritch Ichor: adds an Eater of Worlds implicit of the ichor's tier to eligible armour, replacing the
    /// other implicit modifiers except Searing Exarch ones.</summary>
    public Item? EldritchIchor(Item item, ItemCatalog.BaseType type, int tier, Random random) =>
        EldritchImplicit(item, type, tier, EaterImplicit, ExarchImplicit, random);

    /// <summary>Eldritch Ember: adds a Searing Exarch implicit of the ember's tier to eligible armour, replacing the
    /// other implicit modifiers except Eater of Worlds ones.</summary>
    public Item? EldritchEmber(Item item, ItemCatalog.BaseType type, int tier, Random random) =>
        EldritchImplicit(item, type, tier, ExarchImplicit, EaterImplicit, random);

    Item? EldritchImplicit(Item item, ItemCatalog.BaseType type, int tier, int influence, int keptInfluence, Random random)
    {
        if (tier is < 1 or > 4 || item.Rarity is not (ItemRarity.Normal or ItemRarity.Magic or ItemRarity.Rare) ||
            type.Class is not ("Helmet" or "Body Armour" or "Gloves" or "Boots") ||
            !TryModifiable(item, out _)) return null;
        var pool = EldritchPool(type, influence, tier);
        if (pool.Count == 0) return null;
        var chosen = Pick(pool, random);
        var kept = item.Mods.Where(m => m.Section != ModSection.Implicit ||
            catalog.Mods.ByHash(m.Hash)?.GenerationType == keptInfluence).ToList();
        int at = kept.FindLastIndex(m => m.Section == ModSection.Implicit) + 1;
        kept.Insert(at, chosen.Roll(ModSection.Implicit, random));
        return item with { Mods = kept };
    }

    /// <summary>Orb of Chance, the documented LocalExile rule: a modifiable normal weapon or armour piece becomes
    /// rare (30%, 4-6 affixes and a rare name) or magic (2-4 affixes, at most two of each side). Refused targets
    /// return null before rolling or spending, including missing/empty pools.</summary>
    public Item? Chance(Item item, Random random)
    {
        if (item.Rarity != ItemRarity.Normal ||
            !(item.Components.Contains("Weapon") || item.Components.Contains("Armour")) ||
            !TryModifiable(item, out var type) || Pool(type, item.ItemLevel).Count == 0) return null;
        return (random.Next(100) < 30 ? Roll(item, ItemRarity.Rare, random)
            : Roll(item, ItemRarity.Magic, random, (random.Next(2, 5), 2))) with { Identified = true };
    }

    /// <summary>Vaal Orb, the documented LocalExile rule: a weapon, armour piece or jewellery (ring, amulet, belt)
    /// that is modifiable, not unique and not corrupted/mirrored is always corrupted. A normal or magic item becomes
    /// rare with 4-6 affixes; a rare one replaces 1-5 affixes or rerolls all of them. One white socket is coloured.
    /// Refused targets return null before rolling or spending, including missing/empty pools.</summary>
    public Item? Vaal(Item item, ItemCatalog.BaseType type, (int Str, int Dex, int Int) requirements, Random random)
    {
        bool target = item.Components.Contains("Weapon") || item.Components.Contains("Armour") ||
            type.Class is "Ring" or "Amulet" or "Belt";
        if (!target || item.Rarity == ItemRarity.Unique || !TryModifiable(item, out _) ||
            Pool(type, item.ItemLevel).Count == 0) return null;
        int roll = random.Next(100);
        Item result = item.Rarity != ItemRarity.Rare || roll >= 90 ? Roll(item, ItemRarity.Rare, random)
            : ReplaceAffixes(item, roll switch { < 20 => 1, < 40 => 2, < 60 => 3, < 80 => 4, _ => 5 }, random);
        var sockets = result.Sockets.ToArray();
        var whites = Enumerable.Range(0, sockets.Length).Where(i => sockets[i].Colour == SocketColour.White).ToArray();
        if (whites.Length > 0)
        {
            int at = whites[random.Next(whites.Length)];
            sockets[at] = sockets[at] with { Colour = SocketColours.Colour(requirements, random) };
            result = result with { Sockets = sockets };
        }
        return result with { Corrupted = true, Identified = true };
    }

    /// <summary>The item with this rarity and freshly rolled affixes (its implicits and other lists kept, earlier
    /// explicits replaced); a rare item also gets a name. Types without a Mods component, or normal / unique
    /// rarity, only get the rarity. <paramref name="shape"/> overrides the affix count and the most of each kind.
    /// Fractured mods stay, first, and count towards the affixes.</summary>
    public Item Roll(Item item, ItemRarity rarity, Random rng, (int Count, int MaxEach)? shape = null,
        ModCatalog.Mod? guaranteed = null, int? maxModLevel = null, bool respectMetamods = false) =>
        RollCore(item, rarity, rng, shape, guaranteed, maxModLevel, respectMetamods, null, null)!;

    /// <summary>A Harvest reforge that must include one modifier with this Tags.dat tag (fire, life, attack �?�). Null when
    /// the base has no room for one at this item level.</summary>
    public Item? RollRequiredTag(Item item, ItemRarity rarity, Random rng, string tag) =>
        rarity == ItemRarity.Rare ? RollCore(item, rarity, rng, null, null, null, false, tag, null) : null;

    /// <summary>A Harvest "more/less likely to receive the same modifier types" reforge. <paramref name="percent"/> is the
    /// client parameter (900 = ten times the chance of a tag the item already has, -90 = a tenth); a local, documented
    /// weighting of the ordinary pool, not an official probability.</summary>
    public Item? RollAffinity(Item item, ItemRarity rarity, Random rng, int percent) =>
        rarity == ItemRarity.Rare ? RollCore(item, rarity, rng, null, null, null, false, null, percent) : null;

    Item? RollCore(Item item, ItemRarity rarity, Random rng, (int Count, int MaxEach)? shape,
        ModCatalog.Mod? guaranteed, int? maxModLevel, bool respectMetamods, string? requiredTag, int? affinityPercent)
    {
        var fractured = item.FracturedIndexes();
        var rules = new MetacraftRules(item, catalog.Mods);
        var kept = item.Mods.Where((m, i) => m.Section != ModSection.Explicit || fractured.Contains(i) || respectMetamods && rules.Protected(i)).ToList();
        var result = item with { Rarity = rarity, Mods = kept, NameWords = [] };
        if (catalog.ByHash(item.TypeHash) is not { } t || !t.Components.Contains("Mods") || rarity is not (ItemRarity.Magic or ItemRarity.Rare))
            return result;
        // CoE beta rare counts: affixNumberGeneration[6] = [2/3, 1/4, 1/12] for 4, 5 or 6 affixes.
        // Jewel classes cap at 4 (2 per side), affixNumberGeneration[4] = [.65,.35] for 3 or 4 affixes.
        var (count, maxEach) = shape ?? (rarity == ItemRarity.Magic
            ? (rng.Next(1, 3), 1)
            : t.Class is "Jewel" or "AbyssJewel"
                ? (rng.Next(20) < 13 ? 3 : 4, MaxAffixesPerSide(t))
                : (rng.Next(12) switch { < 8 => 4, < 11 => 5, _ => 6 }, MaxAffixesPerSide(t)));
        var generation = new MetacraftRules(item with { Mods = kept }, catalog.Mods);
        var pool = Pool(t, Math.Min(item.ItemLevel, maxModLevel ?? item.ItemLevel))
            .Where(p => !respectMetamods || generation.Allows(p.Mod)).ToList();
        var chosen = kept.Where(m => m.Section == ModSection.Explicit).Select(m => catalog.Mods.ByHash(m.Hash)).OfType<ModCatalog.Mod>().ToList();
        int keptCount = chosen.Count;
        if (guaranteed is not null) chosen.Add(guaranteed);
        bool Fits(ModCatalog.Mod mod, int max) => chosen.Count(c => c.GenerationType == mod.GenerationType) < max &&
            !chosen.Any(c => c.Hash == mod.Hash || c.Families.Intersect(mod.Families).Any());
        if (requiredTag is not null)
        {
            var required = pool.Where(p => p.Mod.Tags.Contains(requiredTag) && Fits(p.Mod, maxEach)).ToList();
            if (required.Count == 0) return null;
            chosen.Add(Pick(required, rng));
        }
        // The affinity recipes weight the rest of the pool by whether it shares a tag with the item's current affixes.
        var affinityTags = affinityPercent is null ? null
            : kept.Where(m => m.Section == ModSection.Explicit).Select(m => catalog.Mods.ByHash(m.Hash)).OfType<ModCatalog.Mod>()
                .SelectMany(m => m.Tags).ToHashSet();
        while (chosen.Count < count)
        {
            var allowed = pool.Where(p => Fits(p.Mod, maxEach)).ToList();
            if (affinityPercent is { } percent && affinityTags is { Count: > 0 })
                allowed = [.. allowed.Select(p => (p.Mod, Weight: affinityTags.Overlaps(p.Mod.Tags)
                    ? Math.Max(1, p.Weight * (100 + percent) / 100) : p.Weight))];
            if (allowed.Count == 0 || allowed.All(p => p.Weight <= 0)) break;
            chosen.Add(Pick(allowed, rng));
        }
        // Prefixes first, as the tooltip lists them.
        var explicits = chosen.Skip(keptCount).OrderBy(m => m.GenerationType).Select(m => m.Roll(ModSection.Explicit, rng));
        return result with { Mods = [.. kept, .. explicits], NameWords = rarity == ItemRarity.Rare ? RareName(t, rng) : [] };
    }

    /// <summary>Divine Orb: every explicit mod keeps its place and tier and rerolls only its values (a new tier would
    /// change the mod, and the client reorders the tooltip by mod). Null when the item has no explicit mods.</summary>
    public Item? Reforge(Item item, Random rng)
    {
        var rules = new MetacraftRules(item, catalog.Mods);
        bool Mutable(int i) => item.Mods[i].Section == ModSection.Explicit && !rules.Protected(i) && catalog.Mods.ByHash(item.Mods[i].Hash) is not null;
        if (!Enumerable.Range(0, item.Mods.Count).Any(Mutable)) return null;
        return item with { Mods = [.. item.Mods.Select((m, i) => Mutable(i) ? catalog.Mods.ByHash(m.Hash)!.Roll(ModSection.Explicit, rng) : m)] };
    }

    public Item? Scour(Item item)
    {
        if (item.Rarity is not (ItemRarity.Magic or ItemRarity.Rare)) return null;
        var rules = new MetacraftRules(item, catalog.Mods);
        var kept = item.Mods.Where((m, i) => m.Section != ModSection.Explicit || rules.Protected(i)).ToArray();
        int count = kept.Count(m => m.Section == ModSection.Explicit);
        var rarity = count == 0 ? ItemRarity.Normal : count == 1 ? ItemRarity.Magic : ItemRarity.Rare;
        if (kept.Length == item.Mods.Count && rarity == item.Rarity) return null;
        return item with { Mods = kept, Rarity = rarity, Identified = true, NameWords = rarity == ItemRarity.Rare ? item.NameWords : [] };
    }

    /// <summary>Annulment's validated explicit-affix removal: one random unprotected explicit leaves a magic or rare
    /// item; rarity and name stay. Refused targets return null before removing or spending: non-magic/rare,
    /// corrupted/mirrored, unknown/unsupported/unmodifiable bases and items with no eligible explicit.</summary>
    public Item? Annul(Item item, Random rng)
    {
        if (item.Rarity is not (ItemRarity.Magic or ItemRarity.Rare) || !TryModifiable(item, out _)) return null;
        var rules = new MetacraftRules(item, catalog.Mods);
        var candidates = Enumerable.Range(0, item.Mods.Count).Where(i => item.Mods[i].Section == ModSection.Explicit && !rules.Protected(i)
            && (catalog.Mods.ByHash(item.Mods[i].Hash) is not { } mod || rules.Allows(mod))).ToArray();
        if (candidates.Length == 0) return null;
        int removed = candidates[rng.Next(candidates.Length)];
        return item with { Mods = [.. item.Mods.Where((_, i) => i != removed)] };
    }

    /// <summary>Blessed Orb: every implicit mod keeps its place and tier and rerolls only its values within the tier, as
    /// <see cref="Reforge"/> does for explicits; explicit, enchant and other mods stay. Null when the item has no implicit
    /// mod (nothing is spent).</summary>
    public Item? Bless(Item item, Random rng) => !item.Mods.Any(m => m.Section == ModSection.Implicit) ? null
        : item with { Mods = [.. item.Mods.Select(m => m.Section == ModSection.Implicit && catalog.Mods.ByHash(m.Hash) is { } mod
            ? mod.Roll(ModSection.Implicit, rng) : m)] };

    /// <summary>One more explicit affix, drawn by weight from the base's pool at its item level: no family already on the
    /// item and at most <paramref name="maxEach"/> prefixes / suffixes. The old mods stay; null when nothing fits.</summary>
    public Item? AddAffix(Item item, int maxEach, Random rng, bool respectMetamods = true) =>
        AddAffixCore(item, maxEach, rng, respectMetamods, requiredTag: null);

    /// <summary>A Harvest augment's added modifier: one with this Tags.dat tag, as <see cref="AddAffix"/> otherwise.</summary>
    public Item? AddTaggedAffix(Item item, int maxEach, Random rng, string tag, bool respectMetamods = true) =>
        AddAffixCore(item, maxEach, rng, respectMetamods, tag);

    /// <summary>The affixes <see cref="AddAffix"/> may add: the base's pool at the item's level, without families
    /// already on the item, without a side that already has <paramref name="maxEach"/>, and filtered by the item's
    /// metamods and an optional Tags.dat tag.</summary>
    public IReadOnlyList<(ModCatalog.Mod Mod, int Weight)> AffixesToAdd(Item item, int maxEach, bool respectMetamods = true,
        string? requiredTag = null)
    {
        if (catalog.ByHash(item.TypeHash) is not { } t) return [];
        var have = item.Mods.Where(m => m.Section == ModSection.Explicit).Select(m => catalog.Mods.ByHash(m.Hash)).OfType<ModCatalog.Mod>().ToList();
        var rules = new MetacraftRules(item, catalog.Mods);
        return [.. Pool(t, item.ItemLevel).Where(p => (!respectMetamods || rules.Allows(p.Mod)) && have.Count(h => h.GenerationType == p.Mod.GenerationType) < maxEach &&
            !have.Any(h => h.Families.Intersect(p.Mod.Families).Any()) && (requiredTag is null || p.Mod.Tags.Contains(requiredTag)))];
    }

    Item? AddAffixCore(Item item, int maxEach, Random rng, bool respectMetamods, string? requiredTag)
    {
        var allowed = AffixesToAdd(item, maxEach, respectMetamods, requiredTag);
        if (allowed.Count == 0) return null;
        var added = Pick(allowed, rng);
        // Prefixes before suffixes, as Roll orders them.
        var mods = item.Mods.ToList();
        int at = added.GenerationType == Prefix
            ? mods.FindLastIndex(m => m.Section == ModSection.Explicit && catalog.Mods.ByHash(m.Hash)?.GenerationType == Prefix) + 1
            : mods.Count;
        if (at == 0) at = mods.FindIndex(m => m.Section == ModSection.Explicit) is int e and >= 0 ? e : mods.Count;
        // Never before the fractured mods, which stay the first explicits.
        if (item.FracturedIndexes() is { Count: > 0 } f) at = Math.Max(at, f.Max() + 1);
        mods.Insert(at, added.Roll(ModSection.Explicit, rng));
        return item with { Mods = mods };
    }

    /// <summary>Two Words.dat keys (prefix from list 1, suffix from list 2) for the base's tags; empty without Words.dat.</summary>
    public IReadOnlyList<uint> RareName(ItemCatalog.BaseType t, Random rng)
    {
        uint? Draw(int list)
        {
            var words = _words.Value.Where(w => w.List == list)
                .Select(w => (w, w.SpawnWeights.FirstOrDefault(s => t.Tags.Contains(s.Tag)).Weight)).Where(p => p.Weight > 0).ToList();
            return words.Count == 0 ? null : Pick(words, rng).Key;
        }
        return Draw(1) is uint a && Draw(2) is uint b ? [a, b] : [];
    }

    /// <summary>Vaal Orb on a rare item: <paramref name="count"/> random explicit affixes leave and as many new ones come
    /// (<see cref="AddAffix"/>, at most three of each kind); the others stay.</summary>
    public Item ReplaceAffixes(Item item, int count, Random rng)
    {
        var fractured = item.FracturedIndexes();
        var explicits = item.Mods.Select((m, i) => (m, i)).Where(p => p.m.Section == ModSection.Explicit && !fractured.Contains(p.i)).Select(p => p.i)
            .OrderBy(_ => rng.Next()).Take(count).ToHashSet();
        var result = item with { Mods = [.. item.Mods.Where((_, i) => !explicits.Contains(i))] };
        for (int n = 0; n < explicits.Count && AddAffix(result, 3, rng, respectMetamods: false) is { } added; n++) result = added;
        return result;
    }

    /// <summary>A rare name for this item's base; empty when the base is unknown.</summary>
    public IReadOnlyList<uint> RareName(Item item, Random rng) => catalog.ByHash(item.TypeHash) is { } t ? RareName(t, rng) : [];

    /// <summary>"Dire Bane" from the keys (for logs and /mods).</summary>
    public string NameOf(IReadOnlyList<uint> keys) => string.Concat(keys.Select(k => _words.Value.FirstOrDefault(w => w.Key == k)?.Text ?? $"[{k:x8}]"));

    static T Pick<T>(IReadOnlyList<(T Item, int Weight)> choices, Random rng)
    {
        long pick = rng.NextInt64(choices.Sum(c => (long)c.Weight));
        foreach (var (item, weight) in choices)
        {
            if (pick < weight) return item;
            pick -= weight;
        }
        return choices[^1].Item;
    }

    static List<Word> LoadWords(string folder)
    {
        string path = Path.Combine(folder, "tables", "Words.json"), tagsPath = Path.Combine(folder, "tables", "Tags.json");
        if (!File.Exists(path) || !File.Exists(tagsPath)) return [];
        List<string> tags;
        using (var t = JsonDocument.Parse(File.ReadAllBytes(tagsPath)))
            tags = [.. t.RootElement.EnumerateArray().Select(r => r.GetProperty("Id").GetString() ?? "")];
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        return [.. d.RootElement.EnumerateArray().Select(r => new Word(r.GetProperty("HASH32").GetUInt32(), r.GetProperty("Text").GetString() ?? "",
            r.GetProperty("Wordlist").GetInt32(),
            [.. r.GetProperty("SpawnWeight_Tags").EnumerateArray().Zip(r.GetProperty("SpawnWeight_Values").EnumerateArray(),
                (k, v) => (k.GetInt32() < tags.Count ? tags[k.GetInt32()] : "", v.GetInt32()))]))];
    }
}
