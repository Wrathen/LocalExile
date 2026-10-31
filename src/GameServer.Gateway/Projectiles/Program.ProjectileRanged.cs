using System.Net.Sockets;
using GameServer.Core.Projectiles;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;

partial class Program
{
    // WandProjectile's own rig plays its impact (index 0, 500 ms) with the WandTrailImpact sound and wand_impact particles.
    internal static readonly ProjectilePresentation OrdinaryWandPresentation = new()
    {
        ImpactClip = OrdinaryRangedAttack.WandImpactAnimation,
        ImpactLifetime = ProjectileRuntime.Time(OrdinaryRangedAttack.WandImpactSeconds),
    };

    // DefaultArrow's rig has no impact clip (Projectiles.dat gives it stuck and bounce art the client plays only for its own
    // arrows): the stopped arrow object stays in its target or wall for a second, then goes.
    internal static readonly ProjectilePresentation OrdinaryArrowPresentation = new()
    {
        StayOnImpact = true,
        ImpactLifetime = ProjectileRuntime.Time(OrdinaryRangedAttack.ArrowStuckSeconds),
    };

    // An ordinary bow or wand shot on the shared runtime. The release waits for the weapon's contact (attack1_bow, or
    // the ACT-selected wand clip for this input's sequence), then freezes damage, chance and multiplier; conditional
    // critical rules read the live owner at impact, and target evasion and impales resolve at contact.
    internal static Task ScheduleOrdinaryRanged(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        (int X, int Y) aim, double attackSeconds, int action, int epoch, string projectileId, ushort sequence,
        SkillCriticalRoll criticalRoll)
    {
        var session = ProjectilesFor(remote, stream, ciphers, conn);
        session.Advance();
        double acceptedAt = conn.Clock.Elapsed.TotalSeconds;
        var (delay, release) = OrdinaryRangedContact(conn, attackSeconds, projectileId, sequence);
        session.Schedule(ProjectileRuntime.Time(acceptedAt + delay),
            () => OrdinaryRangedReleaseCurrent(conn, action, epoch)
                && OrdinaryRangedAttack.ProjectileId(WeaponProfileFor(conn, activeSet: true)) == projectileId,
            () =>
            {
                var character = CharacterPlayerStats(conn);
                int Get(string name) => Stats.Key(name) is uint key ? character.GetValueOrDefault(key) : 0;
                int RawCritical(string name) => IsCriticalProhibition(name) ? 0 : Get(name);
                int effect = SkillEffectId(conn, SkillCatalog.FirstSkillId) is { } id ? SkillsFor(conn).EffectRow(id) : -1;
                var weapon = WeaponProfileFor(conn, activeSet: true);
                int criticalChance = CriticalProhibited(conn, attack: true)
                    ? AttackBaseCriticalStrike.Chance(!weapon.Unarmed, weapon.Critical, conn.Pools.CriticalStrikeChancePct, RawCritical)
                    : conn.Pools.AttackCriticalStrikeChance;
                var captured = OrdinaryRangedShot.Capture(WeaponFor(conn), character, SkillsFor(conn).EffectStats(effect, 1), Stats,
                    conn.Pools.Accuracy, criticalChance, conn.Pools.CriticalStrikeMultiplier);
                bool wand = projectileId == OrdinaryRangedAttack.Wand;
                var shot = new ProjectileOffense(captured.Hit, false, captured.Accuracy, captured.CriticalChance,
                    captured.CriticalMultiplier, wand ? "ordinary wand" : "ordinary bow")
                { SkillStats = SkillsFor(conn).EffectStats(effect, 1).ToDictionary(s => s.Item1, s => s.Item2),
                    Penetration = captured.Penetration, ResistanceMods = captured.ResistanceMods, ImpaleChance = captured.ImpaleChance,
                    ImpaleSettings = captured.ImpaleSettings, AccuracyProfile = captured.AccuracyProfile,
                    Ignite = MonsterIgnites.Settings.From(AilmentPanelStats.Compute(new("main_hand", Spell: false, Projectile: true),
                        SupportedAttackDamage.Raw(WeaponFor(conn), Get), DamageStats.Conversion(character, Stats, attack: true), Get),
                        "main_hand", Get),
                    CriticalRoll = criticalRoll, Leech = captured.Leech,
                    AccuracyMoreUnique = conn.Pools.AccuracyMoreVsUniquePct, AccuracyMoreClose = conn.Pools.AccuracyMoreInCloseRangePct,
                    AlwaysHits = Get("global_always_hit") != 0, FarShot = Get("player_far_shot") != 0,
                    ReturnMultiplier = ProjectileReturn.DamageMore(Get),
                    // The object flies at the height the clip releases it (the bow's nock, the wand's tip).
                    Presentation = (wand ? OrdinaryWandPresentation : OrdinaryArrowPresentation) with { LaunchHeight = release?.HeightSteps },
                    // The target's blood and particles on contact, matched to this shot's action (0x1af, kind 3).
                    HitPacket = true, ActionSequence = sequence };
                var ranged = SkillsFor(conn).Projectile(projectileId);
                double speed = ProjectileSpeed.CellsPerSecond(ranged?.Speed ?? 3260, Get, _ => 0,
                    Environment.GetEnvironmentVariable("Game_PROJECTILE_SPEED"));
                var profile = GatewayProjectileDefaults.Resolve(ranged?.Id ?? projectileId, Get, speed, attack: true);
                var origin = ProjectileSession.Vector(conn.Walk.Position(session.Runtime.Now / (double)ProjectileRuntime.QuantaPerSecond));
                var target = ProjectileSession.Vector(aim);
                // The shot leaves from the clip's contact keyframe, in front of the body along the aim (the Ranger's bow
                // 5.5 cells, a wand's tip 7.5), else 1.5 cells ahead; never past half way to the aim, so a close target
                // is not behind the release. The lateral part of the keyframe is not applied (ContactPoint).
                double ahead = release is { ForwardCells: > 0 } point ? point.ForwardCells : OrdinaryRangedAttack.ReleaseAheadCells;
                origin += (target - origin).Unit * Math.Min(ahead, (target - origin).Length / 2);
                return new(profile, new(PlayerId, epoch), new(PlayerId, epoch), 1, origin, target)
                    { Payload = shot, ReturnAnchor = new(PlayerId, epoch) };
            });
        return Task.CompletedTask;
    }
}
