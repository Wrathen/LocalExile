# Hit-to-resource boundary: receiver audit (3.29.3b)

The concrete numerical resource-loss receivers are identified, but an independent
**Hit → these receivers → deduction** path remains unresolved. In particular,
an indirect call with displacement `+0x48` is not enough to identify resource loss.
The Life **component** and its embedded **resource pools** use different tables
and assign different meanings to that same displacement.

This is an independent static supplement to
[client damage calculation](client-damage-calculation.md#why-a-direct-call-negative-result-is-insufficient).
It does not establish official-server execution, live client prediction, or
global absence of hit calculation. No client or server was executed, attached,
hooked or controlled for this pass.

## 1. Identity and method

| Input | Identity |
|---|---|
| `Client/Game.exe` | SHA256 `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| Preferred image base | `0x140000000`; addresses are not live ASLR addresses |
| `data/tables/Stats.json` | SHA256 `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| Decoding | Capstone 5.0.3, pefile 2024.8.26; PE unwind roots plus chained ranges |
| Leaf exception | Explicit reviewed range `[0x1400f0f00,0x1400f0f03)`, bytes `c20000` (`ret 0`) |

The pass reused [damage_audit.py](../../../scripts/re/damage_audit.py).
Generated evidence stays under ignored `Tools/re/out/client-damage-audit/`:

- `r3-loss-receiver-boundary/audit.json`: 13 decoded bodies, range hashes,
  25 direct reducer references, four constructor/table LEAs and three raw reducer pointers.
- `r3-loss-vtables.json`: four exact table spans and leaf previews.
- `r3-loss-candidates.json`: preceding 55 linear instructions for the existing
  667 displacement-`0x48` candidates; this is triage, not reaching-definitions analysis.
- `r3-loss-materialized48-records.json`: 53 additional register-target candidates,
  load/dispatch bytes, context and source ranges/hashes for 43 owners.
- `r3-loss-{peek,vtables,materialized48,records}.py`: local analysis helpers.

These are original-byte audit records, not REA Evidence IDs. No new native REA
session was opened. The action/event follow-up below used a separate offline
Ghidra project, with selected decompilation completing for all four requested
bodies; its temporary project was deleted after export. Decompiled signatures
are provisional; the ABI claims below follow decoded instructions. The audit
checked instruction boundaries; it did not perform whole-program CFG/value-flow
analysis.

## 2. Proven table and object binding

Constructor `0x141e678d0` stores the owning Entity at component `+8`, then installs
the Life primary table at `0x141e67930..0x141e67937`. Its three calls to the pool
base constructor `0x141e65df0` pass the Life component as `RDX` and the embedded
pool address as `RCX`; each is immediately followed by its concrete table store.

| Receiver | Address relative to Life | Installed table | Installation | Relevant slot |
|---|---|---|---|---|
| Life component | `+0` | `0x1435a7340` | `0x141e67937` | `+0x40 → 0x141e6a190`; `+0x48 → 0x1400f0f00` |
| Life pool | `+0x178` | `0x1435a5da0` | `0x141e679c1` | `+0x48 → 0x141e6a8a0` |
| Mana pool | `+0x1c8` | `0x1435a5b68` | `0x141e67a0b` | `+0x48 → 0x141e6ae00` |
| Energy Shield pool | `+0x210` | `0x1435a6218` | `0x141e67a4c` | `+0x48 → 0x141e6a780` |

The loss functions consume `RCX=pool`, `XMM1=delta`, `R8b=reason`,
`R9b=nonlethal`. Their common numerical reducer is `0x141e6a6b0`.
Its integer-current/fractional-carry behavior and the continuous step functions
are described in the main report. The table installation and reducer bodies
establish this ABI; matching an arbitrary float argument does not establish it.

The pools also have a distinct integer-delta interface at `+0x28` and an absolute
setter. [Cost and arrow-sacrifice callers](client-integer-resource-mutations.md)
positively bind numerical changes through those interfaces. Their caller gates,
reason codes and integer ABI remain separate from floating damage and ordinary
accepted-Hit prediction. The earlier `+0x48` receiver search was not an inventory
of every numerical resource-write mechanism.

The additional [terminal Mana-refund audit](client-terminal-mana-refunds.md)
binds two conditional numerical slices to the actual Mana integer method and
setter. One is installed in concrete FireMortar tables; the other's actual
incoming caller is still unbound. Neither supplies target Hit loss. The bounded
Life-accessor caller search is navigation, not a whole-program receiver census.

A raw eight-byte-pointer search finds each concrete reducer exactly once, at
its table's `+0x48`: `0x1435a5de8`, `0x1435a5bb0`, `0x1435a6260`.
The boundary-checked RIP-LEA search for the four tables finds the four constructor
sites above. This supports this binding; it does not rule out dynamically copied
tables, computed addresses, different instruction forms or another object implementation.

The Life primary table also assigns `+0xf8` and `+0x168` to `0x1400f0f00`.
Thus the Life reducer's virtual callbacks at those slots are empty on the object
constructed with this table. The `reserve_life_instead_of_loss_from_damage_for_x_ms`
branch alone does not prove that a meaningful deferred-damage callback executes
on that object. A different table installation would need separate evidence.

## 3. Concrete indirect receiver examples

Three attractive candidates pass a float in `XMM1` and call `+0x48`, but the
receiver is loaded directly from an Entity's component vector at `+0x10..+0x18`:

| Callsite / owner | Actual receiver and input |
|---|---|
| `0x14167a111` / `0x14167a040` | `RCX = Entity.components[index]`; `XMM1` is a selected float from the enclosing iteration, or zero |
| `0x142184b81` / `0x142184ae0` | `RCX = Entity.components[index]`; `XMM1` is selected through the two input pointers after world/entity flag tests |
| `0x141769857` / `0x141769210` | `RCX = Entity.components[index]`; requires `[RCX+8] == Entity`, then explicitly zeros `XMM1` |

When the element is the Life component constructed above, these dispatches land
in `ret 0`, not any pool-loss reducer. The vectors may contain other components;
this finding does not resolve all their dynamic element types or claim that
every component's slot is empty. It does prove why component iteration must not
be mistaken for a call on `Life+0x178`, `Life+0x1c8` or `Life+0x210`.
The ordinary Life integration function instead occupies component slot `+0x40`.

Other selected float-nearby candidates also fail to establish a pool receiver:

| Callsite / owner | Observed boundary |
|---|---|
| `0x14163dc18` / `0x14163dbc0` | Incoming object at `RCX`; decreases its `+0x18` float using a rate at `+0x1c`, invokes `+0x48` on expiry, then sets a flag and invokes `+0x18`. No embedded-pool provenance is established. |
| `0x141eb74e1`, `0x141eb754f` / `0x141eb7470` | Incoming object's `+0x18` float and `+0x1c` state drive `+0x48`; transitions to state 4 and an object-removal-style flag/slot-`+0x18` sequence. The first float load is a threshold comparison, not proved resource delta. |
| `0x141c0cbba` / `0x141c0c6b0` | Receiver is a vector element with a tag check `[receiver+8]==0x10`; preceding `XMM1` computes a sampling/index ratio, then is converted to an integer. Return `RAX` is tested and dereferenced at `+0xc`; no pool binding is established. |
| `0x141fe6306` / `0x141fe5fd0` | Receiver is the incoming object, whose owner Entity is at `+0x10`; preceding work traverses spatial coordinates/terrain thresholds. No embedded-pool receiver is established. |

The expiry/state observations are structural descriptions, not recovered class names.

## 4. Direct numerical ingress is bounded and known

The fresh E8/E9 instruction-boundary scan of the common reducer and three pool
reducers finds **25 callsites in seven owners**, with no unowned candidate for
those exact direct target encodings:

| Owner | Direct destinations and callsites |
|---|---|
| Spending `0x141e6a370` | Common at `0x141e6a4b4`, `0x141e6a4f0`, `0x141e6a517`, `0x141e6a533`; Life at `0x141e6a4ce` |
| ES reducer `0x141e6a780` | Common at `0x141e6a7b6`; Mana at `0x141e6a809`, `0x141e6a84c`; Life at `0x141e6a86f` |
| Life reducer `0x141e6a8a0` | Common at `0x141e6abb0`, `0x141e6ac55` |
| Mana reducer `0x141e6ae00` | Common at `0x141e6ae2c`; Life at `0x141e6ae51` |
| Life step `0x141e6ae70` | Life at `0x141e6afa1`, `0x141e6afda`, `0x141e6b010`, `0x141e6b04a` |
| ES step `0x141e6b480` | ES at `0x141e6b54a`, `0x141e6b566`; Common at `0x141e6b588` |
| Mana step `0x141e6cba0` | Common at `0x141e6cc9e`, `0x141e6cce8`, `0x141e6cd4b`; Life at `0x141e6cd06`, `0x141e6cd6a` |

These are continuous integration, spending and overflow routes. The audited
instructions establish numerical deduction, but no independent Hit input or
hit-result value has been connected to one of these calls.

## 5. Coverage and unresolved questions

The original FF /2,/4 memory-indirect scan contains **667 sites in 448 owners**,
plus **21 unowned candidate byte positions**. All 667 received a preceding-55-
instruction context pass; this did not resolve 667 receivers. In that window,
144 sites have a syntactic `XMM1` write and 165 have syntactic writes to both
reason/nonlethal registers. Writes can be stale across calls or branches.
Only 17 windows contain `+0x178`, `+0x1c8` or `+0x210`; these are not proof that
the base register is Life. The three component examples above were reviewed by
actual operand flow rather than these counts.

An additional scan covers a previously omitted form: opcode `8b` loads from
memory displacement `+0x48`, followed within 16 linear decoded instructions by
`call reg` or `jmp reg` using the loaded register. It stops at an intervening
call, branch, return or detected destination write. It found **53 sites in 43
owners**, none in the Life-local range `[0x141e60000,0x141e70000)`.
The **560 unowned byte candidates** in that pass are load candidates, not calls.
Inspected contexts include packed integer color arguments, stack/object pointer
arguments, global callback tables and function-pointer fields directly inside
objects. No new embedded-pool receiver was established. A target load from an
object's own `+0x48` field is not necessarily a virtual-table access at all.

Both scans remain bounded: no cross-block reaching definitions, callee summaries,
complete pointer-escape analysis, computed table addresses, arbitrary target copies,
complete prefix coverage or unwind-less-leaf discovery. The register-target
helper does not fully normalize GPR subregister aliases; retained candidates
therefore require manual inspection. No absence claim follows from the linear
`XMM1` ranking or the zero Life-local candidate count.

| Research question | Status |
|---|---|
| Which concrete objects own loss slot `+0x48`? | Answered for the three constructor-installed embedded pool tables. |
| Do the three component-vector calls enter those pool reducers for Life? | No; on the identified Life component table they invoke an empty function. |
| Are direct numerical loss callers accounted for? | Answered for the stated E8/E9 target scan: 25 sites, seven owners. |
| Is any indirect caller proved to supply Hit loss to an embedded pool? | Unresolved. |
| Does every possible indirect path exclude a Hit calculation? | Unresolved; the scans are not a whole-program proof. |

Closing R3 still requires a concrete input/result producer, defender identity,
receiver provenance to an embedded pool (or another numerical setter), and the
value flow for amount/reason/nonlethal. RNG, mitigation and routing must be linked
to that same path before calling it independent client hit prediction.

## 6. Reproducible source anchors

The following exact original-file spans support the new receiver distinctions.
End addresses are exclusive. Chained function portions are named as portions;
they are not silently widened into complete functions.

| Purpose / VA span | File offset | SHA256 |
|---|---:|---|
| Life constructor `[0x141e678d0,0x141e67ac4)` | 31878352 | `2edc7dd34048b12b220f92aefd34a6b9854aeebe6764aa151142a5f19e32e0fb` |
| Empty leaf `[0x1400f0f00,0x1400f0f03)` | 983808 | `b6cbececf1b07e74f37d57710c68c31f0b92cb08d51093252b321b07542e177b` |
| Component loop portion `[0x14167a062,0x14167a14a)` | 23565410 | `3ca461fb7596b3a1d95f770d5fee05ea9528bdbb56f804f41ac28e9e1d59750c` |
| Component loop `[0x142184ae0,0x142184bad)` | 35143392 | `4d2083116fe16776ccdfb6576d740220b19d789153befa9a3ade7c9f1c6f9c51` |
| Zero-float component dispatch portion `[0x14176969e,0x1417698eb)` | 24545950 | `45181755975b8b176bb99dda67c684289365e03006ab6796ea681260357d6cff` |
| Expiry candidate `[0x14163dbc0,0x14163df4b)` | 23318464 | `27c92c6dd6f4bb9131eef4a206a0744145a615350e2a82460095b7832b69f485` |
| State candidate `[0x141eb7470,0x141eb756e)` | 32204912 | `e0236416a67c4dd08ff91a14923af2391253448f8b7b518ddd49b87e08243f98` |
| Sampling candidate `[0x141c0c6b0,0x141c0cf0c)` | 29407920 | `ec3c94c8393b1e09a2cc12b64546a2eb1bacdc05392a07841d906d7887d4d105` |
| Life primary table `[0x1435a7340,0x1435a74b8)` | 56256320 | `64d77b7db7b79b12fe30e106e2b04a4c5d95bc3f4c9add54e07cd8152a4b1aef` |
| Life pool table span `[0x1435a5da0,0x1435a5e28)` | 56250784 | `7a93ee6a7effd8130bb2ef5c44ea1d2abee1ddea2cd283bfbc40dfc307ae6955` |
| Mana pool table span `[0x1435a5b68,0x1435a5bf0)` | 56250216 | `0cea5d7c1cbab2f5f0f2e60749d8328776de8ab512daf2bc0aca91788cf4ec78` |
| ES pool table span `[0x1435a6218,0x1435a62a0)` | 56251928 | `51334c88ad76507fe58236218f347aad9391d32ef007390ba1b4fe0ab4927ca4` |

The table spans are selected byte ranges, not claims that the following address
starts another complete object. Full chained ranges for every audited body and
the 43 additional candidate owners are retained in the local records named above.

## 7. Concrete action callbacks: Fireball and Melee

The result consumer `0x141e91580` calls action slots `+0x2d0` at
`0x141e92053` and `+0x2d8` at `0x141e920ae`, with `RDX=result` and
`R8=resolved target Entity`. Both require a matching action and nonzero
`result+0x49`. A base Action table with empty slots cannot settle the concrete
dispatch. Named registrations and their factory stores give these bindings.
The Melee entries here belong to the earlier registration set; the later
[startup winners](client-ranged-action-selection.md#startup-registration-and-replacement-order)
install `0x1432e5718` and `0x14326d930` respectively, retaining these feedback
callbacks while changing other slots. The earlier full tables cannot be treated
as the final startup actions.

| Action / registry variant | Named registration → factory | Final table / installation | `+0x2d0` | `+0x2d8` |
|---|---|---|---|---|
| Fireball / 0 and 1 | `0x141973cd0 → 0x141988dd0` | `0x143335d80` in constructor `0x141a8b150`, store at `0x141a8b17e` | `ret 0` | `ret 0` |
| Melee / 0 | `0x14196a070 → 0x1419bbb70` | `0x143517ba0`, final factory store at `0x1419bbc67` | `ret 0` | `0x14136f210` |
| Melee / 1 | `0x14196a040 → 0x1419bbca0` | `0x143273730` in constructor `0x141ac9dc0`, store at `0x141ac9de3` | `ret 0` | `ret 0` |

The Fireball row likewise records the **base** factory, not the final client specialization.
Client factory `0x14128d9f0` calls that same constructor and then replaces the table with
`0x14333ac40` at `0x14128dadb/0x14128dae2`; its clone restores that client table too.
Both result slots above remain the same exact no-op on the client table, but clone/terminal
slots differ. See the [factory correction and compared slots](client-hit-production.md#concrete-action-binding).
Do not generalize the earlier whole base table to the final client action.

Here `ret 0` is the exact leaf `0x1400f0f00`. The selected factory branches
are the ordinary non-mine/trap/totem cases; wrapper branches through
`0x1419e2e50`, `0x1419e2ef0` and `0x1419e2fa0` remain unexamined. Melee variant 0
temporarily installs `0x14355e290` in constructor `0x141c98150`; its later
factory store is essential. No RTTI class name is inferred from these tables.

The data references are zero-based: GrantedEffects row 0 `Fireball` points to
ActiveSkills row 84 `fireball`, which selects ActionTypes row 36 `Fireball`,
`HASH16=43598 (0xaa4e)`. GrantedEffects row 468 `PlayerMelee` points to
ActiveSkills row 108 `melee`, which selects ActionTypes row 47 `Melee`,
`HASH16=45448 (0xb188)`. The constructors pass those exact action identifiers.
GrantedEffects row 406 `Melee` also selects ActiveSkills row 108.

Lookup `0x141ee97f0 → 0x141941c90` uses the skill identifier at `+0x40`
and action hash at `+0x42`, searches the registry through `0x1419774d0`, and
invokes the selected entry's `+8` factory. Registrar `0x141941bb0` hashes the
supplied ASCII name with multiply-then-XOR FNV starting at zero and folds its
high/low 16-bit halves. Coverage argument 0 inserts both variants, argument 1 only variant 0,
and argument 2 only variant 1. The lookup derives the variant from Actor virtual
`+0x148` data fields `+0x14c/+0x150`; the [selector supplement](client-ranged-action-selection.md)
now binds this to the Bow/Wand weapon-state enum test, selected equipment updates
and startup replacement order. The numerical Hit-to-resource edge remains open.

**Correction to the older generic spell-table example:** table `0x1434adb70`
is concretely installed by factory `0x141991bc0`, whose registration
`0x141972290` supplies the name `CastAura`. It must not be used as the concrete
Fireball binding; the named Fireball registration above selects another table.

### Melee's nonempty callback

`0x14136f210` preserves the target Entity, calls action virtual `+0x4b8`, then
tail-jumps to `0x1419e4620` with `RCX=action`, `RDX=target`. For the earlier table
`0x143517ba0`, slot `+0x4b8` is `0x141b55cb0`: it returns `1.0` for a skill
data flag at `+0xa5`; otherwise it obtains stat context through `0x14193b350`,
selects main/offhand attack-speed key 294/295 using action `+0x311`, and returns
`1 / (1 + speed/100)`. The raw callback does not explicitly copy that returned
float into another XMM argument; a guessed decompiler signature is insufficient
to label it a damage amount.

The following helper reads `enable_test_hit_epks` (9464 / `0x24f8`). Its enabled
branch resolves target Render through `0x14013de80`, accesses Render `+0x260`,
checks the target's Player component, and calls `0x141f66ba0` with variant tag 1.
It also conditionally reads `disable_test_hit_animation_slow_down`
(9465 / `0x24f9`). The inspected callback/helper bodies do not call a pool
reducer or setter or write pool current/carry.

The positive effect-data chain continues: `0x141f66ba0` selects an effect record
(tag 1/value 1 selects row 0 through `0x14248c4f0`), reads path fields at
`+8/+0x2d`, converts time fields `+0x10/+0x14` by dividing by 1000, and reads
flags at `+0x2c/+0x35`. At `0x141f66d23` it invokes global service
`0x1445bf970` virtual `+8`. That service's concrete binding remains open, so
this is evidence of the effect route, not an exhaustive transitive exclusion
of numerical side effects.

## 8. Event dispatch reaches a programmable VM

The same consumer sends key `0x6876` at `0x141e921ba` through
`0x141e95620`, also gated by a matching action and `result+0x49`. Its captured
callable table `0x1435aa4a8`, invoke slot `+0x10`, selects leaf `0x141ea08a0`.
The leaf supplies `0x141de5ec0` with the selected event entry's payload, the
captured script context, `sourceActor.ownerEntity+0x28`, and the resolved
`targetEntity+0x28` when present. The target came from `0x141ebefd0` and was
stored at consumer stack `[rbp+0x168]` at `0x141e91639`. These offsets identify
passed interfaces; they do not independently recover their concrete classes.
The folded hash of ASCII `hit` equals `0x6876`, but this pass has not bound that
string as this event's registration name.

The numerical side-effect boundary is now more precise:

```text
result consumer 141e91580
  → event key 6876 / 141e95620
  → captured callable 1435aa4a8+10 / 141ea08a0
  → event program iteration 141de5ec0
  → program runner 141df49e0
  → bytecode interpreter 141ded710
  → opcode 13 / descriptor-selected callable 141dfa9f0
  → [callable.vtable+10]                 ← concrete target unresolved
```

`0x141de5ec0` checks for a nonempty program list and an allowed source interface
through `0x141de5a60`, installs source/target values through
`0x141de8600/0x141de5bc0`, and iterates **0x60-byte program records**. At
`0x141de5fbe` it calls `0x141df49e0`. The runner requires context `+0x58`
nonzero and `+0x59` zero, saves/restores current program `+0x68`, and at
`0x141df4ab5` invokes the interpreter with the program's byte stream.

The interpreter's audited unwind span contains 6,565 decoded instructions and
123 distinct direct callees. It dispatches opcode bytes `0..0x65` through the
102-entry jump table at `0x141df47b0`. The audited body has no direct call to
the four numerical reducers or the absolute pool setter; this bounded direct
observation excludes neither its callees nor its program-selected callbacks.
Its four direct virtual calls use the passed context interface's `+0x28` at
`0x141def09f`, `0x141defc59`, `0x141defcfe` and `0x141defda3`.

Opcode **13** starts at `0x141dee1d8`. It reads argument-count and function-index
bytes, selects a **72-byte descriptor** from program `+0x30` with
`0x141dfaa20`, requires descriptor `+0x40 == 2` through `0x141df49a0`, and
builds an argument range from the VM stack. Callsite `0x141dee2bf` enters
`0x141dfa9f0`; that bridge loads the callable at descriptor `+0x38` and invokes
its virtual `+0x10` at `0x141dfaa05`. **The actual descriptor and callable for
the Actor's selected `0x6876` event program have not been recovered.** This is
an explicit remaining route for numerical effects, rather than evidence that
the event is presentation-only.

There are also positive script-state writes. Opcodes **40, 41 and 42** start at
`0x141defc41`, `0x141defce6` and `0x141defd8b`, resolve a target context through
interface `+0x28`, and invoke keyed typed-value helpers. `0x141de8c70`
replaces/assigns typed values, `0x141de9a90` loops additions through
`0x141de9380`, and `0x141dea100` compares/removes values then stores through
`0x141de8c70`. Their observed containers use 40-byte values and 88-byte keyed
entries. No binding from these containers to Life/Mana/ES current or fractional
carry has been shown.

The ordinary startup guard on native action-context clones documented in
[stat evaluation](client-stat-evaluation.md) does not establish a no-op event
VM. Conversely, this executable VM does not prove independent client hit loss.
R3 remains open until a concrete selected event program/callable or another
result path carries a numerical amount into a proved resource receiver/setter.

## 9. Action/event evidence and additional byte anchors

The follow-up evidence uses ignored batches `r3-loss-action-{seeds,factory,
registry,registration,ctors,overrides}`, `r3-loss-concrete-actions`,
`r3-loss-factories-bound`, `r3-loss-melee-{followup,slot4b8,test-effect}`,
`r3-loss-script-receivers` and `r3-loss-script-descriptors-bound`.
`r3-loss-concrete-binding.json` retains the selected data rows, four table
spans/slots and the complete interpreter jump table. The partial failed batch
`r3-loss-script-call-descriptors` is not used as a completed audit.

Offline Ghidra export `r3-loss-vm-decompilations.jsonl` completed all four
requested bodies (`0x141ded710`, `0x141df49e0`, `0x141de5ec0`, `0x14136f210`),
SHA256 `fecf7160393b11c14e7620988534e10f68955597d98aee7944cf5d603c6c1645`.
The separately extracted raw interpreter text is
`r3-loss-0x141ded710.c` (145,439 bytes), SHA256
`6385df7410a0316973cd267c9d670f20a086a4dd529a4d4e3787037a7060a874`.
Local `r3-loss-vm-sidecars.json` records these and the three other extracted C
file hashes; these generated files remain uncommitted.

| Data table | SHA256 |
|---|---|
| ActionTypes | `eef7b004b13a3f32e487d5c102c41362b62131f83249f45194ece05b8518d9d6` |
| ActiveSkills | `baadc60b03734004624100d6aeab1a7b3a97728a4c625761d2b01c04b1404c75` |
| GrantedEffects | `ee02ccde977a7a7c444b3fb329867b65a931dda3ab89f204dfc96b15571a6b3b` |

| Purpose / VA span | File offset | SHA256 |
|---|---:|---|
| Fireball registration `[0x141973cd0,0x141973cfd)` | 26685648 | `cecc0a5bb0190fc31c48a3e0333b37b2404a6ca186009b4fc0fd33eba968bce6` |
| Melee variant 0 registration `[0x14196a070,0x14196a0a0)` | 26645616 | `2ab03a1b2a04e7f7b9e5623021dfd6aeda1a51e1cc2ceba2d94175b1d66b48e0` |
| Melee variant 1 registration `[0x14196a040,0x14196a070)` | 26645568 | `a5bcc8e8a911c25472f4d96ceb1449984f6201125dd7435ae0e0ebabe30021a9` |
| Fireball factory `[0x141988dd0,0x141988eb0)` | 26771920 | `56a1c63f13ac81fb4e96d98bf0795bfd432c281c2360106455c573e6c4f3ae02` |
| Melee variant 0 factory `[0x1419bbb70,0x1419bbc9d)` | 26980208 | `a01eef4cb4b070439a07068202f7786b67491af7985602d299313238333c3526` |
| Melee variant 1 factory `[0x1419bbca0,0x1419bbd80)` | 26980512 | `4a0e76ad393c380687d5bdefb4695448208be15bbfea77f092b38dff1bcb5a9d` |
| Fireball constructor `[0x141a8b150,0x141a8b1c9)` | 27829584 | `9513b412dc56bce9fafb57591ae1bdf291ccacb18b09b153e8303ba79602610b` |
| Melee variant 1 constructor `[0x141ac9dc0,0x141ac9e27)` | 28086720 | `dae44d0ef4dff2b3c66cd9db5d4f2e41021526e55808866223f12d9838587097` |
| Melee callback `[0x14136f210,0x14136f23e)` | 20375056 | `ad0e1a90356eb6b50c77a66def2a6bcfef7c00214b0087c6bfce11e0464c09ec` |
| Test-hit helper `[0x1419e4620,0x1419e4743)` | 27146784 | `39ca8955622dbe3d3fac7c62a3660fa716efa0b8f97834248e31b63fd1cdd8fb` |
| Speed getter `[0x141b55cb0,0x141b55d56)` | 28659888 | `af644618cefa95908c970a89ccca7e5249615c1d0f085dacf31bba6289f16673` |
| Event iteration `[0x141de5ec0,0x141de5ff3)` | 31347392 | `e2f65137bf6d8446055d0a96d100099972f7f2e95f735daa9ae9f28177705a38` |
| Interpreter `[0x141ded710,0x141df4948)` | 31378192 | `63ca6a3796bae102c0d4fb50017bb2ce9010d86a5e13dee9d62e75c809df0b89` |
| Callable bridge `[0x141dfa9f0,0x141dfaa17)` | 31432176 | `f6164daf10a8378c0920d45ae7cc6a7be0f2c82f8a7cea1b66146cf015d47401` |
| Fireball table `[0x143335d80,0x143336060)` | 53694848 | `de83e6fabedf0aad31f1dda07d424033903894929b2094d3c3582b210afcaa59` |
| Melee variant 0 table `[0x143517ba0,0x143518060)` | 55668640 | `bbccd48123f94edcf4292c0f7672aa3be55c7390ee6ee5ad9448b33c29af071f` |
| Melee variant 1 table `[0x143273730,0x143273a10)` | 52898608 | `775641251ecc13dbac9d9066cf0b01797da58180ac0a4d6d6e5a62516bd2431f` |

## 10. GEAL callable construction and client/server eligibility

The event VM is GEAL: the compiler/interpreter's own error strings name it.
Its callable availability has a separate mode from the native action-context
clone guard. Registration body `0x1400fe9f0` calls `0x14013ebc0` at
`0x1400fee8b`; on that constructor's successful path, `0x14013ed38` writes
`1` to global `0x14458e830` if it is zero. Compiler `0x141df4bb0` compares the
literal UTF-16 keywords `server` (`0x143473128`) and `client`
(`0x1435999a8`): the server branch is enabled for modes 2/3, the client branch
for modes 1/3. Thus mode 1 has a direct client semantic binding; it is not
inferred from global `0x1445b38cd`.

Named-function compilation `0x141df94b0` first searches the concrete native
name/factory registry at `0x1445b9f88..0x1445b9f90`. If no native entry matches,
it consults name metadata map `0x1445bd038`. At `0x141df96c6..0x141df96db`
it accepts the entry's mode at `+0x30` only when that mode is 3, equals the
current mode, or the current mode is zero; otherwise it takes the
`Function ... does not exist` error path. Metadata initializer `0x141ec7790`
supplies modes to insertion helper `0x141ee1bf0`, which stores the supplied
integer at node `+0x30`. The initializer also merges duplicate client/server
names into mode 3. The selected mode-2 names below have one registration each.

Two concrete array registrations feed the native registry through
`0x141de6120`: `[0x144488380,0x1444891a0)` has 226 name/factory pairs and
`[0x1444999b0,0x14449b8e0)` has 499. These are 725 recorded native entries,
not a proof that all possible dynamic registrations have been recovered.
The separate metadata initializer contains 1,230 registration calls, including
duplicates; names in that larger map do not themselves imply an implementation.

| Literal metadata name | Registration call | Initial/final mode for this unique name | Present in either native array? |
|---|---|---|---|
| `SetLife` | `0x141ed8417` | 2 | No |
| `RefillLifeManaESAndFlasks` | `0x141ed28cd` | 2 | No |
| `CalculateDamage` | `0x141edaec4` | 2 | No |
| `AddStat` | `0x141ed537a` | 2 | No |
| `RemoveStatsFromSource` | `0x141ed53d9` | 2 | No |
| `InterpolateStat` | `0x141ed44a2` | 2 | No |
| `AddBuff` | `0x141ed6252` | 2 | No |
| `AddBuffWithOwner` | `0x141ed62b1` | 2 | No |
| `AddBuffAdvanced` | `0x141ed6310` | 2 | No |
| `AddBuffInRadius` | `0x141ed3c19` | 2 | No |
| `AddBuffTemplate` | `0x141edb397` | 2 | No |
| `IfBuff` | `0x141eca8d6` | 3 | Yes: factory `0x14207d7f0` |
| `HasBuff` | `0x141eca914` | 3 | Yes: factory `0x14207d940` |

Consequently the shown ordinary client-mode compilation cannot use the
metadata-only mode-2 names above as native resource/buff mutators. This is an
exact GEAL lookup/eligibility boundary, not a claim that the rest of the client
cannot calculate or change resources. In particular, the separate native Buff
backend registry and resource notification paths require their own analysis.
The literal names `ApplyDamageOverTime`, `StartDegen`, `DealDamage` and
`AddBuffWithStatValues` are not present in these two stated name inventories;
that bounded lookup result does not exclude equivalent behavior under another
name or another registration path.

### Accepted metadata-only names produce an empty-return placeholder

When a name is accepted through the metadata fallback, `0x141df94b0` emits
opcode 10 or 12 and appends a 72-byte descriptor to program `+0x30` via
`0x141e001d0` or growth helper `0x141dfdd50`. This descriptor has variant tag
1 at `+0x40` and callable table `0x14359b6e0`. Its invoke `+0x10` selects
the fully inspected leaf `0x141dfeec0`, which sets an empty typed-value vector
(`begin == end == output+0x18`, capacity end `output+0x40`) and returns without
any call. This closes that specific placeholder's behavior.

Concrete native entries take another route: their table entry `+8` factory
builds a callable descriptor, compiler callback `0x141df9930` copies it into
the same program vector via `0x141de53e0` or `0x141dfdf20`, and the interpreter
can invoke it through opcode 9/10/11/12 as well as its separate opcode-13
support. Thus opcode 13 is an explicit open callable route from section 8,
not the sole native-call boundary. The current pass has not recovered the
descriptor sequence of the selected Actor `0x6876` program. The inspected
tag-2 stores in `0x141de53e0`, `0x141e004f0` and `0x141e005c0` are variant
copy/assignment machinery; they do not identify an original concrete callable.

### A concrete native numeric write: `ClientOnlyKillSelf`

The client callable inventory contains a positive resource-mutating example;
the rejected metadata-only `SetLife` name must not be generalized to all GEAL
callables. Native entry `0x144488ac0` names `ClientOnlyKillSelf` at
`0x14348f3f0` and binds factory `0x1417ddb50`. That factory accepts no
arguments and installs table `0x143490a90` at `0x1417ddb72`. Its invoke slot
`+0x10` is `0x141819ed0`.

The invoke resolves an Entity from its supplied context through `0x1417c53d0`
and obtains the component by literal `Life` through `0x14013dda0`. When the
embedded Life pool at `Life+0x178` has a positive integer current (`pool+0x30`),
`0x141819f09..0x141819f11` supplies reason `0x0e`, integer value zero and
that pool pointer to absolute setter `0x141e698e0`. The setter clamps to the
available pool range and writes current at `0x141e69939`; it also dispatches
the existing change/empty notifications when applicable. The invoke then
explicitly calls Life slot `+0xe8` at `0x141819f22`, using reason `0x0e`
and the integer captured from the owning Entity's `+0x88`. On the established
Life table this slot is `0x141e6c710`, the positive-to-nonpositive transition
handler. This is an absolute-zero/transition path, not a floating damage delta
through the ordinary loss reducer.

This closes **registered native callable → context Entity → concrete Life
pool → numerical assignment → transition callback**, conditional on invoking
that callable with the stated component/current. No recovered contact program
has been shown to invoke it, and its name alone does not establish a
client-only Entity eligibility check. The invoke's actual guards are the
resolved Life component and positive current. It therefore supplies a positive
resource-write boundary without proving numerical Hit pre-resolution or a
source-instance DoT producer.

The factory and invoke audits are `r4-client-selfkill-factory` and
`r4-client-selfkill-invoke`; `r4-client-selfkill-verification.json` retains the
native entry, table slot, literals and exact instruction assertions. The same
bounded invoke audit records `ConsumeSkillUse` and `KillGroundEffect`
continuations separately; their names are not evidence of resource deduction
or incoming DoT publication.
The [ground-effect lifetime follow-up](client-ground-effect-boundary.md) now binds the latter
to its owning GroundEffect component, timer replacement, conditional Buffs guard, cancellation
and animation/removal-request consequences. Its numerical target-rate publication remains open.

### A concrete native numeric read, and a programmable Life listener

Entry `0x14449b430`, literal `GetCurrentLife`, binds factory `0x14209e390`.
The factory installs callable table `0x1435cd8a0`, whose invoke `+0x10` is
`0x1420e88b0 → 0x1420c6700`. That body resolves Life through `0x14013dda0`,
reads integer current at Life `+0x1a8` and maximum at `+0x1a4`, and returns
current as a typed integer or a percentage as a typed float. This is actual
native numeric access from GEAL; it does not deduct a resource.

The name `AddLifeOnDeath` must also be followed beyond its factory. Static
entry `0x144499ae0` selects `0x142077a10`, which builds callable table
`0x1435d5a90`; invoke `+0x10` is `0x1420dea00 → 0x142077b30`. That path
creates an object through `0x1420cae40`, installs primary table
`0x1435d10c0`, and installs an embedded listener interface at object `+0x18`
with table `0x1435d4ee0`. The constructor subscribes that interface to the
resolved Life's listener list at `+0x20` and retains a supplied code block.

The embedded listener's slot `+0x10 → 0x1420b9f50` and slot
`+0x20 → 0x1420b9f00` both call `0x142077c60`; the latter is additionally
gated by object `+0x50`. The complete inspected consequence is
`0x142077c60 → 0x142060be0 → 0x141fdc2d0 → 0x141df49e0` (callsite
`0x141fdc483`) and therefore the same interpreter `0x141ded710`.
The evaluator prepares keyed script variables, invokes the supplied program,
then inspects/clears script key `0x62ce9938`. No healing amount or deduction
has been demonstrated by this registration/handler chain. Its supplied program
remains an open source of further effects; the chain is not classified harmless.

New local records are `r3-loss-geal-{descriptor,emission,registry,builtins,
native-examples,native-invokes,native-consequences,mode-insert-bound}` and
`r3-loss-life-death-{handler,invoke,consequence,program,evaluate}` audit batches.
The joined `r3-loss-geal-source-api-candidates.json` retains literal names,
registration callsites, initial modes and native-array matches; supporting
inventories are `r3-loss-geal-name-modes.json` and
`r3-loss-geal-native-tables.json`. No target execution or new decompiler project
was needed for this increment.

## 11. Additional GEAL source anchors

All ends are exclusive. Selected table spans do not assert complete object sizes.

| Purpose / VA span | File offset | SHA256 |
|---|---:|---|
| Client mode constructor `[0x14013ebc0,0x14013ed6b)` | 1302464 | `0635cbf7f0e9f02a2316b7361b641d024438fcb10ddfea9d5d2aae946e1b3b93` |
| Name/mode initialization `[0x141ec7790,0x141ede115)` | 32271248 | `2d05487fd091bfefa522eb0c7a6a3ea71c9606f49dc7cd3e68bd4cb5283a3378` |
| Client/server keyword compiler `[0x141df4bb0,0x141df5120)` | 31408048 | `ed8083c18eb5524c1eb9a7231490ec154a84ea44c21df17340e83f71a915f2b9` |
| Named function compiler `[0x141df94b0,0x141df9925)` | 31426736 | `3748ce757d11c08fa7a040f0621f0562d6a3dc59cbdffab1442859d5b9b26fac` |
| Mode insertion helper `[0x141ee1bf0,0x141ee1cc1)` | 32378864 | `e6979e347e136a854cd72579499bcb0f47332d11cc3c25314f4f9f788c8c878c` |
| Current Life native read `[0x1420c6700,0x1420c686c)` | 34364160 | `fb9d2890f97636cedebf4b30edf86892cbffe9ea2ac4ccf24e8d6e59e64b4481` |
| Life listener constructor `[0x1420cae40,0x1420cafa5)` | 34382400 | `5d47a9cc70a2c393e6780615908292f275d7005c0fa1a4877aa371bafb4d787a` |
| Captured-block evaluator `[0x141fdc2d0,0x141fdc4b4)` | 33404624 | `8215c447585de7073d4f8393b415355836f2b0117a93a0094638908faa8bc035` |
| Native array (226 entries) `[0x144488380,0x1444891a0)` | 71854976 | `42e2990b2b371102b4d163b0e9d56be3cbcdc6544a74a2e7c1f5850ce52c7d29` |
| Native array (499 entries) `[0x1444999b0,0x14449b8e0)` | 71926192 | `d5cf999c789332f0e6cecbaa7a76f627ab29fbf4d55eb6b983fec5e36a27194c` |
| Fallback callable table `[0x14359b6e0,0x14359b708)` | 56208096 | `8c6d1c59dd833f512b80d4c89f7e3499ec784ba67a743da249eb23bd9452ff1b` |
| Fallback empty-return leaf `[0x141dfeec0,0x141dfeed7)` | 31449792 | `d2adb5e4ac0fac0792a7a2bb10c6d2af6c9eb8cc7b8f3e9f6a0eb8036af597d1` |
| Current Life callable table `[0x1435cd8a0,0x1435cd8c8)` | 56413344 | `5cd1048c06319c781766eaaae242d6afe694064967d404f036d233b9be02790a` |
| Life listener primary table span `[0x1435d10c0,0x1435d1108)` | 56427712 | `1323b5b7eb031031605fe7ed666b3f75ef29423fdf529883fd35f2a485e1b460` |
| Life embedded listener table span `[0x1435d4ee0,0x1435d4f08)` | 56443616 | `d78f9991f06476c4947783512ff9cd3030a6928ed7a468db2b59aec9a6cb2a12` |

`r3-loss-geal-binding.json` retains these spans and hashes of the three joined
inventories. Raw decoded evidence, mode-1 assignment, unique mode-2 registrations,
placeholder invocation and native numeric-read bindings are checked against the
same original executable; none of these checks executes the client.

## 12. Actor event programs come from parsed component script blocks

The two event tables consumed by `0x141e95620` have concrete parser producers.
They are not an unconditional built-in damage program. The Actor constructor
stores its configuration argument at Actor `+0x188` (`0x141e8c311`), then sets
Actor `+0x2b0 = configuration+0x140` (`0x141e8c4f2–0x141e8c4ff`). The base
configuration constructor `0x141e8bf20` constructs this embedded event block
through `0x141e8c070`; that leaf initializes its vectors, including `+0x60`
and `+0xd8`, to empty. This is an initialization binding, not a claim that no
later code can change the Actor's pointer.

The client configuration factory `0x1415e9480` installs table `0x143467ae8`
at `0x1415e9527`. Its script-parser slot `+0x18` is `0x1421922e0`.
The object-type component parser `0x142476f40` supplies a parsed block's token
range in script context `+0xc0..+0xc8`, then calls the selected configuration's
slot `+0x18` at `0x14247743a`. The client Actor wrapper passes
`configuration+0x140`, the configuration itself, the field name, and script
context to `0x141e88020` at `0x1421926fd–0x14219270d`. This connects ordinary
component resource parsing to the exact event block read by the dispatcher.
The existing `.ot` loader/inheritance rules are documented in
[tooling.md](tooling.md#7-schema-generators-objects-and-messages-without-live-trial-and-error).

| Resource field grammar | Parsed destination relative to configuration `+0x140` | Selection by `0x141e95620` |
|---|---|---|
| `on_custom_event_<event>_for_<action>` | `+0x60..+0x68`: 0x20-byte action groups, each containing 0x20-byte event records | Action word `+0x118`, then event word |
| `on_effect_custom_event_<event>_for_<granted-effect>` | `+0xd8..+0xe0`: 0x28-byte effect groups, each containing 0x20-byte event records | Resolved action `+0xf0/+0x48` effect pointer, then event word |

The first branch recognizes its literal at `0x141e8a06a`, splits at `_for_`,
hashes the event substring through `0x140e35420` (`0x141e8a0fb`), hashes the
action suffix through `0x1400f3c70` (`0x141e8a132`), and finds/appends the
action group at `0x141e8a148–0x141e8a1a9`. It finds/appends the event record at
`0x141e8a1ae–0x141e8a20a`, then passes that record's `+8` program vector and
the supplied script context to `0x141de5e20` (`0x141e8a237`). Existing matching
event records take the same append path.

The effect branch recognizes `on_effect_custom_event_` at `0x141e8990d`, hashes
the event substring through the same leaf (`0x141e8999e`), resolves the suffix
through `0x14239c860` (`0x141e89a1e`), and finds/appends the effect group and
event record at `0x141e89a3b–0x141e89b17`. Its program append is
`0x141e89b22 → 0x141de5e20`. These are the dispatcher's two custom-event
tables; the distinct `on_event_` / `on_effect_event_` parser branches populate
other vectors and are not substituted for these bindings.

`0x141de5e20` checks whether the supplied script token range is nonempty,
appends a zero-initialized 0x60-byte program, and calls `0x141de5720` at
`0x141de5ea6`. The compiler iterates the supplied token range through the
already inspected compiler `0x141df4bb0` (`0x141de57ab`) and appends the
terminating zero opcode. Thus the selected event record's program vector is
compiled from that resource field's supplied script block; it is not selected
merely by the presence of opcode 13 support in the interpreter.

The inspected hash leaf multiplies a zero-initialized accumulator by
`0x1000193`, XORs each byte, and folds upper/lower 16 bits. The literal `hit`
does produce `0x6876`, but that alone does not identify a resource field or
exclude another name with the same folded word.

A bounded read of the existing extracted resource cache inspected 12,643
`.ot`, `.otc`, `.ao`, and `.aoc` files. It found nine fields in these two
custom-event grammars across seven files; none of their event names hashes to
`0x6876`. The cache contains no actual `.otc` file bodies, and has a missing-file
marker for `Metadata/Characters/Character.otc`. These are cache facts, not an
archive-wide absence result. The local inventory retains the matching resource
names, raw-file hashes, field names and computed event words. No archive was
modified and no client was executed.

**Remaining concrete edge:** identify the source Actor's actual object type and
resolved component resource chain, the matching action ID or granted-effect
pointer, and any matching custom-event field whose event word is `0x6876`.
Then recover any matching field's supplied token block, including inherited and
client-specific definitions, and bind its emitted descriptors. Section 14 checks
the actual bundled source chains for the player and Sand Spitter cases and finds
no such field; a matching program must not be assumed to exist for every source.
The parser boundary is closed, while general numerical/resource consequences of
other possible configurations remain unresolved. This does not establish a
generic client damage calculator or prove the whole callback harmless.

## 13. Event program source anchors

All ends are exclusive. The table span does not assert a complete object size.

| Purpose / VA span | File offset | SHA256 |
|---|---:|---|
| Actor event-block initialization `[0x141e8c240,0x141e8c848)` | 32028224 | `370b17726ee3cef9e7ce32442635b82e3562a1b94ee370af015ccd40c4c83370` |
| Client Actor config factory `[0x1415e9480,0x1415e9551)` | 22972544 | `9a3371a2319c513799ba8734c624ae86f9910dd9973135f5ad80558a2ca1026d` |
| Client Actor block parser `[0x1421922e0,0x14219285b)` | 35198688 | `177b14472a68afb352516f409e5b898bbbc2cffc8a30181f3f8e5072bb76aa5d` |
| Actor named-handler parser `[0x141e88020,0x141e8aa7e)` | 32011296 | `d7283053664a2f5e06aeef2adb82a643e37c99572096f9fdd544b71f65924d43` |
| Type component script dispatch `[0x142476f40,0x142477e98)` | 38232896 | `647afcaf0bdda9ad07b8d1109800480347eff8d476569bf68b64530f3bccaf79` |
| Event program append `[0x141de5e20,0x141de5eb1)` | 31347232 | `06cd152a9e3c27cfa3e2a6fc50c0724a4548cd7838aa03eaf8af3eace06717e7` |
| Event block compilation `[0x141de5720,0x141de57f7)` | 31345440 | `889a72f488256ccfc715ea13b9c66c191e3e205e929b0b3b1e933a6fb56a2ce5` |
| Custom-event name hash leaf `[0x140e35420,0x140e35470)` | 14895136 | `de0609ec85e2dc2d5ef00fe95c2ca859226f2a18922360cc3f37c81aadc8aad2` |
| Client Actor config table span `[0x143467ae8,0x143467b28)` | 54947560 | `3d97c17ef72598dc4b3e40f32cfcae8ba1605f90c7c10977cb48a07e9118e048` |

Successful new audit batches are `r3-event-program-{actor-ctor,parser,
resource-link,config-bound}`. `r3-event-program-binding.json` retains the nine
published spans and inventory hashes. `r3-event-program-resource-handler-inventory.json`
retains the bounded resource matches. The incomplete `r3-event-program-config`
attempt is not used as an audit source; its explicit leaf was subsequently
reviewed and included in `config-bound`.

## 14. Concrete bundled source chains: player and Sand Spitter

PR [#373](https://github.com/LocalExile/LocalExile/pull/373), inspected at
`b0afa90ea5c58e9082099d887180df8299dcdaf3`, supplies a concrete research lead:
its author reports official-server captures involving a Ranger and
`Metadata/Monsters/SandSpitters/SandSpitter12@1`. The following is a separate
static check of locally available resources. It does not independently verify
that capture or identify the resources actually loaded in its recorded process.

A fresh read-only inventory of the available GGPK's bundle index contains
1,214,937 paths, including **12,997 `.ot` paths and zero `.otc` paths**. Direct
probes for the four selected player/monster `.otc` companions also fail. Thus
the old extracted cache's missing `.otc` markers do not, by themselves, indicate
an unexamined existing companion file in this indexed snapshot. This is an
index-membership result, not an assertion about loose-file overrides, every
distribution, or the pristine identity of the entire locally maintained archive.

Fresh extraction recovers the following complete selected inheritance chains:

| Selected source | Parent-first resource chain | Custom-event fields in either Actor event grammar |
|---|---|---|
| Ranger object type | `Metadata/Characters/Character.ot` → `Metadata/Characters/Dex/Dex.ot` | None |
| Witch object type | `Metadata/Characters/Character.ot` → `Metadata/Characters/Int/Int.ot` | None |
| Sand Spitter base object type | `Metadata/Monsters/Monster.ot` → `Metadata/Monsters/SandSpitters/SandSpitter.ot` | None |

The extracted `MonsterVarieties.json` row 490 binds literal
`Metadata/Monsters/SandSpitters/SandSpitter12` to `BaseMonsterTypeIndex =
Metadata/Monsters/SandSpitters/SandSpitter`, with an empty
`AddonMonsterTypeIndex` list. This closes that particular data-to-source-file
selection. The table SHA256 is
`4d6ccccc097599bac7a22f05921183e796a3d1ba5a9f167ea28669a62cd29db6`.
It does not recover the author-reported capture's entity-creation bytes.

All five fresh resource bodies match the existing extracted cache byte for byte:

| Resource | Bytes | SHA256 |
|---|---:|---|
| `Metadata/Characters/Character.ot` | 8,950 | `a9fcdf168d84956e569c345b474aa2c2262efa61057046a87eb8036d36ef9f4b` |
| `Metadata/Characters/Dex/Dex.ot` | 388 | `087176117e602d9d7c5757d6cc4ce38851b070203cf032f145fee5fad090a054` |
| `Metadata/Characters/Int/Int.ot` | 388 | `087176117e602d9d7c5757d6cc4ce38851b070203cf032f145fee5fad090a054` |
| `Metadata/Monsters/Monster.ot` | 6,654 | `27bc3ceddf8e4798b1a18e239b3fc5650b302d10c9b7fc67b754b70ef61c4dff` |
| `Metadata/Monsters/SandSpitters/SandSpitter.ot` | 452 | `5cc590dd3f423b8c1bf66dd7b133c80eb99142a896201d269626581027bc63ac` |

The character/monster roots already contain embedded `client` blocks: their
contents are Render/Targetable configuration. Sand Spitter adds a client
BaseEvents construction handler that hides a mesh segment; its ordinary Actor
block adds `Emerge` and `Emerge2`. None supplies either custom-event grammar from
section 12. Consequently **these source files supply no `0x6876` custom-event
program to extract**. An empty optional source-event route is distinct from an
unrecovered mandatory damage program. Dynamic reconfiguration and other object
types remain outside this selected source-file result; neither the numerical
hit path nor all possible native consequences are thereby excluded.

The native loader was also checked beyond its previously cited addresses.
`0x142474ae0` invokes `0x142474fc0` for the common pass at `0x142474b74`, stores
the common component count at type `+0x80` at `0x142474b85`, and performs a
separate configured extension pass at `0x142474bfd`. The inheritance routine
`0x142476300` recognizes the literal `nothing` and invokes `0x142474fc0` for
a nonterminal parent at `0x1424764b0`; `0x142474fc0` processes the stored
parent list before the current-file parser path in its mode-3/4 branch
(`0x1424751d5` before `0x142475297`). These are original instruction/control-flow
checks, not evidence that a specific runtime instance took the path.

A further bounded search freshly extracts **879 monster/character/projectile
`.ot` paths** that are present in the current index and absent from the old
cache. None contains either custom-event grammar. This extends the prior cache
coverage; it is not a complete fresh extraction of all 12,997 `.ot` bodies and
must not be reported as an archive-wide absence of hit logic.

New native source anchors, with exclusive ends:

| Purpose / VA span | File offset | SHA256 |
|---|---:|---|
| Type component passes `[0x142474ae0,0x142474ce7)` | 38223584 | `3884d14ed1a1be6e47d8e54c0d7aa5f324c0c432646c875df8e68e0b087482d4` |
| Parent/current-file dispatch `[0x142474fc0,0x14247531a)` | 38224832 | `e95953359162b640083de53ce6ef35f78d790022a27890eedb61491ed27326fa` |
| Extension file selection `[0x142475320,0x142475a0c)` | 38225696 | `dd790047b75b3165dddc720cf8c54e8f406e8c8d54ab8272065a213f9f4c0e08` |
| Parent resource selection `[0x142476300,0x14247657c)` | 38229760 | `2e93b08fe855677be4afab4864c5bb7d8ab83f9cb752b3fbbb88667fcdc92d7c` |

Local `r3-event-archive-loader` retains four successful selected decompilations
and original-byte audits. `r3-event-archive-inventory.json` binds the five
resource bodies, parent chains, index counts and monster table row;
`r3-event-archive-additional-inventory.json` retains all 879 additional resource
hashes and the exact bounded search result. Their scripts recheck these records
without running the client or modifying the archive.
