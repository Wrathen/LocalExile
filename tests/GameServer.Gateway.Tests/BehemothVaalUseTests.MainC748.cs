namespace GameServer.Gateway.Tests;

public partial class BehemothVaalUseTests
{
    [Fact]
    public async Task Main_weapon_restrictions_refuse_Vaal_input_before_Rage_payment_or_action_reservation()
    {
        using var game = new VaalSoulGatewayTests.Fixture();
        var skill = Prepare(game); var c = game.Conn;
        var host = Assert.Single(game.Items.Items(PlayerInventories.Weapon1));
        game.Items.Change(PlayerInventories.Weapon1, host.Id, item =>
            ItemCatalog.Create(Program.Items.Find("Driftwood Wand")!) with { Sockets = item.Sockets, Links = item.Links });
        c.Pools = Program.PoolsFor(c, 100);
        Assert.True(c.Rage.Gain(30, 0, Inputs(c)));
        var target = game.Monster(corpse: false);
        int action = c.ActionCount;
        uint mana = c.PlayerMana;
        using var wire = new VaalSoulGatewayTests.Wire();
        await wire.Dispatch(c, game.Id(target), skill.Id);
        Assert.Equal(action, c.ActionCount);
        Assert.Equal(mana, c.PlayerMana);
        Assert.Equal(30, c.Rage.Read(Program.ProjectileWorldTime(c), Inputs(c)).Current);
        Assert.Null(c.VaalUse);
        Assert.Equal(0, c.VaalPreventionUntil);
        Assert.Empty(c.SkillReadyAt);
        Assert.Equal(target.MaxLife, target.Life);
    }
}
