/// <summary>
/// The auras the server models: their BuffDefinitions rows and their effects on the player. Reservation itself is
/// data-driven (SkillCatalog.Reservation). This table holds ordinary self auras; heralds, banners, minion summons
/// and enemy debuff auras require their own mechanics rather than treating every reservation as a self buff.
/// </summary>
static class AuraCatalog
{
    /// <summary>One active reservation skill: the skill id, its GrantedEffects row and id, its buff instance, and its gem's
    /// level and quality.</summary>
    public sealed record Aura(ushort SkillId, int Effect, string EffectId, string Name, ushort Buff, ushort BuffRow,
        int ManaFlat, int ManaPercent, int Level = 1, int Quality = 0, ushort? SourceBuff = null);

    // The source supplies the ground art; the received buff supplies the HUD icon and active-skill association.
    // Distinct ids let both survive area restoration and end together without counting the aura's bonuses twice.
    public static IEnumerable<byte[]> StartPackets(Aura aura, uint player, ushort? action)
    {
        if (aura.SourceBuff is ushort source)
            yield return GameServer.Core.Protocol.Instance.InstanceEntities.AddBuff(player, source, aura.BuffRow,
                player, aura.SkillId, auraSource: true, displayState: 0);
        yield return GameServer.Core.Protocol.Instance.InstanceEntities.AddBuff(player, aura.Buff, aura.BuffRow,
            player, aura.SkillId, activeSkill: action is ushort hash ? (aura.SkillId, hash) : null);
    }

    public static IEnumerable<byte[]> EndPackets(Aura aura, uint player)
    {
        yield return GameServer.Core.Protocol.Instance.InstanceEntities.RemoveBuff(player, aura.Buff);
        if (aura.SourceBuff is ushort source)
            yield return GameServer.Core.Protocol.Instance.InstanceEntities.RemoveBuff(player, source);
    }

    /// <summary>The BuffDefinitions row that draws each aura's visual and buff icon.</summary>
    private static readonly Dictionary<string, ushort> BuffRows = new()
    {
        ["PurityOfElements"] = 144,
        ["PurityOfFire"] = 145,
        ["PurityOfIce"] = 146,
        ["PurityOfLightning"] = 147,
        ["Clarity"] = 155,
        ["Vitality"] = 153,
        ["Precision"] = 1293,
        ["Haste"] = 152,
        ["Determination"] = 142,
        ["Grace"] = 143,
        ["Discipline"] = 148,
        ["Anger"] = 149,
        ["Hatred"] = 150,
        ["Wrath"] = 151,
        ["Zealotry"] = 1272,
        ["Malevolence"] = 1274,
        ["Envy"] = 828,
    };

    public static string Family(string effectId) => effectId switch
    {
        "GraceHardMode" => "Grace", "DeterminationHardMode" => "Determination", _ => effectId,
    };

    public static bool Supported(string effectId) => BuffRows.ContainsKey(Family(effectId));

    public static ushort BuffRow(string effectId) => BuffRows[Family(effectId)];

    public static IReadOnlyList<Aura> Restore(IEnumerable<string> saved, IEnumerable<Aura> available, CharacterStats.Pools pools, bool inTown = false)
    {
        if (inTown) return [];
        var gems = available.Where(a => Supported(a.EffectId)).GroupBy(a => Family(a.EffectId)).ToDictionary(g => g.Key, g => g.First());
        var restored = new List<Aura>();
        foreach (string effect in saved.Select(Family).Distinct())
            if (gems.TryGetValue(effect, out var aura))
                restored.Add(aura);
        return restored;
    }

    /// <summary>
    /// The mana reserved by the active auras: flat reservations plus the percent reservations (permyriad) of the
    /// character's maximum mana, each reduced by mana reservation efficiency (PoB CalcPerform: divide by
    /// 1 + efficiency / 100; the final amount is rounded up to whole mana).
    /// </summary>
    public static int ReservedMana(CharacterStats.Pools pools, IEnumerable<Aura> auras)
        => Math.Min((int)pools.Mana, RequiredMana(pools, auras));

    public static int RequiredMana(CharacterStats.Pools pools, IEnumerable<Aura> auras)
    {
        double efficiency = Math.Max(-99, pools.ManaReservationEfficiencyPct) / 100.0 + 1;
        double flat = 0, percent = 0;
        foreach (var aura in auras)
        {
            flat += Math.Round(aura.ManaFlat / efficiency, MidpointRounding.AwayFromZero);
            // PoB rounds each skill's reserved percent to two decimals before summing it.
            percent += Math.Round(aura.ManaPercent / 100.0 / efficiency, 2, MidpointRounding.AwayFromZero);
        }
        double reserved = flat + pools.Mana * percent / 100.0;
        return (int)Math.Clamp(Math.Ceiling(reserved), 0, int.MaxValue);
    }

    public static int UnreservedMana(CharacterStats.Pools pools, IEnumerable<Aura> auras) =>
        Math.Max(0, (int)pools.Mana - ReservedMana(pools, auras));

    /// <summary>The player stat bonuses from the active auras, scaled by generic aura effect.</summary>
    public static CharacterStats.Bonus Bonus(IEnumerable<Aura> auras, int auraEffectPct, SkillCatalog skills,
        StatCatalog stats)
    {
        int fire = 0, cold = 0, lightning = 0, chaos = 0;
        int fireMax = 0, coldMax = 0, lightningMax = 0, chaosMax = 0;
        int accuracy = 0, criticalChance = 0, attackSpeed = 0, movementSpeed = 0;
        int armour = 0, evasion = 0, energyShield = 0, armourMore = 0, evasionMore = 0, spellCrit = 0;
        foreach (var aura in auras)
        {
            switch (Family(aura.EffectId))
            {
                case "Determination":
                    armour += Value(skills, stats, aura, "base_physical_damage_reduction_rating", auraEffectPct);
                    armourMore += Value(skills, stats, aura, "determination_aura_armour_+%_final", auraEffectPct);
                    break;
                case "Grace":
                    evasion += Value(skills, stats, aura, "base_evasion_rating", auraEffectPct);
                    evasionMore += Value(skills, stats, aura, "grace_aura_evasion_rating_+%_final", auraEffectPct);
                    break;
                case "Zealotry":
                    spellCrit += Value(skills, stats, aura, "spell_critical_strike_chance_+%", auraEffectPct);
                    break;
                case "Discipline":
                    // Flat energy shield before the increases (PoB: a BASE mod); RawStats only publishes the raw stat.
                    energyShield += Value(skills, stats, aura, "base_maximum_energy_shield", auraEffectPct);
                    break;
                case "PurityOfElements":
                    int all = Value(skills, stats, aura, "base_resist_all_elements_%", auraEffectPct);
                    fire += all;
                    cold += all;
                    lightning += all;
                    break;
                case "PurityOfFire":
                    fire += Value(skills, stats, aura, "base_fire_damage_resistance_%", auraEffectPct);
                    fireMax += Value(skills, stats, aura, "base_maximum_fire_damage_resistance_%", auraEffectPct);
                    break;
                case "PurityOfIce":
                    cold += Value(skills, stats, aura, "base_cold_damage_resistance_%", auraEffectPct);
                    coldMax += Value(skills, stats, aura, "base_maximum_cold_damage_resistance_%", auraEffectPct);
                    break;
                case "PurityOfLightning":
                    lightning += Value(skills, stats, aura, "base_lightning_damage_resistance_%", auraEffectPct);
                    lightningMax += Value(skills, stats, aura, "base_maximum_lightning_damage_resistance_%", auraEffectPct);
                    break;
                case "Precision":
                    accuracy += Value(skills, stats, aura, "accuracy_rating", auraEffectPct);
                    int crit = Value(skills, stats, aura, "skill_buff_grants_critical_strike_chance_+%",
                        auraEffectPct);
                    criticalChance += crit;
                    break;
                case "Haste":
                    attackSpeed += Value(skills, stats, aura, "attack_speed_+%_granted_from_skill", auraEffectPct);
                    movementSpeed += Value(skills, stats, aura, "base_movement_velocity_+%", auraEffectPct);
                    break;
            }
        }
        return new CharacterStats.Bonus(FireResistance: fire, ColdResistance: cold, LightningResistance: lightning,
            ChaosResistance: chaos, FireMaxResistance: fireMax, ColdMaxResistance: coldMax,
            LightningMaxResistance: lightningMax, ChaosMaxResistance: chaosMax,
            Accuracy: accuracy, CriticalStrikeChancePct: criticalChance, SpellCriticalStrikeChancePct: criticalChance + spellCrit,
            AttackSpeedPct: attackSpeed, MovementVelocityPct: movementSpeed, Armour: armour, Evasion: evasion,
            ArmourMorePct: armourMore, EvasionMorePct: evasionMore, EnergyShield: energyShield);
    }

    /// <summary>The raw keys behind Purity's resistance and maximum bonuses, for per-key dev replacements.
    /// Keep these separate from RawStats: resistance presentation already reads the completed pools.</summary>
    public static IEnumerable<(uint Key, int Value)> ResistanceStats(IEnumerable<Aura> auras, int auraEffectPct,
        SkillCatalog skills, StatCatalog stats)
    {
        foreach (var aura in auras)
        {
            string? element = aura.EffectId switch
            {
                "PurityOfFire" => "fire", "PurityOfIce" => "cold", "PurityOfLightning" => "lightning", _ => null,
            };
            string[] names = aura.EffectId == "PurityOfElements" ? ["base_resist_all_elements_%"]
                : element is not null ? [$"base_{element}_damage_resistance_%", $"base_maximum_{element}_damage_resistance_%"]
                : [];
            foreach (string name in names)
                if (stats.Key(name) is uint key) yield return (key, Value(skills, stats, aura, name, auraEffectPct));
        }
    }

    /// <summary>The flat stat bonuses from the active auras (regeneration and Haste's cast speed), scaled by aura effect.</summary>
    public static IEnumerable<(uint Key, int Value)> RawStats(IEnumerable<Aura> auras, int auraEffectPct,
        SkillCatalog skills, StatCatalog stats)
    {
        var result = new Dictionary<uint, int>();
        foreach (var aura in auras)
        {
            switch (aura.EffectId)
            {
                case "Anger":
                    Add("attack_minimum_added_fire_damage"); Add("attack_maximum_added_fire_damage");
                    Add("spell_minimum_added_fire_damage"); Add("spell_maximum_added_fire_damage");
                    break;
                case "Hatred":
                    Add("physical_damage_%_to_add_as_cold");
                    break;
                case "Wrath":
                    Add("attack_minimum_added_lightning_damage"); Add("attack_maximum_added_lightning_damage");
                    Add("wrath_aura_spell_lightning_damage_+%_final");
                    break;
                case "Envy":
                    Add("attack_minimum_added_chaos_damage"); Add("attack_maximum_added_chaos_damage");
                    Add("spell_minimum_added_chaos_damage"); Add("spell_maximum_added_chaos_damage");
                    break;
                case "Zealotry":
                    Add("spell_damage_aura_spell_damage_+%_final");
                    Add("spell_critical_strike_chance_+%");
                    break;
                case "Precision":
                    Add("critical_strike_chance_+%", "skill_buff_grants_critical_strike_chance_+%");
                    break;
                case "Malevolence":
                    Add("delirium_aura_damage_over_time_+%_final");
                    Add("skill_effect_duration_+%", "delirium_skill_effect_duration_+%");
                    break;
                case "Discipline":
                    Add("base_maximum_energy_shield");
                    Add("energy_shield_recharge_rate_+%");
                    break;
                case "Clarity":
                    Add("base_mana_regeneration_rate_per_minute");
                    break;
                case "Vitality":
                    Add("base_life_regeneration_rate_per_minute");
                    break;
                case "Haste":
                    Add("cast_speed_+%", "cast_speed_+%_granted_from_skill");
                    break;
            }

            // Only size inputs compose here; this does not activate or add any aura.
            var effectStats = skills.EffectStats(aura.Effect, aura.Level, aura.Quality);
            foreach (string name in ActorScale.Inputs)
            {
                if (stats.Key(name) is not uint key) continue;
                var source = effectStats.FirstOrDefault(s => s.Item1 == key);
                if (source.Item1 != key) continue; // Preserve present zero limits as well as nonzero size sources.
                int raw = source.Item2;
                int value = (int)Math.Clamp(Math.Round(raw * Math.Max(0L, 100L + auraEffectPct) / 100.0,
                    MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);
                result[key] = ActorScale.Add(key, result.GetValueOrDefault(key), value);
            }

            void Add(string name, string? source = null)
            {
                if (stats.Key(name) is not uint key) return;
                result[key] = result.GetValueOrDefault(key)
                    + Value(skills, stats, aura, source ?? name, auraEffectPct);
            }
        }
        return result.Select(kv => (kv.Key, kv.Value));
    }

    private static int Value(SkillCatalog skills, StatCatalog stats, Aura aura, string name, int auraEffectPct)
    {
        if (stats.Key(name) is not uint key) return 0;
        int value = skills.EffectStats(aura.Effect, aura.Level, aura.Quality).FirstOrDefault(kv => kv.Item1 == key).Item2;
        // PoB ModStore.ScaleAddList truncates these integer defence-aura stats before character-wide scaling.
        // Decimal preserves the exact hundredths from the integer aura-effect percentage.
        if (Family(aura.EffectId) is "Discipline" or "Determination" or "Grace")
            return (int)Math.Clamp(decimal.Truncate(value * Math.Max(0m, 100m + auraEffectPct) / 100m), int.MinValue, int.MaxValue);
        return (int)Math.Round(value * Math.Max(0, 100 + auraEffectPct) / 100.0, MidpointRounding.AwayFromZero);
    }
}
