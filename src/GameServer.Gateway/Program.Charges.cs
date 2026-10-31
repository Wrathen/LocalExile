using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;
using GameServer.Core.Diagnostics;

partial class Program
{
    // Endurance, Frenzy and Power charges (issue #237, 11-skills.md §15). Each connection has a ChargeSession: the combat
    // code enqueues what may grant charges (ChargesOnHit, ChargesOnKill, ChargesWhenHit) and RunCharges rolls, times,
    // expires and presents them. The charges held count in the character's stats through WithCharges.
    static readonly ConditionalWeakTable<ClientState, ChargeSession> ChargeSessions = new();

    // The charges an area change hands on, by the destination's ticket (Transfer): kept with the duration they had left.
    // A ticket nobody connects with (a failed load) is forgotten after a few minutes.
    static readonly ConcurrentDictionary<uint, (DateTime At, List<Charges.Carried> Charges)> CarriedCharges = new();

    static ChargeSession ChargesOf(ClientState conn) => ChargeSessions.GetValue(conn, _ => new ChargeSession());

    // A player hit on a monster that landed (Attack, AreaSkillHit, a projectile's impact): its "on hit", "on critical
    // strike" and "on non-critical strike" gains. effect: the gem skill's GrantedEffects row (-1: the default attack).
    // primary: the use's first target; once-per-use grants (the Frenzy gem) come only from it.
    static void ChargesOnHit(ClientState conn, SpawnedMonster m, bool critical, bool spell, int effect = -1, int level = 1,
        int quality = 0, bool? melee = null, bool primary = true)
    {
        Func<string, int>? skill = null;
        if (effect >= 0)
        {
            var own = SkillsFor(conn).EffectStats(effect, level, quality).ToDictionary(kv => kv.Item1, kv => kv.Item2);
            skill = name => Stats.Key(name) is uint key ? own.GetValueOrDefault(key) : 0;
        }
        ChargesOf(conn).Enqueue(new ChargeEvent.Hit(critical, spell, melee, m.Rarity, effect >= 0 ? SkillsFor(conn).EffectId(effect) : null,
            skill, Killed: m.Dead || m.Life == 0, Primary: primary));
    }

    // The player died (PlayerDied): every gained charge goes now, not at the ticker's next step, which a pause holds back.
    static void ChargesOnDeath(ClientState conn)
    {
        lock (conn.Monsters) ChargesOf(conn).Die();
    }

    // A hostile monster the player killed (MonsterKilled): its "on kill" gains.
    static void ChargesOnKill(ClientState conn, SpawnedMonster m)
    {
        if (conn.PlayerLife > 0) ChargesOf(conn).Enqueue(new ChargeEvent.Kill(m.Rarity));
    }

    // A monster's hit on the player that landed (when hit) or was blocked (on block).
    static void ChargesWhenHit(ClientState conn, bool blocked, bool spell = false) =>
        ChargesOf(conn).Enqueue(blocked ? new ChargeEvent.Block(spell) : new ChargeEvent.WhenHit());

    // Transfer: the charges go with the player to the area of the new ticket.
    // Whether the official server keeps charges across areas is not documented (poewiki's Charge page says nothing about
    // it); this one call is the rule. A dead player carries nothing (Resurrect in Town changes area while dead).
    static void CarryCharges(ClientState conn, uint ticket)
    {
        List<Charges.Carried> carried;
        lock (conn.Monsters)
        {
            if (conn.PlayerLife == 0) ChargesOf(conn).Die();
            carried = ChargesOf(conn).State.Carry(conn.Clock.Elapsed.TotalSeconds);
        }
        foreach (var (old, entry) in CarriedCharges)
            if (DateTime.UtcNow - entry.At > TimeSpan.FromMinutes(5)) CarriedCharges.TryRemove(old, out _);
        if (carried.Count > 0) CarriedCharges[ticket] = (DateTime.UtcNow, carried);
    }

    // The character's stat sources with its charges (CharacterStatSources): Character.ot's per-charge stats, what the
    // charges held add (ChargeEffects.Expand) and their counts for the panel and the buffs.
    static Dictionary<uint, int> WithCharges(ClientState conn, Dictionary<uint, int> sources)
    {
        var stats = new Dictionary<uint, int>(sources);
        foreach (var (stat, value) in ChargeEffects.Inherent)
            if (Stats.Key(stat) is uint key) stats[key] = stats.GetValueOrDefault(key) + value;
        // A stat /set_stat set keeps its value here too (as in CharacterPlayerStats), so a per-charge stat can be tried out.
        Dictionary<uint, int> dev;
        lock (conn.Monsters) dev = new(conn.PlayerStats);
        int Get(string name) => Stats.Key(name) is uint k ? dev.TryGetValue(k, out int set) ? set : stats.GetValueOrDefault(k) : 0;
        var session = ChargesOf(conn);
        var limits = new Dictionary<ChargeType, Charges.Limits>();
        var held = new Dictionary<ChargeType, int>();
        var converted = new Dictionary<ChargeType, bool>();
        var minimumConverted = new Dictionary<ChargeType, bool>();
        lock (conn.Monsters)
        {
            // The session's limits, periodic stats and conversion flags follow every change of the character's stats
            // from here, so the ticker never polls them.
            session.Observe(Get);
            foreach (var type in Charges.Types)
            {
                limits[type] = session.Limits(type);
                held[type] = session.Effective(type);
                converted[type] = session.Converted(type);
                minimumConverted[type] = session.MinimumConverted(type);
            }
        }
        // The special per-charge coefficients (Character.ot's 3% triple damage and 10% stun threshold per Brutal Charge)
        // are PanelStatDefaults, which BaseStats adds to the published sources after this: count them here too.
        int Coefficient(string name) => (int)Math.Clamp((long)Get(name) + PanelStatDefaults.Default(name), int.MinValue, int.MaxValue);
        var added = ChargeEffects.Expand(Coefficient, t => held[t], t => converted[t]);
        foreach (var (stat, value) in added.Select(kv => (Stat: kv.Key, kv.Value))
            .Concat(ChargeEffects.Display(t => held[t], t => limits[t], t => converted[t], t => minimumConverted[t])))
            if (Stats.Key(stat) is uint key) stats[key] = stats.GetValueOrDefault(key) + value;
        return stats;
    }

    // The more multiplier of the charges on a hit (AttackHit, SpellDamage) or a burn, from the final stats.
    static double ChargeDamageMore(IReadOnlyDictionary<uint, int> stats, bool spell = false) =>
        ChargeEffects.DamageMore(name => Stats.Key(name) is uint key ? stats.GetValueOrDefault(key) : 0, spell);

    // The player's additional physical and elemental damage reduction (Endurance Charges, passives) for hits taken.
    static (int Physical, int Elemental) PlayerDamageReduction(ClientState conn,
        IReadOnlyDictionary<uint, int>? stats = null)
    {
        stats ??= CharacterPlayerStats(conn);
        return ChargeEffects.Reduction(name => Stats.Key(name) is uint key ? stats.GetValueOrDefault(key) : 0);
    }

    // A hit's damage taken after the player's mitigation (the dev /hit commands, MonsterSwing's rules): physical through
    // armour plus additional physical reduction; the other types take resistance before the shared hit-only pass.
    static int MitigateTaken(ClientState conn, DamageType type, int amount, int resistance, int maxResistance)
    {
        lock (conn.Monsters)
        {
            int priorContacts = conn.RecentEnemyHits.Count(conn.Clock.Elapsed.TotalSeconds);
            var stats = CharacterPlayerStats(conn);
            bool relevant = amount > 0 && (type == DamageType.Physical
                || PlayerHitMitigation.ArmourPercent(conn.Pools, type, name => Stats.Key(name) is uint key ? stats.GetValueOrDefault(key) : 0) > 0
                    && DamageModel.Mitigate(amount, resistance, maxResistance) > 0);
            var armour = IncomingArmourLocked(conn, relevant, priorContacts, stats);
            return MitigateTakenWithArmour(conn, type, amount, resistance, maxResistance, armour.EffectiveArmour, stats);
        }
    }

    static int MitigateTakenWithArmour(ClientState conn, DamageType type, int amount, int resistance, int maxResistance,
        int effectiveArmour, IReadOnlyDictionary<uint, int> stats)
    {
        var reduction = PlayerDamageReduction(conn, stats);
        int resisted = type == DamageType.Physical ? amount : DamageModel.Mitigate(amount, resistance, maxResistance);
        return FortificationDamageTaken(conn, (int)Math.Round(PlayerHitMitigation.AfterResistance(conn.Pools, type, resisted,
            reduction.Physical, reduction.Elemental, effectiveArmour,
            PlayerHitMitigation.ArmourPercent(conn.Pools, type, name => Stats.Key(name) is uint key ? stats.GetValueOrDefault(key) : 0))
            * conn.ElementalAilments.DamageTakenFactor(conn.Clock.Elapsed.TotalSeconds)));
    }

    // What MitigateTaken applied, for the dev commands' replies: armour or resistance, and the additional reduction.
    static string MitigationLabel(ClientState conn, DamageType type, int amount, int resistance, int maxResistance,
        int effectiveArmour, IReadOnlyDictionary<uint, int> stats)
    {
        var (physical, elemental) = PlayerDamageReduction(conn, stats);
        if (type == DamageType.Physical)
            return $"{DamageModel.ArmourReduction(effectiveArmour, amount)}% armour" + (physical > 0 ? $" + {physical}% physical damage reduction" : "");
        int share = PlayerHitMitigation.ArmourPercent(conn.Pools, type, name => Stats.Key(name) is uint key ? stats.GetValueOrDefault(key) : 0);
        return $"{DamageModel.Capped(resistance, maxResistance)}% resistance" +
            (share > 0
                ? $" + {DamageModel.ArmourReduction((int)((long)effectiveArmour * share / 100), DamageModel.Mitigate(amount, resistance, maxResistance))}% armour"
                : "") +
            (DamageTypes.IsElemental(type) && elemental > 0 ? $" + {elemental}% elemental damage reduction" : "");
    }

    // A warcry (CastGemSkill), when its cast is over: it taunts the hostile monsters within its radius (wiki Warcry: every
    // warcry taunts, base radius 6 m, grown by area of effect; TauntLocked), and Enduring Cry gives an Endurance Charge per
    // 5 power (endurance_charge_granted_per_X_monster_power_during_endurance_warcry) of the hostile monsters within it and
    // its life regeneration (Path of Building: power 1 normal, 2 magic, 10 rare, 20 unique). warcries_have_infinite_power
    // (Bring the Battle) makes the power 999999 (PoB CalcPerform's override): the charges reach their maximum and the
    // regeneration its 25-power cap. Every warcry then runs the on-warcry effects (WarcryReleased). The other warcries'
    // own buffs and exerted attacks are not modelled.
    // True when the skill was a warcry.
    static bool UseWarcryInstance(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        ushort skillId, int effect)
        => SkillsFor(conn).IsWarcry(effect) && UseWarcry(remote, stream, ciphers, conn, effect, SupportedSkill(conn, skillId).Stats);

    const int InfiniteWarcryPower = 999999;

    // A warcry's use time (PoB CalcOffence calcWarcryCastTime): its cast time over warcry speed (the player's and the
    // gem's) and action speed, at least a server tick. The client times the cry from the same warcry_speed_+% (0x141abcaa0).
    internal static double WarcryCastSeconds(ClientState conn, int effect, IReadOnlyDictionary<uint, int> player,
        IReadOnlyDictionary<uint, int> own)
    {
        long speed = Stats.Key("warcry_speed_+%") is uint key ? (long)player.GetValueOrDefault(key) + own.GetValueOrDefault(key) : 0;
        return Math.Max(TickSeconds, SkillsFor(conn).CastTimeMs(effect) / 1000.0 / Math.Max(0.01, 1 + speed / 100.0) / PlayerActionFactor(conn));
    }

    static bool UseWarcry(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, int effect,
        IReadOnlyDictionary<uint, int> own)
    {
        if (!SkillsFor(conn).IsWarcry(effect)) return false;
        bool enduring = SkillsFor(conn).EffectId(effect) == "EnduringCry";
        int Own(string name) => Stats.Key(name) is uint key ? own.GetValueOrDefault(key) : 0;
        int per = Own("endurance_charge_granted_per_X_monster_power_during_endurance_warcry");
        // The warcry cannot be cancelled before its contact point (cannot_cancel_skill_before_contact_point), so a later
        // action does not stop it. Admission checks the actor restriction before this accepted release is scheduled;
        // changing its source later does not cancel this continuation. Warcry and action speed are taken at the use.
        var player = AreaPlayerStats(conn);
        int Player(string name) => Stats.Key(name) is uint key ? player.GetValueOrDefault(key) : 0;
        double castSeconds = WarcryCastSeconds(conn, effect, player, own);
        double until = conn.Clock.Elapsed.TotalSeconds + castSeconds;
        int epoch = conn.ProjectileEpoch;
        double duration = SkillDurationModifiers.Seconds(Own("base_skill_effect_duration"), Player, Own);
        double buffEffect = Math.Max(0, 1 + ((long)Player("warcry_buff_effect_+%") + Own("warcry_buff_effect_+%")) / 100d);
        _ = Task.Run(async () =>
        {
            // The game clock: a pause holds the cry.
            while (!conn.Closed && conn.Clock.Elapsed.TotalSeconds < until) await Task.Delay(16);
            if (!conn.ProjectileActive(epoch) || conn.PlayerLife == 0) return;
            double taunt = TauntSeconds(Player);
            foreach (var (k, v) in own) player[k] = v;
            int radius = SkillArea.Radius(WarcryRadius, name => Stats.Key(name) is uint key ? player.GetValueOrDefault(key) : 0, spell: false);
            double now = conn.Clock.Elapsed.TotalSeconds;
            int power, taunted;
            (int X, int Y) here;
            lock (conn.Monsters)
            {
                if (!conn.ProjectileActive(epoch) || conn.PlayerLife == 0) return;
                here = conn.Walk.Position(now);
                taunted = TauntLocked(conn, here, radius, now, taunt);
                if (!enduring)
                    Console.WriteLine($"[gateway] {SkillsFor(conn).EffectId(effect)}: taunts {taunted} monster(s) within {radius} cells for {taunt:0.##} s");
            }
            if (!enduring)
            {
                await WarcryReleased(remote, stream, ciphers, conn, here, radius, epoch);
                return;
            }
            lock (conn.Monsters)
            {
                if (!conn.ProjectileActive(epoch) || conn.PlayerLife == 0) return;
                power = Player("warcries_have_infinite_power") != 0 ? InfiniteWarcryPower
                    : conn.Monsters.Values.Where(m => m.Hostile && !m.Dead && m.Life > 0 && Dist(m.Walk.Position(now), here) <= radius)
                        .Sum(m => WarcryPower(m.Rarity));
                // The target's stat description caps regeneration at 25 Power. Charge generation retains its own rule.
                double percentPerMinute = Math.Min(25, power) / 5d
                    * Own("enduring_cry_life_regeneration_rate_per_minute_%_per_5_power_up_to_cap")
                    * buffEffect;
                conn.EnduringCryRecovery.Begin(now, duration, conn.PlayerMaxLife * percentPerMinute / 6000d);
                Console.WriteLine($"[gateway] Enduring Cry recovery: {power} power, {duration:0.###} seconds, "
                    + $"{percentPerMinute:0.###}% Life/min (Whaletooth included)");
            }
            int charges = per > 0 ? power / per : 0;
            Console.WriteLine($"[gateway] Enduring Cry: {power} power within {radius} cells, {charges} endurance charge(s), {taunted} taunted");
            if (charges > 0) ChargesOf(conn).Enqueue(new ChargeEvent.Grant(ChargeType.Endurance, charges, $"Enduring Cry ({power} power)"));
            await WarcryReleased(remote, stream, ciphers, conn, here, radius, epoch);
        });
        return true;
    }

    // A warcry's radius: the wiki's base 6 m, Path of Building's EnduringCry skill("radius", 60); one unit is one cell.
    const int WarcryRadius = 60;

    // A monster's warcry power (Path of Building's ConfigOptions): normal 1, magic 2, rare 10, unique 20.
    internal static int WarcryPower(MonsterRarity rarity) => rarity switch
    {
        MonsterRarity.Magic => 2,
        MonsterRarity.Rare => 10,
        MonsterRarity.Unique => 20,
        _ => 1,
    };

    // The charges ticker of one connection, started with the area's other loops: every ChargeSession.StepSeconds of game
    // time (a pause stops it) it steps the session and sends what changed, the charge buffs (ChargeBuffs) and, when the
    // counts the stats use changed, the pools, the walk speed and the stats (SendChargeStats). The character's stats are
    // worked out outside the Monsters lock, and only while events wait (their gain chances); the limits come with every
    // stats change (ChargeSession.Observe through WithCharges).
    static async Task RunCharges(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        var session = ChargesOf(conn);
        if (CarriedCharges.TryRemove(conn.Ticket, out var entry) && entry.Charges is var carried)
            lock (conn.Monsters)
            {
                session.State.Restore(carried, conn.Clock.Elapsed.TotalSeconds);
                Console.WriteLine($"[gateway] {remote} charges carried from the last area: " +
                    string.Join(", ", carried.Select(c => $"{c.Held} {Charges.Name(c.Type)} ({c.Remaining:0.0} s left)")));
            }
        while (!conn.Closed && !conn.RageRetired)
        {
            await Task.Delay(TimeSpan.FromSeconds(ChargeSession.StepSeconds));
            if (conn.Closed || conn.RageRetired) break;
            if (conn.LeavingArea || conn.Paused) continue;
            try
            {
                Func<string, int>? stats = null;
                WeaponProfile weapon = default;
                if (session.Pending)
                {
                    var final = CharacterPlayerStats(conn);
                    stats = name => Stats.Key(name) is uint key ? final.GetValueOrDefault(key) : 0;
                    weapon = WeaponProfileFor(conn);
                }
                double now = conn.Clock.Elapsed.TotalSeconds;
                ChargeSession.StepResult step;
                List<ChargeSession.BuffChange> buffs;
                using (ProjectileMutation(conn))
                {
                    if (conn.Closed || conn.RageRetired) break;
                    if (conn.LeavingArea) continue;
                    now = ProjectileWorldTime(conn);
                    step = session.Step(now, stats, () => weapon, alive: conn.PlayerLife > 0, moving: now < conn.Walk.ArrivesAt,
                        inTown: InTown(conn), Random.Shared);
                    buffs = session.Presentation(now, () => conn.NextBuff++);
                    if (step.CountsChanged)
                    {
                        conn.Pools = PoolsFor(conn, conn.Pools.Level);
                        conn.ApplyMovementSpeed();
                    }
                }
                foreach (string line in step.Log) Console.WriteLine($"[gateway] {remote} {line} (t={now:0.000})");
                foreach (var change in buffs)
                    foreach (var (packet, label) in ChargeBuffs.Packets(change, PlayerId))
                        await Send(remote, stream, ciphers, packet, label);
                if (step.CountsChanged)
                {
                    await SendChargeStats(remote, stream, ciphers, conn,
                        string.Join(", ", Charges.Types.Select(t => $"{session.Effective(t)} {Charges.Label(t, session.Converted(t))}")));
                }
            }
            catch (Exception ex) when (ex is not (IOException or ObjectDisposedException))
            {
                if (!conn.Closed) Console.WriteLine($"[gateway] {remote} charges: {ServerLog.Describe(ex)}");
            }
            catch (Exception) { break; }   // the connection has gone
        }
    }

    // The stats after a charge count changed: 0x1b3 and the skills' stats (the client swings and casts with the speed in
    // its skill records), without SendSkills' skill-bar work (no skill came or went). A changed skill set (a gem moved at
    // the same moment) takes the full SendCharacterStats.
    static async Task SendChargeStats(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, string why)
    {
        if (conn.Closed || conn.LeavingArea || conn.RageRetired) return;
        var skills = PlayerSkills(conn);
        if (!conn.ClientSkillIds.SetEquals(skills.Select(s => s.Id)))
        {
            await SendCharacterStats(remote, stream, ciphers, conn, $"charges {why}");
            return;
        }
        await SendPlayerStatSnapshot(remote, stream, ciphers, conn,
            () => PlayerStatsUpdateAtGate(conn, PlayerStatsWithCleared(conn)), $"0x01b3 player stats (charges {why})");
        await SendPlayerStatSnapshot(remote, stream, ciphers, conn,
            () => InstanceEntities.PlayerSkillsChange(PlayerId, PlayerSkills(conn), []),
            $"0x02ba player Actor: {skills.Count} skill(s), charge stats");
        await SendPlayerEnergyShield(remote, stream, ciphers, conn);
    }

    const string ChargesUsage = "/charges [endurance|frenzy|power <count>] | /charges add <type> [count] | /charges clear " +
        "(dev: the charges held, their maximum, minimum and time left; set, gain or clear them)";

    // /charges: shows the charges, or sets (no refresh roll), gains (as a skill would: refreshes, can reach the maximum
    // chance) or clears them. The ticker applies the change within a step.
    static async Task ChargesCommand(ClientState conn, string[] args)
    {
        var session = ChargesOf(conn);
        if (args.Length == 0)
        {
            double now = conn.Clock.Elapsed.TotalSeconds;
            string text;
            lock (conn.Monsters)
                text = string.Join("; ", Charges.Types.Select(t =>
                {
                    var l = session.Limits(t);
                    return $"{Charges.Name(t)}{(session.Converted(t) ? $" (as {Charges.SpecialName(t)})" : "")} {session.Effective(t)}/{l.Maximum} (minimum {l.Minimum}, " +
                        $"{l.DurationMs / 1000.0:0.###} s, {session.State.Remaining(t, now):0.0} s left)";
                }));
            await conn.Commands!.Reply($"Charges: {text}.");
            return;
        }
        if (args[0].Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            session.Enqueue(new ChargeEvent.Set(null, 0));
            await conn.Commands!.Reply("Charges cleared.");
            return;
        }
        bool add = args[0].Equals("add", StringComparison.OrdinalIgnoreCase);
        if (add) args = args[1..];
        if (args.Length == 0 || Charges.Parse(args[0]) is not { } type) throw new ArgumentException("endurance, frenzy or power");
        int count = args.Length > 1 ? int.Parse(args[1]) : 1;
        if (count < 0) throw new ArgumentException("a count of 0 or more");
        session.Enqueue(add ? new ChargeEvent.Grant(type, count, "/charges add") : new ChargeEvent.Set(type, count));
        await conn.Commands!.Reply($"{(add ? "Gaining" : "Setting")} {count} {Charges.Name(type)} charge(s).");
    }
}
