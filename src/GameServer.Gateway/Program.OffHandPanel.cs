using GameServer.Core.World;

partial class Program
{
    /// <summary>
    /// The Offence tab's Off Hand rows (issue #75) for an attack that swings the off-hand weapon of a dual-wielded pair:
    /// <c>skill_uses_off_hand</c> makes the panel draw them, and the hand's accuracy rating, chance to hit (plain and
    /// evasive monsters), attack speed, critical strike chance and multiplier are that weapon's own, from the shared
    /// character buckets with the off-hand item's local modifiers in the main hand's place. The client derives none
    /// of them (11-skills.md §4). Null when nothing is swung with the off hand (empty, a shield, a quiver, no main-hand
    /// weapon): the rows then keep the main hand's values the class attributes send, and the flag clears.
    /// <paramref name="player"/> is the character's final stats; <paramref name="own"/> a selected attack gem's own
    /// stats, whose flat/increased accuracy and critical increases join as they do for the main hand.
    /// </summary>
    internal static List<(string Stat, int Value)>? OffHandPanelStats(ClientState conn, IReadOnlyDictionary<uint, int> player,
        IReadOnlyDictionary<string, int> mainLocal, IReadOnlyDictionary<string, int> offLocal,
        int mainHandAttackSpeedPct, IReadOnlyDictionary<uint, int>? own = null, int effect = -1)
    {
        if (OffhandWeaponFor(conn) is null || ActiveOffHand(conn) is not { Type: { } offType }) return null;
        var pools = conn.Pools;
        int Player(string name) => Stats.Key(name) is uint key ? player.GetValueOrDefault(key) : 0;
        int Own(string name) => Stats.Key(name) is uint key ? own?.GetValueOrDefault(key) ?? 0 : 0;
        var profile = WeaponProfileFor(conn, activeSet: true);
        // Accuracy: the pools' unrounded basis carries the main weapon's local accuracy; swap in the off hand's. As
        // AccuracyRating.Selected does, keep the pools' final rating when nothing differs from the main hand.
        long localDifference = (long)offLocal.GetValueOrDefault("local_accuracy_rating") - mainLocal.GetValueOrDefault("local_accuracy_rating");
        long ownFlat = AccuracyRating.Flat(profile, n => Own(n), includeLocal: false), ownIncreased = AccuracyRating.Increased(profile, n => Own(n));
        int rating = localDifference == 0 && ownFlat == 0 && ownIncreased == 0 ? pools.Accuracy
            : AccuracyRating.Calculate(pools.AccuracyBase + localDifference + ownFlat, pools.AccuracyPct + ownIncreased);
        var accuracy = AttackAccuracyProfile.From(pools, player, Stats, own, offLocal) with { Rating = rating };
        // Critical chance: the off-hand weapon's base with its local mods, the same override and global increases.
        int weaponBase = AddedBaseCriticalStrike.LocalWeaponChance(ItemsFor(conn).WeaponCritical(offType),
            offLocal.GetValueOrDefault("local_critical_strike_chance"), offLocal.GetValueOrDefault("local_critical_strike_chance_+%"));
        int chance = AttackBaseCriticalStrike.Chance(true, weaponBase,
            pools.CriticalStrikeChancePct + (own is null ? 0 : AttackBaseCriticalStrike.IncreasedSum(profile, n => Own(n))),
            Player, own is null ? null : Own);
        int multiplier = (int)Math.Clamp((long)AttackCriticalMultiplier(conn, player, effect, own)
            + Own("base_critical_strike_multiplier_+"), int.MinValue, int.MaxValue);
        return
        [
            ("skill_uses_off_hand", 1),
            ("off_hand_accuracy_rating", rating),
            ("off_hand_chance_to_hit_%", accuracy.Chance(Monsters.BaseEvasion(pools.Level) ?? 0, false, false)),
            ("off_hand_chance_to_hit_evasive_monsters_%", accuracy.Chance(Monsters.BaseEvasiveEvasion(pools.Level) ?? 0, false, false)),
            // The attack's final main-hand speed (the default attack's shared bucket, or the gem's with its supports)
            // without the main weapon's own local speed, which belongs to that weapon alone.
            ("off_hand_attack_speed_+%", SkillCatalog.Unarmed.OffHandAttackSpeedPct(mainHandAttackSpeedPct, mainLocal.GetValueOrDefault("local_attack_speed_+%"))),
            ("off_hand_critical_strike_chance", chance),
            ("off_hand_critical_strike_multiplier", multiplier),
        ];
    }

    /// <summary>The off hand's twelve damage rows for a selected attack gem: the same supported hit swung with the
    /// off-hand weapon, every type written (zero clears a type the conversion no longer deals).</summary>
    internal static IEnumerable<(string Stat, int Value)> OffHandDamageRows(DamagePacket hit)
    {
        var ranges = PanelStats.HandDamage(hit, "off_hand").ToDictionary(r => r.Stat, r => r.Value);
        foreach (string stat in PanelStats.DamageRows("off_hand")) yield return (stat, ranges.GetValueOrDefault(stat));
    }
}
