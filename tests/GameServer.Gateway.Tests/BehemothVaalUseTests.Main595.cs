using System.Reflection;
using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

public partial class BehemothVaalUseTests
{
    sealed class ExactDraws(int bound, params int[] values) : Random
    {
        public int Calls;
        public override int Next(int maxValue)
        {
            Assert.Equal(bound, maxValue);
            Assert.True(Calls < values.Length, "Unexpected random draw");
            return values[Calls++];
        }
    }

    static void CriticalDraws(ClientState conn, Random random) => typeof(ClientState)
        .GetProperty("CriticalRandom", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(conn, random);

    static Task<bool> Cast(ProjectileRuntimeFixture wire, ushort skill) => (Task<bool>)Call("CastGemSkill",
        "vaal-main595", wire.Stream, wire.Ciphers, wire.Conn, skill, (100, 100), (105, 100),
        43u, (ushort)0, wire.Conn.ActionCount, null, false);

    static void Pay(ClientState conn, (ushort Id, int Effect) skill)
    {
        Assert.True(conn.Rage.Gain(30, 0, Inputs(conn)));
        Assert.True(Program.TryBeginSkillCooldownUse(conn, skill.Id, skill.Effect, null, false,
            () => conn.ActionCount++, out _));
    }

    [Fact]
    public async Task Ground_Slam_keeps_its_native_always_hit_flag_with_zero_raw_accuracy()
    {
        using var game = new VaalSoulGatewayTests.Fixture();
        var skill = Prepare(game); var c = game.Conn;
        c.PlayerStats[Key("global_always_hit")] = 0;
        c.PlayerStats[Key(RampageSession.Source)] = 1;
        c.PlayerStats[Key("life_leech_from_any_damage_permyriad")] = 100;
        c.Pools = c.Pools with { Accuracy = 0, AccuracyBase = 0 };
        c.PlayerLife = 500;
        var accuracy = new ExactDraws(100); c.AttackAccuracyRandom = accuracy;
        var critical = new ExactDraws(10000); CriticalDraws(c, critical);
        await using var wire = new ProjectileRuntimeFixture(c);
        var target = new SpawnedMonster("evades Vaal", new(105, 100, 0), 0, 0, 100000);
        c.Monsters[43] = target;
        Pay(c, skill);
        Assert.True(await Cast(wire, skill.Id));
        // Vaal Ground Slam's actual stat set includes global_always_hit. It does not invent an entropy roll.
        Assert.True(target.Life < target.MaxLife);
        Assert.Equal(0, accuracy.Calls); Assert.Equal(0, critical.Calls);
        Assert.Equal(-1, target.AttackEvasion.Value);
        Assert.Equal(1, c.Rampage.Current.Stacks);
        Assert.Equal(1, Program.LeechOf(c).Life.Count);
        Assert.Equal(15, c.Rage.Read(Program.ProjectileWorldTime(c), Inputs(c)).Current);
        await wire.Read();
    }

    [Fact]
    public async Task Vaal_payment_captures_low_life_luck_before_held_output_and_shares_one_roll_across_targets()
    {
        using var game = new VaalSoulGatewayTests.Fixture();
        var skill = Prepare(game); var c = game.Conn;
        c.PlayerStats[Key("global_cannot_crit")] = 0;
        c.PlayerStats[Key(AttackBaseCriticalStrike.WeaponOverrideStat)] = 1000;
        c.PlayerStats[Key("physical_damage_+%")] = -10000;
        c.PlayerStats[Key("extra_critical_rolls_while_on_low_life")] = 1;
        c.PlayerLife = 100;
        var critical = new ExactDraws(10000, 9000, 0); CriticalDraws(c, critical);
        var accuracy = new ExactDraws(100); c.AttackAccuracyRandom = accuracy;
        await using var wire = new ProjectileRuntimeFixture(c);
        var first = new SpawnedMonster("first", new(105, 100, 0), 0, 0, 100000);
        var second = new SpawnedMonster("second", new(100, 105, 0), 0, 0, 100000);
        c.Monsters[43] = first; c.Monsters[44] = second;
        Pay(c, skill);
        Assert.Equal(0, critical.Calls); // The admitted roll allocates its random values only on an accepted hit.
        await wire.HoldOutput();
        var cast = Cast(wire, skill.Id);
        Assert.False(cast.IsCompleted);
        c.PlayerLife = c.PlayerMaxLife;
        wire.ReleaseOutput();
        Assert.True(await cast.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(first.Life < first.MaxLife);
        Assert.Equal(first.MaxLife - first.Life, second.MaxLife - second.Life);
        Assert.Equal(2, critical.Calls); // Original lucky admission, one shared best-of-two roll for the whole use.
        Assert.Equal(0, accuracy.Calls); // Always-hit bypasses entropy and critical confirmation.
        await wire.Read();
    }

    [Fact]
    public async Task Ground_Slam_generates_scoped_leech_from_direct_damage_before_impale_reflection()
    {
        using var game = new VaalSoulGatewayTests.Fixture();
        var skill = Prepare(game); var c = game.Conn;
        c.PlayerStats[Key("life_leech_from_any_damage_permyriad")] = 100;
        c.PlayerStats[Key("physical_damage_+%")] = -10000;
        c.PlayerLife = 500;
        await using var wire = new ProjectileRuntimeFixture(c);
        var target = new SpawnedMonster("leech target", new(105, 100, 0), 0, 0, 100000);
        c.Monsters[43] = target;
        Assert.True(target.Impale.Apply(5000, Program.ProjectileWorldTime(c)));
        int reflected = Program.MonsterImpaleDamage(target, 500);
        Pay(c, skill);
        Assert.True(await Cast(wire, skill.Id));
        int total = (int)(target.MaxLife - target.Life);
        Assert.True(total > reflected);
        Assert.Equal(1, Program.LeechOf(c).Life.Count);
        var recovered = Program.LeechOf(c).TakeRecovery(Program.ProjectileWorldTime(c) + 20);
        Assert.Equal((total - reflected) * .01, recovered.Life, 6);
        await wire.Read();
    }
}
