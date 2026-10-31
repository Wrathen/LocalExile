# Empty-key Buff backend selection and base lifecycle boundaries (3.29.3b)

## Result and scope

For the selected startup registry, `ignited` and `poison` select **no backend**.
Their empty `BuffDefinitions.Id2` is looked up as an ordinary empty UTF-16 key;
lookup failure returns a null backend rather than constructing a generic one.
The separate factory-callback route also skips its callback after a miss.

This closes the empty-key/default-backend question. It does **not** establish
that an ignite/poison Buff has no numerical effects: the base lifecycle still
publishes notifications and dispatches scripts. Neither a source token nor a
concrete incoming damage-rate contribution has been recovered through those
boundaries in this cut.

This supplements [DoT routing](client-dot-routing.md),
[source publication](client-dot-source-publication.md), and
[the network/clock contract](client-buff-network-clock-contract.md).
All addresses are preferred VAs for the original 3.29.3b executable, image base
`0x140000000`, SHA-256
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`.
Only static existing artifacts were inspected; no client launch, attachment,
capture, server change or CI operation was performed.

## Table identity and the two lookup routes

The pinned `BuffDefinitions` extract declares:

| Zero-based row / native key | Id | Id2 | GrantedStats row / native key | IsRecovery |
|---|---|---|---|---|
| 0 / 1 | `ignited` | empty string | 8214 / 8215 | false |
| 10 / 11 | `poison` | empty string | 8215 / 8216 | false |

Native keys are table row plus one. These are declarations, not recovered
instance damage amounts. As established in the network contract, the selected
packet reader sizes a granted-value vector from the definition, but reads its
numerical payload only for `IsRecovery`; these two definitions take the
zero-filled vector path.

Actual backend creation is `0x141e05fa0`:

1. R8 is the actual Buff. `0x141e05fc4/0x141e05fc8` load its definition through
   `Buff+8`, then the definition's `+0x74` UTF-16 Id2 pointer.
2. The function constructs that exact string, hashes its UTF-16 **bytes** with
   FNV-1a64, then calls map lookup `0x1401b1cf0` at `0x141e06060`.
   Empty input skips the hash loop at `0x141e0602a`, retaining the basis
   `0xcbf29ce484222325`.
3. A found, non-sentinel entry supplies its factory at entry `+0x30`, whose
   virtual slot zero is called at `0x141e06081`.
4. A null or sentinel result branches to `0x141e060d8`, which writes **zero**
   into the output backend pointer. There is no alternate factory on that path.

`0x141e05e40` is a separate factory-callback route. It reads the definition's
same `+0x74` field at `0x141e05e7c`, constructs/hashes that key, and calls the
same map lookup at `0x141e05f10`. Its null/sentinel branches
`0x141e05f1c/0x141e05f22` go directly to cleanup; only a found factory receives
the virtual `+8` call at `0x141e05f39`.

The map lookup is strict. `0x1401b1d24` loads the requested string length;
`0x1401b1d49` loads the stored length; `0x1401b1d5e/0x1401b1d61` reject unequal
lengths before comparing UTF-16 characters. Empty input therefore cannot match
a nonempty name even on a hash collision. This is not wildcard or default-key
dispatch.

## Binding the startup registry, rather than assuming its contents

Getter `0x141e06110` returns the singleton at `0x144755a40`. Fresh initialization
clears its element count at `0x141e0617f`, allocates a self-linked sentinel, and
fills the initial hash bucket pairs with that sentinel. It does not insert a
default factory.

Startup owner `0x1400fe9f0` calls the two registration roots consecutively:

| Call site | Registration root |
|---|---|
| `0x1400feaeb` | `0x141e07370` |
| `0x1400feaf0` | `0x1415be830` |

The selected catalogue includes both root bodies and their 54 directly called
registration helpers. All calls within those 56 bodies were inventoried: they
are direct calls to selected registration helpers, the singleton getter,
string construction/destruction, the insertion helpers, allocation/free, or
initialization/failure support. There is no unclassified or indirect call in
those registration bodies.

| Registration form | Calls | Key evidence |
|---|---:|---|
| Direct `0x1415d34b0` insertion | 129 | Original literal bytes and explicit constructor length verified |
| Name-taking constructor `0x1415d0610` | 2 | Caller R8 literals: `OrionDeathZone`, `EffectFacingOwner` |
| Name-taking constructor `0x1415cfb00` | 5 | Caller R8 literals listed below |
| Value-slot helper `0x1415d2120` | 1 | `CorrosiveShroudBuff` string, through `0x1401304d0` |

The five `0x1415cfb00` caller keys are `BloodBarrierBuff` at `0x1415c10a6`,
`DamageAbsorptionBuff` at `0x141e07c50`, `QuickGuardBuff` at `0x141e07c9f`,
`BoneArmourBuff` at `0x141e07cee`, and `ArcaneCloakBuff` at `0x141e07d3d`.
The constructor scans the supplied name before insertion; it does not replace
that name with an implicit default.

Together these are **137 registration call sites, 120 distinct names, zero empty
names**. Duplicate names do not create an empty key. Thus the actual declared
empty Id2 misses this startup catalogue. This is a static bootstrap conclusion,
not an observation of a live map, or a proof excluding arbitrary later registry
mutation or hooks elsewhere in the program.

## What null means through the actual Buff lifecycle

Constructor `0x141e06290` zeros `Buff+0x148` at `0x141e064c3` using R15, cleared
at `0x141e062b6`. Insertion `0x141e11dc0` calls backend creation at
`0x141e11e65`, loads the returned pointer at `0x141e11e6a`, and stores it to
the retained Buff's `+0x148` at `0x141e11e7b`. It therefore preserves the null
selection, rather than installing a second backend afterwards.

| Selected operation | Backend virtual call | Null branch bypasses it |
|---|---|---|
| Insertion's optional callback | `+0x50` at `0x141e11ec9` | `0x141e11ec4 → 0x141e11ecc` |
| Partial update | `+0x10` at `0x141e11c6c`; `+0x20` at `0x141e11c90` | `0x141e11c67`; `0x141e11c8b` |
| Replacement, before/after copying | `+8` at `0x141e067ef`; `+0x10` at `0x141e069a2` | `0x141e067ea`; `0x141e0699d` |
| Removal | `+0x18` at `0x141e120a6` | `0x141e120a1 → 0x141e120a9` |
| Destruction | Slot zero at `0x141e06695` | `0x141e0668b → 0x141e06697` |

The base constructor, insertion, partial update, replacement, removal and
destructor, plus the selected handle/container cleanup callees, have no direct
call/tail-call to the recovered contribution adaptor `0x14193c850` or native
add/remove/replace entries `0x1424fc480/0x1424fc770/0x1424fc9e0`.
This is a bounded direct-call inventory, not transitive closure through every
callback or proof that all Buff stat contributions are disabled.

## Remaining numerical boundary

Insertion still publishes the actual Buff through `0x141690920` at
`0x141e11ee5` and dispatches script events through `0x141de6000` at
`0x141e11f00/0x141e11f71`. Removal publishes through `0x141690920` at
`0x141e121b2` and dispatches scripts at `0x141e121cd/0x141e12234`.
Partial updates likewise retain publisher and other indirect callback paths.
The listener collections/handle cleanup paths are separate from the nullable
`+0x148` backend and must not be erased by this result.

No concrete `ignited`/`poison` source-token-plus-amount publication into incoming
native damage rates has been closed through those observer/script boundaries.
The next useful lead must identify such a numerical operation, its destination,
source identity and update/removal pairing. Repeating the named visual backend
or treating the empty key as an unexamined default would not close that gap.

## Reproduction and limits

Private audit `r12-buff-default-verify.py` verifies pinned PE/table hashes,
original function ranges and instruction bytes, exact lookup/null/lifecycle
anchors, all 137 named registration literals and direct-call classification.
Result: **PASS — 81 functions, 93 ranges, 9,334 instructions, 52 semantic anchors,
137 registration sites, 120 distinct names, no empty names**.
The audit never executes target code or reconstructs an observed runtime map.

Independent `r12-buff-default-review.py` rereads all 81 functions and 49 additional
semantic anchors, and reconstructs reachability of all 56 registration bodies.
It interprets the original strict lookup for 124 forced-bucket-collision cases:
empty input misses every one of the 120 nonempty registered names, while a
counterfactual empty/empty pair matches. Two factory miss/sentinel cases both
write a null backend. These synthetic controls do not observe a live registry.

Pinned table SHA-256 values:

- `BuffDefinitions.json`:
  `3a23c6fdf7ba29fb8508e5c9560a44e5292345e45dd2f886ef7126b8427fd659`.
- `Stats.json`:
  `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da`.
