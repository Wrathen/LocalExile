# Bleeding policy inputs and native dependency propagation (3.29.3b)

## Result and scope

Four previously unexpanded numerical callbacks recover the effective Crimson
Dance condition, bleeding stack-limit override, moving-damage override and
attack-hit aggravation chance. They compose with the existing bleeding-final
callback through the original dependency queue and native record getter.
This establishes **policy inputs**, not actual bleeding-instance selection.
**R2/R3/R4 remain Partial.**

The evidence is offline interpretation of original PE instructions, with
separately formulated scalar checks. It is not native client execution,
observed traffic or a server implementation. Artifact identities and key
mapping remain those in the [main report](client-damage-calculation.md#artifact-identity-and-reproducibility):
native key `k` maps to `Stats.json[k - 1]`.

## 1. Registration and input identities

All five original registrars execute to the original registration-call
boundary; output key, callback pointer and ordered dependency vector agree
with the retained declaration graph.

| Output key | Original registrar | Original callback | Declared inputs, in order |
|---|---|---|---|
| 7690 `virtual_keystone_crimson_dance` | `0x1400d74b0` | `0x14286cea0` | 7691, 7692, 4537, 7693, 7565 |
| 7694 `virtual_bleeding_stacks_up_to_x_times` | `0x1400d8f70` | `0x14287ac10` | 6428, 7690 |
| 16935 `virtual_bleeding_moving_damage_%_of_base_override` | `0x1400e58b0` | `0x1428d2ea0` | 6180, 1162 |
| 21006 `aggravate_bleeding_on_attack_hit_chance_%` | `0x1400e7fc0` | `0x1428e4280` | 20938, 21007, 16298 |
| 6154 `combined_bleeding_damage_over_time_+%_final` | `0x1400a0610` | `0x1425dc040` | 6156, 6177, 1162, 7690, 6526, 6525, 6529, 9320, 9835, 9461, 9911, 13847, 14754, 19745 |

| Key | Exact Stats Id |
|---|---|
| 7691 | `keystone_crimson_dance` |
| 7692 | `gain_crimson_dance_if_have_dealt_critical_strike_recently` |
| 4537 | `have_crit_recently` |
| 7693 | `gain_crimson_dance_while_you_have_cat_stealth` |
| 7565 | `has_cats_stealth` |
| 6428 | `bleeding_stacks_up_to_x_times` |
| 6180 | `bleeding_moving_damage_%_of_base_override` |
| 1162 | `is_player_minion` |
| 20938 | `base_aggravate_bleeding_on_attack_hit_chance_%` |
| 21007 | `aggravate_bleeding_on_exerted_attack_hit_chance_%` |
| 16298 | `is_exerted` |

## 2. Exact numerical contracts

Here `s[k]` is the signed 32-bit effective input. `wrap32` retains the low
32 bits and interprets them as signed. A flag is true for any nonzero value;
it need not equal one.

```text
C = (s[7691] != 0)
    OR (s[4537] != 0 AND s[7692] != 0)
    OR (s[7565] != 0 AND s[7693] != 0)

out[7690]  = C ? 1 : 0
out[7694]  = C ? 8 : s[6428]
out[16935] = s[1162] != 0 ? 0 : s[6180]
out[21006] = wrap32(s[20938] + (s[16298] != 0 ? s[21007] : 0))
```

The stack callback **replaces** the raw value with eight under Crimson Dance;
it does not take a maximum or add seven. Raw 99 becomes eight. Without the
condition, raw zero stays zero in this callback: it does not introduce the
ordinary one-stack default. Original `0x14287ad1a..0x14287ad23` tests the
effective condition and returns the literal eight.

The movement callback first calls Boolean getter `0x1425135a0` for 1162,
returning zero when it is nonzero. Otherwise it returns 6180 verbatim.
It does not test target movement, introduce a default 300, clamp the raw
override or apply it to a damage amount. The separately recorded
[Monster source declaration](client-effect-source-catalogue.md) of 6180 = 500
is data; this callback alone does not establish its actual target use.

The aggravation callback conditionally adds the exerted bonus to the base
chance. There is no local 0–100 clamp: 60 + 75 produces 135; signed overflow
is retained. Original `0x1428e43b7` and `0x1428e447b` perform the final
32-bit addition on the two getter paths. This is not an aggravation random
trial or evidence that an existing instance was aggravated.

The complete 6154 equation is already retained in the
[bleeding-output report](client-bleeding-output-family.md#62-final-key-6154).
Its effective 7690 input selects a separate 0.5 factor. With all its other
effective inputs fixed to zero in this batch, it returns -50 when C is true
and zero otherwise. That restricted composition does not replace the full
equation or claim a damage amount.

## 3. Dynamic numerical propagation, separate from instance updates

Original rank construction, dirty insertion, ordered callback dispatch and
native previous-value reads execute in the supplied eligible/cache-ready
state. The metadata-function updater is an observational hook; it supplies
no numerical outputs.

| Supplied change | Actual numerical consequence |
|---|---|
| Raw limit 99; conditional-on-recent-crit enabled; recent-crit changes 0 to 1 | 7690 becomes one, 7694 becomes eight, 6154 becomes -50 |
| Raw limit changes 99 to 3 while Crimson Dance remains active | 7694 callback executes but returns eight again; no changed-output notification |
| Recent-crit then changes back to zero | 7694 returns the current raw three, rather than the previous 99; 6154 returns zero |
| Conditional Cat's Stealth input toggles | Both dependent outputs follow the same effective Crimson Dance condition |
| Player-minion condition activates, raw moving override changes, then condition clears | Zero is retained while suppressed; clearing reveals the latest raw override |
| Exerted state toggles with base 60 and bonus 75 | Aggravation output switches between 60 and 135 |

These are changes in native numerical records. They do not prove that a
previously inflicted bleed changes its snapshotted properties, that a target
reselects stacks, or that any corresponding incoming-rate override is written.
External cache values, eligibility and owner binding remain supplied. Queue
cleanup retains the inherited bounded-drain fixture; whole transaction exit
and ordinary actor eligibility are not claimed.

## 4. Consumer boundary and next evidence

Within the already recovered **2,539 registration declarations**, effective
7690 is declared by 6154, 7694 and 8254
`virtual_current_number_of_keystones` (`0x142886820`). There is no declared
consumer of 7694, 16935 or 21006 in that graph.

This reuses a finite declaration inventory, not a whole-program consumer
search. Computed, indirect and inlined reads, omitted declarations and
server-side consumers remain possible. The next useful edge is a concrete
consumer that selects/updates real instances or supplies a target rate;
repeating this declaration inventory cannot establish that edge.

## 5. Verification and reproduction

Private ignored batch: `Tools/re/out/client-damage-audit/r51-bleeding-policy/`.
Its producer is `r51-bleeding-policy.py`; fresh verifier is
`r51-bleeding-fresh.py`. Earlier sealed producer mains are never rerun.

- **224 cases**, five original registrations, **1,023,880 interpreted
  instructions** and **953 original sites**. Cases include all 32 flag
  combinations, noncanonical nonzero flags, extreme signed inputs, values
  above 100 and four dynamic sequences with 14 changes.
- Fresh verification covers **19 function identities / 21 ranges / 3,116
  decoded records**, **29 instruction anchors** and **16 exact Stats names**.
  Every visited instruction maps back to original PE bytes. Saved initial,
  final and dynamic results, changed-output notifications and registration
  vectors agree with separate checks.
- All numerical callbacks and native record getters execute as interpreted
  instructions. The sole post-setup fixture target is the metadata updater;
  inherited allocation/sort/memory and prepared-state fixtures stay explicit.
- Four new manifest identities are numerical callbacks; the other identities
  reuse retained evidence. Counts do not imply four closed gameplay paths.

No bleed instantiation, stack arbitration, moving-target application,
aggravation trial, target-rate publication, shared-cap reachability or
ordinary-network/native-provider eligibility is established by this batch.
