# Status

Where the project stands. Last updated 2026-10-21, against client **3.29.3b**. Update this file in the same pull request
as any change that makes something work, or that finds something broken.

What to build next is in [Roadmap.md](Roadmap.md). How the protocol works is in [docs/README.md](docs/README.md).

## Works (verified with the real client)

| Feature | Where | Docs |
|---|---|---|
| Game data extracted from the GGPK in one step: `scripts/dev/extract-data.ps1` (run by `live.ps1` on every start, `-NoExtract` to skip) regenerates each data file that is missing or whose manifest version changed (stamps in `data/extract-stamps.json`), and the Gateway warns once at startup about any missing file | `scripts/dev/extract-data.ps1`, `GameDataFiles.cs` | [GettingStarted](GettingStarted.md) step 7 |
| Local patching server: the client never contacts the official patch servers | `GameServer.Patch` | [01](docs/protocol/01-login.md) §1, [GettingStarted](GettingStarted.md) step 6 |
| Login: custom TCP, Diffie-Hellman + DSA-signed server key + Salsa20, account login, character list, Play | `GameServer.Auth`, `GameServer.Core/Protocol/Login` | [01](docs/protocol/01-login.md), [re/008](docs/re/008-login-handshake.md), [re/009](docs/re/009-login-messages.md) |
| Instance connection: opening, encryption switch (0x11), framing, dispatcher | `GameServer.Gateway`, `GameServer.Core/Protocol/Instance` | [02](docs/protocol/02-instance-connection.md) |
| Entering Lioneye's Watch: area (0x1d), local player id, world activation, HUD | Gateway | [03](docs/protocol/03-area-and-world.md) |
| Player object (0x2b9) with its component blob; the character's name and class model | `InstanceEntities.cs` | [04](docs/protocol/04-player-entity.md) |
| Inventories and passive slots the UI needs (empty) | Gateway | [05](docs/protocol/05-inventories-and-ui.md) |
| Latency acks (0x1b/0x1c) and the HNC side channel, answered from the read loop ahead of other handlers, the client's stale tick queue cleared on each connection (server delay 0xFFFF, so F1 NETWORK reads 0 and spikes only on overdue ticks; ticks or handlers over 50 ms are logged), visibility (wake zone), click-to-move | Gateway, `Hnc.cs`, `ClientInbox.cs` | [07](docs/protocol/07-latency-visibility-movement.md) |
| The player's walk on the server is the client's own pathfinder and stepping, ported 1:1 (A* with the player's ellipse limit, waypoint pruning, Bresenham steps at the client's step time, NPCs and stashes as blockers): on a recorded session every one of 424 client paths matched, and the reported cell is exact or within one step (client frame timing). Walk-ups to NPCs, stashes, waypoints and area transitions use the client's range (object size + 3); clicks are timed by their socket read; monster paths port 1:1 too (1,235 of 1,235, 100 expansions + coarse pass) | `GameServer.Core/World/ClientPathfinder.cs`, `PlayerWalk.cs` | [07](docs/protocol/07-latency-visibility-movement.md) §5 |
| Server frames (S→C 0x2b6 frame length, 0x2b5 frame end every 33 ms): what a lockstep client's world runs on; ignored by a predictive client | Gateway (`RunServerFrames`) | [07](docs/protocol/07-latency-visibility-movement.md) §6 |
| Area transitions in every area, with their destinations, models (the arch highlighted on hover), rotation and height: fixed layouts from the atlas, generated layouts from the walk-map recorder's capture; verified town ↔ The Coast ↔ The Tidal Island and The Coast ↔ The Mud Flats; area change through them; Ctrl+click opens the Instance Manager (list, join, create); `/goto <area>` | Gateway (`AreaTransitionCatalog.cs`), `scripts/walkmaps`, `scripts/re/atlas`, Auth's instance registry | [08](docs/protocol/08-area-transitions.md), [09](docs/protocol/09-area-change.md) |
| Arrival points: entering through an exit (or `/goto`) puts the player next to the exit back; The Twilight Strand → Lioneye's Watch arrives at the town's `entrance1`; Resurrect at Checkpoint revives there, or next to the area's waypoint once the character has activated it | `AreaLayout.Arrival`, Gateway | [09](docs/protocol/09-area-change.md) §4 |
| Monsters belong to the area instance: re-entering it finds the living ones where they were, with their damage, and the dead stay dead; a new instance gets fresh packs | `InstanceStore.cs`, `PopulateArea`, Gateway | [14](docs/protocol/14-monster-packs.md) §2 |
| Ground items belong to the area instance: dropped items and loot stay where they lay across logouts, disconnects and client crashes until the instance expires; ids from 0x100000; one pickup per item | `InstanceStore.cs`, Gateway | [13](docs/protocol/13-items.md) §5 |
| Quests: flags per character in the save, one script per quest (`Quests.cs`), `/quest_flag`. a1q1: new characters start on The Twilight Strand's beach and log in there until they reach a town; coming within 60 cells of Hillock sets `a1q1-SeenHillock` ("Kill Hillock"); Hillock has the boss life bar at the top of the screen once he aggroes (StateMachine `boss_life_bar`, 0x2ba), unique life (311 at level 1) and a second stage: at half life he pulls out his sword (ZombieBossExtract, Transitionable → 2) and his life fills up again; the force wall (`Tutorial_Blocker_3`, the room's angle and scale 2.7) stands across the way to the gate until he dies, and the server closes the way on its own walk map too (`QuestBarriers`), so its position estimate stops where the client stops; after his death the quest log says to enter the gate (every character has `CompletedAllTutorials`); his death levels the character to 2, removes the wall and creates the gate into Lioneye's Watch (the Gateway checks the flag too); Portal Scrolls do nothing on the Strand (its area mod MapNoPortals); no Resurrect in Town before a town | `Quests.cs`, `BossFights.cs`, `QuestBarriers.cs`, `AreaModCatalog.cs`, Gateway | [12](docs/protocol/12-quests.md) §5–6 |
| Instances: one per town (all 12 act towns from WorldAreas `IsTown`); other areas get private instances that live 6 minutes once empty | `InstanceRegistry.cs`, `WorldAreaTowns.cs` | [09](docs/protocol/09-area-change.md) |
| Walk maps and area transitions captured from the client on first visit; the Gateway uses them for position estimates, spawns and area transitions | `scripts/walkmaps`, `live.ps1 -Walkmaps` | [GettingStarted](GettingStarted.md) step 9, [08](docs/protocol/08-area-transitions.md) §7 |
| Chat commands `/commands` (`/com`; paged, `/com <name>` for one), `/raw <hex>`, `/set_stat`, `/set_speed`, `/set_level <1 to 100>` (saved to the account, sent in place), `/add_passive_points <n>`, `/add_passive <id>` (replies as chat lines) | `Program.cs` (`Commands`), `ChatCommands.cs` | [10](docs/protocol/10-chat-and-commands.md) |
| Monsters: `/spawn_mob <row, Id or name>[@level] [friendly] [rarity] [name]`, `/mobs`, `/despawn`, `/mob_walk`, `/ai`: any of 10 345 of the 10 691 MonsterVarieties at a level and rarity, with full life for its level, hostile (red) or friendly, that chases the player when near (Move action 0x1ab, client pathing) | `MonsterObject`, `MonsterCatalog.cs`, Gateway | [10](docs/protocol/10-chat-and-commands.md) §5 |
| Monster packs in areas: on entry the area fills with its MonsterPacks at the area level (The Coast: crab and Drowned packs, some magic, at least 2 with a rare; cannibal packs at the bonfires) and the unique boss at the camp's `mapboss` point with its boss pack (Fire Fury beside the campfire, verified live 2026-10-18); room points are found by fitting rooms to the doodads `-Walkmaps` captures | `MonsterPacks.cs`, Gateway (`PopulateArea`), `scripts/walkmaps`, `scripts/re/atlas room-points` | [14](docs/protocol/14-monster-packs.md) |
| Skills and combat: the player has the default attack (PlayerMelee, Characters.dat StartingMeleeSkill) in the Actor skill list; it goes on the skill bar, walks into range, swings at the unarmed speed (757 ms with dual wielding's 10%) or faster with `/set_stat attack_speed_+%` (server-computed final stats, 0x2ba skill update, panel's Attacks per Second), and the server lowers the monster's life 2–5 at the contact point; moving cancels; the dead monster's life bar hides | `GrantedSkill`, `SkillCatalog.cs`, Gateway | [11](docs/protocol/11-skills.md) |
| Impale: a physical hit that rolls `impale_on_hit_%_chance` stores 10% of the hit's physical damage on the monster (five stacks, five hits each), and every later physical hit on it takes the stored damage again as extra physical before the roll; five stacks add half the impaling hit's damage — the mechanic behind the panel's Chance to Impale row, verified live 2026-10-20 (the bonus climbs 0.5 → 2 over five hits and a fresh target reopens at one stack) | `Impale.cs`, Gateway (`Attack`) | [11](docs/protocol/11-skills.md) §14 |
| Offence panel damage rows: the hit the server swings with becomes `main_hand_minimum/maximum_total_damage` and the per-type minimum/maximum rows, with `skill_uses_main_hand` so the client shows them; whole numbers, main hand only, and an empty packet sends none | `PanelStats.HandDamage`, Gateway (`CharacterPlayerStats`) | [11](docs/protocol/11-skills.md) §14 |
| Accuracy: the player's final rating includes flat/increased passives, weapon-specific stats and the unconditional Accuracy Mastery effects; attacks roll PoB's hit chance against the monster's `DefaultMonsterStats.Evasion`, misses send no life update, spells always hit; the Offence panel gets `main_hand_accuracy_rating` / `off_hand_accuracy_rating` and the Chance to Hit rows at the character's level; `/accuracy [level]` | `CharacterStats.cs`, `DamageModel.cs`, `MonsterCatalog.cs`, Gateway | [11](docs/protocol/11-skills.md) §4–5 |
| Armour and Evasion: flat armour/evasion from the passive tree (`base_physical_damage_reduction_rating`, `base_evasion_rating`) and the base body armour/helmet/gloves/boots (the `ArmourTypes.dat` minimum), with `physical_damage_reduction_rating_+%`, `evasion_rating_+%`, `evasion_and_physical_damage_reduction_rating_+%` (Cloth and Chain), `global_defences_+%` and the combined increase; the character panel gets the final `physical_damage_reduction_rating` / `evasion_rating`; physical `/hit` and monster melee apply PoB's armour reduction (90% cap), monster attacks use PoE's shared evasion entropy counter before damage, and `/defences` shows armour, physical reduction and the chance to evade a reference monster. Iron Reflexes converts all evasion to armour and drops dexterity's evasion bonus. Not yet: item mods/quality, shields or auras | `CharacterStats.cs`, `DamageModel.cs`, `ItemCatalog.cs`, Gateway | [11](docs/protocol/11-skills.md) §4, [13](docs/protocol/13-items.md) §2 |
| Monsters fight back: their first granted effect as a non-pathing skill with MonsterVarieties attack time, reach and level damage; they swing (animated) when in reach and the player's life goes down after evasion, armour and block; walking away dodges; death plays and blocks moving; the death screen (S→C 0x55) shows "Resurrect in Town" / "Resurrect at Checkpoint" (hardcore: "Exit"), and both buttons work (C→S 0x54; in town: the last town); `/heal` | `MonsterAttack`, `Resurrect`, Gateway | [11](docs/protocol/11-skills.md) §6–7 |
| Life and mana flasks: a flask key (C→S 0x70) spends the flask's charges and recovers Flasks.dat's life or mana over its time in a 20 ms loop, the orb rising by +1, with the flask_effect buff; every kill, town, hideout and resurrection fills the flasks | `Flasks.cs`, Gateway (`UseFlask`, `RunFlaskRecovery`) | [13](docs/protocol/13-items.md) §8 |
| Items: the start kit (Characters.dat skill and support gems, two life flasks on keys 1–2 and a mana flask on key 5) in the inventories, and the class weapon with two linked sockets worn in the main hand (`Game_START_WEAPON=ground`: on the ground a few cells from the spawn until it is picked up); pick up (0x28), put down, swap and equip (0x2a, slot classes and attribute requirements checked by the client), one-hander off hand → main hand, socket and unsocket gems (0x30, 0x2f), drop to the ground outside town (0x29 → WorldItem) and pick up again (to the cursor with the inventory open, at the client's 22-cell range), Destroy in town (0x16 /destroy); stacks: merge a held stack onto one of its type up to the CurrencyItems StackSize (0x4a), split with Shift + click (0x7c), Shift + click while holding places one, full fragment / shard stacks become their item, pick-ups, `/item` and sale rewards fill existing stacks first; the equipped weapon on the model; active gems in equipment grant their skill; class attributes and the class on the character panel; `/item <row, Id or name> [count]` | `InstanceItems.cs`, Gateway (`ItemCatalog.cs`, `PlayerInventories.cs`) | [13](docs/protocol/13-items.md) §4a |
| Item mods: rarity (normal, magic, rare, unique), identified, and the implicit, explicit, enchant, scourge and hidden mod lists in the Mods component; new items roll their BaseItemTypes implicits (Driftwood Wand 8-12% increased Spell Damage); magic names come from the affixes; rarity and mods are saved with the item (characters and stash); `/mod`, `/mods`, `/rarity` on the held item. Magic and rare items: affixes rolled from Mods.dat by domain, item level, the item's tags and families, rare names from Words.dat; equipment drops magic / rare and unidentified; a Scroll of Wisdom identifies (C→S 0x71); `/item <type> magic\|rare`, `/roll`. Equipped items' mods count on the character: global stats like a passive's (life, resistances, attributes, spell damage …), the main hand's local crit, attack speed and accuracy on the weapon. Not yet: local weapon damage and defence mods, uniques and special mods (#60) | `InstanceItems.cs`, Gateway (`ModCatalog.cs`, `ItemRolls.cs`, `ItemDrops.cs`, `ItemModStats.cs`, `ItemCatalog.cs`, `CharacterSaves.cs`) | [13](docs/protocol/13-items.md) §2a-§2c |
| Gem skills: Fireball from the socketed gem with its level-1 stats (9–14 fire, 750 ms cast via `virtual_base_spell_cast_time_ms`, 5 mana), cast at the ground or at a monster; the server creates the `Metadata/Projectiles/Fireball` object, flies it to the target at terrain height + `Game_PROJECTILE_HEIGHT` where the area has a height grid (Lioneye's Watch, The Coast seed 1; live 2026-10-18: above the ground at every level, at belly height), else on the terrain and hits the monster on arrival | `SkillCatalog.cs`, Gateway (`CastOn`, `LaunchProjectile`), `ProjectileObject` | [11](docs/protocol/11-skills.md) §9–10 |
| Saving characters: items, equipment, sockets and the skill bar per character in `data/saves.db` (a new bar: the default attack on the left mouse button, gem skills into empty slots only, right mouse button first); the bar is restored with S→C 0x53 after login and area changes (binds are C→S 0x4e) | `CharacterSaves.cs`, `PlayerInventories.cs`, Gateway | [11](docs/protocol/11-skills.md) §11, [13](docs/protocol/13-items.md) §7 |
| Logins start at the character's last town's spawn point, as in the real game (after logging out, Exit to Character Selection, a crash, a restart or a disconnect while dead); Auth records the town when a town ticket resolves (`Character.LastTown` in `data/saves.db`) | Auth (`InstanceDirectory`), `WorldAreaTowns.cs` | [09](docs/protocol/09-area-change.md) §4 |
| Waypoints: the Waypoint object where the area's layout is captured (Lioneye's Watch built in), activated on entry (towns), within 25 cells or on click (the client plays the activation), saved per character; clicking opens the World panel in travel mode (S→C 0x7b), its pins and connection lines show from the character's visited areas and activated waypoints (S→C 0x4c, quest flags in the Player component via 0x2ba), and picking a pin (C→S 0x4d, Ctrl → instance chooser) takes the player next to the destination's waypoint; `/waypoints [all \| reset]` | `InstanceWaypoints.cs`, `WaypointCatalog.cs`, `AreaVisitFlags.cs`, Gateway | [16](docs/protocol/16-waypoints.md) |
| NPCs in Lioneye's Watch: Tarkleigh, Nessa and Bestel at their real-game cells, with name, minimap icon and idle model; clicking walks to them and opens the dialogue window (S→C 0x84); talk options play (0x82 → 0x84 + index); Continue and Goodbye (C→S 0x85) are answered; Purchase Items opens the shop (S→C 0xc0 manager 1 + 0x8c) and buying (C→S 0x88) costs 1 Scroll of Wisdom; Sell Items: the sell window (S→C 0xb8) takes drops, Ctrl + click (first free cell) and Shift + click, Accept gives 1 Scroll Fragment per item and Cancel returns them, and the player can walk afterwards; Respecialisation opens the character's passive tree (0x7b kind 0x5f), where Apply refunds (C→S 0x36) and Refund All (0x37) refund passives for the client's gold price; the normal tree's Apply refunds (0x36 with flag 0) spend saved refund points; Identify Items identifies the inventory for free, prints "N Items identified" in chat (S→C 0x17) and grays the option; `/npc`, `/npcs`, `/pos` | `InstanceNpcs.cs`, `NpcCatalog.cs`, Gateway (`SpawnTownNpcs`, `TalkToNpc`) | [17](docs/protocol/17-npcs.md) |
| Stash: the Stash in Lioneye's Watch at (319, 272) opens the stash panel (S→C 0x7b kind 1) with the account's 11 tabs of the character's league (normal, premium, quad, currency, essence, fragment, map, flask, gem) from the tab list in S→C 0x21; each tab's contents come on the client's request (C→S 0xc7 → 0xc0 row 26 + 0xc8); items go into and out of tabs by click (0x28 / 0x2a) and Ctrl + click (C→S 0xe0 in, 0xe3 out, stacks onto stacks); special tabs hold only their own items (the map tab nothing yet), the currency, essence and fragment tabs each item in its own slot (currency up to 10 000), from the game data's stash tables (18 §7); Ctrl + click on currency goes to its affinity tab's slot (C→S 0xe1, live 2026-10-20) and a plain click on a currency slot picks up the stack; the tab settings' affinities (0xd3) are saved; the stash reopens on the account's last tab after a relog (0x7b affinity on the first open per connection) and keeps the panel's own tab while the connection lasts; tabs and items are account-wide (the account's row in `data/saves.db`). Verified live with normal and premium tabs; the special tabs load but their slots are not checked. Guild stash: a second object in every hideout opens the guild stash panel (0x7b kind 2) with its own tabs (manager 2), one per account until there are guilds (verified live) | `AccountStash.cs`, `StashCatalog.cs`, `InstanceStash.cs`, Gateway (`OpenStash`, `SendStashTab`) | [18](docs/protocol/18-stash.md) |
| Portal Scroll and the Portal skill (its 2.5 s cast): outside towns and hideouts a portal opens beside the player, replacing the previous one; from a map it leads to the hideout, anywhere else to the last town. That town or hideout gets a portal back, which brings the player to the portal's cell of the same instance; there the portal stands again (`OpenTownPortal`, `SpawnTownPortals`, `OpenPortals`; while the server runs, being tested 2026-10-23) | Gateway (`UsePortalScroll`, `UseMapReturnPortal`) | [21](docs/protocol/21-hideouts.md) §5 |
| Hideouts: every character has the hideout unlocked (quest flags HideoutUnlocked, CanEditHideout …; league quest flags UnlockedHideouts, HaveHideout, UnlockedMapDevice in the Player component, sent as bit id − 1) and owns the Lush Hideout; `/hideout` (C→S 0x16 command 45) and the World panel's Hideout button (C→S 0x17a) take the player there (its own instance, seed 1) next to its waypoint; its Waypoint (with HideoutDoodad data), Stash, Nessa and map device come from its decoration (`scripts/re/atlas hideouts` → `data/hideouts.json`); the HUD shows the Edit button; the map device can be clicked and opens its window (S→C 0x7b kind 5); maps go into its slots, Activate (C→S 0x00d8) checks them (maps and map fragments only, one map), consumes them and opens six portals (MultiplexPortal + S→C 0x008f) into one instance of the map's area, each used once (the one taken stays gone); in the map, a portal back to the hideout, the map's own monster packs (Maps.dat) and its boss arena gates (teleports); Nessa sells Beach Maps; `/league_flag`, `/hideout_keys` (dev, answered in chat) | `HideoutCatalog.cs`, `InstanceHideouts.cs`, `WorldAreaHideouts.cs`, Gateway (`WarpToHideout`, `SpawnHideoutDoodads`, `UseMapDevice`, `ActivateMapDevice`, `UseMapPortal`), `MapDevice.cs` | [21](docs/protocol/21-hideouts.md) |
| Shop portal effects as game data (dev): `/cosmetic list [filter]`, `/cosmetic portal <name, number or part of a name>` (`CosmeticCatalog.Matches`: an ambiguous piece lists the matching effects with their numbers instead of applying the first), `/cosmetic off`. A portal cosmetic is the portal's own 0x008f variation (+0x26, a `MicrotransactionPortalVariations` row), so the client draws the shop effect in place of the base portal — also for portals already standing, which are listed again — with no second object, store or balance (seen live 2026-10-21: the Black Hole effect replaces the base portal visual on all six portals — its flat disc is the real art, and the client keeps each rig's own orientation, ignoring the object's rotation and the portal type; `/portal_rotation` is the dev knob for a rig that does come out misoriented); the choice is saved per character (`PortalCosmetic` in the save, verified to survive a relog) | `Cosmetics.cs`, `InstanceMapDevice.Portal`, Gateway (`ResendMapPortals`, `/cosmetic`) | [21](docs/protocol/21-hideouts.md) §5 |
| Microtransactions: every account owns every cosmetic the client can show (the 7721 `MtxTypes` rows with an `MtxTypeGameSpecific` row); the Microtransactions panel (N), the store's ALREADY OWNED marks and the Cosmetics tab (K) load (C→S 0x1f2 → S→C 0x1f4, hideout and guild lists empty via 0x1f5 / 0x1f4); a pick in the Cosmetics tab (C→S 0x1f9) is saved per character and worn (S→C 0x1f3, also on every area entry); in the world: equipment skins, weapon effects, attachments and character effects on the model (Inventories component references), footprints and apparitions through the stats, portals (the map device's), cursors and flask skins. Verified live 2026-10-23. Built, awaiting live verification: pets (spawned beside the player, following it, not blocking; not yet their idle-relaxed animations, e.g. the Goblin Troupe's song), the portrait (character panel and selection screen), skill effects (the Cosmetics tab's skill slots, picked with C→S 0x1fa, the effect's stats on that skill, Righteous Fire's ring and buff visuals, restyled at once while it burns, and the projectile and explosion of projectile skills such as Fireball) | `InstanceMicrotransactions.cs`, `Microtransactions.cs` (`MtxCatalog`, `CosmeticEffects`), Gateway (`AnswerMtxRequest`, `PickCosmetic`, `Looks`, `CosmeticStats`), `CharacterSaves.Cosmetics`, `PetObject`, `SpawnPets`, `RunPetFollow`, `SkillSlots` | [23](docs/protocol/23-microtransactions.md) |
| Pause: the Escape menu outside towns and hideouts pauses the area (C→S 0x1d4 → S→C 0x1d3): the Gateway's clock stops, monsters neither move nor swing, regeneration and flasks wait, and the client pauses its world; closing the menu resumes | `InstancePause.cs`, Gateway (`SetPaused`) | [22](docs/protocol/22-pause.md) |
| Gold: shared by the account's characters of one league (S→C 0x2a9), saved with that league's stash, sent on area entry; every league has its own stash and gold, Standard keeps the account's stash from before (`AccountStash.Key`); monsters drop piles of gold (normal 40%, magic 75%, rare and unique always; (3 + 1.2 × level) × 1/2/5/15, our own numbers, `ItemDrops.Gold`), and picking one up adds it to the gold; gold within 15 cells of the player is picked up by itself (player tick, `GoldAutoLootRange`); older per-character gold moves into the account at login; `/gold <±amount>` | `InstanceEntities.Gold`, `AccountStash`, Gateway | [04](docs/protocol/04-player-entity.md) §6 |
| Dev account service: register, log in, create characters (JSON-lines over TCP) | `GameServer.Host`, `GameServer.DevClient` | [dev-server.md](docs/dev-server.md) |
| Leagues and character creation: Standard and Allflame with their Hardcore / SSF / Ruthless variants on the create screen (S→C 0x1b), 24 character slots, any class in any league (C→S 0x12 → 0x15, 0x13), straight into the game; the league under the area name in game (0x1d); the hardcore death screen from the character's league; `Game_LEAGUES` | `LeagueCatalog.cs`, `LoginServerSession.cs`, Auth, Gateway | [01](docs/protocol/01-login.md) §7 |
| Character deletion on the character screen (C→S 0x0c → new list 0x15 + result 0x0d); the save is set aside in `data/saves.db` (`deleted_characters`) | `LoginServerSession`, Auth | [01](docs/protocol/01-login.md) §5 |
| Every class in game: the Characters.dat row's object type and model, class byte, attributes, unarmed attack and start kit (weapon, gems, sockets in the gems' colours); verified with a Templar | Gateway (`SkillCatalog.cs`, `PlayerInventories.cs`) | [01](docs/protocol/01-login.md) §7, [04](docs/protocol/04-player-entity.md) |
| Exit to character selection: Esc → "Exit to Character Selection" and the hardcore death screen's Exit (C→S 0x153); the Gateway closes the instance connection and the client returns to the character list on the same login connection; Play enters again | Gateway | [09](docs/protocol/09-area-change.md) §6 |
| Experience and levels: kills within 150 cells give DefaultMonsterStats experience × the variety's multiplier × rarity, with the level penalty (S→C 0x2ba Player experience; the client derives the level); a new level sends its stats, full life and mana, the passive points and the account's level; experience is saved per character; `/add_xp` | `Progression.cs`, Gateway (`GainExperience`, `ApplyLevel`) | [15](docs/protocol/15-progression.md) §1 |
| Passive Skill Tree: S→C 0x33 (level, class, allocated nodes, passive and refund points; the client counts level − 1 points), C→S 0x34 allocates a node, checked, applied to the stats (attributes, life, mana, resistances and more) and saved; C→S 0x36 with flag 0 refunds a node for a saved refund point (checked, mastery effects removed); using an Orb of Regret (right-click, C→S 0x58) takes one item and adds one point; a Book of Skill (right-click, C→S 0x58; every quest book of that name) is used up and gives one more passive skill point, both saved together; a Book of Regrets gives two refund points the same way; `/passives`, `/add_refund_points`, `/reset_passives` | `PassiveTree.cs`, `CharacterSaves.cs`, Gateway (`ChangePassives`, `UseItem`) | [15](docs/protocol/15-progression.md) §2 |
| Resistances: the passive `base_resist_all_elements_%` / per-element `base_*_damage_resistance_%` are summed from the 0% base, the two Kitava quest penalties (-30% each) are mirrored from quest flags, and the final value uses Path of Building's floor -200 / default max 75 / absolute cap 90 (`DamageModel`); used by Righteous Fire's self-burn and `/hit`; `/resistances` shows modifier, penalty and final | `CharacterStats.cs`, `DamageModel.cs`, Gateway | [11](docs/protocol/11-skills.md) §12 |
| Spell Suppression: the passive `base_spell_suppression_chance_%` is capped at 100% and a successful suppress takes 40% less spell damage plus `base_spell_damage_%_suppressed` (PoB `SuppressionChanceCap`/`SuppressionEffect`); `spell_suppression_chance_is_lucky` rolls twice and keeps the better roll (effective `1-(1-p)^2`), with an unlucky `p^2` dev mode; the conditional chance passives count while their condition holds: `spell_suppression_chance_%_while_on_full_life`, `_while_moving` (the walk estimate) and `_if_enemy_hit_recently` (an attack or spell hit in the last 4 s); `/suppression`, `/suppression_luck`, `/spellhit`. Not implemented: the full energy shield stats (no energy shield model), the other conditional effect stats, monster spell hits. The character panel gets the chance again within 250 ms of a condition changing (the player tick) | `CharacterStats.cs`, `DamageModel.cs`, Gateway | [11](docs/protocol/11-skills.md) §12 |
| Movement speed: the passive `base_movement_velocity_+%` is summed into `Pools`, sent as the client's `movement_velocity_+%` and used by the server's walk copy (37 × (1 + pct / 100) cells/s); applied on login, level, passive and area changes; `/set_speed` adds its percentage on top; only unconditional passive increases are modelled (no action speed, more multipliers, gear/buff movement or the 128 cap yet) | `CharacterStats.cs`, Gateway (`ApplyMovementSpeed`) | [07](docs/protocol/07-latency-visibility-movement.md) §3 |
| Block / Spell Block: passives' `additional_block_%` and `base_spell_block_%` are summed and capped at 75% / 90% (PoB `BlockChanceCap`), max-res passives `maximum_block_%` / `base_maximum_spell_block_%` raise the caps; a successful block prevents the hit, monster melee and `/blockhit` use it; `/block` shows both chances; an equipped shield adds its `ShieldTypes.Block` base and enables the shield-specific passives, staff passives apply with a staff, and dual wielding adds its inherent 20% attack block and dual-wield block/spell-block passives; Versatile Combatant reduces both maxima by 10% and converts overcapped attack block to spell block at 2% per 1%; lucky block uses 1-(1-p)^2 for attack/spell (dev `/block_luck`); the character panel gets `uncapped_attack_block_%` and `display_spell_block_chance` so it shows the uncapped value in parentheses and the spell block row | `CharacterStats.cs`, `DamageModel.cs`, Gateway | [11](docs/protocol/11-skills.md) §12 |
| Character panel sums: about 150 panel stats that sum base forms are computed from passives, items and auras with their unconditional combined stats: stun, ailment avoidance, durations and effects (ignite, chill, freeze, shock, scorch, brittle, sap), ailment chances, penetration, leech, life/mana/ES on kill, rarity and quantity, cooldown, projectile speed, area, curse and flask effect, reflect, charge durations, cannot-be/immune flags; the main hand / off hand rows add that weapon's local stats; projectile block and chaos resistance against damage over time combine the pools with their stat. The rest of the 387 rows is tracked in issue #75 | `PanelStats.cs`, Gateway (`CharacterPlayerStats`, `HandLocalStats`) | [11](docs/protocol/11-skills.md) §14 |
| Passive masteries: S→C 0x33 carries selected effects (`node \| effect << 16`) and C→S 0x35 selects the effect HASH16; a mastery node can be allocated normally and its effect selected/changed without spending another point. Block Mastery's four effects are implemented: max attack block +2, max spell block +2, 20 life + 20 mana on block, and +1% spell block per 5% attack block; the effects are saved with the character | `PassiveTree.cs`, `CharacterSaves.cs`, Gateway (`ChangePassives`, `PassivesMessage`) | [15](docs/protocol/15-progression.md) §2 |
| Loot: dying monsters drop currency, flasks and equipment (our own table) around the corpse | `ItemDrops.cs`, Gateway (`DropLoot`) | [15](docs/protocol/15-progression.md) §3 |
| Buffs (S→C 0x1c1 add, 0x1c2 remove) and Righteous Fire: cast on oneself, its buffs and the big burning circle (an effect object, `Metadata/Effects/PermanentServerEffect` with `rifire.ao`, following the player), 90% of life per second nonlethal burn on the player, 70% of life per second to monsters within its radius (18 cells, grown by the player's area of effect stats, `SkillArea`; they burn to death), the circle scaled to that radius (MiscAnimated's reference radius, the uniform Positioned scale; matches the real game at level 1) and kept on the player every 100 ms; server-side mana (cost, regeneration); `/god` (also 200% increased area of effect), `/set_aoe` | Gateway (`ActivateRighteousFire`, `RunPlayerTick`), `SkillArea.cs` | [11](docs/protocol/11-skills.md) §12 |
| Spells are released at the cast animation's contact keyframe (Witch 599 of 1066 ms; every class's from its rig .amd), sped up by cast speed | `AnimationContacts.cs`, `scripts/re/anim_contacts.py` | [11](docs/protocol/11-skills.md) §9 |

## Partly working

- **Stable gem skill ids (tested, not yet checked with the real client).** Each gem keeps its skill id while it stays
  socketed (a gem moved through the cursor keeps its own; a gem swapped in never takes the old gem's id), so skill bar keys no longer move when other gems come or go; a skill that goes
  away ends its aura or Righteous Fire; one aura of a kind runs at a time (`GemSkillIds.cs`; [19](docs/protocol/19-gems.md) §3).
- **Gem levels (tested, level 1 → 2 checked with the real client).** A gem's level comes from its experience up to its
  experience type's cap; its skill uses that level's cost, damage, reservation and cooldown, and its quality stats;
  transfigured and Trarthan gems grant their own skill, Vaal gems their Vaal skill and their base skill (refused without
  Vaal souls, which are not gathered yet); the variant is sent in the SkillGem data and saved; `/gem <name> [level]
  [quality]` (`ItemCatalog.cs`, `SkillCatalog.cs`; [19](docs/protocol/19-gems.md) §1, §3). Not yet: supports, corruption.
- **Gem requirements (tested, checked with the real client).** A gem's level requirement is the
  character level of its level (GrantedEffectStatSetsPerLevel `PlayerLevelReq`; Haste 24 at gem level 1 . 70 at 20, the
  client's tooltips), and its attribute requirements follow the client's own formula (`0x14246fc40`:
  `trunc((3 * level_req + 11) * (percent/100)^0.9 * k + 0.5)`, `k` 0.7 for a skill and 0.5 for a support, none below 14;
  fitted against 570 poedb tooltip cells with no mismatch: Haste 20 is 155 Dex, Clarity 20 119 Int, Fireball 20 155).
  Socketing a gem the character is too low or too weak for is refused (0x30, `GemRequirementsUnmet`), and a gem its
  requirements above the character's makes its item inactive, as a worn item the character is too low for is:
  the client reds it ("Requires level 70"), the item's mods stop counting and its skills leave the Actor list, so there
  is nothing to cast; a use that arrives anyway is refused without the action (no 0x1ab, and the mana pool again so the
  client's predicted cost comes back; `GemDisabled`). An inactive main-hand weapon is ignored: the default attack falls
  back to the class's unarmed hit, as if it were not worn. Checked live 2026-10-23 with a level 20 Fireball wand at
  character level 10, and its attributes with `/set_stat intelligence 200` and then 130 (155 needed: the client reds
  the gem, the wand's mods stop counting, a use is refused). In play the gem never gets there: its experience stalls
  at the first level the character does not meet (`SkillCatalog.MaxLevelAt` in `GainGemExperience`, the game's rule; the
  client derives levels from experience alone and would not stall by itself), and the level change re-sends the skills.
  Not yet: supports don't stop applying when a gem is disabled, and a disabled gem's aura keeps running
  ([19](docs/protocol/19-gems.md) 1, 3).
- **Gem experience from kills (one gem in the first weapon set checked with the real client).** Every gem socketed in an
  equipped item, in either weapon set, gains 10% of a kill's monster experience (support gems included; inactive items,
  gems at their last level and corrupted gems over their cap nothing; quest levels and `/add_xp` give the gems nothing),
  sent with the kill as 0x00bf item changes, each item once, so the XP bars follow, and saved with the character
  (`GainGemExperience`, `PlayerInventories.ChangeSocketedGems`; [19](docs/protocol/19-gems.md) §1). A kill's share stops
  one point below the next threshold, where the client shows the "+": it does not level a gem by itself (checked live
  2026-10-23). Not checked live yet: several gems in one item, the second weapon set.
- **Gem level-up and the lock (checked with the real client).** Clicking the "+" sends C→S 0x31 (inventory, item, socket;
  14 bytes); the server writes the next threshold into the gem, replaces the item (0xbf) and re-sends the skills (0x02ba
  Actor), so the client derives the new level and its numbers at once, and refuses a gem at its last level, a locked one,
  or one whose next level's requirements the character does not meet (`LevelUpGem`; the client's "+" check does not test
  them, so the server answers with the missing level or attribute). A right-click sends C→S 0xd7 (the
  same three u32s and a u8 lock flag; 15 bytes), kept in the SkillGem flags bit 2 (`Item.GemFlags`) and saved, so the
  lock survives a login; the client greys the "+" while it is set (`LockGem`). Checked live 2026-10-23: level 1 → 2,
  right-click, relogin, unlock, level again; a level-up refused at character level 31 (Fireball level 10 needs 32) and
  with 70 Int (needs 75); `/gem_xp <amount|near>` sets the experience. Not checked: a gem in the inventory (not
  socketed), which sent nothing in the captures.
- **Saves in SQLite (tested, not yet checked with the real client).** Accounts, characters, character saves and stashes
  are rows in `data/saves.db` (WAL, synchronous=FULL); a stash is a header row and one row per tab. Every item, stash,
  gold, passive, quest flag and waypoint change is saved at once in a transaction. Stash moves, purchases, sales,
  currency used on items, gold respec, the map device and character deletion save all their parts in one transaction or
  none; a failed save rolls back, reloads what it changed and closes the connection. Experience and kill flask refills
  are saved every 5 s, at a level-up and at disconnect. Offered sale items keep their inventory cells, as 3.29's greyed
  imprints. The level comes from the saved experience. Unknown save fields are kept; a save holding items the game data
  doesn't know stops the login. JSON saves and accounts.json are imported once (`Saves.cs`, `ItemMoves.cs`,
  `GameServer.Storage/SaveDatabase.cs`, `SqliteAccountStore.cs`; [saves](docs/saves.md)).

- **Instant skills and cooldowns (verified live 2026-10-21).** Haste, the Purities and Righteous Fire carry
  `skill_is_instant`, so the client sends them as C→S 0x191 and keeps walking; the server applies them and answers
  S→C 0x1ce, which starts the skill bar's cooldown. Skill records carry the GEPL Cooldown and the stats the cooldown
  count, and the server refuses a skill until its cooldown is over. The skill bar keeps its slots when skills are
  re-sent or a gem is socketed again (the saved bar goes out as 0x53) ([11](docs/protocol/11-skills.md) §9, §11).
- **Jeweller's Orb: initial local implementation verified live (2026-10-21).** Normal use and Shift share the currency
  handler; rerolls counts 1/2/3/4/6, RGB colours and link groups, limited by class, dimensions and item level.
  Socketed gems, unsupported targets and maximum base capacity are refused without spending an orb. The live-found
  refusal of level-1 boots/gloves with two sockets is fixed and verified live. Probabilities are
  approximate; quality and special socket overrides are not modelled. See [13](docs/protocol/13-items.md),
  "Jeweller's Orb".
- **Orb of Alchemy, Chaos Orb, Divine Orb (verified live, 2026-10-21):** Alchemy makes a normal item an identified
  rare with 4-6 random affixes and a rare name; Chaos rerolls all of a rare item's affixes (4-6, new tiers); Divine
  keeps the affixes, tiers and order and rerolls only values. Shift works for all three. See
  [13](docs/protocol/13-items.md), "Orb of Alchemy", "Chaos Orb", "Divine Orb".
- **Orb of Scouring (verified live, 2026-10-21):** a magic / rare / unique item becomes normal, only base mods stay.
  See [13](docs/protocol/13-items.md), "Orb of Scouring".
- **Orb of Transmutation (verified live, 2026-10-21):** a normal item becomes magic with 1-2 random affixes.
  See [13](docs/protocol/13-items.md), "Orb of Transmutation".
- **Orb of Alteration (verified live, 2026-10-21):** a magic item gets 1-2 new affixes (one prefix, one suffix at most).
  See [13](docs/protocol/13-items.md), "Orb of Alteration".
- **Orb of Augmentation (verified live, 2026-10-21):** +1 affix on a magic item, up to one prefix and one suffix.
  See [13](docs/protocol/13-items.md), "Orb of Augmentation".
- **Regal Orb (verified live, 2026-10-21):** magic → rare, affixes kept, +1 affix, rare name.
  See [13](docs/protocol/13-items.md), "Regal Orb".
- **Exalted Orb (verified live, 2026-10-21):** +1 affix on a rare item, up to 6 (three of each kind).
  See [13](docs/protocol/13-items.md), "Exalted Orb".
- **Orb of Annulment (verified live, 2026-10-21):** removes one random affix from a magic or rare item.
  See [13](docs/protocol/13-items.md), "Orb of Annulment".
- **Armourer's Scrap (verified live, 2026-10-21):** +1% quality on armour, up to 20%; Scouring resets it to 0.
  See [13](docs/protocol/13-items.md), "Armourer's Scrap".
- **Blacksmith's Whetstone (verified live, 2026-10-21):** +1% quality on weapons, up to 20%; Scouring resets it.
  See [13](docs/protocol/13-items.md), "Blacksmith's Whetstone".
- **Orb of Chance (verified live, 2026-10-21):** a normal weapon / armour piece → rare (30%, 4-6 affixes) or magic
  (2-4 affixes). See [13](docs/protocol/13-items.md), "Orb of Chance".
- **Vaal Orb and corruption (verified live, 2026-10-21):** weapons, armour and jewellery are always corrupted
  (Base +0xc7 bit 0, saved); normal / magic → rare with 4-6 affixes, a rare item gets 1-5 or all affixes rerolled; a
  corrupted item takes no currency but Wisdom. See [13](docs/protocol/13-items.md), "Vaal Orb and corruption".
- **Glassblower's Bauble (verified live, 2026-10-21):** +1% flask quality up to 20%; Scouring resets it.
  See [13](docs/protocol/13-items.md), "Glassblower's Bauble".
- **Fracturing Orb (verified live, 2026-10-21):** one random mod of a rare item with 4+ mods is fractured and kept by
  every other currency, Scouring included; shown fractured (magic properties u8 +0x7a). See
  [13](docs/protocol/13-items.md), "Fracturing Orb".
- **Mirror of Kalandra (verified live, 2026-10-21):** a mirrored copy (Mods flag bit 1) of a non-corrupted,
  non-mirrored item goes onto the cursor; mirrored items take no currency. See [13](docs/protocol/13-items.md),
  "Mirror of Kalandra".
- **Niko and Kirac in the hideout (verified live, 2026-10-21):** Niko to Nessa's right, Kirac to her left. Kirac's
  hideout row has no dialogue of its own and uses the main Kirac's (`NpcCatalog.TalkSource`); Purchase Items sells maps.
  Niko's Delve options need quest flag ThisIsMyHideout (sent in hideouts and the Mine Encampment) and league quest flag
  68 DelveEncounterComplete (every character): NPCTalk columns 15-18 are league quest flag ids. See
  [21](docs/protocol/21-hideouts.md).
- **Delve, first steps (verified live, 2026-10-21):** View Subterranean Chart opens the chart (S→C 0x7b kind 0x1c, from
  the client's screen table); its data request C→S 0x23f is answered with S→C 0x240, and one entry draws the first mine
  section (the other fields are being worked out with `/delve_chart`). Visit Mine Encampment goes to Delve_Hub, which
  has its tile's waypoint (still drawn inactive: no 0x4c bit), Niko, the stash and the guild stash. The mine itself is
  not implemented. See [21](docs/protocol/21-hideouts.md).
- **Nessa's shop tabs (2026-10-21; the gem tabs verified live, the maps and the tabbed window flag being tested):** Goods (her stock, one Beach Map, then every map of tiers 1-16)
  and Goods2 for the rest, then every shop gem in Red / Green / Blue Skills and Support and White Skills, 1 Scroll of
  Wisdom each, filled row by row. Shops send one inventory per tab and their names in 0x8c. See
  [17](docs/protocol/17-npcs.md) §5.
- **Orb of Fusing: verified live (2026-10-21).** Captured 0x71/0x72 packets feed the same
  handler. Only links change; sockets, colours, gems and item properties stay intact. Refuses fewer than two sockets
  and protects fully linked 5/6-socket items; 2-4 sockets remain rerollable. Quality favours longer groups without
  being consumed. Link/quality probabilities are approximate, and special overrides/corruption are not modelled.
  See [13](docs/protocol/13-items.md), "Orb of Fusing".

- **Auras (the simple set is verified live).** Reservation is read from GrantedEffectsPerLevel
  (`ManaReservationFlat` / `ManaReservationPercent`), reduced by the passive/mastery reservation efficiency and sent as
  the Life pool's reserved mana (and in every 0x1b1 mana update). Aura skills toggle on cast; Purity of
  Elements/Fire/Ice/Lightning, Clarity, Vitality, Precision and Haste apply their gem's level and quality effects and
  buff icons; their 3D effects (`affected_player.epk`, `faster_attack.epk` …) do not render yet.
  The skill-bar active border is not reproduced (0x25f is an internal UI event, not an instance/world message;
  sending it crashed the client). Not modelled: combat auras (Anger/Hatred/Wrath/Zealotry/Pride), Heralds, banners,
  curse auras, Determination/Grace/Discipline (the base armour/evasion model exists, but their buffs and energy shield
  are not applied), Purity ailment immunity, Reservation
  masteries 1/2/6, supports, party/minion auras and life reservation
  ([11](docs/protocol/11-skills.md) 13).
- **Positions are estimated, not simulated.** The Gateway estimates where the player is from clicks, walk speed and the
  walk map; there is no server-side pathing or collision.
- **Stats.** The client's stat key is the Stats.dat row + 1 with the vendored schema ([11](docs/protocol/11-skills.md)
  §8); `/set_stat` takes stat names or keys. The player's life, mana, accuracy, armour and evasion come from its class
  and level, with the attributes' bonuses (energy shield, evasion and melee physical damage increases); accuracy also
  takes its flat/increased passives, weapon-specific stats and Accuracy Mastery, and the final value is sent as
  `main_hand_accuracy_rating` / `off_hand_accuracy_rating` for the Offence panel; armour and evasion also take their
  flat/increased passives and the base equipped armour pieces (body, helmet, gloves, boots), while life and mana still
  ignore passives and items (attack speed is handled below) ([11](docs/protocol/11-skills.md) §4).
- **Attack speed passives.** Generic, melee, one-/two-handed, weapon-family, shield and dual-wield passive attack speed
  is summed into the client's `main_hand_attack_speed_+%`; dual wielding adds PoB's 10% more and the server's unarmed
  attack uses the same final value. The default attack also sends the panel's `hundred_times_damage_per_second` and
  `display_hundred_times_damage_per_skill_use`. Live verified: the dual-wield character's APS follows the passives
  (2.93 → 2.98 after a node). Minion, totem and timed/conditional stats are not modelled; the character panel's APS
  stays cached after a passive allocation (reopening the panel or switching its tab does not refresh it; only a relog
  does). ([11](docs/protocol/11-skills.md) §4)
- **Critical strikes.** The server rolls unconditional crit passives: the equipped main hand's `WeaponTypes.dat`
  critical chance (permyriad) raised by generic/attack/melee/weapon-shape increased crit chance for attacks, Fireball's
  5% base for spells, and 150% plus `base_critical_strike_multiplier_+`; critical damage is multiplied and logged.
  Unarmed has 0% base crit. The final main-hand and spell crit chance/multiplier stats go to the client for the
  Offence panel; timed, charge and enemy-state crit stats are not modelled; `/crit` shows both final values
  ([11](docs/protocol/11-skills.md) §5).
- **Spawn points.** Logins, Resurrect in Town and one-way entries other than The Twilight Strand → Lioneye's Watch
  use the area's spawn: the town's fixed point, or the walkable cell farthest from any edge in a generated area.
- **Area seeds.** Non-town areas use seed 1 unless `Game_RANDOM_SEEDS=1`; each new seed needs its own walk map. The
  port of the client's level generator has started ([re/011](docs/re/011-level-generation.md)): the RNG, the tile hash,
  the extra-terrain-feature rolls and the topology pick match the live client, and Lioneye's Watch generates tile for
  tile (`scripts/re/levelgen generate 1_1_town 1`), and so does The Twilight Strand at seed 1: tile graphs (`.tgr`)
  are ported from the Voronoi jitter through edge tiling, vertex grounds, overlays, fill, mirror seams and the outer
  buffer (all 3 276 entries, tile hash 266153788, all 3 772 tile-stage draws; `levelgen generate 1_1_1 1`). Other
  seeds and areas still reach unported cases (re/011 §3g: overlay pass 2, the client's seed retry, the height levels;
  The Coast seed 1 matches tile for tile and by tile hash (695393902), with Vaal side area entrances, the room
  position search, the height levels, the level painting, room vertex grounds, the walkability pass, post-unique
  tiles, the storeys stage, outer-buffer heights and the StoreyHeight scaling ported; The Northern Forest seed 1 also
  matches by hash (2500166236); three Coast seeds, The Tidal Island, The Mud
  Flats, The Northern Forest and The City of Sarn among them); multi-node `.dgr` dungeons are ported (The Ship Graveyard Cave seed 1
  and the Prison `1_1_7_1` seeds 1, 1898277627, 4162278830 match by tile hash, re/011 §3c2: untagged nodes, special
  rooms, door tiles, the outer ground's height fixing and the outer buffer; composite maps of `.dgr` subgraphs too:
  the Prison's second level `1_1_7_2` seeds 1, 2512127042 match, re/011 §3c3; `.tgr` subgraphs on the shared map and
  the extra room pool's mission rooms too: MapWorldsGorge seed 1 matches tile for tile (3320701478); The
  Fetid Pool `1_1_3a` seed 1 matches tile for tile (401409093) with door types with a direction, fill rooms, slot
  corners and 3 × 3 outer tiles; The Slave Pens `1_5_1` seed 1 by hash (3415378844) with the node door types and the
  overlap tile fallbacks; The Reliquary `1_5_7`, The Ossuary `1_5_5` and eight more seed-1 traces since the plain
  room slots, room grounds, seam tests, `s`-keyed fill rooms and dungeon post-unique tiles, and `2_10_2`, `2_10_5`,
  `2_10_6`, `2_9_10_2` since the dungeon seams' edge-point fallback, MapWorldsGorge and MapWorldsToxicSewer since the
  `.tgr` subgraphs and mission rooms, `1_4_2`, `1_4_6_*`, `2_7_8`, `2_8_10` and `2_9_10_1` since the shrine rooms,
  the sub-area records (a rolled discoverable hideout's claim area beside the map: `2_8_5` matches every draw up to
  its stop), the shuffle and unique node groups and the template nodes (`1_3_18_1` likewise), and since the Labyrinth
  pass (secret nodes dropped, gold door, boss overrides; `LabyrinthPass.cs`) and the unmirrored spawner tiles 27 of the
  41 Labyrinth traces plus `1_2_1`, `1_3_6`, `2_6_10`, `2_6_14`, `2_7_1` and others: 91 of the 197 seed-1 traces;
  the oracle matches 143 of 710 logged seeds; the Labyrinth's final-boss areas of difficulty 2+ (FinalBoss pick
  `0x1424e5ee0`) and the EndGame non-boss rooms are open), the
  one-node `.dgr` path (hideouts) lacks the outer buffer, and the Gateway does not use the port yet. The walkability
  and projectile grids are built from the generated tiles and doodads (re/011 §6b / §6c, `Walkability.cs`,
  `DoodadStage.cs`, `DoodadHull.cs`, `levelgen walk`): the doodad stage places room doodads, the .ddt's random and
  vertex doodads, then every placed tile's own objects (its `.tmd`), and stamps the Hull of each static one's `.ao`
  as it goes; which room and tile lines are client objects follows their type's BaseEvents flags. The rooms a dungeon's
  fill picks from its `.gft` (`Fills/plants*.arm`, `tent1.arm`: forced-blank tiles with doodad lines) count among the
  record's rooms. On the 61 seed-1 captures whose tile hash matches, all 61 areas now match the client's walk and
  projectile grids cell for cell (The Twilight Strand, The Coast, The Prison, the dungeons with forced-blank slots such
  as `1_5_2` and `2_10_2`, The Dried Lake `1_4_2`, Chayula's monastery, …). The Twilight Strand matches every
  doodad-stage draw and places all 250 captured doodads. AOSet doodads (`1_1_6`) and the objects of reskinned tiles
  (`2_9_5`) are in. A fill room's own `.arm` vertex grounds now go in, so `2_8_10` matches every doodad-stage draw and
  places all 1 083 captured doodads; its grids match since the outer buffer flags its vertices unwalkable (vertex
  flag 8, as the client's spread does). Each tile's second objects file (the `.tmd` its `.tdt` names, tile +0x180)
  is placed too, so the Prison areas (`1_1_7_1`, `1_1_7_2`, `2_6_7_1`) place all their captured doodads (their
  archways). Tiles made of sub-tiles place each sub-tile's objects (ported from the code; no capture has such a
  tile). `DoodadNoBlocking` objects are created (no collision), text `.tmd` objects sit at their own cells, and a
  reskinned tile's objects keep the reskin's name unless the original `.ao` exists: 72 of the 117 areas the stage
  reaches now place every captured doodad (was 56), and `2_10_6` matches every doodad-stage draw (the client's
  animation-start draws there only touch the LCG).
- **Projectiles.** Areas without a height grid (`scripts/re/frida/capture-terrain-tiles.js` + `scripts/walkmaps/heights.py`) fly the fireball on the terrain; it hits the first monster or wall on its path (checked at launch; walls from the
  projectile grid, Lioneye's Watch and The Coast seed 1) and explodes there, but doodads with their own colliders do not
  stop it ([11](docs/protocol/11-skills.md) §10).
- **Combat.** The player's default attack kills monsters with server-side damage (no monster armour or resistances
  yet; the contact point is half the attack time, not the animation's). Monsters use their first granted effect and
  swing back; their attacks roll player evasion and then go through armour and block. The player's life comes from the
  class/level/passive pools. The server has no simulation loop: hits use timers, attacks are bounded by a 30/s tick
  (`TickRate`), the AI runs every 500 ms. Item mods count through the global item stats; "emerge" monsters stay hidden;
  346 varieties with NPC / ControlZone / ... components are refused.
- **Damage model (foundation, with armour/evasion).** A hit is a per-type `DamagePacket`; `DamageConversion` handles
  the game's physical, lightning, cold, fire, chaos order, chained conversion, skill/global precedence, the 100% cap
  and gain-as-extra. DoT is a single type with fractional tick carry, and `Ailments` has the ignite/bleed/poison base
  formulas. The default attack, Fireball and monster melee build packets and read the player's/skill's conversion
  stats; packet mitigation applies each type's own resistance, and the physical part then goes through armour.
  Righteous Fire's burn uses the DoT tick model. Not yet: energy shield, penetration/exposure, added damage,
  increased/more support modifiers, ailment application/duration and monster resistances
  ([20](docs/protocol/20-damage.md)).

## Not working yet

- Area monsters: pack counts and magic/rare chances are our own numbers; rarity adds its life (MonsterMagic2/Rare2/Unique2 `monster_life_+%_final_from_rarity`) but no other mods; Drowned ignore their ground types; emerge packs (Gravel Eaters) are skipped; the boss life bar and boss stages exist for Hillock only (`BossFights.cs`; other bosses with `BossHealthBar` get the bar too); monsters respawn on every entry; a new (area, seed) needs a `-Walkmaps` visit for its unique boss ([14](docs/protocol/14-monster-packs.md) §4).
- NPC dialogue options, shops, NPCs in other towns and outside towns ([17](docs/protocol/17-npcs.md) "Open"); the 346
  monster varieties with NPC, ControlZone, BlightTower … components. "Emerge" monsters (Gravel Eater …)
  are created but stay hidden until an Emerge action, which the server does not send.
- Chat lines: server text only appears as a message box. The chat-line message and the global channel join are unknown
  (the client logs "Timeout while joining global chat channel").
- Area transitions of a generated area need a `-Walkmaps` capture for its seed; quest-gated ones are created but the
  way stays blocked: The Mud Flats' Submerged Passage has a client-side wall until quest flag `a1q4-PlacedGlyphs`.
  Quest objects with extra components are sent as plain AreaTransition, so The Twilight Strand's gate appears without
  its animation; the Doorman and the opening tutorial's wall are not created ([12](docs/protocol/12-quests.md) §6).
  Only quest a1q1 is scripted; no quest log.
- Uniques and special mods (#60), items' base damage and defences; currency other than Scroll of Wisdom, Chromatic Orb, Jeweller's Orb, Orb of Fusing, Orb of Alchemy, Chaos Orb, Divine Orb, Orb of Scouring, Orb of Transmutation, Orb of Alteration, Orb of Augmentation, Regal Orb, Exalted Orb, Orb of Annulment, Armourer's Scrap, Blacksmith's Whetstone, Orb of Chance, Vaal Orb, Glassblower's Bauble, Fracturing Orb, Mirror of Kalandra, Gemcutter's Prism and Orb of Regret; utility flasks; support gems and links
  ([13](docs/protocol/13-items.md) "Open").
- The client's lockstep network mode (also what "auto" picks on localhost): the player is drawn (the Gateway sends
  server frames) but cannot move or use objects. Only Predictive is supported; the Gateway asks for it once per
  connection ([07](docs/protocol/07-latency-visibility-movement.md) §6).
- Waypoint travel to a generated area without a `-Walkmaps` capture for its seed puts the player at an arbitrary cell
  (The Submerged Passage, seed 1); quest flags are derived from visited areas and waypoints, not from quests
  ([16](docs/protocol/16-waypoints.md) "Open").
- Hideouts: no decorations (trees, tents …), editing does nothing, logins never start in the hideout, only the Lush Hideout ([21](docs/protocol/21-hideouts.md) "Open").
- Map device: a map without a walk-map capture puts the player at an arbitrary cell (Beach is captured); the arena gates
  land next to their pair, not on the telepads; fragments add nothing; the portals look like player portals; the open map is lost on a
  Gateway restart ([21](docs/protocol/21-hideouts.md) §5, "Open").
- Stash: a switch to an already loaded tab sends nothing, so it is missed if the player logs out without reopening the stash or moving an item; the special tabs' slot rules, tab folders (the tab order, names and colours are kept, 2026-10-22), real guilds for the guild stash, per-league stashes and stashes in other towns ([18](docs/protocol/18-stash.md) "Open").
- Saving quest progress. Quest passive points, quest refund points and ascendancy ([15](docs/protocol/15-progression.md) "Open").
- Effects that should be attached to bones (Righteous Fire's orbs, MiscAnimated RighteousFireOrb) are not sent; an
  effect object follows the player only by 0x1ba glides. Combat auras, Heralds and banners are not implemented; only the
  simple auras above use buffs. Ruthless and SSF change nothing in game; towns are shared across leagues.

## Tests

`dotnet test tests/GameServer.Core.Tests` (378 tests), `tests/GameServer.Data.Tests` (15) and
`tests/GameServer.Gateway.Tests` (376: currency on items (from the inventory or a stash tab), the currency tab's crafting slot, gem levels, gem requirements (level and attributes) and the level stall, the level-up stopped between two levels, the gem level-up and lock messages and the saved lock flag, quality and variants, stable gem skill ids, the save database and its transactions, stash tab rules, Ctrl + click moves, item moves with failing saves, inventory moves, stacks, the start weapon on the ground, flasks, arrival points,
instance ground items and monsters, quests, passives, spell suppression, attack speed, critical strikes, accuracy,
armour/evasion, item mods, auras, damage conversion and the account stash) pass.
Tests that need the real client pass quietly without `Client/`.

## Tooling

Everything used to work out the protocol is in the repository: the Ghidra, Frida and Python scripts in `scripts/re/`, our
own tools in `Tools/`, and how to install the third-party ones in [GettingStarted](GettingStarted.md) ("Third-party
tools"). The full reverse-engineering setup has not yet been rebuilt from a fresh clone; if something is missing, fix the
guide.

## Doc caveats

`docs/re/003`–`004` are early notes with known errors (for example "login is HTTPS"; it is custom TCP, see `re/007`). Treat them as history, not reference.

## Jeweller's Orb (2026-10-21)

Initial implementation verified live: normal and Shift use rerolls empty sockets, colours and links, consumes one
orb, updates inventory and saves the result. Class, dimensions and item level limit socket rolls; only base
capacity blocks further use. Local counts are 1/2/3/4/6. Probabilities are approximate; quality, white-socket
3.29 rules and special item overrides remain open. See protocol/13-items.md, Jeweller's Orb.

PR branch validation: build succeeded with zero warnings/errors; Core 307, Data 15, Gateway 210 tests passed.
Gateway test artifacts were placed in the local workspace so the existing extracted data could be read.
