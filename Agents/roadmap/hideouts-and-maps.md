# Roadmap: Hideouts, crafting stations and maps

Part of the [Roadmap](../Roadmap.md); what already works in this area is in
[status/hideouts-and-maps.md](../status/hideouts-and-maps.md).
Items keep the Roadmap's priority groups, and are in rough priority order within each group.

## 1. Quality of life

Harvest station placement, native recipe browsing and the item-recipe execution path (targeted/affinity reforges,
augments, modifier conversions, atomic lifeforce payment) are implemented; see Status and protocol/21-hideouts.md.
Remaining Harvest recipes (influences, stack exchanges, divination cards, gem lenses, corrupted maps and quality
enchants) wait for their prerequisite systems.

### Switching hideout templates

- **Known:** Helena now stands to Niko's screen-right and opens HideoutSelectionWindow (0x7b kind 0x22).
  The owned list contains the default Lush Hideout.
  [21](../docs/protocol/21-hideouts.md).
- **Start:** map the selection request, persist the chosen template, and use it for /hideout, player keys and portals.

The ordinary crafting bench's active recipes, unlocks and inventory/personal-stash payment are implemented (Status and
protocol/21-hideouts.md).
Disabled legacy recipes, map-device recipes and other specialised crafting stations are separate from the ordinary
bench.

## 2. The world

### Maps and the Atlas (3.29)

- **Known:** the 3.29 rules and the 20 generic map bases (`MapKeyTier1` … `MapKeyTier16` and four special ones) are in
  [game/maps.md](../docs/game/maps.md).
  Generic `MapKeyTierN` items, `MapKeys.dat` tiers, the MapKey component, the default Atlas layout and basic map drops
  are implemented; see Status. The native Atlas panel/progress and the map stash are not.
- **Start:** finished Atlas progress (client progress message, bonus objectives, adjacent unlocks and real point rules)
  and the Atlas graph validation; Voidstones; the map stash tab's layout (18 A5); the official drop pools.
- **Done when:** maps drop, are stored and run the 3.29 way, and the Atlas shows and saves progress.

### Hideouts: decorations, editing and the map device

- **Known:** the Lush Hideout works: travel, waypoint, stash, Nessa, the Edit button and the map device: slots,
  Activate and six portals into the map ([21](../docs/protocol/21-hideouts.md) §5).
  Its decoration (`data/hideouts.json`) lists every doodad with its type and model.
  The Heist Locker is placed and opens its native storage window; contract/blueprint transfers need separate work.
  Moving/rotating installed furniture and NPCs now saves per character/template through C→S 0x5b.
  The map device, Nessa and stash were checked live, including restored positions after reconnect.
  Other installed objects use the same handler; their individual editor interactions need manual verification.
  Device edits rebuild native walkability through 0x161 and replace the server footprint at the saved position.
  Small-device underfoot escape and walking across its former position are verified in the real client.
  NPCs and functional furniture now use inherited OT footprints and static-model hulls through one registry.
  Waypoints and loot remain passable. Check large MTX models and wall-adjacent layouts separately.
  Save/reentry persistence and collision escape for all supported NPC types have automated coverage.
- **Start:** create the decorations (`HideoutObject`, each type's components from data/object-types.json);
  the Edit mode's placement/removal and appearance messages;
  maps: the arrival point, a return portal, fragments and device recipes and map mods.
  Personal template selection and login to the selected home are implemented (see Status).
- **Done when:** the hideout looks like the real one, can be edited and saved, and maps can be run from its device.
