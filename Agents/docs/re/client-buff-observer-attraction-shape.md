# Concrete Buff observer with no-op callbacks (3.29.3b)

## Result and scope

The positive subscription candidate `0x14205bea0` is now bound to an actual
constructed child object. Its secondary Buff-listener table has **no-op gain
and loss callbacks**: both slots point to the original three-byte
`ret 0` leaf at `0x1400f0f00`. This subscription therefore does not publish a
damage amount, source record or owner-clock change through those callbacks.

The same concrete child belongs to a task whose update writes a transform
matrix and publishes it under `attractionShape_matrix_CTRLs`. That is a
positive presentation output. The Buff callbacks themselves do not even
update that presentation state; they return immediately.

This continues the [Buff lifecycle observer contract](client-buff-lifecycle-scripts.md#native-observers-retain-instance-identity).
It closes one candidate, not every subscriber, component or script program.
The [DoT source-publication obligation](client-dot-source-publication.md)
remains open.

All addresses are preferred VAs for image base `0x140000000`, original 3.29.3b
executable SHA-256
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`.
The evidence is static original executable code and data. No target execution,
attachment, new capture, server change or CI operation was performed.

## Binding the actual receiver

The parent constructor `0x141716d50` installs table `0x14347eb30` at
`0x141716d7a/0x141716d81` and calls child creator `0x141717380` at
`0x141716df6`. The actual installed table binds:

| Parent slot | Concrete method |
|---|---|
| `+8` | Tick `0x141717260` |
| `+0x48` | Per-child update `0x141717820` |

Child creator `0x141717380` places the child at shared allocation `+0x10`
(`0x141717494`). It installs primary table `0x14347edc8` at child `+0`,
and **secondary table `0x14347e968` at child `+8`**, through
`0x1417174b5..0x1417174c6`. The stores are repeated at
`0x1417174f3/0x1417174f6`. The entity key is stored at child `+0x10`
(`0x1417174fa`); the retained collection entries hold the actual child pointer
at entry `+8`, with stride `0x18`.

The parent tick reads that entry `+8` at `0x141717350`. If child `+0x18` has
no resolved Entity, it passes the actual child to `0x14205bea0` at
`0x14171735e`. This is the concrete constructor-to-subscription chain; nearby
base-class tables are not used to infer the derived listener's behavior.

The subscription helper obtains the resolved Entity's actual Buffs component
through `0x1401ebc40` at `0x14205bfbe`. Its link records retain:

| Link field | Original construction |
|---|---|
| Receiver | Child `+8`, `0x14205c00e/0x14205c012` |
| Publisher | Buffs `+0x18`, `0x14205c015/0x14205c01d` |
| Child's retained link | Child `+0x20`, `0x14205c05a` |

## Both delivered Buff callbacks return immediately

The already established publisher passes the listener receiver in RCX, the
Buffs component in RDX and the actual Buff in R8. Its insertion/removal thunks
dispatch through listener table `+8/+0x10`, respectively.

For the actual installed secondary table:

| Table address | Value | Meaning |
|---|---|---|
| `0x14347e968` | `0x141720804` | Separate adjustment thunk; original bytes subtract eight from RCX and jump to `0x141715d70` |
| `0x14347e970` (`+8`) | `0x1400f0f00` | Gain callback: `c2 00 00`, `ret 0` |
| `0x14347e978` (`+0x10`) | `0x1400f0f00` | Loss callback: the same immediate return |

Neither delivered callback reads the Buff or writes any state. The first
slot's adjustment thunk must not be mistaken for a gain/loss implementation.
Subscription presence alone is consequently not evidence of numerical
publication or instance-clock ownership.

## Independently bound presentation output

The parent tick conditionally invokes its actual `+0x48` method at `0x14171732d`
for retained children. Parent `+0x168` skips that matrix-update loop, and the
startup byte `0x1445b38cd` is tested at `0x1417172b1`; a nonzero value jumps to
`0x14171733e` after the loop. The previously bound ordinary startup value is
one, so this matrix-update path is skipped under that guard. Its presence is
not an observed ordinary network-client update.
Method `0x141717820` reads parent `+0x158`, interpolates child
state and writes four matrix rows to child `+0x80/+0x90/+0xa0/+0xb0` at
`0x141717981..0x141717996`.

When the required child resource and resolved Entity are present, it constructs
a string view at `0x141717a3e/0x141717a45` from the original ASCII literal
`0x143472cb0`, length `0x1c`: **`attractionShape_matrix_CTRLs`**. It passes
the resolved child Entity, that string view and the matrix to `0x14171d690`
at `0x141717a62`. This named transform output establishes why its task-local
interpolation state cannot simply be relabelled a Buff duration or damage
clock. No deeper rendering traversal is needed for this classification.

## Verification and remaining boundary

Private verifier `r14-buff-observer-verify.py` reports **PASS: six functions,
17 original unwind ranges, 808 decoded records, 37 exact instruction anchors,
nine table slots, four explicit leaves and the 28-byte property name**.
Complete unwind ranges are retained with hashes; decoded-record counts may
include embedded non-code data and are not a reachable-instruction census.
Independent review additionally checked 34 anchors, five RIP bindings and
nine startup/parent gate controls. Its guard qualification is reflected above.

This result excludes the concrete gain/loss callbacks above as a missing DoT
publisher. It does not exclude other callbacks, later table replacement,
other child classes, lifecycle programs, or another component's numerical
implementation. A future numerical claim still needs an actual instance/source,
amount, destination, update and removal chain.
