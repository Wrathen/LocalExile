# Final spell hit ranges and conversion equations (3.29.3b)

## Result and scope

All twelve named spell minimum/maximum typed and total damage callbacks now
have recovered equations. They consume the previously recovered
[base and selected-added components](client-spell-damage-components.md),
apply their own conversion histories and modifiers, and publish integer ranges.
This closes the selected spell range calculation, not a per-hit damage roll,
defender mitigation, target resource loss or ordinary network-entity evaluation.

The accompanying [ordered equation DAG](client-spell-hit-equations.json) retains
the scalar operation parentheses and branch variants. It is analyst-derived
equation metadata, not original source or a disassembly export. Its stat leaves
are supplied callback inputs; evaluating them does not recursively recompute
upstream components. Network explicit overrides remain a separate provider path.

All evidence is static/offline, with synthetic fully-valid native stat caches.
No client execution, attachment, new capture, server implementation or CI was
performed. Addresses use preferred image base `0x140000000`.

| Artifact | SHA256 |
|---|---|
| `Client/Game.exe` | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `Stats.json` | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |

## Endpoints and qualification

Native key `k` means zero-based Stats row `k-1`.

| Type | Minimum key / callback | Maximum key / callback | Zero-output gate |
|---|---|---|---|
| Physical | 261 / `0x142544d00` | 262 / `0x142545b70` | 1723 |
| Fire | 255 / `0x1425469e0` | 256 / `0x1425484f0` | 1724 |
| Cold | 253 / `0x14254a000` | 254 / `0x14254b830` | 1725 |
| Lightning | 257 / `0x14254d060` | 259 / `0x14254e770` | 1726 |
| Chaos | 258 / `0x14254fe80` | 260 / `0x142551dc0` | 1727 |
| Total | 674 / `0x1425428a0` | 675 / `0x142542d10` | None |

Any nonzero value of a typed gate returns zero before reading its damage
components. Negative nonzero values also qualify. These are destination-output
gates: a physical source remains an input to an elemental output even when
the separate physical output would be prohibited.
Lightning and chaos maximum keys are not simply minimum key plus one.

Total 674 adds supplied typed keys 261, 255, 253, 257 and 258 as signed32
integers. Total 675 similarly adds 262, 256, 254, 259 and 260.
They have no floating conversion, saturation or final nonnegative clamp.
For example, supplying `INT_MAX` and one to two components returns `INT_MIN`.
This is callback arithmetic, not evidence that such a total is an ordinary
reachable gameplay result.

## Base, added and conversion histories

Use `S[k]` for a supplied signed32 input, `W` for signed32 wrapping and `f`
for binary32 rounding. Let `P(x) = max(0, f(f(W(x))/100))` and
`F(k) = P(W(100 + S[k]))` for ordinary finite scalar arithmetic.

| Type | Base min / max | Selected-added min / max | Destination lost-to-conversion |
|---|---|---|---|
| Physical | 1533 / 1534 | 1545 / 1546 | 1502 |
| Fire | 1535 / 1536 | 1549 / 1550 | 1503 |
| Cold | 1537 / 1538 | 1553 / 1554 | 1504 |
| Lightning | 1539 / 1540 | 1557 / 1558 | 1505 |
| Chaos | 1541 / 1542 | 1561 / 1562 | 1506 |

Each original source contributes `f(f(f(added) * F(5761)) + f(base))`.
The added-effectiveness factor does **not** multiply the intrinsic base here.
For example, base 100, added 100 and key5761=40 produce raw source 240,
before conversion, increases and final modifiers.
This is distinct from an attack source's weapon/base multiplier.

The directed conversion order is physical → lightning → cold → fire → chaos.
Every forward pair exists:

| Edge | Input key | Edge | Input key |
|---|---|---|---|
| Physical → lightning | 1515 | Physical → cold | 1514 |
| Physical → fire | 1513 | Physical → chaos | 1516 |
| Lightning → cold | 1518 | Lightning → fire | 1517 |
| Lightning → chaos | 1519 | Cold → fire | 1520 |
| Cold → chaos | 1521 | Fire → chaos | 1522 |

An edge uses `P(S[edge])`, without adding 100. Each destination sums all
monotone paths that end there, including its own unconverted source:
physical has one path, lightning two, cold four, fire eight and chaos sixteen.
Do not substitute an intermediate final typed output for a source component;
doing so would introduce its publication rounding, output gate and modifiers.

The factor census below describes which modifiers apply. It is algebraic
bookkeeping; distributing or rearranging floating products is not an exact
replacement for the ordered DAG.

| Visited category | Increased inputs | Separate final inputs |
|---|---|---|
| Every path | 1426, 5762, 1428 | 1427, 5103, 1429 |
| Physical | 1434 | 1435, 8403 |
| Any elemental type, once per path | 1450, 1461 | 1451, 1462 |
| Lightning | 1446, 1459 | 1447, 1460 |
| Cold | 1444, 1458 | 1445 |
| Fire | 1442, 1456 | 1443, 1457 |
| Chaos | 1448 | 1449 |

For a path, increased inputs add together with 100 in signed32 arithmetic
before one `P` operation. Each final input has its own `F` factor.
Thus a physical → lightning → cold path has each visited type's increases,
one common elemental increase, and each listed final factor exactly once.
There is no additional cold-spell final input in this callback family.

Paths also have a destination remainder `P(W(100-S[lost]))`, with one
specific original-code exception described next. Intermediate destination
remainders are not multiplied into every conversion edge.

## Special branches and original asymmetries

### The longest chaos path omits the destination remainder

Both chaos endpoints omit key1506's remainder for the full
physical → lightning → cold → fire → chaos path. The other fifteen paths
retain it. This is not inferred from a stat name: the ordered scalar
dataflow and isolated original-instruction controls agree.

With physical base 100 and keys1515/1518/1520/1522 all 100, key1506=100
still yields chaos 100. With only the direct physical → chaos key1516=100,
the same key1506=100 instead yields zero.
Both minimum and maximum reproduce this distinction.
The tail summation for minimum is at `0x142551a29..0x142551d36`;
its last path multiplies the retained edge product at `0x142551d1c`.

This report preserves the inspected client calculation. It does not label
the difference an official-server bug, infer its intended balance rule or
silently repair it using a generic conversion algorithm.

### Lightning Purist changes the two endpoints differently

Only the lightning endpoints query Boolean key23310,
`keystone_lightning_purist`. Nonzero selects a factor of **0.5 for minimum**
and **1.25 for maximum**; zero selects 1.0. The factor applies to both
own-lightning and physical-converted-lightning contributions.

Minimum selects integer 50 versus 100 at `0x14254e524..0x14254e532`;
maximum selects 125 versus 100 at `0x14254fc34..0x14254fc42`.
Both divide the selected value by 100 and clamp its factor before use.
Supplying lightning base 100 and key23310=1 therefore yields 50/125.
The same 50/125 appears for physical base 100 converted fully to lightning.
This does not establish the modifier's producer or a per-event random roll.

### The selected Prismatic Burst branch excludes converted terms

Nonzero key18664, `prismatic_burst_unchosen_type_damage_-100%_final`, selects
an own-source-only branch in fire, cold and lightning outputs.
It removes converted contributions but retains that destination's intrinsic
base and selected-added source, with their ordinary modifiers and remainder.
Both positive and negative nonzero values select it.

For fire base 100 it retains 100; for physical base 100 fully converted to
fire it returns zero. Cold and lightning have the same branch distinction.
Physical and chaos endpoints do not query this flag. In particular it is
not applied globally to every intermediate elemental history in chaos.
Its upstream grant/selection remains a separate contract.

## Ordered equation artifact and verification

The JSON DAG contains twelve outputs, twenty-two branch equations and 788
shared scalar nodes. A node's index is its reference; `const` contains a
literal, `stat` contains a native key, and other operands reference earlier
nodes. Output records declare early zero gates, final nonnegative clamping,
and branch selection by nonzero keys18664/23310 where applicable.
Every branch uses supplied raw/component stats rather than output recursion.

`iadd/isub/imul` wrap signed32; `float/fadd/fsub/fmul/fdiv` round binary32
after each operation. `R` adds signed half and performs nonsaturating
`CVTTSS2SI`; invalid or out-of-range conversion gives `INT_MIN`.
Typed outputs then clamp negative integer results to zero.
Physical uses an inline half/conversion sequence; the other typed outputs
call `0x142513810`. A universal `INT_MAX` saturation would change these results.
The supplied finite-intermediate controls do not claim full exceptional
IEEE NaN behavior or a complete native cache-miss/provider simulation.

Private `r14-spell-equations.py` verified 3,038 equation/original-instruction
cases and 1,315,835 interpreter steps, including all isolated conversion
paths, gate/branch products, mixed modifiers and single-input integer bounds.
An independently enumerated path/category factor census matches all ten
typed min/max equations. Actual read unions match all twelve declared
dependency sets; their sizes are 5 for each total, 14 physical, 42 fire,
33 cold, 27 lightning and 49 chaos.

Independent review uses the earlier R10 integer/SSE engine, not the author's
R9/R13 interpreter: twelve callbacks and registrars, 24 function records,
44 original ranges, 15,965 decoded records and 181 directed controls.
Both approaches interpret original instructions with synthetic inputs;
neither executes the target. The separate DAG review checks the published
equation metadata against that independent engine: 2,767 additional controls,
1,499,875 interpreter steps, all 31 conversion histories and all twelve
dependency sets agree. The DAG evaluator was separately written for that review.

Actual hit sampling, critical confirmation, target defence and pool loss
remain open in the [research workbench](client-damage-calculation.md#8-research-workbench-and-completion-criteria).
