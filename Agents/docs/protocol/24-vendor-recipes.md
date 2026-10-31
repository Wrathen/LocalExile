# Vendor sales and recipes

## Quote and settlement

`VendorRecipes` evaluates only items placed in Your Offer. Each ingredient has a remaining count; each match
consumes its own ingredients once. Matched recipe currency left over in a stack is returned. No inventory/stash
currency outside the offer is charged. Shards and scroll fragments are normalized using CurrencyItems' full-stack
conversion rows, and rewards are split at the native stack limit.

`SendSaleReward` stores the ingredient snapshot and the fully rolled output items on NpcSale. The client sees this
quote; acceptance verifies the snapshot and pays the same outputs without rerolling. Generated magic/rare outputs
hide their modifiers in the preview. An oversized reward refuses the sale rather than truncating it.
`PlayerInventories.Sell(IReadOnlyList<Item>)` stages every reward on a copy with offered cells freed, commits only
if all rewards fit, and persists once. Multiple changes to one stack produce one final inventory update. Failed
persistence invalidates the in-memory inventory; the saved ingredients remain intact. Cancel/disconnect retains
all original items. Socketed equipment cannot enter the sale until its gems have been removed.

## Implemented common recipes

- Rare equipment sets: Chance below ilvl 60, Chaos at 60-74, Regal at 75+, based on the lowest ingredient ilvl.
  Unidentified and quality bonuses are independent (+1 each). Jewellery needs no catalyst quality for this set.
  One two-handed weapon/bow, or two one-handed weapons / one-handed weapon plus shield. Quivers do not count.
  Multiple disjoint sets and unrelated sale items can share an offer.
- Noncorrupted gem plus Scouring: exactly one gem level down, resetting experience to the target level threshold
  and preserving type, variant and quality. Regret resets to level one. Vaal variants are excluded even if a malformed
  saved item lacks its corrupted flag. Level-20 support plus GCP becomes level one with 20 quality; active and awakened
  gems do not qualify. Blink/Mirror Arrow swaps preserve matching variants, experience and quality.
- Six linked sockets: 20 Fusing; six sockets: 7 Jeweller's; one linked RGB group: Chromatic. Separate RGB sockets
  do not qualify. Six-link/socket salvage takes precedence over consuming that item in a full rare set.
- Normal 20-quality weapons, armour, flasks and gems; disjoint 40-quality groups of any rarity. Each group must
  individually reach 40, so a bare sum of 80 does not necessarily pay twice. Chisel recipes remain disabled.
- Identical rare-name pairs/triples, their quality bonuses; same-base normal/magic/rare(/unique) sets; five matching
  magic/rare bases; three identical uncorrupted unique variants with matching explicit modifier identities.
- Three-to-one essence tiers, oils and table-defined map upgrades; five-to-one Kalguuran rune upgrades. No upgrade
  when the data has no next tier. Unspent units of a successfully matched currency upgrade are returned.
- Currency downgrades for Portal/Transmutation/Alteration/armour/weapon quality currency; Fusing + Chromatic;
  Mirror -> 2 Exalted + 5 Regal + 1 Divine. These are sell recipes, not the direct-purchase currency exchange chain.
- Vaal Orb from seven Vaal gems and a Sacrifice fragment; Mortal fragments do not qualify.
- Iron Ring plus coloured gem, three distinct Two-Stone Rings, Onyx Amulet, hybrid attribute amulets.
- Movement-speed boots, physical/spell damage belt recipes, flat attack damage flask upgrades,
  +1 elemental/physical/chaos spell gem weapons from 40 tagged gem quality, chaos-resistance jewellery upgrades.
  Crafting excludes corrupted, mirrored and fractured bases; ordinary socket/link layouts are retained.
- Three flask bases -> next flask base and life + mana + Fusing -> hybrid flask.
- Ordinary single-item fallback prices: unidentified magic/rare Transmutation shards, identified affix prices,
  six-explicit rare Augmentation and two-explicit rare Scouring, then Scroll Fragment.

## Data and scope

The ModType.ModSellPriceTypesKeys join associates modifiers with native sale-price classes. Common published
family prices, maximum-roll bonuses, fractured quadrupling and sell-price doubling are modeled. This is not a
claim of audited parity for every possible affix: exact pricing of all legacy/unique/league modifiers still needs
reference verification. Native special price classes (Kingmaker, fossil extra sale rewards, Crucible and similar)
are rejected explicitly until implemented; never silently downgraded to a fragment price.

Items carry influences ([13](13-items.md) §2f), but no recipe reads them yet. Influenced sets,
named unique combinations (Loreweave, Kingmaker, The Taming, etc.), catalyst quality recipes, Breach blessing
conversions, random catalyst/scarab exchanges, bandit reform books and other special league recipes are **not
implemented**. The map upgrade uses Maps.dat's target, not the full official atlas-combination selection algorithm.
A 40-quality subset uses least-excess matching; global optimal partitioning for arbitrary large mixed offers is not
claimed. Further implementation must keep these limitations explicit instead of calling the system complete.

## Sources and version checks

The user-supplied [Russian community wiki](https://pathofexile.fandom.com/ru/wiki/Рецепты_торговцев) is not an
official GGG specification and contains obsolete recipes. The recipe catalog was cross-checked against
[PoEDB](https://poedb.tw/us/Vendor_recipe_system), local 3.29.3b tables and GGG patch notes:

- [3.19](https://www.pathofexile.com/forum/view-thread/3293287): six-link sale gives 20 Fusing, not Divine.
- [3.23](https://www.pathofexile.com/forum/view-thread/3451455): 20/20 reset is only for non-awakened supports.
- Chisel, flat spell-damage ring recipes and Book of Regression are not revived from old wiki entries.

Automated coverage exercises threshold boundaries, set bonuses and hand combinations, recipe precedence,
gem restrictions/experience, linked sockets, quality partitioning, leftover stacks, affix prices and fracture
multipliers, multiple sets, duplicate-ID rejection, full-inventory refusal and transactional save failure.
Real-client quote/acceptance verification is pending.
