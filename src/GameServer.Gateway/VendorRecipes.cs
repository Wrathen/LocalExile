using System.Text.Json;
using GameServer.Core.Protocol.Instance;

/// <summary>One sale quote: ingredients are consumed once, outputs are rolled once and retained until acceptance.</summary>
sealed partial class VendorRecipes
{
    readonly ItemCatalog _catalog;
    readonly ItemRolls _rolls;
    readonly Dictionary<ushort, int[]> _sellTypes = [];
    readonly Dictionary<ushort, string> _sellFamilies = [];
    readonly Dictionary<uint, int> _upgradeCosts = [];
    readonly Dictionary<uint, uint> _upgrades = [];
    readonly List<ItemCatalog.BaseType> _types;

    public VendorRecipes(ItemCatalog catalog, string folder = "data")
    {
        _catalog = catalog;
        _rolls = new(catalog, folder);
        _types = Enumerable.Range(0, 20000).Select(catalog.Row).TakeWhile(t => t is not null).Select(t => t!).ToList();
        var modTypes = Read("ModType");
        if (modTypes.Count == 0) throw new InvalidOperationException("Vendor pricing data is missing: run scripts/dev/extract-data.ps1 and restart Gateway.");
        foreach (var mod in Read("Mods"))
            if (mod.GetProperty("ModTypeKey") is { ValueKind: JsonValueKind.Number } key && key.GetInt32() < modTypes.Count)
            {
                ushort hash = (ushort)mod.GetProperty("HASH16").GetInt32();
                _sellTypes.TryAdd(hash, modTypes[key.GetInt32()].GetProperty("ModSellPriceTypesKeys").EnumerateArray().Select(x => x.GetInt32()).ToArray());
                _sellFamilies.TryAdd(hash, modTypes[key.GetInt32()].GetProperty("Name").GetString()!);
            }
        var essences = Read("Essences");
        foreach (var essence in essences)
            if (essences.FirstOrDefault(e => e.GetProperty("EssenceTypeKey").GetInt32() == essence.GetProperty("EssenceTypeKey").GetInt32()
                && e.GetProperty("Level").GetInt32() == essence.GetProperty("Level").GetInt32() + 1) is { ValueKind: JsonValueKind.Object } next
                && catalog.Row(essence.GetProperty("BaseItemTypesKey").GetInt32()) is { } from
                && catalog.Row(next.GetProperty("BaseItemTypesKey").GetInt32()) is { } to)
                _upgrades[from.Hash] = to.Hash;
        // The table contains legacy map series too; only expose upgrades belonging to that exact base row.
        foreach (var map in Read("Maps"))
            if (map.GetProperty("MapUpgrade_BaseItemTypesKey") is { ValueKind: JsonValueKind.Number } next
                && catalog.Row(map.GetProperty("BaseItemTypesKey").GetInt32()) is { Supported: true } from
                && catalog.Row(next.GetInt32()) is { Supported: true } to)
                _upgrades.TryAdd(from.Hash, to.Hash);
        string[] oils = ["Clear", "Sepia", "Amber", "Verdant", "Teal", "Azure", "Indigo", "Violet", "Crimson", "Black", "Opalescent", "Silver", "Golden"];
        foreach (var (a, b) in oils.Zip(oils.Skip(1)))
            if (Exact(a + " Oil") is { } from && Exact(b + " Oil") is { } to) _upgrades.TryAdd(from.Hash, to.Hash);

        for (int i = 1; i < 10; i++)
            if (Exact($"Metadata/Items/Currency/KalguuranRune{i}") is { } from && Exact($"Metadata/Items/Currency/KalguuranRune{i + 1}") is { } to)
            { _upgrades[from.Hash] = to.Hash; _upgradeCosts[from.Hash] = 5; }

        List<JsonElement> Read(string name)
        {
            string path = Path.Combine(folder, "tables", name + ".json");
            if (!File.Exists(path)) return [];
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            return doc.RootElement.EnumerateArray().Select(r => r.Clone()).ToList();
        }
    }

    ItemCatalog.BaseType? Exact(string idOrName) => _types.FirstOrDefault(t => t.Id == idOrName || t.Name == idOrName);
    ItemCatalog.BaseType Type(Item item) => _catalog.ByHash(item.TypeHash)
        ?? throw new InvalidOperationException($"Unknown vendor item 0x{item.TypeHash:x8}");
    public sealed record Quote(IReadOnlyList<Item> Rewards, IReadOnlyList<string> Recipes);
    sealed class Ingredient(InventoryItem placement, ItemCatalog.BaseType type)
    {
        public uint Id => placement.Id;
        public Item Item => placement.Item;
        public ItemCatalog.BaseType Type => type;
        public int Left = placement.Item.StackSize;
    }

    public Quote Evaluate(IEnumerable<InventoryItem> offered, Random? random = null, double? basicSaleMultiplier = null)
    {
        if (basicSaleMultiplier is { } multiplier) return BasicSale(offered, multiplier);
        var rng = random ?? Random.Shared;
        var pool = offered.OrderBy(x => x.Id).Select(i => new Ingredient(i, Type(i.Item))).ToList();
        if (pool.Select(i => i.Id).Distinct().Count() != pool.Count) throw new ArgumentException("Duplicate vendor ingredient id");
        if (pool.Any(i => i.Item.Mods.Any(m => (_sellTypes.GetValueOrDefault(m.Hash) ?? []).Any(t => t >= 9 && t != 34))))
            throw new NotSupportedException("This item's special vendor recipe is not implemented yet. Nothing has been sold.");
        var rewards = new List<Item>();
        var recipes = new List<string>();
        var currency = new Dictionary<uint, int>();
        var usedCurrency = new HashSet<uint>();
        void Money(string name, int count = 1)
        {
            var type = Exact(name) ?? throw new InvalidOperationException($"Vendor reward missing: {name}");
            currency[type.Hash] = checked(currency.GetValueOrDefault(type.Hash) + count);
        }
        void Use(string recipe, params Ingredient[] ingredients)
        {
            foreach (var item in ingredients) item.Left--;
            recipes.Add(recipe);
        }
        Ingredient? Find(Func<Ingredient, bool> predicate) => pool.FirstOrDefault(i => i.Left > 0 && predicate(i));
        bool Spend(string name, int count)
        {
            var stacks = pool.Where(i => i.Left > 0 && i.Type.Name == name).ToList();
            if (stacks.Sum(i => i.Left) < count) return false;
            foreach (var stack in stacks)
            {
                int n = Math.Min(count, stack.Left);
                if (n > 0) usedCurrency.Add(stack.Item.TypeHash);
                stack.Left -= n; count -= n;
            }
            return true;
        }
        bool Has(string name, int count = 1) => pool.Where(i => i.Type.Name == name).Sum(i => i.Left) >= count;
        Item Fresh(Ingredient item, ItemCatalog.BaseType? type = null, ItemRarity? rarity = null) =>
            _rolls.Roll(ItemCatalog.Create(type ?? item.Type) with { ItemLevel = item.Item.ItemLevel }, rarity ?? item.Item.Rarity, rng);

        // Gem transformations must run before currency downgrades or quality recipes.
        foreach (var gem in pool.Where(i => i.Type.Components.Contains("SkillGem")))
        {
            if (gem.Left == 0 || gem.Item.Corrupted || gem.Item.Mirrored || _catalog.Gem(gem.Type) is not { } info) continue;
            if (info.Variant(gem.Item.GemVariant).Effect2 >= 0) continue;
            int level = info.Level(gem.Item.GemExperience);
            int target = level;
            bool quality = false;
            if (level > 1 && Has("Orb of Scouring")) { Spend("Orb of Scouring", 1); target--; }
            else if (level > 1 && Has("Orb of Regret")) { Spend("Orb of Regret", 1); target = 1; }
            else if (level == 20 && info.IsSupport && !info.IsVaalOrAwakened && Has("Gemcutter's Prism"))
            { Spend("Gemcutter's Prism", 1); target = 1; quality = true; }
            else continue;
            Use(quality ? "support gem quality" : "gem level", gem);
            rewards.Add(gem.Item with { GemExperience = info.ExperienceFor(target), Quality = quality ? (byte)20 : gem.Item.Quality,
                GemFlags = 0, Price = [] });
        }

        foreach (var gem in pool.Where(i => i.Left > 0 && !i.Item.Corrupted && !i.Item.Mirrored
                     && i.Type.Name is "Blink Arrow" or "Mirror Arrow").ToList())
        {
            if (!Has("Orb of Alteration")) continue;
            var target = Exact(gem.Type.Name == "Blink Arrow" ? "Mirror Arrow" : "Blink Arrow")!;
            var from = _catalog.Gem(gem.Type)!;
            var to = _catalog.Gem(target)!;
            string oldName = from.Variant(gem.Item.GemVariant).Name;
            string newName = oldName.Replace(gem.Type.Name, target.Name);
            int variant = to.Variants.ToList().FindIndex(v => v.Name == newName);
            if (variant < 0) continue;
            rewards.Add(ItemCatalog.Create(target) with { Quality = gem.Item.Quality, GemExperience = gem.Item.GemExperience, GemVariant = variant });
            Spend("Orb of Alteration", 1); Use("arrow gem exchange", gem);
        }
        while (Has("Orb of Fusing") && Has("Chromatic Orb"))
        { Spend("Orb of Fusing", 1); Spend("Chromatic Orb", 1); Money("Jeweller's Orb"); recipes.Add("socket currency downgrade"); }

        CraftEquipment(pool, rewards, recipes, rng, Has, Spend);

        // Seven Vaal gems and one Sacrifice fragment (never Mortal fragments).
        while (true)
        {
            var gems = pool.Where(i => i.Left > 0 && _catalog.Gem(i.Type) is { } g && g.Variant(i.Item.GemVariant).Effect2 >= 0).Take(7).ToArray();
            var fragment = Find(i => i.Type.Id.Contains("VaalFragment") && !i.Type.Id.Contains("VaalFragment2"));
            if (gems.Length != 7 || fragment is null) break;
            Use("Vaal Orb", [.. gems, fragment]); Money("Vaal Orb");
        }

        // Six-link/socket salvage is a single-item recipe and must not disappear into a rare set.
        foreach (var item in pool.Where(i => i.Left > 0 && i.Item.Sockets.Count == 6
                     && i.Item.Sockets.All(s => s.Colour is >= SocketColour.Red and <= SocketColour.White)))
        {
            Money(item.Item.Links?.Contains((byte)6) == true ? "Orb of Fusing" : "Jeweller's Orb",
                (item.Item.Links?.Contains((byte)6) == true ? 20 : 7) * item.Left);
            item.Left = 0; recipes.Add("six-link/socket salvage");
        }
        RareSets(pool, Money, Use);

        // Reroll three identical unique variants; explicit identities, not just the shared base type, must match.
        foreach (var group in pool.Where(i => i.Left > 0 && i.Item.Rarity == ItemRarity.Unique && !i.Item.Corrupted && !i.Item.Mirrored && Empty(i.Item))
                     .GroupBy(i => (i.Item.TypeHash, Mods: string.Join(',', i.Item.Mods.Where(m => m.Section == ModSection.Explicit).Select(m => m.Hash).Order()),
                         Name: string.Join(',', i.Item.NameWords))))
        {
            var list = group.ToList();
            while (list.Count >= 3)
            {
                var used = list.Take(3).ToArray();
                var source = used.OrderBy(i => i.Item.ItemLevel).First();
                var mods = source.Item.Mods.Where(m => m.Section == ModSection.Explicit)
                    .Select(m => _catalog.Mods.ByHash(m.Hash)?.Roll(m.Section, rng) ?? m).ToList();
                mods.InsertRange(0, ItemCatalog.Create(source.Type).Mods);
                rewards.Add(source.Item with { Mods = mods, Quality = 0, Sockets = [], Links = null, Identified = false, Price = [] });
                Use("three identical uniques", used); list.RemoveRange(0, 3);
            }
        }

        // Identical rare names: words, not just identical base types.
        foreach (var group in pool.Where(i => i.Left > 0 && i.Item.Rarity == ItemRarity.Rare && i.Item.Identified && i.Item.NameWords.Count == 2)
                     .GroupBy(i => string.Join(',', i.Item.NameWords)))
        {
            var available = group.OrderByDescending(i => i.Item.Quality).ToList();
            while (available.Count >= 2)
            {
                int count = available.Count >= 3 ? 3 : 2;
                var used = available.Take(count).ToArray();
                bool quality = used.All(i => i.Item.Quality >= 20);
                Money(count == 3 ? quality ? "Regal Orb" : "Orb of Alchemy" : quality ? "Orb of Alchemy" : "Orb of Chance");
                Use("matching rare names", used); available.RemoveRange(0, count);
            }
        }

        // Same base type in different rarities.
        foreach (var group in pool.Where(i => i.Left > 0 && i.Type.Components.Contains("Mods")).GroupBy(i => i.Item.TypeHash))
            while (true)
            {
                var set = new[] { ItemRarity.Normal, ItemRarity.Magic, ItemRarity.Rare }
                    .Select(r => group.Where(i => i.Left > 0 && i.Item.Rarity == r).OrderByDescending(i => i.Item.Quality).FirstOrDefault()).ToArray();
                if (set.Any(i => i is null)) break;
                if (group.FirstOrDefault(i => i.Left > 0 && i.Item.Rarity == ItemRarity.Unique) is { } unique)
                { Use("four rarities", [.. set.Select(i => i!), unique]); Money("Orb of Chance", 5); }
                else
                {
                    var used = set.Select(i => i!).ToArray();
                    Money(used.All(i => i.Item.Quality >= 20) ? "Orb of Alchemy" : "Orb of Augmentation",
                        used.Where(i => i.Item.Rarity != ItemRarity.Normal).All(i => !i.Item.Identified) ? 2 : 1);
                    Use("three rarities", used);
                }
            }

        // Five matching magic/rare bases; neither socketed contents nor special components may disappear.
        foreach (var group in pool.Where(i => i.Left > 0 && i.Type.Components.Contains("Mods") && Empty(i.Item)
                         && i.Item.Rarity is ItemRarity.Magic or ItemRarity.Rare).GroupBy(i => (i.Item.TypeHash, i.Item.Rarity)))
        {
            var list = group.ToList();
            while (list.Count >= 5)
            {
                var used = list.Take(5).ToArray();
                var output = Fresh(used.OrderBy(i => i.Item.ItemLevel).First()) with { Identified = false };
                if (used[0].Type.Name == "Breach Ring" || used[0].Type.Id.Contains("Talisman")) output = output with { Corrupted = true };
                rewards.Add(output); Use("five matching bases", used); list.RemoveRange(0, 5);
            }
        }

        // Three matching essences or maps. Currency remainders are returned, not silently sold.
        foreach (var group in pool.Where(i => i.Left > 0 && _upgrades.ContainsKey(i.Item.TypeHash)).GroupBy(i => i.Item.TypeHash))
        {
            var entries = group.ToList();
            int cost = _upgradeCosts.GetValueOrDefault(group.Key, 3);
            int count = entries.Sum(i => i.Left) / cost;
            if (count == 0) continue;
            int consumed = count * cost;
            foreach (var i in entries) { int take = Math.Min(consumed, i.Left); i.Left -= take; consumed -= take; }
            var target = _catalog.ByHash(_upgrades[group.Key])!;
            if (target.Stackable) currency[target.Hash] = currency.GetValueOrDefault(target.Hash) + count;
            else for (int n = 0; n < count; n++) rewards.Add(ItemCatalog.Create(target) with { ItemLevel = entries.Min(i => i.Item.ItemLevel) });
            recipes.Add($"{cost}-to-one upgrade");
            if (entries[0].Type.Stackable)
                foreach (var i in entries.Where(i => i.Left > 0)) { rewards.Add(i.Item with { StackSize = (ushort)i.Left }); i.Left = 0; }
        }

        FlaskRecipes(pool, rewards, recipes, rng, Has, Spend);

        // 40 quality must be covered by disjoint item subsets, not floor(total / 40).
        foreach (var group in pool.Where(i => i.Left > 0 && i.Item.Quality > 0 && QualityCurrency(i) is not null).GroupBy(QualityCurrency))
        {
            var singles = group.Where(i => i.Item.Rarity == ItemRarity.Normal && i.Item.Quality >= 20 && !LinkedRgb(i.Item)).ToArray();
            foreach (var single in singles) { Money(group.Key!); Use("20 quality", single); }
            while (QualitySubset(group.Where(i => i.Left > 0).ToList()) is { Count: > 0 } used)
            { Money(group.Key!); Use("40 total quality", used.ToArray()); }
        }

        foreach (var remainder in pool.Where(i => i.Left > 0 && usedCurrency.Contains(i.Item.TypeHash)))
        { rewards.Add(remainder.Item with { StackSize = (ushort)remainder.Left }); remainder.Left = 0; }

        foreach (var i in pool.Where(i => i.Left > 0))
        {
            int amount = i.Left;
            i.Left = 0;
            if (i.Item.Sockets.Count == 6 && i.Item.Links?.Contains((byte)6) == true)
                Money("Orb of Fusing", 20 * amount);
            else if (i.Item.Sockets.Count == 6 && i.Item.Sockets.All(s => s.Colour is >= SocketColour.Red and <= SocketColour.White))
                Money("Jeweller's Orb", 7 * amount);
            else if (LinkedRgb(i.Item)) Money("Chromatic Orb", amount);
            else if (i.Type.Name == "Mirror of Kalandra")
            { Money("Exalted Orb", 2 * amount); Money("Regal Orb", 5 * amount); Money("Divine Orb", amount); }
            else if (i.Type.Name is "Portal Scroll" or "Armourer's Scrap" or "Blacksmith's Whetstone" or "Orb of Transmutation" or "Orb of Alteration")
                Money("Scroll of Wisdom", amount * (i.Type.Name == "Portal Scroll" ? 1 : i.Type.Name == "Armourer's Scrap" ? 2 : 4));
            else if (!i.Item.Identified && i.Item.Rarity != ItemRarity.Normal)
                Money("Transmutation Shard", amount * (i.Item.Rarity == ItemRarity.Magic ? 2 : 5));
            else if (i.Item.Rarity == ItemRarity.Rare && i.Item.Mods.Count(m => m.Section == ModSection.Explicit) is var affixes && affixes is 2 or 6)
                Money(affixes == 2 ? "Orb of Scouring" : "Orb of Augmentation", amount);
            else
            {
                var special = i.Item.Mods.SelectMany(m => _sellTypes.GetValueOrDefault(m.Hash) ?? []).Where(t => t >= 9).ToHashSet();
                if (special.Contains(34)) { Money("Scroll Fragment", amount); continue; }
                if (special.Count > 0)
                    throw new NotSupportedException("This item's special vendor recipe is not implemented yet. Nothing has been sold.");
                var price = ModPrice(i.Item);
                if (price.Alteration == 0 && price.Alchemy == 0) Money("Scroll Fragment", amount);
                else { if (price.Alteration > 0) Money("Alteration Shard", price.Alteration * amount); if (price.Alchemy > 0) Money("Alchemy Shard", price.Alchemy * amount); }
            }
        }

        // Normalize the quote's shards before sending the offer (inventory stacking also handles existing remainders).
        foreach (var hash in currency.Keys.ToList())
            if (_catalog.ByHash(hash) is { BecomesWhenFull: true } type && _catalog.Row(type.FullStackRow) is { } full)
            { int count = currency[hash] / type.MaxStack; currency[hash] %= type.MaxStack; currency[full.Hash] = currency.GetValueOrDefault(full.Hash) + count; }
        foreach (var (hash, count) in currency.OrderBy(p => p.Key))
            for (int left = count; left > 0;)
            {
                var type = _catalog.ByHash(hash)!;
                int n = Math.Min(left, type.MaxStack); left -= n;
                rewards.Add(ItemCatalog.Create(type) with { StackSize = (ushort)n });
            }
        return new(rewards, recipes);
    }

    // A league with a basic-sale multiplier deliberately excludes the regular deterministic recipes. Basic selling remains useful, with the
    // LocalExile configurable reward multiplier applied after combining like shards, before full-stack normalization.
    Quote BasicSale(IEnumerable<InventoryItem> offered, double multiplier)
    {
        var money = new Dictionary<string, long>();
        void Add(string name, long count) => money[name] = checked(money.GetValueOrDefault(name) + count);
        var placements = offered.ToArray();
        if (placements.Select(i => i.Id).Distinct().Count() != placements.Length)
            throw new ArgumentException("Duplicate vendor ingredient id");
        foreach (var placement in placements)
        {
            var item = placement.Item;
            var type = Type(item);
            int count = item.StackSize;
            if (type.Class == "StackableCurrency")
            {
                int wisdoms = type.Name switch
                {
                    "Portal Scroll" => 1, "Armourer's Scrap" => 2,
                    "Blacksmith's Whetstone" or "Orb of Transmutation" or "Orb of Alteration" => 4,
                    _ => throw new NotSupportedException("This currency's basic sale is not implemented. Nothing has been sold."),
                };
                Add("Scroll of Wisdom", (long)wisdoms * count);
            }
            else if (!item.Identified && item.Rarity != ItemRarity.Normal)
                Add("Transmutation Shard", (item.Rarity == ItemRarity.Magic ? 2L : 5L) * count);
            else
            {
                var modPrice = ModPrice(item);
                if (modPrice.Alteration == 0 && modPrice.Alchemy == 0) Add("Scroll Fragment", count);
                else
                {
                    Add("Alteration Shard", (long)modPrice.Alteration * count);
                    Add("Alchemy Shard", (long)modPrice.Alchemy * count);
                }
            }
        }
        multiplier = double.IsFinite(multiplier) ? Math.Clamp(multiplier, 0, 1) : 0;
        var totals = new Dictionary<uint, long>();
        foreach (var (name, count) in money)
        {
            var type = Exact(name) ?? throw new InvalidOperationException($"Vendor reward missing: {name}");
            totals[type.Hash] = checked(totals.GetValueOrDefault(type.Hash) + (long)Math.Floor(count * multiplier));
        }
        foreach (var hash in totals.Keys.ToArray())
            if (_catalog.ByHash(hash) is { BecomesWhenFull: true } type && _catalog.Row(type.FullStackRow) is { } full)
            {
                totals[full.Hash] = checked(totals.GetValueOrDefault(full.Hash) + totals[hash] / type.MaxStack);
                totals[hash] %= type.MaxStack;
            }
        var rewards = new List<Item>();
        foreach (var (hash, count) in totals.OrderBy(x => x.Key))
            for (long left = count; left > 0;)
            {
                var type = _catalog.ByHash(hash)!;
                int amount = (int)Math.Min(left, type.MaxStack);
                rewards.Add(ItemCatalog.Create(type) with { StackSize = (ushort)amount });
                left -= amount;
            }
        return new(rewards, placements.Length == 0 ? [] : ["basic sale"]);
    }

    static bool Empty(Item i) => i.Sockets.All(s => s.Gem is null);
    static bool Weapon(ItemCatalog.BaseType t) => t.Components.Contains("Weapon") && t.Class != "FishingRod";
    static bool TwoHanded(ItemCatalog.BaseType t) => t.Class is "Bow" or "Staff" or "Warstaff" || t.Class.StartsWith("Two Hand");
    static bool Armour(ItemCatalog.BaseType t) => t.Class is "Helmet" or "Body Armour" or "Gloves" or "Boots" or "Shield";
    static string? QualityCurrency(Ingredient i) => i.Type.Components.Contains("SkillGem") ? "Gemcutter's Prism"
        : Weapon(i.Type) ? "Blacksmith's Whetstone" : Armour(i.Type) ? "Armourer's Scrap"
        : i.Type.Class.Contains("Flask") ? "Glassblower's Bauble" : null;

    static List<Ingredient>? QualitySubset(List<Ingredient> entries)
    {
        var sums = new Dictionary<int, List<Ingredient>> { [0] = [] };
        foreach (var i in entries)
            foreach (var (sum, list) in sums.ToArray())
            {
                if (sum >= 40) continue;
                int next = sum + i.Item.Quality;
                if (!sums.TryGetValue(next, out var before) || before.Count > list.Count + 1) sums[next] = [.. list, i];
            }
        return sums.Where(p => p.Key >= 40).OrderBy(p => p.Key).ThenBy(p => p.Value.Count).Select(p => p.Value).FirstOrDefault();
    }

    public static bool LinkedRgb(Item item)
    {
        int offset = 0;
        foreach (int size in item.Links ?? [])
        {
            var colours = item.Sockets.Skip(offset).Take(size).Select(s => s.Colour).ToHashSet(); offset += size;
            if (colours.Contains(SocketColour.Red) && colours.Contains(SocketColour.Green) && colours.Contains(SocketColour.Blue)) return true;
        }
        return false;
    }

    void RareSets(List<Ingredient> pool, Action<string, int> money, Action<string, Ingredient[]> use)
    {
        foreach (int mode in new[] { 3, 1, 2, 0 })
            foreach (int floor in new[] { 75, 60, 1 })
                while (true)
                {
                    bool Quality(Ingredient i) => i.Type.Class is "Ring" or "Amulet" or "Belt" || i.Item.Quality >= 20;
                    var eligible = pool.Where(i => i.Left > 0 && i.Item.Rarity == ItemRarity.Rare && Empty(i.Item) && i.Item.ItemLevel >= floor
                        && ((mode & 1) == 0 || !i.Item.Identified) && ((mode & 2) == 0 || Quality(i))).OrderBy(i => i.Item.ItemLevel).ToList();
                    var set = new List<Ingredient>();
                    foreach (string slot in new[] { "Helmet", "Body Armour", "Gloves", "Boots", "Belt", "Amulet", "Ring", "Ring" })
                        if (eligible.FirstOrDefault(i => i.Type.Class == slot && !set.Contains(i)) is { } item) set.Add(item);
                    if (set.Count != 8) break;
                    if (eligible.FirstOrDefault(i => Weapon(i.Type) && TwoHanded(i.Type)) is { } two) set.Add(two);
                    else
                    {
                        var weapons = eligible.Where(i => Weapon(i.Type) && !TwoHanded(i.Type)).ToList();
                        if (weapons.Count == 0) break;
                        set.Add(weapons[0]);
                        var offhand = eligible.FirstOrDefault(i => i.Type.Class == "Shield") ?? weapons.Skip(1).FirstOrDefault();
                        if (offhand is null) break;
                        set.Add(offhand);
                    }
                    int level = set.Min(i => i.Item.ItemLevel);
                    money(level >= 75 ? "Regal Orb" : level >= 60 ? "Chaos Orb" : "Orb of Chance",
                        1 + (set.All(i => !i.Item.Identified) ? 1 : 0) + (set.All(Quality) ? 1 : 0));
                    use("full rare set", set.ToArray());
                }
    }

    public (int Alteration, int Alchemy) ModPrice(Item item)
    {
        if (item.Rarity == ItemRarity.Normal || !item.Identified) return (0, 0);
        int alteration = 0, alchemy = 0;
        var fractured = item.FracturedIndexes();
        foreach (var (entry, index) in item.Mods.Select((m, i) => (m, i)).Where(p => p.m.Section == ModSection.Explicit))
        {
            if (_catalog.Mods.ByHash(entry.Hash) is not { } mod) continue;
            bool max = Enumerable.Range(0, mod.Stats.Count).Any(i => { var range = mod.RangeAt(i); return range.Min < range.Max; })
                && Enumerable.Range(0, mod.Stats.Count).All(i => i < entry.Values.Count && entry.Values[i] >= mod.RangeAt(i).Max);
            int multiplier = fractured.Contains(index) ? 4 : 1;
            string family = _sellFamilies.GetValueOrDefault(entry.Hash, "");
            if (family is "Strength" or "Dexterity" or "Intelligence") { alteration += 2 * multiplier; continue; }
            if (family == "IncreasedLife") { alteration += (mod.Id == "IncreasedLife1" ? 2 : 4) * multiplier; continue; }
            if (family == "AllAttributes") { alteration += (max ? 4 : 3) * multiplier; continue; }
            if (family == "AllResistances") { alteration += (max ? 6 : 3) * multiplier; continue; }
            if (family == "LocalEnergyShield" || family == "LocalIncreasedPhysicalDamagePercentAndAccuracyRating")
            { alteration += 3 * multiplier; continue; }
            if (family == "MovementVelocity") { alteration += 4 * multiplier; continue; }
            foreach (int price in _sellTypes.GetValueOrDefault(entry.Hash) ?? [])
                switch (price)
                {
                    case 0: alteration += (max ? 2 : 1) * multiplier; break;
                    case 1: alteration += 2 * multiplier; break;
                    case 2: alteration += (max ? 8 : 4) * multiplier; break;
                    case 3: alteration += 8 * multiplier; break;
                    case 4: alchemy += (max ? 3 : 1) * multiplier; break;
                    case 5: alchemy += (max ? 7 : 2) * multiplier; break;
                    case 6: alchemy += 4 * multiplier; break;
                    case 7: alchemy += 4 * multiplier; break;
                    case 8: alchemy += 8 * multiplier; break;
                }
        }
        bool doubled = item.Mods.Any(m => _catalog.Mods.ByHash(m.Hash)?.Stats.Select((s, n) =>
            s.Id == "local_item_sell_price_doubled" && n < m.Values.Count && m.Values[n] != 0).Any(x => x) == true);
        return doubled ? (alteration * 2, alchemy * 2) : (alteration, alchemy);
    }
}
