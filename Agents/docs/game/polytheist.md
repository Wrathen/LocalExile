# Polytheist

Polytheist is the Templar's Phrecia `Guardian` identity (3.29.3b), start node 409.
This page lists every node with its consumer or the engine system it is waiting for.
Native acceptance is still open: it needs the reviewed combined graph installed (see Admission below).

## Graph and builds

No Polytheist node grants passive points, so the 8 ascendancy points buy 8 nodes.
Start 409 (free) links to six small nodes, and they open three rings of seven nodes, each ring holding three notables:

```text
A: 27664 - 22056 Garukhan - 26231 - 32742 Solaris - 21979 - 61900 Abberath - 59443
B: 46465 - 64171 Yugul - 51229 - 43703 Lunaris - 29846 - 1830 Ralakesh - 6605
C: 30769 - 5266 Gruthkul - 322 - 24342 Brine King - 33835 - 23429 Ryslatha - 25478
```

Three legal eight-point builds (one whole ring plus a small node of the next ring) cover every node:

```text
27664 22056 26231 32742 21979 61900 59443 46465
46465 64171 51229 43703 29846 1830 6605 30769
30769 5266 322 24342 33835 23429 25478 27664
```

## Node checklist

Effect text and numbers are the wiki's (from the game's stat descriptions) and the 3.29.3b PassiveSkills values.

| Node | Effect | State |
|---|---|---|
| 409 Guardian | Start node, no stats. | Done |
| 27664 small (ring A) | 5% increased Light Radius, 10% increased Defences. | Done |
| 26231 small (ring A) | Same as 27664. | Done |
| 21979 small (ring A) | Same as 27664. | Done |
| 59443 small (ring A) | Same as 27664. | Done |
| 46465 small (ring B) | Same as 27664. | Done |
| 51229 small (ring B) | Same as 27664. | Done |
| 29846 small (ring B) | Same as 27664. | Done |
| 6605 small (ring B) | Same as 27664. | Done |
| 30769 small (ring C) | Same as 27664. | Done |
| 322 small (ring C) | Same as 27664. | Done |
| 33835 small (ring C) | Same as 27664. | Done |
| 25478 small (ring C) | Same as 27664. | Done |
| 22056 Devout of Garukhan | Evasion, Life and Mana Regeneration while moving; Unaffected by Shock; ground immunity. | Done; ground immunity: engine gap |
| 32742 Devout of Solaris | Nearby Enemies are Blinded; crit against and from Blinded enemies; less damage from them. | Done, except the enemies' reduced crit: no consumer, monsters have no base crit chance here (see below) |
| 61900 Devout of Abberath | Triggers Level 20 Abberath's Fury; Burning Hoofprints; Unaffected by Ignite. | Done, its ignites included; radius and display buff unverified |
| 64171 Devout of Yugul | Hex Reflection; Hexes affect Hexproof enemies; Immune to Reflected Damage; Unaffected by Curses. | Engine gap (all four) |
| 43703 Devout of Lunaris | Nova spells at the target; Spells fire 2 additional Projectiles; 30% increased spell area. | Done |
| 1830 Devout of Ralakesh | +3 to Minimum Frenzy, Power and Endurance Charges; Corrupted Blood cannot be inflicted on you. | Charges done; Corrupted Blood: engine gap |
| 5266 Devout of Gruthkul | Spells deal no damage and are disabled; 8% Life regeneration; +400 Life; enemy attack slow. | Done; swing animation unverified |
| 24342 Devout of the Brine King | +10% to maximum Cold Resistance; Cannot be Frozen; Cannot be Chilled. | Done |
| 23429 Devout of Ryslatha | Enemies you've Hit Recently have 100% reduced Life Regeneration; 30% more Maximum Attack Damage. | Done |

## How the effects are applied

- **Defences** (`global_defences_+%`): the passive stat bonus adds it to the armour, evasion, energy shield and ward
  increases (PoB's "Defences" applies to all four), so the final pools the hit and recharge code read grow with it.
- **Light Radius** (`light_radius_+%`) is the client's own effect: the player stats (0x1b3) carry the summed stat
  and the client sizes the player's light from it. The server has nothing to do with light radius.
  `FUN_1400b97b0` registers `virtual_light_radius_+%` (key 0x1e59) with `FUN_14273e960`, which returns
  `light_radius_+%` (0x4c1) plus `light_radius_+%_while_phased` (0xce7) while `phase_through_objects` (0x76a) is set.
  `FUN_1416574c0` reads 0x1e59 at `0x14165762a` (checked in capstone) and passes it into the light it attaches to
  `hip_jntBnd`.
- **Devout of the Brine King**: `base_maximum_cold_damage_resistance_%` raises the cold maximum the mitigation uses.
  `base_cannot_be_frozen` and `base_cannot_be_chilled` become the panel's `cannot_be_frozen` and `cannot_be_chilled`
  (PanelStats ailment rows). The incoming-ailment consumer (`PlayerElementalAilments`) reads them: a monster's cold hit
  never chills or freezes, and a chill or freeze already running ends at the next player tick (`Reconcile`).
- **Devout of Ralakesh**: `base_minimum_*_charges` set each type's minimum in `Charges.LimitsOf`, at most the maximum
  (PoB: max(charges, min(maximum, minimum))). The character always has three of each, with their usual effects, buffs
  and Charges-tab counts. Gains count from the minimum up to the maximum; when they expire, and at death, the count
  goes back to three.
- **Devout of Garukhan, while moving** (PoB: Condition Moving; the server's walk lasts until `Walk.ArrivesAt`, as for
  the while-moving suppression and charge gains):
  - 100% increased Evasion Rating joins the other evasion increases in one rating, `Pools.EvasionWhileMoving`
    (`CharacterStats.For`). `PlayerEvasion` gives monster attacks (`MonsterSwing`), the sent `evasion_rating` and
    `/defences` that rating during a walk. With Iron Reflexes the converted armour does not get it (a `shortcut:` in the
    code).
  - Regenerate 4% of Life per second (240 per minute) and 100% increased Mana Regeneration Rate:
    `PanelStats.WhileMoving` adds them to `life_regeneration_rate_per_minute_%` and `mana_regeneration_rate_+%` for the
    recovery formulas only. The resource writer (`LifeRegenPerSecond`, `ManaRegenPerSecond`) and the regeneration
    finals (`life/mana_recovery_per_minute`, `total_*_recovery_per_minute_from_regeneration`) read them; the raw stats
    the player stats carry stay as they are.
  - The client derives none of these finals from the raw stats: it steps its pools from `life/mana_recovery_per_minute`
    ([recovery timing](../re/recovery-timing.md)). So the player tick sends the five finals again (one small 0x1b3,
    `SendWhileMovingStats`) when a walk starts or ends, for a character with a while-moving stat.
    The client also has while-moving derivations of its own: `virtual_track_movement` (0x14aa, `FUN_142839330`) is set
    by any of 0x1af7-0x1af9, and `combined_evasion_rating_+%` (`FUN_142726680`) and `combined_mana_regeneration_rate_+%`
    (`FUN_14253a170`) add 0x1af7 / 0x1af8 under `virtual_is_moving` (0x4fb8). The server still sends the finals, as
    for every other stat ([11](../protocol/11-skills.md) §4), and leaves the raw inputs unchanged so nothing can count
    twice.
- **Devout of Garukhan, Unaffected by Shock** (PoB: SelfShockEffect 100% less; wiki: you can still be shocked, it has
  no effect): `PlayerElementalAilments.Reconcile` reads `unaffected_by_shock`, and `DamageTakenFactor` is then 1. The
  shock itself stays, with its duration and `is_shocked`. Monster hits, ignite ticks, the dev enemy-hit commands and
  Righteous Fire's self-burn all read that factor.
  The client derives `virtual_unaffected_by_shock` (0x380e, `FUN_14277f740`), which only stops a shock counting in
  `num_different_ailments_affecting_you` (`FUN_1428c50f0`).
- **Devout of Ryslatha, 30% more Maximum Attack Damage** (`maximum_attack_damage_+%_final_from_ascendancy`):
  `MaximumAttackDamage.Apply` scales the maximum of every final component of an attack hit, after conversion, next to
  Rage's final multiplier: the default attack and its panel rows (`AttackHitConverted`), the native main/off-hand
  physical bounds (`WeaponStats`), attack gems (`SupportedHitFor`), `AttackSkillHit` and ordinary bows. Minimum damage
  and spells are unchanged. PoB applies MaxDamage MORE to each type's own maximum and carries it through conversion,
  which is the same. The wiki says the stat applies to the skill's base and added damage, so it also raises the
  damaging ailments an attack inflicts (PoB builds an ailment's base through the same `calcDamage`). Attacks inflict
  no damaging ailments on the server yet; when they do, their flat ailment packet goes through
  `MaximumAttackDamage.Apply` after conversion, before `IgniteFromHit` (roadmap, skills and combat). The client
  multiplies its three sources
  (`combined_maximum_attack_damage_+%_final` = Π max(0, 1 + x/100) × 100 − 100, rounded: `FUN_1425d25b0`, checked
  in capstone at `0x1425d292e`); only the ascendancy source exists on the server.
- **Devout of Ryslatha, Enemies you've Hit Recently have 100% reduced Life Regeneration rate**
  (`enemy_life_regeneration_rate_+%_for_4_seconds_on_hit` −100): every player hit on a monster (the default attack and
  `ApplyMonsterHitLocked`: gems, areas, projectiles; Righteous Fire's burn is not a hit) records `PlayerHitAt`. The
  regeneration tick (`RegenerateConnectionMonsters`) scales the part of each 0.5 s step within 4 s of that hit by
  max(0, 1 + value / 100), so a regenerating monster gains nothing. The player's death clears every `PlayerHitAt`
  (Recently ends at death). The value is the player's current stat, not one stored on the monster at the hit
  (unverified for a refund within those 4 s). The client does not predict monster regeneration (the monster stats
  carry no `life_recovery_per_minute`), so the 0x1b1 updates alone show it and nothing snaps back.

- **Devout of Gruthkul, Your Spells are disabled** (`your_spells_are_disabled`): every spell is refused before its
  action, cost or cooldown, on every route the warcry and own-aura restrictions use
  ([11](../protocol/11-skills.md) §10j). Spells are the skills the client marks `casting_spell`: auras, Heralds,
  Righteous Fire, Portal and movement spells included; attacks and warcries stay usable (the wiki: spells cannot be cast
  or triggered; auras, heralds, stances and banners are spells, warcries are not). The client greys them itself
  (`skill_disabled_by_stat` = 0x1520 AND `casting_spell`, `FUN_1428cf770`); the skill record also carries it.
  Running spell auras are suspended with their saved intent, as for "Your Aura Skills are disabled", and come back after
  a refund. A Righteous Fire already burning keeps burning the player and deals the enemies nothing. Both are local
  policy: how the real game ends (or keeps) an aura that runs when the restriction starts is unverified.
- **Devout of Gruthkul, Spell Skills deal no Damage**: the existing consumer (#293, `SkillDamageContext`) zeroes every
  spell hit and Righteous Fire's enemy burn, for spells released before the allocation too.
- **Devout of Gruthkul, +400 to maximum Life and Regenerate 8% of Life per second**: `base_maximum_life` joins the life
  pool and `life_regeneration_rate_per_minute_%` (480) the regeneration the resource writer applies.
- **Devout of Gruthkul, Enemies that have Hit you with an Attack Recently have 10% reduced Attack Speed**
  (`enemies_that_hit_you_with_attack_recently_attack_speed_+%` -10): every monster hit is an attack here (its basic
  contact). A hit that is not evaded or blocked records `HitPlayerAt` on the monster; for 4 s the value joins the
  monster's own increased attack speed (attack time × (1 + inc) / (1 + inc − 10%), `MonsterModEffects.AttackSeconds`),
  read as the player's current stat (resolved with the stat pools, `Pools.AttackerSlowPct`, so a swing builds no
  stats). The AI's busy time, the contact at half the attack time and the 0x1ab log use it
  (`MonsterSwingSeconds`); the player's death ends it. The monster's attack stats go to its Stats component again when
  the slow starts and ends (`SendAttackerSlows`); whether the client's swing animation follows them is unverified
  ([11](../protocol/11-skills.md) §6). The client has no literal read of 0x187f, so the effect is the server's.
- **Devout of Solaris, Nearby Enemies are Blinded** (`local_display_nearby_enemies_are_blinded` is only the tooltip;
  the effect is the node's PassiveSkillBuffs entry, BuffTemplates 34 `SaboteurBlind`): `PassiveBuffAuras` reads the
  template, `generic_buff_aura` (BuffDefinitions 1521, invisible, no stats, cancelled on death) with AuraRadius 35.
  The radius is in cells like every other radius; the wiki's distance table gives this template 3.5 m. Every player tick
  (250 ms, not while paused) `TickPassiveAuras` blinds each living hostile monster within 35 cells of the living player
  and shows it with the `blinded` buff (179) on the monster, as the Stibnite smoke cloud does. A monster that leaves the
  radius, the player's death and a refund take the buff off at the next tick; a new area is a new connection. The
  invisible aura buff on the player is not sent: it has no stats and no visual, so the client has nothing to show or
  derive. The Saboteur's Born in the Shadows uses the same template and gets the same aura.
- **Blind on monsters** (`Program.Blinded`): a monster is Blinded by the player while it stands in their smoke cloud or
  in the passive aura. Each source keeps its own set and its own buff, so one ending never un-blinds the other. Blind
  is 20% less accuracy (its hits on the player) and 20% less evasion (the player's default attack and projectile
  attacks; PoB's Blind). Both sources count as "Enemies Blinded by you".
- **Devout of Solaris, 100% increased Critical Strike Chance against Blinded Enemies**
  (`critical_strike_chance_+%_vs_blinded_enemies`; PoB: an increase with ActorCondition enemy Blinded): against a
  Blinded target the chance is computed again from the base with the extra increase in the increased sum, never the
  final chance scaled (`CriticalChanceOn`). The default attack (`AttackCriticalChanceVsBlinded`, from the main hand's
  base and the pooled attack increases), the area sweep (`SupportedCriticalChanceVsBlinded`, once per sweep) and every
  projectile (captured at release as `ProjectileOffense.VsBlindedCriticalChance`, ordinary bows included; the target is
  checked at contact) use it. A 5% base goes to 10% with no other increase. The client reads 0xe8d only in stat
  descriptions, so the effect is the server's.
- **Devout of Solaris, 10% reduced Damage taken from Blinded Enemies** (`damage_taken_+%_from_blinded_enemies`; PoB:
  DamageTaken INC from a Blinded enemy): a Blinded monster's hit has the value added to the hit's other increased damage
  taken, the shock (`DamageTakenFactor(now, increasedPct)`): unshocked ×0.9, 50% shocked ×1.4. Unaffected by Shock still
  removes only the shock's part. The ignite that monster's hit puts on the player keeps its source, so the burning
  ignite's ticks add the value too while that monster is Blinded (`PlayerElementalAilments.Advance`; PoB's DamageTaken
  covers damage over time as well).
- **Devout of Solaris, Enemies Blinded by you have 100% reduced Critical Strike Chance**
  (`enemies_you_blind_have_critical_strike_chance_+%`): no monster here has a chance it could reduce. Monster attacks
  have no base critical chance in the server ([14](../protocol/14-monster-packs.md) §5); they crit only through
  `always_crit` (of Deadliness), and that is an override. The client's
  `main_hand_effective_total_chance_permyriad_for_hit_to_be_critical` (0x5ac7, `FUN_14275c050`; the off-hand and spell
  ones `FUN_14275c6a0` and `FUN_14275ccf0` alike) returns 0 for `virtual_global_cannot_crit`, then 10000 for
  `virtual_always_crit` (0x2549, derived from `always_crit` by `FUN_14275ba00`) before it reads any chance or increase
  (checked in capstone: `0x14275c24c` jumps to `0x14275c694: mov eax, 0x2710`). PoB agrees: an always-crit enemy
  ignores increases (CalcDefence's `AlwaysCrit`). A blinded Deadliness monster therefore still crits. Whether
  ordinary 3.29 monster attacks carry a base critical chance is unverified: of the 9 901 monster varieties with a
  granted effect, none has a first effect whose stat sets (GrantedEffectStatSetsPerLevel) give an `AttackCritChance`
  (`Melee`, `MeleeAtAnimationSpeed` and the like: 0); only 1 123 have a spell projectile with a `SpellCritChance` of
  5-6%, and the server makes every monster hit an attack. PoB assumes 5% for an unconfigured enemy. If a base chance is
  added, this stat must join its increases; until then the stat has no consumer. The client never reads 0x3099.
- **Devout of Lunaris, Nova Spells Cast at the targeted location instead of around you**
  (`nova_spells_cast_at_target_location`): a self-targeted spell (ActiveSkillTargetTypes 4 or 10: Ice Nova, Discharge,
  Shock Nova) is centred on the caster, and with the stat a self-targeted Nova spell (ActiveSkillType Nova) is centred
  on the aimed point (`SpellCentre` in `AreaSkillHit`, the client's rule in `FUN_14193e290`). Totem, mine and trap
  skills and Novas without a self target (Shockwave Totem, Stormblast Mine, Dark Pact) keep the aimed point either
  way. The client derives
  `virtual_self_targeted_skills_target_location_instead` from this stat, `casting_spell` and `skill_is_nova_skill`
  (`FUN_142896650`), but network stats run no calculators ([04](../protocol/04-player-entity.md) §4.1a), so the Gateway
  publishes `skill_is_nova_skill` and the derived flag itself in the Nova's record when all three hold. Without the flag
  the client sent Ice Nova as a cast on the player (live 2026-10-30); with it, a cast at the cursor, which the server
  centres there ([11](../protocol/11-skills.md) §9a).
- **Devout of Lunaris, Spells fire 2 additional Projectiles** (`spells_number_of_additional_projectiles`): the projectile
  profile adds them to spells only (`GatewayProjectileModifiers`), for the release and the skill record's
  `total_number_of_projectiles_to_fire` alike.
- **Devout of Abberath, Triggers Level 20 Abberath's Fury** (the node's `GrantedEffectsPerLevel` 45556:
  `RepeatingShockwave` level 20; `display_abberaths_hooves_skill_level` is only the tooltip): a passive-granted skill
  ([11](../protocol/11-skills.md) §12, `PassiveGrantedSkills`). Each footstep while the player walks explodes at the
  player's feet: two per 800 ms of the Templar's run animation (the first 233 ms after the walk starts), more often
  with movement speed and less when chilled (the wiki: movement speed raises the frequency; cast speed does nothing).
  A movement skill's blink is no walk, so it triggers nothing, as the wiki says. The wiki's one exception, Shield
  Charge (the player still walks, faster), is a known gap: Shield Charge is a Movement skill here and blinks too, so it
  triggers nothing until it gets a walk-like charge. Nothing triggers in town, in the grace
  period, or while the player is dead, frozen or stunned. The hit is the level-20 row's 300-450 fire
  (GrantedEffectStatSetsPerLevel 45353) with the player's increases, added damage and conversion (`GrantedSpellHit`),
  a 5% base spell critical chance (with the Solaris increase against Blinded targets) and the spell multiplier, rolled
  per monster as an area spell's sweep. 3.27.0g's "100% more damage" is in that row (level 7 has 50-75; the wiki says
  about 6 times), not a separate stat. The radius is 7 cells grown by spell area (Lunaris's 30% included): no stat,
  PoB or poedb gives one, so it is the explosion mesh's size, unverified. The hit log reads
  "RepeatingShockwave (footstep)". The wiki's limit of about +160% movement speed is not modelled (its mechanism is
  unknown).
  Devout of Gruthkul disables it (the wiki: taking both disables Abberath's Fury): `ActorSkillDisabledCore` refuses
  the spell, and Spell Skills deal no Damage would zero it.
- **Abberath's Fury's 25% chance to Ignite** (`base_chance_to_ignite_%` in its level-20 row): a hit that lands ignites
  the monster when it survives, with that chance (plus the player's `chance_to_ignite_%` and
  `chance_to_freeze_shock_ignite_%`) or always on a critical strike (`IgniteFromHit`, the wiki's Ignite). The ignite
  burns 90% of the hit's flat fire damage per second (300-450 with added fire damage and conversion, before any
  increase) for 4 s. Only the increases the wiki lists for ignite count (damage, damage over time, elemental, fire,
  burning, ignite and ailment damage; never spell or area damage), with the Frenzy charges' more damage and the damage
  over time multipliers, +50% of them from a critical strike (the player's inherent bonus for ailments from critical
  strikes). Increased ignite duration lengthens it, and the monster's `base_self_ignite_duration_-%` (Fire and Ignite
  Resistant: 80%) shortens it. The "deal Damage faster" modifiers the wiki lists for ignite (`faster_burn_%`,
  `damaging_ailments_deal_damage_+%_faster`: Searing Heat, Wasting Affliction, Blowback) multiply its damage per second
  and divide its duration by the same factor. Several ignites can be on one monster; only the strongest burns
  (`MonsterIgnites`). The player tick burns them (`BurnIgnited`, every 250 ms, the same burn as projectile ignites) through the monster's fire resistance
  and damage taken, as Righteous Fire's burn, and kills with it; they burn on while the player is dead (unverified: no
  source found), with no experience then, and a dead Hardcore character's kills make no progress (`MonsterKilled`'s
  `HardcoreProgressBlocked` gate, as a projectile's). The monster shows the `ignited` buff (BuffDefinitions 0) from
  the hit until the last one ends or it dies, its timer running to that end.
  The ignite's flat fire is rolled apart from the hit's damage (the game takes the hit's own roll; the spread is the
  same, a `shortcut:` in the code). Projectile hits add to the same ignites; the default
  attack and melee or area gems do not ignite yet ([20](../protocol/20-damage.md) §3).
- **Devout of Abberath, Burning Hoofprints** (`goat_footprints_from_item`) is the client's: the player stats carry it,
  and `FUN_141ebd630` turns it into the goat footprint variation that `FUN_14166a840` draws on each footstep event
  ([11](../protocol/11-skills.md) §12). The server sends nothing else.
- **Devout of Abberath, Unaffected by Ignite** (PoB: SelfIgniteEffect 100% less): `PlayerElementalAilments.Reconcile`
  reads `unaffected_by_ignite`, and `Advance` then deals no ignite damage. The ignite itself stays, with its duration
  and `is_ignited`. The client derives `virtual_unaffected_by_ignite` (0x380d: `FUN_1400c0320` registers it with
  `FUN_14277a080`, from `unaffected_by_ignite` 0x1de6 and other sources); only `num_different_ailments_affecting_you`
  (`FUN_1428c50f0`) reads it.
- **Devout of Lunaris, Spell Skills have 30% increased Area of Effect** (`spell_area_of_effect_+%`): `SkillArea.Increased`
  adds it for spells only, so a spell's radius grows by √1.3 (PoB calcRadius) and attacks keep theirs.

## Engine gaps

These effects are not faked. Each waits for an engine system that does not exist yet:

- **Hex Reflection, Hexes affect Hexproof enemies, Unaffected by Curses** (Yugul): there are no curses or hexes.
  Monsters never curse the player, and no curse the player casts is applied to monsters.
  `MonsterModCatalog.Vacuous` lists `hexproof` and `reflect_hexes_chance_%` for this reason.
  The client derives `virtual_unaffected_by_curses` (0x4030: `FUN_1400e53c0` registers it, `FUN_1428d1880` sets it
  from `unaffected_by_curses` 0x1d7d); no other literal read of 0x4030 was found, so it is display only.
- **Immune to Reflected Damage** (Yugul): nothing reflects damage. Monster reflect mods never roll (`SupportOf` None),
  and the map device applies no map modifiers. The client has no literal read of `immune_to_reflected_damage`
  (0x45ac).
- **Immune to Burning Ground, Shocked Ground and Chilled Ground** (Garukhan): nothing creates hazardous ground. No
  monster, monster mod, area or map makes burning, shocked or chilled ground, and the player's own ground skills
  (Frostblink, the Aquamarine flask) do not create it either.
- **Corrupted Blood cannot be inflicted on you** (Ralakesh): nothing inflicts Corrupted Blood.
  The only related code maps a flask's bleed-and-Corrupted-Blood immunity to `immune_to_bleeding`.
- **Shield Charge sets off Abberath's Fury** (Abberath; the wiki's one movement-skill exception): Shield Charge is a
  Movement skill here, so the player blinks to the aimed cell and takes no footsteps.
  It needs a charge that moves like a walk (`FootstepsDue` then advances during it).

## Admission

The default Phrecia gate stays closed.
`Game_VERIFIED_POLYTHEIST=1` admits only this identity, after the same read-only installed-graph check as Gambler
(`VerifiedGamblerGraph`, ordinary `82D82CD3…` and combined `F2766B5A…`).
A client with the original Phrecia graph is refused before any save, and the refusal points to this page.
Nothing installs or patches the archive.

With the graph installed, set `Game_VERIFIED_POLYTHEIST=1` for the Gateway, start through `scripts/dev/live.ps1` and,
on a Templar, run:

```text
/ascendancy phrecia Polytheist
/ascendancy_points all
/add_passive 30769 5266 322 24342 33835 23429 25478 27664
/ascendancy
```

What to check with that build (ring C):

- The Defence tab shows +10% maximum Cold Resistance and "Cannot be Frozen" / "Cannot be Chilled".
  A cold monster's critical hit never chills or freezes.
- The player's light is larger.
- Armour, evasion and energy shield increases are 50% higher than before the allocation.
- Ryslatha: the Offence tab's maximum attack damage is 30% higher and the minimum unchanged (the panel may need a
  relog to refresh, [11](../protocol/11-skills.md) §4). A monster with regeneration (`/mob_mods <id> of_the_Hydra`)
  regains no life for 4 s after each hit, then regenerates again.
- Gruthkul, with Fireball, Clarity and an attack gem socketed (Clarity on before the allocation): Fireball and Clarity
  are greyed and refused ("spells are disabled" in the Gateway log, no mana spent), Clarity's buff and reservation end,
  and the attack still works. Life is 400 higher and regenerates 8% per second. A monster that hits the player logs a
  longer "attack time" on its next swings for 4 s ("attack time ... (enemies that hit you recently)"); watch whether its
  swing animation slows too. A refund brings Clarity back.

The ring B build (`46465 64171 51229 43703 29846 1830 6605 30769`) shows three of each charge at once.
With Lunaris, Fireball fires three projectiles, Ice Nova goes off at the cursor (it stays around the player once
refunded; check which point the 0x01ab log line carries), and spell areas are larger.

The ring A build (`27664 22056 26231 32742 21979 61900 59443 46465`) has Garukhan and Solaris:

- While walking, the Defence tab's evasion and its Life and Mana Regeneration per second rise (4% of maximum life,
  double the mana rate), and fall back when the walk ends; the orbs fill faster only while walking.
- A monster made with `/mob_mods <id> of_Deadliness of_Electricity` crits and shocks on every hit. With Garukhan the
  Gateway log still shows the shock ("elemental ailments changed"), but the next hits take the same damage as
  unshocked ones; refunded, they take up to 50% more.
- Solaris: monsters within 3.5 m show the blind effect ("blinded by the passive aura" in the Gateway log, "out of the
  blind aura" when they leave or the player dies). Their hits miss more often and deal 10% less (compare the 0x01b1
  damage with a monster outside the radius); the of_Deadliness monster still crits while blinded. The player's
  critical strikes ("(critical)" in the hit log) come more often against blinded monsters (twice as often with no
  other increased critical chance).
- Abberath: walk and watch the fire explode at the player's feet about twice a second, with the burning hoofprints
  behind; a monster within about 0.7 m of a step takes the hit ("hit by RepeatingShockwave (footstep)" in the Gateway
  log). Standing, casting or using a movement skill sets off nothing. Check the explosion art and size against the
  real game, and whether the real game shows the Abberath's Hooves buff icon (it is not sent). About one hit in four
  ignites the monster: it shows the burning effect ("ignited by RepeatingShockwave" in the Gateway log when it starts
  burning) and loses life for 4 s between the hits; a Fire and Ignite Resistant monster (`/mob_mods <id> MonsterModResistantToFire`) burns for
  0.8 s and takes a quarter. A monster's critical fire hit still ignites the player ("elemental ailments changed"),
  but life does not drop from it.
- With Gruthkul (on another Polytheist Templar, `/add_passive 59443 61900 30769 5266`) nothing explodes; refund 5266
  and 30769 in the ascendancy tree and the explosions come back.

Coverage: `AscendancyGatewayIntegrationTests.Polytheist` and `VerifiedGamblerGraphTests`.
