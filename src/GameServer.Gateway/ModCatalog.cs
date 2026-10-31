using System.Text.Json;
using GameServer.Core.Protocol.Instance;

/// <summary>
/// Mods.dat (data/tables/Mods.json, loaded on first use): what the server needs to put mods on items
/// (Agents/docs/protocol/13-items.md §2a). The client identifies a mod by its HASH16 and counts its stats itself
/// (StatsKey1..8 up to the first empty one), so every <see cref="ItemMod"/> is built here with exactly that many values.
/// </summary>
sealed class ModCatalog(string folder = "data", bool hardMode = false, ModRangeProvider? ranges = null)
{
    public ModRangeProvider Ranges { get; } = ranges ?? ModRangeProvider.Default;

    /// <summary>A stat of a mod: its Stats.dat Id, the Stat{n}Min / Stat{n}Max roll range, and Stats.dat IsLocal (the
    /// stat changes the item it is on, not the character).</summary>
    public sealed record ModStat(string Id, int Min, int Max, bool Local = false);

    /// <summary>A Mods.dat row. GenerationType 1 prefix, 2 suffix, 3 unique / base implicit, 10 enchantment …;
    /// Domain 1 items.</summary>
    /// <summary>A Mods.dat row. Rolling (ItemRolls): SpawnWeights is the (Tags.dat Id, weight) list where the first tag the
    /// item has decides, GenerationWeights multiply that by weight / 100, Families exclude each other on one item.</summary>
    public sealed record Mod(int Row, string Id, ushort Hash, string Name, int Domain, int GenerationType, int Level,
        IReadOnlyList<ModStat> Stats)
    {
        internal ModRangeProvider? Ranges { get; init; }
        public IReadOnlyList<int> Families { get; init; } = [];
        public IReadOnlyList<(string Tag, int Weight)> SpawnWeights { get; init; } = [];
        public IReadOnlyList<(string Tag, int Weight)> GenerationWeights { get; init; } = [];
        public bool EssenceOnly { get; init; }
        /// <summary>ModType.dat's Name for ModTypeKey: the Harvest conversion groups (ColdResistance, FireDamage …).</summary>
        public string ModType { get; init; } = "";
        public IReadOnlySet<string> Tags { get; init; } = new HashSet<string>();
        /// <summary>What a mod does beyond its stats (monster mods: MonsterModCatalog): its BuffTemplates row (an aura or
        /// buff, BuffTemplate1), the monster it leaves on death (MonsterOnDeath) and its granted skills
        /// (GrantedEffectsPerLevelKeys).</summary>
        public int? BuffTemplate { get; init; }
        public string MonsterOnDeath { get; init; } = "";
        public int GrantedEffects { get; init; }
        public IReadOnlyList<int> GrantedEffectLevels { get; init; } = [];

        /// <summary>The weight for an item with these tags: the first spawn tag it has, times the first generation tag
        /// it has / 100; 0 when no spawn tag matches.</summary>
        public int WeightFor(IReadOnlySet<string> tags)
        {
            int weight = SpawnWeights.FirstOrDefault(w => tags.Contains(w.Tag)).Weight;
            if (weight > 0 && GenerationWeights.FirstOrDefault(w => tags.Contains(w.Tag)) is { Tag: not null } g) weight = weight * g.Weight / 100;
            return weight;
        }

        /// <summary>The list a mod goes into when nothing else says so: prefixes and suffixes are explicit,
        /// enchantments enchants, everything else (base implicits, corruptions …) implicit.</summary>
        public ModSection DefaultSection => GenerationType switch { 1 or 2 => ModSection.Explicit, 10 => ModSection.Enchant, _ => ModSection.Implicit };

        /// <summary>The effective range of one stat: the database override when present, else the base Mods.dat range.</summary>
        public (int Min, int Max) RangeAt(int index) =>
            index >= 0 && index < Stats.Count
                ? Ranges?.Range(Id, index, Stats[index].Min, Stats[index].Max) ?? Normalise(Stats[index])
                : (0, 0);

        /// <summary>The effective maxima, in stat order (max-roll dev items and unique definitions use these).</summary>
        public IReadOnlyList<int> MaxValues() => [.. Stats.Select((_, i) => RangeAt(i).Max)];

        static (int Min, int Max) Normalise(ModStat stat) => (Math.Min(stat.Min, stat.Max), Math.Max(stat.Min, stat.Max));

        /// <summary>The mod with a value per stat rolled in its effective range.</summary>
        public ItemMod Roll(ModSection section, Random random)
        {
            var values = new int[Stats.Count];
            for (int i = 0; i < values.Length; i++)
            {
                var (min, max) = RangeAt(i);
                values[i] = random.Next(min, max + 1);
            }
            return new(section, Hash, values);
        }

        /// <summary>The mod with these values: missing ones are rolled, extra ones dropped (the client reads exactly one
        /// per stat).</summary>
        public ItemMod With(ModSection section, IReadOnlyList<int> values, Random random)
        {
            var rolled = Roll(section, random).Values;
            return new(section, Hash, [.. rolled.Select((v, i) => i < values.Count ? values[i] : v)]);
        }

        public override string ToString() => $"{Id} (row {Row})";
    }

    private readonly Lazy<(List<Mod> Rows, Dictionary<ushort, Mod> ByHash, Dictionary<string, Mod> ById)> _mods =
        new(() => Load(folder, hardMode, ranges ?? ModRangeProvider.Default));

    public bool Loaded => _mods.Value.Rows.Count > 0;

    public Mod? Row(int row) => row >= 0 && row < _mods.Value.Rows.Count ? _mods.Value.Rows[row] : null;

    public Mod? ByHash(ushort hash) => _mods.Value.ByHash.GetValueOrDefault(hash);

    public Mod? ById(string id) => _mods.Value.ById.GetValueOrDefault(id);

    public IReadOnlyList<Mod> All => _mods.Value.Rows;

    /// <summary>A mod by row number, exact Id (any case), then the first Id that contains the text.</summary>
    public Mod? Find(string query)
    {
        if (int.TryParse(query, out int row)) return Row(row);
        return ById(query) ?? _mods.Value.Rows.FirstOrDefault(m => m.Id.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>"SpellDamageOnWeaponImplicitWand1 implicit: spell_damage_+% 10" for logs and /item_mods.</summary>
    public string Describe(ItemMod m) => ByHash(m.Hash) is { } mod
        ? $"{mod.Id} {m.Section.ToString().ToLowerInvariant()}: " + string.Join(", ", mod.Stats.Select((s, i) => $"{s.Id} {(i < m.Values.Count ? m.Values[i] : 0)}"))
        : $"0x{m.Hash:x4} {m.Section.ToString().ToLowerInvariant()}: {string.Join(", ", m.Values)}";

    private static (List<Mod>, Dictionary<ushort, Mod>, Dictionary<string, Mod>) Load(string folder, bool hardMode,
        ModRangeProvider ranges)
    {
        string path = Path.Combine(folder, "tables", "Mods.json"), statsPath = Path.Combine(folder, "tables", "Stats.json"),
            tagsPath = Path.Combine(folder, "tables", "Tags.json"), modTypePath = Path.Combine(folder, "tables", "ModType.json");
        var rows = new List<Mod>();
        if (!File.Exists(path) || !File.Exists(statsPath)) return (rows, [], []);
        List<(string Id, bool Local)> stats;
        using (var s = JsonDocument.Parse(File.ReadAllBytes(statsPath)))
            stats = [.. s.RootElement.EnumerateArray().Select(r => (r.GetProperty("Id").GetString() ?? "", r.GetProperty("IsLocal").GetBoolean()))];
        List<string> modTypes = [];
        if (File.Exists(modTypePath))
            using (var t = JsonDocument.Parse(File.ReadAllBytes(modTypePath)))
                modTypes = [.. t.RootElement.EnumerateArray().Select(r => r.GetProperty("Name").GetString() ?? "")];
        List<string> tags = [];
        if (File.Exists(tagsPath))
            using (var t = JsonDocument.Parse(File.ReadAllBytes(tagsPath)))
                tags = [.. t.RootElement.EnumerateArray().Select(r => r.GetProperty("Id").GetString() ?? "")];
        List<(string, int)> Weights(JsonElement r, string keys, string values) =>
            [.. r.GetProperty(keys).EnumerateArray().Zip(r.GetProperty(values).EnumerateArray(),
                (k, v) => (k.GetInt32() < tags.Count ? tags[k.GetInt32()] : $"#{k.GetInt32()}", v.GetInt32()))];
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var r in d.RootElement.EnumerateArray())
        {
            bool alternate = hardMode && r.TryGetProperty("HasHardmodeStatValues", out var hardmode) && hardmode.GetBoolean();
            string suffix = alternate ? "Hardmode" : "";
            // The client's stat iterator (0x142430710) stops at the first empty key, so the count does too.
            var modStats = new List<ModStat>();
            for (int i = 1; i <= 8; i++)
            {
                if (r.GetProperty($"StatsKey{i}") is not { ValueKind: JsonValueKind.Number } k) break;
                int key = k.GetInt32();
                var (statId, local) = key < stats.Count ? stats[key] : ($"#{key}", false);
                // Explicit zero is meaningful (for example disabled boss rarity bonuses); it must not fall back.
                modStats.Add(new(statId, r.GetProperty($"Stat{i}Min{suffix}").GetInt32(), r.GetProperty($"Stat{i}Max{suffix}").GetInt32(), local));
            }
            rows.Add(new Mod(rows.Count, r.GetProperty("Id").GetString() ?? "", (ushort)r.GetProperty("HASH16").GetInt32(),
                r.GetProperty("Name").GetString() ?? "", r.GetProperty("Domain").GetInt32(), r.GetProperty("GenerationType").GetInt32(),
                r.GetProperty("Level").GetInt32(), modStats)
            {
                Ranges = ranges,
                Families = [.. r.GetProperty("Families").EnumerateArray().Select(f => f.GetInt32())],
                SpawnWeights = Weights(r, "SpawnWeight_TagsKeys", "SpawnWeight_Values"),
                GenerationWeights = Weights(r, "GenerationWeight_TagsKeys", "GenerationWeight_Values"),
                EssenceOnly = r.GetProperty("IsEssenceOnlyModifier").GetBoolean(),
                ModType = r.TryGetProperty("ModTypeKey", out var modType) && modType.ValueKind == JsonValueKind.Number &&
                    modType.GetInt32() is int mt && mt >= 0 && mt < modTypes.Count ? modTypes[mt] : "",
                BuffTemplate = r.TryGetProperty("BuffTemplate1", out var buff) && buff.ValueKind == JsonValueKind.Number ? buff.GetInt32() : null,
                MonsterOnDeath = r.TryGetProperty("MonsterOnDeath", out var death) ? death.GetString() ?? "" : "",
                GrantedEffects = r.TryGetProperty("GrantedEffectsPerLevelKeys", out var granted) ? granted.GetArrayLength() : 0,
                GrantedEffectLevels = r.TryGetProperty("GrantedEffectsPerLevelKeys", out var effects)
                    ? [.. effects.EnumerateArray().Select(e => e.GetInt32())] : [],
                Tags = new[] { "TagsKeys", "ImplicitTagsKeys" }
                    .Where(key => r.TryGetProperty(key, out _))
                    .SelectMany(key => r.GetProperty(key).EnumerateArray().Select(k => k.GetInt32()))
                    .Where(k => k >= 0 && k < tags.Count).Select(k => tags[k]).ToHashSet(),
            });
        }
        var byHash = new Dictionary<ushort, Mod>();
        var byId = new Dictionary<string, Mod>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in rows) { byHash.TryAdd(m.Hash, m); byId.TryAdd(m.Id, m); }
        return (rows, byHash, byId);
    }
}
