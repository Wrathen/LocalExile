# Client defender-conditional damage context flag (3.29.3b)

## 1. Result and boundary

Native stat 9189, `defender_has_attacker_conditional_damage_stats`, is a presence
predicate over 37 defender-side inputs. Its callback returns 1 when any input is
nonzero and 0 otherwise. Negative values also activate it; it does not sum the
inputs, multiply damage, roll a critical hit, choose a target, or consume a pool.

This is evidence that the stat model explicitly groups defender properties
which affect an attacker's result. It does **not** establish where that group is
used during an actual hit. The actual consumer, per-event critical confirmation,
joint attacker/defender numerical calculation and resource deduction remain open.

This supplement uses original executable bytes only. No client was started,
attached or executed. All addresses use the preferred image base.

| Input | Identity |
|---|---|
| Executable | `Client/Game.exe`, x86-64, 3.29.3b, base `0x140000000` |
| Executable SHA256 | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `Stats.json` SHA256 | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| Stat mapping | Native key `k` = `data/tables/Stats.json` row `k - 1` |
| Registration / callback | `0x1400d5640` / `0x14284fc10` |
| Registration call | `0x1400d5703` → `0x142513630` |
| Private evidence | `r10-hit-numerical-context` |
| Reproduction | `r10-hit-numerical-context.py`, `r10-hit-numerical-verify.py` |

## 2. Complete predicate inputs

The original registration declares the following 37 keys. Original-instruction
interpretation confirms that the all-zero path reads exactly that set. The
callback reads `all_damage_taken_can_ignite` before the faster-ailment input;
the registration places those two dependencies in the opposite order. This
difference does not change the predicate.

| Keys | `Stats.Id` / family |
|---|---|
| 9185 | `enemy_critical_strike_chance_+%_against_self` |
| 23122 | `enemy_critical_strike_chance_+%_final_against_self_from_abyssal_gaze` |
| 9187 | `enemy_additional_critical_strike_chance_against_self` |
| 12561 | `enemy_additional_critical_strike_multiplier_against_self` |
| 10259 | `virtual_enemy_chance_to_deal_double_damage_%_against_self` |
| 10448 / 10449 | `mortar_barrage_mine_minimum/maximum_added_fire_damage_taken` |
| 12444 | `enemy_spell_critical_strike_chance_+%_against_self` |
| 7382 / 7383 | `minimum/maximum_added_chaos_damage_taken` |
| 12557 / 12558 | `minimum/maximum_added_physical_damage_taken` |
| 17125 / 17126 | `from_self_minimum/maximum_added_attack_physical_damage_taken` |
| 17127 / 17128 | `from_self_minimum/maximum_added_attack_fire_damage_taken` |
| 17129 / 17130 | `from_self_minimum/maximum_added_attack_cold_damage_taken` |
| 17131 / 17132 | `from_self_minimum/maximum_added_attack_lightning_damage_taken` |
| 17149 / 17150 | `from_self_minimum/maximum_added_attack_chaos_damage_taken` |
| 12559 / 12560 | `from_self_minimum/maximum_added_physical_damage_taken` |
| 17133 / 17134 | `from_self_minimum/maximum_added_fire_damage_taken` |
| 17143 / 17144 | `virtual_from_self_minimum/maximum_added_cold_damage_taken` |
| 17137 / 17138 | `from_self_minimum/maximum_added_lightning_damage_taken` |
| 8550 / 8551 | `from_self_minimum/maximum_added_chaos_damage_taken` |
| 12564 | `enemy_damaging_ailments_deal_damage_+%_faster_against_self` |
| 16725 | `all_damage_taken_can_ignite` |
| 17140 | `enemy_physical_damage_%_as_extra_fire_vs_you` |
| 22472 | `self_attack_physical_damage_%_to_add_as_fire` |
| 21436 | `chain_hook_attachment_grant_X_rage_per_hit_from_attacks` |

The first short circuit is `test ecx, ecx` at `0x14284fd03`, jumping to the
true return at `0x14285108d`. The last input is read through `0x142513520` at
`0x142851074`; its `test eax, eax` / `jne` at `0x142851079` / `0x14285107b`
selects the same true return. Falling through returns the last zero input.
The true return writes `eax = 1` at `0x142851092`.

Each of the 37 keys was tested in isolation with values `INT_MIN`, -1, 1 and
`INT_MAX`: all 148 synthetic ready-cache cases returned 1. The all-zero case
returned 0. These are interpretations of the original instructions with
synthetic cache inputs, not observations of the running game.

## 3. Typed native cache contract

A follow-up establishes how a **native evaluation context** would store and
read this output. This is a receiver contract, not identification of a consumer.

| Original instruction | Verified operation |
|---|---|
| `0x1424fd2ea`, `0x1424fd310` | Native transaction creation sets context `+0` to context `+0xc0`, the signed 32-bit value array |
| `0x1424fd2da`, `0x1424fd324` | Context `+8` points to context `+0x16d8c`, the validity bitmap |
| `0x1424fd331` | Context `+0xb0` retains the native owner |
| `0x1424fd365` | Native owner `+0x190` retains that context |
| `0x1424fccf1`, `0x1424fccf8` | Formula dispatcher passes native owner `+0x190` as callback argument |
| `0x14251354d`, `0x142513569` | Generic integer lookup addresses the value array by signed key multiplied by four |
| `0x142513561` | Lookup tests bitmap word `(key >> 5) * 4`, bit `key & 31` |
| `0x14251356e`, `0x142513575` | A cache miss reads context `+0xb0` through the native owner getter |
| `0x14251357d`, `0x142513584` | Lookup writes the obtained value and sets its valid bit |
| `0x1424fcd0b`, `0x1424fcd29` | Formula dispatch publishes its result into the same value/validity layout |

For key 9189 this means value-array byte offset `0x8f94` and validity word
offset `0x47c`, mask `0x20`. Those offsets identify the key only after the
receiver is independently bound to this native context layout. A network
override wrapper, another object's similarly placed field, or an untyped offset
match is insufficient. The constructor's value region contains 23,347 integer
slots including key zero, matching 23,346 extracted stat rows.

The extracted row marks 9189 `IsPublic = true`, `IsVirtual = true`,
`IsApplyDamageFunctionStat = false`, with empty `ContextFlags` and `DotFlag`.
These are table facts; they do not prove transmission, local evaluation or
application during an actual hit. In particular, an empty context-flag list
must not be renamed an explicit server-only restriction.

Original-byte evidence for these three functions and the extracted row is
retained in private `r11-defender-consumer-cache`, reproduced by
`r11-defender-consumer-prepare.py` and verified by
`r11-defender-consumer-verify.py`. The focused follow-up did not bind an actual
reader of 9189; the numerical consumer remains open.

## 4. Unresolved use

The callback's existence does not show that a received network entity executes
the native formula; [the stat-provider boundary](client-stat-evaluation.md)
still applies. Nor does the predicate reveal the numerical semantics of each
member, attacker attribution, eligibility, or how simultaneous values combine.

A focused search for the exact 9189 immediate in unwind-owned executable
instructions found its registration only. That bounded inventory excludes
data tables, dynamic keys, indirect calls, shorter immediates and unwindless
code, and cannot establish that the output has no consumer. It is retained as
private evidence, not a proof that the client lacks a numerical resolver.

The broader continuation remains the one described in
[the hit/resource boundary](client-hit-resource-boundary.md): bind a concrete
accepted hit and its actual target to a numerical amount, defender mitigation
and pool receiver. Expected-value critical formulas and reference-target
defence estimates do not close that edge.
