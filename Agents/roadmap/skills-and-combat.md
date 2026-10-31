# Roadmap: Skills, auras, stats and combat

Part of the [Roadmap](../Roadmap.md); what already works in this area is in
[status/skills-and-combat.md](../status/skills-and-combat.md).
Items keep the Roadmap's priority groups, and are in rough priority order within each group.

## 1. Quality of life

Aura intent persistence, town suppression, own-aura restriction (#304) and death reset are implemented.
Own-aura restriction has native paired-buff, bonus, reservation, relog and death controls.
Validate native grey-icon prediction and broader town/combat/character-switch round trips.
The suspend/restore lifecycle is explicit LocalExile policy; see protocol/11-skills.md sections 10c and 13.

## 3. Gameplay

- **Fortification extensions:** the hit-generated system is implemented and native-verified separately from Behemoth.
  Other conditional sources, permanent grants and specialised monster ailment thresholds remain separate work.
  See [Fortification](../docs/game/fortification.md).

- **Ordinary aura activation:** nine previously rejected self auras now share reservation, presentation and the
  supported stat/damage paths.
  Discipline's ordinary ES pool and recharge are implemented with shared character scaling.
  Remaining work: Pride's enemy debuff, conditional consecrated ground, herald/minion/banner
  mechanics.
  See protocol/11-skills.md section 13.
- **Aura caster art:** the distinct EPKFiles2 visuals are selected on activation and area restoration;
  the live source-only regression is addressed with a separate received buff for the top-left icon, awaiting
  confirmation.
  Radius-scaled aura rings, if added, still need their actual radius.
- **Completed area fix:** Cyclone's `/set_aoe` final radius is now sent to the client and shared with hit selection;
  see Status and protocol/11-skills.md (live check pending).

### The player's real stats

Track the exact issue #75 target in the [357-stat ledger](../docs/dev/issue75-stat-ledger.md). Partial audit
records retain their unresolved scope; pending records need individual tracing. No current-branch completion
certificate follows from a historical native run or a matching source name. The inventory rejects membership drift.

Ordinary melee/bow now share defender evasion entropy and independent critical confirmation, with target
evasion modifiers/Blind, target Masteries, qualified raw cannot-evade flags and evaded-arrow continuation.
Selected flat/increased accuracy supports and raw actor overrides now compose before the final floor.
Area attacks consume that profile inside admitted contact, with miss/critical-confirmation and callback controls.
Gem projectile forward/piercing/return evasion and critical confirmation are implemented with missed-contact
continuation, budget and volley-exclusion controls. Gem uses now share one critical roll, with admitted-use luck
and per-hit chance/prohibition/accuracy confirmation. Ordinary attack luck now follows swing/input admission;
review other bow snapshots and special repeats. Complete remaining conditions, correct off-hand sources, and
publish all matching chance/always-hit rows. Ordinary/selected main-hand
average/DPS now includes normal-reference landing and critical-confirmation probabilities; dual-hand/multi-hit,
integer-roll expectation and other sources remain open. Validate these paths natively when shared resources are free.
See [damage §2g](../docs/protocol/20-damage.md#2g-ordinary-attack-evasion-and-critical-confirmation).

Impale now has a shared actual-contact implementation for ordinary and supported hits, scoped chance/effect
inputs, captured duration/hit counts and separately mitigated reflection. The three selected chance aliases
include real supports and local weapon inputs. Remaining work includes conditional weapon/Pride chance,
extra applications and consumption overrides, strongest/Impaler mechanics, native count/visual publication,
corpse consumers, critical-only spell chance presentation and official duration tick rounding. Validate source,
support, weapon, pause, transition and relog behavior in the current native client when other agents release
the shared testing resources. See [damage §3a](../docs/protocol/20-damage.md) and the
[review report](../docs/dev/issue75-review.md); automated coverage does not close these rows.

Issue #75 validation counts and Standard/Ruthless native matrices below describe the source LocalExile revisions.
This branch ports their completed ordinary-mode calculations; fresh build/test results, scope exclusions and
remaining native checks are recorded in [the port record](../docs/dev/issue75-port.md).

- **Known:** client stat key = Stats.dat row + 1 ([11](../docs/protocol/11-skills.md) §8); `/set_stat` takes names.
  The server publishes its authoritative final values; the attack's are computed in `SkillCatalog.Unarmed.Stats`.
  The client has derived-stat evaluators, but its network getter honors explicit overrides, including zero.
  Omitted keys read type stats; native update transactions can publish derived outputs there.
  Ordinary network Actor/skill providers and same-key update/notification behavior are statically bound.
  Supplying a raw network input does not establish native recomputation of omitted derived outputs.
  See [client damage research](../docs/re/client-damage-calculation.md); do not rely on automatic raw-stat derivation.
  Mana per complete group of final Strength now has native raw-source/gear controls and automated pool coverage.
  Raw-source reservation refresh and clamping have decrypted wire coverage; recheck active auras natively.
  See [Status](../status/skills-and-combat.md) and [11](../docs/protocol/11-skills.md) section 4.
  Mental Conditioning now combines mana per Strength with mana-to-armour conversion in current-main tests.
  Historical joint native conversion controls passed at `c9cd1864`; Phrecia selection stays separate.
  Ordinary one-use cooldown recovery now shares the native base/rate and a server base-time budget, with atomic
  preflight before action side effects.
  Initial Dash recovery, early raw rejection, pause and native mid-cooldown rate changes pass live alongside
  layout/send-gate checks; remaining base progress is retained without consuming another use.
  Remaining cooldown work includes multi-use recharge, shared groups, bypass, typed/conditional/more recovery,
  official tick boundaries.
  See [11](../docs/protocol/11-skills.md) §9.
- **Spell cast timing (#192):** native base milliseconds are preserved and cast speed adjusts server waits and
  displayed rate once. Automated checks and real-client -80%/+100%/zero clip durations pass.
  Projectile release/contact alignment was not separately measured.
  Instant casts and attack timing stay separate; conditional action-speed and special native mechanics remain open.
  Attack gems now share level-aware inherent speed with server pacing/contact and native total-duration/APS stats.
  Native input verified the ordinary 0.714 s bow duration and Burning Arrow 1.02 s; automated checks also pass.
  Attack-gem damage/DPS and conditional speed remain separate work.
- **Spell hit panel (#188):** selected-spell ranges now share combat's converted packet and hitless spells clear
  inherited weapon hit damage. Automated checks pass; verify selection, equipment updates and relog in the client.
  DPS now includes the current critical chance/multiplier and signed luck, with exact action duration and
  bounded native integers. Attack averages/DPS now include reference accuracy and independent critical confirmation.
  Integer-roll expectation,
  cooldowns and DoT/beam displays remain separate work. Quality conversion and existing modifier precedence
  are unchanged. Selected attack-gem main-hand ranges and critical-weighted averages share the combat packet.
  Native selection, added-damage refresh/clearing, server hit and relog checks pass alongside automated tests.
  Equipment UI refresh remains to be checked separately.
  Off-hand alternation, conditional damage, supports and modifier-scope corrections remain separate work.
- **Warcry restriction (#297):** shared actor admission has regression and seven real-client controls in Status.
  Blocked input, source clearing, paid retries and an accepted cry's release are verified.
  See [11](../docs/protocol/11-skills.md) section 10b; broader warcry engines and Phrecia enablement remain separate.
- **Character panel (#75):** the Energy Shield regeneration row now shares the implemented recovery calculation,
  with native checks for modifiers, clearing and separation from recharge.
  Ordinary Life/Mana regeneration now includes separate regeneration/recovery increases and matching prohibitions.
  Native actual-recovery, clearing and relog checks pass; Fortification expiry integration uses the same factors.
  Regeneration conversions, named conditional/more modifiers and other recovery systems remain open.
  Exact native Brand/special-charge defaults now enter actor sources once, with a native selected Brand gate.
  Real item/passive/support composition gives actor Brand 6 and selected 8; explicit zero and source removal are covered
  by automated cases in both modes, with new native checks pending.
  Three sourced mirror flags publish special maxima from completed standard maxima, including final zero overrides;
  real unequal 4/5/6 source fixtures preserve each mapping. Pinned PoB 3.29.x remains a labelled fallback.
  Brutal / Affliction / Absorption counts, effects and buffs now follow the three conversion flags (Status, charges);
  Brand attachment/cap enforcement, the Affliction non-damaging effect and Absorption recoup consumers and
  Spirit/Abyss-jewel counting remain open; these changes do not certify entire charge rows.
  Every panel row now has a server derivation (the 2026-10-29 batch in Status covers the Defence estimates, off-hand,
  effective critical, secondary and damaging-ailment rows; the owner's live run of 2026-10-29 passed them, the converted
  charges and the Blood Charges); what remains for #75 is actual ailment application behind the ailment rows (projectile
  ignite now applies; melee/area ignite, bleed and poison do not), Reap's
  damage over time debuff and Vaal Reap's temporary Blood Charge maximum, and executing the secondary hits (Bear Trap,
  Herald of Ice) whose rows are display-only. Deferred (2026-10-29, owner's decision): Spirit and Siphoning Charges. Their only 3.29
  player sources are unique mods (`maximum_spirit_charges_per_abyss_jewel_equipped`,
  `maximum_siphoning_charges_per_elder_or_shaper_item_equipped`), so they need, in the items area, unique definitions
  beyond Mageblood (#60), abyss jewels socketable into abyssal sockets (the Stygian Vise is created without its socket,
  `PlayerInventories.Socket` takes gems only, `ItemModStats.Global` reads no socketed item's mods) and Shaper/Elder
  influence on the `Item` record with its Base component wire flags (undocumented in 13-items.md). Blitz and Challenger
  Charges have no 3.29 player source at all.
  Selected area/item-rarity/minion-increased aliases now use one raw actor-plus-own snapshot, with explicit final
  zero/replacement precedence, source clearing and Cyclone radius preservation. `SendPlayer` initial/transition
  creation and later skill refreshes share the snapshot path. Real support controls cover ordinary availability.
  The historical LocalExile Ruthless exclusion of Item Rarity Support is outside this port.
  Minion combat/drop execution and native alias checks remain open.
  Selected spell penetration now includes real Fire/Cold/Lightning Penetration supports, with source/link/requirement
  lifecycle and hit-consumer agreement covered by nine new Gateway regressions.
  Attack-hand alias composition now includes attack-only sources, linked supports and each active hand's local
  modifiers, with automated lifecycle checks.
  Display prohibitions and weapon-local/non-base combat penetration now have automated source/lifecycle/contact
  coverage. Verify the updated rows and contacts natively; off-hand attack selection and remaining conditional
  penetration sources still need implementation and validation.
  Selected ailment duration/effect coefficients now include real Unbound Ailments contributions, with 24
  source/link/override regressions. Validate the twelve aliases natively, then complete ailment chance/eligibility,
  damaging ranges, duration/application and stacking; a correct coefficient alone does not prove those consumers.
  Added-base/default-mirror/selected-alias coverage passes 52/19/22 new cases and 95 existing controls: 188 focused
  cases without skips. Fresh full Gateway 3803/Core 648/Data 37 pass without skips (4488 freshly rerun cases).
  The current hook records six actual unobserved disposed-NetworkStream faults, all `MonsterKilled` through
  `Program.Send`, with no captured new operator path or diagnostic errors/truncation/cap hit.
  Historical G14/full 3710 and earlier native controls do not establish these operators or complete an issue 75 row.
  Armour/evasion now include ordinary item-local flat/increased modifiers and quality, with native tooltip/panel,
  crafting refresh, unequip and active-shield checks.
  Saved variable bases now feed all three defences before local modifiers; legacy saves retain minimum bases.
  Sacred Orb rerolls and supported special ES rules are implemented; see Items and crafting for scope and validation.
  Shield-specific increases now compose with global increases, and final fractional defence totals round halfway up.
  Shield exclusions also remove ordinary shield bases from the player pools.
  Determination/Grace integer aura scaling and exact aura/flask more-factor composition now feed those pools.
  Body-specific ES increases, doubled Armour and no-Armour operators now retain their slot scope, including IR.
  Item ES per ten final Strength and Evasion per ten final Intelligence now use ordinary global INC, including IR.
  Complete native vectors, source removal, qualification, active sets and explicit zero have automated coverage;
  actual command packets cover ES clamping without refill. Fourteen fresh supplied-source native controls now verify
  corrected ES/Evasion publication, ordinary gear swaps, capacity no-refill/clamping and persisted reload in both modes.
  Other Shaper's Touch effects and legal unique acquisition remain separate; neither defence row is complete.
  The ordinary inherent ES term now follows native help text and pinned PoB: 1% per ten final Intelligence.
  Focused 216 and full Gateway 3939 pass without skips, including eleven new baseline cases.
  Verify dependent recharge/full-ES conditions and queued-send scheduling natively; preserve prior captures as historical.
  Five native prohibition inputs now suppress only the inherent attribute grants, retaining attributes, requirements
  and item group terms. Eighty new automated cases and fresh Gateway 4019/Core 648/Data 37 pass without skips.
  Verify those flag inputs and reload natively; the earlier fourteen controls do not certify the new operators.
  Exact official fractional/group arithmetic, remaining prohibitions/conversions and all-source immediate Life
  publication remain separate work; ordinary equipment Life still follows the recovery tick.
  Full-ES suppression chance now follows the effective resource state and shares the panel/combat consumer.
  Ordinary typed/all-attribute increases now share one whole-source calculation for pools, equipment and gems.
  Automated real-source/removal, zero, derived-pool and active-set checks pass in both modes. Thirty-four native
  controls verify those states, weapon-set persistence after relog and ordinary Fireball requirement refusal/recovery
  (recorded while the port kept a qualified host active; the host now goes inactive with its gem, main's restored
  rule). Supplied-source controls do not certify unique acquisition.
  Pinned-PoB fractional rounding remains a labelled projection. Signed equipment cycles, attribute conversions,
  conditional/more operators and mercenary gates remain open.
  Six missing ordinary Life/Mana coefficients now join flat pools before increases: signed per-level Life/Mana,
  Life per ten final Dexterity/Intelligence, per four Dexterity and per two Intelligence.
  Actual level and completed attribute groups apply once; native -12/-6/-3 Life-per-level reductions retain their sign.
  Whole-coefficient developer zero/replacements, pool rebuilding and resource publication preserve the existing
  Strength-based Mana and reservation paths. All 71 new cases (45 pure/native-source and 26 Gateway) plus 82 existing
  regressions pass: 153 focused with no skips. Full Gateway passes 3874/3874 without skips; earlier unchanged
  Core 648/Data 37 from this session remain applicable (4559 applicable; only Gateway rerun after these changes).
  That run's hook records seven actual unobserved disposed-NetworkStream faults: six `MonsterKilled` → `Send`
  and one `NotifyMonsterHit` through the projectile-hit path; 390 first-chance records are separate.
  No diagnostic errors/truncation/cap hit or new pool-source path is captured; new native checks remain pending.
  The retained final pool floor differs from pinned PoB's final round, so official fractional parity remains open.
  Isolated source fixtures do not prove acquisition/event-passive admission or full conversion nodes, and this subset
  does not complete either Life/Mana panel row or its conditional/more/conversion operators.
  Frenzy attack/cast speed coefficients include the combined per-charge modifier, with signed/zero,
  source-removal and held-count agreement covered in both modes (17 selected tests, 15 new).
  Eight native supplied-source controls verify values, positive formatting, clearing and relog; native unique-gear
  acquisition and zero-row formatter evidence remain outside those controls.
  The four elemental self-duration reduction rows now subtract the raw all-ailment self-duration source once;
  Harsh Lessons' real -20 gives +20 reduction in both modes. Its 14 new cases pass within the combined 53 focused
  cases, followed by full Gateway 3710/3710; unchanged prior Core 648/Data 37 remain applicable (4395 total).
  Five disposed-stream background faults remain recorded without new-operator references; native verification is
  outstanding. Left/right ring operators, other ailment families, conditional ES delay and actual
  ailment execution remain separate; do not sum both slot-local ring coefficients globally.
  Combined Block/Stun Recovery and flat per-player-level on-kill/flask-duration aliases now use one raw snapshot
  and actual character level. Twelve positive source controls at levels 10/20/100 match all six fixed fields in
  Standard/Ruthless. Combined with six utility and four Life controls, 22 of 40 completed native controls pass;
  the 18 complete zero controls remain UNPROVED. Six-field verdicts total 138 VERIFIED/42 UNPROVED across source,
  retained and clearing/restoration/relog evidence; hidden-zero rendering is not established.
  Full on-kill recovery/percentage/prohibition rules and block-animation timing remain separate work.
  The preceding panel/flask batch passed 55 new plus 80 existing focused checks and Gateway 3657/3657;
  prior unchanged Core 648/Data 37 results still apply. These historical native/test results do not validate the new
  self-duration/flask-effect operators. A subsequent eight-control positive flask-effect batch at actual levels10/100
  in both modes verifies recovery rate/amount and final utility movement/armour strength, charge costs, native timer
  duration/removal, canonical restoration and representative relog. The utility verifier checks explicit final stats
  rather than the unused recovery-value channel; after-capture revisions remain disclosed. Self-duration native
  evidence, source-removal/zero next-use and active-source changes, local/magic composition, unique acquisition and
  tooltip parity remain separate. The old18 whole-zero controls remain UNPROVED. Details are in protocol13 §8.
  Continue auditing the remaining rows against the current implementation before adding formulas.
  Leech limits now have native display, modifier, prohibition, pool-conversion, reservation and relog controls.
  Ordinary active leech now has an accepted-hit and resource-writer implementation; official fractional endpoint rounding is unmeasured.
  The current branch now recomputes all 24 scoped leech coefficients and three speed aliases from raw actor,
  support and qualified hand inputs. Native checks for this composition/recovery remain pending. The ordinary engine
  uses separate duration, total recovery and maximum-rate rules (damage §5b). Ghost Reaver now has independent
  converted/native ES instances, cap/recharge effects and source/refund controls. Continue other conversions,
  conditional and weapon-family sources, skill-specific recovery speeds and speed/defence/
  ailment/DoT leech-state consumers. Actual leech/full-ES flags and ordinary/selected hit-damage conditions now have
  support, contact and idle-refresh controls; do not add overlapping displayed scopes or substitute regeneration for leech.
  Attack-only ES overleech now preserves instance origin and the shared cap; ordinary Life retention now honors zero.
  Continue other Life-retention virtual sources and native validation; do not broaden the attack-only item scope.
  Claw mastery/Weapon Master instant sources and Life-instant conversion now have real-source controls. Continue
  remaining conditional instant sources and verify native mixed converted/native ES composition before certification.
  Highwayman's total-recovery doubling and the explicit Life maximum-rate doubling flag now have separate
  consumers and controls. Verify native stacking/recovery and current source acquisition; do not infer Vaal Pact
  behavior from legacy raw flag names or multiply one boolean doubling per item copy.
  Native conversion/prohibition interaction remains unverified; the conservative source-plus-destination policy
  and the alternative are recorded in damage §5b and the review report.
  Ward and unsupported conditional mechanics still need their own systems and evidence.
  Weapon-local typed damage and multiplicative physical quality now feed ordinary and supported attacks,
  with correct melee/projectile context and active-hand sources. Pool/charge/timing/block conditions use active gear.
  Twenty-four native publication controls pass in Standard/Ruthless, with an independent local-mod/quality tooltip.
  Alternative quality, supported-contact native composition and offhand alternation remain open.
  Local flat-before-increased critical chance is implemented with automated source/order cases; native validation
  remains outstanding and is not supplied by the earlier weapon-local checks.
  Damage conversion now retains visited-type modifier history for destination/intermediate increases and inherited
  typed more factors. The Core seam has 21 new path cases, 12 existing packet controls and all 648 Core cases passing.
  The shared player resolver also supplies previously omitted charge more on ranged/attack-gem hits.
  All 43 new Gateway cases, 340 focused checks and full Gateway3602/Core648/Data37 suites pass. Thirty-two native
  numeric controls pass across both modes: 24 original explicit-field and eight separately scoped sparse-zero
  controls, including one retrospective diagnostic baseline. Four restoration/relog checks and all 714 frozen hashes
  pass. These prove local panel publication; authoritative hit contacts and conditional operators,
  endpoint-only modifiers, ailments/DoT and official fractional-rounding parity remain separate.
  Flask charge-gain source resolution and separate hybrid Life/Mana rate/buff lifetimes have automated coverage
  and 24 native controls across both modes. Conditional flask sources and queued timer/predictor presentation
  remain open; see the items-and-crafting status.
  Final supported-spell ranges and pre-critical average/DPS now follow linked support changes and clear removed
  types. Native Standard checks bind eleven positive formatter captures to fresh owned skill stats, verify removal
  and relog, and retain partial zero-row evidence for Brutality/Righteous Fire. Native in-place link mutation,
  official critical weighting, DoT and special skill mechanics remain open.
- **Done when:** life, mana, attributes, resistances and their derived values come from the server's own calculation
  (Characters.dat base values, level, items, passives) and are sent in the Stats component.
  The overcapped resistances' derived evasion and critical increases now follow that rule and are native-verified.
  Generic increased attributes have historical native raw-source, derived-pool and requirement controls at `60044ad`.
  Current-main mana-per-Strength, requirement and inactive-weapon composition has automated coverage.
  Eleven rebased native controls verify final Strength mana groups, increased mana once and ordinary gear requirements.
  Ordered joint native controls at `c9cd1864` verify conversion after final Strength and odd Intelligence.
  Remaining native composition includes own-item percentage retention, cascading equipment, passive refunds and swaps.
  The fractional algorithm remains a documented PoB server model; official-server parity is not established.
  See [Status](../status/skills-and-combat.md) and [the calculation model](../docs/protocol/11-skills.md) section 4.
  Phrecia play compatibility remains a separate prerequisite.
  Power of Purpose has historical original-head raw-source, armour/mana, actual attack-consumer and relog controls.
  Native idle reservation, Iron Reflexes and MORE compositions remain; their model and automated tests are in
  [the calculation notes](../docs/game/power-of-purpose.md).
  Combined increased attributes, mana-per-Strength and conversion have regressions on base `939b44fe`.
  Joint native controls at `c9cd1864` remain historical; the rebased candidate needs separate native acceptance.

### Auras: reservation and effects

- **Known:** `GrantedEffectsPerLevel` carries the reservation; the Life pool's reserved field and S→C 0x1b1 update it;
  Purity of Elements/Fire/Ice/Lightning, Clarity, Vitality, Precision and Haste are implemented with their buff
  visuals and `/auras`, awaiting live verification ([11](../docs/protocol/11-skills.md) 13,
  [Status](../status/skills-and-combat.md)).
  Own-aura admission and suspension have source/dispatch/send-gate/lifecycle coverage, including Rage refreshes.
  Fifteen native controls verify installed pairs, actual bonuses/Mana, refusal, restoration, relog and death.
  Native grey-icon prediction and persistent restricting equipment remain separate verification.
  Duplicate-instance support context and queued ordinary acknowledgements have separate regression controls.
  Complete Lone Messenger, party auras and unsupported curse/enemy/banner/Vaal executors remain separate.
- **Start:** the auras' 3D effects (BuffVisuals EPKs of the `IsSkillBuff` rows do not render, unlike Righteous
  Fire's); the combat auras (Anger, Hatred, Wrath, Zealotry, Pride), Heralds, banners, curse auras and
  their unsupported conditional effects.
  The skill-bar indicator's buff association is implemented (0x1c1 bit3; live check pending, see Status).
  Support gems come with the gem system ("Gems" below); party and minion auras and life reservation with their own
  systems.
- **Done when:** the standard auras reserve correctly, their effects match PoB, and the character panel and HUD show
  the same reserved and unreserved values.

### Area-entry protection verification

- **Known:** Grace Period protection and the native timed buff are implemented outside towns and hideouts.
- **Start:** verify the 60-second countdown, first-step removal and damage prevention in the real client.
  The icon is visible; its corrected floating-point timer needs a live recheck after the upstream integration.
- **Done when:** the timer counts down and protection ends on movement, actions or expiry.

### Damage model (foundation done, deeper defence next)

- **Client damage research:** recover Hit and DoT calculations before proposing further server mechanics.
  Network Actor/skill input freshness and the distinct native transaction/startup boundary are established.
  Typed routing, cap/overflow boundaries, mode-dependent ticks and absolute assignment have static evidence.
  Outgoing RF parameters and 14408/8198/5981 formulas are bound separately from incoming base rates.
  Resolve source instances into base rates and actual publication eligibility.
  Reuse [bleeding policy inputs](../docs/re/client-bleeding-policy-inputs.md): conditional Crimson Dance,
  replacement stack limit, minion-suppressed movement override and exerted aggravation chance are numerical inputs.
  Find an actual instance-selection/application consumer; the finite registration graph has no declared downstream
  consumer for the three policy outputs and does not cover indirect/inlined reads.
  Reuse [native DoT queue composition](../docs/re/client-dot-native-composition.md): 105 outputs now compose through
  original rank/dirty/dispatch instructions. Supplied eligibility/cache and local caps do not close instance publication
  or prove an official global cap; continue from actual source/target records rather than restaging these formulas.
  Reuse [the native getter-to-pool continuation](../docs/re/client-native-dot-pool-composition.md): record lookup and
  numerical loss now compose under supplied native provider binding. Actual actor selection and source publication
  remain open; avoid recreating scalar rate adapters for this already verified numerical boundary.
  Reuse versioned source-sampling/target-dynamic rules and native duration/faster compensation.
  Reuse the complete six source-duration modifier equations, including conditional sums and ordered binary32 arithmetic;
  do not rescan them as instance construction. Base-duration application and actual instance assignment remain open.
  Reuse the six additional skill-duration numeric stages and their source input units; do not flatten their intermediate rounding.
  Resolve actual 1192/3745/6188 override selection and instance assignment separately; the 6187/6188 UI literals are ClientStrings labels.
  Existing owner-stat-to-clock subscriptions and source-rate expiry publication remain separate open edges.
  Reuse recovered clock input transport/defaults and partial/replacement asymmetry; later recalculation stays open.
  Reuse [the retained replacement composition](../docs/re/client-buff-replacement-composition.md): actual temporary
  construction and field transfer preserve identity and some initialization inputs. Do not model it as reinsertion,
  reapply the local speed floor, or assume clock changes withdraw the independent explicit loss-rate override.
  Reuse [body-to-source attribution composition](../docs/re/client-buff-wire-source-composition.md): both retained
  paths overwrite the transported source, including zero; partial/replacement eligibility and extra context differ.
  Do not repeat this body/selector prefix or interpret it as strongest-instance selection, rate publication or final attribution.
  Empty Id2 for ignited/poison selects null in the audited startup catalogue, not an implicit default backend.
  Follow concrete numerical observer/script leads; do not repeat that closed factory/registry question.
  Reuse actual Buff observer payloads, lifecycle program storage/invocation and selected presence predicates.
  Initial granted values share the recovery gate; reuse their byte-order and prefix-preserving storage contracts.
  The attraction-shape observer has no-op gain/loss callbacks; do not reopen it as a numerical publisher.
  These contracts do not yet identify a numerical subscriber; do not expand unrelated presentation code.
  Reuse recovered outgoing ailment coefficients, faster/slower and critical-multiplier branches.
  Reuse complete MH/OH/spell chance builders and the selected defender modifier composition.
  Reuse all twelve named bleeding, 56 ignite and 72 poison rates with their category/critical builders.
  Preserve implicit ignite tincture inputs, poison hand gates, wrapping totals and chaos-only always-output gates.
  Reuse the complete fire self-rate 8180 equation; its primitive source publication remains a separate open edge.
  Reuse complete physical/chaos self-rates 8179/8183, including transfer gates and the physical post-cap tail.
  Reuse thirty typed spell component equations and four life-cost/weapon-as-spell contributor builders.
  Reuse twelve final spell range equations, including ordered conversion terms and both special branch families.
  Reuse twenty ordered attack ranges and four hand totals; base 6172 and added 5761 have separate scaling.
  Preserve attack Lightning Purist minimum 0.5/maximum 1.25 and every chaos-path destination remainder.
  Hand selection stays with the projectile research; formula closure does not close actual hit-to-pool loss.
  Preserve the longest chaos-path remainder exception and signed integer total wrapping in comparisons.
  Preserve wand half-splitting, hand-cast/type gates and distinct integer/float32 boundaries.
  Fire intermediate-added reads active-Buff count outside declared dependencies; grants/refresh stay separate.
  These callbacks consume supplied bleed/ignite/poison rates; primitive instance publication remains open.
  Defender-conditional presence output 9189 groups 37 inputs; bind its numerical use before treating it as a resolver.
  The native cache receiver ABI is established; do not promote untyped offset matches into actual consumers.
  Reuse [native dependency ordering](../docs/re/client-stat-dependency-order.md) and changed-output propagation.
  Preserve actual duplicate declarations; neither the ordered queue nor declared edges guarantee every runtime read.
  IsApplyDamageFunctionStat selects guarded active-index maintenance for eleven on-hit side effects.
  Reuse its typed metadata consumer; actual function execution and numerical hit application remain separate.
  Three concrete Buff Stats subscribers update state/presentation; do not repeat them as clock-refresh candidates.
  Reuse typed ailment permissions, target acquisition gates and ordinary/critical-only chance inputs.
  Connect their real infliction consumers without assuming the chance sums perform a roll or create an instance.
  Keep effective critical probability distinct from per-event trials, confirmation and defender critical modifiers.
  Aura source metadata and versioned stack rules constrain authority without supplying the native rate publisher.
  Concrete Fireball/Melee contact and avoidance decisions are bound separately from panel formulas and feedback.
  Preserve Fireball base-versus-client factory scope and mode/kind filters; reuse parallel projectile research.
  Reuse received-result control admission and mask/duration rewrites; selected critical feedback is not a roll.
  Reuse [release/result/checkpoint composition](../docs/re/client-hit-checkpoint-resource-composition.md):
  actual ProcessResult can consume independent resource assignments at its three nested checkpoints.
  Preserve supplied ordered-mode/core/TLS boundaries; do not count checkpoints as frames or infer a hit amount.
  Actual wire/core production stays with projectile research; pursue missing numerical damage connections here.
  Bow/Wand specialization, equipment-to-setter updates and startup registry winners are bound.
  Follow numerical consequences only where a concrete extension output requires them; do not expand all rendering.
  Melee attachment/controller outputs and DefaultArrow's five component update targets are bound.
  Selected Entity task factories, timers and secondary subscriptions are bound separately from component updates.
  Reuse the selected action-start task's bound animation publisher/listener when a relevant trace reaches it.
  Selected Buff lifecycle/quantity listeners resolve to Ruin count effects/audio; reuse that closed boundary.
  Selected Sand Spitter source Actor callbacks and attachment outputs are bound separately.
  Owner clocks initialize at insertion; existing-instance network updates overwrite duration/speed (default 1.0).
  Separate any local owner-stat refresh claim from that established synchronization contract and damage rate.
  Flame Wall secondary Buff has a concrete polling override restoring captured duration from owner stat 12588.
  Reuse that selected path; general owner timing-stat subscriptions and existing-instance speed refresh remain open.
  Reuse the actual Buff capacity and exhausted-duration contract for reason-4 Life loss.
  Generic Buff contribution and source-rate publication remain open.
  Reuse the separate Buff resource forecasts and first-eligible Life-callback source context; neither publishes rate.
  Four Hit script subscribers have exact asymmetric contexts; trace their actual program consequences.
  Reuse the selected Ferrofluid source/capture/named-task cleanup; its render-pack block is not numerical damage.
  Life-to-Buffs guards and the decoded nonpositive-to-positive Life clear trigger are bound.
  Trace other guard producers and source-rate consequences separately from that assignment branch.
  GroundEffect kill/expiry now reaches the owner's conditional Buffs guard and a concrete lifecycle boundary.
  Reuse its typed parent/token ownership; numerical source-to-target publication remains a separate open edge.
  A named RF template declares a rate input; connect its actual instance and target to native publication.
  Keep reference-damage defence estimates separate from accepted-hit mitigation and resource loss.
  Reuse taken-as coefficients and armour applicability formulas; preserve their declared/actual dependency differences.
    Reuse complete additional physical/elemental reduction and physical-ceiling input equations.
    Preserve conditional qualifications, small ceilings and Strength of Blood's integer per-minute conversion.
    Seven physical reads are absent from its declared vector; do not assume that vector is exhaustive.
  PreloadFromStats and the selected Cold Snap backend have concrete resource/presentation outputs; do not reclassify
  them as numerical publishers or expand their renderer without a separate numerical edge.
  Reuse [penetration input equations](../docs/re/client-penetration-inputs.md); typed and hand totals already include
  generic/highest-uncapped contributions. Preserve separate event-qualified inputs and undeclared condition reads.
  Bind actual resistance selection, ignore/prohibition/penetration application instead of adding these totals twice.
  Connect accepted contacts to numerical damage/critical/mitigation and pool receivers; these edges remain open.
  Reuse [actual received-result/resource compositions](../docs/re/client-hit-result-resource-composition.md):
  missing-action ProcessResult can succeed with no current/carry/reservation change in the supplied empty signals.
  Current target ES and protects-Mana gate a sequence-derived control trial; resource assignment order matters.
  Do not repeat the exhaustive u16 trial or treat its duration rewrite as numerical damage.
  [Current Fireball Action lookup/callback/event cleanup](../docs/re/client-hit-action-present-composition.md) now
  executes for supplied states. Do not repeat that empty-event path; ordinary construction/ingress, nonempty
  numerical subscribers and the accepted-Hit numerical publisher remain separate next boundaries.
  Reuse integer pool cost/sacrifice paths separately; trace payment-gate provenance before claiming client prepayment.
  Their concrete numerical writes do not close accepted-Hit damage or DoT source publication.
  Reuse [terminal Mana-refund arithmetic](../docs/re/client-terminal-mana-refunds.md), preserving FireMortar scope,
  the separate reason-body ingress limit, fresh cost-stat reads and unsigned product widening.
  Reuse the [maximum-stat subscriber and mode gate](../docs/re/client-pool-maximum-refresh.md): the two bound
  client World implementations suppress proportional current adjustment, while Life alone clears carry.
  Do not replace that contract with unconditional rescaling or confuse it with accepted-Hit resource loss.
  Reuse [recharge-control and total-rate authority](../docs/re/client-recharge-rate-authority.md): active notifications
  can cancel positive delay and suppress reset, while the pool step integrates its separately read total recovery rate.
  Local timer expiry and the installed no-op active-state publication do not supply a missing DoT/Hit source publisher.
  [Parsed Stats-to-Life composition](../docs/re/client-network-rate-composition.md) establishes received total-rate loss,
  not source-instance publication. Preserve its extra full-step checkpoint and raw/final override distinction.
  Reuse [initial Stats byte composition](../docs/re/client-initial-stats-rate.md): the original component reader,
  compact primitives and subsequent Life loss are bound. Do not count duplicate pairs as additive DoT sources,
  or treat this selected component body as a whole creation packet or an observed official rate publisher.
  Reuse [the direct contribution map](../docs/re/client-native-contribution-callers.md): 103 retained calls in
  13 owners have object/argument boundaries. Graph-owned remove-then-rebuild and type/action contributions are
  separate from a concrete target-rate publisher; do not repeat this finite inventory as a new source search.
  Reuse [owner/instance clock composition](../docs/re/client-buff-clock-authority.md): actual insertion and
  selected owner notifications do not replace a received-instance refresh policy. Network speed zero bypasses
  the initializer floor; definition force-one still governs ticks. Find the generic actual refresh publisher
  without repeating these supplied-state controls or interpreting timer speed as DPS.
  Reuse [Buff lifecycle/rate composition](../docs/re/client-buff-rate-independence.md): parsed removal and
  eligible timer expiry do not clear the supplied explicit aggregate in the selected empty-lifecycle state.
  Separate instance synchronization from rate withdrawal; recover the source publisher and its actual ordering,
  without assuming empty subscribers, automatic rate clearing or a measured 33ms cadence.
    [Critical Link export](../docs/re/client-critical-link-export.md) positively classifies one critical-input
    consumer as three raw-value appends. Reuse its action/definition bindings; actual grant publication remains open.
    The completed bounded immediate-key navigation is not a whole-program absence proof or Hit numerical closure.
  Preserve [resource input read order](../docs/re/client-resource-read-order.md): saved recovery rates, later pool state,
  sequential Life loss and preloaded ES loss differ. Reuse [the composed ordered resource route](../docs/re/client-resource-checkpoint-composition.md):
  peer-established World checkpoints/parsed dispatch now execute the actual setter and later recovery arithmetic.
  Wire parsing, queue production and official checkpoint choices remain separate; do not repeat the peer's World audit.
  Reuse [AddBuff magnitude transport](../docs/re/client-buff-wire-magnitudes.md): the writer and reader share a recovery
  gate but use different length sources. Keep their cardinalities aligned; do not place non-recovery DoT magnitude
  in this omitted vector or confuse the separate counted vector with a proven DPS carrier.
  Follow other event configurations beyond GEAL/native listeners; selected player/Sand Spitter files have no handler.
  Extend the selected 22-source semantic catalogue through positive data/native links to numerical publication.
  Preserve its exact inheritance, repeated fields, mode and component distinctions; six scripts are catalogued.
  Prior keyword inventories are bounded searches, not full semantic audits; reuse projectile-side source findings.
  Reuse the pinned PR #373 comparison: `0x1b4` is a skill-stat update; private capture claims need source records.
  Bounded negative scans are not absence proofs.
  Prioritize server-usable calculations and original C/S contracts; full rendering and live parity are not gates.
  Track artifact hashes, exact anchors and evidence boundaries in the
  [research workbench](../docs/re/client-damage-calculation.md#8-research-workbench-and-completion-criteria).
  This is static research, not authorization for client execution or a server refactor.

- **Done:** hits are per-type `DamagePacket`s; conversion follows physical, lightning, cold, fire, chaos, including
  chained conversion, the 100% cap and gain-as-extra.
  DoT/ailment formulas and fractional ticks are in the same model, and per-type resistance mitigation works on
  packets.
  The default attack, Fireball and monster melee use packets and read the player's/skill's conversion stats
  ([20](../docs/protocol/20-damage.md)).
  Player hits on monsters take the target's base resistance per type (`MonsterTypes.Resistances` →
  `MonsterResistances` by the level's difficulty, `MonsterResistanceCatalog`/`HitMitigation`), with the hit's
  unconditional penetration subtracted from the capped value and no second clamp (`Penetration`): general
  per-element and elemental stats on every hit, attack-only variants on attacks, the cannot-penetrate flag
  suppressing the elemental ones; DoT and physical take none. Monster armour is still not modelled.
  The player's real life, resistances, armour and evasion are computed from level, passives and base equipment, and
  armour/evasion mitigate monster hits (Status: Armour and Evasion).
  Ordinary physical monster melee can now trigger native player hit recovery with matching server cancellation.
  Copied-save live checks verified walking, pending cast cancellation, released flight and a stun-immune control.
  Enemy TakeHit recovery uses native Actor type 5, stops movement and invalidates old swing generations.
  Historical LocalExile controls cover the 50 ms planner, bounded repaths, pacing and menu pause.
  Fresh native validation of this standalone port remains pending.
  Automated regressions retain the full attack duration and guard pending swings and boss-stage completion.
  Selected Antiquarian now has native Ward/Adrenaline, Turtle Shell, always-hit/Far Shot and shield controls.
  Whaletooth extends actual Enduring Cry recovery; see [its model and evidence](../docs/game/antiquarian.md).
- **Known:** both sides still have flat damage ranges ([11](../docs/protocol/11-skills.md) 5, 6);
  ordinary player ES absorption, regeneration and interrupted recharge are implemented and live-checked.
  Current ES/prediction publication now samples at the send gate with encrypted-wire regressions;
  verify queued capacity changes and predictor clearing in the native client.
  Added damage has its first stat: the attack's added lightning gets
  `attack_skills_have_added_lightning_damage_equal_to_%_of_maximum_mana` percent of the maximum mana pool
  (Whisperer "we know all", [20](../docs/protocol/20-damage.md) §2b).
  Arcane Might's six modern contracts share attack/panel consumers, maximum independent grants and skill quality.
  Normal Crown and mastery lifecycle have automated coverage; their native acceptance remains pending.
  Earlier Whisperer raw-stat native checks do not establish this expanded composition's native behavior.
  Iron Will/Strength provenance and missing upstream conditional spell producers require separate work.
  Unsupported skill/variant execution and unrelated Crown leech remain separate; see
  [Arcane Might boundaries](../docs/game/arcane-might.md) and [20](../docs/protocol/20-damage.md) §2c.
  Steel Willed's attack increase from complete groups of final armour has shared regression and native coverage.
  Boolean chaos-hit armour has native raw-source Life-loss controls and shared incoming-hit regressions.
  Mixed monster packets and ES/block/suppression/Fortification have automated coverage; recheck them natively.
  See Status and protocol/20-damage.md section 2e.
  Rampager's raw spell restriction has hit/panel and RF regressions; eleven native controls are historical (#293).
  See [status](../status/skills-and-combat.md) for the verified consumer scope.
  Fresh ordered joint hit controls pass at `c9cd1864`; original RF, Ice Nova and relog checks remain historical.
  Rampager's other mechanics and class admission remain separate work.
  Recent-hit armour defence now composes with physical/chaos mitigation in actual and queued contact regressions.
  Full stat writers, including Rage, own auras and Mageblood, have expiry and encoded-pair acknowledgment regressions.
  Dirty aura/Fortification composition also checks decoded wire order, independent expiry and idle correction.
  Its eighteen original-head controls remain historical; fresh ordered joint controls pass at `c9cd1864`.
  Native cap/order uses one raw40 contact; four-contact timing and forced full-writer races remain automated coverage.
  See [the model](../docs/game/steel-willed-armour.md) for event policies and evidence boundaries.
  Partial armour percentages, other Behemoth nodes, class admission and suppression transfer remain separate.
  Fresh composed default-attack mana, penetration and critical-ignore checks pass at source `1366f48`.
  Verify the remaining supported-gem, modifier and bow combinations natively; retain their automated regressions.
  Unconditional penetration is integrated with base/mod resistance, fractional totals and target damage taken.
  Critical elemental ignore and non-critical penetration now compose with it on the same rolled packet.
  Rerun critical ignore against a rebuilt final DLL: the previous combined run used the preceding #225 artifact.
  Recheck the resolved bow-contact and target-modifier composition natively before claiming new combined proof.
  Special ES mechanics, conditional penetration, exposure, ailments and further added-damage/support effects remain open.
  Ordinary Frostblink's bounded ground-instant acceptance passed actual Q, retry and invalid-request checks.
  Its hovered stationary-monster 0x191 path now uses the native size-aware object aim and object-bound confirmation.
  Automated acceptance/queued rejection checks and manual native endpoint experiments pass.
  Actual hovered Q reached native/server `(317,220)` with one object confirmation and one 12-mana cost.
  A framed early ground retry was refused without extra cost or confirmation.
  Production framed off-axis/far controls matched native/server endpoints.
  Framed moving-target, partial-terrain, missing-target and noninstant controls were refused without spending.
  Moving targets, coincident random aim and partial object-candidate terrain fallback remain outside this path.
  Production use during an active walk still needs measurement; native manual walk-stop experiments passed.
  Its departure/arrival damage, chilled ground, enemy-dependent recovery and shared Blink groups remain open.
  Extend terrain and movement restrictions from native evidence before accepting other instant movement skills.
- **Start:** special ES mechanics, exposure, conditional penetration and the remaining critical strike stats
  from the stats both sides already carry; monster skills beyond the first granted effect (ranged, spells).
  Extend player stun rules to ES avoidance, substituted thresholds, conditional defences and monster-specific modifiers.
  Spell base critical chance now comes from each skill's level and shares its panel/combat calculation.
  Native skill switching, combined bonuses, no-crit, relog and hitless-spell controls are verified.
  The overcapped-resistance modifiers (Sea Legs' flags: cold overcap as increased evasion, lightning overcap as
  increased critical chance) are implemented through the pools with automated and original-head native checks.
  Recheck the combined armed 830 +45% =1204 result and extreme signed cancellation on the resolved stack natively.
  Recheck Purity plus typed/all-element dev replacements and zero maximum overrides after the aura-source correction.
  The pure base overrides are in as a generic stat consumer (`override_weapon_base_critical_strike_chance` and the
  unarmed base in `AttackBaseCriticalStrike`): Slayer Overwhelm, Blind Prophet's Sightless and Behemoth Final Blow
  have native-verified base calculations, active-set isolation and cannot-crit gates (Status and
  protocol/11-skills.md §critical strikes).
  Recheck the resolved stack's shared half-up midpoint natively; the original head's native checks remain historical.
  Recheck development generic/spell increases and rounding live after the input-consistency review fixes.
  Deliver their nodes through the separate ascendancy lifecycle; Slayer's per-nearby-enemy crit multiplier is separate.
  Generic conditional rolls and the unarmed melee multiplier are implemented and native-verified separately.
  Continue native spell/projectile checks and the resolved critical-hit/Fortification composition on `effb797`.
  Verify successful paid Final Blow refunds once Phrecia play has an agreed client-compatible graph.
  Focus/curse lifecycles and statistical RNG validation remain separate from the deterministic policy tests.
  Ordinary generic/typed added BASE now follows intrinsic-base replacement and precedes increased chance, including
  native weapon-local flat-before-increase ordering and linked Increased Critical Strikes once.
  Mode-specific passive/item, source-removal, zero, prohibition and hitless controls have automated cases;
  combined automated results are in the panel entry.
  Sixteen positive Fireball/ICS selected-field controls on `0aabeb6` pass at actual level 100 in both modes, including
  actor/support removal, relinking and relog, plus separate default-sentinel and current-Actor absence controls.
  The after-capture Standard sentinel correction and its current-identity/historical-numeric boundary are in protocol 11 §5.
  Continue native attack/local-weapon/Assassin, signed/zero/prohibition/hitless and actual critical-hit checks.
  These controls do not establish rendered zero or certify a complete issue75 row.
  Isolated modifiers do not prove acquisition; the local-flat fixture does not certify its no-extra-critical-damage
  consumer, and exact official rounding is unproved.
  Four unconditional MORE/LESS critical sources are now implemented with final cap/rounding and actual support/
  item lifecycle/contact tests. Recheck Controlled Destruction and the unique -40% source in the native client.
  Other conditional more chance, guaranteed-critical effects, off-hand chance/multiplier publication and execution,
  and the attack critical confirmation remain separate work. The off-hand range/timing publication currently
  has no corresponding critical fields; do not infer completion from its other weapon fields.
  Add support effects through `DamagePacket.Add`, `Scale`, `Convert` and `AddGain`.
  When owned-actor execution is implemented, supply actual-caster stats to `SkillDamageContext` explicitly.
  Do not inherit an owner's spell-damage restriction onto independent minions or daemons without a skill rule.
- **Done when:** damage numbers match the character panel and the monsters' levels.

### Incoming elemental ailment extensions

- Ordinary supported monster basic contacts now exercise ignite/chill/freeze/shock and final avoidance.
  Finish dedicated native buff icons and source/threshold/conditional-immunity parity before claiming a general ailment engine.
  Hostile spell adapters and alternative-ailment sources remain separate work; monster mortar shots already run the
  contact pipeline at their landing (status: Skills and combat, "Monster projectiles").
  See [Gambler](../docs/game/gambler.md).

### Rage extensions

- Generic Rage generation, limits, installed buff/count, actual damage, decay, pause and lifecycle are native-verified.
  Remaining native composition includes supported projectiles, area sweeps, linked/active equipment sources,
  notification delays beyond the shared interval and slow destination loading.
  Recheck charge, equipment and command refreshes queued across expiry and separate decay-speed modifiers.
  The 140-damage/11-Fortification composition has automated accepted-contact coverage; verify it natively.
  Measure official capped-attempt refresh behavior and area-loading timer parity before replacing policy v1.
  See [26-rage.md](../docs/protocol/26-rage.md); Vaal costs and whole-class selection remain separate mechanics.

### Vaal execution and remaining soul checks

- Collection, bounded shared budgets and the proved Actor payloads (#308) pass automated and bounded native checks.
  Source checks on base `939b44fe` cover attribute/mana composition, supports, payment and current publication.
  The proved missing skill-record bit1 is implemented from raw Vaal metadata, with parsed and socketed regressions.
  Forty-two pinned 3.29.3b checks cover real-kill charging, the cap, cost changes, zero cost and prevention.
  They also cover nonlethal/friendly controls and unsupported input without action echo or resource loss.
  Remaining native acceptance covers duplicates, removal/readd, cooldown refresh and transient lifecycle.
  Verify held death notifications, Vaal Haste/Grace aura costs, death/resurrection and slow destination loading.
  Include attribute gear removal before inventory notifications; current automated coverage is not native proof.
  See [27-vaal-souls.md](../docs/protocol/27-vaal-souls.md) and Status for the explicit LocalExile policies.
  Vaal executors, payment, official distribution/capacity parity and the SGP countdown require separate work.
  Unsupported Vaal casts remain refused; Behemoth and whole-class enablement remain separate.

### Death screen variants

- **Known:** S→C 0x55 flags bit0/2/3 are mapped ([11](../docs/protocol/11-skills.md) §7);
  bit4 and the trailing u32 / u8 / u16 are not.
- **Done when:** Sanctum and other modes show their own options.
  Hardcore Exit (C→S 0x153) is implemented; death migration and permanent Ruthless death are tracked in
  [league modes](../docs/game/league-modes.md).

### Skill projectiles

- **Known:** the [framework](../docs/game/projectile-framework.md) owns production projectile routing and the
  only Core simulator. Catalog-known gems and ordinary bows and wands have basic straight flight and resolved
  modifiers; [registered skills](../docs/game/projectile-skills.md) supply terminal Fireball blasts and separate KB
  clusters. Clustering converts quantity to explosions and forbids Split; the internal AltY row is a compatibility alias.
  Direct admission, independent explosion overlap and supported area scaling have automated runtime/input coverage.
  Chronological contacts, hit quotas, pause/transfer, release snapshots and held-output independence have automated
  production-input coverage, including raw item modifiers, scoped area damage and continuation/lifetime boundaries.
  Ordinary wand ground/object attacks share the bow's release path, the native main-hand contact and the bolt's rig
  impact animation; wand main-hand variation follows each class ACT pool and the native input-seeded selector
  (extraction manifest version 2 supplies the ordered stance pools and exact rig contacts for all seven classes).
  Complete off-hand timing/damage and nonzero intrinsic attack repeats remain open.
  Basic catalog flight does not implement all special skill behavior; unknown metadata is refused before costs.
  Issue #75 accuracy, shared critical rolls, impale and leech are integrated with automated composition coverage.
  Earlier native packet/collision controls need retesting on the new path; actor positions still use integer walk
  estimates and shared projectile radii; Gateway actor radii are zero, making area coverage center-based even
  for large monsters. Calibrate combat body geometry against the available movement `ObjectSize` data.
  Monster projectiles beyond mortars: `monster_projectile_weapon`, `monster_snipe` and `monster_puncture` skills
  (109 varieties' first effect) take their projectile from the monster's weapon art (MonsterVarieties
  `Weapon1_ItemVisualIdentityKeys`, e.g. `SkeletonArrow1`), a link the server does not read yet, so those monsters
  still hit instantly at half the attack time with no visible shot. Monster spell projectiles
  (`MonsterProjectileSpell`) are a separate adapter. The mortar splash radius is a development default and
  `projectile_spread_radius` (3) is not modelled. Mortars release at their clip's contact keyframe and point
  (`data/monster-contacts.json`); melee swings still land at half the attack time although the same pools carry
  `Melee` keyframes. Open: whether the client seeds a monster action's clip choice from the 0xffff sequence (the
  Spitter's attack2 would spit 75 ms earlier and 1.3 cells farther back), the grid handedness of a keyframe's lateral
  offset (not applied), MonsterVarieties `MinimumAttackDistance` (6 for the Spitter) and the minimum-range dead zone,
  all to check live.
- **Live reference** ([port record](../docs/dev/ranged-monster-actions-port.md) "Live reference", 2026-10-29/30): GGG's
  server sends no object for a player's bow shot or a Sand Spitter's mortar, and the client draws both.
  Find what stops our client from drawing its own (try live-style skill ids, `0x4000` for the default attack, and the
  live `0x1af` fields: main-hand `0x0200`, skill index 2, the header u32/u16), then drop the server arrow objects.
  For monsters, try real action sequences (live `0x80bc`…) instead of `0xffff` in the attack's `0x1ab` and its `0x1af`
  (skill index 2), and see whether the client then draws the mortar.
  Explain the hit-timing fit (ours releases at the table's 299.9 cells/s, close to live's 308, yet hits arrive
  2.72 ms per cell against live's 3.25) and add the 300 ms stun bit on hits.
  Find why a level 1 Sand Spitter hits for 5–7 here and a constant 4 on live.
  Decode S→C `0x1b4` (player only, the attack's skill id and a point list).
- **Start:** user-run Fireball/KB checks from the skill guide, including return, terrain and overlapping explosions.
  Check the aimed-height model (a shot below a cliff stops at the face, one aimed on top clears it) against the live
  client, then find the client's own projectile height/terrain rule for its predicted gem projectiles and replace the
  12-step clearance; the wall-hit sound of a server `DefaultArrow` stopping at terrain is not sent yet.
  Calibrate cluster radial distribution, counter-clockwise sequence timing and native art against client evidence.
  Build a server-side surface-height/world model for platforms and doodads (#182), height grids for more areas
  (or in the walk-map recorder), and collision with doodads and objects that have their own colliders.
- **Done when:** Fireball and bow arrows look like the real ones in every area: at the caster's torso on terrain and
  platforms, stopping at walls and objects.
