using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;

partial class Program
{
    // Reuse the ordinary planner's cadence, action generations and repath budget for owned weapon targets.
    static bool PlanRampageWeaponTarget(ClientState conn, MonsterAiCadence cadence, MonsterAiPlan plan,
        uint id, SpawnedMonster monster, double now, (int X, int Y) from, double playerDistance)
    {
        var owned = monster.Hostile ? conn.Monsters.Where(p => p.Value.OwnerId == PlayerId && !p.Value.Dead
            && p.Value.Life > 0 && conn.RampageWorld.Actors.TryGetValue(p.Key, out var actor) && actor.Weapon is not null)
            .OrderBy(p => Dist(from, p.Value.Walk.Position(now))).FirstOrDefault() : default;
        if (owned.Value is null) return false;
        var target = owned.Value.Walk.Position(now);
        double distance = Dist(from, target);
        if (distance >= playerDistance) return false;
        if (monster.Attack is { } attack && distance <= attack.Range)
        {
            monster.BusyUntil = now + attack.Seconds;
            monster.LastTarget = null;
            monster.Walk.Order(from.X, from.Y, now);
            monster.SwingGeneration = ++monster.ActionGeneration;
            cadence.BeginSwing(monster, monster.ActionGeneration);
            plan.WeaponSwings.Add((id, monster, monster.ActionGeneration, owned.Key, owned.Value));
        }
        else if (cadence.TryChase(monster, target, now, retryAfterArrival: now >= monster.Walk.ArrivesAt))
        {
            monster.LastTarget = target;
            monster.SwingGeneration = null;
            plan.Moves.Add((id, monster, ++monster.ActionGeneration,
                monster.Walk.Order(target.X, target.Y, now), target, "chases Rampage weapon"));
        }
        return true;
    }

    static async Task RunPlannedRampageWeaponSwing(string remote, NetworkStream stream, ConnectionCiphers ciphers,
        ClientState conn, MonsterAiCadence cadence, uint id, SpawnedMonster attacker, int action,
        uint targetId, SpawnedMonster weapon)
    {
        try { await MonsterSwingAtRampageWeapon(remote, stream, ciphers, conn, id, attacker, action, targetId, weapon); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
        finally { lock (conn.Monsters) cadence.CompleteSwing(attacker, action); }
    }

    static async Task MonsterSwingAtRampageWeapon(string remote, NetworkStream stream, ConnectionCiphers ciphers,
        ClientState conn, uint id, SpawnedMonster attacker, int action, uint targetId, SpawnedMonster weapon)
    {
        if (attacker.Attack is not { } attack) return;
        bool TargetCurrent() => !weapon.Dead && weapon.Life > 0 && weapon.OwnerId == PlayerId
            && ReferenceEquals(conn.Monsters.GetValueOrDefault(targetId), weapon);
        double startedAt = 0;
        if (!await SendMonsterAction(remote, stream, ciphers, conn, id, attacker, action, () =>
        {
            if (!TargetCurrent()) return null;
            startedAt = conn.Clock.Elapsed.TotalSeconds;
            var from = attacker.Walk.Position(startedAt); var to = weapon.Walk.Position(startedAt);
            attacker.BusyUntil = startedAt + attack.Seconds;
            return InstanceProtocol.ActionStarted(id, from.X, from.Y, to.X, to.Y,
                attack.SkillId, MonsterActionSequence, 0x0408, targetObjectId: targetId);
        }, "Monster attacks Rampage weapon")) return;
        if (!await WaitMonsterActionTime(conn, id, attacker, action, startedAt + attack.Seconds / 2)) return;
        await SendMonsterAction(remote, stream, ciphers, conn, id, attacker, action, () =>
        {
            double now = Math.Max(ProjectileWorldTime(conn), conn.Clock.Elapsed.TotalSeconds);
            if (!TargetCurrent() || Dist(attacker.Walk.Position(now), weapon.Walk.Position(now)) > attack.Range + 2)
                return null;
            int accuracy = attacker.Mods.Effects.Accuracy(Monsters.BaseAccuracy(attacker.Level) ?? 0);
            if (attacker.RampageBlindUntil > now) accuracy = accuracy * 4 / 5;
            if (!RampageAttackHits(weapon, accuracy, now, Random.Shared)) return null;
            var packet = MonsterModHit(attacker, attack.Damage.Roll(Random.Shared), weapon.Life,
                weapon.MaxLife, Random.Shared, out bool critical);
            var (damage, _) = MonsterDamage(weapon, packet, critical, true, default);
            weapon.Life = (uint)Math.Max(0, (long)weapon.Life - damage);
            if (weapon.Life == 0) weapon.Dead = true;
            return InstanceProtocol.LifeUpdate(targetId, weapon.Life);
        }, "Monster hit on Rampage weapon");
    }
}
