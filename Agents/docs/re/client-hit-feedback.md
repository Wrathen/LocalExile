# Client hit feedback: action queues and control effects

## Scope

This is static analysis of the executable identified in
[client damage calculation](client-damage-calculation.md).
It follows incoming result records beyond their Render subscribers, without treating a feedback record as a damage roll.
The [protocol page](../protocol/29-client-combat-signals.md) owns the wire read sequence.
Addresses and original-byte ranges are retained in the [manifest](client-damage-evidence.json).
No client execution or new packet capture was performed.

## Result dispatch and action-associated contexts

`0x141e90f50` has two paths selected by decoded record `+0x48`:

- Nonzero: process immediately through Actor virtual `+0x230`, bound to `0x141e91580` in the audited Actor tables.
- Zero: insert an `0x88`-byte representation into Actor's vector `+0x200..+0x208`, capacity end `+0x210`.
  Ordering uses the tuple at record `+0x40` (u16), `+0x42` (u8), `+0x43` (u8).
  The entity field at `+0x44` additionally distinguishes records when retrieving one result.

The immediate path matches primary action `Actor+0x1b0` or a secondary action in `+0x1e8..+0x1f0`.
The match compares action `+0x11a` to record `+0x40`, and, for secondary actions, action `+0x11c` to record `+0x42`.
It uses an action-associated shared stat context when present in action `+0x1b8..+0x1c0`.
The selected entry is indexed by action `+0x174` with `0x20`-byte stride.
Otherwise it asks `0x141ee7e70` through action `+0xf0` for the skill's provider.
The provider's actual native/network binding is a separate question; obtaining a context does not establish a roll.

The instruction-level forwarding matters here:
the Actor result consumer receives the record, the shared context and, when available, the matching action.
Decompilation without callee signatures can omit some of these arguments.

### Comparing the action fields with PR #373

The [ranged-action report at PR #373's reviewed head](https://github.com/evrydayjunglist/evryPath/blob/b0afa90ea5c58e9082099d887180df8299dcdaf3/Agents/docs/dev/ranged-monster-actions-port.md)
reports official-server arrow and Sand Spitter results with the second byte after
the action sequence set to **2**, and calls it a "skill index". Its server writer
uses that parameter name. This pass has not inspected those private capture
bytes; the reported count and values remain author-reported observations.
The original executable supplies a more precise distinction between the fields:

| Wire position after the common header | Decoded field | Established consumer meaning |
|---|---|---|
| u16 | record `+0x40` | Action sequence; compared with action `+0x11a` |
| First u8 | record `+0x43` | Progress/phase key; queued retrieval compares it with action `+0x120` |
| Second u8, named `skillIndex` in that writer | record `+0x42` | Primary/secondary action selector; zero selects the primary action, nonzero searches secondary actions for matching `+0x11c` and sequence |
| u32 | record `+0x44` | Referenced target Entity and part of an exact queued-result key |

The byte order is verified at reader `0x14195dde0`: the first byte is written
to destination `+3` at `0x14195de0a`, and the second to `+2` at `0x14195de1f`.
The immediate consumer makes the primary/secondary distinction at
`0x141e90f7f..0x141e90ff4`; there is no skill-list-index lookup in that branch.
The field also differs from action `+0x174`, which indexes the associated stat
context with a `0x20`-byte stride. These three bytes should not be conflated.

This distinction has a positive producer binding, rather than just a guessed
name from a comparison:

1. Action construction `0x141938020` initializes action `+0x11c` to zero at
   `0x14193821f`.
2. `0x14193daf0` receives an action and a byte. It writes the supplied byte to
   action `+0x11c` at `0x14193dbbf`, then appends that same action pointer to
   the owning Actor's secondary-action vector `+0x1e8..+0x1f0`.
3. In the concrete Fireball-bound producer `0x141bb13a0`, the `EDX == 1` branch
   obtains an identifier through `0x141421a10` at `0x141bb183a`. It obtains a
   new action through Fireball slot `+0x450` and passes that identifier to
   `0x14193daf0` at `0x141bb1aa4`.
4. That slot is concretely `0x1433361d0 → 0x1413dfbb0`. The factory allocates
   `0x530` bytes and calls `0x1413dfbf0`, whose store at `0x1413dfc0c` installs
   the same Fireball table `0x143335d80`. Thus the subordinate action's concrete
   type is bound; the factory is not merely an unresolved virtual call.
5. `0x141421a10` lazily creates a shared two-byte counter through the pointer at action `+0x160`,
   initializes it with u16 value **1**, and forwards to `0x14193dc60`.
   The latter increments the low byte and returns it. On that fresh counter the
   first identifier is **2**; its `0xff` branch resets the low byte to 1 before
   incrementing, and increments the second byte.

The table/clone in steps 3–4 are the inspected **Fireball base-table** path.
The final client factory replaces primary table `0x143335d80` with `0x14333ac40`;
its `+0x450` clone is `0x1413dfc70` and restores that client table after the base copy.
The identifier allocator remains the recovered shared producer, but a base clone is not proof
of the final client clone's complete behavior. See the [final-factory correction](client-hit-production.md#concrete-action-binding).

Consequently the report's repeated value 2 is consistent with an allocated
secondary-action identifier. It does not establish a universal value to send
for every projectile, or prove that the captured bow/mortar action uses this
exact Fireball branch. Identifying the matching action matters: the immediate
path still forwards a record when no action matches, but supplies an empty
associated context and a null action. A mismatch therefore need not suppress
all target feedback; it can change which action/context reaches later callbacks.
This does not turn that context into evidence of a numerical damage calculation.

The report's changing common-header u32 and u16 also have a separate established
role. The reader stores them at message `+0x14/+0x18`; the queued-frame scheduler
uses those fields to interleave messages with entity/component updates
(`0x142184742`, `0x1421847db..0x142184817`). See
[message timing](client-damage-timing.md#lockstep-order-is-interleaved).
They are distinct from the action-result key, and the changing u32 should not
be relabelled random hit seed on this evidence. The exact reported values and
their live scheduling context have not been independently rechecked here.

## Queued lifecycle

Queued results have concrete consumers; they do not simply wait for an arbitrary Life tick:

| Function | Established operation |
|---|---|
| `0x141939f40` | Finds one queued record matching an action's sequence/sub-action/phase and a supplied entity; removes it and invokes Actor `+0x230` with that action and its context |
| `0x14193a170` | Increments action byte `+0x120`, selects its matching record range, and passes the range to `0x141e91330` |
| `0x141e930e0` | Selects records for a secondary action, processes the range, then removes the action from Actor's secondary-action vector |
| `0x141e91330` | Filters a selected range through `0x141e91500`, compacts retained records, then invokes `+0x230` for extracted records |
| `0x141e90c30` | Finds and removes a result by the exact record tuple; a boolean argument determines whether the removed result is also processed |

`0x141e91500` distinguishes two cases.
A record whose `+0x48` is nonzero is copied into the temporary output range and removed from the queue.
For a zero flag, the world virtual `+0x1b0` result is checked.
When it returns zero, Actor virtual `+0x238` receives the record key with processing flag zero, and the filter removes it.
Otherwise the filter retains the record.
This is a conditional protocol/action lifecycle, not evidence of accumulating unprocessed damage amounts.
In particular, entering `0x141e91330` through a stage/end call is not equivalent to consuming every
ordinary queued record. Kind-bit-zero records survive the nonzero-mode branch of this predicate;
the mode/kind result must be retained when describing any concrete action's feedback route.
The parallel projectile audit binds the selected predictive/lockstep World getters to values 1/2;
ordinary kind-bit-zero records are therefore retained by this stage/end filter in those paths.
A zero getter instead removes them without processing; nonzero kind selects processing.
Immediate `0x1af` and matching queued `0x1af` plus processing `0x1b0` remain separate positive ingress
contracts. This correction does not require inventing a local area-target loop or asserting that
the official server always chooses either route. The imported eight-range snapshot is recorded
with the [final-factory cross-check](client-hit-production.md#concrete-action-binding).

The direct-call scan retains 293 callers of `0x141939f40` and 559 callers of `0x14193a170` in unwind-owned bodies.
These counts establish broad reuse across actions, not that every caller's skill semantics have been recovered.
There are also unwind-less candidate references outside that scan's validated coverage.
Action-specific contact and prediction behavior must still be examined where needed.

## Target effects are broader than Render

The result consumer `0x141e91580` resolves the referenced target and applies relationship and stat guards.
Besides the previously established source/target Render signals, it conditionally invokes target Actor slots:

| Slot | Binding in audited Actor tables | Established behavior |
|---|---|---|
| `+0xc0` | `0x141e96630` | Frozen-time guard; optional timed control action from record fields; target listeners |
| `+0xc8` | `0x141e96500` | Frozen-time guard; chooses a control-action identifier using the record's directional byte |
| `+0xd0` | `0x141e96790` | Computes and starts a knockback displacement, subject to the current-action guard |
| `+0xe0` | `0x141e95e60` | Requests timed action interruption/control behavior; includes avoid-interruption handling |

These bindings are Actor methods.
They must not be confused with identically numbered Life or pool virtual slots.

For the `+0xc0` branch, instructions `0x141e96675..0x141e966bb` inspect mask bit `0x400`.
They convert decoded record `+0x34` from an unsigned integer to seconds by division by **1000**,
then call Actor `+0xe0` with identifier `0x2e` and a true flag.
Failure clears that mask bit and the corresponding duration field in the working record.
This gives the field a concrete duration use; it must not be labelled a damage amount.

`0x141e95e60` checks current-action eligibility and may query `avoid_interruption_%` (439).
The guarded chance branch calls `0x1404d03d0` with an Actor-associated state and the range 0..99.
It can cancel/replace control actions, populate a timed control record, and write Actor `+0x26c/+0x274`.
Actor tick `0x141e8ef50` later decrements that timer using its time argument and an action-rate input.
This is a real random/control branch, but it is **not an accuracy, critical or damage roll**.
The complete gameplay taxonomy of these control-action identifiers is not recovered here.

The [received-result qualifier supplement](client-hit-result-qualifiers.md) establishes
current-target stun/leech admission and distinct mask/duration rewrites before later callbacks,
the `+0x3a` quantized-direction input, and a selected critical-feedback use of mask bit `0x20`.
Its zero-duration suppression path can still invoke target listeners; it must not be
simplified into discarding the complete hit event or recomputing numerical damage.

The knockback method reads `enemy_knockback_direction_is_reversed` (2712),
`virtual_knockback_distance_+%` (12307), and `knockback_speed_+%` (13596) in the paths shown by its original bytes.
When a result record supplies the corresponding parameters, it uses the record's signed fields instead.
Instructions `0x141e969ef..0x141e96a63` construct a destination from Positioned coordinates and
call `0x141e34050`, followed by `0x141e325e0` to restore the saved orientation.
This positively identifies movement/control effects, rather than resource subtraction.

## Trigger callback boundary

`0x141e91580` also invokes action virtual `+0x2d0/+0x2d8` and
`0x141e95620` with event identifier `0x6876`.
The latter selects callbacks from Actor's type/event tables and invokes a supplied callable.
The callable constructed at `0x141e92183` installs table `0x1435aa4a8`.
Its invoke slot `+0x10` is the small thunk `0x141ea08a0`, which forwards into `0x141de5ec0`.
This is an event-dispatch edge; the event's complete script/effect consequences remain a separate reachability task.
The presence of this branch prevents describing the entire feedback consumer as presentation only.
Named Fireball/Melee factory bindings and the event-to-bytecode/native-callable path are now traced in
[the action/event supplement](client-hit-resource-boundary.md#7-concrete-action-callbacks-fireball-and-melee).
This resolves those selected action targets; arbitrary event programs and wrapper-action variants remain separate.

## Evidence and remaining questions

The retained batches are `r3-hit-consumers`, `r3-hit-target-and-life`, `r3-actor-loop`,
`r3-feedback-queue`, and the callback/predicate entries in `r4-world-scheduler`.
The PR #373 comparison adds `r3-pr373-action-identities`,
`r3-pr373-subaction-allocation` and `r3-pr373-fireball-subaction-{factory,copy,base}`.
The ignored `r3-pr373-hit-key-verify.json` independently rechecks original-byte
range hashes, exact instruction boundaries and the Fireball factory pointer.
The supplementary bounded `r3-pr373-key-scan.json` was a lead-finding scan of
byte stores in one address region; unrelated objects share the same offsets,
and its coverage is not an absence proof.
The byte copier `0x141e9edf0` and callback thunks have explicit bounded leaf ranges;
other listed functions use chained PE unwind ownership.

Established: immediate/queued dispatch, action-context selection, conditional queue removal,
timed control requests, a guarded interruption roll, knockback and event forwarding.
Unclosed: the complete action `+0x2d0/+0x2d8` implementation set and downstream event/script effects;
any independent concrete defender accuracy/critical/mitigation/resource-deduction path.
Neither a missing damage field in this particular reader nor absence from one bounded callback proves
that every possible client path lacks numerical hit calculation.
