# Maps and the Atlas (3.29 rules)

How maps work in 3.29, the version this server targets. Build maps, the map device, map drops and the map stash tab
from these rules. The rules come from the project owner (2026-10-20, from the 3.29 game and its wiki); the data names
come from the extracted tables.

## Map items: 20 generic base types

3.29 maps are not named after an area. There are 20 map base types, and any of them can be used on any layout of the
Atlas: the player picks the layout on the Atlas, the map item sets the tier.

| Base type (BaseItemTypes Id) | Name | Map tier | Area level |
|---|---|---|---|
| `Metadata/Items/Maps/MapKeyTier1` … `MapKeyTier16` | Map (Tier 1) … Map (Tier 16) | 1 … 16 | 68 … 83 (tier + 67) |
| `Metadata/Items/Maps/MapKeyNightmareBoss` | Nightmare Map | 16 | 83 |
| `Metadata/Items/Maps/MapKeyShaperGuardian` | Shaper Guardian Map | 16 | 83 |
| `Metadata/Items/Maps/MapKeyVaalTemple` | Vaal Temple Map | 16 | 83 |
| `Metadata/Items/Maps/MapKeyReliquary` | Valdo Map | 17 | 84 |

Their item class is `MapKey` (ItemClassCategory 63, which StashtabAffinityByItemClassCategory gives the Maps affinity).

- **Nightmare Map:** modifiable only with Chaos Orbs, Vaal Orbs, Delirium Orbs and chisels. Tier 16 maps have a chance
  to drop as a Nightmare Map when Voidstones are socketed into the Atlas.
- **Shaper Guardian Map:** the area is influenced by the Shaper.
- **Vaal Temple Map:** the area contains 10 additional guarded Vaal Vessels; always corrupted. Obtained by corrupting
  tier 16 maps.
- **Valdo Map:** acquired from a Valdo's Puzzle Box.

The tables also hold about 490 older map bases named after areas (`MapTier1_1` "Cursed Crypt Map", `MapAtlasBeach` and
`MapWorldsBeach` "Beach Map" …, class `Map`, and Maps.dat with each one's `Regular_WorldAreasKey`). They are left over
from earlier versions: 3.29 doesn't drop them. Don't build new features on them.

## The Atlas

- The player's progress shows on the Atlas. Completing a map on the chosen layout reveals the adjacent connected areas,
  which opens new regions to enter.
- The player starts in the centre of the Atlas, where the first maps (area level 68) are.
- 16 standard tiers, each tier one area level (table above).

## Drops

- The lowest tier drops during the campaign; inside a map, higher tiers start dropping.
- Magic and rare monsters can drop maps one tier above the map the character is in; map bosses two tiers above.
- Moving from one group of tiers to the next is a challenge: higher tiers are harder to sustain. Mid and top tiers add
  higher values for the mods they roll, new harder mods, and mod combinations low tiers don't allow together.
- Many Atlas passive skills improve map drops.

## Voidstones

When a Voidstone is socketed into the Atlas:

- every map in that quadrant has its minimum tier raised to 16;
- found maps drop one tier higher, and maps below tier 16 are converted to gold;
- every map on the Atlas gets a global effect, including the chance for a tier 16 map to drop as a Nightmare Map.

## In this server today

- **Implemented first slice (2026-10-27):** generic `MapKeyTierN` items (MapKey component) carry their map tier and can
  be created with `/item Map (Tier N)`; the map device accepts them, the native Atlas panel selects a starting layout
  (C?S 0x00e7), and the device opens that layout at the map tier. The selected layout is saved with the character.
  The area level follows the tier (`tier + 67`), and server-generated terrain, the area instance and monsters use it. Every character has the epilogue
  flag `EndGameAvailable` that the client requires for Atlas activation. A map's first unique-boss kill marks the layout
  completed and awards one temporary Atlas point; bonus objectives and the client progress message are still open.
  Basic map drops from monsters use local rates; magic/rare monsters drop one tier above and unique map bosses two above
  the current map.
- **Atlas passive tree, first slice:** the three presets are saved and sent (S→C 0x3d); node allocation/refund
  (C→S 0x3e/0x3f) is saved, with a dev point budget (`/atlas_points`). The Atlas graph, point awards from completion,
  The active preset's map drop chance stat is applied to map drops; progress, Voidstones, map stash placement and the
  official drop pools are not implemented.
- **The map device** ([21](../protocol/21-hideouts.md) §5) opens a map's area from Maps.dat's `Regular_WorldAreasKey`,
  which only the old area-named maps have, and Nessa sells Beach Maps. Both follow the old system: with the generic
  bases, the area comes from the layout picked on the Atlas instead. Generic maps now use `MapKeys.dat` for their tier
  and the selected Atlas layout for the area; their area level follows the tier rather than the layout's WorldAreas row.
- **The map stash tab** takes nothing yet: the client lists its maps by tier, and how it places them is not known
  ([18](../protocol/18-stash.md) §7).
