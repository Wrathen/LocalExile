# Stat-driven preload rows and the .epk collector (3.29.3b)

## Result and scope

The metadata row whose mode at `+0x50` feeds `0x141e52c60` is positively
bound to **`Data/PreloadFromStats.dat`**. The previously unresolved global
`0x1444998f8`, called by `0x141e518f0` through table `+0x10`, is a callable
wrapper. A concrete installation routine gives it a handler that checks
UTF-16 paths for the `.epk` suffix, resolves a resource and appends a retained
handle to the caller's qword vector.

This closes a resource-dependency candidate. The selected boundary collects
resource handles; it does not provide a numerical Hit amount, an actual
defender, a resistance calculation or a resource deduction. Package internals
and rendering traversal are outside this report.

All evidence is static original executable code and data, preferred image
base `0x140000000`, client 3.29.3b, executable SHA256
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`.
No client execution, attachment, new capture, server operation or CI was used.

## 1. The producer is PreloadFromStats

The selective entry `0x141e53070` constructs a 25-code-unit view of the
original UTF-16 literal **`Data/PreloadFromStats.dat`** at `0x1435a3978`
(`0x141e530ee/0x141e530f9`) and obtains its resource through `0x141e64010`
at `0x141e5311f`.

The adjacent path obtains **`Data/Stats.dat`** from literal `0x14317e9c8`
and constructs the requested native stat's table reference from key minus one
at `0x141e53168..0x141e53179`. It uses that reference in the preload
resource's lookup at `0x141e53188..0x141e53194`. The retained entry row at
entry `+0x10` is copied into the descriptor passed to `0x141e52c60` at
`0x141e53236`.

The full-table entry `0x141e535a0` obtains the same named resource at
`0x141e53632..0x141e53663`. Its row walk advances by `0x64` at
`0x141e537c3`, checks the corresponding stat through the supplied stat
interface, and passes eligible rows to `0x141e52c60` at `0x141e537be`.

That callee reads the retained row's mode at `+0x50`
(`0x141e52c7f/0x141e52c82`). This is a field of the stat-driven preload
row, not evidence for a damage-mode enum. Its individual enum labels are
not recovered here. Existing mode/guard branches control which resource
helpers are used; they are not newly interpreted as client/server authority.

## 2. Bind the callable before classifying its virtual call

The original executable data contains:

| Location | Original value |
|---|---|
| `0x1444998f8` | `0x1444998c0` |
| Inline object `0x1444998c0`, table | `0x1435a5388` |
| Default table `+0x10` | `0x140125860`: `mov al,1; ret` |

The pointer at `0x1444998f8` is the inline wrapper's `+0x38` field.
The default call returns true and collects nothing. It must not be assumed
to remain the selected implementation.

Installation function `0x141599660` constructs a different inline callable:
it writes table `0x14345e998` at stack object `+0`
(`0x141599795/0x14159979c`), points its `+0x38` field to itself, and passes
that wrapper and `0x1444998c0` to swap helper `0x140133ff0` at
`0x1415997bf`.

The original helper handles inline storage through table `+8` copy/move
leaves. Default leaf `0x141e646e0` installs `0x1435a5388`; incoming leaf
`0x1415bae70` installs `0x14345e998`. Bounded original-instruction replay
of this exact inline/inline input leaves the global pointer referring to
`0x1444998c0`, now with **table `0x14345e998`**. Its `+0x10` slot is
**`0x1415bad30`**.

This is a static installation contract, not a live observation of which
handler is currently installed. The pattern explains why scanning only
direct writes to `0x1444998f8` misses installation: the generic helper writes
the wrapper field indirectly. Later replacement remains possible.

## 3. The installed call collects .epk handles

Handler `0x1415bad30` receives the destination vector in RDX and a pointer
to the UTF-16 path pointer in R8. It:

1. Counts the path's code units and rejects lengths below four.
2. Compares its final four code units with original literal **`.epk`** at
   `0x14345fe90`, through `0x142d7e060` at `0x1415bad7d`.
3. For an accepted nonempty path, obtains the resource-manager entry and
   calls resolver `0x1412d5a90` at `0x1415badcf`.
4. Retains the resulting resource and appends its pointer to the vector:
   direct store `0x1415bae03`, end increment by eight at `0x1415bae10`, or
   capacity-growth helper `0x141191b60` at `0x1415bae22`.

The append contains a resource pointer with reference-count handling, not
a scalar damage result. The handler's boolean return reports acceptance;
the selected caller does not turn it into a damage amount.

In `0x141e52c60`, preload row `+0x40/+0x48` supplies a counted array of
16-byte references. Its loop calls `0x141e518f0` at `0x141e52d56`.
That helper passes the subordinate row's nonempty UTF-16 path fields
`+8` and `+0x2d` to the same wrapper `+0x10`
(`0x141e51933/0x141e51964`), together with the destination vector.
The caller-to-installed-handler chain therefore explains these fields as
resource dependencies at this boundary. Their full subordinate table schema
and package contents are not inferred from the offsets.

## Verification and remaining boundary

Ignored verifier `Tools/re/out/client-damage-audit/r15-resource-service-verify.py`
retains `r15-resource-service/selected-audit.json` and `verification.json`.
It checks ten original functions, 12 unwind ranges, 1,694 decoded records,
four explicit leaf ranges, 77 instruction anchors, 13 RIP bindings,
six original pointer/table slots and three UTF-16 literals. Three bounded
original-instruction swap controls independently establish installation
for distinct stack locations. No native code or allocator was executed.

This report stops at the positive preload and dependency-retention category.
It does not close the separate numerical Hit consumer, DoT source magnitude
publication or authoritative actor-resource application. Those require an
actual source/instance, amount, target and application chain.
