# 11 — Skills: how the client gets them, the default attack

The standalone bow/wand and monster-action port is documented in [the port record](../dev/ranged-monster-actions-port.md).
Its fresh automated results are separate from historical LocalExile native evidence quoted below.

Status (live, 2026-10-17): the player has the default attack. It shows in the skill picker, can be put on the left mouse
button, walks into range, swings at the Witch's attack speed (757 ms, or faster with `/set_stat attack_speed_+%`), and
the server lowers the monster's life 2–5 per hit at the swing's contact point until it dies. Code: `GrantedSkill`, `PlayerObject` (`InstanceEntities.cs`), `SkillCatalog.cs` and `Attack` in the
Gateway. Test: `InstanceEntitiesTests.Player_blob_with_stats_and_a_granted_skill`.

## 1. ActiveSkillManager

Every Actor has an `ActiveSkillManager` (RTTI `Skills::ActiveSkillManager`, vtable `0x1435db980`) at **Actor +0x598**,
returned by the Actor's vtable +0x140. Its skills are a vector at **manager +0x158/+0x160** of
`shared_ptr<ActiveSkillInstance>` sorted by the instance's u16 at **+0x40**. Every add ends in the manager's slot 7
(+0x38), `SkillInstanceAdded` `0x141eea6a0` (found through its lambda's RTTI name). Lookups by id: `0x141ee97f0`
(creates the action for a skill id, used by S→C 0x1ab and by the client's own skill use).

ActiveSkillInstance (base vtable `0x1435b9728`, 400-byte `make_shared` block, vtable `0x1435db960`):

| Offset | Meaning |
|---|---|
| +0x40 | u16 **skill id**: what C→S 0x24/0x25 and S→C 0x1ab carry |
| +0x42 | u16 **action id**: an ActionTypes `HASH16` |
| +0x48 | the GrantedEffectsPerLevel reference (row handle, then the effect, its `Id` string) |

## 2. Basic actions: from the object type

The `ClientActor` constructor (`0x142192860`) walks a u16 list on the Actor type (`type+0xf8..+0x100`) and calls
**AddSkill** `0x142198d40(manager, u16)` for each: id = value & 0x3fff, action = value. The list comes from the .ot's
`Actor { basic_action = "…" }` lines (Character.ot: Move, Interaction, InteractionChannelled, WashedUp, Dance,
AreaTransitionAnimation, SinPickup, BloodVortexGetUp, EpilogueKnockdown, AncestralTrialDestroyTotem, …; Monster.ot:
Move, Flee, StrafeLeft, StrafeRight, Advance, DoNothing, MonsterPickup), and the value is the action's
**ActionTypes.dat `HASH16`**: Move = 10505 = **0x2909**, Interaction = 49766 = 0xc266 (id 0x0266). Traced live with
`scripts/re/frida/trace-skills.js`: the player gets 12, a Hungry Corpse 9. Nothing on the wire is needed for them.
`ot_components.py` extracts each type's list (`basic_actions` in `data/object-types.json`). Monsters that rise from the
ground add `Emerge` (16641 = 0x4101, id **0x0101**), which the server starts with 0x1ab to raise them
([10](10-chat-and-commands.md) §5).

## 3. Granted skills: Actor slot 20, flag 0x40

Real skills (gems, the default attack) come from the server, inside the **Actor component's vtable +0xa0 reader
`0x1421932b0`** (the part after the main body, [04](04-player-entity.md) §4.9; also what S→C 0x2ba runs). Its first flag
byte f0:

- **f0 0x40 → `0x142197b10`**: the skill list. It adds each record or replaces the one with the same id; it never
  removes a skill, so a skill missing from a later list stays on the client (and selectable on the skill bar), and it
  does not tell the UI: the character panel's skill list does not show a skill added this way after login.
- **f0 0x20 → `0x142198800`**: the skill update (`InstanceEntities.PlayerSkillsChange`). When it has a record or an id
  it calls `0x140b14b00` before (`0x1402fffbc`) and after (`0x1401d8280`) them, which the character panel's skill list
  follows:
  ```
  u8 a, u8 b
  a × skill record (0x1421981d0, as in the list: add or replace)
  b × u16 skill id (0x142198560: binary search of the manager's skills at +0x158 by id (+0x40), the manager's
                    vtable +0x40 runs for it, then it is erased; an unknown id is skipped)
  u8 c, c × (u32 ActiveSkills row, u32 capacity)                 → soul seeds, current = 0
  u8 d, d × (u32 ActiveSkills row, u32 current, u32 NEW capacity) → soul updates, OLD-capacity clamp
  ```
  After login the Gateway sends every skill change this way (`SendSkills`): all the player's skills as records and the
  ids that left. It remembers the ids the client has per connection (`ClientSkillIds`, from the player object and every
  update since). Live: a re-socketed gem's skill appears in the character panel again (with 0x40 it did not).

`0x142197b10`:
```
u8 n,  n × skill record (0x142196ce0, through 0x1421981d0: insert sorted by id, replacing an equal id, then slot 7)
u8 m,  m × (u32 ActiveSkills row (0x101-byte rows), u32 capacity, u32 current) → manager +0x188
u32 k, k × (u16 skill id, u32 c, c × (f32 remaining base s, f32 total base s, f32 rate, u32 flags))
```

Both soul readers preserve existing entries when their lists are empty.
Collection, explicit removal resets, capacity seeding and the native consumers are documented in
[27-vaal-souls.md](27-vaal-souls.md).
The implementation has automated coverage; native acceptance is pending and unsupported Vaal casts remain refused.

Skill record (`0x142196ce0`):
```
u16  skill id                                      → +0x40
GEPL reference (0x1424a0dc0):
  u8                                               → ref +0x10 (0 for us; a stat-set reference with its u8 set uses it + 1)
  u32  GrantedEffectsPerLevel row (0x78-byte rows)
  stat-set reference (0x1424a0990): u8 a, [u8 if a], u32 GrantedEffectStatSetsPerLevel row (0xb8-byte rows)
  one more stat-set reference per entry of the effect's additional stat sets (GrantedEffects row → its list count)
u8   flags: bit0 → u32 parent object id ("Skill parent object did not exist" if unknown) + u16
            bit1 (0x02) → retain native Vaal soul inputs; bit2 → constructor parameter13 bit1
u8   n supports, n × (GEPL reference, u32)
u32, u32, u32 cooldown ms, u8                       → skill +0xc0, +0xc4, +0xc8, +0xcc (0x141ee73d0)
compact n × (compact stat key, compact value)      the skill's own stats
per additional stat set: the same stats list
```
At `0x1421975f0..0x142197610`, clear wire bit1 and nonzero raw GEPL +0x2c stored uses set `dil = 1`.
At `0x1421976e8..0x1421976f6`, constructor parameter13 is `(wireBit2 * 2) | dil`.
Constructor `0x141ee7571..0x141ee75ba` zeros soul inputs +0xe8/+0xec/+0xf0 when parameter13 bit0 is set.
Otherwise it retains GEPL +0x28 base souls, +0x2c stored uses and the selected soul multiplier.
`GrantedSkill.VaalResourcesEnabled` defaults false and emits only wire bit1 for catalog Vaal resource definitions.
Both catalog lookup paths derive it from raw stored uses, independently of effective cost, capacity or current souls.
Ordinary partners, attacks and supports keep their existing layout; parent bit0 and third bit2 remain clear.
The literal primary constructor regression is source evidence; native Vaal acceptance is still pending.

The additional stat sets are the GrantedEffects row's `StatSet2` list (Volcanic Fissure has one). Each needs its
reference after the first and its stat list after the skill's own: a record without them made the client misread the
rest and crash when Volcanic Fissure was socketed (live 2026-10-23). `GrantedSkill.ExtraStatSets` carries them
(`SkillCatalog.WithExtraSets`: each set's GESSPL row at the gem's level, its implicit, constant and per-level stats).
The action id (+0x42) is not sent: `0x141ee73d0` takes it from the data (GEPL row → GrantedEffect → ActiveSkill →
ActionType → `HASH16`); the default attack's is `Melee` 45448 = 0xb188.

**Skill ids** are the server's choice, but they share the manager with the basic actions, whose ids are `HASH16 &
0x3fff`. 0x0101 collided with a Monster.ot basic action (monsters showed a skill 0x101 / 0x4101). The Gateway uses
**0x0102**, which no ActionType masks to in 3.29.3b (`SkillCatalog.FirstSkillId`).

## 4. The default attack

**Issue #75 evidence scope:** imported test counts and Standard/Ruthless native matrices in this document describe
the original LocalExile revisions. They are retained as historical evidence, including their UNPROVED boundaries.
This branch ports the completed ordinary-mode work without the source's private game-mode implementation.
See [the port record](../dev/issue75-port.md) for revision mapping, fresh automated validation and native checks
still required on the ported branch. Source `artifacts/issue75-completion/` paths refer to the LocalExile checkout.

Characters.dat `StartingMeleeSkill` is the GEPL row of each class's default attack: **4474** = `PlayerMelee` level 1
for the Witch. Its stat set is GrantedEffects `StatSet1` = 476 (`PlayerMelee`), level 1 = GESSPL row **4430**.
`SkillCatalog` looks both up by GrantedEffects `Id` and level (`Game_PLAYER_ATTACK` picks another effect).

Without stats the swing is instant: no animation, and the client repeats C→S 0x24 every ~40 ms while the button is
held (live, first try). The Gateway sends the class's unarmed weapon (Characters.dat `WeaponSpeed`, `MinDamage`,
`MaxDamage`, `MaxAttackDistance`: Witch 833 ms, 2–5, 8) both in the player's Stats component and in the skill's stats:

| Key | Stat |
|---|---|
| 0x117, 0x118 | main_hand_minimum/maximum_physical_damage |
| 0x29e, 0x29f | main_hand_minimum/maximum_total_damage |
| 0x142 | main_hand_base_weapon_attack_duration_ms |
| 0x221a | main_hand_total_base_weapon_attack_duration_ms |
| 0x2a6 | hundred_times_attacks_per_second |
| 0x2a7 | hundred_times_damage_per_second |
| 0x7fc | display_hundred_times_damage_per_skill_use |
| 0x1e70 | skill_uses_main_hand (1: the panel's MH_* rows) |
| 0x126 | main_hand_attack_speed_+% |
| 0x146, 0x4d3 | main_hand_base_maximum_attack_distance, main_hand_maximum_attack_distance |

**The server sends final values; the client derives none of them.** Live 2026-10-17 (`dump-stats-object.js`): a skill
holding `attack_speed_+%` 2000 still swung every ~757 ms and the character panel kept showing 1.2. What the client
uses:

- The **swing time** follows the skill's `main_hand_attack_speed_+%` (with the base 833 ms). When it is not sent the
  getter answers **10**: unarmed attacks alternate hands, i.e. count as dual wielding, worth 10% attack speed. So the
  Witch's real unarmed swing is 833 / 1.1 ≈ 757 ms.
- The panel's **Attacks per Second** is `hundred_times_attacks_per_second / 100`. The panel caches it: it shows a new
  value only after the skill is selected again in the panel. After a passive allocation it stays stale even when the
  panel or its tab is reopened; only a relog rebuilds the character sheet (live 2026-10-19). The server's stats and
  the actual swing are already updated.
- The client walks into range by itself using `main_hand_maximum_attack_distance` (8 cells for the Witch).

**A dual-wielded attack has two hands.** When the off hand holds a weapon, a melee attack swings that one too, and the
client sets the off-hand strike's weapon up from the hand's **own** final stats — `off_hand_…` instead of `main_hand_…`,
the keys next to the main hand's in Stats.dat: `off_hand_minimum/maximum_physical_damage` (0x119/0x11a),
`off_hand_minimum/maximum_total_damage` (0x2a0/0x2a1), `off_hand_base_weapon_attack_duration_ms` (0x143),
`off_hand_total_base_weapon_attack_duration_ms` (0x221b), `off_hand_attack_speed_+%` (0x127),
`off_hand_base_maximum/maximum_attack_distance` (0x147/0x4d4). Sending only the main hand's leaves the second weapon
without attack time, damage or reach, and the off-hand animation of a ground attack then has a null animation set: the
next action crashes the client at 0x141c988c1 (issue #167). `WeaponStats()` sends both hands' sets, from the item in
Offhand1 with its own local mods, in the player's Stats component and in every granted skill's stats. Only Offhand1: the
weapon swap trades the hands' items with rows 14/15, so Offhand2 always holds the inactive set's off hand. The off-hand set
is sent only when the main hand holds a weapon too. `off_hand_attack_speed_+%` is the final main-hand value without the
main weapon's own `local_attack_speed_+%` (CharacterStats multiplies it in like a more), because the off-hand weapon's
local attack speed is already in its attack time (`SkillCatalog.Unarmed.OffHandAttackSpeedPct`). The weapon-type passives
(`axe_` / `sword_` / ... `attack_speed_+%`) apply to both hands alike, as they do to the main hand's value (any equipped
weapon of that type counts).

`CharacterStats`/`PassiveBonus` compute the final attack speed from the player's passives: generic
`attack_speed_+%`, `melee_attack_speed_+%`, one-/two-handed melee, all one-handed, `axe_` / `sword_` / `mace_` /
`claw_` / `dagger_` / `wand_` / `bow_` / `staff_attack_speed_+%`, shield and dual-wield stats. Dual wielding (and
unarmed, which counts as dual wielding for this bonus) adds PoB's `dual_wield_inherent_attack_speed_+%_final` 10% more.
The increased and more parts are combined into one integer `main_hand_attack_speed_+%` (75% increased and 10% more
become 93%), which `SkillCatalog.Unarmed.Stats(player)` sends. Attack time = base / (1 + that / 100), at least one
server tick (`Program.TickRate` = 30/s, so at most 30 attacks per second), and
`hundred_times_attacks_per_second` = 100 / attack time. The panel's **Damage per Second** is the average hit × APS
(`hundred_times_damage_per_second`) and **Damage per Use** is the average hit
(`display_hundred_times_damage_per_skill_use`), both in hundredths. `/set_stat` records raw attack-speed inputs and adds
them on top of the passives, then sends the stats (0x1b3) and the skill again. Minion, totem and timed/conditional
attack-speed stats are not modelled.

**The character's own stats** (`CharacterStats.For`, sent by `ClassAttributes` as final values): the per-level values
come from `Metadata/Characters/Character.ot` (`life_per_level` 12, `mana_per_level` 6,
`accuracy_rating_per_level` 2, `base_evasion_rating` 15; it has no `evasion_rating_per_level`, so evasion does not grow
with level), the bases and attributes from Characters.dat:

| Stat | Base value before optional sources |
|---|---|
| `maximum_life` | BaseMaxLife (38) + 12 × level + strength / 2 |
| `maximum_mana` | BaseMaxMana (34) + 6 × level + intelligence / 2 |
| `accuracy_rating` | (2 × level + 2 × dexterity + flat) × (1 + increased / 100) |
| `physical_damage_reduction_rating` | (flat armour from passives + base body/helmet/gloves/boots) × (1 + increased armour / 100) |
| `evasion_rating` | (15 + flat evasion from passives + base body/helmet/gloves/boots) × (1 + `evasion_rating_+%` / 100) |
| `evasion_rating_+%` | dexterity / 5 + increased evasion passives |
| `physical_damage_reduction_rating_+%` | increased armour passives |
| `maximum_energy_shield_+%` | floor(intelligence / 10) |
| `melee_physical_damage_+%` | strength / 5 |

`AttributeModifiers` combines class attributes and every flat passive/active-equipment source before scaling once.
It supports the existing single, pair and all-attribute flat stats, plus `strength_+%`, `dexterity_+%`,
`intelligence_+%` and `all_attributes_+%`.
For each attribute, its own increase and the all-attribute increase add in the same increased bucket.
Raw attribute totals stay wide through per-key aggregation, overrides, bucket cancellation and scaling.
The result is `floor(flat * (100 + increased) / 100 + 0.5)`, clamped to 0 through `int.MaxValue`.
Only the final attributes and native integer views are bounded; clipped display values never feed the raw calculation.
Derived evasion, Iron Reflexes, energy shield and melee bounds also clamp before integer conversion.
Life and mana stop at `int.MaxValue`, matching their native publication and reservation consumers.
Raw attribute rebuilds clamp current resources to the new caps without refilling or resurrecting the owner.
They publish fresh mana reservation even when current mana does not change.
The shared mana sender samples both values under the combat lock after acquiring the send gate.
Idle-aura and blocked-send reservation behavior has decrypted command-wire coverage.

This rounding model follows PoB commit `16de4b82d57f1c0de6eb40f37143c32d4da36a02`:
[stats](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b8/src/Modules/CalcPerform.lua#L401-L408),
[buckets](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b8/src/Modules/CalcTools.lua#L14-L35) and
[rounding](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b8/src/Modules/Common.lua#L680-L688).
Those functions do not separately floor the signed flat bucket or increased scale factor at zero.
Artificial raw input of -10 flat and -150% increased therefore gives +5; the nonnegative clamp follows rounding.
It is a pinned server model; receiving its computed fractional boundaries does not prove official-server parity.
The extracted Behemoth small nodes (4561, 9053, 22167 and the other `AscendancyCustomBerserker` small nodes)
supply 3% increased Strength and 10% increased Armour through ordinary raw stats.
That source evidence does not enable Phrecia selection or establish native class gameplay.

The final attributes feed life/melee damage, accuracy/evasion and mana/energy shield respectively.
Native `ClientStrings` row169, `IntelligenceDescription`, declares 1% increased ES per ten Intelligence.
The ES term uses complete final groups of ten, matching pinned PoB `CalcPerform.lua:541`.
This corrects the former `/5` term; static help text does not establish official fractional server arithmetic.
Historical attribute captures below precede that correction and do not validate the updated ES totals.
An explicit `AttributesResolved` pool marker distinguishes calculated zero attributes from legacy uninitialized pools.
Record clones retain the marker; three saturated attributes cannot overflow a sum-based fallback check.
`ClassAttributes` sends the same final values as both panel attributes and `virtual_*_for_requirements`.
Equipment requirements recalculate these values from active raw sources at each existing activation stage
([items](13-items.md#2d-requirements-and-inactive-items)).
Raw attribute `/set_stat` inputs replace their own passive/item key, including zero, and rebuild pools immediately.
Final `/set_stat strength`, `dexterity` or `intelligence` keeps its development display/requirement override semantics.
Those final overrides never become flat sources or change the derived pools.
Clearing a raw source, refunding a passive or swapping weapons rebuilds from raw inputs without compounding increases.
Automated boundary, production-pool, command and equipment regressions pass.
Historical evidence at `60044ad` covers thirteen native controls on an unascended Marauder with raw inputs.
They verify 32 Strength becoming 33 at 3%, 40 at 25%, and 48 when another 25% all-attribute increase is added.
The same all-attribute input sends 18 Dexterity and Intelligence from their flat 14 midpoint.
Life, mana and native requirement stats follow the final values.
A Plate Vest supplies 19 Armour when its 12-Strength requirement is met, stops contributing below it,
and resumes after the final Strength rises again.
Calculated all-zero attributes stay zero; removing the sources and relog restore the expected base pools.
These controls verify delivery and shared consumers, not the official server's fractional attribute algorithm.
Eleven native controls at rebased source `00b246f2` on main `79cc0337` verify the Strength-mana composition.
On a new level-one Marauder, eight mana per four Strength changes maximum mana from 47 to 111.
Strength 40 gives 127 mana; Strength 48 and Intelligence 18 give 145.
A temporary active-vest 100% mana modifier raises that result to 290 once; removing it restores 145.
Reduced attributes disable the ordinary vest at Strength 8 with 58 mana and restore it at Strength 16 with 75 mana.
Clearing the inputs restores Strength 32, mana 47 and Armour 19.
Every control reads the actual native final and requirement stats; Gateway/Core/Storage/Host revisions were pinned.
The check uses raw development inputs and ordinary equipment, without enabling a Phrecia class.
The ordered #290 / #294 / #296 / #300 integration at `c9cd1864` passes 28 fresh native controls.
Eight conversion controls preserve final Strength groups and the half mana from odd final Intelligence.
At final Strength 16 and Intelligence 7, conversion uses B=75.5 and publishes 15 mana and 140 Armour with the vest.
Combined regressions cross the rounded four-Strength boundary, then verify mana increases and truncation once.
Equipment, inactive/current weapon snapshots, zero/off raw replacements and idle aura reservation share that result.
Inactive-weapon, cascading-equipment, passive-refund and active-aura composition still need native coverage.
Historical LocalExile controls also cover 34 Standard/Ruthless supplied-source states, including Fireball
qualification at Intelligence 68/62/68 against its 67 requirement, source recovery and weapon-set relog.
Those controls do not enable private game modes in this branch or prove official fractional parity.
Fourteen subsequent supplied-source controls at actual level 100 verify the corrected ES and attribute-scaled Evasion
in Standard and Ruthless, including removal/return, no-refill, clamping and fresh entry. They bind completed local World
updates to the actual equipment actions; they do not certify every attribute operator or the rendered Defence rows.
See [20](20-damage.md#player-energy-shield-2026-10-25) for the exact states and remaining recovery scope.

Five native source flags now suppress only the named inherent attribute grants, after final attributes resolve:

| Native source | Removed inherent contribution |
|---|---|
| `gain_no_maximum_life_from_strength` (7964) | Strength Life |
| `gain_no_maximum_mana_from_intelligence` (7965) | Intelligence Mana |
| `gain_no_inherent_bonus_from_strength` (15532) | Strength Life and melee physical increase |
| `gain_no_inherent_bonus_from_dexterity` (18387) | Dexterity accuracy, including the mastery replacement, and Evasion increase |
| `gain_no_inherent_bonus_from_intelligence` (18388) | Intelligence Mana and ES increase |

Narrow and broad flags suppress their shared contribution once. Actual attributes and requirement fields stay intact;
independent item Life/Mana/ES/Evasion per attribute still reads those final values. The guards follow pinned PoB
`CalcPerform.lua` 516–543 after attribute resolution; `ModStore.lua` 501/525 retains the separate completed item groups.
The complete raw flag sum is replaced from one developer snapshot before boolean interpretation, including explicit
zero. Actual `/set_stat` commands rebuild the affected pools and skill stats, clamp current Life/Mana/ES as applicable,
retain Mana reservation and leave capacity increases unfilled. Ordinary equipment Life clamping still follows the
existing recovery tick; these command tests do not establish immediate Life publication for every source-change path.

All 16 native modifier vectors, including the companion +60/80/90/120 attributes on weapon-tree rows, have automated
both-mode catalogue/range/hash and saved-item round-trip checks. They are supplied-source controls, not acquisition or
weapon-tree admission. The 80 new cases (64 pure/native-source and 16 Gateway) pass; fresh full Gateway 4019, Core 648
and Data 37 all pass without skips. The diagnostic hook retains six actual disposed-stream `MonsterKilled`→`Send`
task faults separately from 386 first-chance records; no new flag path appears in those unobserved stacks, with no
diagnostic error/truncation/cap hit. Passing suites do not establish fault-free background execution.
Native-client flag publication/reload remains pending. Key18620 has no direct modifier/passive source in the checked
tables and is not an Iron Reflexes alias; global/other attribute-prohibition or conversion operators remain separate.

Life and Mana also collect six signed raw pool coefficients from passives and active equipment.
`LifeManaPoolSources` adds these terms to the flat buckets after resolving actual character level (1–100) and the
completed nonnegative attributes, before the pool's increased percentage:

| Raw coefficient | Flat contribution |
|---|---|
| `life_per_level` | coefficient × character level, added to inherent 12 per level |
| `mana_per_level` | coefficient × character level, added to inherent 6 per level |
| `maximum_life_per_10_dexterity` | coefficient × floor(final Dexterity / 10) |
| `maximum_life_per_10_intelligence` | coefficient × floor(final Intelligence / 10) |
| `X_life_per_4_dexterity` | coefficient × floor(final Dexterity / 4) |
| `x_to_maximum_life_per_2_intelligence` | coefficient × floor(final Intelligence / 2) |

Native item fixtures use +2 Life/Mana per level, +2 Life per ten Dexterity/Intelligence, +1 per four Dexterity and
+1 per two Intelligence; distinct +2 Mana-per-level modifiers also compose to +4 before multiplying by level.
Negative native `life_per_level` values -12/-6/-3 subtract from the inherent growth; they are not discarded.
Mode-specific source-vector cases read The Apostate (graph 56940), Crimson Power (20931) and Sanguine Power (24063)
through `PassiveTree.Stats`, `PassiveBonus` and the pool calculation; they do not allocate a Gateway character's tree.
Each attribute term counts complete groups once from the final whole-source attributes, after typed/all increases;
display-only final attribute overrides do not feed these pool inputs. Flat Life from existing equipment ES conversion
and the ordinary Strength/Intelligence halves remain in their existing buckets. Mana retains its Strength-based
branch below. Wide signed intermediates preserve cancellation before final pool scaling and bounds.
`PassiveBonus` uses one developer snapshot: an explicit coefficient replacement, including zero, replaces its whole
passive/item coefficient. `/set_stat` on any of these six inputs rebuilds pools and clamps current Life/Mana;
the relevant resource update is sampled at the send gate, with reservation retained for Mana.
The ordering follows pinned PoB `16de4b8` CalcSetup 597, ModParser 1354/1471 and ModStore 501; CalcPerform 517 supplies
the ordinary attribute bonuses. The existing final Life/Mana floor is retained, whereas pinned CalcPerform 88
rounds final pools. Exact official fractional parity remains unverified; this change does not adopt PoB's final round.
Native modifier and isolated conversion-passive fixtures do not establish legal acquisition, event-passive admission
or complete conversion-node behavior. The 71 new cases (45 pure/native-source and 26 Gateway) pass with 82 existing
regressions: 153/153 focused, without skips. Full Gateway passes 3874/3874 with no skips; earlier unchanged
Core 648/Data 37 from this session remain applicable, for 4559 applicable cases. Only Gateway was rerun after these
six-source changes. The current hook records seven actual unobserved disposed-NetworkStream faults: six
`MonsterKilled` → `Send`, and one `NotifyMonsterHit` → `HitMonster` → `AreaSkillHit` → `LaunchProjectile` →
`CastGemSkill` → `Send`. Its 390 first-chance records are separate from those seven faults.
There are no diagnostic errors, truncation or cap hits, and no captured new pool-source path; passing tests do not
establish fault-free background execution. New native checks remain pending; this bounded source addition does not
complete the Life or Mana panel rows, their more/conditional operators or other conversion engines.

Maximum mana also includes the ordinary flat and increased mana sources from passives and active equipment.
`X_mana_per_4_strength` (Stats row 4992, client key 4993) contributes
`floor(max(0, final Strength) / 4) * signed raw value` to that same flat mana bucket.
`CharacterStats.For` uses rounded final Strength; Intelligence `/ 2` and final mana truncation are unchanged.
Mana increases apply once to the combined flat total.
The active consumer uses wide signed arithmetic, clamps nonpositive totals or multipliers to zero, and saturates at
`int.MaxValue` for the native maximum and existing reservation/damage consumers.
A missing or zero source keeps ordinary mana rounding and signed flat-mana/multiplier behavior.
A missing or zero source uses the ordinary mana formula after resolving nonnegative final attributes.
Both routes retain complete Intelligence halves. Wide intermediates prevent a saturated final attribute from
wrapping ordinary mana or Accuracy; existing inherent ratios and Accuracy's final projection are unchanged.
`PassiveBonus` reads the generic source; `/set_stat X_mana_per_4_strength <value>` replaces it and rebuilds pools.
The rebuild clamps current mana to the new unreserved cap without filling it when maximum mana increases.
It explicitly sends the mana pool's `0x1b1`, including reservation, even when current mana is unchanged.
`SendPlayerMana` snapshots current mana and reservation together under the combat lock after acquiring the send gate.
Decrypted command-wire tests cover a 50% aura with regeneration disabled, unchanged current mana and a lowered cap.
An explicit zero suppresses the source; repeated builds cannot compound it, and fresh connections clear dev inputs.
Final native `strength` overrides remain presentation/requirement inputs and do not change this pool calculation.
Reservation, native `maximum_mana` and maximum-mana attack lightning all read the resulting pool.
Mental Conditioning (graph node 38080) supplies raw value 8 and conversion key 9548.
With conversion active, `StrengthMana.Flat` joins the unrounded flat bucket once before mana/armour increases.
Both Strength and Intelligence use final scaled attributes; odd Intelligence retains half mana until final rounding.
The ordinary decimal/`Rating` bounds and every raw-attribute `/set_stat` rebuild remain in place.
The full node gives a fresh Witch 16 mana and 128 Armour; display key 21159 alone enables neither consumer.
The consumer does not enable Phrecia selection or claim the full class.
Automated arithmetic, production-source and actual command/reload checks pass.
Thirteen native checks use a fresh unascended Witch, raw key 4993 and synthetic Plate Vest modifiers.
Fourteen and fifteen Strength grant three groups: maximum mana rises from 56 to 80 at raw value 8.
Sixteen Strength grants four groups and 88 mana; 25% increased mana scales the whole bucket to 110.
The maximum-mana attack-lightning panel follows those final values.
Signed input, explicit zero, removal/reapplication and relog are verified.
A final display-only Strength override does not become a raw input during a later rebuild.
These controls do not select Mental Conditioning or verify idle aura reservation in the native client;
that reservation path has the decrypted command-wire coverage above.

Accuracy's flat and increased passives are summed from `accuracy_rating` and `accuracy_rating_+%`, the weapon-specific
stats according to the equipped weapon shape (`sword_accuracy_rating`, `wand_accuracy_rating_+%`, ...), and the Accuracy
Mastery (`accuracy_rating_per_level`, `dexterity_accuracy_bonus_grants_accuracy_rating_+3_per_dexterity_instead`,
`gain_accuracy_rating_equal_to_intelligence`). Two other effects are target conditions applied at the roll, not in
the pools: `accuracy_rating_+%_final_vs_unique_enemies_from_mastery` (row 15547, PassiveSkillMasteryEffects Accuracy1:
40% more) multiplies the rating when the target's rarity is Unique, and
`accuracy_rating_+%_final_vs_enemies_in_close_range_from_mastery` (row 18375, Accuracy5: 50% more) when the target is in
close range (TargetAccuracy.AgainstTarget).
"Close Range" is the ReminderText row `(Close Range is up to 2 metres)`, which at the 10 units per metre of PoB's
ConfigOptions is the constant 20-unit radius; the server reads radius stat units as grid cells (SkillArea, Righteous
Fire's 18 = 1.8 m), so a target whose centre is within 20 cells of the player is close, exactly 20 inclusive, and
"Nearby" is not the same mechanic.
The target's rarity (`SpawnedMonster.Rarity`) and its tracked position at the hit's resolution time decide both; the two
"more" effects multiply (unique and close: × 2.1) and the product floors to a whole rating.
The default attack samples both actors' current tracked positions after its contact delay.
Ordinary, area and gem projectile attack contacts use these conditions.
Ten native checks allocated connected ordinary paths and selected both effects through real C→S 0x35 requests.
The unconditional native panel stayed at 711; logged rolls used 995 against Unique and 1066 against a close normal.
A close Unique used 1493, while a Unique outside close range used 995.
Moving a target back into range restored the close multiplier, relog preserved both choices, and refund restored 30.
Other conditional stats (nearby ally, marked enemy, low life, frenzy charges) are not modelled, and the panel is not
told the conditions: it keeps the unconditional `accuracy_rating` and, for the Offence
panel's Main Hand / Off Hand Accuracy rows, `main_hand_accuracy_rating` / `off_hand_accuracy_rating`. Its Chance to
Hit rows are sent directly as `main_hand_chance_to_hit_%` / `off_hand_chance_to_hit_%` (and the evasive-monster
variants), against a reference monster of the character's level; `character_sheet_reference_evasion` and
`..._high_dex_evasion` carry that monster's evasion. `/accuracy` states the mastery's per-target values.

**Accuracy source and selected-support correction (issue #75 review).** `AccuracyRating` now collects the
unconditional flat/increased generic and weapon-source buckets with wide signed sums. Pool builds replace
each complete raw source with its developer override, including zero, before deriving accuracy; Precision's
flat contribution participates once and is also removed by an explicit zero. `/set_stat ... off` restores the
underlying source and all implemented accuracy inputs trigger a pool rebuild. Per-level/mastery replacements
follow the same override rule. The exact nonnegative product floors once and saturates at `int.MaxValue`.

`CharacterStats.Pools.AccuracyBase` retains the signed, unrounded base before increased accuracy. A selected
attack's own flat/increased inputs join those original buckets, without adding weapon-local accuracy twice.
`SupportedAccuracy` supplies that profile and the selected main-hand rating, normal hit chance and evasive-target
hit chance. Actual level-one Additional Accuracy contributes 74 flat; quality contributes 1% increased per point.
Linking, unlinking, removing the support or changing the character's attributes rebuilds those three aliases.
Ordinary attack and unrelated spell records retain their own values. Explicit final alias overrides retain the
existing display policy; raw accuracy overrides affect the calculation. Target-conditional Masteries stay out
of the reference-monster estimates. Cannot-evade flags yield 100% in these selected chance estimates.

Decision: retain the original base and combine supported inputs before rounding, rather than adding 74 to an
already increased final rating or dividing that floored rating to reconstruct a lost base. Use the established
whole-source replacement policy for commands, instead of adding a second developer contribution on top of
Precision/items. Pinned PoB `16de4b82d57f1c0de6eb40f37143c32d4da36a02`, `CalcOffence.lua` accuracy section
and `Data/Skills/sup_dex.lua` `SupportAdditionalAccuracy`, supplies the formula and support values; native
catalog support/quality controls match them. This is the accepted 3.29.x reference, not exact 3.29.3b certification.
Automated source/command/Precision restoration, real support lifecycle, flooring, signed and overflow checks pass.
Area gem attacks now consume this profile for evasion and independent critical confirmation at admitted contact.
Misses do not consume impales, roll critical chance or trigger on-hit effects. Spells bypass attack accuracy.
Fourteen additional cases cover Cyclone/Heavy Strike, a real Additional Accuracy support, independent targets,
cannot-evade, spells, cancelled actions and rejected-contact side effects. Gem projectiles now consume the same
profile with forward/piercing/return continuation (see §10). Off-hand scope/execution, conditional/more
accuracy, complete always-hit publication and native checks remain open. Ordinary/selected main-hand
average/DPS estimates now include reference accuracy and critical confirmation as described in §14.

Armour and evasion are summed from the passive tree and the **base types** of the equipped body armour, helmet, gloves
and boots ([13](13-items.md) §1): `base_physical_damage_reduction_rating` / `base_evasion_rating` are flat, while
`physical_damage_reduction_rating_+%`, `evasion_rating_+%`, `evasion_and_physical_damage_reduction_rating_+%`
(Cloth and Chain and similar nodes), `global_defences_+%` and the combined armour/evasion increase are additive
increased modifiers. The overcapped cold resistance's increase is one of them (§12); it applies before the conversion,
so Iron Reflexes' converted evasion keeps it, and monster attacks roll against the boosted rating.
Power of Purpose's raw `keystone_mental_conditioning` flag converts 80% of flat mana to twice as much armour.
Mana and armour increases add on that branch, and its unrounded contribution joins ordinary armour before rounding.
The remaining maximum mana drives reservation and maximum-mana added lightning; Steel Willed reads final armour once.
See [the calculation boundaries and pending native checks](../game/power-of-purpose.md).
**Iron Reflexes**
(`keystone_iron_reflexes`) converts all evasion rating, including the base 15 and
passive/item flat evasion, to armour and removes dexterity's 1% per 5 evasion; the converted part keeps the armour,
evasion and shared defence increases once each.
Equipped armour and the active shield now include local flat and increased modifiers plus multiplicative quality,
rounded per item before the character-wide modifiers and conversion.
The saved Armour component roll selects a rounded base from the item's native minimum/maximum range before local mods.
All three defences share that roll; legacy items without it retain minimum bases.
See [13](13-items.md), Base defences, for the formula and stat sources.
The final `physical_damage_reduction_rating` and `evasion_rating` (with their `_+%`) go to the client. Armour uses
PoB's `armour / (armour + damage × 5) × 100`, rounded and capped at 90% (`DamageModel.ArmourReduction`); evasion uses
the same hit-chance formula as accuracy, so the chance to evade is `100 - AccuracyHitChance(evasion, attacker accuracy)`
(`DamageModel.EvadeChance`). Monster melee rolls the same formula against `DefaultMonsterStats.Accuracy` at the
monster's level and evades before damage; physical monster hits then go through armour and block. `/defences` prints
both against a reference monster of the character's level. Life and Mana use the resolved passive/item pools (§4).
**Chaos Inoculation** (`keystone_chaos_inoculation`) applies to that life: maximum life becomes 1 after the life
modifiers (the character's energy shield stays the real pool), and the player is immune to chaos damage through the
same damage-immunity table as the panel's `chaos_damage_immunity` row. The pre-CI maximum life that the game uses for
stun and ailment thresholds is not modelled.

The default attack starts with the active item's typed local packet or the class's unarmed physical base, plus raw
added attack damage. `PlayerHitDamage` traces conversion/gain before resolving the complete visited-type increase
bucket and applicable more factors. Melee modifiers require actual melee context, so a bow does not inherit Strength's
melee physical increase. Charge more and attack Rage apply once; the final packet feeds both panel and hit rolls.
The separate conversion matrix verifies 32 numeric states across Standard/Ruthless, including inherited source/
destination increases, typed support more, final prohibitions and effective Frenzy charges. Twenty-four use explicit
fields; eight ordinary-attack states prove omitted zero endpoints with actual natural getter returns. One initial
Standard zero baseline is retrospective and seven are prospective; hidden zero-row rendering is not claimed.
Both modes restore complete item/socket contents and pass fresh-login controls. See [20](20-damage.md) §2 for the
frozen input, preservation and diagnostic boundaries. These controls establish panel publication, not native hit contact.
`/set_stat` overrides resolved inputs. The packet, conversion order and damage-over-time model are in
[20-damage.md](20-damage.md); the panel's older `Unarmed.Damage` tuple is only the physical display range.

**Changing a skill after entering:** S→C **0x2ba** on the player with component index 9 (Actor) and the slot-20 data
`f0 0x40, f1 0, skill list`: the list replaces the skill of the same id, new stats included. Live: the swing sped up at
once. The Gateway now sends the 0x20 update instead (above), which also removes skills and updates the character panel.

**Inherent attack-gem speed (#191).** `GrantedEffectsPerLevel.AttackSpeedMultiplier` multiplies weapon attack
rate separately from the player's increased/more speed: Burning Arrow -30%, Heavy Strike -15%, Flicker Strike +20%.
The level-aware catalog accessor follows the same selected-level rule as other GEPL values.
[PoB CalcOffence](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/1afbc4cdcc4fe83273ce8e06a51c5cf986999250/src/Modules/CalcOffence.lua#L2299)
multiplies `source.AttackRate` by `1 + attackSpeedMultiplier / 100` before ordinary speed modifiers.
For the supported ordinary weapon-based path, `SkillAttackTiming` sends total base weapon ms as
`round(weapon base ms / (1 + inherent percent / 100))`, midpoint away from zero, with a minimum of one ms.
It retains the original weapon base ms (0x142/0x143) and final player speed percent (0x126/0x127).
The native total base fields (0x221a/0x221b) are integer ms, so the server uses that same rounded base, then player
speed, then its existing 30 Hz attack-duration floor; rounding the base changes the uncapped time by at most
0.5 ms divided by the player-speed factor.
Native action code `0x141c06c2a..0x141c06c8e` reads 0x221a and divides by 1000 and `1 + stat(0x126) / 100`.
The explicit 0x221a getter (`0x142706568..0x14270658f`) returns the stored value without another GEPL multiplier.
Gem pacing, attack contact/projectile release and channel intervals share that duration; selected APS is `100 / seconds`.
Ordinary attacks and non-attack skills retain their previous paths.
Native baseline before this fix: a 714 ms bow had stored speed 0%, base/total base 714 ms and APS 140 for both
ordinary attacks and Burning Arrow, with actual native clip duration 0.714 seconds in each case.
After the fix, actual native input verified Burning Arrow total base 1020 ms, clip duration 1.019999959 seconds
and stored APS 98; its original base stayed 714 ms and player speed stayed 0%.
The ordinary bow attack retained total base 714 ms, clip duration 0.7140000038 seconds and stored APS 140.
These checks verify native duration and stored APS, not a separate measurement of release/contact timing.
Local before/after captures are `logs/skill-timing-{before,after}-default-bow-real.json` and
`logs/skill-timing-{before,after}-burning-arrow-real.json` in the integration checkout.
Catalog, native-field, modifier-order, pacing/contact and spell-exclusion tests pass.
This excludes support/conditional/per-charge speed, skill-specific attack-time overrides and off-hand alternation rules.
Selected supported attacks publish their completed typed packet, clearing removed damage types and sharing the
weapon-local packet with contact/release calculations. Pre-critical average/DPS remains the existing local convention;
critical weighting and special skill mechanics remain separate work. See [20](20-damage.md) §2a for local item damage.
No generator output changes are needed: the multiplier is already extracted.

## 5. Combat timing (Gateway `Attack`)

1. C→S 0x24 arrives when the player clicks, before the client walks into range. The server walks its position estimate
   toward the monster and stops at the first cell within `MaxAttackDistance` (`WalkEstimator.Approach`).
2. On arrival it starts the swing: the 0x1ab echo (from the stop cell, aimed at the monster). Echoing at the click made
   the client swing on the spot, out of range.
3. At the swing's contact point it rolls the accuracy check and, on a hit, lowers the monster's life with 0x1b1. The
   contact point is the active weapon's `attack1_<weapon>` clip contact keyframe (`AnimationContacts.AttackContact`:
   its shield or dual-wield variant, then any attack1 of that weapon, else 0.4) as a fraction of the attack time, since
   the client stretches the clip to it.
   Only an actual timed walk from the original cell to a different stop cell adds the walk-in compensation
   (`Game_HIT_OFFSET_MS`, default 150 ms); stationary attacks use the native keyframe without that extra delay.
   `DefaultAttackContactDelay` receives the original cells and departure/arrival times from the production attack.
   It never lands later than the next held attack may start (a quarter early, less 50 ms:
   `AttackTiming.ContactDelay`), because that attack cancels a hit still waiting. The chance is PoB's `calcs.hitChance`: `accuracy / (accuracy + (evasion / 5)^0.9) × 125`,
   rounded and clamped to 5..100, with the monster's `DefaultMonsterStats.Evasion` at its level. The player's accuracy
   for this roll is the pools' rating raised by the Accuracy Mastery's target conditions under
   `TargetAccuracy.AgainstTarget` at the target's own rarity and position at that moment (§4). Ordinary attacks now
   use the defender's entropy and independent critical confirmation ([20 §2g](20-damage.md#2g-ordinary-attack-evasion-and-critical-confirmation)). A miss is logged and
   sends no life update; spells do not roll accuracy and always hit.
4. Any later player action (0x25 move, another 0x24) cancels pending contact/release (`ClientState.ActionCount`).
   A ranged projectile already created keeps its own area lifetime and can still collide after the player moves.
5. Pacing: one swing per attack time. While the button is held the client sends one 0x24 per swing; a swing may
   arrive up to a quarter of an attack early (jitter) and the schedule stays on the client's cadence. When the server
   thought the swing took 833 ms instead of 757 ms it fell behind and dropped every third or fourth swing, which then
   animated without damage.

**Critical strikes.** The server rolls a hit's critical chance after the damage roll: `(intrinsic base + added base)
× (1 + increased critical strike chance / 100) × applicable more/less factors`, capped at 100%, then multiplies damage by the final multiplier
(150% plus
`base_critical_strike_multiplier_+`), as PoB's `CalcOffence` does. PoB's `unarmedWeaponData[class].CritChance = 0`
and `base_critical_strike_multiplier = 150` are the defaults used here. The base is the equipped main hand's
`WeaponTypes.dat` `Critical` permyriad (830 = 8.30%); unarmed is 0. Two pure base stats replace that base before the
global increases, the way PoB's `WeaponBaseCritChance` override replaces an attack pass's
[source.CritChance](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcOffence.lua#L2003-L2012)
only when that hand's weapon type is not "None" (ModParser gets it from "Base Critical Strike Chance for Attacks
with Weapons is X%").
`override_weapon_base_critical_strike_chance` (Stats.dat row 9892, client key 9893, permyriad) is the armed main
hand's base: Slayer Overwhelm (graph id 17315) grants 800 = 8.00%, and the Blind Prophet position's
Sightless (graph id 24698) grants 900 = 9.00%.
`base_melee_critical_strike_chance_while_unarmed_%` (Stats.dat row 3906,
client key 3907, permyriad; the Behemoth position's Final Blow, graph id 10135, grants 1000 = 10.00%) fills the unarmed
melee attack that has no weapon base. Each is a base, not a fixed final chance: the generic
`critical_strike_chance_+%` increases still apply on top. A weapon hand keeps the unarmed base out, an unarmed
context keeps the weapon override out, and `SpellCriticalStrike`'s spell route reads neither.
The active weapon set determines the attack base and weapon-specific increases; an inactive weapon grants neither.
`global_cannot_crit`, `virtual_global_cannot_crit` and `cannot_critical_strike_with_attacks` suppress attack crits.
The pool builds the base once in `AttackBaseCriticalStrike`.
`AddedBaseCriticalStrike` adds raw `additional_base_critical_strike_chance` and the applicable
`attack_additional_critical_strike_chance_permyriad` or `spell_additional_critical_strike_chance_permyriad` after the
intrinsic base or its replacement, before increased chance. Actor and selected skill/support sources enter once;
published final chance is never an input. Weapon-local `local_critical_strike_chance` adds to the native weapon base
before `local_critical_strike_chance_+%`; that rounded permyriad becomes the intrinsic weapon input.
Signed BASE and increased sums remain wide through the completed expression, then round halfway up and cap.
`CriticalChanceFinal` now handles four unconditional native MORE/LESS inputs: Controlled Destruction's
`support_controlled_destruction_critical_strike_chance_+%_final`,
`support_multiple_projectiles_critical_strike_chance_+%_final`, `unique_critical_strike_chance_+%_final` and
`active_skill_critical_strike_chance_+%_final`. Player and own contributions to one raw stat sum before its factor;
different factors multiply after BASE/INC and before the final cap. Nonpositive base/INC/factors produce zero
instead of allowing two negative terms to create a positive chance. Final display aliases are not read back.
Exact rational intermediates avoid overflow and preserve the existing half-up permyriad boundary even when
large developer values and opposing sources compose. `/set_stat` recognizes each input for pool refresh.

Real level-one Controlled Destruction supplies -80: a skill at 10% becomes 2%, while the unrelated/default
skills retain their own chance. It affects supported attacks as well as spells, although its more-damage benefit
is spell-only. The actual `LessCriticalStrikeChanceAmulet17` modifier supplies -40, turning the ordinary test
weapon's 10% into 6%; a source override of zero and item removal restore 10%. Source, qualification and link
changes agree with selected publication and actual critical draws in parsed melee/bow/projectile/area contacts.

Evidence: local reference library `pob/skills/SupportControlledDestruction`, pinned PoB
`16de4b82d57f1c0de6eb40f37143c32d4da36a02`, maps the -80 source to `CritChance MORE` with no attack/spell
restriction. `CalcOffence.lua` lines 3102–3111 multiply BASE, INC and MORE before rounding/capping. The native
stat descriptions identify the other three inputs as more/less critical chance. This is a labelled 3.29.x fallback,
with current native verification pending. Decision: use an explicit evidenced source list and one exact final
projection; applying LESS after an already capped chance would incorrectly turn a pre-cap 200% × 20% into
20% instead of 40%. The alternative of treating every matching stat name as unconditional would incorrectly
enable target/owner conditions. Conditional more chance, guaranteed-critical effects, off-hand critical chance
publication/execution and attack critical confirmation remain separate implementation work.
Linked Increased Critical Strikes contributes its own BASE and increased chance once through the shared calculation.
Real native item modifiers, mode-specific Ascendant Assassin sources, local ordering, source removal, explicit zero
and selected supports have automated coverage; combined results are in §14.
The bounded selected-spell native controls below cover actor-plus-own BASE before increased chance.
The local-flat fixture retains its real no-extra-critical-damage flag; its chance checks do not certify multiplier
behavior. The formulas follow pinned PoB `16de4b8` as a labelled 3.29.x fallback, without exact official rounding proof.

On source `0aabeb6`, sixteen positive selected Fireball controls pass at actual level 100 in Standard and Ruthless.
Fireball and Increased Critical Strikes are genuine L1/Q0 gems in a two-white-socket linked host.
An in-range native spell BASE50 vector (Mods row 8490, HASH16 8721) supplies the actor source on a controlled carrier.
The support supplies BASE200 and 60% increased chance once, so `(500 + 50 + 200) × 1.60 = 1200` permyriad.
An in-range +100 all-attributes qualifier gives final attributes 114/114/132.
Fresh owned Actor/primary records and natural selected getters bind key554 to exact converter14/function15 text.

| Stage in each mode | Sources after the ordinary refresh | Selected permyriad / native text |
|---|---|---|
| C0 | Fireball alone | 500 / `5%` |
| C1 | Add actor BASE50 | 550 / `5.5%` |
| C2 | Link Increased Critical Strikes | 1200 / `12%` |
| C3 | Remove the support | 550 / `5.5%` |
| C4 | Reinsert the support | 1200 / `12%` |
| C5 | Remove actor BASE50, retain the support | 1120 / `11.2%` |
| C6 | Restore actor BASE50 and relog | 1200 / `12%` |
| C7 | Remove actor BASE50 and the support | 500 / `5%` |

Two further controls prove current Fireball identity disappears after removing its gem; they do not prove numeric zero.
Two D0 default sentinels observe their own current primary/DPS receiver and raw spell fallback550.
The original Standard D0 verdict remains UNPROVED: its assessor required C2's old Fireball instance pointer
to survive a source refresh, although the ordinary skill update can replace an object with the same skill ID.
Separately reviewed sentinel-v2 was authored after that capture and frozen before the Ruthless sentinel capture.
It checks the unique current Fireball ID/effect and a distinct current Default object, with C2's source-equal 1200
numeric capture explicitly historical; it makes no simultaneous current Fireball primary/receiver claim.
Original frozen inputs, scalar expectations and the failed verdict remain unchanged.

Canonical full item payloads, cells, empty host, returned gems and full flask charges were restored.
Ordinary unsocketing retained gem placement remaps 603/604 to 607/606 in both modes, without database normalization.
All eight identities, original account/probes, both stash banks, all 40 tabs/meta and four nonparticipant saves held.
Participant last-played timestamps and the saved slot2 Fireball intent changed normally; Ruthless current Life
1345 → 1245 retained the temporary qualifier-removal maximum clamp, and restoration did not heal it.
Cleanup passed with all 774 frozen hashes unchanged; the post-run audit matched 773 source hashes.
Observers/client were detached/closed, and normal `data/saves.db` services were restored.
Evidence is under `artifacts/issue75-completion/selected-crit-native/`, its sibling assessment directory and
`artifacts/issue75-completion/selected-crit-native-preservation/post-run-audit.json`.
These controls establish selected publication, not native critical hits, rendered zero, legal carrier acquisition,
attack/local-weapon/Assassin coverage, exact official rounding or a complete issue75 row.

`/set_stat` replaces its source value and rebuilds the pools for critical-chance inputs and prohibitions.
The panel's final `main_hand_critical_strike_chance` and the rolls share that result.
Generic and spell increases use the same effective raw inputs as `CharacterPlayerStats`: a development value,
including zero, replaces that stat's complete passive/equipment/aura sum.
The attack and pooled spell reference report those effective increased percentages, so `/crit` agrees with the rolls.
The attack base feeds shared `AddedBaseCriticalStrike` evaluation used by pools, selected panels and hit rolls.
Nonnegative half permyriad values round up: a 650 base with +1% increased gives 657.
Generic and applicable typed inputs are summed as signed `long` values before the final cap;
large positive and negative inputs may cancel before clamping.
The earlier native checks elsewhere in this section belong to the original PR head `8c4c62d`.
The shared half-up midpoint and remaining resolved contexts still need native checks.
Signed increases stay in 64 bits through the pool and player-plus-skill sum; only final native values are bounded.
Pool refresh uses exact supported stat names; unsupported conditional or minion stats do not rebuild pools.
Pool rebuilding runs outside the monster lock and retries if another rebuild changes the pools before assignment.

**Conditional critical policy (#242).**
The supplied 3.29.3b static evidence identifies `0x1427e5480` as the virtual critical-extra-roll getter.
It adds `extra_critical_rolls`, `extra_critical_rolls_while_on_low_life` when virtual stat 7751 holds,
`extra_critical_rolls_during_focus` while `has_focus` holds, and one for `keystone_sharp_and_brittle`.
Its final signed clamp at `0x1427e59f9` is -1..1, after the addition; two lucky sources still mean two total rolls.
There is no Attack or Melee restriction on the low-life term, so supported spell hits qualify too.
`CriticalStrikePolicy` and `Program.CriticalStrikes` consume those raw inputs independently of any class choice.
Low life is derived from current living owner Life and maximum Life with an inclusive 50% default threshold.
`low_life_threshold_%_override` takes precedence over `low_life_threshold_%` when positive.
The not-full-Life and Vulnerability count-as-low-life flags use current Life and current curse-state stats.
The Focus skill and curse application lifecycles are not implemented by this critical policy.

The existing positive-chance `Next(10000)` stays after the damage packet's original per-type draws.
A nonzero signed extra adds exactly one following `Next(10000)` even if the first draw decides the result.
Positive luck combines with OR, negative luck with AND, and zero luck uses only the original draw.
This ordering is LocalExile's local RNG policy, not an established official-server RNG sequence.
Cannot-crit flags take precedence and consume no critical draws, including under lucky conditions.
Ordinary attacks and supported skills now retain a shared `SkillCriticalRoll`:
luck is captured when their cost/admission commits, and all area targets, volley projectiles, piercing/return hits
and existing impact-area callbacks compare the same roll against their current critical chance/prohibition.
Channelled attack cycles start separate contexts. A blocked/evaded contact does not consume a critical draw.
Ordinary melee captures luck after its approach, when the swing reserves pacing. Object-targeted bows capture
there too; ground bows capture inside their accepted input/pacing gate. Both carry the context through queued
confirmation and release. Ordinary bow arrows retain the existing released damage/chance/multiplier snapshot;
cannot-crit flags still read the live owner at impact. Broader snapshot lifetimes remain a separate review.
No additional packet, accuracy or impale draw is introduced by the policy.

[Mark_GGG's mechanics answer](https://www.pathofexile.com/forum/view-thread/1676002/filter-account-type/staff/page/1)
distinguishes the per-use random roll from each hit's critical-chance comparison (Abyssal Cry answer), and says
luck acts when that roll is made (Diamond Flask answer). This corrects the prior assumption that each accepted
gem hit should reroll luck. Native extra-roll getter evidence establishes the source formula, not its lifetime.
Decision: retain one sampled number rather than caching a critical/noncritical boolean; changed chances and
later prohibition must still affect each hit. Capture luck at admitted use rather than its first target.
Random numbers are allocated lazily on the first admitted, non-prohibited positive-chance hit; this preserves
the existing cancelled/missed-action RNG behavior without asserting an official allocation sequence.
Critical accuracy confirmation remains a separate random test for each landed attack hit.
Twenty-two new cases cover shared area/volley/pierce/return/impact-area rolls, signed luck, independent confirmation,
separate uses and chance/prohibition changes between targets. Four parsed gem controls now verify use-time
luck across a later Life change. A further 20 ordinary controls cover both directions of Life/Focus/raw-source
changes behind queued confirmation for melee/object-bow/ground-bow, and changes during a melee approach.
The first 18 reproduced contact-time resampling before correction. Existing release/prohibition and signed-source
controls now assert admission luck. Decision: capture when the swing starts rather than the initial walk-in click,
and carry that state through the released arrow rather than refreshing it at release/impact. Preserve the other
bow snapshots pending specific evidence instead of changing all offensive lifetimes together.
Native and broader repeat/skill exceptions remain open.

`unarmed_melee_attack_critical_strike_multiplier_+` adds points to the ordinary multiplier only for an active
empty-hand melee Attack context without Spell; an inactive-set weapon cannot arm that context.
A selected ranged attack and every spell exclude it, including when their owner is unarmed.
The unarmed melee base also stays out of a selected ranged attack's chance.
Final Blow's raw graph node 10135 grants 1000 base permyriad, one low-life extra roll and 50 multiplier points.
The generic consumer uses the raw stats; it adds no ascendancy choice, class or node-allocation behavior.
Default and selected supported panel multipliers use the same context and add linked support bonuses once.
The panel retains the ordinary permyriad chance; `virtual_extra_critical_rolls` carries the separate lucky policy.
`virtual_on_low_life` and the selected skill's extra-roll virtual follow current owner state.
The player tick refreshes player/skill stats when Life, Focus or the effective extra-roll condition changes.
Combat samples contact state directly and does not wait for that panel refresh cadence.
Full player-stat and Actor refreshes use the shared `SendPlayerStatSnapshot` factory inside the connection's send gate.
The factory locks `Monsters`, rejects closed/leaving connections and samples current stats at send time.
A queued character or charge refresh cannot replay old low-life virtuals or an earlier active-weapon multiplier.
Held-gate wire regressions cover both directions across the 50% boundary and active-set swaps in both packet kinds.

Automated tests parse `0x24` into the actual object-use route and check Life losses and deterministic critical draws.
Controls cover default contact, ordinary bows, linked melee/ranged attacks, spell projectiles and spell areas,
the 50% boundary, signed cancellation/clamping, live state changes, active sets and cannot-crit precedence.
The existing module initializer supplies the checkout root once; production catalogs retain relative `data/` paths.
The tests consume node 10135's raw stats without allocating an ascendancy node or selecting a Phrecia class.
An unselected ascendancy refund is refused without spending points; removing the raw inputs clears the policy.
Upstream's separate ascendancy lifecycle and `AscendancyClientCompatibility` play gate remain unchanged.
A successful paid Final Blow refund is not established by these raw-source controls or the native checks below.
Fifteen native checks used a new unascended Marauder with raw inputs.
The active empty hand dealt a 200-damage critical hit; an active wand dealt 150, and an inactive wand did not suppress
its empty alternate hand's 200 multiplier.
Removing the raw bonus restored 150.
A 66-Life owner at 34 Life was above low life, at exactly 33 was low life, and remained low at 32.
Native received virtuals confirmed signed cancellation, the one-extra-roll clamp, unlucky state and condition clearing.
Cannot-crit forced an actual noncritical 100-damage hit despite a capped base chance and positive luck.
Healing, death and relog cleared the conditional state.
The spell multiplier stayed 150 while the unarmed melee multiplier was 200.
The hit checks exercise the default attack; supported spell/projectile contacts and exact RNG draw order are covered
by deterministic automated tests, not a native statistical-distribution claim.
The fifteen controls were also repeated on the resolved `effb797` composition.
The Fortification combination below has automated coverage; the native controls do not allocate Final Blow.
The merged Fortification path uses the same resolved critical multiplier as target damage.
Its source is the direct rolled packet before target mitigation and excludes stored Impale damage.
A parsed default-attack control with raw stats deals 200 damage to a 400-Life target and grants 14 Fortification.
Removing the unarmed bonus gives 150 damage and 12 stacks; cannot-crit gives 100 damage and 10 stacks.
These are automated checks of LocalExile's explicit Fortification generation model, not new native evidence.
Once Phrecia play is compatible, allocate/refund node 10135.
Verify that base, multiplier and conditional luck clear together.

**Existing base-chance native checks (historical).**
Twelve real-client checks used a fresh synthetic character and read the client's received stat values.
A Driftwood Wand's 830 became 800 and 900; 100% increased chance raised 900 to 1800, then cannot-crit set it to zero.
Switching to an empty second set gave zero despite the inactive wand; the unarmed base gave 1000.
Switching back restored 900, clearing the weapon override restored 830, and relog cleared the development overrides.
Automated regressions also cover generic/spell development increases, aura replacement and active-hand increases.
Five follow-up native getter checks verify the corrected development inputs on a new synthetic character.
With weapon base 900, generic increase 0 gives attack 900 and the pooled spell reference 500;
generic 100 gives 1800 and 1000, then spell-specific 50 changes only the spell reference to 1250.
Generic `int.MaxValue` with that spell modifier caps both chances at 10000; `int.MinValue` clears both.
The Gateway runs from its checkout root with repository-relative catalogs during these checks.
These checks validate the generic stat consumers; ascendancy allocation remains in its separate lifecycle change.
Spells use their own level's `GrantedEffectStatSetsPerLevel.SpellCritChance`:
Fireball 500 (5.00%), Freezing Pulse and Ice Nova 750, Arc 600, Scorching Ray 0.
`SkillCatalog.SpellCritChance` returns null for missing/invalid data, distinct from a known zero.
Missing/negative intrinsic data fails closed; a known zero can receive ordinary added BASE.
The unconditional passive stats are summed into `CharacterStats.Pools`: generic `critical_strike_chance_+%` and
`base_critical_strike_multiplier_+` for both attacks and spells, the attack/melee/weapon-shape chance and multiplier
stats for attacks, and `spell_critical_strike_chance_+%` / `base_spell_critical_strike_multiplier_+` for spells. The final
`main_hand_critical_strike_chance` / `_multiplier` and `spell_critical_strike_chance` / `_multiplier` stats go to the
client, so the Offence panel shows them.
`SpellCriticalStrike` shares the raw player-plus-skill increased-chance calculation between `GemSkills` and projectile/
area spell hits; the old final Fireball-based player display chance is not added as another input.
The overcapped lightning resistance's increase reaches that calculation through `CharacterPlayerStats`, which adds the
pools' stored derived value to the raw `critical_strike_chance_+%` once (§12); the attack and panel chances fold the
same value in `PoolsFor` instead.
Precision and Zealotry expose their existing crit bonuses through `AuraCatalog.RawStats` for that calculation.
Pool construction still reads passives/items and adds `AuraCatalog.Bonus` once; it does not read `RawStats` back.
The result rounds to integer permyriad, halves away from zero, then caps at 0–10000, matching
[PoB CalcOffence](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcOffence.lua#L3022-L3027).
`global_cannot_crit` and `virtual_global_cannot_crit` disable the supported spell roll.
Ordinary increased chance alone cannot make a known zero-base spell crit.
`SkillHitPlan.CanHit` still rejects hitless or missing selected skills before added BASE is evaluated.
The selected skill's spell rows also require `casting_spell = 1` (CharacterPanelStats `Spell_CritChance`);
the main-hand rows exclude that flag.
`EffectStats` supplies it only for an actual ActiveSkillType Spell without Attack, and sends zero otherwise.
Warcries are not classified as spells merely because they are not attacks.
Area and projectile hits use the same actual Attack/Spell types, with Attack taking precedence for a mixed type.
Bow attack projectiles retain their weapon damage, damage effectiveness, attack critical chance and multiplier;
the spell-specific calculation applies only to the spell route.
`SkillHitPlan` rejects unsupported types and spells without extracted hit damage before building a weapon packet or
rolling critical chance, so Scorching Ray, Righteous Fire and warcries cannot fall through to a wand hit.
This adds no beam or damage-over-time implementation.
The copied-save baseline recorded stored spell chance 500 for both Fireball and Freezing Pulse,
and the selected Freezing Pulse panel still showed main-hand rows (main-hand crit 830).
Native getters also read the stored 500; evidence is `logs/spell-crit-baseline.json` in the validation checkout.
Copied-save native panel/getter checks now show Fireball 5%, Freezing Pulse and Ice Nova 7.5%, and Arc 6%.
With 100% increased generic crit they show Fireball 10% and Freezing Pulse 15%; adding another 100% spell crit gives
15% and 22.5%, respectively, so the inputs add once.
`global_cannot_crit = 1` makes the native skill chance zero and hides its critical row.
After clearing those modifiers and relogging, Freezing Pulse still shows 7.5%.
Scorching Ray remains explicitly zero with 100000% increased generic crit; its actual cast caused no weapon hit or
critical roll against a nearby 86-life Ash Harvester.
On the same connection, Fireball with that increased chance dealt a logged 22-damage critical hit, leaving 64 life.
The final Arc/Ice Nova and hitless-spell checks used the reviewed type-routing fix.
Evidence: validation `logs/spell-crit-{after-default,after-generic100,after-combined100,after-disabled,after-relog,
final-arc-ice,final-scorching-ray}.json`, `spell-critical-live-summary.json` and `live-gateway.log`.
Native probes reported no errors.
Catalog, raw-source, aura, flag, rounding, hit routing and roll-boundary checks are also automated.
Timed, charge, enemy-state and other conditional/more critical mechanics remain outside this slice.
`/crit` prints the attack values and the legacy pooled 5%-base spell reference, not the selected gem's chance.

**Impale (review correction).** Ordinary melee/bow and supported projectile/area contacts now share actual rolled
physical damage, including the inflicting owner's critical multiplier, before target mitigation. Each instance stores
10% times impale effect and lasts five later hits or eight game-time seconds, modified by its captured hit-count and
duration inputs. Any later accepted hit can trigger existing impales, including spells and zero-damage hits.
Their sum resolves as separate reflected physical damage, with target physical reduction and generic damage taken
once; it does not inherit the triggering hit's critical multiplier, conversion, spell-only damage taken or return
multiplier. Existing instances consume a hit before a new instance is added. There is no artificial five-stack cap.
Killing hits retain instances on the corpse until expiry/removal. See [20-damage.md](20-damage.md) §3a for source
scope, lifecycle gates, evidence, decisions and remaining conditional mechanics.
The 2026-10-20 native observations exercised the old maximum-roll, capped, clear-on-death implementation and do
not verify this corrected implementation. Current-branch native validation remains pending.

Exits use the same approach (`ExitReach` = 6 cells) before the area change or the instance list.

**Life bar on death:** when life reaches 0 the client's Life tick (`0x141e6a190`) stops notifying its listeners, so the
mini life bar over the monster kept its last value. The bar entry (`0x140220ab0` computes its flags; `0x140221fd0`
decides visibility) also listens to stats (`0x140220c30`): `hide_mini_life_bar` (0x2a8b) or `virtual_is_hidden_monster`
(0x59d0) hide it. The Gateway sends 0x1b3 `hide_mini_life_bar = 1` on the monster with the killing 0x1b1. Live: the bar
disappears.

## 6. Monsters attack

Live 2026-10-17: Hungry Corpses walk up, swing (animated) and lower the player's life; walking away dodges a swing.

- **Skill.** A monster's Actor gets the same skill list as the player (slot 20, f0 0x40) with its MonsterVarieties
  `GrantedEffectsKeys[0]` (Hungry Corpse: 406 `Melee`). Monster effects have per-level rows only for some levels, so
  `SkillCatalog.FindByRow` takes the highest GEPL / GESSPL level at or below the monster's (Melee: level 1, GEPL 4394).
  Id 0x0102, like the player's (no ActionType masks to it).
- **Stats.** The final attack stats (`MonsterAttack.Stats`): attack time = MonsterVarieties `AttackSpeed` (2400 ms),
  reach = `MaximumAttackDistance` (8), damage = DefaultMonsterStats `Damage` at the level × `DamageMultiplier` / 100
  with a ±20% spread (the spread is assumed), then the monster's mods (its rarity's, its affixes: damage, attack time,
  extra fire / cold / lightning / chaos; [14](14-monster-packs.md) §5). They go in the monster's Stats component and
  in the skill.
- **`skill_is_non_pathing` (0x4f32) on the skill.** The Melee action's begin (vtable +0x38 `0x141938830`) is just that
  stat: when 0, the action runner (`0x141e8e060`) sends the action through the client's own walk-into-range step,
  which drops a server-driven monster's queued attack in the same frame (traced: created, queued, both checks passed,
  then neither current nor queued). The server walks the monster itself, so the skill is non-pathing. Only on the
  skill: on the monster's Stats component it also stops its Move action from pathing (the monsters stood still).
- **AI** (`RunMonsterAi`, `MonsterAiCadence`): decisions run at 50 ms intervals of elapsed game time.
  A late wake runs one decision and schedules the next from the current time; it never replays missed steps.
  Pause freezes game time, and disabled AI or a dead player does not accumulate work to replay afterward.
  Hostile monsters neither chase nor swing while the entry grace period still protects the player.
  Chasers take surround cells around the player (`MonsterSurround`); a negative footprint keeps its direct approach.
  A ranged chaser (its first effect a ranged attack, or a ranged weapon) stands off: its first ring lies one cell inside
  its reach (MonsterVarieties `MaximumAttackDistance`, the Sand Spitter's 30; `MinimumAttackDistance` 6 is not read
  yet) and a full ring sends it inward, so it stops where its attack covers the player instead of walking to the melee
  contact ring (live 2026-10-29: Spitters chasing to the contact ring came in close).
  Chase repaths have a separate 100 ms minimum interval and require at least three cells of Manhattan target change;
  a chaser that stopped short of its cell retries it after arrival, at most every 0.5 s.
  This retains the current walk when the target moves slightly instead of repeatedly restarting its path.
  A hostile monster within reach stops and swings: 0x1ab with its skill, flags 0x0408 | 0x80, target the player,
  sequence 0xffff.
  Its busy period covers the full attack duration from the actual action send; contact still occurs halfway through:
  the hit lands if the monster lives and the player is still within reach + 2 (0x1af, then 0x1b1 on the player).
  A mortar skill (Sand Spitter: `monster_mortar_attack`, GrantedEffects SkillID 183 → MonsterMortar 183 →
  `Metadata/Projectiles/SandspitterMortarProjectile`, 1170 units/s) releases a server projectile object instead
  (`Program.ProjectileHostile.cs`), at the clip's contact keyframe rather than halfway: MonsterMortar's `Animation`
  (row 20, Animation.dat `SpellProjectile`) names the ACT pool (`SpellProjectile = "attack1,attack2"`), the native
  selector seeded with the action's 0xffff sequence picks `attack1` (as it picks the player's wand clip), and that
  clip's `contact` is at 374.5 of 1000 ms in the Spitter's .ao (`exclude_metadata_events = true` replaces the rig's
  416 ms) with position [1.33, 45.89, −71.56]: model units, +x the model's left, −y its front, −z up. With
  `projectile_uses_contact_position` the shot leaves there: 45.9 units (4.2 cells) behind a body that faces away from
  its target (`action_should_face_away_from_target`), so toward the player, and 71.6 units (9 marker steps) up; the
  lateral part is not applied (the grid's handedness is not established and it is under a cell), the offset scales
  with the model when `use_scaled_contact_offset` says so (MonsterVarieties `ModelSizeMultiplier`), and the flight
  still runs from the monster toward the aim (`maintain_projectile_direction_when_using_contact_position`). Data:
  `data/monster-contacts.json` (`scripts/re/monster_contacts.py`: per variety the ACT pools of `Melee` and the
  projectile tables' animation keys, each clip's index, length, contact and position, rig events merged with the .ao
  chain's as `ot_components.rig_clips`). Whether the client seeds a monster action's clip choice from the 0xffff
  sequence is assumed from the wand's selector and still needs a live check (the two clips differ by 75 ms and 1.3
  cells). The lob lands on the player's cell on the shared runtime, no closer to the Spitter than the skill's
  `projectile_minimum_range` (50 units, 4.6 cells: a player at its feet stands in the dead zone), team 2 so no
  monster is in its way, and the same contact pipeline commits the hit when it lands on a player within the splash
  (`Game_MONSTER_MORTAR_RADIUS`, default 10 cells; the skill's stats carry no radius; `projectile_spread_radius` 3 is
  not modelled). Melee swings still land at half the attack time. The
  client creates no projectile of its own for a monster's action (Frida 2026-10-29: 27 Sand Spitter attacks, no
  client-side object), so without the server's object there is nothing to see. Such a skill also carries its stat
  set's own entries beside the attack's numbers (`MonsterProjectileSkillStats`: `action_should_face_away_from_target`,
  `projectile_uses_contact_position`, `use_scaled_contact_offset`, the 3-cell spread and 50-cell minimum range;
  not the effect's cast-time or mana entries), because the client derives none and without the face-away flag the
  Spitter faced the player like a melee swing instead of turning its back to spit, as it does on the live game. The
  object itself plays nothing: the client's Projectile component is inert for a server object, and unlike the wand
  bolt's .ao (`default_animation = projectile_01`) the mortar's declares no default, so it was invisible until the
  Gateway started its rig's flight clip (live 2026-10-29). `data/projectile-clips.json`
  (`scripts/re/projectile_clips.py`, every Projectiles row's first .ao → its rig's animation list) gives each
  projectile type its flight clip (LoopAnimationIds: the mortar's `proj`, index 1, 1 s once, played at spawn by 0x1b2
  at the speed that stretches it over the flight, as `mortar_projectile_scale_animation_speed_instead_of_projectile_speed`
  says) and its impact clip (ImpactAnimationIds: `end`, index 0, 666 ms, played where it lands, the object staying
  that long), and the .ao's default animation when it has one, in which case no flight clip is sent
  (`MonsterProjectilePresentation`). The wand entry agrees with the constants the ordinary wand attack already used.
  The hit lands at the later of the shot's simulated contact and the connection clock: the fortification and recent-hit
  trackers refuse an earlier time than their last read, and the pump once closed a connection over it (live
  2026-10-29). A melee contact commits at the same rule: the runtime's time, read at the send gate without advancing
  it, lagged the life tick's clock read, and the tracker's refusal faulted the swing's task unobserved (review
  2026-10-30; such faults are logged now). Weapon projectile skills (`monster_projectile_weapon`, SkillID 0) take their projectile from the
  monster's weapon art, not resolved yet.
  A pending swing reserves its generation through contact, so a blocked send gate cannot reserve duplicate attacks.
  Contact checks the original monster, action generation, life, range, pause and area lifetime at the send gate.
  Boss stages and emergence retain their own timing; the next ordinary action waits for stage completion's final
  transition, life refill and ActionEnded to pass through the gate together.
  Cadence, repath, attack duration, interruption and queued-stage guards have automated coverage.
  A native Hillock smoke check reached 1/311 life, completed the stage and refill to 311, then resumed ordinary attacks.
  Evidence: `artifacts/wand-ai-validation/ai-native-boss-stage.json` and `ai-after-gateway.log`.
  Three native AI-enable controls issued one chase each after 43, 38 and 50 ms.
  Four deliberate player turns produced four chase orders at 249 ms intervals, without repeated orders between turns.
  A Drowned with a 2.4 s attack started three attacks 2432 and 2428 ms apart; faster decisions did not shorten attacks.
  Actual Escape-menu pause held all 30 enemy samples at (589,368) with no actions.
  After resume, 61 samples advanced to (594,368) along the existing path without another chase order.
  Evidence: `artifacts/wand-ai-validation/ai-native-response-{1,2,3}.json`, `ai-native-turns.json`,
  `ai-native-attack-pacing.json` and `ai-native-menu-{paused,resumed}.json`.
  Those controls predate the surround cells and the merge onto the shared projectile runtime; the combined AI still
  needs a native recheck.
- **Defence.** The monster's accuracy is `DefaultMonsterStats.Accuracy` at its level (raised by its mods); the server
  rolls the player's
  evasion with the same `AccuracyHitChance` formula and sends no life update on a miss. The roll uses PoE's shared
  entropy counter (0–99): each attack adds its hit chance, 100 or more means a hit and subtracts 100, and the counter is
  randomized after 3.33 s without an attack. A hit is a critical strike only when the monster's data gives it a
  chance: monster attacks have none (the monster Melee stat sets' `AttackCritChance` is 0, Monster.ot only sets the
  130% `base_critical_strike_multiplier`), so only the of Deadliness mod makes one crit, at 130% less the player's
  `base_self_critical_strike_multiplier_-%` (at least 100%; [14](14-monster-packs.md) §5). Live 2026-10-26: a normal
  Hungry Corpse never crit, one with of Deadliness every time. The hit then goes through the player's armour
  (`MitigatePhysical`) and block. `/defences` prints the reference values.
- **Player life** (`ClientState.PlayerLife`, from the level/class/passive pools). At 0 the client plays the death
  animation and blocks moving. `/heal` sends full life: 0x1b1 from 0 runs the client's revive. The overhead life bar is
  hidden at 0 (`hide_mini_life_bar`, as for monsters) and shown again on revive.

## 7. The death screen

The client's `death_screen` panel (constructor `0x14028fc80`, setup `0x140290820`) does not open by itself when the
local player dies: **S→C 0x55** opens it (instance group, class `0x1435dce40`, read `0x14219f820`, dispatcher case 0x14 →
listeners at instance +0xf78 → `0x140d5d6a0` → a task → HUD vtable +0x4d0 `0x140d86180`).

```
u16 0x0055, u8 flags: bit0, bit1 = hide, bit2, bit3, bit4
unless bit1: u32 BE (+0x18), u8 (+0x15) when that u32 is non-zero, u16 BE (+0x1c)     (meanings unknown, sent 0)
```

Title "You have died!" (ClientStrings ids are row + 1). Live 2026-10-17:

| Flags | Buttons |
|---|---|
| 0x0c (standard league) | "Resurrect in Town", "Resurrect at Checkpoint" |
| 0x00 | "Resurrect" |
| 0x01 (hardcore) | "Exit" ("Exit to the character selection screen": sends C→S 0x153) |
| 0x02 | hides the screen |

bit3 turns the left button into "Resurrect in Town", bit2 adds the checkpoint button (not with bit0); a third
"Spectate" button belongs to PvP. bit4 and the trailing fields are not mapped (Sanctum and other modes probably use them).

The buttons (`0x140290bf0`) send **C→S 0x54**: `u16 0x0054, u8 flags`, bit0 = "Resurrect in Town" (1) or the
checkpoint / plain "Resurrect" (0). The Gateway (`Resurrect`): hides the screen (0x55 bit1); in town → area change to
the character's last town (`LastTown` in Auth's "resolve" answer, the town the next login would start in; the new player
object has full life); at the checkpoint → 0x1b1 full life first (a teleport sent while
dead was ignored), then 0x1b7 next to the area's waypoint if the character has activated it, else to the area's arrival point
(`AreaLayout.Checkpoint`). `Game_LEAGUE=hardcore` sends 0x01;
`/death_screen [flags]` previews any variant.

Actual Hardcore deaths commit migration to the matching softcore league, preserving SSF.
Hardcore Ruthless deaths are permanent and remain stored in Void.
Resurrection packets are refused for the old Hardcore connection, including forged packets.
See [league modes](../game/league-modes.md) for persistence, stale-ticket checks and storage-failure retry.

## 8. Stat keys

**Client stat key = Stats.dat row + 1** with the vendored schema: every key read during an attack matches
(0xf5 → row 244 `movement_velocity_+%`, 0x7d → 124 `base_maximum_life`, 0x144 → 323
`main_hand_minimum_attack_distance`, 0x35a → 857 `is_totem`, 0xce6 → 3301 `virtual_action_speed_+%`, …).
`scripts/re/frida/trace-skill-stats.js` logs the keys, values and callers of the stat getter `0x1424cfa40` while an
action is created (`0x141941c90`) or started (`0x141e8cf10`); `dump-stats-object.js` dumps the stored (key, value)
vector (stats +0xf0) of whichever stats object answers a key; `scripts/re/statkeys.py <key>…` lists the client code
that reads a key (`mov edx, key` + call). Stats like `main_hand_total_base_weapon_attack_duration_ms` are read by many
small calculators in `0x141b..`–`0x141d..`.

## 9. Gem skills: stats and casting

Live 2026-10-18 with the Witch's Fireball. Code: `SkillCatalog.EffectStats`, `GemSkills`, `CastOn`, `LaunchProjectile`
(Gateway).

- **Skill ids.** Gem skills take the ids after the default attack's that no ActionType `HASH16 & 0x3fff` uses
  (`SkillCatalog.SkillIds`): 0x0106, 0x010a and 0x0114 are basic actions in 3.29.3b, so plain counting from 0x0103 would
  have replaced one. Fireball is 0x0103.
- **Stats** (`SkillCatalog.EffectStats`): the effect's GrantedEffectStatSets row (ImplicitStats = 1, ConstantStats with
  ConstantStatsValues), its GESSPL row at the gem level (FloatStats with BaseResolvedValues, AdditionalStats with
  AdditionalStatsValues), the cast and the cost. Fireball level 1: `spell_minimum/maximum_base_fire_damage` 9–14,
  `active_skill_base_area_of_effect_radius` 10, `base_chance_to_ignite_%` 25, `base_is_projectile` 1, mana cost 5. The
  skill's list also carries the player's stats.
- **Cast time.** The client reads **`virtual_base_spell_cast_time_ms`** (0x24b9) when it starts the cast; without it
  the cast was instant (`trace-skill-stats.js`). The Gateway sends GrantedEffects `CastTime` (750 ms) there and in
  `base_spell_cast_time_ms`, and `hundred_times_casts_per_second`. `mana_cost` (0x2e72) is what the client spends.
- **Casting.** At the ground: C→S 0x25 with the gem skill's id. A **spell** stops where it stands and the server echoes
  0x1ab from there (no walk). An **attack** (ActiveSkillType Attack: Holy Strike, Ground Slam …) follows bit 0 of the
  message's flags, the client's **"Always Attack Without Moving"** setting (live 2026-10-25: 0x400 with it off, 0x401
  with it on; the setting itself also arrives as C→S 0x16 command 8):
  - bit 0 set: the player swings from where it stands; the server echoes 0x1ab there and pays at the swing.
  - bit 0 clear: a point **within the weapon's reach** is a swing at the ground, even with nothing standing there (live
    2026-10-26: standing and swinging at empty ground must cost the skill's mana, as it does on the client); the server
    echoes 0x1ab in place, pays the cost and runs the skill's area hit. A point **beyond** the reach is an
    **attack-move** — the client walks to the aimed point itself and sends no C→S 0x25 Move for that walk, so the server
    walks its estimate after it (`Walk.Order`) and answers 0x1ab from that estimate, like a move's echo; it costs no
    mana. A monster inside the skill's hit radius adds nothing to the decision: within reach the point is a swing (the
    hit still covers every monster within the skill's radius of it, the client aiming at the cell rather than the
    object: Holy Strike on a monster sends 0x25, live 2026-10-25), beyond reach it is an attack-move.
    The client spends the skill's mana on every use it starts, including an attack-move it only walks for, so the
    Gateway answers an attack-move with the authoritative mana (S→C 0x1b1, at most four a second); otherwise the orb
    falls behind the server's and recovery flasks are refused as "full" ([13](13-items.md) §8).
  On a monster: C→S 0x24; an attack walks into the weapon's reach first (`Walk.Approach`, the swing is echoed on
  arrival, unlike a spell) and a spell casts from where it stands. An attack with "Always Attack Without Moving" on
  casts in place. On the player itself (Righteous Fire): C→S 0x24 with the player's id as the target; the server
  echoes 0x1ab in place.
  An **instant** skill (stat `skill_is_instant`, below) is not sent this way.
  C→S **0x191** carries an object target (8 bytes: u32 object id, u16 skill id).
  C→S **0x190** carries a ground aim (12 bytes: u32 x, u32 y, u16 skill id), without an action sequence.
  Sender `0x1401232e0` selects 0x190 for target mode 4 and 0x191 for object/self modes 5/6.
  The 0x190 class `0x1435c0718` writer `0x141f91f60` confirms network order and these widths.
  Actual Frostblink Q sent `0190 0000011A 000000DC 0103`; unframed, this discarded the rest of its receive chunk.
  Both messages now frame independently, including fragments and coalesced following messages.
  Only an equipped Instant gem may use either route; movement currently supports ordinary Frostblink only.
  The server applies the effect and answers with S→C **0x1ce** (`InstanceProtocol.InstantAction`:
  object header, u16 skill id, u16 sequence (0xffff), u16 flags (0x20 → u32 target object), compact target position;
  handler `0x142195880`): the client runs the skill's action without animation and consumes a use of the skill
  (actor vtable +0x198), which starts the skill bar's cooldown. Without it the tooltip showed the cooldown but the bar
  never ran it: the cooldown records had no entries (Frida, live 2026-10-21; not yet checked with 0x1ce).
  A location acceptance has flags 0, no object id, sequence 0xffff and the original ground aim.
  Ordinary Frostblink uses the existing mana and cooldown checks but skips the generic single-area hit approximation.
  Its server movement commits inside the 0x1ce send gate after repeating life, area, gem and placement checks.
  IceDash action `0x1412f1200` records its departure and aim; endpoint resolver `0x141bfe240` clamps distance
  to an integer maximum `42 * (100 + skill_maximum_travel_distance_+%) / 100` and a 32-cell minimum.
  Component rounding is `floor(component * factor + .5)` in `0x140671d00`, including negative components.
  The server uses the same emitted player/skill stat precedence, refuses overflow and unsupported minimum/maximum cases,
  and requires a captured grid, in-bounds aim/endpoint, clear straight terrain, quest barriers and endpoint clearance.
  It rejects cases needing the client's terrain endpoint search instead of guessing a destination.
  Manual native 0x1ce experiments confirmed near aim `(290,229) -> (300,220)` resolves to `(314,208)`;
  far aim `(311,220) -> (200,220)` resolves to `(269,220)`.
  While walking from `(279,221)`, aim `(310,220)` resolved to `(311,220)` and ended the old predicted Move.
  These manual protocol experiments establish resolver and walk-stop behavior separately from production validation.
  The server mirrors that walk stop with `Walk.Teleport`, preserving ActionCount, channel and pending release tokens.
  Hovering a monster sends Frostblink as **0x191**, rather than 0x190; the former nonmovement-only gate rejected it.
  The object confirmation uses flags 0x20 and the target id, with its raw cell as the coordinate fallback.
  Handler `0x142195ab1..0x142195b0e` binds the entity through `0x14193e0a0` and replaces the packet aim with its
  Positioned cell. IceDash `0x141bfe1e0` and `0x141bfe240` then route a bound object through `0x141bfe610` before
  applying the usual distance limits. Simply passing the monster's centre to the ground resolver is incorrect.
  The object resolver first extends a short direction to 32 cells, rounds its components, then normalizes that
  rounded vector again and extends it by player Positioned size + target Positioned size + 2.
  Positioned size is `object_size - 1`; the normal player has size 1, and Ash Harvester ObjectSize 3 gives size 2.
  `0x141c02050` traces those candidates in inclusive Bresenham order, retaining the last valid cell.
  Its first terrain-layer threshold can abort the trace; other terrain or target-footprint failures skip a cell.
  The target-footprint check is `0x141c02c30 -> 0x141c03c80`; the outer resolver may then search terrain again.
  The server accepts only a wholly clear candidate extension and the existing conservative final placement.
  Partial traces, coincident targets requiring seeded random direction, unknown sizes and other object types reject.
  Both preflight and the actual confirmation gate require a living monster whose current walk has ended.
  The object id makes the client resolve its target again on delivery, so moving-target parity is not claimed.
  At the gate the current equipped gem, player/area state, target, sizes, raw cell and placement are checked again;
  one game-time sample observes both walkers. No invalid preflight pays mana or reserves a cooldown.
  A start invalidated after payment retains the existing accepted-use cost policy, but sends no late confirmation.
  The object route skips generic area damage and preserves action/channel tokens, as the ground route does.
  Manual native object confirmations from `(280,220)` with player/target sizes 1/2 established these clear cases:
  target `(300,220)` traces `(312,220)..(317,220)` and lands `(317,220)`;
  target `(330,220)` traces `(330,220)..(335,220)` and lands at the 42-cell clamp `(322,220)`;
  target `(300,240)` traces `(303,243)..(307,247)` and lands `(307,247)`;
  target `(300,230)` traces `(309,234)..(314,236)` and lands `(314,236)`.
  The last case distinguishes normalizing the rounded minimum vector from reusing the original direction.
  Reflected target `(260,220)` computed `(248,220)..(243,220)`, but terrain retained `(248,220)` and the native
  fallback landed `(264,220)`; the server rejects such partial traces instead of guessing that fallback.
  Evidence is `logs/instant-location-object-manual-{near,far,diagonal,offaxis,reflected}.json` in integration.
  These are injected native protocol experiments, without server movement or production cost acceptance.
  Actual hovered Q before this extension produced 0x191, no confirmation, no cost and no movement.
  Production hovered Q now passed from `(280,220)` against stationary target `(300,220)`, with sizes 1/2.
  The native request hook recorded message 401 (`0x191`), skill 259 and target 12288.
  Exactly one 12-mana cost changed mana 175 to 163 and emitted one object-bound 0x1ce.
  Its flags were 0x20, target id 12288 and raw aim `(300,220)`.
  Native position and server `/pos` both became `(317,220)`.
  A framed ground 0x190 retry after 0.5 seconds produced no extra mana cost or confirmation.
  Combined #197 progress synchronization retained base 3000 ms, total 3 seconds and recovery rate 1.
  Near evidence: integration `logs/instant-location-object-live-near.json`.
  Final evidence: integration `logs/instant-location-object-live-near-final.json`.
  Framed production 0x191 controls from `(280,220)` matched native/server endpoints for two stationary targets.
  Off-axis target `(300,230)` reached `(314,236)`; far target `(330,220)` reached `(322,220)`.
  Each accepted control paid 12 mana once and emitted one object-bound confirmation.
  A framed request on a target walking from `(330,220)` toward `(283,220)` was rejected without cost or movement.
  Reflected partial terrain, missing target `0xdeadbeef` and noninstant Fireball 260 were also rejected.
  Those three framed invalid requests left mana 175, native cooldown entries empty and position `(280,220)`.
  They emitted no 0x1ce.
  These are production framed-request controls, distinct from actual hovered Q and injected native RE confirmations.
  Framed positives: integration `logs/instant-location-object-framed-{offaxis,far}.json`.
  Moving rejection: integration `logs/instant-location-object-framed-moving-reject.json`.
  Other invalid cases: integration `logs/instant-location-object-framed-invalid.json`.
  End-to-end stationary validation subsequently passed with actual Q input from `(280,220)`:
  raw aim `(308,221)` resolved to native/server `(312,221)`.
  Requests after 0.3 seconds, with noninstant Fireball, or with an unsigned-invalid x coordinate were rejected.
  Rejections sent no confirmation and changed no mana or position.
  A retry after 3.2 seconds aimed at `(300,220)` and reached native/server `(280,218)`.
  Exactly two accepted uses paid 12 mana each, generated two ground confirmations and consumed two native uses.
  The combined validation also included the separate #197 cooldown progress synchronization after each acceptance;
  its record retained base 3000 ms, total 3 seconds and rate 1.
  No 0x190 request remained unframed.
  Evidence: `logs/instant-location-live-default.json` in the integration validation checkout.
  Production ground Frostblink use during an active walk has not been measured;
  the walking evidence is manual native RE.
  Departure/arrival damage, chilled ground, nearby-enemy cooldown recovery, shared Blink cooldown groups,
  other location instant skills and complete movement restriction/terrain searches remain unimplemented.
- **Hit reaction.** A surviving monster receives S→C **0x1d1**, `ActorAnimation` type 5 (`TakeHit`), for recovery.
  The local eligibility policy requires a positive hit of at least one tenth of maximum life, a known TakeHit profile,
  no stun immunity and expiry of its previous recovery.
  A killing blow gets no flinch because the client's death runs instead.
  The duration policy randomly chooses one extracted clip's length, with a 0.4 s minimum; this is a recovery profile,
  not a forced native rig variant or a complete official stun formula.
  An accepted flinch now stops the server walk at its current game-time cell, clears the chase target and advances
  the monster's action generation, permanently invalidating queued old movement, swing echoes and contact damage.
  AI waits until the same recovery ends; a cancelled ordinary swing releases its old busy period, while boss-stage
  and emergence timing remain intact.
  The connection's send gate commits native `ActorAnimation` (0x1d1), type 5 (`TakeHit`), with the same requested
  recovery duration as `FlinchUntil`. The actor's ACT selects the actual presentation variant; the extracted clip
  profile supplies the duration policy rather than forcing a rig index. Neither rig-only 0x1b2 nor 0x1ac followed
  by 0x1b2 stops native movement: an initial moving-Drowned probe kept sliding despite its accepted hurt clip.
  Do not append 0x1b2 after 0x1d1, which would replace the native recovery animation's completion lifecycle.
  Projectile contacts (ordinary arrows and bolts, skill projectiles) commit the same 0x1d1 from the runtime's hit
  (`CommitProjectileHit`) instead of a rig-only 0x1b2 clip, which left the client monster walking while the server's
  estimate had stopped. The static 0x1af decode (`research/poe-rea-reference`,
  `Agents/docs/re/action-hit-packet-3.29.3b.md` §4) agrees: 0x1d1 and the hit packet's stun share one interruption
  routine, and 0x1b2 only replaces the clip.
  Native type-5 TakeHit fits its non-looping clip to the requested duration. Verification on 2026-10-26 stopped a
  Drowned at (582,367): all 46 samples from 40 to 950 ms after the hit retained that cell and a zero movement step;
  enabling AI afterward started a new walk. Evidence: `artifacts/combat-validation/enemy-moving-flinch-final.json`
  and `enemy-movement-resume-final.json`.
  Monster identity, death, removal, area transfer, disconnection and pause are checked at that actual send point.
  Ordinary monster swing contact also commits under its original action generation at the send gate and waits on
  game time, so pause cannot consume its remaining windup and recovery expiry cannot revive an interrupted hit.
  `data/monster-takehit.json` (`python -I scripts/re/monster_takehit.py`) holds each MonsterVarieties
  row's index(es) and clip lengths: its `.act` maps `TakeHit` to a clip, and the `.ao`'s `AnimationController` names
  the rig. A rig whose parsed animations do not add up to the `.amd`'s count line is skipped (a missed entry would shift
  every later index to a wrong but in-range clip). 26 `.act`
  files map a pool — `flinch_01, flinch_02, flinch_03`, or `flinch_small:max_duration=0.45, flinch_large:min_duration=0.45`
  (the native condition selects by the requested recovery duration).
  The server randomly chooses a duration profile; the actor's native ACT chooses the actual presented variant.
  Every other variety has a single clip.
  An `@Name` alias is expanded to the whole list it names, and every
  `extends` parent is followed (not just the first), with "nothing" ending the chain. The client also has the directional
  types TakeHitForward/Backward/Left/Right, but the `.act` files that set them alias them all to `@TakeHit`, so they add
  no variants. 0x262 (a Life "changed by" event with a signed delta) reaches the Life listeners but plays no flinch,
  sound or blood.
- **Player hit recovery.** S→C **0x1d1** selects an `Animation.dat` type through the actor's current class/stance ACT.
  `InstanceProtocol.ActorAnimation` writes object header, BE float seconds, u8 animation type, u8 boolean.
  Type **5** is `TakeHit`; this is not a rig clip index.
  Reader `0x141fde9d0` widens the type to message `+0x24` and stores the boolean at `+0x28`.
  Actor handler `0x1421949c3` calls virtual `+0xe0`, `0x141e95e60`.
  It ends predictive action `+0x1b8` through `0x141e90740`, cancels queued work, and sets Actor `+0x219` bit 2.
  Action gate `0x141e8d900` rejects starts while that bit is set.
  Completion `0x141e934b0` clears the bit and resumes queued/idle processing.
  Ground bow attacks reserve their pacing and token under the same recovery gate; queued arrow creation checks it.
  Live on a captured Strand, a 0.5 s packet stopped predictive walking and a subsequent cast.
  The predicted/current action became null, movement stopped, and recovery cleared after about 0.508 s.
  A new cast worked afterward.
  Integrated copied-save tests used a level 1 Witch (57 life, no ES) and Ash Harvester row 717 at level 7.
  Actual 24/27 physical damage hits stopped client prediction and the server walk estimate at cell (283, 216).
  TakeHit selected Witch clip 177 and cleared Actor `+0x219` bit 2 after the requested 429 ms recovery.
  A slow Fireball cast was cancelled before release, with no projectile created.
  An already released Fireball continued through recovery to impact and removal.
  A bow-equipped copied character also lost a pending Burning Arrow before release after an actual monster hit.
  An ordinary ground arrow request during recovery produced no action echo or projectile; a fresh request worked afterward.
  Burning Arrow released before the hit continued to removal, while native recovery blocked the new arrow request.
  With `cannot_be_stunned = 1`, another actual 24 damage hit sent no 0x1d1 and did not set the recovery flag.
  Evidence: `logs/monster-hit-recovery-{walk,cast,flight,immune}.json` in the validation checkout.
  In contrast, 0x1b2 merely replaced the clip while movement or server cast release continued.
  Its Play ABI is `0x141dba530(controller, &resourcePair, index, speed, ...)`, with the index in **r8d**.
  Actor `+0x1a0` is the body controller; its main state is `+0x180`, clip index `+0x190`, elapsed `+0x1a4`.
  `trace-player-hit-animation.js` records these fields and Positioned movement without sending packets.
  The trailing 0x1d1 boolean enables a client avoid-interruption roll; the server sends false and decides eligibility.

  The supported slice is surviving, unblocked **physical monster melee**.
  [PoB's defence calculation](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcDefence.lua#L2154-L2250)
  gives effective physical damage a 25% bonus, then rolls `min(100, 200 × effectiveDamage / stunThreshold)`.
  Base chances at or below 20% are discarded before applying unconditional stun avoidance.
  The life threshold includes unconditional `stun_threshold_+%`; known stun immunity is respected.
  Duration uses PoB's 350 ms base, the extracted Monster.ot's 20% more melee stun duration, self duration modifiers,
  the player's final stun recovery and the current action-speed factor, rounded upward to a 33 ms frame (normally
  429 ms at 0% action speed).
  Invalid stats yielding a nonpositive duration or more than 60 s disable recovery rather than locking input.
  `SendPlayerHitRecovery` commits cancellation inside the send gate after rechecking survival and active recovery.
  It increments `ActionCount`, clears channel hits, and stops `PlayerWalk` at its current cell.
  Pending melee, casts and area hits retain their original token across awaits; released projectiles keep flying.
  Projectile creation rechecks the token inside the send gate; cancellation resets ordinary attack pacing too.
  Buffered 0x24/0x25/0x197 inputs are rejected using socket-read game time through the recovery end.
  Object walk-ups reserve movement and the original token under the same recovery check.
  Their delayed continuations and queued action/teleport echoes recheck that token.
  Recovery does not restart while active; flasks and instant skills keep their existing paths.
  This does not implement block-recovery animation/timing, ES stun avoidance, CI/mana/ES threshold substitutions, conditional defences,
  attacker-specific stun modifiers or monster spell/ranged stuns.
  Effective maximum ES or published base ES disables this slice until ES avoidance exists.
  Suppressed or converted raw equipment ES does not disable it by itself.
  This includes Discipline's effective ES pool and its published flat ES source.
  Conditional stun-defence builds, flat/substituted thresholds and CI are also skipped conservatively.
  An equipped skill with `skill_cannot_be_stunned` or contact-phase immunity disables this slice for the build.
  Its precise active action phase is not tracked, so even an inactive equipped skill is conservatively excluded.
- **Hit effects (2026-10-25).** The target's blood and the weapon impact sound come from S→C **0x1af** "action hit"
  (`InstanceProtocol.ActionHit`; class `0x1435ddb38`, read `0x141fddba0` → `0x1419bd6f0`, Actor handler `0x1421940a0`
  → `0x141e90f50`): object header (the attacker), u16 action sequence (must match the action's), u8, u8 skill index
  (0 = the current action), u32 target id, u16 flags, u8 kind (bit 0: belongs to an action, bit 1: hit data follows).
  With bit 1: a u16 if `flags & 0x400` (`flags >> 10 & 1` in the reader), u8 code, u8 hit angle (0..255 a full turn,
  `atan2(dx, −dy)` from attacker to target), then a u16 for kind bit 2 and one for bit 3. The handler calls the
  attacker's `ProcessDamagePackageInternal` (vtable +0x230), which plays the effects; 0x1b1 stays authoritative on
  life. The server sends it before 0x1b1 on the default attack and on attack-gem hits (flags 0x4201, kind 3) and on a
  monster's hit on the player (kind 3, sequence 0xffff, flags 0x4201 plus 0x0040 when blocked and 0x0020 when critical,
  code = the largest damage component: 0 Physical, 1 Cold, 2 Fire, 3 Lightning, 4 Chaos; `HitCode`) and on an
  ordinary arrow's or wand bolt's contact (kind 3 with the shot's own action sequence, flags 0x4201 plus 0x0020 on a
  critical strike, code by type, the angle from the flight direction: blood, particles and the main-hand weapon
  effect, as GGG's server sends them on a bow hit); spells and gem projectiles skip it. The monster hit used to go out as 0x4601 with a 450 ms duration and kind 2. The static decode
  (`research/poe-rea-reference`, `Agents/docs/re/action-hit-packet-3.29.3b.md` §§1–4) shows that kind bit 0 clear
  only queues the package until an action with that sequence consumes it, which the fabricated 0xffff never
  guarantees, and that 0x0400 asks the client to stun the target itself, with its own avoidance roll, through the
  same interruption routine as 0x1d1; the player's recovery is already the authoritative 0x1d1 below.
  GGG's server does it differently (live capture 2026-10-29,
  [ranged-monster-actions-port.md](../dev/ranged-monster-actions-port.md) "Live reference").
  A bow's arrow hits, both the default attack's and the attack gem's, carry the main-hand bit `0x0200` and skill
  index 2, and the header after the attacker's id is a per-hit u32 and u16 1, not zeros. The arrow's packet carries
  the main-hand bit since 2026-10-30; index 2 and the header stay zero until their meaning is known (the decode says
  index 0 searches the current action).
  A Sand Spitter's hit on the player (live 2026-10-30) is kind 3, flags 0x4201 (0x4221 on a crit), code 0, skill
  index 2, with the sequence of the monster's own action (0x80c0…, from its 0x1ab), not 0xffff.
- **Release.** A spell leaves at its cast animation's **contact** keyframe, which the client plays stretched to the cast
  time: the Witch's `spell_projectile_wand` in `Art/Models/CHARACTERS/Witch/rig.amd` is 1066 ms long with `contact` at
  599 ms, so Fireball leaves after 56% of the cast. `python -I scripts/re/anim_contacts.py` writes every class's
  contacts to data/animation-contacts.json (`AnimationContacts`); the Gateway waits cast time × that fraction.
- **Cast speed (#192).** Genuine Spell/non-Attack skills keep the catalog milliseconds in both
  `base_spell_cast_time_ms` and `virtual_base_spell_cast_time_ms`. The native spell action already divides that
  virtual base by `1 + cast_speed_+% / 100`; pre-dividing it on the server applied cast speed twice.
  `SpellCastTiming` adjusts only the server duration and final `hundred_times_casts_per_second`, using the same
  computed cast-speed total. A 750 ms Fireball at +100% therefore takes 375 ms, and at -80% takes 3750 ms;
  the panel rates are 2.67 and 0.27 casts/s. The outgoing virtual base remains 750 in both cases.
  Native 3.29.3b evidence: `0x1419e7660` reads key 0x24b9 at `0x1419e7683`, divides by 1000 at
  `0x1419e7698`, then divides by one plus getter +0x498 / 100 at `0x1419e76a0..0x1419e76bd`.
  Its speed getter `0x1419e7500` ordinarily reads `cast_speed_+%` (296) at `0x1419e763c`.
  The alternate action `0x1419eef50` uses the same formula with getter +0x788.
  In the earlier recovery capture (`logs/monster-hit-recovery-cast.json`), a 1.066-second clip at playback speed
  0.0568533279 took 18.7500015 seconds: the old 3750 virtual milliseconds were divided by 0.2 again.
  Real-client Q casts in the combined validation build now have native clip durations 3.750000157 seconds at
  -80%, 0.375 seconds at +100%, and 0.75 seconds at zero. The native virtual base was 750 in all three captures;
  emitted cast rates were 27, 267 and 133 hundredths. Duration was measured as clip length / playback speed.
  Captures: `logs/skill-timing-after-fireball-{minus80,plus100,default}-real.json` in the integration checkout.
  These checks establish native clip duration; projectile release/contact alignment was not separately measured.
  Server projectile release, area-spell delay and the Portal spell's cast wait use the adjusted duration once.
  Catalog instant skills retain their instant flag, zero server cast duration and no finite casts-per-second rate.
  A channelled spell's native base is also preserved; this does not implement beam damage or change the existing
  server channel-hit cadence. Attack and non-spell timing paths are unchanged.
  For non-instant spells, a nonpositive speed factor or a duration exceeding signed integer milliseconds has
  no server duration, no hit/effect or mana cost, and zero displayed cast rate. Positive server durations round
  to milliseconds with a 1 ms numeric floor; this bound is not an official cast-speed cap. Conditional action-speed
  rules, skill-specific speed substitutions and further native timing mechanics remain outside this fix.
- **Instant skills.** The client knows an instant skill by its stat **`skill_is_instant`** (0x2064), not by the
  ActiveSkillType `Instant`; the server derives it from the type and sends it with `base_skill_is_instant`
  (`SkillCatalog.EffectStats`). Without it Haste's and Righteous Fire's actions used the spell action class (vtable
  `0x1434adb70`, as Fireball's) and waited for a cast that never played: the action stayed the player's current
  action (+0x1b0, flag +0x218 bit 0x02), and the queued-action gate `0x141e8d900` refused every other skill (only
  Move passes it) until a move replaced it (Frida, live 2026-10-21). With the stat the client sends the skill as 0x191
  (above) instead. The 0x1ab handler skips a remote actor's action of a skill with 0x2064.
- **Cooldowns.** The client keeps a skill's cooldown itself (manager +0x170 records, cooldown group from the data, 36 =
  per skill; entries `{remaining s, total s, recovery rate, -NaN}` added by `0x141eebfe0` when the skill is used), but
  only when the skill's stats have **`virtual_total_cooldown_count`** (0x4806; `0x141eea440` returns no cooldown
  otherwise). The duration comes from the data (or `skill_cooldown_time_override_ms`) plus `virtual_cooldown_modifier_ms`.
  `SkillCatalog.EffectStats` sends `base_cooldown_count` and `virtual_total_cooldown_count` = GEPL StoredUses for
  skills with a GEPL Cooldown. The client creates the cooldown record when the skill is added (`0x141eea6a0`) and
  needs a cooldown time there: the skill's +0xc8, which comes from the **skill record** (the third u32 of the record's
  `u32, u32, u32, u8` → +0xc0, +0xc4, +0xc8, +0xcc in `0x141ee73d0`), not from the data; or the stats
  `skill_cooldown_time_override_ms` / `skill_cooldown_is_4_seconds`. With 0 there the client kept no cooldown and the
  bar showed none (Frida: no records, +0xc8 = 0; live 2026-10-21); `GrantedSkill.CooldownMs` now carries GEPL Cooldown.
  +0xd0 (the cooldown group) comes from the GEPL row's CooldownGroup (+0x30).
  For ordinary one-use spells/attacks, the native base remains GEPL Cooldown; the final recovery percentage is
  player-computed `virtual_cooldown_speed_+%` (3398) + the skill's own base (3397) and virtual (3398) inputs once.
  Dash quality 20 adds 10%; the player's base input is already included in its computed virtual value.
  `SingleUseCooldown` shares this resolution between granted skill stats and server readiness.
  The server retains remaining **base milliseconds**, subtracting elapsed game-clock time times the recovery rate;
  stat/skill refresh settles the old rate before installing the new one, and pause advances no game time.
  Its ordinary preflight atomically checks readiness, reserves one use, and changes action/movement under `Monsters`
  before an action echo or effect can run.
  A queued continuation keeps the original action/area ticket; cancellation consumes the accepted use without a refund.
  An attack approach only checks readiness, then reserves at arrival under its original action token.
  Instant 0x191 skills preserve the current action.
  A projectile's creation gate ends action cancellation; released flights continue with their existing area lifetime.
  Multi-use, extra-use, duplicate same-effect gem, flat/override and pause-while-active mechanics retain legacy
  `SkillReadyAt` raw deadlines.
  Shared cooldown groups, bypass, conditional/enemy recovery, typed/more recovery and official tick equivalence are
  outside this generic one-use implementation.
  A zero recovery rate stalls a spent use.
  Extreme input sums are bounded to avoid signed native `percent + 100` overflow.

  Native evidence: Actor +0x598 owns the manager; its +0x170/+0x178 range holds 0x48-byte records.
  A record's +0x10/+0x18 vector holds 0x10-byte entries: remaining base seconds, total base seconds, recovery rate,
  flags; record +0x30 is uses, +0x34 base ms, +0x3c skill id, +0x40 group.
  Constructor `0x141eebed0` reads skill +0xc8 unless an override applies.
  `0x141eebfe0` takes the cooldown object at record +0x10 in RCX, recovery percent in EDX and group propagation in R8B;
  it stores base ms / 1000 and `(100 + percent) / 100` separately.
  Tick `0x141eebdb0..0x141eebdc8` subtracts elapsed time times rate.
  `0x141eeb7aa..0x141eeb7e7` can refresh active rates, but ordinary skill replacement does not call it for a positive
  rate change: `0x141eead13..0x141eead4a` only checks base/count changes or an existing zero rate.
  Likewise, SkillStats' `0x14219465b..0x1421946d7` checks modifier/count/override changes, not recovery percentage.
  Therefore updated 3398 alone leaves an active native entry at its old positive rate.
  The cooldown-only Actor 0x40 trailer above fixes that without replacing a skill or consuming another use.
  `0x142197d24..0x142197e7b` reads its entries; `0x142197f90..0x142197fd8` replaces the matching cooldown's vector.
  `InstanceEntities.PlayerSingleUseCooldowns` sends remaining/total **base** seconds, recovery rate, flags `0xffffffff`.
  An empty vector clears a use that expired while the update waited; native base duration and group are untouched.
  Only eligible spent uses whose original action/instant acknowledgement reached its gate can request this refresh.
  The packet's factory samples game time and resolves the current skill id/effect map under `Monsters` at the actual
  send gate, after the skill-stat update; dead/old-area states and recycled skill ids cannot refresh the wrong entry.
  Unsynchronized progress stays dirty through rate round trips such as 1 -> 2 -> 1,
  including changes before acknowledgement.
  Rejected/unacknowledged uses send nothing.
  Instant acknowledgements request one progress snapshot even at an unchanged rate: native use consumption starts at
  0x1ce, so its fresh full budget must align with the original server reservation after time spent queued.
  Ordinary casts keep their native predicted initial timing and refresh only changed progress.
  A refresh replaces one entry, including zero-rate transitions, rather than appending or restarting it.
  Initial ordinary prediction/network alignment and official tick boundaries are not claimed exact.
  Pre-fix real-client Dash at +100% held base 2.5 s/rate 2 and completed natively within 1.4 s, but a raw 0x25 retry
  was echoed/moved before the server rejected it for 1132 ms remaining (`logs/cooldown-before-dash-plus100.json`).
  Automated checks cover 0/+100/-50%, source aggregation, quality, changing rates, pause,
  and atomic stale-use rejection.
  Real-client Dash checks pass for initial +100% (retry at 1.4 s), -50% (3 s refused, 5.2 s accepted), and an early
  0.3 s raw request with no echo, movement or mana charge.
  Pause held native remaining base time exactly through 1.2 s of wall time and refused the paused retry.
  Artifacts: `logs/cooldown-after-dash-{plus100,minus50,early,pause}.json` in the validation checkout.
  The initial rate-change check exposed the native positive-rate cache above: the server changed rate at 0.5 s,
  while the client retained rate 1 (`logs/cooldown-after-dash-rate-change.json`).
  A manual wire experiment replaced Frostblink's active entry with remaining 2.5/total 3/rate 2, retaining exactly one
  entry and native base 3000; this verified the reader, not the production refresh path
  (`logs/cooldown-manual-actor40-progress.json` in the validation checkout).
  The production Dash rate-change check now passes: an actual Q use retained native base 2500/total 2.5 seconds,
  and +100% recovery at 0.5 seconds changed the one existing entry from rate 1 to rate 2 without resetting progress.
  A raw retry at 1.3 seconds was refused without an echo or mana cost; at 1.7 seconds the native entry was empty and
  the next use succeeded. Exactly two accepted uses produced two action echoes and two mana costs.
  Artifact: `logs/cooldown-after-dash-rate-change-fixed.json` in the validation checkout.
  Layout, active/expired/zero/pause, rate-roundtrip and queued encrypted-send checks also pass.
  Frostblink's C→S 0x190 location-instant handling belongs to a separate contribution; it is not implemented here.
  [PoB](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/dev/src/Modules/CalcOffence.lua)
  also divides base duration by recovery, then rounds ordinary one-use durations to its server tick.
  This server keeps its existing continuous game clock and does not claim that tick boundary behavior.
- **Mana.** The server keeps the player's mana (`PlayerMana`): a cast pays the skill's `mana_cost` first (refused
  without enough), and `RunPlayerTick` regenerates 1.75% of maximum mana per second (Character.ot
  `mana_regeneration_rate_per_minute_%` 105) plus `base_mana_regeneration_rate_per_minute`, raised by
  `mana_regeneration_rate_+%`. Changes go out as 0x1b1 with pool 1. The same two formulas (`PanelStats.LifePerMinute`
  and `ManaPerMinute`) are sent per minute as `total_life_recovery_per_minute_from_regeneration` and
  `total_mana_recovery_per_minute_from_regeneration`, the Defence tab's Life / Mana Regeneration per Second rows,
  which the client divides by 60 — so the panel shows the regeneration the tick applies. An **attack** also waits for
  the shared attack timer (`AttackTiming`): the client re-aims a held attack every 50 ms, so before 2026-10-25 every
  use was a swing (Holy Strike: twelve a second, 6 mana each). Attack gem skills and the default attack share
  `ClientState.NextAttackAt`, a use may arrive a quarter of an attack early, and a use that arrives sooner is ignored:
  no cost, no swing, no 0x1b1. The client does not deduct the cost itself (a live trace of its Life pool showed the
  mana change at the server's 0x1b1), so no "cost back" update is needed.
- **Ground attack in dual wield crashes the client: the off hand's weapon stats were missing (issue #167, fixed
  2026-10-25).** A dual-wielded attack swings the off hand too, and the client sets that strike's weapon up from the
  hand's **own** final stats; it derives none (§4). The Gateway sent only `main_hand_*`, so the second weapon had no
  attack time, damage or reach on the client at all. A Holy Strike use on *empty ground* — an action the client starts by
  itself (`0x141e8cf10` called from `0x141e8d8c7`, its own click, not from the 0x1ab handler) and repeats one attack time
  later on its own — then animated the off-hand weapon (`holy_strike_animates_owners_main_hand_weapon`) with a null
  animation set: the weapon's `AnimationController` holds an animation set at +0x180 (Frida: rig animation id 493/494 in
  dual wield) and drops it when the animation ends, and the next action's first update (`0x141c98700`) found +0x180 = 0
  and faulted at `mov r10, [r11+0xc8]` (0x141c988c1, access violation at 0xc8). Frida 2026-10-25 while the client builds
  the action read `off_hand_maximum_attack_distance` = 0 where the main hand's was 11. The Gateway now sends both hands'
  weapon stats from `WeaponStats()` (`WeaponFor` + `OffhandWeaponFor`, `SkillCatalog.Unarmed.OffStats`):
  `off_hand_minimum/maximum_physical_damage` (0x119/0x11a), `off_hand_minimum/maximum_total_damage` (0x2a0/0x2a1),
  `off_hand_base_weapon_attack_duration_ms` (0x143), `off_hand_total_base_weapon_attack_duration_ms` (0x221b),
  `off_hand_attack_speed_+%` (0x127) and `off_hand_base_maximum/maximum_attack_distance` (0x147/0x4d4). Live: the set is
  installed on every action (493/494 alternating) and holding the button on empty ground no longer crashes. What was
  *not* the cause: the echo's skill id, sending no 0x1ab for ground uses, dropping
  `holy_strike_animates_owners_main_hand_weapon`, the animated-weapons limit (the client derives
  `holy_strike_maximum_number_of_animated_weapons` itself from `base_holy_strike_maximum_number_of_animated_weapons` and
  `double_holy_strike_animated_weapons_limit`: 0x1400c37d1 registers 0x14279ee30), and the Templar rig's missing
  mace/sceptre stances (`Art/Models/CHARACTERS/Templar/rig.amd`).
- **Damage** (`SpellHit.cs`, `SpellDamage.cs`). Projectile hits, area hits and the selected spell's panel
  share the same typed packet: the catalog base hit, flat added spell damage scaled by damage effectiveness,
  conversion/gain with complete type history, then the matching generic/spell/elemental/typed/context increase bucket
  and supported aura/support more factors. Each visited modifier applies once, including ancestors of Chaos.
  Existing source precedence remains distinct at its established seams: interim `SpellHit` uses own-key overrides
  and player-first level-only conversion; final `SupportedHit` merges player and supported skill inputs with
  `MergeSkillStats`, retaining its existing base-field replacements and quality inputs. Panel and actual supported
  combat both finish with that same final supported packet.
  The final packet still applies the actual caster's spell-damage restriction and clears inherited hand rows.
  See [20](20-damage.md) section 2f for key 5341, zero-hit callbacks, RF and native verification boundaries.
  Damage ranges are floored only for display/rolls; the packet keeps fractions through conversion.
  The displayed per-use average and DPS retain the unrounded packet average, before critical strikes and target
  mitigation. DPS uses the skill's emitted casts-per-second value, not the weapon's attacks per second.
  This is the current server convention, not an official DPS calculation: crit weighting, target defences,
  per-type integer-roll expectation, cooldown/downtime and special skill mechanics are excluded.
  Hitless spells do not borrow weapon hits or turn flat added spell damage into a hit; their hit ranges and
  inherited weapon DPS/per-use fields are explicitly zeroed. This does not implement a beam or DoT damage panel.
  After linked supports finish their damage/type restrictions, the final supported spell packet replaces both
  `spell_minimum/maximum_*_damage` and inherited main-hand display fields. Zeroes clear a type removed by a support.
  The average and DPS use this final packet and the supported cast time, retaining the pre-critical convention above.
  Eight real-pipeline tests cover Controlled Destruction, Lesser Multiple Projectiles, Added Cold, Empower,
  combined supports, Brutality, unlink/relink/removal and a hitless Righteous Fire control with an equipped weapon.
  Native selected-panel checks cover twelve stages and fourteen captures on the isolated Standard fixture.
  Eleven positive captures bind the formatter's natural getter receiver to the current local player's live skill
  and freshly parsed primary stats; one removal capture proves Fireball's ID and previous instance are absent.
  Baseline, individual/combined supports, unlink/relink/removal and relog agree with the fixed native-table inputs.
  Brutality and hitless Righteous Fire publish explicit zero hit fields and show no damage rows. Their hidden
  zero rows have no independent formatter binding; Righteous Fire has no selected formatter receiver observation.
  Link changes use a held host and re-equipping it, so native in-place link mutation remains unmeasured.
  These checks establish this local pre-critical publication, not official critical-weighted DPS or DoT behavior.
- **Damage effectiveness.** `GrantedEffectStatSetsPerLevel.DamageEffectiveness` is an offset from 100%,
  normalized by `SkillCatalog` to `10000 + raw` permyriad before attack or added-spell consumers use it.
  KB level 1 raw 4000 means 140%, Clustering raw 0 means 100%, and Cyclone raw -1840 means 81.6%.
  Flicker Strike raw 7730/30890/69600 means 177.3%/408.9%/796% at levels 1/20/40.
  This follows pinned PoB `16de4b82`, `src/Export/Scripts/skills.lua` lines 311–321.
  The previous absolute-value interpretation under-scaled positive offsets and ignored negative offsets.
  Missing data retains the identity fallback; a known zero offset resolves to an explicit 100%.
  A spell's own base damage is already final; its added damage alone uses effectiveness (Fireball level 1: 300%).
  Current attacks retain the shared effectiveness scalar for their whole packet; separate weapon-base
  `BaseMultiplier` versus added-damage coefficients are not distinguished by this existing constructor.
  Normal KB's 40% less area damage is applied by the secondary-explosion profile, not the main-hit panel.
- **Per-level stats** add to a ConstantStats value of the same stat instead of replacing it (Righteous Fire's radius:
  18 + 0 at level 1).

## 9a. Gem skills without a projectile (first version, 2026-10-23)

Only projectile skills (Fireball) and Righteous Fire dealt damage: every other gem skill logged "no projectile". Now
`AreaSkillHit` gives them one hit, at the attack's contact keyframe (attacks, as the default attack but without the
walk-in offset) or after the cast (spells), on every living monster
within the skill's `active_skill_base_area_of_effect_radius` (in cells, else 12, grown by area of effect as Righteous
Fire's: `SkillArea`; halved before 2026-10-23, which the units do not support) of the aimed
point, or of the player when a melee skill is aimed too far. Attacks use the default attack's hit (`WeaponFor`: the
weapon in the hand by its WeaponTypes row with its local physical damage, added physical damage and attack speed mods,
else the class's unarmed attack; the player's stats on top), spells `SkillCatalog.HitDamage` with the player's stats; both convert and crit.
The player object of every area (`SendPlayer`) carries the same weapon attack, so the character panel's DPS includes the
weapon's local mods from the start (until 2026-10-23 it started with the unarmed attack, and only an equipment change,
`SendSkills`, brought the weapon's numbers).
`base_deal_no_damage` skills (Portal) deal none. An attack's hit carries the skill's damage effectiveness (§9). Not
modelled: the skills' shapes (waves, cones, chains, leaps) and stun. Area attacks now resolve evasion, block,
critical confirmation, damage and impale inside the admitted target lock, before any on-hit callback.
Decision: move contact rolls inside the existing admission gate rather than rolling before `HitMonster` and
discarding their result after cancellation. This keeps cancelled/paused/dead-owner actions from consuming RNG
or target state. Explicit blocked/evaded results reject the contact; treating them as zero-damage hits would
incorrectly update hit timestamps and callbacks. The roll policy and primary evidence are in [damage §2g](20-damage.md).
Current-branch native validation remains pending.

**Movement skills** (ActiveSkillType Movement: Leap Slam, Whirling Blades …): the server puts the player at the aimed
cell when the skill starts (`SkillCatalog.IsMovement`); it kept the starting cell before, and a later resync pulled the
player back there (live 2026-10-23).

**Channelled skills** (Cyclone): after the skill's 0x1ab start the client sends **C→S 0x197** (10 bytes: u32 BE x,
u32 BE y, the player's cell) every few frames while the button is held, and **C→S 0x199** (2 bytes) when it is let go
(read from live captures 2026-10-23). The Gateway only moves the player there: 0x197 does not come at the attack rate.
The hits run on the server's own timer (`RunChannel`, started with the channel): the first sweep lands as the channel
starts, then one sweep around the player's current cell every `SupportedAttackSeconds` (the skill's own speed, its
supports and the player's attack speed, as the tooltip shows), paying the cost each time, until 0x199, any other action,
running out of mana, death or travel, or when the client's 0x197 updates stop for a second
(`LastChannelUpdateAt`, `ClientState.ChannelSkill`, `NextChannelHitAt`). One sweep resolves its damage
and multiplier once and rolls them per monster; every monster gets its hit, life update and death packet before the
sweep's deaths run their loot, experience and quest work (`AreaSkillHit`). Before, a sweep recalculated the whole skill
per monster and handled each death inline, spreading one sweep over up to 1.4 s in a crowd.

An accepted channel owns `ChannelGeneration` as well as its action token.
The initial timer starts only after payment succeeds; 0x199 invalidates the generation under the combat lock.
Queued sweeps check it again after mana publication and inside each hit commit, so releasing and restarting the same
skill cannot revive old damage.
The sweep centre is sampled from the current walk after queued sends, rather than its earlier timer position.
Pause still rejects contacts, and each timer revalidates that its gem remains available before charging the next sweep.
A freeze or a stun's hit recovery (`PlayerActionBlocked`) suspends the timer: no stage is paid or swept until it ends.
A stage already paid when one lands is refused at its hit commit.
The 0x197 updates are dropped while the player is blocked, so an interruption longer than a second ends the channel.

## 10. Projectiles (experimental)

**Current implementation:** the [framework](../game/projectile-framework.md) owns gem/bow production routing and
the sole 30 Hz Core simulator. Without skill coverage, catalog-recognized projectiles use basic straight-flight
recipes; missing metadata is refused before costs. Coverage replaces recipes and presentation, never the engine.
The old callback flight, observed-motion estimator, fixed-gap spread and named return-policy helpers are removed.
The older native evidence below records packet discoveries and the replaced implementation: its 16 ms polling,
send-gate damage commits, return overshoot/debug flags and fixed-gap controls are not current runtime behavior.
Revalidate native gameplay against the current runtime; packet-layout evidence remains applicable.

**The client predicts its own spell gem projectiles (Frida 2026-10-29, 2026-10-31).** One Fireball cast built two Projectile
components: one from the 0x2b9 handler (`0x142187c8a` → `0x141ebe190`), one from the player's action
(`0x141e938c0` → `0x1419eec90` → `0x141ebdf30`, a client-side object whose id the world assigns) with the same type,
`Metadata/Projectiles/StygianFireball`, 160 ms before the server's. With `number_of_additional_projectiles` 2 the
client built three before the server's three, so it predicts every primary projectile of the volley, from its own
position. The server therefore sends no object for a spell gem use's primary projectiles (`ProjectileOffense.ClientPredicted`,
`ProjectileSession.Publish`) and still simulates them; continuation children and area effects still go out. The
client made no explosion object of its own.
An attack gem is not predicted: on 2026-10-31 four Burning Arrow uses started the client's action (`0x415`, accepted
by `0x141e8cf10`) and created no projectile at all until the server's `0x2b9`, as the default attack's arrow; with
no server object nothing was drawn. `ClientPredicted` is therefore set for spells only (`CaptureGemProjectile`). Ordinary bow arrows and wand bolts (`ScheduleOrdinaryRanged`) keep their server object,
and rightly so: across 40 default-attack arrows on 2026-10-29 the same trace saw no client-created projectile at all
(only the 36 server `DefaultArrow` objects), and none for a monster's action either (27 Sand Spitter attacks). The
default attack is not predicted the way a gem's volley is. Trace: `scripts/re/frida/trace-projectile-create.js`
(load `common.js` and the trace in one script: `run.py` gives each file its own scope).

**Which actions predict (static, 2026-10-30).** The action class comes from ActiveSkills `ActionType` →
ActionTypes `HASH16` (Fireball 0xaa4e, factory `0x1412817b0`, vtable `0x1434462c8`; Spark 0x7859, constructor
`0x141b35390`, vtable `0x14350ca90`). Both derive from the projectile action (base vtable `0x14323ac38`) whose slot
0x618, `0x141bb13a0`, fires the volley: it reads `number_of_additional_projectiles` and creates each projectile as a
client object (`0x141ebdf30`, type from Projectiles.dat). Spark's slot 0x618 (`0x141b363a0`) only adjusts the count and
calls it, so Spark's sparks are predicted like Fireball's: the server must not send them. Spark's own overrides make
the flight a wander: slot 0x548 returns false (no fixed range), slot 0x558 (`0x141b35a10`) picks each leg, and slot
0x118 (`0x141b36170`) counts the spark's life down and removes it at zero or 150 cells from where it started.
The life is set once, at `0x141b35b7a`: `skill_effect_duration` (0x168) / 1000 seconds, ± up to 100% of
`spark_random_duration_%` (0x4397). That stat is virtual, so the client does not derive it from
`base_skill_effect_duration`: before the Gateway sent it (`GemSkills`, `SkillDurationModifiers.Seconds`) every client
spark died on its first tick and none was drawn. Not checked live yet.

The server creates and moves the projectile. The client's Projectile component (vtable `0x1435a0d10`, 0x20 bytes) does
nothing by itself: its slot 17 reads nothing and almost every other slot is a bare `ret`.

- **Object.** A 0x2b9 of the Projectiles.dat type named after the effect (`Metadata/Projectiles/Fireball`, hash of the
  path; the type register knows it from the table). `Metadata/Projectiles/Projectile` deserializes Positioned,
  Animated, BaseEvents and Projectile, so the blob is Positioned, one Animated byte and the tasks byte
  (`ProjectileObject`; the Animated component is index 1), then 0x1a9 (wake zone). The real link between a skill and
  its projectile is unknown; the Gateway matches the Id. Multiple projectiles are one object each, spread around the
  aim (`ProjectileSpread`: `number_of_additional_projectiles`, `projectile_spread_radius` or 10°,
  `Game_PROJECTILE_SPREAD`); the client also spawns its own copies of a gem use's volley (above), so those objects
  are no longer sent. Kinetic Blast's effect is ClusterBurst, which names no
  Projectiles row, so it falls back to the wand attack's `Metadata/Projectiles/WandProjectile` (3260) and plays
  `Metadata/Effects/Spells/cluster_burst/big.ao` where it lands; a landing on bare ground runs one area hit of the
  skill's radius (its secondary explosions are not modelled).
- **Pierce.** Pierce Support's `projectile_base_number_of_targets_to_pierce` (2 at level 1, 4 at level 20) and the other
  pierce stats give a hit budget of 1 + pierce: the projectile passes through that many monsters, each hit landing as
  the glide reaches its cell; the support's more Projectile Damage is part of `SupportedHit`. `always_pierce` is
  unlimited. The return leg uses the same budget; Returning Projectiles Support's `returning_projectiles_always_pierce`
  hits all targets. Contacts are observed during the flight, not selected along the launch line: a target that
  leaves the line before contact is missed, one that walks into it is hit, and the budget is spent only at real
  non-evaded contacts; blocked contacts still spend budget. The return leg is swept the same way as the projectile flies back.
- **Returning projectiles.** A projectile whose stats allow it (`projectiles_return`, `attack_projectiles_return`,
  `projectile_return_%_chance`; `skill_projectiles_cannot_return` forbids; `Game_PROJECTILE_RETURN` forces) returns
  from a target/terrain collision or from maximum distance (Freezing Pulse only from a collision), flies back to the
  caster and 15 m (150 cells) past them, and is then removed (`Game_PROJECTILE_RETURN_DISTANCE` in cells,
  `returned_projectile_speed_+%`). Only one return per projectile; Spark and the other self-moving/rain/tornado
  secondary projectiles cannot return; Fireball explodes after the return. The return leg damages every monster along
  it except the one it returned from, with Returning Projectiles Support's less Damage
  (`support_return_returning_projectiles_damage_+%_final`), and `returning_projectiles_always_pierce` lets it hit all
  of them (otherwise the first only). A simultaneous volley is shotgun-limited to one hit per target, out and back.
  The return's hits land as the returning projectile reaches each cell, not when the turn happens; ailments and
  pierce/fork/chain priority are not modelled.
- **Attack evasion (issue #75 review).** Gem projectile contacts now use `SupportedAccuracy` for the first
  evasion check and independent critical confirmation, including real Additional Accuracy support inputs,
  target modifiers/Blind/Masteries and raw cannot-evade flags. Spells bypass attack accuracy. A missed target
  does not stop the flight, spend a pierce allowance, trigger an impact/impale/on-hit callback, or enter the
  volley-wide exclusion. It is checked once per straight leg; another projectile or return leg may check it
  again using the defender's existing entropy. The same hit chance confirms critical results before return
  damage scaling. Piercing/returning critical hits now reach the critical flask callback too.
  Forward first-contact, piercing and returning sweeps admit state changes under the send gate and monster
  lock. Queued pause/transfer/dead-target checks precede random draws and mutations. Each simultaneous ordinary
  gem volley shares its forward exclusion; the old allocation inside the angle loop gave each projectile an
  independent set and could damage a target multiple times. Return exclusion remains separate. Kinetic Blast's
  existing special handling is retained, and its secondary-explosion/skill-specific rules still need review.
  [Official 2.0.0 notes](https://www.pathofexile.com/forum/view-thread/1304441) establish the general rule against
  multiple simultaneous direct projectile hits; they do not certify every modern skill exception.
  Decision: preserve gem inputs sampled at contact and ordinary bow inputs frozen at release. Uniformly freezing
  all gem sources would change their established lifecycle without snapshot evidence. Preserve the existing
  miss-continuation policy rather than consuming a collision as a zero-damage hit, which would stop the flight
  and trigger impacts. Do not mark remaining flight rules complete: pierce-budget terminal positioning,
  return-from-target exclusion, return terrain/endpoint sampling, secondary explosions, sequential volleys,
  special repeat rules and native parity require further work. Ordinary gem volleys and return hits now share
  the original per-use critical roll; see the conditional critical policy above.
  Twenty-four added automated cases cover real Burning Arrow/Fireball/support contacts, returning damage,
  simultaneous volley exclusion, missed/blocked budgets and delayed admission. The expanded focused run passes
  29 cases. This is automated validation, not a fresh native flight capture.
- **Flight.** S→C **0x1ba** to the target cell at the row's `ProjectileSpeed` (world units per second, 1040 → ~96
  cells per second at 250/23 world units per cell). The u32 at +0x28 is a **height**, not padding: the handler
  (`0x141e30c90`) passes it to `0x141e34400`, which keeps the terrain height for 0x7fffffff and otherwise sets
  z = -marker * 7.8125, reading the packet height as the signed Positioned marker, not signed world-z steps.
  With 0 the fireball sat under the ground and did not seem to move; with 0x7fffffff it flies (slowed to 10 cells per
  second with `Game_PROJECTILE_SPEED` to watch it). Positioned's vtable +0xd8, which the move calls when the flag
  byte has bit 0, is a `ret`; the interpolation is `0x141e4d180`.
- **Rotation.** Positioned rotation (+0x29c) = **atan2(dx, −dy)** in grid cells (the player's own moves traced with
  `trace-move.js`: facing (+15, −18) read 0.668).
- **Ordinary speed modifiers (2026-10-25; automated and live Fireball checks).** `ProjectileSpeed` scales the
  table's world-units/second by `1 + (player final projectile_speed_+% + skill base_projectile_speed_+%)/100`,
  then converts to cells/second with 250/23 world units per cell. `Stats.dat` marks `base_projectile_speed_+%`
  (client key 178) as a scalable non-virtual input and `projectile_speed_+%` (1407) as virtual. The player's
  `CharacterPlayerStats`/`PanelStats` total already includes its base input, so that input is not added again.
  Skill inputs are read separately from `SkillCatalog.EffectStats` at release, including level/quality, rather
  than overlaid on the player's raw values. For example, Freezing Pulse's quality row grants 2000 permille per
  quality: +40% at quality 20 adds to a player's +30%, giving 1.7 times table speed.
  The additive formula follows PoB's [ProjectileSpeed calculation](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcOffence.lua#L1152)
  and [modifier formula](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcTools.lua#L16),
  restricted here to additive increases/reductions. One resolved speed drives collision prediction, 0x1ba's
  duration and the server's flight delay. A finite positive `Game_PROJECTILE_SPEED` still overrides the result
  in cells/second. Invalid overrides fall back to the modified table speed. Finite positive overrides, including
  0.001 cells/second, are preserved when their flight duration is representable. Only a nonpositive speed or a
  flight exceeding [.NET's maximum Task.Delay duration](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.delay?view=net-10.0)
  (4,294,967,294 ms) gets a distance-dependent lower speed bound, before collision prediction. The bound includes
  endpoint rounding and is a numeric guard, not a measured official minimum speed.
  More/less modifiers, conditional and per-charge speed, support-gem application and special projectile speed
  rules remain unimplemented.
  Live validation used a copied OfflineWitch database and three Fireball casts over the same 116-cell path.
  At 0/+100/-50% raw player speed, the logs reported 95.7/191.4/47.8 cells per second; the actual 0x1ba payloads
  carried durations 1.212375/0.606187/2.424749 seconds. Before the patch, both 0 and +100% used 95.7 cells per second.
  These checks cover travel timing, not moving-target collision or pixel-level matching with the official game.
- **Maximum range and action speed (2026-10-31).** One movement instance is capped at 150 client units = 15 m
  (10 units per metre; 1 Gateway cell is 0.1 m). The Gateway baseline is now 150 cells through
  `system_settings.projectile_maximum_range_cells`; `projectile_distance_override` sets an exact shorter distance and
  `projectile_maximum_range_override` caps it. Fork, chain and split are new movement instances, so each continuation
  leg gets a fresh 15 m budget and total travel can exceed 15 m; return keeps its measured 150-cell overshoot.
  Action speed is not a projectile input: it changes attack/cast/movement pacing, not projectile speed or the cap.
  Projectile speed only changes the time to the endpoint (and how far a limited-duration projectile gets before it
  expires, up to the cap). `action_speed_-%` is composed additively with chill/freeze and then multiplies the final
  attack, cast, movement and stun-recovery paces; it does not change the attack/cast/movement stats themselves. New
  actions are limited to one per server frame (30/s); channelling repeats use their speed-only interval and are exempt
  from that cap. Trap/mine throwing, warcries, totem summoning, monster action-speed floors and the buff/debuff sources
  (Tailwind, Harbinger, Temporal Chains) remain outside this slice; the full contract is
  [#246](https://github.com/LocalExile/LocalExile/issues/246). Evidence: the client's Stats rows and PoE Wiki
  [Action speed](https://www.poewiki.net/wiki/Action_speed),
  [Projectile](https://www.poewiki.net/wiki/Projectile) and [Range](https://www.poewiki.net/wiki/Range); no live
  native flight or stun-animation capture yet.
- **Hit (reworked 2026-10-25).** `ProjectileFlight` advances from the
  connection's elapsed game clock, polled about every 16 ms. For each interval it solves the earliest intersection
  of the projectile segment and each living monster's observed movement segment in relative coordinates, with the
  existing 4-cell radius (`Game_PROJECTILE_HIT_RADIUS`; the row's CollisionRadius is 2). It keeps previous samples
  across movement orders, rather than choosing a future target at launch. A monster can walk out of a formerly
  predicted hit, walk into the path later, or intercept before a farther monster. Removed/dead monsters are omitted;
  new objects and replaced walk estimators are tested only at the current endpoint, without inventing past motion.
  The first contact stops the flight; range expiry without a contact removes it without an explosion.
  Live at 10 cells/s on The Twilight Strand: a monster ordered from (330, 220) to (330, 240) after release was
  missed, and the projectile reached the wall at (369, 220).
  Ordering the monster from (330, 240) into the ray after release produced a hit at (326, 220).
  These checks used actual monster movement orders; nearer interceptors and dead targets also have automated coverage.
- **Stopping at contact (static evidence and live verification, 2026-10-25).** The initial 0x1ba goes to the
  range/static-wall endpoint. At a discovered contact, S→C **0x1b7** relocates it to the rounded contact cell with the appropriate height,
  then the Animated swap below plays its impact. The 3.29.3b Positioned handler's switch (`0x141e30c90`) maps 0x1b7
  to `0x141e30da7`: it stores the height at +0x290 and trailing u32 at +0x2b4, then calls `0x141e31200`. That routine's
  tail (`0x141e3162f`) calls `0x141e35250`, which clears the movement durations at +0x240/+0x280 and movement flags.
  Thus the explosion does not continue along the old glide. `InstanceProtocol.Teleport` now accepts an optional
  fixed-height marker and float world z; existing calls retain their two zero fields.
  Unlike 0x1ba, 0x1b7 copies the fixed pair directly. For a Positioned marker `m`, send
  marker `m` and float world z `-m * 7.8125`, matching the creation pair and the glide endpoint.
  Terrain is not resampled at impact; consistent native interpolation still needs the fresh integration check below.
  The height getter (`0x141e35800`) compares +0x290 with 0x7fffffff first; that sentinel selects terrain sampling
  and ignores +0x2b4. Current flights use a fixed height, including the bare offset without a height grid.
  Every other marker immediately returns the float at +0x2b4; sending a non-sentinel marker with a zero float
  would snap the effect to world z zero.
  Live contact probes read zero native movement duration at both the monster and wall impacts.
  Separate raw 0x1b7 probes preserved marker/float pairs (-8, -62.5), (8, 62.5) and the terrain sentinel,
  with native movement duration zero in each case.
  These raw probes establish copy/stop semantics, independently of the marker/world-z relation in the
  creation blob. The moving-target and raw-stop live checks preceded the bow packet/height update; the merged
  bow/collision path needs a fresh live integration check. Broader surface-height accuracy remains open (#182).
  Impact damage is committed under the send gate and a short monster lock, rechecking connection/area/pause and
  target life. Packet writes and hit notifications await outside the monster lock. A transfer invalidates flights
  even after its pending ticket is consumed and removes outstanding projectile/impact objects before 0x2b2;
  a failed transfer permits new flights without reviving old ones or leaving their visuals behind.
  Cast release, flight and the explosion's 1.5-second lifetime use game time and wait through pause.
  Terminal model swaps and removals recheck pause inside the send gate and retry after resume;
  a transfer cancels the retry and takes ownership of visual cleanup.
  Live pause held the projectile at (289, 220) for 1.2 seconds, with no removal until the game resumed.
  Local validation captures: `logs/projectile-collision-live.json` and `logs/projectile-fixed-height-live.json`.
  These remain estimates: monster positions are integer `WalkEstimator` samples, movement between samples is a
  straight chord, the radius is shared across monsters, and the client has its own cell stepping/network delay.
  Static wall checks use a captured two-dimensional grid, not object/doodad colliders or three-dimensional geometry.
  Sharp turns between samples or long scheduling stalls can still differ from the client. This is not an official
  collision/tick-rate implementation; explosion area damage, pierce/chains and object-owned colliders are unchanged.
- **Walls (live 2026-10-18).** The client's walkability provider (walk-map hook `0x1410ddcfd`) returns a second grid
  of the same size at +0x20/+0x28: what stops projectiles. 0 blocks, 1..5 (a distance gradient like the walk map's) lets
  them through. On The Coast seed 1 every walkable cell is open in it, cliffs and rocks are 0 in both, and 1.44 M cells
  the walk map blocks (the sea) are open. `scripts/walkmaps` saves it as `<area>_seed<seed>.projectile.bin`
  (`ProjectileGrid`). Static routes use a supercover grid trace with exact rational boundary comparisons,
  including non-45-degree diagonal corner cells, clipped to
  an open cell before the first blocker; the moving-target sweep uses that same endpoint. Earlier live checks: the fireball
  explodes on cliffs and flies over the sea. Doodads and objects with their own colliders (not in the grid) do not stop
  it yet. Grids exist for Lioneye's Watch and The Coast seed 1.
- **Explosion (live 2026-10-18).** Projectiles.dat names the impact as an animation of the projectile's own .ao
  (Fireball: ImpactAnimationIds `fireball_explode` → `point_impact.pet`). No server message found yet plays an
  animation by name, so on a hit the server swaps the projectile's animated object instead: S→C 0x2ba to component 1
  (Animated, in the order Positioned, Animated, BaseEvents) with flags bit0 and the path
  `Metadata/Effects/Spells/fireball/explode_new2.ao`, whose default animation `animate` plays the `splode` particles
  and the End sound (reader `0x141e57490`: bit0 path, bit1 a 0x2c-table index, bit2 `0x141e5adb0` attachments; no
  bit3 there). Only Fireball gets this explosion; a bow arrow's impact is `none` (it sticks/disappears and its
  burning is the monster's debuff). The object is removed 1.5 s later. `Game_PROJECTILE_IMPACT_AO` overrides the path
  (`none` turns it off). `Metadata/Effects/Effect` (Positioned, BaseEvents, Animated, LimitedLifespan) would be the
  cleaner carrier but is not known to be in the client's type register; an unknown type throws in `0x142187480`.
- **Tilt.** `Metadata/Projectiles/Projectile.ot` gives the client's Render `tilt_to_target` and
  `maintain_tilt_after_movement` (type data +0x76, +0x77; parser `0x1415ef5e0`). Render (`0x1415f53c0`) pitches the
  model by −`0x141e35c00`(Positioned, maintain): atan(dz / distance) of the current move, or, when not moving and
  maintained, the angle at Positioned +0x2a0 (f1 0x10 in the blob; the client set it to π/2 on our fireball). Traced live:
  the tilt is 0 during the flight, and the yaw (1.497 to the east, 4.617 to the west) follows the flight on screen.
- **Height.** Heights are absolute, in steps of 7.8125 world units. The client's terrain height of a cell is
  `0x141d84b00`(terrain, int cell[2]): terrain +0x18 / +0x20 = size in tiles of 23 cells, +0x28 = 0x38-byte tile
  entries (tile data pointer, +0x36 rotation code, +0x30 i16 height offset), result = the tile's height at the
  tile-local cell (`0x141d7d100`) + terrain +0x104 × the tile's offset; `0x141e35800` interpolates four cells. With a
  height grid (`TerrainHeights`, data/walkability/<area>_seed<seed>.heights.bin, int16 per cell, −32768 = unknown) the
  Gateway uses terrain + the release point's height as the Positioned marker `m`: the releasing clip's contact
  keyframe (`ProjectilePresentation.LaunchHeight`, from `data/animation-contacts.json` "positions" and
  `data/monster-contacts.json`: the Ranger's attack1_bow at −127.5 world units = 16 steps, its attack1_wand −94 = 12,
  the Sand Spitter's spit −71.6 = 9), else `Game_PROJECTILE_HEIGHT` steps (default 5; it had been tuned as "the torso"
  on a fireball, and left the arrow trail at the hip, live 2026-10-29).
  Positioned f0 0x04 carries i32 `m` and f32 world z `-m * 7.8125`; 0x1ba carries the same marker `m`.
  Without a grid it uses the bare offset.
  The grid covers natural terrain, not doodads/platforms; the protocol carries no player z, so platforms and doodads
  need a server-side world/surface height model (issue #182).
- **Capturing heights from the client** (2026-10-18): direct dumps from Frida's thread crashed or hung the client
  (the terrain was still being built; the lookup runs with several terrain pointers). The read-only capture below
  (`capture-terrain-tiles.js` + `heights.py`, no calls into the client) works; an offline `.tdt` calculation is still
  possible from the same tile files.
- **Heights in the .tdt (decoded offline, 2026-10-18).** The binary parser is `0x1429f31a0`. After the header (§6 of
  08-area-transitions.md) come a u16 flags word, a hull-plane count byte (0 here), then a u16 subtile count (w × h)
  and one record per subtile, row by row: u16 flags, u8, [3 bytes if the high flag byte has bit 1, version ≥ 13],
  then, unless flags bit 2 is set, a **byte map** (`0x1429f59a0`): header byte b, kind = b & 7, n = (b >> 3) & 3 extra
  bytes, size = (b >> 5) | extra << (3 + 8k). Kind 0 = a constant (size is the value; 0 = all zero); kinds 1..4 = an
  LZ4 block of `size` bytes that expands to the 69 / 137 / 281 / 529-byte palette form `0x141d8fc40` reads (2 / 4 / 16
  int8 palette entries + 1 / 2 / 4-bit indices, LSB first, or 529 raw int8). Unless flags bits 0 and 1 are both set,
  a u16 length and that many bytes follow (a second per-cell map). Check: `beachencampment_v01_01.tdt` (15 × 18
  subtiles) placed at cell (69, 0) with the transposing code reproduces the Lioneye's Watch capture exactly (142 830
  cells, 0 differences, height offset 0).
- **Placed tiles of generated areas (live 2026-10-18).** `0x142041200` loops over the finished terrain grid
  (arg0 +0x28: the 0x38-byte entries `0x141d84b00` reads; entry +0 = subtile record whose heights vector is at +0/+8,
  +0x30 i16 height offset, +0x36 code; terrain +0x104 = multiplier, 1 in both areas). The read-only Frida hook
  `scripts/re/frida/capture-terrain-tiles.js` dumps them on entry (no calls into the client; the client stayed up);
  `scripts/walkmaps/heights.py` applies the client's lookup (code tables `0x142fa04b0`, `0x142fb7f98`). The town
  capture reproduces the known grid exactly; The Coast seed 1 is 1725 × 1725, −37..90.
- **Sign.** World z points down, but 0x1ba's signed height marker points up: `marker = -z / 7.8125`.
  `ProjectileObject.Height` takes world-z steps and writes the opposite marker plus float world z.
  The move endpoint takes that encoded marker, not the API argument: marker +5 means world z -39.0625.
  Static 3.29.3b evidence: `0x141e34921` passes the current marker and `0x141e3491a` the packet endpoint marker
  into the 3D stepper `0x141e4d180`; `0x141e3495b..0x141e3496b` converts the packet marker to float, multiplies
  by 7.8125 (`0x1436a36b8`) and flips its sign (`0x1436b3520`) before render interpolation `0x141e4d510`.
  Using the opposite marker made a creation at -39.0625 glide toward +39.0625 before the stop reset its height.
  A raw same-path A/B probe showed marker -5 drifting toward positive z, while overriding the endpoint to +5
  returned it toward -39.0625.
  Corrected normal launches then retained marker 5/world z -39.0625 at creation, after the glide started,
  during a paused mid-flight sample and at moving-monster and wall contact stops.
  The same run verified a moving-away miss, a later-entry hit, pause/resume and zero native glide step after contact.
- **Flight (live 2026-10-18).** S→C 0x1ba's float is the move's **duration in seconds**, not a speed: the handler
  passes distance / it as the speed, and the step walker sets Positioned +0x23c = that speed and +0x240 = the time per
  cell step (duration / the path's longest axis), advancing +0x244 by the frame time (`0x141e32710`). Sending the
  speed (95.7) made each step take ~1 s, so the fireball crawled (~10 world units/s, sampled with Frida); sending
  distance / speed it flies at ~1000 units/s. The fireball now starts `Game_PROJECTILE_AHEAD` cells (1.5) in front of
  the caster and flies on to `Game_PROJECTILE_RANGE` cells (120, clamped to the area), stopped by the first
  monster or wall contact described above. `/mob_move` and `Game_MOVE_MODE=move` still pass a speed in that field.
- **Ordinary bow attacks (#203).** Ground and object uses share `LaunchOrdinaryRanged`:
  the action is confirmed, then the game clock waits for `attack1_bow` contact without the melee walk-in offset.
  The shot leaves from that keyframe's position (`OrdinaryRangedContact`, `ContactPoint`): the Ranger's bow
  59.8 units (5.5 cells) in front of the body along the aim, never past half way to it, at 127.5 units (16 steps) up;
  a wand bolt from the ACT-selected clip's tip (attack1_wand: 82 units ahead, 94 up). Without rig data for the class
  or weapon the old 1.5 cells ahead at `Game_PROJECTILE_HEIGHT` remain. The positions come from the rig .amd events
  (plus the .ao chain's event lists; the Ranger's parent DexNoWind.ao adds only trail and timing events), extracted
  by `scripts/re/anim_contacts.py` (manifest `animation-contacts` v3).
  The original action, life and area epoch must still be valid at creation. An already-paused use is rejected before
  changing action, walk or attack pacing; an accepted echo/creation queued behind a later pause waits for resume.
  Ground confirmation runs outside serial input dispatch, so resume and a cancelling move remain processable.
  After creation, later actions and player death do not cancel flight; pause freezes it and transfer/disconnect
  invalidate it. The clicked monster is an aim: another monster can intercept, and walls or a moved-away target
  can prevent damage. The object-use path returns before the old melee contact hit, so it cannot double-hit.
  Current player position, weapon/raw damage, added damage, player-first default-skill conversion, accuracy,
  attack crit chance/multiplier, impale chance and projectile speed are sampled after the contact wait, before
  enqueueing creation. Aim remains the original aim. This is the selected release snapshot policy; official-server
  snapshot equivalence has not been established.
  At the actual send gate, one game-time sample drives the target observation and swept contact test. Target
  evasion is checked there; on a hit, the direct packet and critical state are rolled, live impales trigger separately,
  and a new instance may store the actual pre-mitigation physical roll. Life is committed with the arrow's stop
  packet. Paused or obsolete queued samples consume no accuracy randomness or target state. Impale instances
  survive a killing hit until expiry/removal. Current damage excludes melee-only increases on bows and preserves
  separate penetration/local-source inputs; see the later issue #75 corrections in [20-damage.md](20-damage.md).
  Official snapshot, armour and entropy parity remain separate work.
  An evaded object is ignored for the rest of this straight flight, and a farther contact in the same sweep may
  hit. This is a bounded policy supported by the current community
  [Projectile](https://www.poewiki.net/wiki/Projectile) and [Avoidance](https://www.poewiki.net/wiki/Avoidance) pages;
  older mirrored descriptions disagree, and no primary GGG statement or official-server trace was found.
  Official evasion-continuation parity remains unverified. Null contact predicates preserve existing gem behavior.
  Ordinary arrows send ranged life/flinch/death notifications and, since 2026-10-29, a `0x1af` with flags 0x4201 (the
  main-hand bit included, as GGG's server sends it on bow hits) for the target's blood and particles; no Fireball
  explosion. The arrow object is created at the release point and its first move runs from there: the late lazy
  snapshot used to create it where the simulation already had it, one to ten cells past the bow (live 2026-10-29,
  "arrows do not come out of my body"). After an enemy or wall contact the stopped arrow stays one second
  (`OrdinaryRangedAttack.ArrowStuckSeconds`) before removal, as the arrow left in its target.
  Before-controls on 2026-10-25 showed object arrows created at bow-clip elapsed ~0.012 s and melee
  damage before flight impact, while a ground arrow crossed the same monster with no damage. On the combined
  runtime, actual right-click ground/object after-checks each caused one hit only at the native arrow stop:
  ground aim (363,224) hit at (296,221) for 13; object aim (300,220) hit at (296,220) for 6, without melee
  `0x1af 0x4201`. Creation was in `attack1_bow`, clip elapsed ~0.466/~0.448 rather than ~0.013;
  the expected clip contact
  is ~0.399, so frame/server scheduling delay remains visible and exact native timing equivalence is not claimed.
  A wall before the selected object stopped the arrow at (318,276) without damage; previously the selected
  monster still took damage. A nearer interceptor took one 7-damage hit while the selected target was untouched.
  A target ordered off-path after release was missed and the arrow continued to the wall. A framed Move before
  release cancelled creation/damage; after release it left the old arrow alive to deal one 13-damage hit.
  During flight pause, 35 native samples stayed at (286,220); that shot later missed and continued to the wall.
  A release paused before contact created nothing while paused, then created ~1.03 s after resume and hit once
  for 11. Paused raw ground/object requests were ignored and resume remained processable.
  A shot with 100 added cold captured at release still dealt 112 after those raw stats were cleared in flight:
  one killing hit and one 4-XP award. Loot was not measured. Live accuracy was not forced to 100%; at that source
  revision `/set_stat accuracy_rating` did not refresh `conn.Pools.Accuracy` used by ordinary combat.
  The issue #75 source correction above now rebuilds that pool, with command tests; this old native run does not
  establish the corrected behavior.
  Combined-only Frostblink checks: actual right-click then actual Q after release preserved one 13-damage hit.
  Actual right-click then a timed framed `0x190` before release moved the native player to (312,221); the arrow
  was created at (313,221) with the original aim (363,224), ending at the wall. This verifies local composition,
  not standalone Frostblink support or official-server mechanics. Combined-only hit-recovery controls showed
  native TakeHit (clip 163, 0.429 s) cancelling the pending bow without creating its arrow; input during recovery
  was denied and a fresh retry after expiry released. An arrow created before recovery still stopped/removed
  under its original id despite action cancellation. These recovery/Frostblink features are separate contributions.
  Evidence: integration-checkout `logs/ordinary-bow-after-{ground,object,wall-object,interceptor-object,
  moving-away-object,prerelease-cancel,released-move-hit,flight-pause,release-pause,paused-dispatch,
  release-snapshot-kill,released-native-frostblink,pending-bow-framed-frostblink}.json`.
  Recovery evidence: `logs/ordinary-bow-hit-recovery-{bow,flight}.json` in the same checkout.
  Automated sweep, release snapshot, actual send-gate lifecycle and paused-input dispatch checks also pass.
  Surface/platform height, supports, pierce, fork and chain are outside this change.
- **Ordinary wand attacks.** Ground and object default attacks use the shared ordinary ranged pipeline
  (`OrdinaryRangedAttack`, `OrdinaryRangedShot`, `Program.RangedAttacks.cs`).
  Wands create `Metadata/Projectiles/WandProjectile` (Projectiles row 62, speed 3260 world units/second) at the
  contact fraction of the actual ACT-selected main-hand attack clip, scaled to the native attack duration.
  Ranged release never takes the melee walk-in offset, and an object attack no longer also deals direct melee damage.
  The release snapshot and actual first swept collision determine accuracy and weapon damage, preserving bow behavior.
  At a collision the bolt stops, receives Animated kind-zero clip 0 (`impact`), remains for 500 ms of game time,
  then is removed; the native clip owns WandTrailImpact sound and wand_impact particles.
  `anim_contacts.py` reads all seven `Characters.ACTFile` assets and their AO/AMD rigs into
  `data/animation-contacts.json`; extraction manifest version 2 regenerates this expanded output automatically.
  The `defaultWand` pools preserve ACT order and resolve nested `WandShield`/`WandWand` Melee overrides or inheritance.
  Templar's dual-wand main hand inherits `attack1_wand`; its `attack1_wand_wand_right` assignment is OffMelee.
  Current ordinary main-hand pools are unconditional group-zero, weight-one entries with no exclusion.
  Aliases, modifiers or conditions in these pools fail extraction instead of guessing eligibility.
  The Witch's single-wand pool is attack1/2/3/4 with contacts 300/233/200/366 ms, each in a 600 ms rig clip.
  `AnimationContacts.TryDefaultWandContact` freezes the selected stance and contact from the accepted input sequence.
  Missing old metadata uses the existing contact fallback with an explicit approximation warning.
  Native action start `0x141e8e92b..0x141e8e932` copies action `+0x11a` to Actor seed `+0x21c`.
  Request builder `0x141ba3104..0x141ba3115` zero-extends that u16 sequence and adds action `+0x2d0`, wrapping as u32.
  The latter is intrinsic `attack_repeat_count` (stat 0x4eb), set by Melee constructor `0x141b6eb70` and decremented
  at completion `0x1419e3960`; it is zero for this ordinary attack, rather than a counter of held-button attacks.
  Fresh held-input actions receive a new sequence (`0x141e8d3d0`); repeated packets need no invented seed offset.
  Seed 0xffff is the zero-extended 65535.
  Duration selector `0x141e991e0` uses the request seed, or Actor seed when the request seed is zero.
  Initializer `0x140e0bec0`, next-word function `0x140484fe0` and bounded draw `0x140484fa0` implement the native RNG.
  `AnimationVariantSelector` mirrors its literal uint32 operations, including the right-shift state transition,
  eight warm-up steps and rejection sampling; this differs from the usual published TinyMT32 transition.
  Selection walks eligible entries in ACT order, accumulating weight and replacing the choice when the bounded draw
  is less than the current entry's weight; bound one returns zero without advancing the RNG.
  Sixteen actual ground RMB inputs, sequences 4097 through 4112, matched the native selected clip in every case,
  covering Witch rig clips 31/36/39/42 and all four contact times.
  At projectile creation, native clip elapsed time exceeded the selected contact by 6.1 to 31.4 ms in 15 samples;
  the first cold sample was 55.6 ms late, so deterministic clip selection does not imply zero scheduling latency.
  An actual object-targeted RMB input, sequence 4113, selected clip 39 in both client and server.
  Evidence: `artifacts/wand-ai-validation/wand-native-summary.json`, `wand-after-ground-*.json`
  and `wand-after-object.json` in that directory.
  Full off-hand selection/timing/damage and nonzero intrinsic attack repeats remain outside this main-hand model.
  Automated release, collision, cancellation, packet-order and paused/invalidated impact-tail checks accompany native
  verification in `artifacts/combat-validation/`. A real RMB default wand attack created type `0x8648b085`, hit
  a normal Drowned for 9, played native clip 0 with a 500 ms length, and removed the object 514 ms after impact.
  These observations establish projectile and impact behavior; the later captures above verify variant selection.
- **Still wrong.** Without a surface-height model the projectile uses the captured terrain height; on a platform or
  doodad (which the terrain grid does not include) it spawns below the player (issue #182). The direction floats of
  Positioned's f1 0x04 block are not used; sending (dx, dy, 0, 0) put the fireball under the ground, so the Gateway no
  longer sends them.

## 10a. Weapon restrictions: why a skill is greyed out

The client decides itself whether a skill can be used (the skill bar's "[Skill can't be used]", ClientStrings 844
`SkillPopupSkillUnusable`): `FUN_141ee7fb0(skill, mainType, offType)` sets the skill's +0xb0 (usable) / +0xb1, and
`FUN_141ee8410` checks a hand's type against the ActiveSkills `WeaponRestriction_ItemClassesKeys` (setting +0xb2 main
hand / +0xb3 off hand fits; neither → unusable). The types live in the ActiveSkillManager at +0x14c / +0x150, set
through its vtable +8 (`FUN_142199200`) from `FUN_141e97190`, which reads them from the player's Inventories component
(`FUN_141e1f310(inventories, 0 / 1)`: the slot's record's first field). A type is the item's **ItemClasses row + 1**
(`FUN_1423b8b90`; `FUN_1424aac70` goes back): Two Hand Axe (row 16) = 17, no weapon = Unarmed 37, 0x68 = nothing.
That first field is the equipment record's first u8 (13-items.md "What the model wears"), which the Gateway sent as 1
for every item: every skill with a weapon restriction was greyed out (Ground Slam with a two-handed axe, live
2026-10-23, found with a Frida trace of `FUN_142199200`). The Gateway now sends ClassRow + 1 (`VisibleItem.Type`).

A worn weapon whose requirements are not met (`InactiveItems`) is not in the hands: `Looks` sends its main- or off-hand
slot empty, as an unequipped hand, so the model does not draw it, the client greys out every skill whose restriction
needs it (a bow for Burning Arrow) and the default attack stays on the bar and punches; the server's own weapon profile
and default attack already treated it as unarmed. `SendEquipment` recomputes qualification before building the model,
and `SendSkills` re-sends the model (`SendModelIfHandsChanged`, keyed by the hand slots last sent) when a level, a
passive or another item changes which weapons qualify. Because the client still sends a use of a greyed skill,
`GemDisabled` / `GemSkillInstanceDisabled` refuse it through `WeaponRestricted`: `SkillCatalog.WeaponRestrictions`
(the effect's ActiveSkills `WeaponRestriction_ItemClassesKeys`) against the active hands' ItemClasses rows, an empty or
inactive main hand counting as Unarmed (row 36), either hand fitting. Automated coverage only; awaiting a native check.

## 10b. Actor restriction: warcry skills disabled

`your_warcry_skills_are_disabled` is Stats row 5408, wire key 5409 in 3.29.3b.
Rampager, PassiveSkills graph node 49937, supplies value 1.
`SkillCatalog.IsWarcry` follows GrantedEffects.ActiveSkill and ActiveSkills.ActiveSkillTypes to the Warcry type.
The extracted ActiveSkillType row is 62; classification resolves its Id instead of hardcoding names or spell status.
Enduring Cry is a Warcry and is not a Spell in these tables.

`ActorSkillDisabled` reads the actual caster through `CharacterPlayerStats`, before any skill stat overlay.
Passive and active equipment sources use the existing raw stat assembly; a raw dev value replaces their total.
Any signed nonzero value disables new warcry uses.
Explicit zero or source removal restores admission on the same connection without rebuilding resource pools.
The restriction leaves the Actor skill list and ordinary attacks, spells and auras available.
The caster stat builder does not call `GemDisabled` or build `PlayerSkills`, avoiding recursive admission checks.

`GemDisabled` shares the restriction with ground 0x25 and the `CastGemSkill` backstop.
`TryBeginCooldownUse` also checks it before creating or spending cooldown state or invoking action side effects.
This covers monster/self object 0x24 and eligible instant paths before a new action echo, cost or effect.
Instant aliases retain their existing supported subset; this does not add instant warcry executors.

The existing `CastGemSkill` check remains the cast admission boundary.
A queued action echo can pass preflight and then fail that check before payment.
After cast admission, a source change behind a queued paid mana packet does not add a late cancellation or refund.
An accepted Enduring Cry still schedules its existing non-cancellable charge release in `UseChargeSkill`.
Later source or action changes do not cancel that release.
Enduring Cry remains partial: this change adds no healing engine or other warcry mechanics.

Automated coverage checks parsed 0x24/0x25 requests, payment, cooldown retry, source precedence and queued sends.
It also checks unsupported instant aliases and admitted instant Righteous Fire with the restriction present.
Seven native checks used a fresh unascended Witch, the real level-one gems and five normal nearby enemies.
Ground and self/object requests with positive or negative flags emitted no new action or cry effect and spent no Mana.
Clearing the source admitted a 13-Mana Enduring Cry, granting one actual native Endurance Charge from five power.
Reapplying the flag after payment did not cancel that accepted release; an ordinary Fireball still dealt 200 damage.
A refused request after the original eight-second cooldown spent no use; clearing the source allowed a paid retry.
The retry granted a second charge; an explicit warcry speed input kept it clear of the first charge's expiry.
Relog cleared the transient developer flag.
These controls verify the shared restriction, not Rampager allocation or complete Enduring Cry behavior.
Future composition with outgoing Spell or incoming armour changes must retain this caster-only admission check.

## 10c. Actor restriction: own aura skills disabled (#304)

Static inspection of the pinned 3.29.3b client establishes these virtuals (wire keys, not JSON row indices):

- `your_auras_are_disabled` (11075) is `base_your_auras_are_disabled` (21148) OR
  `keystone_herald_of_doom` (11881), with either signed nonzero input counting as true.
  Registration `0x1400dd340` selects callback `0x14289a420`.
  Rampager node 49937 supplies 21148; its spell, warcry, scale and Rampage effects are independent.
- `skill_is_aura_skill` (15801), callback `0x1428cf720`, is `base_skill_is_aura_skill` (3004) OR
  (`curse_apply_as_aura` (2730) AND `skill_is_curse` (2532)).
  Registration is `0x1400e4f60`.
  `skill_disabled_by_stat` (15798), callback `0x1428cf770`, tests 15801 and then caster 11075 in its aura branch.
  Registration is `0x1400e4fc0`; other branches remain independent restrictions.

Classification follows GrantedEffects.ActiveSkill to ActiveSkills.ActiveSkillTypes and ActiveSkillType.Id.
Aura type row 38 has FlagStat row 3003, giving wire key 3004.
All 17 supported ordinary self auras are typed Aura, as are unsupported Pride, Flesh and Stone, Vaal Haste and banners.
Reservation alone is insufficient: Heralds, Arctic Armour, Tempest Shield, Petrified Blood and Blood and Sand
are not Aura.
RF is not Aura even though one of its buffs is named `righteous_fire_aura`.
Neither BuffDefinitions.IsSkillBuff nor a buff's name or duration identifies an aura.
Accepted linked Blasphemy adds Aura and supplies 2730 to an AppliesCurse skill (flag 2532).
The metadata and native skill flags are published without adding a curse-aura executor.
Admission, preflight, cast and acknowledgement checks carry the actual skill ID and its accepted supports.
Two copies of Enfeeble can classify differently: the linked copy is an aura; an unlinked copy is not.
Remembered restoration checks the candidate gem's own requirements rather than another copy of the same effect.

The Gateway resolves the restriction from current caster passive/equipment sources before any skill overlay.
Developer values replace each complete raw input; an explicit final 11075 replacement also wins, including zero.
Actor 11075 and skill 3004/15801 are published coherently; an own-aura restriction sets skill 15798 to 1.
Publication preserves an independently supplied 15798 reason and never clears all skill flags.
Ground, object/self, eligible instant-object and the shared pre-cost/cooldown gate reuse the warcry admission seams.
The locked activation boundary rechecks the restriction; the unsupported instant-ground aura alias stays unsupported.
The skills and bindings stay granted while their uses are prohibited.
Ordinary ground/self/object acknowledgement gates recheck the current own-aura restriction under the state lock
before committing native use or cooldown publication; a refused acknowledgement cannot continue into a cast.
The original action/connection ownership predicates and independent accepted Warcry/RF behavior remain in force.

**LocalExile lifecycle policy:** a temporary restriction suspends active self-owned auras
and retains ActiveAuras intent.
Both source and received buff instances end once, reservation and bonuses cease, and current mana gains no refill.
Lifting the restriction restores available remembered ordinary self auras once, using fresh instances and a mana clamp.
Restoration is not a manual activation and does not recheck transient cast affordability.
Death still clears saved intent, including while runtime effects are suspended.
The inspected native admission virtuals do not prove this official-server lifecycle or automatic restoration policy.

Pool/raw-stat reads detach restricted self auras without recursing through CharacterPlayerStats or aura restoration.
At the send gate, reconciliation precedes player, Actor, mana and initial object snapshots.
Rage and Fortification full snapshots share this batch; unchanged Rage still publishes pending aura changes.
Presentation diffs against the instances actually sent on this connection, so queued adds cannot resurrect old buffs.
Loading may restore runtime intent before a player object exists; dirty/presented state remains pending until creation.
Initial 0x2b9 contains reconciled stats/pools, then entry replays each pair once on that object.
Closed/leaving connections cannot restore or publish aura pairs.
RF, flasks, Fortification, other temporary effects and foreign received buff instances are outside this owned list.

Metadata, real-dispatch, source, decrypted loopback, creation-ordering and queued/lifecycle regressions are automated.
Fifteen native controls on source `01e458d` used a fresh unascended Witch and level-one Clarity, Haste and Fireball.
Actual Buffs vectors contained both owned instances for each active aura.
At 700 maximum Mana, Clarity reserved 34 and Clarity plus Haste reserved 384; their regeneration/movement bonuses rose.
The positive restriction removed both pairs, bonuses and reservation, retaining 316 current Mana without a refill.
Ground, self/object and instant-object retries started no action; ordinary Fireball still dealt 200 actual damage.
The installed Fortification buff and count remained present.
Clearing the source restored fresh pairs once and clamped current Mana; repeated snapshots kept the same instances.
Signed negative inputs, the Herald input and explicit final zero/negative replacements passed independent controls.
Relog cleared the transient developer source and replayed the saved Haste pair in the combat area.
Its Mana reservation was sampled after a zero Strength-mana source rebuild, after observing the entry pair.
Death while suspended cleared intent, and resurrection did not restore a phantom aura.
Initial attempts exposed observer gaps for null-GEPL basic actions and creation-only Mana, not gameplay failures.
Evidence is in local `logs/phrecia-completion/own-aura304-20261027-111846` with exact artifact versions and hashes.
Native grey-icon prediction, persistent restricting equipment and another client's party effects remain unverified.
11881 is consumed only by this restriction; complete Lone Messenger, Rampager, Phrecia admission, party/minion auras,
general reservations, banners, enemy auras and Vaal systems remain separate work.
A foreign-source packet non-removal control establishes no party aura executor or gameplay behavior.

## 11. The skill bar

- **C→S 0x4e** (5 bytes, class `0x1435bfc58`): `u8 slot, u16 skill id` (big-endian). Slot 2 is the right mouse button.
  The skill bar UI (`0x140aecab0`) sends it when the player puts a skill in a slot. The client also does it by itself
  when a skill is **added** to an existing player (0x2ba): it took the first free slot after socketing Fireball. Skills in
  the creation blob, or added before the HUD exists, stay unbound.
- **S→C 0x53** (instance group, class `0x1435dc4c0`, read `0x14219f600`): `13 × u16` skill ids by slot (raw bytes, so
  little-endian; basic actions by their own id, Move = 0x2909), then u16 +0x2a and u16 +0x2c (sent 0). The bar (vtable `0x14319d870`) rebuilds from such an array in
  vtable +0x390 `0x140ae0fb0`: it looks each id up in the ActiveSkillManager and fills the slot; an unknown id (0)
  clears it. Without 0x53 the bar is empty after every area change and login.
- **The 0x20 skill update can take a bound skill off the bar**: live 2026-10-21, Haste's slot emptied when its aura went
  on (the aura's stats resend every skill record), and the area change's 0x53 brought it back. A gem taken out and
  socketed again did not get its slot back either. `SendSkills` therefore sends the saved bar (0x53) after every
  update, unless it adds a skill whose GrantedEffects Id the saved bar does not have: the client binds such a new
  skill to a free slot itself (0x4e), and a bar sent then would clear it.
- **The Gateway** saves each 0x4e (`CharacterSaves`, `data/saves.db`) as slot → a GrantedEffects Id for gems and the default
  attack (skill ids change with the socketed gems), `Action/<ActionTypes Id>` for a basic action (Move 0x2909 =
  "Move only"), or "" for a cleared slot. It sends 0x53 right after the player and area messages, no delay needed,
  with the default attack on slot 0 only when nothing was saved there. Live 2026-10-18: Fireball on the right mouse
  button and "Move only" on the left one came back after login and area changes, with the bar there at once.
- **New skills go into empty slots only** (`SkillBarLayout`, `FillSkillBar`). A new character's bar gets the default
  attack on the left mouse button (slot 0) and its gem skill on the right one (slot 2). `SendSkills` runs it before it sends the saved bar
  again (#108), so a gem skill that is nowhere on the bar takes the first empty slot in this order: the right mouse button, Q W E R T (3–7), the
  middle button, then the rest. An empty slot is one never set, cleared, or holding a skill whose gem left. Slots the
  player set stay as they are, and a cleared left button stays cleared. Live 2026-10-20: a new Templar got the sword on
  the left button and Holy Strike on the right.

## 12. Buffs and Righteous Fire

- **S→C 0x1c1** (world group, class `0x1435ddb78`, read `0x141fdf870`, world handler case 0x1c1 → Buffs component
  `0x141e11720`): object header (u32 id, u32, u16), u16 buff instance (+0x20), u16 BuffDefinitions row, u32 +0x64 (the
  source object), u16 +0x60 (the source skill; for a flask buff its flask-bar cell, under which the client draws the
  effect's timer bar, [13](13-items.md) §8a), u8 flags (bit0: u8 u32 u8 u16; bit1: +0x68, an f32 duration in seconds,
  default +infinity; bit2: u16 stacks,
  default 1; bit3: u16 u16; bit4: u16 BE BuffVisuals row override, default the row's, read after bit3's fields (a row past the table gives none); bit5: u8 +0x80, default 2; bit6 → +0x81;
  bit7: f32 +0x6c, default 1.0), a u16 when the row's bool at +0xff is set (`__col44` in the vendored schema), one u32
  per GrantedStats entry when the row's `IsRecovery` (+0x7c) is set, then a u32-counted list of u32. A row with either
  flag needs those fields, or the client misreads the message and the ones after it (the flask buffs, rows 5 and 9:
  [13](13-items.md) §8). The client reads the BuffDefinitions row in memory as the
  .dat row (BuffVisualsKey at +0x55). **S→C 0x1c2** (class `0x1435dd918`): object header, u16 buff instance: removes it.
  The Buffs component has no 0x2ba update reader; its creation blob holds the same entries ([04](04-player-entity.md)).
- **Righteous Fire** (`ActivateRighteousFire`, `RunPlayerTick`, `BurnAround`): cast on the player itself (§9), it puts
  two buffs on the player: `righteous_fire` (row 12, BuffVisuals 15: `ri_fire/bottom.epk`, the ring at the feet) and
  `righteous_fire_aura` (row 13, BuffVisuals 19: the ignite flames, and for a player model EPKFiles2
  `ri_fire/player.epk`, the burning hands). Every 250 ms the player loses 90% of maximum life per second
  (`base_nonlethal_fire_damage_%_of_maximum_life_taken_per_minute` 5400; nonlethal, it stops at 1) and every hostile
  monster within its radius plus its size loses 70% of the player's maximum life per second
  (`base_righteous_fire_%_of_max_life_to_deal_to_nearby_per_minute` 4200) and carries `righteous_fire_aura` while
  inside. It ends at death; casting it again does nothing. **The radius** is `active_skill_base_area_of_effect_radius`
  (18 at level 1; one unit is one cell) plus `active_skill_base_radius_+`, scaled as Path of Building's calcRadius:
  floor(base × floor(100 × √(area modifier)) / 100), where the modifier is (1 + `base_skill_area_of_effect_+%` +
  `skill_area_of_effect_+%` + `spell_area_of_effect_+%`) × each `…area_of_effect_+%_final`, then
  × (1 + `active_skill_area_of_effect_radius_+%_final`), from the player's stats under the skill's (`SkillArea`).
  Conditional area stats (per charge, per attribute, "recently") are not modelled.
- **A worn Righteous Fire effect** (its `skill_art_variation`, [23](23-microtransactions.md) §3) swaps both buffs'
  visuals through 0x1c1's BuffVisuals override and the ring's AO (`SkillArt`), also on the enemies' aura; NoRing
  draws no ring. A pick while the fire burns lights it again with the new art (casting again while it burns does
  nothing, so before 2026-10-23 a pick showed only after the fire ended).
- **The big burning circle** is not a buff visual: it is MiscAnimated `RighteousFireRing`
  (`Metadata/Effects/Spells/ri_fire/rifire.ao`), an effect object of its own. The client's effect script
  (`0x142087910`) makes MiscAnimated effects as `Metadata/Effects/Effect`, `ServerEffect`, `PermanentServerEffect` …
  objects; `PermanentServerEffect` is Positioned, BaseEvents, Animated (Render client-only), so the Gateway creates one
  (`EffectObject`: the projectile's Positioned, Animated bit0 + the .ao path) under the player and glides it with 0x1ba
  every 100 ms to where the player will be 100 ms later along its path (`FollowRighteousFire`, `RunRingFollow`;
  before 2026-10-23 it glided once per ground click to that walk's end, so a walk to a stash or an item, which comes as
  C→S 0x24, left it behind, and the straight glide cut the path's corners). It is created with Positioned flags0 0x80
  (`EffectObject.FixedRotation`): without it each glide turned the ring to face the move, so it spun as the player
  turned (live 2026-10-21). It is also scaled to the radius: Positioned flags0 0x20's uniform scale (+0x2b0, the
  x / y stretches left at 1, [04](04-player-entity.md) §4.0). The art is drawn for the radius in its MiscAnimated row's
  `__col4`, in world units (RighteousFireRing 400, RighteousFireRingWhite 200, the league avatars' blue ring 380, the
  Vaal rings 500; 0 for art not scaled to an area); the meshes agree (`rifire.sm` ±463 with its flames at 380,
  `rifire_white.sm` half that). Scale = radius × 250/23 / `__col4`: 0.49 for the default ring at 18 cells (1.8 m), 0.98
  for the white one. Verified against a screenshot of the real game at level 1 (2026-10-23): with the health bar as the
  ruler, the outer ring is about twice the inner flame ring (the `righteous_fire` buff's `bot_02.ao`). Before, all
  three scale floats carried the scale, so the ring was drawn at its square (0.24) and hid under the inner ring.
- **Units.** A radius stat is in cells (one cell = 250/23 world units), and the client's tooltip shows it as metres at
  10 per metre (Righteous Fire's 18: "1.8 metres"). A world unit is about a centimetre (the Marauder's mesh,
  `Warrior.sm`, is 190 tall), so 18 cells ≈ 196 world units ≈ 1.8–2 m. Live 2026-10-18: with only the
  buffs the player had just the small ring at its feet; with the effect object the big circle shows and follows the
  player, and monsters around burn to death.
- **`/god`** toggles 1000% increased attack and cast speed, 200% increased area of effect
  (`base_skill_area_of_effect_+%`), life and mana regeneration (below), +10 000
  maximum life and +1000 to each attribute (`GodStats`, `PoolsFor`); `/set_aoe <percent> | off` sets the character's
  increased area of effect in place of it (a burning Righteous Fire is lit again with the new radius); `RunPlayerTick` applies the regeneration. Life
  regenerates 100% of maximum life per second (`life_regeneration_rate_per_minute_%` 6000), not a flat 10 000: Righteous
  Fire burns 90% per second, which outran a flat 10 000 once passives raised life above 11 111.
- **Resistances** (`CharacterStats`, `DamageModel`): the passives' `base_resist_all_elements_%` and
  `base_fire/cold/lightning/chaos_damage_resistance_%` are summed from the character's 0% base into `Pools`; the client
  applies the two permanent Kitava quest penalties itself, and the Gateway mirrors them from the same flags
  (`a5q6-CompletedBoss`, `a10q3-SeenBoss`/`a10q3-UsedRewardBook`: -30% each, not an aura). The final value is
  `clamp(resistance, -200, min(75 + max-resistance modifiers, 90))`, taken from Path of Building commit `16de4b8`:
  `src/Modules/Data.lua` (`ResistFloor = -200`, `MaxResistCap = 90`), `src/Data/Misc.lua`
  (`base_maximum_all_resistances_% = 75`) and `src/Modules/CalcDefence.lua` (the clamp). The project is cloned locally
  into the git-ignored `Tools/PathOfBuilding` as the source of these formulas. Righteous Fire's self-burn is mitigated
  by fire resistance; `/resistances` prints the modifier, penalty and final values and `/hit <type> <damage>` applies
  the same rule. Armour reduction and player evasion as avoidance against monster attacks now exist (see §4);
  penetration and max-resistance modifiers are still not modelled, and monster evasion is used for the player's
  accuracy check (§5).
- **Overcapped-resistance modifiers** (`ResistanceOvercap`, `DamageModel.Overcap`): while one of the flag stats is
  set, the uncapped resistance above its maximum is a generic increased modifier — PoB `16de4b8`,
  CalcDefence.lua's `ResistOverCap = max(0, total - max)` with the maximum capped at 90, applied as
  `Evasion` (INC) for `EvasionRatingIncreasedByOvercappedColdRes` and, in CalcOffence.lua lines 745-748, as `CritChance`
  (INC) for `CritChanceIncreasedByOvercappedLightningRes`: +1% increased evasion per 1% of cold overcap and +1%
  increased critical chance per 1% of lightning overcap, additive with the other increases, for attacks and spells
  alike. The flags are ordinary stats (keys 5320/5321), so any source sets them: the Surfcaster's Sea Legs notable
  (PassiveSkills graph 61696, with its +40% `cold_and_lightning_damage_resistance_%`, which the passive/item aggregator
  adds to both elements' raw resistance), an item mod, or `/set_stat`. An explicit dev value replaces its own input —
  the per-type, pair and all-element resistance stats, and per-type or global maximum resistance — rather than
  adding to the aggregate, and `/set_stat` rebuilds the pools at once.
  The shared inputs are `base_resist_all_elements_%`, `additional_maximum_all_resistances_%` and
  `additional_maximum_all_elemental_resistances_%`.
  `PoolsFor` derives the increases once per pool build (with the
  Kitava penalty inside the raw resistance; a penalty change rebuilds the pools again) and stores them in `Pools`, so
  the evade rolls, the panel and the spell consumers read the same numbers; the uncapped values stay in `Pools`
  untouched.
  The resolved stack adds the derived critical increase once to the signed pool sum, before final half-up rounding.
  An armed 830 base with 45% overcap gives 1204; raw spell stats expose the same derived source once.
  Selected skills use the complete pool sum so a clipped native raw stat cannot erase negative cancellation.
  Per-key resistance replacements include the active Purity contribution already in the pool bonus.
  Equipment 80 plus Purity 40 stays 120 when the typed raw key is replaced with 120, rather than becoming 160.
  An explicit zero replaces the typed resistance or maximum completely, while other raw keys remain independent.
  Real aura/catalog/pool tests cover cold, lightning and all-element replacements; this fix needs native rechecking.
  New automated regressions cover those combinations with explicit zero flags and cannot-crit.
  These thirteen native checks belong to original head `39cbda2`; recheck the resolved combinations natively.
  A fresh Witch verified 120% cold/lightning resistance with each flag:
  evasion 15 → 22 (45% additional increased evasion), spell crit 500 → 725 (45% increased chance).
  Raising maxima to 90 reduced these to 19 and 650; a global +5 maximum gave 21 and 700.
  Adding then clearing 40% all-element resistance gave 28/925 then 22/725.
  Act 5 and Act 10 penalties reduced these to 17/575 then 15/500; clearing the penalties restored 22/725.
  Zero flags and relog removed the development bonuses.
  These validate the generic consumers; allocating Sea Legs belongs to the separate ascendancy lifecycle change.
- **Spell Suppression** (`CharacterStats`, `DamageModel`): the passives' `base_spell_suppression_chance_%` is summed into
  `Pools` (raw base retained, displayed chance bounded to 0..100%, PoB `SuppressionChanceCap`).
  A successful roll makes a spell hit take 40% less damage
  (`SuppressionEffect` in PoB `src/Modules/Data.lua`). The passives' unconditional
  `base_spell_damage_%_suppressed` is added to the 40% effect (e.g. Inveterate +3, Primal Instinct -10); the effect is
  independent of conditional chance. Full Life, moving, recent enemy hit and full ES chance sources add to the raw
  base before one final 0..100 clamp. Full ES uses a living character's positive effective maximum and whole current
  ES at that maximum; zero ES and fractional recovery below maximum do not activate it. The native consumer gates
  stat 15406 with public flag 2431 (`0x1428c6862..0x1428c68bf`); this server supplies the condition from its ES model.
  Panel publication, `/suppression` and `/spellhit` share that result and the existing 250 ms condition refresh.
  Same-area resurrection sends its own restored stat snapshot: a pre-death tick cache must not retain the dead
  snapshot after ES refills. The native death/aura-clear/checkpoint sequence restored all three suppression keys.
  The effect is not hard-capped, matching PoB. Conditional effect stats (`...while_on_full_energy_shield`,
  `...if_not_suppressed_recently`, per-hit/per-bark) are not modelled yet. Chance above 100 is overcap
  (`SpellSuppressionChanceOverCap`), but the effective chance is still 100. The passive
  `spell_suppression_chance_is_lucky` rolls twice and keeps the better roll (PoB `suppressRolls`, effective
  `1 - (1 - p)^2`); an unlucky roll (`p^2`) is modelled for the dev probe but has no passive stat yet.
  `/suppression` shows the chance, effective chance, effect and roll mode; `/suppression_luck <lucky|unlucky|off>` forces
  the roll for testing, and `/spellhit <type>` rolls it. There are no monster spell hits yet, so this is a dev probe and a
  preparation for monster spells; `spell_damage_%_suppressed` effect modifiers and suppression-adjacent effects are not
  modelled.
- **Block / Spell Block** (`CharacterStats`, `DamageModel`): passives' `additional_block_%` (attack) and
  `base_spell_block_%` (spells) are summed, with `maximum_block_%` / `base_maximum_spell_block_%` raising the default
  75% maximum up to PoB's `BlockChanceCap = 90` (`src/Modules/Data.lua`; `Misc.lua` has the two 75 bases). The totals
  follow `CalcDefence.lua`: `round((base + flat) × (1 + block_chance_+%) × more)` for attack and
  `round(flat spell block × (1 + block_chance_+%) × more)` for spells, then floored at 0 and capped at the
  per-type maximum. An equipped
  shield adds `ShieldTypes.Block` (22 for the Twig Spirit Shield) to attack block, and the shield-specific passives
  (`shield_block_%`, `dual_wield_or_shield_block_%`, `block_chance_%_while_holding_shield` and their spell equivalents)
  apply only while it is equipped. Staff passives (`staff_block_%`, `additional_staff_block_%`,
  `spell_block_with_staff_%`, `spell_block_chance_%_while_holding_staff_or_shield`) apply with a staff; dual wielding
  adds the inherent 20% attack block and the dual-wield block/spell-block passives; bows use their own
  `with_bow_additional_block_%` / `spell_block_with_bow_%`. A successful block prevents the hit (`BlockEffect` defaults to full prevention).
  Monster melee uses attack block; `/blockhit` tests attack/spell block and `/block` prints both chances. The character
  panel's attack row is `attack_block_%` with `uncapped_attack_block_%` in parentheses; a separate spell block row is
  drawn only when `display_spell_block_chance` is non-zero, so `ClassAttributes` sends both display stats. The Block
  Mastery effects (`PassiveSkillMasteryEffects.dat`) add +2 maximum attack block, +2 maximum spell block, 20 life and
  20 mana on a block, or +1% spell block per 5% attack block; the last uses the final capped attack block and is applied
  after the shield base. **Versatile Combatant** (`keystone_versatile_combatant`) reduces both maxima by 10% and adds
  2% spell block for each 1% of attack block above the reduced maximum (uncapped attack block minus maximum). Glancing
  Blows/`BlockEffect` modifiers, projectile block and other on-block triggers are not modelled. Lucky block
  (`base_block_luck`, `attack_block_luck`, `spell_block_luck`) uses `1 - (1 - p)²` (PoB CalcDefence); Svalinn's unique
  item modifiers themselves are not supported yet.

### Cyclone area updates (2026-10-25)

`/set_aoe` sends the increased-area input, but Cyclone's visual also needs the final
`active_skill_area_of_effect_radius` in its granted skill record. The Gateway now computes and sends
that radius together with its base for Cyclone/CycloneAltX. The hit-radius calculation uses the same
base selection and `SkillArea.Radius`, so visual scaling and hit selection agree on the next cast.
Turning the override off resends the character's own modifiers and the newly calculated radius.

The exported Cyclone stat set has no explicit base radius. Use 16 cells for these two variants,
matching [Path of Building's skill definitions](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/dev/src/Export/Skills/act_dex.txt);
an explicit positive base stat takes precedence. Other skills keep their existing data/fallback base.
Client evidence: `0x1414b13c0` reads final radius stat 0x4876 to scale its cyclone effect; the shared
cyclone path `0x141d434c0` also reads 0x4876 (or the base 0x4875 in its conditional-radius path).
Increased area is not increased radius: +300% area changes radius 16 → 32, +0% restores 16 without
other modifiers. Tests cover this calculation, spell-area exclusion, explicit bases and the fallback.
Additional melee-range/stage modifiers specific to Cyclone remain outside this initial area model.
Live visual verification after restarting Gateway is pending.

**Follow-up after live failure:** sending only 0x4875/0x4876 did not change the rendered channel.
The live hit radius did change (24 → 42 cells after `/set_aoe 300` on the test character), so that
was insufficient evidence for visual scaling. The conditional-radius helper `0x141939880` reads
`total_skill_area_of_effect_radius_+%_including_final` (0x1591) on its normal branch and the
aggregated `skill_area_of_effect_+%` (0xd7), rather than the base input 0x1b5, on its conditional
branch. Cyclone's skill record now carries both derived values as well. +300% increased area is
+100% radius before other modifiers. Compute all outputs from the original inputs before writing
the aggregated 0xd7, to avoid counting 0x1b5 twice. The server's hit calculation still uses the
original input stats. `Cyclone area:` log lines record the transmitted base, radius, area increase
and radius scale. Live verification of this follow-up is pending.
Checked against the disassembly (2026-10-25): the conditional branch of `0x141939880` reads only 0xd7, never
0x1b5, so the record's aggregated 0xd7 is not counted twice; its more multipliers are 0x1670, 0x16ee and 0x28d7.
Its caller `0x141939aa0` adds `active_skill_base_radius_+` (0x16c4) to the base itself, so the record carries the
base without it, as the server's `SkillArea.Radius` adds it to its own base.

## 13. Auras and reservation

**Persistent toggles (2026-10-25).** CharacterSaves.ActiveAuras stores stable GrantedEffects Ids,
updated transactionally on every successful manual toggle; a missing gem suspends runtime effects and retains intent.
On login/area connection, LoadProgress restores progress and evaluates usable gems first, then resolves
saved aura ids to fresh skill and source/recipient buff ids when town and caster restrictions allow them.
It recalculates pools with the restored bonuses and initializes the fresh connection's mana to the unreserved amount.
SendPlayer restores both buff records, so ground art, top-left icons and skill-bar indicators return.
An absent/unusable gem skips that aura without discarding saved intent; it can return after requirements are met.
Remembered restoration clamps reservation/current mana rather than rejecting saved intent for transient affordability.
Old saves without ActiveAuras start empty.

**Expanded activation (2026-10-25).** Determination (buff 142), Grace (143), Discipline (148),
Anger (149), Hatred (150), Wrath (151), Zealotry (1272), Malevolence (1274) and Envy (828)
now use the same toggle/reservation/source-and-recipient lifecycle as the original eight auras.
RequiredMana checks the uncapped total before adding an aura; ReservedMana remains capped for display.
Previously clamping before validation made the insufficient-reservation check unreachable.
Determination and Grace add their level/quality-based flat and more defences, including Iron Reflexes.
Anger/Envy supply added attack/spell damage, Hatred feeds gain-as-extra conversion, Wrath adds attack
lightning damage and more spell lightning damage, Zealotry supplies spell crit and more spell damage.
Spell added damage uses the gem's damage effectiveness. Malevolence's more DoT affects the implemented
Righteous Fire burn; its duration stat is published.
Discipline now contributes flat ES to the authoritative pool and its existing recharge-rate increase to recovery.
General DoT skills and Zealotry's conditional consecrated ground remain separate work.

**Source and recipient pair (2026-10-25).** Sending only the source fixed ground art but the live
client stopped showing top-left aura icons. Each self aura now owns two buff instances of its
BuffDefinitions row: a source with bit0 art and explicit display state 0 (bit5), and an ordinary
received buff with default display state 2 and bit3 skill association. The source is sent first;
both ids are removed on toggle-off or gem removal and restored on area entry. The server retains
one Aura record, so reservation and bonuses are counted once. Top-left recovery awaits live confirmation.

**Caster visuals (2026-10-25, awaiting live verification).** BuffVisuals' EPKFiles1 is the common
`affected_player.epk`; EPKFiles2 holds each aura's own art (Clarity, Vitality, Precision, Haste and
the four Purities). `0x1416082d0` selects `m_AuraEffectPack` (row +0x20/+0x28) when Buff +0x30
is nonzero, otherwise `m_EffectPack` (+0x10/+0x18). AddBuff bit0's first byte (+0x76) becomes
that field via `0x141e07220` and `0x141e06290`. `auraSource: true` sends the complete bit0 group
`u8 1, u32 0, u8 0, u16 0` before other optional fields. All supported self auras use this on
activation and restoration; recipients and other buffs retain their existing default. These aura
rows have no MiscAnimated3 ring, so the nonzero marker only selects their EPK art; a radius-scaled
ring will require its actual radius rather than this marker. The skill-bar association is independent.

- **Reservation is in the data.** `GrantedEffectsPerLevel` has `ManaReservationFlat` and
  `ManaReservationPercent` (permyriad: `5000` = 50%); `SkillCatalog.Reservation` reads them. Standard
  auras reserve 50%, Purity of Fire/Ice/Lightning and Discipline 35%, Heralds 25%; Clarity, Vitality and
  Precision reserve flat amounts at level 1 (34, 28 and 22). No separate table is needed.
- **The server computes the reserved amount.** The client does not derive it: `PlayerObject` carries it in
  the Life component's mana pool (`current, reserved`), and every S→C 0x1b1 mana update must send it too,
  or the client clears it. The amount is PoB's `CalcPerform`: flat and percent reservations are divided by
  `1 + mana reservation efficiency / 100`, the percent is rounded to two decimals per skill, and the total
  is `ceil(maximum mana × total percent / 100) + flat`. The maximum mana is unchanged; current mana is
  capped at `maximum - reserved`.
- **Activation.** The client sends an aura as a normal self/ground gem cast (C→S 0x24/0x25); the Gateway
  toggles it instead of paying a mana cost. The buff row comes from `AuraCatalog` (Purity of Elements 144,
  Purity of Fire/Ice/Lightning 145/146/147, Clarity 155, Vitality 153, Precision 1293) and is sent with
  S→C 0x1c1 with the player as source and the skill id as the source skill. A second cast removes it with
  0x1c2. The buff visual is the row's `BuffVisuals` (the `player_auras/...` EPKs); the server also sends
   the final stats, because the client derives none of them. The skill-bar indicator uses the buff's separate
   skill association (0x1c1 flags bit3): **u16 BE ActionTypes HASH16, then u16 BE server skill id**.
   The Gateway sends it for activation and when restoring auras on area entry. Removing the buff removes
   the association. The full action hash comes from GrantedEffects.ActiveSkill → ActiveSkills.ActionType
   → ActionTypes.HASH16; it must not be masked to the basic-action id.
   Client `0x140ad1400` draws `AuraFrame` and the rotating `AuraCircle` when `0x141e11180` finds an
   IsSkillBuff whose +0x48/+0x4a match the skill's +0x40/+0x42. Reader `0x141fdf870` reads the action
   hash into message +0x74 and skill id into +0x72; `0x141e11720` copies them to buff +0x4a/+0x48.
   The mandatory source skill (+0x60) alone does not satisfy this check. Automated wire/lookup tests
   cover the change; live icon verification is pending.
   **Correction to the earlier notes:** 0x25f in `0x140ae1130`/`0x140ae4550` refers to Elemental
   Overload (BuffDefinitions row 606, returned one-based by `0x142454bd0`), not an aura UI event or a
   network opcode. Sending a standalone 0x25f packet is invalid and is not needed for aura indicators.
- **Implemented effects.** Purity of Elements/Fire/Ice/Lightning add their resistances; Clarity and Vitality
  add their flat regeneration; Precision adds flat accuracy and increased critical strike chance; Haste adds
  attack, cast and movement speed. Generic
  `non_curse_aura_effect_+%` / `aura_effect_+%` and the Reservation mastery's self-aura effect scale the
  values; reservation efficiency comes from `base_mana_reservation_efficiency_+%`,
  `base_reservation_efficiency_+%` and the mana/global efficiency stats. `AuraCatalog.Bonus` and `RawStats`
  apply these to `Pools` and `CharacterStats`.
  Determination and Grace use the integer aura-scaling rule for both flat and more inputs.
  At level 20 with 50% aura effect, Determination supplies 1539 flat Armour and 73% more Armour;
  Grace supplies 2317 flat Evasion and 43% more Evasion.
  Their Ruthless variants have no flat defence and supply 88% more at the same level/effect.
  Quality changes area rather than these values.
  Aura and Basalt/Stibnite more factors multiply without rounding the combined percentage first.
  For example, 73% more and 20% more retain factor 2.076 until the final defence total is rounded.
  The integer input rule follows the pinned PoB reference below; other aura families retain their existing policy.
  Discipline grants 60 flat ES at level 1 or 217 at level 20, plus 30% increased ES recharge rate.
  Ordinary quality changes area of effect only.
  Its flat ES enters after equipment ES-to-Life conversion and receives Intelligence and global ES increases once.
  Shield-only increases and exclusions do not affect this global aura source.
  Aura effect truncates Discipline's integer flat ES and recharge increase before their character-wide calculations.
  This follows pinned PoB `16de4b82` `CalcPerform.lua:2462-2474` and `ModStore.lua:56-92`.
  For example, 217 ES with 50% increased aura effect supplies 325 flat ES; 30 recharge with 5% effect supplies 31.
  Exact official aura-effect arithmetic is unmeasured; this is the accepted 3.29.x reference rule.
  Pool assignment updates ES capacity without refilling it, clamps current ES when the aura ends, and feeds the
  same published maximum, damage absorption and recharge calculation.
  Raw aura-stat publication saturates instead of overflowing when combined with a large independent source.
  Native checks on an INT-200 Witch matched 84 ES at level 1 and 304 at level 20, with 257/734 mana reserved.
  Level 20 with 50% aura effect supplied 325 flat ES and 455 maximum ES; adding 100% global ES gave 780.
  A 40 fire hit changed current ES from 84 to 44 without Life loss, followed by delayed recovery at 2184 per minute.
  Native toggle, quality, gem-host, conversion, shield, town and death controls passed.
  Both fixture characters retained identical inventories and remembered intent across a Gateway restart.
- **Not implemented yet.** Casting one of these logs that its reservation is known but its effect is not implemented and does not
  reserve mana:
  - **Enemy auras:** Pride still needs a radius- and time-dependent enemy physical-damage-taken debuff.
  - **Heralds:** Ash, Ice and Thunder need on-kill/overkill/shatter/storm effects; Agony and Purity need minions.
  - **Banners:** Defiance, Dread and War need the planted banner, stages and their pulses.
  - **Curse auras:** Blasphemy-style skills need the curse system.
  - **Partial effects:** Purity of Elements' elemental-ailment immunity is not applied. Purity of Fire/Ice/Lightning
    max-resistance values are read at the gem's level (0 at level 1).
  - **Masteries:** Mana6, Reservation3 (life efficiency), Reservation4 (area) and Reservation5 (self aura effect) are mapped;
    Reservation1 (damage per aura), Reservation2 (max resistances with reserved life and mana) and Reservation6 (aura
    duration) are not. Skill-specific reservation efficiency/effect stats are not modelled.
  - **Systems:** supports, party/minion auras and life reservation.
- **Dev check.** `/auras` prints the active auras, flat/percent reservation, reserved and unreserved mana,
  reservation efficiency and aura effect. Area changes recreate the player object, so `SendPlayer` puts the
  running auras' buffs back on it.

## 14. Character panel sums

The [issue #75 ledger](../dev/issue75-stat-ledger.md) records all 357 virtual dependencies individually, with
partial/pending review states and links to evidence or remaining work. The 387 rows also use 134 nonvirtual
dependencies. `scripts/dev/audit-character-panel.py` checks the ledger's exact membership against the local
tables. Decision: keep authored review state separate from source matches and historical native certificates;
automatically treating those as completion would hide unresolved sources, consumers and lifecycle behavior.
The ledger's counts are audit states, not numbers of completed implementations.

Whaletooth Bangle's owner duration is projected by `SkillDurationModifiers` in `CharacterPlayerStats`:
`skill_effect_duration_+%` gains the signed per-ten-Strength value (key 2817) for each complete group of ten final Strength.
It adds to ordinary raw duration once per rebuild, after attribute resolution; explicit final-stat overrides win.
The resulting stat reaches the normal player/GrantedSkill stat publication without changing message layouts.
Enduring Cry consumes that duration in its paid recovery interval, including compatible linked duration supports.
Its base 2500 ms becomes 3175 ms at 272 final Strength with Whaletooth, retaining the Power-based recovery rate.
Native paired recovery totals, expiry Life correction, transfer and cleanup are in
[Antiquarian](../game/antiquarian.md#strength-duration-and-stun).
Other skill/summon lifetime engines remain separate.
See [the ascendancy scope and lifecycle checks](../game/ascendancies.md#whaletooth-bangle-duration-stat).

The character panel (CharacterPanelStats.dat, 387 rows) reads mostly virtual stats that the server computes; the client
derives none of them (§4). Passives and item mods give the base form (`base_stun_recovery_+%`) while the Stun Recovery
row reads `stun_recovery_+%`. `PanelStats` (issue #75) lists, for about 150 display stats, what each one sums: the display
stat's own value (Haste gives `cast_speed_+%` itself), its base form and the unconditional combined stats that apply to
it:

| Display | Also counts |
|---|---|
| `avoid_ignite/chill/freeze/shock_%` | `avoid_all_elemental_status_%` (Avoid Elemental Ailments) |
| `self_*_duration_-%` (ignite, chill, freeze, shock) | `self_elemental_status_duration_-%`, minus `base_all_ailment_duration_on_self_+%` |
| `virtual_ignite/chill/freeze/shock/scorch/frostburn/sap_duration_+%` | `base_elemental_status_ailment_duration_+%`, `base_all_ailment_duration_+%`, the per-element ailment duration, `chill_and_freeze_duration_+%` |
| `virtual_chill/shock/scorch/frostburn/sap_effect_+%` | `non_damaging_ailment_effect_+%`, `cold_` / `lightning_ailment_effect_+%` |
| `chance_to_freeze_%`, `chance_to_shock_%`, the ignite chances | `chance_to_freeze_shock_ignite_%` |
| `reduce_enemy_fire/cold/lightning_resistance_%` | `reduce_enemy_elemental_resistance_%`, `base_penetrate_elemental_resistances_%` |
| `cast_speed_+%` | `attack_and_cast_speed_+%` (also counted in attack speed) |
| `attack_speed_+%_per_frenzy_charge`, `cast_speed_+%_per_frenzy_charge` | the matching `base_` coefficient and `attack_and_cast_speed_+%_per_frenzy_charge` |
| `life/mana_leech_from_*_damage_permyriad` | the elemental leech for fire, cold, lightning; life also `horrible_ugly_life_leech_from_*_damage_perhundredthousand` ÷ 10 |
| `virtual_life/mana/energy_shield_leech_speed_+%` | `life_mana_es_leech_speed_+%` |
| `virtual_endurance/frenzy/power_charge_duration_+%` | `charge_duration_+%`, `base_charge_duration_+%` |
| `virtual_block_recovery_+%` | `block_recovery_+%`, `base_stun_recovery_+%`, raw `stun_recovery_+%` |
| `life/mana/energy_shield_gained_on_enemy_death` | the matching `base_` amount and `*_gained_on_enemy_death_per_level` times actual player level |
| `virtual_flask_duration_+%` | `flask_duration_+%` and `flask_duration_+%_per_level` times actual player level |
| `display_skill_increased_item_rarity/quantity_+%` | `base_item_found_rarity/quantity_+%` |
| `virtual_trap/mine_throwing_speed_+%` | `trap_and_mine_throwing_speed_+%` (mine: `mine_laying_speed_+%`) |
| `minimum/maximum_*_damage_to_return_to_melee_attacker` | `*_damage_to_return_to_melee_attacker` (both ends) |
| `extra_damage_taken_from_crit_+%` | − `base_self_critical_strike_multiplier_-%` |
| `virtual_total_main_hand/off_hand_impale_on_hit_%_chance` | general + attack-only + that hand's local chance, capped 0–100 |
| `virtual_total_spell_impale_on_hit_%_chance` | general + spell-only chance, capped 0–100 |
| `intermediary_combined_action_speed_+%` | − `action_speed_-%` |
| `cannot_be_stunned_by_spells`, `cannot_be_stunned_by_attacks` | flags of their own (the all-stun flag stays its own row) |
| `brands_per_target_limit` | `base_number_of_sigils_allowed_per_target` (the selected brand's own 1 plus "additional brand attached" sources) |
| `virtual_maximum_spirit_charges` | `maximum_spirit_charges` (abyss-jewel counting is not modelled) |
| `virtual_physical_damage_taken_per_minute_per_siphoning_charge` | the `..._if_have_used_a_skill_recently` coefficient |

Beyond the sums, `PanelStats.Defence` works the Defence tab's estimate rows out against the reference monster of the
character's level (DefaultMonsterStats `Damage` and `Accuracy`, as Path of Building's CalcDefence does):
`display_estimated_physical_damage_reduciton_%` is `DamageModel.ArmourReduction` (armour / (armour + 5 × damage))
plus the additional physical reduction (Endurance Charges, passives), capped at 90%; "increased Armour against
Projectiles" adds `physical_damage_reduction_rating_against_projectiles`, `armour_differs_against_projectiles` and the
projectile estimate; "additional Physical Damage Reduction against Hits" adds `armour_differs_against_hits` and the hits
estimate; `chance_to_evade_%` is 100 − the reference monster's hit chance (`DamageModel.EvadeChance`) plus flat
`chance_to_evade_attacks_%`, capped at 95%; `ward_delay` (milliseconds, `WardState.Delay`) shows with Ward;
`mana_recharge_rate_per_minute` is the energy shield recharge rate on maximum mana when
`energy_shield_recharge_apply_to_mana` is set (nothing in 3.29 applies recharge to Life, so that row stays blank);
`omniscience_for_attribute_requirements` is Omniscience × `attribute_requirements_can_be_satisfied_by_%_of_ascendance`.

The Offence tab's remaining rows: a dual-wielded attack (the default attack and attack gems) sets `skill_uses_off_hand`
and publishes the off hand's own `off_hand_accuracy_rating`, chance to hit (plain and evasive), `off_hand_attack_speed_+%`,
critical strike chance and multiplier and the twelve `off_hand_*_damage` ranges (`Program.OffHandPanel.cs`: the shared
character buckets with the off-hand item's local accuracy, critical chance and attack speed in the main hand's place,
and the gem's own flat/increased accuracy and critical increases; a gem's off-hand hit is the same supported hit swung
with that weapon). While dual wielding, Damage per Use, Damage per Second and Attacks per Second average the two
hands' own values (`DualWieldPanel`, as PoB's "AVERAGE" weapon-pass combination; the off hand swings its own
duration at `off_hand_attack_speed_+%`). `spell_effective_total_chance_permyriad_for_hit_to_be_critical` / `_double_critical` apply the luck
policy and the Assassin's `spell_critical_strikes_bifurcate` (`CriticalStrikePolicy.EffectiveChance`: 1 − (1 − c)²,
and c² for both rolls). A spell flagged `display_skill_deals_secondary_damage` (Vaal Detonate Dead) also fills the
`secondary_*` damage and critical rows; a flagged skill without spell base damage (Bear Trap, Herald of Ice) fills them
from its own `secondary_*_base_*_damage` with the generic, area and projectile buckets only, the skill's base critical
chance with generic increases and the critical-weighted Damage per Use (`Program.SecondaryPanel.cs`,
`SkillCatalog.SecondaryHitDamage`). `skill_show_average_damage_instead_of_dps` follows the skill's `base_` flag
(movement skills). The Bleeding / Ignite / Poison damage per second rows per hand, spell and secondary context are
`AilmentPanelStats` ([20-damage.md §3](20-damage.md#3-damage-over-time-and-ailments)).

`PanelStats.Compute` receives the actual character level from `Pools.Level`, clamped to 1–100, not a gem level or
an arbitrary `level` stat. Per-level sources multiply before integer division using wide intermediates; only the final
published sum saturates to the native integer range. All aliases read the same raw snapshot before any result is
written back. In particular, Block Recovery adds the raw direct and base Stun Recovery once; it does not add the
newly derived Stun Recovery total a second time. Stun Recovery already expanded from held charges counts once too.
The on-kill rows add their direct/base amounts and per-player-level amounts; this does not implement on-kill resource
recovery, maximum-pool percentage sources, prohibitions or other conditional operators. The signed flask-duration
sum is a display coefficient; actual flask use and local/global factor composition are documented in
[13-items.md](13-items.md#8-life-and-mana-flasks).

Selected aliases derive from one immutable raw character snapshot plus the qualifying skill/support's own raw
inputs: `skill_area_of_effect_+%`, `display_skill_increased_item_rarity_+%`, `virtual_minion_damage_+%`,
`reduce_enemy_fire/cold/lightning_resistance_%`, the eight main/off-hand penetration aliases, and twelve
ailment duration/effect coefficients described below.
The rarity sum includes direct/base item rarity and `base_killed_monster_dropped_item_rarity_+%`; the minion sum
includes increased `minion_damage_+%`, keeping support MORE separate. Previously derived actor aliases are not fed
back as source inputs. The completed signed sum clamps once and retains explicit zero after cancellation/removal;
a final developer alias replacement, including zero, replaces the complete selected sum.
Both `PlayerSkills` refreshes and `SendPlayer` initial/area-transition object creation pass the same raw values,
override membership and actual player level into `GemSkills`. The selected overlay runs after Cyclone's radius
calculation, preserving its separate base/radius inputs: actor area 20 plus real Increased AoE own 30 gives area 50,
radius 19 from base 16, and radius-percent 22; unlinking restores area 20/radius 17 without counting the alias twice.
Real linked supports cover AoE 20+30=50, minion increased 15+quality 10=25 and ordinary rarity 10+40=50.
The historical LocalExile tests additionally excluded Item Rarity Support's own40 in Ruthless; that private
mode-specific availability is outside this branch. Actor sources and explicit final
replacements still work. Requirement failure, incompatible supports, unlink/relink/removal, unrelated/default skill
isolation and repeated builds are covered. This changes selected publication; it does not add minion combat or
item-drop execution, and native validation of these aliases remains outstanding.

The three elemental penetration aliases previously inherited only the character's value even when a linked
support supplied more penetration to the hit calculation.
Real level-one Fire/Cold/Lightning Penetration supports each grant 20 through their matching base stat.
Character 10 plus support 20 now publishes 30; adding general elemental penetration 5 gives 35 in both the
selected record and the existing hit consumer.
Nine Gateway regressions cover all three elements, link/source removal, signed cancellation, explicit final
zero/replacement, support requirement failure/recovery and unrelated/default-skill isolation.
These pass within 29 focused tests on this branch; fresh native panel verification remains pending.
The eight attack-hand penetration aliases now also include their attack-only typed and elemental base sources.
Selected attacks recompute those sums from raw actor/skill values and each active hand's own local modifiers.
The immutable publication snapshot retains both local dictionaries so recomputation cannot lose them or read an
already-derived alias as an additional source.
General 10 plus elemental 5 plus typed attack 7 plus elemental attack 3 gives 25 for each hand and 15 for spells.
Six real-support lifecycle regressions cover selected Heavy Strike for all three elements, five pure regressions
cover attack-only and local scope, and one real-modifier Gateway case preserves main 6/off 9 while excluding an
inactive weapon's 30; removing either active modifier or unlinking the support restores the corresponding values.
These are automated publication checks; native formatting remains unverified on this review's changes.
The player/own `cannot_penetrate_or_ignore_elemental_resistances` flag now explicitly clears all nine elemental
aliases, including zeros in the actor projection, while leaving the two chaos hand aliases intact.
Removing the flag restores the source totals. Explicit developer replacements retain their existing final priority.
Real Elemental Penetration support contributes its non-base elemental input (14 at level one), composing with
actor 10 and direct elemental 7 to show 31; attack-only 5 raises each hand alias to 36.
The combat consumer now reads these non-base general inputs and the attacking weapon's local inputs separately;
[20-damage.md](20-damage.md) records scope, timing, contact tests and remaining off-hand/conditional mechanics.
Decision: publish explicit elemental zeros when blocked, because a sparse omission could retain an earlier raw
alias. The alternative of merely hiding the row would leave the underlying selected value incorrect.

Selected ailment coefficients now include qualified skill/support inputs for seven durations
(`virtual_ignite/scorch/chill/freeze/frostburn/shock/sap_duration_+%`) and five non-damaging effects
(`virtual_scorch/chill/frostburn/shock/sap_effect_+%`). The existing raw source formulas remain shared with the
actor projection. Level-one Unbound Ailments grants `base_all_ailment_duration_+% = 35` and
`non_damaging_ailment_effect_+% = 35`; character 10 plus support 35 now publishes 45 instead of 10.
Its separate 10% more ailment damage does not enter either increased coefficient.
Twenty-four real-support regressions first reproduced 45 expected/10 actual, then verify source/link removal,
unrelated/default-skill isolation, signed cancellation, explicit final replacements and repeated publication.
Reference: extracted `SupportUnboundAilments` stat set and local library `pob/skills/SupportUnboundAilments`
at PoB `16de4b82d57f1c0de6eb40f37143c32d4da36a02` (labelled 3.29.x fallback).
Decision: recompute these twelve established coefficient formulas from raw inputs. The alternative of treating
every ailment row as a simple sum would gloss over critical weighting, prohibitions, damage eligibility
and actual duration/rate calculation. Those mechanics and native validation remain separate completion work;
this publication correction does not implement ailment application or certify the whole ailment system.

Leech projection now includes all 24 scoped coefficient aliases and three recovery-speed aliases in selected
skills. Each scope adds the general any-damage source, its applicable typed/spell/attack source, elemental sources
for fire/cold/lightning, and shared physical-attack Life/Mana sources only in the hand totals. Despite lacking
"attack" in its ID, `life_and_mana_leech_from_physical_damage_permyriad` is explicitly attack-only in its description.
Both previously missing physical/chaos ES aliases
are emitted. Physical-attack hand totals add only that qualified hand's physical, combined Life/Mana and general
local inputs. Mana Leech Support's `attack_skill_mana_leech_from_any_damage_permyriad` enters attack scopes only.
The overlapping aliases are display totals, not independent coefficients to add together in a hit consumer.

Level-one Life/Mana Leech supports supply 200 permyriad; Energy Leech supplies 150. Real linked supports now
add those amounts, follow unlink/removal and requirements, and leave unrelated/default skills isolated. The
Energy Leech item metadata is `SupportGemEnergyShieldLeech`, while its effect is `SupportEnergyLeech`.
Selected per-hundred-thousand Life sources combine across actor/own inputs before dividing by ten; separate
truncation previously lost a 6+4 boundary. Final developer replacements retain their established priority.
Pinned PoB `16de4b82` supplies the three support stat sets and the general/elemental/typed/local descriptions
(`Data/Skills/sup_str.lua`, `sup_dex.lua`, `sup_int.lua`, `Data/StatDescriptions/stat_descriptions.lua`).
Scope composition follows those descriptions; current native getter/formatting parity remains unverified.
Decision: derive each total once from raw sources rather than summing overlapping derived aliases or rounding
actor/support fractions separately. Ordinary active leech now consumes raw qualified sources at accepted contacts
and uses separate instance duration, total recovery and cap rules (damage §5b). Ghost Reaver converts independent
Life-source grants to ES and applies its cap/recharge effects. Conditional sources, other conversions,
skill-specific recovery-speed ownership and native validation remain open in the review ledger.
The owner now publishes actual leeching/full-ES conditions and refreshes selected skills when they change.
Energy Leech's full-ES MORE damage and quality increased damage while ES leeching use distinct buckets, with
real support/unlink and accepted projectile controls. General/resource/attack/physical leeching hit-damage sources
use the same live flags; expiry inspection preserves pending recovery. See damage §5b for decisions and remaining scopes.

Selected impale chance now recomputes all three virtual totals from the raw actor, own support and each hand's
local snapshot. Real Impale Support grants attack chance 60: actor 10 gives selected 70 while the actor/default
remain 10 and spell chance remains zero. Qualified local weapon modifiers 15/17 produce main 85/off 87 without
including the inactive weapon. Completed sums clamp to 0–100, and explicit final developer overrides retain
their normal priority. Unlinking, source removal, incompatible spell support and repeated reads are covered.
The old projection from attack chance into the general `impale_on_hit_%_chance` is removed: it erased source
scope and could make attack-only inputs apply to spells/secondary hits. Decision: use raw scoped inputs in combat
and recompute final display aliases, rather than feeding final aliases back into calculations. Native rendering,
critical-only spell chance presentation and the remaining conditional sources are still open; see §3a of the
damage module for the actual hit implementation and evidence.

`PanelStatDefaults` adds six exact native `Character.ot` values to the original actor sources before derivation:

| Native stat | Inherent value |
|---|---|
| `base_total_number_of_sigils_allowed` | 3 |
| `chance_to_deal_triple_damage_%_per_brutal_charge` | 3 |
| `stun_threshold_+%_per_brutal_charge` | 10 |
| `ailment_damage_+%_final_per_affliction_charge` | 8 |
| `non_damaging_ailment_effect_+%_final_per_affliction_charge` | 8 |
| `elemental_damage_taken_goes_to_energy_shield_over_4_seconds_%_per_absorption_charge` | 12 |

Item/passive additions enter once, repeated snapshots start from the original sources, and explicit developer
replacements, including zero, keep precedence. Native AdditionalBrand +1 and Brand Equity graph 64355 +2 give actor
limit 6; a compatible linked Foulgrasp contributes its own +2 for selected limit 8.
`SkillCatalog.EffectStats` supplies `skill_is_brand_skill` from native ActiveSkillType Brand, row 64 / FlagStat 8740,
only for the selected Brand. Fireball/default attack do not receive an actor-wide Brand gate.
The five special-charge values remain per-charge coefficients, independent of held counts and maxima.
The corresponding special maximum gates are described in §15; defaults do not implement Brand attachment/cap
enforcement or special-charge gain, expiry, combat effects or resource recoup.
Automated cases cover native sources, both modes, real support compatibility, explicit zero and source removal;
combined results are below and new native checks remain pending.

The added-base critical, native-default/mirror and selected-alias changes pass 93 new cases (52/19/22 respectively),
plus 95 existing focused controls, for 188/188 with no skips. Fresh full suites pass Gateway 3803, Core 648 and Data 37:
4488 freshly rerun cases, all passing without skips. The current diagnostic hook recorded six actual unobserved
disposed-NetworkStream task faults, all `MonsterKilled` at `Program.Combat.cs:1260` through
`Program.Send` / `Program.Connection.cs:326`; 385 first-chance records are separate from that fault count.
No diagnostic errors, text truncation or cap hit occurred, and no captured fault names a new operator path.
Passing suites therefore do not establish fault-free background execution.
Native validation of the defaults/mirrors and selected aliases remains outstanding; the bounded critical slice is in §5.
The historical G14/full 3710 and native results below do not validate these new operators or certify all 387 rows.

The four elemental self-duration reduction sums also subtract the unconditional raw
`base_all_ailment_duration_on_self_+%` source (wire key 17087). Native Harsh Lessons, PassiveSkills row 2598 / graph
27623, gives -20 in both modes and therefore adds +20 to each reduction. Typed, elemental and all-ailment sources
combine once; an increased self-duration source gives a signed negative reduction. Explicit final developer
replacements, including zero, still take precedence in the Gateway. This fixes the four declared numeric sums,
not ailment execution. Ring-slot keys 17088/17089 need actual left/right-slot selection and remain separate;
adding both as global sources would incorrectly cancel them. Other ailment families and conditional ES delay
also remain separate work. All 14 new pure/Gateway cases pass within the combined 53/53 focused batch, followed
by full Gateway 3710/3710 with no skips. Prior unchanged Core 648/Data 37 runs remain applicable (4395 applicable
cases, not 4395 newly rerun cases). The full-run diagnostics retain five actual unobserved disposed-stream task
faults: four `MonsterKilled` and one `NotifyMonsterHit` → `AreaSkillHit` → `LaunchProjectile`. No captured fault
mentions the new self-duration/flask-effect paths; passing tests do not mean fault-free background execution.
Native verification of these new operators remains outstanding.

The preceding panel/source batch covered actual level changes, real modifier identities, source qualification,
removal, explicit overrides, signed values and repeated builds in both modes. Its integrated panel/flask validation
passed 55 new and 80 existing focused cases, full Gateway 3657/3657, and Core 648/Data 37 (4342 applicable cases).
Those earlier results and native observations below do not validate the newly added self-duration operator or
subsequent flask-effect resolver. Their new automated results are stated separately above; no complete-387-row
or new native-coverage claim is made.
The combined native assessment contains 40 completed controls: 22 complete PASS and 18 complete zero controls
UNPROVED. The passes comprise 12 positive flat/level-source cases at actual levels 10, 20 and 100 in Standard and
Ruthless, six utility-duration uses and four Life-recovery uses. All positive source cases match the six fixed fields:
combined Block/Stun Recovery, the three on-kill amounts and the flask-duration coefficient. Across the six panel
fields, including clearing/restoration/relog transitions and retained observations, 138 verdicts are VERIFIED and
42 UNPROVED. Both Defence zero fields remain UNPROVED in all 18 zero controls; several Misc zeros remain UNPROVED
too. Missing callbacks or hidden rows never establish rendered zero. Actual duration/recovery values and the original
one-integer Life publication boundary are recorded in protocol 13 §8. Earlier failed readiness/vendor observations
remain preserved; these controls do not establish complete on-kill or block-animation mechanics.

Life/Mana Regeneration per Second and the corresponding native recovery inputs share `PanelStats`' calculation.
Flat plus maximum-pool percentage sources receive separate regeneration and recovery increases.
Ordinary prohibitions stop the matching resource; source removal clears its previously published total.
Ten native controls matched the published totals to actual recovery, clearing and relog.
Regeneration conversions, named conditional modifiers and specialised more factors remain separate.
See [20-damage.md](20-damage.md#5-continuous-resource-updates-2026-10-24) for the sources and evidence boundary.

**Energy Shield Regeneration per Second** (`CharacterPlayerStats`, issue #75) reads
`total_energy_shield_recovery_per_minute_from_regeneration` (client key 1058, `0x422`).
The native `ESRegen` row divides this per-minute total by 60.
The Gateway sends `EnergyShield.RegenPerMinute`, the same calculation the recovery loop applies, rounded to an integer.
It combines flat and maximum-ES percentage regeneration, then increased regeneration and increased recovery.
The total excludes recharge, including while the native prediction input combines both recovery sources.
Removing the source or prohibiting regeneration sends zero; an explicit `/set_stat` display override keeps precedence.
Native checks at 14 maximum ES received 120/minute from flat regeneration, 137 with 120%/minute added,
and 246 with 50% increased regeneration and 20% increased recovery.
The Defence panel rendered the flat input as 2 per second and removed the row when regeneration was prohibited.
During recharge a separate 60/minute control stayed at 60 while the prediction input changed from 60 to 340.
Clearing the sources and relog both restored zero.
See [20-damage.md](20-damage.md#player-energy-shield-2026-10-25) for supported ES mechanics and prediction limits.

**Main hand / off hand rows** (`main_hand_…` / `off_hand_…`, `virtual_total_main_hand_…`) add the global stats and that
hand's item's own local stats (`HandLocalStats`: active rows 2/3 only; inactive rows 14/15 contribute none):
`local_chance_to_bleed_on_hit_%`, `local_poison_on_hit_%`, `local_attacks_impale_on_hit_%_chance`,
`local_chance_to_blind_on_hit_%`, `local_fire/cold/lightning/chaos/elemental_penetration_%`,
`local_stun_threshold_reduction_+%`, `local_life/mana_gain_per_target`, `local_life/mana_leech_from_physical_damage_permyriad`
and `local_double_damage_to_chilled_enemies`. Spell and secondary rows use the global stats only (`spells_…` where the
game has a spell form).

**Damage rows from the hit the server swings with** (`PanelStats.HandDamage`, issue #75):
`main_hand_minimum_total_damage`, `main_hand_maximum_total_damage` and the per-type
`main_hand_minimum/maximum_{physical,fire,cold,lightning,chaos}_damage` are computed from
`Skills.UnarmedFor(ClassRow).HitDamage(stats)` after conversion, rounded down because the client shows whole numbers, and
sent with `skill_uses_main_hand = 1` — the client gates the rows on that flag, so the numbers alone leave them blank. Only
the main hand is filled, since the server attacks with it; an empty packet adds no rows.

**Critical- and accuracy-weighted hit averages (issue #75 review correction).** Ordinary main-hand attacks and selected
supported attacks/spells now weight `display_hundred_times_damage_per_skill_use` (2044) and
`hundred_times_damage_per_second` (679) by their current critical chance and multiplier. They use the same signed
luck policy as combat: probability `p`, `1-(1-p)^2` when lucky, or `p^2` when unlucky. Given that resulting critical
probability `c` and chance to land `h`, the mean multiplier per attempt is
`h * (1-c*h + c*h*criticalMultiplier/100)`. The inner `h` is independent critical confirmation; luck applies to
the critical roll before confirmation. Spells use `h=1`. Attacks use the normal reference monster's evasion at
the player's level and their ordinary/selected accuracy profile, including qualified raw cannot-evade sources.
Target Masteries stay out of this unconditional estimate. Ordinary main-hand normal/evasive chance aliases
also now respect cannot-evade. Explicit diagnostic chance aliases remain display replacements, not inputs to
this calculation. Critical prohibition supplies zero critical probability. Low-life/Focus/raw luck changes
therefore update the means without changing the noncritical minimum/maximum ranges. Hitless/suppressed packets
remain zero. Each supported skill uses its own critical support contributions, not the ordinary attack's values.

`SpellDamage.Display` is the shared hundredths formatter for all three paths. It retains fractional packet means
and the actual action duration until the final rounding, clamps negative results to zero and saturates values
above the native signed-int maximum. The selected constructors no longer calculate once with rounded APS/CPS
and then overwrite the same fields with a second calculation. Their rate parameter is now actions per second.
No per-hit or target random draw occurs during publication.

Decision: preserve the existing final precise-rate behavior while adding the critical policy already used by
combat. The alternative of multiplying the rounded displayed rate changes results at every rounding boundary;
the alternative of retaining precritical means continues to ignore real critical supports. Fractional means remain
continuous packet estimates, not the expectation of every integer damage-roll/rounding path.
[Mark_GGG's mechanics answer](https://www.pathofexile.com/forum/view-thread/1676002/filter-account-type/staff/page/1)
confirms tooltip DPS includes critical strikes and accuracy (the Lacerate/Double Strike answer). This correction
covers critical and reference-target accuracy/confirmation weighting; dual-hand and
multi-hit counts, cooldown/downtime, conditional more critical sources and damage-over-time DPS remain open.
Native validation is pending. The historical numeric observations below are precritical values from the older
implementation, not evidence for the corrected averages. Automated before-controls reproduce six omissions
across ordinary melee/bow, Cyclone, Caustic Arrow, Fireball and Ice Nova; source/luck/prohibition, actual rate,
hitless/support removal, fractional and overflow checks pass.

Pinned PoB `16de4b82d57f1c0de6eb40f37143c32d4da36a02`, `CalcOffence.lua:3146` applies hit chance after the
critical/luck calculation, and lines 3788–3801 weight the critical mean by landing chance and actual action rate.
This is an expected-damage reference, not native 3.29.3b rounding certification. Six additional before-fix
regressions reproduce missing attack weighting while a spell control passes. Nineteen added cases include
luck/bounds, real Additional Accuracy link/removal, local/global cannot-evade, diagnostic alias isolation and
unchanged spell/range outputs. The expanded focused run passes 55.
Decision: weight the critical bonus by both landing and confirmation, rather than applying accuracy only once
to the old critical mean or applying critical luck to the confirmation check. Keep the established normal
reference enemy and full-precision formatter instead of guessing target conditions or rounding intermediate
percentages. Selected gem attacks consume their own profile for both gameplay and these estimates;
per-use critical rolls are shared as documented above. Remaining source conditions and special repeats still require review.
[Damage §2g](20-damage.md#2g-ordinary-attack-evasion-and-critical-confirmation) records the source evidence,
snapshot/admission decisions, alternatives, parsed-contact controls and remaining native work.

**Selected attack-gem hit rows (#196)** (`AttackSkillHit`, `AttackPanelStats`): main-hand total and per-type
ranges now use the same packet constructor as projectile and area attack combat.
The constructor starts with raw local weapon and added damage, applies the skill's level-aware effectiveness,
then resolves conversion/gain history and inherited modifiers together through `PlayerHitDamage`.
Conversion retains `ConversionFor` precedence: player keys win and level-only skill keys fill gaps, without quality
conversion. Final supported rows are then replaced from `SupportedHit`'s existing merged supported inputs.
Burning Arrow level one uses effectiveness 18000 and 100% skill physical-to-fire conversion; Heavy Strike uses 12430.
All twelve main-hand range fields are explicit and floored, including zeros for absent types after conversion.
`CharacterPanelStats` reads whole-number total keys 670/671, physical 279/280, fire 267/268, cold 263/264,
lightning 271/272 and chaos 275/276. These rows retain the existing main-hand and spell/secondary gates.
Per-use average (2044) and DPS (679) are hundredths; packet fractions are retained through the average. The
current final supported projection uses the exact action duration and critical weighting as described above.
Earlier standalone range tests use their explicitly supplied rates, while final publication uses the combat rate.
Copied-save GUI baseline: a 5-13 physical bow with Strength 14 showed 5-13 physical/total, per-use 9.18 and DPS 12.86
for both ordinary attack and Burning Arrow, while Burning Arrow already showed its corrected APS 0.98.
The preserved raw packet is 5.1-13.26; Burning Arrow resolves 9.18-23.868 fire, average 16.524.
Actual native checks now show 9-23 fire/total, physical zero, per-use 16.52 and DPS 16.19 at emitted APS 0.98.
Adding 5 flat cold attack damage showed cold 9-9, fire 9-23, total 18-32, per-use 25.52 and DPS 25.01.
An actual noncritical server hit dealt 24 damage to an 86-life Ash Harvester.
Clearing the added cold stat returned every cold field to zero and restored the 9-23 fire/total display.
Relog retained Burning Arrow's 9-23, per-use 16.52 and DPS 16.19; ordinary attack stayed 5-13 physical,
per-use 9.18, DPS 12.86 and APS 1.40.
The fixture is also regression-tested.
Those historical averages exclude critical weighting; the current correction is described above.
Accuracy, target mitigation, integer-roll expectation and downtime remain excluded.
Initial creation and refresh both pass raw `CharacterPlayerStats` plus the actual weapon into `GemSkills` separately
from inherited final display stats. Derived hand ranges cannot feed back into the packet.
Only attack gems receive this override; ordinary attacks, spells, non-attack actions and off-hand rows keep their
existing display paths. Supports, conditional mechanics, off-hand alternation and broader modifier-scope corrections
remain separate work. Existing character modifiers are preserved even where their weapon scope remains approximate.
Real catalog, packet/source-isolation, conversion, added-damage, flooring, absent-type and native-table tests pass.
Native skill selection, stat refresh/clearing and relog checks passed; equipment UI refresh was not separately exercised.
Local integration captures are `logs/attack-damage-burning-arrow-{default,added-cold,cleared,relog}.json`
and `logs/attack-damage-server-hit.json`; no extracted data changes are needed.

**Selected spell hit rows** (`SpellPanelStats`, issue #188): `spell_minimum/maximum_total_damage` and
`spell_minimum/maximum_{physical,fire,cold,lightning,chaos}_damage` come from `SpellHit.Resolve`, the same converted
packet used by projectile and area combat. Every spell range is written, including zeros for absent types and hitless
spells. CharacterPanelStats reads total keys 674/675, physical 261/262, fire 255/256, cold 253/254, lightning 257/259
and chaos 258/260 as whole integers, with `casting_spell` (438) required and
`display_skill_deals_secondary_damage` (1675) excluded. DPS (679), damage per use (2044) and casts per second (682)
are hundredths (the table's formatter 14), so range values must not be multiplied by 100.
For example, level-one Fireball's 9–14 with +12% spell damage gives a 10.08–15.68 packet: the range is 10–15,
per use is 12.88, and DPS at the emitted 1.33 casts/s is 17.13. These historical standalone averages exclude crit
and mitigation; final supported publication now uses the exact duration and current critical weighting above.
The same skill construction runs on initial player creation and subsequent refresh; character conversion inputs
are passed separately from the inherited final weapon/display stats. Attack and unsupported-action displays keep
their existing path.
Native panel/getter checks with a +12% spell-damage wand show Fireball 10-15 fire, 12.88 per use and 17.13 DPS,
and Freezing Pulse 10-15 cold, 12.88 per use and 19.84 DPS.
With 50% fire-to-chaos conversion and 20% fire gained as chaos, Fireball shows total 12-18, fire 5-7 and chaos 7-10,
15.46 per use and 20.56 DPS; an actual noncritical server hit dealt 14 damage.
Scorching Ray has twelve zero hit-range fields and zero DPS/per-use, also after adding 100 flat spell fire damage.
The same added-damage control gives Fireball 234-239 fire, confirming that hit-capable spells still receive it.
Automated packet, conversion, hitless, creation/refresh and table-format checks also pass.

**Rows that combine a pool with a stat** (`PanelStats.FromPools`): `projectile_attack_block_%` = attack block +
`additional_block_chance_against_projectiles_%`, `spell_projectile_block_%` = spell block + `base_spell_projectile_block_%`
(with `spell_block_differs_against_projectiles` 1, which shows the row), each capped at its maximum, and
`chaos_damage_over_time_resistance_%` (and its uncapped form) = chaos resistance +
`additional_chaos_resistance_against_damage_over_time_%`, capped at the chaos maximum. Each is sent only when its stat is set.

`PanelStats` does not independently evaluate conditional stats (while, if, per charge, against …); the explicit
player-level sources above and already resolved charge contributions use their supplied values. Flags (`cannot_be_frozen`, `immune_to_ignite`
…) are 1 when any source is set; "Immune to Ignite and Shock" sets both. Only non-zero values are sent; a sum that drops
to zero is cleared with the other stale stats (`PlayerStatsWithCleared`). A stat set with `/set_stat` keeps its value.
`cast_speed_+%` is now the total, so `CastSpeedPct` reads only it. Which combined stats count is from the game's
documented behaviour, not a capture; the signs of the crit-taken and action speed rows are not confirmed. Live
2026-10-20: `/set_stat base_stun_recovery_+% 30`, `avoid_all_elemental_status_% 20` and `base_cannot_be_frozen 1` show
on the Defence tab as Stun Recovery 30%, the four Avoidance rows at 20% and "Cannot be Frozen".

## 15. Endurance, Frenzy and Power charges

Issue #237, part of #75's Charges tab. Code: `Charges.cs` (state and limits), `ChargeGains.cs` (what grants them),
`ChargeEffects.cs` (what they do), `ChargeSession.cs` (one connection's charges at work), `ChargeBuffs.cs` (the HUD
buffs), `Program.Charges.cs` (the Gateway glue) and `ChargePanelStats.cs` (the maxima).

- **Rules.** A charge lasts 10 seconds (poewiki's Charge page; Path of Building's CalcSetup `ChargeDuration` BASE 10;
  no game table holds it), × (1 + `charge_duration_+%` + `base_charge_duration_+%` + the type's own increase
  (`endurance_charge_duration_+%`, `base_frenzy_charge_duration_+%`, `power_charge_duration_+%`) +
  `frenzy_charge_duration_+%_per_frenzy_charge` × the Frenzy Charges after the gain) × (1 +
  `power_charge_duration_+%_final`), floored to milliseconds. Gaining a charge restarts the duration of every charge of
  its type, also at the maximum, where none is added, and all charges of a type expire together (poewiki). The maximum
  is `ChargePanelStats`' (3 from Character.ot plus `max_*_charges`, the combined power-and-endurance and
  power-and-frenzy mods and `maximum_frenzy_power_endurance_charges`; verified on the native Charges tab with all three
  baseline rows at `0 / 3`); the minimum is `base_minimum_*_charges` +
  `minimum_frenzy_power_endurance_charges`, at most the maximum, and is always held; a gain adds on top of what the
  character has (Path of Building: max(charges, min(maximum, minimum)); poewiki: the maximum takes priority). A lower
  maximum (an item taken off) removes the charges above it at the next step: the session observes the limits and the
  periodic stats whenever the character's stats are worked out (`ChargeSession.Observe` in `WithCharges`), and nothing
  polls them. "% chance that if you would gain … Charges, you instead gain up to maximum" is
  rolled on every gain. Time is the connection's game clock, which stops while the game is paused.
- **Special maximum publication.** The three native Domain 1 mirror modifiers, Mods rows 26064–26066, publish
  Brutal from completed Endurance maximum, Affliction from completed Frenzy maximum and Absorption from completed
  Power maximum only while their corresponding `maximum_*_charges_is_equal_to_maximum_*_charges` flag is present.
  They run after standard maximum derivation, so explicit final standard replacements, including zero, are visible;
  explicit final special replacements also keep precedence. Removing a flag publishes a zero special maximum and
  clears the removed raw flag without retaining a stale native gate. Real modifier fixtures resolve unequal standard
  maxima 4/5/6 and cover independent removal/recovery and zero overrides in both modes.
  The formulas follow pinned PoB `16de4b8` `CalcPerform` with exact native source identities, as a labelled 3.29.x
  fallback; every official virtual getter has not been independently traced. Combined automated results are in §14;
  new native checks remain pending. This bounded publication leaves standard held state/gains unchanged; the
  current counts and effects of the three converted types are the next bullet, and Spirit/Abyss-jewel counting is
  not implemented.
- **Converted charges** (Brutal, Affliction, Absorption; 2026-10-29). The three Domain 1 unique mods
  `gain_brutal_charges_instead_of_endurance_charges`, `gain_affliction_charges_instead_of_frenzy_charges` and
  `gain_absorption_charges_instead_of_power_charges` (`Charges.ConversionFlag`) are these charges' only 3.29 player
  sources. While the flag is held (observed with the limits, `ChargeSession.Observe`), the held Endurance / Frenzy /
  Power charges *are* the special type (pinned PoB CalcPerform: special = standard count, standard = 0): the held
  state, limits, gains, expiry and area carry stay the standard type's. `ChargeEffects.Expand(converted)` gives the
  standard per-charge and "with no / any … charges" stats zero charges of the type and the `Special` rules the count:
  `chance_to_deal_triple_damage_%_per_brutal_charge` → `base_chance_to_deal_triple_damage_%` (the hit's gambler roll)
  and `stun_threshold_+%_per_brutal_charge` → `stun_threshold_+%` (the stun chance; every stat `PerCharge` expands is
  excluded from `PlayerHitRecovery.UnsupportedStat`, `ChargeEffects.Expands`, Frenzy's stun recovery included),
  with `PanelStatDefaults`' coefficients counted. `ChargeEffects.DamageMore(ailment: true)` multiplies damage with
  ailments by `ailment_damage_+%_final_per_affliction_charge` × `current_affliction_charges` (the ailment panel rows;
  no server path applies player ailment damage yet). `Display` sends `current_brutal/affliction/absorption_charges`
  and `minimum_*` (under `modifiers_to_minimum_*_charges_instead_apply_to_*_charges`) and zero on the standard rows;
  the HUD uses BuffDefinitions 1757 `brutal_charge`, 1758 `affliction_charge`, 1759 `absorption_charge` (whose
  `Current_StatsKey` is `current_*_charges` itself) and swaps the buff when the flag changes. Live 2026-10-29 with the
  flags set by `/set_stat` (rows, buff swap and the swap back). Not modelled: the non-damaging ailment effect per
  Affliction Charge and the Absorption recoup (no consumer); no conversion unique is defined yet (#60).
- **Blood Charges** (Reap; 2026-10-29; `BloodCharges.cs`, `Program.BloodCharges.cs`). ActiveSkills `reap`: a use whose
  hit leaves any enemy alive gives one charge, and the skill's `lose_blood_scythe_charge_on_kill` takes one per enemy
  its hit kills (`BloodCharges.Settle`, run by `AreaSkillHit` after the sweep's kills); the maximum is Character.ot's
  `maximum_blood_scythe_charges` 5 (PoB Misc.lua) plus the raw stat (the Reap enchantment). Each charge held is the
  skill's own `blood_scythe_damage_+%_final_per_charge` (15%, +0.25 per quality) more damage on its hits and on its
  Offence rows (the hit's raw packet is scaled, so the ranges, ailment rows and per-use rows agree), and
  `blood_scythe_cost_+%_final_per_charge` (20%) more cost at `PayMana` (the server pays Reap's cost
  from mana), one multiplier of value × charges (pinned PoB act_str.lua). Rows: `current_blood_scythe_charges`,
  `maximum_blood_scythe_charges` and `show_blood_charge_info` (1 while a socketed gem has the per-charge stat; the Blood
  Charges rows are gated by it); HUD: BuffDefinitions 1822 `blood_scythe_charge` with the count as stacks, re-sent on
  every change with `SendChargeStats`. Death takes them (`CancelOnDeath`). No game table or PoB gives them a duration,
  so they stay until a kill or death; an area change does not carry them; Vaal Reap's temporary maximum, Reap's
  physical damage over time debuff and its life cost are not modelled. `BloodChargesTests` and
  `BloodChargesGatewayTests` (a surviving target gives a charge, the next hit is floor(× 1.15), the cost × 1.4 at two
  charges, a kill takes one); live 2026-10-29 with a socketed Reap (rows, gain, loss, buff).
- **Death and area changes.** Death takes every gained charge at once (`PlayerDied`; BuffDefinitions `CancelOnDeath`
  on the three rows), so a pause or Resurrect in Town cannot keep them; the minimum stays. An area change (`Transfer`)
  hands the charges to the destination ticket with the time they had left;
  the new connection's ticker restores them and puts their buffs on the new player object, so loading does not count.
  A new login starts with none. Whether the official server keeps charges across areas is unverified: poewiki's Charge
  page says nothing about it. The rule is the one `CarryCharges` call in `Transfer`.
- **Gains** (`ChargeGains.Rules`): each source stat is its own roll of `rng.Next(100) < chance`; 100 and the "Gain a …
  Charge" flags gain without a roll, and a random type is drawn with `rng.Next(3)`. On kill:
  `endurance_charge_on_kill_%`, `add_frenzy_charge_on_kill_%_chance`, `add_power_charge_on_kill_%_chance`, the two kill
  flags, `power_frenzy_or_endurance_charge_on_kill_%`, the shield and dual-wield ones. On hit:
  `add_frenzy_charge_on_skill_hit_%` (the character's plus the used skill's), the rare-or-unique ones. On critical
  strike: power, endurance, melee endurance, frenzy, wand power, per enemy you crit. On non-critical strike: power,
  with claws or daggers. When hit: endurance, frenzy. On block: endurance, frenzy, attack-only frenzy, power,
  spell-only power, a random one. Over time (`ChargeGains.Periodics`): per second, every x ms (the body armour
  implicits), random every 6 seconds, each second if hit in the last 4 seconds, while stationary or moving (the walk
  estimate); not in towns. `gain_X_..._charges_per_second` carries the count, so a negative value
  takes charges away: Replica Fragility, Replica Pacifism and Replica Powerlessness store `-1` ("Lose an
  Endurance Charge each second") and lose one each second, never below the minimum; every other use of
  the stat is positive. Skills: the Frenzy gem's hit gives a Frenzy Charge once per use (ActiveSkills `frenzy`;
  without a Frenzy projectile row it hits as an area, and only its first target counts), Power Siphon's
  killing hit a Power Charge, Flicker Strike's and Power Siphon's own on-hit chances count, and Enduring Cry grants an
  Endurance Charge per 5 power (`endurance_charge_granted_per_X_monster_power_during_endurance_warcry`) of the hostile
  monsters within 60 cells grown by area (Path of Building's radius; power 1 normal, 2 magic, 10 rare, 20 unique) when
  its cast ends on the game clock, sped up by the player's and the gem's `warcry_speed_+%`; a later action does not
  cancel it (`cannot_cancel_skill_before_contact_point`).
  The combat code only enqueues events (`ChargesOnHit` at the default attack, area and projectile hits, `ChargesOnKill`
  in `MonsterKilled`, `ChargesWhenHit` in `MonsterSwing` and `/blockhit`); `RunCharges` rolls them every 50 ms, with
  the character's stats worked out outside the Monsters lock and only while events wait.
- **Effects** (`ChargeEffects`). Every character has Character.ot's per-charge stats (`Inherent`): 4% increased attack
  and cast speed and 4% more damage per Frenzy Charge, 4% physical and 4% elemental damage reduction per Endurance
  Charge, 50% increased critical strike chance per Power Charge (Path of Building's CalcSetup uses the same constants).
  They and the "per … charge" stats of passives, items and gems become, × the charges held, the stats the server
  already calculates with (`ChargeEffects.PerCharge`, added to the character's stat sources by `WithCharges`): attack
  and cast speed, critical strike chance and multiplier, increased and added damage, gain as extra chaos, resistances,
  armour, evasion, block, spell suppression, movement speed, accuracy, area, projectile speed, energy shield, life and
  mana regeneration, and panel sums (stun, cooldown, life on kill). The "with no Frenzy Charges" and "while you have
  any" stats follow the count (`Conditional`). The more damage per Frenzy Charge scales player hits once through
  `PlayerHitDamage`, including ordinary ranged and supported attacks, and Righteous Fire's burning separately
  (`ChargeDamageMore`). The physical damage reduction (with the
  passives' `base_additional_physical_damage_reduction_%`) adds to armour's and the elemental one applies after
  resistance, both capped at 90% (Path of Building's CalcDefence; `DamageModel.MitigatePhysical`, `DamageModel.Reduce`),
  for monster hits and the dev `/hit`, `/spellhit` and `/blockhit`.
- **Stats sent.** `current_*_charges` (the Charges tab's "x / y" rows, with `virtual_maximum_*_charges`),
  `actual_current_*_charges` (the buff rows' `Current_StatsKey`), `minimum_*_charges`, `current_removable_*_charges`,
  `additional_physical_damage_reduction_%`, `additional_elemental_damage_reduction_%` and the per-charge stats, which
  the per-charge rows read. A count change recomputes the pools and the walk speed and sends 0x1b3 and the skills'
  stats (the client swings and casts with the speed in its skill records), without the skill-bar work of
  `SendCharacterStats` (`SendChargeStats`).
  The Frenzy attack/cast speed rows publish the coefficient per charge, independent of the held count.
  Each includes its matching base coefficient and the combined attack/cast coefficient. Native Mods row 7236
  (`AttackAndCastSpeedPerFrenzyChargeUniqueBootsDex4`) supplies -4 to the combined coefficient, cancelling the
  inherent 4 for both rows. `WithCharges` already expands that raw source once for actual speed; display alias
  composition does not feed back into it. Seventeen selected automated cases (15 new) cover both modes,
  source removal, signed/zero replacements, held counts, typed composition and an explicit final zero.
  Eight native supplied-source controls verify owned player values, held-count speed, clearing and relog.
  Natural percentage formatting shows +4%, +6% and attack-only +5%; zero coefficients hide their rows, so
  explicit owned zero publication and visual suppression are distinct from a zero formatter callback.
  These raw controls do not equip the unique boots or establish official-server arithmetic.
- **The HUD** (live 2026-10-25). One buff per type: BuffDefinitions rows 28 `endurance_charge`, 27 `frenzy_charge` and
  29 `power_charge` (`ShowCount`; BuffVisuals 41, 40 and 42 with the BuffVisualOrbs `ChargeEndurance`, `ChargeFrenzy`,
  `ChargePower`), the player as its source. S→C 0x1c1 flags bit1 carries the time left (f32 seconds) and bit2 the
  count (u16, read after bit7's field and before bit3's). The icon at the top left shows the count from bit2 (a raw buff
  with 2 stacks and no charge stats showed "2") and a timer from bit1; without bit1 (minimum charges) it has no timer.
  The orbs circle the character. A change sends 0x1c2 for the old instance and a new 0x1c1. In towns the client draws
  no charge buff (Lioneye's Watch, the server's and a raw one alike); the server keeps counting the charges there.
- **`/charges`** shows each type's count, maximum, minimum, duration and time left; `/charges <type> <n>` sets a count,
  `/charges add <type> [n]` gains as a skill would (refresh, maximum chance) and `/charges clear` empties them. A stat
  set with `/set_stat` counts in the charge effects and limits too.
- **Not modelled.** Held state and gain/expiry for the charge types without a reachable player source here: Spirit
  and Siphoning (their maxima come only from abyss-jewel and Elder/Shaper item counts the server does not model),
  Blitz and Challenger (no 3.29 player source: their stats appear only in monster/mercenary skill stat sets), Brine
  and the rest, beyond the three bounded maxima, the converted charges and the Blood Charges above;
  skills and effects that consume or remove
  charges (Discharge, Flicker Strike's cooldown bypass, Immortal Call, "lose all … when", expiry triggers); gains that
  need stun, freeze, bleed, poison, curses, traps, mines, totems, Vaal souls, other warcries, allies, flask use (the
  utility flask PR #233) or support gems (Power Charge On Critical, Endurance Charge on Melee Stun); Power Siphon's "or
  soon after the hit"; Blood Rage (no self-buff skills); conditional maxima and minima (while affected by an aura, on
  low life, devotion, "count as having maximum", other maximum-equality operators); per-charge stats whose target the
  server does not calculate (ailments, leech, damage taken, damage over time increases, wand-only or attack-only damage,
  mana cost, buff duration, Frenzy's and Flicker Strike's own per-charge attack speed and damage).

## 16. Rage

Generic Rage generation, loss, attack damage, transient ownership and native count/buff packets are implemented.
Skill lists prepared outside the send gate carry their resolved stat inputs, immutable equipped-item identities,
pools, life and skill cosmetics.
The gated snapshot reuses a list only while those inputs and the Rage value still match; otherwise it rebuilds from
current state instead of publishing old skill stats alongside a new character snapshot.
See [26-rage.md](26-rage.md) for inputs, primary evidence, versioned policies and the scoped native controls.
Automated parsed-dispatch coverage does not constitute native verification.

## Open

- Monster attacks still land at half the attack time (the player's use the contact keyframe); "more" multipliers; energy shield (Righteous Fire's ES parts are ignored).
- A shared simulation loop remains open: `TickRate` bounds attack time, while hits use timers.
  Monster decisions use 50 ms of game time.
- Monsters use only their first granted effect; no ranged or spell skills, no monster skill choice.
- A damage model: armour, evasion, resistances, crits; the player's real life from level and stats.
- The death screen's bit4 and trailing fields (Sanctum and other modes).
- Skill record fields +0xc0/+0xc4 still have unproved semantic names.
- Projectiles: their height above the ground and model orientation (§10), collision, the explosion, the real
  skill → projectile link; damage for ground casts.
- The meaning of 0x53's two trailing u16.

### Aura lifecycle: town suppression and death

ActiveAuras records per-character enabled intent by GrantedEffects Id. LoadProgress restores every remembered aura whose gem is available, using fresh connection skill/buff ids. Restoration is not a new cast and does not drop saved auras when their combined reservation exceeds a transient mana pool; displayed reservation is clamped to maximum mana. New manual activations still validate the full reservation.

Campaign towns (WorldAreas.IsTown via WaypointCatalog) instantiate no aura runtime entries: no reservation, bonuses, source/recipient buffs, icons or floor effects. Saved intent stays untouched and restores automatically in hideouts, maps and campaign combat areas. Ground skill requests allow only Move in towns; gem/default attacks on objects and the shared gem cast entry point are refused there. NPC, stash and waypoint interactions remain available.

Skill removal suspends runtime effects without deleting enabled intent. A skill-list refresh resumes remembered effects when their gems become available again. Manual toggles update intent normally. Actual death paths clear saved intent and runtime auras before the death screen; the player tick also handles death cleanup. Resurrection resets the per-death cleanup guard but does not reactivate cleared auras. Switching characters never changes another character's intent. AuraSaveTests cover independent characters, town round trips, all enabled effects despite transient reservation limits, fresh runtime ids and death reset. Live lifecycle verification pending.

#### Aura restoration bootstrap correction

A real town/hideout round trip restored only Vitality despite saved Precision and Clarity. The log marked the body armour inactive because Precision needed 22 dexterity while initial pools still contained the class base 14. CharacterStatSources was checking socketed gems against stale/uninitialized pools while constructing those very pools, then disabling the entire item's global modifiers and all its gems.

Item activation now uses only the equipment's own requirements. Gem requirements remain enforced individually at socketing/casting against completed pools; an unavailable gem no longer suppresses its host armour, unrelated gems or their attributes. This removes the circular initialization gate on aura candidates. AuraEquipmentRestoreTests reproduces the fresh-connection equipment case and restores all 17 supported self auras, including 34 distinct source/recipient buff packets after town suppression. Restoration has no aura-count limit.

### Grace Period on combat-area entry

Each instance connection grants a fresh 60-second `grace_period` buff after creating the player and before
spawning monsters. WorldAreas.IsTown and owned hideout templates are excluded by this server's policy.
BuffDefinitions row 6 is visible, non-recovery, uses BuffVisuals row 8 (`invulnerability.dds`), and has
base_cannot_be_damaged / is_hidden_monster / in_grace_period and minion protection flags. Send ordinary
S→C 0x1c1 with a fresh buff instance and u68 holding IEEE-754 f32 **60.0 seconds** (0x42700000); remove once with 0x1c2.

GracePeriod tracks a per-entry deadline and initial cell using the pause-aware connection game clock.
The first actual grid step (including approach-to-object movement), expiry, skill/attack use or a successful
flask use ends protection. An idle/blocked move order alone does not remove it. Protection is checked at
hit time and ends immediately on movement/deadline even before the recovery loop sends the remove packet.
It cannot reactivate by returning to the starting cell. No state is persisted across connections.

While protected, the server grants immunity to every damage type, suppresses RF self-burning and outgoing
RF pulses, and prevents hostile AI targeting and pending monster hits. Functional summoned-minion combat
is not implemented; cosmetic pets have no incoming/outgoing damage to protect.
GGG's [3.25.0 patch notes](https://www.pathofexile.com/forum/view-thread/3531661) explicitly identify the
Grace Period buff and protection from enemy targeting. The native buff metadata supplies the icon/name/stats;
the requested 60-second duration and town/hideout exclusions are server policy. See also the
[community mechanics reference](https://www.poewiki.net/wiki/Grace_Period) for action cancellation rules.
Automated coverage includes exact expiry, all exclusion combinations, native metadata/packet, one-step
movement and action cancellation. Real-client icon/timing verification is pending.

Grace Period countdown correction: packet +0x68 is copied by 0x141e07220 to constructor parameters,
then by 0x141e06290 to Buff +0x18 (initial duration) and +0x1c (remaining duration).
0x141e10520 subtracts elapsed seconds multiplied by Buff +0x20 (packet +0x6c, rate default 1).
The omitted-duration default 0x14015a670 is float positive infinity (0x7f800000).
An integer 60000 was interpreted as a tiny subnormal float and immediately displayed 0:00.
The regression independently decodes the BE duration bits and checks float 60, rather than comparing two calls
to the same packet writer. Native icon was confirmed; corrected countdown awaits live verification.
