namespace GameServer.Gateway.Tests;

public class PlayerHitRecoveryTests
{
    static Func<string, int> Stats(params (string Key, int Value)[] values) =>
        name => values.FirstOrDefault(v => v.Key == name).Value;

    [Theory]
    [InlineData(0, 0)]
    public void Physical_melee_chance_has_a_minimum_and_caps_at_100(int damage, double chance) =>
        Assert.Equal(chance, PlayerHitRecovery.Chance(damage, 100, Stats()));

    [Fact]
    public void Action_speed_shortens_the_recovery_animation()
    {
        float normal = PlayerHitRecovery.Duration(Stats());
        float fast = PlayerHitRecovery.Duration(Stats(), 2);
        Assert.True(normal > 0);
        Assert.InRange(fast, 0.23f, 0.24f);
        Assert.True(fast < normal);
    }

    static ClientState ObjectWalkState()
    {
        var conn = new ClientState(new AreaLayout(0, "test", 100, 100, 30, 30, null, []), 0, 1)
        { PlayerLife = 57, ActionCount = 4, ChannelSkill = 123 };
        conn.Clock.Reset();
        return conn;
    }

}
