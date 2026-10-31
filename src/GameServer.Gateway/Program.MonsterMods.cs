using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using DamageModel = GameServer.Core.World.DamageModel;
using DamagePacket = GameServer.Core.World.DamagePacket;
using DamageOverTime = GameServer.Core.World.DamageOverTime;
using DamageType = GameServer.Core.World.DamageType;
using DamageTypes = GameServer.Core.World.DamageTypes;

// Magic and rare monster mods (MonsterMods.cs, Agents/docs/protocol/14-monster-packs.md §5): the rolls and the hooks the
// spawn, the monsters' hits, the player's hits and the regeneration tick call.
partial class Program
{
    // Monster-domain Mods.dat rows, their names and effects; loaded with the first magic or rare monster.
    static readonly Lazy<MonsterModCatalog> MonsterModRolls = new(() => new MonsterModCatalog(Items.Mods));

    // Per instance, the mods its magic monsters share: "all monsters in an instance that are the same or very similar
    // will have the same magic monster modifier" (3.19.0 patch notes). Very similar: the same name (The Coast's
    // Drowned are several MonsterVarieties rows), as long as the affix can roll on it (MonsterModCatalog.SharedMagic).
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<InstanceState, Dictionary<string, MonsterModSet>> MagicModsOf = new();

    /// <summary>The mods a new monster gets: <paramref name="kept"/> (a monster of a kept instance, a /mob_mods
    /// choice) when it has any, else freshly rolled for its rarity. Area packs pass <paramref name="alone"/> false;
    /// their magic monsters share one roll per monster name in the instance.</summary>
    internal static MonsterModSet MonsterModsFor(ClientState conn, MonsterCatalog.Variety variety, byte level, MonsterRarity rarity,
        MonsterModSet? kept, bool alone)
    {
        if (kept is { Mods.Count: > 0 } || kept is not null && rarity == MonsterRarity.Normal) return kept;
        var rolls = MonsterModRolls.Value;
        if (rarity != MonsterRarity.Magic || alone) return rolls.Roll(variety, level, rarity, Random.Shared, alone);
        var shared = MagicModsOf.GetValue(conn.Instance, _ => []);
        lock (shared) return rolls.SharedMagic(shared, variety, level, Random.Shared);
    }

    /// <summary>The stats the client needs for the mods (it applies no modifier of its own to a monster, 14 §2): the
    /// rarity's life stats and the final maximum_life its life bars read, movement_velocity_+% for its walk (the server
    /// walks it at the same speed), actor_scale_+% for its size and life_regeneration_rate_per_minute_%.</summary>
    internal static IEnumerable<(uint Key, int Value)> MonsterModClientStats(MonsterModEffects e, uint maxLife, int baseLife)
    {
        if (e["monster_life_+%_final_from_rarity"] is int rarity and not 0) yield return (StatKeys.MonsterLifeFromRarityPct, rarity);
        if (e[MonsterModCatalog.RarityTableLifeStat] is int table and not 0 && Stats.Key(MonsterModCatalog.RarityTableLifeStat) is uint tk)
            yield return (tk, table);
        if (maxLife != baseLife) yield return (StatKeys.MaximumLife, (int)maxLife);
        if (e.MovementPct != 0) yield return (StatKeys.MovementVelocityPct, e.MovementPct);
        if (e.ActorScalePct != 0 && Stats.Key("actor_scale_+%") is uint scale) yield return (scale, e.ActorScalePct);
        if (e["life_regeneration_rate_per_minute_%"] is int regen and not 0 && Stats.Key("life_regeneration_rate_per_minute_%") is uint rk)
            yield return (rk, regen);
    }

    /// <summary>The monster's own part of its hit on the player, after the roll and before the player's defences: its
    /// critical strike (only with a chance from its mods: of Deadliness; × Monster.ot's 130% multiplier less the
    /// player's <paramref name="selfCriticalReduction"/>, base_self_critical_strike_multiplier_-%, read only on a crit)
    /// and the increased damage per 10% of the player's missing life (Executioner's). Call under the Monsters lock.</summary>
    internal static DamagePacket MonsterModHit(SpawnedMonster m, DamagePacket rolled, uint playerLife, uint playerMaxLife, Random random,
        out bool critical, Func<int>? selfCriticalReduction = null)
    {
        var e = m.Mods.Effects;
        critical = e.CriticalChance > 0 && random.Next(10_000) < e.CriticalChance;
        double factor = critical ? MonsterModEffects.CriticalMultiplierOnPlayer(selfCriticalReduction?.Invoke() ?? 0) / 100.0 : 1;
        if (e.ExecutionerPct(playerLife, playerMaxLife) is int executioner and > 0)
        {
            int increased = e["damage_+%"] + e["physical_damage_+%"];
            factor *= (100.0 + increased + executioner) / (100 + increased);
        }
        return factor == 1 ? rolled : rolled.Scale(factor);
    }

    /// <summary>
    /// The damage a hit of the player deals to a monster, rolled: each type less the resistance the monster's mods give
    /// (capped at Monster.ot's 75%), the physical part less its physical damage reduction, then its damage taken
    /// (Berserking's reduction, Spell Resistant's less spell damage). Blocking is rolled before (<see cref="MonsterBlocks"/>).
    /// Base and mod resistance compose before the cap; penetration subtracts once after that cap.
    /// </summary>
    internal static int MonsterTakes(SpawnedMonster m, DamagePacket hit, Random random, bool spell,
        Penetration.PerType pen = default) => MonsterTakesRolled(m, hit.Roll(random), spell, pen);

    /// <summary>A monster's resistance to one type of the player's hit: base and mod resistance compose before
    /// Monster.ot's 75% cap; penetration subtracts once after that cap.</summary>
    internal static DamageModel.ResistanceValue MonsterHitResistance(SpawnedMonster m, DamageType type,
        Penetration.PerType pen)
    {
        var resistance = HitMitigation.Resistance(Monsters, m.Variety, m.Level, type);
        return resistance with { Value = resistance.Value + m.Mods.Effects.Resistance(type),
            Maximum = MonsterModEffects.MaxResistance, Penetration = pen.Of(type) };
    }

    // Already rolled: no extra RNG or per-component rounding, including ordinary bow contact snapshots.
    internal static int MonsterTakesRolled(SpawnedMonster m, DamagePacket rolled, bool spell, Penetration.PerType pen = default)
        => MonsterTakesMitigated(m, DamageModel.Mitigate(rolled, type => MonsterHitResistance(m, type, pen)), spell);

    /// <summary>
    /// The damage a resisted hit of the player deals to a monster: the physical part less its physical damage reduction,
    /// then its damage taken (Berserking's reduction, Spell Resistant's less spell damage, its shock and Glacial Wave's
    /// chill at <paramref name="now"/>; without a time those two do not count). Blocking is rolled before
    /// (<see cref="MonsterBlocks"/>).
    /// </summary>
    internal static int MonsterTakesMitigated(SpawnedMonster m, DamagePacket mitigated, bool spell, double now = double.NaN)
    {
        var e = m.Mods.Effects;
        double physical = mitigated[DamageType.Physical].Min;
        double total = mitigated.MinTotal - physical * e.PhysicalReduction / 100.0;
        // The player's debuffs on it add to its own increased damage taken (MonsterDamageTaken): his enemy auras' elemental
        // increase on fire, cold and lightning only.
        double factor = MonsterDamageTaken(m, spell, now);
        if (m.TakenFromPlayer.Elemental == 0) return (int)Math.Round(total * factor);
        double elementalDamage = mitigated[DamageType.Fire].Min + mitigated[DamageType.Cold].Min + mitigated[DamageType.Lightning].Min;
        return (int)Math.Round(total * factor + elementalDamage * (MonsterDamageTaken(m, spell, now, elemental: true) - factor));
    }

    internal static DamagePacket MonsterTakenComponents(SpawnedMonster m, DamagePacket mitigated, bool spell) =>
        mitigated.Select((type, range) => range.Scale(type == DamageType.Physical
            ? Math.Max(0, 1 - m.Mods.Effects.PhysicalReduction / 100.0) : 1)).Scale(m.Mods.Effects.DamageTaken(spell));

    /// <summary>The player's base_self_critical_strike_multiplier_-% (critical strikes against them deal less extra damage).</summary>
    static int SelfCriticalReduction(ClientState conn) =>
        Stats.Key("base_self_critical_strike_multiplier_-%") is uint key ? CharacterPlayerStats(conn).GetValueOrDefault(key) : 0;

    /// <summary>Whether the monster blocks a hit of the player (Stalwart's monster_base_block_%: attacks only). Rolled
    /// before anything of the hit happens: a blocked hit has no damage, impale, flinch or hit message.</summary>
    internal static bool MonsterBlocks(SpawnedMonster m, Random random, bool spell) =>
        !spell && m.Mods.Effects.BlockChance > 0 && random.Next(100) < m.Mods.Effects.BlockChance;

    /// <summary>A blocked projectile's damage (<see cref="MonsterBlocks"/>): the impact applies nothing.</summary>
    internal const int Blocked = -1;
    internal const int Evaded = -2;

    /// <summary>A contact's hit unless blocked or evaded; rejected contacts have no life or on-hit mutation.
    /// The caller holds the Monsters lock.</summary>
    internal static MonsterHit? HitUnlessBlockedLocked(ClientState conn, SpawnedMonster m, int damage) =>
        damage is Blocked or Evaded ? null : ApplyMonsterHitLocked(conn, m, damage);

    /// <summary>The critical strike multiplier a critical hit of the player has on this monster: 100% when it takes no
    /// extra damage from critical strikes (of Toughness).</summary>
    internal static int CriticalMultiplierOn(SpawnedMonster m, int multiplier) =>
        m.Mods.Effects.NoExtraCriticalDamage ? 100 : multiplier;

    /// <summary>One burn tick on a monster (Righteous Fire, fire damage over time), less <see cref="MonsterDotTaken"/>.</summary>
    internal static (int Damage, double Carry) MonsterBurn(SpawnedMonster m, DamageOverTime burn, double seconds,
        double now = double.NaN) =>
        (burn with { DamagePerSecond = burn.DamagePerSecond * MonsterDotTaken(m, burn.Type, now) }).Tick(seconds, m.Burn);

    /// <summary>The share of damage over time of this type a monster takes (Righteous Fire's burn, ignites): less its
    /// resistance (its type's base and its mods', as for hits, without penetration) and with its damage taken
    /// (<see cref="MonsterDamageTaken"/>: its shock and Glacial Wave's chill at <paramref name="now"/>, without a time
    /// they do not count, and the player's debuffs on it). Spell Resistant counts for hits only: its stat has no
    /// description saying it covers damage over time.</summary>
    internal static double MonsterDotTaken(SpawnedMonster m, DamageType type, double now = double.NaN)
    {
        var resistance = MonsterHitResistance(m, type, default);
        return (100 - DamageModel.Capped(resistance.Value, resistance.Maximum)) / 100.0
            * MonsterDamageTaken(m, spell: false, now, DamageTypes.IsElemental(type));
    }

    // Monsters with life regeneration (of the Hydra) regain life: every 0.5 s the server adds it and sends the new life
    // (0x1b1) of each one that changed, as it does for hits.
    static async Task RunMonsterRegeneration(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        const int StepMs = 500;
        double last = conn.Clock.Elapsed.TotalSeconds;
        try
        {
            while (!conn.Closed)
            {
                await Task.Delay(StepMs);
                double now = conn.Clock.Elapsed.TotalSeconds, seconds = now - last;
                last = now;
                if (conn.Paused || seconds <= 0) continue;
                var changed = RegenerateConnectionMonsters(conn, now, seconds);
                // Checked again in the send gate: a kill in between sends 0 first, and an older life after it would
                // revive the monster on the client.
                foreach (var (id, life) in changed)
                    await Send(remote, stream, ciphers, InstanceProtocol.LifeUpdate(id, life), null,
                        still: () => { lock (conn.Monsters) return RegenStillCurrent(conn.Monsters, id, life); });
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The connection closed while sending.
        }
    }

    /// <summary>Whether a regenerated life is still the monster's when its 0x1b1 goes out: alive and unchanged.</summary>
    internal static bool RegenStillCurrent(IReadOnlyDictionary<uint, SpawnedMonster> monsters, uint id, uint life) =>
        monsters.TryGetValue(id, out var m) && !m.Dead && m.Life == life;

    internal const string EnemyRegenerationOnHit = "enemy_life_regeneration_rate_+%_for_4_seconds_on_hit";

    /// <summary>The connection's regeneration step ending at <paramref name="now"/>, with the player's
    /// <see cref="EnemyRegenerationOnHit"/> (Devout of Ryslatha, Pools.EnemyRegenerationOnHitPct) on the enemies it hit in
    /// the last 4 s. The client does not step a monster's life from its regeneration stat (no life_recovery_per_minute is
    /// sent: re/recovery-timing.md), so the 0x1b1 updates alone show it.</summary>
    internal static List<(uint Id, uint Life)> RegenerateConnectionMonsters(ClientState conn, double now, double seconds)
    {
        int onHit = now - seconds - conn.LastEnemyHitAt <= RecentlySeconds ? conn.Pools.EnemyRegenerationOnHitPct : 0;
        using (ProjectileMutation(conn)) return RegenerateMonstersLocked(conn.Monsters, seconds, now, onHit);
    }

    /// <summary>One regeneration step over the living, damaged monsters (under the Monsters lock): the ones whose life
    /// changed, with their new life. The part of the step within 4 s of the player's last hit on a monster regenerates at
    /// (1 + <paramref name="onHitPct"/> / 100) of its rate, at least 0.</summary>
    internal static List<(uint Id, uint Life)> RegenerateMonstersLocked(IReadOnlyDictionary<uint, SpawnedMonster> monsters, double seconds,
        double now = 0, int onHitPct = 0)
    {
        var changed = new List<(uint, uint)>();
        foreach (var (id, m) in monsters)
        {
            if (m.Dead || m.Life == 0 || m.Life >= m.MaxLife) { m.Regen = 0; continue; }
            double perSecond = m.Mods.Effects.LifeRegenPerSecond(m.MaxLife);
            if (onHitPct != 0 && seconds > 0)
            {
                double recently = Math.Max(0, Math.Min(now, m.PlayerHitAt + RecentlySeconds) - Math.Max(now - seconds, m.PlayerHitAt));
                perSecond *= 1 - recently / seconds * (1 - Math.Max(0, 1 + onHitPct / 100.0));
            }
            if (perSecond <= 0) continue;
            double total = m.Regen + perSecond * seconds;
            uint whole = (uint)Math.Truncate(total);
            m.Regen = total - whole;
            if (whole == 0) continue;
            m.Life = (uint)Math.Min(m.MaxLife, (long)m.Life + whole);
            changed.Add((id, m.Life));
        }
        return changed;
    }

    // The /mob_mods command: a monster's mods, or the monster created again with the given affixes.
    static async Task MonsterModsCommand(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, string[] args)
    {
        var rolls = MonsterModRolls.Value;
        uint? target = MonsterArg(conn, args, 0);
        string[] wanted = [.. args.Where(a => !a.StartsWith("0x", StringComparison.OrdinalIgnoreCase))];
        if (wanted is ["pool", ..])
        {
            // The pool a level-N rare monster of the last spawned variety rolls from.
            SpawnedMonster? last;
            lock (conn.Monsters) last = target is { } t ? conn.Monsters[t] : null;
            if (last?.Variety is not { } v) { await conn.Commands!.Reply("No monster."); return; }
            var pool = rolls.Pool(rolls.TagsOf(v, last.Rarity, alone: true), last.Level, rolls.RollUnsupported);
            await conn.Commands!.Reply($"{pool.Count} mods for {v.Name}@{last.Level}: " +
                string.Join(", ", pool.Select(p => $"{p.Mod.Name.Trim()} {p.Weight}")));
            return;
        }
        if (target is not { } id) { await conn.Commands!.Reply("No such monster."); return; }
        SpawnedMonster m;
        lock (conn.Monsters) m = conn.Monsters[id];
        if (wanted.Length == 0)
        {
            await conn.Commands!.Reply($"0x{id:x} {m.Label} {m.Rarity}" +
                (m.Mods.NameWords.Count > 0 ? $" \"{rolls.NameOf(m.Mods.NameWords)}\"" : "") +
                $": {(m.Mods.Affixes.Any() ? rolls.Describe(m.Mods) : "no affixes")}; life {m.Life}/{m.MaxLife}");
            return;
        }
        if (m.Variety is not { } variety || m.Rarity is not (MonsterRarity.Magic or MonsterRarity.Rare))
        {
            await conn.Commands!.Reply("Only a magic or rare monster takes affixes.");
            return;
        }
        var forced = new List<ModCatalog.Mod>();
        foreach (string w in wanted)
        {
            var mod = rolls.Mods.All.FirstOrDefault(x => x.Domain == MonsterModCatalog.Domain && x.GenerationType is 1 or 2
                && (x.Id.Equals(w, StringComparison.OrdinalIgnoreCase) || x.Id.Equals("MonsterMod" + w, StringComparison.OrdinalIgnoreCase)
                    || x.Name.Trim().Equals(w.Replace('_', ' '), StringComparison.OrdinalIgnoreCase)));
            if (mod is null) { await conn.Commands!.Reply($"No monster mod {w}."); return; }
            forced.Add(mod);
        }
        var set = rolls.Roll(variety, m.Level, m.Rarity, Random.Shared, alone: true, count: forced.Count, forced: forced);
        var (x, y) = m.Walk.Position(conn.Clock.Elapsed.TotalSeconds);
        lock (conn.Monsters) conn.Monsters.Remove(id);
        await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(id), $"0x02bb remove monster 0x{id:x} (/mob_mods)");
        await SpawnMonster(remote, stream, ciphers, conn, variety, m.Level, m.Rarity, x, y, friendly: !m.Hostile, mods: set);
    }
}
