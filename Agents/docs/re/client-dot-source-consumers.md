# Actual Buff resource forecasts and DoT source selection (3.29.3b)

## Result and scope

Two numerical resource-forecast consumers read actual Buff granted values and
remaining duration. A separate Life callback selects the **first eligible Buff
in stored order** and reads its source identifier. This extends the evidence
beyond Buff storage, clocks and implementation registration.

These are distinct contracts. The forecasts feed resource-indicator state;
the source selector supplies callback context. Neither closes the missing
source-instance-to-target **incoming damage-rate publication**, DoT stacking
selection, effective-rate mitigation or shared rate cap. In particular, this
selector does not select the highest-DPS ignite.

All evidence is static/offline original bytes, with preferred image base
`0x140000000`. No client execution, attachment, capture, server or CI operation
was performed. Native table keys below are JSON row indices plus one.

| Artifact | SHA256 |
|---|---|
| `Client/Game.exe` | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `Stats.json` | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| `BuffDefinitions.json` | `3a23c6fdf7ba29fb8508e5c9560a44e5292345e45dd2f886ef7126b8427fd659` |

## 1. Positively bound actual-instance consumers

The [Life/Buffs receiver audit](client-buff-clock-producers.md) establishes Life
configuration `+0x160`, Stats `+0x168` and retained actual Buffs `+0x170`.
The [initial granted-value audit](client-buff-initial-granted-values.md) binds
actual Buff `+0x130` to the decoded granted-value vector. Its first signed
32-bit value is the numerical input below; it is not the definition's
GrantedStats key or a rate already read from target Stats.

Both forecast functions load pool owner `+8`, then retained Buffs `+0x170`,
then its actual-instance vector `+0x160..+0x168`. They skip removal bit
`actual Buff +0xd0 & 0x10`. For matching definitions they convert the first
granted value to binary32, multiply by actual remaining duration `+0x1c`,
and accumulate in vector order. They do not read instance speed `+0x20`.
They also do not independently require positive remaining duration: zero or
negative duration can contribute while the removal bit remains clear.

### Recovery forecast: `0x141e6b9c0`

Pool key `+0x1c == 1422` (`life_recovery_per_minute`) selects configuration
vector `+0xf8`; every other pool key selects `+0x110`
(`0x141e6b9cb..0x141e6ba04`). Configuration resolver `0x141e66ec0`
explicitly appends the following native Buff definition keys:

| Configuration vector | Native keys and definitions | Insertion instructions |
|---|---|---|
| `+0xf8` | 6 `flask_effect_life`, 40 `life_leech` | `0x141e66f7b..0x141e66ff1` |
| `+0x110` | 10 `flask_effect_mana`, 39 `mana_leech` | `0x141e66ffd..0x141e67063` |
| `+0x128` | 41 `energy_shield_leech` | `0x141e67068..0x141e67097` |

The third vector exists but is not selected by this forecast's two-way branch.
Do not silently generalize that branch into an ES-leech forecast.

For definition key 6, either Stats Boolean native key 6981
`virtual_keystone_vaal_pact` or 20799 `no_life_recovery_from_flasks` skips that
instance (`0x141e6ba0b..0x141e6ba48`, `0x141e6bac0..0x141e6bac8`).
Other allowed definitions still contribute. The numerical reads and accumulation
are `0x141e6bace..0x141e6bae1`.

### Deferred Life-loss forecast: `0x141e6bb80`

Only pool key 1422 enters the instance scan. It includes native Buff key
**1797**, JSON row 1796, `slow_life_loss_from_hit_damage`
(`0x141e6bb90..0x141e6bc16`). That definition has `IsRecovery=true` and
GrantedStats `[17063]`, which binds native Stats key **17064**
`life_degeneration_per_minute_not_in_grace`.

This is a concrete damage-valued Buff numerical consumer. It computes a
remaining deferred-loss forecast, rather than publishing a rate or integrating
the actual loss. The definition flag's name does not turn the loss into healing.

### Equations, units and original edge behavior

Let `g_i` be the first signed granted value and `t_i` remaining duration for
the eligible, unremoved instances. For finite inputs in the signed conversion
range, the original binary32 order is:

```text
S = 0
for each eligible instance in stored order:
    S = f32(S + f32(f32(g_i) * t_i))
Q = f32(S / 60) if S > 0.0010000000474974513 else S
U = max(0, signed32(pool.maximum - reservationHelper(pool)))

recoveryForecast = min(U, trunc(f32(f32(f32(pool.current) + pool.carry) + Q)))
deferredForecast = min(U, max(0, trunc(Q)))
```

The threshold bytes are `6f12833a` at `0x1436a2bbc`; the binary32 divisor is
60 at `0x1436a39b8`. The division is a per-minute-value-to-remaining-amount
conversion on this forecast path, **not a one-minute accumulation window or
a recovered DoT-cap implementation**. `reservationHelper` is `0x1401f1f90`,
already bound by the [resource-boundary audit](client-hit-resource-boundary.md).
The maximum subtraction uses a sign test after the 32-bit subtraction;
overflowing/nonfinite artificial inputs are outside the equations' ordinary
finite range.

The recovery path has an upper bound but no lower clamp. As an explicit offline
fixture, current 100, carry 0, maximum 1000, reserved 0, a life-flask first
value 600 and remaining duration -2 returns **-1100**: the negative sum does
not take the divide-by-60 branch. This is original instruction behavior under
that fixture, not evidence that such a state occurs during normal play. The
deferred-loss path clamps the corresponding negative forecast to zero.

## 2. Forecasts feed a resource indicator

Verified direct callsites in the retained slice are:

| Caller owner | Deferred-loss forecast | Recovery forecast | Common consumer |
|---|---|---|---|
| `0x140232840` | `0x140232b15` | `0x140232b23` | `0x140232ba8 → 0x140235390` |
| `0x140235100` | `0x140235296` | `0x1402352a4` | `0x14023532b → 0x140235390` |

`0x140232840` checks literal `Life` at `0x1435a7654` and obtains the component
through `0x14013dda0` at `0x1402328ce`. Its mode byte `+0x298` selects Life
`+0x178`, Mana `+0x1c8`, or ES `+0x210` for modes 0, 1 and 2, respectively;
it stores the chosen pool at caller `+0x2b8`.

The caller passes the two forecast results together with that pool's current,
carry, maximum and other resource fields to `0x140235390`. The latter computes
normalized/bounded indicator state at caller offsets `+0x3d4..+0x3f8`.
This positively bounds the consumer category to resource-indicator preparation.
Rendering was not expanded. These calls do not show authoritative damage
deduction or establish that forecast values become target incoming-rate stats.

## 3. Source selection is a separate Life-callback operation

Selector `0x141e692e0` constructs nine Boolean inputs from the actual Life
owner's Stats and current ES, then walks retained actual Buffs in stored order
(`0x141e69459..0x141e69493`). It skips removal bit `+0xd0 & 0x10` and
stops at the first instance accepted by predicate `0x141e69530`. It neither
reads the numerical vector `+0x130` nor tests remaining duration or speed.

| Symbol | Exact input | Producer test |
|---|---|---|
| A | native 1208 `chaos_damage_does_not_bypass_energy_shield == 0` | `0x141e693a2..0x141e693b2` |
| E | integer ES current `Life+0x240 <= 0` | `0x141e69305..0x141e6930c` |
| C | native 311 `chaos_damage_resistance_% >= 100` | `0x141e69300..0x141e69328` |
| K | native 308 `cold_damage_resistance_% >= 100` | `0x141e69338..0x141e69360` |
| F | native 309 `fire_damage_resistance_% >= 100` | `0x141e6931c..0x141e69344` |
| B | native 7951 `bleeding_damage_on_self_converted_to_chaos != 0` | `0x141e693a6..0x141e693cd` |
| I | native 13492 `take_chaos_damage_from_ignite_instead != 0` | `0x141e693c1..0x141e693eb` |
| P | native 719 `physical_immunity != 0` | `0x141e69370..0x141e69397` |
| L | native 310 `lightning_damage_resistance_% >= 100` | `0x141e69354..0x141e6937c` |

Every accepted instance must first satisfy the raw-field qualification:
`(dword[Buff+0x30] == 0 OR (byte[Buff+0x38] & 1) != 0)` and
`byte[Buff+0x118] == 2` (`0x141e69540..0x141e69553`). The
[input-provenance report](client-buff-clock-input-provenance.md) binds the
transported `+0x30/+0x38` fields; their gameplay meaning is not renamed here.

After that qualification, any matching row below accepts the instance:

| Definition-key special case | Required condition |
|---|---|
| 616 `blight`, 814 `new_blight` | `(A OR E) AND !C` |
| 1118 `cold_snap_degen`, 1119 `vaal_cold_snap_degen` | `!K` |
| 763 `fire_beam_damage_stacks` | `E AND !F` |

Otherwise the predicate enumerates the **definition's** GrantedStats, count at
row `+0xdc`, data at `+0xe4`, stride 16, resolving native keys through
`0x142290090` (`0x141e69650..0x141e6977c`). These are key qualifications,
not the actual Buff's numerical magnitudes:

| Native GrantedStats key and name | Acceptance condition |
|---|---|
| 455 `base_chaos_damage_%_of_maximum_life_taken_per_minute`; 456 `base_chaos_damage_taken_per_minute`; 2006 `base_chaos_damage_taken_per_minute_per_viper_strike_orb`; 8216 `base_poison_damage_taken_per_minute` | `(A OR E) AND !C` |
| 7955 `bleeding_damage_taken_per_minute` | `((A OR E) AND !C AND B) OR (E AND !P AND !B)` |
| 8215 `base_ignite_damage_taken_per_minute` | `((A OR E) AND !C AND I) OR (E AND !F)` |
| 2023 `base_fire_damage_taken_per_minute`; 10270 `base_fire_damage_%_of_maximum_life_taken_per_minute`; 2581 `base_fire_damage_%_of_maximum_life_plus_maximum_ES_taken_per_minute` | `E AND !F` |
| 356 `base_physical_damage_taken_per_minute`; 3903 `base_physical_damage_taken_per_minute_per_talisman_degen_stack`; 13573 `base_physical_damage_taken_per_minute_unaffected_by_level_scaling` | `E AND !P` |
| 2025 `base_cold_damage_taken_per_minute` | `E AND !K` |
| 2027 `base_lightning_damage_taken_per_minute` | `E AND !L` |

These exact alternatives should not be promoted to a general mitigation or
damage-conversion algorithm. For example, the Cold Snap special case lacks the
general elemental-key ES gate. This function determines candidate source
context under its own rules; it does not calculate effective damage.

### Actual callers and selected source context

Life primary table `0x1435a7340` binds `+0x120 → 0x141e684c0` and
`+0x128 → 0x141e68f70`. The nonpositive transition `0x141e6c710` invokes
the former at `0x141e6c736`; the positive numerical loss-to-transition route
is already bound in [the Life transition report](client-buff-clock-producers.md).
Initialization `0x141e67b40` also invokes `+0x120` with source argument zero
when current Life is nonpositive.

When their incoming EDX source argument is zero, the two callbacks invoke the
selector at `0x141e6851e` and `0x141e68fc2`. On a nonnull result they read
actual Buff `+0x28` at `0x141e6852c` / `0x141e68fd0`, plus additional
32-bit callback context at `+0x70/+0x74`. The
[input-provenance report](client-buff-clock-input-provenance.md) establishes
the separate 32-bit field's transport to `+0x28`; this consumer supplies its
candidate-source use. The first callback compares it with owner Entity `+0x88`
and retains a possible replacement before lookup; the selected source value is
passed to `0x141ebefd0` at `0x141e6859a` / `0x141e68fff`. Transport alone
does not establish that semantic role. The extra two fields' gameplay names
remain unassigned.

This gives a concrete use for the first eligible DoT Buff: source context for
a Life callback when no explicit source was supplied. The callbacks contain
further source-resolution logic; this finding does not make their initial Buff
choice a final kill-attribution rule or a numerical stacking selector.

## 4. Verification and remaining boundary

The retained private audit `r17-dot-source-consumers` checks the original PE
identity, 12 function owners, 29 unwind ranges and 2,674 decoded instruction
records. An original-instruction interpreter checks 7,720 predicate controls,
512 flag-construction controls, eight stored-order/removal/duration controls,
and 20 forecast controls. The input controls distinguish 99/100 resistance,
positive/nonpositive ES, bypass/conversion/immunity flags, special definition
keys, reversed numerical magnitudes, removed instances, and nonpositive
remaining duration. The source-selector run stops before reference/link
cleanup; row-key, Stats and reservation calls use declared fixtures. These
checks are offline simulations, not client-runtime observations.

Independent fresh-PE review retains the same 12 bodies and table joins, with
797 additional forecast controls, 7,720 predicate controls, 194 complete
selector-return controls and 1,536 flag-to-complete-selector controls. Forecast
checks preserve the -1100 fixture, vector-order binary32 accumulation, removal
bits, signed conversion and each side of the threshold; they assert whether
the actual division instruction executes, even when truncation produces the
same integer. Complete selector controls use an explicit retained-reference
fixture and empty callback storage; external fixtures clobber volatile GPRs
and XMM0–5. They do not simulate the full Buff subscription lifecycle.

The original missing numerical publication remains open: no path here writes
actual ignite/poison/Righteous Fire magnitudes into target incoming-rate Stats,
proves strongest-instance selection, or applies a target-wide cap. Future
research should follow a positively identified numerical publisher, retaining
these forecast and source-context contracts as separate supporting evidence.
