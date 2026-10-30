# Enkindling Orb: CoE comparison

## Scope

`CurrencyEnkindlingOrb` uses the shared 0x71/0x72 currency path and `ItemRolls.Enkindling`; the Gateway handler
spends one orb on success. It is a utility-flask currency, not an affix currency.

The local rule: a utility flask of any rarity receives one of the five official Enkindling enchantments
(GenerationType 21). An existing Enkindling is replaced; an existing Instilling (GenerationType 22) is kept.
Harvest/bench flask enchantments (GenerationType 3, family `FlaskEnchantment`) and other enchant-list mods are
replaced. Explicit affixes, other implicits, quality, charges, rarity and name stay. Life and mana flasks are refused
because the game only allows utility flasks. Corrupted, mirrored, unsupported and unmodifiable targets are refused
before spending.

The enchantment is stored as a normal `ModSection.Enchant` entry, the same list the client already uses for bench
enchants.

## Pinned reference

Queried the external library's beta engine, dataset `3.29.3.1.2`, combined snapshot
`20261024T171803Z-combined-3.29.3.1.2`. Exact correspondence to official 3.29.3b is explicitly unverified by the
source.

| Asset | SHA256 |
|---|---|
| `package.js` | `09f9e940bdfbb38e1edc3af84d6008cc1c3758408dbb35504eb70e0c18fccf41` |
| `data.js` | `280792696ffa6700d389012accd357d9a708f8b7adc0bcb5ca42fe544f6a8489` |
| `extended.js` | `5bb7c9ad296397a565c141f362cbbe54a9316aa56cd16018986b19234e7ffab9` |
| `calculatorProcessor.js` | `e804ac08a0a5e34ec50499bfa937fc57e12021e960e86650f7b4a5b4a980a559` |

The CoE engine declares `enkindlingTypeId: 21` and `instillingTypeId: 22`; its `flaskEnchantParameters` passes that
type to `rollAffixes(1, ...)`, and `flaskEnchant` uses the same GenerationType values as the game's Mods.dat.

## Data

The game tables contain exactly five GenerationType 21 rows. All are Domain 1, ModType `FlaskEnchantmentEnkindling*`,
level 1, and spawn with the `flask` tag.

| Mods.dat row | Stats | Range | Text |
|---|---|---|---|
| `FlaskEnchantmentSealIncreasedEffect_` | `local_flask_cannot_gain_charges_during_flask_effect`, `local_flask_effect_+%` | 1, 60-70 | `Gains no Charges during Effect`; `(60-70)% increased effect` |
| `FlaskEnchantmentSealIncreasedDuration` | `local_flask_cannot_gain_charges_during_flask_effect`, `local_flask_duration_+%` | 1, 80-100 | `Gains no Charges during Effect`; `(80-100)% increased Duration` |
| `FlaskEnchantmentSealIncreasedChargesGained` | `local_flask_cannot_gain_charges_during_flask_effect`, `local_charges_added_+%` | 1, 60-80 | `Gains no Charges during Effect`; `(60-80)% increased Charge Recovery` |
| `FlaskEnchantmentSealReducedChargesUsed` | `local_flask_cannot_gain_charges_during_flask_effect`, `local_charges_used_+%` | 1, -35 to -25 | `Gains no Charges during Effect`; `(25-35)% reduced Charges per use` |
| `FlaskEnchantmentSealMaximumCharges____` | `local_flask_cannot_gain_charges_during_flask_effect`, `local_extra_max_charges` | 1, 40-60 | `Gains no Charges during Effect`; `+(40-60) to Maximum Charges` |

Spawn weights are 250 for the increased-effect row and 100 for the other four (total 650). The local
`ItemRolls.EnkindlingPool` keeps exactly these five rows: Domain 1, GenerationType 21 and a positive
`WeightFor(flask tags)`.

The wiki's [Enkindling Orb](https://www.poewiki.net/wiki/Enkindling_Orb) page gives the same five effects and says the
orb is used on a utility flask. The item description is `Adds an enchantment to a utility flask ... Replaces any
existing enchantment`.

## Coexistence and replacement scope

Enkindling and Instilling are separate enchantments. Applying Enkindling replaces the old Enkindling, keeps an
existing Instilling, and removes Harvest/bench flask enchantments and other enchant-list mods. This is the rule the
native 3.29.3b client confirmed on 2026-10-30: both lines remained on one flask, and repeated Enkindling replaced only
the Enkindling line.

Evidence for the separation:

- The wiki [Crafting](https://www.poewiki.net/wiki/Crafting) page lists both orbs as "Adds or replaces an enchantment
  on a flask"; the [Modifier](https://www.poewiki.net/wiki/Modifier) page treats enchantments as a separate modifier
  layer.
- The wiki's [List of flask enchantment modifiers](https://www.poewiki.net/wiki/List_of_flask_enchantment_modifiers)
  explicitly excludes the Instilling and Enkindling lists; the three families are separate.
- Path of Building's `Data/EnchantmentFlask.lua` has separate `ENKINDLING` and `INSTILLING` tables, and
  `Classes/Item.lua` keeps every enchant line in `enchantModLines` rather than one shared slot.
- The native client showed both lines on one flask and preserved the other line across repeated uses.

CoE's `flaskEnchant` removes both GenerationType 21 and 22 rows before rolling. Its engine therefore does not model
the coexistence the game client shows; the local rule follows the game data, wiki, PoB and the native client, not
CoE's replacement step. CoE is used here for the pool, weights, ranges and target class.

## Harvest/bench family

The four `HarvestFlaskEnchantment*` rows (GenerationType 3, Domain 2, family `FlaskEnchantment`) are the old
Harvest/bench flask enchantments. Harvest flask enchantments were removed in 3.28, but legacy items can still carry
them. The local rule removes them when either orb is applied, while preserving unrelated implicits. The 2026-10-30
native run used new flasks and did not exercise a legacy Harvest item; the removal is covered by unit tests.

## Automated coverage

`FlaskEnchantTests` checks the five-row pool, the 250/100 weights, all five outcomes, the no-charge stat and value
ranges, coexistence and same-type replacement, Harvest removal with unrelated implicit preservation, every rarity,
life/mana/non-flask/corrupted/mirrored/unknown refusal.

`FlaskEnchantGatewayTests` sends framed 0x71 and 0x72 messages through `Program.OnClientMessages`: all inventory/stash
and Shift combinations, one orb spent per success, coexistence after two different orbs, explicit-affix and quality
preservation, save reload, and refusal without spending.

Native acceptance: 2026-10-30, 3.29.3b client; Enkindling and Instilling both appeared on one flask, repeated use
replaced only its own type, magic explicit affixes stayed, and life/mana flasks were refused.
