# Skill-duration coupling for bleeding and poison (3.29.3b)

## Result and scope

Six registered callbacks combine a skill's base duration with bleeding or
poison duration modifiers. They recover a concrete numeric continuation
after the [source ailment-duration inputs](client-ailment-duration-inputs.md),
including general skill-duration factors and two distinct endurance-charge
terms. They do not identify an actual ailment constructor or a target's
incoming-rate publisher. **R2/R3/R4 remain Partial.**

A separate consumer search found a useful false lead: literals 6187/6188
in one client constructor select **ClientStrings Atlas/League labels**,
not the same-numbered Stats. They must not be counted as numerical duration
consumers. This report stops at that positive label-lookup classification;
reconstructing the surrounding interface is outside the damage research.

All evidence is offline original-byte analysis and interpretation. No client
was launched or attached, no new captures were acquired, and no server code
was changed. Executable SHA256:
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`.
Stats SHA256:
`91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da`.

## 1. Registered duration chain

| Output / Stats Id | Registrar | Callback | Declared inputs |
|---|---|---|---|
| 17168 `combined_bleeding_skill_effect_duration_+%` | `0x1400cd530` | `0x1428068d0` | 4598, 2818, 171, 362, 8702, 363 |
| 17167 `combined_bleeding_skill_effect_duration_+%_final` | `0x1400cd5a0` | `0x142806e80` | 3535, 9329 |
| 6187 `bleeding_skill_effect_duration` | `0x1400cd600` | `0x142807120` | 359, 17168, 17167 |
| 17166 `combined_poison_skill_effect_duration_+%` | `0x1400cd810` | `0x142809200` | 2818, 171, 362, 8702, 363, 3424 |
| 17165 `combined_poison_skill_effect_duration_+%_final` | `0x1400cd880` | `0x1428097a0` | 3535, 6477 |
| 3425 `poison_skill_effect_duration` | `0x1400cd8e0` | `0x142809a40` | 359, 17166, 17165 |

Each registrar passes the listed output and callback to `0x142513630`.
The declaration order comes from the separately retained
[native dependency graph](client-stat-dependency-order.md), not a guessed
sort of the names. A declaration does not itself prove an actor invokes it.

The source bleeding inputs are 4598/9329; poison uses 3424/6477. Their
complete equations, including faster compensation, are in the preceding
report. The six equations here are additional stages, not a replacement
of those source calculations.

## 2. Complete equations

Use the preceding report's signed32 `S(k)`, wrapping `add32`/`mul32`,
ordered binary32 factor `F` and signed nearest rounding `R`.
Let `C = S(8702)` and `C5 = min_signed(C, 5)`.

```text
bleed17168 = add32(S(4598), mul32(S(363), C5), mul32(S(362), C), S(2818), S(171))
poison17166 = add32(S(2818), mul32(S(362), C), mul32(S(363), C5), S(171), S(3424))

bleed17167 = R(f32(f32(f32(F(S(3535)) * 100.0f) * F(S(9329))) - 100.0f))
poison17165 = R(f32(f32(f32(F(S(3535)) * 100.0f) * F(S(6477))) - 100.0f))

bleed6187 = R(f32(f32(f32(S(359)) * F(S(17168))) * F(S(17167))))
poison3425 = R(f32(f32(f32(S(359)) * F(S(17166))) * F(S(17165))))
```

The additive orders follow their actual final accumulators. All products
and additions wrap before subsequent floating conversion where applicable.
Each final factor is individually clamped by the original max-selector;
intermediate percentage results are rounded back to signed32 before use
by the next registered output. Flattening the whole graph into one
unrounded real-number expression would discard those intermediate results.

| Key | Pinned Stats Id |
|---|---|
| 359 | `base_skill_effect_duration` |
| 2818 | `virtual_skill_effect_duration_+%` |
| 3535 | `virtual_skill_effect_duration_pluspercent_final` |
| 171 | `buff_duration_+%` |
| 362 | `buff_effect_duration_+%_per_removable_endurance_charge` |
| 363 | `buff_effect_duration_+%_per_removable_endurance_charge_limited_to_5` |
| 8702 | `current_removable_endurance_charges` |

Both additive callbacks compare signed charge count with five and select
the capped pointer: `0x142806aca..0x142806ad8` and
`0x1428094cd..0x1428094db`. Only key 363's product uses that capped count.
Key 362 independently uses the full count. With modifiers 7 and 11,
counts 4/5/6 produce **72/90/97**. The callback has no lower charge clamp;
synthetic count -2 produces -36. This states arithmetic, not availability
of negative charges in play.

The terminal callbacks convert raw base 359 to binary32, multiply by the
two separately built factors and round. There is no embedded default of
2,000 or 5,000, no divide-by-1,000 and no final zero floor in these bodies.
Base 2,000, additive +50 and final 0 yield **3,000** in both callbacks;
base -100 with zero modifiers stays -100 in the supplied-state controls.

Consequently these numeric outputs keep the base input's unit. They are
distinct from a Buff's remaining-time float and its independently stored
expiration speed. This pass does not establish where the skill-duration
override is chosen, how source seconds are converted, or how the result
is assigned to a newly inflicted instance.

In particular, native key 3745 `poison_duration_is_skill_duration`, key
6188 `bleed_duration_is_skill_duration` and key 1192
`bleed_on_hit_base_duration` are **not read by these six callbacks**.
Their actual application/selection consumer remains open. The callback
names do not authorize applying this branch to every bleed or poison.

## 3. Bounded consumer navigation and a positive label collision

The new search selects nine keys: 1192, 3425, 3745, 6187, 6188 and
17165..17168. It byte-prefilters full imm32 and disp32 encodings, then
decodes complete unwind-owned ranges and confirms instruction operands.
It completes **985 candidate owners**, retaining **121 exact operand hits
in 63 owners**: 10 in known registrars, 20 in known formula callbacks and
91 untyped hits elsewhere. Low displacements and immediates frequently
represent unrelated sizes or object offsets. Those are candidates, not
91 numerical consumers.

Constructor `0x140b2dbf0` supplies two particularly plausible-looking hits:

| Literal / call | Actual operation |
|---|---|
| `0x140b2e11a` → `0x140b2e123` | Pass integer 6187 to `0x1421fdc40` |
| `0x140b2e18e` → `0x140b2e197` | Pass integer 6188 to the same function |

The already recovered [text lookup](../protocol/address-index.md) truncates
the ID to u16 at `0x1421fdc54`, calls `0x1421fd1f0` at `0x1421fdc97`, then
copies the returned UTF-16 string at `0x1421fdcd1`. It does not invoke a
numerical Stats getter. The pinned `ClientStrings.json` rows, using row+1
keys, are:

| Key | ClientStrings Id | English text |
|---:|---|---|
| 6187 | `CurrencyStashSubTabAtlas` | Atlas |
| 6188 | `CurrencyStashSubTabLeague` | League |

ClientStrings SHA256:
`11b75772929447f266947532d2ec8e667862cfe6d1e17a076925d4030773db64`.

The caller consumes the returned string's pointer/length and forwards it
to child construction. This positively rules out interpreting **these two
sites** as Stats duration reads. It says nothing about other computed,
indirect or data-driven consumers.

The inventory excludes shorter immediates, computed keys, data-table
navigation, inlined semantic attribution and code without unwind ownership.
It cannot prove that a native instance publisher is absent. It does not
close the source-to-target rate edge or justify shrinking that requirement.

## 4. Reproduction and remaining work

Ignored local evidence under `Tools/re/out/client-damage-audit/`:

```powershell
python Tools/re/out/client-damage-audit/r37-duration-consumers.py
python Tools/re/out/client-damage-audit/r37-skill-duration-formulas.py
python Tools/re/out/client-damage-audit/r37-skill-duration-fresh.py
```

The consumer inventory is retained in `r37-duration-consumers/candidates.json`;
numeric records and controls are in `r37-skill-duration/`. These are local
offline records, not REA Evidence IDs. The original max-selector leaf is
interpreted as original instructions, with supplied valid input caches.
The 72 compositions explicitly feed prior numeric outputs into later
callbacks; they do not claim native dependency-dispatch or entity execution.

The formula pass checks **1,266 controls / 100,709 interpreted instructions /
877 visited sites**. They include 286 single-input boundaries, 600 mixed
contexts and 360 stage evaluations across 72 compositions. Fresh verification
also checks the consumer hits at original instruction boundaries, actual
registrars and declarations, source table identities and label rows. Full
range identities include the four earlier source callbacks actually executed
by the compositions; additional constructor/text-lookup records are static.
The focused original-byte audit retains **19 function identities / 19 ranges /
4,591 decoded instructions**, six registrars, 22 declared input entries,
15 named inputs and 35 instruction anchors. Seven entries are new to the
manifest: six numeric callbacks and the label-bearing constructor; these
are not seven new instance/publication mechanisms.

The established increment is the concrete skill-duration numeric chain and
the classification of two misleading literals. Actual ordinary/default
ailment duration selection, instance creation, target selection/rate
publication, shared cap, expiry withdrawal and generic owner-driven clock
refresh remain open. Accepted-Hit numerical damage/mitigation-to-pool is
also still open. The existing [C/S receiving contracts](client-dot-source-authority.md)
and [PR #373 comparison](client-damage-pr373-comparison.md) retain their
separate evidence boundaries.
