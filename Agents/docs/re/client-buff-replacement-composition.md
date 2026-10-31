# Retained Buff replacement: clock precedence and preserved fields (3.29.3b)

## Result and scope

An existing-instance `0x1c1` **replacement** now composes through the original
decoded-message converter, base Buff constructor, retained-object updater,
reference-holder notification/renewal, temporary destruction and subsequent ticks.
This differs from [partial clock updates](client-buff-clock-authority.md).
It is not removal followed by insertion: the retained object keeps its identity,
and its clock is not recomputed by the owner-Stats insertion calculator.

The retained updater copies a selected subset of the temporary object's state.
In particular, it accepts new remaining duration and speed while preserving the
old construction-duration field and two timing-applicability inputs. These are
conditional receiving contracts, not universal instance-refresh semantics.
**R2/R3/R4 remain Partial.** Generic owner-stat-driven clock refresh, DoT source
publication/selection/shared cap and numerical Hit-to-pool remain open.

All evidence is offline original-instruction interpretation. No client, attachment,
capture, server implementation or CI was run. The pinned executable, Stats and
BuffDefinitions identities are those of the [clock-authority report](client-buff-clock-authority.md).

## 1. Actual construction and retained-object update

The supplied decoded message matches instance key 17 and sets replacement selector
`+0x81` to one. Its definition/resource handles, granted vector, remaining duration,
speed and other fields are explicit fixtures. Original `0x141e11720` executes:

1. `0x141e07220` converts the decoded message to construction inputs.
2. `0x141e06290` constructs a temporary base Buff, including its observer container,
   construction/remaining duration, speed, applicability fields and granted vector.
3. `0x141e067c0` updates the existing object from that temporary.
4. The actual reference holder is renewed through `0x141e092a0`, the temporary is
   destroyed through `0x141e06650`, and the component's empty script event completes.

The updater first dispatches the retained Buff's observers through `0x14019a240`
at `0x141e067fc`. The real housekeeping record reaches `0x140145180`, thunk
`0x141e05e30` and `0x141e091c0`; it withdraws the holder before field transfer.
This is the existing instance observer, not an owner-Stats subscription. A new
holder is registered after transfer. The null backend remains null; arbitrary
backend or script effects are not supplied by an assumed no-op implementation.

Both incoming definitions are pinned rows: `ignited` (0) and `poison` (10), each
category two, empty Id2 and non-recovery. Tests start with the poison instance
and either retain its definition or supply the ignite definition. These object
inputs test the receiving branch; no official packet or gameplay transition is
claimed. Native row fields and loaded-resource state are supplied explicitly.

## 2. Replacement transfers selected fields rather than the whole object

An observation immediately before the original updater records both actual
objects. The following witness values deliberately distinguish old and new state:

| Buff field | Retained before | Constructed temporary | Retained after |
|---|---:|---:|---:|
| `+0x18`, construction duration | 9 | 2 | **9** |
| `+0x1c`, remaining duration | 3 | 2 | **2** |
| `+0x20`, stored duration speed | 1 | Supplied speed | **Supplied speed** |
| `+0x24`, additive clock input | 17 | 0 | **0** |
| `+0x30`, applicability input | 19 | 2 | **19** |
| `+0x38`, timing flags | 21 | 8 | **21** |
| `+0x9c`, instance key | 17 | 0 | **17** |

The definition pointer is transferred at `0x141e0680d`, remaining duration at
`0x141e068e8`, speed at `0x141e068ee` and additive input at `0x141e068f4`.
Granted words move through the actual vector-transfer helper. The component vector
still contains the original object; no replacement insertion is executed.
The numeric applicability witnesses above are selected test inputs, not named
gameplay states. They demonstrate preservation without inventing their semantics.

## 3. New clock values take precedence over changed owner Stats

Each composition first executes ordinary insertion and an original owner-Stats
update/notification. Owner `buff_time_passed_+%` becomes -90 or +100. Replacement
then accepts speed zero, 0.125, one or two, without invoking `0x141f79160` or
insertion `0x141e11dc0`. The local initializer's 0.25 floor is not reapplied.
Recorded instruction sites cover the entire replacement and subsequent tick/loss
phase, including tail dispatches; this is not inferred solely from direct calls.

With new remaining duration two and supplied delta `0.033`, an eligible tick gives:

| Stored speed | Definition force-one disabled | Definition force-one enabled |
|---:|---:|---:|
| 0 | 2 | 1.9670000076293945 |
| 0.125 | 1.9958750009536743 | 1.9670000076293945 |
| 1 | 1.9670000076293945 | 1.9670000076293945 |
| 2 | 1.934000015258789 | 1.9670000076293945 |

The force-one definition byte is a supplied override, not a claim that these two
pinned rows enable it by default. The tick reads the transferred definition and
uses its original force-one branch. Delta `0.033` is a test input, not a measured
official cadence or a proof of subframe event handling.

The explicit network Life loss-rate override remains 6,000 per minute. Subsequent
original Life integration changes current 100/carry 0.25 to current 97/carry
-0.04999995231628418. Changing a Buff's clock or zero granted word does not clear
that supplied aggregate in these compositions. This extends [rate independence](client-buff-rate-independence.md)
without identifying its actual source publisher or treating duration speed as DPS.

## 4. Reproduction and limits

Ignored local evidence under `Tools/re/out/client-damage-audit/`:

- `r49-buff-replacement.py` and `r49-buff-replacement/frontier.json`:
  **33 compositions / 133,864 interpreted instructions / 1,989 original sites**.
  The 32 ordinary-shaped inputs use zero granted words, as the non-recovery wire
  contract requires. One additional word-314 case tests structural transfer only;
  that synthetic decoded value is not attributed to the real non-recovery decoder.
- `r49-buff-replacement-fresh.py` and the batch's `audit.json/fresh-check.json`:
  **52 function identities / 68 ranges / 3,370 decoded records**, 13 instruction
  anchors and two pointer slots. Every visited site matches original decoding.

The same-agent scalar and original-byte checks supply definition/resource/provider
state, available containers, component lookups, empty backend/script collections
and one observational Stats listener. During replacement and later tick/loss,
five support hook targets execute: four-byte allocation `0x142c835e0`, byte-preserving
memmove `0x142d7d580`, the exact-zero stub `0x1400ecd70`, free `0x142c83550` and
resource-policy helper `0x1401f1f90`. Setup/insertion additionally retain the
explicit lookup/backend/policy fixtures of the parent clock composition.

This does not replay raw message decoding, World scheduling, official sends, a
nonempty numerical backend or the whole observer graph. No independent worker or
native execution is claimed. Server reconstruction should preserve **identity,
field-level refresh policy and clock authority** separately, while continuing to
resolve the original numerical source-selection, publication and cap obligations.
