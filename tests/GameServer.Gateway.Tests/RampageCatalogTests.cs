namespace GameServer.Gateway.Tests;

public class RampageCatalogTests
{
    // TestEnvironment's shared module initializer owns the CWD. Production catalogs keep their normal data/ default.
    static readonly Lazy<RampageCatalog> Real = new(() => new());
    static bool TablesAvailable => RampageCatalog.RequiredTables.All(name => File.Exists(Path.Combine("data", "tables", name + ".json")));

    [Fact]
    public void Actual_client_thresholds_join_all_thirteen_exact_daemon_and_granted_effect_rows()
    {
        if (!TablesAvailable) return;
        var catalog = Real.Value;
        Assert.Null(catalog.Error);
        Assert.Equal(new[] { 15, 30, 45, 70, 100, 135, 175, 220, 270, 325, 385, 450, 520 }, catalog.Thresholds);
        Assert.Equal(new[] { 3396, 3395, 3397, 3401, 3403, 3398, 3406, 3399, 3402, 3404, 3407, 3400, 3405 },
            catalog.Tiers.Select(t => t.Daemon.Row));
        Assert.Equal(new[] { 3791, 3790, 2951, 3961, 3963, 2952, 3966, 2953, 3962, 3964, 3967, 2954, 3965 },
            catalog.Tiers.Select(t => t.Skill.Row));
        Assert.All(catalog.Tiers, tier =>
        {
            Assert.True(tier.Daemon.IsPlayerMinion);
            Assert.Same(tier.Skill, Assert.Single(tier.Daemon.Skills));
            Assert.Contains(1, tier.Skill.ActiveSkillTypes); // Their spell type belongs to the daemon, not its owner.
            Assert.NotEmpty(tier.Skill.Levels);
            Assert.All(tier.Skill.Levels, level =>
            {
                Assert.Equal(tier.Skill.Row, level.GrantedLevelData.GetProperty("GrantedEffect").GetInt32());
                Assert.Equal(tier.Skill.StatSetRow, level.StatLevelData.GetProperty("StatSet").GetInt32());
                Assert.Equal(level.Level, level.GrantedLevelData.GetProperty("Level").GetInt32());
            });
        });
    }

    [Fact]
    public void Actual_native_count_stats_and_first_threshold_feed_the_pure_state()
    {
        if (!TablesAvailable) return;
        var catalog = Real.Value;
        Assert.Null(catalog.Error);
        Assert.Equal((ushort)328, RampageCatalog.BuffDefinitionRow);
        Assert.Equal((uint)2379, catalog.StatKey("current_rampage_stacks"));
        Assert.Equal((uint)2380, catalog.StatKey("max_rampage_stacks"));
        var state = new RampageState(catalog.Thresholds);
        Assert.Empty(state.GainQualified(0, 14).CrossedThresholds);
        Assert.Equal(new[] { 15 }, state.GainQualified(0).CrossedThresholds);
    }

    [Fact]
    public void Actual_tier_plans_preserve_stun_blind_repeats_and_corpse_damage_without_claiming_execution()
    {
        if (!TablesAvailable) return;
        var catalog = Real.Value;
        Assert.Null(catalog.Error);
        RampageCatalog.SkillLevel Level(int threshold) => catalog.Tiers.Single(t => t.Threshold == threshold).Skill.AtLevel(1)!;
        Assert.Equal(1, Level(15).Get("always_stun"));
        Assert.Equal(250, Level(15).Get("base_stun_duration_+%"));
        Assert.Equal(25, Level(30).Get("explode_on_kill_%_fire_damage_to_deal"));
        Assert.Equal(10, Level(70).Get("ice_nova_number_of_repeats"));
        Assert.Equal(-10, Level(70).Get("ice_nova_radius_+%_per_repeat"));
        Assert.Equal(100, Level(100).Get("global_chance_to_blind_on_hit_%"));
        Assert.Equal(150, Level(100).Get("blind_duration_+%"));
        Assert.Equal(3, Level(100).Get("number_of_chains"));
        Assert.Equal(4, Level(175).Get("skill_repeat_count"));
        Assert.Equal(20, Level(270).Get("ice_nova_number_of_repeats"));
        Assert.Equal(-5, Level(270).Get("ice_nova_radius_+%_per_repeat"));
        Assert.Equal(24, Level(325).Get("corpse_explosion_monster_life_%"));
        Assert.Equal(1, Level(325).Get("detonate_dead_chain_explode"));
        Assert.Equal(8, Level(385).Get("skill_repeat_count"));
        Assert.Equal(8, Level(520).Get("skill_repeat_count"));
        Assert.Equal(25, Level(520).Get("explode_on_kill_%_fire_damage_to_deal"));
        Assert.Equal(1, Level(520).Get("always_stun"));
        foreach (var tier in catalog.Tiers)
            foreach (var (name, value) in tier.Skill.AtLevel(1)!.Stats)
                Assert.Equal(value, tier.Skill.AtLevel(1)!.WireStats[catalog.StatKey(name)!.Value]);
    }

    [Fact]
    public void Actual_animated_weapon_plans_use_the_correct_varieties_counts_and_lifetimes()
    {
        if (!TablesAvailable) return;
        var catalog = Real.Value;
        Assert.Null(catalog.Error);
        Assert.Equal(3192, catalog.AnimatedWeapons.Single(w => w.Kind == RampageCatalog.WeaponKind.Dagger).Variety.Row);
        Assert.Equal(3193, catalog.AnimatedWeapons.Single(w => w.Kind == RampageCatalog.WeaponKind.Bow).Variety.Row);
        var sword = catalog.AnimatedWeapons.Single(w => w.Kind == RampageCatalog.WeaponKind.TwoHandedSword);
        Assert.Equal(3194, sword.Variety.Row);
        Assert.Equal("WhiteMonsterCyclone", Assert.Single(sword.Variety.Skills).Id);
        Assert.Contains(sword.Variety.Mods.SelectMany(m => m.Stats), s => s.Name == "cyclone_extra_distance" && s.Minimum == 10);
        Assert.All(catalog.AnimatedWeapons, weapon =>
        {
            // The target's type flag is false for these animated weapons. Ownership must be assigned by their
            // summoning executor; do not rewrite the target data or confuse this flag with the owner relation.
            Assert.False(weapon.Variety.IsPlayerMinion);
            Assert.Contains(weapon.Variety.Mods.SelectMany(m => m.Stats), s => s.Name == "attack_speed_+%" && s.Minimum == 40 && s.Maximum == 40);
            Assert.Contains(weapon.Variety.Mods.SelectMany(m => m.Stats), s => s.Name == "monster_no_drops_or_experience" && s.Minimum == 1);
        });
        var expected = new[]
        {
            (45, RampageCatalog.WeaponKind.Dagger, 4),
            (135, RampageCatalog.WeaponKind.Bow, 6),
            (220, RampageCatalog.WeaponKind.TwoHandedSword, 8),
            (450, RampageCatalog.WeaponKind.Dagger, 5),
            (450, RampageCatalog.WeaponKind.Bow, 5),
            (450, RampageCatalog.WeaponKind.TwoHandedSword, 5),
        };
        Assert.Equal(expected, catalog.Tiers.SelectMany(t => t.Summons.Select(s => (t.Threshold, s.Weapon.Kind, s.Count))));
        Assert.All(catalog.Tiers.SelectMany(t => t.Summons), summon => Assert.Equal(37500, summon.DurationMs));
    }

    [Fact]
    public void Missing_fixture_data_reports_an_error_and_never_falls_back_to_another_checkouts_data()
    {
        // No file writes, process CWD changes, parent searches or absolute production defaults.
        var catalog = new RampageCatalog(Path.Combine("data", "nonexistent-rampage-fixture-" + Guid.NewGuid().ToString("N")));
        Assert.NotNull(catalog.Error);
        Assert.Empty(catalog.Tiers);
        Assert.Empty(catalog.AnimatedWeapons);
        Assert.Null(catalog.StatKey("current_rampage_stacks"));
    }
}
