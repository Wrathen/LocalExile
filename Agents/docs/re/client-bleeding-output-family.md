# Client outgoing bleeding rate family (3.29.3b)

## 1. Result and boundary

The twelve bleeding `to_deal_per_minute` Stats in the pinned client form a complete named
family: four physical-source rates for main/off hand and minimum/maximum, followed by eight
critical/noncritical outputs. The eight final outputs each multiply **one** physical-source
rate by the appropriate hand/critical DoT multiplier. They do not sum five damage types,
choose a critical result, or roll a value between minimum and maximum.

This extends the representative MH-minimum formula in
[outgoing ailment formulas](client-ailment-outgoing-formulas.md). It recovers all twelve
named rate outputs, the four hand/critical multiplier builders, both combined bleeding damage
modifiers, the offhand shared critical-multiplier alias, and the virtual frenzy multiplier.
The combined all-bleeding multiplier and virtual faster producer are reused from
[ailment multiplier inputs](client-ailment-multiplier-inputs.md).

There is a real hand asymmetry: the two MH physical-source formulas include final modifier
19873; the two OH formulas have no corresponding hand-specific final factor. Mirroring MH
code and substituting every hand name would add arithmetic absent from this client.

These are context-evaluation formulas. Their registration does not establish invocation on
an ordinary network entity, application-time sampling, ailment qualification, strongest-bleed
selection, aggravation/movement, target mitigation, shared DoT cap or resource deduction.
Those remain distinct contracts. No client execution, attachment, capture or server change
was performed.

| Artifact / method | Identity |
|---|---|
| Executable | `Client/Game.exe`, 3.29.3b x86-64, preferred base `0x140000000` |
| Executable SHA256 | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `data/tables/Stats.json` SHA256 | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| Stat mapping | Native key `k` = JSON row `k - 1`, field `Id`; 23,346 rows |
| Evidence | Original PE bytes, unwind-owned ranges, Capstone; isolated bounded Ghidra 12.1.4 typed export with `-noanalysis` |
| New batch | 20 registrations, 44 function records, 68 ranges, 9,946 instruction records, no undecoded bytes; 20 completed selected decompilations |

## 2. Arithmetic notation and publication boundaries

```text
S(k) = current effective integer input for native key k
P(n) = max(0, float(n)/100)
F(n) = P(add32(n,100))
R(x) = trunc0(x + (x < 0 ? -0.5f : +0.5f))
C(x) = x >= 2147483648.0f ? 2147483647 : R(x)
A    = F(trunc0(S(6172)/100))
q    = S(5995) != 0
```

Equations describe normal finite inputs, with signed32 integer sums/products where the
client uses them. They are algebraic summaries, not permission to reorder the SSE
instructions for a bit-exact port. Operations use binary32, including integer conversion,
intermediate products and signed 0.5 addition. Original instruction order is retained in the
ignored assembly and untouched typed export. The integer divide in `A` is before percent
conversion; `6172` is `active_skill_base_attack_damage_final_+permyriad`.

The nonnegative percent helper is `0x141d1e400`. Signed rounding `R` is
`0x142513810..0x14251383a`; upper-saturating rounding `C` is the distinct leaf
`0x142513840..0x14251387c`. This family's rates inline the same upper-threshold and
signed-rounding pattern. The typed physical rates additionally clamp the returned integer
to nonnegative. The eight final multiplier outputs do not add that last integer clamp.

The stages remain separate: 6154 publishes an integer percentage modifier, each typed
physical rate publishes an integer damage/minute value, and each final output converts that
already rounded integer to float, applies its multiplier, and publishes another rounded
integer. Collapsing the whole chain into one final rounding changes results.

## 3. Four physical-source rates

| Output | Hand / bound | Registration | Callback |
|---:|---|---|---|
| 6001 | MH minimum | `0x1400a6010` | `0x14262d7d0` |
| 6002 | MH maximum | `0x1400a60c0` | `0x14262ef70` |
| 6007 | OH minimum | `0x1400a6170` | `0x142630720` |
| 6008 | OH maximum | `0x1400a6210` | `0x142631e90` |

All four share this algebra after selecting the row-specific inputs below:

```text
if S(5998) != 0 or S(handNoPhysical) != 0: return 0

B = float(S(base)) * A + float(S(added)) * F(S(5761))
L = P(sub32(100,S(1502)))
I = S(1426) + S(2007) + S(11735) + S(handIncrease) + S(6153)
    + (q ? S(5763) : 0) + S(1434) + S(2009)
M = F(S(1427)) * F(S(2008)) * F(S(6367))
    * (q ? F(S(5764)) : 1) * F(S(6154))
    * F(S(boundFinal)) * F(S(1435)) * F(S(2010))
    * (hand == MH ? F(S(19873)) : 1)

typedRate = max(0, C(B * L * F(I) * M * 42))
```

The sums in `I` are signed32, before conversion to the percent factor. Each applicable final
modifier is independently converted to a nonnegative factor. A negative increase is not
discarded before summation. `q` adds both the skill-DoT increase and its separate final
factor; it does not redirect the input to the final hit damage.

| Row | Base / added physical | Hand prohibition | Hand increase | Bound final | Hand final |
|---|---|---|---|---|---|
| MH min | 1563 / 1575 | 1733 | 11736 | 19089 | 19873 |
| MH max | 1564 / 1576 | 1733 | 11736 | 19090 | 19873 |
| OH min | 1593 / 1605 | 1738 | 11737 | 19089 | none |
| OH max | 1594 / 1606 | 1738 | 11737 | 19090 | none |

The two base/added pairs refer to `*_total_*_base_physical_damage` and
`*_total_*_added_physical_damage`, respectively. Base damage receives `A`; added damage
receives 5761 `combined_all_added_damage_+%_final`. Neither path starts from final MH/OH hit
damage. The shared scopes are all damage 1426/1427, all DoT 2007/2008, ailment all-DoT
11735/6367, bleeding 6153/6154, physical 1434/1435 and physical DoT 2009/2010.
19089/19090 are combined minimum/maximum attack-damage final modifiers.

The coefficient is literal **42 damage/minute per unit base**, equivalent to 0.70/second:

| Callback | Literal 42 assignment | Upper saturation return | Final positive-integer selection |
|---|---|---|---|
| MH min | `0x14262ee4c` | `0x14262ef07` | `0x14262ef3d` |
| MH max | `0x1426305fc` | `0x1426306ba` | `0x1426306f0` |
| OH min | `0x142631d80` | `0x142631e2f` | `0x142631e65` |
| OH max | `0x1426334f0` | `0x14263359f` | `0x1426335d5` |

Both MH bodies read 19873 (`combined_main_hand_ailment_all_damage_over_time_+%_final`)
at `0x14262e803` / `0x14262ffb3` and multiply its factor. Both complete OH bodies and
their declared dependency spans omit that factor. OH retains its own **increased** key
11737, so the result is not a general absence of offhand ailment scaling. This is a bounded
arithmetic asymmetry, not a claim that the client or game is defective.

No movement or aggravation multiplier is present in these four inspected formulas. A
42/minute base coefficient does not by itself describe the damage eventually received by
a moving or aggravated target.

## 4. Eight final hand/critical outputs

For each row, the entire output equation is:

```text
output = C(float(S(typedRate)) * F(S(multiplier)))
```

| Output | Hand / bound / critical | Typed rate | Multiplier | Registration | Callback |
|---:|---|---:|---:|---|---|
| 10348 | MH min noncrit | 6001 | 10318 | `0x1400a62b0` | `0x142633600` |
| 10349 | MH max noncrit | 6002 | 10318 | `0x1400a6310` | `0x142633860` |
| 10350 | OH min noncrit | 6007 | 10319 | `0x1400a6370` | `0x142633ac0` |
| 10351 | OH max noncrit | 6008 | 10319 | `0x1400a63d0` | `0x142633d20` |
| 10344 | MH min crit | 6001 | 10308 | `0x1400a6430` | `0x142633f80` |
| 10345 | MH max crit | 6002 | 10308 | `0x1400a6490` | `0x1426341e0` |
| 10346 | OH min crit | 6007 | 10309 | `0x1400a64f0` | `0x142634440` |
| 10347 | OH max crit | 6008 | 10309 | `0x1400a6550` | `0x1426346a0` |

Each callback's two-key read pattern matches its registration dependencies and arithmetic.
For the MH minimum noncritical representative, the typed read is followed by the multiplier
read at `0x1426337b8`, percent conversion at `0x1426337fa`, multiplication at
`0x142633814`, upper threshold at `0x142633818` and rounded conversion at `0x142633843`.
All eight complete bodies were checked, rather than inferred by address spacing or names.

No critical chance is mixed into these values. Ordinary critical outputs use the same
physical-source rate as noncritical outputs and differ through their selected DoT
multiplier. This does not establish whether a particular attack can inflict each variant;
chance and infliction qualification have their own formulas.

## 5. Four hand/critical DoT multiplier builders

| Key | Role | Registration | Callback |
|---:|---|---|---|
| 10318 | MH noncritical | `0x1400beb60` | `0x14276c290` |
| 10308 | MH critical | `0x1400bebc0` | `0x14276c5a0` |
| 10319 | OH noncritical | `0x1400bec30` | `0x14276cb70` |
| 10309 | OH critical | `0x1400bec90` | `0x14276ce80` |

Select `H = 610` and `local = 13844` for MH, or `H = 611` and `local = 13845` for OH.
Select critical-ailment input 10333 for MH or 10334 for OH. The exact branch order is:

```text
noncrit = S(12648) != 0
          ? sub32(S(H),100)
          : add32(S(10338),S(local))

crit = if S(10376) != 0: S(corresponding noncrit key)
       else if S(12648) != 0: sub32(S(H),100)
       else: S(10338) + S(criticalAilment) + S(10440) + S(local)
```

12648 is `virtual_keystone_ailment_crit` (the Perfect Agony input); 10376 is
`ailment_multipliers_never_critical`. These are percentage-point multiplier outputs, with
signed32 sum/subtraction and no local floating-point conversion, saturation or nonnegative
clamp. Conversion to a factor occurs downstream in the eight rate outputs.

In the ordinary branch, 10338 supplies combined all-bleeding DoT multiplier, 10440 is
`critical_bleeding_dot_multiplier_+`, and 13844/13845 are the respective hand-local bleeding
DoT multipliers. The Perfect Agony branch **replaces** that sum with the hand's hit critical
multiplier minus 100; it does not add both. When 10376 is true, the critical builder first
returns its effective noncritical input, which can itself contain the Perfect Agony branch.

The OH alias is positively registered too: 10334
`combined_off_hand_critical_ailment_dot_multiplier_+`, registration `0x1400bea70`, callback
`0x14276c1a0`, returns `S(6175)` (`critical_ailment_dot_multiplier_+`). It shares the exact
callback already registered for MH 10333 and spell 10335. There is no extra offhand
critical-chance weighting in that shared body.

The combined 10338 formula is documented separately. This pass additionally closes its
virtual frenzy contribution, 12012: registration `0x1400de960`, callback `0x1428a97a0`:

```text
S(12012) = mul32(S(12011), S(6))
```

12011 is `bleeding_dot_multiplier_per_frenzy_charge_+`; 6 is current frenzy charges.
The final multiply is `0x1428a9970`. It has no positive-count qualification or clamp.
10338 adds this already combined contribution once; multiplying it by frenzy count again
would square that count accidentally.

## 6. Combined bleeding damage increase and final factor

### 6.1 Increased key 6153

Registration `0x1400a04e0`, callback `0x1425db9e0`:

```text
S(6153) = S(6155) + mul32(S(7138),S(5))
          + (S(2083) != 0 ? S(6191) : 0)
          + (S(20703) != 0 ? S(20883) : 0)
```

6155 is bleeding increased damage, 7138 is bleeding increased damage per endurance charge,
5 is current endurance charges, 2083 is `is_bleeding`, 6191 is increased damaging-ailment
damage while suffering from that ailment, 20703 is `tincture_applied`, and 20883 is bleeding
increased damage with tinctured weapons. The integer multiply is at `0x1425dc016`, followed
by additions through `0x1425dc02e`. There is no positive endurance-count guard, unlike the
separate endurance DoT-multiplier contribution inside 10338. This callback returns the sum
without clamping; the typed-rate consumer later applies `F` to the full increased sum.

### 6.2 Final key 6154

Registration `0x1400a0610`, callback `0x1425dc040`:

```text
projectileAttack = S(2989) != 0 and S(1485) != 0
meleeAttack      = S(2989) != 0 and S(1486) != 0
projectile = projectileAttack ? F(S(6526)) : 1
melee      = meleeAttack      ? F(S(6525)) : 1
blood      = S(9461) == 0     ? F(S(9835)) : 1
monster    = S(1162) == 0     ? F(S(6177)) : 1
crimson    = S(7690) != 0     ? 0.5 : 1

S(6154) = R(100 * F(S(9320)) * F(S(6156)) * monster * crimson
            * projectile * melee * F(S(6529)) * blood * F(S(9911))
            * F(S(13847)) * F(S(14754)) * F(S(19745)) - 100)
```

2989/1485/1486 are skill-is-attack / is-projectile / attack-is-melee. Projectile and melee
factors are independently qualified; this body does not enforce mutual exclusion.
9461 is `in_sand_stance`: the blood-named factor is enabled by **absence of sand stance**,
not a separate positive blood-stance test. 1162 is `is_player_minion`: the monster-named
factor is included when that input is zero, not when an additional monster classification
has been proven. 14754 is read unconditionally despite its name mentioning minions.
Likewise, 19745 is an unconditional effective input here; the tincture qualification of
6153 must not be silently copied onto it.

| Input | Meaning |
|---:|---|
| 9320 | Virtual faster bleed; producer recovered in the input supplement |
| 6156 | Active-skill bleeding final damage |
| 6177 | Monster bleeding final damage, suppressed by player-minion branch |
| 7690 | Virtual Crimson Dance, selecting the separate 0.5 factor |
| 6526 / 6525 | Qualified projectile/melee support bleeding-and-poison final damage |
| 6529 | Chance to Bleed support bleeding final damage; damage factor, not chance |
| 9835 | Active-skill bleeding final damage in blood stance |
| 9911 | Puncture bleeding final damage |
| 13847 | Vampiric Icon bleeding final damage |
| 14754 | Bleeding final damage for minions |
| 19745 | Tincture bleeding final damage |

Each percentage factor is clamped independently before multiplication. The combined
modifier uses signed rounding without an upper-saturation branch; its integer result is
subsequently consumed through `F(S(6154))`. Classification reads 2989/1485/1486 are absent
from its declared dependency span, illustrating why registry dependencies are not an
exhaustive inventory of actual reads. Static arithmetic does not establish how contexts
are invalidated when those classifications change.

## 7. Reconstruction checks and remaining work

The independent verifier includes distinguishing examples with normal finite small inputs
and binary32 rounding at the recovered publication boundaries:

- 100 base physical + 10 added physical, `A = 1.4`, added factor 2 and otherwise neutral
  inputs produce a typed rate of 6,720/minute. Changing only MH final 19873 to +100%
  doubles MH's result; an OH calculation with the same physical inputs stays unchanged.
- A 6,000/minute typed rate, combined multiplier 40, hand-local multiplier 10, shared
  critical multiplier 50 and critical-bleed multiplier 20 yield 9,000 noncrit and
  13,200 crit. A hand critical multiplier of 300 under Perfect Agony replaces both
  ordinary sums with 200 and yields 18,000, without proving noncrit infliction eligibility.
- 25% faster with the separate Crimson Dance factor produces combined modifier -38
  (`R(100 * 1.25 * 0.5 - 100)`). Its consumer applies 0.62, not unrounded 0.625.
- A rounded typed rate of 1/minute with multiplier +50 becomes 2/minute after the
  second rounding. Applying that factor to an unrounded hypothetical typed value 0.51
  and rounding only once would give 1/minute; the publication boundary matters.

Ignored evidence is under `Tools/re/out/client-damage-audit/`:

- `r7-bleeding-family/`: original-byte audit, exact mapping, reconstructed dependency
  spans (162 entries), bounded typed targets, untouched typed exports, console log,
  assembly and separately labelled compact reading aids.
- `r7-bleeding-verify.py`: fresh original-byte and owned-instruction comparison, selected
  arithmetic anchors and literals, all 20 registration argument/dependency dataflows,
  full named-rate-family coverage, per-output input mapping, typed-export identity and
  reconstruction examples. Verification checks the report for local-machine paths.
- `r7-bleeding-prepare.py`, `r7-bleeding-regdeps.py`, `r7-bleeding-decompile.py`,
  `r7-bleeding-compact.py` and `R7BleedingFunctions.java`: retained reproduction tools.

"Complete family" here means the twelve named outgoing bleeding rate Stats and the
explicit builders above in this pinned registry/artifact. It does not claim every upstream
generic damage/physical/DoT multiplier producer has been reconstructed. Generic inputs
such as 10297/10298/10337 remain separate upstream work. No source-instance-to-target
numeric publisher, actual bleed stack arbitration, target movement/aggravation branch,
duration application, critical-event selection or shared target cap is established here.
