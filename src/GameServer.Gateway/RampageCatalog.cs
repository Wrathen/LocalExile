using System.Collections.Frozen;
using System.Text.Json;

/// <summary>
/// Read-only 3.29.3b Rampage execution plans, not an effect executor. Thresholds join their actual daemon variety,
/// granted effect, GEPL and stat-set level rows. Animated weapons retain their own variety/mod/skill records.
/// Production data always defaults to the repository's data/; a fixture may supply a different folder explicitly.
/// </summary>
sealed class RampageCatalog(string folder = "data")
{
    public const ushort BuffDefinitionRow = 328;
    // Native Game.exe: HUD constructor 0x14024b480, row328 observer 0x14024c320 (ID 0x149),
    // count/threshold selector 0x14024ce60 (Buff+0x40), HUD update 0x14024c760 (duration +0x1c/+0x18).
    // Existing 0x1c1's stacks and duration fields feed that HUD; this catalog sends no packets.
    public static readonly IReadOnlyList<string> RequiredTables = Array.AsReadOnly(new[]
    {
        "KillstreakThresholds", "MonsterVarieties", "MonsterTypes", "Mods", "GrantedEffects",
        "GrantedEffectsPerLevel", "GrantedEffectStatSets", "GrantedEffectStatSetsPerLevel", "Stats",
        "ActiveSkills", "ActionTypes", "BuffDefinitions",
    });

    public enum Kind { StunNova, ExplodeNova, AnimateWeapons, RippleNova, BlindNova, RepeatNova,
        SuperRippleNova, CorpseExplosions, SuperRepeatNova, CombinedNova }
    public enum WeaponKind { Dagger, Bow, TwoHandedSword }

    public sealed record ModStat(uint Key, string Name, int Minimum, int Maximum);
    public sealed record ModPlan(int Row, string Id, IReadOnlyList<ModStat> Stats);
    public sealed record SkillLevel(int GrantedLevelRow, int StatLevelRow, int Level,
        IReadOnlyDictionary<string, int> Stats, IReadOnlyDictionary<uint, int> WireStats,
        JsonElement GrantedLevelData, JsonElement StatLevelData)
    {
        public int Get(string stat) => Stats.GetValueOrDefault(stat);
        public int CooldownMs => GrantedLevelData.GetProperty("Cooldown").GetInt32();
        public int StoredUses => GrantedLevelData.GetProperty("StoredUses").GetInt32();
        public int SpellCriticalChance => StatLevelData.GetProperty("SpellCritChance").GetInt32();
    }
    public sealed record SkillPlan(int Row, string Id, int StatSetRow, int CastTimeMs, ushort ActionHash,
        IReadOnlyList<int> ActiveSkillTypes, IReadOnlyList<SkillLevel> Levels, JsonElement EffectData,
        JsonElement StatSetData)
    {
        /// <summary>The greatest actual gem-level row not exceeding the requested level, as SkillCatalog selects.</summary>
        public SkillLevel? AtLevel(int level) => level < 1 ? null : Levels.LastOrDefault(r => r.Level <= level);
    }
    public sealed record VarietyPlan(int Row, string Id, int MonsterTypeRow, bool IsPlayerMinion,
        IReadOnlyList<ModPlan> Mods, IReadOnlyList<SkillPlan> Skills, JsonElement VarietyData, JsonElement MonsterTypeData);
    public sealed record AnimatedWeapon(WeaponKind Kind, VarietyPlan Variety);
    public sealed record Summon(AnimatedWeapon Weapon, int Count, int DurationMs);
    public sealed record Tier(int Threshold, Kind Effect, VarietyPlan Daemon, SkillPlan Skill, IReadOnlyList<Summon> Summons);

    sealed record Data(string? Error, IReadOnlyList<Tier> Tiers, IReadOnlyList<AnimatedWeapon> Weapons,
        IReadOnlyDictionary<string, uint> StatKeys);
    readonly Lazy<Data> data = new(() => Load(folder));
    public string? Error => data.Value.Error;
    public IReadOnlyList<Tier> Tiers => data.Value.Tiers;
    public IReadOnlyList<AnimatedWeapon> AnimatedWeapons => data.Value.Weapons;
    public IReadOnlyList<int> Thresholds => Array.AsReadOnly(Tiers.Select(t => t.Threshold).ToArray());
    public uint? StatKey(string name) => data.Value.StatKeys.TryGetValue(name, out uint key) ? key : null;

    static readonly (int Threshold, int Variety, int Effect, string Id, Kind Kind)[] Expected =
    [
        (15, 3396, 3791, "RampageDaemonStunNova", Kind.StunNova),
        (30, 3395, 3790, "RampageDaemonExplodeNova", Kind.ExplodeNova),
        (45, 3397, 2951, "RampageDaemonAnimateWeapon", Kind.AnimateWeapons),
        (70, 3401, 3961, "RampageDaemonRippleNova", Kind.RippleNova),
        (100, 3403, 3963, "RampageDaemonBlindNova", Kind.BlindNova),
        (135, 3398, 2952, "RampageDaemonAnimateWeapon2", Kind.AnimateWeapons),
        (175, 3406, 3966, "RampageDaemonRepeatNova", Kind.RepeatNova),
        (220, 3399, 2953, "RampageDaemonAnimateWeapon3", Kind.AnimateWeapons),
        (270, 3402, 3962, "RampageDaemonSuperRippleNova", Kind.SuperRippleNova),
        (325, 3404, 3964, "RampageDaemonVaalDetonateDead", Kind.CorpseExplosions),
        (385, 3407, 3967, "RampageDaemonSuperRepeatNova", Kind.SuperRepeatNova),
        (450, 3400, 2954, "RampageDaemonAnimateWeapon4", Kind.AnimateWeapons),
        (520, 3405, 3965, "RampageDaemonCombinedNova", Kind.CombinedNova),
    ];

    static Data Load(string folder)
    {
        var documents = new Dictionary<string, JsonDocument>(StringComparer.Ordinal);
        try
        {
            foreach (string table in RequiredTables)
            {
                string path = Path.Combine(folder, "tables", table + ".json");
                if (!File.Exists(path)) return Empty($"Rampage requires {path}.");
                documents.Add(table, JsonDocument.Parse(File.ReadAllBytes(path)));
                Require(documents[table].RootElement.ValueKind == JsonValueKind.Array, $"{table} is not a row array.");
            }
            JsonElement Rows(string table) => documents[table].RootElement;
            JsonElement Row(string table, int row)
            {
                var rows = Rows(table);
                Require(row >= 0 && row < rows.GetArrayLength(), $"Invalid {table} row {row}.");
                return rows[row];
            }
            var statKeys = Rows("Stats").EnumerateArray().Select((r, i) => (Name: Text(r, "Id"), Key: (uint)i + 1))
                .ToFrozenDictionary(r => r.Name, r => r.Key, StringComparer.Ordinal);
            string StatName(int row) => Text(Row("Stats", row), "Id");
            var buff = Row("BuffDefinitions", BuffDefinitionRow);
            Require(Text(buff, "Id") == "rampage_stack" && buff.GetProperty("ShowCount").GetBoolean()
                && buff.GetProperty("CancelOnDeath").GetBoolean(), "Rampage buff definition changed.");
            Require(StatName(Int(buff, "Current_StatsKey")) == "current_rampage_stacks"
                && StatName(Int(buff, "Maximum_StatsKey")) == "max_rampage_stacks", "Rampage buff count stats changed.");
            var grantedLevels = Rows("GrantedEffectsPerLevel").EnumerateArray().Select((r, i) => (Row: r, Index: i))
                .GroupBy(r => Int(r.Row, "GrantedEffect")).ToDictionary(g => g.Key, g => g.ToArray());
            var statLevels = Rows("GrantedEffectStatSetsPerLevel").EnumerateArray().Select((r, i) => (Row: r, Index: i))
                .GroupBy(r => Int(r.Row, "StatSet")).ToDictionary(g => g.Key, g => g.ToArray());
            var skills = new Dictionary<int, SkillPlan>();
            SkillPlan Skill(int index)
            {
                if (skills.TryGetValue(index, out var existing)) return existing;
                var effect = Row("GrantedEffects", index);
                int setIndex = Int(effect, "StatSet1");
                var set = Row("GrantedEffectStatSets", setIndex);
                var active = Row("ActiveSkills", Int(effect, "ActiveSkill"));
                var action = Row("ActionTypes", Int(active, "ActionType"));
                var constants = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (int stat in Ints(set, "ImplicitStats")) constants[StatName(stat)] = 1;
                var constantKeys = new HashSet<string>(StringComparer.Ordinal);
                AddPairs(constants, set, "ConstantStats", "ConstantStatsValues", false, constantKeys);
                Require(grantedLevels.TryGetValue(index, out var gl), $"Missing GEPL for effect {index}.");
                Require(statLevels.TryGetValue(setIndex, out var sl), $"Missing GESSPL for effect {index}.");
                var levels = new List<SkillLevel>();
                foreach (var entry in gl!.OrderBy(e => Int(e.Row, "Level")))
                {
                    int level = Int(entry.Row, "Level");
                    var candidates = sl!.Where(s => Int(s.Row, "GemLevel") <= level)
                        .OrderBy(s => Int(s.Row, "GemLevel")).ToArray();
                    Require(candidates.Length > 0, $"Missing GESSPL for effect {index} level {level}.");
                    var statsAtLevel = candidates[^1];
                    var values = new Dictionary<string, int>(constants, StringComparer.Ordinal);
                    AddPairs(values, statsAtLevel.Row, "FloatStats", "BaseResolvedValues", true, constantKeys);
                    AddPairs(values, statsAtLevel.Row, "AdditionalStats", "AdditionalStatsValues", true, constantKeys);
                    foreach (int flag in Ints(statsAtLevel.Row, "AdditionalFlags")) values[StatName(flag)] = 1;
                    levels.Add(new(entry.Index, statsAtLevel.Index, level, values.ToFrozenDictionary(StringComparer.Ordinal),
                        values.ToFrozenDictionary(p => statKeys[p.Key], p => p.Value), entry.Row.Clone(), statsAtLevel.Row.Clone()));
                }
                var plan = new SkillPlan(index, Text(effect, "Id"), setIndex, Int(effect, "CastTime"),
                    checked((ushort)Int(action, "HASH16")), Array.AsReadOnly(Ints(active, "ActiveSkillTypes")),
                    levels.AsReadOnly(), effect.Clone(), set.Clone());
                skills.Add(index, plan);
                return plan;
            }
            void AddPairs(Dictionary<string, int> target, JsonElement row, string keys, string values, bool addConstants,
                HashSet<string> constantKeys)
            {
                int[] ids = Ints(row, keys), amounts = Ints(row, values);
                Require(ids.Length == amounts.Length, $"Rampage {keys}/{values} lengths differ.");
                for (int i = 0; i < ids.Length; i++)
                {
                    string name = StatName(ids[i]);
                    target[name] = checked((addConstants && constantKeys.Contains(name) ? target.GetValueOrDefault(name) : 0) + amounts[i]);
                    if (!addConstants) constantKeys.Add(name);
                }
            }
            VarietyPlan Variety(int index)
            {
                var row = Row("MonsterVarieties", index);
                int typeIndex = Int(row, "MonsterTypesKey");
                var type = Row("MonsterTypes", typeIndex);
                var mods = new List<ModPlan>();
                foreach (int modIndex in Ints(row, "ModsKeys").Concat(Ints(row, "ModsKeys2")))
                {
                    var mod = Row("Mods", modIndex);
                    var stats = new List<ModStat>();
                    for (int i = 1; i <= 8; i++)
                        if (mod.TryGetProperty($"StatsKey{i}", out var key) && key.ValueKind == JsonValueKind.Number)
                        {
                            int stat = key.GetInt32();
                            stats.Add(new((uint)stat + 1, StatName(stat), Int(mod, $"Stat{i}Min"), Int(mod, $"Stat{i}Max")));
                        }
                    mods.Add(new(modIndex, Text(mod, "Id"), stats.AsReadOnly()));
                }
                return new(index, Text(row, "Id"), typeIndex, type.GetProperty("IsPlayerMinion").GetBoolean(),
                    mods.AsReadOnly(), Array.AsReadOnly(Ints(row, "GrantedEffectsKeys").Select(Skill).ToArray()),
                    row.Clone(), type.Clone());
            }
            var weapons = new[]
            {
                new AnimatedWeapon(WeaponKind.Dagger, Variety(3192)),
                new AnimatedWeapon(WeaponKind.Bow, Variety(3193)),
                new AnimatedWeapon(WeaponKind.TwoHandedSword, Variety(3194)),
            };
            for (int i = 0; i < weapons.Length; i++)
                Require(weapons[i].Variety.Id == $"Metadata/Monsters/AnimatedItem/AnimatedWeaponRampage{i + 1}",
                    "Rampage animated weapon variety changed.");
            var thresholds = Rows("KillstreakThresholds");
            Require(thresholds.GetArrayLength() == Expected.Length, "Rampage threshold count changed.");
            var tiers = new List<Tier>();
            for (int i = 0; i < Expected.Length; i++)
            {
                var expected = Expected[i];
                var threshold = thresholds[i];
                Require(Int(threshold, "Kills") == expected.Threshold
                    && Int(threshold, "MonsterVarietiesKey") == expected.Variety, "Rampage threshold/variety join changed.");
                var daemon = Variety(expected.Variety);
                string daemonId = expected.Threshold switch
                {
                    45 => "RampageDaemonAnimateWeaponsDaggers",
                    135 => "RampageDaemonAnimateWeaponsBows",
                    220 => "RampageDaemonAnimateWeaponsSwords",
                    450 => "RampageDaemonAnimateWeaponsArmy",
                    _ => expected.Id,
                };
                Require(daemon.Id == "Metadata/Monsters/Daemon/" + daemonId, "Rampage daemon metadata changed.");
                Require(daemon.IsPlayerMinion && daemon.Skills.Count == 1, "Rampage daemon must be one owned skill.");
                var skill = daemon.Skills[0];
                Require(skill.Row == expected.Effect && skill.Id == expected.Id, "Rampage daemon/effect join changed.");
                var summons = new List<Summon>();
                var baseLevel = skill.AtLevel(1);
                Require(baseLevel is not null, "Rampage tier has no level-one skill row.");
                foreach (var weapon in weapons)
                {
                    string countStat = weapon.Kind switch
                    {
                        WeaponKind.Dagger => "number_of_animated_daggers_to_summon",
                        WeaponKind.Bow => "number_of_animated_bows_to_summon",
                        _ => "number_of_animated_two_handed_swords_to_summon",
                    };
                    int count = baseLevel!.Get(countStat);
                    if (count == 0) continue;
                    int duration = baseLevel.Get("base_skill_effect_duration");
                    Require(count is > 0 and <= 1000 && duration > 0, "Invalid Rampage summon count/duration.");
                    summons.Add(new(weapon, count, duration));
                }
                Require(expected.Kind != Kind.AnimateWeapons || summons.Count > 0, "Rampage weapon tier has no summons.");
                tiers.Add(new(expected.Threshold, expected.Kind, daemon, skill, summons.AsReadOnly()));
            }
            return new(null, tiers.AsReadOnly(), Array.AsReadOnly(weapons), statKeys);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException
            or InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException)
        {
            return Empty($"Rampage data unavailable: {e.Message}");
        }
        finally { foreach (var document in documents.Values) document.Dispose(); }
    }

    static Data Empty(string error) => new(error, [], [], FrozenDictionary<string, uint>.Empty);
    static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    static int Int(JsonElement row, string name) => row.GetProperty(name).GetInt32();
    static string Text(JsonElement row, string name) => row.GetProperty(name).GetString() ?? "";
    static int[] Ints(JsonElement row, string name) => [.. row.GetProperty(name).EnumerateArray().Select(v => v.GetInt32())];
}
