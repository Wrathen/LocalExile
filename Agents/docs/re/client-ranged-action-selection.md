# Client default ranged-action selection (3.29.3b)

Default Attack does not require a different extracted ActionTypes name for a bow.
The native action factory lookup has a concrete weapon-type branch that selects
the second registry variant for `Bow` or `Wand`.
This supplement establishes that selector, its Actor provider and the two
registration sets' replacement order during the audited startup registration.
The later bow/wand factory installs table `0x14326d930`; its contact callback
consumes queued results rather than calculating a new numerical damage amount.
An end-to-end default-bow numerical damage calculation remains unestablished.

## Identity and evidence

The input is the same original `Client/Game.exe` as
[contact production](client-hit-production.md): SHA256
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`.
Addresses are preferred-image VAs with base `0x140000000`.
All findings are offline original-byte observations, using unwind ownership,
Capstone 5.0.3 and pefile 2024.8.26. No client/server execution, new traffic,
archive modification or new shared decompiler session was used.

Ignored records under `Tools/re/out/client-damage-audit/` are:

- `r3-default-bow-key-refs/audit.json`: the bounded 852/853 candidate scan.
- `r3-default-bow-provider/audit.json`: 12 function records covering the
  selector, provider construction/parser, enum resolver, setter and candidate
  action registration/construction.
- `r3-default-bow-registration-bound/audit.json`: four records for the exact
  provider leaf, configuration defaults and alternative registrations.
  The eight-byte leaf is explicitly bounded, rather than assigned a guessed
  unwind owner.
- `r3-default-bow-type-enum.json`: all 103 literal enum entries in the resolver's
  searched span, preserving names, values and entry addresses.
- `r3-default-bow-verify.py/json`: independent instruction, slot, enum and
  function-range rechecks against the original PE.
- `r3-default-bow-startup-{refs,winner,entry,continuations}/audit.json`:
  startup callback construction, both initialization bodies, existing-key
  replacement, winning factory stores, table boundaries and contact callbacks.
- `r3-default-bow-startup-verify.py/json`: focused original-byte rechecks of
  that startup and continuation chain, including both tables' valid spans.

These are SHA-bound local audit records, not REA Evidence IDs or execution traces.

## Skill identity and conditional factory lookup

The extracted-data identity remains GrantedEffects row 468 `PlayerMelee` →
ActiveSkills row 108 `melee` → ActionTypes row 47 `Melee`, hash `0xb188`.
Stat-set row 476 includes native keys 852 `skill_can_fire_arrows` and 853
`skill_can_fire_wand_projectiles`; their presence alone does not select a factory.

Lookup `0x141941c90` performs a different concrete test:

1. It tries `0x141ee88f0` using the supplied skill and source Entity's world;
   when that resolves an Entity it obtains its Actor with `0x14013d8c0`.
   Otherwise it uses the source component vector/index at `+0x10`.
2. At `0x141941cea`, it calls the Actor's virtual `+0x148`.
3. A missing Actor/provider selects variant 0. Otherwise provider `+0x14c`
   equal to 14 or 9 selects variant 1. Value 37 selects variant 1 only when
   provider `+0x150` is 9. All other cases select variant 0.
4. It searches with `{action hash, variant}` at `0x141941d35` through
   `0x1419774d0`, then calls the found record's `+8` factory at `0x141941d5c`.
   The factory receives the preserved source context, skill and original
   fourth-argument byte; the latter is not the variant selector.

The numeric labels are backed by the native literal resolver, not by assumed
JSON row numbers. `0x1423f1bb0` searches 103 entries of 24 bytes beginning at
`0x142fc8fd0`, compares UTF-16 names and returns the entry's dword `+0x10`.

| Entry VA | Literal | Returned enum |
|---|---|---:|
| `0x142fc90c0` | `Bow` | 14 |
| `0x142fc98a0` | `Unarmed` | 37 |
| `0x142fc9948` | `Wand` | 9 |

Therefore the selector does positively distinguish bow/wand weapon state.
It does not read keys 852/853 at this branch.

## Provider identity and updates

The two audited derived Actor primary tables `0x1435daa70` and `0x14346e558`
bind `+0x148` to `0x141651830`, whose exact eight bytes implement
`return Actor + 0x598`.
The base Actor table `0x1435aa5d0` instead binds the null-return leaf
`0x1400ecd70`; the derived binding must not be generalized to every Actor.

Constructor `0x142192860`, called by the concrete client factory
`0x1416640c0`, constructs the embedded provider at Actor `+0x598` and installs
table `0x1435db980` at `0x1421929b0`.
It copies config `+0x88/+0x8c` to provider `+0x14c/+0x150` at
`0x142192955/0x14219295b`.
Config parser `0x141e8ac40` binds those fields to literal keys
`main_hand_unarmed_type` and `off_hand_unarmed_type`, invoking the enum resolver
at `0x141e8b1d4/0x141e8b24f`.
Config constructor `0x141e8bf20` defaults them to 37 and 104 respectively.
These are initialization inputs; they do not establish the equipped weapon
state of a later live action.

Provider table `+8` is setter `0x142199200`.
It compares and writes both fields, calls `0x141ee7fb0` for each retained active
skill, and checks for changes in skill eligibility bytes `+0xb0/+0xb1`.
That proves a mutable weapon-state provider and a skill-refresh consequence.
The equipment-to-setter caller and selected notification producer are bound below.

### Equipment records update the same provider

The ordinary Inventories reader `0x141e1ee30` decodes a slot byte and a
record through `0x1424f44e0` into temporary records with stride `0x88`.
The latter reads a type byte at `0x1424f4520` and, when nonzero, stores it
as the record's first dword at `0x1424f45d5`. The reader applies each record
through `0x141e1f9b0` at `0x141e1f165`. This is the equipment-visible type
transport documented in [items](../protocol/13-items.md#6-equipment-on-the-character-visuals-attributes-gem-skills);
it is not a stream of authoritative item damage modifiers.

Actor constructor `0x141e8c240` resolves literal `Inventories`, obtains its
publisher at component `+0x18`, and inserts a subscription token whose first
pointer is the Actor's embedded `+0x178` interface (`0x141e8c6da..0x141e8c71e`).
The derived constructor installs table `0x14346ea20` on that interface.
Its `+8/+0x10` callbacks are `0x141e9d320/0x141e9d3b0`; both adjust back to
the Actor and tail-dispatch primary slot `+0x2a8` after their hand/stance work.

The applied-record path reaches Inventories `+0xc0 → 0x141e1fb90` on its
factory-installed table `0x14359e078`. That method dispatches removed and
added nonempty records through `0x141e287e0`, using concrete wrappers for
subscriber `+0x10` and `+8`. The reader brackets the record loop with
`0x141e28710` using wrappers for subscriber `+0` and `+0x18`.
Those two derived Actor callbacks are exact leaves `0x142196250` and
`0x142196260`: they increment/decrement the embedded interface's `+0x5e0`
(Actor `+0x758`). The latter tests the pending byte at `+0x5e1`, adjusts the
receiver by `-0x178`, and tail-dispatches Actor `+0x2a8` when pending.

On both reviewed derived Actor tables, `+0x2a8 → 0x142196290`. While Actor
`+0x758` is nonzero, this method sets pending byte `+0x759` and returns;
otherwise it clears pending, supplies zero as the second argument, and jumps
to `0x141e97190`. Thus the selected subscription has a concrete batching and
refresh path, rather than an assumed notification based on a slot number.

Refresh `0x141e97190` obtains the same Inventories component and looks up
hand records 0 and 1 through `0x141e1f310` at `0x141e9723f/0x141e9724c`.
That getter searches the sorted stride-`0x88` vector at `+0xc8..+0xd0` and
returns `record+8` only for an exact hand key with nonzero type.
Present records supply their first dword into the two type values; absent
records retain the Actor's initialized fallback types at `+0x560/+0x564`.
The reviewed refresh also has stance/morph branches, whose type loads and
guards remain part of its actual code, rather than an unconditional copy rule.

At `0x141e97621..0x141e97633` and `0x141e977a2..0x141e977b4`, the refresh
gets the Actor's mutable provider through `+0x140`, supplies the two types,
and invokes provider `+8`. Both reviewed Actor tables bind `+0x140` to
the same `Actor+0x598` getter as `+0x148`. The provider table is still
`0x1435db980`, so this is the already-audited `0x142199200` setter and its
skill-eligibility refresh, not an unrelated object with a matching slot.

This closes **decoded equipment-visible record → subscribed Actor refresh →
mutable provider setter → weapon-state selector inputs** for the selected
constructor/reader contract. It does not establish when the official server
sends such records, whole-program notification ordering, or numerical damage
production. Local `r3-weapon-equipment-verify.json` checks 34 instructions,
11 pointers and all 19 function/range records from
`r3-weapon-provider-equipment-{bound-v2,events,notify}` and
`r3-weapon-provider-notify-loop`. The preliminary `equipment-bound` audit is
superseded: the final callback leaf starts before its 64-bit receiver adjustment,
and includes both its conditional paths.

## Startup registration and replacement order

Registrar `0x141941bb0` gives its fourth argument a precise meaning: 1 inserts
variant 0, 2 inserts variant 1, and 0 inserts both.
The values are registration coverage, not evidence of client/server mode.

| Registration | Argument | Variant | Factory | Initialization order |
|---|---:|---:|---|---|
| `0x14196a070`, literal `Melee` | 1 | 0 | `0x1419bbb70` | First set |
| `0x14196a040`, literal `Melee` | 2 | 1 | `0x1419bbca0` | First set |
| `0x141262a00`, literal `Melee` | 1 | 0 | `0x1412caec0` | Later replacement |
| `0x1412629d0`, literal `Melee` | 2 | 1 | `0x1412cb0d0` | Later replacement |

The order is a positive instruction chain, not an inference from code addresses:

1. The successful startup initializer `0x140101680` constructs a callable at
   `0x140101e6f..0x140101e8e`, with table `0x142f9d200` and a captured pointer
   to its registration owner. It moves this callable through `0x140133540`.
   The no-worker branch invokes its `+0x10` at `0x140101f62`; the worker branch
   submits the same moved callable through `0x140e2f550` at `0x140101f1f`.
   The queue moves/preserves the task's callable through `0x140133540` at `0x140e2f6b6`.
   This binds the scheduled work; it is not a recorded worker execution.
2. Callable table `+0x10` is the exact nine-byte thunk `0x140151800`:
   it advances the callable receiver to its captured field at `+8` and jumps
   to `0x1400fe9f0`.
   Its copy/move slot `+8 → 0x140151810` preserves that table and capture.
3. Registration body `0x1400fe9f0` calls `0x141947e70` at `0x1400feae1`, then
   `0x141249120` at `0x1400feae6`, without an intervening conditional branch.
   The former calls the first Melee pair at `0x14195dad9/0x14195db2b`;
   the latter calls the replacement pair at `0x14125ccdf/0x14125cd31`.
   Each named registration has its own static-initialization guard.
4. Both use the registry returned by `0x141941e50`, global `0x144755188`.
   Existing-key search/insertion `0x141977330` branches to `0x1419774b5` on
   a matching hash/variant and returns that record's payload at `0x1419774ba`.
   Registrar `0x141941bb0` then unconditionally writes the factory and metadata
   callback to that payload at `0x141941c46/0x141941c49` or
   `0x141941c72/0x141941c75`.

Thus, after this audited registration pass, the second pair replaces the first
pair for both Melee variants. The earlier tables remain useful superclass and
candidate evidence; they are not the final startup registrations.
This is a static startup contract, not a universal claim excluding later
computed registry writes or proof that a live bow shot traversed the branch.

## Winning factories, bounded tables and contact continuation

The ordinary branches below follow the wrappers' mine/trap/totem checks.
Those wrappers remain distinct and are not claimed to follow the same tables.

| Variant | Winning factory and construction | Final primary table | Bounded end |
|---|---|---|---|
| 0 | `0x1412caec0` allocates `0x438`; calls `0x141c98150`, constructs extension `+0x430` through `0x1412d50e0`, then performs final table store `0x1412cb019` | `0x1432e5718` | `0x1432e5ca8` (`+0x590`) |
| 1, including Bow/Wand | `0x1412cb0d0` allocates `0x530`; calls `0x141ac9dc0`, then overwrites the primary table at `0x1412cb1c3` and constructs extension `+0x520` through `0x141378630` | `0x14326d930` | `0x14326e0e8` (`+0x7b8`) |

The ends are established adjacent-table starts: `0x141370110` installs
`0x1432e5ca8` at `0x141370146`, and `0x1412e1160` installs `0x14326e0e8` at
`0x1412e1174`. Bytes at or beyond those ends must not be interpreted as another
slot of the preceding action table.

The winning variant-0 table retains `+0x520 → 0x141c98700`, empty
`+0x530 → 0x1400f0f00`, and feedback `+0x2d8 → 0x14136f210`.
However, it changes the follow-on record's `+0x548` from `0x141c999b0` to
`0x14126ec80`. That wrapper may call extension helper `0x1413c07a0`, then
applies target eligibility checks before tail-calling `0x141939f40` with its
Entity ID. The [extension follow-up](client-action-extensions.md) binds its
art selection, target Render, attached object and controller write; the
delegated lifecycle effects remain a separate boundary.
Other changed valid slots are `0`, `+0x90`, `+0x458`, `+0x498`, `+0x4a8`
and `+0x550`; their presence prevents equating the complete old and new actions.

The winning variant-1 table retains the concrete shared paths:

- `+0x618 → 0x141bb13a0`, flight and spatial-contact production.
- `+0x570 → 0x141b6f950`, with `+0x778 → 0x1400ed4a0` selecting the false
  accuracy-mode flag, as in the earlier candidate table.
- `+0x530 → 0x1412e34e0`, which calls `0x14193a170` at `0x1412e34e9`, and
  repeats it under virtual `+0x498`. The callee advances phase byte `+0x120`,
  selects the action's existing Actor `+0x200..+0x208` queue range using
  `0x141e9f160/0x141e9f090`, and invokes `0x141e91330` at `0x14193a296`.
  It does not forward the tested target to a new numerical hit calculator.
- Feedback slots `+0x2d0/+0x2d8` remain the empty `0x1400f0f00` leaf.

Its changed `+0x558 → 0x1412df070` first invokes the inherited projectile
setup/launch helper `0x141bacf60`, then passes the retained projectile Entity to
extension helper `0x1413789e0` with `action+0x520`.
Changed `+0x590 → 0x1412df7c0` only calls `0x141374480` when its input flag is
false and the resolved source Actor provider's main-hand type is `Bow` (14).
The latter is the attachment chain bounded in [contact production](client-hit-production.md),
not a demonstrated numerical damage producer.
Other changed valid slots are `0`, `+0x20`, `+0xf8`, `+0x140`, `+0x248`
and `+0x450`; their complete consequences remain outside this pass.

The selected equipment-to-provider setter call is now bound. The
[extension follow-up](client-action-extensions.md) identifies melee attachment
outputs and a Bow-only zero-time update of the retained projectile, whose
concrete component/callback effects remain open.
The startup-selected default-bow contact continuation is now bound, but its
audited `+0x530` consumes already queued results. A local numerical hit producer
or complete Hit-to-resource-loss chain remains Partial.
