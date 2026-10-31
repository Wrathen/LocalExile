using GameServer.Core.World;

/// <summary>An ordinary bow or wand's offensive inputs frozen at release. The target supplies evasion and impales
/// at contact. Direct hit and reflected impale damage are resolved separately.</summary>
sealed record OrdinaryRangedShot(DamagePacket Hit, int Accuracy, int CriticalChance, int CriticalMultiplier, int ImpaleChance)
{
    public Penetration.PerType Penetration { get; init; }
    public HitResistances.Mods ResistanceMods { get; init; }
    public ImpaleStats.Settings? ImpaleSettings { get; init; }
    public AttackAccuracyProfile? AccuracyProfile { get; init; }
    public SkillCriticalRoll? CriticalRoll { get; init; }
    public LeechSources Leech { get; init; } = new(DamagePacket.Empty, DamagePacket.Empty, DamagePacket.Empty);
    public static OrdinaryRangedShot Capture(SkillCatalog.Unarmed weapon, IReadOnlyDictionary<uint, int> character,
        IEnumerable<(uint, int)> skillConversion, StatCatalog catalog, int accuracy, int criticalChance, int criticalMultiplier)
    {
        int Get(string name) => catalog.Key(name) is uint key ? character.GetValueOrDefault(key) : 0;
        var conversion = new Dictionary<uint, int>(character);
        var own = skillConversion.ToDictionary(kv => kv.Item1, kv => kv.Item2);
        foreach (var (key, value) in own) conversion.TryAdd(key, value);
        var hit = MaximumAttackDamage.Apply(RageStats.ApplyAttack(PlayerHitDamage.Resolve(SupportedAttackDamage.Raw(weapon, Get), Get,
            DamageStats.Conversion(conversion, catalog, attack: true), spell: false, projectile: true), Get), Get);
        var impale = ImpaleStats.From(character, catalog, attack: true, own, weapon.LocalSources);
        return new(hit, accuracy, criticalChance, criticalMultiplier, impale.Chance)
        { ImpaleSettings = impale,
            Leech = LeechSources.From(name => (int)Math.Clamp((long)Get(name) + Own(name), int.MinValue, int.MaxValue),
                attack: true, name => weapon.LocalSources?.GetValueOrDefault(name) ?? 0),
            AccuracyProfile = new(accuracy,
                Get("accuracy_rating_+%_final_vs_unique_enemies_from_mastery"),
                Get("accuracy_rating_+%_final_vs_enemies_in_close_range_from_mastery"),
                Get("global_always_hit") != 0 || Own("global_always_hit") != 0
                    || (weapon.LocalSources?.GetValueOrDefault("local_always_hit") ?? 0) != 0),
            Penetration = global::Penetration.Of(character, catalog, attack: true, own, weapon.LocalSources),
            ResistanceMods = new(
                Get("critical_strikes_ignore_elemental_resistances") | Own("critical_strikes_ignore_elemental_resistances"),
                Get("attack_critical_strikes_ignore_elemental_resistances") | Own("attack_critical_strikes_ignore_elemental_resistances"),
                Get("non_critical_strikes_penetrate_elemental_resistances_%") + Own("non_critical_strikes_penetrate_elemental_resistances_%"),
                Get("cannot_penetrate_or_ignore_elemental_resistances") | Own("cannot_penetrate_or_ignore_elemental_resistances")) };

        int Own(string name) => catalog.Key(name) is uint key ? own.GetValueOrDefault(key) : 0;
    }

    public (int Damage, bool Critical) Roll(ImpaleState impale, Random random,
        Func<DamagePacket, bool, int>? mitigate = null, int? criticalMultiplier = null,
        int extraCriticalRolls = 0, Random? criticalRandom = null, bool cannotCrit = false,
        double now = 0, Func<double, int>? reflect = null, int avoidImpale = 0,
        int hitChance = 100, Random? accuracyRandom = null, Action<int>? directHit = null)
    {
        var rolled = Hit.Roll(random);
        int chance = cannotCrit ? 0 : CriticalChance;
        bool critical = (CriticalRoll?.Check(chance, criticalRandom ?? random)
            ?? CriticalStrikePolicy.Roll(chance, criticalRandom ?? random, extraCriticalRolls))
            && EvasionEntropy.ConfirmsCritical(hitChance, accuracyRandom ?? random);
        int damage = mitigate?.Invoke(rolled, critical) ?? (int)Math.Round(rolled.MinTotal);
        if (critical) damage = DamageModel.CriticalDamage(damage, criticalMultiplier ?? CriticalMultiplier);
        directHit?.Invoke(damage);
        double physical = rolled[DamageType.Physical].Min * (critical ? Math.Max(0, CriticalMultiplier) / 100.0 : 1);
        var (stored, _) = impale.OnHit(physical, now, ImpaleSettings ?? new(ImpaleChance), random, avoidImpale);
        int reflected = stored > 0 ? reflect?.Invoke(stored) ?? (int)Math.Round(stored) : 0;
        return (ImpaleStats.AddReflected(damage, reflected), critical);
    }
}
