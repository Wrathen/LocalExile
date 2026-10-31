# 04 — The player entity: object types, 0x2b9 and the component blob

Code: `InstanceEntities.ObjectTypeHash`, `InstanceEntities.CreateObject`, `PlayerObject.Blob()`, `BlobWriter`
(`src/GameServer.Core/Protocol/Instance/InstanceEntities.cs`). Tests: `InstanceEntitiesTests`. The component order and
sizes were **traced live with Frida** (`scripts/re/frida/trace-player-blob.js`). Static reading alone got the order
wrong twice; see [06](06-debugging-log.md).

## 1. Object types need no registration message

`world+0x8d8` holds `Game::GameObjectTypeRegister`, the resource "Metadata/GameObjectRegister" (factory `0x141ee54b0`,
cache lookup `0x141ee3f70`). Despite the name it is not a file: its loader `0x141ec18c0` **builds it from the client's
own dat tables**. It walks the rows of several tables and registers each row's metadata path with
`0x1424f7520(register, path, base, …)`:

| Row size | Table (where identified) | Base type passed |
|---|---|---|
| 0x141 | | row field +0xc2 |
| 0xac | | row field +0x10 |
| 0xea | | row field +0x43 |
| 0x264 | **Characters.dat** | `"Metadata/Characters/Character"` |
| 0x31a | | row field +0x48 (with a list at +0x2e3/+0x2eb) |
| 0x8a, 0x2c, 0x9c | | none |

The register is a sorted vector of 0x30-byte entries keyed by a u32 under a critical section (lookup `0x1416719b0`,
insert `0x1424f76c0`).

**Key = MurmurHash2, seed 0, over the path with each UTF-16 unit narrowed to a byte** (`0x1424748a0` narrows, then
`0x140df8570` hashes: `m = 0x5bd1e995`, `h = len`, 4-byte LE blocks, standard tail and finalizer).

| Path | Key |
|---|---|
| `Metadata/Characters/Int/Int` (Witch) | `0x9c41993e` |
| `Metadata/Characters/Str/Str` (Marauder) | `0xd7aa9065` |

Characters.dat rows (`data/tables/Characters.json`): 0 Marauder `Str/Str`, **1 Witch `Int/Int`**, 2 Scion
`StrDexInt/StrDexInt`, 3 Ranger `Dex/Dex`, 4 Duelist `StrDex/StrDex`, 5 Shadow `DexInt/DexInt`, 6 Templar
`StrInt/StrInt`. Note that the login 0x15 class byte uses a different enum (3 showed "Witch").

A key missing from the register makes the world handler log "Unknown object type serialized by server" and create
nothing.

## 2. S→C 0x2b9: create object

World group (factory `0x1421c6150`, handler `0x142187480`). Class `0x1435dd8f8`, write `0x141fe04d0`, read `0x141fe0520`.

```
u16  0x02b9
u32  entity id      +0x10   ┐ common entity header (0x141f91f60 / 0x141f92000)
u32                 +0x14   │ we send 0
u16                 +0x18   ┘ we send 0
u32  object type    +0x20   MurmurHash2 key (section 1)
blob16 component data +0x28 u16 BE length + bytes (0x141fa4610)
```

Handler (`0x142187480`, id 0x2b9): look the type up, build the object with `0x141ebe190(obj, type, id, &blob, world)`
(0x248 bytes), then if `id == world+0x980` (set by 0x22) the object becomes the **local player** (`world+0x970`,
`0x141ec2fe0`, event `0xe794fcda`). It is only processed when the world activates, i.e. on 0x21 (see
[03](03-area-and-world.md#4-world-messages-are-queued-until-the-world-is-active)).

`InstanceEntities.CreateObject(0x1000, ObjectTypeHash("Metadata/Characters/Int/Int"), witch.Blob())`.

## 3. The component blob

**Little-endian.** Every read is a bounds-checked memcpy on a reader `{u8* data; u64 size; u64 pos}`. Running short
throws (the out-of-range builder is `0x140e28a30`, the exception type `0x143a50ab0`) and the client shows a
**"Buffer Underflow"** message box. `0x141ebe190` reads, in order:

1. **u8 object flags**: bit0 → obj `+0x8c |= 0x80`, bit1 → `+0x8d |= 2`
2. **base data** (`0x141dea920`): u8 n, then n × (raw u32, u8, sub-data `0x141dfaf40`)
3. **each component's deserializer**, vtable slot 17 (+0x88), for the first `type+0x80` components in the type's
   component order
4. **tasks** (`0x141ee00f0`): u8 n, then n × (u16 task id + task data; an unknown id logs "Unrecognised task serialised by
   server; missing registration?")

### Component order: traced, and now derived from the .ot files

Components are built from the type's component list (`0x142427510`). The object-type constructor `0x141ec1360`
creates **Positioned first, for every type**, then loads the `.ot` (`0x142474ae0` → parser `0x142475bd0`: `extends`
first via `0x142476300`, then the file's sections via `0x142476580`, a name already present keeping its place). The
`client { }` blocks are appended after that, and `type+0x80` (the count the read loop uses) is the number before them.
The parent `Metadata/Characters/Character.ot` lists `Stats, Positioned, Pathfinding, Buffs, Life, Animated, Player,
PlayerClass, Inventories, Actor` and `client { Render, Targetable }`; with Positioned moved first that is exactly the
live order below. `python -I scripts/re/ot_components.py --check` derives it and compares it with this table (and with
AreaTransition, [08](08-area-transitions.md)); [../re/object-types.md](../re/object-types.md) lists other types.
**The live object has 12 components:**

| # | Component (vtable) | +0x88 deserializer | Reads | Minimal bytes |
|---|---|---|---|---|
| 0 | Positioned `0x1435a0f18` | `0x141e2f0b0` | yes | 16 |
| 1 | Stats (`ClientInternalStats` subclass) `0x1435dd4e8` | `0x1421c2f40` | yes | 10 |
| 2 | Pathfinding `0x1435a6ba0` | `0x1400f0f00` (ret) | no | 0 |
| 3 | Buffs `0x14359f5f8` | `0x141e114d0` | yes | 2 |
| 4 | Life `0x1435a7340` | `0x141e6c2f0` | yes | 55 |
| 5 | Animated `0x1435a5030` | `0x141e56a80` | yes | 1 |
| 6 | Player (`ClientPlayer`) `0x143466450` | `0x141e7dc10` | yes | 61 for "DevWitch" |
| 7 | PlayerClass `0x1435a0078` | `0x1400f0f00` | no | 0 |
| 8 | Inventories `0x14359e078` | `0x1415d8cb0` → own slot 20 `0x141e1ee30` | yes | 2 |
| 9 | Actor (`ClientActor`) `0x14346e558` | `0x142192a80` (+ own slot 20 `0x1421932b0`) | yes | 17 |
| 10 | Render (client) `0x143468920` | (`0x1415f8fb0`) | not reached | 0 |
| 11 | Targetable (client) `0x14346c238` | (`0x141627c30`) | not reached | 0 |

Total for "DevWitch": 1 + 1 + 16 + 10 + 2 + 55 + 1 + 61 + 2 + 17 + 1 = **167 bytes**, consumed exactly (live).

How the component classes were found (they have no RTTI): each has a static initializer in `0x14006xxxx`
(`lea rdx,"Name"; lea rcx,pool; call 0x140e33c20`), listed in an initializer table (e.g. `0x142f496a0` for
Positioned). The pool (`0x1445bb240` for Positioned) is used by exactly one other function, the **factory**
(`0x141e2e790`), whose constructor sets the component vtable. Pools: Stats `0x1445bb6f0`, Positioned `0x1445bb240`,
Pathfinding `0x1445bbe50`, Life `0x1445bbd60`, Animated `0x1445bb760`, Player `0x1445bbd10`, Inventories
`0x1445ba850`, Actor `0x1445bc610`. Factories: `0x141e40a20`, `0x141e2e790`, `0x141e7ac50`, `0x141e67110`, `0x141e54b20`,
`0x141e7d4c0`, `0x141e1ec10`, `0x141e87ec0`. The vtables found this way for Stats (`0x1435a0af8`), Player
(`0x1435a64e8`) and Actor (`0x1435aa5d0`) are not the ones on the live object; the trace is authoritative. The live
ones are subclasses that `scripts/re/ghidra/ComponentRegistry.java` now finds too (installed by functions that
also install or call the base constructor): see [../re/components.md](../re/components.md). Read sequences of every
deserializer: [schema.md](schema.md).

## 4. Each component

Offsets are inside the component object. "If bN" means that bit of the component's own flag bytes.

### 4.0 Positioned (`0x141e2f0b0`), 16 bytes minimal

| Field | Offset | Notes |
|---|---|---|
| (prefix) u32 + u32 | Sector component `+0x44` | **only if** the component type's `+0x21` flag is set ("Sector" link). Not for players |
| i32 x | +0x294 | **grid** x |
| i32 y | +0x298 | grid y. World position = `(grid + 0.5) * const` |
| f32 rotation | +0x29c | radians; turns the object's model (the Coast exit's arch uses π/2, [08](08-area-transitions.md) §5) |
| u16 | +0x1e0 | |
| u8 flags0, u8 flags1 | | |
| if f0 & 0x20: f32 +0x2b0, f32 +0x2a8, f32 +0x2ac | | +0x2b0 the uniform scale (× the object type's own, type `+0x5c`; "Scale" in the info dump `0x141e33310`; setter `0x141e35d10`, skipped when `+0x1e6` bit 4 "locked scale"), +0x2a8 / +0x2ac an x / y stretch on top of it: the local-to-world transform `0x141e361b0` takes x × +0x2a8 × +0x2b0, y × +0x2ac × +0x2b0, z × +0x2b0. The constructor sets 1.0. Sending s in all three draws at s² in x and y (Righteous Fire's ring, live 2026-10-23) |
| if f1 & 0x04: f32 +0x2d8, +0x2dc, +0x2e0, +0x2e4 | | probably a rotation quaternion (copied as one block) |
| if f0 & 0x04: i32 +0x290, f32 +0x2b4 | | **fixed height**: whole steps of 7.8125 and the world z; when `+0x290` ≠ `0x7fffffff` the z is used instead of the terrain height (`0x141e35800`) |
| if f0 & 0x40: f32 +0x2b8, f32 +0x2bc | else derived from x/y | world x, y (else `(grid + 0.5) × 250/23`) |
| if f1 & 0x01: u32, u32, u32, u8, u32, u8 | | movement (`0x141e34120`) |
| if f1 & 0x02: u8, u8 | | visibility toggles |
| if f1 & 0x08: u8 | | `+0x1e6` bit 0x40: the object blocks no walker (the collision test `0x141e61540` skips it; pets) |
| if f1 & 0x10: u32 +0x2a0 | | |

Other bits only set flags: f0 0x01 → `+0x1e5|0x10`, f0 0x80 → `+0x1e6|2` (the object keeps its rotation while it glides with 0x1ba: Righteous Fire's ring, live 2026-10-21), f0 0x02 → mount/parent lookups (stat keys
`0x7700c879`, `0x31fb1f01`), f1 0x20 → `+0x1e7|2`. The object is then inserted into the spatial index
(23×23 buckets, no bounds check: see [03](03-area-and-world.md#6-spatial-index-where-a-bad-position-crashes)).

### 4.1 Component 1 (`0x1421c2f40`, stats-like), 10 bytes minimal

compact int n, then n × (compact int key `0x141e61010`, value `0x141e490e0` → `0x1424cfaf0(stats, key, value)`), then
u32 `+0x28`, u32 `+0x2c`, u8 `+0x30`. Compact int: a first byte < 0x80 is the value, 0x80–0xbf is a 2-byte form, and so
on. The writer counterpart is `0x141fa3c50`.

### 4.1a Player Actor scale (#299)

The network Stats provider stores the supplied key/value pairs without evaluating derived actor scale.
Raw `base_actor_scale_+%` (720) alone therefore does not drive the inspected network player scale path.
Creation reader `0x1421c2f40` and update reader `0x141fe1350` reach setter `0x1424cfaf0`.
Getter `0x1424cfa40` searches its override vector before its upstream fallback.
The setter changes and notifies only the supplied key through `0x1424cfd00`.
Actor setup `0x141e9ca20` and change observer `0x141e9cf90` consume final `actor_scale_+%` (1973).
Applicator `0x141e9ce80` writes Positioned `+0x2b0` from Actor `+0x1a8 * (1 + final1973 / 100)` for players.
Its optional coefficient lookup `0x14013d9f0` is Monster-specific and defaults to one.
Positioned `+0x1e6` mask **0x04** (numeric four) locks this scale write.
Render `0x1415f4710` multiplies type scale by uniform scale, then the independent x/y stretches, and interpolates.
Player blobs retain their existing Positioned layout; no custom uniform-scale field is added.

The generic native final1973 formula is registered at `0x1400caf90` with evaluator `0x1427ecc30`.
Its other inputs are capped scale (1974), scale limit (1975) and metamorphosis permyriad scale (10778).
The native SSE order matters, including its signed-half addition before integer truncation:

```text
a = float(raw10778) / 100.0f
a = a + float(raw720)
a = a + float(clamp(raw1974, -50, 50))
v = max(-99, truncate(a + (a < 0 ? -0.5f : 0.5f)))
if raw1975 != 0: v = min(v, raw1975)
```

`Metadata/Characters/Character.ot` supplies a baseline limit100 that adds to ordinary size-limit sources.
An ordinary limit1975=-50 therefore yields limit50; an ordinary zero preserves limit100.
Cosmetic limit inputs also add before development replacements; an explicit development zero disables the upper cap.
A negative nonzero limit still applies after the lower floor, matching the native operation order.
The Gateway saturates size-source dictionary additions to signed int at each aggregation boundary.
It saturates an out-of-range float-to-int conversion before applying the formula bounds.
These deterministic domain-extreme rules avoid undefined conversion or integer wraparound.
Ordinary in-range values preserve the native float and rounding order.

`CharacterPlayerStatsCore` projects final1973 after passive, active equipment, aura, flask and cosmetic inputs.
Only size inputs720/1974/10778/1975 add across cosmetics and ordinary sources, including the metadata limit baseline.
Unrelated cosmetics retain their existing `TryAdd` precedence.
Development raw replacements apply to the whole composed input, including explicit zero.
An explicit development final1973 replaces the result and intentionally bypasses formula bounds.
Projection uses a fresh dictionary and never feeds derived1973 back into source state.
Creation, post-activation and full refresh packets share this snapshot and always include one final1973, even zero.
Their send gate reads current sources and rejects a closed or leaving owner.
Removing the last source sends final0 to restore baseline through the native observer.
Peer publication replaces only1973 in an immutable template and sends only its delta/zero to seen peers.
Peer creation, late joining and revival use the latest published scale at the observer's send gate.
Closed/leaving source sessions are rejected there without locking their simulation.
Other peer creation stats, equipment and skills retain their existing replication behavior.

Rampager graph49937 supplies raw720=10 alongside its four other stats; this does not enable Phrecia admission.
Separate flag9807 conditionally scales native melee range at `0x141c9ba55` and `0x141c9bb0a`.
Rampager and Character metadata do not grant that flag; this implementation adds no9807 mechanic.
Native collision `0x141e61540` uses Positioned `+0x1e3`, independently of uniform `+0x2b0`.
Server AoE, reach/contact, collision/navigation and projectile height/radius remain unchanged.
RF retains its separate skill-radius effect object and its `(s, 1, 1)` Positioned scale encoding.
No CharacterPanelStats scale row exists, and no extraction output or manifest version changes are needed.

Primary formula evidence is static 3.29.3b disassembly, not Path of Building.
The inspected executable SHA256 is `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`.
Eighteen native checks on an unascended Witch also measured Positioned uniform and actual Render scale axes.
The instance-only read probe observes `0x1415f4710` after its tick; its argument is the Render base+0x28 subobject.
The owner pointer is argument-0x20; published axes are Render base+0x380/+0x384/+0x388 after interpolation.
An item with raw720=20 plus real amulet/ring cosmetics20/3 gives final43 and scale1.43 on all three axes.
Removal gives1.23,1.20 and1.0; area recreation, relog and death/revival retain their surviving sources.
Raw720=10 alone leaves scale1.0; canonical final10 gives1.1, and an explicit final0 restores1.0.
The native checks also verify capped1974, metadata limit100, explicit limit0 and positive half-away rounding.
Object size remains1, type scale1 and x/y stretches1 throughout these controls.
Source `0d7a01f` was built with matching Gateway/Core/Host informational revisions and a checked listener CWD.
Its Gateway SHA256 is `94e7104156daeddf250fcecea4385b777768251ad76093e95df4fd9d4f16b37b`.
Local evidence: `logs/phrecia-completion/scale299-20261027-090346/{metadata,results}.json`.
The probe reads instance stats and render state only; it does not capture credentials or handshake buffers.
Automated send-gate and loopback-wire tests cover current/late peer creation, change, removal and revival.
A second-client visual check and native AoE, RF, reach and projectile comparisons were not performed.
Those authoritative consumers are unchanged and have regression coverage; this is not official-server geometry proof.
Phrecia class admission and full Rampage remain separate prerequisites.

### 4.3 Component 3 (`0x141e114d0`), 2 bytes minimal

u16 n, then n × (u16, u8, record `0x141e06bb0`) → `0x141e11dc0`. Probably skills (the record carries a 0x75 default).

### 4.4 Life (`0x141e6c2f0`), 55 bytes

The serialization below is separate from the component's continuous resource calculation.
The Life tick `0x141e6a190` advances native Life/Mana/ES pools using rates from the Stats interface.
The S→C `0x1b1` handler calls `0x141e69fb0` to assign decoded current and reservation inputs to a pool.
This establishes local continuous integration plus absolute incoming assignment, not an independent hit predictor.
See [client damage research](../re/client-damage-calculation.md) for fractional carry, overflow and binding limits.

Three pools at component `+0x178`, `+0x1c8` and `+0x210` (presumably life, mana and energy shield, from the HUD), each read by `0x141e6be70`:

| Field | Pool offset | Meaning |
|---|---|---|
| u32 | +0x30 | **current** |
| u32 | +0x10 | **reserved** (flat; the HUD clamps it to the maximum) |
| i16 | +0x14 | probably reserved percent |
| u32 | +0x28 | |
| u32 | +0x40 | |

Then u8 flags (`0x141e6c270`): bit0 → `+0x26a` (corpse usable), bit1 → `+0x26b` (prevent corpse sinking).
The **maximum is not in the blob**: the client derives
it from stats (Witch level 1: life 50, mana 40). Live 2026-10-16: (50, 50, …) showed "Life 50/50 Reserved 50" and
"Mana 50/40 Reserved 40"; with (current, 0, 0, 0, 0) and mana 40 the HUD shows "Life 50/50", "Mana 40/40" and full orbs.

The Life type constructor `0x141e254f0` installs vtable `0x1435a6a70`.
Its store at `0x141e25645` writes `0x01000000` to config `+0xe4`: bytes `00 00 00 01`.
Thus `corpse_usable` at `+0xe7` defaults to true and `prevent_corpse_sinking` at `+0xe4` defaults to false.
Boolean parser `0x141e67720` maps `corpse_usable` (`0x1435a8920`) and `prevent_corpse_sinking` to those fields.
The finalizer `0x141e66ec0` looks up `Player` (`0x1435a92ec`) in the object's component map.
It sets config `+0xe6` at `0x141e66f86` when that component exists; this is the ordinary corpse-use veto.
Factory `0x141e67110`, through constructor `0x141e678d0`, copies config `+0xe7/+0xe4` to runtime `+0x26a/+0x26b`.
Deserializer `0x141e6c2f0` unconditionally overwrites both runtime bytes from the trailing wire flags.
These findings come from static examination of the primary 3.29.3b `Game.exe`, SHA-256
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`.

Predicate `0x141e6c610` accepts the ordinary corpse path only with runtime `+0x26a`, no config veto and life below 1.
With a skill context, target `is_spectre_corpse` (20977) requires `skill_is_minion_skill` (6339) and no
`cannot_use_spectre_corpses` (20976); a non-minion skill or that restriction causes an early refusal.
The early refusal precedes the fallback; a null skill context does not apply the spectre restriction.
Otherwise target `is_usable_as_corpse_when_alive` (9525) provides a separate fallback, including on dead targets.
A staged boss at life 1 therefore fails the ordinary path; a living object carrying wire bit0 is not yet a corpse.

`ot_components.py` resolves inherited Life booleans and the component-derived veto in `object-types` manifest v4.
The combined format also retains inherited `positioned` footprints and `animated_object` collision models.
Version 4 supersedes the separate Life-only and collision-only v3 outputs; `hideouts` still depends on `object-types`.
Missing inheritance keeps unresolved fields null; explicit later overrides, including false, remain authoritative.
`MonsterCatalog.Variety.LifeData` binds this record through `BaseMonsterTypeIndex`.
Ordinary `SpawnMonster` passes the resolved record to `MonsterObject`, which writes the two bits unchanged.
`MonsterLifeData.IsUsableAsCorpse` mirrors the source predicate and has an optional owned-actor exclusion guard.
It does not establish final death, corpse identity, availability or consumption.

Integrated native evidence, recorded in source commit `53edc77` for the implementation at `dbfe717`, covers six cases.
A living Hungry Corpse has bit0 set but the primary predicate returns false; after actual death it returns true.
An explicitly ineligible Raised Zombie stays false after death, and Hillock's life-1 stage stays false.
A Strength Totem carries bit1 independently of corpse usability.
An owned Spectral Wolf receives bit0 false through the integrated source's separate summon runtime.
The predicate was called on the game thread in that integrated run.
This split preserves the same ordinary flag math and runtime fields; this is inherited evidence, not a native pass
on this independent branch.
The summon runtime is not included here, so the owned-wolf case does not verify standalone actor ownership or spawning.
No client, runtime probe or extraction was run for this split; an actual Raise Zombie consumer remains unfinished.

### 4.5 Animated (`0x141e56a80`), 1 byte minimal

u8 flags. Bit0: an animated-object reference (bit1 selects a u32 index into a 0x2c-byte table, otherwise a
u32-length UTF-16 path). Bit2 (`0x141e5adb0`): u32 parent object, two f32 fields, then for a nonzero parent a
u32-length byte-string bone name, u8 attachment kind, u8 flags and three 32-bit offset fields.
The stationary Crawler's wheels use zero values for these optional fields and the native axle attachment bones.
Bit3: u32, f32, u32 (animation + speed). Bit5: two u32. Bit4: several
u32. With 0 nothing else is read.

### 4.6 Player (`0x141e7dc10`), 61 bytes for "DevWitch"

| Field | Offset | Notes |
|---|---|---|
| u32 n + n × UTF-16 | +0x168 | **name** (`0x140e28d60`) |
| u8 | → `+0x158` of the player data | **class**: Characters.dat **IntegerId** (Marauder 1, Witch 3; the row put the panel on Marauder, [13](13-items.md) §6), bit5 and bits 6–7 ascendancy. Changing it fires `0x141e473f0` |
| u32 | +0x18c | **experience** (`0x141e7f6b0`). The level `+0x1ac` is derived from it (`0x142426710`) |
| u32 | +0x188 | |
| u16 | +0x1a8 | |
| u16 | +0x1aa | |
| vec16⟨u8 + 8 bytes⟩ | +0x1b8 | `0x141e83340` |
| vec16⟨u8 + 8 bytes⟩ | +0x1d0 | **league quest flags**: bit id − 1 of the client's built-in list ([21](21-hideouts.md) §1) |
| 9 raw bytes | +0x1e8 | |
| u16, u16 | → +0x1f8, +0x208 | Hideouts.dat keys by HASH16 (lookups that fail give null and are tolerated); the first is the character's hideout ([21](21-hideouts.md) §1) |
| u8 | +0x1b1 | non-zero → "WashedUp" state |
| u8 × 5 | +0x1ae, +0x1ad, +0x1b0, +0x1af, +0x238 | |
| u32 n + n × u32 | → vtable +0x158 | |
| u8 n + n × (record `0x141e840c0`, u32) | → vtable +0x160 | MicrotransactionCounters.dat |

**Updates (S→C 0x2ba, component index 6):** the vtable +0xa0 reader `0x141e7ee30` (both Player vtables) reads a u16
flag word, then one part per set bit; every part is conditional. Bit 0 = u32 **experience**, through the same setter
`0x141e7f6b0`: it derives the level again and, when it changed, sets "levelled up" (+0x2e8 = new > old) and calls the
component's vtable +0x108. Other bits: 2 and 8 the two vec16 lists, 0x200 the 9 raw bytes, 4 → +0x1a8, 0x40 → +0x1aa,
0x10 the Hideouts keys, 0x20 the class byte, 0x1000 → +0x1b0, 0x80 → +0x1ae/+0x1ad, 0x100 → +0x1af, 0x800 a u32-counted
list. `InstanceEntities.PlayerExperienceUpdate` sends `0001` + the u32 (`/set_level`).

### 4.8 Inventories (`0x141e1ee30`, through slot 17 → slot 20), 2 bytes minimal

u8 n, then n × (u8 slot type, record `0x1424f44e0`: u8 flag, 0 = empty), then u8 n2, then n2 × u16 (looked up in a
sorted list at `+0x158` and applied with `0x141ee7fb0`). Entries are registered with `0x141e1f9b0`. This component is
not the inventory manager that UI uses; that one needs 0xc0 ([05](05-inventories-and-ui.md)).

### 4.9 Component 9 (`0x142192a80`), 17 bytes minimal

| Field | Offset | Notes |
|---|---|---|
| u32 n + n × UTF-16 | | a metadata path, looked up with `0x14171ed10`. Empty for us |
| u16 | +0x21c | |
| u8 | → `0x141e96b50` | |
| u8, u8 | +0x230, +0x234 | **only if** a runtime value (`… +0x1a8 < 1`) holds. It does **not** for our player (traced: 15 bytes in the main body) |
| u32 | +0x26c | |
| u32 | +0x274 | |
| then own vtable slot 20 `0x1421932b0` | | 2 flag bytes (f0, f1). f0 0x40 → `0x142197b10`, else f0 0x20 → `0x142198800`; f0 0x01 → u16 + `0x141938bf0` record; f0 0x04 → u16 + record + u8; f0 0x08, f0 0x10 and f1 0x01 → further sections; f1 0x02 → a flag. All 0 for us |

## 5. Our blob

```
00                                     object flags
00                                     base data: 0 entries
73000000 45000000 00000000 0000 00 00  Positioned (x = 115, y = 69 in the test; the Gateway uses 197, 261, see 03 §7)
00 00000000 00000000 00                comp1
0000                                   comp3
32000000 00000000 0000 00000000 00000000, 28000000 00000000 …, (00…) × 1, 00   Life: current 50 / 40 / 0, reserved 0, flags
00                                     Animated
08000000 "DevWitch" UTF-16LE  01  00000000 00000000 0000 0000  0000 0000  00×9  0000 0000  00  00×5  00000000  00   Player
0000                                   Inventories
00000000 0000 00 00000000 00000000 0000   comp9
00                                     tasks
```

`InstanceEntitiesTests.Minimal_player_blob_layout` pins this byte for byte.

## 6. Gold

S→C **0x2a9** (class `0x14316c420`, read `0x140913050`): u32 BE, the gold shown for the character. The instance dispatcher
(case 0xe2, `0x14214dbe1`) keeps it at session +0xc40c (0 in the session constructor `0x142144b50`), where the gold
respec cost check (`0x140997b10`) and the village gold label (`0x1404ff5d0`) read it, and plays Gold_Spend.ogg when it
drops. Found with a hardware watchpoint on the field and the dispatcher's disassembly (its decompile fails). The
Gateway keeps gold per account, as in the real game where every character of an account (and league) shares it:
`AccountStash.Gold`, saved in the account's stash row of `data/saves.db` ([18](18-stash.md) §6). It sends that amount on every area
entry, on `/gold` and after a gold respec. A character save from before this still holding `Gold` gives it to the
account at the character's next login (`CharacterSaves.TakeGold`). Leagues are not kept apart yet. Gold coins on the ground are the item
`Metadata/Items/Currency/GoldCoin` (type hash 0xDDA0D6FB, `0x14031e810` plays their pickup sound); picking them up is
not done yet.

## Open

- Real meanings of the unnamed Player fields and Life +0x14/+0x28/+0x40.
- What comp1 (stats) pairs the client expects for a working character (attributes, max life …).
- Components 10/11 and the `type+0x80` count: why the loop stops after comp 9.
- Base-data entries and tasks (empty for now).
