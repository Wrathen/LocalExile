using System.Net.Sockets;
using GameServer.Core.Projectiles;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;

partial class Program
{
    internal static bool UsesProjectileRuntime(int effect) => Skills.IsProjectile(effect)
        || ProjectileDefinition(Skills.EffectId(effect) ?? "") is not null;

    // Validate adopted paths before accepting an action, spending mana or reserving a cooldown.
    // Release still resolves again: equipment/stats may change during the ordinary cancellable windup.
    internal static bool ProjectilePathAvailable(string remote, ClientState conn, ushort skillId, int effect)
    {
        if (!UsesProjectileRuntime(effect)) return true;
        try
        {
            var definition = ResolveProjectileDefinition(effect)
                ?? throw new InvalidOperationException("no available projectile recipe/metadata");
            var gem = GemSkillEffects(conn).FirstOrDefault(g => g.Id == skillId);
            if (gem.Name is null || !SkillHitPlan.ForSkill(SkillsFor(conn), effect, gem.Level, gem.Quality).CanHit)
                throw new InvalidOperationException("no supported projectile damage plan");
            var resolved = MergeSkillStats(CharacterPlayerStats(conn), SupportedSkill(conn, skillId).Stats);
            int Get(string name) => Stats.Key(name) is uint key ? resolved.GetValueOrDefault(key) : 0;
            _ = definition.Parameters.Resolve(Get, 1);
            var at = conn.Walk.Position(conn.Clock.Elapsed.TotalSeconds);
            if (definition.RequiresTerrain && ProjectileGrid.Blocks(conn.Layout.Id, conn.Seed, at.X, at.Y) is null)
                throw new InvalidOperationException("Projectile recipe requires a projectile terrain mask");
            return true;
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"[gateway] {remote} {SkillsFor(conn).EffectId(effect)} unavailable: {ex.Message}");
            return false;
        }
    }

    internal static Task ScheduleGemProjectile(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        ushort skillId, (ushort Id, int Effect, string Name, int Level, int Quality, uint Gem) gem,
        (int X, int Y) aim, int action, int recoveryGeneration, CooldownUseTicket? ticket, SkillCriticalRoll criticalRoll)
    {
        var session = ProjectilesFor(remote, stream, ciphers, conn);
        session.Advance();
        bool attack = SkillsFor(conn).IsAttack(gem.Effect);
        bool cancelOnNewAction = !attack || ResolveProjectileDefinition(gem.Effect)?.CancelWindupOnNewAction == true;
        int? cast = attack ? 0 : CastMs(conn, gem.Effect, skillId);
        if (cast is null) return Task.CompletedTask;
        double acceptedAt = conn.Clock.Elapsed.TotalSeconds;
        double delay = attack ? AttackContactDelay(conn, SupportedAttackSeconds(conn, skillId), false)
            : cast.Value / 1000d * Animations.SpellRelease(SkillsFor(conn).ClassFor(conn.ClassRow).AnimatedObject);
        int epoch = conn.ProjectileEpoch;
        session.Schedule(ProjectileRuntime.Time(acceptedAt + delay),
            // Paid attacks survive Move unless their recipe opts into cancellable windup (e.g. release-time costs).
            // Spells and cooldown tickets retain action cancellation.
            // Epoch, death and hit recovery still cancel unreleased attacks.
            () => conn.ProjectileActive(epoch) && conn.ActionInterruptionGeneration == recoveryGeneration
                && (ticket?.Current(conn) ?? (cancelOnNewAction
                ? PlayerActionCurrent(conn, action)
                : conn.PlayerLife > 0 && conn.ElementalAilments.ActionFactor(conn.Clock.Elapsed.TotalSeconds) > 0 && !conn.HitRecovery.Active(conn.Clock.Elapsed.TotalSeconds)))
                && GemSkillInstanceDisabled(conn, gem.Effect, skillId) is null,
            () => CaptureGemProjectile(conn, session, skillId, gem, aim, criticalRoll));
        return Task.CompletedTask;
    }

    internal static ProjectileUse CaptureGemProjectile(ClientState conn, ProjectileSession session, ushort skillId,
        (ushort Id, int Effect, string Name, int Level, int Quality, uint Gem) gem, (int X, int Y) aim, SkillCriticalRoll criticalRoll)
    {
        var player = CharacterPlayerStats(conn);
        var own = SupportedSkill(conn, skillId).Stats;
        var merged = MergeSkillStats(player, own);
        int Get(string key) => Stats.Key(key) is uint k ? merged.GetValueOrDefault(k) : 0;
        int Player(string key) => Stats.Key(key) is uint k ? player.GetValueOrDefault(k) : 0;
        int Own(string key) => Stats.Key(key) is uint k ? own.GetValueOrDefault(k) : 0;
        var definition = ResolveProjectileDefinition(gem.Effect)
            ?? throw new InvalidOperationException("No projectile definition at release.");
        string metadata = definition.Metadata;
        int baseSpeed = SkillsFor(conn).Projectile(metadata)?.Speed ?? 1000;
        double speed = ProjectileSpeed.CellsPerSecond(baseSpeed, Player, Own, Environment.GetEnvironmentVariable("Game_PROJECTILE_SPEED"));
        if (!double.IsFinite(speed) || speed <= 0)
            throw new InvalidOperationException("Projectile release requires a finite positive speed.");
        var profile = definition.Parameters.Resolve(Get, speed);
        var at = conn.Walk.Position(session.Runtime.Now / (double)ProjectileRuntime.QuantaPerSecond);
        var origin = ProjectileSession.Vector(at);
        if (definition.RequiresTerrain && ProjectileGrid.Blocks(conn.Layout.Id, conn.Seed, at.X, at.Y) is null)
            throw new InvalidOperationException("Projectile recipe requires a projectile terrain mask");
        var target = ProjectileSession.Vector(aim);
        var direction = (target - origin).Unit;
        bool spell = !SkillsFor(conn).IsAttack(gem.Effect);
        // An attack gem leaves from its weapon's contact keyframe, as the default attack's shot does: puncture_bow (Burning
        // Arrow's clip) has attack1_bow's contact and position. Released at the feet 1.5 cells ahead, the arrow showed up
        // inside the body at the ground instead of at the bow.
        var release = spell ? null : Animations.AttackContactPoint(
            Environment.GetEnvironmentVariable("Game_PLAYER_AO") ?? SkillsFor(conn).ClassFor(conn.ClassRow).AnimatedObject,
            WeaponProfileFor(conn, activeSet: true));
        double ahead = release is { ForwardCells: > 0 } point ? point.ForwardCells : 1.5;
        // A point closer than the muzzle must not end up behind the released projectile.
        origin += direction * Math.Min(ahead, (target - origin).Length / 2);
        int variation = SkillArtVariation(conn, gem.Gem);
        profile = profile with { Id = Art.Projectile(profile.Id, variation) };
        var local = spell ? null : WeaponFor(conn).LocalSources;
        var shot = new ProjectileOffense(SupportedProjectileHit(conn, skillId, area: false), spell, conn.Pools.Accuracy,
            SupportedReleaseCriticalChance(conn, skillId), SupportedCriticalMultiplier(conn, skillId), gem.Name)
        {
            // Child stages and release specialization can introduce areas after this root is resolved.
            AreaHit = SupportedProjectileHit(conn, skillId, area: true),
            Effect = gem.Effect, Level = gem.Level, Quality = gem.Quality,
            SkillStats = new Dictionary<uint, int>(own),
            Penetration = Penetration.Of(player, Stats, !spell, own, local), ResistanceMods = HitResistMods(player, own),
            ImpaleSettings = ImpaleStats.From(player, Stats, !spell, own, local),
            CriticalImpaleSettings = ImpaleStats.From(player, Stats, !spell, own, local, critical: true),
            AccuracyProfile = spell ? null : SupportedAccuracy(conn, skillId), CriticalRoll = criticalRoll,
            Leech = HitLeechSources(player, !spell, own, local),
            ImpaleChance = spell ? 0 : Get("impale_on_hit_%_chance"),
            // Burning Arrow's 25% base_chance_to_ignite_% and the caster's sources, with the panel's ignite range.
            Ignite = MonsterIgnites.Settings.From(SupportedAilmentRows(conn, skillId, spell ? "spell" : "main_hand"),
                spell ? "spell" : "main_hand", Get),
            AccuracyMoreUnique = conn.Pools.AccuracyMoreVsUniquePct,
            AccuracyMoreClose = conn.Pools.AccuracyMoreInCloseRangePct,
            AlwaysHits = Get("global_always_hit") != 0, FarShot = Get("player_far_shot") != 0,
            ReturnMultiplier = ProjectileReturn.DamageMore(Get),
            // A spell's action spawns its projectiles itself, every one of a volley, from where it stands (Frida
            // 2026-10-29, 0x141ebdf30 from the action at 0x141e938c0): a server object for them was a second fireball.
            // An attack gem's does not: Burning Arrow's action 0x415 started and created no projectile until the server's
            // 0x2b9, as the default attack's arrow (Frida 2026-10-31).
            ClientPredicted = spell,
            // Only where the skill's recipe draws nothing of its own (PresentationFor prefers this one).
            Presentation = release is { } launch && definition.Presentation is null ? new() { LaunchHeight = launch.HeightSteps } : null,
        };
        var use = new ProjectileUse(profile, new(PlayerId, conn.ProjectileEpoch), new(PlayerId, conn.ProjectileEpoch), 1, origin, target)
            { Payload = shot, ReturnAnchor = new(PlayerId, conn.ProjectileEpoch) };
        var context = new ProjectileReleaseContext(conn, session, skillId, gem, variation, Get, Player, Own);
        return definition.SpecializeRelease?.Invoke(context, use) ?? use;
    }

}
