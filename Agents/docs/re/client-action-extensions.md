# Startup-selected action extensions

## Scope and result

This follows the extra callbacks installed by the **startup-selected** default
melee and ranged actions. The factory, replacement order and bounded table spans
are established in [the action-selection report](client-ranged-action-selection.md).
The question here is whether their previously unexpanded extensions supply a
numerical damage producer or mutate the tested target's resources.

The melee extension positively reaches an art-variation lookup, the tested
target's Render component, a hit-bone selector, an attached animated object and
an AnimationController parameter. The ranged extension positively requests a
zero-time update of the **produced projectile's** components and callback list.
Neither bounded chain closes a numerical damage producer or Life/ES/Mana
deduction. The [projectile receiver supplement](client-projectile-extension-receivers.md)
now binds the conditional DefaultArrow resource, allocation/action clone and five
component update targets. Installed tasks and nested listeners remain separate;
generic dispatch cannot be classified as entirely presentation-only.

All findings are static artifact observations for the existing 3.29.3b executable,
SHA-256 `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`,
image base `0x140000000`. No runtime execution, traffic acquisition or game-file
modification was performed. Native addresses below are VAs for this build.

## Evidence inventory

The ignored evidence directory is `Tools/re/out/client-damage-audit/`:

| Audit | Coverage |
|---|---|
| `r3-action-extensions-seeds/audit.json` | Winning wrappers and their two extension constructors/entry points: 6 records, 8 unwind-owned ranges |
| `r3-action-extensions-consequences/audit.json` | Art lookup, Render selector, attached-object setup, controller parameter and Entity callback dispatch: 7 records, 11 ranges |
| `r3-action-extensions-attachment/audit.json` | Render resource gate, object creation, resource lookup, named AttachedAnimatedObject getter and attachment registration: 5 records, 5 ranges |
| `r3-action-extensions-verify.json` | Independent checks of 51 instruction anchors, 6 literal byte strings, 6 instruction-bound RIP references, original-PE range hashes and ownership for all 18 records / 24 ranges |

The focused verifier is `r3-action-extensions-verify.py`. It also rechecks the
extracted Stats digest
`91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da`
and native-key-to-row identities. These local audits are reproducible original-PE
checks, not runtime traces or REA Evidence IDs.

## Melee: art selection and target-attached feedback

### Entry and selected resource

The winning variant-0 table `0x1432e5718`, slot `+0x548`, points to
`0x14126ec80`. That wrapper reads the extension pointer at `action+0x430`,
obtains a float through action virtual `+0x4b8`, and calls `0x1413c07a0` at
`0x14126ecb3` with the extension and tested target Entity. Its subsequent
eligibility check and tail-call to `0x141939f40` are separate continuations;
discovering an attached effect does not replace that action-result path.

The factory constructs this extension through `0x1412d50e0`, which allocates
`0x68` bytes, installs table `0x143326848` and calls `0x1413beb20` with output
`extension+8`, a skill-associated resource pair and the action Stats provider.
The constructor also reads native stat 10880, `supress_weapon_surges`, into
`extension+0x2f`. That stored flag is not itself evidence of a damage operation.

`0x1413beb20` reads native stat 349, `skill_art_variation`, at `0x1413beb4d`.
Its RIP reference at `0x1413beb6a` resolves to the UTF-16 literal
`Data/Melee.dat` at `0x14332ab00`. It loads that table through `0x1413ccd00`,
then looks up the pair of resource identity and art-variation integer through
`0x14225e4b0`. A missing entry causes a second lookup with variation zero.
The result is a selected table-row/resource-owner pair stored at `extension+8`.
This establishes an art resource selection, rather than a damage amount lookup.

### Contact effect and attachment

`0x1413c07a0` performs the following bounded operations:

| Stage | Original-PE evidence |
|---|---|
| Find target Render | Calls `0x14013fa90` at `0x1413c07d7`; the getter references ASCII `Render` at `0x14346a1c4` |
| Read selected effect | Takes the selected row's resource pair at row `+0x14`; a null resource exits |
| Select on-hit bone | Calls `0x1415fe130`; sentinel `-3` and `-4` enter the diagnostic branches below |
| Set up attached object | Calls `0x141604300` at `0x1413c0a90` with Render, resource pair and selector |
| Find effect controller | Calls `0x14013def0` on the returned object; the getter references `AnimationController` at `0x143593330` |
| Set controller parameter | Takes the reciprocal of the incoming action factor, passes it through helpers with bounds `5.0` and `1.0`, and calls `0x141dbde00` on `AnimationController+0x180` |

The diagnostics are exact executable strings, each bound to its branch by a
verified RIP instruction:

- `0x14332a9d0`: `MeleeAttackActionClientImpl::ContactAttack on_hit_bone Invalid for {}`.
- `0x14332a940`: `MeleeAttackActionClientImpl::ContactAttack on_hit_bone Unset for {}`.

These support identifying the selector as a contact-effect attachment bone.
Random selection within `0x1415fe130` is consequently not evidence of a
numerical damage roll.

Inside `0x141604300`, `0x141604040` searches the Render-owned resource records
at `+0x2c8..+0x2d0`, can reject an existing record based on its float field and
Render `+0x2fc`, or creates a record initialized from Render `+0x2f8`. This is
an attachment-resource gate/cache; it is not the resource loader or a damage cap.
The branch then resolves the selected resource's path through `0x140d1fa30`
and creates an object/owner pair through `0x140d235f0`. The latter allocates
`0x308` bytes and calls `0x141da6360` to construct the object at allocation
`+0x10`.

The component getter `0x140cbf280` references ASCII `AttachedAnimatedObject`
at `0x1435955f0`. `0x141604300` calls it before registering the created object
through `0x141dc31c0` at `0x14160451c`. That registration configures an attachment
record, copies transforms and can notify listeners. The same registration
family appears in the separately bounded [mortar attachment path](client-hit-production.md).
This common callee does not identify every callback in either path.

Finally, `0x141dbde00` writes the supplied finite, nonnegative parameter to
receiver `+0x30` at `0x141dbde30` and notifies controller listeners through
`0x14175ef90`. In this call the receiver is `AnimationController+0x180`, so
the concrete write is to the created object's controller `+0x1b0`.
An animation playback parameter is the supported interpretation; this is not
a write to the tested target's Life, ES or Mana component.

The selected object definition, its component constructors and attachment/
controller listeners are not exhaustively expanded here. The positive finding
is the resource/Render/attachment/controller chain. It does not establish that
all delegated lifecycle behavior is presentation-only.

## Ranged: Bow-only zero-time update of the produced projectile

The winning variant-1 table `0x14326d930`, slot `+0x558`, points to
`0x1412df070`. At `0x1412df08f` it calls inherited projectile setup/launch helper
`0x141bacf60`, which requires the already retained projectile; it does not
allocate that Entity. The wrapper then reloads the **projectile Entity** from
`action+0x328` at `0x1412df094`. It passes that Entity, the embedded extension
at `action+0x520` and a preserved original stack byte to `0x1413789e0`.
This receiver must not be confused with the enemy target of a contact test.

The extension constructor `0x141378630` asks the source Actor's virtual
`+0x148` for its weapon provider, reads provider `+0x14c` and compares it
with enum value `14` at `0x141378695`. The action-selection report independently
binds `14` to Bow. The constructor stores the comparison result at
`extension+8`. Missing provider uses fallback `0x68`, which fails the Bow test.

`0x1413789e0` runs only when that stored Bow predicate and the incoming byte
are both nonzero. Its entire owned body then does two dispatches:

1. Enumerates the produced Entity's component pointers in `+0x10..+0x18` and
   invokes each component's virtual `+0x40` at `0x141378a22`, with `XMM1=0.0`.
2. Invokes `0x141edf940` at `0x141378a35`, with receiver `Entity+0x58` and
   the same zero delta.

`0x141edf940` traverses a collection of 16-byte entries. Eligible callback
objects are selected by flag bit `2`, removed objects are excluded by bit `1`,
and each active object's virtual `+8` is called at `0x141edfa0f` with the
supplied delta. Dispatch-state bookkeeping and deferred removal follow.
This is a callback-update collection, not an identified DPS aggregation loop.

The observed behavior is therefore a **zero-time component/callback update**.
Zero delta alone cannot establish absence of effects: an update can initialize
state, dispatch queued work or execute an operation independent of elapsed time.
The [projectile receiver supplement](client-projectile-extension-receivers.md)
binds the conditional DefaultArrow resource to Entity creation, the same-table
action clone, `+0x710` binding and `action+0x328`. It names five concrete component
update slots and identifies `Entity+0x58` as an Entity-owned task container.
Installed task types and nested listener consequences still require their own
bindings; zero-time updates and initial empty storage are not absence proofs.

## Remaining research boundary

| Edge | State after this pass |
|---|---|
| Winning melee extension entry and resource selection | Bound through original PE, native stat identity and exact table literal |
| Target contact attachment and controller write | Bound through named component getters and concrete write address |
| Complete effect-definition/lifecycle listener consequences | Open; registration and controller listener dispatches are not exhaustively typed |
| Winning ranged extension's receiver and zero-delta arguments | Bound to the produced projectile, not the enemy target |
| Conditional DefaultArrow component `+0x40` identities | Bound in the projectile receiver supplement; nested listeners remain open |
| Entity-owned task `+8` consequences | [Selected timers and secondary subscriptions](client-entity-task-boundaries.md) bound; live installation and external listener effects remain separate |
| Numerical hit-damage production and target-resource deduction | Not closed by these extensions; remain the original R3 question |

These narrower results should be combined with the ongoing
[hit-production investigation](client-hit-production.md), rather than used to
mark its producer or pre-deduction edges complete.
