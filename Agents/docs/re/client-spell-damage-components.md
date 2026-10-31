# Native spell base and added damage components (3.29.3b)

## Result and scope

Thirty registered callbacks compose the minimum/maximum base, intermediate-added
and selected-added amounts for physical, fire, cold, lightning and chaos spell
damage. This report recovers their complete arithmetic and qualification rules.
The four upstream life-cost/weapon contributors have their own
[builder report](client-spell-contributor-builders.md).

These are component amounts before downstream effectiveness, conversion and
damage modifiers. They are not a hit roll, ailment application, target mitigation
or resource deduction. The native registration/evaluation path remains distinct
from [network Actor/skill providers](client-stat-evaluation.md).
Recovered equations do not prove that every network entity executes them.

All evidence is static/offline original PE instructions and synthetic ready-cache
interpretation. No client execution, attachment, new capture, server change or CI
was performed. Preferred image base is `0x140000000`; executable SHA256 is
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`.
The 23,346-row `Stats.json` SHA256 is
`91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da`;
native key `k` means JSON row `k-1`.

## Registered outputs

Each slash-separated pair is minimum/maximum. The table gives callback VAs;
the retained registrar reconstruction separately binds output, callback and
declared dependency vector through `0x142513630`.

| Type | Base keys / callbacks | Intermediate-added keys / callbacks | Selected-added keys / callbacks |
|---|---|---|---|
| Physical | 1533/1534; `0x142553d00` / `0x142554200` | 1543/1544; `0x142557500` / `0x142558c70` | 1545/1546; `0x142556e70` / `0x1425585d0` |
| Fire | 1535/1536; `0x142554700` / `0x142554c10` | 1547/1548; `0x14255a3e0` / `0x14255b720` | 1549/1550; `0x142559d40` / `0x14255b080` |
| Cold | 1537/1538; `0x142555120` / `0x1425555d0` | 1551/1552; `0x14255ca60` / `0x14255dda0` | 1553/1554; `0x14255c3c0` / `0x14255d700` |
| Lightning | 1539/1540; `0x142555a70` / `0x142555f10` | 1555/1556; `0x14255f0a0` / `0x142560850` | 1557/1558; `0x14255ea00` / `0x1425601b0` |
| Chaos | 1541/1542; `0x1425563b0` / `0x142556910` | 1559/1560; `0x142562020` / `0x142563670` | 1561/1562; `0x142561980` / `0x142562fd0` |

Base IDs have form `spell_total_<minimum|maximum>_base_<type>_damage`;
intermediate IDs are `intermediate_spell_total_<minimum|maximum>_added_<type>_damage`;
selected IDs are `spell_total_<minimum|maximum>_added_<type>_damage`.
Thus similar names describe three different component stages.

## Numeric conventions

`S[k]` is the signed32 native input. `W(x)` wraps to signed32;
`T(x,n)` divides by positive `n`, truncating toward zero.
Every integer addition/product below wraps unless explicitly inside a division
of an already wrapped product. No implicit arbitrary-precision multiplication
or final nonnegative clamp is intended.

`f(x)` converts/rounds to binary32 after **each** shown operation.
Define `P(x)=max(0,f(f(x)/100))`.
`R(x)` adds a binary32 signed half (`-0.5` for negative `x`, otherwise `+0.5`)
then uses signed32 truncating `CVTTSS2SI`. Out-of-range results become
`INT_MIN`, not saturation. `C(x)` first returns `INT_MAX` when `x>=2^31`;
otherwise it uses `R(x)`. It has no additional lower clamp.

The distinction is observable: the physical Ward term uses `R`; fire/chaos
base sums use `C`; integer cold/lightning components use neither.

## Base amounts

Pair-valued `S[a/b]` selects the matching minimum/maximum input, including the
intentional reversed physical shield-quality keys 12858/12857.

```text
physical = W(R(f(f(S[14544]) * P(S[22069/22070])))
             + S[1475/1476] + W(S[12856] * S[12858/12857]))

fireInteger = W(S[1477/1478] + W(S[8702] * S[476/477]))
fire = C(f(f(f(S[7418]) * P(S[6975])) + f(fireInteger)))

cold = W(W(T(S[578],10) * S[2815/2816])
         + S[1479/1480] + W(S[8703] * S[478/479]))

lightning = W(T(W(S[248] * S[23232]),100)
              + S[1481/1482] + W(S[8704] * S[357/358]))

lifeChaos = f(f(S[7418]) * P(S[14435]))
esChaos = f(f(S[14405]) * P(S[14436]))
chaos = C(f(esChaos + f(lifeChaos + f(S[1483/1484]))))
```

Here 14544 is Ward, 12856 virtual shield quality, 7418 skill-user maximum
Life, 578 Intelligence, 248 maximum Mana, and 14405 skill-user maximum ES.
8702/8703/8704 are removable endurance/frenzy/power charge counts.
1475..1484 are the skill's typed base endpoints. Percentage inputs are clamped
by `P`; source resource values themselves are not clamped in these bodies.

Useful original instruction anchors include physical Ward `mulss` and rounding
at `0x1425541bc..0x1425541e4`, followed by raw integer additions;
fire integer combination and saturation at `0x142554ba5..0x142554bed`;
lightning wrapped product/division at `0x142555ee0..0x142555efd`;
and chaos ordered additions/saturation at `0x1425568a0..0x1425568e1`.
Physical rounds only its Ward term before adding the other components.
Fire rounds the combined result. Chaos adds Life to intrinsic base before ES.
Reassociating these expressions can change an endpoint.

## Common intermediate-added contributions

Start with main-hand coefficient `m=S[13705]`, adding `S[13707]` through `W`
for physical only; off-hand coefficient is `o=S[18333]`.
These coefficients come from the [four contributor builders](client-spell-contributor-builders.md).
Input 11036, `gain_%_of_base_wand_damage_as_added_spell_damage`, is then
distributed by actual hand types 3/4:

```text
w = S[11036]
if w != 0:
    if mainType == 6:
        if offType == 6: m=W(m+T(w,2)); o=W(o+T(w,2))
        else:           m=W(m+w)
    elif offType == 6:   o=W(o+w)
```

Both wands receive a separately truncated half. An odd remainder is not assigned
to one hand. The branch is visible at `0x142557a3f..0x142557a64`.
For each typed endpoint, add `T(W(weaponBase*m),100)` only when `m>0`, and
the equivalent off-hand term only when `o>0`.
Nonpositive coefficients suppress the corresponding weapon-base read.
Main weapon base pairs are physical 1563/1564, fire 1565/1566, cold 1567/1568,
lightning 1569/1570, chaos 1571/1572; off-hand uses 1593..1602 in the same order.

Three other contribution families use independent qualifications:

| Type | If 246 is nonzero (dual wielding) | If main type is two-handed | If off type is 13 (shield) |
|---|---|---|---|
| Physical | 6837/6838 | 6847/6848 | 6857/6858 |
| Fire | 6831/6832 | 6841/6842 | 6851/6852 |
| Cold | 6833/6834 | 6843/6844 | 6853/6854 |
| Lightning | 6835/6836 | 6845/6846 | 6855/6856 |
| Chaos | 6839/6840 | 6849/6850 | 6859/6860 |

The two-handed predicate is the byte table at `0x142fdd688`, with nonzero
indices `{2,4,8,9,11,15,17}` in the inspected domain `0..17`.
All ten callbacks use that same table. They do not bounds-check the type index;
out-of-domain indexing is outside the verified model.
The dual-wield Boolean is read independently rather than recomputed from types.
Synthetic inconsistent combinations test the formula, not real equipability.

## Type-specific intermediate-added contributions

Add the following terms to the common contributions, then return their wrapped
signed32 sum. There is no final float conversion, saturation or zero floor.

| Type | Unconditional terms | Conditional terms |
|---|---|---|
| Physical | `S[1623/1624] + S[4219/4221] + W(S[7455]*S[7459/7460]) + S[19700] + W(T(S[1],3)*S[6751/6752]) + W(S[3458]*S[23196/23197])` | None beyond the common gates |
| Fire | `S[1625/1626] + S[4930/4932] + S[5625/5626] + W(S[3075]*S[3116/3117])` | Add 7682/7683 only when 4206 is zero |
| Cold | `S[1627/1628] + S[4829/4831] + S[3958/3959] + W(S[7]*S[5272/5273])` | Add 7684/7685 only when 4206 is zero |
| Lightning | `S[1629/1630] + S[4528/4530] + S[3257/3258] + W(T(S[578],10)*S[21392/21393]) + W(S[7]*S[7407/7408])` | Add 7686/7687 only when 4206 is zero; add 2763/2764 only when main type is 14 |
| Chaos | `S[1631/1632] + S[6055/6057] + W(T(S[575],50)*S[13001/13000]) + R(f(f(f(S[19724])/100)*f(S[247])))` | Add 5635/5636 only when 3918 is nonzero |

Inputs 7455, 3075, 7 and 3458 are siphoning charges, active Buff count, power
charges and active permanent minions. 4206 is reserved Life; 3918 is flask
activity; 575 is Strength; 1 is level. Input 19724 adds chaos equal to a
percentage of maximum Life (247). Its percentage is **not** clamped by `P`.
The Strength-based min/max coefficient order 13001/13000 is intentional.
Main type 14 is the native unarmed qualification in these lightning callbacks.

The level, Intelligence and Strength divisions happen before multiplication.
The weapon and base-Mana products wrap before division. These differences
must survive any subsequent implementation of the recovered formulas.

## Selected-added type routing

Input 22775 selects the added-damage destination. With zero, each callback
returns its own typed intermediate-added input unchanged. With matching type
code it returns the wrapped sum of all five same-endpoint intermediate inputs.
Any other nonzero code returns zero.

| Destination | Matching code |
|---|---|
| Physical | 1 |
| Fire | 3 |
| Cold | 2 |
| Lightning | 4 |
| Chaos | 5 |

Minimum and maximum route separately. No effectiveness multiplication or
additional typed-more factor occurs in these ten selection bodies.
The result preserves base-versus-added separation; it does not combine the
skill's intrinsic base endpoints into the added pool. See the
[existing offence composition](client-offence-estimates.md) and
[table-to-native producer audit](client-stat-evaluation.md) for adjacent stages.

## Evidence and remaining work

Ignored evidence under `Tools/re/out/client-damage-audit/` retains
`r13-spell-components/` original bytes, registrar dependency vectors,
ready-cache read sets and equation comparisons, plus `r13-spell-equations.py`
and its verifier. The combined preparation includes four separately reported
builders: 34 callbacks, 68 registrar/callback entries and 80 unwind ranges.
Only the selected reviewed contracts enter the public evidence manifest.

The verifier passed 8,310 comparisons and 1,133,938 interpreted instruction steps.
It compares independently written equations against interpretation of
the original instructions, including signed extremes, every `0..17` hand-type
pair, odd/negative wand coefficients, endpoint routing and seeded combinations.
Interpretation substitutes the previously bound scalar helpers and a fully
valid synthetic stat cache; it is not native execution or a provider-refresh
test. Independent review uses a separate earlier integer/SSE interpreter and
337 named numerical/read-boundary controls across all thirty callbacks;
it also rereads 60 original registrar/callback records, 70 unwind ranges,
17,463 instructions and 21 explicit byte anchors. Those checks are retained
with the sealed evidence.
Fire intermediate outputs 1547/1548 actually read active-Buff count 3075,
which is absent from both registrar dependency vectors. All other actual input
unions match their declared sets in the selected thirty callbacks.
Declared dependencies are therefore not an automatic invalidation proof.

This closes the selected component formulas. Actual source grants, native
evaluation on a particular network entity, random hit selection, ailment
infliction and target/resource application retain their separate open edges.
