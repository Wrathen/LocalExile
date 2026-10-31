# Native damaging-ailment eligibility (3.29.3b)

## 1. Result and boundary

Twenty-one native formula registrations recover three different eligibility contracts:
which source damage types can contribute to ignite or poison, hand-specific elemental
poison eligibility, and whether a target can gain bleeding, ignite or poison. These
callbacks publish integer **0 or 1**, not damage, probability, duration or instance data.
Permission to use a damage type does not grant a chance to inflict the ailment, cancel
another prohibition, or establish that a target successfully receives an instance.

This supplements [outgoing ailment formulas](client-ailment-outgoing-formulas.md) and
[ailment speed/multiplier inputs](client-ailment-multiplier-inputs.md). A callback reads
its current evaluation context; the [native versus network provider boundary](client-stat-evaluation.md)
still applies. No official server implementation, client execution, attachment, or new
capture is claimed. In particular, eligibility outputs do not close the source-to-instance
publisher or the incoming DoT contribution path.

| Artifact | Identity |
|---|---|
| Executable | `Client/Game.exe`, x86-64, 3.29.3b, preferred base `0x140000000` |
| Executable SHA256 | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `data/tables/Stats.json` SHA256 | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| Native key mapping | Key `k` = zero-based JSON row `k - 1`, field `Id` |
| Original-byte audit | Private `r7-ailment-eligibility-formulas`: 42 function records / 42 ranges / 1,708 instructions; no undecoded bytes |
| Decompilation | Ghidra 12.1.4, bounded unwind-owned functions, `-noanalysis`; 42 completed exports |
| Verification | Private `r7-ailment-eligibility-verify.py` and `r7-ailment-eligibility-verification.json` |

`S(k)` is the effective signed 32-bit input. `B(k)` means `S(k) != 0`, including negative
values. Predicate helper `0x1425135a0` tests the full integer with `test eax,eax` at
`0x14251360c` and produces `setne al` at `0x142513618`; it does not test only a low bit.
Logical operations below short-circuit **left to right** as written. All comparisons of
numeric counts and attributes are signed; these formulas contain no floating-point
scaling, numeric damage-type enum or weapon-category lookup table.

## 2. Ignite source-type permission and fire prohibition

| Output / `Stats.Id` | Registration | Callback |
|---|---|---|
| 17689 `virtual_all_damage_can_ignite` | `0x1400b0530` | `0x1426f1e20` |
| 6009 `virtual_physical_damage_can_ignite` | `0x1400b05a0` | `0x1426f1eb0` |
| 6010 `virtual_fire_damage_cannot_ignite` | `0x1400b0600` | `0x1426f1ef0` |
| 6011 `virtual_cold_damage_can_ignite` | `0x1400b0660` | `0x1426f1f30` |
| 6012 `virtual_lightning_damage_can_ignite` | `0x1400b06c0` | `0x1426f1f90` |
| 6013 `virtual_chaos_damage_can_ignite` | `0x1400b0720` | `0x1426f1fd0` |

```text
17689 = B5080 || B4028 || B17688
        || (B1843 && B7029)
        || (B20658 && B20654)

6009 = B17689 || B21575
6010 = B2107 || B2101
6011 = B17689 || B2105 || B4875 || B2102
6012 = B17689 || B7681
6013 = B17689 || B7986 || B13008
```

| Key | `Stats.Id` |
|---:|---|
| 5080 | `all_damage_can_ignite` |
| 4028 | `all_damage_can_chill_ignite_shock` |
| 17688 | `all_damage_can_ignite_freeze_shock` |
| 1843 / 7029 | `is_ignited` / `all_damage_can_cause_elemental_ailments_you_are_suffering_from` |
| 20658 / 20654 | `ailment_bearer_active` / `ailment_bearer_all_damage_can_inflict_elemental_ailments` |
| 21575 | `base_physical_damage_can_ignite` |
| 2107 / 2101 | `fire_damage_cannot_ignite` / `unique_fire_damage_shocks` |
| 2105 / 4875 / 2102 | `cold_damage_can_ignite` / `unique_cold_damage_can_also_ignite` / `unique_cold_damage_ignites` |
| 7681 | `lightning_damage_can_ignite` |
| 7986 / 13008 | `chaos_damage_can_ignite_chill_and_shock` / `base_chaos_damage_can_ignite` |

The self-ailment branch checks `is_ignited` first and reads the transfer permission only
when that is true. The Ailment Bearer branch checks its active state before its permission.
Neither branch is an unconditional global enable. The fire output has the opposite
polarity from the other four typed outputs: **1 prohibits fire from igniting**. It does
not read 17689; enabling all-damage permission does not clear this prohibition. The already
audited fire-rate consumer independently returns zero for key 6010. Do not turn all six
outputs into interchangeable positive permission flags.

The all-damage builder short-circuits to 1 at `0x1426f1ea5`. Its conditional pairs are
`0x1426f1e59..0x1426f1e79` and `0x1426f1e7b..0x1426f1e9b`. Fire's two independent
prohibitions are `0x1426f1ef6..0x1426f1f16`.

## 3. Poison permission, prohibitions and hand locality

| Output / `Stats.Id` | Registration | Callback |
|---|---|---|
| 17696 `virtual_all_damage_can_poison` | `0x1400b1070` | `0x1426f2800` |
| 6116 `virtual_fire_damage_can_poison` | `0x1400b10e0` | `0x1426f2880` |
| 13125 `main_hand_fire_damage_can_poison` | `0x1400b1140` | `0x1426f28d0` |
| 13128 `off_hand_fire_damage_can_poison` | `0x1400b11a0` | `0x1426f2920` |
| 6117 `virtual_cold_damage_can_poison` | `0x1400b1200` | `0x1426f2970` |
| 13126 `main_hand_cold_damage_can_poison` | `0x1400b1260` | `0x1426f29c0` |
| 13129 `off_hand_cold_damage_can_poison` | `0x1400b12c0` | `0x1426f2a10` |
| 6118 `virtual_lightning_damage_can_poison` | `0x1400b1320` | `0x1426f2a60` |
| 13127 `main_hand_lightning_damage_can_poison` | `0x1400b1380` | `0x1426f2ab0` |
| 13130 `off_hand_lightning_damage_can_poison` | `0x1400b13e0` | `0x1426f2b00` |
| 6115 `virtual_physical_damage_cannot_poison` | `0x1400b14a0` | `0x1426f2b90` |
| 6119 `virtual_chaos_damage_cannot_poison` | `0x1400b1500` | `0x1426f2bd0` |

```text
17696 = B6121 || (B11864 && B11860)
        || (B21749 && B2531 && B438)

6116 = !B6120 && (B17696 || B6253)
6117 = !B6120 && (B17696 || B6254)
6118 = !B6120 && (B17696 || B6255)

13125 = !B6120 && (B6116 || B13132)   # main-hand fire
13126 = !B6120 && (B6117 || B13132)   # main-hand cold
13127 = !B6120 && (B6118 || B13132)   # main-hand lightning
13128 = !B6120 && (B6116 || B13133)   # off-hand fire
13129 = !B6120 && (B6117 || B13133)   # off-hand cold
13130 = !B6120 && (B6118 || B13133)   # off-hand lightning

6115 = B6120 || B7114
6119 = B7114
```

| Key | `Stats.Id` |
|---:|---|
| 6121 | `all_damage_can_poison` |
| 11864 / 11860 | `all_damage_can_poison_while_affected_by_glorious_madness` / `affected_by_glorious_madness` |
| 21749 / 2531 / 438 | `triggered_spells_all_damage_can_poison` / `skill_is_triggered` / `casting_spell` |
| 6120 | `non_chaos_damage_cannot_poison` |
| 6253 / 6254 / 6255 | `base_fire_damage_can_poison` / `base_cold_damage_can_poison` / `base_lightning_damage_can_poison` |
| 13132 / 13133 | `main_hand_local_all_damage_can_poison` / `off_hand_local_all_damage_can_poison` |
| 7114 | `chaos_damage_cannot_poison` |

The triggered branch requires the permission, triggered classification **and** `casting_spell`.
It does not merely test that a skill was triggered. Each elemental and hand builder tests
6120 first and returns 0 immediately when prohibited, so neither global permission nor a
hand-local permission overrides that input. Main-hand local permission is not read by an
off-hand builder and does not rewrite the shared virtual elemental outputs. There is no
additional explicit `skill_is_attack` or weapon-type test inside these six hand callbacks.

Preserve the surprising physical prohibition exactly: callback `0x1426f2b90` reads both
6120 and **7114 `chaos_damage_cannot_poison`**, despite publishing the *physical* output.
The second getter is at `0x1426f2baf` and branches to 1 at `0x1426f2bb6`. The chaos
callback only reads 7114 and zero-extends the returned Boolean. This report does not
rename that input, infer an item-level explanation, or replace it with an intuitive rule.
The existing physical-source poison-rate formula independently consumes 6115 as a veto.

The all-poison conditional blocks are `0x1426f2817..0x1426f2837` and
`0x1426f2839..0x1426f286a`. The elemental and hand veto precedes either enabling read;
for example fire uses `0x1426f2886..0x1426f2895`, MH fire uses
`0x1426f28d6..0x1426f28e5` and OH fire uses `0x1426f2926..0x1426f2935`.

## 4. Target acquisition gates

| Output / `Stats.Id` | Registration | Callback |
|---|---|---|
| 8264 `cannot_gain_bleeding` | `0x1400c0000` | `0x142777ed0` |
| 7005 `cannot_be_ignited` | `0x1400c0230` | `0x1427790c0` |
| 11992 `virtual_cannot_be_poisoned` | `0x1400c0ad0` | `0x14277fbb0` |

For readability define `Occupied = B17970 && B18498`, but evaluate it at the indicated
place in each callback, rather than eagerly before the earlier immunity checks.

```text
8264 = B5012 || B4770 || Occupied || B19161

7005 = B5005 || B17727 || Occupied
       || (B7001 && S575 > S581)
       || (B4513 && B7544)
       || (B12294 && B12290)
       || (B8498 && B20408)
       || (B15130 && S2700 == S5)
       || B19161

T = S11991                              # read before the following immunity checks
11992 = B8872 || B3696 || Occupied
        || (T != 0 && T <= S14762)
        || (B18608 && B2083)
        || B19161
```

| Key | `Stats.Id` |
|---:|---|
| 17970 / 18498 | `cannot_have_more_than_1_damaging_ailment` / `virtual_has_a_damaging_ailment` |
| 19161 | `cannot_gain_damaging_ailments` |
| 5012 / 4770 | `base_cannot_gain_bleeding` / `virtual_immune_to_bleeding` |
| 5005 / 17727 | `immune_to_ignite` / `base_cannot_be_ignited` |
| 7001 | `cannot_be_ignited_with_strength_higher_than_dex` |
| 575 / 581 | `strength` / `dexterity` |
| 4513 / 7544 | `current_number_of_fire_golems` / `cannot_be_ignited_while_flame_golem_summoned` |
| 12294 / 12290 | `have_been_ignited_recently` / `cannot_be_ignited_if_you_have_been_ignited_recently` |
| 8498 / 20408 | `cannot_be_shocked_or_ignited_while_moving` / `virtual_is_moving` |
| 15130 | `cannot_be_ignited_while_at_maximum_endurance_charges` |
| 2700 / 5 | `virtual_maximum_endurance_charges` / `current_endurance_charges` |
| 8872 / 3696 | `virtual_immune_to_poison` / `cannot_be_poisoned` |
| 11991 / 14762 | `cannot_be_poisoned_if_x_poisons_on_you` / `num_poisons` |
| 18608 / 2083 | `cannot_be_poisoned_while_bleeding` / `is_bleeding` |

Important boundary cases follow directly from the instructions:

- Strength must be **strictly greater** than dexterity; equality does not qualify
  (`cmp edi,ecx` / `jg` at `0x1427793b0..0x1427793b2`).
- The flame-golem count is tested for nonzero, not positive. The count is read before
  its enabling modifier. The recent-ignite state is also tested before its modifier.
- Maximum-endurance protection requires equality only. Enabled protection with current
  and maximum both zero qualifies; no positive maximum guard appears
  (`0x142779b86..0x142779b88`). Values above the maximum do not satisfy equality.
- A zero poison threshold disables that branch. A nonzero threshold compares **signed**
  `T <= num_poisons`, not equality and not `T > 0`
  (`0x14277fcfb..0x14277fcfd`, `0x14277fde6..0x14277fde8`).
- `Occupied` does not ask whether the existing ailment is the same type as the proposed
  one, or whether the new one would be stronger. Those questions are not inputs here.

These are acquisition gates. They do not prove removal of existing ailments, suppress
their damage, change their remaining duration, or equate immunity with an avoidance roll.
The upstream immunity and occupied-state producers are retained as inputs; this bounded
pass does not expand their entire formula families or identify every granting item.

## 5. Evidence and reproducibility

All 21 registrations are verified from original instructions: output in EDX, callback in
R8, and dependency begin/end span in R9 at the call to `0x142513630`. The span is decoded
from actual stack/vector writes, not inferred from nearby literals. Its order is an
invalidation declaration, not execution order: the fire-poison declaration starts with
6253, whereas its callback first reads the 6120 prohibition.

The private verifier rechecks original PE ownership, complete instruction bytes, range
hashes, registrations, dependency spans, table identities and bounded decompilation
identities. It also interprets selected decoded branch/register instructions against
synthetic cached stat inputs and compares all Boolean combinations with the equations;
signed/count boundary cases and short-circuit read traces are checked separately. This
is a static model check, **not** native execution or a server behavior experiment. Inline
cache-miss lookup mechanics retain the existing stat-evaluation report's boundary.

Canonical ignored evidence under `Tools/re/out/client-damage-audit/`:

- `r7-ailment-eligibility-formulas/`: `audit.json`, complete `.asm` exports, `mapping.json`,
  `dependencies.json`, `targets.json`, and `decompilations.jsonl`.
- `r7-ailment-eligibility-build.py`, `r7-ailment-eligibility-regdeps.py`,
  `R7EligibilityFunctions.java`, and `r7-ailment-eligibility-ghidra.log`.
- `r7-ailment-eligibility-verify.py` and `r7-ailment-eligibility-verification.json`.

No final infliction probability, RNG order, target avoidance, instance replacement,
reflection, duration sampling, DoT selection or shared-cap placement follows from these
Boolean outputs alone. Their native input composition is recovered; those consumers and
interactions remain separate contracts.
