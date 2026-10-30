using System.Reflection;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

public class RampageStunLifecycleTests
{
    static object? Call(string name, params object?[] args) => typeof(Program)
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);

    static SpawnedMonster Prepare(ClientState conn)
    {
        conn.PlayerStats[Program.Stats.Key(RampageSession.Source)!.Value] = 1;
        Program.CharacterPlayerStats(conn);
        var target = new SpawnedMonster("stun target", new(105, 100, 10), 60, 8, 100)
        { Attack = new(0x102, 1, 8, DamagePacket.Of(DamageType.Physical, 10, 10)) };
        conn.Monsters[42] = target;
        return target;
    }

    static Program.RampageStunToken Accept(ClientState conn, SpawnedMonster target, double at) =>
        Assert.IsType<Program.RampageStunToken>(Call("RampageStatusLocked", conn, 42u, target,
            Program.RampageData.Tiers.Single(t => t.Threshold == 15).Skill.AtLevel(1)!, at));

    static Task Present(ProjectileRuntimeFixture wire, Program.RampageStunToken token) =>
        (Task)Call("SendRampageStun", "stun-lifecycle", wire.Stream, wire.Ciphers, wire.Conn, token)!;

    [Theory]
    [InlineData("expiry-new-action")]
    [InlineData("new-action")]
    [InlineData("new-stun")]
    [InlineData("death")]
    [InlineData("replacement")]
    [InlineData("boss-stage")]
    [InlineData("source-removal")]
    [InlineData("owned-epoch")]
    [InlineData("owner-death")]
    [InlineData("incoming-epoch")]
    [InlineData("projectile-epoch")]
    [InlineData("paused-death")]
    public async Task A_queued_stun_cannot_cancel_a_new_action_or_outlive_its_target_and_owner(string change)
    {
        using var game = new VaalSoulGatewayTests.Fixture();
        var conn = game.Conn;
        var target = Prepare(conn);
        await using var wire = new ProjectileRuntimeFixture(conn);
        wire.Advance(100);
        var token = Accept(conn, target, 100);
        int acceptedAction = target.ActionGeneration;
        await wire.HoldOutput();
        var pending = Present(wire, token);
        Assert.False(pending.IsCompleted);
        lock (conn.Monsters)
        {
            switch (change)
            {
                case "expiry-new-action":
                    wire.Advance(target.FlinchUntil + .1);
                    var plan = Program.PlanMonsterAi(conn, new MonsterAiCadence(), target.FlinchUntil + .1);
                    Assert.True(Assert.Single(plan.Swings).Action > acceptedAction);
                    break;
                case "new-action": target.ActionGeneration++; break;
                case "new-stun": Accept(conn, target, 100.1); break;
                case "paused-death": conn.Clock.Stop(); goto case "death";
                case "death": target.Life = 0; target.Dead = true; break;
                case "replacement": conn.Monsters[42] = ProjectileRuntimeFixture.Monster(105, 100); break;
                case "boss-stage": target.StageRunning = true; break;
                case "source-removal": conn.PlayerStats[Program.Stats.Key(RampageSession.Source)!.Value] = 0; break;
                case "owned-epoch": conn.RampageWorld.Disable(); conn.RampageWorld.Enable(); break;
                case "owner-death": conn.PlayerLife = 0; break;
                case "incoming-epoch": conn.IncomingHitEpoch++; break;
                case "projectile-epoch": conn.ProjectileEpoch++; break;
            }
        }
        wire.ReleaseOutput();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(await wire.ReadWire());
    }

    [Fact]
    public async Task Gate_time_publication_uses_only_remaining_recovery_without_extending_the_server_deadline()
    {
        using var game = new VaalSoulGatewayTests.Fixture();
        var target = Prepare(game.Conn);
        await using var wire = new ProjectileRuntimeFixture(game.Conn);
        wire.Advance(100);
        var token = Accept(game.Conn, target, 100);
        double until = target.FlinchUntil;
        await wire.HoldOutput();
        var pending = Present(wire, token);
        wire.Advance(100.5);
        wire.ReleaseOutput();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(InstanceProtocol.ActorAnimation(42, (float)(until - 100.5), PlayerHitRecovery.TakeHitAnimation),
            await wire.ReadWire());
        Assert.Equal(until, target.FlinchUntil);
    }

    [Fact]
    public async Task Pause_holds_the_same_stun_outside_the_send_gate_and_resume_sends_its_remaining_recovery()
    {
        using var game = new VaalSoulGatewayTests.Fixture();
        var conn = game.Conn;
        var target = Prepare(conn);
        await using var wire = new ProjectileRuntimeFixture(conn);
        wire.Advance(100);
        var token = Accept(conn, target, 100);
        double until = target.FlinchUntil;
        await wire.HoldOutput();
        var pending = Present(wire, token);
        conn.Clock.Stop();
        wire.ReleaseOutput();
        // The sentinel passes the same send gate while the stun is waiting on pause.
        Assert.Empty(await wire.ReadWire());
        Assert.False(pending.IsCompleted);
        Assert.Equal(until, target.FlinchUntil);
        conn.Clock.Start();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(InstanceProtocol.ActorAnimation(42, (float)(until - 100), PlayerHitRecovery.TakeHitAnimation),
            await wire.ReadWire());
    }

    static Task OwnedHit(ProjectileRuntimeFixture wire, SpawnedMonster target) =>
        (Task)Call("RampageOwnedHit", "stun-deferred", wire.Stream, wire.Ciphers, wire.Conn,
            wire.Conn.RampageWorld.Epoch, 42u, target, DamagePacket.Empty,
            Program.RampageData.Tiers.Single(t => t.Threshold == 15).Skill.AtLevel(1)!,
            "deferred stun", false, 0)!;

    static void ProjectileHit(ProjectileRuntimeFixture wire, double until)
    {
        var offense = new ProjectileOffense(DamagePacket.Empty, true, 0, 0, 150, "deferred stun")
        {
            RampageEpoch = wire.Conn.RampageWorld.Epoch,
            RampageSkill = Program.RampageData.Tiers.Single(t => t.Threshold == 15).Skill.AtLevel(1)!
        };
        wire.Release(wire.Shot(offense: offense, origin: new(100, 100), aim: new(200, 100)));
        wire.Advance(until);
    }

    static string[] StunPackets(ProjectileRuntimeFixture wire) =>
        Program.HistoryFor(wire.Ciphers).Lines().Where(l => l.Contains("=> 0x01d1 ")).ToArray();

    [Theory]
    [InlineData("direct", false)]
    [InlineData("direct", true)]
    [InlineData("projectile", false)]
    [InlineData("projectile", true)]
    public async Task Two_real_hits_queued_before_presentation_entry_publish_only_the_current_stun(
        string route, bool recycleOwner)
    {
        using var game = new VaalSoulGatewayTests.Fixture();
        var conn = game.Conn;
        var target = Prepare(conn);
        await using var wire = new ProjectileRuntimeFixture(conn);
        wire.Advance(100);
        await wire.HoldOutput();
        Task first = Task.CompletedTask, second = Task.CompletedTask;
        if (route == "direct") first = OwnedHit(wire, target);
        else ProjectileHit(wire, 100.5);
        Assert.Equal(1, target.StunSerial);
        Assert.Empty(StunPackets(wire));
        if (route == "direct") Assert.False(first.IsCompleted); // Still awaiting LifeUpdate, before presentation.
        if (recycleOwner) { conn.RampageWorld.Disable(); conn.RampageWorld.Enable(); }
        if (route == "direct") second = OwnedHit(wire, target);
        else ProjectileHit(wire, 101);
        Assert.Equal(2, target.StunSerial);
        Assert.Empty(StunPackets(wire));
        double until = target.FlinchUntil;
        wire.ReleaseOutput();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        await wire.Read(); // Drain real deferred projectile callbacks as well as direct output.
        float remaining = (float)(until - (route == "direct" ? 100 : 101));
        Assert.EndsWith(Convert.ToHexString(InstanceProtocol.ActorAnimation(42, remaining,
            PlayerHitRecovery.TakeHitAnimation)), Assert.Single(StunPackets(wire)));
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("projectile")]
    public async Task An_old_hit_cannot_adopt_a_reenabled_owner_before_its_presentation_enters(string route)
    {
        using var game = new VaalSoulGatewayTests.Fixture();
        var conn = game.Conn;
        var target = Prepare(conn);
        await using var wire = new ProjectileRuntimeFixture(conn);
        wire.Advance(100);
        await wire.HoldOutput();
        Task hit = Task.CompletedTask;
        if (route == "direct") hit = OwnedHit(wire, target);
        else ProjectileHit(wire, 100.5);
        Assert.Equal(1, target.StunSerial);
        Assert.Empty(StunPackets(wire));
        conn.RampageWorld.Disable(); conn.RampageWorld.Enable();
        wire.ReleaseOutput();
        await hit.WaitAsync(TimeSpan.FromSeconds(5));
        await wire.Read();
        Assert.Empty(StunPackets(wire));
    }

    [Theory]
    [InlineData("no-status")]
    [InlineData("dead")]
    [InlineData("immune")]
    public void Status_without_an_accepted_stun_returns_no_presentation_token(string reason)
    {
        using var game = new VaalSoulGatewayTests.Fixture();
        var target = reason == "immune"
            ? new SpawnedMonster("immune", new(105, 100, 0), 60, 8, 100)
            { Mods = new([], [], new(new Dictionary<string, int> { ["base_cannot_be_stunned"] = 1 })) }
            : Prepare(game.Conn);
        if (reason == "dead") { target.Life = 0; target.Dead = true; }
        var own = reason == "no-status" ? null
            : Program.RampageData.Tiers.Single(t => t.Threshold == 15).Skill.AtLevel(1)!;
        Assert.Null(Call("RampageStatusLocked", game.Conn, 42u, target, own, 100d));
        Assert.Equal(0, target.StunSerial);
        Assert.Equal(0, target.ActionGeneration);
    }
}
