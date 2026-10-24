# 11 — Skills: how the client gets them, the default attack

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
  u8 c, c × (u32, u32)        → manager +0x188 (ActiveSkills row → Vaal souls: FUN_141eea5b0 compares the souls a
                                use needs with the entry's count)
  u8 d, d × (u32, u32, u32)
  ```
  After login the Gateway sends every skill change this way (`SendSkills`): all the player's skills as records and the
  ids that left. It remembers the ids the client has per connection (`ClientSkillIds`, from the player object and every
  update since). Live: a re-socketed gem's skill appears in the character panel again (with 0x40 it did not).

`0x142197b10`:
```
u8 n,  n × skill record (0x142196ce0, through 0x1421981d0: insert sorted by id, replacing an equal id, then slot 7)
u8 m,  m × (u32 ActiveSkills row (0x101-byte rows), u32, u32) → manager +0x188, the Vaal souls (not used)
u32 k, k × (u16 skill id, u32 c, c × (u32, u32, u32, u32))     (not used)
```

Skill record (`0x142196ce0`):
```
u16  skill id                                      → +0x40
GEPL reference (0x1424a0dc0):
  u8                                               → ref +0x10 (0 for us; a stat-set reference with its u8 set uses it + 1)
  u32  GrantedEffectsPerLevel row (0x78-byte rows)
  stat-set reference (0x1424a0990): u8 a, [u8 if a], u32 GrantedEffectStatSetsPerLevel row (0xb8-byte rows)
  one more stat-set reference per entry of the effect's additional stat sets (GrantedEffects row → its list count)
u8   flags: bit0 → u32 parent object id ("Skill parent object did not exist" if unknown) + u16; bit1, bit2 → instance flags
u8   n supports, n × (GEPL reference, u32)
u32, u32, u32 cooldown ms, u8                       → skill +0xc0, +0xc4, +0xc8, +0xcc (0x141ee73d0)
compact n × (compact stat key, compact value)      the skill's own stats
per additional stat set: the same stats list
```
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

| Stat | Value |
|---|---|
| `maximum_life` | BaseMaxLife (38) + 12 × level + strength / 2 |
| `maximum_mana` | BaseMaxMana (34) + 6 × level + intelligence / 2 |
| `accuracy_rating` | (2 × level + 2 × dexterity + flat) × (1 + increased / 100) |
| `physical_damage_reduction_rating` | (flat armour from passives + base body/helmet/gloves/boots) × (1 + increased armour / 100) |
| `evasion_rating` | (15 + flat evasion from passives + base body/helmet/gloves/boots) × (1 + `evasion_rating_+%` / 100) |
| `evasion_rating_+%` | dexterity / 5 + increased evasion passives |
| `physical_damage_reduction_rating_+%` | increased armour passives |
| `maximum_energy_shield_+%` | intelligence / 5 |
| `melee_physical_damage_+%` | strength / 5 |

Accuracy's flat and increased passives are summed from `accuracy_rating` and `accuracy_rating_+%`, the weapon-specific
stats according to the equipped weapon shape (`sword_accuracy_rating`, `wand_accuracy_rating_+%`, ...), and the Accuracy
Mastery (`accuracy_rating_per_level`, `dexterity_accuracy_bonus_grants_accuracy_rating_+3_per_dexterity_instead`,
`gain_accuracy_rating_equal_to_intelligence`). Conditional stats (nearby ally, marked enemy, low life, frenzy charges,
unique/close enemies) are not modelled. The final values go to the client as `accuracy_rating` and, for the Offence
panel's Main Hand / Off Hand Accuracy rows, `main_hand_accuracy_rating` / `off_hand_accuracy_rating`. Its Chance to
Hit rows are sent directly as `main_hand_chance_to_hit_%` / `off_hand_chance_to_hit_%` (and the evasive-monster
variants), against a reference monster of the character's level; `character_sheet_reference_evasion` and
`..._high_dex_evasion` carry that monster's evasion.

Armour and evasion are summed from the passive tree and the **base types** of the equipped body armour, helmet, gloves
and boots ([13](13-items.md) §1): `base_physical_damage_reduction_rating` / `base_evasion_rating` are flat, while
`physical_damage_reduction_rating_+%`, `evasion_rating_+%`, `evasion_and_physical_damage_reduction_rating_+%`
(Cloth and Chain and similar nodes), `global_defences_+%` and the combined armour/evasion increase are additive
increased modifiers. **Iron Reflexes** (`keystone_iron_reflexes`) converts all evasion rating, including the base 15 and
passive/item flat evasion, to armour and removes dexterity's 1% per 5 evasion; the converted part keeps the armour,
evasion and shared defence increases once each. Item mods, quality, local defence rolls, shields and auras are not counted
yet; the base value is the armour row's minimum, which is the client's default when the item's Armour component is zero.
The final `physical_damage_reduction_rating` and `evasion_rating` (with their `_+%`) go to the client. Armour uses
PoB's `armour / (armour + damage × 5) × 100`, rounded and capped at 90% (`DamageModel.ArmourReduction`); evasion uses
the same hit-chance formula as accuracy, so the chance to evade is `100 - AccuracyHitChance(evasion, attacker accuracy)`
(`DamageModel.EvadeChance`). Monster melee rolls the same formula against `DefaultMonsterStats.Accuracy` at the
monster's level and evades before damage; physical monster hits then go through armour and block. `/defences` prints
both against a reference monster of the character's level. Life and mana still ignore passives and items.

The default attack's damage (`Unarmed.HitDamage`) is a physical `DamagePacket`: MinDamage..MaxDamage × (1 +
(`damage_+%` + `physical_damage_+%` + `melee_damage_+%` + `melee_physical_damage_+%`) / 100). The packet goes through
the conversion and gain-as-extra stats, then rolls one number per damage type; the server uses the same packet for its
hits. `/set_stat` overrides any of these. The packet, conversion order and damage-over-time model are in
[20-damage.md](20-damage.md); the panel's older `Unarmed.Damage` tuple is only the physical display range.

**Changing a skill after entering:** S→C **0x2ba** on the player with component index 9 (Actor) and the slot-20 data
`f0 0x40, f1 0, skill list`: the list replaces the skill of the same id, new stats included. Live: the swing sped up at
once. The Gateway now sends the 0x20 update instead (above), which also removes skills and updates the character panel.

## 5. Combat timing (Gateway `Attack`)

1. C→S 0x24 arrives when the player clicks, before the client walks into range. The server walks its position estimate
   toward the monster and stops at the first cell within `MaxAttackDistance` (`WalkEstimator.Approach`).
2. On arrival it starts the swing: the 0x1ab echo (from the stop cell, aimed at the monster). Echoing at the click made
   the client swing on the spot, out of range.
3. Half an attack time later (the contact point, roughly) it rolls the accuracy check and, on a hit, lowers the
   monster's life with 0x1b1. The chance is PoB's `calcs.hitChance`: `accuracy / (accuracy + (evasion / 5)^0.9) × 125`,
   rounded and clamped to 5..100, with the monster's `DefaultMonsterStats.Evasion` at its level. A miss is logged and
   sends no life update; spells do not roll accuracy and always hit.
4. Any later player action (0x25 move, another 0x24) cancels steps 2 and 3 (`ClientState.ActionCount`).
5. Pacing: one swing per attack time. While the button is held the client sends one 0x24 per swing; a swing may
   arrive up to a quarter of an attack early (jitter) and the schedule stays on the client's cadence. When the server
   thought the swing took 833 ms instead of 757 ms it fell behind and dropped every third or fourth swing, which then
   animated without damage.

**Critical strikes.** The server rolls a hit's critical chance after the damage roll: `base chance × (1 + increased
critical strike chance / 100)`, capped at 100%, then multiplies the damage by the final multiplier (150% plus
`base_critical_strike_multiplier_+`), as PoB's `CalcOffence` does. PoB's `unarmedWeaponData[class].CritChance = 0`
and `base_critical_strike_multiplier = 150` are the defaults used here. The base is the equipped main hand's `WeaponTypes.dat` `Critical` permyriad
(830 = 8.30%); unarmed is 0. Fireball uses its `GrantedEffectStatSetsPerLevel` `SpellCritChance` 500 (5.00%). The
unconditional passive stats are summed into `CharacterStats.Pools`: generic `critical_strike_chance_+%` and
`base_critical_strike_multiplier_+` for both attacks and spells, the attack/melee/weapon-shape chance and multiplier
stats for attacks, and `spell_critical_strike_chance_+%` / `base_spell_critical_strike_multiplier_+` for spells. The final
`main_hand_critical_strike_chance` / `_multiplier` and `spell_critical_strike_chance` / `_multiplier` stats go to the
client, so the Offence panel shows them. Timed, charge, enemy-state and similar conditional crit stats are not modelled;
`/crit` prints both final values.

**Impale.** A hit that rolls `impale_on_hit_%_chance` stores 10% of the swing's physical damage on the monster as a
stack: five at most, each lasting five physical hits. The stored amount is taken from the packet's physical **maximum**,
not the rolled damage, so it is steady for a given weapon. Every later physical hit on that monster first takes the
stored total as extra physical (a `DamageRange(stored, stored)` added to the packet before `RollTotal`, so it converts and
mitigates with the hit) and then spends one hit of every stack. Order, in `Gateway.Attack`: add the bonus, `AfterHit()`,
then roll `Apply(physical)` — a new stack applied first would lose one of its five hits on the swing that made it. The
stack count and stored total are read before the hit applies because a killing blow clears them, which is why the log
reports the real stacks on the killing 0x1b1. Only physical is stored. Live 2026-10-20, `/set_stat
attacks_impale_on_hit_%_chance 100` on one monster: `hit for 5, impaled (1 stack(s), 0.5 stored)`, then `impale +0.5 (2
stack(s))`, `impale +1 (3)`, `impale +1.5 (4)`, `impale +2 (5 stack(s), 2.6 stored)` on the killing critical, and the next
monster reopened at one stack.

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
  with a ±20% spread (the spread is assumed). They go in the monster's Stats component and in the skill.
- **`skill_is_non_pathing` (0x4f32) on the skill.** The Melee action's begin (vtable +0x38 `0x141938830`) is just that
  stat: when 0, the action runner (`0x141e8e060`) sends the action through the client's own walk-into-range step,
  which drops a server-driven monster's queued attack in the same frame (traced: created, queued, both checks passed,
  then neither current nor queued). The server walks the monster itself, so the skill is non-pathing. Only on the
  skill: on the monster's Stats component it also stops its Move action from pathing (the monsters stood still).
- **AI** (`RunMonsterAi`, 500 ms): a hostile, chasing monster within reach stops and swings: 0x1ab with its skill, flags
  0x0408 | 0x80, target = the player, sequence 0xffff. At half the attack time the hit lands if the monster lives and
  the player is still within reach + 2: 0x1b1 on the player.
- **Defence.** The monster's accuracy is `DefaultMonsterStats.Accuracy` at its level; the server rolls the player's
  evasion with the same `AccuracyHitChance` formula and sends no life update on a miss. The roll uses PoE's shared
  entropy counter (0–99): each attack adds its hit chance, 100 or more means a hit and subtracts 100, and the counter is
  randomized after 3.33 s without an attack. A hit then goes through the player's armour (`MitigatePhysical`) and
  block. `/defences` prints the reference values.
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
| 0x01 (hardcore) | "Exit" ("Exit to the character selection screen": sends C→S 0x153, not implemented yet) |
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
  - bit 0 clear: the client is **attack-moving** — it walks to the aimed point itself and sends no C→S 0x25 Move for
    that walk. The server walks its estimate after it (`Walk.Order`) and answers 0x1ab from that estimate, like a
    move's echo. A point with a living monster on it within the skill's hit radius is a swing (the client aims at the
    cell, not at the object: Holy Strike on a monster sends 0x25, live 2026-10-25): the server walks into the
    weapon's reach and only then pays and hits. An empty point never swings, so running costs no mana.
  On a monster: C→S 0x24; an attack walks into the weapon's reach first (`Walk.Approach`, the swing is echoed on
  arrival, unlike a spell) and a spell casts from where it stands. An attack with "Always Attack Without Moving" on
  casts in place. On the player itself (Righteous Fire): C→S 0x24 with the player's id as the target; the server
  echoes 0x1ab in place.
  An **instant** skill (stat `skill_is_instant`, below) is not sent this way: C→S **0x191** (8 bytes: u32 target
  object id, u16 skill id; `InstanceProtocol.ReadUseInstantSkill`) uses it without an action or a sequence, so the
  client keeps walking. The server applies the effect and answers with S→C **0x1ce** (`InstanceProtocol.InstantAction`:
  object header, u16 skill id, u16 sequence (0xffff), u16 flags (0x20 → u32 target object), compact target position;
  handler `0x142195880`): the client runs the skill's action without animation and consumes a use of the skill
  (actor vtable +0x198), which starts the skill bar's cooldown. Without it the tooltip showed the cooldown but the bar
  never ran it: the cooldown records had no entries (Frida, live 2026-10-21; not yet checked with 0x1ce).
- **Hit reaction (live 2026-10-25).** A monster that takes a hit plays its rig's **TakeHit** clip with S→C **0x1b2**
  (`InstanceProtocol.AnimationUpdate`, kind 0): object header, u8 kind, u32 clip index (+0x24), u32 float bits (speed,
  +0x28). The index is 0-based into the actor's animation list, which is the rig `.amd`'s animations in file order
  (Sanctum zombie: `flinch_unarmed` = 156 of 249 in `Art/Models/MONSTERS/skeleton/rig.amd`; `flinch_pitchfork` = 151,
  both played the flinch *with its SoundEvent* live). The handler (`0x141e58ef0`) calls `0x141dba530`, which ignores an
  index past the actor's animation count — a wrong index costs nothing. `0x1b1` alone only stores the pool and runs the
  death/revive slots, so a hit without 0x1b2 is silent and still. A killing blow gets no flinch (the client's death
  runs instead). The reaction is gated like a stun: the hit must take at least a tenth of the monster's max life, and a
  monster flinches at most every 0.4 s — otherwise a fast attack or a volley restarts the clip and its sound and keeps
  cutting the monster's own attack and walk animations while the server still moves it.
  `data/monster-takehit.json` (`python -I scripts/re/monster_takehit.py`) holds each MonsterVarieties
  row's index(es): its `.act` maps `TakeHit` to a clip, and the `.ao`'s `AnimationController` names the rig. 26 `.act`
  files map a pool — `flinch_01, flinch_02, flinch_03`, or `flinch_small:max_duration=0.45, flinch_large:min_duration=0.45`
  (the condition picks by the hit's stun duration, which the server does not model) — and the server picks one at random
  per hit; every other variety has a single clip. An `@Name` alias is expanded to the whole list it names, and every
  `extends` parent is followed (not just the first), with "nothing" ending the chain. The client also has the directional
  types TakeHitForward/Backward/Left/Right, but the `.act` files that set them alias them all to `@TakeHit`, so they add
  no variants. 0x262 (a Life "changed by" event with a signed delta) reaches the Life listeners but plays no flinch,
  sound or blood.
- **Release.** A spell leaves at its cast animation's **contact** keyframe, which the client plays stretched to the cast
  time: the Witch's `spell_projectile_wand` in `Art/Models/CHARACTERS/Witch/rig.amd` is 1066 ms long with `contact` at
  599 ms, so Fireball leaves after 56% of the cast. `python -I scripts/re/anim_contacts.py` writes every class's
  contacts to data/animation-contacts.json (`AnimationContacts`); the Gateway waits cast time × that fraction.
- **Cast speed.** `base_cast_speed_+%` and `cast_speed_+%` shorten `virtual_base_spell_cast_time_ms` and
  `hundred_times_casts_per_second` in the skill's stats and the server's release time.
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
  +0xd0 (the cooldown group) comes from the GEPL row's CooldownGroup (+0x30). The server also enforces it: `CastGemSkill` refuses a skill until its
  GEPL Cooldown is over (`ClientState.SkillReadyAt`; Haste and the Purities 1200 ms, Righteous Fire 300 ms).
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
- **Damage** (`SpellDamage.cs`). A spell's hit is, per damage type, its `spell_minimum/maximum_base_{type}_damage`
  × (1 + `damage_+%` + `spell_damage_+%` + `elemental_damage_+%` for fire / cold / lightning + `{type}_damage_+%` +
  `projectile_damage_+%` for `base_is_projectile` skills), from the player's stats (passives, item mods, auras) under
  the skill's own. The server's hits roll between the unrounded minimum and maximum and drop the fraction; the skill's
  tooltip gets `display_hundred_times_damage_per_skill_use` (the unrounded average × 100) and
  `hundred_times_damage_per_second` (× casts per second). Before 2026-10-20 a gem skill carried the default attack's
  DPS with the player's stats. Conversion is applied per type through `DamagePacket`; more multipliers and added
  damage are not modelled.
- **Damage effectiveness** (2026-10-24). An attack gem's hit is the weapon's damage × its `DamageEffectiveness` (the
  GESSPL row, permyriad: Flicker Strike 7730 at level 1, 30890 at 20, 69600 at 40 — the gem's "Deals 77.3% of base
  Attack Damage"), through `SkillCatalog.DamageEffectiveness` and `SpellDamage.WithEffectiveness`. A row of 0 (the
  default attack's) or a negative one (a few skills have them) scales nothing. A **spell**'s own base damage is already
  final: its effectiveness is the added-damage one (Fireball 200%), and the GESSPL `BaseMultiplier` is 0 for every spell,
  so spells are left alone until added damage is modelled.
- **Per-level stats** add to a ConstantStats value of the same stat instead of replacing it (Righteous Fire's radius:
  18 + 0 at level 1).

## 9a. Gem skills without a projectile (first version, 2026-10-23)

Only projectile skills (Fireball) and Righteous Fire dealt damage: every other gem skill logged "no projectile". Now
`AreaSkillHit` gives them one hit, after half the attack time (attacks) or the cast (spells), on every living monster
within the skill's `active_skill_base_area_of_effect_radius` (in cells, else 12, grown by area of effect as Righteous
Fire's: `SkillArea`; halved before 2026-10-23, which the units do not support) of the aimed
point, or of the player when a melee skill is aimed too far. Attacks use the default attack's hit (`WeaponFor`: the
weapon in the hand by its WeaponTypes row with its local physical damage, added physical damage and attack speed mods,
else the class's unarmed attack; the player's stats on top), spells `SkillCatalog.HitDamage` with the player's stats; both convert and crit.
The player object of every area (`SendPlayer`) carries the same weapon attack, so the character panel's DPS includes the
weapon's local mods from the start (until 2026-10-23 it started with the unarmed attack, and only an equipment change,
`SendSkills`, brought the weapon's numbers).
`base_deal_no_damage` skills (Portal) deal none. An attack's hit carries the skill's damage effectiveness (§9). Not
modelled: the skills' shapes (waves, cones, chains, leaps), stun, and the accuracy roll. Being tested live.

**Movement skills** (ActiveSkillType Movement: Leap Slam, Whirling Blades …): the server puts the player at the aimed
cell when the skill starts (`SkillCatalog.IsMovement`); it kept the starting cell before, and a later resync pulled the
player back there (live 2026-10-23).

**Channelled skills** (Cyclone): after the skill's 0x1ab start the client sends **C→S 0x197** (10 bytes: u32 BE x,
u32 BE y, the player's cell) every few frames while the button is held, and **C→S 0x199** (2 bytes) when it is let go
(read from live captures 2026-10-23). The Gateway moves the player there and, once per attack time, runs
`AreaSkillHit` around it at once (`ClientState.ChannelSkill`, `NextChannelHitAt`).

## 10. Projectiles (experimental)

The server creates and moves the projectile. The client's Projectile component (vtable `0x1435a0d10`, 0x20 bytes) does
nothing by itself: its slot 17 reads nothing and almost every other slot is a bare `ret`.

- **Object.** A 0x2b9 of the Projectiles.dat type named after the effect (`Metadata/Projectiles/Fireball`, hash of the
  path; the type register knows it from the table). `Metadata/Projectiles/Projectile` deserializes Positioned,
  BaseEvents, Animated and Projectile, so the blob is Positioned, one Animated byte and the tasks byte
  (`ProjectileObject`), then 0x1a9 (wake zone). The real link between a skill and its projectile is unknown; the
  Gateway matches the Id.
- **Flight.** S→C **0x1ba** to the target cell at the row's `ProjectileSpeed` (world units per second, 1040 → ~96
  cells per second at 250/23 world units per cell). The u32 at +0x28 is a **height**, not padding: the handler
  (`0x141e30c90`) passes it to `0x141e34400`, which keeps the terrain height for 0x7fffffff and otherwise sets
  z = −height × 7.8125. With 0 the fireball sat under the ground and did not seem to move; with 0x7fffffff it flies
  (slowed to 10 cells per second with `Game_PROJECTILE_SPEED` to watch it). Positioned's vtable +0xd8, which the move
  calls when the flag byte has bit 0, is a `ret`; the interpolation is `0x141e4d180`.
- **Rotation.** Positioned rotation (+0x29c) = **atan2(dx, −dy)** in grid cells (the player's own moves traced with
  `trace-move.js`: facing (+15, −18) read 0.668).
- **Hit (live 2026-10-18).** At launch the server walks the path cell by cell and stops it at the first living
  monster within 4 cells (`Game_PROJECTILE_HIT_RADIUS`; the row's CollisionRadius is 2) of where the projectile will
  be, using where that monster will be by then. Ground and monster casts alike: the projectile flies to the hit (else
  to its range), the monster loses the skill's fire damage (0x1b1), a kill is removed 10 s later. A monster that walks
  into the path after launch is not hit.
- **Walls (live 2026-10-18).** The client's walkability provider (walk-map hook `0x1410ddcfd`) returns a second grid
  of the same size at +0x20/+0x28: what stops projectiles. 0 blocks, 1..5 (a distance gradient like the walk map's) lets
  them through. On The Coast seed 1 every walkable cell is open in it, cliffs and rocks are 0 in both, and 1.44 M cells
  the walk map blocks (the sea) are open. `scripts/walkmaps` saves it as `<area>_seed<seed>.projectile.bin`
  (`ProjectileGrid`); the path stops half a cell before the first blocking cell and explodes there. Live: the fireball
  explodes on cliffs and flies over the sea. Doodads and objects with their own colliders (not in the grid) do not stop
  it yet. Grids exist for Lioneye's Watch and The Coast seed 1.
- **Explosion (live 2026-10-18).** Projectiles.dat names the impact as an animation of the projectile's own .ao
  (Fireball: ImpactAnimationIds `fireball_explode` → `point_impact.pet`). No server message found yet plays an
  animation by name, so on a hit the server swaps the projectile's animated object instead: S→C 0x2ba to component 2
  (Animated, in the order Positioned, BaseEvents, Animated, Projectile, Render) with flags bit0 and the path
  `Metadata/Effects/Spells/fireball/explode_new2.ao`, whose default animation `animate` plays the `splode` particles
  and the End sound (reader `0x141e57490`: bit0 path, bit1 a 0x2c-table index, bit2 `0x141e5adb0` attachments; no
  bit3 there). The object is removed 1.5 s later. `Game_PROJECTILE_IMPACT_AO` overrides the path (`none` turns it
  off). `Metadata/Effects/Effect` (Positioned, BaseEvents, Animated, LimitedLifespan) would be the cleaner carrier but
  is not known to be in the client's type register; an unknown type throws in `0x142187480`.
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
  Gateway sends the projectile at terrain + `Game_PROJECTILE_HEIGHT` steps (default 8) in Positioned's f0 0x04 fixed
  height and in 0x1ba. Only Lioneye's Watch has a grid (−48..72), where skills cannot be used.
- **Capturing heights from the client failed** (2026-10-18, four client crashes or hangs, so the walk-map recorder does
  not do it): dumping every cell from Frida's thread at the first lookup worked in town but crashed on The Coast (the
  terrain was still being built); a native listener on `0x141d84b00` that called the hooked function crashed at login;
  delayed dumps hit unreadable tiles (access violations; the lookup is called with several terrain pointers) and hung
  the client. Next: compute the heights offline from the tile files the atlas already reads (the .tdt heights and each
  placed tile's height offset).
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
- **Sign.** World z points down: the client's terrain z is −height × 7.8125 (`0x141e35800`), and Positioned's fixed
  height is i32 z / 7.8125 with f32 z, as the area transitions send it. S→C 0x1ba takes +height. Sending +height in the
  fixed height put the fireball under raised ground; with −height it flies above the ground at every level (belly
  height with the default 8 steps).
- **Flight (live 2026-10-18).** S→C 0x1ba's float is the move's **duration in seconds**, not a speed: the handler
  passes distance / it as the speed, and the step walker sets Positioned +0x23c = that speed and +0x240 = the time per
  cell step (duration / the path's longest axis), advancing +0x244 by the frame time (`0x141e32710`). Sending the
  speed (95.7) made each step take ~1 s, so the fireball crawled (~10 world units/s, sampled with Frida); sending
  distance / speed it flies at ~1000 units/s. The fireball now starts `Game_PROJECTILE_AHEAD` cells (4) in front of the
  caster and, cast at the ground, flies on to `Game_PROJECTILE_RANGE` cells (120, clamped to the area); cast at a
  monster it stops there and hits. `/mob_move` and `Game_MOVE_MODE=move` still pass a speed in that field.
- **Still wrong.** Without a height grid the fireball flies on the terrain. The
  four floats of Positioned's f1 0x04 block (+0x2d8..+0x2e4, zero by default) are not the direction: (dx, dy, 0, 0) put
  it under the ground (`Game_PROJECTILE_DIRECTION=1` sends them).

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
  source object), u16 +0x60 (the source skill), u8 flags (bit0: u8 u32 u8 u16; bit1: u32 +0x68; bit2: u16 stacks,
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
- **Spell Suppression** (`CharacterStats`, `DamageModel`): the passives' `base_spell_suppression_chance_%` is summed into
  `Pools` (capped at 100%, PoB `SuppressionChanceCap`); a successful roll makes a spell hit take 40% less damage
  (`SuppressionEffect` in PoB `src/Modules/Data.lua`). The passives' unconditional
  `base_spell_damage_%_suppressed` is added to the 40% effect (e.g. Inveterate +3, Primal Instinct -10); the effect is
  not hard-capped, matching PoB. Conditional effect stats (`...while_on_full_energy_shield`,
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

## 13. Auras and reservation

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
  the final stats, because the client derives none of them. The skill-bar active border is not reproduced
  yet: 0x25f is an internal UI event, not an instance/world message (both factories fall through to their
  default case), so sending it crashed the 3.29.3b client; the Gateway does not send it. The client's
  setter (`0x140ae4550`, skill id + ActionTypes HASH16) is not called by the buff add/remove events.
- **Implemented effects.** Purity of Elements/Fire/Ice/Lightning add their resistances; Clarity and Vitality
  add their flat regeneration; Precision adds flat accuracy and increased critical strike chance; Haste adds
  attack, cast and movement speed. Generic
  `non_curse_aura_effect_+%` / `aura_effect_+%` and the Reservation mastery's self-aura effect scale the
  values; reservation efficiency comes from `base_mana_reservation_efficiency_+%`,
  `base_reservation_efficiency_+%` and the mana/global efficiency stats. `AuraCatalog.Bonus` and `RawStats`
  apply these to `Pools` and `CharacterStats`.
- **Not implemented yet.** Casting one of these logs that its reservation is known but its effect is not implemented and does not
  reserve mana:
  - **Combat auras:** Anger, Hatred and Wrath need added elemental damage and Hatred's physical-as-extra-cold; Zealotry needs
    more spell damage and spell crit; Pride needs an enemy physical-damage-taken debuff. The damage model has no added
    elemental damage or enemy aura debuffs yet.
  - **Heralds:** Ash, Ice and Thunder need on-kill/overkill/shatter/storm effects; Agony and Purity need minions.
  - **Banners:** Defiance, Dread and War need the planted banner, stages and their pulses.
  - **Curse auras:** Blasphemy-style skills need the curse system.
  - **Determination, Grace and Discipline:** the reservation is known, but armour, evasion as avoidance and energy shield
    are not modelled; they are not in `AuraCatalog` yet.
  - **Partial effects:** Purity of Elements' elemental-ailment immunity is not applied. Purity of Fire/Ice/Lightning
    max-resistance values are read at the gem's level (0 at level 1).
  - **Masteries:** Mana6, Reservation3 (life efficiency), Reservation4 (area) and Reservation5 (self aura effect) are mapped;
    Reservation1 (damage per aura), Reservation2 (max resistances with reserved life and mana) and Reservation6 (aura
    duration) are not. Skill-specific reservation efficiency/effect stats are not modelled.
  - **Systems:** supports, gem levels, party/minion auras, life reservation, and the skill-bar active border (0x25f is an
    internal UI event, not an instance/world message; the client's setter is not reached by the buff events).
- **Dev check.** `/auras` prints the active auras, flat/percent reservation, reserved and unreserved mana,
  reservation efficiency and aura effect. Area changes recreate the player object, so `SendPlayer` puts the
  running auras' buffs back on it.

## 14. Character panel sums

The character panel (CharacterPanelStats.dat, 387 rows) reads mostly virtual stats that the server computes; the client
derives none of them (§4). Passives and item mods give the base form (`base_stun_recovery_+%`) while the Stun Recovery
row reads `stun_recovery_+%`. `PanelStats` (issue #75) lists, for about 150 display stats, what each one sums: the display
stat's own value (Haste gives `cast_speed_+%` itself), its base form and the unconditional combined stats that apply to
it:

| Display | Also counts |
|---|---|
| `avoid_ignite/chill/freeze/shock_%` | `avoid_all_elemental_status_%` (Avoid Elemental Ailments) |
| `self_*_duration_-%` (elemental ailments) | `self_elemental_status_duration_-%` |
| `virtual_ignite/chill/freeze/shock/scorch/frostburn/sap_duration_+%` | `base_elemental_status_ailment_duration_+%`, `base_all_ailment_duration_+%`, the per-element ailment duration, `chill_and_freeze_duration_+%` |
| `virtual_chill/shock/scorch/frostburn/sap_effect_+%` | `non_damaging_ailment_effect_+%`, `cold_` / `lightning_ailment_effect_+%` |
| `chance_to_freeze_%`, `chance_to_shock_%`, the ignite chances | `chance_to_freeze_shock_ignite_%` |
| `reduce_enemy_fire/cold/lightning_resistance_%` | `reduce_enemy_elemental_resistance_%`, `base_penetrate_elemental_resistances_%` |
| `cast_speed_+%` | `attack_and_cast_speed_+%` (also counted in attack speed) |
| `life/mana_leech_from_*_damage_permyriad` | the elemental leech for fire, cold, lightning; life also `horrible_ugly_life_leech_from_*_damage_perhundredthousand` ÷ 10 |
| `virtual_life/mana/energy_shield_leech_speed_+%` | `life_mana_es_leech_speed_+%` |
| `virtual_endurance/frenzy/power_charge_duration_+%` | `charge_duration_+%`, `base_charge_duration_+%` |
| `display_skill_increased_item_rarity/quantity_+%` | `base_item_found_rarity/quantity_+%` |
| `virtual_trap/mine_throwing_speed_+%` | `trap_and_mine_throwing_speed_+%` (mine: `mine_laying_speed_+%`) |
| `minimum/maximum_*_damage_to_return_to_melee_attacker` | `*_damage_to_return_to_melee_attacker` (both ends) |
| `extra_damage_taken_from_crit_+%` | − `base_self_critical_strike_multiplier_-%` |
| `impale_on_hit_%_chance` | `attacks_impale_on_hit_%_chance` (the attack reads this row's name) |
| `intermediary_combined_action_speed_+%` | − `action_speed_-%` |

**Main hand / off hand rows** (`main_hand_…` / `off_hand_…`, `virtual_total_main_hand_…`) add the global stats and that
hand's item's own local stats (`HandLocalStats`: the active item in the first weapon set, else the second):
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

**Rows that combine a pool with a stat** (`PanelStats.FromPools`): `projectile_attack_block_%` = attack block +
`additional_block_chance_against_projectiles_%`, `spell_projectile_block_%` = spell block + `base_spell_projectile_block_%`
(with `spell_block_differs_against_projectiles` 1, which shows the row), each capped at its maximum, and
`chaos_damage_over_time_resistance_%` (and its uncapped form) = chaos resistance +
`additional_chaos_resistance_against_damage_over_time_%`, capped at the chaos maximum. Each is sent only when its stat is set.

Conditional stats (while, if, per charge, against …) are not counted. Flags (`cannot_be_frozen`, `immune_to_ignite`
…) are 1 when any source is set; "Immune to Ignite and Shock" sets both. Only non-zero values are sent; a sum that drops
to zero is cleared with the other stale stats (`PlayerStatsWithCleared`). A stat set with `/set_stat` keeps its value.
`cast_speed_+%` is now the total, so `CastSpeedPct` reads only it. Which combined stats count is from the game's
documented behaviour, not a capture; the signs of the crit-taken and action speed rows are not confirmed. Live
2026-10-20: `/set_stat base_stun_recovery_+% 30`, `avoid_all_elemental_status_% 20` and `base_cannot_be_frozen 1` show
on the Defence tab as Stun Recovery 30%, the four Avoidance rows at 20% and "Cannot be Frozen".

## Open

- Attacks still land at half the attack time: use the attack animations' contact keyframes (data/animation-contacts.json)
  as spells do; "more" multipliers; energy shield (Righteous Fire's ES parts are ignored).
- A 30 Hz server loop: `TickRate` only bounds the attack time; hits use timers and the monster AI runs every 500 ms.
- Monsters use only their first granted effect; no ranged or spell skills, no monster skill choice.
- A damage model: armour, evasion, resistances, crits; the player's real life from level and stats.
- The death screen's bit4 and trailing fields (Sanctum and other modes); exit to character selection (0x153).
- f0 0x20 (skill update), the second and third lists of f0 0x40, supports (linked support gems), gem levels.
- Projectiles: their height above the ground and model orientation (§10), collision, the explosion, the real
  skill → projectile link; damage for ground casts.
- The meaning of 0x53's two trailing u16.
