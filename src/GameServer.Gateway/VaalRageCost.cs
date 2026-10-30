/// <summary>3.29.3b's self-used Vaal attack cost conversion. Native 0x1428a8660 adds raw soul cost after the
/// ordinary base Rage cost, then applies generic/Rage increases, the final cost modifier and efficiency.
/// The converted portion therefore receives neither support cost multipliers nor soul-cost reductions.
/// 0x1428c3380 derives no-souls eligibility from the same Vaal, attack, self and conversion flags.</summary>
static class VaalRageCost
{
    public readonly record struct Modifiers(int GenericIncreasedPercent = 0, int RageIncreasedPercent = 0,
        int FinalCostPercent = 0, int GenericEfficiencyPercent = 0, int RageEfficiencyPercent = 0,
        bool NoCost = false);

    public static bool ReplacesSouls(bool conversion, bool vaal, bool attack, bool selfUsed) =>
        conversion && vaal && attack && selfUsed;

    public static int SoulsRequired(int rawSouls, bool conversion, bool vaal, bool attack, bool selfUsed) =>
        ReplacesSouls(conversion, vaal, attack, selfUsed) ? 0 : Math.Max(0, rawSouls);

    public static int Calculate(int totalBaseRageCost, int rawSouls, bool conversion, bool vaal, bool attack,
        bool selfUsed, Modifiers modifiers = default)
    {
        if (modifiers.NoCost) return 0;
        decimal value = totalBaseRageCost;
        if (ReplacesSouls(conversion, vaal, attack, selfUsed)) value += rawSouls;
        value += decimal.Truncate(value * ((decimal)modifiers.GenericIncreasedPercent
            + modifiers.RageIncreasedPercent) / 100);
        value += decimal.Truncate(value * modifiers.FinalCostPercent / 100);
        decimal denominator = 100m + modifiers.GenericEfficiencyPercent + modifiers.RageEfficiencyPercent;
        if (denominator <= 0) return value == 0 ? 0 : int.MaxValue;
        return (int)Math.Clamp(decimal.Round(value * 100 / denominator, 0, MidpointRounding.AwayFromZero),
            0, int.MaxValue);
    }
}
