# Client received-hit result qualifiers

## Scope and evidence

This supplement follows the existing received-result consumer and its selected
subscribers in the executable pinned by [the main report](client-damage-calculation.md).
It establishes field uses and local control admission, not a numerical hit resolver.
The [feedback report](client-hit-feedback.md) owns dispatch, action matching and queue lifetime;
the [protocol report](../protocol/29-client-combat-signals.md) owns serialization.
Projectile contact, result identity and concrete action callers remain separate investigations.
No client execution, attachment or new capture was performed.

The ignored `r9-hit-result-qualification` batch retains eight complete unwind-owned functions,
ten ranges and 5,026 instructions, including the existing Actor result consumer,
source/target Render subscribers and three target control receivers.
The focused `r9-hit-result-verify.py` rereads original bytes, field-use anchors, literals,
float constants and exact Stats identities. Full subscriber bodies are retained for audit;
their rendering internals are not a research completion requirement.

## Received flags can be changed before later subscribers

The working record passed to `0x141e91580` is mutable. After target resolution,
relationship checks and the record `+0x49` admission branch, the selected path performs
knockback/control admission before continuing to later callbacks. These are successive
operations on one record, rather than independent immutable descriptions of the wire result.

| Field/condition | Positive original-byte behavior | Limit |
|---|---|---|
| Mask `+0x36`, bit `0x800` | Calls target Actor `+0xd0`; a false return clears this bit with `0xf7ff` at `0x141e91a37..0x141e91a3c` | Knockback admission; no damage amount is changed here |
| Mask bit `0x400` or `0x40` | Enters the current target control-admission branch at `0x141e91a44..0x141e91a5f` | This branch is conditional on the earlier consumer gates |
| `cannot_be_stunned_while_leeching` (3470) and `is_leeching` (2919), both nonzero | Clears `0x400` and writes zero to u16 `+0x34` at `0x141e91a9d..0x141e91aaa` | Does not clear `0x40`; later decisions still execute |
| `cannot_be_stunned` (1194) | Nonzero selects the suppression branch at `0x141e91c31..0x141e91c40` | Suppression below is not identical to clearing the mask |
| `cannot_be_stunned_if_you_have_been_stunned_recently` (5947) and `have_been_stunned_recently` (5949) | Both nonzero select suppression at `0x141e91c46..0x141e91c66` | Reads the current target stat provider |
| `cannot_be_stunned_if_you_have_blocked_a_stun_recently` (6503) and `have_blocked_a_stun_recently` (6505) | Both nonzero select suppression at `0x141e91c6c..0x141e91c8c` | Does not establish how those recent-state stats are produced |
| `cannot_be_stunned_while_stunned` (12360) | Nonzero plus target Actor byte `+0x219 & 2` selects suppression at `0x141e91c92..0x141e91cac` | The native state-bit test is retained explicitly |

The target provider binding is concrete: target Entity is stored at local `+0x168`
after lookup at `0x141e91634`; `0x141e91708..0x141e91714` obtains its Stats component
and provider in RBX. These are not reads of the attacker's offensive stat context.

The shared suppression branch `0x141e91d37..0x141e91d53` zeros duration `+0x34`
when `0x400` is set, **without clearing that bit there**. If `0x40` remains set,
it still invokes target Actor `+0xc0`. Consequently an immune/control-suppressed
target can follow a listener path with a zero-duration record; suppression is
not equivalent to discarding the complete received event.

On the ordinary admitted branch, `0x40` selects Actor `+0xc0` first. Otherwise
`0x400` selects `+0xc8`; a false return from that directional receiver clears
`0x400` and zeros the duration at `0x141e91d27..0x141e91d30`.
Additional flask/resource-dependent gates precede these calls; this table does not
replace their complete branch order with a universal immunity predicate.

The established slot bindings are `+0xc0 → 0x141e96630`,
`+0xc8 → 0x141e96500`, `+0xd0 → 0x141e96790` and `+0xe0 → 0x141e95e60`.
Both control receivers check current `frozen_in_time` (9469).
The `+0xc0` receiver returns before dispatching its listener lists when frozen;
the directional receiver returns true without requesting a control action when frozen.
These different contracts must not be simplified into one boolean hit outcome.

## Direction and duration are separate from damage

Actor `0x141e91580` reads byte `+0x3a`, converts it to float, divides by **255.0f**
and multiplies by the stored float approximation of **2π** at
`0x141e91d5b..0x141e91d8a`. The constructed direction is stored at target Actor
`+0x2b8`. Directional receiver `0x141e96500` repeats this decoding, subtracts
the target's orientation, wraps/scales the angle, and selects control-action
identifier `0x7d`, `0x7f`, `0x80` or `0x7e`.
This is a quantized direction, not a damage scalar or RNG seed.

Both target control receivers convert the unsigned u16 at `+0x34` to seconds
by division by **1000.0f**, then pass it to Actor `+0xe0`.
Receiver `+0xc0` uses action identifier `0x2e` when `0x400` is set.
If that request fails, it clears `0x400` and the duration before dispatching
its listener lists. These are positive consumers of a control duration;
they do not establish the damage or stun-threshold formula that produced it.

## Critical feedback has a positive selected consumer

Source Render subscriber `0x141600c10` keeps the received record in R15.
At `0x1416016f1..0x1416016ff`, a selected branch tests record mask bit **0x20**:

- Set: selects the executable literal `EnergyShieldCrit` at `0x1434696c8`
  through the LEA at `0x141601706`.
- Clear: selects `EnergyShieldHit` at `0x143469748`
  through the LEA at `0x141601751`.

Both branches feed the selected name through the same following lookup/request path.
This supports a **critical-feedback qualifier** for the received bit, rather than
guessing solely from an unrelated resource name. Other preceding conditions select
whether this effect branch is reached; a critical bit does not guarantee an effect
request for every target or every result.
It does not establish a local critical roll, critical multiplier application,
critical confirmation, or life/ES deduction.

The low three bits of byte `+0x39` also select feedback variants in the source
subscriber (`0x14160309d`) and a selected target subscriber branch
(`0x14160a2d1`). The latter additionally requires target `is_tornado_owner_id`
(14982) and mask bit `0x4000`. Preserve these as opaque effect selectors here:
the complete mapping to gameplay damage types has not been established, and
using such a selector does not demonstrate a numerical damage channel.

## Consequence for server reconstruction

Keep authoritative result qualifiers, current client control admission, listener
eligibility and resource updates as distinct contracts. A later callback can receive
a locally rewritten mask/duration, while critical feedback consumes a supplied flag.
Neither `Damage > 0` nor one `WasHit` boolean can faithfully represent these paths.
This finding narrows the received-event boundary; the numerical hit publisher,
defender-resolution invocation and connection to resource deduction remain open
under the original research criteria.
