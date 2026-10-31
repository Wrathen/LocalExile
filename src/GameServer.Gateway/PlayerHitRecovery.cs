/// <summary>Ordinary physical monster melee stun eligibility and bounded native TakeHit recovery.
/// PoB CalcDefence: physical hits count 25% extra, chance = 200 * effective damage / threshold, only above 20%.
/// Monster.ot adds 20% more melee stun duration to the 350 ms base; recovery rounds up to a 33 ms tick.
/// Special threshold substitutions, conditional immunity and monster-specific modifiers are outside this slice.</summary>
sealed class PlayerHitRecovery
{
    public const byte TakeHitAnimation = 5;
    public double Until { get; private set; } = double.NegativeInfinity;
    /// <summary>Accepted recovery permanently invalidates older paid windups, independently of Move and expiry.</summary>
    public int Generation { get; private set; }
    public bool Active(double now) => now < Until;
    public bool BlocksInput(double requestedAt, double now) => Active(now) || requestedAt < Until;
    public void Clear() => Until = double.NegativeInfinity;

    public bool Begin(double now, float seconds)
    {
        if (Active(now) || !float.IsFinite(seconds) || seconds <= 0) return false;
        Until = now + seconds;
        Generation++;
        return true;
    }

    public static double Chance(int damage, uint maximumLife, Func<string, int> get)
    {
        if (damage <= 0 || maximumLife == 0 || get("cannot_be_stunned") != 0
            || get("cannot_be_stunned_by_attacks") != 0 || get("cannot_be_stunned_by_hits_of_only_physical_damage") != 0
            || EnergyShieldPresent(get) || get("maximum_life_is_one") != 0
            || get("avoid_interruption_%") != 0) return 0;
        // These need their own threshold calculation. Do not silently use life for a substituted threshold.
        if (get("stun_threshold_based_on_%_mana_instead_of_life") != 0
            || get("stun_threshold_based_on_%_energy_shield_instead_of_life") != 0
            || get("stun_threshold_based_on_energy_shield_instead_of_life") != 0
            || get("occultist_stun_threshold_is_based_on_energy_shield_instead_of_life") != 0
            || get("additional_stun_threshold_based_on_%_of_energy_shield") != 0) return 0;
        double threshold = maximumLife * (1 + get("stun_threshold_+%") / 100.0);
        if (threshold <= 0) return 0;
        double chance = Math.Min(100, 200 * (damage * 1.25) / threshold);
        return chance <= 20 ? 0 : chance * (100 - Math.Clamp(get("avoid_stun_%"), 0, 100)) / 100;
    }

    public static bool EnergyShieldPresent(Func<string, int> get, int equippedBase = 0) =>
        equippedBase > 0 || get("maximum_energy_shield") > 0 || get("base_maximum_energy_shield") > 0;

    public static bool UnsupportedStat(string name) =>
        name.StartsWith("cannot_be_stunned_", StringComparison.Ordinal)
            && name is not ("cannot_be_stunned_by_attacks" or "cannot_be_stunned_by_hits_of_only_physical_damage"
                or "cannot_be_stunned_by_spells" or "cannot_be_stunned_by_suppressed_spell_damage" or "cannot_be_stunned_by_blocked_hits")
        || name.StartsWith("base_cannot_be_stunned_", StringComparison.Ordinal)
        || name.Contains("avoid_stun", StringComparison.Ordinal) && name is not ("avoid_stun_%" or "base_avoid_stun_%")
        || name.Contains("stun_avoid", StringComparison.Ordinal)
        // The per-charge threshold and recovery stats ChargeEffects expands (Brutal's threshold, Frenzy's recovery) are
        // already in stun_threshold_+% / stun_recovery_+% with the charges held.
        || name.StartsWith("stun_threshold_+%_", StringComparison.Ordinal) && !ChargeEffects.Expands(name)
        || name.StartsWith("stun_recovery_+%_", StringComparison.Ordinal) && !ChargeEffects.Expands(name)
        || name.StartsWith("skill_cannot_be_stunned", StringComparison.Ordinal)
        || name is "stun_threshold_+" or "stun_threshold_increased_by_overcapped_fire_resistance"
            or "local_flask_stun_recovery_+%_while_healing" or "block_and_stun_+%_recovery_per_fortification";

    public static float Duration(Func<string, int> get, double actionSpeed = 1)
    {
        double recovery = 1 + get("stun_recovery_+%") / 100.0;
        double duration = 1 + get("stun_duration_on_self_+%") / 100.0;
        if (recovery <= 0 || duration <= 0 || !double.IsFinite(actionSpeed) || actionSpeed <= 0) return 0;
        // Action speed also scales the recovery animation; stun_recovery_+% is the separate, additive stun stat.
        double seconds = Math.Ceiling(.35 * 1.2 * duration / recovery / actionSpeed / .033) * .033;
        // Do not let malformed developer stats create an unrepresentable native time or an unbounded input lockout.
        return seconds is > 0 and <= 60 ? (float)seconds : 0;
    }
}
