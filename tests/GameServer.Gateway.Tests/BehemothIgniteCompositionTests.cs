using System.Reflection;
using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

public class BehemothIgniteCompositionTests
{
    static Task Burn(ProjectileRuntimeFixture wire) => (Task)typeof(Program)
        .GetMethod("BurnIgnited", BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, ["behemoth-ignite", wire.Stream, wire.Ciphers, wire.Conn])!;

    [Theory]
    [InlineData(true, false, 1)]
    [InlineData(false, false, 0)]
    [InlineData(true, true, 0)]
    public async Task A_projectiles_ignite_credits_one_owner_kill_without_a_melee_contact_or_boss_stage_kill(
        bool rampage, bool boss, int stacks)
    {
        using var game = new VaalSoulGatewayTests.Fixture();
        var c = game.Conn;
        c.PlayerStats[Program.Stats.Key(RampageSession.Source)!.Value] = rampage ? 1 : 0;
        Program.CharacterPlayerStats(c);
        await using var wire = new ProjectileRuntimeFixture(c);
        var target = new SpawnedMonster("ignite target", new(15, 10, 10, width: 300, height: 300), 0, 2, 100)
        { Stage = boss ? new(50, 2612, 3, 2) : null };
        c.Monsters[42] = target;
        var offense = new ProjectileOffense(DamagePacket.Of(DamageType.Fire, 10, 10), false, 0, 0, 150, "ignite")
        {
            AlwaysHits = true,
            Ignite = new(100, 100, 100, 100, 100, 4),
        };
        wire.Release(wire.Shot(offense: offense));
        wire.Advance(1);
        Assert.Equal(90u, target.Life);
        Assert.True(target.Ignites.Burning);
        Assert.NotNull(target.Ignites.Buff);
        Assert.Equal(0, c.Rampage.Current.Stacks); // Ranged contact is not a melee stack.
        await wire.Read();

        wire.Advance(2);
        await Burn(wire);
        Assert.Equal(stacks, c.Rampage.Current.Stacks);
        Assert.Equal(!boss, target.Dead);
        Assert.Equal(boss, target.StageRunning);
        if (!boss)
        {
            Assert.Equal(0u, target.Life);
            Assert.False(target.Ignites.Burning);
            Assert.Null(target.Ignites.Buff);
        }
        await Burn(wire); // Notification/tick retries cannot credit the same corpse again.
        Assert.Equal(stacks, c.Rampage.Current.Stacks);
        Assert.Equal(0, Program.LeechOf(c).Life.Count);
        await wire.Read();
    }
}
