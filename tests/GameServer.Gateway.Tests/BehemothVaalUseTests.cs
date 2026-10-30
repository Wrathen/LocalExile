using System.Reflection;
using GameServer.Core.Protocol.Instance;

namespace GameServer.Gateway.Tests;

public partial class BehemothVaalUseTests
{
    static uint Key(string name) => Program.Stats.Key(name)!.Value;
    static object Call(string name, params object?[] args)
    {
        var m = typeof(Program).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        return m.Invoke(null, args.Concat(m.GetParameters().Skip(args.Length).Select(p => p.DefaultValue)).ToArray())!;
    }
    static (ushort Id, int Effect) Prepare(VaalSoulGatewayTests.Fixture f)
    {
        f.Gems("SkillGemVaalGroundSlam");
        foreach (var (name, value) in new[] { ("vaal_attack_rage_cost_instead_of_souls_per_use", 1),
            ("cannot_gain_rage_during_soul_gain_prevention", 1), ("gain_x_rage_on_hit", 10), ("no_inherent_rage_loss", 1),
            ("global_always_hit", 1) })
            f.Conn.PlayerStats[Key(name)] = value;
        var skills = (IReadOnlyList<GrantedSkill>)Call("PlayerSkills", f.Conn);
        int effect = Program.Skills.EffectRow("VaalGroundSlam");
        return (skills.Single(s => s.EffectsPerLevelRow == Program.Skills.FindByRow(0, effect, 1)!.EffectsPerLevelRow).Id, effect);
    }
    static RageState.Inputs Inputs(ClientState c)
    {
        var stats = Program.CharacterPlayerStats(c);
        return RageStats.Inputs(n => stats.GetValueOrDefault(Key(n)));
    }

    [Fact]
    public void Underfunded_Vaal_attack_refuses_before_action_cooldown_and_any_resource_payment()
    {
        using var f = new VaalSoulGatewayTests.Fixture(); var skill = Prepare(f); var c = f.Conn;
        Assert.True(c.Rage.Gain(14, 0, Inputs(c)));
        uint mana = c.PlayerMana; int action = c.ActionCount;
        Assert.False(Program.TryBeginSkillCooldownUse(c, skill.Id, skill.Effect, null, false,
            () => c.ActionCount++, out var ticket));
        Assert.Null(ticket); Assert.Equal(action, c.ActionCount); Assert.Equal(mana, c.PlayerMana);
        Assert.Equal(14, c.Rage.Read(0, Inputs(c)).Current); Assert.Null(c.VaalUse);
        Assert.Equal(0, c.VaalPreventionUntil); Assert.Empty(c.SkillReadyAt);
    }

    [Fact]
    public async Task Paid_Vaal_Ground_Slam_hits_once_and_SGP_prevents_Rage_until_exact_expiry()
    {
        using var f = new VaalSoulGatewayTests.Fixture(); var skill = Prepare(f); var c = f.Conn;
        await using var wire = new ProjectileRuntimeFixture(c);
        Assert.True(c.Rage.Gain(30, 0, Inputs(c)));
        uint mana = c.PlayerMana;
        var target = new SpawnedMonster("Vaal target", new(105, 100, 0), 0, 0, 100000);
        c.Monsters[43] = target;
        Assert.True(Program.TryBeginSkillCooldownUse(c, skill.Id, skill.Effect, null, false,
            () => c.ActionCount++, out _));
        Assert.Equal(15, c.Rage.Read(0, Inputs(c)).Current); Assert.Equal(mana, c.PlayerMana);
        Assert.Equal(1, Program.CharacterPlayerStats(c)[Key("has_soul_gain_prevention")]);
        Assert.False((bool)Call("RageOnHitLocked", c, true, true, false, null, null));
        Assert.True(await (Task<bool>)Call("CastGemSkill", "vaal-test", wire.Stream, wire.Ciphers,
            c, skill.Id, (100, 100), (105, 100), 43u, (ushort)0, c.ActionCount, null, false));
        Assert.True(target.Life < target.MaxLife);
        uint life = target.Life;
        Assert.False(await (Task<bool>)Call("CastGemSkill", "vaal-test", wire.Stream, wire.Ciphers,
            c, skill.Id, (100, 100), (105, 100), 43u, (ushort)0, c.ActionCount, null, false));
        Assert.Equal(life, target.Life); Assert.Equal(15, c.Rage.Read(0, Inputs(c)).Current);
        double expiry = c.VaalPreventionUntil;
        wire.Advance(expiry - .00001);
        Assert.Equal(1, Program.CharacterPlayerStats(c)[Key("has_soul_gain_prevention")]);
        wire.Advance(expiry);
        Assert.Equal(0, Program.CharacterPlayerStats(c).GetValueOrDefault(Key("has_soul_gain_prevention")));
        Assert.True((bool)Call("RageOnHitLocked", c, true, true, false, null, null));
        Assert.Equal(25, c.Rage.Read(expiry, Inputs(c)).Current);
        Program.RefreshVaalSouls(c); Assert.All(c.VaalSouls.Read(), b => Assert.Equal(0u, b.Capacity));
        await wire.Read();
    }

    [Fact]
    public void Approach_does_not_pay_and_actual_death_clears_the_reservation_and_prevention_before_healing()
    {
        using var f = new VaalSoulGatewayTests.Fixture(); var skill = Prepare(f); var c = f.Conn;
        Assert.True(c.Rage.Gain(30, 0, Inputs(c)));
        Assert.True(Program.TryBeginSkillCooldownUse(c, skill.Id, skill.Effect, null, false,
            () => c.ActionCount++, out _, reserve: false));
        Assert.Equal(30, c.Rage.Read(0, Inputs(c)).Current); Assert.Null(c.VaalUse);
        Assert.True(Program.TryBeginSkillCooldownUse(c, skill.Id, skill.Effect, null, false, () => { }, out _));
        Assert.NotNull(c.VaalUse); Assert.True(c.VaalPreventionUntil > 0);
        c.PlayerLife = 0; c.PlayerLife = 100;
        Assert.Null(c.VaalUse); Assert.Equal(0, c.VaalPreventionUntil); Assert.Equal(0, c.Rage.Read(0, Inputs(c)).Current);
    }
}
