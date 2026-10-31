using System.Text.Json;
using GameServer.Core.Protocol.Instance;

/// <summary>
/// The monsters /spawn_mob can create: MonsterVarieties rows from the extracted tables
/// (data/tables/MonsterVarieties.json, Agents/GettingStarted.md "Extracting the game's data tables"), with each type's
/// deserialized components from data/object-types.json (`python -I scripts/re/ot_components.py --all --json
/// data/object-types.json`). Without that file every variety is assumed to have the base Monster list, which is wrong
/// for about half of them (StateMachine, DiesAfterTime …) and then the client shows "Buffer Underflow".
/// Both files are game data: they stay in data/ and are loaded on first use.
/// </summary>
sealed class MonsterCatalog(string folder = "data")
{
    public sealed record Variety(int Row, string Id, string Name, string BaseType, string? AnimatedObject, IReadOnlyList<string>? Components,
        int LifeMultiplier = 100, int MonsterType = -1, StateMachineData? StateMachine = null, byte TransitionStates = 1,
        int MovementSpeed = 30, int ObjectSize = 3, int AggroRange = 60, IReadOnlyList<int>? GrantedEffects = null,
        int AttackMs = 1500, int AttackRange = 8, int DamageMultiplier = 100, int ExperienceMultiplier = 100, bool BossHealthBar = false)
    {
        public MonsterLifeData LifeData { get; init; } = new(null, null, null);
        public bool RangedWeapon { get; init; }
        /// <summary>
        /// The row's movement speed roll range in percent: the two unnamed i32 columns before the f32 that precedes
        /// AilmentThreshold (<c>__col75</c>, <c>__col76</c>; (0, 0) for most rows, (−5, 5) for the Goatmen, (−10, 10)
        /// for many others). The client draws each monster's roll in it (MonsterSpeedRoll.cs).
        /// </summary>
        public int SpeedRollMin { get; init; }
        public int SpeedRollMax { get; init; }
        /// <summary>The row's ModelSizeMultiplier as a factor (100 → 1): the client scales the model, and a skill with
        /// <c>use_scaled_contact_offset</c> scales its release offset with it.</summary>
        public double ModelScale { get; init; } = 1;
        public IReadOnlyList<string> ComponentsOrBase => Components ?? MonsterObject.BaseComponents;
        public bool Supported => MonsterObject.CanSerialize(ComponentsOrBase, StateMachine is not null);
        public override string ToString() => $"{Name} ({Id}, row {Row})";
    }

    private readonly Lazy<(List<Variety> Rows, string? Error)> _rows = new(() => Load(folder));
    private readonly Lazy<LifeTables?> _life = new(() => LifeTables.Load(folder));
    private readonly MonsterResistanceCatalog _resistance = new(folder);
    private readonly Lazy<Dictionary<string, HitAnimation[]>> _takeHit = new(() => LoadTakeHit(folder));

    public string? Error => _rows.Value.Error;

    /// <summary>The variety's elemental/chaos base resistance at this level, before spawned monster modifiers.</summary>
    public int? BaseResistance(Variety variety, int level, GameServer.Core.World.DamageType type) =>
        _resistance.BaseResistance(variety.MonsterType, level, type);

    /// <summary>
    /// The variety's TakeHit clips: their indexes in its rig's animation list and their lengths
    /// (data/monster-takehit.json, written by <c>python -I scripts/re/monster_takehit.py</c>). S→C 0x1b2 kind 0 plays
    /// one of them on the Animated component and the clip's own SoundEvents play with it, when a hit passes
    /// <see cref="HitReaction"/>'s gates. Most varieties map one clip; a few map a pool (the .act's
    /// "flinch_small:max_duration=…, flinch_large:min_duration=…" or "flinch_01, flinch_02, flinch_03"), from which the
    /// server picks one per flinch. Empty without the file or the
    /// row (the monster then just does not flinch). A clip without a length (a file from before "lengths") has 0 s, so
    /// the 0.4 s minimum applies.
    /// </summary>
    public IReadOnlyList<HitAnimation> TakeHit(string id) => _takeHit.Value.GetValueOrDefault(id) ?? [];

    static Dictionary<string, HitAnimation[]> LoadTakeHit(string folder)
    {
        string path = Path.Combine(folder, "monster-takehit.json");
        if (!File.Exists(path)) return [];
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var result = new Dictionary<string, HitAnimation[]>();
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            // "lengths" (ms) runs parallel to "indexes".
            JsonElement[] lengths = p.Value.TryGetProperty("lengths", out var l) && l.ValueKind == JsonValueKind.Array
                ? [.. l.EnumerateArray()] : [];
            double Seconds(int i) =>
                i < lengths.Length && lengths[i].ValueKind == JsonValueKind.Number ? lengths[i].GetDouble() / 1000 : 0;
            if (p.Value.TryGetProperty("indexes", out var list) && list.ValueKind == JsonValueKind.Array)
                result[p.Name] = [.. list.EnumerateArray().Select((e, i) => (e, i))
                    .Where(x => x.e.ValueKind == JsonValueKind.Number)
                    .Select(x => new HitAnimation(x.e.GetInt32(), Seconds(x.i)))];
            else if (p.Value.TryGetProperty("index", out var one) && one.ValueKind == JsonValueKind.Number)
                result[p.Name] = [new HitAnimation(one.GetInt32(), Seconds(0))];
        }
        return result;
    }

    /// <summary>
    /// The maximum life the client gives a monster of this variety at this level, before mods: the client's Monster type
    /// (0x141e701d0) sets stat 0x7d to max(1, column × LifeMultiplier / 100) from DefaultMonsterStats row level − 1 (0x48-byte
    /// rows). The column depends on the variety's MonsterTypes row: AltLife1 → AltLife1, else AltLife2 → AltLife2, else
    /// IsPlayerMinion → MinionLife, else MonsterLife. Null when the tables are missing.
    /// </summary>
    public int? BaseLife(Variety v, byte level)
    {
        if (_life.Value is not { } t || level < 1 || level > t.Levels.Count) return null;
        var row = t.Levels[level - 1];
        var type = v.MonsterType >= 0 && v.MonsterType < t.Types.Count ? t.Types[v.MonsterType] : default;
        int column = type.AltLife1 ? row.AltLife1 : type.AltLife2 ? row.AltLife2 : type.Minion ? row.MinionLife : row.MonsterLife;
        return Math.Max(1, column * v.LifeMultiplier / 100);
    }

    /// <summary>
    /// monster_life_+%_final_from_rarity of the rarity's own monster mod (Mods.dat MonsterMagic2 148, MonsterRare2 390,
    /// MonsterUnique2 698): the life a magic, rare or unique monster has more than a normal one. Hillock at level 1:
    /// 39 → 311.
    /// </summary>
    public static int RarityLifePct(MonsterRarity rarity) => rarity switch
    {
        MonsterRarity.Magic => 148,
        MonsterRarity.Rare => 390,
        MonsterRarity.Unique => 698,
        _ => 0,
    };

    /// <summary>The maximum life with the rarity's monster_life_+%_final_from_rarity (<see cref="RarityLifePct"/>).</summary>
    public static int LifeWithRarity(int baseLife, MonsterRarity rarity) => (int)((long)baseLife * (100 + RarityLifePct(rarity)) / 100);

    /// <summary>
    /// A monster's hit at this level, before mods: DefaultMonsterStats row level − 1 `Damage` × DamageMultiplier / 100
    /// × the attack time in seconds (unless its MonsterTypes row has BaseDamageIgnoresAttackSpeed, as Path of Building
    /// does), spread ± MonsterTypes `DamageSpread` percent (20 without one). Hillock at level 1: 4.99 × 1.01 × 1.65 = 8.3,
    /// spread 30%. Null when the tables are missing.
    /// </summary>
    public (int Min, int Max)? BaseDamage(Variety v, byte level)
    {
        if (_life.Value is not { } t || level < 1 || level > t.Levels.Count) return null;
        var type = v.MonsterType >= 0 && v.MonsterType < t.Types.Count ? t.Types[v.MonsterType] : default;
        double d = t.Levels[level - 1].Damage * v.DamageMultiplier / 100.0 * (type.IgnoresAttackSpeed ? 1 : v.AttackMs / 1000.0);
        double spread = (type.DamageSpread > 0 ? type.DamageSpread : 20) / 100.0;
        return (Math.Max(1, (int)Math.Round(d * (1 - spread))), Math.Max(1, (int)Math.Round(d * (1 + spread))));
    }

    /// <summary>The rarity's damage and attack speed (its monster mods MonsterMagic5/Rare5/Unique5
    /// monster_rarity_damage_+%_final 30/50/70, and MonsterMagic9/Rare9/Unique8
    /// monster_rarity_attack_cast_speed_+%_and_damage_-%_final 20/33/33: that much faster, that much less damage per
    /// hit). Hillock: × 1.7 × 0.67 per hit, 1.65 s → 1.24 s.</summary>
    public static (double DamageFactor, double SpeedFactor) RarityAttack(MonsterRarity rarity)
    {
        var (more, faster) = rarity switch
        {
            MonsterRarity.Magic => (30, 20),
            MonsterRarity.Rare => (50, 33),
            MonsterRarity.Unique => (70, 33),
            _ => (0, 0),
        };
        return ((1 + more / 100.0) * (1 - faster / 100.0), 1 + faster / 100.0);
    }

    /// <summary>The experience a monster of this level gives before its variety's ExperienceMultiplier, rarity and the
    /// level penalty (Progression.MonsterExperience): DefaultMonsterStats row level − 1 `Experience`. Null when the
    /// tables are missing.</summary>
    public int? BaseExperience(Variety v, byte level) =>
        _life.Value is { } t && level >= 1 && level <= t.Levels.Count ? t.Levels[level - 1].Experience : null;

    /// <summary>A normal monster's damage at this level, before a variety's multiplier: DefaultMonsterStats `Damage`.
    /// Used by the defence probe; the actual hit spread is in <see cref="BaseDamage(Variety, byte)"/>.</summary>
    public int? BaseDamage(int level) =>
        _life.Value is { } t && level >= 1 && level <= t.Levels.Count ? (int)Math.Round(t.Levels[level - 1].Damage) : null;

    /// <summary>A monster's accuracy at this level: DefaultMonsterStats `Accuracy`.</summary>
    public int? BaseAccuracy(int level) =>
        _life.Value is { } t && level >= 1 && level <= t.Levels.Count ? t.Levels[level - 1].Accuracy : null;

    /// <summary>A monster's evasion rating at this level: DefaultMonsterStats `Evasion` (the same table PoB uses for
    /// <c>monsterEvasionTable</c>). Null when the tables are missing.</summary>
    public int? BaseEvasion(int level) =>
        _life.Value is { } t && level >= 1 && level <= t.Levels.Count ? t.Levels[level - 1].Evasion : null;

    /// <summary>The evasion of an evasive (high-dexterity) monster at this level: DefaultMonsterStats `EvasiveEvasion`.
    /// The character panel's "Chance to Hit Evasive Monsters" row uses it.</summary>
    public int? BaseEvasiveEvasion(int level) =>
        _life.Value is { } t && level >= 1 && level <= t.Levels.Count ? t.Levels[level - 1].EvasiveEvasion : null;

    private sealed record LifeTables(List<(int MonsterLife, int MinionLife, int AltLife1, int AltLife2, double Damage,
        int Experience, int Accuracy, int Evasion, int EvasiveEvasion)> Levels,
        List<(bool Minion, bool AltLife1, bool AltLife2, int DamageSpread, bool IgnoresAttackSpeed)> Types)
    {
        public static LifeTables? Load(string folder)
        {
            string stats = Path.Combine(folder, "tables", "DefaultMonsterStats.json"), types = Path.Combine(folder, "tables", "MonsterTypes.json");
            if (!File.Exists(stats) || !File.Exists(types)) return null;
            using var s = JsonDocument.Parse(File.ReadAllBytes(stats));
            using var t = JsonDocument.Parse(File.ReadAllBytes(types));
            return new LifeTables(
                [.. s.RootElement.EnumerateArray().Select(r => (r.GetProperty("MonsterLife").GetInt32(), r.GetProperty("MinionLife").GetInt32(),
                    r.GetProperty("AltLife1").GetInt32(), r.GetProperty("AltLife2").GetInt32(), r.GetProperty("Damage").GetDouble(),
                    r.GetProperty("Experience").GetInt32(), r.GetProperty("Accuracy").GetInt32(), r.GetProperty("Evasion").GetInt32(),
                    r.GetProperty("EvasiveEvasion").GetInt32()))],
                [.. t.RootElement.EnumerateArray().Select(r => (r.GetProperty("IsPlayerMinion").GetBoolean(), r.GetProperty("AltLife1").GetBoolean(),
                    r.GetProperty("AltLife2").GetBoolean(),
                    r.TryGetProperty("DamageSpread", out var spread) && spread.ValueKind == JsonValueKind.Number ? spread.GetInt32() : 0,
                    r.TryGetProperty("BaseDamageIgnoresAttackSpeed", out var ignores) && ignores.ValueKind == JsonValueKind.True))]);
        }
    }

    /// <summary>A MonsterVarieties row by number, or by Id: exact, then the last path segment, then any part of it,
    /// then the display name. Several matches: the first one the blob writer supports.</summary>
    public Variety? Find(string query)
    {
        var rows = _rows.Value.Rows;
        if (int.TryParse(query, out int row)) return row >= 0 && row < rows.Count ? rows[row] : null;
        foreach (Func<Variety, bool> match in new Func<Variety, bool>[]
        {
            v => v.Id.Equals(query, StringComparison.OrdinalIgnoreCase),
            v => v.Id.EndsWith("/" + query, StringComparison.OrdinalIgnoreCase),
            v => v.Name.Equals(query, StringComparison.OrdinalIgnoreCase),
            v => v.Id.Contains(query, StringComparison.OrdinalIgnoreCase),
        })
        {
            var hits = rows.Where(match).ToList();
            if (hits.Count > 0) return hits.FirstOrDefault(v => v.Supported) ?? hits[0];
        }
        return null;
    }

    private static (List<Variety>, string?) Load(string folder)
    {
        string varieties = Path.Combine(folder, "tables", "MonsterVarieties.json");
        if (!File.Exists(varieties)) return ([], $"{varieties} is missing: extract the game tables (Tools/PoEDataExtractor)");
        var components = new Dictionary<string, IReadOnlyList<string>>();
        var stateMachines = new Dictionary<string, StateMachineData>();
        var transitionStates = new Dictionary<string, byte>();
        var lifeData = new Dictionary<string, MonsterLifeData>();
        string types = Path.Combine(folder, "object-types.json");
        if (File.Exists(types))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(types));
            foreach (var t in doc.RootElement.GetProperty("types").EnumerateArray())
            {
                int read = t.GetProperty("read").GetInt32();
                string type = t.GetProperty("type").GetString()!;
                components[type] = [.. t.GetProperty("components").EnumerateArray().Take(read).Select(c => c.GetString()!)];
                // Files generated before the StateMachine data have no "shared_states": those types stay unsupported.
                if (t.TryGetProperty("shared_states", out var ss) && t.TryGetProperty("shared_timers", out var st))
                    stateMachines[type] = new([.. ss.EnumerateArray().Select(e => e[1].GetInt32())], st.GetArrayLength(),
                        [.. ss.EnumerateArray().Select(e => e[0].GetString() ?? "")]);
                if (t.TryGetProperty("transitionable", out var tr) && tr.TryGetProperty("num_states", out var ns))
                    transitionStates[type] = (byte)Math.Clamp(ns.GetInt32(), 1, 127);
                if (t.TryGetProperty("life", out var life) && life.ValueKind == JsonValueKind.Object)
                {
                    bool? Flag(string name) => life.TryGetProperty(name, out var value)
                        && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
                    lifeData[type] = new(Flag("corpse_usable"), Flag("prevent_corpse_sinking"), Flag("corpse_use_veto"));
                }
            }
        }
        var rangedVisuals = RangedWeaponVisuals(folder);
        bool RangedWeapon(JsonElement row) => new[] { "Weapon1_ItemVisualIdentityKeys", "Weapon2_ItemVisualIdentityKeys" }
            .Any(name => row.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array
                && values.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.Number && rangedVisuals.Contains(value.GetInt32())));
        static int Column(JsonElement row, string name) =>
            row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
        var rows = new List<Variety>();
        using (var doc = GameTables.Open(varieties))
        {
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                string id = r.GetProperty("Id").GetString() ?? "";
                string baseType = r.GetProperty("BaseMonsterTypeIndex").GetString() ?? "";
                string? ao = r.GetProperty("AOFiles").EnumerateArray().Select(a => a.GetString()).FirstOrDefault();
                rows.Add(new Variety(rows.Count, id, r.GetProperty("Name").GetString() ?? "", baseType, ao,
                    components.Count == 0 ? null : components.GetValueOrDefault(baseType),
                    r.GetProperty("LifeMultiplier").GetInt32(),
                    r.GetProperty("MonsterTypesKey").ValueKind == JsonValueKind.Number ? r.GetProperty("MonsterTypesKey").GetInt32() : -1,
                    stateMachines.GetValueOrDefault(baseType), transitionStates.GetValueOrDefault(baseType, (byte)1),
                    r.GetProperty("MovementSpeed").GetInt32(), r.GetProperty("ObjectSize").GetInt32(), r.GetProperty("MaxAgroRange").GetInt32(),
                    [.. r.GetProperty("GrantedEffectsKeys").EnumerateArray().Select(e => e.GetInt32())],
                    r.GetProperty("AttackSpeed").GetInt32(), r.GetProperty("MaximumAttackDistance").GetInt32(),
                    r.GetProperty("DamageMultiplier").GetInt32(), r.GetProperty("ExperienceMultiplier").GetInt32(),
                    r.TryGetProperty("BossHealthBar", out var bar) && bar.ValueKind == JsonValueKind.True)
                    {
                        LifeData = lifeData.GetValueOrDefault(baseType) ?? new(null, null, null), RangedWeapon = RangedWeapon(r),
                        SpeedRollMin = Column(r, "__col75"), SpeedRollMax = Column(r, "__col76"),
                        ModelScale = Column(r, "ModelSizeMultiplier") is int scale and > 0 ? scale / 100.0 : 1,
                    });
            }
        }
        // With object-types.json, a type missing from it has no .ot: do not guess.
        if (components.Count > 0)
            rows = [.. rows.Select(v => v.Components is null ? v with { Components = [] } : v)];
        return (rows, null);
    }

    static HashSet<int> RangedWeaponVisuals(string folder)
    {
        string classes = Path.Combine(folder, "tables", "ItemClasses.json");
        string bases = Path.Combine(folder, "tables", "BaseItemTypes.json");
        if (!File.Exists(classes) || !File.Exists(bases)) return [];
        using var c = JsonDocument.Parse(File.ReadAllBytes(classes));
        using var b = JsonDocument.Parse(File.ReadAllBytes(bases));
        var rangedClasses = c.RootElement.EnumerateArray().Select((row, index) => (row, index))
            .Where(p => p.row.GetProperty("Id").GetString() is "Bow" or "Wand").Select(p => p.index).ToHashSet();
        return b.RootElement.EnumerateArray()
            .Where(row => row.TryGetProperty("ItemClassesKey", out var k) && k.ValueKind == JsonValueKind.Number
                && rangedClasses.Contains(k.GetInt32()) && row.TryGetProperty("ItemVisualIdentity", out var v)
                && v.ValueKind == JsonValueKind.Number)
            .Select(row => row.GetProperty("ItemVisualIdentity").GetInt32()).ToHashSet();
    }
}
