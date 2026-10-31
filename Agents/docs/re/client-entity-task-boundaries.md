# Client Entity task construction and dispatch boundaries (3.29.3b)

The Bow startup zero-time dispatch reaches a **native task list embedded at
Entity `+0x58`**. Both local creation and serialized creation feed that list
through a registry of two factory pointers per type. Selected concrete task
types are now bound to their constructors and dispatch tables. This closes
the list/receiver identity. The [listener supplement](client-entity-task-listener.md)
additionally binds selected `0xc944` AnimationController publications; other
listener paths and ordinary-hit damage/resource consequences remain separate.
The [Ruin listener supplement](client-entity-task-ruin-listener.md) now closes
the selected `0xd9aa` Buff lifecycle/quantity subscriber through its concrete
count effect/audio consumer; no renderer expansion is needed for that boundary.

The input is original `Client/Game.exe`, SHA256
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`,
preferred image base `0x140000000`. Findings are offline original-byte
observations with chained PE-unwind ownership, Capstone 5.0.3 and pefile
2024.8.26. No client execution, new traffic acquisition or server changes
were used. These are native Entity tasks; no binding to a GEAL program is
established by the list or its factory registry.

## Entity ownership and the two creation paths

In the selected startup path `0x1413789e0`, after calling component virtual
`+0x40` with zero delta, `0x141378a2e` forms **Entity `+0x58`** and
`0x141378a35` calls `0x141edf940`, again with zero delta. This proves the
receiver used by that call. It does not prove which tasks an individual
projectile actually has at runtime.

Local factory dispatcher `0x141edf5c0` receives that list wrapper in RCX,
a u16 type key in DX and a type-specific argument pointer in R8. It recovers
the owning Entity by subtracting `0x58` (`0x141edf5de`), obtains the registry
through `0x141643780` and calls lookup `0x141ee0ba0`. Lookup returns the
address of a factory pair, or null. The dispatcher loads **pair `+0`**
at `0x141edf62a`, then calls that factory with:

- RCX: an output owned-pointer location;
- RDX: the owning Entity;
- R8: the original local argument pointer.

After creation, already-marked tasks are destroyed. Otherwise the dispatcher
moves ownership through `0x141edff60` at `0x141edf6b8`. A task flag `0x20`
also invokes Entity `+0x78` receiver virtual `+0xc0` first; that receiver's
meaning is unbound here.

Serialized reader `0x141ee00f0` takes the same wrapper and a packet reader.
It reads a u8 count (`0x141ee0121`), then a raw u16 key per entry
(`0x141ee0158`), with no byte swap in this path. After the same registry
lookup it loads **pair `+8`** at `0x141ee0191` and calls it with the owning
Entity and packet reader. Accepted objects use the same ownership helper
at `0x141ee01f6`. A missing type/factory result follows the exception path
with literal **`Unrecognised task serialised by server; missing registration?`**
at `0x1435b5ec8`; this is not a generic unknown-type skip mechanism.

## Registry storage and bounded registration inventory

Getter `0x141643780` returns registry `0x1445cd8b0`. Exact leaf lookup
`0x141ee0ba0..0x141ee0c22` hashes the low then high byte of the u16 key
using FNV-1a, compares the key at registry node `+0x10`, and returns node
`+0x18`. The local and wire factory pointers therefore reside at **node
`+0x18` and `+0x20`**. Lookup itself does not invoke either pointer.

The complete inspected initializer is the exact PE-unwind range
**`0x1416370f0..0x141639d0f`** (end exclusive). It contains **98** calls
to registry insertion `0x141644310`; each selected entry writes those two
factory fields. The historical
[quest notes](../protocol/12-quests.md#the-gates-animation-what-is-known-paused-2026-10-19-low-priority)
say 139 registrations around this initializer. This pass establishes 98 in
this specific range, and leaves the global count/discrepancy open. It does
not establish that no other initializer registers types.

Stub `0x1400f22f0..0x1400f22fb` writes a null output pointer and returns the
output location. It fills an unavailable side of the pair in the selected
entries below. Numeric type keys must not be interpreted as Stats row IDs:
automatic candidate-stat annotations on disassembly immediates are not
type names or evidence of damage semantics.

| u16 type | Local factory | Serialized factory | Concrete local caller(s) |
|---|---|---|---|
| `0xc944` | `0x14163ffc0` | null-output stub | Actor action-start body `0x141e9a3c0`, key setup `0x141e9aacc`, call `0x141e9aada` |
| `0xd9aa` | `0x141641c10` | null-output stub | `0x1415ccc00`, key setup `0x1415ccc66`, call `0x1415ccc74` |
| `0x1840` | `0x141641290` | null-output stub | `0x141f8d560 → 0x141f8d7f1`; `0x141f8d890 → 0x141f8dd02` |
| `0x2d87` | `0x1416407f0` | `0x141640780` | `0x141eb4160`, key setup `0x141eb42f5`, call `0x141eb4301` |

## Ownership, selection and callback order

Registration `0x141edff60` consumes the owned pointer supplied by its
caller. Task byte `+8` and u16 key `+0xa` control placement and replacement:

| Mask | Observed use |
|---|---|
| `0x01` | Marked task: excluded from callback dispatch and later removed/destroyed. |
| `0x02` | Select task for the tick's temporary callback list. |
| `0x04` | Use the task's u16 type key for keyed insertion; otherwise append with record key zero. |
| `0x08` | On a matching keyed record, replace the previous task; notify its virtual `+0x18`, marking/removing it according to whether dispatch is active. |

The wrapper holds 16-byte records, with key at record `+0` and task pointer
at `+8`. `0x141edf940` first collects nonnull tasks whose byte `+8` has
mask `0x02` set (`0x141edf9d0`). It then sets wrapper `+0x18` to one,
skips tasks with mask `0x01`, and calls each selected task's **virtual
`+8` with the supplied delta** (`0x141edfa0f`). After clearing the dispatch
marker it removes/destroys marked tasks. Keyed replacement and tick
selection are separate decisions; a type's existence in the list does not
imply its tick performs work or damage.

## Selected concrete task consequences

**`0xc944`: a local action-start task with a separate subscription.**
Factory `0x14163ffc0` calls constructor `0x141eb8060`, which stores the
type key at `0x141eb8087`, owning Entity at task `+0x10` and masks
`0x04 | 0x02 | 0x08`. The factory finally installs primary table
`0x14346d200` and secondary table `0x14346d398` at task `+0x18`.
The primary tick slot `+8` is **`0x1400f0f00`, exact `ret 0`**.

That return is **only the tick slot**. The constructor obtains another
object through Entity → `0x14013de80` → result `+0x260` → `0x14013def0`;
it allocates a 16-byte link containing the task's secondary receiver and
the publisher-side container, appends it to that container, and retains
the link at task `+0x80` (`0x141eb810b..0x141eb8149`). Primary removal
slot `+0x18 → 0x141eb81f0` clears this retained link and unregisters it
through `0x142c66120`. The publisher's concrete event invocation and the
secondary callback are now bound in the [listener supplement](client-entity-task-listener.md).
The invoked secondary slot is `+0`, reaching animation-related operations;
this task cannot be called empty or harmless on the basis of its tick return.

**`0xd9aa`: another tick-returning task with a secondary receiver.**
Constructor `0x1416a0690` stores key `0xd9aa`, the owning Entity, masks
`0x04 | 0x02 | 0x08`, primary table `0x143472940`, and secondary table
`0x1434729d0` at task `+0x18`. Its primary tick slot `+8` also points to
the same exact return. Its constructor registers a retained link through
the object returned by `0x1401ebc40`. Its listener event consequences
are not closed by this pass.

**`0x1840`: a concrete countdown, not an empty list entry.**
Factory `0x141641290` calls `0x14205cbe0`, then installs primary table
`0x14347e788`. The selected tick slot `+8 → 0x14171b6c0` subtracts delta
from task float `+0x24` and stores it. A null task `+0x18` pointer or an
exhausted countdown marks mask `0x01` and tail-calls task virtual `+0x18`,
whose selected target is the return stub. This proves countdown/removal
state; neither the field name nor an HP consequence is established.

**`0x2d87`: both local and serialized creation, with timed callbacks.**
Local factory `0x1416407f0` builds through `0x141eb43b0` and installs
table `0x143483dc8`; serialized factory `0x141640780` builds through
`0x14176a010`. The local path can convert supplied integer arguments to
floats by dividing by `1000.0`. Selected tick `+8 → 0x14176a510` adds
delta to task `+0x30`, checks thresholds `+0x28/+0x2c`, invokes
`0x14176a3b0` when a threshold is crossed, and can disable ticking or
mark removal. That helper calls `0x1429452f0` and retains the returned
integer at `+0x38`; removal `+0x18 → 0x14176a590` can pass that identifier
to `0x142949980`. The called subsystem/type semantics remain unbound;
these operations are not evidence of periodic damage or resource loss.

## Research impact and next bounded questions

This list is a real extension point on the native Entity path, but
dispatching it at zero time is not proof of a damage evaluation. It also
has a serialized construction path and owns subscribers whose work may
occur through external events. The original R3/R4 questions about ordinary
hit pre-deduction and damage-relevant callbacks therefore remain open.

Concrete next candidates are:

- Reuse the [bound `0xc944` AnimationController publisher and secondary slot](client-entity-task-listener.md)
  when a damage-relevant trace reaches it. Broader callback/state semantics
  remain separate from the selected animation-related consequences.
- Bind the `0xd9aa` publisher returned by `0x1401ebc40` and its secondary
  table separately; shared virtual offsets do not establish shared types.
- Recover the dynamic type-key sources at callers `0x141fd59c0`,
  `0x141fd5cf0`, `0x141fe3aa0`, `0x141fe3d30` and `0x1420d9560` if a
  concrete damage-relevant trace reaches them.
- Reconcile the historical registration count only with additional exact
  initializer/registration evidence, rather than assuming 139 factories
  were covered by this inspection.

Ignored evidence under `Tools/re/out/client-damage-audit/`:
**`r3-entity-task-boundaries-final`** contains 30 canonical function records,
34 original-byte ranges, 116 direct call references, zero unowned direct
candidates and zero requested pointer occurrences. The independent
**`r3-entity-task-verify.py/json`** checks instruction anchors, selected
table targets, the complete exact initializer's 98 registration pairs,
range digests, null-output stub and exception literal. Earlier
`r3-entity-task-*` directories are exploration, not the canonical batch.
The scans are bounded original-byte direct-call searches; indirect event
publishers, runtime populations and arbitrary alternative paths are not
covered. No global absence claim follows from the selected callback bodies.
