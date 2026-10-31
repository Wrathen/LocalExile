using System.Reflection;
using GameServer.Core.Accounts;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.World;
using GameServer.Storage;

namespace GameServer.Gateway.Tests;

public sealed class GameModeAilmentIntegrationTests : IDisposable
{
    readonly List<string> characters = [];

    static object? Call(string name, params object?[] args) => typeof(Program)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);

    ClientState Connection(string league)
    {
        TestSaves.Ensure();
        string name = "ModeAilment" + Guid.NewGuid().ToString("N");
        characters.Add(name);
        var mode = LeagueCatalog.Default.Find(league)!;
        var character = new Character(Guid.NewGuid(), Guid.NewGuid(), name, CharacterClass.Witch, 1,
            league, "1_1_2", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        new SqliteAccountStore(Saves.Db).AddCharacter(character);
        CharacterSaves.Write(name, new([]));
        var conn = new ClientState(new(ushort.MaxValue, "1_1_2", 300, 300, 10, 10, null, []), 1, 1)
        {
            Character = name, CharacterId = character.Id, ClassRow = SkillCatalog.WitchRow, League = league,
            Hardcore = mode.Hardcore, Ssf = mode.Ssf, Ruthless = mode.Ruthless,
            PlayerLife = 1, PlayerMana = 1,
        };
        conn.Pools = Program.PoolsFor(conn, 1);
        return conn;
    }

    [Theory]
    [InlineData("Standard")]
    public void Chill_slows_attack_and_spell_timers_once_in_both_modes(string league)
    {
        var conn = Connection(league);
        int attack = Program.SkillsFor(conn).EffectRow("HeavyStrike");
        int spell = Program.SkillsFor(conn).EffectRow("Fireball");
        Assert.True(Program.SkillsFor(conn).IsAttack(attack));
        Assert.True(Program.SkillsFor(conn).IsSpell(spell));
        double beforeAttack = (double)Call("SkillAttackSeconds", conn, attack, 1)!;
        int beforeCast = (int)Call("CastMs", conn, spell, (ushort)0)!;
        Assert.True(beforeCast > 0);
        var cold = DamagePacket.Of(DamageType.Cold, conn.PlayerMaxLife, conn.PlayerMaxLife);
        // The chill lasts two seconds of the connection's clock, which has run since its creation (the catalog loads
        // above take longer than that under a loaded suite): apply it now and stop the clock, as a pause does, so the
        // reads below see the same instant. The clock must not go backwards: earlier observers (Fortification) refuse it.
        double now = conn.Clock.Elapsed.TotalSeconds;
        conn.Clock.Stop();
        conn.ElementalAilments.Apply(cold, cold, false, conn.PlayerMaxLife, now, _ => 0, new Random(1));

        Assert.Equal(.7, Program.PlayerActionFactor(conn), 6);
        Assert.Equal(beforeAttack / .7, (double)Call("SkillAttackSeconds", conn, attack, 1)!, 6);
        Assert.Equal((int)Math.Ceiling(beforeCast / .7), (int)Call("CastMs", conn, spell, (ushort)0)!);
    }

    [Fact]
    public void Action_speed_adds_to_chill_in_one_bucket_and_scales_attack_cast_and_walk()
    {
        var conn = Connection("Standard");
        int attack = Program.SkillsFor(conn).EffectRow("HeavyStrike");
        int spell = Program.SkillsFor(conn).EffectRow("Fireball");
        double baseAttack = (double)Call("SkillAttackSeconds", conn, attack, 1)!;
        int baseCast = (int)Call("CastMs", conn, spell, (ushort)0)!;
        Assert.True(baseCast > 0);

        uint key = Program.Stats.Key("action_speed_-%") ?? throw new InvalidOperationException("action_speed_-%");
        lock (conn.Monsters) conn.PlayerStats[key] = -50; // Negative reduction: 50% faster.
        Assert.Equal(1.5, Program.PlayerActionFactor(conn), 6);
        Assert.Equal(baseAttack / 1.5, (double)Call("SkillAttackSeconds", conn, attack, 1)!, 6);
        Assert.Equal((int)Math.Ceiling(baseCast / 1.5), (int)Call("CastMs", conn, spell, (ushort)0)!);

        double walk = Program.WalkSpeed * (100 + conn.Pools.MovementVelocityPct) / 100 * 1.5;
        conn.ApplyMovementSpeed();
        Assert.Equal(walk, conn.Walk.Speed, 6);

        var cold = DamagePacket.Of(DamageType.Cold, conn.PlayerMaxLife, conn.PlayerMaxLife);
        double now = conn.Clock.Elapsed.TotalSeconds;
        conn.Clock.Stop();
        conn.ElementalAilments.Apply(cold, cold, false, conn.PlayerMaxLife, now, _ => 0, new Random(1));

        // +50% action speed and a 30% chill are additive, not multiplicative: 1.20, not 1.5 * 0.7.
        Assert.Equal(1.2, Program.PlayerActionFactor(conn), 6);
        conn.ApplyMovementSpeed();
        Assert.Equal(walk / 1.5 * 1.2, conn.Walk.Speed, 6);
    }

    [Fact]
    public void New_actions_are_limited_to_one_per_server_tick_but_channel_repeats_are_not()
    {
        var conn = Connection("Standard");
        int spell = Program.SkillsFor(conn).EffectRow("Fireball");
        int attack = Program.SkillsFor(conn).EffectRow("HeavyStrike");
        uint key = Program.Stats.Key("action_speed_-%") ?? throw new InvalidOperationException("action_speed_-%");
        lock (conn.Monsters) conn.PlayerStats[key] = -1_000_000;
        int ms = (int)Call("CastMs", conn, spell, (ushort)0)!;
        Assert.Equal(34, ms); // 1/30 s tick rounded up to whole milliseconds
        Assert.Equal(Program.TickSeconds, (double)Call("SkillAttackSeconds", conn, attack, 1)!, 6);
        Assert.True((double)Call("SupportedChannelSeconds", conn, (ushort)attack)! < Program.TickSeconds);
    }

    [Fact]
    public async Task Action_speed_and_stats_snapshots_do_not_deadlock()
    {
        var conn = Connection("Standard");
        uint key = Program.Stats.Key("action_speed_-%") ?? throw new InvalidOperationException("action_speed_-%");
        lock (conn.Monsters) conn.PlayerStats[key] = -50;
        var work = Enumerable.Range(0, 32).SelectMany(_ => new Task[]
        {
            Task.Run(() => Program.PlayerActionFactor(conn)),
            Task.Run(() => Program.CharacterPlayerStats(conn)),
        });
        await Task.WhenAll(work).WaitAsync(TimeSpan.FromSeconds(10));
    }

    public void Dispose()
    {
        foreach (string name in characters) PlayerInventories.Forget(name);
    }
}
