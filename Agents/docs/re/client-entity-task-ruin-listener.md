# Client `0xd9aa` task: Buff lifecycle to Ruin presentation (3.29.3b)

The selected task's external subscription is **Buffs lifecycle/quantity
publication → secondary callback → aggregate Ruin count → count-specific
effect/audio assets**. This is a concrete Buff count reader and presentation
consumer. It is not the client damage calculator, and its count is not an
incoming DoT-rate pool. This closes the `0xd9aa` listener question in
[Entity task boundaries](client-entity-task-boundaries.md) at the level
needed to separate combat calculation from client presentation.

All code evidence is offline original `Client/Game.exe`, SHA256
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`,
preferred image base `0x140000000`, with chained PE-unwind ownership,
Capstone 5.0.3 and pefile 2024.8.26. Selected data rows come from the
supplied `BuffDefinitions.json`, SHA256
`3a23c6fdf7ba29fb8508e5c9560a44e5292345e45dd2f886ef7126b8427fd659`.
No runtime observation, new capture or server change was used.

## A positive Buffs subscription

Getter `0x1401ebc40` references literal **`Buffs`** at `0x14359cdc8`
(`0x1401ebc5e`) and resolves its component-name index against the supplied
Entity's component vector. Task constructor `0x1416a0690` calls this
getter on its owning Entity, forms **Buffs `+0x18`** at `0x1416a071d`,
allocates a subscription link, and stores task **`+0x18`** as its listener
receiver. It appends the link to that container and retains it at task
`+0x38` (`0x1416a0733..0x1416a076f`). Destructor `0x1416a0bf0` removes
the retained link through `0x142c66120`.

Buffs factory `0x141e0fc40` initializes this same observer container's
begin/end at component `+0x20/+0x28`. The selected task installs secondary
table **`0x1434729d0`**. Its relevant slots are:

| Secondary slot | Target | Concrete use below |
|---|---|---|
| `+8` | `0x1416a0f30` | Buff insertion notification |
| `+0x10` | `0x1416a0fa0` | Buff removal notification |
| `+0x28` | `0x1416a0f30` | Quantity-reduction notification |

These are distinct from the primary task's returning tick slot. The
selected table span is `0x1434729d0..0x143472a08` (end exclusive): seven
pointers, with `+0x18/+0x20/+0x30` returning directly and slot zero an
adjusting destructor thunk. The next table at `0x143472a08` has its own
installer `0x141643f30` (`0x141643fa8`), so later pointers are not added
to this listener's contract.

## Three publishers and their callback ordering

| Producer | State and notification |
|---|---|
| Insertion `0x141e11dc0` | After insertion, forms Buffs `+0x18` at `0x141e11ed3` and calls `0x141690920` at `0x141e11ee5`, supplying the component, inserted Buff and thunk `0x1401d8280`. The thunk invokes listener `+8`. |
| Removal `0x141e11fb0` | Removes the pointer from the instance vector and marks Buff `+0xd0` with `0x10` before forming the observer container at `0x141e121a1`. Its `0x141690920` call supplies thunk `0x1401d81f4`, which invokes listener `+0x10`. Thus the count reader below sees remaining eligible instances. |
| Quantity reducer `0x141e10810` | Reads signed16 quantity `Buff+0x40`, uses its definition/owner-Stats-dependent bound, then either zeroes that field (`0x141e108a4`) or subtracts from it (`0x141e108b3`). Both branches publish through `0x141d6c1c0` with thunk `0x140cea0c4`, invoking listener `+0x28` after the store. |

The two dispatch helpers' link-vector passes load the stored listener
pointer before invoking their supplied thunk (`0x1416909c3..0x1416909c6`,
`0x141d6c266..0x141d6c269`). The exact six-byte thunks perform the
corresponding virtual `+8/+0x10/+0x28` jumps. Receiver adjustment inside
the selected callbacks subtracts `0x18` to recover the primary task.

The quantity reducer is an actual **provider-side Buff state mutation**,
not merely a repaint. The subscribed task is the separate downstream
consumer. Neither fact makes every Buff operation a presentation operation;
the provider's other backends and modifier-container work have their own
contracts.

## What the consumer calculates

Both callbacks filter definition keys **1783, 1784, 2633, 2715**. The
definition-key helper `0x142454bd0` calculates the record index using its
314-byte row stride and adds one; selected extracted rows are:

| Native key | JSON row | `BuffDefinitions.Id` |
|---:|---:|---|
| 1783 | 1782 | `ultimatum_ruin` |
| 1784 | 1783 | `supporter_map_ruin` |
| 2633 | 2632 | `dark_pact_ruin` |
| 2715 | 2714 | `gauntlet_ruin` |

Aggregate helper **`0x141f793f0`** iterates Buffs `+0x160..+0x168`, skips
instances marked with `Buff+0xd0 & 0x10`, filters those same four
definition keys, and **sums their signed16 `Buff+0x40` quantities**
(`0x141f79486..0x141f7948a`). This is an observed numerical count
calculation; it neither reads a DoT DPS channel nor sums damage rates.

The insertion/quantity callback `0x1416a0f30` calls refresh
`0x1416a10f0` for a matching definition. Refresh obtains the aggregate,
uses at least one for its selected display count, and reconciles task
`+0x20` by adding/removing count-specific resources. Its bound path format
is **`Metadata/Effects/Spells/monsters_effects/League_Ultimatum/mechanics/EPKs/ruin_count_0{}.epk`**
at `0x143473430`; the resource calls are at `0x1416a1218/0x1416a1353`.
It then tail-calls `0x1416a13c0`, whose bound audio format is
**`Metadata/Effects/Spells/monsters_effects/League_Ultimatum/mechanics/Audio/Ruin/Ruin{}Audio.ao`**
at `0x143473250`.

The removal callback `0x1416a0fa0` reads the remaining aggregate. It
refreshes in the branch where that aggregate exceeds the removed instance's
quantity; when the aggregate is zero it marks the task for removal and
invokes primary `+0x18`. This is not a claim that every removal branch
unconditionally refreshes every effect.

## Combat responsibility boundary

For this selected listener, the closed contract is **read current Ruin
quantities and maintain corresponding client effect/audio state**. Its
observed output counter belongs to the task, not a StatSet or a Life/ES
resource pool. The recovered chain provides no implementation of the
Ultimatum failure rule, reward forfeiture or Gauntlet death rule; those
outcomes must not be inferred from the visual count or the data descriptions.
The report therefore stops at the established effect/audio consumer instead
of expanding downstream rendering internals.

This is sufficient to remove this listener as an unresolved generic
damage-calculation candidate. It does not close other Buff subscribers,
damage-bearing backend types, owner-Stats refresh, or ordinary-hit
pre-deduction. Those original Hit/DoT questions remain independent.

Canonical ignored evidence **`r3-d9aa-listener-final`** contains 20 function
records and 26 original-byte ranges. Independent
**`r3-d9aa-listener-verify.py/json`** checks 68 instruction anchors, seven
table pointers, four exact leaves, the selected data hash/rows, row-index
arithmetic and both asset-path literals. Earlier
`r3-d9aa-listener-seeds`, `r3-d9aa-listener-publishers` and
`r3-d9aa-listener-quantity` batches are superseded exploration.
