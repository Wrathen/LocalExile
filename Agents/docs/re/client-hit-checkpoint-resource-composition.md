# Hit-result checkpoints and independent resource assignment (3.29.3b)

## Result and scope

An original Fireball controller/release path now composes with the actual
`ProcessResult`, its nested World checkpoints and an independent resource
assignment consumed inside that result call. The previous projectile composition
stopped at an observing `ProcessResult` fixture; this pass executes its original
body and returns to the original outer caller.

With supplied empty event/listener collections, hit-data and critical-feedback
qualifiers preserve all three resource pools. Separately supplied `0x1b1`
assignments stamped for a nested checkpoint change Life, Mana or ES before
`ProcessResult` returns. This establishes a conditional receiving/order contract,
**not** a 30-damage Fireball, local numerical hit resolution or a global claim
that no client listener can change resources. **R2/R3/R4 remain Partial.**

All controls interpret original instructions offline. No client/native target,
new capture, server implementation or CI/deployment was executed. Preferred
image base is `0x140000000`; object offsets are not wire offsets.

| Artifact | SHA256 |
|---|---|
| `Client/Game.exe`, 3.29.3b | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |

## 1. Fixed upstream evidence and the new connection

The parallel projectile investigation's fixed R60 revision
`4e9c9a39363a8de49c672d1c2c393a11e7a4fe20`,
`Agents/docs/re/projectiles/world-scheduling.md` section 10.1, establishes the
original controller/Actor release, World checkpoint, generic dispatcher and
Actor `0x1af/0x1b0` handlers. Its 106 snapshot files, including 56 exact Git
sources and parent-manifest identities, were checked before reuse. Manifest
SHA256 is `0e79559abb7c02b71e7bdbb32c537d4ff342244b25b67d6209f6000cfd9618b5`.
This is a locally retained fixed revision, without a claim of GitHub availability.
The peer's standalone numerical audit was reused, not rerun; the new downstream
composition and its original-byte checks are this pass's work.

This extends [actual result/resource processing](client-hit-result-resource-composition.md)
and the [action-present Fireball continuation](client-hit-action-present-composition.md).
Two interpreters share one byte-memory dictionary. At original indirect callsite
`0x141e90ec4`, a mechanical ABI bridge transfers the existing registers/flags,
pushes the original return address and executes original `0x141e91580`.
It checks stack balance and preservation of RBX/RBP/RSI/RDI/R12–R15 and XMM6–15,
then transfers the actual returned state to the outer interpreter. It neither
constructs a new result nor computes an adapter damage value.

The source Actor, supplied current Fireball Action, existing result pointer and
provider pair remain the original call's RCX/R9/RDX/R8 arguments. The Action's
required empty-arena initialization executes original slice
`0x141937f47..0x141937f5f`. The full Action constructor is not replayed.

## 2. The nested checkpoints are application opportunities, not frames

The witness uses an explicitly supplied active ordered World queue, nonzero
mode, ordering key 77 and initial counter zero. A queued `0x1af` stamped 1 enters
the source Actor's result queue before release; a matching processing `0x1b0`
stamped 2 removes it after one or three motions are prepared. Result processing
then makes three further actual World `+0x90` calls:

| Position | Original callsite | Counter on entry → own increment before dispatch | Actor processing depth on entry |
|---|---|---|---:|
| Actor, before forwarding release | `0x141e939c5` or `0x141e93f3c` | 0 → 1 | 1 |
| After movement preparation | `0x141bb23df` | 1 → 2 | 1 |
| Inside `ProcessResult`, early | `0x141e915cf` | 2 → 3 | 2 |
| Inside `ProcessResult`, later | `0x141e91da7` | 3 → 4 | 2 |
| Result exit, after its depth decrement | `0x141e92273` | 4 → 5 | 1 |

The original World drain advances its cursor before dispatch. While the outer
drain remains active, the result's three checkpoint entries see TLS depth 1;
resource messages consumed by those nested drains see TLS depth 2. On returning
through both interpreters, the Actor and TLS depths are zero again. The same
World/counter/TLS memory is shared; these are not independently reset fixtures
for each nested call.

These counters are checkpoint ordering state. Five application opportunities
inside one supplied controller dispatch are **not five simulation ticks**, a
33 ms measurement or evidence of arbitrary instruction interruption. Raw
nonzero mode and prepared ordered input do not establish mid-release intake in
Predictive; the upstream report separately documents its initial-batch behavior.

## 3. Numerical witness: an assignment can land inside result processing

Each pool starts at current 100, fractional carry 0.25 and reservation zero.
Baseline controls vary event channel 0/10, one/three motions, hit-data false/true
and critical-feedback mask zero/`0x20`. With the supplied empty listeners and
events, all baseline pools remain unchanged and the consumer returns 1.

For assignment controls, the result is hit-data true with mask `0x20`. A third
parsed record is independently supplied for the same target, with the resource
selector 0/1/2, absolute current `70 + selector`, reservation `selector` and bit 4
excluding the separate loss-listener path. Original generic dispatch, selector
routing, assignment body `0x141e69fb0` and current-value store `0x141e69fd1` execute.

| Third record stamp | Applied before result return? | Selected pool's final current | Ordered cursor after dispatch |
|---:|---|---:|---:|
| 3 | Yes, first nested checkpoint | `70 + selector` | 3 records |
| 4 | Yes, second nested checkpoint | `70 + selector` | 3 records |
| 5 | Yes, result-exit checkpoint | `70 + selector` | 3 records |
| 6 | No; future record remains pending | 100 | 2 records |

Unselected pools and fractional carry remain unchanged; an applied record sets
the supplied reservation too. In the Life/selector-zero witness, 100 becomes 70
because an independent absolute assignment says 70. No numerical amount was
derived from the Fireball result and no hit-to-assignment source attribution is
claimed. The evidence distinguishes the mechanisms, not their official message
ordering or causal origin on the server.

Hit-data cases also reach original action callback sites
`0x141e92053/0x141e920ae`, whose selected final Fireball slots are actual no-op
`0x1400f0f00`, and actual typed event builders `0x141de8600/0x141e95620` with empty
configuration. False hit-data controls do not reach those sites. With zero
requested motions, only the first outer checkpoint executes: the result remains
queued, no `ProcessResult` bridge is entered and the counter ends at 1.

## 4. Verification and explicit supplied boundaries

Ignored local evidence under `Tools/re/out/client-damage-audit/`:

- `r53-result-join.py` and `r53-result-join/{verification,frontier}.json`:
  **42 supplied-state cases**: 16 baseline, 24 assignment and two zero-emission
  controls. They execute **423,264 upstream + 62,420 downstream instructions**,
  visiting 3,411 and 1,542 original sites respectively, **4,742 distinct sites**
  in union. Eighteen resource assignments apply; 32 cases reach the hit callbacks.
- `r53-result-fresh.py` and `r53-result-join/{audit,fresh-check}.json`:
  independently specified expected pool/cursor/checkpoint outcomes, complete case
  identities, original callsite and preserved-register assertions, all visited
  interpreter instructions compared with freshly decoded PE bytes, **43 numerical
  function identities / 64 ranges / 4,896 decoded records**, **155 upstream
  executable ranges**, 16 instruction anchors, ten pointer slots and five switch
  entries. Inline switch data is retained separately from executable decoding.
- `coordination-r60-reuse.json` and exact `r53-peer-r60/` snapshot bytes:
  fixed revision/manifest and all 106 copied file digests. The original frozen
  producer mains are not rerun or edited.

The 43 numerical identities already existed in the evidence manifest; this pass
adds a composition, not 43 newly recovered mechanics. A newly retained World
`+0x190` pointer slot supplements the existing original getter evidence.

Supplied inputs remain material: parsed message records and their ordering/stamps,
active/nonzero-mode World/core state, TLS storage and object/component/type/provider
bindings; source/target resource-policy values, current Fireball Action and empty
native/script listeners/events; inherited release geometry, motion/emission
storage and empty-owned cleanup; allocator/free boundaries. The numerical
zero-return support stub is inherited explicitly. Shared byte memory and the
ABI bridge are interpreter apparatus, not original client source.

This pass does not execute wire decoding, queue production, complete Action or
World construction, nonempty script/event programs or an official schedule. It
does not recover accepted-Hit damage sampling, critical/mitigation-to-pool, DoT
instance selection/publication/shared cap, or generic existing-Buff clock refresh.
Those remain the original R2/R3/R4 obligations. Future server work should preserve
the observed distinction between result delivery, nested message checkpoints and
resource authority while continuing to establish their missing numerical links.
