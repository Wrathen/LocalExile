using System.Text.Json;
using GameServer.Core.Protocol.Instance;

/// <summary>The ordinary bench's live client recipes, costs, class filters and guaranteed modifiers.</summary>
sealed class BenchCrafting(ItemCatalog catalog, ItemRolls rolls, string folder = "data")
{
    public sealed record Recipe(int Row, string Name, ModCatalog.Mod? Mod, ModCatalog.Mod? Enchant,
        int Action, int Sockets, int Links, string Colours, IReadOnlySet<int> Classes,
        IReadOnlyList<(uint Currency, int Amount)> Costs, IReadOnlyList<int> Unlocks);
    public sealed record Result(Item Item, IReadOnlyList<(uint Currency, int Amount)> Costs);
    readonly Lazy<IReadOnlyList<Recipe>> _all = new(() => Load(catalog, folder));
    public IReadOnlyList<Recipe> All => _all.Value;
    public IEnumerable<int> UnlockFlags => All.SelectMany(r => r.Unlocks).Where(i => i > 0).Distinct();
    public Recipe? Find(int row) => All.FirstOrDefault(r => r.Row == row);
    public bool IsCrafted(ItemMod mod) => mod.Section == ModSection.Explicit && catalog.Mods.ByHash(mod.Hash)?.Domain == 9;

    public Result? Apply(int row, Item item, Random rng)
    {
        if (Find(row) is not { } recipe || catalog.ByHash(item.TypeHash) is not { } type ||
            !recipe.Classes.Contains(type.ClassRow) || item.Mirrored || !item.Components.Contains("Mods")) return null;
        bool socketCraft = recipe.Sockets > 0 || recipe.Links > 0 || recipe.Colours.Length > 0;
        if (item.Corrupted && !socketCraft) return null;
        if (!item.Identified && !socketCraft) return null;
        var costs = recipe.Costs.ToList();
        Item? result = null;
        if (socketCraft)
        {
            int capacity = CurrencyEffects.SocketCapacity(item, type);
            var requirements = catalog.Requirements(type);
            if (recipe.Sockets > 0)
            {
                int count = recipe.Sockets;
                if (count > capacity || count == item.Sockets.Count || item.Sockets.Any(s => s.Gem is not null)) return null;
                var sockets = item.Sockets.Take(count).ToList();
                // 3.29: sockets the bench adds are white; the sockets that stay keep their colours.
                while (sockets.Count < count) sockets.Add(new(SocketColour.White));
                // Preserve links between retained sockets; added sockets begin unlinked.
                var links = new List<byte>();
                int left = count;
                foreach (int group in item.Links ?? Enumerable.Repeat((byte)1, item.Sockets.Count).ToArray())
                { if (left == 0) break; int take = Math.Min(group, left); links.Add((byte)take); left -= take; }
                while (left-- > 0) links.Add(1);
                result = item with { Sockets = sockets, Links = links };
            }
            else if (recipe.Links > 0)
            {
                int count = item.Sockets.Count, link = recipe.Links;
                if (count < link || count > capacity) return null;
                var links = new byte[] { (byte)link }.Concat(Enumerable.Repeat((byte)1, count - link)).ToArray();
                if ((item.Links ?? Enumerable.Repeat((byte)1, count).ToArray()).SequenceEqual(links)) return null;
                result = item with { Links = links };
            }
            else
            {
                // 3.29 replaced the single-colour crafts with "At Least N Non-White Sockets" ("NN"/"NNN"/"NNNN" in
                // CraftingBenchOptions SocketColours): the colours are rerolled and the result keeps at least N coloured.
                int need = recipe.Colours.Length;
                if (need == 0 || item.Sockets.Count < need || capacity == 0) return null;
                var sockets = item.Sockets.ToArray();
                for (int attempt = 0; attempt < 25 && sockets.Count(s => SocketColours.IsColoured(s.Colour)) < need; attempt++)
                    for (int i = 0; i < sockets.Length; i++)
                        sockets[i] = sockets[i] with
                        {
                            Colour = SocketColours.Roll(requirements, item.ItemLevel, item.Quality, rng, chromatic: true),
                        };
                for (int i = 0; i < sockets.Length && sockets.Count(s => SocketColours.IsColoured(s.Colour)) < need; i++)
                    if (sockets[i].Colour == SocketColour.White)
                        sockets[i] = sockets[i] with { Colour = SocketColours.Colour(requirements, rng) };
                result = item with { Sockets = sockets };
            }
            if (item.Corrupted)
            {
                if (catalog.Find(CurrencyEffects.VaalOrb) is not { } vaal) return null;
                costs.Add((vaal.Hash, recipe.Costs.Sum(c => c.Amount)));
            }
        }
        else if (recipe.Mod is { } mod)
        {
            if (item.Rarity == ItemRarity.Unique || type.ModDomain is not (1 or 2)) return null;
            var mods = item.Mods.ToList();
            var crafted = mods.Where(IsCrafted).ToList();
            int limit = 1;
            foreach (var existing in mods)
                if (catalog.Mods.ByHash(existing.Hash) is { } definition)
                    for (int i = 0; i < definition.Stats.Count && i < existing.Values.Count; i++)
                    {
                        string stat = definition.Stats[i].Id;
                        if (stat == "item_generation_can_have_multiple_crafted_mods" && existing.Values[i] > 0) limit = Math.Max(limit, 3);
                        else if (stat == "local_can_have_additional_crafted_mods") limit += existing.Values[i];
                    }
            if (Enumerable.Range(0, mod.Stats.Count).Any(i =>
                mod.Stats[i].Id == "item_generation_can_have_multiple_crafted_mods" && mod.RangeAt(i).Min > 0))
                limit = Math.Max(limit, 3);
            bool conflict(ItemMod m) => catalog.Mods.ByHash(m.Hash) is { } other &&
                (other.Hash == mod.Hash || other.Families.Intersect(mod.Families).Any());
            var remove = crafted.Where(conflict).ToList();
            if (crafted.Count >= limit && remove.Count == 0)
            { if (crafted.Count != 1) return null; remove = crafted; }
            // The client relabels Craft to "Replace" (CraftingModReplace) and sends the ordinary request; one crafted
            // mod is swapped for the recipe's price alone ("You cannot replace multiple crafted modifiers").
            if (remove.Count > 0)
            {
                if (remove.Count > 1 || remove.Any(m => item.FracturedIndexes().Any(i => ReferenceEquals(item.Mods[i], m)))) return null;
                mods.RemoveAll(remove.Contains);
            }
            var explicits = mods.Where(m => m.Section == ModSection.Explicit).ToList();
            int maxEach = item.Rarity == ItemRarity.Rare ? 3 : 1;
            if (explicits.Any(conflict) || explicits.Count(m => catalog.Mods.ByHash(m.Hash)?.GenerationType == mod.GenerationType) >= maxEach) return null;
            mods.Add(mod.Roll(ModSection.Explicit, rng));
            result = item with { Mods = mods, Rarity = item.Rarity == ItemRarity.Normal ? ItemRarity.Magic : item.Rarity, Identified = true };
        }
        else if (recipe.Enchant is { } enchant)
            result = item with { Mods = [.. item.Mods.Where(m => m.Section != ModSection.Enchant), enchant.Roll(ModSection.Enchant, rng)] };
        else if (recipe.Action is 0 or 1)
        {
            bool Remove(ItemMod m) => recipe.Action == 0 ? IsCrafted(m) : m.Section == ModSection.Enchant;
            if (!item.Mods.Any(Remove) || item.Mods.Where((m, i) => item.FracturedIndexes().Contains(i)).Any(Remove)) return null;
            result = item with { Mods = [.. item.Mods.Where(m => !Remove(m))] };
        }
        else if (recipe.Action == 7)
        {
            if (item.Rarity == ItemRarity.Unique) return null;
            var rarity = (ItemRarity)rng.Next(3);
            result = rolls.Roll(item, rarity, rng) with { Identified = true };
            if (result.FracturedCount > 0 && rarity == ItemRarity.Normal) result = result with { Rarity = ItemRarity.Magic };
        }
        else if (recipe.Action is 8 or 9)
        {
            if (item.Rarity != ItemRarity.Rare) return null;
            int count = recipe.Action == 8 ? 3 : 1;
            result = RerollAffixes(item, type, count, rng);
        }
        if (result is null) return null;
        return new(ItemRolls.ClearForesee(result), costs.GroupBy(c => c.Currency).Select(g => (g.Key, g.Sum(c => c.Amount))).ToList());
    }

    Item? RerollAffixes(Item item, ItemCatalog.BaseType type, int count, Random rng)
    {
        var rules = new MetacraftRules(item, catalog.Mods);
        var candidates = item.Mods.Select((m, i) => (m, i)).Where(p => p.m.Section == ModSection.Explicit && !rules.Protected(p.i))
            .Where(p => catalog.Mods.ByHash(p.m.Hash) is not null).OrderBy(_ => rng.Next()).Select(p => p.i).ToList();
        if (candidates.Count < count) return null;
        var removed = candidates.Take(count).ToHashSet();
        var mods = item.Mods.Where((_, i) => !removed.Contains(i)).ToList();
        var present = mods.Where(m => m.Section == ModSection.Explicit).Select(m => catalog.Mods.ByHash(m.Hash)).OfType<ModCatalog.Mod>().ToList();
        int maxEach = type.Class is "Jewel" or "AbyssJewel" ? 2 : 3;
        for (int n = 0; n < count; n++)
        {
            var pool = rolls.Pool(type, item.ItemLevel).Where(p =>
                rules.Allows(p.Mod) &&
                present.Count(m => m.GenerationType == p.Mod.GenerationType) < maxEach &&
                !present.Any(m => m.Hash == p.Mod.Hash || m.Families.Intersect(p.Mod.Families).Any())).ToList();
            if (pool.Count == 0) return null;
            long value = rng.NextInt64(pool.Sum(p => (long)p.Weight));
            var selected = pool[^1].Mod;
            foreach (var candidate in pool)
            { if (value < candidate.Weight) { selected = candidate.Mod; break; } value -= candidate.Weight; }
            present.Add(selected);
            mods.Add(selected.Roll(ModSection.Explicit, rng));
        }
        return item with { Mods = mods };
    }

    static IReadOnlyList<Recipe> Load(ItemCatalog catalog, string folder)
    {
        string options = Path.Combine(folder, "tables/CraftingBenchOptions.json"), categories = Path.Combine(folder, "tables/CraftingItemClassCategories.json");
        if (!File.Exists(options) || !File.Exists(categories)) return [];
        using var cats = JsonDocument.Parse(File.ReadAllBytes(categories));
        using var doc = JsonDocument.Parse(File.ReadAllBytes(options));
        ModCatalog.Mod? Mod(JsonElement r, string key) => r.GetProperty(key).ValueKind == JsonValueKind.Number ? catalog.Mods.Row(r.GetProperty(key).GetInt32()) : null;
        var result = new List<Recipe>();
        int row = -1;
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            row++;
            if (r.GetProperty("IsDisabled").GetBoolean() || r.GetProperty("IsAreaOption").GetBoolean() || r.GetProperty("MapDevice").GetBoolean()) continue;
            var classes = r.GetProperty("ItemClasses").EnumerateArray().Select(c => c.GetInt32()).ToHashSet();
            if (classes.Count == 0)
                foreach (int category in r.GetProperty("CraftingItemClassCategories").EnumerateArray().Select(c => c.GetInt32()))
                    classes.UnionWith(cats.RootElement[category].GetProperty("ItemClasses").EnumerateArray().Select(c => c.GetInt32()));
            var costs = r.GetProperty("Cost_BaseItemTypes").EnumerateArray().Zip(r.GetProperty("Cost_Values").EnumerateArray(),
                (b, n) => (catalog.Row(b.GetInt32())!.Hash, n.GetInt32())).ToList();
            result.Add(new(row, r.GetProperty("Name").GetString()!, Mod(r, "AddMod"), Mod(r, "AddEnchantment"),
                r.GetProperty("CraftingBenchCustomAction").GetInt32(), r.GetProperty("Sockets").GetInt32(),
                r.GetProperty("Links").GetInt32(), r.GetProperty("SocketColours").GetString()!, classes, costs,
                r.GetProperty("RecipeIds").EnumerateArray().Select(c => c.GetInt32()).ToList()));
        }
        return result;
    }
}
