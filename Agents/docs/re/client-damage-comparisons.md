# Client damage: official explanations and pinned PoB comparison

## Scope and authority

This comparison was checked on 2026-10-30 against the static 3.29.3b findings in
[client damage calculation](client-damage-calculation.md) and the historical LocalExile measurements in
[recovery timing](recovery-timing.md). It does not run the client or measure an official server.
The client identity and original-instruction anchors remain in the main report and
[evidence manifest](client-damage-evidence.json).

Three different authorities must remain separate:

- A dated GGG explanation describes the mechanic or architecture discussed at that date. It is primary explanatory
  evidence, but is not access to the current server binary or proof that every implementation detail is unchanged.
- A community wiki revision is a versioned synthesis. Follow its references and check what the primary text actually
  says; a citation does not make every phrase in the wiki a statement by GGG.
- Path of Building Community (PoB) is an offline calculation model. Its code establishes what that revision models,
  including its assumptions and approximation order. It does not establish execution on either the client or server.

All source summaries below are paraphrases. Full scraped pages and third-party programs are not part of this report.
No REA native session was opened by this comparison; the native anchors refer to the independently retained audit.

## Versioned external-source ledger

The official forum URLs and Reddit comment permalinks were opened and their actual text inspected, rather than
relying on search snippets. Forum dates below follow the displayed calendar date; no precise UTC conversion is claimed.

| ID | Source and version boundary | Verified content useful to this investigation |
|---|---|---|
| O1 | Chris, [Client-server Action Synchronisation](https://www.pathofexile.com/forum/view-thread/889669), posted 2014-04-15, edited 2015-05-20; historical predictive design | The server controls progress-affecting results. Its worked attack timeline has the server calculate a tentative result before contact, send feedback for client effects, and later commit damage on the server. This is a historical example of server-supplied early feedback, not proof of client numerical hit resolution. |
| O2 | Chris, [Update on Deterministic Lockstep](https://www.pathofexile.com/forum/view-thread/1262596), posted 2015-05-07, edited 2015-05-08; pre-2.0 beta announcement | Lockstep waits for server confirmation before displaying an action; predictive mode displays actions immediately and can diverge. The post explicitly acknowledges gameplay bugs despite the theoretical synchronization model. Neither mode description specifies a pool-update packet cadence or proves local hit subtraction. |
| O3 | Mark_GGG, [Mechanical Questions Thread, post 14699897](https://www.pathofexile.com/forum/view-thread/1676002/filter-account-type/staff/page/4#p14699897), 2017-08-10 | In a frame, recovery precedes over-time damage; sufficient recovery can still count as full life. The same post explains multiplicative more/less modifiers. It says every frame, **not 33 milliseconds**. |
| O4 | Mark_GGG, [Elemental Penetration, comment hfx5nq1](https://www.reddit.com/r/pathofexile/comments/q464j5/comment/hfx5nq1/), October 2021 discussion | Source-side DoT calculation produces a pre-mitigation DPS value. The target accumulates values by damage type, and mitigation/damage-taking operates on the typed total, without individual source identity at that stage. This explains why source-specific penetration cannot simply be applied there. |
| O5 | Mark_GGG, [Elemental Penetration, comment hfxndgt](https://www.reddit.com/r/pathofexile/comments/q464j5/comment/hfxndgt/), same October 2021 discussion | Defender processing includes resistance, damage taken as, damage-taken modifiers and resource routing. Raising fire resistance is supposed to reduce ongoing fire DoT. Basing ailments on already-mitigated hit damage would recreate double dipping. |
| O6 | GGG, answers by Mark, [Game Mechanics Q&A Answers](https://www.pathofexile.com/forum/view-thread/3284453), 2022 discussion | The Elemental Overload/ignite answer says all applied ignites remain until their own expiry; only the highest damage one applies at a time. A newly gained offensive bonus affects a later application, rather than upgrading the existing ignite. This is not a universal snapshot rule for every continuously maintained effect. |
| O7 | GGG, [Damage Over Time Changes — More Information Part 2](https://www.pathofexile.com/forum/view-thread/1897612), 2017-05-10, pre-3.0 changes | Damaging ailments cease to derive from final hit damage; source base damage/conversion and applicable ailment modifiers are distinct from hit-only modifiers and defender mitigation. The post promises ailment values in the character panel. Its proposed balance values must not be transplanted into 3.29.3b. |
| O8 | Mark_GGG, [Fire Penetration staff posts](https://www.pathofexile.com/forum/view-thread/55272/filter-account-type/staff), especially 2012-11-18, 2013-11-27 and 2014-01-20 | Penetration changes how a hit treats resistance at application; it is not a general fire-damage multiplier and does not apply to RF/other DoT. This old explanation is corroborated architecturally by O4/O5, rather than treated as an exact current formula. |
| O9 | Mark_GGG, [Mechanics thread, post 748465 and following staff replies](https://www.pathofexile.com/forum/view-thread/11707/filter-account-type/staff/page/10#p748465), December 2012 | Evasion uses per-entity entropy for the hit check. The separate critical-confirmation check does not use entropy. Other replies distinguish a shared skill critical roll from checks against individual targets and a character-panel average opponent. These are historical mechanics explanations, not current executable identification. |
| O10 | GGG, [3.16 Balance — Core Character Defences and Recovery](https://www.pathofexile.com/forum/view-thread/3185101), October 2021, pre-3.16 manifesto | The armour example changes 10,000 armour versus a 2,000 physical hit from about 33% to 50% mitigation. The stated rule is five times incoming damage in armour for 50% mitigation. This corroborates PoB's denominator factor 5, within the stated version boundary. |
| O11 | RhysGGG, [Explosive Arrow overflow reply](https://www.reddit.com/r/pathofexile/comments/eqptjq/comment/ff0ofyh/), 2020-01-20 as indexed by W1; thread concerns 3.9.2b | Confirms an overflow bug and a forthcoming cap instead of overflow, with a larger damage-pipeline refactor described as substantial work. The reply does **not** specify 35,791,394, a universal final cap stage, or the current server's complete arithmetic. |
| W1 | Community [Damage over time, revision 1702687](https://www.poewiki.net/index.php?title=Damage_over_time&oldid=1702687), revision timestamp 2026-09-23T22:27:15Z | Versioned community synthesis: typed DoT, independent effect durations, strongest applicable nonstacking effect, source snapshot versus changing defender mitigation, and approximately 35.8 million DPS. The exact cap value/stage is a community claim with mixed primary and creator references. |
| W2 | Community [Networking mode, revision 1605725](https://www.poewiki.net/index.php?title=Networking_mode&oldid=1605725), revision timestamp 2026-01-17T15:04:58Z | Useful terminology for predictive/lockstep and server authority. Prefer O1/O2 for primary claims; the wiki's latency recommendations do not establish damage or pool-update timing. |

W1/W2 were read from a retained wiki source snapshot with their revision IDs and timestamps. Live wiki fetches returned
HTTP 403 during this pass; the report therefore binds those summaries to the retained revisions, not an unversioned
current page. The referenced GGG statements were separately read online.
The Reddit pages render relative ages; O4/O5's month and O11's exact date use the versioned wiki's bibliographic context,
not a newly verified Reddit UTC timestamp.

### Timing is not one measurement

W1 says 33 ms in its frame-order paragraph and cites O3. Reading O3 establishes order, but does not establish that number.
PoB independently hard-codes `ServerTickTime = 0.033` and `ServerTickRate = 1 / 0.033`
([Data.lua lines 236–238](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/Data.lua#L236-L238)).
That is PoB model evidence for 33 ms. It is not a fresh official-server measurement and should not be silently rounded
into exactly 30 Hz: `1 / 0.033` is approximately 30.303 Hz.

The audited client instead integrates `rate / 60 * dt` at its Life component update; historical LocalExile observations
showed approximately 120 client Life updates per second in the foreground and approximately 10 in the background.
The local protocol's advertised frame length, authoritative local server recovery loop, client updates, packet arrival,
and HUD rendering are different clocks. Neither the native `0.033` ES-estimate constant nor PoB's model constants
establish the client's Life scheduler or the current official server's packet frequency.

## Pinned PoB identity and readable source checkpoints

Repository: [PathOfBuildingCommunity/PathOfBuilding](https://github.com/PathOfBuildingCommunity/PathOfBuilding).
Revision: [`16de4b82d57f1c0de6eb40f37143c32d4da36a02`](https://github.com/PathOfBuildingCommunity/PathOfBuilding/commit/16de4b82d57f1c0de6eb40f37143c32d4da36a02),
GitHub commit timestamp `2026-09-08T00:30:43Z`, message `Update Spec`.
The retained reference manifest identifies application 2.67.2 and a 3.29.x fallback; **exact 3.29.3b correspondence remains
unverified**. A matching league family is not proof of equal client data or formulas.

The seven files below were downloaded from the public raw URL at that exact SHA and compared byte-for-byte with
the retained snapshot on 2026-10-30. All seven matched. Hashes describe unmodified downloaded bytes, including line endings.

| Repository-relative file | SHA256 |
|---|---|
| `src/Modules/Data.lua` | `f748f7f5a885552d2f6a498614494f6bb9397fc0dad7171d6292a2c08c2356cb` |
| `src/Modules/CalcOffence.lua` | `fc7bd39774725ebe8726a0a23eddde7c8485c7cee61b724e47a7a6569bfa5604` |
| `src/Modules/CalcDefence.lua` | `9e82a0c71ebb4b6005c6fd850ea0057cf2e6668c3c8e480b8d5310149d3db331` |
| `src/Modules/CalcTools.lua` | `8f590b40b4ab9ec04bfde3e41cd2e4af55b2b6ed592b1ec1285430893ca5f9a6` |
| `src/Classes/ModStore.lua` | `59f8bd7e2760c94996cafae02b354229dcd8dd11e145fbd7f671b70e63bdb932` |
| `src/Modules/CalcSetup.lua` | `d51fcf3aa47f2903d7a2066f08a6dcd0b2a6d5df86553c6a50685bd2e81f7df1` |
| `src/Modules/CalcActiveSkill.lua` | `6df68d2287f49dd8e82dc24a2709c11467c55a3cb9366305a546d5257b97955e` |

### DoT units, mitigation and caps

PoB's [Data.lua line 261](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/Data.lua#L261)
defines `DotDpsCap = 35791394` and comments on signed-int maximum divided by 60. This is a DPS model constant.
The client instead stores per-minute integer rates and divides them by 60 during time integration.
The quotient `(2^31 - 1) / 60 = 35,791,394.1166…` explains the same magnitude, not identical rounding or cap placement.
There is no minute-reset damage budget in either of these source checkpoints.

PoB generic DoT processing has explicit stages:

1. [CalcOffence.lua lines 5772–5815](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcOffence.lua#L5772-L5815)
   constructs a DoT modifier configuration and removes inappropriate hit/attack/spell/projectile/area scopes unless
   the skill explicitly permits them. Spell modifiers applying to skill DoT do not imply spell-damage-taken applies.
2. [Lines 5811–5853](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcOffence.lua#L5811-L5853)
   obtains typed base rate, applies configured defender resistance or physical reduction and taken modifiers, then
   combines increased damage, more damage, DoT multiplier and aura effect. Physical DoT uses additional physical
   reduction, not the armour hit formula. Resistance penetration is absent here. `TotalDotInstance` is capped while
   typed totals are accumulated, **after** the effective defender multiplier in this path.
3. [Lines 5861–5913](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcOffence.lua#L5861-L5913)
   distinguish stackable rate × speed × duration, strongest burning/caustic ground, corrupted blood and attached-brand
   cases. This is a configured DPS/uptime model, not an effect-instance scheduler.
4. Ailments also have caps: [bleed 4593–4598](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcOffence.lua#L4593-L4598),
   [single/stacked poison 4866–4897](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcOffence.lua#L4866-L4897),
   and [ignite 5213–5231](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcOffence.lua#L5213-L5231).
   Despite its name, `singlePoisonDPSUncapped` already calls `min` at line 4866; reading variable names alone would
   misidentify that boundary.
5. [Lines 6250–6256](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcOffence.lua#L6250-L6256)
   sum the selected skill's supported DoT outputs into `TotalDotDPS`, cap again, then add that amount to combined DPS.
   This is not a simulation of every source on one actual defender, including other players, simultaneously.

The client fire taken-as callback `0x1426d40f0` caps before fire mitigation `0x1426da000`, which caps again before
selected resource aggregate caps. Those are observably different calculation boundaries from PoB's generic path above.
For example, abstractly `min(x,C) * 0.25` and `min(x*0.25,C)` differ when `x > C`.
This demonstrates why moving a cap is material; it is **not** a claim that a specific live source reaches that input,
that PoB predicts the wrong live result, or that the official server uses the audited client callback sequence.

### Hit chance, critical chance and mitigation

PoB's [CalcDefence.lua lines 58–76](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcDefence.lua#L58-L76)
give two concrete formula checkpoints, with `A` accuracy, `E` evasion, `R` armour and `D` physical hit damage:

```text
hitChancePercent = clamp(round(125 * A / (A + (E / 5)^0.9)), 5, 100)
                   // special case: A < 0 returns 5
armourReductionPercent = 100 * R / (R + 5 * D)
                         // special case: R = D = 0 returns 0
```

The first is an approximate probability, not the entropy state or RNG sequence in O9. PoB
[CalcOffence.lua lines 2230–2249](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcOffence.lua#L2230-L2249)
uses the configured enemy's evasion and calculates expected successful-hit percentage including block.
Spells and cannot-be-evaded branches use 100% accuracy chance. The client main-hand evaluator `0x1427854b0` instead
passes `character_sheet_reference_evasion` (10745) to `0x1424ff510`; its identified consumer is a reference estimate,
not an actual defender binding or demonstrated hit roll.

PoB [lines 3098–3146](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcOffence.lua#L3098-L3146)
calculate critical chance from base plus added chance, increased and more modifiers, round to permyriad precision,
apply the configured cap, and handle lucky/special-roll cases. Effective mode multiplies the resulting chance by
accuracy chance for critical confirmation. This expected-value calculation does not establish client RNG consumption,
the current entropy implementation, or pool reduction. The audited client callbacks for accuracy and critical chance
are evidence of stat calculation, with different unresolved consumer boundaries.

PoB [lines 3509–3524](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcOffence.lua#L3509-L3524)
apply armour reduction using the model's average hit for that pass, add other physical reduction and apply caps/ignore
effects. Because armour reduction depends on hit size, mitigating an average is not generally identical to averaging
every possible mitigated integer roll. For nonphysical hits,
[lines 3554–3596](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcOffence.lua#L3554-L3596)
include penetration, resistance inversion/ignore cases and damage-taken modifiers. DoT's corresponding effective path
does not read penetration. O8 and O4/O5 explain the mechanical reason for that distinction.

### Modifier and static-input boundary

PoB [CalcTools.lua lines 15–35](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcTools.lua#L15-L35)
expresses the common `base * (1 + sum(increased)/100) * more` construction. Modifier collection is scoped, not a sum
of all similarly worded stats: [ModStore.lua lines 165–187](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Classes/ModStore.lua#L165-L187)
passes flags, keywords, source and configuration to the stores. This is conceptually compatible with the client's
separate combined hit/attack/physical and final modifier inputs, but does not prove identical rounding or complete
modifier support. The client's combined values must not be expanded and then applied a second time.

PoB's inputs are an explicit build/configuration model:

- [CalcSetup.lua 490–526](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcSetup.lua#L490-L526)
  constructs player/enemy modifier databases from build data, selected tree/spec, configuration and enemy level.
  [681–682](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcSetup.lua#L681-L682)
  adds configuration modifiers; allocated nodes and selected items are read at
  [747–824](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcSetup.lua#L747-L824).
- [CalcTools.lua 192 onward](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcTools.lua#L192)
  builds gem stat values from quality and selected granted-effect level. [CalcActiveSkill.lua 583–594](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcActiveSkill.lua#L583-L594)
  reads level-specific critical chance, attack timing and cooldown.
- [CalcOffence.lua 494–506](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcOffence.lua#L494-L506)
  constructs configured enemy resistance and clamps it using model maxima and floor. `Data.lua` has -200 floor and
  90 maximum-cap constants. The client fire-DoT resistance callback has special flags and selected maximum inputs;
  the absence of a literal 90 clamp in that single callback is not a contradiction unless its upstream maximum is closed.

There was no attempt to declare all PoB input tables equal to 3.29.3b data, execute a build for numeric parity, or use
PoB configuration state as evidence that a network actor's client-side stat caches update at the right time.

## Agreement, difference and remaining evidence

| Question | Comparison result | Consequence for the client investigation |
|---|---|---|
| Are action prediction and numeric hit prediction the same? | **Different claims.** O1/O2 discuss action display/synchronization; O1 even describes server-supplied early hit feedback. | Client effects after S→C `0x1af` and local action animation do not establish a local numerical hit deduction. |
| Does the client contain damage arithmetic? | **Established locally; compatible externally.** Native offence callbacks and typed DoT arithmetic exist; PoB and O7 describe separate hit/ailment calculations. | These observations do not close actual contact → defender → RNG → pool mutation. |
| Is a reference hit chance an actual hit roll? | **Different consumers.** Native key 10745 is sheet reference evasion; PoB supplies configured enemy evasion; O9 distinguishes panel estimates and per-target entropy. | Reconstructing the formula alone is insufficient to claim numerical pre-resolution. |
| Source DoT versus defender DoT? | **Architectural agreement.** O4/O5 distinguish pre-mitigation source rate from typed defender totals, mitigation and resource routing, matching the roles in the audited stat graph. | Source-instance producers, eligibility and freshness on a network entity still require native binding evidence. |
| Does penetration apply to DoT? | **Agreement within audited scope.** O8/O4/O5 explain no source-specific DoT penetration; PoB generic DoT omits it; audited native fire DoT does not read it. | This corroborates one boundary; it does not certify every special DoT rule. |
| Can changing defender resistance affect existing DoT? | **Agreement in principle.** O5 says yes; both PoB effective mode and native mitigation use defender resistance. | PoB recalculation is not proof of native invalidation, recomputation or publication timing. |
| Are all DoTs one timer/stack rule? | **No.** O6 gives independently expiring ignites with only the strongest active; W1 distinguishes poison and shared-stack effects; PoB has distinct stacking/ground/ailment branches. | A base-rate getter cannot stand in for effect storage, application, strongest-selection or expiry. |
| Is the cap a minute-reset allowance? | **No evidence for that interpretation.** Native per-minute rates and PoB's per-second model conversion explain the magnitude. | Preserve rate units and elapsed-time integration; no reset mechanism is established. |
| Is cap placement identical? | **Different audited boundaries.** Native taken-as fire saturates before mitigation; PoB generic DoT caps its total after effective mitigation, and caps other ailment/combined outputs too. | Neither a universal final-only clamp nor official-server parity follows from the common 35.8M magnitude. |
| Does 33 ms govern every client resource update? | **Unsupported.** O3 supplies frame order, W1 supplies 33 ms, PoB hard-codes 0.033; native Life uses its supplied `dt`. | Keep scheduling, integration and correction measurements separate. Official-current cadence remains unmeasured. |
| Does recovery precede DoT? | **Historically explained by O3; locally comparable.** Native pool integration has separately identifiable recovery/loss paths. | Caller/order analysis must establish the actual client sequence and guard conditions; O3 cannot replace it. |
| Is current official-server arithmetic recovered? | **Unresolved.** No current official server binary, trace or full formula specification was obtained. | External agreement is corroboration; divergence between models is not refutation of either live implementation. |

## Completion boundary

This pass establishes a reproducible, versioned **comparison**, including meaningful agreements, observable model
differences and primary-source limitations. It does not establish current official-server equivalence.
Exact current official-server parity is not a prerequisite for this investigation's goal: recover calculations,
rules and client/server responsibilities sufficient to guide server implementation. Static original-byte formulas
and versioned primary/community rules are useful evidence without a live replay. A claim of live parity or measured
cadence would need its own evidence; no such claim is made here.
Substantive remaining research includes unresolved formula inputs/cap order, actor-specific source selection,
aggregation/expiry and concrete per-defender hit responsibility. Missing official traffic does not invalidate
the closed static contracts, and a presentation callback does not close a missing numerical rule.
The main report owns the R0–R4 states and retains those independent completion criteria.
