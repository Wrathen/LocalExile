using System.Collections.Frozen;
using System.Text.Json;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.World;

/// <summary>
/// Skills the server grants: a GrantedEffects Id at a level becomes the GrantedEffectsPerLevel row and the
/// GrantedEffectStatSetsPerLevel row (of the effect's StatSet1 at that gem level) the Actor skill list refers to
/// (Agents/docs/protocol/11-skills.md). Reads data/tables (game data, loaded on first use). Without the tables it
/// knows only the default attack, PlayerMelee at level 1 (GEPL 4474, GESSPL 4430 in 3.29.3b).
/// </summary>
sealed class SkillCatalog(string folder = "data", bool hardMode = false)
{
    /// <summary>The ids the server gives its skills (+0x40). Basic actions use their ActionTypes HASH16 instead, so these
    /// must not collide with the type's basic actions (Character.ot: Move 0x2909, Interaction 0x0266 …). 0x0101 did: a
    /// Monster.ot basic action masks to it. 0x0102 is no ActionType's id in 3.29.3b.</summary>
    public const ushort FirstSkillId = 0x0102;

    private readonly Lazy<Tables?> _tables = new(() => Tables.Load(folder));
    private readonly StatCatalog _stats = new(folder);

    // Both sets of effects already have their own native rows and per-level stat sets. Only name-based lookups
    // require resolution; explicit row-based calls retain their exact identity (including monster effects).
    private readonly Lazy<Dictionary<int, int>> _modeEffects = new(() =>
    {
        var result = new Dictionary<int, int>();
        string path = Path.Combine(folder, "tables", "GemEffects.json");
        if (!hardMode || !File.Exists(path)) return result;
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var r in doc.RootElement.EnumerateArray())
            foreach (string name in new[] { "GrantedEffect", "GrantedEffect2" })
                if (r.TryGetProperty(name, out var regular) && regular.ValueKind == JsonValueKind.Number
                    && r.TryGetProperty(name + "Hardmode", out var alternate) && alternate.ValueKind == JsonValueKind.Number
                    && regular.GetInt32() >= 0 && alternate.GetInt32() >= 0)
                    result.TryAdd(regular.GetInt32(), alternate.GetInt32());
        return result;
    });

    /// <summary>Each GrantedEffects row's additional stat sets (StatSet2), when it has any.</summary>
    private readonly Lazy<Dictionary<int, int[]>> _extraSets = new(() =>
    {
        var result = new Dictionary<int, int[]>();
        string path = Path.Combine(folder, "tables", "GrantedEffects.json");
        if (!File.Exists(path)) return result;
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        int row = 0;
        foreach (var r in d.RootElement.EnumerateArray())
        {
            if (r.TryGetProperty("StatSet2", out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0)
                result[row] = [.. v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32())];
            row++;
        }
        return result;
    });

    /// <summary>A skill with its effect's additional stat sets: each set's per-level row at this level (the closest one
    /// otherwise) and its stats (implicit 1s, constants, then the level's stats). Null when a set has no row at all: a
    /// record without it would make the client misread it.</summary>
    GrantedSkill? WithExtraSets(GrantedSkill skill, Tables t, int effectRow, int level)
    {
        // Both Find paths use catalog identity and raw stored uses, never effective cost or current souls.
        skill = skill with { VaalResourcesEnabled = VaalBudget(effectRow, level) is not null };
        if (!_extraSets.Value.TryGetValue(effectRow, out var sets)) return skill;
        var extra = new List<(uint, IReadOnlyList<(uint, int)>)>();
        foreach (int set in sets)
        {
            int row = PickLevel(t.StatSetsPerLevel, set, level);
            if (row < 0) return null;
            var stats = new Dictionary<uint, int>();
            if (set >= 0 && set < t.StatSetBase.Count)
            {
                foreach (int s in t.StatSetBase[set].Implicit) stats[(uint)s + 1] = 1;
                foreach (var (s, v) in t.StatSetBase[set].Constant) stats[(uint)s + 1] = v;
            }
            foreach (var (s, v) in t.StatSetLevelStats[row]) stats[(uint)s + 1] = stats.GetValueOrDefault((uint)s + 1) + v;
            extra.Add(((uint)row, [.. stats.Select(kv => (kv.Key, kv.Value))]));
        }
        return skill with { ExtraStatSets = extra };
    }

    /// <summary>Skill ids for the server's skills from <see cref="FirstSkillId"/> on, skipping every id an ActionType's
    /// HASH16 masks to (0x3fff): 0x0106, 0x010a and 0x0114 are basic actions in 3.29.3b, and a gem skill numbered after
    /// the default attack would replace one.</summary>
    public IEnumerable<ushort> SkillIds()
    {
        var taken = _actionIds.Value;
        for (int id = FirstSkillId; id <= 0x3fff; id++)
            if (!taken.Contains((ushort)id)) yield return (ushort)id;
    }

    /// <summary>Whether an id is one of the gem skills' (<see cref="SkillIds"/> after the default attack, the first 64), not a
    /// basic action such as Move.</summary>
    public bool IsGemSkill(ushort id) => id > FirstSkillId && id < FirstSkillId + 64 && !_actionIds.Value.Contains(id);

    private readonly Lazy<HashSet<ushort>> _actionIds = new(() =>
    {
        string path = Path.Combine(folder, "tables", "ActionTypes.json");
        if (!File.Exists(path)) return [0x0101, 0x0106, 0x010a, 0x0114];
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        return [.. d.RootElement.EnumerateArray().Select(r => (ushort)(r.GetProperty("HASH16").GetInt32() & 0x3fff))];
    });

    /// <summary>
    /// A gem skill's own stats at a level (client keys = Stats.dat row + 1): the effect's stat set (GrantedEffectStatSets
    /// ImplicitStats = 1, ConstantStats with their values) and its per-level row (GESSPL FloatStats with
    /// BaseResolvedValues, e.g. Fireball's spell_minimum/maximum_base_fire_damage 9–14 at level 1, and AdditionalStats),
    /// then the cast: base_spell_cast_time_ms (GrantedEffects CastTime), hundred_times_casts_per_second, and the mana cost
    /// (GEPL CostAmounts of CostTypes 0 = mana). Then the quality stats: GrantedEffectQualityStats per 1% quality in
    /// thousandths, times the gem's quality, rounded toward zero (the wiki: rounded down;
    /// the 16 negative values are unverified). No modifiers yet: the base values are the final ones.
    /// </summary>
    public IReadOnlyList<(uint, int)> EffectStats(int effectRow, int level, int quality = 0)
    {
        if (_tables.Value is not { } t || effectRow < 0 || effectRow >= t.Effects.Count) return [];
        var e = t.Effects[effectRow];
        var stats = new Dictionary<uint, int>();
        var constant = new HashSet<uint>();
        if (e.StatSet >= 0 && e.StatSet < t.StatSetBase.Count)
        {
            var (implicits, constants) = t.StatSetBase[e.StatSet];
            foreach (int s in implicits) stats[(uint)s + 1] = 1;
            foreach (var (s, v) in constants) { stats[(uint)s + 1] = v; constant.Add((uint)s + 1); }
        }
        int gesspl = PickLevel(t.StatSetsPerLevel, e.StatSet, level);
        // A per-level stat that is also a constant adds to it (Righteous Fire: radius 18 + 0 at level 1, + 5 at level 20).
        if (gesspl >= 0) foreach (var (s, v) in t.StatSetLevelStats[gesspl]) stats[(uint)s + 1] = (constant.Contains((uint)s + 1) ? stats[(uint)s + 1] : 0) + v;
        int gepl = PickLevel(t.PerLevel, effectRow, level);
        if (e.CastTime > 0)
        {
            Put("base_spell_cast_time_ms", e.CastTime);
            // what the client reads when it starts the cast (traced live 2026-10-18; 0 = an instant cast)
            Put("virtual_base_spell_cast_time_ms", e.CastTime);
            Put("hundred_times_casts_per_second", (int)Math.Round(100_000.0 / e.CastTime));
        }
        if (gepl >= 0 && t.ManaCost[gepl] > 0) { Put("base_mana_cost", t.ManaCost[gepl]); Put("mana_cost", t.ManaCost[gepl]); }
        // A skill with a cooldown (GEPL Cooldown, e.g. Haste's 1200 ms): the client keeps the cooldown only when the skill
        // has uses (virtual_total_cooldown_count, GEPL StoredUses) and a duration in its record (GrantedSkill.CooldownMs).
        // An instant skill (ActiveSkillType Instant: Haste, Righteous Fire) is one by its stat: the client reads
        // skill_is_instant (0x2064), not the skill type. Without it the skill's action waited for a cast that never came,
        // stayed the player's current action and held back every other skill until a move (Frida, live 2026-10-21).
        if (IsInstant(effectRow))
        {
            Put("base_skill_is_instant", 1);
            Put("skill_is_instant", 1);
        }
        if (IsAura(effectRow)) stats[OwnAuraRestriction.BaseAura] = 1;
        if (_curseEffects.Value.Contains(effectRow)) stats[OwnAuraRestriction.Curse] = 1;
        if (OwnAuraRestriction.IsAura(stats)) stats[OwnAuraRestriction.Aura] = 1;
        // CharacterPanelStats gates spell rows on this final flag and hides the weapon rows while it is set.
        // A non-attack is not necessarily a spell (warcries, for example); use the actual ActiveSkillType.
        Put("casting_spell", IsSpell(effectRow) && !IsAttack(effectRow) ? 1 : 0);
        // ActiveSkillType Brand's FlagStat is skill_is_brand_skill. It belongs to this selected skill, not the actor.
        Put("skill_is_brand_skill", IsBrand(effectRow) ? 1 : 0);
        if (gepl >= 0 && t.Cooldowns[gepl] is { Ms: > 0, Uses: > 0 } cooldown)
        {
            Put("base_cooldown_count", cooldown.Uses);
            Put("virtual_total_cooldown_count", cooldown.Uses);
        }
        if (quality > 0 && t.Quality.TryGetValue(effectRow, out var perQuality))
            foreach (var (s, permille) in perQuality) stats[(uint)s + 1] = stats.GetValueOrDefault((uint)s + 1) + permille * quality / 1000;
        return [.. stats.Select(kv => (kv.Key, kv.Value))];

        void Put(string name, int value) { if (_stats.Key(name) is uint k) stats[k] = value; }
    }

    /// <summary>
    /// A skill's base hit damage by type, from its per-level stats (<c>spell_minimum_base_fire_damage</c> and so on).
    /// The packet keeps the types separate so conversion and mitigation can be applied per type. Added damage and
    /// increased/more modifiers are applied by the caller; this is the skill's own base range.
    /// </summary>
    public DamagePacket HitDamage(int effectRow, int level, int quality = 0) => HitDamageOf(effectRow, level, quality, "spell");

    /// <summary>A secondary-damage skill's own base hit (Bear Trap's <c>secondary_minimum/maximum_base_physical_damage</c>,
    /// Herald of Ice's shatter): neither an attack nor a spell, so no added attack/spell damage joins it.</summary>
    public DamagePacket SecondaryHitDamage(int effectRow, int level, int quality = 0) => HitDamageOf(effectRow, level, quality, "secondary");

    private DamagePacket HitDamageOf(int effectRow, int level, int quality, string prefix)
    {
        var stats = EffectStats(effectRow, level, quality).ToDictionary(kv => kv.Item1, kv => kv.Item2);
        var result = DamagePacket.Empty;
        foreach (var type in DamageTypes.ConversionOrder)
        {
            string slug = type switch
            {
                DamageType.Physical => "physical",
                DamageType.Lightning => "lightning",
                DamageType.Cold => "cold",
                DamageType.Fire => "fire",
                DamageType.Chaos => "chaos",
                _ => throw new ArgumentOutOfRangeException(),
            };
            int min = prefix == "spell" ? Value(stats, $"spell_minimum_base_{slug}_damage", $"minimum_base_{slug}_damage")
                : Value(stats, $"{prefix}_minimum_base_{slug}_damage");
            int max = prefix == "spell" ? Value(stats, $"spell_maximum_base_{slug}_damage", $"maximum_base_{slug}_damage")
                : Value(stats, $"{prefix}_maximum_base_{slug}_damage");
            if (min > 0 || max > 0) result = result.Add(type, new DamageRange(min, Math.Max(min, max)));
        }
        return result;
    }

    private int Value(Dictionary<uint, int> stats, params string[] names)
    {
        foreach (string name in names)
            if (_stats.Key(name) is uint key && stats.TryGetValue(key, out int value)) return value;
        return 0;
    }

    /// <summary>How far a gem's stored experience may level it (GrantedEffectStatSetsPerLevel PlayerLevelReq): its level
    /// stalls at the first one the character's level does not meet, as the game's does. The client derives the level from
    /// the experience alone (it does not stall), so the server stops the experience below that level's threshold
    /// (Program.GainGemExperience). 1 when not even the first level's requirement is met (the character cannot use the
    /// gem), 40 when the skill has none.</summary>
    public int MaxLevelAt(int effectRow, int charLevel)
    {
        if (_tables.Value is not { } t || effectRow < 0 || effectRow >= t.StatSets.Count) return 40;
        int statSet = t.StatSets[effectRow];
        int max = 0;
        foreach (var (level, req) in t.LevelReqsOf(statSet))
            if (req <= charLevel && level > max) max = level;
        return max > 0 ? max : 1;
    }

    /// <summary>The character level a gem needs at this level (GrantedEffectStatSetsPerLevel PlayerLevelReq): the
    /// highest requirement of its levels at or below it, 0 when the skill has none.</summary>
    public int PlayerLevelReq(int effectRow, int level)
    {
        if (_tables.Value is not { } t || effectRow < 0 || effectRow >= t.StatSets.Count) return 0;
        int statSet = t.StatSets[effectRow], req = 0;
        foreach (var (lvl, r) in t.LevelReqsOf(statSet))
            if (lvl <= level && r > req) req = r;
        return req;
    }

    /// <summary>
    /// A skill's resolved damage effectiveness in permyriad. The exported value is an offset from 100%:
    /// Kinetic Blast's 4000 means 14000 (140%), and Cyclone's -1840 means 8160 (81.6%). Missing data returns 0
    /// for the callers' identity fallback. A spell's own base damage is already final (see SpellDamage).
    /// </summary>
    public int DamageEffectiveness(int effectRow, int level)
    {
        if (_tables.Value is not { } t || effectRow < 0 || effectRow >= t.StatSets.Count) return 0;
        int row = PickLevel(t.StatSetsPerLevel, t.StatSets[effectRow], level);
        return row >= 0 && row < t.Effectiveness.Count ? Math.Max(0, t.Effectiveness[row]) : 0;
    }

    /// <summary>The inherent weapon attack-rate multiplier at the selected GEPL level, in percent.
    /// It multiplies weapon rate separately from player increased speed; absent data keeps ordinary weapon timing.</summary>
    public int AttackSpeedMultiplier(int effectRow, int level) =>
        effectRow >= 0 && level > 0 && _tables.Value is { } t
            && PickLevel(t.PerLevel, effectRow, level) is int row and >= 0 ? t.AttackSpeedMultipliers[row] : 0;

    /// <summary>The effect's primary stat set's base spell critical chance at this gem level, in permyriad.
    /// Null means missing/invalid data, distinct from a known zero (a spell with no inherent critical chance).</summary>
    public int? SpellCritChance(int effectRow, int level)
    {
        if (_tables.Value is not { } t || effectRow < 0 || effectRow >= t.StatSets.Count || level < 1) return null;
        int set = t.StatSets[effectRow];
        if (set < 0) return null;
        int row = PickLevel(t.StatSetsPerLevel, set, level);
        return row >= 0 ? t.SpellCritChances[row] : null;
    }

    /// <summary>A gem's requirement for one attribute at a level (0x14246fc40): the level requirement of that level
    /// (PlayerLevelReq) through the client's curve (3 x requirement + 11), the gem's percentage share (SkillGems
    /// Strength/Dexterity/IntelligenceRequirementPercent) raised to the power 0.9, and 0.5 for a support or 0.7 for a
    /// skill, truncated. Below 14 the client hides it and the requirement is 0. Anchors from the client's tooltips:
    /// 100% at requirement level 70 is 155 (Haste, Fireball), 75% is 119 (Clarity), 60%/40% are 98/68 (Anger), and a
    /// support at 8 is 18 (Added Fire Damage Support).</summary>
    public static int GemAttributeRequirement(int percent, int playerLevelReq, bool support)
    {
        if (percent <= 0) return 0;
        int requirement = (int)((3 * playerLevelReq + 11) * MathF.Pow(percent / 100f, 0.9f) * (support ? 0.5f : 0.7f) + 0.5f);
        return requirement < 14 ? 0 : requirement;
    }

    /// <summary>The ActionTypes Id of a basic action's skill id (HASH16 &amp; 0x3fff: Move 0x2909), or null.</summary>
    public string? ActionName(ushort skillId) => _actions.Value.FirstOrDefault(a => a.Id == skillId).Name;

    /// <summary>The full ActionTypes HASH16 for a GrantedEffects row, used with its server skill id by skill buffs.</summary>
    public ushort? ActionHash(int effectRow) => _effectActions.Value.TryGetValue(effectRow, out ushort hash) ? hash : null;

    private readonly Lazy<Dictionary<int, ushort>> _effectActions = new(() =>
    {
        string PathOf(string name) => Path.Combine(folder, "tables", name + ".json");
        if (!File.Exists(PathOf("GrantedEffects")) || !File.Exists(PathOf("ActiveSkills")) || !File.Exists(PathOf("ActionTypes"))) return [];
        using var effects = JsonDocument.Parse(File.ReadAllBytes(PathOf("GrantedEffects")));
        using var active = JsonDocument.Parse(File.ReadAllBytes(PathOf("ActiveSkills")));
        using var actions = JsonDocument.Parse(File.ReadAllBytes(PathOf("ActionTypes")));
        var result = new Dictionary<int, ushort>();
        int row = 0;
        foreach (var effect in effects.RootElement.EnumerateArray())
        {
            int index = row++;
            if (!effect.TryGetProperty("ActiveSkill", out var a) || a.ValueKind != JsonValueKind.Number
                || a.GetInt32() < 0 || a.GetInt32() >= active.RootElement.GetArrayLength()) continue;
            var entry = active.RootElement[a.GetInt32()];
            if (!entry.TryGetProperty("ActionType", out var t) || t.ValueKind != JsonValueKind.Number
                || t.GetInt32() < 0 || t.GetInt32() >= actions.RootElement.GetArrayLength()) continue;
            result[index] = actions.RootElement[t.GetInt32()].GetProperty("HASH16").GetUInt16();
        }
        return result;
    });

    /// <summary>A basic action's skill id by ActionTypes Id ("Move" → 0x2909), or null.</summary>
    public ushort? ActionSkillId(string name) => _actions.Value.FirstOrDefault(a => a.Name == name) is { Name: not null } a ? a.Id : null;

    private readonly Lazy<List<(ushort Id, string Name)>> _actions = new(() =>
    {
        string path = Path.Combine(folder, "tables", "ActionTypes.json");
        if (!File.Exists(path)) return [(0x2909, "Move")];
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        return [.. d.RootElement.EnumerateArray().Select(r => ((ushort)(r.GetProperty("HASH16").GetInt32() & 0x3fff), r.GetProperty("Id").GetString() ?? ""))];
    });

    /// <summary>A GrantedEffects row's Id, or null.</summary>
    public string? EffectId(int effectRow) =>
        _tables.Value?.EffectRows.FirstOrDefault(kv => kv.Value.Row == effectRow).Key;

    /// <summary>A GrantedEffects row by its Id, or -1.</summary>
    public int EffectRow(string effectId) =>
        _tables.Value is { } t && t.EffectRows.TryGetValue(effectId, out var effect)
            ? _modeEffects.Value.GetValueOrDefault(effect.Row, effect.Row) : -1;

    /// <summary>
    /// The mana a skill reserves at a level: GrantedEffectsPerLevel ManaReservationFlat (a flat amount) and
    /// ManaReservationPercent (permyriad: 5000 = 50%). Null when the skill reserves nothing.
    /// </summary>
    public (int Flat, int Percent)? Reservation(int effectRow, int level)
    {
        if (_tables.Value is not { } t) return null;
        int row = PickLevel(t.PerLevel, effectRow, level);
        if (row < 0 || row >= t.Reservations.Count) return null;
        var r = t.Reservations[row];
        return r.ManaFlat == 0 && r.ManaPercent == 0 ? null : (r.ManaFlat, r.ManaPercent);
    }

    /// <summary>The Vaal souls a use of a skill needs at a level (GrantedEffectsPerLevel VaalSouls; 0 for skills that are
    /// not Vaal skills).</summary>
    public int VaalSouls(int effectRow, int level) =>
        _tables.Value is { } t && PickLevel(t.PerLevel, effectRow, level) is int row and >= 0 ? Math.Max(0, t.VaalSouls[row]) : 0;

    public readonly record struct VaalDefinition(uint ActiveSkillsRow, int BaseCost, int StoredUses);

    /// <summary>Null for non-Vaal skills or missing/invalid catalog references. Never substitutes GE/GEPL identity.</summary>
    public VaalDefinition? VaalBudget(int effectRow, int level)
    {
        if (_tables.Value is not { } t || effectRow < 0 || effectRow >= t.ActiveSkills.Count
            || t.ActiveSkills[effectRow] is not int active || active < 0) return null;
        int row = PickLevel(t.PerLevel, effectRow, level);
        return row >= 0 && t.VaalSouls[row] >= 0 && t.VaalStoredUses[row] > 0
            ? new((uint)active, t.VaalSouls[row], t.VaalStoredUses[row]) : null;
    }

    /// <summary>The mana a GrantedEffects Id reserves at a level, or null.</summary>
    public (int Flat, int Percent)? Reservation(string effectId, int level) =>
        EffectRow(effectId) is int row and >= 0 ? Reservation(row, level) : null;

    /// <summary>A GrantedEffects row's CastTime in ms (0 when unknown).</summary>
    public int CastMs(int effectRow) =>
        _tables.Value is { } t && effectRow >= 0 && effectRow < t.Effects.Count ? t.Effects[effectRow].CastTime : 0;

    /// <summary>A GrantedEffects row's cooldown at a level in ms (GEPL Cooldown: Haste 1200, Righteous Fire 300), 0 when
    /// it has none.</summary>
    public int CooldownMs(int effectRow, int level) =>
        _tables.Value is { } t && PickLevel(t.PerLevel, effectRow, level) is int gepl and >= 0 ? t.Cooldowns[gepl].Ms : 0;

    public int CooldownUses(int effectRow, int level) =>
        _tables.Value is { } t && PickLevel(t.PerLevel, effectRow, level) is int row and >= 0 ? t.Cooldowns[row].Uses : 0;

    public bool CooldownPausesWhileActive(int effectRow) =>
        _tables.Value is { } t && effectRow >= 0 && effectRow < t.Effects.Count && t.Effects[effectRow].CooldownPauses;

    /// <summary>Whether a GrantedEffects row's skill is instant: its ActiveSkills row has the ActiveSkillType Instant (Haste,
    /// Righteous Fire). An instant skill is used while moving, without an action of its own.</summary>
    public bool IsInstant(int effectRow) => _instantEffects.Value.Contains(effectRow);

    /// <summary>A movement skill (ActiveSkillType Movement: Leap Slam, Whirling Blades, Flame Dash …): the player ends
    /// where it was aimed.</summary>
    public bool IsMovement(int effectRow) => _movementEffects.Value.Contains(effectRow);

    /// <summary>Whether a GrantedEffects row's skill is an attack (ActiveSkillType Attack: the default attack, Holy
    /// Strike, Flicker Strike …): it swings with the weapon's attack time and its use is paced like the default
    /// attack's, unlike a spell, which the client paces by its cast time.</summary>
    public bool IsAttack(int effectRow) => _attackEffects.Value.Contains(effectRow);

    /// <summary>Whether a GrantedEffects row's skill classifies Melee (the ActiveSkillType). Decides whether an attack
    /// gem's hit takes melee_damage_+% (the default attack does by its weapon profile instead).</summary>
    public bool IsMelee(int effectRow) => _meleeEffects.Value.Contains(effectRow);

    public bool IsSpell(int effectRow) => _spellEffects.Value.Contains(effectRow);
    public bool IsProjectile(int effectRow) => _projectileEffects.Value.Contains(effectRow);
    // Generic monster Melee also carries Projectile. Actual melee classification takes precedence.
    public bool IsRangedAttack(int effectRow) => IsAttack(effectRow) && IsProjectile(effectRow) && !IsMelee(effectRow);

    /// <summary>Classify the gem's base-item row from GemTags/SkillGems metadata, not its display name.</summary>
    public bool FamilyStatRaises(string stat, int gemRow) =>
        _stats.Key(stat) is uint key && _families.Value.Raises((int)key - 1, gemRow);

    public bool IsExceptional(int gemRow) => _families.Value.IsExceptional(gemRow);

    readonly Lazy<SkillGemFamilies> _families = new(() => new(folder));

    /// <summary>The ItemClasses rows a GrantedEffects row's skill can be used with (its ActiveSkill's
    /// WeaponRestriction_ItemClassesKeys: Burning Arrow 13, Bow); empty for a skill any hands can use. The client greys
    /// out a skill when neither hand fits (11-skills.md §10a).</summary>
    public IReadOnlySet<int> WeaponRestrictions(int effectRow) =>
        _weaponRestrictions.Value.TryGetValue(effectRow, out var rows) ? rows : FrozenSet<int>.Empty;

    private readonly Lazy<Dictionary<int, IReadOnlySet<int>>> _weaponRestrictions = new(() =>
    {
        string TablePath(string name) => Path.Combine(folder, "tables", name + ".json");
        if (!File.Exists(TablePath("GrantedEffects")) || !File.Exists(TablePath("ActiveSkills"))) return [];
        using var active = JsonDocument.Parse(File.ReadAllBytes(TablePath("ActiveSkills")));
        var restricted = active.RootElement.EnumerateArray().Select(r =>
            r.TryGetProperty("WeaponRestriction_ItemClassesKeys", out var k) && k.ValueKind == JsonValueKind.Array
                ? k.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Number).Select(v => v.GetInt32()).ToFrozenSet()
                : FrozenSet<int>.Empty).ToList();
        using var effects = JsonDocument.Parse(File.ReadAllBytes(TablePath("GrantedEffects")));
        var result = new Dictionary<int, IReadOnlySet<int>>();
        foreach (var (r, i) in effects.RootElement.EnumerateArray().Select((r, i) => (r, i)))
            if (r.TryGetProperty("ActiveSkill", out var a) && a.ValueKind == JsonValueKind.Number
                && a.GetInt32() is int row && row >= 0 && row < restricted.Count && restricted[row].Count > 0)
                result[i] = restricted[row];
        return result;
    });

    /// <summary>Whether a GrantedEffects row's ActiveSkill has the Warcry type.</summary>
    public bool IsWarcry(int effectRow) => _warcryEffects.Value.Contains(effectRow);

    /// <summary>Aura metadata, including only supports already accepted by GemSupports.Resolve.</summary>
    public bool IsAura(int effectRow, IEnumerable<int>? acceptedSupports = null) =>
        _auraEffects.Value.Contains(effectRow)
        || acceptedSupports?.Any(row => _addedAuraEffects.Value.Contains(row)) == true;
    /// <summary>The native Brand skill type, independently of display name or gem tags.</summary>
    public bool IsBrand(int effectRow) => _brandEffects.Value.Contains(effectRow);

    private readonly Lazy<HashSet<int>> _instantEffects = new(() => EffectsOfType(folder, "Instant"));
    private readonly Lazy<HashSet<int>> _movementEffects = new(() => EffectsOfType(folder, "Movement"));
    private readonly Lazy<HashSet<int>> _attackEffects = new(() => EffectsOfType(folder, "Attack"));
    private readonly Lazy<HashSet<int>> _meleeEffects = new(() => EffectsOfType(folder, "Melee"));
    private readonly Lazy<HashSet<int>> _spellEffects = new(() => EffectsOfType(folder, "Spell"));
    private readonly Lazy<HashSet<int>> _projectileEffects = new(() => EffectsOfType(folder, "Projectile"));
    private readonly Lazy<HashSet<int>> _warcryEffects = new(() => EffectsOfType(folder, "Warcry"));
    private readonly Lazy<HashSet<int>> _auraEffects = new(() => EffectsOfType(folder, "Aura"));
    private readonly Lazy<HashSet<int>> _curseEffects = new(() => EffectsOfType(folder, "AppliesCurse"));
    private readonly Lazy<HashSet<int>> _addedAuraEffects = new(() => EffectsOfType(folder, "Aura", added: true));
    private readonly Lazy<HashSet<int>> _brandEffects = new(() => EffectsOfType(folder, "Brand"));

    /// <summary>The GrantedEffects rows whose ActiveSkill has the ActiveSkillType of this Id.</summary>
    static HashSet<int> EffectsOfType(string folder, string typeId, bool added = false)
    {
        string TablePath(string name) => Path.Combine(folder, "tables", name + ".json");
        if (!File.Exists(TablePath("GrantedEffects")) || !File.Exists(TablePath("ActiveSkills")) || !File.Exists(TablePath("ActiveSkillType"))) return [];
        using var types = JsonDocument.Parse(File.ReadAllBytes(TablePath("ActiveSkillType")));
        int instant = types.RootElement.EnumerateArray().Select((r, i) => (Id: r.GetProperty("Id").GetString(), i))
            .FirstOrDefault(t => t.Id == typeId, (null, -1)).i;
        if (instant < 0) return [];
        if (added)
        {
            using var granted = JsonDocument.Parse(File.ReadAllBytes(TablePath("GrantedEffects")));
            return [.. granted.RootElement.EnumerateArray().Select((r, i) => (r, i))
                .Where(e => e.r.TryGetProperty("AddedActiveSkillTypes", out var a) && a.ValueKind == JsonValueKind.Array
                    && a.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.Number && v.GetInt32() == instant))
                .Select(e => e.i)];
        }
        using var active = JsonDocument.Parse(File.ReadAllBytes(TablePath("ActiveSkills")));
        var instantSkills = active.RootElement.EnumerateArray().Select((r, i) => (r, i))
            .Where(a => a.r.TryGetProperty("ActiveSkillTypes", out var t) && t.ValueKind == JsonValueKind.Array
                && t.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.Number && v.GetInt32() == instant))
            .Select(a => a.i).ToHashSet();
        using var effects = JsonDocument.Parse(File.ReadAllBytes(TablePath("GrantedEffects")));
        return [.. effects.RootElement.EnumerateArray().Select((r, i) => (r, i))
            .Where(e => e.r.TryGetProperty("ActiveSkill", out var a) && a.ValueKind == JsonValueKind.Number && instantSkills.Contains(a.GetInt32()))
            .Select(e => e.i)];
    }

    /// <summary>A weapon base's WeaponTypes row by its BaseItemTypes row: attack time (ms), physical damage, range.</summary>
    public (int AttackMs, int Min, int Max, int Range)? WeaponBase(int baseRow) => _weaponTypes.Value.TryGetValue(baseRow, out var w) ? w : null;

    private readonly Lazy<Dictionary<int, (int, int, int, int)>> _weaponTypes = new(() =>
    {
        string path = Path.Combine(folder, "tables", "WeaponTypes.json");
        var result = new Dictionary<int, (int, int, int, int)>();
        if (!File.Exists(path)) return result;
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var r in d.RootElement.EnumerateArray())
            result.TryAdd(r.GetProperty("BaseItemTypesKey").GetInt32(), (r.GetProperty("Speed").GetInt32(), r.GetProperty("DamageMin").GetInt32(),
                r.GetProperty("DamageMax").GetInt32(), r.GetProperty("RangeMax").GetInt32()));
        return result;
    });

    /// <summary>A projectile type for an effect: the Projectiles.dat row whose Id is Metadata/Projectiles/(GrantedEffects
    /// Id), e.g. Fireball → Metadata/Projectiles/Fireball at speed 1040, or null. The real link between a skill and its
    /// projectile is not known yet; the Gateway matches the Id, and <see cref="ProjectileFallbacks"/> covers the gems
    /// whose row is not named after their effect. Kinetic Blast (ClusterBurst) fires the wand attack's own projectile
    /// (3.29 has no ClusterBurst row); its explosion is a separate impact art (Program.Combat).</summary>
    public (string Id, int Speed)? ProjectileFor(int effectRow)
    {
        if (_tables.Value is not { } t) return null;
        string? effect = t.EffectRows.FirstOrDefault(kv => kv.Value.Row == effectRow).Key;
        if (effect is null) return null;
        string id = ProjectileFallbacks.GetValueOrDefault(effect, "Metadata/Projectiles/" + effect);
        return _projectiles.Value.TryGetValue(id, out int speed) ? (id, speed) : null;
    }

    // Granted effects whose Projectiles.dat row is not named after them, by effect Id. Kinetic Blast's effect is
    // ClusterBurst (the skill data names no row); it uses the wand attack's projectile at the same 3260 speed. Tornado
    // Shot's arrow is TornadoShotArrow (2173); without it every use was refused while the client had stopped to shoot,
    // and the server walked on (live 2026-10-29).
    static readonly Dictionary<string, string> ProjectileFallbacks = new()
    {
        ["KineticBlast"] = "Metadata/Projectiles/WandProjectile",
        ["KineticBlastAltX"] = "Metadata/Projectiles/WandProjectile",
        ["KineticBlastAltY"] = "Metadata/Projectiles/WandProjectile",
        ["TornadoShot"] = "Metadata/Projectiles/TornadoShotArrow",
        ["TornadoShotAltX"] = "Metadata/Projectiles/TornadoShotArrow",
        ["TornadoShotAltY"] = "Metadata/Projectiles/TornadoShotArrow",
    };

    /// <summary>A Projectiles.dat row by its Id (<c>Metadata/Projectiles/DefaultArrow</c> for a bow's ordinary attack).</summary>
    public (string Id, int Speed)? Projectile(string id) =>
        _projectiles.Value.TryGetValue(id, out int speed) ? (id, speed) : null;

    private readonly Lazy<Dictionary<string, int>> _projectiles = new(() =>
    {
        string path = Path.Combine(folder, "tables", "Projectiles.json");
        var result = new Dictionary<string, int>();
        if (!File.Exists(path)) return result;
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var r in d.RootElement.EnumerateArray())
            if (r.GetProperty("Id").GetString() is { } id)
                result.TryAdd(id, r.GetProperty("ProjectileSpeed") is { ValueKind: JsonValueKind.Number } v ? v.GetInt32() : 0);
        return result;
    });

    /// <summary>A data file's JSON, or null, with one warning, when it is missing or unreadable: the feature stays off
    /// rather than faulting the projectile pump or a spawn, which closes the connection, and the cached fault would repeat
    /// it on every later one (review 2026-10-30). The generators write atomically, so a truncated file means a crash
    /// elsewhere.</summary>
    static JsonDocument? DataFile(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonDocument.Parse(File.ReadAllBytes(path)); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[gateway] WARNING: {path} is unreadable ({ex.GetType().Name}: {ex.Message}); its feature is off until"
                + " scripts/dev/extract-data.ps1 regenerates it");
            return null;
        }
    }

    /// <summary>One clip of a projectile type's rig: its name, index in the rig's animation list (what 0x1b2 plays), length
    /// and whether it plays once (data/projectile-clips.json, scripts/re/projectile_clips.py).</summary>
    public sealed record ProjectileClip(string Name, uint Index, int Ms, bool Once);

    /// <summary>What the client needs to show a server-created object of a projectile type: the .ao's default animation,
    /// if it declares one (the wand bolt's <c>projectile_01</c>: it flies by itself), the flight clip (Projectiles.dat
    /// LoopAnimationIds, the Sand Spitter mortar's <c>proj</c>: the object is invisible until it plays) and the impact
    /// clip (ImpactAnimationIds, <c>end</c>).</summary>
    public sealed record ProjectileClips(string? DefaultAnimation, ProjectileClip? Flight, ProjectileClip? Impact);

    public ProjectileClips? Clips(string projectileId) =>
        _projectileClips.Value.TryGetValue(projectileId, out var clips) ? clips : null;

    private readonly Lazy<Dictionary<string, ProjectileClips>> _projectileClips = new(() =>
    {
        string path = Path.Combine(folder, "projectile-clips.json");
        var result = new Dictionary<string, ProjectileClips>();
        using var d = DataFile(path);
        if (d is null) return result;
        foreach (var entry in d.RootElement.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object) continue;
            ProjectileClip? Clip(string key) =>
                entry.Value.TryGetProperty(key, out var c) && c.ValueKind == JsonValueKind.Object
                    && c.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    && c.TryGetProperty("index", out var i) && i.ValueKind == JsonValueKind.Number && i.TryGetUInt32(out uint index)
                    && c.TryGetProperty("ms", out var ms) && ms.ValueKind == JsonValueKind.Number && ms.TryGetInt32(out int length)
                    ? new(n.GetString() ?? "", index, length, c.TryGetProperty("once", out var once) && once.ValueKind == JsonValueKind.True)
                    : null;
            string? fallback = entry.Value.TryGetProperty("default", out var def) && def.ValueKind == JsonValueKind.String ? def.GetString() : null;
            result[entry.Name] = new(fallback, Clip("flight"), Clip("impact"));
        }
        return result;
    });

    /// <summary>A monster skill's projectile (Projectiles.dat Id, speed in world units per second, whether it is lobbed at
    /// the aim, collision radius in cells). Speed is the row's ProjectileSpeed; <c>Lobbed</c> flights land where they were
    /// aimed and damage an area there.</summary>
    public sealed record MonsterProjectileInfo(string Metadata, int Speed, bool Lobbed, double CollisionRadius)
    {
        /// <summary>The Animation.dat Id of the clips the client plays for the skill (MonsterMortar.Animation: the Sand
        /// Spitter mortar's <c>SpellProjectile</c>, row 20), the key of the variety's ACT pool; null when the row names none.</summary>
        public string? AnimationKey { get; init; }
        /// <summary>The skill's <c>projectile_minimum_range</c> in world units (the Spitter's 50): a lob lands no closer
        /// to the monster than this. 0 without the stat.</summary>
        public int MinimumRange { get; init; }
    }

    /// <summary>One clip of a monster's attack pool (data/monster-contacts.json, scripts/re/monster_contacts.py): its name,
    /// its index in the rig's animation list, its length, and its contact keyframe's time and position when it has one
    /// (the Sand Spitter's attack1: 375 of 1000 ms, 45.9 units behind the model and 71.6 up).</summary>
    public sealed record MonsterClipContact(string Animation, uint Index, int LengthMs, int? ContactMs, ContactPoint? Point)
    {
        /// <summary>The contact as a fraction of the clip, clamped to it: an .ao event time is independent of the rig's
        /// clip length, and a release past the attack's end would miss its busy period.</summary>
        public double? ContactFraction => ContactMs is int ms && LengthMs > 0 ? Math.Clamp((double)ms / LengthMs, 0, 1) : null;
    }

    /// <summary>The clip the client plays for a monster's action: the variety's ACT pool for the skill's animation key
    /// (<c>SpellProjectile</c> for the Spitter's mortar), chosen as the client chooses it, with the native selector seeded
    /// by the action's sequence (AnimationVariantSelector; the Gateway's monster actions send 0xffff, so the choice is the
    /// same for every swing). Null without data for the variety or key.</summary>
    public MonsterClipContact? MonsterContact(string varietyId, string animationKey, ushort sequence)
    {
        if (!_monsterContacts.Value.TryGetValue(varietyId, out var pools) || !pools.TryGetValue(animationKey, out var pool)
            || pool.Length == 0) return null;
        // Equal weights for every clip; the pool's size comes from a data file, so only a small one lives on the stack.
        Span<uint> weights = pool.Length <= 32 ? stackalloc uint[pool.Length] : new uint[pool.Length];
        weights.Fill(1);
        return pool[AnimationVariantSelector.Select(sequence, weights)];
    }

    private readonly Lazy<Dictionary<string, Dictionary<string, MonsterClipContact[]>>> _monsterContacts = new(() =>
    {
        string path = Path.Combine(folder, "monster-contacts.json");
        var result = new Dictionary<string, Dictionary<string, MonsterClipContact[]>>(StringComparer.Ordinal);
        using var d = DataFile(path);
        if (d is null) return result;
        foreach (var variety in d.RootElement.EnumerateObject())
        {
            if (variety.Value.ValueKind != JsonValueKind.Object
                || !variety.Value.TryGetProperty("pools", out var pools) || pools.ValueKind != JsonValueKind.Object) continue;
            var byKey = new Dictionary<string, MonsterClipContact[]>(StringComparer.Ordinal);
            foreach (var pool in pools.EnumerateObject())
            {
                if (pool.Value.ValueKind != JsonValueKind.Array) continue;
                var clips = new List<MonsterClipContact>();
                foreach (var c in pool.Value.EnumerateArray())
                {
                    if (c.ValueKind != JsonValueKind.Object
                        || !c.TryGetProperty("animation", out var name) || name.ValueKind != JsonValueKind.String
                        || !c.TryGetProperty("index", out var index) || index.ValueKind != JsonValueKind.Number || !index.TryGetUInt32(out uint clipIndex)
                        || !c.TryGetProperty("length", out var length) || length.ValueKind != JsonValueKind.Number || !length.TryGetInt32(out int lengthMs)
                        || lengthMs <= 0) continue;
                    int? contact = c.TryGetProperty("contact", out var ms) && ms.ValueKind == JsonValueKind.Number && ms.TryGetInt32(out int contactMs)
                        ? contactMs : null;
                    var point = c.TryGetProperty("position", out var position) ? AnimationContacts.Point(position) : null;
                    clips.Add(new(name.GetString() ?? "", clipIndex, lengthMs, contact, point));
                }
                if (clips.Count > 0) byKey[pool.Name] = [.. clips];
            }
            if (byKey.Count > 0) result[variety.Name] = byKey;
        }
        return result;
    });

    /// <summary>The projectile of a monster's granted effect: for a mortar skill (ActiveSkills Id <c>monster_mortar*</c>)
    /// the GrantedEffects row's SkillID is the MonsterMortar row that names it (every Sand Spitter variant: SkillID 183 →
    /// MonsterMortar 183 → <c>Metadata/Projectiles/SandspitterMortarProjectile</c>, 1170). Weapon projectile attacks
    /// (<c>monster_projectile_weapon</c>, <c>monster_snipe</c>: SkillID 0 for all) take their projectile from the monster's
    /// weapon art, which is not resolved here: null, and such a monster keeps its instant contact.</summary>
    public MonsterProjectileInfo? MonsterProjectile(int effectRow) =>
        _monsterProjectiles.Value.TryGetValue(effectRow, out var projectile)
            ? projectile with { MinimumRange = MonsterProjectileMinimumRange(effectRow) } : null;

    /// <summary>The skill's <c>projectile_minimum_range</c> (a constant of its stat set, so level 1 reads it), 0 without.</summary>
    private int MonsterProjectileMinimumRange(int effectRow)
    {
        if (_stats.Key("projectile_minimum_range") is not uint key) return 0;
        foreach (var (statKey, value) in EffectStats(effectRow, 1)) if (statKey == key) return value;
        return 0;
    }

    private readonly Lazy<Dictionary<int, MonsterProjectileInfo>> _monsterProjectiles = new(() =>
    {
        string TablePath(string name) => Path.Combine(folder, "tables", name + ".json");
        var result = new Dictionary<int, MonsterProjectileInfo>();
        if (!File.Exists(TablePath("GrantedEffects")) || !File.Exists(TablePath("ActiveSkills"))
            || !File.Exists(TablePath("MonsterMortar")) || !File.Exists(TablePath("Projectiles"))) return result;
        using var projectiles = JsonDocument.Parse(File.ReadAllBytes(TablePath("Projectiles")));
        var rows = projectiles.RootElement.EnumerateArray().Select(r => (
            Id: r.TryGetProperty("Id", out var id) ? id.GetString() ?? "" : "",
            Speed: r.TryGetProperty("ProjectileSpeed", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0,
            Radius: r.TryGetProperty("CollisionRadius", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 1)).ToList();
        // Animation.dat Ids: a mortar row's Animation names the ACT key of the clips the client plays for the skill.
        var animationIds = new List<string>();
        if (File.Exists(TablePath("Animation")))
        {
            using var animations = JsonDocument.Parse(File.ReadAllBytes(TablePath("Animation")));
            foreach (var r in animations.RootElement.EnumerateArray())
                animationIds.Add(r.TryGetProperty("Id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() ?? "" : "");
        }
        using var mortars = JsonDocument.Parse(File.ReadAllBytes(TablePath("MonsterMortar")));
        var mortarRows = new Dictionary<int, (int Projectile, string? AnimationKey)>();
        foreach (var r in mortars.RootElement.EnumerateArray())
            if (r.TryGetProperty("Id", out var id) && id.ValueKind == JsonValueKind.Number
                && r.TryGetProperty("Projectile", out var p) && p.ValueKind == JsonValueKind.Number)
            {
                string? key = r.TryGetProperty("Animation", out var a) && a.ValueKind == JsonValueKind.Number
                    && a.GetInt32() >= 0 && a.GetInt32() < animationIds.Count && animationIds[a.GetInt32()].Length > 0
                    ? animationIds[a.GetInt32()] : null;
                mortarRows.TryAdd(id.GetInt32(), (p.GetInt32(), key));
            }
        using var active = JsonDocument.Parse(File.ReadAllBytes(TablePath("ActiveSkills")));
        var mortarSkills = active.RootElement.EnumerateArray().Select((r, i) => (r, i))
            .Where(a => a.r.TryGetProperty("Id", out var id) && id.GetString() is { } name
                && name.StartsWith("monster_mortar", StringComparison.Ordinal))
            .Select(a => a.i).ToHashSet();
        using var effects = JsonDocument.Parse(File.ReadAllBytes(TablePath("GrantedEffects")));
        int row = -1;
        foreach (var e in effects.RootElement.EnumerateArray())
        {
            row++;
            if (!e.TryGetProperty("ActiveSkill", out var a) || a.ValueKind != JsonValueKind.Number || !mortarSkills.Contains(a.GetInt32())) continue;
            if (!e.TryGetProperty("SkillID", out var k) || k.ValueKind != JsonValueKind.Number
                || !mortarRows.TryGetValue(k.GetInt32(), out var mortar) || mortar.Projectile < 0 || mortar.Projectile >= rows.Count) continue;
            var p = rows[mortar.Projectile];
            if (p.Id.Length == 0 || p.Speed <= 0) continue;
            result[row] = new(p.Id, p.Speed, Lobbed: true, Math.Max(1, p.Radius)) { AnimationKey = mortar.AnimationKey };
        }
        return result;
    });

    // The highest level at or below `level` among rows with this key, else the lowest; -1 when none.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<List<(int Key, int Level)>, Dictionary<int, int[]>> RowsByKey = new();

    private static int PickLevel(List<(int Key, int Level)> rows, int key, int level)
    {
        int best = -1, bestLevel = int.MinValue, low = -1, lowLevel = int.MaxValue;
        // The per-level tables are loaded once and never change: index their rows by key instead of scanning them all
        // (the combat paths pick levels many times per stat calculation). Same rows, same order, same answer.
        var index = RowsByKey.GetValue(rows, r =>
        {
            var byKey = new Dictionary<int, int[]>();
            foreach (var g in r.Select((row, i) => (row.Key, i)).GroupBy(x => x.Key)) byKey[g.Key] = [.. g.Select(x => x.i)];
            return byKey;
        });
        if (!index.TryGetValue(key, out var indices)) return -1;
        foreach (int i in indices)
        {
            int l = rows[i].Level;
            if (l <= level && l > bestLevel) (best, bestLevel) = (i, l);
            if (l < lowLevel) (low, lowLevel) = (i, l);
        }
        return best >= 0 ? best : low;
    }

    /// <summary>A GrantedEffects row's cast time in ms (CastTime; 0 unknown).</summary>
    public int CastTimeMs(int effectRow) =>
        _tables.Value is { } t && effectRow >= 0 && effectRow < t.Effects.Count ? t.Effects[effectRow].CastTime : 0;

    public GrantedSkill? Find(ushort id, string effect, int level, IReadOnlyList<(uint, int)>? stats = null)
    {
        if (_tables.Value is not { } t)
            return effect == "PlayerMelee" && level == 1 ? new GrantedSkill(id, 4474, 4430, stats) : null;
        int effectRow = EffectRow(effect);
        if (effectRow < 0 || effectRow >= t.StatSets.Count) return null;
        int gepl = t.PerLevel.FindIndex(r => r.Effect == effectRow && r.Level == level);
        int gesspl = t.StatSetsPerLevel.FindIndex(r => r.StatSet == t.StatSets[effectRow] && r.Level == level);
        return gepl < 0 || gesspl < 0 ? null : WithExtraSets(new GrantedSkill(id, (uint)gepl, (uint)gesspl, stats, (uint)t.Cooldowns[gepl].Ms), t, effectRow, level);
    }

    /// <summary>
    /// A GrantedEffects row at a level, for monsters: their effects have per-level rows only for some levels (Hungry
    /// Corpse's Melee only level 1), so the highest GEPL level at or below <paramref name="level"/> (else the lowest), and
    /// the effect's StatSet1 at that level the same way. Null without the tables or rows.
    /// </summary>
    public GrantedSkill? FindByRow(ushort id, int effectRow, int level, IReadOnlyList<(uint, int)>? stats = null)
    {
        if (_tables.Value is not { } t || effectRow < 0 || effectRow >= t.StatSets.Count || t.StatSets[effectRow] < 0) return null;
        int statSet = t.StatSets[effectRow];
        int gepl = PickLevel(t.PerLevel, effectRow, level), gesspl = PickLevel(t.StatSetsPerLevel, statSet, level);
        return gepl < 0 || gesspl < 0 ? null : WithExtraSets(new GrantedSkill(id, (uint)gepl, (uint)gesspl, stats, (uint)t.Cooldowns[gepl].Ms), t, effectRow, level);
    }

    /// <summary>The player's default skills: the default attack, with the class's unarmed weapon stats.</summary>
    public IReadOnlyList<GrantedSkill> PlayerDefaults(Unarmed weapon) => PlayerDefaults(weapon.Stats());

    /// <summary>The player's default skills with these stats.</summary>
    public IReadOnlyList<GrantedSkill> PlayerDefaults(IReadOnlyList<(uint, int)> stats)
    {
        string effect = Environment.GetEnvironmentVariable("Game_PLAYER_ATTACK") ?? "PlayerMelee";
        return Find(FirstSkillId, effect, 1, stats) is { } s ? [s] : [];
    }

    /// <summary>A class's unarmed "weapon" (Characters.dat WeaponSpeed, MinDamage, MaxDamage, MaxAttackDistance).</summary>
    public sealed record Unarmed(int AttackMs, int MinDamage, int MaxDamage, int Range)
    {
        /// <summary>The equipped item's local typed ranges. Class unarmed records retain their physical fallback.</summary>
        public DamagePacket? LocalDamage { get; init; }
        /// <summary>Raw local modifiers captured with this weapon, including penetration and impale chance.</summary>
        public IReadOnlyDictionary<string, int>? LocalSources { get; init; }
        public static readonly Unarmed Witch = new(833, 2, 5, 8);

        /// <summary>The weapon's own added elemental/chaos damage (its local added damage mods, ItemWeapons), added to
        /// every hit it makes; empty for unarmed and for weapons without such mods.</summary>
        public DamagePacket LocalAdded { get; init; } = DamagePacket.Empty;
        public bool WieldingWand { get; init; }
        public bool Melee { get; init; } = true;

        /// <summary>Unarmed attacks alternate hands, i.e. count as dual wielding, and the client adds dual wielding's 10%
        /// attack speed to the skill's main_hand_attack_speed_+% (live 2026-10-17: the skill read 10 that the server never
        /// sent, and the swings came every ~757 ms = 833 / 1.1).</summary>
        public const int DualWieldAttackSpeedPct = 10;

        /// <summary>
        /// The same attack's off-hand stats (off_hand_… instead of main_hand_…), for the weapon in the other hand of a
        /// dual-wielded attack. A dual-wielded attack swings the off hand too, and the client sets that strike's weapon
        /// up from these stats; it derives none of them (11-skills.md §4), so without them the off-hand weapon has no
        /// attack time, damage or reach at all. <paramref name="mainHandLocalAttackSpeedPct"/> is the main weapon's own
        /// local_attack_speed_+%, which the final main-hand value carries (<see cref="OffHandAttackSpeedPct"/>).
        /// </summary>
        public IReadOnlyList<(uint, int)> OffStats(IReadOnlyDictionary<uint, int>? player, DamagePacket? totalHit = null,
            int mainHandLocalAttackSpeedPct = 0)
        {
            var physical = (totalHit ?? HitDamage(player))[DamageType.Physical];
            var (min, max) = ((int)Math.Floor(physical.Min), (int)Math.Floor(physical.Max));
            return
            [
                (StatKeys.OffHandMinPhysical, min), (StatKeys.OffHandMaxPhysical, max),
                (StatKeys.OffHandMinTotal, totalHit is null ? min : (int)Math.Floor(totalHit.MinTotal)),
                (StatKeys.OffHandMaxTotal, totalHit is null ? max : (int)Math.Floor(totalHit.MaxTotal)),
                (StatKeys.OffHandAttackDurationMs, AttackMs), (StatKeys.OffHandTotalAttackDurationMs, AttackMs),
                (StatKeys.OffHandAttackSpeedPct, OffHandAttackSpeedPct(IncreasedAttackSpeed(player), mainHandLocalAttackSpeedPct)),
                (StatKeys.OffHandBaseMaxAttackDistance, Range), (StatKeys.OffHandMaxAttackDistance, Range),
            ];
        }

        /// <summary>The off hand's attack_speed_+%: the final main-hand value with the main weapon's local attack speed
        /// taken back out. CharacterStats multiplies that local value in like a more, but it belongs to the main weapon
        /// only; the off-hand weapon's own local attack speed is already in its <see cref="AttackMs"/>.</summary>
        public static int OffHandAttackSpeedPct(int mainHandPct, int mainHandLocalPct) =>
            Math.Max(-99, (int)Math.Round(((1 + mainHandPct / 100.0) / Math.Max(0.01, 1 + mainHandLocalPct / 100.0) - 1) * 100,
                MidpointRounding.AwayFromZero));

        /// <summary>The final main_hand_attack_speed_+% (passives and dual wielding, from CharacterStats), or the raw
        /// stats' sum when no final value was sent.</summary>
        public int IncreasedAttackSpeed(IReadOnlyDictionary<uint, int>? player)
        {
            // The server's stats carry the final value (passives, weapon conditions, dual wielding's 10% more).
            // The fallback is for callers that only have the raw stats.
            if (player?.TryGetValue(StatKeys.MainHandAttackSpeedPct, out int final) == true) return final;
            int increased = DualWieldAttackSpeedPct;
            foreach (uint k in StatKeys.AttackSpeedInputs)
                increased += player?.GetValueOrDefault(k) ?? 0;
            return increased;
        }

        /// <summary>The attack's total increased physical damage: the player's damage, attack, physical, melee
        /// and melee physical damage stats.</summary>
        public long IncreasedDamage(IReadOnlyDictionary<uint, int>? player)
        {
            long increased = 0;
            foreach (uint k in (uint[])[StatKeys.DamagePct, StatKeys.AttackDamagePct, StatKeys.PhysicalDamagePct])
                increased += player?.GetValueOrDefault(k) ?? 0;
            if (Melee) increased += (long)(player?.GetValueOrDefault(StatKeys.MeleeDamagePct) ?? 0)
                + (player?.GetValueOrDefault(StatKeys.MeleePhysicalDamagePct) ?? 0);
            return increased;
        }

        /// <summary>Local typed weapon damage (or class physical damage), with the actual attack's increase buckets.</summary>
        public DamagePacket HitDamage(IReadOnlyDictionary<uint, int>? player, bool melee = true, bool projectile = false)
        {
            // Arcane Might's wand-only grant follows the weapon held (WieldingWand); every local component takes the
            // transfer once, with the weapon's own melee context.
            int Get(string name) => Program.Stats.Key(name) is uint key ? player?.GetValueOrDefault(key) ?? 0 : 0;
            return (LocalDamage ?? DamagePacket.Of(DamageType.Physical, new(MinDamage, MaxDamage)).Add(LocalAdded)).Select((type, range) =>
                DamageStats.IncreaseAttackRange(range, DamageStats.AttackIncrease(Get, type, melee, projectile: projectile, wieldingWand: WieldingWand)));
        }

        /// <summary>The hit's damage range: Characters.dat MinDamage..MaxDamage × (1 + increased / 100), rounded down,
        /// at least 0.</summary>
        public (int Min, int Max) Damage(IReadOnlyDictionary<uint, int>? player)
        {
            var damage = HitDamage(player)[DamageType.Physical];
            return ((int)Math.Floor(damage.Min), (int)Math.Floor(damage.Max));
        }

        /// <summary>The attack time: base / (1 + increased / 100), as the client computes the swing from the skill's
        /// main_hand_attack_speed_+% (the final value already includes more multipliers), at least one server tick
        /// (<see cref="Program.TickSeconds"/>: at most 30 attacks per second).</summary>
        public double AttackSeconds(IReadOnlyDictionary<uint, int>? player) =>
            Math.Max(Program.TickSeconds, AttackMs / 1000.0 / Math.Max(0.01, 1 + IncreasedAttackSpeed(player) / 100.0));

        /// <summary>
        /// The skill's final main-hand stats (client keys = Stats.dat row + 1). The server computes them, as the real one
        /// does: the client derives nothing from attack_speed_+% (live 2026-10-17: a skill holding attack_speed_+% 2000
        /// still swung at ~757 ms and the character panel kept showing 1.2). The swing follows main_hand_attack_speed_+%
        /// (the client puts only dual wielding's 10 there when it is not sent), the panel's "Attacks per Second" is
        /// hundred_times_attacks_per_second / 100.
        /// </summary>
        public IReadOnlyList<(uint, int)> Stats(IReadOnlyDictionary<uint, int>? player = null, DamagePacket? totalHit = null,
            double damageFactor = 1)
        {
            var physical = (totalHit ?? HitDamage(player))[DamageType.Physical];
            var (min, max) = ((int)Math.Floor(physical.Min), (int)Math.Floor(physical.Max));
            double attacksPerSecond = 1 / AttackSeconds(player);
            var display = SpellDamage.Display((totalHit?.MinTotal ?? min, totalHit?.MaxTotal ?? max), attacksPerSecond, damageFactor);
            return
            [
                (StatKeys.MainHandMinPhysical, min), (StatKeys.MainHandMaxPhysical, max),
                (StatKeys.MainHandMinTotal, totalHit is null ? min : (int)Math.Floor(totalHit.MinTotal)),
                (StatKeys.MainHandMaxTotal, totalHit is null ? max : (int)Math.Floor(totalHit.MaxTotal)),
                (StatKeys.MainHandAttackDurationMs, AttackMs), (StatKeys.MainHandTotalAttackDurationMs, AttackMs),
                (StatKeys.MainHandAttackSpeedPct, IncreasedAttackSpeed(player)),
                (StatKeys.SkillUsesMainHand, 1),
                (StatKeys.HundredTimesAttacksPerSecond, (int)Math.Round(100 * attacksPerSecond)),
                // The character panel's DPS and Damage per Use rows read these; the client derives neither.
                (StatKeys.HundredTimesDamagePerSecond, display.PerSecond),
                (StatKeys.DisplayHundredTimesDamagePerSkillUse, display.PerUse),
                (StatKeys.MainHandBaseMaxAttackDistance, Range), (StatKeys.MainHandMaxAttackDistance, Range),
            ];
        }
    }

    /// <summary>A class from Characters.dat: IntegerId (what the Player component's class byte holds: Marauder 1, Witch 3),
    /// the base attributes, and the player object's type (the row's Id, e.g. Metadata/Characters/StrInt/StrInt for the
    /// Templar) and model (AOFile).</summary>
    public sealed record ClassInfo(int IntegerId, int Strength, int Dexterity, int Intelligence,
        string ObjectType = "Metadata/Characters/Int/Int", string AnimatedObject = "Metadata/Characters/Int/Int.ao",
        int BaseMaxLife = 38, int BaseMaxMana = 34)
    {
        public static readonly ClassInfo Witch = new(3, 14, 14, 32);
    }

    /// <summary>The Witch's Characters.dat row, used when a class is unknown.</summary>
    public const int WitchRow = 1;

    /// <summary>The Characters.dat row of a class by IntegerId (CharacterClass): Marauder 1 → row 0, Witch 3 → row 1,
    /// Templar 5 → row 6.</summary>
    public int RowOfClass(int integerId)
    {
        string path = Path.Combine(folder, "tables", "Characters.json");
        if (!File.Exists(path)) return WitchRow;
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        int i = 0;
        foreach (var r in d.RootElement.EnumerateArray())
        {
            if (r.GetProperty("IntegerId").GetInt32() == integerId) return i;
            i++;
        }
        return WitchRow;
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, ClassInfo> _classes = new();

    public ClassInfo ClassFor(int characterRow) => _classes.GetOrAdd(characterRow, row =>
    {
        string path = Path.Combine(folder, "tables", "Characters.json");
        if (!File.Exists(path)) return ClassInfo.Witch;
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        if (row < 0 || row >= d.RootElement.GetArrayLength()) return ClassInfo.Witch;
        var r = d.RootElement[row];
        return new(r.GetProperty("IntegerId").GetInt32(), r.GetProperty("BaseStrength").GetInt32(),
            r.GetProperty("BaseDexterity").GetInt32(), r.GetProperty("BaseIntelligence").GetInt32(),
            r.GetProperty("Id").GetString()!, r.GetProperty("AOFile").GetString()!,
            r.GetProperty("BaseMaxLife").GetInt32(), r.GetProperty("BaseMaxMana").GetInt32());
    });

    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Unarmed> _unarmed = new();

    public Unarmed UnarmedFor(int characterRow) => _unarmed.GetOrAdd(characterRow, LoadUnarmed);

    private Unarmed LoadUnarmed(int characterRow)
    {
        string path = Path.Combine(folder, "tables", "Characters.json");
        if (!File.Exists(path)) return Unarmed.Witch;
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        if (characterRow < 0 || characterRow >= d.RootElement.GetArrayLength()) return Unarmed.Witch;
        var r = d.RootElement[characterRow];
        return new Unarmed(r.GetProperty("WeaponSpeed").GetInt32(), r.GetProperty("MinDamage").GetInt32(),
            r.GetProperty("MaxDamage").GetInt32(), r.GetProperty("MaxAttackDistance").GetInt32());
    }

    // StatSets: each GrantedEffects row's StatSet1 (-1 when none). Effects: per GrantedEffects row its StatSet1 and CastTime.
    // StatSetBase: per GrantedEffectStatSets row its ImplicitStats and (ConstantStats, ConstantStatsValues).
    // StatSetLevelStats: per GESSPL row its (FloatStats, BaseResolvedValues) and (AdditionalStats, AdditionalStatsValues).
    // Effectiveness: resolved permyriad, 10000 + the GESSPL DamageEffectiveness offset; 0 for missing data.
    // ManaCost: per GEPL row the CostAmounts entry of CostTypes 0 (mana), else 0.
    // Reservations: per GEPL row the ManaReservationFlat and ManaReservationPercent (permyriad), parallel to PerLevel.
    // Cooldowns: per GEPL row its Cooldown (ms) and StoredUses, parallel to PerLevel. VaalSouls: per GEPL row its VaalSouls.
    // Quality: per GrantedEffects row its (quality stat, permille).
    private sealed record Tables(Dictionary<string, (int Row, int StatSet)> EffectRows, List<int> StatSets,
        List<(int Effect, int Level)> PerLevel, List<(int StatSet, int Level)> StatSetsPerLevel,
        List<(int StatSet, int CastTime, bool CooldownPauses)> Effects, List<(int[] Implicit, (int, int)[] Constant)> StatSetBase,
        List<(int, int)[]> StatSetLevelStats, List<(int StatSet, int GemLevel, int PlayerLevelReq)> GemLevelReqs, List<int> ManaCost,
        List<(int ManaFlat, int ManaPercent, int LifeFlat, int LifePercent)> Reservations, List<(int Ms, int Uses)> Cooldowns,
        List<int> VaalSouls, List<int> VaalStoredUses, List<int?> ActiveSkills,
        Dictionary<int, (int, int)[]> Quality, List<int> Effectiveness, List<int?> SpellCritChances, List<int> AttackSpeedMultipliers)
    {
        // GemLevelReqs by stat set: every gem's requirement check (twice per linked gem per skill build) scanned the whole
        // table before.
        Dictionary<int, (int GemLevel, int PlayerLevelReq)[]>? _levelReqsBySet;
        public (int GemLevel, int PlayerLevelReq)[] LevelReqsOf(int statSet) =>
            LazyInitializer.EnsureInitialized(ref _levelReqsBySet, () => GemLevelReqs.GroupBy(r => r.StatSet)
                .ToDictionary(g => g.Key, g => g.Select(r => (r.GemLevel, r.PlayerLevelReq)).ToArray()))
                .GetValueOrDefault(statSet, []);

        public static Tables? Load(string folder)
        {
            string Table(string name) => Path.Combine(folder, "tables", name + ".json");
            string ge = Table("GrantedEffects"), gepl = Table("GrantedEffectsPerLevel"), gesspl = Table("GrantedEffectStatSetsPerLevel"),
                gess = Table("GrantedEffectStatSets");
            if (!File.Exists(ge) || !File.Exists(gepl) || !File.Exists(gesspl)) return null;
            using var g = JsonDocument.Parse(File.ReadAllBytes(ge));
            using var p = JsonDocument.Parse(File.ReadAllBytes(gepl));
            using var s = JsonDocument.Parse(File.ReadAllBytes(gesspl));
            static int Int(JsonElement r, string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : -1;
            static int[] Ints(JsonElement r, string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
                ? [.. v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? (int)Math.Round(x.GetDouble()) : -1)] : [];
            static (int, int)[] Pairs(int[] keys, int[] values) => [.. keys.Zip(values).Where(kv => kv.First >= 0)];
            var effects = new Dictionary<string, (int, int)>();
            var statSets = new List<int>();
            var effectInfo = new List<(int, int, bool)>();
            int activeCount = 0;
            if (File.Exists(Table("ActiveSkills")))
            {
                using var active = JsonDocument.Parse(File.ReadAllBytes(Table("ActiveSkills")));
                activeCount = active.RootElement.GetArrayLength();
            }
            var activeSkills = new List<int?>();
            int row = 0;
            foreach (var r in g.RootElement.EnumerateArray())
            {
                string? id = r.GetProperty("Id").GetString();
                int statSet = Int(r, "StatSet1");
                int activeRow = Int(r, "ActiveSkill");
                activeSkills.Add(activeRow >= 0 && activeRow < activeCount ? activeRow : null);
                if (id is not null) effects.TryAdd(id, (row, statSet));
                statSets.Add(statSet);
                effectInfo.Add((statSet, Math.Max(0, Int(r, "CastTime")),
                    r.TryGetProperty("CooldownNotRecoverDuringActive", out var pauses) && pauses.ValueKind == JsonValueKind.True));
                row++;
            }
            var baseStats = new List<(int[], (int, int)[])>();
            if (File.Exists(gess))
            {
                using var b = JsonDocument.Parse(File.ReadAllBytes(gess));
                foreach (var r in b.RootElement.EnumerateArray())
                    baseStats.Add((Ints(r, "ImplicitStats"), Pairs(Ints(r, "ConstantStats"), Ints(r, "ConstantStatsValues"))));
            }
            var perLevel = new List<(int, int)>();
            var mana = new List<int>();
            var reservations = new List<(int ManaFlat, int ManaPercent, int LifeFlat, int LifePercent)>();
            var cooldowns = new List<(int, int)>();
            var vaalSouls = new List<int>();
            var vaalStoredUses = new List<int>();
            var attackSpeedMultipliers = new List<int>();
            foreach (var r in p.RootElement.EnumerateArray())
            {
                perLevel.Add((Int(r, "GrantedEffect"), Int(r, "Level")));
                int[] types = Ints(r, "CostTypes"), amounts = Ints(r, "CostAmounts");
                int i = Array.IndexOf(types, 0);
                mana.Add(i >= 0 && i < amounts.Length ? amounts[i] : 0);
                reservations.Add((Math.Max(0, Int(r, "ManaReservationFlat")), Math.Max(0, Int(r, "ManaReservationPercent")),
                    Math.Max(0, Int(r, "LifeReservationFlat")), Math.Max(0, Int(r, "LifeReservationPercent"))));
                cooldowns.Add((Math.Max(0, Int(r, "Cooldown")), Math.Max(0, Int(r, "StoredUses"))));
                vaalSouls.Add(Int(r, "VaalSouls"));
                vaalStoredUses.Add(Int(r, "VaalStoredUses"));
                attackSpeedMultipliers.Add(r.TryGetProperty("AttackSpeedMultiplier", out var speed)
                    && speed.ValueKind == JsonValueKind.Number && speed.TryGetInt32(out int multiplier) ? multiplier : 0);
            }
            var quality = new Dictionary<int, (int, int)[]>();
            if (File.Exists(Table("GrantedEffectQualityStats")))
            {
                using var q = JsonDocument.Parse(File.ReadAllBytes(Table("GrantedEffectQualityStats")));
                foreach (var r in q.RootElement.EnumerateArray())
                    quality.TryAdd(Int(r, "GrantedEffectsKey"), Pairs(Ints(r, "StatsKeys"), Ints(r, "StatsValuesPermille")));
            }
            var setLevels = new List<(int, int)>();
            var effectiveness = new List<int>();
            var spellCritChances = new List<int?>();
            var levelStats = new List<(int, int)[]>();
            var levelReqs = new List<(int, int, int)>();
            foreach (var r in s.RootElement.EnumerateArray())
            {
                setLevels.Add((Int(r, "StatSet"), Int(r, "GemLevel")));
                // PoB Export/Scripts/skills.lua: raw / 10000 + 1. Zero is 100%, negatives are real reductions.
                effectiveness.Add(r.TryGetProperty("DamageEffectiveness", out var effectivenessValue)
                    && effectivenessValue.TryGetInt32(out int offset)
                        ? (int)Math.Clamp(10_000L + offset, 0, int.MaxValue) : 0);
                int crit = Int(r, "SpellCritChance");
                spellCritChances.Add(crit >= 0 ? crit : null);
                levelStats.Add([.. Pairs(Ints(r, "FloatStats"), Ints(r, "BaseResolvedValues")),
                    .. Pairs(Ints(r, "AdditionalStats"), Ints(r, "AdditionalStatsValues"))]);
                if (Int(r, "PlayerLevelReq") is >= 0 and var req) levelReqs.Add((Int(r, "StatSet"), Int(r, "GemLevel"), req));
            }
            return new Tables(effects, statSets, perLevel, setLevels, effectInfo, baseStats, levelStats, levelReqs, mana, reservations, cooldowns,
                vaalSouls, vaalStoredUses, activeSkills, quality, effectiveness, spellCritChances, attackSpeedMultipliers);
        }
    }
}
