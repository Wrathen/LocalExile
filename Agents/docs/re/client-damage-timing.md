# Client damage timing, message order and resource correction

## Scope

This static investigation follows world scheduling into entity/component updates and the native Life integrator.
The binary/data identities and evidence policy are in [client damage calculation](client-damage-calculation.md).
It establishes conditional client instruction order, not current official-server frame frequency.
Previously recorded LocalExile runtime measurements remain historical evidence in [recovery timing](recovery-timing.md).
No client was executed or attached for this investigation.

## Two scheduling paths

World update `0x142183930` branches on world byte `+0x984`.
Zero selects the predictive path; a nonzero value selects the queued-frame path in this function.
The existing [networking reference](../protocol/07-latency-visibility-movement.md#6-lockstep-not-served-yet)
describes the surrounding configuration values and protocol.

| Boundary | Predictive branch | Queued-frame branch |
|---|---|---|
| World update input | Uses the supplied client update time | Adds scaled client elapsed time to accumulator `+0x988` |
| Step eligibility | Calls world virtual `+0x360` on this update | Requires a queued duration and enough accumulated elapsed time |
| Logical step duration | Supplied time argument | Queued unsigned milliseconds converted to binary32 seconds by division by 1000 |
| Catch-up | No queued-frame loop in this branch | Can execute several queued frames; backlog changes a speed multiplier |
| Secondary update | Calls virtual `+0x368` with client update time | Separately consumes accumulated secondary-update time |

The original instructions preserve XMM1 as a floating time argument.
A decompiler's guessed `uint param_2` on `0x142183ce0` is not evidence of integer-second stepping.
`0x14218395c..0x142183978` contains the predictive branch calls.
`0x1421839c7` loads the float constant **1000**; the queued loop calls `+0x360` at `0x142183b02`.
The backlog policy uses thresholds 1, 2, 30 and 60 with factors 1.01, 1.5, 2 and 5.
These factors alter how quickly queued steps are consumed, not their individual advertised durations.

Incoming `0x2b6` reaches the frame-duration setter through `0x1421872b0`.
The small setter `0x141645fa0` stores its argument at world `+0x11c`.
In the nonzero-mode intake path, `0x2b5` can append a null message delimiter and enqueue that advertised duration.
The zero-mode path discards `0x2b5` as a frame driver.
The queue stores durations; a literal 33 ms is not imposed by this scheduler.
There is no rounding of every predictive update into fixed 33 ms steps in these bodies.

World step `0x142183ce0` appears at pointer slot `0x1435da678`;
the corresponding primary world table is `0x1435da318`, slot `+0x360`.
Constructor `0x142182aa0` loads the table address at `0x142182ad2` and installs it at `0x142182ad9`.
The derived-table pointer is also referenced in the destructor `0x142184bb0`.
These bindings connect the virtual call to a concrete world implementation.

## Message application relative to component ticks

### Predictive order

In the predictive branch of `0x142183ce0`:

1. The queued world messages are dispatched with `0x142187480` at `0x142183d6b`.
2. The processed message queue is cleared.
3. Eligible world entities are traversed; `0x1421845b0` updates each entity.
4. Later world processing includes `0x142184db0`, `0x141ec5380` and other subsystem updates.

`0x1421845b0` selects between the effective time and original time according to world predicates
and entity flags `+0x8c` bits `0x20/0x40`.
Instructions `0x142184640..0x14218465b` iterate entity component pointers in `+0x10..+0x18`
and call each component's virtual `+0x40` with the selected float time in XMM1.
The Life primary table `0x1435a7340` binds `+0x40` to `0x141e6a190`.
This closes **world step → component tick → Life integration**.

Consequently a predictive `0x1b1` assignment or `0x1b3` stat update queued for this world step
is dispatched before that step's component integration.
This says nothing about an unread packet still in the socket, packet arrival frequency, or HUD render time.
The world pause flag `+0xb9c` sets the effective time to zero; the selected original/effective time
and the Life component's own guards remain material.

### Lockstep order is interleaved

The nonzero-mode branch cannot accurately be summarized as "all messages, then all object ticks".
It consumes a queued frame and dispatches messages in several phases, using decoded header `+0x14/+0x18`
and world cursor `+0x9b0/+0x9b4`.
`0x142184680` demonstrates the component boundary:

- It dispatches eligible messages preceding the current entity.
- For each component index, it writes `index << 12` into world `+0x9b4`.
- Before calling that component's `+0x40`, it dispatches matching-header messages whose ordering value
  does not exceed that cursor (`0x1421847d3..0x142184817`).
- It then ticks that component at `0x14218482a` with the selected frame time.

The later entity range uses `0x142184870`, which has a different pre-entity message phase
and still ticks the entity's component vector through `+0x40` at `0x1421849b3`.
Further message phases and queue compaction occur before `0x142183ce0` returns.
The branch also distinguishes special header values such as zero, -2, -1 and the `0xc0000000` cursor.
Those are observed routing/ordering guards; this report does not rename every sentinel as an original protocol term.

Resource/stat updates in this mode must therefore be placed using the actual header and component cursor,
not solely by their arrival order or by assuming one universal beginning-of-frame correction point.
The [combat-signal page](../protocol/29-client-combat-signals.md) deliberately distinguishes decoded header offsets
from wire offsets and unrecovered semantic names.

## Resource ordering inside Life

Life tick `0x141e6a190` preserves its supplied time and, when its alive/world/spectating guards allow it:

```text
Life pool step  0x141e6ae70   // call 0x141e6a1fa
ES pool step    0x141e6b480   // call 0x141e6a209
Mana pool step  0x141e6cba0   // call 0x141e6a218
Life spending virtual +0xe0 // call 0x141e6a226
deferred death-prevention recovery/notifications, if flagged
```

The time argument is forwarded in XMM1 to each of these first four calls.
This is a per-pool sequence, not a guarantee that all three pools recover before any pool takes damage.
The Life pool first calls recovery integration `0x141e6b320` at `0x141e6aea6`,
then processes its other continuous gain/loss and lethal/nonlethal damage rates.
The ES and Mana steps also contain distinct recovery, loss and overflow paths.
Cross-pool overflow makes their relative order significant.
The [DoT routing report](client-dot-routing.md) records which stat outputs each route consumes.

Within an integration step, the rate is expressed per minute and the delta is `rate / 60 * dt`.
The common loss helper `0x141e6a6b0` combines the float delta with pool fractional carry `+0x28`,
truncates a whole-number change, writes integer current `+0x30`, and retains the fractional remainder.
The pool reducers separately implement nonlethal limits and overflow.
This permits small changes to accumulate across updates; it is not a minute-reset allowance.
It also does not imply arbitrary sub-frame event splitting or integration of a continuously varying rate.

The [resource read-order audit](client-resource-read-order.md) identifies separate capture points inside a step.
Recovery saves the total rate before a World boundary and reads pool state later; Life reads later loss channels
after earlier loss processing, while ES saves three loss rates before its first loss operation.
Actual threshold/loss callback boundaries demonstrate conditional numerical consequences with disclosed script fixtures.
The later [checkpoint/resource composition](client-resource-checkpoint-composition.md) reuses the projectile
investigation's fixed World contract and runs an already-parsed `0x1b1` through the original checkpoint,
dispatcher, setter and subsequent recovery arithmetic. A second-checkpoint assignment affects Life gain
in this invocation while remaining pending in the selected Mana/ES controller paths. The checkpoint is a
component-local counter, not elapsed time. Wire parsing, queue production and official checkpoint choices
remain separate, as does any assumption of a single snapshot for all step inputs.

## Absolute assignment and fractional state

Incoming `0x1b1` is routed to pool setter `0x141e69fb0` by `0x142187480`.
That setter assigns decoded current, flat reservation and percentage-reservation input to pool `+0x30/+0x10/+0x14`.
It does **not** reset fractional carry `+0x28` in its audited body.
Thus a later integration reads the assigned integer with whatever fractional state survives the surrounding path.
There is no input-history rewind or replay in this setter.
The notification paths below must be considered separately from the assignment itself before making a whole-program
rollback/correction claim.

There is an explicit exception in the surrounding **Life** selector branch of world handler `0x142187480`.
After the setter, an old positive/current nonpositive transition calls Life `+0xe8` at `0x142188109`.
An old nonpositive/current positive transition calls Life `+0x100` at `0x142188123`,
then **zeros Life-pool fractional carry** at `0x142188129`.
EDI was zeroed at the handler entry `0x1421874aa` and remains zero on this selector path.
The installed Life table binds these methods to `0x141e6c710` and `0x141e6c760` respectively.
They set byte `+0x1e2` on the object reached through `Entity+0x98` to zero/one and dispatch additional callbacks;
the latter also invokes event context `+0x70` through `0x141de6000`.
This is a concrete transition-specific reset, not a reset on every resource assignment.
The [Buff clock guard audit](client-buff-clock-producers.md#a-decoded-life-assignment-clears-the-guard-on-revival)
binds the latter callback to the retained Buffs component and its conditional advancement-guard clear.
The Mana/ES selector branches return after the setter and do not execute that Life-only block.

A separate [maximum-stat subscriber](client-pool-maximum-refresh.md) adds another concrete carry-reset path.
On the two verified client World tables, maximum Life/Mana/ES notifications refresh the pool maximum without
proportional current-value adjustment. The Life refresh clears its carry even on that no-adjust path;
Mana/ES preserve theirs. This does not change the assignment setter's behavior or imply a reset on every stat.

### Assignment notifications and recharge delay

The setter is not just a silent integer store. Its guarded consequences are:

| Guard in `0x141e69fb0` | Dispatch and concrete binding |
|---|---|
| Current or either reservation field changed | Life `+0xf0 → 0x141e684b0` marks Life byte `+0x268 = 1` |
| Current changed and equals the unreserved maximum | Life `+0x108 → 0x141e6a0a0` |
| Current changed to zero | Life `+0x118 → 0x141e6a110` |
| Current decreased and `(decoded message[+0x2c] & 0xfd) == 0` | Pool `+0x58`, with distinct Life/Mana/ES implementations |

The unreserved-maximum test subtracts `0x1401f1f90(pool)` from pool maximum `+0x2c`, clamped at zero.
That helper combines flat reservation `+0x10` with a rounded-up percentage contribution from
the virtual maximum and `+0x14 / 10000`. Thus `+0x14` is a reservation input, not the message's loss-reason flags.
The helper preserves its actual binary32 and signed-conversion operations; the table above describes its role.

The full/empty methods only enter their observed ES branches when the pool identifier is `0x590`
and ES has a nonzero unreserved maximum. The Life constructor passes `0x590` when constructing the ES pool.
`0x141e6a0a0` calls Life `+0x110` then sets byte `+0x258`; `0x141e6a110` iterates listeners through
`0x14176f670` with callback thunk `0x140cea0a8`, then sets byte `+0x259`.
These are conditional notifications; they are not a hit roll or proof of universal death/revive processing.

For the loss notification, the installed tables bind Life pool `+0x58 → 0x141e6bcb0`,
Mana `→ 0x141e6bd30`, and ES `→ 0x141e6bd80`.
Life loss forwards to ES's loss notification when `virtual_energy_shield_protects_mana` (5338) is zero;
Mana loss forwards when it is nonzero. The common pool path can call `0x141e6bde0` when pool byte `+0x44` is zero.
That helper reads `energy_shield_recharge_not_delayed_by_damage` (2334); when it is zero,
it updates pool recharge-delay float `+0x40` using `virtual_energy_shield_delay_-%` (3926):
the ordinary branch stores `2 / (1 + value / 100)`, while `value <= -100` calls `0x14015a670` instead.
That reviewed leaf returns the binary32 positive-infinity constant at `0x1436a48d4` (bits `0x7f800000`).
Incoming resource loss can therefore influence later recharge timing even though it arrives as an absolute total.

The [recharge-control audit](client-recharge-rate-authority.md) binds the reverse notification:
a nonzero recharge-active stat clears a strictly positive delay and sets pool `+0x44` to suppress loss-driven reset.
The common recovery step clears that byte and processes its timer, then independently reads the total recovery rate.
The installed Life active-state publication slot is an exact no-op; timer expiry does not itself add recharge
to that total.
This is an ordered control/rate boundary, without inferring arbitrary sub-frame splitting or packet timing.

The Life/ES loss-notification bodies also use a one-time byte guard at Life `+0x26e` and may call
`0x141de6000`, which constructs an event context and enters `0x141de5ec0`.
That is the event interpreter boundary also encountered in Hit feedback; its complete callable/script consequences
are not classified as presentation only. The ordinary assignment body and these immediate receiver bindings are
established; exhaustive listener/script effects and a whole-program rollback absence claim remain separate.

This established client behavior is better described as continuous local integration plus incoming absolute assignment.
It does not by itself establish independent numerical hit prediction or a server's internal correction policy.

## Evidence and remaining boundary

Hit entropy provides another concrete state lifecycle on the same component scheduler.
The Stats component initializes a 3.0-second float countdown, decrements it by the selected simulation
delta only while entropy is initialized, and marks it for re-seeding when the countdown becomes nonpositive.
Expiry does not replace the entropy integer; the next eligible decision does so and refreshes the countdown.
See [the constructor, exact tick and actual caller bindings](client-hit-production.md#entropy-initialization-and-expiry-use-the-actual-component-tick).
This is neither a wall-clock measurement nor a fixed number of 33 ms ticks, and it does not establish
the timing of a Buff's contribution to damage rates.

The retained batches are `r4-world-scheduler`, `r4-world-binding`, `r4-world-constructor`, `r4-object-tick`,
`r4-pool-assignment-effects`, `r4-pool-recharge-notify`, `r4-pool-notification-leaves`,
`r4-life-death-assignment`, and the earlier Life/pool audits identified in the main manifest.
The frame setter is an explicitly bounded leaf; other scheduler functions use PE unwind roots/chains.

Established: mode-dependent scheduling, advertised-duration conversion, predictive message-before-tick order,
lockstep component interleaving, Life/ES/Mana step order, rate integration, fractional carry and the local assignment body.
Still separate: complete downstream listener effects, source-effect expiry/rate-production timing,
any independent numerical hit deduction, and current official-server cadence/arithmetic.
For the dated GGG explanations and PoB's `0.033` constants, see [external comparison](client-damage-comparisons.md).
