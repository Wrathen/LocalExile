# Client outgoing damaging-ailment formulas (3.29.3b)

## 1. Result and scope

The client's native formula registry contains separate outgoing rates for bleeding, ignite and
poison. They start from base and added damage components, retain conversion history where
applicable, apply ailment-specific modifiers, and publish integer **damage per minute**. They do
not take the already modified final hit damage and turn it into an ailment. Critical and
non-critical ailment rates are separate outputs, with separate DoT-multiplier rules. Faster
ailments enter an upstream combined rate modifier; this pass recovers that numerical connection.

This is a bounded formula study: 26 registrations, their 26 callbacks, and five scalar/cache
helpers. It complements [offence estimates](client-offence-estimates.md),
[stat evaluation](client-stat-evaluation.md), and the broader
[damage investigation](client-damage-calculation.md). A native formula is a usable arithmetic
reference. Registration does not establish that an ordinary network entity invokes that formula
locally; the provider/transaction boundary remains as documented in the stat-evaluation report.

| Artifact | Identity |
|---|---|
| Executable | `Client/Game.exe`, 3.29.3b, PE x86-64; preferred base `0x140000000` |
| Executable SHA256 | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `data/tables/Stats.json` SHA256 | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| Stat mapping | Native key `k` = JSON row `k - 1`, `Id`; 23,346 rows |
| Method | Original PE bytes, chained unwind ranges, Capstone 5.0.3 / pefile 2024.8.26; bounded Ghidra 12.1.4 import, `-noanalysis` |
| Execution | Static/offline only; no target launch, attach, hooks, or traffic acquisition |

Addresses below are preferred VAs. Names such as `P`, `F`, `C`, and history sets are analyst
notation, not recovered source names. Ignored evidence lives under
`Tools/re/out/client-damage-audit/`: `r5-ailment-outgoing-formulas` (28 function records / 45 ranges /
12,021 instructions), `r5-ailment-outgoing-expansion` (24 / 30 / 4,656),
`r5-ailment-outgoing-helpers` (four helpers), and `r5-ailment-outgoing-saturating-v2` (one leaf).
The first untyped decompilation is retained. All 26 selected callbacks completed in the second,
typed export. Its signatures include both distinct rounding helpers; without a float signature
for `0x142513840`, decompilation can lose the ignite SSE expression before rounding.

## 2. Native arithmetic and registration

`S(k)` reads key `k` from the native evaluation context. Cached reads and record fallback are
described in the stat-evaluation report; a stat read here is not recursive formula dispatch.

```text
P(n) = max(0, float(n) / 100)
F(n) = P(add32(n, 100))
R(x) = trunc0(x + (x < 0 ? -0.5f : +0.5f))
C(x) = x >= 2147483648.0f ? 2147483647 : R(x)
A    = F(trunc0(S(6172) / 100))
```

`6172` is `active_skill_base_attack_damage_final_+permyriad`: the audited MH paths perform signed
integer division by 100 before conversion to a percent factor. `A` therefore preserves that
truncation rather than replacing it with an ideal real-valued permyriad division.

`0x141d1e400` implements `P`; `0x142513810..0x14251383a` implements `R` without upper saturation.
The distinct leaf `0x142513840..0x14251387c` implements `C`: comparison at `0x142513843`, upper
return at `0x14251384c`, and the two signed rounding returns at `0x142513866` / `0x142513877`.
`C` is not a symmetric signed clamp. These equations describe normal finite inputs; instruction
order, float32 precision, integer overflow, and invalid float-to-int behavior matter for a
bit-exact port.

For finite values, integer-to-float32 conversion is exact through magnitude `2^24`; above that,
adjacent integers need not remain distinct. Positive float spacing immediately below `2^31`
is 128 damage/minute (about 2.1333 DPS), so these formulas are not exact integer arithmetic
followed by one final rounding. Intermediate products and the addition of `0.5f` round as
float32 too. At lower rates the published integer minute unit represents increments of 1/60 DPS;
that unit alone does not guarantee that every intermediate calculation has that precision.

Each registration calls `0x142513630` with output key in `edx`, callback in `r8`, and a dependency
span in `r9`. A static straight-line dataflow pass reconstructs the stack/vector dependency span
from the original PE and asserts the key and callback. The span declares dependencies; it is not
an exhaustive list of every input a callback reads. For example, the combined bleeding/poison
callbacks also read fixed skill classification keys `2989` (`skill_is_attack`), `1485`
(`is_projectile`) and `1486` (`attack_is_melee`) to qualify support effects.

## 3. Physical bleeding and physical-source poison

Two representative MH formulas can be written completely without flattening conversion paths.
Both read `1563` (`main_hand_total_minimum_base_physical_damage`) and `1575`
(`main_hand_total_minimum_added_physical_damage`), **not** final MH physical-hit output `279`.

```text
B = S(1563) * A + S(1575) * F(S(5761))
L = P(100 - S(1502))
```

Here `5761` is `combined_all_added_damage_+%_final`, and `1502` is
`physical_damage_%_lost_to_conversion`. Base and added components receive different factors.

### 3.1 Bleeding key 6001

Registration `0x1400a6010`, callback `0x14262d7d0`:

```text
if S(5998) != 0 or S(1733) != 0: return 0
q = S(5995) != 0
I = S(1426) + S(2007) + S(11735) + S(11736)
  + S(6153) + S(1434) + S(2009) + (q ? S(5763) : 0)
M = F(S(1427)) * F(S(2008)) * F(S(6367)) * F(S(19873))
  * F(S(6154)) * F(S(19089)) * F(S(1435)) * F(S(2010))
  * (q ? F(S(5764)) : 1)
S(6001) = max(0, C(B * L * F(I) * M * 42))
```

The scopes are all damage (`1426/1427`), all DoT (`2007/2008`), ailment all-DoT
(`11735/6367`), MH ailment all-DoT (`11736/19873`), bleeding (`6153/6154`), physical
(`1434/1435`), physical DoT (`2009/2010`), minimum attack final (`19089`), and optionally
skill DoT (`5763/5764`) when `5995` (`bleed_is_skill_dot`) is set. `5998` is
`deal_no_bleed_damage_over_time`; `1733` is `deal_no_main_hand_physical_damage`.

The factor **42 damage/minute per unit base**, equivalent to 0.70/second, is literal
`0x42280000` at `0x14262ee4c`. The inline upper saturation is at `0x14262ef07`; the final
nonnegative clamp is separate. No target movement multiplier is recovered in this callback.

### 3.2 Poison key 6073

Registration `0x1400aa550`, callback `0x1426a0ed0`:

```text
if S(6000) != 0 or S(1733) != 0 or S(6115) != 0: return 0
q = S(5997) != 0
I = S(1426) + S(2007) + S(11735) + S(11736)
  + S(6113) + S(1448) + S(2019) + S(1434) + (q ? S(5763) : 0)
M = F(S(1427)) * F(S(2008)) * F(S(6367)) * F(S(19873))
  * F(S(6179)) * F(S(19089)) * F(S(1449)) * F(S(2020)) * F(S(1435))
  * (q ? F(S(5764)) : 1)
S(6073) = max(0, R(B * L * F(I) * M * 18))
```

`6000` is `deal_no_poison_damage_over_time`; `6115` is
`virtual_physical_damage_cannot_poison`; `5997` is `poison_is_skill_dot`. Poison uses its own scope
(`6113/6179`), chaos (`1448/1449`), chaos DoT (`2019/2020`), **and source physical history**
(`1434/1435`). It does not use the physical-DoT pair in place of chaos DoT.

The factor **18/minute**, equivalent to 0.30/second, is `0x41900000` at `0x1426a2527`.
This is the coefficient in this executable; older game-version descriptions are not a substitute.
Unlike the bleed and ignite paths above, this callback rounds without the upper-saturating branch.

## 4. Ignite and converted poison preserve damage history

| Output key | Native registration | Callback | Selected input |
|---|---|---|---|
| 6016 | `0x1400a7ad0` | `0x142659f80` | Minimum MH fire eligible for ignite |
| 6036 | `0x1400a6750` | `0x142637960` | Minimum spell fire eligible for ignite |
| 6081 | `0x1400aac50` | `0x1426adc10` | Minimum MH chaos eligible for poison |

The ignite callbacks gate on `5999` (`deal_no_ignite_damage_over_time`), the selected hand/spell
fire prohibition (`1734` / `1724`), and `6010` (`virtual_fire_damage_cannot_ignite`). They read separate
base/added components: MH fire `1565/1579`, or spell fire `1535/1549`, plus the physical, cold
and lightning components feeding the conversion tree. MH paths also apply the attack base factor
and MH/minimum-attack ailment scopes; the spell path does not add those MH/attack scopes.

The fire expression sums eight histories before a final `C` and nonnegative clamp:

```text
fire
physical -> fire
physical -> cold -> fire
physical -> lightning -> fire
physical -> lightning -> cold -> fire
lightning -> fire
lightning -> cold -> fire
cold -> fire
```

Transfer coefficients come from `1513/1520/1517` (physical/cold/lightning as fire),
`1514/1515/1518` (physical as cold/lightning and lightning as cold); retained fire is
`P(100-S(1503))`. Each term preserves the relevant source-type increased/final contributions.
Shared all/DoT/ailment, elemental, fire and ignite contributions are not represented by applying
the finished hit output a second time. Skill-DoT scopes are conditional on `5996`
(`ignite_is_skill_dot`). The exact original term order is retained in the typed export.

Ignite's final rate coefficient is **54/minute** (0.90/second): MH passes 5400 to `P` at
`0x14265bb28`, then calls `C` at `0x14265bb4f`; the spell path calls `C` at `0x1426394cd`.
This closes the coefficient and component/history model, while the two complete physical
equations above provide a smaller implementation reference than transcribing the eight-term SSE
polynomial as an apparently bit-exact new formula.

MH chaos-source poison `6081` similarly reads all five base/added components, chaos transfer
keys `1516/1522/1521/1519`, intermediate elemental conversions, and their modifier histories.
Its coefficient is 18/minute and its final helper is non-saturating `R`. Thus “physical poison”
and “chaos poison” are contribution paths to an ailment rate, not aliases for final physical and
chaos hit results.

## 5. Faster/slower are upstream rate factors

| Output | Registration / callback | Recovered rate factors |
|---|---|---|
| 6154 combined bleeding final | `0x1400a0610` / `0x1425dc040` | `F(S(9320))`, virtual faster bleed |
| 6158 combined ignite final | `0x1400a0750` / `0x1425dd4f0` | `F(S(6203)) / (1 + float(S(6577))/100)` |
| 6179 combined poison final | `0x1400a09e0` / `0x1425df060` | `F(S(9319))`, virtual faster poison |

Each callback composes qualified final multipliers as factors and publishes
`R(100 * product - 100)`. The downstream typed rate then applies `F` to that integer combined
modifier. Preserve this intermediate rounding; algebraically multiplying every original factor
directly into the ailment rate would omit it.

The ignite denominator is a reciprocal of `1 + slower/100`, not a subtraction of slower from
faster. Its division is at `0x1425de0f3`. A 25% faster, 50% slower numerical input therefore
contributes `1.25/1.50` before other factors and intermediate rounding. The callback additionally
uses `23308` (`keystone_fire_purist`) to select 0.75 instead of 1 at `0x1425de08d..0x1425de0db`.
Bleeding selects a separate 0.5 factor for `7690` (`virtual_keystone_crimson_dance`); support
modifiers distinguish projectile attacks and melee attacks. Minion qualification controls
monster/minion-specific factors. These qualifications are branches in the arithmetic, not
uniform percentage aggregation.

This establishes how the **current input values** influence outgoing rate. It does not establish
when an ailment instance samples those values, whether changing equipment updates an existing
ailment, or how faster/slower changes its expiration clock. Duration and instance sampling remain
separate follow-ups; no lifetime model is inferred from a rate callback alone.

## 6. Branch-specific ailment DoT multipliers

Aggregate arithmetic differs by ailment. The selected ignite callbacks convert each of five
typed rates to float32, sum them, apply their multiplier and upper-saturate/round with `C`.
The poison totals instead sum five inputs with wrapping 32-bit integer additions, convert that
one signed sum to float32, apply their multiplier and use non-saturating `R`, without a final
nonnegative clamp. Bleeding totals use their single physical typed rate. None of these totals
chooses a random critical result or mixes critical chance into its output.

| Rate output | Callback | Typed contributions | Multiplier key |
|---|---|---|---|
| 10348 MH noncrit bleed | `0x142633600` | 6001 | 10318 |
| 6058 MH noncrit ignite | `0x14267b2f0` | 6014, 6016, 6018, 6020, 6022 | 10320 |
| 10352 MH crit ignite | `0x14267e370` | same five | 10310 |
| 6062 spell noncrit ignite | `0x142679ab0` | 6034, 6036, 6038, 6040, 6042 | 10322 |
| 10356 spell crit ignite | `0x14267cb30` | same five | 10312 |
| 6122 MH noncrit poison | `0x1426c4440` | 6073, 6075, 6077, 6079, 6081 | 10324 |
| 10360 MH crit poison | `0x1426c7310` | same five | 10314 |
| 6126 spell noncrit poison | `0x1426c2cc0` | 6093, 6095, 6097, 6099, 6101 | 10326 |
| 10364 spell crit poison | `0x1426c5b90` | same five | 10316 |

For example `S(6058) = C((float(S(6014)) + ... + float(S(6022))) * F(S(10320)))`.
That floating-point sum is specific to ignite. For MH poison `6122`, the original additions at
`0x1426c48bb..0x1426c48c2` precede the single signed conversion at
`0x1426c49cb..0x1426c49cf` and inline non-saturating rounding at `0x1426c49f6`.
The expanded audit confirms this integer-sum/non-saturating contract for all sixteen poison
totals (`6122..6129`, `10360..10367`); large inputs can therefore differ from the ignite path.
The original selected formulas in sections 3–4 are not a claim of complete source-type coverage.

The nine multiplier callbacks are audited too:

| Multiplier | Callback | Ordinary noncrit / critical sum |
|---|---|---|
| 10318 MH bleed noncrit | `0x14276c290` | `S(10338) + S(13844)` |
| 10320 MH ignite noncrit | `0x14276d450` | `S(10339)` |
| 10310 MH ignite crit | `0x14276d640` | `S(10339) + S(10333) + S(10441)` |
| 10322 spell ignite noncrit | `0x14276e230` | `S(10339)` |
| 10312 spell ignite crit | `0x14276e420` | `S(10339) + S(10335) + S(10441)` |
| 10324 MH poison noncrit | `0x14276f020` | `S(10340) + S(14121)` |
| 10314 MH poison crit | `0x14276f330` | `S(10340) + S(10333) + S(10442) + S(14121)` |
| 10326 spell poison noncrit | `0x1427701e0` | `S(10340)` |
| 10316 spell poison crit | `0x1427703d0` | `S(10340) + S(10335) + S(10442)` |

For critical outputs, first test `10376` (`ailment_multipliers_never_critical`): if true,
return the corresponding noncrit multiplier. Otherwise test `12648`
(`virtual_keystone_ailment_crit`): if true, return `S(610)-100` for MH or `S(612)-100` for
spell. The noncrit callbacks also use that keystone branch directly. Only when the branch is
false do they use the ordinary sums above. Thus this native Perfect Agony input **replaces** the
ordinary multiplier sum; it does not add the hit critical multiplier on top of every DoT scope.
These are rate outputs; a noncrit rate callback is not proof that the skill is eligible to
inflict a noncritical ailment under that keystone. Infliction qualification is a separate path.

`10338/10339/10340` are the combined all-DoT multipliers for bleed/ignite/poison;
`10333/10335` are combined MH/spell critical ailment multipliers;
`10441/10442` are critical ignite/poison multipliers; `13844` is MH bleeding and `14121` is
MH local poison DoT multiplier. [Selected upstream combined multipliers and virtual faster inputs](client-ailment-multiplier-inputs.md)
are now recovered separately; generic/typed multiplier production remains its own contract.

## 7. Consequences and remaining work

The recovered boundaries support independent outgoing hit and ailment calculations sharing
base/added components and conversion history, with ailment-specific modifier scopes and
critical branches. Minute units are a rate representation: a finite duration or a
“total damage over lifetime” value is not required to calculate this rate.

Several callbacks clamp an individual outgoing per-minute value at signed-int maximum. That
is useful evidence about representation and arithmetic. It does **not** prove that these
callbacks implement the shared target-side DoT cap: they contain no target-wide aggregation,
resistance evaluation, stack arbitration, or resource deduction. The recovered poison rate
paths do not upper-saturate. Neither a shared cap nor its exact placement should be inferred from the
minute stat names alone.

Follow-ups remain bounded and explicit:

- Reuse the [complete named bleeding family](client-bleeding-output-family.md),
  [56 named ignite outputs](client-ignite-output-family.md),
  [72 named poison outputs](client-poison-output-family.md),
  [typed eligibility and target gates](client-ailment-eligibility.md), and
  [ailment chance inputs](client-ailment-chance-inputs.md); preserve their separate
  source/category qualifiers, aggregate arithmetic and intermediate publication boundaries.
- Reuse the [combined ailment multipliers and virtual faster producers](client-ailment-multiplier-inputs.md);
  reuse [common/typed/ailment/non-ailment multiplier production](client-dot-multiplier-inputs.md).
- Trace lifetime, instance snapshot/update policy, duration clocks, and stack selection separately.
- Connect outgoing rates to target mitigation, effective-rate aggregation/cap, and resource pools.

The ignored verifier `r5-ailment-outgoing-verify.py` checks original file/table identities,
audited range hashes and instruction bytes, dependency-span registration dataflow, selected
arithmetic anchors and literals, and both typed decompilation batches. Public conclusions are
the native arithmetic above; unsupported lifetime or target-resolution behavior remains open.
