using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;

partial class Program
{
    /// <summary>BuffDefinitions row 0 <c>ignited</c> ("You're on fire", BuffVisuals 2: the burning flames).</summary>
    internal const ushort IgnitedBuffRow = 0;

    /// <summary>The wiki's Ignite: damaging ailments from critical strikes have +50% to Damage over Time Multiplier,
    /// additive with the others (Character.ot's critical_ailment_dot_multiplier_+; the player stats do not carry it).</summary>
    internal const int CriticalAilmentDotMultiplier = 50;

    // The increases the wiki's Ignite lists (Damage, Damage over Time, Elemental, Fire, Burning, Ignite Damage) and
    // ailment damage; hit-only ones (spell, area, attack, melee damage) never reach an ailment.
    static readonly string[] IgniteIncreases = ["damage_+%", "damage_over_time_+%", "elemental_damage_+%", "fire_damage_+%",
        "fire_damage_over_time_+%", "burn_damage_+%", "ignite_damage_+%", "base_ailment_damage_+%"];

    /// <summary>
    /// The ignite a granted skill's hit inflicts on <paramref name="m"/>, or null (wiki Ignite), for a hit without the
    /// panel's ignite rows (Abberath's Fury: <see cref="TriggerFootstepSkill"/>). The hit needs fire damage
    /// (<paramref name="baseFire"/>: its base and added fire damage after conversion, before any increase) and a critical
    /// strike (always ignites) or the chance (base_chance_to_ignite_%, chance_to_ignite_%, chance_to_freeze_shock_ignite_%);
    /// never_ignite stops it. It burns 90% of that fire per second (Ailments.Ignite) × the ignite increases ×
    /// <paramref name="more"/> × the damage over time multipliers (with +50% from a critical strike), for 4 s × increased
    /// ignite duration × the monster's base_self_ignite_duration_-%; the "faster" stats as in
    /// <see cref="MonsterIgnites.Settings.From"/>. <paramref name="attacker"/> gives the player's and the skill's stats.
    /// </summary>
    internal static DamageOverTime? IgniteFromHit(double baseFire, bool critical, Func<string, int> attacker, double more,
        SpawnedMonster m, Random random)
    {
        if (baseFire <= 0 || attacker("never_ignite") != 0 || attacker("never_ignite_chill_freeze_shock") != 0
            || attacker("never_damaging_ailments") != 0) return null;
        int chance = attacker("base_chance_to_ignite_%") + attacker("chance_to_ignite_%") + attacker("chance_to_freeze_shock_ignite_%");
        if (!critical && !RandomRolls.Chance(chance, 100, random)) return null;
        double multiplier = 1 + (attacker("dot_multiplier_+") + attacker("fire_dot_multiplier_+") + attacker("ignite_dot_multiplier_+")
            + (critical ? CriticalAilmentDotMultiplier + attacker("critical_ailment_dot_multiplier_+") : 0)) / 100.0;
        var ignite = GameServer.Core.World.Ailments.Ignite(DamagePacket.Of(DamageType.Fire, baseFire, baseFire),
            IgniteIncreases.Sum(attacker), more * multiplier);
        double faster = MonsterIgnites.Faster(attacker);
        double seconds = faster > 0 ? ignite.RemainingSeconds * Math.Max(0, 1 + attacker("ignite_duration_+%") / 100.0)
            * MonsterIgnites.SelfDuration(name => m.Mods.Effects[name]) / faster : 0;
        return ignite.DamagePerSecond > 0 && seconds > 0
            ? ignite with { DamagePerSecond = ignite.DamagePerSecond * faster, RemainingSeconds = seconds } : null;
    }

    /// <summary>Puts a worked-out ignite on a living monster (under the Monsters lock): the packets that show it
    /// (<see cref="ShowIgnitedLocked"/>), or null when nothing was added.</summary>
    internal static List<byte[]>? IgniteLocked(ClientState conn, uint id, SpawnedMonster m, DamageOverTime ignite, double now)
    {
        if (m.Dead || m.Life == 0 || !m.Ignites.Add(now, ignite.DamagePerSecond, ignite.RemainingSeconds)) return null;
        return ShowIgnitedLocked(conn, id, m, now);
    }

    // The ignited buff's timer runs to the monster's last ignite's end: every new ignite sends it again (the old instance
    // off first). Under the Monsters lock.
    static List<byte[]> ShowIgnitedLocked(ClientState conn, uint id, SpawnedMonster target, double now)
    {
        var packets = new List<byte[]>();
        if (target.Ignites.Buff is ushort old) packets.Add(InstanceEntities.RemoveBuff(id, old));
        ushort buff = conn.NextBuff++;
        target.Ignites.Buff = buff;
        packets.Add(IgnitedBuff(id, buff, target.Ignites.Until - now));
        return packets;
    }

    internal static byte[] IgnitedBuff(uint id, ushort instance, double seconds) =>
        InstanceEntities.AddBuff(id, instance, IgnitedBuffRow, PlayerId, u68: BitConverter.SingleToUInt32Bits((float)seconds));

    // Called under the combat lock at an accepted projectile hit that dealt fire damage and left the monster alive: the
    // shot's ignite (MonsterIgnites.TryApply, its duration less by the target's base_self_ignite_duration_-%), and the
    // monster's ignited buff.
    internal static void IgniteOnHitLocked(ProjectileSession session, uint id, SpawnedMonster target,
        MonsterIgnites.Settings settings, bool critical, double now)
    {
        var e = target.Mods.Effects;
        settings = settings with { Seconds = settings.Seconds * MonsterIgnites.SelfDuration(name => e[name]) };
        if (!target.Ignites.TryApply(settings, critical, now, e["avoid_ignite_%"],
                e["immune_to_ignite"] != 0 || e["cannot_be_ignited"] != 0, session.Conn.IgniteRandom)) return;
        var packets = ShowIgnitedLocked(session.Conn, id, target, now);
        for (int i = 0; i < packets.Count; i++)
            session.Packet(packets[i], i < packets.Count - 1 ? null
                : $"monster 0x{id:x} ({target.Label}) ignited{(critical ? " by a critical hit" : "")} for {target.Ignites.Until - now:0.##} s");
    }

    // One tick of the ignited monsters (RunPlayerTick): the strongest ignite's fire damage through the monster's
    // resistance and damage taken (MonsterDotTaken), the buff off when the last ignite ends, and kills, as Righteous
    // Fire's burn (BurnAround) commits them. They burn on while the player is dead (the kill then gives no experience,
    // MonsterKilled).
    internal static async Task BurnIgnited(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        var hits = new List<(uint Id, SpawnedMonster M, uint Life, int Damage)>();
        var ended = new List<(uint Id, ushort Buff)>();
        using (ProjectileMutation(conn))
        {
            if (conn.Closed || conn.LeavingArea) return;
            double now = ProjectileWorldTime(conn);
            foreach (var (id, m) in conn.Monsters)
            {
                if (m.Ignites.Idle) continue;
                int damage = 0;
                if (m.Dead || m.Life == 0) m.Ignites.Clear();
                else damage = m.Ignites.Advance(now, MonsterDotTaken(m, DamageType.Fire, now));
                if (!m.Ignites.Burning && m.Ignites.Buff is ushort gone) { ended.Add((id, gone)); m.Ignites.Buff = null; }
                if (damage == 0) continue;
                (m.Life, bool stageStarts) = TakeDamageLocked(m, damage);
                m.Aggro = m.Life > 0;
                if (m.Life == 0)
                {
                    m.Dead = true;
                    CollectVaalSoulAtDeathLocked(conn, m);
                    m.Ignites.Clear();
                    if (m.Ignites.Buff is ushort buff) { ended.Add((id, buff)); m.Ignites.Buff = null; }
                }
                hits.Add((id, m, m.Life, damage));
                if (stageStarts) CommitBossStage(remote, stream, ciphers, conn, id, m, now);
            }
        }
        foreach (var (id, buff) in ended)
            await Send(remote, stream, ciphers, InstanceEntities.RemoveBuff(id, buff), null);
        foreach (var (id, m, life, damage) in hits)
        {
            if (life == 0)
                await Send(remote, stream, ciphers, InstanceEntities.StatsUpdate(id, [(StatKeys.HideMiniLifeBar, 1)]), null);
            await Send(remote, stream, ciphers, InstanceProtocol.LifeUpdate(id, life),
                life == 0 ? $"0x01b1 monster 0x{id:x} ({m.Label}) burnt to death by ignite" : null);
            if (life == 0) await MonsterKilled(remote, stream, ciphers, conn, id, m);
        }
    }
}
