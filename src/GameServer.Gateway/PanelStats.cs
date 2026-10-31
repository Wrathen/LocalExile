using GameServer.Core.World;

/// <summary>
/// The character panel's "sum" stats (issue #75): virtual stats that the server works out from the base forms that
/// passives, items and auras give, because the client derives none of them (11-skills.md §4). A passive grants
/// <c>base_stun_recovery_+%</c>, the panel's Stun Recovery row reads <c>stun_recovery_+%</c>. Each display stat is its
/// own value plus every unconditional source below: the base form, and the combined stats that apply to it too (Avoid
/// Elemental Ailments counts for each avoid row, "increased Duration of Elemental Ailments" for each duration).
/// Main hand / off hand rows also add that weapon's local stats (its own chance to bleed, penetration, life gain on hit).
/// Conditional stats (while, if, per charge, against …) are not counted. Flags (cannot be, immune to) are 1 when any
/// source is set. The pools' own stats (life, mana, armour, evasion, attributes, resistances, suppression, block,
/// movement and attack speed, crit) are not here: <see cref="CharacterStats"/> computes those, and
/// <see cref="FromPools"/> adds the rows that combine them with a stat.
/// </summary>
static class PanelStats
{
    /// <summary>A source stat, scaled: × <paramref name="Mul"/> ÷ <paramref name="Div"/> (a "-%" stat counts negative;
    /// a 1/100 000 leech counts ÷ 10 as permyriad). Player-level sources multiply before integer division.</summary>
    public readonly record struct Src(string Name, int Mul = 1, int Div = 1, bool PerPlayerLevel = false)
    {
        public static implicit operator Src(string name) => new(name);
    }

    /// <summary>A display stat and what it sums; <paramref name="Local"/> are the weapon's own stats (hand rows).</summary>
    public sealed record Sum(string Display, Src[] Sources, bool Flag = false, Src[]? Local = null, Hand Hand = Hand.None);

    public enum Hand { None, Main, Off }

    const string ElementalAilmentDuration = "base_elemental_status_ailment_duration_+%";
    const string AilmentDuration = "base_all_ailment_duration_+%";
    const string NonDamagingAilmentEffect = "non_damaging_ailment_effect_+%";
    const string AvoidElementalAilments = "avoid_all_elemental_status_%";
    const string SelfElementalAilmentDuration = "self_elemental_status_duration_-%";
    const string ChargeDuration = "charge_duration_+%", BaseChargeDuration = "base_charge_duration_+%";
    const string LeechSpeed = "life_mana_es_leech_speed_+%";
    const string ElementalPenetration = "reduce_enemy_elemental_resistance_%", PenetrateElemental = "base_penetrate_elemental_resistances_%";
    const string IgniteChance = "base_chance_to_ignite_%", FreezeShockIgniteChance = "chance_to_freeze_shock_ignite_%";

    static readonly Src[] GlobalFlaskDurationSources =
        ["flask_duration_+%", new("flask_duration_+%_per_level", PerPlayerLevel: true)];

    public static readonly Sum[] Sums =
    [
        // Offence
        new("cast_speed_+%", ["base_cast_speed_+%", "attack_and_cast_speed_+%"]),
        new("global_chance_to_knockback_%", ["base_global_chance_to_knockback_%"]),
        new("virtual_knockback_distance_+%", ["knockback_distance_+%"]),
        new("stun_duration_+%", ["base_stun_duration_+%"]),
        new("virtual_chance_to_blind_on_hit_%", ["global_chance_to_blind_on_hit_%"]),
        new("virtual_ignite_duration_+%", ["ignite_duration_+%", ElementalAilmentDuration, AilmentDuration,
            "fire_ailment_duration_+%", "skill_effect_and_damaging_ailment_duration_+%"]),
        new("virtual_scorch_duration_+%", [ElementalAilmentDuration, AilmentDuration, "fire_ailment_duration_+%"]),
        new("virtual_scorch_effect_+%", ["scorch_effect_+%", NonDamagingAilmentEffect]),
        new("virtual_chill_duration_+%", ["chill_duration_+%", "chill_and_freeze_duration_+%", ElementalAilmentDuration,
            AilmentDuration, "cold_ailment_duration_+%"]),
        new("virtual_chill_effect_+%", ["chill_effect_+%", "cold_ailment_effect_+%", NonDamagingAilmentEffect]),
        new("chance_to_freeze_%", ["base_chance_to_freeze_%", FreezeShockIgniteChance]),
        new("virtual_freeze_duration_+%", ["freeze_duration_+%", "chill_and_freeze_duration_+%", ElementalAilmentDuration,
            AilmentDuration, "cold_ailment_duration_+%"]),
        new("virtual_frostburn_duration_+%", [ElementalAilmentDuration, AilmentDuration, "cold_ailment_duration_+%"]),
        new("virtual_frostburn_effect_+%", ["cold_ailment_effect_+%", NonDamagingAilmentEffect]),
        new("chance_to_shock_%", ["base_chance_to_shock_%", FreezeShockIgniteChance]),
        new("virtual_shock_duration_+%", ["shock_duration_+%", ElementalAilmentDuration, AilmentDuration,
            "lightning_ailment_duration_+%"]),
        new("virtual_shock_effect_+%", ["shock_effect_+%", "lightning_ailment_effect_+%", NonDamagingAilmentEffect]),
        new("virtual_sap_duration_+%", [ElementalAilmentDuration, AilmentDuration, "lightning_ailment_duration_+%"]),
        new("virtual_sap_effect_+%", ["lightning_ailment_effect_+%", NonDamagingAilmentEffect]),
        // Spells and secondary damage (no weapon)
        new("virtual_total_spell_chance_to_ignite_on_hit_%", [IgniteChance, FreezeShockIgniteChance]),
        new("virtual_total_secondary_chance_to_ignite_on_hit_%", [IgniteChance, FreezeShockIgniteChance]),
        new("virtual_total_spell_chance_to_poison_on_hit_%", ["base_chance_to_poison_on_hit_%", "spells_chance_to_poison_on_hit_%"]),
        new("virtual_total_secondary_chance_to_poison_on_hit_%", ["base_chance_to_poison_on_hit_%"]),
        new("virtual_total_spell_impale_on_hit_%_chance", ["spells_impale_on_hit_%_chance", "impale_on_hit_%_chance"]),
        new("reduce_enemy_fire_resistance_%", ["base_reduce_enemy_fire_resistance_%", ElementalPenetration, PenetrateElemental]),
        new("reduce_enemy_cold_resistance_%", ["base_reduce_enemy_cold_resistance_%", ElementalPenetration, PenetrateElemental]),
        new("reduce_enemy_lightning_resistance_%", ["base_reduce_enemy_lightning_resistance_%", ElementalPenetration, PenetrateElemental]),
        // Damage immunities (the Defence tab's "Immune to X" rows). A source sets the base form; the elemental
        // immunity covers fire, cold and lightning, and chaos keeps its own older name (chaos_immunity). The display
        // stat itself counts too (Compute adds get(display)), so a mod that sets fire_damage_immunity directly works.
        new("fire_damage_immunity", ["base_fire_immunity", "elemental_damage_immunity"], Flag: true),
        new("cold_damage_immunity", ["base_cold_immunity", "elemental_damage_immunity"], Flag: true),
        new("lightning_damage_immunity", ["base_lightning_immunity", "elemental_damage_immunity"], Flag: true),
        new("chaos_damage_immunity", ["chaos_immunity", "keystone_chaos_inoculation"], Flag: true),
        new("physical_immunity", [], Flag: true),
        // Main hand and off hand: the global stats and that weapon's own
        .. Hands(h => new($"{h}_chance_to_blind_on_hit_%", ["global_chance_to_blind_on_hit_%", "attacks_chance_to_blind_on_hit_%"],
            Local: ["local_chance_to_blind_on_hit_%"])),
        .. Hands(h => new($"virtual_total_{h}_chance_to_bleed_on_hit_%", ["bleed_on_hit_with_attacks_%"],
            Local: ["local_chance_to_bleed_on_hit_%"])),
        .. Hands(h => new($"virtual_total_{h}_chance_to_ignite_on_hit_%", [IgniteChance, FreezeShockIgniteChance])),
        .. Hands(h => new($"virtual_total_{h}_chance_to_poison_on_hit_%",
            ["base_chance_to_poison_on_hit_%", "chance_to_poison_on_hit_with_attacks_%"], Local: ["local_poison_on_hit_%"])),
        .. Hands(h => new($"virtual_total_{h}_impale_on_hit_%_chance", ["attacks_impale_on_hit_%_chance", "impale_on_hit_%_chance"],
            Local: ["local_attacks_impale_on_hit_%_chance"])),
        .. Hands(h => new($"{h}_attack_damage_penetrates_fire_resist_%", ["base_reduce_enemy_fire_resistance_%", ElementalPenetration,
            PenetrateElemental, "base_attack_damage_penetrates_fire_resist_%", "base_attack_damage_penetrates_elemental_resist_%"],
            Local: ["local_fire_penetration_%", "local_elemental_penetration_%"])),
        .. Hands(h => new($"{h}_attack_damage_penetrates_cold_resist_%", ["base_reduce_enemy_cold_resistance_%", ElementalPenetration,
            PenetrateElemental, "base_attack_damage_penetrates_cold_resist_%", "base_attack_damage_penetrates_elemental_resist_%"],
            Local: ["local_cold_penetration_%", "local_elemental_penetration_%"])),
        .. Hands(h => new($"{h}_attack_damage_penetrates_lightning_resist_%", ["base_reduce_enemy_lightning_resistance_%",
            ElementalPenetration, PenetrateElemental, "base_attack_damage_penetrates_lightning_resist_%",
            "base_attack_damage_penetrates_elemental_resist_%"], Local: ["local_lightning_penetration_%", "local_elemental_penetration_%"])),
        .. Hands(h => new($"{h}_attack_damage_penetrates_chaos_resist_%", ["reduce_enemy_chaos_resistance_%",
            "base_attack_damage_penetrates_chaos_resist_%"],
            Local: ["local_chaos_penetration_%"])),
        .. Hands(h => new($"{h}_stun_threshold_reduction_+%", ["base_stun_threshold_reduction_+%"],
            Local: ["local_stun_threshold_reduction_+%"])),
        .. Hands(h => new($"{h}_chance_to_knockback_%", ["base_global_chance_to_knockback_%"])),
        .. Hands(h => new($"{h}_knockback_on_crit", ["global_knockback_on_crit"], Flag: true)),
        .. Hands(h => new($"{h}_hit_causes_monster_flee_%", ["global_hit_causes_monster_flee_%"])),
        .. Hands(h => new($"{h}_reduce_enemy_block_%", ["global_reduce_enemy_block_%"])),
        .. Hands(h => new($"{h}_double_damage_to_chilled_enemies", [], Flag: true, Local: ["local_double_damage_to_chilled_enemies"])),
        .. Hands(h => new($"{h}_local_life_gain_per_target", ["base_life_gain_per_target"], Local: ["local_life_gain_per_target"])),
        .. Hands(h => new($"{h}_local_mana_gain_per_target", ["mana_gain_per_target"], Local: ["local_mana_gain_per_target"])),
        .. HandLeech("life"), .. HandLeech("mana"),
        new("virtual_leech_energy_shield_instead_of_life", ["leech_energy_shield_instead_of_life", "keystone_ghost_reaver"], Flag: true),
        new("virtual_life_leech_does_not_stop_at_full_life", ["life_leech_does_not_stop_at_full_life"], Flag: true),
        // Defence
        new("virtual_energy_shield_delay_-%", ["energy_shield_delay_-%"]),
        new("virtual_block_recovery_+%", ["block_recovery_+%", "base_stun_recovery_+%", "stun_recovery_+%"]),
        new("stun_recovery_+%", ["base_stun_recovery_+%"]),
        new("avoid_stun_%", ["base_avoid_stun_%"]),
        new("cannot_be_stunned", ["base_cannot_be_stunned"], Flag: true),
        new("extra_damage_taken_from_crit_+%", [new("base_self_critical_strike_multiplier_-%", Mul: -1)]),
        new("avoid_bleed_%", ["base_avoid_bleed_%"]),
        new("cannot_gain_bleeding", ["base_cannot_gain_bleeding"], Flag: true),
        new("virtual_immune_to_bleeding", ["immune_to_bleeding"], Flag: true),
        new("combined_bleeding_duration_on_self_+%", ["self_bleed_duration_+%"]),
        .. Ailment("ignite", "ignited"), .. Ailment("chill", "chilled"), .. Ailment("freeze", "frozen"), .. Ailment("shock", "shocked"),
        new("avoid_poison_%", ["base_avoid_poison_%"]),
        new("virtual_cannot_be_poisoned", ["cannot_be_poisoned"], Flag: true),
        new("virtual_immune_to_poison", ["immune_to_poison"], Flag: true),
        new("combined_poison_duration_on_self_+%", ["self_poison_duration_+%"]),
        .. new[] { "physical", "fire", "cold", "lightning", "chaos" }.SelectMany(type => new Sum[]
        {
            new($"minimum_{type}_damage_to_return_to_melee_attacker", [$"{type}_damage_to_return_to_melee_attacker"]),
            new($"maximum_{type}_damage_to_return_to_melee_attacker", [$"{type}_damage_to_return_to_melee_attacker"]),
        }),
        new("intermediary_combined_action_speed_+%", [new("action_speed_-%", Mul: -1)]),
        // "Cannot be Stunned by Spells / Attacks": raw flags of their own (Stormshroud-style sources set them directly);
        // the all-stun cannot_be_stunned row stays separate, as the panel reads both.
        new("cannot_be_stunned_by_spells", [], Flag: true),
        new("cannot_be_stunned_by_attacks", [], Flag: true),
        // Misc
        new("ascendance", ["base_ascendance"]),
        new("skill_area_of_effect_+%", ["base_skill_area_of_effect_+%"]),
        new("virtual_curse_effect_+%", ["curse_effect_+%"]),
        new("virtual_flask_duration_+%", GlobalFlaskDurationSources),
        .. Leech("life"), .. Leech("mana"), .. Leech("energy_shield"),
        new("life_gained_on_spell_hit", ["base_life_gained_on_spell_hit"]),
        new("virtual_energy_shield_gain_per_target", ["energy_shield_gain_per_target"]),
        new("life_gained_on_enemy_death", ["base_life_gained_on_enemy_death",
            new("life_gained_on_enemy_death_per_level", PerPlayerLevel: true)]),
        new("mana_gained_on_enemy_death", ["base_mana_gained_on_enemy_death",
            new("mana_gained_on_enemy_death_per_level", PerPlayerLevel: true)]),
        new("energy_shield_gained_on_enemy_death", ["base_energy_shield_gained_on_enemy_death",
            new("energy_shield_gained_on_enemy_death_per_level", PerPlayerLevel: true)]),
        new("virtual_minion_damage_+%", ["minion_damage_+%"]),
        new("projectile_speed_+%", ["base_projectile_speed_+%"]),
        new("display_skill_increased_item_rarity_+%", ["base_item_found_rarity_+%", "item_found_rarity_+%",
            "base_killed_monster_dropped_item_rarity_+%"]),
        new("display_skill_increased_item_quantity_+%", ["base_item_found_quantity_+%", "item_found_quantity_+%"]),
        new("virtual_trap_throwing_speed_+%", ["trap_throwing_speed_+%", "trap_and_mine_throwing_speed_+%"]),
        new("virtual_mine_throwing_speed_+%", ["mine_laying_speed_+%", "trap_and_mine_throwing_speed_+%"]),
        new("virtual_cooldown_speed_+%", ["base_cooldown_speed_+%"]),
        // "Attached Brand Limit per Target": the brand skill's own base (Storm Brand grants 1) plus "an additional brand
        // attached to an enemy" sources; a selected brand sums both (Selected), the character alone shows its extras.
        new("brands_per_target_limit", ["base_number_of_sigils_allowed_per_target"]),
        // Charges
        new("virtual_endurance_charge_duration_+%", ["endurance_charge_duration_+%", ChargeDuration, BaseChargeDuration]),
        new("virtual_frenzy_charge_duration_+%", ["base_frenzy_charge_duration_+%", ChargeDuration, BaseChargeDuration]),
        new("virtual_power_charge_duration_+%", ["power_charge_duration_+%", ChargeDuration, BaseChargeDuration]),
        new("attack_speed_+%_per_frenzy_charge", ["base_attack_speed_+%_per_frenzy_charge", "attack_and_cast_speed_+%_per_frenzy_charge"]),
        new("cast_speed_+%_per_frenzy_charge", ["base_cast_speed_+%_per_frenzy_charge", "attack_and_cast_speed_+%_per_frenzy_charge"]),
        // Spirit and Siphoning Charges: the direct maximum sources. Their per-equipped-item sources (abyss jewels,
        // Elder/Shaper items) count items the server does not model, so they contribute nothing yet; the siphoning
        // damage-taken row shows its per-charge coefficient as the panel's "Physical Damage per Second per Siphoning
        // Charge". The counts themselves (current_*) belong to charge mechanics that do not exist yet.
        new("virtual_maximum_spirit_charges", ["maximum_spirit_charges"]),
        new("virtual_physical_damage_taken_per_minute_per_siphoning_charge",
            ["physical_damage_taken_per_minute_per_siphoning_charge_if_have_used_a_skill_recently"]),
    ];

    // A row for each hand: main_hand_… with the main hand weapon's local stats, off_hand_… with the off hand's.
    static Sum[] Hands(Func<string, Sum> row) =>
        [row("main_hand") with { Hand = Hand.Main }, row("off_hand") with { Hand = Hand.Off }];

    // An elemental ailment's defence rows: avoid (with Avoid Elemental Ailments), the duration on self (with the elemental
    // ailment duration on self), cannot be (flag) and immune (flag; "Immune to Ignite and Shock" counts for both).
    static Sum[] Ailment(string ailment, string past) =>
    [
        new($"avoid_{ailment}_%", [$"base_avoid_{ailment}_%", AvoidElementalAilments]),
        new($"self_{ailment}_duration_-%", [$"base_self_{ailment}_duration_-%", SelfElementalAilmentDuration,
            new("base_all_ailment_duration_on_self_+%", Mul: -1)]),
        new($"cannot_be_{past}", [$"base_cannot_be_{past}"], Flag: true),
        new($"immune_to_{ailment}", ailment is "ignite" or "shock"
            ? [$"base_immune_to_{ailment}", "immune_to_ignite_and_shock"] : [$"base_immune_to_{ailment}"], Flag: true),
    ];

    // Raw sources only: feeding another derived alias into these sums would count general sources twice.
    static Src[] LeechSources(string pool, string type) =>
    [
        $"{pool}_leech_from_any_damage_permyriad", $"base_{pool}_leech_from_{type}_damage_permyriad",
        .. type is "fire" or "cold" or "lightning" ? new Src[] { $"base_{pool}_leech_from_elemental_damage_permyriad" } : [],
        .. pool == "life" && type != "spell" ? new Src[] { new($"horrible_ugly_life_leech_from_{type}_damage_perhundredthousand", Div: 10) } : [],
    ];

    static Sum[] HandLeech(string pool) => Hands(h => new($"{h}_{pool}_leech_from_physical_attack_damage_permyriad",
        [.. LeechSources(pool, "physical"), $"{pool}_leech_from_physical_attack_damage_permyriad",
            "life_and_mana_leech_from_physical_damage_permyriad",
            $"base_{pool}_leech_from_attack_damage_permyriad",
            .. pool == "mana" ? new Src[] { "attack_skill_mana_leech_from_any_damage_permyriad" } : []],
        Local: [$"local_{pool}_leech_from_physical_damage_permyriad", "local_life_and_mana_leech_from_physical_damage_permyriad",
            $"local_{pool}_leech_from_any_damage_permyriad"]));

    // Each displayed scope includes applicable general sources; callers must not add these overlapping totals.
    static Sum[] Leech(string pool) =>
    [
        .. new[] { "spell", "physical", "fire", "cold", "lightning", "chaos" }.Select(type => new Sum(
            $"{(pool == "energy_shield" ? "virtual_" : "")}{pool}_leech_from_{type}_damage_permyriad", LeechSources(pool, type))),
        .. pool != "energy_shield" ? new Sum[] { new($"virtual_{pool}_leech_from_attack_damage_permyriad",
            [$"{pool}_leech_from_any_damage_permyriad", $"base_{pool}_leech_from_attack_damage_permyriad",
                .. pool == "mana" ? new Src[] { "attack_skill_mana_leech_from_any_damage_permyriad" } : []]) } : [],
        new($"virtual_{pool}_leech_speed_+%", [$"{pool}_leech_speed_+%", LeechSpeed]),
    ];

    /// <summary>Every display stat with a non-zero value, from the stats as the server holds them (<paramref name="get"/>:
    /// by Stats.dat Id, 0 when absent) and each hand's local weapon stats.</summary>
    public static List<(string Stat, int Value)> Compute(Func<string, int> get,
        Func<string, int>? mainHand = null, Func<string, int>? offHand = null, int playerLevel = 1)
    {
        playerLevel = Math.Clamp(playerLevel, 1, Progression.MaxLevel);
        var result = new List<(string, int)>();
        foreach (var sum in Sums)
        {
            var local = sum.Hand switch { Hand.Main => mainHand, Hand.Off => offHand, _ => null };
            bool blocked = Penetration.IsElementalDisplay(sum.Display) && get(Penetration.ElementalProhibition) != 0;
            int value = blocked ? 0 : Project(sum, RawSum(sum, get, local, playerLevel));
            if (value != 0 || blocked) result.Add((sum.Display, value));
        }
        return result;
    }

    /// <summary>One display stat's sum as <see cref="Compute"/> gives it, from raw stats (a skill's own); a stat with no
    /// row is its own value.</summary>
    public static int Of(string display, Func<string, int> get) =>
        Sums.FirstOrDefault(s => s.Display == display && s.Hand == Hand.None && !s.Flag) is { } sum
            ? (int)Math.Clamp(get(display) + sum.Sources.Sum(s => (long)get(s.Name) * s.Mul / s.Div), int.MinValue, int.MaxValue)
            : get(display);
    static long RawSum(Sum sum, Func<string, int> get, Func<string, int>? local, int level) =>
        get(sum.Display) + Total(sum.Sources, get, level) + Total(sum.Local, local, level);

    static int Project(Sum sum, long value) => sum.Flag ? value > 0 ? 1 : 0
        : ImpaleStats.IsDisplay(sum.Display) ? (int)Math.Clamp(value, 0, 100)
        : (int)Math.Clamp(value, int.MinValue, int.MaxValue);

    static readonly Sum[] SelectedSums = [.. Sums.Where(s => s.Display is "skill_area_of_effect_+%"
        or "display_skill_increased_item_rarity_+%" or "virtual_minion_damage_+%" or "brands_per_target_limit"
        or "reduce_enemy_fire_resistance_%" or "reduce_enemy_cold_resistance_%"
        or "reduce_enemy_lightning_resistance_%"
        or "virtual_ignite_duration_+%" or "virtual_scorch_duration_+%" or "virtual_scorch_effect_+%"
        or "virtual_chill_duration_+%" or "virtual_chill_effect_+%" or "virtual_freeze_duration_+%"
        or "virtual_frostburn_duration_+%" or "virtual_frostburn_effect_+%"
        or "virtual_shock_duration_+%" or "virtual_shock_effect_+%"
        or "virtual_sap_duration_+%" or "virtual_sap_effect_+%"
        or "virtual_total_main_hand_impale_on_hit_%_chance" or "virtual_total_off_hand_impale_on_hit_%_chance"
        or "virtual_total_spell_impale_on_hit_%_chance"
        || s.Display.Contains("_attack_damage_penetrates_", StringComparison.Ordinal)
        || s.Display.Contains("_leech_from_", StringComparison.Ordinal)
        || s.Display.EndsWith("_leech_speed_+%", StringComparison.Ordinal))];

    /// <summary>Selected-skill aliases from original raw character and qualifying skill/support inputs.
    /// Derive each alias once, clamp the completed sum, and retain zero to clear a removed contribution.
    /// Published character aliases must not be passed back as raw seeds.</summary>
    public static List<(string Stat, int Value)> Selected(Func<string, int> character, Func<string, int> own,
        int playerLevel = 1, Func<string, int>? mainHand = null, Func<string, int>? offHand = null)
    {
        int level = Math.Clamp(playerLevel, 1, Progression.MaxLevel);
        bool blockElemental = character(Penetration.ElementalProhibition) != 0 || own(Penetration.ElementalProhibition) != 0;
        long Combined(string name) => (long)character(name) + own(name);
        long SelectedSum(Sum sum) => Combined(sum.Display)
            + sum.Sources.Sum(s => Combined(s.Name) * s.Mul * (s.PerPlayerLevel ? level : 1) / s.Div)
            + Total(sum.Local, sum.Hand switch { Hand.Main => mainHand, Hand.Off => offHand, _ => null }, level);
        return [.. SelectedSums.Select(sum => (sum.Display,
            blockElemental && Penetration.IsElementalDisplay(sum.Display) ? 0 : Project(sum, SelectedSum(sum))))];
    }

    /// <summary>The global duration coefficient shared by the panel and actual flask uses. The display's virtual
    /// seed does not modify gameplay; item-local duration, quality and more modifiers remain separate.</summary>
    public static long FlaskDurationIncrease(Func<string, int> get, int playerLevel = 1) =>
        Total(GlobalFlaskDurationSources, get, Math.Clamp(playerLevel, 1, Progression.MaxLevel));

    // Sum one raw snapshot. The level is the player level supplied by the caller, never a native stat/gem level.
    static long Total(IEnumerable<Src>? sources, Func<string, int>? get, int playerLevel) =>
        get is null || sources is null ? 0 : sources.Sum(s =>
            (long)get(s.Name) * s.Mul * (s.PerPlayerLevel ? playerLevel : 1) / s.Div);

    /// <summary>
    /// The Offence tab's per-hand damage rows (issue #75): the hit the server actually swings with, per damage type and
    /// in total. The client shows whole numbers, so each range is rounded down. An empty packet adds no rows, which
    /// leaves those rows blank, as the game does for an attack that cannot happen.
    /// </summary>
    /// <summary>The twelve damage row names of a hand context (main_hand, off_hand, secondary): total then each type,
    /// minimum then maximum, for callers that clear or fill every row.</summary>
    public static IEnumerable<string> DamageRows(string hand)
    {
        foreach (string type in new[] { "total", "physical", "fire", "cold", "lightning", "chaos" })
            foreach (string bound in new[] { "minimum", "maximum" })
                yield return $"{hand}_{bound}_{type}_damage";
    }

    public static List<(string Stat, int Value)> HandDamage(DamagePacket packet, string hand)
    {
        var result = new List<(string, int)>();
        if (packet.IsZero) return result;
        static int Whole(double value) => (int)Math.Floor(Math.Max(0, value));
        result.Add(($"{hand}_minimum_total_damage", Whole(packet.MinTotal)));
        result.Add(($"{hand}_maximum_total_damage", Whole(packet.MaxTotal)));
        foreach (var (type, range) in packet.Components)
        {
            string? slug = type switch
            {
                DamageType.Physical => "physical",
                DamageType.Fire => "fire",
                DamageType.Cold => "cold",
                DamageType.Lightning => "lightning",
                DamageType.Chaos => "chaos",
                _ => null,
            };
            if (slug is null) continue;
            result.Add(($"{hand}_minimum_{slug}_damage", Whole(range.Min)));
            result.Add(($"{hand}_maximum_{slug}_damage", Whole(range.Max)));
        }
        return result;
    }

    /// <summary>
    /// The rows that combine a pool with a stat: block against projectiles (attack block + <c>additional_block_chance_against_projectiles_%</c>,
    /// spell block + <c>base_spell_projectile_block_%</c>, each capped at its maximum; the spell row shows when
    /// <c>spell_block_differs_against_projectiles</c> is set) and chaos resistance against damage over time (chaos
    /// resistance + <c>additional_chaos_resistance_against_damage_over_time_%</c>, capped at the chaos maximum).
    /// </summary>
    public static List<(string Stat, int Value)> FromPools(Func<string, int> get, CharacterStats.Pools pools)
    {
        var result = new List<(string, int)>();
        int projectileBlock = get("additional_block_chance_against_projectiles_%");
        if (projectileBlock != 0)
            result.Add(("projectile_attack_block_%", Math.Clamp(pools.BlockChanceUncapped + projectileBlock, 0, pools.BlockChanceMax)));
        int spellProjectileBlock = get("base_spell_projectile_block_%");
        if (spellProjectileBlock != 0)
        {
            result.Add(("spell_projectile_block_%", Math.Clamp(pools.SpellBlockChance + spellProjectileBlock, 0, pools.SpellBlockChanceMax)));
            result.Add(("spell_block_differs_against_projectiles", 1));
        }
        int chaosDot = get("additional_chaos_resistance_against_damage_over_time_%");
        if (chaosDot != 0)
        {
            int uncapped = pools.ChaosResistance + chaosDot;
            result.Add(("uncapped_chaos_damage_over_time_resistance_%", uncapped));
            result.Add(("chaos_damage_over_time_resistance_%", DamageModel.Capped(uncapped, pools.ChaosMaxResistance)));
        }
        return result;
    }

    /// <summary>Path of Building's <c>EvadeChanceCap</c>: the 5% minimum hit chance leaves evasion at most 95%.</summary>
    public const int EvadeChanceCap = 95;

    /// <summary>
    /// The Defence tab's estimate rows (issue #75), which the client derives from nothing: the server works them out
    /// against the reference monster of the character's level (DefaultMonsterStats, as Path of Building's CalcDefence
    /// does with its monster damage and accuracy tables).
    /// <list type="bullet">
    /// <item>Estimated Physical Damage reduction: armour against the reference hit (<see cref="DamageModel.ArmourReduction"/>,
    /// armour / (armour + 5 × damage)) plus the additional physical reduction the charges and passives give
    /// (<see cref="ChargeEffects.Reduction"/>), capped at 90%.</item>
    /// <item>Armour against Projectiles and its estimate, when "increased Armour against Projectiles" applies: the
    /// projectile increase joins the armour increases; <c>armour_differs_against_projectiles</c> makes the panel draw
    /// the rows.</item>
    /// <item>Estimated reduction against Hits, when "additional Physical Damage Reduction against Hits" applies
    /// (<c>armour_differs_against_hits</c>).</item>
    /// <item>Estimated chance to Evade Attacks: 100 − the reference monster's chance to hit this evasion
    /// (<see cref="DamageModel.EvadeChance"/>) plus flat "chance to Evade Attacks" sources, capped at 95%.</item>
    /// <item>Ward Restoration time (<c>ward_delay</c>, milliseconds) when the character has Ward.</item>
    /// <item>Mana Recharge per second when Energy Shield Recharge applies to Mana: the recharge rate on maximum mana.
    /// Nothing in 3.29 applies recharge to Life, so the Life row stays blank.</item>
    /// <item>Omniscience for attribute requirements: Omniscience × "can be satisfied by % of Omniscience".</item>
    /// </list>
    /// <paramref name="referenceDamage"/> and <paramref name="referenceAccuracy"/> are the reference monster's values;
    /// null (no DefaultMonsterStats row for the level) leaves the rows that need them out rather than estimating
    /// against a 0-damage, 0-accuracy attacker.
    /// </summary>
    public static List<(string Stat, int Value)> Defence(Func<string, int> get, CharacterStats.Pools pools,
        int? referenceDamage, int? referenceAccuracy)
    {
        var result = new List<(string, int)>();
        int additional = ChargeEffects.Reduction(get).Physical;
        static int Estimate(int armour, int damage, int extra) =>
            Math.Clamp(DamageModel.ArmourReduction(armour, damage) + Math.Max(0, extra), 0, DamageModel.DamageReductionCap);
        if (referenceDamage is int damage)
        {
            result.Add(("display_estimated_physical_damage_reduciton_%", Estimate(pools.Armour, damage, additional)));
            if (get("physical_damage_reduction_rating_+%_against_projectiles") is int projectilePct and not 0)
            {
                // The projectile-only increase joins the ordinary increases; the pools hold their total, so scale by ratio.
                long baseline = 100L + pools.ArmourPct;
                int armour = baseline <= 0 ? pools.Armour
                    : (int)Math.Clamp((long)pools.Armour * Math.Max(0, baseline + projectilePct) / baseline, 0, int.MaxValue);
                result.Add(("physical_damage_reduction_rating_against_projectiles", armour));
                result.Add(("armour_differs_against_projectiles", 1));
                result.Add(("display_estimated_physical_damage_reduciton_against_projectiles_%", Estimate(armour, damage, additional)));
            }
            if (get("additional_physical_damage_reduction_%_against_hits") is int againstHits and not 0)
            {
                result.Add(("armour_differs_against_hits", 1));
                result.Add(("display_estimated_physical_damage_reduction_against_hits_%",
                    Estimate(pools.Armour, damage, additional + againstHits)));
            }
        }
        if (referenceAccuracy is int accuracy)
            result.Add(("chance_to_evade_%", Math.Clamp(DamageModel.EvadeChance(pools.Evasion, accuracy)
                + get("chance_to_evade_attacks_%"), 0, EvadeChanceCap)));
        if (pools.Ward > 0)
            result.Add(("ward_delay", (int)Math.Clamp(Math.Round(WardState.Delay(get) * 1000), 0, int.MaxValue)));
        if (get("energy_shield_recharge_apply_to_mana") != 0)
            result.Add(("mana_recharge_rate_per_minute", RecoveryValue(EnergyShield.RechargePerMinute(pools.Mana, get))));
        if (get("attribute_requirements_can_be_satisfied_by_%_of_ascendance") is int omniscience and not 0)
            result.Add(("omniscience_for_attribute_requirements", (int)Math.Clamp(
                ((long)get("ascendance") + get("base_ascendance")) * omniscience / 100, int.MinValue, int.MaxValue)));
        return result;
    }

    /// <summary>Character.ot's <c>mana_regeneration_rate_per_minute_%</c>: 105, i.e. 1.75% of maximum mana per second.</summary>
    public const int BaseManaRegenPctPerMinute = 105;

    /// <summary>
    /// The Defence tab's regeneration rows (issue #75), as the per-minute totals the client divides by 60: life
    /// (<see cref="LifePerMinute"/>) and mana (<see cref="ManaPerMinute"/>). The same formulas feed the player tick, so
    /// the panel shows the regeneration the server actually applies. A character with no life regeneration gets no life
    /// row; mana always has its base rate. The client also needs life_recovery_per_minute / mana_recovery_per_minute
    /// for its own pool stepping: the Life constructor (0x141e678d0) binds these keys to the pools, and 0x141e6b320
    /// applies the rate / 60 times the client tick duration. The display totals alone do not drive that stepping.
    /// </summary>
    public static List<(string Stat, int Value)> Recovery(Func<string, int> get, uint maxLife, uint maxMana)
    {
        var result = new List<(string, int)>();
        double life = LifePerMinute(get, maxLife);
        if (life != 0)
        {
            result.Add(("total_life_recovery_per_minute_from_regeneration", RecoveryValue(life)));
            result.Add(("life_recovery_per_minute", RecoveryValue(life)));
        }
        int mana = RecoveryValue(ManaPerMinute(get, maxMana));
        result.Add(("total_mana_recovery_per_minute_from_regeneration", mana));
        result.Add(("mana_recovery_per_minute", mana));
        return result;
    }

    /// <summary>The recovery inputs with the "while moving" stats added while the player moves (Devout of Garukhan; PoB
    /// Condition Moving). Only the recovery finals read them; the raw inputs the player stats carry stay unchanged.</summary>
    public static Func<string, int> WhileMoving(Func<string, int> get, bool moving) => !moving ? get : name => get(name) + name switch
    {
        "life_regeneration_rate_per_minute_%" => get("life_regeneration_rate_per_minute_%_while_moving"),
        "mana_regeneration_rate_+%" => get("mana_regeneration_rate_+%_while_moving"),
        _ => 0,
    };

    static int RecoveryValue(double rate) => (int)Math.Clamp(Math.Round(rate), int.MinValue, int.MaxValue);

    /// <summary>Ordinary Life regeneration increases and recovery increases are distinct multiplicative buckets.
    /// Shared with the expiry-aware Fortification recovery writer so its added source follows the same rules.</summary>
    public static double LifeRegenerationMultiplier(Func<string, int> get) =>
        get("no_life_regeneration") != 0 || get("cannot_recover_life") != 0 ? 0 :
        Math.Max(0, 1 + get("life_regeneration_rate_+%") / 100.0)
        * Math.Max(0, 1 + get("life_recovery_rate_+%") / 100.0);

    /// <summary>Flat plus percentage Life regeneration, scaled by regeneration and recovery increases.
    /// PoB 3.29.x CalcDefence's regeneration calculation; conditional/conversion operators remain separate.</summary>
    public static double LifePerMinute(Func<string, int> get, uint maxLife) =>
        (get("base_life_regeneration_rate_per_minute")
            + maxLife * get("life_regeneration_rate_per_minute_%") / 100.0) * LifeRegenerationMultiplier(get);

    /// <summary>
    /// Character.ot's <see cref="BaseManaRegenPctPerMinute"/> plus <c>mana_regeneration_rate_per_minute_%</c> of maximum
    /// mana and flat <c>base_mana_regeneration_rate_per_minute</c> (Clarity), raised by regeneration then recovery increases.
    /// </summary>
    public static double ManaPerMinute(Func<string, int> get, uint maxMana) =>
        get("no_mana_regeneration") != 0 || get("cannot_leech_or_regenerate_mana") != 0 ? 0 :
        (maxMana * (BaseManaRegenPctPerMinute + (double)get("mana_regeneration_rate_per_minute_%")) / 100.0
            + get("base_mana_regeneration_rate_per_minute"))
        * Math.Max(0, 1 + get("mana_regeneration_rate_+%") / 100.0)
        * Math.Max(0, 1 + get("mana_recovery_rate_+%") / 100.0);
}
