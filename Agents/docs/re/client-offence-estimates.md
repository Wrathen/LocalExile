# Client offence estimates and numerical UI consumers (3.29.3b)

## 1. Result and evidence boundary

The client contains registered native formula callbacks for minimum and maximum physical, fire,
cold, lightning and chaos damage for both hands, plus base/added components, hand totals, average
damage and DPS. Two numerical UI updaters actually read the selected skill's average/DPS values,
format them, and assign the result to a text widget. Their provider is the ordinary context-zero
skill provider, whose network ownership and fallback chain is established in
[the stat-context audit](client-stat-evaluation.md). The UI does not directly call the registered
native formula callbacks.

This closes the R3 panel/offence family and its provider identity. It does not turn the native
registration into evidence of ordinary network-skill recomputation. The separate native transaction
and its startup eligibility are covered by R1. These callbacks compute deterministic estimates;
the audited bodies do not select a random hit, inspect a defender's mitigation, or deduct a resource.
Those are separate questions in [the main investigation](client-damage-calculation.md).

All observations below are static, offline artifact evidence. No client was launched, attached,
hooked or controlled. Addresses are preferred image virtual addresses; names are analyst labels.

| Input | Identity |
|---|---|
| Executable | `Client/Game.exe`, PE x86-64, 3.29.3b |
| Preferred image base | `0x140000000` |
| Executable SHA256 | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `data/tables/Stats.json` SHA256 | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| Key mapping | Native/wire key `k` is JSON row `k - 1`, field `Id`; 23,346 rows |
| Byte audit | SHA-bound PE bytes, chained unwind ownership, Capstone 5.0.3 / pefile 2024.8.26 |
| Selected decompilation | Ghidra 12.1.4 / JDK 21.0.12.1; offline import, `-noanalysis` |

The ignored evidence root is `Tools/re/out/client-damage-audit/`. The `r3-offence-formulas` batch
audits 75 registrations and their 75 callbacks; all 75 callbacks decompiled successfully.
`r3-offence-typed` retains a second, 81-function successful decompilation covering those
75 callbacks, three UI candidates and three scalar/cache helpers. This pass supplies verified
signatures for `0x141d1e400` (float return, integer argument), `0x142513810` (integer return, float
argument) and `0x142513520` (integer stat-cache getter). The original instructions remain authoritative:
without the float signature, Ghidra can silently lose converted-damage SSE expressions. Compact
pseudocode files are navigation aids, not independent evidence. Its `provenance.json` identifies
the original byte-audit batches, exact supplied signatures and output hash. The first-pass
`r3-offence-formulas` output is retained unchanged; `r3-offence-scalar-helpers` retains the three
original helper byte audits and the original combined target specification.

## 2. Numeric conventions and native registration

Here `S(k)` denotes a read from the callback's native evaluation context. The getter first uses a
valid cached value; otherwise it reads the native record provider through `0x1424fc1e0`, then caches
that value and its valid bit. The reusable form is `0x142513520`. This is a read of native records,
not recursive formula dispatch. Registration, dispatch and publication remain separate operations.

Define:

```text
P(n) = max(0, float(n) / 100)
F(n) = P(n + 100)
R(x) = trunc0(x + (x < 0 ? -0.5f : +0.5f))
```

`0x141d1e400` implements `P`: integer-to-float conversion at `0x141d1e404..0x141d1e40d`, division
by 100, max with zero, and float return at `0x141d1e430`. The explicit leaf
`0x142513810..0x14251383a` implements signed half-away-from-zero rounding using `cvttss2si` at
`0x142513824` / `0x142513835`. It does not saturate; the adjacent function at `0x142513840` is
different. Constants at `0x1436a3adc`, `0x1436a31a4`, `0x1436a2f80`, `0x1436a49a0` are respectively
100, 1, +0.5 and -0.5. Critical probability denominator `0x1436a47fc` is 10,000.

The formula registrations pass output key in `edx`, callback in `r8`, and dependency span in `r9`
to `0x142513630`. For example, `0x1400982a0` passes key 279 at `0x1400982cb`, callback
`0x1425733e0` at `0x1400982bf`, and registers at `0x14009832b`. DPS registration
`0x1400c2430` passes key 679 at `0x1400c2471`, callback `0x14278f2f0` at `0x1400c245d`, and calls
the same registrar at `0x1400c2481`. These are actual callback/dependency registrations, not
references to localized stat names.

The equations below preserve the recovered component boundaries and rounding sites. They are an
algebraic description, not a bit-exact port: integer additions/multiplications can overflow, SSE
operation order affects floats, and out-of-range float-to-integer conversion is not saturation.
The [complete ordered attack equations](client-attack-hit-formulas.md) now retain those operation
parentheses for all twenty typed endpoints and four hand totals, with a separate factor census.

## 3. Both hands, all five types, minimum and maximum

| Hand / type | Minimum key / callback / registration | Maximum key / callback / registration |
|---|---|---|
| MH physical | 279 / `0x1425733e0` / `0x1400982a0` | 280 / `0x142574980` / `0x140098340` |
| MH fire | 267 / `0x142575f20` / `0x1400983e0` | 268 / `0x142577b20` / `0x140098500` |
| MH cold | 263 / `0x142579720` / `0x140098620` | 264 / `0x14257b140` / `0x140098710` |
| MH lightning | 271 / `0x14257cb70` / `0x140098800` | 272 / `0x14257e320` / `0x1400988d0` |
| MH chaos | 275 / `0x14257fae0` / `0x1400989a0` | 276 / `0x142581c90` / `0x140098ae0` |
| OH physical | 281 / `0x142583e40` / `0x140098c20` | 282 / `0x1425853e0` / `0x140098cc0` |
| OH fire | 269 / `0x142586980` / `0x140098d60` | 270 / `0x142588580` / `0x140098e80` |
| OH cold | 265 / `0x14258a180` / `0x140098fa0` | 266 / `0x14258bba0` / `0x140099090` |
| OH lightning | 273 / `0x14258d5c0` / `0x140099180` | 274 / `0x14258ed70` / `0x140099250` |
| OH chaos | 277 / `0x142590520` / `0x140099320` | 278 / `0x1425926c0` / `0x140099460` |

The twenty callbacks share a verified structure, with the substitutions shown below. For each hand
and endpoint, first form a raw amount for each type from that hand's base and added components:

```text
B = F(trunc0(S(6172) / 100))
A = F(S(5761))
raw[type] = float(base[type]) * B + float(added[type]) * A
```

The division of key 6172, `active_skill_base_attack_damage_final_+permyriad`, is signed integer
division **before** float conversion and `F`; the physical-minimum instructions are
`0x142573704..0x14257372f`, followed by base/added multiplication at `0x14257391d..0x142573944`.
This is not the same operation as a direct floating division by 10,000.

The conversion graph is acyclic in this order:

```text
physical -> lightning -> cold -> fire -> chaos
```

Every forward edge also exists directly. Its coefficient is `P(S(key))`:

| From | To fire | To cold | To lightning | To chaos |
|---|---|---|---|---|
| Physical | 1513 | 1514 | 1515 | 1516 |
| Lightning | 1517 | 1518 | — | 1519 |
| Cold | 1520 | — | — | 1521 |
| Fire | — | — | — | 1522 |

For an output type, the callback sums every increasing path that ends in that type, including the
zero-edge path from its own raw amount. The resulting term counts are physical 1, lightning 2,
cold 4, fire 8 and chaos 16. Each term uses its starting raw amount, all edge coefficients on the
path, one combined increased factor, and the product of applicable final factors. Type-specific
groups apply once for each visited type; the elemental group applies once if any visited type is
elemental. The common group always applies once:

| Scope | Increased keys, summed inside one `F` | Final keys, each a separate `F` |
|---|---|---|
| Common MH | 1426, 5762, 1430, 1432 | 1427, 5103, 1431, 15983, endpoint key |
| Common OH | 1426, 5762, 1430, 1433 | 1427, 5103, 1431, 15984, endpoint key |
| Physical MH / OH | 1434, 1436, 1438 / 1439 | 1435, 1437, 1440 / 1441 |
| Elemental MH / OH | 1450, 5985, 1463 / 1464 | 1451, 11028, 1465 / 1466 |
| Fire MH / OH | 1442, 1452, 1467 / 1468 | 1443, 1453 |
| Cold MH / OH | 1444, 1454, 1469 / 1470 | 1445, 1455 |
| Lightning MH / OH | 1446, 5986, 1471 / 1472 | 1447, 5987 |
| Chaos MH / OH | 1448, 1473 / 1474 | 1449 |

The endpoint key is 19089 for minimum and 19090 for maximum. For clarity, `1438 / 1439` means use
1438 for MH or 1439 for OH, not division. Each path's increased factor is a single `F` of the sum
of common and visited-scope increased values; these are not multiplied as separate `F` factors.

Each output uses one destination remainder `P(100 - S(lostKey))`, with lost keys physical 1502,
fire 1503, cold 1504, lightning 1505, chaos 1506. The converted terms do not multiply the lost
fraction of every intermediate type. All sixteen attack chaos paths retain this destination remainder;
the [spell longest-path exception](client-spell-hit-formulas.md#the-longest-chaos-path-omits-the-destination-remainder)
must not be imported here. Only lightning output applies an additional factor **0.5 for minimum
and 1.25 for maximum** when key 23310 (`keystone_lightning_purist`) is true, otherwise 1.
This corrects the earlier endpoint-independent 0.5 summary. The fire/cold/chaos callbacks do not
read 23310, so those output-specific factors must not be inserted into their conversion paths.

The callback returns zero immediately when its output's `deal_no_*_damage` key is nonzero: MH
physical/fire/cold/lightning/chaos use 1733..1737; OH uses 1738..1742. It does not test all source
type gates on converted terms. Otherwise it rounds the final sum once and clamps the integer to
at least zero. There is no intermediate integer rounding for each conversion edge. For example:

```text
MH physical minimum = max(0, R(
  raw[physical] * P(100-S(1502))
  * F(S(1426)+S(5762)+S(1430)+S(1432)+S(1434)+S(1436)+S(1438))
  * F(S(1435))*F(S(1437))*F(S(1440))
  * F(S(1427))*F(S(5103))*F(S(1431))*F(S(15983))*F(S(19089))))
```

The corresponding physical tail is `0x1425748b0..0x142574969`. The fire-minimum callback
`0x142575f20` explicitly retains its eight terms through its final `0x142513810` call; cold minimum
`0x142579720` has four, lightning minimum `0x14257cb70` has two, and chaos minimum
`0x14257fae0` has sixteen. Their maximum/OH bodies were audited individually, including their
distinct base/added pairs, hand modifiers, output gates and 19089/19090 endpoints.

Hand totals have no new scaling or rounding: 670/671 are the sums of the corresponding five MH
typed integers; 672/673 sum the OH integers. Callbacks are respectively `0x142543a80`,
`0x142543f20`, `0x1425443c0`, `0x142544860`; registrations are `0x140096290`, `0x1400962f0`,
`0x140096350`, `0x1400963b0`. Their ordinary 32-bit addition must not be replaced by a presumed cap.

## 4. Base and added components

All entries give `key @ callback`; minimum precedes maximum. Their exact registrations are retained in the SHA-bound mapping described in section 1.

| Hand / type | Base minimum / maximum | Added minimum / maximum |
|---|---|---|
| MH physical | 1563 @ `0x142594860`; 1564 @ `0x142594e30` | 1575 @ `0x1425988a0`; 1576 @ `0x14259aad0` |
| MH fire | 1565 @ `0x1425958c0`; 1566 @ `0x142595b10` | 1579 @ `0x14259cf50`; 1580 @ `0x14259ece0` |
| MH cold | 1567 @ `0x142595d60`; 1568 @ `0x142595fb0` | 1583 @ `0x1425a0a70`; 1584 @ `0x1425a1bc0` |
| MH lightning | 1569 @ `0x142596200`; 1570 @ `0x142596450` | 1587 @ `0x1425a2d00`; 1588 @ `0x1425a41a0` |
| MH chaos | 1571 @ `0x1425966a0`; 1572 @ `0x142596790` | 1591 @ `0x1425a5e70`; 1592 @ `0x1425a75c0` |
| OH physical | 1593 @ `0x142596880`; 1594 @ `0x142596e50` | 1605 @ `0x1425a8d10`; 1606 @ `0x1425aaf60` |
| OH fire | 1595 @ `0x1425978e0`; 1596 @ `0x142597b30` | 1609 @ `0x1425ad3f0`; 1610 @ `0x1425af0c0` |
| OH cold | 1597 @ `0x142597d80`; 1598 @ `0x142597fd0` | 1613 @ `0x1425b0d90`; 1614 @ `0x1425b20f0` |
| OH lightning | 1599 @ `0x142598220`; 1600 @ `0x142598470` | 1617 @ `0x1425b3450`; 1618 @ `0x1425b4970` |
| OH chaos | 1601 @ `0x1425986c0`; 1602 @ `0x1425987b0` | 1621 @ `0x1425b6510`; 1622 @ `0x1425b7c60` |

### Local weapon base

For physical base, MH local minimum/maximum input keys are 794/795 and local increased physical
key 793; OH substitutes 816/817 and 815. Multiply by `F(local physical)` and by `F(quality)`:
quality is MH 328 or OH 329, except that MH 12491 / OH 12492 makes its quality contribution zero.
MH 9431 / OH 9432 returns zero before these calculations. The component then uses `R`, without
the typed-output nonnegative clamp.

When `skill_is_attack` (2989) is true, minimum physical base additionally multiplies `F(S(5795))`.
Maximum multiplies `F(S(5796))*F(S(18593))*F(S(19769))`; it also multiplies `F(S(15512))` if its
weapon-type key (MH 3, OH 4) is 10 or 16, or its counts-as-all-one-handed-types key (MH 4198,
OH 4199) is nonzero. These are the maximum-only dagger/mastery conditions, not a general minimum
modifier.

Fire/cold/lightning base components use the corresponding local input, MH 796..801 / OH 818..823,
multiply by `F(S(20925))` / `F(S(20926))` (`*_local_elemental_damage_+%`), then `R`. Chaos base
passes through MH 802/803 or OH 824/825; it does not use that elemental multiplier. This stage's
physical-only quality and endpoint factors must not be applied to every elemental base component.

### Added-type selection

The intermediate added inputs for MH physical/fire/cold/lightning/chaos minimum are
1573/1577/1581/1585/1589; maximum uses each key plus one. OH substitutes
1603/1607/1611/1615/1619, likewise plus one for maximum. These are stat-provider inputs to the
audited component callbacks, not constants inferred from a weapon label.

Key 22775, `all_added_damage_treated_as_x_damage_type`, selects how they contribute:

| Destination | Selector value | Extra endpoint input |
|---|---|---|
| Physical | 1 | None |
| Cold | 2 | 444 / 445 |
| Fire | 3 | 442 / 443 |
| Lightning | 4 | 446 / 447 |
| Chaos | 5 | None |

When selector is zero, each component reads its matching intermediate input. When selector equals
the destination value, it sums all five intermediate inputs for that endpoint/hand. For every
other selector it contributes zero from those intermediates. Fire/cold/lightning always also add
the listed minimum/maximum `attack_*_added_*_damage_for_elemental_hit` input, even when selector
chooses another type. Those elemental component callbacks return the integer sum directly.

Physical added components multiply their selected sum by the attack-only minimum factor
`F(S(5795))`, or maximum factors `F(S(5796))*F(S(18593))*F(S(19769))`, then `R`. Unlike maximum
physical base, they do not use the dagger factor 15512. Chaos directly returns its selected integer
input/sum. The same selection algorithm was checked for both hands and endpoints.

### Effectiveness

Registration `0x14009b960` installs key 5761, `combined_all_added_damage_+%_final`, callback
`0x1425b93b0`:

```text
S(5761) = R(100 * F(S(891))*F(S(3079))*F(S(13051))*F(S(13196)) - 100)
```

Its input IDs are respectively `active_skill_gem_added_damage_effectiveness_+%_final`,
`minion_added_damage_+%_final_from_skill`, `added_damage_+%_final`, and
`active_skill_added_damage_+%_final`. This is rounded at the effectiveness component before the
typed formula uses `F(S(5761))`. R1 separately traces the table producer's key 891 and 6172 scaling;
that local parsing path is not the normal network skill wrapper.

## 5. Average damage, rate and display selection

| Key / Stats ID | Callback | Registration |
|---|---|---|
| 7794 `hundred_times_average_damage_per_spell_hit` | `0x14278b7a0` | `0x1400c20a0` |
| 7795 `hundred_times_average_damage_per_secondary_hit` | `0x14278bf10` | `0x1400c2110` |
| 7796 `hundred_times_average_damage_per_main_hand_hit` | `0x14278c5c0` | `0x1400c2180` |
| 7797 `hundred_times_average_damage_per_off_hand_hit` | `0x14278d100` | `0x1400c2200` |
| 2041 `hundred_times_average_damage_per_hit` | `0x14278dc00` | `0x1400c2280` |
| 2042 `hundred_times_average_damage_per_skill_use` | `0x14278e940` | `0x1400c2300` |
| 2144 `skill_show_average_damage_instead_of_dps` | `0x14278eb50` | `0x1400c2360` |
| 2044 `display_hundred_times_damage_per_skill_use` | `0x14278f0f0` | `0x1400c23d0` |
| 679 `hundred_times_damage_per_second` | `0x14278f2f0` | `0x1400c2430` |
| 678 `hundred_times_attacks_per_second` | `0x142789fd0` | `0x1400c1ea0` |

MH average 7796 requires both attack flag 2989 and `skill_uses_main_hand` 7792. OH average 7797
requires 2989 and `skill_uses_off_hand` 7793. The MH expression is:

```text
mean = float(S(670)+S(671)) * 0.5
p = float(S(23239)) / 10000
choice = S(8138) != 0 && S(8139) > 1 ? 1.0 / float(S(8139)) : 1.0
MHavg = R((mean*F(S(1757))*(1-p) + mean*P(S(610))*p)
          * 100 * choice * P(S(676)))
```

Key 23239 is `main_hand_effective_total_chance_permyriad_for_hit_to_be_critical`, 610 is
`main_hand_critical_strike_multiplier`, 1757 is `non_critical_damage_multiplier_+%`, 676 is
`main_hand_chance_to_hit_%`. Keys 8138/8139 are `elemental_hit_no_physical_chaos_damage` and
`elemental_hit_number_of_options`; this is an option-count adjustment, not a sampled choice.

OH substitutes totals 672/673, critical probability 23240, multiplier 611 and chance-to-hit 677.
The literal OH result has **no `choice` division**. Its body reads 8138/8139, but those reads do
not enter its returned float expression. The original OH instructions contain divisions for the
critical denominator and percentage factors, but no reciprocal of 8139. Do not impose MH/OH
symmetry when reconstructing this artifact.

Average per hit 2041 first selects secondary average 7795 when 1675
(`display_skill_deals_secondary_damage`) is true, or spell average 7794 when 438 (`casting_spell`)
is true. Otherwise it requires attack flag 2989 and selects the active hand. If both hand-use flags
are true, it returns `R(float(S(7796)*(S(10402)+1)+S(7797))/float(S(10402)+2))`, with
10402 `additional_main_hand_hits_per_combo_average`. The numerator's multiply/add is integer
before conversion. If neither hand is active, the selector returns zero. Average per use 2042 is ordinary integer
`(S(1124)+1)*S(2041)`, where 1124 is `skill_number_of_additional_hits`.

APS key 678 requires attack flag 2989. If main hand is active and key 19811
(`dual_strike_off_hand_weapon_determines_attack_time`) is false, its duration is 1116 when only
MH is active, or `float(S(1116)+S(1117))*0.5` when both are active. Otherwise OH must be active
and duration is 1117. It returns `R((1000.0/duration)*100)`; 1116/1117 are hand attack durations
in milliseconds. No new zero-duration guard or cap is established by this body.

Show-average flag 2144 is true when 2043 is nonzero, 2535 is positive, 8292 is nonzero, or when
2982 is nonzero and either 2378 or 5416 is zero. These keys mean base show-average, trigger count,
instant skill, Vaal skill, totemified, and totem ignores Vaal cost. Display average 2044 returns
2042 if 2144 is true, otherwise zero. DPS 679 returns zero if 2144 is true; otherwise it returns
`R((float(S(438) ? S(682) : S(678))/100)*float(S(2042)))`. Rate 682 is the spell-rate input.
The displayed average and DPS values are hundredths; the numerical UI multiplies them by 0.01.

## 6. Actual numerical UI consumers and their provider

Two independently audited UI update bodies consume these values. Their full concrete widget class
names have not been assigned; the operations below establish numerical consumption without relying
on a guessed panel name.

| Operation | Updater `0x140826070` | Updater `0x140ad5f30` |
|---|---|---|
| Select context-zero skill provider | `0x140826535..0x14082653a`, then `0x140826553..0x140826558` | `0x140ad5fe3`, then `0x140ad6000` |
| Virtual getter for 2144 | `0x140826545..0x14082654d` | `0x140ad5fee..0x140ad5ff6` |
| Choose 2044 when true, otherwise 679 | `0x140826563..0x140826570` | `0x140ad600b..0x140ad6017` |
| Actual virtual numerical getter | `0x140826576` | `0x140ad601d` |
| Convert result and multiply 0.01 | `0x140826579..0x140826580` | `0x140ad6020..0x140ad602f` |
| Positive-value branch / formatter | `0x14082658e`, `0x1408265a4` | `0x140ad603b`, `0x140ad6050` |
| Numerical widget / text interface | Owner `+0x3a0`, interface `+0x298` | Owner `+0x318`, interface `+0x298` |
| Assign formatted numerical string | `0x140826674..0x14082668d` | `0x140ad6133..0x140ad614b` |
| Nonpositive value hides widget | `0x1408266e7..0x1408266f7` | `0x140ad61a4..0x140ad61b4` |

Both use accessor `0x1400f3a50(skill, 0)`, which calls `0x141ee7e70`: context zero returns the
skill's primary shared provider at `+0x138/+0x140`. R1 proves the normal network skill constructor
creates this wrapper with `0x1424cf8d0`, inheriting its owner's Stats `+0x40/+0x48`; additional
contexts inherit that primary wrapper. Its virtual getter is `0x1424cfa40`: explicit local pair
wins (including zero), absent key delegates upstream. The getter contains no native callback
dispatcher or formula transaction. Reading numerical 679/2044 therefore establishes a consumer
of the provider's current values; it does not establish evaluation of `0x14278f2f0` /
`0x14278f0f0` at this call site.

Updater `0x140ad5f30` also checks/clears owner dirty byte `+0x378` at
`0x140ad5f67` / `0x140ad5f74` and retains the selected skill from owner `+0x360`. This is a UI
refresh condition, not the native stat engine's dirty-key transaction. Direct callers include
`0x140ad5ed0` at `0x140ad5ef7` and `0x140aeac60` at `0x140aeadfa`; the first updater is called
from `0x140825cf0` at `0x140825e3f`.

Nearby label operations are deliberately separated from that numerical chain. In the first updater,
owner `+0x398` receives a localized DPS/average label through `0x1421fdc40`, then owner `+0x3a0`
receives the formatted numeric value. In the second, those fields are `+0x310` and `+0x318`.
`0x1421fdc40` builds a localized UTF-16 string through `0x1421fd1f0` / `0x140143ce0`; its integer
argument alone is not proof of a stat getter. The 670/671 references in `0x140a6bbd0`, 279 reference
in `0x142410df0`, and 7796/7797 references in `0x1405a8060` feed that label producer. They were
checked and are not the numerical/provider closure used above.

## 7. Reuse and limits

Retained byte evidence is grouped as `r3-offence-formulas`, `r3-offence-scalar-helpers`, `r3-offence-typed`,
`r3-offence-numeric-ui`, `r3-offence-panel-bind` and `r3-offence-panel-values` under the ignored
audit root. `r3-offence-formulas/mapping.json` records each exact output key, Stats ID, registration
and callback; each audit records instruction bytes, ownership/ranges and per-range hashes.
The existing [offline audit tooling](../../../scripts/re/damage_audit.py) and
[bounded Ghidra exporter](../../../scripts/re/ghidra/BoundedFunctions.java) reproduce the byte and
selected-function extraction. The helper-signature correction described above must accompany any
reproduction that relies on pseudocode for the conversion expressions. The ignored
`r3-offence-verify.py` verifies all 75 key/callback registration pairs, original byte-range hashes,
successful decompilation coverage, report links and whitespace, and writes
`r3-offence-verification.json` with the correction-script/target/provenance hashes.

Established here: all twenty hand/type/endpoint native callbacks, their local base and selected
added components, effectiveness, conversion/modifier stages, totals and attack display aggregates;
and two actual numeric UI consumers with concrete provider identity. The intermediate added-stat
keys, combined modifier keys and spell/secondary average inputs retain their provider-input boundary
here; their wider producers are not inferred from their names. A comprehensive catalogue of every
possible skill-specific producer, every panel widget class name, live panel freshness, RNG,
defender mitigation and resource deduction is outside this R3 closure. No runtime observation or
server-authoritative damage claim follows from these static formulas.
