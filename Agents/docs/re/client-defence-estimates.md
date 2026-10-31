# Client physical-defence estimates (3.29.3b)

## Scope and result

Three native callbacks compute **estimated** physical damage reduction from a
reference damage amount. Their arithmetic includes an armour-style denominator,
a Ward substitution, additional reduction, a maximum and integer rounding.
The reference inputs distinguish these callbacks from mitigation of a concrete
accepted hit. None of these bodies selects an actual incoming damage roll or
deducts Life, Mana or ES.

The executable and Stats identities are the pinned inputs in
[the research workbench](client-damage-calculation.md#artifact-identity-and-reproducibility).
This is original-PE static analysis, using unwind-owned bodies, Capstone 5.0.3
and pefile 2024.8.26. No client/server execution or capture was performed.
Addresses are preferred-image VAs with base `0x140000000`.

## Registration and numerical inputs

Each registration supplies its output key in EDX, callback in R8, and invokes
the previously bound native registry function `0x142513630`.
The misspelling `reduciton` below is the extracted Stats identifier.

| Output key / Id | Registration / registry call | Callback |
|---|---|---|
| 721 `display_estimated_physical_damage_reduciton_%` | `0x1400c1c80` / `0x1400c1ce7` | `0x142788580` |
| 1077 `display_estimated_physical_damage_reduciton_against_projectiles_%` | `0x1400c1d00` / `0x1400c1d61` | `0x142788e50` |
| 7466 `display_estimated_physical_damage_reduction_against_hits_%` | `0x1400c1d70` / `0x1400c1dd1` | `0x1427893d0` |

Let `S[k]` mean the native evaluator's stat/cache input, as in the
[offence formula report](client-offence-estimates.md). The selected inputs are:

| Output | Reference damage D | Effective defensive input A | Additional reduction P |
|---|---|---|---|
| 721 | `S[10744]` for `is_player` 368 or `is_player_minion` 1162; otherwise `S[20928]` | When `S[22580]` is nonzero, twice Ward `S[14544]`; otherwise signed integer `S[243] * S[12056] / 100`, with division toward zero | `S[448]` |
| 1077 | `S[10744]` | When `S[22580]` is nonzero, twice `S[14544]`; otherwise projectile armour `S[1072]` | `S[7464]` |
| 7466 | `S[10744]` | When `S[22580]` is nonzero, twice `S[14544]`; otherwise armour `S[243]` | `S[7464]` |

The exact named input identities are:

- 10744 `character_sheet_reference_damage`; 20928 `monster_display_reference_damage`.
- 243 `physical_damage_reduction_rating`; 1072 `physical_damage_reduction_rating_against_projectiles`.
- 12056 `armour_%_applies_to_physical_damage`.
- 22580 `keystone_voranas_adaption`; 14544 `ward`.
- 448 `additional_physical_damage_reduction_%`; 7464 `additional_physical_damage_reduction_%_against_hits`.
- 12270 `virtual_maximum_physical_damage_reduction_%`; 23057 `no_physical_damage_reduction`.

Both 1077 and 7466 first test 23057 through `0x1425135a0` and return zero
when true. Callback 721 has no corresponding early test in its inspected
body. Its ordinary armour branch also applies 12056, whereas the other two
read their selected armour input directly. These are local formula differences,
not a reason to silently normalize all three estimates into one implementation.

## Arithmetic and original instruction boundaries

For ordinary nonnegative inputs without signed integer overflow, the readable
structure is:

```text
armourPart = 0 if add32(A, D) == 0 else f32(A) / (f32(A) + 5 * f32(D))
fraction   = max(0, min(f32(S[12270]) / 100, f32(P) / 100 + armourPart))
estimate   = roundAwayFromZero(fraction * 100)
```

`add32` preserves the actual guard: the code tests the integer sum **A + D**,
then forms the floating denominator **A + 5D**. Do not replace that test with
a denominator test when reconstructing edge-case arithmetic. The normal-input
expression is explanatory; multiply/add overflow, negative reference values,
non-finite intermediates and out-of-range float-to-int conversion require the
actual instruction sequence rather than this simplified notation.

The three arithmetic tails begin at `0x142788da5`, `0x14278931c` and
`0x14278989b`. They load the binary32 constant 5 from `0x1436a3614` and 100
from `0x1436a3adc`; the min/max order is cap, then zero floor. Positive
rounding adds binary32 0.5 before `cvttss2si`; the negative branch loads -0.5.
These callbacks return integer percentage points, not a damage amount.

For example, the ordinary 7466 branch with armour 10,000, reference damage
1,000, additional reduction 10 and maximum 90 has the readable result
`round(100 * min(0.9, 0.1 + 10000 / 15000)) = 77`.
The number estimates defence against the reference input; it cannot be used
as the mitigation percentage for every actual hit.

## Consumer and coverage limits

One concrete consumer, `0x140293f40`, reads key 721 through the supplied
provider's virtual getter at `0x14029409b`, compares it with 25, and can read
it again at `0x1402940ad` to compare with 1. It stores the resulting category
at output record `+0x10`. This is a value-to-category consumer, not a proved
numerical panel widget or an incoming-hit calculation.

The ordinary network providers retain their explicit-override/current-fallback
contract. Finding these native registrations does not mean a network getter
invokes these callbacks on demand. In particular, this result does not close
the [accepted-contact numerical-damage edge](client-hit-production.md#remaining-closure-criteria).
It also does not resolve interaction of Ward replacement with a separate
per-hit double-armour roll: no such roll is selected in these estimate bodies.

Ignored evidence under `Tools/re/out/client-damage-audit/` consists of
`r3-defence-estimate-formulas/audit.json` and `r3-defence-estimate-verify.py/json`.
The audit retains eight unwind-owned bodies and the bounded twelve literal
`mov edx,key` references for these three outputs. That scan omits computed,
indirect and unwind-less uses; it is not an exhaustive consumer inventory.
The verifier rechecks registry bindings, named Stats rows, arithmetic anchors,
float constants, the concrete category consumer and every retained byte range.
