# Client complete named outgoing poison-rate family (3.29.3b)

## 1. Result, correction, and boundary

All **72 named outgoing poison-rate outputs** in the pinned `Stats` table are now
individually audited: 40 typed contributions, 16 noncritical/critical totals, and 16
`to_always_deal_per_minute` outputs. Eight multiplier builders, the secondary critical
multiplier alias, and combined poison increased/final modifiers are included too: **83
registrations and callbacks**. Every typed callback's original-instruction expression was
checked independently, including both hands, spell, secondary, minimum and maximum; the
other members were not filled in by assuming symmetry.

A material correction to the earlier [representative outgoing study](client-ailment-outgoing-formulas.md):
**poison totals sum their five inputs with 32-bit integer additions, convert that sum once
to float32, then apply a multiplier and non-saturating signed rounding.** They do not use
the float-by-float sum and upper saturation found in the selected ignite totals. This is
true of all 16 poison totals, not just the representative MH minimum. None of the 72 named
poison rate callbacks has an upper-saturating return. Typed outputs separately clamp the
returned integer to nonnegative; totals and `always` outputs do not.

These are native outgoing **damage-per-minute formulas**, not target-side poison
application or resource deduction. Their minute representation does not establish a shared
DoT cap. Registration and ready-cache interpretation do not prove that an ordinary network
entity executes the formula locally. Reuse the [stat evaluation boundary](client-stat-evaluation.md),
[eligibility builders](client-ailment-eligibility.md), [chance inputs](client-ailment-chance-inputs.md),
[upstream multipliers](client-ailment-multiplier-inputs.md), and
[instance contracts](client-dot-instance-rules.md) without merging their separate claims.

| Artifact | Identity |
|---|---|
| Executable | `Client/Game.exe`, 3.29.3b, PE x86-64; preferred base `0x140000000` |
| Executable SHA256 | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `data/tables/Stats.json` SHA256 | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| Stat mapping | Native key `k` = JSON row `k - 1`, `Id`; 23,346 rows |
| Method | Original PE bytes and chained unwind ranges; Capstone 5.0.3 / pefile 2024.8.26; bounded Ghidra 12.1.4 `-noanalysis` typed export |
| Execution | Static/offline only; no game launch, attach, hooks, traffic acquisition, or server changes |

Addresses are preferred VAs; formula names below are analyst notation. Ignored evidence is
under `Tools/re/out/client-damage-audit/r9-poison-family/`: canonical `audit.json`,
`mapping.json`, `dependencies.json`, original `.asm`, complete `.typed.c` and typed JSONL,
symbolic expressions, per-output semantics and focused replay records. The canonical audit
contains **172 function records / 332 ranges / 81,897 instructions**: 83 registrations,
83 callbacks, and six explicit helpers. The max-pointer leaf has its reviewed 15-byte
boundary; signed rounding and upper-saturating rounding are separate reviewed leaves.
All 83 requested callback decompilations completed. Complete expressions and original
instructions, not a shortened C display, determine the arithmetic claims.

## 2. Complete typed-output map

A typed output's name identifies the **damage bucket feeding poison** after conversion;
it need not be that damage's original ancestry. For example, `from_main_hand_fire_damage`
retains physical-to-fire and other conversion histories. It does not simply multiply final
fire hit damage by a poison coefficient.

| Category | Damage bucket | Minimum key / callback | Maximum key / callback |
|---|---|---|---|
| MH | physical | 6073 / `0x1426a0ed0` | 6074 / `0x1426a2630` |
| MH | fire | 6075 / `0x1426a3d90` | 6076 / `0x1426a59b0` |
| MH | cold | 6077 / `0x1426a75e0` | 6078 / `0x1426a9000` |
| MH | lightning | 6079 / `0x1426aaa20` | 6080 / `0x1426ac310` |
| MH | chaos | 6081 / `0x1426adc10` | 6082 / `0x1426afd30` |
| OH | physical | 6083 / `0x1426b1e50` | 6084 / `0x1426b35a0` |
| OH | fire | 6085 / `0x1426b4cf0` | 6086 / `0x1426b6910` |
| OH | cold | 6087 / `0x1426b8540` | 6088 / `0x1426b9f40` |
| OH | lightning | 6089 / `0x1426bb940` | 6090 / `0x1426bd210` |
| OH | chaos | 6091 / `0x1426beae0` | 6092 / `0x1426c0bd0` |
| spell | physical | 6093 / `0x14267fbb0` | 6094 / `0x1426812b0` |
| spell | fire | 6095 / `0x1426829b0` | 6096 / `0x1426845e0` |
| spell | cold | 6097 / `0x142686210` | 6098 / `0x142687bb0` |
| spell | lightning | 6099 / `0x142689550` | 6100 / `0x14268ad30` |
| spell | chaos | 6101 / `0x14268c510` | 6102 / `0x14268e520` |
| secondary | physical | 6103 / `0x142690530` | 6104 / `0x142691c20` |
| secondary | fire | 6105 / `0x142693310` | 6106 / `0x142694f50` |
| secondary | cold | 6107 / `0x142696b90` | 6108 / `0x142698530` |
| secondary | lightning | 6109 / `0x142699ed0` | 6110 / `0x14269b6a0` |
| secondary | chaos | 6111 / `0x14269ce70` | 6112 / `0x14269eea0` |


### 2.1 Eligibility is category-specific

All typed outputs veto `6000` (`deal_no_poison_damage_over_time`) and the selected category's
**final bucket** prohibition. In physical/fire/cold/lightning/chaos order, those prohibition
keys are MH `1733..1737`, OH `1738..1742`, spell `1723..1727`, and secondary `1728..1732`.
Physical additionally vetoes nonzero `6115`; chaos vetoes nonzero `6119`.

Spell/secondary elemental contributions require the corresponding nonzero virtual permission:
fire `6116`, cold `6117`, lightning `6118`. Hand elemental contributions instead require:

```text
MH fire/cold/lightning: B(13125/13126/13127) || (W(S(3)) && B(19554))
OH fire/cold/lightning: B(13128/13129/13130) || (W(S(4)) && B(19554))
```

`B(k)` means a nonzero input. `19554` is `all_damage_with_tinctured_weapons_can_poison`.
`W` reads the byte table at `0x142fdd728`; its 18 inspected bytes are nonzero at every
index except **13 and 14**. This body does not independently test `tincture_applied`,
hand tincture state, `skill_is_attack`, or Varunastra. Do not add such a condition from the
stat name, or treat the table as a multiplier. Native table indexing has no bounds guard
here; invalid weapon indices are outside the defined-input reconstruction.

MH fire's table load is `0x1426a3e7d..0x1426a3e88`; the hand permission and table/tincture
alternatives join at `0x1426a3f5f..0x1426a405d`. It then tests the global prohibition,
MH fire prohibition, and resulting eligibility at `0x1426a413a`, `0x1426a4217`, and
`0x1426a421f`. This is a further consumer-side qualification beyond the permission
builders themselves. All 12 hand elemental min/max callbacks were checked against the
18 inspected weapon values and both tincture-permission states.

### 2.2 Base and added components

Let `S(k)` read the supplied evaluation context. Use the distinct arithmetic helpers:

```text
P(n) = max(0, float32(n) / 100)
F(n) = P(add32(n, 100))
R(x) = cvtt_i32(float32(x + (x < 0 ? -0.5f : +0.5f)))
A(c) = F(trunc0(S(6172) / 100)) for MH/OH; 1 for spell/secondary
B(c,t,b) = float32(S(base(c,t,b))) * A(c)
         + float32(S(added(c,t,b))) * F(S(5761))
```

`cvtt_i32` truncates a representable finite binary32 value toward zero; signed conversion
overflow yields `-2147483648`, rather than an upper or lower saturating return.
`add32` retains 32-bit wraparound.

`b` selects minimum or maximum. `6172` is `active_skill_base_attack_damage_final_+permyriad`;
both hands truncate its signed integer division by 100 before applying `F`. Spell/secondary
base components do not use that attack factor. `5761` is
`combined_all_added_damage_+%_final`: it applies to the added component, not the base component.

| Category | Physical base min/max | Fire base | Cold base | Lightning base | Chaos base |
|---|---|---|---|---|---|
| MH | 1563/1564 | 1565/1566 | 1567/1568 | 1569/1570 | 1571/1572 |
| OH | 1593/1594 | 1595/1596 | 1597/1598 | 1599/1600 | 1601/1602 |
| spell | 1533/1534 | 1535/1536 | 1537/1538 | 1539/1540 | 1541/1542 |
| secondary | 5329/5330 | 1525/1526 | 1527/1528 | 1529/1530 | 1531/1532 |

| Category | Physical added min/max | Fire added | Cold added | Lightning added | Chaos added |
|---|---|---|---|---|---|
| MH | 1575/1576 | 1579/1580 | 1583/1584 | 1587/1588 | 1591/1592 |
| OH | 1605/1606 | 1609/1610 | 1613/1614 | 1617/1618 | 1621/1622 |
| spell | 1545/1546 | 1549/1550 | 1553/1554 | 1557/1558 | 1561/1562 |
| secondary | 4219/4221 | 4930/4932 | 4829/4831 | 4528/4530 | 6055/6057 |

Secondary uses the **global total added** inputs, with nonadjacent min/max keys. Their
maximum keys are not obtained by adding one. The base rows are secondary inputs; they
must not be replaced with spell inputs because the formulas look similar.

### 2.3 Common scopes and complete conversion histories

For an admitted typed callback, set `q = B(5997)` (`poison_is_skill_dot`). The common inputs are:

```text
I(c) = S1426 + S2007 + S11735 + S6113 + S1448 + S2019
     + (MH ? S11736 : OH ? S11737 : 0) + (q ? S5763 : 0)

M(c,b) = F(S1427) * F(S2008) * F(S6367) * F(S6179)
       * F(S1449) * F(S2020)
       * (MH ? F(S19873) : 1)
       * (MH/OH ? F(S19089 for min, S19090 for max) : 1)
       * (q ? F(S5764) : 1)
```

These are all damage, all DoT, ailment all DoT, poison, chaos, chaos DoT, optional hand
ailment and optional skill-DoT scopes. **OH has increased key 11737 but no counterpart to
MH final key 19873 in these callbacks.** Do not synthesize one. Spell/secondary have neither
the hand scopes nor minimum/maximum attack-final scope.

The conversion graph contains every directed edge allowed by the order
`physical -> lightning -> cold -> fire -> chaos`:

| Edge | Transfer key | Edge | Transfer key |
|---|---:|---|---:|
| physical -> lightning | 1515 | physical -> cold | 1514 |
| physical -> fire | 1513 | physical -> chaos | 1516 |
| lightning -> cold | 1518 | lightning -> fire | 1517 |
| lightning -> chaos | 1519 | cold -> fire | 1520 |
| cold -> chaos | 1521 | fire -> chaos | 1522 |

Each edge contributes `P(S(transfer))`. The retained fraction uses **only the final bucket's**
loss key: physical `1502`, fire `1503`, cold `1504`, lightning `1505`, chaos `1506`:
`L(t) = P(100 - S(loss(t)))`. Do not apply intermediate retained fractions to a conversion path.

Enumerate every ordered path ending at the selected bucket, including the one-node path.
This gives **1 physical, 2 lightning, 4 cold, 8 fire, or 16 chaos histories**. Each path starts
from its own base/added pair above. Source-history scopes use these increased/final pairs:
physical `1434/1435`, fire `1442/1443`, cold `1444/1445`, lightning `1446/1447`.
If any elemental node occurs, also include `1450/1451` **once**, even across several elements.
Chaos `1448/1449` is already included once in the common poison scopes; it is not added again
when the history contains chaos. Physical/fire/cold/lightning **DoT** scopes do not replace
chaos-DoT `2019/2020` merely because an earlier history node had that type.

Writing `Hinc(path)` for that increased sum and `Hfinal(path)` for its final-factor product,
the readable semantic expression is:

```text
D(c,t,b) = max(0, R(18 * sum over paths ending at t of:
    B(c,start(path),b) * product(P(S(edge))) * L(t)
    * F(add32(I(c), Hinc(path))) * M(c,b) * Hfinal(path)))
```

The literal coefficient is **18 damage/minute per unit base**, equivalent to `0.30/second`.
All chaos bodies and the MH/OH fire/cold bodies call `0x142513810` (16 callbacks);
the other 24 have an inline signed-rounding tail. All 40 clamp the resulting signed integer
to nonnegative.
None uses upper-saturating helper `0x142513840` or an equivalent upper-saturating branch.

This compact equation exposes scopes and histories; it is **not a bit-exact evaluation
order**. Float32 addition/multiplication grouping and intermediate rounding remain exactly
as in each callback. In particular, do not factor all common multipliers out of all histories
and assume identical low bits. The retained symbolic trees preserve the original grouping;
80 trees cover `q` off/on, checking 496 history instances against an independently specified
conversion graph and scope map. The per-output manifest lists all 248 histories for `q` on.

## 3. Complete totals and the misleading `always` family

### 3.1 Noncritical/critical totals

For each category and bound, let `p/f/c/l/h` be its five typed poison inputs from section 2:

```text
T = add32(l, h, c, f, p)
Total = R(float32(T) * F(S(multiplier)))
```

The native addition order is lightning, chaos, cold, fire, physical. All operands are
already integer typed rates. There is **one conversion after the integer sum** and no
upper saturation or final nonnegative clamp. This differs from a float sum of the five
inputs, and from `C(sum * factor)`.

| Category / branch | Minimum key / callback | Maximum key / callback | Multiplier |
|---|---|---|---:|
| MH / noncritical | 6122 / `0x1426c4440` | 6123 / `0x1426c4a10` | 10324 |
| MH / critical | 10360 / `0x1426c7310` | 10361 / `0x1426c78e0` | 10314 |
| OH / noncritical | 6124 / `0x1426c4fe0` | 6125 / `0x1426c55b0` | 10325 |
| OH / critical | 10362 / `0x1426c7eb0` | 10363 / `0x1426c8480` | 10315 |
| spell / noncritical | 6126 / `0x1426c2cc0` | 6127 / `0x1426c32a0` | 10326 |
| spell / critical | 10364 / `0x1426c5b90` | 10365 / `0x1426c6170` | 10316 |
| secondary / noncritical | 6128 / `0x1426c3880` | 6129 / `0x1426c3e60` | 10327 |
| secondary / critical | 10366 / `0x1426c6750` | 10367 / `0x1426c6d30` | 10317 |

MH minimum's four integer additions are `0x1426c48bb..0x1426c48c2`; its signed conversion
is `0x1426c49cb..0x1426c49cf`, multiplication `0x1426c49d2`, and non-saturating conversion
`0x1426c49f6`. Each of the other 15 complete bodies was checked for the same semantic shape.
Critical totals select a different multiplier; they do not sample critical chance or choose
whether the actual event was critical.

For a distinguishing numerical case, typed inputs `16,777,217` and `1` sum to `16,777,218`
before conversion. A float-by-float sum can instead become `16,777,216`. Two `2^30` inputs
overflow the 32-bit sum to `-2^31`; these callbacks do not clamp that signed result to zero.
Those are arithmetic properties under supplied inputs, not evidence that normal gameplay
admits every such synthetic combination.

### 3.2 `to_always_deal_per_minute` is not an all-type or expected-DPS total

All 16 callbacks implement this separate contract:

```text
if !B(2768) && S(6388) < 1: return 0
Always = R(float32(S(typed chaos input)) * F(S(multiplier)))
```

`2768` is `chaos_damage_poisons`; `6388` is `chaos_damage_chance_to_poison_%`. The latter
uses a **signed positive** comparison: 1 passes, not only 100. There is no probability
weighting, multiplication by chance, ordinary attack/spell chance key, or addition of the
other four typed rates. A nonzero `2768` bypasses that comparison. The return is again
non-saturating `R`, without a final nonnegative clamp. Preserve this observed contract;
the word `always` alone does not establish a stronger guarantee of successful application.

| Category / branch | Minimum key / callback | Maximum key / callback | Typed chaos min/max | Multiplier |
|---|---|---|---|---:|
| MH / noncritical | 6130 / `0x1426c97e0` | 6131 / `0x1426c9b40` | 6081/6082 | 10324 |
| MH / critical | 10368 / `0x1426cb2e0` | 10369 / `0x1426cb640` | 6081/6082 | 10314 |
| OH / noncritical | 6132 / `0x1426c9ea0` | 6133 / `0x1426ca200` | 6091/6092 | 10325 |
| OH / critical | 10370 / `0x1426cb9a0` | 10371 / `0x1426cbd00` | 6091/6092 | 10315 |
| spell / noncritical | 6134 / `0x1426c8a60` | 6135 / `0x1426c8dc0` | 6101/6102 | 10326 |
| spell / critical | 10372 / `0x1426ca560` | 10373 / `0x1426ca8c0` | 6101/6102 | 10316 |
| secondary / noncritical | 6136 / `0x1426c9120` | 6137 / `0x1426c9480` | 6111/6112 | 10327 |
| secondary / critical | 10374 / `0x1426cac20` | 10375 / `0x1426caf80` | 6111/6112 | 10317 |

MH minimum's bypass read starts at `0x1426c97e0`; its signed positive-chance gate and
non-saturating return are retained in the complete original body. Every wrapper was
checked with chance -1, 0, 1, 100 and both zero/nonzero bypass, including a negative
nonzero bypass. The typed inputs can still be zero because of their separate eligibility
and damage prohibitions.

## 4. Multiplier builders and branch precedence

| Output | Registration | Callback | Ordinary sum |
|---:|---|---|---|
| 10324 | `0x1400bf040` | `0x14276f020` | S10340 + S14121 |
| 10314 | `0x1400bf0a0` | `0x14276f330` | S10340 + S10333 + S10442 + S14121 |
| 10325 | `0x1400bf110` | `0x14276f900` | S10340 + S14122 |
| 10315 | `0x1400bf170` | `0x14276fc10` | S10340 + S10334 + S10442 + S14122 |
| 10326 | `0x1400bf1e0` | `0x1427701e0` | S10340 |
| 10316 | `0x1400bf240` | `0x1427703d0` | S10340 + S10335 + S10442 |
| 10327 | `0x1400bf2b0` | `0x1427708d0` | S10340 |
| 10317 | `0x1400bf310` | `0x142770ac0` | S10340 + S10336 + S10442 |

For every noncritical builder, test `12648` (`virtual_keystone_ailment_crit`) first:
if nonzero return `S(hit critical multiplier) - 100`, otherwise the ordinary sum.
For every critical builder, **first** test `10376` (`ailment_multipliers_never_critical`):
if nonzero return the corresponding supplied noncritical multiplier. Only otherwise test
12648 and then use the ordinary critical sum. This is replacement/selection, not an additive
Perfect Agony bonus on top of all ordinary terms. Arithmetic is signed 32-bit; these builders
contain no float rounding or nonnegative clamp.

| Category | Hit critical multiplier | Noncritical fallback |
|---|---:|---:|
| MH | 610 | 10324 |
| OH | 611 | 10325 |
| spell | 612 | 10326 |
| secondary | 717 | 10327 |

`10340` is the upstream combined poison DoT multiplier; `10442` is the critical poison
multiplier. Hand local additions are `14121/14122`. Their upstream qualifications are
separate from this selection. Secondary critical alias **10336**, registration
`0x1400beb10`, callback **`0x14276c1a0`**, returns `S(6175)` and shares that callback with
10333/10334/10335; it does not add another secondary-only critical term.

A returned noncritical rate does not prove permission to inflict poison on a noncritical
hit under Perfect Agony. Likewise, the never-critical multiplier fallback is not proof
that the attack or spell event itself was noncritical.

## 5. Combined poison increased and final modifiers

### 5.1 Increased key 6113

Registration `0x1400a0960`, callback `0x1425de390`:

```text
S6113 = add32(
    S3422, S6308,
    S6 * S6516, S7 * S7781,
    B2989 ? S6114 : 0,
    S581 >= 300 ? S6521 : 0,
    B20703 ? S20882 : 0,
    B3738 ? S6191 : 0)
```

| Inputs | Meaning |
|---|---|
| 3422 / 6308 | base poison increased / hit-and-poison increased |
| 6 / 6516 | current frenzy charges / poison increased per frenzy charge |
| 7 / 7781 | current power charges / poison increased per power charge |
| 2989 / 6114 | attack classification / poison increased from attack skills |
| 581 / 6521 | dexterity / poison increased with over-300-dexterity input |
| 20703 / 20882 | tincture applied / poison increased with tinctured weapons |
| 3738 / 6191 | is poisoned / damaging-ailment increased while suffering that ailment |

Despite `6521`'s name, the native boundary is **dexterity >= 300**, not strictly greater.
The charge products and sum are integer operations, without final clamp. The registration
span omits actual classification/count reads `2989`, `6`, and `7`; a declared dependency
span is not a complete read set. The terminal integer products/sum are at
`0x1425df023..0x1425df03e`.

### 5.2 Final key 6179

Registration `0x1400a09e0`, callback `0x1425df060`. With `attack = B2989`, its semantic factors are:

```text
proj  = attack && B1485 ? F(S6526) : 1
melee = attack && B1486 ? F(S6525) : 1
monster = B1162 ? 1 : F(S6178)

S6179 = R(100 * F(S9319) * F(S8276) * monster
              * F(S6240) * F(S6384) * proj * melee
              * F(S10212) * F(S19839) * F(S6385) - 100)
```

| Key | `Stats.Id` |
|---:|---|
| 1162 | `is_player_minion` |
| 6178 | `monster_poison_damage_+%_final` |
| 6240 | `support_debilitate_poison_damage_+%_final` |
| 6384 | `support_poison_poison_damage_+%_final` |
| 6526 | `support_phys_proj_attack_damage_bleeing_and_poison_damage_+%_final_from_projectile_hits` |
| 6525 | `support_melee_physical_damage_poison_and_bleeding_damage_+%_final_from_melee_hits` |
| 8276 | `herald_of_agony_poison_damage_+%_final` |
| 9319 | `virtual_faster_poison_%` |
| 10212 | `corrosive_shroud_poison_damage_+%_final_while_accumulating_poison` |
| 19839 | `active_skill_poison_damage_+%_final` |
| 6385 | `support_lethal_dose_poison_damage_+%_final` |

`is_player_minion` suppresses the monster-specific factor; it is not a generic entity-is-monster
test. Projectile and melee support qualification both additionally require attack classification;
when both supplied flags are true both factors can apply. The callback does not add independent
support-presence or accumulation-state tests beyond these named factor inputs.

This publishes a rounded **integer modifier**, then the typed poison callback applies `F(S6179)`.
Preserve that intermediate rounding instead of directly multiplying every upstream factor
into each rate. Faster poison contributes to outgoing rate here; this does not establish
instance snapshot policy or an expiration-clock update rule. Actual classification reads
`2989/1485/1486` are omitted from this registration's dependency span.

## 6. Numerical limits, verification, and remaining work

`P` is `0x141d1e400`, signed rounding is the leaf `0x142513810..0x14251383a`, and
`0x14013c620..0x14013c62f` selects the larger float operand by pointer. The distinct
upper-saturating leaf `0x142513840..0x14251387c` is retained to verify the distinction;
none of these 72 outgoing poison callbacks uses its upper clamp. Float-to-int results
outside the signed range produce the SSE indefinite integer `0x80000000`, rather than an
invented saturating value. Typed outputs subsequently select zero for such a negative integer;
totals and wrappers do not. Arbitrary invalid floats and invalid table indices are not a
portable high-level formula contract.

The ignored `r9-poison-verify.py` rechecks original executable/table identities, every owned
instruction byte and range, registration/dependency argument dataflow, callback coverage,
source/input maps, return shapes, helper/table bytes, full typed exports, and report links.
The separate original-instruction replay covers **1,576 cases / 601,293 interpreted instructions**
across all 83 callbacks, including both `poison_is_skill_dot` branches, 248 isolated conversion
histories with distinguishing source scopes, every inspected hand-weapon table value,
prohibitions, all multiplier branch precedences, dexterity 299/300/301, chance -1/0/1/100,
integer-sum precision/overflow, and non-saturating rounding behavior. This uses a synthetic
ready cache and separately audited helper substitutions; it is neither game execution nor
coverage of cache-miss/provider paths or arbitrary-input SSE equivalence.

The named rate family is closed for this executable and table. Remaining boundaries are
explicit: actual formula dispatch on network entities, outgoing-value publication/selection,
ailment application eligibility and RNG, per-instance sample/update timing, duration and
stack arbitration, defender mitigation, target-wide effective-rate aggregation/cap, and
resource deduction. This report implements no server behavior and supplies no new live
capture evidence for those unresolved boundaries.
