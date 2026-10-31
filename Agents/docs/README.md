# Documentation index

Protocol reference and reverse-engineering notes for the Path of Exile 3.29.3b client. What works now is in
[../Status.md](../Status.md), what to build next in [../Roadmap.md](../Roadmap.md). Start with
**[protocol/00-overview.md](protocol/00-overview.md)**.

## Protocol (consolidated, current)
| Doc | Content |
|---|---|
| [protocol/00-overview.md](protocol/00-overview.md) | processes, ports, the full message sequence from launch to the HUD, conventions |
| [protocol/01-login.md](protocol/01-login.md) | login connection: key blobs, handshake, framing and encodings, messages, our login server |
| [protocol/02-instance-connection.md](protocol/02-instance-connection.md) | instance client, connect path, opening, 0x11 encryption, pump, handler groups, dispatcher, tick |
| [protocol/03-area-and-world.md](protocol/03-area-and-world.md) | 0x1d, 0x7d, 0x22, world queueing and activation, 0x21, spatial index |
| [protocol/04-player-entity.md](protocol/04-player-entity.md) | object type register (MurmurHash2), 0x2b9, the component blob and every component |
| [protocol/05-inventories-and-ui.md](protocol/05-inventories-and-ui.md) | inventory manager, 0xc0, Inventories.dat, 0x3d Atlas tree presets |
| [protocol/07-latency-visibility-movement.md](protocol/07-latency-visibility-movement.md) | tick/ack and HNC, wake zone (visibility), movement |
| [protocol/08-area-transitions.md](protocol/08-area-transitions.md) | area transitions: the object and blob, tile objects (model, rotation, height), the atlas, every area's transitions in the Gateway |
| [protocol/09-area-change.md](protocol/09-area-change.md) | changing area through an exit, instance details (0x27), the instance chooser |
| [protocol/10-chat-and-commands.md](protocol/10-chat-and-commands.md) | chat input (0x15), server replies, stats update (0x1b3), dev commands |
| [protocol/11-skills.md](protocol/11-skills.md) | skills: ActiveSkillManager, basic actions from the .ot, the Actor skill list, the default attack, stat keys, gem casts, buffs (0x1c1/0x1c2), Righteous Fire |
| [protocol/13-items.md](protocol/13-items.md) | items: types and the item data, inventories with items (0xc0, 0xbf), moving, sockets, the ground, equipment on the model, requirements, gem skills |
| [protocol/14-monster-packs.md](protocol/14-monster-packs.md) | monsters in areas: MonsterPacks, rarity, room points (the camp's "mapboss"), the doodad hook and room fitting |
| [protocol/15-progression.md](protocol/15-progression.md) | experience and levels from kills, the Passive Skill Tree (0x33, 0x34), loot |
| [protocol/16-waypoints.md](protocol/16-waypoints.md) | waypoints: the object, activated waypoints (0x4c), the World panel (0x7b), travel (0x4d), quest flags in the Player component, what reveals pins |
| [protocol/24-vendor-recipes.md](protocol/24-vendor-recipes.md) | vendor sale quotes, common recipes, priorities, atomic payment and remaining parity gaps |
| [protocol/25-gem-supports.md](protocol/25-gem-supports.md) | linked supports, native badges, shared combat calculations and execution gaps |
| [Vaal souls](protocol/27-vaal-souls.md) | Actor budgets, collection policy and pending native acceptance |
| [protocol/28-player-trade.md](protocol/28-player-trade.md) | trade between players: requests (0xba, 0xb0), known players (0xa7), the window's 0xb8 flags and acceptance, party/friend requests read |
| [protocol/17-npcs.md](protocol/17-npcs.md) | NPCs: the object, town placements, talking (0x24 → 0x84), dialogue closed (0x85) |
| [protocol/21-hideouts.md](protocol/21-hideouts.md) | hideouts: unlock flags and league quest flags, the Hideouts keys, `.hideout` decorations, /hideout (0x16 command 45) and the Hideout button (0x17a), HideoutDoodad data, the map device and its window (0x7b kind 5) |
| [protocol/18-stash.md](protocol/18-stash.md) | the stash: the object, tab lists (0x21, 0x136), opening (0x7b kind 1), tab contents (0xc7 → 0xc0 + 0xc8), the account's stash |
| [protocol/19-gems.md](protocol/19-gems.md) | gems: the SkillGem item data, levels from experience, the game data for levels, variants, Vaal gems, requirements, quality and supports |
| [protocol/20-damage.md](protocol/20-damage.md) | damage packets: types and conversion order, gain as extra, per-type mitigation, damage over time and ailments |
| [protocol/29-client-combat-signals.md](protocol/29-client-combat-signals.md) | research: `0x1af` feedback reader and callbacks, absolute resource assignment, limits of client damage prediction evidence |
| [protocol/22-pause.md](protocol/22-pause.md) | pause from the Escape menu: C→S 0x1d4, S→C 0x1d3 (world +0xb9c/+0xb9d), what the Gateway freezes |
| [protocol/23-microtransactions.md](protocol/23-microtransactions.md) | microtransactions: the owned lists and their loaded flags (C→S 0x1f2, S→C 0x1f4/0x1f5), the Cosmetics tab (C→S 0x1f9, the worn table in S→C 0x1f3), cosmetics on the model and in the stats (ItemisedVisualEffect) |
| [protocol/12-quests.md](protocol/12-quests.md) | quests (research): the data tables, a1q4 "Breaking Some Eggs" and the Submerged Passage blocker |
| [protocol/06-debugging-log.md](protocol/06-debugging-log.md) | every live run, crash and fix, and lessons |
| [protocol/address-index.md](protocol/address-index.md) | all client addresses by subsystem |

## Generated references
| Doc | Content |
|---|---|
| [protocol/schema.md](protocol/schema.md) ([json](protocol/schema.json)) | read sequence of every component deserializer and of every S→C message class (instance, world, login, patch) |
| [protocol/c2s.md](protocol/c2s.md) ([json](protocol/c2s.json)) | every C→S message (instance, login, patch): class, writer, length, senders |
| [re/components.md](re/components.md) ([json](re/components.json)) | every component class the client registers: pool, factory, vtables |
| [re/object-types.md](re/object-types.md) | component list of every object type (`Metadata/...`), from the `.ot` files |

How to regenerate them: [re/tooling.md](re/tooling.md) §7.

## How-to

[Projectile framework](game/projectile-framework.md): sole production simulator and skill-provider boundary.
[Projectile skills](game/projectile-skills.md): Fireball, Kinetic Blast, catalog policies, evidence and client checks.
| Doc | Content |
|---|---|
| [../GettingStarted.md](../GettingStarted.md) | setup from a fresh clone, third-party tools, troubleshooting |
| [re/tooling.md](re/tooling.md) | Ghidra headless scripts, Python helpers, GGPK dump tool, Frida through REA, recipes |
| [re/client-damage-calculation.md](re/client-damage-calculation.md) | client Hit/DoT research: Stats overrides, formulas, resource integration, incoming assignment and unresolved prediction edges |
| [re/client-stat-evaluation.md](re/client-stat-evaluation.md) | actual network Actor/skill provider chain, update freshness, native transactions and startup guards |
| [re/client-native-contribution-callers.md](re/client-native-contribution-callers.md) | 103 retained direct contribution callsites, receiving-object provenance, graph-owned tokens and remove-then-rebuild policy; target DoT publication remains open |
| [re/client-stat-dependency-order.md](re/client-stat-dependency-order.md) | native declared-dependency ordering, cycle diagnostics, growing transaction queues and actual duplicate declarations |
| [re/client-dot-routing.md](re/client-dot-routing.md) | typed DoT mitigation, healing, ES/Mana/Life routes, self-rate calculations and exact cap/overflow boundaries |
| [re/client-offence-estimates.md](re/client-offence-estimates.md) | both-hand typed damage ranges, base/added scaling, conversion, averages/DPS and actual numerical UI readers |
| [re/client-spell-damage-components.md](re/client-spell-damage-components.md) | thirty spell base/intermediate/selected-added equations, wand splitting, typed qualifications and arithmetic boundaries |
| [re/client-spell-contributor-builders.md](re/client-spell-contributor-builders.md) | life-cost, hand-cast, Battlemage and weapon-as-spell contributors with native qualification tables |
| [re/client-spell-hit-formulas.md](re/client-spell-hit-formulas.md) | twelve final spell ranges, ordered equations, conversion histories and special branch asymmetries |
| [re/client-attack-hit-formulas.md](re/client-attack-hit-formulas.md) | twenty typed attack ranges and four hand totals, ordered equations, base/added scaling and endpoint differences |
| [re/client-integer-resource-mutations.md](re/client-integer-resource-mutations.md) | integer pool deltas, arrow Life sacrifice, skill-cost preparation/check/payment and conditional caller gates |
| [re/client-terminal-mana-refunds.md](re/client-terminal-mana-refunds.md) | conditional FireMortar/zero-reason Mana refund slices, cost-stat layout, percent cap and wrapped-product conversion; invocation limits remain explicit |
| [re/client-pool-maximum-refresh.md](re/client-pool-maximum-refresh.md) | actual maximum-stat subscriber, client-mode current-value gate, Life carry reset and conditional rescaling arithmetic |
| [re/client-recharge-rate-authority.md](re/client-recharge-rate-authority.md) | actual recharge-active notifications, delay-reset ordering, no-op client state publication and separate total recovery-rate integration |
| [re/client-resource-read-order.md](re/client-resource-read-order.md) | captured recovery rates versus later pool state, sequential Life rates, preloaded ES rates and actual callback boundaries |
| [re/client-resource-checkpoint-composition.md](re/client-resource-checkpoint-composition.md) | peer-established World checkpoint and parsed resource dispatch composed with actual setter/recovery arithmetic |
| [re/client-network-rate-composition.md](re/client-network-rate-composition.md) | parsed owner Stats updates through real overrides, notifications and Life loss; full-step checkpoint and raw/final-rate distinction |
| [re/client-initial-stats-rate.md](re/client-initial-stats-rate.md) | original initial Stats bytes through real overrides and Life loss; compact signed/unsigned decoding, entropy transport and bounded setter provenance |
| [re/client-critical-link-export.md](re/client-critical-link-export.md) | Critical Link action slots, ordered raw Stats export and separate non-recovery Buff transport; grant publication remains unbound |
| [re/client-buff-clock-authority.md](re/client-buff-clock-authority.md) | Actual insertion, owner timing-stat notifications and retained network clocks; initializer floor versus received speed and explicit refresh limits |
| [re/client-buff-replacement-composition.md](re/client-buff-replacement-composition.md) | Original retained-Buff replacement construction, observer housekeeping and selected field transfer; identity/applicability retention, received clock precedence and independent resource-rate integration |
| [re/client-buff-rate-independence.md](re/client-buff-rate-independence.md) | Original Buff expiration/removal composed with independent network rates and subsequent Life loss; source-rate publication remains open |
| [re/client-hit-result-resource-composition.md](re/client-hit-result-resource-composition.md) | Actual received-result processing versus resource assignment; current target ES and the u16 sequence-derived stun trial, with explicit empty-listener limits |
| [re/client-hit-checkpoint-resource-composition.md](re/client-hit-checkpoint-resource-composition.md) | Original release/result processing joined to nested World checkpoints and independent Life/Mana/ES assignments; conditional order/resource contract, not numerical Hit damage |
| [re/client-hit-action-present-composition.md](re/client-hit-action-present-composition.md) | Sequence-matched current Fireball Action, original callbacks and typed event cleanup; eight supplied-state compositions, separate from numerical Hit damage |
| [re/client-buff-initial-granted-values.md](re/client-buff-initial-granted-values.md) | initial Buff magnitude eligibility, byte-order distinction and prefix-preserving normalization |
| [re/client-buff-wire-magnitudes.md](re/client-buff-wire-magnitudes.md) | AddBuff writer/reader magnitude gate, uncounted definition slots and wire-cardinality agreement |
| [re/client-buff-observer-attraction-shape.md](re/client-buff-observer-attraction-shape.md) | concrete no-op Buff observer callbacks and a separately guarded presentation output |
| [re/client-coldsnap-buff-backend.md](re/client-coldsnap-buff-backend.md) | exact Cold Snap Buff/factory joins and concrete animation/matrix backend boundary |
| [re/client-firewall-clock-hold.md](re/client-firewall-clock-hold.md) | current owner network stat sampled after duration decrement to restore a captured Buff clock value |
| [re/client-buff-totem-degen-capacity.md](re/client-buff-totem-degen-capacity.md) | actual Buff granted capacity consumed by reason-4 Life loss, including exhaustion and original-delta overflow |
| [re/client-dot-source-consumers.md](re/client-dot-source-consumers.md) | actual Buff recovery/deferred-loss forecasts and first-eligible source context for Life callbacks |
| [re/client-hit-script-listeners.md](re/client-hit-script-listeners.md) | four concrete Actor Hit signal subscriptions and asymmetric captured-program context inputs |
| [re/client-hit-resource-programs.md](re/client-hit-resource-programs.md) | one packaged Ferrofluid Hit-listener program, typed capture arguments and named-task detach cleanup |
| [re/client-stat-preload-epk-collector.md](re/client-stat-preload-epk-collector.md) | PreloadFromStats row provenance and installed callable collecting .epk resource handles |
| [re/client-ailment-outgoing-formulas.md](re/client-ailment-outgoing-formulas.md) | outgoing bleed/ignite/poison components, per-minute rates, faster/slower and critical DoT multipliers |
| [re/client-bleeding-policy-inputs.md](re/client-bleeding-policy-inputs.md) | effective Crimson Dance, stack-limit replacement, moving override and exerted aggravation chance through original native dependency propagation; actual instance selection remains open |
| [re/client-bleeding-output-family.md](re/client-bleeding-output-family.md) | complete named MH/OH min/max bleeding rates, critical branches and hand/final-factor asymmetries |
| [re/client-ailment-eligibility.md](re/client-ailment-eligibility.md) | typed ignite/poison permissions, hand-local poison gates and target acquisition prohibitions |
| [re/client-ailment-chance-inputs.md](re/client-ailment-chance-inputs.md) | ordinary and critical-only bleeding/ignite/poison chance inputs, qualifiers and integer arithmetic |
| [re/client-critical-formulas.md](re/client-critical-formulas.md) | critical probability/luck/unexciting transforms, overrides, multipliers and math-helper contracts |
| [re/client-critical-inputs.md](re/client-critical-inputs.md) | complete MH/OH/spell chance input algebra, overrides, weapon classification and arithmetic order |
| [re/client-penetration-inputs.md](re/client-penetration-inputs.md) | twenty-one registered penetration equations, typed/hand composition, highest-uncapped ties and event-specific input boundaries |
| [re/client-hit-defender-modifiers.md](re/client-hit-defender-modifiers.md) | shared/typed hit-taken factors, ignore-resistance predicates and defender enemy-critical inputs |
| [re/client-defence-transfer-formulas.md](re/client-defence-transfer-formulas.md) | seventeen taken-as coefficients, five armour applicability outputs, override order and dependency differences |
| [re/client-reduction-inputs.md](re/client-reduction-inputs.md) | complete physical/elemental reduction inputs, conditional terms, small ceilings, Strength of Blood and dependency-vector differences |
| [re/client-defender-context-presence.md](re/client-defender-context-presence.md) | 37-input defender-conditional presence predicate, short circuits and unresolved numerical use |
| [re/client-ailment-multiplier-inputs.md](re/client-ailment-multiplier-inputs.md) | virtual faster inputs, combined damaging-ailment multipliers and qualification branches |
| [re/client-dot-multiplier-inputs.md](re/client-dot-multiplier-inputs.md) | generic, typed, ailment and non-ailment DoT multiplier inputs, exact qualifications and integer order |
| [re/client-ignite-output-family.md](re/client-ignite-output-family.md) | all 56 named ignite rate outputs, category/tincture qualification, conversion history and multiplier builders |
| [re/client-poison-output-family.md](re/client-poison-output-family.md) | all 72 named poison rates, hand elemental gates, integer-sum totals and chaos-only always-output qualification |
| [re/client-fire-self-rate.md](re/client-fire-self-rate.md) | complete incoming fire self-rate equation, signed64 percentages, state gates, difficulty selection and overflow |
| [re/client-physical-chaos-self-rates.md](re/client-physical-chaos-self-rates.md) | physical/chaos incoming self-rate equations, bleeding and ignite transfer, conditional inputs and distinct saturation boundaries |
| [re/client-effect-source-catalogue.md](re/client-effect-source-catalogue.md) | selected RF/daemon/ground-effect inheritance, mode/component/script declarations and source-stat defaults |
| [re/client-ground-effect-boundary.md](re/client-ground-effect-boundary.md) | concrete ground-effect lifetime transport, kill/expiry, Buff clock guard, cancellation and removal boundary |
| [re/client-ground-effect-ownership.md](re/client-ground-effect-ownership.md) | typed ServerFill parent route, shared cancellation-flag producer, reference ownership and infinite-lifetime expiry |
| [re/client-hit-result-qualifiers.md](re/client-hit-result-qualifiers.md) | received-result control admission, mutable mask/duration, quantized direction and selected critical-feedback qualifier |
| [re/client-dot-instance-rules.md](re/client-dot-instance-rules.md) | versioned source sampling, target dynamics, duration/expiration rules and their native boundaries |
| [re/client-ailment-duration-inputs.md](re/client-ailment-duration-inputs.md) | complete ignite/bleed/poison additive and final duration modifiers, conditional gates, integer boundaries and ordered binary32 arithmetic; actual instance assignment remains open |
| [re/client-native-dot-pool-composition.md](re/client-native-dot-pool-composition.md) | original native record/getter continuation into Life/ES numerical loss; 24 pool steps with an explicitly supplied native provider binding, separate from source-instance publication |
| [re/client-dot-native-composition.md](re/client-dot-native-composition.md) | 105 DoT outputs through original dependency ranks, dirty queue and numerical callbacks; dynamic inputs, intermediate caps and resource routing under explicit eligibility/input fixtures |
| [re/client-ailment-skill-duration.md](re/client-ailment-skill-duration.md) | six bleeding/poison skill-duration equations, distinct capped/uncapped charge terms, staged arithmetic and ClientStrings literal collisions; instance assignment remains open |
| [re/client-hit-production.md](re/client-hit-production.md) | concrete Fireball/Melee/Sand Spitter contacts, dodge/accuracy/entropy and action-specific continuation boundaries |
| [re/client-ranged-action-selection.md](re/client-ranged-action-selection.md) | default Bow/Wand action selector, equipment-to-provider updates and startup registration winners |
| [re/client-buff-clock-updates.md](re/client-buff-clock-updates.md) | insertion clocks, network replacement and ticking versus unresolved owner-stat-driven refresh |
| [re/client-buff-stats-subscriptions.md](re/client-buff-stats-subscriptions.md) | three concrete owner-Stats subscribers, backend/instance field identities and non-clock callback consequences |
| [re/client-buff-clock-input-provenance.md](re/client-buff-clock-input-provenance.md) | clock eligibility byte/word transport, zero additive defaults and partial/replacement assignment asymmetry |
| [re/client-buff-wire-source-composition.md](re/client-buff-wire-source-composition.md) | original body decoding through retained source updates, complete attribution selector and selected Life-callback prefix; explicit damage rate stays separate |
| [re/client-buff-default-backend.md](re/client-buff-default-backend.md) | strict empty-Id2 backend lookup, startup registration catalogue, null lifecycle guards and remaining numerical boundaries |
| [re/client-buff-lifecycle-scripts.md](re/client-buff-lifecycle-scripts.md) | actual observer payloads, lifecycle program configuration and invocation, selected Buff-presence predicates |
| [re/client-applydamage-stat-index.md](re/client-applydamage-stat-index.md) | actual IsApplyDamageFunctionStat reader, metadata map and guarded active function-index vectors; distinct from numerical damage execution |
| [re/client-buff-clock-producers.md](re/client-buff-clock-producers.md) | concrete Life-to-Buffs advancement guard and its separate speed/rate boundaries |
| [re/client-buff-network-clock-contract.md](re/client-buff-network-clock-contract.md) | existing Buff duration/speed receiver, omitted-speed default and selected backend callbacks |
| [re/client-action-extensions.md](re/client-action-extensions.md) | startup-selected melee attachment/controller output and ranged zero-time projectile dispatch |
| [re/client-projectile-extension-receivers.md](re/client-projectile-extension-receivers.md) | conditional DefaultArrow resource, Entity/action binding, concrete component updates and task-container boundary |
| [re/client-entity-task-boundaries.md](re/client-entity-task-boundaries.md) | local/serialized task factories, Entity ownership, selected countdowns and external-listener boundaries |
| [re/client-entity-task-listener.md](re/client-entity-task-listener.md) | selected action-start task's AnimationController subscription, slot-zero publication and concrete animation callback |
| [re/client-entity-task-ruin-listener.md](re/client-entity-task-ruin-listener.md) | Buff lifecycle/quantity publication to Ruin count effects/audio; provider state versus presentation |
| [re/client-defence-estimates.md](re/client-defence-estimates.md) | reference-damage physical-defence formulas, Ward substitution and category consumers |
| [re/client-dot-source-publication.md](re/client-dot-source-publication.md) | named RF template magnitude, table fixups and unresolved active instance publication |
| [re/client-dot-source-authority.md](re/client-dot-source-authority.md) | server-mode Aura APIs, separate Buff/Stats receivers and versioned ailment selection rules |
| [re/client-hit-feedback.md](re/client-hit-feedback.md) | immediate/queued result processing, action contexts, interruption, knockback and unresolved event consequences |
| [re/client-hit-resource-boundary.md](re/client-hit-resource-boundary.md) | component/pool receivers, numerical-hit searches, GEAL client/server eligibility and native resource listeners |
| [re/client-damage-timing.md](re/client-damage-timing.md) | predictive/lockstep message ordering, component ticks, fractional resource integration and correction boundaries |
| [re/client-damage-comparisons.md](re/client-damage-comparisons.md) | versioned GGG/wiki evidence and pinned PoB formulas, caps and modelling limits |
| [re/client-damage-pr373-comparison.md](re/client-damage-pr373-comparison.md) | pinned ranged-action research inputs; verified skill-stat packet and action identities versus reported capture hypotheses |
| [re/011-level-generation.md](re/011-level-generation.md) | the client's level generator: stages, RNG, topology pick, tile hash, generation traces; the server port in progress |
| [dev/debugging.md](dev/debugging.md) | log timestamps and archives, error stack traces, the packet history and dumps, `Game_TRACE`, client crashes, `bugreport.ps1` |
| [dev/editable-game-data.md](dev/editable-game-data.md) | what game data can become editable in SQLite, the client-compatibility boundary, the snapshot/cache design and the phased plan |
| [dev/modding.md](dev/modding.md) | mods in `Mods/Binaries/<id>/`: `mod.json`, writing one in `Mods/Source/`, the hooks, reloading, limits |
| [dev/live-testing.md](dev/live-testing.md) | the older live-testing notes (GettingStarted supersedes them) |
| [dev/ranged-monster-actions-port.md](dev/ranged-monster-actions-port.md) | standalone bow/wand and monster-action port, preserved upstream behavior and validation limits |
| [dev/passive-graph-data.md](dev/passive-graph-data.md) | pinned PSG format and read-only analysis exports; no client installation or gameplay integration |
| [dev/issue75-port.md](dev/issue75-port.md) | completed character-stat jobs ported from LocalExile, source revisions, integration decisions and validation limits |
| [dev/issue75-review.md](dev/issue75-review.md) | current-branch correctness review, repeatable 357-stat inventory, fixes and remaining completion work |
| [dev/issue75-stat-ledger.md](dev/issue75-stat-ledger.md) | exact 357-stat checklist with authored review states, evidence links and remaining scope |
| [dev-server.md](dev-server.md) | the JSON-lines dev server, `GameServer.Auth` and `GameServer.Patch` usage |
| [saves.md](saves.md) | how player progress is saved: the SQLite save database, transactions, what a failed save does |
| [game/fortification.md](game/fortification.md) | melee-hit protection and the explicit server model |
| [game/arcane-might.md](game/arcane-might.md) | spell increased/reduced transfer to attacks, source inventory and implementation boundaries |
| [game/maps.md](game/maps.md) | 3.29 maps and the Atlas: the 20 generic map bases, tiers, drops, Voidstones (rules to build to) |
| [game/gambler.md](game/gambler.md) | Gambler node checklist, incoming elemental ailments and read-only opt-in |
| [game/aristocrat.md](game/aristocrat.md) | Aristocrat node checklist: gem levels/quality, attribute requirements and conditional immunities |
| [game/ascendancies.md](game/ascendancies.md) | selection, points, saves and the Phrecia play gate |
| [game/antiquarian.md](game/antiquarian.md) | all nine node consumers, verified-graph admission, legal builds and native acceptance |
| [game/unique-jewels.md](game/unique-jewels.md) | the 185 unique jewels, their effects and what the passive-socket server supports |
| [game/radius-jewels.md](game/radius-jewels.md) | all 76 radius unique jewels: radius/ring model, mechanics, reusable timeless pieces and implementation plan; 42 jewels with server-side effects, 33 threshold items with client/server threshold checks, Lioneye's Fall's item source/client conversion, and the generated Impossible Escape keystone bypass |
| [game/radius-jewel-list.md](game/radius-jewel-list.md) | generated radius unique jewel table with radius, limit, mechanism, effect and support status |
| [game/timeless-jewels.md](game/timeless-jewels.md) | Legion timeless jewels: item data, seed/conqueror rules, node replacement algorithm, radius geometry and implementation plan |
| [game/timeless-jewel-list.md](game/timeless-jewel-list.md) | generated full list of timeless keystones, notables, small passives, 96 additions and 16 Militant Faith devotion mods with support status |
| [game/cluster-jewel-list.md](game/cluster-jewel-list.md) | generated full list of cluster notables, small-passive themes and keystones with support status |
| [game/tattoos.md](game/tattoos.md) | all 100 tattoos: mechanics, target/limit rules, the C->S 0x77 / 0x33 fifth-list protocol, acquisition, interactions and the full per-item status table |
| [re/ascendancy-client-3.29.3b.md](re/ascendancy-client-3.29.3b.md) | native identity and point evidence |
| [game/league-modes.md](game/league-modes.md) | Hardcore deaths, SSF isolation, Ruthless native rules and editable balance |

## Working notes (history)
- `re/007`–`010`: the notes these documents were consolidated from (login TCP, handshake, message tables, live instance
  notes). `re/009` still has the most complete login message tables.
- `re/003`–`004`: early notes with known errors (for example "login is HTTPS"). Verify before use.

[Recovery timing](re/recovery-timing.md): read-only client pool tracing, native prediction inputs and ES verification limits.

[Native client control](dev/client-control.md): CLI and persistent JSON RPC for local launch, login, character selection, commands and state reads through the 3.29.3b client.
