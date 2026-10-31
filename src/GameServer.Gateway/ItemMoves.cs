using GameServer.Core.Protocol.Instance;

/// <summary>
/// Item changes that touch more than one save (the character's items and the account's stash) or one save more than
/// once (a currency item used up and the item it changed). Each runs in one transaction (<see cref="Saves"/>): it is
/// saved whole or not at all, so an item is never lost between its two places, nor counted in both. Checks come before
/// the first change; the handlers in Program.cs send the client what changed afterwards.
/// </summary>
static class ItemMoves
{
    const byte Cursor = PlayerInventories.Cursor, Main = PlayerInventories.MainInventory;

    /// <summary>An item out of a stash tab onto the empty cursor (C→S 0x28), keeping its id. Null (nothing changes) when
    /// something is held or the item is not in the tab.</summary>
    public static InventoryItem? StashToCursor(AccountStash stash, PlayerInventories items, int tab, uint itemId) => Saves.Atomically(() =>
    {
        if (items.Items(Cursor).Count > 0 || stash.Take(tab, itemId) is not { } taken) return null;
        stash.SetLastTab(tab);   // the player works in this tab: it is on screen
        return items.Put(Cursor, taken with { X = 0, Y = 0 });
    });

    /// <summary>What a Ctrl + click moved between an inventory row and a stash tab: the inventory side (the row, the item id
    /// that left or changed, and what is left of it there: null when all of it moved) and the tab side (stacks that grew,
    /// items that arrived; or the item id that left the tab and what is left of it there).</summary>
    public sealed record Transfer(byte Row, uint InventoryItemId, InventoryItem? LeftInInventory, int Tab,
        IReadOnlyList<InventoryItem> TabChanged, IReadOnlyList<InventoryItem> TabAdded,
        IReadOnlyList<InventoryItem> InventoryChanged, IReadOnlyList<InventoryItem> InventoryAdded, uint TabItemId = 0, InventoryItem? LeftInTab = null);

    /// <summary>
    /// An item out of a stash tab into the main inventory (Ctrl + click, C→S 0xe3): a stack onto the inventory's stacks of
    /// its type first, then into free cells, at most one normal inventory stack per click (all: Ctrl + right click,
    /// as much of the clicked stack as fits). What doesn't move stays in the tab; any other item into the first free
    /// cell, keeping its id. Null (nothing changes) when it is not there, its type is unknown or nothing fits.
    /// </summary>
    public static Transfer? StashToInventory(AccountStash stash, PlayerInventories items, int tab, uint itemId,
        Func<Item, PlayerInventories.Shape?> shapeOf, bool all = false) => Saves.Atomically(() =>
    {
        if (tab < 0 || tab >= stash.Count || stash.Inventory(tab).Items.FirstOrDefault(i => i.Id == itemId) is not { } inTab
            || shapeOf(inTab.Item) is not { } shape) return null;
        if (shape.MaxStack > 1)
        {
            ushort requested = all ? inTab.Item.StackSize : Math.Min(inTab.Item.StackSize, shape.MaxStack);
            var stacked = items.AddStacked(Main, inTab.Item with { StackSize = requested }, shapeOf);
            int moved = requested - stacked.Left;
            if (moved == 0) return null;
            var leftInTab = moved == inTab.Item.StackSize ? null : stash.Shrink(tab, itemId, moved);
            if (leftInTab is null) stash.Take(tab, itemId);
            stash.SetLastTab(tab);
            return new Transfer(Main, 0, null, tab, [], [], stacked.Changed, stacked.Added, itemId, leftInTab);
        }
        if (items.FreeCell(Main, shape.Width, shape.Height, shapeOf) is not { } cell || stash.Take(tab, itemId) is not { } taken) return null;
        stash.SetLastTab(tab);
        var placed = items.Put(Main, taken with { X = cell.X, Y = cell.Y });
        return new Transfer(Main, 0, null, tab, [], [], [], [placed], itemId);
    });

    /// <summary>
    /// An item out of whichever inventory holds it into a stash tab (Ctrl + click: C→S 0xe0 into the shown tab, 0xe1 into
    /// the tab of its affinity): a stack onto the tab's stacks of its type first, then into free cells (a slotted tab: its
    /// own slot), and what doesn't fit stays in the inventory; any other item into the first free cell (or its slot) with a
    /// stash id. Null with the reason (nothing changes): the tab doesn't hold it, or it is full.
    /// </summary>
    public static Transfer? InventoryToStash(AccountStash stash, PlayerInventories items, int tab,
        uint itemId, Func<Item, PlayerInventories.Shape?> shapeOf, out string why)
    {
        string reason = "";
        var result = Saves.Atomically(() =>
        {
            if (tab < 0 || tab >= stash.Count) { reason = "no such tab"; return null; }
            if (items.RowOf(itemId) is not byte from || items.Items(from).FirstOrDefault(i => i.Id == itemId) is not { } item)
            { reason = "the item is not in the inventories"; return null; }
            if (shapeOf(item.Item) is not { } shape) { reason = "unknown item type"; return null; }
            if (!stash.Holds(tab, item.Item.TypeHash)) { reason = $"the {stash.TypeOf(tab)} tab doesn't hold that item"; return null; }
            if (shape.MaxStack > 1)
            {
                if (stash.AddStacked(tab, item.Item, shapeOf, out reason) is not { } stacked) return null;
                if (stacked.Left == item.Item.StackSize) { reason = "the tab is full"; return null; }
                var left = stacked.Left == 0 ? null : items.SetStackSize(from, itemId, stacked.Left);
                if (stacked.Left == 0) items.Remove(from, itemId);
                stash.SetLastTab(tab);
                return new Transfer(from, itemId, left, tab, stacked.Changed, stacked.Added, [], []);
            }
            if (stash.CellFor(tab, item.Item, shapeOf) is not { } cell) { reason = "the tab is full"; return null; }
            var removed = items.Remove(from, itemId)!;
            if (stash.Place(tab, cell.X, cell.Y, removed, shapeOf, out reason) is not var (placed, _))
                throw new InvalidOperationException($"stash tab {tab} refused item {itemId} at its free cell: {reason}");   // CellFor said it fits
            stash.SetLastTab(tab);
            return new Transfer(from, itemId, null, tab, [], [placed], [], []);
        });
        why = result is null && reason == "" ? "refused" : reason;
        return result;
    }

    /// <summary>A price paid: what it took from the main inventory's stacks and from the stash's (either may be null).</summary>
    public sealed record Price(PlayerInventories.Payment? Inventory, AccountStash.Payment? Stash);

    public sealed record BenchResult(InventoryItem Changed, IReadOnlyList<Price> Payments);

    /// <summary>Where a bench Ctrl + click (C→S 0xe9) moved an item: out of <see cref="From"/>, now <see cref="Placed"/> in
    /// <see cref="To"/>, keeping its id.</summary>
    public sealed record BenchMove(byte From, byte To, InventoryItem Placed);

    /// <summary>
    /// Ctrl + click with the crafting bench open: an item of the main inventory onto the empty bench slot, or the bench's
    /// item into the main inventory's first free cell. Null (nothing changes) when the item is not there, the slot is taken
    /// or the inventory has no room.
    /// </summary>
    public static BenchMove? MoveToBench(PlayerInventories items, uint itemId, bool intoBench,
        Func<Item, PlayerInventories.Shape?> shapeOf) => Saves.Atomically<BenchMove?>(() =>
    {
        byte from = intoBench ? Main : PlayerInventories.CraftingBench, to = intoBench ? PlayerInventories.CraftingBench : Main;
        if (items.Items(from).FirstOrDefault(i => i.Id == itemId) is not { } item || shapeOf(item.Item) is not { } shape) return null;
        var cell = intoBench ? (items.Items(to).Count == 0 ? ((byte)0, (byte)0) : null)
            : items.FreeCell(to, shape.Width, shape.Height, shapeOf);
        if (cell is null || items.Remove(from, itemId) is not { } taken) return null;
        return new(from, to, items.Put(to, taken with { X = cell.Value.X, Y = cell.Value.Y }));
    });

    /// <summary>All currencies and the crafted item commit together, including multi-currency flask crafts.</summary>
    public static BenchResult? CraftAtBench(PlayerInventories items, AccountStash stash, BenchCrafting bench,
        int recipe, Random random) => Saves.Atomically<BenchResult?>(() =>
    {
        var targets = items.Items(PlayerInventories.CraftingBench);
        if (targets.Count != 1 || bench.Apply(recipe, targets[0].Item, random) is not { } craft) return null;
        foreach (var (currency, amount) in craft.Costs)
            if (amount < 0 || (long)items.Count(Main, currency) + stash.CountOf(currency) < amount) return null;
        var payments = new List<Price>();
        foreach (var (currency, amount) in craft.Costs)
            if (amount > 0) payments.Add(Pay(items, stash, currency, amount)
                ?? throw new InvalidOperationException("Bench payment changed after validation"));
        var changed = items.Change(PlayerInventories.CraftingBench, targets[0].Id, _ => craft.Item)
            ?? throw new InvalidOperationException("Bench item disappeared after payment");
        return new(changed, payments);
    });

    public sealed record SlotCraft(InventoryItem Changed, IReadOnlyList<Price> Payments);

    /// <summary>The one item in a slot row and every cost commit together.</summary>
    public static SlotCraft? CraftInSlot(PlayerInventories items, AccountStash stash, byte row,
        Func<Item, GameServer.Modding.CraftResult?> craftIt) => Saves.Atomically<SlotCraft?>(() =>
    {
        var targets = items.Items(row);
        if (targets.Count != 1 || craftIt(targets[0].Item) is not { } craft) return null;
        foreach (var (currency, amount) in craft.Costs)
            if (amount < 0 || (long)items.Count(Main, currency) + stash.CountOf(currency) < amount) return null;
        var payments = new List<Price>();
        foreach (var (currency, amount) in craft.Costs)
            if (amount > 0) payments.Add(Pay(items, stash, currency, amount)
                ?? throw new InvalidOperationException("Craft payment changed after validation"));
        var changed = items.Change(row, targets[0].Id, _ => craft.Item)
            ?? throw new InvalidOperationException("Crafted item disappeared after payment");
        return new(changed, payments);
    });

    /// <summary>
    /// Pays <paramref name="amount"/> of a currency type: from the main inventory's stacks first, the rest from the stash's
    /// (any tab), as a vendor takes it. Null (nothing changes) when the two together hold fewer. Call it inside a
    /// transaction with what the price buys.
    /// </summary>
    static Price? Pay(PlayerInventories items, AccountStash? stash, uint currency, int amount)
    {
        int inInventory = items.Count(Main, currency), inStash = stash?.CountOf(currency) ?? 0;
        if (inInventory + inStash < amount) return null;
        int fromInventory = Math.Min(amount, inInventory);
        var paid = fromInventory > 0 ? items.Pay(Main, currency, fromInventory) : null;
        var fromStash = amount > fromInventory ? stash!.Pay(currency, amount - fromInventory) : null;
        return new Price(paid, fromStash);
    }

    /// <summary>
    /// Ctrl + click on a shop item (C→S 0x8a): the price (or nothing, when free) paid (<see cref="Pay"/>: the inventory, then
    /// the stash) and the item into the main inventory (a stack onto stacks of its type, then free cells), together. Null
    /// with the reason (nothing changes) when there is no room or the price can't be paid.
    /// </summary>
    public static (Price? Paid, PlayerInventories.Stacking Into)? BuyToInventory(PlayerInventories items, AccountStash? stash, Item item,
        (uint Currency, int Amount)? price, Func<Item, PlayerInventories.Shape?> shapeOf, out string why)
    {
        // Checked before anything is paid: the room as the inventory is now (paying first could free a cell, never take one).
        if (!items.Fits(Main, item, shapeOf)) { why = "no room in the inventory"; return null; }
        string reason = "";
        var result = Saves.Atomically<(Price?, PlayerInventories.Stacking)?>(() =>
        {
            Price? paid = null;
            if (price is var (currency, amount) && (paid = Pay(items, stash, currency, amount)) is null) { reason = "not enough to pay"; return null; }
            var into = items.AddStacked(Main, item, shapeOf);
            if (into.Left > 0) throw new InvalidOperationException("a bought item that fit before the payment didn't after it");
            return (paid, into);
        });
        why = reason;
        return result;
    }

    /// <summary>What <see cref="HeldToStash"/> did. <see cref="Merged"/>: the held stack went onto <see cref="InTab"/>
    /// (same id, larger). <see cref="One"/>: one of the held stack went into an empty cell as <see cref="InTab"/>.
    /// Otherwise the held item is <see cref="InTab"/> and <see cref="Displaced"/> (the one it was dropped on) is held now.
    /// <see cref="Held"/> is what the cursor holds after (null: nothing).</summary>
    public sealed record StashPlacement(InventoryItem InTab, InventoryItem HeldBefore, InventoryItem? Held, InventoryItem? Displaced,
        bool Merged, bool One);

    /// <summary>
    /// The held item into a stash tab at (x, y) (C→S 0x2a): onto a stack of its type with room (with <paramref name="one"/>,
    /// one of it), else with <paramref name="one"/> one of the held stack into an empty cell, else the whole item, swapping
    /// the one item it was dropped on onto the cursor. Null with the reason (nothing changes).
    /// </summary>
    public static StashPlacement? HeldToStash(AccountStash stash, PlayerInventories items, int tab, int x, int y, bool one,
        Func<Item, PlayerInventories.Shape?> shapeOf, out string why)
    {
        string reason = "";
        var result = Saves.Atomically(() =>
        {
            if (items.Items(Cursor) is not [var held, ..]) { reason = "nothing held"; return null; }
            one &= held.Item.StackSize > 1;
            if (stash.Merge(tab, x, y, held, shapeOf, one ? 1 : int.MaxValue) is var (stack, moved))
                return new StashPlacement(stack, held, items.SetStackSize(Cursor, held.Id, held.Item.StackSize - moved), null, Merged: true, One: one);
            if (one)
            {
                // Into an empty place only (a slotted tab: its slot), so nothing is swapped out.
                if (!stash.IsFree(tab, x, y, held.Item, shapeOf)) { reason = "something in the way, or the tab doesn't hold it"; return null; }
                var single = new InventoryItem(items.NewItemId(), 0, 0, held.Item with { StackSize = 1 });
                if (stash.Place(tab, x, y, single, shapeOf, out reason) is not (var put, null)) throw new InvalidOperationException($"stash tab {tab}: one item into a free place refused: {reason}");
                return new StashPlacement(put, held, items.SetStackSize(Cursor, held.Id, held.Item.StackSize - 1), null, Merged: false, One: true);
            }
            if (stash.Place(tab, x, y, held, shapeOf, out reason) is not var (placed, displaced)) return null;
            stash.SetLastTab(tab);
            items.Remove(Cursor, held.Id);
            if (displaced is not null) items.Put(Cursor, displaced);
            return new StashPlacement(placed, held, displaced, displaced, Merged: false, One: false);
        });
        why = reason;
        return result;
    }

    /// <summary>Splits <paramref name="amount"/> off a stash tab's stack onto the empty cursor (C→S 0x7c), as a new
    /// character item, at most the item's max stack (a currency slot holds more). Returns the tab's stack as it is now and
    /// the held part, or null (nothing changes).</summary>
    public static (InventoryItem? Left, InventoryItem Part)? SplitFromStash(AccountStash stash, PlayerInventories items, int tab, uint itemId, int amount,
        Func<Item, PlayerInventories.Shape?> shapeOf) =>
        Saves.Atomically<(InventoryItem?, InventoryItem)?>(() =>
        {
            if (items.Items(Cursor).Count > 0 || tab < 0 || tab >= stash.Count || amount < 1
                || stash.Inventory(tab).Items.FirstOrDefault(i => i.Id == itemId) is not { } source
                || shapeOf(source.Item) is not { } shape) return null;
            amount = Math.Min(amount, Math.Max((int)shape.MaxStack, 1));
            // The whole stack: a plain click on a special tab's item sends it this way (a currency slot, live 2026-10-20). It
            // goes onto the cursor as it is, keeping its id; a bigger slot gives a full stack and keeps the rest.
            if (amount >= source.Item.StackSize)
                return stash.Take(tab, itemId) is { } taken ? (null, items.Put(Cursor, taken with { X = 0, Y = 0 })) : null;
            if (stash.Shrink(tab, itemId, amount) is not { } left) return null;
            var part = items.Put(Cursor, PlayerInventories.Completed(new InventoryItem(items.NewItemId(), 0, 0, source.Item with { StackSize = (ushort)amount }), shape));
            return (left, part);
        });

    /// <summary>Where an item is for <see cref="UseAcross"/>: a stash tab (<paramref name="Tab"/>) or a character
    /// inventory row (<paramref name="Row"/>), and its id.</summary>
    public readonly record struct Spot(int? Tab, byte Row, uint Id);

    /// <summary>A currency item used on an item, each in a character inventory or a stash tab (C→S 0x71 naming a stash tab
    /// for either: the currency tab's crafting slot, live 2026-10-23): one leaves the currency's stack (the last one removes
    /// it) and the target becomes <paramref name="change"/>(target), together. Returns the currency's stack as it is now
    /// (null when it is gone) and the changed target, or null when either is gone (nothing changes).</summary>
    public static (InventoryItem? Left, InventoryItem Changed)? UseAcross(PlayerInventories items, AccountStash stash, Spot used, Spot target,
        Func<Item, Item> change) =>
        Saves.Atomically<(InventoryItem?, InventoryItem)?>(() =>
        {
            bool there(Spot s) => s.Tab is int t ? t >= 0 && t < stash.Count && stash.Inventory(t).Items.Any(i => i.Id == s.Id)
                : items.Items(s.Row).Any(i => i.Id == s.Id);
            if (!there(used) || !there(target)) return null;
            InventoryItem? left = null;
            if (used.Tab is int tab)
            {
                var stack = stash.Inventory(tab).Items.First(i => i.Id == used.Id);
                if (stack.Item.StackSize <= 1) { if (stash.Take(tab, used.Id) is null) return null; }
                else if ((left = stash.Shrink(tab, used.Id, 1)) is null) return null;
            }
            else if (items.ConsumeOne(used.Row, used.Id) is { } consumed) left = consumed.Left;
            else return null;
            var changed = target.Tab is int targetTab ? stash.Change(targetTab, target.Id, change) : items.Change(target.Row, target.Id, change);
            return changed is null ? null : (left, changed);
        });

    /// <summary>
    /// A purchase (C→S 0x88): the price (<paramref name="price"/>: currency type and amount, or null when free) paid from the
    /// main inventory and then the stash, and the item goes to <paramref name="row"/> at its cell, together. Null when the
    /// price can't be paid (nothing changes).
    /// </summary>
    public static (Price? Paid, InventoryItem Bought)? Buy(PlayerInventories items, AccountStash? stash, byte row, InventoryItem item,
        (uint Currency, int Amount)? price) =>
        Saves.Atomically<(Price?, InventoryItem)?>(() =>
        {
            Price? paid = null;
            if (price is var (currency, amount) && (paid = Pay(items, stash, currency, amount)) is null) return null;
            return (paid, items.Put(row, item));
        });

    /// <summary>An Orb of Regret used (C→S 0x58): one leaves its stack and the character has <paramref name="refundPoints"/>
    /// refund points, together. Null when the item is gone (nothing changes).</summary>
    public static PlayerInventories.Consumption? UseForRefundPoint(PlayerInventories items, string character, byte row, uint itemId, ushort refundPoints) =>
        Saves.Atomically(() =>
        {
            if (items.ConsumeOne(row, itemId) is not { } taken) return null;
            CharacterSaves.SetRefundPoints(character, refundPoints);
            return taken;
        });

    /// <summary>A Book of Skill used (C→S 0x58): the book is gone and the character has <paramref name="extraPoints"/>
    /// extra passive skill points, together. Null when the item is gone (nothing changes).</summary>
    public static PlayerInventories.Consumption? UseForPassivePoint(PlayerInventories items, string character, byte row, uint itemId, ushort extraPoints) =>
        Saves.Atomically(() =>
        {
            if (items.ConsumeOne(row, itemId) is not { } taken) return null;
            CharacterSaves.SetExtraPassivePoints(character, extraPoints);
            return taken;
        });

    /// <summary>A currency item used on an item (C→S 0x71): one leaves its stack and the target becomes
    /// <paramref name="change"/>(target), together. Null when either is gone (nothing changes).</summary>
    public static (PlayerInventories.Consumption Used, InventoryItem Changed)? UseOn(PlayerInventories items, byte usedRow, uint usedId,
        byte targetRow, uint targetId, Func<Item, Item> change) =>
        Saves.Atomically<(PlayerInventories.Consumption, InventoryItem)?>(() =>
        {
            if (!items.Items(targetRow).Any(i => i.Id == targetId) || items.ConsumeOne(usedRow, usedId) is not { } used) return null;
            return (used, items.Change(targetRow, targetId, change)!);
        });
}
