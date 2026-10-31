using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using DamageModel = GameServer.Core.World.DamageModel;
using DamageType = GameServer.Core.World.DamageType;
using DamageOverTime = GameServer.Core.World.DamageOverTime;

partial class Program
{
    /// <summary>Whether a stash tab goes out on area entry, so shop prices see the currency in it: a currency or grid tab
    /// (the only ones that hold shop currency) with currency (class StackableCurrency) in it.</summary>
    internal static bool PreloadsForPrices(byte? tabType, IEnumerable<InventoryItem> inside, ItemCatalog catalog) =>
        tabType is byte type && (type == StashRules.Currency || StashRules.IsGrid(type))
        && inside.Any(i => catalog.ByHash(i.Item.TypeHash)?.Class == "StackableCurrency");

    // The characters in god mode, by name, for the Gateway's lifetime: every area is a new connection, so without this an
    // area change or a relog silently ended /god.
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> GodCharacters = new(StringComparer.OrdinalIgnoreCase);

    // A new connection of a character in god mode is in it again (LoadProgress, before its pools are worked out). It only
    // switches on: a caller may have set it already.
    internal static void RestoreGod(ClientState conn)
    {
        if (GodCharacters.ContainsKey(CharacterName(conn))) conn.God = true;
    }

    static async Task SetGod(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, bool on)
    {
        lock (conn.Monsters)
        {
            conn.God = on;
            if (on) GodCharacters[CharacterName(conn)] = true; else GodCharacters.TryRemove(CharacterName(conn), out _);
            conn.Pools = PoolsFor(conn, conn.Pools.Level);
            conn.ApplyMovementSpeed();
            conn.PlayerLife = on ? conn.PlayerMaxLife : Math.Min(conn.PlayerLife, conn.PlayerMaxLife);
            conn.PlayerMana = on ? (uint)UnreservedMana(conn) : Math.Min(conn.PlayerMana, (uint)UnreservedMana(conn));
            TrimLeechLocked(conn);
        }
        Console.WriteLine($"[gateway] {remote} god mode {(on ? "on" : "off")}: life {conn.Pools.Life}, mana {conn.Pools.Mana}, {ReservedMana(conn)} reserved");
        await SendCharacterStats(remote, stream, ciphers, conn, on ? "god mode on" : "god mode off");
        await Send(remote, stream, ciphers, InstanceProtocol.LifeUpdate(PlayerId, conn.PlayerLife), $"0x01b1 player life {conn.PlayerLife}");
        await SendPlayerMana(remote, stream, ciphers, conn, $"0x01b1 player mana {conn.PlayerMana}");
        // UI built on entering caches the attributes (see SendPlayer): send them once more.
        await SendPlayerStatSnapshot(remote, stream, ciphers, conn,
            () => InstanceEntities.StatsUpdate(PlayerId, [.. BaseStats(conn)]), "0x01b3 player attributes");
    }

    // The player's life per second from regeneration: the flat life regeneration per minute (/god, /set_stat) plus a
    // percentage of maximum life, from the row PanelStats.Recovery sends to the character panel.
    static double LifeRegenPerSecond(ClientState conn)
    {
        lock (conn.Monsters)
        {
            CharacterPlayerStatsCore(conn, out double baseline);
            return baseline;
        }
    }

    // The player's mana per second from regeneration: Character.ot's mana_regeneration_rate_per_minute_% (105: 1.75% of
    // maximum mana per second) plus the stats' flat base_mana_regeneration_rate_per_minute (/god), raised by
    // mana_regeneration_rate_+%.
    static double ManaRegenPerSecond(ClientState conn)
    {
        var stats = CharacterPlayerStats(conn);
        int Get(string name) => Stats.Key(name) is uint k ? stats.GetValueOrDefault(k) : 0;
        return PanelStats.ManaPerMinute(Get, conn.Pools.Mana) / 60;
    }

    // The mana the running auras reserve, and what is left for casting and regeneration.
    // Restoring an already enabled aura is not a fresh cast: transient mana availability must not discard its saved intent.
    static IReadOnlyList<AuraCatalog.Aura> RestoreRememberedAuras(ClientState conn, IEnumerable<string> saved)
    {
        if (conn.Closed || conn.LeavingArea || conn.RageRetired || InTown(conn) || OwnAuraRestrictionValue(conn) != 0) return [];
        var wanted = saved.Select(AuraCatalog.Family).ToHashSet(StringComparer.Ordinal);
        var candidates = new List<AuraCatalog.Aura>();
        foreach (var gem in GemSkillEffects(conn))
            if (SkillsFor(conn).EffectId(gem.Effect) is { } effect && wanted.Contains(AuraCatalog.Family(effect)) && AuraCatalog.Supported(effect)
                && !conn.Auras.Values.Any(a => AuraCatalog.Family(a.EffectId) == AuraCatalog.Family(effect))
                && GemSkillInstanceDisabled(conn, gem.Effect, gem.Id) is null
                && SupportedReservation(conn, gem.Id) is { } reservation)
                candidates.Add(new(gem.Id, gem.Effect, effect, gem.Name, conn.NextBuff++, AuraCatalog.BuffRow(effect),
                    reservation.Flat, reservation.Percent, SupportedSkill(conn, gem.Id).Level, SupportedSkill(conn, gem.Id).Quality, SourceBuff: conn.NextBuff++));
        var restored = AuraCatalog.Restore(saved, candidates, conn.Pools, InTown(conn));
        foreach (var aura in restored) conn.Auras[aura.SkillId] = aura;
        if (restored.Count > 0)
        {
            conn.AuraStatsDirty = true;
            conn.Pools = PoolsFor(conn, conn.Pools.Level);
            conn.ApplyMovementSpeed();
            conn.PlayerMana = Math.Min(conn.PlayerMana,
                (uint)AuraCatalog.UnreservedMana(conn.Pools, conn.Auras.Values));
            Console.WriteLine($"[gateway] {CharacterName(conn)}: restored saved auras: {string.Join(", ", restored.Select(a => a.EffectId))}");
        }
        return restored;
    }

    static bool InGracePeriod(ClientState conn)
    {
        double now = conn.Clock.Elapsed.TotalSeconds;
        return conn.EntryProtection.Protects(now, conn.Walk.Position(now));
    }

    static async Task StartGracePeriod(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        double now = conn.Clock.Elapsed.TotalSeconds;
        ushort buff;
        lock (conn.Monsters) buff = conn.NextBuff++;
        bool hideout = HideoutCatalog.ByArea(conn.Layout.Hash) is not null;
        if (conn.EntryProtection.Start(now, conn.Walk.Position(now), buff, InTown(conn), hideout))
            await Send(remote, stream, ciphers, GracePeriod.BeginPacket(PlayerId, buff),
                $"Grace Period: {GracePeriod.Seconds} seconds of entry protection");
    }

    static async Task EndGracePeriod(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, bool action = false)
    {
        double now = conn.Clock.Elapsed.TotalSeconds;
        if (conn.EntryProtection.End(now, conn.Walk.Position(now), action) is ushort buff)
            await Send(remote, stream, ciphers, InstanceEntities.RemoveBuff(PlayerId, buff),
                "Grace Period ends: movement, action or deadline");
    }

    static bool InTown(ClientState conn) => WaypointCatalog.Areas.Find(conn.Layout.Hash) is { IsTown: true };

    static async Task EndAurasOnDeath(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        if (conn.PlayerLife != 0 || conn.AuraDeathHandled) return;
        conn.AuraDeathHandled = true;
        await EndGracePeriod(remote, stream, ciphers, conn, action: true);
        CharacterSaves.ClearActiveAuras(CharacterName(conn));
        List<AuraCatalog.Aura> ended;
        lock (conn.Monsters)
        {
            ended = [.. conn.Auras.Values];
            conn.Auras.Clear();
            conn.AuraStatsDirty = ended.Count > 0 || conn.AuraStatsDirty;
            conn.Pools = PoolsFor(conn, conn.Pools.Level);
            conn.ApplyMovementSpeed();
        }
        await SendOwnAuraChanges(remote, stream, ciphers, conn);
        if (ended.Count > 0)
        {
            await SendCharacterStats(remote, stream, ciphers, conn, "auras cleared on death");
            await SendPlayerMana(remote, stream, ciphers, conn, "aura reservation cleared on death");
        }
    }

    static Task PlayerDied(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, string label) =>
        PlayerDiedAtEpoch(remote, stream, ciphers, conn, label);

    static async Task PlayerDiedAtEpoch(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        string label, int? expectedEpoch = null)
    {
        int deathEpoch;
        lock (conn.Monsters)
        {
            if (conn.Closed || conn.LeavingArea || conn.PlayerLife != 0
                || expectedEpoch is int expected && expected != conn.IncomingHitEpoch
                || conn.DeathNotificationEpoch == conn.IncomingHitEpoch) return;
            RecordHardcoreDeath(conn);
            conn.InvalidateIncomingHits();
            conn.DeathNotificationEpoch = deathEpoch = conn.IncomingHitEpoch;
            FortificationOf(conn).Clear();
            LeechOf(conn).Clear(conn.Clock.Elapsed.TotalSeconds);
            ClearRageOnDeath(conn);
            ChargesOnDeath(conn);
            ClearBloodChargesOnDeath(conn);
            ClearAntiquarianOnDeath(conn);
        }
        foreach (var (id, life) in RecoverRuthlessBossesOnDeath(conn))
            await Send(remote, stream, ciphers, InstanceProtocol.LifeUpdate(id, life), $"Ruthless campaign boss 0x{id:x} recovers to {life} life after player death");
        PersistRuthlessVitals(conn, bestEffort: true);
        await SendVaalSouls(remote, stream, ciphers, conn);
        await SendRage(remote, stream, ciphers, conn);
        await EndAurasOnDeath(remote, stream, ciphers, conn);
        await Send(remote, stream, ciphers, [], label, atGate: () =>
        {
            lock (conn.Monsters)
                return !conn.Closed && !conn.LeavingArea && conn.PlayerLife == 0 && conn.IncomingHitEpoch == deathEpoch
                    ? [.. InstanceProtocol.LifeUpdate(PlayerId, 0),
                        .. InstanceEntities.StatsUpdate(PlayerId, [(StatKeys.HideMiniLifeBar, 1)]),
                        .. InstanceProtocol.DeathScreen(DeathScreenFlags(conn))] : null;
        });
    }

    static int ReservedMana(ClientState conn)
    {
        lock (conn.Monsters)
        {
            EnsureOwnAuraPools(conn);
            return AuraCatalog.ReservedMana(conn.Pools, conn.Auras.Values);
        }
    }

    static int UnreservedMana(ClientState conn) => Math.Max(0, (int)conn.Pools.Mana - ReservedMana(conn));

    // 0x1b1 for the mana pool: every update carries the reserved amount, or the client would clear it. The pool and the
    // reservation are read under one lock, so a packet never pairs a pool from before an aura toggle with the
    // reservation from after it.
    static async Task SendPlayerMana(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        string? log = null)
    {
        await Send(remote, stream, ciphers, [], log, atGate: () =>
        {
            // Sample both values together after earlier sends, including a queued pool/reservation rebuild.
            lock (conn.Monsters)
            {
                // Preserve already-paid cost publication on the existing retired-action path.
                // Aura restoration/presentation is forbidden on that old connection.
                if (conn.Closed || conn.LeavingArea || conn.RageRetired)
                    return InstanceProtocol.LifeUpdate(PlayerId, conn.PlayerMana,
                        (uint)ReservedMana(conn), pool: ManaPool);
                return WithOwnAuraChanges(conn, () => InstanceProtocol.LifeUpdate(PlayerId,
                    conn.PlayerMana, (uint)ReservedMana(conn), pool: ManaPool));
            }
        });
        await SendPlayerEnergyShield(remote, stream, ciphers, conn);
    }

    static Task SendPlayerEnergyShield(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn) =>
        Send(remote, stream, ciphers, [], "0x01b1 player energy shield", atGate: () =>
        {
            // Sample and deduplicate after earlier sends: a queued pool change can clamp ES or end recharge.
            // Publish the current resource and its predictor together, from the same locked state.
            lock (conn.Monsters)
            {
                if (conn.Closed || conn.LeavingArea || conn.RageRetired) return null;
                var stats = CharacterPlayerStats(conn);
                int Get(string name) => Stats.Key(name) is uint key ? stats.GetValueOrDefault(key) : 0;
                if (conn.PlayerLife == 0) conn.EnergyShield.Empty();
                uint current = conn.EnergyShield.Current;
                bool active = conn.EnergyShield.Recharging;
                int rate = conn.PlayerLife == 0 ? 0 : (int)Math.Round(
                    EnergyShield.PredictionPerMinute(conn.Pools.EnergyShield, Get, active));
                if (conn.SentEnergyShield == current && conn.SentRechargeActive == active && conn.SentEsRecoveryRate == rate)
                    return null;
                bool rateChanged = conn.SentEsRecoveryRate != rate;
                var resource = InstanceProtocol.LifeUpdate(PlayerId, current, pool: 2);
                byte[] packet = rateChanged && Stats.Key("energy_shield_recovery_per_minute") is uint key
                    ? [.. resource, .. InstanceEntities.StatsUpdate(PlayerId, [(key, rate)])]
                    : resource;
                conn.SentEnergyShield = current;
                conn.SentRechargeActive = active;
                conn.SentEsRecoveryRate = rate;
                return packet;
            }
        });

    // The base mana a gem skill costs at its level (its mana_cost stat), before supports: the client spends it at the
    // start of every use, including a use that becomes a walk (attack-move), so the cost the server gives back must be
    // known outside the supported gem (ManaCostTests pins Holy Strike's 6).
    internal static uint ManaCost(StatCatalog stats, SkillCatalog skills, int effect, int level, int quality) =>
        stats.Key("mana_cost") is uint k ? (uint)skills.EffectStats(effect, level, quality).Where(kv => kv.Item1 == k).Select(kv => kv.Item2).FirstOrDefault() : 0;

    // A gem skill's mana cost at the gem's level (its mana_cost stat), paid when the cast starts. False when the player lacks
    // the mana.
    static bool PayMana(ClientState conn, ushort skillId, out uint cost)
    {
        var own = SupportedSkill(conn, skillId).Stats;
        int Own(string name) => Stats.Key(name) is uint key ? own.GetValueOrDefault(key) : 0;
        cost = (uint)Math.Max(0, Own("mana_cost"));
        lock (conn.Monsters)
        {
            // Reap's Blood Charges: more cost per charge held (BloodCharges), rounded as the client rounds costs.
            if (cost > 0 && BloodCharges.Uses(Own))
                cost = (uint)Math.Round(cost * BloodCharges.More(Own(BloodCharges.CostPerCharge), conn.BloodCharges));
            if (conn.PlayerMana < cost) return false;
            conn.PlayerMana -= cost;
            return true;
        }
    }

    // A genuine spell's final server wait; native actions instead receive the unadjusted base milliseconds.
    // Null refuses an invalid/unrepresentable cast rather than turning it into an immediate hit. A supported skill's
    // own base_cast_speed_+% (skillId) adds to the player's.
    static int? CastMs(ClientState conn, int effect, ushort skillId = 0)
    {
        var own = skillId == 0 ? new Dictionary<uint, int>() : SupportedSkill(conn, skillId).Stats;
        int speed = CastSpeedPct(CharacterPlayerStats(conn));
        if (Stats.Key("base_cast_speed_+%") is uint k) speed += own.GetValueOrDefault(k);
        if (SpellCastTiming.ForSkill(SkillsFor(conn), effect, speed)?.DurationMilliseconds is not int duration) return null;
        // A new action starts at most once per server frame (30/s); channelling repeats are exempt and use their own
        // interval. Keep the fractional tick and round the final wait up to whole milliseconds.
        double seconds = Math.Max(TickSeconds, duration / 1000.0 / PlayerActionFactor(conn));
        return (int)Math.Min(int.MaxValue, Math.Ceiling(seconds * 1000));
    }

    // cast_speed_+% is the total (PanelStats: base_cast_speed_+% and attack_and_cast_speed_+% are already in it).
    static int CastSpeedPct(IReadOnlyDictionary<uint, int> stats) =>
        Stats.Key("cast_speed_+%") is uint k ? stats.GetValueOrDefault(k) : 0;

    // Righteous Fire (GrantedEffects RighteousFire): casting it puts the righteous_fire buff (BuffDefinitions row 12: the
    // flames around the player, BuffVisuals 15) on the player with S→C 0x1c1. While it lasts the player burns for 90% of
    // maximum life per second, nonlethal (base_nonlethal_fire_damage_%_of_maximum_life_taken_per_minute 5400), and enemies
    // within its radius (active_skill_base_area_of_effect_radius 18 cells at level 1, plus their size) take 70% of the
    // player's maximum life per second as fire damage (base_righteous_fire_%_of_max_life_to_deal_to_nearby_per_minute 4200)
    // and carry the righteous_fire_aura buff (row 13). It ends at death or on leaving the area; casting it again does nothing.
    static bool IsRighteousFire(int effect) => Skills.EffectId(effect) == "RighteousFire";

    // The skill_art_variation of the skill effects worn in a gem type's Cosmetics tab slots (0: none), which picks the
    // skill's art (SkillArt).
    static int SkillArtVariation(ClientState conn, uint gem) =>
        Stats.Key("skill_art_variation") is not uint key ? 0
        : CharacterSaves.SkillCosmetics(CharacterName(conn)).Where(kv => CharacterSaves.SkillCosmeticGem(kv.Key) == gem)
            .SelectMany(kv => Mtx.Find(kv.Value.Record) is { } r && MtxEffects.Of(r.MtxTypeHash) is { } fx ? fx.Stats : [])
            .Where(st => st.Stat == key).Select(st => st.Value).FirstOrDefault();

    // Righteous Fire's ring stays on the player. It is an object of its own, which the client moves only when the server
    // says so, so every RingFollowMs (RunRingFollow) and after each move order it glides (0x1ba) to where the player will be
    // RingFollowMs later, along the walk's path. Before 2026-10-23 it glided once per ground click to where that walk
    // ended: a walk started any other way (a click on a stash or an item, C→S 0x24 → Walk.Approach) never moved it, and
    // the straight glide cut the path's corners.
    static async Task FollowRighteousFire(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        if (conn.RighteousFire is not { Ring: uint ring } rf) return;
        double now = conn.Clock.Elapsed.TotalSeconds;
        var to = conn.Walk.Position(now + RingFollowMs / 1000.0);
        if (rf.RingAt == to) return;
        rf.RingAt = to;
        await Send(remote, stream, ciphers, InstanceProtocol.MoveTo(ring, to.X, to.Y, RingFollowMs / 1000f, u28: TerrainHeight), null);
    }

    // The ring's follow loop, while its fire burns.
    static async Task RunRingFollow(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, RighteousFireState rf)
    {
        try
        {
            while (!conn.Closed && conn.RighteousFire == rf)
            {
                await Task.Delay(RingFollowMs);
                if (!conn.Paused && conn.RighteousFire == rf) await FollowRighteousFire(remote, stream, ciphers, conn);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The connection closed while sending.
        }
    }

    static async Task ActivateRighteousFire(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, ushort skillId,
        int effect, int level, int quality, uint gem)
    {
        ushort buff, aura;
        uint ring;
        // A worn skill effect's art (SkillArt): the two buffs' visuals and the ring, which some variations leave out.
        int variation = SkillArtVariation(conn, gem);
        ushort? buffVisual = Art.BuffVisual("righteous_fire_buff_visual", variation), auraVisual = Art.BuffVisual("righteous_fire_aura_visual", variation);
        string? ringAo = Art.Animated("righteous_fire_ring", variation) ?? (variation == 0 ? RighteousFireRingAo : null);
        // The radius takes the player's area of effect stats (passives, items, gem quality) under the skill's own (SkillArea).
        var stats = MergeSkillStats(AreaPlayerStats(conn), SupportedSkill(conn, skillId).Stats);
        int Get(string name, int fallback) => Stats.Key(name) is uint k && stats.TryGetValue(k, out int v) ? v : fallback;
        int radius = SkillArea.Radius(Get("active_skill_base_area_of_effect_radius", 18), name => Get(name, 0), spell: true);
        (int X, int Y) at;
        lock (conn.Monsters)
        {
            if (conn.RighteousFire is not null || conn.PlayerLife == 0) return;
            buff = conn.NextBuff++;
            aura = conn.NextBuff++;
            ring = conn.NextObjectId++;
            at = conn.Walk.Position(conn.Clock.Elapsed.TotalSeconds);
            conn.RighteousFire = new RighteousFireState(buff, aura, ringAo is null ? null : ring, skillId,
                DamagePctPerMinute: Get("base_righteous_fire_%_of_max_life_to_deal_to_nearby_per_minute", 4200),
                BurnPctPerMinute: Get("base_nonlethal_fire_damage_%_of_maximum_life_taken_per_minute", 5400),
                Radius: radius) { AuraVisual = auraVisual,
                    EnergyShieldBurnPctPerMinute = Get("base_nonlethal_fire_damage_%_of_maximum_energy_shield_taken_per_minute", 4200),
                    EnergyShieldDamagePctPerMinute = Get("base_righteous_fire_%_of_max_energy_shield_to_deal_to_nearby_per_minute", 4200) };
        }
        await Send(remote, stream, ciphers, InstanceEntities.AddBuff(PlayerId, buff, RighteousFireBuffRow, PlayerId, skillId, visual: buffVisual),
            $"0x01c1 player buff {buff}: righteous_fire (BuffDefinitions row {RighteousFireBuffRow}) from skill 0x{skillId:x4}, " +
            $"art variation {variation} (BuffVisuals {buffVisual?.ToString() ?? "the row's"} / {auraVisual?.ToString() ?? "the row's"}, ring {ringAo ?? "none"}), " +
            $"radius {conn.RighteousFire!.Radius}, {conn.RighteousFire.DamagePctPerMinute / 60.0:0.#}% of life per second to enemies, {conn.RighteousFire.BurnPctPerMinute / 60.0:0.#}% to the player");
        // The caster burns in its own fire: righteous_fire_aura too, whose BuffVisuals (19) give a player model the
        // flaming hands (EPKFiles2 ri_fire/player.epk) and the ignite flames; the first buff only draws the ring at its feet.
        await Send(remote, stream, ciphers, InstanceEntities.AddBuff(PlayerId, aura, RighteousFireAuraBuffRow, PlayerId, skillId, visual: auraVisual),
            $"0x01c1 player buff {aura}: righteous_fire_aura (BuffDefinitions row {RighteousFireAuraBuffRow})");
        // The big burning circle is not a buff visual: it is MiscAnimated RighteousFireRing, an effect object of its own
        // (EffectObject), kept under the player as it walks (FollowRighteousFire).
        // FixedRotation: the glides that keep it under the player must not turn it (it spun as the player turned).
        if (ringAo is null) return;
        // Scale: the art is drawn for a radius of MiscAnimated __col4 world units (RighteousFireRing 400, about 37 cells of
        // 250/23 units, where the skill's base radius is 18), so it is scaled to the radius the fire burns in.
        float scale = (float)(radius * (250.0 / 23) / (Art.ArtRadius(ringAo) ?? 400));
        var ringObject = new EffectObject(at.X, at.Y, ringAo) { Flags0 = EffectObject.FixedRotation, Scale = scale };
        await Send(remote, stream, ciphers, InstanceEntities.CreateObject(ring, InstanceEntities.ObjectTypeHash(EffectObject.TypePath), ringObject.Blob()),
            $"0x02b9 create effect 0x{ring:x} {ringAo} at ({at.X}, {at.Y}), scale {scale:0.###} for radius {radius}");
        await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(ring), null);
        if (conn.RighteousFire is { } burning)
        {
            burning.RingAt = at;
            _ = RunRingFollow(remote, stream, ciphers, conn, burning);
        }
    }

    static async Task EndRighteousFire(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, string why)
    {
        RighteousFireState? rf;
        List<(uint Monster, ushort Buff)> auras;
        lock (conn.Monsters)
        {
            if (ProjectileOutputGuard.Value is { } guard && !guard()) return;
            rf = conn.RighteousFire;
            conn.RighteousFire = null;
            auras = rf is null ? [] : [.. rf.Auras.Select(kv => (kv.Key, kv.Value))];
        }
        if (rf is null) return;
        await Send(remote, stream, ciphers, InstanceEntities.RemoveBuff(PlayerId, rf.Buff), $"0x01c2 player buff {rf.Buff}: righteous_fire ends ({why})");
        await Send(remote, stream, ciphers, InstanceEntities.RemoveBuff(PlayerId, rf.SelfAura), null);
        if (rf.Ring is uint ring)
            await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(ring), $"0x02bb remove effect 0x{ring:x} (Righteous Fire's ring)");
        foreach (var (monster, buff) in auras)
            await Send(remote, stream, ciphers, InstanceEntities.RemoveBuff(monster, buff), null);
    }

    // The slower player tick: RF on nearby enemies, suppression conditions, waypoints, auto-loot and quests.
    // Continuous player pool changes are handled by RunResourceRecovery instead.
    static async Task RunPlayerTick(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        if (conn.Closed || conn.RageRetired) return;
        double last = conn.Clock.Elapsed.TotalSeconds;
        int suppression = Suppression(conn).Chance;
        var criticalContext = CriticalContext(conn);
        var leechContext = LeechContext(conn);
        var ailments = conn.ElementalAilments.Snapshot(last);
        while (!conn.Closed && !conn.RageRetired)
        {
            await Task.Delay(PlayerTickMs);
            if (conn.Closed || conn.RageRetired) break;
            double tickNow = conn.Clock.Elapsed.TotalSeconds;
            double seconds = tickNow - last;
            last = tickNow;
            if (conn.LeavingArea || conn.Paused || seconds <= 0) continue;
            await ServerMods.Tick(PlayerOf(conn), seconds);
            if (LeechContext(conn) is var currentLeech && currentLeech != leechContext)
            {
                leechContext = currentLeech;
                await SendCharacterStats(remote, stream, ciphers, conn, "leech or full Energy Shield conditions changed");
            }
            // Keep the selected skill's conditional luck virtuals current across live Life/Focus boundaries too.
            // Combat still samples the owner at the accepted contact, independently of this panel refresh cadence.
            if (CriticalContext(conn) is var currentCritical && currentCritical != criticalContext)
            {
                criticalContext = currentCritical;
                await SendCharacterStats(remote, stream, ciphers, conn, "critical strike conditions changed");
            }
            if (conn.ElementalAilments.Snapshot(tickNow) is var nextAilments && nextAilments != ailments)
            {
                ailments = nextAilments;
                await SendCharacterStats(remote, stream, ciphers, conn, "elemental ailments changed");
            }
            await SendAntiquarian(remote, stream, ciphers, conn);
            await SendRecentEnemyHits(remote, stream, ciphers, conn);
            await PushOutOfCollision(remote, stream, ciphers, conn);
            await BurnIgnited(remote, stream, ciphers, conn);
            var rf = conn.RighteousFire;
            if (conn.PlayerLife == 0)
            {
                await EndAurasOnDeath(remote, stream, ciphers, conn);
                if (rf is not null) await EndRighteousFire(remote, stream, ciphers, conn, "death");
                continue;
            }
            if (rf is not null && !InGracePeriod(conn)) await BurnAround(remote, stream, ciphers, conn, rf, seconds);
            // The character panel's Spell Suppression chance again (0x1b3 with only its stats) when a condition changed
            // it: full life lost or regained, a walk started or ended, an enemy hit 4 s ago.
            if (Suppression(conn).Chance is var chance && chance != suppression)
            {
                suppression = chance;
                await Send(remote, stream, ciphers, InstanceEntities.StatsUpdate(PlayerId, [.. SuppressionChanceKeys.Select(k => (k, chance))]),
                    $"0x01b3 player Spell Suppression chance {chance}%");
            }
            lock (conn.Monsters)
                if (HardcoreProgressBlocked(conn)) continue;
            if (conn.Layout.Waypoint is { } waypoint
                && Dist(conn.Walk.Position(conn.Clock.Elapsed.TotalSeconds), (waypoint.GridX, waypoint.GridY)) <= WaypointActivationRange)
                await ActivateWaypoint(remote, stream, ciphers, conn, "the player came near");
            // Gold within the configured auto-loot radius is picked up without a click.
            var at = conn.Walk.Position(conn.Clock.Elapsed.TotalSeconds);
            foreach (var (id, gold) in conn.Instance.Ground.Within(at.X, at.Y, SystemSettings.Default.GoldAutoLootRadius, IsGold))
            {
                await PickUpGold(remote, stream, ciphers, conn, id, gold, "auto-looted");
                lock (conn.Monsters)
                    if (HardcoreProgressBlocked(conn)) break;
            }
            lock (conn.Monsters)
                if (HardcoreProgressBlocked(conn)) continue;
            // Quests that start when the player comes near a monster (a1q1: Hillock, "Kill Hillock").
            if (Quests.Watched(conn.Layout.Id) is { Count: > 0 } watched)
            {
                double now = conn.Clock.Elapsed.TotalSeconds;
                List<string> seen;
                lock (conn.Monsters)
                    seen = [.. conn.Monsters.Values.Where(m => !m.Dead && m.Life > 0 && m.Variety is { } v && watched.Contains(v.Id)
                        && Dist(m.Walk.Position(now), at) <= Quests.SightRange).Select(m => m.Variety!.Id).Distinct()];
                foreach (string variety in seen) await SetQuestFlags(remote, stream, ciphers, conn, Quests.OnMonsterSeen(conn.Layout.Id, variety));
            }
            // a1q4: coming near the Glyph Wall ("Investigate the Mud Flats for a way to lower the water").
            if (conn.Glyphs?.Wall is { } glyphWall && !conn.HasQuestFlag(GlyphQuest.SeenGlyphWall)
                && Dist(at, (glyphWall.X, glyphWall.Y)) <= Quests.SightRange)
                await SetQuestFlags(remote, stream, ciphers, conn, [GlyphQuest.SeenGlyphWall]);
            // a1q4: the HaveGlyph flags follow the glyphs carried (dropped, sold, stashed ones too), about once a second.
            if (conn.Clock.Elapsed.TotalSeconds >= conn.GlyphSyncAt)
            {
                conn.GlyphSyncAt = conn.Clock.Elapsed.TotalSeconds + 1;
                await SyncGlyphs(remote, stream, ciphers, conn);
            }
        }
    }

    // One writer for continuous recovery: regeneration, flask recovery and the player's RF burn.
    // The game clock excludes pause; scheduling/network delays change the step size, never the per-second rate.
    static async Task RunResourceRecovery(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        double lifeCarry = 0, manaCarry = 0, burnCarry = 0, last = conn.Clock.Elapsed.TotalSeconds;
        double ratesAt = double.NegativeInfinity, lifeRate = 0, manaRate = 0, burnPerSecond = 0;
        double nextVitalsSave = last + 5;
        Dictionary<uint, int> recoveryStats = [];
        int RecoveryStat(string name) => Stats.Key(name) is uint key ? recoveryStats.GetValueOrDefault(key) : 0;
        var cachedRf = conn.RighteousFire;
        while (!conn.Closed && !conn.RageRetired)
        {
            await Task.Delay(RecoveryStepMs);
            if (conn.Closed || conn.RageRetired) break;
            await EndGracePeriod(remote, stream, ciphers, conn);
            var rf = conn.RighteousFire;
            uint lifeBefore, lifeAfter, manaBefore, manaAfter, manaCap;
            bool dead, instantLifeChanged, instantManaChanged;
            List<Flasks.Effect> ended;
            double now;
            int? ailmentDeathEpoch = null;
            using (ProjectileMutation(conn))
            {
                // Commit earlier projectile hits before advancing leech/recovery to this resource boundary.
                now = ProjectileWorldTime(conn);
                if (conn.Closed || conn.RageRetired) break;
                if (conn.LeavingArea)
                {
                    // A failed transfer can resume this connection. Do not catch up old cached rates through loading.
                    // These sessions accrue independently of 'last'; settle and discard their loading-time recovery.
                    DiscardLoadingRecovery(conn, now);
                    last = now;
                    ratesAt = double.NegativeInfinity;
                    continue;
                }
                if (conn.Ruthless && now >= nextVitalsSave)
                {
                    PersistRuthlessVitals(conn, bestEffort: true);
                    nextVitalsSave = now + 5;
                }
                double seconds = now - last;
                if (seconds <= 0) continue;
                // Stat aggregation stays at the old 250 ms cadence, rather than rebuilding the panel 60 times/s.
                if (now - ratesAt >= PlayerTickMs / 1000.0 || rf != cachedRf)
                {
                    lifeRate = LifeRegenPerSecond(conn);
                    manaRate = ManaRegenPerSecond(conn);
                    recoveryStats = CharacterPlayerStats(conn);
                    burnPerSecond = rf is null ? 0 : DamageModel.Mitigate((conn.PlayerMaxLife * (double)rf.BurnPctPerMinute + conn.Pools.EnergyShield * (double)rf.EnergyShieldBurnPctPerMinute) / 6000.0,
                        Resistance(conn, DamageType.Fire), MaxResistance(conn, DamageType.Fire));
                    (ratesAt, cachedRf) = (now, rf);
                }
                double recoveryFrom = last;
                double life, mana;
                (life, mana, ended) = conn.FlaskEffects.Advance(last, now);
                last = now;
                lifeBefore = conn.PlayerLife;
                manaBefore = conn.PlayerMana;
                manaCap = (uint)UnreservedMana(conn);
                var leech = LeechOf(conn);
                instantLifeChanged = leech.InstantLifeChanged;
                instantManaChanged = leech.InstantManaChanged;
                leech.InstantLifeChanged = leech.InstantManaChanged = false;
                dead = lifeBefore == 0;
                if (dead)
                {
                    lifeCarry = manaCarry = burnCarry = 0;
                    FortificationOf(conn).Clear();
                    leech.Clear(now);
                    conn.EnergyShield.Empty();
                    ClearAntiquarianOnDeath(conn);
                    lifeAfter = lifeBefore;
                    manaAfter = manaBefore;
                }
                else
                {
                    leech.ClearFull(now, lifeBefore >= conn.PlayerMaxLife, manaBefore >= manaCap,
                        conn.EnergyShield.Current >= conn.EnergyShield.Maximum);
                    var leeched = leech.TakeRecovery(now);
                    burnCarry += (InGracePeriod(conn) ? 0 : burnPerSecond * conn.ElementalAilments.DamageTakenFactor(now)) * seconds;
                    int burnWhole = (int)Math.Truncate(burnCarry); burnCarry -= burnWhole;
                    int lifeBurn = conn.EnergyShield.Damage(burnWhole, false, now);
                    conn.EnergyShield.Advance(recoveryFrom, now, RecoveryStat, true);
                    conn.EnergyShield.Gain(leeched.EnergyShield);
                    double fortificationLife = FortificationOf(conn).TakeRecovery(conn.Clock.Elapsed.TotalSeconds);
                    conn.PlayerLife = lifeAfter = RecoveryPool.Advance(lifeBefore, conn.PlayerMaxLife,
                        life + lifeRate * seconds + fortificationLife + leeched.Life + conn.Antiquarian.TakeRecovery(now)
                            + conn.EnduringCryRecovery.Take(now) - lifeBurn, ref lifeCarry, minimum: 1);
                    AdvancePlayerAilmentsLocked(conn, recoveryFrom, now, recoveryStats);
                    lifeAfter = conn.PlayerLife;
                    if (lifeAfter == 0) ailmentDeathEpoch = conn.IncomingHitEpoch;
                    conn.PlayerMana = manaAfter = RecoveryPool.Advance(manaBefore, manaCap,
                        mana + manaRate * seconds + leeched.Mana, ref manaCarry);
                    leech.ClearFull(now, lifeAfter >= conn.PlayerMaxLife, manaAfter >= manaCap,
                        conn.EnergyShield.Current >= conn.EnergyShield.Maximum);
                }
            }
            if (ailmentDeathEpoch is int lethalEpoch)
                await PlayerDiedAtEpoch(remote, stream, ciphers, conn, "0x0055 lethal elemental ailment", lethalEpoch);
            await SendPlayerEnergyShield(remote, stream, ciphers, conn);
            if (dead)
            {
                await EndFlaskEffects(remote, stream, ciphers, conn, [.. ended, .. conn.FlaskEffects.End(_ => true)], "death");
                continue;
            }
            if (rf is not null && lifeAfter == 1 && conn.EnergyShield.Current == 0)
                await EndRighteousFire(remote, stream, ciphers, conn, "one life and no energy shield");
            if ((lifeAfter != lifeBefore || instantLifeChanged) && ailmentDeathEpoch is null) await Send(remote, stream, ciphers, InstanceProtocol.LifeUpdate(PlayerId, lifeAfter), null);
            if (manaAfter != manaBefore || instantManaChanged) await SendPlayerMana(remote, stream, ciphers, conn, null);
            bool lifeFull = lifeAfter == conn.PlayerMaxLife, manaFull = manaAfter == manaCap;
            ended.AddRange(conn.FlaskEffects.End(e => (e.LifePerSecond == 0 || lifeFull) && (e.ManaPerSecond == 0 || manaFull)));
            ended = conn.FlaskEffects.KeepRunning(ended, lifeFull, manaFull, now);
            if (ended.Count > 0)
                await EndFlaskEffects(remote, stream, ciphers, conn, ended, $"life {lifeAfter}/{conn.PlayerMaxLife}, mana {manaAfter}/{manaCap}");
        }
    }

    // Called under the combat lock. Advance deadlines and consume accrued recovery without paying it into pools.
    internal static void DiscardLoadingRecovery(ClientState conn, double now)
    {
        LeechOf(conn).TakeRecovery(now);
        FortificationOf(conn).TakeRecovery(now);
        conn.Antiquarian.TakeRecovery(now);
        conn.EnduringCryRecovery.Take(now);
    }

    // The ended flask effects' buffs come off the player (S→C 0x1c2).
    static async Task EndFlaskEffects(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, List<Flasks.Effect> ended, string why)
    {
        foreach (var e in ended)
            if (FlaskBuffs && e.Buff != 0) await Send(remote, stream, ciphers, InstanceEntities.RemoveBuff(PlayerId, e.Buff), $"0x01c2 player buff {e.Buff}: flask {e.Slot} effect ends ({why})");
            else Console.WriteLine($"[gateway] {remote} flask {e.Slot} effect ends ({why})");
    }

    // Righteous Fire's damage to the hostile monsters within its radius for one tick, with their aura buff on while they
    // are inside.
    static async Task BurnAround(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, RighteousFireState rf, double seconds)
    {
        double now = conn.Clock.Elapsed.TotalSeconds;
        double perSecond = (conn.PlayerMaxLife * (double)rf.DamagePctPerMinute + conn.Pools.EnergyShield * (double)rf.EnergyShieldDamagePctPerMinute) / 6000.0;
        var playerStats = CharacterPlayerStats(conn);
        if (Stats.Key("delirium_aura_damage_over_time_+%_final") is uint moreDot)
            perSecond *= Math.Max(0, 1 + playerStats.GetValueOrDefault(moreDot) / 100.0);
        perSecond *= ChargeDamageMore(playerStats);
        var damageOverTime = new SkillDamageContext(SkillHitKind.Spell, playerStats)
            .OverTime(new DamageOverTime(DamageType.Fire, perSecond, seconds));
        var hits = new List<(uint Id, SpawnedMonster M, uint Life, int Damage)>();
        var entered = new List<(uint Id, ushort Buff)>();
        var left = new List<(uint Id, ushort Buff)>();
        using (ProjectileMutation(conn))
        {
            now = ProjectileWorldTime(conn);
            var here = conn.Walk.Position(now);
            foreach (var (id, m) in conn.Monsters)
            {
                bool inside = !m.Dead && m.Life > 0 && m.Hostile && Dist(here, m.Walk.Position(now)) <= rf.Radius + m.StopRange;
                if (!inside)
                {
                    if (rf.Auras.Remove(id, out ushort gone)) left.Add((id, gone));
                    continue;
                }
                if (!rf.Auras.ContainsKey(id)) { rf.Auras[id] = conn.NextBuff; entered.Add((id, conn.NextBuff++)); }
                var (damage, carry) = MonsterBurn(m, damageOverTime, seconds);   // its mods' fire resistance
                m.Burn = carry;
                if (damage == 0) continue;
                (m.Life, bool stageStarts) = TakeDamageLocked(m, damage);
                m.Aggro = m.Life > 0;
                if (m.Life == 0)
                {
                    m.Dead = true;
                    CollectVaalSoulAtDeathLocked(conn, m);
                    if (rf.Auras.Remove(id, out ushort b)) left.Add((id, b));
                }
                hits.Add((id, m, m.Life, damage));
                if (stageStarts) CommitBossStage(remote, stream, ciphers, conn, id, m, now);
            }
        }
        foreach (var (id, buff) in entered)
            await Send(remote, stream, ciphers, InstanceEntities.AddBuff(id, buff, RighteousFireAuraBuffRow, PlayerId, rf.Skill, visual: rf.AuraVisual), null);
        foreach (var (id, buff) in left)
            await Send(remote, stream, ciphers, InstanceEntities.RemoveBuff(id, buff), null);
        foreach (var (id, m, life, damage) in hits)
        {
            if (life == 0)
                await Send(remote, stream, ciphers, InstanceEntities.StatsUpdate(id, [(StatKeys.HideMiniLifeBar, 1)]), null);
            await Send(remote, stream, ciphers, InstanceProtocol.LifeUpdate(id, life),
                life == 0 ? $"0x01b1 monster 0x{id:x} ({m.Label}) burnt to death by Righteous Fire" : null);
            if (life == 0) await MonsterKilled(remote, stream, ciphers, conn, id, m);
        }
    }

    // The quest flags the permanent Kitava resistance penalty mirrors (ResistancePenalty): the Act 5 pair and the
    // Act 10 pair. A change to any of them rebuilds the pools (SetQuestFlags), because the overcapped-resistance
    // modifiers (ResistanceOvercap) read the penalty.
    internal static readonly string[] ResistancePenaltyAct5 = ["a5q6-CompletedBoss", "SinOnKitavaDefeat"],
        ResistancePenaltyAct10 = ["a10q3-SeenBoss", "a10q3-UsedRewardBook"];

    // The permanent resistance penalty from Kitava: -30% after the Act 5 encounter and another -30% after the final
    // Act 10 Kitava. It is a quest penalty, not a buff/aura: the client keeps it through its quest flags, and the server
    // mirrors it from the same flags. (In the 3.29.3b tables the flags are a5q6-CompletedBoss and a10q3-SeenBoss /
    // a10q3-UsedRewardBook; a9q5-KilledBoss is the Act 9 boss and does not grant it.)
    static int ResistancePenalty(ClientState conn)
    {
        bool act5, act10;
        lock (conn.Monsters)
        {
            act5 = ResistancePenaltyAct5.Any(conn.QuestFlags.Contains);
            act10 = ResistancePenaltyAct10.Any(conn.QuestFlags.Contains);
        }
        return (act5 ? -30 : 0) + (act10 ? -30 : 0);
    }

    // Whether a quest-flag change moves the Kitava resistance penalty.
    static bool ChangesResistancePenalty(IEnumerable<string> changed) =>
        changed.Any(ResistancePenaltyAct5.Concat(ResistancePenaltyAct10).Contains);

    // The player's final resistance for one damage type: the passive/gear modifier plus the Kitava quest penalty.
    static int Resistance(ClientState conn, DamageType type)
    {
        int modifier = type switch
        {
            DamageType.Fire => conn.Pools.FireResistance,
            DamageType.Cold => conn.Pools.ColdResistance,
            DamageType.Lightning => conn.Pools.LightningResistance,
            DamageType.Chaos => conn.Pools.ChaosResistance,
            _ => 0,
        };
        return type == DamageType.Physical ? modifier : modifier + ResistancePenalty(conn);
    }

    // The per-type maximum from Character.ot's 75 plus the passive/gear maximum-resistance modifiers, hard-capped at 90.
    static int MaxResistance(ClientState conn, DamageType type) => type switch
    {
        DamageType.Fire => conn.Pools.FireMaxResistance,
        DamageType.Cold => conn.Pools.ColdMaxResistance,
        DamageType.Lightning => conn.Pools.LightningMaxResistance,
        DamageType.Chaos => conn.Pools.ChaosMaxResistance,
        _ => DamageModel.DefaultMaxResistance,
    };

    // Damage immunities as the damage model sees them, from the same sources the panel's "Immune to X" rows use.
    static HashSet<DamageType> PlayerImmunities(ClientState conn)
    {
        if (InGracePeriod(conn)) return [.. Enum.GetValues<DamageType>()];
        var baseStats = new Dictionary<uint, int>();
        foreach (var (key, value) in BaseStats(conn)) baseStats[key] = value;
        lock (conn.Monsters)
            foreach (var (key, value) in conn.PlayerStats) baseStats[key] = baseStats.GetValueOrDefault(key) + value;
        return DamageImmunities.From(name => Stats.Key(name) is uint key ? baseStats.GetValueOrDefault(key) : 0);
    }

    // Spell Suppression: chance in 0..100 with the conditional passives that hold now (full life, full energy shield,
    // moving, an enemy hit in the last 4 s: PoB's "recently"), the reduction on a successful suppress (40% by default)
    // and its lucky or unlucky roll (PoB's suppressRolls).
    static (int Chance, int Effect, bool Lucky, bool Unlucky) Suppression(ClientState conn)
    {
        double now = conn.Clock.Elapsed.TotalSeconds;
        bool fullLife, fullEnergyShield, hitRecently;
        lock (conn.Monsters)
        {
            fullLife = conn.PlayerLife > 0 && conn.PlayerLife >= conn.PlayerMaxLife;
            fullEnergyShield = conn.PlayerLife > 0 && conn.EnergyShield.Full;
            hitRecently = now - conn.LastEnemyHitAt <= RecentlySeconds;
        }
        return (CharacterStats.SuppressionChance(conn.Pools, fullLife, moving: now < conn.Walk.ArrivesAt, hitRecently,
                fullEnergyShield),
            Math.Max(0, conn.Pools.SpellSuppressionEffect),
            conn.Pools.SpellSuppressionLucky, conn.Pools.SpellSuppressionUnlucky);
    }

    static DamageType? ParseDamageType(string text) => text.ToLowerInvariant() switch
    {
        "fire" => DamageType.Fire,
        "cold" => DamageType.Cold,
        "lightning" or "light" => DamageType.Lightning,
        "chaos" => DamageType.Chaos,
        "physical" or "phys" => DamageType.Physical,
        _ => null,
    };

    // The death screen's buttons (C→S 0x54). Both hide the screen (0x55 with bit1) and give the player full life; "at
    // checkpoint" revives next to the area's activated waypoint, else at its arrival point (teleport 0x1b7, AreaLayout.Checkpoint),
    // "in town" in the character's last town (an area change;
    // in that town it is the same as the checkpoint).
    static async Task Resurrect(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, bool inTown)
    {
        // The native button is hidden for HC, but a forged 0x54 must not revive the old character in its source league.
        if (conn.Hardcore || conn.HardcoreDeathRecorded) return;
        lock (conn.Monsters)
        {
            if (conn.PlayerLife > 0) return;
            conn.InvalidateIncomingHits();
            LeechOf(conn).Clear(conn.Clock.Elapsed.TotalSeconds);
            conn.PlayerLife = conn.PlayerMaxLife;
            conn.EnergyShield.SetMaximum(conn.Pools.EnergyShield, refill: true);
            conn.HitRecovery.Clear();
            conn.Ward.SetMaximum(conn.Pools.Ward); conn.Ward.Restore();
            conn.AuraDeathHandled = false;
            conn.RuthlessDeathHandled = false;
            conn.PlayerMana = (uint)UnreservedMana(conn);
            foreach (var m in conn.Monsters.Values) m.Aggro = false;
        }
        Console.WriteLine($"[gateway] {remote} <= 0x0054 resurrect {(inTown ? "in town" : "at checkpoint")}");
        await Send(remote, stream, ciphers, InstanceProtocol.DeathScreen(0x02), "0x0055 hide the death screen");
        // The flasks are full again after a death.
        await GainFlaskCharges(remote, stream, ciphers, conn, int.MaxValue, "resurrected");
        if (inTown && conn.LastTown != 0 && conn.Layout.Hash != conn.LastTown)
        {
            // The new area's player object is created with full life.
            await ChangeArea(remote, stream, ciphers, conn, new PendingTransfer(conn.LastTown, 0, false, AtSpawn: true), "resurrect in town");
            return;
        }
        // Revive first: a teleport sent while the player is dead left them where they died (live 2026-10-17).
        await Send(remote, stream, ciphers, InstanceProtocol.LifeUpdate(PlayerId, conn.PlayerMaxLife),
            $"0x01b1 player revived with {conn.PlayerMaxLife} life");
        await SendPlayerMana(remote, stream, ciphers, conn, $"0x01b1 player mana {conn.PlayerMana}");
        // Death may have published false full-pool conditions while the player ticker kept its pre-death chance.
        // Publish the restored life/ES conditions explicitly even when that cached chance is unchanged.
        await SendCharacterStats(remote, stream, ciphers, conn, "resurrected");
        // The checkpoint: the area's waypoint once activated, else where the player entered the area, as in the real game
        // before any checkpoint is activated. Checkpoint objects and boss arenas (revive at the arena's door) are not done.
        bool activated;
        lock (conn.Waypoints)
            activated = WaypointCatalog.Areas.Find(conn.Layout.Hash) is { } entry && conn.Waypoints.Contains(entry.Id);
        var (x, y) = conn.Layout.Checkpoint(activated);
        conn.Walk.Teleport(x, y, conn.Clock.Elapsed.TotalSeconds);
        await Send(remote, stream, ciphers, InstanceProtocol.Teleport(PlayerId, x, y),
            $"0x01b7 player to the checkpoint ({x}, {y}): {(activated && conn.Layout.Waypoint is not null ? "the activated waypoint" : "the arrival point")}");
        await Send(remote, stream, ciphers, InstanceEntities.StatsUpdate(PlayerId, [(StatKeys.HideMiniLifeBar, 0)]),
            "0x01b3 player hide_mini_life_bar = 0");
    }

    // The player in its class: the Characters.dat row's object type (Metadata/Characters/StrInt/StrInt for the Templar), model
    // (AOFile), class byte (IntegerId), attributes and unarmed attack; the name is the character's.
    static PlayerObject PlayerForCreation(ClientState conn)
    {
        var (layout, name, classRow, pools) = (conn.Layout, conn.Character, conn.ClassRow, conn.Pools);
        var cls = SkillsFor(conn).ClassFor(classRow);
        // Keep the ticket's remaining Rage timers frozen while the destination loads (before 0x7d).
        RestoreRage(conn);
        RestoreVaalSouls(conn);
        RestoreAntiquarian(conn);
        conn.Ward.SetMaximum(conn.Pools.Ward);
        pools = conn.Pools;
        var stats = CharacterPlayerStatsSnapshot(conn, out _, out var panelSources, capturePanelSources: true);
        var characterInputs = new Dictionary<uint, int>(stats);
        // The equipped weapon's attack with its own local mods (WeaponFor), as SendSkills sends it after an equipment change:
        // before, every area started with the class's unarmed attack, so a weapon's added damage and attack speed showed only
        // after taking it off and putting it back. After the stats, which mark the inactive items WeaponFor skips.
        var weapon = WeaponFor(conn);
        foreach (var (k, v) in CosmeticStats(conn).Where(s => !ActorScale.IsInput(s.Stat))) stats.TryAdd(k, v);
        var statList = stats.Select(kv => (kv.Key, kv.Value)).ToList();
        var weaponStats = WeaponStats(conn, weapon, stats);
        // The new player object starts with exactly these (PlayerStatsWithCleared clears from here).
        lock (conn.Monsters) conn.SentStatKeys = [.. statList.Select(s => s.Item1), .. weaponStats.Select(s => s.Item1)];
        int reservedMana = ReservedMana(conn);
        var player = new PlayerObject(string.IsNullOrEmpty(name) ? "Exile" : name, ClassIndex: PackedClass(conn), GridX: layout.SpawnX, GridY: layout.SpawnY, Life: conn.PlayerLife, EnergyShield: conn.EnergyShield.Current,
            Mana: (uint)Math.Min(conn.PlayerMana, (uint)Math.Max(0, (int)pools.Mana - reservedMana)), ManaReserved: (uint)reservedMana,
            Experience: conn.Experience,
            AnimatedObject: Environment.GetEnvironmentVariable("Game_PLAYER_AO") ?? cls.AnimatedObject,
            Skills: [.. SkillsFor(conn).PlayerDefaults(weaponStats), .. GemSkills(conn, [.. weaponStats, .. statList], characterInputs, panelSources!)], Stats: [.. weaponStats, .. statList],
            Equipped: Looks(conn, PlayerInventories.For(string.IsNullOrEmpty(name) ? "Exile" : name, ItemsFor(conn))),
            QuestFlags: PlayerQuestFlags(conn), Hideout: HomeHideout(conn)?.Hash16 ?? 0, LeagueQuestFlags: [.. Quests.League.Bits(conn.LeagueQuestFlags)],
            Portrait: WornPortrait(conn));
        lock (conn.Monsters)
        {
            RefreshVaalSouls(conn);
            return player with { Souls = conn.VaalSouls.Read() };
        }
    }

    static async Task SendPlayer(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        RestoreFortification(conn);
        PrepareHideoutWaypoint(conn);
        var (layout, name, classRow) = (conn.Layout, conn.Character, conn.ClassRow);
        var cls = SkillsFor(conn).ClassFor(classRow);
        uint type = InstanceEntities.ObjectTypeHash(cls.ObjectType);
        conn.PeerType = type;
        const string creationLabel = "0x02b9 create current player";
        var messages = new List<(string Name, byte[] Bytes)>
        {
            ("0x01f6 campaign hideout unlocks", InstanceMicrotransactions.HideoutUnlocks(
                HideoutCatalog.OwnedRows.Where(row => row < 128).Select(row => (int)row))),
            ("0x0022 local player 0x1000", InstanceEntities.SetLocalPlayer(PlayerId)),
            (creationLabel, []),
        };
        // The player's inventories (id = row + 1), needed by UI built on entering (flask bar → row 11), with the character's items.
        var items = PlayerInventories.For(string.IsNullOrEmpty(name) ? "Exile" : name, ItemsFor(conn));
        if (ItemsFor(conn).Error is not null) Console.WriteLine($"[gateway] items: {ItemsFor(conn).Error}");
        foreach (var slot in InstanceInventories.PlayerDefaults)
        {
            var inside = items.Items(slot.Row);
            messages.Add(($"0x00c0 inventory {PlayerInventories.InventoryId(slot.Row)} row {slot.Row}" + (inside.Count > 0 ? $" items {inside.Count}" : ""),
                InstanceInventories.AddInventory(PlayerInventories.InventoryId(slot.Row), slot, items: inside)));
        }
        for (int preset = 0; preset < conn.AtlasPassives.Length; preset++)
            messages.Add(($"0x003d atlas passive preset {preset} ({conn.AtlasPassives[preset].Count}/{conn.AtlasPassivePoints} points)",
                AtlasSlots(conn, preset)));
        var (passives, passivesLabel) = PassivesMessage(conn);
        messages.Add((passivesLabel, passives));
        // Area transitions. Like the player they are queued by the world and created when 0x21 activates it.
        foreach (var (id, transition) in layout.AreaTransitions.Where(t => TransitionShown(conn, t.Transition)))
            messages.Add(($"0x02b9 create area transition 0x{id:x} {transition}",
                InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(transition.Type), transition.Blob())));
        foreach (var (id, effect) in layout.Effects)
            messages.Add(($"0x02b9 create transition effect 0x{id:x} {effect}",
                InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(effect.Type), effect.Blob())));
        if (layout.Waypoint is { } waypoint)
            messages.Add(($"0x02b9 create waypoint 0x{WaypointCatalog.ObjectId:x} {waypoint}",
                InstanceEntities.CreateObject(WaypointCatalog.ObjectId, InstanceEntities.ObjectTypeHash(WaypointObject.MetadataPath), waypoint.Blob())));
        if (Environment.GetEnvironmentVariable("Game_GATEWAY_ENTER") == "1")
        {
            var tabs = PersonalStashOf(conn).Records((int)StashSelectAffinity);
            bool secondSet = PlayerInventories.For(string.IsNullOrEmpty(name) ? "Exile" : name, ItemsFor(conn)).WeaponSet == 1;
            messages.Add(($"0x0021 enter area, {tabs.Count} stash tab(s), last {PersonalStashOf(conn).LastTab}, weapon set {(secondSet ? 2 : 1)}",
                InstanceProtocol.EnterArea(tabs, secondSet)));
            // The Delve data (0x228) in the Azurite Mine Encampment and the mine: the gauge and the Subterranean Chart
            // read the character's sulphite from it.
            if (layout.Id.StartsWith("Delve", StringComparison.Ordinal))
            {
                messages.Add(("0x0228 Delve resources and equipment", PlayerDelveData(conn)));
            }
            // The contents of every stash tab with currency in it (stackable items), as 0xc7 would send them (0xc0, no 0xc8:
            // the panel is not open). The client checks a shop price against the currency it knows of, the inventory's and
            // the loaded stash tabs' (3.29 vendors take it from the stash): without this a price was red, and no 0x88 came,
            // until the stash had been opened in that area (live 2026-10-23, Nessa in the hideout). Shop prices are in
            // currency (class StackableCurrency), which only a currency tab or a grid tab can hold: every other loaded tab
            // made each stash open slower by its item count (a full divination tab alone is 470 items, fragments 255), and
            // those tabs still load when the panel shows them (0xc7).
            var personal = PersonalStashOf(conn);
            for (int t = 0; t < personal.Count; t++)
            {
                var (slot, inside) = personal.Inventory(t);
                if (!PreloadsForPrices(personal.TabType(t), inside, ItemsFor(conn)) || !conn.StashTabsSent.Add(t)) continue;
                messages.Add(($"0x00c0 stash tab {t} inventory {personal.InventoryOf(t)}: {inside.Count} item(s) (currency for shop prices)",
                    InstanceInventories.AddInventory(personal.InventoryOf(t), slot, 0, inside)));
            }
            // Put the player in the wake zone so it is drawn and its actions/movement run.
            messages.Add(("0x01a9 player enters wake zone", InstanceProtocol.EnterWakeZone(PlayerId)));
            foreach (var (id, _) in layout.AreaTransitions.Where(t => TransitionShown(conn, t.Transition)))
                messages.Add(($"0x01a9 area transition 0x{id:x} enters wake zone", InstanceProtocol.EnterWakeZone(id)));
            foreach (var (id, _) in layout.Effects)
                messages.Add(($"0x01a9 transition effect 0x{id:x} enters wake zone", InstanceProtocol.EnterWakeZone(id)));
            if (layout.Waypoint is not null) messages.Add(($"0x01a9 waypoint 0x{WaypointCatalog.ObjectId:x} enters wake zone", InstanceProtocol.EnterWakeZone(WaypointCatalog.ObjectId)));
            // The attributes once more as a stats update. UI built on entering caches them (0x141e802d0 reads the Stats
            // component's getter); live 2026-10-18 the cache held 0 while the getter returned 14/14/32.
            messages.Add(("0x01b3 class attributes", []));
        }
        foreach (var (label, bytes) in messages)
            if (label == creationLabel)
                await Send(remote, stream, ciphers, [], label, atGate: () =>
                {
                    lock (conn.Monsters)
                    {
                        if (conn.Closed || conn.LeavingArea || conn.RageRetired) return null;
                        ReconcileOwnAuras(conn, validateSkills: true);
                        var player = PlayerForCreation(conn);
                        conn.PeerTemplate = player;
                        conn.ClientSkillIds = [.. player.Skills!.Select(s => s.Id)];
                        conn.SentVaalSouls.Clear();
                        foreach (var budget in player.Souls ?? [])
                            conn.SentVaalSouls.Add(budget.ActiveSkillsRow, budget);
                        conn.PresentedAuras.Clear();
                        conn.AuraStatsDirty = false;
                        conn.PlayerObjectCreated = true;
                        return InstanceEntities.CreateObject(PlayerId, conn.PeerType, player.Blob());
                    }
                });
            else if (label == "0x01b3 class attributes")
                await SendPlayerStatSnapshot(remote, stream, ciphers, conn,
                    () => PlayerStatsUpdateAtGate(conn, PlayerStatsWithCleared(conn)), label);
            else await Send(remote, stream, ciphers, bytes, label);
        // An area change creates a new player object, so the running auras' buffs must be put on it again.
        await SendOwnAuraChanges(remote, stream, ciphers, conn, replay: true);
    }

}
