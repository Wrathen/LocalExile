namespace GameServer.Gateway.Tests;

public class RampageStateTests
{
    static RampageState Streak() => new([15, 30, 45, 70, 100, 135, 175, 220, 270, 325, 385, 450, 520]);

    [Fact]
    public void Fourteenth_to_fifteenth_increment_crosses_the_first_tier_once()
    {
        var state = Streak();
        var fourteen = state.GainQualified(0, 14);
        Assert.True(fourteen.Accepted);
        Assert.Empty(fourteen.CrossedThresholds);
        Assert.Equal(14, fourteen.After.Stacks);
        var fifteen = state.GainQualified(1);
        Assert.Equal(new[] { 15 }, fifteen.CrossedThresholds);
        Assert.Equal(new RampageState.Value(15, 6), fifteen.After);
        Assert.Empty(state.Advance(1).CrossedThresholds);
        Assert.Empty(state.GainQualified(1).CrossedThresholds);
    }

    [Theory]
    [InlineData(19, 0, 0)]
    [InlineData(20, 2, 1)]
    [InlineData(39, 2, 1)]
    [InlineData(40, 4, 2)]
    [InlineData(999, 98, 49)]
    [InlineData(1000, 100, 50)]
    public void Bonuses_use_complete_twenty_stack_groups(int count, int damage, int movement)
    {
        var value = Streak().GainQualified(0, count).After;
        Assert.Equal(damage, value.DamagePercent);
        Assert.Equal(movement, value.MovementPercent);
    }

    [Fact]
    public void A_qualified_event_at_cap_refreshes_time_without_retriggering_tiers()
    {
        var state = Streak();
        Assert.Equal(13, state.GainQualified(0, 1000).CrossedThresholds.Count);
        var gain = state.GainQualified(4, 1000);
        Assert.Equal(1000, gain.After.Stacks);
        Assert.Equal(9d, gain.After.ExpiresAt);
        Assert.Empty(gain.CrossedThresholds);
        Assert.Equal(1000, state.Advance(5).After.Stacks);
        Assert.Equal(default, state.Advance(9).After);
    }

    [Fact]
    public void Exact_five_second_expiry_clears_bonuses_and_allows_the_next_streaks_tiers()
    {
        var state = Streak();
        state.GainQualified(0, 45);
        var before = state.Advance(4.999);
        Assert.True(before.After.Active);
        Assert.InRange(before.After.RemainingSeconds(4.999), .000999, .001001);
        var expired = state.Advance(5);
        Assert.True(expired.Changed);
        Assert.False(expired.After.Active);
        Assert.Equal(default, expired.After);
        Assert.Equal(new[] { 15, 30, 45 }, state.GainQualified(5, 45).CrossedThresholds);
    }

    [Fact]
    public void An_event_at_the_deadline_starts_a_new_streak_before_awarding_tiers()
    {
        var state = Streak();
        state.GainQualified(0, 100);
        var gain = state.GainQualified(5, 15);
        Assert.Equal(15, gain.After.Stacks);
        Assert.Equal(10d, gain.After.ExpiresAt);
        Assert.Equal(new[] { 15 }, gain.CrossedThresholds);
    }

    [Fact]
    public void Repeated_paused_game_time_does_not_expire_the_streak()
    {
        var state = Streak();
        state.GainQualified(10, 20);
        for (int i = 0; i < 50; i++)
        {
            var update = state.Advance(12);
            Assert.True(update.Accepted);
            Assert.Equal(new RampageState.Value(20, 15), update.After);
            Assert.Equal(3d, update.After.RemainingSeconds(12));
        }
        Assert.False(state.Advance(15).After.Active);
    }

    [Fact]
    public void One_batch_returns_every_crossed_tier_in_order_and_omits_old_tiers()
    {
        var state = Streak();
        Assert.Equal(new[] { 15, 30, 45, 70 }, state.GainQualified(0, 99).CrossedThresholds);
        Assert.Equal(new[] { 100, 135, 175, 220, 270, 325 }, state.GainQualified(1, 226).CrossedThresholds);
        Assert.Equal(new[] { 385, 450, 520 }, state.GainQualified(2, 675).CrossedThresholds);
        Assert.Empty(state.GainQualified(3).CrossedThresholds);
    }

    [Fact]
    public void Qualification_is_explicit_and_equal_time_events_have_no_invented_cooldown()
    {
        var state = Streak();
        for (int i = 0; i < 100; i++) Assert.True(state.GainQualified(1).Accepted);
        Assert.Equal(100, state.Current.Stacks);
        Assert.Equal(6d, state.Current.ExpiresAt);
    }

    [Fact]
    public void Invalid_time_or_count_never_mutates_or_refreshes_the_state()
    {
        var state = Streak();
        state.GainQualified(2, 20);
        var expected = state.Current;
        foreach (double time in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1, 1, double.MaxValue })
        {
            Assert.False(state.Advance(time).Accepted);
            Assert.False(state.GainQualified(time).Accepted);
            Assert.Equal(expected, state.Current);
        }
        foreach (int count in new[] { int.MinValue, -1, 0, 1001, int.MaxValue })
        {
            Assert.False(state.GainQualified(100, count).Accepted);
            Assert.Equal(expected, state.Current);
        }
        Assert.True(state.GainQualified(3).Accepted); // rejected future input did not advance the watermark.
    }

    [Fact]
    public void Clear_permits_retriggering_but_keeps_the_watermark_against_older_events()
    {
        var state = Streak();
        state.GainQualified(10, 520);
        Assert.True(state.Clear());
        Assert.False(state.Clear());
        Assert.False(state.GainQualified(9).Accepted);
        Assert.Equal(default, state.Current);
        Assert.Equal(new[] { 15 }, state.GainQualified(10, 15).CrossedThresholds);
    }

    [Fact]
    public void Thresholds_are_copied_and_invalid_orders_or_ranges_are_rejected()
    {
        int[] fixture = [2, 4];
        var state = new RampageState(fixture);
        fixture[0] = 3;
        Assert.Equal(new[] { 2 }, state.GainQualified(0, 2).CrossedThresholds);
        foreach (int[] invalid in new int[][] { [0], [-1], [1001], [2, 2], [4, 2] })
            Assert.Throws<ArgumentException>(() => new RampageState(invalid));
        Assert.Empty(new RampageState([]).GainQualified(0).CrossedThresholds);
    }
}
