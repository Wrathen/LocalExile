partial class Program
{
    internal static bool IsCriticalProhibition(string name) => name is "global_cannot_crit"
        or "virtual_global_cannot_crit" or "cannot_critical_strike_with_attacks";

    internal static (bool LowLife, bool Focused, int ExtraRolls) CriticalContext(ClientState conn)
    {
        var player = CharacterPlayerStats(conn);
        bool focused = Stats.Key("has_focus") is uint key && player.GetValueOrDefault(key) != 0;
        return (CriticalOnLowLife(conn, player), focused, CriticalExtraRollsFor(conn, player));
    }

    // Compute the native virtual_on_low_life input from current pools. Actions capture its luck contribution at use;
    // the panel continues to show the owner's current state.
    // Focus and Vulnerability use their current raw state inputs; their skill/curse lifecycles are separate mechanics.
    internal static bool CriticalOnLowLife(ClientState conn, IReadOnlyDictionary<uint, int> player)
    {
        int Get(string name) => Stats.Key(name) is uint key ? player.GetValueOrDefault(key) : 0;
        bool counts = Get("you_count_as_low_life_while_not_on_full_life") != 0 && conn.PlayerLife < conn.PlayerMaxLife
            || Get("you_count_as_low_life_while_affected_by_vulnerability") != 0
                && (Get("cursed_by_vulnerability") != 0 || Get("you_are_cursed_with_vulnerability") != 0);
        return CriticalStrikePolicy.OnLowLife(conn.PlayerLife, conn.PlayerMaxLife,
            Get("low_life_threshold_%_override"), Get("low_life_threshold_%"), counts);
    }

    internal static int CriticalExtraRolls(ClientState conn, IReadOnlyDictionary<uint, int>? own = null)
    {
        lock (conn.Monsters)
            return CriticalExtraRollsFor(conn, CharacterPlayerStats(conn), own);
    }

    internal static int CriticalExtraRollsFor(ClientState conn, IReadOnlyDictionary<uint, int> player,
        IReadOnlyDictionary<uint, int>? own = null)
    {
        int Player(string name) => Stats.Key(name) is uint key ? player.GetValueOrDefault(key) : 0;
        int Own(string name) => Stats.Key(name) is uint key ? own?.GetValueOrDefault(key) ?? 0 : 0;
        long Sum(string name) => (long)Player(name) + Own(name);
        return CriticalStrikePolicy.ExtraRolls(Sum("extra_critical_rolls"),
            Sum("extra_critical_rolls_while_on_low_life"), CriticalOnLowLife(conn, player),
            Sum("extra_critical_rolls_during_focus"), Player("has_focus") != 0,
            Player("keystone_sharp_and_brittle") != 0 || Own("keystone_sharp_and_brittle") != 0);
    }

    internal static bool CriticalProhibited(ClientState conn, bool attack, IReadOnlyDictionary<uint, int>? own = null)
        => CriticalProhibitedBy(CharacterPlayerStats(conn), attack, own);

    internal static bool CriticalProhibitedBy(IReadOnlyDictionary<uint, int> player, bool attack,
        IReadOnlyDictionary<uint, int>? own)
    {
        bool Flag(string name) => Stats.Key(name) is uint key
            && (player.GetValueOrDefault(key) != 0 || (own?.GetValueOrDefault(key) ?? 0) != 0);
        return Flag("global_cannot_crit") || Flag("virtual_global_cannot_crit")
            || attack && Flag("cannot_critical_strike_with_attacks");
    }

    // The original per-type damage draws still precede the original critical Next(10000). Conditional luck adds
    // at most one following draw. This RNG order is a local policy, not evidence of the official server's sequence.
    internal static bool RollCriticalStrike(ClientState conn, int chance, IReadOnlyDictionary<uint, int>? own = null,
        bool attack = true, SkillCriticalRoll? skillRoll = null)
    {
        if (chance <= 0) return false;
        lock (conn.Monsters)
        {
            var player = CharacterPlayerStats(conn);
            int allowedChance = CriticalProhibitedBy(player, attack, own) ? 0 : chance;
            return skillRoll is not null ? skillRoll.Check(allowedChance, conn.CriticalRandom)
                : CriticalStrikePolicy.Roll(allowedChance, conn.CriticalRandom, CriticalExtraRollsFor(conn, player, own));
        }
    }

    internal static SkillCriticalRoll CaptureSkillCriticalRoll(ClientState conn, ushort skillId)
    {
        lock (conn.Monsters)
        {
            int effect = SkillEffectId(conn, skillId) is { } id ? SkillsFor(conn).EffectRow(id) : -1;
            var own = skillId == SkillCatalog.FirstSkillId
                ? SkillsFor(conn).EffectStats(effect, 1).ToDictionary(kv => kv.Item1, kv => kv.Item2)
                : SupportedSkill(conn, skillId).Stats;
            return new(CriticalExtraRollsFor(conn, CharacterPlayerStats(conn), own));
        }
    }

    internal static int AttackCriticalMultiplier(ClientState conn, IReadOnlyDictionary<uint, int> player,
        int effect = -1, IReadOnlyDictionary<uint, int>? own = null)
    {
        uint? key = Stats.Key(CriticalStrikePolicy.UnarmedMeleeMultiplierStat);
        long extra = key is uint k ? (long)player.GetValueOrDefault(k) + (own?.GetValueOrDefault(k) ?? 0) : 0;
        return CriticalStrikePolicy.Multiplier(conn.Pools.CriticalStrikeMultiplier, extra,
            WeaponProfileFor(conn, activeSet: true).Unarmed, MeleeAttack(conn, effect),
            effect >= 0 && SkillsFor(conn).IsSpell(effect));
    }
}
