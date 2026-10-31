# Native ailment speed and multiplier inputs (3.29.3b)

## 1. Result and scope

Eight native registrations recover the input composition feeding the previously documented
[outgoing ailment rates](client-ailment-outgoing-formulas.md): three faster-damage outputs,
three combined ailment DoT multipliers, and two critical-ailment outputs sharing one callback.
Faster inputs add as integer percentage deltas **within each virtual faster output**. That does
not turn ignite's separately applied slower divisor into an additive subtraction.

The callbacks evaluate current context inputs. They do not construct an ailment instance,
sample offence on application, advance a duration, select the strongest stack, or aggregate
target-side damage. Those are separate contracts. The native versus network-provider boundary
in [stat evaluation](client-stat-evaluation.md) still applies.

| Artifact | Identity |
|---|---|
| Executable | `Client/Game.exe`, x86-64, 3.29.3b, preferred base `0x140000000` |
| Executable SHA256 | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `data/tables/Stats.json` SHA256 | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| Stat mapping | Native key `k` = JSON row `k - 1`, field `Id` |
| Evidence | Original PE bytes / unwind-owned assembly; bounded Ghidra 12.1.4 export |
| Execution | Static/offline only; no client execution, attachment or new capture |

Ignored canonical batch `r6-ailment-multiplier-inputs` contains 15 function records / 15 ranges /
2,817 instructions, with no undecoded bytes. Nine selected decompilations completed, including
two shared helpers. The eight registration dependency spans are reconstructed separately from
their original stack/vector writes; dependency lists are not exhaustive callback-read lists.

## 2. Registration and arithmetic

| Output | Registration | Callback |
|---|---|---|
| 6203 `virtual_ignite_faster_burn_%` | `0x1400d5e30` | `0x142856610` |
| 9319 `virtual_faster_poison_%` | `0x1400db330` | `0x14288f2f0` |
| 9320 `virtual_faster_bleed_%` | `0x1400db390` | `0x14288f6d0` |
| 10338 `combined_all_dot_multipliers_for_bleeding_+` | `0x1400bf9f0` | `0x142774170` |
| 10339 `combined_all_dot_multipliers_for_ignite_+` | `0x1400bfa70` | `0x142774bb0` |
| 10340 `combined_all_dot_multipliers_for_poison_+` | `0x1400bfb20` | `0x142774f70` |
| 10333 `combined_main_hand_critical_ailment_dot_multiplier_+` | `0x1400bea20` | `0x14276c1a0` |
| 10335 `combined_spell_critical_ailment_dot_multiplier_+` | `0x1400beac0` | `0x14276c1a0` |

`S(k)` denotes one effective signed integer input, after the callback's cache/native-record/
upstream lookup. `when(c,x)` is `x` when condition `c` is true, otherwise zero. Additions and
multiplications below retain their low 32 bits. These callbacks have no floating-point percent
conversion, final saturation, or generic nonnegative clamp; their downstream consumers apply
their own factor conversion and rounding. A negative contribution is not silently discarded.

## 3. Virtual faster-damage inputs

Define `AuraSpeed = when(S(9025) != 0, S(9310))`. Preserve both actual internal names:
9025 is `affected_by_delirium`, whereas 9310 is
`your_ailments_deal_damage_faster_%_while_affected_by_malevolence`. This report does not rename
the tested stat or infer a different aura qualification from its display wording.

```text
S(6203) = S(1335) + S(10766) + AuraSpeed
          + when(S(2989) != 0, S(1651))
S(9319) = S(9225) + S(10766) + AuraSpeed
S(9320) = S(9226) + S(10766) + AuraSpeed + S(12212) * S(6)
```

| Key | `Stats.Id` |
|---:|---|
| 1335 | `faster_burn_%` |
| 1651 | `faster_burn_from_attacks_%` |
| 2989 | `skill_is_attack` |
| 9225 / 9226 | `faster_poison_%` / `faster_bleed_%` |
| 10766 | `damaging_ailments_deal_damage_+%_faster` |
| 12212 / 6 | `faster_bleed_per_frenzy_charge_%` / `current_frenzy_charges` |

The attack-only burn term is omitted for a non-attack. Bleeding multiplies the frenzy count
and per-charge input before addition, without the positive-count test found in the separate
endurance multiplier below. Ignite's terminal additions are `0x142856b8b..0x142856b9a`, poison's
are `0x14288f6ab..0x14288f6b3`, and bleeding's multiply/add tail is
`0x14288fc26..0x14288fc3e`. The current attack classification read in the ignite callback is
not listed in its declared dependency span.

For example, generic faster 20, ailment faster 10, enabled aura faster 15 and attack-only burn
30 produce ignite faster 75 for an attack, 45 otherwise. Downstream key 6158 then composes
`F(6203) / (1 + S(6577)/100)` with other final factors and rounds the combined modifier before
the typed rate applies it. With no other factors, 25 faster / 50 slower gives `1.25/1.50`,
publishes -17 after rounding, and the typed formula applies 0.83. Multiplying an unrounded
5/6 directly into the rate would skip that recovered intermediate publication boundary.

## 4. Combined ailment DoT multipliers

The common input families are 10297 `combined_dot_multiplier_+`, 10337
`combined_ailment_dot_multiplier_+`, and typed multipliers 10298 physical, 10300 fire,
10306 chaos. These are added percentage-point multiplier contributions; they are not factors
multiplied together in these callbacks.

```text
EnduranceBleed = when(S(5) > 0, S(15376) * S(5))
AxeBleed = when(S(3) == 7 or S(4) == 7, S(12141) * S(20860))
S(10338) = S(10297) + S(10298) + S(10337) + S(10341)
           + S(12012) + EnduranceBleed + AxeBleed

S(10339) = S(10297) + S(10300) + S(10337) + S(10342)

WeaponPoison = when(weaponQualified, S(11250))
SpellPoison  = when(S(438) != 0, S(11570))
S(10340) = S(10297) + S(10306) + S(10337) + S(10343)
           + S(10837) * S(6) + WeaponPoison + SpellPoison
```

| Key | `Stats.Id` |
|---:|---|
| 3 / 4 | `main_hand_weapon_type` / `off_hand_weapon_type` |
| 5 / 15376 | `current_endurance_charges` / `bleeding_dot_multiplier_+_per_endurance_charge` |
| 12141 / 20860 | `bleed_dot_multiplier_+_per_rage_if_equipped_axe` / `effective_rage_for_per_rage_effects` |
| 10341 / 10342 / 10343 | `bleeding_dot_multiplier_+` / `ignite_dot_multiplier_+` / `poison_dot_multiplier_+` |
| 12012 | `virtual_bleeding_dot_multiplier_per_frenzy_charge_+` |
| 10837 | `poison_dot_multiplier_+_per_frenzy_charge` |
| 11250 | `posion_damage_over_time_multiplier_+%_while_wielding_claws_daggers` |
| 438 / 11570 | `casting_spell` / `poison_dot_multiplier_+_with_spells` |

Bleeding's axe qualification is the literal numeric comparison with **7**, at
`0x14277431e..0x14277432d`; it is not a general weapon-name or Varunastra test. Do not widen it
to every enum value called an axe elsewhere. Its positive endurance-count branch is
`0x1427743ee..0x1427743f0`. Key 12012 is already a virtual contribution: this callback adds it
once and does not multiply it by frenzy count again. Effective rage 20860 is an actual read
omitted from the declared registration dependency span.

For poison, `weaponQualified` is true if either weapon type indexes a nonzero byte in
`0x142fdd710`, or either Varunastra flag 4198/4199 is nonzero. The 18 inspected bytes have
nonzero entries only at **10, 12, 16**. This table supplies a boolean, not a multiplier per
matching hand. Two qualifying weapons still contribute 11250 once. The spell term uses
`casting_spell`, not `skill_is_attack` or an assumed negation of it. The conditional terms can
both contribute when both supplied qualifications are true. Invalid table indices are outside
the defined-input reconstruction; the native body supplies no bounds guard here.

The terminal sum ranges are `0x142774b83..0x142774b9c` (bleeding),
`0x142774f4e..0x142774f5d` (ignite), and `0x142775b3b..0x142775b4d` (poison).
The downstream critical/noncritical callbacks add hand/local contributions and Perfect Agony
branches as documented in the outgoing-rate report; do not apply these combined inputs twice.

## 5. Shared critical-ailment input

Both 10333 and 10335 register the same callback, `0x14276c1a0`. Its effective read and return are:

```text
S(10333) = S(6175)
S(10335) = S(6175)
```

6175 is `critical_ailment_dot_multiplier_+`. No hand, spell, critical-chance or target input is
tested in that body. Two output names do not prove distinct arithmetic. The callback's local
cache write is at `0x14276c274`; the two registrations declare the same one-key dependency.
Actual per-event critical eligibility remains outside this calculation.

## 6. Evidence and remaining work

Private `r6-ailment-multiplier-inputs-verify.py` rechecks complete original owned instruction
records, registration keys/callbacks, dependency-span reconstruction, input identities,
conditional arithmetic anchors, the weapon-qualification bytes and selected decompilation
coverage. Reconstruction examples check the documented algebra; they do not execute the client.

These findings close the eight selected input formulas. Remaining upstream generic/typed
multiplier production, maximum/offhand ailment rate families, instance sampling, target
selection/aggregation, and the concrete source-to-incoming-rate publisher remain separate work.
See [instance rules](client-dot-instance-rules.md) for versioned external evidence about
application-time offence and later target/clock changes.
