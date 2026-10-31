using System.Net.Sockets;
using System.Collections.Frozen;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    // Pools and published stats already include effective item ES. Raw bases would count suppressed/converted ES.
    static bool PlayerHasEnergyShieldForRecovery(ClientState conn, Func<string, int> get) =>
        conn.Pools.EnergyShield > 0 || PlayerHitRecovery.EnergyShieldPresent(get);

    static PlayerInventories ItemsOf(ClientState conn) => PlayerInventories.For(string.IsNullOrEmpty(conn.Character) ? "Exile" : conn.Character, ItemsFor(conn));

    // Active local item defences retain their base roll, local conversions and final native rounding.
    static IEnumerable<(byte Row, (decimal Armour, decimal Evasion, int EnergyShield, decimal Ward) Defence)> EquippedArmourDefences(
        ClientState conn, Func<string, int>? get = null)
    {
        var items = ItemsOf(conn);
        var catalog = ItemsFor(conn);
        foreach (byte row in new[] { PlayerInventories.BodyArmour, PlayerInventories.Helmet, PlayerInventories.Gloves, PlayerInventories.Boots, PlayerInventories.Offhand1 })
            if (items.Items(row) is [var equipped, ..] && !IsInactive(conn, equipped.Id)
                && catalog.ByHash(equipped.Item.TypeHash) is { } type && catalog.Defences(type) is { } defences)
            {
                var (a, e, s) = ItemDefences.Calculate(equipped.Item, defences, catalog.Mods);
                int percent = row == PlayerInventories.BodyArmour
                    ? Math.Clamp(get?.Invoke("ward_instead_of_%_body_armour_and_evasion") ?? 0, 0, 100) : 0;
                decimal armourWard = a * percent / 100m, evasionWard = e * percent / 100m;
                var local = ItemModStats.Local(equipped.Item, catalog.Mods);
                decimal ward = armourWard + evasionWard
                    + (long)WardState.Max((decimal)defences.WardMin + local.GetValueOrDefault("local_ward"), local.GetValueOrDefault("local_ward_+%"))
                        * (100 + Math.Clamp((int)equipped.Item.Quality, 0, 100)) / 100;
                yield return (row, (a - armourWard, e - evasionWard, s, ward));
            }
    }

    // Active item-tooltip values become flat inputs to the character-wide pool calculation.
    static (decimal Armour, decimal Evasion, int EnergyShield, decimal Ward) EquippedBaseDefences(ClientState conn, Func<string, int>? get = null)
    {
        decimal armour = 0, evasion = 0, ward = 0; long energyShield = 0;
        foreach (var (_, defence) in EquippedArmourDefences(conn, get))
        {
            armour += defence.Armour;
            evasion += defence.Evasion;
            energyShield += defence.EnergyShield;
            ward += defence.Ward;
        }
        return (armour, evasion, (int)Math.Min(int.MaxValue, energyShield), ward);
    }

    // Native 0x142523ab0: flag selects 100%, otherwise use the percentage; truncate each item's local ES separately.
    // The conversion removes all armour-item ES, even when the Life gained is only 50% of its value.
    static (int Life, int EnergyShield) ConvertEquipmentEnergyShield(ClientState conn, Func<string, int> get, int energyShield)
    {
        int percent = get("gain_maximum_life_instead_of_maximum_es_from_armour") != 0 ? 100
            : Math.Max(0, get("gain_maximum_life_instead_of_maximum_es_from_armour_at_x%_of_the_value"));
        if (percent == 0) return (0, energyShield);
        bool shieldExcluded = get("minions_get_shield_stats_instead_of_you") != 0
            || get("gain_no_defences_from_equipped_shield") != 0;
        long life = 0;
        foreach (var (row, defence) in EquippedArmourDefences(conn))
            if (row != PlayerInventories.Offhand1 || !shieldExcluded)
                life += (long)defence.EnergyShield * percent / 100;
        return ((int)Math.Min(int.MaxValue, life), 0);
    }

    // The item in the main hand (row 2, the active set) and its base type, unless its requirements are not met.
    static (Item Item, ItemCatalog.BaseType? Type)? ActiveMainHand(ClientState conn) => ActiveHand(conn, PlayerInventories.Weapon1);

    // "While holding a Fishing Rod" (PoB Condition UsingFishing from weaponData1): the active set's main hand only, and
    // not a rod whose requirements are unmet. The client's calculators test main_hand_weapon_type 15 the same way.
    static bool HoldingFishingRod(ClientState conn) => ActiveMainHand(conn)?.Type?.Class == "FishingRod";

    // The item in the off hand (row 3, the active set). The weapon swap trades the hands' items with rows 14/15
    // (SwapWeaponSets), so Offhand2 always holds the inactive set's off hand, which is never swung.
    static (Item Item, ItemCatalog.BaseType? Type)? ActiveOffHand(ClientState conn) => ActiveHand(conn, PlayerInventories.Offhand1);

    static (Item Item, ItemCatalog.BaseType? Type)? ActiveHand(ClientState conn, byte row) =>
        ItemsOf(conn).Items(row).Where(i => !IsInactive(conn, i.Id))
            .Select(i => ((Item, ItemCatalog.BaseType?)?)(i.Item, ItemsFor(conn).ByHash(i.Item.TypeHash))).FirstOrDefault();

    // The active weapon's local typed damage, quality and duration; no weapon uses the class's unarmed attack.
    static SkillCatalog.Unarmed WeaponFor(ClientState conn) => WeaponOf(conn, ActiveMainHand(conn));

    static SkillCatalog.Unarmed WeaponOf(ClientState conn, (Item Item, ItemCatalog.BaseType? Type)? mainHand) =>
        WeaponIn(conn, mainHand) ?? SkillsFor(conn).UnarmedFor(conn.ClassRow);

    // A hand's weapon attack (WeaponOf); null when the hand holds no weapon (empty, a shield, a quiver).
    static SkillCatalog.Unarmed? WeaponIn(ClientState conn, (Item Item, ItemCatalog.BaseType? Type)? hand)
    {
        var unarmed = SkillsFor(conn).UnarmedFor(conn.ClassRow);
        if (hand is not var (item, type) || type is null || SkillsFor(conn).WeaponBase(type.Row) is not var (ms, min, max, range)) return null;
        var local = ItemWeaponStats.Calculate(item, (ms, min, max, range), ItemsFor(conn).Mods);
        var physical = local.Damage[GameServer.Core.World.DamageType.Physical];
        // The weapon's class decides Arcane Might's wand-only grant and the melee context of its own components.
        return new SkillCatalog.Unarmed(local.AttackMs, (int)physical.Min, (int)physical.Max, Math.Max(range, unarmed.Range))
        {
            LocalDamage = local.Damage, LocalSources = ItemModStats.Local(item, ItemsFor(conn).Mods).ToFrozenDictionary(),
            WieldingWand = type.Class == "Wand", Melee = type.Class is not ("Wand" or "Bow"),
        };
    }

    // The off-hand weapon of a dual-wielded attack (the active set's Offhand1), with the same values WeaponIn gives the main
    // hand. Null when there is nothing to swing with there (empty, a shield, a quiver) or the main hand holds no weapon.
    static SkillCatalog.Unarmed? OffhandWeaponFor(ClientState conn) =>
        WeaponIn(conn, ActiveMainHand(conn)) is null ? null : WeaponIn(conn, ActiveOffHand(conn));

    static (Dictionary<string, int> Main, Dictionary<string, int> Off) HandLocalStats(ClientState conn)
    {
        Dictionary<string, int> Local((Item Item, ItemCatalog.BaseType? Type)? hand) => hand is { } active
            ? ItemModStats.Local(active.Item, ItemsFor(conn).Mods) : [];
        // Weapon swaps exchange the equipment rows; rows 14/15 remain inactive even when an active hand is empty.
        return (Local(ActiveMainHand(conn)), Local(ActiveOffHand(conn)));
    }

    // Current attack executors use the main hand. Read only its qualified active item; a shield, an inactive
    // weapon set or an unqualified weapon cannot supply local modifiers to this contact.
    static IReadOnlyDictionary<string, int>? MainHandLocalSources(ClientState conn) =>
        WeaponFor(conn).LocalSources;

    // Current actions and passive weapon conditions use only the hands' active rows. A normal weapon swap trades
    // these rows with 14/15 before recomputing pools; an explicit false is reserved for inspecting both stored sets.
    internal static WeaponProfile WeaponProfileFor(ClientState conn, bool activeSet = true)
    {
        var items = ItemsOf(conn);
        // An inactive weapon (requirements not met) is not there at all: the class is unarmed, as the default attack's
        // unarmed fallback (WeaponFor) is.
        ItemCatalog.BaseType? At(byte row) => items.Items(row).Where(i => !IsInactive(conn, i.Id))
            .Select(i => ItemsFor(conn).ByHash(i.Item.TypeHash)).FirstOrDefault(t => t is not null);
        static bool OneHandMelee(ItemCatalog.BaseType t) => t.Class is "Claw" or "Dagger" or "Rune Dagger"
            or "One Hand Sword" or "Thrusting One Hand Sword" or "One Hand Axe" or "One Hand Mace" or "Sceptre";
        static bool OneHand(ItemCatalog.BaseType t) => OneHandMelee(t) || t.Class == "Wand";
        static bool TwoHandMelee(ItemCatalog.BaseType t) => t.Class is "Two Hand Sword" or "Two Hand Axe"
            or "Two Hand Mace" or "Staff" or "Warstaff";
        static bool Pair(ItemCatalog.BaseType? main, ItemCatalog.BaseType? off) =>
            main is { } m && off is { } o && OneHand(m) && OneHand(o);
        var main1 = At(2);
        var off1 = At(3);
        var main2 = activeSet ? null : At(14);
        var off2 = activeSet ? null : At(15);
        var all = new[] { main1, off1, main2, off2 }.OfType<ItemCatalog.BaseType>().ToList();
        var dual = Pair(main1, off1) || Pair(main2, off2);
        var claws = (main1?.Class == "Claw" && off1?.Class == "Claw") || (main2?.Class == "Claw" && off2?.Class == "Claw");
        var unarmed = main1 is null && main2 is null;
        var mainItem = items.Items(2).Concat(activeSet ? [] : items.Items(14)).Where(i => !IsInactive(conn, i.Id)).Select(i => i.Item).FirstOrDefault();
        var local = mainItem is null ? [] : ItemModStats.Local(mainItem, ItemsFor(conn).Mods);
        return new WeaponProfile(
            Shield: activeSet ? off1?.Class == "Shield" : HasShield(conn),
            DualWield: dual,
            Staff: all.Any(t => t.Class is "Staff" or "Warstaff"),
            Bow: all.Any(t => t.Class == "Bow"),
            DualClaws: claws,
            Unarmed: unarmed,
            OneHand: all.Any(OneHand),
            TwoHand: all.Any(t => TwoHandMelee(t) || t.Class == "Bow"),
            Melee: unarmed || all.Any(t => OneHandMelee(t) || TwoHandMelee(t)),
            OneHandMelee: all.Any(OneHandMelee),
            TwoHandMelee: all.Any(TwoHandMelee),
            Axe: all.Any(t => t.Class is "One Hand Axe" or "Two Hand Axe"),
            Sword: all.Any(t => t.Class is "One Hand Sword" or "Thrusting One Hand Sword" or "Two Hand Sword"),
            Mace: all.Any(t => t.Class is "One Hand Mace" or "Two Hand Mace"),
            Claw: all.Any(t => t.Class == "Claw"),
            Dagger: all.Any(t => t.Class is "Dagger" or "Rune Dagger"),
            Wand: all.Any(t => t.Class == "Wand"),
            // The main hand's local critical chance and accuracy feed its character calculations. Local attack speed
            // is item metadata here; ItemWeaponStats already includes it in that hand's duration.
            Critical: (main1 ?? main2) is { } main ? AddedBaseCriticalStrike.LocalWeaponChance(ItemsFor(conn).WeaponCritical(main),
                local.GetValueOrDefault("local_critical_strike_chance"), local.GetValueOrDefault("local_critical_strike_chance_+%")) : 0,
            LocalAttackSpeedPct: local.GetValueOrDefault("local_attack_speed_+%"),
            LocalAccuracy: local.GetValueOrDefault("local_accuracy_rating"));
    }

    // Both stored offhands, used only by the explicit inventory-wide profile inspection above.
    static bool HasShield(ClientState conn) => OffhandTypes(conn).Any(t => t.Class == "Shield");

    // Base block comes from the active, requirement-enabled shield, like its local defences and global modifiers.
    static int ShieldBaseBlock(ClientState conn) => ActiveOffHand(conn)?.Type is { Class: "Shield" } shield
        ? ItemsFor(conn).ShieldBlock(shield) : 0;

    static IEnumerable<ItemCatalog.BaseType> OffhandTypes(ClientState conn)
    {
        var items = ItemsOf(conn);
        foreach (byte row in new byte[] { PlayerInventories.Offhand1, 15 })
            foreach (var item in items.Items(row))
                if (ItemsFor(conn).ByHash(item.Item.TypeHash) is { } type) yield return type;
    }

    static PlayerInventories.Shape? ShapeOf(Item i) => Items.ByHash(i.TypeHash) is { } t
        ? new PlayerInventories.Shape(t.Width, t.Height, t.Class, t.MaxStack,
            t.BecomesWhenFull && Items.Row(t.FullStackRow) is { Supported: true } full ? () => ItemCatalog.Create(full) : null)
        : null;

    // The weapon type of an equipment row's item (its BaseItemTypes row), or null: the active (not inactive) item.
    static ItemCatalog.BaseType? HandType(ClientState conn, byte row) =>
        ItemsOf(conn).Items(row).Where(i => !IsInactive(conn, i.Id))
            .Select(i => ItemsFor(conn).ByHash(i.Item.TypeHash)).FirstOrDefault();

    // The record's type is the item's ItemClasses row + 1: the client checks skills' weapon restrictions against it.
    static VisibleItem? LookOf(Item i) => Items.ByHash(i.TypeHash) is { } t
        ? new VisibleItem(0, t.VisualIdentity, t.Stance,
            t.ClassRow is >= 0 and < 255 ? (byte)(t.ClassRow + 1) : (byte)1) : null;

    // What the model wears: the equipped items, dressed in the worn cosmetics (CosmeticEffects). A skin replaces the item's
    // art in its entry's first reference and a weapon effect fills the second; attachments and character effects fill the
    // model slots no item uses, which go out empty when nothing is worn there so taking one off clears it.
    static IReadOnlyList<VisibleItem> Looks(ClientState conn, PlayerInventories items)
    {
        var looks = items.Equipped(LookOf).ToList();
        // A weapon whose requirements are not met is not in the hands (WeaponProfileFor, WeaponFor): the model leaves it out
        // and its slot goes out empty, so the client greys out the skills that need it (11-skills.md §10a) and the default
        // attack punches, while the inventory still shows the item where it is.
        foreach (var (row, slot) in new[] { (PlayerInventories.Weapon1, (byte)0), (PlayerInventories.Offhand1, (byte)1) })
            if (items.Items(row) is [var held, ..] && IsInactive(conn, held.Id) && looks.FindIndex(l => l.Slot == slot) is int hand and >= 0)
                looks[hand] = VisibleItem.Empty(slot);
        // A bow is held in the left hand, and its model slot is not the main hand's: BowSetup holds the record the live server
        // sends (the bow's art in slot 1, an arrow in slot 0 with the weapon type and stance, the quiver on the back in 7).
        if (HandType(conn, PlayerInventories.Weapon1) is { Class: "Bow" } bow)
        {
            int quiver = looks.FindIndex(l => l.Slot == 1 && l.VisualIdentity != 0);
            ushort quiverArt = quiver >= 0 ? looks[quiver].VisualIdentity : ItemsFor(conn).Visual("DefaultQuiver") ?? 0x328b;
            looks.RemoveAll(l => l.Slot is 0 or 1 or 7);
            looks.AddRange(BowSetup.Entries(bow, ItemsFor(conn).Visual("DefaultArrow") ?? 0xb2f6, quiverArt));
        }
        var cosmeticSlots = CosmeticEffects.CosmeticOnlySlots.ToDictionary(s => s, VisibleItem.Empty);
        foreach (var worn in WornCosmetics(conn))
        {
            if (MtxEffects.SlotOf(worn.Slot) is not { ModelSlot: byte model } slot || MtxEffects.Of(worn.MtxTypeHash) is not { } fx) continue;
            if (cosmeticSlots.ContainsKey(model))
            {
                if (fx.Visual != 0)
                    cosmeticSlots[model] = new VisibleItem(model, fx.Visual) { SkinVisual = fx.Visual, SkinHash = worn.MtxTypeHash };
                continue;
            }
            int at = looks.FindIndex(l => l.Slot == model);
            if (at < 0 || looks[at].VisualIdentity == 0) continue;   // nothing equipped there to dress
            if (slot.IsWeaponEffect && fx.WeaponEffect != 0)
                looks[at] = looks[at] with { EffectVisual = fx.WeaponEffect, EffectHash = worn.MtxTypeHash };
            else if (!slot.IsWeaponEffect && fx.Visual != 0)
                looks[at] = looks[at] with { SkinVisual = fx.Visual, SkinHash = worn.MtxTypeHash };
        }
        return [.. looks, .. cosmeticSlots.Values];
    }

    // The stats worn cosmetics give the character (footprints, apparitions, …), summed per stat. A pet is the stat the client
    // spawns it from: pet_slot1 for the first Pet slot (MicrotransactionSlot 18), pet_slot2 for the second (19).
    // Skill Gem Effect (slot 49) is left out: its skill_art_variation belongs to one skill's stats, not the character's.
    static IEnumerable<(uint Stat, int Value)> CosmeticStats(ClientState conn) =>
        WornCosmetics(conn).Where(w => w.Slot != 49).SelectMany(w => MtxEffects.Of(w.MtxTypeHash) is not { } fx ? [] :
                fx.Pet != 0 && Stats.Key(w.Slot == 19 ? "pet_slot2" : "pet_slot1") is uint pet ? [.. fx.Stats, (pet, fx.Pet)] : fx.Stats)
            .GroupBy(s => s.Stat, s => s.Value).Select(g => (g.Key,
                ActorScale.IsInput(g.Key) ? ActorScale.Saturate(g.Sum(v => (long)v)) : g.Sum()));

    static IEnumerable<(uint Stat, int Value)> CosmeticScaleSources(ClientState conn) =>
        CosmeticStats(conn).Where(s => ActorScale.IsInput(s.Stat));

    // What the player wears changed (an item went into or out of a visible equipment row): 0x2ba Inventories for the model,
    // then the Actor skill list again, since the gems socketed in the equipment grant skills.
    static async Task SendEquipment(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, PlayerInventories items)
    {
        RefreshVaalSouls(conn);
        // Under the Monsters lock, as every pools writer (a utility flask that ends recomputes under it too). First, so the
        // model below knows which of the new items are inactive.
        lock (conn.Monsters) { RecomputePoolsAndSpeed(conn); conn.PlayerMana = Math.Min(conn.PlayerMana, (uint)UnreservedMana(conn)); }
        await SendModel(remote, stream, ciphers, conn, Looks(conn, items), "equipment");
        // The socketed gems may have changed: the Cosmetics tab's skill slots follow them.
        await SendWornCosmetics(remote, stream, ciphers, conn, "equipment changed");
        await SendSkills(remote, stream, ciphers, conn);
        await SendPlayerStatSnapshot(remote, stream, ciphers, conn,
            () => PlayerStatsUpdateAtGate(conn, PlayerStatsWithCleared(conn)),
            "0x01b3 player stats (equipment)");
        await SendPlayerMana(remote, stream, ciphers, conn, $"0x01b1 player mana {conn.PlayerMana} (equipment)");
    }

    static async Task SendModel(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        IReadOnlyList<VisibleItem> equipped, string why)
    {
        conn.ModelHands = HandsKey(equipped);
        await Send(remote, stream, ciphers, InstanceEntities.PlayerEquipmentUpdate(PlayerId, equipped),
            $"0x02ba player equipment ({why}): {string.Join(", ", equipped.Select(e => $"slot {e.Slot} visual 0x{e.VisualIdentity:x4} stance {e.Stance}"))}");
    }

    // The hands' part of the model (main hand 0, off hand 1, a bow's quiver 7) as it was last sent.
    internal static string HandsKey(IEnumerable<VisibleItem> equipped) => string.Join(",",
        equipped.Where(e => e.Slot is 0 or 1 or 7).OrderBy(e => e.Slot).Select(e => $"{e.Slot}:{e.VisualIdentity:x4}:{e.Type}"));

    // A weapon's requirements can start or stop being met while the equipment stays (a level, a passive, another item's
    // attributes): the model follows, so a weapon that became inactive leaves the hands and one that qualifies again returns.
    static async Task SendModelIfHandsChanged(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        var equipped = Looks(conn, ItemsOf(conn));
        if (conn.ModelHands is { } sent && sent != HandsKey(equipped))
            await SendModel(remote, stream, ciphers, conn, equipped, "weapon requirements changed");
    }

    // Dev commands (/item_mod, /rarity, /sockets): changes the held item and replaces it on the cursor (0xbf removing and adding
    // its id). The item needs the component the change is about.
    static async Task ChangeHeldItem(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, Func<Item, Item> change, string what,
        string component = "Mods")
    {
        var items = ItemsOf(conn);
        if (items.Items(PlayerInventories.Cursor) is not [var held, ..]) { await conn.Commands!.Reply("Hold an item first"); return; }
        if (!held.Item.Components.Contains(component)) { await conn.Commands!.Reply($"{ItemsFor(conn).ByHash(held.Item.TypeHash)?.Name} has no {component} component"); return; }
        if (items.Change(PlayerInventories.Cursor, held.Id, change) is not { } changed) return;
        await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.Cursor),
            items.NextRevision(PlayerInventories.Cursor), [held.Id], [changed]), $"0x00bf cursor: item {held.Id} {what}");
        await conn.Commands!.Reply(what);
    }

    static ModSection? ParseSection(string text) => text.ToLowerInvariant() switch
    {
        "implicit" or "imp" => ModSection.Implicit,
        "explicit" or "exp" => ModSection.Explicit,
        "enchant" or "ench" => ModSection.Enchant,
        "scourge" or "s10" => ModSection.Scourge,
        "hidden" or "s11" => ModSection.Hidden,
        _ => null,
    };

    // Takes the held item off the cursor (0xbf); null when nothing is held.
    static async Task<InventoryItem?> TakeHeld(string remote, NetworkStream stream, ConnectionCiphers ciphers, PlayerInventories items, string why)
    {
        if (items.Items(PlayerInventories.Cursor) is not [var held, ..] || items.Remove(PlayerInventories.Cursor, held.Id) is null) return null;
        await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.Cursor),
            items.NextRevision(PlayerInventories.Cursor), [held.Id], []), $"0x00bf cursor: remove item {held.Id} ({why})");
        return held;
    }
}
