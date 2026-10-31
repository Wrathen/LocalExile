# Buff lifecycle scripts and selected native predicates (3.29.3b)

## Result and scope

The actual Buff lifecycle has two different delivery contracts. Native observers
receive the actual Buff pointer. The selected gain/loss script calls instead
receive the owning Entity's embedded script interface and a null optional second
object; they select their program by a generic event or the Buff's definition
key. An individual Buff pointer, its source identity and damage amount are not
passed as those script-call arguments.

The selected native GEAL `IfBuff` and `HasBuff` implementations positively bind
to a **Buff-presence query**, not an incoming damage-rate writer. This closes
their local result category without claiming that all GEAL, all Buff subscribers,
or every lifecycle program is non-numerical.

This continues [empty-key backend selection](client-buff-default-backend.md) and
the [DoT source-publication boundary](client-dot-source-publication.md).
All addresses are preferred VAs for image base `0x140000000`, original 3.29.3b
executable SHA-256
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`.
The evidence is static original code and five previously pinned resource files;
no client execution, attachment, new capture, server change or CI operation.

## Native observers retain instance identity

Insertion `0x141e11dc0` publishes through `0x141690920` at `0x141e11ee5`;
removal `0x141e11fb0` does so at `0x141e121b2`. Their supplied six-byte thunks
are `0x1401d8280` and `0x1401d81f4`, respectively. Original bytes decode to
`mov rax,[rcx]; jmp [rax+8]` and `mov rax,[rcx]; jmp [rax+0x10]`.

The publisher retains the supplied component and Buff arguments, visits its
direct-listener and subscription-link collections, and invokes the thunk with:

| Argument | Delivered value |
|---|---|
| RCX | Actual listener receiver, dereferenced from the subscription link when applicable |
| RDX | Buffs component |
| R8 | Actual inserted/removed Buff |

The argument moves and calls are visible at
`0x141690980/0x141690983/0x141690986` and
`0x1416909bd/0x1416909c0/0x1416909c3/0x1416909c6`.
Thus this observer route preserves an instance pointer that a hypothetical
numerical subscriber could inspect. Publisher traversal alone does not identify
such a subscriber. The already bound
[AvatarOfFireCycleBuff listener](client-buff-stats-subscriptions.md#avataroffirecyclebuff-one-cached-stat-flag)
has a separate, established cache/art consequence; that result is not generalized
to other listeners.

## The script programs are component configuration

Configuration constructor `0x141e25320` installs table `0x14359f338`.
Its `+8` slot is component factory `0x141e0fc40`; its `+0x18` slot is property
parser `0x141e0fe20`. The factory retains its incoming configuration pointer at
**Buffs component `+0x158`** (`0x141e0fcc4`). It is not a pointer to an actual
Buff instance or a numerical damage-source record.

The parser recognizes ASCII prefixes by original eight-byte comparisons:
`on_gain_` at `0x141e0fe6b` and `on_lose_` at `0x141e100cf`.
The exact suffix `buff` is compared against literal `0x14359cd80`.
Other suffixes are resolved against `BuffDefinitions`, then converted from a
row pointer to a one-based native definition key.

| Property form | Configuration destination | Compilation/insertion sites |
|---|---|---|
| `on_gain_buff` | Program list `+0x10` | `0x141e0feeb → 0x141e0fef2` |
| `on_lose_buff` | Program list `+0x28` | `0x141e1014f → 0x141e10156` |
| `on_gain_<definition Id>` | Definition-keyed map `+0x40`, node's program list `+0x28` | Key increment `0x141e10047`; map helper `0x141e1005a`; program append `0x141e10065` |
| `on_lose_<definition Id>` | Definition-keyed map `+0x50`, node's program list `+0x28` | Key increment `0x141e102ab`; map helper `0x141e102be`; program append `0x141e102c9` |

Map helper `0x141e26f50` stores the supplied integer at node `+0x20`, initializes
the node's program vector and returns node `+0x28`.
Program appender `0x141de5e20` appends a 0x60-byte program record only when the
input contains code; this is program construction, not stat contribution.

## Actual script invocation supplies the owner, not the Buff argument

Insertion first invokes configuration `+0x10` at `0x141e11f00`, then uses the
inserted Buff's definition key to select map `+0x40` and conditionally invokes
its program at `0x141e11f71`. Removal uses configuration `+0x28` at
`0x141e121cd`, then the removed Buff's definition key in map `+0x50` at
`0x141e12234`.

At all four `0x141de6000` calls, RDX is **owner Entity `+0x28`**, and R8 is
explicitly zero. The generic invoker constructs an evaluation context, moves
RDX to R8 and the optional R8 to R9 at `0x141de609a/0x141de609d`, then calls
`0x141de5ec0`. That iterator eventually dispatches each selected 0x60-byte
program record through `0x141df49e0`.

The direct contract therefore supplies an owner interface and definition-based
program selection. It does not carry the actual Buff as the optional second
object, nor a scalar damage magnitude. This does not exclude indirect reads
from the owner, inherited evaluation state or arbitrary accepted program code.
It prevents treating the generic dispatch itself as a recovered
instance-source/amount publication operation.

## Positively bound `IfBuff` and `HasBuff` outputs

Native registry entries at `0x14449a6c0/0x14449a6d0` bind `IfBuff` and `HasBuff`
to factories `0x14207d7f0/0x14207d940`. Their literal names are at
`0x1435af058/0x1435af088`; both factories resolve a Buff definition and retain
its native key in their callable payload.

| Callable | Installed invoke slot | Concrete consequence |
|---|---|---|
| `IfBuff` | Table `0x1435d3720`, `+0x10 → 0x1420dc880` | Resolves the selected Entity, gets named `Buffs`, calls presence helper `0x141e113d0`, selects a branch, then jumps to `0x141de6360` |
| `HasBuff` | Table `0x1435d3c40`, `+0x10 → 0x1420ec830` | Adjusts its payload by eight and calls `0x1420bc8a0`; that method gets named `Buffs`, calls the same presence helper and boxes its Boolean as GEAL type `0x0a` |

Named getter `0x1401ebc40` has the literal `Buffs` at `0x14359cdc8` and reads
the resolved component from the Entity's component vector.
Presence helper `0x141e113d0` scans Buffs `+0x160..+0x168`, skips actual Buffs
whose `+0xd0` has removed bit `0x10`, and compares the definition's native key.
Its result is the Boolean existence of a matching retained instance
(`0x141e11451/0x141e114b5`). Its cleanup removes a temporary lifetime handle.
The helper does not test the instance's remaining duration, quantity or damage
rate. The missing-component error paths and `IfBuff`'s selected branch programs
remain separate from this presence result.

These predicates can affect program control flow. Their existence-query result
is not a source rate, stacking-selection algorithm or native stat write.

## Selected resource programs and remaining obligation

The five previously extracted files in the selected parent-first source chains
were reread against their retained hashes: `Character.ot`, `Dex.ot`, `Int.ot`,
`Monster.ot`, and `SandSpitter.ot`. `Character.ot` and `Monster.ot` declare empty
`Buffs` blocks; the three child files add no Buffs block or `on_gain_`/`on_lose_`
field. These selected chains therefore contribute no lifecycle program through
the configuration fields above. No new archive-wide resource scan was performed.
Other object types, overrides and configuration mutation are not covered.

A Buff-specific source identity, amount and destination publication into native
incoming damage rates remains unrecovered. Further evidence must bind an actual
observer or configured program to that numerical operation, including its update
and removal behavior. The now-typed script configuration, owner context and
presence predicates do not satisfy that obligation by themselves.

Private verifier `r13-buff-publication-verify.py` reports **PASS: 20 functions,
20 original ranges, 1,844 instructions, 64 exact anchors, four table slots,
two explicit thunk leaves and five pinned resource files**. It does not execute
target code or claim complete subscriber/program enumeration.
