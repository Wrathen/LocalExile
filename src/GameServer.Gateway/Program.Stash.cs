using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    // The account's stash (AccountStash): every character of the account shares it. Without an account (an old Auth) the
    // character's name stands in.
    internal static AccountStash StashOf(ClientState conn) => conn.GuildStashOpen && !conn.Ssf ? GuildStashOf(conn) : PersonalStashOf(conn);

    // The personal stash (and the account's gold, kept with it), whichever stash panel is open: the account's in the
    // character's league (AccountStash.Key).
    internal static AccountStash PersonalStashOf(ClientState conn) => AccountStash.For(AccountStash.Key(conn.Account ?? CharacterName(conn), conn.League), ItemsFor(conn));

    // The guild stash: no guilds yet, so each account has its own per league. SSF refuses this storage boundary.
    internal static AccountStash GuildStashOf(ClientState conn) =>
        AccountStash.ForGuild(conn.Account ?? CharacterName(conn), ItemsFor(conn), conn.League, conn.Ssf);

    // The tabs of that stash whose contents this connection has.
    internal static HashSet<int> TabsSentOf(ClientState conn, AccountStash stash) => stash.IsGuild ? conn.GuildStashTabsSent : conn.StashTabsSent;

    // The town's Stash object (Agents/docs/protocol/18-stash.md §1) where the town has one (StashCatalog).
    internal static async Task SpawnTownStash(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        var scene = HideoutFurniture(conn);
        var at = scene is { } furnished ? new StashCatalog.Placement(furnished.Stash.X, furnished.Stash.Y, furnished.Stash.Rotation)
            : StashCatalog.In(conn.Layout.Hash) is { } town && StashCatalog.FromArrival(conn.Layout.Hash)
                ? town with { X = conn.Layout.SpawnX + town.X, Y = conn.Layout.SpawnY + town.Y } : StashCatalog.In(conn.Layout.Hash);
        if (at is null || Environment.GetEnvironmentVariable("Game_STASH") == "0") return;
        uint id = conn.NextObjectId++;
        var stash = new StashObject(at.X, at.Y, at.Rotation);
        await Send(remote, stream, ciphers, InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(StashObject.MetadataPath), stash.Blob()),
            $"0x02b9 create stash 0x{id:x} at ({at.X}, {at.Y})");
        await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(id), $"0x01a9 stash 0x{id:x} enters wake zone");
        conn.StashObject = id;
        conn.StashAt = (at.X, at.Y);
        await RegisterStationaryCollision(remote, stream, ciphers, conn, id, StashObject.MetadataPath, at.X, at.Y, at.Rotation,
            scene is not null ? "stash" : null);
        // Hideouts, Lioneye's Watch and the Mine Encampment also get a guild stash.
        if (HideoutCatalog.ByArea(conn.Layout.Hash) is null
            && conn.Layout.Hash is not (StashCatalog.DelveHub or StashCatalog.MenagerieHub or StashCatalog.SanctumHub or StashCatalog.Kingsmarch or StashCatalog.HeistHub or StashCatalog.ChayulaHub or AreaLayouts.LioneyesWatch)) return;
        var townGuild = scene is null ? StashCatalog.GuildIn(conn.Layout.Hash) : null;
        if (townGuild is { } offset && StashCatalog.FromArrival(conn.Layout.Hash))
            townGuild = offset with { X = conn.Layout.SpawnX + offset.X, Y = conn.Layout.SpawnY + offset.Y };
        var (gx, gy) = scene?.Guild ?? (townGuild is { } tg ? (tg.X, tg.Y) : conn.Layout.Near((at.X, at.Y)));
        float guildRotation = SavedHideoutPlacements(conn)?.GetValueOrDefault("guild-stash")?.Rotation ?? townGuild?.Rotation ?? at.Rotation;
        if (!conn.Ssf)
        {
            uint guild = conn.NextObjectId++;
            await Send(remote, stream, ciphers, InstanceEntities.CreateObject(guild, InstanceEntities.ObjectTypeHash(StashObject.GuildMetadataPath),
                new StashObject(gx, gy, guildRotation).Blob()), $"0x02b9 create guild stash 0x{guild:x} at ({gx}, {gy})");
            await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(guild), null);
            conn.GuildStashObject = guild;
            conn.GuildStashAt = (gx, gy);
            await RegisterStationaryCollision(remote, stream, ciphers, conn, guild, StashObject.GuildMetadataPath, gx, gy, guildRotation,
                scene is not null ? "guild-stash" : null);
        }
        // The stations of the modules this league uses. One taken away is simply not placed: its spot stays free.
        conn.CurrentHideoutScene = scene;
        if (scene is not null) await ServerHooks.HideoutReady(PlayerOf(conn));
    }

    // A click on the Stash (C→S 0x24): walk there, then S→C 0x7b kind 1 opens the stash panel. Its affinity argument selects
    // the tab carrying that affinity bit in the tab list, which only the account's last tab does (AccountStash.Records).
    internal static async Task OpenStash(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, InstanceTransfer.UseSkillOnObject use, double requestedAt)
    {
        await RefreshBenchBalance(remote, stream, ciphers, conn, clear: true);
        conn.BenchOpen = false;
        conn.ModuleWindow = null;
        if (conn.StashObject == 0) return;
        var at = conn.StashAt;
        if (!TryBeginObjectWalk(conn, requestedAt, at.X, at.Y, ClientState.StashBlockerSize, out var objectWalk)) return;
        var (action, now, here, stop, arrive) = objectWalk;
        Console.WriteLine($"[gateway] {remote} <= 0x0024 stash ({use}) from ({here.X}, {here.Y}): reached at ({stop.X}, {stop.Y}) in {arrive - now:0.00} s");
        if (arrive > now) await Task.Delay(TimeSpan.FromSeconds(arrive - now));
        if (!PlayerActionCurrent(conn, action)) return;
        conn.GuildStashOpen = false;
        var stash = StashOf(conn);
        // The panel keeps its tab while this connection lasts; only its first open here is moved to the account's last tab.
        // A second 0x7b while it is open would close it (0x140dc4db0 toggles), so the choice is made now.
        if (!conn.StashOpened)
        {
            conn.StashOpened = true;
            conn.StashForcedTab = stash.LastTab;
            conn.StashForcedUntil = conn.Clock.Elapsed.TotalSeconds + StashForcedWindow;
            await Send(remote, stream, ciphers, InstanceWaypoints.OpenUi(InstanceStash.OpenStashUi, conn.StashObject, StashSelectAffinity),
                $"0x007b open the stash on tab {stash.LastTab} \"{stash.NameOf(stash.LastTab)}\" (affinity {StashSelectAffinity})",
                still: () => PlayerActionCurrent(conn, action));
        }
        else
            await Send(remote, stream, ciphers, InstanceWaypoints.OpenUi(InstanceStash.OpenStashUi, conn.StashObject, InstanceStash.NoAffinity),
                "0x007b open the stash on the panel's own tab", still: () => PlayerActionCurrent(conn, action));
    }

    // C→S 0xc7: the panel shows a tab whose contents it does not have (or it just opened on it). The first time per
    // connection the tab's inventory goes out (S→C 0xc0, manager 0, row 26), then S→C 0xc8 marks the tab loaded and the panel
    // builds its page. The tab asked for is the one on screen: the stash opens on it next time.
    internal static async Task SendStashTab(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, ushort tab, byte kind)
    {
        if (conn.Ssf && kind == InstanceStash.Guild) return;
        var stash = StashOf(conn);
        if (kind != (stash.IsGuild ? InstanceStash.Guild : InstanceStash.Personal) || tab >= stash.Count)
        {
            Console.WriteLine($"[gateway] {remote} <= 0x00c7 stash {kind} tab {tab}: no such tab");
            return;
        }
        // The panel asks for the tab it shows: as it opens (its tab before the selection) and when a tab without contents
        // is picked. The client sends its queue newest first, so during the first open the forced tab's request comes before
        // the panel's old one; that one is answered but not taken as the player's choice.
        bool forcedOpen = !stash.IsGuild && conn.StashForcedTab >= 0 && conn.Clock.Elapsed.TotalSeconds < conn.StashForcedUntil;
        bool chosen = !forcedOpen || tab == conn.StashForcedTab;
        Console.WriteLine($"[gateway] {remote} <= 0x00c7 stash tab {tab} \"{stash.NameOf(tab)}\" ({stash.TypeOf(tab)})" +
            (chosen ? "" : $" (the panel's tab before it opened on {conn.StashForcedTab}: not kept as the last tab)"));
        if (chosen) stash.SetLastTab(tab);
        uint inventory = stash.InventoryOf(tab);
        byte manager = stash.IsGuild ? InstanceStash.GuildManager : (byte)0;
        var sent = TabsSentOf(conn, stash);
        // Every loaded tab makes each later stash open slower by its item count (the panel walks them all as it shows), so
        // when the player picks a tab the others the panel doesn't show are unloaded (0xc1), and the tab list goes out
        // again with them marked not loaded, so the panel asks for them (0xc7) when shown again. Never during the first
        // open's two requests (the shown tab would go), and never the tabs with shop currency (PreloadsForPrices).
        var unload = chosen && !forcedOpen
            ? sent.Where(o => o != tab && (stash.IsGuild || !PreloadsForPrices(stash.TabType(o), stash.Inventory(o).Items, ItemsFor(conn)))).ToList()
            : [];
        foreach (int other in unload)
        {
            sent.Remove(other);
            await Send(remote, stream, ciphers, InstanceInventories.RemoveInventory(stash.InventoryOf(other), manager),
                $"0x00c1 unload stash tab {other} inventory {stash.InventoryOf(other)} (the panel shows tab {tab})");
        }
        if (sent.Add(tab))
        {
            var (slot, items) = stash.Inventory(tab);
            await Send(remote, stream, ciphers, InstanceInventories.AddInventory(inventory, slot, manager, items),
                $"0x00c0 stash tab {tab} inventory {inventory} ({slot.Width} x {slot.Height}): {items.Count} item(s)");
        }
        if (unload.Count > 0)
        {
            var tabs = stash.Records(null, sent);
            await Send(remote, stream, ciphers, InstanceStash.TabList(tabs, stash.IsGuild ? InstanceStash.Guild : InstanceStash.Personal),
                $"0x0136 stash tab list again ({tabs.Count} tabs): {unload.Count} unloaded tab(s) marked not loaded");
        }
        await Send(remote, stream, ciphers, InstanceStash.TabLoaded(tab, inventory, stash.IsGuild ? InstanceStash.Guild : InstanceStash.Personal), $"0x00c8 stash tab {tab} loaded");
    }

    // C→S 0x28 on a stash tab: the item leaves the tab for the (empty) cursor, keeping its id; both saves in one transaction
    // (ItemMoves).
    internal static async Task PickUpFromStash(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, int tab, InstanceItems.PickUpItem pick)
    {
        var stash = StashOf(conn);
        var items = ItemsOf(conn);
        if (ItemMoves.StashToCursor(stash, items, tab, pick.ItemId) is not { } held)
        {
            Console.WriteLine($"[gateway] {remote} <= 0x0028 pick up {pick} from stash tab {tab}: refused");
            return;
        }
        await Send(remote, stream, ciphers, InstanceItems.InventoryChange(pick.InventoryId, stash.NextRevision(tab), [held.Id], [], stash.IsGuild ? InstanceStash.GuildManager : (byte)0),
            $"0x00bf stash tab {tab}: remove item {held.Id}");
        await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.Cursor),
            items.NextRevision(PlayerInventories.Cursor), [], [held]), $"0x00bf cursor: add item {held.Id} (from the stash)");
    }

    // C→S 0x2a on a stash tab: the held item goes into the tab (with a stash id, AccountStash.Place) or onto a stack of its
    // type there; the one item it was dropped on comes onto the cursor. One transaction (ItemMoves.HeldToStash).
    internal static async Task PlaceInStash(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, int tab, InstanceItems.PlaceItem place)
    {
        var stash = StashOf(conn);
        var items = ItemsOf(conn);
        if (ItemMoves.HeldToStash(stash, items, tab, (int)place.X, (int)place.Y, place.A != 0, ShapeOf, out string why) is not { } done)
        {
            Console.WriteLine($"[gateway] {remote} <= 0x002a place {place} in stash tab {tab}: refused ({why})");
            return;
        }
        var (inTab, before) = (done.InTab, done.HeldBefore);
        if (done.Merged)
            await Send(remote, stream, ciphers, InstanceItems.InventoryChange(place.InventoryId, stash.NextRevision(tab), [inTab.Id], [inTab], stash.IsGuild ? InstanceStash.GuildManager : (byte)0),
                $"0x00bf stash tab {tab}: stack {inTab.Id} now {inTab.Item.StackSize}");
        else
            await Send(remote, stream, ciphers, InstanceItems.InventoryChange(place.InventoryId, stash.NextRevision(tab), done.Displaced is null ? [] : [done.Displaced.Id], [inTab], stash.IsGuild ? InstanceStash.GuildManager : (byte)0),
                $"0x00bf stash tab {tab}: add item {inTab.Id} at ({inTab.X}, {inTab.Y})" + (done.Displaced is null ? "" : $", remove item {done.Displaced.Id}"));
        await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.Cursor),
            items.NextRevision(PlayerInventories.Cursor), [before.Id], done.Held is null ? [] : [done.Held]),
            "0x00bf cursor: " + (done.Held is null ? $"remove item {before.Id}" : done.Displaced is null ? $"stack {before.Id} now {done.Held.Item.StackSize}" : $"remove item {before.Id}, hold item {done.Held.Id}"));
    }

    // C→S 0xe0: Ctrl + click on an inventory item while a stash tab is shown: into that tab (ItemMoves.InventoryToStash: a
    // stack onto the tab's stacks, a special tab only its own items, a slotted tab into the item's slot), one transaction.
    internal static async Task MoveToStash(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, InstanceStash.MoveToTab move)
    {
        if (move.Kind != InstanceStash.Personal)
        {
            Console.WriteLine($"[gateway] {remote} <= 0x00e0 move item {move.ItemId} to stash tab {move.Tab} ({move}): refused (not the personal stash)");
            return;
        }
        await CtrlClickToStash(remote, stream, ciphers, conn, move.Tab, move.ItemId, $"0x00e0 ({move})");
    }

    // C→S 0xe1: Ctrl + click on an inventory item that has an affinity tab (currency …): into the tab the client picked for it
    // (StashTabAffinities), whichever tab is shown.
    internal static async Task MoveToAffinityTab(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, InstanceStash.MoveToAffinityTab move, string hex)
    {
        if (move.Tabs is not [var (tab, affinity), ..])
        {
            Console.WriteLine($"[gateway] {remote} <= 0x00e1 move item {move.ItemId} to its affinity tab: refused (no tab named): {hex}");
            return;
        }
        // Ctrl + right click: every stack of that type in the same inventory goes, the clicked one first; each stack is one
        // Ctrl + click (its own transaction), so a full slot stops only what doesn't fit.
        var items = ItemsOf(conn);
        if ((move.Flags & InstanceStash.MoveAllOfType) != 0 && items.RowOf(move.ItemId) is byte row
            && items.Items(row).FirstOrDefault(i => i.Id == move.ItemId) is { } clicked)
        {
            uint[] ids = [clicked.Id, .. items.Items(row).Where(i => i.Item.TypeHash == clicked.Item.TypeHash && i.Id != clicked.Id).Select(i => i.Id)];
            foreach (uint id in ids)
                await CtrlClickToStash(remote, stream, ciphers, conn, tab, id, $"0x00e1 Ctrl + right click, all {ids.Length} stacks (affinity {affinity})");
            return;
        }
        await CtrlClickToStash(remote, stream, ciphers, conn, tab, move.ItemId, $"0x00e1 (affinity {affinity})");
    }

    internal static async Task CtrlClickToStash(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, int tab, uint itemId, string why)
    {
        var stash = StashOf(conn);
        var items = ItemsOf(conn);
        var moved = ItemMoves.InventoryToStash(stash, items, tab, itemId, ShapeOf, out string refused);
        Console.WriteLine($"[gateway] {remote} <= {why}: move item {itemId} to stash tab {tab}" + (moved is null ? $": refused ({refused})" : ""));
        if (moved is not null) await SendTransfer(remote, stream, ciphers, conn, items, stash, moved, tabFirst: false);
    }

    // An item out of a stash tab into the main inventory (C→S 0xe3), a stack onto the inventory's stacks first
    // (ItemMoves.StashToInventory), one transaction.
    internal static async Task TakeFromStash(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, int tab, uint itemId, bool all)
    {
        var stash = StashOf(conn);
        var items = ItemsOf(conn);
        if (ItemMoves.StashToInventory(stash, items, tab, itemId, ShapeOf, all) is not { } moved)
        {
            Console.WriteLine($"[gateway] {remote} stash tab {tab} item {itemId}: refused (not there, unknown type or no room in the inventory)");
            return;
        }
        await SendTransfer(remote, stream, ciphers, conn, items, stash, moved, tabFirst: true);
    }

    // What a Ctrl + click moved (ItemMoves.Transfer), to the client: one 0xbf for the inventory row, one for the tab (only if
    // this connection has the tab's contents: else it gets them whole when it asks, 0xc7).
    internal static async Task SendTransfer(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, PlayerInventories items,
        AccountStash stash, ItemMoves.Transfer t, bool tabFirst)
    {
        List<uint> invGone = [.. t.InventoryChanged.Select(i => i.Id)], tabGone = [.. t.TabChanged.Select(i => i.Id)];
        List<InventoryItem> invNow = [.. t.InventoryChanged, .. t.InventoryAdded], tabNow = [.. t.TabChanged, .. t.TabAdded];
        if (t.InventoryItemId != 0) { invGone.Add(t.InventoryItemId); if (t.LeftInInventory is { } l) invNow.Add(l); }
        if (t.TabItemId != 0) { tabGone.Add(t.TabItemId); if (t.LeftInTab is { } l) tabNow.Add(l); }
        async Task Inventory()
        {
            if (invGone.Count + invNow.Count == 0) return;
            await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(t.Row), items.NextRevision(t.Row), invGone, invNow),
                $"0x00bf inventory {PlayerInventories.InventoryId(t.Row)}: remove [{string.Join(", ", invGone)}], add [{string.Join(", ", invNow.Select(i => $"{i.Id} at ({i.X}, {i.Y}) x{i.Item.StackSize}"))}]");
        }
        async Task Tab()
        {
            if (tabGone.Count + tabNow.Count == 0 || !TabsSentOf(conn, stash).Contains(t.Tab)) return;
            await Send(remote, stream, ciphers, InstanceItems.InventoryChange(stash.InventoryOf(t.Tab), stash.NextRevision(t.Tab), tabGone, tabNow, stash.IsGuild ? InstanceStash.GuildManager : (byte)0),
                $"0x00bf stash tab {t.Tab}: remove [{string.Join(", ", tabGone)}], add [{string.Join(", ", tabNow.Select(i => $"{i.Id} at ({i.X}, {i.Y}) x{i.Item.StackSize}"))}]");
        }
        if (tabFirst) { await Tab(); await Inventory(); }
        else { await Inventory(); await Tab(); }
        if (t.InventoryItemId != 0 && PlayerInventories.ShowsOnModel(t.Row)) await SendEquipment(remote, stream, ciphers, conn, items);
    }

    // C→S 0xd3: the tab settings' affinities, confirmed: saved with the tab, and the tab list again (S→C 0x136), which the
    // settings window waits for.
    // After the tab settings change a tab (0xcd, 0xce): the tab list again (S→C 0x136), as after the affinities; the
    // settings window waits for it before it closes.
    internal static async Task SendTabListAgain(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, byte kind)
    {
        if (conn.Ssf && kind == InstanceStash.Guild) return;
        var stash = kind == InstanceStash.Guild ? GuildStashOf(conn) : PersonalStashOf(conn);
        var tabs = stash.Records(null, TabsSentOf(conn, stash));
        await Send(remote, stream, ciphers, InstanceStash.TabList(tabs, kind == InstanceStash.Guild ? InstanceStash.Guild : InstanceStash.Personal),
            $"0x0136 stash {kind} tab list again ({tabs.Count} tabs, after the tab settings)");
    }

    internal static async Task SetTabAffinities(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, InstanceStash.SetAffinities set)
    {
        var stash = StashOf(conn);
        uint? now = set.Kind == InstanceStash.Personal ? stash.SetAffinities(set.Tab, set.Changes) : null;
        Console.WriteLine($"[gateway] {remote} <= 0x00d3 stash tab {set.Tab} affinities [{string.Join(", ", set.Changes.Select(c => $"{c.Affinity}={(c.On ? "on" : "off")}"))}]" +
            (now is uint bits ? $": now 0x{bits:x}" : ": refused (no such personal tab)"));
        var tabs = stash.Records(null, TabsSentOf(conn, stash));
        await Send(remote, stream, ciphers, InstanceStash.TabList(tabs), $"0x0136 stash tab list again ({tabs.Count} tabs, after the affinities)");
    }
}
