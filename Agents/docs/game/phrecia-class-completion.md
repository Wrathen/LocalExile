# Behemoth completion and integrated class context

The delivery unit is a playable ascendancy, not an isolated stat or its native display.
Behemoth has historical whole-class native acceptance for the supported gameplay and explicit server models below.
That native evidence belongs to implementation `9657e0ad` over main `fc5f4b17`, delivered in PR #328.
The current-main candidate is based on `c748f5fa` and has automated integration checks only.
No client, server, archive maintenance or native acceptance was run for this candidate.
Antiquarian, Gambler and Aristocrat now have their own integrated audits in main.
Archived integration-reference work is evidence to port and revalidate, not proof of implementation in main.
Work is tracked in issue #98 and the `codex/behemoth-completion` class branch.

## Acceptance contract

Each class needs all of its target-client notable and small-node effects consumed by supported gameplay.
Test legal connected builds within the eight-point budget, rather than allocating every notable simultaneously.
For every branch, verify allocation, actual combat/resource effects, paid refund, reallocation, death,
area lifecycle, save/reload and a Gateway restart with the real client.
Check interaction with the other allocated class nodes and existing ordinary characters.
Raw developer inputs remain useful consumer controls but cannot satisfy the class-selection/allocation checks.
An explicitly documented server model must be distinguished from official-server parity.
Unsupported global skill engines remain explicit refusals, never silently approximated as a successful Vaal use.

The default Phrecia gate from #274 remains. Behemoth alone has an explicit read-only opt-in:
set `Game_VERIFIED_BEHEMOTH=1` for the Gateway, then choose `/ascendancy phrecia Behemoth` on a Marauder.
Admission reads both PSG files from `Client/Content.ggpk` with the extraction decoder and checks exact SHA-256:
ordinary `82D82CD3CD2DAD81606AF0DA9D18FCD53B2E92CF006550947D65A4F7E102DC32`,
combined alternate `F2766B5A4618CDBE7E2993973B4D653F155F71BDFE0E5D063A425196CC93D638`.
A missing decoder/archive or different bytes refuse the choice/login before changing the save.
Other Phrecia classes retain their gate. Ordinary characters do not invoke the check or need Node at admission.
The compatible graph must already be installed through separately reviewed client maintenance.
This mode has no archive-writing code; startup and the read-only exporter retain their separate contracts.
Read-only graph extraction manifest v5 accepts either pinned original input or the exact maintained combined graph;
it does not require `-NoExtract` after compatible client maintenance.
The historical native acceptance session used an archive matching these hashes.
The current candidate neither inspects nor modifies the local client.
The global native Labyrinth/Altar acquisition task remains owned in #151.

## Behemoth: first class

The target graph has nine notables and nine Strength/Armour small nodes.
The small nodes grant 3% increased Strength and 10% increased Armour each.
Every notable path runs through Rampager, so its restrictions and Rampage must work in every accepted build.

| Node | Historical native result at `9657e0ad` |
|---|---|
| 49937 Rampager | Aura/warcry refused; spell damage zero; +10% size; all 13 Rampage tiers |
| 28815 Dire Rampart | Two modelled Fortification stacks grant 24% Life/min regeneration |
| 37308 Tireless Frenzy | Maximum Fortification 30; two stacks grant 2% attack speed |
| 48586 Sheer Fury | Rage Support reaches maximum 45 and triggers the maximum-gain proc |
| 38080 Mental Conditioning | Strength 36; converted Mana 104; supported attack pays 7 Mana |
| 13061 Fueled by Hate | Ground Slam pays 45?30?15?0 Rage; prevention, refusal and death reset |
| 10135 Final Blow | Unarmed 10%/200% crit; low-Life luck; armed and alternate-set isolation |
| 2103 Steel Willed | Armour 1206 grants 4% attack damage; physical loss 29/17; chance cap 30% |
| 29437 Incorruptible | Armour mitigates chaos 100 to 29; paid refund restores loss to 100 |

Three connected eight-point builds cover all nine notables and all nine small nodes:

| Build | Allocated nodes |
|---|---|
| A: Fortification | 7128, 49937, 4561, 28815, 59943, 37308, 42923, 38080 |
| B: Rage and critical strikes | 7128, 49937, 16671, 48586, 9053, 13061, 22167, 10135 |
| C: Armour and chaos | 7128, 49937, 42923, 38080, 24188, 2103, 34257, 29437 |

Production mutation regressions cover every build's paid 40-point full refund, reallocation and durable reload.
Native checks use actual selected-class nodes; developer inputs only control targets, requirements and pacing.

### Rampage execution

The completion branch now connects accepted owner melee contacts and lethal mutations to a pausable streak.
It publishes the native Rampage buff/count, derives increased damage/movement and clears transient state on removal,
death observation and area retirement.
Counter policy v1 grants one per accepted melee contact and one per owner kill, including both for a melee killing blow.
Repeated kill notifications cannot grant again; friendly targets and ranged contacts cannot grant melee stacks.
The target's 13 thresholds are loaded as execution plans, with their actual daemon skills and animated weapons.
The class branch executes all thirteen plans: owned daemon casts, circular projectiles, independent repeat/ripple
channels, corpse consumption, chained kill explosions, blindness and interrupting stun, and 37.5-second weapons.
Daggers attack one target, bows release real arrows and swords sweep their nearby targets.
Hostiles can attack the weapons; owned actors cannot be saved as ordinary area monsters or hit by their owner.
Daemon damage uses its own skill level and minion increases, independently of the owner's spell prohibition/Rage.
The LocalExile v1 geometry/timing is explicit: 12 projectiles in a circle, 120-cell range, 0.1-second ripples,
0.25-second separate repeats/weapon contact, 20-cell corpse explosions and a 40-cell fallback nova radius.
Actual table damage, repeat counts, radius changes, summon counts and duration remain authoritative.
Blind lasts the data-scaled four seconds and reduces attack accuracy/evasion by 20%; refresh packets send once.
Stun invalidates pending enemy attacks, respects stun immunity and uses the data-scaled 0.35-second duration.
Source removal/death/retirement cancels pending jobs and projectiles by epoch and removes owned actors.
Successful area transfer carries only the streak/time once; loading freezes the remaining duration, not old actors.
Movement-speed changes apply at the contact/deadline, retaining the native walker's current-segment pace.
Native build A executes every threshold, reaches 521 stacks and displays +52% damage/+26% movement.
The trace records 76 owned actors, 93 projectile hits and 131 weapon hits, including blind, stun and corpse effects.
An uncaptured area's zero dimensions previously destroyed projectiles immediately; the terrain trace now treats
only known upper bounds as walls, with regression and native projectile-contact checks.
The real Escape menu freezes 81 stacks for six wall-clock seconds; they expire after gameplay resumes.
Paid full refund removes the source, streak and owned runtime.
Automated regressions cover transfer/deadline state, speed reset before the next walk order, owned projectile damage,
blind publication, stun cancellation, stale effect epochs and exclusion of owned actors from instance saves.

One old staging assumption was rejected against the extracted target data:
Rampage animated-weapon MonsterTypes rows have `IsPlayerMinion = false`, while the one-use daemon flag is true.
The summon executor must assign the actual owner relation; rewriting game data or asserting that raw flag is unsafe.
The catalog retains those raw values and the 37.5-second weapon plans.

### Fueled by Hate and executable scope

The current candidate uses main's merged #317 soul collection and trimmed regressions, including `51846363`.
It adds only the Behemoth payment, prevention and Ground Slam executor to that foundation.
Current equipment/requirement snapshots retain main's raw stat cache and pool conversion.
Three legal eight-point Behemoth builds cover every notable and all nine small nodes; allocation, paid 40-point
full refund, reallocation and durable reload pass through the production mutation paths.
Vaal Ground Slam is an explicit LocalExile v1 radial attack: 40-cell base radius around the caster, supported area
scaling, own attack damage/effectiveness, and a linear close-target bonus from its actual stat over that radius.
It uses the real skill/animation and the existing contact, critical, mitigation, Fortification and Rampage pipelines.
Other Vaal executors (including clones, channel/vortex and repeated projectile engines) remain explicit refusals;
the conversion calculator/publication is shared so their future executors can use the same resource policy.
An unsupported name cannot opt into the generic area fallback, even with zero soul cost.

Fueled by Hate adds raw GEPL souls to support-scaled ordinary base Rage cost, then applies generic/Rage increases,
final cost and efficiency with the target's truncation and final half-away rounding.
Soul-cost reductions/support multipliers do not reduce the converted portion.
Admission checks every resource and requirement before movement/action/cooldown reservation; approach only checks.
The accepted action pays once before its native echo. A later cancellation does not refund an accepted payment.
Execution consumes that reservation once and cannot pay or hit again through a duplicate continuation.
The GEPL SoulGainPreventionDuration starts at admission on the pausable game clock and blocks the node's hit Rage gains.
Its expiry publishes through the resource ticker even when Rage itself is unchanged.
Death clears resources/reservation/prevention synchronously at zero Life; successful ticket transfer freezes remaining
prevention through loading and consumes the carry once. No resource is persisted in character saves.

Primary cost references are `0x1428a8660` and `0x1428c3380`; the explicit execution geometry/timing above is a server
model, not official-server parity. The native Labyrinth chooser and unsupported global skill engines stay separate.

### Historical whole-class acceptance evidence

The single real 3.29.3b client passes 41 bounded records across these local sessions under
`logs/behemoth-completion/`:

| Session | Records | Scope |
|---|---|---|
| `complete-20261028-142248` | 5 | Selected class, build A, all Rampage tiers and paid full refund |
| `complete-20261028-144752` | 23 | Builds B/C, Rage Support, Vaal payment, restrictions, crit, armour and refunds |
| `complete-20261028-144341` | 6 | Exact 20→20 streak transfer, destination expiry, durable nodes and clean relog |
| `complete-20261028-145304` | 7 | Keyboard cast, visible tree, real pause, Gateway restart and ordinary class |

Gateway/Core/Host embedded revisions, the listening DLL path and process working directory match `9657e0ad`.
Gateway SHA-256 is `1ae73e9d59ff897dc91961294a581483d37f041f642aa43592a0fadfbd06bf15`.
Physical Shift+Q casts the socketed Vaal Ground Slam and pays 15 Rage.
Its actual hits lose 226/200/174 Life at successive Rage balances; prevention blocks the linked Rage gain.
Underfunded admission produces no attack or hit; expiry permits ordinary Rage gain again.
Death during prevention clears Rage, Rampage and prevention before resurrection.
Refund restores Fireball damage; reallocation restores the node effects.
Relog and a Gateway process restart retain the selected class and eight nodes, with transient resources zero.
Switching to ordinary Juggernaut selects the Default graph with no alternate-graph stat.
Each session ends at login; the accepted normal Gateway is restored afterward without changing the client archive.
The tree and pause screenshots are retained beside the last session's metadata and results.

The historical implementation built without warnings; Core 646, Data 18 and Gateway 3672 tests passed.
The historical #317 integration produced the same tree as `9657e0ad`.
That result is not evidence for the current main or the independent #333/#339 candidates.

### Current-main composition

The pinned base is `fc202a95cb71ae78514d559dffa1862c21f8ee4b`, including main's #387 and #389 changes.
Tested source `136c7e6f` builds with zero warnings/errors and passes Core 404, Data 34 and Gateway 3512.
Suites run serially with ordinary public inputs; logs and TRX are under `logs/current-mainfc/`.
Historical native acceptance at `9657e0ad` does not certify this base or the current class candidate.

Fresh native source `ab998211` combines class `da479995` over `c748f5fa` with the separate PR388 controller fix.
Its loaded Gateway/Core/Auth/Patch/Host revisions, hashes, listener paths and working directories were pinned.
Five checks cover Phrecia selection, real nodes 7128/49937, enemy Life 18 to zero with two Rampage stacks,
paid five-point refund, and ordinary Berserker fallback with alternate selector zero.
Results and process receipts are in `pr328-native-current/logs/native/`.
The subsequent #389 bench update retains Behemoth consumers and regressions.
Its only change in a class-touched source file adds the unrelated bench-move dispatcher branch.
Fresh suites cover that base; the native result remains explicitly tied to the earlier pinned runtime.
This smoke does not repeat the historical thirteen-tier, Vaal, travel or restart matrix.
Two earlier setup failures remain preserved: explicit-password credential mode and an observer's reserved Frida field.
Neither failed attempt counts as class acceptance.
The candidate retains main's issue #75 damage and stat engines, Mental Conditioning, death notification epochs,
mode-aware skill catalogs,
Gambler damage rolls, Aristocrat stats, channel generations, AI cadence, path lifecycle and projectile corrections.
Rampage stuns invalidate the current monster action and use native Actor recovery instead of rig-only animation.
Each accepted stun returns an immutable token from the same locked mutation, before any await or deferred output.
Both direct hits and projectile callbacks carry that original token; presentation never adopts a newer stun or owner.
Queued stun output rechecks target identity, stun/action generations, owner epochs and boss-stage state at the gate.
It waits through pause and sends only the accepted recovery interval remaining at that gate, without extending it.
Held-gate regressions cover expiry followed by a new action, replacement, death, source removal and pause/resume.
Real direct-hit and projectile-output regressions also hold output before presentation entry, commit a second stun
or recycle ownership, and require exactly one current presentation or none for a retired owner.
Ground Slam retains main's native always-hit input, shared critical roll and live chance/prohibition checks.
Its Vaal payment captures low-Life critical luck before output can queue; all targets use that admitted policy.
Owner contacts retain scoped typed leech before reflected impales; owned Rampage effects grant no owner leech.
Regressions cover evaded Rampage contacts, Ground Slam luck across held output, and leech excluding reflection.
Main's conversion histories, raw panel sources, corpse impales and loading-recovery retirement remain authoritative.
Main's weapon-disabled hands, current source/cache guards and weapon restrictions remain authoritative.
Unsupported-weapon Vaal input is refused before Rage payment or action reservation, with a dispatcher regression.
Ordinary projectiles retain main's Burning Arrow ignite settings, damage pipeline and aim-height terrain trace.
Ignite deaths use the same owner-kill mutation hook as other owner damage; ranged hits grant no melee stack.
Combined regressions cover one kill credit, repeated ticks, an absent Rampage source and boss-stage interception.
Owned-weapon targeting uses the same AI action generations, chase budget, send gate and pause checks as ordinary AI.
Main's single combined graph manifest v5, matching GameDataFiles entry and Antiquarian admission remain unchanged.
The earlier duplicate #317 commits and deleted test tails are not part of the candidate diff or ancestry.
This is an independent Behemoth candidate directly over pinned main, without #333 or #339 class implementations.
Publication is serialized: the parent publishes #328 first, then adapts #333 and #339 to their merged predecessors.
Their shared payment, combat, lifecycle and documentation changes need checks against those exact new bases.

## Antiquarian: integrated follow-up

Use actual Phrecia graph membership, not the shared `AscendancyKey` alone.
That key also occurs on ordinary Chieftain nodes, which are not Antiquarian effects.
The original dependency checklist below is historical; all listed consumers are now integrated from `main`.
See [Antiquarian](antiquarian.md) for the completed implementation and native acceptance.

The separate Antiquarian audit owns its nine notables, small nodes and legal builds.
The completed Ward/Adrenaline consumers and acceptance are now in `main`.
Behemoth and Antiquarian retain their separate verified-graph opt-ins and supported model boundaries.
