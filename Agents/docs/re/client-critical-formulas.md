# Client critical probability and multiplier formulas (3.29.3b)

## 1. Result and scope

The native stat evaluator has distinct outputs for base critical chance, effective attack critical
probability after extra-roll rules, and critical damage multiplier. The effective-probability callbacks
recover ordinary lucky/unlucky, extreme luck, and unexciting rules, including their branch order and
rounding. The multiplier callbacks recover an unconditional 300% override and a 100% floor.

These are usable calculation contracts for a server implementation. They are not evidence that a
network entity executes these callbacks locally: its received override/fallback provider is a separate
contract, established in [client-stat-evaluation.md](client-stat-evaluation.md). In particular, an
effective probability is a calculated stat, not a record of the random draws or a confirmed critical hit.

This pass uses original executable bytes and selected original decompilations only. No runtime was
started or attached. Preferred-image addresses below belong to the following artifact:

| Input | Identity |
|---|---|
| Executable | `Client/Game.exe`, x86-64, 3.29.3b, preferred base `0x140000000` |
| Executable SHA256 | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `data/tables/Stats.json` SHA256 | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| Mapping | Native key `k` = JSON row `k - 1` |
| Original-byte exports | Private `r5-critical-formulas` and `r5-critical-math-final` |
| Recheck | Private `r5-critical-formulas-verify.py` / `.json` |

The generated `.compact.c` navigation files are defective and were not used to derive these formulas.
Assembly and the original `.c` exports remain the evidence sources.

## 2. Registration and output units

Each registration installs its callback through `0x142513630`. This distinguishes an output's
registration from a nearby input read or an incidental occurrence of its key.

| Native key / output | Registration | Callback | Coverage |
|---|---|---|---|
| 15066 `intermediary_main_hand_critical_strike_chance` | `0x1400bca40` | `0x142754c20` | [Complete input builder](client-critical-inputs.md) |
| 552 `main_hand_critical_strike_chance` | `0x1400bcb60` | `0x142756600` | Override wrapper closed |
| 553 `off_hand_critical_strike_chance` | `0x1400bcbc0` | `0x142756810` | [Complete input builder](client-critical-inputs.md) |
| 554 `spell_critical_strike_chance` | `0x1400bccf0` | `0x142758320` | [Complete input builder](client-critical-inputs.md) |
| 23239 `main_hand_effective_total_chance_permyriad_for_hit_to_be_critical` | `0x1400bcfe0` | `0x14275c050` | Formula closed |
| 23240 `off_hand_effective_total_chance_permyriad_for_hit_to_be_critical` | `0x1400bd050` | `0x14275c6a0` | Formula closed |
| 610 `main_hand_critical_strike_multiplier` | `0x1400be840` | `0x14276b920` | Formula closed |
| 611 `off_hand_critical_strike_multiplier` | `0x1400be900` | `0x14276bb70` | Formula closed |

Chance inputs 552/553 and effective outputs 23239/23240 use **permyriad** units: 2000 means 20%,
10000 means 100%. This is established by the signed integer-to-float conversion followed by division
by 10000 and the explicit always-critical return value. Multipliers use percentage units: 150 means
1.5 times damage. Do not apply the probability denominator to a multiplier.

`S(k)` below denotes the evaluator's effective integer lookup and `F(k)` its boolean predicate
`0x1425135a0`. Local cache, explicit native overrides and upstream fallback are lookup mechanics;
they do not contribute separate additive terms to the formulas.

## 3. Effective attack critical probability: keys 23239 / 23240

### Inputs and precedence

| Key | `Stats.Id` | Role |
|---:|---|---|
| 2868 | `virtual_global_cannot_crit` | First early return: 0 |
| 9545 | `virtual_always_crit` | Second early return: 10000 |
| 4194 | `attack_always_crit` | Third early return: 10000 |
| 552 / 553 | `main_hand_critical_strike_chance` / `off_hand_critical_strike_chance` | Chance input `C` |
| 8613 | `virtual_extra_critical_rolls` | Signed extra-roll count `R` |
| 21262 | `lucky_or_unlucky_effects_are_instead_unexciting` | Predicate `U` |
| 21377 | `extreme_luck_unluck` | Predicate `E` |

The order matters for contradictory inputs: cannot-crit wins over either always-crit input. If those
early returns do not apply, `R == 0` returns **the original integer `C` unchanged**, including when
unexciting or extreme is present. The callbacks do not normalize or round this fast path.

For a nonzero `R`, if `!U && E`, double `R` first. The machine operation is a 32-bit signed-bit-pattern
addition; an overflow to zero takes the same unchanged-`C` return. The ordinary gameplay formulas
below assume the count and its exponent arithmetic remain in the normal signed range.

### Formula

Set `p = float32(C) / 10000.0f` and `q = 1.0f - p`.

| Condition after early returns | Probability `P` |
|---|---|
| `!U`, `R > 0` | `1 - q^(R + 1)` |
| `!U`, `R < 0` | `p^(abs(R) + 1)` |
| `U`, `!E` | `p^3 + 3*p^2*q` |
| `U`, `E` | `p^5 + 5*p^4*q + 10*p^3*q^2` |

Unexciting is the probability of a majority of three trials, or five with extreme. Once `R` is nonzero,
its magnitude and sign do **not** choose the exponent in these unexciting branches. It is therefore
incorrect to model every branch as a generic `abs(R) + 1` roll count.

The transformed probability is multiplied by `10000.0f`, then adjusted by `+0.5f` for nonnegative
values or `-0.5f` for negative values and truncated with `cvttss2si`. For ordinary finite in-range
values this is round-half-away-from-zero. There is no explicit `[0, 10000]` clamp in these callbacks.
Their float operations and `powf` result rounding must be preserved if binary-level parity matters;
the algebraic expressions above do not prescribe a reordered double-precision implementation.
`abs(INT_MIN)`, exponent overflow and non-finite inputs are machine edge cases, not unbounded
mathematical integer operations.

For an input of 20%, the mathematical results illustrate the branches:

| Extra rolls / predicates | Probability before final permyriad rounding |
|---|---:|
| `R = +1` | 36% |
| `R = -1` | 4% |
| `R = +1`, extreme | 48.8% |
| `R = -1`, extreme | 0.8% |
| Nonzero `R`, unexciting | 10.4% |
| Nonzero `R`, unexciting + extreme | 5.792% |
| `R = 0`, any combination of those predicates | 20% unchanged |

### Original-byte anchors

| Operation | Main hand | Off hand |
|---|---|---|
| Cannot-crit branch | `0x14275c15a..0x14275c160` | Corresponding first branch of `0x14275c6a0` |
| Read unexciting / extreme predicates | `0x14275c4e3..0x14275c4fb` | `0x14275cb33..0x14275cb4b` |
| Zero-roll fast path / doubling | `0x14275c503..0x14275c516` | `0x14275cb53..0x14275cb66` |
| Normalize chance | `0x14275c526..0x14275c539` | `0x14275cb76..0x14275cb89` |
| Unexciting branches | `0x14275c54a..0x14275c5ea` | `0x14275cb9a..0x14275cc3a` |
| Lucky / unlucky branches | `0x14275c5ec..0x14275c632` | `0x14275cc3c..0x14275cc81` |
| Multiply, signed half adjustment, truncate | `0x14275c634..0x14275c670` | `0x14275cc84..0x14275ccc0` |
| Unchanged chance / guaranteed critical | `0x14275c690`, `0x14275c694` | `0x14275cce0`, `0x14275cce4` |

These outputs do not establish attack critical confirmation's accuracy check, the number/order of
actual RNG draws, or how a critical result triggers subsequent ailments. Those require their own
event-resolution contracts; they must not be inferred from an expected-probability formula.

## 4. Main-hand override and multiplier

Callback `0x142756600` implements key 552 as:

```text
override = S(15067)  // override_main_hand_critical_strike_chance_%_from_critical_link
return override != 0 ? override : S(15066)
```

The nonzero test and direct return are at `0x14275670a..0x142756710`; the fallback read is at
`0x1427567d3..0x1427567dd`. This wrapper does not rescale or clamp the override. Despite the
input's `%` spelling, use the actual raw-value contract rather than multiplying it again by 100.
Key 553 is a separate, larger input builder, not this same wrapper with its hand swapped.

The multiplier callbacks implement:

```text
if F(9906): return 300  // critical_strike_multiplier_is_300
610 = max(100, S(609) + S(287))
611 = max(100, S(609) + S(288))
```

Key 609 is `base_critical_strike_multiplier`; keys 287/288 are
`main_hand_critical_strike_multiplier_+` / `off_hand_critical_strike_multiplier_+`.
The 300 return precedes the additions and is a replacement, not an extra 300 points. The floor is
100, not an assumed base value of 150. The addition is a 32-bit operation and the comparison is signed.
Main-hand anchors are `0x14276b926..0x14276b937` and `0x14276bb08..0x14276bb21`;
off-hand anchors are the start of `0x14276bb70` and `0x14276bd55..0x14276bd6e`.

## 5. Math helpers: two implementation traps

### The exponentiation call is positively identified as `powf`

`0x1403b4950` is a five-byte jump to `0x142d79cb0`. The latter's ordinary positive-base path
reconstructs `log(base)`, multiplies it by the exponent at `0x142d79dae`, then reconstructs an
exponential and converts back to a scalar float at `0x142d79e3e`. Its original constants include:

| Address | Double value |
|---|---|
| `0x1436b672c` | `ln(2)` |
| `0x1436b6744` | `ln(2)/64` |
| `0x1436b67b0` | `64/ln(2)` |
| `0x142f9c680 + i*8`, `i = 0..63` | `2^(i/64)` lookup table |

Exponent zero returns 1 (`0x142d7a1e0..0x142d7a269`); exponent one returns the base on its ordinary
finite path (`0x142d7a175..0x142d7a1aa`). Exception branches call `0x142d7d074`, whose original
bytes at `0x142d7d19b` load the literal **`powf\0` at `0x1436b9b80`**. This combines the caller's
data flow, the mathematical implementation and a function-specific literal; it is stronger than
guessing from a decompiler's anonymous function name. A CPU-feature branch also selects an
AVX/FMA implementation. A host-language `pow` is not asserted to reproduce all float edge cases
or the exact rounding of both CPU paths.

### `0x1428e73a0` converts an unsigned bit pattern

This helper is called in the tails of intermediary main-hand chance (`0x14275658b`) and spell chance
(`0x142759947`). Its key instructions are:

```text
0x1428e73a4  mov eax, ecx             // zero-extend 32-bit input into RAX
0x1428e73bb  cvtsi2ss xmm0, rax       // signed 64-bit conversion of nonnegative uint32 value
0x1428e73c0  divss xmm0, [100.0f]
0x1428e73ce  call 0x14013c620         // choose max with zero
```

Leaf `[0x14013c620, 0x14013c62f)` compares the zero float with that value and returns a pointer to
the larger operand. For ordinary inputs the result is `float32(uint32(input)) / 100.0f`.
Passing the same bits through a signed **32-bit** conversion changes negative bit patterns into
negative floats and is incorrect. For example, `0xffffffff` first becomes the nonnegative integer
4294967295, not -1. The helper does not justify reinterpreting all percentage stats as unsigned;
the signedness statement is specifically about this call's input conversion.

## 6. Boundaries left for later research

The complete conditional algebra of chance builders 15066, 553 and 554 is now recovered in
[the input-builder supplement](client-critical-inputs.md), including hand-specific overrides,
weapon tables, integer divisions, signed/unsigned percent conversion and final float32 order.
That closes the selected callbacks; it does not recursively close every input-stat producer.

For server work, reusable pieces include those input builders, effective-probability transformation,
main-hand override, multiplier override/floor, units and rounding. Actual random trials, hit
confirmation, concrete application of enemy-side critical modifiers and downstream ailment effects
remain separate work. [Defender modifier composition](client-hit-defender-modifiers.md) recovers
selected target-side stat formulas without claiming their per-event application.
No claim here changes the established network-provider contract or makes the client authoritative
over the server's final resource deduction.
