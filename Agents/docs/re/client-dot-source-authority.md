# DoT source authority and instance selection (3.29.3b)

The client exposes a positive **server-mode source-creation contract**: the GEAL
metadata for five Aura APIs, like the previously audited AddBuff APIs, is
rejected by ordinary client-mode compilation. This narrows the responsibility
boundary without recovering the server implementation. The concrete
source-instance-to-incoming-rate publisher remains **Partial**; no new numeric
stacking formula is marked recovered from client code here.

This is a supplement to [source publication](client-dot-source-publication.md),
[stat evaluation](client-stat-evaluation.md), [network Buff clocks](client-buff-network-clock-contract.md)
and [official/community/PoB comparisons](client-damage-comparisons.md).
It combines original-byte static evidence with separately labelled external
mechanics evidence. No client launch, gameplay operation, runtime capture,
server change or shared native-analysis session was used.

## Positive GEAL eligibility evidence

Executable SHA256 is `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`;
addresses use preferred image base `0x140000000`.
The existing [GEAL compiler audit](client-hit-resource-boundary.md#10-geal-callable-construction-and-clientserver-eligibility)
binds the `server` and `client` compiler keywords to mode values 2 and 1, with
3 accepting both. Constructor `0x14013ebc0` establishes the ordinary mode-1
global at `0x14013ed38`. These are separate from the action-context mutation guard.

The following newly checked registrations each occur once in the 1,230-call
metadata initializer. Each stores literal mode 2 in a stack slot, passes the
same slot in R9 and the name in R8 to insertion helper `0x141ee1bf0`:

| Metadata name | Mode-2 store | R9 argument | Registration call | Name literal |
|---|---|---|---|---|
| `AddAura` | `0x141ed633a` | `0x141ed6357` | `0x141ed636f` | `0x1435b3a68` |
| `AddRectangularAura` | `0x141ed6399` | `0x141ed63b6` | `0x141ed63ce` | `0x1435b3a40` |
| `AddDonutAura` | `0x141ed63f8` | `0x141ed6415` | `0x141ed642d` | `0x1435b3aa8` |
| `AddAuraTargetPredicate` | `0x141ed6457` | `0x141ed6474` | `0x141ed648c` | `0x1435b3a78` |
| `SetAuraSize` | `0x141ed64b6` | `0x141ed64d3` | `0x141ed64eb` | `0x1435b39b0` |

The helper retains R9 in R14 at `0x141ee1c0a`, reads its integer at
`0x141ee1c61`, and writes metadata node `+0x30` at `0x141ee1c64`.
Named compiler `0x141df94b0` searches the native registry before metadata.
Its metadata path loads that mode at `0x141df96c6`, accepts mode 3, equality
with current mode, or current mode 0, and otherwise branches at
`0x141df96db` to `0x141df989a`, the function-does-not-exist error path.
Therefore mode-2 metadata is ineligible in ordinary mode 1.

None of these five names appears in the two audited native arrays: 226 pairs
at `[0x144488380,0x1444891a0)` and 499 pairs at
`[0x1444999b0,0x14449b8e0)`. This is a bounded registry inventory, not a claim
about every dynamic registration. It extends the already checked unique
mode-2 `AddBuff`, `AddBuffWithOwner`, `AddBuffAdvanced`, `AddBuffInRadius`,
`AddBuffTemplate`, `CalculateDamage`, `AddStat` and `RemoveStatsFromSource` names.

Mode eligibility does not reveal how the server creates or evaluates an Aura.
In this client, an accepted metadata-only fallback produces an empty-return
placeholder (`0x141dfeec0`), not hidden server code. Nor does this imply that
all client GEAL is presentation-only: `IfBuff`/`HasBuff` have native bindings,
and the independently audited `ClientOnlyKillSelf` writes a concrete Life pool.
The name `AddTerrainBurn` is mode 1 with a native factory; it must not be
classified as a server damage-source mutator merely from its name.

## Buff instance state and numerical rate have different receiving contracts

The selected AddBuff reader `0x141fdf870` zero-initializes the granted-value
vector at decoded packet `+0x48` using the definition's GrantedStats count
(`0x141fdfca4..0x141fdfcaf`). Its `IsRecovery` test at `0x141fdfcb8` and branch
at `0x141fdfcbc` skip numeric-value decoding when that flag is false.

| BuffDefinitions JSON row | Id | Granted native stat keys | IsRecovery |
|---|---|---|---|
| 0 | `ignited` | 8215 | false |
| 10 | `poison` | 8216 | false |
| 12 | `righteous_fire` | 882, 21656, 26, 881, 1948, 19861 | false |
| 13 | `righteous_fire_aura` | 2023 | false |

These four selected definitions do not obtain their damage-rate magnitudes
through that recovery-only value loop. The reader also carries other state,
including a separately counted vector at `+0x88`; it is not correct to say
that a Buff packet contains only a timer and quantity. For the bound RF
backend, that separate vector's first value selects art variation.

An independent positive receiver exists for existing instance clocks: World
messages `0x1c1/0x1c2` reach the target's named Buffs component, and handler
`0x141e11720` matches instance key `+0x9c`. The retained-update branch copies
transported remaining duration and speed to Buff `+0x1c/+0x20` at
`0x141e11b89/0x141e11b8f`. Neither this speed nor a transported quantity is
thereby proved to be a damage-rate multiplier. The clock report retains the
exact selected callbacks, including their no-op behavior for BurningArrowDebuff.

Owner Stats has a different contract: creation pairs and S→C `0x1b3` use the
network override setter `0x1424cfaf0`. Getter `0x1424cfa40` returns an explicit
override, including zero; only an absent key falls back. A native contribution
transaction and a network override write are distinct operations. Life/ES/Mana
steps later consume continuous-rate stats and integrate rate divided by 60
over their supplied duration, with fractional carry in the common reducer.

This proves separable receivers for instance state and Stats, and a consumer
for numerical resource rates. It does **not** prove that a particular official
capture transported keys 3361/3362/6270, that every DoT magnitude is sent as a
final-rate override, or that Buff packets alone reconstruct source instances
for numerical simulation. The named RF template's 975,000-per-minute input
also remains declared data until a concrete application/publication edge is bound.

## Source-selection rules available from external evidence

The following are server-model constraints supported by cited mechanics
sources, not recovered implementations of the above metadata functions.
Mark_GGG's [2022 answers](https://www.pathofexile.com/forum/view-thread/3284453)
explicitly retain independently expiring ignites and select the strongest;
a later offensive bonus applies to a later application. The pinned
[2021 source/target explanation](https://www.reddit.com/r/pathofexile/comments/q464j5/comment/hfx5nq1/)
separates source pre-mitigation DPS from target typed totals, and the
[defender follow-up](https://www.reddit.com/r/pathofexile/comments/q464j5/comment/hfxndgt/)
allows changing resistance to affect ongoing DoT.

| Effect | Retained state and default contributing rule | Boundary |
|---|---|---|
| Ignite | Retain independently expiring applications; strongest applicable ignite contributes by default; a suppressed application can later become strongest. | Additional-ignite mechanics and other stats on suppressed effects need their own policy. |
| Poison | Each independently expiring poison contributes; default damage rates add. | Special stack limits or additional-poison rules can override default eligibility. |
| Bleeding | Retain independent applications; ordinary damage selects the strongest by default. Moving changes the extra-damage contribution. | Crimson Dance selects up to eight strongest and removes the moving bonus; aggravated bleeding adds a separate selection problem. |
| RF and ground effects | A maintained area can supply a current rate without a fixed total-damage-over-lifetime value. | Effect identity and overlap rules remain distinct from damage type; all fire DoTs are not one strongest-fire pool. |

Versioned community sources are
[Ignite, revision 1598418](https://www.poewiki.net/index.php?title=Ignite&oldid=1598418),
[Poison, revision 1700788](https://www.poewiki.net/index.php?title=Poison&oldid=1700788),
[Bleeding, revision 1700575](https://www.poewiki.net/index.php?title=Bleeding&oldid=1700575) and
[Damage over time, revision 1702687](https://www.poewiki.net/index.php?title=Damage_over_time&oldid=1702687).
The retained source slices cover the first 8,000 characters of each page;
they do not constitute a review of every section or every stacking exception.

The bleeding source gives a useful counterexample to a universal
`max(each instance's effective DPS)` selector. A 100-DPS bleed becomes
aggravated, providing 200 extra DPS. Then a 200-DPS unaggravated bleed is
applied. The cited result is **400 DPS stationary** (200 normal plus 200
aggravated extra), and **600 DPS moving** (200 normal multiplied by three).
Normal and aggravated-extra selection can choose different applications;
the moving and aggravated bonuses do not stack. This paragraph is evidence
from the versioned wiki; its linked 2024 forum sources have not been
independently opened in this pass. It is not a newly verified native formula.

Consequently a reusable server model should retain instances independently
of whether their damage currently contributes, distinguish effect-selection
groups from damage types, and allow a policy to emit several contributions
from different selected instances. Source offence, instance clock, eligibility,
target mitigation and resource integration need separate ownership. These
are reconstruction implications, not a claim that the client's internal
instance scheduler has been recovered. Snapshot rules for every faster/slower
modifier, tie-breaking and all special stack policies remain research items.

## PoB comparison and remaining work

The pinned PoB revision remains
[`16de4b82d57f1c0de6eb40f37143c32d4da36a02`](https://github.com/PathOfBuildingCommunity/PathOfBuilding/commit/16de4b82d57f1c0de6eb40f37143c32d4da36a02).
In `CalcOffence.lua`, lines 4895–4896 multiply single-poison output by an
estimated stack count and cap; lines 5214–5216 bound active ignites; line
5871 estimates stackable DoT using rate, application speed and duration;
lines 6250–6251 sum selected-skill DoT outputs and cap again. These are useful
calculation checkpoints, not a real-time instance scheduler for a defender
receiving every source simultaneously. The client separately contains
intermediate per-minute saturation before selected mitigation and resource
stages. Neither source establishes a minute-reset budget or certifies one
universal final effective-DPS cap for the official server.

The open numerical edge remains a concrete source/effect application and
target leading to a rate write or equivalent contribution, with the selection
and expiry policy identified. Server-mode API metadata clarifies eligibility;
it cannot complete this missing edge. Native formula results, network
freshness and gameplay-specific selection exceptions must retain independent
completion states. Missing runtime observation is not a defect in the static
evidence establishing the compiler/receiver contracts above.

## Reproduction

The ignored `Tools/re/out/client-damage-audit/r4-dot-source-authority-verify.py`
rechecks the original executable and BuffDefinitions identities, 19 retained
audit ranges and 16,076 instruction-byte records, all 1,230 metadata names/calls,
725 native pairs, the five Aura mode/argument paths, compiler mode checks and
a fresh unwind-owned 322-instruction AddBuff reader. It also records hashes
and coverage for the four versioned wiki slices and checks the pinned PoB
file and selected line anchors. Run with `python -I -B` from the repository
root. Its private `r4-dot-source-authority/verification.json` retains the result;
no extracted game data, native disassembly or local absolute path belongs in
the public report. BuffDefinitions SHA256 is
`3a23c6fdf7ba29fb8508e5c9560a44e5292345e45dd2f886ef7126b8427fd659`.
