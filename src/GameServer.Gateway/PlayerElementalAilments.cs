using GameServer.Core.World;

/// <summary>Incoming ordinary monster-hit ailments. Connection-local, guarded by Monsters, on its pauseable clock.
/// Alternative ailments require an explicit source and are not substituted for the ordinary ailments here.</summary>
sealed class PlayerElementalAilments
{
    // Target Metadata/Monsters/Monster.ot: critical_ailment_dot_multiplier_+ = 30.
    // Character.ot's +50 belongs to player ailments, not this incoming monster consumer.
    internal const double MonsterCriticalAilmentMultiplier = 1.3;
    internal enum Kind { Ignite, Chill, Freeze, Shock }
    // Source: the monster whose hit inflicted it, for damage taken from that enemy (an ignite from a Blinded one).
    sealed record Effect(Kind Kind, double From, double Until, double Magnitude, uint? Source = null);
    readonly List<Effect> effects = [];
    double carry;
    double actionFloor, actionCeiling = double.PositiveInfinity, freezeEffect = 1;
    bool chillReversed, unaffectedByShock, unaffectedByIgnite;

    public (bool Ignite, int Chill, bool Freeze, int Shock) Snapshot(double now) =>
        (Magnitude(Kind.Ignite, now) > 0, (int)Magnitude(Kind.Chill, now), Magnitude(Kind.Freeze, now) > 0,
            (int)Magnitude(Kind.Shock, now));

    public bool HasEffects { get { lock (effects) return effects.Count > 0; } }

    public void Clear() { lock (effects) { effects.Clear(); carry = 0; } }
    public double Magnitude(Kind kind, double now)
    {
        lock (effects) return effects.Where(e => e.Kind == kind && e.From <= now && now < e.Until)
            .Select(e => e.Magnitude).DefaultIfEmpty().Max();
    }
    // Changing Seasons (chill_effect_is_reversed): the chill on you raises action speed by its effect instead of lowering
    // it (PoB CalcPerform: ActionSpeed INC +effect); freeze still stops you (wiki Changing Seasons).
    public double ActionFactor(double now)
    {
        double freeze = Magnitude(Kind.Freeze, now) * freezeEffect, chill = Magnitude(Kind.Chill, now);
        double change = chillReversed && freeze <= 0 ? chill : -Math.Max(freeze, chillReversed ? 0 : chill);
        return Math.Min(actionCeiling, Math.Max(actionFloor, 1 + change / 100));
    }
    // Unaffected by Shock (PoB SelfShockEffect 100% less): the shock stays on the player and in is_shocked, with no effect.
    // increasedPct: the hit's other increased damage taken (from a Blinded enemy), additive with the shock's.
    public double DamageTakenFactor(double now, int increasedPct = 0) =>
        Math.Max(0, 1 + ((unaffectedByShock ? 0 : Magnitude(Kind.Shock, now)) + increasedPct) / 100);

    static string Name(Kind kind) => kind.ToString().ToLowerInvariant();
    static bool Immune(Kind kind, Func<string, int> get) => get($"immune_to_{Name(kind)}") != 0
        || get($"cannot_be_{kind switch { Kind.Ignite => "ignited", Kind.Chill => "chilled", Kind.Freeze => "frozen", _ => "shocked" }}") != 0;

    public void Reconcile(Func<string, int> get)
    {
        lock (effects)
        {
            effects.RemoveAll(e => Immune(e.Kind, get));
            freezeEffect = Math.Max(0, 1 + get("base_frozen_effect_on_self_+%") / 100.0);
            unaffectedByShock = get("unaffected_by_shock") != 0;
            unaffectedByIgnite = get("unaffected_by_ignite") != 0;
            actionFloor = get("action_speed_cannot_be_reduced_below_base") != 0
                || get("not_affected_by_action_speed_modifiers") != 0 ? 1 : 0;
            actionCeiling = get("not_affected_by_action_speed_modifiers") != 0 ? 1 : double.PositiveInfinity;
            chillReversed = get("chill_effect_is_reversed") != 0;
        }
    }

    // Damaging ailments use the pre-critical base, not damage taken. Non-damaging ailments use damage dealt
    // against the ordinary player's maximum-Life threshold. No enemy chance-to-ailment modifiers are invented.
    public void Apply(DamagePacket baseHit, DamagePacket taken, bool critical, uint maxLife, double now,
        Func<string, int> get, Random random, uint? source = null)
    {
        lock (effects)
        {
            Reconcile(get);
            void Add(Kind kind, double magnitude, double seconds) => AddLocked(kind, magnitude, seconds, now, get, random, source);
            if (critical) Add(Kind.Ignite, baseHit[DamageType.Fire].Min * .9 * MonsterCriticalAilmentMultiplier, 4);
            if (maxLife == 0) return;
            double cold = taken[DamageType.Cold].Min / maxLife;
            double lightning = taken[DamageType.Lightning].Min / maxLife;
            // Changing Seasons (all_damage_taken_can_chill): every type of the hit's damage counts toward its chill.
            double chilling = get("all_damage_taken_can_chill") != 0 ? taken.MinTotal / maxLife : cold;
            double chill = Math.Min(30, Math.Floor(50 * Math.Pow(chilling, .4)
                * Math.Max(0, 1 + get("chill_effectiveness_on_self_+%") / 100.0)));
            if (chill >= 5) Add(Kind.Chill, chill, 2);
            if (!critical) return;
            Add(Kind.Freeze, 100, Math.Min(3, 6 * cold));
            double shock = Math.Min(50, Math.Floor(50 * Math.Pow(lightning, .4)
                * Math.Max(0, 1 + get("shocked_effect_on_self_+%") / 100.0)));
            if (shock >= 5) Add(Kind.Shock, shock, 2);
        }
    }

    void AddLocked(Kind kind, double magnitude, double seconds, double now, Func<string, int> get, Random random,
        uint? source = null)
    {
        seconds *= Math.Max(0, 1 - get($"self_{Name(kind)}_duration_-%") / 100.0);
        if (magnitude <= 0 || seconds <= 0 || kind == Kind.Freeze && seconds < .3 || Immune(kind, get)
            || RandomRolls.Chance(get($"avoid_{Name(kind)}_%"), 100, random)) return;
        effects.Add(new(kind, now, now + seconds, magnitude, source));
    }

    // A chill, freeze or shock reflected back onto the player (a Mirrored monster) with the effect and duration it
    // landed with: then the player's own effect and duration modifiers, caps, immunity and avoidance, as for a hit.
    public void Inflict(Kind kind, double magnitude, double seconds, double now, Func<string, int> get, Random random)
    {
        lock (effects)
        {
            Reconcile(get);
            if (kind is Kind.Chill or Kind.Shock)
                magnitude = Math.Min(kind == Kind.Chill ? 30 : 50, Math.Floor(magnitude * Math.Max(0, 1 + get(kind == Kind.Chill
                    ? "chill_effectiveness_on_self_+%" : "shocked_effect_on_self_+%") / 100.0)));
            if (kind is not (Kind.Chill or Kind.Shock) || magnitude >= 5) AddLocked(kind, magnitude, seconds, now, get, random);
        }
    }

    // Integrate strongest ignite through every start/end boundary: a weaker, longer ignite can resume.
    // Resistance/immunity are live; hit luck, block, suppression and the 90% hit override never enter this pass.
    // Unaffected by Ignite (PoB SelfIgniteEffect 100% less): the ignite stays on the player and in is_ignited, dealing nothing.
    // takenFrom: the increased damage taken from the strongest ignite's source monster (a Blinded one), with the shock's.
    public int Advance(double from, double until, Func<string, int> get, int fireResistance, int maxFireResistance,
        bool fireImmune, Func<uint, int>? takenFrom = null)
    {
        lock (effects)
        {
            Reconcile(get);
            if (until <= from) return 0;
            var boundaries = effects.SelectMany(e => new[] { e.From, e.Until }).Where(t => from < t && t < until)
                .Append(from).Append(until).Distinct().Order().ToArray();
            double damage = carry;
            for (int i = 1; i < boundaries.Length; i++)
            {
                double at = (boundaries[i - 1] + boundaries[i]) / 2;
                if (fireImmune || unaffectedByIgnite || effects.Where(e => e.Kind == Kind.Ignite && e.From <= at && at < e.Until)
                        .MaxBy(e => e.Magnitude) is not { } ignite) continue;
                int fromSource = ignite.Source is uint source && takenFrom is not null ? takenFrom(source) : 0;
                damage += DamageModel.Mitigate(ignite.Magnitude, fireResistance, maxFireResistance)
                    * DamageTakenFactor(at, fromSource) * (boundaries[i] - boundaries[i - 1]);
            }
            effects.RemoveAll(e => e.Until <= until);
            int whole = (int)Math.Min(int.MaxValue, Math.Floor(damage));
            carry = damage - whole;
            return whole;
        }
    }
}
