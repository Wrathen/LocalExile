using System.Reflection;
using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

public class BehemothMainCompositionTests
{
    static object? Call(string name, params object?[] args) => typeof(Program)
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);

    [Fact]
    public void Rampage_weapon_targeting_uses_main_cadence_and_stun_invalidates_the_reserved_attack()
    {
        using var fixture = new VaalSoulGatewayTests.Fixture();
        var c = fixture.Conn;
        var enemy = new SpawnedMonster("enemy", new(105, 100, 10), 60, 5, 100)
        { Attack = new(0x102, 1, 8, DamagePacket.Of(DamageType.Physical, 10, 10)) };
        var weapon = new SpawnedMonster("weapon", new(106, 100, 10), 60, 5, 100)
        { OwnerId = Program.PlayerId, Hostile = false };
        c.Monsters[42] = enemy; c.Monsters[43] = weapon;
        c.RampageWorld.Actors[43] = new(43, c.RampageWorld.Epoch, RampageCatalog.WeaponKind.Dagger,
            1, 37.5, 0, 406);
        var cadence = new MonsterAiCadence();
        var first = Program.PlanMonsterAi(c, cadence, 0);
        Assert.Empty(first.Swings);
        var reserved = Assert.Single(first.WeaponSwings);
        Assert.Equal(43u, reserved.Target);
        Assert.True(Program.MonsterActionCurrent(c, 42, enemy, reserved.Action));
        Assert.Empty(Program.PlanMonsterAi(c, cadence, .05).WeaponSwings);
        var stun = Program.RampageData.Tiers.Single(t => t.Threshold == 15).Skill.AtLevel(1)!;
        Call("RampageStatusLocked", c, 42u, enemy, stun, .1);
        Assert.False(Program.MonsterActionCurrent(c, 42, enemy, reserved.Action));
        Assert.Null(enemy.SwingGeneration);
        Assert.Empty(Program.PlanMonsterAi(c, cadence, 1).WeaponSwings);
        Assert.Single(Program.PlanMonsterAi(c, cadence, 1.4).WeaponSwings);
    }

    [Fact]
    public void Rampage_movement_keeps_the_live_chill_action_factor_and_recovers_when_chill_expires()
    {
        using var fixture = new VaalSoulGatewayTests.Fixture();
        var c = fixture.Conn;
        for (int i = 0; i < 100; i++) c.Rampage.MeleeHit(0, true, true, true, true, true);
        var cold = DamagePacket.Of(DamageType.Cold, 10, 10);
        c.ElementalAilments.Apply(cold, cold, false, 100, 0, _ => 0, new Random(1));
        c.ApplyMovementSpeedAt(0);
        Assert.Equal(Program.WalkSpeed * 1.05 * c.ElementalAilments.ActionFactor(0), c.Walk.Speed, 8);
        Assert.True(c.Walk.Speed < Program.WalkSpeed);
        c.ApplyMovementSpeedAt(2);
        Assert.Equal(Program.WalkSpeed * 1.05, c.Walk.Speed, 8);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Smoke_and_Rampage_blind_have_one_penalty_and_independent_expiry(bool smoke, bool rampage)
    {
        using var fixture = new VaalSoulGatewayTests.Fixture();
        var c = fixture.Conn;
        var target = new SpawnedMonster("target", new(105, 100, 0), 0, 0, 100)
        { RampageBlindUntil = rampage ? 1 : 0 };
        c.FlaskGrounds.SetBlinded(42, smoke, () => 1);
        int Value(double at) => Program.RampageBlindedValue(target, at, 101, c.FlaskGrounds.Accuracy(42, 101));
        Assert.Equal(80, Value(0));
        Assert.Equal(smoke ? 80 : 101, Value(1));
        c.FlaskGrounds.SetBlinded(42, false, () => 1);
        Assert.Equal(101, Value(1));
    }

    [Fact]
    public void Boss_stage_keeps_main_action_invalidation_without_granting_a_Rampage_kill()
    {
        using var fixture = new VaalSoulGatewayTests.Fixture();
        var c = fixture.Conn;
        c.PlayerStats[Program.Stats.Key(RampageSession.Source)!.Value] = 1;
        var target = new SpawnedMonster("boss", new(105, 100, 0), 0, 0, 100)
        { Stage = new(50, 2612, 3, 2), ActionGeneration = 7, SwingGeneration = 7 };
        var hit = ((uint Life, bool Stage))Call("TakeOwnerDamageLocked", c, target, 100)!;
        Assert.True(hit.Stage);
        Assert.True(hit.Life > 0);
        Assert.Null(target.SwingGeneration);
        Assert.Equal(8, target.ActionGeneration);
        Assert.Equal(0, c.Rampage.Current.Stacks);
    }
}
