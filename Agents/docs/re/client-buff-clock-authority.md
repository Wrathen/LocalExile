# Buff insertion, owner-stat notifications and retained network clocks (3.29.3b)

## Result and scope

Three previously separate contracts now compose through original instructions:
Buff insertion calculates and stores a clock speed from current owner Stats;
a later owner timing-stat update stores and notifies its new value; and an
existing-instance network update directly replaces the retained clock. In the
supplied empty-lifecycle state with one observational Stats subscriber, that
owner update does **not** recalculate the existing instance's stored speed.
Its next eligible tick still consumes the old speed. A separate calculator
call sees the new value, but is a counterfactual invocation, not a discovered
automatic refresh.

The local calculator's ordinary `0.25` floor is an **initial calculation**
rule on this composition. The partial network update does not apply it:
transported speed zero remains zero and the next eligible tick does not
consume duration. Definition byte `+0x73` is a separate override: it makes
that tick use one even when the stored network speed is two.

This refines [clock initialization and updates](client-buff-clock-updates.md),
[the network clock receiver](client-buff-network-clock-contract.md) and
[Buff/rate independence](client-buff-rate-independence.md). It does not close
the generic owner timing-stat to existing-Buff refresh subscription, numerical
DoT source publication, strongest-instance selection or shared cap.
**R2/R3/R4 remain Partial.**

Everything below is offline interpretation of original instructions with
explicit supplied state. No native client execution, attachment, new capture,
server implementation or CI/deployment occurred. Addresses are preferred VAs,
base `0x140000000`, not wire offsets.

| Artifact | SHA256 |
|---|---|
| Original 3.29.3b `Game.exe` | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `Stats.json` | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| `BuffDefinitions.json` | `3a23c6fdf7ba29fb8508e5c9560a44e5292345e45dd2f886ef7126b8427fd659` |

## 1. Actual insertion owns its initial speed write

`0x141e11dc0` takes a supplied Buff holder in R8. It obtains the owning
Entity's Stats component at `0x141e11de4..0x141e11de8`, reads its provider
`+0x20` at `0x141e11df5`, and calls `0x141f79160` at `0x141e11dfc`.
`0x141e11e01` stores the returned float32 into the actual Buff's `+0x20`.
The remaining insertion instructions append the same object to the component
vector, clear the holder, assign identity/state, select the backend and publish
the native lifecycle/script events.

The new controls execute this full insertion body, the complete clock
calculator, the original network getter and empty lifecycle publishers.
The previously established empty-Id2 registry miss for `ignited`/`poison` is
reused as an explicit null-result fixture; the registry/string/hash lookup
itself is not re-executed here. Both pinned definitions have category two.
Their other clock-eligibility and visual fields remain selected supplied
inputs, as detailed below.

Every new composition seeds stored speed `3.25` before insertion. With zero
applicable timing modifiers and no force-one override, the actual insertion
overwrites it with `1.0`. This tests the write's precedence over an existing
value; the initial `3.25` is supplied instance state, not a claim about a
particular captured Buff or official server send.

## 2. Effective owner value and stored instance speed are separate state

The original setter `0x1424cfaf0`, notification dispatcher `0x1424cfd00` and
getter `0x1424cfa40` execute for each owner update. One observational listener
reads the newly stored value through the original getter. No synthetic
callback rewrites the Buff. The selected control therefore includes a real
notification delivery, but does not reconstruct every possible subscriber.

| Native key | Pinned Stats Id | Selected calculator qualification |
|---|---|---|
| 8762 | `buff_time_passed_+%` | General additive clock input |
| 535 | `buff_time_passed_+%_other_than_temporal_chains` | Added for these two non-Temporal-Chains definitions |
| 7505 | `virtual_debuff_time_passed_+%` | Included for their category two under the supplied applicability state |
| 7948 | `poison_time_passed_+%` | Included for `poison`, excluded for `ignited` |

After these updates, the actual eligible component tick `0x141e10620` calls
`0x141e10520` and uses the retained speed. It does not call the calculator in
these executions. A separate direct call to `0x141f79160` demonstrates that
the updated provider would yield the new speed, while also leaving stored
`Buff+0x20` unchanged: returning a calculation and publishing it are distinct.

For one poison witness, initial remaining time is three seconds and initial
owner key 8762 is zero. Insertion writes speed one. Updating key 8762 to 100
then notifying the observational listener leaves speed one. With supplied
delta `0.033`, the next tick leaves `2.9670000076293945` seconds. A separate
calculator invocation returns two. This is a conditional receiving-side
contract, **not** a whole-client proof that time scaling never refreshes.

## 3. Retained-instance updates govern subsequent local consumption

The controls next invoke the original `0x141e11720` with a supplied decoded
`0x1c1` partial-update object matching the retained instance key. The actual
holder helper `0x141e092a0` registers its housekeeping observer; the visual
resolver, direct clock writes and observer cleanup execute original code.
Backend remains null, native/script observers are empty, quantity stays zero,
and instance state stays two. This avoids attributing effects to a synthetic
replacement backend or newly created instance.

`0x141e11b89` copies decoded `+0x68` into remaining time `Buff+0x1c`;
`0x141e11b8f` copies decoded `+0x6c` into speed `Buff+0x20`. This path does
not invoke the insertion calculator. With remaining time reset to two seconds:

| Supplied update | Stored speed | Next eligible tick with delta `0.033` |
|---|---:|---:|
| Speed two | 2 | Remaining `1.934000015258789` |
| Speed one | 1 | Remaining `1.9670000076293945` |
| Speed zero | 0 | Remaining exactly `2.0` |
| Speed two, definition `+0x73` nonzero | 2 | Remaining `1.9670000076293945`; tick uses one |

The zero witness starts from a locally floor-limited speed `0.25`, obtained
with key 8762 equal to -90, before the independent partial update writes zero.
Thus the initializer floor is not a universal clamp on received clock speeds.
The already audited decoder defaults omitted speed to one; the speed-one row
tests that decoded value's downstream consequence, **not** a new decoder run.

## 4. Consequences for the client/server boundary

An effective owner stat, an instance's saved clock and the target incoming
damage rate have separate stores and receiving operations. The earlier
[Buff/rate composition](client-buff-rate-independence.md) already established
that the supplied explicit incoming rate can survive instance removal. This
pass adds a narrower timing statement: sending an owner timing key alone is
not sufficient to change the existing clock in this selected subscriber state.

A server reconstruction must account for both updating the target timing
properties and updating/recalculating applicable instance clocks. It must
also keep damage-rate publication separate: a timer speed is not automatically
a DPS multiplier. These are supported design constraints, not a recovered
official server implementation or a claim that every refresh uses `0x1c1`.
The generic local subscription, official send choices and simultaneous-event
ordering remain open. No fixed 33 ms cadence is inferred from a supplied
`0.033` delta.

## 5. Verification and limits

Private support under ignored `Tools/re/out/client-damage-audit/`:

- `r34-buff-clock-authority.py` compares original-instruction results with
  separately written scalar expectations across **243 controls**, including
  both definitions, four timing keys, old/new modifier values, three deltas,
  five transported speeds and the force-one override. The composition visits
  **807 instruction sites / 378,320 interpreted instructions**.
- `r34-buff-clock-fresh.py` freshly reproduces **63 function identities /
  88 byte ranges / 5,705 decoded instruction records**, **29 instruction
  anchors**, four pointer slots and pinned table identities. Only two entries
  are new to the global manifest: observer removal `0x141e0b910` and its
  selected destructor `0x140148090`; these are support helpers, not two new
  mechanics.
- `r34-buff-clock-authority/audit.json`, `verification.json` and
  `fresh-check.json` retain original-byte identities, controls and witnesses.

Instance memory, definition/category fields, loaded-resource state, native
component lookup, available vector capacity, allocation/free and imported
locking are supplied fixtures. Generic observer-array memmove is a support
fixture; actual holder registration/removal and destructor execute original
instructions. The controls explicitly supply nonzero configuration `+0x68`,
selected visual fields and finite nonexpired clocks. They do not prove ordinary
clock eligibility for a live ignite/poison. The initial provider vector and
partial-update object are supplied; initial Entity/Buff decoding, the full
World queue and official traffic are not replayed in this pass.

The scalar comparison is authored in this investigation, not an independent
agent review. Existing clock algorithms, network receiver and empty-backend
selection are reused rather than counted as newly discovered formulas.
The missing generic refresh and numerical DoT publication remain explicit
research work.
