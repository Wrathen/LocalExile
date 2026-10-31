using GameServer.Core.Protocol.Instance;

namespace GameServer.Gateway.Tests;

public partial class ConditionalCriticalGatewayTests
{
    [Theory]
    [InlineData(null, 1)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public void Life_overleech_virtual_replacement_controls_projection_and_recovery(int? replacement, int expected)
    {
        using var f = new Contact(new Draws());
        f.Set("life_leech_does_not_stop_at_full_life", 1);
        if (replacement is int value) f.Set("virtual_life_leech_does_not_stop_at_full_life", value);
        f.Conn.PlayerLife = f.Conn.PlayerMaxLife;
        var stats = Program.CharacterPlayerStats(f.Conn);
        Assert.Equal(expected, stats.GetValueOrDefault(Program.Stats.Key("virtual_life_leech_does_not_stop_at_full_life")!.Value));
        var session = Program.LeechOf(f.Conn);
        session.Hit(f.Conn.Clock.Elapsed.TotalSeconds, (100, 0, 0), true, false, false);
        Assert.Equal(expected, session.Life.Count);
    }
}
