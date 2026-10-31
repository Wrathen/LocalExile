using System.Net.Sockets;
using System.Collections.Frozen;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using Progression = GameServer.Core.World.Progression;
using DamageModel = GameServer.Core.World.DamageModel;
using DamagePacket = GameServer.Core.World.DamagePacket;

partial class Program
{
    // The player's granted skills: the default attack with the unarmed weapon's stats and every stat the server set on the
    // player since (a skill's stats include the character's; the client reads the skill's). A gem whose requirements the
    // character does not meet makes its item inactive (EquipmentStatSources) like a worn item the character is too low for,
    // so GemSkillEffects leaves its skill out: the client has nothing to cast. A use that arrives anyway (a stale client) is
    // refused (GemDisabled).
    internal static IReadOnlyList<GrantedSkill> PlayerSkills(ClientState conn)
    {
        // One build reads one snapshot of the character's stats, final stats and supported gems: every gem's cast time,
        // hit, crit and panel rows used to recompute them (CharacterPlayerStats, FinalPlayerStats, SupportedSkill) five or
        // six times per gem, 180-270 ms per build in the Debug server, inside the send gate. The snapshot also captures
        // the panel's raw sources for the selected-skill aliases (GemSkills).
        var character = CharacterPlayerStatsSnapshot(conn, out _, out var panelSources, capturePanelSources: true);
        var outer = StatsScope.Value;
        StatsScope.Value = new(conn, character);
        try
        {
            var player = FinalPlayerStatsFrom(conn, new(character)).ToList();
            return [.. SkillsFor(conn).PlayerDefaults(player), .. GemSkills(conn, player, character, panelSources!)];
        }
        finally { StatsScope.Value = outer; }
    }

    /// <summary>The PlayerSkills build running on this flow: what it computed once and reuses instead of recomputing.</summary>
    internal sealed class SkillBuildScope(ClientState conn, Dictionary<uint, int> stats)
    {
        public ClientState Conn => conn;
        public Dictionary<uint, int> Stats => stats;
        public List<(uint, int)>? Final;
        public readonly Dictionary<ushort, SupportedGem> Supported = [];
    }

    internal static readonly AsyncLocal<SkillBuildScope?> StatsScope = new();

    internal static SkillBuildScope? BuildScopeOf(ClientState conn) =>
        StatsScope.Value is { } scope && ReferenceEquals(scope.Conn, conn) ? scope : null;

    // The player's stats as the server sets them: what /set_stat set, then the attack's final values computed from them
    // (the client derives nothing itself: 11-skills.md §4).
    // FinalPlayerStats as a full 0x1b3, which only sets the stats it lists: every stat sent before that is gone now (a
    // stat only an unequipped item or a refunded passive gave) goes with 0, so the client does not keep its old value.
    internal static List<(uint, int)> PlayerStatsWithCleared(ClientState conn)
    {
        RefreshSingleUseCooldowns(conn);
        var stats = FinalPlayerStats(conn).ToList();
        var now = stats.Select(s => s.Item1).ToHashSet();
        lock (conn.Monsters)
        {
            stats.AddRange(conn.SentStatKeys.Where(k => !now.Contains(k)).Select(k => (k, 0)));
            conn.SentStatKeys = now;
            RefreshPeerScale(conn, stats.Single(s => s.Item1 == ActorScale.Final).Item2);
        }
        return stats;
    }

    internal const string LowestAttributeDamage = "damage_+%_per_5_of_your_lowest_attribute";
    internal static readonly (string Conditional, string Plain, string Higher, string Lower)[] AttributeAilmentConditions =
    [
        ("cannot_be_ignited_with_strength_higher_than_dex", "cannot_be_ignited", "strength", "dexterity"),
        ("cannot_be_frozen_with_dex_higher_than_int", "cannot_be_frozen", "dexterity", "intelligence"),
        ("cannot_be_shocked_with_int_higher_than_strength", "cannot_be_shocked", "intelligence", "strength"),
    ];

    internal static IEnumerable<(uint, int)> FinalPlayerStats(ClientState conn)
    {
        if (BuildScopeOf(conn) is not { } scope) return FinalPlayerStatsCore(conn);
        return [.. scope.Final ??= [.. FinalPlayerStatsCore(conn)]];
    }

    internal static IEnumerable<(uint, int)> FinalPlayerStatsCore(ClientState conn) =>
        FinalPlayerStatsFrom(conn, CharacterPlayerStats(conn));

    // The final stats from a character snapshot already worked out (PlayerSkills reuses its own).
    internal static IEnumerable<(uint, int)> FinalPlayerStatsFrom(ClientState conn, Dictionary<uint, int> stats)
    {
        // Quivers are gated by main_hand_weapon_type on the client, independently of the model's weapon stance. Only the
        // bow's value is known, so other weapons send nothing (a value /set_stat set stays); taking the bow off clears the
        // 9 sent before through PlayerStatsWithCleared.
        var mainHand = ActiveMainHand(conn);
        if (Stats.Key("main_hand_weapon_type") is uint weaponTypeKey && WeaponTypeStats.BowType(mainHand?.Type?.Class) is int bowType)
            stats.TryAdd(weaponTypeKey, bowType);
        // Both hands' weapon stats. A dual-wielded attack swings the off hand as well, and the client sets that strike's
        // weapon up from the off hand's own final stats (it derives nothing itself, 11-skills.md §4).
        foreach (var (k, v) in WeaponStats(conn, WeaponOf(conn, mainHand), stats)) stats[k] = v;
        foreach (var (k, v) in CosmeticStats(conn).Where(s => !ActorScale.IsInput(s.Stat))) stats.TryAdd(k, v);
        return stats.Select(kv => (kv.Key, kv.Value));
    }

    // The main hand's final weapon stats and, when the off hand holds a weapon, the off hand's too (off_hand_… instead of
    // main_hand_…). Until issue #167 the Gateway sent no off-hand stat at all, so the second weapon of a dual-wielded attack
    // had no attack time, damage or reach on the client; the off-hand animation of a Holy Strike on the ground then ran
    // with a null animation set and the next action crashed (0x141c988c1, AnimationController+0x180 = 0). Frida 2026-10-25:
    // while it creates the attack action the client reads off_hand_minimum/maximum_attack_distance.
    // Each hand's local speed is already in its own duration; both receive only the character's shared speed bucket.
    internal static List<(uint, int)> WeaponStats(ClientState conn, SkillCatalog.Unarmed weapon, IReadOnlyDictionary<uint, int> stats)
    {
        var conversion = DamageStats.Conversion(stats, Stats, attack: true);
        var mainHit = AttackHitConverted(weapon, stats, MeleeAttack(conn), conversion);
        var accuracy = OrdinaryAccuracy(conn, stats, weapon.LocalSources);
        double damageFactor = CriticalStrikePolicy.DamageFactor(CriticalProhibitedBy(stats, attack: true, own: null)
            ? 0 : conn.Pools.AttackCriticalStrikeChance, AttackCriticalMultiplier(conn, stats), CriticalExtraRollsFor(conn, stats),
            accuracy.Chance(Monsters.BaseEvasion(conn.Pools.Level) ?? 0, false, false));
        var list = weapon.Stats(stats, mainHit, damageFactor).ToList();
        AddHand(mainHit, "main_hand");
        if (OffhandWeaponFor(conn) is { } off)
        {
            var offHit = AttackHitConverted(off, stats, MeleeAttack(conn), conversion);
            list.AddRange(off.OffStats(stats, offHit));
            AddHand(offHit, "off_hand");
            // The hands alternate: the panel's per-use, per-second and attacks-per-second rows average both, each hand
            // weighted by its own critical chance and chance to hit (the Off Hand rows, OffHandPanelStats).
            int offSpeed = list.First(s => s.Item1 == StatKeys.OffHandAttackSpeedPct).Item2;
            var (perUse, perSecond, attacksPerSecond) = DualWieldPanel.Average(mainHit, weapon.AttackSeconds(stats),
                offHit, DualWieldPanel.OffHandSeconds(off, offSpeed), damageFactor,
                OffHandDamageFactor(conn, stats, n => Stats.Key(n) is uint key ? stats.GetValueOrDefault(key) : 0, own: null));
            Put(StatKeys.DisplayHundredTimesDamagePerSkillUse, perUse);
            Put(StatKeys.HundredTimesDamagePerSecond, perSecond);
            Put(StatKeys.HundredTimesAttacksPerSecond, attacksPerSecond);
        }
        return list;

        void Put(uint key, int value)
        {
            int index = list.FindIndex(s => s.Item1 == key);
            if (index >= 0) list[index] = (key, value);
            else list.Add((key, value));
        }

        void AddHand(GameServer.Core.World.DamagePacket hit, string hand)
        {
            foreach (var (stat, value) in PanelStats.HandDamage(hit, hand))
                if (Stats.Key(stat) is uint key) Put(key, value);
        }
    }

    // The off hand's critical-weighted, hit-chance-weighted damage factor for the dual-wield average, from the Off Hand
    // rows already worked out (OffHandPanelStats; rows: the published ones, or a selected gem's); null when no off hand
    // is swung, so the average falls back to the main hand's factor.
    internal static double? OffHandDamageFactor(ClientState conn, IReadOnlyDictionary<uint, int> player, Func<string, int> rows,
        IReadOnlyDictionary<uint, int>? own)
    {
        if (rows("skill_uses_off_hand") == 0) return null;
        return CriticalStrikePolicy.DamageFactor(CriticalProhibitedBy(player, attack: true, own) ? 0 : rows("off_hand_critical_strike_chance"),
            rows("off_hand_critical_strike_multiplier"), CriticalExtraRollsFor(conn, player, own), rows("off_hand_chance_to_hit_%"));
    }

    internal static AttackAccuracyProfile OrdinaryAccuracy(ClientState conn, IReadOnlyDictionary<uint, int> stats,
        IReadOnlyDictionary<string, int>? local)
    {
        int effect = SkillEffectId(conn, SkillCatalog.FirstSkillId) is { } id ? SkillsFor(conn).EffectRow(id) : -1;
        var own = SkillsFor(conn).EffectStats(effect, 1).ToDictionary(kv => kv.Item1, kv => kv.Item2);
        return AttackAccuracyProfile.From(conn.Pools, stats, Stats, own, local);
    }

    // What /set_stat set, then the class's attributes and pools and the passives' stats for the stats it did not set.
    internal static Dictionary<uint, int> CharacterPlayerStats(ClientState conn)
    {
        if (BuildScopeOf(conn) is { } scope) return new(scope.Stats);
        return CharacterPlayerStatsCore(conn, out _);
    }

    internal sealed record PanelSourceSnapshot(IReadOnlyDictionary<uint, int> Raw,
        IReadOnlySet<uint> Overrides, int PlayerLevel,
        IReadOnlyDictionary<string, int> MainHand, IReadOnlyDictionary<string, int> OffHand);

    internal static Dictionary<uint, int> CharacterPlayerStatsCore(ClientState conn, out double baselineLifePerSecond) =>
        CharacterPlayerStatsSnapshot(conn, out baselineLifePerSecond, out _);

    internal static Dictionary<uint, int> CharacterPlayerStatsSnapshot(ClientState conn, out double baselineLifePerSecond,
        out PanelSourceSnapshot? panelSources, bool capturePanelSources = false)
    {
        EnsureAntiquarianPools(conn);
        EnsureOwnAuraPools(conn);
        Dictionary<uint, int> rawOverrides;
        lock (conn.Monsters) rawOverrides = new(conn.PlayerStats);
        var stats = new Dictionary<uint, int>(rawOverrides);
        // Values and membership describe the same snapshot. A new command must not add a replacement key
        // after values were copied and fault a background recovery/condition-refresh task.
        var devSet = rawOverrides.Keys.ToHashSet();
        foreach (var (k, v) in BaseStats(conn)) stats.TryAdd(k, v);
        EnsureFortificationPools(conn);
        // The overcapped lightning resistance's increased crit (PoolsFor derived it once into Pools): the spell
        // consumers read the raw increased stats here (SpellCriticalStrike), while the attack and panel chances
        // already fold the same value into the pools. Additive with whatever won above, as a derived stat source.
        if (conn.Pools.LightningOvercapCritPct is int overCrit and not 0
            && Stats.Key("critical_strike_chance_+%") is uint critInc)
            stats[critInc] = (int)Math.Clamp((long)stats.GetValueOrDefault(critInc) + overCrit, int.MinValue, int.MaxValue);
        // The passives' conditional attack speed is already one final value in Pools; /set_stat's raw attack-speed stats
        // stay additive. main_hand_attack_speed_+% is the total the client swings with, so Unarmed reads only it.
        int devAttackSpeed = 0;
        foreach (uint key in StatKeys.AttackSpeedInputs)
            devAttackSpeed += rawOverrides.GetValueOrDefault(key);
        stats[StatKeys.MainHandAttackSpeedPct] = conn.Pools.AttackSpeedPct + devAttackSpeed;
        // The utility flasks' plain stats (UtilityFlasks.RawStats: maximum resistances, crit, damage, cast speed), with
        // Mageblood's permanent flasks deduplicated against the running ones, plus the flask grounds.
        int FlaskGet(string name) => Stats.Key(name) is uint k ? stats.GetValueOrDefault(k) : 0;
        UtilityFlasks.AddRawStats(stats, UtilityStats(conn, FlaskGet).Concat(conn.FlaskGrounds.PlayerStats()), Stats);
        WithRage(conn, stats);
        // The character panel's sums of base forms (PanelStats); a stat /set_stat set keeps its value.
        ActorScale.Project(stats, rawOverrides, CosmeticScaleSources(conn));
        int Get(string name) => Stats.Key(name) is uint k ? stats.GetValueOrDefault(k) : 0;
        ConfigureFortificationRecovery(conn, conn.Pools.Life, Get);
        ConfigureLeech(conn, Get, rawOverrides, CriticalOnLowLife(conn, stats));
        WithLeechConditions(conn, stats);
        // Capture the unmodified recovery baseline from this same snapshot, before the temporary contribution.
        // The resource writer integrates Fortification separately across exact expiry boundaries.
        baselineLifePerSecond = PanelStats.LifePerMinute(Get, conn.Pools.Life) / 60;
        lock (conn.Monsters)
        {
            // Turtle Shell's exact one-second integral is consumed separately by the resource writer.
            if (Stats.Key("base_life_regeneration_rate_per_minute") is uint turtle)
                stats[turtle] = (int)Math.Clamp(stats.GetValueOrDefault(turtle)
                    + (conn.Antiquarian.RecoveryPerSecond(conn.Clock.Elapsed.TotalSeconds)
                        + conn.EnduringCryRecovery.Rate(conn.Clock.Elapsed.TotalSeconds)) * 60, 0, int.MaxValue);
            var session = FortificationOf(conn);
            var value = session.Read(conn.Clock.Elapsed.TotalSeconds);
            AddFortificationCounts(stats, value);
            if (Stats.Key(FortificationSession.Regeneration) is uint regen && !devSet.Contains(regen))
                stats[regen] = (int)Math.Clamp((long)stats.GetValueOrDefault(regen)
                    + value.LifeRegenerationPerMinutePercent(session.RegenPerStack), int.MinValue, int.MaxValue);
        }
        var (mainLocal, offLocal) = HandLocalStats(conn);
        // Capture only for a skill publication. Recovery-only callers avoid these extra immutable copies.
        panelSources = capturePanelSources
            ? new(stats.ToFrozenDictionary(), devSet.ToFrozenSet(), conn.Pools.Level,
                mainLocal.ToFrozenDictionary(), offLocal.ToFrozenDictionary()) : null;
        foreach (var (stat, value) in PanelStats.Compute(Get, n => mainLocal.GetValueOrDefault(n), n => offLocal.GetValueOrDefault(n),
            playerLevel: conn.Pools.Level)
            .Concat(PanelStats.FromPools(Get, conn.Pools))
            .Concat(PanelStats.Defence(Get, conn.Pools, Monsters.BaseDamage(conn.Pools.Level), Monsters.BaseAccuracy(conn.Pools.Level)))
            .Concat(PanelStats.Recovery(Get, conn.Pools.Life, conn.Pools.Mana))
            .Concat(LeechLimits.Compute(Get, conn.Pools.Life, conn.Pools.Mana, conn.Pools.EnergyShield,
                name => Stats.Key(name) is uint key && rawOverrides.TryGetValue(key, out int value) ? value : null,
                CriticalOnLowLife(conn, stats)))
            .Concat(FlaskCharges.GainStats(Get))
            .Concat(new[] { ("energy_shield_recovery_per_minute", (int)Math.Round(EnergyShield.PredictionPerMinute(conn.Pools.EnergyShield, Get, conn.EnergyShield.Recharging))),
                ("total_energy_shield_recovery_per_minute_from_regeneration", (int)Math.Round(EnergyShield.RegenPerMinute(conn.Pools.EnergyShield, Get))),
                ("energy_shield_recharge_rate_per_minute", (int)Math.Round(EnergyShield.RechargePerMinute(conn.Pools.EnergyShield, Get))),
                ("energy_shield_recharge_active", 0) })
            .Concat(ChargePanelStats.Compute(Get)))
            if (Stats.Key(stat) is uint key && !devSet.Contains(key)) stats[key] = value;
        // Special maxima mirror completed standard maxima, including explicit final replacements.
        foreach (var (stat, value) in ChargePanelStats.MirroredMaximums(Get))
            if (Stats.Key(stat) is uint key && !devSet.Contains(key)) stats[key] = value;
        // Reap's Blood Charges: the count held, the maximum and the rows' gate (BloodChargePanelRows).
        foreach (var (stat, value) in BloodChargePanelRows(conn, Get))
            if (Stats.Key(stat) is uint key && !devSet.Contains(key)) stats[key] = value;
        // Derive once from the resolved final rating, after pools and explicit stat replacements. Only this
        // temporary dictionary changes; repeated builds and removal cannot feed the bonus back into raw sources.
        if ((Get(ArmourAttackDamage.Per450Armour) != 0 || Get(ArmourAttackDamage.Per450Evasion) != 0)
            && Stats.Key("attack_damage_+%") is uint attackDamage)
            stats[attackDamage] = ArmourAttackDamage.Total(Get("attack_damage_+%"),
                Get("physical_damage_reduction_rating"), Get(ArmourAttackDamage.Per450Armour),
                Get("evasion_rating"), Get(ArmourAttackDamage.Per450Evasion));
        // Whaletooth Bangle uses complete groups of final Strength. Derive from this fresh snapshot once; never
        // write back into raw sources. An explicit final-duration /set_stat keeps its usual replacement semantics.
        if (Stats.Key(SkillDurationModifiers.IncreasedDuration) is uint durationKey && !devSet.Contains(durationKey))
            stats[durationKey] = SkillDurationModifiers.Total(Get(SkillDurationModifiers.IncreasedDuration),
                Get("strength"), Get(SkillDurationModifiers.PerTenStrength));
        // Dreadful Attitude: increased damage per complete 5 of the lowest final attribute, from this snapshot only.
        if (Get(LowestAttributeDamage) is int perFive and not 0 && Stats.Key("damage_+%") is uint damageKey && !devSet.Contains(damageKey))
            stats[damageKey] = (int)Math.Clamp(Get("damage_+%") + (long)perFive
                * (Math.Min(Get("strength"), Math.Min(Get("dexterity"), Get("intelligence"))) / 5), int.MinValue, int.MaxValue);
        // Aristocrat: "cannot be X if A is higher than B" becomes the plain cannot_be_X while it holds (strictly higher),
        // which the Defence tab shows and the ailment consumer reads.
        foreach (var (conditional, plain, higher, lower) in AttributeAilmentConditions)
            if (Get(conditional) != 0 && Get(higher) > Get(lower) && Stats.Key(plain) is uint plainKey && !devSet.Contains(plainKey))
                stats[plainKey] = 1;
        // The Offence tab's per-hand damage rows (issue #75): the hit the default attack actually swings with, so the panel
        // agrees with the damage the server applies. Only the main hand: the server attacks with it. Those rows are gated by
        // skill_uses_main_hand, which nothing sent before, so the numbers alone would leave them blank.
        if (Stats.Key("skill_uses_main_hand") is uint usesMainHand && !devSet.Contains(usesMainHand)) stats[usesMainHand] = 1;
        var attackConversion = DamageStats.Conversion(stats, Stats, attack: true);
        var attack = AttackHitConverted(WeaponFor(conn), stats, MeleeAttack(conn), attackConversion);
        foreach (var (stat, value) in PanelStats.HandDamage(attack, "main_hand"))
            if (Stats.Key(stat) is uint key && !devSet.Contains(key)) stats[key] = value;
        // The default attack's damaging-ailment rows per hand (AilmentPanelStats), from the same raw packet and
        // conversion as its hit; an off hand that is not swung keeps no stale rows.
        var ailmentContext = new AilmentPanelStats.Context("main_hand", Spell: false, Melee: MeleeAttack(conn), Projectile: !MeleeAttack(conn));
        var ailmentRows = AilmentPanelStats.Compute(ailmentContext, SupportedAttackDamage.Raw(WeaponFor(conn), Get), attackConversion, Get);
        if (OffhandWeaponFor(conn) is { } offWeapon)
            ailmentRows.AddRange(AilmentPanelStats.Compute(ailmentContext with { Hand = "off_hand" },
                SupportedAttackDamage.Raw(offWeapon, Get), attackConversion, Get));
        foreach (var (stat, value) in ailmentRows)
            if (Stats.Key(stat) is uint key && !devSet.Contains(key)) stats[key] = value;
        var ailments = conn.ElementalAilments.Snapshot(conn.Clock.Elapsed.TotalSeconds);
        foreach (var (name, value) in new[] { ("is_ignited", ailments.Ignite ? 1 : 0),
            ("is_chilled", ailments.Chill > 0 ? 1 : 0), ("is_frozen", ailments.Freeze ? 1 : 0),
            ("is_shocked", ailments.Shock > 0 ? 1 : 0), ("action_speed_-%", (int)Math.Round(100 * (1 - conn.ElementalAilments.ActionFactor(conn.Clock.Elapsed.TotalSeconds)))),
            ("intermediary_combined_action_speed_+%", (int)Math.Round(-100 * (1 - conn.ElementalAilments.ActionFactor(conn.Clock.Elapsed.TotalSeconds)))) })
            if (Stats.Key(name) is uint key && value != 0) stats[key] = stats.GetValueOrDefault(key) + value;
        // The conditions as they are now; the player tick sends the chance again when they change.
        int suppression = Suppression(conn).Chance;
        foreach (uint key in SuppressionChanceKeys) stats[key] = suppression;
        if (Stats.Key("main_hand_critical_strike_multiplier") is uint multiplier)
            stats[multiplier] = AttackCriticalMultiplier(conn, stats);
        var accuracy = OrdinaryAccuracy(conn, stats, mainLocal);
        foreach (var (name, evasion) in new[]
        {
            ("main_hand_chance_to_hit_%", Monsters.BaseEvasion(conn.Pools.Level) ?? 0),
            ("main_hand_chance_to_hit_evasive_monsters_%", Monsters.BaseEvasiveEvasion(conn.Pools.Level) ?? 0),
        })
            if (Stats.Key(name) is uint key && !devSet.Contains(key))
                stats[key] = accuracy.Chance(evasion, false, false);
        // The Off Hand rows of a dual-wielded default attack; the flag clears when nothing is swung with the off hand.
        if (Stats.Key("skill_uses_off_hand") is uint usesOffHand && !devSet.Contains(usesOffHand)) stats[usesOffHand] = 0;
        foreach (var (name, value) in OffHandPanelStats(conn, stats, mainLocal, offLocal, stats.GetValueOrDefault(StatKeys.MainHandAttackSpeedPct)) ?? [])
            if (Stats.Key(name) is uint key && !devSet.Contains(key)) stats[key] = value;
        if (Stats.Key("virtual_on_low_life") is uint lowLife)
            stats[lowLife] = CriticalOnLowLife(conn, stats) ? 1 : 0;
        if (Stats.Key("virtual_extra_critical_rolls") is uint rolls)
            stats[rolls] = CriticalExtraRollsFor(conn, stats);
        // The Offence tab's effective (and twice-) critical chance for spells: the pools' chance after the luck and
        // bifurcation policies; a selected spell gets its own (GemSkills).
        foreach (var (stat, value) in SpellEffectiveCritical(conn, stats, conn.Pools.SpellCriticalStrikeChance, own: null))
            if (Stats.Key(stat) is uint key && !devSet.Contains(key)) stats[key] = value;
        stats[OwnAuraRestriction.Disabled] = OwnAuraRestrictionValue(conn);
        if (Get("global_always_hit") != 0)
            foreach (string name in new[] { "main_hand_chance_to_hit_%", "off_hand_chance_to_hit_%",
                "main_hand_chance_to_hit_evasive_monsters_%", "off_hand_chance_to_hit_evasive_monsters_%" })
                if (Stats.Key(name) is uint hit) stats[hit] = 100;
        lock (conn.Monsters)
        {
            AddRecentEnemyHitStatsLocked(conn, stats);
            if (Stats.Key("ward") is uint ward) stats[ward] = (int)conn.Ward.Maximum;
            if (Stats.Key("ward_broken") is uint broken) stats[broken] = conn.Ward.Broken ? 1 : 0;
        }
        return stats;
    }

    // spell_effective_total_chance_permyriad_for_hit_to_be_critical / _double_critical: the spell chance after the
    // luck policy (CriticalExtraRollsFor) and the Assassin's bifurcation (spell_critical_strikes_bifurcate); zero when
    // critical strikes are prohibited, as the chance row itself is.
    internal static IEnumerable<(string Stat, int Value)> SpellEffectiveCritical(ClientState conn, IReadOnlyDictionary<uint, int> player,
        int chance, IReadOnlyDictionary<uint, int>? own)
    {
        bool Flag(string name) => Stats.Key(name) is uint key
            && (player.GetValueOrDefault(key) != 0 || (own?.GetValueOrDefault(key) ?? 0) != 0);
        int allowed = CriticalProhibitedBy(player, attack: false, own) ? 0 : chance;
        int extraRolls = CriticalExtraRollsFor(conn, player, own);
        bool bifurcate = Flag("spell_critical_strikes_bifurcate");
        yield return ("spell_effective_total_chance_permyriad_for_hit_to_be_critical",
            CriticalStrikePolicy.EffectiveChance(allowed, extraRolls, bifurcate));
        yield return ("spell_effective_total_chance_permyriad_for_hit_to_be_double_critical",
            CriticalStrikePolicy.DoubleCriticalChance(allowed, extraRolls, bifurcate));
    }

    // The melee context of an attack's damage modifiers. An attack gem classifies
    // its own ActiveSkillType Melee; the default attack and the panel swing the active weapon set's weapon where an
    // inactive weapon is not there at all (WeaponProfileFor) and unarmed swings are melee.
    internal static bool MeleeAttack(ClientState conn, int gemEffect = -1) =>
        gemEffect >= 0 && SkillsFor(conn).IsAttack(gemEffect) ? SkillsFor(conn).IsMelee(gemEffect)
            : WeaponProfileFor(conn, activeSet: true).Melee;

    // Conversion for one hit: the player's stats plus the skill's own stats (base/skill conversion). Player stats win on
    // a key collision; the normal case is that the skill adds stats the player does not have.
    internal static DamagePacket AttackHit(SkillCatalog.Unarmed weapon, IReadOnlyDictionary<uint, int> stats, bool melee) =>
        AttackHitConverted(weapon, stats, melee, null);

    // Resolve conversion history and modifiers from raw local/added damage, then apply Rage once.
    internal static DamagePacket AttackHitConverted(SkillCatalog.Unarmed weapon, IReadOnlyDictionary<uint, int> stats,
        bool melee, GameServer.Core.World.DamageConversion? conversion)
    {
        int Get(string name) => RageStat(stats, name);
        var hit = PlayerHitDamage.Resolve(SupportedAttackDamage.Raw(weapon, Get), Get,
            conversion ?? new(), spell: false, melee: melee, projectile: !melee, wieldingWand: weapon.WieldingWand);
        return RageStats.ApplyAttack(hit, Get).Scale(GamblerFor(stats).DamageFactor);
    }

    internal static double AddedEffectiveness(ClientState conn, int effect, int level) => SkillsFor(conn).DamageEffectiveness(effect, level) is int value and > 0 ? value / 10000.0 : 1;

    internal static GameServer.Core.World.DamageConversion ConversionFor(ClientState conn, int effectRow, int level, bool attack)
    {
        return DamageStats.Conversion(CharacterPlayerStats(conn),
            effectRow >= 0 ? SkillsFor(conn).EffectStats(effectRow, level) : [], Stats, attack);
    }

    // The character's own stats: the class's attributes and the pools (ClassAttributes), then every other stat of the
    // allocated passives, summed (increased life and the attributes are already in the pools; the rest, such as increased
    // spell damage, the client only shows).
    internal static IEnumerable<(uint, int)> BaseStats(ClientState conn)
    {
        var seen = new HashSet<uint>();
        foreach (var (k, v) in ClassAttributes(conn.ClassRow, conn.Pools)) if (seen.Add(k)) yield return (k, v);
        List<ushort> nodes;
        lock (conn.Monsters) nodes = [.. conn.Passives];
        var extra = CharacterStatSources(conn, nodes).Stats;
        int RawDefault(string name) => Stats.Key(name) is uint key ? extra.GetValueOrDefault(key) : 0;
        foreach (var (stat, value) in PanelStatDefaults.Compute(RawDefault))
            if (Stats.Key(stat) is uint key) extra[key] = value;
        if (conn.God)
        {
            foreach (var (stat, value) in GodStats)
                if (Stats.Key(stat) is uint key) extra[key] = extra.GetValueOrDefault(key) + value;
            // the final value the client shows: 100% of maximum life per second, per minute
            if (Stats.Key("life_regeneration_rate_per_minute") is uint lr) extra[lr] = (int)Math.Min(int.MaxValue, (long)conn.Pools.Life * GodLifeRegenPctPerSecond / 100 * 60);
        }
        List<AuraCatalog.Aura> auras;
        lock (conn.Monsters) auras = [.. conn.Auras.Values];
        foreach (var (k, v) in AuraCatalog.RawStats(auras, conn.Pools.AuraEffectPct, SkillsFor(conn), Stats))
            extra[k] = ActorScale.Saturate((long)extra.GetValueOrDefault(k) + v);
        // The mods' stats (Program.Mods.cs), added like /god's: the pools (life, mana) are not rebuilt from them.
        if (ServerHooks.ChangesStats)
            foreach (var (stat, value) in ServerHooks.Stats(PlayerOf(conn)))
                if (Stats.Key(stat) is uint key) extra[key] = ActorScale.Saturate((long)extra.GetValueOrDefault(key) + value);
        foreach (var (k, v) in extra) if (seen.Add(k)) yield return (k, v);
    }

    // The class's attributes (Characters.dat BaseStrength / BaseDexterity / BaseIntelligence) as final stats: strength,
    // dexterity and intelligence for the character panel, and virtual_*_for_requirements, which item requirements are
    // checked against (0x1402ce5f0 via the Player cache 0x141e80340, keys 0x3d1b-0x3d1d; global_no_attribute_requirements
    // skips the check, global_item_attribute_requirements_+% scales it).
    // With the level-based pools (CharacterStats): level, maximum_life / maximum_mana (and their base_ forms),
    // accuracy_rating, physical_damage_reduction_rating (armour) and evasion_rating (base_evasion_rating 15), as final
    // values, and the attributes' increases: armour/evasion_rating_+%, maximum_energy_shield_+% (intelligence) and
    // melee_physical_damage_+% (strength). The Offence panel's accuracy row reads main_hand_accuracy_rating
    // (CharacterPanelStats MH_Accuracy); its Chance to
    // Hit rows read main_hand_chance_to_hit_% and main_hand_chance_to_hit_evasive_monsters_% directly. The reference is a
    // monster of the character's level (the normal and high-dexterity evasion rows of DefaultMonsterStats).
    internal static IEnumerable<(uint, int)> ClassAttributes(int classRow, CharacterStats.Pools pools)
    {
        int referenceEvasion = Monsters.BaseEvasion(pools.Level) ?? 0;
        int referenceEvasiveEvasion = Monsters.BaseEvasiveEvasion(pools.Level) ?? 0;
        int chanceToHit = DamageModel.AccuracyHitChance(referenceEvasion, pools.Accuracy);
        int evasiveChanceToHit = DamageModel.AccuracyHitChance(referenceEvasiveEvasion, pools.Accuracy);
        foreach (var (stat, value) in new[] { ("level", pools.Level), ("base_maximum_life", (int)pools.Life), ("maximum_life", (int)pools.Life),
            ("base_maximum_mana", (int)pools.Mana), ("maximum_mana", (int)pools.Mana), ("accuracy_rating", pools.Accuracy),
            ("main_hand_accuracy_rating", pools.Accuracy), ("off_hand_accuracy_rating", pools.Accuracy),
            ("main_hand_chance_to_hit_%", chanceToHit), ("off_hand_chance_to_hit_%", chanceToHit),
            ("main_hand_chance_to_hit_evasive_monsters_%", evasiveChanceToHit),
            ("off_hand_chance_to_hit_evasive_monsters_%", evasiveChanceToHit),
            ("character_sheet_reference_evasion", referenceEvasion),
            ("character_sheet_reference_high_dex_evasion", referenceEvasiveEvasion),
            ("physical_damage_reduction_rating", pools.Armour), ("physical_damage_reduction_rating_+%", pools.ArmourPct),
            ("base_evasion_rating", CharacterStats.BaseEvasion), ("evasion_rating", pools.Evasion), ("evasion_rating_+%", pools.EvasionPct),
            ("maximum_energy_shield", (int)pools.EnergyShield), ("maximum_energy_shield_+%", pools.EnergyShieldPct), ("melee_physical_damage_+%", pools.MeleePhysicalDamagePct),
            ("movement_velocity_+%", pools.MovementVelocityPct),
            ("combined_base_block_%", pools.BlockChanceBase), ("attack_block_%", pools.BlockChance),
            // The character panel prints attack block as "capped% (uncapped%)"; the client does not derive the second value.
            ("uncapped_attack_block_%", pools.BlockChanceUncapped),
            // A separate spell block row is only drawn when display_spell_block_chance is non-zero.
            ("spell_block_%", pools.SpellBlockChance), ("display_spell_block_chance", pools.SpellBlockChance),
            ("base_spell_block_%", 0),
            // Resistance display: the client shows fire_damage_resistance_% as the capped value and
            // uncapped_fire_damage_resistance_% (and the other three) in parentheses as the overcap. The passives' base
            // resistances are already summed into the pools, so zero the all-elements input to avoid counting it twice. The
            // maximum-resistance passives stay in BaseStats: the client adds them to its own 75 base. Do not send maximum_*
            // here; those keys are additive maximum-resistance modifiers, so sending 75 would count the base twice.
            ("base_resist_all_elements_%", 0),
            ("base_fire_damage_resistance_%", pools.FireResistance),
            ("fire_damage_resistance_%", DamageModel.Capped(pools.FireResistance, pools.FireMaxResistance)),
            ("uncapped_fire_damage_resistance_%", pools.FireResistance),
            ("base_cold_damage_resistance_%", pools.ColdResistance),
            ("cold_damage_resistance_%", DamageModel.Capped(pools.ColdResistance, pools.ColdMaxResistance)),
            ("uncapped_cold_damage_resistance_%", pools.ColdResistance),
            ("base_lightning_damage_resistance_%", pools.LightningResistance),
            ("lightning_damage_resistance_%", DamageModel.Capped(pools.LightningResistance, pools.LightningMaxResistance)),
            ("uncapped_lightning_damage_resistance_%", pools.LightningResistance),
            ("base_chaos_damage_resistance_%", pools.ChaosResistance),
            ("chaos_damage_resistance_%", DamageModel.Capped(pools.ChaosResistance, pools.ChaosMaxResistance)),
            ("uncapped_chaos_damage_resistance_%", pools.ChaosResistance),
            // The character panel reads the final suppression values, not only the raw passive input.
            ("spell_suppression_chance_%", pools.SpellSuppressionChance),
            ("base_spell_suppression_chance_%", pools.SpellSuppressionChance),
            ("combined_spell_suppression_chance_%", pools.SpellSuppressionChance),
            ("spell_damage_%_suppressed", pools.SpellSuppressionEffect),
            ("base_spell_damage_%_suppressed", pools.SpellSuppressionEffect),
            // The Offence panel's crit rows read these final values (permyriad for chance, percent for multiplier).
            ("main_hand_critical_strike_chance", pools.AttackCriticalStrikeChance),
            ("main_hand_critical_strike_multiplier", pools.CriticalStrikeMultiplier),
            ("spell_critical_strike_chance", pools.SpellCriticalStrikeChance),
            ("spell_critical_strike_multiplier", pools.SpellCriticalStrikeMultiplier) })
            if (Stats.Key(stat) is uint key) yield return (key, value);
        // Calculated attributes include legitimate all-zero values. Legacy partial pool records without attributes
        // still fall back to the class's own; record clones preserve the explicit resolved marker.
        var c = Skills.ClassFor(classRow);
        var (str, dex, intel) = pools.AttributesResolved || pools.Strength != 0 || pools.Dexterity != 0 || pools.Intelligence != 0
            ? (pools.Strength, pools.Dexterity, pools.Intelligence) : (c.Strength, c.Dexterity, c.Intelligence);
        foreach (var (stat, value) in new[] { ("strength", str), ("dexterity", dex), ("intelligence", intel),
            ("virtual_strength_for_requirements", str), ("virtual_dexterity_for_requirements", dex),
            ("virtual_intelligence_for_requirements", intel) })
            if (Stats.Key(stat) is uint key) yield return (key, value);
    }

    // The skills of the gems socketed in the equipped items: each gem's GrantedEffects row at the gem's level with its own stats
    // (SkillCatalog.EffectStats: damage, cast time, mana cost) after the player's, with ids after the default attack's that
    // no basic action uses (SkillCatalog.SkillIds).
    internal static IEnumerable<GrantedSkill> GemSkills(ClientState conn, IEnumerable<(uint, int)> player,
        IReadOnlyDictionary<uint, int> characterInputs, PanelSourceSnapshot panelSources)
    {
        string? character = conn.Character;
        var playerStats = new Dictionary<uint, int>();
        foreach (var (key, value) in player) playerStats[key] = value;
        var gems = GemSkillEffects(conn).ToArray();
        var singleSources = gems.GroupBy(g => g.Effect).Where(g => g.Count() == 1).Select(g => g.Key).ToHashSet();
        var skillCosmetics = CharacterSaves.SkillCosmetics(string.IsNullOrEmpty(character) ? "Exile" : character);
        foreach (var (id, effect, name, _, quality, gem) in gems)
        {
            var supported = SupportedSkill(conn, id);
            int level = supported.Level;
            var own = supported.Stats;
            var stats = MergeSkillStats(playerStats, own);
            if (Stats.Key("virtual_extra_critical_rolls") is uint criticalRolls)
                stats[criticalRolls] = CriticalExtraRollsFor(conn, playerStats, own);
            if (singleSources.Contains(effect) && SingleUseCooldown.ForSkill(SkillsFor(conn), Stats, effect, level, playerStats, own) is { } cooldown
                && Stats.Key("virtual_cooldown_speed_+%") is uint recoveryKey)
                stats[recoveryKey] = cooldown.RecoveryPercent;
            SkillAttackTiming.Apply(SkillsFor(conn), effect, level, stats);
            // The client reads the final duration, not its base: its own Spark sparks live skill_effect_duration ms
            // (0x141b35b7a) and without it died on their first tick, so none was drawn.
            if (Stats.Key("base_skill_effect_duration") is uint baseDurationKey && own.GetValueOrDefault(baseDurationKey) > 0
                && Stats.Key("skill_effect_duration") is uint durationKey)
                stats[durationKey] = (int)Math.Round(1000 * SkillDurationModifiers.Seconds(own[baseDurationKey],
                    name => Stats.Key(name) is uint k ? playerStats.GetValueOrDefault(k) : 0,
                    name => Stats.Key(name) is uint k ? own.GetValueOrDefault(k) : 0));
            if (SkillsFor(conn).IsSpell(effect) && !SkillsFor(conn).IsAttack(effect) && Stats.Key("spell_critical_strike_chance") is uint critKey)
                stats[critKey] = SupportedCriticalChance(conn, id);
            // The skill effects worn in the gem's Cosmetics tab slots: their stats (skill_art_variation …) on this skill only.
            foreach (var (key, worn) in skillCosmetics)
                if (CharacterSaves.SkillCosmeticGem(key) == gem && Mtx.Find(worn.Record) is { } record && MtxEffects.Of(record.MtxTypeHash) is { } fx)
                    foreach (var (k, v) in fx.Stats) stats[k] = v;
            // The native spell action divides virtual BASE time by cast speed itself. Keep the catalog base
            // intact; only the panel's final casts-per-second stat is adjusted here.
            int supportCastSpeed = Stats.Key("base_cast_speed_+%") is uint cs ? own.GetValueOrDefault(cs) : 0;
            if (SpellCastTiming.ForSkill(SkillsFor(conn), effect, CastSpeedPct(stats) + supportCastSpeed) is { } spellTiming)
            {
                spellTiming.ApplyTo(stats, Stats);
            }
            // Preserve the legacy non-spell/attack display path; attack timing is outside this fix.
            else if ((CastSpeedPct(stats) + supportCastSpeed) is int castSpeed and not 0 && SkillsFor(conn).CastMs(effect) is int castMs and > 0)
            {
                int ms = (int)Math.Round(castMs / (1 + castSpeed / 100.0));
                if (Stats.Key("virtual_base_spell_cast_time_ms") is uint vk) stats[vk] = ms;
                if (Stats.Key("hundred_times_casts_per_second") is uint hk) stats[hk] = (int)Math.Round(100_000.0 / Math.Max(1, ms));
            }
            // A spell's tooltip DPS is its own hit (SpellDamage) × casts per second, not the default attack's values that
            // came with the player's stats.
            int Get(string name) => Stats.Key(name) is uint k ? stats.GetValueOrDefault(k) : 0;
            // Cyclone's client effect reads the final radius, not the increased-area input. Match AreaSkillHit.
            if (SkillArea.IsCyclone(SkillsFor(conn).EffectId(effect)))
            {
                // The player's base area is in the panel total already (AreaPlayerStats): only the gem's own counts here.
                int AreaGet(string name) => name == SkillArea.BaseIncreased ? (Stats.Key(name) is uint k ? own.GetValueOrDefault(k) : 0) : Get(name);
                int baseRadius = SkillArea.BaseRadius(SkillsFor(conn).EffectId(effect), AreaGet);
                int increased = SkillArea.Increased(AreaGet, spell: false);
                int radiusPercent = SkillArea.RadiusPercent(AreaGet, spell: false);
                int radius = SkillArea.Radius(baseRadius, AreaGet, spell: false);
                if (Stats.Key("active_skill_base_area_of_effect_radius") is uint baseKey) stats[baseKey] = baseRadius;
                if (Stats.Key("active_skill_area_of_effect_radius") is uint radiusKey)
                    stats[radiusKey] = radius;
                // Channelled Cyclone's conditional-radius path reads these derived stats instead of 0x4876.
                // Compute them before writing skill_area_of_effect_+%, or the base increase would count twice.
                if (Stats.Key("total_skill_area_of_effect_radius_+%_including_final") is uint scaleKey) stats[scaleKey] = radiusPercent;
                if (Stats.Key("skill_area_of_effect_+%") is uint areaKey) stats[areaKey] = increased;
                Console.WriteLine($"[gateway] Cyclone area: base {baseRadius}, radius {radius}, increased area {increased}%, radius scale {radiusPercent}%");
            }
            // A trap or herald that deals secondary damage (Bear Trap carries the Spell type but no spell base damage,
            // Herald of Ice's shatter): its own secondary rows, before the attack/spell hit rows.
            if (!SkillsFor(conn).IsAttack(effect) && Get("display_skill_deals_secondary_damage") != 0
                && !SkillHitPlan.ForSkill(SkillsFor(conn), effect, supported.Level, supported.Quality).CanHit
                && SkillsFor(conn).SecondaryHitDamage(effect, supported.Level, supported.Quality) is { IsZero: false } secondaryBase)
            {
                foreach (var (stat, value) in SecondaryPanelRows(conn, Get, playerStats, supported, secondaryBase))
                    if (Stats.Key(stat) is uint key)
                        stats[key] = panelSources.Overrides.Contains(key) ? panelSources.Raw.GetValueOrDefault(key) : value;
            }
            else if (SkillsFor(conn).IsAttack(effect) || SkillsFor(conn).IsSpell(effect))
            {
                // One read of the gem's hit inputs serves its hit, its ailment rows and (dual-wielded) the off hand's.
                // Reap's more damage per Blood Charge held scales the hit itself, as AreaSkillHit does, so the damage
                // ranges, the ailment rows and the per-use/per-second rows agree.
                double bloodMore = BloodChargeDamageMore(conn, name => Stats.Key(name) is uint k ? own.GetValueOrDefault(k) : 0);
                var inputs = SupportedHitInputsFor(conn, id);
                SupportedHitInputs? offInputs = null;
                if (inputs is not null && bloodMore != 1) inputs = inputs with { Raw = inputs.Raw.Scale(bloodMore) };
                var supportedHit = inputs is null ? GameServer.Core.World.DamagePacket.Empty : SupportedHitFrom(inputs, suppress: true);
                double seconds = SkillsFor(conn).IsAttack(effect) ? SupportedAttackSeconds(conn, id) : Math.Max(.001, (CastMs(conn, effect, id) ?? 0) / 1000.0);
                var finalPlan = SkillHitPlan.ForSkill(SkillsFor(conn), effect, supported.Level, supported.Quality);
                int criticalChance = SupportedCriticalChance(conn, id);
                int criticalMultiplier = SupportedCriticalMultiplier(conn, id);
                var accuracy = SkillsFor(conn).IsAttack(effect) ? SupportedAccuracy(conn, id) : default;
                double damageFactor = CriticalStrikePolicy.DamageFactor(
                    CriticalProhibitedBy(characterInputs, SkillsFor(conn).IsAttack(effect), own) ? 0 : criticalChance,
                    criticalMultiplier, CriticalExtraRollsFor(conn, characterInputs, own),
                    SkillsFor(conn).IsAttack(effect) ? accuracy.Chance(Monsters.BaseEvasion(conn.Pools.Level) ?? 0, false, false) : 100);
                var ranges = SkillsFor(conn).IsAttack(effect)
                    ? AttackPanelStats.Compute(finalPlan, supportedHit, 1 / seconds, damageFactor)
                    : SpellPanelStats.Compute(finalPlan, supportedHit, 1 / seconds,
                        includeMainHand: true, damageFactor: damageFactor);
                foreach (var (key, value) in ranges)
                    if (Stats.Key(key) is uint statKey) stats[statKey] = value;
                if (SkillsFor(conn).IsAttack(effect))
                {
                    stats[StatKeys.MainHandAttackSpeedPct] = (int)Math.Round((WeaponFor(conn).AttackMs / (seconds * 1000) - 1) * 100);
                    stats[StatKeys.HundredTimesAttacksPerSecond] = (int)Math.Round(100 / seconds);
                    foreach (var (field, value) in new[]
                    {
                        ("main_hand_accuracy_rating", accuracy.Rating),
                        ("main_hand_chance_to_hit_%", accuracy.Chance(Monsters.BaseEvasion(conn.Pools.Level) ?? 0, false, false)),
                        ("main_hand_chance_to_hit_evasive_monsters_%", accuracy.Chance(Monsters.BaseEvasiveEvasion(conn.Pools.Level) ?? 0, false, false)),
                    })
                        if (Stats.Key(field) is uint key)
                            stats[key] = panelSources.Overrides.Contains(key) ? panelSources.Raw.GetValueOrDefault(key) : value;
                    // A dual-wielded attack gem swings the off hand too: its own rows, with the same supports, and the
                    // per-use / per-second / attacks-per-second rows average both hands.
                    if (OffHandPanelStats(conn, playerStats, panelSources.MainHand, panelSources.OffHand,
                            stats.GetValueOrDefault(StatKeys.MainHandAttackSpeedPct), own, effect) is { } offHand
                        && OffhandWeaponFor(conn) is { } offWeapon)
                    {
                        offInputs = SupportedHitInputsFor(conn, id, weapon: offWeapon);
                        var offHit = offInputs is null ? GameServer.Core.World.DamagePacket.Empty : SupportedHitFrom(offInputs, suppress: true);
                        var offRows = offHand.ToDictionary(r => r.Stat, r => r.Value);
                        var (perUse, perSecond, attacksPerSecond) = DualWieldPanel.Average(supportedHit, seconds, offHit,
                            SupportedOffHandAttackSeconds(conn, id, offWeapon), damageFactor,
                            OffHandDamageFactor(conn, characterInputs, n => offRows.GetValueOrDefault(n), own));
                        foreach (var (field, value) in offHand.Concat(OffHandDamageRows(offHit)))
                            if (Stats.Key(field) is uint key)
                                stats[key] = panelSources.Overrides.Contains(key) ? panelSources.Raw.GetValueOrDefault(key) : value;
                        stats[StatKeys.DisplayHundredTimesDamagePerSkillUse] = perUse;
                        stats[StatKeys.HundredTimesDamagePerSecond] = perSecond;
                        stats[StatKeys.HundredTimesAttacksPerSecond] = attacksPerSecond;
                    }
                }
                if (Stats.Key(SkillsFor(conn).IsAttack(effect) ? "main_hand_critical_strike_chance" : "spell_critical_strike_chance") is uint ck)
                    stats[ck] = criticalChance;
                if (Stats.Key(SkillsFor(conn).IsAttack(effect) ? "main_hand_critical_strike_multiplier" : "spell_critical_strike_multiplier") is uint cm)
                    stats[cm] = criticalMultiplier;
                if (!SkillsFor(conn).IsAttack(effect))
                    foreach (var (stat, value) in SpellEffectiveCritical(conn, characterInputs, criticalChance, own))
                        if (Stats.Key(stat) is uint key) stats[key] = value;
                // A spell flagged as dealing secondary damage (Vaal Detonate Dead) fills the secondary rows with its
                // hit and critical values; the panel then draws those instead of the spell rows.
                bool secondary = !SkillsFor(conn).IsAttack(effect) && Get("display_skill_deals_secondary_damage") != 0;
                if (secondary)
                {
                    foreach (var (stat, value) in PanelStats.HandDamage(supportedHit, "secondary"))
                        if (Stats.Key(stat) is uint key) stats[key] = value;
                    if (Stats.Key("secondary_critical_strike_chance") is uint sc) stats[sc] = criticalChance;
                    if (Stats.Key("secondary_critical_strike_multiplier") is uint sm) stats[sm] = criticalMultiplier;
                }
                // The damaging-ailment rows of this skill's hit, per hand for a (dual-wielded) attack, from the same inputs.
                string ailmentHand = SkillsFor(conn).IsAttack(effect) ? "main_hand" : secondary ? "secondary" : "spell";
                var ailmentRows = inputs is null ? [] : SupportedAilmentRowsFrom(inputs, ailmentHand);
                // A spell inherits the default attack's hand rows with the player's stats: clear them, as the hit rows are.
                if (!SkillsFor(conn).IsAttack(effect))
                    ailmentRows.InsertRange(0, AilmentPanelStats.Rows("main_hand").Concat(AilmentPanelStats.Rows("off_hand")).Select(n => (n, 0)));
                if (offInputs is not null) ailmentRows.AddRange(SupportedAilmentRowsFrom(offInputs, "off_hand"));
                foreach (var (stat, value) in ailmentRows)
                    if (Stats.Key(stat) is uint key)
                        stats[key] = panelSources.Overrides.Contains(key) ? panelSources.Raw.GetValueOrDefault(key) : value;
            }
            // Movement and other skills whose base flag asks the panel for Damage per Use rather than per second.
            if (Stats.Key("skill_show_average_damage_instead_of_dps") is uint showAverage)
                stats[showAverage] = Get("base_skill_show_average_damage_instead_of_dps") != 0 ? 1 : 0;
            stats[OwnAuraRestriction.BaseAura] = own.GetValueOrDefault(OwnAuraRestriction.BaseAura);
            OwnAuraRestriction.PublishSkill(stats, OwnAuraRestrictionValue(conn));
            // These display aliases use the same raw snapshot as the character derivation. Apply them after
            // runtime radius calculations so an already-derived selected AoE alias cannot count its base twice.
            int RawPanel(string stat) => Stats.Key(stat) is uint key ? panelSources.Raw.GetValueOrDefault(key) : 0;
            int OwnPanel(string stat) => Stats.Key(stat) is uint key ? own.GetValueOrDefault(key) : 0;
            foreach (var (stat, value) in PanelStats.Selected(RawPanel, OwnPanel, panelSources.PlayerLevel,
                panelSources.MainHand.GetValueOrDefault, panelSources.OffHand.GetValueOrDefault))
                if (Stats.Key(stat) is uint key)
                    stats[key] = panelSources.Overrides.Contains(key) ? panelSources.Raw.GetValueOrDefault(key) : value;
            if (Stats.Key(VaalSoulCost.Aura) is uint aura)
                stats[aura] = VaalSoulInput(conn, playerStats, own, VaalSoulCost.Aura);
            if (SkillsFor(conn).VaalBudget(effect, supported.Level) is not null)
                PublishVaalSoulInputs(conn, playerStats, own, stats);
            ApplyProjectilePanelStats(effect, stats);
            if (SkillsFor(conn).FindByRow(id, effect, supported.Level, [.. stats.Select(kv => (kv.Key, kv.Value))]) is { } skill)
            {
                Console.WriteLine($"[gateway] gem skill 0x{skill.Id:x4} {name} level {level} quality {quality} (effect {effect}): GEPL {skill.EffectsPerLevelRow}, " +
                    $"GESSPL {skill.StatSetsPerLevelRow}, " + string.Join(", ", own.Select(kv => $"{Stats.Name(kv.Key)} {kv.Value}")));
                yield return skill with { Supports = [.. supported.Supports.Select(s => SkillsFor(conn).FindByRow(0, s.Effect.Row, s.Gem.Level))
                    .OfType<GrantedSkill>().Select(s => new GrantedSupport(s.EffectsPerLevelRow, s.StatSetsPerLevelRow)
                    { ExtraStatSets = [.. s.ExtraStatSets.Select(e => e.Row)] })] };
            }
        }
    }

    // What a skill bar slot holds, as saved (CharacterSaves): a gem or the default attack by GrantedEffects Id (skill ids
    // change with the socketed gems), a basic action as "Action/<ActionTypes Id>" ("Action/Move": "Move only" on a mouse
    // button), "" for a cleared slot. Null for an id the server does not know.
    internal static string? SkillBarEntry(ClientState conn, ushort skillId) =>
        skillId == 0 ? "" : SkillEffectId(conn, skillId) ?? (SkillsFor(conn).ActionName(skillId) is { } a ? "Action/" + a : null);

    // The saved skill bar as the player's current skill ids, sent with S→C 0x53. The default attack goes on slot 0 (left
    // mouse button) only when nothing at all was saved for that slot; nothing is sent without a saved bar, so the client
    // keeps its own default.
    internal static async Task SendSkillBar(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        FillSkillBar(conn);
        if (CharacterSaves.Load(CharacterName(conn))?.SkillBar is not { Count: > 0 } bar) return;
        var ids = new ushort[InstanceProtocol.SkillBarSlots];
        var byEffect = GemSkillEffects(conn).Select(g => (g.Id, Effect: SkillsFor(conn).EffectId(g.Effect)))
            .Append((Id: SkillCatalog.FirstSkillId, Effect: SkillEffectId(conn, SkillCatalog.FirstSkillId)))
            .Where(e => e.Effect is not null).GroupBy(e => e.Effect!).ToDictionary(g => g.Key, g => g.First().Id);
        foreach (var (slot, entry) in bar)
        {
            if (slot < 0 || slot >= ids.Length) continue;
            if (entry.StartsWith("Action/") && SkillsFor(conn).ActionSkillId(entry["Action/".Length..]) is ushort action) ids[slot] = action;
            else if (byEffect.TryGetValue(entry, out ushort id)) ids[slot] = id;
        }
        if (!bar.ContainsKey(0)) ids[0] = SkillCatalog.FirstSkillId;
        await Send(remote, stream, ciphers, InstanceProtocol.SkillBar(ids),
            $"0x0053 skill bar: {string.Join(", ", ids.Select((id, i) => id == 0 ? null : $"slot {i} 0x{id:x4}").OfType<string>())}");
    }

    // Puts the skills that are nowhere on the saved bar into its empty slots (SkillBarLayout), leaving every other slot as
    // it is: the default attack on the left mouse button of a new bar, each gem skill of the equipment on the right mouse
    // button or the next empty slot. True when the bar changed (saved).
    internal static bool FillSkillBar(ClientState conn)
    {
        lock (conn.Monsters)
        {
            if (conn.Closed || conn.LeavingArea || conn.RageRetired || HardcoreProgressBlocked(conn)) return false;
            string character = CharacterName(conn);
            var bar = CharacterSaves.Load(character)?.SkillBar ?? [];
            string attack = SkillEffectId(conn, SkillCatalog.FirstSkillId)!;
            var gems = GemSkillEffects(conn).Select(g => SkillsFor(conn).EffectId(g.Effect)).OfType<string>().ToList();
            var added = SkillBarLayout.Fill(bar, attack, gems, entry => entry == attack || entry.StartsWith("Action/") || gems.Contains(entry));
            foreach (var (slot, entry) in added) CharacterSaves.SetSkillBar(character, (byte)slot, entry);
            if (added.Count > 0)
                Console.WriteLine($"[gateway] {character} skill bar: {string.Join(", ", added.Select(a => $"{a.Entry} on slot {a.Slot}"))}");
            return added.Count > 0;
        }
    }

    // The GrantedEffects Id behind a skill id of the player (the default attack or a gem skill), or null.
    internal static string? SkillEffectId(ClientState conn, ushort skillId) =>
        skillId == SkillCatalog.FirstSkillId ? Environment.GetEnvironmentVariable("Game_PLAYER_ATTACK") ?? "PlayerMelee"
        : GemSkillEffects(conn).FirstOrDefault(g => g.Id == skillId) is { Name: not null } g ? SkillsFor(conn).EffectId(g.Effect) : null;

    // Why the held item may not go into an equipment row: the character is below its level requirement
    // (ItemModStats.LevelRequirement). The client also checks it (0x1421eca50) against the level we send in the item data,
    // but the server does not trust that.
    internal static string? LevelTooLow(ClientState conn, byte row, Item? held)
    {
        if (held is null || !(ItemModStats.EquipmentRows.Contains(row) || row is 14 or 15)) return null;
        if (ItemsFor(conn).ByHash(held.TypeHash) is not { } t) return null;
        int needs = ItemModStats.LevelRequirement(held, t.DropLevel, ItemsFor(conn).Mods);
        return conn.Pools.Level < needs ? $"{t.Name} requires level {needs} (character is {conn.Pools.Level})" : null;
    }

    // Why a gem may not go into a socket: the character's level is below the gem's level requirement (GrantedEffectStatSetsPerLevel
    // PlayerLevelReq at the gem's level) or an attribute requirement is not met. The client checks both (0x14246fff0 for the
    // level, 0x14246fc40 per attribute) and refuses the move itself, but the server does not trust that.
    internal static string? GemRequirementsUnmet(ClientState conn, Item? gem)
        => GemRequirementsUnmetAt(conn, gem, null);

    internal static string? GemRequirementsUnmetAt(ClientState conn, Item? gem, GemRequirementSnapshot? snapshot)
    {
        if (gem is null || ItemsFor(conn).ByHash(gem.TypeHash) is not { } baseType || ItemsFor(conn).Gem(baseType) is not { } type) return null;
        if (!LeagueFor(conn).Rules.AllowsGemBase(baseType.Id))
            return $"{baseType.Name} is unavailable in {LeagueFor(conn).Name}";
        int level = ItemsFor(conn).GemLevel(gem);
        int effect = type.Variant(gem.GemVariant).Effect;
        if (effect < 0) return null;
        string name = ItemsFor(conn).GemName(gem);
        int characterLevel = snapshot?.Level ?? conn.Pools.Level;
        if (SkillsFor(conn).PlayerLevelReq(effect, level) is int needs and > 0 && characterLevel < needs)
            return $"{name} at level {level} requires level {needs} (character is {characterLevel})";
        // A gem's attributes come from its percentage shares and the level requirement, at the gem's level clamped to its cap
        // (a corrupted level 21 gem keeps level 20's requirement; 0x14246fc40). The client checks them against
        // virtual_*_for_requirements, which are the attributes unless something overrides them (/set_stat).
        var (str, dex, intel) = GemAttributeRequirements(conn, gem, type, effect);
        foreach (var (need, attr, has) in new[] { (str, "strength", snapshot?.Strength ?? RequirementAttribute(conn, "strength", conn.Pools.Strength)),
            (dex, "dexterity", snapshot?.Dexterity ?? RequirementAttribute(conn, "dexterity", conn.Pools.Dexterity)),
            (intel, "intelligence", snapshot?.Intelligence ?? RequirementAttribute(conn, "intelligence", conn.Pools.Intelligence)) })
            if (need > 0 && has < need)
                return $"{name} at level {level} requires {need} {attr} (character has {has})";
        return null;
    }

    // A gem's attribute requirements at its level: each SkillGems percentage through SkillCatalog.GemAttributeRequirement.
    internal static (int Str, int Dex, int Int) GemAttributeRequirements(ClientState conn, Item gem, ItemCatalog.GemType type, int effect)
        => GemAttributeRequirements(conn, gem, type, effect, AttributeRequirementsPct(conn));

    internal static (int Str, int Dex, int Int) GemAttributeRequirements(ClientState conn, Item gem, ItemCatalog.GemType type, int effect, int pct)
    {
        int level = Math.Clamp(ItemsFor(conn).GemLevel(gem), 1, Math.Max(1, type.MaxLevel));
        int needs = SkillsFor(conn).PlayerLevelReq(effect, level);
        return (ScaledRequirement(SkillCatalog.GemAttributeRequirement(type.StrengthPercent, needs, type.IsSupport), pct),
            ScaledRequirement(SkillCatalog.GemAttributeRequirement(type.DexterityPercent, needs, type.IsSupport), pct),
            ScaledRequirement(SkillCatalog.GemAttributeRequirement(type.IntelligencePercent, needs, type.IsSupport), pct));
    }

    // The requirements a gem socketed in a worn item adds to that item (the client shows the item red when they are not
    // met): the level its skill needs at the gem's level, and its attribute requirements. Pure catalog data, so the
    // equipment resolver can read it before the pools exist.
    internal static (int Level, int Str, int Dex, int Int) SocketedGemRequirements(ClientState conn, Item gem, int attributePct)
    {
        if (ItemsFor(conn).ByHash(gem.TypeHash) is not { } baseType || ItemsFor(conn).Gem(baseType) is not { } type) return default;
        int effect = type.Variant(gem.GemVariant).Effect;
        if (effect < 0) return default;
        int level = SkillsFor(conn).PlayerLevelReq(effect, ItemsFor(conn).GemLevel(gem));
        var (str, dex, intel) = GemAttributeRequirements(conn, gem, type, effect, attributePct);
        return (level, str, dex, intel);
    }

    // The value the client's requirement checks compare (virtual_*_for_requirements, 13-items.md §2d): what /set_stat set,
    // else the character's attribute.
    internal static int RequirementAttribute(ClientState conn, string attribute, int value)
    {
        lock (conn.Monsters)
            return Stats.Key($"virtual_{attribute}_for_requirements") is uint key && conn.PlayerStats.TryGetValue(key, out int set) ? set : value;
    }

    // Actor restrictions use the actual caster's current sources, never a skill overlay (which may replace a key).
    // CharacterPlayerStats does not build PlayerSkills or call GemDisabled, so this cannot recurse through admission.
    internal static string? ActorSkillDisabled(ClientState conn, int effect)
    {
        // Legacy effect-only callers can use support metadata only when the instance is unambiguous.
        // Production admission always supplies the requested skill ID through ActorSkillInstanceDisabled.
        var copies = GemSkillEffects(conn).Where(g => g.Effect == effect).Take(2).ToArray();
        return ActorSkillDisabledCore(conn, effect, copies.Length == 1 ? copies[0].Id : null);
    }

    internal static string? ActorSkillInstanceDisabled(ClientState conn, int effect, ushort skillId) =>
        ActorSkillDisabledCore(conn, effect, skillId);

    internal static bool OwnAuraSkillRestricted(ClientState conn, int effect, ushort? skillId)
    {
        if (OwnAuraRestrictionValue(conn) == 0) return false;
        if (SkillsFor(conn).IsAura(effect)) return true;
        if (skillId is not ushort id) return false;
        var gem = SupportedSkill(conn, id);
        return gem.Effect == effect && OwnAuraRestriction.IsAura(gem.Stats);
    }

    internal static string? ActorSkillDisabledCore(ClientState conn, int effect, ushort? skillId)
    {
        if (OwnAuraSkillRestricted(conn, effect, skillId)) return "own aura skills are disabled";
        return SkillsFor(conn).IsWarcry(effect) && Stats.Key("your_warcry_skills_are_disabled") is uint key
            && CharacterPlayerStats(conn).GetValueOrDefault(key) != 0 ? "warcry skills are disabled" : null;
    }

    // Why a gem skill may not be used: an actor restriction, unmet gem requirements or provider equipment restrictions.
    // The client reds the gem but still sends the use, so this is the backstop for a client whose item is not inactive yet.
    // Null when the actor restriction and requirements allow the effect.
    internal static string? GemDisabled(ClientState conn, int effect)
    {
        if (ActorSkillDisabled(conn, effect) is { } restriction) return restriction;
        if (WeaponRestricted(conn, effect) is { } weapon) return weapon;
        if (!LeagueFor(conn).Rules.AllowsGemEffect(SkillsFor(conn).EffectId(effect)))
            return $"This skill is unavailable in {LeagueFor(conn).Name}";
        foreach (var place in PlayerInventories.For(CharacterName(conn), ItemsFor(conn)).SocketedGemPlaces())
            if (ItemsFor(conn).GemEffects(place.Gem).Contains(effect) && GemRequirementsUnmet(conn, place.Gem) is { } missing)
                return missing;
        if (SkillsFor(conn).EffectId(effect) is { } effectId
            && ProjectileDefinition(effectId)?.Unavailable?.Invoke(conn) is { } unavailable) return unavailable;
        return null;
    }

    // Keep source-gem requirements and accepted supports local to the actual requested instance.
    // A second gem with the same GrantedEffects row may have a different level or link group.
    internal static string? GemSkillInstanceDisabled(ClientState conn, int effect, ushort skillId)
    {
        var gem = SupportedSkill(conn, skillId);
        if (gem.Effect != effect || gem.SourceGem is null) return "skill is unavailable";
        if ((ActorSkillInstanceDisabled(conn, effect, skillId) ?? WeaponRestricted(conn, effect)
                ?? GemRequirementsUnmet(conn, gem.SourceGem)) is { } disabled)
            return disabled;
        return SkillsFor(conn).EffectId(effect) is { } effectId ? ProjectileDefinition(effectId)?.Unavailable?.Invoke(conn) : null;
    }

    // ItemClasses row 36: the hand type the client gives an empty main hand (11-skills.md §10a, Unarmed 37 = row + 1).
    internal const int UnarmedClassRow = 36;

    // A skill neither active hand can use (its ActiveSkills weapon restriction, the client's FUN_141ee8410): the client
    // greys it out, and a use that still arrives is refused here. An empty or inactive main hand is Unarmed; an inactive
    // weapon is not in the hands (HandType), as the model the client checks does not show it (Looks).
    internal static string? WeaponRestricted(ClientState conn, int effect)
    {
        var allowed = SkillsFor(conn).WeaponRestrictions(effect);
        if (allowed.Count == 0) return null;
        int main = HandType(conn, PlayerInventories.Weapon1)?.ClassRow ?? UnarmedClassRow;
        return allowed.Contains(main) || HandType(conn, PlayerInventories.Offhand1)?.ClassRow is int off && allowed.Contains(off)
            ? null : "the equipped weapon cannot use this skill";
    }

    // A worn item whose requirements are not met (CharacterStatSources): its mods and gems do nothing.
    internal static bool IsInactive(ClientState conn, uint itemId) => InactiveItems.GetValueOrDefault(CharacterName(conn))?.Contains(itemId) == true;

    internal static string CharacterName(ClientState conn) => string.IsNullOrEmpty(conn.Character) ? "Exile" : conn.Character;

    // The skills of the active gems socketed in the equipped items in skill-list order: skill id, GrantedEffects row, gem
    // name, the gem's own level (from its experience; the experience stalls at the character's level requirement instead,
    // GainGemExperience) and quality. A transfigured or Trarthan gem grants its variant's skill, a Vaal gem its Vaal skill and
    // its base skill (ItemCatalog.GemEffects). Each skill keeps its id while its gem stays socketed (GemSkillIds): the client
    // binds skill bar keys by skill id. A gem whose requirements the character does not meet makes its item inactive
    // (EquipmentStatSources), so it grants nothing here; a use that still arrives is refused (GemDisabled).
    internal static IEnumerable<(ushort Id, int Effect, string Name, int Level, int Quality, uint Gem)> GemSkillEffects(ClientState conn)
    {
        string name = CharacterName(conn);
        return GemSkillEffectsAt(name, ItemsOf(conn).SocketedGemPlaces(),
            InactiveItems.GetValueOrDefault(name), LeagueFor(conn));
    }

    internal static IEnumerable<(ushort Id, int Effect, string Name, int Level, int Quality, uint Gem)> GemSkillEffectsAt(
        string? character, IReadOnlyList<(byte Row, uint ItemId, int Socket, Item Gem)> places, IReadOnlySet<uint>? inactive,
        LeagueRuntime league)
    {
        var catalog = league.HardMode ? HardModeItems.Value : Items;
        string name = string.IsNullOrEmpty(character) ? "Exile" : character;
        var gems = places
            .Where(g => catalog.ByHash(g.Gem.TypeHash) is not { } type ? !league.HardMode : league.Rules.AllowsGemBase(type.Id))
            .SelectMany(g => catalog.GemEffects(g.Gem).Select(effect => (g.ItemId, g.Socket, g.Gem, Effect: effect))).ToList();
        var ids = GemSkillIdsOf.GetOrAdd(name, _ => new GemSkillIds())
            .Assign([.. gems.Select(g => new GemSkillIds.Gem(g.ItemId, g.Socket, g.Effect))], GemSkillIdPool.Value);
        // Gems in an inactive item grant nothing, but keep their id.
        for (int i = 0; i < gems.Count; i++)
            if (ids[i] is ushort id && inactive?.Contains(gems[i].ItemId) != true)
                yield return (id, gems[i].Effect, catalog.GemName(gems[i].Gem), catalog.GemLevel(gems[i].Gem), gems[i].Gem.Quality, gems[i].Gem.TypeHash);
    }

    internal static double Dist((int X, int Y) a, (int X, int Y) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // An attack gem used at the ground: a point beyond the weapon's reach is an attack-move (the client walks there);
    // within reach it is a swing at the ground that pays the skill's mana even with nothing standing there (§5 of
    // 11-skills.md; live 2026-10-26: every empty point used to be walked, so swinging cost nothing).
    internal static bool GroundAttackWalks((int X, int Y) here, (int X, int Y) aim, double reach) => Dist(here, aim) > reach;

    // An intentional /set_level reset invalidates older XP events before publishing its current level and points.
    internal static async Task SetLevel(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, int level)
    {
        lock (conn.Monsters)
            if (HardcoreProgressBlocked(conn)) return;
        var change = RecordExperienceReset(conn, level);
        await PublishExperienceChange(remote, stream, ciphers, conn, change, $"/set_level {level}");
    }

    internal sealed record PassiveLevelChange(int Level, int Before, ushort ExtraPoints,
        IReadOnlyList<ushort> Nodes, IReadOnlyDictionary<ushort, ushort> Masteries, AscendancyProgress Ascendancy,
        ushort RefundPoints, CharacterSaves.PassiveProgressSnapshot? Expected, bool Trimmed);

    internal static PassiveLevelChange CaptureLevelChange(ClientState conn, int level)
    {
        lock (conn.Monsters)
        {
            var next = new List<ushort>(conn.Passives);
            int points = Progression.PassivePoints(level) + conn.ExtraPassivePoints + AscendancyOrdinaryPoints(conn);
            TrimOrdinaryPassives(conn, next, points);
            var effects = conn.MasteryEffects.Where(kv => next.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            return new(level, conn.Pools.Level, conn.ExtraPassivePoints, next, effects, conn.MainAscendancy,
                conn.RefundPoints, conn.SavedPassiveProgress, next.Count != conn.Passives.Count);
        }
    }

    internal static int CommitLevelChange(ClientState conn, PassiveLevelChange change)
    {
        lock (conn.Monsters)
        {
            if (HardcoreProgressBlocked(conn)) return conn.Pools.Level;
            // An allocation/refund may have committed after capture, even when this level did not need a trim.
            // Rebuild against the current runtime snapshot; keep the durable comparison for other connections.
            if (!ReferenceEquals(change.Expected, conn.SavedPassiveProgress) || change.Before != conn.Pools.Level ||
                change.ExtraPoints != conn.ExtraPassivePoints || change.RefundPoints != conn.RefundPoints)
                change = CaptureLevelChange(conn, change.Level);
            if (change.Trimmed)
                CharacterSaves.SetMainAscendancy(CharacterName(conn), change.Ascendancy, change.Nodes, change.Masteries,
                    expected: change.Expected);
            return PublishCommittedLevel(conn, change);
        }
    }

    // XP is shared by overlapping area connections. Reconcile from the durable tree, not a stale connection's
    // candidate; the read, optional trim and comparison stay in one transaction before runtime publication.
    internal static int CommitExperienceLevel(ClientState conn, int level)
    {
        lock (conn.Monsters)
        {
            if (HardcoreProgressBlocked(conn)) return conn.Pools.Level;
            var change = Saves.Atomically(() =>
            {
                var save = CharacterSaves.Load(CharacterName(conn)) ?? new CharacterSaves.Save([]);
                ValidateSavedProgress(conn, save);
                var ascendancy = save.MainAscendancy ?? new();
                var nodes = new List<ushort>(save.Passives ?? []);
                ushort extra = save.ExtraPassivePoints ?? 0, refunds = save.RefundPoints ?? 0;
                int points = Progression.PassivePoints(level) + extra + AscendancyRules.OrdinaryPointsGranted(
                    (byte)SkillsFor(conn).ClassFor(conn.ClassRow).IntegerId, ascendancy);
                TrimOrdinaryPassives(conn, nodes, points, ascendancy);
                var masteries = (save.MasteryEffects ?? []).Where(kv => nodes.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
                var current = new PassiveLevelChange(level, conn.Pools.Level, extra, nodes, masteries, ascendancy,
                    refunds, CharacterSaves.PassiveProgressSnapshot.From(save), nodes.Count != (save.Passives?.Count ?? 0));
                if (current.Trimmed)
                    CharacterSaves.SetMainAscendancy(CharacterName(conn), ascendancy, nodes, masteries,
                        expected: current.Expected);
                return current;
            });
            return PublishCommittedLevel(conn, change);
        }
    }

    internal static int PublishCommittedLevel(ClientState conn, PassiveLevelChange change)
    {
        conn.Passives = [.. change.Nodes];
        conn.MasteryEffects = new(change.Masteries);
        conn.MainAscendancy = change.Ascendancy;
        conn.ExtraPassivePoints = change.ExtraPoints;
        conn.RefundPoints = change.RefundPoints;
        conn.SavedPassiveProgress = new(change.Ascendancy, change.Nodes, change.Masteries, change.RefundPoints);
        conn.Pools = PoolsFor(conn, change.Level);
        conn.ApplyMovementSpeed();
        return change.Before;
    }

    // A new level in place, after the Player component's experience (0x2ba; the client derives the level from it and runs
    // its level change): the level's stats (0x1b3: level, life, mana, accuracy, ...), the skills with them (0x2ba Actor),
    // full life, as a level up gives, and the passive skill points (0x33). Passives beyond a lower level's points are
    // taken back, last allocated first.
    internal static async Task ApplyLevel(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, int level)
    {
        lock (conn.Monsters)
            if (HardcoreProgressBlocked(conn)) return;
        int before = CommitLevelChange(conn, CaptureLevelChange(conn, level));
        await PublishLevelChange(remote, stream, ciphers, conn, before, level);
    }

    internal static async Task PublishLevelChange(string remote, NetworkStream stream, ConnectionCiphers ciphers,
        ClientState conn, int before, int level)
    {
        lock (conn.Monsters)
            if (HardcoreProgressBlocked(conn)) return;
        Console.WriteLine($"[gateway] {remote} {CharacterName(conn)}: level {before} → {level} (life {conn.Pools.Life}, mana {conn.Pools.Mana}, {Progression.PassivePoints(level)} passive point(s))");
        await SendCharacterStats(remote, stream, ciphers, conn, $"level {level}");
        int mana = UnreservedMana(conn);
        lock (conn.Monsters)
        {
            // A lethal hit while SendCharacterStats awaited must not turn a Hardcore death into a level-up heal.
            if (conn.Hardcore && (conn.PlayerLife == 0 || conn.HardcoreDeathRecorded)) return;
            (conn.PlayerLife, conn.PlayerMana) = (conn.PlayerMaxLife, (uint)mana);
            conn.EnergyShield.SetMaximum(conn.Pools.EnergyShield, true);
        }
        await Send(remote, stream, ciphers, InstanceProtocol.LifeUpdate(PlayerId, conn.PlayerMaxLife), $"0x01b1 player life {conn.PlayerMaxLife}");
        await SendPlayerMana(remote, stream, ciphers, conn, $"0x01b1 player mana {conn.PlayerMana}");
        await SendPassives(remote, stream, ciphers, conn);
    }

    // The player's stats (0x1b3) and the skills computed with them (0x2ba Actor), after the level or the passives changed.
    internal static async Task SendCharacterStats(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, string why)
    {
        await SendPlayerStatSnapshot(remote, stream, ciphers, conn,
            () => PlayerStatsUpdateAtGate(conn, PlayerStatsWithCleared(conn)), $"0x01b3 player stats ({why})");
        await SendSkills(remote, stream, ciphers, conn);
        await SendPlayerEnergyShield(remote, stream, ciphers, conn);
    }

    // The player's skills again, and the removal of the ones the client still has whose gems left the equipment, in one
    // Actor 0x20 update (PlayerSkillsChange): the 0x40 list neither removes skills nor tells the character panel.
    internal static async Task SendSkills(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        lock (conn.Monsters)
        {
            if (conn.Closed || conn.LeavingArea || conn.RageRetired) return;
            RefreshVaalSouls(conn);
            RefreshSingleUseCooldowns(conn);
            EnsureAntiquarianPools(conn);
            EnsureOwnAuraPools(conn);
            foreach (var id in conn.Auras.Keys.ToList())
            {
                var gem = SupportedSkill(conn, id);
                if (gem.Effect < 0 || SupportedReservation(conn, id) is not { } reservation) continue;
                var before = conn.Auras[id];
                conn.Auras[id] = before with
                {
                    ManaFlat = reservation.Flat, ManaPercent = reservation.Percent,
                    Level = gem.Level, Quality = gem.Quality
                };
                conn.AuraStatsDirty |= before != conn.Auras[id];
            }
            if (conn.Auras.Count > 0)
            {
                conn.Pools = PoolsFor(conn, conn.Pools.Level);
                conn.PlayerMana = Math.Min(conn.PlayerMana, (uint)UnreservedMana(conn));
            }
        }
        List<ushort> gone = [], added = [];
        await SendPlayerStatSnapshot(remote, stream, ciphers, conn, () =>
        {
            var skills = PlayerSkills(conn);
            gone = conn.ClientSkillIds.Except(skills.Select(s => s.Id)).Order().ToList();
            added = skills.Select(s => s.Id).Where(id => !conn.ClientSkillIds.Contains(id)).ToList();
            conn.ClientSkillIds = [.. skills.Select(s => s.Id)];
            return PlayerSkillsWithVaalSouls(conn, skills, gone);
        }, "0x02ba player Actor: current skill stats");
        await SendNativeCooldownRefresh(remote, stream, ciphers, conn);
        if (gone.Count > 0) await EndSkillEffects(remote, stream, ciphers, conn, gone);

        // The update can take a bound skill off the skill bar (live 2026-10-21: Haste's slot emptied when its aura went on,
        // and came back with the area change's 0x53), and a gem socketed again does not get its slot back by itself, so the
        // saved bar goes out again. Not when a skill the saved bar never had was added: the client binds a new skill to a free
        // slot itself (C→S 0x4e), and the bar sent now would clear it there. A new gem skill gets an empty slot of the saved
        // bar first (FillSkillBar: the right mouse button, then Q W E R T …), so it goes out with the bar.
        FillSkillBar(conn);
        var saved = CharacterSaves.Load(CharacterName(conn))?.SkillBar?.Values;
        if (added.All(id => SkillEffectId(conn, id) is { } effect && saved?.Contains(effect) == true))
            await SendSkillBar(remote, stream, ciphers, conn);
        // The same recalculation may have changed which worn weapons qualify.
        await SendModelIfHandsChanged(remote, stream, ciphers, conn);
    }

    // Skills the player no longer has (their gems left the equipment): their auras and Righteous Fire end with them, and the
    // reserved mana comes back.
    internal static async Task EndSkillEffects(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, IReadOnlyCollection<ushort> gone)
    {
        List<AuraCatalog.Aura> ended;
        lock (conn.Monsters)
        {
            if (conn.Closed || conn.LeavingArea || conn.RageRetired) return;
            ended = [.. conn.Auras.Values.Where(a => gone.Contains(a.SkillId))];
            foreach (var aura in ended) conn.Auras.Remove(aura.SkillId);
            if (ended.Count > 0)
            {
                conn.AuraStatsDirty = true;
                conn.Pools = PoolsFor(conn, conn.Pools.Level);
                conn.ApplyMovementSpeed();
            }
        }
        if (ended.Count > 0)
        {
            await SendOwnAuraChanges(remote, stream, ciphers, conn);
            await SendCharacterStats(remote, stream, ciphers, conn, $"aura {string.Join(", ", ended.Select(a => a.Name))} off (gem left)");
            await SendPlayerMana(remote, stream, ciphers, conn, $"0x01b1 player mana {conn.PlayerMana}/{conn.Pools.Mana}, {ReservedMana(conn)} reserved");
        }
        if (conn.RighteousFire is { } rf && gone.Contains(rf.Skill)) await EndRighteousFire(remote, stream, ciphers, conn, "its gem left");
    }
}
