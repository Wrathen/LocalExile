# Hit signals have captured-program listeners (3.29.3b)

## Result and scope

Four concrete native GEAL APIs install captured-program tasks on Actor's
received-hit or dealt-hit signals. Their callbacks build **different keyed
contexts** from the other Entity, a retained watched Entity, a decoded
damage-particle label and, on the dealt-hit callbacks, result mask bit `0x20`.
They then invoke the captured program through the already recovered interpreter
route. Thus the Actor signals cannot be classified as presentation-only merely
because their first recovered subscribers were Render and audio callbacks.

This closes a concrete registration, subscription and callback-input contract.
It does not recover an executing resource program, a damage amount, a local
critical trial, defender mitigation or a pool deduction. A supplied program's
accepted native operations remain relevant; this listener is not itself a
numerical resolver or evidence that every result executes such a program.

This extends [Hit feedback](client-hit-feedback.md) and the
[native captured-block evaluator](client-hit-resource-boundary.md#a-concrete-native-numeric-read-and-a-programmable-life-listener).
All evidence is offline original executable code for preferred image base
`0x140000000`, executable SHA-256
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`.
No client was executed or attached, no capture was acquired and no server/CI
change was made. Offsets below name decoded objects, not wire byte offsets.

## 1. Native entries and actual invoke methods

The retained native-array inventory is now followed through these exact
name/factory pairs and factory-installed tables:

| Native API | Array entry | Factory | Installed table / invoke `+0x10` |
|---|---|---|---|
| `AddOnWhenHit` | `0x144499bd0` | `0x14207ba10` | `0x1435d5000` / `0x1420dd1a0` |
| `AddOnWhenOtherHit` | `0x144499be0` | `0x14207be80` | `0x1435d5830` / `0x1420dcfe0` |
| `AddOnDealHit` | `0x144499bf0` | `0x14207c370` | `0x1435d17d0` / `0x1420dce80` |
| `AddOnOtherDealHit` | `0x144499c00` | `0x14207c820` | `0x1435d0860` / `0x1420dccc0` |

The first and third invokers resolve an Entity from their evaluation argument,
then get its named `Actor` component through `0x14013d8c0`. The other two resolve
a separately retained Entity and require an `Actor` component on its type.
The named getter's literal is `Actor` at `0x1435a9db0`; its final lookup is the
Entity's component-vector entry, not a pool or Stats provider.

The factories capture the supplied code block through `0x141de6420` and retain
it in the callable payload. An optional word-valued argument chooses one of two
task constructors. Those constructors differ in task flags/key setup, while
each pair installs the same concrete signal callback below. The additional
stored Boolean is not identified as a damage multiplier or trigger chance.

These bindings do not establish which resource program uses the API, that all
compilation modes admit it, or that a particular ordinary client entity has
installed one of these tasks.

## 2. Subscriptions preserve the concrete callback and receiver

| API | Task constructors, optional word / absent | Subscribed signal | Callback |
|---|---|---|---|
| `AddOnWhenHit` | `0x1420b35b0` / `0x1420b3760` | Resolved Actor `+0x3e8` | `0x14207bb90` |
| `AddOnWhenOtherHit` | `0x1420b38f0` / `0x1420b3a90` | Watched Actor `+0x3e8` | `0x14207c0d0` |
| `AddOnDealHit` | `0x1420b3cd0` / `0x1420b3e80` | Resolved Actor `+0x2e8` | `0x14207c4f0` |
| `AddOnOtherDealHit` | `0x1420b4010` / `0x1420b41b0` | Watched Actor `+0x2e8` | `0x14207ca70` |

Subscription appenders `0x1420b3500` and `0x1420b3c20` store the actual task
receiver at subscription record `+8` and the callback/member-adjustment pair
at `+0x10`. Their record tables are `0x1435cd880` and `0x1435cda10`.
Slot `+0x18` binds to original leaf thunks `0x1401459c0` and `0x140211550`.
Each checks the retained receiver and callback, applies the signed receiver
adjustment and tail-calls that callback. This is a concrete signal-to-program
binding, rather than an untyped matching vtable slot.

Target publisher `0x14019a1b0` preserves its supplied Entity and result pointers
in `RDX` and `R8` when invoking the subscription. Source publisher
`0x141e9ebb0` preserves its supplied result in `RDX` and target Entity in `R8`;
it also forwards further arguments which these four callbacks do not consume.

The existing result consumer `0x141e91580` supplies source Entity plus result
to target `+0x3e8`, including the ordinary admitted branch at
`0x141e9186d..0x141e9187b`. Later, `0x141e92072..0x141e92086` selects source
`+0x2e8` when result `+0x49` is nonzero and `+0x368` otherwise. These four
registrations subscribe to **`+0x2e8`, not `+0x368`**. Earlier admission,
special target branches and result-mask rewriting retain their previously
documented scope; the signal is not proof that a fresh local accuracy roll or
damage calculation has happened.

## 3. The four contexts are deliberately asymmetric

Each context insertion calls `0x141de8600`. Native key names have not been
recovered here; retain the exact keys rather than inventing variable names.
The following values are direct callback arguments or task fields:

| Callback | Key / GEAL type | Directly supplied value |
|---|---|---|
| WhenHit | `0x390ac1dd` / `1` | Source Entity argument |
| WhenHit | `0x1ab470a2` / `0x0b` | Label selected from result `+0x39` |
| WhenOtherHit | `0xa470b45e` / `1` | Retained watched Entity, task `+0x18` |
| WhenOtherHit | `0x390ac1dd` / `1` | Source Entity argument |
| DealHit | `0xb350ac3a` / `1` | Target Entity argument |
| DealHit | `0x1ab470a2` / `0x0b` | Label selected from result `+0x39` |
| OtherDealHit | `0xa470b45e` / `1` | Retained watched Entity, task `+0x18` |
| OtherDealHit | `0x1ab470a2` / `0x0b` | Label selected from result `+0x39` |
| Both dealt-hit callbacks, only if result `+0x36 & 0x20` | `0x7571828a` / `0x0a` | Boolean `true` |

In particular, `WhenOtherHit` does not insert the label in this body, and
`OtherDealHit` does not insert the current target under `0xb350ac3a`.
These are direct-input differences, not proof that a program cannot obtain
more information through its owner or inherited context. A clear mask bit
does not cause these bodies to insert an explicit `false` under `0x7571828a`.
The previously recovered critical-feedback use of the same bit does not turn
this callback into a critical roll.

The watched Entity is retained at task `+0x18` by base constructors
`0x141fd9de0` (optional word present) and `0x141fd9cd0` (absent); both also
establish an Entity lifecycle subscription. The
captured program's owner remains task `+0x10`, which can differ from the
watched Entity. Keeping both identities is necessary when reconstructing an
equivalent event interface.

### The label is a damage-particle category, not a damage vector

The three label-producing callbacks mask the byte with `7`, accept indices
`0..6`, and index a 16-byte pointer/length table at `0x1435d2110`. They convert
that ASCII view through `0x140e35cc0` before inserting a script string.

| Low three bits | Exact original label |
|---|---|
| 0 | `Physical` |
| 1 | `Cold` |
| 2 | `Fire` |
| 3 | `Lightning` |
| 4 | `Chaos` |
| 5 | `Any` |
| 6 | `NumDamageParticleEffectTypesValues` |
| 7 | Empty pointer/length view passed to conversion |

Index 6 is accepted by the original comparison even though its literal looks
like an enum sentinel. The upper five byte bits do not affect this lookup.
Do not silently clamp to five elements, reorder Cold/Fire to a server damage
enum, or infer component damage magnitudes, highest-damage selection or damage
conversion from this string. The original protocol field's producer/meaning
needs separate evidence.

## 4. Captured-program evaluation and task state

All four callbacks call `0x142060be0` with the new context and task `+0x10`
as owner. Their captured-block locations are task `+0x18` for WhenHit/DealHit
and `+0x28` for the two Other variants. The existing evaluator path is
`0x142060be0 → 0x141fdc2d0 → 0x141df49e0`; it is shared with the previously
recovered programmable Life listener.

If the evaluator returns true and task `+8 & 1` was clear, the callback sets
that bit and calls task virtual `+0x18`. A false return leaves the bit alone;
an already set bit suppresses this extra call. This does not independently
establish task destruction or the full meaning of the evaluator's return.
Context/string ownership and TLS restoration follow their cleanup paths.

The direct callback inputs above include no scalar damage magnitude. That
does not classify the accepted captured program as harmless or preclude
indirect reads/writes. The unresolved obligation is its actual resource
program, accepted native consequences and connection to any numerical
damage operation. Do not replace that obligation with a second traversal of
the already recovered projectile result-ingress machinery.

## 5. Verification

Ignored local evidence under `Tools/re/out/client-damage-audit/` includes:

- `r16-hit-listeners-verify.py`, `r16-hit-listeners/selected-audit.json` and
  `verification.json`: original-body ranges, exact native entries, literals,
  callback bindings, subscription slots and instruction anchors.
- `r16-hit-listeners-probe.py/json`: all four original callback instruction
  streams, every `+0x39` byte value, three representative mask bytes, both
  evaluator outcomes and both task-bit states: **12,288 controls** and
  **1,480,448 interpreted steps**.
- The probe fixtures explicitly replace context insertion, string conversion,
  captured-program evaluation, ownership cleanup, TLS storage and task
  notification. It checks original callback dataflow and branches; it does
  not run actual GEAL programs, the client or the whole subscription lifecycle.
- Independent fresh-PE review in `r16-hit-listeners-review.py/json` checks both
  watched-Entity base constructors and runs 34,816 additional callback controls
  plus 24 actual-thunk controls. These cover all result-mask bytes, all task-state
  bytes in focused controls, signed receiver adjustments, null records, distinct
  Entity identities and fixture-driven TLS restoration. Its GEAL fixture reads
  Boolean type `0x0a` from the first byte; unused payload bytes are not a Boolean
  integer. No actual captured program is executed.

The positive result is programmable, context-bearing Hit signal delivery.
Numerical Hit resolution and source-to-target DoT rate publication remain
open under the original research scope.
