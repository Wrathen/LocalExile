# Address index (3.29.3b `Game.exe`, image base 0x140000000)

Every address used in the protocol docs, grouped by subsystem. Fn = function, Vt = vtable, Cls = message class vtable,
G = global / static data.

## Network core
| Address | Kind | What |
|---|---|---|
| `0x140dfad60` | Fn | stream (socket) ctor, login |
| `0x140dfaf70` | Fn | stream ctor from a connected socket (instance) |
| `0x140dfb630` | Fn | stream read (sets `+0x252` when short) |
| `0x140dfb760` | Fn | stream write |
| `0x140dfb8c0` | Fn | flush / send (cipher slot 1) |
| `0x140dfb3d0` | Fn | receive (cipher slot 2) |
| `0x140dfbe30` | Fn | install cipher pair (`+0x1b8`), decrypt buffered bytes |
| `0x140e02170` | Fn | crypto object ctor (DH groups, "Rhoa") |
| `0x140e03a30` | Fn | Salsa20 pair (km, isClient) |
| `0x140e04840` | Fn | Salsa20 object |
| `0x140e0c9b0` / `0x140e0cbe0` | Fn | wstr write / read |
| `0x141fa4610` / `0x141fa44f0` | Fn | blob16 read / write |
| `0x141fa3c50` / `0x141e61010` | Fn | compact int write / read |
| `0x14089e520` / `0x14089cad0` | Fn | compact int write / read (stream) |
| `0x14299e440` | Fn | message pump: read id, offer to groups |
| `0x14299e3b0` | Fn | register handler group |
| `0x14299e300` | Fn | generic group handle slot (factory → read → owner) |

## Login
| Address | Kind | What |
|---|---|---|
| `0x14213e610` | Fn | login connect, sends 0x0002 |
| `0x14213f190` | Fn | login S→C dispatch (switch on id) |
| `0x14219c350` | Fn | login S→C factory |
| `0x140e03630` / `0x140e03460` | Fn | verify server key / DH2 agree + SHA-512 |
| `0x140d05f70` | Fn | account login sender (0x03, password digest) |
| `0x140d13060` | Fn | character screen → 0x18 |
| `0x1421406f0` | Fn | Play → 0x0e |
| `0x14213e5a0` | Fn | login connection dropped: login `+0xde8` (0 none, 2 handshake, 3 logged in) back to 0 |
| `0x1400f2600` | Fn | state stack singleton; states by id byte (`+0x209`) at `+0x48 + id×0x10` ([09](09-area-change.md) §6) |
| `0x140104510` | Fn | builds every state (login id 7 `0x140d0c8c0`, character select id 9 `0x140d12140`, in game id 4 `0x140d55be0`) |
| `0x140d0d190` | Fn | login state enter: character select if login `+0xde8` = 3 |
| `0x140d574e0` | Fn | in-game frame update → `0x140d57cd0` (instance down: login state, login object kept) or `0x140d57730` |
| `0x140cf1ab0` | Fn | Esc → "Exit to Character Selection": C→S 0x153 |
| `0x140cfc2d0` | Fn | gateway_list.txt reader |
| `0x142fc0150` / `0x142fc04a0` / `0x142fc0620` | G | DSA key / client static private / server static public |

## Instance client
| Address | Kind | What |
|---|---|---|
| `0x1415a4fe0` | Fn | creates the instance client (0xe208 bytes) |
| `0x142144b50` | Fn | instance client ctor |
| `0x14345e810` / `0x14345e8b8` | Vt | instance client / its `+8` sub-object |
| `0x142148880` | Fn | tick (connect job, 0x7d, pump, 0x1b, 0x31, 0xc7) |
| `0x142506420` → `0x1425064d0` → `0x1425065b0` → `0x1425066e0` | Fn | connect job → thread → happy-eyeballs connect |
| `0x142148500` | Fn | send opening 0x0F + 0x0E, state 2 |
| `0x14216e940` | Fn | send message (vtable slot 1 on the stream) |
| `0x141fe1570` | Fn | digest used by 0x0E |
| `0x142fbfbf8` | G | 8-byte constant in the 0x0E digest |
| `0x14214a190` | Fn | instance S→C dispatcher |
| `0x142154e14` | G | dispatcher byte table (`id − 0x0d` → case) |
| `0x1421b5c40` | Fn | instance S→C factory |
| `0x1435d9d98` | Vt | instance handler group |
| `0x142173610` / `0x14299dca0` → `0x14299db60` | Fn | 0x11: main cipher / side-channel cipher |
| `0x142fe0da0` | G | 64-byte XOR mask for the side-channel key |
| `0x142149ca0` | Fn | disconnect (state 1) |
| `0x142162830`, `0x142162630` | Fn | other state writers |

## Area and world
| Address | Kind | What |
|---|---|---|
| `0x1435da180` (ctor `0x14219d940`, write `0x1421415c0`, read `0x142141ef0`) | Cls | 0x1d area |
| `0x142155310`, `0x140e31310` | Fn | queue area generation |
| `0x1435bf8a8` | Cls | C→S 0x7d (tile hash, doodad hash) |
| `0x1435c0358` | Cls | C→S 0x1b tick |
| `0x14316c420` | Cls | u32 message (login 0x1f/0x20, instance 0x0F, 0x22) |
| `0x14316c930` | Cls | empty message (login 0x01, instance 0x11) |
| `0x142186fa0` | Fn | set local player id (`world+0x980`) |
| `0x1435dcb80` (ctor `0x1421a2d40`, write `0x1421a2da0`, read `0x1421a2fe0`) | Cls | 0x21 enter area |
| `0x141d4d020` | Fn | 0x21 Record68 writer |
| `0x142156d80` | Fn | enter area: activate world, bind player, state 3 |
| `0x1400f24b0` / `0x14013f1e0` | Fn | world's first local entity / its "Player" component |
| `0x1435da300` | Vt | world handler group |
| `0x1421c6150` | Fn | world S→C factory |
| `0x142187480` | Fn | world message handler (0x2b9 create, …) |
| `0x142188cc0` | Fn | registers the world group on the instance pump |
| `0x142183670` | Fn | world activation (world vtable +0x1c8): replay queue |
| `0x141d83fb0` → `0x141d7c4c0` | Fn | spatial index insert (23×23 buckets, no bounds check) |

## Objects and components
| Address | Kind | What |
|---|---|---|
| `0x141ee54b0`, `0x141ee3f70` | Fn | GameObjectTypeRegister factory / resource cache |
| `0x141ec18c0` | Fn | build the register from dat tables |
| `0x1424f7520` / `0x1424f76c0` / `0x1416719b0` | Fn | register add / insert / lookup |
| `0x1424748a0` → `0x140df8570` | Fn | narrow path → MurmurHash2 |
| `0x1435dd8f8` (write `0x141fe04d0`, read `0x141fe0520`) | Cls | 0x2b9 create object |
| `0x141f91f60` / `0x141f92000` | Fn | entity header write / read |
| `0x141ebe190` | Fn | create object from blob |
| `0x141ebdf30` | Fn | create a client-side object (id from the world): the client's own predicted projectiles ([11](11-skills.md#10-projectiles-experimental)) |
| `0x141dea920` / `0x141ee00f0` | Fn | blob base data / tasks |
| `0x142427510` | Fn | build components from the type |
| `0x142475bd0`, `0x142476300`, `0x142476580` | Fn | `.ot` parser, extends, sections |
| `0x140e28a30` | Fn | blob reader out-of-range (throws `0x143a50ab0`) |
| `0x1435a0f18` / `0x141e2f0b0` | Vt / Fn | Positioned / deserializer |
| `0x1435dd4e8` / `0x1421c2f40` | Vt / Fn | comp1 (stats-like) |
| `0x14359f5f8` / `0x141e114d0` | Vt / Fn | comp3 |
| `0x1435a7340` / `0x141e6c2f0` (pool `0x141e6be70`, flags `0x141e6c270`) | Vt / Fn | Life |
| `0x141e254f0` / `0x141e25645` | Fn / Insn | Life type constructor / default `+e4..e7 = 00 00 00 01` (corpse usable true) |
| `0x141e67720` / `0x1435a8920` | Fn / Str | Life boolean parser / `corpse_usable` → config `+e7` |
| `0x141e66ec0` / `0x141e66f86` | Fn / Insn | Life type finalizer / sets `+e6` corpse-use veto when `Player` exists |
| `0x141e67110` / `0x141e678d0` | Fn | Life factory / runtime constructor copies corpse and sinking config to `+26a/+26b` |
| `0x141e6c610` | Fn | Corpse predicate: usability, Player veto, life, spectre restriction and stat-9525 fallback |
| `0x1435a5030` / `0x141e56a80` | Vt / Fn | Animated |
| `0x143466450` / `0x141e7dc10` | Vt / Fn | Player (xp setter `0x141e7f6b0`) |
| `0x14359e078` / `0x1415d8cb0` → `0x141e1ee30` | Vt / Fn | Inventories component |
| `0x14346e558` / `0x142192a80`, slot 20 `0x1421932b0` | Vt / Fn | comp9 |
| `0x1435a6ba0`, `0x1435a0078`, `0x143468920`, `0x14346c238` | Vt | Pathfinding, comp7, comp10, comp11 |
| `0x1400f0f00` | Fn | empty `ret` (no-op slot) |
| `0x141e3f4a0` | Fn | StateMachine update reader (vtable `0x1435da8b8` / `0x1435a14e0` +0xa0, S→C 0x2ba): the creation blob's states and timers again; changed values run their events (boss life bar, [12](12-quests.md) §6) |
| `0x141e42410` | Fn | Transitionable component constructor: state `+0x120` = 1, `+0x121` = the type's `transition_on_damage_taken` |
| `0x141e42a80` | Fn | Transitionable .ot keys: `num_states` (2..127, else "That number of states is unsupported"), `transition_on_damage_taken` |
| `0x141e42d50` | Fn | Transitionable type constructor (vtable `0x1435a0a60`): `num_states` (`+0xb0`) defaults to **2** ([10](10-chat-and-commands.md) §5, emerge monsters) |
| `0x141d876e0` | Fn | TriggerableBlockage: blocks the cells of the Animated model's footprint at the object's grid position ([12](12-quests.md) §6) |
| `0x1421c2310` | Fn | Transitionable update reader (vtable `0x1435dd290` +0xa0, S→C 0x2ba): u8 target state, steps toward it ([12](12-quests.md) §6) |
| `0x141e44290` | Fn | TriggerableBlockage reader: u8 blocked, then only while blocked u8 count + count × u8 |
| `0x141ee0ba0` | Fn | blob task lookup by u16 task type (registry), used by the tasks reader `0x141ee00f0` |
| `0x142f496a0` | G | static initializer table entry (Positioned) |
| `0x1445bb240`, `0x1445bb6f0`, `0x1445bbe50`, `0x1445bbd60`, `0x1445bb760`, `0x1445bbd10`, `0x1445ba850`, `0x1445bc610` | G | component pools (Positioned, Stats, Pathfinding, Life, Animated, Player, Inventories, Actor) |

### Player Actor scale ([04](04-player-entity.md#41a-player-actor-scale-299))

| Address | Kind | What |
|---|---|---|
| `0x1421c2f40` | Fn | Creation Stats pairs reach the network setter |
| `0x1424cfa40` | Fn | Network stat override getter; upstream fallback only when absent |
| `0x1424cfaf0` | Fn | Insert/replace one raw pair, notifying only that key |
| `0x1424cfd00` | Fn | Stat change notification to registered listeners |
| `0x141fe1350` | Fn | 0x1b3 reader; world handler `0x142187480` uses the same setter |
| `0x1400caf90` | Fn | Register final1973 evaluator `0x1427ecc30` |
| `0x1427ecc30` | Fn | final1973 formula: raw720, capped1974, permyriad10778, optional limit1975 |
| `0x141e9ca20` | Fn | Actor setup reads1973 at `0x141e9ccfd` and applies scale |
| `0x141e9cf90` | Fn | Actor change observer compares key1973 at `0x141e9d031` |
| `0x141e9ce80` | Fn | Scale applicator writes Positioned +0x2b0 at `0x141e9cf6f` |
| `0x14013d9f0` | Fn | Monster-specific optional scale coefficient lookup |
| `0x1415f4710` | Fn | Render typeScale * uniform * independent stretch; interpolation at +0x358 |
| `0x1415f70a0` | Fn | Render cached scale axes at +0x380/+0x384/+0x388 |
| `0x141e361b0` | Fn | Local-to-world transform uses the same scale axes |
| `0x141c9ba55` | Code | Native melee range scales only when separate flag9807 is set |
| `0x141c9bb0a` | Code | Second native melee range consumer with the same flag9807 guard |
| `0x141e61540` | Fn | Blocking uses signed Positioned size +0x1e3, independently of uniform scale |

## Chat ([10](10-chat-and-commands.md) §1, §2)
| Address | Kind | What |
|---|---|---|
| `0x14215ddd0` | Fn | send a typed line (instance, text, linked items): client slash commands, else C→S 0x15 |
| `0x14215c3b0` | Fn | the client's slash commands (Commands.dat); `/global N`, `/trade N` (row + 1 = 0x2a, 0x2b) → `0x142169250` |
| `0x142169250` | Fn | join a chat channel: C→S 0x134, 5 s deadline (instance +0xcf08 trade, +0xcf18 global), listeners +0xdf8 |
| `0x1435bf730` / `0x141f9ae30` | Cls / Fn | C→S 0x134 class / write |
| `0x1435dc240` / `0x1421a6cd0` | Cls / Fn | S→C 0x135 class / read (ctor `0x1421a6c00`, dispatcher case 0x56 at `0x14214f1f3`) |
| `0x140d5bf60` | Fn | in-game listener (instance +0x240): joins global and trade after entering an area (once, flag +0xa08) |
| `0x14190f7d0` / `0x14190f8a0` / `0x141912640` | Fn | settings `join_global_channel` / `join_trade_channel` / chat language |
| `0x143a60ce8` / `0x143a60c98` | G | remembered global / trade channel (0xffff until `/global N`, `/trade N`) |
| `0x142148880` | Fn | instance network update: a passed join deadline fires the instance +0xe78 listeners |
| `0x140178a80` | Fn | chat panel attaches to the instance: listeners +0xb78 (0x17 line), +0xd78, +0xe78 (join timeout) |
| `0x14017dd10` | Fn | chat panel: "Timeout while joining {0} chat channel {1}" (ClientStrings rows 207 / 206) |
| `0x140195a60` | Fn | chat panel channel popup ("Joining {0} channel …", "Connected to {0} channel …") |
| `0x1422dbfc0` / `0x1421fdc40` | Fn | dat row by index / ClientStrings text by id (row + 1) |
| `0x1435c0258` / `0x141f95200` | Cls / Fn | C→S 0x99 class / write (4 bytes) |

## Monsters ([10](10-chat-and-commands.md) §5)
| Address | Kind | What |
|---|---|---|
| `0x1435dc320` (read `0x1421a4f30`, write `0x1421a4e70`) | Cls | S→C 0x20 monster types at a level (dispatcher case 9) |
| `0x141ec2540` / `0x141ec28e0` | Fn | register `"<variety Id>@<level>"` / the plain variety Id |
| `0x142288c90` | Fn | MonsterVarieties row by index (0x31a-byte rows) |
| `0x1435a6778` / `0x141e6fd00` | Vt / Fn | Monster component (slot 17 `ret`) / its factory ("Creating monster with no level") |
| `0x1435a5a28`, `0x141e701d0` | Vt, Fn | Monster component type; slot 9 parses the `@level` suffix |
| `0x141e2d860` | Fn | Positioned .ot keys (`serialise_collidable` → +0x1b, …) |
| `0x14161d160` | Fn | Targetable deleter (where a failed object build crashes) |

## Patching
| Address | Kind | What |
|---|---|---|
| `0x1428eb82e` | call site | "No patching required" decision |
| `0x1421ccaf0` | Fn | local GGPK root hash is non-zero |
| `0x1428ead70` | Fn | compare a local directory hash with the welcome |

## Inventories and UI
| Address | Kind | What |
|---|---|---|
| `0x14216aa30` | Fn | inventory manager by selector |
| `0x141d625f0` | Fn | register inventory (row → id, id → inventory) |
| `0x141d621a0` | Fn | inventory lookup by id (409 call sites) |
| `0x1431ab2f0` (ctor `0x140b18270`, write `0x140b182a0`, read `0x140b18360`) | Cls | 0xc0 add inventory |
| `0x1421f4a40` / `0x1421f4cc0` / `0x1421edc10` | Fn | inventory object write / read / ctor |
| `0x1421f4f80`, `0x1421ec1d0` | Fn | inventory items read / item write |
| `0x142173fc0`, `0x142174500` | Fn | 0xc3 sockets/gems, 0xc4 list |
| `0x14029dfc0` → `0x1402bafb0` → `0x1402d0500` | Fn | flask frame UI (needs row 11) |
| `0x14098ab0c` | call site | UI lookup of row 0x54 (expanded main inventory) |
| `0x1435dcd00` (ctor `0x14219eb10`, write `0x14219eb40`) | Cls | 0x3d Atlas passive tree presets |
| `0x1435dca60` (read `0x14219e3f0`, handler `0x141ef2710`, numbers only `0x141ef73e0`) | Cls | S→C 0x33 character Passive Skill Tree (instance `+0xc2d8`) |
| `0x141ef3e60` | Fn | character passive tree check: points_from_level, skill_points_from_passives, extra_skill_points |
| `0x1403cc410` | Fn | passive tree click → C→S 0x34 allocate / 0x36 refund (Atlas: 0x3e / 0x3f) |
| `0x1435bf610` (write `0x141f92b00`) | Cls | C→S 0x36 refund a passive (u32 node, u8 expansion, u8 gold respec) |
| `0x141efb140` | Fn | a passive's gold respec cost: VillageBalancePerLevelShared GoldRespecCost × multiplier |
| `0x1409d03bb`, `0x1409da517` | Fn | "Refund All" → C→S 0x37 (id only) |
| `0x141eed000` | Fn | Atlas passive tree check |
| `0x1435ddb78` (read `0x141fdf870`), `0x1435dd918` (read `0x141fdfdd0`) | Cls | S→C 0x1c1 add buff, 0x1c2 remove buff |
| `0x141e11720` | Fn | Buffs component: 0x1c1/0x1c2 handler (world handler cases 0x1c1, 0x1c2) |
| `0x141e11fb0` | Fn | Removes an installed Buffs pointer and marks instance +0xd0 bit 0x10 inactive; instance ID is +0x9c. Native hybrid flask removal verified ([13](13-items.md) §8). |
| `0x142087910` | Fn | effect script: plays a MiscAnimated as a `Metadata/Effects/*Effect` object |
| `0x1400bac70` | Fn | registers the derived stat active_skill_area_of_effect_radius (from the base radius and area %) |
| `0x142155bf0` | Fn | create passive slot 0/1/2 (`instance+0xc2f8`) |
| `0x140785160` | Fn | UI reading the active passive slot |

## Items ([13](13-items.md))
| Address | Kind | What |
|---|---|---|
| `0x1421ec1d0` / `0x1421ec010` | Fn | item write (u16 length + data) / data |
| `0x1421ec2c0` / `0x1421ec390` | Fn | item read (length) / data: type hash → ItemTypeRegister, flags, components (slot 5) |
| `0x141fa5290` | Fn | ItemTypeRegister lookup by HASH32 |
| `0x1421ebe90` | Fn | the four type hashes with a trailing u16 |
| `0x1421e69c0`, `0x1421df6e0`, `0x1421e2e20`, `0x1424aec50`, `0x14249d4b0`, `0x1423f4b30`, `0x14246eb00` | Fn | item component reads: Base, Mods, Sockets, Quality, Charges, Stack, SkillGem |
| `0x1421e7570`, `0x1421e1190` | Fn | debug dumps of the Base and Mods components: each field's name ([13](13-items.md) §2f) |
| `0x1415703a0` | Fn | an item's render flags: Base +0xc6 influences, Mods relic, synthetic, fractured ([13](13-items.md) §2f) |
| `0x1424df5c0` | Fn | a mod line's display kind: crafted, fractured, mutated, divergent |
| `0x142201d70` | Fn | ObjectMagicProperties read: rarity, identified, mod lists, name ([13](13-items.md) §2a) |
| `0x142430380` / `0x142430710` | Fn | one mod read (u16 Mods.dat HASH16 + a compact per stat) / the mod's n-th stat |
| `0x1421ff3c0` → `0x1421ff8f0` | Fn | after the read: rarity tag, apply each mod list with its kind |
| `0x1421f5680` | Fn | inventory item entry read (u32 id, u8 x, u8 y, item) |
| `0x1435dbd28` (ctor `0x1421a37b0`, write `0x1421a37f0`, read `0x1421a3880`) | Cls | S→C 0xbf inventory change (`0x1425079a0` / `0x142507bf0`) |
| `0x141d62550` → `0x1421f5160` | Fn | 0xbf handler → apply removals, additions, revision |
| `0x1435dca20` (read `0x1421aded0`, write `0x1421addf0`) | Cls | S→C 0x263 item-use result; successful flask use triggers the drinking sound ([13](13-items.md) §8) |
| `0x14215391b` → `0x142171890` | Fn | 0x263 dispatch (case 0xc6) → instance +0x3c78 item-use listeners |
| `0x140d95a70` / `0x140ddcd60` → `0x140ff6320` | Fn | item-use UI callbacks → native audio submission (BaseItemTypes use sound when override is empty) |
| `0x1435c0898`, `0x1435dc660` | Cls | S→C 0xc1 remove inventory, 0xc2 inventory permissions |
| `0x1435bf5f0` (write `0x141fa0d00`, sender `0x14215f300`) | Cls | C→S 0x28 pick up item |
| `0x1435bf710` (write `0x141f8feb0`, sender `0x14215f400`) | Cls | C→S 0x2a place held item |
| `0x1435c0838` (write `0x141f90650`) | Cls | C→S 0x30 socket the held gem |
| `0x140117b80` | Fn | drop held item: 0x29 outside town, Keep / Destroy popup in town |
| `0x1435bff00` (write `0x141f91480`, sender `0x14215c3b0`) | Cls | C→S 0x16 Commands.dat command (Destroy = 9) |
| `0x1435c02b8` (write `0x141f90340`) | Cls | C→S 0x2d (not mapped) |
| `0x1435bf6d0` (write `0x141fa08d0`, sender `0x14215f5c0`) | Cls | C→S 0x4a held item onto an item ([13](13-items.md) §4a) |
| `0x1435bfdd8` (write `0x141f90870`, sender `0x142165640`) | Cls | C→S 0x7c split a stack ([13](13-items.md) §4a) |
| `0x141ea10a0` | Fn | WorldItem component read |
| `0x141e1ee30` → `0x1424f44e0` | Fn | player Inventories component: visible equipment entries |
| `0x1402ce5f0` | Fn | item attribute requirement check (virtual_*_for_requirements) |
| `0x1424a6d90` | Fn | an item's requirement for one attribute (type value, flat and % item stats) ([13](13-items.md) §2d) |
| `0x1421e03d0` | Fn | an item's level requirement → Mods +0x2bc (type level, mod level × 4 / 5, override, +); not run for network items, which keep the sent byte ([13](13-items.md) §2d) |
| `0x1421eca50` | Fn | can the player use this item: level (Mods +0x2bc vs Player +0x1ac), gem levels, attributes |
| `0x141e80340`, `0x141e802d0` | Fn | Player caches: requirement attributes; UI attributes |
| `0x1424affc0` / `0x1424aff60` | Fn | Armour component raw base-roll byte reader / writer; 32 is a minimum flag |
| `0x1424afc90` | Fn | Armour roll setter: ordinary 0..31 at +0x28; special flag at +0x2a |
| `0x1424af890`, `0x1424af910`, `0x1424af990` | Fn | Base armour/evasion/ES: interpolate over 31, then round |
| `0x1424af310`, `0x1424af1a0`, `0x1424af430` | Fn | Final armour/evasion/ES: rounded base, locals, quality, round |
| `0x1424afb10`, `0x1424afb90`, `0x1424afc10` | Fn | Local increased ES/evasion/armour helpers |
| `0x142523ab0` | Fn | ES-to-Life: full flag wins; truncate final local ES per active armour slot |
| `0x1428b4e30` | Fn | Effective shield ES: suppressed for ES-to-Life conversion or shield transfer/prohibition |
| `0x142724c50` | Fn | Combined shield defence increase: generic stat 24 plus the Devotion-dependent term |
| `0x14272f680` | Fn | Shield armour increase: global armour plus shield-only armour and combined shield defences |
| `0x142727dd0` | Fn | Shield evasion increase: global evasion plus shield-only evasion and combined shield defences |
| `0x14252f6a0` | Fn | Shield ES increase: global ES plus shield-only ES and combined shield defences |
| `0x142731bfc` | Code | IR shield evasion uses shield armour-only plus shield evasion/shared increases once |
| `0x142732ee7` / `0x14272a59b` | Code | Final AR/EV: truncate after adding 0.5 to the summed positive contributions |
| `0x14252c6e7` / `0x142513810` | Code / Fn | Final ES calls signed half-away rounding after summing sources |
| `0x14252c730` / `0x14252d478` / `0x14252d533` | Fn / Code | Flat ES getter reads key 135 and adds global flat ES |
| `0x1405bdd70` / `0x140664f10` | Fn | Linear interpolation / half-up rounding used by item defences |
| `0x140e1a230` | Fn | Item's Armour component getter |
| `0x140030c10` / `0x14345c550` | Fn/Cls | Register Sacred Orb action / validator vtable |
| `0x141f83050` | Fn | Sacred Orb target gate: Armour, Mods, no mirror, at least one variable native range |
| `0x1402f36c0` | Fn | item's AttributeRequirements component getter |
| `0x141e1dce0` | Fn | InteractionAction type parser (`interaction_distance_override` → +0x1bb) |

## NPCs ([17](17-npcs.md))
| Address | Kind | What |
|---|---|---|
| `0x141e75050` | Fn | NPC component reader (slot 17): u8 → +0x20, u8 → +0x21 |
| `0x141e751d0` | Fn | NPC component debug dump ("NPCTalk: (") |
| `0x141e76d40` | Fn | the NPCTalk row the client picks for the player |
| `0x1435dd7d8` (read `0x141fddcb0`) | Cls | S→C 0x84 NPC dialogue (object header, u8 → +0x20), world handler `0x142187480` |
| `0x142149db0` | Fn | 0x84's handler: object 0 closes; u8 0xff fires the listeners at instance +0x13f8 |
| `0x140dcc720` | Fn | in-game UI: open the NPC dialogue (registered on instance +0x13f8 by `0x140dbcd40`) |
| `0x140723d60` | Fn | NPCDialogueWindow setup (dialogue style; via `0x14071f6d0`) |

## Waypoints and the World panel ([16](16-waypoints.md))
| Address | Kind | What |
|---|---|---|
| `0x1435dc7e0` (read `0x14219f460`) | Cls | S→C 0x4c activated waypoints (u32, 19-byte bitset → instance +0xcf31), dispatcher case 0x12 |
| `0x1435dc2a0` (read `0x1421a0450`) | Cls | S→C 0x7b open a UI (u8 kind, 4 × u32), dispatcher case 0x1c |
| `0x1435c08b8` (write `0x141f92e60`) | Cls | C→S 0x4d waypoint travel (u16 area, u32, u8 Ctrl) |
| `0x142163420` | Fn | 0x7b's switch over UI kinds; kind 0 → listeners at instance +0xff8 |
| `0x140dcd570` | Fn | in-game UI: open the World panel in travel mode (`0x140ab1440` → `0x140a97330`, panel +0x522) |
| `0x140a9b580` | Fn | World panel pin click → C→S 0x4d |
| `0x141fdcf90` | Fn | is an area's waypoint bit set (bit = index among HasWaypoint rows with +0x1e4 in {1, 2, 5, 11}) |
| `0x1421449f0` | Fn | waypoint look of the current area (0 idle, 1 inactive, 2 activation), from 0x4c |
| `0x140ab0330` | Fn | pin widget update: revealed (+0x329) and waypoint (+0x32a) states |
| `0x140ab6200` | Fn | pin revealed: any MapPins `QuestFlags` set |
| `0x140ab5fc0` | Fn | pin as a waypoint: bit set and area accessible |
| `0x140797750` | Fn | area accessible: all WorldAreas +0x54 quest flags set (Player component) |
| `0x140a9a7d0` / `0x140a9aa90` | Fn | connection lines: only between two shown pins |
| `0x141e83340` | Fn | flag set reader (u16 n × (u8 block, u64 bits)); Player +0x1b8 quest flags, +0x1d0 |
| `0x14012aae0` | Fn | flag set test `(set, bit)` |
| `0x141e7ee30` | Fn | Player component update reader (0x2ba): u16 flags, 0x02 quest flags, 0x08 league quest flags, 0x10 Hideouts keys, 0x200 9 bytes, 0x01 experience |
| `0x141e1c550` | Fn | HideoutDoodad reader: u32 MTX hash, u8 variation, u8 flag, only when the object's data +0x14e is set (in hideouts) |

## Hideouts ([21](21-hideouts.md))
| Address | Kind | What |
|---|---|---|
| `0x141e7dc10` / `0x141e7ee30` | Fn | Player reader / update: the two Hideouts keys (update bit 0x10, then vtable +0x130) |
| `0x142fca900` | Data | league quest flags: 0x776 sorted {UTF-16 name, length, u32 id} |
| `0x1423f1fb0` / `0x141de7f60` | Fn | league quest flag name → id (0x777 unknown) |
| `0x14155cdf0` | Fn | league flag test on Player +0x1d0 with bit id − 1 |
| `0x141e65563` | Call | the map device's Targetable test of `UnlockedMapDevice` (bit 349) |
| `0x14216c840` | Fn | HUD hideout panel check: quest flag HASH32 0x2f1b9c25 in the Player component |
| `0x1401ff190` | Fn | hideout panel shown or hidden by `0x14216c840` |
| `0x1401fc880` | Fn | hideout panel (palette) constructor |
| `0x142163420` case 5 | Fn | S→C 0x7b kind 5 → listeners at instance +0x1778 → `0x140dcd9a0` |
| `0x142163420` case 0x2b | Fn | S→C 0x7b kind 0x2b (HideoutMapDeviceScreen) → listeners at instance +0x2278; opens the Atlas map device |
| `0x00e7` | C→S | Atlas map node picked: u16 BE WorldAreas HASH16, u8 0x0d (21-hideouts.md §4) |
| `0x140dcd9a0` / `0x140dce1f0` / `0x140dce290` | Fn | open the map device window (modes 0xd, 0x5e, 0x56; lists +0x1778, +0x17f8, +0x1878) |
| `0x140c2a1c0` | Fn | map device window: Activate button (mode 0xd → `0x142166480`, mode 0x5e → C→S 0x296) |
| `0x142166480` | Fn | C→S 0x00d8 Activate: u16 (target +0x7b), u8 (writer class `0x1435bff20`) |
| `0x1435dcc00` (read `0x1421a0a00`, write `0x1421a08a0`) | Cls | S→C 0x008f the map device's portals (dispatcher case 0x21) |
| `0x142186070` | Fn | 0x008f handler: per portal PortalClient +0xb8, `on_map_portal_opened` / `on_map_portal_closed` |
| `0x1421b9af0` | Fn | 0x008f portal list read (u32 object, u16 area, then u16, u8, u8, u8, wstr when the area is set) |
| `0x14161a020` | Fn | PortalClient vtable +0xb8: set the portal's area, model (`0x141e2d240`) and rendering |
| `0x141e2cee0` / `0x141e2cf40` | Fn | Portal component read (u8) / set the multiplex portal type (the "portal" state) |
| `0x1423f3f10` / `0x1423f3e60` | Fn | item Map component read / write (u8, u8, u8 flag [u32]) |
| `0x140c276c0` | Fn | map device window constructor (HUD +0x7d8) |
| `0x140dbcd40` | Fn | HUD: registers its 0x7b listeners on the instance lists |

## Stash ([18](18-stash.md))
| Address | Kind | What |
|---|---|---|
| `0x14216cf60` | Fn | stash list by kind: instance +0xc498 personal, +0xc4b0 guild, +0xc4c8 trade (0x68-byte records) |
| `0x1421ba150` / `0x141d4d1f0` | Fn | tab list read / tab record read |
| `0x1421a3350` | Fn | S→C 0x136 read (one stash list, case 0x57) |
| `0x1421a45b0` | Fn | S→C 0xc8 read (tab loaded, case 0x3b) |
| `0x140dcd5e0` / `0x140dcd780` | Fn | 0x7b kind 1 / 2 listener: open the stash / guild stash, select a tab by affinity |
| `0x142167010` | Fn | queue C→S 0xc7 (tab contents request); sent by the tick `0x142148880` |
| `0x141f95fa0` | Fn | C→S 0xc7 write (vtable `0x1435bff20`) |
| `0x1421670f0` / `0x141f96130` | Fn | C→S 0xcc tab order: build / write |
| `0x142167370` / `0x141f96c50` | Fn | C→S 0xd1 tab into folder: build / write |
| `0x140a87830` | Fn | stash panel: build the tab buttons from the list |
| `0x140a85c30` | Fn | stash panel: tab selected (request or build its page) |
| `0x140a8d1b0` | Fn | stash panel: refresh on opening (requests the current tab) |
| `0x14025b420` | Fn | tab settings: rename / recolour |
| `0x142168ab0` / `0x141f97560` | Fn | C→S 0xe0 (Ctrl + click into a tab): build / write |
| `0x142168e20` / `0x141f97e40` | Fn | C→S 0xe3 withdrawal: build / write (kind, raw token, bulk boolean) |
| `0x140312810` / `0x140311440` | Fn | Shared stash withdrawal click handler / bulk click entry; forwards bulk 1 to 0xe3 |
| `0x140dc4db0` | Fn | show / hide a panel (a second 0x7b kind 1 would close the stash) |

## Strings worth searching
"Connect time to instance server was", "Client-Safe Instance ID = ", "Generating level ", "Instance sent world area
that does not exist with hash: ", "Area reported by instance does not exist", "Unable to deserialise packet with pid ",
"Unknown object type serialized by server", "Unrecognised task serialised by server; missing registration?",
"Metadata/GameObjectRegister", "Game::GameObjectTypeRegister", "Art/2DArt/UIImages/InGame/Azmeri/SapFlaskFrame".

## Latency, HNC, wake zone, movement ([07](07-latency-visibility-movement.md))

| Address | Kind | What |
|---|---|---|
| `0x142155930` | Fn | 0x1c: drop acknowledged entries from the send-time queue (`+0xc458`); index ≥ size clears it and returns `+0xc470` (latest tick time) |
| `0x14214b8dc` | Code | 0x1c handler after the pop: rtt − server delay clamps the latency graph value (`+0xc490`) |
| `0x140d57cd0` | Fn | per-frame perf stats: feeds `+0xc490` to the latency graph |
| `0x141fa0720` | Fn | 0x1c read (u16, u16, u32 HNC carrier) |
| `0x14299de50` | Fn | HNC byte state machine ("HNC Protocol Violated") |
| `0x14299db60` | Fn | HNC key: km XOR `0x142fe0da0`, byte swaps |
| `0x142fe0da0` | Data | HNC 64-byte mask |
| `0x141ec0aa0` | Fn | ObjectInWakeZone (0x1a9) |
| `0x1421940a0` | Fn | Actor message handler (0x1ab action started, …) |
| `0x141c06c2a`..`0x141c06c8e` | Code | Attack timing reads total base ms 0x221a and final player speed 0x126 |
| `0x142706568`..`0x14270658f` | Code | Explicit total base attack duration getter returns stored 0x221a |
| `0x141e30c90` | Fn | Positioned message handler (0x1b7 teleport, 0x1ba move, …) |
| `0x142166300` | Fn | C→S 0x25 / 0x24 sender (use skill at location / on object) |
| `0x141fdda70` | Fn | 0x1ab read |
| `0x141fa3c50` / `0x141fa3d60` | Fn | compact int write / read |
| `0x141f91f60` / `0x141f92000` | Fn | world object header write / read |
| `0x1416aa670` | Fn | perf overlay ("network: {} ms") |
| `0x141e7b530` | Fn | Pathfinding component MoveTo (target, min/max range, precision, tolerance) |
| `0x141e7b920` / `0x141e7b6f0` | Fn | Pathfinding: compute the path (waypoints `+0xd4`, count `+0x518`) / start the next segment |
| `0x141e4ef90` / `0x141e4d890` | Fn | the pathfinder (A*, player ellipse, waypoint pruning; `ClientPathfinder`) |
| `0x141e4d7d0` | Fn | pathfinder heuristic (distance outside the range + 0.001 × cross product) |
| `0x141d85260` / `0x141d87160` | Fn | walk value of a cell (4 bits, 3 layers) / Bresenham line check against a threshold |
| `0x141e61370` / `0x141e61540` / `0x141e616a0` | Fn | object collision: cell (tile buckets) / one object (Chebyshev − size) / line |
| `0x141beb420` / `0x141beb2b0` | Fn | pathfinder open list push / pop (MSVC heap, `a.f > b.f`) |
| `0x142fbf350` / `0x142fbf330` | Data | the 8 directions (dx, dy) / opposite direction |
| `0x141e34400` / `0x141e4d180` | Fn | Positioned straight move to a cell / Bresenham segment setup (step time) |
| `0x141e32710` / `0x141e4d390` | Fn | Positioned per-frame move tick (step checks, accumulator) / one Bresenham step |
| `0x141e350e0` | Fn | Positioned stop: takes effect at the next tick (`+0x1e6` bit 7), keeps the accumulator |
| `0x141e31200` / `0x141e35250` | Fn | 0x1b7 relocation / immediate glide cancellation: clears movement durations +0x240/+0x280; projectile impact endpoint (static 3.29.3b evidence) |
| `0x141e4ee90` | Fn | pathfinder: the coarse waypoint a cell heads for (within √143 and in sight) |
| `0x141e7c0b0` | Fn | Pathfinding speed: base `+0x548` × (1 + stat 0xf5 / 100) × (1 + stat 0xce6 / 100), capped by stat 0x44f |
| `0x141e9ca20` | Fn | Actor setup: run-speed roll `+0x1a8` = 1 + v / 100, v by TinyMT32 in the actor data's [`+0xa0`, `+0xa4`] |
| `0x1413cbe50` / `0x1404d03d0` | Fn | TinyMT32 seeding (init_by_array) / random int in a range |
| `0x14215f700` | Fn | C→S use a skill: lockstep 0x018e (dx, dy) / 0x018f (object) when the network mode `+0x984` is 2, else 0x25 / 0x24 |
| `0x14215fb10` / `0x14215fcb0` / `0x14215fe50` | Fn | C→S lockstep 0x0192 (new dx, dy) / 0x0195 (release) / 0x0196 (release a skill) |
| `0x142183930` | Fn | world update: lockstep runs the queued server frames (deque `+0x9c0`, count `+0x9d8`), catching up × 1.01–5 |
| `0x142183ce0` | Fn | world step (vtable `+0x360`): one server frame's messages, then the objects' ticks |
| `0x1421872b0` | Fn | world message intake: queues messages; S→C 0x2b5 ends a frame (pushes world `+0x11c` ms), 0x2b6 sets it |
| `0x141645fa0` | Fn | S→C 0x2b6: world `+0x11c` = the frame length in ms |
| `0x14190faf0` | Fn | config `networking_type` → network mode (0 predictive, 2 lockstep, 3 auto) |


## Fixed instance-message writers ([02](02-instance-connection.md))

| Address | Kind | What |
|---|---|---|
| `0x140e29080` / `0x14345ef10` | Fn/VT | C→S 0x020a writer / vtable: u16 opcode and one byte |
| `0x141f935a0` / `0x1435bfe78` | Fn/VT | C→S 0x0059 writer / vtable: 16 bytes including opcode |
| `0x142160020` | Fn | C→S 0x0059 sender; fixed 4/2/2/4/1/1-byte fields |

## Skills ([11](11-skills.md))

| Address | Kind | What |
|---|---|---|
| `0x1400dd340` | Fn | registers own-aura virtual 11075 with dependencies 21148/11881 |
| `0x14289a420` | Fn | 11075: signed-nonzero 21148 OR 11881 |
| `0x1400e4f60` | Fn | registers aura-skill virtual 15801 |
| `0x1428cf720` | Fn | 15801: 3004 OR (2730 AND 2532) |
| `0x1400e4fc0` | Fn | registers skill-disabled virtual 15798 |
| `0x1428cf770` | Fn | 15798: aura branch checks skill 15801 and caster 11075; other restrictions separate |
| `0x1428b0750` | Fn | virtual double-armour chance 12268; [model/evidence](../game/steel-willed-armour.md) |
| `0x1428b0854` | Code | reads Steel Willed per-recent-enemy-hit input 20680 |
| `0x1428b08eb` | Code | reads recent enemy-hit counter 4534 |
| `0x1428b0962` | Code | multiplies per-hit input by recent count |
| `0x1428b0c24..0x1428b0c36` | Code | caps the per-hit contribution at 30 |
| `0x1428b0be3` / `0x1428b0c3f` | Code | reads generic chance 8404 / adds it after the contribution cap |
| `0x1435db980` | VT | ActiveSkillManager (Actor +0x598; skills at +0x158, sorted by instance +0x40) |
| `0x141eea6a0` | Fn | ActiveSkillManager::SkillInstanceAdded (manager slot 7) |
| `0x141eea440` | Fn | find/create skill cooldown record (requires virtual cooldown count) |
| `0x141eebed0` | Fn | cooldown object constructor: base duration from skill +0xc8 or override |
| `0x141eebfe0` | Fn | cooldown consume: base-time entry and separate recovery rate |
| `0x141eebdb0..0x141eebdc8` | Fn | cooldown tick subtracts elapsed time times recovery rate |
| `0x141eeb7aa..0x141eeb7e7` | Fn | refresh an active cooldown entry's recovery rate |
| `0x141eead13..0x141eead4a` | Fn | skill replacement refreshes cooldown only for base/count changes or zero rate |
| `0x142197d24..0x142197e7b` | Fn | Actor 0x40 cooldown trailer reads base-time progress/total, rate and flags |
| `0x142197f90..0x142197fd8` | Fn | Actor 0x40 replaces the matching existing cooldown entry vector |
| `0x1401232e0` | Fn | instant input sender: mode 4 ground 0x190, modes 5/6 object/self 0x191 |
| `0x1435c0718` | Cls | C→S 0x190 location instant; writer `0x141f91f60`, reader `0x141f92000` |
| `0x142195880` | Fn | S→C 0x1ce instant action and use consumption, without an ordinary action sequence |
| `0x1432823d0` | Vt | IceDash action, installed by factory `0x1412805a0` |
| `0x1412f1200` | Fn | IceDash start: departure at action +0x318, aim at +0x320 |
| `0x141bfe240` | Fn | blink endpoint: max 42 cells scaled by stat 11856, min 32, then terrain validation |
| `0x142195ab1..0x142195b0e` | Code | object instant confirmation binds target and reads its Positioned cell |
| `0x141bfe610` | Fn | object blink aim: minimum vector, size-aware extension, candidate trace before clamp |
| `0x141c02050` | Fn | object blink candidate Bresenham trace, retaining the last terrain/target-valid cell |
| `0x141c02c30` | Fn | candidate target-footprint check through `0x141c03c80` |
| `0x140671d00` | Fn | normalized cell vector: floor(component * scale + .5), including negative components |
| `0x141ee97f0` | Fn | create the action for a skill id |
| `0x142192860` | Fn | ClientActor ctor: basic actions from the type's u16 list (`type+0xf8`) |
| `0x142198d40` | Fn | AddSkill(manager, ActionTypes HASH16): basic action |
| `0x1421932b0` | Fn | Actor slot 20 reader (f0 0x40 skill list, 0x20 skill update, …) |
| `0x14246f900` | Fn | SkillGem: the level from the experience (thresholds from ItemExperiencePerLevel) |
| `0x142197b10` | Fn | Actor 0x40: skills, soul row/capacity/current, cooldown trailer |
| `0x142198800` | Fn | Actor 0x20: skills/removes, soul seeds, current/new-capacity updates |
| `0x142199a80` | Fn | Sorted soul-entry upsert at skill manager +0x188, stride 0x20 |
| `0x141eea5b0` | Fn | Soul payload lookup through GrantedEffects ActiveSkills pointer |
| `0x141ee8720` | Fn | Effective required souls: no-souls flag, selected multiplier, final percent |
| `0x141eea3a0` | Fn | Availability: required souls <= current |
| `0x141eea350` | Code | Native soul subtraction and dirty flag inside 0x141ee9d30 |
| `0x140acfbb0` | Fn | Soul icon uses/fill; divisions guarded by nonzero required souls |
| `0x140ad7fb0` | Fn | Soul current/capacity text |
| `0x140826070` | Fn | Soul current/capacity text |
| `0x142198560` | Fn | Actor f0 0x20: remove u16 skill ids from the skill manager |
| `0x1421981d0` | Fn | read n skill records into the manager |
| `0x142196ce0` | Fn | skill record reader |
| `0x1421975f0..0x142197610` | Code | Clear wire bit1 and nonzero raw stored uses set constructor disable bit |
| `0x1421976e8..0x1421976f6` | Code | Constructor parameter13 = (wire bit2 * 2) or dil |
| `0x141ee7571..0x141ee75ba` | Code | Parameter13 bit0 zeros soul inputs; otherwise copies GEPL +0x28/+0x2c |
| `0x1424a0dc0` | Fn | GrantedEffectsPerLevel reference reader |
| `0x1424a0990` | Fn | GrantedEffectStatSetsPerLevel reference reader |
| `0x141ee73d0` | Fn | ActiveSkillInstance ctor (action id from GEPL → GrantedEffect → ActiveSkill → ActionType) |
| `0x1435b9728` | VT | ActiveSkillInstance |
| `0x1424cfa40` | Fn | stat getter (stats, key); stored pairs at stats +0xf0 |
| `0x1424c9920` | Fn | Character-panel formatter: ordered stat keys at +0xd8/+0xe0; returns UTF-16 text. Natural leech-row calls verified ([20](20-damage.md) §5b). |
| `0x1424c6020` | Fn | Panel argument converter: leech limits use NoFunction, DivideByOneHundred, PerMinuteToPerSecond2dpIfRequired and PermyriadPerMinuteToPercentPerSecond. |
| `0x141e6a190` | Fn | Life tick: notifies listeners only while life > 0 |
| `0x140220ab0` | Fn | mini life bar entry: flags from the Life pools (0x20 = dead) |
| `0x140220c30` | Fn | mini life bar entry: stat listener (`hide_mini_life_bar` 0x2a8b, `virtual_is_hidden_monster` 0x59d0) |
| `0x140221fd0` | Fn | mini life bar: visible? |
| `0x141e8cf10` | Fn | Actor: start (queue) an action |
| `0x141e8e060` | Fn | Actor: per-frame runner of the queued action |
| `0x141e8d3d0` | Fn | Fresh local action sequence from Actor `+0x21a + 1` |
| `0x141e8e92b..0x141e8e932` | Code | Action start: copy u16 sequence `+0x11a` to Actor RNG seed `+0x21c` |
| `0x141ba3104..0x141ba3115` | Code | Animation seed: zero-extended action sequence plus intrinsic repeat count |
| `0x141b6eb70` | Fn | Melee ctor: `attack_repeat_count` (stat 0x4eb) becomes action `+0x2d0` |
| `0x1419e3960` | Fn | Melee completion: decrement intrinsic repeat count; previous zero ends action |
| `0x140e0bec0` | Fn | Animation RNG initializer: literal uint32 recurrence and eight warm-up steps |
| `0x140484fe0` | Fn | Native animation RNG next word: state transition and tempering |
| `0x140484fa0` | Fn | Native bounded RNG draw: rejection sampling; bound one consumes no word |
| `0x141e98c40` | Fn | Speed-based ACT animation eligibility and weighted reservoir selection |
| `0x141e991e0` | Fn | Duration-based ACT animation eligibility and weighted reservoir selection |
| `0x141fde9d0` | Fn | 0x1d1/0x1d2 reader: duration float, Animation.dat type byte, boolean |
| `0x141e95e60` | Fn | 0x1d1 Actor animation: cancel prediction, select stance ACT, lock input during recovery |
| `0x1421949c3` | Code | Actor 0x1d1 dispatch to virtual `+0xe0` recovery handler |
| `0x141e8d900` | Fn | Action-start gate: Actor `+0x219` bit 2 blocks input during recovery |
| `0x141e90740` | Fn | End local predicted action (Actor +0x1b8) |
| `0x141e934b0` | Fn | End Actor animation recovery; clear +0x219 bit 2 and resume queued/idle actions |
| `0x141e58ef0` | Fn | Generic Animated message handler: 0x1b2 kind zero plays a rig clip |
| `0x141dba530` | Fn | Play(controller, resource pair, index in r8d, speed); main state at controller +0x180 |
| `0x141938830` | Fn | Melee action begin = skill_is_non_pathing |
| `0x14219f820` | Fn | S→C 0x55 death screen read |
| `0x14028fc80` | Fn | `death_screen` panel ctor (setup `0x140290820`, buttons `0x140290bf0` → C→S 0x54 / 0x153) |
| `0x140d86180` | Fn | HUD vtable +0x4d0: show/hide the death screen |
| `0x1435dc4c0` (read `0x14219f600`) | Cls | S→C 0x53 skill bar: 13 × u16 skill ids (LE), u16, u16 |
| `0x1435bfc58` | Cls | C→S 0x4e bind a skill to a skill bar slot (u8 slot, u16 skill id) |
| `0x140aecab0` | Fn | skill bar UI: put a skill in a slot (`0x140ae16b0`) or clear it (`0x140ae1fd0`), then send C→S 0x4e |
| `0x14319d870` | VT | skill bar UI (13 slots; `0x140ae04b0` subscribes it to instance +0xaf78 and +0xad78) |
| `0x140ae0fb0` | Fn | skill bar vtable +0x390: rebuild from 13 skill ids (lookup in ActiveSkillManager +0x158, slot set +0x398, clear +0x3a0) |
| `0x1435a0d10` | VT | Projectile component (0x20 bytes; slot 17 reads nothing, almost every slot a `ret`) |
| `0x141e37850` | Fn | Projectile component constructor (writes `0x1435a0d10`) |
| `0x141e30c90` | Fn | Positioned message handler: 0x1b7 teleport, 0x1b8, 0x1b9, 0x1ba move (height u32 +0x28, speed f32 +0x2c), 0x1bc |
| `0x1419e7660` / `0x1419eef50` | Fn | Spell duration: virtual base milliseconds / 1000 / (1 + cast speed / 100) |
| `0x1419e7500` / `0x1419e763c` | Fn / Code | Ordinary spell speed getter / read cast_speed_+% (296) |
| `0x141d84b00` | Fn | terrain height of a cell (terrain, int cell[2]) in steps of 7.8125; `0x141e35800` interpolates for a Positioned |
| `0x1415f53c0` | Fn | Render: model transform; tilt = −`0x141e35c00`(Positioned) when the type has `tilt_to_target` |
| `0x141e34400` | Fn | Positioned: start a move (interpolation `0x141e4d180`; height 0x7fffffff = terrain, else z = −height × 7.8125) |
| `0x141e3491a` / `0x141e34921` | Fn | Move endpoint/current Positioned markers passed to the 3D stepper |
| `0x141e3495b` to `0x141e3496b` | Code | Move endpoint world z = -marker * 7.8125 before render interpolation |
| `0x141e4d510` | Fn | Render interpolation setup: stores endpoint world z minus current world z |
| `0x142042360` | Fn | create one client-side doodad from a placed record (grid x, y +0, +4; angle +0x14; type +0x30; .ao +0x38): rooms' doodads too ([14](14-monster-packs.md) §3) |
| `0x14214dbe1` | Fn | instance dispatcher case 0xe2 (S→C 0x2a9 gold, read `0x140913050`, u32 BE): stores it at session +0xc40c, plays Gold_Spend.ogg when it drops |
| `0x140d62ec0` | Fn | 0xb8 listener on the UI state: flags bit 0 → +0x984 = other party, +0x988 = 1 (the active trade) |
| `0x14030e3e0` | Fn | Ctrl + click on an inventory item: with +0x988 set sends 0xb2 x = y = 0xff (`0x142166660`), else the other panels' checks |
| `0x140dc8750` | Fn | S→C 0x7b kind 0x5f: the character's passive tree (UI +0x650); u32 ≠ 0 → mode 2 (`0x1409de680`) |
