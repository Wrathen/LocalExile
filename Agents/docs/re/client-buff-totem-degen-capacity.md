# A Buff granted value consumed by the Life-loss path (3.29.3b)

## Result and scope

There is a concrete numerical consumer beyond Buff storage and presentation:
the `AncestralTrialTotemAbsorbDegen` backend's capacity, initialized from an
actual Buff's first granted value, is consumed by the **reason-4 Life-loss
reducer**. Sufficient capacity takes a zero-return path instead of reaching
the ordinary Life reducer. Exhaustion clears both that capacity and the actual
Buff's remaining duration.

This is a specific Buff-to-resource operation, **not** a recovered general
Buff-to-Stats publisher. The definition's sole granted key is `buff_impl_stat`,
not an incoming damage-rate stat. The incoming numerical loss already exists
before this consumer runs. Ordinary flask/leech recovery and source-instance
publication for ignite, poison and Righteous Fire remain separate questions.

All evidence is static/offline; no client execution, attachment, new capture,
server operation or CI was performed. Addresses use preferred image base
`0x140000000` and do not identify live ASLR locations.

| Artifact | SHA256 |
|---|---|
| `Client/Game.exe` | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `Stats.json` | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| `BuffDefinitions.json` | `3a23c6fdf7ba29fb8508e5c9560a44e5292345e45dd2f886ef7126b8427fd659` |

## 1. Definition, actual instance and capacity

BuffDefinitions JSON row **2289**, native definition key **2290 (`0x8f2`)**, has:

| Field | Pinned value |
|---|---|
| `Id` | `ancestral_trial_totem_absorb_degen` |
| `Id2` | `AncestralTrialTotemAbsorbDegen` |
| `IsRecovery` | `true` |
| `GrantedStats` | `[5022]`: JSON Stats row 5022, native key 5023, `buff_impl_stat` |

The [initial granted-value audit](client-buff-initial-granted-values.md) already
establishes the definition-controlled network decoding and actual Buff vector
at `+0x130`. That decoder and its zero-padding are reused here.
`IsRecovery=true` is the established eligibility flag for decoding; its name
does not make this capacity a healing rate.

The [existing registry/value audit](client-dot-routing.md#confirmed-granted-value-consumers)
binds registration `0x141e08cd7..0x141e08d2c` to factory table
`0x14359be80`, slot zero `0x141e09d90`. This pass checks those original
instructions, table bytes and the exact UTF-16 name again. The factory:

1. Retains the supplied actual Buff at backend `+8` (`0x141e09daf`).
2. Reads Buff `+0x130[0]` as a signed 32-bit integer
   (`0x141e09dbd..0x141e09dc4`).
3. Converts it to binary32 and stores backend `+0x10`
   (`0x141e09dc8..0x141e09dcb`).

Insertion `0x141e11dc0` passes the actual Buff to backend selection and moves
the selected backend into actual Buff `+0x148` at `0x141e11e7b`.
The new result is the downstream capacity consumer below; empty lifecycle
slots alone were never evidence that this backend had no numerical consumer.

## 2. The consumer has a concrete Life-pool receiver

The [resource boundary audit](client-hit-resource-boundary.md#2-proven-table-and-object-binding)
binds Life `+0x178` to its Life pool, its owner at pool `+8`, and installed
pool table `0x1435a5da0`, whose `+0x48` is `0x141e6a8a0`. It also binds
Life `+0x170` to the retained actual Buffs component. This pass retains the
constructor/pool-constructor bodies and verifies these accesses again.

For nonzero delta and positive current Life, reducer `0x141e6a8a0` preserves
the incoming delta in `XMM8`. Its **reason byte equal to 4** branch
(`0x141e6a94d..0x141e6a951`) then:

- Traverses retained Buffs `+0x160..+0x168`.
- Skips actual Buffs with removal bit `+0xd0 & 0x10`.
- Compares each definition key with `0x8f2` and selects the first match
  (`0x141e6a95f..0x141e6a9ca`).
- Retains the selected actual Buff via `0x141e092a0`; a missing/removed
  instance does not supply capacity.
- Loads its backend from actual Buff `+0x148` and capacity from backend
  `+0x10` (`0x141e6a9f2..0x141e6a9fe`).

This establishes actual Buff identity and resource-owner provenance, rather
than inferring them from a matching field offset in an unknown object.
The branch does not enumerate several such Buffs or aggregate their capacities.

## 3. Capacity, exhaustion and the exact overflow boundary

For an ordinary finite negative input delta `-D` with `D > 0` and nonnegative
capacity `C`, the observed binary32 operations are:

| Condition | Stored capacity / duration | Local outcome |
|---|---|---|
| `D < C` | Capacity becomes `f32(C-D)`; duration is not cleared here | Zero-return path |
| `D >= C` and `f32(-(f32(D-C))) > -0.00009999999747378752` | Capacity and actual Buff remaining duration become zero | Zero-return path |
| Remaining exhausted case | Capacity and actual Buff remaining duration become zero | Falls through with original input delta still in `XMM8` |

The exact code uses `-1.0f` at `0x1436a49f4` to obtain `D`, subtracts
capacity in `XMM0`, flips its sign with `0x80000000`, then uses the
**strict `ja`** comparison at `0x141e6aa36`. The threshold's original bytes
are `17b7d1b8` at `0x1436a48f8`; this is not a duration tolerance.

At exhaustion, `0x141e6aa22` zeros backend `+0x10`, and `0x141e6aa25`
zeros its retained actual Buff's remaining duration `+0x1c`. This is an
actual duration write, not removal-bit assignment or proof of immediate
instance destruction.

The zero-return paths perform local reference/link cleanup and return zero
at `0x141e6ab00..0x141e6ab18`, bypassing the later ordinary Life-loss call.
The other path reaches `0x141e6ab28`, where other existing loss policies
still apply. When it reaches the ordinary reducer at `0x141e6ac55`,
`0x141e6ac4e` supplies the **original preserved `XMM8`**, not the residual
computed in `XMM0`. There is no intervening write of the residual into
`XMM8`. Thus this binary slice must not be rewritten in the report as an
idealized remainder-only overflow algorithm.

For example, capacity 100 and loss 99 take the zero-return path and leave
capacity 1; loss 100 clears capacity/duration and also takes the zero-return
path. Loss 101 clears both but takes the original-delta fall-through path.
The retained branch simulation also distinguishes loss `100.00005` from
`100.001` at capacity 100, using binary32 intermediates.

These are static branch facts for this pinned client. They neither establish
official-server behavior nor justify correcting the apparent overflow
behavior as part of this research.

## 4. A concrete continuous-rate caller

Life step `0x141e6ae70` supplies two positive rate-derived callers of this
reason-4 branch:

| Native Stats key | Meaning | Callsite |
|---|---|---|
| 7599 (`0x1daf`) | `virtual_life_degeneration_per_minute` | `0x141e6afa1`, reason 4, nonlethal false |
| 21052 (`0x523c`) | `virtual_non_lethal_life_degeneration_per_minute` | `0x141e6b010`, reason 4, nonlethal true |

Each reads the rate, converts it to binary32, divides by **60.0f**, multiplies
by the step's supplied interval, requires a positive result, negates it and
calls the concrete Life reducer. The second explicitly supplies nonlethal
true. This ties the capacity consumer to an already existing continuous
Life-loss operation. It does not identify which source instance supplied
the rate, prove a 33 ms interval, establish shared capping, or identify this
capacity as another damage rate.

## 5. Verification and remaining edge

Ignored private evidence is under `Tools/re/out/client-damage-audit/`:

- `r16-buff-granted-verify.py` checks the pinned inputs, definition/stat joins,
  72 instruction anchors, the two exact table slots and original constants.
- `r16-buff-granted/verification.json` retains seven decoded function bodies,
  11 PE-unwind ranges with hashes, 745 decoded records and six narrow
  instruction-driven capacity-branch simulations.
- The simulations stop before intrusive-link cleanup; the complete original
  reducer body is retained separately. This is a bounded static harness,
  not execution of the client or a claim to model every loss policy.

The positive closure is **network-eligible actual Buff value → backend
capacity → selected reason-4 Life-loss operation and Buff duration write**.
No generic Buff stat application, ordinary flask/leech recovery contribution,
ignite/poison/RF rate publication, or official-server execution boundary is
closed by this example.
