# Client `0xc944` task listener binding (3.29.3b)

The selected action-start task's secondary subscription is now bound to
**AnimationController publication → listener virtual slot `+0` →
`0x141636280`**. Its final table does not turn that callback into a return.
This closes the publisher/slot question left open in
[Entity task boundaries](client-entity-task-boundaries.md), while preserving
the separate unresolved question of ordinary-hit damage/resource effects.

Evidence is offline original `Client/Game.exe`, SHA256
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`,
preferred image base `0x140000000`, using chained PE-unwind ownership,
Capstone 5.0.3 and pefile 2024.8.26. No runtime observation, new capture or
server change was used.

## Component identity and the subscribed list

The constructor chain `0x141eb8060` has positive component-name getters:

| Getter | Verified RIP literal | Result used by the constructor |
|---|---|---|
| `0x14013de80` | `Animated` at `0x1435a4640`, referenced at `0x14013de9e` | Gets the task owner's Animated component; its retained pointer `+0x260` is passed to the next getter. |
| `0x14013def0` | `AnimationController` at `0x143593330`, referenced at `0x14013df0e` | Gets the AnimationController on that retained Entity; constructor forms controller `+0x18` at `0x141eb80f5`. |

Both getters resolve a component-name index and use the supplied Entity's
component vector `+0x10`; this is not identification from names alone.
AnimationController factory `0x141dba060` initializes its observer container
at `+0x18`, including begin/end at controller `+0x20/+0x28`
(`0x141dba093..0x141dba097`). The task constructor's allocated link stores
**task `+0x18`** in link `+0` and the publisher container in link `+8`,
then appends that link to this exact container. Thus its listener receiver
is the task's secondary object, not its primary tick object.

## Publisher to concrete callback

Three inspected AnimationController bodies publish through the same helper:

| Publisher body | Bound call |
|---|---|
| `0x141dba530` | Forms controller `+0x18` at `0x141dba865`; calls `0x141dd3420` at `0x141dba883`. |
| `0x141dbb620` | Forms controller `+0x18` at `0x141dbb832`; calls `0x141dd3420` at `0x141dbb84b`. |
| `0x141dbe890` | Forms controller `+0x18` at `0x141dbeb3f`; calls `0x141dd3420` at `0x141dbeb5d`. |

Each supplies callback thunk **`0x1402fffbc`**. In the helper's link-vector
pass, `0x141dd34c4` loads the link, `0x141dd34d7` loads its listener pointer,
and `0x141dd34da` invokes the supplied thunk. Its exact five bytes decode as:

```text
mov rax, qword ptr [rcx]
jmp qword ptr [rax]
```

Therefore this publication uses **secondary slot `+0`**. Final factory
`0x14163ffc0` installs table `0x14346d398` at task `+0x18`; the table's
first pointer is **`0x141636280`**, a substantial callback body. That body
reads secondary receiver `-8`, which is the previously bound owning Entity
at task `+0x10` (`0x1416362b8`), preserving the receiver adjustment.

The selected table span is **`0x14346d398..0x14346d420`** (end exclusive):
slot `+0` is that callback, and slots `+8` through `+0x80` are the exact
`ret 0` stub `0x1400f0f00`. The next address `0x14346d420` is separately
installed as another table by `0x14163e010` and `0x141641db0`
(`0x14163e06e..0x14163e075`, `0x141641f04..0x141641f0b`). Later nonempty
pointers must not be interpreted as additional slots of this listener.
Neither the primary tick return nor the secondary slots `+8/+0x10/+0x18`
describe the slot actually invoked by this publication.

## Observed consequence and limits

The concrete callback obtains the owning Actor, branches on its state,
updates task fields, and accesses another positively identified component:
getter `0x140cbe5b0` references **`ClientAnimationController`** at
`0x143640368`. The callback uses the returned component `+0x198` and
calls `0x1429259f0` (`0x141636389..0x1416363b9`). Its diagnostic branches
reference **`' is missing some animations for `** and
**`-directional movement!`** at `0x14346db38` and `0x14346db20`.
These are concrete animation-related consequences, rather than evidence
that this listener evaluates numerical hit damage or deducts resources.

This pass binds selected publication sites to the installed callback.
It does not enumerate every AnimationController event, identify every
argument/state field, prove runtime subscription populations, or establish
an ordinary-hit pre-deduction chain. The `0xd9aa` listener remains a
separate candidate. Original R3/R4 damage questions remain open.

Canonical ignored audit **`r3-c944-listener-final`** contains 15 functions
and 15 original-byte ranges. Independent **`r3-c944-listener-verify.py/json`**
checks 52 instruction anchors, all 17 selected table pointers, five literals,
the exact slot-zero thunk/return leaves, and range digests. The earlier
`r3-c944-listener-seeds` directory is superseded exploration.
