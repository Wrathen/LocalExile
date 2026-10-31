# Buff backend Stats subscriptions and the clock-refresh boundary (3.29.3b)

## Result and scope

Three concrete Buff backends subscribe to the owner's network Stats provider.
Their selected callbacks update a backend cache, rebuild/clear Buff visuals, or
change a target Render flag. None of these callbacks establishes the missing
owner timing-stat change → existing Buff clock recalculation connection.

This is a positive subscription and receiver audit, **not** proof that the client
has no such connection elsewhere. The existing distinction between insertion-time
clock calculation and subsequent network clock updates remains unchanged; see
[clock updates](client-buff-clock-updates.md) and
[the network clock contract](client-buff-network-clock-contract.md).

Static evidence only: no client execution, attachment, traffic capture, server
change or CI operation. Addresses are preferred VAs for image base `0x140000000`.

| Artifact | SHA-256 |
|---|---|
| `Client/Game.exe`, 3.29.3b | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `data/tables/Stats.json` | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |

## The concrete subscription contract

The ordinary Stats component constructor `0x1421c2ad0` installs table
`0x1435dd4e8` at `0x1421c2b28`. Its `+0xb0` slot is `0x1421c3100`.
That method takes the network provider retained at component `+0x40`, adds eight
to reach its listener publisher, and calls `0x14012c3e0`. The helper allocates a
16-byte subscription record containing the listener pointer at record `+0` and
publisher pointer at `+8`, then appends that record to the publisher's list.
The returned token is retained by each backend below.

This binds the calls to the real Stats provider, rather than inferring a Stats
subscription from an arbitrary virtual slot number. Provider change delivery is
covered in [stat evaluation](client-stat-evaluation.md): the setter and inherited
change-forwarding path reach `0x1424cfd00`, which invokes listener slot zero.
That dispatcher alone does not identify a subscriber's consequences.

| Backend | Factory / construction | Installed Stats listener / token |
|---|---|---|
| `AvatarOfFireCycleBuff` | Registration `0x1415ce3e0` uses literal `0x1434634d8` and factory table `0x143464be8`; factory `0x1415ce380` allocates 0x38 bytes and calls `0x1415c8e10` | Table `0x1434651f0` at backend `+0x18`; slot zero `0x1415c9250`; subscribe `0x1415c8ee8`; token at backend `+0x20` |
| `ArchnemesisModBuff` | Registration `0x1415cc920` uses literal `0x143463270` and factory table `0x143464e00`; factory `0x1415cc8c0` allocates 0x70 bytes and calls `0x1415c6300` | Table `0x143465270` at backend `+0x10`; slot zero `0x1415c66d0`; subscribe `0x1415c638d`; token at backend `+0x60` |
| `TetheredDebuff` | Registry `0x1415be830` installs factory table `0x143465038` at `0x1415c0dbf`, binds literal `0x143462ba0` at `0x1415c0de9`, inserts at `0x1415c0e05`; factory `0x1415d04a0` calls `0x1415d2d40` to allocate/construct 0xa0 bytes | Table `0x1434876b8` at backend `+0x58`; slot zero `0x1417a51c0`; subscribe `0x1415d2e18`; token at backend `+0x90` |

All three retain the incoming Buff pointer at backend `+8`. Offsets in this
backend object must not be confused with offsets in the retained Buff instance:
for example, the Avatar backend's `+0x20` is a subscription token, whereas the
actual Buff's `+0x20` is its stored clock speed.

## Selected callback consequences

### AvatarOfFireCycleBuff: one cached stat flag

`0x1415c9250` accepts only native key 7250,
`avatar_of_fire_rotation_active` (`0x1415c9250..0x1415c9257`). It reads that
key from the notifying provider through slot zero, converts the result to a
Boolean and writes listener `+0x10` at `0x1415c9274`. Because this listener is
backend `+0x18`, the destination is **backend `+0x28`**.
The constructor initializes the same cache at `0x1415c8f45`.
The callback returns without reading the retained Buff pointer or writing its
clock. This callback is completely bounded by its key gate, provider getter,
cache write and return.

The backend also subscribes to Buffs events through a separate listener at
`+0x10`. Callback `0x1415c9220` checks Buff-definition key 1013 and writes backend
`+0x29`; backend tick `0x1415c93e0` consumes/clears that flag and calls
`0x1415c9280` twice. That continuation checks cached `+0x28`, reads art-selection
key 17206, and supplies selected string/resource arguments to `0x141dc56d0`.
This separate event/tick path does not turn the Stats cache write into a clock
recalculation. Nested resource internals are outside this supplement.

### ArchnemesisModBuff: Buff-visual visibility state

`0x1415c66d0` accepts keys 22992 (`virtual_is_hidden_monster`), 10892
(`hide_buff_visuals`) and 4624 (`buff_visuals_show_while_hidden_override`).
The provider's slot `+8` is a nonzero-value query: the installed network wrapper
implementation `0x1424cfad0` calls its numeric getter then performs `test/setne`.
It is not an explicit-key-presence query.

The callback recomputes:

```text
hidden = (virtual_is_hidden_monster && !buff_visuals_show_while_hidden_override)
         || hide_buff_visuals
```

If this differs from listener `+0x58`, it writes that byte at `0x1415c6738`.
The listener is backend `+0x10`, so the cached byte is **backend `+0x68`**.
It then adjusts the receiver back to the backend and enters `0x1415c7670` when
hidden, or calls `0x1415c74c0` when visible. The selected continuations traverse
backend-owned effect/resource collections to clear or recreate visuals. Their
nested resource operations are not exhaustively classified here. The selected
Stats callback neither accesses the retained Buff's time fields nor calls the
clock calculator.

### TetheredDebuff: target Render state

`0x1417a51c0` accepts only key 22992 and reads its numeric value from the notifying
provider (`0x1417a51f3`). It visits the collection beginning at listener `+8/+0x10`
(**backend `+0x60/+0x68`**), with 0x20-byte entries and two object handles per
entry. For eligible handles it obtains the target Entity, calls named getter
`0x14013fa90` (its original literal is ASCII `Render` at `0x14346a1c4`), compares
and conditionally writes **Render `+0x309`**, then calls `0x1415f40e0` on that
Render receiver. The nonzero and zero branches set or clear this same flag.

The observed consequence is a concrete Render-state update. It is not a write to
Buff `+0x20`, even though generic handle and component lookup helpers occur along
the path. The Render refresh implementation is not recursively expanded here.

## What remains open

The insertion path already calculates clock speed from owner Stats and writes
actual Buff `+0x20`; network partial updates can overwrite that speed explicitly.
These three subscriptions add concrete subscriber identities and consequences,
but do **not** close a path from a change in `buff_time_passed_+%`,
`virtual_debuff_time_passed_+%`, poison/bleed expiration speed or related timing
keys to recalculating an existing Buff instance's stored speed.

No global subscriber enumeration or whole-program absence is claimed. Inlined
lookups, other backends, dynamically selected callbacks and server-supplied clock
updates remain separate possibilities. In particular, the absence of timing keys
from these three callback gates cannot establish that game timing is snapshot-only.

## Retained checks

Ignored evidence is under `Tools/re/out/client-damage-audit/`.
`r10-buff-refresh-verify.py` retains complete original-PE disassemblies and
per-range hashes in `r10-buff-refresh-external-listener/`, with result
`verification.json`: **PASS**, 22 functions / 22 ranges / 4,276 instructions,
eight pointer bindings, four exact literals and four native-stat identities.
The range audit includes registrations, constructors, subscription helpers,
selected callbacks, first continuations and the named Render getter.

The verifier checks the pinned executable and Stats digests, original instruction
bytes, table targets and literal bytes. Its zero direct clock-calculator edges
applies only to the three selected Stats callbacks; it is not a transitive
no-clock theorem, runtime trace or REA retained Evidence ID.
