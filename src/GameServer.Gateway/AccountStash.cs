using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameServer.Core.Protocol.Instance;
using GameServer.Storage;

/// <summary>
/// An account's stash (Agents/docs/protocol/18-stash.md): its tabs and their items, shared by every character of the
/// account, the tab the stash was last on, and the account's gold (S→C 0x2a9, also shared by every character). One row
/// per account in the save database (<see cref="Saves"/>), written in a transaction after every change. A new account
/// gets <see cref="StarterTabs"/>.
/// <para>Item ids: characters number their items from 1, each on its own, so an item put into the stash gets a stash id
/// (from <see cref="FirstItemId"/>, never reused: the next id is saved) and keeps it when a character takes it out.
/// PlayerInventories does not count stash ids when it picks its next id.</para>
/// </summary>
sealed class AccountStash
{
    /// <summary>Stash item ids start here (PlayerInventories counts from 1, ground items from 0x100000).</summary>
    public const uint FirstItemId = 0x40000000;

    /// <summary>Tab i's inventory id in the player's inventory manager (the player's own are 1..117, shops 0x400…).</summary>
    public const uint FirstInventoryId = 0x800;

    public static uint InventoryId(int tab) => FirstInventoryId + (uint)tab;

    /// <summary>The guild stash's tabs and items (S→C 0x7b kind 2, 0x136 kind 1) take ranges of their own, so the client
    /// never mixes them up with the personal stash's: inventory ids from 0x900, item ids from 0x50000000.</summary>
    public const uint GuildFirstInventoryId = 0x900, GuildFirstItemId = 0x50000000;

    /// <summary>The size of each stash's item id range (personal 0x40000000…, guild 0x50000000…).</summary>
    const uint ItemIdRange = 0x10000000;

    /// <summary>This stash's tab i inventory id.</summary>
    public uint InventoryOf(int tab) => _firstInventoryId + (uint)tab;

    /// <summary>Whether this is the guild stash.</summary>
    public bool IsGuild => _firstInventoryId == GuildFirstInventoryId;

    /// <summary>A tab: name, StashType row, colour, items, and its affinities (a bitset of StashTabAffinityId rows, set in the
    /// tab settings, C→S 0xd3).</summary>
    /// <summary>A tab as saved. <paramref name="Position"/>: its button's place in the panel (C→S 0xcc), null for its index.</summary>
    public sealed record SavedTab(string Name, byte Type, byte R, byte G, byte B, List<CharacterSaves.SavedPlacement> Items, uint Affinities = 0,
        ushort? Position = null, uint Colour = 0);

    public sealed record Save(List<SavedTab> Tabs, int LastTab = 0, uint NextItemId = FirstItemId, uint Gold = 0, System.Text.Json.JsonElement? Delve = null, Dictionary<string, string>? ModuleData = null)
    {
        /// <summary>Fields this build doesn't know (a newer build wrote them): kept as they are by the next write.</summary>
        [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; init; }
    }

    /// <summary>A StashType row's grid: StorageSlots × column 4 (NormalStash 12 × 12, QuadStash 24 × 24; the special tabs'
    /// slots are cells x = slot index of a StorageSlots-wide grid, as their layouts' FirstSlotIndex).</summary>
    public static readonly Dictionary<byte, (string Name, byte Width, byte Height)> Types = new()
    {
        [0] = ("NormalStash", 12, 12), [1] = ("PremiumStash", 12, 12), [3] = ("CurrencyStash", 142, 4), [4] = ("UniqueStash", 254, 4),
        [5] = ("MapStash", 12, 6), [7] = ("QuadStash", 24, 24), [8] = ("EssenceStash", 110, 4), [9] = ("FragmentStash", 255, 9),
        [6] = ("DivinationCardStash", 255, 2), [12] = ("DelveStash", 41, 4), [13] = ("BlightStash", 83, 4),
        [14] = ("UltimatumStash", 62, 5), [15] = ("DeliriumStash", 88, 1),
        [17] = ("FlaskStash", 250, 4), [18] = ("GemStash", 250, 2), [22] = ("BreachStash", 1, 1),
    };

    /// <summary>The tabs every account starts with: two normal tabs, two premium tabs, a quad tab and the special tabs
    /// whose StashType the client supports (StashAvailabilities lists Divination, Delve, Blight and Ultimatum as not).</summary>
    static readonly SavedTab[] StarterTabs =
    [
        new("1", 0, 0x7c, 0x54, 0x36, []), new("2", 0, 0x7c, 0x54, 0x36, []), new("3", 0, 0x7c, 0x54, 0x36, []), new("4", 0, 0x7c, 0x54, 0x36, []),
        new("Premium", 1, 0x2c, 0x6e, 0xc8, []), new("Loot", 1, 0xc8, 0x8c, 0x1e, []),
        new("Quad", 7, 0x8a, 0x2b, 0xe2, []),
        new("Currency", 3, 0xd2, 0xb4, 0x28, []), new("Essence", 8, 0x3c, 0xb4, 0x50, []),
        new("Fragment", 9, 0xa0, 0x3c, 0x3c, []), new("Map", 5, 0x50, 0x82, 0xb4, []),
        new("Flask", 17, 0xb4, 0x28, 0x28, []), new("Gem", 18, 0x28, 0xa0, 0x78, []),
        new("Delve", 12, 0x8c, 0x64, 0x3c, []), new("Blight", 13, 0x5a, 0xa0, 0x3c, []),
        new("Unique", 4, 0xaf, 0x60, 0x25, []), new("Delirium", 15, 0x78, 0x78, 0x78, []), new("Breach", 22, 0x8c, 0x3c, 0xb4, []),
        new("Divination", 6, 0x3c, 0x78, 0xc8, []), new("Ultimatum", 14, 0xb4, 0x8c, 0x3c, []),
    ];

    sealed class TabState(SavedTab saved)
    {
        public SavedTab Saved = saved;
        public readonly List<InventoryItem> Items = [];
        public uint Revision;
        public (byte W, byte H) Grid => Types.TryGetValue(Saved.Type, out var t) ? (t.Width, t.Height) : ((byte)12, (byte)12);
    }

    static readonly ConcurrentDictionary<string, AccountStash> ByAccount = new(StringComparer.OrdinalIgnoreCase);
    static readonly JsonSerializerOptions Opts = CharacterSaves.Opts;

    readonly string _account;
    readonly List<TabState> _tabs = [];
    readonly Lock _lock = new();
    int _lastTab;
    uint _nextItemId = FirstItemId;
    uint _firstItemId = FirstItemId, _firstInventoryId = FirstInventoryId;
    bool OwnId(uint id) => id >= _firstItemId && id < _firstItemId + ItemIdRange;
    uint _gold;
    Dictionary<string, string> _moduleData = [];
    ModCatalog? _mods;  // to save mods by their Mods.dat Id
    Dictionary<string, JsonElement>? _unknown;  // fields of a newer build's save, written back as they are
    bool _stale;  // dropped after a failed save: the database has the stash as it is
    bool _itemsInHeader;  // loaded from a stash saved before tabs had rows: the next save writes every tab
    StashRules _rules = new();  // what each tab type holds (the catalog's)

    AccountStash(string account) => _account = account;

    /// <summary>The stash key of an account in a league: every league has its own stash and gold. Standard (and a
    /// character without a league) keeps the account's own key, so the stash it had before leagues counted stays there.</summary>
    public static string Key(string account, string? league) => GameServer.Core.Accounts.LeagueCatalog.StashKey(account, league);

    /// <summary>The account's stash, loaded from the save database or made with the starter tabs. A saved item the game
    /// data doesn't know stops the load (<see cref="UnknownSavedItemException"/>) and the save stays as it is.</summary>
    public static AccountStash For(string account, ItemCatalog catalog) => Create(account, catalog, FirstInventoryId, FirstItemId, StarterTabs);

    /// <summary>The guild stash of the account (no guilds yet: each account is its own guild), scoped to the full league.
    /// Standard retains the legacy "guild:&lt;account&gt;" key. SSF never opens or creates a guild stash.</summary>
    public static AccountStash ForGuild(string account, ItemCatalog catalog, string? league = null, bool solo = false) =>
        solo ? throw new InvalidOperationException("Guild storage is unavailable in Solo Self-Found.")
            : Create("guild:" + Key(account, league), catalog, GuildFirstInventoryId, GuildFirstItemId, GuildStarterTabs);

    /// <summary>The tabs a guild stash starts with.</summary>
    static readonly SavedTab[] GuildStarterTabs =
    [
        new("Guild 1", 1, 0x1e, 0x8c, 0x3c, []), new("Guild 2", 1, 0x1e, 0x8c, 0x3c, []), new("Guild Quad", 7, 0x8a, 0x2b, 0xe2, []),
    ];

    static AccountStash Create(string account, ItemCatalog catalog, uint firstInventoryId, uint firstItemId, SavedTab[] starter) =>
        ByAccount.GetOrAdd(account, name =>
        {
            var stash = new AccountStash(name) { _mods = catalog.Mods, _rules = catalog.Stash, _firstInventoryId = firstInventoryId, _firstItemId = firstItemId };
            var save = Load(name);
            var headers = save?.Tabs ?? [.. starter];
            // A stash saved before a starter tab existed gets it at the end: a special tab type it has none of (Delve,
            // Blight, Unique …: 2026-10-22), or a normal tab "3" / "4" (the real game starts with four).
            var added = save is null ? [] : starter.Where(s => s.Type is not (0 or 1 or 7) ? headers.All(h => h.Type != s.Type)
                : s.Type == 0 && headers.All(h => h.Type != 0 || h.Name != s.Name)).ToList();
            if (added.Count > 0) headers = [.. headers, .. added];
            for (int i = 0; i < headers.Count; i++)
            {
                var tab = new TabState(headers[i] with { Items = [] });
                // Each tab's items are a row of their own; a stash saved before that (imported) keeps them in its header,
                // and its first save gives every tab its row.
                var items = Saves.LoadStashTab<List<CharacterSaves.SavedPlacement>>(name, i, Opts);
                if (items is null && headers[i].Items.Count > 0) stash._itemsInHeader = true;
                foreach (var p in items ?? headers[i].Items)
                    tab.Items.Add(new InventoryItem(p.Id, p.X, p.Y, CharacterSaves.FromSaved(p.Item, catalog)));
                stash._tabs.Add(tab);
            }
            stash._lastTab = Math.Clamp(save?.LastTab ?? 0, 0, Math.Max(0, stash._tabs.Count - 1));
            // Never below an id handed out before (an item that left for a character keeps its stash id).
            uint used = stash._tabs.SelectMany(t => t.Items).Select(i => i.Id).Where(stash.OwnId).DefaultIfEmpty(firstItemId - 1).Max();
            stash._nextItemId = Math.Max(save?.NextItemId is uint next && stash.OwnId(next) ? next : firstItemId, used + 1);
            stash._gold = save?.Gold ?? 0;
            stash._moduleData = save?.ModuleData is { } saved ? new(saved) : [];
            // A save from before Delve was a module: its equipment becomes that module's text.
            if (save?.Delve is { } legacy) stash._moduleData.TryAdd("delve", legacy.GetRawText());
            stash._unknown = save?.Unknown;
            if (save is null) stash.Persist([.. Enumerable.Range(0, stash._tabs.Count)]);
            else if (added.Count > 0) stash.Persist([.. Enumerable.Range(stash._tabs.Count - added.Count, added.Count)]);
            else if (stash.Repair(i => catalog.ByHash(i.TypeHash) is { } t ? new PlayerInventories.Shape(t.Width, t.Height, t.Class) : null) is { Count: > 0 } repaired)
                stash.Persist([.. repaired]);
            return stash;
        });

    // A stash that can't be read throws UnreadableSaveException and stays as it is, instead of becoming the starter tabs,
    // which For would write over it.
    static Save? Load(string account) => Saves.Load<Save>(SaveDatabase.Table.Stashes, account, Opts);

    /// <summary>Saves the stash's header (tabs, last tab, next item id, gold: small) and the items of the tabs that
    /// changed, in one transaction: a click in one tab writes that tab, not the whole stash.</summary>
    void Persist(params int[] changedTabs) => Saves.Atomically(() =>
    {
        if (_stale) throw new InvalidOperationException($"stash of {_account}: changed on a copy dropped after a failed save");
        // A failed save leaves this copy ahead of the database: drop it, the next use loads the stash as saved.
        Saves.Db.OnRollback(() =>
        {
            _stale = true;
            ByAccount.TryRemove(new KeyValuePair<string, AccountStash>(_account, this));
        });
        // Copied under the stash's lock, inside the transaction: an older copy never lands after a newer one.
        Save header;
        List<(int Tab, string Items)> tabs;
        lock (_lock)
        {
            header = new Save([.. _tabs.Select(t => t.Saved with { Items = [] })], _lastTab, _nextItemId, _gold, null, new(_moduleData)) { Unknown = _unknown };
            var write = _itemsInHeader ? Enumerable.Range(0, _tabs.Count) : changedTabs.Distinct();
            tabs = [.. write.Select(i => (i, JsonSerializer.Serialize(
                _tabs[i].Items.Select(it => new CharacterSaves.SavedPlacement(it.Id, it.X, it.Y, CharacterSaves.ToSaved(it.Item, _mods))).ToList(), Opts)))];
            _itemsInHeader = false;   // a rollback drops this whole copy
        }
        Saves.Put(SaveDatabase.Table.Stashes, _account, JsonSerializer.Serialize(header, Opts));
        foreach (var (tab, items) in tabs) Saves.PutStashTab(_account, tab, items);
    });

    public int Count { get { lock (_lock) return _tabs.Count; } }

    /// <summary>The account's gold (S→C 0x2a9).</summary>
    public uint Gold { get { lock (_lock) return _gold; } }


    /// <summary>A module's saved text (IPlayerWorld.AccountData).</summary>
    public string? ModuleData(string key) { lock (_lock) return _moduleData.GetValueOrDefault(key); }

    /// <summary>Changes a module's saved text; a null result changes nothing.</summary>
    public bool ChangeModuleData(string key, Func<string?, string?> change) => Saves.Atomically(() =>
    {
        lock (_lock)
        {
            if (change(_moduleData.GetValueOrDefault(key)) is not { } next) return false;
            _moduleData[key] = next;
        }
        Persist();
        return true;
    });

    /// <summary>Adds gold (or takes it, with a negative amount, down to 0); the account's gold after.</summary>
    public uint AddGold(long delta)
    {
        uint gold;
        lock (_lock) gold = _gold = (uint)Math.Clamp(_gold + delta, 0, uint.MaxValue);
        Persist();
        return gold;
    }

    /// <summary>Takes <paramref name="cost"/> gold if the account has that much; the account's gold after, or null when
    /// it has less (nothing is taken).</summary>
    public uint? TrySpendGold(long cost)
    {
        uint gold;
        lock (_lock)
        {
            if (cost < 0 || cost > _gold) return null;
            gold = _gold -= (uint)cost;
        }
        if (cost > 0) Persist();
        return gold;
    }

    /// <summary>The tab the stash opens on: the last one the client showed (C→S 0xc7).</summary>
    public int LastTab { get { lock (_lock) return _lastTab; } }

    public void SetLastTab(int tab)
    {
        lock (_lock)
        {
            if (tab < 0 || tab >= _tabs.Count || tab == _lastTab) return;
            _lastTab = tab;
        }
        Persist();
    }

    /// <summary>Turns tab affinities on or off (the tab settings' check mark, C→S 0xd3; rows of StashTabAffinityId, up to
    /// 31). Returns the tab's affinities after, or null when there is no such tab.</summary>
    public uint? SetAffinities(int tab, IEnumerable<(byte Affinity, bool On)> changes)
    {
        uint now;
        lock (_lock)
        {
            if (tab < 0 || tab >= _tabs.Count) return null;
            now = _tabs[tab].Saved.Affinities;
            foreach (var (affinity, on) in changes.Where(c => c.Affinity is > 0 and < 32))
                now = on ? now | 1u << affinity : now & ~(1u << affinity);
            if (now == _tabs[tab].Saved.Affinities) return now;
            _tabs[tab].Saved = _tabs[tab].Saved with { Affinities = now };
        }
        Persist();
        return now;
    }

    /// <summary>Keeps the tab buttons' order the panel sends (C→S 0xcc: tab index → position). Returns how many tabs moved.</summary>
    public int SetPositions(IEnumerable<(ushort Index, ushort Position)> order)
    {
        int moved = 0;
        lock (_lock)
            foreach (var (index, position) in order)
            {
                if (index >= _tabs.Count || (_tabs[index].Saved.Position ?? index) == position) continue;
                _tabs[index].Saved = _tabs[index].Saved with { Position = position };
                moved++;
            }
        if (moved > 0) Persist();
        return moved;
    }

    /// <summary>Every tab but the free normal ones (StashType 0) and folders (16) is premium, as bought tabs are in the real
    /// game: the tab settings then offer the name and colour (record flag 0x02).</summary>
    static bool IsPremium(byte type) => type is not (0 or 16);

    /// <summary>Renames a premium tab (C→S 0xcd). False for no such tab, a tab that is not premium or an empty name.</summary>
    public bool Rename(int tab, string name)
    {
        name = name.Trim();
        if (name.Length is 0 or > 64) return false;
        lock (_lock)
        {
            if (tab < 0 || tab >= _tabs.Count || !IsPremium(_tabs[tab].Saved.Type)) return false;
            if (_tabs[tab].Saved.Name == name) return true;
            _tabs[tab].Saved = _tabs[tab].Saved with { Name = name };
        }
        Persist();
        return true;
    }

    /// <summary>Sets a premium tab's colour (C→S 0xce, the u32 the client keeps at the tab record's +0x2c). False for no
    /// such tab or a tab that is not premium.</summary>
    public bool SetColour(int tab, uint colour)
    {
        lock (_lock)
        {
            if (tab < 0 || tab >= _tabs.Count || !IsPremium(_tabs[tab].Saved.Type)) return false;
            if (_tabs[tab].Saved.Colour == colour) return true;
            _tabs[tab].Saved = _tabs[tab].Saved with { Colour = colour };
        }
        Persist();
        return true;
    }

    /// <summary>The tab of a stash inventory id, or null.</summary>
    public int? TabOf(uint inventoryId)
    {
        lock (_lock)
            return inventoryId >= _firstInventoryId && inventoryId < _firstInventoryId + _tabs.Count ? (int)(inventoryId - _firstInventoryId) : null;
    }

    /// <summary>The tab list for S→C 0x21 / 0x136. The last tab carries affinity bit <paramref name="selectBit"/> (if
    /// any): S→C 0x7b kind 1 with that affinity makes the panel select it. Loaded tabs are the ones this connection
    /// already has the inventory of.</summary>
    public List<InstanceStash.Tab> Records(int? selectBit, ISet<int>? loaded = null)
    {
        lock (_lock)
            return [.. _tabs.Select((t, i) => new InstanceStash.Tab((ushort)i, t.Saved.Name, InventoryOf(i), t.Saved.Type,
                t.Saved.R, t.Saved.G, t.Saved.B,
                Flags: IsPremium(t.Saved.Type) ? InstanceStash.TabFlags.Premium : InstanceStash.TabFlags.None,
                Position: t.Saved.Position ?? (ushort)i, Affinities: t.Saved.Affinities | (selectBit is int b && i == _lastTab ? 1u << b : 0),
                Loaded: loaded?.Contains(i) ?? false, Id: $"{i + 1:x8}", U2c: t.Saved.Colour))];
    }

    public string NameOf(int tab) { lock (_lock) return tab >= 0 && tab < _tabs.Count ? _tabs[tab].Saved.Name : "?"; }

    public string TypeOf(int tab) { lock (_lock) return tab >= 0 && tab < _tabs.Count && Types.TryGetValue(_tabs[tab].Saved.Type, out var t) ? t.Name : "?"; }

    /// <summary>The tab's inventory (S→C 0xc0, manager 0) and items.</summary>
    public (InstanceInventories.Slot Slot, IReadOnlyList<InventoryItem> Items) Inventory(int tab)
    {
        lock (_lock)
        {
            var t = _tabs[tab];
            return (InstanceStash.TabSlot(t.Grid.W, t.Grid.H), [.. t.Items]);
        }
    }

    public uint NextRevision(int tab) { lock (_lock) return ++_tabs[tab].Revision; }

    /// <summary>Takes an item out of a tab (onto the cursor); null when it is not there.</summary>
    public InventoryItem? Take(int tab, uint itemId)
    {
        InventoryItem it;
        lock (_lock)
        {
            if (tab < 0 || tab >= _tabs.Count) return null;
            var items = _tabs[tab].Items;
            int at = items.FindIndex(i => i.Id == itemId);
            if (at < 0) return null;
            it = items[at];
            items.RemoveAt(at);
        }
        Persist(tab);
        return it;
    }

    /// <summary>Whether a tab holds items of this type (<see cref="StashRules"/>: a special tab only its own kind).</summary>
    public bool Holds(int tab, uint itemType)
    {
        lock (_lock) return tab >= 0 && tab < _tabs.Count && _rules.Holds(_tabs[tab].Saved.Type, itemType);
    }

    // Where an item goes in a tab: (x, y) itself in a grid tab, its own slot (slot, 0) in a slotted one (dropped anywhere
    // on the tab, it goes there); null when the tab doesn't hold it or has no slot for it.
    (int X, int Y)? CellIn(TabState t, Item item, int x, int y, out string why)
    {
        why = "";
        if (_rules.TakesAnything(t.Saved.Type, x, y)) return (x, y);   // the currency tab's crafting slot: any item, where dropped
        if (!_rules.Holds(t.Saved.Type, item.TypeHash)) { why = $"the {TypeName(t)} tab doesn't hold that item"; return null; }
        if (!_rules.Slotted(t.Saved.Type)) return (x, y);
        if (_rules.IsOverflowSlot(t.Saved.Type, x, y)) return (x, y);   // a spare cell under the crafting slot: where dropped
        if (_rules.SlotOf(t.Saved.Type, item.TypeHash) is not int slot) { why = $"the {TypeName(t)} tab has no slot for that item"; return null; }
        return SlotCell(t, slot);
    }

    // Slot n of a slotted tab is cell (n mod width, n / width): every layout but the divination tab's fits its first row;
    // its 470 cards run on into the second row of its 255 × 2.
    static (int X, int Y) SlotCell(TabState t, int slot) => (slot % t.Grid.W, slot / t.Grid.W);

    static string TypeName(TabState t) => Types.TryGetValue(t.Saved.Type, out var n) ? n.Name : $"type {t.Saved.Type}";

    // The items covering an item of this shape at (x, y): in a slotted tab, the item in that slot.
    List<InventoryItem> InTheWay(TabState t, int x, int y, PlayerInventories.Shape shape, Func<Item, PlayerInventories.Shape?> shapeOf) =>
        _rules.Slotted(t.Saved.Type)
            ? [.. t.Items.Where(i => i.X == x && i.Y == y)]
            : [.. t.Items.Where(i => shapeOf(i.Item) is { } s && i.X < x + shape.Width && x < i.X + s.Width && i.Y < y + shape.Height && y < i.Y + s.Height)];

    // How many of an item one cell of the tab holds: a currency tab's slot more than the item's own stack (not its crafting
    // slot, which holds any item at its own stack size).
    int MaxStack(TabState t, Item item, PlayerInventories.Shape shape, int x, int y) =>
        _rules.TakesAnything(t.Saved.Type, x, y) ? shape.MaxStack : _rules.MaxStack(t.Saved.Type, item.TypeHash) ?? shape.MaxStack;

    /// <summary>The item put into a tab at (x, y) (in a slotted tab, its own slot), and the one item it was dropped on
    /// (taken out, for the cursor), or null with the reason: the tab doesn't hold it, outside the grid or more than one
    /// item in the way.</summary>
    public (InventoryItem Placed, InventoryItem? Displaced)? Place(int tab, int x, int y, InventoryItem held,
        Func<Item, PlayerInventories.Shape?> shapeOf, out string why)
    {
        (InventoryItem, InventoryItem?) result;
        lock (_lock)
        {
            why = "";
            if (tab < 0 || tab >= _tabs.Count) { why = "no such tab"; return null; }
            var t = _tabs[tab];
            if (shapeOf(held.Item) is not { } shape) { why = "unknown item type"; return null; }
            if (CellIn(t, held.Item, x, y, out why) is not var (cx, cy)) return null;
            (x, y) = (cx, cy);
            if (x < 0 || y < 0 || (!_rules.Slotted(t.Saved.Type) && (x + shape.Width > t.Grid.W || y + shape.Height > t.Grid.H))) { why = "outside the grid"; return null; }
            var inTheWay = InTheWay(t, x, y, shape, shapeOf);
            if (inTheWay.Count > 1) { why = $"{inTheWay.Count} items in the way"; return null; }
            InventoryItem? displaced = null;
            if (inTheWay.Count == 1)
            {
                t.Items.Remove(inTheWay[0]);
                displaced = inTheWay[0] with { X = 0, Y = 0 };
            }
            // A character's id (counted from 1 per character) becomes a stash id: two characters' items never share one.
            // An item keeps the id this stash gave it; one from a character or the other stash gets one of this stash's.
            var placed = held with { Id = OwnId(held.Id) ? held.Id : _nextItemId++, X = (byte)x, Y = (byte)y };
            t.Items.Add(placed);
            result = (placed, displaced);
        }
        Persist(tab);
        return result;
    }

    /// <summary>
    /// The held stack put on a stack of its type with room at (x, y) in a tab (in a slotted tab, its own slot): that stack
    /// grows by what fits (up to the tab's max for the type; at most <paramref name="most"/>) and is returned with how many
    /// moved; null (nothing changes) when there is no such stack. In a grid tab a full stack becomes its full-stack item
    /// (<see cref="PlayerInventories.Completed"/>).
    /// </summary>
    public (InventoryItem Stack, int Moved)? Merge(int tab, int x, int y, InventoryItem held, Func<Item, PlayerInventories.Shape?> shapeOf,
        int most = int.MaxValue)
    {
        (InventoryItem, int) result;
        lock (_lock)
        {
            if (tab < 0 || tab >= _tabs.Count || shapeOf(held.Item) is not { MaxStack: > 1 } shape) return null;
            var t = _tabs[tab];
            if (CellIn(t, held.Item, x, y, out _) is not var (cx, cy)) return null;
            int max = MaxStack(t, held.Item, shape, cx, cy);
            if (InTheWay(t, cx, cy, shape, shapeOf) is not [var under] || under.Item.TypeHash != held.Item.TypeHash || under.Item.StackSize >= max) return null;
            int moved = Math.Min(Math.Min(held.Item.StackSize, most), max - under.Item.StackSize);
            var bigger = under with { Item = under.Item with { StackSize = (ushort)(under.Item.StackSize + moved) } };
            var grown = _rules.Slotted(t.Saved.Type) ? bigger : PlayerInventories.Completed(bigger, shape);
            t.Items[t.Items.IndexOf(under)] = grown;
            result = (grown, moved);
        }
        Persist(tab);
        return result;
    }

    /// <summary>What <see cref="AddStacked"/> did in a tab: stacks that grew (same ids), new items, and how many did not fit.</summary>
    public sealed record Stacking(IReadOnlyList<InventoryItem> Changed, IReadOnlyList<InventoryItem> Added, int Left)
    {
        public bool Any => Changed.Count + Added.Count > 0;
    }

    /// <summary>
    /// Puts <paramref name="item"/> (a stack of <see cref="Item.StackSize"/>, or any one item) into a tab the way Ctrl +
    /// click does: a stack first onto the tab's stacks of its type with room (fullest first), then into free cells (in a
    /// slotted tab: its own slot, then the spare slots in screen order, <see cref="StashRules.OverflowSlots"/>), in one
    /// save. What does not fit is <see cref="Stacking.Left"/>; null (nothing changes) with the reason when the tab doesn't
    /// hold the item. New items get stash ids.
    /// </summary>
    public Stacking? AddStacked(int tab, Item item, Func<Item, PlayerInventories.Shape?> shapeOf, out string why)
    {
        var changed = new List<InventoryItem>();
        var added = new List<InventoryItem>();
        int left;
        lock (_lock)
        {
            why = "";
            if (tab < 0 || tab >= _tabs.Count) { why = "no such tab"; return null; }
            var t = _tabs[tab];
            if (shapeOf(item) is not { } shape) { why = "unknown item type"; return null; }
            if (CellIn(t, item, 0, 0, out why) is not var (slotX, slotY)) return null;
            bool slotted = _rules.Slotted(t.Saved.Type);
            int max = Math.Max(1, MaxStack(t, item, shape, -1, -1));   // stacks into the item's own slot, never the crafting slot
            left = item.StackSize;
            // A slotted tab's cells for it, in order: its own slot, then the spare slots (left to right, top to bottom).
            List<(int X, int Y)> spots = slotted ? [(slotX, slotY), .. _rules.OverflowSlots(t.Saved.Type).Select(s => (s, 0))] : [];
            if (max > 1)
                foreach (var s in t.Items.Where(i => i.Item.TypeHash == item.TypeHash && i.Item.StackSize < max && (!slotted || spots.Contains((i.X, i.Y))))
                             .OrderBy(i => slotted ? spots.IndexOf((i.X, i.Y)) : 0).ThenByDescending(i => i.Item.StackSize).ToList())
                {
                    if (left == 0) break;
                    int take = Math.Min(left, max - s.Item.StackSize);
                    left -= take;
                    var bigger = s with { Item = s.Item with { StackSize = (ushort)(s.Item.StackSize + take) } };
                    var grown = slotted ? bigger : PlayerInventories.Completed(bigger, shape);
                    t.Items[t.Items.IndexOf(s)] = grown;
                    changed.Add(grown);
                }
            while (left > 0 && (slotted
                       ? spots.Where(p => !t.Items.Any(i => i.X == p.X && i.Y == p.Y)).Select(p => ((byte, byte)?)((byte)p.X, (byte)p.Y)).FirstOrDefault()
                       : FreeCellIn(t, shape.Width, shape.Height, shapeOf)) is { } cell)
            {
                int take = Math.Min(left, max);
                left -= take;
                var placed = new InventoryItem(_nextItemId++, cell.Item1, cell.Item2, item with { StackSize = (ushort)take });
                if (!slotted) placed = PlayerInventories.Completed(placed, shape);
                t.Items.Add(placed);
                added.Add(placed);
            }
        }
        if (changed.Count + added.Count > 0) Persist(tab);
        return new(changed, added, left);
    }

    /// <summary>What <see cref="FillSlots"/> did: grown and new stacks, how many item types had no room left in their slot
    /// and how many the server could not create.</summary>
    public sealed record SlotFill(IReadOnlyList<InventoryItem> Changed, IReadOnlyList<InventoryItem> Added, int Full, int Skipped);

    /// <summary>
    /// The /item_tab command: up to <paramref name="amount"/> more of every item type that has a slot of its own in a
    /// slotted tab (currency, essence, fragment, delve, blight, delirium, divination), each into that slot only and as far
    /// as the slot holds (<see cref="StashRules.MaxStack"/>; one of an item that doesn't stack), in one save.
    /// <paramref name="create"/> makes an item of a type, or null when the server can't write it. Null with the reason
    /// when the tab has no slots of its own.
    /// </summary>
    public SlotFill? FillSlots(int tab, int amount, Func<uint, Item?> create, Func<Item, PlayerInventories.Shape?> shapeOf, out string why)
    {
        var changed = new List<InventoryItem>();
        var added = new List<InventoryItem>();
        int full = 0, skipped = 0;
        lock (_lock)
        {
            why = "";
            if (tab < 0 || tab >= _tabs.Count) { why = "no such tab"; return null; }
            var t = _tabs[tab];
            if (StashRules.NotShown(t.Saved.Type)) { why = $"the server doesn't show items on the {TypeName(t)} tab yet"; return null; }
            if (!_rules.Slotted(t.Saved.Type))
            {
                why = $"the {TypeName(t)} tab has no slot for each item type (a currency, essence, fragment, delve, blight, delirium or divination tab has)";
                return null;
            }
            foreach (var (type, slot) in _rules.Slots(t.Saved.Type).OrderBy(s => s.Value))
            {
                // A slot past the tab's published cells would draw over the tab.
                if (slot >= t.Grid.W * t.Grid.H || create(type) is not { } item || shapeOf(item) is not { } shape) { skipped++; continue; }
                var (x, y) = SlotCell(t, slot);
                int at = t.Items.FindIndex(i => i.X == x && i.Y == y);
                // Another type in the slot (two layout rows naming one slot): leave it.
                if (at >= 0 && t.Items[at].Item.TypeHash != type) { skipped++; continue; }
                // The divination tab shows a card only once its set is complete (live 2026-10-28): fill it to its set.
                int max = shape.MaxStack <= 1 || t.Saved.Type == StashRules.Divination ? Math.Max(1, (int)shape.MaxStack) : MaxStack(t, item, shape, -1, -1);
                int room = max - (at >= 0 ? t.Items[at].Item.StackSize : 0);
                if (room <= 0) { full++; continue; }
                int take = Math.Min(amount, room);
                if (at >= 0)
                {
                    var grown = t.Items[at] with { Item = t.Items[at].Item with { StackSize = (ushort)(t.Items[at].Item.StackSize + take) } };
                    t.Items[at] = grown;
                    changed.Add(grown);
                }
                else
                {
                    var placed = new InventoryItem(_nextItemId++, (byte)x, (byte)y, item with { StackSize = (ushort)take });
                    t.Items.Add(placed);
                    added.Add(placed);
                }
            }
        }
        if (changed.Count + added.Count > 0) Persist(tab);
        return new(changed, added, full, skipped);
    }

    /// <summary>
    /// The /item_tab command on a tab without a slot per type (the gem tab): each of <paramref name="items"/> the tab
    /// holds and doesn't have yet (by <paramref name="key"/>) into the first free cell, in order, in one save.
    /// <see cref="SlotFill.Full"/> counts the ones already there, <see cref="SlotFill.Skipped"/> the ones that found no
    /// room or that the tab doesn't hold.
    /// </summary>
    public SlotFill? FillCells(int tab, IEnumerable<Item> items, Func<Item, string> key, Func<Item, PlayerInventories.Shape?> shapeOf, out string why)
    {
        var added = new List<InventoryItem>();
        int present = 0, skipped = 0;
        lock (_lock)
        {
            why = "";
            if (tab < 0 || tab >= _tabs.Count) { why = "no such tab"; return null; }
            var t = _tabs[tab];
            var have = t.Items.Select(i => key(i.Item)).ToHashSet();
            foreach (var item in items)
            {
                if (!have.Add(key(item))) { present++; continue; }
                if (!_rules.Holds(t.Saved.Type, item.TypeHash) || shapeOf(item) is not { } shape
                    || FreeCellIn(t, shape.Width, shape.Height, shapeOf) is not { } cell) { skipped++; continue; }
                var placed = PlayerInventories.Completed(new InventoryItem(_nextItemId++, cell.X, cell.Y, item), shape);
                t.Items.Add(placed);
                added.Add(placed);
            }
        }
        if (added.Count > 0) Persist(tab);
        return new([], added, present, skipped);
    }

    /// <summary>The tab's StashType row, or null for no such tab.</summary>
    public byte? TabType(int tab) { lock (_lock) return tab >= 0 && tab < _tabs.Count ? _tabs[tab].Saved.Type : null; }

    /// <summary>Replaces an item in a tab with a changed copy (same id and cell), saved; null when it is not there (currency
    /// used on an item in the stash, e.g. the currency tab's crafting slot).</summary>
    public InventoryItem? Change(int tab, uint itemId, Func<Item, Item> change)
    {
        InventoryItem result;
        lock (_lock)
        {
            if (tab < 0 || tab >= _tabs.Count) return null;
            var items = _tabs[tab].Items;
            int at = items.FindIndex(i => i.Id == itemId);
            if (at < 0) return null;
            result = items[at] = items[at] with { Item = change(items[at].Item) };
        }
        Persist(tab);
        return result;
    }

    /// <summary>Takes <paramref name="amount"/> off a stack in a tab (a split: less than the whole stack). Returns the
    /// stack as it is now, or null (nothing changes).</summary>
    public InventoryItem? Shrink(int tab, uint itemId, int amount)
    {
        InventoryItem result;
        lock (_lock)
        {
            if (tab < 0 || tab >= _tabs.Count) return null;
            var items = _tabs[tab].Items;
            int at = items.FindIndex(i => i.Id == itemId);
            if (at < 0 || amount < 1 || amount >= items[at].Item.StackSize) return null;
            result = items[at] = items[at] with { Item = items[at].Item with { StackSize = (ushort)(items[at].Item.StackSize - amount) } };
        }
        Persist(tab);
        return result;
    }

    /// <summary>The first free cell of a tab for an item of this size (column by column), or null.</summary>
    public (byte X, byte Y)? FreeCell(int tab, byte width, byte height, Func<Item, PlayerInventories.Shape?> shapeOf)
    {
        lock (_lock) return FreeCellIn(_tabs[tab], width, height, shapeOf);
    }

    static (byte X, byte Y)? FreeCellIn(TabState t, byte width, byte height, Func<Item, PlayerInventories.Shape?> shapeOf)
    {
        for (int x = 0; x + width <= t.Grid.W; x++)
            for (int y = 0; y + height <= t.Grid.H; y++)
                if (!t.Items.Any(i => shapeOf(i.Item) is { } s && i.X < x + width && x < i.X + s.Width && i.Y < y + height && y < i.Y + s.Height))
                    return ((byte)x, (byte)y);
        return null;
    }

    /// <summary>Where Ctrl + click puts an item that doesn't stack: its slot in a slotted tab when the slot is empty, else
    /// the first free cell; null when there is none or the tab doesn't hold it.</summary>
    public (byte X, byte Y)? CellFor(int tab, Item item, Func<Item, PlayerInventories.Shape?> shapeOf)
    {
        lock (_lock)
        {
            if (tab < 0 || tab >= _tabs.Count || shapeOf(item) is not { } shape) return null;
            var t = _tabs[tab];
            if (CellIn(t, item, 0, 0, out _) is not var (sx, sy)) return null;
            if (!_rules.Slotted(t.Saved.Type)) return FreeCellIn(t, shape.Width, shape.Height, shapeOf);
            return t.Items.Any(i => i.X == sx && i.Y == sy) ? null : ((byte)sx, (byte)sy);
        }
    }

    /// <summary>How many of an item type the stash holds, in every tab.</summary>
    public int CountOf(uint itemType)
    {
        lock (_lock) return _tabs.SelectMany(t => t.Items).Where(i => i.Item.TypeHash == itemType).Sum(i => (int)i.Item.StackSize);
    }

    /// <summary>What <see cref="Pay"/> took, per tab: the stacks it emptied (ids) and the ones it left smaller.</summary>
    public sealed record Payment(IReadOnlyDictionary<int, (List<uint> Removed, List<InventoryItem> Changed)> Tabs);

    /// <summary>
    /// Takes <paramref name="amount"/> of an item type from the stash's stacks, in any tab (smallest stacks first), in one
    /// save (a vendor takes its price from the stash too). Null (nothing changes) when the stash holds fewer.
    /// </summary>
    public Payment? Pay(uint itemType, int amount)
    {
        var tabs = new Dictionary<int, (List<uint> Removed, List<InventoryItem> Changed)>();
        lock (_lock)
        {
            var stacks = _tabs.SelectMany((t, tab) => t.Items.Where(i => i.Item.TypeHash == itemType).Select(i => (Tab: tab, Item: i)))
                .OrderBy(s => s.Item.Item.StackSize).ToList();
            if (stacks.Sum(s => (int)s.Item.Item.StackSize) < amount) return null;
            foreach (var (tab, s) in stacks)
            {
                if (amount <= 0) break;
                int take = Math.Min(amount, s.Item.StackSize);
                amount -= take;
                var items = _tabs[tab].Items;
                var paid = tabs.TryGetValue(tab, out var p) ? p : tabs[tab] = ([], []);
                if (take == s.Item.StackSize) { items.Remove(s); paid.Removed.Add(s.Id); }
                else paid.Changed.Add(items[items.IndexOf(s)] = s with { Item = s.Item with { StackSize = (ushort)(s.Item.StackSize - take) } });
            }
        }
        if (tabs.Count > 0) Persist([.. tabs.Keys]);
        return new Payment(tabs);
    }

    /// <summary>Whether an item dropped at (x, y) (in a slotted tab: into its slot) would land in an empty place.</summary>
    public bool IsFree(int tab, int x, int y, Item item, Func<Item, PlayerInventories.Shape?> shapeOf)
    {
        lock (_lock)
            return tab >= 0 && tab < _tabs.Count && shapeOf(item) is { } shape && CellIn(_tabs[tab], item, x, y, out _) is var (cx, cy)
                && InTheWay(_tabs[tab], cx, cy, shape, shapeOf).Count == 0;
    }

    /// <summary>The tab and the item, for an item id anywhere in the stash, or null.</summary>
    public (int Tab, InventoryItem Item)? Find(uint itemId)
    {
        lock (_lock)
            for (int tab = 0; tab < _tabs.Count; tab++)
                if (_tabs[tab].Items.FirstOrDefault(i => i.Id == itemId) is { } it) return (tab, it);
        return null;
    }

    // Items a special tab can't hold (or a slotted tab holds outside their slot) can't be seen or taken there: the client
    // only draws a special tab's own items. At load they go into their slot, else into the first normal tab with room.
    // Returns the tabs that changed.
    List<int> Repair(Func<Item, PlayerInventories.Shape?> shapeOf)
    {
        var changed = new List<int>();
        for (int tab = 0; tab < _tabs.Count; tab++)
        {
            var t = _tabs[tab];
            // Without the tables there is nothing to check a special tab against, except a tab that shows nothing.
            if (StashRules.IsGrid(t.Saved.Type) || (!_rules.Loaded && !StashRules.NotShown(t.Saved.Type))) continue;
            foreach (var it in t.Items.ToList())
            {
                if (CellIn(t, it.Item, it.X, it.Y, out _) is var (sx, sy) && (sx, sy) == (it.X, it.Y)) continue;
                t.Items.Remove(it);
                if (CellIn(t, it.Item, 0, 0, out _) is var (cx, cy) && !t.Items.Any(i => i.X == cx && i.Y == cy))
                {
                    t.Items.Add(it with { X = (byte)cx, Y = (byte)cy });
                    Console.WriteLine($"[gateway] stash of {_account}: item {it.Id} moved into its slot {cx} of tab {tab}");
                    changed.Add(tab);
                    continue;
                }
                var shape = shapeOf(it.Item) ?? new(1, 1, "");
                var home = Enumerable.Range(0, _tabs.Count).Where(i => StashRules.IsGrid(_tabs[i].Saved.Type))
                    .Select(i => (Tab: i, Cell: FreeCellIn(_tabs[i], shape.Width, shape.Height, shapeOf))).FirstOrDefault(h => h.Cell is not null);
                if (home.Cell is not { } cell)
                {
                    t.Items.Add(it);   // nowhere to put it: it stays saved where it is
                    Console.WriteLine($"[gateway] stash of {_account}: item {it.Id} can't be shown in tab {tab} and no normal tab has room for it; it stays there");
                    continue;
                }
                _tabs[home.Tab].Items.Add(it with { X = cell.X, Y = cell.Y });
                Console.WriteLine($"[gateway] stash of {_account}: item {it.Id} can't be shown in the {TypeName(t)} tab {tab}; moved to tab {home.Tab} at ({cell.X}, {cell.Y})");
                changed.Add(tab);
                changed.Add(home.Tab);
            }
        }
        return [.. changed.Distinct()];
    }
    /// <summary>A fresh stash item id.</summary>
    public uint NewItemId() { lock (_lock) return _nextItemId++; }

    /// <summary>Forgets the account's loaded stash: the next use loads it from the database (tests: a restart).</summary>
    internal static void Forget(string account) => ByAccount.TryRemove(account, out _);
}
