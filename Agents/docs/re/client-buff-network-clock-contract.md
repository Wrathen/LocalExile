# Existing Buff clocks: network receiver and concrete callback contract (3.29.3b)

An existing network Buff has a positive clock-update contract: decoded message
`0x1c1` reaches the target Entity's **Buffs** component, matches an existing
instance, and can copy the transported remaining duration and speed into that
same instance. For the conditionally bound `BurningArrowDebuff` backend, the
two backend callbacks reached by this partial update are exact no-ops. This
closes a selected receiver and its backend consequences; it does not identify
the server-side mechanic that chooses those clock values.

This supplement follows [clock update boundaries](client-buff-clock-updates.md),
[instance timing](client-dot-routing.md#instance-clock-updates-are-separate-from-damage-magnitudes)
and the named Burning Arrow backend binding in [DoT routing](client-dot-routing.md).
The inspected executable is the original 3.29.3b `Game.exe`, SHA256
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`,
preferred image base `0x140000000`. Conclusions below are original-byte static
observations, not observations of client execution. No client launch, runtime
session, capture, archive modification or server implementation was needed.

## Message to the retained instance

The World dispatcher `0x142187480` retains the decoded message in RSI, reads
its ID from `message+8`, and resolves its target Entity using `message+0x10`
through `0x141ebefd0` at `0x142187f24..0x142187f33`. On the selected switch:

| Input | Verified dispatch |
|---|---|
| `0x1c1` add/update | Byte index at `0x142188ac8 + (0x1c1 - 0x1a9)` selects relative target `0x142188132`. |
| `0x1c2` remove | The same two-stage switch also selects `0x142188132`; the component handler subsequently distinguishes removal. |

At `0x142188132..0x142188140`, the resolved Entity is passed to
`0x1401ebc40`, then its result and the original decoded message are passed to
`0x141e11720`. The getter is positively named: its RIP-relative literal at
`0x1401ebc5e` resolves to `Buffs\0` at `0x14359cdc8`; after looking up the
component index it loads the component from `Entity+0x10`'s pointer array at
`0x1401ebc8d..0x1401ebc91`.

The component handler selects `0x1c1` at `0x141e11749..0x141e1174f`.
It searches the active instance vector `component+0x160..+0x168`, excluding
instances with `+0xd0` bit `0x10`, and matches the decoded message's 16-bit
`+0x20` key against instance `+0x9c` at `0x141e1178f`. A match is retained
through `0x141e092a0`; the earlier report establishes that this holder's
observer is reference housekeeping, not a Stats subscription.

For that existing instance, decoded selector `message+0x81 == 0` branches
from `0x141e1191d..0x141e11924` to the partial update:

| Decoded field | Direct retained-instance write | Address |
|---|---|---|
| `message+0x68` | Remaining duration `Buff+0x1c` | `0x141e11b89` |
| `message+0x6c` | Duration speed `Buff+0x20` | `0x141e11b8f` |

These are bit-pattern copies, not calls to the owner-Stats insertion calculator.
The partial branch does not assign a replacement definition or backend pointer.
It reads the retained `Buff+0x148` backend and calls its `+0x10` slot at
`0x141e11c6c`; the common continuation later reads that same backend field
and calls `+0x20` at `0x141e11c90`. Other fields, counted-vector values and
component notifications are also updated. This is not a claim that arbitrary
subscribers have no additional effects.

The decoder `0x141fdf870` is already bound to the wire format: flag `0x02`
supplies the float32 remaining-duration bits, flag `0x80` supplies float32
speed bits, and flag `0x40` becomes the replacement selector. Importantly,
omitting speed decodes **`1.0f`**, not “keep the current speed”, at
`0x141fdfa64`. That decoded value is still copied by the partial-update branch.
An update intended to retain a non-default speed must therefore supply it on
this contract. The replacement-selector path has different construction and
backend callbacks; it is documented separately in the linked timing report.

## Concrete timed-DoT backend: Burning Arrow

The existing named binding is conditional on a Buff whose definition selects
`Id2 = BurningArrowDebuff`. BuffDefinitions zero-based row 1443 is
`burning_arrow_additional_burn`, with `GrantedStats = [2022]`, corresponding
to native key 2023, `base_fire_damage_taken_per_minute`. The table SHA256 is
`3a23c6fdf7ba29fb8508e5c9560a44e5292345e45dd2f886ef7126b8427fd659`.
This table identity and named factory were independently reverified; it is
not a claim that a particular recorded message instantiated this effect.

The named factory object's table `0x143464a80`, slot zero, reaches
`0x1415d04f0`. This factory receives the actual Buff in R8, retains it at
backend `+8` (`0x1415d0516`), and installs table `0x1434871f0`
(`0x1415d051a`). Both update slots `+0x10` and `+0x20` resolve to the exact
three-byte leaf `0x1400f0f00`, bytes `c2 00 00` (`ret 0`, with no stack
argument bytes popped; it does not mean “return numeric zero”).

Consequently, if a retained Burning Arrow instance receives the partial
update above, those **selected backend methods** add no speed, damage or
Stats recalculation after the direct copies. The tick callback `+0x28` also
resolves to that exact no-op. This concretizes the previously open backend
side effects for this named case. Component subscribers retain their own
scope; neither this binding nor the stat's name proves publication of a
damage amount, active-source selection or a damage deduction.

## Actual initialization does not add the missing Stats subscription

The selected Entity constructor `0x141ebdf30` calls component creation
`0x142427510` at `0x141ebe080`, then iterates the resulting component pointer
array and invokes each primary table's **`+0x10`** at `0x141ebe0aa`.
Buffs factory `0x141e0fc40` installs table `0x14359f5f8`; this table's
`+0x10` is the same exact no-op `0x1400f0f00`. Thus this actual post-creation
initialization call does not register a Buffs listener on owner Stats.
The factory itself initializes ownership, its embedded publisher interface,
configuration and empty instance vector; it does not resolve Stats or
register a Stats callback in the inspected body.

This is a bounded result for those concrete bodies. It neither excludes an
external subscription nor requires exhausting unrelated client components.
The unresolved claim remains precisely **owner timing-stat update → existing
Buff clock recalculation/write**. The network partial-update writer is now
positively bound independently of that claim; it must not be conflated with
the owner-dependent new-instance insertion path.

## Computation and client/server boundary

The duration consumer `0x141e10520` uses the same stored fields. When its
documented eligibility guards allow advancement, it computes
`remaining -= deltaTime * (definition.+73 ? 1.0f : Buff.+20)` at
`0x141e105cd..0x141e105ea`. Its new remaining duration is not clamped here.
The component's bound `+0x40` tick is `0x141e10620`; it calls this consumer
for the retained instances.

For a server implementation, this gives a concrete synchronization option:
the server may send a new remaining duration and speed for an existing
instance, and this client path stores those timer inputs. It does not require
the client to infer that update from a separately delivered owner stat.
Which game mechanic computes them, when the server sends them, and whether
other client routes also refresh them remain separate questions.

**Duration speed is not yet a proven DoT-DPS multiplier.** This contract does
not establish Temporal Chains semantics, damage-faster/slower snapshot
policy, resistance reevaluation, strongest-instance selection, source-rate
aggregation, shared DoT capping, or Life/ES deduction. A synchronized Buff
timer can be understood and implemented without inventing those links.

## Verification and evidence

Ignored original-byte evidence is in
`Tools/re/out/client-damage-audit/r4-buff-network-clock-contract/`;
its independent sibling verifier is
`r4-buff-network-clock-contract-verify.py`. The recheck passed 53 instruction
anchors, six table slots, two message switch entries and the named component
literal, plus all ten recorded ranges across ten function records (2,504
non-skipdata decode records and 185 skipdata records) against the pinned
executable. It also checks the exact no-op leaf.
The World dispatcher's linear decode includes embedded switch-table data;
these bytes are retained for reproducibility, not treated as executed
instructions. Only decoded instruction-boundary anchors support code claims.

The earlier named registry/factory association and BuffDefinitions identity
were rechecked by `r2-burning-arrow-verify.py`, retaining its original
conditional binding. No unsupported absence claim or runtime attribution is
needed for these static contracts.
