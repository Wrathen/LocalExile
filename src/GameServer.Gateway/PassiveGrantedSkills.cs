using System.Net.Sockets;
using System.Text.Json;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;

/// <summary>
/// Skills that allocated passives grant or trigger (Agents/docs/protocol/11-skills.md §10f): "Grants Level 30
/// Determination Skill", "Trigger level 20 Summon Spirit of Kahuturoa Skill when ...", Abberath's Fury on each footstep.
/// PassiveSkills.dat GrantedEffectsPerLevel by PassiveSkillGraphId, GrantedEffectsPerLevelHardmode in Ruthless. The row
/// fixes the skill and its level. A passive-granted skill is not a gem: it has no supports, no quality, no gem-level
/// bonuses and no requirements. Ordinary and ascendancy nodes both count. Only skills with an executor act: granted
/// self auras (Program.GrantedSkillsOf), Summon Elemental Relic (Program.RelicTriggerOf), the Spirit chieftains
/// (Program.TriggeredGrantsLocked) and the footstep skill <c>RepeatingShockwave</c> (Abberath's Fury, Polytheist's Devout
/// of Abberath at level 20: a spell triggered by each of the player's footsteps while running, Program.RunFootstepSkills).
/// Game data: data/tables/PassiveSkills.json, loaded on first use.
/// </summary>
sealed class PassiveGrantedSkills(SkillCatalog skills, string folder = "data", bool ruthless = false)
{
    /// <summary>One granted skill: the node, its GrantedEffectsPerLevel row, the row's GrantedEffects row (and Id) and
    /// level.</summary>
    public readonly record struct Grant(ushort Node, int EffectsPerLevelRow, int Effect, string EffectId, int Level)
    {
        /// <summary>The host GemSkillIds keys the skill by (socket 0), so its skill id stays stable like a socketed
        /// gem's.</summary>
        public uint Host => HostBase | Node;
    }

    /// <summary>Synthetic hosts: item ids count up from 1 and never reach this range.</summary>
    public const uint HostBase = 0xFFFF0000;

    readonly Lazy<Dictionary<ushort, Grant>> _grants = new(() => Load(skills, folder, ruthless));

    /// <summary>The skills these nodes grant, in node order, each node once.</summary>
    public IReadOnlyList<Grant> For(IEnumerable<ushort> nodes) =>
        [.. nodes.Distinct().Where(_grants.Value.ContainsKey).Select(n => _grants.Value[n])];

    static Dictionary<ushort, Grant> Load(SkillCatalog skills, string folder, bool ruthless)
    {
        var grants = new Dictionary<ushort, Grant>();
        string path = Path.Combine(folder, "tables", "PassiveSkills.json");
        if (!File.Exists(path)) return grants;
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var r in d.RootElement.EnumerateArray())
        {
            int graphId = r.GetProperty("PassiveSkillGraphId").GetInt32();
            // Ruthless uses its own column (Determination's points at DeterminationHardMode), as its gems use their
            // HardMode effects; HasHardmodeAlternate only marks the stat columns.
            if (graphId is <= 0 or > ushort.MaxValue
                || !r.TryGetProperty(ruthless ? "GrantedEffectsPerLevelHardmode" : "GrantedEffectsPerLevel", out var v)
                || v.ValueKind != JsonValueKind.Number || skills.EffectsPerLevel(v.GetInt32()) is not { } level
                || skills.EffectId(level.Effect) is not { } effectId) continue;
            grants.TryAdd((ushort)graphId, new((ushort)graphId, v.GetInt32(), level.Effect, effectId, level.Level));
        }
        return grants;
    }

    /// <summary>A skill each footstep triggers (its trigger has a runtime: Program.RunFootstepSkills).</summary>
    internal static bool IsFootstepSkill(string effectId) => effectId == "RepeatingShockwave";

    // The Templar's run animations (Art/Models/CHARACTERS/Templar/rig.amd, all 18 run_* clips): an 800 ms loop with
    // footstepright at 233 ms and footstepleft at 633 ms.
    // shortcut: the Templar's timing stands for every class, because only the Templar has a passive that grants a footstep
    // skill (the other rigs have 800-833 ms loops with other offsets); read the class rig's run_* footstep events
    // (scripts/re/anim_contacts.py) when another class gets one.
    const double RunLoopSeconds = .8;
    static readonly double[] FootstepAt = [.233, .633];

    /// <summary>The footsteps the run animation passes from <paramref name="from"/> to <paramref name="to"/> (its own clock:
    /// seconds since the run began at the animation's base rate): every footstep event in (from, to].</summary>
    public static int Footsteps(double from, double to) => to <= from ? 0
        : FootstepAt.Sum(at => (int)(Math.Floor((to - at) / RunLoopSeconds) - Math.Floor((from - at) / RunLoopSeconds)));

    /// <summary>
    /// Abberath's Fury's base radius in cells. Its stats have no <c>active_skill_base_area_of_effect_radius</c>, and
    /// neither PoB nor poedb give one: the real radius is the game server's own. This is its explosion art's:
    /// MiscAnimated AbberathsHoovesImpact's mesh (Art/Microtransactions/Aura/Uniques/Disjunction/disjunction.sm, BoundingBox
    /// ±73.553 world units, 6.8 cells at 250/23 units per cell). Unverified against the real game.
    /// </summary>
    public const int FootstepExplosionRadius = 7;

    /// <summary>The explosion art: SkillArtVariations <c>repeating_shockwave</c>'s default (MiscAnimated 1173
    /// AbberathsHoovesImpact), which is not scaled to an area (MiscAnimated <c>__col4</c> 0).</summary>
    public const string ExplosionArtVariation = "repeating_shockwave";

    /// <summary>How long the explosion art plays: its one clip (disjunction.amd "animate", once, 666 ms).</summary>
    public const int ExplosionArtMs = 700;
}

partial class Program
{
    static readonly Lazy<PassiveGrantedSkills> GrantedSkills = new(() => NewGrantedSkills(false));
    static readonly Lazy<PassiveGrantedSkills> RuthlessGrantedSkills = new(() => NewGrantedSkills(true));
    static PassiveGrantedSkills NewGrantedSkills(bool ruthless) => new(ruthless ? RuthlessSkills.Value : Skills, ruthless: ruthless);

    /// <summary>The skills the player's allocated passives grant whose node carries this trigger stat
    /// (<c>has_trigger_on_crit_by_unique_enemy</c> and the like; the level row's AdditionalFlags name the same trigger).
    /// Call under the Monsters lock.</summary>
    static IEnumerable<PassiveGrantedSkills.Grant> TriggeredGrantsLocked(ClientState conn, string trigger)
    {
        if (Stats.Key(trigger) is not uint key) return [];
        var passives = PassivesFor(conn);
        return (conn.Ruthless ? RuthlessGrantedSkills : GrantedSkills).Value.For([.. conn.Passives, .. conn.MainAscendancy.Allocated])
            .Where(g => passives.Find(g.Node)?.Stats.Any(s => s.Key == key && s.Value != 0) == true);
    }

    // The skills the player's allocated passives grant, as virtual gem sources after the socketed gems
    // (GemSkillEffectsAt, SupportedSkillAt): same id rules, the granted level, quality 0, no supports.
    // shortcut: only self auras (AuraCatalog) have an executor; a granted triggered, minion or other skill stays out of
    // the skill list until its mechanic exists (the client would offer a skill that does nothing).
    static IReadOnlyList<PassiveGrantedSkills.Grant> GrantedSkillsOf(ClientState conn)
    {
        ushort[] nodes;
        lock (conn.Monsters) nodes = [.. conn.Passives, .. conn.MainAscendancy.Allocated];
        return [.. (conn.Ruthless ? RuthlessGrantedSkills : GrantedSkills).Value.For(nodes)
            .Where(g => AuraCatalog.Supported(g.EffectId))];
    }

    // A granted triggered skill (InbuiltTrigger) has no bar use: it runs from its trigger and stays out of the skill list.
    // Summon Elemental Relic (GEPL 78412 at level 20: Paladin's Elemental Valiance and the Guardian's Unwavering Crusade,
    // the same buff and grant) is the one with an executor (Program.PlayerMinions.cs).
    static bool IsRelicSkill(string effectId) => effectId is "SummonElementalRelic" or "SummonElementalRelicHardMode";

    /// <summary>The relic skill the player's passives grant and the radius of the buff (BuffTemplates 546, the player
    /// and his allies within 60 cells) whose can_trigger_summon_elemental_relic_on_nearby_ally_kill_or_on_hit_rare_or_unique
    /// lets their kills and hits trigger it; null without both.</summary>
    static (PassiveGrantedSkills.Grant Grant, int AllyRadius)? RelicTriggerOf(ClientState conn)
    {
        ushort[] nodes;
        lock (conn.Monsters) nodes = [.. conn.Passives, .. conn.MainAscendancy.Allocated];
        uint? enables = Stats.Key("can_trigger_summon_elemental_relic_on_nearby_ally_kill_or_on_hit_rare_or_unique");
        if (PassiveBuffAuras.AllyAurasFor(nodes, conn.Ruthless).FirstOrDefault(a => a.Stats.Any(s => s.Key == enables && s.Value != 0))
            is not { } buff) return null;
        var grant = (conn.Ruthless ? RuthlessGrantedSkills : GrantedSkills).Value.For(nodes).FirstOrDefault(g => IsRelicSkill(g.EffectId));
        return grant.Node == 0 ? null : (grant, buff.Radius);
    }

    /// <summary>
    /// A hit or kill that may trigger Summon Elemental Relic, under the Monsters lock: a kill, and a hit with damage on a
    /// rare or unique monster, each roll the skill's skill_triggered_by_nearby_allies_kill_or_hit_rare_unique_%_chance (25)
    /// once, by the living player or by his minion at <paramref name="allyAt"/> within the buff's radius of him. A success
    /// spends the skill's 0.3 s cooldown (with cooldown recovery; a roll while it recovers is not made) and queues the
    /// summon for the minion step. True when one was queued.
    /// </summary>
    internal static bool TriggerElementalRelicLocked(ClientState conn, SpawnedMonster target, bool hit, bool killed,
        (int X, int Y)? allyAt = null)
    {
        if (!target.Hostile || conn.PlayerLife == 0 || conn.Closed || conn.LeavingArea) return false;
        bool queued = false;
        if (hit && target.Rarity is MonsterRarity.Rare or MonsterRarity.Unique) queued = Roll();
        if (killed) queued |= Roll();
        return queued;

        bool Roll()
        {
            if (RelicTriggerOf(conn) is not { } relic) return false;
            var (grant, radius) = relic;
            double now = conn.Clock.Elapsed.TotalSeconds;
            if (allyAt is { } ally && Dist(ally, conn.Walk.Position(now)) > radius) return false;
            if (now < conn.SkillReadyAt.GetValueOrDefault(grant.Effect) - 1e-9) return false;
            var own = SkillsFor(conn).EffectStats(grant.Effect, grant.Level).ToDictionary(s => s.Item1, s => s.Item2);
            int chance = Stats.Key("skill_triggered_by_nearby_allies_kill_or_hit_rare_unique_%_chance") is uint c ? own.GetValueOrDefault(c) : 0;
            if (conn.Minions.Random.Next(100) >= chance) return false;
            if (SingleUseCooldown.ForSkill(SkillsFor(conn), Stats, grant.Effect, grant.Level, CharacterPlayerStats(conn), own)
                is not { } plan) return false;
            var state = CooldownStateLocked(conn, grant.Effect, plan, now);
            if (!state.TryUse(now, plan)) return false;
            conn.SkillReadyAt[grant.Effect] = now + state.RemainingSeconds;
            conn.Minions.PendingRelics++;
            return true;
        }
    }

    // A granted skill resolved like a gem: its own stats at the granted level and the aura markers SupportedSkillAt
    // adds. No EffectiveGemLevel or GemQualityBonus: those raise gems.
    static SupportedGem GrantedSupportedSkill(ClientState conn, PassiveGrantedSkills.Grant grant)
    {
        var own = SkillsFor(conn).EffectStats(grant.Effect, grant.Level).ToDictionary(kv => kv.Item1, kv => kv.Item2);
        if (SkillsFor(conn).IsAura(grant.Effect)) own[OwnAuraRestriction.BaseAura] = 1;
        own[OwnAuraRestriction.Aura] = OwnAuraRestriction.IsAura(own) ? 1 : 0;
        return new(grant.Effect, grant.Level, 0, [], own, Granted: true);
    }

    // The footstep trigger's poll: the run animation's clock and each footstep are followed to within this.
    const int FootstepPollMs = 50;

    // The player's footstep skills (Abberath's Fury), for the connection's life: every poll moves the run animation's clock
    // and triggers each skill once per footstep it passed. Not while paused: the game clock stands still then.
    static async Task RunFootstepSkills(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        double run = 0, last = conn.Clock.Elapsed.TotalSeconds;
        try
        {
            while (!conn.Closed)
            {
                await Task.Delay(FootstepPollMs);
                double now = conn.Clock.Elapsed.TotalSeconds;
                if (conn.Closed || conn.Paused || now <= last) continue;
                int steps = FootstepsDue(conn, ref run, last, now);
                last = now;
                if (steps == 0) continue;
                foreach (var skill in FootstepSkills(conn, now))
                    for (int i = 0; i < steps; i++) await TriggerFootstepSkill(remote, stream, ciphers, conn, skill);
            }
        }
        catch (Exception ex)
        {
            if (!conn.Closed) Console.WriteLine($"[gateway] {remote} footstep skills: {ex.Message}");
        }
    }

    // One poll of the run animation's clock, from → now, and the footsteps it passed. The clock runs while the player walks
    // (until Walk.ArrivesAt; a movement skill's blink is no walk) at the walk's speed over the base speed, so movement speed
    // and chill change the step rate (the wiki: movement speed raises the frequency). It starts over when the walk ends, as
    // the run animation does.
    // shortcut: Shield Charge blinks here, so it takes no steps (the wiki: it triggers footsteps); advance this clock
    // during its charge once Shield Charge moves like a walk.
    internal static int FootstepsDue(ClientState conn, ref double run, double from, double now)
    {
        double before = run, until = Math.Min(now, conn.Walk.ArrivesAt);
        if (until > from) run += (until - from) * conn.Walk.Speed / WalkSpeed;
        int steps = PassiveGrantedSkills.Footsteps(before, run);
        if (now >= conn.Walk.ArrivesAt) run = 0;
        return steps;
    }

    // The footstep skills that trigger now: none while the player is dead, leaving, frozen or stunned, in town or in the
    // grace period, nor a disabled one (Devout of Gruthkul's "Your Spells are disabled": the wiki says it disables Abberath's
    // Fury).
    internal static IReadOnlyList<PassiveGrantedSkills.Grant> FootstepSkills(ClientState conn, double now)
    {
        List<ushort> nodes;
        lock (conn.Monsters)
        {
            if (conn.Closed || conn.LeavingArea || conn.PlayerLife == 0 || PlayerActionBlocked(conn, now, now)) return [];
            nodes = [.. conn.Passives, .. conn.MainAscendancy.Allocated];
        }
        var skills = (conn.Ruthless ? RuthlessGrantedSkills : GrantedSkills).Value.For(nodes).Where(s => PassiveGrantedSkills.IsFootstepSkill(s.EffectId)).ToList();
        if (skills.Count == 0 || InTown(conn) || InGracePeriod(conn)) return [];
        return [.. skills.Where(s => ActorSkillDisabledCore(conn, s.Effect, null) is null)];
    }

    // A granted spell's hit (no gem, no supports): its base damage with the player's increases, added damage and conversion,
    // as a supported spell's (SupportedHitFor); Spell Skills deal no Damage zeroes it (SkillDamageContext).
    internal static DamagePacket GrantedSpellHit(ClientState conn, int effect, int level) => GrantedSpell(conn, effect, level).Hit;

    // The hit, and what its ignite takes: the flat damage (base and added, after conversion, before any increase; wiki
    // Ignite), the caster's and the skill's stats and the damage context.
    internal static (DamagePacket Hit, DamagePacket Flat, Dictionary<uint, int> Stats, SkillDamageContext Context) GrantedSpell(
        ClientState conn, int effect, int level)
    {
        var caster = FinalPlayerStats(conn).ToDictionary(v => v.Item1, v => v.Item2);
        var stats = MergeSkillStats(caster, SkillsFor(conn).EffectStats(effect, level).ToDictionary(v => v.Item1, v => v.Item2));
        int Get(string name) => Stats.Key(name) is uint k ? stats.GetValueOrDefault(k) : 0;
        var plan = SkillHitPlan.ForSkill(SkillsFor(conn), effect, level);
        var context = new SkillDamageContext(plan.Kind, caster);
        if (!plan.CanHit) return (DamagePacket.Empty, DamagePacket.Empty, stats, context);
        var conversion = DamageStats.Conversion(stats, Stats, attack: false);
        double effectiveness = AddedEffectiveness(conn, effect, level);
        var hit = SpellDamage.Packet(plan.SpellBase, Get, effectiveness).Convert(conversion);
        var flat = DamageStats.RawAdded(plan.SpellBase, Get, spell: true, effectiveness: effectiveness).Convert(conversion);
        return (context.Hit(hit), flat, stats, context);
    }

    // One footstep's skill (Abberath's Fury): its explosion art where the player stands, and a spell hit on every living
    // hostile monster within its radius (FootstepExplosionRadius grown by area of effect, SkillArea) of the player, as an
    // area spell's sweep (AreaSkillHit) without a gem: no cost, no cast time, crits and rolls per monster, every hit before
    // the sweep's deaths. A landed hit ignites a surviving monster with its 25% chance or a critical strike (IgniteFromHit).
    internal static async Task TriggerFootstepSkill(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        PassiveGrantedSkills.Grant skill)
    {
        var own = SkillsFor(conn).EffectStats(skill.Effect, skill.Level).ToDictionary(v => v.Item1, v => v.Item2);
        var area = MergeSkillStats(AreaPlayerStats(conn), own);
        int radius = SkillArea.Radius(PassiveGrantedSkills.FootstepExplosionRadius,
            name => Stats.Key(name) is uint k ? area.GetValueOrDefault(k) : 0, spell: true);
        string? art = Art.Animated(PassiveGrantedSkills.ExplosionArtVariation, 0);
        (int X, int Y) at;
        uint? effectObject = null;
        List<(uint Id, SpawnedMonster M)> targets;
        using (ProjectileMutation(conn))
        {
            if (conn.Closed || conn.LeavingArea || conn.PlayerLife == 0 || conn.Paused) return;
            double now = ProjectileWorldTime(conn);
            at = conn.Walk.Position(now);
            targets = [.. conn.Monsters.Where(e => e.Value is { Hostile: true, Dead: false, Life: > 0 }
                && Dist(e.Value.Walk.Position(now), at) <= radius).Select(e => (e.Key, e.Value))];
            if (art is not null) effectObject = conn.NextObjectId++;
        }
        if (effectObject is uint obj)
        {
            await Send(remote, stream, ciphers, InstanceEntities.CreateObject(obj, InstanceEntities.ObjectTypeHash(EffectObject.TypePath),
                new EffectObject(at.X, at.Y, art!).Blob()), null);
            await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(obj), null);
            _ = RemoveAfter(remote, stream, ciphers, conn, obj, PassiveGrantedSkills.ExplosionArtMs);
        }
        if (targets.Count == 0) return;

        var player = FinalPlayerStats(conn).ToDictionary(v => v.Item1, v => v.Item2);
        int Player(string name) => Stats.Key(name) is uint k ? player.GetValueOrDefault(k) : 0;
        int Own(string name) => Stats.Key(name) is uint k ? own.GetValueOrDefault(k) : 0;
        var (hit, flat, merged, context) = GrantedSpell(conn, skill.Effect, skill.Level);
        int Merged(string name) => Stats.Key(name) is uint k ? merged.GetValueOrDefault(k) : 0;
        var fire = flat[DamageType.Fire];
        double igniteMore = ChargeDamageMore(merged);
        var pen = Penetration.Of(player, Stats, attack: false, own);
        var mods = HitResistMods(player, own);
        int baseChance = (SkillsFor(conn).SpellCritChance(skill.Effect, skill.Level) ?? 0) + Own("additional_base_critical_strike_chance");
        int chance = SpellCriticalStrike.Chance(baseChance, Player, Own, conn.Pools.SpellCriticalStrikeChancePct);
        int? vsBlinded = Player(VsBlindedCriticalStat) is not 0 and var extra
            ? SpellCriticalStrike.Chance(baseChance, Player, Own, conn.Pools.SpellCriticalStrikeChancePct + (long)extra) : null;
        int multiplier = (int)Math.Clamp((long)conn.Pools.SpellCriticalStrikeMultiplier + Own("base_critical_strike_multiplier_+"),
            int.MinValue, int.MaxValue);
        var gambler = GamblerFor(player);
        bool rageGranted = false;
        var deaths = new List<(uint Id, SpawnedMonster M)>();
        // A Mirrored monster's reflected ailments reach the player after the sweep, as an area spell's (AreaSkillHit).
        var reflected = new List<MonsterElementalAilments.Effect>();
        foreach (var (id, m) in targets)
        {
            MonsterHit? applied;
            int damage;
            bool critical;
            List<byte[]>? ignited = null;
            using (ProjectileMutation(conn))
            {
                // Stop the sweep, but not the kills it already queued.
                if (conn.Closed || conn.LeavingArea || conn.RageRetired || conn.PlayerLife == 0 || conn.Paused) break;
                if (m.Dead || m.Life == 0 || MonsterBlocks(m, Random.Shared, spell: true)) continue;
                var rolled = (gambler with { DamageMorePct = 0 }).RollOutgoingHit(hit, Random.Shared);
                critical = RollCriticalStrike(conn, CriticalChanceOn(conn, id, chance, vsBlinded), own, attack: false);
                double hitAt = ProjectileWorldTime(conn);
                (damage, _, var mitigated) = MonsterHitDamage(m, rolled, critical, attack: false, hitAt, mods, pen,
                    gambler.InvertsResistance(Random.Shared));
                if (critical) damage = DamageModel.CriticalDamage(damage, CriticalMultiplierOn(m, multiplier));
                applied = ApplyMonsterHitLocked(conn, m, damage);
                if (applied is { StageStarts: true } staged)
                {
                    CommitBossStage(remote, stream, ciphers, conn, id, m, ProjectileWorldTime(conn));
                    applied = staged with { StageStarts = false };
                }
                if (applied is not null)
                    AilmentsOnHitLocked(conn, m, mitigated, spell: true, critical, multiplier, player, own, hitAt, Random.Shared, reflected);
                if (applied is not null && !rageGranted) rageGranted = RageOnHitLocked(conn, attack: false, melee: false, critical, own);
                // shortcut: the ignite's flat fire is rolled apart from the hit (the game takes the hit's own roll; the
                // spread is the same, and lucky or triple damage never reaches it either way), until a rolled hit
                // carries its roll position.
                if (applied is not null && IgniteFromHit(fire.Min + Random.Shared.NextDouble() * (fire.Max - fire.Min), critical,
                        Merged, igniteMore, m, Random.Shared) is { } ignite)
                    ignited = IgniteLocked(conn, id, m, context.OverTime(ignite), ProjectileWorldTime(conn));
            }
            foreach (var packet in ignited ?? [])
                await Send(remote, stream, ciphers, packet, $"0x01c1/0x01c2 monster 0x{id:x} ignited by {skill.EffectId}");
            if (applied is not { } landed) continue;
            if (critical) _ = GainFlaskChargesOnCrit(remote, stream, ciphers, conn);
            await NotifyMonsterHit(remote, stream, ciphers, conn, id, m, landed, damage,
                $"{skill.EffectId} (footstep){(critical ? " (critical)" : "")}", deaths: deaths);
            ChargesOnHit(conn, m, critical, spell: true, skill.Effect, skill.Level, primary: id == targets[0].Id);
        }
        if (reflected.Count > 0)
            using (ProjectileMutation(conn)) ReflectAilmentsLocked(conn, reflected, ProjectileWorldTime(conn), player, Random.Shared);
        if (!conn.Closed) foreach (var (deadId, dead) in deaths) await MonsterKilled(remote, stream, ciphers, conn, deadId, dead);
    }

    // An effect object's art goes after it has played; nothing to remove once the connection is gone (a new area is a new
    // connection with its own objects).
    static async Task RemoveAfter(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, uint obj, int ms)
    {
        try
        {
            await Task.Delay(ms);
            if (!conn.Closed && !conn.LeavingArea) await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(obj), null);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
    }
}
