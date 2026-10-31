# 13 — Items: serialization, inventories, moving, sockets, stacks, the ground

Code: `src/GameServer.Core/Protocol/Instance/InstanceItems.cs` (`Item`, `VisibleItem`, `WorldItemObject`,
`InstanceItems`), `InstanceInventories.AddInventory` (`InstanceEntities.cs`); in the Gateway `ItemCatalog.cs`,
`PlayerInventories.cs` and the item handlers in `Program.Items.cs`. Tests: `InstanceItemsTests`. Live trace:
`scripts/re/frida/common.js` + `scripts/re/frida/trace-items.js`. Verified with the real client on 2026-10-18.

## 1. Item types

An item's type is a **BaseItemTypes row**. The client's `Metadata/ItemTypeRegister` files each row under its `HASH32`
column, which is MurmurHash2 of the row's Id: the same hash as object types (`InstanceEntities.ObjectTypeHash`;
`Metadata/Items/Weapons/OneHandWeapons/Wands/Wand1` → 3652280879). Lookup: `0x1421ec390` → `0x141fa5290` (sorted array of
0x30-byte entries keyed by the u32).

Components come from the `.it` files, merged like `.ot` files ([04](04-player-entity.md), `scripts/re/ot_components.py`)
with two differences: nothing is prepended (no Positioned), and most leaf rows have no `.it` of their own, so their
chain is the row's `InheritsFrom`. `client { }` blocks (RenderItem) are never read. Generate every row's list with:

```bash
python -I scripts/re/ot_components.py --items --json data/item-types.json
```

`scripts/dev/extract-data.ps1` runs it (entry `item-types`; a change to the output bumps the entry's version).

| Type | Components the reader reaches |
|---|---|
| Wand1 (any weapon) | Base, LocalStats, Mods, AttributeRequirements, Weapon, Quality, Sockets |
| FlaskLife1 / FlaskMana1 | Base, Quality, LocalStats, Charges, Mods, Usable, Flask |
| Fireball / Arcane Surge (gems) | Base, SkillGem, Quality |
| Glyph1 (quest item) | Base, Quest |
| CurrencyIdentification | Base, Usable, Stack |

## 2. The item data

**Issue #75 evidence scope:** the defence, requirement and flask native captures and earlier test counts below
belong to the original LocalExile revisions. Standard/Ruthless comparisons describe that source checkout, not
game-mode support or a fresh client run on this branch. See [the port record](../dev/issue75-port.md) for current
automated validation and exclusions; source `artifacts/issue75-completion/` evidence remains in LocalExile.

Write `0x1421ec1d0` → `0x1421ec010`, read `0x1421ec2c0` → `0x1421ec390`. Wrapped in a **u16 big-endian length**; inside,
everything is little-endian (memcpy):

```
u32  type hash (BaseItemTypes HASH32)
u16  flags            bits 0-8 → item +0x28..+0x2f (bit 4 also to the ctor 0x1421ebf10); bit 9 → u64 follows
...  each component's data, in the type's order: vtable slot 5 (+0x28) reads, slot 4 (+0x20) writes
u16  only for the four type hashes in 0x1421ebe90 (0x192c7aab, 0x365f206a, 0x503da850, 0x9aff2df6)
```

Item component vtables (slot 4 write / slot 5 read). The ones with a bare `ret` (`0x1400f0f00`) carry nothing:

| Component | Vtable | Read | Data |
|---|---|---|---|
| Base | `0x1435e0168` | `0x1421e69c0` | u16 ItemVisualIdentity **HASH16** (unknown → "Attempt to set visual identity to NULL"), u8 +0xc0, u8 +0xc7 flags, u16 flags (low bit0 → u8, bit1 flavour text u16, bit2 wide string, bit3 u32, bit4 wide string, bit5 u8 n × (u32, i32), bit6 nested item, bit7 u8 n × (u32 item type, compact); high bits 0-2 → +0xc4, bit4 → 2 × u32), u8 +0xc6 the influences (§2f) |
| Mods | `0x1435df838` | `0x1421df6e0` | u8 +0x2b8 (item level), u8 +0x2bc (the level requirement, kept as sent: §2d), u16 flags (§2f: bit1 mirrored, bit2 split, bit4 relic, bit5 synthesised, bit6 incubator u16 u16 u8 and then bit4's u8, bit7 → 0x1c8 record `0x1424b70c0`), base magic properties `0x142201d70` (rarity, identified, mod lists: §2a), u8, u8 |
| Quality | `0x14363acd8` | `0x1424aec50` | u8 |
| Charges | `0x143639a48` | `0x14249d4b0` | signed compact (`0x141e490e0`) |
| Stack | `0x143634b10` | `0x1423f4b30` | u16 stack size (§4a) |
| SkillGem | `0x143637298` | `0x14246eb00` | i32 experience (-1: one level over the cap), u32 +0x3c the variant, u8 +0x10 flags (bit 2: level locked), u16 +0x40 |
| Sockets | `0x1435df9a0` | `0x1421e2e20` | u8 = bit0/bit1 (special: nothing more) \| socket count << 2 \| link group count << 5; per socket u8 colour << 1 \| has gem, then the gem's item data **without** the length prefix; then a u8 per link group (its socket count). Colours: 1 red, 2 green, 3 blue, 4 white (all seen live 2026-10-26) |
| Armour | `0x14363ad40` | `0x1424affc0` | u8 shared roll 0..31 (stored at +0x28); 32 (0x20) is a special minimum-base flag, which also sets +0x2a |
| MapKey | `0x14363abd8` | `0x1424ae7c0` | u8 flags: bit0 → u32, bit1 → u16, bit2 → u8; zeros carry no override, so a generic `MapKeyTierN` base supplies its tier |
| LocalStats, AttributeRequirements, Weapon, Usable, Flask, Quest, Shield, RenderItem | `0x14363ab88`, `0x1435dfb80` … | `ret` | nothing |

Minimal item the server sends: flags 0, Base with only the visual identity, Mods identified and normal with no mods.

**Base defences:** an `ArmourTypes.dat` row is keyed by `BaseItemTypesKey` (the BaseItemTypes row) and gives
`ArmourMin`/`ArmourMax`, `EvasionMin`/`EvasionMax` and `EnergyShieldMin`/`EnergyShieldMax`.
`Item.DefenceBaseRoll` stores the Armour component's raw byte, shared by all three defences.
Ordinary values 0 through 31 select `round(minimum + (maximum - minimum) * roll / 31)`.
The interpolated base rounds halfway up before local modifiers, separately from the final item rounding.
Raw wire value 32 sets the native special flag and uses minimum bases; it is not a maximum roll.
The flag hides tooltip defences (`0x1416d8c67`, `ItemViewDefencesNotShown`) but does not prevent Sacred Orb use.
It survives wire/save round trips but is never generated for ordinary new items.
Values above 32 are refused by the server model instead of silently aliasing another roll.
The native derived byte at Armour `+0x29` is a display percentile, not an independent saved roll.
Reader/writer: `0x1424affc0` / `0x1424aff60`; base getters: `0x1424af890`, `0x1424af910`, `0x1424af990`.

Fresh armour items from `ItemCatalog.Create` sample a uniform ordinary roll once.
This is the local server's generation policy; the official server's distribution has not been measured.
Drops, rewards, vendor stock and development items share that factory.
Saving, loading, moving, socketing and affix changes preserve the existing roll.
Sacred Orbs deliberately reroll it through the normal currency path described below.
Legacy saved items missing the field keep their original zero/minimum base; loading never draws a fresh base roll.
`/base_defence [0 to 31]` shows or sets the held armour item's roll for development checks.
`EquippedBaseDefences` counts active body armour, helmets, gloves, boots and the active offhand shield.
An item disabled by requirements contributes nothing; the inactive weapon set and backpack contribute nothing.

For armour, evasion and ES, each item contributes
`round((rolled base + local flat) * (1 + local increases / 100) * (1 + effective quality / 100))`.
The final item value rounds halfway up once, before summing equipment or applying character-wide increases.
The reference is Path of Building commit `16de4b82d57f1c0de6eb40f37143c32d4da36a02`,
[Item.lua](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b8/src/Classes/Item.lua#L2345)
and [Common.lua](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b8/src/Modules/Common.lua#L709).
Flat local stats are `local_base_physical_damage_reduction_rating` and `local_base_evasion_rating`.
Evasion also counts `local_evasion_rating_and_energy_shield`.
Typed local increases add to the applicable armour/evasion, armour/ES, evasion/ES and triple-defence increases once.
Effective quality is the saved Quality byte plus `local_item_quality_+`.
`local_quality_does_not_increase_defences` suppresses this multiplier; maximum-quality modifiers do not add quality.
Global item/passive/aura modifiers and Iron Reflexes continue through the shared character-pool calculation afterward.
Equipment changes and quality currency on worn items refresh those pools and send the resulting panel stats.
ES uses that same final half-up rounding instead of truncating between local increases and quality.
`ItemDefences.Calculate` supplies the shared local item values to equipment.
`local_no_energy_shield` and `local_unique_tabula_rasa_no_requirement_or_energy_shield` force this item's ES to zero,
overriding its base, flat/increased mods and quality without removing armour, evasion or another item's ES.
The native ES getter `0x1424af430` tests these flags before the base, flat modifiers, increases or quality.
Supported equipment ES-to-Life conversion and conditional ES increases are described in §2c.
Sacred Orbs reroll the saved shared base roll through the currency handler (see "Sacred Orb" in §4).

**Shield-specific character increases:** retain the active shield's final local AR/EV/ES as separate inputs.
`shield_armour_+%` (24) increases all three shield defences despite its name.
Typed shield increases are `shield_physical_damage_reduction_rating_+%` (495),
`shield_evasion_rating_+%` (496) and `shield_maximum_energy_shield_+%` (492).
These add to the applicable global increase bucket on the shield contribution only, before global more multipliers.
They do not change item tooltips, other equipment, independent flat defences or ES converted into Life.
`combined_shield_defences_+%` (9865) is a derived stat, not a second additive source for stat 24.
The native combined calculators are `0x142724c50`, `0x14272f680`, `0x142727dd0` and `0x14252f6a0`.
Iron Reflexes applies both typed shield armour/evasion increases to converted shield evasion, counting stat 24 once.
`minions_get_shield_stats_instead_of_you` and `gain_no_defences_from_equipped_shield` remove the shield's local
contribution from player pools as well as ES-to-Life conversion.
Full minion redirection of other shield modifiers and Devotion-dependent shield scaling remain separate mechanics.
Fractional contributions survive slot scaling and Iron Reflexes until one final half-up round for each rating.
Native final rounding is at `0x142732ee7` (AR), `0x14272a59b` (EV) and `0x14252c6e7` (ES).
The ES path calls signed rounding helper `0x142513810`; it no longer truncates an ordinary final pool.

Variable-base native checks verified Plate Vest rolls 0, 8, 16 and 31 as 19, 21, 23 and 27 armour.
Raw 32 arrived as stored roll zero with special flag one and minimum armour 19, including after relog.
Changing back to an ordinary roll cleared that native flag; ordinary roll 31 also survived relog unchanged.
With roll 16, 50% local increase and 20 quality, native tooltips showed Plate Vest armour 41,
Rawhide Gloves evasion 29 and Simple Robe ES 31.
The character received those contributions through the same pools, including after quality crafting and relog.
Newly created items had different ordinary rolls, while moving and crafting retained their saved rolls.

Native checks used an isolated level-20 Duelist with 23 Dexterity and no other equipment or passives.
A Scale Vest with +10 local armour/evasion and 26% local increased armour/evasion showed 47/47 before quality.
Twenty Armourer's Scraps on the worn vest raised its saved quality to 20 and its native tooltip to 56/56.
The Defence panel and server pools updated to 56 armour and 73 evasion, including the character's base and Dexterity.
Adding `local_item_quality_+` 10 kept the saved byte at 20 and showed 30% quality and 61/61 in the native tooltip.
The alternate-quality prohibition removed only quality's defence multiplier; removing mods and unequipping cleared
their respective contributions.
A modified shield contributed 18 armour only in the active set; the native weapon-swap key restored it from zero.
With 56 armour, a 100-point physical test hit dealt 90 damage after the existing 10% armour reduction.
Automated tests also cover global modifiers, disabled equipment, Iron Reflexes, per-item rounding and large totals.

## 2a. Mods: rarity and mod lists

The Mods component's base magic properties are ObjectMagicProperties, read by `0x142201d70` (decompiled:
`Tools/re/out/omp.c`). Code: `MagicProperties.cs` (also a monster's, [14](14-monster-packs.md) §5), Mods.dat in the
Gateway's `ModCatalog.cs`.

```
u16  flags   bits 0-2 rarity (0 normal, 1 magic, 2 rare, 3 unique), bit 3 identified,
             one bit per mod list (below), bit 7 custom name, bit 8 Words.dat name, bit 9 alternate quality
u8           (+0x7a, the fractured explicit count; 0 on monsters)
bit 9:  u8 AlternateQualityTypes row, u8
bit 4:  implicit list      bit 6: enchant list      bit 10: scourge list      bit 11: hidden list
identified only:
  bit 7: u32 length + UTF-16 name, else bit 8: u8 n × u32 Words.dat HASH32 (a rare name: prefix, suffix)
  bit 5: explicit list
```

A list is u8 count + mods. A mod (`0x142430380`): **u16 Mods.dat HASH16**, then one signed compact value
(`0x141e490e0`) per stat of the row. The client counts the stats itself (`0x142430710`, StatsKey1..8 up to the first
empty one; no Mods.dat row has a gap), so the count must match. An unknown hash throws "Deserialised a mod that we don't
recognise". After reading, `0x1421ff3c0` adds the "magic" / "rare" tag and gives each list to `0x1421ff8f0` with its
kind (implicit 1, explicit 2, enchant 3, scourge and hidden without one).

What the tooltip shows (live, 2026-10-20, an Iron Ring with `/item_mod <list> IncreasedLife1`): implicit under the
properties, enchant above the implicits, explicit below a separator, scourge as orange lines among the explicits
(Hellscape), hidden not at all. Magic items get their name from the affixes (Healthy Iron Ring); a rare item shows its
two Words.dat words above the base name (Corruption Gyre / Iron Ring; verified 2026-10-20 that the key is HASH32), or the
base name without them. Unidentified items show "Unidentified" and their implicits only.

New items roll their BaseItemTypes `Implicit_ModsKeys` (Driftwood Wand: `SpellDamageOnWeaponImplicitWand1`, 8-12%).
Saves keep rarity, identified and each mod by Mods.dat Id, list and values; an item saved before mods gets its type's
implicits again. Dev commands on the held item: `/item_mod [implicit|explicit|enchant|scourge|hidden] <Mods row, Id or part>
[values…]` (missing values rolled, the list defaults from GenerationType), `/item_mod set [list] <mod> [values…]` (replaces
an existing mod's roll and keeps the item's other mods), `/item_mod clear [list]`, `/item_mods`,
`/rarity <normal|magic|rare|unique> [unid]`.

## 2b. Rolling magic and rare items

### Essence crafting (2026-10-25)

`EssenceCrafting.cs` reads the existing `tables/Essences.json`: BaseItemTypesKey identifies
the consumed currency, and each equipment-class ModsKey resolves the exact guaranteed
Mods.dat row. Quivers use Display_Quiver_ModsKey (there is no separate Quiver_ModsKey in
this schema); Rune Daggers use Dagger, Warstaves use Staff, and thrusting swords use
OneHandThrustingSword. All 105 craftable essences are included, including the four tier-8
essences and tier-0 Essence of Desolation. Remnant of Corruption has no equipment mods
and is deliberately not handled as item crafting currency.

Normal equipment becomes identified rare. Tiers 5–8 and Desolation can also reforge rare
equipment; magic, unique, corrupted, mirrored, incompatible classes/domains and the four
standard metamods are refused without consuming currency. The guaranteed mod bypasses
ordinary spawn weights and item-level requirements. Random affixes retain their normal
base/tag weights and use min(item level, ItemLevelRestriction), when a restriction is set;
the item's actual level stays unchanged. This is a cap on random properties, not a maximum
target item level. Desolation's cap is 15 in this client's table.

`ItemRolls.Roll` inserts the guaranteed affix before filling remaining prefix/suffix slots.
Family conflicts are excluded, the usual three-prefix/three-suffix limits apply, fractured
mods keep their values and positions, and a conflicting fracture refuses the craft. Existing
implicit/enchant mods, sockets, gems, links and quality remain. The random affix-count
distribution is the existing server distribution documented below, not a newly verified
official distribution. Conditional combat effects still depend on the server's implementation
of the corresponding stats; this change supplies their exact item modifiers.

UseItemOn dispatches essences through the existing 0x71 / Shift 0x72 currency path,
including currency in the Essence stash tab, item targets in stash/inventory, atomic saves,
one-unit consumption and 0xbf item replacement. Failures never consume a stack unit.
The extraction manifest explicitly checks Essences.json, and GameDataFiles warns if absent.

Tests exercise every essence across supported equipment classes at item levels 1 and 100,
raw-table class mappings, value ranges, random-mod caps, rarity/protection restrictions,
fractures/metamods and repeated use directly from the Essence tab. Real-client application
awaits the user's test after Gateway restart.

References: [PoEDB essences](https://poedb.tw/us/Essence),
[Desolation](https://poedb.tw/us/Essence_of_Desolation),
[PoE Wiki mechanics](https://www.poewiki.net/wiki/Essence). Prefer this client's extracted
rows over later website data when modifier values differ by game version.

### Ordinary random affixes

`ItemRolls.cs`. An affix can roll when its Mods.dat row has the base's BaseItemTypes **ModDomain** (1 items, flasks
their own), GenerationType 1 (prefix) or 2 (suffix), Level at most the item level, is not essence-only, and has a
weight above 0 for the item's **tags**: the first of its SpawnWeight tags the item has gives the weight, the first
matching GenerationWeight tag multiplies it by weight / 100. An item's tags are the `tag = "…"` lines of the Base
sections along its `.it` chain (`ot_components.py --items` writes them to `data/item-types.json` as `tags`)
plus the row's own TagsKeys: Driftwood Wand → default, weapon, onehand, wand, ranged, one_hand_weapon,
wand_can_roll_caster_modifiers.

Each affix is drawn by weight from all allowed mods (prefixes and suffixes compete), skipping mods that share a
ModFamily with one already chosen. Magic: 1-2 affixes, at most one prefix and one suffix. Rare: 4 (7/12), 5 (4/12) or
6 (1/12) affixes, at most three of each, and a name: one word from Words.dat list 1 (prefixes) and one from list 2
(suffixes, with a leading space), drawn by the same tag weights. Values are rolled uniformly in each stat's range.
The rare caps are `system_settings` (`rare_max_prefixes`/`rare_max_suffixes`, and the jewel pair): lowering a cap
reduces the count to the caps' sum, and raising the sum above six adds the excess to the count (4/4 gives 6-8).
These counts and chances are our own; the game's are not known.

Drops: equipment from a normal / magic / rare / unique monster is magic 25 / 35 / 40 / 30% and rare 5 / 10 / 25 / 50%
of the time, at the monster's level as item level, and drops **unidentified**. Dev: `/item <type> [count] magic|rare`
(at the character's level), `/roll <magic|rare> [item level]` on the held item.

**Identifying** (C→S 0x71, §4): right-click a Scroll of Wisdom, then click an unidentified item. The server marks it
identified, sends it again (its explicit list and name) and takes one scroll from the stack; an identified target
changes nothing. Nessa's **Identify Items** identifies the whole main inventory for free ([17](17-npcs.md) §3).

## 2c. Mods on the character

`ItemModStats.cs`. The items in the worn equipment rows (Inventories.dat 1-10: body armour, the first weapon set,
helmet, amulet, rings, gloves, boots, belt; not the second weapon set or the flasks) count, with every mod list. The
client does not let the player equip an unidentified item, so only identified items reach these rows. A stat that
Stats.dat marks **IsLocal** belongs to its item; every other stat is the character's: it is added to the allocated
passives' stats (`CharacterStatSources`), so it reaches the pools (`PassiveBonus` → `CharacterStats.For`: life, mana,
attributes, resistances, block, suppression, speeds, crit, accuracy …) and the stats the client shows (0x1b3), the same
way a passive's would. They are sent again when equipment changes (`SendEquipment`).

Local mods on the active main hand (row 2 after weapon-set swapping) change the weapon profile through the shared
`ItemWeaponStats` calculator (also used by `ItemWeapons`): `local_physical_damage_+%`,
`local_minimum/maximum_added_physical_damage` and item quality on the weapon's physical damage (quality is a separate
× (1 + quality/100) multiplier after the local increases, the post-3.25 rule; it does not touch the elemental/chaos
added damage), `local_attack_speed_+%` on the attack rate (like a more, before the global increases),
`local_critical_strike_chance_+%` on the WeaponTypes base critical chance, `local_accuracy_rating` as flat accuracy,
and the local added elemental/chaos damage (`local_minimum/maximum_added_fire/cold/lightning/chaos_damage`, the
elemental ones raised by `local_elemental_damage_+%` and `local_attacks_with_this_weapon_elemental_damage_+%`) in every
hit the weapon makes. The armour, evasion and energy shield side is in "Base defences" above. Verified live 2026-10-20:
+20 maximum Life, +30% fire resistance and +25 strength on a ring, a local attack speed mod on the wand, and spell
damage on the wand raising Fireball's damage (11-skills.md §9).

**Supported special defence rules:** `Program.Equipment.ConvertEquipmentEnergyShield` converts active armour items'
final local ES into base Life before character-wide Life increases.
`gain_maximum_life_instead_of_maximum_es_from_armour` selects 100% and takes precedence over
`gain_maximum_life_instead_of_maximum_es_from_armour_at_x%_of_the_value`.
Each item's percentage contribution truncates separately before summing (native consumer `0x142523ab0`).
Conversion removes all equipment ES even when the Life gained is only a percentage of its value.
Independent global and aura ES remains ES; global ES increases do not scale the converted Life.
Shields excluded from the player's defences also contribute no converted Life.
Inactive equipment and backpack items contribute neither ES nor converted Life.

`local_maximum_energy_shield_+%_if_item_corrupted` adds to the character-wide ES increase bucket only while its active
source item is corrupted (`ItemModStats.Global`, matching PoB's item-condition modifier handling).
It does not change the local tooltip value or increase converted Life.
The hidden zero-ES modifier above also removes attribute requirements during equipment activation, as does
`local_no_attribute_requirements`; both retain the level requirement (§2d).
Automated tests cover these rules, source clearing, active shields and inactive equipment.
Historical native controls recorded at `0bae37a` verified 49 + 13 + 7 local ES becoming 69 base Life at 100% and 33
at 50%, together with independent ES, global Life scaling and shield swaps.
Controls recorded at `c9060e4` kept the corrupted robe's tooltip at 49 ES and converted Life at 1,314 while independent
ES increased from 206 to 216.
These are historical feature checks; the merged branch still needs fresh real-client validation.
Other unique and conditional defence mechanics remain separate work (#60).

## 2d. Requirements and inactive items

**Attribute requirements are computed by the client from the item type and the mods; the level requirement is sent.**

- **Attributes** (`0x1424a6d90`, per attribute, read by the equip check `0x1402ce5f0`): the type's
  AttributeRequirements value plus the item's flat requirement stats (`local_strength_and_intelligence_requirement_+` …),
  × (100 + `local_attribute_requirements_+%` + the attribute's own %) / 100, at least 0;
  `local_no_attribute_requirements` and `local_unique_tabula_rasa_no_requirement_or_energy_shield` make it 0. The check
  compares it × (100 + the player's `global_item_attribute_requirements_+%`) / 100 with
  `virtual_*_for_requirements`, and the client refuses the move itself.
- **Level** (Mods component +0x2bc). **This one is sent:** the Mods reader `0x1421df6e0` stores the byte it receives,
  and the client only recomputes it (`0x1421e03d0`) when an item's mods change on its side (crafting paths
  `0x1421e0600`, `0x1421e00d0` …). The tooltip shows that byte, and the client's use check `0x1421eca50` refuses an
  item when it is above the player's level (Player +0x1ac, derived from the experience). With 0 (as we used to send)
  nothing shows and nothing is checked. The client's formula, which the Gateway uses (`ItemModStats.LevelRequirement`,
  sent through `Item.LevelRequirementOf`): 0 for types with the flag at type data +0xcb; else
  `local_level_requirement_override` if non-zero; else max(the type's level, Mods.dat Level × 4 / 5 for every mod in all
  five lists) + `local_level_requirement_+`, at least 0. Gems send 0: their requirement comes from the SkillGem
  component (`0x14246fff0` for the level, `0x14246fc40` per attribute; [19](19-gems.md) §2). The server also refuses
  0x2a / 0x4a into equipment rows below it (`LevelTooLow`).

**Inactive items** (`ItemModStats.Inactive`): a worn item whose requirements stop being met (an attribute item taken
off, a lower level) stays on but does nothing: no mod stats, no local weapon mods, no gem skills (their skill ids are
kept). After each change, items that fall short are switched off one at a time, counting the attributes of every
active item including their own, until all active items are satisfied; an inactive item comes back when the character
and the other active items meet its requirement without its own attributes.
Requirements use the complete class/passive/active-equipment flat bucket scaled by the generic attribute increases.
An inactive item's own flat and increased attributes are excluded when testing whether it can return.
An active item may retain its own flat and increased attributes under the existing staged client policy.
Each activation/removal step recomputes final attributes through `AttributeModifiers`;
no final value becomes a raw source.
Weapon swaps count only the hands' current rows, and raw attribute development overrides replace their own source key.
Calculated zero attributes remain zero in the panel and requirement stats, including after pool record clones.
Automated activation, retention, cascading removal, source clearing and swap checks pass.
Historical controls at `60044ad` reduce and restore a Marauder's attributes while a Plate Vest remains equipped.
Its 19 Armour disappears below the raw 12-Strength requirement and returns when the requirement is met again.
Native all-zero requirement values, source clearing and relog are also verified.
Own-item percentage retention, cascading equipment and weapon-set combinations retain automated coverage.
See [attribute calculation and rounding model](11-skills.md#4-the-default-attack).

Each recount resolves the whole class/passive/active-item flat bucket with the matching typed and all-attribute
percentage increases. Pools and equipment qualification call the same `CharacterStats.ResolveAttributes`; rounding
an individual item's contribution first would change activation thresholds. The old tuple-based solver overload
remains available, while production uses a pure whole-active-set callback. The existing bounded reactivation then
one-at-a-time deactivation phases are retained. Signed circular dependencies can oscillate between evaluations;
their official policy remains unverified, and no fixed-point or stabilization rule is invented here.

A socketed gem's unmet requirement raises its item's requirements (`EquipmentStatSources`, `SocketedGemRequirements`,
[19](19-gems.md)): the item goes inactive with every gem in it, as a worn item the character is too low for does, and
a use that still arrives is refused (`GemRequirementsUnmet`/`GemDisabled`). Gem admission reads the completed pool
attributes and explicit `virtual_*_for_requirements` overrides. An inactive host contributes no mods or usable gem skills.
An inactive main-hand weapon is absent for attacks and weapon-shape conditions: the default attack uses the class's
unarmed hit (`WeaponFor`) and the shape passives read as unarmed (`WeaponProfileFor`).

Open: the client still draws an inactive item on the character (a red wand stays in hand), and the server leaves it
where it is. Historical gem-requirement checks also disabled the host; that behavior is superseded by the individual
gem gate. Seventeen isolated controls in each of Standard and Ruthless verify percentage-driven item activation,
deactivation/recovery and active weapon-set persistence after relog. Normal Fireball input succeeds at Intelligence
68, is refused at 62 against its 67 requirement, and succeeds after recovery, while the qualified Plate Vest retains
19 Armour. These controls use actual native mod identities on isolated supplied items; unique acquisition and
signed circular-dependency policy remain unverified. See [19](19-gems.md), Requirements.

**Stale stats:** S→C 0x1b3 only sets the stats it lists, so every full player stats send also sends 0 for each stat
sent before that is gone now (`PlayerStatsWithCleared`).

## 2e. Unique items and Mageblood

The client data has a unique's name (`Words` wordlist 6) and its artwork (`ItemVisualIdentity`, linked to the word by
`UniqueStashLayout`), but **no table maps a unique name to its mods**: unique mods are ordinary Mods.dat rows with
GenerationType 3 and an empty Name. The server therefore keeps each unique's mod list (`UniqueCatalog`): base type,
the mod rows and their value ranges. Mageblood and Crown of Eyes are curated from poedb for 3.29; the others are
generated by `python scripts/re/unique_items.py --pob <Path of Building folder>` from `src/Export/Uniques/*.lua`.
The generator takes each unique's last variant, the current one: its base type (a unique's base can differ per
variant, Tulfall's is an Opal Wand now), its implicits when they differ from the base type's (`Definition.Implicits`
replaces them), its explicits, and `Definition.Ranges` for a current roll that differs from the Mods.dat row.
It writes no item level: a definition uses 86, and drops use their own. Item text the export has instead of a mod id
resolves through `ModTextMap.lua` when one unique mod's roll fits; the rest is left out and listed by the run (the
timeless jewels and Pure Talent, which have their own code, and about 40 lines of other uniques).
Uniques without a `Words` wordlist 6 row are left out, since nothing could name them on the client, and so is
Disintegrator, whose Maelstrom Staff the client does not have.
The name match ignores curly apostrophes and a leading "The" (`UniqueCatalog.NameKey`).
`/item <unique name>` creates it with the
name words and artwork, unique rarity and the fixed mods; a base type's implicits are rolled as usual unless the
unique has its own (for Mageblood the Heavy Belt's +25-35 Strength). A unique's alternate-art row in `UniqueStashLayout` is not its art. Checked live 2026-10-26: the client shows the Mageblood name, unique art
and full tooltip. Chancing and most uniques' own mechanics remain open (#60).

**Mageblood's mechanic** (`Program.Mageblood`): while the belt is worn (and its requirements met), the leftmost N (2-4,
from `num_magic_utility_flasks_always_apply`) magic utility flasks in the flask bar constantly apply their effects. The
stats are added where a running utility flask's stats are added (pools, the client's stats, phasing), scaled like a use
(the flask's own mods and the character's flask effect/duration), with no charges or timer. Each such flask keeps its
buff on the client with no duration (0x1c1 with the flask-bar cell and without bit 1), put on and taken off by a ticker
(`RunUniqueBeltEffects`, about 4 a second) when the belt or the bar changes, on entering an area and on death. Magic utility
flasks cannot be used while it is worn: the client enforces the item mod and the server refuses the use as a backstop.
The character's stats are re-sent when the flask bar changes while it is worn (`PlaceItem`, `HeldOntoItem`, `PickUpItem`)
and on equipment changes. Live 2026-10-26: placed flasks applied at once, and a flask key did nothing.
The running effects and the permanent ones are combined before the strongest-per-(effect, stat) pass: a flask that was
already used when the belt went on counts once, not twice, while different effects still stack; the running effects keep
their own duration and stay after the belt is removed.

Not modelled yet: the "Magic Utility Flask Effects cannot be removed" backstop (nothing in the server removes a flask's
effect), the Foulborn rightmost variant, and Enkindling/instilling. How the client draws a flask buff without a duration
has not been seen live.

**Headhunter's mechanic** (`StolenMods`, `Program.UniqueBelts`): killing a rare monster with
`gain_rare_monster_mods_on_kill_ms` on the character adds the stats of the rare's rolled affixes (not its rarity's
`MonsterRare` mods) to the character's stat sources, so pools, speed, damage and the panel read them as they read gear.
By the project's rule, not the mod's 20 s: each different modifier is one stack lasting 60 s, and getting it again resets its
time without counting twice. One `stolen_mods_buff` (BuffDefinitions 269) shows them, with the stack count and the longest
time left, replaced at each change by the same ticker. Death and an area change end them. A stat with no consumer on the
character does nothing. `damage_+%_vs_rare_monsters` scales the rolled hit of melee, area and projectile contacts
(`WithDamageVsRarity`): (100 + global increased + the belt's roll) / (100 + global increased), so the hit's other increases are not in
the sum (slightly above the game's). The loss on an area change is a limit: the game keeps them. Replica Headhunter is not modelled.

## 2f. Item states

What an item is besides its mods, and where each state travels.
Fields come from the client's readers and its debug dumps of the two components, Base `0x1421e7570` and Mods
`0x1421e1190`, which print each field's name.
A refusal code near a check names it too: the code is a ClientStrings row + 1 (`0x1762` is
`ItemErrorFailedRerollVariableDefence`, row `0x1761`).

| State | Where | Server |
|---|---|---|
| Corrupted | Base `+0xc7` bit 0 | `Item.Corrupted` |
| Influences | Base `+0xc6`, one bit per influence | `Item.Influences` |
| Mirrored | Mods flags bit 1, `+0x2df` (`duplicated`) | `Item.Mirrored` |
| Split | Mods flags bit 2, `+0x2e0` (`split`) | `Item.Split` |
| Synthesised | Mods flags bit 5, `+0x2e5` (`synthetic`) | `Item.Synthesised` |
| Fractured | magic properties u8 `+0x7a` (`+0xb2` of Mods): the count of fractured explicits, which come first | `Item.FracturedCount` |
| Eater / Exarch | the implicit list: mods of GenerationType 29 / 28, no state of their own | an ordinary implicit |
| Crafted | the explicit list: mods of Mods.dat Domain 9 (`BenchCrafting.IsCrafted`) | an ordinary explicit |
| Enchanted | the enchant list (§2a): GenerationType 10 and other enchantment rows | `ModSection.Enchant` |

**Influence bits.** Bit n is Influence n of `InfluenceTags.dat` and `InfluenceExalts.dat`: 0 Shaper, 1 Elder, 2 Crusader,
3 Redeemer, 4 Hunter, 5 Warlord.
Each bit was sent alone and the client's Ctrl+C text named that influence (live 2026-10-30; below).
The client tests `+0xc6` for zero in its crafting checks (`0x141d57ea0` "Item has influence", `0x141f85860` "Item has no
influence") and reads each bit when it draws the item (`0x1415703a0`).

**Influence mods** are ordinary prefixes and suffixes behind a tag per item class: `InfluenceTags.dat` gives 24 classes a
tag per influence (boots_shaper, ring_eyrie, body_armour_adjudicator …), and 1,446 Mods.dat rows spawn only with one.
`ItemRolls.Pool` adds those tags for an influenced item, so every roll, bench reroll and affix currency can reach them.
`shaper_item` and `elder_item` exist in Tags.dat, but no mod's spawn weights use them.

**Other Mods flags.** Bit 3 `disable_mirrored_visuals` (`+0x2e1`), bit 4 relic (`+0x2e3`, a foil look) with a
RelicItemEffectVariations row as a u8 after the incubator data, bit 6 incubator, bit 7 the 0x1c8 record `0x1424b70c0`, bit 10
vestigial (`+0x2e7`, `ItemErrorCannotApplyToVestigialItem`).
Bits 0, 8 and 9 are unnamed.
`+0x2e2` is no wire field: it marks the client's lazily built caches (`0x1421e1930`).

**Other Base `+0xc7` bits.** Bit 2 locked to character, bit 3 locked to account (`0x1421e7570`); bit 5 changes what the
writer `0x1421e64a0` sends.

Display kinds of a mod line (`0x1424df5c0`): 2 crafted, 3 fractured, 4 mutated (Foulborn), 7 divergent.

`/item_state [state …]` switches corrupted, mirrored, split, synthesised and each influence on the held item, and `clear`
removes them all (`ItemStates`).
They are saved with the item in characters and stash tabs; a save from before reads as none of them.
Uniques that PoB marks Shaper or Elder Item carry it in their definition (25 of them).
`ItemRolls.Fracture` refuses influenced and synthesised items (`ItemErrorCannotFractureSpecialItems`, "Cannot Fracture
Influenced or Fractured Items", and poewiki for synthesised); a split item stays eligible.
Tests pin the bytes, the saves, the tags and the refusals.

Live 2026-10-30, on a rare Iron Ring with `/item_state`, read from the client's Ctrl+C text and tooltip:

- Influences show as icons in the tooltip header and an overlay on the item's art; the tooltip has no line for them, and
  the copied text says "Shaper Item", "Elder Item", "Crusader Item", "Redeemer Item", "Hunter Item", "Warlord Item".
- Split and Corrupted are tooltip lines (Corrupted in red), and Mirrored too.
- Synthesised renames the base ("Synthesised Iron Ring") and copies as "Synthesised Item".
- A Domain 9 mod copies as `Master Crafted Prefix Modifier` with its own tooltip colour, and an enchant as `(enchant)`.
- A Fracturing Orb on a Shaper ring was refused, leaving 3 orbs; the same ring without the influence fractured, leaving 2.
- The ring's copied text was the same after a relog and after a Gateway restart, and a fractured ring kept its mod.

Tooltips only show while the client has focus, and Alt pins one; a check by screenshot has to keep both in mind.

Nothing in play creates these states yet: Conqueror's Orbs, Awakener's Orb, Beastcrafting and synthesis are follow-ups, and
so are the mechanics that read influence (Spirit Charges, Elder/Shaper conditions, `PanelStats`), influenced vendor sets,
foil, Foulborn and vestigial items.

## 3. Inventories with items (S→C 0xc0) and changes (S→C 0xbf)

0xc0 ([05](05-inventories-and-ui.md) §2) carries the items after the inventory header: compact count, then per item
(`0x1421f5680`) **u32 id, u8 x, u8 y, item**. Ids identify items within an inventory (the client keeps a map and drops
duplicates); the Gateway gives every item a character-wide id and keeps it when the item moves.

The 0xc0 **flags** byte: bit 0 (+0x148) = Inventories.dat column 3, a single-item slot drawn as one box (equipment, the
cursor; without it an equipment slot is a grid of cells); bit 1 (+0x149) = column 4, the character's own slots
(equipment, flask bar). With bit 1 the client checks what may go in: the slot's item classes (a wand is red over the body
armour slot) and the attribute requirements (§6).

**S→C 0xbf** changes one inventory (dispatcher case 0x34, class `0x1435dbd28`, write `0x1421a37f0` → `0x1425079a0`,
handler `0x141d62550` → `0x1421f5160`):

```
u8   manager (0 = the player's)
u32  inventory id (unknown → fatal "Got an inventory set change for an inventory that isn't in the set")
u32  revision (→ inventory +0x1e8)
u32  removed count (0xffffffff = remove everything) + that many u32 item ids
u32  added count + (u32 id, u8 x, u8 y, item)
u8   1 → 4 × u32 follow (the 0xc0 flag-bit-4 block)
```

Removal runs before addition, so "remove id N, add id N" replaces an item in place (sockets). The other inventory
messages: 0xc1 removes an inventory ("…inventory set remove…", class `0x1435c0898`: u8 manager, u32 id; the Gateway unloads stash tabs with it, [18](18-stash.md) §3), 0xc2 sets
+0x160/+0x161 ("…permissions change…", class `0x1435dc660`: u8, u32, u8, u8).

## 4. Moving items (C→S)

All senders sit together at `0x14215f300`… and queue through `0x14216e940`.

| Id | Bytes | Class / write | Meaning | Server answer |
|---|---|---|---|---|
| 0x28 | 13 | `0x1435bf5f0` / `0x141fa0d00` | click on an item: pick it up. u32 inventory id **with HNC side-channel bits 21-29** (mask `0xc01fffff`, reader `0x141fa0ea0`), u32 item id, u8 (3), u8, u8 | 0xbf remove from the inventory, 0xbf add to the cursor (row 12) at (0, 0) |
| 0x2a | 18 | `0x1435bf710` / `0x141f8feb0` | put the held item down: u32 inventory id, u32 x, u32 y (0, 0 for equipment), u8 Shift (1: one of a held stack, §4a), u8 (3), u8, u8. Sent for a click while holding on a cell; a click on an item sends 0x4a | 0xbf target (remove the displaced item, add the held one), 0xbf cursor (remove the held one, add the displaced one) |
| 0x30 | 16 | `0x1435c0838` / `0x141f90650` | put the held gem into a socket: u32 inventory id, u32 item id, u32 socket index, u8, u8 | 0xbf cursor, 0xbf replacing the item |
| 0x2f | 17 | — | take a socketed gem onto the empty cursor: u32 inventory id, u32 item id, u32 socket index, 3 bytes | 0xbf replacing the item, 0xbf cursor |
| 0x31 | 14 | not traced | the **"+"** on a socketed gem ([19](19-gems.md) §4): u32 inventory id, u32 item id, u32 socket index | 0xbf replacing the item with the experience at the next level's threshold (the client derives the level), the 0x20 Actor update |
| 0xd7 | 15 | not traced | a right-click on a socketed gem toggles its **level lock** ([19](19-gems.md) §4): u32 inventory id, u32 item id, u32 socket index, u8 locked | 0xbf replacing the item with SkillGem +0x10 bit 2 set or cleared |
| 0x29 | 2 | `0x14316c930` (id only), sender `0x140117b80` | drop the held item into the world | 0xbf cursor, then a WorldItem (§5) |
| 0x16 | 5 + n | `0x1435bff00` / `0x141f91480` | a Commands.dat command: u8 command (row + 1), u16 byte count + UTF-8 argument. The town popup's Destroy sends **9** (row 8 DestroyCursorItem, `/destroy`) | 0xbf cursor (the item is gone) |
| 0x2d | 8 | `0x1435c02b8` / `0x141f90340`, senders `0x14215f520` / `0x14215f570` | inventory request, not mapped: u8, u32, u8 flag (0 / 1 by sender) | not handled |
| 0x58 | 10 | `0x1435bf8a8` / `0x141f926a0` | use an item that needs no target (right-click): u32 inventory id, u32 item id, both big-endian | an Orb of Regret loses one item and adds one refund point (0xbf the stack, 0x33 the tree) |
| 0x71 | 18 | not traced | use an item on an item (right-click it, then click the target): u32 inventory id, u32 item id of the used item, then of the target, all big-endian (live 2026-10-20); the first inventory id is a stash tab (e.g. 2053) when the currency is right-clicked in the open stash (live 2026-10-23): one is taken off that tab's stack; the target inventory id can be a stash tab too, e.g. the currency tab's crafting slot (live 2026-10-23: currency in its slot used on the item in the crafting slot, both inventory ids 2053). `ItemMoves.UseAcross`: the currency and the target saved together, one 0xbf per place changed (one for both when they share it) | a Scroll of Wisdom identifies the target (§2b); a Chromatic Orb uses the 3.29 rule (`CurrencyEffects.Chromatic`, below): one socket is forced non-white, the rest roll normally, gems and links stay: 0xbf with the stack and the target |
| 0x72 | 23 | not traced | the held currency used again with **Shift** (the client keeps it on the cursor while the inventory has more, live 2026-10-21): u32 its BaseItemTypes HASH32 (the type, not an item), u32 inventory id and u32 item id of the target, all big-endian, then u32 0, u8 1, u8 0, u8 7, u16 2 (unknown) | as 0x71, with a stack of that type from the inventories (the main inventory first), then from the stash tabs |
| 0x4a | 14 | `0x1435bf6d0` / `0x141fa08d0`, sender `0x14215f5c0` | the held item clicked **on an item** (§4a): u32 inventory id with HNC bits from bit 20 (mask `0xc00fffff`), u32 the clicked item's id, u8 flags (bit 2 = Shift; HNC bits 3-7), u8 (3), u16 (low byte first, differs every click, unknown) | a merge (0xbf the stack, 0xbf cursor), else a swap as 0x2a |
| 0x7c | 17 | `0x1435bfdd8` / `0x141f90870`, sender `0x142165640` | split a stack (§4a): u32 inventory id, u32 item id, u32 amount, u8 (3), u16 (unknown); no HNC bits | 0xbf the stack (same id, smaller), 0xbf cursor (a new item with the amount) |

Dropping: `0x140117b80` sends 0x29 at once outside town. In town (and for quest items) it opens the Keep / Destroy popup
instead and sends nothing unless Destroy is chosen (0x16 command 9).

Rules the Gateway applies (`PlayerInventories.Place`): grids need the item inside the grid. When exactly one item is in
the way it is swapped onto the cursor; two or more refuse. Single-item slots take the item at (0, 0) when its class fits
(`Accepts`: the ItemClasses Id per equipment row; flasks only in the flask bar). A one-handed weapon put into an empty
main hand's off hand goes to the main hand. Taking the main hand off moves the off hand's one-hander there. The two
hands of a weapon set must fit together (`PlayerInventories.HandConflict`, 2026-10-23): a bow goes with an empty off hand
or a quiver, another two-handed weapon only with an empty off hand; a quiver goes with a bow or an empty main hand, never
with a one-handed weapon; a shield or a one-handed weapon not with a two-handed weapon. A drop that breaks this is
refused (the item stays held). Before, only the class per row was checked, so a bow could go next to a shield or a
sword and a one-handed weapon next to a quiver.

### Client quiver eligibility

`FUN_1421eca50` checks that a quiver (ItemClasses row + 1 = 21) sees player stat 3,
`main_hand_weapon_type`, equal to **9**. This is a separate native enum: Bow's ItemClasses row + 1 is 14,
and its model stance is 8. Sending the visual alone leaves inventory quivers red and prevents placement
requests. `FinalPlayerStats` supplies 9 for an active, usable bow in row 2 (a value /set_stat set wins); other
weapons send nothing, and taking the bow off sends 0 through the cleared-stat list (`PlayerStatsWithCleared`).
Weapon swaps physically exchange rows 2/3 with 14/15. Other weapon-stat enum values remain to be mapped.
Automated coverage passes; in-client confirmation pending.

### Bow model records

A bow is not held in the main hand. The live server (Frida 2026-10-25) sends the bow's art in model slot 1 (left
hand; type `0x68`, stance `0xff`), an arrow in slot 0 (right hand; type 14 `Bows`, stance 5 `Bow`, art
`DefaultArrow` `0xB2F6`), and the quiver on the back in slot 7 (type `0x68`, stance `0xff`, art `DefaultQuiver`
`0x328B`). `BowSetup` reproduces these entries; `ItemCatalog.Visual` maps the ItemVisualIdentity ids to their
HASH16. Without them the bow appeared in the wrong hand and the arrow/quiver were missing or misplaced.

## 4a. Stacks

Stackable types have a Stack component (its u16 is the stack size) and a CurrencyItems.dat **StackSize** above 1
(`ItemCatalog.BaseType.MaxStack`: Orb of Transmutation 40, Scroll of Wisdom 40 …; `CurrencyTab_StackSize` is the
currency stash tab's limit). The client draws the size and the tooltip's "Stack Size: n/max" from the item and its own
tables; it changes nothing itself, the server's 0xbf does.

- **Merge** (C→S 0x4a, the held stack clicked on a stack; live 2026-10-19 the client sends 0x4a, not 0x2a, for a
  click on an item): a stack of the same type with room fills up to the max and the rest stays held. The answer is a
  0xbf on the target (remove and add the stack's id, larger) and one on the cursor (remove the held id, add it again
  smaller, or not at all when all of it went). A full stack, or another type, is swapped as 0x2a on that item's cell
  (`PlayerInventories.MergeOnto` → `Place`). Stash tabs do the same (`AccountStash.Merge`, [18](18-stash.md)).
- **One of the held stack** (Shift + click while holding): 0x2a with its first flag byte 1 puts one into an empty cell
  as a new item; 0x4a with flag bit 2 adds one to a stack of its type (refused on anything else). The rest stays held.
- **Split** (Shift + click on a stack, then the split window's amount): C→S 0x7c. The stack shrinks in place and the
  split-off part is held as a new item (`PlayerInventories.Split`). Refused with something held, an amount of 0 or the
  whole stack. On a stash tab the tab's stack shrinks (`AccountStash.Shrink`, written first) and the held part gets a
  character item id.
- **Full stacks become their item:** CurrencyItems `FullStack_BaseItemTypesKey` (28 types: 5 Scroll Fragments → a
  Scroll of Wisdom, 20 shards → their orb, 100 splinters → their Breachstone or Emblem …). A stack that reaches its max
  by a merge or an add turns into one of that item in the same cell, with the same id (`PlayerInventories.Completed`).
- **Adding** (`PlayerInventories.AddStacked`): items picked up from the ground into the main inventory, `/item <type>
  <count>` (count = how many, up to a full main inventory: 60 stacks; the rest is reported as no room) and the sell window's reward go onto the stacks of their type first
  (fullest first), then as new stacks of at most the max in free cells. A ground stack is picked up only when all of it
  fits (`Fits`).

The 0x4a writer `0x141fa08d0` packs the HNC side-channel bits into the inventory id from bit 20 and into the flags byte
from bit 3, like 0x28. The framing table had 0x4a at 12 bytes (it is 14) and did not know 0x7c; both would have split
the next message. Verified live on 2026-10-19: merge, split (10 off 35), Shift one into a cell and onto a stack, 5
Scroll Fragments → a Scroll of Wisdom.

## 5. Items on the ground

`Metadata/MiscellaneousObjects/WorldItem` (`WorldItemObject`): Positioned, BaseEvents (nothing), Animated (u8 0),
WorldItem, InteractionAction (u8 0). WorldItem `0x141ea10a0`: u32 a (non-zero → one more u32), u8 (+0x50), u32
ItemVisualIdentity HASH16 override (0 = the item's), then the item data without its length prefix. Created with 0x2b9 +
wake zone 0x1a9 at the player's estimated cell, removed with 0x2bb.

Clicking it sends C→S 0x24 on the object (skill 0x0266, Interaction). **Flag 0x0002 = the inventory panel is open**
(0x040a vs 0x0408): the item goes to the cursor instead of the first free cell. The Gateway echoes the interaction at
once (0x1ab aimed at the object, from the player's current cell) and picks the item up when its walk estimate is within
**22 cells**: WorldItem.ot `InteractionAction interaction_distance_override = 20` is in grid cells, plus both radii
(object_size 2). Measured with Frida (Positioned grid position at `+0x294/+0x298`): the client stopped 20.9 cells from
the item, on the first step inside the range.

Dying monsters drop items as WorldItems around the corpse (`DropLoot`, [15](15-progression.md) §3).

**Ground items belong to the instance** (`InstanceStore`, `InstanceGround`), as in the real game. Dropped items and
loot are kept per area instance, by Auth's instance id and its creation time (`Created` in the `resolve` answer: Auth's
ids start at 1 again after a restart, so an old instance's items never reach a new one). Re-entering the instance sends
them where they lay (`SendGroundItems`), after a logout, a disconnect or a client crash. Their object ids come from
their own range, **0x100000** up, the same for every connection; the client accepts them (live 2026-10-18). A pickup
takes the item out of the store first, so two connections can never both get it. The store is in the Gateway's memory:
it is wiped like Auth's instance, once no connection has been in it for `InstanceRegistry.Lifetime` (6 minutes), and
lost when the Gateway restarts.

### Jeweller's Orb (initial local implementation, 2026-10-21)

`CurrencyRerollSocketNumbers` uses the existing 0x71 handler and the shared Shift 0x72 path. One successful use
spends one orb and replaces the target through 0xbf, rerolling socket count and link groups. 3.29: sockets the reroll
keeps retain their colours and only the added sockets are white (`SocketColours`, below). Equipped
targets also refresh equipment. The existing character save stores the result. Verified live with the 3.29.3b client: repeated rolls on gloves, boots, a helmet, weapons and body armour; inventory updates and currency consumption are visible in the Gateway log.

Requested local counts are **1, 2, 3, 4, 6** (five excluded). Class caps: body armour, bows and two-handed melee
weapons 6; helmets/gloves/boots 4; one-handed weapons/shields 3. Item-level caps: below 2 -> 2, 2-24 -> 3,
25-49 -> 4, 50+ -> 6; inventory dimensions also cap the count. Other classes and missing Sockets components are
refused. Items already at the base's capacity (class and dimensions, not the item-level roll cap) or containing gems
are refused without consuming currency; remove gems first. Live failure 2026-10-21: level-1 BootsStr1/GlovesStr1
with two empty sockets were incorrectly refused at the level cap. They can now reroll between one and two sockets;
the level cap limits the result, not eligibility. A regression test covers repeated use on both bases.
The new count differs from the old count. Link groups are independently rerolled and may repeat.

This is deliberately an initial local rule set, not exact 3.29 socket crafting: weights for counts 1/2/3/4/6 are
50/120/100/30/1 (filtered by eligibility and old count), each adjacent link has a 50% chance, and new sockets are
white. Special unique/mod socket overrides and corruption are not modelled. No new extracted data is needed.

C→S 0x72 is 23 bytes: u16 id, u32 currency BaseItemTypes HASH32, u32 target inventory, u32 target item,
then nine currently uninterpreted bytes. All integer fields above are big-endian. The Gateway resolves a stack
of that currency (main inventory first) and reuses the 0x71 handler.

### Socket colours (3.29 rules, 2026-10-26)

The 3.29.0 patch notes ("Gem Socket Changes"):

- Item sockets are white by default, and a gem goes into any equipment socket whatever its colour (the client's own
  check, §2d, only compares requirements and changed in 3.29 to allow this).
- Each socket generated on an item has a small chance to be red, green or blue instead, in proportion to the item's
  attribute requirements; the chance rises with item level.
- A gem in a red, green or blue socket of its own colour gets +10% quality ([25](25-gem-supports.md), the support and
  skill stats read it).
- Chromatic Orbs and Tainted Chromatic Orbs force one of the item's sockets non-white and roll the rest normally;
  higher item quality raises the chance of non-white sockets.
- Currency that adds sockets (Jeweller's, Tainted Jeweller's, Orb of Binding) adds them white.
- Vaal Orb corruption turns a white socket coloured; the Locus of Corruption turns all white sockets coloured.
- The old single-colour bench crafts are gone. `CraftingBenchOptions` now has **At Least Two / Three / Four Non-White
  Sockets** (`SocketColours` `NN`/`NNN`/`NNNN`), 5 / 20 / 75 Chromatic Orbs.

The notes give no numeric probabilities. This server approximates them in `SocketColours`: 5% non-white at item level 1
plus 0.25 points per item level (30% at 100), and +0.25 points per 1% item quality for Chromatic Orbs and the non-white
bench crafts. A rolled colour weighs its attribute requirement + 12, so a strength base rolls mostly red. The curve is
a placeholder to be fitted from live captures; the caps (2 sockets per generated item, socket counts, link rules) and
the class capacities are unchanged (`CurrencyEffects.MaximumSockets`, `SocketCapacity`).

Where it applies: `ItemCatalog.DefaultSockets` (used by `/item`, drops and NPC shops) rolls each of an item's two linked
sockets; the start weapon gets two white linked sockets; `CurrencyEffects.Chromatic` forces one socket and rolls the
rest; `Jewellers` and the bench's socket-count crafts add white sockets and keep the retained ones; `BenchCrafting`
handles the `NN` crafts (rerolls the colours until the item has at least N non-white sockets, with the item's chance,
then forces any missing ones); `CurrencyEffects.Vaal` colours one white socket.

Not modelled: Tainted Chromatic and Tainted Jeweller's Orbs, the Orb of Binding, the Locus of Corruption and the
bench's milestone reward (25 reforges keeping the most non-white sockets). Verified live 2026-10-26: white sockets are
drawn and accepted, Chromatic leaves one socket non-white and the rest white, Jeweller's keeps retained colours and
adds white ones, and the matching-colour +10% quality changes the gem's stats; the stat path is in
`Program.GemSupports.cs` (`Quality`). The probabilities remain an approximation.

### Orb of Alchemy (2026-10-21, verified live; CoE-checked 2026-10-30)

`CurrencyUpgradeToRare` goes through the same 0x71/0x72 handler. `CurrencyEffects.Alchemy` delegates to
`ItemRolls.Alchemy`: a modifiable normal item becomes rare and identified, implicits kept, with 4-6 explicit affixes
from the ordinary pool (`ItemRolls.Roll`, weighted by Mods.dat spawn weights, values within each tier) and a Words.dat
rare name.
The shared rare count table is now CoE's: 4 affixes 2/3, 5 affixes 1/4, 6 affixes 1/12; jewel classes get 3 or 4 with
at most two per side.
Flasks are magic-only and refused. Unknown/unmodifiable bases, non-normal items, corrupted/mirrored items and empty
pools are refused before spending.
The 300-apply sample and jewel caps are in [the source comparison](../re/alchemy-coe.md).

### Chaos Orb (2026-10-21, verified live; CoE-checked 2026-10-30)

`CurrencyRerollRare`, same handler (0x71, and 0x72 with Shift for repeated use). `CurrencyEffects.Chaos` delegates to
`ItemRolls.Chaos`: a modifiable rare item's unprotected explicit affixes are replaced by a fresh `ItemRolls.Roll`
(4-6 affixes, new tiers and values; jewel classes 3-4 with two per side), a new rare name is drawn, implicits stay and
the item ends identified. Prefix/suffix metamod locks keep their side.
Flasks are magic-only and a rare flask is refused.
The base must be known and serializable, with Mods and not `Unmodifiable`; non-rare, corrupted/mirrored and empty-pool
targets are refused before spending.
CoE's 300-applies counts, jewel limits and prefix-lock result are in [the source comparison](../re/chaos-coe.md).

### Divine Orb (2026-10-21, verified live)

`CurrencyModValues`, same handler (0x71, 0x72 with Shift; one orb spent per use). `ItemRolls.Reforge` rerolls only
the values of each explicit mod within its own tier; the mods, their order, implicits, rarity and name stay. A new tier
would be a different mod, and the client reorders the tooltip by mod, so the lines would jump. An item without explicit
mods is refused.

### Blessed Orb (2026-10-23, not verified live yet)

`CurrencyRerollImplicit`, same handler (0x71, 0x72 with Shift; one orb spent per use). Before, the Gateway logged it as
"not handled" (live 2026-10-23, item 221). `ItemRolls.Bless` rerolls only the values of each implicit mod within its own
tier, as the Divine Orb does for explicits; explicit, enchant and other mods, rarity and name stay. An item without an
implicit mod is refused (nothing spent), and like every currency here but the Scroll of Wisdom it does nothing to a
corrupted or mirrored item.

### Sacred Orb

`CurrencyRerollDefences` uses the ordinary 0x71 and repeat 0x72 currency routes (`CurrencyEffects.Sacred`).
The target must have Armour and Mods components, a known variable native defence range, and be neither corrupted
nor mirrored; rarity does not prevent an otherwise eligible reroll.
One orb selects a shared ordinary base roll from 0 through 31, replacing raw special state 32 when present.
This uses the local uniform roll policy; the official server's distribution has not been measured.
A successful reroll may repeat the old value and still spends one orb; it need not improve the defences.
Affixes, fractures, quality, sockets, gems and other item properties stay unchanged.
Inventory/stash saves and currency consumption commit together; a failed save rolls both back.
Worn-item changes feed the shared pools and the next character-panel update.
Native range eligibility includes Ward, but Ward character calculations and recovery remain unimplemented.
`SacredOrbTests` covers eligibility, endpoints, preservation, persistence, rollback and pool refresh.
Fourteen historical native captures recorded at `63c1039` cover ordinary/repeat use, defence updates, refusals,
raw-32 replacement, Ward-only targets and inventory/stash pairings.
A native 9-to-9 reroll consumed one orb, and a Gateway restart preserved the rolls and currency counts.
Fresh real-client checks of the merged branch remain open.

### Orb of Scouring (2026-10-21, verified live)

`CurrencyConvertToNormal`, same handler (0x71, 0x72 with Shift). `CurrencyEffects.Scouring` makes a magic, rare or
unique item normal: explicit mods and the rare name are removed, implicits and enchants stay. A normal item is refused
and nothing is spent.

### Orb of Transmutation (2026-10-21, verified live)

`CurrencyUpgradeToMagic`, same handler (0x71, 0x72 with Shift). `CurrencyEffects.Transmutation` makes a normal item with
a Mods component magic and identified through `ItemRolls.Transmute`: 1-2 target affixes, 50/50, at most one prefix
and one suffix, each tier drawn by weight; implicits, enchants, quality, sockets, gems and charges stay.
An unavailable second side or conflicting family can leave only one affix, as on a level-1 Small Life Flask.
The base must be known and serializable, with Mods and neither its base nor its class marked `Unmodifiable`.
Corrupted, mirrored, non-normal and empty-pool targets are refused before spending.
The empty-pool refusal is LocalExile safety policy; other currencies are unchanged.
The pinned CoE beta count model and two native pools agree with the existing ordinary generator.
Framed real-client 0x71 upgrade/refusal and relog checks passed on 2026-10-29; Shift/stash combinations have automated
production-dispatch coverage, not fresh native acceptance.
See [the source comparison and evidence limits](../re/transmutation-coe.md).

### Orb of Alteration (2026-10-21, verified live; CoE-checked 2026-10-30)

`CurrencyRerollMagic`, same handler (0x71, 0x72 with Shift). `CurrencyEffects.Alteration` delegates to
`ItemRolls.Alter`: a modifiable magic item rerolls its explicit affixes as the official orb, 1-2 with at most one
prefix and one suffix, each tier drawn by weight (`ItemRolls.Roll`, metamod locks respected).
The base must be known, serializable, with Mods and not `Unmodifiable`; corrupted/mirrored, non-magic and empty-pool
targets are refused before spending.
The empty-pool refusal is LocalExile safety policy: otherwise a missing-data target would lose all affixes for an orb.
CoE beta's 300-applies count distribution and flask pool agree; see
[the source comparison](../re/alteration-coe.md).

### Orb of Augmentation (2026-10-21, verified live; CoE-checked 2026-10-30)

`CurrencyAddModToMagic`, same handler (0x71, 0x72 with Shift). `CurrencyEffects.Augmentation` delegates to
`ItemRolls.Augment`: a magic item with a free prefix or suffix gets one affix drawn by weight from the ordinary pool
(`ItemRolls.AffixesToAdd`: no family already on it, at most one prefix and one suffix, as the Orb of Alteration).
Old affixes, rarity, implicits, quality, sockets, gems and charges stay.
The base must be known and serializable, with Mods and neither its base nor its class marked `Unmodifiable`;
corrupted and mirrored items are refused too.
A full item, a non-magic item, or a base whose open side has no ordinary affix is refused before spending.
That last refusal is LocalExile policy: CoE's engine still spends the orb and adds nothing on such an item.
The level-1 Driftwood Wand pools and simulated probabilities agree with CoE beta; see
[the source comparison](../re/augmentation-coe.md).

### Regal Orb (2026-10-21, verified live; CoE-checked 2026-10-30)

`CurrencyUpgradeMagicToRare`, same handler (0x71, 0x72 with Shift). `CurrencyEffects.Regal` delegates to
`ItemRolls.Regal`: a modifiable magic item becomes rare, keeps its affixes, gets one more from the rare pool
(`ItemRolls.AddAffix`, at most three prefixes and three suffixes) and a rare name.
Flasks are magic-only and cannot become rare, so they are refused.
The base must be known and serializable, with Mods and neither its base nor its class marked `Unmodifiable`;
corrupted/mirrored, non-magic and no-fit targets are refused before spending.
CoE's rare-pool applies open both sides after the upgrade (12 candidates, total 6983, for a level-1 wand with one
prefix), unlike its `pool` operation, which still evaluates the magic item. See
[the source comparison](../re/regal-coe.md).

### Exalted Orb (2026-10-21, verified live; CoE-checked 2026-10-30)

`CurrencyAddModToRare`, same handler (0x71, 0x72 with Shift). `CurrencyEffects.Exalted` delegates to
`ItemRolls.Exalted`: a modifiable rare item keeps its affixes and name and gets one more weighted affix within its
rare limits (three prefixes and three suffixes, two of each for jewels).
Flasks are magic-only and a rare flask is refused.
The base must be known and serializable, with Mods and not `Unmodifiable`; corrupted/mirrored, non-rare and full
targets are refused before spending.
The four-mod wand pool (nine candidates, weight 5150) and the jewel 2+2 refusal are in
[the source comparison](../re/exalted-coe.md).

### Orb of Annulment (2026-10-21, verified live; CoE-checked 2026-10-30)

`CurrencyRemoveMod`, same handler (0x71, 0x72 with Shift). `CurrencyEffects.Annulment` delegates to `ItemRolls.Annul`:
one random unprotected explicit affix leaves a magic or rare item; implicits, the other affixes, rarity and name stay.
Prefix/suffix locks and cannot-roll attack/caster crafts filter the candidates, and fractured affixes are never
removed.
Corrupted/mirrored items, unsupported/unknown or `Unmodifiable` bases, and items without an eligible explicit are
refused before spending.
CoE confirms the magic/rare rarity stays and the metamod candidate rule; see
[the source comparison](../re/annulment-coe.md).

### Armourer's Scrap (2026-10-21, verified live)

`CurrencyArmourQuality`, same handler (0x71, 0x72 with Shift). `CurrencyEffects.ArmourerScrap` adds 1% quality (the
Quality component's u8) to an item with an Armour component, any rarity, up to 20%; the client shows the raised
defences from the quality. On this server the Orb of Scouring also sets an armour piece's quality back to 0 (and so
accepts a normal armour piece with quality). Other items, or 20%, are refused and nothing is spent.

### Sacred Orb

`CurrencyRerollDefences` (`reroll_variable_defences` in CurrencyItems.dat) uses the same 0x71 / Shift 0x72 handler.
`CurrencyEffects.Sacred` changes only the saved shared `DefenceBaseRoll` to an ordinary value from 0 through 31.
The local server samples uniformly, as it does for newly created armour; the official distribution is unmeasured.
One orb is spent even when the new roll equals the old roll.
Mods, quality, rarity, identification, sockets, gems, links, names and fractured state stay unchanged.
The existing atomic inventory/stash mutation saves the stack and target together and sends their 0xbf replacements.
Rerolling worn equipment refreshes the character's shared defence pools and panel.

The native target validator `0x141f83050` requires Armour and Mods components and rejects mirrored items.
The ordinary crafting protection also refuses corrupted targets.
At least one native base-defence range must vary; otherwise it returns `0x1762`,
`ItemErrorFailedRerollVariableDefence`: "Target item has no modifiable base defence."
The comparisons include armour, evasion, ES, Ward and the fifth unnamed native range.
`ItemCatalog.BaseDefences.HasAdditionalVariableRange` records the Ward/fifth-range eligibility without adding their
mechanics to the server's character pools.
The current extracted fifth range (`__col10` / `__col11`) is zero in every row.
Fixed bases such as Kaom's Plate and Kaom's Greaves, and all-zero bases such as Golden Wreath, are refused.
Ward-only Runic equipment has a variable range and remains eligible.
The validator has no rarity, identification or current-roll restriction.
Raw 32 is eligible: a successful reroll replaces it with an ordinary roll and clears the native hidden-defences flag.

Native application verified raw 32 to 26 and a worn Plate Vest from 16 to 12, with tooltip and panel armour both 40.
Shift 0x72 rerolled worn gloves from 16 to 23.
A worn robe changed from 16 to 20, then 20 to 28 using currency from the stash.
Every inventory/stash source/target pairing passed, including a same-value stash reroll from 9 to 9 consuming one orb.
Corrupted, mirrored, ring and fixed-base boots targets were refused without spending an orb.
The native validator refused fixed Kaom's Plate and zero-defence Golden Wreath, while accepting Ward-only Runic Helm.
UI application changed the helm's roll from 16 to 25 and its Ward tooltip from 44 to 46.
Shield and unique-rarity targets also rerolled successfully.
After a Gateway restart, both characters' inventories and the stash retained every roll and currency count.
The reloaded stash vest kept roll 9, quality 20 and its 50% local increase, displaying 54 armour and 54 evasion.

### Blacksmith's Whetstone (2026-10-21, verified live)

`CurrencyWeaponQuality`, the same as Armourer's Scrap for items with a Weapon component (`CurrencyEffects.Whetstone`):
+1% quality, any rarity, up to 20%, Shift included; the Orb of Scouring sets a weapon's quality back to 0 as well.

### Orb of Chance (2026-10-21, verified live; CoE boundary 2026-10-30)

`CurrencyUpgradeRandomly`, same handler (0x71, 0x72 with Shift). A server rule, not the official one (which can also
make uniques): `CurrencyEffects.Chance` delegates to `ItemRolls.Chance`, which turns a normal weapon or armour piece
(Weapon or Armour component) rare with a 30% chance (4-6 affixes, rare name) and magic otherwise (2-4 affixes, at most
two prefixes and two suffixes, a server rule above the normal magic limit); the item ends identified.
Corrupted/mirrored items, unsupported/unknown or `Unmodifiable` bases and empty pools are refused before spending.
The CoE beta method tree has no Chance handler, so the official unique/rarity outcome remains separate work; see
[the admission check and boundary](../re/chance-coe.md).

### Glassblower's Bauble (2026-10-21, verified live)

`CurrencyFlaskQuality`, same handler (0x71, 0x72 with Shift). `CurrencyEffects.Glassblower`: +1% quality on an item with
a Flask component, any rarity, up to 20%. The Orb of Scouring makes a flask normal, removes its explicit mods and sets its
quality back to 0, as for armour and weapons.

### Gemcutter's Prism (2026-10-22, being tested)

`CurrencyGemQuality`, same handler (0x71, 0x72 with Shift). `CurrencyEffects.Gemcutter`: +1% quality on an item with a
SkillGem component (active or support) that is not corrupted, up to 20%. Anything else is refused and nothing is spent.

### Fracturing Orb (2026-10-21, verified live; rules rechecked 2026-10-30)

`CurrencyFractureRare`, same handler (0x71, 0x72 with Shift). As the official orb: a rare item with at least four explicit
mods, not corrupted, mirrored or already fractured, on a supported non-`Unmodifiable` base whose
`ItemClasses.CanBeFractured` is set, has one random explicit mod fractured (`ItemRolls.Fracture`). Metamod-protected
mods are eligible: the orb does not respect metamods. The mod moves to the front of the explicits and
`Item.FracturedCount` (saved) counts it. Every other currency keeps fractured mods: Chaos, Vaal and the other rerolls
roll around them (they count towards the affix limits), Annulment and Divine skip them, Augmentation / Exalted insert
after them, and Scouring removes everything else (the item stays magic so the mod still shows).

The client is told through the magic properties' u8 `+0x7a` (the byte right after their u16 flags, sent 0 before):
the fractured explicit count, the first explicits being the fractured ones. Verified live: those mods show fractured.
Tried first and wrong: the Mods component's flag bit 4 with a u8 after the flags shifted the item (it showed magic).

Influenced and synthesised items are refused (§2f). Veiled modifiers are not modelled.
See [the game rules](../re/fracturing-mirror-mechanics.md).

### Mirror of Kalandra (2026-10-21, verified live; rules rechecked 2026-10-30)

`CurrencyDuplicate`, same 0x71 / 0x72 messages (`MirrorItem`). As the official mirror: a non-unique equipment item
that is neither corrupted nor mirrored, on a supported non-`Unmodifiable` base whose `ItemClasses` equipment flag is
set, is copied with every property (mods and values, fractured mods, quality, sockets, links, socketed gems, rarity,
identification and name); the copy is **mirrored** and goes onto the cursor (S→C 0xbf on the cursor inventory) for
the player to place, the original stays unchanged. A held item, a corrupted or mirrored target, a unique item, a
non-equipment item (flask, jewel, map, currency), an unmodifiable base, or an item without mods is refused and
nothing is spent. A mirrored item takes no currency at all, including a Scroll of Wisdom; Reflective Oil is not
modelled. Using a Mirror from a stash tab is a local limitation, not a game rule.

`Item.Mirrored` (saved) is sent as the Mods component's u16 flags bit 1 (`+0x2df`, a plain bool with no data after
it): the client's crafting check refuses an item with it (`0x141d68f9b`); the tooltip shows
"Mirrored". See [the game rules](../re/fracturing-mirror-mechanics.md).

### Vaal Orb and corruption (2026-10-21, verified live)

**Corrupted** is bit 0 of the Base component's u8 `+0xc7` (the second u8 after the visual identity): the client's
crafting checks read it ("input_is_corrupted" at `0x141d68e64`, `test byte ptr [base+0xc7], 1`). `Item.Corrupted` writes
it and is saved with the item. A corrupted item takes no currency but a Scroll of Wisdom (there is no corrupted
currency yet), and nothing removes corruption.

`CurrencyCorrupt`, same handler (0x71, 0x72 with Shift). A server rule, not the official one: `CurrencyEffects.Vaal`
delegates to `ItemRolls.Vaal`, which takes a modifiable weapon, armour piece or jewellery (ring, amulet, belt) that is
not unique, corrupted or mirrored, and always corrupts it.
A normal or magic item becomes rare with 4-6 new affixes and a rare name. A rare item has 1, 2, 3 or 4 random affixes
replaced (20% each), 5 (10%) or all of them rerolled (10%: 4-6, new name) (`ItemRolls.ReplaceAffixes`: the replaced
ones come back through `AddAffix`, at most three of each kind, so the count stays).
Unknown/unsupported or `Unmodifiable` bases and empty pools are refused before spending.
Official CoE Vaal outcomes are separate work; see [the admission check and boundary](../re/vaal-coe.md).

### Eldritch Ichors and Embers (2026-10-30)

`CurrencyEldritchIchor1..4` (Lesser, Greater, Grand, Exceptional), same 0x71/0x72 handler.
`CurrencyEffects.EldritchIchor` delegates to `ItemRolls.EldritchIchor`: a normal, magic or rare body armour, boots,
gloves or helmet gains one Eater of Worlds implicit of the ichor's tier (1-4), replacing every other implicit modifier
except Searing Exarch ones; explicit affixes, quality, sockets, links, rarity and name stay.
The tier selects the `...EldritchImplicitN` row, including the Unique/Pinnacle Presence variants where the base allows.
Uniques, corrupted/mirrored items, non-armour classes and unsupported/unmodifiable bases are refused before spending.
The item stores the implicit in its normal implicit list; a separate Eldritch dominance/influence state and the
Orb of Conflict are not modelled.
The four `CurrencyEldritchEmber1..4` are the Searing Exarch mirror: `CurrencyEffects.EldritchEmber` adds the tier's
Exarch implicit and replaces every other implicit except Eater of Worlds ones, with the same classes, rarity and
admission.
See [the Ichor comparison](../re/eldritch-ichor-coe.md) and [the Ember comparison](../re/eldritch-ember-coe.md).

### Orb of Fusing (2026-10-21, verified live)

`CurrencyRerollSocketLinks`, HASH32 `0xC71BF58D`, uses the same 0x71/0x72 currency handler and 0xbf inventory update.
Captured normal use on item 229: `0071000000010000005B00000001000000E5` (currency stack 91).
Captured Shift use on that item: `0072C71BF58D00000001000000E5000000000100060000`.
The nine trailing bytes of 0x72 vary by currency; do not validate them against the earlier Chromatic capture.
Tests parse both, split consecutive messages and keep a partial 0x72 until all 23 bytes arrive.

`CurrencyEffects.Fusing` replaces only link groups. Socket count, colours (including white), gems and their experience,
quality, rarity and mods stay unchanged. At least two sockets and a supported equipment class are required; the
item-level roll cap does not apply to existing sockets. Fully linked 5/6-socket items are protected; 2-4 sockets
may be unlinked. A refused use spends nothing. Normal and Shift uses consume one orb, update the target, refresh
equipped items and persist through the existing inventory path. The new link layout differs from the old one;
the number of links may repeat with different positions. This does not implement support-gem effects.

Initial **approximate** probability model: enumerate all partitions of the socket chain, assign total weights
500/3500/4500/1400/90/10 to largest groups 1/2/3/4/5/6, divide each weight among that group's layouts, then exclude
the current layout. Quality multiplies a layout's weight by `(1 + quality / 100)^(largestGroup - 1)` and is not
consumed. This is a local approximation, not an official probability table; exact chances, corruption and special
item overrides remain open. No extracted-data changes are required.

## 6. Equipment on the character: visuals, attributes, gem skills

**What the model wears** is the player's Inventories component (index 8, reader `0x141e1ee30`, also as a 0x2ba update):
u8 n × (u8 slot, record `0x1424f44e0`), then u8 m × u16. The slot is Inventories.dat column 7 (Weapon1 0, Offhand1 1,
BodyArmour1 2, Helm1 4, Gloves1 5, Boots1 6, Ring1 8, Ring2 9, Belt1 10, Amulet1 11). The record: u8 type (**0 = empty**,
nothing more; else the item's ItemClasses row + 1, the weapon type skills check, 11-skills.md §10a), two (u16 ItemVisualIdentity HASH16, u32 TieredMicrotransactions key, u8) references (`0x1424f3fc0`,
`0x1424f4250`), u8 ItemStances row (`0x1424f4b10`; the row's ItemStance, else its class's: Wand = 2), u16
ItemVisualIdentity HASH16 (`0x1424f4730`), u8. The Gateway sends every visible slot, empty ones with tag 0: a slot left
out keeps what the model showed before (the unequipped wand stayed in the hand).
The player object's own list does not set how a two-handed weapon is held: after every area load it was held one-handed
(live 2026-10-22). The Gateway sends the same list again as a 0x2ba update right after the player and the skill bar, as
after an equip (verified live 2026-10-22).

**Weapon swap (verified with a live trace, 2026-10-22).** The client sends C→S 0xdd (3 bytes, `0x1421690f0`): u8 the
set it wants (1 the second, sent as "the current set is the first"), then waits (session +0xcfa4 = 1): no other 0xdd
and no skill input (`FUN_14011d660`, `FUN_14215f700`) until the server answers. **S→C 0xde** (u8 flags, dispatcher case
0x42 at `0x14214eb65`) answers: bit 0 is the active set (session +0xcd31, which the inventory panel shows,
`FUN_1408f52e0`; a change tells the listeners), bit 1 keeps the wait (+0xcfa4 = 2), else it ends (0). S→C 0x21 carries
the set on entry as its flag bit 0. Watch out: in the dispatcher `0x14214a190` r13 is the session object + 8, so its
"[r13+0xcd29]" is the session's +0xcd31 and "[r13+0xcf9c]" is +0xcfa4 (a Frida trace of both places showed it; reading
the offsets as they stand had suggested only a reconnect could swap). The panel always shows the hands' rows (Weapon1 /
Offhand1, rows 2 and 3) as the active set and rows 14 / 15 as the other one, under the I / II tab the flag selects
(live 2026-10-22: with only the flag switched, the second tab showed the first set's weapons). So the Gateway answers
0xdd by trading the items of rows 2 ↔ 14 and 3 ↔ 15 (`PlayerInventories.SwapWeaponSets`, saved, ids and cells kept), a
0xbf with the new contents of each of the four, then 0xde with the set and the equipment update.

**Attribute requirements** are checked (`0x1402ce5f0`) against `virtual_strength_for_requirements`,
`virtual_dexterity_for_requirements` and `virtual_intelligence_for_requirements` (keys 0x3d1b-0x3d1d), cached on the Player
component by `0x141e80340`; `global_no_attribute_requirements` (0x2ee3) skips the check and
`global_item_attribute_requirements_+%` (0x51a) scales it. The character panel shows `strength` / `dexterity` /
`intelligence` (575 / 581 / 578), which a UI cache (`0x141e802d0`) reads when it is built. With only those three the
panel said 32 Int while "Requires 14 Int" stayed red. The Gateway sends all six from Characters.dat (BaseStrength …), in
the creation blob and again as 0x1b3 after entering. A gem's attribute requirement is not the item curve: it is the
client's own formula from the SkillGem component (`0x14246fc40`, [19](19-gems.md) §2), checked against the same three
stats (the socket check at `0x1402ce738`).

**The class byte** of the Player component is Characters.dat **IntegerId** (Marauder 1, Witch 3), not the row: with row
1 the panel showed Marauder.

**Gem skills:** each active gem socketed in an equipped item grants its skill (SkillGems → the gem's GemEffects variant →
GrantedEffect at the gem's level, `ItemCatalog.GemEffects`; [19](19-gems.md) §3), sent in the Actor skill list after the
default attack. **Support
gems must not be granted:** a support's effect in the list crashed the client in the skill reader (`0x141ee795a`, via
`0x142197b59` → `0x141eea955`). Supports and links do nothing yet. Gem skills carry their level's stats and can be cast
([11](11-skills.md) §9–10).

## 7. Start kit, saves and dev command

A new character gets Characters.dat StartSkillGem and StartSupportGem in the main inventory, and in the flask bar two
Small Life Flasks (cells 0 and 1: flask keys 1 and 2) and a Small Mana Flask (cell 4: key 5). Its StartWeapons[0], with
two linked white sockets (3.29: a gem fits any colour), is worn
in Weapon1. With `Game_START_WEAPON=ground` it lies on the ground instead, as on the real game's Twilight Strand: `PlayerInventories.StartWeapon` keeps it (in the save as
`StartWeapon`) until it is picked up, and on entering an instance outside towns the Gateway puts it once per character, weapon
and instance (`InstanceState.PlaceStartWeapon`) on the first walkable cell 6 to 20 cells from the spawn
(`AreaLayout.Near`), as a WorldItem like loot. Picking it up (C→S 0x24) clears it in the same save as the item's new cell,
so a crash never keeps it both waiting and in the inventory. A new instance or a Gateway restart before the pickup puts
it there again; so does a new character with a deleted one's name in the same instance (its weapon is another item).
`Game_START_ITEMS=0` starts empty.

The kit is built once, when the character has no save, and saved at once. Without `data/tables` (`Characters.json`,
`SkillGems.json`) and `data/item-types.json` it is empty, the Gateway logs `start kit is empty`, and the character keeps
the empty kit: remove its row from the `characters` table of `data/saves.db` (with the Gateway stopped) to give it the kit
again.

**Saves.** Every inventory change writes the character's items to its row in `data/saves.db` (`CharacterSaves`,
git-ignored): per Inventories.dat row the item ids, cells and items by BaseItemTypes HASH32 with level, quality, charges,
stack size, sockets (with their gems), links and gem experience. A saved character loads from it instead of the start
kit, so items and equipment survive Gateway restarts and logins (live 2026-10-18). The save also holds the skill bar
([11](11-skills.md) §11). How a change is written (SQLite transactions, moves between the character and the stash in
one transaction, what happens when a save fails, saves that can't be read or hold unknown items) is in
[saves](../saves.md).

Each move is one change and one save, so a crash at any moment leaves an item in exactly one inventory: picking up
(`PlayerInventories.PickUp`, C→S 0x28: off its row and onto the cursor), placing or swapping (`Place`, 0x2a), socketing
and unsocketing (0x30, 0x2f) each change everything under one lock and then write the file once. Items on the ground are
not in the save ("Open"), so dropping one takes it out of the save, and a disconnect loses it (#5). Tests:
`tests/GameServer.Gateway.Tests`.

`/item <BaseItemTypes row, Id or name> [count]` adds items to the main inventory; types with a Sockets component get two
linked sockets, mostly white with the item-level colour chance of "Socket colours (3.29)" above, in the colour
proportional to their attribute requirements (ComponentAttributeRequirements).
`/sockets [colours]` (`SocketLayout.cs`, verified live 2026-10-25) sets the held item's sockets and links.
Letters R, G, B, W are linked within a word and unlinked between words: `/sockets RRGBBB` is a 6-link, `/sockets RRG B`
a 3-link and a single (dashes are ignored, so `R-R-G-B` works too); without colours it shows the layout.
It allows up to the base's `CurrencyEffects.SocketCapacity`, whatever the item level; rings, amulets and other classes
without one are refused.
`Game_SOCKET_CAPACITY=1..6` (dev, read by `/sockets`) replaces that capacity for socketable bases, ignoring class and
dimensions: a 6-link wand (a wand's own capacity is 3) is possible with the flag, and a base without a Sockets
component is still refused.
Socketed gems stay in their sockets and take the new colour; a layout that would remove a socket holding a gem is
refused.
The cursor item is replaced through 0xbf like `/mod`.

## 8. Life and mana flasks

**C→S 0x70** (6 bytes, live 2026-10-20): a flask key. u32 big-endian, the flask-bar cell (0 for key 1 … 4 for key 5).
Inferred from presses of the start kit's flasks (cells 0 and 1 sent 0 and 1), not read from the client's serializer. The
client changes nothing by itself: no charges, no recovery until the server answers.

The Gateway (`UseFlask`, `Flasks.cs`, `FlaskRecovery.cs`) takes the flask in that cell of the flask bar (Inventories row
11). Its base use comes from the tables (`ItemCatalog.FlaskOf`): Flasks.dat LifePerUse / ManaPerUse over RecoveryTime
(tenths of a second) and ComponentCharges PerCharge of MaxCharges. Small Life Flask: 70 life over 3 s, 7 of 21 charges;
Small Mana Flask: 50 mana over 3 s, 6 of 24. A use with fewer charges than it needs (§8b), or while dead, does nothing.
Before spending charges, recovery flasks also need room in a pool they restore: life below maximum for life flasks,
mana below the **unreserved** cap for mana flasks; a hybrid needs either of its pools below its cap.
When those pools are full, the Gateway sends `0x263` result `0x4e2`, without spending/saving charges, adding a buff,
restarting an existing recovery effect, or playing the drinking sound; the refusal carries the authoritative pool.
The refusal result was captured from the official 3.29.3b client; the native UI suppresses its failure sound for `0x4e2`.
Otherwise:

1. The charges are spent and saved, and the flask goes to the client again (S→C 0xbf: the item removed and added with
   its new Charges, same id). S→C `0x263` then confirms successful use and triggers the native drinking
   sound (details below).
2. Its recovery (`FlaskRecovery.For`, as the pinned Path of Building `Item.lua` and `CalcPerform` do) starts with
   base amount times quality, local amount modifiers, the matching character amount modifiers and flask effect.
   The low-life more factor is separate from flask effect: their contribution is the effect factor plus the low-life
   factor minus one, at half life or less. The instant fraction is applied to that amount before global duration.
   Only the remaining over-time amount receives the character duration factor described below; longer global
   duration recovers for longer at the same rate and does not increase the instant part.
   Life and Mana periods are calculated independently: base seconds times the local duration factor times the
   global duration factor, divided by the local recovery-speed factor, generic character
   `flask_recovery_speed_+%` factor and matching `flask_life_recovery_rate_+%` / `flask_mana_recovery_rate_+%` factor.
   Those rate factors multiply. A hybrid retains both amounts and separate durations; accelerating Mana does not
   accelerate Life. Recovery quality affects amount; utility quality affects duration (§8a).
   The partial-instant mods (50%), "Instant Recovery" and "Instant Recovery when on Low Life" give that part
   at once. Mods that remove a percentage of recovery from the other pool apply on use, never reducing Life below 1.
   "Mana Recovery occurs instantly at the end of Effect" gives the Mana when its effect ends; effects not removed at
   a full pool stay out of the queue. The extra-Life suffix adds its own ten-second effect based on the flask's total
   Life recovery. A flask whose asynchronous charge send ends with the player dead recovers nothing
   (`FlaskRecovery.Instant`); it must not restore the player to 1 Life.
3. Each recovery component's buff goes on the player (S→C 0x1c1): BuffDefinitions row 5 `flask_effect_life` ("Healing")
   and/or row 9 `flask_effect_mana` ("Regenerating Mana"). A hybrid installs both with their independent rates,
   durations, queues and end/full-pool lifetimes. Both are invisible, cancelled on death and removed once their pool is full.
   Both rows are `IsRecovery` (+0x7c), so 0x1c1 carries one u32 per GrantedStats entry: the recovery per minute
   (`base_life_recovery_per_minute_from_flask`, 70 × 60 / 3 = 1400 for the Small Life Flask). Live 2026-10-20: without
   that value the client misread the message and showed no life change from then on, although the server sent every
   0x1b1. `Game_FLASK_BUFFS=0` recovers without the buffs. The buff carries the duration in flags bit1's field, which
   the reader `0x141fdf870` keeps as an **f32 in seconds** (+0x68; without the bit it is +infinity, `0x14015a670`), and
   the flask-bar cell as its source skill (u16 +0x60) (`Flasks.Buff`). Until 2026-10-25 a u32 3000 (milliseconds) went
   there, which the client read as a float of almost 0 s. `Game_FLASK_BUFF_DURATION=0` sends no duration. The life orb
   rose as before with the f32 duration (live 2026-10-25); the life and mana buffs are invisible and draw no timer bar.
4. `RunResourceRecovery` adds the recovery evenly by the clock, every 16 ms while a flask recovers, and sends each whole
   point at once (S→C 0x1b1): the orb rises by +1 about every 43 ms for a Small Life Flask, not in the player tick's 250
   ms steps of 6 (live 2026-10-20: those looked jerky). Recovery flasks queue (poewiki: "Recovery flasks queue up if
   multiple flasks are active. The effect with the highest regeneration rate is always applied first"): of the life (and
   hybrid) effects only the fastest recovers while the others wait with the rest of their time, the mana ones likewise
   (`Flasks.Effects`); a second use of the same flask queues another effect. Enduring ("Effect does not Queue"), the
   character's "… Flask Effects do not Queue" for its kind of flask, and the suffix's extra life run on their own. The
   buff comes off (S→C 0x1c2) when the time is up, the pool is full (unless it stays), or the player dies.

Character duration is resolved once from raw inputs by `PanelStats.FlaskDurationIncrease`:
`G = flask_duration_+% + flask_duration_+%_per_level * actual player level`, with global factor `1 + G / 100`.
Both actual use handlers capture the character stats and level together before the asynchronous charge-spend send.
They use the actual character level, not a gem level or an arbitrary `level` stat. The displayed
`virtual_flask_duration_+%` seed or developer replacement does not feed back into recovery or utility duration.
The panel's signed coefficient uses the same raw sum; its source rules are in
[11-skills.md](11-skills.md#14-character-panel-sums). Existing nonpositive-duration policy is unchanged and unproved;
the earlier controls cover positive runtime factors.

Actual recovery and utility uses now resolve global effect separately through `FlaskEffectScaling`:
`E = flask_effect_+% + actual player level * flask_effect_+%_per_level`. Captured level is clamped to 1–100; the two
raw inputs are read once with wide intermediates. Local effect and applicable magic-utility effect join this additive
bucket. The existing multiplier is `max(0, 100 + E + local + applicable magic) / 100`; no upper balance cap is added.
Recovery flasks ignore the magic-utility-only source, and normal utility flasks ignore it too. The raw character
keys 1784/20244 remain raw; the resolved amount is not written back into publication or mistaken for a new panel row.

Native Nevalius source rows 4969/36939 provide -60 plus +1 per player level, without Hardmode overrides: the global
coefficient is -50/-40/+40 at levels 10/20/100. The next use consumes the new level/source snapshot; an active utility
keeps its captured stats. Recovery quality, amount, rate, instant fraction and low-life more stages keep their
existing order. Duration remains separate. Utility flags and independent Onslaught values are not effect-scaled.
Individual utility stat projection keeps round-to-two-decimals then truncate and saturates only the native integer
representation; strongest-effect magnitude comparison uses a wide signed value. This does not harden every
later stat/pool sum against arbitrary extreme developer inputs. Existing fractional-duration conventions, including
unrounded local-duration intermediates, are not an official rounding claim.

All 39 new pure/real-handler cases pass, including native source removal, raw zero overrides, instant/partial/
low-life recovery and local/magic composition in both modes. Combined with self-duration tests, the focused batch
passes 53/53 and full Gateway 3710/3710 with no skips. Prior unchanged Core 648/Data 37 runs remain applicable
(4395 applicable cases, not all newly rerun). Full-run diagnostics retain five actual unobserved disposed-stream
task faults: four `MonsterKilled` and one `NotifyMonsterHit` → `AreaSkillHit` → `LaunchProjectile`; none mentions the
new calculation paths. Test passes are not a fault-free background-execution claim. That automated checkpoint did not
establish native coverage, legal named-unique acquisition or native item-tooltip treatment of per-level effect.

Subsequent live controls (2026-10-27, source `6bbae87`) verify eight positive per-level-effect uses at actual levels
10/100 in Standard/Ruthless. Fresh owned Default Attack records and natural DPS getter frames bind raw keys
1784 = -60 and 20244 = 1, attributes 114/114/132, and Life/Mana maxima 215/160 or 1295/700 before each use.
The native modifier vectors use controlled carriers; the new qualifier is an in-range +100 all-attributes vector.
This does not establish legal acquisition of the named uniques or validate the preserved historical +200 qualifier.

| Mode | Level | Small Life: rate per minute / seconds | Observed Life gain / fixed amount | White utility: final stat / seconds |
|---|---|---|---|---|
| Standard | 10 | 700 / 3 | 34 / 35 | movement speed +20% / 6 |
| Standard | 100 | 1960 / 3 | 97 / 98 | armour +2100 / 6 |
| Ruthless | 10 | 700 / 3 | 34 / 35 | movement speed +20% / 2 |
| Ruthless | 100 | 1960 / 3 | 97 / 98 | armour +2100 / 4 |

Each Life use spends 7 of 21 charges; each utility use spends 30 of 60. Life endpoints remain uncapped and satisfy
the original one-integer publication boundary; they do not establish official fractional rounding. Recovery strength
is present in the installed recovery-buff vector. Non-recovery utilities instead publish strength through final
character stats: movement key 245 or armour key 243. Their decoded buff vector `[0]` accompanies the timer and is
not strength evidence. Same-owned, explicit-present character samples show 0 -> the fixed strength -> 0, with no
contradictory value or positive recurrence. Native install/removal, row, slot and exact float32 duration are checked
separately; relative clocks from different observer scripts are not compared.

The first launch incorrectly used `Game_PLAYER_ATTACK=0`, which selects an invalid default effect rather than
disabling attacks; it was restored and retained as unproved. The fresh run selects `PlayerMelee`. Original verifier
failures are also retained: the stage acknowledgment has two documented advisory fields absent from its captured
identity, and the original utility expectation used the recovery-value channel. Separately reviewed verifier
revisions bind every canonical stage field/journal acknowledgment and check the proper final-stat channel without
changing the fixed strengths. The utility channel correction was declared after the Standard captures and before
the Ruthless uses; verifier code/review was completed after capture. Earlier 18 whole-zero controls remain UNPROVED.
All test stock, cells and charges were restored, representative level100 relogs passed in both modes, and the
post-run audit preserved all eight identities, both original probes, both stash banks/40 tabs and untouched level20
saves. Only normal participant last-played timestamps changed. Source/build/input hashes held, observers detached,
the client closed, and the normal `data/saves.db` services were restored. Source removal/zero next-use controls,
active-use source changes, local/magic composition, named-unique acquisition and tooltip parity remain separate.
Local receipts are under `artifacts/issue75-completion/flask-effect2-native/` (captures, operation journal, verdicts
and cleanup), `flask-effect2-native-assessment/` (frozen policy and reviewed verifier revisions), and
`flask-effect2-native-preservation/post-run-audit.json` (the independent preservation audit). These are separate
from the earlier native panel/duration batch below.

The preceding integrated panel/duration batch passed 55 new and 80 existing focused cases and full Gateway 3657/3657.
Prior Core 648/Data 37 runs remain applicable because those sources are unchanged (4342 applicable cases).
The combined native assessment has 40 completed controls: 22 complete PASS (12 positive source cases, six utility
uses and four Life uses), while 18 complete zero controls remain UNPROVED. The six panel fields total 138 VERIFIED
and 42 UNPROVED verdicts, including retained observations and clearing/restoration/relog transitions. Missing or
contradictory zero witnesses remain failures of proof; they are not counted as hidden-zero rendering.

Six actual utility uses match their fixed duration fields, including the native +30% local-duration modifier at 100:

| Mode | Level 10 white Quicksilver | Level 100 white Granite | Level 100 local-duration Granite |
|---|---|---|---|
| Standard | 13.8 seconds | 3 seconds | 3.9 seconds |
| Ruthless | 4.6 seconds | 2 seconds | 2.6 seconds |

Four actual Life uses also match their original prospective observation bounds. At level 10 the fixed amount is
161 over 6.9 seconds: Standard observed +160 and Ruthless +161. At level 100 it is 35 over 1.5 seconds: both modes
observed +34. These satisfy the original one-integer publication boundary; the discrepancy is retained and is not
an official rounding claim. The separate ordinary heal/hit/use bootstrap was captured once per control.

The final recovery3 session logged out/detached cleanly with 769 frozen inputs unchanged, preserved original saves
and all six canonical full inventories, and was followed by parent-verified client closure and normal
`data/saves.db` restoration. Earlier recovery1 binding failures, recovery2 Life-readiness failure and its vendor
response failure remain recorded separately. These results do not prove every zero row, legal acquisition of the
controlled modifier carriers, a complete on-kill engine or the full native matrix.

Raw `charges_gained_+%` sources resolve into `flask_charges_gained_+%` once before charge awards.
An explicit developer virtual replacement, including zero, keeps precedence. The resolved character factor then
multiplies the flask's local charge-recovery factor, preserving fractional carry and saturating extreme whole awards.
The source LocalExile revision also tested Ruthless active-utility prevention and its exception; those private
game-mode mechanics are not part of this branch. Current ordinary-mode charge awards keep the same source formula.
Formula evidence comes from the reference library's pinned PoB `16de4b82` `CalcPerform`/`Item.lua` and the saved Flask
page, with native Stats/Mods/BuffDefinitions identities checked locally. Automated real-handler controls cover both
modes, hybrid queues, full/end/death, instant and delayed Mana recovery. All 24 native controls pass across Standard
and Ruthless: actual installed Life/Mana buff components, rates/durations, independent expiry, resource curves and
charge awards. The native handler exposes its bounded installed-pointer vector at +0x160/+0x168; read-only
observation checks instance +0x9c and inactive flag +0xd0 bit 0x10 after natural add/remove handling.
The hybrid control installed 1200 Life/minute for 5 seconds and 1680 Mana/minute for 2.5 seconds; Mana ended first.
Ruthless preserved spent charges in town and the ordinary Nessa interaction refilled them before further controls.
Source/runtime hashes, original saves and inventory contents held, allowing flask charges and native move IDs.
Evidence: `artifacts/issue75-completion/flask-native/verified-matrix.json`, including preserved setup failures.
Queued native timer/predictor presentation and exact official-server arithmetic/rounding remain unmeasured.

## 8a. Utility flasks

A flask key (0x70) on a utility flask goes to `UseUtilityFlask` (`Program.UtilityFlasks.cs`, `UtilityFlasks.cs`).
Everything comes from the tables (`ItemCatalog.UtilityFlaskOf`): the Flasks.dat row names the flask's BuffDefinitions
row, its BuffStatValues (one value per StatsKeys entry of that row) and its duration (RecoveryTime, tenths of a second);
ComponentCharges gives its charges. This branch uses RecoveryTime, matching the pinned ordinary-mode bases:
Quicksilver and Granite both last 6 seconds. The historical LocalExile Ruthless checks used RecoveryTime2 (2 and 4
seconds respectively); mode-specific catalog selection is outside this port. Duration modifiers apply to the ordinary
bases listed below.

| Flask | Buff (row) | What it does here | Duration, charges |
|---|---|---|---|
| Quicksilver | flask_utility_sprint (121) | base_movement_velocity_+% 40 | 6 s, 30 of 60 |
| Granite | flask_utility_ironskin (120) | base_physical_damage_reduction_rating 1500 | 6 s, 30 of 60 |
| Jade | flask_utility_evasion (124) | base_evasion_rating 1500 | 6 s, 30 of 60 |
| Ruby / Sapphire / Topaz | flask_utility_resist_fire / _cold / _lightning (117-119) | resistance 40, maximum resistance 5 | 8 s, 20 of 50 |
| Amethyst | flask_utility_resist_chaos (122) | base_chaos_damage_resistance_% 35 | 6.5 s, 35 of 65 |
| Bismuth | flask_utility_prismatic (127) | base_resist_all_elements_% 35 | 8.5 s, 15 of 40 |
| Basalt | flask_utility_stone (125) | basalt_flask_armour_+%_final 20 (more armour); taunts monsters within 30 cells for 3 s | 8 s, 40 of 60 |
| Stibnite | flask_utility_smoke (128) | stibnite_flask_evasion_rating_+%_final 20 (more evasion); a smoke cloud of 16 cells that blinds | 8 s, 40 of 60 |
| Diamond | flask_utility_critical_strike_chance (116) | critical_strike_chance_+% 100 | 6 s, 20 of 40 |
| Quartz | flask_utility_phase (123) | base_spell_suppression_chance_% 10, phasing | 6 s, 30 of 60 |
| Sulphur | flask_utility_consecrate (129) | damage_+% 40; consecrated ground of 18 cells | 8 s, 40 of 60 |
| Silver | flask_utility_haste (130), invisible | Onslaught (row 215): attack, cast and movement speed 20 each | 6 s, 40 of 60 |
| Gold | flask_utility_rarity (131) | base_item_found_rarity_+% 30: rarer drops | 5 s, 60 of 80 |
| Aquamarine | flask_utility_aquamarine (126) | base_frozen_effect_on_self_+% −60: character panel only | 8 s, 20 of 50 |
| Corundum | flask_utility_stun_protection (1888) | stun_threshold_+% 30: character panel only | 7.5 s, 20 of 50 |
| Iron | flask_utility_ward (132) | base_ward 200; paid use restores Ward immediately | 4 s, 40 of 60 |

The Silver Flask's buff has no stats, only the flag `silver_flask_display_onslaught`; the Gateway also puts `onslaught`
(row 215) on the player with the values of BuffTemplates `MapModPlayerOnslaught` (20, 20, 20). How each buff stat counts
is `UtilityFlasks.SupportOf`: modelled (the pools, the walk, drops, damage), only the character panel (ailments, stun
and unsupported curse/ailment effects), or Ward.
Ward flat/increased bonuses feed the authoritative pool through `UtilityFlasks.Apply`.
After a successful charge payment and effect start, `local_flask_restore_ward` restores broken Ward immediately.
Stat projection, refused uses and ordinary effect refresh never restore Ward.
Native selected Antiquarian spent 40 charges and restored 502 Ward, returning to 22 after the four-second effect.
See [the Ward model and native evidence](../game/antiquarian.md).
Every utility flask spends its charges and shows its buff and bar.

A use:

1. is refused while dead, with fewer charges than a use needs, or while **this flask's** effect still runs (as in the
   game; the log says how long is left). Another flask of the same kind keeps its own timer: poewiki ("Effects of
   flasks"): "When creating multiple of the same (non-recovery) flask effect, each effect has an independent duration,
   and only the strongest applies"; different effects stack. So per effect (a buff row, or a "during effect" mod's stat)
   and stat the largest value applies (`UtilityFlasks.Strongest`, as Path of Building's mergeBuff), and when it ends the
   other one's rest applies again;
2. spends the charges (S→C 0xbf, as for life flasks);
3. puts the buff on the player (S→C 0x1c1, `Flasks.Buff`): f32 duration in seconds at +0x68 and the flask-bar cell as
   the source skill (+0x60). The client draws a bar under that flask that drains over the duration (live 2026-10-25;
   with source skill 0 the bar ran under the first flask whatever flask was used; flags bit0's fields, the stacks and
   the source object do not move it). Flask buffs (BuffCategory 4) do not show in the buff row; Onslaught does, with its
   countdown;
4. adds the stats to the pools: `PoolsFor` calls `UtilityFlasks.Apply` (resistances and their maxima, flat armour and
   evasion raised by their increases, increased armour and evasion, the Basalt / Stibnite more multipliers on top of
   Determination's and Grace's, movement and attack speed, critical strike chance, spell suppression) and
   `CharacterPlayerStats` adds `UtilityFlasks.RawStats` (the maximum resistances, which the client adds to its own 75;
   increased critical strike chance; increased damage; cast speed; item rarity; phasing; the panel-only stats).
   `ApplyMovementSpeed` changes the walk estimate (Quicksilver: 37 × 1.4 = 51.8 cells/s) and the stats go to the client
   (S→C 0x1b3: `movement_velocity_+%` 40 reached the client's stat setter `0x1424cfaf0`, live 2026-10-25). So
   `/defences`, `/resistances`, `/hit` and the character panel see them. The effect starts and ends under the
   connection's Monsters lock with the pools recomputed in the same block (`UtilityFlasks.Start` / `EndDue`), as every
   pools writer takes it (`SendEquipment` too, which now also applies the walk speed);
5. makes what the flask's implicit makes (`FlaskGrounds`, §8a below);
6. ends when its time is up, by the connection clock (`RunUtilityFlasks`, every 16 ms while any runs; a pause stops the
   clock, so it holds), when its flask leaves its cell of the flask bar, or at death: the buffs come off (S→C 0x1c2) and
   the stats and movement speed go back. An area change starts a new connection without the effect. The ticker starts
   before anything is sent and a failed step is logged and retried; however it exits, the next use starts one again
   (`Effects.TickerExited`). That the effect ends when the flask leaves its cell is **not confirmed by any source**
   (poewiki, the Fandom mirror, Path of Building and the forum don't say): `UtilityFlasks.EndsWhenFlaskLeavesItsCell`;
   flip it if the real game keeps the effect running.

Duration and effect follow the pinned Path of Building `Item.lua`/`CalcPerform` model. Utility duration is base
seconds times the item-local duration factor, the character global duration factor from §8, quality, and the
`local_flask_duration_+%_final` factor. Item-local and global increases multiply; they are not one additive bucket.
For example, a 6-second base with 30% local and 20% global duration lasts `6 * 1.3 * 1.2 = 9.36` seconds before
quality or local more modifiers. Quicksilver with only 20% quality remains 7.2 seconds in ordinary mode.
The raw global sum includes the per-player-level source once and ignores the virtual display seed.
Effect uses §8's raw flat-plus-player-level resolver, adding local effect and `magic_utility_flask_effect_+%` only
on a magic utility flask. Onslaught keeps its own separate effect stat. Values use the existing PoB `ScaleAddMod`
rounding projection with final integer saturation; that representational guard is not an upper balance cap.
Global duration, local duration and effect are distinct operations. The new per-level effect source does not establish
all unique acquisition/conditional mechanics, native tooltip behavior or official fractional duration rounding.

The "during effect" suffixes (`UtilityFlasks.DuringEffect`) count while the flask runs, scaled by its effect: increased
armour (`local_flask_armour_+%_while_healing` → `physical_damage_reduction_rating_+%`), increased evasion, movement
speed, additional elemental resistances, increased critical strike chance and increased Ward; the
ailment, stun and curse ones (avoid, reduced effect or duration on you, immunities, "chance to Freeze, Shock and
Ignite", stun recovery) reach the character panel only; life and energy shield leech are not applied (leech is not
modelled).

**What some flasks make on use** (`FlaskGrounds.cs`, `Program.FlaskGrounds.cs`), from their implicit mods, where the
player stands (radii in cells):
- Stibnite (`local_smoke_ground_on_flask_use_radius` 16): a smoke cloud. Monsters standing in it are blinded (buff 179
  `blinded`): 20% less accuracy against the player (`MonsterSwing`) and 20% less evasion against the player's attacks
  (Path of Building's Blind; the tables have no default value for it). Live: Hillock's accuracy 14 → 11.
- Sulphur (`local_consecrate_ground_on_flask_use_radius` 18): consecrated ground. Standing on it the player regenerates
  5% of maximum life per second (`life_regeneration_rate_per_minute_%` 300) and curses are 50% less effective (Path of
  Building; the stat's reminder text), with the `ground_consecration` buff (208) and the stats sent again.
- Basalt (`local_flask_taunt_enemies_on_use_radius` 30): `taunted` (408) on the hostile monsters around for 3 s (the
  reminder text); with one player that changes nothing else.
- Aquamarine's chilled ground (`local_flask_chilled_ground_on_flask_use_radius` 28) is not made: chilling monsters needs
  their slower walks and attacks to match on the client, which is not done. The ground shows the GroundEffects art
  (`ground_effects_v2/consecrated/consecrated_01.ao`, `smoke_blind/smoke_blind.ao`) as an effect object (`EffectObject`)
  scaled from GroundEffects __col4 (taken as the art's radius in world units) to the radius; live, the consecrated
  ground's ring and light show, the smoke cloud's art does not show. The grounds last as long as the flask's effect: the
  tables give no duration for them. `RunFlaskGrounds` looks every 100 ms of game time.

**Quartz Flask's phasing.** The client's Pathfinding keeps "Phase Through Objects" (+0x52e) from the player's
`virtual_phase_through_objects` (`0x141e9cd68`) and picks its blocker filter with it (`0x141e7b77c`). The Gateway sends
`virtual_phase_through_objects` 1 while a Quartz Flask runs (live: the client's stat setter got 1), and its walk
estimate then does not count monsters as blockers (`ClientState.Phasing`); NPCs and stashes still block, since which
objects the client's filter lets through is not traced.

**Gold Flask's rarity.** The player's increased rarity of items found (`base_item_found_rarity_+%` 30 from the buff,
`item_found_rarity_+%` from gear and passives) multiplies the magic and rare chances of dropped equipment
(`ItemDrops.WithIncreasedRarity`); the drop table's own chances stay ours (§5, [15](15-progression.md) §3).

Live 2026-10-25 (OfflineWitch; a key press, then the control bridge's `flask:use`, with Frida on the client's stat
setter): Quicksilver spent 30 of 60 charges, the client's `movement_velocity_+%` went 0 → 40 → 0 after 5.94 s, and a
second use 2.5 s in was refused; two Quicksilvers, a plain one and one with Alchemist's (25% increased effect, 25%
reduced duration) and the 14% movement suffix: 40 → 67 (max(40, 50) + 17) → 40 when the stronger ended at 4.5 s → 0 at 6
s, both bars draining; Ruby: Fire 13% → 53% (maximum 75% → 80%), `/hit fire 1000` took 470 instead of 870; Sapphire and
Topaz: cold and lightning 53%, maxima +5; Granite and Jade: armour 0 → 2145, evasion 16 → 1636, then Basalt on top:
2574; Amethyst and Bismuth: all elements 48%, chaos 43%; Diamond: the client's main-hand critical chance 16.60%;
Quartz: spell suppression 10%, `virtual_phase_through_objects` 1; Sulphur: the ground's art, the client's life
regeneration 15090 → 24840 per minute on it; Stibnite: monsters blinded in the cloud, accuracy 14 → 11, the player's
evasion 46 → 55; Basalt: two monsters taunted; Silver: the Onslaught icon with its countdown, attack and cast speed 5% →
25%; Gold, Corundum, Aquamarine: rarity 30%, stun threshold 30%, frozen effect −60% on the client; Iron: its buff and
bar for 4 s; a pause of 9 s during Quicksilver: it ended only after the resume, 6 s of game time in all; death ended
Quicksilver at once and a use while dead was refused; `/goto 1_1_town` during Ruby: 13% again in town; picking Basalt up
off the bar ended its effect at once. Not seen live: the client actually walking faster or phasing through monsters (the
bridge cannot make it walk), the tooltip's duration with quality.

**Mageblood:** while the unique belt is worn, the leftmost magic utility flasks apply their effects permanently without
charges or a duration (`Program.Mageblood`, §2e); using them is refused, and the character's stats are re-sent when the
bar changes.

## 8b. Flask charges

`FlaskCharges.cs`, `Program.FlaskCharges.cs`; poewiki (Flask, "Flask charges").
- **Per use and maximum**: charges used = floor(base × (1 + the flask's `local_charges_used_+%`)), then × (1 + the
  character's `flask_charges_used_+%`), floored; maximum = base + `local_extra_max_charges` (Path of Building Item.lua).
- **Kills** give every flask 1 charge × (1 + the monster rarity mod's `monster_slain_flask_charges_granted_+%`: Mods.dat
  MonsterMagic6 +250, MonsterRare6 +500, MonsterUnique6 +1000), that is 1 / 3.5 / 6 / 11 as poewiki gives them. Acts 6
  to 10 and maps give fewer (poewiki) by an amount the tables don't give; that is not applied, nor are single monsters'
  own `monster_slain_flask_charges_granted_+%` from their .ot files.
- **Every gain** is × (1 + the character's `flask_charges_gained_+%`) × (1 + the flask's `local_charges_added_+%`,
  "increased Charge Recovery"), which "stack multiplicatively"; partial charges are kept to two decimals per flask
  (`FlaskCharges.Carry`, for the connection: they are not saved).
- **Critical strikes the player deals** (a hit that landed on a living monster): a flask's own "#% chance to gain a
  Flask Charge when you deal a Critical Strike" (`local_recharge_on_crit_%`) gives it one; the character's
  `gain_flask_chance_on_crit_%` (a chance of one) and `recharge_flasks_on_crit` ("Gain # Flask Charges when you deal a
  Critical Strike", and `recharge_flasks_on_crit_while_affected_by_precision` while Precision runs) give the first
  flask that is not full (`FlaskCharges.CritSources`); one cooldown for all of them, 0.1 s, which the game's 0.033 s
  server frames make 0.132 s (poewiki), not affected by cooldown recovery, looked at before anything else
  (`FlaskCharges.OnCrit`). The hooks are one line after each of the player's crit rolls in `Program.Combat.cs`. Not
  applied: `gain_flask_charge_when_crit_%` / `_amount` (charges when the player *takes* a critical strike: monsters
  deal none here) and `gain_flask_charge_on_crit_chance_%_while_at_maximum_frenzy_charges` (no frenzy charges here).
- **Hits**: "Gain # Charges when you are Hit by an Enemy" when a monster's hit lands (not evaded or blocked).
- **Every 3 seconds**: the character's `flask_charges_recovered_per_3_seconds` (every flask),
  `utility_flask_charges_recovered_per_3_seconds`, `life_flask_charges_recovered_per_3_seconds` (and
  `life_flask_charges_recovered_per_3_seconds_on_low_life` on low life) and
  `mana_flasks_gain_X_charges_every_3_seconds`, by game time (`FlaskCharges.PerThreeSeconds`).
- **One change at a time**: every change of the flask bar (a use's charges, any gain) and its S→C 0xbf go out under the
  connection's flask-bar gate (`ChangeFlaskBar`), so the client gets the revisions in order with the latest charges; a
  use checks its charges again under it.
- **Towns and hideouts** fill every flask on entering, and so does resurrection; a flask put on the flask bar in a
  hostile area has no charges (poewiki: "If a player equips a flask within a hostile area, the flask has zero active
  charges by default").
- `Game_FLASK_CHARGES=full` brings back the shortcut for local play: every kill fills every flask, and a flask put on
  the bar keeps its charges.

Live 2026-10-25 (The Twilight Strand): a normal Drowned's kill gave every flask that was not full 1 charge (30 → 31);
two magic ones 3 and 4 (31 → 34 → 38, the half kept); a Jade Flask put on the bar there had 0 of 60 charges and its use
was refused. Then (after the review fixes): with a Diamond Flask carrying Surgeon's 35% and `recharge_flasks_on_crit`
1, of 25 hits on a rare Drowned two were critical; the first gave the first flask that was not full 1 (14 → 15) and the
Diamond its roll (20 → 21), the second only the sure charge (15 → 16); a Small Life Flask with "40% of Flask's Life
Recovery Amount" used below full life added 28 more life over 10 s (a 3250-life character). Not seen live: charges when
hit and every 3 seconds (tests only).

### Use confirmation and drinking sound (S→C 0x263)

Updating charges and adding the recovery buff do not play the drinking sound.
The server must also send the item-use result, only after a successful use:

| Wire field | Meaning |
|---|---|
| u16 BE `0x263` | Message id |
| u32 BE | Used item's BaseItemTypes HASH32 |
| u32 BE | Inventory id (`12` for the flask bar, not its row `11`) |
| u32 BE | Item id, not its flask-bar cell |
| u16 BE `0` | Empty string (no sound override) |
| u32 BE | Result: `0` success, `0x4e2` full recovery pools |

Evidence: official 3.28 captures in the older project's `out/capture_stream.jsonl` contain
`0263403b9b3b0000000c00000002000000000000` (life) and
`026393c8c37f0000000c00000004000000000000` (mana).
The current 3.29.3b reader `0x1421aded0` confirms the same layout, including the **four-byte** wire result
which it truncates into the message's u16 field at `+0x40`.
Dispatcher case `0xc6` at `0x14215391b` fires the instance's `+0x3c78` listeners through `0x142171890`.
UI callbacks `0x140d95a70` / `0x140ddcd60` use the type hash to look up BaseItemTypes and its use sound.
With an empty override and result zero, the sound comes from the base row's `+0x58` reference, then its `+0x10` path.
A nonzero result takes the failure branch instead; `0x4e2` is used for full-pool refusals without a drinking sound.

Live verification on 3.29.3b: normal C→S `0x70` requests for Small Life and Small Mana Flasks reached
`0x140ddcd60`, selected `Audio/Sound Effects/ItemSounds/flaskdrink.ogg`, and submitted it to audio `0x140ff6320`.
The read-only probe is `scripts/re/frida/trace-flask-use.js` (load after `common.js`).
Tests compare `InstanceItems.ItemUsed` against both official capture payloads.

Listening checks initially raised doubt about `flaskdrink.ogg`.
A fresh read-only capture from the user's official 3.29.3b Steam client confirmed the same path on successful
Small Life Flask use: `0x263`, hash `0x403b9b3b`, inventory `12`, item `8`, empty override, result `0`.
The native callback selected `Audio/Sound Effects/ItemSounds/flaskdrink.ogg`; the user also reported it sounded alike.
Other captured uses returned `0x4e2`; no successful mana use was captured in this official session.
`SoundEffects` row `Flask` is not a substitute: its UI path `flask.ogg2d` was identified by the user as inventory
placement; substituting its other path `flask.ogg` into the UI callback was silent.
All temporary substitutions and the official-client capture were detached; native default selection remains unchanged.

Full-pool gate verified live locally: life `85/85` and mana `63/63` produce `0x263` result `0x4e2` plus the
authoritative `0x1b1` of that pool (so the orb matches the server), with no inventory charge update, no recovery buff
and no drinking audio submission.
After `/hit chaos 20`, life-flask use spends charges, adds its buff, recovers life and plays `flaskdrink.ogg`;
after `/heal`, another use is refused again.
`FlasksTests` cover full pools, one-point deficits, irrelevant missing resources, reservation caps and hybrids.

### The client pays for attack-moves the server only walks

A read-only probe of a real input session (2026-10-26) showed the **client** spending the attack skill's mana on every
attack-move use (Holy Strike: 6 mana per attack time), while the Gateway's attack-move branch only walks the player:
the client's orb fell to 20/63 while the server still held 63, so every mana-flask press came back `0x4e2`.
The Gateway now answers each attack-move with the authoritative mana (S→C `0x1b1`, at most four a second because the
client repeats the use every frame while the button is held), giving the predicted cost back.
A point within the weapon's reach is a swing at the ground instead of an attack-move, so standing and swinging costs
the skill's mana as on the client (live 2026-10-26: it used to cost nothing).
A full-pool refusal carries the authoritative pool too, so the orb and the next press agree.
Probe: `scripts/re/frida/trace-flask-mana.js` (load alone, no `common.js`); test: `ManaCostTests` pins Holy Strike's
6 mana at level 1.

## Open

- Flasks: fewer kill charges in acts 6 to 10 and maps (the amount is not in the tables), single monsters' own flask
  charge stats, partial charges across connections (not saved), queued native timer/predictor presentation,
  the effects across an area change (they end), the "during any flask effect" passives, remaining conditional effect
  operators and nonpositive-duration policy. Flat/per-level global effect has eight positive runtime controls (§8);
  source-removal/zero next-use behavior and whole-unique native acquisition/tooltip parity remain unproved.
  Hybrid Life/Mana buffs are implemented; see §8.
  Utility flasks: unsupported ailments, stun modifiers and curses on the player
  (Aquamarine's freeze effect, Corundum, the ailment suffixes: panel only); Aquamarine's chilled ground; the smoke
  cloud's art; which objects phasing lets the player through besides monsters; how long the grounds last; whether taking
  a flask off the bar ends its effect (it does here, `UtilityFlasks.EndsWhenFlaskLeavesItsCell`); leech suffixes.
- Gem levels and experience, support gems and links.
- The game's own affix counts, rarity chances and drop rates; unsupported currency and individual local/unique /
  corrupted / crafted / influenced item mechanics (#60), beyond the implemented weapon and defence rules in §2;
  unexplained ObjectMagicProperties bit 9, unsupported quality effects and remaining meanings of
  Base +0xc0, Base +0xc7 bits 1, 4, 6, 7 and Mods flags bits 0, 8, 9 (§2f).
- C→S 0x2d and 0x12 (5 bytes, seen once); 0x4a's flag bit 1, its u16 and its other callers (`0x140b34140`,
  `0x14031c0a0`: probably currency use on an item).
- Unsupported currencies beyond those implemented in §4.
  Ctrl + click of a stack between inventories; a ground stack that only partly fits stays
  on the ground whole (the real game takes what fits).
- Another connection in the same instance sees items dropped meanwhile only when it re-enters (no broadcast; the
  Gateway shows each client only its own player).

### Currency metamod protection

Currency mutations snapshot positive explicit metamod stats before changing the item. Scouring, Annulment, Divine and Chaos preserve existing prefixes/suffixes protected by their respective locks, as well as fractured affixes. Scouring removes the opposite-side protecting craft, retains quality, sockets, links and non-explicit mods, refuses uniques, and derives rarity from retained explicit count (0 normal, 1 magic, 2+ rare). Annulment retains rarity and also excludes attack/caster-tagged affixes when their cannot-roll craft is present. Exalt, Regal and Augment allow additions to a locked side, but filter cannot-roll attack/caster tags. Reforges use cannot-roll restrictions from retained crafts; an unprotected craft removed by Chaos does not restrict the new pool. Bench selective replacement uses the same protection and generation rules. Essence rejects the four outcome-changing metamods (multimod remains allowed). Socket/quality/implicit currencies do not alter explicit affixes.

Regression coverage: MetacraftTests, real data prefix/suffix lock removal, Divine/Chaos preservation, annul tag protection and added-affix tag exclusion. These fixes do not implement currencies absent from UseItemOn, or replace the documented local Chance/Vaal outcome distributions with official probabilities. References: https://www.poewiki.net/wiki/Metamod and https://www.poewiki.net/wiki/Orb_of_Scouring.
