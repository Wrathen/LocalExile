# Client damage calculation and resource prediction (3.29.3b)

## 1. Question, scope and evidence rules

Does the client calculate damage, change its local resources, and later accept a server correction?
Treat those as three separate questions, for both **hits** and **damage over time**.
"Pre-resolution" is a research name, not an established description of every client combat path.
The goal is to recover useful calculations and their execution boundaries before choosing a server implementation.

Research sufficiency means recovering calculations, rules and original client/server responsibilities that can
support a corresponding server implementation. It does not require reconstructing the complete client renderer
or obtaining the official server executable. Verified static dataflow is positive evidence within its stated
guards and artifact version; missing live gameplay or raw traffic does not make that evidence unreliable.
Runtime evidence is needed for claims about observed executions, measured cadence or live numerical parity.
An unbound formula input or source-selection rule remains a real gap, independently of those runtime questions.

The strongest result so far is **continuous resource integration plus incoming absolute pool assignment**.
Typed DoT formula chains are present, including mitigation, healing exclusions and Life/Mana/ES routing.
Their presence alone does not prove that an ordinary network entity evaluates those whole chains.
Network Stats can supply a final rate directly and bypass upstream calculation for that key.
Ordinary network Actor/skill provider identities, update forwarding and lookup freshness are now established.
The successful startup path disables the separate action-local native stat mutation mechanism.
Concrete Fireball contact/avoidance and Melee accuracy/entropy decisions are now bound locally.
The connection from those decisions to numerical damage/mitigation/pre-deduction has **not** been established.

| Evidence class | Meaning here |
|---|---|
| Static artifact | Original PE bytes, decoded instructions, installed tables and conditional call/data flow |
| Analyst inference | A proposed meaning or path whose object binding or caller is still unresolved |
| Historical local runtime | Previously recorded behavior of this client connected to LocalExile |
| Official-server behavior | Requires separate evidence; neither the executable nor a local-server trace proves it |

The work in this page is static analysis.
No client was started, attached, hooked, controlled or played for this investigation.
Older runtime results are identified explicitly and remain in [recovery timing](recovery-timing.md).
Addresses below are preferred-image virtual addresses, not live ASLR addresses.
Decompiler signatures and guessed names are not original source.

### Artifact identity and reproducibility

| Input | Identity |
|---|---|
| Executable | `Client/Game.exe`, PE x86-64, client 3.29.3b |
| Preferred image base | `0x140000000` |
| Executable SHA256 | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `data/tables/Stats.json` SHA256 | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| Stats mapping | Native/wire key `k` is zero-based JSON row `k - 1`, field `Id`; 23,346 rows |
| Offline decoders | Capstone 5.0.3, pefile 2024.8.26 |
| Selected decompilation | Ghidra 12.1.4, JDK 21.0.12.1, `-noanalysis`, selected function bodies only |

The [evidence manifest](client-damage-evidence.json) retains function ranges, original-file offsets and SHA256s.
It contains analysis metadata, not copied executable bytes or a full decompiler dump.
Its `boundary` distinguishes unwind-owned functions, explicit leaf ranges and four historical bounded thunk entries.
`selectedDecompilationCompleted: null` means no selected decompilation was retained, not a decompiler failure.
Reproduce the original-byte audit with [damage_audit.py](../../../scripts/re/damage_audit.py).
See [tooling](tooling.md#bounded-damage-audit) for invocation and selected Ghidra decompilation.
Local generated `audit.json`, `.asm` and `decompilations.jsonl` stay under ignored `Tools/re/out/`.
They are offline evidence records, not REA Evidence IDs.

Detailed supplements keep the evidence reviewable without treating this summary as an exhaustive formula list:

- [Stat objects, inputs, notifications and startup guards](client-stat-evaluation.md).
- [Direct contribution callers, receiving objects and graph-owned source replacement](client-native-contribution-callers.md).
- [Native declared-dependency order, transaction queues and actual duplicate declarations](client-stat-dependency-order.md).
- [Typed DoT formulas, resource routes and arithmetic boundaries](client-dot-routing.md).
- [Complete incoming fire self-rate composition, signed64 percentages and state gates](client-fire-self-rate.md).
- [Complete incoming physical/chaos self-rates, transfer gates and arithmetic order](client-physical-chaos-self-rates.md).
- [Buff insertion clocks, network updates and dynamic-refresh boundaries](client-buff-clock-updates.md).
- [Three concrete Buff backend Stats subscribers and their state/presentation consequences](client-buff-stats-subscriptions.md).
- [Buff clock eligibility inputs, serialization defaults and update asymmetry](client-buff-clock-input-provenance.md).
- [Empty-Id2 Buff backend lookup, startup catalogue and base lifecycle boundaries](client-buff-default-backend.md).
- [Actual Buff observer payloads, lifecycle script configuration and selected presence predicates](client-buff-lifecycle-scripts.md).
- [Concrete Buff observer with no-op gain/loss callbacks and guarded matrix output](client-buff-observer-attraction-shape.md).
- [Exact Cold Snap Buff factories and concrete animation/matrix backend boundary](client-coldsnap-buff-backend.md).
- [Flame Wall secondary Buff samples current owner Stats and restores captured remaining duration](client-firewall-clock-hold.md).
- [PreloadFromStats row provenance and installed .epk resource collector](client-stat-preload-epk-collector.md).
- [Initial Entity Buff granted values, recovery qualification, endian differences and storage normalization](client-buff-initial-granted-values.md).
- [AddBuff writer/reader magnitude eligibility, uncounted granted slots and wire-cardinality agreement](client-buff-wire-magnitudes.md).
- [Actual Buff granted capacity consumed by reason-4 Life loss and exhausted-instance duration writes](client-buff-totem-degen-capacity.md).
- [Actual Buff numerical resource forecasts and first-eligible Life-callback source context](client-dot-source-consumers.md).
- [Life-to-Buffs advancement guard and concrete producers](client-buff-clock-producers.md).
- [Existing Buff network clock receiver, omitted-speed default and selected backend consequences](client-buff-network-clock-contract.md).
- [Named RF source template and unresolved active publication](client-dot-source-publication.md).
- [Source-creation authority and versioned ignite/poison/bleed selection rules](client-dot-source-authority.md).
- [Both-hand offence formulas, conversion, average/DPS and numerical UI consumers](client-offence-estimates.md).
- [Twenty ordered attack ranges and four totals, separate base/added factors and endpoint asymmetries](client-attack-hit-formulas.md).
- [Thirty spell base, intermediate-added and selected-added component equations](client-spell-damage-components.md).
- [Life-cost, hand-cast, Battlemage and weapon-as-spell contributor builders](client-spell-contributor-builders.md).
- [Integer pool mutations, skill-cost routing/payment and extra-arrow Life sacrifice](client-integer-resource-mutations.md).
- [Actual maximum-stat subscriber, client-mode current-value gate and Life-only fractional-carry reset](client-pool-maximum-refresh.md).
- [Actual recharge-active notifications, delay-reset ordering and separate total recovery-rate authority](client-recharge-rate-authority.md).
- [Captured recovery rates, later pool state and distinct Life/ES loss-rate read order](client-resource-read-order.md).
- [Peer-established ordered World checkpoint composed through parsed resource assignment and recovery arithmetic](client-resource-checkpoint-composition.md).
- [Parsed Stats rate updates through actual overrides, notifications and Life integration](client-network-rate-composition.md).
- [Buff expiration/removal composed with independent explicit rates and Life loss](client-buff-rate-independence.md).
- [Actual Buff insertion, owner timing notifications and retained network clock precedence](client-buff-clock-authority.md).
- [Retained Buff replacement through actual construction, field transfer and subsequent clock/resource integration](client-buff-replacement-composition.md).
- [Buff body decoding through retained source identity and actual Life-callback attribution, separate from rate publication](client-buff-wire-source-composition.md).
- [Complete final spell min/max/total ranges, ordered equations and conversion branch asymmetries](client-spell-hit-formulas.md).
- [Taken-as coefficients and ordered armour applicability overrides](client-defence-transfer-formulas.md).
- [Complete physical/elemental reduction inputs, Strength of Blood and physical ceiling](client-reduction-inputs.md).
- [Bleeding policy inputs, effective Crimson Dance and native dependency propagation](client-bleeding-policy-inputs.md).
- [Outgoing bleed/ignite/poison components, faster/slower and critical DoT multipliers](client-ailment-outgoing-formulas.md).
- [Complete named ignite outputs, category qualifications and intermediate publication boundaries](client-ignite-output-family.md).
- [Complete named poison outputs, wrapping integer totals and chaos-only always-output gates](client-poison-output-family.md).
- [Effective critical probabilities, luck rules, overrides and hit multipliers](client-critical-formulas.md).
- [Complete main-hand, off-hand and spell critical-chance input builders](client-critical-inputs.md).
- [Critical Link's ordered raw-value export, exact action slots and separate Buff transport boundary](client-critical-link-export.md).
- [Shared/typed hit-taken modifiers, ignore predicates and enemy-critical input composition](client-hit-defender-modifiers.md).
- [Connected elemental/hand penetration inputs, highest-uncapped ties and distinct event-qualified inputs](client-penetration-inputs.md).
- [Defender-conditional damage context presence predicate and unresolved numerical consumer](client-defender-context-presence.md).
- [ApplyDamageFunctions metadata reader and active-index maintenance, separate from hit execution](client-applydamage-stat-index.md).
- [Virtual ailment faster inputs and combined ailment DoT multipliers](client-ailment-multiplier-inputs.md).
- [Common, typed, ailment and non-ailment DoT multiplier input families](client-dot-multiplier-inputs.md).
- [Selected effect-source inheritance, fields, scripts and mode distinctions](client-effect-source-catalogue.md).
- [GroundEffect lifetime transport, termination, cancellation and conditional Buff clock guard](client-ground-effect-boundary.md).
- [GroundEffect cancellation-token producer, typed parent route and shared lifetime ownership](client-ground-effect-ownership.md).
- [Versioned DoT instance sampling, target dynamics and expiration rules](client-dot-instance-rules.md).
- [Complete damaging-ailment duration modifiers, condition gates and binary32 operation order](client-ailment-duration-inputs.md).
- [Bleeding/poison skill-duration coupling and duration-consumer literal classification](client-ailment-skill-duration.md).
- [Physical-defence estimates using reference damage, including Ward substitution](client-defence-estimates.md).
- [Concrete Fireball/Melee contact production, avoidance and entropy](client-hit-production.md).
- [Default ranged-action selector, equipment inputs and startup registry winners](client-ranged-action-selection.md).
- [Winning action extensions: attached effects and zero-time projectile updates](client-action-extensions.md).
- [Conditional DefaultArrow receivers, component updates and Entity task container](client-projectile-extension-receivers.md).
- [Entity task factories, ownership, selected timers and external subscriptions](client-entity-task-boundaries.md).
- [Selected action-start task: AnimationController publication and slot-zero listener](client-entity-task-listener.md).
- [Buff lifecycle/quantity listener: concrete Ruin count effect/audio responsibility](client-entity-task-ruin-listener.md).
- [Hit queues, action contexts and control effects](client-hit-feedback.md).
- [Four captured-program Hit listeners, Actor subscriptions and asymmetric script-context inputs](client-hit-script-listeners.md).
- [One actual packaged Hit-listener program, typed arguments and named-task cleanup](client-hit-resource-programs.md).
- [Received-result critical feedback, direction and current-target control admission](client-hit-result-qualifiers.md).
- [Actual missing-action result processing, independent resource assignment and sequence-derived current-ES stun admission](client-hit-result-resource-composition.md).
- [Original release/result processing, nested World checkpoints and independent pool assignments](client-hit-checkpoint-resource-composition.md).
- [Current Fireball Action lookup, final-table callbacks and typed event cleanup](client-hit-action-present-composition.md).
- [Hit/resource receiver search and its precise coverage](client-hit-resource-boundary.md).
- [Predictive/lockstep scheduling and resource correction](client-damage-timing.md).
- [Versioned official/community explanations and PoB comparisons](client-damage-comparisons.md).
- [Pinned PR #373 reports, independently checked fields and skill-stat updates](client-damage-pr373-comparison.md).

The parallel projectile investigation is reused through a file-based ownership/evidence ledger rather than
duplicate scans. Its fixed
[local hit prediction chapter](https://github.com/LocalExile/LocalExile/blob/37a764b0a72a64aad721e770366f6936dff9ffa9/Agents/docs/re/projectiles/local-hit-prediction.md)
binds target Stats entropy/reset state, its nine-byte creation trailer and selected spell identity-derived trials.
The shared snapshot's 51 files, including 38 exact Git sources, were verified; its retained 14 functions,
20 ranges, 1,195 instructions, 14 slots, four constants and 17 anchors also matched this investigation's pinned PE.
This is reuse of the other investigation's result, not a new discovery or closure of numerical Hit resolution.
Projectile action/RNG/contact/World/result ingress stays with that owner; this investigation follows the
post-result numerical/resource boundary, Buff/DoT sources and calculation formulas.
That snapshot leaves full accuracy/evasion composition and its native power helper open with that owner.

The later fixed object-type binding report (`Agents/docs/re/projectiles/object-type-binding.md`,
peer revision `fbb751a0caed81c6b43b1331884a1917a833098b`, locally retained and not yet available on GitHub)
connects selected projectile rows, generated/source metadata and ordered component construction.
Its 63-file snapshot, including 48 exact Git sources and parent-manifest hashes, was checked before reuse;
its numerical audit was not rerun. World dispatch remains with that owner, while resource-side saved-rate
and later-state reads remain in this investigation. Neither boundary closes numerical Hit or DoT application.

## 2. Observed graph and missing connections

Solid edges below represent verified local data/control dependencies.
Dashed edges are unresolved bindings, not an assumed end-to-end execution path.

```mermaid
flowchart TD
  SKILLDATA[RF stat-set constants] --> RFPARAM[Native parameters / FireDot eligibility]
  RFPARAM --> RFOUT[Conditional outgoing RF area rate]
  BASEADDED[Base / added components and conversion history] --> AILMENT[Separate bleed / ignite / poison outgoing rates]
  SPELLCOMP[Spell base / selected-added components] --> SPELLFINAL[Twelve final spell min / max / total equations]
  SPELLFINAL -. actual hit sampling remains separate .-> HITCALC
  ATTACKCOMP[Hand base / selected-added components; separate scaling] --> ATTACKFINAL[Twenty typed attack ranges and four totals: ordered equations]
  ATTACKFINAL -. hand selection / actual sampling remains separate .-> HITCALC
  AILMENT -. instance application / selection remains separate .-> RAW
  CRITINPUT[Chance / extra-roll / critical-multiplier stats] --> CRITSTAT[Critical probability and multiplier calculations]
  RFOUT -. source application / aggregation unresolved .-> RAW
  TEMPLATE[Named RF template: declared fire-rate magnitude] -. active instance publication unresolved .-> RAW
  SOURCES[DoT sources / effect instances] -. producer unresolved .-> RAW[Base rates per minute]
  NATIVEREADY[Supplied eligible native context / ready inputs] --> NATIVEQUEUE[Original dependency queue: 105 DoT outputs]
  NATIVEQUEUE --> AS
  RAW --> AS[Damage taken as: typed rates]
  AS --> MIT[Per-type DoT mitigation and immunity]
  MIT --> ROUTE[ES bypass / Mana-before-Life / healing branches]
  ROUTE --> FINAL[Final Life / Mana / ES rates]
  FINAL -->|selected native-provider composition| NATIVEPOOL[Original Life / ES numerical loss]
  NATIVEBIND[Supplied native provider binding / eligibility] --> NATIVEPOOL
  NATIVEPOOL --> POOL
  FINAL -. per-actor source inputs and native eligibility unresolved .-> TYPE[Upstream type stats]
  TYPE --> GET[Network Stats getter: fallback only if key absent]
  NET[Creation pairs / S→C 0x1b3] --> OVERRIDE[Sorted explicit key-value overrides]
  OVERRIDE --> GET
  SKILLNET[S→C 0x1b4: skill / stat set / key-value pairs] --> SKILLOVERRIDE[Selected network skill overrides]
  SKILLOVERRIDE --> PANEL[Bound numerical skill-panel readers]
  GET --> TICK[Life component: integrate rate / 60 × dt]
  OVERRIDE -->|selected recharge-active notification| RECHARGESTATE[Clear positive recharge delay and suppress reset]
  RECHARGESTATE --> RECHARGECTRL[Pool recovery controller: local delay / active proposal]
  RECHARGECTRL --> ACTIVEPUBLISH[Installed client active-state publisher: exact no-op]
  RECHARGECTRL -->|reads separate total recovery rate| TICK
  TICK --> READORDER[Captured rate versus later state; Life sequential / ES preloaded loss rates]
  READORDER --> CHECKPOINT[Peer-established World component checkpoint / eligible parsed record]
  CHECKPOINT -->|0x1b1: actual dispatch and numerical composition| SETTER
  TICK --> POOL[Integer current + fractional carry + resource overflow]
  TICK -->|selected Life degen channels| LIFELOSS[Reason-4 Life-loss operation]
  BUFFVALUES[Eligible actual Buff first granted value] --> ABSORBCAP[AncestralTrialTotemAbsorbDegen backend capacity]
  ABSORBCAP -->|consume capacity / can skip loss| LIFELOSS
  LIFELOSS -->|fall-through uses original delta and later policies| POOL
  ABSORBCAP -->|exhaustion| ABSORBEND[Clear capacity and actual Buff remaining duration]
  BUFFVALUES --> BUFFFORECAST[Eligible actual Buff value times remaining duration: recovery / deferred-loss forecasts]
  BUFFFORECAST --> POOLINDICATOR[Resource-indicator preparation]
  BUFFSOURCE[Actual Buff definition keys / target defence gates] --> FIRSTSOURCE[First eligible stored Buff; no DPS comparison]
  FIRSTSOURCE --> LIFEORIGIN[Life callback source context when explicit source is absent]
  CORRECTION[S→C 0x1b1 decoded resource value] --> SETTER[Absolute pool assignment]
  SETTER --> POOL
  SETTER --> NOTIFY[Guarded pool notifications / recharge-delay changes]
  HIT[Fireball / Melee / Sand Spitter contacts] --> AVOID[Concrete target dodge / evasion / projectile avoidance]
  EQUIP[Decoded equipment-visible records] --> WEAPON[Subscribed Actor refresh / weapon-state provider]
  WEAPON --> SELECT[Default Bow/Wand action variant]
  STARTUP[Ordered registry replacement] --> SELECT
  SELECT --> HIT
  SELECT --> ARROW[Conditional DefaultArrow resource / Entity / same-table action clone]
  ARROW --> EXTEND[Retained projectile: zero-time component and task updates]
  EXTEND --> COMPONENTS[Five concrete component update slots]
  EXTEND --> TASKS[Typed Entity-owned task container]
  TASKS --> TASKFACTORY[Local / serialized factory pair and owned registration]
  TASKFACTORY --> TASKCALLBACK[Selected task countdowns / secondary subscriptions]
  TASKCALLBACK --> TASKANIM[Selected c944: AnimationController publishes to secondary slot zero]
  TASKCALLBACK --> RUIN[Selected d9aa: Buff quantities to Ruin count effect / audio]
  AVOID --> CONTINUE[Action-specific continuation: filtered feedback / bookkeeping / empty slots]
  AVOID -. amount and critical calculation unresolved .-> HITCALC[Numerical damage / mitigation / routing]
  HITCALC -. pre-deduction unresolved .-> POOL
  FEEDBACK[S→C 0x1af: decimal 431] --> ACTOR[Actor result signals]
  ACTOR --> RENDER[Render / sound / effects callbacks]
  ACTOR --> HITPROGRAM[Four captured-program tasks: Entity / particle-label / mask-bit context]
  HITPROGRAM --> EVENT
  FERROCONFIG[Ferrofluid client attachment script] -->|AddOnWhenHit / named task| HITPROGRAM
  EVENT -->|selected Ferrofluid captured block| FERROPACK[Remove and add the same hit EffectPack]
  FERROCONFIG -->|detach| FERROCLEAN[Remove pack / request named-task cleanup]
  ACTOR --> ACTIONRESULT[Sequence-matched current Fireball Action: supplied state]
  ACTIONRESULT --> ACTIONNOOP[Actual two no-op Action callbacks]
  ACTIONRESULT --> ACTIONEVENT[Actual typed event arguments and cleanup: empty definitions]
  ACTOR --> QUALIFY[Current target admission: mutable control mask / duration]
  QUALIFY --> CONTROL[Interruption / knockback / action callbacks]
  ACTOR --> EVENT[Event interpreter]
  NOTIFY --> EVENT
  CORRECTION --> LIFETRANS[Life selector compares old / assigned current]
  LIFETRANS --> CLOCKGUARD[Selected sign transitions: set / clear Buffs advancement guard]
  CLOCKGUARD --> CLOCK[Definition-gated duration / backend tick skip]
  BUFFNET[S→C 0x1c1 existing Buff update] --> BUFFCLOCK[Copy remaining duration / speed; omitted speed = 1]
  BUFFWIRE[0x1c1 writer: actual granted-vector length; no count] -->|only definition IsRecovery nonzero| BUFFDECODE[Reader: definition GrantedStats count / network-order words]
  BUFFWIRE -->|IsRecovery zero: no granted words| BUFFZERO[Definition-sized zero values: ignite / poison / RF witnesses]
  BUFFDECODE --> BUFFVALUES
  BUFFCLOCK --> CLOCK
  BUFFNET -->|replacement selector| BUFFREPLACE[Actual temporary Buff construction and selected field transfer]
  BUFFREPLACE --> BUFFRETAIN[Retain object identity and selected initialization fields]
  BUFFREPLACE --> BUFFCLOCK
  FWCAPTURE[Flame Wall backend construction: capture remaining duration] --> FWHOLD[Selected callback: nonzero 12588 restores captured duration]
  CLOCK -->|eligible Flame Wall tick after decrement| FWHOLD
  GET -->|current owner value 12588| FWHOLD
  FWHOLD --> FWREMAIN[Same Buff remaining duration; stored speed untouched]
  BUFFDEF[Ignited / poison definition: empty Id2] --> BUFFNULL[Selected bootstrap lookup miss: null backend]
  BUFFNULL --> BASEEVENT[Base lifecycle still notifies observers and scripts]
  BASEEVENT -. numerical publication unresolved .-> RAW
  STATVALUE[Eligible native stat contribution / formula publication] --> APPLYINDEX[Flagged Stats to sorted ApplyDamageFunctions indices]
  APPLYINDEX -. function execution not recovered .-> EFFECTS
  REFDAMAGE[Character-sheet / monster reference damage] --> DEFEST[Estimated physical reduction: armour or Ward]
  DEFEST --> CATEGORY[Bound value-to-category reader]
  DEFCOEFF[Taken-as coefficients / armour applicability formulas] -. actual hit consumer remains separate .-> HITCALC
  EVENT -. full script and callable consequences unresolved .-> EFFECTS[Context-dependent effects]
  NATIVE[Registered ClientOnlyKillSelf callable] --> ZERO[Context Life: absolute zero if positive]
  ZERO --> POOL
  ZERO --> NOTIFY
  ZERO --> CLOCKGUARD
```

The DoT formula graph and the network pool integration graph are both real static findings.
The upstream getter and separate formula publication mechanism are now identified below.
The ordinary network Actor/skill provider chain is bound separately in the stat-context supplement.
The dashed rate edge still requires source-effect inputs and eligibility on the relevant native object;
network getters can instead read an explicit received final rate.
It is incorrect to present this drawing as proof that the client simulates every source or hit independently.
The solid contact/avoidance branch has concrete action tables and actual target providers;
it is separate from both deterministic panel estimates and received hit feedback.
The RF formula branch is conditional native computation from recovered skill parameters.
It does not close the separate effect-instance/application edge into an enemy's incoming base rate.

## 3. Stats interfaces: explicit overrides and upstream calculations

### Network override provider

`0x1424cfa40` searches sorted eight-byte `(key, value)` pairs at provider `+0xf0..+0xf8`.
An exact match returns its value, **including an explicit zero**, at `0x1424cfaa5`.
Only an absent key falls back through the interface at `+0xe0`; a null upstream returns zero.
The fallback is a virtual getter tail jump at `0x1424cfabc`.
This is an override lookup, not addition of a server value to a locally computed value.

`0x1424cfaf0` replaces an existing pair or inserts a new sorted pair.
It dispatches the changed key through `0x1424cfd00`.
That notification is not proof of complete transitive invalidation of every derived-stat cache.
Creation pairs (`0x1421c2f40`) and S→C `0x1b3` use this setter.
See [the wire format](../protocol/10-chat-and-commands.md#3-sc-0x1b3-stats-update).

The binding to real components is not just a table guessed from a pointer occurrence:

1. `0x1421c2ad0` installs the ClientInternalStats component table `0x1435dd4e8`.
2. It constructs a shared wrapper through `0x1421c3700` and stores the wrapper at component `+0x20`.
3. The wrapper installs table `0x14363bd90`, whose getter slot is `0x1424cfa40`.
4. The wrapper's `+0xe0` upstream comes from the type object's shared pointer at `+0x10`.
5. This constructor supplies flag `1`, skipping the optional upstream subscription block.
6. The Life constructor binds its Stats component at `Life+0x168`.
   Pool steps load `pool+8 → Life+0x168 → Stats+0x20` and call that interface's getter.

### Type fallback and separate update/evaluation transaction

The type binding is now resolved beyond the shared-pointer field:

```text
ClientInternalStats type factory 0x142182790
  → base type constructor 0x141e40560
  → shared stats constructor helper 0x141e496c0 at type+0x10
  → native stat object constructor 0x1424fb790
  → installed getter table 0x14363d3b0, slot 0 = 0x1424fc1e0
```

Consequently a missing network override reads the type stat object's stored records.
The getter itself does not dispatch a formula.
However, it would also be incorrect to conclude that these records can never contain computed values.
A separate update transaction can evaluate formulas and publish their results into those same records:

- `0x1424fd240` begins/nests an update and creates an evaluation context at object `+0x190` when needed.
- `0x1424fc480` adds a source contribution to a stat record and tracks affected values.
  The type's `set_...` parser, `0x141e406e0`, uses it.
- `0x1424fd390` ends an update; at the outermost boundary it processes the affected evaluation entries.
  One dispatch call is `0x1424fd4c3 → 0x1424fcc80`.
- `0x142513630` registers the callback at registry `0x1445c6ec8 + key * 0x50`.
  It also records dependencies and their reverse edges.
- `0x1424fcc80` checks the entry's eligibility, calls the registered function at `0x1424fccf8`,
  updates the evaluation cache and writes changed output into record `+8` at `0x1424fcdb3`.
  It also queues notifications/affected values through `0x1424fcec0` and related helpers.

This closes **registration → transaction dispatch → result publication → getter** as a conditional static mechanism.
The constructor `0x141e40560` itself brackets type initialization with `0x1424fd240`/`0x1424fd390`.
The ordinary network Actor/skill provider chain and its actual update contract are now closed in
[stat evaluation](client-stat-evaluation.md).
Network skills inherit the owner's network wrapper, with explicit per-skill pairs and additional-set wrappers.
The getter reads current pairs/current upstream, while notifications mask overridden keys and forward inherited changes.
The audited setter/forwarder/dispatcher does not enter a native dependency transaction.
Successful client startup sets `0x1445b38cd` to one; action-local native context mutation is skipped under that guard.
Native formula eligibility and source-effect instance production remain separate from this network lookup contract.
Network wrapper setters and native contribution setters are different paths and different objects.
The audited network setter stores its pairs and notifies its listeners; it does not directly enter this transaction.
Do not infer that sending one raw network input automatically makes every omitted derived output recompute.

### Separate raw-stat and derived-stat machinery

The formula callbacks use the native storage and evaluation layout rather than the network pair vector.
`0x1424fc1e0` checks valid keys `1..0x5b32`, a presence bitmap at `+0x268`, and sorted 40-byte records at `+0x1f0`.
The raw value is at record `+8`; its parent is at object `+0xf0`.
Do not confuse this parent pointer with the network wrapper's vector at the same offset.

The callbacks also use cached values and validity bits in an evaluation context.
`0x142513630` is a registration entry point; the selected registration functions bind a stat key to a callback.
For example `0x1400ac590` binds key 2022 to `0x1426cce60`, and `0x1400ad7c0` binds key 3356 to `0x1426da000`.
The transaction dispatcher provides an actual conditional callback invocation, beyond registration alone.
Specific actor/skill inputs and transaction guards still determine which outputs are evaluated and when.

## 4. DoT: raw rates, taken-as and mitigation

### Rate units and input boundary

These stats express a **rate in damage points per minute**.
The Life pool code turns a rate into an increment with `rate / 60 * dt`.
There is no observed elapsed-minute counter or minute-boundary reset in the audited path.
The word "minute" describes the numeric unit, not a rolling or resettable damage budget.

| Output key / Id | Callback | Verified input meaning |
|---|---|---|
| 2022 `fire_damage_taken_per_minute` | `0x1426cce60` | Forwards 2023 `base_fire_damage_taken_per_minute` |
| 1864 `physical_damage_taken_per_minute` | `0x1426cc060` | Base physical rate plus a per-talisman-stack contribution |
| 352 `chaos_damage_taken_per_minute` | `0x1426cf780` | Base chaos rate plus a per-viper-orb contribution |
| 8179 `physical_damage_taken_per_minute_unaffected_by_level_scaling` | `0x1426cc330` | [Complete Life/ES, bleeding/Rupture and siphoning composition, with a post-saturation raw tail](client-physical-chaos-self-rates.md) |
| 8183 `chaos_damage_taken_per_minute_unaffected_by_level_scaling` | `0x1426cfa50` | [Complete Life/frenzy, self, flask, converted bleeding/ignite and poison composition](client-physical-chaos-self-rates.md) |
| 9563 `total_base_damage_per_minute_taken_as_physical` | `0x1426d3820` | Typed taken-as inputs, before physical mitigation |
| 9564 `total_base_damage_per_minute_taken_as_fire` | `0x1426d40f0` | Remaining fire plus physical/cold/lightning/chaos taken as fire |
| 3355 `total_physical_damage_taken_per_minute` | `0x1426d9550` | Physical reduction, taken modifiers, immunity/prohibition |
| 3356 `total_fire_damage_taken_per_minute` | `0x1426da000` | Fire DoT resistance, reduction, taken modifiers, immunity/prohibition |

The forwarding callback for fire does not contain an ignite list, RF radius or a duration/snapshot model.
The producer of base rate 2023 is still unresolved.
[Native queue composition](client-dot-native-composition.md) now executes 105 selected registered outputs together,
including live resistance/prohibition input changes, taken-as saturation and resource routing.
It supplies eligibility and ready inputs; it does not create a source instance or bind ordinary actor publication.
[The native-record continuation](client-native-dot-pool-composition.md) removes the previous-value getter hook
then carries these outputs through real native getter/boolean slots into Life/ES numerical loss, with an explicit provider binding.
Separately, an outgoing RF chain is now established: stat-set constants 883/1947/3399 feed
14408, then area-skill intermediate 8198 and final outgoing area rate 5981. It uses skill-user
Life/ES and mana providers, distinct modifier stages and rounding boundaries. Generic outgoing
fire rate 1999 has a different intermediate; no direct `14408 → 1999` edge was found in these bodies.
See [the outgoing RF formula bridge](client-dot-routing.md#outgoing-righteous-fire-formula-bridge).
These conditional native formulas do not establish a source-instance snapshot, nearby-target
application, or publication of the result into incoming rate 2023.
One investigated candidate, factory `0x1412c40e0`, passes immediate `0x7e7` (2023) through action constructors.
The base action constructor `0x141937cc0` stores it as a 16-bit value at action `+0x118`.
This is **not a verified stat setter**, even though the integer matches the fire stat key.
It must not be named an RF/base-fire-rate producer without further data flow.

### Fire DoT worked formula

The following is a readable algebraic summary of `0x1426da000`, not a bit-exact port.
Individual operations use binary32 arithmetic; the executable's rounding and cache boundaries also matter.

```text
I(x) = max(0, (100 + x) / 100)
K = (1 - fireDoTResistance / 100)
    * I(virtual_fire_damage_taken_over_time_+%)
    * I(fire_damage_taken_over_time_+%_final)
    * I(hellscape_monster_damage_taken_+%_final)

rate = K * (1 - additional_elemental_damage_reduction_% / 100)
       * (total_base_damage_per_minute_taken_as_fire * I(levelTakenModifier)
          + fireRateUnaffectedByLevelScaling)
```

The callback returns zero for `cannot_be_damaged` (19670), `cannot_take_damage_over_time` (15468),
or `fire_damage_immunity` (4522).
The paired stopped-taking-fire-DoT flags 19157/19158 also select the zero branch.
The two base inputs are keys 9564 and 9569; the level modifier is key 8173.
The `max(0, ...)` helper at `0x14013c620` was checked in original instructions.
`virtual_fire_damage_taken_over_time_+%` (10737), callback `0x1427d4bd0`, combines fire/DoT/burning taken increases.

The fire DoT resistance evaluator `0x14271a3f0` (17170) has concrete special branches:

- Key 18912, `no_fire_resistance_to_dot`, returns zero.
- Key 18064 returns zero while key 20408, `virtual_is_moving`, is false.
- Otherwise clamp uncapped fire DoT resistance 17171 to at least -200 and to the selected maximum.
- The maximum is key 146, or highest maximum elemental resistance 15619 when flag 15620 is active.

There is no additional hard-coded 90% clamp in this callback.
Whether its maximum input already includes that rule is a separate upstream question.
Hit penetration is not an input in this audited fire DoT callback.
That observation does not by itself establish all official-server DoT rules.

### Saturation is present at multiple stages

The taken-as-fire callback saturates its output before the fire mitigation callback runs.
The fire mitigation callback saturates again, and selected resource aggregates saturate again.
At `0x1426dab9d`, the fire callback returns `0x7fffffff` after comparing against binary32 `2^31`
from `0x1436a48a8`; smaller results use signed half-point adjustment and truncation, then nonnegative clamping.
The threshold constant is not the exactly represented integer `2^31 - 1`.
Do not silently replace native float edge behavior with ideal real-number arithmetic.

`(2^31 - 1) / 60 = 35,791,394.1166...` damage per second explains the familiar magnitude.
It does **not** prove one universal final `min(sum effective DPS, cap)` operation on the official server.
The audited client has intermediate caps, and some later aggregates use ordinary 32-bit additions.
For example the ES overflow ratio uses `lea eax,[rcx+rbx]` at `0x1426e787a` before signed conversion to float.
That operation is not a saturating or widened sum.
The valid range and reachability of overflow cases require further research.

## 5. DoT resource routing and local integration

`0x14281bd60` computes fire ES bypass (9609).
It returns 100 for key 9587 (`keystone_divine_flesh`) or 9586 (`keystone_miracle_of_thaumaturgy`).
Otherwise it clamps non-chaos bypass 9677 to 0..100.
The presence of those branches is verified; their full interaction with every resource is not yet established.

The typed routing chain distinguishes ES-bypassing damage, damage eligible for ES, Mana-before-Life and healing.
The next work item is to re-audit each route, rather than infer it from its stat name.
The audited aggregate functions already show separate resource totals:

| Key | Callback | Observed role |
|---|---|---|
| 9595 | `0x1426e53f0` | Sum of six Life-directed bypass channels, with float saturation |
| 3361 | `0x1426e7410` | ES rate; selection includes `virtual_energy_shield_protects_mana` (5338) |
| 3362 | `0x1426e7d60` | Life rate; complementary selection using 5338 |
| 3364 | `0x1426e7fc0` | Nonlethal Life aggregate; includes a plain integer addition |
| 9960 | `0x1426e7670` | ES overflow share to Mana, permyriad, from Life/Mana-directed ES channels |
| 9961 | `0x1426e7b00` | Corresponding nonlethal overflow ratio |

`0x141e6a190` steps Life, ES and Mana when its life/alive, world and spectating guards permit it.
The time argument is forwarded in floating-point registers even where Ghidra omits it from the signature.
The pool steps are `0x141e6ae70`, `0x141e6b480` and `0x141e6cba0`.
They read continuous loss/rate stats and multiply by the step duration; recovery uses `0x141e6b320`.
For ES, total damage 3361 and nonlethal damage 3363 are distinct from generic ES loss 12609.
The reason codes passed to the reducers also differ.
Do not merge spending, generic loss and damage solely because they all change a pool.
The separate [integer pool interface](client-integer-resource-mutations.md) binds concrete cost and arrow-sacrifice
callers, their reasons and conditional payment gates; it does not close accepted-Hit numerical deduction.

The common loss reducer `0x141e6a6b0` uses integer current at pool `+0x30` and fractional carry at `+0x28`.
It adds the signed delta to the fraction, truncates the whole part, updates current, and retains the remainder.
When the pool is depleted it returns unabsorbed negative remainder in **XMM0**.
The float return is established by the raw instructions; Ghidra's recovered `void` signature omits it.
Current and fractional writes are at `0x141e6a6f9` and `0x141e6a707`.

ES reducer `0x141e6a780` routes negative overflow for reason 1.
With 5338 active it goes to Mana; otherwise ratio 9960/9961 divides it between Mana and Life.
Mana reducer `0x141e6ae00` can spill its negative remainder to Life.
Life reducer `0x141e6a8a0` has cannot-die/nonlethal/reason-6 floor-one branches.
These are resource transitions, not proof that the client creates fresh hits on spill recipients.

The Life virtual slot `+0xe0`, `0x141e6a370`, reads spending rates 15045..15048.
It is **not** a discovered incoming-hit resolution function.

## 6. Incoming resource assignment and what "correction" means

S→C `0x1b1` reaches the world handler `0x142187480`.
Its Life/Mana/ES branches call `0x141e69fb0` at `0x1421880e6`, `0x1421880cb` and `0x1421880b7`.
The setter copies decoded message current at `+0x20` to pool `+0x30`, reserved at `+0x24` to pool `+0x10`,
and the percentage-reservation input at `+0x2a` to pool `+0x14`.
The current-value assignment is at `0x141e69fd1`.
Those message offsets are **decoded in-memory fields**, not raw packet offsets.
See [Life serialization](../protocol/04-player-entity.md#44-life-0x141e6c2f0-55-bytes).

The setter assigns a total rather than applying a damage delta.
It does not itself write fractional carry at `+0x28`, rewind a simulation or replay an input history.
The surrounding Life-only nonpositive-to-positive branch does clear that pool's carry at `0x142188129`;
this is distinct from an ordinary assignment.
Calling this an authoritative-value correction describes the assignment, not an established rollback algorithm.
No official-server correction cadence has been measured by this static work.
The setter also dispatches guarded change/full/empty/loss notifications; loss notification can update recharge
delay and enter the event system. See [assignment consequences](client-damage-timing.md#assignment-notifications-and-recharge-delay).

Historical LocalExile recovery probes observed native pool changes between server corrections.
Those traces support continuous resource extrapolation for the supplied rates.
They are not evidence of independent hit prediction or official-server formulas.
They also measured about 120 Life ticks/s in the foreground and about 10/s in the background.
A `0.033` constant in `0x1428548a0` is used in an ES threshold estimate, not proof of a 33 ms Life scheduler.
Keep client rendering/update time, protocol frame length and server logic cadence separate.

## 7. Hit investigation: established feedback, unresolved numerical prediction

Decimal 431 is S→C `0x1af`, not the Life-total packet `0x1b1`.
Its newly audited [reader and conditional fields](../protocol/29-client-combat-signals.md) have a separate protocol page.
The previously retained, same-SHA static callback audit establishes this conditional path:

```text
Actor dispatcher 0x1421940a0 / consumer 0x1421952a0
  → 0x141e90f50 → Actor virtual slot +0x230 (0x141e91580)
  → Actor source signals +0x2e8 / +0x368 and target signal +0x3e8
  → registered Render / sound / effect callbacks
```

Render constructor `0x1415f5d50` registers `0x141600c10`, `0x141600aa0` and `0x141609f40` on those signals.
The `+0x368` callback prepares `Audio/Sound Effects/WeaponSwinging/MissSG`.
This does not establish an exhaustive miss/block taxonomy or a numerical accuracy roll.
The confirmed receiver is Render, not Life.
No numerical pre-deduction edge from those audited feedback callbacks to a pool reducer has been established.
The historical callback audit is distinguished from the freshly decoded function ranges in the evidence manifest.

The wider result consumer also drives timed interruption, knockback and action callbacks, as detailed in
[Hit feedback](client-hit-feedback.md). Named Fireball and Melee registrations now bind their concrete
`+0x2d0/+0x2d8` targets; Melee has a nonempty test-effect callback in one registry mode.
Event `0x6876` reaches a 102-opcode interpreter with descriptor-selected native callables.
Those program/callable identities remain unresolved, so the whole result consumer cannot be classified as presentation only.
See [concrete action and event boundaries](client-hit-resource-boundary.md#7-concrete-action-callbacks-fireball-and-melee).

### Why a direct-call negative result is insufficient

A fresh executable-section E8/E9 scan of the four pool reducers finds continuous changes, overflow and spending.
It finds no independent direct-call hit calculation among those callers.
However, the reducers also occupy **virtual slot +0x48** in installed pool tables:

| Pool table | Slot +0x48 |
|---|---|
| `0x1435a5da0` | Life `0x141e6a8a0` |
| `0x1435a5b68` | Mana `0x141e6ae00` |
| `0x1435a6218` | ES `0x141e6a780` |

An additional instruction-boundary-checked FF /2 and /4 displacement scan found 667 candidate sites in 448 owners
for displacement `0x48`, plus 21 unowned candidate byte positions.
These are **generic indirect-call candidates**, not 667 calls to Life or proof of hit prediction.
Receiver provenance is required; most vtables are unrelated.
The scan excludes register-held targets, other prefix forms and unidentified leaf functions.
Selected candidates give concrete reasons not to identify every slot-`0x48` call as resource loss:

| Candidate owner | Observed receiver/data flow | Boundary |
|---|---|---|
| `0x141e8eb60` | Actor's current action at `+0x1b8`; boolean result controls action progression | Not a proved pool receiver |
| `0x141e35d30` | Service returned through world interface `+0x180`; passes owner and integer state | Not a proved pool receiver |
| `0x141eb5e10` / `0x141eb7ca0` / `0x141fd52f0` | Parse fields into the message object, then invoke its slot `+0x48` | Message continuation, not a proved pool receiver |

The global receiver analysis remains incomplete.
The result therefore is **unproven**, not "the client cannot calculate hits".

### Hit-related stat calculations are present

The freshly audited registrations bind the following outputs to native evaluators:

| Key / Id | Registration | Evaluator | Established calculation boundary |
|---|---|---|---|
| 670 `main_hand_minimum_total_damage` | `0x140096290` | `0x142543a80` | Adds the five typed minimum ranges |
| 279 `main_hand_minimum_physical_damage` | `0x1400982a0` | `0x1425733e0` | Base/added inputs, combined modifiers, conversion loss, integer output |
| 1563 `main_hand_total_minimum_base_physical_damage` | `0x1400995f0` | `0x142594860` | Weapon-local inputs including physical flat, local increase and quality |
| 1575 `main_hand_total_minimum_added_physical_damage` | `0x140099ed0` | `0x1425988a0` | Intermediate added ranges, damage-type replacement and minimum-damage modifier |
| 292 `main_hand_accuracy_rating` | `0x1400b2ab0` | `0x1426fb060` | Local/global/conditional accuracy inputs, including override from minion parent |
| 552 `main_hand_critical_strike_chance` | `0x1400bcb60` | `0x142756600` | Critical-link override 15067, otherwise intermediary chance 15066 |
| 676 `main_hand_chance_to_hit_%` | `0x1400c1830` | `0x1427854b0` | Spell/always-hit branches, accuracy 292 and sheet reference evasion 10745 |

These are concrete arithmetic/selection callbacks, not just strings found in a data file.
The physical minimum callback has separate inputs 1563 and 1575 before applying combined damage modifiers.
It also reads `active_skill_base_attack_damage_final_+permyriad` (6172),
`combined_all_added_damage_+%_final` (5761) and `physical_damage_%_lost_to_conversion` (1502).
Its native operation order and integer conversions must be preserved in any later formula reconstruction.
Recovering those inputs' complete provenance remains separate from identifying this callback.

### Main-hand physical minimum: arithmetic checkpoint

Let `S[k]` be the value read for key `k` through this evaluator's native stat/cache context.
Define `F(x) = max(0, (100 + x) / 100)` and `trunc0` as integer division toward zero.
For ordinary finite inputs without integer overflow, the readable structure of `0x1425733e0` is:

```text
if S[1733] != 0: return 0                         // deal_no_main_hand_physical_damage

baseFactor  = F(trunc0(S[6172] / 100))
addedFactor = F(S[5761])
rawMinimum  = S[1563] * baseFactor + S[1575] * addedFactor

increased = F(S[1426] + S[5762] + S[1430] + S[1432]
              + S[1434] + S[1436] + S[1438])
remaining = max(0, (100 - S[1502]) / 100)
more = F(S[1427]) * F(S[5103]) * F(S[1431]) * F(S[15983])
       * F(S[19089]) * F(S[1435]) * F(S[1437]) * F(S[1440])

minimum = nonnegative_round(rawMinimum * remaining * increased * more)
```

The increase inputs distinguish all damage, hit, attack, main-hand attack and their physical counterparts.
The final modifiers have separate all/hit/attack/main-hand/minimum-attack/physical scopes.
Their own evaluators determine how individual modifiers enter these combined values.
This is not a second instruction to reapply those individual modifiers.

Three original-instruction anchors matter beyond this algebra:

- `0x142573704..0x14257372f` implements signed integer division of key 6172 by 100 **before** float scaling.
  Its multiply/shift/sign adjustment implements truncation toward zero, not a direct float division by 10,000.
- `0x14257391d..0x142573944` scales the added and base inputs independently, then adds them.
- `0x142574936..0x142574969` selects signed half-point adjustment, executes `cvttss2si`, then clamps nonpositive output.
  This tail has no equivalent of the fire DoT callback's explicit positive saturation branch.

The actual code groups binary32 multiplications differently from this readable product.
Integer intermediate addition, out-of-range float conversion and rounding make this **not a bit-exact port**.
The native producer is now bound: `0x141ee8ef0` contributes the raw per-level `BaseMultiplier` field unchanged to 6172.
Its separate `DamageEffectiveness` field is divided by 100 toward zero before contributing key 891.
These producers run in audited temporary native type/skill contexts; ordinary network skill providers are different.
See [the producer and its callers](client-stat-evaluation.md#4-a-concrete-native-skill-data-producer).
Applying the formula to a specific network skill still requires that skill's actual received inputs.
This callback establishes a separated base/added calculation, not a complete single-hit resolution algorithm.

### Reference hit chance and remaining consumer boundary

The hit-chance evaluator passes accuracy and **`character_sheet_reference_evasion`** to `0x1424ff510`.
That is a reference-target estimate, not a lookup of the concrete defender's evasion.
The helper returns a float in XMM0 even if decompilation guesses another return type.
No RNG call, per-defender resource mutation or complete critical confirmation roll was established in these callbacks.
They support the conclusion "client contains offence/stat calculation"; they do not close hit pre-deduction.
The [offence supplement](client-offence-estimates.md#6-actual-numerical-ui-consumers-and-their-provider)
now closes the numerical average/DPS readers and their network-provider identities.
Those readers remain distinct from contact resolution. Other identified UI calls construct localized labels;
the presence of key 670/671 in a constructor is not itself a numeric-consumer binding.

### Concrete contact and defender decisions

The [contact production supplement](client-hit-production.md) closes a separate positive path:
Fireball's final action table binds its flight callback to a grid query, candidate-target filtering,
and the target's spell-dodge/projectile-avoidance checks. The selected Melee table binds range/line
tests to actual accuracy/evasion inputs and a target-Stats entropy update. These are conditional
local calculations, not reference-target panel estimates or just received result flags.

Their continuations still matter. Variant-0 Melee's tested-contact `+0x530` slot is empty on its final
table; Fireball's corresponding slot invokes the already established queued-result consumer.
Other inspected Melee follow-on slots likewise consume existing feedback or return without work.
These concrete boundaries do not establish every action's behavior, and a mode check controlling
alternate-arrow MTX cannot be generalized into a prohibition of local target rolls.

No numerical damage amount, critical confirmation, defender mitigation or typed resource-pool
deduction has been closed from those accepted contacts. The open edge now starts **after** a
verified contact/avoidance decision, rather than treating all local hit logic as missing.

The event-script boundary also has positive and excluded paths. GEAL's ordinary client-mode
compiler rejects the specifically recovered server-only `CalculateDamage`, resource/stat mutation
and Buff-add metadata entries. Its accepted metadata-only placeholder returns an empty value vector.
However, concrete native entries can read current Life, set a resolved Life pool to zero through
`ClientOnlyKillSelf`, and register Life listeners that execute supplied programs through the same VM.
The zero assignment is conditional on invoking that callable; it is not a contact-derived damage amount.
These exact boundaries, and the remaining selected-event
program identity, are documented in the [resource receiver supplement](client-hit-resource-boundary.md#10-geal-callable-construction-and-clientserver-eligibility).
They do not justify classifying the whole interpreter as presentation only.
The same supplement binds the event tables to actual Actor configuration script-parser producers
and their compiler. Fresh bundled-resource checks find no such custom-event field in the selected
Ranger/Witch/Sand Spitter inheritance chains. Those source files therefore supply no `0x6876`
program to recover; other types and dynamic configuration remain separate. Neither this selected
resource result nor the earlier bounded cache search excludes a numerical damage path elsewhere.

A concrete packaged program is now bound separately: the
[Ferrofluid armour controller](client-hit-resource-programs.md) declares a client attachment handler using
`AddOnWhenHit(actions, false, FerrofluidWhenHit)`. The block requests removal/addition of one render EffectPack;
detach requests removal of the same named task. Its full selected parent chain, native argument grammar and
task-name lookup are verified. This closes one real resource-program edge without supplying a numerical Hit
amount or classifying every listener program as presentation-only.

## 8. Research workbench and completion criteria

Tracking issue: [#370](https://github.com/LocalExile/LocalExile/issues/370).

Research milestones measure evidence closure, not implementation coverage.
An agent should choose an open row, record the exact artifact and search boundary, and update this page and manifest.
Do not mark a row complete based on a matching stat name or a decompiler-only call signature.
No client execution is authorized by this task list.

Prioritize the missing numerical rules and authority contracts below. Supporting presentation investigations
stop once their relevant input and output category are positively identified. Full nested rendering, every
possible task installation and current official-server bit parity are not additional R0–R4 completion gates.
Do not turn this boundary into an absence claim: a bounded presentation consumer says nothing about other
damage-bearing paths. Keep missing source eligibility/selection, formulas and synchronization rules explicit.

| Milestone | Task | State | Next evidence / completion criterion |
|---|---|---|---|
| R0 | Pin artifact, table keys and instruction boundaries | Established | Re-audit when either input hash changes |
| R1 | Network Stats, actor/skill inputs, lookup freshness and native publication | Established | Actual network provider chain, updates/notifications and guarded native boundary audited; [details](client-stat-evaluation.md); subsequent skill update identified as [0x1b4](client-damage-pr373-comparison.md#0x1b4-is-a-skill-stat-update) |
| R1 | Native dependency ordering and work-vector algorithm | Established for audited helpers and recovered declaration graph | [Cycle-checked traversal, rank rewriting, growing ordered dispatch, unchanged-output propagation stops and 24 actual duplicate-containing declarations](client-stat-dependency-order.md); declared edges do not guarantee all actual reads or arbitrary reentrant callbacks |
| R1/R2 | Selected native DoT numerical composition | Established for supplied eligible/cache-ready states | [105 original registrations and actual dependency queue/callbacks](client-dot-native-composition.md); eight dynamic/cap/routing cases. Actual source publication, ordinary native eligibility and shared-cap reachability remain open. |
| R2/R4 | Native DoT records to numerical pool loss | Established for supplied native-provider states | [Actual native previous-value/boolean getters and Life/ES loss bodies](client-native-dot-pool-composition.md); 1,995 record/cache comparisons and 24 pool steps. Provider selection/eligibility, instance publication and whole-actor execution remain separate. |
| R1 | Absolute pool update path | Established | Preserve decoded-field vs wire-offset distinction |
| R1/R2 | Native contribution caller provenance | Established for the retained 103 direct calls in 13 owners | [Receiving-object map and graph-owned source tokens](client-native-contribution-callers.md); source replacement can remove before rebuilding. This finite inventory does not close target DoT publication or cover indirect/inlined mutation. |
| R1/R4 | Maximum-stat notification and current-resource authority | Established for three pool keys and two client World tables | [Actual Life subscriber and pool refresh](client-pool-maximum-refresh.md) update maxima without proportional current changes on the two nonzero client getters; Life alone clears carry; conditional rescaling code and notification limits remain explicit |
| R1/R4 | Recharge notifications, local delay and integrated-rate authority | Established for selected three-pool paths | [Actual active/block subscribers and recovery controller](client-recharge-rate-authority.md): positive-delay cancellation, reset suppression and ordered loss; installed active-state publication is a no-op, while recovery independently reads total rate; external event listeners remain separate |
| R1/R4 | Ordered parsed resource assignment inside recovery | Established for selected composition | [Peer-owned World checkpoint and 0x1b1 dispatch run through actual setter and subsequent recovery](client-resource-checkpoint-composition.md); Life's additional checkpoint changes when a pending assignment can affect this gain; parsed queue/lookup/provider inputs are fixtures and wire/official scheduling remain separate |
| R1/R2/R4 | Received rate to numerical Life loss | Established for selected compositions | [Parsed 0x1b3, actual network getter/setter/notification and full Life integration](client-network-rate-composition.md); [initial component bytes](client-initial-stats-rate.md) additionally run both original compact readers through that store and subsequent Life loss. Extra full-step checkpoint permits later rate application, while captured recovery stays old. Raw fire input does not replace an explicit total-rate override; actual source selection/publication and official sends remain open |
| R2/R4 | Buff expiration/removal versus explicit incoming-rate withdrawal | Established for selected supplied-state compositions | [Actual parsed 0x1c2 removal, empty lifecycle/destruction and eligible clocks composed with real overrides and Life loss](client-buff-rate-independence.md): expiration or removal does not clear the supplied aggregate; independent Stats updates can stop it while a Buff remains. Full subscribers, server source publication and official sequence remain open |
| R3/R4 | Received-result processing versus resource authority and current-ES control admission | Established for selected supplied-state compositions | [Actual immediate missing-action handler through ProcessResult and independent decoded resource assignment](client-hit-result-resource-composition.md): selected empty signals preserve currents/carry/reservations; positive target ES and zero protects-Mana stat enable a sequence-derived stun-suppression trial, odd across the full u16 domain. Assignment order changes the duration rewrite. [Action-present Fireball continuation](client-hit-action-present-composition.md) executes sequence lookup, actual no-op slots and typed event cleanup. [Release/result/checkpoint composition](client-hit-checkpoint-resource-composition.md) executes the original upstream caller and three nested checkpoints, admitting independent Life/Mana/ES assignments before result return in 42 supplied-state controls; parsed queue/core/TLS, full Action construction, nonempty listeners and numerical Hit damage remain separate |
| R3/R4 | Integer resource changes and selected skill payments | Established for selected conditional paths | [Integer deltas, extra-arrow Life sacrifice, cost routing/check/payment and zero-gate bypass](client-integer-resource-mutations.md); ordinary-client gate provenance, final arrow emission and accepted-Hit loss remain separate |
| R3/R4 | Conditional terminal Mana refunds | Established for selected numerical slices | [FireMortar table-bound refund and separate reason-qualified slice](client-terminal-mana-refunds.md), actual cost-stat layout, signed percent cap, wrapped product/unsigned widening and typed Mana setter; preceding geometry and the second body's actual incoming caller remain separate |
| R2 | Typed DoT formulas through mitigation | Established locally | All five types and nonlethal branches audited; [asymmetries](client-dot-routing.md) remain explicit |
| R2 | Fire self-rate composition | Established for output 8180 | [Complete signed64 percentage, Her Embrace, Touch of Fire, ignite-transfer and difficulty branches](client-fire-self-rate.md); actual source-instance input publication stays open |
| R2 | Physical/chaos self-rate composition | Established for outputs 8179/8183 | [Complete 11/13-input equations, complementary bleeding/ignite transfer, float32 order and signed32 boundaries](client-physical-chaos-self-rates.md); actual source-instance input publication stays open |
| R2 | Source instance → base per-minute rate | Open | [Named RF template](client-dot-source-publication.md) declares native 2023 magnitude; active instance/target publisher and selection/aggregation remain unbound |
| R2/R4 | Insertion, owner notification and retained clock precedence | Established for selected supplied-state compositions | [Actual insertion, original owner getter/setter/notification and eligible ticks](client-buff-clock-authority.md): supplied later timing-stat changes preserve saved speed; partial updates replace it without the initialization floor. [Full replacement](client-buff-replacement-composition.md) constructs a temporary and transfers selected fields while retaining identity and construction-duration/applicability inputs; 33 clock/resource compositions. Definition force-one remains separate; generic refresh, source publication and ordinary eligibility stay open |
| R2/R4 | Concrete Buff backend Stats subscribers | Established for three selected backends | [AvatarOfFireCycleBuff, ArchnemesisModBuff and TetheredDebuff](client-buff-stats-subscriptions.md) bind real provider notifications to backend cache/visual/Render outputs; existing-instance clock refresh remains open |
| R2 | Outgoing RF parameter/formula chain | Established locally | Native stat-set constants and FireDot eligibility feed 14408 → 8198 → 5981; [details](client-dot-routing.md#outgoing-righteous-fire-formula-bridge); target application remains open |
| R2 | Outgoing ailment rate arithmetic | Established for complete named rate families | [Bleeding](client-bleeding-output-family.md), [56 ignite outputs](client-ignite-output-family.md) and [72 poison outputs](client-poison-output-family.md): both hands/spell/secondary, typed min/max, critical/noncritical and poison always variants; category qualification, conversion history, multiplier builders and distinct rounding/overflow recovered; actual infliction and source-to-target publication stay separate |
| R2 | Ailment type permissions, target gates and chance-input composition | Established for selected formula families | [21 eligibility outputs](client-ailment-eligibility.md), [17 chance outputs](client-ailment-chance-inputs.md) and their exact condition/hand scopes; actual infliction trial, ordering and instance publication remain separate |
| R2 | Bleeding policy numerical inputs | Established for four callbacks and selected downstream composition | [Effective Crimson Dance, replacement stack limit, minion-suppressed movement override and exerted aggravation chance](client-bleeding-policy-inputs.md) execute with original dependency dispatch/native records; conditional stack/final outputs update together. Actual instance arbitration, movement application and target publication remain open. |
| R2 | Common DoT multiplier input families | Established for twelve selected outputs | [Generic, five typed, ailment-only and five non-ailment totals](client-dot-multiplier-inputs.md); preserve signed integer operation order, weapon qualification and implicit bow dependency; instance sampling and target application remain separate |
| R2/R3 | Source configuration semantic catalogue | Partial, selected chains established | [22 selected RF/daemon/invisible-fire/ground-effect source chains](client-effect-source-catalogue.md): 261 declarations, six scripts, common/client mode and component distinctions, exact stat defaults; eight `set_` fields now bind to the native zero-token removal/add contract; numerical publication remains separate |
| R2 | Source creation authority and selection rules | Established for selected contracts | [Aura server-mode eligibility and versioned default selection](client-dot-source-authority.md), including separate normal/aggravated bleed selection; native incoming-rate publisher remains open |
| R2 | Instance sampling and duration/expiration rules | Established for selected native formulas and versioned primary explanations | [Faster rate/duration compensation, sampled individual offence, maintained-aura exception, target dynamics and event-driven clock/stat updates](client-dot-instance-rules.md); actual instance publisher and existing-clock owner-stat subscription stay open |
| R2 | Complete source ailment-duration modifier equations | Established for six registered callbacks | [Ignite/bleed/poison additive and final inputs](client-ailment-duration-inputs.md), inclusive poison intelligence threshold, bleed integer quantization and ordered binary32 factors; base-duration application, actual instance assignment and target-rate publication remain open |
| R2 | Skill-duration numerical coupling | Established for six additional callbacks | [Bleeding/poison duration chains](client-ailment-skill-duration.md) combine base 359, distinct capped/uncapped charge terms and separately rounded source/skill factors; actual override selection and instance assignment remain open. Two literal candidates are ClientStrings labels, not numerical consumers. |
| R2 | Typed resource routes and cap boundaries | Established locally | Eight typed/nonlethal/darkness channels, healing/MoM, aggregates and overflow audited; input reachability remains separate |
| R2 | Buff instance clock and native contribution-container semantics | Established locally | Insertion-time owner clocks, infinity defaults and native additive tokens bound; [existing-instance network contract](client-buff-network-clock-contract.md) directly overwrites duration/speed and defaults omitted speed to 1.0; owner-stat-driven refresh and incoming-rate publication are separate claims |
| R2/R4 | Buff clock eligibility input provenance | Established for selected network paths | [Byte/word transport, zero additive input and constructor/partial/replacement differences](client-buff-clock-input-provenance.md) bound; semantic field names, later clock recalculation and source-rate publication remain open |
| R2 | Empty-Id2 Buff backend selection | Established for selected startup catalogue | [137 registration sites, strict empty-key miss, null backend and lifecycle guards](client-buff-default-backend.md); independent observer/script numerical consequences remain open |
| R2/R4 | Buff lifecycle delivery and selected script predicates | Established for selected contracts | [Native observers receive the actual Buff; generic/definition-keyed scripts receive owner Entity and null second object; IfBuff/HasBuff query presence](client-buff-lifecycle-scripts.md); numerical subscriber/publication stays open |
| R2/R4 | Initial Buff granted-value transport and storage | Established for selected readers and lifecycle | [Initial blob and AddBuff share IsRecovery eligibility but differ in byte order; construction truncates or zero-extends while retaining the prefix](client-buff-initial-granted-values.md); numerical actor-Stats publication remains separate |
| R2/R4 | Actual Buff value → continuous Life-loss capacity | Established for AncestralTrialTotemAbsorbDegen | [First granted value initializes capacity consumed by reason-4 Life loss; sufficient capacity skips loss, exhaustion zeros capacity/duration, substantial overflow preserves original delta](client-buff-totem-degen-capacity.md); this is not generic Buff-to-Stats or source-rate publication |
| R2/R4 | Actual Buff forecasts and Life-callback source context | Established for selected consumers | [Recovery/deferred-loss numerical forecasts feed resource indicators; first-eligible stored Buff supplies fallback source context](client-dot-source-consumers.md); neither is target incoming-rate publication or strongest-DPS selection |
| R2/R4 | Buff body → retained source → attribution context | Established for selected partial/replacement paths | [Actual body decoder, retained updates, complete source selector and separately invoked Life-callback prefix](client-buff-wire-source-composition.md); source zero overwrites, optional fields differ from callback context and explicit aggregate-rate loss remains independent; numerical publication and final attribution stay open |
| R2/R4 | Concrete attraction-shape Buff observer | Established for installed callbacks | [Actual secondary listener gain/loss slots immediately return; parent matrix output has startup and parent guards](client-buff-observer-attraction-shape.md); no numerical publisher identified |
| R2/R4 | Concrete Cold Snap backend | Established for selected factories and slots | [Three exact Buff joins, retained instance, no-op lifecycle slots and animation/matrix tick](client-coldsnap-buff-backend.md); numerical rate publication and gameplay expiry remain open |
| R2/R4 | Owner stat → existing Buff remaining-duration override | Established for FirewallSecondaryDegenBuff | [Each eligible tick polls the ordinary owner network provider's 12588 and conditionally restores construction-captured remaining duration after decrement](client-firewall-clock-hold.md); generic timing-stat subscriptions/speed refresh and numerical damage publication remain open |
| R2/R4 | Selected GroundEffect lifecycle and transported lifetime | Established for selected component/callable | [KillGroundEffect, owner component/config enum, five-byte lifetime state, expiry, conditional Buffs guard and cancellation/removal boundary](client-ground-effect-boundary.md); [typed ServerFill parent, cancellation-token producer and reference ownership](client-ground-effect-ownership.md) recovered; numerical target-rate publisher remains open |
| R3 | Hit panel/offence evaluators | Established locally | Both hands, five types, base/added, conversion, average/DPS and actual network-provider numerical readers audited; [details](client-offence-estimates.md) |
| R3 | Final attack typed and hand-total ranges | Established for twenty-four callbacks | [Ordered equations and separate factor census](client-attack-hit-formulas.md) preserve base/added scaling, all conversion remainders, min/max Lightning Purist factors and wrapping totals; hand selection, actual sampling, mitigation and pool loss remain separate |
| R2/R3 | Spell base and added component inputs | Established for thirty component callbacks and four builders | [Typed base/added equations](client-spell-damage-components.md) and [life-cost/weapon contributors](client-spell-contributor-builders.md) preserve wrap/division/rounding, wand splitting and qualifications; source grants, actual entity evaluation and target application remain separate |
| R3 | Final spell typed and total ranges | Established for twelve callbacks | [Ordered equations, complete monotone conversion histories, Prismatic Burst and Lightning Purist branches](client-spell-hit-formulas.md) preserve destination-remainder asymmetry and integer rounding; actual sampling, target defence and pool loss remain open |
| R3 | Selected critical chance and multiplier arithmetic | Established locally | [Effective MH/OH probability and luck/unexciting, Critical Link and multiplier overrides](client-critical-formulas.md) closed; [complete chance-input builders](client-critical-inputs.md) and [selected defender modifier composition](client-hit-defender-modifiers.md) recovered; per-event rolls/confirmation and joint resolver application stay open |
| R3 | Critical Link numerical export | Established for selected factory/table and supplied-provider composition | [Three raw-value appends and non-recovery Buff wire boundary](client-critical-link-export.md); actual collector invocation, grant consumer and refresh policy remain unbound; this is not an actual-Hit numerical resolver |
| R3 | Penetration input composition | Established for 21 registered callbacks | [Generic elemental → typed totals → qualified main/off-hand totals, highest-uncapped ties, resonance and separate event-specific inputs](client-penetration-inputs.md); actual resistance/critical/prohibition application and pool damage remain open |
| R3 | Hit defender modifier composition | Established for selected callbacks | [Shared/typed final, additive elemental, resistance-ignore predicates and enemy-critical increased input](client-hit-defender-modifiers.md); actual hit amount/resistance/critical application stays separate |
| R2/R3 | Taken-as coefficients and armour applicability | Established for 22 callbacks | [Signed coefficient sums, Ward/armour shortcut priority and two declared/actual dependency differences](client-defence-transfer-formulas.md); concrete damage redistribution and mitigation remain separate |
| R2/R3 | Additional reduction inputs and physical ceiling | Established for three callbacks | [Complete 66-input physical and six-input elemental equations, conditional terms, small ceilings, Strength of Blood, fixed cap and seven undeclared physical reads](client-reduction-inputs.md); actual hit application and source publication remain separate |
| R3 | Defender-conditional damage context presence | Established for predicate 9189 and typed cache ABI | [37 defender properties combine as a nonzero OR](client-defender-context-presence.md); native context constructor/getter/dispatcher bind key-indexed storage, but no actual numerical consumer or target relationship is recovered |
| R3 | ApplyDamageFunctions metadata consumer | Established for active-index maintenance | [11 flagged Stats, native row-map lookup and sorted byte-index updates](client-applydamage-stat-index.md); ordinary startup skips this guarded updater; list execution and per-target numerical application remain open |
| R3 | Stat-driven resource preload candidate | Established for selected installed callable | [PreloadFromStats mode provenance and .epk handle collection](client-stat-preload-epk-collector.md) positively classify this boundary as resource dependency collection; numerical Hit application remains open |
| R3 | Physical-defence estimates | Established locally | [Three reference-damage formulas](client-defence-estimates.md), Ward substitution, cap/floor/rounding and category reader; concrete-hit mitigation remains separate |
| R3 | Concrete contact → defender avoidance decision | Established locally | Inherited Fireball gates, Melee and data-bound Sand Spitter MonsterMortarAttack paths audited; [details and Fireball final-factory correction](client-hit-production.md); shared gates remain bound while base clone/terminal differ from final client slots; numerical damage remains separate |
| R3 | Default ranged-action selection | Established locally | [Bow/Wand enum test, selected equipment-to-provider updates and startup registry winners](client-ranged-action-selection.md) bound; selected continuation reaches mode/kind-filtered result processing, without guaranteeing ordinary queued-record consumption; numerical damage remains separate |
| R3 | Winning ranged extension receivers (supporting boundary) | Established for selected contracts | [Conditional DefaultArrow Entity/action/component path](client-projectile-extension-receivers.md), [task ownership/timers](client-entity-task-boundaries.md), [animation listener](client-entity-task-listener.md) and [Ruin count listener](client-entity-task-ruin-listener.md) bound; this is not numerical hit damage or a claim about every task |
| R3 | Accepted hit → numerical pre-deduction | Open | Connect established contact/avoidance to amount, critical, mitigation and typed pool receivers; inspected continuations consume existing feedback or have empty slots |
| R3 | Hit packet → callbacks | Partial | Queue/control, named actions, secondary-action ID producer, GEAL and resource compiler bound; selected player/Sand Spitter files supply no custom-event handler; other configurations/consequences remain open |
| R3/R4 | Programmable Hit signal subscribers | Established for four native APIs | [WhenHit/DealHit and their Other variants install actual Actor signal tasks and asymmetric Entity/particle-label/mask-bit contexts](client-hit-script-listeners.md); remaining resource programs, numerical consequences and damage-to-pool relationship remain open |
| R3/R4 | Actual packaged Hit-listener program | Established for one selected Ferrofluid attachment | [Client source, typed captured-block arguments, effect-pack requests and named-task detach cleanup](client-hit-resource-programs.md); no numerical damage/pool deduction or runtime attachment claimed |
| R3 | Received-result qualifiers and local control admission | Established for selected consumers | [Mutable mask/duration, current-target stun/leech guards, frozen-time differences, quantized direction and selected critical-feedback bit](client-hit-result-qualifiers.md); supplied qualifiers are not local damage/critical rolls or a numerical pool receiver |
| R4 | Timing, freshness and correction policy | Partial | Scheduling, pool notifications, carry reset, entropy expiry, [Life-to-Buffs guards](client-buff-clock-producers.md) and [existing-instance duration/speed replacement](client-buff-network-clock-contract.md) established; source-rate expiry/publication and required numerical consequences remain open; full rendering is outside scope |
| R4 | Official/community comparisons | Established comparison | [Pinned sources](client-damage-comparisons.md) distinguish historical explanations, wiki synthesis and PoB; current server equivalence is unproven |
| R4 | PR #373 intermediate findings | Established comparison | [Pinned report and static cross-checks](client-damage-pr373-comparison.md); private capture counts/timestamps remain reported observations, not independently verified traffic |

Useful starting addresses are indexed in [address-index](../protocol/address-index.md#client-damage-research).
Each newly established edge should cite a range or instruction anchor and state its guard conditions.
Further runtime experiments would be a separately authorized task; retain the historical local traces as history.
