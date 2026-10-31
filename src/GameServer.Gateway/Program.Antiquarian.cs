using System.Collections.Concurrent;
using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    internal static bool PlayerAlwaysHits(ClientState conn) => Stats.Key("global_always_hit") is uint key
        && CharacterPlayerStats(conn).GetValueOrDefault(key) != 0;

    internal static Dictionary<uint, int> WithAntiquarian(ClientState conn, Dictionary<uint, int> sources)
    {
        lock (conn.Monsters)
        {
            int Get(string name) => Stats.Key(name) is uint key
                ? conn.PlayerStats.TryGetValue(key, out int set) ? set : sources.GetValueOrDefault(key) : 0;
            double now = conn.Clock.Elapsed.TotalSeconds;
            conn.Antiquarian.Observe(now, Get(AntiquarianEffects.AdrenalineSource) > 0,
                Get(AntiquarianEffects.StunRecoverySource) > 0, conn.PlayerLife > 0 && !conn.RageRetired);
            if (conn.Antiquarian.Adrenaline(now))
                foreach (var (key, value) in AntiquarianEffects.AdrenalineStats)
                    sources[key] = (int)Math.Clamp((long)sources.GetValueOrDefault(key) + value, int.MinValue, int.MaxValue);
            return sources;
        }
    }

    internal static void EnsureAntiquarianPools(ClientState conn)
    {
        lock (conn.Monsters)
        {
            bool active = conn.PlayerLife > 0 && !conn.RageRetired
                && conn.Antiquarian.Adrenaline(conn.Clock.Elapsed.TotalSeconds);
            if (conn.AppliedAdrenaline != active)
            {
                conn.Pools = PoolsFor(conn, conn.Pools.Level);
                conn.ApplyMovementSpeed();
            }
        }
    }

    // Called under the combat lock after block/mitigation. Ward absorbs once, before ES, and never absorbs DoT.
    internal static int DamagePlayerHit(ClientState conn, int nonChaos, int chaos, double now,
        IReadOnlyDictionary<uint, int> stats)
    {
        int Get(string name) => Stats.Key(name) is uint key ? stats.GetValueOrDefault(key) : 0;
        int total = (int)Math.Min(int.MaxValue, (long)nonChaos + chaos);
        conn.Ward.Advance(now);
        bool wasBroken = conn.Ward.Broken;
        int remaining = conn.Ward.Damage(total, now, Get);
        int absorbed = total - remaining;
        // Ordered absorption keeps chaos bypassing ES. The supported shared Ward bypass is applied per component.
        int nonChaosEligible = nonChaos - (int)((long)nonChaos * Math.Clamp(Get("base_damage_bypass_ward_%"), 0, 100) / 100);
        int nonChaosAbsorbed = Math.Min(nonChaosEligible, absorbed);
        int lifeDamage = conn.EnergyShield.Damage(nonChaos - nonChaosAbsorbed, false, now)
            + conn.EnergyShield.Damage(Math.Max(0, chaos - (absorbed - nonChaosAbsorbed)), true, now);
        if (!wasBroken && conn.Ward.Broken
            && conn.Antiquarian.GrantAdrenaline(now, Get(AntiquarianEffects.AdrenalineSource)))
        {
            conn.Pools = PoolsFor(conn, conn.Pools.Level);
            conn.ApplyMovementSpeed();
        }
        return lifeDamage;
    }

    internal static void ClearAntiquarianOnDeath(ClientState conn)
    {
        if (conn.PlayerLife != 0) return;
        conn.Antiquarian.Clear(); conn.EnduringCryRecovery.Clear(); conn.Ward.SetMaximum(0);
    }

    internal static byte[]? AntiquarianPresentation(ClientState conn)
    {
        lock (conn.Monsters)
        {
            if (conn.Closed || conn.LeavingArea || conn.RageRetired) return null;
            EnsureAntiquarianPools(conn);
            conn.Ward.Advance(conn.Clock.Elapsed.TotalSeconds);
            var ward = (conn.Ward.Maximum, conn.Ward.Broken);
            double adrenaline = conn.PlayerLife > 0 && conn.Antiquarian.Adrenaline(conn.Clock.Elapsed.TotalSeconds)
                ? conn.Antiquarian.AdrenalineUntil : double.NegativeInfinity;
            double recovery = conn.Antiquarian.RecoveryUntil(conn.Clock.Elapsed.TotalSeconds);
            double cry = conn.EnduringCryRecovery.Rate(conn.Clock.Elapsed.TotalSeconds) > 0
                ? conn.EnduringCryRecovery.Until : double.NegativeInfinity;
            if (conn.SentWard == ward && conn.SentAdrenalineUntil == adrenaline
                && conn.SentStunRecoveryUntil == recovery && conn.SentEnduringCryUntil == cry)
                return null;
            var bytes = new List<byte>();
            if (conn.WardBuff != 0) bytes.AddRange(InstanceEntities.RemoveBuff(PlayerId, conn.WardBuff));
            if (conn.AdrenalineBuff != 0) bytes.AddRange(InstanceEntities.RemoveBuff(PlayerId, conn.AdrenalineBuff));
            if (conn.EnduringCryBuff != 0) bytes.AddRange(InstanceEntities.RemoveBuff(PlayerId, conn.EnduringCryBuff));
            conn.WardBuff = conn.AdrenalineBuff = 0;
            conn.EnduringCryBuff = 0;
            if (ward.Maximum > 0 && ward.Broken)
                bytes.AddRange(InstanceEntities.AddBuff(PlayerId, conn.WardBuff = conn.NextBuff++,
                    AntiquarianEffects.WardBrokenDefinition, PlayerId));
            if (double.IsFinite(adrenaline))
                bytes.AddRange(InstanceEntities.AddBuff(PlayerId, conn.AdrenalineBuff = conn.NextBuff++,
                    AntiquarianEffects.AdrenalineDefinition, PlayerId,
                    u68: BitConverter.SingleToUInt32Bits((float)Math.Max(0, adrenaline - conn.Clock.Elapsed.TotalSeconds))));
            if (double.IsFinite(cry))
                bytes.AddRange(InstanceEntities.AddBuff(PlayerId, conn.EnduringCryBuff = conn.NextBuff++, 1541, PlayerId,
                    u68: BitConverter.SingleToUInt32Bits((float)Math.Max(0, cry - conn.Clock.Elapsed.TotalSeconds))));
            bytes.AddRange(PlayerStatsUpdateAtGate(conn, PlayerStatsWithCleared(conn)));
            // The native client predicts regeneration between corrections. Removing the rate alone leaves
            // its predicted overshoot in place when the server has no further recovery tick to publish.
            bytes.AddRange(InstanceProtocol.LifeUpdate(PlayerId, conn.PlayerLife));
            bytes.AddRange(InstanceEntities.PlayerSkillsChange(PlayerId, PlayerSkills(conn), []));
            conn.SentWard = ward; conn.SentAdrenalineUntil = adrenaline;
            conn.SentStunRecoveryUntil = recovery;
            conn.SentEnduringCryUntil = cry;
            Console.WriteLine($"[gateway] Antiquarian Ward {conn.Ward.Current}/{conn.Ward.Maximum}, "
                + $"broken={conn.Ward.Broken}, Adrenaline={double.IsFinite(adrenaline)}");
            return [.. bytes];
        }
    }

    internal static Task SendAntiquarian(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn) =>
        Send(remote, stream, ciphers, [], null, atGate: () => AntiquarianPresentation(conn));

    internal sealed record AntiquarianTransfer(string Character, DateTime At, AntiquarianCombatState.Carried Buffs,
        bool Broken, double WardSeconds, TimedLifeRecovery.Carried Cry);
    internal static readonly ConcurrentDictionary<uint, AntiquarianTransfer> CarriedAntiquarian = new();

    internal static void CarryAntiquarian(ClientState conn, uint ticket)
    {
        lock (conn.Monsters)
        {
            if (conn.Closed || conn.RageRetired) return;
            if (conn.PlayerLife == 0) ClearAntiquarianOnDeath(conn);
            double now = conn.Clock.Elapsed.TotalSeconds;
            conn.Ward.Advance(now);
            foreach (var (old, entry) in CarriedAntiquarian)
                if (DateTime.UtcNow - entry.At > TimeSpan.FromMinutes(5)) CarriedAntiquarian.TryRemove(old, out _);
            CarriedAntiquarian.TryAdd(ticket, new(CharacterName(conn), DateTime.UtcNow,
                conn.Antiquarian.Carry(now), conn.Ward.Broken, Math.Max(0, conn.Ward.RestoresAt - now),
                conn.EnduringCryRecovery.Carry(now)));
        }
    }

    internal static void RestoreAntiquarian(ClientState conn)
    {
        lock (conn.Monsters)
        {
            if (!CarriedAntiquarian.TryGetValue(conn.Ticket, out var carry)
                || carry.Character != CharacterName(conn)
                || !CarriedAntiquarian.TryRemove(new KeyValuePair<uint, AntiquarianTransfer>(conn.Ticket, carry))) return;
            if (conn.PlayerLife == 0 || DateTime.UtcNow - carry.At > TimeSpan.FromMinutes(5)) return;
            double now = conn.Clock.Elapsed.TotalSeconds;
            conn.Antiquarian.Restore(carry.Buffs, now);
            conn.EnduringCryRecovery.Restore(carry.Cry, now);
            conn.Pools = PoolsFor(conn, conn.Pools.Level);
            conn.Ward.RestoreCarried(carry.Broken, carry.WardSeconds, now);
            conn.ApplyMovementSpeed();
        }
    }
}
