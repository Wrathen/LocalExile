# Buff expiration/removal and independent resource-rate updates (3.29.3b)

## Result and scope

The original parsed `0x1c2` handler can remove an `ignited` or `poison`
instance, complete its selected empty lifecycle callbacks and destroy its
storage, while an explicit network total-loss rate remains unchanged.
The subsequent original Life step continues integrating that rate even with
an empty Buff vector. A separate Stats update can stop the rate while a Buff
instance remains stored. These are positively composed conditional paths,
not a proof that every Buff callback is incapable of changing Stats.

An eligible local clock has another distinct transition: its remaining time
can cross zero without either component tick removing the instance or
clearing the explicit total-rate override. The clock controls supply the
eligibility and native row fields; they do not establish the ordinary
eligibility/default visual configuration of a particular ailment.

This extends [parsed Stats-to-Life composition](client-network-rate-composition.md),
[the existing clock contract](client-buff-clock-updates.md) and
[empty-backend selection](client-buff-default-backend.md).
It recovers a receiving-side synchronization boundary useful for server work.
It does **not** recover source-instance selection, aggregation, shared cap or
the producer of a target incoming rate. **R2/R3/R4 remain Partial.**

All controls interpret original instructions offline, with supplied state.
There was no native target execution, client launch/attachment, new capture,
server implementation or CI/deployment.

| Artifact | SHA256 |
|---|---|
| `Client/Game.exe`, 3.29.3b, preferred base `0x140000000` | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `Stats.json` | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| `BuffDefinitions.json` | `3a23c6fdf7ba29fb8508e5c9560a44e5292345e45dd2f886ef7126b8427fd659` |

## 1. Parsed removal reaches the actual instance and lifecycle

Handler `0x141e11720` reads the decoded opcode at packet `+8`, subtracts
`0x1c1` at `0x141e11749`, and selects value one for the `0x1c2` branch.
It loads the 16-bit instance key from packet `+0x20` and calls removal
`0x141e11fb0` at `0x141e11761`.
These are decoded object offsets, not claimed wire offsets.

The removal search ignores entries already carrying Buff `+0xd0` bit `0x10`
and compares the requested key with Buff `+0x9c`.
It removes the first eligible match, compacts the pointer vector and subtracts
eight from component `+0x168` at `0x141e12088`.
A missing key leaves the vector unchanged. Duplicate supplied keys select
the first non-removed match; this is not a claim that official servers issue
duplicate active keys.

The retained instance receives bit `0x10` at `0x141e12090` before callbacks.
For the selected null-backend case, the guard at `0x141e120a1` bypasses
backend slot `+0x18`. The original path still executes:

- Instance observer dispatch `0x14019a240`.
- Handle transfer `0x141e39110`, with empty handles in these controls.
- Component observer publisher `0x141690920`.
- Generic script dispatch `0x141de6000`, plus the actual definition-keyed
  script-map search, empty in these controls.
- Destructor `0x141e06650`, its selected container/handle cleanup helpers,
  and the final deallocation request at `0x141e1224a`.

The destructor sees the removal bit and skips a second instance-observer
notification. Its null-backend branch skips a backend destructor.
Granted-value allocation release executes the original container helper;
the external allocator/deallocator itself is a fixture.
Released storage is retained only as analytical memory for checking writes,
not read as a live instance by the subsequent Life step.

The original definition-index arithmetic `0x142454bd0` executes too.
Only the already-loaded resource check `0x1401326a0` is a fixture.
The supplied definitions match JSON rows zero (`ignited`) and ten (`poison`):
empty Id2, non-recovery, and one granted slot for native key 8215 or 8216.
Their [previously established backend lookup](client-buff-default-backend.md)
justifies the null-backend input. Empty external listener/script collections
are **additional supplied inputs**, not inferred from empty Id2.

## 2. Numerical removal and rate-clear controls

Every control uses the real network getter, setter, notification dispatcher,
original Life step and reducer from the earlier composition.
Native key 3362 is `total_damage_taken_per_minute_to_life`.
It is present as an explicit override; parent/native formula fallback is
therefore not invoked for that key.

Before the independent Stats update, removal neither calls the override
setter nor changes the stored override vector. Subsequent Life arithmetic
reads the unchanged rate. The controls also retain zero overrides for raw
fire/ignite/poison inputs; these do not replace the explicit aggregate.
This is not an execution of the source-to-aggregate formulas.

One witness starts with Life 100, carry `+0.25`, total loss 6,000 per minute,
zero recovery and supplied binary32 `dt = 0.033`.
Removal first leaves no Buff instances:

| Independently supplied update | First Life step | Second Life step |
|---|---|---|
| None | Life 97, carry about `-0.05`, rate 6,000 | Life 94, carry about `-0.35`, rate 6,000 |
| `3362 = 0`, due at checkpoint `0x4001` | Life 100, carry `+0.25`, rate zero | Same |
| `3362 = 0`, due at checkpoint `0x4002` | Life 100, carry `+0.25`, rate zero | Same |
| `3362 = 0`, due at checkpoint `0x4003` | Life 97, carry about `-0.05`, rate 6,000 | Life 97, same carry, rate zero |

The reused World contract starts its checkpoint counter at `0x4000`.
With zero recovery this full Life path still reaches two checkpoints per
step; a rate-clear due at the second can affect that step's later loss read.
The queue, decoded update, entity/component lookup, external policies and
observational Stats listener remain explicit inherited fixtures.
This is not an observed official message sequence or timing measurement.
The counter suffixes are scheduler checkpoints, not elapsed milliseconds.

The opposite ordering is also bound: directly executing the original Stats
setter with `3362 = 0` stops subsequent loss while leaving the supplied Buff
instance stored. Neither carrier alone reconstructs the other state.

The receiving-side composition also runs both parsed message types through
the original World checkpoint and dispatcher. Its removal arm obtains Buffs
at `0x142188135` and invokes the actual handler at `0x142188140`; the component
lookup result is a fixture. The separate Stats arm executes the real setter.
With removal due at `0x4001` and rate-clear at `0x4003`, the two Life steps
produce 97 then 97, with no Buff stored after either step. Reversing the
supplied carrier order, with rate-clear due first and removal next step,
produces 100 then 100 while the first step still has a Buff. Tests include
an earlier queued record delaying a later record with a lower checkpoint.
This reuses the peer-established scheduling contract, not a new claim about
official packet generation, queue ordering or a particular encounter.

## 3. Clock crossing zero is a third operation

The actual Buffs table `0x14359f5f8` installs primary tick
`+0x40 = 0x141e10620` and secondary tick
`+0x48 = 0x141e10690`, an exact five-byte jump to `0x141e106a0`.
The primary calls per-instance timer `0x141e10520`.

The controls retain the successful-startup client guard as nonzero and
supply nonzero configuration `component.+158 → +0x68` to permit ticking with
a null backend. Alternate visual row Buff `+0x120` is null.
The original resolver `0x141e069d0` therefore takes its base visual-row path.
Its complete explicitly delimited code ends at `0x141e06b83`; the following
eleven-entry jump table is retained as data, not extra decoded code.
Other resolver arms are byte-audited, not exercised by these clock controls.

For a positive remaining duration and eligible state, the original timer
subtracts binary32 `dt * storedSpeed`. Definition byte `+0x73` instead selects
speed one. Definition `+0xfc` with component `+0x17a` can hold advancement;
instance state four also bypasses advancement. These selected branches are
included without inventing semantic names for the native bytes.

With remaining time `0.01`, speed one and supplied `dt = 0.033`, the first
tick stores about `-0.023`. The secondary tick sees nonpositive duration and
does not invoke a backend. The next primary tick leaves that duration as is.
Both leave the instance stored and unmarked for removal; the independent
Life loss still produces 97 then 94 in the witness above.
Explicit `0x1c2` removal can empty the vector afterwards without clearing the
rate. Conversely, a separate rate-clear stops loss without emptying it.

Do not infer exact within-frame expiration compensation from this example.
The supplied explicit total rate is integrated for the supplied Life step;
the missing server publisher may choose a different outgoing update.
Nor does a chosen `0.033` fixture prove a fixed official server cadence.

## 4. Server-facing consequences and remaining research

A server reproducing these receivers needs to synchronize instance lifecycle
and the numerical resource-rate state as distinct contracts. Sending a Buff
removal must not be assumed to synthesize a total-rate update inside the
client. Likewise, displaying a Buff timer is insufficient evidence that the
client selected and aggregated its numerical contribution.

This does not specify which rate keys an official server sends, guarantee
that every selected Buff has empty listeners, or prescribe an invented
packet order. Additional subscribers, nonempty scripts/backends, absent-key
fallback, other messages and server-side expiry rules retain their own scope.
The open question remains how an authoritative source instance produces,
updates and withdraws the target's rate, including strongest-instance
selection, poison summation and shared cap. No such publisher was recovered
by this pass; the Hit amount-to-resource and owner-stat clock-refresh gaps
also remain unchanged.

## 5. Verification

Private ignored reproduction files are
`Tools/re/out/client-damage-audit/r32-buff-rate-independence.py`,
`r32-buff-rate-fresh.py` and `r32-buff-rate-independence/`.
They reuse the preceding instruction interpreter rather than invoking the
binary natively. Checks passed for **281 controls**: 156 parsed-removal/Life
compositions, 101 eligible-clock/Life compositions and 24 queued
removal/Stats/Life compositions, with **725,468 interpreted instructions**
and **1,222 visited instruction sites**.
Scalar comparisons are same-agent checks, not an independent review worker.

Fresh checks reproduce **55 function identities, 73 ranges and 5,142 decoded
instructions**, plus 41 instruction anchors, four pointer slots, actual
definition/key mappings and the resolver jump-table targets.
Most identities are reused; these counts are not newly completed mechanics.
Finite surviving resources, sequential execution, empty selected lifecycle
collections and supplied loaded-resource/allocator/queue/provider state are
explicit limits. Concurrency, exception paths and death are not exercised.
