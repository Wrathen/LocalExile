# Native critical-chance input builders (3.29.3b)

## 1. Result and boundary

The complete conditional input algebra of three native callbacks is recovered below: intermediary
main-hand chance **15066**, off-hand chance **553**, and spell chance **554**. Together with the
[critical formula supplement](client-critical-formulas.md), this closes the selected native chance,
effective attack probability and multiplier calculation contracts. It does not recover an official
server implementation, actual random-roll order, critical confirmation, or all producers of the
input stats. A native callback is also distinct from the network override/fallback provider described
in [stat evaluation](client-stat-evaluation.md).

Evidence is the original executable, its unwind-owned assembly, and the original decompiler exports;
defective `.compact.c` files are excluded. This investigation did not run or attach to the client.

| Artifact | Identity |
|---|---|
| Executable | `Client/Game.exe`, 3.29.3b, x86-64, preferred image base `0x140000000` |
| Executable SHA256 | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `data/tables/Stats.json` SHA256 | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| Native key mapping | Key `k` identifies JSON row `k - 1`; key zero has no such `Stats.Id` mapping |
| Canonical byte audit | Private `r6-critical-inputs` |
| Verification | Private `r6-critical-inputs-verify.py` / `.json` |

| Output | Registration | Callback |
|---|---|---|
| 15066 `intermediary_main_hand_critical_strike_chance` | `0x1400bca40` | `0x142754c20` |
| 553 `off_hand_critical_strike_chance` | `0x1400bcbc0` | `0x142756810` |
| 554 `spell_critical_strike_chance` | `0x1400bccf0` | `0x142758320` |

## 2. Notation, arithmetic and finalization

`S(k)` is the callback's effective signed 32-bit stat value after its local-cache / native-override /
upstream lookup. `F(k)` is predicate helper `0x1425135a0`. `when(condition, value)` contributes the
value if true, otherwise zero; numeric conditions mean **nonzero**, unless another comparison is
shown. The formulas describe one evaluation context's effective inputs, not repeated addition of
cache and parent values. Generic stat formulas supplying these inputs remain their own contracts.

Chance and added-base terms below use **permyriad**, where 100 means one percentage point.
Increased/final terms use percentage points. Preserve the operations rather than inferring a scale
from a stat name ending in `%`: the fixed-chance return multiplies by 100, while the unarmed added
base read does not. Integer additions/products retain their low 32 bits; comparisons are signed,
except the explicit unsigned weapon-type test. Integer divisions by 10 and 200 truncate toward zero.

Define these helpers:

```text
UPercent(n) = float32(uint32(n)) / 100.0f
SPercent(n) = max(0.0f, float32(int32(n)) / 100.0f)
Round(x)   = truncate_int32(float32(x + (x < 0 ? -0.5f : +0.5f)))
Cap        = S(17613) != 0 ? S(17613) : 10000
Finish(B, I, M) = min_signed(Cap,
                    Round(float32(float32(UPercent(I) * float32(B)) * M)))
```

`I` already includes the initial 100; neither percentage helper adds it. `SPercent` is the signed
helper `0x141d1e400`; `UPercent` is `0x1428e73a0`, which zero-extends ECX before 64-bit-to-float
conversion. Both call max-with-zero leaf `0x14013c620`. `Round` is the complete leaf
`[0x142513810,0x14251383a)`, with sign-dependent halves and truncation. Finite, in-range values
round half away from zero. Conversion overflow / NaN and invalid weapon-type indices are machine
edge cases; these formulas do not replace them with mathematical arbitrary-precision behavior.
Key 17613 is `virtual_maximum_critical_strike_chance`.

Each builder returns **zero immediately if its assembled `B <= 0`**, before increased/final factors
or cap. There is no final lower clamp after `Finish`: a nonzero negative cap remains negative.
The two attack builders also have an earlier fixed-chance return that bypasses all these stages.
The final multiplication order is `UPercent(I) * float32(B)`, then the precomputed product of final
factors, with binary32 rounding after each multiplication.

## 3. Shared added-base terms

All three builders add the following terms to their selected base:

| Term | Condition / calculation | Input `Stats.Id` |
|---|---|---|
| Elusive | `when(S(10130), S(10154))` | `affected_by_elusive`; `additional_critical_strike_chance_permyriad_while_affected_by_elusive` |
| At maximum power charges | `when(S(7) == S(2702), S(6440))` | `current_power_charges`; `virtual_maximum_power_charges`; `additional_critical_strike_chance_permyriad_while_at_maximum_power_charges` |
| Hatred | `when(S(7278), S(7328))` | `affected_by_hatred`; `additional_critical_strike_chance_permyriad_while_affected_by_hatred` |
| Herald | `when(S(11068), S(11659))` | `skill_is_herald_skill`; `additional_critical_strike_chance_permyriad_with_herald_skills` |
| Dead | `when(S(2527), S(13049))` | `is_dead`; `additional_critical_strike_chance_permyriad_while_dead` |
| Cat aspect | `when(S(7736), S(7733))` | `has_cat_aspect`; `additional_critical_strike_chance_permyriad_while_affected_by_cat_aspect` |
| General added base | `S(571)` | `additional_base_critical_strike_chance` |
| Per power charge | `S(7) * S(3784)` | `additional_critical_strike_chance_per_power_charge_permyriad` |
| Per shield ES | `trunc(S(12863) / 10) * S(6976)` | `total_shield_maximum_energy_shield`; `additional_critical_strike_chance_per_10_shield_maximum_energy_shield_permyriad` |
| Overloaded intensity | `S(12678) * S(12908)` | `current_overloaded_intensity`; `additional_critical_strike_chance_per_overloaded_intensity` |
| Exerting warcries | `S(16299) * S(21372)` | `number_of_warcries_exerting_this_action`; `additional_critical_strike_chance_permyriad_per_warcry_exerting_action` |

Call their integer sum `Common`. The maximum-power condition tests equality only: **zero current
and zero maximum satisfy it**. It does not additionally demand a positive charge count. The per-ES
term divides the shield total before multiplying, and the overloaded/warcry terms multiply counts
by their own values; these must not become boolean-only additions.

## 4. Attack builders: ordered overrides and base assembly

Set `melee = S(1486) != 0` (`attack_is_melee`) and `meleeSkill = S(6763) != 0`
(`skill_is_melee_skill`). They are separate inputs.

### Fixed chance comes first

For main hand, if `S(12658) != 0`, return `S(12658) * 100` immediately. For off hand, use key
12659 instead. Their Ids are `main_hand_weapon_crit_chance_is_%` and
`off_hand_weapon_crit_chance_is_%`. This bypasses added-base terms, increased/final factors, and
the maximum cap. It is not merely a replacement for the weapon's base term.

### Selected base and hand-specific additions

Let `t = S(3)` for main hand or `S(4)` for off hand, the corresponding weapon-type code.
The type exclusion is the actual unsigned test `uint32(t - 13) <= 1`.

```text
Main hand:
    if t not in {13, 14} and S(9893) != 0:
        WeaponBase = S(9893)
    else:
        WeaponBase = S(17790)
    UnarmedBase = when(t == 14 and melee, S(3907))
    B_MH = WeaponBase + UnarmedBase + Common + S(7187)
           + when(S(7174) >= 4, S(21501))

Off hand:
    if t not in {13, 14} and S(9893) != 0:
        WeaponBase = S(9893)
    else if S(9692) != 0:
        WeaponBase = S(9692)
    else:
        WeaponBase = S(17791)
    UnarmedBase = when(t == 14 and melee, S(3907))
    B_OH = WeaponBase + UnarmedBase + Common + S(7187)
           + when(S(7174) >= 4, S(21501))
           + when(S(246), S(15135))
           + S(13341)
           + trunc(S(12863) / 10) * S(14594)
```

| Key | `Stats.Id` |
|---:|---|
| 9893 | `override_weapon_base_critical_strike_chance` |
| 17790 / 17791 | `main_hand_base_crit_chance_from_weapon` / `off_hand_base_crit_chance_from_weapon` |
| 9692 | `active_skill_off_hand_base_critical_strike_chance_override` |
| 3907 | `base_melee_critical_strike_chance_while_unarmed_%` |
| 7187 | `attack_additional_critical_strike_chance_permyriad` |
| 7174 / 21501 | `number_of_equipped_elder_items` / `attack_additional_critical_strike_chance_permyriad_if_4_elder_items` |
| 246 / 15135 | `is_dual_wielding` / `additional_off_hand_critical_strike_chance_while_dual_wielding` |
| 13341 | `additional_off_hand_critical_strike_chance_permyriad` |
| 14594 | `off_hand_critical_strike_chance_+_per_10_es_on_shield` |

All replacements above replace only `WeaponBase`; later additions still apply. The special off-hand
skill override is lower priority than the generic nonzero override for eligible weapon types. Main
hand has no corresponding read of key 9692. The off-hand-specific ES term is **in addition to**
the common ES term. No full-weapon-chance scaling or extra multiplication by 100 is inserted here.

## 5. Attack increased factors and weapon classification

Start the increased accumulator with:

```text
I_MH = 100 + S(9449) + S(5322) + min_signed(200, S(14105) * S(7084))
I_OH = 100 + S(9449) + S(5322)
```

Keys 9449/5322 are `attack_critical_strike_chance_+%` and `combined_critical_strike_chance_+%`.
14105 is `main_hand_critical_strike_chance_+%_per_melee_abyss_jewel_up_to_+200%`;
7084 is `number_of_melee_abyss_jewels`. The capped product is **main-hand only**, with a signed
upper bound and no separate zero floor.

To either accumulator add `WeaponIncrease(t, V)`, where `V` is the hand's Varunastra stat:
4198 main hand / 4199 off hand (`*_varunastra_weapon_counts_as_all_1h_melee_weapon_types`).

```text
if V != 0:
    WeaponIncrease = S(2433) + S(2464) + S(2846) + S(561) + S(563)
else:
    WeaponIncrease = S(W[t])
    if t == 14 and meleeSkill and S(3932) != 0:
        WeaponIncrease += S(563)
```

`0x1428e7330` iterates the five keys in `[0x14449c0e8,0x14449c0fc)` and **adds all five**, not
their maximum. They are sword, mace, axe, dagger and claw increased critical chance. The unarmed
claw transfer requires `skill_is_melee_skill`, not the separate `attack_is_melee` used elsewhere.
Key 3932 is `modifiers_to_claw_critical_strike_chance_also_affect_unarmed_melee_critical_strike_chance`.

The following are the original lookup entries at `0x14449bdc0 + t*4`, and the combined
one-/two-handed selector from four separate byte tables. The callback uses the numeric index;
these rows are not an inferred mapping to the unrelated per-item `WeaponTypes.json` table.

| `t` | `W[t]` / increased stat family | Handed term |
|---:|---|---|
| 0, 1 | 2433 sword | `S(616)` |
| 2 | 2433 sword | `S(626)` |
| 3, 5 | 2464 mace | `S(616)` |
| 4 | 2464 mace | `S(626)` |
| 6 | 2466 wand | None |
| 7 | 2846 axe | `S(616)` |
| 8 | 2846 axe | `S(626)` |
| 9 | 562 bow | None |
| 10, 16 | 561 dagger | `S(616)` |
| 11, 17 | 2465 staff | `S(626)` |
| 12 | 563 claw | `S(616)` |
| 13, 14 | 0 | None |
| 15 | 0 | `S(626)` |

Key-zero entries preserve the actual `S(0)` call; no named stat or nonzero contribution is invented
for that sentinel. The four tables are at `0x142fdd740`, `0x142fdd728`, `0x142fdd758`, and
`0x142fdd688`. The first two must both be nonzero; then the third selects 616; otherwise the fourth
selects 626. Thus these handed additions depend on weapon classification, even if `melee` is false.
616/626 are `one_handed_melee_critical_strike_chance_+%` /
`two_handed_melee_critical_strike_chance_+%`.

After `WeaponIncrease`, add the following integer terms to each hand's accumulator, in this order:

| Addition | Condition / keys |
|---|---|
| Melee increased | If `melee`, add `S(1361)` (`melee_critical_strike_chance_+%`) |
| Recent warcry melee increased | Inside that same melee branch, if `S(4689) != 0`, add `S(11132)` (`num_warcries_used_recently`; `melee_critical_strike_chance_+%_if_warcried_recently`) |
| Full-life melee increased | Inside that same melee branch, if `S(14276) != 0`, add `S(1362)` (`virtual_on_full_life`; `melee_critical_strike_chance_+%_when_on_full_life`) |
| Handed increased | The table-selected 616/626 term above |
| Dual-wield increased | If `S(246) != 0`, add `S(659)` (`critical_strike_chance_while_dual_wielding_+%`) |
| Accuracy-scaled increased | If `S(6772) != 0`, add `trunc(S(292 or 293)/200) * S(6772)` |

6772 is `attack_critical_strike_chance_+%_per_200_accuracy_rating`; 292/293 are main-/off-hand
accuracy. The division is applied before multiplying the modifier.

The final-factor product is:

```text
Blind_MH = S(973) != 0 ? SPercent(100 + S(9756)) : 1.0f
Blind_OH = melee and S(973) != 0 ? SPercent(100 + S(9756)) : 1.0f
FinalCommon = SPercent(100 + S(5323))
M_hand = float32(FinalCommon * Blind_hand)
result = Finish(B_hand, I_hand, M_hand)
```

973 is `blinded`; 9756 is `keystone_blind_monk_melee_critical_strike_chance_+%_final`; 5323 is
`combined_critical_strike_chance_+%_final`. **The two callbacks really have different melee guards**:
main hand tests blinded without a surrounding `attack_is_melee` check; off hand first checks melee.
This is native code evidence, not a claim that a semantically inconsistent stat combination is
reachable in normal gameplay. Preserve the distinction when reconstructing these callbacks.

## 6. Spell builder

The spell path has no attack fixed-chance early return, generic weapon-base override, off-hand
override, attack increased, weapon-type increased, Varunastra sum, melee increased or Blind Monk
factor. It chooses the base through the boolean predicate **17572**, then adds the shared terms:

```text
SpellBase = F(17572) ? S(17790) : S(556)
B_spell = SpellBase + Common + S(7188)
          + when(S(6182), S(18283))
          + when(S(7470) >= 4, S(21504))
if B_spell <= 0: return 0

I_spell = 100 + S(8667) + S(5322)
M_spell = float32(SPercent(100 + S(23137)) * SPercent(100 + S(5323)))
result = Finish(B_spell, I_spell, M_spell)
```

| Key | `Stats.Id` |
|---:|---|
| 17572 | `base_spell_critical_chance_equal_to_the_critical_strike_chance_of_main_weapon` |
| 556 | `base_spell_critical_strike_chance` |
| 7188 | `spell_additional_critical_strike_chance_permyriad` |
| 6182 / 18283 | `used_through_frostbolt` / `active_skill_additional_critical_strike_chance_if_used_through_frostbolt` |
| 7470 / 21504 | `number_of_equipped_shaper_items` / `spell_additional_critical_strike_chance_permyriad_if_4_shaper_items` |
| 8667 | `virtual_spell_critical_strike_chance_+%` |
| 23137 | `assassin_ascendancy_spell_critical_strike_chance_+%_final` |

The copied weapon input is **17790**, not final main-hand chance 552 or intermediary 15066; the
spell's own additions/increased/final factors then apply. The spell callback also reads key 3 before
its increased stage (`0x1427598e3..0x1427598f0`), but does not use the returned value in its arithmetic.
That lookup remains part of the evaluation's read sequence, not an additional multiplier.

## 7. Branch and arithmetic anchors

The private verifier redecodes complete owned ranges, rather than trusting a selected decompiler
expression. Representative algebra boundaries are:

| Contract | Main hand | Off hand | Spell |
|---|---|---|---|
| Fixed chance return | Initial 12658 read / nonzero branch | Initial 12659 read / nonzero branch | Absent |
| Base selection | `0x1427552a9..0x142755330` | `0x142756f67..0x1427571a1` | `0x142758500..0x14275861e` |
| Base accumulation / nonpositive return | `0x142756305..0x142756366` | `0x142757fc7..0x142758055` | `0x14275984b..0x1427598e2` |
| Increased accumulator / weapon transfer | `0x14275636b..0x142756444` | `0x14275805a..0x142758142` | `0x1427598f5..0x142759919` |
| Melee / handed / dual-wield / accuracy | `0x142756444..0x142756536` | `0x142758142..0x142758260` | Absent |
| Final factor, rounding and cap | `0x142756539..0x1427565cb` | `0x142758263..0x1427582f9` | `0x142759911..0x142759983` |

The full per-callback stat-read inventory and operand/branch instruction records are retained in
the ignored verifier output alongside the weapon tables. The already-audited main-hand 552 wrapper
can subsequently replace 15066 with nonzero 15067; attack effective-probability callbacks consume
552/553 afterwards. Neither cannot-crit nor always-crit early returns belong inside the three input
builders above: they are in the later effective-probability stage.

This closes these callbacks' own formulas, including conditionals and numeric conversion order.
It does not recursively close every upstream stat, turn network overrides into native evaluation,
or supply missing per-event randomness and server-side resource mutation.
