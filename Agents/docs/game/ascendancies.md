# Ascendancy selection, points and saves

An ascendancy is a second class chosen for an existing character.
This change supplies the server's choice and passive-point lifecycle through chat commands.
It does not implement the native Labyrinth/Altar chooser (#151), or every special effect of the 19 Phrecia classes.

## What can be used

Ordinary ascendancies keep their normal choices, including the Scion alternatives.
The server can list the 21 ordinary and 19 Phrecia definitions from the exact 3.29.3b tables and graph export.
Each choice is identified by family, stable table Id and revision; two families can share a class number and start node.
Only one main ascendancy is selected at a time, and it must belong to the character's base class.

**Phrecia play is explicitly gated by default.**
Gambler has an explicit read-only installed-graph opt-in; see [its checklist and scope](gambler.md).
Aristocrat has the same check behind `Game_VERIFIED_ARISTOCRAT=1`; see [its checklist](aristocrat.md).
Each opt-in is limited to its identity and the exact pinned graph bytes.
The shipped alternate graph contains older ordinary-tree topology.
A separately agreed client-compatible graph is required before Phrecia can be enabled in an actual game connection.
Antiquarian has an explicit `Game_VERIFIED_ANTIQUARIAN=1` opt-in which verifies exact installed archive bytes read-only.
Its [whole-class audit](antiquarian.md) records the hashes, all node consumers, legal builds and native acceptance.
Behemoth has a read-only opt-in behind `Game_VERIFIED_BEHEMOTH=1`; see [its class audit](phrecia-class-completion.md).
Other Phrecia choices are refused before saving, and their existing saves refuse admission before inventory loading
or passive normalization, retaining their data.
There is no automatic graph installation, recovery, archive rewrite, or memory-hook workaround in this slice.
Ordinary startup and unascended/ordinary characters do not require an alternate-graph match.
The integration tests use isolated Phrecia lifecycle fixtures; that is not a claim of play on a pristine client.
The earlier ordinary-client checks below cover the command selection lifecycle; Antiquarian has separate evidence.

## Try ordinary selection

From the repository root, extract the data with `scripts/dev/extract-data.ps1` and start through `scripts/dev/live.ps1`.
The read-only `passive-graphs` extraction dependency supplies `data/passive-skill-graphs.json`.
In chat, on a Duelist for example:

```text
/ascendancy
/ascendancy normal Slayer
/ascendancy_points normal
```

The first command lists choices for this character.
The second selects Slayer and reloads the area; the character keeps its account, league and stash.
The third is a development shortcut that grants two points once for that Labyrinth tier.
It does not complete the Labyrinth or unlock an Altar UI.
Use `cruel`, `merciless`, `eternal`, or `all` to grant the other tiers, each at most once, up to eight earned points.

Open the normal passive tree, allocate a connected ascendancy node and apply it.
`/passives` lists ordinary points; `/ascendancy` lists the selected class and its separate earned/spent points.
An ascendancy node that grants ordinary skill points adds them to the ordinary budget.
It does not add to the character's level or quest-point field.
The class start is implicit and never charged or saved as an allocated node.

## Refunds and changing class

Use the native Refund Passives operation, or `/reset_ascendancy` to refund the whole selected ascendancy.
An ordinary passive costs one refund point; an ascendancy node or selected choice effect costs five.
A multiple-choice parent costs one ascendancy allocation point but zero refund points; its selected effect costs
zero extra allocation points but five refund points to remove.
Gold respec and `/reset_passives` affect ordinary nodes only.
The server refuses any refund that would disconnect a branch or remove ordinary points that are still spent.
Refund the dependent ordinary nodes first.
Once all ascendancy nodes are refunded, `/ascendancy normal <name>` can choose another ordinary class.

## Saves and validation

Identity, ascendancy nodes, tier awards, ordinary nodes/masteries and refund payments save atomically before success
is published to the connection.
An old area cannot overwrite a newer connection's allocation or refund payment.
Unknown fields are retained, and missing graph data or unknown saved identities/nodes refuse loading without dropping
that progress.
Old characters with no ascendancy field remain unascended.

Ordinary allocations must connect to the base class's implicit start in the chosen graph.
An allocated Ascendant Path node can supply another class start; refunding it cannot orphan that ordinary branch.
Masteries cannot act as bridges, and proxy/cluster expansion nodes remain unsupported.
A lower development level removes safely removable ordinary leaves only; it retains ascendancy points and nodes.

The existing ordinary passive stat aggregation also sees allocated ascendancy stats.
The selection lifecycle alone does not implement a class's signature gameplay.
Antiquarian's Ward, Adrenaline, duration, stun and Lioneye consumers are described in its linked class audit.

### Whaletooth Bangle duration stat

Antiquarian node 27339, `Exhibit 39: Whaletooth Bangle`, supplies
`skill_effect_duration_+%_per_10_strength` = 1 (3.29.3b Stats key 2817).
`SkillDurationModifiers` adds `floor(max(0, final Strength) / 10) * per-ten value` to the raw
`skill_effect_duration_+%` in `CharacterPlayerStats`.
For example, 455 final Strength gives +45%; an ordinary +10% duration passive makes the total +55%.
The calculation uses the resolved attributes after flat/increased sources, or the explicit final Strength override.
It runs on a fresh dictionary, never writes back into sources and clamps signed totals after addition.
An explicit `/set_stat skill_effect_duration_+%` replaces the final result, including zero and negative values.
Removing that replacement with `off` restores derivation; removing the per-ten source removes its bonus.

The automated lifecycle fixture allocates the real connected Antiquarian path, reloads its saved state,
refunds the notable for five points, reloads again and reallocates it.
This fixture does not admit a Phrecia game connection.
On source `d842288a` over main `0805c0ca`, an unascended synthetic Marauder passed 15 native stat checks.
The 3.29.3b client received 45% at 455/459 Strength and 46% at 460, plus signed-source and final-override checks.
Flat/increased attributes produced 455/449 final Strength and 45%/44% duration; repeated rebuilds did not accumulate.
Source removal and relog cleared the transient derived stats; cleanup returned the single client to character selection.
Evidence: `logs/whaletooth-native/20261028-120417/` (metadata and results), with Gateway command logs in the checkout.
Gateway/Core/Host embedded versions, listening Gateway path/CWD and the clean source revision were checked.
The tested Gateway SHA-256 was `a2f7fb652ec8b6f39aa0d47a9c60b8362468bba26847412b8302d8bf8c35073c`.
The solution built without warnings; Core 638, Data 18 and Gateway 3500 tests passed with public extracted data.
The original prerequisite supplied a shared owner stat; the whole-class implementation now executes Enduring Cry recovery.
Native selected Antiquarian with 272 final Strength recovers for 3.175 seconds rather than 2.5, at the same rate.
The paired paid-refund control and resource totals are recorded in [Antiquarian](antiquarian.md#native-evidence).
It does not extend minions' own skills or buffs, charge duration or flask duration.
Additional skill/summon lifetime engines need their own elapsed-time acceptance.
A current-main ordinary-client regression also checks duration together with Ward publication, explicit zero
replacement and clearing developer inputs on relog; this does not replace whole-class native acceptance.

### Family gem levels

The common active-gem calculation consumes four Phrecia family-level sources:

| Node | Source | Bonus |
|---|---|---|
| Herald 40058 | `herald_skill_gem_level_+` | +3 |
| Architect of Chaos 45399 | `vaal_skill_gem_level_+` | +3 |
| Paladin 34448 | `link_skill_gem_level_+` | +5 |
| Puppeteer 18817 | `raise_zombie_gem_level_+` | +5 |

`SkillGemFamilies` reads the target client's GemTags stat columns and SkillGems minion-level column.
Membership belongs to the gem, not its display name or one granted effect.
The Vaal bonus raises both skills of a Vaal gem; transfigured Raise Zombie retains its family.
`SkillGemLevel` combines intrinsic level, linked-support level bonus and matching family sources before clamping.
Explicit family `/set_stat` replacements win over passive/equipment sources; `off` restores the source total.
Support gems receive no active-family bonus, even when their tags match.

The effective level feeds supported skill stats, cost, reservation, damage and native skill records.
Repeated reads do not alter gem experience, quality or intrinsic requirement checks.
The item UI still shows its intrinsic gem level; an inactive host remains inactive.
Automated checks allocate each real connected class path, reload, pay the notable refund and reallocate it.
These lifecycle fixtures do not open Phrecia game connections.
Source `49e804e0` over main `0805c0ca` passed 28 native checks on ordinary synthetic characters in 3.29.3b.
Herald of Thunder and both Vaal Fireball skills changed from level 1 to 4; Soul Link and Raise Zombie changed to 6.
Unrelated sources did not affect them; repeated changes, source removal and relog preserved intrinsic gem storage.
The checks used developer family inputs, not class selection or native refunds.
Evidence: `logs/family-gem-levels/20261028-123153/`, with Gateway skill-publication logs in the source checkout.
Gateway/Core/Host embedded revisions, the listening DLL path/CWD and the clean source revision were verified.
Gateway SHA-256: `2024b41ffac57ffbe12cbb09cb9ad6b1cbc27daf38b49c1e734a2a9437aedc3f`.
The first observer attempt timed out before feature checks; the accepted run used the existing native skill-list probe.
The solution built without warnings; Core 638, Data 18 and Gateway 3507 tests passed with public extracted data.
Family-specific Herald, Link, minion and Vaal execution engines remain separate, as does Phrecia client admission.
This slice does not implement global active-gem levels/quality, additional support-level bonuses or item-local gem levels.
The identity/point lifecycle itself adds no Ward, Rage, charge, minion or other signature gameplay engine.
Choosing a class is not evidence that its signature gameplay works.
Behemoth has historical native acceptance; see [the class audit](phrecia-class-completion.md).
That document separates the current-main automated candidate from the earlier whole-class native evidence.

### Forest Tracking attack damage

Daughter of Oshabi node 6281, Forest Tracking, grants `attack_damage_+%_per_450_evasion` = 3 (client key 2434).
The common final-stat calculation adds three increased attack-damage points for each complete 450 final evasion.
449 gives zero, 450/899 gives three, and 900 gives six.
The source value is a signed coefficient, not a boolean.
It uses the resolved rating after defence modifiers, Iron Reflexes and explicit final-rating replacements.
An Iron Reflexes character with zero final evasion receives no Forest Tracking contribution.

`ArmourAttackDamage.Total` preserves the existing Steel Willed entry point and adds optional evasion inputs.
Both defence contributions and raw increased attack damage sum in wide arithmetic before one final clamp.
This preserves signed cancellation and avoids turning the new bonus into a final damage multiplier.
It applies once to weapon and added attack damage through the existing ordinary/supported/attack-gem paths.
Spells receive no attack-only increase; conversion, support effectiveness and final Rage scaling keep their order.
Repeated builds and source removal operate on fresh raw inputs without cached increases.
As with Steel Willed, an explicit attack-damage input remains additive with the derived contribution.

Automated checks cover the real node, complete-group boundaries, signed overflow/cancellation, final defence conversion,
ordinary and supported damage, spell isolation, allocation, paid refund, reload and reallocation.
The lifecycle fixture does not open a Phrecia connection.
Source `018ef238` over main `0805c0ca` passed 17 native controls on a new ordinary unascended Witch.
Fixed chaos attacks dealt 100 at 449 evasion, 103 at 450/899 and 106 at 900; panel and native monster Life agreed.
Generic/raw attack increases stayed additive; simultaneous armour/evasion inputs produced 110 damage.
Removing either source retained the other; maximum-mana lightning and physical weapon panel controls also passed.
Relog cleared transient derived stats, and cleanup returned the single client to login.
Evidence: `logs/forest-tracking/native-20261028-124332/`, with Gateway command/hit logs in the source checkout.
Clean source, embedded Gateway/Core/Host revisions and the live Gateway path/CWD were checked.
Gateway SHA-256: `4ea9695a9d9e8b2fac257060b97fa5aae81b6a7674fafc7e266bc2362189c826`.
The solution built without warnings; Core 638, Data 18 and Gateway 3500 tests passed with public extracted data.
Full Daughter of Oshabi gameplay and Phrecia client admission remain separate work.

## Validation and limits

Automated tests cover catalog ownership, family separation, native class packing and graph stat, implicit starts,
separate budgets, exclusive choices, ordinary/ascendancy connectivity, paid refunds, old/unknown saves, rollback,
stale connections, regret items and the production passive-message handlers.
Run the Core, Data and Gateway suites from the repository root with the extracted public data available.
The shared Gateway `TestEnvironment` sets the repository working directory once.

The main-integrated source `fd125e7` passed Core 495, Data 18 and Gateway 2375 tests and a warning-free solution build.
Independent review found no remaining substantial issues in the corrected XP and extra-point grant paths.
The merge review preserved both main's locked passive snapshot and the selected ascendancy's stat contribution.
Fourteen deterministic regressions cover delayed XP, overlapping area connections, paid refunds and failed saves.
The complete suites ran in a clean public-data checkout with freshly extracted collision and hideout catalogs.
That checkout has no recorded walk maps; the optional real-capture quest check is outside this verification.

The real 3.29.3b client passed eleven smoke checks on a new synthetic Duelist, choosing ordinary Slayer.
The read-only `scripts/re/frida/trace-ascendancies.js` observed the client's applied tree and counters.
The ordinary graph stayed `Default`, with class byte 4 before selection and 68 afterward.
Ordinary node 39725 survived selection; ascendancy node 61393 used one of two separately earned points.
Repeating the normal-tier award retained two earned points.
An ascendancy refund spent five refund points; an ordinary refund spent one and retained the ascendancy node.
Two extra-point grants produced two ordinary extra points.
Choice, nodes and budgets survived relog and a Gateway process restart, with Auth and Patch left running.
A Phrecia selection request was refused and the synthetic character's saved JSON remained byte-for-byte unchanged.
Admission of an existing saved Phrecia choice is covered by automated tests, not this native smoke.

Local evidence is in `logs/phrecia-completion/choice274-20261026-204858/` in the verification workspace.
The tested Gateway SHA-256 was
`d08482d2c90ed3ac42ab8f4e1ed37619cfb511be50b8442e55f9ce8b24e7b93d`.
Gateway, Core and Host embedded versions matched the source; the live Gateway path and working directory were checked.
The optional merged-graph analysis PSG was absent locally; the JSON graph catalog supplied the ordinary lifecycle.
The client archive was unchanged, and the accepted normal Gateway was restored after the smoke.

To repeat, start the single client through `scripts/dev/live.ps1` and use the ordinary commands above.
Compare the trace's `ascendancyPassives` and `ascendancyGraph` observations with the Gateway log and saved progress.
The smoke does not test the native Altar chooser, every ordinary class, or special ascendancy combat mechanics.
That historical smoke does not establish Phrecia gameplay.
The historical Behemoth audit verifies compatible-graph admission, gameplay and return to ordinary Juggernaut.
That historical selection-only smoke does not replace the separate Antiquarian gameplay acceptance.

Protocol fields and native evidence: [progression](../protocol/15-progression.md) and
[3.29.3b identity/graph research](../re/ascendancy-client-3.29.3b.md).
