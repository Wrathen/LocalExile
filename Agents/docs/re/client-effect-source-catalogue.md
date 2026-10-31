# Selected effect source configurations (3.29.3b)

## 1. Result and evidence boundary

Reading the object sources before chasing native callbacks distinguishes three different things:
combat-stat defaults, effect-object configuration, and scripts that request presentation changes.
They are useful evidence in their own right, but a resource name or an empty component block does
not supply the missing numerical damage publisher.

This is a **bounded semantic catalogue of 22 object-type resources**, including the complete
parent chains of the selected RF, daemon, invisible-fire and ground-effect objects.
It is not a semantic audit of all 12,997 indexed `.ot` files, all skills or their animated objects.
The earlier keyword inventories in [the resource boundary report](client-hit-resource-boundary.md#14-concrete-bundled-source-chains-player-and-sand-spitter)
remain separate evidence, not a substitute for this catalogue.

The seven ground-effect sources explicitly distinguish client-only, server-only, server-fill and
visible server configurations. In particular, `StandardGroundEffect.ot` and
`ServerGroundEffect.ot` are different inheritance families; the former does not even declare a
`GroundEffect` component in its selected chain. Do not assign identical damage ownership merely
because both filenames contain `GroundEffect`.

No client execution, attachment, new capture, archive modification or server implementation was
performed. The archive was read through the existing read-only GGPK extraction tool.
The raw-resource hashes below identify these exact bodies; they do not certify the entire locally
maintained archive as pristine or prove which resources a recorded runtime loaded.

## 2. Catalogue method

Each entry retains the exact resource path, raw bytes and SHA256, declared parents, common/client/server
block provenance, repeated fields, source line spans and the parent-first declaration chain.
All selected parents terminate at `extends "nothing"`; none is an unresolved guessed parent.
The parser retains **all declarations**, rather than assuming every native component applies the
same last-writer-wins field policy.

The local catalogue contains 261 field declarations, including 178 `Stats` declarations and six
script-valued fields. A string/comment-aware balanced scanner preserves script bodies and quoted
braces/comment markers. Call-like token lists are lexical navigation aids, not evaluated GEAL.
Line numbers below use decoded logical source lines; byte hashes use the unmodified resource.

Stat names are matched exactly against `data/tables/Stats.json`, SHA256
`91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da`.
Native key is zero-based row plus one. Eight distinct `set_...` field spellings have no exact
`Stats.Id` match; the lexical catalogue retains null for that exact-name lookup.
The separately verified native parser below now binds their stripped names and source-replacement
contract; those semantic results do not retroactively change the lexical source records.

The native parent/current-file and common/extension passes are already checked in the
[resource boundary report](client-hit-resource-boundary.md#14-concrete-bundled-source-chains-player-and-sand-spitter).
Those loader edges establish how source chains are visited; they do not replace the field-specific
`Stats`, `BaseEvents` or `GroundEffect` parsers.

## 3. Character and monster defaults connect to recovered formulas

These are common `Stats` declarations in `Metadata/Characters/Character.ot` and
`Metadata/Monsters/Monster.ot`. They are source defaults, not a claim that every effective network
provider currently returns the literal value: equipment, additional source layers and
[network overrides](client-stat-evaluation.md) remain separate inputs.

| Exact field / native key | Character value / line | Monster value / line |
|---|---|---|
| `energy_shield_recharge_rate_per_minute_%` / 139 | 2000 / 9 | 2000 / 8 |
| `mana_regeneration_rate_per_minute_%` / 131 | 105 / 10 | 100 / 9 |
| `maximum_physical_damage_reduction_%` / 116 | 90 / 12 | 75 / 11 |
| `base_critical_strike_multiplier` / 609 | 150 / 32 | 130 / 17 |
| `critical_ailment_dot_multiplier_+` / 6175 | 50 / 33 | 30 / 18 |
| `monster_ignite_damage_+%_final` / 6176 | Not declared | -72 / 45 |
| `monster_bleeding_damage_+%_final` / 6177 | Not declared | -86 / 46 |
| `monster_poison_damage_+%_final` / 6178 | Not declared | -50 / 47 |
| `bleeding_moving_damage_%_of_base_override` / 6180 | Not declared | 500 / 48 |

Two positive source-to-formula correspondences are already available:

- [Critical multiplier callbacks](client-critical-formulas.md#4-main-hand-override-and-multiplier)
  in their ordinary branch build 610/611 from 609 plus the appropriate hand contribution and a minimum of 100.
  Thus the source base differs between character and monster even before other modifiers.
- [The complete bleeding family](client-bleeding-output-family.md) reads 6175 for the offhand
  critical-ailment alias 10334, and reads 6177 in 6154 only when the player-minion input 1162 is zero.
  Feeding the declared -86 into that particular final factor gives 0.14, subject to the native
  arithmetic described there; this is not a complete monster-bleeding DPS calculation.

The moving-bleed value 500 is a concrete source declaration. It does **not** close its selection,
movement/aggravation consumer or active-instance publication. Likewise, this catalogue does not
pretend that matching names 6176/6178 alone recover their entire outgoing formula families.

`Character.ot` declares empty common `Buffs` and `Life` blocks at lines 109/111;
`Monster.ot` does so at lines 87/91. Empty configuration is not an absent native implementation.
The monster root additionally declares `BaseEvents.server_only = true` at line 80.
This is a field of that component, not a universal assertion that no monster component executes
on a client. Both roots have explicit client Render/Targetable blocks.

## 4. RF, daemon and invisible-fire chains

Paths in this table are under `Metadata/Monsters/`; `Monster` abbreviates `Monster.ot`.
Every arrow denotes an explicit parent reference, read parent first.

| Selected leaf | Complete chain after `Monster` | Leaf or parent additions relevant to this investigation |
|---|---|---|
| `LeagueAncestral/TrinketDaemons/RighteousFire` | `Daemon/AbstractDaemon` → `Daemon/Daemon` → `LeagueAncestral/TrinketDaemons/TrinketDaemonBase` → leaf | Parent declares ancestral rank damage factor 400; client Functions define ring-on/off effect-pack calls; client deletion calls ring-off |
| `Daemon/PrimorialAltars/AltarRighteousFire` | `Daemon/AbstractDaemon` → leaf | Leaf adds no component fields or scripts |
| `InvisibleFire/InvisibleFire10SecDurationUnrighteousFire` | `InvisibleFire/InvisibleFire` → `InvisibleFire/InvisibleFire10SecDuration` → leaf | Client construction adds an effect pack; parent includes `DiesAfterTime` |
| `InvisibleFire/InvisibleFireAfflictionColdDegen` | `InvisibleFire/InvisibleFire` → `InvisibleFire/InvisibleFireInstantAI` → leaf | Leaf includes empty `DiesAfterTime`; intermediate parent adds no fields |
| `LeagueHarvest/Red/Objects/HarvestHellionBurningGroundDaemon` | `Daemon/AbstractDaemon` → leaf | Leaf adds empty `StateMachine` |
| `LeagueAncestral/KaruiSoulcaster/DeathcasterDegenDaemon` | `Daemon/AbstractDaemon` → `Daemon/Daemon` → leaf | Leaf adds no fields |

`AbstractDaemon.ot` explicitly configures immobility, hidden status, cannot-be-damaged/stunned,
knockback prevention, curse/aura immunity, `Life` corpse flags and `DiesAfterTime`.
Its client Targetable block sets `targetable = false`.
`InvisibleFire.ot` similarly supplies its own immunities, no blocking, a projectile collision hull,
three basic Actor actions, an animated-object reference and `DiesAfterTime`.
The names `10SecDuration` and `InstantAI` are not sufficient evidence for a ten-second native timer
or an AI scheduling rule: their selected leaf declarations do not contain those values.

All four script-valued fields across the two RF leaves are in client blocks:

| Resource / line | Field | Supplied operation and limit |
|---|---|---|
| Ancestral `RighteousFire.ot` / 15 | `Functions.ring_on` | Defines `owner.AddEffectPack(...)`; this does not identify a caller of ring-on |
| Same / 17 | `Functions.ring_off` | Defines `owner.RemoveEffectPack(...)` |
| Same / 27 | `BaseEvents.on_delete` | Calls the named ring-off function |
| `InvisibleFire10SecDurationUnrighteousFire.ot` / 8 | `BaseEvents.on_construction_complete` | Calls `AddEffectPack(...)` |

The calls supply effect-pack paths, not a base damage/minute number.
This local script inspection neither turns those APIs into numeric damage publishers nor proves
that no inherited native component can publish damage.
The separately recovered [RF template/rate evidence](client-dot-source-publication.md) and
[server-mode Aura APIs](client-dot-source-authority.md) remain distinct positive sources.
No selected RF leaf names the recovered Olroth RF template; the missing template-to-active-instance
edge must still be recovered, rather than filled in from a visual effect name.

## 5. Seven ground-effect source configurations

All names below are under `Metadata/Effects/Spells/ground_effects/` and have `.ot` suffixes.
The table reports declarations after reading the complete selected chain; it is not an inferred
runtime component allocation or universal mode decision.

| Source and parent | Relevant common component declarations | Explicit field differences |
|---|---|---|
| `GroundEffect` → nothing | BaseEvents, Positioned, Animated, Stats, Buffs, Life, DiesAfterTime | Stats include maximum life 1 and item-drop slots 0; no `GroundEffect` component block |
| `StandardGroundEffect` → `GroundEffect` | Same family, with Animated/Life additions | `animated_object` names fire1.ao; `on_end_fadein` requests loop; `on_death` requests fadeout |
| `ServerGroundEffect` → nothing | BaseEvents, Positioned, GroundEffect, Buffs | Abstract source; no Animated, Life or Stats blocks |
| `VisibleServerGroundEffect` → `ServerGroundEffect` | Adds Animated | `GroundEffect.object_type = "Server"`; client Render hides buff visuals |
| `FillGroundEffect` → `ServerGroundEffect` | Reuses parent declarations | `GroundEffect.object_type = "ServerFill"` |
| `ServerOnlyGroundEffect` → `ServerGroundEffect` | Adds BaseEvents fields | `server_only = true`, `no_serialise = true`; `object_type = "ServerOnly"` |
| `ClientOnlyGroundEffect` → nothing | BaseEvents, Positioned, Animated, GroundEffect | `client_only = true`, `no_serialise = true`; `object_type = "ClientOnly"`; no Buffs block |

`StandardGroundEffect` contains the remaining two script-valued fields in this catalogue:
Animated `on_end_fadein` at line 7 and Life `on_death` at line 12.
They request `PlayAnimation(loop)` and `PlayAnimation(fadeout)` respectively.
This is explicit script content; the animation names do not establish a numerical DoT lifespan.
The [native GroundEffect clock/kill receiver](client-ground-effect-boundary.md) binds those
`object_type` strings to an enum and traces transported lifetime, expiry and conditional Buffs guard
changes separately from numerical rate publication.

`Metadata/MiscellaneousObjects/CausticGroundTargetCooldownSerialized.ot` independently extends
`nothing`, declares `BaseEvents.server_only = true` and an empty `Transitionable` block.
It declares no GroundEffect, Stats, Life or Buffs component. Its filename alone does not recover
a target cooldown timer, a stack-selection rule or a damage publisher.

## 6. Resource identities

These are raw-file identities, not normalized-text hashes. The long paths above identify each
short label unambiguously; no resource bodies or extracted game files are committed.

| Resource label | Bytes | SHA256 |
|---|---:|---|
| Character | 8950 | `a9fcdf168d84956e569c345b474aa2c2262efa61057046a87eb8036d36ef9f4b` |
| Monster | 6654 | `27bc3ceddf8e4798b1a18e239b3fc5650b302d10c9b7fc67b754b70ef61c4dff` |
| Ancestral RighteousFire | 1232 | `1f7d9840dfe30354eb53bb826992b7e1c2839e512f62383cb9db5d52b63c95e8` |
| UnrighteousFire leaf | 706 | `dfbc6af5c75230376a658dd8a74022edeea5998431de93a30378121c6d989a2c` |
| AltarRighteousFire | 130 | `bd4461af1f75b1e950fc1be0bceb8a7e1ff1817ed18367b0dc2c6bfa95676fd5` |
| TrinketDaemonBase | 288 | `0743a4bbaa07c7f0eb96b6cca60ecc2285632bddb315edc75ca5939c4e917fa1` |
| InvisibleFire10SecDuration | 184 | `b0c6368cf100c97e5c06daf9c397ef0273fcea0a0f2df5acaa214ca2544ce499` |
| AbstractDaemon | 1638 | `017c3801ee959d3453580671f016f8b995bb0bf306374c20392f0d3dfb6a6674` |
| CausticGroundTargetCooldownSerialized | 198 | `33a3b8aad7716fe64d89344dd8ba20071423ac1904f24f41afc5a9fcbff5f244` |
| HarvestHellionBurningGroundDaemon | 164 | `2be800f3f74870a36f0d459efdfcc6b291aa65885d283deaabdfec9ea72f6d91` |
| DeathcasterDegenDaemon | 110 | `e92b009e306e7f9b0ee518eafdd44d2bcd76ffe7b79c225d05e139f35eac8570` |
| InvisibleFireAfflictionColdDegen | 202 | `9599fc7ad174704ca1b04baab54120c07b6f6c96b84ddf2e5e6f39c791168d7b` |
| Daemon | 130 | `bd4461af1f75b1e950fc1be0bceb8a7e1ff1817ed18367b0dc2c6bfa95676fd5` |
| InvisibleFire | 1726 | `84cf47e786ed8cb6d644d892b3cda86da999831a37b329951341fbd6187dd899` |
| InvisibleFireInstantAI | 138 | `baa5f629901b3ae61fcaf32969a16deef20f62189937adc911f335e5333c62b8` |
| ClientOnlyGroundEffect | 556 | `0aa69b3f99a35ca361c16a177032f1afcb775af607c976bbf889248f3ef623e6` |
| FillGroundEffect | 260 | `be35338b2e644321175e70b123ceadc9922f07c9068d64932e83951f48904c07` |
| GroundEffect | 776 | `b931409da45024a653edcef3fcdca355db4dbaadf7185cb251ff47d2e4c6e300` |
| VisibleServerGroundEffect | 408 | `816b754f28aadda4b8bf9ce89f39720bb4588ba8231b21c9811e84ee8dd9b1db` |
| StandardGroundEffect | 540 | `331ed44dd4c3a738bac53ca140ff6c072cbe43a04158f83013d1eaa377f68ca7` |
| ServerGroundEffect | 306 | `4ccf371dbe75461d1c425f1588436b63c3c7b7cc1da12c63903772df19469f7b` |
| ServerOnlyGroundEffect | 390 | `2833dfb9fb5edf2ed7e3d06530d5ecdf714ae0505df0bc44ec08a21f53de41ff` |

Local `r8-effect-source-catalogue.py` reconstructs the complete bounded catalogue from the retained
raw files and checks the stat-table identity. `r8-effect-source-catalogue.json` retains the exact
provenance, repeated fields, script text, mode and parent chain of every selected resource.
These local records are ignored and are not substitutes for source-specific native verification.

## 7. Native `set_` fields replace the type's own source contribution

The previously identified type parser `0x141e406e0` distinguishes additive stat names from
`set_` names using actual bytes, rather than a naming convention invented by this catalogue.
It checks a length of at least four at `0x141e40731/0x141e40735` and compares the first four
bytes with little-endian `0x5f746573`, ASCII `set_`, at `0x141e4073b`.
That branch removes exactly four bytes before the existing name lookup
(`0x141e4075c`, `0x141e40777`, `0x141e407fc`).
The ordinary branch instead looks up the complete name.

For a resolved name, both paths derive native key `(rowAddress - tableBase) / 0x69 + 1`.
The `set_` branch first calls per-key source removal `0x1424fc9e0` at `0x141e408d3`,
passing a pointer to an eight-byte zero token and the resolved key.
The shared successful-lookup tail then calls native add `0x1424fc480` at `0x141e409e0`
with that same zero token and the supplied signed32 value retained in `R15D`.
The already recovered [source-removal contract](client-stat-evaluation.md#native-source-tokens-retain-additive-contributions)
compares both token words, removes matching entries and rebuilds the raw total from the rest.
Consequently this operation replaces the contribution for **this key and zero token**;
it does not clear every source, install a network override, or bypass derived-stat evaluation.
A zero replacement still removes the previous contribution before the add helper's zero-value guard.

The eight exact selected fields now have the following conditional parser meanings:

| Resource field | Stripped `Stats.Id` / native key | Selected declared values |
|---|---|---|
| `set_item_drop_slots` | `item_drop_slots` / 2 | 0 |
| `set_monster_no_drops_or_experience` | `monster_no_drops_or_experience` / 890 | 1 |
| `set_monster_do_not_fracture` | `monster_do_not_fracture` / 2513 | 1 |
| `set_max_frenzy_charges` | `max_frenzy_charges` / 183 | 0 |
| `set_max_endurance_charges` | `max_endurance_charges` / 182 | 0 |
| `set_max_power_charges` | `max_power_charges` / 184 | 0 |
| `set_monster_no_beyond_portal` | `monster_no_beyond_portal` / 2481 | 1 |
| `set_cannot_be_afflicted` | `cannot_be_afflicted` / 11691 | 1 |

These declarations occur in the already hashed AbstractDaemon and InvisibleFire resources.
This closes their field-name/native-source semantics, conditional on dispatch to this parser;
it does not prove which resource a live entity loaded or supply a DoT-instance publisher.
Unresolved-name behavior and every other component's field merge rules remain separate.

Ignored `r11-type-stat-prefix.py` rechecks both complete function records against the pinned PE:
two ranges / 398 instructions, with 24 explicit instruction anchors.
It also checks every one of the 23,346 valid row displacements against the original
multiply-high/shift division and matches the eight stripped names against the pinned Stats table.
This is offline verification, not execution of the complete parser or game.
The retained result is `r11-type-stat-prefix/verification.json`.

## 8. Remaining work

The selected inheritance/field/script catalogue is established within its stated boundary.
The general source-configuration task remains partial: expand from actual positive resource or
data links, recover the relevant component parsers and runtime callers, and connect source rate
creation/selection/removal to the incoming typed rates.
Do not repeat the old broad custom-event keyword search as though it would close those edges.
AO/embedded programs are next steps only where their data or event contracts affect that numerical
or lifetime boundary; full rendering reconstruction is outside this investigation.
