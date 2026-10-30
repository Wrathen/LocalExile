# Status: Items, currency and crafting

Part of [Status](../Status.md); what to build next in this area is in
[roadmap/items-and-crafting.md](../roadmap/items-and-crafting.md).

## Works (verified with the real client)

- Ground items belong to the area instance: dropped items and loot stay where they lay across logouts, disconnects and
  client crashes until the instance expires; ids from 0x100000; one pickup per item.
  Code: `InstanceStore.cs`, Gateway.
  Docs: [13](../docs/protocol/13-items.md) §5.
- Life and mana flasks: a flask key (C→S 0x70) spends the flask's charges and recovers Flasks.dat's life or mana over
  its time in a 20 ms loop, the orb rising by +1, with the flask_effect buff.
  Quality, the flask's prefixes and suffixes and the character's flask stats count (amount, rate, instant and partial
  instant, low life, other-pool costs, mana at the end, not removed at full, extra life), as Path of Building does.
  Several recovery flasks queue, the highest rate first (poewiki).
  Checked live: the base recovery and the extra-life suffix; the other mods and queueing by tests.
  A successful use also sends S→C `0x263`; the native UI then looks the base type up and plays its use sound,
  `Audio/Sound Effects/ItemSounds/flaskdrink.ogg` (traced on the official 3.29.3b client and captured from it).
  Full pools refuse the flask before any charge or buff, with silent `0x263` result `0x4e2` and the authoritative pool;
  mana fullness uses the unreserved cap (full-pool refusals verified live locally).
  The client also pays the attack skill's mana for attack-moves the server only walks; the Gateway answers with the
  authoritative mana so the orb and the flasks agree (probe 2026-10-26).
  Code: `Flasks.cs`, `FlaskRecovery.cs`, Gateway (`UseFlask`, `RunResourceRecovery`).
  Docs: [13](../docs/protocol/13-items.md) §8.
- Flask charges: kills give 1 / 3.5 / 6 / 11 charges by rarity (Mods.dat monster rarity mods, poewiki), increased
  flask charges gained and charge recovery multiply, partial charges are kept.
  Charges on dealing a critical strike (one 0.132 s cooldown), when hit and every 3 seconds; charges used and maximum
  charges with mods.
  Ordinary-mode towns, hideouts and resurrection fill every flask; a flask put on the bar in a hostile area starts empty.
  `Game_FLASK_CHARGES=full` brings back "every kill fills every flask".
  Not yet: fewer charges in acts 6 to 10 and maps; charges on taking a critical strike (monsters deal none) and at
  maximum frenzy charges (none here).
  Checked live: kill and crit charges and the hostile-area rule; charges when hit and every 3 seconds by tests.
  Code: `FlaskCharges.cs`, Gateway (`Program.FlaskCharges.cs`).
  Docs: [13](../docs/protocol/13-items.md) §8b.
- Utility flasks: every utility flask spends its charges, shows its buff and the timer bar under it, and gives its
  buff's stats for its duration, all from the tables (Flasks.dat, BuffDefinitions, ComponentCharges).
  The stats count in the pools (the walk estimate, `/defences`, `/resistances`, `/hit`) and go to the client.
  Two flasks of one kind keep their own timers and only the strongest applies (poewiki); different effects stack.
  They end with the duration, at death, with the area and when the flask leaves its cell; a pause holds them.
  Quality, the flask's duration and effect mods, its "during effect" suffixes and the character's flask effect and
  duration count.
  Stibnite blinds monsters in its smoke cloud, Sulphur's consecrated ground regenerates life, Basalt taunts, Quartz
  phases through monsters, Gold raises the rarity of drops, Silver gives Onslaught.
  Checked live: every flask's use, stats on the client, bars, pause, death, area change; not the client walking faster
  or phasing (the control bridge cannot make it walk).
  Iron Flask now contributes to the authoritative Ward pool and restores broken Ward only after paid use.
  A native Antiquarian check spent 40 charges, restored 502 Ward immediately and returned to 22 after expiry.
  See [Ward and native evidence](../docs/game/antiquarian.md).
  Not yet: ailments, stun and curses on the player (Aquamarine, Corundum and the ailment
  suffixes reach the panel only), Aquamarine's chilled ground, the smoke cloud's art, leech suffixes.
  Code: `UtilityFlasks.cs`, `FlaskGrounds.cs`, Gateway (`UseUtilityFlask`, `RunUtilityFlasks`, `RunFlaskGrounds`).
  Docs: [13](../docs/protocol/13-items.md) §8a.
- Items: the start kit (Characters.dat skill and support gems, two life flasks on keys 1–2 and a mana flask on key 5)
  in the inventories, and the class weapon with two linked sockets worn in the main hand (`Game_START_WEAPON=ground`:
  on the ground a few cells from the spawn until it is picked up);
  pick up (0x28), put down, swap and equip (0x2a, slot classes and attribute requirements checked by the client),
  one-hander off hand → main hand, socket and unsocket gems (0x30, 0x2f), drop to the ground outside town (0x29 →
  WorldItem) and pick up again (to the cursor with the inventory open, at the client's 22-cell range), Destroy in town
  (0x16 /destroy); stacks: merge a held stack onto one of its type up to the CurrencyItems StackSize (0x4a), split
  with Shift + click (0x7c), Shift + click while holding places one, full fragment / shard stacks become their item,
  pick-ups, `/item` and sale rewards fill existing stacks first; the equipped weapon on the model;
  active gems in equipment grant their skill; class attributes and the class on the character panel;
  `/item <row, Id or name> [count]`.
  Code: `InstanceItems.cs`, Gateway (`ItemCatalog.cs`, `PlayerInventories.cs`).
  Docs: [13](../docs/protocol/13-items.md) §4a.
- Item mods: rarity (normal, magic, rare, unique), identified, and the implicit, explicit, enchant, scourge and hidden
  mod lists in the Mods component; new items roll their BaseItemTypes implicits (Driftwood Wand 8-12% increased Spell
  Damage); magic names come from the affixes; rarity and mods are saved with the item (characters and stash);
  `/mod`, `/mods`, `/rarity`, `/sockets` (colours and links, e.g. `/sockets RRGBBB`) on the held item;
  `Game_SOCKET_CAPACITY=1..6` (dev) lets `/sockets` give any socketable base that many, e.g. a 6-link wand (its own
  capacity is 3).
  Magic and rare items: affixes rolled from Mods.dat by domain, item level, the item's tags and families, rare names
  from Words.dat; equipment drops magic / rare and unidentified; a Scroll of Wisdom identifies (C→S 0x71);
  `/item <type> magic|rare`, `/roll`.
  Equipped items' mods count on the character: global stats like a passive's (life, resistances, attributes, spell
  damage …), the main hand's local crit, attack speed and accuracy, its local physical damage and quality and its local
  added elemental/chaos damage (`ItemWeapons`), and the armour pieces' and shield's local flat/increased armour,
  evasion and energy shield mods and quality (`ItemDefences`, [13](../docs/protocol/13-items.md) §2c).
  Not yet: uniques and special mods (#60).
  Code: `InstanceItems.cs`, Gateway (`ModCatalog.cs`, `ItemRolls.cs`, `ItemDrops.cs`, `ItemModStats.cs`,
  `ItemCatalog.cs`, `CharacterSaves.cs`).
  Docs: [13](../docs/protocol/13-items.md) §2a-§2c.
- Gold: shared by the account's characters of one league (S→C 0x2a9), saved with that league's stash, sent on area
  entry; every league has its own stash and gold, Standard keeps the account's stash from before (`AccountStash.Key`);
  monsters drop piles of gold (normal 40%, magic 75%, rare and unique always;
  (3 + 1.2 × level) × 1/2/5/15, our own numbers, `ItemDrops.Gold`), and picking one up adds it to the gold;
  gold within 15 cells of the player is picked up by itself (player tick, `GoldAutoLootRange`);
  older per-character gold moves into the account at login; `/gold <±amount>`.
  Code: `InstanceEntities.Gold`, `AccountStash`, Gateway.
  Docs: [04](../docs/protocol/04-player-entity.md) §6.
- Loot: dying monsters drop currency, gems, flasks and equipment (our own table) around the corpse.
  Code: `ItemDrops.cs`, Gateway (`DropLoot`).
  Docs: [15](../docs/protocol/15-progression.md) §3.

## Partly working

- **Unique items and Mageblood (verified live 2026-10-26).** The client data links a unique's name (`Words` wordlist 6)
  and artwork (`UniqueStashLayout` → `ItemVisualIdentity`), but not its mods, so `UniqueCatalog` curates each definition.
  `/item Mageblood` creates the Heavy Belt unique with its name, artwork and fixed mods (the Strength is the base
  implicit); the client shows the unique tooltip.
  While Mageblood is worn, the leftmost 2–4 magic utility flasks in the flask bar apply their effects permanently without
  charges or a timer, and using them is refused (client mod, server backstop); the stats refresh when the belt or the
  flask bar changes.
  Not yet: other uniques, unique drops/chancing, the permanent flask buff icons, the "effects cannot be removed"
  backstop, the Foulborn rightmost variant and Enkindling (`UniqueCatalog`, `Program.Mageblood`;
  [13](../docs/protocol/13-items.md) §2e, §8a).

- Ruthless uses native mod/flask alternatives, reduced tunable drops and vendor returns, and refuses bench crafting.
  Entering town or a hideout does not refill its pools/flasks; supported vendors refill them instead.
  Active utility flasks cannot gain charges, and native Ruthless durations/Onslaught values apply.
  Automated pool-restoration and session-ownership checks cover area changes, reconnects and vendor refill persistence.
  Drop, recipe, bench and flask-rule calculations have automated coverage; full retail balance remains partial.
  See [league modes](../docs/game/league-modes.md).
- Essence crafting (2026-10-25, automated verification; real-client use pending): all 105 craftable essences resolve
  their exact class-specific guaranteed mod from Essences.dat, including tier-8 and Desolation.
  Normal/rare eligibility, random-mod level caps, fractures, metamod refusal and repeated consumption from the Essence
  tab are covered.
  Uses the existing currency path for ordinary and Shift application.
  See [13](../docs/protocol/13-items.md).
- Quiver eligibility fix (2026-10-25, awaiting client confirmation): active bows now publish `main_hand_weapon_type =
  9`, required by the client's inventory gate; see [13](../docs/protocol/13-items.md).
- **Jeweller's Orb: verified live (2026-10-21).** Normal use and Shift share the currency handler; rerolls counts
  1/2/3/4/6 and link groups, limited by class, dimensions and item level, and consumes one orb.
  3.29: sockets the reroll keeps retain their colours and only the added sockets are white.
  Socketed gems, unsupported targets and maximum base capacity are refused without spending an orb.
  The live-found refusal of level-1 boots/gloves with two sockets is fixed and verified live.
  Count and link probabilities are approximate; special socket overrides are not modelled.
  See [13](../docs/protocol/13-items.md), "Jeweller's Orb".
- **Socket colours (3.29; verified live 2026-10-26).** Item sockets generate white and may roll a
  colour in proportion to the base's attribute requirements, with the chance rising with item level; a gem in a
  matching red/green/blue socket gets +10% quality (the stat path is in [25](../docs/protocol/25-gem-supports.md)).
  Chromatic Orbs force one socket non-white and roll the rest, with item quality raising their chance; the bench's
  "At Least Two/Three/Four Non-White Sockets" crafts (`NN`/`NNN`/`NNNN`) reroll until they guarantee that many; Vaal
  corruption colours one white socket. `/item`, drops and shops use the same generator, and the start weapon's two
  linked sockets are white.
  The probabilities are an approximation (5% non-white at item level 1, +0.25 points per level up to 30% at 100, and
  +0.25 points per 1% quality for Chromatic/bench); `SocketColours`, [13](../docs/protocol/13-items.md),
  "Socket colours (3.29)".
  Not modelled: Tainted Chromatic/Jeweller's Orbs, Orb of Binding, the Locus of Corruption, the 25-roll bench reward,
  special item overrides.
- **Orb of Alchemy:** a modifiable normal item becomes an identified rare with 4-6 weighted affixes (CoE's 2/3, 1/4,
  1/12; jewels 3-4 with two per side) and a rare name. Flasks are magic-only; unknown/unsupported/unmodifiable,
  non-normal, corrupted/mirrored and empty-pool targets are refused before spending.
  CoE count/jewel checks, ordinary/Shift inventory/stash dispatch and save reloads have automated coverage.
  The old live check (2026-10-21) predates these rules; native re-acceptance is pending.
  See [13](../docs/protocol/13-items.md), "Orb of Alchemy", and [evidence](../docs/re/alchemy-coe.md).
- **Chaos Orb:** a modifiable rare item's unprotected affixes are rerolled (CoE 4/5/6 counts, jewels 3-4 with two per
  side; new tiers, new rare name); prefix/suffix metamod locks keep their side. Flasks are magic-only; non-rare,
  unmodifiable/unsupported and empty-pool targets are refused before spending.
  CoE count/metamod checks, ordinary/Shift inventory/stash dispatch and save reloads have automated coverage.
  The old live check (2026-10-21) predates these rules; native re-acceptance is pending.
  See [13](../docs/protocol/13-items.md), "Chaos Orb", and [evidence](../docs/re/chaos-coe.md).
- **Divine Orb (verified live, 2026-10-21):** keeps the affixes, tiers and order and rerolls only values.
  Shift works. See [13](../docs/protocol/13-items.md), "Divine Orb".
- **Orb of Scouring (verified live, 2026-10-21):** a magic / rare / unique item becomes normal, only base mods stay.
  See [13](../docs/protocol/13-items.md), "Orb of Scouring".
- **Orb of Transmutation:** normal to identified magic, targeting 1-2 weighted affixes at equal probability.
  Unknown, unsupported, unmodifiable and empty-pool bases are refused before spending.
  CoE count/pool comparisons, ordinary/Shift inventory/stash dispatch and save reloads have automated coverage.
  Framed native wand/flask upgrades, repeat/unmodifiable refusal and relog passed on 2026-10-29.
  Fresh native Shift/stash and manual currency UI checks remain separate.
  See [13](../docs/protocol/13-items.md), "Orb of Transmutation", and [evidence](../docs/re/transmutation-coe.md).
- **Orb of Alteration:** a modifiable magic item gets 1-2 new weighted affixes (one prefix, one suffix at most),
  with the same admission as the other affix currencies: unknown/unsupported/`Unmodifiable` bases, corrupted/mirrored
  items and empty pools are refused before spending.
  CoE count/flask checks, ordinary/Shift inventory/stash dispatch and save reloads have automated coverage.
  The old live check (2026-10-21) predates these admission rules; native re-acceptance is pending.
  See [13](../docs/protocol/13-items.md), "Orb of Alteration", and [evidence](../docs/re/alteration-coe.md).
- **Orb of Augmentation:** +1 weighted affix on a magic item, up to one prefix and one suffix, with the same admission
  checks as Transmutation: a known, serializable, non-`Unmodifiable` base with Mods; corrupted/mirrored, full items
  and open sides without ordinary affixes are refused before spending.
  CoE beta pool and simulated-probability checks, ordinary/Shift inventory/stash dispatch and save reloads have
  automated coverage. The old live check (2026-10-21) predates these admission checks; native re-acceptance is pending.
  See [13](../docs/protocol/13-items.md), "Orb of Augmentation", and
  [evidence](../docs/re/augmentation-coe.md).
- **Regal Orb:** a modifiable magic item becomes rare, affixes kept, +1 weighted affix from the rare pool (max three
  of each side), rare name. Flasks are refused (magic-only), as are unmodifiable/unsupported bases and no-fit targets,
  without spending.
  CoE rare-pool/apply checks, ordinary/Shift inventory/stash dispatch and save reloads have automated coverage.
  The old live check (2026-10-21) predates these admission rules; native re-acceptance is pending.
  See [13](../docs/protocol/13-items.md), "Regal Orb", and [evidence](../docs/re/regal-coe.md).
- **Exalted Orb:** +1 weighted affix on a modifiable rare item, up to 6 (three per side; jewels two per side), name
  kept. Flasks, unmodifiable/unsupported bases, non-rare and full targets are refused before spending.
  CoE pool/jewel checks, ordinary/Shift inventory/stash dispatch and save reloads have automated coverage.
  The old live check (2026-10-21) predates these rules; native re-acceptance is pending.
  See [13](../docs/protocol/13-items.md), "Exalted Orb", and [evidence](../docs/re/exalted-coe.md).
- **Orb of Annulment:** removes one random unprotected affix from a magic or rare item; rarity, name, implicits and
  the other affixes stay. Metamod/fractured filters exclude protected affixes; corrupted/mirrored/unsupported/
  `Unmodifiable` and no-eligible-explicit targets are refused before spending.
  CoE rarity/metamod checks, ordinary/Shift inventory/stash dispatch and save reloads have automated coverage.
  The old live check (2026-10-21) predates these rules; native re-acceptance is pending.
  See [13](../docs/protocol/13-items.md), "Orb of Annulment", and [evidence](../docs/re/annulment-coe.md).
- **Armourer's Scrap (verified live, 2026-10-21):** +1% quality on armour, up to 20%; Scouring resets it to 0.
  See [13](../docs/protocol/13-items.md), "Armourer's Scrap".
- **Blacksmith's Whetstone (verified live, 2026-10-21):** +1% quality on weapons, up to 20%; Scouring resets it.
  See [13](../docs/protocol/13-items.md), "Blacksmith's Whetstone".
- **Orb of Chance:** the documented local rule: a normal weapon / armour piece → rare (30%, 4-6 affixes) or magic
  (2-4 affixes, at most two per side), identified. Corrupted/mirrored/unsupported/`Unmodifiable` and empty-pool
  targets are refused before spending.
  CoE beta has no Chance handler, so official unique/rarity outcomes remain separate work.
  Ordinary/Shift inventory/stash dispatch and save reloads have automated coverage; native re-acceptance is pending.
  See [13](../docs/protocol/13-items.md), "Orb of Chance", and [admission](../docs/re/chance-coe.md).
- **Vaal Orb and corruption:** the local rule corrupts a modifiable weapon, armour piece or jewellery (Base +0xc7
  bit 0, saved); normal / magic → rare with 4-6 affixes, a rare item gets 1-5 or all affixes rerolled, one white socket
  is coloured. Corrupted/mirrored/unique, unsupported/`Unmodifiable` and empty-pool targets are refused before
  spending; a corrupted item takes no currency but Wisdom.
  CoE's official Vaal outcome table remains separate work; the admission and boundary are documented.
  Ordinary/Shift inventory/stash dispatch and save reloads have automated coverage; native re-acceptance is pending.
  See [13](../docs/protocol/13-items.md), "Vaal Orb and corruption", and [admission](../docs/re/vaal-coe.md).
- **Eldritch Ichors (Lesser, Greater, Grand, Exceptional):** add the tier's Eater of Worlds implicit to body armour,
  boots, gloves or helmet, replacing every other implicit except Searing Exarch ones; normal/magic/rare only.
  Unique/corrupted/mirrored, non-armour and unsupported/unmodifiable targets are refused before spending.
  CoE base-key/tier/presence checks, all four tier suffixes and ordinary/Shift inventory/stash dispatch have automated
  coverage. A separate Eldritch dominance state and the Ember/Conflict family remain separate, and native tooltip
  acceptance is pending.
  See [13](../docs/protocol/13-items.md), "Eldritch Ichors", and [evidence](../docs/re/eldritch-ichor-coe.md).
- **Eldritch Embers (Lesser, Greater, Grand, Exceptional):** the Searing Exarch mirror of the Ichors; add the tier's
  Exarch implicit to body armour, boots, gloves or helmet, keep Eater implicits and remove the other implicits;
  normal/magic/rare only. Unique/corrupted/mirrored, non-armour and unsupported/unmodifiable targets are refused.
  CoE base-key/tier/presence checks, all four tier suffixes and ordinary/Shift inventory/stash dispatch have automated
  coverage. A separate Eldritch dominance state and the Orb of Conflict remain separate, and native tooltip
  acceptance is pending.
  See [13](../docs/protocol/13-items.md), "Eldritch Embers", and [evidence](../docs/re/eldritch-ember-coe.md).
- **Enkindling Orb:** a utility flask of any rarity gets one of the five GenerationType 21 enchantments (increased
  effect / duration / Charge Recovery / maximum Charges, or reduced Charges per use, all with `Gains no Charges during
  Effect`) with the game's 250/100/100/100/100 weights. Life/mana, corrupted/mirrored, non-flask and
  unsupported/unmodifiable targets are refused before spending. An existing Enkindling is replaced; an existing
  Instilling and the flask's explicit affixes, quality, charges, rarity and name stay. Harvest/bench flask
  enchantments (GenerationType 3, family `FlaskEnchantment`) are removed. CoE pool/weights and native coexistence
  checks have automated coverage; see [13](../docs/protocol/13-items.md), "Enkindling Orb and Instilling Orb", and
  [evidence](../docs/re/enkindling-coe.md).
- **Instilling Orb:** the Searing Exarch counterpart: a utility flask gets one of the fifteen spawnable GenerationType
  22 trigger enchantments (charges full, frozen/chilled/shocked/ignited/bleeding/poisoned, block, savage hit, guard or
  travel skill, rare/unique hit, adjacent flask, effect end, life flask), equal weight. The sixteenth row
  (`Used when you lose a Guard Skill Buff`) has spawn weight 0 in 3.29.3b and is excluded, as are the bench recipes.
  An existing Instilling is replaced; an existing Enkindling and the rest of the item stay. See
  [13](../docs/protocol/13-items.md), "Enkindling Orb and Instilling Orb", and
  [evidence](../docs/re/instilling-coe.md).
- **Visual client acceptance (2026-10-30):** the main successful uses of all implemented currencies were checked in
  the 3.29.3b client: Transmutation, Augmentation, Regal, Alteration, Alchemy, Exalted, Chaos, Vaal, Chance,
  Annulment, all four Eldritch Ichors and all four Eldritch Embers, Enkindling and Instilling. Vaal turned normal
  items rare (yellow) and the tooltip showed `Corrupted`; the Eater/Exarch implicit names, values and tiers were
  visible. Enkindling and Instilling both appeared on one flask, repeated use replaced only its own type, and magic
  explicit affixes stayed. No visual defects were found. Vaal on jewels remains outside the local rule; official
  jewel/Vaal outcomes are separate work. Stash/Shift and the remaining refusal paths stay under automated coverage.
- **Glassblower's Bauble (verified live, 2026-10-21):** +1% flask quality up to 20%; Scouring resets it.
  See [13](../docs/protocol/13-items.md), "Glassblower's Bauble".
- **Fracturing Orb (verified live, 2026-10-21):** one random mod of a rare item with 4+ mods is fractured and kept by
  every other currency, Scouring included; shown fractured (magic properties u8 +0x7a).
  See [13](../docs/protocol/13-items.md), "Fracturing Orb".
- **Mirror of Kalandra (verified live, 2026-10-21):** a mirrored copy (Mods flag bit 1) of a non-corrupted,
  non-mirrored item goes onto the cursor; mirrored items take no currency.
  See [13](../docs/protocol/13-items.md), "Mirror of Kalandra".
- **Orb of Fusing: verified live (2026-10-21).** Captured 0x71/0x72 packets feed the same handler.
  Only links change; sockets, colours, gems and item properties stay intact.
  Refuses fewer than two sockets and protects fully linked 5/6-socket items; 2-4 sockets remain rerollable.
  Quality favours longer groups without being consumed.
  Link/quality probabilities are approximate, and special overrides/corruption are not modelled.
  See [13](../docs/protocol/13-items.md), "Orb of Fusing".
- Currency metamods: Scouring/Chaos/Divine/Annul respect prefix/suffix locks;
  annul and affix additions respect attack/caster restrictions.
  Scouring retains quality and refuses uniques.
  Real-data regression tests cover both lock directions.

## Not working yet

- Most uniques and special mods (#60), and unique drops/chancing; Mageblood and normal Crown of Eyes have definitions;
  currency other than Scroll of Wisdom, Chromatic Orb, Jeweller's Orb, Orb of Fusing, Orb of Alchemy, Chaos Orb,
  Divine Orb, Orb of Scouring, Orb of Transmutation, Orb of Alteration, Orb of Augmentation, Regal Orb, Exalted Orb,
  Orb of Annulment, Armourer's Scrap, Blacksmith's Whetstone, Orb of Chance, Vaal Orb, the four Eldritch Ichors and
  Embers, Enkindling Orb, Instilling Orb, Glassblower's Bauble, Fracturing Orb, Mirror of Kalandra, Gemcutter's Prism
  and Orb of Regret;
  support gems and links ([13](../docs/protocol/13-items.md) "Open").
