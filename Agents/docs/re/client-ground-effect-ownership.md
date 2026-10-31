# GroundEffect retained-token ownership (3.29.3b)

## 1. Result and boundary

The previously open `GroundEffect +0x50/+0x58` producer is now identified. The
create/reuse route can pass a child component to `0x1421c3ed0`, which lazily creates
a reference-counted **cancellation flag** and retains the same pair in a parent
`ClientInternalGroundEffect` collection. This is a concrete client effect-lifetime
relationship. It does not identify a damage instance, numerical rate publisher,
combat attacker, or affected damage target.

The positive ownership path is:

`ClientInternalGroundEffect post-initialization` → `fill dispatch` →
`GroundEffect create/reuse` → `attach child cancellation flag to parent collection`.

The child sets the flag when ending; the parent removes flagged entries during
its own advancement. Releasing all parent references also explains the previously
recovered **infinite lifetime + control count 1** expiry branch. A finite child
does not take that special branch merely because its parent releases a reference.

This is static analysis of the pinned executable, with original PE instruction
verification and separate bounded typed Ghidra exports. No client execution,
attachment, hooks, capture, server change, or CI deployment was performed.

## 2. Evidence identity and navigation

- Binary: `Client/Game.exe`, SHA256
  `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`;
  preferred base `0x140000000`.
- Stat table: `data/tables/Stats.json`, SHA256
  `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da`,
  23,346 rows. A native key is JSON row index plus one. This pass does not discover
  a new numerical stat formula.
- Local ignored evidence: `Tools/re/out/client-damage-audit/r9-ground-ownership/`:
  `audit.json`, `targets.json`, `discovery.json`, original-byte `.asm` files,
  `R9GroundOwnershipFunctions.java`, `decompilations.jsonl`, and `console.log`.
- Rebuild/check entry points: `r9-ground-ownership-build.py`,
  `r9-ground-ownership-decompile.py`, and `r9-ground-ownership-verify.py` in the
  same audit parent directory; result `r9-ground-ownership-verification.json`.

These are local evidence records, not REA Evidence IDs or observed runtime traces.
The PE audit uses complete selected unwind-root ranges and four explicit reviewed
leaf ranges; decompiler output remains an interpretation checked against those bytes.

The earlier [clock, network state and kill receiver](client-ground-effect-boundary.md)
and [selected effect source catalogue](client-effect-source-catalogue.md) are
prerequisites. Their conclusions are not silently upgraded into damage ownership.

## 3. Positive discovery and create/reuse boundary

Exact RIP-relative `GroundEffect` literal references include the component getter
`0x1420b60b0`, reuse predicate `0x142190ca0`, and create/reuse function
`0x14218fb00`. Exact E8/E9 references to token attachment `0x1421c3ed0` identify
two call sites in that create/reuse function: `0x14218fd34` and `0x14218ff07`.
The discovery record retains all selected exact reference results, including the
ten direct sites from eight owners calling create/reuse. Those are navigation
results; this report does not claim every caller's semantics has been recovered.

`0x14218fb00` checks the row, coordinates, positive lifetime, and nonzero size-like
input before proceeding. Its reuse candidate predicate resolves an actual
GroundEffect component, rejects ended components (`+0x48`), matches the row's
type pointer, accepts mode 0 or 3, and checks the supplied size-like value.
The outer reuse branch additionally requires mode **3 / ClientOnly** at
`0x14218fcfe`. It stores the maximum of the existing and requested lifetime at
`0x14218fd21`, then attaches to the optional parent if supplied.

On new creation, a non-null row field `+0xa5` selects a referenced object-source
path; otherwise the route requests registry key `0x76698106`. The selected source
identity for that fallback key is **not resolved here**. It is not safe to equate
it to `ClientOnlyGroundEffect.ot` from a filename or the reuse-mode check alone.
The route allocates an Entity, invokes `0x141ebdf30`, installs table
`0x143456508`, resolves its GroundEffect component by the literal name, writes
remaining lifetime `+0x44`, sets the size-like field through slot `+0xb0`, and
calls initializer `0x141e1a820` at `0x14218fef6`. Optional parent attachment follows
at `0x14218ff07`.

The full initializer copies the retained GroundEffects row into `+0x28/+0x30`,
selects/loads AO resources where Animated is configured, subscribes its animation
listener, and updates position/orientation/scale. Neither this initializer nor
create/reuse directly assigns the cancellation pair: the identified attachment
helper does. The orientation branch's TLS/random arithmetic is retained in the
original audit but is not reconstructed as a combat RNG contract.

## 4. Concrete parent route

`ClientInternalGroundEffect` table `0x1435dd640 +0x08` points to
`0x1421c3d20`. After optional animation setup, it tests config mode **2 / ServerFill**
at `0x1421c3d65`. In that branch it supplies its own component pointer as the
parent argument (`0x1421c3dc8`) and calls `0x142190770` at `0x1421c3de6`, alongside
the retained row, owner position, remaining lifetime, and size-like input.

`0x142190770` dispatches according to row field `+0x14`. Value 2 forwards directly
to create/reuse at `0x1421907d0`; its other branch builds a retained callback
context and calls `0x142190920`. The selected cell callback `0x142190c10` reads the
parent from that context and forwards it to create/reuse at `0x142190c8f`.
This pass preserves the latter callback's context operands without reconstructing
the complete fill traversal or assigning combat target-selection semantics to it.

This establishes an actual producer path from a typed ground component, rather
than a search for every instruction whose displacement happens to be `+0x50`.

## 5. Cancellation payload and reference ownership

`0x1421c3ed0` receives parent in RCX and child in RDX. It addresses child `+0x50`
at `0x1421c3ee7`; if the payload pointer is null, it allocates 24 bytes:

| Control/payload field | Concrete initialization or use |
|---|---|
| Control `+0x00` | Table `0x14304ad90` |
| Control `+0x08` | Strong count initialized to 1 at `0x1421c3f0c` |
| Control `+0x0c` | Weak/control count initialized to 1 at `0x1421c3f13` |
| Control `+0x10` | Byte flag initialized to 0 at `0x1421c3f24` |
| Child `+0x50` | Payload pointer = control address + `0x10` |
| Child `+0x58` | Control pointer |
| Parent `+0x68/+0x70/+0x78` | Begin/end/capacity of retained 16-byte pairs |

`0x1401293b0` moves the temporary payload/control pair into child `+0x50/+0x58`
and releases the previous destination pair if necessary. It zeroes the moved-from
temporary, so its later cleanup does not duplicate ownership.

The helper then takes a retained copy of the child's pair and searches the parent
collection **by payload pointer identity**. A duplicate is not appended. Otherwise
it appends a retained pair (or calls the growth helper), then releases its local
temporary. Thus repeated attachment to the same parent is not a multiplicative
damage or stack operation. Different parent collections can retain the same child
flag; their references contribute to the shared control's lifetime.

The control table's payload-destroy slot is the no-op leaf `0x1400f0f00`.
Its control-destroy slot is thunk `0x140145050`, which dispatches deleting
destructor `0x140280410` through slot `+0x10`; that destructor frees 24 bytes.
The recovered payload is a flag, with no embedded source rate, damage type,
attacker pointer, or affected-target collection. This statement is specific to
this allocation and these consumers, not a global claim about every effect token.

## 6. End, cleanup, and infinite lifetime

Child cancellation `0x141e1aef0` writes byte 1 through child `+0x50` at
`0x141e1aefd`, clears the child pair, and releases its control reference.
`ClientInternalGroundEffect` advancement `0x1421c3e00` first calls base advancement,
then inspects each retained flag byte. It compacts unflagged entries with move
assignment and releases the removed tail via `0x140144930`.

Parent termination `0x1421c3ea0` invokes base termination, releases all collection
entries, and resets its end pointer to begin. Its deleting destructor
`0x1421c3ce0` destroys the retained vector through `0x14012bd80` before base
destruction. These paths release references; they do not write the child's flag
or directly call each child's termination method.

In base advancement `0x141e19f10`, the already recovered infinite-lifetime branch
checks a non-null child control and strong count equal to 1 (`0x141e19f64`). With
only the child's own reference remaining, it invokes natural expiry. With another
retaining parent it returns from that infinite branch. A missing control also
leaves the infinite component alive. Finite lifetime follows its ordinary delta
subtraction instead. No fixed 33 ms interval is established by these functions.

| Sequence | Supported consequence |
|---|---|
| Attach a newly allocated child to one parent | Child and parent retain the same flag/control pair |
| Attach the same child to that parent again | Identity match avoids a duplicate collection entry |
| Child ends first | Child sets flag and releases; parent later removes the flagged entry |
| Last parent releases an infinite child | Child's next base advancement sees count 1 and expires |
| Last parent releases a finite child | Remaining finite timer continues through its normal branch |
| One of multiple retaining parents releases | Another parent reference prevents the count-1 infinite-expiry condition |

Animation subscription storage at component **`+0x60` is distinct**.
`0x141e1ad50` builds a listener pair containing component `+0x18` and an
AnimationController collection; its callback is `0x141e1af60`. It must not be
conflated with the shared flag at `+0x50/+0x58`.

## 7. Numerical boundary and next work

This closes the selected cancellation token's creation, concrete parent route,
retention, cancellation and release semantics. It does **not** close source
damage snapshotting, affected-unit membership, active DoT rate publication,
mitigation, per-target rate cap, or resource deduction. The supplied row,
position and lifetime are effect creation inputs; their presence does not turn
the cancellation payload into a numerical DoT instance.

The [source catalogue](client-effect-source-catalogue.md) separately establishes
that ClientOnlyGroundEffect declares no Buffs block, while ServerGroundEffect
declares one. The [expiry receiver](client-ground-effect-boundary.md) reaches a
conditional Buffs advancement guard. Neither result identifies that guard as a
damage publisher. The remaining numerical investigation should follow positively
identified rate/template and Buffs producer-consumer edges, while preserving the
unresolved registry key and any missing traversal callback binding.

Exact direct-call discovery omits indirect calls and leaf bodies absent from the
selected reviewed leaves. The semantic absence claims above apply to the complete
listed functions and identified payload, not to the whole executable. No runtime
ordering, frame frequency, or authoritative server damage result is claimed.
