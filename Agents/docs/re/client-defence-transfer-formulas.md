# Client taken-as and armour applicability formulas (3.29.3b)

## 1. Result and boundary

The native stat registry contains 17 selected damage-taken-as outputs and five armour applicability outputs.
Their callbacks combine percentages, conditions and override gates; they do not accept an incoming damage amount.
This report recovers those formulas, including two differences between declared dependencies and actual reads.
It does not establish damage redistribution, normalization above 100%, armour reduction of a concrete hit,
or publication of resource loss.

This extends [defender modifiers](client-hit-defender-modifiers.md) and
[stat evaluation](client-stat-evaluation.md).
An output named `hit` and an input named `hit_and_dot` do not establish that the same consumer applies both.
The per-event consumer and the network/native provider boundary remain separate obligations.

| Input | Identity |
|---|---|
| Executable | `Client/Game.exe`, PE x86-64, 3.29.3b; preferred base `0x140000000` |
| Executable SHA256 | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| Stats SHA256 | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| Key mapping | Native key `k` equals zero-based `Stats.json` row `k - 1` |
| Method | Original PE instructions and chained unwind ranges; offline synthetic stat-cache controls |
| Runtime | No client execution, attach, hooks or new captures |

The private batch `Tools/re/out/client-damage-audit/r15-defence-transfer` retains the exact registration
and callback mapping, dependency vectors, original instructions and verification controls.
It contains 41 unique functions, 48 ranges and 4,842 decoded instruction records for 22 outputs.

## 2. Notation and arithmetic

`S(k)` is the context's signed 32-bit stat value.
`B(k)` is the nonzero predicate implemented by `0x1425135a0`; negative nonzero values are true.
`W(n)` keeps the low 32 bits and interprets them as signed.
`C(c,k)` means `S(c) >= 4 ? S(k) : 0`; the comparison is signed.
`G(b,k)` means `B(b) ? S(k) : 0`.
These are stat reads, not recursive calls to another registered formula.

Every result in the following tables is wrapped by `W`.
The callbacks have no final clamp to 0 or 100 and no saturation of their integer sums.
For example, a coefficient of 110 is returned as 110; handling competing transfers is outside this evidence.

## 3. Taken-as coefficients

The output labels below identify the source and destination types.
The exact native output identifiers and registration addresses are retained in section 6.

| Output key | Source to destination | Callback | Formula before `W` |
|---|---|---|---|
| 2317 / 2318 / 2319 | Fire / Cold / Lightning to Chaos | `0x1427f36e0` | `S(2316) + C(21232,21505) + (B(9587) ? 50 : 0)` |
| 19718 | Cold hit to Physical | `0x14286ee50` | `S(19722) + S(19719)` |
| 10126 | Fire hit to Physical | `0x14286f060` | `S(19722) + S(10128) - S(10127)` |
| 19720 | Lightning hit to Physical | `0x14286f350` | `S(19722) + S(19721)` |
| 7350 | Physical to Fire | `0x14286f560` | `S(15765) + S(1052) + G(7285,7344) + G(7282,7341) + C(21233,21508)` |
| 7351 | Physical to Cold | `0x14286fce0` | `S(1053) + G(7285,7345) + G(7283,7342) + C(21231,21511)` |
| 7352 | Physical to Lightning | `0x142870370` | `S(1054) + G(7285,7346) + G(7284,7343) + C(21230,21514)` |
| 9653 | Lightning hit to Fire | `0x142870a00` | `S(3393) + S(22991) + S(15766) + (B(9588) ? 50 : 0) + G(7282,16423)` |
| 9654 | Cold hit to Fire | `0x142870ee0` | `S(3395) + S(22991) + S(15767) + (B(9588) ? 50 : 0) + G(7282,16423)` |
| 16427 | Fire hit to Cold | `0x1428713c0` | `S(3391) + G(7283,16421)` |
| 16428 | Lightning hit to Cold | `0x1428716b0` | `S(3392) + G(7283,16421)` |
| 12848 | Cold hit to Lightning | `0x1428719a0` | `S(3394) + S(12852) + G(7284,16422)` |
| 12849 | Fire hit to Lightning | `0x142871d70` | `S(3390) + S(12853) + G(7284,16422)` |
| 16400 | Chaos hit to Fire | `0x1426d2630` | `S(16396)` |
| 16399 | Chaos hit to Lightning | `0x1426d2730` | `S(16395)` |

The four-item conditions use different influence counts: Hunter `21232`, Warlord `21233`,
Redeemer `21231` and Crusader `21230`.
The purity predicates are Elements `7285`, Fire `7282`, Ice `7283` and Lightning `7284`.
`9587` is `keystone_divine_flesh`; `9588` is the separate native identifier `keystone_tempered_by_war`.
These exact inputs explain where the corresponding 50-point additions enter coefficient construction.
They do not prove the order of applying those transfers to a concrete damage vector.

The complete callback for 19720 never reads 10128, although its registrar lists it as a dependency.
This is a declared extra dependency in this artifact, not evidence for an additional Fire-to-Physical term.

## 4. Armour applicability and override order

Define the recent-block contribution:

```text
recent = B(4928) || B(5630)
elemental = W(S(16936) + (recent ? S(16937) : 0))
```

The predicate uses either attack block or spell block, not their count or their sum.
The following cases are evaluated in the displayed order.

| Output | Callback | Ordered cases |
|---|---|---|
| 12056 Physical | `0x1428aa510` | If `B(22580)`: `S(14544) > 0 ? 100 : 0`; else if `S(243) <= 0`: 0; else `B(12063) ? 0 : 100` |
| 12057 Fire | `0x1428aa760` | If `S(243) <= 0`: 0; else if `B(12063)`: 100; else `elemental` |
| 12058 Cold | `0x1428aa760` | Same as Fire |
| 12059 Lightning | `0x1428aac40` | If `S(243) <= 0`: 0; else if `B(12063) || B(12062)`: 100; else `W(elemental + S(16938))` |
| 12060 Chaos | `0x1428ab220` | If `S(243) <= 0`: 0; else if `B(12064)`: 100; else `S(18041)` |

| Key | Exact input identifier |
|---|---|
| 243 | `physical_damage_reduction_rating` |
| 14544 | `ward` |
| 22580 | `keystone_voranas_adaption` |
| 12063 | `keystone_prismatic_bulwark` |
| 12062 | `base_armour_applies_to_lightning_damage` |
| 12064 | `base_armour_applies_to_chaos_damage` |
| 16936 | `armour_%_applies_to_fire_cold_lightning_damage` |
| 16937 | `armour_%_applies_to_fire_cold_lightning_damage_if_have_blocked_recently` |
| 16938 | `base_armour_%_applies_to_lightning_damage` |
| 18041 | `base_armour_%_applies_to_chaos_damage` |
| 4928 | `have_blocked_attack_recently` |
| 5630 | `have_blocked_spell_recently` |

The physical Ward override is evaluated before the ordinary Armour/Prismatic Bulwark conditions.
With 22580 set and positive Ward, physical applicability is 100 even if Armour is zero and 12063 is set.
With that override set and Ward zero, physical applicability is zero even if Armour is positive.
This establishes an applicability predicate; it does not establish the magnitude later used for mitigation.

The full physical callback reads 14544 when 22580 is true, but the registrar declares only 243, 12063 and 22580.
An implementation cannot reconstruct its complete read set from the declared dependency vector alone.
Whether another owner invalidation mechanism compensates for this missing dependency remains open.

For elemental outputs, the positive-Armour gate precedes the 100-point shortcuts.
A shortcut replaces the percentage sum; it does not add 100 to it.
For example, positive Armour, no shortcuts, a recent block, 16936=17 and 16937=29 give Fire/Cold 46.
Lightning additionally includes 16938; with 16938=41 it gives 87.
Setting 12063 makes those three outputs 100, while setting Armour to zero makes them all zero.

## 5. Evidence and verification

The author verification interprets the complete original callback bodies against a synthetic fully-valid cache
and compares them with separately written integer algebra.
It passes 4,640 controls over 174,792 interpreted instruction steps.
Controls cover signed thresholds around four, negative Boolean values, nonpositive Armour and Ward,
override priority, both block predicates, distinct coefficient values and signed 32-bit overflow.
Actual read unions match declared dependencies except for exactly the two differences documented above.

The cache fixture deliberately avoids cache-miss/provider execution.
Stat getter and Boolean helpers are explicit fixtures; these controls do not establish all provider behaviors,
live invalidation, actual hit mitigation, or official gameplay execution.
An independently authored R10 integer engine passes another 1,299 controls over 47,411 steps,
covering 889 callback instructions and checking registration vectors against fresh PE bytes.
It confirms both dependency differences, shortcut read exclusion, block OR, wrapping and unclamped results.
This review does not import the author's algebra or R9/R13 interpreter.
Its controls also confirm that Boolean lookup tests the low 32 bits, including an out-of-width fixture.
Independent review and the selected original-byte evidence are retained with the private batch.

## 6. Exact registered outputs

| Key | Exact output identifier | Registration |
|---|---|---|
| 2317 | `fire_damage_taken_%_as_chaos` | `0x1400cb900` |
| 2318 | `cold_damage_taken_%_as_chaos` | `0x1400cb960` |
| 2319 | `lightning_damage_taken_%_as_chaos` | `0x1400cb9c0` |
| 19718 | `virtual_cold_hit_damage_taken_%_as_physical` | `0x1400d7920` |
| 10126 | `virtual_fire_hit_damage_taken_%_as_physical` | `0x1400d7980` |
| 19720 | `virtual_lightning_hit_damage_taken_%_as_physical` | `0x1400d79e0` |
| 7350 | `virtual_physical_damage_taken_%_as_fire` | `0x1400d7a40` |
| 7351 | `virtual_physical_damage_taken_%_as_cold` | `0x1400d7ab0` |
| 7352 | `virtual_physical_damage_taken_%_as_lightning` | `0x1400d7b20` |
| 9653 | `virtual_lightning_hit_damage_taken_percent_as_fire` | `0x1400d7b90` |
| 9654 | `virtual_cold_hit_damage_taken_percent_as_fire` | `0x1400d7c00` |
| 16427 | `virtual_fire_hit_damage_taken_percent_as_cold` | `0x1400d7c70` |
| 16428 | `virtual_lightning_hit_damage_taken_percent_as_cold` | `0x1400d7cd0` |
| 12848 | `virtual_cold_hit_damage_taken_%_as_lightning` | `0x1400d7d30` |
| 12849 | `virtual_fire_hit_damage_taken_%_as_lightning` | `0x1400d7d90` |
| 16400 | `virtual_chaos_hit_damage_taken_%_as_fire` | `0x1400d7df0` |
| 16399 | `virtual_chaos_hit_damage_taken_%_as_lightning` | `0x1400d7e40` |
| 12056 | `armour_%_applies_to_physical_damage` | `0x1400dec20` |
| 12057 | `armour_%_applies_to_fire_damage` | `0x1400dec80` |
| 12058 | `armour_%_applies_to_cold_damage` | `0x1400decf0` |
| 12059 | `armour_%_applies_to_lightning_damage` | `0x1400ded60` |
| 12060 | `armour_%_applies_to_chaos_damage` | `0x1400dedd0` |
