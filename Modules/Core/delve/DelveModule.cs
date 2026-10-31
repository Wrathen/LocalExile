using System.Runtime.CompilerServices;
using System.Text.Json;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.World;
using GameServer.Modding;

namespace GameServer.Modules.Delve;

/// <summary>Delve: the Crawler and the Voltaxic Generator in the Azurite Mine Encampment, the chart, Niko's options, the equipment
/// upgrades and their stats. The mine's own mechanics (darkness, the cart, depth) are not built.</summary>
public sealed class DelveModule : IModule
{
    /// <summary>The Azurite Mine Encampment (<c>WorldAreas</c> Delve_Hub).</summary>
    public const ushort Hub = 0x8b9b;
    const string HubId = "Delve_Hub";
    /// <summary>Where a module saves: the account's equipment and the character's sulphite.</summary>
    public const string EquipmentKey = "delve", SulphiteKey = "delve.sulphite";

    /// <summary>Where the Crawler stands in the encampment, on the rails by the mine entrance, and its facing
    /// (placed live with /delve_crawler 408 310 90, 2026-10-30).</summary>
    static readonly (int X, int Y) CrawlerAt = (408, 310);
    const float CrawlerRotation = MathF.PI / 2;
    // hub.arm: console (337,307), room origin (69,69), size621, orientation3 => (376,352).
    static readonly (int X, int Y) GeneratorAt = (376, 352);
    /// <summary>How far the Crawler's walk blockers stand from its middle along its length (cells).</summary>
    const int CrawlerHalfLength = 6;
    const int ObjectReach = WalkBlockerObject.Size - 1; // Positioned object_size 5 is stored as radius 4.

    sealed class State
    {
        public uint Crawler;
        public (int X, int Y) CrawlerCell;
        public readonly List<uint> Blockers = [];
        public readonly List<uint> Attachments = [];
        public uint Generator;
        public bool GeneratorOpen;
        /// <summary>What the next Delve chart (S→C 0x240) carries; /delve_chart sets it (one cell at 0, 0 by default).</summary>
        public (uint A, ushort B, List<InstanceDelve.Cell> Cells) ChartProbe = (0, 0, [new(0, 0, 0, 0, 0)]);
    }

    readonly ConditionalWeakTable<IPlayer, State> _states = new();
    static readonly Lazy<string?[]> StatIds = new(() =>
    {
        if (!File.Exists("data/tables/Stats.json")) return [];
        using var doc = JsonDocument.Parse(File.ReadAllBytes("data/tables/Stats.json"));
        return [.. doc.RootElement.EnumerateArray().Select(r => r.GetProperty("Id").GetString())];
    });

    /// <summary>The account's equipment, in the league.</summary>
    public static DelveProgress Equipment(ISavedData account) =>
        account.Get(EquipmentKey) is { } text ? JsonSerializer.Deserialize<DelveProgress>(text, Json) ?? new() : new();

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Changes the equipment; a null result changes nothing.</summary>
    public static bool ChangeEquipment(ISavedData account, Func<DelveProgress, DelveProgress?> change) => account.Change(EquipmentKey, text =>
        change(text is null ? new() : JsonSerializer.Deserialize<DelveProgress>(text, Json) ?? new()) is { } next ? JsonSerializer.Serialize(next, Json) : null);

    static ushort Sulphite(ISavedData character) => ushort.TryParse(character.Get(SulphiteKey), out ushort amount) ? amount : (ushort)0;

    static byte[] DelveData(IPlayerWorld world)
    {
        var equipment = Equipment(world.AccountData);
        return InstanceDelve.DelveData(Sulphite(world.CharacterData), equipment.Azurite, equipment.Flares, equipment.Dynamite, equipment.Upgrades);
    }

    /// <inheritdoc/>
    public void Load(IModuleContext module)
    {
        // The gauge and the Subterranean Chart read the character's sulphite from it, in the encampment and the mine.
        module.OnEnterMessages(player => player.AreaId.StartsWith("Delve", StringComparison.Ordinal)
            ? [("0x0228 Delve resources and equipment", DelveData(module.World(player)))] : []);
        module.OnAreaHeader(player => player.AreaId switch
        {
            "Delve_Main" => new AreaHeader(InstanceDelve.FirstMineTerrain(), [2]),
            HubId => new AreaHeader(null, [2]),
            _ => null,
        });
        module.ModifyStats(sheet =>
        {
            if (!sheet.Player.AreaId.StartsWith("Delve", StringComparison.Ordinal)) return;
            foreach (var (key, value) in DelveEquipment.Stats(Equipment(module.World(sheet.Player).AccountData)))
                if (key >= 1 && key <= StatIds.Value.Length && StatIds.Value[key - 1] is { } id) sheet.Add(id, value);
        });
        module.OnAreaGenerated(async player =>
        {
            await SpawnCrawler(module, player);
            await SpawnGenerator(module, player);
        });
        module.OnObjectUsed(async use =>
        {
            if (!_states.TryGetValue(use.Player, out var state)) return false;
            var world = module.World(use.Player);
            if (state.Generator != 0 && use.ObjectId == state.Generator)
            {
                if (await world.WalkUpTo(GeneratorAt.X, GeneratorAt.Y, ObjectReach, use.RequestedAt))
                {
                    state.GeneratorOpen = true;
                    await use.Player.Send(DelveData(world), "Voltaxic Generator resources");
                    await use.Player.Send(InstanceWaypoints.OpenUi(InstanceDelve.UpgradesUi, 1), "Open Delve equipment upgrades");
                }
                return true;
            }
            if (state.Crawler != 0 && use.ObjectId == state.Crawler)
            {
                // Walk there, then the Subterranean Chart (S→C 0x7b kind 0x1c with 1).
                if (await world.WalkUpTo(state.CrawlerCell.X, state.CrawlerCell.Y, ObjectReach, use.RequestedAt))
                    await use.Player.Send(InstanceWaypoints.OpenUi(InstanceNpcs.DelveMapUi, 1), "0x007b open the Delve chart (Crawler)");
                return true;
            }
            return false;
        });
        module.OnNpcOption(async talk =>
        {
            // Text comes first, as for any NPC: these are the options with only a script.
            if (talk.Category != "Quests" || talk.HasText) return false;
            var world = module.World(talk.Player);
            // Niko's View Subterranean Chart: SendOpenScreen( DelveMapScreen, 1 ), S→C 0x7b kind 0x1c with 1.
            if (talk.Script.Contains("DelveMapScreen", StringComparison.Ordinal))
            {
                world.NpcEndTalk();
                await talk.Player.Send(InstanceWaypoints.OpenUi(InstanceNpcs.DelveMapUi, 1), "0x007b open the Delve chart");
                return true;
            }
            // Niko's Visit Mine Encampment: BeginDelve(), to the Azurite Mine Encampment, as /goto does.
            if (talk.Script.Contains("BeginDelve", StringComparison.Ordinal))
            {
                world.NpcEndTalk();
                if (!await world.TravelTo(HubId, $"{talk.Npc}: {talk.Option}"))
                    module.Log($"{talk.Option}: {HubId} is not in data/area-transitions.json");
                return true;
            }
            return false;
        });
        module.OnPacket(InstanceDelve.RouteId, async packet =>
        {
            var route = InstanceDelve.ReadRoute(packet.Bytes.ToArray());
            module.Log($"{packet.Player.Character} route requested: {string.Join(" -> ", route)}; route execution is not implemented");
            var world = module.World(packet.Player);
            if (world.PlayerReady && world.AreaHash is Hub or InstanceDelve.MineArea)
                await packet.Player.Tell("This mine route is not available yet.");
            return true;
        });
        foreach (ushort id in new[] { InstanceDelve.UpgradesRequestId, InstanceDelve.UpgradeId, InstanceDelve.PurchaseId })
            module.OnPacket(id, async packet =>
            {
                await UseEquipment(module, packet.Player, packet.Opcode, packet.Bytes.ToArray());
                return true;
            });
        // C→S 0x23f: the Delve chart (opened by Niko) asks for its data; S→C 0x240 answers (an empty chart for now).
        module.OnPacket(InstanceDelve.ChartRequestId, async packet =>
        {
            if (packet.Bytes.Length != 2) return false;
            var (a, b, cells) = _states.GetOrCreateValue(packet.Player).ChartProbe;
            await packet.Player.Send(InstanceDelve.Chart(a, b, cells), $"0x0240 Delve chart: {a}, {b}, {cells.Count} cell(s) {string.Join(" ", cells)}");
            return true;
        });
        module.OnChatCommand("delve_crawler", "/delve_crawler [x y degrees] (dev: create the Delve Crawler in the Azurite Mine Encampment, or move and turn the one there; a click on it opens the Subterranean Chart)", c => CrawlerCommand(module, c));
        module.OnChatCommand("sulphite", "/sulphite <amount 0-65535> (dev: the character's Voltaxic Sulphite, saved and sent as the Delve data 0x228)", async c =>
        {
            if (c.Args.Count != 1 || !ushort.TryParse(c.Args[0], out ushort amount)) throw new ArgumentException("/sulphite <amount 0-65535>");
            var world = module.World(c.Player);
            world.CharacterData.Change(SulphiteKey, _ => amount.ToString());
            await c.Player.Send(DelveData(world), $"0x0228 Delve data: {amount} sulphite (/sulphite)");
            await c.Player.Tell($"Sulphite: {amount}");
        });
        module.OnChatCommand("delve_chart", "/delve_chart <u32> <u16> [<u16> <u16> <u64> <u8> <u32> ...] (dev: what the next Delve chart (S→C 0x240) carries, to work out its fields)", async c =>
        {
            try
            {
                var n = c.Args.Select(a => a.StartsWith("0x") ? Convert.ToUInt64(a[2..], 16) : ulong.Parse(a)).ToArray();
                var cells = new List<InstanceDelve.Cell>();
                for (int i = 2; i + 4 < n.Length; i += 5) cells.Add(new((ushort)n[i], (ushort)n[i + 1], n[i + 2], (byte)n[i + 3], (uint)n[i + 4]));
                var state = _states.GetOrCreateValue(c.Player);
                state.ChartProbe = (n.Length > 0 ? (uint)n[0] : 0, n.Length > 1 ? (ushort)n[1] : (ushort)0, cells);
                await c.Player.Tell($"Next Delve chart: {state.ChartProbe.A}, {state.ChartProbe.B}, {cells.Count} cell(s). Open it from Niko.");
            }
            catch (FormatException) { await c.Player.Tell("Numbers only (decimal or 0x…)."); }
        });
    }

    async Task CrawlerCommand(IModuleContext module, ChatCommand c)
    {
        var world = module.World(c.Player);
        if (world.AreaHash is not (Hub or InstanceDelve.MineArea)) { await c.Player.Tell("Only in the Azurite Mine Encampment or mine"); return; }
        var state = _states.GetOrCreateValue(c.Player);
        if (c.Args.Count == 3 && int.TryParse(c.Args[0], out int x) && int.TryParse(c.Args[1], out int y) && float.TryParse(c.Args[2],
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float degrees))
        {
            if (state.Crawler == 0) await SpawnCrawler(module, c.Player);
            float radians = degrees * MathF.PI / 180;
            state.CrawlerCell = (x, y);
            await c.Player.Send(InstanceProtocol.Teleport(state.Crawler, x, y, 0x7fffffff), $"Delve Crawler to ({x}, {y})");
            await c.Player.Send(InstanceHideoutEditing.Rotation(state.Crawler, radians), $"Delve Crawler turned {degrees}°");
            foreach (uint part in state.Attachments)
                await c.Player.Send(InstanceProtocol.Teleport(part, x, y, 0x7fffffff));
            await BlockCrawler(module, c.Player, state, x, y, radians);
            await c.Player.Tell($"Delve Crawler at ({x}, {y}), {degrees}°");
            return;
        }
        await SpawnCrawler(module, c.Player);
        await c.Player.Tell($"Delve Crawler 0x{state.Crawler:x} at {state.CrawlerCell}");
    }

    async Task SpawnGenerator(IModuleContext module, IPlayer player)
    {
        var world = module.World(player);
        var state = _states.GetOrCreateValue(player);
        if (world.AreaHash != Hub || state.Generator != 0) return;
        uint id = world.NewObjectId();
        state.Generator = id;
        var (x, y) = GeneratorAt;
        await player.Send(InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(DelveGeneratorObject.MetadataPath),
            new DelveGeneratorObject(x, y, 5.12494f).Blob()), $"Voltaxic Generator 0x{id:x} at ({x}, {y})");
        await player.Send(InstanceProtocol.EnterWakeZone(id));
        world.SetBlocker(id, x, y, ObjectReach);
        uint cables = world.NewObjectId();
        await player.Send(InstanceEntities.CreateObject(cables, InstanceEntities.ObjectTypeHash(DelveGeneratorCablesObject.MetadataPath),
            new DelveGeneratorCablesObject(382, 370, 5.15258f).Blob()), "Voltaxic Generator cables");
        await player.Send(InstanceProtocol.EnterWakeZone(cables));
    }

    async Task UseEquipment(IModuleContext module, IPlayer player, ushort id, byte[] bytes)
    {
        var world = module.World(player);
        if (!_states.TryGetValue(player, out var state) || !world.PlayerReady || world.AreaHash != Hub || !state.GeneratorOpen || state.Generator == 0) return;
        if (!PlayerWalk.InWalkUpRange(player.Position, GeneratorAt, ObjectReach))
        {
            state.GeneratorOpen = false;
            return;
        }
        bool changed = false;
        if (id == InstanceDelve.UpgradeId)
        {
            int type = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2));
            changed = ChangeEquipment(world.AccountData, p => DelveEquipment.UpgradeEquipment(p, type));
        }
        else if (id == InstanceDelve.PurchaseId && bytes[2] <= 1)
            changed = ChangeEquipment(world.AccountData, p => DelveEquipment.Purchase(p, bytes[2] != 0, bytes[3]));
        await player.Send(DelveData(world), "Delve equipment state");
        if (changed) await world.RefreshStats("Delve equipment upgrade");
    }

    // The Delve Crawler in the Azurite Mine Encampment. A click on it opens the Subterranean Chart, as Niko's option does.
    async Task SpawnCrawler(IModuleContext module, IPlayer player)
    {
        var world = module.World(player);
        var state = _states.GetOrCreateValue(player);
        if (world.AreaHash is not (Hub or InstanceDelve.MineArea) || state.Crawler != 0) return;
        var (x, y) = world.AreaHash == Hub ? CrawlerAt : (world.Spawn.X, world.Spawn.Y - 18);
        uint id = world.NewObjectId();
        state.Crawler = id;
        state.CrawlerCell = (x, y);
        await player.Send(InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(DelveMapViewerObject.MetadataPath),
            new DelveMapViewerObject(x, y, CrawlerRotation).Blob()), $"0x02b9 create the Delve Crawler map viewer 0x{id:x} at ({x}, {y})");
        await player.Send(InstanceProtocol.EnterWakeZone(id), $"0x01a9 Delve Crawler 0x{id:x} enters wake zone");
        // DelveMapViewer's own transition callbacks select clips for its waypoint model, not this two-clip rig.
        // Explicitly select idle_01 (rig index 0), which loops the running engine's shake and sound.
        await player.Send(InstanceProtocol.AnimationUpdate(id, 0, 0, 1), "Crawler idle_01");
        foreach (var (model, bone, light) in new[] {
            (DelveCrawlerAttachmentObject.LeftWheel, "aux_L_wheel_attachment", false),
            (DelveCrawlerAttachmentObject.RightWheel, "aux_R_wheel_attachment", false),
            (DelveCrawlerAttachmentObject.Torch, "aux_main_tip", true) })
        {
            uint part = world.NewObjectId();
            state.Attachments.Add(part);
            string type = light ? DelveCrawlerAttachmentObject.LightType : EffectObject.TypePath;
            await player.Send(InstanceEntities.CreateObject(part, InstanceEntities.ObjectTypeHash(type),
                new DelveCrawlerAttachmentObject(x, y, id, model, bone, light).Blob()), $"Crawler attachment 0x{part:x}: {bone}");
            await player.Send(InstanceProtocol.EnterWakeZone(part));
        }
        await BlockCrawler(module, player, state, x, y, CrawlerRotation);
    }

    // The Crawler's type does not block (DelveRobot.ot: no blocking), so the player walked through it: three invisible
    // blockers (Metadata/NPC/Blocker) along its facing, and the same squares for the server's walk.
    static async Task BlockCrawler(IModuleContext module, IPlayer player, State state, int x, int y, float rotation)
    {
        var world = module.World(player);
        foreach (uint old in state.Blockers)
        {
            world.RemoveBlocker(old);
            await player.Send(InstanceProtocol.RemoveObject(old), $"0x02bb remove the Crawler blocker 0x{old:x}");
        }
        state.Blockers.Clear();
        foreach (int along in new[] { -CrawlerHalfLength, 0, CrawlerHalfLength })
        {
            int bx = x + (int)MathF.Round(along * MathF.Cos(rotation)), by = y + (int)MathF.Round(along * MathF.Sin(rotation));
            uint id = world.NewObjectId();
            state.Blockers.Add(id);
            world.SetBlocker(id, bx, by, ObjectReach);
            await player.Send(InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(WalkBlockerObject.MetadataPath),
                new WalkBlockerObject(bx, by).Blob()), $"0x02b9 create the Crawler blocker 0x{id:x} at ({bx}, {by})");
            await player.Send(InstanceProtocol.EnterWakeZone(id));
        }
        await world.PushOutOfCollision();
    }
}
