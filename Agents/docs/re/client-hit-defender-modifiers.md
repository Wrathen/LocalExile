# Client hit defender modifier composition (3.29.3b)

## 1. Result and boundary

The native registry contains a shared final hit-damage-taken modifier, five typed final
hit-damage-taken modifiers, an additive elemental hit modifier, several resistance-ignore
predicates, and a defender-side increased enemy-critical-chance output. This pass recovers their
numerical composition and conditions. None of these callbacks accepts a concrete incoming hit
amount or target resource pool. They compose **stats**; they are not a complete damage resolver.

This complements [stat evaluation](client-stat-evaluation.md),
[offence estimates](client-offence-estimates.md),
[critical formulas](client-critical-formulas.md), and
[DoT routing](client-dot-routing.md). The native/network provider distinction established there
continues to apply. In particular, a registered native formula does not establish that receiving
an ordinary network stat causes local reevaluation.
The separate [defender-context predicate](client-defender-context-presence.md) detects 37 conditional inputs;
its Boolean composition does not establish their numerical application to a hit.

| Input | Identity |
|---|---|
| Executable | `Client/Game.exe`, PE x86-64, 3.29.3b; preferred base `0x140000000` |
| Executable SHA256 | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `data/tables/Stats.json` SHA256 | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| Mapping | Native key `k` = zero-based JSON row `k - 1`, field `Id` |
| Method | Original PE / chained unwind ranges; Capstone 5.0.3, pefile 2024.8.26; selected Ghidra 12.1.4 export, `-noanalysis` |
| Execution | Static and offline; no client launch, attach, hooks, or new captures |

The ignored canonical batch is `Tools/re/out/client-damage-audit/r6-hit-defender-modifiers`:
26 function records, 34 ranges, 3,164 decoded instructions, zero undecoded bytes. All 13 selected
callbacks completed in `typed-decompilations.jsonl`. Registration `mapping.json` and the original
stack/vector `dependencies.json` are retained alongside the original assembly. Decompilation
uses supplied signatures for stat/cache predicates and scalar helpers; original instructions
remain authoritative.

## 2. Registered outputs and primitive inputs

| Key / output | Registration | Callback |
|---|---|---|
| 10759 `damage_taken_when_hit_+%_final` | `0x1400c80b0` | `0x1427d0140` |
| 10722 `physical_damage_taken_when_hit_+%_final` | `0x1400c8370` | `0x1427d2480` |
| 10728 `fire_damage_taken_when_hit_+%_final` | `0x1400c86a0` | `0x1427d54d0` |
| 10724 `cold_damage_taken_when_hit_+%_final` | `0x1400c8890` | `0x1427d6c60` |
| 10726 `lightning_damage_taken_when_hit_+%_final` | `0x1400c8a70` | `0x1427d7eb0` |
| 10730 `chaos_damage_taken_when_hit_+%_final` | `0x1400c8c50` | `0x1427d8e30` |
| 10733 `virtual_elemental_damage_taken_when_hit_+%` | `0x1400c84b0` | `0x1427d3b90` |
| 12189 `virtual_hits_ignore_enemy_elemental_resistances` | `0x1400d8fd0` | `0x14287ae20` |
| 7699 `virtual_hits_ignore_enemy_lightning_resistance` | `0x1400d9030` | `0x14287ae70` |
| 7698 `virtual_hits_ignore_enemy_cold_resistance` | `0x1400d9090` | `0x14287b250` |
| 7697 `virtual_hits_ignore_enemy_fire_resistance` | `0x1400d90f0` | `0x14287b630` |
| 9052 `virtual_hits_ignore_enemy_chaos_resistance` | `0x1400d9150` | `0x14287b840` |
| 9185 `enemy_critical_strike_chance_+%_against_self` | `0x1400db0f0` | `0x14288e640` |

Each passes its output key, callback and declared dependency span to `0x142513630`.
The recovered 2,539-entry formula-registration inventory contains no registration for:

| Key | Input stat | Interpretation in this pass |
|---|---|---|
| 10757 | `fortify_damage_taken_when_hit_+%_final` | Primitive input, positively read by 10759 |
| 2967 | `damage_taken_+%_from_hits` | Primitive input; not a missing formula callback |
| 9187 | `enemy_additional_critical_strike_chance_against_self` | Primitive flat/additional input, distinct from increased chance 9185 |
| 12561 | `enemy_additional_critical_strike_multiplier_against_self` | Primitive additional multiplier input, distinct from probability |

“Primitive” means no derived callback in this recovered inventory. Values can still be supplied
by a contribution source, native parent, or network override. It does not mean the mechanic is
absent or that this pass has recovered its production/application path. The last three inputs
are not consumed by the 13 selected callbacks; their concrete per-event consumers remain open.

## 3. Arithmetic conventions

`S(k)` is the context's signed integer stat lookup, including its cache and record-provider
fallback. `B(k)` is the nonzero predicate `0x1425135a0`. Neither denotes recursive formula
dispatch. Define:

```text
F(n) = max(0, float32(add32(n, 100)) / 100.0f)
R(x) = trunc0(float32(x + (x < 0 ? -0.5f : +0.5f)))
```

Final-modifier outputs below are integer **percentage deltas**, not damage values: `-20` means
a factor of 0.8, zero means 1, and +50 means 1.5. Each input factor is clamped at zero before
multiplication. The callbacks publish a signed rounded `100 * product - 100`; they do not use
the upper-saturating rate helper from the outgoing ailment formulas.

The formulas below describe ordinary finite inputs. Preserve signed 32-bit additions/products,
float32 operation order and the final `cvttss2si` for an exact port. A double-precision product
followed by a language's default rounding is not guaranteed to match. Integer conversion is
exact only through magnitude `2^24`; products and the half adjustment round as float32 too.

## 4. Shared final hit modifier 10759

The first branch reads `10888` (`should_use_alternate_fortify`):

```text
perFortification = B(10888) ? 1 : F(mul32(S(15041), S(15044)))
S(10759) = R(F(S(10757)) * 100
             * F(S(16784)) * perFortification
             * F(S(20758)) * F(S(22190)) - 100)
```

| Input | Meaning |
|---|---|
| 10757 | `fortify_damage_taken_when_hit_+%_final` |
| 15041 | `damage_taken_when_hit_+%_final_per_fortification` |
| 15044 | `current_fortification` |
| 16784 | `trickster_hit_damage_taken_+%_final` |
| 20758 | `support_storm_barrier_damage_taken_when_hit_+%_final` |
| 22190 | `alternate_ascendancy_damage_taken_when_hit_+%_final` |

`10888` bypasses only the **per-fortification factor**. It does not bypass the separate 10757
factor or all other hit modifiers. The integer multiplication of per-point amount and current
count precedes the percent conversion (`0x1427d0369`). The final multiplication/subtraction
and rounding are at `0x1427d0826..0x1427d087b`.

For example, with `15041=-1`, `15044=20`, all other deltas zero and alternate false, this
publishes -20. Setting alternate true replaces that count factor with 1. This is a numerical
example; it does not assert how those inputs are granted by a particular game effect.

The declared dependency span includes 15041 and 10888 but omits fixed count input 15044.
Original callback reads therefore matter in addition to the registry's dependency list.

## 5. Typed final hit modifiers

Each typed output includes the shared 10759 **once**, together with its already calculated
typed non-hit-specific final modifier and any additional factors in this table. Multiplication
order below follows the native tail: typed factor, 100, shared factor, then listed extras.

```text
S(output) = R(F(S(typedFinal)) * 100 * F(S(10759)) * extraFactors - 100)
```

| Output | Typed final input | Extra factors in native order | Final truncate |
|---|---|---|---|
| 10722 physical | 10721 `physical_damage_taken_+%_final` | `F(S(3119)) * F(S(20790))` | `0x1427d2970` |
| 10728 fire | 10727 `fire_damage_taken_+%_final` | `F(S(3120)) * F(S(15647)) * F(S(15650))` | `0x1427d5ae1` |
| 10724 cold | 10723 `cold_damage_taken_+%_final` | `F(S(15648)) * F(S(15649))` | `0x1427d712e` |
| 10726 lightning | 10725 `lightning_damage_taken_+%_final` | 1 | `0x1427d8131` |
| 10730 chaos | 10729 `chaos_damage_taken_+%_final` | 1 | `0x1427d90b1` |

`3119/3120` are new Arctic Armour's physical/fire hit final inputs; `20790` is physical hit
final from Blood Stance. `15647/15650` are Black Star sunlight/moonlight fire final inputs,
and `15648/15649` are their cold counterparts. These callbacks read those effective stat values;
they do not themselves test stationary status, stance or sunlight/moonlight state. Qualification
can occur when the inputs are produced. In particular, both Black Star factors are multiplied
if both supplied values are nonzero; this callback does not choose one through an exclusive branch.

Intermediate publication is observable arithmetic structure: 10759 is rounded to an integer
modifier before the typed callback applies `F` and rounds again. Combining all original effects
into one unrounded product would omit that boundary. Applying 10759 again after a typed output
would double-count it.

## 6. Additive elemental hit modifier 10733

This output is deliberately not a `+%_final`:

```text
S(10733) = add32(S(12090), mul32(S(7404), S(5)))
```

`12090` is `base_elemental_damage_taken_when_hit_+%`, `7404` is
`elemental_damage_taken_from_hits_+%_per_endurance_charge`, and `5` is
`current_endurance_charges`. The tail at `0x1427d3e3b..0x1427d3e3e` is integer multiplication
and addition: no percent-to-factor conversion, clamp, or floating rounding here. For example,
base +10, per-charge -4 and three charges produces -2, an additive percentage delta.

Neither 10733 nor primitive 2967 appears as an input to the five selected final callbacks.
The additive and final categories therefore should not be collapsed into one formula merely
because all refer to hit damage taken. Their eventual joint application to an incoming amount
is a separate resolver contract.

## 7. Resistance-ignore predicates: context flags, not a resistance mutation

These callbacks return integer 0 or 1. They read context inputs and contain no incoming damage
amount, defender resistance lookup, penetration application, or resource deduction.

```text
S(12189) = B(14092) || (B(12190) && !B(3288))
S(7697)  = B(7696) && B(1843)
S(7699)  = B(22193) && S(22196) >= 1 && S(22196) == S(22195)
S(7698)  = B(22194) && S(22197) >= 1 && S(22197) == S(22195)
S(9052)  = B(14092)
        || (S(7470) == S(4749) && B(9051))
        || (S(7174) == S(4749) && B(12997))
```

| Input family | Meaning |
|---|---|
| 14092 | `hits_ignore_all_enemy_monster_resistances` |
| 12190 / 3288 | Elemental ignore while ES is present / `currently_has_no_energy_shield` |
| 7696 / 1843 | Fire ignore while ignited / `is_ignited` |
| 22193 / 22196 / 22195 | Lightning ignore with all rings synaptic / synaptic ring count / equipped ring count |
| 22194 / 22197 / 22195 | Cold ignore with all rings cryonic / cryonic ring count / equipped ring count |
| 9051 / 7470 / 4749 | Chaos ignore with all equipped items Shaper / Shaper count / equipped count |
| 12997 / 7174 / 4749 | Chaos ignore with all equipped items Elder / Elder count / equipped count |

The ring callbacks explicitly require at least one matching ring; equality of two zero counts
is insufficient. The chaos callback has no corresponding positive-count test: equal zero counts
and the respective enabled input satisfy its arithmetic predicate. Do not silently insert an
extra item-count guard into a port of this function.

The elemental flag and per-element flags are separate outputs: for example, 7697 does not OR
12189 into itself. Their actual combination at a resistance calculation remains a separate
consumer question. Likewise, the word **monster** in 14092 is a qualification requirement to
preserve when finding that consumer; these callbacks have no defender parameter to enforce it.
They do not justify setting the defender's resistance stat to zero globally, or skipping resistance
for every type/target. A critical-hit-specific ignore such as Inevitable Judgement requires its
own event-context branch; it is not recovered by assuming these predicates inspect critical status.

## 8. Enemy increased critical chance against this defender: 9185

```text
S(9185) = add32(S(9186), min_signed(S(10452), 500),
               mul32(S(9199), 20), B(9190) ? 50 : 0)
```

`9186` is base increased enemy critical chance against self; `10452` is the cold-projectile-mine
component; `9199` is explicitly named `enemy_critical_strike_chance_+%_against_self_20_times_value`;
`9190` enables the extra +50%. The tail at `0x14288e879..0x14288e899` caps **only 10452** at 500,
then adds the other terms. There is no zero floor or final aggregate 500 cap. With base +100,
mine +700, twenty-times input 3 and the +50 predicate true, the result is +710%.

This output is increased critical chance in percentage units, not a permyriad critical probability.
Primitive 9187 adds a different flat-chance category; 12561 changes enemy critical damage
multiplier. Neither is combined here. How attacker chance, defender increased/flat inputs,
always/cannot-crit, confirmation and critical damage meet in one concrete hit remains open.

## 9. Reusable contracts and remaining work

The positive result is a reusable stat-composition layer: shared and typed final modifiers,
additive elemental hit modifiers, qualified resistance-ignore flags, and defender critical
inputs remain distinct. Existing input values can be composed without duplicating already
included modifiers or inventing formulas for primitive stats.

This pass does not recover damage-taken-as ordering, actual per-hit armour, resistance/penetration,
block/suppression, critical confirmation, resource routing, or life/ES deduction. No reference-hit
armour estimate is used here. Those require a concrete hit amount and event/defender context;
the callbacks above establish neither by themselves.

The ignored `r6-hit-defender-modifiers-verify.py` and its JSON summary recheck original artifact
identity, function ownership/range hashes, complete instruction sequences, registration dependency
dataflow, arithmetic anchors, primitive classification in the recovered inventory, typed export
coverage and sidecar hashes. The boundary is numerical composition, not a claim of a completed
incoming-hit pipeline.
