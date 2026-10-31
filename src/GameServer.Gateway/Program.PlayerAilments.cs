using System.Net.Sockets;
using GameServer.Core.Protocol.Login;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.World;

partial class Program
{
    internal static void ApplyPlayerAilmentsLocked(ClientState conn, DamagePacket baseHit, DamagePacket taken,
        bool critical, bool blocked, double now, IReadOnlyDictionary<uint, int> stats, uint? source = null)
    {
        int Get(string name) => Stats.Key(name) is uint key ? stats.GetValueOrDefault(key) : 0;
        // Changing Seasons lets any hit chill (all_damage_taken_can_chill), so a physical one is not skipped.
        if (blocked || conn.Closed || conn.LeavingArea || conn.PlayerLife == 0 || InGracePeriod(conn)
            || !baseHit.Components.Any(c => DamageTypes.IsElemental(c.Type)) && Get("all_damage_taken_can_chill") == 0) return;
        double previousAction = conn.ElementalAilments.ActionFactor(now);
        // Ailment damage is a separate ordinary roll from the pre-hit range. Hit luck and the hit's critical
        // multiplier cannot alter its distribution; non-damaging ailments still use the actual mitigated hit.
        int fire = critical ? baseHit[DamageType.Fire].Roll(conn.IncomingHitRandom) : 0;
        var ailmentBase = DamagePacket.Of(DamageType.Fire, fire, fire);
        conn.ElementalAilments.Apply(ailmentBase, taken, critical, conn.PlayerMaxLife, now, Get, conn.IncomingHitRandom, source);
        PlayerAilmentsChangedLocked(conn, previousAction, now);
    }

    // A new freeze cancels the player's action, channel and walk; any change of action speed re-times the walk.
    internal static void PlayerAilmentsChangedLocked(ClientState conn, double previousAction, double now)
    {
        if (previousAction != 0 && conn.ElementalAilments.ActionFactor(now) == 0)
        {
            conn.FreezeGeneration++;
            Interlocked.Increment(ref conn.ActionCount);
            conn.ChannelSkill = 0;
            var at = conn.Walk.Position(now); conn.Walk.Teleport(at.X, at.Y, now);
        }
        if (previousAction != conn.ElementalAilments.ActionFactor(now)) conn.ApplyMovementSpeed();
    }

    internal static int AdvancePlayerAilmentsLocked(ClientState conn, double from, double until,
        IReadOnlyDictionary<uint, int> stats)
    {
        if (conn.Closed || conn.LeavingArea || conn.PlayerLife == 0) { conn.ElementalAilments.Clear(); return 0; }
        if (!conn.ElementalAilments.HasEffects) return 0;
        double previousAction = conn.ElementalAilments.ActionFactor(from);
        int Get(string name) => Stats.Key(name) is uint key ? stats.GetValueOrDefault(key) : 0;
        int damage = conn.ElementalAilments.Advance(from, until, Get, Resistance(conn, DamageType.Fire),
            MaxResistance(conn, DamageType.Fire), PlayerImmunities(conn).Contains(DamageType.Fire),
            source => DamageTakenFromBlindedPct(conn, source, stats));
        int lifeDamage = conn.EnergyShield.Damage(damage, false, until);
        conn.PlayerLife = (uint)Math.Max(0, (long)conn.PlayerLife - lifeDamage);
        if (conn.PlayerLife == 0)
        {
            conn.InvalidateIncomingHits();
            // Commit the mode outcome inside lethal damage, before disconnects or network writes can intervene.
            RecordHardcoreDeath(conn);
        }
        if (previousAction != conn.ElementalAilments.ActionFactor(until)) conn.ApplyMovementSpeed();
        return damage;
    }

    // The chilled, frozen and shocked buffs on the player, as on monsters (AilmentBuff, on the fixed top instances that
    // NextBuff never hands out); a kind whose magnitude changed is sent again. The player's ignited buff is not sent.
    static async Task SendPlayerAilmentBuffs(string remote, NetworkStream stream, ConnectionCiphers ciphers,
        (bool Ignite, int Chill, bool Freeze, int Shock) before, (bool Ignite, int Chill, bool Freeze, int Shock) after)
    {
        foreach (var (packet, log) in PlayerAilmentBuffs(before, after)) await Send(remote, stream, ciphers, packet, log);
    }

    internal static List<(byte[] Packet, string Log)> PlayerAilmentBuffs((bool Ignite, int Chill, bool Freeze, int Shock) before,
        (bool Ignite, int Chill, bool Freeze, int Shock) after)
    {
        var packets = new List<(byte[], string)>();
        foreach (var (kind, old, now) in new[] { (MonsterElementalAilments.Kind.Chill, before.Chill, after.Chill),
            (MonsterElementalAilments.Kind.Freeze, before.Freeze ? 100 : 0, after.Freeze ? 100 : 0),
            (MonsterElementalAilments.Kind.Shock, before.Shock, after.Shock) })
        {
            if (old == now) continue;
            ushort instance = MonsterElementalAilments.Instance(kind);
            if (old > 0) packets.Add((InstanceEntities.RemoveBuff(PlayerId, instance), $"0x01c2 player {kind} ends"));
            if (now > 0) packets.Add((AilmentBuff(PlayerId, instance, kind, now), $"0x01c1 player {kind} {now}%"));
        }
        return packets;
    }

    internal static bool PlayerActionBlocked(ClientState conn, double requestedAt, double now) =>
        conn.HitRecovery.BlocksInput(requestedAt, now) || conn.ElementalAilments.ActionFactor(now) == 0;

    internal static double PlayerActionFactor(ClientState conn) =>
        Math.Max(.01, conn.ElementalAilments.ActionFactor(conn.Clock.Elapsed.TotalSeconds));
}
