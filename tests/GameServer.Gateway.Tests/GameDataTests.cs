using System.Text.Json;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.World;
using GameServer.Storage;

namespace GameServer.Gateway.Tests;

public class GameDataTests
{
    [Fact]
    public void A_mod_range_override_changes_new_rolls_and_max_values()
    {
        var ranges = new ModRangeProvider();
        var catalog = new ModCatalog("data", ranges: ranges);
        var mod = catalog.ById("LocalAddedPhysicalDamage9");
        Assert.NotNull(mod);
        Assert.Equal(2, mod.Stats.Count);
        int baseMin = mod.Stats[0].Min, baseMax = mod.Stats[1].Max;

        ranges.Replace(new Dictionary<(string, int), (int, int)>
        {
            [(mod.Id, 0)] = (100, 100),
            [(mod.Id, 1)] = (200, 200),
        });

        var rolled = mod.Roll(ModSection.Explicit, new Random(1));
        Assert.Equal([100, 200], rolled.Values);
        Assert.Equal([100, 200], mod.MaxValues());
        // The reference row is unchanged; only the effective provider is overridden.
        Assert.Equal(baseMin, mod.Stats[0].Min);
        Assert.Equal(baseMax, mod.Stats[1].Max);
    }

    [Fact]
    public void A_mod_range_provider_normalises_a_reversed_range()
    {
        var ranges = new ModRangeProvider();
        ranges.Replace(new Dictionary<(string, int), (int, int)> { [("x", 0)] = (20, 10) });
        Assert.Equal((10, 20), ranges.Range("x", 0, 1, 2));
        Assert.Equal((1, 2), ranges.Range("missing", 0, 1, 2));
    }

    [Fact]
    public void Limit_settings_use_overrides_and_zero_means_unlimited()
    {
        var limits = new LimitSettings();
        Assert.Equal(1, limits.Get("timeless_jewel", 1));
        Assert.Equal(5, limits.Get("unknown", 5));

        limits.Replace(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["timeless_jewel"] = 2,
            ["impossible_escape"] = 0,
        });
        Assert.Equal(2, limits.Get("TIMELESS_JEWEL", 1));
        Assert.Equal(0, limits.Get("impossible_escape", 1));
        Assert.Equal(5, limits.Get("unknown", 5));
    }

    [Fact]
    public void The_game_data_store_round_trips_ranges_and_limits()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"localexile-game-data-{Guid.NewGuid():N}.db");
        try
        {
            using (var store = new GameDataStore(path))
            {
                store.SetModRange("LocalAddedPhysicalDamage9", 0, 100, 200);
                store.SetLimit("timeless_jewel", 2);
                Assert.Equal("2", store.Meta("schema_version"));
                Assert.Equal([new GameDataStore.ModStatOverride("LocalAddedPhysicalDamage9", 0, 100, 200)],
                    store.LoadModRanges());
                Assert.Equal([new GameDataStore.LimitOverride("timeless_jewel", 2)], store.LoadLimits());
            }

            using (var reopened = new GameDataStore(path))
            {
                Assert.Single(reopened.LoadModRanges());
                Assert.Single(reopened.LoadLimits());
                reopened.ClearModRange("LocalAddedPhysicalDamage9");
                reopened.ClearLimit("timeless_jewel");
                Assert.Empty(reopened.LoadModRanges());
                Assert.Empty(reopened.LoadLimits());
            }
        }
        finally
        {
            foreach (string file in new[] { path, path + "-wal", path + "-shm" })
                try { File.Delete(file); } catch (IOException) { }
        }
    }

    [Fact]
    public void A_fresh_database_gets_the_reference_tables_from_the_extracted_game_data()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"localexile-reference-{Guid.NewGuid():N}.db");
        try
        {
            using var store = new GameDataStore(path);
            Assert.True(GameDataReference.Seed(store));
            Assert.True(store.ReferenceSeeded());
            var (stats, mods, modStats) = store.ReferenceCounts();
            Assert.True(stats > 20_000, $"{stats} stat rows");
            Assert.True(mods > 40_000, $"{mods} mod rows");
            Assert.True(modStats > 60_000, $"{modStats} mod stat rows");
            // The seed runs once; a second startup keeps the extracted tables as they are.
            Assert.False(GameDataReference.Seed(store));
        }
        finally
        {
            foreach (string file in new[] { path, path + "-wal", path + "-shm" })
                try { File.Delete(file); } catch (IOException) { }
        }
    }

    [Fact]
    public void The_game_data_store_round_trips_catalog_rows_and_keeps_a_seeded_flag()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"localexile-catalog-{Guid.NewGuid():N}.db");
        try
        {
            using (var store = new GameDataStore(path))
            {
                Assert.False(store.CatalogSeeded("radius_jewel"));
                store.PutCatalogRow("radius_jewel", "Thread of Hope|Crimson Jewel", "{\"Name\":\"Thread of Hope\"}");
                store.PutCatalogRow("radius_jewel", "Disabled|Crimson Jewel", "{\"Name\":\"Disabled\"}", enabled: false);
                store.MarkCatalogSeeded("radius_jewel");
                Assert.True(store.CatalogSeeded("radius_jewel"));
                var rows = store.LoadCatalog("radius_jewel");
                Assert.Equal(2, rows.Count);
                Assert.False(rows.Single(row => row.Key == "Disabled|Crimson Jewel").Enabled);
                store.DeleteCatalogRow("radius_jewel", "Thread of Hope|Crimson Jewel");
                store.DeleteCatalogRow("radius_jewel", "Disabled|Crimson Jewel");
                Assert.Empty(store.LoadCatalog("radius_jewel"));
                Assert.True(store.CatalogSeeded("radius_jewel"));
            }
        }
        finally
        {
            foreach (string file in new[] { path, path + "-wal", path + "-shm" })
                try { File.Delete(file); } catch (IOException) { }
        }
    }

    [Fact]
    public void The_catalog_rows_round_trip_through_the_json_payload_shape()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var radius = RadiusJewelCatalogue.All.First(entry => entry.Name == "Thread of Hope");
        var radiusBack = JsonSerializer.Deserialize<RadiusJewelCatalogue.Entry>(JsonSerializer.Serialize(radius, options), options)!;
        Assert.Equal(radius.Name, radiusBack.Name);
        Assert.Equal(radius.Base, radiusBack.Base);
        Assert.Equal(radius.ConversionMod, radiusBack.ConversionMod);
        Assert.Equal(radius.Limit, radiusBack.Limit);
        Assert.Equal(radius.Radius, radiusBack.Radius);
        Assert.Equal(radius.GlobalMods, radiusBack.GlobalMods);
        Assert.Equal(radius.Variants!.Select(v => (v.Name, v.ModId, v.Value)), radiusBack.Variants!.Select(v => (v.Name, v.ModId, v.Value)));

        var timeless = TimelessJewelCatalog.All[0];
        var timelessBack = JsonSerializer.Deserialize<TimelessJewelCatalog.Entry>(
            JsonSerializer.Serialize(timeless, options), options)!;
        Assert.Equal(timeless.Name, timelessBack.Name);
        Assert.Equal(timeless.TreeVersion, timelessBack.TreeVersion);
        Assert.Equal(timeless.MinSeed, timelessBack.MinSeed);
        Assert.Equal(timeless.Conquerors.Select(c => (c.Name, c.Index, c.Version)),
            timelessBack.Conquerors.Select(c => (c.Name, c.Index, c.Version)));

        var unique = UniqueCatalog.Seed[0];
        var uniqueBack = JsonSerializer.Deserialize<UniqueCatalog.Definition>(
            JsonSerializer.Serialize(unique, options), options)!;
        Assert.Equal(unique.Name, uniqueBack.Name);
        Assert.Equal(unique.BaseTypeId, uniqueBack.BaseTypeId);
        Assert.Equal(unique.ItemLevel, uniqueBack.ItemLevel);
        Assert.Equal(unique.Mods, uniqueBack.Mods);

        // Rows written before these fields existed read as "keep the base's implicits, no overrides".
        var older = JsonSerializer.Deserialize<UniqueCatalog.Definition>(
            "{\"Name\":\"Old\",\"BaseTypeId\":\"x\",\"Mods\":[],\"ItemLevel\":255,\"Corrupted\":false}", options)!;
        Assert.Null(older.Implicits);
        Assert.Null(older.Ranges);
        Assert.False(older.Mirrored);
    }

    [Fact]
    public void System_settings_seed_the_requested_defaults()
    {
        Assert.Equal(3, SystemSettings.Seeds["socket_max_wand"]);
        Assert.Equal(6, SystemSettings.Seeds["socket_max_body_armour"]);
        Assert.Equal(4, SystemSettings.Seeds["socket_max_helmet"]);
        Assert.Equal(1, SystemSettings.Seeds["passive_points_per_level"]);
        Assert.Equal(1, SystemSettings.Seeds["gold_drop_multiplier"]);
        Assert.Equal(1, SystemSettings.Seeds["drop_multiplier_currency"]);
        Assert.Equal(1, SystemSettings.Seeds["monster_pack_density_multiplier"]);
        Assert.Equal(1, SystemSettings.Seeds["experience_multiplier"]);
        Assert.Equal(100, SystemSettings.Seeds["max_level"]);
        Assert.Equal(15, SystemSettings.Seeds["gold_auto_loot_radius"]);
        Assert.Equal(50, SystemSettings.Seeds["tattoo_total_limit"]);
        Assert.Equal(60, SystemSettings.Seeds["grace_period_seconds"]);
        Assert.Equal(3, SystemSettings.Seeds["rare_max_prefixes"]);
        Assert.Equal(3, SystemSettings.Seeds["rare_max_suffixes"]);
        Assert.Equal(2, SystemSettings.Seeds["rare_jewel_max_prefixes"]);
        Assert.Equal(2, SystemSettings.Seeds["rare_jewel_max_suffixes"]);
        Assert.Equal(45, SystemSettings.Seeds.Count);
    }

    [Fact]
    public void Rare_affix_caps_are_validated_as_integers_from_zero_to_six()
    {
        Assert.True(SystemSettings.Validate("rare_max_prefixes", 4, out _));
        Assert.True(SystemSettings.Validate("rare_max_suffixes", 6, out _));
        Assert.True(SystemSettings.Validate("rare_jewel_max_prefixes", 0, out _));
        Assert.False(SystemSettings.Validate("rare_max_prefixes", -1, out _));
        Assert.False(SystemSettings.Validate("rare_max_suffixes", 7, out _));
        Assert.False(SystemSettings.Validate("rare_jewel_max_suffixes", 2.5, out _));
    }

    [Fact]
    public void System_settings_map_item_classes_to_their_socket_caps()
    {
        var settings = new SystemSettings();
        settings.Replace(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["socket_max_wand"] = 6,
            ["socket_max_body_armour"] = 6,
            ["socket_max_belt"] = 10,
            ["gold_drop_multiplier"] = 2.5,
        });

        Assert.Equal(6, settings.SocketMax("Wand"));
        Assert.Equal(6, settings.SocketMax("Body Armour"));
        Assert.Equal(6, settings.SocketMax("Belt")); // clamped to the client's maximum
        Assert.Equal(0, settings.SocketMax("Unknown"));
        Assert.Equal(2.5, settings.GoldDropMultiplier);

        settings.Replace(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["drop_multiplier_currency"] = 2,
            ["drop_multiplier_maps"] = 0.5,
            ["monster_pack_density_multiplier"] = 3,
            ["monster_pack_size_multiplier"] = 2,
            ["experience_multiplier"] = 4,
            ["max_level"] = 50,
            ["gold_drop_chance_multiplier"] = 2,
            ["gold_auto_loot_radius"] = 20,
            ["tattoo_total_limit"] = 100,
            ["tattoo_group_limit"] = 2,
            ["timeless_jewel_limit"] = 3,
            ["impossible_escape_limit"] = 4,
            ["radius_jewel_default_limit"] = 5,
            ["grace_period_seconds"] = 30,
        });
        Assert.Equal(2, settings.DropMultiplier("currency"));
        Assert.Equal(0.5, settings.DropMultiplier("maps"));
        Assert.Equal(3, settings.MonsterPackDensityMultiplier);
        Assert.Equal(2, settings.MonsterPackSizeMultiplier);
        Assert.Equal(4, settings.ExperienceMultiplier);
        Assert.Equal(50, settings.MaxLevel);
        Assert.Equal(2, settings.GoldChanceMultiplier);
        Assert.Equal(20, settings.GoldAutoLootRadius);
        Assert.Equal(100, settings.TattooTotalLimit);
        Assert.Equal(2, settings.TattooGroupLimit);
        Assert.Equal(3, settings.TimelessJewelLimit);
        Assert.Equal(4, settings.ImpossibleEscapeLimit);
        Assert.Equal(5, settings.RadiusJewelDefaultLimit);
        Assert.Equal(30, settings.GracePeriodSeconds);
    }

    [Fact]
    public void System_setting_validation_rejects_bad_socket_and_point_values()
    {
        Assert.False(SystemSettings.Validate("socket_max_wand", 7, out _));
        Assert.False(SystemSettings.Validate("socket_max_wand", 2.5, out _));
        Assert.True(SystemSettings.Validate("socket_max_wand", 6, out _));
        Assert.False(SystemSettings.Validate("passive_points_per_level", 0, out _));
        Assert.False(SystemSettings.Validate("passive_points_per_level", 101, out _));
        Assert.False(SystemSettings.Validate("gold_drop_multiplier", -1, out _));
        Assert.False(SystemSettings.Validate("drop_multiplier_currency", -1, out _));
        Assert.False(SystemSettings.Validate("experience_multiplier", -1, out _));
        Assert.False(SystemSettings.Validate("max_level", 101, out _));
        Assert.False(SystemSettings.Validate("grace_period_seconds", 3601, out _));
        Assert.False(SystemSettings.Validate("unknown", 1, out _));
    }

    [Fact]
    public void Passive_points_per_level_is_a_multiplier_after_level_one()
    {
        Assert.Equal(0, GameServer.Core.World.Progression.PassivePoints(1, 2));
        Assert.Equal(198, GameServer.Core.World.Progression.PassivePoints(100, 2));
        Assert.Equal(99, GameServer.Core.World.Progression.PassivePoints(100, 1));
    }

    [Fact]
    public void The_game_data_store_round_trips_system_settings()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"localexile-settings-{Guid.NewGuid():N}.db");
        try
        {
            using (var store = new GameDataStore(path))
            {
                Assert.False(store.SettingsSeeded());
                store.SetSetting("gold_drop_multiplier", 2.5);
                store.SetSetting("socket_max_wand", 6);
                store.MarkSettingsSeeded();
                Assert.True(store.SettingsSeeded());
                var rows = store.LoadSettings();
                Assert.Equal(2, rows.Count);
                Assert.Equal(2.5, rows.Single(row => row.Key == "gold_drop_multiplier").Value);
                store.DeleteSetting("socket_max_wand");
                Assert.Single(store.LoadSettings());
            }
        }
        finally
        {
            foreach (string file in new[] { path, path + "-wal", path + "-shm" })
                try { File.Delete(file); } catch (IOException) { }
        }
    }

    [Fact]
    public void The_default_socket_capacity_still_matches_the_game()
    {
        var type = Program.Items.Find("Driftwood Wand")!;
        var item = ItemCatalog.Create(type);
        Assert.Equal(3, CurrencyEffects.SocketCapacity(item, type));
        var body = Program.Items.Find("Plate Vest")!;
        Assert.Equal(6, CurrencyEffects.SocketCapacity(ItemCatalog.Create(body), body));
    }

    [Fact]
    public void The_generated_unique_catalogue_covers_the_full_pob_export()
    {
        Assert.True(UniqueItemSeed.All.Length >= 1200);
        Assert.Contains(UniqueItemSeed.All, entry => entry.Name == "Mageblood");
        Assert.Contains(UniqueItemSeed.All, entry => entry.Name == "Seven-League Step");
        Assert.Equal(3, UniqueItemSeed.All.Count(entry => entry.Name == "Grand Spectrum"));
        Assert.Equal(1297, UniqueCatalog.Seed.Length);
    }

    sealed record SeedRow(string Key, string Value);

    static string CatalogDatabase() =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"localexile-refresh-{Guid.NewGuid():N}.db");

    static void DeleteDatabase(string path)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (string file in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }

    static string Payload(GameDataStore store, string key) =>
        store.LoadCatalog("test").Single(row => row.Key == key).Payload;

    [Fact]
    public void A_corrected_seed_reaches_unedited_rows_and_leaves_edits_and_deletions_alone()
    {
        string path = CatalogDatabase();
        try
        {
            using var store = new GameDataStore(path);
            SeedRow[] first = [new("same", "old"), new("edited", "old"), new("deleted", "old"), new("moved", "old")];
            GameDataCatalog.Seed(store, "test", first, row => row.Key);
            GameDataCatalog.RefreshSeed(store, "test", first, row => row.Key, "1");
            Thread.Sleep(20);
            store.PutCatalogRow("test", "edited", "{\"Key\":\"edited\",\"Value\":\"admin\"}");
            store.DeleteCatalogRow("test", "deleted");

            SeedRow[] second = [new("same", "new"), new("edited", "new"), new("deleted", "new"), new("moved2", "new")];
            GameDataCatalog.Seed(store, "test", second, row => row.Key);
            GameDataCatalog.RefreshSeed(store, "test", second, row => row.Key, "2");

            var keys = store.LoadCatalog("test").Select(row => row.Key).ToHashSet();
            Assert.Contains("new", Payload(store, "same"));
            Assert.Contains("admin", Payload(store, "edited"));
            Assert.DoesNotContain("deleted", keys);
            Assert.DoesNotContain("moved", keys);
            Assert.Contains("moved2", keys);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void A_seed_revision_applies_once_and_keeps_a_later_edit()
    {
        string path = CatalogDatabase();
        try
        {
            using var store = new GameDataStore(path);
            SeedRow[] seed = [new("row", "old")];
            GameDataCatalog.Seed(store, "test", seed, row => row.Key);
            GameDataCatalog.RefreshSeed(store, "test", seed, row => row.Key, "1");
            Thread.Sleep(20);
            store.PutCatalogRow("test", "row", "{\"Key\":\"row\",\"Value\":\"admin\"}");

            GameDataCatalog.RefreshSeed(store, "test", [new SeedRow("row", "new")], row => row.Key, "1");
            Assert.Contains("admin", Payload(store, "row"));
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void A_disabled_row_stays_edited_through_a_seed_revision()
    {
        string path = CatalogDatabase();
        try
        {
            using var store = new GameDataStore(path);
            GameDataCatalog.Seed(store, "test", [new SeedRow("row", "old")], row => row.Key);
            GameDataCatalog.RefreshSeed(store, "test", [new SeedRow("row", "old")], row => row.Key, "1");
            Thread.Sleep(20);
            store.PutCatalogRow("test", "row", Payload(store, "row"), enabled: false);

            GameDataCatalog.RefreshSeed(store, "test", [new SeedRow("row", "new")], row => row.Key, "2");
            var row = store.LoadCatalog("test").Single();
            Assert.False(row.Enabled);
            Assert.Contains("old", row.Payload);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void A_gold_multiplier_above_one_increases_the_pile()
    {
        var drops = new ItemDrops(Program.Items);
        var normal = drops.Gold(10, MonsterRarity.Unique, new Random(7), 1);
        var doubled = drops.Gold(10, MonsterRarity.Unique, new Random(7), 2);
        Assert.NotNull(normal);
        Assert.NotNull(doubled);
        Assert.True(doubled.Item.StackSize > normal.Item.StackSize);
    }

    [Fact]
    public void A_tattoo_group_limit_can_be_raised_without_a_restart()
    {
        var warlord = TattooCatalogue.Find("Honoured Tattoo of the Warlord")!;
        var mountain = TattooCatalogue.Find("Honoured Tattoo of the Mountain")!;
        var tattoos = new Dictionary<ushort, string> { [100] = warlord.Name };

        Assert.Equal("only one Attribute Notable Tattoo can be applied",
            Program.TattooLimitRefusal(tattoos, 200, mountain));

        var limits = new LimitSettings();
        limits.Replace(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["tattoo:Attribute Notable Tattoo"] = 2,
        });
        Assert.Null(Program.TattooLimitRefusal(tattoos, 200, mountain, limits));
    }
}
