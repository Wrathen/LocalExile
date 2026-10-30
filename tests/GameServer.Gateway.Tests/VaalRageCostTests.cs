namespace GameServer.Gateway.Tests;

public class VaalRageCostTests
{
    [Theory]
    [InlineData(15)] // Real 3.29.3b Vaal Ground Slam / Vaal Molten Strike.
    [InlineData(20)]
    [InlineData(30)] // Vaal Double Strike.
    public void The_conversion_flag_uses_raw_souls_as_rage_instead_of_a_one_rage_cost(int rawSouls)
    {
        Assert.Equal(rawSouls, VaalRageCost.Calculate(0, rawSouls, true, true, true, true));
        Assert.Equal(0, VaalRageCost.SoulsRequired(rawSouls, true, true, true, true));
    }

    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void Normal_skills_spells_and_proxy_uses_keep_their_soul_policy(bool conversion, bool vaal,
        bool attack, bool self)
    {
        Assert.Equal(3, VaalRageCost.Calculate(3, 30, conversion, vaal, attack, self));
        Assert.Equal(30, VaalRageCost.SoulsRequired(30, conversion, vaal, attack, self));
    }

    [Fact]
    public void Ordinary_base_rage_and_converted_souls_add_before_both_cost_increases()
    {
        // Native adds raw 15 to an existing 3, then combines -10 generic and -15 Rage increases:
        // 18 + trunc(-4.5) = 14. The converted portion has no support-multiplier input.
        var mods = new VaalRageCost.Modifiers(GenericIncreasedPercent: -10, RageIncreasedPercent: -15);
        Assert.Equal(14, VaalRageCost.Calculate(3, 15, true, true, true, true, mods));
        // Final cost applies to the already modified 14: 14 + trunc(-7) = 7.
        Assert.Equal(7, VaalRageCost.Calculate(3, 15, true, true, true, true,
            mods with { FinalCostPercent = -50 }));
    }

    [Fact]
    public void Efficiencies_add_in_the_denominator_and_upfront_cost_rounds_half_away_from_zero()
    {
        var mods = new VaalRageCost.Modifiers(GenericEfficiencyPercent: 40, RageEfficiencyPercent: 60);
        Assert.Equal(8, VaalRageCost.Calculate(0, 15, true, true, true, true, mods)); // 7.5 -> 8.
        Assert.Equal(5, VaalRageCost.Calculate(0, 20, true, true, true, true,
            mods with { GenericEfficiencyPercent = 240 })); // 20 / 4 = 5.
        Assert.Equal(15, VaalRageCost.Calculate(0, 15, true, true, true, true,
            mods with { GenericEfficiencyPercent = 0, RageEfficiencyPercent = 0 }));
    }

    [Fact]
    public void No_cost_and_extreme_signed_modifiers_cannot_wrap_the_final_cost()
    {
        var huge = new VaalRageCost.Modifiers(int.MaxValue, int.MaxValue, int.MaxValue);
        Assert.Equal(int.MaxValue, VaalRageCost.Calculate(int.MaxValue, int.MaxValue, true, true, true, true, huge));
        Assert.Equal(0, VaalRageCost.Calculate(int.MaxValue, int.MaxValue, true, true, true, true,
            huge with { NoCost = true }));
        Assert.Equal(0, VaalRageCost.Calculate(0, 15, true, true, true, true,
            new(GenericIncreasedPercent: -200)));
        Assert.Equal(int.MaxValue, VaalRageCost.Calculate(0, 15, true, true, true, true,
            new(GenericEfficiencyPercent: -100)));
        Assert.Equal(0, VaalRageCost.Calculate(0, 0, true, true, true, true,
            new(GenericEfficiencyPercent: -100)));
    }
}
