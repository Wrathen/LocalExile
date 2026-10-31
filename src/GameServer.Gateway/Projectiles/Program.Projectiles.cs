using System.Net.Sockets;
using System.Threading.Channels;
using GameServer.Core.Projectiles;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;

partial class Program
{
    static readonly AsyncLocal<Func<bool>?> ProjectileOutputGuard = new();
    /// <summary>One gameplay pump and one presentation consumer per connection, regardless of projectile count.</summary>
    internal sealed class ProjectileSession : IProjectileWorld
    {
        internal readonly string remote;
        internal readonly NetworkStream stream;
        internal readonly ConnectionCiphers ciphers;
        internal readonly ClientState Conn;
        readonly int epoch;
        readonly Channel<Func<Task>> output = Channel.CreateBounded<Func<Task>>(
            new BoundedChannelOptions(8192) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
        sealed class Visual(uint id, ProjectileHeight height)
        {
            public readonly uint Id = id;
            public ProjectileHeight Height = height;
            public bool Created, Ended, SecondFormStarted;
            public ProjectileArc? Arc;
            public int MotionVersion;
            // Where the object is created (the release point) and whether its first move has been sent from there.
            public ProjectileVector SpawnedAt;
            public bool Glided;
            // An ended flight whose stop keeps the object (an impact clip, a stuck arrow) is still created when its
            // creation snapshot runs late: the shot then appears at its impact rather than not at all.
            public bool StaysAfterEnd;
        }
        readonly Dictionary<long, Visual> visuals = [];
        readonly Dictionary<SpawnedMonster, int> generations = new(ReferenceEqualityComparer.Instance);
        int generation, seed;
        bool invalid;
        public ProjectileRuntime Runtime { get; }
        public Task OutputCompleted { get; }
        public bool Active => !invalid && Conn.ProjectileActive(epoch);

        public ProjectileSession(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
        {
            (this.remote, this.stream, this.ciphers, Conn, epoch) = (remote, stream, ciphers, conn, conn.ProjectileEpoch);
            Runtime = new(this);
            OutputCompleted = RunOutput();
        }

        public void Start() => _ = Pump();
        async Task Pump()
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1d / 30));
                while (Active && await timer.WaitForNextTickAsync())
                    Advance();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[gateway] {remote} projectile pump failed: {ex}");
                Conn.Closed = true;
            }
            finally { Invalidate(); }
        }

        public void Advance()
        {
            // Match the existing XP -> connection lock order. Kill reactions may gain XP synchronously.
            lock (LevelUpdatesFor(Conn).Gate)
            lock (Conn.Monsters)
            {
                if (!Active) { Invalidate(); return; }
                if (!Conn.Paused) Runtime.AdvanceTo(Math.Max(Runtime.Now, ProjectileRuntime.Time(Conn.Clock.Elapsed.TotalSeconds)));
            }
        }

        public void AdvanceTo(long at)
        {
            lock (LevelUpdatesFor(Conn).Gate)
            lock (Conn.Monsters)
            {
                if (!Active) { Invalidate(); return; }
                Runtime.AdvanceTo(at);
            }
        }

        public void Schedule(long at, Func<bool> valid, Func<ProjectileUse> release)
        {
            lock (Conn.Monsters)
                if (Active)
                    Runtime.Schedule(Math.Max(Runtime.Now, at), () =>
                    {
                        if (!Active || !valid()) return;
                        try
                        {
                            var use = release() with { Seed = ++seed };
                            long useId = Runtime.Release(use);
                            Console.WriteLine($"[gateway] {remote} projectile release use={useId} "
                                + $"skill={PayloadLabel(use.Payload)} metadata={use.Profile.Id} count={use.Profile.Count} "
                                + $"origin={use.Origin} aim={use.Aim} speed={use.Profile.Speed:0.###}");
                        }
                        catch (InvalidOperationException ex)
                        { Console.WriteLine($"[gateway] {remote} projectile path unavailable: {ex.Message}"); }
                    });
        }

        public void Invalidate()
        {
            lock (Conn.Monsters)
            {
                if (invalid) return;
                invalid = true;
                Runtime.Clear();
                foreach (var visual in visuals.Values) Conn.ProjectileObjects.Remove(visual.Id);
                visuals.Clear(); output.Writer.TryComplete();
            }
        }

        internal void Enqueue(Func<Task> intent)
        {
            lock (Conn.Monsters)
            {
                if (!Active || output.Writer.TryWrite(intent)) return;
                // Never stall gameplay or silently lose ordered lifecycle/inventory packets on a slow socket.
                Console.WriteLine($"[gateway] {remote} projectile output backlog exceeded 8192 intents; closing connection");
                Conn.Closed = true; Invalidate(); stream.Close();
            }
        }
        internal void Packet(byte[] bytes, string? log = null) => Packet(() => bytes, log);
        void Packet(Func<byte[]?> snapshot, string? log = null) => Enqueue(async () =>
        {
            while (Active)
            {
                bool completed = false;
                await Send(remote, stream, ciphers, [], log, atGate: () =>
                {
                    lock (Conn.Monsters)
                    {
                        if (!Active || Conn.Paused) return null;
                        completed = true;
                        return snapshot();
                    }
                });
                if (completed) return;
                await Task.Delay(16);
            }
        });

        async Task RunOutput()
        {
            try
            {
                await foreach (var intent in output.Reader.ReadAllAsync())
                    if (Active)
                    {
                        ProjectileOutputGuard.Value = () => Active;
                        try { await intent(); }
                        finally { ProjectileOutputGuard.Value = null; }
                    }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            { Conn.Closed = true; Invalidate(); }
            catch (Exception ex)
            {
                Console.WriteLine($"[gateway] {remote} projectile output failed: {ex}");
                Conn.Closed = true; Invalidate();
            }
        }

        ProjectileActorKey Key(uint id, SpawnedMonster monster)
        {
            if (!generations.TryGetValue(monster, out int value)) generations[monster] = value = ++generation;
            return new(id, value);
        }

        /// <summary>The actor key a monster has in this session's world: a monster's own shot names it as owner, so its
        /// flight never contacts the monster itself.</summary>
        internal ProjectileActorKey ActorKey(uint id, SpawnedMonster monster) => Key(id, monster);

        public IReadOnlyList<ProjectileActor> Actors(long from, long until)
        {
            var result = new List<ProjectileActor>();
            foreach (var (id, monster) in Conn.Monsters)
            {
                if (monster.Dead || monster.Life == 0) continue;
                var walk = monster.Walk;
                result.Add(new(Key(id, monster), monster.Hostile ? 2 : 1,
                    t => Vector(walk.Position(t / (double)ProjectileRuntime.QuantaPerSecond)), 0));
            }
            if (Conn.PlayerLife > 0)
                result.Add(new(new(PlayerId, epoch), 1,
                    t => Vector(Conn.Walk.Position(t / (double)ProjectileRuntime.QuantaPerSecond)), 0));
            return result;
        }

        public TerrainContact? TraceTerrain(ProjectileVector from, ProjectileVector to) =>
            ProjectileGeometry.TraceGrid(from, to, Wall);

        bool Wall(int x, int y) => x < 0 || y < 0 || Conn.Layout.GridW > 0 && x >= Conn.Layout.GridW
            || Conn.Layout.GridH > 0 && y >= Conn.Layout.GridH
            || ProjectileGrid.Blocks(Conn.Layout.Id, Conn.Seed, x, y) == true;

        int? Height(int x, int y) => TerrainHeights.At(Conn.Layout.Id, Conn.Seed, x, y);

        // A player's primary projectile flies at the ground its aim gives it (ProjectileAimHeight); returns,
        // continuation children and monster shots keep the plain projectile grid.
        int? FlightGround(ProjectileEntity p) => p.ParentId != 0 || p.Phase > 0 || p.Use.Team != 1 ? null
            : ProjectileAimHeight.Ground(p.Use.Origin, p.Use.Aim, Height);

        public TerrainContact? TraceFlight(ProjectileEntity p, ProjectileVector from, ProjectileVector to) =>
            FlightGround(p) is int ground
                ? ProjectileGeometry.TraceGrid(from, to, (x, y) => Wall(x, y) || ProjectileAimHeight.Blocks(x, y, ground, Height))
                : TraceTerrain(from, to);

        public ProjectileHitResult Hit(ProjectileDamage contact)
        {
            if (!Active) return ProjectileHitResult.Miss;
            // A monster's shot: the player is the target and the monster's damage pipeline commits the hit.
            if (contact.Projectile.Use.Payload is HostileProjectileOffense hostile) return HostileHit(this, contact, hostile);
            if (contact.Projectile.Use.Payload is not ProjectileOffense shot) return ProjectileHitResult.Miss;
            if (!Conn.Monsters.TryGetValue(contact.Target.Id, out var target) || target.Dead || target.Life == 0
                || Key(contact.Target.Id, target) != contact.Target) return ProjectileHitResult.Miss;
            var random = contact.Projectile.RandomSource;
            double at = contact.At / (double)ProjectileRuntime.QuantaPerSecond;
            int hitChance = 100;
            if (!shot.Spell)
            {
                var accuracy = shot.AccuracyProfile ?? new(shot.Accuracy, shot.AccuracyMoreUnique, shot.AccuracyMoreClose, shot.AlwaysHits);
                hitChance = AttackHitChanceAt(Conn, contact.Target.Id, target, at, accuracy);
                if (!accuracy.CannotBeEvaded && !target.AttackEvasion.Hits(hitChance, at, Conn.AttackAccuracyRandom))
                    return ProjectileHitResult.Miss;
            }
            if (MonsterBlocks(target, random, shot.Spell)) return ProjectileHitResult.Blocked;
            double more = contact.Multiplier * (contact.Phase > 0 ? shot.ReturnMultiplier : 1);
            if (!shot.Spell && shot.FarShot) more *= AntiquarianEffects.FarShot(contact.Projectile.Distance);
            var baseHit = contact.IsArea ? shot.AreaHit ?? shot.Hit : shot.Hit;
            var player = CharacterPlayerStats(Conn);
            var packet = new SkillDamageContext(shot.Spell ? SkillHitKind.Spell : SkillHitKind.Attack, player)
                .Hit(baseHit).Scale(more);
            var gambler = GamblerFor(player);
            var rolled = WithDamageVsRarity(player, target, (gambler with { DamageMorePct = 0 }).RollOutgoingHit(packet, random));
            int chance = contact.Projectile.Form > 0 && shot.SecondFormCriticalChance is int second ? second
                : CriticalChanceOn(Conn, contact.Target.Id, shot.CriticalChance, shot.VsBlindedCriticalChance);
            // Base chance/multiplier belong to release; conditional owner state is read at this contact.
            bool critical = RollCriticalStrike(Conn, chance, shot.SkillStats, attack: !shot.Spell, skillRoll: shot.CriticalRoll)
                && (shot.Spell || EvasionEntropy.ConfirmsCritical(hitChance, Conn.AttackAccuracyRandom));
            int multiplier = CriticalMultiplierOn(target, shot.CriticalMultiplier);
            var (damage, _, mitigated) = MonsterHitDamage(target, rolled, critical, !shot.Spell, at, shot.ResistanceMods,
                shot.Penetration, gambler.InvertsResistance(random));
            if (critical) damage = DamageModel.CriticalDamage(damage, multiplier);
            LeechOnHitLocked(Conn, target, MonsterTakenComponents(target, mitigated, shot.Spell), damage, shot.Leech);
            var settings = (critical ? shot.CriticalImpaleSettings : null) ?? shot.ImpaleSettings ?? new(shot.ImpaleChance);
            double physical = rolled[DamageType.Physical].Min * (critical ? Math.Max(0, shot.CriticalMultiplier) / 100d : 1);
            var (stored, _) = target.Impale.OnHit(physical, at, settings, random, target.Mods.Effects["avoid_impale_%"]);
            damage = ImpaleStats.AddReflected(damage, MonsterImpaleDamage(target, stored));
            var hit = ApplyMonsterHitLocked(Conn, target, damage);
            if (hit is null) return ProjectileHitResult.Miss;
            AilmentsOnHitLocked(Conn, target, mitigated, shot.Spell, critical, multiplier, player, shot.SkillStats,
                at, contact.Projectile.ReactionRandomSource);
            if (hit.Value.Life > 0 && rolled[DamageType.Fire].Max > 0) IgniteOnHitLocked(this, contact.Target.Id, target, shot.Ignite, critical, at);
            RageOnHitLocked(Conn, attack: !shot.Spell, melee: false, critical, shot.SkillStats,
                contact.Projectile.ReactionRandomSource);
            ChargesOnHit(Conn, target, critical, shot.Spell, shot.Effect, shot.Level, shot.Quality, melee: false);
            CommitProjectileHit(this, contact, target, hit.Value, damage, shot, critical);
            return ProjectileHitResult.Hit;
        }

        public void Publish(ProjectileEvent value)
        {
            var p = value.Projectile;
            int x = (int)Math.Round(value.Position.X), y = (int)Math.Round(value.Position.Y);
            var presentation = PresentationFor(p);
            if (value.Kind == ProjectileEventKind.End && p.ParentId == 0)
                Console.WriteLine($"[gateway] {remote} projectile end use={p.UseId} entity={p.Id}"
                    + $" reason={value.Reason} position={value.Position} metadata={p.Profile.Id}"
                    + $" grid={Conn.Layout.Id}_seed{Conn.Seed} blocked={ProjectileGrid.Blocks(Conn.Layout.Id, Conn.Seed, x, y)?.ToString() ?? "unknown"}");
            if (presentation.EventEffect?.Invoke(value) is { } eventEffect)
                Effect(x, y, eventEffect, presentation.EffectLifetime,
                    value.Kind == ProjectileEventKind.Area ? value.Radius : null);
            // The client draws its own primary projectiles of a skill use; the server still simulates them (contacts,
            // areas and continuation children, which do get objects).
            if (value.Kind == ProjectileEventKind.Spawn && p.ParentId == 0
                && p.Use.Payload is ProjectileOffense { ClientPredicted: true }) return;
            if (value.Kind == ProjectileEventKind.Spawn)
            {
                string? flightVisual = (p.Use.Payload as ProjectileOffense)?.FlightVisual ?? presentation.FlightVisual;
                uint id = Conn.NextObjectId++;
                // A shot aimed at higher ground flies at that ground's height, so its object clears the cliff it passes over.
                var height = ProjectileHeight.Flight(FlightGround(p) ?? TerrainHeights.At(Conn.Layout.Id, Conn.Seed, x, y),
                    Environment.GetEnvironmentVariable("Game_PROJECTILE_HEIGHT"), presentation.LaunchHeight);
                var visual = new Visual(id, height) { SpawnedAt = value.Position };
                visuals[p.Id] = visual; Conn.ProjectileObjects.Add(id);
                if (p.Profile.Lobbed) BeginArc(p, visual);
                Packet(() =>
                {
                    // A flight that ended before this snapshot is created only when its stop keeps the object (the impact
                    // clip or stuck arrow then shows where it hit); otherwise a create followed by its removal is a flash.
                    if (visual.Ended && !visual.StaysAfterEnd) return null;
                    // Created where it was released, not where the simulation has it when this snapshot is taken: an arrow
                    // covers a cell every 3 ms, so the late snapshot put it one to ten cells past the bow (live 2026-10-29).
                    // The first glide starts here too; the visual trails the simulation by the output delay and catches
                    // up at its stop.
                    var at = visual.SpawnedAt;
                    var obj = new ProjectileObject(p.Profile.Id, (int)Math.Round(at.X), (int)Math.Round(at.Y),
                        (float)Math.Atan2(p.Direction.X, -p.Direction.Y),
                        Team: p.Use.Team == 1 ? (ushort)0 : (ushort)1,
                        Height: (visual.Arc is { } arc ? arc.Height(arc.Fraction(p.Distance)) : visual.Height).ObjectHeight)
                        { AnimatedObject = flightVisual };
                    visual.Created = true;
                    if (p.ParentId == 0)
                        Console.WriteLine($"[gateway] {remote} projectile visual use={p.UseId} entity={p.Id} object={id}"
                            + $" metadata={p.Profile.Id} created={at} simulated={p.Position} direction={p.Direction}"
                            + $" lagMs={(Runtime.Now - p.CreatedAt) / 120d:0.###} height={(obj.Height is int h ? h.ToString() : "terrain")}");
                    return InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(p.Profile.Id), obj.Blob(), headerU32: 0xffffffff);
                });
                Packet(() => visual.Created ? InstanceProtocol.EnterWakeZone(id) : null);
                if (presentation.FlightClip is { } clip)
                    Packet(() => visual.Created && !visual.Ended ? InstanceProtocol.AnimationUpdate(id, 0, clip, presentation.FlightClipSpeed) : null);
                UpdateForm(p, visual);
                Glide(p, visual);
            }
            else if (value.Kind == ProjectileEventKind.Redirect && visuals.TryGetValue(p.Id, out var redirect))
            {
                if (p.Profile.Lobbed) BeginArc(p, redirect);
                UpdateForm(p, redirect);
                Glide(p, redirect);
            }
            else if (value.Kind == ProjectileEventKind.End && visuals.TryGetValue(p.Id, out var end) && !end.Ended)
            {
                end.Ended = true;
                // A lobbed shot's landing is its impact too (the mortar's "end" clip plays where it comes down).
                end.StaysAfterEnd = (presentation.ImpactClip is not null || presentation.StayOnImpact)
                    && value.Reason is ProjectileEndReason.Enemy or ProjectileEndReason.Terrain or ProjectileEndReason.Landing;
                var height = end.Arc is { } arc ? arc.Height(arc.Fraction(p.Distance)) : end.Height;
                if (end.StaysAfterEnd)
                {
                    // The stop ends the glide and the object stays for ImpactLifetime of game time: for the rig's own impact
                    // clip (an ordinary wand bolt, with its particles and sound), or as the arrow left in its target or a
                    // wall. Its removal follows on the same pause- and transfer-aware game clock as an effect's lifetime.
                    Packet(() => !end.Created ? null
                        : presentation.ImpactClip is { } impactClip
                            ? [.. height.StopAt(end.Id, x, y), .. InstanceProtocol.AnimationUpdate(end.Id, 0, impactClip, 1)]
                            : height.StopAt(end.Id, x, y));
                    Runtime.Schedule(Runtime.Now + presentation.ImpactLifetime, () =>
                    {
                        visuals.Remove(p.Id);
                        Packet(() =>
                        {
                            Conn.ProjectileObjects.Remove(end.Id);
                            return end.Created ? InstanceProtocol.RemoveObject(end.Id) : null;
                        });
                    });
                }
                else
                {
                    visuals.Remove(p.Id);
                    Packet(() =>
                    {
                        Conn.ProjectileObjects.Remove(end.Id);
                        return end.Created ? [.. height.StopAt(end.Id, x, y), .. InstanceProtocol.RemoveObject(end.Id)] : null;
                    });
                }
            }
            else if (value.Kind == ProjectileEventKind.Area && p.Use.Payload is ProjectileOffense { AreaVisual: { } ao })
                Effect(x, y, ao, presentation.EffectLifetime, value.Radius);
        }

        void Effect(int x, int y, string ao, long lifetime, double? radius = null)
        {
            uint id = Conn.NextObjectId++;
            // Native area art radii are world units; simulation radii are grid cells.
            float scale = radius is double r && Art.ArtRadius(ao) is int nativeRadius
                ? (float)(r * (250d / 23) / nativeRadius) : 1;
            var effect = new EffectObject(x, y, ao) { Scale = scale };
            if (radius is not null)
                Console.WriteLine($"[gateway] {remote} projectile area object={id} position=({x}, {y})"
                    + $" radius={radius:0.###} art={ao} scale={scale:0.###}");
            var visual = new Visual(id, default);
            Conn.ProjectileObjects.Add(id);
            Packet(() =>
            {
                if (visual.Ended) return null;
                visual.Created = true;
                return InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(EffectObject.TypePath), effect.Blob());
            });
            Packet(() => visual.Created ? InstanceProtocol.EnterWakeZone(id) : null);
            Runtime.Schedule(Runtime.Now + lifetime, () =>
            {
                visual.Ended = true;
                Packet(() =>
                {
                    Conn.ProjectileObjects.Remove(id);
                    return visual.Created ? InstanceProtocol.RemoveObject(id) : null;
                });
            });
        }

        void UpdateForm(ProjectileEntity p, Visual visual)
        {
            if (PresentationFor(p).SecondFormClip is not { } clip) return;
            Packet(() =>
            {
                if (visual.Ended || !visual.Created || p.Form == 0 || visual.SecondFormStarted) return null;
                // Continuation children may already be in their second form at birth.
                visual.SecondFormStarted = true;
                return InstanceProtocol.AnimationUpdate(visual.Id, 0, clip, 1);
            });
        }

        void BeginArc(ProjectileEntity p, Visual visual)
        {
            var end = p.Position + p.Direction * p.Remaining;
            int? Ground(ProjectileVector at) => TerrainHeights.At(Conn.Layout.Id, Conn.Seed,
                (int)Math.Round(at.X), (int)Math.Round(at.Y));
            if (Ground(p.Position) is not int startGround || Ground(end) is not int endGround)
            {
                // A guessed absolute parabola can run entirely underground. Let the client sample the ground
                // until both endpoints are known; XY landing/damage and bounce chronology remain authoritative.
                visual.Arc = null;
                visual.Height = ProjectileHeight.Terrain;
                return;
            }
            int start = p.GroundBounces == 0
                ? ProjectileHeight.Flight(startGround, Environment.GetEnvironmentVariable("Game_PROJECTILE_HEIGHT"),
                    PresentationFor(p).LaunchHeight).Marker
                : startGround;
            var arc = new ProjectileArc(p.Distance, p.Remaining, start, endGround);
            visual.Arc = arc;
            for (int segment = 1; segment < ProjectileArc.Segments; segment++)
            {
                double at = Runtime.Now + Math.Ceiling(arc.Distance * segment / ProjectileArc.Segments / p.Speed * ProjectileRuntime.QuantaPerSecond);
                if (at <= Runtime.Now || at >= p.ExpiresAt) continue;
                Runtime.Schedule((long)at, () =>
                {
                    if (!visual.Ended && visual.Arc == arc) Glide(p, visual);
                });
            }
        }

        void Glide(ProjectileEntity p, Visual visual)
        {
            int version = ++visual.MotionVersion;
            Packet(() =>
            {
                if (visual.Ended || !visual.Created || version != visual.MotionVersion) return null;
                var end = p.Position + p.Direction * p.Remaining;
                var startHeight = visual.Height;
                var endHeight = visual.Height;
                if (visual.Arc is { } arc)
                {
                    double fraction = arc.Fraction(p.Distance);
                    double next = Math.Min(1, (Math.Floor(fraction * ProjectileArc.Segments + .000001) + 1) / ProjectileArc.Segments);
                    end = p.Position + p.Direction * Math.Min(p.Remaining, (next - fraction) * arc.Distance);
                    startHeight = arc.Height(fraction);
                }
                if (!(p.Phase > 0 && p.Profile.IgnoreTerrainOnReturn) && TraceTerrain(p.Position, end) is { } wall)
                    end = p.Position + (end - p.Position) * wall.Fraction;
                if (visual.Arc is { } path) endHeight = path.Height(path.Fraction(p.Distance + (end - p.Position).Length));
                // The first move starts where the object was created (the release point), without a stop there and with
                // the whole way's duration, so it flies at the projectile's speed. Later moves (arc segments, redirects)
                // first stop at the simulated position.
                bool first = !visual.Glided;
                visual.Glided = true;
                var start = first ? visual.SpawnedAt : p.Position;
                float duration = (float)Math.Max(.001, (end - start).Length / p.Speed);
                byte[] move = endHeight.MoveTo(visual.Id, (int)Math.Round(end.X), (int)Math.Round(end.Y), duration);
                return first ? move
                    : [.. startHeight.StopAt(visual.Id, (int)Math.Round(p.Position.X), (int)Math.Round(p.Position.Y)), .. move];
            });
        }
        internal static ProjectileVector Vector((int X, int Y) p) => new(p.X, p.Y);
        static string? PayloadLabel(object? payload) => payload switch
        {
            ProjectileOffense offense => offense.Label,
            HostileProjectileOffense hostile => hostile.Label,
            _ => null,
        };
    }

    static ProjectileSession ProjectilesFor(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        lock (conn.Monsters)
        {
            if (conn.ProjectileRuntime is { Active: true } current) return current;
            var session = new ProjectileSession(remote, stream, ciphers, conn);
            conn.ProjectileRuntime = session; session.Start();
            return session;
        }
    }

    static void AdvanceProjectiles(ClientState conn) => conn.ProjectileRuntime?.Advance();

    // Advance and replace actor motion/life in the same critical section. A flush followed by a separate
    // Monsters lock leaves a race in which the pump can consume the replacement as historical movement.
    internal static IDisposable ProjectileMutation(ClientState conn, bool advance = true, string? phase = null) => new ProjectileMutationScope(conn, advance, phase);
    internal static double ProjectileWorldTime(ClientState conn) => conn.ProjectileRuntime is { Active: true } session
        ? session.Runtime.Now / (double)ProjectileRuntime.QuantaPerSecond : conn.Clock.Elapsed.TotalSeconds;

    sealed class ProjectileMutationScope : IDisposable
    {
        readonly System.Threading.Lock experience;
        readonly ConnectionPerformance.LockScope actorLock;
        public ProjectileMutationScope(ClientState conn, bool advance, string? phase)
        {
            experience = LevelUpdatesFor(conn).Gate;
            experience.Enter();
            try { actorLock = conn.Performance.Lock(conn.Monsters, phase ?? "projectile"); }
            catch { experience.Exit(); throw; }
            try
            {
                if (advance && !conn.Paused && conn.ProjectileRuntime is { Active: true } session)
                    session.AdvanceTo(Math.Max(session.Runtime.Now, ProjectileRuntime.Time(conn.Clock.Elapsed.TotalSeconds)));
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { try { actorLock.Dispose(); } finally { experience.Exit(); } }
    }

}
