using GameServer.Core.Protocol.Instance;

namespace GameServer.Modding;

/// <summary>What a module can do with a player's world (<see cref="IModuleContext.World"/>): objects, walking, windows, crafting
/// and saved data. Messages go through <see cref="IPlayer.Send"/>, built with GameServer.Core's protocol, which modules may use.</summary>
public interface IPlayerWorld
{
    /// <summary>The area's <c>WorldAreas</c> HASH16.</summary>
    ushort AreaHash { get; }

    /// <summary>Where a player arrives in this area (cells).</summary>
    (int X, int Y) Spawn { get; }

    /// <summary>The player's own object exists on the client.</summary>
    bool PlayerReady { get; }

    /// <summary>A free object id in this area.</summary>
    uint NewObjectId();

    /// <summary>A square of cells around an object blocks the server's walking.</summary>
    void SetBlocker(uint objectId, int x, int y, int size);

    /// <summary>Removes <see cref="SetBlocker"/>'s square.</summary>
    void RemoveBlocker(uint objectId);

    /// <summary>Moves the player out of a blocker that was placed on them.</summary>
    Task PushOutOfCollision();

    /// <summary>Places a piece of hideout furniture in one of the scene's reserved spots, or where the character has moved it
    /// (<paramref name="editorKey"/>), with its collision, movable in the hideout editor. Null outside a hideout, or when the
    /// object type cannot be written.</summary>
    Task<uint?> SpawnHideoutStation(HideoutSlot slot, string metadataPath, string editorKey, string name);

    /// <summary>Where <see cref="SpawnHideoutStation"/>'s object stands now.</summary>
    (int X, int Y)? StationAt(uint objectId);

    /// <summary>Walks to within reach of a cell. True when the player arrived and has not started anything else.</summary>
    Task<bool> WalkUpTo(int x, int y, int reach, double requestedAt);

    /// <summary>As <see cref="WalkUpTo"/>, to a station at its interaction size.</summary>
    Task<bool> WalkUpToStation(uint objectId, double requestedAt);

    /// <summary>Opens a native window as this module's: any other window closing it clears <see cref="OpenWindowOwner"/>.</summary>
    Task OpenWindow(string owner, byte ui, string log);

    /// <summary>Who opened the window that is open now, if a module did.</summary>
    string? OpenWindowOwner { get; }

    /// <summary>Rebuilds the character's stats and sends them.</summary>
    Task RefreshStats(string reason);

    /// <summary>Moves the player to an area by its <c>WorldAreas</c> id; false when it has no known way in.</summary>
    Task<bool> TravelTo(string areaId, string reason);

    /// <summary>What this module keeps per account and league, as text under keys it chooses.</summary>
    ISavedData AccountData { get; }

    /// <summary>What this module keeps per character.</summary>
    ISavedData CharacterData { get; }

    /// <summary>The item rules of the player's data set.</summary>
    IItemCrafting Crafting { get; }

    /// <summary>The NPC dialogue plays this option's text and audio; Continue returns to the options.</summary>
    Task NpcSay(NpcOption option);

    /// <summary>Ends the dialogue state without closing the window: another screen takes over.</summary>
    void NpcEndTalk();

    /// <summary>Closes the NPC dialogue.</summary>
    Task NpcClose(string log);

    /// <summary>Identifies every unidentified item in the main inventory, free.</summary>
    Task NpcIdentifyItems();

    /// <summary>Opens a quest's reward window. False when the offer has nothing for this character, or was taken.</summary>
    Task<bool> NpcOfferReward(int offer);

    /// <summary>The id of the one item in an inventory row, 0 when there is none.</summary>
    uint ItemIdIn(byte row);

    /// <summary>Crafts the one item in an inventory row: the new item and all its costs, paid from the inventory then the stash,
    /// commit together and reach the client. Null, changing nothing, when the slot, the craft or the payment fails.</summary>
    Task<InventoryItem?> CraftInSlot(byte row, Func<Item, CraftResult?> craft, string log);
}

/// <summary>A reserved spot on every hideout's floor: there whether or not a module uses it, so the rest of the layout does not
/// change with the modules.</summary>
public enum HideoutSlot
{
    /// <summary>For something about 8 cells around.</summary>
    Large,
    /// <summary>For something about 4 cells around.</summary>
    Small,
}

/// <summary>What a craft makes and what it costs: currency type hash and amount.</summary>
public sealed record CraftResult(Item Item, IReadOnlyList<(uint Currency, int Amount)> Costs);

/// <summary>Text a module saves. The key is the module's own; use its id in it.</summary>
public interface ISavedData
{
    /// <summary>The text under a key, null when there is none.</summary>
    string? Get(string key);

    /// <summary>Changes a key's text atomically; a null result changes nothing and returns false.</summary>
    bool Change(string key, Func<string?, string?> change);
}

/// <summary>The item rules a crafting module builds on, for one data set.</summary>
public interface IItemCrafting
{
    /// <summary>A <c>BaseItemTypes</c> Id's hash, null when unknown.</summary>
    uint? TypeHash(string id);

    /// <summary>Every <c>ModType</c> name of the mods.</summary>
    IReadOnlySet<string> ModTypes { get; }

    /// <summary>Whether an explicit mod of the item has one of these stats.</summary>
    bool HasExplicitStat(Item item, IReadOnlyCollection<string> stats);

    /// <summary>A reroll that must include a mod with this <c>Tags.dat</c> tag; null when none can.</summary>
    Item? RollRequiredTag(Item item, ItemRarity rarity, Random rng, string tag);

    /// <summary>A reroll where mod types the item has are this percent as likely.</summary>
    Item? RollAffinity(Item item, ItemRarity rarity, Random rng, int percent);

    /// <summary>Removes one random explicit mod.</summary>
    Item? Annul(Item item, Random rng);

    /// <summary>How many prefixes and suffixes a rare of this base holds.</summary>
    (int Prefix, int Suffix) RareMaxAffixes(Item item);

    /// <summary>Adds a mod with this tag, within the limits.</summary>
    Item? AddTaggedAffix(Item item, (int Prefix, int Suffix) max, Random rng, string tag);

    /// <summary>Turns one random explicit mod whose <c>ModType</c> is a key of the map into the nearest-level mod of the value's type.</summary>
    Item? ConvertMod(Item item, IReadOnlyDictionary<string, string> map, Random rng);
}
