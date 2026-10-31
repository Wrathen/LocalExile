using GameServer.Core.Protocol.Login;

namespace GameServer.Core.Protocol.Instance;

/// <summary>Socket colours as the Sockets component stores them (u8 per socket, bits 1..7).</summary>
public enum SocketColour : byte { Red = 1, Green = 2, Blue = 3, White = 4, Abyss = 5, Delve = 6 }

/// <summary>A socket of an item, with the gem in it (if any).</summary>
public sealed record ItemSocket(SocketColour Colour, Item? Gem = null);

/// <summary>An item's rarity: bits 0-2 of the ObjectMagicProperties flags (0x142201d70 → +0x7c).</summary>
public enum ItemRarity : byte { Normal = 0, Magic = 1, Rare = 2, Unique = 3 }

/// <summary>
/// The mod lists of ObjectMagicProperties (0x142201d70), by the flag bit that announces each one. Every list is u8 count +
/// entries; <see cref="Explicit"/> is only read for identified items. The other lists are read whether identified or not.
/// Seen in the tooltip (2026-10-20): implicit under the item's properties, enchant above the implicits, explicit below,
/// scourge as orange lines among the explicits (Hellscape), hidden not shown at all.
/// </summary>
public enum ModSection : byte { Implicit = 4, Explicit = 5, Enchant = 6, Scourge = 10, Hidden = 11 }

/// <summary>An item's influences, the Base component's u8 +0xc6: one bit per InfluenceTags/InfluenceExalts Influence row, in
/// that order (the conqueror names follow InfluenceExalts). The client refuses crafting on it (0x141d57ea0 tests it for
/// zero) and draws it (0x1415703a0 reads each bit). Agents/docs/protocol/13-items.md §2.</summary>
[Flags]
public enum ItemInfluence : byte
{
    None = 0, Shaper = 1 << 0, Elder = 1 << 1, Crusader = 1 << 2, Redeemer = 1 << 3, Hunter = 1 << 4, Warlord = 1 << 5,
}

/// <summary>A mod on an item: a Mods.dat row by its HASH16 and one value per stat of that row (StatsKey1..8 in order,
/// as many as the row has). The client counts the row's stats itself, so the count must match.</summary>
public sealed record ItemMod(ModSection Section, ushort Hash, IReadOnlyList<int> Values);

/// <summary>
/// An item as the client deserializes it (Agents/docs/protocol/05-inventories-and-ui.md §6). The type is a BaseItemTypes
/// row: <see cref="TypeHash"/> is its HASH32 (MurmurHash2 of the Id, <see cref="InstanceEntities.ObjectTypeHash"/>),
/// which the client looks up in Metadata/ItemTypeRegister; <see cref="Components"/> are the type's components from its
/// .it chain (`scripts/re/ot_components.py --items`), in order. Each one reads its own data (vtable slot 5, +0x28).
/// </summary>
public sealed record Item(uint TypeHash, IReadOnlyList<string> Components, ushort VisualIdentity)
{
    public byte ItemLevel { get; init; } = 1;
    public byte Quality { get; init; }
    public const byte MaximumDefenceBaseRoll = 31;
    public const byte SpecialDefenceBaseRoll = 32;
    byte defenceBaseRoll;

    /// <summary>The Armour component's shared base-defence roll: 0..31 interpolates each defence's native range.
    /// Wire value 32 preserves the client's special minimum-base flag; it is not the maximum roll.</summary>
    public byte DefenceBaseRoll
    {
        get => defenceBaseRoll;
        init => defenceBaseRoll = value <= SpecialDefenceBaseRoll ? value
            : throw new ArgumentOutOfRangeException(nameof(DefenceBaseRoll), value, "Defence base roll must be 0..32.");
    }
    public int Charges { get; init; }
    public ushort StackSize { get; init; } = 1;
    public IReadOnlyList<ItemSocket> Sockets { get; init; } = [];

    /// <summary>Link groups: the number of sockets in each, in socket order (null: every socket alone).</summary>
    public IReadOnlyList<byte>? Links { get; init; }
    public int GemExperience { get; init; }

    /// <summary>A gem's variant: its index in the SkillGems row's GemVariants (0 the normal gem, then the transfigured,
    /// Trarthan or Vaal ones; SkillGem component +0x3c).</summary>
    public int GemVariant { get; init; }

    /// <summary>SkillGem component +0x10: bit 2 (0x04) is the client's "level locked" (right-click on the gem; its level-up
    /// check 0x142472db0 returns false then). Payload derives bit 5 from GemVariant, as the native copy does.</summary>
    public byte GemFlags { get; init; }

    public ItemRarity Rarity { get; init; }

    /// <summary>Unidentified items carry no <see cref="ModSection.Explicit"/> list (the client does not read it).</summary>
    public bool Identified { get; init; } = true;

    /// <summary>Corrupted (Vaal Orb): the Base component's u8 +0xc7 bit 0, which the client checks for corrupted-only
    /// and corrupted-refused crafting ("input_is_corrupted", 0x141d68e64).</summary>
    public bool Corrupted { get; init; }

    /// <summary>Fracturing Orb: the first this many explicit mods are fractured (kept by every other currency). Sent as
    /// the magic properties' u8 +0x7a, right after their flags; the client shows those mods fractured.</summary>
    public byte FracturedCount { get; init; }

    /// <summary>Mirrored (a Mirror of Kalandra copy): no currency changes it and it cannot be mirrored again. Sent as the
    /// Mods component's flag bit 1 (+0x2df, a plain bool: the client refuses crafting on it, 0x141d68f9b).</summary>
    public bool Mirrored { get; init; }

    /// <summary>Influences, the Base component's u8 +0xc6. Their mods are ordinary affixes, which these open by tag.</summary>
    public ItemInfluence Influences { get; init; }

    /// <summary>Split: the Mods component's flag bit 2 (+0x2e0, "split" in its debug dump 0x1421e1190).</summary>
    public bool Split { get; init; }

    /// <summary>Synthesised: the Mods component's flag bit 5 (+0x2e5, "synthetic" in its debug dump 0x1421e1190).</summary>
    public bool Synthesised { get; init; }

    /// <summary>The indexes in <see cref="Mods"/> of the fractured explicit mods.</summary>
    public IReadOnlySet<int> FracturedIndexes() => Mods.Select((m, i) => (m, i)).Where(p => p.m.Section == ModSection.Explicit)
        .Take(FracturedCount).Select(p => p.i).ToHashSet();

    /// <summary>Implicit, explicit and other mods, written into the Mods component per <see cref="ModSection"/>.</summary>
    public IReadOnlyList<ItemMod> Mods { get; init; } = [];

    /// <summary>A rare item's name: Words.dat keys (ObjectMagicProperties bit 8), prefix then suffix; only sent for
    /// identified items.</summary>
    public IReadOnlyList<uint> NameWords { get; init; } = [];

    /// <summary>A vendor price: (item type hash, amount) entries, Base flag bit 7 (17-npcs.md §5). Empty for items not
    /// in a shop.</summary>
    public IReadOnlyList<(uint TypeHash, uint Amount)> Price { get; init; } = [];

    /// <summary>The four item types whose data ends with an extra u16 (0x1421ebe90; the writer sends 1).</summary>
    static readonly uint[] TrailingU16 = [0x192c7aab, 0x365f206a, 0x503da850, 0x9aff2df6];

    /// <summary>Components whose slot 5 reads nothing (their vtables' slot 5 is the bare `ret` 0x1400f0f00).</summary>
    public static readonly HashSet<string> NoData = ["LocalStats", "AttributeRequirements", "Weapon", "Usable", "Flask", "Quest", "Shield", "RenderItem"];

    /// <summary>Components <see cref="Payload"/> can write.</summary>
    public static readonly HashSet<string> Supported = [.. NoData, "Base", "Mods", "Armour", "Quality", "Sockets", "Charges", "Stack", "SkillGem", "Map", "MapKey"];

    /// <summary>The character level the item requires, sent in the Mods component (+0x2bc). The client keeps the byte
    /// it receives (it only recomputes it, 0x1421e03d0, when an item's mods change on its side) and shows it in the
    /// tooltip and checks it before equipping, so the server must send it. The Gateway sets this from the type and the
    /// mods; without it 0 is sent. <see cref="RequiredLevel"/> overrides it for one item.</summary>
    public static Func<Item, int>? LevelRequirementOf { get; set; }

    /// <summary>This item's level requirement, instead of <see cref="LevelRequirementOf"/>.</summary>
    public int? RequiredLevel { get; init; }

    public static bool CanSerialize(IEnumerable<string> components) => components.All(Supported.Contains);

    /// <summary>
    /// The item data the reader 0x1421ec390 takes (all little-endian: it reads with memcpy): u32 type hash, u16 flags
    /// (bits 0-8 → item +0x28..+0x2f, bit 4 also passed to the item ctor; bit 9 → a u64 follows), then each component's
    /// data in the type's order, then a u16 for four types.
    /// </summary>
    public byte[] Payload()
    {
        if (!CanSerialize(Components)) throw new ArgumentException($"unsupported item components: {string.Join(", ", Components.Where(c => !Supported.Contains(c)))}");
        var w = new BlobWriter().U32(TypeHash).U16(0);
        foreach (string c in Components)
        {
            switch (c)
            {
                // 0x1421e69c0: u16 ItemVisualIdentity HASH16 (null → "Attempt to set visual identity to NULL"), u8 +0xc0,
                // u8 +0xc7 flags, u16 flags (low: bit0 → u8 +0xc5, bit1 flavour text u16, bit2 wide string, bit3 u32,
                // bit4 wide string, bit5 u8 n × (u32, i32), bit6 nested item, bit7 u8 n × (u32 item type, compact);
                // high: bits 0-2 → +0xc4, bit4 → 2 × u32), u8 +0xc6 the influences.
                // Bit 7 (a vendor price): u8 n, then n × (u32 item type hash, compact uint amount; 0x141fa5290 looks the type
                // up, 0x141e61010 reads the amount); stored in the Base's +0xb8 item list.
                case "Base":
                    w.U16(VisualIdentity).U8(0).U8((byte)(Corrupted ? 1 : 0)).U16((ushort)(Price.Count > 0 ? 0x80 : 0)).U8((byte)Influences);
                    if (Price.Count > 0)
                    {
                        w.U8((byte)Price.Count);
                        foreach (var (type, amount) in Price)
                        {
                            w.U32(type);
                            var cw = new LoginWriter();
                            InstanceProtocol.CompactUInt(cw, amount);
                            foreach (byte b in cw.ToArray()) w.U8(b);
                        }
                    }
                    break;
                // Armour 0x1424affc0 reads u8 (0..0x20); it is stored in +0x28 (0x1424afc90 clamps to 0x1f,
                // 0x20 sets +0x2a and uses minimum bases instead of a percentile).
                case "Armour":
                    w.U8(DefenceBaseRoll);
                    break;
                // 0x1421df6e0: u8 +0x2b8 (item level), u8 +0x2bc (level requirement, kept as sent), u16 flags (bit 1
                // duplicated, the mirrored state; bit 2 split; bit 3 disable_mirrored_visuals; bit 4 relic → a u8 after the
                // incubator; bit 5 synthetic; bit 6 incubator; bit 7 a 0x1c8 record; bit 10 vestigial), the base magic
                // properties 0x142201d70 (MagicProperties), u8, u8.
                case "Mods":
                    w.U8(ItemLevel).U8((byte)Math.Clamp(RequiredLevel ?? LevelRequirementOf?.Invoke(this) ?? 0, 0, 255))
                        .U16((ushort)((Mirrored ? 0x2 : 0) | (Split ? 0x4 : 0) | (Synthesised ? 0x20 : 0)));
                    MagicProperties(w); w.U8(0).U8(0);
                    break;
                case "Quality": w.U8(Quality); break;                 // 0x1424aec50
                case "Charges": w.CompactI(Charges); break;           // 0x14249d4b0: signed compact (0x141e490e0)
                case "Stack": w.U16(StackSize); break;                // 0x1423f4b30
                // 0x1423f3f10 (write 0x1423f3e60): u8 +0x19, u8 +0x18, u8 flag (1: a u32 HASH32 follows, looked up in a
                // table; stored at +0x20). Zeros: the map's tier and area come from Maps.dat by its type.
                case "Map": w.U8(0).U8(0).U8(0); break;
                // MapKey 0x1424ae7c0 reads a flag byte, then optional u32/u16/u8. The generic map base already names its
                // tier; 0 sends no overrides.
                case "MapKey": w.U8(0); break;
                // 0x14246eb00: i32 experience (-1: one level over the cap), u32 +0x3c the variant (the copy 0x14246fa50
                // checks it against the GemVariants count and sets +0x10 bit 5 when it is not 0), u8 +0x10, u16 +0x40 an
                // IndexableSupportGems index (0: none).
                // The network reader copies the flags byte verbatim; unlike the native copy it does not derive
                // bit 5 from the variant. Normalize it here for both standalone and nested socket gems.
                case "SkillGem": w.I32(GemExperience).U32((uint)GemVariant)
                    .U8((byte)((GemFlags & ~0x20) | (GemVariant > 0 ? 0x20 : 0))).U16(0); break;
                // 0x1421e2e20: u8 = bit0/bit1 (special, nothing more) | socket count << 2 | link group count << 5; per socket
                // u8 colour << 1 | has gem, then the gem (item data without the length prefix); then a u8 per link group
                // (assumed: the group's socket count).
                case "Sockets":
                    int n = Math.Min(Sockets.Count, 6);
                    var links = Links ?? [.. Enumerable.Repeat((byte)1, n)];
                    w.U8((byte)(n << 2 | Math.Min(links.Count, 7) << 5));
                    foreach (var s in Sockets.Take(n))
                    {
                        w.U8((byte)((byte)s.Colour << 1 | (s.Gem is null ? 0 : 1)));
                        if (s.Gem is not null) w.Bytes(s.Gem.Payload());
                    }
                    foreach (byte g in links.Take(7)) w.U8(g);
                    break;
            }
        }
        if (Array.IndexOf(TrailingU16, TypeHash) >= 0) w.U16(1);
        return w.ToArray();
    }

    /// <summary>
    /// ObjectMagicProperties as 0x142201d70 reads it: u16 flags (bits 0-2 rarity, bit 3 identified, bit 9 an
    /// AlternateQualityTypes row u8 + u8, bit 7 a custom name / bit 8 Words.dat name parts, one bit per mod list), u8
    /// (+0x7a), then the lists in the reader's order: bit 4, 6, 10, 11 and, for identified items only, the name and
    /// bit 5. A mod (0x142430380): u16 Mods.dat HASH16 (unknown → "Deserialised a mod that we don't recognise"), then a
    /// signed compact (0x141e490e0) per stat of the row.
    /// </summary>
    void MagicProperties(BlobWriter w) =>
        global::GameServer.Core.Protocol.Instance.MagicProperties.Write(w, (byte)Rarity, Identified, Mods, NameWords, u7a: FracturedCount);

    /// <summary>The item as inventories and other messages carry it (0x1421ec1d0 / 0x1421ec2c0): u16 big-endian length,
    /// then <see cref="Payload"/>.</summary>
    public byte[] Wire() => new LoginWriter().Blob(Payload()).ToArray();
}

/// <summary>
/// An equipped item as the player's model shows it (Inventories component entry, 0x141e1ee30 → 0x1424f44e0). u8 slot
/// (Inventories.dat column 7: Weapon1 0, Offhand1 1, BodyArmour1 2, Helm1 4, Gloves1 5, Boots1 6, Ring1 8, Ring2 9,
/// Belt1 10, Amulet1 11), then the record: u8 the item's type (0 = empty, nothing else; else its ItemClasses row + 1:
/// the client's weapon type for skill weapon restrictions, 0x1424f44e0 → entry +8, read by 0x141e1f310), two (u16 ItemVisualIdentity HASH16, u32
/// TieredMicrotransactions key, u8) references (0x1424f3fc0, 0x1424f4250), u8 ItemStances row (0x1424f4b10; Wand = 2),
/// u16 ItemVisualIdentity HASH16 (0x1424f4730), u8 (+0x1e).
/// </summary>
public sealed record VisibleItem(byte Slot, ushort VisualIdentity, byte Stance = 0xff, byte Type = 1)
{
    /// <summary>
    /// A worn skin (the first reference, 0x1424f3fc0 → entry +0x18): the cosmetic's ItemVisualIdentity HASH16 and its
    /// MTXTypeGameSpecific HASH32, which the client also looks up for tiered cosmetics. 0 shows the item's own art.
    /// </summary>
    public ushort SkinVisual { get; init; }
    public uint SkinHash { get; init; }

    /// <summary>A worn effect (the second reference, 0x1424f4250 → entry +0x40): an ItemVisualEffect HASH16 (weapon
    /// effects) and its MTXTypeGameSpecific HASH32; 0 for none.</summary>
    public ushort EffectVisual { get; init; }
    public uint EffectHash { get; init; }

    /// <summary>An empty slot (tag 0): what clears an unequipped item from the model. A slot left out of the list keeps
    /// whatever the model showed before.</summary>
    public static VisibleItem Empty(byte slot) => new(slot, 0);

    public void Write(BlobWriter w)
    {
        if (VisualIdentity == 0) { w.U8(Slot).U8(0); return; }
        w.U8(Slot).U8(Type)
            .U16(SkinVisual != 0 ? SkinVisual : VisualIdentity).U32(SkinHash).U8(0)
            .U16(EffectVisual).U32(EffectHash).U8(0)
            .U8(Stance).U16(VisualIdentity).U8(0);
    }
}

/// <summary>
/// An item lying on the ground, `Metadata/MiscellaneousObjects/WorldItem`: Positioned, BaseEvents (no data), Animated,
/// WorldItem, InteractionAction (+ client Render, Targetable). WorldItem (0x141ea10a0): u32 a (non-zero → one more u32),
/// u8 (+0x50), u32 ItemVisualIdentity HASH16 override (0 = the item's own), then the item data without its length
/// prefix (0x1421ec390). The component then calls its vtable +0xb0 with the item, the u8 and both u32.
/// </summary>
public sealed record WorldItemObject(int GridX, int GridY, Item Item)
{
    public const string MetadataPath = "Metadata/MiscellaneousObjects/WorldItem";

    public byte[] Blob()
    {
        var w = new BlobWriter();
        w.U8(0).U8(0);                                   // object flags, base data entry count
        w.I32(GridX).I32(GridY).F32(0).U16(0).U8(0).U8(0); // Positioned
        w.U8(0);                                         // Animated: none
        w.U32(0).U8(0).U32(0).Bytes(Item.Payload());     // WorldItem
        w.U8(0);                                         // InteractionAction
        w.U8(0);                                         // tasks
        return w.ToArray();
    }
}

/// <summary>An item at a grid cell of an inventory. <see cref="Id"/> identifies it within the inventory (the client keeps
/// a map by id and drops duplicates).</summary>
public sealed record InventoryItem(uint Id, byte X, byte Y, Item Item);

public static class InstanceItems
{
    public const ushort InventoryChangeId = 0xbf, PickUpItemId = 0x28, DropItemId = 0x29, PlaceItemId = 0x2a, InventoryRequestId = 0x2d,
        ItemActionId = 0x4a, SplitStackId = 0x7c, CommandId = 0x16, SocketGemId = 0x30, UnsocketGemId = 0x2f,
        LevelUpGemId = 0x31, LockGemId = 0xd7;

    /// <summary>
    /// C→S 0x31 (14 bytes, live 2026-10-23): the level-up arrow on a socketed gem with experience one below its next
    /// level's threshold (the client's "+", 19-gems.md §4). u32 inventory id, u32 item id, u32 socket index.
    /// </summary>
    public static SocketGem ReadLevelUpGem(ReadOnlySpan<byte> m) => ReadSocketGem(m);

    /// <summary>
    /// C→S 0xd7 (15 bytes, live 2026-10-23): the gem's level lock toggled by a right-click. u32 inventory id, u32 item
    /// id, u32 socket index, u8 locked (1 lock, 0 unlock; SkillGem +0x10 bit 2).
    /// </summary>
    public sealed record LockGem(uint InventoryId, uint ItemId, uint Socket, bool Locked);

    public const int LockGemLength = 15;

    public static LockGem ReadLockGem(ReadOnlySpan<byte> m)
    {
        var r = new LoginReader(m[2..]);
        return new(r.U32(), r.U32(), r.U32(), r.U8() != 0);
    }

    /// <summary>
    /// C→S 0x2f (17 bytes): take the gem out of a socket onto the cursor (a click on a socketed gem with nothing held).
    /// u32 inventory id, u32 item id, u32 socket index, then three bytes (u8 0 in every capture, two that vary).
    /// </summary>
    public static SocketGem ReadUnsocketGem(ReadOnlySpan<byte> m) => ReadSocketGem(m);

    /// <summary>
    /// C→S 0x30 (class 0x1435c0838, write 0x141f90650; 16 bytes): put the held gem into a socket. u32 inventory id, u32
    /// item id, u32 socket index, u8 +0x1c, u8 +0x1d (both vary between clicks; unused here).
    /// </summary>
    public sealed record SocketGem(uint InventoryId, uint ItemId, uint Socket);

    public static SocketGem ReadSocketGem(ReadOnlySpan<byte> m)
    {
        var r = new LoginReader(m[2..]);
        return new(r.U32(), r.U32(), r.U32());
    }

    /// <summary>Commands.dat row 8 (DestroyCursorItem, "/destroy") + 1: what the Keep / Destroy popup's Destroy sends.</summary>
    public const byte DestroyCursorItemCommand = 9;

    /// <summary>Commands.dat row 44 (WarpToHideout, "/hideout") + 1: the chat command that goes to the hideout.</summary>
    public const byte WarpToHideoutCommand = 45;

    /// <summary>
    /// C→S 0x16 (class 0x1435bff00, write 0x141f91480, sender 0x14215c3b0): a chat command from Commands.dat with
    /// SendsGenericCommandPacket. u8 command (row + 1), then the argument: u16 byte count + UTF-8 (0x140e0c9b0; at most
    /// 300 characters, 1000 for command 0x39). The Keep / Destroy popup for an item dropped in town sends
    /// <see cref="DestroyCursorItemCommand"/> with no argument.
    /// </summary>
    public sealed record Command(byte Id, string Argument);

    public static Command ReadCommand(ReadOnlySpan<byte> m)
    {
        var r = new LoginReader(m[2..]);
        byte id = r.U8();
        return new(id, System.Text.Encoding.UTF8.GetString(r.Blob()));
    }

    /// <summary>The length of a 0x16 at the start of <paramref name="data"/>, or null until its header has arrived.</summary>
    public static int? CommandLength(ReadOnlySpan<byte> data) =>
        data.Length < 5 ? null : 5 + System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data[3..]);

    // C→S 0x29 (class 0x14316c930, id only, sender 0x140117b80): drop the held item into the world. Outside town the
    // client sends it at once; in town (and for quest items) it shows the Keep / Destroy popup instead and sends nothing
    // unless Destroy is chosen (0x16, DestroyCursorItemCommand).

    /// <summary>
    /// S→C 0xbf (dispatcher case 0x34, class 0x1435dbd28, write 0x1421a37f0 → 0x1425079a0; handler 0x141d62550 →
    /// 0x1421f5160): change one inventory. u8 manager, u32 inventory id, u32 revision (→ inventory +0x1e8), u32 removed
    /// count (0xffffffff = remove every item) + that many u32 item ids, u32 added count + (u32 id, u8 x, u8 y, item), u8
    /// flag (1: 4 × u32 follow, the 0xc0 flag-bit-4 block). An unknown inventory id is fatal ("Got an inventory set change
    /// for an inventory that isn't in the set").
    /// </summary>
    public static byte[] InventoryChange(uint inventoryId, uint revision, IReadOnlyList<uint> removed, IReadOnlyList<InventoryItem> added, byte manager = 0)
    {
        var w = new LoginWriter().U16(InventoryChangeId).U8(manager).U32(inventoryId).U32(revision).U32((uint)removed.Count);
        foreach (uint id in removed) w.U32(id);
        w.U32((uint)added.Count);
        foreach (var it in added) w.U32(it.Id).U8(it.X).U8(it.Y).Bytes(it.Item.Wire());
        return w.U8(0).ToArray();
    }

    /// <summary>
    /// C→S 0x28 (class 0x1435bf5f0, write 0x141fa0d00; 13 bytes): a click on an item in an inventory, which picks it up to
    /// the cursor. u32 inventory id with HNC side-channel bits in bits 21-29 (the reader 0x141fa0ea0 masks with
    /// 0xc01fffff), u32 item id, u8, u8, u8 (+0x18..+0x1a; the first was 3 in every capture).
    /// </summary>
    public sealed record PickUpItem(uint InventoryId, uint ItemId, byte A, byte B, byte C);

    public static PickUpItem ReadPickUpItem(ReadOnlySpan<byte> m)
    {
        var r = new LoginReader(m[2..]);
        return new(r.U32() & 0xc01fffff, r.U32(), r.U8(), r.U8(), r.U8());
    }

    /// <summary>
    /// C→S 0x2a (class 0x1435bf710, write 0x141f8feb0, sender 0x14215f400; 18 bytes): put the item held on the cursor
    /// into an inventory. u32 inventory id, u32 x, u32 y (the target cell; 0, 0 for equipment), u8 +0x1d (1 with Shift:
    /// one of a held stack, the sender's param 7), u8 +0x1c (3 in every capture), u8 +0x1e, u8 +0x1f. The client sends it for any click while it holds an item, including on an
    /// occupied cell (a swap).
    /// </summary>
    public sealed record PlaceItem(uint InventoryId, uint X, uint Y, byte A, byte B, byte C, byte D);

    public static PlaceItem ReadPlaceItem(ReadOnlySpan<byte> m)
    {
        var r = new LoginReader(m[2..]);
        return new(r.U32(), r.U32(), r.U32(), r.U8(), r.U8(), r.U8(), r.U8());
    }

    public const int InventoryRequestLength = 8;

    // C→S 0x2d (class 0x1435c02b8, write 0x141f90340, senders 0x14215f520 / 0x14215f570; 8 bytes): an inventory request,
    // not mapped yet. The right-click item use is 0x58 below.

    public const ushort UseItemId = 0x58;
    public const int UseItemLength = 10;

    /// <summary>
    /// C→S 0x58 (class 0x1435bf8a8, write 0x141f926a0; 10 bytes): use an item that needs no target, such as an Orb of
    /// Regret. u32 inventory id, u32 item id, both big-endian. The client sends it again while a usable item is held.
    /// </summary>
    public sealed record UseItemRequest(uint InventoryId, uint ItemId);

    public static UseItemRequest ReadUseItem(ReadOnlySpan<byte> m)
    {
        var r = new LoginReader(m[2..]);
        return new(r.U32(), r.U32());
    }

    public const ushort UseFlaskId = 0x70;
    public const int UseFlaskLength = 6;

    /// <summary>
    /// C→S 0x70 (6 bytes, live 2026-10-20): a flask key was pressed. u32 big-endian, the flask slot (0 for key 1, 1 for
    /// key 2, …: the x cell in the flask bar, Inventories row 11). Inferred from presses of the start kit's two flasks, not
    /// read from the client's serializer. The client changes nothing itself: the server spends the charges and recovers.
    /// </summary>
    public static uint ReadUseFlask(ReadOnlySpan<byte> m) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(m[2..]);

    /// <summary>
    /// S→C 0x263: item-use result. Reader 0x1421aded0: u32 type HASH32, inventory id, item id, string, u32 result.
    /// The empty string and zero result let the native callback play the base item's use sound; 0x4e2 refuses a full
    /// recovery pool without that sound. The result occupies four wire bytes despite its u16 client field.
    /// </summary>
    public static byte[] ItemUsed(uint typeHash, uint inventoryId, uint itemId, ushort result = 0) =>
        new LoginWriter().U16(0x263).U32(typeHash).U32(inventoryId).U32(itemId).U16(0).U32(result).ToArray();

    public const ushort UseItemTypeOnId = 0x72;
    public const int UseItemTypeOnLength = 23;

    /// <summary>
    /// C→S 0x72 (23 bytes, live 2026-10-21): the held currency used again with Shift (the client keeps it on the cursor
    /// while the inventory has more). It names the currency by its BaseItemTypes HASH32, not by item: u32 type, then u32
    /// inventory id and u32 item id of the target, all big-endian; then nine uninterpreted bytes, varying by currency.
    /// Fusing capture: 00 00 00 00 01 00 06 00 00; Chromatic: 00 00 00 00 01 00 07 00 02.
    /// </summary>
    public sealed record UseItemTypeOn(uint TypeHash, uint TargetInventoryId, uint TargetId);

    public static UseItemTypeOn ReadUseItemTypeOn(ReadOnlySpan<byte> m)
    {
        var r = new LoginReader(m[2..]);
        return new(r.U32(), r.U32(), r.U32());
    }

    public const ushort UseItemOnId = 0x71;
    public const int UseItemOnLength = 18;

    /// <summary>
    /// C→S 0x71 (18 bytes, live 2026-10-20): use an item on another one, e.g. right-click a Scroll of Wisdom, then click
    /// an unidentified item. u32 inventory id and u32 item id of the used item, then of the target, all big-endian.
    /// </summary>
    public sealed record UseItemOn(uint InventoryId, uint ItemId, uint TargetInventoryId, uint TargetId);

    public static UseItemOn ReadUseItemOn(ReadOnlySpan<byte> m)
    {
        var r = new LoginReader(m[2..]);
        return new(r.U32(), r.U32(), r.U32(), r.U32());
    }

    public const ushort UseItemOnPassiveId = 0x77;
    public const int UseItemOnPassiveLength = 12;

    /// <summary>
    /// C→S 0x77 (12 bytes, live 2026-10-29): a tattoo used on a passive. Captured after right-clicking
    /// Tattoo of the Ngamahu Firewalker and clicking an allocated small Strength node:
    /// <c>0077 00000001 0000049C 5335</c> — u32 inventory id, u32 item id, u16 PassiveSkillGraphId, all big-endian.
    /// </summary>
    public sealed record UseItemOnPassive(uint InventoryId, uint ItemId, ushort Node);

    public static UseItemOnPassive ReadUseItemOnPassive(ReadOnlySpan<byte> m)
    {
        var r = new LoginReader(m[2..]);
        return new(r.U32(), r.U32(), r.U16());
    }

    public const int ItemActionLength = 14;

    /// <summary>
    /// C→S 0x4a (class 0x1435bf6d0, write 0x141fa08d0, sender 0x14215f5c0; 14 bytes): a click with the held item on an
    /// item (live 2026-10-19: a held Scroll Fragment clicked on another one sends this, not 0x2a). u32 inventory id with
    /// HNC side-channel bits from bit 20 (mask 0xc00fffff), u32 the clicked item's id, u8 flags (bits 0-2: sender params 6
    /// and 7 shifted left once, bit 2 = Shift held; HNC bits 3-7), u8 (3 unless the caller passes its own), u16 (two bytes, low first; differs
    /// on every click, unknown).
    /// </summary>
    public sealed record ItemAction(uint InventoryId, uint ItemId, byte Flags, byte B, ushort Extra);

    public static ItemAction ReadItemAction(ReadOnlySpan<byte> m)
    {
        var r = new LoginReader(m[2..]);
        uint inventory = r.U32() & 0xc00fffff, item = r.U32();
        byte flags = (byte)(r.U8() & 0x07), b = r.U8(), lo = r.U8(), hi = r.U8();
        return new(inventory, item, flags, b, (ushort)(lo | hi << 8));
    }

    public const int SplitStackLength = 17;

    /// <summary>
    /// C→S 0x7c (class 0x1435bfdd8, write 0x141f90870, sender 0x142165640; 17 bytes): Shift + click on a stack, then the
    /// split window's amount. u32 inventory id, u32 item id, u32 amount, u8 (3 unless the caller passes its own), u16 (two
    /// bytes, low first; differs every time, unknown). No side-channel bits.
    /// </summary>
    public sealed record SplitStack(uint InventoryId, uint ItemId, uint Amount, byte B);

    public static SplitStack ReadSplitStack(ReadOnlySpan<byte> m)
    {
        var r = new LoginReader(m[2..]);
        return new(r.U32(), r.U32(), r.U32(), r.U8());
    }
}
