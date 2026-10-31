# Fire self-damage and incoming ignite rate composition (3.29.3b)

## Result and scope

Native output **8180**, `fire_damage_taken_per_minute_unaffected_by_level_scaling`,
combines several numerical inputs before the separately recovered
[typed incoming mitigation and resource routing](client-dot-routing.md).
This supplement completes that callback's formula, including its mixed signed64
and float32 arithmetic. It extends the earlier base-rate summary; it does not
recover a new Buff application, source-instance publisher or per-frame damage event.

The exact executable is `Client/Game.exe`, preferred image base `0x140000000`, SHA256
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`.
`Stats.json` SHA256 is
`91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da`;
native keys are one-based JSON row indices.
Registration `0x1400ac5e0` binds 8180 to callback `0x1426ccf50`.
All observations and checks are static/offline. No client execution, attachment,
new capture, server implementation or archive write was performed.

## 1. Inputs and units

`S[k]` means the signed32 input read from this native computation context,
through its cache/provider contract. It is not an instruction to reevaluate
every ordinary network actor when an input changes. Explicit network overrides
and native formula transactions retain their [separate contracts](client-stat-evaluation.md).

| Keys | Verified `Stats.Id` / role |
|---|---|
| 247 / 249 | `maximum_life` / `maximum_energy_shield` |
| 2581 | `base_fire_damage_%_of_maximum_life_plus_maximum_ES_taken_per_minute` |
| 10270 | `base_fire_damage_%_of_maximum_life_taken_per_minute` |
| 6972 / 6973 | `has_her_embrace` / `%_maximum_es_and_life_taken_as_fire_damage_per_minute_per_level_while_in_her_embrace` |
| 1 | `level` |
| 16711 / 16715 | `affected_by_touch_of_fire` / `fire_damage_taken_per_second_while_flame_touched` |
| 13492 / 8215 | `take_chaos_damage_from_ignite_instead` / `base_ignite_damage_taken_per_minute` |
| 13574 | `base_fire_damage_taken_per_minute_unaffected_by_level_scaling`, **read as a Boolean** |
| 4533 / 7406 / 5 | `number_of_times_have_been_hit_recently` / `fire_damage_taken_per_minute_per_endurance_charge_if_you_have_been_hit_recently` / `current_endurance_charges` |
| 4592 | `current_difficulty` |
| 4698 / 4699 | Normal-difficulty maximum-Life / maximum-ES fire percentages per minute |
| 4700 / 4701 | Cruel-difficulty equivalents |
| 4702 / 4703 | Merciless-difficulty equivalents |

The last six exact Ids are `base_fire_damage_%_of_maximum_life_taken_per_minute_in_`
or `base_fire_damage_%_of_maximum_es_taken_per_minute_in_`, followed by
`normal`, `cruel` or `merciless` respectively.
The retained names identify inputs; they do not prove that each legacy difficulty
or arbitrary input combination is reachable in the current game.

## 2. Complete composition

Use `W64` for signed64 wrap, `f` for binary32 conversion, and `trunc0` for
truncation toward zero. Each displayed integer sum/product is signed64 with
wrap at its native operation. `Q(a,b) = trunc0(W64(a*b)/100)`.
Pool and coefficient inputs are sign-extended from signed32. Percentage
terms are not clamped nonnegative in this callback.

```text
L = S[247]
E = S[249]
P = W64(L + E)
n = W64(Q(P, S[2581]) + Q(L, S[10270]))

if S[6972] != 0:
    coefficient = f(f(S[6973]) / 100.0f)
    embrace = f(f(f(P) * coefficient) * f(S[1]))
    n = W64(n + trunc64(embrace))

if S[16711] != 0:
    n = W64(n + W64(S[16715] * 60))

if S[13492] == 0:
    n = W64(n + S[8215])

n = W64(n + (S[13574] != 0 ? 1 : 0))

if S[4533] != 0:
    n = W64(n + W64(S[7406] * S[5]))

if S[4592] == 2:      (lifePct, esPct) = (S[4700], S[4701])
else if S[4592] == 3: (lifePct, esPct) = (S[4702], S[4703])
else:                 (lifePct, esPct) = (S[4698], S[4699])

n = W64(n + Q(L, lifePct))
n = W64(n + Q(E, esPct))
result = C32(f(n))
```

`trunc64` is the native `CVTTSS2SI r64` conversion; an out-of-range conversion
produces signed64 minimum. `C32` first returns `INT_MAX` when the binary32 input
is at least 2,147,483,648; otherwise it adds binary32 signed 0.5 and executes
`CVTTSS2SI r32`. A negative conversion overflow produces `INT_MIN`.
There is **no final nonnegative clamp** here. These are finite-input/native
overflow semantics, not a claim that extreme synthetic states occur during play.

## 3. Consequences and original-instruction anchors

The ordinary percentage terms divide separately. With Life 123, ES 456,
2581 = 1 and 10270 = 1, their contribution is `trunc0(579/100) + trunc0(123/100)`
= **6**, not 7 from merging the fractions before division. The corresponding
negative inputs produce -6. The Her Embrace branch instead uses float32
multiplication and truncates that branch to signed64 before accumulation.

Touch of Fire explicitly converts a per-second input to per-minute with a
factor of 60. The recently-hit value is only a gate: it does not multiply
the charge contribution by the number of hits. Difficulty values other than
2 or 3 select the normal pair, including 0 and unknown synthetic values.

The numeric-looking 13574 input contributes **one for any nonzero value**,
including negative values. Retain this observed behavior rather than silently
correcting it from its name. Ignite input 8215 is included only when 13492
is zero; the separate chaos callback supplies the complementary path.
Neither branch establishes how actual ignite instances produced 8215.

| Original location | Operation |
|---|---|
| `0x1426cd2fd..0x1426cd327` | Signed64 Life+ES sum, percentage product and signed division by 100 |
| `0x1426cd43a..0x1426cd45c` | Independent Life percentage truncation and addition |
| `0x1426cd71a..0x1426cd73b` | Float32 Her Embrace multiplication, signed64 truncation, addition |
| `0x1426cd90a..0x1426cd911` | Sign extension and multiplication by 60 |
| `0x1426cd914..0x1426cda25` | Ignite-transfer gate and Boolean 13574 addition |
| `0x1426cda65..0x1426cdc27` | Recently-hit gate and charge-dependent contribution |
| `0x1426cdc27..0x1426ce1cb` | Difficulty selection and separate Life/ES percentage terms |
| `0x1426ce1ce..0x1426ce21c` | One signed64-to-float32 conversion, upper saturation and signed rounding |

## 4. Evidence and remaining boundary

Ignored private batch `Tools/re/out/client-damage-audit/r10-fire-self-rate`
retains the original-byte records, named input mapping and synthetic cases.
`r10-fire-self-verify.py` checks three function records / seven ranges /
1,230 decoded instructions, including the registrar and Boolean helper.
The original-instruction ready-cache interpreter matches the separately written
equation for **2,282 cases / 455,393 interpreted steps**, covering each input,
all difficulty branches, conditional terms, signed/extreme inputs and conversion
boundaries. This is offline instruction interpretation, not target execution or
an implementation of cache invalidation, infliction or frame scheduling.
An independent retained review also compares all 1,230 original instructions,
nine explicit byte anchors and three constants, with 17 fixed expected-value
and branch-read cases separate from the equation oracle.

This gives a complete local formula for this specific incoming-rate output.
Actual source-instance ownership, publication of its primitive inputs and native
transaction activation on a particular network target remain unresolved.
The downstream mitigation, Life/ES/Mana routing and fractional-carry consumer
retain their own contracts. No universal final cap, minute-reset allowance or
independent self-damage budget follows from this callback.
