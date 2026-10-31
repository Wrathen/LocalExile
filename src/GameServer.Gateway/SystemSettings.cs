using GameServer.Core.World;
using GameServer.Storage;

/// <summary>
/// Numeric server settings that used to be constants in code. The SQLite <c>system_settings</c> table is seeded with
/// the defaults on the first run; the in-memory snapshot is replaced on <c>/game_data reload</c>. Unknown keys fall back
/// to the seed, so deleting a row restores the code default.
/// </summary>
sealed class SystemSettings
{
    public static readonly IReadOnlyDictionary<string, double> Seeds = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
    {
        ["socket_max_wand"] = 3,
        ["socket_max_one_hand_sword"] = 3,
        ["socket_max_thrusting_one_hand_sword"] = 3,
        ["socket_max_one_hand_axe"] = 3,
        ["socket_max_one_hand_mace"] = 3,
        ["socket_max_sceptre"] = 3,
        ["socket_max_dagger"] = 3,
        ["socket_max_rune_dagger"] = 3,
        ["socket_max_claw"] = 3,
        ["socket_max_two_hand_sword"] = 6,
        ["socket_max_two_hand_axe"] = 6,
        ["socket_max_two_hand_mace"] = 6,
        ["socket_max_bow"] = 6,
        ["socket_max_staff"] = 6,
        ["socket_max_warstaff"] = 6,
        ["socket_max_body_armour"] = 6,
        ["socket_max_helmet"] = 4,
        ["socket_max_gloves"] = 4,
        ["socket_max_boots"] = 4,
        ["socket_max_shield"] = 3,
        ["socket_max_belt"] = 0,
        ["rare_max_prefixes"] = 3,
        ["rare_max_suffixes"] = 3,
        ["rare_jewel_max_prefixes"] = 2,
        ["rare_jewel_max_suffixes"] = 2,
        ["passive_points_per_level"] = 1,
        ["gold_drop_multiplier"] = 1,
        ["gold_drop_chance_multiplier"] = 1,
        ["gold_auto_loot_radius"] = 15,
        // The client caps one projectile movement instance at 150 units = 15 m (10 units per metre). Cells use the
        // same scale: 150 cells = 15 m. Fork, chain and split start a new instance with a fresh budget.
        ["projectile_maximum_range_cells"] = 150,
        ["drop_multiplier_currency"] = 1,
        ["drop_multiplier_equipment"] = 1,
        ["drop_multiplier_flasks"] = 1,
        ["drop_multiplier_gems"] = 1,
        ["drop_multiplier_maps"] = 1,
        ["drop_multiplier_unique_rarity"] = 1,
        ["monster_pack_density_multiplier"] = 1,
        ["monster_pack_size_multiplier"] = 1,
        ["experience_multiplier"] = 1,
        ["max_level"] = 100,
        ["tattoo_total_limit"] = 50,
        ["tattoo_group_limit"] = 1,
        ["timeless_jewel_limit"] = 1,
        ["impossible_escape_limit"] = 1,
        ["radius_jewel_default_limit"] = 0,
        ["grace_period_seconds"] = 60,
    };

    static readonly Dictionary<string, string> SocketKeys = new(StringComparer.Ordinal)
    {
        ["Wand"] = "socket_max_wand",
        ["One Hand Sword"] = "socket_max_one_hand_sword",
        ["Thrusting One Hand Sword"] = "socket_max_thrusting_one_hand_sword",
        ["One Hand Axe"] = "socket_max_one_hand_axe",
        ["One Hand Mace"] = "socket_max_one_hand_mace",
        ["Sceptre"] = "socket_max_sceptre",
        ["Dagger"] = "socket_max_dagger",
        ["Rune Dagger"] = "socket_max_rune_dagger",
        ["Claw"] = "socket_max_claw",
        ["Two Hand Sword"] = "socket_max_two_hand_sword",
        ["Two Hand Axe"] = "socket_max_two_hand_axe",
        ["Two Hand Mace"] = "socket_max_two_hand_mace",
        ["Bow"] = "socket_max_bow",
        ["Staff"] = "socket_max_staff",
        ["Warstaff"] = "socket_max_warstaff",
        ["Body Armour"] = "socket_max_body_armour",
        ["Helmet"] = "socket_max_helmet",
        ["Gloves"] = "socket_max_gloves",
        ["Boots"] = "socket_max_boots",
        ["Shield"] = "socket_max_shield",
        ["Belt"] = "socket_max_belt",
    };

    static readonly IReadOnlyDictionary<string, double> Empty = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    public static SystemSettings Default { get; } = new();

    IReadOnlyDictionary<string, double> _values = Empty;

    public IReadOnlyDictionary<string, double> Values => Volatile.Read(ref _values);

    public double Get(string key) =>
        Values.TryGetValue(key, out double value) ? value : Seeds.GetValueOrDefault(key, 0);

    public int GetInt(string key) => (int)Math.Round(Get(key), MidpointRounding.AwayFromZero);

    public int SocketMax(string itemClass) =>
        SocketKeys.TryGetValue(itemClass, out string? key)
            ? Math.Clamp(GetInt(key), 0, SocketLayout.MaximumSockets)
            : 0;

    /// <summary>The most prefixes/suffixes one rare item may have. Jewels keep their own, lower defaults.</summary>
    public const int MaxRareAffixesPerSide = 6;
    public int RareMaxPrefixes => Math.Clamp(GetInt("rare_max_prefixes"), 0, MaxRareAffixesPerSide);
    public int RareMaxSuffixes => Math.Clamp(GetInt("rare_max_suffixes"), 0, MaxRareAffixesPerSide);
    public int RareJewelMaxPrefixes => Math.Clamp(GetInt("rare_jewel_max_prefixes"), 0, MaxRareAffixesPerSide);
    public int RareJewelMaxSuffixes => Math.Clamp(GetInt("rare_jewel_max_suffixes"), 0, MaxRareAffixesPerSide);

    public int PassivePointsPerLevel => Math.Clamp(GetInt("passive_points_per_level"), 1, 100);

    public double GoldDropMultiplier => Math.Max(0, Get("gold_drop_multiplier"));
    public double GoldChanceMultiplier => Math.Clamp(Get("gold_drop_chance_multiplier"), 0, 100);
    public double GoldAutoLootRadius => Math.Clamp(Get("gold_auto_loot_radius"), 0, 100);

    /// <summary>One projectile movement instance's maximum flight, in cells (10 cells = 1 m): 150 = the client's 15 m
    /// cap. Fork, chain and split start a new movement instance with a fresh budget.</summary>
    public int ProjectileMaximumRangeCells => Math.Clamp(GetInt("projectile_maximum_range_cells"), 1, 10_000);

    public double DropMultiplier(string category) => category switch
    {
        "currency" => Math.Max(0, Get("drop_multiplier_currency")),
        "equipment" => Math.Max(0, Get("drop_multiplier_equipment")),
        "flasks" => Math.Max(0, Get("drop_multiplier_flasks")),
        "gems" => Math.Max(0, Get("drop_multiplier_gems")),
        "maps" => Math.Max(0, Get("drop_multiplier_maps")),
        "unique_rarity" => Math.Max(0, Get("drop_multiplier_unique_rarity")),
        _ => 1,
    };

    public double MonsterPackDensityMultiplier => Math.Clamp(Get("monster_pack_density_multiplier"), 0, 100);
    public double MonsterPackSizeMultiplier => Math.Clamp(Get("monster_pack_size_multiplier"), 0, 50);
    public double ExperienceMultiplier => Math.Clamp(Get("experience_multiplier"), 0, 1000);
    public int MaxLevel => Math.Clamp(GetInt("max_level"), 1, Progression.MaxLevelCeiling);

    public int TattooTotalLimit => Math.Max(0, GetInt("tattoo_total_limit"));
    public int TattooGroupLimit => Math.Max(0, GetInt("tattoo_group_limit"));
    public int TimelessJewelLimit => Math.Max(0, GetInt("timeless_jewel_limit"));
    public int ImpossibleEscapeLimit => Math.Max(0, GetInt("impossible_escape_limit"));
    public int RadiusJewelDefaultLimit => Math.Max(0, GetInt("radius_jewel_default_limit"));
    public int GracePeriodSeconds => Math.Clamp(GetInt("grace_period_seconds"), 0, 3600);

    public void Replace(IReadOnlyDictionary<string, double> values) =>
        Volatile.Write(ref _values, new Dictionary<string, double>(values, StringComparer.OrdinalIgnoreCase));

    public static void ResetToSeeds()
    {
        Default.Replace(Seeds);
        Progression.PassivePointsPerLevel = 1;
        Progression.ExperienceMultiplier = 1;
        Progression.MaxLevel = Progression.MaxLevelCeiling;
    }

    public static void Reload(GameDataStore store)
    {
        // Add keys introduced by a newer build without overwriting an edited row. The seeded flag is informational;
        // the missing-key check is what makes an upgrade fill the new rows.
        var existing = store.LoadSettings().Select(row => row.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in Seeds)
            if (!existing.Contains(key)) store.SetSetting(key, value);
        if (!store.SettingsSeeded()) store.MarkSettingsSeeded();
        var loaded = store.LoadSettings().ToDictionary(row => row.Key, row => row.Value, StringComparer.OrdinalIgnoreCase);
        Default.Replace(loaded);
        Progression.PassivePointsPerLevel = Default.PassivePointsPerLevel;
        Progression.ExperienceMultiplier = Default.ExperienceMultiplier;
        Progression.MaxLevel = Default.MaxLevel;
    }

    public static bool Validate(string key, double value, out string? error)
    {
        error = null;
        if (!Seeds.ContainsKey(key))
        {
            error = $"unknown setting \"{key}\"";
            return false;
        }
        if (key.StartsWith("socket_max_", StringComparison.OrdinalIgnoreCase))
        {
            if (value < 0 || value > SocketLayout.MaximumSockets || value != Math.Round(value))
                error = $"{key} is an integer 0..{SocketLayout.MaximumSockets}";
        }
        else if (key is "rare_max_prefixes" or "rare_max_suffixes" or "rare_jewel_max_prefixes" or "rare_jewel_max_suffixes")
        {
            if (value < 0 || value > MaxRareAffixesPerSide || value != Math.Round(value))
                error = $"{key} is an integer 0..{MaxRareAffixesPerSide}";
        }
        else if (key.Equals("passive_points_per_level", StringComparison.OrdinalIgnoreCase))
        {
            if (value < 1 || value > 100 || value != Math.Round(value))
                error = "passive_points_per_level is an integer 1..100";
        }
        else if (key.Equals("projectile_maximum_range_cells", StringComparison.OrdinalIgnoreCase))
        {
            if (value < 1 || value > 10_000 || value != Math.Round(value))
                error = "projectile_maximum_range_cells is an integer 1..10000 (cells; 10 cells = 1 m)";
        }
        else if (key.StartsWith("drop_multiplier_", StringComparison.OrdinalIgnoreCase))
        {
            if (!double.IsFinite(value) || value < 0 || value > 100)
                error = $"{key} is a number 0..100";
        }
        else if (key.Equals("gold_drop_multiplier", StringComparison.OrdinalIgnoreCase))
        {
            if (!double.IsFinite(value) || value < 0 || value > 10000)
                error = "gold_drop_multiplier is a number 0..10000";
        }
        else if (key.Equals("gold_drop_chance_multiplier", StringComparison.OrdinalIgnoreCase)
            || key.Equals("gold_auto_loot_radius", StringComparison.OrdinalIgnoreCase))
        {
            if (!double.IsFinite(value) || value < 0 || value > 100)
                error = $"{key} is a number 0..100";
        }
        else if (key.Equals("monster_pack_density_multiplier", StringComparison.OrdinalIgnoreCase))
        {
            if (!double.IsFinite(value) || value < 0 || value > 100)
                error = "monster_pack_density_multiplier is a number 0..100";
        }
        else if (key.Equals("monster_pack_size_multiplier", StringComparison.OrdinalIgnoreCase))
        {
            if (!double.IsFinite(value) || value < 0 || value > 50)
                error = "monster_pack_size_multiplier is a number 0..50";
        }
        else if (key.Equals("experience_multiplier", StringComparison.OrdinalIgnoreCase))
        {
            if (!double.IsFinite(value) || value < 0 || value > 1000)
                error = "experience_multiplier is a number 0..1000";
        }
        else if (key.Equals("max_level", StringComparison.OrdinalIgnoreCase))
        {
            if (value < 1 || value > Progression.MaxLevelCeiling || value != Math.Round(value))
                error = $"max_level is an integer 1..{Progression.MaxLevelCeiling}";
        }
        else if (key.Equals("grace_period_seconds", StringComparison.OrdinalIgnoreCase))
        {
            if (value < 0 || value > 3600 || value != Math.Round(value))
                error = "grace_period_seconds is an integer 0..3600";
        }
        else if (key is "tattoo_total_limit" or "tattoo_group_limit" or "timeless_jewel_limit"
            or "impossible_escape_limit" or "radius_jewel_default_limit")
        {
            if (value < 0 || value > 1000 || value != Math.Round(value))
                error = $"{key} is an integer 0..1000";
        }
        return error is null;
    }
}
