using System.Text.Json;
using GameServer.Core.Protocol.Instance;

/// <summary>
/// Item types: BaseItemTypes rows from the extracted tables (Id, Name, HASH32, size, visual identity → ItemVisualIdentity
/// HASH16), each type's components from data/item-types.json (`python -I scripts/re/ot_components.py --items --json
/// data/item-types.json`), flask charges from ComponentCharges and stack sizes from CurrencyItems. All game data: it stays in data/ and loads on first use.
/// </summary>
sealed class ItemCatalog
{
    /// <summary>What the stash tabs hold (<see cref="StashRules"/>), loaded on first use; tests set their own.</summary>
    public StashRules Stash
    {
        get => _stash ??= StashRules.Load(_folder, _rows.Value.Rows);
        internal set => _stash = value;
    }

    private StashRules? _stash;
    private readonly string _folder;

    public ItemCatalog(string folder, bool hardMode = false, LimitSettings? limits = null)
    {
        _folder = folder;
        Limits = limits ?? LimitSettings.Default;
        Mods = new ModCatalog(folder, hardMode);
        _rows = new(() => Load(folder, Mods));
        _byHash = new(() => _rows.Value.Rows.GroupBy(t => t.Hash).ToDictionary(g => g.Key, g => g.First()));
        _gems = new(() => LoadGems(folder, hardMode));
        _requirements = new(() => LoadRequirements(folder));
        _shieldBlocks = new(() => LoadShieldBlocks(folder));
        _weaponCrits = new(() => LoadWeaponCrits(folder));
        _defences = new(() => LoadDefences(folder));
        _flasks = new(() => LoadFlasks(folder, _rows.Value.Rows, hardMode));
        _utilityFlasks = new(() => UtilityFlasks.Load(folder, _rows.Value.Rows, hardMode));
        _visualsById = new(() => LoadVisualsById(folder));
    }

    private readonly Lazy<Dictionary<string, ushort>> _visualsById;

    /// <summary>An ItemVisualIdentity row's HASH16 by its Id (DefaultArrow, DefaultQuiver …): the hashes the client
    /// looks an art up by. Null when the table is missing or has no such row.</summary>
    public ushort? Visual(string id) => _visualsById.Value.TryGetValue(id, out ushort hash) && hash != 0 ? hash : null;

    static Dictionary<string, ushort> LoadVisualsById(string folder)
    {
        var map = new Dictionary<string, ushort>(StringComparer.Ordinal);
        string path = Path.Combine(folder, "tables", "ItemVisualIdentity.json");
        if (!File.Exists(path)) return map;
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var r in doc.RootElement.EnumerateArray())
            if (r.GetProperty("Id").GetString() is { Length: > 0 } id
                && r.GetProperty("HASH16") is { ValueKind: JsonValueKind.Number } h)
                map.TryAdd(id, (ushort)h.GetInt32());
        return map;
    }

    public sealed record BaseType(int Row, string Id, string Name, uint Hash, byte Width, byte Height, ushort VisualIdentity,
        IReadOnlyList<string> Components, int MaxCharges, string Class = "", byte Stance = 0xff, int DropLevel = 0, ushort MaxStack = 1,
        int FullStackRow = -1, IReadOnlyList<ModCatalog.Mod>? Implicits = null)
    {
        /// <summary>Tags.dat Ids for mod spawn weights: the .it chain's Base tags (item-types.json) and the row's TagsKeys.</summary>
        public IReadOnlySet<string> Tags { get; init; } = new HashSet<string>();

        /// <summary>BaseItemTypes ModDomain: the Mods.dat Domain its affixes come from (1 items, 2 flasks, …).</summary>
        public int ModDomain { get; init; } = 1;

        /// <summary>Its ItemClasses row (-1 unknown): the worn item's weapon type the client checks skills against is this
        /// row + 1 (11-skills.md "Weapon restrictions").</summary>
        public int ClassRow { get; init; } = -1;

        /// <summary>BaseItemTypes or ItemClasses forbids modification of this base.</summary>
        public bool Unmodifiable { get; init; }

        /// <summary>ItemClasses CanBeFractured: true for equipment, flasks, jewels, maps, contracts and logbooks;
        /// false for currency, gems, fragments, tinctures and relics. The Fracturing Orb's class check.</summary>
        public bool CanBeFractured { get; init; }

        /// <summary>ItemClasses <c>__col21</c> (unnamed in the dat schema): true for the equipment classes (weapons,
        /// armour, jewellery and quivers) and false for flasks, jewels, maps and currency. The Mirror of Kalandra's
        /// non-unique equipment check; the wiki's equipment definition is the same set.</summary>
        public bool CanBeMirrored { get; init; }

        public bool Supported => Components.Count > 0 && Item.CanSerialize(Components);
        /// <summary>Several of the type share one cell (a Stack component and a CurrencyItems StackSize above 1).</summary>
        public bool Stackable => MaxStack > 1;
        /// <summary>A full stack becomes one of this BaseItemTypes row (CurrencyItems FullStack_BaseItemTypesKey: 5 Scroll
        /// Fragments → a Scroll of Wisdom, 20 shards → their orb), or -1.</summary>
        public bool BecomesWhenFull => FullStackRow >= 0;
        public override string ToString() => $"{Name} ({Id}, row {Row})";
    }

    private readonly Lazy<(List<BaseType> Rows, string? Error)> _rows;

    /// <summary>Mods.dat, for the implicits of new items and the mods of saved ones.</summary>
    public ModCatalog Mods { get; }

    /// <summary>Effective server-side item limits (the database override, else the catalogue default).</summary>
    public LimitSettings Limits { get; }

    public string? Error => _rows.Value.Error;

    /// <summary>The type of an item (its BaseItemTypes HASH32).</summary>
    public BaseType? ByHash(uint hash) => _byHash.Value.GetValueOrDefault(hash);

    private readonly Lazy<Dictionary<uint, BaseType>> _byHash;

    public ItemCatalog() : this("data") { }

    public BaseType? Row(int row) => row >= 0 && row < _rows.Value.Rows.Count ? _rows.Value.Rows[row] : null;

    /// <summary>A BaseItemTypes row by number, or by Id: exact, then the last path segment, then the display name, then
    /// any part of the Id. Several matches: the first one the serializer supports.</summary>
    /// <summary>Every BaseItemTypes row (the launcher's suggestions).</summary>
    public IReadOnlyList<BaseType> All => _rows.Value.Rows;

    /// <summary>The names <see cref="FindGem"/> takes: every creatable variant of every gem.</summary>
    public IEnumerable<string> GemNames() =>
        All.Select(Gem).OfType<GemType>().SelectMany(g => g.Variants.Where(v => v.Creatable).Select(v => v.Name)).Distinct();

    public BaseType? Find(string query)
    {
        var rows = _rows.Value.Rows;
        if (int.TryParse(query, out int row)) return Row(row) is { } numbered ? EmptyResonatorType(numbered) : null;
        foreach (Func<BaseType, bool> match in new Func<BaseType, bool>[]
        {
            t => t.Id.Equals(query, StringComparison.OrdinalIgnoreCase),
            t => t.Id.EndsWith("/" + query, StringComparison.OrdinalIgnoreCase),
            t => t.Name.Equals(query, StringComparison.OrdinalIgnoreCase),
            t => t.Id.Contains(query, StringComparison.OrdinalIgnoreCase),
        })
        {
            var hits = rows.Where(match).ToList();
            if (hits.Count > 0) return EmptyResonatorType(hits.FirstOrDefault(t => t.Supported) ?? hits[0]);
        }
        return null;
    }

    // Empty legacy resonators have no Stack component. Filled ones retain their original type.
    public BaseType EmptyResonatorType(BaseType type) =>
        ResonatorSockets(type) > 0 && !type.Components.Contains("Stack")
            ? _rows.Value.Rows.FirstOrDefault(t => t.Id == type.Id.Replace("/DelveSocketable", "/DelveStackableSocketable")) ?? type
            : type;

    public static int ResonatorSockets(BaseType type) =>
        type.Id.StartsWith("Metadata/Items/Delve/Delve", StringComparison.Ordinal)
        && type.Id.Contains("SocketableCurrency", StringComparison.Ordinal)
        && type.Id[^1] is >= '1' and <= '4' ? type.Id[^1] - '0' : 0;

    public static IReadOnlyList<ItemSocket> ResonatorSocketList(BaseType type) =>
        Enumerable.Range(0, ResonatorSockets(type)).Select(_ => new ItemSocket(SocketColour.Delve)).ToArray();

    /// <summary>A gem's variant (SkillGems GemVariants → GemEffects; the SkillGem component's u32 +0x3c is its index): its
    /// name, the GrantedEffects rows it grants (a Vaal gem's Vaal skill, then its base skill; -1 for none), and whether
    /// it may be created.</summary>
    public sealed record GemVariant(int Index, string Name, int Effect, int Effect2, bool Creatable);

    /// <summary>A gem type (a SkillGems row): its variants (the first is the normal gem) and its experience thresholds,
    /// ItemExperiencePerLevel's Experience for levels 1, 2, … of its experience type; the last level is the cap.</summary>
    public sealed record GemType(int Row, bool IsSupport, IReadOnlyList<GemVariant> Variants, IReadOnlyList<int> Thresholds)
    {
        /// <summary>SkillGems GemColour: 1 red (strength), 2 green (dexterity), 3 blue (intelligence), 4 white.</summary>
        public int Colour { get; init; }

        /// <summary>A Vaal gem's own row (IsVaalVariant) or an awakened one (RegularVariant set).</summary>
        public bool IsVaalOrAwakened { get; init; }

        /// <summary>SkillGems MtxSlotTypes: the MicrotransactionSkillGemEffectSlotTypes rows its Cosmetics tab skill slots
        /// take (0 the skill's effect, 1 a totem skin).</summary>
        public IReadOnlyList<int> MtxSlotTypes { get; init; } = [];

        /// <summary>SkillGems StrengthRequirementPercent: the strength share of the gem's attribute requirement
        /// (SkillCatalog.GemAttributeRequirement), 0 for a pure dexterity or intelligence gem.</summary>
        public int StrengthPercent { get; init; }

        /// <summary>SkillGems DexterityRequirementPercent (<see cref="StrengthPercent"/>).</summary>
        public int DexterityPercent { get; init; }

        /// <summary>SkillGems IntelligenceRequirementPercent (<see cref="StrengthPercent"/>).</summary>
        public int IntelligencePercent { get; init; }

        public int MaxLevel => Thresholds.Count;

        /// <summary>The level as the client derives it (0x14246f900): the levels whose threshold the experience reaches, up
        /// to the cap. Experience -1 is the cap + 1 (0x14246eb00; a guess: the corrupted gems over their cap).</summary>
        public int Level(int experience) => experience == -1 ? MaxLevel + 1 : Thresholds.Count(t => t <= experience);

        /// <summary>The experience a gem of this level starts with (the level clamped to 1 … the cap).</summary>
        public int ExperienceFor(int level) => Thresholds.Count == 0 ? 0 : Thresholds[Math.Clamp(level, 1, MaxLevel) - 1];

        /// <summary>The variant the client uses: an index outside the list is the normal gem (0x14246fa50 resets it).</summary>
        public GemVariant Variant(int index) => index > 0 && index < Variants.Count ? Variants[index] : Variants[0];
    }

    /// <summary>The gems a shop sells of one colour, active or support: creatable, writable, dropping (DropLevel above 0),
    /// not Vaal or awakened, one per name, by drop level then name.</summary>
    public IReadOnlyList<BaseType> ShopGems(int colour, bool support) =>
        [.. _rows.Value.Rows.Where(t => t.Supported && t.DropLevel > 0 && !t.Name.Contains("Playtest")
                && Gem(t) is { } g && g.Colour == colour && g.IsSupport == support && !g.IsVaalOrAwakened && g.Variants is [{ Creatable: true }, ..])
            .DistinctBy(t => t.Name).OrderBy(t => t.DropLevel).ThenBy(t => t.Name)];

    /// <summary>The local full gem shop: every writable, creatable variant, including transfigured and Vaal gems.
    /// Keep the catalog's creatability checks: placeholder and unsupported awakened rows are not safe to create.</summary>
    public IReadOnlyList<(BaseType Type, GemVariant Variant)> FullShopGems(int colour, bool support) =>
        [.. _rows.Value.Rows.Where(t => t.Supported && t.DropLevel > 0 && !t.Name.Contains("Playtest")
                && Gem(t) is { } g && g.Colour == colour && g.IsSupport == support)
            .SelectMany(t => Gem(t)!.Variants.Where(v => v.Creatable).Select(v => (Type: t, Variant: v)))
            .OrderBy(x => x.Type.DropLevel).ThenBy(x => x.Variant.Name)];

    /// <summary>A base type's gem (its SkillGems row), or null for items that are not gems.</summary>
    public GemType? Gem(BaseType t) => _gems.Value.GetValueOrDefault(t.Row);

    /// <summary>The GrantedEffects rows a gem item grants as skills: its variant's effect and, for a Vaal gem, the base
    /// skill. Support gems grant none: they modify the linked active skills (granting one as a skill crashed the client in
    /// the Actor skill reader, 0x141ee795a).</summary>
    public IEnumerable<int> GemEffects(Item gem)
    {
        if (ByHash(gem.TypeHash) is not { } t || Gem(t) is not { IsSupport: false } g || g.Variants.Count == 0) yield break;
        var v = g.Variant(gem.GemVariant);
        if (v.Effect >= 0) yield return v.Effect;
        if (v.Effect2 >= 0) yield return v.Effect2;
    }

    /// <summary>A gem item's level from its experience (<see cref="GemType.Level"/>), 1 for an unknown gem type.</summary>
    public int GemLevel(Item gem) => ByHash(gem.TypeHash) is { } t && Gem(t) is { } g ? g.Level(gem.GemExperience) : 1;

    /// <summary>A gem item's name: its variant's ("Ice Nova of Frostbolts"), else the type's.</summary>
    public string GemName(Item gem) => ByHash(gem.TypeHash) is not { } t ? "?"
        : Gem(t) is { Variants.Count: > 0 } g ? g.Variant(gem.GemVariant).Name : t.Name;

    /// <summary>A creatable gem by name, its type's or a variant's ("Ice Nova of Frostbolts", "Vaal Ice Nova of
    /// Frostbolts"), case-insensitive: an exact name first, then the first that contains it.</summary>
    public (BaseType Type, GemVariant Variant)? FindGem(string name)
    {
        var all = _rows.Value.Rows.Select(t => (Type: t, Gem: Gem(t))).Where(x => x.Gem is not null)
            .SelectMany(x => x.Gem!.Variants.Where(v => v.Creatable).Select(v => (x.Type, Variant: v))).ToList();
        foreach (var match in (Func<string, bool>[])[n => n.Equals(name, StringComparison.OrdinalIgnoreCase),
            n => n.Contains(name, StringComparison.OrdinalIgnoreCase)])
            foreach (var hit in all)
                if (match(hit.Variant.Name)) return hit;
        return null;
    }

    private Lazy<Dictionary<int, int>> _shieldBlocks = null!;

    /// <summary>The shield's base block percentage from ShieldTypes.dat (0 for non-shields).</summary>
    public int ShieldBlock(BaseType t) => _shieldBlocks.Value.GetValueOrDefault(t.Row);

    private static Dictionary<int, int> LoadShieldBlocks(string folder)
    {
        string path = Path.Combine(folder, "tables", "ShieldTypes.json");
        var result = new Dictionary<int, int>();
        if (!File.Exists(path)) return result;
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var r in d.RootElement.EnumerateArray())
            if (r.GetProperty("BaseItemTypesKey") is { ValueKind: JsonValueKind.Number } b &&
                r.GetProperty("Block") is { ValueKind: JsonValueKind.Number } block)
                result.TryAdd(b.GetInt32(), block.GetInt32());
        return result;
    }

    /// <summary>A base's defence range from ArmourTypes.dat (the values the client interpolates with the item's base
    /// percentile). Body armour, helmets, gloves, boots and shields have them; most other bases are zero.</summary>
    public readonly record struct BaseDefences(int ArmourMin, int ArmourMax, int EvasionMin, int EvasionMax,
        int EnergyShieldMin = 0, int EnergyShieldMax = 0, bool HasAdditionalVariableRange = false,
        int WardMin = 0, int WardMax = 0)
    {
        /// <summary>Sacred Orb eligibility includes Ward and the native table's fifth, currently unnamed range.
        /// Ward has its own pool calculation; the unnamed range only affects reroll eligibility.</summary>
        public bool HasVariableRange => ArmourMin != ArmourMax || EvasionMin != EvasionMax
            || EnergyShieldMin != EnergyShieldMax || WardMin != WardMax || HasAdditionalVariableRange;

        /// <summary>Native 3.29.3b base getters (0x1424af890/910/990): one shared roll over 31,
        /// rounded halfway up before local modifiers. Wire 32 is the special minimum-base state.</summary>
        public (int Armour, int Evasion, int EnergyShield) AtRoll(byte roll)
        {
            if (roll > Item.SpecialDefenceBaseRoll)
                throw new ArgumentOutOfRangeException(nameof(roll), roll, "Defence base roll must be 0..32.");
            int effective = roll == Item.SpecialDefenceBaseRoll ? 0 : roll;
            // Native table bounds are nonnegative integers; +15 implements nearest rounding over odd denominator 31.
            int At(int minimum, int maximum) => (int)(minimum + (((long)maximum - minimum) * effective + 15) / 31);
            return (At(ArmourMin, ArmourMax), At(EvasionMin, EvasionMax), At(EnergyShieldMin, EnergyShieldMax));
        }
    }

    private Lazy<Dictionary<int, BaseDefences>> _defences = null!;

    /// <summary>The base defence range of an item type, or null when it has no armour row.</summary>
    public BaseDefences? Defences(BaseType t) => _defences.Value.TryGetValue(t.Row, out var d) ? d : null;

    private static Dictionary<int, BaseDefences> LoadDefences(string folder)
    {
        string path = Path.Combine(folder, "tables", "ArmourTypes.json");
        var result = new Dictionary<int, BaseDefences>();
        if (!File.Exists(path)) return result;
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var r in d.RootElement.EnumerateArray())
        {
            if (r.GetProperty("BaseItemTypesKey") is not { ValueKind: JsonValueKind.Number } key) continue;
            bool Variable(string min, string max) => r.TryGetProperty(min, out var lo)
                && r.TryGetProperty(max, out var hi) && lo.GetInt32() != hi.GetInt32();
            result[(int)Math.Max(0, key.GetInt32())] = new BaseDefences(
                r.GetProperty("ArmourMin").GetInt32(), r.GetProperty("ArmourMax").GetInt32(),
                r.GetProperty("EvasionMin").GetInt32(), r.GetProperty("EvasionMax").GetInt32(),
                r.TryGetProperty("EnergyShieldMin", out var esMin) ? esMin.GetInt32() : 0,
                r.TryGetProperty("EnergyShieldMax", out var esMax) ? esMax.GetInt32() : 0,
                Variable("WardMin", "WardMax") || Variable("__col10", "__col11"),
                r.TryGetProperty("WardMin", out var wardMin) ? wardMin.GetInt32() : 0,
                r.TryGetProperty("WardMax", out var wardMax) ? wardMax.GetInt32() : 0);
        }
        return result;
    }

    private Lazy<Dictionary<int, int>> _weaponCrits = null!;

    /// <summary>The weapon's base critical strike chance (permyriad: 500 = 5%) from WeaponTypes.dat.</summary>
    public int WeaponCritical(BaseType t) => _weaponCrits.Value.GetValueOrDefault(t.Row);

    private static Dictionary<int, int> LoadWeaponCrits(string folder)
    {
        string path = Path.Combine(folder, "tables", "WeaponTypes.json");
        var result = new Dictionary<int, int>();
        if (!File.Exists(path)) return result;
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var r in d.RootElement.EnumerateArray())
            if (r.GetProperty("BaseItemTypesKey") is { ValueKind: JsonValueKind.Number } b &&
                r.GetProperty("Critical") is { ValueKind: JsonValueKind.Number } crit)
                result.TryAdd(b.GetInt32(), crit.GetInt32());
        return result;
    }

    private Lazy<Dictionary<int, Flasks.Flask>> _flasks = null!;

    /// <summary>A life or mana flask's use (Flasks.dat and ComponentCharges), or null for other types, utility flasks
    /// included.</summary>
    public Flasks.Flask? FlaskOf(BaseType t) => _flasks.Value.GetValueOrDefault(t.Row);

    private readonly Lazy<Dictionary<int, UtilityFlasks.Utility>> _utilityFlasks;

    /// <summary>A utility flask's use (<see cref="UtilityFlasks"/>), or null for other types.</summary>
    public UtilityFlasks.Utility? UtilityFlaskOf(BaseType t) => _utilityFlasks.Value.GetValueOrDefault(t.Row);

    /// <summary>Any flask's charges (life, mana or utility, usable or not), for refills; null for other types.</summary>
    public Flasks.Flask? FlaskChargesOf(BaseType t) => FlaskOf(t) ?? UtilityFlaskOf(t)?.Charges;

    private static Dictionary<int, Flasks.Flask> LoadFlasks(string folder, IReadOnlyList<BaseType> rows, bool hardMode)
    {
        string flasks = Path.Combine(folder, "tables", "Flasks.json"), charges = Path.Combine(folder, "tables", "ComponentCharges.json");
        var result = new Dictionary<int, Flasks.Flask>();
        if (!File.Exists(flasks) || !File.Exists(charges)) return result;
        var perUse = new Dictionary<string, (int Max, int PerCharge)>();
        using (var c = JsonDocument.Parse(File.ReadAllBytes(charges)))
            foreach (var r in c.RootElement.EnumerateArray())
                perUse.TryAdd(r.GetProperty("BaseItemTypesKey").GetString() ?? "", (r.GetProperty("MaxCharges").GetInt32(), r.GetProperty("PerCharge").GetInt32()));
        using var f = JsonDocument.Parse(File.ReadAllBytes(flasks));
        foreach (var r in f.RootElement.EnumerateArray())
            if (r.GetProperty("BaseItemTypesKey") is { ValueKind: JsonValueKind.Number } b && b.GetInt32() < rows.Count &&
                perUse.TryGetValue(rows[b.GetInt32()].Id, out var use) &&
                (r.GetProperty("LifePerUse").GetInt32(), r.GetProperty("ManaPerUse").GetInt32()) is var (life, mana) && life + mana > 0)
                // RecoveryTime is in tenths of a second (Small Life Flask: 30, 3 s).
                result.TryAdd(b.GetInt32(), new(life, mana,
                    r.GetProperty(hardMode && r.TryGetProperty("RecoveryTime2", out var alternate)
                        && alternate.ValueKind == JsonValueKind.Number ? "RecoveryTime2" : "RecoveryTime").GetInt32() / 10.0,
                    use.PerCharge, use.Max));
        return result;
    }

    private Lazy<Dictionary<int, GemType>> _gems = null!;

    // Not creatable: the "[DNT]" placeholder variants, the "[UNUSED] …", "[DNT] …" and "WIP …" gem rows, the awakened
    // supports other than Empower, Enhance and Enlighten (disabled since 3.28; theirs is GemProgressionExceptionalPlus),
    // and a Vaal variant whose base skill is only a placeholder's. A Vaal variant is named "Vaal " + its base variant.
    private static Dictionary<int, GemType> LoadGems(string folder, bool hardMode)
    {
        string Table(string n) => Path.Combine(folder, "tables", n + ".json");
        var result = new Dictionary<int, GemType>();
        string[] needed = ["SkillGems", "GemEffects", "BaseItemTypes", "ItemExperiencePerLevel", "ItemExperienceTypes"];
        if (needed.Any(n => !File.Exists(Table(n)))) return result;
        using var g = JsonDocument.Parse(File.ReadAllBytes(Table("SkillGems")));
        using var e = JsonDocument.Parse(File.ReadAllBytes(Table("GemEffects")));
        using var b = JsonDocument.Parse(File.ReadAllBytes(Table("BaseItemTypes")));
        using var x = JsonDocument.Parse(File.ReadAllBytes(Table("ItemExperiencePerLevel")));
        using var xt = JsonDocument.Parse(File.ReadAllBytes(Table("ItemExperienceTypes")));
        static int Row(JsonElement r, string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : -1;
        // Null or absent means no alternate; row zero remains a real effect, and must not be treated as missing.
        int Effect(JsonElement r, string name) => hardMode && Row(r, name + "Hardmode") is int alternate and >= 0
            ? alternate : Row(r, name);
        var thresholds = x.RootElement.EnumerateArray().GroupBy(r => Row(r, "ItemExperienceType")).ToDictionary(t => t.Key,
            t => (IReadOnlyList<int>)[.. t.OrderBy(r => Row(r, "ItemCurrentLevel")).Select(r => Row(r, "Experience"))]);
        var effects = e.RootElement;
        string Name(int row) => effects[row].GetProperty("Name").GetString() ?? "";
        static IEnumerable<(int Row, int Index)> Variants(JsonElement r) =>
            r.GetProperty("GemVariants").EnumerateArray().Select((v, i) => (v.GetInt32(), i));
        // A regular skill's GrantedEffects row → its variant's name, for the Vaal variants that grant it as their base.
        var regular = new Dictionary<int, string>();
        var rows = new List<(int BaseRow, JsonElement Row, string BaseName)>();
        foreach (var r in g.RootElement.EnumerateArray())
        {
            int baseRow = Row(r, "BaseItemTypesKey");
            if (baseRow < 0) continue;
            string baseName = b.RootElement[baseRow].GetProperty("Name").GetString() ?? "";
            rows.Add((baseRow, r, baseName));
            if (r.GetProperty("IsVaalVariant").GetBoolean() || baseName.StartsWith('[')) continue;
            foreach (var (v, i) in Variants(r))
                if (Effect(effects[v], "GrantedEffect") is int ge and >= 0 && !Name(v).StartsWith("[DNT]"))
                    regular.TryAdd(ge, i == 0 ? baseName : Name(v));
        }
        foreach (var (baseRow, r, baseName) in rows)
        {
            bool vaal = r.GetProperty("IsVaalVariant").GetBoolean();
            int expType = Row(r, "ItemExperienceType");
            bool awakened = Row(r, "RegularVariant") >= 0
                && (expType < 0 || xt.RootElement[expType].GetProperty("Id").GetString() != "GemProgressionExceptionalPlus");
            bool rowCreatable = !baseName.StartsWith('[') && !baseName.StartsWith("WIP") && !awakened;
            var variants = new List<GemVariant>();
            foreach (var (v, i) in Variants(r))
            {
                int effect = Effect(effects[v], "GrantedEffect"), effect2 = Effect(effects[v], "GrantedEffect2");
                string name = i == 0 ? baseName : Name(v);
                bool creatable = rowCreatable && !name.StartsWith("[DNT]");
                if (vaal && effect2 >= 0)
                {
                    creatable &= regular.TryGetValue(effect2, out var baseSkill);
                    if (i > 0 && baseSkill is not null) name = "Vaal " + baseSkill;
                }
                variants.Add(new GemVariant(i, name, effect, effect2, creatable));
            }
            result.TryAdd(baseRow, new GemType(baseRow, r.GetProperty("IsSupport").GetBoolean(), variants,
                thresholds.GetValueOrDefault(expType) ?? [])
            {
                Colour = Row(r, "GemColour"), IsVaalOrAwakened = vaal || Row(r, "RegularVariant") >= 0,
                MtxSlotTypes = r.TryGetProperty("MtxSlotTypes", out var st) ? [.. st.EnumerateArray().Select(t => t.GetInt32())] : [],
                StrengthPercent = Row(r, "StrengthRequirementPercent"), DexterityPercent = Row(r, "DexterityRequirementPercent"),
                IntelligencePercent = Row(r, "IntelligenceRequirementPercent"),
            });
        }
        return result;
    }

    /// <summary>Two linked sockets for a type with a Sockets component: 3.29 white by default, each with the item-level
    /// chance to roll a colour proportional to its attribute requirements (ComponentAttributeRequirements:
    /// strength red, dexterity green, intelligence blue; SocketColours); no sockets for other types.</summary>
    public (IReadOnlyList<ItemSocket> Sockets, IReadOnlyList<byte>? Links) DefaultSockets(
        BaseType t, int itemLevel = 1, int quality = 0, Random? random = null)
    {
        if (!t.Components.Contains("Sockets")) return ([], null);
        if (ResonatorSockets(t) > 0) return (ResonatorSocketList(t), null);
        // 3.29: sockets are white by default; each has a chance to roll its attribute colour that rises with item level
        // (SocketColours). Both sockets of a new item still start linked.
        var requirements = Requirements(t);
        random ??= Random.Shared;
        return ([new(SocketColours.Roll(requirements, itemLevel, quality, random)),
                 new(SocketColours.Roll(requirements, itemLevel, quality, random))], [2]);
    }

    private Lazy<Dictionary<string, (int Str, int Dex, int Int)>> _requirements = null!;

    /// <summary>The base's attribute requirements (ComponentAttributeRequirements), zero when it has none.</summary>
    public (int Str, int Dex, int Int) Requirements(BaseType t) => _requirements.Value.GetValueOrDefault(t.Id);

    private static Dictionary<string, (int, int, int)> LoadRequirements(string folder)
    {
        string path = Path.Combine(folder, "tables", "ComponentAttributeRequirements.json");
        var result = new Dictionary<string, (int, int, int)>();
        if (!File.Exists(path)) return result;
        using var d = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var r in d.RootElement.EnumerateArray())
            result[r.GetProperty("BaseItemTypesKey").GetString() ?? ""] =
                (r.GetProperty("ReqStr").GetInt32(), r.GetProperty("ReqDex").GetInt32(), r.GetProperty("ReqInt").GetInt32());
        return result;
    }

    /// <summary>A new item: item level 1, normal, flasks full, implicits and an ordinary armour base roll.
    /// An explicit defence roll is used by save restoration and deterministic fixtures, without drawing a new roll.</summary>
    public static Item Create(BaseType t, IReadOnlyList<ItemSocket>? sockets = null, IReadOnlyList<byte>? links = null,
        Random? random = null, byte? defenceBaseRoll = null)
    {
        random ??= Random.Shared;
        return new(t.Hash, t.Components, t.VisualIdentity)
        {
            DefenceBaseRoll = defenceBaseRoll ?? (t.Components.Contains("Armour") ? (byte)random.Next(Item.MaximumDefenceBaseRoll + 1) : (byte)0),
            Charges = t.MaxCharges, Sockets = ResonatorSockets(t) > 0 ? ResonatorSocketList(t) : sockets ?? [],
            Links = ResonatorSockets(t) > 0 ? null : links,
            Mods = t.Components.Contains("Mods") ? [.. (t.Implicits ?? []).Select(m => m.Roll(ModSection.Implicit, random))] : [],
        };
    }

    private static (List<BaseType>, string?) Load(string folder, ModCatalog mods)
    {
        string bases = Path.Combine(folder, "tables", "BaseItemTypes.json"), visuals = Path.Combine(folder, "tables", "ItemVisualIdentity.json");
        if (!File.Exists(bases) || !File.Exists(visuals)) return ([], $"{bases} is missing: extract the game tables (Tools/PoEDataExtractor)");
        var components = new Dictionary<string, IReadOnlyList<string>>();
        var itemTags = new Dictionary<string, IReadOnlyList<string>>();
        string types = Path.Combine(folder, "item-types.json");
        if (!File.Exists(types)) return ([], $"{types} is missing: run scripts/dev/extract-data.ps1");
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(types)))
            foreach (var t in doc.RootElement.GetProperty("types").EnumerateArray())
            {
                components[t.GetProperty("type").GetString()!] =
                    [.. t.GetProperty("components").EnumerateArray().Take(t.GetProperty("read").GetInt32()).Select(c => c.GetString()!)];
                // "tags" since 2026-10-20; older files have none (regenerate with ot_components.py --items).
                if (t.TryGetProperty("tags", out var tg)) itemTags[t.GetProperty("type").GetString()!] = [.. tg.EnumerateArray().Select(x => x.GetString()!)];
            }
        List<ushort> visualHashes;
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(visuals)))
            visualHashes = [.. doc.RootElement.EnumerateArray().Select(r => (ushort)r.GetProperty("HASH16").GetInt32())];
        var charges = new Dictionary<string, int>();
        string chargesPath = Path.Combine(folder, "tables", "ComponentCharges.json");
        if (File.Exists(chargesPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(chargesPath));
            foreach (var r in doc.RootElement.EnumerateArray())
                charges[r.GetProperty("BaseItemTypesKey").GetString() ?? ""] = r.GetProperty("MaxCharges").GetInt32();
        }
        // CurrencyItems StackSize by BaseItemTypes row (Orb of Transmutation 40, Scroll of Wisdom 40, Portal Scroll 40 …);
        // only types with a Stack component stack.
        var stackSizes = new Dictionary<int, ushort>();
        var fullStacks = new Dictionary<int, int>();
        string currencyPath = Path.Combine(folder, "tables", "CurrencyItems.json");
        if (File.Exists(currencyPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(currencyPath));
            foreach (var r in doc.RootElement.EnumerateArray())
                if (r.GetProperty("BaseItemTypesKey") is { ValueKind: JsonValueKind.Number } b && r.GetProperty("StackSize").GetInt32() is > 1 and var size)
                {
                    stackSizes.TryAdd(b.GetInt32(), (ushort)Math.Min(size, ushort.MaxValue));
                    if (r.GetProperty("FullStack_BaseItemTypesKey") is { ValueKind: JsonValueKind.Number } full) fullStacks.TryAdd(b.GetInt32(), full.GetInt32());
                }
        }
        var classes = new List<string>();
        var stances = new List<byte>();
        var unmodifiableClasses = new List<bool>();
        var fracturableClasses = new List<bool>();
        var mirrorableClasses = new List<bool>();
        string classesPath = Path.Combine(folder, "tables", "ItemClasses.json");
        if (File.Exists(classesPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(classesPath));
            classes = [.. doc.RootElement.EnumerateArray().Select(r => r.GetProperty("Id").GetString() ?? "")];
            stances = [.. doc.RootElement.EnumerateArray().Select(r => r.GetProperty("ItemStance") is { ValueKind: JsonValueKind.Number } s ? (byte)s.GetInt32() : (byte)0xff)];
            unmodifiableClasses = [.. doc.RootElement.EnumerateArray().Select(r =>
                r.TryGetProperty("Unmodifiable", out var u) && u.ValueKind == JsonValueKind.True)];
            fracturableClasses = [.. doc.RootElement.EnumerateArray().Select(r =>
                r.TryGetProperty("CanBeFractured", out var f) && f.ValueKind == JsonValueKind.True)];
            // __col21 is unnamed in the dat schema. Its true set is exactly the equipment classes (weapons, armour,
            // jewellery, quivers); flasks, jewels, maps and currency are false, which is the Mirror's "non-unique
            // equipment" rule from the wiki.
            mirrorableClasses = [.. doc.RootElement.EnumerateArray().Select(r =>
                r.TryGetProperty("__col21", out var m) && m.ValueKind == JsonValueKind.True)];
        }
        List<string> tagIds = [];
        string tagsPath = Path.Combine(folder, "tables", "Tags.json");
        if (File.Exists(tagsPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(tagsPath));
            tagIds = [.. doc.RootElement.EnumerateArray().Select(r => r.GetProperty("Id").GetString() ?? "")];
        }
        var rows = new List<BaseType>();
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(bases)))
        {
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                string id = r.GetProperty("Id").GetString() ?? "";
                var itemComponents = components.GetValueOrDefault(id) ?? [];
                int visual = r.GetProperty("ItemVisualIdentity").GetInt32();
                rows.Add(new BaseType(rows.Count, id, r.GetProperty("Name").GetString() ?? "", r.GetProperty("HASH32").GetUInt32(),
                    (byte)r.GetProperty("Width").GetInt32(), (byte)r.GetProperty("Height").GetInt32(),
                    visual >= 0 && visual < visualHashes.Count ? visualHashes[visual] : (ushort)0,
                    itemComponents, charges.GetValueOrDefault(id),
                    r.GetProperty("ItemClassesKey") is { ValueKind: JsonValueKind.Number } c && c.GetInt32() < classes.Count ? classes[c.GetInt32()] : "",
                    // the row's own ItemStance, else its class's (ItemStances: Wand = 2)
                    r.GetProperty("ItemStance") is { ValueKind: JsonValueKind.Number } st ? (byte)st.GetInt32()
                        : r.GetProperty("ItemClassesKey") is { ValueKind: JsonValueKind.Number } c2 && c2.GetInt32() < stances.Count ? stances[c2.GetInt32()] : (byte)0xff,
                    r.GetProperty("DropLevel").GetInt32(),
                    itemComponents.Contains("Stack") ? stackSizes.GetValueOrDefault(rows.Count, (ushort)1) : (ushort)1,
                    fullStacks.GetValueOrDefault(rows.Count, -1),
                    // Implicit_ModsKeys: Mods.dat rows every item of the type has (Driftwood Wand: 8-12% increased Spell Damage)
                    [.. r.GetProperty("Implicit_ModsKeys").EnumerateArray().Select(k => mods.Row(k.GetInt32())).OfType<ModCatalog.Mod>()])
                {
                    Tags = new HashSet<string>([.. itemTags.GetValueOrDefault(id) ?? [],
                        .. r.GetProperty("TagsKeys").EnumerateArray().Select(k => k.GetInt32()).Where(k => k < tagIds.Count).Select(k => tagIds[k])]),
                    ModDomain = r.GetProperty("ModDomain").GetInt32(),
                    ClassRow = r.GetProperty("ItemClassesKey") is { ValueKind: JsonValueKind.Number } cr ? cr.GetInt32() : -1,
                    Unmodifiable = r.TryGetProperty("Unmodifiable", out var u) && u.ValueKind == JsonValueKind.True ||
                        r.GetProperty("ItemClassesKey") is { ValueKind: JsonValueKind.Number } uc &&
                        uc.GetInt32() >= 0 && uc.GetInt32() < unmodifiableClasses.Count && unmodifiableClasses[uc.GetInt32()],
                    CanBeFractured = r.GetProperty("ItemClassesKey") is { ValueKind: JsonValueKind.Number } fc &&
                        fc.GetInt32() >= 0 && fc.GetInt32() < fracturableClasses.Count && fracturableClasses[fc.GetInt32()],
                    CanBeMirrored = r.GetProperty("ItemClassesKey") is { ValueKind: JsonValueKind.Number } mc &&
                        mc.GetInt32() >= 0 && mc.GetInt32() < mirrorableClasses.Count && mirrorableClasses[mc.GetInt32()],
                });
            }
        }
        return (rows, null);
    }
}
