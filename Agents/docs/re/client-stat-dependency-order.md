# Native stat dependency order and transaction work queues

This report recovers the ordering algorithm behind the native transactions in
[stat evaluation](client-stat-evaluation.md#3-native-objects-and-actual-evaluation).
It describes the pinned 3.29.3b executable, SHA256
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`.
Stats SHA256 is `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da`;
keys are one-based, with 23,346 rows. All execution controls below interpret original
instructions offline. They do not run the client or observe gameplay.

The native evaluator uses an order derived from declared dependencies and propagates
changed results through a sorted work vector. It is not a recovered general fixed-point
solver. The earlier description of that vector as unconditionally duplicate-free was
too strong: actual registration declarations contain duplicates, and insertion can retain them.

## 1. Registration stores both directions without deduplication

Registry entry `global[0x1445c6ec8] + key * 0x50` contains:

| Offset | Meaning in the audited algorithm |
|---|---|
| `+0` | Formula callback, possibly null |
| `+8..+0x10` | Reverse dependencies: initially dependent stat keys, later evaluation ranks |
| `+0x20..+0x28` | This output's declared input keys |
| `+0x40` | Callback eligibility mask |

Registration helper `0x142513630` stores the callback at `0x142513665`.
For each declared input it appends the output key to that input's reverse vector
(`0x142513670..0x1425136b1`), then copies the declaration into the output's forward vector.
There is no deduplication in this helper. Forward and reverse vectors have different roles;
neither is a record of all stat reads performed by a callback.

Reinterpreting the **previously discovered** 2,539 registration bodies through their actual
registration call recovers 20,384 declared edges. All bodies in that mapping were decoded;
this is not a new claim that every registration mechanism in the executable is covered.
Twenty-four declarations repeat an input, covering 27 repeated `(output, input)` pairs.
Examples relevant to damage and recovery are:

| Output key | Output | Repeated declared input |
|---|---|---|
| 1634 | `total_base_life_regeneration_rate_per_minute` | 2980 |
| 16891 | `intermediary_mana_recovery_per_minute_from_regeneration` | 7 |
| 1555 / 1556 | Minimum / maximum intermediate spell added lightning | 3 |
| 1573 / 1574 | Minimum / maximum intermediate main-hand added physical | 1 |

These are declaration multiplicities, not instructions to multiply a formula's numerical result.

## 2. Startup constructs a declared-dependency order

Initializer `0x1428e68b0` allocates scratch bit vectors and a circular list, then calls
`0x1428e6720(key)` for keys `1..23346` at `0x1428e69a2..0x1428e69b6`.
The recursive helper implements these steps:

1. If the key's active bit is set, enter the diagnostic/exception path at `0x1428e6851`.
2. If its completed bit is set, return at `0x1428e6778 → 0x1428e6827`.
3. Set active; recursively visit the input keys at entry `+0x20..+0x28`, in stored order.
4. Set completed and clear active at `0x1428e67b8..0x1428e67cc`.
5. If that input vector is nonempty, insert the key at the list's front
   (`0x1428e67d0..0x1428e6824`). Empty-input keys are not inserted.

The initializer drains the **tail**, preserving dependency-first postorder. At
`0x1428e6a47..0x1428e6a58` it writes both maps:

```text
rankToKey = global[0x1445c6ee0]
keyToRank = global[0x1445c6eb0]
rankToKey[nextRank] = poppedKey
keyToRank[poppedKey] = nextRank
```

It then replaces every reverse-vector key with `keyToRank[key]` at
`0x1428e6aa0..0x1428e6aac` and sorts each vector at `0x1428e6ad6`.
The declaration vector remains in key space. Sorting does not remove repeated ranks.
Cycle handling is a diagnostic path, not iterative convergence or an ordering priority field.
The controls stop at entry to that diagnostic path; they do not execute exception formatting/unwinding.

Using the recovered declaration graph, the original traversal across all 23,346 keys
agrees exactly with an independently written scalar depth-first traversal: 2,535 outputs
receive ranks. Four mapped callbacks declare no inputs: keys 13754, 13755, 13756 and 16850.
This algorithm alone does not schedule them; their other invocation paths are not established here.
Missing registrations are empty fixtures in this reconstruction, so its numerical rank values
must not be presented as independently observed live-client registry indices.

For a concrete dependency example, 12270 (physical ceiling) precedes 448 (additional physical
reduction). Changing 116 queues 12270; changing 12270 queues 448 and other declared dependents.
The recovered [reduction equations](client-reduction-inputs.md) supply the numerical operations;
this report supplies the ordering of their declared edges.

## 3. One transaction grows and walks the ordered vector

`0x1424fb5f0(context, changedKey)` reads the changed key's reverse vector, whose entries
are now ranks. For each entry it performs unsigned lower-bound insertion into context
`+0x10..+0x18`. On an equal entry it skips insertion. After an insertion it advances its
search start past the inserted element (`0x1424fb756`); it relies on the reverse vector's order.
Entries already processed by the outer loop are not removed from this vector.

At outer transaction completion:

- `0x1424fd3b3` decrements nesting depth. A nonzero remaining depth returns immediately.
- Changed contribution entries at owner `+0x1b0..+0x1b8` queue their reverse dependencies
  and changed-key records (`0x1424fd3d3..0x1424fd3ec`).
- The loop at `0x1424fd490..0x1424fd4de` loads a rank, maps it through `rankToKey`, and calls
  actual dispatcher `0x1424fcc80`. After every call it reloads the vector base and length;
  newly inserted later ranks can therefore run within the same transaction.
- The context is detached/released at `0x1424fd4f0..0x1424fd503`. There is no backward
  restart or repeated sweep in this loop.

The dispatcher supplies the propagation rules:

| Case | Effect on the work graph |
|---|---|
| Non-null callback, nonzero eligibility mask disjoint from owner context | Skip callback and downstream propagation |
| Callback result equals old value | Write cache/value-valid bit; stop before record notification and downstream propagation |
| Changed callback result | Publish output, notify through the existing helper, queue reverse dependents and changed-key record |
| Null callback and raw value zero | Return without propagating |
| Null callback and raw value nonzero | Activate permitted context bits, queue reverse dependents and changed-key record |

The unchanged-value early exit is at `0x1424fcd2c..0x1424fcd2e`; reverse enqueue is at
`0x1424fce7f..0x1424fce95`. Thus an input can change without forcing all transitive outputs
to run if an intermediate output stays equal. A different directly changed path may still
have queued those downstream outputs independently.

For unique reverse entries and callbacks following the declared graph, ranks enforce
dependency-before-dependent processing and a previously queued rank is not added again.
This guarantee does not extend to arbitrary callback mutations or undeclared read/write edges.
Later child propagation and observers can open further transactions; the absence of a
fixed-point sweep in this work loop does not make every listener or reentrant update inert.

## 4. Actual duplicate declarations qualify the uniqueness claim

The helper's advancing search start means two equal ranks in **one** reverse vector can
both be inserted into an initially empty work vector. Starting a later call from the vector's
beginning can recognize existing entries; it does not remove duplicates already present.

This was checked through actual registration, recursive ordering, rank rewrite and insertion
instructions for all 24 duplicate-containing declarations in isolated reserved-capacity graphs.
All 27 repeated pairs retain the expected multiplicity. The larger recovered graph also yields:

| Changed key | Queued entries | Distinct entries | Selected repeated output |
|---|---|---|---|
| 2980 | 18 | 17 | 1634 |
| 3 | 157 | 155 | 1555 / 1556 |
| 1 | 65 | 61 | 1573 / 1574 / 1603 / 1604 |
| 7 | 38 | 37 | 16891 |

These are reproducible offline queue states under the stated declaration reconstruction,
not gameplay observations. The completion loop has no independent visited-rank filter, so
repeated entries can reach dispatch. This does **not** establish duplicate damage, repeated
resource loss or a client bug: unchanged outputs return before publication, eligibility can
skip callbacks, and this queue is a stat evaluator rather than a concrete damage-event queue.

## 5. Evidence and remaining boundaries

Ignored local evidence is under `Tools/re/out/client-damage-audit/r19-stat-order/`.
Its focused `audit.json` pins seven original bodies, nine unwind-owned spans and 1,531 decoded
instructions. `final-audit.json` additionally binds the registration bodies: 2,546 bodies,
2,548 spans and 49,663 decoded instructions in total, not all claimed executed. The controls include:

- 193 unique-edge graphs, 3,548 enqueue calls, a duplicate-declaration counterexample,
  one complete key-range traversal, three cycle-entry cases, 24 dispatch-loop cases and
  three nested-end cases: 2,702,566 interpreted instructions.
- 2,539 original registration prefixes: 43,189 interpreted instructions and 20,384 edges.
- 24 actual duplicate declarations through native registration/order/insertion helpers:
  27 pair controls and 121,662 interpreted instructions.
- Complete key-range ordering of the recovered graph plus seven queue witnesses:
  2,667,743 interpreted instructions, compared with an independent scalar traversal.

Allocator/free, memory-move and sort are explicit host fixtures. Vectors have reserved capacity;
growth, allocation failure and exception unwinding are not tested. Numerical callback bodies,
old-value getters and notification handlers in dispatch-loop controls are fixtures. Existing
materialized output records avoid the separate record-allocation branch. The outer-loop controls
end before child/observer handling; nested-end controls execute their actual early-return path.
These qualifications prevent a model check from being mistaken for native runtime verification.

The ordinary network provider remains an explicit override/fallback wrapper; this algorithm
does not make received raw pairs start native evaluation. Likewise declared dependencies do
not retroactively supply the missing actual reads identified in the reduction equations.
No universal freshness conclusion follows from the graph alone.

R0/R1 remain Established and R2/R3/R4 remain Partial. Accepted Hit → numerical mitigation/pool,
DoT source instance → target incoming-rate selection/publication/expiry, and generic existing-Buff
timing refresh retain their original completion criteria. This result closes a native ordering
question and corrects the queue contract; it does not substitute for those numerical boundaries.
