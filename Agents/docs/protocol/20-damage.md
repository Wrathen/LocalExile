# 20. Damage: hits, conversion and damage over time

This is the model the Gateway uses for combat. It is deliberately separate from the protocol messages: 0x1b1 carries a
life total, so everything here happens before that total is sent. The reference is Path of Exile's
[Damage](https://www.poewiki.net/wiki/Damage), [Damage conversion](https://www.poewiki.net/wiki/Damage_conversion) and
[Damage over time](https://www.poewiki.net/wiki/Damage_over_time) pages, with the exact conversion bookkeeping checked
against the local Path of Building (`Tools/PathOfBuilding/src/Modules/CalcOffence.lua`: `dmgTypeList`,
`buildConversionTable` and `calcDamage`).

## 1. Types and order

A hit is a **packet** with a separate minimum/maximum range for each type:

| Type | Notes |
|---|---|
| Physical | reduced by the defender's armour and physical damage reduction |
| Lightning | elemental |
| Cold | elemental |
| Fire | elemental; its damage over time is called burning |
| Chaos | not elemental; ignores energy shield in the real game |

Conversion only goes **forward** in this order:

```
physical → lightning → cold → fire → chaos
```

So physical can become fire, lightning or cold; cold can become fire or chaos; fire can become chaos. Fire cannot
become cold, and chaos does not convert. The `DamageType` enum is numbered in this same order (the old enum's numeric
values were not used on the wire).

## 2. Hit pipeline

**Issue #75 evidence scope:** test counts and native matrices imported with the completed jobs are historical
LocalExile results. Standard/Ruthless observations do not add private game-mode support to this branch or certify
its current native behavior. The [port record](../dev/issue75-port.md) identifies revisions, fresh automated
results and remaining checks. Source `artifacts/issue75-completion/` paths remain in the LocalExile checkout.

```
base skill/weapon damage by type
  → added damage (DamageStats.RawAdded; the first added-damage stat is read, see §2)
  → gain X% as extra (does not consume the source)
  → conversion (source is consumed; converted damage can convert again)
  → increased / more multipliers per type
  → critical strike
  → per-type resistance / mitigation (the critical-strike elemental rules resolve first, §4)
  → roll a whole number per type and send the life change
```

`DamagePacket` is immutable and holds one `DamageRange` per type. `DamageConversion` holds global conversion,
skill conversion and gain-as-extra percentages. `DamageConversion.Apply` processes the types in the table's order, so a
converted portion reaches the next type and can be converted again. Skill conversion is applied first; global
conversion fills whatever remains of the 100% cap, and the final conversion for a pair is stored as a whole percent,
as in Path of Building. Gains are added to the destination without reducing the source, matching the wiki's
"gain as extra" rule.

A player hit on a monster rolls each type once and then takes the monster's own base resistance on each type
(MonsterResistanceCatalog, §4), before the critical multiplier and the life update. A monster's attack on the player
does the mirror pass through the player's resistances. Damage over time is outside this pass: it has no hit and no
penetration. Righteous Fire's monster burn (its per-tick percentage-of-life model) and the player's ignites take the
monster's capped resistance of their type, its base and its mods' as for hits, and its damage taken (`MonsterDotTaken`;
before the ignites, the burn took only the mods' resistance).

Penetration lowers the target's resistance before the damage is reduced. The server caps the monster's base
resistance first (the 75 default, the 90 absolute maximum, the −200 floor) and subtracts the hit's penetration from
that capped value with no second clamp, as Path of Building's `calcResistForType` and hit branch do (16de4b8, lines
494..509 and 3490..3500): the effective resistance can go below the floor, so penetration against a negative
resistance deals more than the raw damage. Penetration is read per hit from the player's and the skill's own stats
and never touches the rolled input; the critical multiplier still applies once, to the mitigated total.
Player and skill contributions to the same raw penetration stat add in `Penetration.Of`.
All three hit paths collect those contributions before the general damage-stat dictionary overlays skill values.
Nine real-client checks read the target's native Life component after each authoritative update.
A level-68 fire skeleton took 60 from fixed 100 fire, then 80, 90 and 100 as penetration sources stacked.
The prohibition restored 60; its zero cold resistance took 100, then 120 with 20 cold penetration.
Map Hillock's 25% chaos resistance took 75, then 105 with 30 attack chaos penetration.

The unconditional stats the server reads (whole percents, this client's Stats.dat names):

| Stats | Scope |
|---|---|
| `base_reduce_enemy_{fire,cold,lightning}_resistance_%` | every hit on that element; the historical penetration stats despite the name (the per-element Penetration support gems grant them, as do the Heart of Flame/Ice/Thunder passives and the jewel mods) |
| `base_penetrate_elemental_resistances_%` | every hit on fire, cold and lightning (the Inquisitor's start node, Martyr's Legacy) |
| `reduce_enemy_elemental_resistance_%`, `reduce_enemy_chaos_resistance_%` | raw general inputs for elemental and chaos hits respectively; level-one Elemental Penetration Support grants 14 through the elemental input |
| `base_attack_damage_penetrates_{fire,cold,lightning,chaos}_resist_%` | attacks only |
| `base_attack_damage_penetrates_elemental_resist_%` | attacks only, on fire, cold and lightning (Forces of Nature, Warden) |
| `local_{fire,cold,lightning,chaos,elemental}_penetration_%` | attacks using the explicitly supplied, qualified active weapon only; spells exclude these inputs |
| `cannot_penetrate_or_ignore_elemental_resistances` | a flag: every elemental penetration is off for the player's hits; chaos is not elemental |

The derived `main_hand_…`/`off_hand_…` and per-element display aliases are not raw inputs to combat.
`WeaponIn` captures an immutable local-source dictionary with the weapon's damage. Ordinary melee uses that
accepted weapon; ordinary bows freeze its penetration at release. Supported projectile/area attacks use their
current main-hand weapon at contact, consistent with their existing damage path. Inactive-set items, shields and
unqualified weapons cannot contribute. The current executors still lack off-hand attack selection.
The source totals use wide intermediates and saturate once at the integer representation boundary.
Either player or skill prohibition suppresses elemental penetration even if signed flag values would cancel.
The non-critical companion is handled by the critical resistance rules below; other conditions remain separate work.
Spells take only general sources; physical damage never penetrates.

The issue-75 review added parsed contact regressions for ordinary melee/bow, Cyclone, Burning Arrow and Fireball,
each with and without prohibition. General elemental 7 + attack-only 5 + a real qualified local modifier 5 gives
17 for attacks and 7 for Fireball; an inactive weapon's 30 is excluded. Fixed 1,000 fire takes 1,170 life on an
unresistant ordinary attack, or 1,000 under prohibition. These are automated server checks, not new native captures.
Selected real Elemental Penetration support checks verify actor 10 + direct 7 + own 14 = 31, attacks adding 5,
explicit zero/restoration under prohibition, and chaos remaining 11.

Decision: pass the attacking item's raw local sources separately and retain the current executors' weapon timing.
The alternative, feeding published hand aliases back into combat, would double-count their global/skill inputs.
Changing attack-hand selection or all projectile snapshot semantics is a separate mechanic, requiring its own tests.
Reference evidence is the local library's PoB snapshot `16de4b82d57f1c0de6eb40f37143c32d4da36a02`
(`pob/skills/SupportElementalPenetration`, level 1; `pob/mods/Item~1SynthesisImplicitLocalElementalPen3`)
and the extracted 3.29.3b tables. The library is a labelled 3.29.x fallback, not proof of exact patch parity.
Validation decision: a bow lifecycle regression now passes one queued send at a time and holds the following
send before changing the player's action. This replaces polling a short-lived object, which can miss creation
under scheduler load. Raising its timeout would not fix that observation race. Production flight is unchanged.
For shared-machine native checks, wait for other agents' clients/listeners to exit before using `live.ps1`;
that script restarts fixed-port services and is not safe to run over another agent's validation session.
Stop this task's own test processes afterward. Automated tests use separate loopback connections and can continue.

The same packet is used for attacks, spells and monster hits. The caller decides whether the attack-only or spell-only
stats apply; `DamageStats` reads the Stats.dat names:

| Kind | Example stat |
|---|---|
| base conversion | `base_physical_damage_%_to_convert_to_fire` |
| skill conversion | `skill_physical_damage_%_to_convert_to_cold` |
| global conversion | `physical_damage_%_to_convert_to_lightning` |
| attack conversion | `attack_physical_damage_%_to_convert_to_fire` |
| spell conversion | `spell_physical_damage_%_to_convert_to_fire` |
| gain as extra | `physical_damage_%_to_add_as_fire` |
| non-chaos as chaos | `non_chaos_damage_to_add_as_chaos_damage_%` |

`skill_…` stats use the skill-conversion table; `base_…`, `attack_…`, `spell_…` and the plain
`…_damage_%_to_convert_to_…` names use the global table. Conditional variants ("while affected by Anger", "against
frozen enemies", ...) are not read yet. Skill conversion takes precedence when skill and global conversion together
exceed 100%, matching PoB's `buildConversionTable`.

`DamageConversion.Trace` provides a separate raw-history view for player modifier resolution. Each immutable
`DamageConversionPath` carries its final type, fractional range and the set of original/intermediate/final types
it visited. Equal final types with different histories stay separate; only equal histories merge. The forward
five-type graph bounds the result to 31 nonempty histories. Conversion and gain use the same existing normalized
coefficients, including skill priority, while gain keeps its source and can convert onward.
The quantity-only `Apply` path retains its original arithmetic and accumulation order for existing consumers;
it is not implemented by collapsing `Trace`. Collapsing separate histories can regroup floating sums.
Twenty-one new Core cases and twelve existing packet cases pass. The full Core suite passes 648 cases.
The seam preserves the existing whole-percent normalization boundary
where separately rounded 50.5/49.5 becomes 51/50; official endpoint/normalization parity is not established here.
Reference: pinned PoB `16de4b82`, `CalcOffence.lua` 35-140 and 1938-1982, via the PoE Reference Library.

`PlayerHitDamage.Resolve` now uses that history for ordinary and supported player hits. Callers supply raw item/class
or extracted spell base plus raw added damage, with attack effectiveness on the complete attack base and spell
effectiveness only on added damage. Each path receives one combined increased/reduced bucket: generic/context
inputs once, each visited typed increase once, and Elemental once if any elemental type was visited. Clamp only the
completed bucket, so a negative source-type subtotal can recover through a positive destination increase.
Each applicable support/aura more source also follows the visited-type set once. The existing three no-damage flags
inspect final destination only; suppressing Elemental does not erase a portion that becomes Chaos.
Merge final types after these operations, then retain the existing roll/mitigation and panel projections.

All hit paths now receive the existing `ChargeEffects.DamageMore` once, including ordinary ranged and attack gems
that previously omitted it. `WithCharges` expands increased/added sources separately; it does not replace that more
factor. Spell-only Power factors remain spell-only, and attack Rage stays outside the resolver once.
Keep the established spell aura/charge grouping and the ordered general/physical then elemental/chaos support
products, qualifying both products against full history. This preserves demonstrated no-conversion floor boundaries
117 and 27/45 without an epsilon or a new endpoint rule. Ordinary ranged packets still freeze at release.
Forty-three new Gateway cases and 340 focused checks pass, followed by all 3602 Gateway tests; Core 648 and Data 37
also pass. Fixed reference examples include mixed histories, negative original subtotals, gains/chains, typed more,
real Fireball/Burning Arrow, final prohibitions, source removal and zero/one/three-charge snapshots.
The previous native no-conversion support projections (1599/2131 and 51153/68205, the latter 75979/101305 since
the effectiveness offset decode of 11-skills.md raised Fireball's added-damage effectiveness to 300%) have exact
regression controls.
Thirty-two native numeric controls pass, sixteen each in Standard and Ruthless: Fireball conversion/gain, added Cold,
Void Manipulation's final elemental prohibition, ordinary bow partial/full/chained conversion, and supported Burning
Arrow with one/three effective Frenzy charges. Positive rows bind natural formatter reads to the fresh owned selected
skill primary. This verifies local panel publication, not a separate native authoritative-hit or official-server comparison.

Twenty-four controls pass the original explicit-field gates. Eight ordinary-bow controls use a separate sparse-zero
policy: the packet omits absent damage types, and actual natural getter returns on the exact current receiver prove
their effective zero values. Absence alone is insufficient; these are not explicit wire zeros or rendered hidden rows.
The first Standard ordinary baseline is a retrospective diagnostic after the preserved original presence failure;
the other seven sparse controls use the policy frozen before their source stage. The numerical oracle did not change.
Earlier filesystem-transport interruptions and their cleanup receipts remain preserved separately.

Four further controls verify canonical item/socket restoration and a fresh login in both modes. All 184 normal item
and socket mutations have full payload acknowledgements. The original account, two character metadata/saves, two
stash banks and forty tabs retain exact UTF-8 payload hashes; the two fixture inventories persist with their complete
canonical contents. All 714 frozen source/runtime/helper/oracle inputs match at session end. The supplemental sparse
policy, verifier and normal-source wrapper have separate recorded freezes and are not included in that 714 count.
The copied test client is closed and the normal Gateway uses `data/saves.db` again.

The full test run also retained six existing `MonsterKilled` cleanup writes to disposed streams, with no targeted
conversion fault in the filtered diagnostic capture. The native log retained four remote-host connection closures;
this is not an exception-free-run claim. Evidence: `artifacts/issue75-completion/conversion-native/verified-matrix-independent.json`
and `diagnostic-hook/conversion-final-diagnostic-assessment.json`.
Monster, ailment and DoT consumers remain outside this player-hit change; conditional/minimum-/maximum-only
operators, critical-weighted DPS, authoritative offhand alternation and official fractional parity remain open.

## 2a. Equipped weapon-local damage

`ItemWeaponStats` constructs one typed item packet from the active, requirement-enabled weapon before character,
skill and support modifiers. Physical endpoints use `(base + local flat) * (1 + local physical increase/100)`
times the separate `1 + effective quality/100` factor, rounding halfway up once at the completed item value.
Effective quality includes `local_item_quality_+`; `local_quality_does_not_increase_physical_damage` removes its
physical factor. Local flat ranges cover all five types. `local_elemental_damage_+%` scales Fire/Cold/Lightning,
while Chaos remains outside that elemental increase. `local_weapon_no_physical_damage` suppresses the item's
physical range without suppressing character-added physical damage.

The formula follows the reference library's pinned PoB `16de4b82d57f1c0de6eb40f37143c32d4da36a02`,
`src/Classes/Item.lua` local weapon calculation; the saved Quality page includes the 3.25 multiplicative change.
Local attack speed enters item duration once. Character speed then includes global increases and the existing
inherent dual-wield/unarmed factor. Integer-ms rounding and the numeric denominator guard retain local timing
conventions; they do not establish official-server fractional timing or a minimum attack rate.

Ordinary attacks, released bows/wands and supported attacks consume the typed packet with the actual
melee/projectile context. Strength's melee physical increase therefore does not scale a bow or wand.
The shared packet then receives conversion and Rage once. A fresh ordinary-hand packet publishes its present
types and replaces removed fields; supported attack/spell packets also publish explicit zero endpoints. Active rows 2/3 supply hand-local modifiers; inactive rows 14/15 supply none.
Pool, timing, charge and block conditions also use the active weapon profile; an inactive dual set or shield
cannot grant current speed, block or conditional bonuses. Shield base block requires an enabled active offhand.
Pinned PoB `CalcSetup.lua` 840-845 excludes unselected weapon-set slots before `CalcPerform.lua` 147-153/221-227
derives shield, unarmed and dual-wield conditions from the active item context.
Offhand publication is covered separately from the still-unimplemented authoritative offhand attack alternation.
Alternative quality bonuses and local base-critical modifiers remain separate work.
The player resolver retains visited-type qualifiers for destination/intermediate increases and typed more effects
after conversion to Chaos. The separate 32-control conversion matrix above verifies that integration's local panel
publication; the earlier local-weapon controls below retain their original narrower scope.

Helper/gateway regressions cover quality, signed/local modifiers, typed damage, suppression, timing and active-set
scope. All 24 native publication controls passed in Standard/Ruthless, including typed ranges, swap/removal and
inactive-set exclusion. The independent W03 native item tooltip rendered 36-70 physical damage and 1.80 attacks
per second at quality 20 with its local physical/flat/speed modifiers. Source/DLL hashes and inventories held.
Other variant tooltips, exact fractional timing and supported-contact composition remain outside that native proof.
Evidence: `artifacts/issue75-completion/weapon-native/`, including the preserved inactive-profile failure.

## 2b. Added damage from stats (the first stat)

Added damage is not fully modelled yet, but one stat is read.
`DamageStats.RawAdded` adds `attack_skills_have_added_lightning_damage_equal_to_%_of_maximum_mana` (Stats.dat row
11881, client key 11882) to an attack's added-lightning range.
The value is the percentage: both the added lightning minimum and maximum become that percent of `maximum_mana`
from the same final stat dictionary, so spending mana and reserved mana never change the damage, while a pool
change (level, equipment, passives) does.
Path of Building pinned 16de4b82 (ModParser.lua lines 5636–5639) maps the node's value to both LightningMin and
LightningMax as BASE with PercentStat Mana and ModFlag Attack; the packet agrees.
At 6% of a 1000 mana pool (Whisperer "we know all", PassiveSkills.dat graph id 22720) the attack gets 60-60
added lightning, not 1-60.
The pool bonus joins the added range before the increases, so the increased attack-damage stats and later
conversion treat it like any other added lightning, and a fractional result is only rounded at the existing roll
and panel stages.
Ordinary attacks use `AttackHit` or the bow release snapshot; attack gems retain main's `SupportedHit` pipeline.
Both constructors read the same raw `DamageStats.ManaAddedLightning` grant before increases and effectiveness.
Support restrictions, support multipliers, conversion, target blocking and post-hit stages remain in that pipeline.
This stack does not introduce the separate derived-damage engine from archival PR #206.
Automated production-path cases cover ranged and melee gems, effectiveness and a spell isolation control.
Native evidence here belongs to original head `378ffde`; supported-skill composition needs a new native check.
`attack_damage_+%` scales the added range for every attack.
`melee_damage_+%` applies only in explicit melee context: the default attack uses its active weapon profile,
and attack gems use `ActiveSkillType Melee` through `SkillCatalog.IsMelee`.
An inactive weapon set cannot change that context.
Spell hits and damage over time are untouched, and saved gem quality/experience is not read.
The node's separate mana-cost drawback (`base_mana_cost_-%`) is a mana-cost change outside this damage
consumption's scope.
Fourteen real-client stat checks used a fresh synthetic Witch and development overrides for these inputs.
With the 6% grant, 56 maximum mana showed 3–3 added lightning and 1000 showed 60–60.
Adding 50% increased lightning showed 90–90; 997 maximum mana showed 59–59 after the panel's existing floor.
With 1000 mana, 50% increased attack damage showed 90–90; another 50% melee increase raised unarmed damage to 120–120.
A wand stayed at 90–90 even with a club in the inactive set; switching to the club gave 120–120 and back gave 90–90.
Clearing the attack increase left the ranged wand at 60–60 despite the melee input.
Clearing the grant removed the lightning rows' damage, and relog restored the baseline maximum mana and damage.
These checks validate the generic damage consumer; ascendancy allocation remains in its separate lifecycle change.

## 2c. Arcane Might: spell increases also apply to attacks

`ArcaneMight` selects the highest eligible grant from six modern client stat contracts, then multiplies the
resolved `spell_damage_+%` (key 26) by that ratio.
The result joins the attack's increased/reduced bucket; it is not a more multiplier.
At 150%, base 100, generic increased 100% and spell increased 20% produce 230.
One percent spell increase contributes 1.5%; -20% contributes -30%, retained until existing rounding stages.
A fixed-150 flag value of 2 still enables 150%, and independent 150% sources never combine to 300%.
See the [source inventory, contracts and boundaries](../game/arcane-might.md).

Independent item/passive/actor/skill grants use maximum numeric ratios or boolean flag union.
A skill's own base ratio and quality contribution still add before comparing it with those independent grants.
Weapon snapshots carry active wand and melee context; supported attacks obtain retaliation eligibility from
ActiveSkillType 120, with ordinary attacks ineligible.
An inactive wand cannot enable the wand mastery.
The resolver consumes the supplied resolved spell increase; it does not implement Iron Will or track modifier origins.
Spell-only flat damage, more multipliers and cast speed do not transfer.

`DamageStats.SpellAttackIncrease` feeds weapon physical/local damage, added attack ranges and supported packets.
Local weapon elemental damage now enters supported attacks before increases, effectiveness, restrictions and conversion.
Maximum-mana additions keep their existing order and receive the contribution once.
Final skill panels clear damage types removed by support restrictions, including Brutality's elemental exclusion.
Ordinary bows and gem direct/area packets retain release snapshots; later calculations use the current resolved input.
Panel projection neither mutates raw spell inputs nor compounds the derived contribution.
Normal Crown of Eyes has a curated definition with current real item mods; unrelated leech remains unsupported.

Automated checks cover all six contracts, real item/passive/mastery sources, 24 skill constants and three quality records.
Gateway regressions cover mastery refund, active/inactive weapons, Crown persistence/removal, stronger-grant fallback,
repeated panels, local weapon damage/support restrictions, spell isolation and actual direct/area target Life loss.
Data coverage does not enable unsupported variants, retaliation skills, mercenaries or Phrecia allocation.
The pinned [PoB calculation](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82d57f1c0de6eb40f37143c32d4da36a02/src/Modules/CalcOffence.lua#L650)
uses maximum ratios and copies increased/reduced modifiers with provenance; exact 3.29.3b parity is unverified.
This bounded scalar consumer does not reproduce PoB's Strength anti-double-dipping.

Historical native checks on a fresh unascended Witch used Whisperer's raw fixed-150 flag (node 50932, key 11811).
Fixed 100 chaos became 250 with 100% spell increase; generic 100% plus spell 20% produced 230.
Base 200 with 1% spell increase produced 203 in both panel and target Life; spell -20% with generic 100% produced 170.
A flag value of 2 still used 150%; removal/relog cleared it and spell more/cast-speed inputs added no attack damage.
Those checks belong to the earlier raw-stat consumer, not this expanded implementation's native acceptance.
Crown, mastery and current composition still need client verification by the user.

## 2d. Behemoth: attack damage from final armour

Forest Tracking (Daughter of Oshabi node 6281) now shares this increased-attack calculation.
Client key 2434, `attack_damage_+%_per_450_evasion`, contributes 3 per complete 450 final evasion.
Both armour and evasion contributions add to the raw attack increase before one signed clamp.
Final evasion after Iron Reflexes supplies no evasion groups; the resulting armour can still supply Steel Willed groups.
See [Forest Tracking](../game/ascendancies.md#forest-tracking-attack-damage) for scope and lifecycle checks.

Steel Willed (3.29.3b PassiveSkills graph node 2103) grants
`attack_damage_+%_per_450_physical_damage_reduction_rating` with value 2, stat key 13019.
`ArmourAttackDamage` adds `floor(max(0, final armour) / 450) * value` to resolved `attack_damage_+%` once.
Thus 449 armour gives no bonus, 450 gives 2%, 899 still gives 2%, and 900 gives 4%.
This is an increased modifier, additive with other increases; base 100 plus generic 100% and this 4% gives 204.
The complete groups use the final rating after existing defence increases and conversions.
An explicit development override of the final rating replaces that input; it is not raised by armour increases again.
The calculation changes only the temporary character-stat dictionary, never the raw passive/equipment/dev sources.
Repeated builds cannot compound it, and removing the modifier or changing armour immediately changes the next packet.

Path of Building pinned 16de4b82 maps "per N armour" to a PerStat Armour/div tag in
[ModParser](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82/src/Modules/ModParser.lua#L1472).
[ModStore](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82/src/Classes/ModStore.lua#L460)
floors the groups before multiplying the modifier.
The separate chance to double armour against a hit does not change this rating; see section 2f.

The shared increased-attack stat feeds supported attacks and added damage, including maximum-mana lightning.
Weapon physical damage now consumes that same generic attack stat, with signed sums kept wide before cancellation.
All three attack constructors retain fractional damage and multiply before dividing to preserve whole endpoints.
Existing effectiveness, conversion, support restrictions and bow release snapshots retain their order.
Spell damage receives none of this attack-only increase.
Automated checks cover the actual node/key, thresholds, additive and signed cases, repeated builds, removal,
final-defence composition, bow snapshots, mana lightning and socketed Burning Arrow/Cyclone/Fireball/Brutality paths.
Fourteen native checks on a fresh unascended Witch use raw development inputs rather than Phrecia allocation.
Ten fixed-chaos cases read both panel ranges and actual monster life: 100 at 449 armour, 102 at 450/899 and 104 at 900.
Generic 100% gives 204; another raw 10% attack increase gives 214, and a modifier value of 3 at 900 armour gives 106.
Negative armour and modifier removal restore 100; maximum-mana lightning composes to a native 62-damage hit.
Two additional panel-only checks show weapon physical 2–5 becoming 6–15 at 45000 armour.
Relog clears the development effect.
Phrecia node admission and the rest of Behemoth remain separate work; this consumer does not enable a new chooser.

## 2e. Armour against incoming chaos hits

`base_armour_applies_to_chaos_damage` (client key 12064) is a boolean source.
The current `PassiveSkills` node 29437, Incorruptible, grants value 1.
`PassiveBonus` carries it through `CharacterStats.Bonus` into `Pools.ArmourAppliesToChaosDamage`.
It never changes `Pools.Armour`, the native final rating or Steel Willed's offensive input.

Incoming chaos hits first take the ordinary capped chaos resistance, including the Kitava penalty.
`PlayerHitMitigation.AfterResistance` then uses the existing rounded armour model against that remaining damage.
For example, 1000 chaos against 50% resistance and 1000 armour leaves 500 before armour.
The armour reduction is 29%, so 355 remains; using the raw 1000 in the denominator would incorrectly leave 415.
The existing 90% armour mitigation limit applies, with no additional physical reduction contributed to chaos.
Physical and elemental reduction retain their existing rules and per-type rounding.
Immunity zeroes the component before this pass.
Fortification follows mitigation; suppression and block keep their existing command/contact ordering.
Energy Shield then absorbs non-chaos damage, while the mitigated chaos share still bypasses it.
Mixed monster hits mitigate each type before computing that bypass share and the existing Fortification floors.

The calculation order comes from Path of Building commit `16de4b82d57f1c0de6eb40f37143c32d4da36a02`.
Its [CalcDefence.lua][pob-chaos-armour], `applyDmgTakenConversion` lines 97-116, uses resistance-adjusted damage
in the armour denominator and keeps additional reduction separate per damage type.
LocalExile retains main's existing integer rounding instead of changing the global physical model.

[pob-chaos-armour]:
  https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/16de4b82/src/Modules/CalcDefence.lua#L97-L116

`MonsterSwing`, `/hit`, `/spellhit` and `/blockhit` share the hit-only mitigation pass.
DoT uses its resistance-only path and never enters this helper.
`/set_stat base_armour_applies_to_chaos_damage 1` enables the raw development source.
Value 0 replaces a passive source with false; `off` removes the replacement and rebuilds from current sources.
Development values are connection-local and do not survive relog.
Raw-node tests inject 29437 only as a stat fixture and do not establish Phrecia allocation or class selection.

Automated checks cover resistance ordering/caps, flag-off identity, immunity, zero/extreme armour,
physical-reduction isolation, final rating/offence, source replacement/removal and fresh connection reload.
Framed chat dispatch and real monster swing tests cover suppression, mixed damage, block, ES and Fortification.
Current-main integration preserves Strength-derived Mana and Chaos Inoculation in the same pool calculation.
Regression controls cover complete Strength groups, independent raw-source removal and current-Mana clamping.
The parsed Mana refresh retains its send-gate snapshot; restoring a source does not refill current Mana.
Eleven native controls use a new unascended Marauder, raw key 12064 and a level-one Plate Vest fixture.
With 969 Armour, a raw 20-chaos hit removes 20 Life while disabled and two while enabled.
At 50% chaos resistance it removes one Life with the enabled armour pass.
Physical and fire controls preserve their ordinary behavior.
At 50 Armour and 50% chaos resistance, the same chaos hit removes five Life even with 80% additional physical
and elemental reductions active; controls confirm those reductions still affect their own types.
The native Armour rating stays unchanged when the flag toggles.
Explicit zero, removing the development replacement, source restoration and relog are verified.
These controls establish actual player Life loss for the development hit route.
Mixed monster packets, suppression, block, ES, Fortification and DoT separation retain automated coverage.
The raw-source controls do not select Incorruptible or establish whole-class gameplay.
The test records the exact embedded Git versions, DLL hash and process working directory.
Recent-hit armour composition is covered below; other class mechanics remain separate work.
Implemented local item defences feed the shared armour pool (see [13](13-items.md) §2 and §2c).

A share of armour can apply to non-physical hits too (`PlayerHitMitigation.ArmourPercent`, PoB CalcDefence
`percentOfArmourApplies`, the BASE sum capped at 100%): `armour_%_applies_to_fire_cold_lightning_damage` for fire, cold
and lightning, `base_armour_%_applies_to_chaos_damage` for chaos (all of it with the flag above).
That share of the hit's effective armour rating goes through the same armour formula against the damage after
resistance, and the reduction adds to the elemental damage reduction under the 90% cap.
Kahuturoa's Leadership gives 10% of each: 5000 armour against a 100 fire hit after resistance is 500, so the hit loses
50%.
The armour decision of a hit counts such a component as armour-relevant.
`MonsterSwing`, `/hit`, `/spellhit` and `/blockhit` apply it.

## 2f. Steel Willed: temporary incoming-hit armour

`RecentEnemyHits` implements the bounded double-armour consumer, independently of the outgoing bonus in section 2d.
The prior four seconds of accepted enemy contacts supply key 4534.
Key 20680 scales that count with a 30% contribution cap, then generic chance key 8404 adds before a 0..100 clamp.
The result is published as virtual key 12268.
One lazy decision per unblocked relevant hit supplies physical and enabled chaos mitigation without changing pools.
The current contact records after resolution, including blocked/zero-damage contacts, under the same combat lock.
Death, shutdown, area entry and delayed-swing epochs use the explicit connection lifecycle.
The existing stat send gate and player tick publish current counts, including idle expiry.
See [Steel Willed armour](../game/steel-willed-armour.md) for primary addresses, model v1, dev-input semantics,
current-main regressions and the eighteen historical raw-input native checks from original head `777b292`.
The rebased head and exact batch composition await fresh native verification.

## 2g. Spell skills deal no damage (#293)

The extracted 3.29.3b `Stats.dat` row 5340 is `spell_skills_deal_no_damage`, client key 5341.
`PassiveSkills.dat` node 49937, Rampager, supplies value 1.
This consumer reads the raw stat already carried by the caster's character-stat dictionary.
It adds no pool field or pool rebuild, class admission, passive allocation exception or graph bypass.

`SkillDamageContext` takes an explicit skill kind and the actual caster's stats, with no implicit owner lookup.
A nonzero flag suppresses the entire outgoing Spell packet, including physical, elemental and chaos components.
Attack classification takes precedence for mixed Attack/Spell skills, matching `SkillHitPlan`.
Default melee/bow attacks and attack gems keep their damage, including bonuses granted by Spell-tagged auras.
Aura activation, reservation and buffs remain available.
The rule does not prohibit skill use: casting, mana costs, action timing, cooldowns and visuals retain their paths.

`SupportedHit` suppresses the final supported packet after added damage, conversion and support multipliers.
Existing projectile and area execution consumes that packet before rolling damage and applying target mitigation.
The standalone `SpellHit.Resolve` seam applies the same rule with its separate caster dictionary.
The final `GemSkills` record publishes the supported packet's spell and main-hand ranges, per-use damage and DPS.
Explicit zeros replace inherited weapon ranges, so server Life loss and native panel inputs agree.
Removing or zeroing the source restores the next packet without rebuilding pools.

Gem projectiles retain main's impact-time stat resolution, including flag addition or removal during flight.
Area hits resolve at their existing hit time; ordinary bows retain their existing release snapshots.
No projectile snapshot, Fireball impact art/shape (#262) or broader projectile execution (#258) is added here.
Their future spell impact paths can reuse the same explicit context.

RF enemy burn passes its outgoing `DamageOverTime` through the context on every existing monster tick.
Suppression sets the rate to zero, preserving fractional carry and enemy aura membership.
The resource-recovery loop's RF self-burn remains an independent self-damage cost, including ES absorption.
Restoring the source's damage resumes enemy burn without restarting RF.
This change does not implement a separate RF or other DoT panel; existing hitless-spell hit rows remain zero.

Zero-damage hit policy is preserved deliberately.
Supported hits still roll block/critical state, accept the hit, update the existing hit timestamp and run callbacks.
On-hit/noncritical charge events and existing notifications therefore still occur on an accepted zero-damage hit.
Main's generic on-hit Rage gain also remains eligible on accepted spell contacts, including suppressed hits.
Attack/melee Rage gains and Fortification still require their own attack/melee eligibility.
No primary evidence in this slice establishes a different callback rule; no damage-based hit-admission gate is added.
RF remains a DoT tick and does not create hit callbacks.

Main has no full owned-actor executor.
Independent minions or daemons must supply their own actual-caster stats to this seam when that executor is built.
An owner's suppression is not inherited merely because it owns the actor.
Future owner-derived integration needs explicit per-skill ownership rules and separate production-route coverage.
No archived actor executor, Rampage, Vaal or new resource cost is imported.

Automated checks cover actual supported spell constructors, selected native records and encrypted dispatch to Life.
They include physical/elemental/chaos hits, source default/on/off/removal, supported added chaos and charge callbacks.
Released Fireball tests witness the glide before adding or removing the source, then check impact Life.
Default melee/bow, attack gems, Anger/Wrath/Hatred attacks and RF enemy/self ticks are covered.
Current-main regressions compose local weapon quality, supported additions, charges, conversion and Rage.
Parsed source replacements preserve Strength-derived Mana, chaos-hit armour and Mageblood flask deduplication.
Maximum-Mana added lightning remains attack-only, and restoring a source does not refill current Mana.
Unpaid casts keep main's action-intent ordering without releasing damage or granting hit resources.
Utility reservation/cooldowns, cosmetic target identity and RF fractional carry retain combined regression coverage.
Historical native checks at original head `e80a81c` used an unascended Witch and raw development inputs.
The eleven controls used a synthetic level-one Plate Vest; they do not verify the rebased candidate.
Fireball dealt a fixed 200 chaos and Ice Nova 130 to actual native target Life; the flag reduced both to zero.
Native Fireball mouse actions exposed matching selected ranges and DPS, including explicit zeros with the flag.
Paid casts still corrected native Mana, signed nonzero flags suppressed damage, and removing the flag restored it.
A default attack still dealt 100 with the spell flag active.
With RF installed, enemy Life stayed at 4516 while actual player Life fell from 259 to 209.
Removing the flag resumed enemy burn to 4455; reapplying it stopped that existing burn without restarting RF.
Relog cleared the development source.
In-flight source changes, conversion/gain, supported additions and aura attack bonuses have automated coverage.
The ordered #290 / #294 / #296 / #300 integration at `c9cd1864` passes 28 fresh native controls.
On its new level-one unascended Marauder, a level-one Fireball deals fixed 200 chaos, then zero with the flag.
An attack still removes 100 native target Life while flagged; removing the flag restores Fireball's 200.
The same owner retains converted mana 29 and Armour 251; two existing synthetic accuracy mods ensure a 100% attack roll.
The real native Life reads and accepted-hit logs agree, with Gateway/Core/Storage/Host revisions and hashes pinned.
RF, Ice Nova, relog and selected-panel controls above remain attributed to the original head.
No native allocation of Rampager, owned-actor execution or whole-class acceptance is claimed.

## 2h. Ordinary attack evasion and critical confirmation

Ordinary melee and bow contacts now share `SpawnedMonster.AttackEvasion`, one entropy sequence per defender.
The first attack seeds it with a random 0..99 value. Each attempted hit adds its whole-percent hit chance;
reaching 100 accepts the hit and subtracts 100. The sequence resets after more than 3.33 game-clock seconds
without a check, matching the existing incoming-attack policy. A new spawn starts fresh. Pause does not advance
the clock. This timing is the current local policy; exact official tick boundaries remain unverified.

After an accepted hit rolls critical chance/luck, an independent accuracy check confirms that critical result.
Failure makes it a normal hit, retaining damage and normal on-hit behavior. Confirmation does not consume
entropy or apply critical luck again. The confirmed result reaches resistance selection, damage multiplier,
new impale storage, Fortification and critical callbacks. Existing reflected impale damage stays separate.
At 100% hit chance confirmation needs no random draw. A miss performs no critical roll or on-hit mutation.

The same contact chance includes the target's evasion modifiers and current smoke-ground Blind, plus the
attacker's Unique/close-range Accuracy Masteries. Ordinary bows freeze rating, mastery magnitudes and
unconditional cannot-evade flags at release; target rarity, position and evasion resolve at impact. Evaded
targets do not stop the arrow: the sweep can hit a later target once. Block is tested after evasion and before
damage/critical/impale. Melee's admission, evasion and damage now share the accepted-contact lock, preventing
cancelled/paused/dead-owner contacts from consuming outgoing accuracy state. Bow admission remains at its
existing relocation gate.

Raw `global_always_hit` from the owner/default skill, or `local_always_hit` from the qualified attacking weapon,
bypasses both accuracy checks. Inactive/off-hand weapon sources and final hand aliases cannot grant this flag.
This establishes ordinary, area and gem projectile accuracy, not all source conditions or selected publication:
other cannot-evade conditions,
off-hand execution and complete chance/always-hit aliases
remain open. Spell hits continue without attack accuracy. Native revalidation remains pending.

Selected flat/increased accuracy sources now use `SupportedAccuracy` and the exact original pool base.
Additional Accuracy's real flat/quality contributions update the selected rating/reference chance rows;
raw commands replace all corresponding actor contributions, including Precision, and refresh pools.
Decision: preserve the original base instead of reversing a floored final value, and apply support inputs before
the final floor. The alternative would mis-scale flat support values and lose fractional boundaries.
[Skills §4](11-skills.md) records source evidence and actual support/command tests.

Area gem attacks now sample that profile at contact, including target Masteries/evasion/Blind and raw cannot-evade
flags. `HitMonster` admits the action before evasion, block, critical confirmation and damage; rejected contacts
neither consume impales nor trigger Fortification/Rage/charge callbacks. Critical flask gains occur only after
accepted damage. Spells skip attack accuracy. Each target keeps its own entropy. Fourteen added cases cover
channelled/ordinary area attacks, real support inputs, misses, confirmation, cannot-evade, spells, cancellation
and independent targets; the expanded focused run passes 267. Native checks remain pending.
Decision: resolve contact rolls under the existing admission lock, rather than consuming random/target state
before discovering that an action is cancelled. Preserve explicit rejection results instead of converting a
miss/block into a zero-damage accepted hit, which would still trigger callbacks. Broader scope/conditions and native parity remain open.

Gem uses now capture signed luck at cost/admission and retain one `SkillCriticalRoll` across their area targets,
volley, piercing/return contacts and existing impact-area callbacks. Each hit compares that sampled value with
its current critical chance and prohibition; attack accuracy confirmation remains independent for every hit.
Decision: keep the random value, not a cached boolean, so changes between targets still matter. Capture luck
at admitted use rather than first impact. Allocate the values lazily to preserve the no-RNG behavior of rejected
contacts; this is a local allocation policy. [Skills conditional critical policy](11-skills.md) records the primary
GGG distinction and 22 new cases. Ordinary melee/object-bow uses now capture luck when their swing begins after
any approach; ground bows capture within their input/pacing admission. The context survives queued confirmation,
release and flight. Twenty additional controls cover Life/Focus/raw changes, both directions and walk-in timing.
Decision: capture at swing admission rather than the earlier walk-in request or later contact; keep the existing
bow damage/chance/multiplier snapshot pending its own evidence rather than changing unrelated lifetimes.
Special repeats and native parity remain open; the native virtual getter alone does not certify a roll's lifetime.

Gem projectile forward/piercing/return contacts now consume the selected profile under admitted sweeps.
Evaded targets continue the flight without spending pierce budget, triggering impact/impale/on-hit effects or
reserving the target in the volley-wide exclusion. Blocked contacts still collide. Critical confirmation receives
the very same contact chance before direct/return damage scaling. Gem owner inputs retain their existing
contact-time sampling; ordinary bows retain release snapshots. Decision: preserve those distinct lifecycles
instead of freezing gem inputs without snapshot evidence; retain explicit miss continuation instead of a
zero-damage collision. The forward exclusion is now allocated once per volley, matching its intended scope.
[Skills §10](11-skills.md) records primary volley evidence, 24 added tests, the 29-case focused run and unresolved
projectile geometry/exception rules. Current build/tests do not certify those separate mechanics or native parity.

[Mark_GGG's entropy explanation](https://www.pathofexile.com/forum/view-thread/11707/filter-account-type/staff/page/10)
establishes per-defender state, independent critical confirmation, inactivity resets and separate block checks.
His [later correction](https://www.reddit.com/r/pathofexile/comments/ezhhtc/comment/fgqomvw/?context=3)
confirms that misses do not roll critical chance and failed confirmation downgrades a landed hit.
Pinned PoB `16de4b82d57f1c0de6eb40f37143c32d4da36a02`, `CalcOffence.lua` hit-chance section, independently
applies cannot-be-evaded before the accuracy/evasion formula; native stat descriptions identify both raw flags.
These are labelled reference mechanics, not an official-server RNG-sequence claim for 3.29.3b.

Decision: use one defender-owned sequence and a separate confirmation draw, keeping the existing bow release
snapshot and game clock. Independent random misses would retain streaks; advancing entropy for confirmation
would wrongly prevent critical hits at low hit chance. Recomputing bow owner inputs at impact would change
the established release contract. Extend gem flight admission in a separate reviewable change rather than
treating an evaded projectile as a terminal zero-damage collision. Automated checks cover deterministic sequences,
expiry, parsed ordinary melee/bow confirmation, real local/global flags, pass-through, rejected actions,
target modifiers/Blind, source removal and noncritical impale storage.

## 2i. More maximum attack damage

`maximum_attack_damage_+%_final_from_ascendancy` (Devout of Ryslatha's 30%) scales the maximum of the attack's base and
added damage, which for a hit is the maximum of every final component after conversion; minima and spells are unchanged
(`MaximumAttackDamage`).
It also scales the damaging ailments an attack inflicts (the wiki; PoB builds an ailment's base through the same
`calcDamage`), so an attack's flat ailment packet must go through `MaximumAttackDamage.Apply` too (§3).
PoB CalcOffence applies `MaxDamage` MORE to each type's own maximum and carries it through conversion, which is the same.
It sits next to Rage's final multiplier at every attack site: the default attack and its panel rows, the native
physical bounds, attack gems, `AttackSkillHit` and ordinary bow and wand shots (`OrdinaryRangedShot`).
The client multiplies its three sources (volatility support, ascendancy, graft) into
`combined_maximum_attack_damage_+%_final` (`FUN_1425d25b0`); only the ascendancy one exists on the server.
See [Polytheist](../game/polytheist.md).

## 3. Damage over time and ailments

DoT is one damage type, has no hit, cannot critically strike and uses that type's resistance. `DamageOverTime` carries
the damage per second, the remaining seconds and an optional ailment. `Tick(seconds, carry)` returns whole damage and
the fraction to carry into the next server tick, so a 0.35 s tick does not lose damage; it never runs past
`RemainingSeconds`. Call `Advance` when applying a duration.

The modelled damaging ailments are:

| Ailment | Damage type | Base | Duration |
|---|---|---|---|
| Ignite | fire | 90% of the hit's fire damage per second | 4 s |
| Bleed | physical | 70% of the hit's physical damage per second (200% while moving is not modelled) | 5 s |
| Poison | chaos | 30% of the hit's physical + chaos damage per second, stacking | 2 s |

`Ailments.Ignite/Bleed/Poison` build the effect from a hit packet. Pass the **converted base packet before hit-only
increased/more modifiers**; the method's `increasedPct`/`more` arguments are the ailment-specific multipliers, so a
caller cannot double-count hit modifiers. For a specific hit pass its rolled packet; for expected damage pass the
average range. The caller applies the target's resistance. The player's chill, freeze and shock on monsters are
modelled ([below](#outgoing-chill-freeze-and-shock-on-monsters)), and so are his ignites (below); critical ailment
damage and the other damaging ailments' chance, application and duration on monsters are not modelled yet. Righteous
Fire still uses its existing per-tick percentage-of-life burn; it is a fire DoT but not an ignite.

**The player's ignites on monsters** (`MonsterIgnites.cs`, the wiki's Ignite): `IgniteFromHit` decides a landed hit's
ignite. A critical strike always ignites; otherwise `base_chance_to_ignite_%`, `chance_to_ignite_%` and
`chance_to_freeze_shock_ignite_%` give the chance, and `never_ignite` stops it. The base is the hit's flat fire damage
(base and added, after conversion, before increases), 90% per second (`Ailments.Ignite`) × (1 + the ignite increases:
`damage_+%`, `damage_over_time_+%`, `elemental_damage_+%`, `fire_damage_+%`, `fire_damage_over_time_+%`,
`burn_damage_+%`, `ignite_damage_+%`, `base_ailment_damage_+%`) × the Frenzy charges' more damage × (1 + the damage
over time multipliers `dot_multiplier_+`, `fire_dot_multiplier_+`, `ignite_dot_multiplier_+`, and on a critical strike
50 + `critical_ailment_dot_multiplier_+`). Hit-only modifiers (spell, area, attack damage, critical multiplier, lucky or
triple damage, penetration) never reach it. It lasts 4 s × (1 + `ignite_duration_+%`) × (1 −
`base_self_ignite_duration_-%` of the monster).
The "deal Damage faster" modifiers (`faster_burn_%`, `damaging_ailments_deal_damage_+%_faster`) multiply its damage per
second by (1 + their sum) and divide its duration by it.
The ignite goes onto the monster's one `MonsterIgnites` (`MonsterIgnites.Add`, through `IgniteLocked`), the same set
projectile hits add to (below), so every source shares one burn and one `ignited` buff.
Only Abberath's Fury's footstep hits use `IgniteFromHit` ([Polytheist](../game/polytheist.md)); projectile hits use the
panel rows captured at release (below); the default melee attack and melee or area gems do not ignite yet.
An attack's flat packet will go through `MaximumAttackDamage.Apply` (§2i) before it. The ignites burn on while the
player is dead (unverified), and a dead Hardcore character's kills make no progress. Shaper of Flames-style ignites from
other damage types and ignite proliferation are not modelled.

The character panel's 32 damaging-ailment rows (`minimum/maximum_{bleeding,ignite,poison}_damage_to_deal_per_minute_from_{non_critical,critical}_{main_hand,off_hand,spell,secondary}_damage`
and the `_to_always_deal_` poison rows) come from `AilmentPanelStats`, which takes the same raw packet and conversion as
the hit (the default attack per hand, a supported gem's hit per hand / as a spell / as secondary damage) and applies
the ailment's buckets instead of the hit's: each converted portion keeps the increases of the types it visited, plus
generic, attack or spell, melee, area and projectile damage, damage over time (generic and the ailment type's),
`bleeding_damage_+%` / `burn_damage_+%` / `base_poison_damage_+%`, elemental damage for ignite, then the
damage-over-time multipliers (`dot_multiplier_+`, the type's, and `critical_ailment_dot_multiplier_+` for the critical
rows: critical strikes give ailments nothing else) and the Frenzy Charges' more multiplier, times the base percentage
(70 / 90 / 30) and 60. The "from only Chaos Damage" rows use the chaos portion alone and show with a
`chaos_damage_chance_to_poison_%` source. Pinned PoB CalcOffence (16de4b82, `calcAilmentDamage` and the bleed /
poison / ignite sections) composes the same buckets. Not modelled: conditional sources, stacking, "as though dealing
more", support more multipliers, the moving bleed bonus, and the rows' native formatting. Burning Arrow's quality
(`active_skill_ignite_damage_+%_final`, 1% per quality) multiplies the ignite rows.

**Projectile ignite on monsters (`MonsterIgnites`, `Program.MonsterIgnites.cs`).** A gem projectile or ordinary bow/wand
shot captures its ignite at release: chance `base_chance_to_ignite_%` + `chance_to_freeze_shock_ignite_%` from the caster
and the skill (Burning Arrow's own 25%), the context's ignite rows above (main hand for attacks, spell for spells) as the
damage-per-second range, and 4 s × (1 + `ignite_duration_+%` + `base_all_ailment_duration_+%`), with the "deal damage
faster" factor above on both and the target's `base_self_ignite_duration_-%` on the duration. `never_ignite` and
`never_damaging_ailments` stop it. An accepted hit that dealt fire damage and left the target alive ignites on a critical
strike or a successful chance roll, then the monster's `avoid_ignite_%`; `immune_to_ignite` / `cannot_be_ignited` refuse
it. The ignite's damage is an independent roll in the non-critical or critical range (the critical rows already carry
the ailment critical multiplier). Several ignites can be on one monster; only the strongest deals damage, and a weaker,
longer one resumes when it ends. The player tick (`BurnIgnited`, 250 ms) integrates the strongest ignite through each start/end,
applies the monster's capped fire resistance and damage taken, carries fractions and commits kills as Righteous Fire's
burn does. The monster wears the native `ignited` buff (BuffDefinitions row 0, flame visual 2) with its timer to the
last ignite's end, re-sent (the old instance removed first) with each new ignite and removed at expiry or death. Not modelled: melee and area
ignite, ignite proliferation, "as though dealing more", support more multipliers and the panel's unmodelled buckets.

## 3a. Impale contacts and reflected damage

`ImpaleStats` resolves unconditional general + attack/spell chance, the attacking weapon's local chance, general
effect, spell-only effect, extra reflected hits and increased duration. The native spell-only effect name really is
`impale_effect_+%_for_impales_inficted_by_spells` (the spelling is preserved). Critical spell hits also consume
`spell_impale_on_crit_%_chance`; this is not an unconditional panel input. Actor and qualified own inputs sum in
wide intermediates; chance clamps once to 0–100. The three selected virtual chance rows share raw source scope,
with each hand's local snapshot supplied separately. Attack-only chance no longer becomes a general raw stat.

`ImpaleState` stores 10% of the actual physical roll, after conversion and source modifiers/critical multiplier but
before target reduction or damage taken, multiplied by increased impale effect. Each instance captures its hit
count (base five) and duration (base eight seconds). On a later accepted hit, expired instances are removed,
their stored values are summed into one reflected physical hit, remaining hit counts decrement, then a successful
new chance/target-avoidance roll may add an instance. Nonphysical and zero-damage hits can trigger existing
instances but cannot create one. There is no independent five-stack cap; modifiers can extend the effective count.
The state uses the paused connection clock and lazy expiry at the next hit/state observation. Corpses retain their
instances until expiry/removal; native impale-count/visual publication and corpse consumers are not implemented.

The reflected total receives the existing target physical reduction and generic damage-taken calculation once.
It receives no new source modifiers, conversion, critical multiplier, spell-only damage taken or returning-projectile
multiplier. It causes no second on-hit/resource callback. Ordinary melee/bow and supported projectile/area paths
all use this lifecycle. Reflection is combined with direct damage only for the existing Life/notification commit.
Ordinary bow inputs, including chance/effect/duration/local sources, remain frozen at release; melee keeps its
existing accepted-weapon snapshot and supported attacks use the contact weapon. Area state mutation occurs
inside the accepted-contact lock after closed/leaving/paused/dead/action/cooldown guards. Existing projectile
collision, accuracy and block guards remain ahead of mutation. Returning hits scale only their own direct damage
and newly stored physical amount. Target avoidance prevents new instances, not consumption of existing ones.

Evidence: extracted `SupportImpale` supplies attack chance 60 at level one. The reference library entry
`pob/skills/SupportImpale`, pinned PoB commit `16de4b82d57f1c0de6eb40f37143c32d4da36a02`, and its
`CalcOffence.lua` use eight seconds, five base hits and capped chance. Library `wiki/12484`, revision 1699530
(2026-08-07, snapshot 2026-10-13), describes actual pre-mitigation physical damage, any later hit, duration and corpse
retention. [Mark_GGG's explanation](https://www.reddit.com/r/pathofexile/comments/bxa11w/comment/eq4of7c/?context=10)
confirms one reflected hit sums the instances and does not trigger on-hit effects. These references establish
mechanics, not exact parity for every conditional source or the current extracted build.

Decision: correct actual-roll storage and separate reflection using the existing target mitigation path instead of
retaining the old maximum-roll/critical-multiplied bonus. Remove the unsupported five-stack cap and death clear.
The alternative of leaving support chance as display-only would continue to omit gem attack/spell gameplay.
Keep established action/snapshot timing while changing the impale lifecycle; do not introduce a global timer or
rewrite other agents' live sessions to obtain native evidence. Existing monster armour modelling, official tick
rounding, conditional weapon/Pride sources, extra applications, no-consumption/strongest-instance/Impaler rules,
critical-only chance display and native source/link/equipment/transition checks remain explicit completion work.

Automated checks reproduce the old min-roll error (10..20 stored 2 instead of 1) and double-critical error
(direct 10 crit200 plus reflected10 dealt40 instead of30), then cover expiry, hit count, captured inputs, physical
conversion, critical/target separation, local source removal and selected real support lifecycle. Parsed default
melee/bow, Cyclone, Caustic Arrow (`PoisonArrow`), Fireball and Ice Nova contacts exercise application and target
avoidance; zero-damage supported contacts trigger independently mitigated reflection. Rejected contacts leave
state unchanged. Current-branch native validation remains pending; historical native checks used the old code.

## 4. What the server uses now

The issue #75 publication review now includes current critical chance/multiplier and signed luck in ordinary
and supported hit average/DPS rows. These are fractional outgoing estimates before target
mitigation; the direct packet ranges remain noncritical. Initial creation and refresh share the same publication
path. Exact action duration is retained until the hundredths formatter, with zero/overflow bounds.
Decision and alternative: keep that existing precise-rate behavior rather than introduce an extra rounding at
the displayed APS/CPS boundary; remove the duplicate rounded-then-overwritten calculation instead.
See [skills module, critical-weighted hit averages](11-skills.md) for the formula, primary evidence, regression
controls and remaining multi-hit/DoT/native work. This does not certify either DPS row as complete.

Attack estimates now include normal-reference landing probability `h` and independent critical confirmation:
the mean factor is `h * (1-c*h + c*h*multiplier/100)`, where `c` is the probability after signed critical luck.
Spells supply `h=1`. Ordinary/selected accuracy includes raw global/qualified local cannot-evade; the ordinary
main-hand normal/evasive chance aliases now agree with that bypass too. Final diagnostic chance replacements
do not replace combat inputs, and target Masteries stay outside unconditional reference estimates.
Decision: apply accuracy separately to landing and confirmation rather than once to the old critical mean,
and keep luck on the critical roll rather than the accuracy draw. [Skills §14](11-skills.md) records pinned
PoB lines, primary tooltip evidence, six reproduced failures and the 55-case focused run. Full integer-roll
expectation, dual-hand/multi-hit/cooldown/DoT and native certification remain separate work.

The shared critical chance now also applies the four evidenced unconditional MORE/LESS sources before final
rounding/capping, including actual Controlled Destruction support and the unique -40% chance modifier.
Actor and selected-skill calculations consume raw sources once, so critical-weighted means inherit this
correction without another multiplier. Decision: compute the full exact product instead of capping BASE/INC
first; a pre-cap 200% with 80% less must become 40%, not 20%. Native and remaining conditional/hand/accuracy
mechanics remain open; source IDs, support lifecycle and parsed contact tests are in [11](11-skills.md).

- The default attack builds the active weapon's typed packet, or the class's Characters.dat unarmed packet, plus
  raw added damage. The shared player resolver traces conversion/gain before inherited increases/more; the caller
  applies attack Rage once, rolls each final type and retains the existing critical/mitigation stages.
- Fireball builds raw `spell_minimum/maximum_base_<type>_damage` (`SkillCatalog.HitDamage`) plus effective added
  damage. `SpellDamage.Packet` uses the same conversion-history resolver, then the hit path rolls the packet and
  sends the life change. Final linked support changes feed both the supported panel and combat packet.
- Monster melee is a physical packet (`MonsterAttack.Damage`); the server rolls it when the swing lands.
  Each type takes resistance, then shared incoming hit mitigation, including enabled chaos armour (section 2e).
  Within one of the player's passive enemy auras the rolled hit and its ailment base first take the aura's conversion
  stats (Born under Fire: half the physical as fire; [11](11-skills.md) section 10e).
- `missing_unreserved_life_%_gained_as_life_before_hit` adds that percent of missing Life, rounded down, once an enemy
  hit is decided not evaded and before it is rolled (`MonsterSwing` and the dev hits), only while alive.
- A blocked hit recovers `life_gained_on_block` and `mana_gained_on_block`, plus `recover_%_of_maximum_life_on_block`,
  `mana_%_gained_on_block` and `energy_shield_%_gained_on_block` percent of each maximum, rounded down (PoB
  LifeOnBlock, ManaOnBlock and EnergyShieldOnBlock; the rounding is unverified), in `MonsterSwing` and `/blockhit`.
  A blocked hit counts (blocking does not stop the defender's when-hit effects); Life reservation is not modelled.
- Player hits on monsters (default attack, gem projectile and area hits) roll each type once and reduce it by the
  monster's base resistance: `MonsterTypes.Resistances` → `MonsterResistances`, the level's
  `DefaultMonsterStats.Difficulty` (1/2/3) picking the Normal/Cruel/Merciless columns (`MonsterResistanceCatalog`,
  `MonsterCatalog.BaseResistance`, the shared pass is `HitMitigation`). Without the extracted tables every type reads
  0 for the base input.
  Spawned-monster resistance modifiers add before the cap; physical reduction and damage taken apply after mitigation.
  Ordinary bows snapshot player/skill penetration at release and resolve target resistance at contact.
  Main has no connection-aware resistance or presence engine; those archival #206 systems are outside this stack.
  Automated mixed fractional-hit, target-modifier, bow RNG and same-key player/skill cases cover the resolved paths.
  Native penetration checks belong to original head `0d800d1`; recheck the target-modifier and bow composition natively.
- The hit's penetration (`Penetration.cs`) subtracts from the capped resistance with no second clamp: the general
  per-element and elemental stats on every hit, the attack-only variants on attacks (§2), the
  `cannot_penetrate_or_ignore_elemental_resistances` flag suppressing the elemental ones. Damage over time and the
  physical part take no penetration.
- Critical elemental hits with `critical_strikes_ignore_elemental_resistances` use effective resistance zero,
  including when the target's resistance or maximum is negative; ignore overrides penetration.
  `attack_critical_strikes_ignore_elemental_resistances` covers attacks only.
  Non-critical elemental hits add `non_critical_strikes_penetrate_elemental_resistances_%` to general penetration.
  Player and skill contributions to the same key sum; the prohibition suppresses all elemental penetration and ignore.
  Physical, chaos and DoT keep their separate rules.
  Damage types roll once, followed by the original single critical draw; deterministic mitigation uses that result.
  Target physical reduction and damage taken follow mitigation, preserving fractional components until the final total.
  Ordinary bows retain offensive resistance rules in their release snapshot and resolve the target at contact.
  Native checks on original head `13c41e5` verified the basic ignore and non-critical rules.
  Ten composed native groups pass at source `1366f48`, with Gateway/Core/Host embedded Git versions matching it.
  Critical panels show 657/505, then 657/755 after a spell-only increase; active Sea Legs gives 1204 and empty-set zero.
  Fixed mana lightning produces actual Life losses 90/120/60 for ranged/melee/ranged-only contexts.
  Against a level-68 fire skeleton, fixed 100 fire + 100 cold deals 160, or 220 after 40/20 penetration.
  A 150% critical multiplier then gives 300 with ignore, 330 without it and 240 with the prohibition.
  These values are read from the native Life component after the authoritative hit.
  The run uses a fresh unascended Witch and exercises default attacks, not the ascendancy chooser or whole classes.
  Supported-gem, modifier/bow and Purity-replacement combinations retain their automated integration coverage.
  Earlier native evidence remains attributed to its original heads; a stale #225 DLL did not test #226.
  Rebuild after the final checkout and verify embedded Git versions, hashes and the running DLL before `-NoBuild`.
  See [native build provenance](../dev/client-control.md#build-provenance-for-native-verification).
- The player's debuffs on a monster add to its damage taken (`SpawnedMonster.TakenFromPlayer`, kept by
  `EnemyDebuffsTick`, [11](11-skills.md) §12): `taunted_enemies_damage_taken_+%` on every type while his taunt lasts,
  a passive enemy aura's `elemental_damage_taken_+%` on fire, cold and lightning. Both are increases that add to the
  monster's `base_damage_taken_+%` (PoB: DamageTaken and ElementalDamageTaken are INC), in `MonsterTakesMitigated`
  (every player hit) and `MonsterBurn` (Righteous Fire); Spell Resistant's less spell damage multiplies after them.
  25% elemental and 20% taunted: fire 145%, physical 120%.
- `DamageModel.Mitigate(DamagePacket, ...)` applies each type's own capped resistance.
  Incoming hit commands and monster swings share `PlayerHitMitigation` after resistance.
  Righteous Fire keeps the single-value resistance-only DoT path.
- `/hit` and `/spellhit` still use a single type; conversion is not applied to those debug commands.

## 5. Continuous resource updates (2026-10-24)

The client Life handler for S→C 0x1b1 stores the current whole pool value; merely sending the
regeneration stat does not make the visible life value advance between these updates. The former
250 ms player tick therefore made /god recovery jump by roughly one quarter of maximum life.

`RunResourceRecovery` now combines life/mana regeneration, flask recovery and the player's nonlethal
Righteous Fire burn in one 16 ms loop. Each step integrates the actual elapsed `ClientState.Clock`
time, including scheduler delays; the clock stops during pause. `RecoveryPool` retains fractional
points, discards overflow at a full pool and negative carry at RF's one-life floor, and never revives
dead life. Only changed whole values generate 0x1b1. The unreserved mana cap is unchanged.
Stat aggregation is refreshed at the old 250 ms cadence, with
an additional refresh when RF changes, instead of rebuilding the panel about 60 times a second.
While the player walks, `life_regeneration_rate_per_minute_%_while_moving` and `mana_regeneration_rate_+%_while_moving`
join the two formulas' inputs (`PanelStats.WhileMoving`); the player tick sends the regeneration finals again when a
walk starts or ends ([Polytheist](../game/polytheist.md)).

`RunPlayerTick` keeps the 250 ms cadence for RF on monsters, suppression conditions, waypoints,
auto-loot and quests; its RF duration also uses elapsed game time. Instant damage and instant
healing remain immediate. No client executable or packet layout changes are needed. Live visual
verification: enable /god, damage life with `/blockhit attack physical 10000`, then observe recovery;
also check pause, flasks and RF. The new visual behavior awaits that real-client check.

Ordinary Life/Mana regeneration uses the flat source plus its percentage of the full maximum pool, then
multiplies regeneration increases by the separate recovery-rate increase bucket.
Mana retains Character.ot's base 105% per minute before those modifiers; reservation changes its recovery cap,
not the maximum used to calculate the rate.
`no_life_regeneration` and `cannot_recover_life` stop Life regeneration.
`no_mana_regeneration` and `cannot_leech_or_regenerate_mana` stop Mana regeneration.
Signed nonzero prohibitions count as enabled; regeneration/recovery reductions stop at a zero multiplier.
The panel totals and native recovery inputs use the resource writer's same calculation and clear removed sources.
Integer publication saturates at the protocol limit rather than wrapping extreme rates.
Signed developer base sources retain their existing linear combination with Fortification's separate contribution;
they do not establish an official negative-regeneration mechanic.

The formula follows the reference library's pinned PoB `16de4b82d57f1c0de6eb40f37143c32d4da36a02`,
`CalcDefence.lua` regeneration section, with native 3.29.3b stat IDs and modifier rows.
This is an accepted 3.29.x reference, not proof of exact official-server arithmetic for every conditional mechanic.
No generic Life/Mana regeneration `+%_final` input exists in the native table; derived virtual outputs are not raw sources.
Zealot's Oath, other regeneration conversions and named conditional/more modifiers remain separate work.

Ten native controls verified flat sources, independent increase buckets, prohibition, source clearing and relog.
The 600/minute Life control became 1080 with 50% regeneration and 20% recovery, matching actual 18 Life/second.
A 750-Mana character with 1200 flat/minute, 40% regeneration and 20% recovery received 3339/minute,
matching actual recovery near 55.65/second after reservation was released.
Both rates stopped under negative-valued prohibition flags and resumed after zeroing them.
The isolated characters' inventories stayed unchanged; this does not verify every flask/RF visual timing path.

## 5b. Character-panel leech limits

Leech coefficients now include general/typed/elemental/attack/spell and qualified hand-local raw sources, including
the missing physical/chaos ES aliases. Selected skills recompute all 24 coefficient and three recovery-speed
aliases with their qualified supports. These are overlapping display scopes; do not sum them to compute a hit's
leech. Combine raw actor/support per-hundred-thousand values before unit conversion. [Skills §14](11-skills.md)
records the pinned source descriptions, real support evidence and the decision to avoid derived-alias feedback.
The ordinary direct-hit recovery path is now implemented below; publication alone does not certify native formatting.

The recovery implementation follows [Mark_GGG's worked example](https://www.reddit.com/r/pathofexile/comments/axgevs/comment/ehtoiwd/?context=3)
distinguishes the fixed per-instance 2%/second rate, amount-based duration, the total recovery modifier and the
separate total-rate cap. Increasing total recovery does not shorten an instance. Generic recovery rate acts after
the capped leech contribution. Pinned PoB `CalcOffence.lua:3721` likewise derives duration from amount/full pool
and its base rate; instant portions and Ghost Reaver remain distinct conversions. Preserve these distinctions
in gameplay rather than using a single regeneration rate or an amount-draining speed multiplier.

`LeechSources`, `LeechRecoveryState`, `LeechSession` and `Program.Leech` now connect raw actor/qualified support/
attacking-hand inputs to accepted ordinary melee/bow, gem projectile and area contacts. Coefficients apply to final
damage types after resistance, physical reduction and damage-taken modifiers. Misses, blocks, rejected owners and
targets, DoT and reflected impales do not grant instances. A monster's `cannot_have_life_leeched_from` gates Life
only. Real Life Leech, Mana Leech and Energy Leech supports have accepted-hit controls, including attack-only Mana.

Each resource retains separate instances with a creation-time base rate of 2% of its full maximum pool per second.
The capped nominal amount sets duration; current total recovery, maximum total rate and generic recovery modifiers
stay separate. The shared limit resolver retains fractions for gameplay even when an integer panel endpoint is zero.
Configuration settles elapsed recovery before adopting new rates. The existing resource writer integrates every
expiry boundary, adds Life/Mana with their existing fractional carry and gives ES without starting/resetting recharge.
General/resource instant percentages split off immediate recovery inside the accepted hit transaction, before another
enemy hit can act. Instant Life/Mana notifications are marked dirty for the existing writer; ES uses its normal sender.
Sub-point instant recovery retains a separate carry and discards overflow at full pools.

Ordinary leech ends at full unreserved Mana/full Life/full ES; supported unconditional Life/ES overleech flags retain
their instances. Instant flasks, block gains and the recovery writer trim full pools at their commits. Death, resurrection,
successful area transfer and disconnect clear state. Pause uses the stopped game clock. Supported cannot-leech flags
remove matching instances; cannot-recover Life/ES suppress recovery without inventing a cannot-leech source.

Decisions and alternatives:

- Keep existing total-hit rounding and critical/return scaling. Retain post-mitigation type proportions and scale them
  to that accepted integer direct total for leech. Rounding each component independently would change existing damage;
  using pre-mitigation damage would overgrant. This is a local projection, not a measured official rounding rule.
- Keep ordinary bow offensive leech sources with its existing release snapshot; settle owner recovery limits at contact.
  Rebuilding bow damage/sources at impact is a separate snapshot-policy review, not an incidental leech change.
- Clear leech on successful area transfer because the current destination refills resources and owns a fresh clock.
  Carrying instances/timestamps across connections is unverified; do not silently transfer them or affect another session.
- Extend the existing resource writer instead of adding another timer. A single writer avoids competing periodic pool
  updates. Immediate leech stays inside the hit lock; notification latency cannot delay its defensive benefit.

Ghost Reaver now converts each Life-source grant into its own ES instance, separate from native ES leech from the
same hit. [Mark_GGG explicitly confirms separate instances](https://www.reddit.com/r/PathOfExileBuilds/comments/mrgqh3/comment/gxksr13/?context=3).
Converted instances use ES maximum pool, instance cap, total recovery and generic recovery; Life-specific rate
modifiers do not substitute for them. Life-instant percentages follow the converted source, as detailed below.
All ES instances share the same total cap. The actual extracted
passive 24426 supplies `keystone_ghost_reaver`; `ReminderTextKeystoneGhostReaver` and pinned PoB's 3.29 tree agree
that this also doubles the total ES cap and disables recharge. The plain `leech_energy_shield_instead_of_life`
source only converts. The virtual conversion flag publishes/clears, preserving explicit developer replacements.
Refund affects new grants and current cap/recharge settings; it does not reclassify old instances.

Decision: preserve independent converted/native instances instead of summing their coefficients or extending one
instance's duration. Primary GGG evidence takes precedence over treating PoB's combined average-duration output
as a runtime instance model. Conservatively retain original source-level Life prohibitions and apply destination
ES prohibitions as well, following pinned PoB `CalcOffence.lua:3403,3631,3678`; their exact native conversion
interaction remains a specific validation requirement. Target ES-leech immunity blocks both converted and native
ES grants while leaving Mana available. Existing Life instances from before conversion retain their original type.

`is_life_leeching`, `is_mana_leeching`, `is_es_leeching`, `is_leeching` and `on_full_energy_shield` now come from
live owner state, overriding forged raw condition values. Reading these conditions expires old instances without
draining pending recovery. Instant-only grants leave no timed instance. The normal player tick republishes player
and selected skill stats when these conditions change; pause keeps their game-clock lifetime fixed.

The hit resolver now adds general/resource leeching damage, attack-only leeching damage, physical damage while
Life leeching and full-ES damage to their proper increased buckets. Physical increases follow conversion history
once. Energy Leech's full-ES MORE modifier is separate from its quality's increased damage while ES leeching;
both can apply with overleech. The retained legacy while-ES-leeching MORE source also has an explicit condition.
Pinned PoB `Data/Skills/sup_int.lua:2214` and the actual qualified support set supply the current 150-permyriad,
15% full-ES MORE and 1%-per-quality increased-damage inputs. Actual linked/unlinked quality and committed projectile
controls distinguish those factors. Ordinary bow damage continues to use its documented release snapshot.

Decision: derive conditions from active instances, not nonzero recovery or raw developer flags. Suppressed recovery
can leave a live instance, while an instant-only grant cannot create a persistent leech condition. Preserve pending
recovery when inspecting expiry; consuming it during a panel rebuild would make damage/UI queries change healing.
Extend only evidenced hit-damage scopes rather than treating every `while_leeching` stat as generic damage.
Committed-hit controls floor each component's random-range endpoints before summing, matching `DamageRange.Roll`.
Lifecycle controls synchronize on the send gate and pause the owner clock after creation rather than racing a
wall-clock delay. A two-flight exclusion control permits legitimate entropy expiry; exact expiry/no-redraw rules
remain in the explicit-time Core tests. These choices preserve gameplay assertions under shared-machine load.

Attack-only ES overleech now preserves each instance's attack origin, including Life-source instances converted by
Ghost Reaver. Full ES removes spell instances while retaining attack instances when the corresponding item stat is
active. Both categories remain in one queue under one total cap; filtering does not restart duration, change the
creation-time base rate or bank recovery accumulated at full ES. Gaining/removing the modifier changes retention
at the next full-pool check, including existing instances. Unconditional ES overleech still retains both categories.
The Life-overleech virtual now projects its ordinary raw source, and an explicit zero replacement disables retention.
Other sources of that virtual, including the incomplete Immortal Ambition conversion, are not certified by this mapping.

Evidence: actual Stats row 12087 and Mods `ESLeechFromAttacksNotRemovedOnFullESUnique__1`, pinned PoB stat description
6560 and unique claw data. Decision: retain an immutable per-instance hit-family tag rather than expanding the
attack-only modifier to all leech or giving attacks a second independent cap. PoB's generic overleech display flag
alone does not retain this source qualification. Real-mod contacts cover ordinary melee/bow, gem area/projectile
attacks and excluded spells, plus source removal; model controls cover mixed caps, unchanged expiry and conversion.
Validation here is direct code review plus automated controls. The review skill's dedicated Bugbot tool was
unavailable; continuing those checks was chosen over waiting for the owner. No independent tool review is claimed.

Instant leech now includes Life-specific percentages, the actual Claw mastery (effect 4516 / `Claw4`, Stats 18613),
and Weapon Master's all-resource percentage while wielding a claw (passive 52575, Stats 20745). Count usable claws
in the active hands only: the per-claw Life source scales with one/two claws, while Weapon Master applies once.
Gore Dancer's Life percentage (passive 29630, Stats 19735) and the common percentage add before a 0..100 clamp.
The split is fixed when an instance is created; later source changes do not retroactively split existing recovery.
The actual Stats catalog has no raw `mana_leech_%_is_instant` or `energy_shield_leech_%_is_instant`; resource virtual
replacements retain their existing final-override semantics, including zero.

[GGG's 3.22.1d patch notes](https://www.pathofexile.com/forum/view-thread/3439509/filter-account-type/staff)
explicitly fix Life-instant sources with Ghost Reaver. This supersedes the earlier assumption that conversion drops
Life-specific instant bonuses. The pinned PoB calculation is not sufficient evidence to contradict that correction.
Decision: retain the Life instant fraction on the converted Life-source instance; native ES leech keeps its own
fraction. Count the common percentage once per instance, and let an explicit ES virtual replacement govern both
ES instance categories. The alternative would propagate a Life-only percentage to all native ES leech or add two
already-composed percentages, broadening scope or double-counting the common source. This mixed-source rule is a
conservative local interpretation; the patch notes establish conversion compatibility, not its exact composition
with native ES leech or developer overrides. Native confirmation remains required. Actual source/contact tests
cover mastery removal, active-set switches, requirement-disabled claws and both conversion states; model controls
cover clamps, split duration, resource scopes and explicit replacements.
The full-pool source/contact control asserts retained instances at the hit; it does not assume they remain active
through a later wall-clock query. Such a short instance can legitimately expire. Dedicated condition tests and
explicit-time session/recovery tests cover that contract instead of lengthening production duration for the fixture.
The full validation passes with four xUnit workers. An unchanged death/resurrection case timed out under default
parallelism; its failed receipt is retained in the report. Bounding this run's worker count preserves all assertions
and shared-machine resources; it does not establish that the intermittent default-parallel timeout is fixed.

Still open: other conversions, remaining conditional/weapon-family sources and instant percentages,
skill-specific recovery-speed ownership, legacy instant flags, speed/defence/ailment/DoT leech-state consumers,
remaining cap conditions and current-branch native recovery/prediction/lifecycle parity. These are explicit follow-up
requirements, not completed by the ordinary engine. No current-branch client/server was launched for these controls.

`LeechLimits` calculates limits independently of active leech recovery. Character.ot's defaults are 10% of the
full maximum pool per instance, 20% per second total for Life/Mana and 10% per second total for Energy Shield.
Reserved Life/Mana remains part of that maximum. Instance-amount increases and maximum-rate increases have separate
buckets; leech recovery speed does not increase either cap. Generic, resource-specific and map maximum-rate
increases/reductions add before their multiplier. The supported ES doubled-instance flag applies afterward.
The explicit `maximum_life_leech_rate_%_per_minute_is_doubled` flag doubles only the Life total cap after rate
increases; final virtual replacements still win, including zero. This is separate from the recovery multiplier.
The calculation publishes six absolute limits, their six native permyriad companions and the cannot-leech flags.
Base/global/resource prohibitions gate matching limits; the supported Low Life prohibition gates Life/Mana only.
Explicit developer replacements, including zero, retain the existing override policy.

The reference library's pinned PoB `16de4b82d57f1c0de6eb40f37143c32d4da36a02` supplies the formula and Low Life
scope, cross-checked with this client's Character.ot, Stats and modifier tables.
Absolute integer endpoints truncate nonnegative fractions as a local wire projection. Official-server endpoint
rounding is unmeasured; native display agreement does not establish that rounding rule.
The actual Highwayman modifier `TotalRecoveryLifeLeechDoubledUnique__1` supplies Stats 12776,
`total_recovery_per_minute_from_life_leech_is_doubled`. It now doubles the combined Life recovery rate after
increases, before the total cap and generic recovery multiplier, without changing nominal instance duration,
instant amount, Mana or ES recovery. Two copies, or this source together with `life_leech_speed_is_doubled`, still
give one doubling. Source removal settles elapsed recovery under the old multiplier and preserves original expiry.

Evidence: actual Mods/Stats rows, pinned PoB `Data/StatDescriptions/stat_descriptions.lua` entries 7516, 9313 and
10575, Highwayman data in `Data/Uniques/ring.lua`, and `Data/ModCache.lua:22120` / `Modules/ModParser.lua:6855`
which cap the shared doubling multiplier at one application. Decision: combine equivalent boolean sources rather
than multiplying once per equipped copy or once per raw spelling; those alternatives would quadruple recovery.
Keep total-recovery and maximum-rate flags distinct. Real modifier contacts cover one/two rings and source removal;
explicit-time tests cover caps, expiry, instant portions, conversion and overrides. These are controlled mod-bearing
Gold Ring fixtures, not a complete unique-item generation test. Supporting the raw maximum-rate flag does not
assign it to current Vaal Pact or certify a current acquisition path; that alternative would assume legacy keystone
behavior. Native recovery/prediction and stacking checks remain required.

Reservation/recent-hit/Defiance/party-link conditions and the derived Wicked Ward virtual remain unimplemented.
Direct resolved flags do not establish an item/passive acquisition path.

Native checks verified baseline values and display conversion, independent instance/rate changes, reductions,
prohibitions, Low Life/healing, equipment-derived pool conversion, source removal, reservation and relog.
At 1345 Life / 750 Mana / 175 ES, limits display 134 / 75 / 17 per instance and 269 / 150 / 17.5 per second.
An actual conversion ring changes the pools to 1465 Life / 0 ES and the limits follow them; independent flat ES
remains ES and contributes to its own limits. A developer-only conversion override is not a pool source.
Read-only hooks observed the native formatter and its percent/per-minute converters without calling its getters.
Evidence is in `artifacts/issue75-completion/leech-native-verified/`, including preserved failed fixture controls.

Character-stat aggregation copies developer values and their key set from one locked snapshot. This prevents a
concurrent command from adding a key between copies and faulting the periodic refresh task. Four observed-task
regressions cover tick-only and tick-plus-recovery loops with both native and synthetic stat layouts.

### Composition with the upstream projectile runtime

The issue #75 merge uses the single chronological projectile engine for ordinary bows and gem projectiles. Admission
captures one shared critical roll; release retains selected accuracy, conversion-history damage, weapon-local
penetration, impale settings and raw leech sources. Accepted contacts perform evasion/critical confirmation, direct
mitigation, leech, then separate impale reflection. Kills keep corpse/impale lifetime and level-up refills trim leech.
The recovery writer advances the projectile horizon before consuming earned recovery. Leech conditions and limits
use that committed horizon, preventing late pump work from being aged prematurely to the wall clock.
Decision: port these consumers into the new engine; keeping the removed flight task would duplicate execution paths
and lose upstream continuation/secondary-area rules. The [framework guide](../game/projectile-framework.md#issue-75-integration)
and [merge report](../dev/issue75-review.md#upstream-merge-resolution) record the contracts and validation.

The later main integration also retains incoming ignite/shock and Hardcore death handling in the same resource
writer, alongside leech, Energy Shield, Fortification and temporary recovery. Ruthless periodic pool persistence
samples after projectile catch-up at that boundary. Instant-leech notifications remain visible, while a lethal
ailment uses its epoch-checked death publication instead of sending a second ordinary Life update. Keeping main's
separate wall-time sample would bypass the branch's chronological recovery boundary. Gambler rolls and typed
mitigation are composed as described in the [projectile module](../game/projectile-framework.md#issue-75-integration).

## 6. Not modelled yet

- Exposure, full monster armour and specialized damage-taken modifiers; ordinary player Energy Shield and the
  supported incoming shock path are implemented. The monsters' base resistances are in §4, with penetration.
- Remaining skill-specific increased/more/effectiveness mechanics beyond the documented packet and support paths.
  Weapon-local and character flat ranges retain conversion history and their separate scopes.
- Outgoing ailments beyond projectile and granted-skill ignite (§3) and chill, freeze and shock: melee/area attack
  ignite, bleed, poison; alternative ailments (scorch, brittle, sap); the shatter visual and the on-death effects Shatter
  suppresses; bleed movement bonus; poison stacking.
  Supported incoming elemental ailments are bounded by [Gambler](../game/gambler.md#incoming-elemental-ailments).
- Per-monster damage conversion and monster-specific mitigation.
- Damage reflection, specialized leech conversions/conditions, life/mana gain on hit, and recoup. Ordinary leech is in §5b.
- Party/minion damage and the full support-gem effect set.

The next support-gem work (#64) can add its effects as packet operations: `Add` for added damage, `Scale` for
increased/more, and `AddGain`/`Convert` for conversion, without touching the life-update code.

## Player energy shield (2026-10-25)

`EnergyShield.cs` owns the ordinary ES pool, fractional recovery, damage interruption, and recharge phase. Equipment
reads ArmourTypes ES bounds with the item's saved shared base roll, local flat/increased ES,
quality, intelligence, passive ES and global defence increases. Offhand shields count. Non-chaos damage after mitigation
spends ES before life; chaos bypasses it. A positive damaging hit interrupts recharge; a zero/blocked hit does not.
Death empties ES; existing heal/resurrection/entry refills include it. Righteous Fire uses its life and ES burn inputs
and absorbs its non-chaos self-damage through ES before life. Special keystones and conditional mastery effects are
not included (CI, Eldritch Battery, Zealot's Oath, Wicked Ward, leech and related substitutions).
The native rolled base rounds before local modifiers; the final item ES rounds after local increases and quality.
This avoids the former intermediate truncation: Simple Robe roll 16, 50% local ES and 20 quality yields 31 ES.
Character-wide ES retains the local shield as a separate contribution so shield-only increases add to its global
increase bucket without multiplying increases or scaling independent flat ES.
The body slot retains the same source identity. `maximum_energy_shield_from_body_armour_+%` adds to its global
ES increase bucket only (native `0x1425304f9..0x142530506`); it does not scale other gear, auras or converted Life.
`damage_reduction_rating_from_body_armour_doubled` doubles the body Armour contribution and, under Iron Reflexes,
the body's converted Evasion (`0x142731a7b..0x142731aff`). `gain_no_armour_from_body_armour` suppresses both of
those Armour sources and wins over doubling, while ordinary unconverted body Evasion and ES remain available.
`shield_armour_+%` increases all shield defences; `shield_maximum_energy_shield_+%` adds only to shield ES.
The completed fractional total rounds halfway up, matching native `0x14252c6e7` and helper `0x142513810`.
Armour and evasion use the same final rounding rule, including one final armour round after Iron Reflexes.
Their aura/flask more factors retain the exact decimal product until that final round.
Determination's 73% more with Basalt's 20% more therefore uses 2.076 rather than a rounded 2.08 factor.
The two shield-defence exclusion flags remove shield bases from the pools; other equipment and flat sources remain.
The shared `ItemDefences.Calculate` applies the Tabula hidden override and explicit no-ES flag before this calculation.
Both flags zero only that item's ES, leaving its armour/evasion and other items' ES intact.
Full or percentage ES-to-Life conversion then consumes each active armour slot's final local ES as a Life source.
The percentage calculation truncates separately per item; the full-conversion flag takes precedence when both exist.
All converted item ES is removed from the character's ES inputs, including with a fractional conversion percentage.
Global ES increases and Intelligence do not scale the converted Life; global Life increases apply afterward.
Independent flat global ES continues to supply ES, and shield exclusions also exclude that shield's converted Life.

`energy_shield_+%_per_10_strength` adds its signed coefficient times `floor(final Strength / 10)` to global ES INC.
`evasion_+%_per_10_intelligence` likewise adds its coefficient times `floor(final Intelligence / 10)` to Evasion INC.
Completed production attributes supply the groups, after flat and typed/all-attribute increases.
These are ordinary additive increases before slot-specific increases, MORE factors and the final rating round.
Wide intermediates retain signed cancellation before rating bounds; published percentage fields remain signed ints.
Iron Reflexes keeps the Intelligence-derived item term, while excluding the inherent Dexterity Evasion increase.
Its converted bucket still counts shared global defences once and retains shield/body scope and exclusions.
The Strength-derived increase scales remaining independent ES, including Discipline, without changing local ES-to-Life.
Native Mods rows 13590/13594 (HASH16 34083/5007) supply complete vectors 1/2 in both modes.
Rows 39699/39701 supply independent additive sources with their actual effective level 52 requirement.
`/set_stat` replaces each complete coefficient, including zero, rebuilds pools and publishes the final stats.
The ES input also publishes current ES; lowering capacity clamps it, while raising capacity does not refill it.
Final attribute presentation overrides do not become a second production attribute source.
Thirty-three pure and fourteen both-mode Gateway cases cover groups, slot/IR composition, qualification, active sets,
source removal, actual command packets and unchanged serialized item payloads.
Fresh supplied-source native reload is verified by the separate controls below.
Those 47 cases, seven ES publication regressions and eleven inherent-baseline cases pass in a 216-case focused batch.
Full Gateway 3939 passes without skips; unchanged prior Core 648/Data 37 remain applicable after this Gateway-only fix.
The latest diagnostic hook retains six actual disposed-NetworkStream task faults, all `MonsterKilled` through `Send`.
Its 386 first-chance records are a separate count; previous runs and their faults remain preserved.
No new coefficient/publisher path, diagnostic errors, truncation or cap hit is captured.
Passing tests do not establish fault-free execution.
The native vectors and pinned PoB `16de4b8` grouping rule support these operators as a labelled 3.29.x projection.
Complete Shaper's Touch, acquisition and full ES/Evasion rows remain unverified.
The ordinary inherent Intelligence ES term is now `floor(final Intelligence / 10)`, correcting the former `/5` term.
Native `ClientStrings` row169, `IntelligenceDescription` (HASH32 1789395335), declares 1% ES per ten Intelligence.
This static help text is independent of our server-fed panel and agrees with pinned PoB `CalcPerform.lua:541`.
The current extraction report associates the table with the copied 3.29.3b client; no fresh archive comparison was run.
Complete groups follow the accepted pinned model; exact official fractional/group arithmetic remains unverified.
At Intelligence 132, the native-source recipe's zero/one/two Strength coefficients now give ES 113/124/135.
The corresponding EV 18/22/26, local item ES and converted Life remain unchanged.

`gain_no_inherent_bonus_from_intelligence` suppresses the inherent Intelligence ES increase; it leaves final
Intelligence, item Evasion per ten Intelligence and item ES per ten Strength intact. The broad Dexterity flag removes
only inherent Dexterity Evasion and accuracy; IR already excludes that inherent Evasion term while retaining the item
and shared/slot terms. The broad Strength flag removes inherent Life/melee grants without removing item ES or Mana per Strength.
The narrow Life/Mana flags and the broad flags compose as described in [11](11-skills.md) section 4. Eighty new
automated cases cover exact native vectors, overlaps, whole-source zero overrides, command resource changes and retained
item/slot/IR terms; all 4704 fresh full-suite cases pass. Native checks of these new flag inputs remain pending.

Fourteen native controls at actual level 100 (A0–A6 in Standard and Ruthless) bind source `810d98b` to the
current owned Player and its ordinary resources. The fixed state sequence is ES 103/104/124/135/124/113/135,
EV 15/16/22/26/22/18/26, and current ES 103/102/103/123/124/113/135 in both modes.
The fixture supplies a native 100 flat-ES vector, native 1/2 attribute coefficients and an in-range +100 qualifier;
the historical out-of-range +200 qualifier stays parked throughout the numeric controls.
After a literal one-ES hit, the six source increases preserve current 102/103/123 within 487–749 ms, below the fixed
2000 ms recharge-delay boundary. Removing sources from full 135/124 produces fresh pool 2 clamps to 124/113.
Re-equipping both sources and completing logout/login/Play yields current/max 135 in each mode.
Each accepted target also has a retained completed local World 0x1b3 call with an in-frame Stats setter, matching
PID/receiver/generation/clear token, a full explicit target vector and an epoch after the effective source action.
Later natural Life-tick bindings cannot erase or substitute for that World proof; saturated/error buffers fail.

The original defence2 Standard A2 remains **UNPROVED**: its single provenance slot was overwritten, and the retained
World samples only prove the preceding state. A subsequent prospective observer retains the two evidence channels
separately and keeps one observer loaded across both modes; no original verdict was replaced or reassessed.
All fourteen defence3 verdicts were independently reproduced in memory from their frozen inputs and journal prefixes.
The parent completed with 918 input hashes held; the stopped-child audit checked its distinct 917-file set unchanged.
Both canonical inventories, exact item/gem IDs, full charges, empty host, saved bar intent, account, probes, stash and
metadata payloads were preserved apart from both LastPlayed values and Ruthless current Mana 750→655.
Its native trace shows a 700→650 clamp, higher capacity without refill, then recovery through 654; the later save is 655,
without a captured event for that final tick. Only the disposable child was used; normal `data/saves.db` was restored.
The three live service stderr files are empty, but the Gateway stdout log retains one remote-closure IOException;
this is not a fault-free-run claim. Earlier automated diagnostic faults above remain separate and preserved.
Evidence: `artifacts/issue75-completion/attribute-defence3-native/final-fourteen-controls-summary.json` and the adjacent
assessment, independent review, preservation and cleanup receipts. These are bounded supplied-source controls:
no legal acquisition, Defence formatter/row binding, rendered zero, official fractional arithmetic, complete issue row,
native queued-send scheduling, or newly corrected recharge/full-ES-condition certification is implied.
These controls precede the five prohibition operators and do not establish their native-client behaviour.

Discipline supplies another global flat ES source after equipment conversion, including with no ES equipment worn.
Its 60/217 flat ES at gem levels 1/20 receives Intelligence and global ES increases, without shield-only modifiers.
Aura effect scales and truncates its integer ES/recharge stats first, following pinned PoB's ordinary aura rule.
Gem quality changes area of effect, not those two stats.
The existing 30% recharge increase is counted once through the raw aura stats and the shared recharge calculator.
Toggling the aura updates capacity without healing current ES; removing it clamps current ES to the new maximum.
See [11](11-skills.md) §13 for source ordering, reference scope and lifecycle checks.
The item's tooltip ES is retained even though its character contribution changes to Life.
The hit-recovery ES guard uses the final pool and published ES values rather than raw equipment bases.
`local_maximum_energy_shield_+%_if_item_corrupted` is a global ES increase gated by its source item's corruption.
It affects character ES, including independent global ES, without scaling tooltip ES or converted Life.
Native application and reload checks passed for the supported conversions, no-ES overrides and corrupted-item
scaling; see [13](13-items.md) §2 for the verified scope and remaining mechanics.

Character.ot supplies 2000% of maximum per minute: base recharge is one third per second, after a two-second delay.
Faster start divides the delay by `1 + energy_shield_delay_-%/100`; recharge/regen recovery increases scale their rates.
The Defence tab's Energy Shield Regeneration per Second row receives
`total_energy_shield_recovery_per_minute_from_regeneration` from that same regeneration calculation.
The per-minute value is rounded once for transmission and divided by 60 by the native panel.
Recharge never contributes to this row; zero rates are published to clear removed or disabled regeneration.
Elapsed game time integrates only the part of a tick after the delay boundary, retaining fractions. Base constants
also match [Path of Building's data](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/dev/src/Modules/Data.lua)
and [defence calculation](https://github.com/PathOfBuildingCommunity/PathOfBuilding/blob/dev/src/Modules/CalcDefence.lua).

Pool 2 of 0x1b1 synchronizes current ES. Updating the virtual `energy_shield_recharge_active` stat through 0x1b3 did
**not** start the client's recharge predictor in the live probe: it predicted only baseline regeneration and jumped
on the next pool correction. During the server recharge phase, publish regeneration plus recharge through the verified
native `energy_shield_recovery_per_minute` input (key 0x590), retaining the separate recharge-rate panel stat. The virtual
recharge-active stat stays zero to avoid double prediction. This is a compatibility bridge; the native server event
that starts the client's distinct recharge phase has not been identified.

`SendPlayerEnergyShield` samples current ES, recharge state and prediction after acquiring the connection's send gate.
The combat lock covers that snapshot, duplicate suppression, sent caches and construction of the resource/prediction
packets; both packets share one send, so a queued capacity clamp cannot publish an earlier current value or rate.
An unchanged state suppresses packets only at the gate, allowing changes made while waiting to publish normally.
Held-gate encrypted-wire tests cover a clamped pool, prediction clearing, death and duplicate suppression.
These publication regressions are automated; the earlier live checks below do not validate this new queued-send case.

The earlier live ES values below predate the inherent Intelligence correction and remain historical lifecycle evidence.
The fresh controls above verify corrected supplied-source totals, no-refill, clamp and reload; dependent recharge
prediction and full-ES-condition behaviour after the correction remain outside that batch.
Live OfflineWitch had 185 ES: `/hit physical 100` left life at 3250 and ES at 85, then recovered to 185. Chaos reduced
life while leaving ES intact. With the prediction bridge, client values advance between pool packets during recharge.
Measurements used the local client Life component, not orb pixels or an official server trace. ES uses upstream RunResourceRecovery with its 16 ms target and elapsed time/fractional carries; foreground verification saw at most 2-point positive ES updates
instead of the previous 22-point steps. OfflineWitch's selected Energy Shield mastery grants 2% ES regeneration per
second (3.7 at 185 ES); that regeneration correctly continues while recharge is interrupted. A temporary zero-regen
control recovered nothing during the recharge delay. See [recovery timing](../re/recovery-timing.md) for evidence and
measurement limits. Recovery-cadence PR #159 is merged; this change extends its existing recovery loop.

## Incoming ordinary elemental ailments

The Gateway's supported monster melee contact now calls `ApplyPlayerAilmentsLocked` after hit mitigation/block.
`PlayerElementalAilments` owns ordinary ignite/chill/freeze/shock on the pauseable connection clock.
Risk Aversion's final avoidance stats are consumed independently per eligible ailment, with no lucky avoidance.
The resource writer integrates strongest ignite with live resistance, ES and lethal Life damage; RF remains nonlethal.
Chill changes action timing/movement, freeze cancels/gates actions, and shock increases subsequent damage taken.
`action_speed_cannot_be_reduced_below_base` floors the action factor at 1; a passive change reconciles it at once.
`unaffected_by_shock` keeps the shock (duration, `is_shocked`) but leaves damage taken at 1, and
`unaffected_by_ignite` keeps the ignite (duration, `is_ignited`) but its ticks deal nothing
([Polytheist](../game/polytheist.md)).
A hit from a monster the player has Blinded adds `damage_taken_+%_from_blinded_enemies` to the shock's increase
(additive, `DamageTakenFactor(now, increasedPct)`).
An ignite keeps the monster that applied it, and the burning ignite's ticks add the same value while that monster is
Blinded (`PlayerElementalAilments.Advance`).
See [Gambler's exact source, threshold, formula and presentation limits](../game/gambler.md).

## Outgoing chill, freeze and shock on monsters

Each `SpawnedMonster` has a `MonsterElementalAilments`, changed under the Monsters lock on the connection's pauseable
clock (`ProjectileWorldTime`). The default attack, area skills and projectiles (ordinary bow shots included) take the
hit's damage with `MonsterHitDamage` (the target's shock and chill at the hit's time) and then call
`AilmentsOnHitLocked` (`Program.MonsterAilments.cs`) for every hit that landed, a killing one included, so a hit does
not gain from its own shock. The older `MonsterDamage`, `MonsterTakes*` and `MonsterBurn` without a time leave the
target's shock and chill out; a new player hit path needs both calls ([Surfcaster](../game/surfcaster.md), Hit paths).
The damage per type is the resisted packet × the monster's damage taken × the critical multiplier on a critical
strike; the threshold is its maximum life (wiki: equal to it for most monsters). The attacker's stats are
`CharacterPlayerStats` plus the skill's own, summed through the panel rows (`PanelStats.Of`).

| Ailment | Applied by | Effect | Duration |
|---|---|---|---|
| Chill | every hit with cold damage | floor(50 × (cold / threshold)^0.4 × modifiers), at most 30%; none under 5% | 2 s |
| Shock | a critical strike or `chance_to_shock_%`, with lightning damage, or cold damage with `cold_damage_can_shock` | the same formula on that damage, at most 50% | 2 s |
| Freeze | a critical strike or `chance_to_freeze_%`, with cold damage | the monster cannot act | 0.06 s per 1% of the threshold, at most 3 s; none under 0.3 s |

The effect modifiers are the attacker's `virtual_chill_effect_+%`/`virtual_shock_effect_+%` and the monster's
`chill_effectiveness_on_self_+%`/`shocked_effect_on_self_+%`, both counted before the cap and the 5% floor. Durations
take the attacker's `virtual_*_duration_+%` and the monster's `base_self_*_duration_-%` (positive reduces, as on the
player's jewels) added together, × (1 + (attacker − target) / 100), as PoB `CalcOffence` sums `Enemy…Duration` INC
and the enemy's `Self…Duration` INC: 50% increased against 50% reduced on itself leaves the base duration. Of the
Ice carries −80 of the freeze one in 3.29.3b Mods.dat; the wiki's text for it (Modifier:MonsterModResistantToCold) is
"80% increased Freeze Duration on you", which is that sign. `immune_to_*`, `cannot_be_*` and `base_cannot_be_*` on the
monster's stats block each kind. The strongest of a kind applies, and each keeps its own duration (wiki); a weaker one
that also ends sooner is dropped. A monster that dies keeps them, the killing hit's included (wiki Ailment: a killing
hit that inflicts an elemental ailment counts as killing an affected enemy); it no longer acts.

- **Shock** adds to the mods' `base_damage_taken_+%` (PoB DamageTaken INC), before a spell's less damage taken, on
  the player's hits and Righteous Fire's burn (`MonsterDamageTaken`).
- **Chill** divides the monster's attack time (`BusyUntil`, the swing's contact delay) by 1 − chill and re-times the
  server's walk by the same factor.
- **Freeze** stops its walk, chase and swings; a freeze that lands during a swing cancels it at contact.
- Unstoppable (`action_speed_cannot_be_reduced_below_base`, `movement_speed_cannot_be_reduced_below_base`) keeps both
  at base, as the player's action floor does (forum bug report 3494363: freeze only interrupts channelling there).
- **Stormy Seas.** `cold_damage_can_shock` lets the cold damage count for the shock's magnitude; the chance is still
  a critical strike or `chance_to_shock_%`. The client's calculator `0x1426f2660` makes `virtual_cold_damage_can_shock`
  (0x1bc6) from keys 0x451a, 0x1bc9 and 0x1b96, and nothing else in the client reads it.
- **Mirrored** (`reflect_non_damaging_ailments_%_chance`, 100): the ailments a hit added to a living monster act on
  the player as well (wiki Reflect: "in addition to its target"), through `PlayerElementalAilments.Inflict` with the
  player's own effect modifiers, caps and floor (`ReflectAilmentsLocked`). The exact reflected magnitude is not
  verified. An area skill's hits land together, so its sweep collects the reflections and applies them after its last
  target; a reflected freeze therefore cannot end the sweep halfway. A sweep stopped partway for any other reason still
  runs the deaths it already caused (experience, loot, quest flags).
- **Glacial Wave.** A chill carries the attacker's `enemies_chilled_by_hits_take_damage_increased_by_chill_effect` and
  `..._deal_damage_lessened_by_chill_effect` as they were when it landed. While such a chill lasts, the monster takes
  increased damage by its strongest chill (any source, wiki Glacial Wave), added to shock and the mods' damage taken
  (PoB DamageTaken INC), and its swings deal less by half of it: `MonsterSwing` scales the rolled hit by
  1 − chill / 200 after the monster mods and before the player's mitigation (PoB Damage MORE −chill / 2). Less damage
  covers all of its damage, so the base its critical strike's ignite on the player starts from is scaled the same.
- **Freeze minimum.** `freeze_minimum_duration_X_ms` (Brine Bound, 1000) lifts any freeze the attacker inflicts that
  passed the 0.3 s threshold to at least that long; no duration modifier changes the minimum (wiki Brine Bound).
- **Freeze when hit.** `chance_to_freeze_enemies_for_1_second_when_hit_%` (Brine Bound) rolls once for each monster
  hit that lands in `MonsterSwing`, blocked hits included (wiki Blocking: blocking keeps the defender's when-hit
  effects), never for an evaded one. The freeze is 1 s through the usual duration modifiers, threshold, minimum and
  immunity; whether duration modifiers apply to this fixed 1 s is not verified. It draws from `Random.Shared`, so the
  incoming hit's own draws keep their order.
- **Shock on kill.** `enemy_shock_on_kill` (Within the Tempest) shocks a monster the player kills
  (`ShockOnKillLocked`: from `AilmentsOnHitLocked` after a killing hit's own ailments, and from Righteous Fire's burn):
  15% base (wiki Shock: shocks without damage) and 2 s, through the player's shock effect and duration modifiers and
  the monster's own, at most 50%. Skill-local shock modifiers of the killing skill are not counted; whether the game
  counts them is not verified. The killing hit's own shock stays on the corpse too; the stronger one applies to it, and
  each spreads as far as its own radius reaches (wiki Within the Tempest: the spread includes shocks inflicted on
  enemies the character kills). A shattered monster
  gets neither.
- **Shatter** (wiki Shatter, Freeze): a non-unique monster that a hit kills while frozen, the killing hit's own freeze
  included, leaves no corpse. The server ends its ailments, so it spreads nothing and gets no kill shock. A kill by
  damage over time (Righteous Fire) never shatters. The client's shatter visual is not sent, because the packet that
  starts it is not known, so the client still shows the corpse; on-death effects that shatter would stop are not
  modelled either.
- **Shock proliferation.** A shock takes the attacker's `virtual_shock_proliferation_radius` when it lands, as the
  client's calculator `0x142828340` builds it: the largest of `gloves_shock_proliferation_radius`,
  `virtual_elemental_status_effect_aura_radius` (the larger of `elemental_status_effect_aura_radius` and
  `elementalist_...`, `0x142828e10`), `unique_vinktars_flask_shock_proliferation_radius`,
  `ascendancy_non_damaging_elemental_ailment_proliferation_radius` and `passive_mastery_shock_proliferation_radius`,
  and 15 with `shock_proliferation_radius_is_at_least_15`. Nothing else in the client reads the radius. The calculator
  scales none of them by area of effect; the server grows the radius by the area modifier like a skill's area
  (`SpreadRadius`: × the square root of `SkillArea.Modifier`, truncated to two places), because the wiki's
  Elemental proliferation page says modifiers to area of effect affect proliferation. A hit's shock uses the hitting
  skill's area stats over the player's, a kill's shock the player's own. Whether the game scales Within the Tempest's
  fixed 1.5 m this way is not verified beyond that general rule.
  Each AI tick (`ProliferateShocksLocked`), every living hostile monster has, at the same effect until it ends, the
  strongest shock whose own radius (cells) reaches it from the monster it is on, a corpse included: each spreading
  shock is an aura of its own (wiki Elemental proliferation), so a weaker shock with a wider radius still reaches the
  monsters beyond a stronger one's. The copy does not spread again, does not count as inflicting, and goes as soon as
  the monster is out of range or the source shock ends. The receiving monster's immunity counts; its own
  `shocked_effect_on_self_+%` does not change the copy (not verified). Chill, freeze and ignite proliferation are not
  modelled, nor a reflected shock spreading from the player. A shattered monster spreads nothing.

Presentation runs on the AI tick (`MonsterAilmentChangesLocked`), so with `/ai off` the server still applies the
effects but sends nothing, and shocks do not spread. A kind is re-sent only when its strongest magnitude changes: S→C
0x1c2 for the old buff, then 0x1c1 with BuffDefinitions 1 chilled, 2 frozen or 3 shocked (BuffVisuals 3, 4, 5), the
player as source. Chill and shock have no duration; freeze carries its remaining time (flag 0x02, f32 +0x68), because
without a duration or a value the client draws no icon under the monster's life bar (live 2026-10-30). Each kind always uses the same instance on its monster, 0xfffd, 0xfffe or 0xffff
(`MonsterElementalAilments.Instance`). The client keeps buffs per object by instance (`0x141e11720` looks up a live
one, `0x141e11fb0` removes by instance, both skip one already being removed), and the connection's `NextBuff` wraps
back to 1 below that range, so ailment changes take none of its instances and cannot meet another buff's. 0x1c2
removes a buff at expiry, so a pause cannot desync it. All three rows have the +0xff bool, so 0x1c1 carries a u16 at
+0x82 ([11](11-skills.md) §12); chilled and shocked are `IsRecovery` and carry their one GrantedStats value
(`action_speed_-%_from_chill`, `base_damage_taken_+%_from_shock`). The magnitude goes in both; what the client does
with +0x82 is not known. The player's own chill, freeze and shock use the same rows and instances on the player
(`PlayerAilmentBuffs`, sent by the player tick when its ailments change), so they show in the buff bar. The chill is also sent as 0x1b3 `virtual_action_speed_+%` (−chill), which the native
walk `0x141e7c0b0` and the action clips read; network stats run no calculators ([04](04-player-entity.md) §4.1a). A
freeze is not sent there (−100 would divide the native clip by zero); the server stops the monster instead.

Not modelled: the shatter visual, unique and boss thresholds and their reduced effects, immunities from the monster's
variety (.ot), Glacial Wave's chill reflected onto the player by Mirrored (wiki: it then acts on the player too), and the
client's frozen animation (not checked live).
