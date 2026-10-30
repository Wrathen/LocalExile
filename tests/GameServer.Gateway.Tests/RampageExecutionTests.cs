using System.Reflection;
using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

public class RampageExecutionTests
{
    static uint Key(string name) => Program.Stats.Key(name)!.Value;
    static T Call<T>(string name, params object?[] arguments) => (T)typeof(Program)
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments)!;

    static ClientState Character()
    {
        TestSaves.Ensure();
        var c = new ClientState(new(1, "Rampage effects", 300, 300, 10, 10, null, []), 1, 1)
        { Character = "Rampage" + Guid.NewGuid().ToString("N"), ClassRow = 0,
            Pools = new(1, 100, 100, 100, 0), PlayerLife = 100, PlayerMana = 100 };
        c.PlayerStats[Key(RampageSession.Source)] = 1;
        Program.CharacterPlayerStats(c);
        return c;
    }

    [Fact]
    public void All_thirteen_real_plans_have_executable_steps_and_independent_repeat_channels()
    {
        var data = new RampageCatalog(); Assert.Null(data.Error);
        Assert.Equal(13, data.Tiers.Count);
        foreach (var tier in data.Tiers)
        {
            var steps = RampageExecution.Steps(tier, 1);
            Assert.NotEmpty(steps);
            Assert.Equal(0, steps[0].Delay);
            Assert.True(steps.SequenceEqual(steps.OrderBy(s => s.Delay)));
            if (tier.Summons.Count > 0) Assert.Equal(RampageExecution.StepKind.Weapons, Assert.Single(steps).Kind);
            if (steps.Any(s => s.Kind == RampageExecution.StepKind.Projectiles))
                Assert.NotNull(Program.Skills.ProjectileFor(tier.Skill.Row));
            Assert.True(Program.Monsters.Find(tier.Daemon.Row.ToString())?.Supported);
        }
        var combined = RampageExecution.Steps(data.Tiers.Single(t => t.Threshold == 520), 1);
        Assert.Equal(29, combined.Count); // initial + 20 ripples + 8 separate repeats, never 21 * 9.
        Assert.Contains(combined, s => s.Delay == 2 && s.RadiusPercent == 0);
    }

    [Fact]
    public async Task An_uncaptured_area_does_not_treat_its_unknown_dimensions_as_an_immediate_terrain_wall()
    {
        var c = Character();
        c.Use(c.Layout with { GridW = 0, GridH = 0 });
        try
        {
            await using var f = new ProjectileRuntimeFixture(c);
            var target = ProjectileRuntimeFixture.Monster(15, 10); c.Monsters[42] = target;
            f.Release(f.Shot(offense: new(DamagePacket.Of(DamageType.Chaos, 10, 10), true, 0, 0, 150, "uncaptured")
                { RampageEpoch = c.RampageWorld.Epoch }));
            f.Advance(1); Assert.Equal(90u, target.Life); await f.Read();
        }
        finally { PlayerInventories.Forget(c.Character!); }
    }

    [Fact]
    public async Task Owned_projectiles_bypass_owner_spell_suppression_without_granting_owner_hit_rage()
    {
        var c = Character();
        try
        {
            c.PlayerStats[Key("spell_skills_deal_no_damage")] = 1;
            c.PlayerStats[Key("gain_x_rage_on_hit")] = 10;
            c.PlayerStats[Key("life_leech_from_any_damage_permyriad")] = 10000;
            c.PlayerLife = 50;
            Program.CharacterPlayerStats(c);
            await using var f = new ProjectileRuntimeFixture(c);
            var target = ProjectileRuntimeFixture.Monster(15, 10);
            c.Monsters[42] = target;
            var shot = new ProjectileOffense(DamagePacket.Of(DamageType.Chaos, 10, 10), true,
                0, 0, 150, "owned test") { RampageEpoch = c.RampageWorld.Epoch };
            f.Release(f.Shot(offense: shot)); f.Advance(1);
            Assert.Equal(90u, target.Life);
            Assert.Equal(0, c.Rage.Read(1, new()).Current);
            Assert.Equal(50u, c.PlayerLife);
            Assert.Equal(0, Program.LeechOf(c).Life.Count);
            await f.Read();
            c.RampageWorld.Disable();
            f.Release(f.Shot(offense: shot)); f.Advance(2);
            Assert.Equal(90u, target.Life);
        }
        finally { PlayerInventories.Forget(c.Character!); }
    }

    [Fact]
    public async Task Blind_refresh_sends_once_and_source_removal_clears_native_and_authoritative_state()
    {
        var c = Character();
        try
        {
            await using var f = new ProjectileRuntimeFixture(c);
            var target = ProjectileRuntimeFixture.Monster(15, 10); c.Monsters[42] = target;
            var own = Program.RampageData.Tiers.Single(t => t.Threshold == 100).Skill.AtLevel(1)!;
            Call<object?>("RampageStatusLocked", c, 42u, target, own, 0d);
            Assert.Equal(10, target.RampageBlindUntil);
            await Call<Task>("ExpireRampageBlinds", "test", f.Stream, f.Ciphers, c);
            Assert.NotEmpty(await f.Read());
            await Call<Task>("ExpireRampageBlinds", "test", f.Stream, f.Ciphers, c);
            Assert.Empty(await f.Read());
            c.RampageWorld.Disable();
            await Call<Task>("ExpireRampageBlinds", "test", f.Stream, f.Ciphers, c);
            Assert.NotEmpty(await f.Read()); Assert.Equal(0, target.RampageBlindUntil);
        }
        finally { PlayerInventories.Forget(c.Character!); }
    }

    [Fact]
    public void Stun_cancels_pending_attacks_and_owned_weapons_never_persist_as_area_monsters()
    {
        var c = Character();
        try
        {
            var target = ProjectileRuntimeFixture.Monster(15, 10);
            var own = Program.RampageData.Tiers.Single(t => t.Threshold == 15).Skill.AtLevel(1)!;
            Call<object?>("RampageStatusLocked", c, 42u, target, own, 1d);
            Assert.Equal(1, target.StunSerial); Assert.Equal(2.225, target.BusyUntil, 6);
            var weapon = new SpawnedMonster("owned", new(10, 10, 10), 30, 5, 100)
            { OwnerId = Program.PlayerId, Hostile = false,
                Variety = Program.Monsters.Find("3192") };
            Assert.Empty(SavedMonster.Save([weapon], 0));
            long old = c.RampageWorld.Epoch;
            c.RampageWorld.Actors[2] = new(2, old, RampageCatalog.WeaponKind.Dagger, 1, 37.5, 0, 406);
            c.RampageWorld.Disable(); c.RampageWorld.Enable();
            Assert.False(c.RampageWorld.Current(old)); Assert.Contains(2u, c.RampageWorld.PendingRemoval);
        }
        finally { PlayerInventories.Forget(c.Character!); }
    }

    [Fact]
    public void Carry_preserves_remaining_time_without_replaying_tiers_and_expires_at_the_destination_deadline()
    {
        var first = new RampageSession();
        for (int i = 0; i < 20; i++) first.MeleeHit(1, true, true, true, true, true);
        var carried = first.Carry(3);
        Assert.Equal(20, carried.Stacks); Assert.Equal(3, carried.Remaining);
        Assert.Empty(first.PendingTiers); Assert.Equal(0, first.Current.Stacks);
        var next = new RampageSession(); next.Restore(carried, 100);
        Assert.Equal(20, next.Observe(102.999, true, true, true).Stacks);
        Assert.Empty(next.PendingTiers);
        Assert.Equal(0, next.Observe(103, true, true, true).Stacks);
    }

    [Fact]
    public async Task Movement_speed_resets_before_the_next_order_after_delayed_expiry()
    {
        var c = Character();
        try
        {
            await using var f = new ProjectileRuntimeFixture(c);
            c.Walk.Teleport(10, 10, 0);
            for (int i = 0; i < 1000; i++) c.Rampage.MeleeHit(0, true, true, true, true, true);
            c.ApplyMovementSpeedAt(0);
            f.Advance(6);
            Program.CharacterPlayerStats(c);
            Assert.Equal(Program.WalkSpeed, c.Walk.Speed);
            c.Walk.Order(80, 10, 6);
            Assert.InRange(c.Walk.Position(7).X, 46, 48);
        }
        finally { PlayerInventories.Forget(c.Character!); }
    }
}
