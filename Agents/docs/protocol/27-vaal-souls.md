# Vaal soul collection and native budgets (3.29.3b)

Issue #308 implements collection, bounded connection state and the native Actor budget payloads.
Implementation source `66571d55d5d5bd70f9e7b631849f77fbe862ddbe` is based on main `939b44fe`.
Automated wire/dispatcher tests and 42 bounded real-client collection/counter checks pass.
This establishes the collection/counter scope, not full Vaal execution or lifecycle parity.
Unsupported Vaal casts are refused before action echoes, including full budgets and effective zero cost.
The collection slice itself adds no executor, Rage payment, prevention timer or Phrecia gate change.
Behemoth's separate [class extension](../game/phrecia-class-completion.md) adds paid Vaal Ground Slam and a
pausable prevention countdown; other Vaal executors remain refused.

## Primary evidence

Existing read-only exports `vaal-research-primary.c`, `vaal-research-manager.c` and
`vaal-research-consumers.c` establish these readers and consumers.
The research handoff is `logs/vaal-research-handoff.txt` in the parent's research checkout.
These ignored research files and game data are not committed.

The parent's UTF-8 `vaal-r22-wire-flag-constructor-dis.txt` proves the missing skill-record flag independently.
`vaal-research-primary.c:717` reads wire flags immediately after the GEPL/stat-set references.
At `0x1421975f0`, flags enter `eax`; `0x1421975f7` tests wire bit1 (0x02).
When that bit is clear and raw GEPL +0x2c stored uses are nonzero, `dil = 1`; otherwise `dil = 0`.
`0x1421976e8..0x1421976f6` passes constructor parameter13 as `(wireBit2 * 2) | dil`.
The parent's `vaal-research-skill-dis.txt` shows `0x141ee7571..0x141ee75ba` testing parameter13 bit0.
If set, it zeros skill +0xe8 base souls, +0xec stored uses and +0xf0 soul multiplier.
If clear, it copies GEPL +0x28/+0x2c and retains the selected multiplier.
Server-granted Vaal resource skills therefore require skill-record bit1 to retain these native inputs.
This changes neither parent-object bit0 nor third bit2 and adds no fields to the record.

Actor reader `0x1421932b0` selects `0x142197b10` for flag 0x40 and `0x142198800` for flag 0x20.
Both use Actor +0x598's skill manager and its sorted vector at manager +0x188.
Entries have stride 0x20: ActiveSkills row pointer +0, capacity +0x10, current +0x14 and dirty +0x18.
Identity is the zero-based **ActiveSkills** row, using 0x101-byte rows.
It is not the GrantedEffects row, GEPL row, effect ID, skill instance ID or cooldown grouping alias.
`0x141eea5b0` follows GEPL to GrantedEffects +0x63's ActiveSkills pointer and finds this same entry.

All integers below are little endian inside the component blob.

```
Actor 0x40, after the skill records:
  u8 count
  count * (u32 ActiveSkills row, u32 capacity, u32 current)
  u32 cooldown count, existing cooldown trailer

Actor 0x20, after the added skill records and removed u16 skill IDs:
  u8 seed count
  seeds * (u32 ActiveSkills row, u32 capacity)
  u8 update count
  updates * (u32 ActiveSkills row, u32 current, u32 NEW capacity)
```

0x40 upserts capacity and `min(capacity, current)`.
0x20 seeds upsert capacity with current zero and notify the UI.
0x20 updates first clamp current against the **old** capacity, then assign the new capacity and mark dirty.
The update loop assumes that its row already exists.
Empty lists in either reader preserve existing entries.
Removal therefore sends an explicit `(row, 0, 0)` update.
Unknown rows and increases seed the capacity before updating current, avoiding the old-capacity clamp.
Routine skill refreshes do not reseed unchanged rows or refill balances.
Cooldown-only, Rage and Fortification refreshes can keep their empty soul lists without clearing the native budget.

`0x141eea3a0` checks required souls against entry +0x14.
`0x141ee9d30` contains the native spend at `0x141eea350..0x141eea362` and marks dirty.
`0x140ad7fb0` and `0x140826070` display current/capacity text.
`0x140acfbb0` computes icon uses and fractional fill from capacity/current divided by required souls.
Its divisions and modulus are guarded by `required != 0`.
Zero required souls pass the resource comparison; they still do not enable an unsupported server executor.

## Catalog and effective cost

The existing tables supply GEPL `VaalSouls`, `VaalStoredUses` and GrantedEffects `ActiveSkill`.
Missing tables, missing fields, negative costs/uses and invalid references do not create budgets.
Vaal Fireball level 1 is GE 3366, GEPL 532, ActiveSkills 87, cost 30 and stored uses 1.
Vaal Haste (GE 1963, AS 239) and Vaal Grace (GE 1994, AS 247) each have base cost 50 and stored uses 1.
Both `SkillCatalog.Find` and `FindByRow` set typed `GrantedSkill.VaalResourcesEnabled` at their shared metadata seam.
The existing valid Vaal definition requires a nonnegative raw cost, positive raw stored uses and a valid active row.
This flag remains set at zero effective cost, zero capacity and zero current souls, including no-souls overrides.
Removing or explicitly zeroing an override does not change the flag or refill the resource.
The property defaults false; ordinary Fireball, default attacks and support records retain their existing bytes.
No extraction dependency or generated file changes, so the extraction manifest needs no version bump.
Native construction `0x141ee7592..0x141ee75b4` sets base cost, raw stored uses and the selected multiplier.
Getter `0x141ee8720` reads the following keys, whose names come from Stats.json:

- 14270: `virtual_vaal_skill_require_no_souls_per_use`.
- 19748: `virtual_non_aura_vaal_skill_soul_requirement_multipler`.
- 19749: `virtual_aura_vaal_skill_soul_requirement_multipler`.
- 15801: `skill_is_aura_skill`, selecting key 19749 when nonzero, otherwise 19748.
- 14089: `unique_ring_support_effect_vaal_skill_soul_requirement_+%_final`.

The spelling `multipler` is literal table data.
Any nonzero no-souls flag returns zero.
Otherwise, with truncation toward zero at each division:

```
t = baseCost + trunc(baseCost * selectedMultiplier / 100)
required = max(0, t + trunc(t * finalPercent / 100))
```

The server uses long arithmetic for the first stage and a widened product for the second stage.
It saturates the result and the capacity to `int.MaxValue` instead of emulating signed native overflow.
Native parity is not claimed for out-of-range developer inputs.
Only these proved cost inputs are combined from current character and resolved skill/support stats.
Explicit developer values, including zero, retain final precedence and are also published on the skill record.
The aura selector uses each instance's ActiveSkills Aura type and accepted support AddedActiveSkillTypes.
It does not require an ordinary stat-set entry and does not inherit the caster's global selector.
An explicit developer selector replacement deliberately applies to every instance, including explicit zero.
At +100% aura requirement, Vaal Haste and Grace have capacity 100 while Vaal Fireball remains at 30.
This is not a general Vaal modifier or unrelated cost engine.

## LocalExile collection and capacity policy

Official distribution and global capacity algorithms are not established by this evidence.
The following are explicit LocalExile policies:

- Each living owner's actual hostile current-monster kill within `ExperienceRange` awards one soul.
  Rarity does not change the amount.
  Identity and eligibility are committed under `Monsters` with the lethal Life/Dead mutation.
  Default attacks, shared direct/projectile/area hits and Righteous Fire all use this boundary.
  A per-monster soul-credit flag prevents duplicate or later corpse credit without changing XP/loot/charge policy.
  Ineligible deaths are consumed at death too; later notification callbacks cannot award souls.
- Recipients are sorted distinct eligible nonfull ActiveSkills rows, visited deterministically round-robin.
  Equipped instances sharing a row share one balance.
  Each source proposes `effectiveRequired * rawGEPLStoredUses`; the row uses the maximum proposal.
  Effective supported gem level and each source's requirements are respected.
- Removing the last eligible instance loses its balance.
  Source/cost/cap changes clamp without minting or refilling; adding a larger source only raises the cap.
  Zero effective cost yields zero capacity and cannot collect.
- Towns, dead/closed/leaving/retired/paused owners, friendly or stale monsters and replayed corpses cannot collect.
  The current actor's signed nonzero `has_soul_gain_prevention` (8855) also prevents gain.
  This uses the existing character stat seam; no countdown is inferred or started.
- The positive-to-zero player Life transition clears souls inside `Monsters`; relog starts empty.
  This covers monster swings and every developer damage route through the shared Life setter.
  Delayed death notifications publish current soul state and cannot clear a later resurrection's earnings.
  Successful area-ticket transfer moves the budget and round-robin position once to the same character.
  The old connection retires; the ticket expires after five minutes, matching Rage's transfer policy.
  Area loading freezes this transient state; there is no decay and no persistent save schema.

Creation publishes full 0x40 budgets alongside current skills.
Real `SendSkills` reconciles sources before awaiting and again at the send gate.
Kill publication also reads the current state at the send gate, so queued removal/death/transfer cannot resurrect souls.
Holding death notifications cannot reclassify prevention, range or source eligibility from the lethal event.
Vaal gem and accepted-support requirements use one immutable current-equipment snapshot.
Attribute gear, host activation, socket places and link groups are resolved from that same inventory read.
The snapshot reuses the character's equipment/passive attribute resolver and explicit requirement replacements.
It preserves main's wide raw attribute buckets, percentage scaling and explicit-zero input replacements.
Final Strength/Intelligence mana, pool flags, supported skill payment and current send-gate publication remain intact.
It does not rebuild/refill combat pools or publish an older requirement snapshot into a cache.
Removing attribute gear therefore affects the accepted kill even while inventory notifications hold cached pool values.
`VaalSoulState.TryConsume` exposes a later atomic payment seam under the caller's `Monsters` lock.
No unsupported cast calls it.
The shared skill preflight refuses raw Vaal instances before movement, action echoes or cooldown reservation.
This also applies when soul-cost modifiers or the no-souls flag reduce their effective requirement to zero.
The execution backstop remains in place; ordinary partner skills retain their normal admission and mana payment.

## Verification and remaining scope

Source validation covers the solution build and Core, Data and Gateway suites on base `939b44fe`.
At implementation `66571d55`, all 531 Core, 18 Data and 3404 Gateway tests pass, with no skips.
The solution build has zero warnings and errors.
Exact composition with PR #294 head `911f06a7` passes 531 Core, 18 Data and 3436 Gateway tests at `0615fc3f`.
That private integration preserves both mechanics; PR #294 is not imported into this feature branch.
Combined regressions cover wide attribute requirements, mana, support removal, ordinary mana payment and soul payloads.
Parsed Core creation 0x40 and update 0x20 regressions check flag 0x02 and immediate support layout.
No parent fields are present.
The literal primary constructor model shows old flags zeroing soul inputs and flag 0x02 retaining base 30/uses 1.
Real catalog/socketed dispatcher tests cover Vaal Fireball, Vaal Haste, ordinary partners, supports and inactive hosts.
They retain bit1 across no-souls zero/removal and zero budgets, and check reacquisition without restoring balance.
Twenty-four admission regressions cover targeted, ground, self, approach, attack-move and instant inputs.
Full, no-souls and reduced-to-zero costs cannot confirm actions, move, reserve cooldowns or spend resources.
The same gem's ordinary partner retains normal admission.

Client 3.29.3b completed one unchanged 42-check scenario with a new synthetic character in the Strand, seed 1.
The native required-soul getter and Actor manager consumer reported current, capacity and cost together.
An eligible Vaal Fireball started at 0/30; real hostile kills charged it to 30/30 and another kill stayed capped.
Nonlethal damage and a killed friendly monster awarded no souls.
Cost changes clamped to 15/15, raised capacity to 60 with current 15, then restored capacity 30 without refilling.
The no-souls flag produced zero cost/capacity/balance; clearing it restored capacity 30 with zero balance.
Soul-gain prevention rejected an actual kill, and clearing it allowed the next kill to award one soul.
Finally an unsupported Vaal input preserved mana, target Life and the one-soul balance, with no action echo.
Natural gem level-ups retained the instance identities and native budget observations.
Owned-character logout, observer detach and temporary Host cleanup completed without errors.
The ordinary server was restored without rebuilding or extracting game data.

Native implementation revision: `66571d55d5d5bd70f9e7b631849f77fbe862ddbe`.
Gateway SHA256: `ce82eb040afcb4fb6e8502570cb422a3e257f79ee740936daec48e4404412d3a`.
Core SHA256: `5eca39052d7ef34d41e2c585c1cd4a0283fd689594577ca5cd2ccb7c8aabdb3f`.
Host SHA256: `51dfc0f077fd9a3edf8f7dc3e64dc59942ee664c9290ad99437fa6b7adb629ab`.
All embedded informational versions matched that implementation revision; the listener path and cwd were checked.
The subsequent verification-documentation commit changes no production source or tests.
Ignored evidence is retained as `vaal308-level-r25/run-20261028-001117` in the parent's local logs.
Earlier partial/failed runs are excluded from the accepted count.

To reproduce, start through `scripts/dev/live.ps1` and socket a usable level-1 Vaal Fireball on a new test character.
Use its ordinary Fireball partner for real nearby kills and inspect the native consumers mapped above.
Repeat with `virtual_non_aura_vaal_skill_soul_requirement_multipler` at -50, 100 and its original value.
Toggle `virtual_vaal_skill_require_no_souls_per_use` and `has_soul_gain_prevention` through `/set_stat`.
Check native Life, mana, manager balances and action logs, rather than treating command replies as proof.

Duplicates, last-source removal, cooldown refresh, death/relog/transfer and held notifications have automated coverage.
Their native lifecycle acceptance, Vaal Haste/Grace aura-cost composition and held-equipment interleavings remain open.
No HUD animation, SGP countdown, native death/transfer session or complete Vaal parity is claimed by these 42 checks.
Official distribution, all Vaal modifiers, SGP countdown ownership, refunds and executors remain separate work.
