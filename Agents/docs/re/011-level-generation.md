# 011 — The client's level generator (port in progress)

Goal: the server generates every non-town, non-hideout area from a **random seed** exactly as client 3.29.3b does,
so walkability, area transitions, waypoints and room points no longer need a `live.ps1 -Walkmaps` capture per
(area, seed). State of 2026-10-22: the pipeline is mapped (below); ported and checked against the live client: the RNG,
the tile hash, the extra-terrain-feature rolls (for areas without area stats), the topology pick, and fixed layouts with
one room (`.dgr`): **Lioneye's Watch generates on the server tile for tile** (hash 1139181677, all 540 entries, every
draw up to the finished grid), and tile graphs (`.tgr`) far enough that **The Twilight Strand seed 1 generates tile
for tile** (hash 266153788, every draw up to the finished grid, §3g). Not ported: what §3g lists, parts of the doodads (§6c; room, random, vertex and tile doodads are in, and the Strand's
doodad-stage draws and walkability grids match). Seeds stay pinned to 1 until an area's generation matches (Roadmap "Server-side level
generation"). Code: `src/GameServer.Data/Terrain/Generation/`.

Size: the call tree under `0x14204d820` (callers ≤ 30, so allocators and strings excluded) is about 1 800 functions /
1.1 MB of code, ~89 of which draw random numbers. Most of it sits in `0x141fe0000`–`0x142130000`; the file parsers
are at `0x1429f0000`–`0x142a20000` and `0x142bb2a10`.

## 1. Entry and order

`0x142183130` (area load job, after 0x1d queues it) builds the generator (`0x14203bce0`, vtable `0x1435c6988`) and
calls `0x14204d820` "Generating terrain...". The generator object (`gen`):

| Field | Meaning |
|---|---|
| `gen[5]` (+0x28) | the tile grid (world vtable +0x180): +0x18 u64 width, +0x20 u64 height (in tiles), +0x28 entries of 0x38 bytes |
| `gen[6]` (+0x30) | generation params (world vtable +0x1a0): +0 u32 seed, +4 u32 seed actually used, +8/+10 flags, +9 u8 primary subgraph count, +0x10 vector of subgraphs (0x98 bytes each) |
| `gen[7]` (+0x38) | the world: +0xb0 WorldAreas row, +0xd0 topology variant, +0xd4 u8 level, +0x114 tile hash, +0x120 area stats (vtable +0 = stat lookup by id) |
| +0x40 | extra terrain features to add (`0x141f28aa0(name, count)`) |
| +0x88..+0x90 | ExtraTerrainFeatures rows |

`0x14204d820` in order:
1. seeds the thread-local RNG (TLS +400) with `gen[6]+0` (§2); WorldAreas byte +0x14e disables `Next` (always 0).
   Stats 0x22c1 / 0x22c2 / 0x528b set flags. Plugins (world +0xe0..+0xe8, vtable +0x10) run.
2. `0x14204a570` — extra terrain features: area-stat and WorldAreas-driven rolls (strongboxes, shrines, league
   encounters, discoverable hideouts…). It **draws even in plain campaign areas** (e.g. `Next(100)` against
   WorldAreas chances, `Next(100000)`), so it must be ported for the stream to line up.
3. `0x142049d10` — the layout: `0x142047e50` picks the topology (vtable slot 0 = `0x142046c60`, §3), sub-areas
   (slot 1 = `0x142047540`), places secondary subgraphs around the primary (`0x1420487f0`, sorts), sizes the grid
   (`gen[5]` vtable +8); then `0x1420452e0` "Processing subgraph" per subgraph (the room/tile solver), overlays
   (`0x142049ef0`), `0x1420455f0`, plugins +0x28, `0x14202eaf0`, `0x142044f30`.
4. `0x14204c7e0` (Betrayal forts / Vaal side area, when gen +0xe), `0x14204cbd0` (scales tile heights by the master's
   +0x14a), `0x14204cdc0` (VaalSideArea), plugins +0x48, world +400 → `0x141ebfeb0`.
5. `0x142049420` (outside tiles), `0x141d85970(gen[5])`, then vtable +0x48 `0x142048930` (zones / environments),
   +0x58 `0x1420425d0` "Generating doodads...", +0x50, +0x20, `0x14204d0f0`.
6. tile hash `0x141d8a460(gen[5])` → world +0x114 (§6), logged "Tile hash:" and sent in C→S 0x7d.

"Retrying with seed: {}" — a failed generation is retried with another seed (`gen[6]+4`); the trace records it.

## 2. Random numbers (`LevelRandom.cs`)

| Client | Port | What |
|---|---|---|
| TLS +400, `0x14204d820` | constructor | `std::mt19937` (MSVC double-buffer layout: +8 index, +0xc state[624·2], +0x1390 temper mask 0xffffffff), standard seeding |
| `0x140a0f990` | `NextRaw` | one mt19937 output (standard tempering) |
| `0x141f5d130` | `Uniform(n)` | MSVC `_Rng_from_urng`: one draw, rejected while in the last incomplete bucket; n ≤ 1 draws nothing |
| `0x141ff9720` | `Next(n)` | n = 0 → 1; returns 0 without drawing when the area disables randomness |
| `0x141f4fb80`, `0x14212d7f0`, `0x142129060`, `0x142037ea0` | `Shuffle` | forward shuffle: element i swaps with i + Uniform(remaining) (the last one draws nothing); `0x142037ea0` stops after a count |
| `0x142054a50` | `Chance` | percent roll: ≤ 0 never, ≥ 100 always, else `Next(100) < p` |
| `0x140e3b310` | `LcgBits` | MSVC `rand()` LCG at TLS +0x170 (×0x41c64e6d + 0x3039, 15 bits per step) |
| `0x142046a70` | `PickIndex(AndOrientation)` | one `Next(0xffffffff)` seeds the LCG; index by rejection over bit-length(n−1) bits; orientation restarts the LCG from the same seed: 10 bits until ≤ 0x347, then `0x14299da50` |
| `0x14299da50` | `OrientationFromMask` | r-th allowed code of a mask (0 / 0xff = r & 7) |
| `0x141d75980` | — | composes two orientation codes (9 × 9 table) |
| `0x141d8bba0` | — | proportional share with a random remainder (`Next`), used when splitting counts |

Checked: the first draw of Lioneye's Watch seed 1 is 1791095845 in the client trace and in the port (test
`FirstDrawsMatchLionsEyeWatchTrace`).

## 3. Topology

`0x142046c60`: stat 0xc1f forces topology N (1-based), stat 0xc20 an orientation (else 8 = random). Otherwise
`0x142027a20(area, world +0xd0)` lists the area's Topologies (WorldAreas in-memory +0x68 count / +0x70 rows) whose
Topologies +0x2c equals world +0xd0, else those with 60 (the default; 59 = Deepwater variants), else retries with 1.
`0x142046a70` picks one (`PickIndexAndOrientation`, mask from Topologies +0x1c, a list of codes, 8 = all; every row
lists `[0]` in 3.29.3b, so the orientation is 0). `0x142046340` then loads the graph (`0x141ff9340`: .dgr sets
flag 1, .tgr not) and `0x1420457f0` adds it as a subgraph: its master file (`0x14203c190`: the area's TSI override at
row +0x192, else the graph's `MasterFile:`, following .tgr/.dgr to their own master; a non-file name picks among
masters with `Next`), size from the graph (+0x40 / +0x48) scaled by the master's tile units (+0x149 − +0x14b), the
master's outer buffer (+0x170..+0x173), and the orientation's placement code (`0x142fa04b0`, as in
[08](../protocol/08-area-transitions.md) §5).

**Sub-areas** (`LevelGenerator.SubAreas`): while the records are not yet counted (generator +0x30 +9 = 0),
`0x1420457f0` runs `0x14204f590` on each new record's graph: every node passing the league filter (`0x142036490`)
whose description starts "subarea=" (case-insensitive) queues a world area at generator +0x1f0 — "subarea=hideout"
the claim subarea of the area's Hideouts row (+0x2a, "Cannot find Hideout claim subarea") when the discoverable
hideout rolled (generator +0xb), else the node's remaining descriptions are skipped; any other "subarea=X" the
WorldAreas row X ("Unknown sub world area"). A description starting "teleport" sets the record's +0x91 bit 1. Slot 1
(`0x142047540`) takes the queue and, for each, `0x142047090` → `0x142046340` with no topology: the world area's
topologies as in §3 (`0x142027a20`), `Next(count)`, then `Next(840)` → `0x14299da50` with the picked topology's
orientation mask; `0x1420457f0` adds its record. `2_8_5` (The Bath House) seed 1: the hideout's
`hideout_baths_claim.dgr` is record 1 (draws #7, #8). Sub-areas of a `.tgr` primary are not ported (the Act 9
`oasis_foothills_*.tgr`).

**Secondary records** (`0x142047e50`, `DungeonTiler.PlaceSecondaries`): each record after the first gets parent
+0x92 = its index ("Secondary graph: {}") and goes to a corner among those right of (x + w + 1, y) and below (x,
y + h + 1) each earlier record that keeps a tile clear of every placed rectangle (`0x1420487f0`); the score is
max(threshold, map width, its right) × max(threshold, map height, its bottom), and a stable sort (`0x142051070`)
takes the first lowest. The threshold is 0, or with more than 10 records 4/5 of the integer square root
(`0x141937840`) of their total area. The map is sized to the bounding box (`gen[5]` vtable +8): `2_8_5` 63 × 63 and
27 × 27 at (64, 0) → 91 × 63. Their parent is not 0, so they take no share of the extra room pool.

## 3b. Extra terrain features (`ExtraTerrainFeatures.cs`)

`0x14204a570` returns at once for towns, areas with randomness off (WorldAreas +0x14e, hideouts), area type
(+0x1e4) 0x13 and HASH16 0x8f53. Otherwise, with every area stat 0 (no map mods), its rolls come from the area's
`WorldAreaLeagueChances` row (+0x22a; +0x23e when world +0xd7) read by byte offset: Vaal side area `Chance(+0x60)`;
maps above level 72 a labyrinth trial `Chance(+0x6c)`; the discoverable hideout of `Hideouts.dat` (the row whose
SpawnAreas +0x1a lists the area: +0x42 enabled, +0x43 chance in 10 000; campaign hideouts have 10 000, no draw);
league mechanics by `CoreLeagues` row N (`0x14204a310` → `0x141ea1f50` picks the league-chances field: 1 → +0x24,
7 → +0x40, 0xf → +0x2c, 0x10 → +0x48, 0x12 → +0x34, 0x1b → +0x9c) rolled by `0x141ea2400` with the percent roll
`0x142054a50`; the fishing mission `Next(100000) == 0` when +0x88..+0x94 sum above 0. A zero chance draws nothing,
which is why the traces of The Twilight Strand and The Coast start with the topology pick. Rolls with a nonzero
league-mechanic chance (`0x141ea1c80`, which mixes in stats) are not ported and throw. The WorldAreas row in memory
is the `.datc64` row, so `AreaInfo.Load` reads these offsets from the file (`RawDat`).

**The extra room pool** (generator +0x40, `ExtraRoomPool.cs`). Each rolled feature that is an `ExtraTerrainFeatures`
row (`0x141f28aa0`: by id; the Vaal side area flag, the discoverable hideout and the shrine count are not rows) goes
into the pool in roll order (`0x141d8ae40`: count copies at the end of +0x48, the row's `.tdt` / `.tst` tiles into the
pool's own tile store +0x90, then the linked row +0x49 as many times). The Shrine row is capped by the shrine count
+0x148: with the Shrine rows already waiting or injected (`0x141d8ace0`) at most that many go in, and with none the
linked row is not added either. Row layout (0x66 bytes): id +0, rooms +8 (.arm), tiles +0x18, a bool +0x28, a string list
+0x29, room orientations +0x39 (8 = any), linked row +0x49 (8 bytes), WorldAreas +0x51, oversize flag +0x61 (rooms
for telepad injectors, `0x142101e50`), an i32 +0x62. Before the records (`0x142049d10`), with rows in the pool,
+0x140 = `0x14204bfa0` of the top graph: the sum of its subgraphs' masters' +0x152 (taken to be ExtraFeatureWeight,
default 0: no trace drew the roll below). Each subgraph whose parent is record 0 (`0x14203c900` / `0x14203f890`)
takes its share (`0x141d8bba0`, +0x144): all rows when +0x140 is 0, else rows × weight / +0x140 plus one when
`Next(+0x140)` falls below the remainder, and +0x140 drops by the weight. The layout's mission rooms take the rows
(§3c2 "Mission rooms"); `0x141d8b2c0` removes a row (and counts the share down).

## 3c. Fixed layouts: one room (`LevelGenerator.cs`, `DungeonTiler`)

The `.dgr` path of `0x1420452e0` is `0x14203c900`: rooms placed by `0x1421038d0`, then `0x1420f3480` tilises.
For a one-node graph (towns, hideouts):

1. **Grid**: graph `Size` × master `RoomUnitSize` (town 5 × 3 × 6 = 30 × 18); hideouts add the master's
   `OuterBufferSize` around it (HideoutForest 21 + 2 × 6 = 33, not ported).
2. **Room**: `0x142105a70` picks among the RoomSet's (.rs) rooms with the node's tag, `Next(total weight)` (the town
   has one: `Next(1)`, no draw). Its orientation is the node's rotation word (`R270` = code 3) composed with the
   topology's (`0x141d75980`, 9 × 9 table).
3. **Tilise** (`0x1420ef310`), slot by slot, row by row in the room's own grid: a 'k' slot whose key has no edges,
   no tag, no corner heights and one ground on all corners is left for the fill (`0x1420eec10` records its cells
   with the ground); any other slot's key is turned by the room's orientation and looked up in the tile store
   (`0x141d81f70`): `Next(total weight)` over the candidate list (`0x141d902d0`), placed at the turned footprint's
   top-left corner with the picked orientation (`0x141d833e0`; entries +0x34/+0x35 = offset inside the tile).
4. **Tile store** (`0x141d81590`): the master's TileSet (.tst, `0x1429f7040`: optional weight, quoted path,
   orientation words `I`, `R90`, `FR180`, … as a mask, none = all; `include`); every tile is filed for each
   orientation of its mask (and of the tile's own restriction mask, tile data +0x171, not ported) under (tag, key
   turned by that orientation), in list order. Tiles tagged `postunique` are not filed: they go to the TileSet's +0x80
   list (step 7b). A tile whose key is
   the same under two orientations is listed twice (live probe: `[(straight, 0), (straight, 4)]`). The key
   (`TileKey.cs`): edges, size, corner grounds (a ground whose .gt sets flag bit 4 is left out, `0x141d813c0`; the .gt parser `0x1429f6a80` reads
   five flag numbers after the name, number i → bit i, and a number the file leaves out keeps its default 1, 1, 1, 1,
   0: `chris_sand_dune.gt` "1 0" has flags 1 | 4 | 8),
   corner heights, edge points; turning uses the tables `0x142fbe3b8` / `0x142fbe3d8` and mirrors edge points for
   codes 4..7 (`0x141d80480`). `.tdt` layout after the string table: u32 "", u32 .tgt, u32 tag, 4 × u32 edges,
   u8 w, u8 h, 4 × u32 grounds, 4 × i8 heights, 4 × u8 points a, 4 × u8 points b.
5. **Fill**: the recorded cells plus every still-empty entry (ground "default"), sorted by (x, y), so column by
   column (`0x14212d520`); each empty one gets a tile from the master's FillTiles (.gft: quoted ground name, then
   "weight path") for its ground's name (.gt first line): `Next(total weight)`, then for a square tile `Next(8)` as
   its orientation (`0x141fecd00`; non-square tiles use `Next(840)` and a mask, not ported).
6. **Mirror seams** (`0x142025440`, after `0x1420f8f20`, which draws only when +0xb0 lists cells): row by row, a
   placed tile larger than one entry, without a tag and not a plain fill tile (`0x14201d800`: no edges but wildcard
   ones, .et +0x48 bit 7, and one ground on all four corners), none of whose vertices took a room's own vertex ground
   (`0x142025fb0`), whose neighbour one tile size away
   (up, right, down, left) is the same tile in the mirrored orientation (left/right: 0↔4, 1↔7, 2↔6, 3↔5; up/down:
   0↔6, 1↔5, 2↔4, 3↔7) is re-picked (`0x142027670`) among its key's candidates that make no such seam with their up
   and left neighbours (`0x142026100`) and do not mirror that neighbour: `Next(total weight)`. With none left, the
   edge-point fallback `0x142026bc0` (§5 step 6; it found nothing in the town). This is why every
   symmetric edge tile of Lioneye's Watch ends in orientation 4 or 6 whatever its first pick.

Lioneye's Watch (seed 1): topology (MT draw + 10 LCG bits), room `Next(1)`, 11 keyed slots, 82 fills, 3 seam
repairs: the grid is final after 181 logical draws, all equal to the trace's. The client's next draws
(`0x141530658`, `Next(100)` / `Next(110)`, ~300) and LCG bits at `0x142be7a0b` belong to later stages (meshes,
doodads).

## 3c2. Dungeon layouts: several nodes (`DungeonLayout.cs`, `DungeonRoomSet.cs`)

A `.dgr` graph with more than one node goes through `TileGraphGenerator.Prepare` (the same exist rolls as a tile
graph) and the layout `0x1421038d0` on a grid of the graph's `Size` in room units (a `DungeonMap`: cell 0 free, 1
reserved, k ≥ 2 = room k − 2). **The Ship Graveyard Cave (`1_1_9a` seed 1, `shipdungeon1_4.dgr`, 9 × 6 units of 7
tiles) generates tile for tile: all 2 835 entries, tile hash 3341726258, all 2 076 draws up to the doodads** (the
225 layout draws, the cells at every recorded stage and all 17 rooms included). So does **the Prison (`1_1_7_1` seeds
1, 1898277627, 4162278830; `prisonlower_4_1.dgr`, 12 × 8 units of 5, buffer 5, 59 × 43): seed 1 all 2 537 entries,
tile hash 1542354689, all 1 962 draws up to the doodads, all 13 rooms** (untagged nodes, special rooms, door tiles).

The master's **RoomOverlap defaults to 1** (Lioneye's Watch sets 0; the cave and the tile-graph masters set nothing):
rooms step by unit − overlap tiles and share their outer row and column with the neighbour. For tile graphs it only
shrinks the overlay exclusion of rooms whose own overlap (`.arm` header after version 21, room data +1) is the same.

- **.dgr records** (`0x142a0c0a0`): after the grounds a `Default%:` line (branching, extra door, fuzzy %, and after
  version 22 `open_extra_doors_into_impenetrable_rooms`); a node adds a voidness word `V`/`N` (+0x88) and, after
  version 21, its branching byte (+0x89); an edge adds chance2 (+0x4c), chance1 (+0x48), depth (+0x50), branch type,
  extra door % (+0x54: the path rooms' extra door chance, room +0x10) and type (+0x70, default rooms.et), fuzzy
  (+0x58), bias (+0x5c: the path rooms' search bias, fill state +8, and the edge lists' key), `I`/`P` (+0x60), `V`/`N`
  (+0x61), accessible (+0x62). (The reader stores them in this order; the port had +0x54 and +0x5c swapped until
  2026-10-23.)
- **Room set** (`0x141ff7930`, `.arm` header `0x142a0f3a0`): unit, size in units, tag, unique, the room's own key
  line (any slot, read by `0x142a0e2a0`: `k`, or `s` in `shrine_1_1.arm`; every `.arm` reads it so, and fill rooms
  such as `bodies_02.arm`, `pools_00.arm` and `bellyplant_01.arm` are 1 × 1 rooms keyed `s` whose slot grid comes after
  that line), then 2(w + h) exits clockwise (top left → right, right top → bottom, bottom right → left, left bottom → top):
  door type, height, chirality. Per door type a size table (`0x141ff5760` / `0x141ff6350`, the fill's sizes: the
  weights of the rooms filed under the type, at the sizes some room allows: a room that is not unique, room data +6,
  and, for rooms.et, whose exits all have a door at height 0). `0x141ff6960` files a room under each distinct door
  type of its exits other than the default (the global string at 0x1445c0fa8, rooms.et), else under room data
  +0x180: taken to be rooms.et for a room with a rooms.et exit and none for a room with no exits (`1_5_7`, Reliquary
  rooms: its exitless fills, `abyss_forceblank` and `vault_blocked_01` are in no size table, 1 × 1 2100 and
  2 × 2 2100 as the trace, not 2207 / 2203); per tag
  every room in every orientation of its mask (the "" list: untagged rooms); per size every room that is not unique
  and not of a special tag (shrine, mission, stonecircle, preunique, injectors), in every orientation that gives that
  size. Turning exits (`0x141ff2ca0`, `0x141d75a50`): exit i → (j + a·w + b·h) mod 2(w + h), a = 0 0 1 1 1 1 2 2,
  b = 0 1 1 2 0 1 1 2, j = i or, mirrored (4..7), 2(w + h) − 1 − i, which also flips a directional type's chirality.
- **Order** (`0x1421038d0`): void edges reserve their Bresenham line (`0x142002460`); tagged nodes then untagged,
  by priority, take a room of their tag (see "Node rooms" below); teleport flags (`0x142100be0`); then, edge list
  by edge list (`0x1420028c0`: per priority group and slot, the slot's edges keyed by `0x1420014a0`: I first, then
  bias descending (unsigned), then the door type's n0 (.et +0x4c) descending, then the type's path in ordinal order
  (`0x142006570`); one list per key in key order, built as the edges are grouped (`0x142002b50`), so a list a
  template node emptied stays; each `Next(count)` + shuffle, by priority), `0x1421040c0`:
  1. **Fill** (`0x1420fd4e0`): row by row each free cell gets a blank of one of the type's sizes (≤ 5
     `Next(total)` tries, else the first size, else 1 × 1); the row then moves on by the size's width, the first
     size's after a 1 × 1 (so the cell after it stays free: `1_5_5`); new rooms draw their `Next(128)` (`0x1420fd1f0`).
  2. **Connect** (`0x1420facb0`): A* over rooms (`0x1420fb720`: step = half the centre distance in half units,
     estimate = half distance × (100 − fuzzy) + bias × 128 / 100 + fuzzy × the room's `Next(128)`), then from the far
     end every room joins the path at the edge's level and each step opens a door (`0x142001e80`: the possible doors
     `0x1420017a0`, `Next(count)` + shuffle, the first).
  3. **Prune** (`0x1420fd9c0`): rooms off every path go (two-pointer swap), paths renumbered, cells rebuilt.
- **Branches** (`0x1420fc690`): the paths' door types sorted, `Next(count)` + shuffle, rooms.et last and types with
  one size first; for each type with a path whose edge has chance1: fill again, then `0x1420fca30`: blanks next to
  branching end nodes learn the path level (`0x1420fa120`); the path rooms (ends only for branching nodes) offer their
  neighbours (`0x1420fc240` → `0x1420fbfe0`: off every path, not impenetrable, levels fit, and `0x1420fb480`: a door
  there leaves some untagged room able to fit the blank, `0x141ff89d0`); below 100 % the offers are cut to
  share(chance1, n) (⌊c·n/100⌋ + (`Next(100)` < remainder)) after `Next(n)` + shuffle; each offer is taken
  (`0x1420fcf40`: level, type, flags, kind 5, a door, and while depth is left it offers its own neighbours); then
  round by round the new offers are shuffled, grouped by chance2 and share(chance2, n) of each taken. Prune.
- **Extra doors** (`0x142100d60`, rooms with a chance at +0x10) and **door swaps** (`0x1421012c0`, master
  `SwapDungeonDoorsFrom` / `To`): ported, not exercised by the cave.
- **Room picks** (`0x1421083e0`, each room on a path): a blank with a door type and no descriptions takes a room of
  its size whose exits have its doors (`0x141ff8300` → `0x141ff84e0`; strict = no extra exits, else extra exits of
  types that can end (.et flag 0x10) on the first pass, any on a second); weight (10 e² + 1) × room weight, e = empty
  exits both share; `Next(total)`, `Next(840)` the orientation among those that fit (`0x14299da50`); once more when a
  side neighbour already holds that room (`0x142108920`). The entry takes the room's turned exits (`0x1420fe360`).
- **Node rooms** (`0x142105a70`, a tagged node that is not "graph"): the wanted exits (`0x142104990`, fixed
  orientation): one per slot edge in slot order, (type, max of the edge's two nodes' heights − the node's, chirality
  = directional type and (node is the edge's A) xor edge chirality; `0x141ff2ee0`). The candidates
  (`0x141ff8ff0`) are the room set's per-tag (room, orientation) entries (weight ≠ 0) whose turned exits hold the
  wanted ones as a multiset keyed by (type, height, chirality) (`0x141ff8900`). None → the generator's extra room
  pool (layout +0x38 = generator +0x40, its (room, id) list at +0x18; `0x141d8b750`: entries whose id is −1 or
  layout +8 and whose room data +0x10 is the node's tag, whose unturned exits (+0x50) hold the wanted ones
  (`0x141ff8900` over the node's slot count), each in all 8 orientations at weight 1, size swapped for odd ones; not
  ported, and its writer was not found: no seed-1 capture reaches it now that the Labyrinth pass drops the `secret`
  nodes), else "No possible rooms found". Then `0x1421298e0` keeps those in the node's orientation (any for 8 and
  9) that pass `0x1421065a0`: cells free or reserved at the anchor (`0x1420fe0e0`); unless the room is the map's
  size, its doors leading to a cell inside the map must number at least the node's edges; a door into a placed room
  (not a blank) whose facing exit has another type is a mismatch (doors of rooms.et = layout +0x20, or of an .et
  with flag 0x10, aside). Mismatched entries go when some entry has none. Empty → "No suitable room found". Then
  `Next(total)`, the anchor (`0x1421062e0`, graph orientation), and once more when a side neighbour holds that room
  (`0x142108920`). Orientation 9 (`0x142104990`'s first branch: the slot edges sorted by the direction to their next
  point, `0x141ff81b0`) is not ported.
- **Node door type** (`0x142003740`, after the room pick of `0x142106b00`, and in `0x142107130` once a blank's
  rectangle fits): the node's four edge slots (+0x58) with an extra door % (+0x54) are weighted by it, `Next(total)`
  picks one, and its extra door type (+0x70) becomes the room's +0x48 type; with none, no draw and the default
  (rooms.et). An edge whose extra door type is `""` (the parser reads it over the default) holds rooms.et too: in
  `2_8_5` seed 1 `exit_1.arm`'s extra door goes through a rooms.et exit, which an empty type would not allow (the
  port kept `""` until 2026-10-23). The room's extra door chance (+0x10) is the largest of the slots' +0x54. Missing until 2026-10-23
  (session 3o): every `.dgr` whose node edges had an extra door % lost the draw (Act 5, Act 10 and others).
- **Template nodes** (`0x142107840`, `DungeonLayout.PlaceTemplate`; nodes with the description "template", before
  the tag test; the Sceptre of God `1_3_18_*` corners and sides): each slot edge whose type is not a wildcard (.et
  flag 0x80) goes to a side (`0x1421299f0`): towards its first internal point from this end (the other node without
  points), |dy| < |dx| → right / left, else down / up (0 top, 1 right, 2 bottom, 3 left). While a side holds two, its
  slots may move to a neighbouring side holding none (`0x142129d80` with 0), else one (with 1): of all such moves
  (sides in order, the side before then the side after) the one nearest the new side's direction (`0x14299d730`:
  2·a·b·127 / isqrt(|a|²|b|²) / 2 as a signed byte + 127 into an arc-cosine table at `0x142fe0b10`, the first
  lowest); "Failed to calculate edge layout" if a side still holds two. The side's exit is (edge type, height 0,
  direction as for node rooms `0x141ff2ee0`), no door where no edge. An edge of another type than rooms.et
  (layout +0x20) leaves the void list or its key list (`0x142002e40`; the emptied key list stays and still draws
  `Next(0)` in `0x1420028c0`, so the key lists are built when the edges are grouped), and its type joins the
  layout's set (`0x142127400`, ordered by handle address: with two types, as in `_city_tower3_3.dgr`, the port takes
  slot order; `1_3_18_2` seed 1 agrees either way up to its stop). The tag's room-set entries whose four sides each
  hold that exit (`0x141ff81b0` → `0x142012ab0`), kept to a "size:" size ("No matching rooms of size"), else all of
  one size ("Multiple room sizes found"); `Next(total weight)`, the anchor (`0x1421062e0`), the node door type, and
  `0x142100650` places a blank: no room, orientation 8, kind 0, +0x40 = the first set type (else rooms.et), flag 4,
  with the picked room's turned exits whose being rooms.et matches +0x40's. A template node without edges (a "size:"
  blank at its clamped corner) is not ported. `1_3_18_1` seed 1: twelve template nodes, every draw (2 453) up to the
  numbered special rooms matches.
- **Untagged nodes** (`0x142107130`): blank kind 3; its sizes from the first slot edge whose type is not the
  default rooms.et (layout +0x20), else rooms.et (`roomSet.Sizes`, `0x142106db0`; the port took the first slot's
  type until 2026-10-23, which with the node door type draw broke the Templar Courts `1_5_4`, "size:" description filter `0x142127f90`), `Next(total)`, anchored at the graph-oriented corner (not
  clamped); the rectangle must be free cells with no other node's cell (`0x1421069d0` → `0x142133340`), else that
  size is dropped and the pick redrawn.
- **Special rooms** (room data tags starting "special": .arm flag +8, number after "special" at +4 else −1, chance +5
  default 100; filed apart, `DungeonRoomSet.Apart`, so out of the size tables): `0x142101aa0` → `0x1420ed7a0` →
  `0x1420ff140`: each unnumbered special room by its chance. `0x1420ff140` takes a list of rooms (with orientation
  masks, 0 = any): candidates are the accessible placed rooms with a room, not special, empty tag, whose size and
  exits fit some allowed orientation of a listed room (the first, `0x1420ffb90`; exits by `0x1420feff0`, door types
  that can end may be missing), kept by entry index (`0x142127a50`, sorted) with each listed room that fits;
  `Next(entries)`, then `Next(that entry's rooms)` (`0x141f4f970`, weight 1 each); the entry takes room and
  orientation, kind 8. Not ported: numbered lists, placement over several rooms (`0x1420ffcf0`), injector rooms,
  room data +9 / +0x13a and `0x142101e50` (oversize rows into telepad injector rooms).
- **Shrine rooms** (`0x142103490`, `DungeonLayout.ShrineRooms`): with shrines rolled (pool +0x148), as many tries as
  the count: the room set's "shrine" rooms (+0x118) of the layout's unit size, any orientation, go to `0x1420ff140`;
  a placed room with a shrine object (room data +0x70, taken to be a `Metadata/Shrines/Shrine` object in the `.arm`)
  counts a shrine placed (`0x141d8b9a0`: the count drops by one, +0x14a rises, the first Shrine row leaves the pool;
  the position goes to `0x141d8abb0`, which nothing in the layout reads). The 48 shrine seeds of the oracle all
  generate since 2026-10-23; most run to another hash, so this needs a trace of a shrine seed (`Game_SEED`, e.g.
  `2_10_7` seed 3117172586).
- **Mission rooms** (`0x142102170`, after `0x142101aa0` / `0x142101e50`): the room set's "mission" rooms by their
  count n of 'k' slots tagged "missiontile" (room set +0x130, `0x141ff6960`, ascending; "contains no mission tiles"
  for none), those of the layout's unit size. While the subgraph's share (a copy of pool +0x144) lasts, each pool
  row that is not oversize offers its room (several: `Next(count)` + shuffle, the first that goes; `0x142102ed0`):
  with mission rooms present a 3 × 3 room (room data +0x138 / +0x139, tiles) waits for a mission tile; any other
  room of the unit size replaces a placed room through `0x1420ff140` (its row then leaves the pool, `0x141d8abb0`
  records it by position, not ported). The waiting rooms (more than the share: shuffled and cut, `0x14212dec0`, not
  ported) then need mission rooms: per count n in order, k = min(left, waiting / n) … waiting / n mission rooms
  from a copy of the layout (each `Next(list)` + shuffle `0x142129610` and `0x1420ff140`); for what is left, counts
  below n from the largest down while one places, then any count from the smallest up; when every waiting room has a
  tile, each gets `0x14299da50(mask, Next(840))` and goes to the pool's injected list (+0, 0x28 bytes: room,
  orientation, row), its row leaving the pool. With no full placement the last try stays and as many waiting rooms
  as it placed tiles for go. MapWorldsGorge seed 1: Village (`ore_node.arm`, 3 × 3) into `mission_4_1.arm` (one
  mission tile), `Next(1)`, shuffle, `Next(25)`, `Next(1)`, `Next(840)`.
- **End** (0x142103f31): door counts (`0x142100910`); fill rooms (`0x142108dc0`, below); the special rooms,
  oversize rows, mission rooms and shrine rooms (`0x142101aa0`, `0x142101e50`, `0x142102170`, `0x142103490`);
  reserved cells become free; fill rooms again.
- **Fill rooms** (`0x142108dc0` → `0x142108f70` while it places one, first for grounds with .gt flag 2, then the
  rest): every placed room (those placed in the pass too) whose two corner grounds along a side are the same (room
  data +0x13a = the room key's corners TL, TR, BR, BL, turned: table 0x142fbe348 = inverse orientation, then
  rows 0 1 2 3 / 1 2 3 0 / 2 3 0 1 / 3 0 1 2 / 1 0 3 2 / 2 1 0 3 / 3 2 1 0 / 0 3 2 1) offers each cell across that
  side (right, bottom, left, top) at the level of the exit there (master DungeonFillRoomHeightOverride, +0x159 /
  +0x14d, not ported) to `0x1421096f0`: a free cell (0) takes a fill room of that ground. Fill rooms (room data +9,
  `0x141ff4e70`: four equal corner grounds, no exits, +0x40 clear, taken to be no tag and not unique) are filed by
  `0x141ff5760` per ground into a size list (room set +0x98: (w, h) and, not square, (h, w), each at the room's
  weight) and per (ground, size) into a room list (+0xd8: mask all for square, else & 0x55 at (w, h), & 0xaa at
  (h, w)). `Next(size total)`; `0x142109e90` tries the size with the cell at its top left, top right, bottom left,
  bottom right (`0x142109c10`: inside, free cells `0x1420fe050`, `Next(room total)`, `Next(840)` orientation
  `0x14299da50`, placed kind 6 at the level, accessible when the ground is null or has .gt flag 4 and is not
  master +0x230, taken to be OuterGroundType); a size that fits nowhere goes and the pick repeats. Master
  +0x238 / +0x240 ground overrides over a flooded region (`0x142108a40`) are not ported.
- **Search neighbours** (`0x1420fb720` → `0x1420fee60` with `0x1421337a0`): a neighbour is kept when a door from
  the current room to it is possible (`0x1420fb650(cur, n, level, type, chirality)`); the step test is then
  `0x1420fb650(n, cur, …, chirality ^ 1)`. The port had the first one reversed, so paths of door types with a
  direction never connected (57 oracle loads).

- **RoomOverlap in the layout:** the middle of three rooms on a path (`0x1420fa240(next, middle, before, …, type,
  chirality)`, in the connect and for every path in the branches) lets the blanks across its doorless exits learn
  that exit's level, and a branch room (`0x1420fec10`) the blanks around it and across its corners
  (`0x1420fa120`). For a door type with a direction (no RoomOverlap test): the sides of the middle room's first
  door to `next` (chirality flipped) and to `before` (`0x1420017a0`); for ground 1 (null or .gt flag 1) the sides
  (1 + 2·flipped + door side) & 3, for ground 2 (1 + 2·chirality + door side) & 3, each through `0x1420fa750`:
  when side s (top, right, bottom, left) has no door, the blanks across the side in the order top, bottom, left,
  right (s → that one, as the client) learn their levels.

`levelgen generate` compares the trace's `dungeon.stages` (cells at the entry `<` and exit `>` of each stage) and
`dungeon.rooms` with the port's (`LevelGenerator.DungeonStage` / `DungeonRooms`).

**Tilising** (`0x14203c900` → `0x1420f3480`, `DungeonTiler` in `LevelGenerator.cs`):

1. **Grid:** (unit − overlap) × size + overlap tiles plus OuterBufferSize on every side (cave: 6 × 9 + 1 + 8 = 63,
   6 × 6 + 1 + 8 = 45); room k at buffer + (unit − overlap) × its cell.
2. **Rooms** (`0x1420ef310`, map order): slot by slot as for the towns (`k`, `f` and `s` slots; height = slot height +
   room level). A slot without a tag whose key has no edges, no heights and one ground goes to the fill
   (`0x1420eec10`) before anything else, even on the room's outer row or column, so it is never shared. A key's last number (slot +0x73, "corner": 0 top left, 1 top right, 2 bottom right, 3 bottom left) is
   the corner at the slot's cell: the key moves by 1 − w when corner % 3 ≠ 0 and by 1 − h when corner > 1, in the
   room's unturned cells, before the orientation (`RoomSlot.ShiftX` / `ShiftY`; also for tile-graph rooms and the
   fill's slots). Each tile is placed (`0x141ffb870`) and then set to the slot height (`0x141d82d00`): its vertices take
   that height plus the tile's own whatever their grounds (placing alone writes a vertex height only where a ground
   has flag 1 or there was none). `0x141ffb870` first drops the mirror of a tile whose four key edges are none or
   wildcards (.et +0x48 bit 7) and whose tile data +0x16f has bit 1 (`0x141d7cc50`: an object's type path contains
   "Spawner"; bit 0 is the transition flag): orientation & 3 (the
   Labyrinth's statue spawners; their o and o + 4 keys are equal, so the store's pick lands on either; `TileGrid.Place`,
   `TerrainCache.Unmirrored`). The same holds for the shared slots (`0x1420f5fb0`). With RoomOverlap, a slot whose key has an odd size and is centred on the room's outer row or column
   (`0x1420f0a70`, not for `missiontile`) is shared: `0x1420eeec0` stores it by position (sorted by x · 65536 + y),
   with the sides the room covers (0x10 right, 8 left, 2 bottom, 4 top) and the side it lies on; a second room's slot
   is merged (`0x1420ee240`): corners both of whose sides it covers take its ground and height (`0x1420ee460`), sides
   it covers take its edge, or, covered twice, the two edges combined by the master's EdgeCombination (.ecf,
   `0x1420ee580` / `0x1420f9880`; one edge stays, two without an entry give none) and the side is marked shared
   (`0x1420ee900`); then the key's heights are made to start at 0. A slot tagged "missiontile" without a tile in the
   store (`0x141d81f70`) takes an injected room of its turned size from the pool (`0x141d8b450`: `Next(count)` over
   the injected rooms that fit, in list order): its w × h vertices take the slot height and flag 2, then
   `0x141fe89e0` tilises the room there, column by column: each 'k' / 'f' / 's' slot whose entry is still empty takes
   a tile of its tag and key (`Next(total)`, up to five tries until `0x141ffbbe0` lets it fit) at slot height + the
   mission tile's; then the room's vertex grounds, and every vertex of it still without a ground takes the ground at
   the mission tile's corner. Not ported there: plain slots (fill rooms through master FillTiles), nested mission
   tiles, the pool's own tiles (+0x90, "Could not find matching feature tile"), the description and rectangle
   records (`0x141d83f10`, tiler +0x18 +0x30 / +0x60).
3. **Shared slots** (`0x1420f6b70`, by x then y): without tags and with a plain key (`0x1420ee9e0`) they go to the
   fill (with the slot's accessible bit, +0x5c bit 0); else their tags are tried in `Next(total)` order (each weighted by its store total) and then the untagged
   store (`0x1420f5fb0`: `Next(total weight)`). A 1 × 1 slot placed with tag "door", and an untagged shared tile with a 3 × 3 or passage-shaped
   key (end of `0x1420f6b70`), become doorways (`0x1420edf80`; non-1 × 1 ones apart).
   **Without a match** `0x1420f5fb0` tries, in order, until the store (`0x141d81f70`) has tiles:
   - the movable sides' points moved to the middle (3 × length / 2), one side at a time and cumulatively
     (`0x1420f69c0`). A side is movable when it has an edge that is not virtual (.et +0x48 flag 4) nor N2 (flag 8),
     its point A (key +0x46) is not the middle, and the slot beyond it is a shared slot: at (x, y − size),
     (x + w, y), (x, y + h), (x − size, y) for top, right, bottom, left, size = .et +0x4b (a size of 0 above or to the
     left means no slot; master +0x162 set would drop the shared-slot test, taken as clear);
   - when the key has any height, from the key again: one corner whose ground is set and lacks .gt flag 1 at height 0
     at a time (`0x1420f6ab0`); then the points, then the corners once more; failing all, the key again;
   - every such corner at 0 at once (the others keep theirs), then the points;
   - when the remaining corners have one height, all corners at 0 and that height added to the tile's.

   A tile placed with moved points is forced (`0x141ffb870` flag: grounds forced); then `0x1420f5b20` visits the
   slot beyond each moved side: a shared slot not placed untagged (flag 0x20) whose facing side has the same edge and
   length takes the middle point there; else a placed tile there (its turned key, `0x141d80480`) with that edge and
   length and another point is placed again (grounds left out of keys dropped, the middle point, its tag and height,
   `0x1420f5fb0` with the flag that stops the point moves and this step), or "Could not find matching adjacent tile".
   Not ported: the description objects (`0x141d83f10`) and `0x1420f8df0` (records of master +0x218's edge types
   that 1 to 3 of the key's edges have, tiler +0x40).
3b. **Door tiles** (TileSet has "door" tiles, tiler +0x38; with RoomOverlap): `0x1420ed9f0` marks each room's exits
   (4 / 0x10 / 2 / 8 at 1 or 3 entries per exit) before its slots; `0x1420f4010`: per doorway the orientation, the
   sizes from the store's "door" sizes for its edges, `Next(n)` + shuffle, the key resized with points at none, the
   middle of the unit for odd units else `Next(len − size + 1)`, `Next(total)`; `0x1420f4ae0` for the big ones.
   Without RoomOverlap (`0x1420edd50` / `0x1420f4d90`) not ported.
4. **Room vertex grounds** (`0x141ffbd70` per room), then the **room grounds** (`0x1420f0e10` per room, map +0x10
   off): every vertex of the room without a ground (flag 1 clear, or a null ground) takes the graph's DefaultGround
   (tiler +0x20 = graph +0xb0); TopGround (+0x30 = +0xa0) instead when its height is the highest of the view
   (`0x141ffc700`), else BottomGround (+0x28 = +0xa8) when the lowest (`0x141ffc7d0`), both only when set and the
   default is not impassable_ground.gt. With RoomOverlap a room leaves out its left / top vertex column / row unless
   its cell is at x / y 0, and its right / bottom one unless its cell x / y is the map's last (cell, not cell + size).
   Not ported: room data +0xa with +0x13a (the room's own ground instead of the default, taken as clear) and the
   master's level overrides +0x146 / +0x144. Then the **fill**: the recorded cells (`0x1420eec10`, which also
   gives the footprint's vertices the height and flag 2) sorted by (x, y), each still empty one filled by
   `0x141feb970` (as for tile graphs) for its ground, or its top-left vertex's ground when that vertex has flag 1,
   at the record's height (`0x1420f3de0` → `0x141fec8f0`: a given height is used, 0x80000000 = corners' middle).
5. **Mirror seams** (`0x142025440`, the same pass as for tile graphs, `Seams.cs`, with the fallback `0x142026bc0`):
   `0x1420f3480` calls it on the layout's view (its corner to step × size + overlap) with the placed rooms as the room
   list, so tiles inside a room never take the fallback and tiles touching a room's vertex ground stay. The one-node
   path runs it on the whole map with its one room. Then, after the subgraph (`0x142044f30`), the **outer ground**
   (`0x14201db80`) over the subgraph's view (`0x141ffb6c0`): the layout inside the outer buffer, not the buffer.
   Vertices with flag 1 get flag 2. When the outer ground's .gt has flag 1 (or master +0x161), every empty entry
   takes its first fill tile at orientation 0, no draw. Otherwise the heights are fixed: row by row an empty entry
   with a flag-2 corner is ready at its corners' middle (max − (max − min) / 2; `0x14201ec30` / `0x14201e930`), the
   rest wait; the ready ones are laid (`0x14201e4f0`: all four vertices take the height and flag 2, then the tile)
   and the waiting ones tried again. For a w × h fill tile only entries on its grid settle ((x + a) % w = 0 and
   (y + a) % h = 0 in view coordinates; a = 1 for a 3 × 3 tile when the view's origin and size are all 1 mod 3, the
   grid then starting one entry out), by their own four corners; a ready one is laid only where the tile fits on free
   entries inside the view (one entry more each side when a = 1, `0x141d84e10`), all (w + 1) × (h + 1) vertices
   taking the height, and joins the outer tiles either way ("Could not fix heights in dungeon" when none settles); then up to 15 rounds over
   every outer tile (those the fill laid too) compute all moves to the corners' middle first and apply them
   (`0x14201ed90`, `0x141d82d00`), stopping after a round with no move larger than one level.
6. **Outer buffer** (`0x14201ffa0`, `OuterBuffer.cs` as for tile graphs) around that view: the inner border's
   vertices spread outwards and the buffer is tiled. Then StoreyHeight scales every origin's height (`0x14204cbd0`).

Not ported on this path (no draws in the cave): `0x1420f4010` / `0x1420f4ae0` (RoomOverlap), `0x1420f7960`,
`0x1420f1590` / `0x1420f2340` / `0x1420f3100` (unique, chest, pre-unique rooms), the overlay passes, `0x1420f8f20`.
**The Fetid Pool `1_1_3a` seed 1 (`fetid_3.dgr`, trace recorded 2026-10-23) generates tile for tile: all 3 060
entries, tile hash 401409093, all 910 draws up to the doodads** (directional door types, fill rooms, slot corners,
3 × 3 outer tiles). **The Slave Pens `1_5_1` seed 1 matches by tile hash (3415378844)** with the node door type and
the overlap fallbacks. **The Reliquary `1_5_7`, `1_5_2`, The Ossuary `1_5_5`, `2_10_4`, `2_10_7`, `2_10_8`, `2_8_2_2`,
`2_7_5_map`, `2_9_6` and The Mud Flats `1_1_3` (seed 1 each) match by tile hash** with the plain slots before the
shared-slot test, the room grounds `0x1420f0e10`, the seams' plain-tile and room-vertex tests, the shared slots'
accessible bit, the `s`-keyed fill rooms, the layout fill's step after a 1 × 1 blank and the post-unique tiles per
subgraph. Oracle stops on `.dgr` areas: see the oracle line in §8.

## 3c3. Composite maps: subgraph nodes (`DungeonTiler.Composite`, `DungeonLayout.PlaceSubgraphNode`)

A `.dgr` whose nodes are tagged "graph" is a frame for other graphs. **The Prison's second level (`1_1_7_2` seeds 1,
2512127042; `prisonmain_1_1.dgr`, 115 × 45 units of 1, OuterBufferSize 5) generates tile for tile: all 6 875
entries, tile hash 1294234863, all 4 134 draws up to the doodads, every room of its three layouts.**

- **Records** (`0x1420457f0`, 0x98 bytes, generator +0x30 → +0x10): graph path (+0x20), rectangle (+0x40 x, +0x44
  y, +0x48 w, +0x4c h), master (+0x50, the graph's MasterFile), flags (+0x90, bit 0 .dgr), parent (+0x92),
  orientation (+0x97). For a .dgr: w × h = (RoomUnitSize − RoomOverlap) × the graph's Size + RoomOverlap, swapped for
  an odd orientation, plus the margins. The margins are OuterBufferSize's up to four numbers (`0x142a06390`): top
  (+0x170), right (+0x171, else top), bottom (+0x172, else top), left (+0x173, else right). The top graph is record
  0 at (0, 0); the tile map is its size.
- **Graph nodes** (`0x142105a70` → `0x1421052e0`): descriptions = graph (.dgr / .tgr) or FileGroups group, width,
  height ("Could not parse special graph node", "Bad special graph node size" over 1000; swapped for an odd graph
  orientation). A group (.fgp, master FileGroups: a quoted name, then its files) draws `Next(count)`; when this
  layout already took files of the group (layout +0x40), those are left out and, if any are left, `Next(left)` draws
  again; the file replaces the first description. The node takes a roomless kind-1 entry of w × h at its anchor
  (`0x1421062e0`).
- **Driver** (`0x142049d10`): records in order (the list grows while it runs), each with the view set to its
  rectangle (`0x1420452e0`): .dgr → `0x14203c900` (layout at the view's corner inside the margins), .tgr →
  `0x14203f890`; then `0x14203ece0` (the post-unique tiles over the record's view, as for tile graphs, §7b; The
  Ossuary `1_5_5`'s stained-glass glyphs), `0x14203e1f0` (not ported). At the end of `0x14203c900`, `0x14204f030`: every roomless entry
  with descriptions adds the record of its first description at its corner (cell × step + the view's corner;
  orientation 0 has no offset), parent = this record, orientation = this one's. After all records, last first: each
  one's outer ground over its view and its outer buffer (`0x142044f30`); then StoreyHeight (`0x14204cbd0`): every
  origin with a height takes the StoreyHeight of the last record whose rectangle holds it (`0x141d77a90`), else
  record 0's (map +0x104 is set per subgraph and reset to 1 there; room heights +0x58 scale by their record's).
- **`.tgr` subgraphs** (`0x14203f890` on the shared map): the record's size is the graph's `Size` (+0x40 / +0x48)
  plus its master's margins; the view is the record's corner inside the margins (left +0x173, top +0x170), "Subgraph
  … extends outside the TileMap!" past the map. Map +0x104 = the master's StoreyHeight, map +0x10 (ground checks) on,
  then the whole tile-graph pipeline (`0x142116600`, §3d–3g) on that view over the tiles already there; the
  walkability pass (`0x1420289a0`) only for the first generator +0x30 +9 records (taken to be 1) or a master with
  +0x164 (taken to be CountAsPrimary); the post-unique tiles and the outer stages are the driver's, as for `.dgr`
  records (a `.tgr` record's outer ground and margins are not ported). All stages take separate x and y offsets now
  (`SpecialNodes`, `EdgeRouter`, `EdgeTiling`, `GroundStages`, `HeightLevels`, `Overlays`, `Fill`, `Storeys`).
  **MapWorldsGorge seed 1 (`mapglacier_01.dgr`, 14 × 14 units of 5, its "graph" node at (0, 0) 5 × 11 units taking
  `_glacier_vertical_01.tgr`, 36 × 57) generates tile for tile: tile hash 3320701478 and all 4 778 draws up to the
  doodads** (with the Village row in `mission_4_1.arm`, §3c2). One view vertex differs at the fill (18, 45): flag 2 in
  the client, not in the port; heights and tiles are equal.
- Not ported: turned subgraphs (orientation ≠ 0), the outer buffer of a subgraph that is not the whole map, the pool's
  rows in a `.tgr` subgraph's special nodes, sub-area records beside a `.tgr` primary (§3), and the rest of
  `0x1420457f0` (oversize-feature / telepad-injector records).

## 3d. Tile graphs (`.tgr`): first stages (`TileGraph.cs`, `FortuneVoronoi.cs`, `VoronoiCells.cs`)

`0x1420452e0` sends `.tgr` subgraphs to `0x14203f890`, then `0x142116600` lays them out. Ported so far (The Twilight
Strand seed 1: the first 83 draws match the trace, including all 21 cell-area totals):

1. **Parse** (`0x142a157c0`; header `0x142bb2a10`): version, `Size:` (tiles), [`Resolution:`], `MasterFile:`,
   `Nodes:`, `Edges:`, [`EnvironmentOverride:`], then three quoted grounds (default, bottom, top). Node record
   (`0x142bb34c0`): x y (cells; versions below 0x13 scale them), edge count (≤ 4) and 0-based edge indexes, "tag",
   orientation word ("(any)" = 8), description count and descriptions, exist %, height, then priority and deletion
   (version > 0x15), lock orientation (> 0x17), random offset rectangle (> 0x18), x / y lock. Edge record
   (`0x142bb3f20`): node a, node b, internal point count and points, exist %, chirality (version ≥ 0xf), priority
   (> 0x15), "edge type .et", min width, max width (and a ground side below 0xf). The instance (`0x142126b40`,
   `0x142126c40`) keeps each node's edge slots in the order its record lists them (`0x142126550`): node +0x58..+0x70
   edges, +0x44 exist, +0x48 height, +0x4c position, +0x8b / +0x8c x / y lock; edge +0x20 / +0x28 nodes, +8 points,
   +0x30 exist, +0x38 type.
2. **Prepare** (`0x14203f890`): random offsets (`0x14203a600`, none in v19), a tag "a,b,c" keeps one alternative
   (`0x14203a480`, `Next(count)`), the **Labyrinth pass** (below), league description passes (not ported: league=
   nodes throw; the hideout nodes of `0x142035350` / `0x142034e60` are dropped without a discoverable hideout), tag
   "league" → exist 0. **Labyrinth pass** (`0x142032760` from `0x14203c900`, `0x142033e30` from `0x14203f890`,
   `LabyrinthPass`): only for a WorldAreas row with +0x171 (the 289 Labyrinth areas; +0x1bb marks the 24 boss areas;
   without a row, a graph path under `Metadata/Terrain/Labyrinth/Areas/`). Every node tagged "secret" or "secret" +
   one character (`0x14203a2e0`) is collected and gets exist 0. With a labyrinth node (instance +0x48, the server's
   daily layout) its LabyrinthNodeOverrides apply and `0x142033660` hands the node's secrets to the "secret1" /
   "secret2" nodes (not ported: the Gateway sends none). Without one: a boss area applies one LabyrinthNodeOverrides
   row (Id at +0, node tag +8, override tags +0x10, weights +0x20 — empty in every row): "Boss1_Sec1" (tag
   `boss_entrance1` → `boss_entrance1_1`), or for an id containing "_end" "FinalBossEasy" (difficulty 1: the id's
   first digit, 'E' = 4) / "FinalBoss" (`0x1420339a0` → `0x142033ad0`: a node with the row's tag takes the only
   override, none empties the tag; with several, `0x1424e5ee0` picks with a small RNG seeded from (difficulty + 4 ×
   the node's index in the labyrinth layout `0x1424e5ca0` builds) × 0x13d, no level draw; not ported, FinalBoss has
   4). Any other area whose id contains "_quad_door" or "_branch_door" gets the gold door: the collected nodes are
   shuffled when all are plain "secret" (`Next(count − i)` per place, swap with the front) or else stably sorted by
   tag (`0x1420393f0`); the first takes LabyrinthSecrets "GoldDoor" +8 (`gold_door`) as its tag, exist 100 and a
   "secret1" description (+0x6d, an "entrance<N>" description, is clear in that row); none collected: "Not enough
   secret nodes in …" is logged. The nodes still collected then lose their descriptions too; the exist rolls remove
   them, or blank one with two or more edges (§ exist rolls). **Vaal side areas** (`0x142032320`, `SideAreas`): nodes with a description "sidearea<N>" (the
   first such; N from '1', none = 1) are side area entrances, grouped by N (The Coast: `sidevalley` "sidearea3",
   `cave1` "sidearea2" / "sidearea1", each also "unique1"). When the area rolled its Vaal side area (generator +0xa,
   §3b) one survives: `Next(groups)`, `Next(group size)` (unused), the group shuffled (`0x141f4fb80`), its first node
   loses its "unique…" descriptions, gets exist 100 and "accessible", and +0xa is cleared (with no groups the
   generator adds the VaalSideArea feature row instead, not ported). Every other grouped node gets exist 0 and its
   descriptions cleared (`0x142037f50`). **Exist rolls** (`0x142031390`): every node, then every edge: a node with exist 1..99 rolls
   `Next(100) < exist` to stay (0 = always removed, 100 = no draw); an edge with exist ≠ 0 always draws `Next(100)`
   and stays below exist. Failed edges are removed (`0x141f4f130`), then failed nodes (`0x142036640`): a node between
   two edges of the same type whose far ends share its height is merged away (`0x1420371a0`: the first edge takes the
   second's far node; the node's position and the second edge's points become internal points of the first edge).
   Any other node with two or more edges and a tag stays as a plain node: tag and descriptions cleared, orientation
   any (8). Otherwise every edge at the node moves that end to its nearest internal point (a new node there,
   `0x141d94560`) or, without points, is removed, and the node goes. **Shuffle groups** (`0x142030790` with
   `0x14203a720`; the keyword is compared as inline UTF-16 constants, so no string search finds it): a node's first
   description starting "shuffle" puts it in group N (the number after it, none = 0; not a number: no group). Per
   group in order, while it holds two or more: `Next(size)` twice, both nodes leave it, and two different nodes swap
   tag (+0x20) and descriptions (+8); the orientation (+0x40) stays. **Unique groups** (next, `0x142031100` /
   `0x142035c70`): a node's first description starting "unique" reads `unique[letter][N][#count]` (none = group 0,
   count 1). Per group in order, keeping the largest count: with more nodes than the count, `Next(size)` picks one to
   keep, count times; the rest are removed as failed nodes (`0x142035f10`, a copy of `0x142036640`). With a letter,
   the letter's count picks which of its N groups stay (`Next(count)`, then `0x142037ea0`), shared across the level's
   records; not ported. These were the early `0x142030de5` differences of `1_4_2` and `2_8_5`. Nodes are then stably sorted by priority
   (`0x142003bc0`).
3. **Voronoi** (`0x141f230e0`): sites are the node positions then every edge's internal points. The diagram is
   Steven Fortune's sweep2 in 32-bit integers (`0x141f27200`; lines `dx·x − (−dy)·y = c` unnormalised, integer square
   root `0x141937840`, integer hash buckets that are almost always the first or last), clipped to [0, cell width] ×
   [0, cell height] (`0x141f26d70`; output prepended, so newest first). Vertices with one or three clipped segments
   become graph nodes; each segment is attached to the nearest nodes (`0x141f4f4b0` with `0x141f5bf30`: exact
   match, else smallest squared distance; rounding moves segment ends off their vertices) and appended to both
   sites' cell lists. The four corners are added; border nodes (at most two edges, x = 0 / ≥ width − 1, y = 0 /
   ≥ height − 1) are sorted along each border, corners take a site from their neighbour (`0x141f22e50`) and border
   pieces join consecutive border nodes, each appended to the cell on its inner side by a cross-product test
   (`0x141f227c0`). Site sets per node are ordered by x · 65536 + y.
4. **Jitter** (`0x141f22460`): for each node (list order) a random point of its cell (`0x141f21dd0`): the cell's
   edges fan around the node into triangles weighted by area / 2 (`0x141f21c00`; a node in the first column, first
   row, last column or last row whose cell has an edge along that border picks that edge alone, weight 1),
   `Next(total)`, then `Next(128)` twice (reflected if their sum is above 128) for a point in the triangle, scaled by
   1/160 (80 %). The node moves unless locked on that axis (most nodes with edges are locked; free nodes move); the
   draws happen either way. Then each edge's internal points the same way, in edge order.

## 3e. Tile graphs: special nodes and edge routing (`SpecialNodes.cs`, `EdgeRouter.cs`)

After the jitter `0x142116600` continues (The Twilight Strand seed 1: 2 145 draws match, the three rooms' 211 grid
entries equal the client's final grid):

1. **Edge types** (`.et`, parser `0x1429f78f0`, builders `0x141d7b3b0` / `0x141d7b570` / `0x141d7b6e0`; `EdgeType` in
   `TerrainCache.cs`): line 1 `Name [#colour]`, two grounds (path or `wildcard`; +0x20 / +0x28), `n0 size n2` (+0x4c,
   +0x4b = 1 or 3, flag 8), then either `virtual` (flag 4) with two `sub-edge chirality` lines (+0x38 / +0x40) and
   the default min / max width (+0x49 / +0x4a), or wiggle (+0x4d), `can_end` (0x10), `force_height_zero` (0x40) and a
   centre ground (+0x30). Flag 0x80: the name starts with "Wildcard" (left out of edge keys; such edges leave the
   graph after the special nodes).
2. **Lists** (`0x14211f100`, per node in graph order): tagged nodes are special; untagged ones go by degree and by
   whether an edge is virtual (+0x89): degree 0 with descriptions; degree 1 virtual and not within its edge's size of
   the border (`0x14211f2b0`), else plain; degree 2 of one edge type in no list; others virtual or plain.
3. **Edge key** (`0x14212b5f0` / `0x1421299f0`): each edge takes the side (0 up, 1 right, 2 down, 3 left) its
   direction to the next point points to (|dx| > |dy| → right / left); while a side holds two edges, the move of one
   edge to a neighbouring side holding none (then one) with the smallest angle `0x14299d730` is made (integer cosine
   through the acos table `0x142fe0b10`, side vectors `0x142fc00f8` × 10). The key's edges are the types per side.
4. **Candidates** (`0x14211f3c0`): for a tagged node, some tile or room of its tag must have the key (`0x14211ce90` /
   `0x14211d240`; a two-edge key may move its first edge to the free neighbouring side). Rooms come from the master's
   RoomSet index (+0x1f8): per tag, every room in list order in orientations 0..7 with its own key's edges turned
   (weight from the list, 1 by default); a variant whose four edges are the key's adds the room turned by the node's
   orientation (`0x14211fb20`; a symmetric room adds it twice). Any-orientation nodes (`0x14211fcc0`) and tile
   candidates are not ported.
5. **Pick** (`0x1421203f0`): `Next(total weight)` at `0x1421207cd`; the pick is centred on the node's tile (even sizes
   by the placement table), snapped to the alignment (`align<n>` description, else the largest edge size;
   `0x14202ecc0`), clamped to the graph, and must fit the map (`0x141d84e10`, entries free; `0x14211c820`, edge points
   lining up with placed neighbours); a pick that does not fit is dropped and the pick repeats. Then the position
   keeps room for each edge (`0x14211d6b0`) and the sides are fitted to the key (`0x142110020` / `0x14210fc70`).
   Tagged nodes first (one that fails is fatal), then the virtual list (its failures are exploded later). The map is
   the graph's size plus OuterBufferSize on every side.
6. **Rooms** (`0x142121610` → `0x142120fa0` → `0x141fea780`): the room is snapped again, then must find room
   (`0x142121380`, `FindRoomForTiles`): its footprint plus a margin per edge side (the align on the top and left, the
   edge type's size +0x4b on the right and bottom) inside the graph and free, and its corners suiting the map
   (`0x141ffba10`: no real edge on a side touching the map border; each corner ground equal to a written vertex's, or
   both null or flag 8, `0x141d85570`). Else a spiral (`0x142121210`: runs of 1, 1, 2, 2 … 9, 9 steps of the align,
   turning up, right, down, left through the orientation's table) moves the margin's corner until margin and corners
   fit (`0x142132b80`), then, from the room's position, the bare footprint (`0x142132a50`); "Overlapping node tiles"
   when neither does. The Coast seed 1 needs it: `stairs_high.arm`'s margin overlaps the `lgcliff_smcliff_2.arm`
   room tilised before it, and the room moves 3 tiles right and down. Then it is tilised column by column:
   'k' and 'f' slots ("f N" is a 1 × 1 key with tag N and no edge points, e.g. `forcedblank`); a plain slot remembers
   its ground for the fill, any other picks from the TileSet store (`Next(total)` at `0x141feadce`). Then internal
   points inside the room go (`0x14211d420`), each edge's end offset becomes its side's direction and its exit is the
   key's edge point on that side (`0x14211d830`; points at 3, 11, 19 of each tile side, `0x142fa09b8`; virtual edges
   keep two end points, `0x14211e540`), and the node moves to its first edge's exit while every other edge gets a new
   node there (`0x142126310`).
7. **Explode** (`0x142122d90`): the virtual list's failures and plain nodes with more than two edges: points within 46
   of the node go (`0x14211e860`), each edge gets a point one tile out on its side.
8. **Edge routing** (`0x142124600`, `0x142125790`): a cost grid of one cell per graph tile; every cell `Next(1024)`
   (`0x1421257cc`, which also clears the corridor counts), border cells + 1024; then every edge's straight corridors
   are marked (`0x142125eb0` → `0x142124920`: Bresenham; the cell's corridor count + 1 and the cells beside it, up to
   the corridor width away (size + 2; virtual: max width / 23 + 3), cost + (w − k + 1) · 2000 / w); cells under placed
   tiles are blocked. Each edge (`0x142124fc0`) takes away a segment's corridor and searches it from its end back to
   its start (`0x142124c70`: A* over four neighbours; a cell costs 100 + its cost, the client does not add the path so
   far; estimate 100 per Manhattan cell; open set ordered by g + h, then cell index; corridor and blocked cells are
   closed except the goal); the path's cells get the corridor, and every sixth cell becomes a new internal point
   (`Next(min(23, …))` twice, `0x1421255ce` / `0x1421255fb`). Edges that fail are retried until a round fixes none.

Before the rooms are tilised, `0x1421239d0` relaxes the graph (no draws; `Relax` in `SpecialNodes.cs`): every
placed room is a rectangle grown by its edges' room (size × 2 for a virtual edge, `0x142123000`); each node and then
each internal point is pushed away from every rectangle (`0x142123310`: inside, out through the side nearer the
room's node; outside, a push falling off over 138 positions from a side or corner), a node also toward the map
centre from its own room's four border probes (`0x142124210`); pushes are applied unless the axis is locked and
clamped to the map (`0x142124480`, `0x1421327c0`), and each room is centred on its node again where it still fits.
This is what moves the free nodes after the jitter.

## 3f. Tile graphs: corners, virtual edges, plain nodes (`EdgeShaping.cs`, `EdgeSplitting.cs`, `LinkMerge.cs`)

After the routing (The Twilight Strand seed 1: 2 180 draws match; the graph equals the client's at all eight stage
snapshots below; all 247 grid entries placed so far equal the client's final grid):

1. **Corners** (`0x142114f00`, no draws): a virtual edge with an end offset (it leaves a room) that leaves its node
   at more than 30° to the offset bends into an arc of a circle tangent to the offset, one point per 45°
   (`0x14212a340` at the first node, inserted in front; `0x14212ac40` at the second, appended reversed; up to five
   rounds while one bends). The sine helper `0x14299d820` (table `0x142fe0a10`, 127 = 1) interpolates with the two
   weights swapped (sin 0 reads 3/127). Then corner rounding (`0x14210aeb0` → `0x14210a240`, up to five rounds while
   something changes): from every node with one edge, then every node with more, each edge not yet done is walked from
   that end and on through nodes with two edges (visited sets of edges and nodes); a corner sharper than
   cos > −0.76 is cut by two points √((s1 − d02)/(d02 − s2)) · (max width + 10) along its segments, or moved to the
   middle / removed (`0x142109fb0`; a node next to the far node takes over the nearest point) when its segments are too
   short; points closer than 16 to each other are not split.
2. **Tile-less virtual nodes** (`0x14211e9b0`, per node of the virtual list that got no room): with three or more
   edges, points within the largest max width go (`0x14211e860`), then around the node each pair of neighbouring edges
   with a virtual one draws that edge's width (`Next(max − min + 1) + min`, `0x14211ece2` / `0x14211edf2`) for a virtual
   end point toward the other edge (`0x14211e100`) or, between two virtual edges, the corner point (`0x14211e1d0`).
   The node then splits into one node per edge, neighbouring ends with a virtual edge becoming logical links
   (`0x14211dbc0`, node +0x90 / +0x94 link indexes, the generator's list of 0x20-byte links).
3. **Split** (`0x142113c10`): virtual ends on the map border get their two end points along the border
   (`0x142111880`, `Next(width range)` at `0x142111948`); each chain of virtual edges is split from an end with end
   points (`0x142112d00`): the edge is copied (`0x141d942e0`), the original keeps one sub-edge type and the copy the
   other with chirality from the type's flags 1 / 2 (`0x142112600`; the symmetric-type case `0x141d7bcf0` is not
   ported), the end nodes move to the two end points (the original's at the end's first point at the start, at the
   second at the far end; new nodes at the others), the internal points move apart by `Next(width range)` either side
   of their middle path (`0x142111f60` at `0x1421121be`, `0x142111170` for the pair of offset points, clamped to the
   map), the chain's start draws its width at `0x142112e52`, and at inner joints `0x142111590` decides by segment
   crossings and distances which copy joins the new node.
4. **Link merge** (`0x1421148c0`): each link's live end nodes on one spot are joined (`0x142113ec0`, `0x142114aa0`), a
   link without such a pair reconnects its lone end to the nearer of the split pair (`0x142114270`); nodes with more
   than two edges join the plain list, emptied nodes are removed.
5. **Plain nodes** (`0x142122970`, after a priority sort and another corner rounding): every node of the plain list
   not on the border with one edge gets a room or tile (`0x1421203f0`; fatal if none). Any-orientation nodes keep a room
   variant (`0x14211fcc0`) or tile (`0x142120090`) only if its turned key passes the strict side check
   (`0x14210fc70` with heights: the corners' rise must equal the far node's height above this one), else the loose
   one; tiles come from the TileSet store's sizes per (tag, edges) (`0x141d811c0`) and its tiles of any tag per (edges,
   size) (`0x141d82280`, size swapped as a fallback), filtered by tag. After the pick, `0x142121910` turns an
   any-orientation placement a quarter either way when that lowers the side cost (`0x1421102d0`: Σ angle between each
   edge's side and direction, in degrees) without pointing an edge off a map border it touches. A tile is placed whole
   (height 0); a 1 × 1 tile for a node without edges or tag only on an empty entry. Border nodes with one edge move off
   the map (x or y −1, or the map size × 23) and their edge's end offset points back in. Then the relaxation (nodes with
   one edge left out) and the tilising with exits as for the special nodes.

Snapshots (`walkmaps.js` `hookGraphStages`, compared by `levelgen generate`): `relax` (0x1421239d0 in, also its
placement records as `relaxRecords`: node position, room position, size, slot sides, align), `special` (0x1421225d0 out),
`beforeRoute` / `route` (0x142125790), `corners` (0x142114f00), `virtualNode` (each 0x14211e9b0), `split`
(0x142113c10), `merge` (0x1421148c0).

## 3g. Tile graphs: edge tiling to the finished grid (`EdgeTiling.cs` … `OuterBuffer.cs`)

After the plain nodes, slot compaction (`0x142114ea0`) and another corner rounding, the rest of `0x142116600` and
`0x142044f30` place every remaining tile. **The Twilight Strand seed 1 now generates on the server tile for tile:
all 3 276 entries, tile hash 266153788, every one of the 3 772 draws up to the finished grid** (the next client draws
are the doodads, `0x142042fae`).

1. **Edge tiling** (`EdgeTiling.cs`, `0x14210de80`): a grid of one cell per graph tile (cost, blocked under placed
   tiles, `0x141f247e0`); `Prepare` (`0x14210db90`) adds end offsets as points and raises the cost along every edge
   (+80 per block crossed, `0x141f25c90`); edges with a placed end go first. `Distribute`: `Next(list1.Count)` (unused),
   a forward shuffle with `Uniform`, a stable sort by edge priority and a stable partition (split edges first); each
   chain of two-edge nodes becomes a path (`0x14210b950`), its cells found by a best-first search from the later
   points (`0x14210c600` → `0x141f24d60`: `Next(0x4000) + 20000` twice as weights A, B; step g = (block cost +
   ((x+1)A + (y+1)B) mod 707 / 101 + size) × 100 + g; backtracking deques), then one tile per cell (`0x14210f470`): the
   key has the type on the sides to the previous and next cell (`0x14210ee30`), points from a placed neighbour
   (`0x141ffbf40`, reading its opposite side) or the path's Bézier curve (`0x14210e9f0` / `0x14210f0f0`), or 3s/2 for
   types with flag 8; corner grounds from the type (`0x14210f2b0` / `0x14210f1b0`); `Next(total)` among untagged
   tiles. Tiles of an edge type listed in the FillTiles object (master +0x218, +0x140) are recorded for step 3.
2. **Lonely nodes** (no edges, with descriptions): a room or tile as for the special nodes, tilised when one fits.
3. **Vertex grounds** (`TileGrid` corners, `GroundStages.cs`). The map keeps (w + 1) × (h + 1) vertices (+0x50,
   3 bytes: ground index into the map's ground list +0x68, height, flags 1 = ground written, 2 = height written, 4 =
   locked, 8 = outside). Placing a tile (`0x141d833e0`) writes its `.tdt` vertex grounds (`0x141d85640`: with map
   +0x10 on, or the vertex locked, a different ground goes in only null/flag-8 over null/flag-8 or between grounds
   without flags 1 and 4, else "Mismatching ground types") and, where the ground is null or flag 1, the height.
   `0x142116600` then locks written vertices, floods (`0x142110a20` → `0x142110630`) every flag-2 ground from each
   tile's border breadth first over unwritten vertices, never across a tile edge (`0x1420281e0`: `0x142027df0` vertex
   inside a tile, `0x142027bd0` an edge point on that entry's side), sets the height levels (below), paints the
   levels and gives every still-unwritten vertex the graph's third ground (+0xb0, `0x142110d60`), fills the open
   sides of the step-1 edge tiles (`0x141feed20`, none on the Strand) and unlocks. `.tdt` vertex grounds sit after the
   packed sub-tile data (`0x1429f59a0`, not ported); the port finds the block by a validated scan from the file's end
   (`TerrainCache.VertexBlock`; `levelgen vertices` checks a tile set). Whether a tile has vertex grounds and heights
   is the u16 after its key (`0x1429f31a0`, file offset 58 after the string table for a tile without a base tile: bit
   2 grounds, bit 1 heights); guessing it from the bytes dropped heights that start with zeros. The block is
   (w + 1) × (h + 1) u32 ground strings (bit 2), then as many i8 heights (bit 1), then two u32 strings and a byte. A
   tile can have heights without grounds (e.g. `dunes_concave_v01_06.tdt`, flags 0x4c5: a ramp whose key heights are
   1, 1, 0, 1); `TerrainCache.HeightBlock` finds that block by the same kind of scan (the four corners equal the
   key's corner heights, two string offsets after). Read as all-zero heights, such a tile left the Northern Forest's
   holes between a level-1 wall and the dune with two candidate heights ("Mismatched heights").

   **Height levels** (`HeightLevels.cs`, `0x142118470`; state `0x1421173d0`: bit sets of done vertices and entries).
   Row by row, every tile origin not yet done starts a breadth-first walk (`0x142118100`, a deque). Each tile's
   candidate heights (`0x1421176c0`): an empty entry takes the heights of its done vertices whose map ground is null
   or flag 1 (`0x142117e10`), several of them being fatal ("Mismatched heights found during height fix-ups"); a tile
   takes, for each done border vertex where its own (turned) ground is null or flag 1, the vertex height less its own
   vertex height (`0x142117fb0`; `0x141d7ff50` / `0x141d80030` read a turned tile's vertex through the inverse code
   table 0x142fb7f98); with none, for each real edge whose entry across the edge point is done, that tile's offset plus
   the step between the two tiles' vertex heights at the edge point; with none, its done corners; several keep the
   lowest (`0x142c65290`, signed min). The tile takes the height (`0x1421174f0` → `0x141d82d00`: every entry's offset,
   every vertex = offset + the tile's vertex height; an empty entry is 1 × 1) and is done, then queues the tiles it
   is open to (`0x14211b4a0`, a set ordered by x · 65536 + y: an empty entry's four neighbours; a tile's neighbours
   beside each side cell whose two side vertices have an open ground on the tile, and the entry across each real
   edge). With any height in the view (+0x20), every vertex and tile is lowered by the lowest tile offset, then
   vertices not done take a lone done neighbour's height until nothing changes (`0x142118790` / `0x142118d50`);
   `0x142118900` gives each room the height of one of its tiles (room +0x18, not ported, nothing reads it yet).
   `0x142110d60` then paints, where no ground is written, the graph's first ground (+0xa0) on the highest level and
   its second (+0xa8) on the lowest (`0x142110f70`; the levels are master +0x146 / +0x144 when set, else the view's
   highest / lowest vertex height, `0x141ffc700` / `0x141ffc7d0`). Heights are counted in levels (storeys) until
   `0x14204cbd0` multiplies them by the master's StoreyHeight after the layout (step 8). The `paint` trace confirms the graph's ground lines: +0xa0 = line 1 (top), +0xa8 = line 3
   (bottom), +0xb0 = line 2 (default). Just before the overlays `0x142116600` stores the two levels (+0x90 / +0x94);
   from then on the fill object's tile listener `0x141fef240` runs after every tile that room tilising
   (`0x141fea780`) and the fill (`0x141feb970`, `0x141fe89e0`, `0x141fed640`) place: with heights, a highest level
   other than 0 and a top or bottom ground, each vertex of the tile with no ground or a flag-8 one takes the top
   ground at or above the highest level, else the bottom ground at or below the lowest
   (`GroundStages.LevelPainter`).

   **Room vertex grounds.** An `.arm` file ends with a line of (w − 1) × (h − 1) dictionary indices (`0x142a0f3a0`
   reads numbers to the end of the file; 0 = none): the grounds of the room's inner vertices, row by row (room data
   +0xe8; `RoomDef.VertexGrounds`). Room tilising ends with `0x141ffbd70`: each inner vertex with a ground takes it
   unchecked (`0x141d85840`), turned like a tile's vertex. The Coast's camp writes `sandscorched` this way, the town
   entrance `stream_edge`, the area transition room its `NextLevel` column.
4. **Overlays** (`Overlays.cs`, `0x141ffe240` / `0x141fff1a0` pass 1, pass 2 after the fill). The master's `.toy`
   (`0x141ffca30`): a pass line (up to three "0"/"1" for the passes, `SizeOrder` / `FileOrder` / `Random`, ±flags of
   `OverlayTileOverlays OverlayFeatureTiles OverlayFeatureRooms OverlayFeatureRoomOverlap OverlayDescriptionNodes
   OverlayExtraFeatures MatchAllTileKeys`, default 0x18), then "weight path [args]" rooms (`limit=`, `required=`,
   orientation names, `!condition`). Each room in each allowed orientation adds its key to the group with that key and
   flags; the same room again only ORs in the orientation bit. An exclusion grid marks rooms of nodes with
   descriptions (0x10), rooms (4 shrunk by RoomOverlap, 8), tagged tiles (2), extra features (0x20) and placed
   overlays (1); a cell takes an overlay only if the group's flags have every bit set there. Per group (FileOrder),
   every view position (every third when a key edge has size 3) where it fits (`0x141ffef60`: no excluded bits, the
   area holds tiles lying inside it (`0x141d850b0`); `0x141ffa7b0`: the four corner grounds match (`0x141ffa470`), the
   heights agree, each side inside the map matches the edges across it (`0x141ffa390` / `0x141ff9cb0` /
   `0x141ff9ee0`)) is a spot of the group's weight. `0x141fffb70` draws spots (`Next(total)`), a group that still fits
   (`Next`), a room under its limit padded to 100 with nothing (`Next`; nothing closes the spot), its orientation
   (`Next(840)`), removes the tiles and vertex grounds there and tilises the room (`0x141fea780`).
5. **Fill** (`Fill.cs`, `0x141fef070`): map +0x10 off; every empty entry, row by row, gets a fill for the ground of its
   top-left vertex (`0x141feb970`; FillTiles per ground name, none → "default"): tiles only → `Next(total)` and the
   orientation (`0x141fecd00`: fixed; square `Next(8)` where it fits; else `Next(840)` over the fitting codes), a failed
   choice leaving the list; with rooms, tiles and rooms together (`0x141fed100`, rooms `0x141fecf80` / `0x141fece90`).
   A fill room (`0x141fe89e0`) tilises column by column: an empty fill slot ('s' slots and plain 'k' slots) takes a
   fill tile of its ground that fits the room's fill slots (`0x141fea4d0`, `0x141fe8660`), turned by the room; other
   slots take a TileSet tile (up to five draws until one fits); then map +0x10 goes on, the room's own inner vertex
   grounds go in (`0x141ffbd70`, forced, turned with the room; e.g. Act 8 `farm_fill_04.arm`'s `trims2.gt` ring around
   its mound tiles) and the room's groundless vertices take the fill ground. The doodad stage reads these grounds
   (§6c: a flag-4 ground with no `.ddt` group still draws `Next(1024)` and `Next(default total)`).
   Old `.arm` versions (≤ 18) have 23 numbers per key (no "extra").
5b. **Storeys** (`Storeys.cs`, `0x1421193d0`, after the second overlay pass, on the height levels' view and state):
   the plain tiles take their heights from the tiles around them. A tile is plain for ground g (`0x142119260`) with
   no tag, no real edge (none or wildcard, edge type +0x48 bit 7) and g on all four key corners (a ground with flag
   0x10 counting as none); `0x142119060` takes g from the first corner. Every cell of a tile that is not plain is
   marked done. `0x142119600`: row by row, stepping by the width of the tile at each cell, a plain tile whose ground g
   is not none or flag 1 collects (a set) the heights its non-plain neighbours offer: the tile left of the cell (its
   turned key's top-right / bottom-right corners), right of the tile (top-left / bottom-left), above the cell
   (bottom-left / bottom-right) and below the tile (top-left / top-right); a corner counts when its ground is g and it
   lies along this tile's side, giving the neighbour's offset plus the corner's height. One distinct height: the tile
   takes it (`0x1421174f0` → `0x141d82d00`) and is done. `0x14211aca0` then sweeps until nothing changes: a plain tile
   not done takes the one distinct offset of its done neighbours (left of the cell, right of the tile, above, below)
   that are plain for its ground. No draws (The Coast seed 1: 252 entries).
6. **Mirror seams** (`Seams.cs`, `0x142025440` on the view): as for the towns (§3c), tiles touching a vertex that
   took a room's own vertex ground (room data +0xe8, `0x142025fb0`) included; when no replacement exists and the tile is outside rooms, the
   edge-point fallback `0x142026bc0` runs: per other side with a non-virtual edge whose neighbour has the same size and
   no tag, every other edge point gives the tiles of the changed key making no seam up or left; `Next(total)` picks
   one, which stands when the neighbour with the meeting point has tiles making no seam at all (`Next(total)`); both
   are replaced (`0x1420264b0`, which gives vertices the new tile leaves groundless the most common ground nearby).
7b. **Post-unique tiles** (`PostUniqueTiles.cs`, `0x14203ece0`, after the seams): each `postunique` tile of the
   TileSet (master +0x228) replaces one placed tile. Row by row over the view, every tile origin without a tag (tile
   data +0x10 is the length of the tag string at +0, `0x141d7d260`; not a mesh), with a corner vertex in the map and
   walkable, whose turned key equals the tile's key under an allowed orientation is a candidate; its orientation is
   drawn among the matching ones (`0x14203f560`, `Next(count)`, usually `n(1)`). `Next(candidates)` picks the place;
   the tile goes in at the old tile's height; none is fatal ("Failed to place post-unique tile"). The Coast:
   `beach_karuistone1.tdt` over one of 114 `Blank.tdt`. The walkability pass's "tile without a tag" test
   (`0x142028510`) reads the same field.
7. **Outer buffer** (`OuterBuffer.cs`, `0x142044f30` → `0x14201ffa0`; `0x14201db80` only with OuterGroundType): the
   border vertices' grounds and heights spread outward (vertical, then horizontal; each spread also sets vertex flag 8,
   `0x141ffbd30`, on its source vertex and on every buffer vertex on that side, written ones included, so the whole
   buffer is unwalkable for the walkability pass after it); then passes top, bottom, left and
   right walk the border: where a tile edge crosses outward (`0x14201d890`) an edge tile with the point continued is
   drawn (`Next(total)`) and repeated to the map edge; elsewhere, where the step's two border vertices agree
   (`0x141d77670`), the ground's first fill tile (`0x141feffb0`) is repeated outward at orientation 0. Fill tiles go
   in at the height of the step's first border vertex; an edge tile at the lower of its two border vertices, its key
   carrying each corner's height above that (`0x141ffb870`, not forced).
8. **Storey height** (`0x14204cbd0`, after the layout `0x142049d10`): row by row, every tile origin with a height
   offset has it multiplied by the master's StoreyHeight (+0x14a, a byte, default 0; The Coast 24), its entries
   taking the product and its vertices the product plus the tile's vertex height times the storey (`0x141d83060`).
   The first loop also scales the rooms' +0x18 heights (not ported). The tile hash reads these heights.

Also from the oracle: the extra-feature league rolls (`0x14204a310` → `0x141ea1f50` / `0x141ea1c80` →
`0x141ea2400`, each roll `0x142054a50`: `Next(100)` only for 0 < chance < 100; CoreLeagues +0x8a most per area,
+0x8e needs a chance, +0xa0 skipped; repeat chance for shrines etc.), three-edge key turning (`0x14211ce90`), the
store lookup's height fallback (`0x141d81f70`: corners whose ground lacks flag 1 drop their height), `.arm` keys
(`0x142a0e2a0`: heights relative to the lowest corner, which is the slot's height; the last number is "corner",
not the height) and symmetric virtual edge splits (`0x141d7bcf0`, `0x142112600`) are ported.

Not ported (they throw): overlay pass-2 placements, SizeOrder overlay passes, MatchAllTileKeys, overlay room
conditions, room heights (`0x142118900`), FillTiles edge lists,
OuterGroundType, the seams' skip of room +0xe8 vertices, outer buffers of `.dgr` layouts. The Twilight Strand seed 2 needs overlay
pass 2; seeds 3 and 7 stop with "Could not tilise edge", which the client answers by retrying with another seed
("Retrying with seed", §1) — not done by the port yet.

## 4. Files the generator reads (client parsers)

| File | Parser | Notes |
|---|---|---|
| .tgr / .dgr graph | `0x142bb2a10` (header: version, Size, Resolution, MasterFile, Nodes, Edges, EnvironmentOverride), `0x1429f8c30` (records) | `GraphFile` in `TerrainFiles.cs` is a heuristic reader; the port needs the client's exact fields |
| .tsi master | `0x142a06390` | keys RoomSet, TileSet, FileGroups, EdgeCombination, GroundCombination, FillTiles, OuterGroundType, OuterBufferSize, RoomUnitSize, … |
| .arm room | `0x142a0f3a0` (version ≥ 11) | dictionary, unit size, exits, spawns, doodads, decals, sectors |
| .et edge types | `0x142a0c0a0`, `0x1429f78f0` | default %, branching, voidness, split edges |
| .tdt tile | `0x1429f31a0` | size, hull planes, UniqueMesh; vertex grounds / heights after the packed sub-tiles |
| .toy overlays | `0x141ffca30` | passes and overlay rooms (§3g) |
| .gft fill tiles | FillTiles (master +0x218) | per ground name: tiles and rooms with weights |
| .fgp file groups | `0x14204c520`, `0x1421052e0` | random subgraphs ("graph" nodes) |

## 5. Not yet mapped in detail

The rest of the room/tile solver (`0x1420f7960` outer edge, `0x14202e6c0` chisel, `0x14201db80` fix heights, the
height stages). Doodads: §6c.

The busiest draw sites in the traces (§8): `0x142042fae`/`0x142042fbc` (doodads, ~2 per tile), `0x1421257cc`,
`0x14152e730`/`0x14152e73a` and `0x141530658` (outside the generator range: tile mesh variation), `0x141febf93` /
`0x141febfcf` (fill tile pick: `Next(1)` then `Next(8)` — orientation), `0x1420437eb`, `0x1420efc0c` (room tiling).
Draws after the tile grid is final (doodads, meshes) do not change the tile hash but do change the stream for
anything later, so the port follows the client's order to the end.

## 6. Tile hash (`TileHash.cs`)

`0x141d8a460`: for each grid entry, column by column (x outer, y inner), skipping empty entries and entries with
+0x34 / +0x35 set (the offset of the entry inside a multi-tile tile), one u32 = MurmurHash2 (seed 0xf36b12) of
`[i32 height (+0x30, i16), u32 rotation code (+0x36), x, y, MurmurHash2(.tdt path as UTF-16)]`; the hash is
MurmurHash2 (seed 0xf36b12) of that u32 array (`0x140df8570`; `0x1406db330` is the same over a wide string). Entry +8
→ the tile definition, whose +8 is the .tdt path (`std::wstring`).

Checked against four live traces (seed 1): Lioneye's Watch 1139181677, The Twilight Strand 266153788, The Coast
695393902, HideoutForest 3482012158 — all match (`levelgen hash`). The client log (`Client/logs/Client.txt`) has
"Generating level … with seed …" / "Tile hash" pairs for ~560 (area, seed) loads, an oracle for any seed.

## 6b. Walkability and projectile grids (`Walkability.cs`)

The grids the walk-map recorder captures ([03](../protocol/03-area-and-world.md) §7: `0x1410ddb90` asks the terrain
for them, vtable `0x14346ea40` +0x50 = `0x14165c260` → `0x141d852d0`) are stored nibbles of the tile map: walk at
map +0xd0, projectile at +0xe8, (23·w + 1) / 2 bytes a row (+0x100), built once by `0x141d85970` in step 5 of §1
(after the outside tiles). Every cell starts at 5; each tile entry then lays a patch over the grid keeping the lower
value, so the order does not matter.

- **Tile data** (`0x1429f31a0`, `TileWalk.Parse`; only tiles without a base tile): after the 58-byte key a u16 flags
  word (bit 0x100 from version 14, 0x200 from 13), from version 10 the orientation restriction byte and the hull plane
  count (0 … 4; with 2 or more one orientation mask per plane), with flag 0x200 one byte, then a u16 sub-tile count
  (w × h, the tile's own frame, row by row) and per sub-tile: u16 F | G << 8, a byte, three more with flag 0x200, a
  byte map unless F has bit 4 (`0x1429f59a0`: a header byte, bits 0-2 the packing — 0 one value, 1 … 4 LZ4 to 69 / 137
  / 281 / 529 bytes — bits 3-4 extra length bytes, bits 5-7 the length's low bits), and unless F has bits 1 and 2 a
  u16 length and the LZ4 hull (`0x140e42cd0` is LZ4_decompress_safe). The vertex blocks follow.
- **F / G**: 1 walking uniform, 2 projectiles uniform (no hull when both), 8 not walkable, 0x10 stops projectiles,
  0x20 / 0x40 / 0x80 the hull reaches 4 cells past the top / right / bottom; G 1 past the left. A hull is
  (23 + margins)² bytes per plane, low nibble walk, high nibble projectile. A tile's plane is the first whose mask is 0
  or has the entry's orientation code, else the last (tile data +0x172 / +0x173).
- **Per entry** (`0x141d85970`): the entry's sub-tile, u = its 4 vertices with flag 8 (outside the map counts,
  `0x141d883c0`). Without F 2: walk and projectile from the hull when F has no 1 and u < 4; otherwise a blocked walk
  patch when F has 8 or u = 4, and the projectile from the hull. With F 2: F 0x10 blocks both; else walk from the hull
  when F has no 1 and u < 4, or a blocked walk patch when F has 8 or u = 4. A hull cell (x, y), x and y from −4, goes
  to the slot's cell turned by the entry's code (`0x142fa04b0` with 23 × 23), clamped into the map.
- **Blocked patch** (`0x141d867d0`; an empty entry blocks both): 0 over the slot; toward each neighbour (up, right,
  down, left) that is a tile not blocked the same way (its F lacks 8, or 0x10) it reaches 4 cells further with 1 … 4
  away from the slot (`0x141d7c6d0` / `0x1429f53b0`: top and bottom rows set, left and right columns raised); cells
  outside the map are left out.
- **Vertex flag 8** comes from the walkability pass (`0x1420289a0`, §3g), which runs twice: in the tile-graph
  pipeline on the view, and in `0x142044f30` after the outer stages over each primary record's whole rectangle (the
  buffer included; records with CountAsPrimary too), followed by `0x142028870` (the map's border vertices). The
  second run is what makes a tile graph's outer buffer unwalkable. Then `0x142049420` flags the vertices under a
  room's `outside_tiles` sector (`0x142025190`; none in the areas checked, not ported).
- **Doodads** lower both grids around their collision as they are created (§6c).

Checked with `levelgen walk` against the seed-1 captures (`data/walkability/<area>_seed1.bin`, `.projectile.bin`;
`LEVELGEN_NODOODADS` leaves the doodads out, `LEVELGEN_CAPTURED_DOODADS` stamps the captured ones instead of the
generated): **all 61 captures whose tile hash matches match both grids cell for cell** with the generated
doodads (without doodads: none; median 99.44% walk, 99.89% projectile). The dungeon forced-blank slots (`1_5_2`
93.9%, `2_10_2` 93.2% before) were doodad collision, not a walkability rule. The last one, `2_8_10`'s 6 cells, was
the outer buffer's vertex flag 8 (§3g step 7): a buffer tile with a hull cell of 1 and only the two corners the pass
had flagged; with all four flagged it lays a blocked patch.

## 6c. Doodads (`DoodadStage.cs`, `DoodadHull.cs`)

`0x1420425d0` ("Generating doodads...", step +0x58 of §1) over the subgraph records (generator +0x30 → +0x10, 0x98
bytes; `GenerationResult.Records`: rectangle +0x40…+0x4c, master +0x50, placed rooms +0x58 at 0x38 bytes: +8 x, +0xc
y, +0x18 height, +0x1b orientation code). Order:

1. **Room doodads** of every record's rooms, in placing order (`0x142040770`). A `.tgr` record's rooms are the special
   nodes', overlays' and fill rooms (`special.Rooms`), a dungeon's the layout's placed rooms and then the rooms its
   fill picked (`0x1420f3de0` → `0x141feb970`: a `.gft` line naming an `.arm`, e.g. The Dried Lake's
   `Fills/plants*.arm` and `tent1.arm`, small rooms of forced-blank tiles with doodad lines; their place in the list is
   taken as after the layout's, which only matters for draws, and none was seen). Per room line (`.arm`
   reader `0x142a0f3a0`: `x y [has position, x y](v ≥ 34) angle [quaternion](v ≥ 17) flip [wake group](v > 24)
   has-fixed-height [height] [scale](v > 13) "file.ao" "type"`, then links and decals): `0x14202ede0` resolves the .ao
   with no random angle (no draw without AOSet); filters type +0x2ac ∈ {60, generator +0xd0}, a per-line orientation
   mask (+0x1e), vtable +0x98 (`0x14152d680`: type +0x2a8 ∈ {0, 1}); `0x14202f1c0` places it: cell =
   `Orientation.Map(code, x, y, w·23, h·23)` (the room's own size) + room origin·23 + the AOSet offset; angle = 2π − a
   for codes 4–7, + (code & 3)·π/2, wrapped, + the AOSet angle; scale = line × AOSet; mirrored = (code 4–7) ^ flip ^
   AOSet. Type checks `0x142030080` / `0x14202fe70` (angle halves), `0x14202f860` (inside a +0x1c bit-2 room),
   `0x14202f640` (type id of the MiscObjects.dat rows `DoodadNoOverlap`, `DoodadNoOverlap3`,
   `TileUpperDoodadNoOverlap` → `0x142041ac0`, not ported), `0x14202f530` (`DoodadLayerDoodad` → world vtable +0x60,
   not ported). **Which types are created** (`0x142041960`, vtable +0x98 = `0x14152d680`): an object type
   (`0x141ec1360`) takes from its BaseEvents +0x2a4 serialised (no `no_serialise`), +0x2a8 1 = `client_only`,
   2 = `server_only`, +0x2ac `league_category` (default 60; must be 60 or the generator's +0xd0). A line is created
   when its type is not serialised or client only, and not server only (spawners, markers, chests, transitions are
   not). Checked over all captures (49 000 placements): every such type is captured, `DoodadNoBlocking` included
   (all 5 062 of its room and tile lines in the 117 captures the stage reaches; it was left out before, from a count
   taken while the reskin and text `.tmd` names were still wrong). Types with a league category are left out.
   **Collision:** only a type whose Positioned has `static = true` (`Doodad`, `DoodadInvisible`, …) stamps its Hull;
   `HangingSlave`, `BellyGrassClusterTrigger`, the `FlowBlocking` objects are created without collision (their
   Hulls are absent from the captured grids).
2. Per record: **random doodads** of the master's `.ddt` (attempts = max density·w·h/1000) between a copy and a
   restore of the generator state (TLS +400), then **vertex doodads** ("All" groups, fixed lines) with normal draws.
   Not ported (not hit by any matching trace): the entity exclusion circles and the `Next(10000)` falloff (lines
   274–550: entities with components 0x407c7f95 / 0x9e3b9e73, stats 0x25a4f824 / 0xc9c0b154 / 0xb36d48d9), the
   `0x14154c2d0` check after walk ≥ 5, the tile no-doodad bitmask (tile +0x150).
3. The deferred list `0x142041d40` (groups shuffled by `Next(count)`, no two within 3 × 3): not ported.
4. After the stage, generator vtable +0x50 (`0x142041950`, world vtable +0x28) and +0x20 = **`0x142041200`, the
   tiles' own objects**: row by row, every tile origin (+0x34 / +0x35 zero) whose tile data has +0x178 or +0x180;
   the record is the last one holding the tile (`0x141d77a90`, else the first). `0x141d7eda0`: a tile without
   sub-tiles (+0x16d bit 1 clear) → `0x141d7f700`: with the record master's `Reskin` (master +0x80), the file
   `<dir>/alttiles/<reskin>/<reskin>_<name>.tmd` beside the tile (`0x141d88810`) when it exists, else +0x178. The
   copy is loaded under the key `<copy>;<middle of +0x178's key>;<reskin>` (`0x141d801b0` takes the part between the
   first and last `;`); its records are named `<reskin>_<name>`. The client's virtual `.ao` is the original
   `<.tdt folder>/<name>.ao` when the record starts with the Reskin exactly as the master writes it (case counts) and
   that `.ao` exists, else `<.tdt folder>/<reskin>_<name>.ao` (no such file, so no Hull): `2_9_5`'s foothills ramps and
   `2_9_10_2`'s boss platform take the original, `2_9_10_2`'s `rot_…_assHole02` / `rot_…_bellyfx` (no original
   `.ao`) and `2_10_6`'s `blood_…_cartblocker` (master `Reskin "Blood"`) keep the prefix. Read off the captures; the
   loader that does it was not found (`.tmd` is not a string in the decompile). The copy exists when
   `0x1429f8b30` opens it with 5 bytes or more. +0x178
   is the mesh's `.tmd` (`0x141d7fb60` at load: for each part of the tile's mesh string +0x20 — a `;` list for a tile
   made of sub-tiles, `0x141d7f990` — that part with its last four characters replaced by `.tmd` (`0x141d7fcb0`); the
   last one that exists; none when the flags word has bit 3). +0x180 is the `.tmd` the `.tdt` names (`0x141d7e060`:
   the first of the two strings after the vertex block, e.g. `LargeCliffsGoatSpawner/beach_v01_01.tmd`, the Prison's
   archways; 390 `.tdt` name one), loaded the same way (`0x141d7cc50`, so it also sets the transition flag +0x16f);
   its entries go to the same callback after +0x178's, with no Reskin. Each 0x58-byte entry goes
   through `0x1420415e0` = the room-line path with the tile's size (+0x60 / +0x61), the entry's origin and
   orientation code (+0x36) and height (+0x30); the `.ao` is the record's, else the virtual
   `<.tdt folder>/<record name>.ao`. **Tiles made of sub-tiles** (flags word 0x200, read only from `.tdt` version 13;
   0x100 only from 14, `0x1429f31a0`) run per slot of the +0x90 grid, row by row (`0x141d7eda0`). The slot entry
   (`0x141d7d860` from the `.tdt` record: u16 F | G << 8, a byte, then three bytes) has G at +0x31, the mesh index
   (first extra byte, ≤ 0x3e) at +0x36 bits 2–7, the placement code (second) at +0x37 bits 5–7 and a signed height
   step (third) at +0x37 bits 0–4. A slot whose G has bit 2, 4 (it continues the sub-tile on its left / above; for
   other tiles the reader sets these for every slot past the first column / row) or 8 is skipped; otherwise the mesh's
   `.tmd` (`0x141d7f430`, the Reskin copy first) is read and each entry is turned inside the sub-tile's span
   (`0x141d7ec10`: 1 + the slots on the right with G bit 2, 1 + those below with bit 4; swapped for odd codes;
   × 250): a = [w − x, x, h − y, y] taken by the code's table row (`0x142fb7f78`, the same bytes as `0x142fa04b0`),
   plus the slot's (u, v) × 250, then cells = floor(× 0.092f) (`0x1402c2390`); the angle is turned by the code
   (2π − a for 4–7, + (code & 3)·π/2, wrapped), codes 4–7 xor the mirror and map the orientation mask through
   `0x143646180`; height −= step × tile +0x177 × 7.8125. The entry then takes the callback like the others.
   Ported (`DoodadStage.SubTileDoodads`; heights and the mask are not carried) but not checked: the 19 such tiles
   (`levelgen subtiles`; 10 with objects: the `MapUberChambers` terrace overlaps, `MapNocturne`'s `fence_cv`, the
   Dreadnought hideout) are in no capture. Then `0x142041d40` again. These lines draw nothing without AOSet.

Each doodad is created by `0x142042360` (nothing outside the map) and added to the world, which stamps its `.ao`
`Hull` on both grids (§6b, `Walkability.Stamp`: walk grid walk shapes then projectile shapes, projectile grid
projectile shapes; triangles at 1/16 cell with a top-left rule, circles as dx² + dy² < round(r²), falloff ≤ 4). So
later walk checks (`walk < 5` in the random loop) see earlier doodads: this is what kept the Strand's draws apart
before.

Results: The Twilight Strand matches every doodad-stage draw (to #25 151, then the mesh variation `0x141530658`) and
places all 250 captured doodads (its torches and waterfalls are tile objects); its grids match cell for cell, as 60
other captures' (all 61 with a matching tile hash; 60 before the outer buffer's flag 8, 47 before the tile objects and the type rules, 53 before the
dungeon fill rooms; the fill rooms brought `1_4_2`, `1_5_1`, `2_7_5_map`, `2_10_5`, `2_10_6`: their missing
collision was also what made `2_7_5_map`'s first random loop run longer; AOSet then brought `1_1_6`, and the
reskinned names above `2_9_5`, whose `foothills_*` ramp planes and blocker had found no `.ao` and so no Hull).
Text `.tmd` records (version 8) carry the `.ao` as their first quoted field and z before the angle (08 §6); the
waypoint effect of `beach_waypoint_v01_01.tmd` is `waypoint_idle.ao` at angle 0. Every doodad of `1_1_3`, `1_1_6`
and `2_9_5` now has the captured name. The +0x180 lists brought the Prison's archways (`1_1_7_1` 314 →
336 of 336 captured doodads, `1_1_7_2` 761 → 795 of 795, `2_6_7_1` 281 → 298 of 298; grids were already exact, the
archways have no Hull). Text `.tmd` records place at their own cell fields, not world / (250/23): `2_10_6`'s cart
blockers are at world y 500 and cell 45, and the client puts them in 45; this fixed one-cell offsets in `1_1_2`,
`1_3_3_1`, `1_3_6`, `2_6_2`, `2_8_13`, `MapAtlasHarbingerLow` and the Labyrinth rooms. With `DoodadNoBlocking` and
the reskin rule above, **72 of the 117 captures the stage reaches place every captured doodad** (was 56), the 61
grids still exact. The `l(8)` draws at `0x142063b9c` the traces show inside the stage (10 traces, up to 452 in
`2_10_2`) are `0x142063a60`, an animated entity's random start time (`rand()` 8 bits × 1/256 × the animation's
length) as the doodad is created: they read only the LCG, which `0x142046a70` reseeds from the MT before each pick,
so they move nothing; `levelgen` leaves them out of the comparison, and `2_10_6` then matches every doodad-stage
draw. Left:
- Tiles made of sub-tiles: ported, no capture has one.

**AOSet** (`0x14202ede0`, every room, tile, random and vertex line resolves its `.ao` through it, room and tile lines
before their type filters, so a set draws even when its type is not created). The component (`0x141dbf380`, keys
`0x141dc0140` / `0x141dc0270`; 232 `.ao` files, all extending `Metadata/AOSetParent`) holds either fixed children
`fixed_ao = "x y z … file"` (+0x30) or weighted ones `ao = "weight file"` (+0x10, total +0x28; `null` places nothing),
never both; `random_rotation_min` / `_max` +0x48 / +0x4c (`random_rotation = true`: −π … π), `random_scale_min` /
`_max` +0x50 / +0x54 (1 when absent), `random_translate_radius` +0x58, orientation mask +0x5c (`random_flip` |= 0x11,
`random_orientation` = 0xff). The resolver loops: an `.ao` whose AOSet has no fixed children picks a child
(`Next(total)`, `0x141dd31d0`) and goes on with it; an `.ao` without AOSet, or with fixed children, is the doodad (a
fixed set is one entity with its own Hull; its children are attached, not placed). The last AOSet seen then gives:
the angle (`Next(512)` in the rotation range when min < 0 or max > 0, else a full random angle only for a line
without one), an orientation code (`Next(840)`, `0x14299da50`: the roll's set bit mod their count, 0xff → roll & 7;
codes 4–7 mirror the angle and set the mirror), the scale (`Next(512)` when min < 1 or max > 1), the offset (two
`Next(512)`, radius 0 in every file: the offset itself is not ported). Result: +0 `.ao`, +8 angle, +0xc scale, +0x10
mirror, +0x14 / +0x18 offset; room and tile lines add the angle, multiply the scale, xor the mirror and add the
offset (`0x14202f1c0`), random and vertex doodads (`0x142044180`) add their own angle draw and xor their mirror roll.
`1_1_6` (`lioneye_swords_hill_set.ao`) now matches both grids and `1_1_3` places its toxic lights, both fixed sets
(no draws). Weighted sets and the range draws are ported from the code but no matching trace has hit one yet.

## 7. What the server will use

From the generated grid: the walkability and projectile grids, heights, area transitions and waypoints (tile .tmd
records, as the walk-map recorder computes them today), and room points (rooms placed). Until the port reaches an
area, the Gateway keeps reading `data/walkability/<area>_seed<seed>.*` captures.

## 8. Tooling

- **Trace:** `live.ps1 -Walkmaps` also hooks the generator (`scripts/walkmaps/walkmaps.js`, `hookGeneration`) and
  writes `data/walkability/<area>_seed<seed>.gen.json`: every draw on the generating thread as
  `[kind, caller VA, argument, result]` (`n` = `Next`, `u` = `Uniform` called directly, `r` = raw draw from elsewhere,
  `l` = LCG bits) and every grid entry `[x, y, .tdt, height, rotation, +0x34, +0x35]`, plus the seeds and tile hash.
  Traces of 2026-10-22: town 496 draws (30 × 18 tiles), Twilight Strand 30 586 (78 × 42), The Coast 46 706 (75 × 75),
  HideoutForest 2 818 (33 × 33). Since the second Strand trace of 2026-10-22 a trace also holds `graphs`: the tile
  graph (nodes, edges with points, virtual end points, end offsets, widths) at each layout stage boundary (§3f).
- **Check:** `dotnet run --project scripts/re/levelgen -- hash <trace.gen.json>…` recomputes the tile hash from a
  traced grid. `… generate <area> <seed> [trace.gen.json]` runs the port and, with a trace, compares draw by draw
  (kind, argument, result; the first difference names the client's caller; the animation-start LCG draws of
  `0x142063b9c` are skipped, §6c) and entry by entry, also when the port
  stops at a part that is not ported, and graph by graph at each recorded stage. `… cat <game path>…` prints terrain files, `… tile <.tdt>…` a tile's tag and key, `… subtiles` every `.tdt` made of sub-tiles, `… tmds` where each `.tdt`'s objects come from (+0x178 / +0x180), `… ls <regex>` the game paths
  that match (e.g. every `.arm` of a folder, to find rooms no room set lists). `… doodads <area> <seed>` compares the
  placed doodads with `<area>_seed<seed>.doodads.json` (`LEVELGEN_ALL` prints every capture-only one with its tile). Traces also record the area level
  (world +0xd4).
- **Oracle:** `… oracle [area regex]` runs every (area, seed) the client logged with its tile hash
  (`Client/logs/Client.txt`, 558 pairs) and reports match / mismatch / where the port stops, with a summary.
  2026-10-23 (session 3s: shrine rooms, sub-area records beside the primary, shuffle and unique groups, the empty
  extra door type, template nodes): 104 of 710 match (was 90; new: `1_1_9a` seed 3101637759, `1_4_2` seeds 1,
  1017325810, `1_4_6_1` seed 1, `1_4_6_2` seed 1, `1_4_6_3` seeds 1, 778578247, 2425308814, `1_5_5` seeds
  973365745, 2084366798, 2637803820, `2_7_8`, `2_8_10`, `2_9_10_1` seed 1); of the seed-1 traces 61 match by hash
  (was 54), none lost, no trace matches fewer draws. `2_8_5` seed 1 (hideout rolled) matches every draw up to its
  stop (3 373, door tiles without RoomOverlap) and `1_3_18_1` seed 1 every draw up to its stop (2 453, numbered
  special rooms). The 48 shrine seeds generate. Most common stops: 350 run to the end with another hash, node kinds
  (73: the Labyrinth's `secret` rooms 62, `labyrinth_final_boss` 11), numbered special rooms (35), door tiles
  without RoomOverlap (29), DungeonFillRoomHeightOverride (17), "Mismatching ground types" (11), breaches.
  2026-10-23 (session 3r: `.tgr` subgraphs on a shared map, the extra room pool and mission rooms): 90 of 710 match
  (was 88; new: MapWorldsGorge seed 1, MapWorldsToxicSewer seed 1); of the seed-1 traces 54 match by hash (was 52),
  none lost, no trace matches fewer draws. The 118 glacier subgraph stops are gone: Gorge's other seeds run to the
  end (100 with another hash, which need traces), 17 stop at shrine rooms (`0x142103490`), 4 at a breach. Most
  common stops: 317 run to the end with another hash, node kinds (115), shrine rooms (48, which before ran to a wrong
  hash), numbered special rooms (25), door tiles without RoomOverlap (20), DungeonFillRoomHeightOverride (12).
  2026-10-23 (session 3q: the dungeon seams run `Seams.cs` on the layout's view with the edge-point fallback): 88 of
  710 match (was 84); of the seed-1 traces 52 match by hash (was 48; new: `2_10_2`, `2_10_5`, `2_10_6`,
  `2_9_10_2`), none lost, no trace matches fewer draws.
  2026-10-23 (session 3p: plain room slots before the shared-slot test, room grounds `0x1420f0e10`, seam tests
  `0x14201d800` / `0x142025fb0`, the shared slots' accessible bit, `s`-keyed `.arm` rooms, the layout fill's step,
  post-unique tiles on dungeons): 84 of 710 match; of the seed-1 traces 48 match by hash (was 38; new: `1_1_3`,
  `1_5_2`, `1_5_5`, `1_5_7`, `2_10_4`, `2_10_7`, `2_10_8`, `2_7_5_map`, `2_8_2_2`, `2_9_6`), none lost, no trace
  matches fewer draws. Most common stops: 250 run to the end with another hash, node kinds (115), glacier `.tgr`
  subgraphs (118), numbered special rooms (25), door tiles without RoomOverlap (23), DungeonFillRoomHeightOverride
  (12), "Mismatching ground types" (10).
  2026-10-23 (session 3o: node door type, the untagged node's door type, exitless rooms out of the size tables, the
  overlap fallbacks): 63 of 710 match; of the seed-1 traces 38 match by hash (was 31; new: `1_4_3_2`, The Slave Pens
  `1_5_1`, `1_5_6`, `2_6_7_2`, `2_7_9`, `2_7_10`, `2_10_3`), none lost, no trace matches fewer draws. Most common
  stops now: 258 run to the end with another hash, node kinds (115), glacier `.tgr` subgraphs (118), numbered special
  rooms (25), door tiles without RoomOverlap (23), DungeonFillRoomHeightOverride (12), "Mismatching ground types"
  (10), "Cannot combine tile keys of different sizes" (10).
  2026-10-23 (seed-1 traces of 186 areas, `data/walkability/<area>_seed1.gen.json`): 51 of 710 match (new, all
  seed 1: `1_1_4_0`, `1_2_5_2`, `1_4_3_1`, `1_4_7`, `1_5_4b`, `2_6_2`, `2_6_7_1`, `2_6_8`, `2_6_15`, `2_7_4`,
  `2_9_2`, `2_9_5`, `2_9_7`, ChayulaLeague, MapWorldsBoneCrypt); 238 run to the end with another hash; the most
  common stops are unported node kinds (115) and glacier tile-graph subgraphs (118).
  2026-10-23 (The Fetid Pool): 35 of 561 match (new: `1_1_3a` seed 1).
  2026-10-23 (composite maps): 34 of 561 match (new: `1_1_7_2` seeds 1, 2512127042; `1_3_3_1` seed 1055960378;
  `1_5_4` seed 1). Before: 30 of 560 (new then: the Prison `1_1_7_1` seeds 1, 1898277627, 4162278830; `1_3_3_1` seeds 1,
  2000385416; `1_4_3_1` seed 1109898802; `1_2_10` seed 615157463). 2026-10-22: 23 of 559 (The Ship Graveyard Cave `1_1_9a` seed 1; Lioneye's Watch; The Twilight Strand seeds 1, 965262086, 2073986762, 3200638083; The
  Coast seeds 1, 370918376, 1029867915; The Tidal Island seed 1; The Mud Flats seed 222312849; `1_1_6` seed 1;
  The Northern Forest (`1_2_8`) seeds 1, 602433223; The City of Sarn (`1_3_1`) seeds 1, 149738592; `1_4_7` seed
  2326754436; `2_6_town`; four ChayulaLeague seeds; MapAtlasHarbingerLow seed 1); the most common stops are
  multi-node `.dgr` dungeons and "Mismatching ground types" (7); 57 run to the end with another hash; The Boiling
  Lake (`2_9_6`) stopped at its fill room `Rooms/Fills/pools_00.arm`, a 1 × 1 room keyed `s` the port could not read
  then (it matches since 2026-10-23). The Northern Forest seed
  1 matches every draw up to the doodads (10 562), every vertex and tile, and the tile hash (2500166236). The Coast seed 1 matches
  every tile-stage draw (8 178, up to the doodads), every vertex at the painting and as the fill starts (`levelgen`
  compares the trace's `paint.after` and `fillVertices`), every tile and the tile hash.
  Some seeds fail in the client itself ("Terrain generation failed" in `Client.txt`, then an exception dialog). Seen
  2026-10-23, all seed 1:
  - The Crossroads (`1_2_3`, macro_crossroads.tgr): "Mismatching ground types at (44, 57): Cannot place
    ForestPoolFlowingWater over (wildcard) … forestpool_flowingwater_v01_02.tdt @ (43, 56) (R180)". The port stops
    earlier (fill tiles with edge types).
  - The Southern Forest, Act 6 (`2_6_12`, southern_forest_01.tgr): "Failed to tilise edge: Could not find matching
    tile at (22, 57) for edge type ForestRiverBank".
  - The Karui Fortress (`2_6_5`, karui_fortress_01.dgr): "Dungeon generation failed: Failed to connect edge (58,
    58)(35, 12) of type Metadata/Terrain/Act6/Area5/Rooms/swamp_deep_pool_cliff_rooms.et".
  - The Crossroads, Act 7 (`2_7_2`, crossroads.tgr): "Could not permute edge ForestSmallCliff: (758, 931)-(782, 977)".

  The official server must give the client only seeds that generate; ours pins 1, so such an area cannot be entered
  (or traced) until the server rerolls failing seeds.
  `Game_SEED=<n>` (Auth) pins non-town seeds for traces of one seed; `LEVELGEN_AROUND=<n>` prints the draws around the first
  difference (also in `levelgen doodads`; `LEVELGEN_HULL_RAW` with `LEVELGEN_HULL` in `levelgen walk` prints the
  sub-tile's whole plane as stored, border included; a composite map's layouts are compared one by one: the trace's `dungeons`, every layout of the load,
  and `subgraphs`, each record as 0x1420452e0 starts it); `LEVELGEN_DUMP=<file>` writes the port's grid as the trace's `tiles` (to line it up with the client's,
  e.g. to find where rooms land).
- **Probe:** candidate lists can be read live by hooking `0x141d902d0` (list: begin, end of 0x18-byte entries: tile
  handle (+8 → path), u8 orientation, i32 weight; +0x18 total weight) and `0x142005ee0` (fill lists, same layout)
  on the generating thread.
