using System.Text.Json;
using GameServer.Core.Protocol.Instance;

/// <summary>
/// Unique items the server can create. The client data has a unique's name (<c>Words</c>) and artwork
/// (<c>ItemVisualIdentity</c>, linked through <c>UniqueStashLayout</c>), but no table that says which Mods rows belong to
/// it, so each definition below lists them from the game's data (poedb: Mageblood, 3.29). The base type's implicits are
/// rolled by <see cref="ItemCatalog.Create"/>; the unique's own mods are explicit and rolled once per item.
/// </summary>
sealed class UniqueCatalog(ItemCatalog items, string folder = "data")
{
    /// <summary>One unique. <paramref name="Implicits"/> replaces the base type's own when the unique differs from it
    /// (null keeps the base's). <paramref name="Ranges"/> are "mod id:stat index:min:max" for a current roll that
    /// differs from the Mods.dat row, which legacy variants of a unique can share.</summary>
    public sealed record Definition(string Name, string BaseTypeId, string[] Mods, byte ItemLevel = 86,
        bool Corrupted = false, string[]? Implicits = null, string[]? Ranges = null, bool Mirrored = false,
        ItemInfluence Influences = ItemInfluence.None);

    /// <summary>The two entries whose curated 3.29 data differs from the PoB export used for the full catalogue.</summary>
    static readonly Definition[] Curated =
    [
        // Mageblood (Heavy Belt): the +25-35 Strength is the Heavy Belt implicit; the rest are the unique's mods.
        new("Mageblood", "Metadata/Items/Belts/Belt4",
            ["DexterityUnique__3", "FireResistUnique__24", "ColdResistUnique__34",
             "MagicUtilityFlasksCannotUseUnique__1____", "MagicUtilityFlasksCannotRemoveUnique__1",
             "MagicUtilityFlasksAlwaysApplyUnique__1"]),
        // Crown of Eyes (3.29): use the current attack-leech mods, not the old physical-only variants.
        new("Crown of Eyes", "Metadata/Items/Armours/Helmets/HelmetInt7",
            ["IncreasedAccuracyUniqueHelmetInt7", "LocalIncreasedEnergyShieldUniqueHelmetInt7",
             "FireResistUniqueHelmetInt7", "LifeLeechPermyriadUniqueHelmetInt7",
             "ManaLeechPermyriadUniqueHelmetInt7", "SpellDamageModifiersApplyToAttackDamageUniqueHelmetInt7"]),
    ];

    /// <summary>The generated full PoB catalogue (see scripts/re/unique_items.py), with the curated entries first so
    /// the existing item tests and name lookup keep their 3.29 definitions.</summary>
    internal static readonly Definition[] Seed =
    [
        .. Curated,
        .. UniqueItemSeed.All.Where(entry => Curated.All(curated =>
            !curated.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase))),
    ];

    readonly Lazy<Dictionary<string, UniqueName>> _names = new(() => LoadNames(folder));

    public IReadOnlyList<Definition> All => GameDataCatalog.Uniques;

    /// <summary>The definition a name matches, or null: the exact name, then the name followed by a base type's name for
    /// a unique with several bases ("Grand Spectrum Cobalt Jewel"), then the first containing it.</summary>
    public Definition? Find(string name) =>
        All.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? All.FirstOrDefault(d => name.StartsWith(d.Name + " ", StringComparison.OrdinalIgnoreCase)
            && items.Find(d.BaseTypeId)?.Name.Equals(name[(d.Name.Length + 1)..], StringComparison.OrdinalIgnoreCase) == true)
        ?? All.FirstOrDefault(d => d.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The unique item for a name, or null when the name is not one or its base type is not writable.</summary>
    public Item? Create(string name, Random random) => Find(name) is { } definition ? Create(definition, random) : null;

    /// <summary>The item of one definition, which tells apart the bases of a unique with several.</summary>
    public Item? Create(Definition definition, Random random)
    {
        if (items.Find(definition.BaseTypeId) is not { Supported: true } type) return null;
        if (!_names.Value.TryGetValue(NameKey(definition.Name), out var unique)) return null;
        var item = ItemCatalog.Create(type) with
        {
            ItemLevel = definition.ItemLevel, Rarity = ItemRarity.Unique, Identified = true,
            Corrupted = definition.Corrupted, Mirrored = definition.Mirrored, Influences = definition.Influences,
            NameWords = [unique.Word], VisualIdentity = unique.VisualIdentity,
        };
        var mods = item.Mods.ToList();
        if (definition.Implicits is { } implicits)
        {
            mods.RemoveAll(m => m.Section == ModSection.Implicit);
            mods.AddRange(Rolled(definition, implicits, ModSection.Implicit, random));
        }
        mods.AddRange(Rolled(definition, definition.Mods, ModSection.Explicit, random));
        return item with { Mods = mods };
    }

    IEnumerable<ItemMod> Rolled(Definition definition, string[] ids, ModSection section, Random random)
    {
        foreach (string id in ids)
        {
            if (items.Mods.ById(id) is not { } mod) continue;
            var values = mod.Stats.Select((_, i) =>
            {
                var (min, max) = Override(definition, id, i) ?? mod.RangeAt(i);
                return min == max ? min : random.Next(Math.Min(min, max), Math.Max(min, max) + 1);
            }).ToArray();
            yield return new ItemMod(section, mod.Hash, values);
        }
    }

    static (int Min, int Max)? Override(Definition definition, string id, int stat)
    {
        foreach (string entry in definition.Ranges ?? [])
            if (entry.Split(':') is [var mod, var index, var min, var max] && mod == id && int.Parse(index) == stat)
                return (int.Parse(min), int.Parse(max));
        return null;
    }

    /// <summary>Ancient Orb ("Reforges a unique equipment as another of the same item class"): another unique of the class,
    /// by <paramref name="weight"/> (ItemDrops.UniqueWeight: a Mageblood stays rare), keeping the item's level, quality,
    /// sockets, links and identified state. Corrupted and mirrored items are refused, and corrupted and Replica uniques are
    /// not results. Our own limit: only uniques of the same inventory size, which can hold its sockets, so the item fits
    /// where it lies and no socketed gem is lost.</summary>
    public Item? Reforge(Item item, Func<string, double> weight, Random random)
    {
        if (item.Rarity != ItemRarity.Unique || item.Corrupted || item.Mirrored || items.ByHash(item.TypeHash) is not { } current
            || !ItemDrops.EquipmentClasses.Contains(current.Class)) return null;
        var candidates = new List<(Definition Definition, double Weight)>();
        foreach (var definition in All)
        {
            if (definition.Corrupted || definition.Name.StartsWith("Replica ", StringComparison.Ordinal)
                || weight(definition.Name) is not (> 0 and var chance) || items.Find(definition.BaseTypeId) is not { Supported: true } type
                || type.Class != current.Class || type.Width != current.Width || type.Height != current.Height
                || !_names.Value.TryGetValue(NameKey(definition.Name), out var name) || item.NameWords.Contains(name.Word)
                || item.Sockets.Count > CurrencyEffects.SocketCapacity(item, type)) continue;
            candidates.Add((definition, chance));
        }
        if (candidates.Count == 0) return null;
        double pick = random.NextDouble() * candidates.Sum(c => c.Weight);
        var chosen = candidates[^1].Definition;
        foreach (var (definition, chance) in candidates)
        {
            if (pick < chance) { chosen = definition; break; }
            pick -= chance;
        }
        return Create(chosen, random) is { } created ? created with
        {
            ItemLevel = item.ItemLevel, Quality = item.Quality, Sockets = item.Sockets, Links = item.Links, Identified = item.Identified,
        } : null;
    }

    sealed record UniqueName(uint Word, ushort VisualIdentity);

    /// <summary>What ties a definition's name to its Words row: Words writes some apostrophes curly ("Emperor’s Vigilance")
    /// and has Broken Elegy without its article (scripts/re/unique_items.py keeps the same key).</summary>
    static string NameKey(string name)
    {
        string key = name.Replace('’', '\'').Trim();
        return key.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? key[4..] : key;
    }

    static Dictionary<string, UniqueName> LoadNames(string folder)
    {
        var result = new Dictionary<string, UniqueName>(StringComparer.OrdinalIgnoreCase);
        string Table(string name) => Path.Combine(folder, "tables", name + ".json");
        if (!File.Exists(Table("Words")) || !File.Exists(Table("UniqueStashLayout")) || !File.Exists(Table("ItemVisualIdentity")))
            return result;
        using var words = JsonDocument.Parse(File.ReadAllBytes(Table("Words")));
        using var layout = JsonDocument.Parse(File.ReadAllBytes(Table("UniqueStashLayout")));
        using var visuals = JsonDocument.Parse(File.ReadAllBytes(Table("ItemVisualIdentity")));
        // Unique names are wordlist 6 (rare names use 1 and 2); a layout row links the word to the unique artwork.
        // Keep every wordlist row, not only the definitions known at startup: a definition added to the database later
        // can then resolve its existing client word and art without a code change.
        var byWord = new Dictionary<int, string>();
        for (int i = 0; i < words.RootElement.GetArrayLength(); i++)
            if (words.RootElement[i].GetProperty("Wordlist").GetInt32() == 6
                && words.RootElement[i].GetProperty("Text").GetString() is { Length: > 0 } text)
                byWord[i] = text;
        foreach (var r in layout.RootElement.EnumerateArray())
        {
            int wordsKey = r.GetProperty("WordsKey").GetInt32(), visualKey = r.GetProperty("ItemVisualIdentityKey").GetInt32();
            if (!byWord.TryGetValue(wordsKey, out string? name) || visualKey < 0
                || visualKey >= visuals.RootElement.GetArrayLength()) continue;
            // A unique can have a second row with its alternate art (Headhunter2.dds); the ordinary art is the item's.
            if (r.TryGetProperty("IsAlternateArt", out var alternate) && alternate.GetBoolean() && result.ContainsKey(NameKey(name))) continue;
            result[NameKey(name)] = new(words.RootElement[wordsKey].GetProperty("HASH32").GetUInt32(),
                (ushort)visuals.RootElement[visualKey].GetProperty("HASH16").GetInt32());
        }
        return result;
    }
}
