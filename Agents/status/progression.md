# Status: Experience and the passive tree

Part of [Status](../Status.md); what to build next in this area is in
[roadmap/progression.md](../roadmap/progression.md).

## Works (verified with the real client)

- Experience and levels: kills within 150 cells give DefaultMonsterStats experience × the variety's multiplier ×
  rarity, with the level penalty (S→C 0x2ba Player experience; the client derives the level);
  a new level sends its stats, full life and mana, the passive points and the account's level;
  experience is saved per character; `/add_xp`.
  Code: `Progression.cs`, Gateway (`GainExperience`, `ApplyLevel`).
  Docs: [15](../docs/protocol/15-progression.md) §1.
- Passive Skill Tree: S→C 0x33 (level, class, allocated nodes, passive and refund points;
  the client counts level − 1 points), C→S 0x34 allocates a node, checked, applied to the stats (attributes, life,
  mana, resistances and more) and saved; C→S 0x36 with flag 0 refunds a node for a saved refund point (checked,
  mastery effects removed); using an Orb of Regret (right-click, C→S 0x58) takes one item and adds one point;
  a Book of Skill (right-click, C→S 0x58; every quest book of that name) is used up and gives one more passive skill
  point, both saved together; a Book of Regrets gives two refund points the same way;
  `/passives`, `/add_refund_points`, `/reset_passives`.
  Code: `PassiveTree.cs`, `CharacterSaves.cs`, Gateway (`ChangePassives`, `UseItem`).
  Docs: [15](../docs/protocol/15-progression.md) §2.
- Passive masteries: S→C 0x33 carries selected effects (`node | effect << 16`) and C→S 0x35 selects the effect HASH16;
  a mastery node can be allocated normally and its effect selected/changed without spending another point.
  Block Mastery's four effects are implemented: max attack block +2, max spell block +2, 20 life + 20 mana on block,
  and +1% spell block per 5% attack block; the effects are saved with the character.
  Code: `PassiveTree.cs`, `CharacterSaves.cs`, Gateway (`ChangePassives`, `PassivesMessage`).
  Docs: [15](../docs/protocol/15-progression.md) §2.
- Passive tree jewel sockets (server and live stat check 2026-10-27; tree art check pending): the 21 ordinary large
  sockets are the `PassiveJewels1` pane (Inventories.dat row 23, inventory id 24, 60x1). Pane cell = the
  `PassiveJewelSlots.dat` row and its `Slot` = the `PassiveSkills.dat` row. Ordinary/magic/rare jewels can be placed,
  swapped and picked up (0x2a/0x4a/0x28), are saved with the inventories, and their global mods apply like worn items;
  the socket must be allocated, the item a non-cluster jewel and its level requirement met. Refunding a socket that
  holds a jewel is refused until it is taken out; a level trim returns the jewel to the inventory.
  Unique jewels use the same path but have no unique item catalogue/source yet.
  The Adorned is the pilot unique jewel: `/adorned [0-1000]` creates the corrupted unique Crimson Jewel, and its effect
  scales corrupted magic jewels in the ordinary passive sockets (live: 225% -> 240% -> 255% fire resistance with a
  +15% jewel and The Adorned at 100%). Cluster sockets and the pre-3.25 50-150% variant are not covered.
  Percentage attribute mods (`strength_+%`, `dexterity_+%`, `intelligence_+%`, `all_attributes_+%`) now scale the final
  class and item attributes, so Brawn and the Emperor's jewels reach the pools and the character panel.
  Readiness, counted per stat: 46 of the 97 stats of the 43 Global-set jewels (42 Global + The Adorned) are in game
  and 11 more are panel only, so the set is 47% done by stats (59% counting the panel rows); the whole 185-jewel
  catalogue is 10%. The per-jewel table is in [unique-jewels](../docs/game/unique-jewels.md).
  Code: `PassiveTree.JewelNodeForSlot`, Gateway (`PlaceItem`, `HeldOntoItem`, `PickUpItem`, `CharacterStatSources`).
  Docs: [15](../docs/protocol/15-progression.md) §2a, [unique-jewels](../docs/game/unique-jewels.md).
- Cluster jewels (server and live client check 2026-10-27): `/cluster_jewel <small|medium|large> [passives] [sockets]
  [theme] [notables…]` and `/voices [1|3|5|7]` make real items, and nesting follows the size rule (a tree socket takes any
  size, a large jewel's sockets medium/small, a medium one's socket small). Their passives are the client's own
  expansion ids (block base fixed per pane cell, e.g. 195..206 for cell 20); the server keeps them in the third list of
  0x33, counts a point each, saves them with the character and restores them on login up to the socketed jewels' node
  budget (the `Adds N Passive Skills` count per jewel, which counts the sockets; Voices' empty passives are plus its
  sockets). `ClusterJewelLayout` reproduces the client's measured layout: the block base is `39 * ClusterIndex` for a tree socket,
  `9 * the child's ClusterIndex + 12` for a socket of one and `6` deeper, the position is `(StartIndices[size] +
  templateIndex) % TotalIndices`, a tree-socket large jewel keeps the full ring (a small passive gets the theme, a notable
  its own row, a socket nothing) and a jewel in an expansion cell is compressed onto half the ring (a measured 4-passive
  medium: smalls 208, 209, 211 and socket 212 in cell 51, 225, 228, 229 and 226 in cell 53). 20 of the 42 theme stats are
  translated; expansion-cell shapes without a measured layout (notables) still use the count-based estimate. A small
  cluster jewel also fits medium and large sockets now (the client pads the cell); a blue/yellow small (with a notable)
  gets its measured graph (base 213 in cell 54: notable 213, smalls 214/215; in the large tree socket 17 the small's
  157/161 are theme smalls and 159 its first notable, the other notable mods inert) and gives the theme per small plus
  the first notable's row, while a Normal theme-only small drew no client graph in our test. Live full-tree check: a
  large in tree socket 11 (base 0), a medium in its cell 21 (base 12) and a blue small in cell 24 (base 18); the small's
  18/19 added the theme and 20 was Fettle. The chat's per-node debug line is gone; the Gateway still logs the ids and the
  `passive jewel stats` line. The jewel's rarity follows its notables (none Normal, one or
  two Magic, three Rare): a Magic cluster jewel with no affix makes the client draw the whole cluster as empty
  "Nothingness" nodes.
  Still missing: keystones, radius rules and the vendor recipe. A cluster jewel fits only a socket of its own size: the client builds the layout from the cell, so a
  smaller jewel is padded with empty "Nothingness" nodes that grant nothing.
  An end-to-end client check (`scripts/dev/jewel_selftest.py`) covers insert/remove with no node taken, the small and
  notable stats of a large cluster, a medium cluster in a large one's socket, and a normal jewel with +80 life in a
  cluster socket.
  Code: `Program.Items.cs` (`ClusterJewelSocketed`, `ClusterNodeBudget`, `CreateClusterJewel`, `CreateVoices`),
  `ClusterJewelStats.cs`, Gateway (`ChangeClusterPassive`). Docs: [cluster-jewels](../docs/game/cluster-jewels.md).

- Timeless jewels (Legion; server and live client check 2026-10-29): `/timeless <name> [displayed seed] [conqueror]`
  creates the six unique jewels with the client's `UniqueJewelAlternateTreeInRadius*` mod. The client itself draws the
  conquered radius from those stats; the server reproduces the same alternate tree with the TinyMT32 generator, the
  `AlternateTreeVersions` flags, `AlternatePassiveSkills` and `AlternatePassiveAdditions`, and applies replacement or
  added stats to allocated ordinary nodes in the fixed 0..1500 radius. The RNG and generated values match Path of
  Building's LUTs (Vaal small 62429/seed 2000: Aura Effect 3; notable 37326: Cult of Fire, 30 fire resistance and 1
  maximum) and the live client log confirms the alternate tree version, seed and conqueror. `Elegant Hubris` stores its
  displayed seed divided by 20; only current conquerors are offered; only one Historic timeless jewel may be socketed;
  cluster sockets are refused. Militant Faith also carries two distinct current per-10-Devotion mods (the pre-3.29.0
  reduced Mana Cost row is legacy and not rolled); `DevotionStats` scales them by `floor(Devotion / 10)` into the normal
  stats, so resistance/damage/aura/regen/ailment targets are applied while curse duration, minion/totem/brand/channelled,
  mana cost and shield defences remain unread. The generated list covers the 96 timeless additions, including the four
  rows missing from Path of Building's reference data.
  Keystone stat rows are applied, but the conqueror keystones' special gameplay mechanics (Divine Flesh, Chainbreaker and
  the rest) are still missing.
  Code: `TimelessJewelCatalog.cs`, `TimelessJewelEngine.cs`, `PassiveTree` geometry/radius, `Program.Progression.cs`
  (`EquipmentStatSources`), `Program.Items.cs` (`PassiveJewelRefusal`). Docs:
  [timeless-jewels](../docs/game/timeless-jewels.md).

- Radius unique jewels (pilot 2026-10-29): all **78 rows / 76 distinct names** with a `Radius:` line are classified in
  [radius-jewels](../docs/game/radius-jewels.md) and the generated
  [radius-jewel-list](../docs/game/radius-jewel-list.md): 6 attribute conversions, 6 damage/defence/weapon
  conversions, 3 notable replacements, 10 passive additions, 33 threshold/skill jewels, 11 per-node scaling, 3
  allocation/tree rules, 1 passive-effect/notable-suppression, 5 special/global-only. **The 42 implemented jewels
  (39 pilot rows plus Might in All Forms, Pitch Darkness and Sudden Ignition):** `/radius_jewel <name> [variant]`
  creates the six attribute conversions, five damage/defence conversions, three notable replacements, ten passive
  additions, eleven per-node scaling jewels
  (Anatomical Knowledge, Eldritch Knowledge, Pugilist, Spire of Stone, Static Electricity, the Tempered/Transcendent
  trios), Might of the Meek and the three allocation/tree-rule jewels (Intuitive Leap, Thread of Hope's five ring
  variants, Unnatural Instinct).
  `EquipmentStatSources` moves/transforms the matching stats, replaces allocated notables, appends the passive
  additions (including the resistance-scaled Breach jewels), selects the Light/Perandus variant, computes the per-node
  scaling from allocated and unallocated attributes, applies Might of the Meek's 50% non-keystone effect with notables
  suppressed, and swaps Unnatural Instinct's allocated and unallocated small passives. `OrdinaryPassiveRules` treats
  Intuitive Leap/Thread of Hope bypass nodes as extra roots; `PassiveTree.NodesInRing` selects the Thread of Hope ring,
  and taking either jewel out is refused while a bypass-allocated node remains. Live: Brute Force in socket 1 changed
  Strength 559 -> 549 and Intelligence 647 -> 681; Energy From Within in socket 16 changed life 5857 -> 5311 and ES
  243 -> 273; The Light of Meaning (Life) in socket 12 changed life 5311 -> 5355; in the full demo layout (Brute Force
  socket 1, Anatomical socket 11, Fevered Mind socket 13, Light socket 12, Energy From Within socket 16) Anatomical is
  worth +165 (life 5520 with it, 5355 without), and the same layout reads mana 2345 and ES 276 (mana 2355/ES 277 after
  the later relog). Socket 11 has 20 Int, so `floor(20 / 3) = 6` flat life is part of that total; socket 13 has no
  Intelligence and in the earlier isolated check only the global life% applied (5355 -> 5502). Might of the Meek in cell
  3 changed life 5520 -> 5529 and mana 2345 -> 2375 from the 50% effect on the Intelligence/Strength smalls; uncapped
  attack/spell block moved 168%/27% -> 162%/20% as the Dark Arts notable was suppressed (partly offset by the scaled
  dual-wield block smalls). Intuitive Leap in cell 3 accepted disconnected node 55247, life went 5520 -> 5575 and the
  allocation survived a relog; in cell 1 it accepted 42981 but refused its neighbour 53802 outside the disc (no
  bridging). Thread of Hope uses the PoB 3_16 Variable rings 960-1320, 1320-1680, 1680-2040, 2040-2400 and 2400-2880,
  does not enable jewel sockets in the ring, and its -10% all-res roll changed `/resistances` 268/221/221 ->
  258/211/211; its ring value is implicit and the allocate mod's base radius is 0 so the client draws the annulus
  instead of a filled disc. Massive in cell 3 accepted 23507 (2628) and 48477 (523 allocated); after refunding them the
  Large item accepted 38129 (1808); swapping the jewel while those bypass nodes remained was refused. Might in All
  Forms in cell 1 changed the `passive jewel stats` melee value 129 -> 137 (+8 = 40 Dex+Int in radius / 5) while Brute
  Force was in the inventory; restoring Brute Force returned it to 127 (the conversion changes the baseline). Cluster
  passives are never in a radius jewel's radius: Path of Building precomputes `nodesInRadius` before its cluster graphs
  are built, so the server's ordinary-PSG-only radius is intentional. The 33 threshold jewels have generated item
  sources (`scripts/re/threshold_jewels.py`, `ThresholdJewelCatalogue.cs`); the client applies their local threshold
  mods, and `ThresholdJewels.cs` parses the target/attribute mask, sums every ordinary passive in radius (allocated or
  not, PoB's `Threshold` pass) and adds the mapped stat when the total is met. Live: `Combat Focus Cobalt` in socket
  36634 logged `elemental_hit_cannot_roll_fire_damage 1` (60 Int); `Collateral Damage` in socket 60735 logged
  `galvanic_arrow_area_damage_+% 50` and `shrapnel_shot_radius_+% 25` (60 Dex); `Spirited Response` in socket 6
  (cell 8) changed mana 2355 -> 2476 from its 10% maximum mana global. The skill-specific consumers remain partial and
  targets without a client stat row are skipped. Unnatural Instinct in cell 3 changed life 5520 -> 5685, mana
  2355 -> 2326 and ES 277 -> 285 from three allocated Int smalls and three unallocated Life/ES smalls. Lioneye's Fall's
  item source and client conversion are present (`/radius_jewel Lioneye's Fall`), but its server flag model is missing.
  Inspired Learning and Warrior's Tale remain inactive; the missing layers are the melee/weapon flag model, the
  rare-mod steal, tattoos and the skill-specific threshold consumers. The socket/save path,
  `PassiveTree.NodesInRadius`/`NodesInRing`, the timeless per-node pattern and the art table are reused. The radius
  suite has 98 tests.

- Impossible Escape (generated special): `/impossible_escape <keystone>|list` creates the 48-variant Viridian Jewel
  (Small, limit 1, corrupted). The item mod stores the keystone's `PassiveSkillGraphId` and display base radius 800;
  the effect radius is Path of Building 3_16's Small **960**. Legacy items with the Maven table row are migrated on
  load. `AllocationBypassNodes` uses the allocated keystone as the centre and the jewel socket as provider. Live: Versatile Combatant (row 2444,
  graph 50679) drew the client ring; node 29547 (Life Leech, 293 units away) was accepted without a path, pickup was
  refused until it was refunded, then both were removed. Supreme Ego (row 2312, graph 49639) showed no ring until the
  keystone was allocated; after allocation the ring appeared and Dire Torment (9015) next to it was accepted.
  Replenishing Remedies (50690, 953.7 units from Supreme Ego) is inside the client ring and is now accepted too. The
  command now warns when the chosen keystone is not allocated; the five legacy row-valued items were migrated on load
  and their tooltips now show Supreme Ego/Versatile Combatant. Rational Doctrine now has a `/unique_jewel` item source with
  its four ground mods; the ground mechanics themselves remain inert.

- Wiki catalogue gap check: the poewiki list (189 distinct names) vs this catalogue (182) leaves Bound By Destiny,
  Forbidden Flame/Flesh, Megalomaniac, Sublime Vision, The Balance of Terror and Watcher's Eye unimplemented; they are
  recorded in [unique-jewels](../docs/game/unique-jewels.md) without item sources. Impossible Escape and Rational
  Doctrine are the two gaps closed here.

- Command-based ordinary ascendancy selection, separate point awards, connected allocations, paid refunds and saves.
  A native Duelist/Slayer smoke checked ordinary nodes, ascendancy allocation/refund and both point budgets.
  Choice and progress survived relog and a Gateway process restart; a Phrecia request left the saved JSON unchanged.
  The read-only client trace observed class bytes, the Default graph, allocated nodes and separate point counters.
  Automated domain, packet and production-handler tests cover ownership, separate budgets, graph connectivity,
  multiple-choice effects, atomic refunds, stale connections and old/unknown saves.
  Area admission validates saved progress before inventory changes, then validates items before gold migration.
  Level changes recheck concurrent passive mutations before applying; queued tree packets publish current state.
  Old XP events cannot undo later gains or explicit level resets; Auth follows current XP.
  Old-area gains cannot suppress the live connection's pending level publication.
  XP level reconciliation adopts completed saved trims/refunds atomically before updating runtime state.
  Extra-point grants commit before runtime publication, including concurrent XP updates and failed-write checks.
  Code: `AscendancyCatalog`, `AscendancyProgress`, `OrdinaryPassiveRules`, `CharacterSaves`, Gateway progression.
  Docs: [scope, evidence and beginner steps](../docs/game/ascendancies.md).
- Behemoth: historical native acceptance at `9657e0ad` covers three legal connected eight-point builds.
  The candidate over main `c748f5fa` retains all nine notables and nine small nodes with automated checks.
  Real-client acceptance covers selected-class combat, all 13 Rampage tiers, Rage-paid Vaal Ground Slam,
  restrictions, critical strikes, Fortification, converted mana/armour and chaos mitigation.
  Paid refunds, reallocation, transfer, pause, death, relog and a Gateway restart pass; ordinary selection still works.
  Admission requires `Game_VERIFIED_BEHEMOTH=1` and an already installed, exact compatible client graph.
  The check is read-only; native Labyrinth acquisition and other Vaal executors remain separate.
  See the [per-node audit, explicit server models and evidence](../docs/game/phrecia-class-completion.md).

- Antiquarian: all nine notables and nine small nodes execute through three legal connected eight-point builds.
  Native checks cover Ward/Adrenaline, final attributes/requirements, stun recovery, Enduring Cry duration,
  always-hit attacks, Far Shot arrows and active-shield block/ranged mitigation.
  Paid full refunds, reallocation, pause, death, area carry, relog and Gateway restart were checked.
  Selection requires `Game_VERIFIED_ANTIQUARIAN=1` and read-only verification of the exact installed compatible graph.
  Ordinary Juggernaut selection after a full refund restores the Default graph.
  Docs: [whole-class evidence, setup and explicit model boundaries](../docs/game/antiquarian.md).

- Gambler: every node in its installed graph has a gameplay consumer, including both Block Luck choices and drawbacks.
  Legal eight-point builds, exclusive choices, paid refunds, failed-save rollback and reload have Gateway coverage.
  Incoming ordinary monster ailments give Risk Aversion an actual avoidance consumer, alongside bounded stun recovery.
  Native checks cover legal builds, both choices, outgoing/incoming resistance outcomes, damage, real ignite avoidance,
  lethal death-panel/checkpoint behavior, refunds, relog and restart.
  Current-base build/tests and exact related-head composition preserve the documented shared merge resolutions.
  Default Phrecia admission is gated; the explicit Gambler opt-in verifies installed graph bytes read-only.
  Docs: [Gambler checklist, scope and verification](../docs/game/gambler.md).

## Partly working

- Aristocrat: every node in its installed graph has a server consumer (gem levels and quality, attribute requirements,
  conditional ailment immunity, lowest-attribute damage, granted passive points), with Gateway coverage of legal builds,
  paid refunds and reload. Not yet checked with the real client: that needs the reviewed combined graph installed.
  Docs: [Aristocrat checklist](../docs/game/aristocrat.md).
- Phrecia identities, graph ownership and the point/save lifecycle have automated coverage.
  Phrecia game connections/selection are gated by default; Antiquarian and Gambler have accepted opt-ins above.
  Normal startup does not install or recover a graph and ordinary/unascended play has no alternate-graph preflight.
  The native Labyrinth/Altar flow is separate (#151); other Phrecia class gameplay remains separate work.
  Native ordinary command checks do not establish an expanded chooser or special class gameplay.
  Forest Tracking's attack-damage bonus from final evasion shares the existing Steel Willed calculation.
  Automated checks cover actual node allocation/refund/reload, final defences, signed composition and attack consumers.
  Seventeen native raw-input controls cover real damage, armour/evasion composition, added damage and relog.
  Full Daughter of Oshabi gameplay remains separate.
  Herald, Architect of Chaos, Paladin and Puppeteer family gem levels reach actual skill records and calculations.
  Automated checks cover real connected allocation, paid refunds, reload, supported levels and intrinsic requirements.
  Twenty-eight native checks cover effective family levels, unrelated sources, removal, unchanged storage and relog.
  Family-specific execution and Phrecia native admission remain separate.
  Docs: [scope and compatibility gate](../docs/game/ascendancies.md).
- Ruthless applies a configurable compounding XP multiplier from level 68 by default.
  The numeric rate is LocalExile tuning because the retail server formula is not public.
  Native Ruthless passive stat/point alternatives are selected for each Ruthless connection.
  Automated configuration tests cover valid live edits and retaining the last valid snapshot after invalid input.
  Native passive alternatives and the compounding level formula have automated coverage.
  See [league modes](../docs/game/league-modes.md).

## Not working yet

- Saving quest progress.
  Quest passive/refund rewards and native Labyrinth/Altar acquisition (#151) remain missing.
  Remaining special ascendancy gameplay and broader Phrecia client compatibility are separate work.
- Tattoos: the 100-item 3.29.3b catalogue, protocol and implementation groups are documented in
  [tattoos](../docs/game/tattoos.md). The client flow is implemented: C->S `0x77` names the inventory, item and
  passive; `0x33`'s fifth list carries `node | PassiveSkillOverrides.HASH16 << 16`; the item is consumed and the
  map is saved independently of allocation. **45 rows are client-verified**: the ten pilot, 22 plain stats,
  nine Makanga gates, three Journey tattoos and Journey Makanui; Ancestral Bloodlines is partial (life/mana
  recovery unread) and Hinekora/Kitava Makanga are inert. Firewalker also survived relog and was removed with an
  Orb of Scouring. The Makanga gate, Journey counting and the five `PassiveSkillOverrides.Limit` groups are
  enforced server-side; Makanui stores its random keystone in `TattooKeystones`. The character currently has 530
  passives and 12 tattoos from the test session. 52 rows remain not implemented; next are the condition/skill
  rows, Loyalty and the seven Forbidden ascendancy tattoos.
