# Surfcaster

Surfcaster is the Shadow's Phrecia Saboteur identity (`Saboteur`, ascendancy row 17, 3.29.3b), start node 18635.
No node grants passive points, so a build spends the class's eight ascendancy points.
Every effect has a gameplay consumer on the server, except the two fishing lines, which have no gameplay in 3.29.3b.
Native acceptance is still open and needs the reviewed combined graph installed (see Admission below).

## Node checklist

| Node | Effect (wiki text) | Status |
|---|---|---|
| 37082 Baited Breath | All hits are Critical Strikes while holding a Fishing Rod | Works: every player hit crits without a roll. |
| | You can catch Corrupted Fish | No effect in 3.29.3b: the game has no fishing. |
| 64012 Screaming Reels | 60% more Cast Speed while holding a Fishing Rod | Works: server cast wait and the spell's sent `cast_speed_+%`, which should drive the native cast clip (not checked live). The player-level `cast_speed_+%` leaves the multiplier out. |
| 61696 Sea Legs | +40% to Cold and Lightning Resistances; Evasion Rating and Critical Strike Chance increased by overcapped Cold / Lightning Resistance | Works (`ResistanceOvercap`). |
| 60505 Stormy Seas | 100% of Lightning Damage Converted to Cold Damage | Works on every hit path (`DamageStats.Conversion`). |
| | Your Cold Damage can Shock | Works: a critical strike with cold damage shocks by that damage (`cold_damage_can_shock`). |
| 52264 Brine Bound | Cannot be Frozen | Works: `cannot_be_frozen` blocks incoming freeze. |
| | 50% chance to Freeze Enemies for 1 second when they Hit you; Enemies you Freeze remain Frozen for at least 1 second | Works: a monster hit that lands (blocked too) may freeze it for 1 s, and every freeze you inflict past the 0.3 s threshold lasts at least 1 s. |
| 6252 Glacial Wave | Enemies Chilled by your Hits have Damage taken increased by Chill Effect; lessen their Damage dealt by half of Chill Effect | Works: on your hits and Righteous Fire, and on the monster's swings and the ignite they put on you. Not modelled: the same chill reflected onto you by a Mirrored monster. |
| 61246 Within the Tempest | Shocks you inflict spread to other Enemies within 1.5 metres; Enemies you kill are Shocked | Works: a corpse keeps the killing hit's shock and gets the kill's own (15%, 2 s), and your shocks, the corpse's included, spread 15 cells, grown by area of effect, to living enemies. |
| 52944 Changing Seasons | The Effect of Chill on you is reversed; All Damage Taken from Hits can Chill you | Works: any monster hit chills by its total damage, and the chill raises action speed instead; freeze is unchanged. The chilled icon shows in your buff bar (live). |
| 37553 Ghost of the Deep | Touched by a Tormented Illegal Fisherman: 23% increased Movement, Attack and Cast Speed; nearby Allies deal 23% increased Damage | Works: the Torment Touched buff gives the player all four 23% bonuses; the buff is sent for its icon (not checked live). No allies or tormented spirits exist to use the rest. |
| 48926, 53922, 41643, 34767, 33031, 36782, 37534, 56184, 42290 | 4% increased Cast Speed | Works: `base_cast_speed_+%` reaches `cast_speed_+%`. |
| | 5% chance to Cause Monsters to Flee | Works: a hit may make a normal or magic monster run away (`MonsterFlee`); the distance is not verified. |
| | 10% increased Rarity of Fish Caught | No effect in 3.29.3b: the game has no fishing. |

Effect text and numbers are the wiki's (from the game's stat descriptions) and the 3.29.3b PassiveSkills values.

Three legal eight-point builds cover all 18 non-start nodes:

```text
48926 64012 33031 37082 36782 37553 42290 61696
41643 60505 34767 61246 53922 6252 37534 52944
41643 60505 56184 52264 36782 37553 42290 61696
```

## How the effects are applied

- **Holding a Fishing Rod** means the active weapon set's main hand holds a `FishingRod` item class whose requirements
  are met (`HoldingFishingRod`; PoB's `UsingFishing` condition from `weaponData1`).
  The inactive set and an unusable rod do not count.
  The client's calculators test `main_hand_weapon_type` 15 for both rod stats, so 15 is the rod in that enum (inferred
  from those two reads).
- **Baited Breath.** `RollCriticalStrike` returns a critical strike without drawing while the condition holds and
  `always_crit_while_holding_fishing_rod` is set.
  That covers the default attack, area skills and projectiles, including hits with a 0% base chance.
  Any cannot-crit stat still wins (wiki).
  As the wiki describes, `main_hand_critical_strike_chance`, the pooled `spell_critical_strike_chance` and each
  skill's chance show 100% where the base chance is not 0, and stay 0 for a 0% base.
  Native: `virtual_always_crit` (key 0x2549, calculator `0x14275ba00`) is `always_crit`, Pressure Points, this stat with
  `main_hand_weapon_type` 15, or the non-channelling attack flag.
  `scripts/re/statkeys.py` places that read in `~0x14275b560`, but that function is the `virtual_global_cannot_crit`
  (0xb34) calculator before it.
  Bow shots resolve at projectile contact like other projectiles, so they reach `RollCriticalStrike` and the ailment
  consumers too; Baited Breath never applies to them only because a bow and a rod cannot be held together.
- **Screaming Reels.** `FinalCastSpeedPct` applies the 60% more to the summed increases, the skill's own
  `base_cast_speed_+%` included, the way the client's `cast_speed_+%` calculator `0x142709610` does it:
  `round((increased + 100) × (1 + more / 100) − 100)`, at least −99.
  Its combined final (`combined_cast_speed_+%_final`, `0x142708190`) reads key 0x529f only for weapon type 15.
  The server cast wait (`CastMs`) and the skill record use that total.
  The skill record now carries the total as its `cast_speed_+%`, because the native action divides the base cast time
  by that stat (`0x1419e7500`).
  The client's stat getter `0x1424fc1e0` is a plain lookup and runs no calculator, so the client uses the value the
  server sends.
  Example: 16% increased with a rod is 86%, so a 750 ms Fireball takes 403 ms.
  Sending the total also adds a support's own cast speed (Faster Casting) to the record's `cast_speed_+%`, which the
  native clip had left out before.
- **Corrupted fish and fish rarity.** Fishing is not a gameplay feature in 3.29.3b (wiki: no confirmed fishing
  gameplay); there is no fishing skill or table, so `can_catch_corrupted_fish` and `fish_rarity_+%` have no consumer.
  No client code reads key 0x8e6 as a stat. Its two uses (`0x14017ae7c`, `0x140183a45`) pass 0x8e6 to the
  ClientStrings getter `0x1421fdc40` for the whisper "From"/"To" labels.
- **Ghost of the Deep.** Its PassiveSkills row lists BuffTemplates 671 (`CustomAscendancyIllegalFisherman`,
  AuraRadius 0), which puts BuffDefinitions 2550 (`Torment Touched`, BuffVisuals 1875, CancelOnDeath false) on the
  player with 23 each to `base_movement_velocity_+%`, `attack_speed_+%`, `base_cast_speed_+%` and `damage_+%`, and 1
  to `cant_touch_this` and `monster_is_touched_by_ghost`.
  `PassiveBuffAuras` reads that chain from the tables and adds the values to the allocated passives' sum, so the
  existing consumers take them like any passive: movement speed (server walk and the sent `movement_velocity_+%`),
  attack speed, cast speed and damage.
  The wiki's infocard and its 3.27.0g history give the player all four bonuses.
  The stat text's "nearby allies" line has no aura behind it (the template's radius is 0), and the engine has no allies.
  The two flags have no consumer: there are no tormented spirits to touch the player again, and the client reads
  `monster_is_touched_by_ghost` only in a monster drop-quantity calculator.
  Fairgraves' Tricorne's interaction (wiki) does not apply.
  The buff is published with S→C 0x1c1 at area entry and after an allocation, and removed with 0x1c2 on refund; it has
  no duration and stays through death.
  Whether the native client keeps it after a death is not checked live.
- **Changing Seasons.** `all_damage_taken_can_chill` lets a monster hit of any type chill the player.
  The magnitude uses the hit's total damage taken, all types together, against the usual maximum-Life threshold
  (wiki Changing Seasons: hits chill "with any damage type").
  Freeze and shock still need their own cold or lightning damage.
  `chill_effect_is_reversed` turns the chill into increased action speed by its effect (PoB `CalcPerform`: ActionSpeed
  INC +effect), capped at 30% like any chill, and still scaled by `chill_effectiveness_on_self_+%`.
  A freeze still stops the player (wiki: other action speed modifiers such as Freeze are unaffected).
  `not_affected_by_action_speed_modifiers` cancels the speed-up as well.
  The server walk, attack and cast timers and the sent action-speed stats follow `PlayerElementalAilments.ActionFactor`:
  a 30% chill gives 1.3, with `virtual_action_speed_+%` +30, `action_speed_-%` −30 and the intermediary +30.
  The client's `virtual_chill_effect_is_reversed` (0x1e46) is only a calculator (`0x14277bcd0`: key 0x1e44, or 0x1e45
  on chilled ground), network stats run none ([04](../protocol/04-player-entity.md) §4.1a), and its other uses are
  ClientStrings lookups, so the sent action speed carries the reversal.
  Chilled ground is not modelled, so `chill_effect_is_reversed_when_on_chilled_ground` has no source.
- **Stormy Seas.** The conversion turns the hit's lightning into cold, so without the second line a rod's always-crit
  hit freezes and chills but cannot shock. `cold_damage_can_shock` lets the shock use the cold damage, on the general
  monster ailment engine ([20](../protocol/20-damage.md#outgoing-chill-freeze-and-shock-on-monsters)): threshold the
  monster's maximum life, at most 50% increased damage taken for 2 s.
  The client's `virtual_cold_damage_can_shock` calculator (`0x1426f2660`) is its only reader, so nothing is sent for it.
- **Glacial Wave.** A chill from your hit keeps the node's two stats as they were when it landed. While it lasts,
  the monster takes increased damage by its strongest chill, added to shock and its mods' damage taken, on your hits and
  Righteous Fire's burn, and its swings deal less by half of it (PoB `CalcPerform`: DamageTaken INC, Damage MORE
  −chill / 2): a 30% chill gives 130 from 100 and 85 from 100.
  Less damage dealt covers all of the monster's damage, so the ignite its critical strike puts on you starts from the
  lessened hit as well.
  The wiki counts the strongest chill from any source once yours is on it; here every chill on a monster comes from
  your hits. The same chill reflected onto you (Mirrored) carries only its magnitude, so the wiki's reflected
  Glacial Wave (and its reversal by Changing Seasons) is not modelled.
- **Brine Bound.** `chance_to_freeze_enemies_for_1_second_when_hit_%` rolls once for each monster hit that lands in
  `MonsterSwing`, blocked hits included (wiki Blocking: blocking does not prevent the defender's when-hit effects) and
  evaded ones never, and freezes that monster for 1 s.
  `freeze_minimum_duration_X_ms` lifts every freeze you inflict that passed the 0.3 s threshold to at least 1 s,
  whatever modifies its duration (wiki Brine Bound): a 0.4 s freeze lasts 1 s, a 0.2 s one is still discarded.
  Whether freeze duration modifiers lengthen the fixed 1 s freeze is not verified; they are applied, as to any freeze.
  `base_cannot_be_frozen` reaches the panel's `cannot_be_frozen`, which blocks every incoming freeze.
- **Within the Tempest.** `enemy_shock_on_kill` shocks every monster you kill, by a hit or Righteous Fire: 15% (the
  wiki's base for shocks without damage) for 2 s, through your shock effect and duration modifiers.
  A killing hit also leaves its own chill, freeze and shock on the corpse, as on a living monster (wiki Ailment: a
  killing hit that inflicts an elemental ailment counts as killing an affected enemy; wiki Within the Tempest: the
  spread includes shocks inflicted on enemies you kill).
  The stronger shock applies to the corpse, and each of them spreads as far as its own radius reaches.
  A non-unique monster that a hit kills while frozen, the killing hit's freeze included, shatters and leaves no corpse
  (wiki Shatter), so it spreads nothing and gets no kill shock; the server ends its ailments, but the client still
  shows the corpse, because the shatter packet is not known.
  With Stormy Seas all the damage is cold.
  Without shock effect or freeze duration modifiers, a cold hit shocks above 15% only from 5.8% of the threshold,
  where it also freezes for 0.35 s, so a killing hit of that size shatters a non-unique monster.
  Then the killing hit's stronger shock spreads only from unique monsters, from monsters that cannot be frozen, and
  from lightning kills without Stormy Seas.
  Those modifiers change this: with 20% increased shock effect a hit of 3.7% shocks 16%, and its 0.22 s freeze is
  discarded, so the monster does not shatter and spreads the 16%.
  `shock_proliferation_radius_is_at_least_15` gives every shock you inflict, the kill's included, a proliferation
  radius of at least 15 cells: the client's `virtual_shock_proliferation_radius` calculator (`0x142828340`) takes the
  largest of five radius stats and 15 with this flag, and does not scale it.
  The server grows that radius by area of effect like a skill's area (wiki Elemental proliferation: modifiers to area
  of effect affect it): a hit's shock by the hitting skill's area stats over yours, a kill's shock by yours, so 100%
  increased area of effect spreads 21 cells.
  Whether the game scales this node's fixed 1.5 m that way is not verified beyond the wiki's general rule.
  On every AI tick a living enemy has the strongest shock whose own radius reaches it from the shocked monster, alive
  or a corpse, until that shock ends, like an aura.
  So a weaker shock with a wider radius (from a skill with more area) still reaches the enemies beyond a stronger one's.
  The copy keeps the source's magnitude, the source's own `shocked_effect_on_self_+%` included; the receiving
  enemy's own value is not applied (not verified against the game).
  The copy does not spread again ([20](../protocol/20-damage.md#outgoing-chill-freeze-and-shock-on-monsters)).
- **The small nodes** each give `base_cast_speed_+%` 4, `global_hit_causes_monster_flee_%` 5 and `fish_rarity_+%` 10.
  The cast speed reaches the panel's `cast_speed_+%` like any increase, so four of them give 16%.
- **Monster flee** (`MonsterFlee`, wiki Fleeing). Every player hit that leaves a monster alive rolls once in
  `AilmentsOnHitLocked`, so the default attack, area skills and projectiles all count.
  An attack's chance is `main_hand_hit_causes_monster_flee_%`; the client's calculator `0x1427906b0` sums it from the
  global stat and the main hand's `main_hand_local_hit_causes_monster_flee_%`.
  No 3.29.3b weapon mod grants the local stat, so the server's panel row sums the global one only.
  A spell uses `global_hit_causes_monster_flee_%`; the skill's own stat (Chance to Flee Support) adds to both.
  A successful roll then faces the monster's `monster_chance_to_not_flee_%`, which the rarity's own mods give:
  MonsterMagic7 50, MonsterRare7 and MonsterUnique7 100.
  That is the wiki's rule: magic monsters avoid half of the flees, rare and unique ones all.
  A monster that is already fleeing does not roll again.
  The monster picks a floor cell 50 cells (5 m) away within 60° of straight away from the player.
  It tries eight times, then eight times at 25 cells; when none is floor it does not flee.
  A swing that has already started finishes first.
  Then the AI walks the monster there (0x1ab Move). It neither chases nor swings until that walk ends, at the point
  or where the path stops short, and then chases again.
  It keeps its aggro during the walk; a freeze stops the walk and so ends the flee.
  The rolls use their own random, so the projectile reaction draws stay in order.
  Not verified: the distance, which the real server picks, and whether a hit during a swing cancels the swing.
  The real server may walk the monster with Monster.ot's `Flee` basic action (ActionTypes `HASH16` 0xe1a9).
  Its factory `0x14199aba0` builds the same move-action base (`0x141937cc0`) as Move's `0x141989180`, with its own
  vtable; the server sends Move, which the client is known to walk.
  Stun would also end a flee (wiki), but monsters are not stunned in this engine.
  Monsters that flee when ignited (wiki) are not modelled: monsters are not ignited.
- **Hit paths.** The ailments, Within the Tempest's kill shock and flee reach a hit only through
  `MonsterHitDamage` (the target's shock and chill at the hit's time, and the resisted packet) followed by
  `AilmentsOnHitLocked` on every landed hit, a killing one included. The default attack (`Attack`), area skills
  (`AreaSkillHit`) and projectile contact (`Hit`) do that; Righteous Fire's burn passes its time to `MonsterBurn` and
  calls `ShockOnKillLocked` on a kill.
  The older helpers (`MonsterDamage`, `MonsterTakes`, `MonsterTakesRolled`, and `MonsterTakesMitigated` or
  `MonsterBurn` without a time) still compile and leave the target's shock and chill out.
  A new hit of the player's own (for example a warcry's hit or a passive's triggered skill) has to use the two calls
  to get these effects; a minion's hit stays on the older helpers, since its kills and ailments are not the player's.
- **Sea Legs** already had its consumer (`ResistanceOvercap`, [11-skills](../protocol/11-skills.md) §12); the
  Gateway test compares the node with plain +40% cold and lightning resistance and both flags.

## Admission

The default Phrecia gate stays closed.
`Game_VERIFIED_SURFCASTER=1` admits only this identity after the same read-only installed-graph check as Gambler
(`VerifiedGamblerGraph`, ordinary `82D82CD3…` and combined `F2766B5A…`). A client with the original Phrecia graph is
refused before any save. Nothing installs or patches the archive.

With the graph installed, start through `scripts/dev/live.ps1` and run:

```text
/ascendancy phrecia Surfcaster
/ascendancy_points all
/add_passive 48926 64012 33031 37082 36782 37553 42290 61696
/item FishingRod1
```

Equip the rod and socket a spell, then check that its casts per second rise by about 60% and every hit is a critical
strike.
Take the rod off to see both end.
Build A also holds Ghost of the Deep: the Torment Touched icon shows, and the character panel adds 23 to increased
movement, attack and cast speed (cast speed 16% to 39%; with a rod the socketed spell's own cast speed goes from 86% to
122%, while the player-level stat keeps the plain 39%).
To try Changing Seasons, refund and allocate build B, then let a physical monster hit you: you are chilled and move
and attack faster for two seconds.

```text
/add_passive 41643 60505 34767 61246 53922 6252 37534 52944
```
To try the rod with Stormy Seas and Within the Tempest, refund and allocate:

```text
/add_passive 48926 64012 33031 37082 41643 60505 34767 61246
```

Then give the rod's attack some lightning and hit a monster with it:

```text
/item FishingRod1
/set_stat attack_minimum_added_lightning_damage 200
/set_stat attack_maximum_added_lightning_damage 200
```

Every hit is a critical strike of cold damage. Check that the monster shows the chilled, frozen and shocked icons,
stands still while frozen and takes more damage while shocked; the log's `0x01c1 monster` lines give the magnitudes.
A monster shocked by your hit shocks its neighbours within 1.5 m (`0x01c1 monster … Shock`).
Set both added lightning stats back to 0 and kill one monster of a pack with the rod's physical hit: its corpse and
every monster within 1.5 m of it show the shocked icon for two seconds (`0x01c1 monster … Shock 15%`).

Put the added lightning back and kill a unique monster with a cold critical strike above its life: its corpse shows
the hit's shock (`0x01c1 monster … Shock 50%` for a hit well above its life), not 15%, and nearby monsters get the
same.
A normal monster killed by a big cold hit shatters: nothing spreads from it, though its corpse stays on screen.
Its buffs go (`0x01c2 monster …`) only if it already showed ailments from an earlier hit; a one-hit kill sends none.

For Glacial Wave, allocate build B, give the attack the same lightning (Stormy Seas makes it cold) and chill a
monster: its `0x01b1 player hit by monster` lines drop by half the chill, and your hits on it rise by the chill.
For Brine Bound, allocate build C and let monsters hit you: about half of them freeze for one second (frozen icon,
`0x01c1 monster … Freeze`), and a cold monster's critical hit does not freeze you.
For monster flee, any build's four small nodes give 20%; `/set_stat global_hit_causes_monster_flee_% 100` makes every
hit roll it.
Hit a normal monster: it runs about 5 m away from you without attacking (`0x01ab monster … flees`), then comes back.
A rare or unique monster never flees, and a magic one flees from about half of the hits.

Coverage: `AscendancyGatewayIntegrationTests.Surfcaster` (the small nodes' cast speed and flee chance against plain
stats, the rarity's chance not to flee, the flee point, a forced flee through the hit, the AI walking a fleeing
monster in reach instead of swinging and its return after the walk; Ghost of the Deep against plain stats with its publication,
reload and refund; Changing Seasons on a physical and a cold hit, a freeze and the unaffected flag; the monster
ailment model's floor, caps, freeze threshold and strongest effect; shock against plain increased damage taken; the
default attack, an area sweep, a projectile's contact and Righteous Fire's burn each counting a target's shock and
Glacial Wave chill like a twin with the same increased damage taken from its mods; chill and freeze in the monster AI
with their buffs, the fixed buff instances and the additive durations; a frozen monster not starting its swing, a
chilled one reaching its contact later and a freeze before the contact cancelling the hit; Mirrored and Unstoppable;
Stormy Seas' cold critical strike shocking only with the node; Glacial Wave's damage taken, burn, the monster's swing
and its ignite against plain values; Brine Bound's freeze on a landed swing, the 1 s minimum and Cannot be Frozen
against plain stats; Within the Tempest's kill shock from the default attack, and the 15-cell spread from a corpse and
a hit shock, 21 cells with 100% increased area of effect, and a weaker, wider shock reaching past a stronger one's
radius; Faster Casting's cast speed in the spell record's total; an area skill's sweep, a projectile's contact and
Righteous Fire's burn each inflicting and killing, a unique's corpse keeping a killing critical strike's 50% shock
and spreading it, a normal monster shattering, and a Mirrored monster's reflected freeze landing after the sweep, so
the monster after it is still hit and the kill before it still gives experience; the passive buff sent on area
entry), the buff layout in `InstanceEntitiesTests` and `VerifiedGamblerGraphTests`.
