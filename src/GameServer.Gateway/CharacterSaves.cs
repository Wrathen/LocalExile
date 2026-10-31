using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameServer.Core.Accounts;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Saves;
using GameServer.Core.World;
using GameServer.Storage;

/// <summary>
/// A character's saved state: its items per inventory (Inventories.dat row), its skill bar and its progress, one row per
/// character in the save database (<see cref="Saves"/>, data/saves.db, git-ignored). Items are stored by BaseItemTypes
/// HASH32 and rebuilt through the <see cref="ItemCatalog"/>, so the save holds no client-side layout. Every change is a
/// transaction: it reads the row, changes one part and writes it back, and nothing else writes in between.
/// </summary>
static class CharacterSaves
{
    public sealed record SavedSocket(SocketColour Colour, SavedItem? Gem = null);

    /// <summary>A mod by its Mods.dat Id (hashes are the client's lookup key, Ids are what people read), the list it is
    /// in and its stat values.</summary>
    public sealed record SavedMod(string Id, ModSection Section, List<int> Values);

    /// <summary>Mods: null in saves from before mods (the item gets its type's implicits again), else every mod.
    /// A missing defence roll keeps the old minimum (zero); raw 32 preserves the native special state. Saves from before
    /// influences, split and synthesised read as none of them.</summary>
    public sealed record SavedItem(uint Type, byte ItemLevel = 1, byte Quality = 0, int Charges = 0, ushort StackSize = 1,
        List<SavedSocket>? Sockets = null, List<byte>? Links = null, int GemExperience = 0,
        ItemRarity Rarity = ItemRarity.Normal, bool Identified = true, List<SavedMod>? Mods = null, List<uint>? Name = null,
        int GemVariant = 0, bool Corrupted = false, byte Fractured = 0, bool Mirrored = false, byte GemFlags = 0, ushort? Visual = null,
        byte DefenceBaseRoll = 0, ItemInfluence Influences = ItemInfluence.None, bool Split = false, bool Synthesised = false);

    public sealed record SavedPlacement(uint Id, byte X, byte Y, SavedItem Item);

    /// <summary>A worn cosmetic: the record picked (a MtxTypes row + 1) and the 0x1f9 option byte it was picked with.</summary>
    public sealed record SavedCosmetic(uint Record, byte Option = 0);

    /// <summary>Skill bar: slot index → a GrantedEffects Id (skill ids change with the socketed gems, effects do not),
    /// "Action/&lt;ActionTypes Id&gt;" for a basic action ("Action/Move") or "" for a cleared slot. Experience: the
    /// character's total (its level is the account's; a save from another level is ignored). Passives: the allocated
    /// nodes' PassiveSkillGraphId. Waypoints: the WorldAreas Ids of the activated waypoints; Visited: of the areas
    /// entered (their visit quest flags reveal the World panel's pins). QuestFlags: the quest flags quests set
    /// (QuestFlags.dat Ids, 12-quests.md §5). Gold: only in older saves, see <see cref="TakeGold"/>. StartWeapon: the
    /// class's start weapon while it waits on the ground for the character to pick it up (<see cref="PlayerInventories.StartWeapon"/>).
    /// PortalCosmetic: the MicrotransactionPortalVariations row the character's portals use (null = the base effect), so a
    /// /cosmetic portal choice survives a relog. Cosmetics: what the Cosmetics tab (K) wears, MicrotransactionSlot Id →
    /// the picked record (<see cref="MtxCatalog"/>: a MtxTypes row + 1). SkillCosmetics: the Cosmetics tab's skill
    /// slots, <see cref="SkillCosmeticKey"/> (a gem's BaseItemTypes HASH32 and effect slot type) → the picked record
    /// (every gem of that type wears it). ClusterPassives: the cluster jewel nodes the client allocated (its expansion
    /// layout ids, not PassiveSkillGraphIds).</summary>
    public sealed record Save(Dictionary<byte, List<SavedPlacement>> Inventories, Dictionary<int, string>? SkillBar = null,
        uint? Experience = null, List<ushort>? Passives = null, List<string>? Waypoints = null, List<string>? Visited = null,
        List<string>? QuestFlags = null, ushort? ExtraPassivePoints = null,
        Dictionary<ushort, ushort>? MasteryEffects = null, uint? Gold = null, ushort? RefundPoints = null,
        SavedItem? StartWeapon = null, ushort? PortalCosmetic = null, Dictionary<byte, SavedCosmetic>? Cosmetics = null,
        Dictionary<string, SavedCosmetic>? SkillCosmetics = null, List<string>? ActiveAuras = null, string? SelectedHideout = null,
        OpeningTutorial? Tutorial = null, AscendancyProgress? MainAscendancy = null,
        Dictionary<string, Dictionary<string, HideoutPlacement>>? HideoutPlacements = null,
        uint? Life = null, uint? Mana = null, VitalsSession? VitalsOwner = null, List<ushort>? ClusterPassives = null,
        ushort? Sulphite = null, Dictionary<ushort, string>? Tattoos = null,
        Dictionary<ushort, ushort>? TattooKeystones = null, List<List<ushort>>? AtlasPassivePresets = null,
        ushort? AtlasPassivePoints = null)
    {
        /// <summary>Fields this build doesn't know (a newer build wrote them): kept as they are by the next write.</summary>
        [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; init; }
    }

    public static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    /// <summary>Deferred class gems and progress for new characters. Null preserves older pre-granted starter kits.</summary>
    public sealed record OpeningTutorial(SavedItem SkillGem, SavedItem SupportGem,
        bool Talked = false, bool ZombieKilled = false, bool ChestOpened = false, bool SkillClaimed = false, bool SupportClaimed = false,
        string? Id = null);
    public static void ChangeTutorial(string character, Func<OpeningTutorial, OpeningTutorial> change) =>
        Update(character, s => s.Tutorial is { } tutorial ? s with { Tutorial = change(tutorial) } : s);
    public static void SetTutorial(string character, OpeningTutorial tutorial) => Update(character, s => s with { Tutorial = tutorial });

    /// <summary>The character's save, or null when it has none, with the experience gained since it was last saved
    /// (<see cref="AddExperience"/>): every write that loads it saves that too. A save that can't be read throws
    /// <see cref="UnreadableSaveException"/> and stays as it is.</summary>
    public static Save? Load(string character)
    {
        var save = Saves.Load<Save>(SaveDatabase.Table.Characters, character, Opts);
        return save is not null && UnsavedExperience.TryGetValue(character, out uint xp) ? save with { Experience = xp } : save;
    }

    public static void Write(string character, Save save) =>
        Saves.Put(SaveDatabase.Table.Characters, character, JsonSerializer.Serialize(save, Opts));

    public static void SetHideoutPlacement(string character, string area, string key, HideoutPlacement placement) =>
        Update(character, save =>
        {
            var areas = save.HideoutPlacements is null
                ? new Dictionary<string, Dictionary<string, HideoutPlacement>>() : new(save.HideoutPlacements);
            var positions = areas.TryGetValue(area, out var prior)
                ? new Dictionary<string, HideoutPlacement>(prior) : [];
            positions[key] = placement;
            areas[area] = positions;
            return save with { HideoutPlacements = areas };
        });

    public static void SetSelectedHideout(string character, string area) => Update(character, s => s with { SelectedHideout = area });

    /// <summary>The character's Voltaxic Sulphite (the Delve gauge's current value).</summary>
    public static void SetSulphite(string character, ushort amount) => Update(character, s => s with { Sulphite = amount });

    /// <summary>A Ruthless resource writer. Retired transfer owners retain their token so only that exact failed
    /// transfer can resume; a newer area's token can never be reclaimed by the old connection.</summary>
    public sealed record VitalsSession(Guid CharacterId, Guid SessionId, bool Retired = false);

    public static (uint Life, uint Mana) BeginVitalsSession(string character, Guid characterId, Guid sessionId,
        string? league, Func<Save?, (uint Life, uint Mana)> initialize) => Saves.Atomically(() =>
    {
        if (!VitalsCharacterMatches(character, characterId, league))
            throw new InvalidOperationException("The resource owner is no longer the admitted character");
        var save = Load(character);
        // A reused name must not inherit a prior identity's pools, even if an older server retained that save.
        var initial = initialize(save?.VitalsOwner is { } owner && owner.CharacterId != characterId ? null : save);
        Write(character, (save ?? new Save([])) with
        {
            Life = initial.Life, Mana = initial.Mana, VitalsOwner = new(characterId, sessionId),
        });
        return initial;
    });

    /// <summary>Only the currently admitted UUID/session may save pools, including its ordinary logout save.</summary>
    public static bool SaveVitals(string character, Guid characterId, Guid sessionId, string? league,
        uint life, uint mana, bool retire = false) => ChangeVitalsSession(character, characterId, sessionId, league,
            life, mana, wasRetired: false, retired: retire);

    public static bool ResumeVitalsSession(string character, Guid characterId, Guid sessionId, string? league,
        uint life, uint mana) => ChangeVitalsSession(character, characterId, sessionId, league,
            life, mana, wasRetired: true, retired: false);

    static bool ChangeVitalsSession(string character, Guid characterId, Guid sessionId, string? league,
        uint life, uint mana, bool wasRetired, bool retired) => Saves.Atomically(() =>
    {
        if (!VitalsCharacterMatches(character, characterId, league) || Load(character) is not { } save
            || save.VitalsOwner != new VitalsSession(characterId, sessionId, wasRetired)) return false;
        Write(character, save with { Life = life, Mana = mana, VitalsOwner = new(characterId, sessionId, retired) });
        return true;
    });

    static bool VitalsCharacterMatches(string character, Guid characterId, string? league) =>
        new SqliteAccountStore(Saves.Db).FindCharacter(character) is { } current
        && HardcoreDeathRules.TicketMatches(current, characterId, league);

    public static void ClearActiveAuras(string character) => Update(character, save => save with { ActiveAuras = [] });

    /// <summary>Persist aura intent by stable GrantedEffects Id, replacing any alias of the same aura family.</summary>
    public static void SetAuraActive(string character, string effect, bool active) => Update(character, save =>
    {
        string family = AuraCatalog.Family(effect);
        var auras = (save.ActiveAuras ?? []).Where(id => AuraCatalog.Family(id) != family).ToList();
        if (active) auras.Add(effect);
        return save with { ActiveAuras = auras };
    });

    /// <summary>Records what a skill bar slot holds (see <see cref="Save.SkillBar"/>), keeping the saved items.</summary>
    public static void SetSkillBar(string character, byte slot, string entry) => Update(character, save =>
    {
        var bar = save.SkillBar is null ? new Dictionary<int, string>() : new Dictionary<int, string>(save.SkillBar);
        bar[slot] = entry;
        return save with { SkillBar = bar };
    });

    /// <summary>Records the character's inventories and its start weapon still on the ground, keeping the rest of the save.</summary>
    public static void SetInventories(string character, Dictionary<byte, List<SavedPlacement>> inventories, SavedItem? startWeapon = null) =>
        Update(character, s => s with { Inventories = inventories, StartWeapon = startWeapon });

    /// <summary>The character's level: from its saved experience. The account's level (Auth, the ticket's) is only a copy
    /// for the character list, which a failed update at a level-up leaves behind; it counts only for a save without
    /// experience (an older one, or none).</summary>
    public static int LevelOf(Save? save, int accountLevel, IReadOnlyList<uint>? levelStarts = null) => save?.Experience is uint xp
        ? Progression.LevelFor(xp, levelStarts ?? CharacterStats.LevelStarts)
        : Math.Clamp(accountLevel, 1, Progression.MaxLevel);

    /// <summary>Records the character's experience (a level set by hand) at once, keeping the rest of the save.</summary>
    public static void SetExperience(string character, uint experience)
    {
        // Through the unsaved total, so a kill's gain meanwhile adds to this one instead of to the old total.
        UnsavedExperience[character] = experience;
        try { SaveExperience(character); }
        catch
        {
            UnsavedExperience.TryRemove(new KeyValuePair<string, uint>(character, experience));   // not set: the caller hears why
            throw;
        }
    }

    /// <summary>Records the portal cosmetic (/cosmetic portal): a MicrotransactionPortalVariations row, 0 for the base
    /// effect, which is stored as no field so an older save keeps reading as the default.</summary>
    public static void SetPortalCosmetic(string character, ushort variation) =>
        Update(character, s => s with { PortalCosmetic = variation == 0 ? null : variation });

    /// <summary>Records what a Cosmetics tab slot wears (null empties it), keeping the rest of the save.</summary>
    public static void SetCosmetic(string character, byte slot, SavedCosmetic? worn) => Update(character, s =>
    {
        var cosmetics = s.Cosmetics is null ? new Dictionary<byte, SavedCosmetic>() : new Dictionary<byte, SavedCosmetic>(s.Cosmetics);
        if (worn is null) cosmetics.Remove(slot); else cosmetics[slot] = worn;
        return s with { Cosmetics = cosmetics.Count == 0 ? null : cosmetics };
    });

    /// <summary>A skill cosmetic's save key: the gem's BaseItemTypes HASH32 (hex) / its effect slot type
    /// (MicrotransactionSkillGemEffectSlotTypes row).</summary>
    public static string SkillCosmeticKey(uint gem, byte slotType) => $"{gem:x8}/{slotType}";

    /// <summary>The gem HASH32 of a <see cref="SkillCosmeticKey"/>, or null for a key it did not write.</summary>
    public static uint? SkillCosmeticGem(string key) =>
        key.Split('/') is [var gem, _] && uint.TryParse(gem, System.Globalization.NumberStyles.HexNumber, null, out uint g) ? g : null;

    /// <summary>Records what a gem type's skill slot wears (null empties it), keeping the rest of the save.</summary>
    public static void SetSkillCosmetic(string character, uint gem, byte slotType, SavedCosmetic? worn) => Update(character, s =>
    {
        var skills = s.SkillCosmetics is null ? new Dictionary<string, SavedCosmetic>() : new Dictionary<string, SavedCosmetic>(s.SkillCosmetics);
        if (worn is null) skills.Remove(SkillCosmeticKey(gem, slotType)); else skills[SkillCosmeticKey(gem, slotType)] = worn;
        return s with { SkillCosmetics = skills.Count == 0 ? null : skills };
    });

    /// <summary>The character's skill cosmetics by <see cref="SkillCosmeticKey"/>; empty when none are saved.</summary>
    public static IReadOnlyDictionary<string, SavedCosmetic> SkillCosmetics(string character) =>
        Load(character)?.SkillCosmetics ?? new Dictionary<string, SavedCosmetic>();

    /// <summary>The character's worn cosmetics by MicrotransactionSlot Id; empty when none are saved.</summary>
    public static IReadOnlyDictionary<byte, SavedCosmetic> Cosmetics(string character) =>
        Load(character)?.Cosmetics ?? new Dictionary<byte, SavedCosmetic>();

    /// <summary>The character's portal cosmetic, 0 (the base effect) when none is saved.</summary>
    public static ushort PortalCosmetic(string character) => Load(character)?.PortalCosmetic ?? 0;

    /// <summary>How long experience may wait in memory before it is saved (<see cref="SaveExperience()"/>).</summary>
    public static readonly TimeSpan ExperienceSaveInterval = TimeSpan.FromSeconds(5);

    // The character's experience total gained since it was last saved. Kills come by the hundred per second, and a
    // committed write per kill would queue the Gateway behind the disk; a crash or a power cut loses at most the last few
    // seconds of it. Shared by every connection of the character, so an older connection's gains still count.
    static readonly ConcurrentDictionary<string, uint> UnsavedExperience = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds experience to the character's total (<paramref name="add"/>: the total before → after) and returns both. The
    /// total is the character's, not a connection's copy: gains from an older connection still running (an area change) or
    /// from several tasks at once all count. It is kept in memory and saved within <see cref="ExperienceSaveInterval"/>,
    /// at a level-up, when the connection ends, or with any other write of the character's save. A save without
    /// experience starts from <paramref name="start"/>.
    /// </summary>
    public static (uint Before, uint After) AddExperience(string character, uint start, Func<uint, uint> add)
    {
        uint before = 0, after = 0;
        // The last run of either function is the one stored (ConcurrentDictionary retries a lost race).
        UnsavedExperience.AddOrUpdate(character,
            name => after = add(before = Saves.Load<Save>(SaveDatabase.Table.Characters, name, Opts)?.Experience ?? start),
            (_, total) => after = add(before = total));
        return (before, after);
    }

    /// <summary>Saves the character's unsaved experience, if any. A character with no save any more (deleted) only drops it.</summary>
    public static void SaveExperience(string character)
    {
        if (!UnsavedExperience.ContainsKey(character)) return;
        uint? flushed = null;
        Saves.Atomically(() =>
        {
            // Sample inside the write transaction: a queued flush must not restore XP captured before a level reset.
            if (!UnsavedExperience.TryGetValue(character, out uint xp)) return;
            if (Saves.Load<Save>(SaveDatabase.Table.Characters, character, Opts) is { } save) Write(character, save with { Experience = xp });
            flushed = xp;
        });
        if (flushed is { } saved)
            UnsavedExperience.TryRemove(new KeyValuePair<string, uint>(character, saved)); // a later gain waits for the next save
    }

    /// <summary>Saves every character's unsaved experience (the Gateway's timer, its shutdown). One that can't be saved
    /// stays in memory for the next try and doesn't stop the others.</summary>
    public static void SaveExperience()
    {
        foreach (string character in UnsavedExperience.Keys)
            try { SaveExperience(character); }
            catch (IOException e) { Console.WriteLine($"[saves] experience of {character} not saved yet: {e.Message}"); }
    }

    /// <summary>Drops a character's unsaved experience without saving it: its save is gone (deleted), and a new character
    /// of that name must not get it.</summary>
    public static void ForgetExperience(string character) => UnsavedExperience.TryRemove(character, out _);

    /// <summary>Sets (or clears) quest flags (QuestFlags.dat Ids) in the save and keeps the others, so flags another
    /// connection set meanwhile stay.</summary>
    public static void ChangeQuestFlags(string character, IEnumerable<string> flags, bool on) => Update(character, s =>
    {
        var saved = new HashSet<string>(s.QuestFlags ?? []);
        if (on) saved.UnionWith(flags); else saved.ExceptWith(flags);
        return s with { QuestFlags = [.. saved.Order()] };
    });

    /// <summary>Records the allocated passives (PassiveSkillGraphId) with their mastery effects (node → effect HASH16), and
    /// the refund points when given, in one write.</summary>
    public static void SetPassives(string character, IEnumerable<ushort> nodes, IReadOnlyDictionary<ushort, ushort> effects, ushort? refundPoints = null) =>
        Update(character, s => s with { Passives = [.. nodes], MasteryEffects = new(effects), RefundPoints = refundPoints ?? s.RefundPoints });

    /// <summary>Records the cluster jewel nodes the client allocated, in the order it sent them.</summary>
    public static void SetClusterPassives(string character, IEnumerable<ushort> nodes) =>
        Update(character, s => s with { ClusterPassives = [.. nodes] });

    /// <summary>Records the three Atlas passive presets (PassiveSkillGraphId lists) and the points the character has.</summary>
    public static void SetAtlasPassives(string character, IReadOnlyList<IReadOnlyList<ushort>> presets, ushort points) =>
        Update(character, s => s with
        {
            AtlasPassivePresets = [.. presets.Select(p => p.ToList())],
            AtlasPassivePoints = points,
        });

    /// <summary>Records the applied tattoos (passive node -> tattoo name). The map is independent of the allocated
    /// passive list: unallocating a tattooed passive does not remove the tattoo.</summary>
    public static void SetTattoos(string character, IReadOnlyDictionary<ushort, string> tattoos,
        IReadOnlyDictionary<ushort, ushort>? keystones = null) =>
        Update(character, s => s with
        {
            Tattoos = new(tattoos),
            TattooKeystones = keystones is null ? s.TattooKeystones : new(keystones),
        });

    /// <summary>
    /// Main ascendancy identity, its own nodes/point awards and any associated ordinary-node/refund changes commit in
    /// one transaction. Callers validate first and publish runtime state only after the outermost transaction commits.
    /// Unrelated saved fields (including fields from newer builds) are retained.
    /// </summary>
    public static void SetMainAscendancy(string character, AscendancyProgress progress, IEnumerable<ushort>? passives = null,
        IReadOnlyDictionary<ushort, ushort>? effects = null, ushort? refundPoints = null, PassiveProgressSnapshot? expected = null) => Update(character, s =>
    {
        if (expected is not null && !expected.Matches(s)) throw new PassiveProgressConflictException();
        return s with
        {
            MainAscendancy = progress with { Nodes = progress.Allocated.ToArray(), Awards = progress.EarnedAwards.ToArray() },
            Passives = passives is null ? s.Passives : [.. passives],
            MasteryEffects = effects is null ? s.MasteryEffects : new(effects),
            RefundPoints = refundPoints ?? s.RefundPoints,
        };
    });

    /// <summary>
    /// The passive state a connection based its mutation on. Captured copies are compared inside the write transaction,
    /// so an old area cannot replace a newer area's choices, allocations or refund payment. Unrelated fields are merged.
    /// </summary>
    public sealed class PassiveProgressSnapshot
    {
        readonly string _ascendancy;
        readonly ushort[] _passives;
        readonly Dictionary<ushort, ushort> _masteries;
        readonly ushort _refundPoints;

        public PassiveProgressSnapshot(AscendancyProgress? ascendancy, IEnumerable<ushort>? passives,
            IReadOnlyDictionary<ushort, ushort>? masteries, ushort refundPoints)
        {
            _ascendancy = AscendancyJson(ascendancy);
            _passives = passives?.ToArray() ?? [];
            _masteries = masteries is null ? [] : new(masteries);
            _refundPoints = refundPoints;
        }

        public static PassiveProgressSnapshot From(Save? save) =>
            new(save?.MainAscendancy, save?.Passives, save?.MasteryEffects, save?.RefundPoints ?? 0);

        internal bool Matches(Save save) => _refundPoints == (save.RefundPoints ?? 0) &&
            _passives.SequenceEqual(save.Passives ?? []) &&
            _masteries.Count == (save.MasteryEffects?.Count ?? 0) &&
            _masteries.All(kv => save.MasteryEffects is not null && save.MasteryEffects.TryGetValue(kv.Key, out ushort value) && value == kv.Value) &&
            _ascendancy == AscendancyJson(save.MainAscendancy);

        static string AscendancyJson(AscendancyProgress? progress)
        {
            progress ??= new();
            return JsonSerializer.Serialize(progress with { Nodes = progress.Allocated.ToArray(), Awards = progress.EarnedAwards.ToArray() }, Opts);
        }
    }

    /// <summary>
    /// Changes a selection/point award from the latest durable state, not an old area's connection copy. In particular,
    /// simultaneous completion/retry of two Labyrinth tiers cannot overwrite one another's awards. The returned state
    /// may be published after the outermost transaction commits; a failed write returns nothing.
    /// </summary>
    public static AscendancyProgress ChangeMainAscendancy(string character, Func<AscendancyProgress, AscendancyProgress> change) =>
        Saves.Atomically(() =>
        {
            var save = Load(character) ?? new Save([]);
            var progress = change(save.MainAscendancy ?? new());
            progress = progress with { Nodes = progress.Allocated.ToArray(), Awards = progress.EarnedAwards.ToArray() };
            Write(character, save with { MainAscendancy = progress });
            return progress;
        });

    /// <summary>Records the extra passive skill points from /add_passive_points.</summary>
    public static void SetExtraPassivePoints(string character, ushort points) =>
        Update(character, s => s with { ExtraPassivePoints = points });

    /// <summary>Records the refund points (0x33's first u16; /add_refund_points grants them for now).</summary>
    public static void SetRefundPoints(string character, ushort points) =>
        Update(character, s => s with { RefundPoints = points });


    /// <summary>Takes the gold an older save kept with the character (gold is the account's now, AccountStash.Gold): the
    /// amount, 0 when there is none; the save no longer holds it.</summary>
    public static uint TakeGold(string character) => Saves.Atomically(() =>
    {
        if (Load(character) is not { Gold: uint gold } save) return 0u;
        Write(character, save with { Gold = null });
        return gold;
    });

    /// <summary>Records the character's activated waypoints, keeping the rest of the save.</summary>
    public static void SetWaypoints(string character, IEnumerable<string> areaIds) => Update(character, s => s with { Waypoints = [.. areaIds] });

    /// <summary>Records the areas the character has entered, keeping the rest of the save.</summary>
    public static void SetVisited(string character, IEnumerable<string> areaIds) => Update(character, s => s with { Visited = [.. areaIds] });

    /// <summary>Adds activated waypoints to the saved ones (another connection's additions meanwhile stay).</summary>
    public static void AddWaypoints(string character, IEnumerable<string> areaIds) =>
        Update(character, s => s with { Waypoints = [.. (s.Waypoints ?? []).Union(areaIds)] });

    /// <summary>Adds entered areas to the saved ones (another connection's additions meanwhile stay).</summary>
    public static void AddVisited(string character, IEnumerable<string> areaIds) =>
        Update(character, s => s with { Visited = [.. (s.Visited ?? []).Union(areaIds)] });

    static void Update(string character, Func<Save, Save> change) =>
        Saves.Atomically(() => Write(character, change(Load(character) ?? new Save([]))));

    public static SavedItem ToSaved(Item i, ModCatalog? mods = null) => new(i.TypeHash, i.ItemLevel, i.Quality, i.Charges, i.StackSize,
        i.Sockets.Count == 0 ? null : [.. i.Sockets.Select(s => new SavedSocket(s.Colour, s.Gem is null ? null : ToSaved(s.Gem, mods)))],
        i.Links is null ? null : [.. i.Links], i.GemExperience, i.Rarity, i.Identified,
        [.. i.Mods.Select(m => new SavedMod(mods?.ByHash(m.Hash)?.Id ?? $"0x{m.Hash:x4}", m.Section, [.. m.Values]))],
        i.NameWords.Count == 0 ? null : [.. i.NameWords], i.GemVariant, i.Corrupted, i.FracturedCount, i.Mirrored, i.GemFlags, i.Rarity == ItemRarity.Unique ? i.VisualIdentity : null,
        i.DefenceBaseRoll, i.Influences, i.Split, i.Synthesised);

    /// <summary>The item again, with its socketed gems and mods. A type, gem or mod the game data doesn't know throws
    /// <see cref="UnknownSavedItemException"/>: dropping it would lose it at the next save.</summary>
    public static Item FromSaved(SavedItem s, ItemCatalog catalog)
    {
        if (catalog.ByHash(s.Type) is not { } t)
            throw new UnknownSavedItemException($"item type 0x{s.Type:x8}" + (catalog.Error is { } e ? $" ({e})" : ""));
        bool emptyResonator = ItemCatalog.ResonatorSockets(t) > 0 && (s.Sockets ?? []).All(k => k.Gem is null);
        if (emptyResonator) t = catalog.EmptyResonatorType(t);
        var item = ItemCatalog.Create(t, defenceBaseRoll: s.DefenceBaseRoll) with
        {
            VisualIdentity = s.Visual ?? t.VisualIdentity,
            DefenceBaseRoll = s.DefenceBaseRoll,
            ItemLevel = s.ItemLevel, Quality = s.Quality, Charges = s.Charges, StackSize = s.StackSize, GemExperience = s.GemExperience, GemVariant = s.GemVariant, GemFlags = s.GemFlags,
            Sockets = emptyResonator ? ItemCatalog.ResonatorSocketList(t)
                : [.. (s.Sockets ?? []).Select(k => new ItemSocket(k.Colour, k.Gem is null ? null : FromSaved(k.Gem, catalog)))],
            Links = emptyResonator ? null : s.Links, Rarity = s.Rarity, Identified = s.Identified, NameWords = s.Name ?? [], Corrupted = s.Corrupted, FracturedCount = s.Fractured, Mirrored = s.Mirrored,
            Influences = s.Influences, Split = s.Split, Synthesised = s.Synthesised,
        };
        if (s.Mods is not null) item = item with { Mods = [.. s.Mods.Select(m => FromSaved(m, catalog.Mods))] };
        return ImpossibleEscapeCatalogue.Migrate(item, catalog.Mods);
    }

    /// <summary>The mod again, by Id, or by an "0x…" hash when Mods.dat was not loaded at save time.</summary>
    static ItemMod FromSaved(SavedMod m, ModCatalog mods)
    {
        var mod = m.Id.StartsWith("0x") && ushort.TryParse(m.Id[2..], System.Globalization.NumberStyles.HexNumber, null, out ushort h)
            ? mods.ByHash(h) : mods.ById(m.Id);
        return mod?.With(m.Section, m.Values, Random.Shared) ?? throw new UnknownSavedItemException($"mod {m.Id}");
    }
}

/// <summary>A save holds an item, gem or mod the extracted game data doesn't know (tables missing or out of date). The
/// login stops and the save stays as it is.</summary>
sealed class UnknownSavedItemException(string what) : IOException(
    $"the save holds {what}, which the game data doesn't know; nothing was changed: extract the game data " +
    "(scripts/dev/extract-data.ps1, Agents/GettingStarted.md) and log in again");
