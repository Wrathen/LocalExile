using System.Text.Json;
using GameServer.Core.Protocol.Instance;

/// <summary>
/// NPCs (Agents/docs/protocol/17-npcs.md): NPCs.dat rows from the extracted tables with their object type's deserialized
/// components (data/object-types.json), and where each town's NPCs stand: the NPC lines of the town's room (.arm) through
/// the client's room placement (TownNpcs, 17-npcs.md §2).
/// </summary>
sealed class NpcCatalog
{
    /// <summary>An NPCs.dat row. <paramref name="ShopRow"/>: the NPCShop row of its ShopSet (NPCShopSets.Shop), −1 for none.</summary>
    public sealed record Npc(int Row, string Id, string Name, IReadOnlyList<string>? Components, StateMachineData? StateMachine,
        byte TransitionState, int ShopRow = -1)
    {
        /// <summary>The model to send instead of the type's: a town room's line (<see cref="Placement.Model"/>), else the
        /// model a town room gives this NPC (TownNpcs.ModelOf), so /npc shows Irasha, whose type Metadata/NPC/NPC has none.</summary>
        public string? Model { get; init; }
        /// <summary>A fixed world z (a town room's line, <see cref="Placement.Height"/>).</summary>
        public float? Height { get; init; }
        public int HardModeShopRow { get; init; } = -1;
        public bool Supported => Components is { } c && NpcObject.CanSerialize(c, StateMachine is not null);
        public override string ToString() => $"{Name} ({Id}, row {Row})";
    }

    /// <summary>An NPCTalk row: the NPC's dialogue option <paramref name="Index"/> (column 1, what C→S 0x82 carries), its
    /// NPCTalkCategory Id (Gossip, Quests, BuyItems, SellItems, IdentifyItems, RespecPassivePointsForGold …), script and
    /// NPCTextAudio rows.</summary>
    public sealed record Talk(int Row, int Npc, int Index, string Option, string Category, string Script, IReadOnlyList<int> TextAudio)
    {
        /// <summary>A SelectHideout row's script opens the guild's list: <c>SendOpenScreen( HideoutSelectionWindow, 1 )</c>
        /// (the character's list has no argument or 0).</summary>
        public bool OpensGuildHideouts => System.Text.RegularExpressions.Regex.IsMatch(Script, @"HideoutSelectionWindow\s*,\s*1\s*\)");
        public IReadOnlyList<string> RequiredFlags { get; init; } = [];
        public IReadOnlyList<string> ExcludedFlags { get; init; } = [];
        public string? StartFlag { get; init; }
        public string? FinishFlag { get; init; }
        public int? RewardOffer { get; init; }
        public bool Available(IReadOnlySet<string> flags) => RequiredFlags.All(flags.Contains) && !ExcludedFlags.Any(flags.Contains);
    }

    /// <summary>An NPC standing in a town: its NPCs.dat Id, cell and facing (radians); optionally the model and fixed
    /// height its town room gives it and a Transitionable state other than the type's.</summary>
    public sealed record Placement(string Id, int X, int Y, float Rotation, string? Model = null, float? Height = null,
        byte? TransitionState = null)
    {
        /// <summary>A town room's own line (TownNpcs): it may stand on a cell the walk map blocks (a stall, a pier).</summary>
        public bool FromRoom { get; init; }
    }

    // The campaign towns are TownNpcs (their rooms' NPC lines); other towns here. Facing in radians (Positioned +0x29c).
    static readonly Dictionary<ushort, Placement[]> Towns = new()
    {
        // The Azurite Mine Encampment (Delve_Hub, HASH16 0x8b9b): Niko by the waypoint (333, 402) and the arrival
        // (330, 420); our own cell (2026-10-21). The hideout Niko's row: the hub's own (DelveMinerHub) lists no Sell Items.
        [StashCatalog.DelveHub] = [new("Metadata/NPC/League/Delve/DelveMinerHideout", 342, 410, 4.0f)],
        // The Menagerie (Menagerie_Hub): Einhar placed live with /npc_place Einhar 495 485 290 (2026-10-30). The hideout
        // Einhar's row: EinharMenagerie has no .ot in the GGPK, and the client drew it without a model.
        [StashCatalog.MenagerieHub] = [new("Metadata/NPC/League/Bestiary/EinharHideout", 495, 485, 290 * MathF.PI / 180)],
        // The Forbidden Sanctum (SanctumFoyer_Fellshrine): Divinia by the waypoint (126, 149), to be placed live with /npc_place.
        // Kingsmarch (KalguuranSettlersLeague): its NPCs around the arrival (offsets, StashCatalog.FromArrival), to be placed
        // live with /npc_place.
        [StashCatalog.Kingsmarch] =
        [
            new("Metadata/NPC/League/Kalguur/VillageMayor", 0, -14, 4.0f),
            new("Metadata/NPC/League/Kalguur/VillageTujen", 10, -12, 4.0f),
            new("Metadata/NPC/League/Kalguur/VillageGwennen", -10, -12, 4.0f),
            new("Metadata/NPC/League/Kalguur/VillageRog", 14, -4, 4.0f),
            new("Metadata/NPC/League/Kalguur/VillageDannig", -14, -4, 4.0f),
            new("Metadata/NPC/League/Kalguur/VillageFaustus", 14, 6, 4.0f),
            new("Metadata/NPC/League/Kalguur/VillageIsla", -14, 6, 4.0f),
            new("Metadata/NPC/League/Kalguur/VillageFarmer", 8, 14, 4.0f),
            new("Metadata/NPC/League/Kalguur/VillageRecruiter", -8, 14, 4.0f),
        ],
        // The Rogue Harbour (HeistHub): Kurai, Faustus, Adiyah, Whakano and the nine rogues in a ring around the arrival
        // (offsets, StashCatalog.FromArrival), to be placed live with /npc_place.
        [StashCatalog.HeistHub] =
        [
            new("Metadata/NPC/League/Heist/TheBoss", 18, 0, 4.0f),
            new("Metadata/NPC/League/Heist/Fence", 16, 8, 4.0f),
            new("Metadata/NPC/League/Heist/Driver", 10, 15, 4.0f),
            new("Metadata/NPC/League/Heist/Barber", 2, 18, 4.0f),
            new("Metadata/NPC/League/Heist/Lockpick1", -6, 17, 4.0f),
            new("Metadata/NPC/League/Heist/Interrogator1", -13, 12, 4.0f),
            new("Metadata/NPC/League/Heist/Soldier1", -17, 4, 4.0f),
            new("Metadata/NPC/League/Heist/HeavyLifter1", -17, -4, 4.0f),
            new("Metadata/NPC/League/Heist/Detector1", -13, -12, 4.0f),
            new("Metadata/NPC/League/Heist/Demolitionist1", -6, -17, 4.0f),
            new("Metadata/NPC/League/Heist/CatBurglar1", 2, -18, 4.0f),
            new("Metadata/NPC/League/Heist/Illusionist1", 10, -15, 4.0f),
            new("Metadata/NPC/League/Heist/Engineer1", 16, -8, 4.0f),
        ],
        // The Monastery of the Keepers (ChayulaLeague): Ailith by the arrival (an offset, StashCatalog.FromArrival), to be
        // placed live with /npc_place.
        [StashCatalog.ChayulaHub] = [new("Metadata/NPC/League/Chayula/ChayulaFarmerHub", 0, -16, 4.0f)],
        [StashCatalog.SanctumHub] = [new("Metadata/NPC/League/Sanctum/SanctumNPCTown", 136, 158, 4.0f)],
    };

    public IReadOnlyList<Npc> Rows { get; }
    public string? Error { get; }
    readonly Dictionary<string, Npc> _byId;
    readonly Dictionary<(int Npc, int Index), Talk> _talk = [];

    NpcCatalog(List<Npc> rows, string? error, IEnumerable<Talk>? talk = null)
    {
        Rows = rows;
        Error = error;
        foreach (var t in talk ?? []) _talk.TryAdd((t.Npc, t.Index), t);
        _byId = rows.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    public static NpcCatalog Load(string folder = "data")
    {
        try { return Read(folder); }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new([], $"NPCs: {ex.Message}");
        }
    }

    /// <summary>The NPCTalk row an NPC's option index (C→S 0x82) stands for.</summary>
    public Talk? TalkOf(Npc npc, int index) => _talk.GetValueOrDefault((TalkSource(npc).Row, index));

    public bool HasAvailableReward(Npc npc, int offer, IReadOnlySet<string> flags) =>
        _talk.Values.Any(t => t.Npc == TalkSource(npc).Row && t.RewardOffer == offer && t.Available(flags));

    /// <summary>The quest reward offers any NPC currently shows (the quests complete), for the vendors' gem stock.</summary>
    public IReadOnlySet<int> AvailableRewards(IReadOnlySet<string> flags) =>
        _talk.Values.Where(t => t.RewardOffer is { } offer && t.Available(flags)).Select(t => t.RewardOffer!.Value).ToHashSet();

    /// <summary>The NPC whose NPCTalk rows (and shop) this one uses: itself, or for a copy without rows of its own (the
    /// hideout Kirac, NPCs.dat row 412) the first row of the same name that has them (Kirac, 410).</summary>
    public Npc TalkSource(Npc npc) => _talk.Keys.Any(k => k.Npc == npc.Row) ? npc
        : Rows.FirstOrDefault(r => r.Name == npc.Name && _talk.Keys.Any(k => k.Npc == r.Row)) ?? npc;

    /// <summary>The town's NPCs, or none.</summary>
    // Hideouts: Nessa, at the cell the user picked in the Lush Hideout (2026-10-20); the real game has no town NPCs in
    // hideouts. Niko and Kirac (their hideout NPCs.dat rows) stand to her right and left on screen (2026-10-21): screen
    // right is +x / -y on the grid.
    static readonly Placement[] Hideout =
    [
        new("Metadata/NPC/Act1/Nessa", 256, 351, 4.0f),
        new("Metadata/NPC/League/Delve/DelveMinerHideout", 263, 344, 4.0f),
        new("Metadata/NPC/Missions/Hideout/Helena", 270, 337, 4.0f),
        new("Metadata/NPC/Epilogue/KiracHideout", 249, 358, 4.0f),
        new("Metadata/NPC/League/Bestiary/EinharHideout", 242, 365, 4.0f),
    ];

    public static IReadOnlyList<Placement> HideoutNpcs => Hideout;

    /// <summary>The NPCs of an area instance of <paramref name="seed"/>: a campaign town's (the notes say which room's are
    /// left out for another seed), another town's, a hideout's, or none.</summary>
    public static IReadOnlyList<Placement> In(ushort areaHash, uint seed, List<string>? notes = null) =>
        TownNpcs.Of(areaHash) is { } town ? town.Placements(seed, notes)
        : Towns.GetValueOrDefault(areaHash) ?? (HideoutCatalog.ByArea(areaHash) is not null ? Hideout : []);

    /// <summary>The NPC a placement stands for, with the placement's model, height and state.</summary>
    public Npc? Place(Placement p) => Find(p.Id) is { } npc
        ? npc with { Model = p.Model ?? npc.Model, Height = p.Height, TransitionState = p.TransitionState ?? npc.TransitionState }
        : null;

    /// <summary>An NPC by row, Id or name (exact name first, then the first Id or name containing the text).</summary>
    public Npc? Find(string query)
    {
        if (int.TryParse(query, out int row)) return row >= 0 && row < Rows.Count ? Rows[row] : null;
        if (_byId.TryGetValue(query, out var byId)) return byId;
        return Rows.FirstOrDefault(r => r.Name.Equals(query, StringComparison.OrdinalIgnoreCase))
            ?? Rows.FirstOrDefault(r => r.Id.Contains(query, StringComparison.OrdinalIgnoreCase) || r.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    static NpcCatalog Read(string folder)
    {
        string table = Path.Combine(folder, "tables", "NPCs.json");
        if (!File.Exists(table)) return new([], $"{table} is missing: extract the game tables (Tools/PoEDataExtractor)");
        var components = new Dictionary<string, IReadOnlyList<string>>();
        var stateMachines = new Dictionary<string, StateMachineData>();
        var transitionStates = new Dictionary<string, byte>();
        string types = Path.Combine(folder, "object-types.json");
        if (File.Exists(types))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(types));
            foreach (var t in doc.RootElement.GetProperty("types").EnumerateArray())
            {
                string type = t.GetProperty("type").GetString()!;
                if (!type.StartsWith("Metadata/NPC/", StringComparison.OrdinalIgnoreCase)) continue;
                components[type] = [.. t.GetProperty("components").EnumerateArray().Take(t.GetProperty("read").GetInt32()).Select(c => c.GetString()!)];
                if (t.TryGetProperty("shared_states", out var ss) && t.TryGetProperty("shared_timers", out var st))
                    stateMachines[type] = new([.. ss.EnumerateArray().Select(e => e[1].GetInt32())], st.GetArrayLength(),
                        [.. ss.EnumerateArray().Select(e => e[0].GetString() ?? "")]);
                if (t.TryGetProperty("transitionable", out var tr) && tr.TryGetProperty("num_states", out var ns))
                    transitionStates[type] = (byte)Math.Clamp(ns.GetInt32(), 1, 127);
            }
        }
        var shopOfSet = new List<int>();
        var hardModeShopOfSet = new List<int>();
        string sets = Path.Combine(folder, "tables", "NPCShopSets.json");
        if (File.Exists(sets))
            using (var doc = JsonDocument.Parse(File.ReadAllBytes(sets)))
            {
                shopOfSet = [.. doc.RootElement.EnumerateArray().Select(s => s.GetProperty("Shop").ValueKind == JsonValueKind.Number ? s.GetProperty("Shop").GetInt32() : -1)];
                hardModeShopOfSet = [.. doc.RootElement.EnumerateArray().Select(s => s.TryGetProperty("ShopRuthless", out var shop)
                    && shop.ValueKind == JsonValueKind.Number ? shop.GetInt32() : -1)];
            }
        var rows = new List<Npc>();
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(table)))
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                string id = r.GetProperty("Id").GetString() ?? "";
                // The client files every NPCs.dat row under its Id on the row's Metadata .ot (the register's 0xac-byte
                // table, base at row +0x10; 04-player-entity.md §1): an Id without an .ot of its own (Act 6's Tarkleigh on
                // Act 1's) has its Metadata's components.
                string meta = r.TryGetProperty("Metadata", out var m) ? m.GetString() ?? "" : "";
                string type = components.ContainsKey(id) || !components.ContainsKey(meta) ? id : meta;
                // Without object-types.json, assume the base NPC list.
                IReadOnlyList<string>? list = components.Count == 0 ? NpcObject.BaseComponents : components.GetValueOrDefault(type);
                int set = r.GetProperty("ShopSet").ValueKind == JsonValueKind.Number ? r.GetProperty("ShopSet").GetInt32() : -1;
                rows.Add(new Npc(rows.Count, id, r.GetProperty("Name").GetString() ?? "", list,
                    stateMachines.GetValueOrDefault(type), transitionStates.GetValueOrDefault(type, (byte)1),
                    set >= 0 && set < shopOfSet.Count ? shopOfSet[set] : -1)
                {
                    Model = TownNpcs.ModelOf(id),
                    HardModeShopRow = set >= 0 && set < hardModeShopOfSet.Count ? hardModeShopOfSet[set] : -1,
                });
            }
        return new(rows, null, ReadTalk(folder));
    }

    static List<Talk> ReadTalk(string folder)
    {
        string table = Path.Combine(folder, "tables", "NPCTalk.json"), categories = Path.Combine(folder, "tables", "NPCTalkCategory.json");
        if (!File.Exists(table) || !File.Exists(categories)) return [];
        List<string> names;
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(categories)))
            names = [.. doc.RootElement.EnumerateArray().Select(c => c.GetProperty("Id").GetString() ?? "")];
        string flagsFile = Path.Combine(folder, "tables", "QuestFlags.json");
        List<string> flags = [];
        if (File.Exists(flagsFile))
            using (var doc = JsonDocument.Parse(File.ReadAllBytes(flagsFile)))
                flags = [.. doc.RootElement.EnumerateArray().Select(f => f.GetProperty("Id").GetString() ?? "")];
        string? Flag(int row) => row >= 0 && row < flags.Count && flags[row] != "NEVER_SET" ? flags[row] : null;
        var talk = new List<Talk>();
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(table)))
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                int Int(string name) => r.GetProperty(name).ValueKind == JsonValueKind.Number ? r.GetProperty(name).GetInt32() : -1;
                int category = Int("Category");
                int OptionalInt(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : -1;
                string[] FlagList(string name) => r.TryGetProperty(name, out var v)
                    ? [.. v.EnumerateArray().Select(e => e.GetInt32()).Where(i => i >= 0 && i < flags.Count).Select(i => flags[i])] : [];
                talk.Add(new Talk(talk.Count, Int("NPCKey"), Int("__col1"), r.GetProperty("DialogueOption").GetString() ?? "",
                    category >= 0 && category < names.Count ? names[category] : "", r.GetProperty("Script").GetString() ?? "",
                    [.. r.GetProperty("NPCTextAudioKeys").EnumerateArray().Select(e => e.GetInt32())])
                {
                    RequiredFlags = FlagList("QuestFlag1"), ExcludedFlags = FlagList("QuestFlag2"),
                    StartFlag = Flag(OptionalInt("QuestFlag4")), FinishFlag = Flag(OptionalInt("QuestFlag5")),
                    RewardOffer = OptionalInt("QuestRewardOffersKey") is int offer and >= 0 ? offer : null
                });
            }
        return talk;
    }
}
