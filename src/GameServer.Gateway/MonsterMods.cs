using System.Text.Json;
using GameServer.Core.Protocol.Instance;
using DamageModel = GameServer.Core.World.DamageModel;
using DamagePacket = GameServer.Core.World.DamagePacket;
using DamageConversion = GameServer.Core.World.DamageConversion;
using DamageType = GameServer.Core.World.DamageType;

/// <summary>
/// A monster's mods (Agents/docs/protocol/14-monster-packs.md §5): the rarity's own mods (Mods.dat MonsterMagic1–11,
/// MonsterRare1–12, MonsterUnique1–10), the affixes a magic or rare monster rolled, and a rare monster's name
/// (Words.dat keys). <see cref="Effects"/> are the numbers the server's calculations read. One set per monster; kept
/// with the instance (SavedMonster) so a re-entered area has the same mods.
/// </summary>
sealed record MonsterModSet(IReadOnlyList<ItemMod> Mods, IReadOnlyList<uint> NameWords, MonsterModEffects Effects)
{
    public static readonly MonsterModSet None = new([], [], MonsterModEffects.None);

    /// <summary>The rolled affixes (the explicit list), without the rarity's own mods.</summary>
    public IEnumerable<ItemMod> Affixes => Mods.Where(m => m.Section == ModSection.Explicit);
}

/// <summary>
/// What a monster's mods change, summed per stat over its mods (Mods.dat stat ids): the numbers the life pool, its hits
/// on the player, its walk, the player's hit chance and the damage the player deals read. Only the stats in
/// <see cref="MonsterModCatalog.Modelled"/> act; the others are kept for the record.
/// </summary>
sealed class MonsterModEffects(IReadOnlyDictionary<string, int> stats)
{
    public static readonly MonsterModEffects None = new(new Dictionary<string, int>());

    /// <summary>Monster.ot's <c>base_critical_strike_multiplier</c> (130), the critical multiplier of every monster. Their
    /// attacks have no base critical strike chance in the data (the monster Melee stat sets' AttackCritChance is 0), so
    /// only a mod that sets one (always_crit) makes them crit.</summary>
    public const int CriticalMultiplier = 130;

    /// <summary>Monster.ot's <c>maximum_physical_damage_reduction_%</c> and <c>base_maximum_all_resistances_%</c>.</summary>
    public const int MaxPhysicalReduction = 75, MaxResistance = 75;

    /// <summary>The highest block chance a monster can have: Monster.ot's <c>maximum_block_%</c>.</summary>
    public const int MaxBlock = 75;

    public IReadOnlyDictionary<string, int> Stats => stats;

    public int this[string stat] => stats.GetValueOrDefault(stat);

    /// <summary>Maximum life: the base for the level and variety × (1 + maximum_life_+%) × (1 +
    /// monster_life_+%_final_from_rarity) × (1 + monster_life_+%_final_from_rarity_table).</summary>
    public int MaxLife(int baseLife) => Math.Max(1, (int)Math.Floor(baseLife * (1 + this["maximum_life_+%"] / 100.0)
        * (1 + this["monster_life_+%_final_from_rarity"] / 100.0) * (1 + this[MonsterModCatalog.RarityTableLifeStat] / 100.0)));

    /// <summary>The monster's hit before the player's defences: the base physical damage raised by damage_+% and
    /// physical_damage_+% (increased, the gains inherit them), by the rarity's monster_rarity_damage_+%_final (more)
    /// and lowered by monster_rarity_attack_cast_speed_+%_and_damage_-%_final (less), rounded and at least 1 as the
    /// rarity's hit always was (Hillock at level 1: 6.6–12.3 → 7–12); then the "gain as extra" stats add their types
    /// (fire, cold or lightning from physical, chaos from the non-chaos part), rounded.</summary>
    public DamagePacket Hit(DamagePacket physical)
    {
        double factor = (1 + (this["damage_+%"] + this["physical_damage_+%"]) / 100.0)
            * (1 + this["monster_rarity_damage_+%_final"] / 100.0)
            * (1 - this["monster_rarity_attack_cast_speed_+%_and_damage_-%_final"] / 100.0);
        var gains = new DamageConversion()
            .AddGain(DamageType.Physical, DamageType.Fire, this["physical_damage_%_to_add_as_fire"])
            .AddGain(DamageType.Physical, DamageType.Cold, this["physical_damage_%_to_add_as_cold"])
            .AddGain(DamageType.Physical, DamageType.Lightning, this["physical_damage_%_to_add_as_lightning"]);
        var physicalRange = physical[DamageType.Physical];
        var hit = DamagePacket.Of(DamageType.Physical, Math.Max(1, Math.Round(physicalRange.Min * factor)),
            Math.Max(1, Math.Round(physicalRange.Max * factor))).Convert(gains).Round();
        int chaos = this["non_chaos_damage_to_add_as_chaos_damage_%"];
        if (chaos <= 0) return hit;
        double minChaos = 0, maxChaos = 0;
        foreach (var (type, range) in hit.Components)
            if (type != DamageType.Chaos) { minChaos += range.Min * chaos / 100.0; maxChaos += range.Max * chaos / 100.0; }
        return hit.Add(DamageType.Chaos, new GameServer.Core.World.DamageRange(minChaos, maxChaos).Round());
    }

    /// <summary>The attack time: the base over (1 + attack_speed_+%) and the rarity's more speed
    /// (monster_rarity_attack_cast_speed_+%_and_damage_-%_final). Every attack-speed mod of the pool gives cast speed
    /// alike, so a casting monster's skill uses the same time.</summary>
    public double AttackSeconds(double baseSeconds) => baseSeconds
        / Math.Max(0.1, 1 + this["attack_speed_+%"] / 100.0)
        / (1 + this["monster_rarity_attack_cast_speed_+%_and_damage_-%_final"] / 100.0);

    /// <summary>The walk's movement_velocity_+% (base_movement_velocity_+% summed), sent to the client so both walk
    /// the monster at its base × (1 + value / 100) (0x141e7c0b0).</summary>
    public int MovementPct => this["base_movement_velocity_+%"];

    public double MovementSpeed(double baseSpeed) => Math.Max(1, baseSpeed * (100 + MovementPct) / 100.0);

    /// <summary>The resistance to one damage type the mods give (before the cap; physical has none here).</summary>
    public int Resistance(DamageType type) => type switch
    {
        DamageType.Fire => this["base_fire_damage_resistance_%"] + this["base_resist_all_elements_%"],
        DamageType.Cold => this["base_cold_damage_resistance_%"] + this["base_resist_all_elements_%"],
        DamageType.Lightning => this["base_lightning_damage_resistance_%"] + this["base_resist_all_elements_%"],
        DamageType.Chaos => this["base_chaos_damage_resistance_%"],
        _ => 0,
    };

    /// <summary>Physical damage reduction (Carapaced, the god-touched mods), capped at Monster.ot's 75%.</summary>
    public int PhysicalReduction => Math.Clamp(this["base_additional_physical_damage_reduction_%"], 0, MaxPhysicalReduction);

    public int Accuracy(int baseAccuracy) => (int)Math.Round(baseAccuracy * (1 + this["accuracy_rating_+%"] / 100.0));

    public int Evasion(int baseEvasion) => (int)Math.Round(baseEvasion * (1 + this["evasion_rating_+%"] / 100.0));

    /// <summary>The chance its hit is a critical strike, permyriad: always_crit, else none (no base chance to increase).</summary>
    public int CriticalChance => this["always_crit"] > 0 ? 10_000 : 0;

    /// <summary>A monster's critical multiplier on the player: Monster.ot's 130%, less the player's
    /// base_self_critical_strike_multiplier_-% (the panel's extra_damage_taken_from_crit_+%), at least 100%.</summary>
    public static int CriticalMultiplierOnPlayer(int selfCriticalReduction) => Math.Max(100, CriticalMultiplier - selfCriticalReduction);

    /// <summary>Life regenerated per second: life_regeneration_rate_per_minute_% of the maximum.</summary>
    public double LifeRegenPerSecond(uint maxLife) => maxLife * this["life_regeneration_rate_per_minute_%"] / 6000.0;

    /// <summary>The factor on damage the monster takes: base_damage_taken_+% (increased/reduced), and for spells the
    /// rare mod's rare_monster_mod_spell_damage_taken_+%_final (less).</summary>
    public double DamageTaken(bool spell) => Math.Max(0, 1 + this["base_damage_taken_+%"] / 100.0)
        * (spell ? Math.Max(0, 1 + this["rare_monster_mod_spell_damage_taken_+%_final"] / 100.0) : 1);

    /// <summary>The chance to block an attack (monster_base_block_%), capped at Monster.ot's maximum_block_%.</summary>
    public int BlockChance => Math.Clamp(this["monster_base_block_%"], 0, MaxBlock);

    public bool CannotBeStunned => this["base_cannot_be_stunned"] > 0;

    /// <summary>"Takes no extra damage from critical strikes": a critical hit on it deals its normal damage.</summary>
    public bool NoExtraCriticalDamage => this["self_take_no_extra_damage_from_critical_strikes"] > 0;

    /// <summary>The increased damage a hit gets for the target's missing life: hit_damage_+%_per_10%_missing_enemy_life
    /// per whole 10% of the target's life it misses.</summary>
    public int ExecutionerPct(uint life, uint maxLife) => maxLife == 0 ? 0
        : this["hit_damage_+%_per_10%_missing_enemy_life"] * (int)((maxLife - Math.Min(life, maxLife)) * 10 / maxLife);

    /// <summary>base_actor_scale_+%: the model's size, sent as actor_scale_+%.</summary>
    public int ActorScalePct => this["base_actor_scale_+%"];
}

/// <summary>
/// Monster mods from Mods.dat (Agents/docs/protocol/14-monster-packs.md §5). An affix can roll on a magic or rare
/// monster when its row has the monster Domain (3), GenerationType 1 (prefix) or 2 (suffix), Level at most the
/// monster's level and a spawn weight above 0 for the monster's tags (<see cref="TagsOf"/>: its MonsterVarieties
/// TagsKeys, "default", its rarity's tag and "not_part_of_pack" when it is alone); a ModFamily is taken once. Only
/// mods the server can honour roll by default (<see cref="SupportOf"/>; <c>Game_MONSTER_MODS=all</c> rolls the whole
/// pool for tests). Counts: <see cref="AffixCount"/>. A rare monster is named from Words.dat lists 3 (monster prefix)
/// and 4 (monster suffix) by the same tag weights.
/// </summary>
sealed class MonsterModCatalog(ModCatalog mods, string folder = "data")
{
    public const int Domain = 3, Prefix = 1, Suffix = 2;
    public const int MonsterPrefixWords = 3, MonsterSuffixWords = 4;
    /// <summary>The stat the rarity's level table (MagicMonsterLifeScalingPerLevel, RareMonsterLifeScalingPerLevel)
    /// gives: Stats.dat row 9877. No Mods.dat row carries it; the server applies it.</summary>
    public const string RarityTableLifeStat = "monster_life_+%_final_from_rarity_table";
    /// <summary>The area level from which rare monsters have the maps' mod counts (the first map level).</summary>
    public const int MapLevel = 68;

    /// <summary>How well the server honours a mod: every stat acts (Full), its main stats act and the rest is listed in
    /// the docs (Partial), or it needs a system the server does not have (None).</summary>
    public enum Support { None, Partial, Full }

    /// <summary>Stats the server applies (MonsterModEffects).</summary>
    public static readonly IReadOnlySet<string> Modelled = new HashSet<string>
    {
        "maximum_life_+%", "damage_+%", "physical_damage_+%",
        "physical_damage_%_to_add_as_fire", "physical_damage_%_to_add_as_cold", "physical_damage_%_to_add_as_lightning",
        "non_chaos_damage_to_add_as_chaos_damage_%", "attack_speed_+%", "base_cast_speed_+%", "base_movement_velocity_+%",
        "base_fire_damage_resistance_%", "base_cold_damage_resistance_%", "base_lightning_damage_resistance_%",
        "base_chaos_damage_resistance_%", "base_resist_all_elements_%", "base_additional_physical_damage_reduction_%",
        "evasion_rating_+%", "accuracy_rating_+%", "always_crit",
        "life_regeneration_rate_per_minute_%", "rare_monster_mod_spell_damage_taken_+%_final", "base_damage_taken_+%",
        "monster_base_block_%", "base_cannot_be_stunned", "hit_damage_+%_per_10%_missing_enemy_life",
        "self_take_no_extra_damage_from_critical_strikes", "avoid_impale_%", "cannot_have_life_leeched_from",
        "cannot_have_energy_shield_leeched_from",
    };

    /// <summary>Stats that only stop something the server does not do to monsters yet (ailments on monsters, curses,
    /// slows): true as they stand. Each needs its effect when that system arrives.</summary>
    public static readonly IReadOnlySet<string> Vacuous = new HashSet<string>
    {
        "base_self_ignite_duration_-%", "chill_effectiveness_on_self_+%", "base_self_freeze_duration_-%",
        "shocked_effect_on_self_+%", "self_wither_duration_+%", "hexproof",
        "action_speed_cannot_be_reduced_below_base", "movement_speed_cannot_be_reduced_below_base",
        "reflect_hexes_chance_%", "reflect_non_damaging_ailments_%_chance",
    };

    /// <summary>Monster-buff templates whose stats are the mod's own and apply to the monster that has the aura
    /// (Speed, Large Physical Damage, Accuracy and Crits aura): the allies around it get nothing yet.</summary>
    static readonly HashSet<string> SelfAuraMods = ["MonsterModSpeedAura", "MonsterModPhysicalDamageAura", "MonsterModAccuracyAndCritAura"];

    /// <summary>Whether a monster mod can be honoured (the pool's filter, and the PR's table). A mod with a buff, a
    /// granted skill, a monster on death or no stats needs a system the server lacks; Berserking's buff has its own
    /// server-side trigger. Stats the server applies (or that only stop what it never does) make it Full; with others
    /// besides (exposure on hit) or an aura that reaches only its owner, Partial; none of its stats, None.</summary>
    public static Support SupportOf(ModCatalog.Mod m)
    {
        if (m.Stats.Count == 0 || m.GrantedEffects > 0 || m.MonsterOnDeath.Length > 0) return Support.None;
        bool aura = m.BuffTemplate is not null;
        if (aura && !SelfAuraMods.Contains(m.Id)) return Support.None;
        int modelled = m.Stats.Count(s => Modelled.Contains(s.Id)), vacuous = m.Stats.Count(s => Vacuous.Contains(s.Id));
        // Hexproof and the like: every stat stops what the server never does, so the mod holds as it stands.
        if (modelled + vacuous == m.Stats.Count) return aura ? Support.Partial : Support.Full;
        return modelled > 0 ? Support.Partial : Support.None;
    }

    /// <summary>Whether unsupported mods roll too (<c>Game_MONSTER_MODS=all</c>): for checking how the client shows them.</summary>
    public bool RollUnsupported { get; init; } = Environment.GetEnvironmentVariable("Game_MONSTER_MODS") == "all";

    private readonly Lazy<Dictionary<int, IReadOnlySet<string>>> _varietyTags = new(() => LoadVarietyTags(folder));
    private readonly Lazy<List<ItemRolls.Word>> _words = new(() => LoadWords(folder));
    private readonly Lazy<(int[] Magic, int[] Rare)> _lifeTables = new(() => LoadLifeTables(folder));
    private readonly Lazy<(int MaxPrefix, int MaxSuffix)[]> _rarity = new(() => LoadRarity(folder));

    public ModCatalog Mods => mods;

    /// <summary>The monster's tags for spawn weights: its MonsterVarieties TagsKeys, "default", "magic" / "rare" for
    /// its rarity (the client adds the same tag, 0x1421ff3c0) and "not_part_of_pack" for a monster on its own.</summary>
    public IReadOnlySet<string> TagsOf(MonsterCatalog.Variety variety, MonsterRarity rarity, bool alone)
    {
        var tags = new HashSet<string>(_varietyTags.Value.GetValueOrDefault(variety.Row) ?? new HashSet<string>()) { "default" };
        if (rarity == MonsterRarity.Magic) tags.Add("magic");
        if (rarity == MonsterRarity.Rare) tags.Add("rare");
        if (alone) tags.Add("not_part_of_pack");
        return tags;
    }

    /// <summary>The affixes that can roll on a monster with these tags at this level, with their weights.</summary>
    public IReadOnlyList<(ModCatalog.Mod Mod, int Weight)> Pool(IReadOnlySet<string> tags, int level, bool unsupported = false) =>
        [.. mods.All.Where(m => m.Domain == Domain && m.GenerationType is Prefix or Suffix && m.Level <= level
                && (unsupported || SupportOf(m) != Support.None))
            .Select(m => (m, m.WeightFor(tags))).Where(p => p.Item2 > 0)];

    /// <summary>
    /// How many affixes a monster rolls; the client's data has no count (Rarity.dat's 1–2 / 4–6 are the items'). Magic:
    /// one (poewiki "Rarity": "given one monster modifier"). Rare: two to four (poewiki "Rarity": "given two to four
    /// monster modifiers"; the 3.20 FAQ balances around 2 to 4). The 3.19.0 patch notes gave campaign rares one or two,
    /// but 3.20 replaced that mod system, so the current page wins; the campaign keeps fewer (the notes' intent): below area
    /// level 68 2 at 75%, 3 at 25%; from 68 2 at 40%, 3 at 40%, 4 at 20%. These percentages are invented. Other
    /// rarities roll none.
    /// </summary>
    public static int AffixCount(MonsterRarity rarity, int level, Random rng) => rarity switch
    {
        MonsterRarity.Magic => 1,
        MonsterRarity.Rare when level < MapLevel => rng.Next(100) < 75 ? 2 : 3,
        MonsterRarity.Rare => rng.Next(100) switch { < 40 => 2, < 80 => 3, _ => 4 },
        _ => 0,
    };

    /// <summary>The rarity's own mods (Mods.dat MonsterMagic1–11, MonsterRare1–12, MonsterUnique1–10), which every
    /// monster of the rarity has: its life, damage, speed, rewards. Empty for normal monsters and without Mods.dat.</summary>
    public IReadOnlyList<ModCatalog.Mod> Innate(MonsterRarity rarity)
    {
        string prefix = rarity switch { MonsterRarity.Magic => "MonsterMagic", MonsterRarity.Rare => "MonsterRare", MonsterRarity.Unique => "MonsterUnique", _ => "" };
        if (prefix.Length == 0) return [];
        var list = new List<ModCatalog.Mod>();
        for (int i = 1; mods.ById($"{prefix}{i}") is { } m; i++) list.Add(m);
        return list;
    }

    /// <summary>The rarity table's extra life at a level (MagicMonsterLifeScalingPerLevel / RareMonsterLifeScalingPerLevel
    /// "Life": magic +50% at level 1 to +75%, rare +33% to +100%), the server's monster_life_+%_final_from_rarity_table.</summary>
    public int RarityTableLifePct(MonsterRarity rarity, int level)
    {
        var table = rarity switch { MonsterRarity.Magic => _lifeTables.Value.Magic, MonsterRarity.Rare => _lifeTables.Value.Rare, _ => [] };
        return table.Length == 0 ? 0 : table[Math.Clamp(level, 1, table.Length) - 1];
    }

    /// <summary>
    /// A monster's mods: its rarity's own (as hidden mods: nothing to show, the client applies none of them itself)
    /// and, for magic and rare monsters, <paramref name="count"/> (default <see cref="AffixCount"/>) affixes drawn by
    /// weight from the pool, each family once, at most Rarity.dat's MaxPrefix / MaxSuffix of each kind (magic 1 / 1,
    /// rare 3 / 3), prefixes first; a rare monster also gets a name. <paramref name="forced"/> mods come first and are
    /// not checked (the /mob_mods command).
    /// </summary>
    public MonsterModSet Roll(MonsterCatalog.Variety variety, int level, MonsterRarity rarity, Random rng, bool alone = false,
        int? count = null, IReadOnlyList<ModCatalog.Mod>? forced = null)
    {
        var tags = TagsOf(variety, rarity, alone);
        var chosen = new List<ModCatalog.Mod>(forced ?? []);
        if (rarity is MonsterRarity.Magic or MonsterRarity.Rare)
        {
            int want = count ?? AffixCount(rarity, level, rng);
            var (maxPrefix, maxSuffix) = MaxAffixes(rarity);
            var pool = Pool(tags, level, RollUnsupported);
            while (chosen.Count < want)
            {
                var allowed = pool.Where(p => chosen.Count(c => c.GenerationType == p.Mod.GenerationType)
                        < (p.Mod.GenerationType == Prefix ? maxPrefix : maxSuffix)
                    && !chosen.Any(c => c.Hash == p.Mod.Hash || c.Families.Intersect(p.Mod.Families).Any())).ToList();
                if (allowed.Count == 0) break;
                chosen.Add(Pick(allowed, rng));
            }
        }
        var affixes = chosen.OrderBy(m => m.GenerationType == Suffix ? 1 : 0).Select(m => m.Roll(ModSection.Explicit, rng));
        var innate = Innate(rarity).Select(m => m.Roll(ModSection.Hidden, rng));
        var all = (IReadOnlyList<ItemMod>)[.. innate, .. affixes];
        var name = rarity == MonsterRarity.Rare ? RareName(tags, rng) : [];
        return Create(all, name, rarity, level);
    }

    /// <summary>
    /// The mods a magic monster of an area pack gets: the ones the magic monsters of its name already have in this
    /// instance (<paramref name="shared"/>, "all monsters in an instance that are the same or very similar will have the
    /// same magic monster modifier", 3.19.0), unless one of those affixes could not roll on it (a weight of 0 for its
    /// tags, or above its level: The Coast's small Savage Crabs can be Blurred, the large one cannot); then the ones of
    /// its name and tags, rolled once. Call under a lock on <paramref name="shared"/>.
    /// </summary>
    public MonsterModSet SharedMagic(IDictionary<string, MonsterModSet> shared, MonsterCatalog.Variety variety, int level, Random rng)
    {
        var tags = TagsOf(variety, MonsterRarity.Magic, alone: false);
        bool Fits(MonsterModSet set) => set.Affixes.All(a => mods.ByHash(a.Hash) is { } m && m.Level <= level && m.WeightFor(tags) > 0);
        if (shared.TryGetValue(variety.Name, out var set) && Fits(set)) return set;
        string key = variety.Name + "|" + string.Join(",", tags.Order(StringComparer.Ordinal));
        if (!shared.TryGetValue(key, out set)) shared[key] = set = Roll(variety, level, MonsterRarity.Magic, rng);
        shared.TryAdd(variety.Name, set);
        return set;
    }

    /// <summary>A set from known mods (a kept monster, a test): its effects from the mods' values, plus the rarity
    /// table's life. Without the rarity's own mods (no Mods.dat) the life and damage of the rarity still apply
    /// (MonsterCatalog.RarityLifePct / RarityAttack).</summary>
    public MonsterModSet Create(IReadOnlyList<ItemMod> list, IReadOnlyList<uint> nameWords, MonsterRarity rarity, int level)
    {
        var stats = new Dictionary<string, int>();
        void Add(string stat, int value) => stats[stat] = stats.GetValueOrDefault(stat) + value;
        foreach (var m in list)
            if (mods.ByHash(m.Hash) is { } mod)
                for (int i = 0; i < mod.Stats.Count && i < m.Values.Count; i++) Add(mod.Stats[i].Id, m.Values[i]);
        if (rarity != MonsterRarity.Normal && !stats.ContainsKey("monster_life_+%_final_from_rarity"))
        {
            Add("monster_life_+%_final_from_rarity", MonsterCatalog.RarityLifePct(rarity));
            var (more, faster) = rarity switch { MonsterRarity.Magic => (30, 20), MonsterRarity.Rare => (50, 33), MonsterRarity.Unique => (70, 33), _ => (0, 0) };
            Add("monster_rarity_damage_+%_final", more);
            Add("monster_rarity_attack_cast_speed_+%_and_damage_-%_final", faster);
        }
        if (RarityTableLifePct(rarity, level) is int table and > 0) Add(RarityTableLifeStat, table);
        return new MonsterModSet(list, nameWords, new MonsterModEffects(stats));
    }

    (int MaxPrefix, int MaxSuffix) MaxAffixes(MonsterRarity rarity)
    {
        var rows = _rarity.Value;
        int i = (int)rarity;
        if (i < rows.Length && rows[i].MaxPrefix + rows[i].MaxSuffix > 0) return rows[i];
        return rarity == MonsterRarity.Magic ? (1, 1) : (3, 3);
    }

    /// <summary>A rare monster's name: a Words.dat MONSTER_PREFIX and a MONSTER_SUFFIX word by the monster's tags (the
    /// suffixes are lower case and join the prefix: "Ash" + "bite"); empty when a list has no word for the tags.</summary>
    public IReadOnlyList<uint> RareName(IReadOnlySet<string> tags, Random rng)
    {
        uint? Draw(int list)
        {
            var words = _words.Value.Where(w => w.List == list)
                .Select(w => (w, w.SpawnWeights.FirstOrDefault(s => tags.Contains(s.Tag)).Weight)).Where(p => p.Weight > 0).ToList();
            return words.Count == 0 ? null : Pick(words, rng).Key;
        }
        return Draw(MonsterPrefixWords) is uint a && Draw(MonsterSuffixWords) is uint b ? [a, b] : [];
    }

    /// <summary>"Ashbite" from the keys (logs, /mob_mods).</summary>
    public string NameOf(IReadOnlyList<uint> keys) => string.Concat(keys.Select(k => _words.Value.FirstOrDefault(w => w.Key == k)?.Text ?? $"[{k:x8}]"));

    /// <summary>"Quick (MonsterModAttackCastMovementSpeed)" per affix, for logs and /mob_mods.</summary>
    public string Describe(MonsterModSet set) => string.Join(", ", set.Affixes.Select(a => mods.ByHash(a.Hash) is { } m
        ? $"{m.Name.Trim()} ({m.Id}{(SupportOf(m) == Support.None ? ", no effect" : "")})" : $"0x{a.Hash:x4}"));

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

    static List<string> LoadTagNames(string folder)
    {
        string path = Path.Combine(folder, "tables", "Tags.json");
        if (!File.Exists(path)) return [];
        using var t = JsonDocument.Parse(File.ReadAllBytes(path));
        return [.. t.RootElement.EnumerateArray().Select(r => r.GetProperty("Id").GetString() ?? "")];
    }

    static Dictionary<int, IReadOnlySet<string>> LoadVarietyTags(string folder)
    {
        string path = Path.Combine(folder, "tables", "MonsterVarieties.json");
        var result = new Dictionary<int, IReadOnlySet<string>>();
        if (!File.Exists(path)) return result;
        var tags = LoadTagNames(folder);
        using var d = GameTables.Open(path);
        int row = 0;
        foreach (var r in d.RootElement.EnumerateArray())
            result[row++] = r.GetProperty("TagsKeys").EnumerateArray().Select(k => k.GetInt32())
                .Where(k => k >= 0 && k < tags.Count).Select(k => tags[k]).ToHashSet();
        return result;
    }

    static List<ItemRolls.Word> LoadWords(string folder)
    {
        string path = Path.Combine(folder, "tables", "Words.json");
        if (!File.Exists(path)) return [];
        var tags = LoadTagNames(folder);
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        return [.. d.RootElement.EnumerateArray()
            .Where(r => r.GetProperty("Wordlist").GetInt32() is MonsterPrefixWords or MonsterSuffixWords)
            .Select(r => new ItemRolls.Word(r.GetProperty("HASH32").GetUInt32(), r.GetProperty("Text").GetString() ?? "",
                r.GetProperty("Wordlist").GetInt32(),
                [.. r.GetProperty("SpawnWeight_Tags").EnumerateArray().Zip(r.GetProperty("SpawnWeight_Values").EnumerateArray(),
                    (k, v) => (k.GetInt32() < tags.Count ? tags[k.GetInt32()] : "", v.GetInt32()))]))];
    }

    static (int[], int[]) LoadLifeTables(string folder)
    {
        int[] Table(string name)
        {
            string path = Path.Combine(folder, "tables", name + ".json");
            if (!File.Exists(path)) return [];
            using var d = JsonDocument.Parse(File.ReadAllBytes(path));
            return [.. d.RootElement.EnumerateArray().OrderBy(r => r.GetProperty("Level").GetInt32()).Select(r => r.GetProperty("Life").GetInt32())];
        }
        return (Table("MagicMonsterLifeScalingPerLevel"), Table("RareMonsterLifeScalingPerLevel"));
    }

    static (int, int)[] LoadRarity(string folder)
    {
        string path = Path.Combine(folder, "tables", "Rarity.json");
        if (!File.Exists(path)) return [];
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        return [.. d.RootElement.EnumerateArray().Select(r => (r.GetProperty("MaxPrefix").GetInt32(), r.GetProperty("MaxSuffix").GetInt32()))];
    }
}
