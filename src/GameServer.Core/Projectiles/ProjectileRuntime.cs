namespace GameServer.Core.Projectiles;

/// <summary>A deterministic connection/instance-owned combat simulator. The caller serializes all access.
/// Simulation never awaits presentation. Global earliest contacts run before later contacts in other flights.</summary>
public sealed partial class ProjectileRuntime(IProjectileWorld world)
{
    public const long QuantaPerSecond = 120000, TickQuanta = 4000;
    readonly SortedDictionary<long, ProjectileEntity> projectiles = [];
    readonly PriorityQueue<Action, (long At, long Order)> scheduled = new();
    readonly ProjectileHitLedger ledger = new();
    readonly Dictionary<long, ProjectileEntity> pendingAreas = [];
    long sequence, commandSequence;
    public long Now { get; private set; }
    public IReadOnlyCollection<ProjectileEntity> Projectiles => projectiles.Values;
    public static long Time(double seconds) => checked((long)Math.Round(seconds * QuantaPerSecond));

    public void Schedule(long at, Action command)
    {
        if (at < Now) throw new ArgumentOutOfRangeException(nameof(at), "Cannot insert a command in committed history.");
        scheduled.Enqueue(command, (at, ++commandSequence));
    }

    public long Release(ProjectileUse use)
    {
        Validate(use.Profile);
        if (use.ProjectilePayloads is { } payloads && payloads.Count != use.Profile.Count)
            throw new ArgumentException("Payload count must match initial projectile count.", nameof(use));
        long useId = ++sequence, emission = ++sequence;
        // One sample per released use, independent of spread, damage and child RNG streams.
        int returnRoll = new Random(unchecked(use.Seed * 397 ^ (int)useId ^ 0x3172c85d)).Next(100);
        use = use with { Profile = ResolveReturn(use.Profile, returnRoll) };
        var random = use.Profile.RandomSpread ? new Random(unchecked(use.Seed * 397 ^ (int)useId)) : null;
        int count = use.Profile.Count;
        double spread = use.Profile.SpreadRadians;
        if (spread is > 0 and < Math.PI && use.Profile.AimSpreadDistance > 0)
            spread = 2 * Math.Atan(Math.Tan(spread / 2) * use.Profile.AimSpreadDistance
                / Math.Max(.001, (use.Aim - use.Origin).Length));
        for (int i = 0; i < count; i++)
        {
            double angle = random is not null ? spread * (random.NextDouble() - .5) : count <= 1 ? 0 : spread *
                (i / (double)((use.Profile.CircularSpread ?? spread >= Math.Tau) ? count : count - 1) - .5);
            var direction = (use.Aim - use.Origin).Unit.Rotate(angle);
            long group = use.Profile.SequentialDelay > 0 && i > 0 ? ++sequence : emission;
            var projectileUse = use.ProjectilePayloads is { } snapshots
                ? use with { Payload = snapshots[i], ProjectilePayloads = null } : use;
            void Emit() => Spawn(projectileUse, useId, group, 0, use.Origin, direction);
            if (use.Profile.SequentialDelay > 0 && i > 0) Schedule(Now + i * use.Profile.SequentialDelay, Emit);
            else Emit();
        }
        return useId;
    }

    static void Validate(ProjectileProfile p)
    {
        if (p.Count is < 1 or > 256 || !double.IsFinite(p.Speed) || p.Speed <= 0
            || !double.IsFinite(p.Range) || p.Range <= 0 || !double.IsFinite(p.Radius) || p.Radius < 0
            || p.Lifetime <= 0 || p.SequentialDelay < 0 || p.ReturnChancePercent is < 0 or > 100
            || !double.IsFinite(p.ReturnSpeedMultiplier) || p.ReturnSpeedMultiplier <= 0
            || !double.IsFinite(p.ReturnOvershoot) || p.ReturnOvershoot < 0 || p.ReturnHitLimit < 0 || p.OutboundHitLimit < 0)
            throw new ArgumentOutOfRangeException(nameof(p));
        foreach (var area in new[] { p.ContactArea, p.EndArea })
            if (area is { Delay: < 0 } or { Interval: < 0 }) throw new ArgumentOutOfRangeException(nameof(p));
    }

    static ProjectileProfile ResolveReturn(ProjectileProfile profile, int roll) => profile with
    {
        Returns = !profile.CannotReturn && (profile.Returns || roll < profile.ReturnChancePercent),
        // Child emissions share this use's roll; continuation children already share its profile.
        EndEmission = profile.EndEmission is { } end ? end with { Child = ResolveReturn(end.Child, roll) } : null,
        TravelEmission = profile.TravelEmission is { } travel ? travel with { Child = ResolveReturn(travel.Child, roll) } : null
    };

    ProjectileEntity Spawn(ProjectileUse use, long useId, long emission, long parent,
        ProjectileVector position, ProjectileVector direction, ProjectileEntity? continuing = null, bool split = false)
    {
        var p = new ProjectileEntity(++sequence, useId, emission, parent, use, position, direction, Now);
        projectiles.Add(p.Id, p);
        if (continuing is { } previous)
        {
            // Fork and split start a new movement instance: the client caps each instance separately (150 units =
            // 15 m), so the child gets a fresh range budget while the total travelled distance keeps accumulating.
            p.Remaining = p.Profile.Range; p.Distance = previous.Distance; p.ExpiresAt = previous.ExpiresAt;
            p.Speed = previous.Speed; p.Form = previous.Form; p.DamageMultiplier = previous.DamageMultiplier;
            p.OutboundContacts = previous.OutboundContacts;
            p.Phase = previous.Phase; p.ReturnContacts = previous.ReturnContacts;
            p.TurnaroundTarget = previous.TurnaroundTarget;
            p.Pierce = previous.Pierce; p.Fork = previous.Fork; p.Chain = previous.Chain; p.Split = 0;
            p.Contacts.UnionWith(previous.Contacts); p.Interactions.UnionWith(previous.Interactions);
        }
        if (split && p.Profile.SplitForm is { } splitForm)
        {
            p.Form = 1; p.Speed *= splitForm.SpeedMultiplier; p.Pierce = splitForm.Pierce;
        }
        // A lobbed child still lands at the use's aim, never past it.
        if (p.Profile.Lobbed) p.Remaining = Math.Max(.001, Math.Min(p.Remaining, (use.Aim - position).Length));
        Publish(ProjectileEventKind.Spawn, p);
        StartStages(p);
        return p;
    }

    public void AdvanceTo(long until)
    {
        if (until < Now) throw new ArgumentOutOfRangeException(nameof(until));
        while (true)
        {
            while (scheduled.TryPeek(out _, out var due) && due.At <= Now) scheduled.Dequeue()();
            if (projectiles.Count == 0)
            {
                if (Now == until) break;
                Now = scheduled.TryPeek(out _, out var idleNext) ? Math.Min(until, idleNext.At) : until;
                continue;
            }
            long horizon = Math.Min(until, (Now / TickQuanta + 1) * TickQuanta);
            if (scheduled.TryPeek(out _, out var next)) horizon = Math.Min(horizon, next.At);
            var actors = world.Actors(Now, horizon);
            Contact? first = null;
            foreach (var p in projectiles.Values)
            {
                var candidate = NextContact(p, horizon, actors);
                if (candidate is { } c && (first is null || c.CompareTo(first.Value) < 0)) first = c;
            }
            // The upper bound is inclusive: another flight may have a contact at the same instant.
            if (first is null && Now == until) break;
            long at = first?.At ?? horizon;
            double seconds = (at - Now) / (double)QuantaPerSecond;
            foreach (var p in projectiles.Values)
            {
                double distance = Math.Min(p.Remaining, seconds * p.Speed);
                p.Position += p.Direction * distance;
                p.Remaining -= distance;
                p.Distance += distance;
            }
            Now = at;
            if (first is { } contact) Resolve(contact, actors);
        }
        // Detached delayed areas still need the triggering flight's phase-specific admission history.
        ledger.RetainGroups(projectiles.Values.Concat(pendingAreas.Values)
            .SelectMany(p => new[] { p.Id, p.EmissionId, p.ChildEmission }).ToHashSet());
    }

    readonly record struct Contact(long At, ProjectileEntity Projectile, ProjectileActor? Target,
        ProjectileEndReason? End, ProjectileVector Normal, ProjectileInteraction? Interaction = null,
        ProjectileVector? TerrainPoint = null) : IComparable<Contact>
    {
        public int CompareTo(Contact other)
        {
            int c = At.CompareTo(other.At);
            if (c != 0) return c;
            c = Projectile.Id.CompareTo(other.Projectile.Id);
            return c != 0 ? c : (Target?.Key.Id ?? 0).CompareTo(other.Target?.Key.Id ?? 0);
        }
    }

    Contact? NextContact(ProjectileEntity p, long horizon, IReadOnlyList<ProjectileActor> actors)
    {
        long end = Math.Min(horizon, p.ExpiresAt);
        long rangeEnd = Now + (long)Math.Ceiling(Math.Max(0, p.Remaining) / p.Speed * QuantaPerSecond);
        end = Math.Min(end, rangeEnd);
        ProjectileVector to = p.Position + p.Direction * Math.Min(p.Remaining, p.Speed * (end - Now) / QuantaPerSecond);
        Contact? best = end == p.ExpiresAt ? new(end, p, null, ProjectileEndReason.Lifetime, default)
            : end == rangeEnd ? new(end, p, null, p.Profile.Lobbed ? ProjectileEndReason.Landing
                : p.Phase == 0 ? ProjectileEndReason.Distance : ProjectileEndReason.Returned, default) : null;
        if (!(p.Phase > 0 && p.Profile.IgnoreTerrainOnReturn) && world.TraceFlight(p, p.Position, to) is { } wall)
        {
            double fraction = Math.Clamp(wall.Fraction, 0, 1);
            best = new(Now + (long)Math.Ceiling((end - Now) * fraction), p, null,
                ProjectileEndReason.Terrain, wall.Normal, TerrainPoint: p.Position + (to - p.Position) * fraction);
        }
        foreach (var interaction in Interactions.OrderBy(w => w.Id))
        {
            if (interaction.Capacity <= 0 || p.Interactions.Contains(interaction.Id)
                || (interaction.Team is int team && team != p.Use.Team)) continue;
            if (Crossing(p.Position, to, interaction) is not double fraction) continue;
            long at = Now + (long)Math.Ceiling((end - Now) * fraction);
            if (best is null || at < best.Value.At) best = new(at, p, null, null, default, interaction);
        }
        int? contactLimit = p.Phase == 0 ? p.Profile.OutboundHitLimit : p.Profile.ReturnHitLimit;
        int contacts = p.Phase == 0 ? p.OutboundContacts : p.ReturnContacts;
        if (!p.Profile.DirectDamage || p.Profile.Lobbed || contactLimit is int limit && contacts >= limit) return best;
        var directRule = DirectHitRule(p);
        bool skipDenied = p.Phase == 0 ? p.Profile.SkipDeniedContacts || p.Profile.OutboundHitLimit is not null
            : p.Profile.SkipDeniedReturnContacts || p.Profile.ReturnHitLimit is not null;
        foreach (var actor in actors.OrderBy(a => a.Key.Id).ThenBy(a => a.Key.Generation))
        {
            if (actor.Team == p.Use.Team || actor.Key == p.Use.Owner || actor.Key == p.TurnaroundTarget) continue;
            if (p.Contacts.Contains(actor.Key) && directRule.Repeat != ProjectileRepeat.Once
                && (p.Position - actor.Position(Now)).Length > p.Profile.Radius + actor.Radius + .001)
                p.Contacts.Remove(actor.Key);
            if (p.Contacts.Contains(actor.Key)) continue;
            double? t = ProjectileGeometry.Contact(p.Position - actor.Position(Now), to - actor.Position(end),
                p.Profile.Radius + actor.Radius);
            if (t is not double f) continue;
            long at = Now + (long)Math.Ceiling((end - Now) * f);
            if (skipDenied && !ledger.CanHit(directRule.Scope == ProjectileHitScope.Projectile ? p.Id : p.EmissionId,
                p.Phase, "direct", actor.Key, at, p.CreatedAt, directRule)) continue;
            // Terrain wins coincident contact. An actor at the range endpoint still receives its contact.
            if (best is null || at < best.Value.At || (at == best.Value.At && best.Value.End != ProjectileEndReason.Terrain
                && best.Value.Target is null)) best = new(at, p, actor, null, default);
        }
        return best;
    }

    void Resolve(Contact c, IReadOnlyList<ProjectileActor> actors)
    {
        var p = c.Projectile;
        if (c.TerrainPoint is { } point)
        {
            // Contact time is quantized upward, but geometry must stay on the surface. Otherwise
            // fast reflected/split flights can start inside the blocked cell and die immediately.
            double excess = (p.Position - point).Dot(p.Direction);
            p.Distance -= excess; p.Remaining += excess; p.Position = point;
        }
        if (c.Interaction is { } interaction) { Interact(p, interaction, actors); return; }
        if (c.Target is { } target)
        {
            p.Contacts.Add(target.Key);
            if (p.Phase > 0) p.ReturnContacts++;
            else p.OutboundContacts++;
            var terminal = p.Profile.EndArea is { } endArea && AllowsEndArea(p, ProjectileEndReason.Enemy)
                && !CanContinue(p, actors) && ReturnAnchor(p, ProjectileEndReason.Enemy) is null ? endArea : null;
            var result = Damage(p, target, terminal?.Stage ?? "direct", multiplier: terminal?.Multiplier ?? 1,
                admissionStage: "direct", isArea: terminal is not null);
            if (result == ProjectileHitResult.Miss) return;
            if ((p.Phase == 0 || p.Profile.ContactAreaOnReturn) && p.Profile.ContactArea is { } area)
                Area(p, area, area.ExcludeContactTarget ? target.Key : null);
            if (result == ProjectileHitResult.Hit && p.Profile.ContactTargets is { } strikes)
                TargetedHits(p, target, strikes);
            if (Continue(p, target, world.Actors(Now, Now))) return;
            End(p, ProjectileEndReason.Enemy, victim: target.Key);
        }
        else if (c.End == ProjectileEndReason.Terrain)
        {
            if (AllowsTerminalArea(p) && (p.Phase == 0 || p.Profile.ContactAreaOnReturn)
                && p.Profile.ContactArea is { } area) Area(p, area);
            if (p.Profile.SplitOnTerrain && !p.Profile.CannotSplit && p.Split > 0)
            {
                p.Position += c.Normal * .001; // Children start on the incident side of the blocking surface.
                if (Split(p, actors, c.Normal)) return;
            }
            if (p.TerrainBounces > 0)
            {
                p.TerrainBounces--;
                p.Direction = (p.Direction - c.Normal * (2 * p.Direction.Dot(c.Normal))).Unit;
                p.Position += c.Normal * .001;
                Publish(ProjectileEventKind.Redirect, p);
            }
            else End(p, c.End.Value, terrainNormal: c.Normal);
        }
        else End(p, c.End!.Value);
    }

    bool CanContinue(ProjectileEntity p, IReadOnlyList<ProjectileActor> actors) => p.Phase == 0 && p.Profile.OutboundHitLimit is not null
        || p.Phase > 0 && p.Profile.PierceAllOnReturn && !p.Profile.CannotPierce
        || (!p.Profile.CannotSplit && p.Split > 0 && (p.Profile.SplitWithoutTargets || Nearby(p, actors).Any()))
        || (!p.Profile.CannotPierce && p.Pierce != 0) || (!p.Profile.CannotFork && p.Fork > 0)
        || (!p.Profile.CannotChain && p.Chain > 0 && Nearby(p, actors).Any());

    ProjectileHitResult Damage(ProjectileEntity p, ProjectileActor target, string stage,
        long? group = null, ProjectileHitRule? rule = null, double multiplier = 1, string? admissionStage = null,
        bool isArea = false)
    {
        rule ??= (admissionStage ?? stage) == "direct" ? DirectHitRule(p) : p.Profile.HitRule;
        long g = group ?? (rule.Scope == ProjectileHitScope.Projectile ? p.Id : p.EmissionId);
        var result = ProjectileHitResult.Blocked;
        ledger.TryHit(g, p.Phase, admissionStage ?? stage, target.Key, Now, p.CreatedAt, rule, () =>
        {
            result = world.Hit(new(p, target.Key, Now, stage, p.Position, multiplier * p.DamageMultiplier) { IsArea = isArea });
            return result != ProjectileHitResult.Miss;
        });
        if (result == ProjectileHitResult.Hit)
            world.Publish(new(ProjectileEventKind.Hit, p, Now, target.Position(Now), Stage: stage));
        return result;
    }

    static ProjectileHitRule DirectHitRule(ProjectileEntity p) =>
        p.Phase > 0 ? p.Profile.ReturnHitRule ?? p.Profile.HitRule : p.Profile.HitRule;

    bool Continue(ProjectileEntity p, ProjectileActor target, IReadOnlyList<ProjectileActor> actors)
    {
        var profile = p.Profile;
        // Explicit contact budgets limit actor attempts, never the remaining flight.
        if (p.Phase == 0 && profile.OutboundHitLimit is not null) return true;
        if (p.Split > 0 && !profile.CannotSplit && Split(p, actors)) return true;
        if (p.Phase > 0 && profile.PierceAllOnReturn && !profile.CannotPierce) return true;
        if (!profile.CannotPierce && p.Pierce != 0) { if (p.Pierce > 0) p.Pierce--; return true; }
        if (!profile.CannotFork && p.Fork > 0)
        {
            p.Fork--;
            ContinueChild(p, p.Direction.Rotate(Math.PI / 6));
            ContinueChild(p, p.Direction.Rotate(-Math.PI / 6));
            End(p, ProjectileEndReason.Fork, allowReturn: false);
            return true;
        }
        if (!profile.CannotChain && p.Chain > 0 && Nearby(p, actors).FirstOrDefault() is { } next)
        {
            p.Chain--; p.Direction = (next.Position(Now) - p.Position).Unit;
            // A chain jump starts a new movement instance, so its 15 m cap is fresh; Distance stays total for Far Shot.
            p.Remaining = p.Profile.Range;
            Publish(ProjectileEventKind.Redirect, p);
            return true;
        }
        return false;
    }

    IEnumerable<ProjectileActor> Nearby(ProjectileEntity p, IReadOnlyList<ProjectileActor> actors) => actors
        .Where(a => a.Team != p.Use.Team && !p.Contacts.Contains(a.Key) && a.Key != p.Use.Owner && a.Key != p.TurnaroundTarget
            && (a.Position(Now) - p.Position).Length <= p.Profile.ChainRange)
        .OrderBy(a => (a.Position(Now) - p.Position).Length).ThenBy(a => a.Key.Id).ThenBy(a => a.Key.Generation);

    bool Split(ProjectileEntity p, IReadOnlyList<ProjectileActor> actors, ProjectileVector? terrainNormal = null)
    {
        var targets = Nearby(p, actors)
            .Where(a => terrainNormal is null || world.TraceTerrain(p.Position, a.Position(Now)) is null)
            .Take(p.Split).ToArray();
        if (targets.Length == 0 && terrainNormal is null && !p.Profile.SplitWithoutTargets) return false;
        foreach (var other in targets)
            Spawn(p.Use, p.UseId, p.EmissionId, p.Id, p.Position, (other.Position(Now) - p.Position).Unit,
                continuing: p, split: true);
        if (terrainNormal is not null || p.Profile.SplitWithoutTargets)
            // LocalExile provisional geometry for unclaimed slots: seeded azimuths after enemy hits,
            // incident hemisphere after terrain hits. The split count does not depend on this geometry.
            for (int i = targets.Length; i < p.Split; i++)
                Spawn(p.Use, p.UseId, p.EmissionId, p.Id, p.Position,
                    terrainNormal is { } normal ? normal.Unit.Rotate((p.Random.NextDouble() - .5) * Math.PI)
                        : p.Direction.Rotate(p.Random.NextDouble() * Math.Tau), continuing: p, split: true);
        End(p, ProjectileEndReason.Split, allowReturn: false);
        return true;
    }

    void ContinueChild(ProjectileEntity p, ProjectileVector direction)
    {
        Spawn(p.Use, p.UseId, p.EmissionId, p.Id, p.Position, direction, continuing: p);
    }

    void End(ProjectileEntity p, ProjectileEndReason reason, bool allowReturn = true, ProjectileActorKey? victim = null,
        ProjectileVector? terrainNormal = null)
    {
        if (allowReturn && ReturnAnchor(p, reason) is { } anchor)
        {
            if (p.Profile.EndAreaOnReturnTurnaround && AllowsEndArea(p, reason) && p.Profile.EndArea is { } turnArea)
                Area(p, turnArea, victim);
            if (terrainNormal is { } normal) p.Position += normal * .001;
            p.Phase = 1; p.Contacts.Clear();
            if (p.Profile.ExcludeTurnaroundTarget) p.TurnaroundTarget = victim;
            var delta = anchor.Position(Now) - p.Position;
            p.Direction = delta.Unit; p.Remaining = Math.Max(.001, delta.Length + p.Profile.ReturnOvershoot);
            p.Speed *= p.Profile.ReturnSpeedMultiplier;
            Publish(ProjectileEventKind.Redirect, p);
            return;
        }
        if (reason is ProjectileEndReason.Enemy or ProjectileEndReason.Terrain or ProjectileEndReason.Distance
            or ProjectileEndReason.Lifetime or ProjectileEndReason.Landing or ProjectileEndReason.Returned)
        {
            if (AllowsEndArea(p, reason) && p.Profile.EndArea is { } area) Area(p, area, victim);
            if (p.Profile.EndEmission is { } emission) EmitChildren(p, emission);
        }
        if (reason == ProjectileEndReason.Landing && p.GroundChains > 0)
        {
            p.GroundChains--;
            p.GroundBounces++;
            p.Remaining = Math.Max(.001, p.Profile.GroundBounceDistance);
            Publish(ProjectileEventKind.Redirect, p);
            return;
        }
        projectiles.Remove(p.Id);
        Publish(ProjectileEventKind.End, p, reason);
    }

    ProjectileActor? ReturnAnchor(ProjectileEntity p, ProjectileEndReason reason) =>
        p.Phase == 0 && p.Profile.Returns && !p.Profile.CannotReturn
        && Now < p.ExpiresAt
        && (reason != ProjectileEndReason.Distance || p.Profile.ReturnOnDistance)
        && reason is ProjectileEndReason.Enemy or ProjectileEndReason.Distance or ProjectileEndReason.Lifetime or ProjectileEndReason.Terrain
        && p.Use.ReturnAnchor is { } key ? world.Actors(Now, Now).FirstOrDefault(a => a.Key == key) : null;

    static bool AllowsEndArea(ProjectileEntity p, ProjectileEndReason reason) =>
        AllowsTerminalArea(p) && (p.Profile.EndAreaReasons is null || p.Profile.EndAreaReasons.Contains(reason));

    static bool AllowsTerminalArea(ProjectileEntity p) =>
        !p.Profile.TerminalAreaRequiresNoContact || p.Phase > 0 || p.OutboundContacts == 0;

    void Publish(ProjectileEventKind kind, ProjectileEntity p, ProjectileEndReason? reason = null) =>
        world.Publish(new(kind, p, Now, p.Position, reason));

    public void Clear()
    {
        foreach (var p in projectiles.Values.ToArray()) End(p, ProjectileEndReason.Teardown, allowReturn: false);
        scheduled.Clear(); pendingAreas.Clear(); ledger.Clear(); Interactions.Clear();
    }
}
