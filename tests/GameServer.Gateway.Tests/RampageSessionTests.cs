namespace GameServer.Gateway.Tests;

public class RampageSessionTests
{
    [Fact]
    public void Melee_contact_and_its_kill_each_grant_once_but_repeated_death_notices_do_not()
    {
        var s = new RampageSession(); var monster = new object();
        Assert.True(s.MeleeHit(0, true, true, true, true, true));
        Assert.True(s.Kill(0, monster, true, true, true, true));
        Assert.False(s.Kill(1, monster, true, true, true, true));
        Assert.Equal(2, s.Current.Stacks);
        s.Observe(6, true, true, true);
        Assert.False(s.Kill(6, monster, true, true, true, true));
        Assert.Equal(0, s.Current.Stacks);
    }

    [Theory]
    [InlineData(false, true, true, true, true)]
    [InlineData(true, false, true, true, true)]
    [InlineData(true, true, false, true, true)]
    [InlineData(true, true, true, false, true)]
    [InlineData(true, true, true, true, false)]
    public void Ineligible_contacts_do_not_create_a_streak(bool enabled, bool alive, bool active, bool melee, bool hostile)
    {
        var s = new RampageSession();
        Assert.False(s.MeleeHit(0, enabled, alive, active, melee, hostile));
        Assert.Equal(0, s.Current.Stacks);
    }

    [Fact]
    public void All_thirteen_tiers_are_pending_once_and_expiry_discards_unexecuted_old_streak_work()
    {
        var s = new RampageSession();
        for (int i = 0; i < 1000; i++) Assert.True(s.MeleeHit(0, true, true, true, true, true));
        Assert.Equal(RampageSession.Thresholds, s.PendingTiers);
        Assert.Equal(1000, s.Current.Stacks);
        Assert.Equal(100, s.Current.DamagePercent); Assert.Equal(50, s.Current.MovementPercent);
        s.Observe(5, true, true, true);
        Assert.Empty(s.PendingTiers);
        Assert.Equal(0, s.Current.Stacks);
        for (int i = 0; i < 15; i++) Assert.True(s.MeleeHit(5, true, true, true, true, true));
        Assert.Equal(new[] { 15 }, s.PendingTiers);
    }

    [Fact]
    public void Source_removal_advances_the_watermark_and_old_events_cannot_revive_it()
    {
        var s = new RampageSession();
        s.MeleeHit(1, true, true, true, true, true);
        s.Observe(3, false, true, true);
        Assert.False(s.MeleeHit(2, true, true, true, true, true));
        Assert.Equal(0, s.Current.Stacks);
        var monster = new object();
        Assert.False(s.Kill(2, monster, true, true, true, true));
        Assert.True(s.Kill(3, monster, true, true, true, true));
    }
}
