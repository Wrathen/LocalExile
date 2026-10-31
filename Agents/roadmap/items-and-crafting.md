# Roadmap: Items, currency and crafting

Part of the [Roadmap](../Roadmap.md); what already works in this area is in
[status/items-and-crafting.md](../status/items-and-crafting.md).
Items keep the Roadmap's priority groups, and are in rough priority order within each group.

## 1. Quality of life

Essence item crafting is implemented (105 currencies, class-specific guaranteed modifiers;
see Status and protocol/13-items.md).
Imprisoned essence encounters, Remnant of Corruption on monsters and stash-tab essence upgrading remain separate
features.

## 3. Gameplay

### Items: mods, rarity, using flasks and currency

- **Jeweller's Orb:** initial local 0x71/Shift 0x72 implementation rerolls counts, colours and links with class and
  item-level caps; the live-found refusal at the item-level cap is fixed (only base capacity blocks use), verified
  live.
  See 13-items.md, "Jeweller's Orb" for the requested local rules and remaining exact-game behaviour.
- **Orb of Fusing:** normal/Shift handling and link-only rerolls are implemented and verified live.
  Exact link probabilities, quality scaling and special item overrides remain open (13-items.md, "Orb of Fusing").
- **Known:** items serialize, move, socket and drop ([13](../docs/protocol/13-items.md));
  rarity, implicits and every mod list are sent and saved (13 §2a);
  magic and rare items roll, drop unidentified and a Scroll of Wisdom identifies them (13 §2b);
  equipped items' mods count on the character (13 §2c).
  Life/mana flask charges, recovery, native drinking sound and full-pool refusal are implemented (13 §8).
  Open issues: enchants / crafted / corrupted / influences / uniques (#60), area and monster mods (#61).
  The Mud Flats glyphs are quest items (`Metadata/Items/QuestItems/Glyphs/Glyph1..3`,
  [12](../docs/protocol/12-quests.md)).
- **Start:** other currency through C→S 0x71 (13 §4: Alteration, Augmentation, Regal, Alchemy,
  Chaos, Scouring can reuse `ItemRolls`); the other uniques and special mods (#60, 13 §2e: `UniqueCatalog` defines
  Mageblood; each remaining unique needs its base type, mod rows and ranges curated), unique drops/chancing, and
  Mageblood's follow-ups (buff icons, the "effects cannot be removed" backstop, the Foulborn rightmost variant,
  Enkindling).
  Transmutation's CoE comparison and admission checks are implemented; recheck Shift/stash and manual UI use natively.
  See [the evidence limits](../docs/re/transmutation-coe.md).
  Augmentation uses the same validated entry point and CoE-checked weighted pools; recheck its admission and
  Shift/stash paths natively. See [the evidence](../docs/re/augmentation-coe.md).
  Regal uses the same entry point and opens the rare pool after the upgrade; flasks are refused. Recheck it natively.
  See [the evidence](../docs/re/regal-coe.md).
  Alteration rerolls 1-2 affixes through the same entry point and refuses empty pools; CoE's 300-apply count and
  flask checks agree. Recheck it natively. See [the evidence](../docs/re/alteration-coe.md).
  Alchemy uses the same entry point and CoE's 4/5/6 and jewel 3/4 rare counts; flasks are refused. Recheck it
  natively. See [the evidence](../docs/re/alchemy-coe.md).
  Exalted adds one rare affix within the base's cap (jewels 2 per side), keeps the name and refuses flasks. Recheck
  it natively. See [the evidence](../docs/re/exalted-coe.md).
  Chaos rerolls the same rare pool, keeps metamod locks and refuses flasks/empty pools. Recheck it natively. See
  [the evidence](../docs/re/chaos-coe.md).
  Vaal's local outcome rule has validated admission (corrupted/mirrored/unique/unsupported/empty refused); official
  CoE Vaal outcomes remain separate. See [the boundary](../docs/re/vaal-coe.md).
  Chance's local 30/70 rule has validated admission; CoE beta has no Chance handler, so official unique/rarity
  outcomes remain separate. See [the boundary](../docs/re/chance-coe.md).
  Annulment's removal rule has validated admission and CoE metamod/rarity checks; recheck it natively. See
  [the evidence](../docs/re/annulment-coe.md).
  The four Eldritch Ichors and Embers add tiered Eater/Exarch implicits and have CoE base/tier checks. The Orb of
  Conflict and a separate dominance/influence state remain separate. See [the Ichor evidence](../docs/re/eldritch-ichor-coe.md)
  and [the Ember evidence](../docs/re/eldritch-ember-coe.md).
  Saved variable defence bases, local mods and quality now feed the pools.
  Sacred Orb rerolls, local zero-ES rules, equipment ES-to-Life conversion and corrupted-item global ES increases
  are implemented with automated coverage and historical native controls (13 §2c, §4; Status).
  Remaining: fresh real-client checks of the merged branch, Ward calculations and unsupported unique/conditional
  defence mechanics.
  Shield-specific increases and exclusions now feed ordinary defence pools with native final half-up rounding.
  Remaining special mechanics include Ward conversions, ring-slot and jewel-radius operators.
  Sacred Orb rerolls use the ordinary/Shift currency path and atomic inventory/stash saves.
  See Status and 13-items.md, "Sacred Orb" for the native checks and local roll distribution policy.
  Fracturing and Mirror have validated class/rarity admission (Fracturing uses `ItemClasses.CanBeFractured`, Mirror
  the equipment flag, non-unique targets and no currency on mirrored items); the 2026-10-30 native run covered the
  success and refusal paths. Influence/synthesis/veiled modifiers and Reflective Oil/Split/Sanctified remain separate,
  and Mirror from a stash tab is still unsupported. See
  [the game rules](../docs/re/fracturing-mirror-mechanics.md).
  Hinekora's Lock's server rules are in (Mods flag bit 8 + seed, C→S 0x29c / S→C 0x29f preview with status 0,
  exact committed result, clear-on-modification); the client's preview branch is the item popup's foresight element.
  See [the evidence](../docs/re/hinekora-lock.md).
- **Socket colours (3.29):** the 3.29 rules are in (white by default with an item-level colour chance, the
  Chromatic and non-white bench crafts, white sockets from Jeweller's/bench socket adds, Vaal colouring one white
  socket; [13](../docs/protocol/13-items.md), "Socket colours (3.29)", Status).
  Remaining: Tainted Chromatic/Jeweller's Orbs, the Orb of Binding, the Locus of Corruption, the milestone bench
  reward (25 reforges keeping the most non-white sockets), special item overrides, and fitting the exact colour
  probabilities from live captures (the current curve is an approximation).
- **Done when:** items' damage and defences count, and flasks and currency can be used.
- **Flask modifiers (#75):** character charge-gain source resolution and independent hybrid recovery components
  have ordinary-mode handler/lifecycle coverage here. The counts and Standard/Ruthless observations in this entry
  describe historical LocalExile validation, not a new native run on this branch; see the
  [port record](../docs/dev/issue75-port.md). Twenty-four earlier native controls verify actual buffs,
  recovery and charge awards, including Ruthless utility prevention and ordinary vendor refill.
  Flat and per-player-level duration now share one raw resolver for the panel and actual uses; level and inputs are
  captured before the charge send. Utility local/global duration factors multiply rather than add, with quality and
  local more kept separate. Runtime global effect now shares one raw flat-plus-actual-level resolver for recovery
  and utility, with local/applicable magic effect additive and raw publication unchanged. Its 39 new pure/handler
  cases pass in the combined 53 focused cases and full Gateway 3710/3710; prior unchanged Core 648/Data 37 remain
  applicable (4395 total, not all newly rerun). Five actual disposed-stream task faults remain recorded without
  new-calculation references; native verification is outstanding. Per-use snapshots, instant/low-life/rate order,
  flags and independent Onslaught remain separate. Wide projection guards do not cover every downstream aggregate.
  The preceding panel/duration batch passed 55 new plus 80 existing focused cases,
  full Gateway 3657/3657 and applicable prior Core 648/Data 37 checks. Of 40 completed native controls, 22 complete
  controls pass (12 positive source, six utility and four Life); 18 complete zero controls remain UNPROVED, with
  138 VERIFIED/42 UNPROVED panel-field verdicts. Utility durations match Standard13.8/3/3.9 and Ruthless4.6/2/2.6
  seconds for level-10 white, level-100 white and level-100 local uses. Life observed Standard160/34 and Ruthless161/34
  against fixed161/35 at levels10/100 under the original one-integer boundary. Clean recovery3 cleanup,769 held
  hashes and preserved saves/canonical inventories do not turn the unproved zeros into passes.
  These historical native results do not validate the new effect resolver. Conditional gain/effect operators,
  whole-unique native acquisition/tooltip behavior, nonpositive-duration policy, official fractional rounding and
  queued native timer/predictor presentation remain open. See protocol 13 §8 and the items status.

## 4. Server design

- **Editable game data and limits:** the SQLite overlay, snapshot/reload foundation, mod roll ranges and item limits are
  implemented and live-checked. The unique-item, radius/threshold/unique/timeless jewel, tattoo-effect and jewel-art
  catalogues are now in `catalog_entries` (including the full 1,302-row unique catalogue), seeded from the current code
  arrays and swapped on `/game_data reload`. The `stats`/`mods`/`mod_stats` reference tables and
  `effective_mod_stats` view give an editor the default keys and ranges without opening the JSON. `system_settings` now
  holds socket caps by item class, passive points per level, drop/pack/experience/max-level/gold/default-limit/grace
  numbers. The next step is item templates with custom names, then the admin interface, then NPC shops/placements and
  the remaining drop/balance settings.
  The client-compatibility boundary, schema and cache plan are in
  [dev/editable-game-data.md](../docs/dev/editable-game-data.md).
  The extracted JSON stays the reference; the database only overrides it.

## 5. Project

- Completed currency metacraft protection for supported explicit-affix currencies;
  specialized unsupported currencies and official Chance/Vaal outcome distributions remain separate work.
