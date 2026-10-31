# Status: Monsters

Part of [Status](../Status.md); what to build next in this area is in
[roadmap/monsters.md](../roadmap/monsters.md).

## Works (verified with the real client)

- Monsters belong to the area instance: re-entering it finds the living ones where they were, with their damage, and
  the dead stay dead; a new instance gets fresh packs.
  Code: `InstanceStore.cs`, `PopulateArea`, Gateway.
  Docs: [14](../docs/protocol/14-monster-packs.md) §2.
- Monsters: `/spawn_mob <row, Id or name>[@level] [friendly] [rarity] [name]`, `/mobs`, `/despawn`, `/mob_walk`,
  `/ai`: any of 10 345 of the 10 691 MonsterVarieties at a level and rarity, with full life for its level, hostile
  (red) or friendly, that chases the player when near (Move action 0x1ab, client pathing).
  Code: `MonsterObject`, `MonsterCatalog.cs`, Gateway.
  Docs: [10](../docs/protocol/10-chat-and-commands.md) §5.
- Monster packs in areas: on entry the area fills with its MonsterPacks at the area level (The Coast: crab and Drowned
  packs, some magic, at least 2 with a rare; cannibal packs at the bonfires) and the unique boss at the camp's
  `mapboss` point with its boss pack (Fire Fury beside the campfire, verified live 2026-10-18);
  room points are found by fitting rooms to the doodads `-Walkmaps` captures.
  The Tidal Island's boat rooms are too sparse to fit and use their captured placement: Hailrake stands on the wreck.
  Rooms with a boss point take their actual placement (captured or generated); an unplaced sibling room adds none.
  `mapboss<n>[WG]` is the n-th of the area's bosses, sub-graph rooms included (Brutus, Merveil).
  `mapboss`, `force:mapboss` (Kitava) and `mapbosspack` take every boss left, once (The Den's two, both seals).
  Named packs bring their boss (Pikerivet, The Burning Man); The Quay's Tolman stands at his spawner.
  The Ashen Fields' boss is the bandit lord the character spared in Act 2, or Greust (`MapBossOverride`).
  A boss whose point is off the walk map stands on the nearest walkable cell; the boss pack stands once per area.
  `CampaignBossPlacementTests` (`Game_BOSS_CHECK=1`) checks every boss of the act 1–10 areas the Gateway generates.
  Code: `MonsterPacks.cs`, Gateway (`PopulateArea`), `scripts/walkmaps`, `scripts/re/atlas room-points`.
  Docs: [14](../docs/protocol/14-monster-packs.md).
- Unique map bosses get the map tables' life and damage increases: `MonsterMapBossDifficulty` plus
  `MonsterMapDifficulty`, added as percentages (level 83: ×20.73 life, ×1.15 damage). The King in the Mists' humanoid
  form therefore has 45,614,731 life at level 83, matching poedb's display.
  Code: `MonsterCatalog.MapBossScaling`, `SpawnMonster`.
  Docs: [king-in-the-mists](../docs/game/king-in-the-mists.md).
- Emerge monsters (Gravel Eaters, Dripping Dead, the Twilight Strand's Drowned …) are created buried, cannot be hit
  while buried, and rise when the player comes within 24 cells: the client plays their emerge clip (Emerge action
  0x0101, then Transitionable 2), and they fight, die and give experience like other monsters.
  The ones the client shows targetable while they wait (Tide Striders, Blood Elementals …) can be hit and rise when
  hit; unique bosses keep their own entrance state.
  Their packs are placed again (The Coast's `Act1-SandspitterEmerge`, the Mud Flats' `Act1-ZombieMiredEmerge`);
  an instance keeps buried ones buried and risen ones risen.
  Verified live 2026-10-25 (`/spawn_mob` Gravel Eater, packs on The Coast, the Mud Flats, the Twilight Strand, and
  lurking Tide Striders on Tidal Island).
  Code: `MonsterEmerge.cs`, `Program.Emerge.cs`, Gateway.
  Docs: [10](../docs/protocol/10-chat-and-commands.md) §5.
- Monster hit reaction: a positive hit that leaves a monster alive and takes at least a tenth of its maximum life
  requests native Actor TakeHit recovery (S→C 0x1d1, animation type 5).
  Stun immunity, an absent TakeHit profile or an existing recovery prevents it; a killing blow gets no flinch.
  A randomly chosen extracted clip length supplies the duration profile, with a 0.4 s minimum.
  The actor's native ACT selects and fits the presented clip; the server does not force a rig index.
  Recovery stops server and client movement, holds AI and invalidates old ordinary swing echoes and contacts.
  Historical LocalExile moving-Drowned samples held one cell through recovery, then resumed a fresh AI walk.
  This standalone port has fresh automated coverage; native integration remains pending in the
  [port record](../docs/dev/ranged-monster-actions-port.md).
  Rig-only 0x1b2 did not stop locomotion; it is no longer used as the enemy's recovery packet.
  Thresholds and duration profiles remain a bounded local policy rather than a complete official stun formula.
  The weapon impact sound and the target's blood come from S→C 0x1af "action hit", sent on melee and attack-gem hits
  and on monster hits on the player (2026-10-25; not yet checked live after the timing cap).
  GGG's server sends it too, but with the monster action's own sequence and skill index 2, where we send 0xffff and 0
  (Sand Spitters, live capture 2026-10-30; [port record](../docs/dev/ranged-monster-actions-port.md) "Live reference").
  A level 1 Sand Spitter hits for 5–7 here and a constant 4 on live (5 on a crit); the cause is not known.
  Code: `HitReaction.cs`, `MonsterCatalog.TakeHit`, Gateway (`SendMonsterFlinch`), `scripts/re/monster_takehit.py` →
  `data/monster-takehit.json`.
  Docs: [11](../docs/protocol/11-skills.md).
- Magic and rare monsters roll their mods from Mods.dat (monster domain, spawn weights by the monster's tags, level,
  one per family): magic one affix shared by the monsters of one name in an instance, rare two to four (poewiki
  "Rarity"), with a rare name from the Words.dat monster lists.
  Magic monsters glow blue and rare ones gold: the server puts the hidden champion buff on them
  (`MonsterRarityBuffs`, BuffDefinitions 66 / 67; [14](../docs/protocol/14-monster-packs.md) §2).
  The client shows them on hover (verified live 2026-10-25: "Gorewalker" / "Extra Life", "Quick Hungry Corpse" /
  "Hasted").
  Their life, hit, attack time, walk (sent as movement_velocity_+%, the client walks at the same speed), resistances,
  physical reduction, block, evasion, critical strikes and regeneration act on the server (each checked live); the
  instance keeps them.
  Only the 37 mods the server can honour roll; the other 74 are listed in [14](../docs/protocol/14-monster-packs.md)
  §5.
  Code: `MonsterMods.cs`, `Program.MonsterMods.cs`, `MagicProperties.cs`.
  Docs: [14](../docs/protocol/14-monster-packs.md) §5.
- Monster decisions use 50 ms of elapsed game time, with no catch-up burst after pauses or slow steps.
  Chase repaths use a separate 100 ms budget and require at least three cells of Manhattan target change.
  A ranged chaser stands off one cell inside its reach (MonsterVarieties `MaximumAttackDistance`: the Sand Spitter's
  30), moving inward only when that ring is full, instead of taking a melee contact cell (`MonsterSurround.Asker.Ranged`;
  live 2026-10-29: Spitters came in close). `MinimumAttackDistance` is not read yet.
  Attack busy time covers the full attack duration; pending swing and stage generations prevent duplicate work.
  Three native AI-enable controls issued one chase each after 43, 38 and 50 ms.
  Four deliberate turns produced four chase orders at 249 ms intervals without duplicate orders between turns.
  Drowned attack starts remained 2432 and 2428 ms apart for a 2.4 s attack.
  Actual Escape-menu pause held 30 samples at one cell without actions; resume continued the existing path.
  Automated cadence, pause, interruption and stage-completion checks pass; quoted native controls are historical
  LocalExile evidence, not a fresh run of this standalone port.
  Code: `MonsterAiCadence.cs`, `Program.Monsters.cs`.
  Evidence: `artifacts/wand-ai-validation/ai-native-response-*.json`, `ai-native-turns.json`,
  `ai-native-attack-pacing.json` and `ai-native-menu-{paused,resumed}.json`.
  Docs: [11](../docs/protocol/11-skills.md) §6.
- Monsters fight back: their first granted effect as a non-pathing skill with MonsterVarieties attack time, reach and
  level damage; they swing (animated) when in reach and the player's life goes down after evasion, armour and block;
  only monsters with a critical strike chance from their data or mods crit (none from their data yet; of Deadliness);
  walking away dodges; death plays and blocks moving; the death screen (S→C 0x55) shows "Resurrect in Town" /
  "Resurrect at Checkpoint" (hardcore: "Exit"), and both buttons work (C→S 0x54; in town: the last town); `/heal`.
  Code: `MonsterAttack`, `Resurrect`, Gateway.
  Docs: [11](../docs/protocol/11-skills.md) §6–7.

## Partly working

- The King in the Mists' Crux of Nothingness encounter: `An Audience With The King` alone opens the area in the map
  device; the humanoid form fights with the default attack, spell projectile, Vine Slam and Vine Cascade, teleports to
  three Mystic Fetishes (Portent Vapors stacks) and summons Plagued Rats; after the third fetish it becomes the treant
  (`VoodooKingBoss3`, about 26.6M life) with Treant Slam, Bug Bomb, Bugrend and effigies. The kill rolls the guaranteed
  unique, Hextoad/Hexpass and The Endless Darkness.
  Not yet: the separate maze layout, ground hazards, Ritual of Dance/Meditation imprisonment, monster energy shield,
  Shadow Surge/Mystic Miasma damage over time, corpse items, the Unlight Altar/Bloodline and the native animation
  capture.
  Code: `KingInMists.cs`, `ItemDrops.KingInMists`, `MapDevice`.
  Docs: [king-in-the-mists](../docs/game/king-in-the-mists.md).
- Inherited Life corpse eligibility and sinking flags flow from object-types manifest v4 through `MonsterCatalog`
  into ordinary monster creation.
  The combined object-types format retains the stationary collision footprints and models alongside Life data.
  Explicit false, native defaults, missing inheritance and the Player veto have focused extraction and Core tests.
  The ordinary corpse path requires zero Life; a life-1 boss stage remains alive, with sinking handled independently.
  Six integrated native cases at source `dbfe717`, recorded by `53edc77`, cover ordinary alive/dead, Raised Zombie
  false, Hillock life 1, sinking and an owned Spectral Wolf forced false by that source's separate summon runtime.
  This branch preserves the ordinary flag math and fields; it has no independent native pass or owned-actor runtime.
  The Core helper retains an optional owned guard; corpse identity, final-death registration and consumption remain open.
  Code: `MonsterLifeData.cs`, `MonsterObject`, `MonsterCatalog.cs`, `Program.Monsters.cs`, `ot_components.py`.
  Docs: [04](../docs/protocol/04-player-entity.md#44-life-0x141e6c2f0-55-bytes).
- Monster walk speed on the server includes the client's per-monster speed roll: Actor setup draws a uniform percent
  in the MonsterVarieties row's range (the Goatmen ±5 %, the 7.60–8.40 band measured live for a base of 8; most rows
  none) from a TinyMT32 state keyed by the object id and its type, and the walk estimate, swing range and contact
  checks use that speed.
  The range, the generator and the key order come from the decompile and pass the tests; whether the key's type word
  is the 0x2b9 type hash, and so whether the exact value matches the client's, is not checked live yet
  (`scripts/re/frida/trace-movement.js` prints each monster's seed words, range and multiplier).
  Code: `MonsterSpeedRoll.cs`, `AnimationVariantSelector.cs`, `MonsterCatalog.cs`, Gateway (`SpawnMonster`).
  Docs: [07](../docs/protocol/07-latency-visibility-movement.md) §5 ("Monsters on the client").
- Monster mods' accuracy (of the Marksman, of Precision): the monster's accuracy is raised in its swing, but only the
  tests check it; live, the player was never evaded.
  Code: `MonsterMods.cs`.
  Docs: [14](../docs/protocol/14-monster-packs.md) §5.

## Not working yet

- Area monsters: pack counts, magic/rare chances and rare mod counts are our own numbers;
  74 monster mods need systems the server lacks (auras on allies, monster skills, ailments on the player …) and do
  not roll;
  Drowned ignore their ground types; how near the player must come before an emerge monster rises is our choice;
  the boss life bar and boss stages exist for Hillock only (`BossFights.cs`;
  other bosses with `BossHealthBar` get the bar too); monsters respawn on every entry;
  a new (area, seed) needs a `-Walkmaps` visit for its unique boss ([14](../docs/protocol/14-monster-packs.md) §4).
  Act 2's bandit lords (Kraityn, Alira, Oak) are created hostile without their help-or-kill dialogue
  ([17](../docs/protocol/17-npcs.md)).
  Not placed: Marceus the Defaced (a tile's statue spawner), The Solaris Temple Level 2's three Banners (a room the
  layouts never place) and Thistlesage (no room names his pack) ([14](../docs/protocol/14-monster-packs.md) §3).
  A boss's later form (Abberath2, Shakari2, Malachai's second form, The Depraved Trinity) is left to a fight or quest
  that does not create it yet.
