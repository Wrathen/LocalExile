# Status: Skills, auras, stats and combat

Part of [Status](../Status.md); what to build next in this area is in
[roadmap/skills-and-combat.md](../roadmap/skills-and-combat.md).

## Works (verified with the real client)

- Fortification: qualifying melee hits build temporary protection through an explicit LocalExile generation model.
  Native checks confirmed the buff component, expiry, 20/30-stack limits, attack speed and actual hit protection.
  Elapsed Life regeneration, stopping at expiry, area carry, death and relog were also verified.
  The evidence uses a new unascended character with raw inputs; it does not establish Behemoth selection.
  See [Fortification](../docs/game/fortification.md) for the formula, source coverage and remaining extensions.
- Antiquarian's real selected nodes now drive Ward absorption/restoration and Runic Tablet's pausable Adrenaline.
  Goliath's zero Intelligence reaches requirements and pools; Whaletooth extends actual Enduring Cry recovery.
  Turtle Shell integrates 8% Life over one second after accepted melee stun, with native Life correction at expiry.
  Lioneye's Hair bypasses supported attack accuracy, Bow scales actual travelled projectiles, and Shield replaces
  usable-shield base block and reduces physical ranged hits after armour.
  Three native builds cover all nodes, combat controls, paid refunds and effect/save lifecycles.
  See [Antiquarian](../docs/game/antiquarian.md) for evidence, exact graph admission and remaining engine boundaries.
- Conditional critical policy (#242): ordinary, low-life, Focus and Sharp and Brittle sources share a signed clamp.
  Opposing luck cancels, and multiple lucky sources still add at most one critical draw.
  The unarmed multiplier applies only to the active empty-hand melee attack, with matching panel values.
  Fifteen native checks verified real 200/150 critical hits, active-set switching and bonus removal.
  A 66-Life Marauder verified above, exactly at and below the inclusive 50% boundary.
  Native virtuals confirmed cancellation, clamping, unlucky state, cannot-crit, healing, death and relog.
  Native evidence targets `ea24316`; the resolved `effb797` composition has automated coverage and awaits native reruns.
  The checks use raw inputs on an unascended character; spell/bow contact rules and RNG order have automated coverage.
  A parsed 200-damage critical hit against 400 Life generates 14 Fortification from the same resolved multiplier.
  Removing the unarmed bonus gives 150 damage and 12 stacks; cannot-crit gives 100 damage and 10 stacks.
  The issue #75 review now shares one critical roll across a gem use's area targets, volley, pierce/return hits
  and existing impact callbacks. Gem luck is captured at admitted use; each hit samples current chance/prohibition
  and confirms attack accuracy independently. Twenty-two new cases and four updated parsed use-time-luck controls
  cover the changed lifecycle. This correction is automated, not part of the older native captures.
  Ordinary melee/object-bow now capture luck at swing admission after approach, and ground bows at their input
  admission. Twenty added controls cover Life/Focus/raw changes before confirmation, plus walk-in timing;
  eight existing signed-source/released-prohibition cases now assert admission luck. The other ordinary-bow
  snapshots, special repeats and native reruns remain open.
  Successful paid ascendancy refunds and Focus/curse lifecycles remain separate integration work.
  See [11-skills.md](../docs/protocol/11-skills.md), conditional critical policy.

- Generic Rage (#281): source-qualified attack/melee/critical and incoming gains share a half-second interval.
  Maximum, effect, separate delay recovery/loss speed, prevention and final attack multipliers are consumed.
  Seventeen native checks verify raw-source generation, installed buff/count, actual damage and unchanged speed.
  Fixed 100 damage at 15 Rage displays and deals 115; prevention, decay, pause, travel, death and relog pass.
  The checks use an unascended Witch; Behemoth selection and Vaal consumers remain separate.
  Supported contacts, area-sweep deduplication, queued refreshes and the 140-damage/11-Fortification composition
  have automated coverage; further native composition checks remain in [26-rage.md](../docs/protocol/26-rage.md).
  Capped attempted gains and frozen destination-loading time use explicit LocalExile policy v1.
- Behemoth combat has historical selected-class native acceptance at `9657e0ad` in legal eight-point builds.
  Integration over main `c748f5fa` has automated checks only; native acceptance has not been repeated.
  All 13 Rampage plans execute real owned attacks/projectiles, corpse effects, blind, stun and animated weapons.
  Fortification, Rage, critical policy, mana conversion and both armour defences compose with those builds.
  Vaal Ground Slam has atomic Rage payment, a pausable Soul Gain Prevention countdown and explicit refusal gates.
  Historical native keyboard use, pause, death, refund, travel, relog and Gateway restart passed.
  Other Vaal executors remain refused; geometry/timing and Fortification generation use documented server models.
  See the [whole-class audit and evidence](../docs/game/phrecia-class-completion.md).

- Mana per final Strength (#287): `X_mana_per_4_strength` joins the ordinary flat mana bucket before increases.
  Thirteen native controls verify complete four-Strength groups, signed inputs, source removal and relog.
  A fresh Witch's 56 mana becomes 80 at 14/15 Strength, 88 at 16 Strength and 110 with 25% increased mana.
  Maximum-mana attack lightning follows the final pool; display-only Strength overrides cannot feed it back.
  Idle aura reservation and current-mana clamps have decrypted command-wire coverage, including the send gate.
  Historical native controls use raw inputs and synthetic gear; Phrecia selection remains gated.
  The full Mental Conditioning node now composes with Power of Purpose in automated tests; native reruns remain pending.
  See [11](../docs/protocol/11-skills.md) section 4.

- Player Actor scale (#299): passive, active item, aura, flask and cosmetic size inputs share final1973.
  Metadata limit100 adds before ordinary limits, then explicit development replacements, including zero.
  Eighteen native checks measured real Positioned and interpolated Render scale on a new unascended Witch.
  Item20 plus cosmetic20 plus cosmetic3 produces scale1.43; removing sources restores1.0.
  Area entry, relog, death/revival, the default cap and explicit zero overrides were verified.
  Raw720 alone leaves the native model unchanged; server-derived final1973 drives its scale observer.
  Native object size, type scale and stretches remain unchanged; geometry consumers have automated coverage.
  Current/late peer change, reset, creation and revival have loopback-wire coverage, not a second-client visual check.
  Phrecia admission, Rampage and the separate native melee-range flag9807 remain separate work.
  See [04](../docs/protocol/04-player-entity.md#41a-player-actor-scale-299) for formula and evidence limits.

- Armour against chaos hits (#288): the boolean `base_armour_applies_to_chaos_damage` enables the existing armour
  model after chaos resistance, with the ordinary 90% limit.
  Eleven native controls verify actual player Life loss, source toggles, resistance ordering and relog.
  Extra physical/elemental reductions stay with their own types; the native final Armour rating is unchanged.
  Mixed monster packets, block, suppression, ES, Fortification and DoT separation have automated coverage.
  Native controls use a raw source on an unascended Marauder; Incorruptible selection and partial armour
  percentages remain separate.
  Recent-hit doubling composes in automated tests and fresh ordered joint native controls at `c9cd1864`.
  See [20](../docs/protocol/20-damage.md) section 2e.

- Increased attributes (#285): typed and all-attribute percentages scale the complete class/passive/active-gear
  flat bucket once, using the documented PoB rounding model.
  Historical evidence at `60044ad` has thirteen native controls for 3% Strength, additive increases, derived pools and
  matching requirement values on an unascended Marauder.
  A Plate Vest stops contributing below its requirement and resumes when the final attributes recover.
  Calculated zero attributes stay zero; source clearing and relog restore the expected base values.
  Own-item percentage retention, cascading activation, passive refunds and weapon swaps have automated coverage.
  Current-main composition with mana per final Strength, inactive weapons and raw zero/off overrides has regressions.
  Eleven current-main native controls verify final Strength groups, additive increases, mana and equipment requirements.
  Maximum mana follows 47 to 111, then 127/145 as attributes rise; 100% mana increases it to 290 once.
  Source removal and the ordinary vest's requirement disable/restore pass at rebased source `00b246f2`.
  Ordered joint native validation at `c9cd1864` also verifies final attributes feeding mana-to-armour conversion.
  Odd final Intelligence keeps its half mana until converted pools round; 16 Strength / 7 Intelligence gives 15 mana.
  Inactive-weapon, cascading-equipment, passive-refund and active-aura composition remain automated-only.
  The evidence does not establish official-server fractional parity or enable Phrecia class selection.
  See [11](../docs/protocol/11-skills.md) section 4 and [13](../docs/protocol/13-items.md) section 2d.

- Power of Purpose (#291): raw `keystone_mental_conditioning` converts 80% of flat mana to twice as much armour.
  Mana and armour increases add for the converted contribution, under the documented PoB calculation model.
  Thirteen historical native controls at original head `7678d60` covered toggles, increases, rounding and relog.
  Those controls exercised actual Steel Willed attack damage and remaining-mana lightning on an unascended Witch.
  Current-main tests compose mana per final Strength, active equipment, increases, chaos-armour/CI and Mageblood.
  The rebase onto `939b44fe` adds increased-attribute and complete command-snapshot regressions.
  Final scaled Strength/Intelligence, odd half mana, independent zero/off overrides and main's bounds compose there.
  Reservation, clamps, zero/off/removal, Iron Reflexes, MORE and own-aura send-gate freshness have automated coverage.
  Historical joint validation at `c9cd1864` passed 28 native controls, including eight final-attribute conversion cases.
  B=127 gives 25 mana / 222 Armour; 100% mana gives 51 / 425; odd final Intelligence retains its half mana.
  Remaining native cases include reservation, Iron Reflexes, MORE and the full node's independent consumers.
  The candidate on `939b44fe` has no new native acceptance; the earlier controls remain historical evidence.
  Phrecia selection remains gated.
  See [the model and scope](../docs/game/power-of-purpose.md).

- Skills and combat: the player has the default attack (PlayerMelee, Characters.dat StartingMeleeSkill) in the Actor
  skill list; it goes on the skill bar, walks into range, swings at the unarmed speed (757 ms with dual wielding's
  10%) or faster with `/set_stat attack_speed_+%` (server-computed final stats, 0x2ba skill update, panel's Attacks
  per Second), and the server lowers the monster's life 2–5 at the contact point; moving cancels;
  the dead monster's life bar hides.
  A weapon whose requirements are not met leaves the hands: the model sends its slot empty, so the client greys out
  the skills its restriction needs and the default attack punches; the server refuses such a skill too and re-sends
  the model when qualification changes without an equipment move. Automated only; awaiting a native check.
  Code: `GrantedSkill`, `SkillCatalog.cs`, Gateway.
  Docs: [11](../docs/protocol/11-skills.md) §10a.
- Local combat package: stationary default melee attacks no longer take the 150 ms walk-in offset.
  Native evidence in this entry belongs to LocalExile; the standalone destination port has automated validation
  and still needs a native rerun, as recorded in the [port record](../docs/dev/ranged-monster-actions-port.md).
  An in-range unarmed attack had no approach and its authoritative hit arrived 312 ms after the action echo
  (757 ms attack, attack1 contact baseline); positive walk-ins retain compensation.
  Ordinary ground/object wand attacks create native WandProjectile bolts, apply damage at the first actual collision,
  and play the native 500 ms impact clip before removal.
  Class ACT pools and the native action-seeded selector choose the correct main-hand clip and release contact.
  Sixteen ground RMB inputs matched native Witch clips 31/36/39/42; object sequence 4113 matched clip 39.
  Release was 6.1 to 31.4 ms after the selected contact in 15 ground samples; the first cold sample was 55.6 ms late.
  Enemy flinches now cancel movement and ordinary pending swings, hold AI for the requested native TakeHit recovery,
  and invalidate old queued contacts permanently. Moving-Drowned native samples held one cell through recovery;
  AI movement resumed afterward. The original rig-only hurt animation did not stop locomotion; projectile hits
  (`CommitProjectileHit`) send the same native recovery instead of that clip.
  The faster monster planner issued its first chase after 38 to 50 ms in native controls, without duplicate orders.
  Turn tracking, full 2.4 s Drowned attack pacing and actual Escape-menu pause/resume were also verified.
  Full off-hand timing/damage and nonzero intrinsic attack repeats are not implemented.
  Code: `Program.Combat.cs`, `Program.RangedAttacks.cs`, `AnimationContacts.cs`, `AnimationVariantSelector.cs`,
  `Program.Monsters.cs`, `HitReaction.cs`.
  Evidence: `artifacts/combat-validation/`, `artifacts/wand-ai-validation/wand-native-summary.json`
  and `wand-after-object.json`; docs: [11](../docs/protocol/11-skills.md).
- Impale: ordinary melee/bow and supported projectile/area hits store the actual pre-mitigation physical roll,
  including the source critical multiplier and impale effect. Each instance lasts five later hits/eight seconds
  with captured modifiers; any accepted hit can trigger its separate reflected damage, without a second crit,
  spell-only or return multiplier. No artificial five-stack cap or clear-on-death remains. Target avoidance,
  local/attack/spell scope, real Impale Support and all three selected chance aliases have automated coverage.
  Historical 2026-10-20 native observations used the old maximum-roll implementation; current native checks,
  conditional sources and advanced application/consumption rules remain open.
  Code: `Impale.cs`, `ImpaleStats.cs`, `Program.Impale.cs`, `Program.Combat.cs`, `OrdinaryRangedShot.cs`.
  Docs: [20](../docs/protocol/20-damage.md) §3a and [11](../docs/protocol/11-skills.md) §14.
- Offence panel damage rows: the hit the server swings with becomes `main_hand_minimum/maximum_total_damage` and the
  per-type minimum/maximum rows, with `skill_uses_main_hand` so the client shows them;
  whole numbers, main hand only, and an empty packet sends none.
  The rows and the server's hit share `AttackHit`, so the maximum-mana added lightning (Whisperer "we know all")
  appears in the panel the same way.
  Fourteen real-client stat checks verified 56 mana → 3–3 lightning, 1000 → 60–60, 50% increased lightning → 90–90,
  997 → 59–59 at the panel's floor, removal and relog.
  Generic attack increases apply; melee increases follow the actual attack type and active weapon set.
  Native switching between wand and club verified ranged exclusion and melee inclusion of that increase.
  These native checks belong to original head `378ffde`.
  The resolved supported-skill path has automated context, effectiveness and spell-isolation checks.
  Recheck that composition natively.
  Code: `PanelStats.HandDamage`, Gateway (`CharacterPlayerStats`).
  Docs: [11](../docs/protocol/11-skills.md) §14.
- Whisperer "your mind is destroyed": resolved spell increases/reductions also scale attacks at 150% of their value.
  Weapon, added-damage and supported attack constructors share the fractional additive contribution.
  Native panel and target-life checks verify 100 to 250, additive 230, a half-percent endpoint of 203 and reductions.
  Removal and relog clear the effect; ordinary bows retain release snapshots and supported spells remain isolated.
  The native check uses development stat inputs; Phrecia node selection and the rest of Whisperer remain separate.
  These native results are historical raw-stat checks; expanded Arcane Might coverage is listed under Partly working.
  Code: `ArcaneMight`, `DamageStats`, `SkillCatalog.Unarmed.HitDamage`, `SupportedAttackDamage`.
  Docs: [20](../docs/protocol/20-damage.md) §2c.
- Behemoth Steel Willed: each complete group of 450 final armour adds 2% increased attack damage.
  The contribution joins raw attack increases once and feeds weapon physical, added damage and supported attacks.
  Native fixed-hit checks verify thresholds, additive 204/214, modifier removal and maximum-mana lightning.
  Weapon physical panel checks show 2–5 becoming 6–15; relog clears the development inputs.
  Native checks use raw stats on an unascended character; selection remains separate.
  Recent-hit armour defence is implemented below; current-main composition awaits native verification.
  Code: `ArmourAttackDamage`, Gateway (`CharacterPlayerStats`), `SkillCatalog.Unarmed.IncreasedDamage`.
  Docs: [20](../docs/protocol/20-damage.md) §2d.
- Steel Willed incoming-hit armour: prior accepted enemy contacts supply a four-second authoritative counter.
  The per-hit contribution caps at 30%, then the real generic double-armour chance adds before a 0..100 clamp.
  One decision supplies physical and enabled chaos mitigation; blocked and zero contacts count without armour RNG.
  Permanent armour and the outgoing attack bonus remain unchanged.
  Parsed development routes, mixed monster packets, idle expiry, pause and stale swings pass tests.
  Connection lifecycle, AI collection epochs and queued full/partial stat sends also have automated coverage.
  Rage, own-aura and Mageblood full writers acknowledge their exact encoded pair after gated construction.
  Return-to-previous-pair, queued expiry and rejected-send regressions cover these remaining full writers.
  Dirty aura prefixes and Fortification tails build in wire order across expiry, with decoded-pair/idle checks.
  Eighteen historical native checks at original head `777b292` verified 50/33 Life loss and a real Zombie hit.
  They covered unchanged 500 armour/outgoing 102, the 30% cap, additive 50%, pause/expiry and full-refresh counters.
  Death, resurrection, area change and relog clear history under explicit LocalExile model v1.
  Current-main mixed physical/chaos, charges/resists/Fortification/ES and parsed suppression regressions pass.
  Queued hits sample changed sources and final defences at contact, including lazy own-aura suspension.
  Ordered joint validation at `c9cd1864` passes 28 native controls with final attributes and mana conversion active.
  Permanent Armour 251 stays unchanged as a physical 100 hit removes 67 Life normally or 50 when doubling is certain.
  A raw40 single-contact source verifies cap30 plus generic20; full refresh and idle expiry retain generic20.
  Four-contact timing and the forced Fortification/aura/Rage/Mageblood deadline races have automated coverage.
  Class admission and suppression-to-armour chance integration remain separate work.
  See [Steel Willed armour](../docs/game/steel-willed-armour.md).

- Accuracy: the player's final rating includes flat/increased passives, weapon-specific stats and the unconditional
  Accuracy Mastery effects; attacks roll PoB's hit chance against the monster's `DefaultMonsterStats.Evasion`, misses
  send no life update, spells always hit; the Offence panel gets `main_hand_accuracy_rating` /
  `off_hand_accuracy_rating` and the Chance to Hit rows at the character's level; `/accuracy [level]`.
  The Mastery's two target-conditional effects are final multipliers applied to the roll's own target
  (`TargetAccuracy`): 40% more accuracy against Unique enemies (`SpawnedMonster.Rarity`) and 50% more against enemies
  in the constant 20-unit close range (2 metres; 10 units per metre), multiplying when both hold and never folded into
  the panel's rating or `/accuracy`'s unconditional rating.
  Ordinary, area and gem projectile attacks apply these target conditions.
  Ordinary melee/bow now share per-defender evasion entropy and a separate critical-confirmation draw.
  Bow contacts include target evasion modifiers/Blind and live target mastery conditions, retaining release-time
  owner inputs; an evaded arrow continues to later targets. Raw global/qualified local cannot-evade flags bypass
  both checks. Failed critical confirmation keeps a normal hit and normal impale storage. Rejected melee contacts
  do not consume entropy; ordinary bow block is checked after evasion. Deterministic parsed-contact, pass-through,
  source-freeze/removal and target-condition tests pass. Current native validation,
  off-hand sources/execution and complete accuracy/always-hit publication remain open.
  Unconditional selected flat/increased accuracy now joins the original unrounded pool base before the final
  floor, updating main-hand rating and normal/evasive reference chances. Real Additional Accuracy at 0/20 quality
  covers link/removal, attribute changes and unrelated skills. Raw accuracy commands rebuild pools with complete
  source replacement, including Precision and explicit zero; `off` restores sources without relog.
  Wide signed sums and exact flooring prevent intermediate overflow and fractional drift. Area attacks now
  consume the selected profile under admitted contact, including block and independent critical confirmation;
  misses/cancelled actions have no critical/impale/on-hit side effects. Fourteen added cases cover Cyclone,
  Heavy Strike, real support inputs, independent targets, bypass, spells and cancellation; 267 focused checks pass.
  Gem projectile forward/piercing/return contacts now use the same profile and independent confirmation.
  Misses pass through without spending pierce budget, triggering impacts/on-hit effects or reserving the
  target for the volley. One shared forward exclusion prevents repeated simultaneous direct hits.
  Twenty-four added cases cover actual gems/supports, return scaling, budgets, volley sharing and queued admission;
  29 focused checks pass. Gem uses now share critical rolls across their contacts. Other conditional/more accuracy, remaining projectile
  geometry/skill exceptions and native checks remain open.
  See [damage §2g](../docs/protocol/20-damage.md#2g-ordinary-attack-evasion-and-critical-confirmation).
  Ten native checks used connected ordinary paths and actual mastery selection requests.
  The panel stayed at 711 while target rolls used 995 against Unique, 1066 in close range and 1493 when both held.
  A far Unique kept only 995; target movement, relog and refund restored the expected conditions and baseline.
  Code: `CharacterStats.cs`, `TargetAccuracy.cs`, `DamageModel.cs`, `MonsterCatalog.cs`, Gateway.
  Docs: [11](../docs/protocol/11-skills.md) §4–5.
- Armour and Evasion: flat armour/evasion from the passive tree (`base_physical_damage_reduction_rating`,
  `base_evasion_rating`) and body armour/helmet/gloves/boots/active shield (the rolled `ArmourTypes.dat` bases), with
  `physical_damage_reduction_rating_+%`, `evasion_rating_+%`, `evasion_and_physical_damage_reduction_rating_+%` (Cloth
  and Chain), `global_defences_+%` and the combined increase;
  the character panel gets the final `physical_damage_reduction_rating` / `evasion_rating`;
  physical `/hit` and monster melee apply PoB's armour reduction (90% cap), monster attacks use PoE's shared evasion
  entropy counter before damage, and `/defences` shows armour, physical reduction and the chance to evade a reference
  monster.
  Iron Reflexes converts all evasion to armour and drops dexterity's evasion bonus.
  Item-local flat and increased defence mods, applicable hybrid mods and multiplicative quality are included per item.
  Additional local quality and the alternate-quality defence prohibition are respected.
  Each item's result rounds halfway up once before global increases; inactive items contribute nothing.
  Native checks matched the crafted vest's 56/56 tooltip to 56 armour / 73 evasion on the Defence panel.
  Quality currency refreshed worn-item pools, local additional quality showed 30% once, and unequipping cleared stats.
  Active/inactive shield checks, native weapon swap and physical-hit mitigation also passed.
  The saved shared base roll selects each defence's rounded native base before local modifiers and quality.
  New armour items get ordinary rolls; legacy saves retain their previous minimum bases.
  Native base-roll landmarks, raw-32 flag handling, local/quality composition and relog preservation passed.
  Chaos Inoculation sets maximum life to 1 after life modifiers and grants chaos immunity in the damage model
  and the panel's Immune row; energy shield remains the pool.
  The pre-CI life used for stun/ailment thresholds and the low-life condition are not modelled.
  Sacred Orb rerolls, local zero-ES rules, equipment ES-to-Life conversion and corrupted-item global ES increases
  are implemented with automated coverage and historical native controls described in Items and crafting.
  Fresh real-client checks of the merged branch remain open.
  See Items and crafting for their scope; other unique/conditional mechanics and Ward calculations remain open.
  Sacred Orbs reroll the saved base through ordinary and Shift currency use, refreshing worn-item pools and panel.
  Native armour, evasion and ES rerolls matched the character updates while preserving mods and quality.
  Shared item defence calculations now include the Tabula and explicit no-ES item overrides.
  ES-to-Life conversion uses each active item's final local ES before global Life scaling.
  Independent global ES remains ES.
  The percentage variant truncates per item, removes all item ES, and yields to the full-conversion flag.
  Recovery guards read effective ES rather than suppressed or converted bases.
  Native full/50% conversion, Life scaling, independent ES, shield swaps and Gateway-restart reloads passed.
  The 69 local ES control converted to 69 Life at 100% or 33 Life at 50%, preserving independent ES.
  Corrupted-item conditional ES scales global ES only while that source item is corrupted, preserving local tooltip ES.
  Shield-specific increases add to global increases only on the active shield's local contribution.
  The native `shield_armour_+%` stat increases all three shield defences; typed increases retain their own scope.
  Iron Reflexes keeps both shield typed increases and counts the generic shield increase once.
  Final AR/EV/ES pools round halfway up after summing fractional sources, including converted evasion.
  Shield defence exclusions remove shield bases from player pools while retaining other equipment and flat sources.
  Native generic/global increases, the shield ES passive, IR, exclusions, conversion and shield swaps matched.
  A 99/99/99 local shield retained fractions through final totals of 168 armour / 186 evasion / 176 ES.
  Both synthetic inventories and expected pools survived a Gateway restart.
  Body-only ES increases now add only to the body's global increase bucket. Body Armour doubling also covers
  IR-converted body Evasion; the no-body-Armour flag suppresses those sources, preserving ordinary EV/ES.
  Twelve native checks covered additive scaling, exclusions, IR, conversion, independent ES and body removal/return.
  Doubling has real-passive regression and native calculation evidence.
  The login path used for those checks filtered ascendancy passives, so the disconnected Unbreakable client fixture
  could not exercise it in-game.
  Full minion redirection and Devotion-dependent shield scaling remain separate mechanics.
  Ward conversions, ring-slot and jewel-radius operators remain separate work.
  Code: `CharacterStats.cs`, `DamageModel.cs`, `ItemCatalog.cs`, `ItemDefences.cs`, Gateway.
  Docs: [11](../docs/protocol/11-skills.md) §4, [13](../docs/protocol/13-items.md) §2.
- Gem skills and bow attacks: Fireball from the socketed gem with its level-1 stats (9–14 fire, 750 ms cast via
  `virtual_base_spell_cast_time_ms`, 5 mana), cast at the ground or at a monster; a bow's ordinary attack and attack
  gems create `Metadata/Projectiles/DefaultArrow` (or the gem's own projectile) at the attack animation's `attack1_bow`
  contact (0.374 of the attack time).
  Attack gems deal the weapon's damage × `DamageEffectiveness` with the attack's crit; a bow arrow has no Fireball
  impact.
  Fireball's impact explodes: an area hit of its radius (`active_skill_base_area_of_effect_radius`) on an enemy or a
  wall, excluding the projectile's own target; the explosion art is a standalone effect object and the projectile is
  removed at the impact.
  Ordinary bow ground/object attacks now share contact-time release and damage at the actual flight collision
  (#203, automated and native collision/release/pause checks pass).
  Object uses no longer also deal a melee contact hit.
  The client spawns the primary projectiles of its own gem skill use itself, every one of a volley, from where it
  stands; the server simulates them but sends no object for them (one fireball was two, 2026-10-29). Their
  continuation children still get server objects.
  GGG's server sends no object for ordinary bow arrows either, so the live client draws them itself (live capture
  2026-10-29, [port record](../docs/dev/ranged-monster-actions-port.md) "Live reference").
  Tornado Shot fires `Metadata/Projectiles/TornadoShotArrow` (`SkillCatalog.ProjectileFallbacks`; its row is not named
  after the effect, so every use was refused and the server walked on while the client stopped, 2026-10-29); it is the
  basic straight-flight arrow only: the secondary arrows from the target point are not modelled yet.
  Projectiles spawn at the captured terrain height + the releasing clip's keyframe height (the bow 16 steps, a wand
  12, the Sand Spitter's spit 9; `Game_PROJECTILE_HEIGHT`, 5 steps, without rig data) on natural ground; without a
  capture they use the bare offset.
  An attack gem at the ground swings (and pays its mana) when the point is within the weapon's reach, and only walks
  (attack-move, no mana) beyond it; the client spends the mana itself on either use, so the Gateway answers an
  attack-move with the authoritative mana.
  Code: `SkillCatalog.cs`, Gateway (`CastOn`, `LaunchProjectile`, `AreaSkillHit`), `ProjectileObject`.
  Docs: [11](../docs/protocol/11-skills.md) §9–10.
- Resistances: the passive `base_resist_all_elements_%` / per-element `base_*_damage_resistance_%` are summed from the
  0% base, the two Kitava quest penalties (-30% each) are mirrored from quest flags, and the final value uses Path of
  Building's floor -200 / default max 75 / absolute cap 90 (`DamageModel`);
  used by Righteous Fire's self-burn and `/hit`; `/resistances` shows modifier, penalty and final.
  Code: `CharacterStats.cs`, `DamageModel.cs`, Gateway.
  Docs: [11](../docs/protocol/11-skills.md) §12.
- Spell Suppression: the passive `base_spell_suppression_chance_%` is capped at 100% and a successful suppress takes
  40% less spell damage plus `base_spell_damage_%_suppressed` (PoB `SuppressionChanceCap`/`SuppressionEffect`);
  `spell_suppression_chance_is_lucky` rolls twice and keeps the better roll (effective `1-(1-p)^2`), with an unlucky
  `p^2` dev mode; the conditional chance passives count while their condition holds:
  `spell_suppression_chance_%_while_on_full_life`, `_while_moving` (the walk estimate), `_if_enemy_hit_recently`
  (an attack or spell hit in the last 4 s), and `_while_on_full_energy_shield` from the effective ES pool.
  Full ES requires a living player with positive capacity and whole current ES at maximum; zero ES does not qualify.
  The raw base and active conditions add before one 0..100 clamp, shared by the panel and spell-hit roll.
  Sixteen native controls checked damage/refill, actual recharge, cap, conversion, independent ES, no-ES/zero-ES
  sources and independent full-Life/full-ES conditions. The guaranteed first spell dealt 60 damage to 118 ES,
  leaving 58 ES and changing suppression from 100% to 90% for the next hit.
  Checkpoint resurrection publishes restored full-pool conditions explicitly, even if the player tick cached the
  same chance before death. The encrypted-wire regression fails before the fix and passes afterward; native
  Clarity/death/checkpoint controls confirm 100% -> 90% -> 100% without another equipment or aura refresh.
  `/suppression`, `/suppression_luck`, `/spellhit` expose the shared result.
  Not implemented: conditional suppression effect stats and monster spell hits.
  The character panel gets the chance again within 250 ms of a condition changing (the player tick).
  Code: `CharacterStats.cs`, `DamageModel.cs`, Gateway.
  Docs: [11](../docs/protocol/11-skills.md) §12.
- Movement speed: the passive `base_movement_velocity_+%` is summed into `Pools`, sent as the client's
  `movement_velocity_+%` and used by the server's walk copy (37 × (1 + pct / 100) cells/s);
  applied on login, level, passive and area changes; `/set_speed` adds its percentage on top;
  only unconditional passive increases are modelled (no action speed, more multipliers, gear/buff movement or the 128
  cap yet).
  Code: `CharacterStats.cs`, Gateway (`ApplyMovementSpeed`).
  Docs: [07](../docs/protocol/07-latency-visibility-movement.md) §3.
- Block / Spell Block: passives' `additional_block_%` and `base_spell_block_%` are summed and capped at 75% / 90% (PoB
  `BlockChanceCap`), max-res passives `maximum_block_%` / `base_maximum_spell_block_%` raise the caps;
  a successful block prevents the hit, monster melee and `/blockhit` use it; `/block` shows both chances;
  an equipped shield adds its `ShieldTypes.Block` base and enables the shield-specific passives, staff passives apply
  with a staff, and dual wielding adds its inherent 20% attack block and dual-wield block/spell-block passives;
  Versatile Combatant reduces both maxima by 10% and converts overcapped attack block to spell block at 2% per 1%;
  lucky block uses 1-(1-p)^2 for attack/spell (dev `/block_luck`);
  the character panel gets `uncapped_attack_block_%` and `display_spell_block_chance` so it shows the uncapped value
  in parentheses and the spell block row.
  Code: `CharacterStats.cs`, `DamageModel.cs`, Gateway.
  Docs: [11](../docs/protocol/11-skills.md) §12.
- Endurance, Frenzy and Power charges: gained from the on-kill, on-hit, on-crit, on-non-crit, when-hit and on-block
  chance stats, the timed ones (each second, while stationary or moving, if hit recently) and skills (Frenzy, Power
  Siphon, Flicker Strike, Enduring Cry); 10 s duration with the duration mods, a gain refreshes the whole type, all
  expire together, minimum charges always held, lost on death, carried over area changes with the time left.
  Character.ot's per-charge bonuses and every "per charge" mod the server calculates with apply to the pools, hits,
  damage taken, walk speed and the sent stats; the Charges tab gets `current_*_charges` and the per-charge rows.
  The HUD shows one icon per type with the count (0x1c1 flags bit2) and time left (bit1), and the orbs.
  Live 2026-10-25: gains when hit, on non-critical hit and on kill, refresh, expiry, death, area change, minimum
  charges, 3 Power Charges taking attack crit 8.3% → 20.75%, a Frenzy Charge shortening the attack, 3 Endurance
  Charges taking 12% off physical and fire hits; towns draw no charge icons.
  Replica Fragility, Replica Pacifism and Replica Powerlessness ("Lose ... Charge each second", the stat stored as
  `-1`) take one charge each second down to the minimum: the negative count used to be skipped, so nothing happened.
  The maximum rows (with the maximum-charge mods) were verified on the native Charges tab at `0 / 3` before this;
  controlled native counts of 0 and 3 now agree with the held state. Enduring Cry and timed gains are not live-checked.
  Three native mirror modifiers now publish Brutal/Affliction/Absorption maxima from completed Endurance/Frenzy/Power
  maxima, with final standard/special zero overrides and independent source clearing. Real 4/5/6 native-modifier
  fixtures cover both modes; combined automated results are recorded with the panel changes below.
  This uses labelled pinned-PoB 3.29.x formulas and native source identities, without certifying every official getter.
  Converted charges (2026-10-29): under the three "gain … instead of" unique flags the held Endurance / Frenzy / Power
  charges are Brutal / Affliction / Absorption Charges: the special `current_*` and `minimum_*` rows and BuffDefinitions
  1757–1759 buffs, the standard effects off, Brutal's triple-damage chance and stun threshold per charge applied, and
  Affliction's more ailment damage in the ailment panel rows (`ConvertedChargesGatewayTests`, `ChargeEffectsTests`,
  `ChargeSessionTests`, `ChargeBuffsTests`; live 2026-10-29 with the flags set by `/set_stat`: the Brutal row, the
  zeroed Endurance row and the swapped buff). Reap's Blood Charges (2026-10-29): a use whose hit
  leaves an enemy alive gives one, a kill takes one, up to Character.ot's 5 plus the enchantment; the charges held are
  15% more damage per charge on the hits and the Offence rows and 20% more cost, with the Charges tab rows, the
  `show_blood_charge_info` gate and BuffDefinitions 1822; no duration (none in the tables), not carried across
  areas (`BloodChargesTests`, `BloodChargesGatewayTests`; live 2026-10-29: rows, gain, loss and buff with a socketed
  Reap). Spirit, Siphoning, Blitz and Challenger
  have no reachable player source here (abyss-jewel / Elder-Shaper counting, none in 3.29), the Affliction
  non-damaging effect and Absorption recoup have no consumer.
  Code: `Charges.cs`, `ChargeGains.cs`, `ChargeEffects.cs`, `ChargeSession.cs`, `ChargeBuffs.cs`,
  `Program.Charges.cs`, `ChargePanelStats.cs`, `BloodCharges.cs`, `Program.BloodCharges.cs`.
  Docs: [11](../docs/protocol/11-skills.md) §15.
- Character panel sums: about 150 panel stats that sum base forms are computed from passives, items and auras with
  their unconditional combined stats: stun, ailment avoidance, durations and effects (ignite, chill, freeze, shock,
  scorch, brittle, sap), ailment chances, penetration, leech, life/mana/ES on kill, rarity and quantity, cooldown,
  projectile speed, area, curse effect and flask duration, reflect, charge durations, cannot-be/immune flags (including the
  four damage immunities and physical immunity, from their base forms);
  the main hand / off hand rows add that weapon's local stats;
  projectile block and chaos resistance against damage over time combine the pools with their stat;
  the Defence tab's Life / Mana / Energy Shield Regeneration per Second rows are the tick's own per-minute rates
  (`total_*_recovery_per_minute_from_regeneration`).
  Evidence scope for the imported issue #75 entries: earlier test counts and Standard/Ruthless native matrices
  describe the source LocalExile revisions, not a fresh native run of this branch. Private game-mode mechanics
  are excluded; current automated results and remaining checks are in the [port record](../docs/dev/issue75-port.md).
  Six native Character.ot defaults now enter the original sources once: Brand limit 3, Brutal triple-damage/stun
  coefficients 3/10, Affliction damage/non-damaging coefficients 8/8 and Absorption recoup coefficient 12.
  Real Brand item +1 and Brand Equity +2 give actor 6; compatible linked Foulgrasp gives selected 8.
  The Brand gate follows native skill type 64 only on selected Brands; explicit replacements/zero and removal retain
  their source precedence in both modes. The special values remain coefficients; Brand attachment/cap enforcement,
  special-charge gameplay and recoup execution are not added; new native checks remain pending.
  Selected area, item-rarity and minion-increased aliases now combine raw actor and qualified own sources once,
  retaining signed cancellation/zero and final developer replacement precedence. Initial `SendPlayer` and
  area-transition object creation share that snapshot with later `PlayerSkills` refreshes; Cyclone's radius inputs
  are calculated before the selected overlay and keep their existing result.
  Real supports cover area 20+30=50, minion 15+quality 10=25 and ordinary Item Rarity 10+40=50.
  Selected fire/cold/lightning penetration now also includes the linked support's raw contribution.
  Each real level-one support adds 20 to actor 10, displaying 30 instead of the previous incorrect 10.
  Nine new Gateway regressions cover link/source removal, cancellation, overrides, requirements and skill isolation;
  general elemental penetration composes to 35 in both the panel and hit consumer.
  These pass within 29 focused checks on this branch; fresh native verification remains pending.
  Both attack-hand penetration rows include typed/elemental attack-only sources and qualified linked supports.
  Raw local hand snapshots preserve each active weapon's contribution during selected-skill recomputation.
  Twelve additional automated cases cover all three elemental supports, chaos/attack-only sums, independent
  local modifiers, inactive weapons, source removal and link changes; native display checks remain pending.
  The penetration consumer now includes non-base general elemental/chaos sources and the qualified attacking
  weapon's local modifiers; spells exclude local/attack-only inputs. Parsed contact cases cover ordinary melee/bow,
  Cyclone, Burning Arrow and Fireball with/without elemental prohibition. Selected and actor projections emit zero
  for all nine elemental aliases under prohibition and restore totals when removed; chaos remains separate.
  Real Elemental Penetration support contributes 14 at level one and agrees with the hit consumer.
  Current-branch native validation, off-hand attack selection and remaining target conditions are still open.
  Selected ailment duration/effect coefficients now also include qualifying supports: real level-one Unbound
  Ailments adds 35 to character 10 for all seven duration and five non-damaging effect aliases. Its 10% more
  ailment damage stays separate. Twenty-four regressions cover the reproduced omission, link/source removal,
  signed cancellation, explicit replacements and unrelated/default-skill isolation. Native presentation and
  actual ailment application remain open; this does not complete the ailment system.
  Historical LocalExile Ruthless support exclusions are outside this branch. Link/source removal, incompatible or unqualified supports,
  unrelated/default skills and repeated builds are covered; minion combat and item-drop execution remain separate.
  Added-base critical/default-mirror/selected-alias cases pass 52/19/22 new tests plus 95 existing controls:
  the combined focused batch passes 188/188 without skips. Fresh Gateway 3803/Core 648/Data 37 all pass without skips,
  for 4488 freshly rerun cases. The current hook records six actual unobserved disposed-NetworkStream task faults,
  all `MonsterKilled` through `Program.Send`; 385 first-chance records are separate from those six faults.
  No diagnostic errors/truncation/cap hit or new operator path appears in the captured faults.
  Passing tests do not mean fault-free background execution.
  Native default/mirror and selected-alias checks remain pending; the critical entry below records its bounded controls.
  Earlier G14/full 3710 and native observations below do not validate these new operators or certify all 387 rows.
  Energy Shield's row excludes recharge and clears when its sources are removed or regeneration is prohibited.
  Native checks verified the row at 2 per second, modifier totals, zero clearing and relog.
  During recharge its total stayed at 60 per minute while native recovery prediction changed from 60 to 340.
  Frenzy attack/cast speed coefficients include the combined per-charge modifier. Native Mods row 7236's -4
  cancels the inherent 4 for both; held charges still expand the raw source once. Seventeen selected tests
  (15 new) cover source removal and explicit zero in both modes. Eight native supplied-source controls agree,
  including +4%/+6%/attack-only +5% formatting, zero publication, charge clearing and relog.
  Zero rows are visually suppressed; missing formatter callbacks do not prove zero rendering or gear acquisition.
  Ignite/Chill/Freeze/Shock duration reductions also subtract raw `base_all_ailment_duration_on_self_+%` once.
  Real Harsh Lessons gives -20 in both modes, producing +20 reduction; typed and elemental sources remain additive,
  signed increases remain negative reductions, and final developer zero/overrides keep precedence.
  All 14 new pure/Gateway cases pass in the combined 53/53 focused batch; full Gateway passes 3710/3710 with no
  skips. Unchanged prior Core 648/Data 37 results remain applicable (4395 total, not all newly rerun).
  Five actual unobserved disposed-stream faults remain recorded (four MonsterKilled, one projectile-hit path),
  with no captured reference to the new operators. No new native evidence is claimed. Ring-slot selection,
  other ailment families, conditional ES delay and actual ailment execution remain separate.
  Block Recovery now adds base and raw direct Stun Recovery once from the same snapshot, including already expanded
  per-charge recovery. Life/Mana/ES on-kill amounts and the flask-duration coefficient include their real per-player-level
  inputs, using the actual character level and wide arithmetic. Thirty new panel/source cases cover both modes.
  The preceding panel/flask batch passed 55 new plus 80 existing focused cases and Gateway 3657/3657;
  unchanged prior Core 648/Data 37 results remain applicable (4342 total). The combined native assessment has
  40 completed controls: 22 complete PASS (12 positive source cases, six utility uses and four Life uses), with
  18 complete zero controls UNPROVED. Positive source cases at actual levels 10/20/100 match all six fields in both
  modes. The six panel fields have 138 VERIFIED/42 UNPROVED verdicts including retained and transition evidence;
  neither Defence zero field is proved in any zero control, and some Misc zeros also remain UNPROVED.
  No hidden row is counted as a rendered zero. Full on-kill recovery/percentage/prohibition operators and block
  animation timing remain open; exact runtime values and the original one-integer Life boundary are in protocol 13 §8.
  Those historical native checks predate the new self-duration and flask-effect operators. A subsequent eight-control
  live batch verifies positive per-level flask effect at actual levels 10/100 in both modes: Life rates 700/1960 for
  three seconds, and utility movement +20% / armour +2100 with native Standard/Ruthless durations. Utility strength
  is verified on explicit final character stats, separately from its timer buff. All stock/charges and original data
  were preserved, both-mode representative relogs passed, and normal services were restored. The retained verifier
  revisions and one-integer Life boundary are documented in protocol 13 §8. These controls do not promote the old
  18 whole-zero cases or establish source-removal/zero next-use behavior, whole-unique acquisition/tooltip parity,
  new self-duration native coverage, or completion of an issue 75 row.
  The 2026-10-29 row batch publishes the rows that had no derivation: the Defence estimates (physical reduction plain /
  against projectiles / against hits with their differs flags, chance to evade, ward delay, mana recharge, Omniscience
  requirements) against DefaultMonsterStats at the character's level; the off hand's own accuracy, chance to hit,
  attack speed, critical chance/multiplier and damage ranges with `skill_uses_off_hand` for dual-wielded default and
  gem attacks, whose per-use / DPS / attacks-per-second rows average both hands (`DualWieldPanel`); the spell
  effective / twice-critical chance (luck, then Assassin bifurcation); secondary-damage rows for
  flagged spells and for Bear Trap / Herald of Ice from their own secondary base damage (generic buckets only,
  `Program.SecondaryPanel.cs`); `skill_show_average_damage_instead_of_dps`; the 32 Bleeding / Ignite / Poison damage per second rows
  per hand, spell and secondary context (`AilmentPanelStats`, from the hit's raw packet and conversion with the
  ailment buckets and DoT multipliers); stun-by-kind flags, brand per-target limit, Spirit maximum and the Siphoning
  damage-taken coefficient. 60 new automated cases pass with real sword/dagger, Cyclone and Fireball fixtures; the
  owner's live run of 2026-10-29 (base `7fd944be`) passed the batch's test plan (off hand and clearing, ailment rows,
  effective critical, secondary blocks, Defence estimates under `/set_stat`). Actual ailment application and
  Elder/Shaper and abyss-jewel item counting (Spirit, Siphoning; deferred behind #60) remain open
  ([review](../docs/dev/issue75-review.md#offence-defence-misc-and-charges-row-batch)).
  Code: `PanelStats.cs`, `PanelStatDefaults.cs`, `AilmentPanelStats.cs`, `Program.OffHandPanel.cs`, `DualWieldPanel.cs`,
  Gateway (`CharacterPlayerStats`, `HandLocalStats`, `GemSkills`).
  Docs: [11](../docs/protocol/11-skills.md) §14.
- Character-panel leech limits: full maximum pools supply separate per-instance amounts and total recovery caps.
  The current issue #75 review also corrects 24 leech coefficient and three recovery-speed aliases: general,
  typed/elemental, attack/spell and qualified hand sources, missing physical/chaos ES, and real selected supports.
  Actor/support fractional inputs combine before unit conversion. The new composition has automated controls;
  native getter/formatting parity remains open (damage §5b, skills §14).
  Six absolute rows and their percent companions include supported increases/reductions and prohibition flags.
  Native controls verify display conversion, Low Life/healing, real item pool conversion, reservation and relog.
  The periodic stat refresh uses one consistent developer snapshot, with four observed-task regressions.
  Fractional endpoint truncation is a documented local projection; official-server rounding is unmeasured.
  Ordinary accepted melee/bow/projectile/area hits now create separate Life/Mana/ES instances from raw qualified
  sources and post-mitigation direct damage. Their duration, total recovery, cap and generic recovery multipliers
  are distinct. Instant portions recover inside the hit transaction; the existing resource writer integrates timed
  portions and exact expiry boundaries. Full-pool removal, unconditional overleech, prohibition, pause/death/transfer
  cleanup and real support contacts have automated controls. Ghost Reaver now creates separate converted/native
  ES instances, doubles the total ES cap and disables recharge; actual node sources and parsed refund have controls.
  Plain conversion does not add those keystone side effects. Other conversions/conditions, skill-specific
  recovery-speed ownership and current native recovery/prediction parity remain open. Leeching/full-ES flags now
  reflect actual instances/pools and refresh player/selected stats on the normal tick. General/resource/attack/
  physical leeching hit damage and Energy Leech's full-ES MORE/quality increased bonuses have separate buckets
  and actual support/contact controls; speed/defence/ailment/DoT consumers remain open.
  Attack-only ES overleech retains attack instances (including Ghost Reaver conversion) while removing spell
  instances at full ES, preserving original expiry and one shared cap. Actual mod/contact and source-removal
  controls pass. Ordinary Life-overleech projection now respects explicit zero replacements; other sources remain open.
  Claw mastery instant leech counts active usable claws, Weapon Master applies its claw condition once, and
  Gore Dancer's Life instant source survives Ghost Reaver conversion. Real-source contacts and split/override
  controls pass; exact native mixed Life/ES instant composition remains unverified (damage §5b records the policy).
  Highwayman's real modifier now doubles total Life-leech recovery once, independently of the explicit Life
  maximum-rate doubling flag. Source/removal, duplicate flags, caps, duration, instant and conversion controls pass;
  current Vaal Pact acquisition and native stacking/recovery parity are not implied by raw-flag support.
  Code: `LeechLimits.cs`, `LeechSources.cs`, `LeechRecoveryState.cs`, `LeechSession.cs`, Gateway (`Program.Leech`, resource writer).
  Docs: [20](../docs/protocol/20-damage.md) §5b.
- Ordinary Life/Mana regeneration: flat and maximum-pool percentage sources receive regeneration and recovery increases.
  The matching prohibitions stop recovery, and the panel/native prediction inputs share the actual resource writer.
  Ten native checks verify flat sources, independent increases, prohibition, clearing and relog.
  Controls matched 1080 Life/minute to 18/second and 3339 Mana/minute to recovery near 55.65/second.
  Fortification's exact expiry integration uses the same factors, with automated modifier/prohibition coverage.
  Regeneration conversions and named conditional/more modifiers remain separate work.
  See [20](../docs/protocol/20-damage.md), "Continuous resource updates".
- Determination and Grace truncate their scaled flat/more aura inputs before character-wide defence calculation.
  Normal and Ruthless native data and quality isolation have regression coverage.
  Basalt/Stibnite compose more factors without rounding the combined percentage before the final rating.
  Native ordinary aura/flask, global-increase, IR and clearing checks matched the shared pools.
  Code: `AuraCatalog.cs`, `UtilityFlasks.cs`, `CharacterStats.cs`.
  Docs: [11](../docs/protocol/11-skills.md) §13.
- Discipline supplies its level-based flat ES to the shared pool, panel, absorption and recharge paths.
  Intelligence and global ES increases apply once; shield-only increases and armour ES-to-Life leave the aura source.
  Quality affects area of effect only; aura-effect scaling truncates integer ES/recharge stats using pinned PoB.
  Native activation, clearing, capacity without instant refill, absorption and delayed recharge matched.
  Native level 20, quality isolation, global scaling, conversion and shield-source controls also matched.
  Town suppression/return, gem-host removal/return and death clearing were checked with this aura.
  Both level-1 and level-20 fixtures restored expected ES and identical saved inventories after a Gateway restart.
  Code: `AuraCatalog.cs`, Gateway (`PoolsFor`, `BaseStats`).
  Docs: [11](../docs/protocol/11-skills.md) §13, [20](../docs/protocol/20-damage.md), "Player energy shield".
- Buffs (S→C 0x1c1 add, 0x1c2 remove) and Righteous Fire: cast on oneself, its buffs and the big burning circle (an
  effect object, `Metadata/Effects/PermanentServerEffect` with `rifire.ao`, following the player), 90% of life per
  second nonlethal burn on the player, 70% of life per second to monsters within its radius (18 cells, grown by the
  player's area of effect stats, `SkillArea`; they burn to death), the circle scaled to that radius (MiscAnimated's
  reference radius, the uniform Positioned scale; matches the real game at level 1) and kept on the player every 100
  ms; server-side mana (cost, regeneration); `/god` (also 200% increased area of effect), `/set_aoe`.
  Code: Gateway (`ActivateRighteousFire`, `RunPlayerTick`), `SkillArea.cs`.
  Docs: [11](../docs/protocol/11-skills.md) §12.
- Warcry admission restriction (#297): any signed nonzero `your_warcry_skills_are_disabled` blocks new typed warcries.
  Actual caster passive/equipment/raw dev sources are read before skill overlays, action echoes, costs and cooldowns.
  Explicit zero and source removal restore admission without rebuilding pools or removing Actor skills.
  Parsed ground/object, paid Enduring Cry, source, instant safety and queued-send regressions pass.
  Accepted non-cancellable cry releases retain their existing behavior; Enduring Cry's charge executor stays partial.
  Seven native checks verify refusal without Mana loss or release, source clearing and paid 13-Mana retries.
  Native Endurance Charges confirm accepted releases; Fireball remains usable and relog clears the developer flag.
  Phrecia play stays gated; source precedence, aliases and queued boundaries have additional automated coverage.
  See [11](../docs/protocol/11-skills.md) section 10b.
- Own-aura restriction (#304) consumes signed 21148 OR 11881, with explicit final 11075 developer precedence.
  Typed Aura and accepted Blasphemy metadata drive admission and native skill flags.
  Admission and queued acknowledgements use the requested skill ID; duplicate gems keep separate support context.
  Remembered candidates check their own requirements, so an unusable copy cannot suppress another available gem.
  Local policy suspends self-owned pairs, bonuses and reservation while retaining intent, without a mana refill.
  Lifting the restriction restores available remembered ordinary self auras once with fresh buff IDs and a mana clamp.
  RF, flasks, Fortification and received effects remain outside this owned lifecycle; no party aura executor is claimed.
  Source, real-dispatch, loopback, queued snapshot/add, initial creation and entry replay regressions pass.
  Rage refreshes reconcile paired removals and reservation before their snapshots, even when Rage is unchanged.
  Fifteen native controls verify Clarity/Haste pairs, real bonuses/Mana, three refused routes and ordinary Fireball.
  Relog restores remembered Haste after the transient source clears; death while suspended clears saved intent.
  Native grey-icon prediction and persistent restricting equipment remain unverified.
  The suspend/restore lifecycle is explicit LocalExile policy, not proven GGG behavior; Phrecia play stays gated.
  See [11](../docs/protocol/11-skills.md) section 10c.
- **Spell skills deal no damage (#293):** raw caster key 5341 suppresses supported Spell hit packets and RF enemy burn.
  Physical, elemental and chaos spell damage, supported projectiles and areas share the final packet restriction.
  Selected native record ranges, average damage and DPS use that packet, including inherited main-hand zero rows.
  Casting, costs, timing, visuals, zero-hit callbacks, attacks and aura-granted attack bonuses retain their scope.
  RF self-burn continues independently; source changes follow existing impact/tick resolution without pool rebuilds.
  Automated supported constructors, dispatch/Life, in-flight changes, aura controls and RF tick checks pass.
  Current-main controls cover Rage/charge contacts, quality, Mana sources, Mageblood, chaos armour
  and cosmetic identity.
  Unpaid release guards, utility reservation/cooldowns and RF fractional carry keep their existing behavior.
  Eleven historical native checks at original head `e80a81c` cover Fireball/Ice Nova, paid casts and attacks.
  Native Fireball action rows clear ranges and DPS; RF keeps its self-burn while its enemy damage stops/resumes.
  Those controls cover source changes and relog at the historical head.
  Fresh ordered joint controls at `c9cd1864` verify Fireball 200 / zero, attack 100 and restored Fireball 200.
  Final attributes and converted pools remain active in that same unascended Marauder session.
  Rampager 49937 supplies the source, but class admission, owner execution and the node's other mechanics are separate.
  Code: `SkillDamageContext`, `SupportedHit`, `SpellHit`, `BurnAround`, `GemSkills`.
  Docs: [20](../docs/protocol/20-damage.md) section 2f.
- Spells are released at the cast animation's contact keyframe (Witch 599 of 1066 ms;
  every class's from its rig .amd), with the server duration adjusted once for cast speed.
  Code: `AnimationContacts.cs`, `scripts/re/anim_contacts.py`.
  Docs: [11](../docs/protocol/11-skills.md) §9.

## Partly working

- Selected supported-spell panel: final linked-support damage replaces both spell and inherited hand ranges,
  clearing removed types and updating pre-critical average/DPS from the supported cast time.
  Eight pipeline controls cover damage, projectiles, added damage, gem level, type prohibition, link lifecycle
  and a hitless spell with an equipped weapon. Fourteen native captures cover twelve Standard-fixture stages:
  eleven positive formatter/owned-skill checks and one skill-removal check pass strict identity checks.
  Brutality and Righteous Fire have explicit zero hit fields and visually suppressed rows; their zero-row
  formatter evidence remains partial. Unlink/relink uses host removal and re-equipping, not in-place mutation.
  Relog restores the baseline and exact item contents; original copied saves and stashes remain unchanged.
  Official critical-weighted DPS, damage over time and special skill mechanics remain separate.
  Code: Gateway (`PlayerSkills`), `SpellPanelStats.cs`; tests: `SupportedSpellPanelTests.cs`.
  Docs: [11](../docs/protocol/11-skills.md) §9.

- Ordinary percentage attributes: typed and all-attribute increases act once on the complete class/passive/active-item
  flat bucket. Pools, equipment qualification and gem requirements share the resulting final attributes.
  Calculated zero attributes publish zero; inactive items contribute nothing, and a socketed gem's unmet requirement
  makes its host inactive again (main's rule restored 2026-10-29, [19](../docs/protocol/19-gems.md); the port had kept
  the host active). Forty-seven new automated cases and 63 selected checks cover both modes, real Utmost sources,
  signed/zero modifiers, dependent pools, source removal/recovery, final developer overrides and weapon swaps.
  Thirty-four native controls (17 per mode) verify final values, explicit zeroes, derived pools, item activation,
  source removal/recovery and active weapon-set persistence after relog. Ordinary Fireball input succeeds at INT 68,
  is refused at 62 against its 67 requirement, and succeeds after recovery (those controls predate the restored host
  deactivation: the host now goes inactive with its gem).
  Source/runtime hashes and original saves hold; isolated item contents/cells return to their canonical state.
  Supplied-source controls do not certify unique acquisition. Half-up fractional rounding follows pinned PoB; official-server rounding,
  signed equipment cycles, attribute conversions, conditional/more operators and mercenary gates remain open.
  Wide final evaluation does not claim arbitrary raw int-source aggregation is overflow-proof.
  Six ordinary pool coefficients use those completed attributes and actual level in the flat Life/Mana buckets,
  before final pool increases: signed `life_per_level`/`mana_per_level`, Life per ten Dexterity/Intelligence,
  per four Dexterity and per two Intelligence.
  Native -12/-6/-3 Life-per-level penalties subtract from inherent growth; attribute sources count
  complete groups once. Existing Strength-based Mana, ordinary halves and equipment ES-to-Life flats retain their order.
  One developer snapshot replaces whole coefficients, including zero; commands rebuild/clamp pools and publish the
  affected resource at the send gate, retaining Mana reservation.
  All 71 new cases (45 pure/native-source and 26 Gateway) plus 82 existing regressions pass: 153 focused, no skips.
  Full Gateway passes 3874/3874 with no skips; earlier unchanged Core 648/Data 37 from this session remain applicable
  (4559 applicable cases; only Gateway rerun after these changes). That run's hook records seven actual unobserved
  disposed-NetworkStream faults: six `MonsterKilled` → `Send` and one `NotifyMonsterHit` through the projectile-hit
  path to `Send`. Its 390 first-chance records are separate; no diagnostic errors/truncation/cap hit or new pool-source
  path appears. Passing tests do not imply fault-free background execution; new native checks remain pending.
  The existing final pool floor differs from pinned PoB's final rounding; official fractional parity is unverified.
  Real modifier/isolated passive fixtures do not prove acquisition, event-passive admission or full conversion nodes,
  and these six inputs do not complete the Life/Mana rows or conditional/more pool operators.
  Item ES per ten final Strength and Evasion per ten final Intelligence now join their ordinary INC buckets once.
  Native coefficients 1/2, completed attribute groups, slot/shield exclusions, IR and signed wide cancellation have
  33 pure and 14 both-mode Gateway cases; actual commands cover whole-source zero, ES clamping and no refill.
  Fourteen fresh level-100 native controls (seven each in Standard and Ruthless) verify supplied-source publication
  and reload: ES 103/104/124/135/124/113/135 and EV 15/16/22/26/22/18/26.
  Six capacity increases retain damaged current ES in 487–749 ms; four removals clamp full ES to 124/113.
  Both fresh entries restore current/max ES 135. Completed World updates bind each target to its actual source action.
  Acquisition, full Shaper's Touch and Defence-row formatter bindings remain open.
  Inherent ES now uses complete groups of ten final Intelligence, correcting the former doubled `/5` term.
  Static native Intelligence help text agrees with pinned PoB; exact official fractional arithmetic remains unverified.
  These new controls cover the corrected totals, no-refill, clamp and reload; older native totals remain historical.
  Corrected recharge/full-ES conditions and native queued-send scheduling still need separate validation.
  Eleven new baseline cases and selected compositions pass in 216 focused cases.
  Full Gateway 3939 covers all updated tests, including the percentage-attribute and tick cases omitted by that filter.
  Unchanged prior Core 648/Data 37 remain applicable; no tests skipped.
  Retained diagnostic faults and native/persistence limits are qualified in the defence reference below.
  Five native prohibitions now remove only inherent Strength Life/melee, Dexterity accuracy/EV and Intelligence Mana/ES;
  the narrow Life/Mana flags overlap once with their broad counterpart. Final attributes, requirements and item group
  terms remain active. One whole raw developer snapshot supports zero replacements and affected resource publication.
  All 16 full native vectors retain their companion attributes and saved payloads in both-mode automated controls.
  Eighty new cases pass (64 pure/native-source, 16 Gateway); fresh full Gateway 4019/Core 648/Data 37 pass with no skips.
  Its hook records six actual disposed-stream `MonsterKilled`→`Send` task faults and 386 separate first-chance records;
  no new flag path or diagnostic error/truncation/cap hit is captured. This is not a fault-free-run claim.
  Native flag publication/reload and all-source immediate Life publication remain unverified; ordinary equipment Life
  still clamps on the existing recovery tick. Key18620 has no checked direct source and is not an IR alias.
  Code: `CharacterStats.cs`, `ItemModStats.cs`, Gateway (`CharacterStatSources`, `PassiveBonus`, `ClassAttributes`).
  Docs: [11](../docs/protocol/11-skills.md) §4, [13](../docs/protocol/13-items.md) §2d,
  [attribute-scaled defences](../docs/protocol/20-damage.md#player-energy-shield-2026-10-25).

- Weapon-local damage and timing: active weapons supply a typed packet with multiplicative physical quality,
  local flat/increased damage, no-physical/quality-prohibition flags and local attack speed applied once.
  Ordinary melee/ranged and supported attacks share the actual context; final per-type rows clear removed types.
  Automated controls cover weapon swaps, inactive hand-local exclusion and shared conversion/Rage composition.
  Pool/charge/timing conditions and shield base block also exclude inactive or disabled equipment.
  Twenty-four native publication controls pass in Standard/Ruthless; the independent local-mod/quality tooltip
  matches 36-70 physical damage and 1.80 attacks per second. Source/DLL hashes and inventories held.
  Authoritative offhand alternation and alternative quality remain separate.
  Local critical flat-before-increase ordering has automated coverage in the critical-strike implementation below;
  the preceding native weapon checks do not validate that change.
  Supported-contact composition retains automated coverage and needs its own native check.
  A Core raw conversion-history seam is available with 21 new path cases and 12 existing packet controls passing.
  The full Core suite passes 648 tests. `PlayerHitDamage` now resolves one visited-type increased bucket, inherited
  support/aura more, final-destination prohibitions and common charge more across ordinary/supported hits.
  It preserves explicit contexts, source precedence, no-conversion floating grouping and ranged release snapshots.
  Forty-three new Gateway cases and 340 focused checks pass, followed by all 3602 Gateway tests and 37 Data tests.
  Thirty-two native numeric controls pass across Standard/Ruthless: 24 original explicit-field controls and eight
  ordinary-bow sparse-zero controls. The first sparse Standard baseline is retrospective; the next seven are
  prospective. Actual natural getter zeros are distinct from explicit wire zeros or hidden-row rendering.
  Four restoration/relog controls, 184 full item/socket acknowledgements, original-save preservation and 714 unchanged
  source/runtime inputs pass. The client is closed and normal `data/saves.db` restored. Native publication does not
  certify authoritative hit contact, critical-weighted DPS, conditional/endpoints/ailment/DoT or official rounding.
  Existing disposed-stream cleanup faults and separate native connection closures remain recorded in the evidence.
  Code: `ItemWeaponStats.cs`, `DamageStats.cs`, `SkillCatalog.cs`, `SupportedAttackDamage.cs`.
  Docs: [20](../docs/protocol/20-damage.md) §2a.


The generic [projectile framework](../docs/game/projectile-framework.md) owns production gem/bow routing and
the sole 30 Hz runtime.
Unknown area dimensions no longer become an immediate projectile wall when walk captures are absent.
Catalog fallback provides basic flight; registered skill definitions supply special rules
and can require explicit recipes.

- Arcane Might (#364) shares one highest-eligible-ratio consumer across current attacks and damage panels.
  Normal Crown of Eyes, Whisperer's existing consumer and wand mastery use the six modern stat contracts.
  Independent grants do not stack; skill base plus quality still adds before the maximum comparison.
  Weapon-local damage now scales in ordinary and supported attacks; restricted types are cleared from skill panels.
  Automated cases cover actual direct/area target Life loss, snapshots, mastery refund and Crown persistence/removal.
  All 24 inventoried numeric skill constants and three quality records have calculation/data checks.
  Those checks do not enable unsupported skills, variants, mercenaries or Phrecia allocation.
  Iron Will/Strength provenance, missing upstream spell inputs and unrelated leech remain outside this consumer.
  Native user checks confirm ordinary Crown ratios, KB non-stacking and Fireball isolation in the damage panels.
  KB's absolute damage exposed an effectiveness-offset decoding error; raw 4000 now resolves to 140%, not 40%.
  Corrected absolute damage and mastery lifecycle still need native verification.
  See [scope and coverage](../docs/game/arcane-might.md) and [20](../docs/protocol/20-damage.md) §2c.
- Ruthless resolves native skill/gem, passive and mod alternatives per connection, including zero-valued overrides.
  Ordinary characters in the same Gateway keep ordinary catalogs.
  Hardcore death is terminal for the old connection; a queued level-up cannot revive it.
  Hardcore migration and permanent Ruthless death have automated lifecycle coverage.
  Earlier native checks were against the original fork; native checks on the current integration are recorded separately.
  Native skill/passive/mod alternatives and late-action guards have automated coverage.
  Main's projectile kill path retains Ruthless rewards, gem XP and active utility-flask restrictions.
  Synchronous quest/gem commits retain the Hardcore terminal-progress guard; composed regressions pass.
  See [league modes](../docs/game/league-modes.md).
- Grace Period grants 60 seconds of entry protection outside towns and hideouts.
  First movement, a skill, a flask or expiry ends it; incoming damage, hostile targeting and RF damage are suppressed.
  The native buff uses floating-point seconds for its countdown; the icon was checked live, timer retest is pending.
  See [11](../docs/protocol/11-skills.md), "Area-entry Grace Period".
- Vaal souls (#308): genuine owner kills collect into bounded shared ActiveSkills-row budgets.
  Creation and skill updates publish the proved native Actor lists; automated wire/dispatcher checks pass.
  Duplicates, source/cost changes, removal/readd, range, prevention, queued sends and transient lifecycle are covered.
  Eligibility commits with lethal mutations before death notifications, including shared hits and Righteous Fire.
  Vaal aura costs use per-instance Aura types and accepted supports, with explicit developer replacement precedence.
  Player soul loss commits at zero Life; delayed death notices preserve actual resurrection earnings.
  Vaal gem/support requirements use current equipment snapshots while gear notifications are held.
  Source checks on base `939b44fe` preserve wide raw attributes, mana, pool flags and supported skill payment.
  Skill-record bit1 (0x02) now retains native Vaal soul inputs using catalog raw stored uses in both lookup paths.
  Parsed creation/update and real socketed tests cover zero cost/balance, ordinary partners and inactive hosts.
  A pinned 3.29.3b session passes 42 collection/counter checks: real kills, cap, cost, zero capacity and prevention.
  Native required-soul and manager consumers agree, including natural gem level-ups and unsupported-input refusal.
  The build and all three suites pass; exact private composition with PR #294 also passes.
  Distribution and capacity are documented LocalExile policies, not a claim of official algorithm parity.
  Native duplicate/removal, death/relog/transfer, aura costs and held-notification composition remain unverified.
  Unsupported Vaal casts are refused before action echoes or cooldown reservation, including effective zero cost.
  No executor, Rage payment or SGP countdown is added.
  See [27-vaal-souls.md](../docs/protocol/27-vaal-souls.md).
- Aura restoration bootstrap fix: socketed gem requirements no longer deactivate their entire host item during pool
  initialization.
  Equipment contributes attributes before individual gem validation;
  saved auras remain candidates on town/hideout round trips.
  Regression covers fresh-connection Precision/Clarity equipment and all 17 supported self auras.
  Live retest pending.
- Aura lifecycle: towns suppress all aura effects/reservation while retaining per-character enabled intent.
  Exit/relogin/character return restores available remembered auras without rechecking cast affordability;
  manual activation still validates mana.
  Death clears saved/runtime auras; skill refresh resumes temporarily unavailable effects.
  The shared own-aura restriction has separate native controls above.
  Discipline's town, death and gem-host lifecycle also passed historical native checks in LocalExile.
  Fresh merged-branch and other aura-family round trips remain partial.
  See protocol/11-skills.md.
- Aura presentation (2026-10-25): self auras select their distinct EPKFiles2 art.
  A live check found that source-only buffs hid the top-left icons;
  source and received buff instances now coexist, restore and end together.
  Icon restoration awaits client confirmation.
  See [11](../docs/protocol/11-skills.md) section 13.
- Ordinary aura expansion (2026-10-25, awaiting client confirmation): Determination, Grace, Anger, Hatred,
  Wrath, Zealotry, Malevolence and Envy now activate with reservation and paired visual/HUD buffs.
  Armour/evasion, added hit damage, gain-as-cold, spell damage/crit and RF's more DoT are connected.
  Discipline's ordinary ES pool and recharge are verified separately above; conditional effects remain partial.
  Reservation overflow is refused.
  See [11](../docs/protocol/11-skills.md) section 13.
- Saved aura toggles (2026-10-25, awaiting client confirmation): active effects persist per character and restore on
  relog, area entry and server restart, subject to usable gems and available reservation.
  Helena's picker now receives all 103 exported personal hideouts, including MTX templates;
  switching templates now uses the native selection request and persists the chosen home.
  See [11](../docs/protocol/11-skills.md) and [21](../docs/protocol/21-hideouts.md).
- **Projectile modifiers.** Generic profiles consume resolved primary counts, speed, pierce/fork/chain/split/return
  grants and independent prohibitions in the sole staged runtime. Ordinary bows and catalog-known projectile gems
  work without a skill provider. A basic recipe is straight flight, not complete special-skill behavior.
  Return opens another hit-admission phase and reads the shared returning-damage multiplier. Partial return chance,
  return speed, contact budgets, overshoot and terrain behavior are configurable through skill definitions.
  Boolean fork sources grant one generation, and raw bow always-pierce sources bind to the same common profile.
  Return retains remaining continuation counts and the original lifetime; return pierce-all requires its own stat.
  Direct and area snapshots keep area increases/supports scoped to their damage stages.
  Area effects scale to the committed radius when the selected art exports a native radius; unknown art stays unscaled.
  Terrain-blocked area centers are omitted without resampling or reserving time; surviving areas keep consecutive intervals.
  Skill explosions, pulses, children, forms, native art and specialized resources belong to skill definitions.
  Missing metadata or unsupported damage plans are refused before costs, without generic area-hit fallback.
  [Named skills](../docs/game/projectile-skills.md) implement terminal Fireball blasts and Kinetic Blast clusters.
  Burning Arrow uses catalog flight with its 100% fire conversion, a 25% base ignite chance (critical hits always
  ignite) and Vigour's fire added from maximum Life; projectile ignite burns monsters through their fire resistance
  with the native `ignited` buff ([20](../docs/protocol/20-damage.md) §3). Automated only; native ignite visuals and
  damage still need a user-run check.
  Direct volleys share admission separately outbound and returning; denied siblings pass through.
  Fireball waits for exhausted continuation and gives the terminal victim one area-classified hit.
  Normal KB creates four offset explosions with 40% less area damage; Clustering creates three plus quantity modifiers,
  keeps one primary and cannot Split.
  The internal AltY row retains normal-KB compatibility, without establishing a third playable gem.
  Separate blast/placement radii use supported area stats once; independent explosions can overlap covered victims.
  Each KB contact group excludes its own triggering enemy, including delayed explosions; other groups may hit it.
  Automated checks cover large triggering victims, pierced neighbors and wall/range groups. Gateway actor radii
  remain zero, so live area coverage uses monster centers rather than calibrated body intersections.
  KB centers use a fixed scaled circle and random sector angles, with separate first-delay/inter-slot parameters:
  immediate first blast, then 1/30 s between slots. These are user-tuned feel values, not official measurements.
  Secondary explosions use their own small-burst art and equipped MTX; native art scales to the actual blast radius.
  Skill-bar explosion totals use the resolved cluster recipe, including quality and Clustering's projectile conversion.
  Fireball wall-contact damage and default/Stygian packets have regression coverage; visual visibility remains unverified.
  Flights retain fixed terrain-plus-offset height (bare offset without a grid); explosion effects follow client terrain.
  A player's primary shot flies at the higher of its release and aimed ground: terrain rising more than 12 steps above
  that stops it, so a shot aimed below a cliff hits the face and one aimed on top clears it
  ([framework](../docs/game/projectile-framework.md), "Aimed height"). LocalExile model v1, awaiting a native check.
  Official geometry/timing, surface height and art equivalence still need client validation.
  Code: Core `Projectiles/`, Gateway `Projectiles/` and `Projectiles/Skills/`; see the
  [framework guide](../docs/game/projectile-framework.md).
- **Instant skills and cooldowns (verified live 2026-10-21).** Haste, the Purities and Righteous Fire carry
  `skill_is_instant`, so the client sends them as C→S 0x191 and keeps walking;
  the server applies them and answers S→C 0x1ce, which starts the skill bar's cooldown.
  Skill records retain GEPL Cooldown as the native base and carry the cooldown count.
  Ordinary one-use spells/attacks now resolve player and skill generic recovery once, and the server keeps remaining
  base-time budget through stat changes and pause.
  Preflight reserves before action/movement/echo side effects; queued effects retain the original action/area ticket.
  Real-client Dash initial +100%/-50% recovery, early raw rejection without movement/echo/mana, and pause checks pass.
  The native positive-rate cache required an explicit cooldown-state refresh after skill-stat changes;
  this preserves remaining/total base time at the send gate without consuming another use.
  Native Dash mid-cooldown recovery changes now pass live: one entry changes rate with base/total/progress preserved,
  early input has no echo/cost, and reuse succeeds after native expiry.
  Wire, active/expired/zero-rate and queued encrypted-send checks also pass.
  Multi-use, duplicate same-effect gems, shared groups, extra uses, bypass, typed/conditional/more recovery and official
  tick equivalence remain outside this slice.
  Skill records carry the GEPL Cooldown and the stats the cooldown count, and the server refuses a skill until its
  cooldown is over.
  Ground-target C→S 0x190 now frames its 12 bytes and routes ordinary Frostblink through mana/cooldown acceptance.
  The ground 0x1ce uses the raw aim; the server mirrors native 32/42-cell distance limits and ends the old walk.
  Placement requires a captured grid and clear terrain, barriers and endpoint clearance.
  Action tokens and pending releases remain unchanged; other location instant movement is withheld.
  Actual Q input and a cooldown retry matched native/server positions, with two 12-mana costs and two confirmations.
  Early retry, noninstant Fireball and unsigned-invalid coordinate requests changed no position or mana.
  Manual native experiments separately verified near/far clamps and the old predicted walk ending after a blink.
  Hovered-monster Frostblink sends 0x191 and now shares movement acceptance, with an object-bound 0x1ce.
  Its size-aware object candidate trace precedes the distance clamp;
  it does not reuse the monster centre as a ground aim.
  Both preflight and the send gate require a living known-size monster with no active walk, and recheck placement.
  Invalid preflight changes no mana/cooldown; later invalidation withholds confirmation under the existing cost policy.
  Automated actual-send tests cover current target/player sampling, target death/walk, pause, transfer, barriers and unequip.
  Four manual native object confirmations verified clear near/far/diagonal/off-axis endpoints.
  A reflected terrain fallback is deliberately rejected.
  Actual hovered Q on a stationary monster emitted 0x191 and one object-bound 0x1ce with flags 0x20.
  From `(280,220)` against `(300,220)`, native/server position became `(317,220)` for one 12-mana cost.
  A framed ground retry at 0.5 seconds changed no mana or position and sent no extra confirmation.
  Combined #197 synchronization retained base 3000 ms, total 3 seconds and rate 1.
  Production framed off-axis/far object requests matched native/server endpoints with one 12-mana cost each.
  Framed moving-target, reflected-terrain, missing-target and noninstant requests changed no mana or position.
  They sent no confirmation, and the invalid-combination control kept native cooldown entries empty.
  Production movement during an active walk has not been measured.
  Frostblink damage, chilled ground, enemy-dependent recovery and shared Blink cooldown groups remain unsupported.
  The skill bar keeps its slots when skills are re-sent or a gem is socketed again (the saved bar goes out as 0x53)
  ([11](../docs/protocol/11-skills.md) §9, §11).
- **Auras (the simple set is verified live).** Reservation is read from GrantedEffectsPerLevel (`ManaReservationFlat`
  / `ManaReservationPercent`), reduced by the passive/mastery reservation efficiency and sent as the Life pool's
  reserved mana (and in every 0x1b1 mana update).
  Aura skills toggle on cast; Purity of Elements/Fire/Ice/Lightning, Clarity, Vitality, Precision and Haste apply
  their gem's level and quality effects and buff icons; their 3D effects (`affected_player.epk`, `faster_attack.epk`
  …) do not render yet.
  The skill-bar active frame and rotating circle now receive the buff's skill id/action hash association (0x1c1 bit3),
  including area-entry restoration; automated checks pass, live icon verification pending.
  Not modelled: combat auras (Anger/Hatred/Wrath/Zealotry/Pride), Heralds, banners, curse auras,
  unsupported conditional effects, Purity ailment immunity, Reservation masteries 1/2/6,
  supports, party/minion auras and life reservation
  ([11](../docs/protocol/11-skills.md) 13).
- **Stats.** The client's stat key is the Stats.dat row + 1 with the vendored schema
  ([11](../docs/protocol/11-skills.md) §8); `/set_stat` takes stat names or keys.
  The [357-stat ledger](../docs/dev/issue75-stat-ledger.md) tracks current individual review state and remaining
  scope. The inventory validates exact membership; these states do not count completed implementations.
  The player's life, mana, accuracy, armour and evasion come from its class and level, with the attributes' bonuses
  (energy shield, evasion and melee physical damage increases);
  accuracy also takes its flat/increased passives, weapon-specific stats and Accuracy Mastery (rating for the panel,
  the two target-conditional effects at the roll), and the final rating value is
  sent as `main_hand_accuracy_rating` / `off_hand_accuracy_rating` for the Offence panel;
  armour and evasion also take their flat/increased passives and the base equipped armour pieces (body, helmet,
  gloves, boots); Life/Mana also take signed per-level and complete final-attribute-group sources before their
  existing flat/increased projection. Its final floor remains a documented local choice; current automated results
  are in the attribute entry above, with new native checks pending.
  See [11](../docs/protocol/11-skills.md) §4.
- **Attack speed passives.** Generic, melee, one-/two-handed, weapon-family, shield and dual-wield passive attack
  speed is summed into the client's `main_hand_attack_speed_+%`;
  dual wielding adds PoB's 10% more and the server's unarmed attack uses the same final value.
  Attack gems now apply their extracted inherent speed multiplier to total base duration separately from player speed.
  Pacing, contact/release timing, channel intervals and selected APS share the same rounded native base duration.
  Actual native bow input verified ordinary duration 0.714 s and Burning Arrow 1.02 s, with stored APS 1.40 and 0.98.
  Original weapon base 714 ms and player speed 0% stayed unchanged; release/contact timing was not separately measured.
  Catalog, modifier-order, native-field and spell-exclusion tests pass.
  Supports, conditional/per-charge speed, special time overrides and attack-gem damage/DPS remain separate work.
  Selected attack-gem main-hand ranges now share the converted combat packet, including skill effectiveness.
  All twelve ranges clear absent types; pre-crit averages retain fractions and DPS uses the emitted APS value.
  Creation and refresh pass raw character stats and the actual weapon separately from inherited display values.
  Native Burning Arrow showed 9-23 fire/total, physical zero, per-use 16.52 and DPS 16.19 at APS 0.98.
  Added cold damage showed 9-9 cold and 18-32 total; clearing it removed the cold rows.
  Relog retained the base result.
  A noncritical server hit dealt 24 damage, while ordinary attack retained its original ranges and averages.
  Packet, catalog, conversion, source-isolation and native row-format tests also pass.
  Ordinary attacks, spells and off-hand display paths are unchanged.
  Conditional/support effects, crit weighting and broader weapon modifier scope remain separate work.
  The default attack also sends the panel's `hundred_times_damage_per_second` and
  `display_hundred_times_damage_per_skill_use`.
  Live verified: the dual-wield character's APS follows the passives (2.93 → 2.98 after a node).
  Minion, totem and timed/conditional stats are not modelled;
  the character panel's APS stays cached after a passive allocation (reopening the panel or switching its tab does not
  refresh it; only a relog does).
  ([11](../docs/protocol/11-skills.md) §4)
- **Spell cast timing (#192).** Catalog base/virtual cast milliseconds now stay unchanged for native spell actions,
  which apply cast speed themselves.
  Server waits and displayed casts per second share `SpellCastTiming`; attacks keep their existing path.
  The old -80% Fireball recovery capture lasted 18.75 seconds instead of 3.75 due to two divisions by 0.2.
  Automated 0/+100/-80 timing, instant/channel classification, invalid-rate and existing aura/sum checks pass.
  Real-client Q Fireball clips measured 3.750000157 s at -80%, 0.375 s at +100%, and 0.75 s at zero.
  Native virtual base stayed 750 ms; emitted cast rates were 0.27, 2.67 and 1.33 casts/s respectively.
  Projectile release/contact alignment was not separately measured.
  Instant casts remain instant; channel damage cadence and conditional action-speed mechanics are unchanged.
  Numeric server durations below 1 ms are floored, and invalid factors/unrepresentable waits produce no spell effect.
  Code: `SpellCastTiming.cs`, Gateway (`GemSkills`, `CastMs`, `CastGemSkill`, `LaunchProjectile`, `AreaSkillHit`).
  Docs: [11](../docs/protocol/11-skills.md) section 9.
- **Selected spell hit damage panel (#188).** Spell total/per-type ranges and historical pre-crit average damage/DPS
  now use the same converted packet as projectile and area combat.
  All range fields are explicit, including zeros for absent types; hitless spells clear inherited weapon DPS/per-use.
  Final selected spell records use the supported combat packet for spell and inherited main-hand ranges.
  The issue #75 review correction now weights ordinary/supported average damage and DPS with current critical
  chance/multiplier and signed luck, retaining exact action duration and clamping the native integer bounds.
  Attack estimates also include normal-reference landing probability and separate critical confirmation, using
  the ordinary/selected accuracy profile. Raw global/local cannot-evade updates estimates and both ordinary
  main-hand chance aliases; final diagnostic chances do not feed back into estimates. Nineteen added cases
  cover six reproduced omissions, spell controls, actual support link/removal, bypass, luck and bounds;
  the expanded focused run passes 55. Native revalidation, integer-roll expectation, multi-hit,
  dual-hand, cooldown and DoT calculations remain open. See [11](../docs/protocol/11-skills.md).
  This includes explicit zero ranges after spell damage suppression, without changing attack displays.
  Automated mixed conversion/gain, added-damage effectiveness, hitless, attack and native row-format checks pass.
  Native selection/getter checks show Fireball 10-15 fire and Freezing Pulse 10-15 cold with a +12% spell-damage wand.
  A conversion/gain control split Fireball into fire and chaos rows and dealt a noncritical 14-damage server hit.
  Scorching Ray cleared inherited hit ranges and weapon DPS, including after added spell damage refreshed its record.
  Ranges use server floors; corrected averages retain packet fractions and exclude target mitigation,
  per-type integer-roll expectation and cooldown/downtime.
  Beam/DoT displays, conditional damage rules and broader conversion semantics remain outside this slice.
  Code: `SpellHit.cs`, `SpellPanelStats.cs`, Gateway (`GemSkills`, `LaunchProjectile`, `AreaSkillHit`).
  Docs: [11](../docs/protocol/11-skills.md) sections 9 and 14.
- **Critical strikes.** The server rolls unconditional crit passives: the equipped main hand's `WeaponTypes.dat`
  critical chance (permyriad) raised by generic/attack/melee/weapon-shape increased crit chance for attacks,
  each spell's extracted base chance, and 150% plus `base_critical_strike_multiplier_+`;
  critical damage is multiplied and logged.
  Unarmed has 0% base crit.
  Two pure base stats replace that base before the increased chance, the way PoB's `WeaponBaseCritChance` replaces
  an attack pass's `source.CritChance` when that hand has a weapon:
  `override_weapon_base_critical_strike_chance` (Stats row 9892, permyriad) on the armed main hand — Slayer
  Overwhelm (graph id 17315) grants 800 = 8.00% and Blind Prophet's Sightless (graph id 24698) grants 900 =
  9.00% — and `base_melee_critical_strike_chance_while_unarmed_%` (Stats row 3906, permyriad; Behemoth Final Blow,
  graph id 10135, grants 1000 = 10.00%) on the unarmed melee attack.
  Each is a base, not a fixed final chance: generic increased crit still applies on top;
  the weapon override stays out of unarmed and spell rolls, and the unarmed base stays out of a weapon hand.
  Only the active weapon set supplies the attack base and its weapon-specific increases.
  Ordinary added BASE now combines generic and attack/spell-specific raw actor and selected skill/support sources
  after the intrinsic base or its override and before increased chance, with signed wide sums and final half-up cap.
  Native weapon-local flat critical chance precedes local increased chance and its permyriad rounding.
  Linked Increased Critical Strikes supplies its own BASE and increase once; known zero intrinsic chance may receive
  added BASE, while missing/negative data, hitless skills and cannot-crit gates still fail closed.
  Native modifier identities, mode-specific Ascendant Assassin sources, active-set isolation, removal, zero and
  support composition have automated coverage; current combined results are in the panel entry above.
  Sixteen positive Fireball/ICS native controls on `0aabeb6` pass at actual level 100 in both modes.
  Selected permyriad values 500/550/1200/1120 render as 5/5.5/12/11.2%, including actor/support removal, relinking and relog.
  Two current-Actor Fireball-removal controls and two default sentinels also pass without numeric-zero inference.
  The original Standard D0 sentinel remains UNPROVED; its separately reviewed after-capture correction uses current
  Fireball/Default identities and historical C2 numeric evidence, with no simultaneous Fireball receiver claim.
  Its version freeze preceded the Ruthless sentinel capture.
  Canonical stock/charges and protected data held; gem-ID remaps and retained participant state are qualified in
  protocol 11 §5, and normal services were restored.
  Native critical hits, rendered zero and the remaining attack/local/Assassin/rounding contexts remain unverified here.
  Isolated carriers do not establish legal unique acquisition, and the local-flat chance fixture retains its real
  no-extra-critical-damage flag without certifying multiplier behavior. Pinned PoB 3.29.x is a labelled fallback.
  `/set_stat` replaces the input and rebuilds the pools, so the panel and the rolls agree.
  Generic/spell development increases replace the same complete raw input used by spell rolls, including aura sources.
  Attack/spell increased percentages and the pooled spell reference now follow those effective inputs.
  Automated regressions cover these replacements, signed sum bounds and shared integer rounding.
  Attack and spell chances now share half-up permyriad rounding: 650 with +1% gives 657.
  Native evidence below targets original head `8c4c62d`; recheck the changed midpoint and resolved stack natively.
  Five follow-up native checks verify generic/spell input agreement and maximum/minimum integer saturation.
  Twelve native checks verified 830 → 800 → 900, increased chance → 1800, cannot-crit → zero,
  empty-set/unarmed behavior, weapon restoration and relog.
  Ascendancy allocation remains in the separate lifecycle change.
  Global, virtual-global and attack-specific cannot-crit flags suppress the attack chance.
  `SpellCriticalStrike` reads neither base override.
  The final main-hand and spell crit chance/multiplier stats go to the client for the Offence panel;
  selected spell chance and server projectile/area rolls now share raw player and skill inputs once.
  Precision and Zealotry retain their existing bonuses without counting them twice.
  Actual Spell skill types set the panel's casting flag; attacks and non-spell warcries keep it clear.
  Hitless spells and unsupported action types cannot fall through to weapon damage or attack critical rolls.
  Native panel/getter checks show Fireball 5%, Freezing Pulse and Ice Nova 7.5%, and Arc 6%, including after relog.
  Combined generic/spell bonuses add once; the no-crit flag clears the native chance and hides its row.
  A hitless Scorching Ray cast dealt no weapon damage, while the same connection's Fireball dealt a critical hit.
  Catalog, chance, panel gates, aura sources and hit routing also have automated checks.
  Four unconditional MORE/LESS chance sources now run before final cap/rounding. Real Controlled Destruction
  changes selected 10% to 2% for both attacks and spells; its link/requirement/removal lifecycle and actual parsed
  critical draws agree. The real unique -40% source changes an ordinary weapon's 10% to 6%, with explicit zero
  and item removal restoring 10%. Source cancellation, extreme bounds, independent factor composition and
  half-up rounding have regressions; current native verification is pending.
  Focus skill and curse lifecycles, guaranteed-critical effects, off-hand publication/execution and other
  enemy-state/more crit mechanics remain separate work.
  `/crit` shows the attack values and a pooled 5%-base spell reference ([11](../docs/protocol/11-skills.md) §5).
- **Overcapped-resistance modifiers.** While the flag stat is set, the uncapped resistance above its maximum is a
  generic increased modifier, exactly PoB's `ResistOverCap` (the maximum capped at 90): +1% increased evasion per 1%
  of cold overcap, +1% increased critical chance per 1% of lightning overcap, additive with the other increases for
  attacks and spells alike.
  The flags are ordinary stats, so any source sets them: the Surfcaster's Sea Legs notable (graph 61696, with its
  +40% cold-and-lightning pair resistance the aggregator now sums into both elements), an item mod, or `/set_stat`.
  An explicit `/set_stat` value replaces its own input (per-type, pair and all-element resistance, and per-type or
  global maximum resistance) instead of adding to the aggregate, and rebuilds the pools; a Kitava change rebuilds them
  again.
  `PoolsFor` derives the increases once per pool build and stores them in the pools, so the monster-attack evade
  rolls, the panel rows and the spell crit consumers read the same numbers; the uncapped resistance values stay
  untouched, and Iron Reflexes' converted evasion keeps the overcapped increase once.
  Automated checks cover below/equal/above cap, the 90 maximum, both penalties, flag removal and override, additive
  behaviour, spell/attack isolation and the Iron Reflexes conversion.
  Combined automated checks retain the 830 +45% =1204 armed chance and complete signed critical cancellation.
  Purity-aware raw replacements now remove the aura contribution for that exact key, including explicit zero maxima.
  Real aura/catalog/pool regressions pass; the corrected aura-override composition needs a native recheck.
  The resolved stack needs native rechecking; the following thirteen checks belong to original head `39cbda2`.
  Real-client stat checks verified evasion 15 → 22 and spell crit 500 → 725 at 120% resistances,
  raised maxima, replacement of global inputs, both Kitava penalties, flag removal and relog.
  Ascendancy allocation remains part of the separate lifecycle change.
  See [11](../docs/protocol/11-skills.md) §4, §5 and §12.
- **Projectiles.** One chronological runtime drives production gem/bow flight, contacts and continuation.
  It runs at 30 Hz boundaries with subtick contacts and commits damage/rewards before network output.
  Native gem skill records derive projectile count, pierce, chain, fork, split and return from shared parameter rules.
  These display-only projections respect prohibitions and refresh after support removal without changing release inputs.
  Missing projectile metadata allows generic panel parameters while executable release remains unavailable.
  Release captures position and offense; target movement, evasion, defenses and impales resolve on contact.
  Issue #75 accuracy entropy/critical confirmation, one critical roll per use, raw penetration, actual-roll impales and
  direct-hit leech run in this same engine. Resource recovery catches up projectiles before consuming leech; corpses
  retain the ordinary lifetime. Combined automated regressions cover Ward/slot defences, mana conversion and contact
  recovery. Details and test receipts: [merge review](../docs/dev/issue75-review.md#upstream-merge-resolution).
  The later main merge composes Gambler, Vaal credit, incoming ailments and league modes with these stat consumers.
  Combined regressions cover direct-only leech and once-only soul credit under held output, plus mode-aware defences.
  Its [validation report](../docs/dev/issue75-review.md#main-merge-league-modes-gambler-and-vaal-souls) records the decisions.
  Native revalidation of that combined build remains pending; the wider 357-stat goal stays paused.
  Pause freezes the game horizon, transfer/retirement invalidates epochs, and released shots survive later actions.
  Ordinary bows and wands share one release path on that runtime: the shot is scheduled at the accepted input's weapon
  contact (`attack1_bow`, or the wand clip the native ACT selects for the input's sequence) and the action echo follows
  it; a cancelled action, death, freeze or hit recovery releases nothing. The wand bolt plays its rig's impact clip where
  it stops and is removed after that clip on the same game clock, so pause holds it and a transfer drops the removal.
  The arrow stays a second where it hit (`ArrowStuckSeconds`) before removal. Both send `0x1af` (kind 3, the shot's
  action sequence, flags 0x4201 plus the critical bit, code by damage type) on contact for the target's blood and
  particles; the main-hand bit follows the live capture, the skill index and header words it also fills stay zero. Projectile objects are created at their release point and their first move runs from there: the lazy
  snapshot used to create them where the simulation already had them, one to ten cells past the bow in a live log
  (2026-10-29). A Frida trace the same day saw the client create no projectile of its own for the default attack
  (40 arrows) nor for a monster's action, so these server objects are the only visible shots on our server.
  On GGG's server the same shots have no objects and the client draws them; what in our answer stops it is open
  (skill ids `0x102`/`0x103` against live `0x4000`/`0x8002`, and the `0x1af` fields, are the candidates).
  The live hit-timing fit put our arrows at 367 cells/s against live's 308, but the Gateway releases DefaultArrow at
  the table's 3260 units/s = 299.9 cells/s (logged on every release), so the fit's slope measures something else
  (the roadmap). They leave from the
  releasing clip's contact keyframe (`data/animation-contacts.json` positions, `anim_contacts.py` v3): the Ranger's
  bow 5.5 cells ahead along the aim at 16 steps up, a wand's tip 7.5 cells ahead at 12 (the 5-step default, tuned on a
  fireball, had the arrow trail start at the hip, live 2026-10-29); the keyframe's lateral part is not applied.
  Monster projectiles: a mortar skill's shot (Sand Spitter, `monster_mortar_attack` via GrantedEffects SkillID →
  MonsterMortar → Projectiles) is a server object on the same runtime, released at the clip the client plays for the
  skill (MonsterMortar.Animation → the ACT's `SpellProjectile` pool, the native selector on the action's 0xffff
  sequence: attack1, contact at 37.5 % of the attack) from that clip's keyframe (4.2 cells behind the body that faces
  away, so toward the player, 9 steps up: `projectile_uses_contact_position`; `data/monster-contacts.json`,
  `scripts/re/monster_contacts.py`), lobbed at the player's cell but no closer than the skill's
  `projectile_minimum_range` (4.6 cells), team 2, and its landing splash (`Game_MONSTER_MORTAR_RADIUS`, default 10
  cells) runs the monster contact pipeline (`HostileProjectileOffense`, `Program.ProjectileHostile.cs`). Not checked
  live yet: that the client seeds a monster's clip choice from the sequence, and the dead zone. The object plays its rig's flight
  clip from spawn and its impact clip where it lands (`data/projectile-clips.json`, `scripts/re/projectile_clips.py`:
  every Projectiles row's clip indexes and lengths, and its .ao default animation), without which a mortar is
  invisible (its .ao declares no default animation, the wand bolt's does). The data loaders turn their feature off
  with one warning on an unreadable file instead of faulting the pump, and monster ACT pools come from the root block
  only (a nested stance's entries had replaced them for 191 varieties; review 2026-10-30). Weapon-projectile monsters
  (`monster_projectile_weapon`, SkillID 0) still hit instantly with no shot; see the roadmap.
  Wand main-hand variation follows each class ACT pool and the native input-seeded selector; sixteen actual ground
  inputs covered all four Witch clips with matching selection and an object input also matched (historical LocalExile
  evidence on the earlier flight loop; the runtime path still needs a native recheck). Complete off-hand timing/damage
  and nonzero intrinsic attack repeats remain open.
  Real-input held-output, moving-target, wall, evasion, pierce, return, pause, transfer, impale and actor-scale checks
  exercise this same runtime. Native bow/skill controls and animation alignment still require client validation.
  Straight flight uses captured ground plus the release keyframe's height, else `Game_PROJECTILE_HEIGHT` (default 5);
  missing captures use the bare offset.
  Creation, glide and stop retain the same height; independent effects still follow client terrain.
  Platforms/doodads still need a surface model (#182). Integer walk estimates and shared radius remain approximations.
  Code: Core `Projectiles/`, Gateway `Projectiles/` (`Program.ProjectileRanged.cs`), `Program.RangedAttacks.cs`,
  `OrdinaryRangedAttack.cs`, `OrdinaryRangedShot.cs`, `Program.Combat.cs` and `ClientState.cs`.
  Docs: [framework](../docs/game/projectile-framework.md), [11](../docs/protocol/11-skills.md) section 10.
- **Combat.** The player's default attack kills monsters with server-side damage (no monster armour yet; each damage
  type of a hit takes the monster's own base resistance with the hit's penetration subtracted from the capped value,
  `HitMitigation`/`Penetration`; the player's hit lands at the attack animation's contact keyframe, a monster's still
  at half its attack time).
  Monsters use their first granted effect and swing back; their attacks roll player evasion and then go through armour
  and block.
  Ordinary physical melee hits can trigger native player TakeHit recovery through 0x1d1.
  Native proof stopped predictive walking and casting, blocked new actions, and cleared the recovery flag afterward.
  Server recovery cancels pending actions and channel hits and stops the walk estimate for the same interval.
  Recovery or a freeze also suspends a running channel: it pays and sweeps nothing until the player recovers.
  That channel suspension has automated coverage only, with no native check yet.
  Actual physical monster hits stopped walking and cancelled a slow cast before release in copied-save live checks.
  Released Fireball flight continued, and a stun-immune control took damage without recovery.
  With a bow equipped, a hit cancelled pending Burning Arrow, blocked an ordinary arrow during recovery,
  and allowed a fresh arrow afterward; an already released Burning Arrow kept flying.
  Chance, avoidance, immunity, recovery and delayed object walk-ups have automated checks.
  ES builds, substituted thresholds and unsupported conditional stun defences are skipped conservatively.
  See [11](../docs/protocol/11-skills.md), player hit recovery, for scope and protocol evidence.
  The player's life comes from the class/level/passive pools.
  The server has no shared simulation loop: hits use timers and attack time is bounded by a 30/s tick (`TickRate`).
  Monster decisions use 50 ms game-time steps, a separate 100 ms repath budget and a three-cell target threshold.
  Full attack duration, pending-swing generations and stage completion guard the faster planner against duplicate work.
  Native controls verified initial response, deliberate turns, full attack pacing and actual menu pause/resume.
  Core, Data and Gateway suites pass, including cadence, pending-swing and stage-completion regressions.
  Item mods count through the global item stats;
  346 varieties with NPC / ControlZone / ... components are refused.
- **Critical-strike elemental resistance rules (Inevitable Judgement).** The hit's
  already-rolled critical boolean decides the resistance: the packet rolls once (the original per-type draws), the
  existing single critical draw keeps its original place after them, and the mitigation resolves on the
  already-rolled packet without a second roll — a critical elemental hit with
  `critical_strikes_ignore_elemental_resistances` mitigates with an effective resistance
  of zero, negative included, overriding any penetration; the attack-only variant covers attacks only; a
  non-critical elemental hit keeps its capped resistance and `non_critical_strikes_penetrate_elemental_resistances_%`
  (10) subtracts after the cap, stacked with the hit's general penetration;
  `cannot_penetrate_or_ignore_elemental_resistances` restores the plain capped resistance.
  Physical, chaos and damage over time are outside the rules; Inevitable Judgement (graph node 48214) grants the
  generic ignore and the 10 penetration through the ordinary passive stat sum.
  The shared helper (`HitResistances`, the Gateway's `MonsterDamage`) covers default attacks, bows and gem hits.
  Default and area damage logs name the decision; gem projectiles emit a separate resistance trace.
  Core and Gateway checks cover positive/negative resistance, maximum independence and the node's exact stat keys.
  Eight native checks read monster life: fixed 100 fire dealt 60 normally and 70 with non-critical penetration.
  Guaranteed critical hits dealt 150 with generic or attack-only ignore, and 90 after removal or prohibition.
  Those captures belong to original head `13c41e5`; combined penetration/modifier/bow paths need native rechecking.
  Automated checks preserve same-key source sums, fractional totals, target damage taken and the original RNG order.
  Sequential C24 handler tests read real `/set_stat` changes after a prior hit on the same connection and target.
  Skill `0x102`, sequence 0 and flags `0x408` give ignore 300, ordinary critical 330 and prohibition 240.
  Friendly and hostile controls pass without a production hostility change.
  The combined run labelled `462c883` used a DLL proven by hash to be #225 `4a71968`; its ninth case is not #226 proof.
  Ascendancy allocation remains in its separate lifecycle change.
  Code: `HitResistances.cs`, `HitMitigation.cs`, Gateway (`MonsterDamage`).
  Docs: [20](../docs/protocol/20-damage.md) §4.
- **Damage model (foundation, with armour/evasion).** A hit is a per-type `DamagePacket`;
  `DamageConversion` handles the game's physical, lightning, cold, fire, chaos order, chained conversion, skill/global
  precedence, the 100% cap and gain-as-extra.
  DoT is a single type with fractional tick carry, and `Ailments` has the ignite/bleed/poison base formulas.
  The default attack, Fireball and monster melee build packets and read the player's/skill's conversion stats;
  packet mitigation applies each type's own resistance, and the physical part then goes through armour.
  Righteous Fire's burn uses the DoT tick model.
  A damage-type immunity (`*_immunity`, `elemental_damage_immunity`) zeroes that type on the player's hits, from the
  same sources the panel's Immune rows use (`DamageImmunities.cs`).
  Ordinary player energy shield derives from equipment, intelligence and passives.
  Equipment ES uses the saved variable base roll and native rounding before and after local modifiers and quality.
  It absorbs non-chaos hits before life and recovers through regeneration and delayed, damage-interrupted recharge.
  The existing 16 ms elapsed-time loop supplies the client's native ES recovery prediction.
  Current ES and its prediction are sampled together at the network send gate, including duplicate suppression.
  Encrypted-wire held-gate tests cover a queued clamp, cleared prediction, death and unchanged-state suppression;
  native checks of this queued-send case remain pending.
  Live checks covered absorption, regeneration, interrupted recharge and chaos bypass.
  Special ES keystones, recoup and conditional substitutions remain unimplemented; ordinary ES leech uses the shared recovery writer (§5b).
  See [ES scope](../docs/protocol/20-damage.md#player-energy-shield-2026-10-25).
  Maximum-mana added lightning has original-head native checks and resolved automated checks.
  Not yet: exposure, aura-dependent penetration, other added damage,
  increased/more support modifiers and outgoing ailments other than projectile ignite ([20](../docs/protocol/20-damage.md) §3).
  Incoming ordinary monster basic-contact ailments now have runtime and Gateway coverage:
  critical fire ignite, cold chill/freeze and critical lightning shock, with avoidance, expiry, immunity, lethal ignite/ES and freeze action cancellation.
  Native checks verify real monster ignite/avoidance, lethal death-panel/checkpoint behavior and nonlethal RF.
  Alternative ailments and hostile spell adapters remain unimplemented; monster mortar shots land through the same
  contact pipeline ("Monster projectiles" below).
  See [Gambler](../docs/game/gambler.md) for the exact bounded scope.
  Unconditional penetration works on the player's outgoing hits (general per-element and elemental stats, the
  attack-only variants, the cannot-penetrate flag; DoT and physical take none).
  Player and skill contributions to the same raw penetration stat add without overwriting either source.
  Nine native checks verified fixed 100-fire hits for 60 → 80 → 90 → 100 damage as penetration stacked,
  the prohibition returning 60, cold penetration giving 120 against zero resistance, and chaos 75 → 105.
  Each check read the actual native monster life after the authoritative update.
  Those captures belong to original head `0d800d1`; resolved bow and target-modifier paths need native rechecking.
  Target mods compose with base resistance before penetration; damage taken follows mitigation and rounds once.
  Ten fresh composed native groups verify critical rounding, active overcap and default-attack mana lightning.
  Actual mixed-hit Life losses are 160/220 before criticals, then 300 with ignore, 330 without and 240 prohibited.
  Build versions and source hashes match `1366f48`; supported-gem, modifier and bow combinations remain automated.
- Continuous life/mana recovery (2026-10-24): regeneration, flask recovery and self RF now share a 16 ms
  elapsed-game-time loop, replacing regeneration's 250 ms jumps; pool fractions and caps are preserved.
  Implemented with regression tests; real-client visual verification after Gateway restart is pending.
  See [20](../docs/protocol/20-damage.md), "Continuous resource updates".
- **Cyclone area of effect (2026-10-25).** `/set_aoe` now resends Cyclone's calculated final radius in the skill
  record, using the same radius calculation for server hits.
  Cyclone/CycloneAltX use a 16-cell base when their exported stat set has none.
  The player's increased area counts once for every area skill (`AreaPlayerStats`); it counted twice before.
  Spell-only area does not affect the attack.
  Tested calculations; live visual check pending.
  The first live check found that direct radius alone did not scale the channelled visual;
  its conditional path now also receives aggregate area (0xd7) and radius percentage (0x1591).
  Cyclone's additional melee-range and stage modifiers remain partial.
  See protocol/11-skills.md.

## Not working yet

- Projectile spawn height on platforms and doodads: the protocol carries no player z and the server has no
  surface-height model, so a projectile on the hideout waypoint platform spawns at the terrain height below it.
  Tracked in issue #182.
- Effects that should be attached to bones (Righteous Fire's orbs, MiscAnimated RighteousFireOrb) are not sent;
  an effect object follows the player only by 0x1ba glides.
  Combat auras, Heralds and banners are not implemented; only the simple auras above use buffs.
