using GameServer.Core.Protocol.Instance;
using GameServer.Modding;
using PlayerWalk = GameServer.Core.World.PlayerWalk;

// What modules can do with a player's world (IPlayerWorld), in the Gateway's terms.
partial class Program
{
    /// <summary>The modules a connection's league lists.</summary>
    internal static IReadOnlySet<string> ModulesFor(ClientState conn) => LeagueFor(conn).Modules;

    internal sealed class ModuleSave(Func<string, string?> get, Func<string, Func<string?, string?>, bool> change) : ISavedData
    {
        public string? Get(string key) => get(key);
        public bool Change(string key, Func<string?, string?> apply) => change(key, apply);
    }

    internal static readonly Lazy<ModuleItemCrafting> StandardCrafting = new(() => new(Items!, Rolls!));
    internal static readonly Lazy<ModuleItemCrafting> HardModeCrafting = new(() => new(HardModeItems!.Value, HardModeRolls!.Value));

    internal sealed class ModuleWorld(ClientState conn) : IPlayerWorld
    {
        Task Send(byte[] message, string? log) => Program.Send(conn.Wire.Remote, conn.Wire.Stream, conn.Wire.Ciphers, message, log);

        public ushort AreaHash => conn.Layout.Hash;
        public (int X, int Y) Spawn => (conn.Layout.SpawnX, conn.Layout.SpawnY);
        public bool PlayerReady => conn.PlayerSent;
        public uint NewObjectId() => conn.NextObjectId++;
        public void SetBlocker(uint objectId, int x, int y, int size) => conn.SetBlocker(objectId, x, y, size);
        public void RemoveBlocker(uint objectId) => conn.RemoveBlocker(objectId);
        public Task PushOutOfCollision() => Program.PushOutOfCollision(conn.Wire.Remote, conn.Wire.Stream, conn.Wire.Ciphers, conn);

        public async Task<uint?> SpawnHideoutStation(HideoutSlot slot, string metadataPath, string editorKey, string name)
        {
            if (conn.CurrentHideoutScene is not { } scene) return null;
            if (HideoutCatalog.TypeOf(metadataPath) is not var (components, _)) return null;
            if (components.Count == 0 || !HideoutObject.CanSerialize(components)) return null;
            var (x, y, rotation) = scene.Station(slot, editorKey);
            uint id = conn.NextObjectId++;
            var obj = new HideoutObject(x, y, rotation, components, new HideoutDoodad(0, 0));
            await Send(InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(metadataPath), obj.Blob()),
                $"0x02b9 create {name} 0x{id:x} at ({x}, {y})");
            await Send(InstanceProtocol.EnterWakeZone(id), null);
            await RegisterStationaryCollision(conn.Wire.Remote, conn.Wire.Stream, conn.Wire.Ciphers, conn, id, metadataPath, x, y, rotation, editorKey);
            return id;
        }

        public (int X, int Y)? StationAt(uint objectId) => conn.HideoutEditor.PositionOf(objectId) is { } p ? (p.X, p.Y) : null;

        public async Task<bool> WalkUpTo(int x, int y, int reach, double requestedAt)
        {
            if (!TryBeginObjectWalk(conn, requestedAt, x, y, reach, out var walk)) return false;
            if (walk.Arrive > walk.Now) await Task.Delay(TimeSpan.FromSeconds(walk.Arrive - walk.Now));
            return !conn.Closed && PlayerActionCurrent(conn, walk.Action) && PlayerWalk.InWalkUpRange(walk.Stop, (x, y), reach);
        }

        public Task<bool> WalkUpToStation(uint objectId, double requestedAt) => StationAt(objectId) is { } at
            ? WalkUpTo(at.X, at.Y, (int)conn.StationaryInteractionSize(objectId), requestedAt) : Task.FromResult(false);

        public async Task OpenWindow(string owner, byte ui, string log)
        {
            conn.BenchOpen = false;
            conn.ModuleWindow = owner;
            await RefreshBenchBalance(conn.Wire.Remote, conn.Wire.Stream, conn.Wire.Ciphers, conn, clear: true);
            await Send(InstanceWaypoints.OpenUi(ui), log);
        }

        public string? OpenWindowOwner => conn.ModuleWindow;

        public Task RefreshStats(string reason) => SendCharacterStats(conn.Wire.Remote, conn.Wire.Stream, conn.Wire.Ciphers, conn, reason);

        public async Task<bool> TravelTo(string areaId, string reason)
        {
            if (AreaTransitionCatalog.HashOf(areaId) is not { } hash) return false;
            await ChangeArea(conn.Wire.Remote, conn.Wire.Stream, conn.Wire.Ciphers, conn, new PendingTransfer(hash, 0, false, AtSpawn: true), reason);
            return true;
        }

        public ISavedData AccountData => new ModuleSave(key => PersonalStashOf(conn).ModuleData(key), (key, change) => PersonalStashOf(conn).ChangeModuleData(key, change));

        public ISavedData CharacterData => new ModuleSave(key => CharacterSaves.ModuleData(CharacterName(conn), key),
            (key, change) => CharacterSaves.ChangeModuleData(CharacterName(conn), key, change));

        public IItemCrafting Crafting => LeagueFor(conn).HardMode ? HardModeCrafting.Value : StandardCrafting.Value;

        SpawnedNpc? TalkingTo()
        {
            lock (conn.Npcs) return conn.Npcs.GetValueOrDefault(conn.TalkingTo);
        }

        public Task NpcSay(NpcOption option)
        {
            conn.InNpcTalk = true;
            return Send(InstanceNpcs.Talk(conn.TalkingTo, (byte)option.Index), $"0x0084 npc 0x{conn.TalkingTo:x} {option.Npc} talks: NPCTalk row {option.Row} \"{option.Option}\" (index {option.Index})");
        }

        public void NpcEndTalk() => conn.InNpcTalk = false;

        public Task NpcClose(string log)
        {
            conn.InNpcTalk = false;
            conn.TalkingTo = 0;
            return Send(InstanceNpcs.Talk(0), log);
        }

        public Task NpcIdentifyItems() => TalkingTo() is { } npc ? IdentifyInventory(conn.Wire.Remote, conn.Wire.Stream, conn.Wire.Ciphers, conn, npc) : Task.CompletedTask;
        public Task<bool> NpcOfferReward(int offer) => OfferNpcReward(conn, offer);

        public uint ItemIdIn(byte row) => ItemsOf(conn).Items(row).FirstOrDefault()?.Id ?? 0;

        public async Task<InventoryItem?> CraftInSlot(byte row, Func<Item, CraftResult?> craft, string log)
        {
            var items = ItemsOf(conn);
            var stash = PersonalStashOf(conn);
            if (ItemMoves.CraftInSlot(items, stash, row, craft) is not { } result) return null;
            // Clear and refill the slot in separate changes: a client panel watching the slot sees the item replaced.
            uint inventory = PlayerInventories.InventoryId(row);
            await Send(InstanceItems.InventoryChange(inventory, items.NextRevision(row), [result.Changed.Id], []), $"{log}: remove crafted item {result.Changed.Id} from the slot");
            await Send(InstanceItems.InventoryChange(inventory, items.NextRevision(row), [], [result.Changed]), $"{log}: crafted item {result.Changed.Id}");
            foreach (var payment in result.Payments)
            {
                if (payment.Inventory is { } paid)
                    await Send(InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.MainInventory),
                        items.NextRevision(PlayerInventories.MainInventory), [.. paid.Removed, .. paid.Changed.Select(c => c.Id)], paid.Changed),
                        $"{log}: paid from inventory");
                await SendStashPayment(conn.Wire.Remote, conn.Wire.Stream, conn.Wire.Ciphers, conn, stash, payment.Stash);
            }
            return result.Changed;
        }
    }
}

/// <summary>The Gateway's item rules for one data set, as modules see them.</summary>
sealed class ModuleItemCrafting(ItemCatalog catalog, ItemRolls rolls) : IItemCrafting
{
    readonly Lazy<IReadOnlySet<string>> _modTypes = new(() =>
        catalog.Mods.All.Select(m => m.ModType).Where(t => t.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase));

    public uint? TypeHash(string id) => catalog.Find(id)?.Hash;
    public IReadOnlySet<string> ModTypes => _modTypes.Value;

    public bool HasExplicitStat(Item item, IReadOnlyCollection<string> stats) => item.Mods.Where(m => m.Section == ModSection.Explicit)
        .Any(m => catalog.Mods.ByHash(m.Hash)?.Stats.Any(s => stats.Contains(s.Id)) == true);

    public Item? RollRequiredTag(Item item, ItemRarity rarity, Random rng, string tag) => rolls.RollRequiredTag(item, rarity, rng, tag);
    public Item? RollAffinity(Item item, ItemRarity rarity, Random rng, int percent) => rolls.RollAffinity(item, rarity, rng, percent);
    public Item? Annul(Item item, Random rng) => rolls.Annul(item, rng);
    public (int Prefix, int Suffix) RareMaxAffixes(Item item) => rolls.RareMaxAffixes(item);
    public Item? AddTaggedAffix(Item item, (int Prefix, int Suffix) max, Random rng, string tag) => rolls.AddTaggedAffix(item, max, rng, tag);

    public Item? ConvertMod(Item item, IReadOnlyDictionary<string, string> map, Random rng)
    {
        var fractured = item.FracturedIndexes();
        var candidates = Enumerable.Range(0, item.Mods.Count).Where(i => item.Mods[i].Section == ModSection.Explicit
            && !fractured.Contains(i) && catalog.Mods.ByHash(item.Mods[i].Hash) is { } mod && map.ContainsKey(mod.ModType)).ToList();
        if (candidates.Count == 0) return null;
        int at = candidates[rng.Next(candidates.Count)];
        var source = catalog.Mods.ByHash(item.Mods[at].Hash)!;
        if (catalog.ByHash(item.TypeHash) is not { } type) return null;
        // The target is another ModType (ColdResistance -> FireResistance): the nearest-level row of that group that
        // can roll on this base, with the same affix kind.
        var targetType = map[source.ModType];
        var target = catalog.Mods.All.Where(m => m.ModType.Equals(targetType, StringComparison.OrdinalIgnoreCase) &&
                m.GenerationType == source.GenerationType && m.Domain == source.Domain && m.Level <= item.ItemLevel &&
                !m.EssenceOnly && m.WeightFor(type.Tags) > 0)
            .OrderBy(m => Math.Abs(m.Level - source.Level)).ThenBy(m => m.Level).FirstOrDefault();
        if (target is null) return null;
        for (int i = 0; i < item.Mods.Count; i++)
            if (i != at && item.Mods[i].Section == ModSection.Explicit && catalog.Mods.ByHash(item.Mods[i].Hash) is { } other &&
                (other.Hash == target.Hash || other.Families.Intersect(target.Families).Any())) return null;
        var mods = item.Mods.ToArray();
        mods[at] = target.Roll(mods[at].Section, rng);
        return item with { Mods = mods };
    }
}
