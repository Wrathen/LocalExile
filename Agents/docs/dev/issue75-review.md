# Character-stat correctness review and completion work

The target is all 357 virtual stats consumed by the 387 character-panel rows in issue #75.
The rows also reference 134 nonvirtual stats, for 491 distinct dependencies including visibility gates.
A row is not complete merely because a value is emitted, a source name exists, or a test passes.
Each needs the applicable source composition, formula, units, conditions, lifecycle and native presentation checked.
Where a value represents a gameplay mechanic, its authoritative consumer must agree with the display.

## Starting revisions and evidence

PR #318 and local `issue-75-character-stats` both started at `a7f93e566082a6dccecd349658374a2334e50ab6`.
Fetched upstream `main` was `939b44fec76ba46df762fcd9cdf616fa3d2f8739`, already in the branch's history.
The initial worktree was clean.
Only local changes and commits are authorized; do not push or publish the review or its changes.

The fresh baseline solution build passed with zero warnings/errors.
Core passed 555, Data 18 and Gateway 4,066 tests, with no reported skips.
The imported historical native evidence retains the limitations in [the port record](issue75-port.md).
The target checkout has extracted tables but no `Client/` installation at this review checkpoint.
No native run of these new changes has been performed.

## Repeatable inventory

Run from the repository root in PowerShell:

```powershell
python -X utf8 scripts/dev/audit-character-panel.py
```

The local report is `artifacts/issue75-review/panel-inventory.json`.
It records the Git revision, working-tree status, table hashes, all 491 dependencies and each panel row using them.
Filter `stats` by `virtual: true` for the exact 357-stat target.
The initial table inventory reproduces 387 rows, 491 dependencies and 357 virtual stats.
Every tracked Markdown file is inventoried with its content hash, headings and panel-related references.
The initial scan covers 81 Markdown files; the new review document adds another on subsequent runs.
This scan supports document navigation; it is not a claim that every statement has been verified against code.

Exact C# literal matches are navigation candidates only.
Interpolated names and numeric keys must be traced manually; absence of a literal does not establish missing behavior.
The [authored stat ledger](issue75-stat-ledger.md) lists each of the 357 targets exactly once. Its current
82 `partial` and 275 `pending` entries are audit dispositions, not implemented-stat counts. No entry is certified
complete on the current branch. Partial records link the reviewed corrections and their unresolved scope;
pending records do not imply absence of implementation or historical evidence.
The inventory copies these authored states and their links without inferring completion from literal matches.
It rejects missing, duplicate or unexpected ledger names. The 134 supporting dependencies stay `unreviewed`
in this report. The concrete findings below and protocol/status documents provide the detailed evidence.
Generated reports contain extracted metadata and remain ignored; do not commit the tables or client files.

Ledger validation reproduced the exact 357-name set and accepted both LF and CRLF Markdown. Deliberate missing,
duplicate and unexpected names were rejected; local fixtures remain under `artifacts/issue75-review/ledger-checks/`.
Decision: keep explicit partial/pending states and per-stat links rather than treating source-name matches or
historical certificates as completion. The ledger defines the evidence required to change a row to complete.

## Confirmed defects addressed

| Finding | Trigger and previous behavior | Change and evidence | Remaining scope |
|---|---|---|---|
| Selected spell penetration omitted supports | Actor penetration 10 plus real level-one Fire/Cold/Lightning Penetration support 20 displayed 10 while the hit consumer used 30 | Recompute three selected aliases from raw actor and own inputs; six before-fix regressions reproduced 30 expected / 10 actual; nine new Gateway cases cover composition, requirements, overrides, unlink/removal and isolation | Native formatting and conditional sources |
| Attack rows omitted attack-only penetration | General 10 + elemental 5 + typed attack 7 + elemental attack 3 displayed 15 instead of 25 | Add attack-only typed/elemental sources to both hand aliases, including the separate chaos source; four before-fix pure regressions reproduced omissions | Off-hand execution and remaining conditions |
| Selected attack rows omitted supports | Heavy Strike with actor 10 plus linked support 20 displayed 10 | Recompute all eight hand aliases while preserving raw local snapshots; six before-fix Gateway regressions reproduced omissions; twelve added pure/Gateway cases cover sources, removal, hand isolation and inactive gear | Native checks and broader selected aliases |
| Combat ignored non-base/general and weapon-local penetration | Elemental Penetration Support and local weapon penetration appeared in panel sums but had no hit effect | Consume raw general elemental/chaos inputs plus the attacking weapon's local snapshot; real support, parsed contact, inactive-weapon and shot snapshot checks | Native verification, off-hand attack selection and conditional sources |
| Elemental prohibition left positive display values | Combat suppressed penetration while the panel retained positive elemental sums | Emit explicit zero for all nine elemental aliases while preserving chaos; check player/own flags, real support composition and restoration | Native presentation; explicit developer overrides intentionally keep their final priority |
| Selected ailment coefficients omitted supports | Actor duration/effect 10 plus real Unbound Ailments 35 still displayed 10 | Recompute seven duration and five effect aliases from raw actor/own inputs; all 24 new lifecycle/override cases reproduced 45 expected/10 actual | Native presentation and full ailment mechanics remain open |

Implementation: `PanelStats.Selected`, `PanelStats.Sums`, and `Program`'s `PanelSourceSnapshot`.
Tests: `SelectedMiscPanelTests`, `SelectedPanelStatCompositionTests`, `HandPenetrationPanelTests` and existing
`PanelStatsTests`/`PenetrationTests`.
The penetration fixes improve eleven display stats and the ailment fix improves twelve coefficients.
They do not certify those stats against every game source or complete their associated gameplay systems.

## Validation of the penetration changes

Local implementation commits are `5052fa3` (selected spell aliases) and `fb80ff9` (attack-hand composition).
The solution build passed with zero warnings and errors.
The final required suites passed Core 555, Data 18 and Gateway 4,087: 4,660 tests with no reported skips.
This is 21 additional Gateway cases relative to the starting revision.
The first full Gateway run after these changes had one timeout in
`ConditionalCriticalGatewayTests.Released_ordinary_bow_reads_life_luck_and_prohibition_at_impact(false)`.
All 28 tests in that class then passed, followed by all 4,087 Gateway tests on the unchanged code.
The timeout remains recorded; this does not prove its cause or establish fault-free background execution.

Reproduce from the repository root:

```powershell
dotnet build GameServer.slnx --artifacts-path artifacts/issue75-review/build
dotnet test tests/GameServer.Core.Tests --no-build --artifacts-path artifacts/issue75-review/build
dotnet test tests/GameServer.Data.Tests --no-build --artifacts-path artifacts/issue75-review/build
dotnet test tests/GameServer.Gateway.Tests --no-build --artifacts-path artifacts/issue75-review/build
```

Local evidence is under `artifacts/issue75-review/results/`.
`selected-penetration-reproduced.trx` and `hand-penetration-reproduced.trx` preserve before-fix failures.
The final successes are `core-penetration-final.trx`, `data-penetration-final.trx` and
`gateway-penetration-recheck.trx`; `gateway-penetration-final.trx` preserves the timeout run.
No native completion claim follows from these tests.

The next penetration batch adds 19 Gateway cases (4,106 total): ten parsed contact cases, four real
Elemental Penetration support/lifecycle cases, two pure source tests, two prohibition projection cases and one
ordinary-shot snapshot case. The focused run passed 58; build passed without warnings/errors; Core 555 and Data 18 passed.
The first full Gateway run passed 4,105 and timed out in
`OrdinaryBowLifecycleTests.Paused_creation_waits_and_a_released_arrow_survives_a_later_player_action_until_one_impact`.
That test polled for a transient projectile and could miss its whole lifetime if its continuation was delayed.
It now holds the actual send gate between packets, witnessing creation before changing the action and releasing
flight. All 15 bow lifecycle/conditional-release checks passed after this test-only correction.
The subsequent full Gateway run passed all 4,106 tests with no reported skips. Combined with Core 555 and Data 18,
the required suites pass 4,679 tests. This validates the current automated behavior; native checks remain pending.
Receipts: `penetration-consumers-final-focused.trx`, `core-penetration-consumers.trx`,
`data-penetration-consumers.trx`, `gateway-penetration-consumers.trx` (failure), `bow-lifecycle-gated.trx`,
and `gateway-penetration-consumers-gated.trx` (full success).
The local commits are `0e91b09` (bow-test synchronization) and `329aa0a` (penetration consumers/prohibitions).

The ailment coefficient batch adds 24 Gateway cases. All 24 failed before the fix with 45 expected / 10 actual,
preserved in `selected-ailment-reproduced.trx`. Afterward, 149 focused checks passed in
`selected-ailment-focused.trx`. The solution build passed without warnings/errors, followed by Core 555,
Data 18 and Gateway 4,130: 4,703 tests with no reported skips. The three final receipts are
`core-selected-ailment.trx`, `data-selected-ailment.trx` and `gateway-selected-ailment.trx`.
The exact inventory confirms that these twelve names are virtual dependencies of the panel, including native
`virtual_frostburn_*` names for the Brittle duration/effect rows. Native checks remain pending.

## Impale review and corrections

The two before-fix damage regressions are preserved in `impale-roll-reproduced.trx`: a 10..20 physical roll at
its minimum incorrectly stored 2 instead of 1, and direct 10 crit200 plus existing reflected 10 incorrectly dealt 40
instead of 30. Gem projectile/area hits also omitted both application and consumption, so the selected support
chance did not describe their combat behavior. The legacy state had an unsupported five-stack cap, no expiry,
and erased instances on death.

The shared implementation now stores actual pre-mitigation physical damage with source critical/effect inputs,
captures five-hit/eight-second defaults plus modifiers, and resolves existing instances as one separately mitigated
reflected hit before adding a new one. Ordinary melee/bow and supported area/projectile attacks/spells participate.
General, attack, spell, critical-spell and qualified weapon-local chance keep their scope. Actual target avoidance
prevents new instances; all three selected aliases include raw linked support inputs without creating a general
chance from attack-only inputs. Killing hits retain state for expiry/removal. Current lifecycle and remaining
conditional mechanics are detailed in [the damage module](../protocol/20-damage.md#3a-impale-contacts-and-reflected-damage).

The final focused receipt `impale-focused-final.trx` passes 94 with no skips. It includes parsed real contacts,
independent reflected mitigation on zero-damage hits, target avoidance, rejected-contact state protection,
actual Impale Support removal/overrides, separate active hand modifiers and frozen ordinary-bow settings.
Earlier expanded fixture failures (missing test import, paused-clock setter, round-versus-floor expectation and
absent raw zero key) were corrected; their logs are preserved, not counted as production regression failures.
The solution build passes without warnings/errors. Core 555, Data 18 and Gateway 4,169 pass with no skips,
for 4,742 total. Receipts are `core-impale.trx`, `data-impale.trx` and `gateway-impale.trx`.

Decision: correct the damage/state engine now rather than merely displaying support chance or retaining known
incorrect legacy approximations. Keep existing source sampling and action admission, with mutation under the
accepted-contact lock; no new global timer. The alternative of rewriting all combat scheduling would expand
the change beyond this defect. The module records evidence, limits and these alternatives. Parent-checkout
Gateway 39900/Auth 45972/Patch 56752 were still running at the latest resource check; this task left them alone.
No current native claim is made, and the goal remains active.

## Critical weighting and duplicate DPS publication

Six before-fix regressions in `critical-average-reproduced.trx` confirm ordinary melee/bow and Cyclone,
Caustic Arrow, Fireball and Ice Nova still publish noncritical means at 100% crit. The correction weights average
damage/DPS with the actual critical policy (including conditional luck and prohibition) while preserving direct
noncritical ranges. It shares one bounded hundredths formatter and removes the selected path's redundant
rounded-rate calculation that was immediately overwritten using the exact rate. Current initial/refresh paths
use the same implementation. These are virtual keys 2044/679 in the 357-stat inventory.

The expanded focused run passes 144 tests without skips (`critical-average-expanded.trx`). Existing historical
support-average controls explicitly disable crit to preserve their old numeric evidence; separate tests cover
current crit weighting. The first focused run's eight failures were those old precritical expectations, retained
in `critical-average-focused.trx`. Solution build passes without warnings/errors. Core 555, Data 18 and Gateway
4,191 pass with no skips, for 4,764 total. Receipts are `core-critical-average.trx`, `data-critical-average.trx`
and `gateway-critical-average.trx`. The preceding impale implementation is local commit `e979a1c`.

Decision: use the existing combat critical policy and exact final action duration, keeping fractional packet
means until the integer boundary. The alternatives were retaining the known omission or switching DPS to the
rounded displayed rate. These choices, primary GGG evidence and limitations are also recorded in the
[skills module](../protocol/11-skills.md) and [damage module](../protocol/20-damage.md).
Accuracy and attack crit confirmation, conditional more crit, off-hand/multi-hit averaging, cooldowns, integer-roll
expectation and DoT/native validation remain open. Neither average/DPS row is marked complete yet.

## More/less critical chance correction

The real Controlled Destruction support supplied -80% less chance, but neither selected spell nor attack
calculations read it. Two real-support before-controls show expected 200 permyriad / actual 1000 in
`critical-support-reproduced.trx`. Two expression controls in `critical-finals-reproduced.trx` show expected
4800 / actual 10000 when BASE/INC and three independent factors compose.

The common BASE calculation now passes through `CriticalChanceFinal`, applying the four evidenced
unconditional native factors before one half-up round and cap. Exact rational arithmetic preserves signed
cancellation and prevents intermediate overflow. Nonpositive individual stages cannot revive chance through
another negative factor. Each raw input is registered for command-triggered pool refresh. The projection
explicitly excludes final aliases and unresolved target/owner conditions.

Twenty-three new tests cover four factors, exact rounding/extreme inputs, prohibition, real support qualification,
unlink/removal and unrelated skills, parsed Cyclone/Caustic Arrow/Fireball/Ice Nova critical outcomes, and ordinary
melee/bow with the actual unique -40% modifier, an explicit zero override and item removal. The expanded focused
receipt passes 72 (`critical-finals-expanded.trx`); solution build passes without warnings/errors. Core 555,
Data 18 and Gateway 4,214 pass without skips, for 4,787 total. Receipts are `core-critical-finals.trx`,
`data-critical-finals.trx` and `gateway-critical-finals.trx`.

Decision: multiply the evidenced source factors before capping, instead of applying LESS to an already capped
chance or assuming every similarly named conditional stat is active. The [skills module](../protocol/11-skills.md)
and [damage module](../protocol/20-damage.md) record this choice, alternatives and pinned source evidence.
The review also confirmed that off-hand range/timing publication currently supplies no off-hand critical chance
or multiplier. That needs the correct hand-specific source/execution work; it is not certified by the existing
main-hand/spell checks. Guaranteed critical effects and attack accuracy/confirmation remain open too.
The parent Gateway/Auth/Patch processes were rechecked alive and left alone; no native session was started.

## Ordinary attack evasion and critical confirmation

Review found independent random misses on ordinary melee/bow, no critical confirmation on either path,
and bare base evasion on bow contact. Bow also skipped target block. Ordinary contacts now share a
defender-owned entropy sequence, use current target evasion/Blind and mastery conditions, and independently
confirm critical hits before resistance, damage, impale storage and critical callbacks. Failed confirmation
keeps the normal hit; evaded arrows can continue to another target. Melee admission and all hit mutations
share one lock. Raw global and actual qualified weapon-local cannot-evade flags bypass both checks.

Twenty-five added cases cover six Core entropy/confirmation cases and nineteen Gateway cases: parsed
ordinary melee/bow accepted and downgraded criticals, actual `AlwaysHits` modifier and global flag,
miss/cancellation safety, later arrow collision, target modifiers/Blind, raw-source isolation and frozen
bow inputs with source removal. An existing bow case now verifies downgraded mitigation/impale storage.
The focused Gateway run passes 45 (`ordinary-accuracy-expanded.trx`); Core focused passes 6
(`evasion-entropy.trx`). Initial compile errors in the new test setup were corrected before execution.
The solution builds with no warnings/errors; Core 561, Data 18 and Gateway 4,233 pass without skips,
for 4,812 total. Final receipts are `core-ordinary-accuracy.trx`, `data-ordinary-accuracy.trx` and
`gateway-ordinary-accuracy.trx`. The prior critical-final correction is local commit `e00d398`.

Decision: preserve release-time bow owner inputs and use the existing game clock/inactivity policy, while
sharing one sequence per defender and keeping critical confirmation independent. The alternatives of
independent random misses, reusing entropy for confirmation, or re-reading bow sources at impact would
retain known errors or alter established flight semantics. Gem projectile accuracy requires continuation
through evaded collisions, including pierce/return; implement that separately instead of terminating
evaded gem arrows as zero-damage impacts. These decisions, primary GGG/pinned PoB evidence and remaining
source/panel/native work are also in [damage §2h](../protocol/20-damage.md#2h-ordinary-attack-evasion-and-critical-confirmation).
Parent Gateway 39900/Auth 45972/Patch 56752 remain active and were left alone.

## Accuracy sources and selected support inputs

The next review found raw accuracy commands did not rebuild/replace the pool inputs, and selected attack rows
inherited actor accuracy while ignoring Additional Accuracy's flat and quality contributions. `AccuracyRating`
now shares wide flat/increased source collection and an exact single-floor calculation. Pool builds retain the
signed original base; `SupportedAccuracy` combines skill/support sources before final rounding without repeating
the weapon's local flat rating. Three main-hand selected rating/chance aliases use that profile. Raw commands
rebuild pools, replace complete sources (including Precision) and restore them with `off`.

Twelve added tests cover actual 0/20-quality Additional Accuracy, link/removal, changed attributes, unaffected
actor/unrelated skills, real commands with Precision/zero/source restoration, fractional boundaries, signed
suppression and wide sums. The expanded focused run passes 30 (`selected-accuracy-expanded.trx`). The initial
diagnostic receipts are retained: their first numeric expectation used the stale zero-override baseline, and
the first qualification expectation incorrectly assumed level-one support failed at 14 Dexterity. Those are
corrected controls, not numeric before-fix proof. Source inspection independently identified the omissions.
The solution build has no warnings/errors; Core 561, Data 18 and Gateway 4,245 pass without skips,
for 4,824 total. Final receipts are `core-selected-accuracy.trx`, `data-selected-accuracy.trx` and
`gateway-selected-accuracy.trx`. The ordinary attack contact correction is local commit `67b58fb`.
The inventory was refreshed: 387 panel rows, 491 unique dependencies, 357 virtual stats and 82 tracked Markdown
files. Counts alone remain insufficient to mark a stat complete.

Decision: keep the exact actor base instead of adding support flat values to an already increased result or
reconstructing a lost base from a floored result. Apply the existing whole-source override policy, instead of
stacking a developer value on top of Precision/equipment. [Skills §4](../protocol/11-skills.md) and
[damage §2h](../protocol/20-damage.md#2h-ordinary-attack-evasion-and-critical-confirmation) record these decisions,
alternatives and pinned PoB/native-catalog evidence. Gem attack evasion/confirmation must next consume the
selected profile; remaining conditional/more accuracy, off-hand sources, complete always-hit/DPS publication
and native validation remain open. This work does not certify those entire rows.

## Area attack evasion and accepted-contact ordering

Area gem attacks previously skipped evasion/critical confirmation, and rolled block/damage/critical chance before
`HitMonster` checked action validity. They now consume `SupportedAccuracy` inside admitted contact, using the
same defender entropy, target conditions and independent critical confirmation as ordinary attacks. A miss
does not roll crit, consume impales, update hit timestamps or grant on-hit effects. Cannot-evade skips both
accuracy checks; spells remain outside attack accuracy. Critical flask gains follow accepted damage.

Fourteen new cases cover Cyclone and Heavy Strike, failed/successful confirmation, actual 20-quality Additional
Accuracy, independent targets, cannot-evade, spells, cancellation and explicit rejection results. An initial
11-failure diagnostic run asserted before parsed dispatch's background contacts finished; tests now await the
actual hit/entropy or executor completion. That receipt is retained as a synchronization correction, not proof
of a production regression. The synchronized 14-case run and expanded 267-case run pass. The solution builds
with zero warnings/errors; Core 561, Data 18 and Gateway 4,259 pass, no skips, for 4,838 total. Receipts are
`area-accuracy-expanded.trx`, `core-area-accuracy.trx`, `data-area-accuracy.trx` and `gateway-area-accuracy.trx`
under the ignored review artifacts. The preceding source/support correction is local commit `c2ae587`.

Decision: move rolls into the existing action/target admission lock rather than consuming state before discovering
cancellation. Preserve blocked/evaded rejection values rather than accepting a zero-damage hit with callbacks.
The module decisions and alternatives are recorded in [skills](../protocol/11-skills.md) and
[damage §2h](../protocol/20-damage.md#2h-ordinary-attack-evasion-and-critical-confirmation).
Gem projectile continuation, per-use critical-roll correlation, other conditional/more accuracy, off-hand
execution and full chance/always-hit/DPS publication remain open. No live session was launched; native validation
waits for shared resources. This correction does not establish completion of all 357 virtual stats.

## Gem projectile accuracy, continuation and volley exclusions

Gem projectile hits previously skipped evasion and critical confirmation. Forward, piercing and returning
contacts now use `SupportedAccuracy`; the same contact chance confirms critical results before return damage
scaling. An evaded target does not stop flight, spend pierce budget, trigger impact/impale/on-hit callbacks or
reserve the target in the volley-wide exclusion. A blocked contact still collides. All three sweeps check their
current lifetime/pause state under the send gate and target lock before consuming RNG or mutating state.
Piercing/returning critical hits now reach the critical flask callback, matching first-contact projectiles.

The review also found the forward exclusion dictionary was allocated inside the angle loop, giving each
projectile a private set despite the intended shared-volley rule. It is now allocated once outside that loop;
the return set stays separate. A three-projectile Burning Arrow control proves one direct hit. The
[official 2.0.0 notes](https://www.pathofexile.com/forum/view-thread/1304441) establish the general simultaneous
projectile restriction; this does not establish every modern skill exception. Kinetic Blast's prior special
handling is retained for separate review.

Twenty-four added cases cover actual Burning Arrow/Fireball, real 20-quality Additional Accuracy, independent
critical confirmation, cannot-evade, passing a missed target, forward/return budgets, blocked contacts, repeat
attempts after misses, volley sharing and queued pause/transfer/dead-target admission. The expanded focused run
passes 29 cases, including five existing matches; the earlier broad existing-control run passed 262. The solution
build has zero warnings/errors; Core 561, Data 18 and Gateway 4,283 pass without skips, 4,862 total. Receipts:
`projectile-accuracy-expanded.trx`, `core-projectile-accuracy.trx`, `data-projectile-accuracy.trx` and
`gateway-projectile-accuracy.trx`. The previous area correction is local commit `efd081b`.

Decision: preserve gem inputs sampled at contact and ordinary bow inputs frozen at release; uniformly freezing
gem inputs would change source lifecycles without snapshot evidence. Keep explicit miss continuation instead
of accepting zero-damage collisions, which would stop flight and trigger impacts. These choices and alternatives
are in [skills §10](../protocol/11-skills.md) and [damage §2h](../protocol/20-damage.md).
Remaining work includes per-use critical-roll correlation, conditional/more accuracy, off-hand execution,
complete chance/always-hit/DPS publication and native validation. Flight also retains pre-existing incomplete
rules for pierce-budget terminal positioning, return-from-target exclusion, return terrain/endpoint sampling,
secondary explosions and sequential/skill-specific exceptions. Do not treat this correction as completing them.
Parent-checkout Patch/Gateway/Auth processes remain active; this task launched or stopped no live processes.

## Accuracy-weighted attack averages and DPS

The ordinary and selected attack averages still assumed every attack landed after the combat corrections.
Six new before-fix cases reproduced inflated estimates; the spell control passed (`accuracy-damage-reproduced.trx`).
`CriticalStrikePolicy.DamageFactor` now uses `h * (1-c*h + c*h*multiplier/100)`, where `h` is normal-reference hit
chance and `c` is critical probability after signed luck. Landing and critical confirmation use separate factors;
spells use `h=1`. Raw cannot-evade sources bypass both factors. The ordinary main-hand normal/evasive chance
aliases also now respect global and qualified local cannot-evade, including removal.

Nineteen added cases cover the reproduced defects, luck/bounds, real Additional Accuracy link/removal, local/global
bypass, final diagnostic alias isolation, target-condition exclusion and unchanged spell/range outputs. The focused
run passes 55. The solution builds with zero warnings/errors; Core 561, Data 18 and Gateway 4,302 pass, no skips,
4,881 total. Receipts: `accuracy-damage-expanded.trx`, `core-accuracy-damage.trx`, `data-accuracy-damage.trx`
and `gateway-accuracy-damage.trx`. The preceding projectile correction is local commit `5a59764`.

Decision: apply accuracy independently to landing and critical confirmation, rather than multiplying the old
critical mean by accuracy only once or applying critical luck to the confirmation check. Preserve normal-reference
evasion, source profiles and the exact final formatter rather than guessing target conditions or rounding
intermediate percentages. Explicit final chance overrides remain diagnostic display replacements, not gameplay
inputs. [Skills §14](../protocol/11-skills.md) and [damage](../protocol/20-damage.md) record these choices and
alternatives, the official tooltip statement and pinned PoB's separate confirmation/landing calculations.
This is an expected-damage reference, not native 3.29.3b rounding certification. Integer-roll expectation,
dual-hand/multi-hit/secondary output, cooldown/DoT, other source conditions and native validation remain open.

## Shared critical rolls within a gem use

Area targets, volley projectiles, pierced/returning targets and existing impact explosions previously rerolled
critical chance independently. They now share one random value through `SkillCriticalRoll`, while each hit compares
its current chance/prohibition and independently confirms attack accuracy. Signed luck is captured when the gem's
cost/admission commits. The fallback direct area/projectile entry points capture at their own admission.

The [GGG mechanics answers](https://www.pathofexile.com/forum/view-thread/1676002/filter-account-type/staff/page/1)
distinguish the action's shared roll from chance calculated for each hit, and the Diamond Flask answer places
luck at the time of that roll. This corrects the earlier assumption that gem luck should be resampled at contact.
Native extra-roll getter evidence establishes the source formula, not its lifetime. Ordinary attack admission
timing remains the next correction; its existing contact/impact luck behavior is not certified by this change.

Twenty-two added cases cover shared area/volley/pierce/return/explosion rolls, signed luck, separate uses,
independent confirmation and changing chance/prohibition between targets. Four existing parsed-use controls now
verify admission-time luck across a Life change. Two reflection helpers were corrected after the full suite
caught their old `LaunchProjectile` argument count. The final focused run passes 28 cases, including those two
spell-impact controls (`skill-critical-final-focused.trx`). The previous accuracy/DPS correction is local
commit `acf2feb`. The final solution build has zero warnings/errors. Core 561, Data 18 and Gateway 4,324 pass,
with no skips, 4,903 total. Receipts are `core-skill-critical-final.trx`, `data-skill-critical-final.trx` and
`gateway-skill-critical-final.trx`; the preceding `gateway-skill-critical.trx` preserves the two reflection failures.

Decision: retain the random value rather than a critical boolean, because later chance/prohibition can differ
per target. Keep lazy allocation at the first eligible hit so cancellation, misses and prohibited hits consume
no critical randomness; eagerly drawing at admission was the alternative and lacks server RNG-sequence evidence.
This is an allocation policy, not a claim about the official random stream. The [skills module](../protocol/11-skills.md)
and [damage module](../protocol/20-damage.md) record these choices and alternatives. Special repeats, skill-specific
exceptions and native validation remain open. No live processes were started or stopped.

## Ordinary attack luck at swing admission

Ordinary melee and bow attacks still resampled luck at contact/impact after the gem correction. Eighteen new
before-fix cases reproduced both directions of Life, Focus and raw-luck changes for melee, object bows and
ground bows (`ordinary-critical-admission-reproduced.trx`). Each now retains a `SkillCriticalRoll` from admission,
including across a queued action confirmation and arrow release. Melee/object-bow capture after their walk-in
when the swing reserves pacing; ground bows capture within their existing input gate. Prohibition remains live
at contact, and attack accuracy confirmation remains independent.

Two further approach controls prove that a Life change during the walk-in affects the ensuing swing; capturing
at the initial click would be too early. Six existing signed-luck cases and two released-bow prohibition cases
now assert the corrected lifetime. A recovery-input control exposed a wall-time assumption: initial catalog/stat
loading could exhaust its one-second synthetic attack timer. It now restarts that fixture's clock before checking
the existing pacing reservation. This changes no production timer or timeout and preserves the failed receipt
`ordinary-critical-admission-focused.trx` (207 pass, one pacing assertion failure).
The expanded focused run passes 210. The solution builds with zero warnings/errors; Core 561, Data 18 and
Gateway 4,344 pass without skips, 4,923 total. Receipts: `ordinary-critical-admission-expanded.trx`,
`core-ordinary-critical-admission.trx`, `data-ordinary-critical-admission.trx` and
`gateway-ordinary-critical-admission.trx`.

Decision: use swing/input admission rather than the click that begins an approach, release or impact. Keep the
other ordinary-bow damage/chance/multiplier snapshots pending their own evidence instead of changing all offensive
lifetimes as part of luck. [Skills](../protocol/11-skills.md) and [damage](../protocol/20-damage.md) record the choices,
alternatives and primary GGG reference. The preceding shared gem-roll correction is local commit `d98597c`.
Other agents' server processes remain active; this task has launched or stopped none. Native validation remains open.

## Leech coefficient source composition

The panel omitted physical/chaos Energy Shield leech aliases, shared elemental ES sources and general any-damage
leech from scoped totals. Selected skills also omitted raw support contributions. Eighteen real-support controls
reproduced missing values or 210/160 expected versus 10 actual (`leech-projection-valid-reproduced.trx`). The earlier
receipt had six genuine Life/Mana failures and twelve fixture failures from confusing the Energy Leech effect
ID with its item metadata; those fixture failures are not counted as production evidence.

All 24 scoped coefficients and three speed aliases now compose raw actor/qualified support inputs. Physical
attack hand totals use general, typed physical, attack, combined Life/Mana and that hand's local sources once.
Mana Leech Support remains attack-only. Per-hundred-thousand actor/support values combine before unit conversion,
preserving the 6+4 -> 1 permyriad boundary. Final explicit replacements and zero-clearing remain intact.
Thirty-two added cases cover real support lifecycle/qualification/overrides, scopes, hand isolation and units;
the focused run passes 230 (`leech-projection-focused.trx`).
The subsequent source-description pass caught an important scope detail: the combined physical Life/Mana
stat's ID omits "attack", but its description explicitly restricts it to attacks. It is now confined to hand
totals; the pure controls assert that general physical/spell aliases exclude it. The first full run passed 4,955
tests before this source correction (`*-leech-projection.trx`); that pass alone did not establish correct scope.
After the correction, the solution builds with zero warnings/errors and the full suites pass again: Core 561,
Data 18, Gateway 4,376, no skips, 4,955 total. Final receipts are `core-leech-projection-final.trx`,
`data-leech-projection-final.trx` and `gateway-leech-projection-final.trx`.

Decision: derive overlapping display totals from original raw sources rather than feeding one alias into another
or treating them as independent coefficients in combat. This follows the pinned PoB source descriptions and
support stat sets, with native getter/formatting parity still open. Keep active leech recovery, conditional sources,
conversions and weapon-family conditions explicitly open instead of treating publication as implemented gameplay.
The [skills module](../protocol/11-skills.md) records these choices and alternatives; [damage §5b](../protocol/20-damage.md#5b-character-panel-leech-limits)
also records primary GGG evidence distinguishing instance duration, total recovery and maximum-rate caps for the
next gameplay work. The ledger moves these 27 rows to partial, not complete. No live session was started.

## Ordinary active leech recovery

The preceding coefficient correction exposed the missing authoritative consumer: valid leech supports could
publish percentages but restore no resource. `LeechSources` now builds per-type raw coefficients; accepted
ordinary melee/bow, gem projectile and area contacts grant Life/Mana/ES recovery from post-mitigation direct
damage. Reflected impales, DoT, evasion/block and rejected contact states stay outside that consumer. Monster
Life-leech immunity is now modelled instead of vacuous, with a Mana control.

`LeechRecoveryState` implements the primary GGG 1000-Life/700-damage example: a 150 nominal capped instance
has a 20/second base rate and lasts 7.5 seconds; 100% increased total recovery gives 40/second without shortening
it. Six such instances cap at 230/second, and generic recovery applies after that cap. The shared `LeechLimits`
resolver keeps fractional gameplay caps while preserving existing integer panel projection. `LeechSession`
keeps the three resources independent, settles modifiers before adopting new values, handles instant portions
inside the hit lock, and removes ordinary instances on full pools. The existing writer applies timed recovery;
pause, death/resurrection, successful transfer and disconnect have explicit ownership/cleanup. Instant flask
and block gains also trim full pools at their commits rather than waiting for the periodic writer.

Automated evidence: ten timing-model cases, three fractional/limit cases, six source/typed-damage/reflection
cases, five session cases and nineteen contact/resource-writer cases. The actual level-one Life/Mana/ES supports
recover their appropriate resource; six ordinary/gem contact routes create timed instances. The focused run
passes 100 cases (`leech-lifecycle.trx`). Initial focused runs passed 33 shared timing/limit cases, 70 existing
leech cases and 92 accepted-contact cases. Two test-fixture compilation errors (missing protocol namespace and
assignment to an init-only hostile flag) were fixed before execution; they were not gameplay regressions.
After immediate full-pool/death cleanup was added, the solution builds with zero warnings/errors and all required
suites pass: Core 561, Data 18, Gateway 4,419, no skips, 4,998 total. Receipts are `build-active-leech.log`,
`core-active-leech.trx`, `data-active-leech.trx` and `gateway-active-leech.trx`. The exact ledger audit still finds
357 virtual stats, 387 rows and 491 total dependencies across 83 tracked Markdown files.

Decisions and alternatives are also in [damage §5b](../protocol/20-damage.md#5b-character-panel-leech-limits):

- Preserve current whole-hit/critical/return rounding and allocate that direct total by post-mitigation type
  weights. Component-wise rounding would change established damage; pre-mitigation weighting overgrants typed
  leech. The chosen projection is explicit and remains unverified against official fractional rounding.
- Keep ordinary bow leech sources with its existing release snapshot, while owner recovery settings update at
  contact. Changing all bow snapshot policy at the same time would hide a separate unresolved mechanics decision.
- Clear on successful transfer into the existing full-pool destination; carrying old-clock instances is unverified.
  Failed transfers do not clear them. Use the existing recovery writer rather than a competing timer.
- Keep conversions, conditional/weapon-family sources, attack-only ES overleech, skill-owned recovery speed,
  legacy instant flags and leech-state consumers open. A tested ordinary engine is useful progress, not completion
  of the specialized mechanics or native presentation/prediction parity.

The ledger moves 15 specifically audited limit/prohibition rows to partial: 82 partial, 275 pending, zero complete.
No live process was launched or stopped. The persistent 357-stat goal remains active.

## Ghost Reaver conversion and keystone behavior

The extracted passive 24426 grants `keystone_ghost_reaver`, but the consumer previously ignored it. Five initial
regressions fail against the ordinary engine (`ghost-reaver-reproduced.trx`): conversion, independent instance
timing, ES-based limits/modifiers, plain conversion and source-removal behavior. They now pass. A focused combined
run passes 105 cases (`ghost-reaver-focused.trx`). The initial node-labelled filter matched only the five model
tests: the contact method contains `Ghost_Reaver`, so `GhostReaver` did not select it. The first full run caught
four fixture assertions indexing an intentionally omitted zero-valued alias after refund. Those now use the
dictionary's zero default; the contact filter explicitly selects the methods. The original receipts are retained.
The corrected filter passes all 15 new cases (`ghost-reaver-contact-final.trx`), including actual passive sources,
parsed refund across four hit paths, zero replacement, separate caps and source/destination prohibition controls.

The keystone now doubles the total ES recovery cap without changing per-instance capacity and prevents recharge.
The plain conversion source changes neither cap nor recharge. Converted Life-source grants and native ES grants
create separate ES instances sharing the one total cap. ES pool/caps, total recovery and generic recovery apply;
Life-specific rate modifiers do not replace those. The original instant-percentage assumption is corrected in
"Instant leech sources and Ghost Reaver correction" below. Existing instances retain their type when
the source is removed. A virtual conversion override of zero suppresses conversion independently of the other
keystone effects. Target ES-leech immunity gates both converted and native ES while leaving Mana available.

Evidence: this client's `ReminderTextKeystoneGhostReaver`, actual passive/Stats rows, pinned PoB 3.29 tree node
24426, and [Mark_GGG's separate-instance clarification](https://www.reddit.com/r/PathOfExileBuilds/comments/mrgqh3/comment/gxksr13/?context=3).
[The adjacent explanation](https://www.reddit.com/r/PathOfExileBuilds/comments/mrgqh3/comment/gw9c1w0/?context=3)
also confirms that each instance stores its own immutable rate/duration and modifiers act on the sum.

Decision: implement separate instances rather than translating PoB's combined average-duration output literally
into one runtime instance. The alternative produces slower recovery over a longer duration and contradicts GGG's
clarification. Preserve source-level Life prohibitions and destination ES prohibitions conservatively, matching
pinned PoB's explicit checks; ignoring the source prohibition could grant recovery from a blocked source. Exact
native conversion/prohibition interaction is still unverified and recorded in the module. Other conversions,
conditional sources, attack-only overleech and leech-condition consumers remain open.

This adds 15 model/keystone/contact controls without upgrading any row to complete. The 357-stat goal remains
active; no live processes were started/stopped and no changes were published.
Final validation: solution build has zero warnings/errors (`build-ghost-reaver-final.log`); Core 561 and Data 18
pass (`core-ghost-reaver.trx`, `data-ghost-reaver.trx`), and the corrected complete Gateway suite passes 4,434
(`gateway-ghost-reaver-final.trx`): 5,013 total, no skips. The exact 357-row audit remains 82 partial/275 pending.
The first full Gateway receipt retains four failed fixture assertions and 4,430 passing cases for comparison.

## Live leech conditions and Energy Leech damage

Four new regressions reproduce ignored Energy Leech conditional damage and forgeable raw leeching flags
(`leech-conditions-reproduced.trx`). Live flags now come from actual per-resource instances and the ES pool.
Expiry inspection leaves pending recovery for the resource writer, and instant-only grants do not manufacture
a persistent leech state. The existing player tick publishes condition changes without requiring another action.

The hit resolver applies general, per-resource, attack-only and physical-during-Life-leech increases in their
correct buckets, including physical conversion history. Energy Leech's current 15% full-ES MORE source stays
separate from its 1%-per-quality increased damage while ES leeching. The retained legacy conditional MORE source
is also gated. Pinned PoB `Data/Skills/sup_int.lua:2214`, stat descriptions and actual qualified support stats
provide the source evidence. These formulas affect the authoritative hit and the selected hit projection.

Twenty-three new cases pass in `leech-conditions-contacts.trx`: scoped damage/conversion and distinct MORE
operators, non-draining expiry reads, real quality/full-ES/overleech/unlink behavior, unforgeable condition flags,
committed projectile damage and an observed idle player tick. The earlier focused set passed 19 cases, including
the existing timing/session controls (`leech-conditions-focused.trx`).

The first full Gateway run passed 4,453 and failed four (`gateway-leech-conditions.trx`). One new committed-hit
expectation rounded the total instead of flooring each component endpoint as `DamageRange.Roll` does; correct the
expectation without changing production damage. Two piercing-flight fixtures raced target/transfer mutations
against fixed wall-clock delays; they now acquire the send gate after creation, pause the game clock, observe the
launch packet and mutate before resuming. A queued recent-hit fixture now seeds its unrelated evasion state after
its deliberate expiry wait. All 33 new/affected focused cases pass (`leech-conditions-corrected.trx`).
The next full run passed 4,456 and failed one (`gateway-leech-conditions-corrected.trx`), exposing an existing
two-flight test's assumption that entropy never expires under load.
It now permits the legitimate inactivity reseed with a deterministic hit value while retaining exact damage and
target-exclusion assertions. Core's explicit-time tests still enforce the 3.33-second boundary. These fixture
choices avoid widening timeouts, weakening contact assertions or accepting a lucky rerun.
The third full run passed 4,456 with one unrelated save-concurrency fixture timeout
(`gateway-leech-conditions-final.trx`). A synchronous loopback connect could enter the held save lock before the
test coordinator reached its release. Schedule publication independently, as the fixture already does for the
grant, so the coordinator always releases that lock. No save/gameplay behavior changes.

Decision: use instance existence rather than nonzero recovery or developer-supplied condition values. Recovery
suppression need not remove an instance, and immediate leech has no lasting instance. Read expiry without taking
recovery so UI/damage queries cannot consume healing. Add only evidenced hit-damage scopes rather than broad
name-based rules; speed, defence, ailments, DoT and other conditional consumers remain explicit work. The module
records these choices and alternatives. Native flag/prediction/lifecycle parity is still open; no live sessions ran.

Final validation: solution build has zero warnings/errors (`build-leech-conditions-verified.log`), Core 561 and
Data 18 pass (`core-leech-conditions.trx`, `data-leech-conditions.trx`), and the complete Gateway suite passes
4,457 (`gateway-leech-conditions-verified.trx`): 5,036 total, no skips. The prior failed receipts remain intact.
The exact inventory remains 357 virtual stats, 82 partial/275 pending; no completion status was inferred from tests.

## Attack-only ES overleech and Life retention overrides

Nine source/contact regressions reproduce six failures (`overleech-reproduced.trx`): ordinary melee/bow and gem
area/projectile attacks drop their ES instances at full ES, the Life retention virtual is missing its ordinary raw
source, and a zero virtual replacement fails to disable retention. Two spell controls and the positive replacement
already pass. The real item source is Stats row 12087 via Mods `ESLeechFromAttacksNotRemovedOnFullESUnique__1`;
pinned PoB stat description 6560 explicitly limits it to attack effects.

Each timed instance now retains its attack origin. At full ES the attack-only modifier removes nonattack instances
without restarting retained duration or splitting the shared recovery cap. Ghost Reaver preserves that origin for
both converted and native instances. Source changes apply to existing instances at full-pool checks; unconditional
ES overleech still covers both categories. The Life virtual projects its ordinary base source and respects zero.
Other virtual sources and Immortal Ambition remain separate incomplete work.

Decision: store the hit family on the instance rather than treating the item stat as unconditional or maintaining
separate attack/spell caps. A generic PoB display flag is insufficient evidence to discard the explicit attack scope.
The damage module records this decision and the alternatives. No native sessions were started or interrupted.

The local review skill was inspected for an independent pass. It requires a dedicated Bugbot subagent tool that
is not present in this session. Continue direct code review and repository tests rather than interrupting the
unavailable owner or claiming that an independent tool reviewed the patch. This limitation is not a gameplay blocker.

The first focused run passed 38 and failed four removal controls (`overleech-focused.trx`): the fixture refreshed
back to its actual zero-ES equipment, so it was no longer full when checking removal. Restore the controlled full
pool before that assertion and independently check that the raw item source disappeared. All 42 focused cases
then pass (`overleech-focused-corrected.trx`), including 17 new source/retention/override cases and existing recovery,
session and Ghost Reaver controls. No production pool behavior was changed to satisfy the fixture.

Final validation: clean solution build (`build-overleech.log`), Core 561, Data 18 and Gateway 4,474 pass with no
skips (`core-overleech.trx`, `data-overleech.trx`, `gateway-overleech.trx`): 5,053 total. The exact 357-row audit
still reports 82 partial/275 pending; the supporting retention fixes do not certify the unresolved stat families.

## Instant leech sources and Ghost Reaver correction

Eight initial controls reproduce six failures (`instant-conversion-reproduced.trx`): the actual Claw mastery has
no effect with one/two claws, with or without Ghost Reaver, and Life-only percentages disappear on conversion.
[GGG's 3.22.1d patch notes](https://www.pathofexile.com/forum/view-thread/3439509/filter-account-type/staff)
explicitly establish that Life-instant sources work with Ghost Reaver. Correct the earlier implementation and the
test that incorrectly expected a 100% Life-instant source to be ignored. Its independent ES pool/rate/recovery
assertion remains, with the unrelated instant source removed; new tests verify the corrected instant interaction.

The runtime now counts usable claws in active hands for the actual Claw mastery (effect 4516, 10% per claw).
Weapon Master (passive 52575) applies its 20% all-resource instant source once while any claw is wielded; Gore
Dancer (29630) applies its 30% Life source. Common and qualified Life percentages add before clamping. Mastery
removal, inactive weapon sets, switching back, requirement-disabled claws, the two real ascendancy sources,
conversion and native ES controls exercise accepted spell contacts, not just a helper projection. These fixtures
seed actual passive sources directly; they do not certify allocation legality or a native client session.

Decision: carry the Life instant fraction into the separate converted Life-source ES instance while retaining
the ordinary fraction on native ES grants. The alternative of broadening Life percentages to all ES grants has no
specific primary support; adding Life and ES final percentages would count the common percentage twice. Preserve
explicit final ES overrides for both ES categories, including zero. The exact mixed-source rule remains a local
interpretation pending native confirmation. The module records the same limitation and alternative. No new raw
Mana/ES instant stat names or one-value virtual projection were invented to imply unsupported composition.

The first focused run passes 32 cases (`instant-conversion-focused.trx`), including existing Ghost Reaver controls.
The first full Gateway run passes 4,500 and fails one older overleech condition assertion
(`gateway-instant-conversion.trx`). The retained-instance assertion already passes, but the short instance can
legitimately expire before the subsequent wall-clock condition query. Remove that redundant query from the
full-pool retention test; preserve accepted-hit retention, spell exclusion, raw source removal and clearing checks.
Explicit-time recovery/session tests and dedicated condition tests retain the expiry/flag contract. The alternative
of lengthening instances or changing production expiry to satisfy an unrelated assertion would obscure that contract.
No gameplay timing was changed. The next default-parallel run passes 4,500 and times out in the unrelated
death/resurrection case `Swing_epoch_is_captured_before_first_await_and_stale_work_cannot_land("dead")`
(`gateway-instant-conversion-verified.trx`). No lifecycle implementation or assertion was changed. Running the
same build with `-- xUnit.MaxParallelThreads=4` passes all 4,501 Gateway cases
(`gateway-instant-conversion-bounded.trx`). This bounds this task's worker load on the shared host rather than
raising timeouts, disabling tests or changing repository-wide runner defaults. The default-parallel timeout is
retained as an intermittent validation limitation, not claimed fixed by a passing rerun.

Final validation: clean solution build, zero warnings/errors (`build-instant-conversion-verified.log`), Core 561
and Data 18 (`core-instant-conversion.trx`, `data-instant-conversion.trx`), plus the bounded Gateway run above:
5,080 passing tests, no skips, including 27 new cases. The 357-row inventory remains 82 partial/275 pending;
these source corrections do not certify the remaining families or native recovery parity. No live server/client
was started or stopped. Follow-up doubling regressions use a separate artifact directory and are not part of
this commit's 4,501-case Gateway binary.

## Life leech recovery and maximum-rate doubling

Sixteen new cases reproduce eight failures (`leech-doubling-reproduced.trx`). The actual Highwayman modifier
`TotalRecoveryLifeLeechDoubledUnique__1` reaches the actor's raw stats but its Stats 12776 flag has no recovery
consumer. The explicit Life maximum-rate doubling flag (Stats 6967) is also ignored. The old test deliberately
expected the latter omission; it now retains only its still-deferred Wicked Ward virtual assertion.

The runtime now applies Highwayman's total-recovery doubling after increases and before the total cap and generic
recovery multiplier. It does not shorten instance duration, increase instant recovery, alter Mana/ES, or double
the maximum rate. The separate maximum-rate flag doubles the Life cap in the shared panel/combat resolver after
increases; final virtual replacements and prohibitions retain precedence. Both changes have explicit-time controls.
Actual mod contacts cover one/two equipped Gold Rings and removal. These fixtures verify the real modifier's
collection/consumer path without claiming a complete Highwayman generation or native-client test.

Decision: treat equivalent total-recovery doubling flags as one boolean operator, including multiple copies and
the earlier `life_leech_speed_is_doubled` spelling. The alternative would quadruple recovery when the same doubling
is supplied twice. Actual Stats descriptions and pinned PoB's bounded doubling operator support the selected
behavior (`16de4b82`, ModCache line 22120 and ModParser line 6855). The module records the same evidence and
alternative. Implement the explicit maximum-rate flag without assigning it to current Vaal Pact; inferring current
keystone acquisition from a legacy raw stat would silently add unevidenced behavior. Native parity remains open.

The focused run passes 49 cases (`leech-doubling-focused.trx`). It uses `artifacts/issue75-review/doubling-build`
to avoid changing assemblies used by the preceding instant-leech verification. The final solution build has zero
warnings/errors (`build-leech-doubling.log`). Core 561, Data 18 and Gateway 4,517 all pass, with no skips
(`core-leech-doubling.trx`, `data-leech-doubling.trx`, `gateway-leech-doubling.trx`): 5,096 total, including 16 new
cases. Gateway uses four workers as documented above; no test was excluded. The 357-row inventory remains
82 partial/275 pending. No live servers/clients were started or stopped, and nothing was pushed or published.

The owner requested pausing the goal after this implementation group is committed. This group is implemented,
documented and validated; pause after its local commit and clean-tree verification, without starting another
group. Remaining source systems and native validation are listed below for an explicit future resume.

## Upstream merge resolution

The owner requested completion of the already-started merge on `issue-75-character-stats` while the broader
357-stat goal remains **paused**. This task resolves that merge and commits locally; it does not resume the goal.
The incoming target was fetched and verified against `upstream/main` before resolution:

- Local parent: `0aaeb40d1540c40b24f3ec6729fe539535a07d58` (Life leech doubling).
- Incoming parent: `6a5697ff2731d2e3fafa83b27fa5ff1e71290149` (upstream PR #334 merge).
- Common ancestor: `939b44fec76ba46df762fcd9cdf616fa3d2f8739`.

The starting index had 23 conflicted paths. The original index, staged/working diffs, conflict files and all three
versions of each conflict were retained under ignored `artifacts/issue75-upstream-merge/`. Existing incoming
nonconflicting changes were preserved. No rebase, history rewrite, push or publication was performed.

The main conflict was semantic: upstream replaced the projectile executor while this branch extended the removed
executor. The integrated runtime keeps upstream geometry, continuation, secondary areas, release/admission,
output ordering and synchronous reward/boss-stage work, with this branch's combat consumers ported into it.
The following decisions preserve both sides rather than selecting an entire file wholesale:

- One projectile engine owns all bow/gem paths. Each accepted use shares one critical roll; release captures selected
  accuracy, raw damage/penetration, impale and leech sources. Targets retain entropy and independent critical
  confirmation. Direct and area damage retain their separate support scopes. The alternative old `FlyProjectile`
  task would duplicate execution and bypass upstream stages. Tests move to explicit simulation time with held
  output, rather than assuming presentation gates stop gameplay.
- Actual rolled physical damage, including its source critical multiplier, supplies impale. Reflected damage is
  separate from leech and does not crit again. Source-specific instant leech, Ghost Reaver, retention and doubling
  survive the port. The alternative upstream legacy impale calculation used maximum damage and merged reflection
  into the direct packet. Corpses/impales keep ordinary kill lifetime instead of the stale new-path ten-second cleanup.
- Leech reads committed projectile time; the resource writer catches up pending contacts before consuming recovery.
  The alternative wall-clock sampling prematurely ages earlier contacts during a delayed pump. Refill on projectile
  level-up trims ordinary leech just as other full-pool commits do. Non-projectile sweeps keep one damage snapshot
  while their critical chance/prohibition remains live per contact; freezing that chance would discard existing
  branch source-change behavior.
- Equipment keeps saved AR/EV/ES base rolls, local quality/conversion and native zero-ES rules. Body-to-Ward conversion
  acts on those local results before slot-specific operators. Fractional remaining Armour/Evasion survive into
  shield/body modifiers and Iron Reflexes. Reverting to upstream's minimum-only equipment model would lose saved
  rolls. Native Ward bases retain upstream's minimum-base implementation; Ward roll interpolation is not guessed.
- Power of Purpose includes signed per-level Mana and independent Strength-based Mana, honors inherent Intelligence
  prohibitions, and combines mana-derived Armour with scoped shield/body and IR contributions. Exact fractions retain
  decimal MORE groups and signed cancellation. The alternative old fixed denominator truncates new fractions; clamping
  intermediate contributions loses cancellation. The inactive path retains the branch's final half-up defence
  rounding and corrected Intelligence/10 ES contribution, rather than restoring older truncation and Intelligence/5.
- Upstream maximum-Mana physical attack damage enters the raw pre-conversion packet once. Its skill-specific transfer
  of spell increases uses wide multiplication; the alternative int product overflows for valid large signed inputs.
- The default per-Brutal-Charge stun coefficient does nothing at zero current charges. Its presence alone must not
  disable ordinary stun/Turtle recovery. Actual special charges retain the unsupported-threshold guard. Ignoring
  that active source or rejecting every ordinary character would both change behavior incorrectly.
- The single recovery writer retains leech, Fortification, Turtle Shell and Enduring Cry as separate earned amounts,
  and preserves Grace Period's damage suppression. Summing projected regeneration again would double recovery.

The module details and alternatives are in [projectiles](../game/projectile-framework.md#issue-75-integration),
[Power of Purpose](../game/power-of-purpose.md#rounding-and-numeric-boundaries),
[Antiquarian](../game/antiquarian.md#ward-and-adrenaline) and [damage/recovery](../protocol/20-damage.md#composition-with-the-upstream-projectile-runtime).
Status and roadmap entries now describe the new engine without retaining obsolete claims that fork/chain or KB
secondary explosions are unimplemented. Historical native observations remain historical, not merged-build evidence.

Validation receipts are under `artifacts/issue75-upstream-merge/results/`. The first full pass preserved 46 failures
in `gateway-first.trx`; these exposed the stun guard, changed execution timing, defence rounding/base-roll expectations
and test RNG ownership. A focused pass then had one remaining old-spread fixture failure (671 passed); its target
positions were corrected for the upstream spread. Eight new combined-behavior cases cover pool/slot composition,
leech timing, direct-versus-reflected damage, corpse retention, stun admission and wide spell-increase transfer.
The final solution build has **zero warnings/errors**. Final Core **676**, Data **18**, Gateway **4,811** all pass:
**5,505 tests, zero failures/skips**, with Gateway limited to four workers and no test filter/exclusions.
The final receipts are `core-final.trx`, `data-final.trx` and `gateway-final.trx`.
The inventory still validates the exact 357 virtual stats: **82 partial, 275 pending, zero certified complete**.

No native client or development servers were launched. The parent checkout's Patch/Gateway/Auth processes were
still running, so invoking `live.ps1` would risk stopping another agent's session. Decision: retain automated
validation and report native revalidation as pending; the alternative restart would interfere with shared work.
All saves, client installations, profiles and existing logs were preserved. The existing goal stays paused after
this local merge commit. Ward and the incoming features add implementation, but do not automatically certify their
ledger entries or complete the wider 357-stat objective.

## Main merge: league modes, Gambler and Vaal souls

The owner requested resolution of a second in-progress merge, from local `main` into `issue-75-character-stats`.
The incoming commit was also verified against a fresh fetch of `upstream/main`:

- Local parent: `8a22be05adbe509df92f0a02909c3cae1612f37f` (the preceding stats/projectile integration).
- Incoming parent: `0f49c07d85ba02ba004b535db5e7a36966dbe300` (upstream PR #341 merge).
- Common ancestor: `6a5697ff2731d2e3fafa83b27fa5ff1e71290149`.

There were 17 conflicted paths. The original index, binary diffs, conflicted files and all three versions were
saved under ignored `artifacts/issue75-main-merge-0f49c07d/` before editing. The merge retains both histories and
all incoming nonconflicting work. The pre-existing untracked `config/pob-server.json` is outside the merge commit.
The Markdown inventory checks all 92 unique tracked documents, including the new module references and the
existing Agents instructions/status/roadmap. No publishing, remote writes or history rewriting is part of this task.

Resolution decisions and alternatives:

- Keep one projectile/area/default-attack damage pipeline. Gambler's damage penalty applies once, followed by its
  luck/triple roll and resistance inversion. Retain entropy, the shared skill critical roll and independent accuracy
  confirmation. The typed post-mitigation packet still supplies leech; impale reflection is separate and cannot
  receive another critical/triple multiplier or leech event. Keeping both conflict blocks would roll attacks and
  apply impales twice; replacing the branch block would lose its actual-roll storage and source-specific recovery.
- Credit Vaal souls at the same lethal mutation for default attacks and the shared hit path, preserving the
  once-only corpse guard and current requirement snapshot. Keep corpse impales, Hardcore progress guards and
  Vaal transfer/restoration. The alternative old projectile corpse clear/removal would discard the branch lifecycle.
- Retain the connection-aware gem enumeration and ordinary/Ruthless catalogs throughout the branch's added
  weapon, defence, critical and charge consumers. Preserve saved local defence rolls, fractional Ward conversion,
  active-hand filtering, raw damage and selected-stat snapshots. Reverting to minimum-only defences or ordinary
  catalogs would lose either the stats fixes or the incoming mode rules.
- Share the equipment requirement resolver with Vaal checks, retaining wide attribute buckets and local requirement
  modifiers. Publish inactive-item cache state only from the normal character-stat entry point; restoring the old
  publication inside the shared resolver would introduce a side effect into current-equipment requirement reads.
- Keep one resource writer with leech, Energy Shield, Fortification, temporary recovery and incoming ailments.
  Shock scales RF burn; lethal ignite retains Hardcore persistence and epoch-checked death publication. Ruthless
  periodic persistence runs after projectile catch-up. Preserve instant-leech notifications and the combined
  stun/freeze interruption generation; replacing either with the old single-purpose version loses the other feature.
- Keep the native successful flask-use sound acknowledgement and full-pool refusal/resynchronization, while
  retaining the branch's captured level, independent hybrid recovery and Mageblood effect scaling. Restoring main's
  older recovery call would silently omit per-level effect sources.

The module decisions are recorded in [projectiles](../game/projectile-framework.md#issue-75-integration),
[league modes](../game/league-modes.md) and [damage/recovery](../protocol/20-damage.md#composition-with-the-upstream-projectile-runtime).
Existing native observations in incoming documentation remain tied to their original revisions.

Validation: the final solution build has zero warnings/errors. Core **727**, Data **18** and Gateway **5,174**
pass: **5,919 tests, no failures/skips**, with all suites unfiltered and Gateway limited to four workers.
Four added regression cases combine Gambler critical damage/inversion, direct-only leech, retained impales and
once-only Vaal credit with held output, plus saved maximum defence rolls/fractional Ward in ordinary and Ruthless.
The focused combined pass contains 227 tests. Final receipts are `results/core-final.trx`, `data-final.trx` and
`gateway-final.trx`; the clean build log is `build-fourth.log` under the artifact folder above.

The initial Gateway attempt could not initialize because this checkout's `data/` held only tracked `schema/`;
generated data now lives under the shared `../build/data/`. Decision: copy only 952 extracted table/module files
into this checkout temporarily, without saves, keys, runtime changes or production-path changes. The alternative
startup/extraction would involve the shared client and runtime unnecessarily. After validation, those independent
copies are retained under the ignored artifact folder's `validation-data/`, restoring the checkout's data layout.
Their source, paths and hashes are recorded in `temporary-data.json`. The first usable full run passed 5,047 and
failed 123: all failures were six test helpers selecting the old string-based gem method. They now use the actual
connection-based API; no assertions or test cases were removed. Failed receipts remain preserved.

No native client or development servers were launched or stopped; current-build native revalidation remains pending.
The local client/runtime configuration and existing client junction were preserved. The exact stat ledger remains
357 entries, **82 partial and 275 pending**, with zero certified complete. The broader goal remains paused after
this local merge commit.

## Autonomous decisions and shared validation resources

The owner authorized choosing safe reasonable options while unavailable, recording the decision and alternative,
and continuing until an irreversible action is required. Publishing remains prohibited.

- Penetration: supply raw weapon-local sources separately from player/skill inputs. Reusing final hand aliases
  would double-count inputs. Keep existing attack timing while off-hand execution is reviewed separately.
  [Damage module rationale](../protocol/20-damage.md) records this choice and the library's pinned source IDs.
- Prohibition display: send explicit zeros and preserve the existing final developer-override convention.
  Merely hiding rows would retain incorrect selected values; see [skills documentation](../protocol/11-skills.md).
- Bow-test timeout: synchronize on the real send gate instead of increasing timeout limits or accepting a lucky rerun.
  This preserves the test's assertion that a later action occurs before impact; it changes no production flight behavior.
- Ailment coefficients: extend the selected projection only for twelve established duration/effect sums, keeping
  Unbound Ailments' separate more-damage source out. The alternative of extending all ailment rows as sums would
  assume unresolved critical, eligibility and application rules; [the skills module](../protocol/11-skills.md)
  records those limits and the pinned reference evidence. Continue implementing those mechanics separately.
- Native testing: other agents may use the machine's client and fixed server ports. A Gateway from the parent
  checkout's `build/quest-validation/wake-skip-final` was running at this checkpoint. Leave it alone and continue
  independent code/tests while the live slot is occupied. Do not invoke `live.ps1` until the shared resources are
  free: it stops existing dotnet listeners and patched clients. The alternative of restarting those resources
  would interrupt another agent's validation. Recheck immediately before launching, and stop only this task's
  own processes when its test session ends. Preserve logs and existing client/profile data.
  A subsequent check also found an active client under a Claude `custom-assets-spike` worktree; this task
  launched no servers/client and did not stop any of those processes.

## Offence, Defence, Misc and Charges row batch

This batch (2026-10-29) publishes the remaining virtual rows that had no server derivation at all, as display rows
with automated composition checks. The owner's live run of 2026-10-29 (merged base `7fd944be`, a stack from this
checkout on alternative ports) passed the test plan over them: the Off Hand block and averaged per-use/DPS/APS with two
weapons and its clearing, the ailment rows per hand/spell/secondary, the spell effective-critical rows, the secondary
blocks of Bear Trap, Herald of Ice and Vaal Detonate Dead, and the Defence tab's estimate, projectile, hits, evade,
mana-recharge and stun-flag rows under `/set_stat`.

- Defence estimates (`PanelStats.Defence`): estimated physical reduction (plain, against projectiles with
  `physical_damage_reduction_rating_against_projectiles` and its `armour_differs_against_projectiles` flag, against hits
  with `armour_differs_against_hits`), `chance_to_evade_%`, `ward_delay`, `mana_recharge_rate_per_minute` and
  `omniscience_for_attribute_requirements`, against DefaultMonsterStats damage/accuracy at the character's level.
  Formulas: `DamageModel.ArmourReduction` + `ChargeEffects.Reduction` capped 90; `DamageModel.EvadeChance` +
  `chance_to_evade_attacks_%` capped 95 (PoB EvadeChanceCap); without a DefaultMonsterStats row for the level the
  estimate and evade rows are left out rather than estimated against a 0-damage, 0-accuracy attacker; `WardState.Delay` in milliseconds;
  `EnergyShield.RechargePerMinute` on maximum mana. The projectile armour scales the pools' final armour by
  (100 + increased + projectile increase) / (100 + increased); shield-only and more operators are not re-applied.
  `chance_to_dodge_spells_%` and `life_recharge_rate_per_minute` have no 3.29 source and stay blank.
- Sums: `cannot_be_stunned_by_spells/attacks` flags, `brands_per_target_limit` (selected too),
  `virtual_maximum_spirit_charges`, `virtual_physical_damage_taken_per_minute_per_siphoning_charge`. Abyss-jewel and
  Elder/Shaper item counting is not modelled, so their per-item sources contribute nothing.
- Spell effective critical chance (`CriticalStrikePolicy.EffectiveChance` / `DoubleCriticalChance`): the luck policy
  then bifurcation (`spell_critical_strikes_bifurcate`, PoB BifurcateCrit: 1 − (1 − c)², c² for both rolls), zero
  under prohibition; for the pools' spell chance and each selected spell. `skill_show_average_damage_instead_of_dps`
  mirrors the selected skill's `base_` flag.
- Off hand (`Program.OffHandPanel.cs`): `skill_uses_off_hand` and the hand's accuracy, chance to hit, attack speed,
  critical chance/multiplier and twelve damage ranges for the default attack and attack gems, from the shared buckets
  with the off-hand item's local modifiers (and the gem's own accuracy/critical increases and supported hit). Per use,
  DPS and attacks per second average the two hands' own values (`DualWieldPanel`, PoB's AVERAGE combination), each
  hand weighted by its own critical chance, multiplier and chance to hit (the Off Hand rows); the off hand's swing
  time is its own duration at the off-hand speed, with the gem's supports. `GemSkills` reads a gem's hit inputs once
  (`SupportedHitInputsFor`) for its hit, its ailment rows and the off hand's.
- Secondary damage: a spell with `display_skill_deals_secondary_damage` (Vaal Detonate Dead) fills `secondary_*` damage
  and critical rows from its supported hit; a flagged skill without spell base damage (Bear Trap, Herald of Ice's
  shatter) gets `Program.SecondaryPanel.cs`: its `secondary_*_base_*_damage` through conversion with the generic, area
  and projectile buckets only (`PlayerHitDamage.Resolve(secondary: true)`), the skill's base critical chance with the
  generic increases, the generic multiplier, the critical-weighted Damage per Use and the secondary ailment rows.
  These are display rows; the server does not execute those hits.
- Damaging-ailment rows (`AilmentPanelStats`, [20-damage.md §3](../protocol/20-damage.md#3-damage-over-time-and-ailments)):
  all 32 per-minute rows per hand / spell / secondary context from the same raw packet and conversion as the hit, with
  the ailment buckets, DoT multipliers (critical rows add `critical_ailment_dot_multiplier_+`) and Frenzy more.
  Conditional sources, stacking, support more multipliers and actual application remain open.

Tests: `PanelStatsTests` (Defence, flags, brand), `CriticalStrikePolicyTests`, `SpellEffectiveCriticalPanelTests`,
`OffHandPanelTests`, `AilmentPanelStatsTests`, `AilmentPanelGatewayTests` (real Rusted Sword / Glass Shank, Cyclone and
Fireball fixtures). Native display checks: the owner's live run above.

## Converted special charges

The Charges tab's Brutal, Affliction and Absorption rows (2026-10-29): the three Domain 1 unique mods
`GainBrutalChargesInsteadOfEnduranceUnique__1`, `GainAfflictionChargesInsteadOfFrenzyUnique__1` and
`GainAbsorptionChargesInsteadOfPowerUnique__1` (`gain_*_charges_instead_of_*_charges`) are the only 3.29 player
sources of these charges; Blitz and Challenger have none (their stats appear only in monster/mercenary skill stat sets),
Spirit and Siphoning maxima come only from abyss-jewel and Elder/Shaper item counts the server does not model, and
Blood Charges need Reap.

- `Charges.ConversionFlag` / `MinimumConversionFlag`: while the flag is held (observed with the limits in
  `ChargeSession.Observe`, so an unequipped belt takes effect at the next stats change), the held Endurance / Frenzy /
  Power state *is* the special type: the standard held state, limits, gains, expiry and area carry are unchanged, and
  pinned PoB `16de4b8` CalcPerform's rule (special = standard count, standard = 0) is applied at presentation.
- `ChargeEffects.Expand(converted)`: the standard per-charge and "with no / any … charges" stats see zero charges of a
  converted type (Character.ot's reductions, Frenzy speed, Power critical, the "per Frenzy, Power or Endurance Charge"
  total); the `Special` rules take the count: `chance_to_deal_triple_damage_%_per_brutal_charge` →
  `base_chance_to_deal_triple_damage_%` (the gambler's hit roll) and `stun_threshold_+%_per_brutal_charge` →
  `stun_threshold_+%` (`PlayerHitRecovery.Chance`; every stat `ChargeEffects.PerCharge` expands is excluded from the
  unsupported stun gate, `ChargeEffects.Expands`, which also lets `stun_recovery_+%_per_frenzy_charge` stun). The coefficients
  include `PanelStatDefaults` (3 / 10 / 8 / 8 / 12) through `PanelStatDefaults.Default`, since `BaseStats` adds those
  defaults to the published sources after `WithCharges`.
- `ChargeEffects.DamageMore(ailment: true)`: `ailment_damage_+%_final_per_affliction_charge` × `current_affliction_charges`
  as one more multiplier (PoB: Damage MORE with KeywordFlag.Ailment); read by `AilmentPanelStats` only, because no
  server path applies player ailment damage yet. The non-damaging ailment effect per Affliction Charge and the
  Absorption recoup of elemental damage taken as energy shield have no server consumer: their rows show the
  coefficients and the counts.
- `ChargeEffects.Display(converted, minimumConverted)`: `current_brutal/affliction/absorption_charges` (the rows and
  BuffDefinitions 1757–1759's `Current_StatsKey`) and `minimum_*` (under the minimum flag); the standard
  `current_*`, `actual_current_*` and `current_removable_*` rows show 0. The special rows are always sent, so a removed
  flag clears them. `virtual_maximum_*` stays the earlier mirror publication (`ChargePanelStats.MirroredMaximums`).
- HUD: `ChargeBuffs.Row(type, converted)` uses rows 1757 `brutal_charge`, 1758 `affliction_charge`, 1759
  `absorption_charge`; `ChargeSession.Presentation` swaps the buff when the flag changes without a count change.
  `/charges` shows "endurance (as brutal)" and accepts the special names.

Tests: `ChargeEffectsTests` (conversion stats exist and name the mods, Brutal effects instead of reductions, special
rows, Affliction ailment-only multiplier), `ChargeSessionTests` (special buff and the swap on flag removal),
`ChargeBuffsTests` (rows 1757–1759 against BuffDefinitions, the 0x1c1 bytes), `ConvertedChargesGatewayTests`
(composition through `CharacterPlayerStats` with the stun threshold accepted) and the adjusted
`Issue75UpstreamCompositionTests` stun case. Live (owner, 2026-10-29, base `7fd944be`): the conversion and
maximum-equals flags set with `/set_stat` on held Endurance Charges showed the Brutal row, the zeroed Endurance row, the
swapped HUD buff and the swap back on `off`; a real conversion unique has not been equipped (none is defined, #60).

## Blood Charges

Reap's Blood Charges (2026-10-29; `BloodCharges.cs`, `Program.BloodCharges.cs`), the Charges tab's Blood Charges rows
and the two per-charge coefficient rows gated by `show_blood_charge_info`.

- Sources: ActiveSkills `reap` ("If any survive, you gain a blood charge … Players can have 5 maximum blood charges"),
  the skill's `lose_blood_scythe_charge_on_kill`, GrantedEffectStatSets `Reap` constants
  `blood_scythe_damage_+%_final_per_charge` 15 and `blood_scythe_cost_+%_final_per_charge` 20, the gem base
  `Metadata/Items/Gems/SkillGemBloodreap`, BuffDefinitions 1822 `blood_scythe_charge` (`Current_StatsKey`
  `current_blood_scythe_charges`, `Maximum_StatsKey` `maximum_blood_scythe_charges`, `CancelOnDeath`), Character.ot's
  `maximum_blood_scythe_charges` 5 through pinned PoB `16de4b8` Misc.lua, PoB act_str.lua's Damage / LifeCost MORE ×
  BloodCharge.
- Mechanics: `AreaSkillHit` counts the hits that landed and the sweep's kills, then `BloodCharges.Settle` takes one per
  kill and gives one when any hit enemy survived, within 0 and the published maximum; `SendBloodCharges` swaps the
  buff and resends the stats and skills. The charges held multiply the sweep's hits (`BloodChargeDamageMore`, with the
  caller's `damageMore`), the skill's Offence hit (its raw packet, so the damage ranges, the ailment rows and the
  per-use/per-second rows agree) and its cost (`PayMana`). Rows: count, maximum (base 5 + raw sources, published
  once in `CharacterPlayerStatsSnapshot`) and the show flag while a gem socketed in an active item uses the charges
  (decided once per GrantedEffects row, `UsesBloodCharges`; the stat snapshot runs on every hit). Death clears them.
- Not modelled: a duration (none in the tables or PoB), carrying them across areas, Vaal Reap's temporary maximum,
  Reap's physical damage over time debuff, its life cost (the server pays mana). Live (owner, 2026-10-29, base
  `7fd944be`): a socketed Reap showed the Blood Charges rows and gained, displayed and lost charges with its buff.

Tests: `BloodChargesTests` (tables, multiplier, settle rule), `BloodChargesGatewayTests` (Reap on the contact
fixture: a surviving target gives a charge, the second hit is floor(first × 1.15), the cost at two charges is
round(base × 1.4), a killing use takes one and gives none, the rows are hidden without the gem).

## REA bundle cross-check (2026-10-29)

The 3.29.3b REA reference bundle (`F:\evry\evryPath\rea-analysis\poe-3.29.3b`, REA 6.8.0 over Ghidra 12.1.3, target
SHA-256 `78a0e227…48d5`, 116 selected functions, no runtime validation) was reviewed against the panel implementation.
It corroborates existing notes and changes nothing here:

- Stat getter `0x1424cfa40` (evidence 043, `ev_3693fc63…`): the sorted network override vector is searched first and a
  present value, zero included, returns at once; only an absent key reaches the upstream provider at +0xe0 (an
  indirect call, no recovered formula) or 0 without one. This is the basis of the explicit-zero publication the panel
  rows rely on (`PlayerStatsWithCleared`, the zeroed hand/ailment rows of spells and secondary skills, the always-sent
  special charge rows): a row the server wants blank must be sent as 0, not omitted.
- Actor scale `0x1427ecc30` (evidence 046): keys 0x2d0 / 0x7b6 / 0x2a1a / 0x7b7 (base, capped clamped ±50, permyriad
  ÷ 100, limit), signed half-rounding, floor −99, limit cap when non-zero — `ActorScale.Derive` line for line.
- Buff add reader `0x141fdf870` (evidence 121): flags bit1 u32 +0x68, bit7 f32 +0x6c, bit2 u16 stacks +0x70, bit3 two
  u16 (+0x74, +0x72), bit4 visual — the layout `InstanceEntities.AddBuff` writes for the charge and Blood Charge buffs.
- Pool recovery `0x141e6b320` / `0x141e6a600` (evidence 049/050): (rate ÷ 60) × elapsed with fractional carry, as
  `Agents/docs/re/recovery-timing.md` records; the per-minute recovery rows stay separate from the native predictor
  inputs.
- The setter `0x1424cfaf0`, the change notification `0x1424cfd00` and the skill record/list/update readers carry no
  panel formula. The bundle's own OBSERVATIONS state it measures no official-server behaviour.

## Integration base (2026-10-29)

Branch head `0cfcfdd7` merged with `main` `b2737133` (156 commits past the previous base `0f49c07d`: the test-suite
trim `bd81101c`, the channelled-skill generation, the PlayerSkills build scope, Dreadful Attitude, the Aristocrat ailment
conditions, map drops). Conflicts: `Program.Skills.cs` (the panel-source snapshot installed as main's `SkillBuildScope`;
`FinalPlayerStatsCore` delegates to the branch's `FinalPlayerStatsFrom`), `Program.Combat.cs` (`criticalRoll` and
`channelGeneration` carried together through `CastGemSkill`, `LaunchProjectile` and `AreaSkillHit`; the target sweep
keeps `excludeMonster` with main's channel checks), `13-items.md` (both Armour descriptions), and 34 trimmed test files
(main's trim kept; the branch's version of a kept test where the branch changed it; the branch's added tests for
branch-only behaviour kept; the contact and Rage fixtures pass the new `channelGeneration` argument). On the merged
tree: solution build clean; Core 389, Data 34, Gateway 2556 pass. One case,
`GameModeAilmentIntegrationTests.Chill_slows_attack_and_spell_timers_once_in_both_modes`, failed in two of three full
concurrent runs: it applied the chill at game time 0 while the connection's Stopwatch had run since its creation, so a
loaded suite's catalog loads outran the chill's two seconds; the test now applies the chill at the clock's current time
and stops the clock (as a pause does) for its reads.
Native validation on this base: the owner's live run of 2026-10-29 over the row batch, the converted charges and the
Blood Charges (each section above); the stack ran from this checkout on alternative ports beside another checkout's.

Second merge (2026-10-29): `main` `5d5b5150` (PR #361: socketed gem requirements disable their item again, client-predicted
projectiles, Tornado Shot's arrow) into `ff61e937`. Conflicts: `Program.Progression.cs` (main's `WithGems` requirement
raise and `virtual_*_for_requirements` comparison, with the branch's mod catalog passed to `ResolveEquipment` for the
requirement-waiving hidden modifier), `Program.Skills.cs` and `19-gems.md` (comments and text). Two rules changed
underneath the branch and the branch follows main on both: a socketed gem's unmet requirement makes its host
inactive again (the port had kept the host active; `PercentageAttributeGatewayTests` now expects the Plate Vest to go
inactive with its Fireball, the four selected-row support-qualification tests expect the host and both its gems to
leave when the support is disqualified (`SelectedMiscCharacter.AssertHostInactive`), the contact fixture sets
`virtual_*_for_requirements` so its supports qualify, and the 19-gems/13-items/status/roadmap/port-record text no
longer claims the port's rule), and a gem use's primary
projectiles get no server object (the client draws its own; the volley test expects no new object ids). Main's new
`CaptureGemProjectile` test passes the branch's per-use critical roll. On the merged tree: build clean; Core 389,
Data 34, Gateway 2561 pass.

Third merge (2026-10-30): `main` `c1b112e4` (PR #369 block: Arcane Might unified across attacks and the damage panels,
passive tree jewels, skill damage effectiveness decoded as an offset from base, the Eldritch Ichors and Embers, the
per-currency Craft of Exile validations, the Delve Crawler, debugging aids) into `cb8ff7ee`. Conflicts: the attack-hit
pipeline (`DamageStats.cs`, `PlayerHitDamage.cs`, `SupportedAttackDamage.cs`, `AttackSkillHit.cs`, `SkillCatalog.cs`,
`Program.Skills.cs`, `Program.GemSupports.cs`, `Program.Equipment.cs`): the branch keeps one increase bucket
(`PlayerHitDamage.Resolve` over `RawAdded`, with the area/projectile/melee context the panel rows need) and
`DamageStats.SpellAttackIncrease` now delegates to main's `ArcaneMight.Increase`, with the weapon's `WieldingWand` and
the retaliation skill type (120) threaded through `AttackIncrease`, `Resolve`, `SupportedHitInputs`,
`SupportedAttackDamage.Hit`, `AttackHitConverted` and `Unarmed.HitDamage`; main's `DamageStats.Added` (the same added
ranges with the increases applied per component) is not duplicated. `WeaponIn` keeps the branch's `ItemWeaponStats`
construction (variable defence bases, local sources) and sets main's `WieldingWand`/`Melee` from the weapon class.
`CharacterStats.cs` keeps both additions (`ResolveAttributes`, `Percent`); `CharacterSaves.cs` passes the saved
`DefenceBaseRoll` into main's migrated item; `Program.Items.cs`, the items status and roadmap take the union of the
Sacred Orb and Eldritch entries; `11-skills.md` keeps the selected-spell paragraph and main's effectiveness bullet (the
branch's absolute-value one is superseded); `ChargeSessionTests.cs` keeps the converted-charge test beside main's
restored tests; the chill test keeps the branch's stopped clock. `SupportedAttackDamage.Raw` adds a weapon's separate
`LocalAdded` components (main's `ItemWeapons` and its tests construct them apart from `LocalDamage`), so main's Arcane
Might unit tests pass through the branch's pipeline; main's two Arcane Might projectile tests pass the branch's per-use
critical roll. Main's effectiveness decode moves two of the branch's controls: Burning Arrow level 1 is 280% of base
(raw 18000; `DamageConversionHistoryTests` expects 28/72.8 before Frenzy scaling) and Fireball's added damage takes 300%
(the combined-support control of `SupportedSpellPanelTests` is 75979/101305 per use/second with 595–893 cold, from
51153/68205 and 397–595). Main's passive tree jewel tests need the `passive-graphs` v5 extract
(`scripts/dev/extract-data.ps1 -Only passive-graphs`); they fail on a checkout whose `data/passive-skill-graphs.json`
predates the geometry. On the merged tree: build clean; Core 394, Data 34, Gateway 3172 pass.

Fourth merge (2026-10-30): `main` `4602dd8f` (PR #375 block: the ranged monster actions port, which replaces the
ordinary bow executors with `Program.RangedAttacks.cs`/`Projectiles/Program.ProjectileRanged.cs` and renames
`OrdinaryBowShot` to `OrdinaryRangedShot`; tattoos; the world panel pins) into `58edf093`. Conflicts: the two deleted
bow files (the branch's per-use critical roll and the captured `ImpaleSettings`/`AccuracyProfile`/`CriticalRoll`/`Leech`
now travel through `TryBeginRangedAttack`, `ConfirmAndLaunchOrdinaryRanged`, `LaunchOrdinaryRanged` and
`ScheduleOrdinaryRanged` into the ordinary bow or wand `ProjectileOffense`), `OrdinaryRangedShot.cs` (upstream's name
and summary, the branch's capture fields), `Program.Combat.cs` and `Program.Messages.cs` (upstream's calls with the
roll), `Program.Monsters.cs` (upstream's monster action flow; the branch's `TrimLeechLocked` after the hit's pool update
in the new hit resolution), `PanelStats.cs` (the branch's `LifeRegenerationMultiplier` already applies main's
`life_regeneration_rate_+%`), the shot tests (renamed) and the combat status (upstream's local combat package entry
beside the branch's Impale entry). Main's ranged monster work reads two new extracts (`monster-contacts`, `projectile-clips`) and bumped two (`animation-contacts` v3, `area-transitions` v3); `scripts/dev/extract-data.ps1` regenerates them, and nine of its projectile tests fail on a checkout that has not. On the merged tree: build clean; Core 396, Data 34, Gateway 3317 pass.

Deferred and open at the close of the PR (each with its home): Spirit and Siphoning Charges (unique definitions #60,
abyss-jewel socketing and Shaper/Elder influence: the combat roadmap); Blitz and Challenger Charges (no 3.29 player
source); the Affliction non-damaging ailment effect and the Absorption recoup (no server consumer); executing the
secondary hits of Bear Trap and Herald of Ice, Reap's damage over time debuff and Vaal Reap's temporary maximum; actual
damaging-ailment application behind the ailment rows (Protocol 20 §3); the ledger's 169 `pending` audit states, which
are audit coverage, not missing rows.

## Transfer-safe character stat publication

The owner independently merged current main at `b001bfa3`; the earlier admission candidate is superseded.
The follow-up proposal extracts the shared stale-connection publication fix from the dependent Inquisitor work.
It leaves class gameplay out of #318 and keeps the owner branch under its existing writer.
See [character stat lifecycle](character-stat-lifecycle.md) for the contract, failing-before regressions,
exact base/code revisions and current native-validation scope.
Historical row-batch evidence is not a fresh acceptance run of this lifecycle change or every panel row.

## Remaining review and implementation

Continue from the exact inventory, tracing each stat through raw collection, derivation, publication and gameplay.
Prioritize the following confirmed partial systems, then reconcile every remaining inventory entry:

1. Penetration native validation and remaining target/conditional sources; the off-hand rows now publish, but
   off-hand attack execution does not. Unconditional raw composition and display prohibition have automated
   coverage, not native certification.
2. Selected ailment chances, leech and other aliases whose raw support contributions are not recomputed.
   The twelve duration/effect coefficients are corrected; prove other omissions using real sources before expanding
   the selected alias set. `SupportImpale` chance and ordinary/gem impale contact behavior are now corrected as
   described above. Conditional impale sources, application/consumption overrides and native validation remain.
3. Hit averages/DPS: ordinary/selected main-hand critical and reference accuracy/confirmation weighting now
   follows the combat policy; dual-wielded rows average both hands. Integer-roll expectation, remaining conditional
   critical sources, multi-hit/secondary averages, cooldown/downtime and DoT remain open.
4. Damaging-ailment application, duration, critical and stacking mechanics: the panel ranges are display rows
   (batch above); Protocol 20 section 3 documents helpers, but also the missing execution paths.
5. Current-branch validation of the incoming Ward implementation, remaining regeneration/recharge conversions,
   specialized leech, minion/brand/trap/mine mechanics and conditional source operators documented in the roadmap
   (the defence estimates, converted charges and Blood Charges passed the owner's live run above). Spirit and Siphoning
   Charges are deferred by the owner (2026-10-29): their sources are unique mods, which need unique definitions (#60),
   abyss-jewel socketing and Shaper/Elder influence in the item model first (the combat roadmap lists the gaps); Blitz
   and Challenger have no 3.29 player source.
6. Current-branch native validation of source changes, link/equipment changes, overrides, clearing, transitions and relog.

Do not turn historical client observations, hidden rows, sparse zero packets or source-name matches into completion.
Completion still requires every required stat to be implemented and supported by adequate evidence. The owner
requested a pause after the Life-leech doubling group is validated and committed locally; resume from these
remaining items when the owner resumes the goal. This pause does not certify completion of the 357-stat target.
