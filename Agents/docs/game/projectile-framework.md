# Projectile framework

`src/GameServer.Core/Projectiles/` supplies the single chronological simulator.
`src/GameServer.Gateway/Projectiles/` binds player skills, combat state and native presentation to it.
Ordinary bows and catalog-recognized projectile gems use these same production entry points.
Catalog fallback provides basic straight flight; a registered definition supplies special behavior.
Missing metadata or an unsupported damage plan is refused before action costs.

## Simulation and combat

Time uses 120000 integer quanta per second, with 30 Hz boundaries and exact chronological contacts
inside each boundary. Actor generations distinguish removed/replaced targets. The host serializes gameplay
under XP-before-Monsters locking and advances pending projectile history before replacing actor motion or life.
Hit, charge, Rage, reward and boss-stage changes commit synchronously. An ordered outbox presents the committed
results without waiting for network output inside the combat lock. Pause freezes the game clock; area transfer,
death/retirement and connection closure invalidate the appropriate owner epochs and pending work.

Release captures position, offensive stats, speed and critical inputs. Evasion, block, resistance, impale state,
conditional critical restrictions and the actual caster's spell-damage prohibition resolve at contact.
Gem release always retains separate direct and area damage packets, including for initially direct-only roots
whose children or release specialization introduce areas. Area increases and area-only support multipliers
apply to the area packet once; stage multipliers and projectile/form/return multipliers compose at impact.
Non-projectile damage callers retain their own classification.

### Issue #75 integration

An accepted skill use captures critical luck once and shares its `SkillCriticalRoll` across the entire volley,
continuation children and secondary areas. Release captures the unprohibited chance and multiplier; live
prohibitions still apply at contact. The selected accuracy profile includes linked support and weapon-local
always-hit sources. Attacks consume the target's entropy once, then independently confirm a possible critical;
spells do neither. Misses consume no continuation budget or on-hit effects, while blocks retain their existing
continuation policy. Direct damage keeps conversion history, penetration and separate direct/area packets.

Impale stores the actual rolled physical hit with the source critical multiplier, then resolves reflected damage
separately from direct damage and target critical mitigation. Spell-only and spell-critical impale sources retain
their own release settings. Leech receives only the accepted direct damage, with separate Life/Mana/ES sources
and the branch's instant, conversion, retention and doubling policies. Neither evasion, block nor reflection grants
leech. Corpses retain impale state and remain until area teardown, matching ordinary kills; level-up refills trim
ordinary leech immediately.

Leech configuration/conditions use the committed simulation horizon when a projectile session exists. The recovery
writer advances projectiles under XP-before-Monsters locking before consuming recovery at that boundary. This
keeps delayed pump contacts in chronological order instead of ageing their instances to wall-clock time first.
Decision: integrate these consumers into the one runtime, rather than retaining `FlyProjectile` as a second path.
Tests use explicit simulation time and held output; waiting for a network send is no longer a contact barrier.
Non-projectile sweeps retain one damage snapshot plus their shared critical roll, but resample critical chance and
prohibitions per contact so an intervening source change keeps the branch's existing behavior.

Gambler's less-damage factor applies once in the outgoing packet; its contact roll applies luck/triple damage
without reapplying that factor. Resistance inversion joins the same typed mitigation pass that supplies leech.
Existing impale reflection is added afterwards, without another critical, triple-damage or leech application.
Vaal soul credit commits once with lethal Life/Dead state, before network publication, using the shared death
boundary. Keeping a second impale/accuracy block from main would duplicate those mechanics and lose the branch's
entropy, actual-roll storage and typed leech. Pending gem releases retain the combined stun/freeze interruption
generation as well as the shared critical roll; already released flights keep their existing lifecycle.

The merge reports and alternatives are in [the issue #75 review](../dev/issue75-review.md#upstream-merge-resolution)
and [the later main integration](../dev/issue75-review.md#main-merge-league-modes-gambler-and-vaal-souls).
These automated composition checks do not replace native validation of the merged runtime.

## Continuation and hit admission

Continuation priority is split, pierce, fork, chain, then return. Prohibitions override the corresponding grant.
Finite pierce consumes one grant per admitted contact, then hands off to the next available continuation.
Boolean fork sources establish one base generation; explicit additional-fork counts add generations.
Raw and resolved aliases are combined without counting the same source twice. Count conversions declare
their consumer so an extra projectile cannot expand both primary emission and a secondary stage.
A declared non-primary consumer retains quantity when a competing split conversion is prohibited by the
profile or modifiers. Competing viable conversions remain an error; primary count conversion is unchanged.

The common player defaults share direct-hit admission by emission, separately for outbound and return phases.
Denied siblings pass through that victim. An evaded contact keeps flying without consuming continuation.
Independent area events have independent admission and may overlap any geometrically covered target.
Core contact tests combine projectile and actor radii; area tests combine area and actor radii. The Gateway
adapter currently passes zero actor radii, so area coverage checks target centers, including for large monsters.
Monster catalog `ObjectSize` feeds movement blocking but has not been calibrated as a circular combat radius.
A terminal collision victim shares direct admission with its terminal area, receiving one area-classified hit
instead of a direct hit plus another blast. An evaded terminal victim does not end the flight.

Contact areas run at admitted impacts while continuation remains possible. Terminal areas and end emissions
wait until no continuation, including return, remains. Recipes select terminal reasons and can exclude a
contact victim when the mechanic requires it. Offset areas support one sample per angular sector, a minimum
placement distance and skipped terrain-obstructed sectors. Radius, placement radius and damage multiplier
are separate inputs. Area recipes have a first-slot `Delay` and inter-slot `Interval` in simulation quanta;
zero defaults remain synchronous. Centers and trigger flight state are captured before continuation, while
actors and defenses resolve at each detonation. Pending areas survive their parent flight, retain admission
history, pause with simulation time and clear on teardown. Terrain-blocked centres are not resampled and do not
consume time slots: surviving areas start at `Delay` and follow consecutively at `Interval` spacing.
Sector sampling and recipe timing values do not establish official random distributions or measured timing.

`EndAreaOnReturnTurnaround` lets a recipe emit an allowed endpoint area before a successful return starts.
It respects the selected end reasons and leaves child emissions deferred; the default keeps terminal areas deferred.

Return preserves remaining split/pierce/fork/chain counts and the original lifetime. An expired projectile
cannot start another flight. Returning children retain their phase and cannot return a second time.
`returning_projectiles_always_pierce` explicitly grants unlimited return piercing; ordinary return does not.
Terrain collision remains enabled unless the profile requests otherwise. Common player recipes exclude
the turnaround victim and continue 150 cells past the captured caster position. The fixed overshoot and
remaining-count implementation are approximations; moving-anchor tracking and exact engine equivalence
are not established. Phase-specific hit rules, contact caps, speed and terrain policy remain configurable.
`OutboundHitLimit` is an independent cap on actor attempts while traveling to the endpoint; finite pierce
must use `Pierce`, never that cap.

## Range and action speed

A projectile movement instance is capped at 150 client units = 15 m (10 units per metre, the same scale as the
Gateway's cells). `ProjectileProfile.Range` now defaults to 150; the Gateway reads
`system_settings.projectile_maximum_range_cells` for every new profile, so an admin can shorten or lengthen the
baseline without a rebuild. `projectile_distance_override` sets an exact distance from the client tables, and
`projectile_maximum_range_override` (a skill/support stat in the same units, e.g. 48 = 4.8 m) caps either value. The
3.29.3b player tables do not currently assign that stat, so it is a safety net for future skill and support rows.

Fork, chain and split start a **new movement instance** at the continuation point, so each leg gets a fresh range
budget and the total path can exceed 15 m; `ProjectileEntity.Distance` keeps accumulating for Far Shot and the
flight arc. The return leg keeps the measured 150-cell overshoot past the captured caster rather than taking a
plain fresh budget.

Action speed is not a projectile input. It changes attack, cast and movement pacing, while projectile speed only
changes the time to the endpoint (and, for limited-duration projectiles, how far they get before expiring, up to
the same cap). An action-speed theory of longer projectiles is therefore not supported by the client rules: the
observable "beyond 15 m" cases are continuation legs, not faster actions. The Gateway composes `action_speed_-%`
additively with chill/freeze, multiplies the final attack/cast/movement/stun-recovery paces, and limits new actions to
one per server frame while exempting channelling repeats. Tailwind, Harbinger, Temporal Chains, trap/mine/warcry/totem
timings and monster floors remain [#246](https://github.com/LocalExile/LocalExile/issues/246).

## Skill definitions and presentation

`ProjectileSkillParameters` owns profile resolution and optional native panel-stat projection.
`ProjectileSkillDefinition` combines those parameters with metadata, optional terrain admission, windup
cancellation, release specialization and presentation.
Paid attacks normally survive Move; spells and definitions with release-time resources retain action cancellation.
Cooldown tickets, death, recovery and
epoch restrictions still apply. Specialization runs once in the simulation commit and may provide frozen
per-initial-projectile payloads; continuation children inherit their parent's payload.
Accepted hit recovery permanently cancels unreleased paid attacks, even after recovery expires;
already released flights continue independently.

Gem skill records resolve the same profile for native projectile-count, pierce, chain, fork, split and return inputs.
Prohibitions override displayed continuation grants; unlimited pierce uses the native all-targets flag.
Arrow pierce display aliases are cleared after their applicable contributions enter the combined profile budget,
so they cannot shadow the generic total or the cannot-pierce row.
Final display values modify only the outgoing skill record, never raw supported stats or later release calculations.
Parameters may project specialized stage counts through `PanelStats` after the common projection.
Return descriptions distinguish guaranteed return from a percentage chance; prohibited return suppresses both
and the return-pierce description. Cannot-pierce also suppresses the return-pierce description.
Catalog projectile skills without native projectile metadata still resolve known generic parameters and skill overrides.
This stat-only path never supplies executable metadata or bypasses release admission; special mechanics remain
dependent on skill coverage. An invalid parameter recipe omits projection without blocking the remaining skill list.
Native description templates choose the wording and visibility.

`ProjectilePresentation` supplies flight mesh/clip, second-form clip and event effects. Non-gem sources may
supply presentation directly through `ProjectileOffense`. The common host owns object lifecycle, terrain
height, arcs, redirects and effect cleanup. Core actor/team contracts are source-neutral. The Gateway has two
damage adapters: the player's shots hit monsters, and a monster's shot (`HostileProjectileOffense`, team 2:
`Program.ProjectileHostile.cs`) hits the player through the monster's own contact pipeline, as a mortar landing on
the aimed cell with a splash (`Game_MONSTER_MORTAR_RADIUS`, default 10 cells) or a straight shot's first contact.
Monster projectiles are known for mortar skills (`SkillCatalog.MonsterProjectile`); weapon-projectile skills still
hit instantly at half the attack time. A mortar leaves at its clip's contact keyframe and from that keyframe's point
(`MonsterRelease`, `data/monster-contacts.json`), no closer to the monster than the skill's minimum range; an ordinary
arrow or bolt leaves from the class clip's keyframe (`data/animation-contacts.json` positions). Objects are created at
the release point and their first move runs from there; the visual trails the simulation by the output delay and
catches up at the stop. GGG's server sends no object for a player's bow shot, default attack or gem: the live client
draws its own arrows, which fly about 308 cells/s after a release about 258 ms into the attack. It sends none for a
Sand Spitter's mortar either ([port record](../dev/ranged-monster-actions-port.md), "Live reference").
Here a gem use's primary projectiles get no object (`ProjectileOffense.ClientPredicted`): the client's projectile
action makes them itself, Fireball's and Spark's alike ([protocol §10](../protocol/11-skills.md#10-projectiles-experimental)).
The client still needs the skill's stats to draw them right: its sparks live `skill_effect_duration`, which the
Gateway now sends with every gem skill that has a base duration.
A flight that has already ended when its creation snapshot runs is still created when its stop keeps the object
(impact clip, stuck arrow), so
a short shot on a busy connection appears at its impact instead of not at all; other ended flights are skipped.
Area event effects use the actual event radius and the selected art's exported radius to set uniform native scale.
Both callback and payload effects use this conversion; art without a known radius keeps its native scale.
Endpoint logs include the stop reason, position and projectile-grid state; area logs include art, radius and scale.
Straight flights use `(captured terrain height ?? 0) + the release point's height` as their fixed height marker:
the releasing clip's contact keyframe in steps (`ProjectilePresentation.LaunchHeight`: the Ranger's bow 16, a wand 12,
the Sand Spitter's spit 9), else `Game_PROJECTILE_HEIGHT`, whose integer setting defaults to 5 steps (39.0625 world
units upward), including for an invalid setting.
Creation, glide and stop retain the same signed height. Missing captures use the bare offset as a provisional
absolute height, preserving the main flight contract; this is not client-sampled terrain plus a relative offset.
Lobbed arcs use the configured lift for the initial start when both ground endpoints are known; later bounces
start at ground level. Without both endpoints they retain client terrain sampling instead of a guessed parabola.
Independent area effects still follow client terrain. Ground clipping and platform/doodad surfaces remain unverified.

### Aimed height

Generated levels leave cliff faces open in the projectile grid: on 1_1_2 seed 2032549229 a face rises 2 → 13 → 23
height steps over two cells with no blocking cell, so every arrow flew over it. `IProjectileWorld.TraceFlight` lets the
host trace a flight segment per projectile (default: the plain `TraceTerrain`); the Gateway's `ProjectileAimHeight`
gives a player's primary projectile the higher of its release cell's and aimed cell's ground, and a cell whose terrain
rises more than `ClearanceSteps` (12, 93.75 world units) above that ground stops it as a wall (`Terrain`). A shot aimed
at the low ground in front of a cliff hits the face; aimed at the walkable top it clears the face; shooting down never
meets rising terrain. The projectile grid still blocks as before. Returns, continuation children, monster shots, area
placement and target selection keep the plain grid, as does any area without height data. A server-created object of
such a shot flies at its flight ground plus the release height. LocalExile model v1 from the user's live observation
(2026-10-30), not the client's collision code: the clearance is a development value, the flight is level rather than a
measured arc, and the wall-hit sound of a stopped server arrow is not sent.

Automated checks exercise the production runtime and session, including held-output gameplay, moving
targets, walls, evasion, continuation combinations, real item stat sources and lifetime boundaries.
Native animation alignment, platform/doodad surfaces and visual equivalence still require client validation.
Actor motion uses integer walk estimates and shared collision radii; basic catalog flight does not establish
complete behavior for every skill.

See [projectile skills](projectile-skills.md) for registered behavior, evidence and client checks.
