# Critical Link numerical export and Buff transport boundary (3.29.3b)

## Result and scope

The action registered as `CriticalLink` has a concrete three-value numerical
collector. It reads the link skill's multiplier grant, accuracy grant and
intermediary main-hand critical chance, appending their raw signed32 bit
patterns to an output vector in that order. This positively classifies a
consumer found while searching for actual Hit arithmetic: it exports Link
parameters, without rolling a critical strike or applying target mitigation.

The collector's actual incoming invocation and vector-to-Buff grant consumer
remain unbound. Matching the target definition's three declared attributes
does not establish that missing connection. Both Link Buff definitions are
non-recovery, so their granted magnitudes are omitted on the separately
audited AddBuff wire carrier. The target's calculated critical override and
network Stats synchronization remain distinct contracts.

This is original-byte static research and supplied-state instruction
interpretation. No client/server execution, new capture or official sending
behavior is claimed. R0/R1 retain their established scope; **R2/R3/R4 remain
Partial**. The broader goal still includes actual Hit-to-resource and DoT
source-selection/publication, not just formula or special-skill coverage.

| Input | SHA256 |
|---|---|
| `Client/Game.exe`, 3.29.3b, preferred base `0x140000000` | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `Stats.json` | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| `BuffDefinitions.json` | `3a23c6fdf7ba29fb8508e5c9560a44e5292345e45dd2f886ef7126b8427fd659` |
| `ActionTypes.json` | `eef7b004b13a3f32e487d5c102c41362b62131f83249f45194ece05b8518d9d6` |

## 1. Concrete action binding

Registrar `0x14126da40` passes literal `CriticalLink` at `0x14325c718`
and factory `0x141275470` to `0x141941bb0`. The factory's ordinary allocation
branch supplies action type `0x848d` to `0x1414e6940`, then installs final
primary table `0x143254eb0` at `0x141275560..0x141275567`.
The extracted `ActionTypes` row names `CriticalLink` with `HASH16 = 33933`,
the same number. Its earlier alternate-factory branches remain conditional;
this report does not establish startup registration precedence or an actual
runtime action instance.

Copy factory `0x1412cb750` calls `0x1412cc4e0` and reinstalls the same final
primary table at `0x1412cb77f..0x1412cb786`. The constructor's intermediate
table stores are therefore not substituted for this factory's final table.

| Final primary slot | Bound function and behavior |
|---|---|
| `+0x4e8` | `0x1412cb660`, append three numerical Stats values |
| `+0x4f8` | Exact leaf `0x1412cb650`, returns whether the supplied key equals 15066 |
| `+0x500` | Exact leaf `0x1412cb640`, returns 1957 |
| `+0x508` | Exact leaf `0x1412cb630`, returns 1958 |

The returned numbers correspond to one-based source/target Buff definition
numbers below. Their downstream lookup/application is not recovered here.
The key predicate recognizes 15066, not final chance 552 or Link override
15067; its use as a subscription or refresh trigger is likewise unbound.

## 2. Ordered numerical reads

The collector consumes `RCX = action`, `RDX = uint32 vector`. Before each
read, it reloads action byte `+0x174` and calls provider selector
`0x14193b350`. It then calls that provider's numerical getter at slot zero.

| Order | Native source key | Id | Getter call |
|---|---|---|---|
| 1 | 15077 | `critical_link_grants_base_critical_strike_multiplier_+` | `0x1412cb687` |
| 2 | 15590 | `critical_link_grants_accuracy_rating_+%` | `0x1412cb6cb` |
| 3 | 15066 | `intermediary_main_hand_critical_strike_chance` | `0x1412cb70f` |

With spare vector capacity, each result is written as one four-byte word and
the end pointer advances by four. Otherwise the corresponding stack word is
passed to growth helper `0x14014cdd0`. Existing prefix words are retained.
No percentage conversion, clamp, zero filtering or random sampling occurs in
this collector. For example, supplied values `100, 15, 750` append exactly
those three integers; negative values retain their original 32-bit patterns.

These are three sequential getter invocations, not an atomic snapshot of a
provider captured once. Controls intentionally changing the selector fixture
between calls confirm the repeated selection/read structure, not that the
game actually changes providers during this operation. Sampling time,
refresh cadence and the use of the completed vector remain unresolved.

For the supplied network-provider binding, all three calls execute the actual
getter `0x1424cfa40`, with the requested keys present in its sorted override
vector. This tests the collector/getter composition; the action-to-provider
selection itself is an explicit fixture, not a newly closed provider path.

## 3. Definition alignment is not grant publication

Extracted definition rows are zero-based. Their `GrantedStats` entries refer
to zero-based Stats rows; native stat keys add one.

| Definition row / Id | Granted native keys | `IsRecovery` |
|---|---|---|
| 1956 / `critical_link_source` | None | false |
| 1957 / `critical_link_target` | 96, 85, 15067 | false |

The three target attributes are `base_critical_strike_multiplier_+`,
`accuracy_rating_+%` and
`override_main_hand_critical_strike_chance_%_from_critical_link`, respectively.
Their order is consistent with the collector's three values. A native grant
consumer must still be bound before drawing solid dataflow edges between
these source values and target attributes.

The already recovered [main-hand chance wrapper](client-critical-formulas.md#4-main-hand-override-and-multiplier)
returns nonzero 15067 directly, otherwise intermediary chance 15066. It does
not multiply that override by 100. The collector's third raw value is
consistent with this downstream unit contract, but this consistency does not
prove the missing transfer, target selection or update policy.

The original [AddBuff writer/reader](client-buff-wire-magnitudes.md) provides a
different positive result. With these actual definitions, granted-vector
content and length do not change the encoded body. The target reader creates
three zero words; the source reader creates none. A supplied separately
counted vector still round-trips independently. This does not prove that the
separate vector is a stat-value carrier. Nor does it prove that the server
sends a particular target override; the separate
[network Stats receiver](client-initial-stats-rate.md) is already documented.

For server reconstruction, keep native source sampling/grant application,
Buff presence/timing synchronization, and target Stats publication distinct.
Do not implement the missing numerical grant by writing these three values
into a granted-vector field that this selected wire contract omits.

## 4. Navigation coverage and next boundary

The preceding 32-key offence navigation is now complete for its stated scan:
16,767 candidate unwind owners, 4,311 instruction matches in 2,198 owners.
Of those matches, 111 fall in known registrars and 265 in known formula
callbacks. The remaining 3,935 are an **untyped navigation population**, not
3,935 actual numerical consumers or completed classifications. Constants may
be allocation sizes, localization IDs or unrelated indices.

The high-key candidate above is now positively classified as a Link parameter
collector. A separate aligned uint32 data scan in `.rdata/.data` found seven
windows with at least four distinct selected numbers in 128 bytes. The
two selected SIMD-constant regions have known registrar readers; selected
24-byte records instead point to unrelated localized text. Other numerical
windows remain untyped. Neither scan covers every dynamic key, inline cache
read, encoded/unaligned array, computed reference or unwind-less function.
No whole-program absence or accepted-Hit conclusion follows.

Do not repeat the same immediate-key scan to claim progress. Next Hit work
must establish a typed numerical event consumer or follow a different
producer/cache/resource boundary. The Link vector consumer is a retained
specific lead, not a substitute for the original Hit/DoT completion criteria.
Projectile action/RNG/contact/World research continues with its existing owner.

## 5. Verification

Ignored reproduction files are `Tools/re/out/client-damage-audit/`:
`r31-critical-link-inputs.py`, `r31-critical-link-fresh.py` and
`r31-critical-link-inputs/`. The bounded navigation records are under
`r31-critical-navigation/` and `r30-offence-consumers/`.

All **190 controls** pass: 160 collector/real-getter cases, 12 predicate cases,
two returned-number cases, eight original-writer/independent-byte comparisons
and eight original-reader controls. They interpret **28,000 instructions**
across **422 visited sites**. Cases cover signed32 boundaries, zeros, retained
prefixes, capacity/growth branches and repeated supplied provider selection.

Fresh verification rechecks 12 function identities / 12 original ranges /
879 decoded instructions, 29 instruction anchors, five table slots, six Stats
keys and two actual Buff definitions. Eight entries are new to this evidence
inventory, including three exact leaves; that is not eight new semantic
closures. Prior immutable evidence remains retained.

Provider selection, growth allocation, stream/header I/O and resource-table
resolution are explicit fixtures. Original collector, network getter and
AddBuff writer/reader instructions are interpreted offline. This is same-agent
verification, not an independent-worker review, native execution or captured
official behavior. No .NET/game tests are applicable to this research-only pass.
