namespace GameServer.Gateway.Tests;

public partial class ConditionalCriticalGatewayTests
{
    [Fact]
    public async Task A_Rampage_source_cannot_credit_evaded_main_area_contacts_or_generate_leech()
    {
        var critical = new Draws();
        using var f = new Contact(critical, "Cyclone");
        f.Set(RampageSession.Source, 1);
        f.Set("life_leech_from_any_damage_permyriad", 100);
        f.Conn.Pools = f.Conn.Pools with { Accuracy = 0, AccuracyBase = 0 };
        var accuracy = new AccuracyDraws(0);
        f.Conn.AttackAccuracyRandom = accuracy;
        Assert.True(f.Target.Impale.Apply(100, Program.ProjectileWorldTime(f.Conn)));
        double previousHit = f.Conn.LastEnemyHitAt;
        await f.Sweep();
        Assert.Equal(f.Target.MaxLife, f.Target.Life);
        Assert.Equal(previousHit, f.Conn.LastEnemyHitAt);
        Assert.Equal(0, f.Conn.Rampage.Current.Stacks);
        Assert.Equal(0, Program.LeechOf(f.Conn).Life.Count);
        Assert.Equal(0, critical.Calls);
        Assert.Equal(1, accuracy.Calls);
        for (int i = 0; i < 4; i++) f.Target.Impale.AfterHit(Program.ProjectileWorldTime(f.Conn));
        Assert.Equal(10, f.Target.Impale.Bonus);
    }
}
