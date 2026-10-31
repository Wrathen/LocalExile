using GameServer.Core.World;

/// <summary>
/// The chill, freeze and shock the player's hits put on one monster (wiki Chill, Freeze and Shock; PoB CalcOffence's
/// non-damaging ailments). Each inflicted ailment keeps its own magnitude and duration; only the strongest of a kind
/// applies. They stay on a corpse until they run out. Guarded by the Monsters lock, on the connection's pauseable clock.
/// </summary>
sealed class MonsterElementalAilments
{
    internal enum Kind { Chill, Freeze, Shock }
    internal sealed record Effect(Kind Kind, double From, double Until, double Magnitude)
    {
        // Glacial Wave: a chill from a hit of a player with enemies_chilled_by_hits_take_damage_increased_by_chill_effect
        // / ..._deal_damage_lessened_by_chill_effect, as the player had them when it landed.
        public bool MoreTaken { get; init; }
        public bool LessDealt { get; init; }
        // A shock's proliferation radius in cells when it landed; 0 for one that does not spread, as a spread copy.
        public int Spread { get; init; }
    }
    readonly List<Effect> effects = [];
    // The strongest shock another enemy's proliferation puts on this one, set again on every AI tick.
    Effect? spread;
    // What the client was last sent: each kind's magnitude, the action speed stat and the factor its walk speed carries.
    readonly Dictionary<Kind, int> shown = [];
    public int SentActionSpeed;
    public double WalkFactor = 1;

    public bool Idle => effects.Count == 0 && spread is null && shown.Count == 0 && SentActionSpeed == 0 && WalkFactor == 1;

    IEnumerable<Effect> Active(Kind kind, double now) => (spread is null ? effects : effects.Append(spread))
        .Where(e => e.Kind == kind && e.From <= now && now < e.Until);

    // The AI asks every living monster each tick, and most have no ailment: that case allocates nothing.
    public double Magnitude(Kind kind, double now) =>
        effects.Count == 0 && spread is null ? 0 : Active(kind, now).Select(e => e.Magnitude).DefaultIfEmpty().Max();

    /// <summary>Seconds until the last active effect of <paramref name="kind"/> ends (0 when none).</summary>
    public double Remaining(Kind kind, double now) => Active(kind, now).Select(e => e.Until - now).DefaultIfEmpty().Max();

    /// <summary>Action speed: 0 while frozen, else less by the chill's effect.</summary>
    public double ActionFactor(double now) => Magnitude(Kind.Freeze, now) > 0 ? 0 : 1 - Magnitude(Kind.Chill, now) / 100;

    /// <summary>Glacial Wave: while a chill from such a hit lasts, the monster takes increased damage by its strongest
    /// chill's effect (from any source, wiki Glacial Wave; PoB CalcPerform DamageTaken INC).</summary>
    public double ChillDamageTakenPct(double now) => Active(Kind.Chill, now).Any(e => e.MoreTaken) ? Magnitude(Kind.Chill, now) : 0;

    /// <summary>Glacial Wave: and deals less damage by half that effect (PoB CalcPerform Damage MORE −effect / 2).</summary>
    public double ChillDamageDealtFactor(double now) =>
        Active(Kind.Chill, now).Any(e => e.LessDealt) ? 1 - Magnitude(Kind.Chill, now) / 200 : 1;

    /// <summary>
    /// virtual_shock_proliferation_radius as the client's calculator (0x142828340) builds it: the largest of the gloves',
    /// Elemental Proliferation's (virtual_elemental_status_effect_aura_radius, itself the larger of its two sources),
    /// Vinktar's flask's, the ascendancy's and the mastery's radius, and 15 with shock_proliferation_radius_is_at_least_15
    /// (Within the Tempest). The calculator scales none of them by area of effect; <see cref="SpreadRadius"/> does.
    /// </summary>
    internal static int ShockSpreadRadius(Func<string, int> get) => new[]
    {
        get("gloves_shock_proliferation_radius"), get("elemental_status_effect_aura_radius"),
        get("elementalist_elemental_status_effect_aura_radius"), get("unique_vinktars_flask_shock_proliferation_radius"),
        get("ascendancy_non_damaging_elemental_ailment_proliferation_radius"), get("passive_mastery_shock_proliferation_radius"),
        get("shock_proliferation_radius_is_at_least_15") != 0 ? 15 : 0, 0,
    }.Max();

    /// <summary>The cells a shock spreads: that radius grown by the area of effect modifier <paramref name="area"/>
    /// (SkillArea.Modifier) like a skill's area, × its square root truncated to two places (wiki Elemental proliferation:
    /// the area of proliferation is affected by modifiers to area of effect).</summary>
    internal static int SpreadRadius(Func<string, int> attacker, double area) =>
        (int)Math.Floor(ShockSpreadRadius(attacker) * Math.Floor(100 * Math.Sqrt(Math.Max(0, area))) / 100);

    /// <summary>This monster's own shocks that spread, alive or a corpse. Each is an aura of its own radius (wiki Elemental
    /// proliferation), so a weaker shock with a wider radius still reaches the enemies beyond a stronger one's.</summary>
    public IEnumerable<Effect> Spreading(double now) => effects.Count == 0 ? [] : effects
        .Where(e => e.Kind == Kind.Shock && e.Spread > 0 && e.From <= now && now < e.Until);

    /// <summary>Proliferation (wiki Elemental proliferation): the strongest of the spreading shocks whose radius reaches
    /// this monster affects it at the same effect until that shock ends, like an aura; the copy does not spread again and
    /// is not a new shock. Null, or a monster that cannot be shocked, takes it off.</summary>
    public void Proliferated(Effect? shock, Func<string, int> target) =>
        spread = shock is null || Immune(Kind.Shock, target) ? null : shock with { Spread = 0 };

    /// <summary>Chill and shock effect: floor(50 × (damage / threshold)^0.4 × effect modifiers), up to the cap, and none
    /// below 5% (the modifiers count before both limits).</summary>
    internal static int Effectiveness(double damage, double threshold, double modifiers, int cap)
    {
        if (damage <= 0 || threshold <= 0) return 0;
        int effect = (int)Math.Min(cap, Math.Floor(50 * Math.Pow(damage / threshold, .4) * modifiers));
        return effect >= 5 ? effect : 0;
    }

    static string Name(Kind kind) => kind.ToString().ToLowerInvariant();

    static bool Immune(Kind kind, Func<string, int> get)
    {
        string past = kind switch { Kind.Chill => "chilled", Kind.Freeze => "frozen", _ => "shocked" };
        return get($"immune_to_{Name(kind)}") != 0 || get($"cannot_be_{past}") != 0 || get($"base_cannot_be_{past}") != 0;
    }

    /// <summary>
    /// One hit's ailments from the damage the monster took per type (critical multiplier included) against its ailment
    /// threshold. Chill: every hit with cold damage, 2 s. Freeze: a critical strike or chance_to_freeze_% with cold
    /// damage, 0.06 s per 1% of the threshold up to 3 s, then duration modifiers, discarded under 0.3 s. Shock: a
    /// critical strike or chance_to_shock_% with lightning damage, and cold damage too with cold_damage_can_shock
    /// (Stormy Seas: the magnitude, not the chance), 2 s. <paramref name="attacker"/> gives the player's and the skill's
    /// summed stats, <paramref name="target"/> the monster's own. The effects added are returned. A chill carries the
    /// attacker's Glacial Wave stats, a shock its proliferation radius grown by the hit's area modifier <paramref name="area"/>.
    /// </summary>
    public List<Effect> Inflict(double cold, double lightning, bool critical, double threshold, double now,
        Func<string, int> attacker, Func<string, int> target, Random random, double area = 1)
    {
        var added = new List<Effect>();
        if (threshold <= 0) return added;
        void Add(Effect effect) { if (Apply(effect, attacker, target) is { } e) added.Add(e); }
        Add(new(Kind.Chill, now, now + 2, Effectiveness(cold, threshold, EffectModifier(Kind.Chill, attacker, target), 30))
        {
            MoreTaken = attacker("enemies_chilled_by_hits_take_damage_increased_by_chill_effect") != 0,
            LessDealt = attacker("enemies_chilled_by_hits_deal_damage_lessened_by_chill_effect") != 0,
        });
        if (cold > 0 && (critical || RandomRolls.Chance(attacker("chance_to_freeze_%"), 100, random)))
            Add(new(Kind.Freeze, now, now + Math.Min(3, 6 * cold / threshold), 100));
        double shocking = lightning + (attacker("cold_damage_can_shock") != 0 ? cold : 0);
        if (shocking > 0 && (critical || RandomRolls.Chance(attacker("chance_to_shock_%"), 100, random)))
            Add(new(Kind.Shock, now, now + 2, Effectiveness(shocking, threshold, EffectModifier(Kind.Shock, attacker, target), 50))
                { Spread = SpreadRadius(attacker, area) });
        return added;
    }

    /// <summary>Brine Bound (chance_to_freeze_enemies_for_1_second_when_hit_%): a freeze of a set base duration, through
    /// the same duration modifiers, threshold, minimum and immunity as a hit's.</summary>
    public Effect? Freeze(double seconds, double now, Func<string, int> attacker, Func<string, int> target) =>
        Apply(new(Kind.Freeze, now, now + seconds, 100), attacker, target);

    /// <summary>Shatter: the monster left no corpse, so its ailments end with it (the next presentation takes their buffs
    /// off) and none spreads.</summary>
    public void Shatter()
    {
        effects.Clear();
        spread = null;
    }

    /// <summary>Within the Tempest (enemy_shock_on_kill): the shock a killed monster's corpse gets, 15% base for 2 s
    /// through the shock effect and duration modifiers (wiki Within the Tempest; the 15% of shocks without damage, wiki
    /// Shock), up to 50%. It spreads like any shock the player inflicts, by the player's area modifier <paramref name="area"/>.</summary>
    public Effect? ShockOnKill(double now, Func<string, int> attacker, Func<string, int> target, double area = 1) =>
        Apply(new(Kind.Shock, now, now + 2, Math.Min(50, Math.Floor(15 * EffectModifier(Kind.Shock, attacker, target))))
            { Spread = SpreadRadius(attacker, area) }, attacker, target);

    static double EffectModifier(Kind kind, Func<string, int> attacker, Func<string, int> target) =>
        Math.Max(0, 1 + attacker($"virtual_{Name(kind)}_effect_+%") / 100.0)
        * Math.Max(0, 1 + target(kind == Kind.Chill ? "chill_effectiveness_on_self_+%" : "shocked_effect_on_self_+%") / 100.0);

    /// <summary>One inflicted ailment with its base duration: the attacker's and target's duration modifiers, a freeze
    /// under 0.3 s discarded, the target's immunity. Null when it does not apply. The attacker's increase and the
    /// target's increase on itself add up (PoB CalcOffence: Enemy…Duration INC + Self…Duration INC); a positive
    /// base_self_*_duration_-% is a reduction.</summary>
    Effect? Apply(Effect effect, Func<string, int> attacker, Func<string, int> target)
    {
        var (kind, now) = (effect.Kind, effect.From);
        double seconds = (effect.Until - now) * Math.Max(0, 1 + (attacker($"virtual_{Name(kind)}_duration_+%")
            - target($"base_self_{Name(kind)}_duration_-%")) / 100.0);
        if (effect.Magnitude <= 0 || seconds <= 0 || kind == Kind.Freeze && seconds < .3 || Immune(kind, target)) return null;
        // Brine Bound (freeze_minimum_duration_X_ms): a freeze that passes the threshold lasts at least this long, which no
        // duration modifier changes (wiki Brine Bound).
        if (kind == Kind.Freeze) seconds = Math.Max(seconds, attacker("freeze_minimum_duration_X_ms") / 1000.0);
        effect = effect with { Until = now + seconds };
        // A weaker one of the same sort that also ends sooner can never be the strongest again.
        effects.RemoveAll(e => e.Until <= now || e.Kind == kind && e.Magnitude <= effect.Magnitude && e.Until <= effect.Until
            && (e.MoreTaken, e.LessDealt, e.Spread) == (effect.MoreTaken, effect.LessDealt, effect.Spread));
        effects.Add(effect);
        return effect;
    }

    /// <summary>The buff instance each kind always has on its monster: the top of the range, which
    /// <see cref="ClientState.NextBuff"/> never hands out, so a monster's frequent ailment changes take nothing from the
    /// connection's counter and never meet another buff's instance on the same object.</summary>
    internal static ushort Instance(Kind kind) => (ushort)(FirstInstance + (int)kind);
    internal const ushort FirstInstance = ushort.MaxValue - 2;

    /// <summary>The buffs to take off and put on so the client shows what applies at <paramref name="now"/>: a kind
    /// whose strongest magnitude changed is sent again (the old one removed first, on the same instance); expired effects
    /// are dropped.</summary>
    public List<(Kind Kind, ushort? Removed, ushort? Added, int Magnitude)> Present(double now)
    {
        effects.RemoveAll(e => e.Until <= now);
        if (spread?.Until <= now) spread = null;
        var changes = new List<(Kind, ushort?, ushort?, int)>();
        foreach (var kind in Enum.GetValues<Kind>())
        {
            int magnitude = (int)Magnitude(kind, now);
            bool had = shown.TryGetValue(kind, out int old);
            if (had ? old == magnitude : magnitude == 0) continue;
            if (magnitude > 0) shown[kind] = magnitude;
            else shown.Remove(kind);
            changes.Add((kind, had ? Instance(kind) : null, magnitude > 0 ? Instance(kind) : null, magnitude));
        }
        return changes;
    }
}
