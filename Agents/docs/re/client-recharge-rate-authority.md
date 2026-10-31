# Client recharge control and aggregate recovery-rate authority (3.29.3b)

The actual Life Stats subscriber can clear a resource pool's recharge delay
when its recharge-active stat becomes nonzero. Its next recovery step treats
that delay and the **total recovery rate** as separate inputs. The installed
client Life table's active-state publication method is an exact no-op;
counting down a local recharge timer does not itself publish a new active stat
or add the recharge-rate stat to resource recovery.

This closes another concrete numerical freshness and C/S boundary. It extends
[maximum notifications](client-pool-maximum-refresh.md),
[timing and assignment](client-damage-timing.md) and
[network/native Stats authority](client-stat-evaluation.md).
R0/R1 remain Established; R2/R3/R4 remain Partial. Accepted-Hit damage and
DoT instance-to-target rate publication retain their original open gates.
No client process, native target code, capture, server or CI was run.
Addresses are preferred VAs with image base `0x140000000`.

| Input | SHA256 |
|---|---|
| Original `Client/Game.exe` | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `Stats.json`; native key = JSON row + 1 | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |

## Typed pools and distinct rate/state keys

The actual Life constructor `0x141e678d0` supplies each embedded pool's keys
to `0x141e65df0`. Its stores bind recovery key `+0x1c`, recovery-block key
`+0x20`, recharge-rate key `+0x38` and recharge-active key `+0x3c`.
The two rates have per-minute units and distinct identities:

| Pool / Life offset | Total recovery key | Recovery-block key | Recharge-rate key | Recharge-active key |
|---|---|---|---|---|
| Life `+0x178` | 1422 `life_recovery_per_minute` | 1937 `cannot_recover_life` | 9542 `life_recharge_rate_per_minute` | 15464 `life_recharge_active` |
| Mana `+0x1c8` | 1423 `mana_recovery_per_minute` | 7945 `virtual_no_mana_recovery` | 21324 `mana_recharge_rate_per_minute` | 21325 `mana_recharge_active` |
| ES `+0x210` | 1424 `energy_shield_recovery_per_minute` | 7959 `no_energy_shield_recovery` | 252 `energy_shield_recharge_rate_per_minute` | 15466 `energy_shield_recharge_active` |

Pool `+0x40` stores a binary32 delay; `+0x44` is a separate byte used to
suppress delay reset. Neither field is fractional resource carry `+0x28`
or recovery-block byte `+0x24`.

The [actual subscription](client-pool-maximum-refresh.md#actual-subscription-and-selected-receiver)
retains Life's Stats listener at Life `+0x18`. Callback `0x141e6c350` receives
that interface, a notification-source provider and the changed key.
The resource step later reads the retained owner's provider at
`Life+0x168 → Stats+0x20`. These provider roles remain distinct.

## Actual notifications change control state

For a matching recovery-block key, `0x141e6c3c8..0x141e6c429` reads the
notification-source provider's numeric getter and stores its nonzero predicate
to the selected pool's `+0x24`. It does not assign current resource or carry.
The [actual recovery adder](client-damage-timing.md#resource-ordering-inside-life)
subsequently uses that byte to reject recovery.

The three recharge-active branches at `0x141e6c44c/0x141e6c46c/0x141e6c48c`
have this exact sequence:

1. Require the earlier World `+0x1b0` result to be nonzero.
2. Query the changed key through the notification-source provider's nonzero
   getter `+8`; a zero result leaves control state unchanged.
3. Require the selected pool's existing delay to be **strictly positive**.
4. Set pool `+0x44=1`, write delay `+0x40=0`, then invoke pool `+0x60`
   (`0x141e6c4aa..0x141e6c4c5`).

Both previously bound client World tables select `0x14164dc70`, returning
`1 + (World.byte984 == 2)`. All 256 raw byte values satisfy the nonzero gate.
A positive-infinity delay also passes the positive test and is cleared.
An already zero or negative delay does not set the byte or invoke `+0x60`.
The callback does not deduct or restore resource, and does not clear carry.

Life/Mana `+0x60 → 0x141e6c1c0` conditionally tail-calls owner `+0xb0`
when current differs from the nonnegative unreserved maximum. ES
`+0x60 → 0x141e6c200` first dispatches through owner interface `+0x20`,
then rechecks the unreserved maximum before owner `+0xb0`.
The installed Life table's `+0xb0` is the exact `ret 0` leaf
`[0x1400f0f00,0x1400f0f03)`. The ES event can have external subscribers;
their transitive consequences are not classified here.

## Recovery-step order and rate authority

The three actual pool steps `0x141e6ae70/0x141e6b480/0x141e6cba0` each
call common recovery controller `0x141e6b320`. The existing World/Life tick
guards and resource order remain as documented in
[the timing report](client-damage-timing.md#resource-ordering-inside-life).
Within this controller, let `U` be the nonnegative unreserved maximum,
`C` current, `T` delay and `R` the selected recharge-rate stat:

```text
pool.resetSuppressionByte = 0
R = ownerProvider[rechargeRateKey]
activeProposal = false

if U != 0 and R <= 0:
    resetDelayUsingOwnerStats()
else if T > 0:
    T = binary32(T - suppliedDt)
    if T <= 0:
        pool.virtual60()
else if R > 0 and C < U:
    activeProposal = true

owner.virtualC0(rechargeActiveKey, activeProposal)

if C < currentUnreservedMaximum or carry < 0:
    totalRate = ownerProvider[totalRecoveryKey]
    if totalRate > 0:
        pool.virtual40(binary32(binary32(float32(totalRate) / 60) * suppliedDt),
                       reason = 14)
```

The original instructions retain fresh reservation checks at their separate
sites. The pseudocode does not cache them across possible callbacks.
If a positive delay crosses zero, that branch invokes `+0x60`; the
active-proposal branch is not revisited in the same invocation.
This describes the proposed Boolean, not a one-frame postponement of resource
integration: the later total-rate block is independent of these branches.

The actual installed Life table `0x1435a7340` binds **`+0xc0` to the same
exact `ret 0` leaf**. The controller's attempt to publish an active flag
therefore has no effect through that method on this client implementation.
The ordinary network getter and explicit-zero/fallback contract remain the
[separately established receiver](client-stat-evaluation.md).

Two supplied-state examples demonstrate the numerical distinction. With
current 100, maximum 1,000, no reservation, carry 0.25 and a 0.033-second step:

| Recharge rate | Total recovery rate | Initial delay | Result on this controller path |
|---:|---:|---:|---|
| 100/min | 600/min | 2 seconds | Delay becomes about 1.967; current stays 100 and carry becomes about 0.58 |
| 100/min | 600/min | 0.01 seconds | Delay becomes about -0.023; the same recovery delta is integrated |
| 100/min | 0/min | 0 seconds | No recovery-adder invocation; current/carry stay 100/0.25 |

Thus the recharge-rate input controls local recharge state, while the total
recovery input supplies the integrated numerical delta. These offline witnesses
deliberately vary supplied inputs independently; they do not claim the server
normally sends mutually inconsistent states. A server reconstruction must
establish its own contribution to the total rate and its active-state updates;
adding recharge again merely because this local timer expired would duplicate
an already included contribution when the received total contains it.

## A concrete notification-order rule

The existing [absolute-resource assignment path](client-damage-timing.md#assignment-notifications-and-recharge-delay)
can invoke pool `+0x58` after eligible resource loss. Its common receiver
`0x141e6bd80` skips delay-reset helper `0x141e6bde0` when pool `+0x44` is
nonzero. The one-time owner/script notification precedes that test and is
not suppressed by it.

This gives a positively bound sequence on the ES pool:

```text
positive delay -> active notification -> delay 0, reset-suppression byte 1
              -> ES loss notification -> delay stays 0

positive delay -> active notification -> common recovery step clears byte
              -> ES loss notification -> delay resets from current owner Stats
```

When delay was already zero, the active notification does not set suppression;
an ensuing loss notification can reset delay. The reset helper's previously
bound policy remains unchanged: key 2334 can leave it untouched; key 3926
otherwise supplies `2 / (1 + value/100)`, or positive infinity for values
at most -100. The statement is about these ordered invocations, not arbitrary
sub-frame event splitting, packet timing or a universal period of immunity.

## Verification and original remaining gaps

The focused audit retains **22 bodies / 26 original ranges / 1,552 decoded
records**, including explicit reviewed no-op, infinity and float min/max leaves.
**1,862 controls / 146,135 interpreted original instructions / 443 visited
sites** agree with separately written scalar rules. These include all 256
World mode bytes per pool, signed notification values, distinct owner/source
providers, zero/negative/infinite delays, delay crossing, recovery blocks,
reservation/full-resource states, fractional carry and eight ordered
notification/recovery/loss sequences. This is same-agent verification, not an
independent worker or native execution.

Fresh original-PE checks bind 48 semantic anchors, 14 concrete table slots,
nine constructor keys, five literals and three actual pool-step callers.
Owner/source provider reads, reservations and policy maxima, World `+0x90`,
the ES signal dispatcher and script listeners are fixtures. Actual `+0xc0`
and `+0xb0` no-op leaves, the World mode getter, dirty-mark and min/max leaves,
and the selected notification/controller/recovery arithmetic execute as original
interpreted instructions. NaNs, full transitive event listeners and every
outer gameplay guard are outside these numerical controls.

A fresh repeat of the existing R3 memory-dispatch navigation reproduces its
exact 667-callsite set; it supplies **zero new dispatch sites**. The repeat
finds 848 raw selected `+0x48` encodings in 553 unwind owners; fresh decoding
covers 323,178 records and 667 matching dispatches in 448 owners.
Seventy bodies also contain selected
pool offsets. This is an explicitly untyped candidate inventory; it did not
bind an additional accepted-Hit or source-DoT amount. Its calls and offset
co-occurrences are not additional semantic discoveries or global-absence proof.
Register-materialized targets, other encodings and cross-procedure receiver
provenance remain outside that navigation method.

Ignored evidence is under `Tools/re/out/client-damage-audit/`:
`r24-recharge-boundary.py`, `r24-recharge-fresh.py`,
`r24-float-loss-ingress.py` and their named outputs.
[The evidence manifest](client-damage-evidence.json) seals their provenance.
The result establishes another recovery-control/rate authority contract;
it supplies no accepted-Hit amount-to-pool path, DoT instance selection,
aggregation or expiry publisher, or generic existing-Buff speed refresh.
