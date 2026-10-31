# Ranged extension: concrete projectile receivers

## Scope and result

This continues the zero-time ranged extension in
[the action-extension report](client-action-extensions.md). Its receiver is the
projectile Entity held at `action+0x328`, **not the enemy tested for a hit**.
The ordinary default-arrow resource supplies five concrete components; their
update slots can now be named. The Entity-owned task list is also identifiable
as a task registry/dispatch mechanism, but the concrete tasks installed on an
individual projectile remain open.

This is a bounded static result for the existing 3.29.3b executable, SHA-256
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`,
image base `0x140000000`. No client/runtime execution, traffic acquisition or
archive modification was performed. Addresses below are VAs for that build.
The resource case is the `default_ranged`, variation-zero DefaultArrow;
arbitrary MTX, alternate variations and other skills require their own binding.

## Evidence inventory

Private reproducible evidence lives under `Tools/re/out/client-damage-audit/`:

| Audit | Coverage |
|---|---|
| `r3-projectile-extension-selector/audit.json` | Resource selector and art tables: 8 records / 8 ranges |
| `r3-projectile-extension-entity/audit.json` | Allocation, action binding, component construction and task dispatch: 7 records / 9 ranges |
| `r3-projectile-extension-components/audit.json` | Five configuration factories, concrete component constructors and three nonempty update callbacks: 16 records / 20 ranges |
| `r3-projectile-extension-clone/audit.json` | Selected action clone and explicit empty-update leaf: 2 records / 2 ranges |
| `r3-projectile-extension-resources/` | Fresh read-only extraction of DefaultArrow and its Projectile parent `.ot` files |
| `r3-projectile-extension-verify.json` | Independent original-PE instruction, slot, RIP, literal, range-hash, table-row and resource checks |

The focused verifier is `r3-projectile-extension-verify.py`; all 33 records /
39 ranges are checked against the original PE. The two unwind-less leaves use
explicit reviewed boundaries, rather than guessed unwind owners. Extracted
Stats are bound to SHA-256
`91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da`.
These are local static audits, not runtime traces or REA Evidence IDs.

## Resource selection is data driven

The startup-selected ranged table is `0x14326d930`; its selection is established
in [the action-selection report](client-ranged-action-selection.md). The relevant
entries are:

| Slot | Selected target | Role in this case |
|---|---|---|
| `+0x380` | `0x1419401e0` | Return/cache art-variation row keys |
| `+0x450` | `0x1412e2970` | Clone the action for the produced projectile |
| `+0x510` | `0x141ac9e70` | Select a Projectiles definition |
| `+0x558` | `0x1412df070` | Existing-projectile setup, then ranged extension |
| `+0x618` | `0x141bb13a0` | Allocate the projectile Entity |
| `+0x710` | `0x141bab560` | Bind a supplied projectile to an action |

`0x141ac9e70` obtains the weapon provider and tests its `+0x14c` value against
14 (Bow) at `0x141ac9ed5`. The Bow branch requests projectile index zero and
calls `0x1419417c0`. That helper obtains the action Stats provider, calls action
virtual `+0x380` at `0x14194186c`, then passes the returned keys and selection
context to `0x141f67770` at `0x1419418a4`.

The selected `+0x380` implementation caches its key vector at `action+0x100`;
when empty it calls `0x14193faf0`. That function loads
`Data/SkillArtVariations.dat`, with a verified RIP-bound UTF-16 literal at
`0x1434ba1d0`, and combines matches associated with the skill's source record.
`0x141f67770` passes the accessor `0x141937b70` into `0x141f73060`.
The accessor returns the row's vector at `+0x78/+0x80` as a begin/end pair;
the caller checks the requested index before using it. The selector obtains
the variation using the row's stat key (`+0x88`) unless an override is supplied,
then looks up `Data/ProjectilesArtVariations.dat`. Its table literal is
`0x1434ba3a0`, bound at `0x141f73229`. A resulting row provides a vector of
Projectiles definitions, from which an entry is returned.

The extracted data supplies this concrete default case. Row indices in this
table are zero based, unlike native Stats keys:

| Table / row | Relevant fields |
|---|---|
| `SkillArtVariations[5]` | `Id=default_ranged`; `ActiveSkill=[108,109,1832]`; `ProjectileArt=[0]`; `Stat=4905` |
| `Stats[4905]` | `arrow_projectile_variation`, native stat key 4906 |
| `ProjectilesArtVariations[0]` | `Id=default_ranged`; `Variant=0`; `Projectiles=[60]` |
| `Projectiles[60]` | `Id=Metadata/Projectiles/DefaultArrow`; `InheritsFrom=Metadata/Projectiles/Projectile`; arrow animated-object resource |

Full extracted-table SHA-256 values are:

| Table | SHA-256 |
|---|---|
| SkillArtVariations | `c8ea3d20e7678272ec8e836a051b81a745a53e7aeb4ca0adb670df2ca0a5ce6d` |
| ProjectilesArtVariations | `2054a1d24d70bf2e8a14d86e316434847fdd90a0824ca4d081ef512e1ccdc848` |
| Projectiles | `28d902b5a89dcb584250fca84ee970d24f973d72e262f542b22f2c7064e1a2ef` |

This closes a static data-linked resource case. It does not observe a live
character's cached key vector, active MTX or variation stat; the claim is
conditional on selecting that default row/variation, not that every Bow action
must always create DefaultArrow.

## Definition, allocation and action binding

`0x141bb66a0` calls action virtual `+0x510` at `0x141bb66b8`, stores the selected
Projectiles row at `action+0x358` and retains its owner at `+0x360`. The allocation
function `0x141bb13a0` reads that row at `0x141bb15cd`, reads its Id pointer at
`0x141bb15d4`, and loads the object definition through `0x1401b4500` at
`0x141bb160f`. For the row above, this is the DefaultArrow object definition.
The `InheritsFrom` JSON field alone is not used as proof of the `.ot` hierarchy;
the files were extracted separately below.

The allocator reserves `0x248` bytes at `0x141bb1966` and calls Entity
constructor `0x141ebdf30` at `0x141bb199a` with that definition. Entity creation
calls `0x142427510`; for each definition configuration object, this calls
configuration virtual `+8` with the new Entity in RDX at `0x14242754f`, then
stores the returned component into the Entity's component vector at
`0x142427556`. Thus the vector visited by the ranged extension is populated by
these configuration factories, rather than being the defender's components.

Before binding, `0x141bb13a0` invokes action virtual `+0x450`. Its selected
implementation `0x1412e2970` allocates a `0x530`-byte clone, calls copy helper
`0x1412439d0`, and reinstalls the **same selected table `0x14326d930`** through
the RIP reference at `0x1412e29a3` and store at `0x1412e29aa`. The allocator then
passes the new projectile Entity to that clone's virtual `+0x710` at
`0x141bb1ab9`. This prevents accidentally assigning the original table's slots
to an untyped object returned by the clone operation.

The selected binder `0x141bab560` stores the supplied Entity at
`action+0x328` at `0x141bab659`. It also subscribes the action to two separate
projectile callback containers:

| Projectile field | Callback | Registration call |
|---|---|---|
| `Entity+0xc8` | `0x141bab280` | `0x141bab684`, through `0x14019ff20` |
| `Entity+0x148` | `0x141bab4a0` | `0x141bab6b4`, through `0x14019ff20` |

Neither container is `Entity+0x58`. Those subscriptions must not be presented
as the task list dispatched by the ranged extension. Likewise `0x141bacf60`
in the `+0x558` wrapper configures an **existing** `action+0x328` projectile;
it is not the allocation function.

## DefaultArrow's concrete component callbacks

Fresh read-only archive extraction returned:

- `Metadata/Projectiles/DefaultArrow.ot`: 296 bytes, extends Projectile; overrides
  Positioned object size and client Render dampening.
- `Metadata/Projectiles/Projectile.ot`: 594 bytes, extends `nothing`; declares
  BaseEvents, Positioned, Animated, Projectile, and client Render.
- The corresponding two `.otc` paths were not found by that extraction.

Applying the already documented constructor/parser ordering rules (Positioned
first, common parent sections first, then client sections; duplicate sections
retain their existing position) gives
**Positioned, BaseEvents, Animated, Projectile, Render**. See
[the object-type tooling notes](tooling.md) and `scripts/re/ot_components.py`.

Names/factories follow the existing [component registry](components.md), with
the table and constructor bindings rechecked below. The configuration constructors install the following tables; their `+8`
factories construct components whose main tables provide the dispatched `+0x40`:

| Component | Configuration table | Factory at `+8` | Component main table | Update at `+0x40` |
|---|---|---|---|---|
| Positioned | `0x14359fb88` | `0x141e2e790` | `0x1435a0f18` | `0x141e32710` |
| BaseEvents | `0x1435a51e8` | `0x141e5e220` | `0x1435a55e8` | `0x1400f0f00` |
| Animated | `0x14359fa00` | `0x141e54b20` | `0x1435a5030` | `0x141e54d10` |
| Projectile | `0x1435a0400` | `0x141e37850` | `0x1435a0d10` | `0x1400f0f00` |
| Render | `0x1434687d0` | `0x1415ef480` | `0x143468920` | `0x1415f3b00` |

`0x1400f0f00` is the explicit `ret 0` instruction (`c2 00 00`), an empty
callback; it is not an implementation returning a meaningful numeric zero.
The other callbacks have positive work:

- Positioned `0x141e32710` processes queued position/movement state and issues
  transform/spatial notifications through further callbacks.
- Animated `0x141e54d10` processes queued effect/animation state, reaches an
  attached animated object, and can forward the delta into nested listeners
  through virtual `+0x10` at `0x141e55089`.
- Render `0x1415f3b00` advances resource/cache timers, removes expired entries,
  processes pending refresh state and calls further visual/resource helpers.

The extension supplies delta zero. That does **not** prove all of these paths
are inert: queues, already expired entries and pending state can still be
processed without advancing time. Nested callback implementations are not
exhaustively typed in this pass. The default component list contains no Life
or Stats section, but that is not proof that arbitrary nested callbacks cannot
reach other entities or resource logic.

The Render table is checked at the constructor's main-object store
`0x1415f5d9c`. A later table-shaped reference inside that large constructor
belongs to a local exception object; using the last such reference as the
component's table would produce a false identity.

## Entity-owned tasks: identified container, open installed types

Entity constructor `0x141ebdf30` initializes `+0x58/+0x60/+0x68` to zero.
The ranged extension calls `0x141edf940` on `projectile+0x58` with delta zero;
that dispatcher visits eligible entries and invokes each object's virtual `+8`
at `0x141edfa0f`. Initial empty storage does not prove the list remains empty
after component initialization, action setup or other task producers.

The registration path `0x141edf5c0` explicitly subtracts `0x58` from its receiver
at `0x141edf5de` to recover the owning Entity. It uses the u16 incoming type key,
gets the registry through `0x141643780`, resolves it through `0x141ee0ba0`,
invokes the returned factory at `0x141edf638` with the owner Entity, and registers
the resulting task through `0x141edff60` at `0x141edf6b8`.
This matches the previously recorded entity-blob task registry in
[the quest protocol notes](../protocol/12-quests.md).

That proves a typed Entity-owned task mechanism, not a damage aggregation pool
or a generic event-script VM. The [task-boundary supplement](client-entity-task-boundaries.md)
now binds local/serialized factory pairs, ownership, selected countdowns and
secondary subscriptions. It does not establish the particular tasks installed
on a live DefaultArrow or their complete callback graphs. Live installation,
external listener consequences and any numerical damage/resource connection
remain separate; the component callback identities above can be reused independently.
