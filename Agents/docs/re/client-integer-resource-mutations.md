# Integer resource changes: costs and arrow life sacrifice (3.29.3b)

The executable has a second numerical pool interface in addition to the floating
continuous-loss reducers: **integer deltas at pool slot `+0x28`**, and an absolute
setter. Concrete skill-cost and extra-arrow life-sacrifice paths reach it.
These are positive resource-mutation contracts, conditional on their callers and
inputs. They do not close accepted Hit → damage → mitigation → pool loss.

This supplements [the receiver boundary](client-hit-resource-boundary.md),
[continuous integration](client-damage-timing.md) and
[life-cost offence inputs](client-spell-contributor-builders.md).
No executable was launched, attached or run as native code; no new traffic was captured.
R0/R1 remain Established; R2/R3/R4 remain Partial under the original criteria.

## Identity and concrete interfaces

The input is the same original `Client/Game.exe`, SHA256
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`.
Preferred image base is `0x140000000`; all addresses below are preferred VAs.
The Stats extract has 23,346 rows, native key = row + 1, SHA256
`91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da`.
Names are analyst labels for verified instructions, not recovered source symbols.

The established Life constructor installs these embedded-pool tables:

| Pool, relative to Life | Table | Integer delta `+0x28` | Floor getter `+0x50` |
|---|---|---|---|
| Life `+0x178` | `0x1435a5da0` | `0x141e699d0` | `0x1400ecfa0`, returns 1 |
| Mana `+0x1c8` | `0x1435a5b68` | `0x141e6b6e0` | `0x1400ecd70`, returns 0 |
| ES `+0x210` | `0x1435a6218` | `0x141e6c0a0` | `0x1400ecd70`, returns 0 |

The integer-delta ABI is `RCX=pool`, `EDX=signed delta`, `R8D=context integer`,
`R9b=reason`. It differs from the floating `+0x48` loss ABI.
Life's wrapper can apply cannot-die, minimum-Life, recovery and reason-specific
policies; Mana/ES have their own guards. An arbitrary slot-`+0x28` call is still
not sufficient to identify one of these receivers.

Absolute setter `0x141e698e0` takes `EDX=new current`, `R8b=reason`.
It clamps to zero and the unreserved maximum, writes pool `+0x30` at
`0x141e69939`, and dispatches guarded change/empty/full notifications.
Pool byte `+0x24` can reject increases except reason 11.
This setter does not write the fractional carry at `+0x28`.
It is distinct from the decoded-network assignment setter `0x141e69fb0`.

## Extra arrows from sacrificed Life

`0x1419e3530` contains a concrete consumer of these skill inputs:

| Key | Id |
|---|---|
| 21321 | `sacrifice_%_life_to_fire_additional_arrow_for_each_100_life` |
| 21362 | `skip_life_sacrifice_chance_%` |
| 852 / 2989 | `skill_can_fire_arrows` / `skill_is_attack` |
| 3 | `main_hand_weapon_type` |

The function first tests action latch `+0x308`; an already-set latch exits.
Otherwise it sets the latch before the work. In the selected sacrifice branch,
21321 must be nonzero, the source must resolve a Life component, both Boolean
skill qualifications must hold, and weapon type must equal 9. The branch reads
the selected skill provider via action stat-set byte `+0x174`, not a universal
character-wide provider.

Define `W` as signed 32-bit wrapping and `T(n,100)` as signed division toward zero.
The concrete Life floor above is 1:

```text
available = max(W(currentLife - 1), 0)
percentageAmount = T(W(S[21321] * maximumLife), 100)
amount = min(available, percentageAmount)
if amount <= 0: stop this branch

action[2c0] = W(action[2c0] + T(amount, 100))
sample = the prepared integer draw supplied to the 0..99 helper
if sample < signed32(S[21362]): stop this branch
Life.integerDelta(-amount, sourceEntityId, reason=7)
```

The actual signed multiply/constant-division sequence is at
`0x1419e37cd..0x1419e37e3`. The accumulator update is at `0x1419e3810`,
**before** the skip comparison at `0x1419e3898..0x1419e389a` and the concrete
Life call at `0x1419e38bc`. Thus skipping the sacrifice retains the arrow
accumulator benefit. It does not reroll a lower arrow amount.

With maximum/current Life 1,000, 21321 = 20 and initial accumulator 7,
the branch computes amount 200 and accumulator 9. A supplied draw 49 with
skip chance 50 retains 1,000 Life; draw 50 pays and leaves 800 Life.
With a sufficient percentage and current Life 1,000, the floor limits the
amount to 999, giving nine whole additional units and leaving 1 Life.
These examples assume nonmutating notifications and the stated pool inputs.

Caller `0x1419e3b10` invokes this function at `0x1419e3b19` before its subsequent
action work and repeat setup. This pass does not reconstruct RNG state/order,
final projectile count, frenzy-charge consumption, minion sacrifice or all
callers of that wrapper. Those action responsibilities stay with the pinned
[projectile research](https://github.com/LocalExile/LocalExile/blob/dee19667a2bd14a92fa07d024110c4f9732f59de/Agents/docs/re/projectiles/repeat-source-state.md).
The established arrow-accumulator write is not itself proof of a visible launch.

## Skill-cost preparation, affordability and payment

Three different functions own the stages:

| Function | Responsibility |
|---|---|
| `0x141e6c7d0` | Copy/adjust a supplied cost record |
| `0x141e6c950` | Check adjusted costs against available resources |
| `0x141e6ca30` | Prepare, check, then apply resource deltas |

The record holds a kind byte at `+0`, Mana/Life/ES costs at `+4/+8/+0xc`
and a Rage requirement at `+0x10`. These are in-memory offsets, not wire fields.
For the installed pools, `availableLife=max(currentLife-1,0)`;
Mana and ES use `max(current,0)`.
The floor getter is separate from reservation and the maximum resource.

Preparation preserves the original branch order:

1. Kinds 0–3 and 5 enter the ordinary costs path. With 5339
   `virtual_spend_energy_shield_for_costs_before_mana`, ES available **after the
   record's existing ES cost** can replace up to the Mana cost. That amount
   moves from the Mana field to the ES field.
2. If the remaining Mana cost exceeds available Mana, 14222
   `skill_usable_with_insufficient_mana` clamps it to the available amount.
   Separately, 21033 `skill_usable_with_insufficient_life` can clamp Life cost
   to available Life. Neither branch transfers its excess to another pool.
3. Kind 4 instead tests 844 `use_life_in_place_of_mana_for_skill_effects`:
   if true, wrapping-add Mana cost to Life cost and clear Mana cost.
   Other kinds skip these selected adjustments. The numeric kind values alone
   do not establish universal gameplay names.

`0x141e6c950` then compares the three costs against their available pools.
Rage availability is wrapping `7594 current_rage - 20673 minimum_rage`;
its final cost comparison is **unsigned**, unlike the signed resource checks.
Preparation's insufficient-resource comparisons are also unsigned. Do not
silently replace these boundary operations with a uniform ideal arithmetic model;
negative/overflow controls do not establish legal gameplay inputs.

If any check fails, `0x141e6ca30` returns false before its payment writes.
On success it applies, in order:

```text
Mana.integerDelta(-manaCost, 0, reason=6)
Life.integerDelta(-lifeCost, 0, reason=6)
ES absolute setter through its guarded integer wrapper, reason=6
Life callbacks +170 (Rage requirement), then +b8 (adjusted record and skill handle)
return true
```

The ES block is inlined here. On the installed Life table, `+0x170` binds
`0x141e6cb40`: it reads current/minimum Rage again and returns a **signed**
availability comparison. This method does not deduct Rage, and the payment
caller ignores its return. This is distinct from the unsigned earlier check.
Slot `+0xb8` binds leaf `0x141e6c7c0`, which moves the record/skill arguments and
tail-dispatches `0x141e82b70` with `Life+0x20`. Transitive listeners remain outside
this numerical pass; they are recorded as a fixture in the interpreter.
The integer writes preserve the supplied fractional-carry fields.

For a Mana cost of 40, current Mana 50 and ES 30: ordinary preparation pays
40 Mana. With 5339 active it prepares 10 Mana + 30 ES and pays those amounts.
These are supplied-resource examples, not observations of an ordinary network skill.

## Call eligibility and the C/S boundary

Two positively bound callers prevent an unconditional client-prepayment claim:

| Caller | Gate and payment edge |
|---|---|
| `0x141941a00` | A zero first byte of the supplied owner context returns true without entering payment; nonzero can proceed through its additional preflight to call `0x141e6ca30` at `0x141941b09`. |
| `0x141b79410` | Reads action `+0xe8`; zero first byte there returns true without payment. The nonzero branch checks preparation/affordability, then calls `0x141e6ca30` at `0x141b7952e`. |

The byte is not the general native-formula global, a network-mode enum or an
Actor vtable byte merely because it gates costs. Its producer and applicability
to ordinary network actions are not recovered here. The sacrifice branch does
not contain either of those two caller gates; upstream eligibility still matters.
Existing LocalExile trace notes in [skill Mana](../protocol/11-skills.md#9-gem-skills-stats-and-casting)
remain historical, build-specific observations. This static result neither
generalizes those traces nor contradicts them by asserting ordinary local payment.

The useful server-facing distinction is concrete: cost preparation, affordability,
payment, sacrifice and accepted enemy-hit damage are different operations.
Reusing a pool writer does not give them identical mitigation or event semantics.
The original numerical Hit and DoT publication gaps remain open.

## Evidence and verification limits

Private `r21-integer-pools.py` interprets original decoded instructions using the
R19 integer VM extended for the selected negate/SSE operations. A separately
written scalar specification checks resource results, affordability and the
arrow accumulator. **886 controls / 270,494 interpreted instructions / 614 visited
instructions** pass: 359 arrow-sacrifice, 493 cost-payment, 32 preparation/check
boundary controls and two zero-gate caller controls.
This is same-agent verification, not an independent worker or native execution.

Resolved providers/getters, prepared random draws, reservation totals, selected
maximum helpers, threshold listeners and notification consequences are explicit
fixtures. The full nonzero caller branches and unrelated frenzy/minion branches
are not interpreted. No actual equipment, network-cache refresh, source-instance
publication or ordinary-client payment claim follows from these tests.
The selected concrete constant-return leaves are byte-checked scalar fixtures.

`r21-integer-fresh.py` independently reloads original bytes, checks all twelve
retained bodies, eight concrete table slots, selected literal leaves and 35 semantic
anchors. The private audit retains exact ranges and instruction records;
public provenance is indexed in [the manifest](client-damage-evidence.json).
The separate embedded-field offset scan is navigation only: a matching displacement
does not establish a Life receiver or global resource-writer completeness.

The projectile R36 snapshot is pinned at `dee19667a2bd14a92fa07d024110c4f9732f59de`.
All 56 snapshot files, including 43 exact Git sources, and parent-manifest hashes
were checked. Its repeat backend results are reused without rerunning their
numerical audit; this pass does not duplicate action/RNG/repeat research.
