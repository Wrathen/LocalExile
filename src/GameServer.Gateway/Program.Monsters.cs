using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.Diagnostics;
using DamageModel = GameServer.Core.World.DamageModel;
using DamageType = GameServer.Core.World.DamageType;
using DamagePacket = GameServer.Core.World.DamagePacket;

partial class Program
{
    // Monster basic Melee skills are weapon-polymorphic, just like the player's default attack.
    internal static bool MonsterAttackIsRanged(MonsterCatalog.Variety variety) =>
        variety.GrantedEffects is [int first, ..] && (Skills.IsRangedAttack(first)
            || variety.RangedWeapon && Skills.IsAttack(first));

    // A projectile skill's own stats go to the client with the attack's numbers: the client derives none itself
    // (11-skills.md §4), and without them a Sand Spitter's action faces its target like a melee swing instead of turning
    // its back to spit (`action_should_face_away_from_target`, with `projectile_uses_contact_position`, the spread and
    // minimum range from the stat set). The attack's numbers win on a shared key; the effect's cast-time and mana-cost
    // entries, which an attack action does not use, stay out.
    static readonly string[] MonsterSkillStatsNotSent =
        ["base_spell_cast_time_ms", "virtual_base_spell_cast_time_ms", "hundred_times_casts_per_second", "base_mana_cost", "mana_cost"];

    internal static IReadOnlyList<(uint, int)> MonsterProjectileSkillStats(ClientState conn, int effect, int level, MonsterAttack attack)
    {
        var own = attack.SkillStats();
        var taken = own.Select(s => s.Item1).ToHashSet();
        foreach (string name in MonsterSkillStatsNotSent) if (Stats.Key(name) is uint key) taken.Add(key);
        return [.. SkillsFor(conn).EffectStats(effect, level).Where(s => !taken.Contains(s.Item1)), .. own];
    }

    /// <summary>The sequence of a monster's actions (0x1ab); the client's clip selector is seeded with it.</summary>
    internal const ushort MonsterActionSequence = 0xffff;

    // A projectile skill's release, from what the client itself plays: the clip of the variety's ACT pool for the skill's
    // animation key (MonsterMortar.Animation; the Spitter's SpellProjectile = attack1, attack2), chosen with the native
    // sequence-seeded selector, gives the contact keyframe's time, which the client stretches to the attack time, and its
    // position. The skill's stats say whether the shot leaves from that position (projectile_uses_contact_position),
    // whether the offset follows the model's scale (use_scaled_contact_offset, MonsterVarieties ModelSizeMultiplier) and
    // whether the monster turns its back for it (action_should_face_away_from_target). Without rig data: null, the
    // halfway release and the default point.
    internal static MonsterRelease? MonsterReleaseFor(ClientState conn, MonsterCatalog.Variety variety, int effect, int level,
        SkillCatalog.MonsterProjectileInfo projectile)
    {
        var catalog = SkillsFor(conn);
        var stats = catalog.EffectStats(effect, level);
        bool Has(string name) => Stats.Key(name) is uint key && stats.Any(s => s.Item1 == key && s.Item2 != 0);
        var clip = catalog.MonsterContact(variety.Id, projectile.AnimationKey ?? "Melee", MonsterActionSequence);
        if (clip is null) return null;
        var point = Has("projectile_uses_contact_position") ? clip.Point : null;
        if (point is { } p && Has("use_scaled_contact_offset")) point = p.Scaled(variety.ModelScale);
        return new(clip.Animation, clip.ContactFraction ?? 0.5, point, Has("action_should_face_away_from_target"));
    }

    // Creates one monster: registers its type at this level (0x20, once per connection), creates it (0x2b9) and puts it in
    // the wake zone (0x1a9), and keeps it for the AI. /spawn_mob and the area's packs (PopulateArea) use it.
    internal static async Task SpawnMonster(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, MonsterCatalog.Variety variety,
        byte level, MonsterRarity rarity, int x, int y, string? name = null, bool friendly = false, bool log = true, uint? damagedLife = null,
        MonsterModSet? mods = null, bool alone = true, bool? unrisen = null)
    {
        uint id = conn.NextObjectId++;
        int bodySize = Math.Clamp(variety.ObjectSize - 1, -1, 127);
        if (bodySize >= 0)
        {
            bool Floor(int cx, int cy) => conn.Layout.WalkableFor(cx, cy, bodySize) && !conn.Barrier.Contains((cx, cy));
            if (CollisionEscape.Find((x, y), conn.Layout.GridW, conn.Layout.GridH, Floor,
                conn.MonsterBlockers(id), moverSize: bodySize) is { } clear) (x, y) = clear;
        }
        // A monster that rises from the ground starts waiting to rise, unless it was kept after rising (MonsterEmerge.cs).
        var emerge = Emerges.Kind(variety, rarity);
        bool waits = emerge != EmergeKind.None && (unrisen ?? true);
        // Its mods (MonsterMods.cs): the kept ones, else rolled for its rarity. They set its life, hit, speed and defences.
        mods = MonsterModsFor(conn, variety, level, rarity, mods, alone);
        // Full life: the client derives the base from the level (MonsterCatalog.BaseLife); the mods' maximum goes on its Stats.
        int baseLife = Monsters.BaseLife(variety, level) ?? 100;
        uint life = (uint)mods.Effects.MaxLife(baseLife);
        // The type supplies the animated object; Game_MONSTER_AO=1 also sends the variety's first AOFiles path (Animated
        // bit0), which is not needed.
        string? ao = Environment.GetEnvironmentVariable("Game_MONSTER_AO") == "1" ? variety.AnimatedObject : null;
        ushort team = friendly ? (ushort)0 : (ushort)1;
        // Its attack: the variety's first granted effect (Hungry Corpse: Melee) at its level, with the variety's attack time,
        // reach and the level's damage as the skill's final stats (11-skills.md §7).
        // The mods' damage and speed: the rarity's more damage per hit and faster attacks with that much less damage, the
        // affixes' increased damage, attack speed and extra elemental / chaos damage (MonsterModEffects.Hit).
        var (baseMin, baseMax) = Monsters.BaseDamage(variety, level) ?? (1, 1);
        bool rangedAttack = MonsterAttackIsRanged(variety);
        // A mortar skill's projectile (Sand Spitter): the shot flies and lands its hit; other ranged skills keep the
        // instant contact until their projectile is known (SkillCatalog.MonsterProjectile). Its release time and point
        // come from the clip the client plays for it (MonsterReleaseFor).
        var projectile = rangedAttack && variety.GrantedEffects is [int rangedEffect, ..] ? SkillsFor(conn).MonsterProjectile(rangedEffect) : null;
        var attack = new MonsterAttack(SkillCatalog.FirstSkillId, mods.Effects.AttackSeconds(variety.AttackMs / 1000.0), variety.AttackRange,
            mods.Effects.Hit(DamagePacket.Of(DamageType.Physical, baseMin, baseMax)))
        {
            Ranged = rangedAttack,
            Projectile = projectile,
            Release = projectile is null ? null : MonsterReleaseFor(conn, variety, variety.GrantedEffects![0], level, projectile),
        };
        var skill = variety.GrantedEffects is [int effect, ..]
            ? SkillsFor(conn).FindByRow(attack.SkillId, effect, level,
                attack.Projectile is null ? attack.SkillStats() : MonsterProjectileSkillStats(conn, effect, level, attack)) : null;
        // A boss with stages also has the skill that changes them (Hillock's sword extract, BossFights).
        var stage = BossFights.For(variety.Id);
        // Its stats as the attack's (skill_is_non_pathing above all, and the stage's own time): without them the client sent
        // the action through its pathing step, played nothing and kept the monster in it (live 2026-10-21).
        var stageSkill = skill is not null && stage is not null
            ? SkillsFor(conn).FindByRow(StageSkillId, stage.SkillEffect, level, (attack with { SkillId = StageSkillId, Seconds = stage.CastSeconds }).SkillStats())
            : null;
        List<(uint, int)>? stats = skill is null ? null : [.. attack.Stats()];
        // The final maximum too: the client applies no modifier of its own (as for the player's maximum_life), so without it
        // the life bars kept the base maximum and a unique showed full until its life fell below it (live 2026-10-21).
        foreach (var stat in MonsterModClientStats(mods.Effects, life, baseLife)) (stats ??= []).Add(stat);
        var monster = new MonsterObject(x, y, life, variety.ComponentsOrBase, ao, rarity, name, variety.StateMachine, MonsterEmerge.CreateState(variety, emerge, waits), team,
            skill is null ? null : stageSkill is null ? [skill] : [skill, stageSkill], stats)
        {
            Mods = mods.Mods, NameWords = mods.NameWords, LifeData = variety.LifeData,
        };
        // A monster type needs a level: 0x20 registers "<Id>@<level>" (once per connection), the 0x2b9 type is its hash.
        if (conn.MonsterTypes.Add((variety.Row, level)))
            await Send(remote, stream, ciphers, InstanceEntities.MonsterTypes([((ushort)variety.Row, level)]),
                $"0x0020 monster type {variety.Id}@{level} (row {variety.Row})");
        uint type = InstanceEntities.ObjectTypeHash(MonsterObject.TypePath(variety.Id, level));
        // The client multiplies this monster's pathing speed by its own roll in the variety's range, drawn from [id, type]
        // (MonsterSpeedRoll.cs); the walk estimate uses the same speed so arrival, swing range and contact agree with it.
        int speedRoll = MonsterSpeedRoll.Percent(id, type, variety);
        double walkSpeed = mods.Effects.MovementSpeed(variety.MovementSpeed) * MonsterSpeedRoll.Factor(speedRoll);
        // The world is active by now, so 0x2b9 is created at once; 0x1a9 makes it drawn and updated.
        await Send(remote, stream, ciphers, InstanceEntities.CreateObject(id, type, monster.Blob()), !log ? null :
            $"0x02b9 create {rarity}{(friendly ? " friendly" : "")}{(waits ? $" {emerge.ToString().ToLowerInvariant()}" : "")} monster 0x{id:x} {variety}{(name is null ? "" : $" \"{name}\"")} type=0x{type:x8} at ({x}, {y}), life {life}, speed {walkSpeed:0.##}{(speedRoll == 0 ? "" : $" (roll {speedRoll:+0;-0}%)")}, components {string.Join(", ", variety.ComponentsOrBase)}");
        await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(id), !log ? null : $"0x01a9 monster 0x{id:x} enters wake zone");
        if (log && mods.Affixes.Any()) Console.WriteLine($"[gateway] {remote} monster 0x{id:x} mods: {MonsterModRolls.Value.Describe(mods)}" +
            (mods.NameWords.Count > 0 ? $", named {MonsterModRolls.Value.NameOf(mods.NameWords)}" : ""));
        if (log && attack.Projectile is { } shot)
            Console.WriteLine($"[gateway] {remote} monster 0x{id:x} ranged attack projectile {shot.Metadata} ({shot.Speed} units/s{(shot.Lobbed ? ", mortar" : "")})");
        using (ProjectileMutation(conn))
            MonsterEmerge.AddLocked(conn, id, new SpawnedMonster($"{variety.Name}@{level}", conn.NewMonsterWalk(id, x, y, walkSpeed, bodySize, rarity == MonsterRarity.Unique),
                variety.AggroRange, Math.Max(2, variety.ObjectSize + 1), life)
            {
                Attack = skill is null ? null : attack, Hostile = !friendly, Variety = variety, Level = level, Rarity = rarity,
                Life = Math.Min(damagedLife ?? life, life), Stage = stageSkill is null ? null : stage, States = variety.StateMachine,
                HitAnimations = Monsters.TakeHit(variety.Id), Mods = mods, CollisionPosition = (x, y), SpeedRoll = speedRoll,
            }, emerge, waits);
        if (waits) WatchBuried(remote, stream, ciphers, conn);
        // A kept monster that was hit: its life as a hit sets it (0x1b1).
        if (damagedLife < life)
            await Send(remote, stream, ciphers, InstanceProtocol.LifeUpdate(id, damagedLife.Value), !log ? null : $"0x01b1 monster 0x{id:x} life {damagedLife}/{life}");
    }

    // The area's monsters, once the player is in (Agents/docs/protocol/14-monster-packs.md): MonsterPacks scattered over the
    // walk map, the room points' packs and the unique boss at "mapboss" (AreaPopulation). Towns have none; neither does an
    // area without a walk map. Game_MONSTERS=0 turns it off.
    static async Task PopulateArea(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        var layout = conn.Layout;
        if (Environment.GetEnvironmentVariable("Game_MONSTERS") == "0") return;
        // A capture tour only waits for the recorder: no monsters to attack the character meanwhile.
        if (CaptureTour.Active(CharacterName(conn))) return;
        // An instance entered before keeps its monsters as the last connection left them: where they were, with their
        // damage; the dead stay dead (#12).
        if (conn.Instance.Monsters is { } saved)
        {
            conn.MonstersPlaced = true;
            foreach (var s in saved)
                if (Monsters.Find(s.Variety.ToString()) is { } kept)
                    await SpawnMonster(remote, stream, ciphers, conn, kept, s.Level, s.Rarity, s.X, s.Y, friendly: !s.Hostile, log: false, damagedLife: s.Life, mods: s.Mods, unrisen: s.Unrisen);
            Console.WriteLine($"[gateway] {remote} area {layout.Id} monsters: {saved.Count} kept from the instance");
            return;
        }
        // The packs need the walk map; until it exists nothing is placed, so nothing is kept either.
        if (layout.Walkable is not { } walkable) return;
        conn.MonstersPlaced = true;
        if (Packs.For(layout.Id) is not { } area || (area.Scattered.Count == 0 && area.Bosses.Count == 0)) return;
        if (Monsters.Error is { } error) { Console.WriteLine($"[gateway] {remote} area monsters: {error}"); return; }
        // Emerge monsters (Gravel Eater, Tide Strider …) are placed waiting and rise near the player (MonsterEmerge.cs).
        bool Spawnable(int row) => Monsters.Find(row.ToString()) is { Supported: true } v && Emerges.CanPlace(v);
        var notes = new List<string>();
        var points = AreaPopulation.LoadPoints(layout.Id, conn.Seed, notes);
        var spawns = AreaPopulation.Plan(area, layout.GridW, layout.GridH, walkable, (layout.SpawnX, layout.SpawnY),
            [.. layout.AreaTransitions.Select(t => (t.Transition.GridX, t.Transition.GridY))], points,
            HashCode.Combine(layout.Hash, conn.Seed), Spawnable, notes);
        foreach (var n in notes) Console.WriteLine($"[gateway] {remote} area {layout.Id} monsters: {n}");
        byte level = (byte)Math.Clamp(area.AreaLevel, 1, 100);
        foreach (var s in spawns)
        {
            var variety = Monsters.Find(s.Variety.ToString())!;
            bool notable = s.Rarity is MonsterRarity.Unique or MonsterRarity.Rare;
            await SpawnMonster(remote, stream, ciphers, conn, variety, level, s.Rarity, s.X, s.Y, log: notable, alone: false);
        }
        Console.WriteLine($"[gateway] {remote} area {layout.Id} monsters: created {spawns.Count} at level {level}: " +
            string.Join(", ", spawns.GroupBy(s => s.Pack).Select(g => $"{g.Key} × {g.Count()}")) +
            $"; waiting to rise: {MonsterEmerge.Unrisen(conn).Count} ({MonsterEmerge.Buried(conn).Count} buried)" +
            $"; unique: {string.Join(", ", spawns.Where(s => s.Rarity == MonsterRarity.Unique).Select(s => $"{Monsters.Find(s.Variety.ToString())!.Name} at ({s.X}, {s.Y})").DefaultIfEmpty("none"))}");
    }

    internal sealed class MonsterAiPlan
    {
        public readonly List<(uint Id, SpawnedMonster Monster, int Action, (int X, int Y) From, (int X, int Y) To, string Why)> Moves = [];
        public readonly List<(uint Id, SpawnedMonster Monster, int Action)> Swings = [];
        public readonly List<(uint Id, SpawnedMonster Monster, int Action, uint Target, SpawnedMonster Weapon)> WeaponSwings = [];
        public readonly List<(uint Id, SpawnedMonster Monster)> BossBars = [];
    }

    internal static async Task RunMonsterAi(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        var cadence = new MonsterAiCadence();
        try
        {
            while (!conn.Closed && !conn.LeavingArea && conn.PendingTransfer is null)
            {
                await StepMonsterAi(remote, stream, ciphers, conn, cadence);
                int delay;
                lock (conn.Monsters)
                    delay = cadence.DelayMilliseconds(conn.Clock.Elapsed.TotalSeconds);
                await Task.Delay(delay);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The connection closed while sending.
        }
    }

    // Kept separate from socket dispatch so all decisions in a pass use the same game time and player pose.
    internal static MonsterAiPlan PlanMonsterAi(ClientState conn, MonsterAiCadence cadence, double now)
    {
        var plan = new MonsterAiPlan();
        lock (conn.Monsters)
        {
            if (conn.Closed || conn.LeavingArea || conn.PendingTransfer is not null
                || !cadence.TryBeginStep(now, conn.AiEnabled && !conn.Paused && conn.PlayerLife > 0)) return plan;
            using var blockers = conn.BeginMonsterBlockerFrame(now);
            var (px, py) = conn.Walk.Position(now);
            // Entry protection: hostile monsters neither chase nor swing while the player stands protected on the arrival cell.
            bool protectedEntry = InGracePeriod(conn);
            var askers = new List<(MonsterSurround.Asker Asker, SpawnedMonster M, double D)>();
            foreach (var (id, m) in conn.Monsters)
            {
                if (m.Dead || m.Life == 0 || m.OwnerId is not null || m.StageRunning
                    || !MonsterAiCadence.Due(now, m.FlinchUntil)) continue;
                if (m.Hostile && protectedEntry) continue;
                var (mx, my) = m.Walk.Position(now);
                double d = Math.Sqrt((double)(mx - px) * (mx - px) + (double)(my - py) * (my - py));
                if (!m.Aggro && d <= m.AggroRange) { m.Aggro = true; m.LastTarget = null; }
                if (m.Aggro && !m.BossBarShown && m.Variety is { BossHealthBar: true } && m.States?.IndexOf("boss_life_bar") >= 0)
                {
                    m.BossBarShown = true;
                    plan.BossBars.Add((id, m));
                }
                else if (m.Aggro && d > 2 * m.AggroRange)
                {
                    m.Aggro = false;
                    m.SwingGeneration = null;
                    plan.Moves.Add((id, m, ++m.ActionGeneration, m.Walk.Order(mx, my, now), (mx, my), "gives up"));
                    continue;
                }
                if (!m.Aggro || !MonsterAiCadence.Due(now, m.BusyUntil) || cadence.SwingPending(m)) continue;
                if (PlanRampageWeaponTarget(conn, cadence, plan, id, m, now, (mx, my), d)) continue;
                // Attack cadence remains the attack's own duration; faster AI only reduces the delay after expiry.
                // A pending echo/contact keeps its original generation instead of reserving repeated swings at the gate.
                if (m.Hostile && m.Attack is { } a && d <= a.Range)
                {
                    m.BusyUntil = now + a.Seconds;
                    m.LastTarget = null;
                    m.Walk.Order(mx, my, now);
                    m.SwingGeneration = ++m.ActionGeneration;
                    cadence.BeginSwing(m, m.ActionGeneration);
                    plan.Swings.Add((id, m, m.ActionGeneration));
                    continue;
                }
                // Bodies take distinct surround slots. A negative footprint does not collide with living actors,
                // but still chases: retain its direct approach without reserving space in the surround.
                if (LivingCollision.Size(m) < 0)
                {
                    if (d <= m.StopRange) continue;
                    double f = (m.StopRange - 1) / d;
                    (int X, int Y) to = ((int)Math.Round(px + (mx - px) * f), (int)Math.Round(py + (my - py) * f));
                    if (!conn.Layout.WalkableFor(to.X, to.Y, 0) || conn.Barrier.Contains(to)) to = (px, py);
                    if (!cadence.TryChase(m, to, now)) continue;
                    m.LastTarget = to;
                    m.SwingGeneration = null;
                    plan.Moves.Add((id, m, ++m.ActionGeneration, m.Walk.Order(to.X, to.Y, now), to, $"chases (distance {d:F0})"));
                }
                else if (LivingCollision.Blocks(m))
                    askers.Add((new(id, mx, my, LivingCollision.Size(m), m.LastTarget,
                        m.Attack?.Range ?? double.PositiveInfinity, m.Attack?.Ranged == true), m, d));
            }
            if (askers.Count == 0) return plan;
            // Every other living body near the player keeps its place: the chasers' cells go around it.
            var asking = askers.Select(a => a.Asker.Id).ToHashSet();
            var occupied = blockers.Bodies.Where(b => !asking.Contains(b.Id)
                && Math.Max(Math.Abs(b.X - px), Math.Abs(b.Y - py)) < 64).Select(b => (b.X, b.Y, b.Size));
            var fixedBlockers = blockers.FixedBlockers;
            long surroundAt = System.Diagnostics.Stopwatch.GetTimestamp();
            long allocationAt = GC.GetAllocatedBytesForCurrentThread();
            var slots = MonsterSurround.Assign(askers.Select(a => a.Asker), (px, py), occupied,
                (x, y, size) => conn.Layout.WalkableFor(x, y, size) && !conn.Barrier.Contains((x, y))
                    && !GameServer.Core.World.ClientPathfinder.ObjectBlocks(fixedBlockers, x, y, size + 1, size + 4));
            conn.Performance.Elapsed(LatencyPhase.Surround, surroundAt);
            conn.Performance.SurroundAllocation(GC.GetAllocatedBytesForCurrentThread() - allocationAt);
            foreach (var (asker, m, d) in askers)
            {
                // A full surround (every ring taken): aim at the player and queue behind the others, as before.
                (int X, int Y) to = slots.TryGetValue(asker.Id, out var slot) ? slot : (px, py);
                // Standing in its cell already: it waits there for the swing (no order, no new path).
                if ((asker.X, asker.Y) == to) { m.LastTarget = to; continue; }
                // The repath budget and the three-cell change rule apply; the native budget can end a path short of its
                // cell, so the same cell is retried after arrival on the bounded retry cadence.
                if (!cadence.TryChase(m, to, now, retryAfterArrival: now >= m.Walk.ArrivesAt)) continue;
                m.LastTarget = to;
                m.SwingGeneration = null;
                plan.Moves.Add((asker.Id, m, ++m.ActionGeneration, m.Walk.Order(to.X, to.Y, now), to, $"chases (distance {d:F0})"));
            }
        }
        return plan;
    }

    internal static async Task StepMonsterAi(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        MonsterAiCadence cadence)
    {
        MonsterAiPlan plan;
        // Advance the projectile runtime first: decisions and committed projectile contacts share one game time and pose.
        using (ProjectileMutation(conn, phase: "ai")) plan = PlanMonsterAi(conn, cadence, ProjectileWorldTime(conn));
        const ushort Move = 0x2909;
        // A chase is re-ordered every few ticks: its lines only with Game_TRACE=ai (the packet history keeps them).
        foreach (var (id, m, action, from, to, why) in plan.Moves)
            await SendMonsterAction(remote, stream, ciphers, conn, id, m, action,
                () => InstanceProtocol.ActionStarted(id, from.X, from.Y, to.X, to.Y, Move, 0xffff, 0x0408),
                ServerTrace.Ai || why == "gives up" ? $"0x01ab monster 0x{id:x} {why}: move from {from} to {to}" : null);
        foreach (var (id, m) in plan.BossBars)
        {
            lock (conn.Monsters) if (!MonsterPresent(conn, id, m)) continue;
            await ShowBossLifeBar(remote, stream, ciphers, id, m, conn);
        }
        foreach (var (id, m, action) in plan.Swings)
            _ = RunPlannedMonsterSwing(remote, stream, ciphers, conn, cadence, id, m, action);
        foreach (var (id, m, action, target, weapon) in plan.WeaponSwings)
            _ = RunPlannedRampageWeaponSwing(remote, stream, ciphers, conn, cadence, id, m, action, target, weapon);
        conn.Performance.MaybeLog(remote);
    }

    static async Task RunPlannedMonsterSwing(string remote, NetworkStream stream, ConnectionCiphers ciphers,
        ClientState conn, MonsterAiCadence cadence, uint id, SpawnedMonster m, int action)
    {
        try { await MonsterSwing(remote, stream, ciphers, conn, id, m, action); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
        // Anything else is a server bug: the swing was already shown, so name the lost hit instead of faulting the task
        // unobserved (review 2026-10-30).
        catch (Exception ex) { Console.WriteLine($"[gateway] {remote} monster 0x{id:x} swing {action} failed: {ServerLog.Describe(ex)}"); }
        finally { lock (conn.Monsters) cadence.CompleteSwing(m, action); }
    }

    // A monster swings at the player: its skill as an action aimed at the player (0x1ab, flags | 0x80 + target id, sequence
    // 0xffff), then at the contact point (half the attack time for a melee swing; a projectile skill's clip contact,
    // MonsterRelease) the hit lands if the monster still lives and the player is still in reach (walking away dodges
    // it): the player's life goes down by the monster's damage (0x1b1 on the player). At 0 the client runs the player's
    // death.
    // PoE's evasion entropy (wiki: Evasion): shared by every attack against the player, reset after 3.33 s. Call under
    // the Monsters lock.
    static bool EvadesLocked(ClientState conn, int hitChance, double now)
    {
        if (conn.EvasionEntropy < 0 || now - conn.LastEvasionAt > 3.33)
            conn.EvasionEntropy = conn.IncomingHitRandom.Next(100);
        conn.LastEvasionAt = now;
        conn.EvasionEntropy += hitChance;
        if (conn.EvasionEntropy < 100) return true;
        conn.EvasionEntropy -= 100;
        return false;
    }

    // Ordinary AI work belongs to this monster object in this connection. Generations also prevent a flinch that has
    // already ended from reviving an old queued order or contact. Callers may already hold Monsters.
    internal static bool MonsterActionCurrent(ClientState conn, uint id, SpawnedMonster m, int action)
    {
        lock (conn.Monsters)
            return MonsterPresent(conn, id, m) && conn.PlayerLife > 0 && m.Hostile && !m.StageRunning
                && m.ActionGeneration == action && conn.Clock.Elapsed.TotalSeconds >= m.FlinchUntil;
    }

    static bool MonsterPresent(ClientState conn, uint id, SpawnedMonster m) =>
        !conn.Closed && !conn.LeavingArea && !conn.RageRetired && conn.PendingTransfer is null && !m.Dead && m.Life > 0
        && ReferenceEquals(conn.Monsters.GetValueOrDefault(id), m);

    // Keep the stage's final transition, refill and ActionEnded contiguous at the actual send gate. Only then may
    // the planner reserve another action; that action's write must follow this batch through the same gate.
    // Completion waits through a pause, but never writes to a departed area or a replacement monster with the same id.
    internal static async Task CompleteMonsterStage(string remote, NetworkStream stream, ConnectionCiphers ciphers,
        ClientState conn, uint id, SpawnedMonster m)
    {
        while (true)
        {
            lock (conn.Monsters) if (!MonsterPresent(conn, id, m) || !m.StageRunning) return;
            bool completed = false;
            await Send(remote, stream, ciphers, [], null, atGate: () =>
            {
                lock (conn.Monsters)
                {
                    if (!MonsterPresent(conn, id, m) || !m.StageRunning || conn.Paused) return null;
                    var stage = m.Stage!;
                    var bytes = new List<byte>();
                    if (m.Variety?.ComponentsOrBase.ToList().IndexOf("Transitionable") is int t and >= 0)
                        bytes.AddRange(InstanceEntities.TransitionableUpdate(id, (byte)t, stage.TransitionTo));
                    bytes.AddRange(InstanceProtocol.LifeUpdate(id, m.MaxLife));
                    bytes.AddRange(InstanceProtocol.ActionEnded(id));
                    m.Life = m.MaxLife;
                    m.StageRunning = false;
                    completed = true;
                    Console.WriteLine($"[gateway] {remote} => monster 0x{id:x} ({m.Label}) stage complete: " +
                        $"Transitionable {stage.TransitionTo}, life {m.Life}, 0x01ac action ended");
                    return bytes.ToArray();
                }
            });
            if (completed) return;
            await Task.Delay(16);
        }
    }

    // Recheck at the actual encryption/send gate: movement, a swing echo or its contact may have waited behind a hit
    // reaction. Paused work keeps its original generation and resumes only while that generation still exists.
    internal static async Task<bool> SendMonsterAction(string remote, NetworkStream stream, ConnectionCiphers ciphers,
        ClientState conn, uint id, SpawnedMonster m, int action, Func<byte[]?> packet, string? log = null)
    {
        while (MonsterActionCurrent(conn, id, m, action))
        {
            bool checkedAtGate = false, sent = false;
            await Send(remote, stream, ciphers, [], log, atGate: () =>
            {
                lock (conn.Monsters)
                {
                    if (!MonsterActionCurrent(conn, id, m, action) || conn.Paused) return null;
                    checkedAtGate = true;
                    var bytes = packet();
                    sent = bytes is not null;
                    return bytes;
                }
            });
            if (checkedAtGate) return sent;
            await Task.Delay(16);
        }
        return false;
    }

    internal static async Task<bool> WaitMonsterActionTime(ClientState conn, uint id, SpawnedMonster m, int action, double until)
    {
        if (!double.IsFinite(until)) return false;
        while (true)
        {
            lock (conn.Monsters)
            {
                if (!MonsterActionCurrent(conn, id, m, action)) return false;
                if (!conn.Paused && conn.Clock.Elapsed.TotalSeconds >= until) return true;
            }
            await Task.Delay(16);
        }
    }

    // Commit the flinch with native Actor TakeHit recovery, which cancels the current walk/attack and blocks new
    // actions. ActionEnded or a rig-only AnimationUpdate leaves Positioned's movement advancing underneath the clip.
    // If the send gate was busy, recovery starts when sent; a pause does not consume that recovery time.
    internal static async Task SendMonsterFlinch(string remote, NetworkStream stream, ConnectionCiphers ciphers,
        ClientState conn, uint id, SpawnedMonster m, int damage, bool log)
    {
        while (true)
        {
            lock (conn.Monsters)
                if (!MonsterPresent(conn, id, m) || damage <= 0 || m.HitAnimations is not { Count: > 0 }
                    || m.Mods.Effects.CannotBeStunned || damage < m.MaxLife * HitReaction.DamageFraction) return;
            bool checkedAtGate = false;
            await Send(remote, stream, ciphers, [], null, atGate: () =>
            {
                lock (conn.Monsters)
                {
                    if (!MonsterPresent(conn, id, m) || conn.Paused) return null;
                    checkedAtGate = true;
                    double now = conn.Clock.Elapsed.TotalSeconds;
                    if (HitReaction.TryFlinch(m, damage, now, Random.Shared) is not { } clip) return null;
                    float seconds = (float)(m.FlinchUntil - now);
                    if (log) Console.WriteLine($"[gateway] {remote} => 0x01d1 monster 0x{id:x} TakeHit recovery {seconds:0.000} s " +
                        $"(clip profile {clip.Index}), action {m.ActionGeneration}, recovery until {m.FlinchUntil:0.000}");
                    // Animation.dat row 5 selects TakeHit through this actor's native ACT. Keep clip selection and
                    // completion with Actor recovery instead of following it with a rig-only 0x1b2 override.
                    return InstanceProtocol.ActorAnimation(id, seconds, PlayerHitRecovery.TakeHitAnimation);
                }
            });
            if (checkedAtGate) return;
            await Task.Delay(16);
        }
    }

    internal static async Task MonsterSwing(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, uint id,
        SpawnedMonster m, int action)
    {
        if (m.Attack is not { } a) return;
        // Entry protection: a protected player is not attacked (the planner also skips hostile monsters meanwhile).
        if (InGracePeriod(conn)) return;
        double startedAt = 0;
        if (!await SendMonsterAction(remote, stream, ciphers, conn, id, m, action, () =>
        {
            startedAt = conn.Clock.Elapsed.TotalSeconds;
            var from = m.Walk.Position(startedAt);
            var player = conn.Walk.Position(startedAt);
            m.BusyUntil = startedAt + a.Seconds;
            m.SwingGeneration = action;
            return InstanceProtocol.ActionStarted(id, from.X, from.Y, player.X, player.Y, a.SkillId, MonsterActionSequence, 0x0408, targetObjectId: PlayerId);
        }, $"0x01ab monster 0x{id:x} ({m.Label}) attacks the player, attack time {a.Seconds:0.00} s")) return;
        // The contact: the clip's keyframe for a projectile skill (the Spitter's attack1 spits at 37.5 % of the attack),
        // half the attack time for a melee swing (its clips' keyframes are not read yet).
        if (!await WaitMonsterActionTime(conn, id, m, action, startedAt + a.Seconds * (a.Release?.Fraction ?? 0.5))) return;
        if (a.Projectile is { } projectile)
        {
            // The shot leaves at the contact and flies; its hit lands when it reaches the player (Program.ProjectileHostile.cs).
            LaunchMonsterProjectile(remote, stream, ciphers, conn, id, m, a, projectile, action);
            return;
        }
        await MonsterContact(remote, stream, ciphers, conn, id, m, action);
    }

    // A melee contact at the attack's halfway point: committed at the actual send gate under the monster's action, so a
    // flinch accepted while it was queued suppresses both the packet and the life mutation. A ranged monster's shot commits
    // the same pipeline when its flight reaches the player instead (Program.ProjectileHostile.cs).
    internal static async Task MonsterContact(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        uint id, SpawnedMonster m, int action)
    {
        if (m.Attack is not { } a) return;
        MonsterHitOutcome? outcome = null;
        if (!await SendMonsterAction(remote, stream, ciphers, conn, id, m, action, () =>
        {
            // Entry protection: the player standing protected on the arrival cell takes no hit.
            if (InGracePeriod(conn)) return null;
            // The contact time is the later of the projectile runtime's committed time (a buffered input cannot rewrite
            // that history) and the connection clock: the runtime is not advanced at this gate, so its time can lag a
            // clock read the life tick or a mortar landing already made, and the fortification and recent-hit trackers
            // refuse to go backwards (review 2026-10-30: the refusal faulted the swing's task and the hit was lost).
            double now = Math.Max(ProjectileWorldTime(conn), conn.Clock.Elapsed.TotalSeconds);
            outcome = CommitMonsterHitLocked(remote, conn, id, m, a, now, conn.Walk.Position(now), m.Walk.Position(now), checkRange: true);
            return outcome?.HitPacket;
        }, $"0x01af monster 0x{id:x} hit on the player (kind 3, sequence 0xffff)")) return;
        if (outcome is not null) await PresentMonsterHit(remote, stream, ciphers, conn, id, outcome);
    }

    /// <summary>The monster hit packet's flags: generic particles (0x4000), main hand (0x0200) and blood (0x0001) always,
    /// 0x0040 for a blocked hit and 0x0020 for a critical strike (action-hit-packet-3.29.3b.md §2).</summary>
    internal static ushort MonsterHitFlags(bool blocked, bool critical) =>
        (ushort)(0x4201 | (blocked ? 0x0040 : 0) | (critical ? 0x0020 : 0));

    /// <summary>What a monster's hit on the player did (CommitMonsterHitLocked), for the packets that present it.</summary>
    internal sealed record MonsterHitOutcome(byte[] HitPacket, uint Life, uint Mana, uint ManaBefore, int Damage, int RawDamage,
        bool Blocked, bool Critical, PlayerRecoveryIntent? Recovery, RecentEnemyHits.Decision Armour);

    // The monster's hit on the player, committed under the Monsters lock at the moment it lands: reach (melee only),
    // evasion, the mod-rolled packet, block, armour, resistances, ailments, recovery and death. Null when it misses or is
    // evaded. `from` is where the hit comes from: the monster for a swing, the shot's contact point for a projectile.
    internal static MonsterHitOutcome? CommitMonsterHitLocked(string remote, ClientState conn, uint id, SpawnedMonster m, MonsterAttack a,
        double now, (int X, int Y) here, (int X, int Y) from, bool checkRange)
    {
        int priorContacts = conn.RecentEnemyHits.Count(now);
        using var defenceTime = FortificationOf(conn).Capture(now);
        // Resolve lazy final defences here, never at AI collection or before a queued echo/pause wait.
        var stats = CharacterPlayerStats(conn);
        var reduction = PlayerDamageReduction(conn, stats);
        if (checkRange && Dist(here, from) > a.Range + 2)
        {
            Console.WriteLine($"[gateway] {remote} monster 0x{id:x} missed: the player is {Dist(here, from):0.0} away");
            return null;
        }
        // The monster's accuracy comes from DefaultMonsterStats at its level. Evasion uses PoE's shared entropy
        // counter: every attack adds its hit chance, and reaching 100 consumes 100 and hits (evasion cannot streak
        // indefinitely). A new counter starts after 3.33 s without an attack.
        int baseAccuracy = m.Mods.Effects.Accuracy(Monsters.BaseAccuracy(m.Level) ?? 0);
        int monsterAccuracy = RampageBlindedValue(m, now, baseAccuracy, conn.FlaskGrounds.Accuracy(id, baseAccuracy));
        int hitChance = DamageModel.AccuracyHitChance(conn.Pools.Evasion, monsterAccuracy);
        if (EvadesLocked(conn, hitChance, now))
        {
            Console.WriteLine($"[gateway] {remote} monster 0x{id:x} ({m.Label}) evaded: accuracy {monsterAccuracy} " +
                $"vs evasion {conn.Pools.Evasion}, {hitChance}% chance to hit, entropy {conn.EvasionEntropy:0}");
            return null;
        }
        // Roll the whole monster-mod packet (the Gambler's incoming luck included), then mitigate each component
        // with this hit's one armour decision.
        var gambler = GamblerFor(stats);
        var rolled = gambler.RollIncomingHit(a.Damage, conn.IncomingHitRandom);
        rolled = MonsterModHit(m, rolled, conn.PlayerLife, conn.PlayerMaxLife, conn.IncomingHitRandom, out bool monsterCritical,
            () => SelfCriticalReduction(conn));   // its crit, Executioner's
        var mitigated = gambler.MitigateIncomingHit(rolled, type =>
            new DamageModel.ResistanceValue(Resistance(conn, type), MaxResistance(conn, type)), conn.IncomingHitRandom);
        var immune = PlayerImmunities(conn);
        // This remains the original next RNG draw when the feature is absent. Blocking never
        // consumes an armour draw; one lazy decision is shared by the whole rolled packet.
        bool blocked = DamageModel.Blocks(conn.Pools.BlockChance, conn.IncomingHitRandom.NextDouble() * 100, conn.Pools.AttackBlockLucky, conn.Pools.AttackBlockUnlucky);
        bool armourRelevant = mitigated.Components.Any(c => !immune.Contains(c.Type)
            && Math.Round(c.Range.Min) > 0 && (c.Type == DamageType.Physical
                || c.Type == DamageType.Chaos && conn.Pools.ArmourAppliesToChaosDamage));
        var armour = IncomingArmourLocked(conn, !blocked && armourRelevant, priorContacts, stats);
        int rawDamage = (int)Math.Round(rolled[DamageType.Physical].Min);
        int damage = 0, chaosDamage = 0;
        var ailmentTaken = DamagePacket.Empty;
        double shockFactor = conn.ElementalAilments.DamageTakenFactor(now);
        foreach (var (type, range) in mitigated.Components)
        {
            int value = immune.Contains(type) ? 0 : (int)Math.Round(range.Min);
            value = PlayerHitMitigation.AfterResistance(conn.Pools, type, value,
                reduction.Physical, reduction.Elemental, armour.EffectiveArmour);
            if (type == DamageType.Physical)
                value = AntiquarianEffects.RangedPhysical(value, a.Ranged,
                    name => Stats.Key(name) is uint key ? stats.GetValueOrDefault(key) : 0);
            value = (int)Math.Round(value * shockFactor);
            ailmentTaken = ailmentTaken.Add(type, new(value, value));
            if (type == DamageType.Chaos) chaosDamage += value;
            damage += value;
        }
        damage = FortificationDamageTaken(conn, damage);
        chaosDamage = FortificationDamageTaken(conn, chaosDamage);
        if (blocked) { damage = 0; chaosDamage = 0; }
        uint manaBefore = conn.PlayerMana;
        int gainedLife = blocked ? conn.Pools.LifeGainedOnBlock : 0;
        int gainedMana = blocked ? conn.Pools.ManaGainedOnBlock : 0;
        conn.PlayerLife = (uint)Math.Min(conn.PlayerMaxLife, Math.Max(0, (long)conn.PlayerLife - DamagePlayerHit(conn, damage - chaosDamage, chaosDamage, now, stats) + gainedLife));
        conn.PlayerMana = (uint)Math.Min((uint)UnreservedMana(conn), (long)conn.PlayerMana + gainedMana);
        TrimLeechLocked(conn);
        ApplyPlayerAilmentsLocked(conn, a.Damage, ailmentTaken.Select((_, r) =>
            new(FortificationDamageTaken(conn, (int)r.Min), FortificationDamageTaken(conn, (int)r.Max))),
            monsterCritical, blocked, now, stats);
        conn.RecentEnemyHits.Record(now);
        ChargesWhenHit(conn, blocked);
        if (conn.PlayerLife == 0) { conn.InvalidateIncomingHits(); ClearAntiquarianOnDeath(conn); }
        RecordHardcoreDeath(conn);
        uint life = conn.PlayerLife;
        PlayerRecoveryIntent? recovery = null;
        if (!blocked && !a.Ranged && life > 0 && a.Damage.Components.All(c => c.Type == DamageType.Physical))
            recovery = CommitPlayerHitRecoveryLocked(conn, damage, now);
        else if (Stats.Key(AntiquarianEffects.StunRecoverySource) is uint turtle && stats.GetValueOrDefault(turtle) > 0)
            Console.WriteLine($"[gateway] Turtle Shell hit ineligible: block={blocked}, ranged={a.Ranged}, "
                + $"alive={life > 0}, types={string.Join(',', a.Damage.Components.Select(c => c.Type))}");
        RageWhenHitLocked(conn);
        // The hit packet carries this outcome's presentation and is processed at once (kind 3): 0x4000 particles,
        // 0x0200 main hand, 0x0001 blood, 0x0040 blocked, 0x0020 critical, code = the largest damage component.
        // The 3.29.3b decode (action-hit-packet-3.29.3b.md §§1–4) shows kind 2 only queues the package until an
        // action with that sequence consumes it, which 0xffff never guarantees, and that 0x0400 asks the client for
        // its own stun roll; the player's recovery is the authoritative 0x1d1 (SendPlayerHitRecovery).
        var packet = InstanceProtocol.ActionHit(id, 0xffff, PlayerId, flags: MonsterHitFlags(blocked, monsterCritical), kind: 0x03,
            code: HitCode(mitigated), angle: HitAngle(here.X - from.X, here.Y - from.Y));
        return new(packet, life, conn.PlayerMana, manaBefore, damage, rawDamage, blocked, monsterCritical, recovery, armour);
    }

    // The packets after a committed hit: energy shield, native recovery, the life bar and life, mana, death or flask charges.
    static async Task PresentMonsterHit(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, uint id,
        MonsterHitOutcome hit)
    {
        await SendPlayerEnergyShield(remote, stream, ciphers, conn);
        if (hit.Recovery is not null) await SendPlayerHitRecovery(remote, stream, ciphers, conn, hit.Recovery);
        // At 0 the Life tick stops refreshing the overhead life bar (see Attack): hide it.
        if (hit.Life == 0)
            await Send(remote, stream, ciphers, InstanceEntities.StatsUpdate(PlayerId, [(StatKeys.HideMiniLifeBar, 1)]),
                "0x01b3 player hide_mini_life_bar = 1");
        await Send(remote, stream, ciphers, InstanceProtocol.LifeUpdate(PlayerId, hit.Life),
            $"0x01b1 player hit by monster 0x{id:x} for {hit.Damage}{(hit.Critical ? " (critical)" : "")}" +
            $"{(hit.RawDamage != hit.Damage ? $" ({hit.RawDamage} physical before {DamageModel.ArmourReduction(hit.Armour.EffectiveArmour, hit.RawDamage)}% armour, effective rating {hit.Armour.EffectiveArmour})" : "")}" +
            $"{(hit.Blocked ? " (blocked)" : "")}, life {hit.Life}/{conn.PlayerMaxLife}{(hit.Life == 0 ? " (dies)" : "")}");
        if (hit.Mana != hit.ManaBefore)
            await SendPlayerMana(remote, stream, ciphers, conn,
                $"0x01b1 player mana {hit.Mana} ({(hit.Blocked ? $"+{conn.Pools.ManaGainedOnBlock} on block" : "no block gain")})");
        if (hit.Life == 0)
            await PlayerDied(remote, stream, ciphers, conn, $"0x0055 death screen ({conn.League ?? "?"} league)");
        else if (!hit.Blocked) await GainFlaskChargesWhenHit(remote, stream, ciphers, conn);
    }

    // A spawned monster's id from the argument if it is 0x hex, else the last spawned one.
    static uint? MonsterArg(ClientState conn, string[] args, int i)
    {
        if (args.Length > i && (args[i].StartsWith("0x", StringComparison.OrdinalIgnoreCase)))
        {
            uint id = Convert.ToUInt32(args[i], 16);
            return conn.Monsters.ContainsKey(id) ? id : null;
        }
        return conn.Monsters.Count == 0 ? null : conn.Monsters.Keys.Last();
    }

    // A cell 3 cells from the server's estimate of the player, walkable if the walk map says so.
    static (int X, int Y) NextToPlayer(ClientState conn)
    {
        var (px, py) = conn.Walk.Position(conn.Clock.Elapsed.TotalSeconds);
        foreach (var (dx, dy) in new[] { (3, 0), (-3, 0), (0, 3), (0, -3), (2, 2), (-2, -2) })
            if (conn.Layout.Walkable is not { } w || w(px + dx, py + dy)) return (px + dx, py + dy);
        return (px, py);
    }
}
