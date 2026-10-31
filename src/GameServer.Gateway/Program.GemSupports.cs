using GameServer.Core.Protocol.Instance;

partial class Program
{
    internal static readonly GemSupports Supports = new();
    sealed record SupportedGem(int Effect, int Level, int Quality, IReadOnlyList<GemSupports.Applied> Supports,
        Dictionary<uint, int> Stats, Item? SourceGem = null, bool RequirementsMet = true, bool Granted = false);

    static SupportedGem SupportedSkill(ClientState conn, ushort skillId)
    {
        if (BuildScopeOf(conn) is not { } scope)
            lock (conn.Monsters) return SupportedSkillAt(conn, skillId, null);
        // Within one PlayerSkills build each gem is resolved once; callers get their own copy of its stats.
        if (!scope.Supported.TryGetValue(skillId, out var gem))
            lock (conn.Monsters) scope.Supported[skillId] = gem = SupportedSkillAt(conn, skillId, null);
        return gem with { Stats = new(gem.Stats) };
    }

    static SupportedGem SupportedSkillAt(ClientState conn, ushort skillId, GemRequirementSnapshot? requirements)
    {
        string name = CharacterName(conn);
        var equipment = requirements?.Equipment ?? ItemsOf(conn).EquipmentSnapshot();
        var places = GemRequirementSnapshot.PlacesIn(equipment);
        var active = places
            .Where(p => !conn.Ruthless || ItemsFor(conn).ByHash(p.Gem.TypeHash) is { } type && RuthlessGemRules.AllowsBase(type.Id))
            .SelectMany(p => ItemsFor(conn).GemEffects(p.Gem).Select(e => (Place: p, Effect: e))).ToList();
        var granted = GrantedSkillsOf(conn);
        var ids = GemSkillIdsOf.GetOrAdd(name, _ => new GemSkillIds()).Assign(
            [.. active.Select(g => new GemSkillIds.Gem(g.Place.ItemId, g.Place.Socket, g.Effect)),
                .. granted.Select(g => new GemSkillIds.Gem(g.Host, 0, g.Effect))], GemSkillIdPool.Value);
        int index = Enumerable.Range(0, ids.Count).FirstOrDefault(i => ids[i] == skillId, -1);
        if (index < 0) return new(-1, 1, 0, [], []);
        if (index >= active.Count) return GrantedSupportedSkill(conn, granted[index - active.Count]);
        var selected = active[index];
        // Vaal source/support eligibility must not inherit an earlier equipment notification's cached pools.
        if (requirements is null && SkillsFor(conn).VaalBudget(selected.Effect, ItemsFor(conn).GemLevel(selected.Place.Gem)) is not null)
            requirements = CurrentGemRequirements(conn, equipment);
        var host = equipment.Single(e => e.Item.Id == selected.Place.ItemId).Item.Item;
        var local = ItemModStats.Local(host, ItemsFor(conn).Mods);
        (int Level, int Quality) LocalBonus(int socket, Item gem) =>
            SocketedGemBonuses.For(host, socket, gem, ItemsFor(conn), SkillsFor(conn), local, conn.Pools.Level);
        int group = GemSupports.LinkGroup(host, selected.Place.Socket);
        int Quality(int socket, Item gem)
        {
            int colour = ItemsFor(conn).ByHash(gem.TypeHash) is { } b ? ItemsFor(conn).Gem(b)?.Colour ?? 0 : 0;
            return gem.Quality + LocalBonus(socket, gem).Quality
                + (colour is >= 1 and <= 3 && (int)host.Sockets[socket].Colour == colour ? 10 : 0);
        }
        var candidates = new List<GemSupports.Candidate>();
        foreach (var p in places.Where(p => p.ItemId == selected.Place.ItemId
            && GemSupports.LinkGroup(host, p.Socket) == group && GemRequirementsUnmetAt(conn, p.Gem, requirements) is null))
            if (ItemsFor(conn).ByHash(p.Gem.TypeHash) is { } type && ItemsFor(conn).Gem(type) is { IsSupport: true } gem)
            {
                var variant = gem.Variant(p.Gem.GemVariant);
                foreach (int effect in new[] { variant.Effect, variant.Effect2 }.Where(e => e >= 0))
                    candidates.Add(new(effect, EffectiveGemLevel(conn, p.Gem, LocalBonus(p.Socket, p.Gem).Level),
                        Quality(p.Socket, p.Gem), p.Socket));
            }
        candidates.AddRange(Supports.ItemCandidates(host, ItemsFor(conn).Mods));
        var weapon = equipment.Where(e => e.Row == PlayerInventories.Weapon1).Select(e => e.Item).FirstOrDefault();
        int? weaponClass = weapon is null ? null : ItemsFor(conn).ByHash(weapon.Item.TypeHash)?.ClassRow;
        var supports = Supports.Resolve(selected.Effect, candidates, SkillsFor(conn), weaponClass);
        int Value(string stat) => Stats.Key(stat) is uint k ? supports.Sum(s => s.Stats.Where(v => v.Key == k).Sum(v => v.Value)) : 0;
        int level = EffectiveGemLevel(conn, selected.Place.Gem, Value("supported_active_skill_gem_level_+")
            + LocalBonus(selected.Place.Socket, selected.Place.Gem).Level);
        int quality = Quality(selected.Place.Socket, selected.Place.Gem) + Value("supported_active_skill_gem_quality_%")
            + GemQualityBonus(conn, selected.Place.Gem);
        var own = SkillsFor(conn).EffectStats(selected.Effect, level, quality).ToDictionary(kv => kv.Item1, kv => kv.Item2);
        foreach (var support in supports)
            foreach (var (key, value) in support.Stats)
            {
                string stat = Stats.Name(key) ?? "";
                if (stat is "base_spell_cast_time_ms" or "virtual_base_spell_cast_time_ms" or "hundred_times_casts_per_second"
                    or "casting_spell" or "base_mana_cost" or "mana_cost" or "base_mana_cost_per_minute" or "mana_cost_per_minute") continue;
                own[key] = stat.EndsWith("_final", StringComparison.Ordinal) && own.TryGetValue(key, out int previous)
                    ? (int)Math.Round(100 * ((1 + previous / 100.0) * (1 + value / 100.0) - 1))
                    : own.GetValueOrDefault(key) + value;
            }
        double costMultiplier = supports.Aggregate(1.0, (v, s) => v * s.CostMultiplier / 100.0);
        foreach (string stat in new[] { "mana_cost", "base_mana_cost", "mana_cost_per_minute", "base_mana_cost_per_minute" })
            if (Stats.Key(stat) is uint k && own.TryGetValue(k, out int cost)) own[k] = (int)Math.Floor(cost * costMultiplier);
        // Global added damage on support gems applies to this skill, not to the character or other link groups.
        foreach (string type in new[] { "physical", "fire", "cold", "lightning", "chaos" })
            foreach (string bound in new[] { "minimum", "maximum" })
                if (Stats.Key($"global_{bound}_added_{type}_damage") is uint k && own.TryGetValue(k, out int damage))
                    foreach (string kind in new[] { "attack", "spell" })
                        if (Stats.Key($"{kind}_{bound}_added_{type}_damage") is uint target)
                            own[target] = own.GetValueOrDefault(target) + damage;
        if (SkillsFor(conn).IsAura(selected.Effect, supports.Select(s => s.Effect.Row)))
            own[OwnAuraRestriction.BaseAura] = 1;
        own[OwnAuraRestriction.Aura] = OwnAuraRestriction.IsAura(own) ? 1 : 0;
        return new(selected.Effect, level, quality, supports, own, selected.Place.Gem,
            requirements?.Inactive.Contains(selected.Place.ItemId) != true
                && GemRequirementsUnmetAt(conn, selected.Place.Gem, requirements) is null);
    }

    static Dictionary<uint, int> SupportedStats(ClientState conn, ushort skillId)
    {
        var player = FinalPlayerStats(conn).ToDictionary(kv => kv.Item1, kv => kv.Item2);
        return MergeSkillStats(player, SupportedSkill(conn, skillId).Stats);
    }

    static Dictionary<uint, int> MergeSkillStats(IReadOnlyDictionary<uint, int> player, IReadOnlyDictionary<uint, int> own)
    {
        var result = new Dictionary<uint, int>(player);
        foreach (var (key, value) in own)
        {
            string stat = Stats.Name(key) ?? "";
            bool replace = stat is "mana_cost" or "base_mana_cost" or "casting_spell" or "virtual_base_spell_cast_time_ms"
                or "base_spell_cast_time_ms" or "hundred_times_casts_per_second" || stat.Contains("_base_", StringComparison.Ordinal)
                && (stat.StartsWith("spell_minimum", StringComparison.Ordinal) || stat.StartsWith("spell_maximum", StringComparison.Ordinal));
            result[key] = replace ? value : ArcaneMight.IsGrant(key)
                ? ArcaneMight.Combine(key, result.GetValueOrDefault(key), value) : result.GetValueOrDefault(key) + value;
        }
        return result;
    }

    static double SupportedAttackSeconds(ClientState conn, ushort skillId) => SupportedAttackSecondsFor(conn, skillId, null);

    // The supported attack's swing time with the off-hand weapon of a dual-wielded pair: its own duration at the
    // off-hand increased speed (the main weapon's local speed taken back out), with the same supports.
    static double SupportedOffHandAttackSeconds(ClientState conn, ushort skillId, SkillCatalog.Unarmed offHand) =>
        SupportedAttackSecondsFor(conn, skillId, offHand);

    static double SupportedAttackSecondsFor(ClientState conn, ushort skillId, SkillCatalog.Unarmed? offHand)
    {
        var stats = SupportedStats(conn, skillId);
        int Get(string stat) => Stats.Key(stat) is uint k ? stats.GetValueOrDefault(k) : 0;
        var gem = SupportedSkill(conn, skillId);
        if (Stats.Key("attack_speed_+%") is uint raw && Stats.Key("main_hand_attack_speed_+%") is uint final)
        {
            var profile = WeaponProfileFor(conn);
            double factor = profile.DualWield || profile.Unarmed ? 1.1 : 1;
            stats[final] = stats.GetValueOrDefault(final) + (int)Math.Round(gem.Stats.GetValueOrDefault(raw) * factor);
        }
        double more = Supports.AttackSpeedMultiplier(gem.Effect, gem.Level) * Math.Max(.01, 1 + Get("support_melee_physical_damage_attack_speed_+%_final") / 100.0)
            * Math.Max(.01, 1 + Get("support_multistrike_attack_speed_+%_final") / 100.0);
        double seconds = offHand is null ? WeaponFor(conn).AttackSeconds(stats)
            : DualWieldPanel.OffHandSeconds(offHand, SkillCatalog.Unarmed.OffHandAttackSpeedPct(
                stats.GetValueOrDefault(StatKeys.MainHandAttackSpeedPct), WeaponProfileFor(conn, activeSet: true).LocalAttackSpeedPct));
        return Math.Max(TickSeconds, seconds / more / PlayerActionFactor(conn));
    }

    static GameServer.Core.World.DamagePacket SupportedHit(ClientState conn, ushort skillId)
        => SupportedHitFor(conn, skillId, suppress: true);

    internal static GameServer.Core.World.DamagePacket SupportedProjectileHit(ClientState conn, ushort skillId, bool area)
        => SupportedHitFor(conn, skillId, suppress: false, area);

    // The same supported attack swung with the off-hand weapon of a dual-wielded attack (the Offence tab's Off Hand rows).
    static GameServer.Core.World.DamagePacket SupportedOffHandHit(ClientState conn, ushort skillId, SkillCatalog.Unarmed offHand)
        => SupportedHitFor(conn, skillId, suppress: true, weapon: offHand);

    // A supported skill's hit inputs before conversion and modifiers, shared by the hit itself and the panel's ailment
    // rows, which apply their own buckets to the same raw packet and conversion. Null when the skill cannot hit.
    sealed record SupportedHitInputs(GameServer.Core.World.DamagePacket Raw, Func<string, int> Get,
        GameServer.Core.World.DamageConversion Conversion, bool Attack, bool Melee, bool Area, bool Projectile,
        List<(string Name, int Value)> Modifiers, HashSet<int> Types, SkillHitPlan Plan, Dictionary<uint, int> Caster,
        bool WieldingWand = false);

    static SupportedHitInputs? SupportedHitInputsFor(ClientState conn, ushort skillId, bool? area = null,
        SkillCatalog.Unarmed? weapon = null)
    {
        var gem = SupportedSkill(conn, skillId);
        var caster = FinalPlayerStats(conn).ToDictionary(v => v.Item1, v => v.Item2);
        var stats = MergeSkillStats(caster, gem.Stats);
        int Get(string name) => name == "is_area_damage" && area is bool isArea ? isArea ? 1 : 0
            : Stats.Key(name) is uint k ? stats.GetValueOrDefault(k) : 0;
        bool attack = SkillsFor(conn).IsAttack(gem.Effect);
        var types = Supports.Of(gem.Effect)?.Types.ToHashSet() ?? [];
        if (area is true) types.Add(7);
        else if (area is false) types.Remove(7);
        var plan = SkillHitPlan.ForSkill(SkillsFor(conn), gem.Effect, gem.Level, gem.Quality);
        if (!plan.CanHit) return null;
        var raw = attack
            ? SpellDamage.WithEffectiveness(SupportedAttackDamage.Raw(weapon ?? WeaponFor(conn), Get), SkillsFor(conn).DamageEffectiveness(gem.Effect, gem.Level))
            : DamageStats.RawAdded(plan.SpellBase, Get, spell: true, AddedEffectiveness(conn, gem.Effect, gem.Level));
        var modifiers = gem.Supports.SelectMany(s => s.Stats).Select(s => (Stats.Name(s.Key) ?? "", s.Value)).ToList();
        return new(raw, Get, DamageStats.Conversion(stats, Stats, attack), attack,
            Melee: attack && types.Contains(19), Area: attack ? types.Contains(7) : Get("is_area_damage") != 0,
            Projectile: attack ? types.Contains(2) : Get("base_is_projectile") != 0, modifiers, types, plan, caster,
            WieldingWand: attack && (weapon ?? WeaponFor(conn)).WieldingWand);
    }

    static GameServer.Core.World.DamagePacket SupportedHitFor(ClientState conn, ushort skillId, bool suppress, bool? area = null,
        SkillCatalog.Unarmed? weapon = null)
        => SupportedHitInputsFor(conn, skillId, area, weapon) is { } inputs ? SupportedHitFrom(inputs, suppress) : GameServer.Core.World.DamagePacket.Empty;

    // The supported hit from inputs already worked out (GemSkills reads one gem's inputs once for its hit and ailment rows).
    static GameServer.Core.World.DamagePacket SupportedHitFrom(SupportedHitInputs inputs, bool suppress)
    {
        var hit = PlayerHitDamage.Resolve(inputs.Raw, inputs.Get, inputs.Conversion, spell: !inputs.Attack,
            melee: inputs.Melee, area: inputs.Area, projectile: inputs.Projectile,
            supportModifiers: inputs.Modifiers, skillTypes: inputs.Types,
            wieldingWand: inputs.WieldingWand, retaliation: inputs.Attack && inputs.Types.Contains(120));
        if (inputs.Attack) hit = MaximumAttackDamage.Apply(RageStats.ApplyAttack(hit, inputs.Get), inputs.Get);
        return suppress ? new SkillDamageContext(inputs.Plan.Kind, inputs.Caster).Hit(hit) : hit;
    }

    // The damaging-ailment rows of a supported skill's hit in one context (AilmentPanelStats): the main or off hand of
    // an attack with that hand's weapon, or a spell's / secondary-damage skill's hit.
    static List<(string Stat, int Value)> SupportedAilmentRows(ClientState conn, ushort skillId, string hand,
        SkillCatalog.Unarmed? weapon = null)
        => SupportedHitInputsFor(conn, skillId, weapon: weapon) is { } inputs ? SupportedAilmentRowsFrom(inputs, hand) : [];

    static List<(string Stat, int Value)> SupportedAilmentRowsFrom(SupportedHitInputs inputs, string hand)
    {
        var context = new AilmentPanelStats.Context(hand, Spell: !inputs.Attack, inputs.Melee, inputs.Area, inputs.Projectile);
        return AilmentPanelStats.Compute(context, inputs.Raw, inputs.Conversion, inputs.Get);
    }

    static AttackAccuracyProfile SupportedAccuracy(ClientState conn, ushort skillId)
    {
        var own = SupportedSkill(conn, skillId).Stats;
        long Own(string name) => Stats.Key(name) is uint key ? own.GetValueOrDefault(key) : 0;
        return AttackAccuracyProfile.From(conn.Pools, CharacterPlayerStats(conn), Stats, own, MainHandLocalSources(conn))
            with { Rating = AccuracyRating.Selected(conn.Pools, WeaponProfileFor(conn, activeSet: true), Own) };
    }

    static int SupportedCriticalChance(ClientState conn, ushort skillId) => SupportedCriticalChanceFor(conn, skillId, false);

    // A release retains the unprohibited offensive chance; the hit evaluates current owner prohibitions separately.
    static int SupportedReleaseCriticalChance(ClientState conn, ushort skillId) => SupportedCriticalChanceFor(conn, skillId, true);

    // extraIncrease: a target condition's increase (critical_strike_chance_+%_vs_blinded_enemies, Program.Blind).
    static int SupportedCriticalChanceFor(ClientState conn, ushort skillId, bool release, long extraIncrease = 0)
    {
        var gem = SupportedSkill(conn, skillId);
        var hitPlan = SkillHitPlan.ForSkill(SkillsFor(conn), gem.Effect, gem.Level, gem.Quality);
        if (!hitPlan.CanHit) return 0;
        var player = FinalPlayerStats(conn).ToDictionary(v => v.Item1, v => v.Item2);
        int Own(string name) => release && IsCriticalProhibition(name) ? 0
            : Stats.Key(name) is uint k ? gem.Stats.GetValueOrDefault(k) : 0;
        int Player(string name) => release && IsCriticalProhibition(name) ? 0
            : Stats.Key(name) is uint key ? player.GetValueOrDefault(key) : 0;
        if (SkillsFor(conn).IsAttack(gem.Effect))
        {
            if (new[] { "global_cannot_crit", "virtual_global_cannot_crit", "cannot_critical_strike_with_attacks" }
                .Any(name => Player(name) != 0 || Own(name) != 0)) return 0;
            var weapon = WeaponProfileFor(conn, activeSet: true);
            int basis = weapon.Unarmed && (!SkillsFor(conn).IsMelee(gem.Effect) || SkillsFor(conn).IsSpell(gem.Effect)) ? 0
                : AttackBaseCriticalStrike.OfMainHand(!weapon.Unarmed, weapon.Critical, Player);
            long increased = conn.Pools.CriticalStrikeChancePct + extraIncrease
                + AttackBaseCriticalStrike.IncreasedSum(weapon, name => Own(name));
            return DisplayedCriticalChance(conn, AttackBaseCriticalStrike.ChanceOfBase(basis, increased, Player, Own), Player);
        }
        return DisplayedCriticalChance(conn, SpellCriticalStrike.Chance(SkillsFor(conn).SpellCritChance(gem.Effect, gem.Level),
            Player, Own, conn.Pools.SpellCriticalStrikeChancePct + extraIncrease), Player);
    }

    static (int Flat, int Percent)? SupportedReservation(ClientState conn, ushort skillId)
    {
        var gem = SupportedSkill(conn, skillId);
        if (SkillsFor(conn).Reservation(gem.Effect, gem.Level) is not { } reservation) return null;
        double factor = gem.Supports.Aggregate(1.0, (v, support) => v * support.CostMultiplier / 100.0);
        return ((int)Math.Floor(reservation.Flat * factor), (int)Math.Floor(reservation.Percent * factor));
    }

    static int SupportedCriticalMultiplier(ClientState conn, ushort skillId)
    {
        var gem = SupportedSkill(conn, skillId);
        int extra = Stats.Key("base_critical_strike_multiplier_+") is uint k ? gem.Stats.GetValueOrDefault(k) : 0;
        return (int)Math.Clamp((long)(SkillsFor(conn).IsAttack(gem.Effect)
            ? AttackCriticalMultiplier(conn, CharacterPlayerStats(conn), gem.Effect, gem.Stats)
            : conn.Pools.SpellCriticalStrikeMultiplier) + extra, int.MinValue, int.MaxValue);
    }
}
