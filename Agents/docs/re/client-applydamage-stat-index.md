# Client ApplyDamageFunctions stat index (3.29.3b)

## 1. Result and scope

`Stats.IsApplyDamageFunctionStat` has a concrete native reader. It selects
whether a changed stat participates in an **active function-index list** on a
native stat object. The reader maps a Stats row to an `ApplyDamageFunctions`
row, then inserts or removes that function's byte-sized row index according to
whether the stat's resulting value is nonzero.

The pinned table contains eleven such stats, all associated with named on-hit
effects such as charge/soul generation, vines and relic skills. This is a
positive classification/activation contract. It is not the numerical damage
application function implied by interpreting the field's name in isolation.
The reader does not accept an actual defender, calculate hit damage or deduct a
resource. No function-list consumer or per-target execution is established here.

This pass uses original executable bytes and extracted tables only. No client
was launched, attached or executed. Addresses use the preferred image base.

| Input | Identity |
|---|---|
| Executable | `Client/Game.exe`, x86-64, 3.29.3b, base `0x140000000` |
| Executable SHA256 | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `Stats.json` SHA256 | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| `ApplyDamageFunctions.json` SHA256 | `199485ac1ee3ff044f518f46ccc1be6b2802cc93a6faf94fd417b3efd752c928` |
| Private original-byte evidence | `r12-applydamage-stat-readers` |
| Reproduction / verification | `r12-applydamage-stat-prepare.py`, `r12-applydamage-stat-replay.py`, `r12-applydamage-stat-verify.py` |

## 2. Typed row and metadata read

The 3.29.3b Stats schema has raw row stride `0x69` and places the flag at byte
`+0x47`. This is consistent with its ContextFlags/DotFlag arrays at `+0x49` and
`+0x59`, whose actual native readers are already established in
[stat evaluation](client-stat-evaluation.md#eligibility-masks-have-a-data-table-identity).
The row accessor `0x14228ffe0` independently computes `base + rowIndex * 0x69`
at `0x14229004d..0x142290054` and returns that row in the output handle.

Native updater `0x1424fdd70` has a complete positive receiver chain:

| Instruction | Verified operation |
|---|---|
| `0x1424fddb4..0x1424fddbf` | Converts incoming native key to zero-based row and calls the typed Stats accessor |
| `0x1424fddc5..0x1424fddce` | Loads the returned row, reads byte `+0x47`, and exits for false |
| `0x1424fdde4..0x1424fddf2` | Looks up that Stats handle in the supplied ApplyDamageFunctions resource's map at `+0x60` |
| `0x1424fde25` | Takes the mapped function-row pointer from map entry `+0x10` |
| `0x1424fde73..0x1424fde92` | Computes its zero-based row index using the table's `0x19` byte stride |
| `0x1424fde95` | Materializes the index as one byte |
| `0x1424fdea4..0x1424fdeab` | Selects native object vector `+0x100` for row byte `+0x18 != 0`, otherwise `+0x118` |
| `0x1424fdeae..0x1424fdeb1` | Selects insertion for nonzero resulting stat value, removal for zero |

The table's byte `+0x18` is schema field `__col2`. Its gameplay name is not
recovered, so the two lists must not be renamed attacker/defender lists. The
function maintains unsigned, sorted, duplicate-free index vectors. Both positive
and negative stat values select insertion; it tests nonzero, not positivity.

## 3. Table-to-index producer

This table identity is bound through code, not inferred from those row strides.
Raw stat contribution add `0x1424fc480` explicitly loads the UTF-16 path
`Data/ApplyDamageFunctions.dat` at `0x1424fc693`, obtains its resource through
`0x142452340`, then passes that resource, the native object, the key and its
resulting value to `0x1424fdd70` at `0x1424fc6de`.

That resource factory installs parser callback `0x1424534f0` at
`0x142452d37`; the callback invokes table parser `0x142432100` at
`0x14245353e`. The parser creates the map at resource `+0x60`
(`0x142432193`), iterates each function row's Stats handles
(`0x1424324cb..0x1424324df`), and passes each Stats handle with its enclosing
function-row pointer to map insertion (`0x1424324e1..0x14243251b`). The
insertion helper's available-capacity path stores that pointer at entry `+0x10`
(`0x142229b61..0x142229b65`), the same field the updater later reads.

Formula publication also reaches this updater: dispatcher `0x1424fcc80`
passes its published value to `0x1424fdd70` at `0x1424fce55`. Transaction end
`0x1424fd390` supplies the ApplyDamageFunctions resource to that dispatcher.
These are stat update paths; a changed value is not an actual hit event.

For the pinned data, the eleven flagged Stats rows exactly equal all Stats
references in the eleven function rows, after adding one to the extracted
zero-based references:

| Function row index / Id | Native stat | `__col2` |
|---|---|---|
| 0 `RelicMinionSkillTrigger` | 8313 `number_of_active_relics` | true |
| 1 `HeraldOfLightChampion` | 8297 `herald_of_light_summon_champion_on_unique_or_rare_enemy_hit_%` | true |
| 2 `AzuriteDebuffOnHit` | 8451 `apply_azurite_debuff_on_hit_ms` | true |
| 3 `TaserDebuffOnHit` | 8694 `apply_taser_debuff_action_speed_-%_on_hit` | true |
| 4 `PreventVaalSoulsOnHit` | 7851 `prevent_vaal_souls_on_hit` | true |
| 5 `GainSoulGenerationOnHit` | 7861 `gain_soul_regeneration_for_x_seconds_on_hit_every_500_ms` | true |
| 6 `GrantVaalAttackSoulOnHit` | 7857 `grant_vaal_attack_soul_on_hitting_rare_or_unique_with_non_vaal_skill_every_x_ms` | true |
| 7 `EnduranceChargeOnHitVsBleedChance` | 7120 `chance_to_gain_endurance_charge_on_hit_%_vs_bleeding_enemy` | true |
| 8 `RandomChargeOnHitChance` | 7615 `chance_to_gain_random_standard_charge_on_hit_%` | true |
| 9 `NemesisVinesOnHit` | 7548 `attach_vines_to_defender_on_hit_%_chance` | false |
| 10 `PuppetMaster` | 10107 `grant_puppet_master_buff_to_parent_on_hit_%` | true |

## 4. Eligibility and proof limits

The updater returns before the row read when either global byte
`0x1445b38cd` or `0x14458159b` is nonzero (`0x1424fdd93..0x1424fdda7`).
The first byte is the existing native-mutation guard which
[successful ordinary startup sets to one](client-stat-evaluation.md#5-action-local-native-mutation-is-guarded-out-by-ordinary-startup).
Consequently that established startup path skips this updater. The second
byte's broader mode meaning is not established here. Neither observation proves
that all possible function consumers are absent or that this schema field is
universally server-only.

Original-instruction interpretation of the index-update region covers all
eleven function indices, five signed values (`INT_MIN`, -1, 0, 1, `INT_MAX`)
and three initial sorted lists: 165 cases. It confirms insertion, duplicate
suppression, removal and preservation of the other list. These cases start
after a successful metadata lookup, assume sufficient vector capacity and model
byte-preserving memmove; they do not validate archive loading, allocation growth,
startup state, running-game behavior or execution of a listed function.

Independent review rereads all fourteen original function records (2,683 instructions),
21 control/dataflow anchors and both guard addresses. A further 264 distinct
index-region controls cover full-list removal, sparse insertion, duplicate suppression,
negative nonzero values, both list flags and high unsigned byte sentinels.
They retain the same resolved-metadata and sufficient-capacity boundaries.

The actual per-target numerical hit/defender/resource edge remains open. This
result closes the flag's selected metadata-reader and activation-index contract.
