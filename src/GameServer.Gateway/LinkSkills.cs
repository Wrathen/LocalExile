/// <summary>
/// The player's links (Program.LinkSkills.cs): Soul, Flame, Vampiric, Protective, Intuitive and Destructive Link. A link
/// is one of his skills bound to one target for its duration; the target is an allied player (none: there is no party
/// play) or, with <c>link_skills_can_target_minions</c>, one of his damageable minions. Each link has a source buff on
/// him and a target buff on the target, costs mana per second, breaks after 4 s out of range, and kills him if its
/// target dies while linked (the gems' text: "If the target dies while linked, you will also die"). Guarded by the
/// connection's Monsters lock; links go with the connection.
/// </summary>
sealed class LinkSkills
{
    /// <summary>A link gem's buffs (BuffDefinitions 3.29.3b): the source on the caster, the target on the linked one.
    /// The source buff grants <c>number_of_active_links</c> (its condition stats); the target buffs' stats act on the
    /// target only.</summary>
    internal static readonly IReadOnlyDictionary<string, (ushort Source, ushort Target)> Buffs = new Dictionary<string, (ushort, ushort)>
    {
        ["SoulLink"] = (1946, 1947), ["FlameLink"] = (1948, 1949), ["VampiricLink"] = (1950, 1951),
        ["ProtectiveLink"] = (1952, 1953), ["IntuitiveLink"] = (1954, 1955), ["DestructiveLink"] = (1956, 1957),
    };

    /// <summary>Links break after this long out of range (wiki Link skill).</summary>
    internal const double BreakSeconds = 4;

    /// <summary>The link range in cells, as the client's link-target filter (0x141bf8490): 80 × (100 +
    /// <c>link_skill_range_+%</c>) / 100, integer division.</summary>
    internal static int Range(int increasedPct) => (int)Math.Max(0, (100L + increasedPct) * 80 / 100);

    /// <summary>One link: its target, the skill (id and effect), until when it lasts (game seconds), its mana cost per
    /// minute (fixed at the cast: <c>skill_cost_over_time_is_not_removed_with_skill</c>), its two buff instances and since
    /// when its target has been out of range.</summary>
    internal sealed class Link(uint target, ushort skillId, string effectId)
    {
        public uint Target => target;
        public ushort SkillId => skillId;
        public string EffectId => effectId;
        public double Until;
        public double ManaPerMinute;
        public ushort SourceBuff, TargetBuff;
        public double? AwaySince;
    }

    readonly Dictionary<uint, Link> links = [];
    public IReadOnlyCollection<Link> All => links.Values;
    public int Count => links.Count;
    public Link? On(uint target) => links.GetValueOrDefault(target);
    public void Add(Link link) => links[link.Target] = link;
    public void Remove(uint target) => links.Remove(target);

    /// <summary>A flask effect on a linked target (Field Medicine): the target, its buff instances (the flask's, and the
    /// Silver Flask's Onslaught), when it ends and the stats it applies there.</summary>
    internal sealed record FlaskOn(uint Target, ushort Buff, ushort? Extra, double Until, IReadOnlyList<UtilityFlasks.Source> Stats);

    public readonly List<FlaskOn> Flasks = [];

    /// <summary>The cost paid up to this game time, and the unpaid fraction of a mana point.</summary>
    public double PaidAt, ManaCarry;
}

sealed partial class ClientState
{
    /// <summary>The player's links (LinkSkills), under the Monsters lock.</summary>
    public readonly LinkSkills Links = new();
}
