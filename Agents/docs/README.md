# Documentation index

Protocol reference and reverse-engineering notes for the Path of Exile 3.29.3b client. What works now is in
[../Status.md](../Status.md), what to build next in [../Roadmap.md](../Roadmap.md). Start with
**[protocol/00-overview.md](protocol/00-overview.md)**.

## Protocol (consolidated, current)
| Doc | Content |
|---|---|
| [protocol/00-overview.md](protocol/00-overview.md) | processes, ports, the full message sequence from launch to the HUD, conventions |
| [protocol/01-login.md](protocol/01-login.md) | login connection: key blobs, handshake, framing and encodings, messages, our login server |
| [protocol/02-instance-connection.md](protocol/02-instance-connection.md) | instance client, connect path, opening, 0x11 encryption, pump, handler groups, dispatcher, tick |
| [protocol/03-area-and-world.md](protocol/03-area-and-world.md) | 0x1d, 0x7d, 0x22, world queueing and activation, 0x21, spatial index |
| [protocol/04-player-entity.md](protocol/04-player-entity.md) | object type register (MurmurHash2), 0x2b9, the component blob and every component |
| [protocol/05-inventories-and-ui.md](protocol/05-inventories-and-ui.md) | inventory manager, 0xc0, Inventories.dat, 0x3d Atlas tree presets |
| [protocol/07-latency-visibility-movement.md](protocol/07-latency-visibility-movement.md) | tick/ack and HNC, wake zone (visibility), movement |
| [protocol/08-area-transitions.md](protocol/08-area-transitions.md) | area transitions: the object and blob, tile objects (model, rotation, height), the atlas, every area's transitions in the Gateway |
| [protocol/09-area-change.md](protocol/09-area-change.md) | changing area through an exit, instance details (0x27), the instance chooser |
| [protocol/10-chat-and-commands.md](protocol/10-chat-and-commands.md) | chat input (0x15), server replies, stats update (0x1b3), dev commands |
| [protocol/11-skills.md](protocol/11-skills.md) | skills: ActiveSkillManager, basic actions from the .ot, the Actor skill list, the default attack, stat keys, gem casts, buffs (0x1c1/0x1c2), Righteous Fire |
| [protocol/13-items.md](protocol/13-items.md) | items: types and the item data, inventories with items (0xc0, 0xbf), moving, sockets, the ground, equipment on the model, requirements, gem skills |
| [protocol/14-monster-packs.md](protocol/14-monster-packs.md) | monsters in areas: MonsterPacks, rarity, room points (the camp's "mapboss"), the doodad hook and room fitting |
| [protocol/15-progression.md](protocol/15-progression.md) | experience and levels from kills, the Passive Skill Tree (0x33, 0x34), loot |
| [protocol/16-waypoints.md](protocol/16-waypoints.md) | waypoints: the object, activated waypoints (0x4c), the World panel (0x7b), travel (0x4d), quest flags in the Player component, what reveals pins |
| [protocol/17-npcs.md](protocol/17-npcs.md) | NPCs: the object, town placements, talking (0x24 → 0x84), dialogue closed (0x85) |
| [protocol/21-hideouts.md](protocol/21-hideouts.md) | hideouts: unlock flags and league quest flags, the Hideouts keys, `.hideout` decorations, /hideout (0x16 command 45) and the Hideout button (0x17a), HideoutDoodad data, the map device and its window (0x7b kind 5) |
| [protocol/18-stash.md](protocol/18-stash.md) | the stash: the object, tab lists (0x21, 0x136), opening (0x7b kind 1), tab contents (0xc7 → 0xc0 + 0xc8), the account's stash |
| [protocol/19-gems.md](protocol/19-gems.md) | gems: the SkillGem item data, levels from experience, the game data for levels, variants, Vaal gems, requirements, quality and supports |
| [protocol/20-damage.md](protocol/20-damage.md) | damage packets: types and conversion order, gain as extra, per-type mitigation, damage over time and ailments |
| [protocol/22-pause.md](protocol/22-pause.md) | pause from the Escape menu: C→S 0x1d4, S→C 0x1d3 (world +0xb9c/+0xb9d), what the Gateway freezes |
| [protocol/23-microtransactions.md](protocol/23-microtransactions.md) | microtransactions: the owned lists and their loaded flags (C→S 0x1f2, S→C 0x1f4/0x1f5), the Cosmetics tab (C→S 0x1f9, the worn table in S→C 0x1f3), cosmetics on the model and in the stats (ItemisedVisualEffect) |
| [protocol/12-quests.md](protocol/12-quests.md) | quests (research): the data tables, a1q4 "Breaking Some Eggs" and the Submerged Passage blocker |
| [protocol/06-debugging-log.md](protocol/06-debugging-log.md) | every live run, crash and fix, and lessons |
| [protocol/address-index.md](protocol/address-index.md) | all client addresses by subsystem |

## Generated references
| Doc | Content |
|---|---|
| [protocol/schema.md](protocol/schema.md) ([json](protocol/schema.json)) | read sequence of every component deserializer and of 333 instance/world message classes |
| [re/components.md](re/components.md) ([json](re/components.json)) | every component class the client registers: pool, factory, vtables |
| [re/object-types.md](re/object-types.md) | component list of every object type (`Metadata/...`), from the `.ot` files |

How to regenerate them: [re/tooling.md](re/tooling.md) §7.

## How-to
| Doc | Content |
|---|---|
| [../GettingStarted.md](../GettingStarted.md) | setup from a fresh clone, third-party tools, troubleshooting |
| [re/tooling.md](re/tooling.md) | Ghidra headless scripts, Python helpers, GGPK dump tool, Frida through REA, recipes |
| [re/011-level-generation.md](re/011-level-generation.md) | the client's level generator: stages, RNG, topology pick, tile hash, generation traces; the server port in progress |
| [dev/live-testing.md](dev/live-testing.md) | the older live-testing notes (GettingStarted supersedes them) |
| [dev-server.md](dev-server.md) | the JSON-lines dev server, `GameServer.Auth` and `GameServer.Patch` usage |
| [saves.md](saves.md) | how player progress is saved: the SQLite save database, transactions, what a failed save does |
| [game/maps.md](game/maps.md) | 3.29 maps and the Atlas: the 20 generic map bases, tiers, drops, Voidstones (rules to build to) |

## Working notes (history)
- `re/007`–`010`: the notes these documents were consolidated from (login TCP, handshake, message tables, live instance
  notes). `re/009` still has the most complete login message tables.
- `re/003`–`004`: early notes with known errors (for example "login is HTTPS"). Verify before use.
