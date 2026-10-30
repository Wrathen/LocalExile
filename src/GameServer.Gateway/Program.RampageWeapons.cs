using System.Net.Sockets;
using GameServer.Core.Projectiles;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;

partial class Program
{
    static void ReleaseRampageProjectiles(string remote, NetworkStream stream, ConnectionCiphers ciphers,
        ClientState conn, RampageWorld.Job job)
    {
        var own = job.Tier.Skill.AtLevel(job.Level)!;
        var metadata = SkillsFor(conn).ProjectileFor(job.Tier.Skill.Row);
        if (metadata is null) throw new InvalidOperationException($"No projectile metadata for {job.Tier.Skill.Id}");
        var profile = new ProjectileProfile(metadata.Value.Id)
        {
            Count = 12, CircularSpread = true, SpreadRadians = Math.PI * 2,
            Speed = ProjectileSpeed.CellsPerSecond(metadata.Value.Speed, _ => 0, _ => 0), Range = 120,
            Pierce = own.Get("always_pierce") != 0 ? int.MaxValue : 0,
            Chain = Math.Max(0, own.Get("number_of_chains")),
        };
        var session = ProjectilesFor(remote, stream, ciphers, conn);
        ProjectileActorKey emitter;
        lock (conn.Monsters)
        {
            if (!conn.Monsters.TryGetValue(job.Actor, out var daemon)) return;
            emitter = session.ActorKey(job.Actor, daemon);
        }
        var packet = RampageSpellPacket(conn, job.Tier.Skill.Row, job.Level);
        session.Schedule(ProjectileRuntime.Time(job.At), () => RampageEffectCurrent(conn, job.Epoch), () =>
            new ProjectileUse(profile, new(PlayerId, conn.ProjectileEpoch), emitter, 1,
                new(job.Origin.X, job.Origin.Y), new(job.Origin.X + 100, job.Origin.Y))
            { Payload = new ProjectileOffense(packet, true, 10000, own.SpellCriticalChance, 150, job.Tier.Skill.Id)
                { RampageEpoch = job.Epoch, RampageSkill = own, Effect = job.Tier.Skill.Row, Level = job.Level } });
    }

    static async Task AdvanceRampageWeapons(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        List<(uint Id, SpawnedMonster Actor, RampageWorld.Actor Plan, uint TargetId, SpawnedMonster Target)> swings = [];
        List<(uint Id, (int X, int Y) From, (int X, int Y) To)> moves = [];
        lock (conn.Monsters)
        {
            double now = ProjectileWorldTime(conn);
            foreach (var plan in conn.RampageWorld.Actors.Values.Where(a => a.Weapon is not null).ToArray())
            {
                if (!RampageEffectCurrent(conn, plan.Epoch) || plan.NextActionAt > now
                    || !conn.Monsters.TryGetValue(plan.Id, out var actor) || actor.Dead || now < actor.BusyUntil) continue;
                var from = actor.Walk.Position(now);
                var target = conn.Monsters.Where(p => p.Value.Hostile && !p.Value.Dead && p.Value.OwnerId is null)
                    .OrderBy(p => Dist(from, p.Value.Walk.Position(now))).FirstOrDefault();
                var to = target.Value?.Walk.Position(now) ?? conn.Walk.Position(now);
                double range = plan.Weapon == RampageCatalog.WeaponKind.Bow ? 80
                    : plan.Weapon == RampageCatalog.WeaponKind.TwoHandedSword ? 20 : 10;
                if (target.Value is not null && Dist(from, to) <= range)
                {
                    var sources = CharacterStatSources(conn).Stats;
                    int speed = Stats.Key("minion_attack_speed_+%") is uint k ? sources.GetValueOrDefault(k) : 0;
                    double seconds = (actor.Variety?.AttackMs ?? 1500) / 1000.0 / Math.Max(.01, 1 + (40 + speed) / 100.0);
                    conn.RampageWorld.Actors[plan.Id] = plan with { NextActionAt = now + seconds };
                    actor.Walk.Order(from.X, from.Y, now); actor.LastTarget = null;
                    swings.Add((plan.Id, actor, plan, target.Key, target.Value));
                }
                else if (Dist(from, to) > 8 && (actor.LastTarget is not { } last || Dist(last, to) > 3))
                {
                    actor.Walk.Order(to.X, to.Y, now); actor.LastTarget = to;
                    moves.Add((plan.Id, from, to));
                }
            }
        }
        foreach (var (id, from, to) in moves)
            await Send(remote, stream, ciphers, InstanceProtocol.ActionStarted(id, from.X, from.Y, to.X, to.Y,
                0x2909, 0xffff, 0x0408), "Rampage weapon moves");
        foreach (var swing in swings)
        {
            var from = swing.Actor.Walk.Position(ProjectileWorldTime(conn));
            var to = swing.Target.Walk.Position(ProjectileWorldTime(conn));
            await Send(remote, stream, ciphers, InstanceProtocol.ActionStarted(swing.Id, from.X, from.Y, to.X, to.Y,
                SkillCatalog.FirstSkillId, 0xffff, 0x0408, targetObjectId: swing.TargetId), "Rampage weapon attacks");
            var variety = swing.Actor.Variety!;
            var baseDamage = Monsters.BaseDamage(variety, (byte)swing.Plan.Level) ?? (1, 1);
            var sources = CharacterStatSources(conn).Stats;
            int inc = Stats.Key("minion_damage_+%") is uint key ? sources.GetValueOrDefault(key) : 0;
            var packet = DamagePacket.Of(DamageType.Physical, baseDamage.Item1, baseDamage.Item2)
                .Scale(Math.Max(0, 100L + inc) / 100.0);
            int accuracyInc = Stats.Key("minion_accuracy_rating_+%") is uint ak ? sources.GetValueOrDefault(ak) : 0;
            int accuracy = (int)Math.Clamp((long)(Monsters.BaseAccuracy((byte)swing.Plan.Level) ?? 0) * Math.Max(0L, 100L + accuracyInc) / 100, 0, int.MaxValue);
            if (swing.Plan.Weapon == RampageCatalog.WeaponKind.Bow)
            {
                var session = ProjectilesFor(remote, stream, ciphers, conn);
                var arrow = SkillsFor(conn).Projectile("Metadata/Projectiles/DefaultArrow");
                var profile = new ProjectileProfile(arrow?.Id ?? "Metadata/Projectiles/DefaultArrow")
                { Speed = ProjectileSpeed.CellsPerSecond(arrow?.Speed ?? 3260, _ => 0, _ => 0), Range = 120 };
                var emitter = session.ActorKey(swing.Id, swing.Actor);
                session.Schedule(ProjectileRuntime.Time(ProjectileWorldTime(conn) + .25),
                    () => RampageEffectCurrent(conn, swing.Plan.Epoch) && !swing.Actor.Dead
                        && ProjectileWorldTime(conn) < swing.Plan.ExpiresAt
                        && ReferenceEquals(conn.Monsters.GetValueOrDefault(swing.Id), swing.Actor), () =>
                    new ProjectileUse(profile, new(PlayerId, conn.ProjectileEpoch), emitter, 1,
                        new(from.X, from.Y), new(to.X, to.Y))
                    { Payload = new ProjectileOffense(packet, false, accuracy, 500, 150, "Rampage bow")
                        { RampageEpoch = swing.Plan.Epoch } });
            }
            else
            {
                lock (conn.Monsters)
                    if (RampageEffectCurrent(conn, swing.Plan.Epoch))
                        conn.RampageWorld.Strikes.Add(new(ProjectileWorldTime(conn) + .25, swing.Plan.Epoch,
                            swing.Id, swing.Actor, swing.TargetId, swing.Target, packet,
                            swing.Plan.Weapon == RampageCatalog.WeaponKind.TwoHandedSword, accuracy));
            }
        }
        RampageWorld.Strike[] contacts;
        lock (conn.Monsters)
        {
            double now = ProjectileWorldTime(conn);
            contacts = conn.RampageWorld.Strikes.Where(s => s.At <= now).ToArray();
            foreach (var contact in contacts) conn.RampageWorld.Strikes.Remove(contact);
        }
        foreach (var strike in contacts)
        {
            KeyValuePair<uint, SpawnedMonster>[] targets;
            lock (conn.Monsters)
            {
                if (!RampageEffectCurrent(conn, strike.Epoch) || strike.Actor.Dead
                    || !ReferenceEquals(conn.Monsters.GetValueOrDefault(strike.ActorId), strike.Actor)) continue;
                double now = ProjectileWorldTime(conn);
                var at = strike.Actor.Walk.Position(now);
                targets = conn.Monsters.Where(p => p.Value.Hostile && !p.Value.Dead && p.Value.OwnerId is null
                    && (strike.Area || p.Key == strike.TargetId && ReferenceEquals(p.Value, strike.Target))
                    && Dist(p.Value.Walk.Position(now), at) <= (strike.Area ? 20 : 12)).ToArray();
            }
            foreach (var target in targets)
                await RampageOwnedHit(remote, stream, ciphers, conn, strike.Epoch,
                    target.Key, target.Value, strike.Damage, null, "Rampage weapon", attack: true, accuracy: strike.Accuracy);
        }
    }
}
