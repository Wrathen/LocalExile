# Client outgoing ignite rate family (3.29.3b)

## 1. Result and boundary

The pinned Stats table contains **56** named ignite `to_deal_per_minute` outputs. All 56
are covered here: 40 typed source rates (MH/OH/spell/secondary × five damage types ×
minimum/maximum), followed by 16 critical/noncritical totals. Each total sums five
already published typed integer rates and applies its own category/critical multiplier.
It does not choose a critical result or roll between minimum and maximum.

This completes the named ignite output family beyond the representative formulas in
[outgoing ailment formulas](client-ailment-outgoing-formulas.md). It also recovers the
eight category/critical multipliers, secondary critical alias, combined increased/final
ignite modifiers, and both hand-final aliases. Shared damage/DoT/ailment input producers
are reused from [ailment multiplier inputs](client-ailment-multiplier-inputs.md) and
[DoT multiplier inputs](client-dot-multiplier-inputs.md).

Two differences must survive any reconstruction. MH typed rates include final factor
19873; OH has no matching factor. For all 16 nonfire hand outputs, qualification can
fall back to `tincture_applied` AND `all_damage_with_tinctured_weapons_can_ignite`.
Those bodies read 20703 although their registration vectors omit it, and do not read
the weapon-type key 3/4 that those vectors declare. Registration metadata alone is not
a complete description of arithmetic inputs or conditional access.

These are **outgoing context-evaluation formulas**. This pass establishes neither
invocation on an ordinary network entity nor application-time sampling, duration clocks,
strongest-ignite selection, target mitigation, shared per-target DoT cap, or actual
resource deduction. No client execution, attachment, new capture, or server change was
performed. Local output saturation is not evidence of the shared target DoT cap.

| Artifact / method | Identity |
|---|---|
| Executable | `Client/Game.exe`, 3.29.3b x86-64, preferred base `0x140000000` |
| Executable SHA256 | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `data/tables/Stats.json` SHA256 | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| Stat mapping | Native key `k` = JSON row `k - 1`, field `Id`; 23,346 rows |
| Evidence | Original PE bytes, unwind-owned ranges, Capstone; isolated bounded Ghidra 12.1.4 typed export with `-noanalysis` |
| Batch | 69 registrations, 142 function records, 278 ranges, 80,120 instruction records, no undecoded bytes; 69 completed selected typed exports |

## 2. Arithmetic and publication boundaries

```text
S(k) = current effective integer input for native key k
B(k) = S(k) != 0
P(n) = max(0, float32(n)/100)
F(n) = P(add32(n,100))
R(x) = cvtt_i32(float32(x + (x < 0 ? -0.5f : +0.5f)))
C(x) = x >= 2147483648.0f ? 2147483647 : R(x)
A    = F(trunc0(S(6172)/100))
q    = B(5996)                         # ignite_is_skill_dot
```

Integer sums/subtractions use signed low32 where the original uses them. Float operations,
integer conversion, intermediate products, and the signed 0.5 addition are binary32.
These equations describe normal finite inputs and the recovered algebra; they do not
permit arbitrary reassociation for bit-exact SSE emulation. The original instruction
order remains in ignored assembly and untouched typed exports. In particular, `A` divides
the integer permyriad modifier before percent conversion; 6172 is
`active_skill_base_attack_damage_final_+permyriad`.

For a finite value whose truncation fits signed32, `cvtt_i32` truncates toward zero;
finite out-of-range conversion returns the SSE indefinite value `-2147483648`. This
is distinct from upper saturation in `C` and from the typed nonnegative clamp.

The percent helper is `0x141d1e400`. Signed rounding is
`0x142513810..0x14251383a`; upper-saturating rounding is the separate leaf
`0x142513840..0x14251387c`. Typed rates additionally clamp the returned integer to
nonnegative. Some bodies inline saturation and rounding, others call the latter leaf.

The publication boundaries matter: 6158 publishes a rounded integer percentage modifier;
each typed rate publishes a rounded integer damage/minute rate; each total converts five
of those integers to binary32 and rounds again after its multiplier. There is no integer
publication between conversion histories within one typed callback. One final rounding
for the entire chain would produce different results.

## 3. Complete typed output registry

Minimum and maximum are separate callbacks with their own inputs. They share the algebra
below but not necessarily identical register allocation or SSE evaluation order.

| Category / current source type | Output min / max | Registration min / max | Callback min / max |
|---|---|---|---|
| MH Physical | 6014 / 6015 | `0x1400a7910` / `0x1400a79f0` | `0x142656dc0` / `0x1426586a0` |
| MH Fire | 6016 / 6017 | `0x1400a7ad0` / `0x1400a7be0` | `0x142659f80` / `0x14265bbe0` |
| MH Cold | 6018 / 6019 | `0x1400a7cf0` / `0x1400a7df0` | `0x14265d850` / `0x14265f2f0` |
| MH Lightning | 6020 / 6021 | `0x1400a7ef0` / `0x1400a7fe0` | `0x142660d90` / `0x142662710` |
| MH Chaos | 6022 / 6023 | `0x1400a80d0` / `0x1400a8210` | `0x1426640a0` / `0x142666280` |
| OH Physical | 6024 / 6025 | `0x1400a8350` / `0x1400a8420` | `0x142668460` / `0x142669d70` |
| OH Fire | 6026 / 6027 | `0x1400a84f0` / `0x1400a8600` | `0x14266b680` / `0x14266d2e0` |
| OH Cold | 6028 / 6029 | `0x1400a8710` / `0x1400a8810` | `0x14266ef40` / `0x1426709c0` |
| OH Lightning | 6030 / 6031 | `0x1400a8910` / `0x1400a8a00` | `0x142672440` / `0x142673dc0` |
| OH Chaos | 6032 / 6033 | `0x1400a8af0` / `0x1400a8c20` | `0x142675730` / `0x1426778f0` |
| Spell Physical | 6034 / 6035 | `0x1400a65b0` / `0x1400a6680` | `0x142634900` / `0x142636130` |
| Spell Fire | 6036 / 6037 | `0x1400a6750` / `0x1400a6850` | `0x142637960` / `0x142639570` |
| Spell Cold | 6038 / 6039 | `0x1400a6950` / `0x1400a6a40` | `0x14263b180` / `0x14263cc30` |
| Spell Lightning | 6040 / 6041 | `0x1400a6b30` / `0x1400a6c00` | `0x14263e6e0` / `0x142640070` |
| Spell Chaos | 6042 / 6043 | `0x1400a6cd0` / `0x1400a6df0` | `0x142641a00` / `0x142643ac0` |
| Secondary Physical | 6044 / 6045 | `0x1400a6f10` / `0x1400a6fe0` | `0x142645b80` / `0x1426473b0` |
| Secondary Fire | 6046 / 6047 | `0x1400a70b0` / `0x1400a71b0` | `0x142648be0` / `0x14264a7d0` |
| Secondary Cold | 6048 / 6049 | `0x1400a72b0` / `0x1400a73a0` | `0x14264c3c0` / `0x14264de70` |
| Secondary Lightning | 6050 / 6051 | `0x1400a7490` / `0x1400a7560` | `0x14264f910` / `0x1426512a0` |
| Secondary Chaos | 6052 / 6053 | `0x1400a7630` / `0x1400a7750` | `0x142652c30` / `0x142654cf0` |

### Qualification and short circuit

All 40 typed outputs return zero when 5999 (`deal_no_ignite_damage_over_time`) or their
category/type prohibition is nonzero. Prohibition keys follow the type order
physical/fire/cold/lightning/chaos:

| Category | Five prohibition keys |
|---|---|
| Spell | 1723, 1724, 1725, 1726, 1727 |
| Secondary | 1728, 1729, 1730, 1731, 1732 |
| MH | 1733, 1734, 1735, 1736, 1737 |
| OH | 1738, 1739, 1740, 1741, 1742 |

Fire requires **zero** 6010 (`virtual_fire_damage_cannot_ignite`). The other four types
require their positive permission 6009 / 6011 / 6012 / 6013 respectively. The upstream
permission producers are covered in [ailment eligibility](client-ailment-eligibility.md).
For a nonfire MH/OH typed rate only, the alternative is:

```text
typePermission OR (B(20703) AND B(19551))
20703 = tincture_applied
19551 = all_damage_with_tinctured_weapons_can_ignite
```

There is no additional weapon-table test in these callback bodies. This does not establish
how upstream context/provider construction constrains the tincture inputs. It also does
not establish an invalidation bug from the metadata mismatch.

Guard read order is not uniform. Spell/secondary and hand-fire callbacks read global
prohibition, category/type prohibition, and then type permission/prohibition before 5996.
Nonfire hand callbacks test permission first, read tincture inputs only when needed, and
then test the prohibitions. For example, eligible MH physical reads
`6009 → 5999 → 1733 → 5996`; with 6009 zero it may first read `20703 → 19551`.
The equivalent Boolean formula is not permission to erase those short-circuit reads.

### Base and added source inputs

Each cell lists **base min/max; added min/max**. All keys are literal Stats inputs; the
secondary added maxima deliberately advance by two rather than one.

| Category | Physical | Fire | Cold | Lightning | Chaos |
|---|---|---|---|---|---|
| MH | 1563/1564; 1575/1576 | 1565/1566; 1579/1580 | 1567/1568; 1583/1584 | 1569/1570; 1587/1588 | 1571/1572; 1591/1592 |
| OH | 1593/1594; 1605/1606 | 1595/1596; 1609/1610 | 1597/1598; 1613/1614 | 1599/1600; 1617/1618 | 1601/1602; 1621/1622 |
| Spell | 1533/1534; 1545/1546 | 1535/1536; 1549/1550 | 1537/1538; 1553/1554 | 1539/1540; 1557/1558 | 1541/1542; 1561/1562 |
| Secondary | 5329/5330; 4219/4221 | 1525/1526; 4930/4932 | 1527/1528; 4829/4831 | 1529/1530; 4528/4530 | 1531/1532; 6055/6057 |

For one origin type `o`, selected category `c` and bound `b`:

```text
base(c,b,o) = float32(S(baseKey)) * (hand ? A : 1)
              + float32(S(addedKey)) * F(S(5761))
```

6172 applies to base hand damage, not to the added term; 5761 is
`combined_all_added_damage_+%_final`. Spell and secondary do not read 6172. These callbacks
consume upstream effective base/added values; recovering their producers is a separate
boundary, not evidence that these are raw gem/weapon values.

### Conversion histories and actual fire scope

The typed labels describe damage sourcing ignite. They are not five outgoing ailment
damage types: every contribution uses the common actual-fire/elemental DoT scopes below.
Histories follow this directed acyclic graph:

```text
physical  -> lightning (1515), cold (1514), fire (1513), chaos (1516)
lightning -> cold (1518), fire (1517), chaos (1519)
cold      -> fire (1520), chaos (1521)
fire      -> chaos (1522)
```

Each edge factor is `P(S(edgeKey))`. For the final current source type `t`, the retained
factor is `P(sub32(100,S(lostKey)))`, with lost keys physical 1502, fire 1503, cold 1504,
lightning 1505, chaos 1506. Intermediate vertices do **not** add their lost factors to a
history. A history includes its origin and end; the direct unconverted term is a
one-vertex history. The five typed callbacks contain respectively 1 / 8 / 4 / 2 / 16
histories in physical/fire/cold/lightning/chaos table order.

Let `visited` be the source types on one history. The extra source-type increased/final
pairs are physical 1434/1435, cold 1444/1445, lightning 1446/1447, chaos 1448/1449. Fire
is already present once in the common actual-fire scope; it must not be added twice when
a history includes a fire vertex.

```text
I0 = S(1426) + S(2007) + S(11735) + S(5994)
     + S(1450) + S(2011) + S(1442) + S(2013)
     + (q ? S(5763) + S(5988) + S(5989) : 0)
     + (MH ? S(11736) : OH ? S(11737) : 0)

M0 = F(S(1427)) * F(S(2008)) * F(S(6367)) * F(S(6158))
     * F(S(1451)) * F(S(2012)) * F(S(1443)) * F(S(2014))
     * (q ? F(S(5764)) * F(S(7845)) : 1)
     * (MH ? F(S(19873)) : 1)
     * (hand ? F(S(handFinal)) * F(S(boundFinal)) : 1)

history = base(c,b,origin) * product(edge factors)
          * retained(t)
          * F(I0 + sum(sourceIncreased(v) for v in visited excluding fire))
          * M0 * product(F(S(sourceFinal(v))) for v in visited excluding fire)

typedRate = max(0, C(54 * sum(all histories ending at t)))
```

| Shared scope | Increased / final input |
|---|---|
| All damage | 1426 / 1427 |
| All DoT | 2007 / 2008 |
| All ailment DoT | 11735 / 6367 |
| Ignite | 5994 / 6158 |
| Elemental damage | 1450 / 1451 |
| Elemental DoT | 2011 / 2012 |
| Fire damage | 1442 / 1443 |
| Fire DoT | 2013 / 2014 |
| Skill DoT, only when 5996 | all 5763 / 5764; elemental increased 5988; fire 5989 / 7845 |
| Hand-specific increased | MH 11736; OH 11737 |
| Hand final | MH 14288; OH 14289 |
| Bound final | minimum 19089; maximum 19090 |
| Additional MH final | 19873; no OH counterpart read by this family |

There is no direct spell-damage multiplier in these callbacks. The common DoT scopes are
actual fire/elemental scopes even for physical/cold/lightning/chaos source terms. The
conversion-history type modifiers are regular damage modifiers, not an invented physical
or chaos DoT multiplier for those histories. The hard-coded `54` is the recovered outgoing
per-minute coefficient; this pass does not infer duration or selection behavior from it.

## 4. Sixteen critical/noncritical totals

| Output | Five typed inputs | Multiplier | Registration | Callback |
|---:|---|---:|---|---|
| 6058 | 6014, 6016, 6018, 6020, 6022 | 10320 | `0x1400a8f10` | `0x14267b2f0` |
| 6059 | 6015, 6017, 6019, 6021, 6023 | 10320 | `0x1400a8f80` | `0x14267b8f0` |
| 6060 | 6024, 6026, 6028, 6030, 6032 | 10321 | `0x1400a8ff0` | `0x14267bef0` |
| 6061 | 6025, 6027, 6029, 6031, 6033 | 10321 | `0x1400a9060` | `0x14267c510` |
| 6062 | 6034, 6036, 6038, 6040, 6042 | 10322 | `0x1400a8d50` | `0x142679ab0` |
| 6063 | 6035, 6037, 6039, 6041, 6043 | 10322 | `0x1400a8dc0` | `0x14267a0d0` |
| 6064 | 6044, 6046, 6048, 6050, 6052 | 10323 | `0x1400a8e30` | `0x14267a6f0` |
| 6065 | 6045, 6047, 6049, 6051, 6053 | 10323 | `0x1400a8ea0` | `0x14267acf0` |
| 10352 | 6014, 6016, 6018, 6020, 6022 | 10310 | `0x1400a9290` | `0x14267e370` |
| 10353 | 6015, 6017, 6019, 6021, 6023 | 10310 | `0x1400a9300` | `0x14267e970` |
| 10354 | 6024, 6026, 6028, 6030, 6032 | 10311 | `0x1400a9370` | `0x14267ef70` |
| 10355 | 6025, 6027, 6029, 6031, 6033 | 10311 | `0x1400a93e0` | `0x14267f590` |
| 10356 | 6034, 6036, 6038, 6040, 6042 | 10312 | `0x1400a90d0` | `0x14267cb30` |
| 10357 | 6035, 6037, 6039, 6041, 6043 | 10312 | `0x1400a9140` | `0x14267d150` |
| 10358 | 6044, 6046, 6048, 6050, 6052 | 10313 | `0x1400a91b0` | `0x14267d770` |
| 10359 | 6045, 6047, 6049, 6051, 6053 | 10313 | `0x1400a9220` | `0x14267dd70` |

Each original body converts the typed integers to binary32, adds them in the order
**fire + physical, then cold, lightning, chaos**, multiplies by `F(S(multiplier))`,
and publishes `C(result)`. The final totals have **no additional max(integer,0)** clamp.
Ordinary upstream typed outputs are nonnegative, but the distinction remains observable
with supplied negative input values. Summation is floating point, not signed integer
addition; large totals saturate after the multiplication.

These outputs describe separate critical/noncritical rate ranges. They do not perform a
critical chance weighted average, select an actual critical event, or merge distinct
ignite instances. Per-category min/max totals retain separate source inputs.

## 5. Category multipliers and combined modifiers

| Key | Stat | Registration | Callback |
|---:|---|---|---|
| 10320 | `total_main_hand_ignite_non_critical_ailment_dot_multiplier_+` | `0x1400bed00` | `0x14276d450` |
| 10310 | `total_main_hand_ignite_critical_ailment_dot_multiplier_+` | `0x1400bed60` | `0x14276d640` |
| 10321 | `total_off_hand_ignite_non_critical_ailment_dot_multiplier_+` | `0x1400bedd0` | `0x14276db40` |
| 10311 | `total_off_hand_ignite_critical_ailment_dot_multiplier_+` | `0x1400bee30` | `0x14276dd30` |
| 10322 | `total_spell_ignite_non_critical_ailment_dot_multiplier_+` | `0x1400beea0` | `0x14276e230` |
| 10312 | `total_spell_ignite_critical_ailment_dot_multiplier_+` | `0x1400bef00` | `0x14276e420` |
| 10323 | `total_secondary_ignite_non_critical_ailment_dot_multiplier_+` | `0x1400bef70` | `0x14276e920` |
| 10313 | `total_secondary_ignite_critical_ailment_dot_multiplier_+` | `0x1400befd0` | `0x14276eb10` |
| 10336 | `combined_secondary_critical_ailment_dot_multiplier_+` | `0x1400beb10` | `0x14276c1a0` |
| 5994 | `combined_ignite_damage_over_time_+%` | `0x1400a06e0` | `0x1425dce60` |
| 6158 | `combined_ignite_damage_over_time_+%_final` | `0x1400a0750` | `0x1425dd4f0` |
| 14288 | `combined_main_hand_ignite_damage_over_time_+%_final` | `0x1400a07d0` | `0x1425de190` |
| 14289 | `combined_off_hand_ignite_damage_over_time_+%_final` | `0x1400a0820` | `0x1425de290` |

### Category and critical precedence

| Category | Noncritical / critical output | Hit critical multiplier H | Ailment critical input K |
|---|---|---:|---:|
| MH | 10320 / 10310 | 610 | 10333 |
| OH | 10321 / 10311 | 611 | 10334 |
| Spell | 10322 / 10312 | 612 | 10335 |
| Secondary | 10323 / 10313 | **717** | 10336 |

```text
noncritical = B(12648) ? sub32(S(H),100) : S(10339)

critical = B(10376) ? S(correspondingNoncritical)
         : B(12648) ? sub32(S(H),100)
         : add32(S(10339),S(K),S(10441))
```

12648 is `virtual_keystone_ailment_crit`; 10376 is `ailment_multipliers_never_critical`.
The first critical branch wins over 12648. 10339 is the virtual ailment DoT multiplier
recovered in [ailment multiplier inputs](client-ailment-multiplier-inputs.md).
10441 is `critical_ignite_dot_multiplier_+`.
The secondary critical alias 10336 returns 6175, the same shared ailment critical input
used by the other category aliases; its function is `0x14276c1a0`.

These builders publish integer percentage modifiers, without a final clamp. They do not
silently add a second local hand-specific ignite DoT multiplier to the typed formula.

### Combined increased modifier 5994

```text
S(5994) = add32(S(5993),
               B(2989) ? S(6072) : 0,
               B(20703) ? S(20884) : 0,
               B(1843) ? S(6191) : 0)
```

5993 is ignite increased damage; 6072 is ignite damage from attack skills; 2989 is
`skill_is_attack`; 20884 is ignite increased damage with tinctured weapons; 1843 is `is_ignited`;
6191 is damaging-ailment increased damage while suffering that ailment. The callback
reads 2989 although its declared dependency vector omits it. Its signed sum is not
clamped. This formula does not prove which entity the caller supplies for `is_ignited`.

### Combined final modifier 6158

```text
monster = !B(1162) ? F(S(6176)) : 1
recent  = F(min_signed(S(19181),S(18842)))
purist  = B(23308) ? 0.75 : 1

S(6158) = R(100 * monster * F(S(6203)) * purist
            * F(S(7916)) * F(S(8017)) * F(S(6366)) * F(S(7946))
            * F(S(14330)) * F(S(16079)) * recent
            / (1 + float32(S(6577))/100) - 100)
```

The binary32 multiplication order is `monster`, 6203, 100, purist, 7916, 8017, 6366,
7946, 14330, 16079, recent, then multiplication by the separately computed reciprocal
slower factor, then subtraction of 100. In particular, the recent pair is a **signed
integer minimum before** percent conversion, not a multiplication of both factors.

| Input | Meaning |
|---:|---|
| 6203 | `virtual_ignite_faster_burn_%` |
| 6577 | `ignite_slower_burn_%` |
| 1162 / 6176 | player-minion predicate / monster ignite final damage modifier |
| 7916 | expanding fire cone release ignite final damage modifier |
| 8017 | elementalist ignite final damage modifier |
| 6366 | ignite proliferation support final damage modifier |
| 7946 | active skill ignite final damage modifier |
| 14330 | Vaal Righteous Fire ignite final damage modifier |
| 16079 | Infernal Cry ignite final damage modifier |
| 18842 / 19181 | recent-ignite support final modifier / its maximum |
| 23308 | `keystone_fire_purist`, changes the base factor from 1 to 0.75 |

6158 uses signed rounding without the upper-saturation helper or an explicit denominator
clamp. `S(6577) == -100` and nonfinite intermediates are outside the finite algebra
summarized here. Its presence in a rate formula does not
establish how an active instance's duration/rate changes over time; see
[DoT instance rules](client-dot-instance-rules.md) for the separate instance evidence.

### Hand-final aliases

14288 returns 14284 (`ignite_damage_+%_final_with_main_hand`);
14289 returns 14285 (`ignite_damage_+%_final_with_off_hand`). Both return the signed low32
input without extra arithmetic or clamping. All ten typed outputs of their respective
hand read the alias. Spell and secondary read neither.

## 6. Verification and remaining work

The ignored verifier re-decodes every owned function range from the pinned executable,
checks its full byte stream, literal bytes, all 69 registration vectors (1,968 declared
inputs), Stats identities, and all 69 untouched completed typed exports. The 56-output
inventory is also re-derived from all Stats rows rather than inferred from contiguous IDs.

An offline interpreter follows the original decoded integer/SSE instructions with all
input cache bits supplied as set. It models the already recovered stat-read, nonzero
predicate, percent, max-reference and saturation helpers. It does **not** execute native
client code or simulate an ordinary network provider. Checks include:

- 2,224 qualification/short-circuit branch cases across all 40 typed outputs, including
  negative nonzero predicates and the hand-only tincture fallback.
- 2,384 typed arithmetic cases: every conversion history in every category/bound callback,
  additive inherited increases, multiplicative inherited finals, all histories together,
  skill-DoT gating, each common/hand/bound modifier, retained loss and output boundaries
  (including finite lower conversion overflow before the typed nonnegative clamp).
- 112 aggregate cases, including binary32 integer conversion, signed inputs, final-factor
  clamp, multiplication before upper saturation and finite lower conversion overflow;
  216 category multiplier
  precedence/overflow cases.
- 49 combined-modifier/alias cases, including recent-ignite minimum, player-minion gate,
  Fire Purist, faster/slower factors and the undeclared 5994 attack predicate.

The finite arithmetic cases use distinguishing inputs with controlled binary factors;
they corroborate the displayed algebra without claiming arbitrary-input bit-exact
reassociation. The canonical ignored assembly remains the instruction-order reference.
The retained per-callback profile records exact read sets, default eligible read traces,
conversion histories, coefficient/saturation/clamp sites, and metadata discrepancies.

The local verification result is `r9-ignite-verification.json` in the ignored audit
output. Discovery is bounded to this pinned executable and selected output family.
Remaining boundaries include upstream context invocation, effective base/added producers,
application-time snapshot behavior, active-instance clocks and strongest selection,
target-side mitigation, per-target aggregation/cap and frame resource deduction.
