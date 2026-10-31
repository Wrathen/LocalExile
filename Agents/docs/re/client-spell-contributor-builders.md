# Native spell contributor builders (3.29.3b)

## Result and scope

Four registered callbacks construct the life-cost and weapon-as-spell
contributors used by native spell damage composition. Their complete arithmetic,
branch input reads and finite-width behavior are recovered here. These are
intermediate contributors: a returned percentage is not itself spell damage,
and the life-cost result does not pay a resource cost.

The pinned `Client/Game.exe` SHA256 is
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`;
preferred image base is `0x140000000`.
`Stats.json` SHA256 is
`91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da`.
Native keys are one-based JSON row indices. All checks are static/offline;
no client execution, attachment, capture or server modification was performed.

| Output key and `Stats.Id` | Registrar | Callback | Declared inputs |
|---|---|---|---|
| 19700 `spell_total_added_physical_damage_from_life_cost` | `0x1400dd730` | `0x14289c710` | 5 |
| 13705 `virtual_main_hand_weapon_damage_as_added_spell_damage_%` | `0x1400e2260` | `0x1428bea30` | 11 |
| 13707 `virtual_main_hand_weapon_physical_damage_as_added_spell_damage_%` | `0x1400e2200` | `0x1428be820` | 2 |
| 18333 `virtual_off_hand_weapon_damage_as_added_spell_damage_%` | `0x1400e22e0` | `0x1428bf330` | 10 |

The registrar vectors and the union of branch reads agree for all four outputs.
Declared dependency order is not evaluation order. This pass retains the
[native stat activation and publication boundary](client-stat-evaluation.md):
recovering a callback does not prove it is evaluated for every network entity.

## 1. Inputs and exact integer operations

`S[k]` is a signed32 input read from the native computation context.
`W(x)` wraps to signed32. `T(x,n)` divides a signed integer by positive `n`,
truncating toward zero. Define `D(x) = T(W(6*x),10)`.
The multiplication wraps **before** division; replacing `D` with a floating
`0.6*x` would change negative, fractional and overflow results.
There is no floating-point arithmetic or final nonnegative clamp in these four
callbacks. Boolean helpers treat every nonzero signed input as true.

| Key | Verified `Stats.Id` |
|---|---|
| 247 / 4206 / 14185 | `maximum_life` / `life_reserved` / `life_cost` |
| 12814 | `is_not_hand_cast` |
| 19699 | `spells_you_cast_gain_added_physical_damage_%_life_cost_if_payable` |
| 3 / 4 | `main_hand_weapon_type` / `off_hand_weapon_type` |
| 4198 | `main_hand_varunastra_weapon_counts_as_all_1h_melee_weapon_types` |
| 4199 | `off_hand_varunastra_weapon_counts_as_all_1h_melee_weapon_types` |
| 11640 | `is_wielding_two_weapon_types` |
| 18861 | `support_spell_weapon_damage_gain_%_of_one_hand_melee_weapon_damage_as_added_spell_damage` |
| 18936 | `support_spell_weapon_damage_gain_%_of_one_hand_melee_weapon_damage_as_added_spell_damage_while_wielding_two_different_weapon_types` |
| 19471 | `gain_%_of_base_dagger_damage_as_added_spell_damage` |
| 13602 | `spells_you_cast_gain_%_of_base_main_hand_weapon_damage_as_added_spell_damage` |
| 18335 | `spells_you_cast_gain_%_of_base_off_hand_weapon_damage_as_added_spell_damage` |
| 13706 | `keystone_battlemage` |
| 13708 | `gain_%_of_two_hand_weapon_physical_damage_as_added_spell_damage` |

The native type predicates are byte tables, not runtime string comparisons.
Within the inspected integer domain `0..17`, their exact nonzero sets are:

| Predicate | Original address | Nonzero indices |
|---|---|---|
| `V(t)`: selected-hand validity | `0x142fdd728` | All except 13, 14 |
| `M1(t)` | `0x142fdd740` | All except 6, 9 |
| `M2(t)` | `0x142fdd758` | 0, 1, 3, 5, 6, 7, 10, 12, 16 |
| `P(t)`: physical-only two-hand predicate | `0x142fdd688` | 2, 4, 8, 9, 11, 15, 17 |

Let `M(t)=M1(t) && M2(t)`, whose set is
`{0,1,3,5,7,10,12,16}`. The numerical model preserves these exact predicates;
it does not infer equipability of every synthetic combination from their names.
The callbacks contain no native bounds check before the indexed reads.
Out-of-domain type values are outside the verified model.

## 2. Life-cost physical contributor

The complete output 19700 is:

```text
available = W(S[247] - S[4206])
cost = S[14185]
if cost > available: return 0
if S[12814] != 0: return 0
return T(W(S[19699] * cost), 100)
```

The comparison is signed. Its input is **maximum Life minus reserved Life**;
the body does not read current Life or perform a cost payment.
Read order is 247, 4206, 14185, then conditionally 12814 and 19699.
An unpayable cost therefore suppresses both later reads; a non-hand-cast result
suppresses the coefficient read.

At `0x14289c8f0`, `SUB EBP,ECX` establishes signed32 available Life.
At `0x14289c9cd`, the cost comparison precedes the handcast helper.
At `0x14289cac6`, `IMUL EBX,ESI` keeps the low32 product; the following
`0x51eb851f` signed multiply/shift sequence implements truncation by 100.
For Life 1000, reserved 100, cost 900 and coefficient 25, the result is 225.
Cost 901 returns zero. A synthetic wrapped product of -100 returns -1;
the callback does not clamp that result.

## 3. General main-hand and off-hand percentages

Both callbacks first read 3, 4, 4198 and 4199, even if the selected hand will
fail validity. Choose `own/other` and `ownVarunastra/otherVarunastra` according
to the output hand. Then:

```text
if !V(own): return 0

subtotal = 0
if M(own):
    subtotal = S[18861]
    if S[11640] != 0:
        subtotal = W(subtotal + S[18936])
    if M(other):
        subtotal = D(subtotal)

dagger = S[19471]
ownDagger = ownVarunastra != 0 || own in {10,16}
otherDagger = otherVarunastra != 0 || other in {10,16}
if dagger != 0 && ownDagger:
    subtotal = W(subtotal + (otherDagger ? D(dagger) : dagger))

handcast = 0
if S[12814] == 0:
    handcast = S[13602]     // main hand
            or S[18335]    // off hand

main-hand result = W(subtotal + handcast + (S[13706] != 0 ? 100 : 0))
off-hand result  = W(subtotal + handcast)
```

These separate gates matter:

- The two-type input 11640 is read only inside the support's selected-hand
  `M(own)` branch. Its nonzero state controls addition of 18936; it does not
  control whether the subsequent dual-hand scaling occurs.
- Varunastra flags affect the explicit 19471 dagger predicates. They do not
  replace either native table predicate in the 18861/18936 support branch.
- The support subtotal and dagger contribution each undergo their own
  `D` operation. They are not summed and scaled together.
- The handcast-only 13602/18335 term is added after those operations and is
  not scaled by them. Main-hand Battlemage adds exactly 100, also afterwards,
  independently of the handcast gate. The off-hand callback never reads 13706.
- A failed selected-hand validity gate suppresses every later contribution,
  including the handcast term and main-hand Battlemage.

For example, with both selected types in `{10,16}`, support inputs 37 and 19,
two-type flag set, dagger input 23 and handcast term 11, the result is
`T(6*56,10) + T(6*23,10) + 11 = 33+13+11 = 57`.
With the other hand outside the respective predicates, those two reductions
do not apply. These are coefficient values, not final flat damage values.

Main-hand support gating begins at `0x1428bedb4`; the low32 six-times
support subtotal is formed at `0x1428bf0ba..0x1428bf0c4`.
The separate dagger path begins at `0x1428bf1ba`.
The handcast and Battlemage helpers occur at `0x1428bf214` and `0x1428bf2ff`.
Off-hand counterparts include validity at `0x1428bf6a4`, support division
at `0x1428bf9aa..0x1428bf9c0`, and the handcast helper at `0x1428bfb03`.
Its final `LEA EAX,[RDI+RSI]` is at `0x1428bfbe7`.

## 4. Physical-only main-hand percentage

Output 13707 is a separate, much smaller callback:

```text
type = S[3]
if !V(type): return 0
if !P(type): return 0
return S[13708]
```

It does not read either Varunastra flag, the off-hand type, Battlemage or
the handcast gate. Input 13708 is read only after both tables pass.
The two table reads are at `0x1428be914` and `0x1428be924`;
the ready-cache coefficient read is at `0x1428be944`.
This callback does not prove how a later consumer combines its percentage
with the general main-hand percentage.

## 5. Verification and remaining boundary

Private reproducibility artifacts under `Tools/re/out/client-damage-audit/`
are `r13-spell-builders-model.py`, `r13-spell-builders-verify.py` and
`r13-spell-builders-verification.json`. The verifier:

- Re-reads the pinned PE and stat table, re-derives all eight registrar/callback
  function records, and checks 31 explicit original-byte anchors and four tables.
- Reconstructs the registrar stack vectors independently of branch read sets.
- Compares the separate equations with original-instruction interpretation,
  including read order, for **13,488** numerical controls. These include all
  18-by-18 inspected type pairs, both Varunastra flags, two-type and handcast
  gates for both general builders, and seeded signed32 extreme/random controls.
- Checks named Life/reservation boundaries, overflow before division, negative
  truncation, separate dual-hand rounding, handcast suppression, Battlemage
  asymmetry and explicit early-return read boundaries.

The retained eight function records contain 10 PE ranges and 1,621 decoded
instructions. Interpretation uses a synthetic fully-valid local stat cache;
cache-miss/provider internals are retained in the function records but are not
claimed as dynamically exercised. No native code is executed.

This closes the four numerical builder bodies for their inspected input domain.
It does not close modifier acquisition, reachability of arbitrary synthetic
equipment combinations, upstream current-resource/cast ownership, network-entity
evaluation activation, final panel publication, hit/ailment generation or
resource deduction. Those boundaries require their own consumers and producers.
