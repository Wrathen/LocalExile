using GameServer.Core.Projectiles;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.World;

partial class Program
{
    static void CommitBossStage(string remote, System.Net.Sockets.NetworkStream stream,
        GameServer.Core.Protocol.Login.ConnectionCiphers ciphers, ClientState conn, uint id, SpawnedMonster monster, double at)
    {
        var session = ProjectilesFor(remote, stream, ciphers, conn);
        CommitProjectileBossStage(session, id, monster, Math.Max(session.Runtime.Now, ProjectileRuntime.Time(at)));
    }

    // Called synchronously under XP -> combat lock. The outbox contains notifications of committed results.
    static void CommitProjectileHit(ProjectileSession session, ProjectileDamage contact, SpawnedMonster target,
        MonsterHit hit, int damage, ProjectileOffense shot, bool critical)
    {
        var conn = session.Conn;
        double now = contact.At / (double)ProjectileRuntime.QuantaPerSecond;
        conn.LastEnemyHitAt = now;
        uint id = contact.Target.Id;
        var random = contact.Projectile.ReactionRandomSource;
        if (hit.Life == 0)
            session.Packet(InstanceEntities.StatsUpdate(id, [(StatKeys.HideMiniLifeBar, 1)]));
        // Native Actor TakeHit recovery (0x1d1), as the melee path sends: a rig-only clip (0x1b2) leaves the client
        // monster walking while the server's estimate has stopped (11-skills.md "Hit reaction").
        if (HitReaction.TryFlinch(target, damage, now, random) is not null)
            session.Packet(InstanceProtocol.ActorAnimation(id, (float)(target.FlinchUntil - now), PlayerHitRecovery.TakeHitAnimation));
        if (shot.HitPacket)
        {
            // S→C 0x1af before the life update, as the melee default attack sends it: an immediate package (kind 3) for the
            // shot's action with blood (0x0001), the generic particles (0x4000) of its largest damage type, the critical
            // bit when it crit, and the main-hand bit (0x0200: the weapon's own effect and stat selection), which GGG's
            // server sets on every bow hit (live capture 2026-10-29, ranged-monster-actions-port.md "Live reference").
            // Live also sends skill index 2 and a filled object header; their meanings are unknown, so they stay zero.
            var direction = contact.Projectile.Direction;
            ushort flags = (ushort)(0x4201 | (critical ? 0x0020 : 0));
            session.Packet(InstanceProtocol.ActionHit(PlayerId, shot.ActionSequence ?? 0xffff, id, flags: flags, kind: 0x03,
                code: HitCode(shot.Hit), angle: HitAngle((int)Math.Round(direction.X * 1000), (int)Math.Round(direction.Y * 1000))));
        }
        session.Packet(InstanceProtocol.LifeUpdate(id, hit.Life),
            $"projectile {shot.Label} {contact.Stage} t={now:0.000000} use={contact.Projectile.UseId} entity={contact.Projectile.Id}"
            + $" phase={contact.Phase} form={contact.Projectile.Form} at={contact.Position}"
            + $" target=0x{id:x} damage={damage} life={hit.Life}/{target.MaxLife}");
        if (critical) CommitProjectileCritFlasks(session, now, random);
        if (hit.Life == 0) CommitProjectileKill(session, id, target, contact.At, contact.Projectile.RewardRandomSource);
        if (hit.StageStarts) CommitProjectileBossStage(session, id, target, contact.At);
        // Charges affect the next chronological hit/use, rather than waiting for the presentation ticker.
        var charges = ChargesOf(conn);
        if (charges.Pending)
        {
            var final = CharacterPlayerStats(conn);
            int Get(string name) => Stats.Key(name) is uint key ? final.GetValueOrDefault(key) : 0;
            var step = charges.Step(now, Get, () => WeaponProfileFor(conn), conn.PlayerLife > 0,
                now < conn.Walk.ArrivesAt, InTown(conn), random);
            foreach (var buff in charges.Presentation(now, () => conn.NextBuff++))
                foreach (var (packet, _) in ChargeBuffs.Packets(buff, PlayerId)) session.Packet(packet);
            if (step.CountsChanged)
            {
                conn.Pools = PoolsFor(conn, conn.Pools.Level); conn.ApplyMovementSpeed();
                session.Enqueue(() => SendChargeStats(session.remote, session.stream, session.ciphers, conn, "projectile hit"));
            }
        }
    }

    static void CommitProjectileBossStage(ProjectileSession session, uint id, SpawnedMonster monster, long at)
    {
        var stage = monster.Stage!;
        double now = at / (double)ProjectileRuntime.QuantaPerSecond;
        var pos = monster.Walk.Position(now);
        monster.Walk.Order(pos.X, pos.Y, now); monster.LastTarget = null; monster.BusyUntil = now + stage.CastSeconds;
        session.Packet(InstanceProtocol.ActionStarted(id, pos.X, pos.Y, pos.X, pos.Y, StageSkillId, 0xffff, 0x0408));
        session.Runtime.Schedule(at + ProjectileRuntime.Time(stage.CastSeconds), () =>
        {
            if (!session.Conn.Monsters.TryGetValue(id, out var current) || !ReferenceEquals(current, monster) || monster.Dead) return;
            monster.StageRunning = false; monster.Life = monster.MaxLife;
            if (monster.Variety?.ComponentsOrBase.ToList().IndexOf("Transitionable") is int index and >= 0)
                session.Packet(InstanceEntities.TransitionableUpdate(id, (byte)index, stage.TransitionTo));
            session.Packet(InstanceProtocol.LifeUpdate(id, monster.Life));
            session.Packet(InstanceProtocol.ActionEnded(id));
        });
    }

    static void CommitProjectileKill(ProjectileSession session, uint id, SpawnedMonster monster, long at, Random random,
        bool playerKill = true)
    {
        var conn = session.Conn;
        var position = monster.Walk.Position(at / (double)ProjectileRuntime.QuantaPerSecond);
        // Match ordinary kills: corpses stay until area teardown, and impales retain their own expiry/hit state.
        if (HardcoreProgressBlocked(conn) || monster.Variety is not { } variety || !monster.Hostile) return;
        if (playerKill) ChargesOnKill(conn, monster);
        if (conn.Layout.Id == TutorialObjects.Strand && variety.Id == TutorialObjects.Zombie
            && CharacterSaves.Load(CharacterName(conn))?.Tutorial is { ZombieKilled: false })
        {
            CharacterSaves.ChangeTutorial(CharacterName(conn), t => t with { ZombieKilled = true });
            if (CharacterSaves.Load(CharacterName(conn))?.Tutorial is { SkillClaimed: false } tutorial)
            {
                var item = CharacterSaves.FromSaved(tutorial.SkillGem, Items);
                var ground = new WorldItemObject(position.X, position.Y, item);
                if (conn.Instance.PlaceTutorialItem(TutorialObjects.DropKey(CharacterName(conn), tutorial, false), ground) is uint groundId)
                    PublishProjectileGroundItem(session, groundId, ground);
            }
        }
        if (CommitQuestFlags(conn, Quests.OnMonsterKilled(conn.Layout.Id, variety.Id)) is { } quest)
            session.Enqueue(() => PublishQuestFlags(session.remote, session.stream, session.ciphers, conn, quest));
        if (MercyMission.DropsChest(conn.Layout.Id, variety.Id) && MedicineChestType() is { } chest
            && MercyMission.HailrakeDrops(chest.Hash, MercyDone(conn), ItemsOf(conn), conn.Instance.Ground))
            AddProjectileGroundItem(session, new(position.X, position.Y, ItemCatalog.Create(chest)));
        uint xp = 0;
        if (conn.PlayerLife > 0 && Dist(position, conn.Walk.Position(at / (double)ProjectileRuntime.QuantaPerSecond)) <= ExperienceRange
            && Monsters.BaseExperience(variety, monster.Level) is int baseXp)
            xp = Progression.MonsterExperience(baseXp, variety.ExperienceMultiplier, monster.Rarity, conn.Pools.Level, monster.Level);
        var (gemPackets, leveled) = CommitGemExperience(conn, xp);
        foreach (var packet in gemPackets) session.Packet(packet);
        if (leveled) session.Enqueue(() => SendSkills(session.remote, session.stream, session.ciphers, conn));
        if (Quests.LevelAfterKill(conn.Layout.Id, variety.Id) is int required and > 1 && required <= CharacterStats.LevelStarts.Count)
            xp = (uint)Math.Max(xp, Math.Max(0L, (long)CharacterStats.LevelStarts[required - 1] - conn.Experience));
        if (xp > 0 && RecordExperienceGain(conn, xp) is { } change)
        {
            int level = Progression.LevelFor(change.After, CharacterStats.LevelStarts);
            session.Packet(InstanceEntities.PlayerExperienceUpdate(PlayerId, change.After));
            if (level != conn.Pools.Level)
            {
                CommitExperienceLevel(conn, level);
                conn.PlayerLife = conn.PlayerMaxLife; conn.PlayerMana = (uint)UnreservedMana(conn);
                conn.EnergyShield.SetMaximum(conn.Pools.EnergyShield, true);
                TrimLeechLocked(conn);
                session.Enqueue(() => SendCharacterStats(session.remote, session.stream, session.ciphers, conn, "projectile kill level"));
                session.Packet(InstanceProtocol.LifeUpdate(PlayerId, conn.PlayerLife));
                session.Enqueue(() => SendPlayerMana(session.remote, session.stream, session.ciphers, conn, "projectile kill level"));
                session.Packet(PassivesMessage(conn).Bytes);
                // Account-level persistence is a projection of the committed XP ledger.
                session.Enqueue(async () => { await SynchronizeAccountLevel(conn); });
            }
        }
        CommitProjectileFlaskGain(session, (_, _) => FlaskCharges.FullShortcut ? int.MaxValue
            : FlaskCharges.KillCharges(monster.Rarity, ItemsFor(conn).Mods));
        if (Environment.GetEnvironmentVariable("Game_DROPS") != "0")
        {
            var balance = conn.Ruthless ? RuthlessSettings.Current : null;
            List<ItemDrops.Drop> drops = [.. DropsFor(conn).Roll(monster.Level, monster.Rarity, random, IncreasedItemRarity(conn),
                balance?.ItemDropMultiplier ?? 1, balance?.EquipmentRarityMultiplier ?? 1)];
            if (DropsFor(conn).Gold(monster.Level, monster.Rarity, random, balance?.GoldMultiplier ?? 1) is { } gold) drops.Add(gold);
            foreach (var drop in drops)
            {
                double angle = random.NextDouble() * Math.Tau, radius = 3 + random.NextDouble() * 3;
                int x = position.X + (int)Math.Round(Math.Cos(angle) * radius), y = position.Y + (int)Math.Round(Math.Sin(angle) * radius);
                if (conn.Layout.Walkable is { } walks && !walks(x, y)) (x, y) = position;
                AddProjectileGroundItem(session, new(x, y, drop.Item));
            }
        }
    }

    static void AddProjectileGroundItem(ProjectileSession session, WorldItemObject ground) =>
        PublishProjectileGroundItem(session, session.Conn.Instance.Ground.Add(ground), ground);

    static void PublishProjectileGroundItem(ProjectileSession session, uint id, WorldItemObject ground)
    {
        session.Packet(InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(WorldItemObject.MetadataPath), ground.Blob()));
        session.Packet(InstanceProtocol.EnterWakeZone(id));
    }

    static void CommitProjectileFlaskGain(ProjectileSession session,
        Func<InventoryItem, IReadOnlyDictionary<string, int>, double> gain)
    {
        var conn = session.Conn;
        if (HardcoreProgressBlocked(conn)) return;
        var stats = CharacterPlayerStats(conn);
        int Get(string name) => Stats.Key(name) is uint key ? stats.GetValueOrDefault(key) : 0;
        var items = ItemsOf(conn);
        var changed = new List<InventoryItem>();
        foreach (var flask in items.Items(PlayerInventories.Flask))
        {
            if (!CanGainFlaskCharges(conn, flask)) continue;
            if (ItemsFor(conn).ByHash(flask.Item.TypeHash) is not { } type || ItemsFor(conn).FlaskChargesOf(type) is not { } basis) continue;
            var local = ItemModStats.Local(flask.Item, ItemsFor(conn).Mods); var charges = FlaskCharges.Of(basis, local);
            if (flask.Item.Charges >= charges.MaxCharges) continue;
            int whole = conn.FlaskChargeCarry.Take(flask.Id, gain(flask, local) * FlaskCharges.GainMultiplier(local, Get));
            if (whole > 0 && items.ChangeUnsaved(PlayerInventories.Flask, flask.Id, value => Flasks.Gain(value, charges, whole)) is { } item)
                changed.Add(item);
        }
        if (changed.Count > 0)
            session.Packet(InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.Flask),
                items.NextRevision(PlayerInventories.Flask), [.. changed.Select(i => i.Id)], changed));
    }

    static void CommitProjectileCritFlasks(ProjectileSession session, double at, Random random)
    {
        var conn = session.Conn;
        if (!conn.FlaskChargeCarry.CritReady(at)) return;
        var bar = ItemsOf(conn).Items(PlayerInventories.Flask).Select(f => (f.Id,
            Chance: ItemModStats.Local(f.Item, ItemsFor(conn).Mods).GetValueOrDefault("local_recharge_on_crit_%"),
            Full: ItemsFor(conn).ByHash(f.Item.TypeHash) is not { } type || ItemsFor(conn).FlaskChargesOf(type) is not { } basis
                || f.Item.Charges >= FlaskCharges.Of(basis, ItemModStats.Local(f.Item, ItemsFor(conn).Mods)).MaxCharges)).ToList();
        var stats = CharacterPlayerStats(conn);
        int Get(string name) => Stats.Key(name) is uint key ? stats.GetValueOrDefault(key) : 0;
        bool precision = conn.Auras.Values.Any(a => a.EffectId == "Precision");
        var gains = FlaskCharges.OnCrit(bar, FlaskCharges.CritSources(Get, precision), conn.FlaskChargeCarry, at, random.Next);
        if (gains.Count > 0) CommitProjectileFlaskGain(session, (f, _) => gains.GetValueOrDefault(f.Id));
    }
}
