# Instilling Orb: CoE comparison

## Scope

`CurrencyInstillingOrb` uses the shared 0x71/0x72 currency path and `ItemRolls.Instilling`; the Gateway handler
spends one orb on success. It is a utility-flask currency, not an affix currency.

The local rule: a utility flask of any rarity receives one of the fifteen spawnable official Instilling trigger
enchantments (GenerationType 22). An existing Instilling is replaced; an existing Enkindling (GenerationType 21) is
kept. Harvest/bench flask enchantments (GenerationType 3, family `FlaskEnchantment`) and other enchant-list mods are
replaced. Explicit affixes, other implicits, quality, charges, rarity and name stay. Life and mana flasks are refused
because the game only allows utility flasks. Corrupted, mirrored, unsupported and unmodifiable targets are refused
before spending.

The enchantment is stored as a normal `ModSection.Enchant` entry.

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

The CoE engine declares `instillingTypeId: 22`; its `flaskEnchantParameters` passes that type to
`rollAffixes(1, ...)`, and `flaskEnchant` uses the same GenerationType values as the game's Mods.dat.

## Data

The game tables contain sixteen GenerationType 22 rows. All are Domain 1, ModType `FlaskEnchantmentInjector*`, level 1,
and spawn with the `flask` tag. Fifteen have spawn weight 100; `FlaskEnchantmentInjectorOnGuardSkillExpired` has
weight 0 and is not obtainable from the orb or the bench. The local `ItemRolls.InstillingPool` keeps the fifteen
positive-weight rows.

| Mods.dat row | Text |
|---|---|
| `FlaskEnchantmentInjectorAdjacentFlasks___` | `Used when an adjacent Flask is used` |
| `FlaskEnchantmentInjectorFlaskEffectEnd` | `Reused at the end of this Flask's effect` |
| `FlaskEnchantmentInjectorOnFullCharges__` | `Used when Charges reach full` |
| `FlaskEnchantmentInjectorOnGuardSkillUsed` | `Used when you Use a Guard Skill` |
| `FlaskEnchantmentInjectorOnTravelSkillUsed` | `Used when you Use a Travel Skill` |
| `FlaskEnchantmentInjectorOnHittingRareOrUnique_` | `Used when you Hit a Rare or Unique Enemy, if not already in effect` |
| `FlaskEnchantmentInjectorOnAffectedByFreeze__` | `Used when you become Frozen` |
| `FlaskEnchantmentInjectorOnAffectedByChill` | `Used when you become Chilled` |
| `FlaskEnchantmentInjectorOnAffectedByShock_` | `Used when you become Shocked` |
| `FlaskEnchantmentInjectorOnAffectedByIgnite` | `Used when you become Ignited` |
| `FlaskEnchantmentInjectorOnAffectedByBleed` | `Used when you start Bleeding` |
| `FlaskEnchantmentInjectorOnAffectedByPoison_` | `Used when you become Poisoned` |
| `FlaskEnchantmentInjectorOnDamageBlocked` | `Used when you Block` |
| `FlaskEnchantmentInjectorOnTakingSavageHit_` | `Used when you take a Savage Hit` |
| `FlaskEnchantmentInjectorOnUsingLifeFlask` | `Used when you use a Life Flask` |
| `FlaskEnchantmentInjectorOnGuardSkillExpired` | `Used when you lose a Guard Skill Buff` (weight 0, excluded) |

Every eligible row has one stat with value 1. The local `ItemRolls.InstillingPool` keeps the fifteen rows with
Domain 1, GenerationType 22 and a positive `WeightFor(flask tags)`.

The wiki's [Instilling Orb](https://www.poewiki.net/wiki/Instilling_Orb) page gives the same trigger list and says the
orb is used on a utility flask. The item description is `Adds an enchantment to a utility flask ... Replaces any
existing enchantment`. The 3.15.2 patch notes added crafting-bench recipes for all possible Instilling enchantments;
`CraftingBenchOptions.json` has fifteen `AddEnchantment` rows for item class 33 (utility flask), exactly the fifteen
positive-weight rows.

Path of Building's `Data/EnchantmentFlask.lua` lists sixteen Instilling lines, including the zero-weight
`Used when you lose a Guard Skill Buff`; PoB's table is a data dump, not the orb's spawn-weight filter. The local pool
follows the game's spawn weights and the bench's fifteen recipes.

## Coexistence and replacement scope

Instilling and Enkindling are separate enchantments. Applying Instilling replaces the old Instilling, keeps an
existing Enkindling, and removes Harvest/bench flask enchantments and other enchant-list mods. The native 3.29.3b
client confirmed on 2026-10-30 that both lines remain on one flask and that repeated Instilling replaces only the
Instilling line. See [the Enkindling comparison](enkindling-coe.md) for the evidence for the separation; CoE's
`flaskEnchant` removes both types before rolling, so its engine does not model the coexistence the client shows.

## Harvest/bench family

The four `HarvestFlaskEnchantment*` rows (GenerationType 3, Domain 2, family `FlaskEnchantment`) are the old
Harvest/bench flask enchantments. They were removed in 3.28 but legacy items can carry them; the local rule removes
them when either orb is applied, preserving unrelated implicits. The native run used new flasks, so legacy Harvest
removal is covered by unit tests only.

## Automated coverage

`FlaskEnchantTests` checks the fifteen-row pool and equal weights, the zero-weight exclusion, all fifteen outcomes,
value 1, coexistence and same-type replacement, Harvest removal with unrelated implicit preservation, every rarity,
life/mana/non-flask/corrupted/mirrored/unknown refusal.

`FlaskEnchantGatewayTests` sends framed 0x71 and 0x72 messages through `Program.OnClientMessages`: all inventory/stash
and Shift combinations, one orb spent per success, coexistence after two different orbs, explicit-affix and quality
preservation, save reload, and refusal without spending.

Native acceptance: 2026-10-30, 3.29.3b client; Enkindling and Instilling both appeared on one flask, repeated use
replaced only its own type, magic explicit affixes stayed, and life/mana flasks were refused.
