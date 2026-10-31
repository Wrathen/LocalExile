using System.Text.Json;
using System.Text.Json.Serialization;
using GameServer.Modding;

namespace GameServer.Modules.Ruthless;

/// <summary>Ruthless's rules, for the leagues that use this module.</summary>
public sealed class RuthlessModule : IModule
{
    /// <inheritdoc/>
    public void Load(IModuleContext module)
    {
        var rules = new RuthlessRulesSource(Environment.GetEnvironmentVariable("Game_RUTHLESS_RULES") ?? Path.Combine("config", "ruthless.json"), module.Log);
        module.ProvideLeagueRules(() => rules.Rules);
    }
}

/// <summary>Ruthless: its known mechanics, and the numbers the retail server does not publish, from a file that hot-reloads.</summary>
public sealed class RuthlessRulesSource(string settingsPath, Action<string>? log = null, TimeProvider? time = null)
{
    readonly RuthlessRulesFile _settings = new(settingsPath, log, time);
    readonly Lock _gate = new();
    RuthlessRules? _from;
    LeagueRules _rules = LeagueRules.Standard;

    /// <summary>The rules now.</summary>
    public LeagueRules Rules
    {
        get
        {
            var settings = _settings.Current;
            lock (_gate)
            {
                if (!ReferenceEquals(settings, _from)) (_from, _rules) = (settings, Of(settings));
                return _rules;
            }
        }
    }

    /// <summary>What the known mechanics are with these numbers.</summary>
    public static LeagueRules Of(RuthlessRules settings) => new()
    {
        ItemDropMultiplier = settings.ItemDropMultiplier,
        GoldMultiplier = settings.GoldMultiplier,
        EquipmentRarityMultiplier = settings.EquipmentRarityMultiplier,
        GemExperienceMultiplier = settings.GemExperienceMultiplier,
        VendorRewardMultiplier = settings.VendorRewardMultiplier,
        ExperiencePenaltyStartLevel = settings.ExperiencePenaltyStartLevel,
        ExperienceMultiplierPerLevel = settings.ExperienceMultiplierPerLevel,
        BossLifeRecoveryFraction = settings.CampaignBossLifeRecoveryFraction,
        BossRecoveryAreaPrefixes = ["1_", "2_"],
        PersistsVitals = true,
        TownRefillsFlasks = false,
        CraftingBench = false,
        TutorialGifts = false,
        UtilityFlaskChargesWhileActive = false,
        UtilityFlaskExceptionStat = "hard_mode_utility_flask_gain_charges_while_active",
        VitalRefillNpcs = ["Metadata/NPC/Act1/Nessa", "Metadata/NPC/Act2/Yeena", "Metadata/NPC/Act3/ClarissaTown", "Metadata/NPC/Act4/PetarusVanja", "Metadata/NPC/Act5/Lani", "Metadata/NPC/Act7/YeenaTown", "Metadata/NPC/Act8/ClarissaTown", "Metadata/NPC/Act9/PetarusVanja", "Metadata/NPC/Act10/Lani", "Metadata/NPC/Epilogue/Lani", "Metadata/NPC/Missions/Hideout/Helena", "*/Delve/DelveMiner"],
        // Siosa exchanges an Uncarved Gemstone for an active gem; no other vendor sells gems, and none sells supports.
        SellsMaps = false,
        FullGemStock = false,
        GemVendors = ["/Siosa"],
        SellsSupportGems = false,
        SellsGemsInGoods = false,
        ShopExcludedClasses = ["Ring", "Amulet", "Belt"],
        SupportGemRewards = false,
        // Explicit unavailable gems from GGG's Ruthless rules, also absent from the pinned Ruthless active index. A native
        // HardMode effect definition is not proof that its gem is obtainable. Quickstep remains allowed.
        UnavailableGemBases =
        [
            "SkillGemDash", "SkillGemFrostblink", "SkillGemFlameDash", "SkillGemLightningWarp", "SkillGemLeapSlam",
            "SkillGemShieldCharge", "SkillGemWhirlingBlades", "SkillGemBlinkArrow", "SkillGemSmokeMine", "SkillGemCorpseWarp",
            "SkillGemChargedDash", "SkillGemNewPhaseRun", "SkillGemPhaseRun", "SkillGemWitheringStep", "SkillGemVenomGyre",
            "SupportGemItemRarity",
        ],
        UnavailableGemEffects =
        [
            "Dash", "Frostblink", "FlameDash", "LightningWarp", "LeapSlam", "ShieldCharge", "WhirlingBlades", "BlinkArrow",
            "SmokeMine", "Bodyswap", "ChargedDash", "PhaseRun", "WitheringStep", "VenomGyre", "SupportItemRarity",
        ],
    };
}

/// <summary>LocalExile tuning for balance numbers the retail server does not publish.
/// Known Ruthless mechanics and native table overrides are enforced independently of these multipliers.</summary>
public sealed record RuthlessRules
{
    /// <summary>See <see cref="LeagueRules.ItemDropMultiplier"/>.</summary>
    public double ItemDropMultiplier { get; init; } = 0.20;
    /// <summary>See <see cref="LeagueRules.GoldMultiplier"/>.</summary>
    public double GoldMultiplier { get; init; } = 0.25;
    /// <summary>See <see cref="LeagueRules.EquipmentRarityMultiplier"/>.</summary>
    public double EquipmentRarityMultiplier { get; init; } = 0.25;
    /// <summary>See <see cref="LeagueRules.GemExperienceMultiplier"/>.</summary>
    public double GemExperienceMultiplier { get; init; } = 0.25;
    /// <summary>See <see cref="LeagueRules.VendorRewardMultiplier"/>.</summary>
    public double VendorRewardMultiplier { get; init; } = 0.20;
    /// <summary>See <see cref="LeagueRules.ExperiencePenaltyStartLevel"/>.</summary>
    public int ExperiencePenaltyStartLevel { get; init; } = 68;
    /// <summary>See <see cref="LeagueRules.ExperienceMultiplierPerLevel"/>.</summary>
    public double ExperienceMultiplierPerLevel { get; init; } = 0.90;
    /// <summary>See <see cref="LeagueRules.BossLifeRecoveryFraction"/>.</summary>
    public double CampaignBossLifeRecoveryFraction { get; init; } = 0.25;

    /// <summary>Throws <see cref="ArgumentException"/> for a number out of range.</summary>
    public void Validate()
    {
        foreach (var (name, value) in new[]
        {
            (nameof(ItemDropMultiplier), ItemDropMultiplier), (nameof(GoldMultiplier), GoldMultiplier),
            (nameof(EquipmentRarityMultiplier), EquipmentRarityMultiplier), (nameof(GemExperienceMultiplier), GemExperienceMultiplier),
            (nameof(VendorRewardMultiplier), VendorRewardMultiplier), (nameof(ExperienceMultiplierPerLevel), ExperienceMultiplierPerLevel),
            (nameof(CampaignBossLifeRecoveryFraction), CampaignBossLifeRecoveryFraction),
        })
            if (!double.IsFinite(value) || value < 0 || value > 1)
                throw new ArgumentException($"{name} must be between 0 and 1 inclusive");
        if (ExperiencePenaltyStartLevel is < 1 or > 100)
            throw new ArgumentException("ExperiencePenaltyStartLevel must be between 1 and 100");
    }
}

/// <summary>Reads at most once a second, publishes complete validated snapshots, and keeps the last good snapshot
/// through partial writes, invalid edits or temporary file failures. No server restart is required.</summary>
public sealed class RuthlessRulesFile(string path, Action<string>? log = null, TimeProvider? time = null)
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    readonly Lock gate = new();
    readonly TimeProvider clock = time ?? TimeProvider.System;
    RuthlessRules current = new();
    DateTimeOffset nextRead = DateTimeOffset.MinValue;
    (DateTime Modified, long Length)? seen;
    string? lastError;

    /// <summary>The numbers now.</summary>
    public RuthlessRules Current
    {
        get
        {
            lock (gate)
            {
                var now = clock.GetUtcNow();
                if (now < nextRead) return current;
                nextRead = now.AddSeconds(1);
                try
                {
                    var file = new FileInfo(path);
                    if (!file.Exists) throw new IOException($"settings file does not exist: {path}");
                    var stamp = (file.LastWriteTimeUtc, file.Length);
                    if (stamp == seen) return current;
                    // A failed read is retried next poll even if an editor preserves the file's timestamp.
                    var candidate = JsonSerializer.Deserialize<RuthlessRules>(File.ReadAllText(path), Json)
                        ?? throw new JsonException("settings must be a JSON object");
                    candidate.Validate();
                    current = candidate;
                    seen = stamp;
                    lastError = null;
                    log?.Invoke($"loaded settings from {Path.GetFullPath(path)}");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
                {
                    string error = e.Message;
                    if (error != lastError) log?.Invoke($"keeping previous settings: {error}");
                    lastError = error;
                }
                return current;
            }
        }
    }
}
