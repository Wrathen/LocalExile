# Client Buff clock update boundaries (3.29.3b)

The reviewed network Buffs code initializes a new instance's clock from its
owner's Stats, consumes its stored speed during ticks, and accepts replacement
speed from network updates. A **changed owner stat → recalculation of an
already existing instance's speed** remains unclosed. These are different
paths; a Stats read or an instance callback alone does not connect them.
Three [concrete external backend subscriptions](client-buff-stats-subscriptions.md) now bind actual owner-Stats
notifications to cache/visibility/Render updates. Their selected callbacks do not refresh instance speed;
this positive subscriber evidence remains separate from the unresolved timing-stat connection.

The follow-up [network receiver contract](client-buff-network-clock-contract.md) positively binds the World
dispatch, existing-instance writes and selected Burning Arrow callbacks. Omitted wire speed becomes `1.0`,
then overwrites the old instance speed. This synchronized clock path can be implemented without assuming
an owner-Stats listener; the distinct local-refresh claim below remains bounded rather than a completion
requirement to reconstruct all client components.

This supplement extends [instance clocks](client-dot-routing.md#owner-stats-initialize-concrete-instance-clocks)
and [provider notifications](client-stat-evaluation.md#notification-forwarding-versus-derived-cache-updates).
It uses the same original executable, SHA256
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`,
preferred image base `0x140000000`, and Stats identity recorded in those
reports. Findings are offline original-byte observations, using chained PE
unwind ranges, Capstone 5.0.3 and pefile 2024.8.26. No runtime, traffic or
archive changes were used.

## Concrete update paths

| Entry | Bound behavior and limit |
|---|---|
| Initial Entity Buff deserialization, `0x141e114d0` | Reads the instance data, calls constructor wrapper `0x141e27e30` at `0x141e1160e`, then insertion `0x141e11dc0` at `0x141e11626`. Consequently this initial population also reaches the owner Stats calculation; it does not merely retain the transported speed. |
| New-instance insertion, `0x141e11dc0` | Obtains Stats from `component+8`'s Entity, passes Stats `+0x20` and the new instance to `0x141f79160`, and writes the result to Buff `+0x20`. This occurs before backend creation and insertion notifications. |
| Existing-instance partial network update, `0x141e11720` | Packet `+0x68/+0x6c` becomes Buff remaining duration `+0x1c` and speed `+0x20` at `0x141e11b89/0x141e11b8f`. Backend `+0x10` is subsequently invoked at `0x141e11c6c`. The direct writes use packet values; they do not invoke the insertion calculator. |
| Existing-instance replacement, `0x141e067c0` | Copies replacement Buff `+0x1c/+0x20/+0x24` into the retained instance at `0x141e068e8/0x141e068ee/0x141e068f4`. Backend `+8` runs before replacement and `+0x10` afterwards. These indirect callbacks retain their own scope; the copies are not proof of a Stats-driven recalculation. |
| Main component tick, `0x141e10620` | Iterates Buff pointers at component `+0x160..+0x168` and calls `0x141e10520`. The duration consumer reads stored Buff speed `+0x20`, or uses `1.0f` for the definition's `+0x73` override, and writes remaining duration `+0x1c`. It does not directly recompute speed from current owner Stats. |
| Secondary component tick, `0x141e106a0` | Tests instance removal/duration state and invokes backend `+0x30`; it contains no direct write to instance speed. The backend callback is a distinct boundary, not an automatically empty operation for every Buff type. |

Thus the local owner-dependent initialization applies to both newly received
instances and instances reconstructed during initial Entity state. Neither
path demonstrates that a later independent Stats update reinitializes an
existing Buff. Similarly, direct network speed replacement demonstrates an
update mechanism, not that all clock changes originate on the server.

## The concrete component does not supply a missing listener binding

Factory `0x141e0fc40` installs Buffs primary table `0x14359f5f8`, its
embedded interface table `0x14359f1a0` at object `+0x18`, and initializes the
instance vector. Its allocation helper `0x141e26cb0` is pool allocation.
The inspected factory/allocation bodies do not resolve Stats or register a
Stats callback. Primary slots `+8..+0x38`, `+0x68..+0x78` and `+0x90`
are the exact `ret 0` leaf `0x1400f0f00`. The bound initial deserializer is
`+0x88`; the tick slots are `+0x40/+0x48`. These concrete slots cannot be
filled with an assumed notification handler.

This is a bounded factory/table observation. An external initializer, another
component or a generic subscription could still retain a reference to Buffs;
their absence is not established by this table.

Two other positive update paths must also be kept separate from speed:

- `+0xb0 → 0x141e10810` does read owner Stats through `0x14013de10` and
  its `+0x20` provider. It derives a key from definition `+0x7e`, subtracts
  that value from the instance's signed 16-bit quantity at `+0x40`, limits
  the requested change, writes that same quantity and emits an event through
  component `+0x18`. It does not write Buff speed `+0x20` or call
  `0x141f79160`. The exact key/quantity semantics are not renamed here.
- `+0xc8 → 0x141e0fbc0`, exact seven-byte leaf, writes the supplied byte
  to component `+0x17a`. The duration tick checks this byte under definition
  `+0xfc` at `0x141e105a8..0x141e105b8`. This is a conditional advancement
  guard, not recalculation of stored speed. The follow-up
  [Life-to-Buffs producer audit](client-buff-clock-producers.md) binds selected
  initialization and transition callers; this still does not recalculate speed.

## A selected instance observer is reference housekeeping

The existing-instance handler obtains a temporary reference holder through
`0x141e092a0` at `0x141e117bc`. This function stores the Buff pointer in
the holder and registers a small callback record in **that Buff's `+0x150`
collection**. At `0x141e09340..0x141e0935e` the record receives table
`0x142f9cd70`, the holder pointer and function pointer `0x141e05e30`.
This is not a subscription to the owner's Stats notification list.

The record's invoke slot `+0x18 → 0x140145180` forwards its retained holder
to the retained function. The exact thunk
`[0x141e05e30,0x141e05e35)` jumps to `0x141e091c0`. That function calls
`0x141e091e0` to remove the matching record, returns the formerly held Buff
pointer, and clears the holder. Its complete inspected consequence is
reference/observer housekeeping, without a speed or damage-rate write.
The replacement updater dispatches this collection through `0x14019a240`
before copying fields; that dispatcher invokes each record's `+0x18`.
Other records in the collection retain their own unresolved consequences.

The actual network-provider notification dispatcher `0x1424cfd00` instead
iterates its own listener vector and invokes each retained interface's slot
zero. Its existence, documented in the provider report, does not identify
this Buff holder or any Buff clock callback as one of its subscribers.

## Evidence and remaining proof

Ignored original-byte records under `Tools/re/out/client-damage-audit/` are
`r4-buff-clock-update-discovery`, `r4-buff-clock-update-callbacks` and
`r4-buff-clock-observer-dispatch`, with table bytes in
`r4-buff-clock-tables.json` and independent rechecks in
`r4-buff-clock-updates-verify.py/json`. Existing insertion, wire-update and
tick records remain in the linked reports. The independent recheck validates
71 instruction anchors, 18 selected table pointers and four exact leaf bodies,
plus all 28 recorded ranges across the 18 function records in these three
new audit batches.

The discovery scan found the already known insertion call to the calculator,
the network call to the replacement updater, and the named poison/bleeding
clock reads. A second bleeding-key candidate is a native stat registration
in `0x1400d8360`, not an instance-speed writer. This direct-reference scan
does not cover indirect/dynamic-key/unwind-less/inlined alternatives and is
not an absence proof.

To close dynamic owner-stat refresh, bind an actual subscription or other
update producer to an existing Buff, retain its concrete callback/receiver,
then show the selected timing-stat change causing a recalculation and speed
write. Backend callbacks, other instance observers and external component
initializers remain candidates. None of the paths here establishes an
instance's contribution to native incoming DoT rates, or equates expiration
speed with damage rate.
