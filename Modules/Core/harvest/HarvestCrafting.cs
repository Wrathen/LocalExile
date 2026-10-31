using System.Text.Json;
using System.Text.RegularExpressions;
using GameServer.Core.Protocol.Instance;
using GameServer.Modding;

namespace GameServer.Modules.Harvest;

/// <summary>
/// HarvestCraftOptions.dat (data/tables/HarvestCraftOptions.json): the recipes the native Horticrafting Station lists.
/// The server executes the item recipes (targeted/affinity reforges, augments and modifier conversions) against the item
/// in HarvestCraftingItem, and refuses the recipes for systems it does not model (influences, exchanges, enchants, gems).
/// Costs are the exact lifeforce types and amounts from the table.
/// </summary>
public sealed class HarvestCrafting(IItemCrafting items, string folder = "data")
{
    public enum Operation { Unsupported, ReforgeTag, ReforgeAffinity, AugmentTag, Convert }

    public sealed record Recipe(ushort Hash, string Id, Operation Operation, string? Tag, int Affinity,
        IReadOnlyDictionary<string, string> Convert, IReadOnlyList<(uint Currency, int Amount)> Costs)
    {
        public bool Supported => Operation != Operation.Unsupported;
    }

    // The four lifeforce currencies, resolved once from BaseItemTypes (the table has only the type number).
    public const string Wild = "Metadata/Items/Currency/HarvestSeedRed",
        Vivid = "Metadata/Items/Currency/HarvestSeedGreen",
        Primal = "Metadata/Items/Currency/HarvestSeedBlue",
        Sacred = "Metadata/Items/Currency/HarvestSeedBoss";

    static readonly string[] Metamods =
    [
        "item_generation_cannot_change_prefixes", "item_generation_cannot_change_suffixes",
        "item_generation_cannot_roll_attack_affixes", "item_generation_cannot_roll_caster_affixes",
    ];

    readonly Lazy<IReadOnlyList<Recipe>> _all = new(() => Load(items, folder));
    public IReadOnlyList<Recipe> All => _all.Value;
    public Recipe? ByHash(ushort hash) => All.FirstOrDefault(r => r.Hash == hash);

    /// <summary>The crafted item and its exact cost, or null when the recipe is unknown, unsupported or does not fit
    /// this item (nothing is changed or spent).</summary>
    public CraftResult? Apply(ushort hash, Item item, Random rng)
    {
        if (ByHash(hash) is not { } recipe || !recipe.Supported || item.Corrupted || item.Mirrored || !item.Identified ||
            !item.Components.Contains("Mods") || items.HasExplicitStat(item, Metamods)) return null;
        Item? result = recipe.Operation switch
        {
            Operation.ReforgeTag => items.RollRequiredTag(item, ItemRarity.Rare, rng, recipe.Tag!),
            Operation.ReforgeAffinity => items.RollAffinity(item, ItemRarity.Rare, rng, recipe.Affinity),
            Operation.AugmentTag => Augment(item, recipe.Tag!, rng),
            Operation.Convert => items.ConvertMod(item, recipe.Convert, rng),
            _ => null,
        };
        return result is null ? null : new(result with { Identified = true }, recipe.Costs);
    }

    Item? Augment(Item item, string tag, Random rng)
    {
        if (item.Rarity is not (ItemRarity.Magic or ItemRarity.Rare) ||
            items.Annul(item, rng) is not { } removed) return null;
        (int Prefix, int Suffix) max = item.Rarity == ItemRarity.Magic ? (1, 1) : items.RareMaxAffixes(item);
        return items.AddTaggedAffix(removed, max, rng, tag);
    }

    static IReadOnlyList<Recipe> Load(IItemCrafting catalog, string folder)
    {
        string path = Path.Combine(folder, "tables", "HarvestCraftOptions.json");
        if (!File.Exists(path)) return [];
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var rows = doc.RootElement.EnumerateArray().ToList();
        var lifeforce = new[]
        {
            (Type: 1, Hash: catalog.TypeHash(Wild) ?? 0),
            (Type: 2, Hash: catalog.TypeHash(Vivid) ?? 0),
            (Type: 3, Hash: catalog.TypeHash(Primal) ?? 0),
        };
        uint sacred = catalog.TypeHash(Sacred) ?? 0;

        // Conversion recipes point at a target by element; the reverse row lists the target's ModType groups, which is
        // how a source group is mapped to the same-tier group in the other element.
        var conversions = new Dictionary<int, (string[] Sources, string From, string To)>();
        foreach (var row in rows)
        {
            if (row.GetProperty("Command").GetString() != "convert_mod") continue;
            var parts = (row.GetProperty("Parameters").GetString() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int convert = Array.IndexOf(parts, "CONVERT");
            if (convert < 1 || convert + 2 >= parts.Length) continue;
            conversions[row.GetProperty("HASH16").GetInt32()] = (parts[..convert],
                parts[convert + 1].ToLowerInvariant(), parts[convert + 2].ToLowerInvariant());
        }

        var modTypes = catalog.ModTypes;
        var result = new List<Recipe>();
        foreach (var row in rows)
        {
            string id = row.GetProperty("Id").GetString() ?? "";
            string command = row.GetProperty("Command").GetString() ?? "";
            var parts = (row.GetProperty("Parameters").GetString() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var costs = Costs(row.GetProperty("LifeforceType").GetInt32(), row.GetProperty("LifeforceCost").GetInt32(),
                row.GetProperty("SacredCost").GetInt32(), lifeforce, sacred);
            Operation operation = Operation.Unsupported;
            string? tag = null;
            int affinity = 0;
            IReadOnlyDictionary<string, string> convertMap = new Dictionary<string, string>();
            if (costs is null) { /* an unknown lifeforce currency: the recipe stays unsupported */ }
            else if (command == "reroll_with_mod" && parts.Length > 0) { operation = Operation.ReforgeTag; tag = parts[0].ToLowerInvariant(); }
            else if (command == "reroll_with_current_tags_affinity_multiplier" && parts.Length > 0 &&
                int.TryParse(parts[0], out affinity)) operation = Operation.ReforgeAffinity;
            else if (command == "remove_type_and_add_type_mod")
            {
                int forIndex = Array.IndexOf(parts, "FOR");
                if (forIndex >= 0 && forIndex + 1 < parts.Length)
                {
                    operation = Operation.AugmentTag;
                    tag = parts[forIndex + 1].ToLowerInvariant();
                }
            }
            else if (command == "convert_mod" && conversions.TryGetValue(row.GetProperty("HASH16").GetInt32(), out var c))
            {
                // The reverse recipes (fire -> cold) list the target groups. Both the resistance and the damage pair use
                // the same elements, so every reverse source list is a candidate; the replaced/stripped name picks the
                // right group (FireResistance vs FireDamage).
                var targetIds = conversions.Values.Where(x => x.From == c.To && x.To == c.From)
                    .SelectMany(x => x.Sources).ToList();
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string source in c.Sources)
                {
                    string replaced = Regex.Replace(source, c.From, c.To, RegexOptions.IgnoreCase);
                    string? target = targetIds.FirstOrDefault(t => t.Equals(replaced, StringComparison.OrdinalIgnoreCase))
                        ?? targetIds.FirstOrDefault(t => StripToken(t, c.To).Equals(StripToken(source, c.From), StringComparison.OrdinalIgnoreCase));
                    if (target is not null && modTypes.Contains(source) && modTypes.Contains(target))
                        map[source] = target;
                }
                if (map.Count > 0) { operation = Operation.Convert; convertMap = map; }
            }
            result.Add(new((ushort)row.GetProperty("HASH16").GetInt32(), id, operation, tag, affinity, convertMap,
                costs ?? []));
        }
        return result;
    }

    static IReadOnlyList<(uint Currency, int Amount)>? Costs(int type, int amount, int sacred,
        (int Type, uint Hash)[] lifeforce, uint sacredHash)
    {
        var costs = new List<(uint, int)>();
        if (amount > 0)
        {
            uint currency = lifeforce.FirstOrDefault(l => l.Type == type).Hash;
            if (currency == 0) return null;
            costs.Add((currency, amount));
        }
        if (sacred > 0)
        {
            if (sacredHash == 0) return null;
            costs.Add((sacredHash, sacred));
        }
        return costs;
    }

    static string StripToken(string id, string token) => Regex.Replace(id, token, "", RegexOptions.IgnoreCase);
}
