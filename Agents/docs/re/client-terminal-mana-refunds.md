# Conditional terminal Mana refunds (3.29.3b)

Two original numerical slices use
`recover_%_of_skill_mana_cost_per_unspent_chain_up_to_50%` to reach the
constructor-bound **Mana integer-delta receiver**, followed by its absolute
setter. One is bound to selected FireMortar action tables. The other is a
reason-qualified slice whose actual incoming caller remains unbound here.
Neither is a target Hit-damage amount or a DoT source-rate publisher.

This extends [integer resource changes](client-integer-resource-mutations.md)
and the [Hit-to-resource receiver audit](client-hit-resource-boundary.md).
R0/R1 remain Established; R2/R3/R4 remain Partial under the original criteria.
No client launch, attachment, native execution, capture or server change occurred.

## Identity and the bounded discovery cut

The original 3.29.3b executable has SHA256
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`;
addresses are preferred VAs at image base `0x140000000`.
The Stats extract has 23,346 rows, native key = row + 1, SHA256
`91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da`.

The search starts with the previously retained **940 direct references in 908
owners** to the literal-Life accessor `0x14013dda0`. Each retained reference is
freshly checked against original instruction boundaries and its destination.
Their 163,322 decoded records yield eight slot-`+0x28` candidates in eight owners,
four in bodies also containing embedded-pool offsets. This is navigation, not
908 resolved receivers or an exhaustive pool-writer census. The register-target
variant is limited to 16 preceding instructions in the same linear block.
Parameter-passed Life/pool pointers, other getters, cross-block target copies,
arbitrary aliases and unwind-less bodies are outside that discovery cut.

The two Mana sites below are positively typed by value flow:

```text
action +e8 → owner context +8 → Entity
Entity → literal-Life accessor → Life +1c8 → Mana pool
pool table +28 → 141e6b6e0 → 141e698e0 → integer current +30
```

Pool `+0x28` as a **data field** is fractional carry; table slot `+0x28` is
the integer method. These separate meanings must not be conflated.

## Inputs and exact arithmetic

| Native key | Input |
|---|---|
| 21260 (`0x530c`) | `recover_%_of_skill_mana_cost_per_unspent_chain_up_to_50%` |
| 3673 (`0xe59`) | `virtual_number_of_chains` |
| 2583 (`0xa17`) | `uses_no_mana` |
| 11890 (`0x2e72`) | `mana_cost` |

`0x141ee7320` zero-initializes a 32-byte result, reads eight cost stats and
stores `mana_cost` in its **first** dword at `0x141ee734e`. Its layout differs
from the adjusted payment record in the earlier cost report; that record has
a leading kind and Mana cost at `+4`. The refund reads the first dword of
this separate helper result, rather than a remembered amount actually paid.

Let `W` mean signed32 wrapping and `F` mean rounding to binary32. With supplied
coefficient `p`, current chain limit `n`, used count `u=action.+458` and selected
cost `c`, the numerical slice computes:

```text
percent = min_signed(W(W(n - u) * p), 50)
product = uint32(W(percent * c))
delta = trunc_to_int32(F(F(product) / 100.0f))
```

The unsigned widening is visible: `MOV EAX, product` clears the upper half of
RAX; `CVTSI2SS XMM0,RAX` then converts that nonnegative 64-bit value. It is not
a signed32-to-float conversion. The maximum unsigned product divided by 100
fits signed32, so these finite inputs do not require an out-of-range SSE result.
There is no lower percent clamp in either audited slice. Synthetic negative or
overflowing inputs can therefore produce a large positive delta; those controls
preserve original arithmetic without claiming such stats occur in gameplay.

For stable ordinary inputs `p=10,n=4,u=1,c=41`, percent is 30 and the delta is
12. With enough unused chains the percent becomes 50, giving 20 for cost 41.
The Mana wrapper/setter subsequently clamps current to its unreserved maximum;
this does not change the initially calculated refund amount.

## Selected FireMortar callback

The concrete constructor `0x141b800c0` passes action hash `0xeb04` to its parent,
then installs table `0x1432a7c08` at `0x141b800e3`. The selected client factory
`0x14129f6f0` calls that constructor at `0x14129f7d6`, then replaces the table
with `0x1432a83a0` at `0x14129f7e2` on its allocation branch. Its preceding
trap/mine/totem alternatives remain separate. Both tables bind
`+0x528 → 0x141b80a30`.

ActionTypes zero-based row 373 identifies hash 60164 as **FireMortar**; table
SHA256 is `eef7b004b13a3f32e487d5c102c41362b62131f83249f45194ece05b8518d9d6`.
This is not ordinary Fireball. The five already-studied ordinary bow/wand, KB,
TS, BA and client-Fireball tables have an empty `+0x528` instead; the FireMortar
binding must not be generalized to them.

After the callback's preceding geometry/continuation decisions reach
`0x141b80c96`, this resource slice:

1. Requires nonzero 21260 and `used < first read of virtual_number_of_chains`.
2. Reads the chain limit again and calculates the capped percent above. The two
   reads are separate; the numerical controls use a stable provider fixture.
3. Starts selected cost at zero. Action byte `+0x11f`, a true `+0x358` result,
   or present owner Stats with nonzero `uses_no_mana` bypasses cost retrieval.
4. Otherwise calls the cost helper on the selected action stat-set provider
   and uses its first dword.
5. If the source Entity resolves Life, calls **its Mana pool**, with the
   calculated delta, source Entity ID as context and reason **14** at
   `0x141b80dd2`. A zero selected cost can still reach a zero-delta call.

Both installed tables bind `+0x358 → 0x14193aa00`, whose actual body tests
the owner's `uses_no_mana`; it is included in the numerical interpretation.
The earlier geometry/return eligibility is not executed by these controls.
This closes the conditional resource slice and table installation, not actual
ordinary-network invocation or complete FireMortar behavior.

## Separate reason-qualified slice

Body `0x141baf5f0` retains incoming EDX in R13D. Its slice begins at
`0x141bafac4`, after earlier continuation/resource work, and requires:

- reason zero;
- nonzero 21260;
- nonzero first read of `virtual_number_of_chains`.

Unlike the FireMortar slice, its guard is **not** `used < total`. It rereads
the limit before subtraction, caps the wrapped percent at 50, applies the
same zero-cost qualifications and helper, then reaches Mana `+0x28` at
`0x141bafc09`, again with source ID and reason 14. The reason-zero gate uses
the original incoming reason, not the outgoing resource reason.

The numerical and receiver rules are established for supplied slice inputs.
This pass has no positively bound actual incoming caller for this body. It
therefore does not assert that this is the ordinary-client terminal path, that
every projectile refunds here, or that an action reaches both sites. Static
existence is not an invocation or double-refund proof. Action/continuation
ingress remains with the parallel projectile investigation.

## Resource and authority consequences

The installed Mana table is `0x1435a5b68`; its integer method
`0x141e6b6e0` calls `0x141e698e0` at `0x141e6b727`. The actual setter clamps
and writes current at `0x141e69939`, retaining its existing guards and
notifications. These integer paths leave fractional carry unchanged.

This is a numerical consequence of conditional skill/action state, distinct
from target damage, mitigation, infliction and cost payment. In particular,
refund cost is a fresh selected stat read, not proof of an earlier local Mana
deduction. Receiver identity alone cannot fill the accepted Hit→pool gap.
Source-instance DoT publication and generic existing Buff timing-stat refresh
also remain open.

## Evidence and verification

Ignored `r22-integer-ingress/audit.json` retains the bounded discovery bodies;
`r22-mana-refunds/audit.json` retains eight focused bodies, ten ranges and 1,105
decoded records. These are semantic checks of selected existing bodies,
not eight newly discovered functions or whole-function numerical execution.

`r22-mana-refunds.py` compares original-instruction interpretation of both
resource slices, the actual cost helper, `+0x358`, Mana wrapper and setter with
a separately written scalar specification: **1,240 controls / 177,192 interpreted
instructions / 306 visited instructions**. The eight focused bodies also include
constructor/factory context checked statically, outside those execution counts.
The controls cover eligibility, suppression, zero costs, maximum clamping,
negative/wrapping products and retained fractional carry.

Providers, component getters, reservations and notification consequences are
explicit fixtures. Preceding geometry/return decisions and actual incoming
reason values are supplied boundaries. This is same-agent verification,
not native execution, independently captured traffic or a separate worker.
`r22-refund-fresh.py` freshly reloads all eight original records and verifies
28 semantic anchors, ten table slots, two table LEAs, both divisor literals
and the ActionTypes identity. Provenance is retained in
[the evidence manifest](client-damage-evidence.json).

The parallel projectile R37 snapshot is pinned at
`8344af2114b7605e4d0460805f436f94c0369546`: 63 snapshot files, 44 exact Git
sources and parent-manifest hashes were verified. Its Chain candidate admission
and TLS discovery boundaries are reused without rerunning that numerical audit.
Candidate admission is not authoritative damage admission.
