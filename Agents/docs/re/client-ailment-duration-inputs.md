# Complete damaging-ailment duration modifier inputs (3.29.3b)

## Result and boundary

The three additive and three final duration-modifier callbacks for ignite,
bleeding and poison have complete conditional equations within their supplied
evaluation context. This extends the earlier [duration/faster investigation](client-dot-instance-rules.md#2-current-native-duration-arithmetic-independently-compensates-faster),
which deliberately left the additive conditional sums untranscribed.

Two details matter for a corresponding server implementation: the poison
intelligence test is **at least 150**, despite its `over_150` name; bleeding
divides intelligence by 12 **before** multiplying its per-step modifier.
The poison-specific bonus has an upper cap of 100, with no lower clamp in
this callback. Boolean qualifiers accept any nonzero signed value.

These outputs are **percentage modifiers**, not base durations, remaining
seconds or an amount of damage. Neither registering these equations nor
evaluating them with supplied cache values establishes an actual ailment
instance constructor, target-rate publisher or owner-stat clock refresh.
The broader R2/R3/R4 packages remain Partial.

Original PE SHA256:
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`.
Stats SHA256:
`91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da`.
Keys are one-based native keys into the pinned 23,346-row table.

## 1. Exact registered outputs

Each original registrar passes its output key and callback to `0x142513630`.
The six dependency declarations are retained independently in the
[native dependency investigation](client-stat-dependency-order.md).

| Output key / Stats Id | Registrar | Callback |
|---|---|---|
| 2706 `virtual_ignite_duration_+%` | `0x1400cc730` | `0x1427fac60` |
| 6578 `virtual_ignite_duration_+%_final` | `0x1400cc7c0` | `0x1427fb8d0` |
| 4598 `bleed_duration_+%` | `0x1400cd3f0` | `0x142805120` |
| 9329 `virtual_bleed_duration_+%_final` | `0x1400cd470` | `0x142805d60` |
| 3424 `poison_duration_+%` | `0x1400cd660` | `0x142807490` |
| 6477 `combined_poison_duration_+%_final` | `0x1400cd6e0` | `0x142808060` |

`S(k)` below is a signed32 value read from the evaluation context. All
integer additions/subtractions and two-input products wrap to signed32.
`trunc0` divides toward zero. A condition is evaluated on that signed value;
it is not inferred from a display string.

## 2. Shared conditional additions

| Term | Value and exact condition |
|---|---|
| `Bow` | `S(11182)` when `S(3) == 9`, otherwise 0 |
| `Focus` | `S(8658)` when `S(8611) != 0`, otherwise 0 |
| `Retaliation` | `S(20845)` when `S(4034) != 0`, otherwise 0 |
| `NotRecent(k)` | `S(6190)` when `S(k) == 0`, otherwise 0 |

The bow gate compares the native main-hand weapon-type value to **9** at
`0x1427faf34`, `0x1428053f1` and `0x142807b1c`. The native lookup
`0x14449bdc0 + 9*4` names stat 562, `bow_critical_strike_chance_+%`,
consistent with the independently recovered [weapon classification](client-critical-inputs.md#5-attack-increased-factors-and-weapon-classification).
Do not substitute row indices from the unrelated per-item WeaponTypes table.
These three callbacks do not independently test whether the skill uses the
main hand, whether this is an attack, or whether an off-hand weapon is a bow.
Any upstream context qualification remains an additional responsibility.

| Key | Pinned Stats Id |
|---|---|
| 3 | `main_hand_weapon_type` |
| 11182 | `status_ailments_you_inflict_duration_+%_with_bows` |
| 8658 / 8611 | `status_ailments_you_inflict_duration_+%_while_focused` / `has_focus` |
| 20845 / 4034 | `retaliation_skills_all_ailment_duration_+%` / `skill_is_retaliation_skill` |
| 6190 | `base_all_ailment_duration_+%_if_you_have_not_applied_that_ailment_recently` |
| 11205 / 11207 / 11209 | `have_ignited_an_enemy_recently` / `have_bleed_an_enemy_recently` / `have_poisoned_an_enemy_recently` |

The three recent flags are separate inputs. A callback reads the flag;
this investigation does not establish its producer or duration of recency.

## 3. Complete additive equations

```text
ignite2706 =
    S(6225) - S(2878) + S(12756) + S(6189) + S(205)
  + Retaliation
  + (S(20703) != 0 ? S(20893) : 0)
  + (S(20658) != 0 ? S(20656) : 0)
  + Focus + Bow + NotRecent(11205) + S(2710)

bleed4598 =
    mul32(trunc0(S(578) / 12), S(4231))
  + S(5453) + S(6225) + S(6189)
  + Retaliation + Focus + Bow + NotRecent(11207)

poison3424 =
    mul32(S(7), S(6515)) + S(3423) + min_signed(S(16280), 100)
  + S(6189) + S(6225)
  + Retaliation + Bow + Focus
  + (S(578) >= 150 ? S(6520) : 0)
  + NotRecent(11209)
```

The displayed additive order follows the final accumulator instructions.
These callbacks contain no final zero floor or saturating addition.

| Key | Pinned Stats Id |
|---|---|
| 205 / 2878 | `ignite_duration_+%` / `ignite_duration_-%` |
| 2710 | `base_elemental_status_ailment_duration_+%` |
| 12756 | `fire_ailment_duration_+%` |
| 6189 | `base_all_ailment_duration_+%` |
| 6225 | `skill_effect_and_damaging_ailment_duration_+%` |
| 20656 / 20658 | `ailment_bearer_ignite_freeze_shock_duration_+%` / `ailment_bearer_active` |
| 20893 / 20703 | `elemental_ailment_duration_+%_with_tinctured_weapons` / `tincture_applied` |
| 5453 | `base_bleed_duration_+%` |
| 4231 / 578 | `bleed_duration_per_12_intelligence_+%` / `intelligence` |
| 3423 | `base_poison_duration_+%` |
| 6515 / 7 | `poison_duration_+%_per_power_charge` / `current_power_charges` |
| 6520 | `poison_duration_+%_with_over_150_intelligence` |
| 16280 | `assassin_poison_duration_+%_up_to_100%` |

Ignite's tincture term is guarded only by key 20703 in this callback;
adding an extra weapon/category condition here would change the recovered
equation. The source of that qualifier and other offence callbacks have
their own conditions. The bearer term similarly uses its separate active flag.

### Original arithmetic and branch witnesses

- Ignite's final accumulator is `0x1427fb886..0x1427fb8b4`.
- Bleeding's signed divide-by-12 uses multiplier `0x2aaaaaab`, high-half
  shift and sign correction at `0x142805d18..0x142805d26`; only then does
  `0x142805d28` multiply by key 4231. With modifier 5, intelligence 23
  contributes **5**, not `trunc0(23*5/12) = 9`. Intelligence -23 contributes -5.
- Poison compares intelligence with `0x96` at `0x14280782c` and skips the
  bonus on signed `jl` at `0x142807832`. Inputs 149/150/151 with bonus 31
  contribute **0/31/31**.
- Poison's signed comparison with 100 and `cmovle` at
  `0x142807f40..0x142807f4d` retain a negative value. Inputs -25/99/100/101
  contribute **-25/99/100/100**. This cap is on one duration modifier,
  not damage, total duration, or a target's shared DoT rate.
- Poison multiplies current power charges by the per-charge percentage
  at `0x142808019`; no separate charge cap is applied by this callback.

## 4. Final modifiers: preserve binary32 order

Each floating operation below rounds to binary32; `+32` and `*32` mean
signed32 wrapping arithmetic. Define:

```text
F(n) = max(0.0f, f32(f32(n +32 100) / 100.0f))
R(x) = trunc_to_i32(f32(x + (x < 0 ? -0.5f : +0.5f)))
Reciprocal(faster) = f32(1.0f / f32(1.0f + f32(f32(faster) / 100.0f)))
```

`R` returns the x86 indefinite integer `-2147483648` if the finite rounded
operand is outside signed32 range. There is no upper-saturating helper
here. Nonfinite intermediates and faster exactly -100 are outside this
round's interpreted controls and must not be assigned invented semantics.

Evaluate the following **left to right**, rounding after each multiply:

```text
bleed9329:
  x = F(S(14795)) * 100.0f
  x = x * F(S(18832)) * F(S(22849))
  x = x * Reciprocal(S(9320))
  result = R(f32(x - 100.0f))

poison6477:
  x = F(S(6476)) * 100.0f
  x = x * F(S(10557)) * F(S(14795)) * F(S(18832)) * F(S(22849))
  x = x * Reciprocal(S(9319))
  result = R(f32(x - 100.0f))

ignite6578:
  Q = trunc0(((S(6577) +32 100) *32 (S(13009) +32 100)) / 100)
  x = max(0.0f, f32(f32(Q) / 100.0f)) * 100.0f
  x = x * F(S(14795)) * F(S(15280)) * F(S(18832)) * F(S(22516)) * F(S(22849))
  x = x * Reciprocal(S(6203))
  result = R(f32(x - 100.0f))
```

These are ordered factor summaries, not a replacement of the real
instruction sequence with real-number arithmetic. The previously recovered
[final-factor key names](client-dot-instance-rules.md#2-current-native-duration-arithmetic-independently-compensates-faster)
still apply. Ignite's intermediate integer product/division must precede
its float conversion. Each `F` clamps separately; factors must not first
be combined into one sum or one real-number product.

The original max-selector leaf `0x14013c620..0x14013c62f` is entered by the
offline interpreter in this pass, rather than replaced by a host `max` call.
Its five instructions compare the two pointed-to floats, conditionally
select a pointer and return it. The cache-miss evaluator is not entered:
all input valid bits and signed values are explicitly supplied fixtures.

### A concrete operation-order counterexample

For bleeding, supply only:

```text
S(9320) = -58; S(14795) = 932; S(18832) = 917; S(22849) = 1139
```

The original ordered reciprocal/multiply path and independent ordered
binary32 equation return **309516**. Multiplying the numerator identically
but dividing it by the denominator at the end returns **309515**.
Separate retained ignite and poison witnesses also differ. Their large
synthetic modifiers test arithmetic fidelity, not availability of a game build.

## 5. Reproduction and scope of completion

Ignored local evidence lives under `Tools/re/out/client-damage-audit/`:

```powershell
python Tools/re/out/client-damage-audit/r36-duration-formulas.py
python Tools/re/out/client-damage-audit/r36-duration-fresh.py
```

The first command retains `r36-duration-formulas/audit.json`, `controls.json`
and `verification.json`; the second independently rereads the pinned PE,
Stats names, registrars, prior declarations and original ranges into
`fresh-check.json`. These are offline evidence records, not REA Evidence IDs.

- **2,074 controls**, **298,239 interpreted instructions**, **847 visited sites**:
  six baselines, 972 single-input boundaries, 112 conditional combinations,
  21 explicit integer/gate witnesses, 960 mixed contexts and three float-order witnesses.
- **13 function identities / 13 ranges / 3,689 decoded instructions**:
  six registrars, six callbacks and one exact max-selector leaf; all were
  already present in the manifest. This is zero newly discovered functions.
- Six declarations contain **65 input entries**; the controls collectively
  read exactly those declared inputs for these callbacks. This is not a
  general guarantee for other registered equations or cache-miss paths.
- Fresh checks retain **32 instruction anchors**, four float constants and
  the native weapon-selector entry, with original bytes and source offsets.

No client was launched or attached, no new captures were acquired, and no
server code was changed. [PR #373's evidence boundary](client-damage-pr373-comparison.md)
and the projectile investigation's ownership remain unchanged.

The modifier equations can now be reused without guessing their conditional
sums. Still open are base-duration selection/application, actual infliction,
the source instance to target incoming-rate connection, strongest-instance
selection/aggregation/shared cap, expiry withdrawal and generic existing-Buff
clock refresh. Formula completion does not discharge those separate R2/R4
requirements or the accepted-Hit numerical damage-to-pool requirement.
