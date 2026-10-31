# Client ground-effect lifetime and termination boundary (3.29.3b)

## 1. Result and scope

The registered GEAL `KillGroundEffect` callable reaches the **current context Entity's
GroundEffect component**, not an identified target's incoming-damage pool.
It can replace that component's remaining lifetime, terminate its local state, set a
conditional Buffs clock guard, cancel a retained token, and select an end animation or
request removal of its owning Entity.
These are concrete lifecycle consequences; they do not establish which damage source
instances, if any, are numerically owned by the component.

This follows the existing [native-callable boundary](client-hit-resource-boundary.md#10-geal-callable-construction-and-clientserver-eligibility).
Only the selected callable, concrete component bindings, lifetime receivers and termination
consequences were inspected.
The separate `KillGroundEffectsOfType` callable and full rendering/container implementation
were not reconstructed.
No client execution, attachment, hooks, packet acquisition, server changes or CI were used.

| Input / method | Identity |
|---|---|
| Executable | `Client/Game.exe`, 3.29.3b x86-64; preferred base `0x140000000` |
| Executable SHA256 | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| Stats SHA256 | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| GroundEffects JSON | 205 rows; SHA256 `89d7c41086c55e1b58ee975d07f762bf264b7c39c74aa4643b6342ca898bf111` |
| GroundEffectTypes JSON | 42 rows; SHA256 `5f6fe9de92f57bb803be077d96779d820f9b41092dae2364ca19e42a8031681e` |
| Evidence | Original PE bytes and unwind-owned ranges, four explicitly reviewed leaves, Capstone, bounded Ghidra export |
| Selected audit | 26 function records, 36 ranges, 1,671 instruction records, no undecoded bytes; 26 completed decompilations |

Decompilation types are aids, not original source types.
The heterogeneous callable payload is explicitly typed as float/int/int; the component
tick's `RCX` pointer and `XMM1` float, and selected getter return pointers, follow assembly.
All conclusions below retain original instructions as the authority.

## 2. Callable and receiver binding

| Binding | Original-byte evidence |
|---|---|
| Native registration | Entry `0x14449abb0`: UTF-16 name at `0x1435ae3b8`, factory `0x14208ed90` |
| Descriptor table | Factory stores `0x1435d3220` at `0x14208ee90..0x14208ee97` |
| Invoke slot | Table `+0x10 → 0x1420db7d0` |
| Exact invoke leaf | `[0x1420db7d0,0x1420db7d9)`: `add rcx,8; jmp 0x1420b5ec0` |
| Entity | Continuation calls `0x1417c53d0` using the supplied context at `0x1420b5eed` |
| Component | Literal `GroundEffect` at `0x14359dd10`; name-map lookup at `0x1420b5f24`, Entity component-vector load at `0x1420b5f46..0x1420b5f4a` |

The context resolver's direct branch accepts context tag 1 and returns the pointer stored
there; its tag-2 route resolves an Entity through the existing script-variable machinery.
There is no target-radius scan or numerical damage operand in this receiver selection.
It does not mean every GEAL caller supplies the same Entity, nor that an arbitrary contact
program has been shown to invoke this callable.
Missing GroundEffect metadata/index/component takes an error path.

The factory builds the following three-field payload at descriptor `+8`:

```text
+0 float32 delay           // argument 0; default 0
+4 int32   typeFilter      // default 42, used as the unfiltered sentinel
+8 int32   rowFilter       // argument 2; default -1
```

Before changing state, the continuation checks the type against the selected GroundEffects
row's GroundEffectTypes reference, unless `typeFilter == 42`.
For a non-null type row it derives the index by subtracting that table's begin pointer and
shifting by six, consistent with a 64-byte GroundEffectTypes row.
If `rowFilter != -1`, it must equal the signed integer at the selected GroundEffects row
`+0x10`.
This is the field exported as `__col1`; it is not the transported visual-row index.
The exact factory's type-argument parser is retained as a callee boundary; no new friendly
semantics for unnamed table columns are invented here.

## 3. The component and its mode are positively bound

Factory `0x141e19970` installs primary table `0x14359e268` and retains the owning Entity
at component `+8` and configuration at `+0x20`.
ClientInternalGroundEffect factory `0x1421c3c20` installs `0x1435dd640`, retaining those
same fields and adding a retained-entry vector at `+0x68..+0x70`.
Both initialize lifetime `+0x44` from `0x14015a670`.
That exact nine-byte helper loads `0x1436a48d4`, whose bits are **`0x7f800000`, positive
infinity**, not a duration cap or a finite maximum.

| Relevant slot | GroundEffect table | ClientInternalGroundEffect table |
|---|---|---|
| `+0x40`, lifetime tick | `0x141e19f10` | `0x1421c3e00`, calls the base tick |
| `+0x88`, initial state | `0x141e1b500` | Same |
| `+0xa0`, state update | `0x141e1b430` | Same |
| `+0xc8`, termination | `0x141e1a5e0` | `0x1421c3ea0`, calls base termination then releases retained entries |

Configuration resolver `0x141e19d20` resolves literal **Animated** into configuration
`+0x14`, and literal **Buffs** into configuration `+0x18`.
These are component indices into the same owning Entity's component vector.
The mode parser `0x141e19b10` accepts key `object_type` and binds exact string values:

| GroundEffect `object_type` | Configuration `+0x10` |
|---|---:|
| `Server` | 0 |
| `ServerOnly` | 1 |
| `ServerFill` | 2 |
| `ClientOnly` | 3 |

This is a GroundEffect configuration enum, separate from the GEAL compiler's client/server
callable-availability modes.
The [selected source catalogue](client-effect-source-catalogue.md#5-seven-ground-effect-source-configurations)
records the actual `object_type` declarations and distinct component inheritance families.
A name containing `Server` does not establish that this client performs official server
simulation.

## 4. Lifetime replacement, countdown and termination

For matching filters and normal finite delay input, the continuation does the following:

```text
if delay > 0:
    component.remaining = delay
    return
if component.ended:
    return
perform the qualified Buffs-guard / Entity-flag work
dispatch component.+0xc8()
```

The positive delay is a replacement, not `min(old,delay)`, a subtraction, or a multiplier
on the old remaining lifetime (`0x1420b5fe9`).
It can therefore lengthen or shorten the stored timer.
The positive branch precedes the ended test: it can overwrite an ended component's timer
without clearing its ended flag.
This does **not** revive that component; the tick exits immediately while ended.
No immunity to repeated delay changes follows from the callable's name.

The base lifetime tick `0x141e19f10` first rejects ended components.
Its finite countdown is exactly `remaining = float32(remaining - delta)` at
`0x141e19f78..0x141e19f7c`, followed by expiry when the result is nonpositive.
Expiry reaches `0x141e1a560`, then the same qualified guard/flag sequence and component
termination slot used by the immediate GEAL path.
There is no damage-per-minute operand in this countdown.

Positive infinity takes a separate path: it does not subtract delta.
When retained control pointer `+0x58` is absent or its integer at `+8` differs from 1,
that branch returns; when that pointer is present and the integer equals 1, it expires.
The pass does not identify the producer or every possible concrete implementation of that
retained control object.

The tick also has animation-threshold state at `+0x49/+0x4c` and animation selection
branches, separately from its lifetime arithmetic.
Neither this supplied float delta nor the existence of a component tick establishes a
fixed 33 ms duration, subframe interpolation, Temporal Chains scaling, damage speed, or
an owner-Stats-driven lifetime refresh.

### Concrete termination state

Base termination `0x141e1a5e0` checks ended `+0x48`, then writes remaining `+0x44 = 0`
at `0x141e1a609` and ended `+0x48 = 1` at `0x141e1a60c`.
Helper `0x141e1aef0` sets byte zero of a retained object at component `+0x50` to 1, clears
component `+0x50/+0x58`, and releases its shared control reference.
This proves token cancellation and ownership release, not the token's numerical purpose.
Its final shared-reference callbacks retain their own dynamic implementation boundary.

The ClientInternal override additionally releases its entire retained-entry range and sets
the end pointer back to the begin pointer (`0x1421c3ebb..0x1421c3ec4`).
The inspected range helper `0x140144930` is reference release with conditional destructor
dispatch; it does not itself reveal which instances those handles denote.
It is not justified to rename this vector a target DoT pool.

## 5. Buff clock guard and presentation/removal consequences

Before dispatching termination, the immediate callable and natural-expiry helper have the
same special branch:

```text
if configuration.object_type == Server:
    if selectedGroundEffectsRow[0xd9] == 0:
        owner.Buffs.+0xc8(1)
    if component.remaining > 0:
        owner.byte_0x8d |= 4
        component.byte_0x10 = 1
```

The byte at row `+0xd9` remains an unnamed qualifier.
The component-map provenance identifies **Buffs**, not just an arbitrary virtual `+0xc8`.
On the established Buffs table `0x14359f5f8`, that slot is the exact seven-byte
`0x141e0fbc0`: `mov byte ptr [rcx+0x17a],dl; ret`.
This is the [conditional clock advancement guard](client-buff-clock-updates.md#the-concrete-component-does-not-supply-a-missing-listener-binding),
also reached by the previously recovered [Life transition producers](client-buff-clock-producers.md).
It does not erase the Buff vector, recompute stored Buff speed, or assign incoming DoT rates.
It addresses the ground Entity's own Buffs; it does not enumerate nearby affected units.

After termination state/cancellation, the component has two concrete consequences:

- With configuration Animated index `+0x14 != -1`, it follows that owning Entity's
  Animated component `+0x260`, resolves literal **AnimationController** through
  `0x14013def0`, and resolves an animation from GroundEffects row `+0x5d`.
  If that controller/animation is already current, it returns; otherwise it requests
  the end animation through `0x141dba530`.
  The row field is the extracted table's `EndEffect` string; GroundEffects row 0 uses
  `fadeout`. This is not a uniform assertion about every Fire definition.
  This branch has a positively identified presentation receiver.
  The pass stops there rather than reconstructing the animation engine or every script
  that an animation can trigger.
- With Animated index `-1`, it calls `0x141ebe680` on the owning Entity.
  In its ordinary branch this sets Entity `+0x8c` bit zero and appends that same Entity
  pointer to a container obtained through its world `+0x190` service, at returned object
  `+0xb8..+0xc0`.
  A separate Entity-flag branch emits a diagnostic instead.
  This establishes a removal **request/queue** boundary, not completed deallocation or
  destruction of any numerically linked damage instances.

Consequently this is not merely an empty/render-only callable: component lifecycle and
the owning Buffs clock guard change too.
Conversely, finding a terminated ground visual Entity is insufficient to conclude that
client incoming-damage ownership has been recovered.

## 6. Data relations do not supply a missing damage-rate edge

Initial component reader `0x141e1b500` reads an unsigned 16-bit index, bounds-checks it
against `Data/GroundEffects.dat`, and selects a **219-byte** row into component `+0x28`.
It then calls `0x141e1b470`.
That shared reader consumes one byte into component `+0x38` and the next four raw bytes
into lifetime `+0x44`.
Its caller's float countdown establishes the latter field's interpretation.
Update slot `0x141e1b430` uses the same five-byte reader and invokes termination when
the transported remaining value is nonpositive and the component has not ended.
This is a concrete transported lifetime replacement, not a numerical DoT-rate update.
The enclosing packet identity/complete component serialization is outside this pass.

GroundEffectTypes contains `Stat` and BuffDefinition references, but their presence alone
must not be renamed damage magnitude or active Buff instantiation:

| GroundEffectTypes row | Extracted Stats row / native key | Actual Stat Id |
|---|---|---|
| 0 `Fire` | 3658 / 3659 | `ground_fire_art_variation` |
| 1 `Ice` | 6320 / 6321 | `ground_ice_art_variation` |
| 2 `CausticCloud` | 4664 / 4665 | `ground_caustic_art_variation` |

These selected references are art-variation controls.
The corresponding BuffDefinition relationships are data facts; this callable's inspected
branches do not construct Buffs from those references or publish their numerical rates.
The component, visual definition, Buff clock, source damage snapshot, incoming typed rate
and resource deduction must remain distinct identities until actual value flow joins them.

## 7. Reproduction and next concrete edges

Ignored original-byte evidence is retained under `Tools/re/out/client-damage-audit/`:

- `r8-ground-effect-boundary/`: audit, original assembly, selected bounded targets,
  typed decompilations, headless log and data identities.
- `r8-ground-effect-build.py` and `r8-ground-effect-decompile.py`: reproduce the selected
  original-byte and bounded export records; no executable is launched.
- `r8-ground-effect-verify.py` and `r8-ground-effect-verification.json`: fresh PE/range/
  instruction/RIP-literal comparison, native entry/table/leaf identities, exact branch
  anchors, table identities, distinguishing lifecycle reconstructions and report checks.

The verified lifecycle observations answer where this particular native callable inserts
and what local state it changes.
The remaining numerical question is a concrete **ground source instance → target selection
→ incoming typed rate** publisher, followed by mitigation and resource integration.
Neither the animation continuation nor the Entity removal queue closes it.
The [retained-token ownership supplement](client-ground-effect-ownership.md) now identifies
the concrete `+0x50/+0x58` producer, typed ServerFill parent route, shared cancellation flag,
identity-deduplicated retention and release/infinite-lifetime behavior.
This closes effect-lifetime ownership; numerical publication remains a separate open edge.
Follow only those source programs that actually invoke this callable or create the relevant ground instances.
Do not substitute a whole-cache keyword scan or assume every burning/caustic visual has
the same damage, overlap, snapshot or target-cap contract.
