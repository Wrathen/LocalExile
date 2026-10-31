# Physical and chaos incoming/self-rate composition (3.29.3b)

## Result and scope

Native outputs **8179**, `physical_damage_taken_per_minute_unaffected_by_level_scaling`,
and **8183**, `chaos_damage_taken_per_minute_unaffected_by_level_scaling`,
combine concrete numerical inputs before the separately recovered
[typed incoming routing](client-dot-routing.md).
Together with [fire output 8180](client-fire-self-rate.md), these equations
recover the selected incoming/self-rate composition, not source-instance publication.
Neither callback creates an ailment, selects overlapping instances or commits a frame's resource loss.

The pinned `Client/Game.exe` SHA256 is
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`;
preferred image base is `0x140000000`.
`Stats.json` SHA256 is
`91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da`.
Native keys are one-based JSON row indices.
All evidence and checks are static/offline; no target execution or attachment occurred.

| Output | Registrar | Callback | Declared/read input set |
|---|---|---|---|
| 8179 physical | `0x1400ac510` | `0x1426cc330` | 11 keys |
| 8183 chaos | `0x1400ac930` | `0x1426cfa50` | 13 keys |

The original registrar vectors match the union of observed reads for each callback.
Individual branches read only the inputs they need.
As elsewhere, native formula evaluation and explicit network values retain
[different activation and publication contracts](client-stat-evaluation.md).

## 1. Inputs and arithmetic

`S[k]` is the signed32 value obtained from this native computation context.
`W(x)` wraps a signed32 sum/product, and `f(x)` converts or rounds to binary32.
Every floating operation below rounds separately; reassociation is not implied.
Define `P(x) = max(0, f(f(W(x)) / 100.0f))` and `I(x) = P(W(x + 100))`.

`C(x)` returns `INT_MAX` if binary32 `x >= 2147483648`.
Otherwise it adds binary32 signed 0.5 and executes `CVTTSS2SI r32`.
Negative out-of-range conversion returns `INT_MIN`; there is no final nonnegative clamp.
The percentage clamps are earlier, separate operations.

| Keys | Verified `Stats.Id` / role |
|---|---|
| 247 / 249 | `maximum_life` / `maximum_energy_shield` |
| 7951 | `bleeding_damage_on_self_converted_to_chaos` |
| 7955 | `bleeding_damage_taken_per_minute` |
| 13579 | `rupture_debuff_bleeding_damage_taken_+%_final` |
| 5338 | `virtual_energy_shield_protects_mana` |
| 2614 / 3113 | `base_physical_damage_%_of_maximum_life_taken_per_minute` / `base_physical_damage_%_of_maximum_energy_shield_taken_per_minute` |
| 7471 / 7455 | `virtual_physical_damage_taken_per_minute_per_siphoning_charge` / `current_siphoning_charges` |
| 13573 | `base_physical_damage_taken_per_minute_unaffected_by_level_scaling` |
| 13492 / 8215 | `take_chaos_damage_from_ignite_instead` / `base_ignite_damage_taken_per_minute` |
| 3918 / 15830 | `using_flask` / `chaos_damage_per_minute_while_affected_by_flask` |
| 455 | `base_chaos_damage_%_of_maximum_life_taken_per_minute` |
| 1358 / 6 | `base_chaos_damage_%_of_maximum_life_taken_per_minute_per_frenzy_charge` / `current_frenzy_charges` |
| 8217 / 8216 | `base_self_chaos_damage_taken_per_minute` / `base_poison_damage_taken_per_minute` |

Input 7955 is **bleeding**, not Blood Rage.
The Ids identify numerical roles, not current-game reachability of every synthetic combination.

## 2. Complete physical composition

```text
bleed = S[7951] != 0 ? 0 : f(f(S[7955]) * I(S[13579]))
es = S[5338] != 0 ? 0 : f(f(S[249]) * P(S[3113]))

n = f(f(S[247]) * P(S[2614]))
n = f(n + bleed)
n = f(n + f(W(S[7471] * S[7455])))
n = f(n + es)
result8179 = W(C(n) + S[13573])
```

The raw 13573 contribution is added **after** floating-point rounding and upper saturation.
For example, Life 100 with 2614 = 1 gives 1; adding 13573 = `INT_MAX`
then wraps to `INT_MIN`. A lone 13573 = -1 returns -1.
These are observed finite-width semantics, not a claim about reachable gameplay damage.

The ES-protects-Mana input suppresses this particular ES-based physical term.
It does not set maximum ES to zero or describe all later resource routing.
The siphoning coefficient times charge count wraps as signed32 before conversion to binary32.

## 3. Complete chaos composition

```text
bleed = S[7951] != 0 ? f(f(S[7955]) * I(S[13579])) : 0
ignite = S[13492] != 0 ? S[8215] : 0
flask = S[3918] != 0 ? S[15830] : 0

n = f(f(S[247]) * P(W(S[1358] * S[6])))
n = f(n + f(f(S[247]) * P(S[455])))
n = f(n + f(S[8217]))
n = f(n + f(flask))
n = f(n + bleed)
n = f(n + f(ignite))
n = f(n + f(S[8216]))
result8183 = C(n)
```

Bleeding enters one of these physical/chaos branches according to 7951.
Its Rupture factor is applied here, before downstream typed mitigation.
An input bleeding rate of 100 with Rupture 25 yields 125 in the selected channel.
This does not establish the publisher of 7955 or the selection of moving/aggravated bleeding instances.

The 13492 gate complements the fire callback's ignite branch:
nonzero sends the already supplied 8215 rate into chaos; zero retains it in fire.
The two callbacks do not themselves demonstrate a new ignite instance or a change of its outgoing damage type.
Poison input 8216 is added as a supplied aggregate rate, without an instance loop here.

With Life 1000, per-frenzy percentage 10, three frenzy charges, flat percentage 20,
self rate 20 and poison rate 30, the chaos result is 550.
The percentage/charge product wraps before its nonnegative clamp:
`INT_MAX * 2` becomes -2, producing zero for that percentage term.
Likewise Rupture `INT_MAX + 100` wraps before clamping.

## 4. Original-instruction anchors and verification

| Original span | Bound operation |
|---|---|
| `0x1426cc44b..0x1426cc665` | Physical bleeding gate and Rupture factor |
| `0x1426cc741..0x1426cc955` | ES-protects-Mana gate and ES percentage term |
| `0x1426cca3b..0x1426ccd3a` | Life percentage, signed32 siphoning product and float32 accumulation |
| `0x1426ccd3f..0x1426ccd94` | Physical rounding/saturation followed by raw tail addition |
| `0x1426cfab4..0x1426cfcc9` | Complementary chaos bleeding and Rupture branch |
| `0x1426cfcc9..0x1426cffab` | Ignite-transfer and flask gates |
| `0x1426d032a..0x1426d035d` | Signed32 frenzy product before percentage clamp |
| `0x1426d060e..0x1426d06d7` | Ordered chaos accumulation and final conversion |

Ignored batch `Tools/re/out/client-damage-audit/r12-self-rates` retains all four
original registrar/callback records, declared dependencies and traversed ready-cache paths.
`r12-self-rates-regdeps.py` checks both original dependency vectors.
`r12-self-rates-verify.py` compares separate equations with interpretation of the
original callback instructions in **5,600 synthetic cases / 790,933 steps**.
The cases include branch controls, every input's signed extremes, seeded random inputs,
post-cap wrap, and actual branch-dependent read sets.
This interpretation supplies synthetic ready-cache values; it does not execute game code.

Independent `r12-self-rates-review.py` rereads all 1,515 original instruction records,
27 byte anchors and four arithmetic constants, reconstructs both registrar vectors,
and checks 27 distinct numerical controls plus four exact branch read sets using
a different retained instruction interpreter. Saturation-before-tail, signed overflow
and float32 cancellation/order controls all pass.

Source-instance creation, infliction, overlap selection and expiry-to-rate publication
remain open. Per-minute names express rate units, not a rolling minute allowance;
the already recovered [Life integration](client-dot-routing.md) remains a separate downstream contract.
