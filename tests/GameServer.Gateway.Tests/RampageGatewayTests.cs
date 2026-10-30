using System.Reflection;
using GameServer.Core.Protocol.Instance;

namespace GameServer.Gateway.Tests;

public class RampageGatewayTests
{
    sealed class Character : IDisposable
    {
        public ClientState State { get; }
        public Character()
        {
            TestSaves.Ensure();
            State = new(new(0, "Rampage", 128, 128, 32, 32, null, []), 0, 1)
            { Character = "Rampage" + Guid.NewGuid().ToString("N"), ClassRow = 0,
                Pools = new(100, 500, 100, 100, 0), PlayerLife = 500, PlayerMana = 100, PlayerSent = true };
            State.PlayerStats[Key(RampageSession.Source)] = 1;
        }
        public void Dispose() => PlayerInventories.Forget(State.Character!);
    }
    static uint Key(string name) => Program.Stats.Key(name)!.Value;
    static void Contact(ClientState state, SpawnedMonster monster, bool melee = true) =>
        typeof(Program).GetMethod("RampageOnMeleeHitLocked", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [state, monster, melee]);

    [Fact]
    public void Real_owner_stats_damage_movement_and_native_buff_use_the_current_streak_once()
    {
        using var c = new Character(); var s = c.State;
        s.PlayerStats[Key("damage_+%")] = 10;
        var monster = new SpawnedMonster("target", new(100, 100, 0), 0, 0, 500);
        for (int i = 0; i < 20; i++) Contact(s, monster);
        s.Clock.Stop();
        var stats = Program.CharacterPlayerStats(s);
        Assert.Equal(20, stats[Key("current_rampage_stacks")]);
        Assert.Equal(12, stats[Key("damage_+%")]);
        Assert.Equal(12, Program.CharacterPlayerStats(s)[Key("damage_+%")]);
        var bytes = Program.RampagePresentation(s)!;
        ushort buff = s.RampageBuff;
        Assert.NotEqual((ushort)0, buff);
        var expected = InstanceEntities.AddBuff(0x1000, buff, 328, 0x1000,
            u68: BitConverter.SingleToUInt32Bits((float)s.Rampage.Current.RemainingSeconds(s.Clock.Elapsed.TotalSeconds)),
            stacks: 20);
        Assert.Contains(Convert.ToHexString(expected), Convert.ToHexString(bytes));
        Assert.Equal(Program.WalkSpeed * 1.01, s.Walk.Speed, 6);
        Assert.Null(Program.RampagePresentation(s));
        s.PlayerStats[Key(RampageSession.Source)] = 0;
        bytes = Program.RampagePresentation(s)!;
        Assert.Contains(Convert.ToHexString(InstanceEntities.RemoveBuff(0x1000, buff)), Convert.ToHexString(bytes));
        Assert.Equal(0, s.Rampage.Current.Stacks);
        Assert.Equal(10, Program.CharacterPlayerStats(s)[Key("damage_+%")]);
        Assert.Equal(Program.WalkSpeed, s.Walk.Speed);
    }

    [Fact]
    public void Friendly_dead_ranged_and_paused_requests_cannot_grant_or_erase_an_existing_streak()
    {
        using var c = new Character(); var s = c.State;
        var target = new SpawnedMonster("target", new(100, 100, 0), 0, 0, 500);
        Contact(s, target); Assert.Equal(1, s.Rampage.Current.Stacks);
        Contact(s, target, melee: false);
        var friendly = new SpawnedMonster("friendly", new(100, 100, 0), 0, 0, 500) { Hostile = false };
        Contact(s, friendly);
        target.Dead = true; Contact(s, target);
        target.Dead = false; s.Clock.Stop(); Contact(s, target);
        Assert.Equal(1, s.Rampage.Current.Stacks);
        s.Clock.Start(); s.PlayerLife = 0;
        Assert.Equal(0, Program.CharacterPlayerStats(s)[Key("current_rampage_stacks")]);
        s.PlayerLife = 500;
        Assert.Equal(0, Program.CharacterPlayerStats(s)[Key("current_rampage_stacks")]);
    }

    [Fact]
    public void Lethal_life_mutation_commits_kill_credit_before_any_delayed_notification()
    {
        using var c = new Character(); var s = c.State;
        var method = typeof(Program).GetMethod("TakeOwnerDamageLocked", BindingFlags.NonPublic | BindingFlags.Static)!;
        var target = new SpawnedMonster("target", new(100, 100, 0), 0, 0, 10);
        var result = ((uint Life, bool Stage))method.Invoke(null, [s, target, 10])!;
        Assert.Equal(0u, result.Life);
        Assert.Equal(1, s.Rampage.Current.Stacks);
        target.Life = result.Life; target.Dead = true;
        method.Invoke(null, [s, target, 10]);
        Assert.Equal(1, s.Rampage.Current.Stacks);
        var friendly = new SpawnedMonster("friendly", new(100, 100, 0), 0, 0, 10) { Hostile = false };
        method.Invoke(null, [s, friendly, 10]);
        Assert.Equal(1, s.Rampage.Current.Stacks);
    }
}
