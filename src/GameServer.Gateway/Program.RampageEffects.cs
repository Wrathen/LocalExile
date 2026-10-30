using System.Net.Sockets;
using GameServer.Core.Projectiles;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;

partial class Program
{
    internal sealed record RampageStunToken(uint Id, SpawnedMonster Target, int Stun, int Action,
        int IncomingEpoch, int ProjectileEpoch, long OwnedEpoch, double Until);

    internal static readonly RampageCatalog RampageData = new();

    static bool RampageEffectCurrent(ClientState conn, long epoch) => conn.RampageWorld.Current(epoch)
        && !conn.Closed && !conn.LeavingArea && !conn.RageRetired && conn.PlayerLife > 0 && RampageSource(conn);

    static DamagePacket RampageSpellPacket(ClientState conn, int effect, int level)
    {
        var owner = CharacterStatSources(conn).Stats;
        int increased = Stats.Key("minion_damage_+%") is uint key ? owner.GetValueOrDefault(key) : 0;
        // The daemon is a separate caster. Rampager's spell-skills-deal-no-damage and owner Rage do not apply.
        return SkillsFor(conn).HitDamage(effect, level).Scale(Math.Max(0, 100L + increased) / 100.0);
    }

    static async Task<uint?> CreateRampageActor(string remote, NetworkStream stream, ConnectionCiphers ciphers,
        ClientState conn, RampageCatalog.VarietyPlan plan, int level, double lifetime,
        RampageCatalog.WeaponKind? weapon, (int X, int Y) at, long epoch)
    {
        if (Monsters.Find(plan.Row.ToString()) is not { Supported: true } variety) return null;
        uint id;
        lock (conn.Monsters) id = conn.NextObjectId++;
        byte actorLevel = (byte)Math.Clamp(level, 1, 100);
        uint life = (uint)Math.Max(1, Monsters.BaseLife(variety, actorLevel) ?? 1);
        int effect = plan.Skills[0].Row;
        var own = SkillsFor(conn).EffectStats(effect, level).ToDictionary(s => s.Item1, s => s.Item2);
        var skill = SkillsFor(conn).FindByRow(SkillCatalog.FirstSkillId, effect, level, own.Select(s => (s.Key, s.Value)).ToArray());
        if (skill is null) return null;
        var blob = new MonsterObject(at.X, at.Y, life, variety.ComponentsOrBase, StateMachine: variety.StateMachine,
            Team: 0, Skills: [skill], Stats: [(StatKeys.MaximumLife, (int)life)]) { LifeData = variety.LifeData };
        await Send(remote, stream, ciphers, [], $"Rampage owned {weapon?.ToString() ?? "daemon"} 0x{id:x}", atGate: () =>
        {
            lock (conn.Monsters)
            {
                if (!RampageEffectCurrent(conn, epoch)) return null;
                double now = ProjectileWorldTime(conn);
                conn.Monsters[id] = new(variety.Name, conn.NewMonsterWalk(id, at.X, at.Y, variety.MovementSpeed,
                    Math.Clamp(variety.ObjectSize - 1, -1, 127), false), variety.AggroRange, variety.AttackRange, life)
                { OwnerId = PlayerId, Hostile = false, Variety = variety, Level = actorLevel,
                    HitAnimations = Monsters.TakeHit(variety.Id) };
                conn.RampageWorld.Actors[id] = new(id, epoch, weapon, actorLevel, now + lifetime, now, effect);
                var bytes = new List<byte>();
                if (conn.MonsterTypes.Add((variety.Row, actorLevel)))
                    bytes.AddRange(InstanceEntities.MonsterTypes([((ushort)variety.Row, actorLevel)]));
                uint type = InstanceEntities.ObjectTypeHash(MonsterObject.TypePath(variety.Id, actorLevel));
                bytes.AddRange(InstanceEntities.CreateObject(id, type, blob.Blob()));
                bytes.AddRange(InstanceProtocol.EnterWakeZone(id));
                return bytes.ToArray();
            }
        });
        return id;
    }

    static async Task StartRampageTier(string remote, NetworkStream stream, ConnectionCiphers ciphers,
        ClientState conn, RampageCatalog.Tier tier)
    {
        long epoch; int level; (int X, int Y) origin;
        lock (conn.Monsters)
        {
            epoch = conn.RampageWorld.Epoch; level = conn.Pools.Level;
            origin = conn.Walk.Position(ProjectileWorldTime(conn));
            if (!RampageEffectCurrent(conn, epoch)) return;
        }
        var own = tier.Skill.AtLevel(level)!;
        double cast = tier.Skill.CastTimeMs / 1000.0 / Math.Max(.01, 1 + own.Get("base_cast_speed_+%") / 100.0);
        var steps = RampageExecution.Steps(tier, level);
        double end = cast + steps.Max(s => s.Delay) + .5;
        uint? actor = await CreateRampageActor(remote, stream, ciphers, conn, tier.Daemon, level, end, null, origin, epoch);
        if (actor is null) throw new InvalidOperationException($"Rampage tier {tier.Threshold} cannot create its actor");
        await Send(remote, stream, ciphers, InstanceProtocol.ActionStarted(actor.Value, origin.X, origin.Y,
            origin.X, origin.Y, SkillCatalog.FirstSkillId, 0xffff, 0x0408), $"Rampage tier {tier.Threshold}",
            still: () => RampageEffectCurrent(conn, epoch));
        lock (conn.Monsters)
            if (RampageEffectCurrent(conn, epoch))
                foreach (var step in steps)
                    conn.RampageWorld.Jobs.Add(new(ProjectileWorldTime(conn) + cast + step.Delay,
                        epoch, actor.Value, tier, step, origin, level));
    }

    static async Task AdvanceRampageEffects(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        int[] tiers; List<RampageWorld.Job> due; uint[] removals;
        lock (conn.Monsters)
        {
            if (conn.Paused) return;
            CharacterPlayerStats(conn);
            var world = conn.RampageWorld;
            double now = ProjectileWorldTime(conn);
            tiers = world.Enabled ? conn.Rampage.TakeTiers() : [];
            due = world.Jobs.Where(j => j.At <= now).OrderBy(j => j.At).ToList();
            foreach (var job in due) world.Jobs.Remove(job);
            foreach (var actor in world.Actors.Values.ToArray())
                if (actor.ExpiresAt <= now || !RampageEffectCurrent(conn, actor.Epoch)
                    || !conn.Monsters.TryGetValue(actor.Id, out var state) || state.Dead)
                { world.PendingRemoval.Add(actor.Id); world.Actors.Remove(actor.Id); }
            removals = world.PendingRemoval.ToArray(); world.PendingRemoval.Clear();
            foreach (uint id in removals)
                if (conn.Monsters.Remove(id, out var dead)) dead.Dead = true;
        }
        foreach (uint id in removals)
            await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(id), "Rampage owned actor ends");
        foreach (int threshold in tiers)
        {
            var tier = RampageData.Tiers.SingleOrDefault(t => t.Threshold == threshold)
                ?? throw new InvalidOperationException(RampageData.Error ?? "Missing Rampage tier");
            await StartRampageTier(remote, stream, ciphers, conn, tier);
        }
        foreach (var job in due)
        {
            if (!RampageEffectCurrent(conn, job.Epoch)) continue;
            if (job.Step.Kind == RampageExecution.StepKind.Weapons)
            {
                foreach (var summon in job.Tier.Summons)
                    for (int i = 0; i < summon.Count; i++)
                        await CreateRampageActor(remote, stream, ciphers, conn, summon.Weapon.Variety, job.Level,
                            summon.DurationMs / 1000.0, summon.Weapon.Kind, job.Origin, job.Epoch);
            }
            else if (job.Step.Kind == RampageExecution.StepKind.Projectiles)
                ReleaseRampageProjectiles(remote, stream, ciphers, conn, job);
            else await RampagePulse(remote, stream, ciphers, conn, job);
        }
        await AdvanceRampageWeapons(remote, stream, ciphers, conn);
        await ExpireRampageBlinds(remote, stream, ciphers, conn);
    }

    static async Task RampagePulse(string remote, NetworkStream stream, ConnectionCiphers ciphers,
        ClientState conn, RampageWorld.Job job)
    {
        var own = job.Tier.Skill.AtLevel(job.Level)!;
        var origins = new List<((int X, int Y) Position, DamagePacket Damage)>();
        var corpses = new List<uint>();
        lock (conn.Monsters)
        {
            if (!RampageEffectCurrent(conn, job.Epoch)) return;
            if (job.Step.Kind == RampageExecution.StepKind.Corpses)
            {
                foreach (var (corpseId, target) in conn.Monsters.Where(p => p.Value.Dead && p.Value.Hostile && p.Value.OwnerId is null)
                    .OrderBy(p => Dist(p.Value.Walk.Position(job.At), job.Origin)))
                    if (Dist(target.Walk.Position(job.At), job.Origin) <= 120
                        && conn.RampageWorld.ConsumedCorpses.Add(target))
                    {
                        double amount = target.MaxLife * own.Get("corpse_explosion_monster_life_%") / 100.0;
                        origins.Add((target.Walk.Position(job.At), DamagePacket.Of(DamageType.Fire, amount, amount)));
                        corpses.Add(corpseId);
                    }
            }
            else origins.Add((job.Origin, RampageSpellPacket(conn, job.Tier.Skill.Row, job.Level)));
        }
        foreach (uint id in corpses)
        {
            lock (conn.Monsters) conn.Monsters.Remove(id);
            await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(id), "Rampage consumes corpse");
        }
        foreach (var (origin, packet) in origins)
        {
            double radius = job.Step.Kind == RampageExecution.StepKind.Corpses ? RampageExecution.CorpseRadius
                : Math.Max(0, own.Get("active_skill_base_area_of_effect_radius") is > 0 and var r ? r : RampageExecution.NearbyRadius)
                    * Math.Sqrt(Math.Max(0, 100 + own.Get("active_skill_area_of_effect_radius_+%_final")) / 100.0)
                    * job.Step.RadiusPercent / 100;
            (uint Id, SpawnedMonster Monster)[] targets;
            lock (conn.Monsters) targets = conn.Monsters.Where(p => p.Value.Hostile && !p.Value.Dead
                && p.Value.OwnerId is null && Dist(p.Value.Walk.Position(ProjectileWorldTime(conn)), origin) <= radius)
                .Select(p => (p.Key, p.Value)).ToArray();
            foreach (var (id, target) in targets)
                await RampageOwnedHit(remote, stream, ciphers, conn, job.Epoch, id, target, packet, own,
                    job.Step.Kind == RampageExecution.StepKind.Corpses ? "Rampage corpse explosion" : "Rampage nova");
        }
    }

    static async Task<bool> RampageOwnedHit(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        long epoch, uint id, SpawnedMonster target, DamagePacket packet, RampageCatalog.SkillLevel? own, string label,
        bool attack = false, int accuracy = 0)
    {
        MonsterHit? hit; int damage; RampageStunToken? stun;
        using (ProjectileMutation(conn))
        {
            if (!RampageEffectCurrent(conn, epoch) || conn.Paused || target.Dead || !target.Hostile
                || target.OwnerId is not null || !ReferenceEquals(conn.Monsters.GetValueOrDefault(id), target)) return false;
            if (attack && !RampageAttackHits(target, accuracy, ProjectileWorldTime(conn), Random.Shared)) return false;
            if (MonsterBlocks(target, Random.Shared, !attack)) return false;
            bool critical = Random.Shared.Next(10000) < (attack ? 500 : own?.SpellCriticalChance ?? 0);
            (damage, _) = MonsterDamage(target, packet.Roll(Random.Shared), critical, attack, default);
            if (critical) damage = DamageModel.CriticalDamage(damage, CriticalMultiplierOn(target, 150));
            hit = ApplyMonsterHitLocked(conn, target, damage);
            if (hit is null) return false;
            if (hit.Value.StageStarts) CommitBossStage(remote, stream, ciphers, conn, id, target, ProjectileWorldTime(conn));
            stun = RampageStatusLocked(conn, id, target, own, ProjectileWorldTime(conn));
        }
        await Send(remote, stream, ciphers, InstanceProtocol.LifeUpdate(id, hit.Value.Life),
            $"{label} hit 0x{id:x} for {damage}, life {hit.Value.Life}/{target.MaxLife}");
        if (hit.Value.Life == 0)
        {
            await MonsterKilledBy(remote, stream, ciphers, conn, id, target, playerKill: false);
            if (own?.Get("explode_on_kill_%_fire_damage_to_deal") is int percent and > 0)
                await RampageKillExplosion(remote, stream, ciphers, conn, epoch, id, target, percent);
        }
        else if (stun is not null)
            await SendRampageStun(remote, stream, ciphers, conn, stun);
        return hit.Value.Life == 0;
    }

    static bool RampageAttackHits(SpawnedMonster target, int accuracy, double now, Random random)
    {
        int evasion = target.Mods.Effects.Evasion(Monsters.BaseEvasion(target.Level) ?? 0);
        if (target.RampageBlindUntil > now) evasion = evasion * 4 / 5;
        return DamageModel.Hits(DamageModel.AccuracyHitChance(evasion, accuracy), random.Next(100));
    }

    static RampageStunToken? RampageStatusLocked(ClientState conn, uint id, SpawnedMonster target,
        RampageCatalog.SkillLevel? own, double now)
    {
        if (own is null || target.Dead) return null;
        RampageStunToken? stun = null;
        if (own.Get("always_stun") != 0 && !target.Mods.Effects.CannotBeStunned)
        {
            target.StunSerial++;
            target.ActionGeneration++;
            target.SwingGeneration = null;
            target.FlinchUntil = Math.Max(target.FlinchUntil,
                now + .35 * (1 + own.Get("base_stun_duration_+%") / 100.0));
            target.BusyUntil = Math.Max(target.BusyUntil, target.FlinchUntil);
            var at = target.Walk.Position(now); target.Walk.Teleport(at.X, at.Y, now);
            target.CollisionPosition = at;
            target.LastTarget = null;
            // Capture the accepted mutation before any LifeUpdate await or deferred projectile output.
            stun = new(id, target, target.StunSerial, target.ActionGeneration, conn.IncomingHitEpoch,
                conn.ProjectileEpoch, conn.RampageWorld.Epoch, target.FlinchUntil);
        }
        if (own.Get("global_chance_to_blind_on_hit_%") > 0)
        {
            target.RampageBlindUntil = now + 4 * (1 + own.Get("blind_duration_+%") / 100.0);
            ushort buff = conn.RampageWorld.Blinds.TryGetValue(id, out var old) ? old.Buff : conn.NextBuff++;
            conn.RampageWorld.Blinds[id] = (target.RampageBlindUntil, buff);
        }
        return stun;
    }

    static async Task SendRampageStun(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        RampageStunToken token)
    {
        var target = token.Target;
        double Now() => Math.Max(ProjectileWorldTime(conn), conn.Clock.Elapsed.TotalSeconds);
        bool Current() => token.Stun > 0 && target.StunSerial == token.Stun && target.ActionGeneration == token.Action
            && conn.IncomingHitEpoch == token.IncomingEpoch && conn.ProjectileEpoch == token.ProjectileEpoch
            && target.FlinchUntil == token.Until && token.Until > Now() && !target.StageRunning
            && !target.Mods.Effects.CannotBeStunned && MonsterPresent(conn, token.Id, target)
            && RampageEffectCurrent(conn, token.OwnedEpoch);
        // Recovery was committed at the hit. Queued output may only present that same interval, never restart it.
        // Like ordinary flinches, pause waits outside the send gate; identity and generations are rechecked there.
        while (true)
        {
            lock (conn.Monsters) if (!Current()) return;
            bool completed = false;
            await Send(remote, stream, ciphers, [], null, atGate: () =>
            {
                lock (conn.Monsters)
                {
                    if (!Current()) { completed = true; return null; }
                    if (conn.Paused) return null;
                    completed = true;
                    float remaining = (float)(token.Until - Now());
                    return remaining > 0
                        ? InstanceProtocol.ActorAnimation(token.Id, remaining, PlayerHitRecovery.TakeHitAnimation) : null;
                }
            });
            if (completed) return;
            await Task.Delay(16);
        }
    }

    static async Task ExpireRampageBlinds(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        List<byte> bytes = [];
        lock (conn.Monsters)
        {
            double now = ProjectileWorldTime(conn);
            foreach (var (id, entry) in conn.RampageWorld.Blinds.ToArray())
                if (!conn.RampageWorld.Enabled || entry.Until <= now || !conn.Monsters.TryGetValue(id, out var target) || target.Dead)
                {
                    bytes.AddRange(InstanceEntities.RemoveBuff(id, entry.Buff));
                    if (conn.Monsters.TryGetValue(id, out var old)) old.RampageBlindUntil = 0;
                    conn.RampageWorld.Blinds.Remove(id);
                    conn.RampageWorld.SentBlinds.Remove(id);
                }
                else if (conn.RampageWorld.SentBlinds.GetValueOrDefault(id) != entry.Until)
                {
                    if (conn.RampageWorld.SentBlinds.ContainsKey(id)) bytes.AddRange(InstanceEntities.RemoveBuff(id, entry.Buff));
                    bytes.AddRange(InstanceEntities.AddBuff(id, entry.Buff, 179, PlayerId,
                        u68: BitConverter.SingleToUInt32Bits((float)(entry.Until - now))));
                    conn.RampageWorld.SentBlinds[id] = entry.Until;
                }
        }
        if (bytes.Count > 0) await Send(remote, stream, ciphers, bytes.ToArray(), "Rampage blind state");
    }

    static async Task RampageKillExplosion(string remote, NetworkStream stream, ConnectionCiphers ciphers,
        ClientState conn, long epoch, uint corpseId, SpawnedMonster corpse, int percent)
    {
        var pending = new Queue<(uint Id, SpawnedMonster Corpse)>();
        pending.Enqueue((corpseId, corpse));
        while (pending.TryDequeue(out var current))
        {
            (uint Id, SpawnedMonster Target)[] targets;
            lock (conn.Monsters)
            {
                if (!RampageEffectCurrent(conn, epoch)) return;
                if (!conn.RampageWorld.Exploded.Add(current.Id)) continue;
                var at = current.Corpse.Walk.Position(ProjectileWorldTime(conn));
                targets = conn.Monsters.Where(p => p.Value.Hostile && !p.Value.Dead && p.Value.OwnerId is null
                    && Dist(p.Value.Walk.Position(ProjectileWorldTime(conn)), at) <= RampageExecution.CorpseRadius)
                    .Select(p => (p.Key, p.Value)).ToArray();
            }
            double fire = current.Corpse.MaxLife * percent / 100.0;
            foreach (var (id, target) in targets)
                if (await RampageOwnedHit(remote, stream, ciphers, conn, epoch, id, target,
                    DamagePacket.Of(DamageType.Fire, fire, fire), null, "Rampage kill explosion"))
                    pending.Enqueue((id, target));
        }
    }
}
