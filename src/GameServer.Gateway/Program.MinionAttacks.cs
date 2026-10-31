using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using DamageModel = GameServer.Core.World.DamageModel;

/// <summary>A fighting minion's target this AI step: the hostile monster and where it stands, whether it is in reach, and
/// the cell the minion walks to so it is.</summary>
readonly record struct MinionTarget(uint Id, SpawnedMonster Monster, (int X, int Y) At, bool InReach, (int X, int Y) Approach);

partial class Program
{
    // A fighting minion reaches a monster's edge: its attack's distance (MaximumAttackDistance, as a monster's reach of the
    // player) plus the target's body.
    static double MinionReach(MonsterAttack a, SpawnedMonster target) => a.Range + Math.Max(0, LivingCollision.Size(target));

    /// <summary>
    /// The monster a fighting minion goes for this AI step: the living hostile one nearest it within its aggro range, of it
    /// and of the player (it does not leave him for a distant pack), not lurking unrisen. Null when there is none or the
    /// player is dead; it then follows him. Under the Monsters lock.
    /// </summary>
    internal static MinionTarget? MinionTargetLocked(ClientState conn, uint id, SpawnedMonster m, (int X, int Y) at,
        (int X, int Y) player, double now)
    {
        if (m.Attack is not { } a || conn.PlayerLife == 0) return null;
        MinionTarget? best = null;
        double nearest = double.MaxValue;
        foreach (var (targetId, t) in conn.Monsters)
        {
            if (targetId == id || !t.Hostile || t.Dead || t.Life == 0 || double.IsPositiveInfinity(t.BusyUntil)) continue;
            var to = t.Walk.Position(now);
            double d = Dist(to, at);
            if (d >= nearest || d > m.AggroRange || Dist(to, player) > m.AggroRange) continue;
            nearest = d;
            double reach = MinionReach(a, t), f = d > 0 ? Math.Max(0, reach - 1) / d : 0;
            best = new(targetId, t, to, d <= reach,
                ((int)Math.Round(to.X + (at.X - to.X) * f), (int)Math.Round(to.Y + (at.Y - to.Y) * f)));
        }
        return best;
    }

    /// <summary>
    /// A fighting minion's hit at its swing's contact, under the Monsters lock: both still there and the target in reach,
    /// the minion's accuracy (DefaultMonsterStats at its level) against the target's evasion, the target's block
    /// (Stalwart), then the attack's damage (the minion's own, with the player's minion modifiers it was summoned with)
    /// mitigated by the target as any hit. It is the minion's hit, not the player's (no hit-recently). Null when it did not
    /// land.
    /// </summary>
    internal static (MonsterHit Hit, int Damage)? MinionHitLocked(ClientState conn, uint id, SpawnedMonster m, MinionTarget target,
        double now, Random random)
    {
        var t = target.Monster;
        if (m.Attack is not { } a || m.Dead || !conn.Minions.Fights(id) || !conn.Monsters.TryGetValue(id, out var self)
            || !ReferenceEquals(self, m) || t.Dead || t.Life == 0 || !t.Hostile
            || !conn.Monsters.TryGetValue(target.Id, out var current) || !ReferenceEquals(current, t)) return null;
        var at = m.Walk.Position(now);
        if (Dist(at, t.Walk.Position(now)) > MinionReach(a, t) + 2) return null;
        int accuracy = m.Mods.Effects.Accuracy(Monsters.BaseAccuracy(m.Level) ?? 0);
        int evasion = UnderBlind(conn, target.Id, t.Mods.Effects.Evasion(Monsters.BaseEvasion(t.Level) ?? 0));
        if (!DamageModel.Hits(DamageModel.AccuracyHitChance(evasion, accuracy), random.Next(100)) || MonsterBlocks(t, random, spell: false))
            return null;
        int damage = MonsterTakesRolled(t, a.Damage.Roll(random), spell: false);
        return ApplyMonsterHitLocked(conn, t, damage, allyAt: at) is { } hit ? (hit, damage) : null;
    }

    // A fighting minion attacks its target: its skill aimed at it (0x1ab with the target's id, as a monster's swing at the
    // player), and at the contact point (half the attack time) the hit lands (MinionHitLocked): 0x1af from the minion, then
    // the target's hit and death as for the player's hits. Its kills give the player their experience, items and quest
    // progress, but are not his for his on-kill effects.
    static async Task MinionSwing(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, uint id,
        SpawnedMonster m, (int X, int Y) from, MinionTarget target)
    {
        var a = m.Attack!;
        bool started = false;
        await Send(remote, stream, ciphers,
            InstanceProtocol.ActionStarted(id, from.X, from.Y, target.At.X, target.At.Y, a.SkillId, 0xffff, 0x0408, targetObjectId: target.Id),
            $"0x01ab minion 0x{id:x} ({m.Label}) attacks monster 0x{target.Id:x}, attack time {a.Seconds:0.00} s", still: () =>
            {
                lock (conn.Monsters)
                    return started = !conn.Closed && !conn.LeavingArea && conn.Minions.Fights(id) && !m.Dead
                        && !target.Monster.Dead && target.Monster.Life > 0;
            });
        if (!started) return;
        await Task.Delay(TimeSpan.FromSeconds(a.Seconds / 2));
        (MonsterHit Hit, int Damage)? landed;
        (int X, int Y) at, to;
        while (true)
        {
            await WhilePaused(conn);
            using (ProjectileMutation(conn))
            {
                if (conn.Closed || conn.LeavingArea) return;
                if (conn.Paused) continue;
                double now = ProjectileWorldTime(conn);
                (at, to) = (m.Walk.Position(now), target.Monster.Walk.Position(now));
                landed = MinionHitLocked(conn, id, m, target, now, Random.Shared);
                if (landed is { Hit.StageStarts: true } staged)
                {
                    CommitBossStage(remote, stream, ciphers, conn, target.Id, target.Monster, now);
                    landed = staged with { Hit = staged.Hit with { StageStarts = false } };
                }
                break;
            }
        }
        if (landed is not { } hit)
        {
            Console.WriteLine($"[gateway] {remote} minion 0x{id:x} ({m.Label}) missed monster 0x{target.Id:x}");
            return;
        }
        await Send(remote, stream, ciphers,
            InstanceProtocol.ActionHit(id, 0xffff, target.Id, flags: 0x4601, kind: 0x02, u34: 450, angle: HitAngle(to.X - at.X, to.Y - at.Y)),
            $"0x01af minion 0x{id:x} hit on monster 0x{target.Id:x}");
        var deaths = new List<(uint Id, SpawnedMonster M)>();
        await NotifyMonsterHit(remote, stream, ciphers, conn, target.Id, target.Monster, hit.Hit, hit.Damage, $"minion 0x{id:x} ({m.Label})",
            deaths: deaths);
        foreach (var (dead, monster) in deaths) await MonsterKilled(remote, stream, ciphers, conn, dead, monster, byMinion: true);
    }
}
