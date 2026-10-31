using GameServer.Core.World;
using DamageType = GameServer.Core.World.DamageType;

/// <summary>
/// Reads the damage-conversion and gain-as-extra stats the client already has. The stat names follow Stats.dat:
/// <c>physical_damage_%_to_convert_to_fire</c>, <c>base_physical_damage_%_to_convert_to_cold</c>,
/// <c>attack_physical_damage_%_to_add_as_lightning</c>, and so on. Skill, base and global conversion stack; the
/// resulting percentages are normalised by <see cref="DamageConversion"/>.
/// </summary>
static class DamageStats
{
    public const string SpellDamageIncreasesApplyToAttacks150 = ArcaneMight.Fixed150;

    // Arcane Might: transfer increased/reduced spell damage into the attack's increased bucket at the strongest grant's
    // ratio (ArcaneMight: the flag, the 150% keystone, the actor/skill ratios, the wand-only and retaliation-only ones),
    // retaining half-percent values; spell-only more multipliers and speed never enter this contribution.
    public static double SpellAttackIncrease(Func<string, int> get, bool wieldingWand = false, bool retaliation = false) =>
        ArcaneMight.Increase(get, wieldingWand, retaliation);

    public static double SpellAttackIncrease(IReadOnlyDictionary<uint, int>? stats, bool wieldingWand = false,
        bool retaliation = false) => ArcaneMight.Increase(stats, wieldingWand, retaliation);

    public static DamageRange IncreaseAttackRange(DamageRange range, double increased)
    {
        // Multiply before dividing: 200 * 101.5 / 100 is exactly 203, whereas 200 * 1.015 can fall below it
        // and the panel's final floor would disagree with the hit roll. Non-integral damage stays fractional.
        double percent = Math.Max(0, 100.0 + increased);
        return new(range.Min * percent / 100.0, range.Max * percent / 100.0);
    }

    /// <summary>The additive increase bucket for an attack damage type in its actual skill context
    /// (<paramref name="wieldingWand"/> and <paramref name="retaliation"/> select Arcane Might's conditional grants).</summary>
    public static double AttackIncrease(Func<string, int> get, DamageType type, bool melee,
        bool area = false, bool projectile = false, bool wieldingWand = false, bool retaliation = false) =>
        (double)get("damage_+%") + get("attack_damage_+%") + get($"{type.ToString().ToLowerInvariant()}_damage_+%")
        + (DamageTypes.IsElemental(type) ? get("elemental_damage_+%") : 0)
        + (melee ? get("melee_damage_+%") : 0)
        + (melee && type == DamageType.Physical ? get("melee_physical_damage_+%") : 0)
        + (area ? get("area_damage_+%") : 0) + (projectile ? get("projectile_damage_+%") : 0)
        + SpellAttackIncrease(get, wieldingWand, retaliation);

    /// <summary>"Attack skills have added lightning damage equal to % of maximum mana" (Stats.dat row 11881, client key
    /// 11882): PassiveSkills.dat graph id 22720, Whisperer "we know all", grants row 11881 with the value 6.</summary>
    public const string MaxManaAddedLightning = "attack_skills_have_added_lightning_damage_equal_to_%_of_maximum_mana";

    // Raw attack-only added damage, before increases, effectiveness, support restrictions and conversion.
    // Read maximum mana, so spending or reserving the current pool cannot change the grant.
    public static double ManaAddedLightning(Func<string, int> get) =>
        get("maximum_mana") is > 0 and var mana ? mana * (get(MaxManaAddedLightning) / 100.0) : 0;

    public static double ManaAddedPhysical(Func<string, int> get) =>
        get("maximum_mana") is > 0 and var mana
            ? mana * (get("added_physical_damage_to_attacks_equal_to_%_maximum_mana") / 100.0) : 0;

    // Burning Arrow of Vigour (GEPL AdditionalStats, 21–32): attack fire added from maximum life, as the mana grants above.
    public static double LifeAddedFire(Func<string, int> get) =>
        get("maximum_life") is > 0 and var life
            ? life * (get("added_fire_damage_to_attacks_equal_to_%_maximum_life") / 100.0) : 0;

    /// <summary>Raw added damage, with effectiveness but without increased/more modifiers or conversion.</summary>
    public static DamagePacket RawAdded(DamagePacket hit, Func<string, int> get, bool spell, double effectiveness = 1)
    {
        foreach (var type in Types)
        {
            string slug = Slug(type), prefix = spell ? "spell" : "attack";
            var range = new DamageRange(get($"{prefix}_minimum_added_{slug}_damage"), get($"{prefix}_maximum_added_{slug}_damage"));
            if (!spell && type == DamageType.Lightning)
            {
                // PoB ModParser 16de4b82 lines 5636–5639 grants both attack LightningMin and LightningMax.
                double added = ManaAddedLightning(get);
                range = range.Add(new DamageRange(added, added));
            }
            if (!spell && type == DamageType.Physical)
            {
                double added = ManaAddedPhysical(get);
                range = range.Add(new DamageRange(added, added));
            }
            if (!spell && type == DamageType.Fire)
            {
                double added = LifeAddedFire(get);
                range = range.Add(new DamageRange(added, added));
            }
            hit = hit.Add(type, range.Scale(effectiveness));
        }
        return hit;
    }
    private static readonly DamageType[] Types =
        [DamageType.Physical, DamageType.Lightning, DamageType.Cold, DamageType.Fire, DamageType.Chaos];

    private static string Slug(DamageType type) => type switch
    {
        DamageType.Physical => "physical",
        DamageType.Lightning => "lightning",
        DamageType.Cold => "cold",
        DamageType.Fire => "fire",
        DamageType.Chaos => "chaos",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>Existing combat precedence: player keys win, then the skill fills missing conversion inputs.</summary>
    public static DamageConversion Conversion(IReadOnlyDictionary<uint, int> player, IEnumerable<(uint, int)> own,
        StatCatalog catalog, bool attack)
    {
        var stats = new Dictionary<uint, int>(player);
        foreach (var (key, value) in own) stats.TryAdd(key, value);
        return Conversion(stats, catalog, attack);
    }

    /// <summary>Conversion for a hit. <paramref name="attack"/> selects the attack-only stats; spells use the spell
    /// stats instead, and a hit that is neither (an explosion, null) only the global ones. Conditional stats (while affected
    /// by an aura, against a frozen enemy, …) are ignored.</summary>
    public static DamageConversion Conversion(IReadOnlyDictionary<uint, int> stats, StatCatalog catalog, bool? attack)
    {
        var conversion = new DamageConversion();
        int Get(string name) => catalog.Key(name) is uint key ? stats.GetValueOrDefault(key) : 0;
        void Add(string name, DamageType from, DamageType to, bool skill = false)
        {
            int percent = Get(name);
            if (percent > 0)
            {
                if (skill) conversion.AddSkill(from, to, percent);
                else conversion.AddGlobal(from, to, percent);
            }
        }
        void Gain(string name, DamageType from, DamageType to)
        {
            int percent = Get(name);
            if (percent > 0) conversion.AddGain(from, to, percent);
        }

        for (int fromIndex = 0; fromIndex < Types.Length - 1; fromIndex++)
        {
            var from = Types[fromIndex];
            string source = Slug(from);
            for (int toIndex = fromIndex + 1; toIndex < Types.Length; toIndex++)
            {
                var to = Types[toIndex];
                string target = Slug(to);
                Add($"{source}_damage_%_to_convert_to_{target}", from, to);
                Add($"base_{source}_damage_%_to_convert_to_{target}", from, to);
                Add($"skill_{source}_damage_%_to_convert_to_{target}", from, to, skill: true);
                if (attack is bool a) Add($"{(a ? "attack" : "spell")}_{source}_damage_%_to_convert_to_{target}", from, to);
                Gain($"{source}_damage_%_to_add_as_{target}", from, to);
                Gain($"skill_{source}_damage_%_to_add_as_{target}", from, to);
                if (from == DamageType.Physical && attack is bool b)
                    Gain($"{(b ? "attack" : "spell")}_physical_damage_%_to_add_as_{target}", from, to);
            }
        }

        // Non-chaos to chaos is a common wording (e.g. "gain 15% of non-chaos damage as extra chaos damage").
        int nonChaos = Get("non_chaos_damage_to_add_as_chaos_damage_%");
        if (nonChaos > 0)
            foreach (var type in Types)
                if (type != DamageType.Chaos) conversion.AddGain(type, DamageType.Chaos, nonChaos);
        int elementalChaos = Get("elemental_damage_%_to_add_as_chaos");
        if (elementalChaos > 0)
            foreach (var type in Types)
                if (DamageTypes.IsElemental(type)) conversion.AddGain(type, DamageType.Chaos, elementalChaos);

        return conversion;
    }
}
