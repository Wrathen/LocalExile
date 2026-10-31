# DoT instances: sampled source values, target response and independent clocks

## Result and evidence boundary

An applied ignite is not a promise to remove a fixed total amount of life. The useful
mechanical contract is a source-computed, pre-mitigation rate plus an independently advancing
lifetime. Target resistance, damage-taken effects, suppression by a stronger instance and
expiration speed can change the damage actually received during that lifetime.

This pass distinguishes three kinds of evidence:

- **Original-client arithmetic:** 3.29.3b contains outgoing ailment-rate formulas and separate
  duration formulas with reciprocal faster factors. It also has concrete Buff remaining-time
  and speed fields, an owner-dependent insertion calculator, and a network update writer.
- **Versioned primary explanations:** GGG explicitly describes sampling an individual debuff's
  source values, dynamic target response, event-driven stat updates, and per-frame timer
  advancement. The 2021 Q&A even specifies the rate and duration applied to an enemy.
- **Unresolved numerical publication:** these explanations do not recover the client function
  or packet contract that publishes one outgoing ailment instance into an incoming DoT pool.
  That source-to-incoming edge remains **Partial**. A registration or Buff name is not that edge.

No server implementation, client execution, capture acquisition or runtime intervention was
performed. This supplements [outgoing ailment formulas](client-ailment-outgoing-formulas.md),
[DoT routing](client-dot-routing.md), [Buff clock updates](client-buff-clock-updates.md), and
the [concrete network clock contract](client-buff-network-clock-contract.md).

| Pinned local artifact | Identity |
|---|---|
| Executable | `Client/Game.exe`, 3.29.3b, preferred image base `0x140000000` |
| Executable SHA256 | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `data/tables/Stats.json` SHA256 | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| `data/tables/BuffDefinitions.json` SHA256 | `3a23c6fdf7ba29fb8508e5c9560a44e5292345e45dd2f886ef7126b8427fd659` |
| Native key mapping | One-based native Stats key = zero-based JSON row + 1 |

## 1. What is retained, and what can change?

| Property | Mechanical rule supported by the sources below | Boundary |
|---|---|---|
| Individual debuff's source offence | Its values are set when that debuff is created. Later source-stat changes normally do not rewrite it. | GGG explanation; the native instance assignment is not recovered here. |
| Source "deal damage faster" | Changes outgoing rate and applies the reciprocal to duration before application. | Official numerical example plus current outgoing/duration arithmetic. |
| Source ignite "slower burn" | The pinned outgoing arithmetic divides by its factor; the duration arithmetic includes its matching factor. | Current formula observation; legacy availability and sampling are separate questions. |
| Target resistance / damage taken | Current target response affects ongoing DoT; it is not baked permanently into the source rate. | Explicit GGG explanation and the independently audited incoming formulas. |
| Target expiration speed | Changes how quickly remaining lifetime is consumed; it does not by itself multiply damage per real second. | Official Temporal Chains/Rupture explanations plus the native timer consumer. |
| Ordinary duration modifiers | Form part of an applicable duration calculation; do not infer that gaining one rewrites all existing instances. | Reflection is an explicit counterexample to reapplying target duration modifiers. |
| Maintained RF / Death Aura | Source values can update while the aura is maintained. | Explicit exception in Mark's 2021 explanation. |
| Created ground effect | Can retain source rate and lifetime independently of later source changes. | Wiki's explicit Caustic Arrow example; not proof for every ground skill. |

These are per-property policies. A single global `snapshot = true/false` loses the distinction
between a stable source rate, a changing target response and a changing expiration clock.

### Positive application contract, not just a modifier name

The [2021 GGG mechanics Q&A](https://www.pathofexile.com/forum/view-thread/3123794)
gives a concrete example: 100 DPS for 5 seconds with 25% faster bleeding becomes **125 DPS for
4 seconds**, and says these are the values applied in the enemy's debuff. The same answer
separates Rupture's target damage-taken modifier from its faster timer and states that both
change dynamically when Rupture is gained or removed. This is a positive official
**mechanics-layer** rate-plus-duration contract; it is not a recovered C/S packet layout.

[Mark's individual-debuff explanation](https://www.reddit.com/r/pathofexile/comments/mr3ap2/questions_thread_april_15_2021/guntwg8/?context=1)
explicitly contrasts a created ignite, whose values remain set, with continually maintained
RF/Death Aura, whose source updates propagate to their enemy effects. The
[2022 buff explanation](https://www.reddit.com/r/pathofexile/comments/uqudc8/when_you_pick_a_great_league_starter/i8wdsnm/?context=1)
adds that source magnitude modifiers normally lock at creation, whereas the target's effect
modifiers and "unaffected" state are dynamic. Magnitude and applied effect are distinct.

Together these justify treating source faster/duration calculations as application inputs
for an ordinary individual ailment. They do not prove every special copied, proliferated,
reflected or maintained effect shares the same construction path.

## 2. Current native duration arithmetic independently compensates faster

The following six registry entries were checked against original PE bytes. Each registration
passes its output key in EDX and callback in R8 to `0x142513630`. The additive entries are
included to identify the distinct source-duration outputs; this pass does not publish a
complete transcription of their conditional sums.

| Key / Stats Id | Registration owner | Callback |
|---|---|---|
| 2706 `virtual_ignite_duration_+%` | `0x1400cc730` | `0x1427fac60` |
| 6578 `virtual_ignite_duration_+%_final` | `0x1400cc7c0` | `0x1427fb8d0` |
| 4598 `bleed_duration_+%` | `0x1400cd3f0` | `0x142805120` |
| 9329 `virtual_bleed_duration_+%_final` | `0x1400cd470` | `0x142805d60` |
| 3424 `poison_duration_+%` | `0x1400cd660` | `0x142807490` |
| 6477 `combined_poison_duration_+%_final` | `0x1400cd6e0` | `0x142808060` |

Using the notation from the outgoing-formula report, `S(k)` is a current evaluation-context
read, `F(n) = max(0, float(add32(n,100))/100)`, and `R` is signed nearest rounding implemented
by adding signed 0.5 then truncating. For normal finite inputs, the final duration modifiers
are represented by these expressions; they are **percent modifiers**, not seconds:

```text
bleed9329 = R(100 * F(S14795) * F(S18832) * F(S22849)
              / (1 + float(S9320)/100) - 100)

poison6477 = R(100 * F(S6476) * F(S10557) * F(S14795)
               * F(S18832) * F(S22849)
               / (1 + float(S9319)/100) - 100)

Q = trunc0(mul32(add32(S6577,100), add32(S13009,100)) / 100)
ignite6578 = R(100 * max(0, float(Q)/100)
               * F(S14795) * F(S15280) * F(S18832)
               * F(S22516) * F(S22849)
               / (1 + float(S6203)/100) - 100)
```

These are readable arithmetic summaries, not a claim of bit-exact real-number evaluation.
The PE uses ordered binary32 operations, signed32 arithmetic and intermediate rounding.
In particular, ignite's slower/Replica-Volkuur product has a signed integer divide by 100
**before** conversion to float. The final outputs round without the upper-saturating `C`
helper used by some outgoing damage formulas. Invalid or extreme inputs need their actual
instruction semantics rather than extension of these finite-input equations.

| Input | Meaning in the pinned Stats table |
|---|---|
| 6203 / 9319 / 9320 | Virtual faster ignite / poison / bleed |
| 6577 | `ignite_slower_burn_%` |
| 13009 | Replica Volkuur ignite-duration final modifier |
| 6476 / 10557 | Volkuur poison-duration / active-skill poison-duration final modifiers |
| 14795 | Swift Affliction skill-effect and damaging-ailment duration final modifier |
| 18832 | Sadism (`support_faster_ailments`) ailment-duration final modifier |
| 22849 | Active-skill ailment-duration final modifier |
| 15280 / 22516 | Active-skill ignite-duration / Emberglow ignite-duration final modifiers |

The reciprocal sequences are explicit:

- Ignite reads 6577 at `0x1427fb9e3`, 13009 at `0x1427fbac3` and 6203 at
  `0x1427fbbc3`; the signed preproduct/division is at `0x1427fbaea..0x1427fbb04`.
  The reciprocal is formed at `0x1427fc13b..0x1427fc158` and multiplied into the
  duration factor at `0x1427fc166`; integer publication is at `0x1427fc1bb`.
- Bleed reads 9320 at `0x142805e53`; the reciprocal is formed at
  `0x1428061cb..0x1428061e5`, multiplied at `0x1428061e9`, and rounded at
  `0x14280622a`.
- Poison reads 9319 at `0x142808156`; its value is retained in EBP. The reciprocal
  is formed at `0x1428086fb..0x142808717`, multiplied at `0x142808720`, and rounded
  at `0x142808775`.

The separately audited outgoing combined-rate formulas use the matching faster factors,
and ignite's rate includes the reciprocal slower factor. This gives **both numerical halves**
of faster's compensation in this executable. It does not locate a consumer that writes
6578/9329/6477 into an actual ailment's lifetime, nor prove that a normal network entity
locally evaluates those callbacks. Current formula evaluation is not existing-instance mutation.

## 3. Duration, expiration and damage use different clocks

For explanation, let `D` be applied lifetime measured in effect-time seconds, `e(t)` be the
current expiration speed, and `r(t)` the source contribution selected at real time `t`.
Then the distinction is:

```text
remaining(t) = D - integral(e(u) du)
damage(t0,t1) = integral(targetResponse(selectedSourceRates(u), targetState(u)) du)
```

These are analyst equations expressing the sourced rules, not recovered server source code.
The damage integral uses real elapsed time. Do not multiply DPS by `e(t)` merely because
the timer advances with that speed. An independent damage-taken modifier may change DPS,
as in Rupture. A maintained source may change `r(t)`; a sampled individual source normally
does not. Selection, immunity, cap and resource routing can further affect actual loss.

For constant ordinary expiration speed, real lifetime is `D/e`. Increasing applicable
duration increases total potential damage at the same DPS. Faster damage instead increases
DPS and inversely shortens `D`, preserving expected total only while target response,
selection and cap remain constant and rounding is ignored.

### Native timer contract and official update policy

The native Buff timer consumer `0x141e10520` advances eligible remaining duration with
`remaining -= deltaTime * (definition.+73 ? 1 : Buff.+20)`. The new-instance insertion path
calls `0x141f79160` with the actual Buff and owner Stats, then stores its returned speed.
The ordinary speed expression has a `0.25f` floor and a separate bleeding/Rupture factor;
earlier named pause guards can return zero. The network `0x1c1` partial-update path also
copies remaining duration and speed into an existing instance. Full eligibility, field and
callback bounds are in the linked native reports.

In the [2022 explanation](https://www.reddit.com/r/pathofexile/comments/uqudc8/when_you_pick_a_great_league_starter/i8wdsnm/?context=1),
Mark says PoE iterates Buff timers each frame and subtracts elapsed time. It recalculates
and stores expiration scaling when something affecting that scaling changes. Separately,
Buff stat contributions are recalculated on relevant additions, removals or effect changes,
including a stronger effect disappearing. He explicitly rejects scanning all ailments and
reselecting/recalculating their effects every server tick as prohibitively costly.

That explanation supports a cheap timer loop plus updates on relevant events. It does not
close the **native owner-Stats change → existing Buff speed rewrite** subscription that is
still unresolved in this client study. It also does not specify 33 ms, sub-frame integration,
partial-last-frame treatment, fractional life deduction, or the order of simultaneous events.
No such timing details can be derived from the existence of a float duration alone.

### Versioned expiration limits

[Mark's 2014 Temporal Chains reply](https://www.pathofexile.com/forum/view-thread/941731#p7993723)
separates unchanged numerical duration from slower passage of effect time and confirms the
DoT can deal more total damage while lasting longer. The [2018 cap explanation](https://www.pathofexile.com/forum/view-thread/2240771/page/3#p15915997)
describes the normal 75% slower limit, but also a historical earlier failsafe that could
pause the clock beyond 100% slower and bypass that normal cap. This is not evidence that
the old reachable bug survives in 3.29.3b. The current inspected calculator's ordinary
0.25 floor and explicit pause guards are the current artifact observations.

## 4. Target response and instance selection remain dynamic

[Mark's 2021 typed-total explanation](https://www.reddit.com/r/pathofexile/comments/q464j5/comment/hfx5nq1/)
says the source supplies pre-mitigation DPS; the target adds contributions to its per-type
rate and performs mitigation and damage-taking on the total. A
[second reply](https://www.reddit.com/r/pathofexile/comments/q464j5/comment/hfxndgt/)
explicitly requires current fire resistance to affect ongoing fire DoT. Target mitigation
therefore cannot be permanently included in an ordinary source-rate snapshot. Resistance
changes from a curse or exposure are a straightforward application of that rule when
the particular modifier applies to that target and damage type.

The [earlier 2017 explanation](https://www.reddit.com/r/pathofexile/comments/634vc5/some_mechanics_questions_answered_by_mark/dfshfxm/?context=1)
also describes target-owned DoT stats with no creator-stat access during damage application.
It is historical architecture corroboration, not a substitute for the current PE formulas.

Logical instance existence is distinct from current damage contribution. The
[2022 Elemental Overload answer](https://www.pathofexile.com/forum/view-thread/3284453)
describes individually expiring ignites: the old one does not acquire a later offensive
bonus; a subsequently applied stronger ignite can take over. Poison sums independent
instances by default; ordinary ignite/bleed use their active strongest contribution, subject
to mechanic-specific overrides. These selection rules come from versioned mechanics
sources, not the timer fields alone.

Do not require physical storage of every losing elemental instance forever. The
[December 2022 official explanation](https://www.pathofexile.com/forum/view-thread/3324396/page/2#p24824609)
describes an optimization discarding an elemental ailment dominated in both strength and
remaining lifetime, and a bug involving extra effects such as Combustion/Bonechill, with a
fix planned. The current Wiki summarizes pruning with an extra-effects exception. Neither
establishes the exact current storage implementation or a rule for non-elemental ailments.
A weaker instance that can outlast the stronger one must remain semantically available;
attached extra effects can also make a simple single-winner representation insufficient.

## 5. Exceptions that prevent a universal duration rule

- **Reflected duration:** [Jatin's April 2023 reply](https://www.pathofexile.com/forum/view-thread/3366418#p24974604)
  confirms that reflected poison copies the original applied duration, rather than
  recalculating it with the player's reduced poison duration. The user's reported Apep
  expiration test on that page is not itself staff confirmation. Duration-copy and
  target-expiration operations must remain separate concepts.
- **Skill duration is not all ailment duration:** the full Wiki pages for Ailment duration
  and Viper Strike identify Viper Strike/Puncture's explicit overrides as exceptions.
  Swift Affliction explicitly names damaging-ailment duration; it should not be inferred
  from the presence of a generic Duration gem tag.
- **Decay is not an ailment:** [Mark's July 2022 reply](https://www.pathofexile.com/forum/view-thread/3205646)
  states that Decay has never scaled with skill-effect-duration modifiers. The full Decay
  page distinguishes its own lifetime from Storm Burst skill-duration-driven damage
  scaling. This is a concrete reason not to apply every duration modifier to every DoT.
- **Maintained effects:** RF's continuous source update is an explicit exception to
  individually sampled DoT. It does not justify making all area effects dynamic. The
  Wiki's created Caustic Arrow ground is a counterexample with retained rate and duration.
- **Historical snapshot removal:** the [2014 manifesto](https://www.pathofexile.com/forum/view-thread/951414)
  concerns the named aura/minion/maintained-effect changes. It cannot be read as removal
  of every independent finite debuff's sampled values. Its RF gem-removal details are
  historical and are not a verified modern unsocketing policy.
- **Copied/proliferated effects:** the full Ailment page distinguishes copying remaining
  duration, creating with a new duration, and aura-like proliferation. They require their
  own application contracts; one "copy DoT" operation is underspecified.

## 6. Numerical examples and limits

These examples use exact real-number arithmetic to explain the mechanics, not to predict
the client's float32 rounding, its integer minute publication or server frame rounding.
Assume one selected 100-DPS source, ordinary five-second lifetime and no mitigation:

| Change | Rate | Lifetime / result |
|---|---|---|
| None | 100 DPS | 5 s, potential 500 damage |
| Source 25% faster | 125 DPS | Applied lifetime 4 s, potential 500 damage |
| Applicable 50% increased duration | 100 DPS | 7.5 s, potential 750 damage |
| Target 25% slower expiration throughout | 100 DPS | 5 / 0.75 = 6 2/3 real seconds; potential 666 2/3 damage |
| Target 25% slower only from real time 1 to 3 s | 100 DPS | At 3 s, 2.5 effect seconds remain; expires at 5.5 s; potential 550 damage |
| Target gains 50% fire resistance at time 2 s | 100 raw fire DPS | 200 damage in first 2 s, 150 in last 3 s; total 350 |

The old Q&A's Rupture example combines 25% faster source bleeding with 25% more target
damage taken and 25% faster target expiration. Ideal arithmetic gives 156.25 DPS for
3.2 real seconds, preserving 500. The official answer reports **156 DPS and 499 damage**
after rounding; preserve that reported result rather than claiming exact conservation.
Those percentages illustrate the 2021 mechanic, not current Rupture Support values.

If current source rate is 125 DPS but the applicable aggregate cap permits only 100 DPS,
four real seconds can deliver at most 400 damage in this simplified example. Faster's
pre-cap rate-duration compensation does not create a protected post-cap damage budget.
This is an arithmetic consequence, not recovery of the server's exact shared-cap placement.
The client already exposes damage/minute formula units; expressing the same rate per
second or per minute changes units, not a timer into a rolling one-minute damage allowance.

## 7. Reference coverage and reproducibility

The local reference library's Wiki snapshot is dated **2026-10-13**; its exact correspondence
to 3.29.3b is unverified. The source was paged to completion, rather than reading only a
single excerpt. Each continuation offset, total character count and revision is retained.
Twenty-three complete source records were acquired; two are redirects (Debuff and Temporary
effect), not additional mechanics articles. Mechanics and relevant historical notes were
reviewed separately from generated item/query templates.

| Full Wiki source | Revision | Full Wiki source | Revision |
|---|---|---|---|
| Ignite | 1598418 | Poison | 1700788 |
| Bleeding | 1700575 | Damage over time | 1702687 |
| Temporal Chains | 1681627 | Duration | 1659328 |
| Ailment | 1673310 | Buff | 1701074 |
| Sadism Support | 1682278 | Swift Affliction Support | 1681929 |
| Unbound Ailments Support | 1681954 | Righteous Fire | 1681585 |
| Decay Support | 1700612 | Rupture | 1660589 |
| Expiration rate | 1673449 | Snapshot | 1697725 |
| Ailment duration | 1482835 | Viper Strike | 1681619 |
| Rupture Support | 1682287 | Emberwake | 1677241 |
| Low Tolerance | 1619687 | Redirects | 118420 / 269159 |

The Sadism revision says faster and slower add together. That is not the algebra recovered
for the pinned ignite rate/duration callbacks, which treat slower as a reciprocal rate
factor and a matching duration factor. The Wiki also contains explicitly stale commented
historical duration tables. Do not use either as the current executable's arithmetic
authority; version differences and edits must be resolved before a cross-version claim.
Emberwake's full history states that 3.16 removed slower burning for new items while leaving
existing items unaffected. A retained native slower input can therefore have a legacy-item
contract; its presence alone does not prove availability on a newly generated item.

Primary pages were read through the web connector, retaining staff attribution separately
from ordinary replies and bot quotations. Direct forum HTTP requests returned 403 and are
recorded as failed, not as primary page captures. Forum displayed dates can differ from
Wiki citation dates by a day; the Q&A example is identified by its actual thread/answer
and 2021 version, rather than forcing an unverified timezone conversion. A quoted Vortex
reply whose original comment was not opened is not used as independent primary evidence.

Ignored evidence is under `Tools/re/out/client-damage-audit/`:

- `r6-ailment-duration-formulas/`: 12 function records containing the six registrations
  and six duration callbacks, original-byte range hashes and instruction records.
- `r6-dot-instance-rules/`: complete Wiki source continuations, revision ledgers, retained
  primary connector responses, failed-fetch ledger, and independent verification output.
- `r6-dot-instance-rules-verify.py`: independently rechecks original bytes and instruction
  boundaries, registration argument flow, selected semantic instructions, table IDs, full
  Wiki continuation coverage, retained primary anchors and exact arithmetic examples.

Remaining work is narrowly stated: recover an actual source rate/lifetime → numeric
instance publisher → incoming typed contribution path, and distinguish any server-supplied
values from client-local evaluation. Owner timing-stat changes to existing native clocks,
current cap placement, sub-frame boundaries and skill-specific exceptional construction
also remain separate open questions. This report does not mark those edges complete.
