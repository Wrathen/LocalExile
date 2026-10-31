# Client penetration input equations (3.29.3b)

Twenty-one registered callbacks recover a connected penetration-input chain:
conditional generic elemental contributions, three typed totals, weapon-qualified
inputs, and eight main/off-hand totals. Highest-uncapped-resistance selection,
resonance, critical-only and Unholy Might inputs have separate contracts.
These outputs are integer modifiers, not a concrete hit's resistance or damage.

This supplements [defender modifiers](client-hit-defender-modifiers.md),
[native dependency ordering](client-stat-dependency-order.md) and
[the research workbench](client-damage-calculation.md#8-research-workbench-and-completion-criteria).
R0/R1 remain Established; R2/R3/R4 remain Partial. Actual hit sampling,
critical qualification, resistance selection/ignore/prohibition/penetration
application and pool loss are not closed by these formulas.
No client was launched, attached or executed; no new traffic or server changes
were used. The projectile investigation retains action/contact/World/RNG and
result-ingress ownership; its fixed R43 handoff was reused without repeating
that investigation's numerical audit or sending routine thread messages.
The fixed peer revision is `ede49e0a1e1a32903a1eba4a93375ff5c6d4c9be`: all
64 snapshot files, including 51 exact Git sources and parent hashes, were
checked locally; this does not claim GitHub availability for that revision.

## Identity, registration and notation

The original `Client/Game.exe` SHA256 is
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`.
Preferred image base is `0x140000000`; all addresses below are preferred VAs.
The 23,346-row Stats extract SHA256 is
`91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da`.
Native key `k` maps to zero-based JSON row `k-1`, field `Id`.
All 21 registrations pass the stated key/callback and a recovered dependency
vector to `0x142513630`; merely finding a pointer is not this binding.

| Output key / meaning | Registration | Callback |
|---|---|---|
| 3886 generic elemental | `0x1400bd8c0` | `0x142762810` |
| 13560 resonance contribution | `0x1400bd970` | `0x142763e10` |
| 594 fire total | `0x1400bd9d0` | `0x142764010` |
| 593 cold total | `0x1400bdaa0` | `0x142764d30` |
| 595 lightning total | `0x1400bdb60` | `0x142765590` |
| 4645 fire with weapons | `0x1400d46a0` | `0x142842440` |
| 4644 cold with weapons | `0x1400d4700` | `0x142842810` |
| 4646 lightning with weapons | `0x1400d4760` | `0x142842be0` |
| 14467 / 14470 fire MH / OH | `0x1400d47c0` / `0x1400d4830` | `0x142842fb0` / `0x142843620` |
| 14468 / 14471 cold MH / OH | `0x1400d48a0` / `0x1400d4910` | `0x142843c90` / `0x142844300` |
| 14469 / 14472 lightning MH / OH | `0x1400d4980` / `0x1400d49f0` | `0x142844970` / `0x142844fe0` |
| 14477 / 14478 chaos MH / OH | `0x1400d4a60` / `0x1400d4ad0` | `0x142845650` / `0x142845cc0` |
| 5552 / 5553 / 5554 highest-uncapped fire / cold / lightning | `0x1400d5280` / `0x1400d52e0` / `0x1400d5340` | `0x14284a360` / `0x14284a750` / `0x14284ab40` |
| 9318 critical-only elemental input | `0x1400db2d0` | `0x14288f0f0` |
| 23127 Unholy Might chaos input | `0x1400e6260` | `0x1428d7120` |

`S(k)` is the supplied signed integer stat value in the native context;
`B(k)` means it is nonzero, including negative values. `W` wraps signed
32-bit arithmetic, and `T(a,d)` divides signed integers toward zero.
Every product and sum below wraps where the original integer instructions do.
These callbacks use no floating-point percentage conversion or final 0/100
clamp. Negative/overflow controls verify instruction semantics, not legal
gameplay inputs. The cache reads and generic integer lookup do not themselves
recursively evaluate another callback.

## Generic elemental penetration

Output 3886, `virtual_penetrate_elemental_resistances_%`, is `W(sum)` of:

| Contribution | Qualification / arithmetic |
|---|---|
| `S(3887) + S(599)` | Base penetration plus `reduce_enemy_elemental_resistance_%` |
| `S(1875)` / `S(1876)` | Respectively `B(2376)` skill-is-trapped / `B(2377)` skill-is-mined |
| `S(6268)` | Trapped **or** mined; added once even if both predicates are true |
| `S(3588) + S(6896)` | Each when `!B(4531)` have-killed-recently |
| `W(S(2678) * S(6))` | Per-frenzy-charge coefficient times current Frenzy charges |
| `S(4899)` | `B(3918)` using-flask |
| `S(7662)` | Nonzero current Power charges `S(7)`; no local positive-count comparison |
| `S(13306)` | `B(1842)` is-chilled |
| `S(13560)` | Separately composed resonance output below |
| `W(S(15642) * T(S(15636),15))` | Per-15-Ascendance: divide the Ascendance value **before** multiplying |
| `W(S(18489) * S(7088))` | Per-abyss-jewel coefficient times number of abyss jewels |
| `S(20895)` | `B(20703)` tincture-applied |
| `min_signed(200, W(S(21071) * S(21011)))` | Same tincture gate; per-toxicity coefficient times current toxicity |
| `S(22568)` | `B(14548)` Ward-broken |

The toxicity product wraps before its **own** signed 200 ceiling at
`0x14276371a..0x142763727`. That ceiling does not limit the whole output or
the separate flat tincture term. For flat 5, coefficient 30 and toxicity 8,
the two tincture contributions sum to 205. Negative products have no local
zero floor. The Ascendance division precedes its coefficient at
`0x142763d9a..0x142763db5`; coefficient 7 and Ascendance 29 contribute 7.

Resonance output 13560 returns `S(13442)` when
`S(13434) >= 25`, otherwise zero. Key 13434 is `virtual_lowest_resonance`.
The native comparison at `0x142763f1a` is **at least** 25, not equality,
despite the input modifier's name ending in `while_all_resonance_is_25`.
This does not recover how current resonance is produced or bounded.

## Typed elemental totals

The three global typed outputs have names `reduce_enemy_*_resistance_%`.
Here they calculate source-side integer modifiers; these bodies do not mutate
an enemy's resistance stat. Their complete equations are:

```text
S(594) = W(S(596) + S(3886) + S(5552)
           + (B(4928) || B(5630) ? S(5639) : 0)
           + (B(7277) ? S(7303) : 0)
           + (B(7604) ? S(7608) : 0)
           + (B(21316) ? min_signed(200,S(15294)) : 0)
           + T(W(S(18282)*S(581)),100))
S(593) = W(S(597) + S(3886) + S(5553)
           + (B(1486) ? S(5800) : 0)
           + (B(7278) ? S(7302) : 0)
           + (B(7605) ? S(7609) : 0))
S(595) = W(S(598) + S(3886) + S(5554)
           + (B(7276) ? S(7304) : 0)
           + (B(7606) ? S(7610) : 0)
           + (B(1146) ? S(14828) : 0))
```

596/597/598 are typed base inputs. Recent attack block 4928 and recent spell
block 5630 share **one** fire contribution 5639. Anger/Hatred/Wrath qualify
7303/7302/7304; Herald of Ash/Ice/Thunder qualify 7608/7609/7610.
1486 is `attack_is_melee`; it gates cold input 5800. Low Mana 1146 gates
lightning input 14828. The fire overcap toggle 21316 selects current virtual
overcapped fire resistance 15294 with a signed upper ceiling of 200, not an
extra multiplicative coefficient. This ceiling is separate from toxicity's.

Fire's Dexterity term multiplies coefficient 18282 by Dexterity 581 with
32-bit wrap, **then** divides by 100 at `0x142764cec..0x142764cfb`.
For coefficient 5 and Dexterity 99 it contributes 4; dividing Dexterity first
would incorrectly give zero. These callbacks read effective conditions;
they do not establish when the conditions are granted or refreshed.

### Highest uncapped resistance and ties

5552/5553/5554 each return signed `S(5548)` if their corresponding uncapped
resistance equals the maximum of `S(2094), S(2095), S(2096)`; otherwise zero.
All comparisons are signed. These are the source context's **uncapped** fire,
cold and lightning resistances, not capped defender resistances.

Two equal highest values qualify both types; all three equal values qualify
all three. There is no arbitrary first-element tie break or division of the
modifier among tied types. For `(76,100,100)` and modifier 15, outputs are
`(0,15,15)` even if capped resistances could all display 75.
The fire comparison tree is at `0x14284a62a..0x14284a648`.

## Weapon and main/off-hand composition

The three shared weapon inputs are:

```text
S(4645) = W(S(3961) + S(3974) + (B(20703) ? S(20712) : 0))
S(4644) = W(S(3960) + S(3974) + (B(20703) ? S(20711) : 0))
S(4646) = W(S(3962) + S(3974) + (B(20703) ? S(20713) : 0))
```

3961/3960/3962 are typed weapon contributions, 3974 is elemental with weapons,
and 20712/20711/20713 are typed tinctured-weapon contributions.
The tincture gate is an actual read even though it is undeclared here.

Let `V(type)` be the previously bound 17-byte table at `0x142fdd728`:
indices 13 and 14 are false, all other indices 0–16 are true.
All eight hand callbacks independently address this exact table, also used
by [spell weapon contributors](client-spell-contributor-builders.md).
The original access has no local bounds clamp; negative/out-of-domain indices
are not assigned invented semantics by this report.

For fire/cold/lightning, each hand output is the wrapped sum of six terms:

| Type | MH / OH output | Attack base | Local MH / OH | Weapon input | Global typed |
|---|---|---:|---|---:|---:|
| Fire | 14467 / 14470 | 14473 | 10922 / 10923 | 4645 | 594 |
| Cold | 14468 / 14471 | 14474 | 10925 / 10926 | 4644 | 593 |
| Lightning | 14469 / 14472 | 14475 | 10928 / 10929 | 4646 | 595 |

The terms are attack base, common elemental attack base **14476**, local
typed, local elemental (**4180 MH / 4181 OH**),
`V(S(3 MH / 4 OH)) ? S(weaponInput) : 0`, and the global typed output.
Only that weapon-input term has this table qualification: index 13 does not
zero the whole result. For fire base 2, common base 3, local typed 5, local
elemental 7, global 11 and weapon input 100, index 13 returns **28**, not zero
or 128. Main/off-hand inputs stay separate; the three elemental attack bases
are shared by hands.

Chaos is different:

```text
S(14477) = W(S(14479) + S(8908) + S(600)
             + (V(S(3)) ? S(3963) : 0)
             + (B(20703) ? S(20710) : 0))
S(14478) = W(S(14479) + S(8909) + S(600)
             + (V(S(4)) ? S(3963) : 0)
             + (B(20703) ? S(20710) : 0))
```

Chaos's tincture gate is separate from its weapon-table gate; the original
body can retain the tinctured contribution with index 13/14. There is no
elemental common-base or local-elemental term in these two callbacks.
600 is supplied `reduce_enemy_chaos_resistance_%`; no derived callback for
it is present in the recovered 2,539-registration inventory.

The elemental chain **already includes** generic 3886 and highest-uncapped
5552/5553/5554 through global 594/593/595. Adding them again after a hand
total would double-count them. A ready-cache getter instead uses supplied
594/593/595 as-is; it does not automatically evaluate those upstream formulas.
Numerical dependency composition and actual native transaction eligibility
remain different contracts.

## Event-specific inputs and unresolved application

9318, `virtual_elemental_penetration_%_on_crit`, returns `S(9311)` if
9017 `affected_by_spell_damage_aura` is nonzero, otherwise zero.
It does **not** inspect whether a particular hit is critical.
Likewise 23127 returns signed `S(23128)` when the number of minion abyss
jewels 7087 is at least threshold 23124, otherwise zero. It has no local
Unholy-Might-active test or positive-threshold requirement: zero equals zero
qualifies locally. Its eventual use under Unholy Might requires a separate
consumer binding. These two outputs are not read by the other 19 callbacks.

3885 `non_critical_strikes_penetrate_elemental_resistances_%` is a primitive
input in the recovered registration inventory, not an invented alias for
9318. None of the 21 callbacks applies `cannot_penetrate_or_ignore_elemental_resistances`
12052, defender elemental penetration prohibitions, per-event resistance
ignore, Hexblast's chosen resistance or an actual damage amount.
For example, adding 12052 to a supplied cache does not zero 3886.
That verifies this callback's boundary, not that the prohibition can be
ignored by a complete damage resolver. DoT application is not established
by the presence of these Hit-oriented modifiers either.

## Dependency differences and verification

The declared vectors and actual instruction reads differ:

| Callback outputs | Read keys absent from the declaration |
|---|---|
| 3886 | 6 current Frenzy, 7 current Power, 1842 chilled, 15636 Ascendance, 21011 toxicity |
| 593 | 1486 melee classification |
| 4645 / 4644 / 4646 | 20703 tincture-applied |
| 14477 / 14478 | 20703 tincture-applied |

3886's declaration also repeats 4531; it is retained as declared, not silently
deduplicated. All declared inputs are positively read in the bounded control
set. This neither proves stale live values nor allows treating the registry
as a complete actual-read graph; reuse the separate native dependency limits.

Private `r28-penetration-inputs` retains 42 original-byte bodies / 42 unwind
ranges / 8,881 decoded instructions, with no undecoded bytes. Registration
dependency arguments are reconstructed from original stack/vector writes.
The independent scalar equations are compared with supplied-cache execution
of the original decoded instructions, including single-input and dense signed
controls, all 17 checked weapon indices, resistance rankings/ties, wrapping
products/sums, both division orders, and ordered numerical composition.
12,020 controls / 861,675 interpreted instructions / 1,562 visited sites pass.

`r28-penetration-fresh.py` separately reloads the PE and Stats extract,
rechecks all retained bodies, 21 registration calls, 37 instruction anchors,
the qualification table and its eight exact references. This is same-agent
verification, not an independent worker, native execution or actual-network
evaluation. Ready cache values and generic integer lookup are explicit input
fixtures; cache misses, equipment/source publication, native dirty scheduling
and arbitrary live callback reentrancy are not simulated. The public
[manifest](client-damage-evidence.json) seals provenance, not extracted game
files or disassembly. The unresolved actual Hit and DoT publication edges
remain visible in the main workbench.
