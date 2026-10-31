# Status: Hideouts, crafting stations and maps

Part of [Status](../Status.md); what to build next in this area is in
[roadmap/hideouts-and-maps.md](../roadmap/hideouts-and-maps.md).

## Works (verified with the real client)

- Heist Locker is placed in each hideout and opens its native window (0x7b kind 0x37).
  The native window handler and visible state were verified through the real client.
  Its placement supports the hideout editor; storage transfers were not added by this change.
  See [21](../docs/protocol/21-hideouts.md), Heist Locker.
- Horticrafting Station (2026-10-27): placed beside the ordinary bench in every selected hideout; interaction opens
  the native recipe browser (0x7b kind 0x39).
  C→S 0x274 carries the selected HarvestCraftOptions HASH16 (captured live: ReforgeFire 0xa3e5); S→C 0x276 carries the
  crafted item id and clears the panel's pending state, so Craft can be clicked repeatedly without reopening.
  Targeted/affinity reforges, augments and modifier conversions execute on the item in HarvestCraftingItem; the exact
  Wild/Vivid/Primal/Sacred lifeforce cost is split across the main inventory and personal stash and commits with the
  item atomically.
  Refusals spend nothing; unsupported influence, exchange, divination-card, gem, corrupted-map and quality-enchant
  recipes are reported in chat and wait for their prerequisite systems.
  Automated tests cover raw rows/costs, every supported operation family, payment, insufficient funds and save
  rollback; station, window and the request were confirmed live.
  See [21](../docs/protocol/21-hideouts.md), Horticrafting Station.
- Generic 3.29 maps, first slice (2026-10-27): `MapKeyTierN` items serialize their MapKey component and `/item Map
  (Tier N)` creates one; the map device accepts them and uses `MapKeys.dat` for the tier. The native hideout Atlas
  window (S?C 0x7b kind 0x2b) shows the four starting layouts; a node click (C?S 0x00e7) selects one and Activate opens
  it. Every character has the epilogue quest flag `EndGameAvailable`, without which the client refuses Atlas activation.
  Live 2026-10-29: selecting Estuary opened Estuary; Cells is the default.
  Basic monster map drops use a local approximation.
  Atlas passive tree foundation (2026-10-30): the three saved presets are sent with their node lists and point budget
  (S→C 0x3d); C→S 0x3e/0x3f allocate/refund nodes and save them; `/atlas_tree` opens the tree and
  `/atlas_points <n>` sets the dev budget. Live: with 30 points, three "Map Drop Chance" nodes allocated and the
  client updated the tree; the presets are saved. The Atlas graph and point awards from map completion are not
  implemented, so connectivity is left to the client and the budget has no in-game source yet.
  The selected layout is saved per character, and generic maps now use their tier as the area level (tier + 67).
  Atlas progress, Voidstones, map stash placement and official drop pools remain open.
  Code: `AtlasCatalog.cs`, `AtlasPassives.cs`, `MapDevice.cs`, `ItemDrops.cs`, `InstanceItems.cs`.
  Docs: [21](../docs/protocol/21-hideouts.md), [maps](../docs/game/maps.md), [05](../docs/protocol/05-inventories-and-ui.md).
- Portal Scroll and the Portal skill (its 2.5 s cast): outside towns and hideouts a portal opens beside the player,
  replacing the previous one; from a map it leads to the hideout, anywhere else to the last town.
  That town or hideout gets a portal back, which brings the player to the portal's cell of the same instance;
  there the portal stands again (`OpenTownPortal`, `SpawnTownPortals`, `OpenPortals`;
  while the server runs, being tested 2026-10-23).
  Code: Gateway (`UsePortalScroll`, `UseMapReturnPortal`).
  Docs: [21](../docs/protocol/21-hideouts.md) §5.
- Hideouts: every character has the hideout unlocked (quest flags HideoutUnlocked, CanEditHideout …;
  league quest flags UnlockedHideouts, HaveHideout, UnlockedMapDevice in the Player component, sent as bit id − 1) and
  owns the Lush Hideout; `/hideout` (C→S 0x16 command 45) and the World panel's Hideout button (C→S 0x17a) take the
  player there (its own instance, seed 1) next to its waypoint;
  its Waypoint (with HideoutDoodad data), Stash, Nessa and map device come from its decoration (`scripts/re/atlas
  hideouts` → `data/hideouts.json`); the HUD shows the Edit button;
  the map device can be clicked and opens its window (S→C 0x7b kind 5);
  maps go into its slots, Activate (C→S 0x00d8) checks them (maps and map fragments only, one map), consumes them and
  opens six portals (MultiplexPortal + S→C 0x008f) into one instance of the map's area, each used once (the one taken
  stays gone); `An Audience With The King` is a fragment that opens the Crux of Nothingness by itself; in the map, a
  portal back to the hideout, the map's own monster packs (Maps.dat) and its boss arena
  gates (teleports); Nessa sells Beach Maps; `/league_flag`, `/hideout_keys` (dev, answered in chat).
  Code: `HideoutCatalog.cs`, `InstanceHideouts.cs`, `WorldAreaHideouts.cs`, Gateway (`WarpToHideout`,
  `SpawnHideoutDoodads`, `UseMapDevice`, `ActivateMapDevice`, `UseMapPortal`), `MapDevice.cs`.
  Docs: [21](../docs/protocol/21-hideouts.md).

## Partly working

- **Waypoint arrival.** Hideout entry uses the exact saved waypoint cell for spawn and movement origin,
  including login and returns from other areas.
  Live player and waypoint records used the same saved cell.

- Hideout editing moves and rotates installed furniture, the waypoint and NPCs through native C→S 0x5b.
  Placements persist per character/template; runtime interactions and path blockers follow the moved objects.
  Every template of the selected hideout is editable: one without a captured walk map leaves cell checks to the client.
  Furniture moves send 0x161 to rebuild native hideout model collision without changing object ids.
  Server blockers are registered again at saved positions on entry.
  NPCs and all functional furniture share inherited OT collision footprints and interaction clearance.
  Arrival correction runs after the full scene is registered; waypoints and ground loot remain passable.
  Live overlap escape and released-floor checks passed for all nine functional hideout objects.
  A player overlapping terrain or stationary objects is moved to a nearby clear cell after edits and during ticks.
  The map-device underfoot escape and walking through its released old position were verified in the client.
  Extracted AO hull radii cover small and large map devices; persistence and clearance have automated coverage.
  Map-device, Nessa and stash movement/rotation and reconnect persistence were verified in the real client.
  Adding/removing decorations and changing their appearance remain unimplemented.
  See [21](../docs/protocol/21-hideouts.md), Moving installed furniture and NPCs.
- Ordinary crafting bench (2026-10-25, automated coverage; live client verification pending): all 765 active recipes
  and their 130 recipe flags are available.
  Exact modifiers, class masks, socket/link recipes, flask enchantments, replacement/removal and rare-affix rerolls
  are served.
  Payment combines main inventory and personal stash, including multiple tabs/currencies, and commits with the item
  atomically.
  Replacing an existing crafted mod (the Craft button relabelled "Replace", an ordinary request) costs only the
  recipe; with the multimod only the conflicting craft is replaced.
  Ctrl + click (C→S 0xe9) moves an inventory item onto the empty bench slot and the bench's item back.
  Tests cover each recipe/class, replacement with and without the multimod, and failed-payment/save cases.
  See [21](../docs/protocol/21-hideouts.md), Ordinary bench recipes and payment.
- Bench affordability correction: native bench counters exclude ordinary stash inventories.
  A separate, nonspendable currency-count view now publishes current personal stash totals on opening and refreshes
  after crafting, while actual main-inventory currency is counted once.
  Payment still uses only real items; large totals retain their full value.
  Automated checks pass; corrected live UI verification is pending.
  See [21](../docs/protocol/21-hideouts.md), Bench affordability.
- Helena placement and hideout picker entry point (2026-10-25, awaiting client confirmation): Helena stands
  screen-right of Niko and Select Hideout opens the native window.
  All exported personal templates are owned; selection, transfer and persistence are implemented.
  See [21](../docs/protocol/21-hideouts.md).
- Hideout picker crash fix (2026-10-25, awaiting client confirmation): initialize the campaign unlock bitset with S→C
  0x1f6 on area entry, before Helena can open the picker.
  Missing data caused the confirmed client access violation at 0x14216d8b7.
  See [21](../docs/protocol/21-hideouts.md).
- **Niko and Kirac in the hideout (verified live, 2026-10-21):** Niko to Nessa's right, Kirac to her left.
  Kirac's hideout row has no dialogue of its own and uses the main Kirac's (`NpcCatalog.TalkSource`);
  Purchase Items sells maps.
  Niko's Delve options need quest flag ThisIsMyHideout (sent in hideouts and the Mine Encampment) and league quest
  flag 68 DelveEncounterComplete (every character): NPCTalk columns 15-18 are league quest flag ids.
  See [21](../docs/protocol/21-hideouts.md).
- **Delve, first steps (verified live, 2026-10-21):** View Subterranean Chart opens the chart (S→C 0x7b kind 0x1c,
  from the client's screen table); its data request C→S 0x23f is answered with S→C 0x240, and one entry draws the
  first mine section (the other fields are being worked out with `/delve_chart`).
  Visit Mine Encampment goes to Delve_Hub, which has its tile's waypoint (still drawn inactive: no 0x4c bit), Niko,
  the stash and the guild stash.
  The World panel's special pins are all shown (league quest flags for every character, the Labyrinth's waypoint
  always activated). The Hideout, Menagerie, Mine Encampment, Forbidden Sanctum, Kingsmarch, Rogue Harbour and
  Monastery of the Keepers pins lead to their hubs, which have a clickable waypoint back, a stash, a guild stash and
  their NPCs (Einhar, Divinia, the Kingsmarch villagers, the Heist crew, Ailith; Einhar also in every hideout).
  The hub waypoints stay unlit (client area-type rule); The Sovereign (Deepwater) has no layout yet.
  The upper hub entrance now reaches an initial Delve_Main mine section and its return gate leads back to the hub.
  Native entry, ordinary walking and return were verified; see the initial terrain block below.
  Mine encounters and progression remain unimplemented.
  See [16](../docs/protocol/16-waypoints.md) §4.
  See [21](../docs/protocol/21-hideouts.md).
- **Delve sulphite and the Crawler (verified live, 2026-10-29).**
  - The character's Voltaxic Sulphite is saved, sent on entering the Mine Encampment or a Delve area (S→C 0x228), and
    shown by the Subterranean Chart's gauge. `/sulphite <n>` sets it.
  - The HUD gauge shows only in WorldAreas type 27, the mine areas; the encampment is type 15 in the client data, so
    there the gauge is the chart's.
  - `/delve_crawler [x y degrees]` creates the stationary Crawler in the encampment or initial mine section,
    or moves and turns it.
    It uses the native DelveMapViewer interaction type with the Crawler model, highlights on hover and opens the chart
    on a real mouse click; the roaming DelveRobot serializer remains separate.
    Repeated creation is idempotent; client/server blockers follow relocation and chart use checks walk-up range.
    Attached wheel meshes and a powered torch use the original body bones; explicit looping idle runs the engine.
    The user confirmed the engine animation after restarting the client.
  - The Voltaxic Generator console and its cables occupy their native hub-room positions.
    Walking up and clicking opens the native Delve upgrades tab with resources, upgrade prices and purchase buttons.
    Its shared window requires permanent achievement set 2 in AreaInstance; an empty list hides all its tabs.
    Azurite, upgrade levels and flare/dynamite counts persist per account/league with atomic costs, caps and rollback.
    Server transactions have automated coverage; live purchases remain unverified because the current balance is zero.
    Equipment stats are sent in Delve areas; darkness damage and consumable use remain unimplemented.
  - The first mine area receives a 1×2 Delve terrain block with an entrance and an Azurite quest room.
    Delve_Main's copied hub is excluded from arrival selection; the actual mine-side transition is the return to camp.
    The real client generated the section, accepted walking within it and returned through that gate.
    Geometry uses client walk-map capture.
    The chart's native route request is parsed; unsupported routes report unavailable without spending sulphite.
    Route validation/execution, moving Crawler, darkness, encounters and rewards still need server mechanics.
  - The Crawler stands on the rails by the mine entrance at (408, 310), facing 90°, on every entry; the stash and the
    guild stash are at (355, 295) and (344, 295), facing 180°. Lioneye's Watch has them at (327, 270) and (317, 270).
    The user placed all of these live (2026-10-30).
  - `/stash_place` and `/guild_stash_place <x> <y> <degrees>` move the stash and the guild stash of any area until the
    player leaves it, to find their places.
  - See [21](../docs/protocol/21-hideouts.md) "Delve".
- **Hideout crafting bench placement (2026-10-25).** Personal hideout selection now handles the native 0x254 request,
  saves the selected WorldAreas Id per character and transfers immediately.
  Auth uses the saved choice on login; world-panel travel, `/hideout` and map return portals use the same home.
  Functional furniture and all four NPCs are placed on the selected template's floor.
  Selection decoding, persistence and placement have automated coverage;
  the new transfer and reconnect path awaits user verification in the client.
- The standard bench spawns to the left of the guild stash in hideouts, using the existing decoration's model and a
  walkable placement (placement confirmed in the real client).
  Clicking it now walks into interaction range and sends 0x7b kind 0x33, type 0 to open the ordinary crafting window.
  Automated checks cover the wire format; a live window-opening check is pending.
  Recipe unlocks and applying crafts are now implemented; see protocol/21-hideouts.md.

## Not working yet

- Hideouts: adding/removing decorative trees/tents and changing decoration appearance are not implemented.
  Personal template selection, saved home login and functional furniture placement are implemented
  ([21](../docs/protocol/21-hideouts.md)).
- Map device: a map without a walk-map capture puts the player at an arbitrary cell (Beach is captured);
  the arena gates land next to their pair, not on the telepads;
  the portals look like player portals; the open map is lost on a Gateway restart
  ([21](../docs/protocol/21-hideouts.md) §5, "Open").
