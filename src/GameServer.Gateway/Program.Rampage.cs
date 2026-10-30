using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    // Smoke and Rampage apply the same Blind; overlapping sources do not multiply its penalty twice.
    internal static int RampageBlindedValue(SpawnedMonster target, double now, int value, int groundValue) =>
        target.RampageBlindUntil > now ? Math.Min(groundValue, FlaskGrounds.Blinded(value)) : groundValue;

    static bool RampageActive(ClientState conn) =>
        !conn.Closed && !conn.LeavingArea && !conn.RageRetired && !conn.Paused;

    static bool RampageSource(ClientState conn)
    {
        var sources = CharacterStatSources(conn).Stats;
        return Stats.Key(RampageSession.Source) is uint key
            && conn.PlayerStats.GetValueOrDefault(key, sources.GetValueOrDefault(key)) != 0;
    }

    static void RampageOnMeleeHitLocked(ClientState conn, SpawnedMonster target, bool melee)
    {
        if (conn.Paused || target.Dead || target.Life == 0) return;
        CharacterPlayerStats(conn);
        int movement = conn.Rampage.Current.MovementPercent;
        conn.Rampage.MeleeHit(ProjectileWorldTime(conn), RampageSource(conn), conn.PlayerLife > 0,
            RampageActive(conn), melee, target.Hostile);
        if (movement != conn.Rampage.Current.MovementPercent) conn.ApplyMovementSpeedAt(ProjectileWorldTime(conn));
    }

    static void RampageOnKillLocked(ClientState conn, SpawnedMonster target)
    {
        if (conn.Paused) return;
        CharacterPlayerStats(conn);
        int movement = conn.Rampage.Current.MovementPercent;
        conn.Rampage.Kill(ProjectileWorldTime(conn), target, RampageSource(conn), conn.PlayerLife > 0,
            RampageActive(conn), target.Hostile);
        if (movement != conn.Rampage.Current.MovementPercent) conn.ApplyMovementSpeedAt(ProjectileWorldTime(conn));
    }

    static void WithRampage(ClientState conn, Dictionary<uint, int> stats)
    {
        lock (conn.Monsters)
        {
            bool enabled = Stats.Key(RampageSession.Source) is uint source && stats.GetValueOrDefault(source) != 0;
            // Pausing holds the game clock; it does not remove a streak.
            var before = conn.Rampage.Current;
            double now = conn.LeavingArea && conn.RampageTransferAt is double frozen ? frozen : ProjectileWorldTime(conn);
            var value = conn.Rampage.Observe(now, enabled, conn.PlayerLife > 0,
                !conn.Closed && !conn.RageRetired);
            if (before.MovementPercent != value.MovementPercent)
                conn.ApplyMovementSpeedAt(enabled && conn.PlayerLife > 0 && before.ExpiresAt is double end && now >= end ? end : now);
            if (enabled && conn.PlayerLife > 0 && !conn.Closed && !conn.LeavingArea && !conn.RageRetired)
                conn.RampageWorld.Enable();
            else conn.RampageWorld.Disable();
            void Add(string name, int amount)
            {
                if (amount != 0 && Stats.Key(name) is uint key)
                    stats[key] = (int)Math.Clamp((long)stats.GetValueOrDefault(key) + amount, int.MinValue, int.MaxValue);
            }
            Add("damage_+%", value.DamagePercent);
            Add("movement_velocity_+%", value.MovementPercent);
            if (Stats.Key("current_rampage_stacks") is uint current) stats[current] = value.Stacks;
            if (Stats.Key("max_rampage_stacks") is uint maximum) stats[maximum] = RampageState.MaximumStacks;
        }
    }

    internal static int RampageMovement(ClientState conn)
    {
        lock (conn.Monsters) return conn.Rampage.Current.MovementPercent;
    }

    internal static byte[]? RampagePresentation(ClientState conn)
    {
        lock (conn.Monsters)
        {
            if (conn.Closed || conn.LeavingArea || conn.RageRetired) return null;
            CharacterPlayerStats(conn);
            var value = conn.Rampage.Current;
            if (value == conn.SentRampage) return null;
            var bytes = new List<byte>();
            if (conn.RampageBuff != 0) bytes.AddRange(InstanceEntities.RemoveBuff(PlayerId, conn.RampageBuff));
            conn.RampageBuff = 0;
            if (value.Active)
            {
                conn.RampageBuff = conn.NextBuff++;
                bytes.AddRange(InstanceEntities.AddBuff(PlayerId, conn.RampageBuff, RampageCatalog.BuffDefinitionRow,
                    PlayerId, u68: BitConverter.SingleToUInt32Bits((float)value.RemainingSeconds(ProjectileWorldTime(conn))),
                    stacks: (ushort)value.Stacks));
            }
            conn.ApplyMovementSpeed();
            bytes.AddRange(PlayerStatsUpdateAtGate(conn, PlayerStatsWithCleared(conn)));
            bytes.AddRange(InstanceEntities.PlayerSkillsChange(PlayerId, PlayerSkills(conn), []));
            conn.SentRampage = value;
            return [.. bytes];
        }
    }

    static async Task RunRampage(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        try
        {
            while (!conn.Closed && !conn.RageRetired)
            {
                await AdvanceRampageEffects(remote, stream, ciphers, conn);
                await Send(remote, stream, ciphers, [], null, atGate: () => RampagePresentation(conn));
                await Task.Delay(32);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { }
        catch (Exception e)
        {
            Console.WriteLine($"[gateway] {remote} Rampage execution failed: {e}");
            conn.Closed = true; conn.RampageWorld.Disable();
        }
    }
}
