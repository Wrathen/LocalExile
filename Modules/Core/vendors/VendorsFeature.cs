using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;
using static Program;

static class VendorsFeature
{
    // C→S 0x88: finite goods move with their id. Nessa's gems are permanent shop templates: each paid purchase
    // creates a fresh inventory id, while a manager-1 update refreshes the original template in its shop cell.
    internal static async Task BuyFromShop(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, InstanceNpcs.Buy buy)
    {
        if (conn.NpcRewardOffer is not null)
        {
            if (buy.InventoryId == PlayerInventories.InventoryId(PlayerInventories.MainInventory))
                await ClaimNpcReward(remote, stream, ciphers, conn, buy.ItemId);
            return;
        }
        OpenShop? shop;
        uint tab = 0;
        InventoryItem? item = null;
        bool replenish = false;
        lock (conn.Npcs)
        {
            shop = conn.Shop;
            if (shop is not null && shop.Items.TryGetValue(buy.ItemId, out var found))
            {
                (tab, item) = found;
                replenish = NpcShops.ReplenishesGem(shop.Npc.Npc.Id, item.Item);
                if (!replenish) shop.Items.Remove(buy.ItemId);
            }
        }
        if (shop is null || item is null || PlayerInventories.Row(buy.InventoryId) is not byte row)
        {
            Console.WriteLine($"[gateway] {remote} <= 0x0088 buy {buy}: {(shop is null ? "no shop open" : item is null ? "not in the shop" : "unknown inventory")}");
            if (shop is not null && item is not null) lock (conn.Npcs) shop.Items[item.Id] = (tab, item);
            return;
        }
        var items = ItemsOf(conn);
        var stash = StashOf(conn);
        // The price: the item's price list (its first entry), paid from the main inventory and then the stash, together with
        // the item's arrival (one transaction). Not enough: nothing happens (the client shows the price in red).
        (uint Currency, int Amount)? price = item.Item.Price is [var (currency, amount), ..] ? (currency, (int)amount) : null;
        (ItemMoves.Price? Paid, InventoryItem Bought)? purchase;
        try
        {
            purchase = ItemMoves.Buy(items, stash, row, item with
            {
                Id = replenish ? items.NewItemId() : item.Id,
                X = buy.X, Y = buy.Y, Item = item.Item with { Price = [] }
            }, price);
        }
        catch
        {
            lock (conn.Npcs) shop.Items[item.Id] = (tab, item);
            throw;
        }
        if (purchase is not var (paid, bought))
        {
            lock (conn.Npcs) shop.Items[item.Id] = (tab, item);
            Console.WriteLine($"[gateway] {remote} <= 0x0088 buy item {item.Id}: costs {price!.Value.Amount} × {Items.ByHash(price.Value.Currency)?.Name ?? $"0x{price.Value.Currency:x8}"}, not enough in the inventory and the stash");
            return;
        }
        if (paid?.Inventory is { } fromInventory)
            await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.MainInventory),
                items.NextRevision(PlayerInventories.MainInventory), [.. fromInventory.Removed, .. fromInventory.Changed.Select(c => c.Id)], fromInventory.Changed),
                $"0x00bf inventory 1: paid (stacks emptied [{string.Join(", ", fromInventory.Removed)}], changed [{string.Join(", ", fromInventory.Changed.Select(c => $"{c.Id}={c.Item.StackSize}"))}])");
        await SendStashPayment(remote, stream, ciphers, conn, stash, paid?.Stash);
        await Send(remote, stream, ciphers, InstanceItems.InventoryChange(tab, shop.NextRevision(tab), [item.Id],
                replenish ? [item] : [], NpcShops.Manager),
            $"0x00bf shop tab {tab}: item {item.Id} {(replenish ? "replenished" : "sold")}");
        await Send(remote, stream, ciphers, InstanceItems.InventoryChange(buy.InventoryId, items.NextRevision(row), [], [bought]),
            $"0x00bf inventory {buy.InventoryId}: bought item {bought.Id} from {shop.Npc.Npc.Name}, offer {item.Id}, at ({buy.X}, {buy.Y}) ({buy})");
    }

    // C→S 0x8a: Ctrl + click on an item in the open shop tab: bought into the main inventory (a stack onto stacks of its type,
    // then the first free cell), the price paid from the inventory and then the stash in the same transaction
    // (ItemMoves.BuyToInventory). Without room or enough to pay, nothing happens.
    internal static async Task CtrlBuyFromShop(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, InstanceNpcs.CtrlBuy buy)
    {
        if (conn.NpcRewardOffer is not null)
        {
            await ClaimNpcReward(remote, stream, ciphers, conn, buy.ItemId);
            return;
        }
        OpenShop? shop;
        uint tab = 0;
        InventoryItem? item = null;
        bool replenish = false;
        lock (conn.Npcs)
        {
            shop = conn.Shop;
            if (shop is not null && shop.Items.TryGetValue(buy.ItemId, out var found))
            {
                (tab, item) = found;
                replenish = NpcShops.ReplenishesGem(shop.Npc.Npc.Id, item.Item);
                if (!replenish) shop.Items.Remove(buy.ItemId);
            }
        }
        if (shop is null || item is null)
        {
            Console.WriteLine($"[gateway] {remote} <= 0x008a Ctrl + click buy {buy}: {(shop is null ? "no shop open" : "not in the shop")}");
            return;
        }
        var items = ItemsOf(conn);
        var stash = StashOf(conn);
        (uint Currency, int Amount)? price = item.Item.Price is [var (currency, amount), ..] ? (currency, (int)amount) : null;
        (ItemMoves.Price? Paid, PlayerInventories.Stacking Into)? purchase;
        string why;
        try
        {
            purchase = ItemMoves.BuyToInventory(items, stash, item.Item with { Price = [] }, price, ShapeOf, out why);
        }
        catch
        {
            lock (conn.Npcs) shop.Items[item.Id] = (tab, item);
            throw;
        }
        if (purchase is not var (paid, into))
        {
            lock (conn.Npcs) shop.Items[item.Id] = (tab, item);
            Console.WriteLine($"[gateway] {remote} <= 0x008a Ctrl + click buy item {item.Id}: refused ({why})");
            return;
        }
        // The payment and the item land in the same inventory: one 0xbf with every stack that shrank, emptied, grew or arrived.
        var fromInventory = paid?.Inventory;
        var gone = new List<uint>([.. fromInventory?.Removed ?? [], .. fromInventory?.Changed.Select(c => c.Id) ?? [], .. into.Changed.Select(c => c.Id)]);
        var now = new Dictionary<uint, InventoryItem>();
        foreach (var i in (IEnumerable<InventoryItem>)[.. fromInventory?.Changed ?? [], .. into.Changed, .. into.Added]) now[i.Id] = i;
        foreach (var removed in fromInventory?.Removed ?? []) now.Remove(removed);
        await SendStashPayment(remote, stream, ciphers, conn, stash, paid?.Stash);
        await Send(remote, stream, ciphers, InstanceItems.InventoryChange(tab, shop.NextRevision(tab), [item.Id],
                replenish ? [item] : [], NpcShops.Manager),
            $"0x00bf shop tab {tab}: item {item.Id} {(replenish ? "replenished" : "sold")}");
        await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.MainInventory),
            items.NextRevision(PlayerInventories.MainInventory), [.. gone.Distinct()], [.. now.Values]),
            $"0x00bf inventory 1: bought item {item.Id} from {shop.Npc.Npc.Name} with Ctrl + click" + (price is var (_, a) ? $" for {a}" : ""));
    }

    // An item goes into the sell offer (C→S 0xb2, or 0x2a on the offer inventory) from the inventory it is in (the cursor, or
    // the main inventory on a Ctrl-click); the NPC answers with one Scroll Fragment per offered item in its own offer. As in
    // the saved item and its inventory cell remain reserved until the sale ends (PlayerInventories.Offer).
    // The client sees the item only in its offer grid.
    internal static async Task OfferForSale(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, NpcSale sale,
        uint fromInventory, uint itemId, byte x, byte y)
    {
        var items = ItemsOf(conn);
        if (PlayerInventories.Row(fromInventory) is not byte row || items.Offer(row, itemId, x, y, ShapeOf) is not var (waits, offered))
        {
            Console.WriteLine($"[gateway] {remote} sell offer: item {itemId} is not in inventory {fromInventory} (or no cell to wait in)");
            return;
        }
        // Keep the durable item in its reserved server cell, but show it only in the offer grid.
        await Send(remote, stream, ciphers, InstanceItems.InventoryChange(fromInventory, items.NextRevision(row), [offered.Id], []),
            $"0x00bf inventory {fromInventory}: hide offered item {offered.Id}");
        var inOffer = offered with { X = x, Y = y };
        await Send(remote, stream, ciphers, InstanceItems.InventoryChange(sale.Mine, ++sale.MineRevision, [], [inOffer], NpcShops.Manager),
            $"0x00bf sell offer: item {inOffer.Id} at ({x}, {y})");
        await SendSaleReward(remote, stream, ciphers, conn, sale);
    }

    // Taking an offered item back restores its original inventory view and removes its offer entry.
    internal static async Task TakeFromSale(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, NpcSale sale, uint itemId)
    {
        var items = ItemsOf(conn);
        if (items.TakeBack(itemId) is not var (row, back))
        {
            Console.WriteLine($"[gateway] {remote} sell offer item {itemId}: not offered");
            return;
        }
        await Send(remote, stream, ciphers, InstanceItems.InventoryChange(sale.Mine, ++sale.MineRevision, [itemId], [], NpcShops.Manager),
            $"0x00bf sell offer: remove item {itemId} (back in inventory {PlayerInventories.InventoryId(row)} at ({back.X}, {back.Y}))");
        await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(row), items.NextRevision(row), [], [back]),
            $"0x00bf return item {back.Id} to inventory {PlayerInventories.InventoryId(row)}");
        await SendSaleReward(remote, stream, ciphers, conn, sale);
    }

    // Quote once on each offer change. The same immutable outputs are displayed and paid at acceptance.
    internal static async Task SendSaleReward(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, NpcSale sale)
    {
        var items = ItemsOf(conn);
        var offered = items.Offered().OrderBy(i => i.Id).ToList();
        sale.QuoteError = null;
        VendorRecipes.Quote quote;
        try { quote = VendorSales.Value.Evaluate(offered,
            basicSaleMultiplier: LeagueFor(conn).Rules.VendorRewardMultiplier); }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            sale.QuoteError = ex.Message;
            quote = new([], []);
            Console.WriteLine($"[gateway] {remote} vendor quote refused: {ex.Message}");
        }
        var reward = new List<InventoryItem>();
        bool[,] occupied = new bool[NpcShops.Offer.Width, NpcShops.Offer.Height];
        foreach (var item in quote.Rewards)
        {
            var shape = ShapeOf(item) ?? new PlayerInventories.Shape(1, 1, "");
            (byte X, byte Y)? spot = null;
            for (byte y = 0; y + shape.Height <= NpcShops.Offer.Height && spot is null; y++)
                for (byte x = 0; x + shape.Width <= NpcShops.Offer.Width && spot is null; x++)
                    if (!Enumerable.Range(x, shape.Width).Any(cx => Enumerable.Range(y, shape.Height).Any(cy => occupied[cx, cy]))) spot = (x, y);
            if (spot is not { } cell) { sale.QuoteError = "The vendor reward does not fit in the offer window. Sell fewer items at once."; break; }
            for (int x = cell.X; x < cell.X + shape.Width; x++)
                for (int y = cell.Y; y < cell.Y + shape.Height; y++) occupied[x, y] = true;
            // Random recipe outputs are hidden until acceptance, matching the native vendor UI behavior since 3.13.
            var preview = item.Rarity != ItemRarity.Normal ? item with { Identified = false } : item;
            reward.Add(new(items.NewItemId(), cell.X, cell.Y, preview));
        }
        if (sale.QuoteError is not null) { reward.Clear(); await conn.Commands!.Reply(sale.QuoteError); }
        List<uint> before;
        lock (conn.Npcs)
        {
            before = [.. sale.Reward.Select(r => r.Id)];
            sale.Reward = reward;
            sale.QuotedItems = offered;
            sale.QuotedRewards = quote.Rewards;
        }
        await Send(remote, stream, ciphers, InstanceItems.InventoryChange(sale.Theirs, ++sale.TheirsRevision, before, reward, NpcShops.Manager),
            $"0x00bf {sale.Npc.Npc.Name}'s offer: {reward.Count} reward stack(s); {string.Join(", ", quote.Recipes)}");
    }

    // C→S 0xb1: Cancel (u8 0). The offered items go back to the inventory and S→C 0xb9 closes the window.
    internal static async Task AnswerSale(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, (uint Other, byte Answer) a)
    {
        Console.WriteLine($"[gateway] {remote} <= 0x00b1 trade answer {a.Answer} (trade {a.Other})");
        await CloseSale(remote, stream, ciphers, conn, accepted: a.Answer != 0);
    }

    // An item Shift-clicked in an inventory while the sell window is open goes into a free cell of the offer.
    internal static async Task OfferFromInventory(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, NpcSale sale, uint inventory, uint itemId)
    {
        var items = ItemsOf(conn);
        byte? row = PlayerInventories.Row(inventory);
        var it = row is byte r ? items.Items(r).FirstOrDefault(i => i.Id == itemId) : null;
        if (it is null || ShapeOf(it.Item) is not { } shape) { Console.WriteLine($"[gateway] {remote} sell offer: item {itemId} not in inventory {inventory}"); return; }
        List<(InventoryItem Item, PlayerInventories.Shape? Shape)> offered = [.. items.Offered().Select(o => (o, ShapeOf(o.Item)))];
        (byte X, byte Y)? cell = null;
        for (int x = 0; x + shape.Width <= NpcShops.Offer.Width && cell is null; x++)
            for (int y = 0; y + shape.Height <= NpcShops.Offer.Height && cell is null; y++)
                if (!offered.Any(o => o.Shape is { } s && o.Item.X < x + shape.Width && x < o.Item.X + s.Width && o.Item.Y < y + shape.Height && y < o.Item.Y + s.Height))
                    cell = ((byte)x, (byte)y);
        if (cell is not { } c) { Console.WriteLine($"[gateway] {remote} sell offer: no room for item {itemId}"); return; }
        await OfferForSale(remote, stream, ciphers, conn, sale, inventory, itemId, c.X, c.Y);
    }

    // Ends the sale. Accepted: the offered items are gone and the NPC's offer goes into the inventory, in one save; when the
    // offer doesn't fit (the offered items' cells free up first, so it nearly always does), nothing changes and the window
    // stays open. Cancelled: every offered item is in its cell again (it kept it). Then S→C 0xb9 closes the window.
    internal static async Task CloseSale(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, bool accepted)
    {
        NpcSale? sale;
        lock (conn.Npcs) sale = conn.Sale;
        if (sale is null) Console.WriteLine($"[gateway] {remote} trade: no sale open");
        accepted &= sale is not null;
        var items = ItemsOf(conn);
        if (accepted)
        {
            var offered = items.Offered().OrderBy(i => i.Id).ToList();
            if (offered.Count == 0) { await CloseSale(remote, stream, ciphers, conn, accepted: false); return; }
            bool sameQuote = sale!.QuotedItems.Select(i => (i.Id, i.Item)).SequenceEqual(offered.Select(i => (i.Id, i.Item)));
            PlayerInventories.Stacking? added = null;
            string? refused = sale.QuoteError ?? (!sameQuote ? "The offer has changed. Review the vendor's new offer before accepting." : null);
            if (refused is null && (added = items.Sell(sale.QuotedRewards, ShapeOf)) is null)
                refused = "Not enough room in your inventory for the complete vendor reward.";
            if (refused is not null)
            {
                Console.WriteLine($"[gateway] {remote} sale refused: {refused}");
                await conn.Commands!.Reply(refused);
                return;
            }
            Console.WriteLine($"[gateway] {remote} sale accepted: {offered.Count} item(s), {sale.QuotedRewards.Count} reward stack(s)");
            // Offered items were already removed from the client's inventory view.
            if (added is { Any: true })
                await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.MainInventory),
                    items.NextRevision(PlayerInventories.MainInventory),
                    added.RemovedIds, added.AddedItems),
                    $"0x00bf inventory 1: sold items out, {added.Changed.Count} stack(s) grown, {added.Added.Count} new (the NPC's offer)");
        }
        else if (sale is not null)
        {
            // Restore the hidden inventory view before closing the offer.
            var returned = items.CancelOffers();
            foreach (var group in returned.GroupBy(i => i.Row))
                await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(group.Key), items.NextRevision(group.Key),
                    [], [.. group.Select(i => i.Item)]), $"0x00bf sale cancelled: restore {group.Count()} item(s)");
        }
        lock (conn.Npcs) if (conn.Sale == sale) conn.Sale = null;
        await Send(remote, stream, ciphers, InstanceNpcs.TradeClose(0, accepted), $"0x00b9 sell window closed ({(accepted ? "accepted" : "cancelled")})");
        // The client is still talking to the NPC (session +0xd7c0 = 2, +0xd7c4 = the NPC, live 2026-10-19) and the player cannot
        // move until 0x84 with object 0 ends the talk. Cancel sends 0x85 for it, Accept sends nothing, so end it here.
        conn.TalkingTo = 0;
        conn.InNpcTalk = false;
        await Send(remote, stream, ciphers, InstanceNpcs.Talk(0), "0x0084 object 0: npc dialogue closed (sale over)");
    }

    // Sell Items: two empty offer inventories (manager 1: the player's offer, the NPC's), then S→C 0xb8 with no other party
    // (an NPC sale) opens the sell window. Being tested.
    internal static async Task OpenNpcSale(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, SpawnedNpc npc)
    {
        if (conn.Sale is not null) await CloseSale(remote, stream, ciphers, conn, accepted: false);
        uint mine = NpcShops.FirstTabId + 0x10, theirs = mine + 1;
        ItemsOf(conn).CancelOffers();   // none should be left; a new sale must not count them
        lock (conn.Npcs) conn.Sale = new NpcSale(npc, mine, theirs);
        await Send(remote, stream, ciphers, InstanceInventories.AddInventory(mine, NpcShops.Offer, NpcShops.Manager), $"0x00c0 sell offer {mine} (manager {NpcShops.Manager})");
        await Send(remote, stream, ciphers, InstanceInventories.AddInventory(theirs, NpcShops.Offer, NpcShops.Manager), $"0x00c0 {npc.Npc.Name}'s offer {theirs} (manager {NpcShops.Manager})");
        conn.InNpcTalk = false;
        await Send(remote, stream, ciphers, InstanceNpcs.TradeOpen(0, mine, theirs, InstanceNpcs.TradeActive | InstanceNpcs.TradeNoMouseOver, npc.Npc.Name, npc.Npc.Row),
            $"0x00b8 open the sell window with {npc.Npc.Name} (offers {mine}, {theirs})");
    }

    // Purchase Items: the NPC's stock as one shop tab (S→C 0xc0, inventory manager 1), then S→C 0x8c opens the buy window on it.
    internal static async Task OpenNpcShop(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, uint npcObject, SpawnedNpc npc)
    {
        if (conn.Sale is not null) await CloseSale(remote, stream, ciphers, conn, accepted: false);
        await RefreshBenchBalance(remote, stream, ciphers, conn, clear: true);
        conn.BenchOpen = false;
        conn.ModuleWindow = null;
        if (Items.Error is { } error) { Console.WriteLine($"[gateway] {remote} shop: {error}"); return; }
        var notes = new List<string>();
        var items = ItemsOf(conn);
        // A gem vendor's stock follows the character's completed quests, class and Siosa/Lilly (GemVendorStock), and its
        // gems cost the currency of their level-1 requirement tier. The local Nessa shop overrides progression
        // with the complete creatable assortment, including variants in ordinary mode; other vendors retain normal progression.
        var catalog = ItemsFor(conn);
        var gemVendor = GemVendorStock.Sells(npc.Npc.Id)
            ? new GemVendorStock(catalog, QuestRewardCatalog.Load(hardMode: LeagueFor(conn).HardMode, supportGems: LeagueFor(conn).Rules.SupportGemRewards), Npcs.Value, SkillsFor(conn))
            : null;
        var gemStock = gemVendor?.Stock(npc.Npc.Id, conn.ClassRow, conn.QuestFlagsSnapshot(), LeagueFor(conn).HardMode, conn.Pools.Level, LeagueFor(conn).Rules);
        var tabs = NpcShops.Tabs(npc.Npc.Id, catalog, items.NewItemId, notes, gemStock: gemStock,
            gemPrice: gemVendor is null ? null : gemVendor.Price, hardMode: LeagueFor(conn).HardMode, rules: LeagueFor(conn).Rules,
            fullGemStock: npc.Npc.Id.Equals("Metadata/NPC/Act1/Nessa", StringComparison.OrdinalIgnoreCase));
        var shopSource = Npcs.Value.TalkSource(npc.Npc);
        int shopRow = LeagueFor(conn).HardMode ? shopSource.HardModeShopRow : shopSource.ShopRow;
        foreach (var n in notes) Console.WriteLine($"[gateway] {remote} shop {npc.Npc.Name}: {n}");
        // One manager-1 inventory per tab (S→C 0xc0), then 0x8c lists them with their names.
        var ids = tabs.Select((_, i) => NpcShops.FirstTabId + (uint)i).ToList();
        lock (conn.Npcs) conn.Shop = new OpenShop(npc, tabs.SelectMany((t, i) => t.Items.Select(item => (Tab: ids[i], Item: item))).ToDictionary(p => p.Item.Id));
        for (int i = 0; i < tabs.Count; i++)
            await Send(remote, stream, ciphers, InstanceInventories.AddInventory(ids[i], NpcShops.Tab, NpcShops.Manager, tabs[i].Items),
                $"0x00c0 shop tab {ids[i]} \"{tabs[i].Name}\" (manager {NpcShops.Manager}) of {npc.Npc.Name}: {tabs[i].Items.Count} items");
        conn.InNpcTalk = false;
        // 0x8c's u8: 0 opens the window at UI +0x7b0, 1 the one at +0x7b8 (listener 0x140dcccf0); being tested live whether
        // +0x7b8 is the tabbed vendor window (with 8 tabs +0x7b0 drew its tab bar over the title and the grid below the frame).
        await Send(remote, stream, ciphers, InstanceNpcs.Dialogue(npcObject, (ushort)Math.Max(0, shopRow), tabs.Count > 1, ids,
            [.. tabs.Select((t, i) => new InstanceNpcs.Choice(i, t.Name))]),
            $"0x008c open the buy window of {npc.Npc.Name} (NPCShop row {shopRow}) with tabs {string.Join(", ", tabs.Select(t => t.Name))}");
    }

}
