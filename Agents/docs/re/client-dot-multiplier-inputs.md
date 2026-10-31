# Native common DoT multiplier inputs (3.29.3b)

## 1. Result and scope

The client builds generic, typed and ailment DoT multipliers as **additive signed integer
percentage-point contributions**, then combines them in distinct ailment and non-ailment
outputs. This pass recovers twelve registrations: the generic input, five typed inputs, one
ailment-only input and five non-ailment totals. It extends the already recovered
[ailment multiplier inputs](client-ailment-multiplier-inputs.md), without duplicating their
faster-damage, bleeding/ignite/poison or critical branches.

Two similarly worded resistance contributions have different integer operation order:
cold divides overcapped resistance by four before multiplying; chaos multiplies its inputs
before dividing by four. Their rounding and signed behavior must be preserved.

These callbacks evaluate effective context inputs. They do not prove when offence is sampled
into an instance, actual invocation on a network entity, a target-side publisher, shared rate
cap, elapsed-time integration or resource deduction. No target execution, attachment, new
capture or server change was performed.

| Artifact / method | Identity |
|---|---|
| Executable | `Client/Game.exe`, 3.29.3b x86-64, preferred base `0x140000000` |
| Executable SHA256 | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `data/tables/Stats.json` SHA256 | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| Stat mapping | Native key `k` = zero-based JSON row `k - 1`, field `Id`; 23,346 rows |
| Evidence | Original PE bytes, unwind-owned assembly, Capstone; isolated bounded Ghidra 12.1.4 typed export with `-noanalysis` |
| New batch | 12 registrations, 28 function records / ranges, 3,426 instruction records, no undecoded bytes; 12 completed selected decompilations |

## 2. Registration and arithmetic contract

| Key | Output | Registration | Callback |
|---:|---|---|---|
| 10297 | `combined_dot_multiplier_+` | `0x1400bf3d0` | `0x142770fd0` |
| 10298 | `combined_physical_dot_multiplier_+` | `0x1400bf450` | `0x142771810` |
| 10300 | `combined_fire_dot_multiplier_+` | `0x1400bf570` | `0x142771f70` |
| 10302 | `combined_cold_dot_multiplier_+` | `0x1400bf630` | `0x142772600` |
| 10304 | `combined_lightning_dot_multiplier_+` | `0x1400bf6a0` | `0x142772bc0` |
| 10306 | `combined_chaos_dot_multiplier_+` | `0x1400bf6f0` | `0x142772cc0` |
| 10337 | `combined_ailment_dot_multiplier_+` | `0x1400bf760` | `0x142773350` |
| 10299 | `total_non_ailment_physical_dot_multiplier_+` | `0x1400bf7c0` | `0x142773640` |
| 10301 | `total_non_ailment_fire_dot_multiplier_+` | `0x1400bf820` | `0x142773850` |
| 10303 | `total_non_ailment_cold_dot_multiplier_+` | `0x1400bf880` | `0x142773a60` |
| 10305 | `total_non_ailment_lightning_dot_multiplier_+` | `0x1400bf8e0` | `0x142773c70` |
| 10307 | `total_non_ailment_chaos_dot_multiplier_+` | `0x1400bf940` | `0x142773e80` |

`S(k)` means the current effective integer input after the native context/cache/upstream
lookup. `when(condition,value)` returns value when the condition is true and zero otherwise.
Every addition/multiplication retains the low 32 bits; interpret subsequent signed operations
accordingly. `trunc0(n/4)` is signed integer division toward zero. These twelve callbacks have
no floating-point factor conversion, saturation or final nonnegative clamp. A negative output
remains a signed percentage-point contribution for downstream consumers.

## 3. Generic multiplier 10297

```text
S(10297) = S(10328)
           + when(S(9025) != 0, S(10496))
           + when(S(7659) != 0, S(13472))
           + when(S(2989) != 0, S(14329))
           + when(S(438) != 0, S(21678))
           + when(S(4531) != 0, S(15380))
           + when(S(8243) != 0, S(11180))
           + when(S(20703) != 0, S(20894))
```

| Qualification | Contribution |
|---|---|
| 9025 `affected_by_delirium` | 10496 `dot_multiplier_+_while_affected_by_malevolence` |
| 7659 `have_crit_in_past_8_seconds` | 13472 `dot_multiplier_+_if_crit_in_past_8_seconds` |
| 2989 `skill_is_attack` | 14329 `damage_over_time_multiplier_+_with_attacks` |
| 438 `casting_spell` | 21678 `damage_over_time_multiplier_+_with_spells` |
| 4531 `have_killed_recently` | 15380 `damage_over_time_multiplier_+_if_enemy_killed_recently` |
| 8243 `skill_is_bow_skill` | 11180 `dot_multiplier_+_with_bow_skills` |
| 20703 `tincture_applied` | 20894 `damage_over_time_multiplier_+_with_tinctured_weapons` |

10328 is `dot_multiplier_+`. The aura-named term is tested against the actual internal key
9025; this report preserves that name mismatch instead of inventing a different condition.
The attack, spell and bow tests are independent. This body neither requires them to be
mutually exclusive nor substitutes a weapon-type check for `skill_is_bow_skill`.

The bow qualification is the actual read at `0x1427714fe`, even though 8243 is absent from
the declared registration dependency span. Final additions are
`0x1427717e6..0x1427717fe`. All seven qualified contributions are added once, including when
multiple conditions are simultaneously true. None is weighted by critical chance.

## 4. Five typed multipliers

### 4.1 Physical 10298

```text
weaponQualified = table[S(3)] != 0 or table[S(4)] != 0
S(10298) = S(10329)
           + when(weaponQualified, S(11281))
           + when(S(14115) != 0, S(14117))
           + when(S(4537) != 0, S(14118))
           + when(S(2989) != 0, S(14326))
```

3/4 are main/offhand weapon types. The original table is `0x142fdd6e0`; its eighteen
inspected bytes are `01 01 01 00 00 00 00 01 01 00 00 00 00 00 00 00 00 00`.
Thus indices **0, 1, 2, 7, 8** qualify. The main-hand table check at `0x142771918` can
short-circuit the offhand read. Two qualifying weapons still add the contribution once.
There is no Varunastra-flag read or table-index bounds guard in this inspected body; do not
extend the table by assumed weapon-name semantics. Invalid indices are outside this
defined-input reconstruction.

| Key | `Stats.Id` |
|---:|---|
| 10329 | `physical_dot_multiplier_+` |
| 11281 | `physical_dot_multiplier_+_while_wielding_axes_swords` |
| 14115 / 14117 | `life_spent_recently` / `physical_dot_multiplier_+_if_spent_life_recently` |
| 4537 / 14118 | `have_crit_recently` / `physical_dot_multiplier_+_if_crit_recently` |
| 14326 | `physical_damage_over_time_multiplier_+_with_attacks` |

Life spent is a **nonzero** test (`0x142771bab..0x142771bad`), not a positive threshold or
a multiplication by the amount spent. The critical-history key differs from generic 10297's
eight-second key. Final additions are `0x142771f4d..0x142771f5f`.

### 4.2 Fire 10300

```text
S(10300) = S(10330)
           + when(S(3650) != 0, S(11251))
           + when(S(2989) != 0, S(14327))
           + mul32(S(21875),S(20860))
```

10330 is `fire_dot_multiplier_+`; 3650 is `is_burning`; 11251 is
`fire_damage_over_time_multiplier_+%_while_burning`; 14327 is the fire multiplier with
attacks. 21875 is `fire_dot_multiplier_+_per_rage`, multiplied by 20860
`effective_rage_for_per_rage_effects`. The rage product is unconditional with no positive
rage-count guard. The burning input is also a nonzero test at `0x14277207e`.
Multiply/add tail: `0x1427725d6..0x1427725ee`.

### 4.3 Cold 10302

```text
S(10302) = S(8725)
           + when(S(9025) != 0, S(9306))
           + mul32(trunc0(S(15295)/4),S(21677))
           + mul32(S(7),S(22128))
```

8725 is `cold_dot_multiplier_+`; 9306 is the cold multiplier while affected by Malevolence,
qualified by the same actual key 9025. 15295 is `virtual_overcapped_cold_damage_resistance_%`;
21677 is `cold_damage_over_time_multiplier_+_per_4%_overcapped_cold_resistance`.
7/22128 are current power charges and the cold multiplier per power charge. Neither product
has a positive-count/resistance guard here. The supplied virtual resistance's own producer
is a separate upstream contract.

Division toward zero is implemented by `cdq; and edx,3; add eax,edx; sar eax,2` at
`0x142772b93..0x142772b99`, followed by multiplier `imul eax,ebp` at `0x142772b9c`.
It is not mathematical floor for negative inputs. The charge product precedes this division
at `0x142772b89`; final additions are `0x142772ba4..0x142772bae`.

### 4.4 Lightning 10304

```text
S(10304) = S(10331)
```

10331 is `lightning_dot_multiplier_+`. The one-input alias has no extra qualifications,
hand or critical branch. The fallback read is `0x142772c89`, with integer return preserved
through `0x142772cb3`.

### 4.5 Chaos 10306

```text
S(10306) = S(10332)
           + when(S(9025) != 0, S(9305))
           + when(S(2989) != 0, S(14328))
           + trunc0(mul32(S(15128),S(311))/4)
```

10332 is `chaos_dot_multiplier_+`; 9305 is the chaos multiplier while affected by
Malevolence, qualified by 9025; 14328 is the chaos multiplier with attacks.
15128 is `chaos_damage_over_time_multiplier_+_per_4_chaos_resistance`; 311 is the effective
`chaos_damage_resistance_%` input. This callback does not read overcapped chaos resistance,
maximum resistance, enemy resistance or penetration.

Unlike cold, `imul ebx,esi` at `0x142773319` computes the signed32 product **before** the
division sequence `0x142773328..0x14277332e`. Negative resistance can therefore contribute
a negative integer amount; no local zero clamp is present. Final additions are
`0x142773331..0x14277333b`.

For coefficient 3 and supplied resistance 6, cold contributes `trunc0(6/4)*3 = 3`, whereas
chaos contributes `trunc0((3*6)/4) = 4`. At -6 the corresponding contributions are -3 and
-4. Treating both as a generic “count groups of four” implementation changes the latter.

## 5. Ailment-only input and non-ailment totals

```text
S(10337) = S(10377) + mul32(S(10495),S(7174))

S(10299) = S(10297) + S(10298)
S(10301) = S(10297) + S(10300)
S(10303) = S(10297) + S(10302)
S(10305) = S(10297) + S(10304)
S(10307) = S(10297) + S(10306) + S(8724)
```

10377 is `ailment_dot_multiplier_+`, 10495 is the ailment multiplier per equipped Elder
item, and 7174 is `number_of_equipped_elder_items`. There is no positive-count guard.
Multiply/add sites: `0x142773618..0x14277361b`.

The first four non-ailment totals are exactly two-input sums, with final additions at
`0x142773827`, `0x142773a37`, `0x142773c47`, `0x142773e53`. Chaos additionally reads
8724 `chaos_non_ailment_damage_over_time_multiplier_+`, then sums at
`0x14277414b..0x142774153`. None adds ailment-only 10337 or a critical-ailment multiplier.

The existing 10338/10339/10340 ailment builders instead add generic 10297, the appropriate
typed multiplier and ailment-only 10337, alongside their ailment-specific terms. A port must
choose the correct combined output and avoid reapplying its shared inputs. This is a
dependency structure, not evidence that instance offence dynamically updates after creation.

## 6. Verification and remaining upstream work

Ignored evidence under `Tools/re/out/client-damage-audit/`:

- `r8-dot-multiplier-family/`: original-byte audit, exact registry mapping, reconstructed
  60 declared dependencies, bounded targets, untouched typed exports, assembly and console log.
- `r8-dot-multiplier-verify.py` and `r8-dot-multiplier-verification.json`: complete fresh
  original-byte/owned-instruction comparisons, 12 registration argument/dependency dataflows,
  all effective-input IDs, selected branch/arithmetic anchors, weapon-table bytes, typed
  export identity, signed/integer-order reconstruction examples and public-path checks.
- `r8-dot-multiplier-prepare.py`, `r8-dot-multiplier-regdeps.py`,
  `r8-dot-multiplier-decompile.py`, `R8DotMultiplierFunctions.java`: reproduction scripts.

Scope is the twelve explicit registered callbacks in this pinned artifact. Upstream production
of weapon/classification/history flags, effective rage, virtual overcapped resistance,
resistance itself and equipped-item counts remains separate. This report does not reconstruct
the entire generic increased/final damage families. It also does not close context
invalidation, instance sampling, target DoT arbitration, elapsed-time integration or cap logic.
