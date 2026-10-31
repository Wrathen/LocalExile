using GameServer.Core.World;

/// <summary>
/// The player's ignites on one monster. Several can be on it at once; only the strongest deals damage, and each keeps its
/// own expiry, so a weaker, longer one resumes when the strongest ends (PoE 3.x ignite). Guarded by the connection's
/// Monsters lock; times are projectile-world seconds (<c>ProjectileWorldTime</c>), so pause freezes them. Every source
/// adds here: a hit's chance (<see cref="TryApply"/>) or an ignite a granted skill worked out itself (<see cref="Add"/>,
/// Abberath's Fury).
/// </summary>
sealed class MonsterIgnites
{
    /// <summary>What a hit can ignite with, captured at release: the chance, the ignite's fire damage per second for a
    /// non-critical and a critical hit (the panel's ignite rows, <see cref="AilmentPanelStats"/>, already scaled by the
    /// damage-over-time buckets), and the duration in seconds.</summary>
    public readonly record struct Settings(int Chance, double Min, double Max, double CriticalMin, double CriticalMax,
        double Seconds)
    {
        public static readonly Settings None = new(0, 0, 0, 0, 0, 0);

        /// <summary><paramref name="rows"/>: a context's ailment rows (per minute); <paramref name="get"/>: the merged
        /// caster and skill stats. Base duration 4 s (<see cref="Ailments.IgniteDurationSeconds"/>); never_ignite and
        /// never_damaging_ailments stop it. faster_burn_% and damaging_ailments_deal_damage_+%_faster multiply the damage
        /// per second and divide the duration (wiki Ignite; PoB IgniteBurnFaster).</summary>
        public static Settings From(IEnumerable<(string Stat, int Value)> rows, string hand, Func<string, int> get)
        {
            if (get("never_ignite") != 0 || get("never_damaging_ailments") != 0) return None;
            var r = rows.ToDictionary(x => x.Stat, x => x.Value);
            double faster = Faster(get);
            if (faster <= 0) return None;
            double Row(string bound, string kind) =>
                r.GetValueOrDefault($"{bound}_ignite_damage_to_deal_per_minute_from_{kind}_{hand}_damage") / 60.0 * faster;
            long chance = (long)get("base_chance_to_ignite_%") + get("chance_to_freeze_shock_ignite_%");
            double seconds = Ailments.IgniteDurationSeconds
                * Math.Max(0, 1 + ((double)get("ignite_duration_+%") + get("base_all_ailment_duration_+%")) / 100) / faster;
            return new((int)Math.Clamp(chance, 0, 100), Row("minimum", "non_critical"), Row("maximum", "non_critical"),
                Row("minimum", "critical"), Row("maximum", "critical"), seconds);
        }
    }

    /// <summary>The "deal damage faster" factor of the attacker's ignites (1 without one).</summary>
    public static double Faster(Func<string, int> get) =>
        Math.Max(0, 1 + ((double)get("faster_burn_%") + get("damaging_ailments_deal_damage_+%_faster")) / 100.0);

    /// <summary>The target's base_self_ignite_duration_-% (Fire and Ignite Resistant) on an ignite's duration.</summary>
    public static double SelfDuration(Func<string, int> target) =>
        Math.Max(0, 1 - target("base_self_ignite_duration_-%") / 100.0);

    readonly record struct Ignite(double From, double Until, double DamagePerSecond);
    readonly List<Ignite> active = [];
    double last, carry;

    /// <summary>The native <c>ignited</c> buff instance on the monster while it burns (BuffDefinitions row 0).</summary>
    public ushort? Buff;

    public bool Burning => active.Count > 0;

    /// <summary>Nothing burns and no buff is shown.</summary>
    public bool Idle => active.Count == 0 && Buff is null;

    /// <summary>When the last of its ignites ends.</summary>
    public double Until => active.Count == 0 ? 0 : active.Max(i => i.Until);

    /// <summary>The strongest ignite's damage per second at <paramref name="now"/> (0 when none burns).</summary>
    public double PerSecond(double now) =>
        active.Where(i => i.From <= now && now < i.Until).Select(i => i.DamagePerSecond).DefaultIfEmpty().Max();

    /// <summary>A hit that dealt fire damage tries to ignite. Critical hits always do; otherwise the chance rolls, then the
    /// target's avoidance. The damage per second is an independent roll in the context's range, as the incoming
    /// ailments roll theirs (<c>ApplyPlayerAilmentsLocked</c>). True when an ignite was added.</summary>
    public bool TryApply(Settings settings, bool critical, double now, int avoid, bool immune, Random random)
    {
        if (immune || settings.Seconds <= 0 || !critical && !RandomRolls.Chance(settings.Chance, 100, random)
            || avoid > 0 && RandomRolls.Chance(avoid, 100, random)) return false;
        var (min, max) = critical ? (settings.CriticalMin, settings.CriticalMax) : (settings.Min, settings.Max);
        return Add(now, min + (Math.Max(min, max) - min) * random.NextDouble(), settings.Seconds);
    }

    /// <summary>An ignite already worked out (its chance rolled, its damage and duration final). True when added.</summary>
    public bool Add(double now, double perSecond, double seconds)
    {
        if (!(perSecond > 0) || !(seconds > 0)) return false;
        if (active.Count == 0) { last = now; carry = 0; }
        active.Add(new(now, now + seconds, perSecond));
        return true;
    }

    /// <summary>The whole fire damage the strongest ignite deals from the last advance to <paramref name="until"/>,
    /// through every start and end in between, times the target's mitigation <paramref name="factor"/>; the fraction
    /// carries to the next advance. Ended ignites are dropped.</summary>
    public int Advance(double until, double factor)
    {
        if (active.Count == 0 || !(until > last)) return 0;
        var cuts = active.SelectMany(i => new[] { i.From, i.Until }).Where(t => last < t && t < until)
            .Append(last).Append(until).Distinct().Order().ToArray();
        double damage = carry;
        for (int k = 1; k < cuts.Length; k++)
        {
            double mid = (cuts[k - 1] + cuts[k]) / 2;
            damage += active.Where(i => i.From <= mid && mid < i.Until).Select(i => i.DamagePerSecond).DefaultIfEmpty().Max()
                * Math.Max(0, factor) * (cuts[k] - cuts[k - 1]);
        }
        last = until;
        active.RemoveAll(i => i.Until <= until);
        int whole = (int)Math.Min(int.MaxValue, Math.Floor(Math.Max(0, damage)));
        carry = active.Count == 0 ? 0 : damage - whole;
        return whole;
    }

    public void Clear() { active.Clear(); carry = 0; }
}
