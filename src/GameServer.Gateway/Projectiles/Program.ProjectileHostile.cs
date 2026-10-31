using System.Net.Sockets;
using GameServer.Core.Projectiles;
using GameServer.Core.Protocol.Login;

/// <summary>A monster's projectile attack in flight: the monster that released it, its attack and the AI action it belongs
/// to. The player is its only target (<c>Program.HostileHit</c>); the monster's own damage pipeline commits the hit when
/// the flight reaches the player, or, for a mortar, when it lands on them.</summary>
sealed record HostileProjectileOffense(uint MonsterId, SpawnedMonster Monster, MonsterAttack Attack, int Action, string Label)
{
    /// <summary>The rig clips the object needs to be seen (Program.MonsterProjectilePresentation).</summary>
    public Program.ProjectilePresentation? Presentation { get; init; }
}

partial class Program
{
    // The mortar's splash. Sand Spitter's skill carries no radius stat (is_area_damage, projectile_uses_contact_position
    // and a 3-cell spread only), so this is a development default like the Kinetic Blast fallbacks, in cells.
    internal const double MonsterMortarRadiusCells = 10;

    internal static double MonsterMortarRadius() =>
        double.TryParse(Environment.GetEnvironmentVariable("Game_MONSTER_MORTAR_RADIUS"), out double radius) && radius > 0
            ? radius : MonsterMortarRadiusCells;

    /// <summary>The use for a monster's ranged attack released now: from the monster toward the player's current cell. A
    /// mortar lands there and damages the area; a straight shot flies on past it. Team 2, so only the player (and friendly
    /// actors) are in its way, never the other monsters. The client draws nothing of its own for a monster's action
    /// (Frida 2026-10-29), so the server's object is the projectile the player sees.</summary>
    internal static ProjectileUse MonsterProjectileUse(ProjectileSession session, uint id, SpawnedMonster m, MonsterAttack a,
        SkillCatalog.MonsterProjectileInfo projectile, int action)
    {
        var conn = session.Conn;
        double now = session.Runtime.Now / (double)ProjectileRuntime.QuantaPerSecond;
        var from = ProjectileSession.Vector(m.Walk.Position(now));
        var aim = ProjectileSession.Vector(conn.Walk.Position(now));
        double speed = ProjectileSpeed.CellsPerSecond(projectile.Speed, _ => 0, _ => 0,
            Environment.GetEnvironmentVariable("Game_PROJECTILE_SPEED"));
        double distance = Math.Max(1, (aim - from).Length);
        var toward = (aim - from).Unit;
        // The release point: the clip's contact keyframe along the line to the aim (the Spitter's lies 4.2 cells behind a
        // body that faces away from its target, so toward the player, 9 steps up), else 1.5 cells ahead at the default
        // height; never past half way to the aim, so a close target is not behind the shot. The shot still flies from the
        // monster toward the aim (maintain_projectile_direction_when_using_contact_position); the keyframe's lateral part
        // is not applied (ContactPoint).
        var release = a.Release;
        double ahead = release?.Point is { } point ? point.ForwardCells * (release.FacesAway ? -1 : 1) : OrdinaryRangedAttack.ReleaseAheadCells;
        ahead = Math.Min(ahead, distance / 2);
        var origin = from + toward * ahead;
        // A lob lands on the aim, but no closer to the monster than the skill's minimum range (projectile_minimum_range,
        // the Spitter's 50 units = 4.6 cells): a player at its feet stands inside the dead zone. The runtime lands a lob
        // at its aim, so the aim itself moves out to the landing point.
        double landing = Math.Max(0.5, Math.Max((aim - origin).Length, projectile.MinimumRange / WorldUnits.PerCell - ahead));
        var target = projectile.Lobbed ? origin + toward * landing : aim;
        var profile = new ProjectileProfile(projectile.Metadata)
        {
            Speed = speed, Radius = projectile.CollisionRadius, Count = 1, Lobbed = projectile.Lobbed,
            // A lobbed shot's range is its landing distance; a straight one flies on past the aim.
            Range = projectile.Lobbed ? landing : Math.Max(distance, a.Range) * 1.5 + 5,
            DirectDamage = !projectile.Lobbed,
            EndArea = projectile.Lobbed ? new ProjectileAreaRecipe("monster mortar", MonsterMortarRadius()) : null,
            EndAreaReasons = projectile.Lobbed
                ? new HashSet<ProjectileEndReason> { ProjectileEndReason.Landing, ProjectileEndReason.Terrain } : null,
        };
        var owner = session.ActorKey(id, m);
        double flightCells = projectile.Lobbed ? landing : (aim - origin).Length;
        var presentation = MonsterProjectilePresentation(SkillsFor(conn).Clips(projectile.Metadata), flightCells / speed);
        if (release?.Point is { } launch) presentation = (presentation ?? new ProjectilePresentation()) with { LaunchHeight = launch.HeightSteps };
        Console.WriteLine($"[gateway] {session.remote} monster 0x{id:x} ({m.Label}) releases {projectile.Metadata} from {origin}"
            + $" ({ahead:0.0} cells toward the aim, {(release?.Point is { } up ? up.HeightSteps.ToString() : "default")} steps up"
            + $"{(release is null ? "" : $", clip {release.Clip} at {release.Fraction:0.000} of the attack{(release.FacesAway ? ", facing away" : "")}")})"
            + $" aim={aim}{(projectile.Lobbed ? $" lands at {target} ({landing:0.0} cells)" : "")}");
        return new ProjectileUse(profile, owner, owner, 2, origin, target)
            { Payload = new HostileProjectileOffense(id, m, a, action, $"{m.Label} {projectile.Metadata}") { Presentation = presentation } };
    }

    /// <summary>A server-created projectile object plays nothing by itself. Start the rig's flight clip at spawn unless the
    /// .ao plays it as its default anyway (the wand bolt), stretching a once-only clip over the flight (the mortar's 1 s
    /// <c>proj</c> over its arc: <c>mortar_projectile_scale_animation_speed_instead_of_projectile_speed</c>), and play
    /// the impact clip where it stops, keeping the object for the clip's length. Null without clip data: the object
    /// then flies as its .ao presents it by itself.</summary>
    internal static ProjectilePresentation? MonsterProjectilePresentation(SkillCatalog.ProjectileClips? clips, double flightSeconds)
    {
        if (clips is null || clips is { Flight: null, Impact: null }) return null;
        var presentation = new ProjectilePresentation();
        if (clips.Flight is { } flight && clips.DefaultAnimation != flight.Name)
            presentation = presentation with
            {
                FlightClip = flight.Index,
                FlightClipSpeed = flight.Once && flightSeconds > 0 ? (float)Math.Clamp(flight.Ms / 1000.0 / flightSeconds, 0.1, 10) : 1,
            };
        if (clips.Impact is { } impact)
            presentation = presentation with
            {
                ImpactClip = impact.Index,
                ImpactLifetime = ProjectileRuntime.Time(Math.Max(0.1, impact.Ms / 1000.0)),
            };
        return presentation;
    }

    // The release at the attack's contact: the shot leaves toward where the player stands now and the flight then owns
    // contact and damage (HostileHit). The AI action must still be current when the runtime releases it.
    internal static void LaunchMonsterProjectile(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        uint id, SpawnedMonster m, MonsterAttack a, SkillCatalog.MonsterProjectileInfo projectile, int action)
    {
        var session = ProjectilesFor(remote, stream, ciphers, conn);
        session.Advance();
        session.Schedule(session.Runtime.Now, () => MonsterActionCurrent(conn, id, m, action) && !InGracePeriod(conn),
            () => MonsterProjectileUse(session, id, m, a, projectile, action));
    }

    // Called under the XP -> Monsters lock by the runtime. Only the player takes a monster's projectile; a friendly monster in
    // its way is passed through. The monster's damage pipeline (CommitMonsterHitLocked) owns evasion, block, armour,
    // ailments, recovery and the hit packet; the presentation follows through the session's ordered outbox.
    internal static ProjectileHitResult HostileHit(ProjectileSession session, ProjectileDamage contact, HostileProjectileOffense shot)
    {
        var conn = session.Conn;
        if (contact.Target.Id != PlayerId || conn.PlayerLife == 0 || InGracePeriod(conn)) return ProjectileHitResult.Miss;
        // The hit pipeline's timers (fortification, recent hits, ailments, evasion entropy) run on the connection clock and
        // refuse to go backwards. The contact's simulated instant lies a few milliseconds behind that clock, which other
        // reads (a stats refresh, a melee swing) may already have advanced, so the hit lands at whichever is later. The
        // runtime's fixtures run their simulation ahead of the clock; there the contact time wins.
        double now = Math.Max(contact.At / (double)ProjectileRuntime.QuantaPerSecond, conn.Clock.Elapsed.TotalSeconds);
        var here = conn.Walk.Position(now);
        var from = ((int)Math.Round(contact.Position.X), (int)Math.Round(contact.Position.Y));
        var outcome = CommitMonsterHitLocked(session.remote, conn, shot.MonsterId, shot.Monster, shot.Attack, now, here, from, checkRange: false);
        if (outcome is null) return ProjectileHitResult.Miss;
        session.Packet(outcome.HitPacket, $"0x01af monster 0x{shot.MonsterId:x} projectile hit on the player ({shot.Label})");
        session.Enqueue(() => PresentMonsterHit(session.remote, session.stream, session.ciphers, conn, shot.MonsterId, outcome));
        return outcome.Blocked ? ProjectileHitResult.Blocked : ProjectileHitResult.Hit;
    }
}
