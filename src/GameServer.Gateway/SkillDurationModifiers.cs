/// <summary>Owner skill-duration increases derived from final attributes. Duration consumers must choose their own
/// inputs; this does not give a minion's own skills or buffs the owner's skill duration.</summary>
static class SkillDurationModifiers
{
    public const string PerTenStrength = "skill_effect_duration_+%_per_10_strength";
    public const string IncreasedDuration = "skill_effect_duration_+%";

    public static int Total(int rawIncreasedDuration, int finalStrength, int increasedPerTenStrength) =>
        (int)Math.Clamp(rawIncreasedDuration + (long)(Math.Max(0, finalStrength) / 10) * increasedPerTenStrength,
            int.MinValue, int.MaxValue);

    // minion: a CreatesMinion skill, whose duration also takes the player's increased minion duration (PoB SkillStatMap
    // base_minion_duration_+% is Duration INC for SkillType.CreatesMinion).
    public static double Seconds(int baseMilliseconds, Func<string, int> player, Func<string, int> own, bool minion = false)
    {
        double value = Math.Max(0, baseMilliseconds) / 1000d
            * Math.Max(0, 1 + ((long)player(IncreasedDuration) + own(IncreasedDuration)
                + (minion ? player("base_minion_duration_+%") : 0)) / 100d);
        foreach (string name in new[] { "support_reduced_duration_skill_effect_duration_+%_final",
            "support_more_duration_skill_effect_duration_+%_final",
            "support_swift_affliction_skill_effect_and_damaging_ailment_duration_+%_final" })
            value *= Math.Max(0, 1 + own(name) / 100d);
        return Math.Min(86400, value);
    }
}
