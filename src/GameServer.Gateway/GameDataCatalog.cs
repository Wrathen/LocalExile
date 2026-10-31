using System.Text.Json;
using GameServer.Storage;

/// <summary>
/// The item and jewel catalogues that used to be static arrays in code. The current arrays are the seed; the SQLite
/// <c>catalog_entries</c> table is the runtime source once a kind has been seeded. Each kind is loaded once at startup
/// and replaced atomically after <c>/game_data reload</c>. A fresh database keeps the seed's exact contents.
/// </summary>
static class GameDataCatalog
{
    public sealed record TattooStatRow(string Stat, int Value);
    public sealed record TattooEffectRow(string Name, TattooStatRow[] Stats);
    public sealed record VisualRow(string Name, ushort Hash);

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Raise when UniqueItemSeed corrects existing rows, so that databases already seeded take the correction.</summary>
    const string UniqueSeedRevision = "3";

    static readonly UniqueCatalog.Definition[] UniqueSeed = UniqueCatalog.Seed;
    static readonly RadiusJewelCatalogue.Entry[] RadiusSeed = RadiusJewelCatalogue.Seed;
    static readonly RadiusJewelCatalogue.Entry[] ThresholdSeed = ThresholdJewelCatalogue.Seed;
    static readonly UniqueJewelCatalogue.Entry[] GlobalSeed = UniqueJewelCatalogue.SeedGlobal;
    static readonly UniqueJewelCatalogue.Entry[] SpecialSeed = UniqueJewelCatalogue.SeedSpecial;
    static readonly TimelessJewelCatalog.Entry[] TimelessSeed = TimelessJewelCatalog.Seed;
    static readonly TattooEffectRow[] TattooSeed = [.. TattooStats.Seed.Select(kv =>
        new TattooEffectRow(kv.Key, [.. kv.Value.Select(v => new TattooStatRow(v.Stat, v.Value))]))];
    static readonly VisualRow[] VisualSeed = [.. UniqueJewelVisuals.Seed.Select(kv => new VisualRow(kv.Key, kv.Value))];

    static UniqueCatalog.Definition[] _uniques = UniqueSeed;
    static RadiusJewelCatalogue.Entry[] _radius = RadiusSeed;
    static RadiusJewelCatalogue.Entry[] _threshold = ThresholdSeed;
    static RadiusJewelCatalogue.Entry[] _radiusAll = [.. RadiusSeed, .. ThresholdSeed];
    static UniqueJewelCatalogue.Entry[] _globals = GlobalSeed;
    static UniqueJewelCatalogue.Entry[] _specials = SpecialSeed;
    static TimelessJewelCatalog.Entry[] _timeless = TimelessSeed;
    static IReadOnlyDictionary<string, (string Stat, int Value)[]> _tattoos = ToTattooMap(TattooSeed);
    static IReadOnlyDictionary<string, ushort> _visuals = ToVisualMap(VisualSeed);

    public static UniqueCatalog.Definition[] Uniques => Volatile.Read(ref _uniques);
    public static RadiusJewelCatalogue.Entry[] RadiusJewels => Volatile.Read(ref _radiusAll);
    public static RadiusJewelCatalogue.Entry[] ThresholdJewels => Volatile.Read(ref _threshold);
    public static UniqueJewelCatalogue.Entry[] UniqueJewelGlobals => Volatile.Read(ref _globals);
    public static UniqueJewelCatalogue.Entry[] UniqueJewelSpecials => Volatile.Read(ref _specials);
    public static TimelessJewelCatalog.Entry[] TimelessJewels => Volatile.Read(ref _timeless);
    public static IReadOnlyDictionary<string, (string Stat, int Value)[]> TattooEffects => Volatile.Read(ref _tattoos);
    public static IReadOnlyDictionary<string, ushort> JewelVisuals => Volatile.Read(ref _visuals);

    public static string Summary =>
        $"{Uniques.Length} unique item(s), {RadiusJewels.Length} radius jewel(s), {UniqueJewelGlobals.Length + UniqueJewelSpecials.Length} unique jewel(s), " +
        $"{TimelessJewels.Length} timeless jewel(s), {TattooEffects.Count} tattoo effect(s), {JewelVisuals.Count} jewel art row(s)";

    public static void ResetToSeeds()
    {
        Volatile.Write(ref _uniques, UniqueSeed);
        Volatile.Write(ref _radius, RadiusSeed);
        Volatile.Write(ref _threshold, ThresholdSeed);
        Volatile.Write(ref _radiusAll, [.. RadiusSeed, .. ThresholdSeed]);
        Volatile.Write(ref _globals, GlobalSeed);
        Volatile.Write(ref _specials, SpecialSeed);
        Volatile.Write(ref _timeless, TimelessSeed);
        Volatile.Write(ref _tattoos, ToTattooMap(TattooSeed));
        Volatile.Write(ref _visuals, ToVisualMap(VisualSeed));
    }

    public static void Reload(GameDataStore store)
    {
        var uniques = Load(store, "unique_item", UniqueSeed, UniqueKey);
        var radius = Load(store, "radius_jewel", RadiusSeed, RadiusKey);
        var threshold = Load(store, "threshold_jewel", ThresholdSeed, RadiusKey);
        var globals = Load(store, "unique_jewel_global", GlobalSeed, entry => entry.Name);
        var specials = Load(store, "unique_jewel_special", SpecialSeed, entry => entry.Name);
        var timeless = Load(store, "timeless_jewel", TimelessSeed, entry => entry.Name);
        var tattoos = ToTattooMap(Load(store, "tattoo_effect", TattooSeed, entry => entry.Name));
        var visuals = ToVisualMap(Load(store, "unique_jewel_visual", VisualSeed, entry => entry.Name));

        Volatile.Write(ref _uniques, uniques);
        Volatile.Write(ref _radius, radius);
        Volatile.Write(ref _threshold, threshold);
        Volatile.Write(ref _radiusAll, [.. radius, .. threshold]);
        Volatile.Write(ref _globals, globals);
        Volatile.Write(ref _specials, specials);
        Volatile.Write(ref _timeless, timeless);
        Volatile.Write(ref _tattoos, tattoos);
        Volatile.Write(ref _visuals, visuals);
    }

    static T[] Load<T>(GameDataStore store, string kind, T[] seed, Func<T, string> key)
    {
        if (kind == "unique_item") MigrateUniqueKeys(store);
        Seed(store, kind, seed, key);
        if (kind == "unique_item") RefreshSeed(store, kind, seed, key, UniqueSeedRevision);
        return [.. store.LoadCatalog(kind).Where(row => row.Enabled)
            .Select(row => JsonSerializer.Deserialize<T>(row.Payload, Json)
                ?? throw new InvalidOperationException($"catalog {kind} row {row.Key} is empty"))];
    }

    /// <summary>
    /// Seeds a kind once, then adds only keys that are new to the seed since the last run. The applied seed key set is
    /// recorded, so a key that was in the previous seed and is missing from the catalogue was deleted by an admin and
    /// stays deleted; only a genuinely new default is inserted. On the first run with this tracking, an existing
    /// catalogue (or the old seeded flag) is kept as it is and the current seed becomes the baseline, which preserves
    /// deletions made before the table existed.
    /// </summary>
    internal static void Seed<T>(GameDataStore store, string kind, T[] seed, Func<T, string> key)
    {
        var seedKeys = seed.Select(key).ToHashSet(StringComparer.Ordinal);
        var applied = store.LoadSeededKeys(kind);
        if (applied.Count == 0)
        {
            bool hasCatalog = store.CatalogSeeded(kind) || store.LoadCatalog(kind).Count > 0;
            if (!hasCatalog)
                foreach (var entry in seed)
                    store.PutCatalogRow(kind, key(entry), JsonSerializer.Serialize(entry, Json));
            store.ReplaceSeededKeys(kind, seedKeys);
            if (!store.CatalogSeeded(kind)) store.MarkCatalogSeeded(kind);
            return;
        }

        var existing = store.LoadCatalog(kind).Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in seed)
        {
            string rowKey = key(entry);
            if (!applied.Contains(rowKey) && !existing.Contains(rowKey))
                store.PutCatalogRow(kind, rowKey, JsonSerializer.Serialize(entry, Json));
        }
        if (!seedKeys.SetEquals(applied)) store.ReplaceSeededKeys(kind, seedKeys);
        if (!store.CatalogSeeded(kind)) store.MarkCatalogSeeded(kind);
    }

    /// <summary>
    /// <see cref="Seed"/> never changes a row it wrote, so a corrected seed would not reach an existing database. When
    /// <paramref name="revision"/> is new, the rows nobody edited since the last seed take their new defaults, and those
    /// whose key left the seed go (a base type correction changes the key). Edited rows stay as they are, and so do
    /// rows an admin deleted, unless the correction gave them a new key.
    /// </summary>
    internal static void RefreshSeed<T>(GameDataStore store, string kind, T[] seed, Func<T, string> key, string revision)
    {
        string revisionKey = "catalog_seed_revision:" + kind, writtenKey = "catalog_seed_written:" + kind;
        if (store.Meta(revisionKey) == revision) return;
        if ((store.Meta(writtenKey) ?? store.Meta("catalog_seeded:" + kind)) is { } written)
        {
            var defaults = seed.ToDictionary(key, entry => JsonSerializer.Serialize(entry, Json), StringComparer.Ordinal);
            var unedited = store.CatalogKeysWrittenBy(kind, written);
            foreach (var row in store.LoadCatalog(kind).Where(row => unedited.Contains(row.Key)))
                if (!defaults.TryGetValue(row.Key, out string? payload)) store.DeleteCatalogRow(kind, row.Key);
                else if (payload != row.Payload) store.PutCatalogRow(kind, row.Key, payload, row.Enabled);
        }
        store.SetMeta(revisionKey, revision);
        store.SetMeta(writtenKey, DateTime.UtcNow.ToString("O"));
    }

    /// <summary>The first full-catalogue build used the unique name alone as the key; move those rows aside once so
    /// the same name can appear for different bases (Combat Focus, Doryani's Delusion, Grand Spectrum, Stormblood).</summary>
    static void MigrateUniqueKeys(GameDataStore store)
    {
        foreach (string key in store.LoadCatalog("unique_item").Where(row => !row.Key.Contains('|')).Select(row => row.Key).ToList())
            store.DeleteCatalogRow("unique_item", key);
    }

    static string UniqueKey(UniqueCatalog.Definition entry) => $"{entry.Name}|{entry.BaseTypeId}";
    static string RadiusKey(RadiusJewelCatalogue.Entry entry) => $"{entry.Name}|{entry.Base}";

    static IReadOnlyDictionary<string, (string Stat, int Value)[]> ToTattooMap(IEnumerable<TattooEffectRow> rows) =>
        rows.ToDictionary(row => row.Name,
            row => row.Stats.Select(stat => (stat.Stat, stat.Value)).ToArray(), StringComparer.Ordinal);

    static IReadOnlyDictionary<string, ushort> ToVisualMap(IEnumerable<VisualRow> rows) =>
        rows.ToDictionary(row => row.Name, row => row.Hash, StringComparer.OrdinalIgnoreCase);
}
