# Final attack damage ranges and ordered equations (3.29.3b)

## Result and boundary

All twenty main-hand/off-hand physical, fire, cold, lightning and chaos minimum/maximum
callbacks, plus four hand totals, now have recovered ordered equations. The
[equation DAG](client-attack-hit-equations.json) preserves signed integer operations,
binary32 parentheses and the Lightning Purist branches. It is analyst-derived equation
metadata, not original source or a disassembly export.

This refines the [offence summary](client-offence-estimates.md), including a correction:
Lightning Purist multiplies the attack **minimum by 0.5 and maximum by 1.25**, for both
hands. The earlier statement that both endpoints use 0.5 was too broad.
The longest physical-to-chaos attack path also preserves a remainder that the
[corresponding spell path](client-spell-hit-formulas.md#the-longest-chaos-path-omits-the-destination-remainder)
omits. A shared generic conversion formula must not silently erase that distinction.

These are native range calculations with supplied stat inputs. They do not select a hand,
roll a hit, confirm a critical strike, inspect a concrete defender or deduct resources.
Network explicit overrides and native derived evaluation remain distinct
[provider contracts](client-stat-evaluation.md). Original accepted-Hit-to-pool and
DoT-source-to-incoming-rate gaps remain open; R2/R3/R4 remain Partial.

All evidence is static/offline. No client execution, attachment, new capture, server
implementation or CI was performed. Preferred image base is `0x140000000`.

| Artifact | SHA256 |
|---|---|
| `Client/Game.exe`, 3.29.3b | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `data/tables/Stats.json` | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |

## Outputs and supplied components

Native key `k` is Stats row `k-1`. The exact twenty callback/registration pairs remain
in the [offence endpoint table](client-offence-estimates.md#3-both-hands-all-five-types-minimum-and-maximum).
For each pair below, the first key is minimum and the second maximum.

| Type | MH outputs | OH outputs | MH base / selected-added min | OH base / selected-added min | Lost-to-conversion key |
|---|---|---|---|---|---|
| Physical | 279 / 280 | 281 / 282 | 1563 / 1575 | 1593 / 1605 | 1502 |
| Lightning | 271 / 272 | 273 / 274 | 1569 / 1587 | 1599 / 1617 | 1505 |
| Cold | 263 / 264 | 265 / 266 | 1567 / 1583 | 1597 / 1613 | 1504 |
| Fire | 267 / 268 | 269 / 270 | 1565 / 1579 | 1595 / 1609 | 1503 |
| Chaos | 275 / 276 | 277 / 278 | 1571 / 1591 | 1601 / 1621 | 1506 |

Maximum base/added inputs are minimum plus one. MH zero-output gates are physical 1733,
fire 1734, cold 1735, lightning 1736 and chaos 1737; OH substitutes 1738..1742.
Any nonzero signed32 gate returns zero before damage component reads. These are
destination-output gates: prohibiting physical output does not remove a physical source
from a separate elemental/chaos calculation. No Prismatic Burst key 18664 branch occurs
in these attack callbacks.

Totals 670/671 sum the five supplied MH minimum/maximum integers. Totals 672/673 sum
the OH integers. They use wrapping signed32 addition, without float conversion,
saturation or final nonnegative clamp. For example, `INT_MAX + 1` returns `INT_MIN`.
They do not recursively evaluate the typed callbacks.

## Base multiplier, added effectiveness and conversion

Let `W` wrap signed32, `f` round binary32, `S[k]` read a supplied signed32 stat and
`P(n)=max(0,f(f(W(n))/100))`. The source amount is:

```text
B = P(W(100 + trunc0(S[6172] / 100)))
A = P(W(100 + S[5761]))
raw[type] = f(f(f(added[type]) * A) + f(f(base[type]) * B))
```

The division of 6172 is signed integer division **before** float conversion. In
physical minimum its compiler-generated signed high-product/shift sequence is at
`0x142573704..0x14257372f`. The DAG retains that integer circuit explicitly rather than
replacing it with floating division by 10,000.
With base 10,000, added 100, key 6172=4080 and key 5761=40, either hand/endpoint returns
14,140 before other modifiers: base uses 1.40 and added uses 1.40. A direct 1.408 base
factor would incorrectly return 14,220. This demonstrates two distinct component
contracts, even where their displayed percentages appear close.

The conversion graph is physical → lightning → cold → fire → chaos, with every
forward edge present. Edge keys and path modifier groups are in the
[offence conversion census](client-offence-estimates.md#3-both-hands-all-five-types-minimum-and-maximum).
Each destination sums all monotone histories ending there, including its own source:
1 physical, 2 lightning, 4 cold, 8 fire and 16 chaos paths per hand/endpoint.
Every path carries both its source base and selected-added contributions.

The separate semantic census independently enumerates these histories and their
category-factor multisets. All twenty equations match: one combined increased factor,
the applicable separate final factors, edge coefficients and the destination remainder
`P(W(100-S[lost]))`. Intermediate destination remainders are not inserted along the
path. Replacing a source with an intermediate published typed output would wrongly
introduce its output gate, rounding and modifiers.

In particular, **all sixteen chaos paths retain key 1506's remainder**, including the
longest physical → lightning → cold → fire → chaos path. Physical base 100, four
chain-edge keys 1515/1518/1520/1522=100 and key 1506=100 yield chaos 0 for MH/OH and
min/max. The spell counterpart yields 100 under its corresponding supplied components.
Preserve this original artifact difference without asserting its intended balance rule
or its official-server numerical equivalence.

## Lightning Purist and arithmetic order

Only the four lightning endpoints query Boolean key 23310. Nonzero selects integer 50
for minimum, integer 125 for maximum, otherwise 100. The selected percentage applies
to own-lightning and physical-converted-lightning paths alike. MH selection sites are
`0x14257e15c..0x14257e16d` and `0x14257f91c..0x14257f92d`; OH uses the same recovered
contracts. Base 100 consequently gives 50/125 for either hand, including a fully
converted physical source. Other typed outputs do not read the flag; do not propagate
that final-lightning range adjustment into fire/cold/chaos conversion histories.

Typed callbacks round once after summation, using signed half-away-from-zero and
nonsaturating `CVTTSS2SI`, then retain only a positive signed32 result. Physical inlines
this sequence, for example `0x142574936..0x142574969`; other types call `0x142513810`.
Out-of-range conversion returns `INT_MIN`, subsequently clamped to zero. This differs
from a universal `INT_MAX` cap.

The algebraic factor census is not a license to reassociate float operations. One
finite supplied-input witness for MH physical minimum uses:

```text
base1563=56745; added1575=6881; 6172=19216; 5761=65; 1426=206; 1502=40;
1435=69; 1437=48; 1440=2; 1427=62; 5103=65; 1431=86; 15983=49; 19089=75.
```

The original instruction order and DAG return 10,751,058. Keeping the same raw amount,
remainder and increased factor, then multiplying those eight final factors successively
in the listed order returns 10,751,056. This is a synthetic arithmetic witness, not a
gameplay observation or an actual reachable build claim.

## Equation artifact and verification

The JSON contains 24 outputs, 28 branch equations and 1,618 shared nodes. Operands are
earlier node indices; `const` and `stat` instead hold a literal and native key.
`iadd/isub/imul` wrap signed32; `hi32mul` is the signed high32 of a signed64 product;
`isar/ishr` preserve arithmetic/logical low32 shifts. Float operations round binary32
at each node. `P`, `R`, early gates and output clamps have the contracts above.
The four extra equations are the nonzero Lightning Purist alternatives.

All 24 actual-read unions equal their declared dependency sets: 5 per total, 21 physical,
36 lightning, 44 cold, 54 fire and 63 chaos. This local equality does not imply that
every native callback declares every read or that arbitrary transactions are fresh;
the [dependency-order audit](client-stat-dependency-order.md) documents those limits.

Private evidence under ignored `Tools/re/out/client-damage-audit/r20-attack-final/`:

| Check | Coverage |
|---|---|
| Fresh callback and registrar audit | 48 existing bodies, 92 ranges, 35,369 decoded instructions |
| Original-instruction engine against generated equations | 8,072 cases, 4,797,766 interpreted instructions |
| Separately written DAG evaluator against earlier R10 integer/SSE engine | 5,612 cases, 3,512,171 interpreted instructions |
| Directed supplementary witnesses | 9 cases, 5,981 interpreted instructions |
| Semantic conversion/category census | All 20 typed endpoints; all 31 histories per hand/endpoint |

Both engines cover gates, nonzero/low32 selectors, every isolated source/history,
base-vs-added scaling, negative and large single inputs, mixed modifiers and totals.
The second implementation does not use the first engine or its evaluator; no separate
worker review is claimed. The supplementary controls preserve the rounding witness.
The fixtures provide fully-valid native stat caches and documented scalar/getter helpers;
they do not model cache misses, ordinary network recomputation, actual equipment values
or complete exceptional IEEE NaN/Inf behavior. These are interpreted original instructions,
not native execution. Complete disassembly and extracted game data remain uncommitted.

The parallel investigation's fixed
[hand-selection report](https://github.com/LocalExile/LocalExile/blob/f092bc51c5840f4c9528660f9ad5bba3e1c15bcc/Agents/docs/re/projectiles/hand-selection.md)
owns startup weapon eligibility, stage/sequence/source-ordinal selection and the 518
action cache. Its 55-file snapshot, 42 exact Git sources and parent-manifest hashes were
checked before reuse; its numerical audit was not rerun here. Formula construction
and selecting which hand supplies an action are different contracts. Coordination
continues through owner work files and fixed snapshots, without routine messages.
