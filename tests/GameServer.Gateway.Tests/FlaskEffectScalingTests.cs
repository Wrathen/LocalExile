using GameServer.Core.Protocol.Instance;

namespace GameServer.Gateway.Tests;

public sealed class FlaskEffectScalingTests
{
    static string Data()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "data", "tables"))) return Path.Combine(dir.FullName, "data");
        throw new DirectoryNotFoundException("data/tables");
    }
    static readonly Lazy<ItemCatalog> Normal = new(() => new ItemCatalog(Data()));
    static ItemCatalog Catalog() => Normal.Value;
    static ItemMod Mod(ItemCatalog catalog, string id, int domain, params int[] values)
    {
        var mod = catalog.Mods.ById(id)!;
        Assert.Equal(domain, mod.Domain);
        Assert.Equal(values.Length, mod.Stats.Count);
        for (int i = 0; i < values.Length; i++) Assert.InRange(values[i], mod.Stats[i].Min, mod.Stats[i].Max);
        return new(ModSection.Explicit, mod.Hash, values);
    }
    // Exact native sources on isolated carriers. Cross-base combinations are not legal acquisition claims.
    static Dictionary<string, int> Pair(ItemCatalog catalog, bool duration = false, bool magic = false)
    {
        var mods = new List<ItemMod>
        {
            Mod(catalog, "BeltIncreasedFlaskEffectUnique__2", 1, -60),
            Mod(catalog, "FlaskEffectPerLevelUnique__1", 1, 1),
        };
        if (duration)
        {
            mods.Add(Mod(catalog, "BeltIncreasedFlaskDurationUnique__4", 1, 150));
            mods.Add(Mod(catalog, "FlaskDurationPerLevelUnique__1", 1, -2));
        }
        if (magic) mods.Add(Mod(catalog, "RecombinatorSpecialMagicUtilityFlaskEffect", 1, 20));
        var carrier = ItemCatalog.Create(catalog.Find("Iron Ring")!) with { Mods = mods };
        return ItemModStats.Sum([carrier], catalog.Mods);
    }
    static Item Flask(ItemCatalog catalog, string name, byte quality = 0, params ItemMod[] mods) =>
        ItemCatalog.Create(catalog.Find(name)!) with
        { Quality = quality, Mods = mods, Rarity = mods.Length == 0 ? ItemRarity.Normal : ItemRarity.Magic };

    [Theory]
    [InlineData(10, -50, 35, 25, 750)]
    [InlineData(20, -40, 42, 30, 900)]
    [InlineData(100, 40, 98, 70, 2100)]
    public void Native_flat_and_level_pair_scales_each_flask_category_once(int level,
        int global, double life, double mana, int armour)
    {
        var catalog = Catalog(); var raw = Pair(catalog); int Get(string name) => raw.GetValueOrDefault(name);
        Assert.Equal(global, FlaskEffectScaling.GlobalIncrease(Get, level));
        var lifeType = catalog.FlaskOf(catalog.Find("Small Life Flask")!)!;
        var manaType = catalog.FlaskOf(catalog.Find("Small Mana Flask")!)!;
        Assert.Equal(life, FlaskRecovery.For(lifeType, Flask(catalog, "Small Life Flask"), new Dictionary<string, int>(), Get, 100, 1000, level).Life, 8);
        Assert.Equal(mana, FlaskRecovery.For(manaType, Flask(catalog, "Small Mana Flask"), new Dictionary<string, int>(), Get, 100, 1000, level).Mana, 8);
        var granite = catalog.UtilityFlaskOf(catalog.Find("Granite Flask")!)!;
        Assert.Equal(armour, Assert.Single(UtilityFlasks.For(granite, Flask(catalog, "Granite Flask"), new Dictionary<string, int>(), Get, level).Stats).Value);
        Assert.Equal(-60, raw["flask_effect_+%"]);
        Assert.Equal(1, raw["flask_effect_+%_per_level"]);
    }

    [Theory]
    [InlineData(0, 1, 100, 140, 3000)]
    [InlineData(-60, 0, -60, 28, 600)]
    [InlineData(0, 0, 0, 70, 1500)]
    public void Raw_flat_and_per_level_replacements_are_independent(int flat, int perLevel, int global,
        double life, int armour)
    {
        var catalog = Catalog(); var raw = Pair(catalog);
        raw["flask_effect_+%"] = flat; raw["flask_effect_+%_per_level"] = perLevel;
        int Get(string name) => raw.GetValueOrDefault(name);
        Assert.Equal(global, FlaskEffectScaling.GlobalIncrease(Get, 100));
        Assert.Equal(life, FlaskRecovery.For(catalog.FlaskOf(catalog.Find("Small Life Flask")!)!,
            Flask(catalog, "Small Life Flask"), new Dictionary<string, int>(), Get, 100, 1000, 100).Life, 8);
        Assert.Equal(armour, Assert.Single(UtilityFlasks.For(catalog.UtilityFlaskOf(catalog.Find("Granite Flask")!)!,
            Flask(catalog, "Granite Flask"), new Dictionary<string, int>(), Get, 100).Stats).Value);
    }

    [Fact]
    public void Resolver_reads_each_raw_source_once_and_uses_the_explicit_level_or_existing_default()
    {
        var reads = new List<string>();
        int Get(string name) { reads.Add(name); return name switch { "flask_effect_+%" => -60, "flask_effect_+%_per_level" => 1, _ => 777 }; }
        Assert.Equal(1.4, FlaskEffectScaling.Multiplier(Get, 100), 9);
        Assert.Equal(["flask_effect_+%", "flask_effect_+%_per_level"], reads);
        Assert.Equal(-59, FlaskEffectScaling.GlobalIncrease(Get));
        Assert.Equal(-59, FlaskEffectScaling.GlobalIncrease(Get, 0));
        Assert.Equal(40, FlaskEffectScaling.GlobalIncrease(Get, int.MaxValue));
    }

    [Fact]
    public void Wide_effect_arithmetic_has_no_upper_balance_cap_and_buff_projection_saturates_only_int_representation()
    {
        int Large(string _) => int.MaxValue;
        Assert.Equal(216895848347L, FlaskEffectScaling.GlobalIncrease(Large, 100));
        Assert.Equal(2211908157.41, FlaskEffectScaling.Multiplier(Large, 100, int.MaxValue, int.MaxValue), 6);
        Assert.Equal(int.MaxValue, UtilityFlasks.Scale(int.MaxValue, FlaskEffectScaling.Multiplier(Large, 100)));
        Assert.Equal(int.MinValue, UtilityFlasks.Scale(int.MinValue, FlaskEffectScaling.Multiplier(Large, 100)));
        Assert.Equal(int.MinValue, Assert.Single(UtilityFlasks.Strongest(
            [new("same effect", "controlled_signed_stat", int.MaxValue), new("same effect", "controlled_signed_stat", int.MinValue)])).Value);
        Assert.Equal(0, FlaskEffectScaling.Multiplier(_ => int.MinValue, 100));
    }

    [Theory]
    [InlineData(4.824)]
    public void Native_local_prefix_and_magic_only_source_join_effect_while_quality_and_duration_stay_separate(double seconds)
    {
        var catalog = Catalog(); var raw = Pair(catalog, magic: true); int Get(string name) => raw.GetValueOrDefault(name);
        // Native Alchemist's1: -33 local duration, +25 local effect. Do not invent a standalone +30 recipe.
        var item = Flask(catalog, "Granite Flask", 20, Mod(catalog, "FlaskEffectReducedDuration1", 2, -33, 25));
        var use = UtilityFlasks.For(catalog.UtilityFlaskOf(catalog.Find("Granite Flask")!)!, item, ItemModStats.Local(item, catalog.Mods), Get, 100);
        Assert.Equal(2775, Assert.Single(use.Stats).Value);
        Assert.Equal(seconds, use.Seconds, 8);
        var normal = UtilityFlasks.For(catalog.UtilityFlaskOf(catalog.Find("Granite Flask")!)!, Flask(catalog, "Granite Flask"),
            new Dictionary<string, int>(), Get, 100);
        Assert.Equal(2100, Assert.Single(normal.Stats).Value); // Magic-only20 does not apply to normal utility.
    }

    [Fact]
    public void Real_instant_recipe_keeps_quality_and_instant_duration_rules_and_ignores_magic_utility_effect()
    {
        var catalog = Catalog(); var raw = Pair(catalog, duration: true, magic: true); int Get(string name) => raw.GetValueOrDefault(name);
        var item = Flask(catalog, "Small Life Flask", 20, Mod(catalog, "FlaskFullInstantRecovery1", 2, 1, -66));
        var use = FlaskRecovery.For(catalog.FlaskOf(catalog.Find("Small Life Flask")!)!, item, ItemModStats.Local(item, catalog.Mods), Get, 100, 1000, 100);
        Assert.Equal(39.984, use.InstantLife, 8);
        Assert.Equal(0, use.Life); Assert.Equal(0, use.Seconds);
    }

    [Fact]
    public void Native_partial_instant_recipe_preserves_the_independent_duration_and_rate_factors()
    {
        var catalog = Catalog(); var raw = Pair(catalog, duration: true); int Get(string name) => raw.GetValueOrDefault(name);
        var item = Flask(catalog, "Small Life Flask", 0, Mod(catalog, "FlaskPartialInstantRecovery1", 2, 50, -52, 135));
        var use = FlaskRecovery.For(catalog.FlaskOf(catalog.Find("Small Life Flask")!)!, item, ItemModStats.Local(item, catalog.Mods), Get, 100, 1000, 20);
        Assert.Equal(10.08, use.InstantLife, 8); Assert.Equal(21.168, use.Life, 8);
        Assert.Equal(2.680851063829787, use.Seconds, 8);
    }

    [Fact]
    public void Native_low_life_more_recovery_is_not_multiplied_by_the_new_global_effect()
    {
        var catalog = Catalog(); var raw = Pair(catalog); int Get(string name) => raw.GetValueOrDefault(name);
        var item = Flask(catalog, "Small Life Flask", 0, Mod(catalog, "FlaskIncreasedRecoveryOnLowLife1", 2, 101));
        var local = ItemModStats.Local(item, catalog.Mods); var type = catalog.FlaskOf(catalog.Find("Small Life Flask")!)!;
        Assert.Equal(168.7, FlaskRecovery.For(type, item, local, Get, 100, 1000, 100).Life, 8);
        Assert.Equal(98, FlaskRecovery.For(type, item, local, Get, 900, 1000, 100).Life, 8);
    }

    // A flask's Onslaught takes the flask's effect (wiki Silver Flask, Onslaught: since 3.20), added to the character's
    // increased Onslaught effect; flags stay as they are.
    [Fact]
    public void Level_scaled_effect_leaves_flags_and_scales_a_flask_Onslaught()
    {
        var catalog = Catalog(); var raw = Pair(catalog); raw["flask_effect_+%_per_level"] = 0;
        int Get(string name) => raw.GetValueOrDefault(name); // Controlled raw-coefficient replacement at actual100.
        var quartz = UtilityFlasks.For(catalog.UtilityFlaskOf(catalog.Find("Quartz Flask")!)!, Flask(catalog, "Quartz Flask"), new Dictionary<string, int>(), Get, 100);
        Assert.Contains(quartz.Stats, s => s.Stat == "phase_through_objects" && s.Value == 1);
        Assert.Contains(quartz.Stats, s => s.Stat == "base_spell_suppression_chance_%" && s.Value == 4);
        int[] Silver() => [.. new[] { "attack_speed_+%", "base_cast_speed_+%", "base_movement_velocity_+%" }.Select(name =>
            UtilityFlasks.For(catalog.UtilityFlaskOf(catalog.Find("Silver Flask")!)!, Flask(catalog, "Silver Flask"),
                new Dictionary<string, int>(), Get, 100).Stats.Single(s => s.Stat == name).Value)];
        Assert.Equal([8, 8, 8], Silver()); // 20 × (1 − 60%)
        raw["onslaught_effect_+%"] = 60;
        Assert.Equal([20, 20, 20], Silver()); // 20 × (1 − 60% + 60%)
    }
}
