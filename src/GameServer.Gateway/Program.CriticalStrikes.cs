partial class Program
{
    static bool IsCriticalProhibition(string name) => name is "global_cannot_crit"
        or "virtual_global_cannot_crit" or "cannot_critical_strike_with_attacks";

    static (bool LowLife, bool Focused, int ExtraRolls) CriticalContext(ClientState conn)
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

    static int CriticalExtraRollsFor(ClientState conn, IReadOnlyDictionary<uint, int> player,
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

    static bool CriticalProhibitedBy(IReadOnlyDictionary<uint, int> player, bool attack,
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
        if (chance <= 0 && !HoldingFishingRod(conn)) return false;
        lock (conn.Monsters)
        {
            var player = CharacterPlayerStats(conn);
            if (AlwaysCritical(conn, name => Stats.Key(name) is uint key ? player.GetValueOrDefault(key) : 0))
                return !CriticalProhibitedBy(player, attack, own);
            if (chance <= 0) return false;
            int allowedChance = CriticalProhibitedBy(player, attack, own) ? 0 : chance;
            return skillRoll is not null ? skillRoll.Check(allowedChance, conn.CriticalRandom)
                : CriticalStrikePolicy.Roll(allowedChance, conn.CriticalRandom, CriticalExtraRollsFor(conn, player, own));
        }
    }

    static SkillCriticalRoll CaptureSkillCriticalRoll(ClientState conn, ushort skillId)
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

    // Baited Breath (always_crit_while_holding_fishing_rod): while holding a Fishing Rod every hit is a critical strike
    // without a roll, whatever its base chance; a cannot-crit prohibition still wins (wiki). The client's
    // virtual_always_crit calculator (0x14275ba00) reads the stat only for main_hand_weapon_type 15; PoB: CritChance
    // OVERRIDE 100.
    static bool AlwaysCritical(ClientState conn, Func<string, int> get) =>
        get("always_crit_while_holding_fishing_rod") != 0 && HoldingFishingRod(conn);

    // The chance the panel shows under it: 100% where the hit could crit at all; a 0% base still shows 0 although its
    // hits crit (wiki Baited Breath). Prohibited chances are already 0.
    static int DisplayedCriticalChance(ClientState conn, int chance, Func<string, int> get) =>
        chance > 0 && AlwaysCritical(conn, get) ? 10_000 : chance;

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
