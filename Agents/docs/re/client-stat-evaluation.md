# Client stat contexts, producers and evaluation boundaries (3.29.3b)

## 1. Result and scope

The executable has a native contribution/evaluation engine, but normal network-granted skills use
network override providers. Their primary provider inherits the owner's network Stats provider;
additional stat sets inherit the primary skill provider. A getter does not itself evaluate a formula.
The action-local native context builder is conditional on a global flag that the successful client
startup path sets to one, which makes that builder return without constructing its native contexts.

This establishes R1's ordinary network Actor/skill input and freshness contract: the received values
update explicit overrides, absent values read their current upstream provider, and these operations
do not automatically enter the native formula transaction. Native contributions have a separate,
conditionally eligible dispatch/publication path. This is not a claim that every possible native
damage or DoT-source producer has been recovered.
See [the main damage investigation](client-damage-calculation.md) for formula and resource consumers,
and [the skill protocol](../protocol/11-skills.md) for the packet layout.

This page contains static artifact observations only. No client was launched, attached, hooked or
controlled. No server code, packet, CI or deployment behavior was changed. Addresses are preferred
image virtual addresses, not live ASLR addresses. Function names are analyst descriptions.

| Input | Identity |
|---|---|
| Executable | `Client/Game.exe`, PE x86-64, 3.29.3b |
| Preferred image base | `0x140000000` |
| Executable SHA256 | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `data/tables/Stats.json` SHA256 | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| Key mapping | Native/wire key `k` maps to JSON row `k - 1`, field `Id`; 23,346 rows |
| Instruction audit | Capstone 5.0.3, pefile 2024.8.26; SHA checked before decoding |
| Selected decompilation | Ghidra 12.1.4, JDK 21.0.12.1, offline import with `-noanalysis` |

## 2. The provider used by a network-granted skill

The ownership chain is established by constructors and field assignments, rather than matching
vtable pointers in the executable:

```text
type's native stat object
  <- owner's ClientInternalStats network wrapper
       <- primary network skill wrapper
            <- additional stat-set network wrapper
```

Each arrow means fallback for an absent key. It is not addition, a copied snapshot, or a formula
recalculation. `0x1424cfa40` returns an explicit pair even when its value is zero; it delegates to
provider `+0xe0` only when the key is absent. Table `0x14363bd90` installs that getter.

### Owner and skill construction

`0x1421c2ad0` constructs the owner's Stats component. Its `0x1421c3700` call at `0x1421c2b7c`
creates the network wrapper. The same pointer is stored in component `+0x40` at `0x1421c2b92`
and component `+0x20` at `0x1421c2b96`; the shared control pointer is at `+0x48`.
Thus the shared provider used by skill construction and the provider read by ordinary component
consumers are aliases of the same wrapper.

The network skill record reader `0x142196ce0` selects the owner (the Actor's entity, or the packet's
optional parent entity), finds its Stats component, and then:

| Instruction | Verified operation |
|---|---|
| `0x1421971ca..0x1421971e6` | Retains Stats component `+0x40/+0x48` as upstream provider |
| `0x14219720a` | Calls `0x1424cf8d0` to construct the primary network override wrapper |
| `0x142197291` | Adds each decoded primary `(key, value)` pair through `0x1424cfaf0` |
| `0x142197345` | Constructs each additional stat-set wrapper with the primary wrapper as parent |
| `0x14219740a` | Adds the additional set's decoded pairs through `0x1424cfaf0` |
| `0x142197758` | Calls `0x141ee73d0` with the primary provider and additional-provider vector |

`ActiveSkillInstance` constructor `0x141ee73d0` retains the primary shared provider at
skill `+0xf8/+0x100` (`0x141ee75cf..0x141ee75f1`). It also places that same provider at
`+0x138/+0x140` (`0x141ee778f..0x141ee77bc`). The additional providers are copied into the
vector at `+0x108/+0x110/+0x118` (`0x141ee75f8..0x141ee76ba`).

The context accessor `0x141ee7e70` returns `+0x138/+0x140` for context index zero
(`0x141ee7ec5..0x141ee7eeb`) and additional vector element `index - 1` for a nonzero index
(`0x141ee7e92..0x141ee7ebe`). Helper `0x1400f3a50` calls it and returns the raw pointer.
This establishes the provider type returned through the normal skill accessor. It does not depend
on an assumption that a field called “stats” always holds a native evaluation object.

### Subsequent network updates

The audited branch of Actor dispatcher `0x1421940a0`, at `0x1421945b7..0x1421946ee`, chooses
the primary provider from skill `+0xf8`, or an additional provider from `+0x108`, and loops through
the decoded pairs at message `+0x28..+0x30`. Its setter call is `0x14219464d → 0x1424cfaf0`.
It reads selected cooldown-related values before and after and may call `0x141eeb550` at
`0x1421946d7`. This audited branch does not use the native contribution transaction.

### Notification forwarding versus derived-cache updates

Unlike the owner wrapper constructor's flag-one path, `0x1424cf8d0` subscribes to upstream
notifications. It installs callback table `0x14363bdb8` at wrapper `+0xd8`
(`0x1424cf93c..0x1424cf943`); slot zero points to leaf callback `0x1424cfc00`.
That callback binary-searches the receiving wrapper's own explicit pairs:

- If the changed key has a local override, `0x1424cfc53 → 0x1424cfc6b` returns without forwarding.
  An inherited change cannot change that key's effective value while the override exists.
- If the changed key is absent locally, `0x1424cfc55..0x1424cfc66` forwards it to
  `0x1424cfd00`, with the receiving wrapper as source and its listener interface at `+8`.

The setter itself enters the same signal dispatcher by tail jump at `0x1424cfbee`, after storing
the pair. The dispatcher visits subscribed listener interfaces and invokes their slot zero through
thunk `0x1402fffbc` at `0x1424cfd76`, then compacts removed listeners. Neither the setter, the
installed forwarding callback nor this dispatcher calls the native dirty queue, begins/ends a native
transaction or writes the native evaluation cache. Native child propagation instead uses the
separate parent `+0x220` vector described below.

This is a same-key notification contract, including masking of inherited changes by explicit values.
It does not turn a received raw key into a native dependency update. A subscriber can perform its
own work; virtual delivery alone does not identify that work as formula evaluation or prove every
display/observer refreshes. The audited provider chain establishes forwarding, not a connection
to the native derived cache.

There is also no copied-value cache in the network getter: the next getter call searches the updated
local vector and, for an absent key, calls the current upstream provider. Consequently override and
fallback lookup freshness is established independently of a UI listener's refresh policy. The owner's
flag-one construction skips subscription to its native type parent; that affects notification delivery,
not the absent-key getter's fallback behavior.

### A concrete owner-provider consumer: Buff clock initialization

Buff insertion `0x141e11dc0` obtains the owning entity's Stats through `0x14013de10` and reads
the same component `+0x20` provider at `0x141e11df5`. It passes that provider and the actual new Buff
to `0x141f79160`, then writes its returned speed into instance `+0x20` at `0x141e11e01`.
The calculator reads explicit named timing stats through the provider's getter, including
`poison_time_passed_+%` for the concrete `poison` definition and bleeding-specific expiry inputs.
This is an actual network-owner provider consumption path, not evidence that the owner enters a native formula
transaction. The local procedural clock formula can consume current overrides and native-parent fallbacks
without reevaluating the native damage dependency graph.

The complete clock branches, local floor/pause rules and packet-refresh distinction are in
[DoT instance clocks](client-dot-routing.md#owner-stats-initialize-concrete-instance-clocks), backed by
`r2-buff-speed-owner-v3`. This closes insertion-time ownership for those **clock** reads; neither later
stat-change-driven clock refresh nor named instance damage-source publication is established by this path.

## 3. Native objects and actual evaluation

The native root constructor `0x1424fb790` installs table `0x14363d3b0`, whose getter is
`0x1424fc1e0`. The getter checks the presence bitmap and sorted records, returning record `+8`
or consulting its native parent. It does not invoke the registered formula. Formula evaluation
occurs through a separate transaction:

| Function | Verified responsibility |
|---|---|
| `0x1424fd240` | Begin or nest an update; prepare the evaluation context at native object `+0x190` |
| `0x1424fc480` | Add a contribution identified by a source token; update/dirty the affected stat |
| `0x1424fc770` | Remove contributions for a source token |
| `0x1424fd390` | End a transaction; perform work at the outermost nesting boundary |
| `0x1424fcc80` | Check formula eligibility, invoke a callback and publish a changed result |
| `0x142513630` | Register formula callbacks and dependency information |

The registry entry is at `global[0x1445c6ec8] + key * 0x50`. If its callback pointer is null,
the dispatcher uses the raw value and returns early for zero. If the callback is present, its
nonzero eligibility mask at entry `+0x40` must intersect object `+0x238`; otherwise evaluation
is skipped. The callback invocation is at `0x1424fccf8`, with the object's evaluation context.
The dispatcher writes the cache value and valid bit; an unchanged value returns before record
publication/notifications. A changed output is written to record `+8` at `0x1424fcdb3`.

`0x1424fb5f0(context, key)` walks the reverse-dependency vector at registry entry `+8..+0x10`
and inserts unsigned evaluation ranks into the sorted work vector at context `+0x10..+0x18`.
Startup builds these ranks by cycle-checked, dependency-first traversal of declared inputs;
transaction completion maps ranks through global `0x1445c6ee0` before dispatching. Its loop
reloads the vector after callbacks and processes newly queued later ranks. Unchanged formula
outputs stop propagation; this is not a general repeated-sweep fixed-point solver.
The earlier unconditional duplicate-free description is corrected: 24 of the 2,539 recovered
declarations contain repeated inputs, and the helper can retain repeated ranks from one reverse
vector. This does not establish duplicate damage. See [dependency order and transaction queues](client-stat-dependency-order.md)
for exact algorithm, actual duplicate examples, bounded controls and callback/reentrancy limits.

### Native source tokens retain additive contributions

The contribution store has a concrete contract separate from any particular effect's stacking rule.
Native add `0x1424fc480` accepts an eight-byte source token and a signed 32-bit amount.
It adds the amount to record `+8` with ordinary integer `add` at `0x1424fc63a`, then appends a
12-byte `(token, amount)` entry at `0x1424fc64b..0x1424fc657` or through its capacity-growth helper.
The existing-record path does not replace an earlier amount for the same token or select a maximum.
The zero-amount entry guard at `0x1424fc480` returns before recording a contribution.

Source removal `0x1424fc770` looks up both token words (`0x1424fc7cd..0x1424fc7db`), obtains
that source's affected-key vector, and calls per-key removal `0x1424fc9e0` at `0x1424fc8a9`.
That helper filters matching token pairs from the per-stat list (`0x1424fcb22..0x1424fcb77`),
then rebuilds record `+8` by ordinary 32-bit addition of the remaining entries
at `0x1424fcbaa..0x1424fcbcf`. There is no saturation in these particular add/rebuild operations;
derived callbacks retain their own separately documented clamps and eligibility checks.

These are raw stat-source accumulation/removal operations, not an established ignite-max or poison-sum
algorithm. A caller would still have to choose the applicable instances and publish their magnitudes.
The network override vector is a different store, and no Buff-to-these-functions binding follows from
the token layout. The three original-byte bodies are retained in `r2-native-source-tokens`.

### Native child inheritance is actively propagated

Native child constructor `0x1424fbb80` installs the native table, copies the parent's resource/data
pointer to child `+0xe8`, and retains the parent at child `+0xf0/+0xf8`
(`0x1424fbc22..0x1424fbc3d`). Its records/cache initially start empty. It derives eligibility as:

```text
child.+238 = parent.+238 & qword[0x14458e848]  (0x1424fbd39..0x1424fbd4a)
child.+240 = parent.+240                     (0x1424fbd51..0x1424fbd5b)
```

The constructor registers the raw child pointer in the parent's `+0x220..+0x228` vector
(`0x1424fbef6..0x1424fbf32`); destructor `0x1424fbf50` unregisters it.

The outermost parent transaction in `0x1424fd390` walks changed keys and registered native
children. It begins a child transaction at `0x1424fd8d8`, calls `0x1424fcfb0(child, key)` at
`0x1424fd935`, and closes the child transaction through `0x141937a40` at `0x1424fd969`.
This is synchronous propagation to native children, beyond listener notification.

For a raw key with a materialized child record, `0x1424fcfb0` refreshes its inherited contribution
from the parent through `0x1424fc2f0` at `0x1424fd107`, preserves/re-adds child-local source
contributions, and writes the evaluation cache/value bit (`0x1424fd1cc..0x1424fd1f4`). Regardless
of whether that raw-record refresh ran, its tail queues reverse dependencies through
`0x1424fb5f0` at `0x1424fd209` and changed-key notification through `0x1424fcec0`.
The precise record-presence and eligibility conditions still matter.

The native layout is incompatible with treating a network wrapper as a native child parent:
native `+0xf0` is a parent, while network wrapper `+0xf0` begins its explicit-pair vector.
The two object families must remain separate in the analysis.

### Eligibility masks have a data-table identity

Initializer `0x1428e68b0` names `Data/VirtualStatContextFlags.dat` at `0x1428e6db6`.
The corresponding extracted `data/tables/VirtualStatContextFlags.json` contains 40 rows,
SHA256 `cddd987614a0e55dcb9c990242c0cbd94fb679950c44ddfa9fbe7c28e1475c2b`.
The zero-based table row supplies the bit position; this is separate from the one-based Stats key convention.
Examples include Object 0, Skill 1, AttackHit 2, SpellHit 3, SecondaryHit 4,
PhysicalDot/FireDot/ColdDot/LightningDot/ChaosDot 5..9, and SanctumDefence 38.

At `0x1428e6f58..0x1428e6f9c`, the initializer partitions those bits using the table's
currently unnamed `__col1`: value 0 contributes to global `0x14458e838`, value 1 to
`0x14458e848`, and value 2 to `0x14458e840`. The five typed DoT rows and SanctumDefence
have value 1. This binds the native root/child masks above to actual table rows;
it does not rename those three classes as client/server modes.

The same initializer reads each Stats row's ContextFlags and DotFlag reference lists and
populates registry masks `+0x40/+0x48` (`0x1428e7008..0x1428e70c4`). ContextFlags populate `+0x40`
only when that registry entry has a nonnull formula callback (`0x1428e7002..0x1428e7006`);
the DotFlag-to-`+0x48` loop follows independently. Reverse-dependency
processing also adds dependent context bits to `+0x48`, except entries whose referenced
flag row has class 1 (`0x1428e70e1..0x1428e7170`). This is more than a mask guessed from
its later use: the table-to-mask producer is present in the same executable.

On first materialization of a raw contribution record, `0x1424fc480` reads registry `+0x48`,
intersects native object `+0x240`, and ORs the result into `+0x238` at `0x1424fc5f4..0x1424fc607`.
An already-present record branches directly from `0x1424fc540` to `0x1424fc62f`, bypassing that OR.
Formula publication and inherited raw-record refresh can also add masked bits.
Thus a zeroed constructor field is not proof that the object's formulas stay permanently ineligible.
For the pinned Stats table, offence keys 279/670 refer to AttackHit, DPS 679 to Skill,
and final incoming DoT keys 3357..3359 to Object. Raw source keys 2023/2025/2027/8215/8216/8217
have empty ContextFlags/DotFlag lists. Empty source lists do not prohibit dependency-derived bits.

These are native formula-context categories. A named AttackHit/FireDot row does not itself
establish a concrete hit roll, source instance, resource deduction, or native recomputation by a
network wrapper. Those execution/data-flow questions retain their separate completion criteria.

## 4. A concrete native skill-data producer

`0x141ee9090 → 0x141ee8ef0` is a verified native contribution producer: it actually calls
`0x1424fc480`, rather than merely containing integers matching stat keys.
The outer function supplies a source token with category `8` and an index.

`0x141ee8ef0` iterates stat-set key/value sequences through `0x14249f140` and `0x14249f2f0`.
The sequences concatenate ConstantStats, per-level FloatStats and per-level AdditionalStats;
implicit stats are emitted in a second loop with value one. Helper `0x142290090` converts a
Stats row handle to a native key: a null handle gives sentinel `0x5b33`; otherwise it computes
`(rowPointer - StatsTableBase) / 0x69 + 1`.

The producer also contributes these explicit `GrantedEffectStatSetsPerLevel` fields:

| Raw row offset / schema field | Native stat | Producer instruction and conversion |
|---|---|---|
| `+0x18` / `SpellCritChance` | 556 `base_spell_critical_strike_chance` | Call at `0x141ee900c`; direct integer |
| `+0x1c` / `AttackCritChance` | 9692 `active_skill_off_hand_base_critical_strike_chance_override` | Call at `0x141ee9025`; direct integer |
| `+0x20` / `BaseMultiplier` | 6172 `active_skill_base_attack_damage_final_+permyriad` | Load at `0x141ee902a/0x141ee902d`; nonzero value passed unchanged at `0x141ee9043` |
| `+0x24` / `DamageEffectiveness` | 891 `active_skill_gem_added_damage_effectiveness_+%_final` | Signed integer division by 100, truncating toward zero, at `0x141ee904b..0x141ee905d`; nonzero result passed at `0x141ee9071` |

The schema offsets are independently corroborated by `data/schema/schema.min.json` for the
3.29.3b layout. In particular, this closes the local raw `BaseMultiplier → key 6172` encoding:
the producer performs no division, multiplication or subtraction on that field. This does not
establish that every received key 6172 came from the same producer, nor supply the complete
downstream attack-damage equation.

`0x14249f2f0` also contains value interpolation: constants are direct integers; additional/float
values can be direct when the context's level parameter is zero, while selected interpolation
modes use the surrounding rows, level/PlayerLevelReq and effectiveness. A complete port of all
interpolation modes is outside this bounded result.

### RF stat-set constants can activate a typed DoT context

The pinned data supplies a concrete conditional use of that producer. GrantedEffects row 712
`RighteousFire` selects GrantedEffectStatSets row 719, whose ConstantStats provide native keys
883 `base_righteous_fire_%_of_max_life_to_deal_to_nearby_per_minute` and
1947 `base_righteous_fire_%_of_max_energy_shield_to_deal_to_nearby_per_minute`, both with value 4200.
GrantedEffects row 713 `RighteousFireAltX` selects stat-set row 720, which supplies native key
3399 `base_righteous_fire_%_of_max_mana_to_deal_to_nearby_per_minute` with value 4200.
The table references are zero-based Stats rows 882/1946/3398; the native keys are one greater.
All three Stats rows explicitly list DotFlag `[6]`, the FireDot context bit.

For a native object whose `+0x240` permits that bit, processing these constants through
`0x141ee8ef0` reaches native add at `0x141ee8f86`. First record insertion can therefore OR FireDot
into `+0x238` through `0x1424fc607`; the default native root mask includes class 1 and permits it.
This is a positive parameter-publication/context-activation bridge, conditional on that stat set being
processed for that native object. It does not establish a live RF source instance, nearby-target damage,
or conversion into incoming raw2023. The known type-information callers and their limitations remain below.
The conditional outgoing formula bridge is now established separately as
`883/1947/3399 → 14408 → 8198 → 5981`, with native pool getters and arithmetic boundaries in
[DoT routing](client-dot-routing.md#outgoing-righteous-fire-formula-bridge).

The data bridge is retained in local evidence `r2-context-dot-data/evidence.json`.
GrantedEffectStatSets SHA256 is `fa258d15498c5e1910f94bf6cfdf938184a4e9d4a1f4b71212cbaace192d5c8b`;
GrantedEffects SHA256 is `ee02ccde977a7a7c444b3fb329867b65a931dda3ab89f204dfc96b15571a6b3b`.
The Stats and VirtualStatContextFlags identities are pinned above. No runtime execution was used.

### Where this producer is used

The direct-caller audit of `0x141ee9090` found `0x141e701d0` (`0x141e709ef`) and
`0x141e8b760` (`0x141e8bced`). Both construct temporary **native children** while parsing type
information, rather than updating normal network-created skill wrappers:

- `0x141e701d0` constructs a child at `0x141e70976` for level/skill information during type parsing.
- Actor type initialization `0x141e8b760`, reached through `0x142192210`, obtains the type's Stats
  component, constructs a child at `0x141e8bcb2`, adds skill values, then uses active-skill registry
  callbacks through `0x141941e50/0x1419774d0`. It destroys the child at `0x141e8be7f`.

The constructor call sites in passive-tree/Atlas code are separate native objects, not evidence
that a live Actor's network skill uses this producer. The bounded direct-call search excludes
indirect calls, computed targets and unwind-less leaves.

## 5. Action-local native mutation is guarded out by ordinary startup

The executable contains a conditional native action-context mechanism. `0x14193be90` can lazily
populate action `+0x1b8..+0x1c0`, using skill `+0xf0 → 0x141ee7e70`, and helper `0x1419bd030`
creates native children through `0x1424fbb80`. Descriptor modes select all contexts, one context,
or a fresh native root. Helpers `0x14193c850`, `0x14193c910` and `0x14193c690` add source
contributions, remove them, and unwind the context stacks respectively.

Crucially, `0x14193be90` checks byte `0x1445b38cd` at `0x14193bebc` and branches directly to
its exit when it is nonzero (`0x14193bec3 → 0x14193c660`). Transaction/virtual-producer wrapper
`0x14193bae0` is guarded by the same byte at `0x14193baf8`. Conditional attack-state producer
`0x1419e3030` likewise requires it to be zero.

That byte has an actual initialization binding:

```text
PE entry 0x142c82084
  jmp 0x142c81f10 at 0x142c82091
  call 0x140103710 at 0x142c82011
  call 0x1401036e0 at 0x140103778
  call 0x140103390 at 0x1401036f2
  call 0x140101680 at 0x1401034d4
    mov byte [0x1445b38cd], 1 at 0x140101e50
```

The successful initialization path writes one before its normal return-one result. Caller
`0x140103390` tests that result at `0x1401034d9` and enters `0x1401027a0` at `0x1401034dd`
only on success. Thus the normal startup-to-main-loop path establishes the value which disables
the audited native action builders. The global is not assigned a semantic source name here.

A bounded scan of executable sections for common RIP-relative compare/load/store/LEA encodings,
verified against instruction boundaries in PE unwind bodies, found this write-one and no reset
among the selected encodings. It is not an exhaustive alias analysis: computed references,
arbitrary bulk writes and other instruction encodings remain outside that negative result.
No runtime value was read. The supported conclusion is that these native action mutation paths
are disabled after the audited successful startup, absent an unestablished later change to the flag.
Their existence does not establish native stat recomputation on ordinary network skills.

## 6. Freshness and remaining closure criteria

| Context | Established | Not established by this page |
|---|---|---|
| Native type/root | Contributions, outer transaction dispatch, eligibility checks and result publication | Every actor-specific producer or every eligible callback's initialization timing |
| Native child | Parent ownership, child registration and transaction propagation with local contribution preservation | Universal freshness when masks/record guards skip work |
| Owner network Stats | Explicit overrides, type-native fallback and shared-provider identity | That a raw network update recomputes omitted derived keys |
| Network-granted primary/additional skill | Exact constructor/accessor/update binding to network wrappers | A full native recomputation transaction for each received pair |
| Action-local native context | Conditional constructor/mutation mechanism and successful-startup disabling flag | An ordinary network-client execution with that flag cleared |
| Local skill-table producer | Actual contribution calls, stat-row mapping and raw BaseMultiplier encoding | Incoming hit/DoT source production or complete downstream damage arithmetic |

R1 is **Established for the ordinary network Actor/skill contract**: the actual input providers,
pair producer/update paths, getter behavior, inherited notification masking, native transaction
publication boundary and successful-startup action guard are bound above. A raw network override
does not become a native source contribution, and omitting a derived output does not cause the
network getter to evaluate it. Native outputs become readable when the separate eligible transaction
publishes them into native records; network fallback can then read those records.

The wider damage investigation still needs any particular damage/DoT source producer required by
its own criterion. Such a producer must be connected to an applicable context and transaction, not
inferred from the existence of a formula. Recovery of all possible listener implementations and
dynamic aliases is not claimed; those are bounded static coverage limits rather than missing links
in the ordinary provider chain demonstrated here.

## 7. Retained local evidence and reproduction

Original instructions and selected decompilations are retained under ignored
`Tools/re/out/client-damage-audit/`; they are offline audit records, not REA Evidence IDs.
The public evidence manifest is maintained with the main investigation. No generated client code
or original executable bytes belong in version control.

| Batch | Scope |
|---|---|
| `r1-producer-refs` | Native constructor/add/begin/end callers and reverse-dependency helper |
| `r2-native-source-tokens` | Raw additive contribution entries, token-directed removal and per-key sum rebuilding |
| `r1-skill-producers` | Native skill producers, native action context builders, source removal and child destructor |
| `r1-object-binding` | Skill context accessor, shared child helper and conditional action wrappers |
| `r1-active-context` | Actor type skill producer, native child constructor and stat-set iteration/mapping |
| `r1-skill-native-link-v2` | Native getters and inherited-record refresh instructions |
| `r1-active-skill-owner` | Manager and action ownership instruction bodies |
| `r1-network-skill-context` | Direct network wrapper constructor/setter references |
| `r1-network-skill-binding` | Skill record reader, Actor update branch and child-refresh decompilation |
| `r1-network-notifications-v2` | Network setter, inherited-change forwarding callback and listener dispatcher; callback has explicit reviewed leaf range `0x1424cfc00..0x1424cfc6c` (exclusive end) |
| `r1-skill-final-binding` | ActiveSkillInstance constructor, insertion and accessor helper instructions |
| `r1-client-mode-init` | Successful startup flag assignment and owner Stats alias construction |
| `r1-client-entry`, `r1-client-entry-wrapper`, `r1-client-entry-main`, `r1-client-crt` | Original instruction chain from application entry to startup |
| `r1-client-pe-entry.json` | PE entrypoint and original entry instruction bytes |
| `r1-client-guard.json` | Bounded verified RIP references to the action mutation flag |
| `r4-native-mask-initializer` | Context-flag table identity, category partition and registry mask production |
| `r4-native-eligibility.json` | Bounded native-stat-region field-reference candidates; each still needs object provenance |

Reproduce bounded function audits with [damage_audit.py](../../../scripts/re/damage_audit.py)
and selected decompilation with [BoundedFunctions.java](../../../scripts/re/ghidra/BoundedFunctions.java),
following [the tooling notes](tooling.md#bounded-damage-audit). Use an isolated Python invocation
(`python -I -B`) so the repository's `dis.py` does not shadow the standard library. Every retained
`audit.json` identifies the executable digest, exact unwind-owned spans and span digests.
