using GameServer.Core.World;

/// <summary>Incoming ordinary monster-hit ailments. Connection-local, guarded by Monsters, on its pauseable clock.
/// Alternative ailments require an explicit source and are not substituted for the ordinary ailments here.</summary>
sealed class PlayerElementalAilments
{
    // Target Metadata/Monsters/Monster.ot: critical_ailment_dot_multiplier_+ = 30.
    // Character.ot's +50 belongs to player ailments, not this incoming monster consumer.
    internal const double MonsterCriticalAilmentMultiplier = 1.3;
    internal enum Kind { Ignite, Chill, Freeze, Shock }
    sealed record Effect(Kind Kind, double From, double Until, double Magnitude);
    readonly List<Effect> effects = [];
    double carry;
    double actionFloor, freezeEffect = 1;

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
    /// <summary>Action speed is one additive bucket: chill/freeze and the character's own action_speed_-% sum, then the
    /// result multiplies attack, cast and movement pacing. A positive <paramref name="statReduction"/> is slower; a
    /// negative value (Tailwind, Acceleration Shrine) is faster.</summary>
    public double TotalActionFactor(double now, double statReduction) => Math.Max(actionFloor,
        1 - (Math.Max(Magnitude(Kind.Freeze, now) * freezeEffect, Magnitude(Kind.Chill, now)) + statReduction) / 100);

    public double ActionFactor(double now) => TotalActionFactor(now, 0);
    public double DamageTakenFactor(double now) => 1 + Magnitude(Kind.Shock, now) / 100;

    static string Name(Kind kind) => kind.ToString().ToLowerInvariant();
    static bool Immune(Kind kind, Func<string, int> get) => get($"immune_to_{Name(kind)}") != 0
        || get($"cannot_be_{kind switch { Kind.Ignite => "ignited", Kind.Chill => "chilled", Kind.Freeze => "frozen", _ => "shocked" }}") != 0;

    public void Reconcile(Func<string, int> get)
    {
        lock (effects)
        {
            effects.RemoveAll(e => Immune(e.Kind, get));
            freezeEffect = Math.Max(0, 1 + get("base_frozen_effect_on_self_+%") / 100.0);
            actionFloor = get("action_speed_cannot_be_reduced_below_base") != 0
                || get("not_affected_by_action_speed_modifiers") != 0 ? 1 : 0;
        }
    }

    // Damaging ailments use the pre-critical base, not damage taken. Non-damaging ailments use damage dealt
    // against the ordinary player's maximum-Life threshold. No enemy chance-to-ailment modifiers are invented.
    public void Apply(DamagePacket baseHit, DamagePacket taken, bool critical, uint maxLife, double now,
        Func<string, int> get, Random random)
    {
        lock (effects)
        {
            Reconcile(get);
            void Add(Kind kind, double magnitude, double seconds)
            {
                seconds *= Math.Max(0, 1 - get($"self_{Name(kind)}_duration_-%") / 100.0);
                if (magnitude <= 0 || seconds <= 0 || kind == Kind.Freeze && seconds < .3 || Immune(kind, get)
                    || RandomRolls.Chance(get($"avoid_{Name(kind)}_%"), 100, random)) return;
                effects.Add(new(kind, now, now + seconds, magnitude));
            }
            if (critical) Add(Kind.Ignite, baseHit[DamageType.Fire].Min * .9 * MonsterCriticalAilmentMultiplier, 4);
            if (maxLife == 0) return;
            double cold = taken[DamageType.Cold].Min / maxLife;
            double lightning = taken[DamageType.Lightning].Min / maxLife;
            double chill = Math.Min(30, Math.Floor(50 * Math.Pow(cold, .4)
                * Math.Max(0, 1 + get("chill_effectiveness_on_self_+%") / 100.0)));
            if (chill >= 5) Add(Kind.Chill, chill, 2);
            if (!critical) return;
            Add(Kind.Freeze, 100, Math.Min(3, 6 * cold));
            double shock = Math.Min(50, Math.Floor(50 * Math.Pow(lightning, .4)
                * Math.Max(0, 1 + get("shocked_effect_on_self_+%") / 100.0)));
            if (shock >= 5) Add(Kind.Shock, shock, 2);
        }
    }

    // Integrate strongest ignite through every start/end boundary: a weaker, longer ignite can resume.
    // Resistance/immunity are live; hit luck, block, suppression and the 90% hit override never enter this pass.
    public int Advance(double from, double until, Func<string, int> get, int fireResistance, int maxFireResistance,
        bool fireImmune)
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
                if (!fireImmune) damage += DamageModel.Mitigate(Magnitude(Kind.Ignite, at), fireResistance, maxFireResistance)
                    * DamageTakenFactor(at) * (boundaries[i] - boundaries[i - 1]);
            }
            effects.RemoveAll(e => e.Until <= until);
            int whole = (int)Math.Min(int.MaxValue, Math.Floor(damage));
            carry = damage - whole;
            return whole;
        }
    }
}
