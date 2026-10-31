# Fracturing Orb and Mirror of Kalandra: game rules

## Scope

`CurrencyFractureRare` and `CurrencyDuplicate` use the shared 0x71/0x72 currency path. `CurrencyEffects.Fracture`
and `CurrencyEffects.Mirror` delegate to `ItemRolls`, where the admission checks live. The local rules follow the
3.29.3b game data, the wiki and the external reference library's beta engine; no runtime dependency on the library
is added.

## Pinned references

External library: Craft of Exile beta, dataset `3.29.3.1.2`, combined snapshot
`20261024T171803Z-combined-3.29.3.1.2`. Exact correspondence to official 3.29.3b is explicitly unverified by the
source.

| Asset | SHA256 |
|---|---|
| `package.js` | `09f9e940bdfbb38e1edc3af84d6008cc1c3758408dbb35504eb70e0c18fccf41` |
| `data.js` | `280792696ffa6700d389012accd357d9a708f8b7adc0bcb5ca42fe544f6a8489` |
| `extended.js` | `5bb7c9ad296397a565c141f362cbbe54a9316aa56cd16018986b19234e7ffab9` |
| `calculatorProcessor.js` | `e804ac08a0a5e34ec50499bfa937fc57e12021e960e86650f7b4a5b4a980a559` |

Wiki pages (saved revisions): [Fracturing Orb](https://www.poewiki.net/wiki/Fracturing_Orb) `wiki/10177`,
revision `1681353`, 2026-07-26; [Fractured item](https://www.poewiki.net/wiki/Fractured_item) `wiki/10171`,
revision `1701034`, 2026-08-17; [Mirror of Kalandra](https://www.poewiki.net/wiki/Mirror_of_Kalandra) `wiki/16591`,
revision `1681148`, 2026-07-26; [Mirrored](https://www.poewiki.net/wiki/Mirrored) `wiki/16586`, revision `1493172`,
2024-12-21; [Equipment](https://www.poewiki.net/wiki/Equipment) `wiki/8820`, revision `1611817`, 2026-02-09.

## Fracturing Orb

The wiki's item description is `Fracture a random modifier on a rare item with at least 4 modifiers, locking it in
place`; its help text adds `Cannot be used on Influenced or Fractured items.` The mechanics section adds:

- one random explicit modifier on a rare item with 4 or more modifiers;
- the modifiers are not rerolled;
- metamods are not respected;
- Influenced, Synthesised and Fractured items are refused (the 3.29.0 note only removed the explicit Synthesised
  text from the item description; existing Synthesised items still cannot be fractured);
- items with Eldritch implicit modifiers and Split items are allowed;
- Strongboxes cannot be modified.

The beta engine's `methods` lists `fracture` with the constraints `rarity_rare`, `minimum_4_explicits`,
`can_fracture`, `can_be_rare` and `not_strongbox`. Its `canFracture` refuses an item that is already fractured,
synthesised or influenced. Its `fracture(1)` collects the explicit modifiers (veiled ones only when the engine allows
fracturing veiled mods), draws one uniformly, and marks it fractured without touching the values.

The game data adds the class gate: `ItemClasses.CanBeFractured` is true for equipment, flasks, jewels, maps,
contracts and logbooks, and false for currency, gems, fragments, tinctures and relics. The local
`ItemCatalog.BaseType.CanBeFractured` reads that column.

Local rule (`ItemRolls.Fracture`): the item must be rare, have at least four explicit mods, be neither corrupted,
mirrored nor already fractured, have a supported base with `Mods`, not be `Unmodifiable`, and its class must have
`CanBeFractured`. One explicit is drawn uniformly (metamod-protected mods included), moved to the front of the
explicits, and counted by `Item.FracturedCount`. Influenced and synthesised items are refused; veiled modifiers are not modelled by
LocalExile, so those refusals cannot be exercised locally.

## Mirror of Kalandra

The wiki says the Mirror creates a mirrored duplicate of `any piece of non-unique equipment, non-corrupted item that
is not itself a duplicate`. The help text is `Right click this item then left click an equipable non-unique item to
apply it. Mirrored copies cannot be modified.` The [Mirrored](https://www.poewiki.net/wiki/Mirrored) page adds that
mirrored items `cannot be altered in any way except through the use of currency items that explicitly affect mirrored
items, such as Reflective Oil`.

The [Equipment](https://www.poewiki.net/wiki/Equipment) page defines equipment as weapons, off-hands, armour and
jewellery. Flasks and trinkets are equippable but are not "equipped items"; jewels are socketables, not equipment.
The game data agrees: `ItemClasses.__col21` (unnamed in the dat schema) is true for the equipment classes (weapons,
armour, jewellery and quivers) and false for flasks, jewels, maps and currency. `ItemCatalog.BaseType.CanBeMirrored`
reads that column.

The beta engine has no Mirror method; it only carries a mirrored item flag. The wiki is therefore the rule source
for the target class and the uniqueness check.

Local rule (`ItemRolls.Mirror`): the item must be non-unique, not corrupted, not already mirrored, have a supported
base with `Mods`, not be `Unmodifiable`, and its class must have `CanBeMirrored`. The copy keeps every property
(mods and values, fractured mods, quality, sockets, links, socketed gems, rarity, identification and name) and sets
`Item.Mirrored`; the original is unchanged. `Program.MirrorItem` puts the copy on the cursor and spends one Mirror.
A mirrored item takes no currency at all, including a Scroll of Wisdom; Reflective Oil, Split and Sanctified items
are not modelled. Using a Mirror from a stash tab is a separate local limitation (`MirrorItem` still requires the
inventory), not a game rule.

## Automated coverage

`FractureTests` covers the four-mod success, the first-explicit placement, value preservation, three-mod, non-rare,
already-fractured, corrupted/mirrored, unmodifiable, non-fracturable-class and unknown/no-`Mods` refusals.

`FractureGatewayTests` sends framed 0x71 and 0x72 messages through `Program.OnClientMessages`: all inventory/stash
and Shift combinations, one orb spent per success, save reload, and refusals without spending.

`MirrorTests` covers normal/magic/rare equipment, unique, non-equipment, corrupted, already-mirrored, unmodifiable,
unknown/no-`Mods` refusals and the preserved properties of the copy.

`MirrorGatewayTests` sends framed 0x71 and 0x72 messages: the mirrored copy on the cursor, one Mirror spent, the
original unchanged, save reload, a second use refused while the cursor is occupied, unique/non-equipment/corrupted
refusals without spending, and a Scroll of Wisdom refused on a mirrored item.

## Native acceptance

2026-10-30, 3.29.3b client (`MyWitch`, account `localexile`), Gateway built from this branch. The client bridge
created the items and sent framed 0x71 messages; the Gateway log and the 0xbf updates were checked.

- Fracturing Orb on a four-mod rare Driftwood Wand: the 0xbf update changed the wand, the Gateway log listed the
  first explicit mod as the fractured one, and the client accepted the item. A second use on the same wand was refused
  and the orb stack stayed at 4.
- Mirror of Kalandra on a rare Driftwood Wand: the 0xbf cursor update carried the mirrored copy, the original stayed
  unchanged, and the Mirror stack went from 5 to 4.
- Mirror on Mageblood (unique), a Diamond Flask and a Crimson Jewel: all refused without spending.
- Scroll of Wisdom on the mirrored copy: refused without spending.
- The client accepted every 0xbf update; the fractured/mirrored tooltip payload is unchanged from the 2026-10-21 live
  check. A rare item of a non-fracturable class is covered by the unit and gateway tests: the native `/item` command
  creates watchstones normal, so that class refusal was not exercised natively.
