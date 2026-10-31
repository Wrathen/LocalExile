using GameServer.Core.World;

partial class Program
{
    /// <summary>
    /// The Offence tab's secondary-damage rows (issue #75) for a selected skill that is neither an attack nor a spell
    /// but deals secondary damage (<c>display_skill_deals_secondary_damage</c>: Bear Trap, Herald of Ice's shatter):
    /// its own <c>secondary_*_base_*_damage</c> hit through conversion with the generic, area and projectile buckets
    /// only (<see cref="PlayerHitDamage"/>, no added attack or spell damage), the twelve <c>secondary_*_damage</c>
    /// ranges, the critical chance (the skill's base with the generic increases) and multiplier, the critical-weighted
    /// Damage per Use (these skills ask for the average, not DPS) and the secondary ailment rows. Display rows only:
    /// the trap's and herald's actual hits are not executed by the server.
    /// </summary>
    internal static List<(string Stat, int Value)> SecondaryPanelRows(ClientState conn, Func<string, int> get,
        IReadOnlyDictionary<uint, int> player, SupportedGem supported, DamagePacket secondaryBase)
    {
        int Player(string name) => Stats.Key(name) is uint key ? player.GetValueOrDefault(key) : 0;
        int Own(string name) => Stats.Key(name) is uint key ? supported.Stats.GetValueOrDefault(key) : 0;
        var types = Supports.Of(supported.Effect)?.Types.ToHashSet() ?? [];
        var merged = MergeSkillStats(player, supported.Stats);
        var conversion = DamageStats.Conversion(merged, Stats, attack: false);
        var modifiers = supported.Supports.SelectMany(s => s.Stats).Select(s => (Stats.Name(s.Key) ?? "", s.Value)).ToList();
        bool area = get("is_area_damage") != 0, projectile = get("base_is_projectile") != 0;
        var hit = PlayerHitDamage.Resolve(secondaryBase, get, conversion, spell: false, area: area, projectile: projectile,
            supportModifiers: modifiers, skillTypes: types, secondary: true);
        int chance = SpellCriticalStrike.Chance(SkillsFor(conn).SpellCritChance(supported.Effect, supported.Level), Player, Own,
            playerIncrease: Player("critical_strike_chance_+%"));
        int multiplier = (int)Math.Clamp((long)DamageModel.BaseCriticalStrikeMultiplier
            + Player("base_critical_strike_multiplier_+") + Own("base_critical_strike_multiplier_+"), int.MinValue, int.MaxValue);
        double factor = CriticalStrikePolicy.DamageFactor(CriticalProhibitedBy(player, attack: false, supported.Stats) ? 0 : chance,
            multiplier, CriticalExtraRollsFor(conn, player, supported.Stats));
        var rows = new List<(string, int)> { ("display_skill_deals_secondary_damage", 1) };
        // The skill inherits the default attack's hand rows with the player's stats: clear them, as a spell does.
        foreach (string hand in new[] { "main_hand", "off_hand" })
            rows.AddRange(PanelStats.DamageRows(hand).Concat(AilmentPanelStats.Rows(hand)).Select(n => (n, 0)));
        var ranges = PanelStats.HandDamage(hit, "secondary").ToDictionary(r => r.Stat, r => r.Value);
        rows.AddRange(PanelStats.DamageRows("secondary").Select(stat => (stat, ranges.GetValueOrDefault(stat))));
        rows.Add(("secondary_critical_strike_chance", chance));
        rows.Add(("secondary_critical_strike_multiplier", multiplier));
        rows.Add(("display_hundred_times_damage_per_skill_use", SpellDamage.Display((hit.MinTotal, hit.MaxTotal), 0, factor).PerUse));
        rows.Add(("hundred_times_damage_per_second", 0));
        rows.AddRange(AilmentPanelStats.Compute(new("secondary", Spell: false, Area: area, Projectile: projectile),
            secondaryBase, conversion, get));
        return rows;
    }
}
