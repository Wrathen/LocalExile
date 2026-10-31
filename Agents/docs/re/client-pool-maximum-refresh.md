# Client resource-maximum notifications and current-value authority (3.29.3b)

An actual Life Stats subscriber connects changes to `maximum_life`, `maximum_mana`
and `maximum_energy_shield` with the corresponding embedded pool's maximum-refresh
method. **On the two verified client World tables, this callback refreshes the
maximum without proportionally changing current resource.** Life clears its
fractional carry; Mana and ES preserve theirs. The callback also marks Life's
resource state dirty. These are concrete client/server and freshness boundaries,
separate from accepted-Hit damage and source-instance-to-incoming-DoT publication.

This extends [provider notifications](client-stat-evaluation.md),
[integer mutations](client-integer-resource-mutations.md) and
[resource timing](client-damage-timing.md). R0/R1 remain Established;
R2/R3/R4 remain Partial under the original criteria.
No client process, native target code, capture, server or CI was run.
Addresses are preferred VAs for image base `0x140000000`.

| Input | SHA256 |
|---|---|
| Original `Client/Game.exe` | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `Stats.json`, native key = JSON row + 1 | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |

## Actual subscription and selected receiver

Life constructor `0x141e678d0` installs listener table `0x1435a64b0` at
Life `+0x18` (`0x141e6793a/0x141e67941`). Its slot zero is `0x141e6c350`.
Initializer `0x141e67b40` loads retained Stats component `Life+0x168`, supplies
`Life+0x18` at `0x141e67b6d`, and calls its subscription slot `+0xb0` at
`0x141e67b75`; the returned token moves to `Life+0x260`.
The concrete Stats subscription and network-provider delivery contract is already
[bound separately](client-buff-stats-subscriptions.md#the-concrete-subscription-contract).
This is an actual subscriber, not an assumed consequence of every Stats read.

The three pool constructor calls pass native keys 247/248/249 in R8D.
Base constructor `0x141e65df0` stores that maximum key at pool `+0x18`, the
owner Life pointer at `+8`, and initial maximum/current at `+0x2c/+0x30`.
The listener's RCX is **Life+0x18**, not Life itself:

| Selected pool | Pool relative to Life | Maximum key | Installed pool table | Refresh `+0x38` |
|---|---:|---|---|---|
| Life | `+0x178` | 247 `maximum_life` | `0x1435a5da0` | `0x141e6b0a0` |
| Mana | `+0x1c8` | 248 `maximum_mana` | `0x1435a5b68` | `0x141e697e0` |
| ES | `+0x210` | 249 `maximum_energy_shield` | `0x1435a6218` | `0x141e697e0` |

`0x141e6c350` takes the changed key in R8D. It recovers the Entity from
`[listener-0x10]`, follows Entity `+0x78` to World, and calls World `+0x1b0`.
It compares the changed key with each stored pool maximum key and invokes the
matching refresh with **DL = (World result == 0)**
(`0x141e6c380..0x141e6c3c5`). Other stat keys have separate branches;
this report does not claim their complete numerical behavior.

Each maximum handler reads the new maximum from the owner's retained
`Life+0x168 → Stats+0x20` provider. It does not use the notification-source
provider in RDX for this read. Provider identity and inherited notification
source therefore must not be conflated.

## The client-mode gate and fractional-state asymmetry

The peer investigation's fixed
[result contract](https://github.com/LocalExile/LocalExile/blob/742e0b6103203b44aa043fae5bd4cde3d4917e22/Agents/docs/re/projectiles/results-and-protocol.md#6-stageend-batching-is-filtered-and-ordinary-queued-records-can-survive)
binds client World tables `0x1435da318` and `0x14346e0c8`.
Both have `+0x1b0 → 0x14164dc70`. Fresh bytes and both slots agree:

```text
return 1 + (World.byte984 == 2)
```

The complete five-instruction getter is `[0x14164dc70,0x14164dc7f)`.
All 256 raw byte values produce 1 or 2; neither produces zero.
Thus these installed client implementations supply DL=0 to the refresh.
This conclusion does not require naming the gameplay meaning of a zero result
on an unbound World implementation.

| Direct numerical behavior | Life refresh | Mana / ES refresh |
|---|---|---|
| Load new maximum and store pool `+0x2c` | Yes | Yes |
| With DL=0, adjust or clamp current `+0x30` | No | No |
| Clear fractional carry `+0x28` | Always, before the DL/current guards (`0x141e6b106`) | No direct assignment |
| Final owner notification | Life `+0xf0 → 0x141e684b0` sets `Life+0x268=1` | Same |

For example, old maximum 1,000, current 800 and new maximum 500 leave current
800 on this callback's selected client path. A carry of 0.25 becomes zero for
Life and remains 0.25 for Mana/ES. This is an offline supplied-state witness,
not an assertion that the official server publishes that transient state.
Later resource messages and integration are distinct operations.

The maximum refresh must therefore not be implemented as an unconditional
client `current *= newMax/oldMax`, nor as an unconditional current clamp after
each network stat update. Conversely, the carry clear is not limited to the
[revival-specific resource-message reset](client-damage-timing.md#absolute-assignment-and-fractional-state).
These are different, positively identified reset paths.

## Conditional rescaling code retained in the same executable

DL=1 has numerical behavior, but is **not** reached from the two client getters
above. Standalone controls supply that argument explicitly; they do not invent
a caller or assert ordinary client pre-deduction.

Let `f` be binary32 conversion/rounding. Before replacing the maximum, both
handlers calculate a percentage using the old stored maximum M and current C:

```text
P = 100                            if C > M
    f(f(f(C) * 100) / f(M))         otherwise, if M != 0
    0                              otherwise
X = f(f(f(newMaximum) * P) / 100)
```

The ordinary conditional branches preserve the following differences:

- Mana/ES temporarily clear pool `+0x24`, pass `CVTTSS2SI(X)` to absolute
  setter `0x141e698e0` with reason 5, then restore the byte. That setter clamps
  to zero and the unreserved maximum. Fractional carry remains unchanged.
- Life clears carry even when it does not adjust. It adjusts only if DL is
  nonzero **and old current is positive**. With positive new maximum but no
  positive unreserved capacity, a separate branch directly writes current 1.
  Otherwise it upper-saturates X at `2^31`, converts toward zero, imposes a
  one-current lower candidate when both old current and new maximum are
  positive, and sends the wrapping candidate-minus-current integer delta
  through its `+0x28` method with reason 5 and context zero. Its existing
  cannot-die/minimum-Life/recovery policies remain separate.

With old maximum 1,000, current 800 and new maximum 500, conditional rescaling
gives 400 for all three pools under zero extra Life-policy inputs. Full
reservation at positive Life can instead take the direct-one exception;
the generic Mana/ES setter clamps to zero. These are pinned client instructions,
not a recommendation for server reservation semantics.

An artificial upper-bound witness also distinguishes the implementations:
old maximum/current 100 and new maximum `INT_MAX` produce binary32 X=`2^31`.
Life's saturation gives `INT_MAX`; Mana/ES `CVTTSS2SI` produces `INT_MIN`,
then the absolute setter clamps it to zero. No gameplay reachability is claimed
for this synthetic limit. Do not replace the two bodies with one idealized formula.

## Discovery, verification and remaining boundary

The independent integer-dispatch discovery begins with four memory `FF`
call/jump encoding patterns for displacement `+0x28`, rather than the prior
Life-accessor caller list. It finds 1,003 raw matches in 724 unwind owners;
decoding 276,464 records yields 988 memory dispatches in 722 owners, with no
body-decode failures. Seventy-one bodies also contain selected embedded-pool
offsets. These counts are **navigation only**: offset co-occurrence, a byte
reason argument or an arbitrary `+0x28` method does not identify a resource pool.
Register-materialized targets, alternative encodings, cross-function parameter
provenance and unwind-less bodies remain outside this discovery boundary.

The positive focused result above uses 12 retained bodies / 20 original ranges /
1,294 decoded records. It includes constructor/subscription bodies inspected
statically, the two reviewed leaves, and the actual max/notification/setter paths.
**3,432 controls / 256,900 interpreted original instructions / 397 visited sites**
match a separately written scalar specification. They include all 256 raw World
mode bytes for each pool, no-adjust and explicit-adjust controls, zero/over-maximum
current, reservation, disabled-recovery restoration and the conversion limit.
The notification-source provider deliberately differs from the retained owner
provider. Same-agent verification is not an independent worker or native execution.

Provider lookups, reservation totals, policy maximum helpers and non-dirty-mark
notification consequences are fixtures. Additional Life-policy values default
to zero; their nonzero branches are not characterized by these numerical controls.
The real World getter and final dirty-mark leaf execute as original interpreted
instructions. Fresh checks retain 26 semantic anchors, ten concrete table slots,
the three constructor keys and seven percent/threshold literal references.
The peer R38 snapshot's 61 files, including 45 exact Git sources and parent
manifest hashes, were checked; its numerical audit was not rerun.

Ignored evidence lives under `Tools/re/out/client-damage-audit/`:
`r23-pool-slot-ingress.py`, `r23-pool-max-refresh.py`,
`r23-pool-max-fresh.py` and their named output directories.
[The metadata manifest](client-damage-evidence.json) retains the focused provenance.
No accepted Hit amount/critical/mitigation-to-pool binding, DoT instance-rate
publisher, strongest-instance selection or generic existing-Buff speed refresh
is supplied by this result. The original open gates remain open.
