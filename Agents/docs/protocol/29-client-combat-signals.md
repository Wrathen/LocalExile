# 29. Client combat signals and resource authority (research)

This page separates incoming combat feedback, resource totals and stat inputs.
It documents client 3.29.3b readers and consumers, not an official-server damage algorithm.
See [client damage calculation](../re/client-damage-calculation.md) for artifact hashes, formula evidence and open tasks.
No client execution or new packet capture was performed for this static audit.

## Message roles and established limits

| S→C message | Established client path | Not established by that message alone |
|---|---|---|
| `0x1af` (decimal 431) | Actor result record, conditional source/target feedback signals | Per-hit damage amount, complete miss/block/crit taxonomy, local numerical pre-deduction |
| `0x1b1` (decimal 433) | Assign current and reservation inputs for Life, Mana or ES | Which hit/source caused the difference, full rollback/replay, server tick cadence |
| `0x1b3` (decimal 435) | Replace explicit stat values through the network override getter | Raw-input recomputation of every omitted derived output |
| `0x1b4` (decimal 436) | Update explicit keys on a selected skill/stat-set network provider | Projectile trajectory, numerical hit amount or resource deduction |

An incoming Life difference is a resource observation, not hit attribution or kill credit.
A Render or sound callback is a presentation observation, not proof of a client damage roll.
The generated [schema](schema.md) can leave these factories unresolved; that is missing generator coverage.
It does not invalidate a separately audited concrete reader.

## `0x1af`: read sequence

Reader `0x141fddba0` calls common header reader `0x141f92000`, then tail-jumps to `0x1419bd6f0`
with the decoded message's `+0x20` record as its destination.
`0x1419bd6f0` first calls `0x14195dde0` on record `+0x40`.
The sequence below excludes the opcode and connection framing.
Multi-byte scalar fields are big-endian on the wire.
Names such as `record+0x36` are **decoded memory offsets**, not wire offsets or recovered semantic names.

| Read order | Wire field | Decoded destination / condition |
|---|---|---|
| 1 | u32 entity | Common world-message header |
| 2 | u32 unnamed | Common header `+0x14` |
| 3 | u16 unnamed | Common header `+0x18` |
| 4 | u16 | record `+0x40` |
| 5 | u8 | record `+0x43` |
| 6 | u8 | record `+0x42` |
| 7 | u32 | record `+0x44` |
| 8 | u16 mask | record `+0x36` |
| 9 | u8 wire flags | bit 0 → record `+0x48`; bit 1 → record `+0x49` |
| 10 | u16 | Only if wire bit 1 and mask `0x400`; record `+0x34` |
| 11 | u8 | Only if wire bit 1; record `+0x39` |
| 12 | u8 | Only if wire bit 1; record `+0x3a` |
| 13 | u16 | Only if wire bits 1 and 2; record `+0x3c` |
| 14 | u16 | Only if wire bits 1 and 3; record `+0x3e` |

The higher wire bits do not independently add fields in this reader.
Do not label the u16 mask a damage amount or a critical flag without establishing its consumer semantics.
The record's action/entity-related fields and guard conditions must be followed separately.

## `0x1af`: dispatch and feedback binding

Actor group dispatcher `0x1421940a0`, through `0x1421952a0`, reaches `0x141e90f50`.
Record `+0x48` selects immediate action-associated processing or an `0x88`-byte queued representation.
The immediate path invokes Actor virtual slot `+0x230`.
The audited Actor tables bind it to `0x141e91580`.
The queued path is tied to action progress and removal, rather than an arbitrary Life tick.
`0x141939f40` retrieves one matching result; `0x14193a170` and `0x141e930e0` select action-related ranges.
`0x141e91330` filters/compacts those ranges and forwards extracted records through the same `+0x230` slot.
`0x141e90c30` removes an exact keyed result and optionally processes it.
The guard-dependent retention and removal rules are documented in
[hit feedback](../re/client-hit-feedback.md#queued-lifecycle).

The downstream consumer resolves the record's entity reference and applies relationship/stat guards.
Its source branches use Actor signals `+0x2e8` and `+0x368`; guarded target branches use `+0x3e8`.
These have a concrete subscriber binding:

| Registration | Receiver callback | Established scope |
|---|---|---|
| Render constructor `0x1415f5d50` → source Actor `+0x2e8` | `0x141600c10` | Render callback; record is argument 2 |
| Same constructor → source Actor `+0x368` | `0x141600aa0` | Prepares `Audio/Sound Effects/WeaponSwinging/MissSG` |
| Same constructor → target Actor `+0x3e8` | `0x141609f40` | Target Render callback; record is argument 3 |

The different record argument positions follow the registered thunk ABI.
Decompiler-inferred signatures can omit forwarded parameters; the instruction audit takes precedence.
The MissSG cue does not identify every server miss/block outcome or prove a sound played.
Other subscribers and the numerical consequences of all mask bits remain research tasks.
No complete accuracy/critical/armour/pre-deduction chain is established by this dispatch.

The consumer also invokes target Actor control and knockback methods, not only Render subscribers.
Target `+0xc0 = 0x141e96630` uses record `+0x34 / 1000` as a duration under mask bit `0x400`.
Its request goes to Actor `+0xe0 = 0x141e95e60`, which includes an avoid-interruption chance branch.
Target `+0xd0 = 0x141e96790` computes Positioned displacement using knockback parameters.
The action `+0x2d0/+0x2d8` methods and event `0x6876` still need their downstream effects resolved.
These are concrete additional effects, but they do not establish a numerical damage roll or Life/ES/Mana deduction.

### Separate local contact production

The absence of a damage amount in `0x1af` does not imply that all contact decisions come from that message.
Concrete Fireball flight callbacks query the spatial grid, filter candidates and perform target spell dodge
and projectile avoidance. Named Melee callbacks use actual target evasion and update its Stats entropy.
See [contact production](../re/client-hit-production.md) for final table bindings, callsites and guards.
The inspected Fireball continuation consumes an existing queued-result range; the selected Melee contact
dispatch slot is empty. Neither binding establishes accepted-contact numerical damage or pre-deduction.

The event VM's callable boundary is also distinct from packet decoding. GEAL client-mode compilation rejects
the recovered server-only resource/stat/Buff mutation metadata, while real native callables can read Life
and install listeners executing supplied programs. Fresh selected player/Sand Spitter source chains supply
no custom-event field for `0x6876`; other configurations and dynamic registration remain separate questions.
See [GEAL eligibility and native listeners](../re/client-hit-resource-boundary.md#10-geal-callable-construction-and-clientserver-eligibility).

## `0x1b1`: absolute resource assignment

The [existing wire description](10-chat-and-commands.md) documents the resource message under Monsters.
The world handler `0x142187480` routes it to `0x141e69fb0`:

| Pool selector | Pool within Life | Call instruction |
|---|---|---|
| 0: Life | `+0x178` | `0x1421880e6` |
| 1: Mana | `+0x1c8` | `0x1421880cb` |
| 2: ES | `+0x210` | `0x1421880b7` |

The setter copies decoded current into pool `+0x30`, flat reservation into `+0x10`,
and percentage-reservation input into `+0x14`.
The setter itself does not reset fractional carry `+0x28` or replay input history.
Death/revive handling in the surrounding dispatcher is distinct from the pool assignment.
The Life-only nonpositive-to-positive branch calls `+0x100` and then zeros that pool's fractional carry
at `0x142188129`; ordinary assignments do not take this transition-specific block.
Guarded change/full/empty/loss notifications can update recharge delay and invoke event callbacks.
See [the concrete receiver bindings](../re/client-damage-timing.md#assignment-notifications-and-recharge-delay).

Between such assignments, the native Life tick can integrate continuous recovery and loss using stat rates.
That supports continuous extrapolation plus incoming value correction.
It does not establish that the client independently predicts numerical hit damage.
See [Stats update](10-chat-and-commands.md#3-sc-0x1b3-stats-update) for explicit-zero overrides and type fallback.

## `0x1b4`: update an existing skill's stat values

Factory `0x1421c6150` binds opcode `0x1b4` to table `0x1435c21c0`, reader `0x141fdeb10`
and serializer `0x141fdea50`.
After the common world header, the reader takes a big-endian u16 skill ID, u8 stat-set selector,
unsigned compact pair count, then unsigned compact stat keys and signed compact values.
Decoded fields are skill ID `+0x20`, selector `+0x22` and pair vector `+0x28..+0x30`.

Actor dispatcher `0x1421940a0` maps this opcode to `0x142194482`.
It finds an existing skill by ID, selects its primary provider for selector zero or additional set
`selector - 1`, and applies each pair through `0x1424cfaf0` at `0x14219464d`.
Unknown skills return; an out-of-range selector reports an error and returns.
The setter inserts/replaces supplied explicit keys and sends same-key notifications.
Omitted keys are retained. This is separate from the native contribution/evaluation transaction.

The compact pairs are **stat key/value pairs**, not spatial points.
The packet gives a wire identity to the previously audited subsequent skill updates and supports independent
updates of client-visible skill values. It does not establish which keys the official server sends.
See the [reader/consumer audit and PR #373 comparison](../re/client-damage-pr373-comparison.md#0x1b4-is-a-skill-stat-update).

## `0x1c1` / `0x1c2`: concrete Buff clock receiver

The existing Buff wire format remains in the protocol's Buff reference. The
[receiver audit](../re/client-buff-network-clock-contract.md) binds World dispatch `0x142188132`
through the named Buffs getter to handler `0x141e11720`.
For an existing instance and decoded selector `+0x81 == 0`, decoded duration/speed at `+0x68/+0x6c`
overwrite the retained Buff's `+0x1c/+0x20`. These are decoded-object offsets, not wire offsets.
Omitted speed decodes as `1.0` and therefore replaces the old speed.
The selected Burning Arrow backend's two post-copy update callbacks are exact no-ops.

This gives an implementation-ready timer synchronization contract. It does not specify which server mechanic
computes the speed, transform clock speed into a DoT multiplier, or bind source-rate aggregation.

## Remaining protocol questions

- Resolve action-specific `0x1af` virtual callbacks and event consequences beyond the established queue/control paths.
- Establish concrete meanings of the action-related fields and mask bits, preserving version and object guards.
- Determine whether another message or local action path supplies a real pre-deduction input.
- Trace how rate changes, fractional state and incoming resource assignment are ordered in the client loop.
- Keep official-server claims separate from any future, separately authorized LocalExile runtime experiment.
