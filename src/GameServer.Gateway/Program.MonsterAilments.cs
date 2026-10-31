using GameServer.Core.Protocol.Instance;
using GameServer.Core.World;
using Kind = MonsterElementalAilments.Kind;

// The player's chill, freeze and shock on monsters (MonsterElementalAilments, Agents/docs/protocol/20-damage.md §3):
// inflicted by every outgoing hit, shock as increased damage taken, chill and freeze in the monster AI, and their buffs.
partial class Program
{
    /// <summary>The factor on damage a monster takes: its mods' increased damage taken, its shock, Glacial Wave's chill and
    /// the player's debuffs on it (his taunt's on every type, his enemy auras' on <paramref name="elemental"/> damage),
    /// added together (PoB DamageTaken and ElementalDamageTaken INC), then a spell's less damage taken.</summary>
    internal static double MonsterDamageTaken(SpawnedMonster m, bool spell, double now, bool elemental = false) =>
        m.Mods.Effects.DamageTaken(spell, m.Ailments.Magnitude(Kind.Shock, now) + m.Ailments.ChillDamageTakenPct(now)
            + m.TakenFromPlayer.All + (elemental ? m.TakenFromPlayer.Elemental : 0));

    /// <summary>Within the Tempest (enemy_shock_on_kill): a monster the player just killed (its life reached 0 from a hit
    /// or Righteous Fire) is shocked through the player's modifiers, and its corpse spreads that shock by the player's own
    /// area of effect. <paramref name="player"/> is the caller's CharacterPlayerStats, so a kill computes no stats of its
    /// own. Under the Monsters lock.</summary>
    static void ShockOnKillLocked(SpawnedMonster m, IReadOnlyDictionary<uint, int> player, double now)
    {
        int Get(string name) => Stats.Key(name) is uint key ? player.GetValueOrDefault(key) : 0;
        if (Get("enemy_shock_on_kill") != 0)
            m.Ailments.ShockOnKill(now, Get, name => m.Mods.Effects[name], AilmentArea(Get, _ => 0, spell: false));
    }

    /// <summary>The area of effect modifier a hit's shock spreads by (SkillArea.Modifier): the player's panel total
    /// skill_area_of_effect_+% (which holds the player's base form, so that is left out, as AreaPlayerStats does) and the
    /// skill's own stats.</summary>
    static double AilmentArea(Func<string, int> player, Func<string, int> skill, bool spell) =>
        SkillArea.Modifier(name => (name == SkillArea.BaseIncreased ? 0 : player(name)) + skill(name), spell);

    /// <summary>Shock proliferation, once per AI tick before the buffs are sent: every living hostile monster within a
    /// spreading shock's own radius (cells, 10 to the metre) of the monster it is on, a corpse included, has the strongest
    /// shock that reaches it until that ends (MonsterElementalAilments.Proliferated). Under the Monsters lock.</summary>
    static void ProliferateShocksLocked(ClientState conn, double now)
    {
        var sources = new List<(SpawnedMonster M, (int X, int Y) At, MonsterElementalAilments.Effect Shock)>();
        foreach (var m in conn.Monsters.Values)
            foreach (var shock in m.Ailments.Spreading(now)) sources.Add((m, m.Walk.Position(now), shock));
        foreach (var m in conn.Monsters.Values)
        {
            if (m.Dead || !m.Hostile || sources.Count == 0) { m.Ailments.Proliferated(null, _ => 0); continue; }
            var at = m.Walk.Position(now);
            m.Ailments.Proliferated(sources.Where(s => !ReferenceEquals(s.M, m) && Dist(s.At, at) <= s.Shock.Spread)
                .Select(s => s.Shock).MaxBy(s => s.Magnitude), name => m.Mods.Effects[name]);
        }
    }

    /// <summary>Its action speed: 0 frozen, else less by its chill. Unstoppable (action_speed_cannot_be_reduced_below_base)
    /// keeps it at 1.</summary>
    internal static double MonsterActionFactor(SpawnedMonster m, double now) =>
        m.Mods.Effects["action_speed_cannot_be_reduced_below_base"] > 0 ? 1 : m.Ailments.ActionFactor(now);

    /// <summary>
    /// A player hit that landed inflicts its ailments (MonsterElementalAilments.Inflict) from the damage the monster took
    /// per type: the mitigated packet × its damage taken × the critical multiplier on a critical strike. The threshold is
    /// its maximum life (wiki: equal to it for most monsters). <paramref name="player"/> is CharacterPlayerStats,
    /// <paramref name="skill"/> the skill's own stats; both count. Under the Monsters lock, after the hit's damage, so the
    /// hit does not gain from its own shock.
    /// A killing hit's ailments stay on the corpse (wiki Ailment: a killing hit that inflicts an elemental ailment counts
    /// as killing an affected enemy; Elemental proliferation goes on from corpses), then the kill's own shock (Within the
    /// Tempest), unless the monster shatters; a corpse neither flees nor reflects. A living monster may flee (MonsterFlee), and a Mirrored one puts the
    /// ailments on the player too: at once, or into <paramref name="reflected"/> for the caller to apply after a sweep
    /// whose hits land together (<see cref="ReflectAilmentsLocked"/>).
    /// </summary>
    internal static void AilmentsOnHitLocked(ClientState conn, SpawnedMonster m, DamagePacket mitigated, bool spell,
        bool critical, int criticalMultiplier, IReadOnlyDictionary<uint, int> player, IReadOnlyDictionary<uint, int>? skill,
        double now, Random random, List<MonsterElementalAilments.Effect>? reflected = null)
    {
        double factor = MonsterDamageTaken(m, spell, now) * (critical ? criticalMultiplier / 100.0 : 1);
        int Player(string name) => Stats.Key(name) is uint key ? player.GetValueOrDefault(key) : 0;
        int Skill(string name) => skill is not null && Stats.Key(name) is uint key ? skill.GetValueOrDefault(key) : 0;
        var added = m.Ailments.Inflict(mitigated[DamageType.Cold].Min * factor, mitigated[DamageType.Lightning].Min * factor,
            critical, m.MaxLife, now, name => Player(name) + PanelStats.Of(name, Skill), name => m.Mods.Effects[name], random,
            AilmentArea(Player, Skill, spell));
        if (m.Dead || m.Life == 0)
        {
            // Shatter (wiki Shatter, Freeze): a non-unique monster a hit kills while frozen, the killing hit's own freeze
            // included, leaves no corpse, so nothing spreads from it and the kill's shock has nothing to land on.
            if (m.Rarity != MonsterRarity.Unique && m.Ailments.Magnitude(Kind.Freeze, now) > 0) m.Ailments.Shatter();
            else ShockOnKillLocked(m, player, now);
            return;
        }
        // Fleeing (MonsterFlee): its own draws (Random.Shared), so a projectile's reaction draws stay in order.
        if (!m.Fleeing && MonsterFlee.Rolls(MonsterFlee.Chance(spell, Player, Skill),
                m.Mods.Effects["monster_chance_to_not_flee_%"], Random.Shared))
        {
            int size = Math.Max(0, LivingCollision.Size(m));
            m.FleeTo = MonsterFlee.Point(m.Walk.Position(now), conn.Walk.Position(now),
                (x, y) => conn.Layout.WalkableFor(x, y, size) && !conn.Barrier.Contains((x, y)), Random.Shared);
            m.Fleeing = m.FleeTo is not null;
        }
        if (added.Count == 0 || conn.PlayerLife == 0
            || !RandomRolls.Chance(m.Mods.Effects["reflect_non_damaging_ailments_%_chance"], 100, random)) return;
        if (reflected is not null) reflected.AddRange(added);
        else ReflectAilmentsLocked(conn, added, now, player, random);
    }

    /// <summary>Mirrored: the ailments a hit added act on the player as well (wiki Reflect), as they landed, then through
    /// the player's own modifiers; the exact reflected magnitude is not verified. A new freeze cancels the player's action.
    /// Under the Monsters lock.</summary>
    internal static void ReflectAilmentsLocked(ClientState conn, IEnumerable<MonsterElementalAilments.Effect> added, double now,
        IReadOnlyDictionary<uint, int> player, Random random)
    {
        if (conn.PlayerLife == 0) return;
        int Player(string name) => Stats.Key(name) is uint key ? player.GetValueOrDefault(key) : 0;
        double previous = conn.ElementalAilments.ActionFactor(now);
        foreach (var e in added)
            conn.ElementalAilments.Inflict(Enum.Parse<PlayerElementalAilments.Kind>(e.Kind.ToString()), e.Magnitude,
                e.Until - e.From, now, Player, random);
        PlayerAilmentsChangedLocked(conn, previous, now);
    }

    /// <summary>
    /// What changed on the monsters' ailments since the last AI tick, spread shocks included, as packets: the chilled, frozen and shocked buffs
    /// (BuffDefinitions 1, 2, 3, which draw BuffVisuals 3, 4, 5) and the virtual_action_speed_+% the native walk
    /// (0x141e7c0b0) and action clips read, which network stats do not derive (04 §4.1a). A chill re-times the server's
    /// walk by the same factor, so both walk the monster alike. A freeze is not sent there (−100 would divide the native
    /// clip by zero); the AI stops the monster instead. Under the Monsters lock.
    /// </summary>
    internal static List<(byte[] Packet, string Log)> MonsterAilmentChangesLocked(ClientState conn, double now)
    {
        ProliferateShocksLocked(conn, now);
        var packets = new List<(byte[], string)>();
        foreach (var (id, m) in conn.Monsters)
        {
            var a = m.Ailments;
            if (a.Idle) continue;
            foreach (var (kind, removed, added, magnitude) in a.Present(now))
            {
                if (removed is ushort r) packets.Add((InstanceEntities.RemoveBuff(id, r), $"0x01c2 monster 0x{id:x} {kind} ends"));
                if (added is ushort b) packets.Add((AilmentBuff(id, b, kind, magnitude, a.Remaining(kind, now)), $"0x01c1 monster 0x{id:x} ({m.Label}) {kind} {magnitude}%"));
            }
            double action = m.Mods.Effects["action_speed_cannot_be_reduced_below_base"] > 0 ? 1 : 1 - a.Magnitude(Kind.Chill, now) / 100;
            double walk = m.Mods.Effects["movement_speed_cannot_be_reduced_below_base"] > 0 ? Math.Max(1, action) : action;
            if (walk != a.WalkFactor)
            {
                m.Walk.SetSpeed(m.Walk.Speed / a.WalkFactor * walk, now);
                a.WalkFactor = walk;
            }
            int speed = (int)Math.Round(100 * (action - 1));
            if (speed != a.SentActionSpeed && Stats.Key("virtual_action_speed_+%") is uint key)
            {
                a.SentActionSpeed = speed;
                packets.Add((InstanceEntities.StatsUpdate(id, [(key, speed)]), $"0x01b3 monster 0x{id:x} virtual_action_speed_+% = {speed}"));
            }
        }
        return packets;
    }

    /// <summary>
    /// The ailment buff on a monster (0x1c1): no duration (the server ends it with 0x1c2, so a pause cannot desync it).
    /// All three rows have the +0xff bool, so the reader takes a u16 (+0x82); chilled and shocked are IsRecovery and
    /// carry their one GrantedStats value (action_speed_-%_from_chill, base_damage_taken_+%_from_shock). The magnitude
    /// goes in both; what the client does with +0x82 is not known.
    /// </summary>
    // Freeze carries no value, so it is the one sent with its duration (u68): without one the client draws no icon under
    // the monster's life bar (live 2026-10-30). Chill and shock still end only by 0x1c2.
    internal static byte[] AilmentBuff(uint id, ushort instance, Kind kind, int magnitude, double seconds = 0) =>
        InstanceEntities.AddBuff(id, instance, (ushort)(kind + 1), PlayerId, u82: (ushort)magnitude,
            values: kind == Kind.Freeze ? null : [(uint)magnitude],
            u68: kind == Kind.Freeze && seconds > 0 ? BitConverter.SingleToUInt32Bits((float)seconds) : null);
}
