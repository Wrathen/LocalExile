# Native contribution callers and receiving-object identity (3.29.3b)

## Result and scope

A direct call to native contribution add `0x1424fc480` is not sufficient
evidence for a DoT instance publishing into its target's incoming rate.
The receiving object, source token, amount producer and call qualification
must also be bound. This pass makes the existing direct-call inventory
reusable and follows three previously unclassified callers into graph-owned
Stats contributions. It does **not** recover the missing DoT publisher.

The retained inventory contains **103 direct callsites in 13 unwind owners**.
Each callsite is rechecked against original instructions and its unwind
owner. This is not a new whole-program scan: the inventory's exclusions for
indirect calls, inlined operations and unowned code remain unchanged.
Previously documented action/type/skill-data paths are reused, not presented
as new mechanism discoveries. Five newly recorded functions support argument
and object provenance; they are not five new damage mechanisms.

Everything here is static original-byte dataflow, not interpreted execution,
native client execution, a new capture or an observation of official sends.
Executable SHA256 is
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`;
preferred image base is `0x140000000`.

## 1. Finite direct-call map

| Caller owner | Calls | Bound receiving operation / next boundary |
|---|---:|---|
| `0x14193c850` | 2 | Generic action-context source adaptor; context acquisition is separately guarded. |
| `0x141c1a8d0` | 4 | Temporary action-context splash modifiers, including keys 1255 and 621; not a target-rate assignment. |
| `0x141c52500` | 2 | Temporary action-context negation of key 502 `base_is_projectile`. |
| `0x141c98a60` | 26 | Splash-related temporary action-context contributions; spatial continuation is a separate operation. |
| `0x141c99b50` | 26 | Further temporary action-context contributions; obtaining the native context remains a distinct requirement. |
| `0x141e406e0` | 1 | Type parser's named additive stat input. |
| `0x141e701d0` | 22 | Type/skill-information construction using temporary native objects. |
| `0x141e7d680` | 2 | Selected component provider receives base maximum Life/Mana from source-row fields; see section 3. |
| `0x141ee8ef0` | 6 | Stat-set constants, implicit and explicit per-level fields, including base multiplier and added effectiveness. |
| `0x141ee9090` | 9 | Outer native skill-data producer and additional contributions, with category-8 source identity. |
| `0x141eee7b0` | 1 | Graph-owner embedded native Stats at owner `+0x1d0`, with a category-3 node token. |
| `0x141eef620` | 1 | Same embedded offset, category-1 input token; removes that token before rebuilding contributions. |
| `0x141ef1890` | 1 | Another node-contribution layout uses owner `+0x190` and a category-3 token; do not equate the two owner layouts. |

The first five entries account for 60 callsites. Their native context
mechanism and ordinary startup guard are established in
[stat evaluation](client-stat-evaluation.md#5-action-local-native-mutation-is-guarded-out-by-ordinary-startup).
Their existence does not bypass that guard. Splash spatial/feedback
continuations remain described in [hit production](client-hit-production.md).
The type parser is described in [effect-source configuration](client-effect-source-catalogue.md),
and the two skill-data producers in
[the native skill-data audit](client-stat-evaluation.md#4-a-concrete-native-skill-data-producer).

This map does not prove that the executable has no other contribution
producer. A dynamic key/value vector can carry different keys on different
inputs; classifying its receiving object is not an exhaustive key census.

## 2. Graph-owned contributions have explicit source identity and update policy

Constructor `0x141eec2f0` forms a resource name with the literal `.psg` at
`0x141eec3c9`, retains that graph resource, and separately looks up
`atlas_start_node` at `0x141eec582`. At `0x141eec53a..0x141eec541` it
constructs a native Stats root directly at **this object's `+0x1d0`** through
`0x1424fb790`. This is a positive object-construction relationship, rather
than an attribution based only on nearby stat numbers.

Selected caller `0x142155bf0` invokes this constructor at `0x142155c87`
for an object retained through its outer owner's indexed slots. It later
passes that retained object to `0x141eef620` at `0x142155dff`.
This binds a concrete configuration-object path. It does not identify that
object as the Stats component of an enemy receiving a DoT instance.

### Node additions

`0x141eee7b0` first obtains a `(native key, signed amount)` vector through
`0x141eeeca0`, then selects its own embedded root at `0x141eee7e8`.
At `0x141eee7ef..0x141eee7fb`, the eight-byte source token becomes
`(3, zeroExtend16(input+0x10))`.
The loop at `0x141eee820..0x141eee837` passes each pair to native add,
with that embedded root in RCX, key in EDX, token pointer in R8 and amount
in R9D. Its known caller edges include `0x141eee580`'s tail jump at
`0x141eee69e` and `0x141eef380`'s call at `0x141eef597`.

`0x141ef1890` is a **different owner layout**. It selects `owner+0x190`
at `0x141ef1a04`, with token `(3, zeroExtend16(nodeId))` at
`0x141ef1a0b..0x141ef1a16`. Its selected ordinary row path obtains the
pair vector through `0x1424b46b0`; a nonzero row field `+0xbc` skips that
builder. The contribution call is `0x141ef1a4e`. Caller edges from
`0x141eefba0` and `0x141eefe60` are checked independently; their complete
configuration/lifecycle semantics are not inferred from these calls.

### Replacement is performed by the producer

`0x141eef620` builds token `(1, input+0x144)` at
`0x141eef6d5..0x141eef6e3`, selects its own `+0x1d0`, and calls source
removal at `0x141eef6f6`. **Removal precedes examining the replacement
collection.** The collection's empty check at `0x141eef70a..0x141eef70d`
can therefore return after removal without adding a replacement pair.
On the nonempty selected path, the actual add loop reads the chosen
provider's pair vector at `+0x180..+0x188`; its add call is `0x141eef7ce`.

This is a concrete remove-then-rebuild policy at a producer. It is not an
upsert policy supplied by native add. As established in
[source-token storage](client-stat-evaluation.md#native-source-tokens-retain-additive-contributions),
add appends another contribution even for an existing token, while a zero
amount returns before recording anything. Consequently, replacing a source
with an empty vector and adding a zero amount are different operations.
That distinction is useful when reconstructing source ownership, but this
graph producer is not evidence for ignite selection or Buff withdrawal.

## 3. Base Life/Mana initialization is another distinct contribution path

`0x141e7d680` initializes the source token to zero at `0x141e7d8c2`.
It loads signed amounts from the selected source row's `+0x20` and `+0x24`,
and contributes them to the selected component's provider at `+0x10`:

| Call | Key | Verified Stats Id | Amount |
|---|---:|---|---|
| `0x141e7d8da` | 125 | `base_maximum_life` | Source row `+0x20` |
| `0x141e7d8f3` | 129 | `base_maximum_mana` | Source row `+0x24` |

The exact higher-level source-row identity and general runtime eligibility
are outside this result. These two calls positively bind base-resource
configuration; they do not write a target's DoT loss rate. The Stats table
is pinned to SHA256
`91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da`.

## 4. Research consequence and reproducibility

The direct-call inventory is now classified at the stated argument/object
boundaries. Repeating those same callsites, the graph constructor or the
already guarded action context is not a new lead for source publication.
The next candidate needs a concrete target instance and an amount write or
transport relationship. Inlined/computed mutations and indirect providers
remain outside this inventory; none is assumed present or absent.

Canonical private batch: `Tools/re/out/client-damage-audit/r35-contribution-callers/`.
The retained producer/verification records check 23 function identities,
27 ranges, 6,721 instruction records, all 103 retained direct callsites,
52 semantic anchors, two literals and two named Stats keys.
The old reference scan is reused with its hash and limits; it is not rerun
or claimed expanded. Original-byte records and a fresh verifier are retained
through [the evidence manifest](client-damage-evidence.json).

**R2/R3/R4 remain Partial.** Actual DoT instance selection, target-rate
publication, shared-cap composition and expiry withdrawal; accepted-Hit
numerical damage to resource loss; and generic existing-Buff clock refresh
retain their original completion criteria.
