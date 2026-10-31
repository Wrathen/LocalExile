# Client physical and elemental reduction inputs (3.29.3b)

Three native callbacks supply additional physical reduction, additional elemental
reduction and the physical-reduction ceiling. Their complete input equations are
recovered here, including conditional contributions, independent small ceilings,
integer overflow and seven reads absent from the physical callback's declared
dependency vector. These are input calculations; they do not sample a hit,
apply armour to an actual amount, or publish a DoT instance's rate.

This extends [typed DoT mitigation](client-dot-routing.md),
[reference-damage defence estimates](client-defence-estimates.md) and
[taken-as/armour applicability](client-defence-transfer-formulas.md).
The original executable SHA256 is
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`.
Stats SHA256 is
`91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da`;
key `k` means JSON row `k - 1`. Addresses are preferred-image VAs, base
`0x140000000`. All findings are offline original-byte observations, not client
execution or official-server numerical parity.

## Registration and arithmetic

| Output key / Id | Registration | Callback | Declared inputs |
|---|---|---|---:|
| 448 `additional_physical_damage_reduction_%` | `0x1400bb230` | `0x14274b370` | 59 |
| 20993 `additional_elemental_damage_reduction_%` | `0x1400bb520` | `0x14274d840` | 6 |
| 12270 `virtual_maximum_physical_damage_reduction_%` | `0x1400df960` | `0x1428b0c50` | 2 |

Each registration passes the exact callback in R8, output key in EDX and
dependency-vector descriptor in R9 to `0x142513630`. Dependencies were decoded
from the actual registration instructions and their RIP-relative constants.

Below, `S[k]` is a supplied signed32 native-context value; `B[k]` means nonzero.
Every ordinary addition and multiplication wraps to signed32. `min` and `max`
use signed comparisons after that wrapping. `trunc(a/b)` divides toward zero.
The equations do not silently widen arithmetic, saturate intermediate products
or add an unobserved clamp. Nonzero flags, including negative values, select
their true branch.

## Additional physical reduction: key 448

`B[23057]` (`no_physical_damage_reduction`) returns zero immediately at
`0x14274b386..0x14274b394`, before reading the remaining contributions or cap.
Otherwise the callback adds the following terms and returns
`min(signed32(sum), S[12270])`. It has **no local lower-zero clamp**.

| Contribution | Exact term |
|---|---|
| Base and common/minion reduction | `S[1038] + S[4481]` |
| Endurance charges | `S[5] * (S[449] + (!B[1162] ? S[16932] : 0))` |
| Frenzy / power charges | `S[6] * S[8370] + S[7] * S[8371]` |
| Active zombies | `S[3529] * S[3627]` |
| Summoned sentinels | `S[8448] * S[11179]` |
| Active minions | `min(S[10378] * S[3460], 10)` |
| Recent hits taken | `min(S[5963] * S[4533], 5)` |
| Nearby enemies | `S[6286] * min(S[6305], 8)` |
| Keystones | `S[8255] * S[8254]` |
| Crab Aspect stacks | `S[7537] * S[7535]` |
| Movement state | `B[20408] ? S[7142] : S[5287]` |
| Strength of Blood | `B[9834] ? trunc(trunc(signed32(200 * S[9347]) / S[247]) / 180) : 0` |

The remaining conditional additions are independent:

| Condition | Added key / contribution |
|---|---|
| `B[7273]`, affected by Determination | 7286 |
| `S[3461] > 0`, positive active-totem count | 3647 |
| `B[7751]`, virtual low-Life state | 959 |
| `!B[368]`, not a player | 6531 |
| `S[6305] == 1`, exactly one nearby enemy | 6279 |
| `B[3918]`, using a flask | 4900 |
| `B[11168] OR B[10144]`, Life or Mana flask | 11152, added once |
| `!B[6885]`, not damaged by a hit recently | 6920 |
| `B[8611]`, Focus | 8615 |
| `B[5455]`, channelling | 9002 |
| `B[8555]`, affected by Herald of Purity | 9297 |
| `B[2083]`, bleeding | 9675 |
| `S[9761] >= 150`, Devotion threshold | 9860 |
| `B[9991]`, affected by a guard skill | 9978 |
| `B[10575]`, warcry in the past eight seconds | 11134 |
| `B[1841]`, frozen | 11733 |
| `B[14917]`, in the Hellscape timeline | 15449 |

The conditional contributions are read into distinct stack slots before final
summation. Pure additions may be regrouped modulo 32 bits; the ceilings,
divisions and conditional selections must retain their stated positions.

Selected exact anchors:

- `0x14274b3d5..0x14274b4cd`: player-minion qualification and endurance coefficient.
- `0x14274b89a..0x14274b8b5`: moving selects 7142; otherwise 5287.
- `0x14274ba88`: the one-enemy condition compares with exactly 1.
- `0x14274bc7b..0x14274bca3`: Life/Mana flask OR contributes once.
- `0x14274c25c..0x14274c277`: signed Devotion comparison against 150.
- `0x14274c65a..0x14274c69a`: Strength of Blood's multiply, signed division,
  then signed division-by-180 sequence.
- `0x14274c95f..0x14274c97a`: nearby-enemy count capped at 8 before its product.
- `0x14274cbeb..0x14274cc16`: minion-product ceiling 10 and recent-hit-product ceiling 5.
- `0x14274cc16..0x14274cce2`: wrapping products and sum.
- `0x14274cdc6..0x14274cdd3`: final signed minimum with 12270.

For Strength of Blood, 9347 is the intermediary **per-minute** Life recovery
from leech and 247 is maximum Life. With 12,000/minute and 1,000 maximum Life,
the term is `trunc(trunc(2,400,000 / 1,000) / 180) = 13` percentage points.
There is an actual `idiv` at `0x14274c683`: this body supplies no zero-denominator
or quotient-overflow fallback. That native arithmetic boundary is preserved;
normal maximum-Life validity belongs to the upstream context.

The small ceilings are upper-only signed comparisons. A wrapped negative
minion product is not replaced with 10 or zero. Similarly, the nearby-enemy
term retains a negative supplied count; the callback does not normalize all
inputs to gameplay-valid nonnegative values. These observations describe the
calculator, not a claim that such inputs occur in normal play.

## Elemental reduction: key 20993

```text
halfChaos = B[17461] ? trunc(S[311] / 2) : 0
perCharge = signed32(S[20991] + (B[1162] ? S[20992] : 0))
result = max(0, signed32(halfChaos + signed32(S[5] * perCharge)))
```

17461 is `additional_elemental_damage_reduction_as_half_of_chaos_resistance`.
Its **presence** enables the half term; its magnitude is not a multiplier.
311 is `chaos_damage_resistance_%`, not final DoT-chaos-resistance key 5955.
20991 is the ordinary endurance coefficient; 20992 adds the player-minion
coefficient. Physical reduction's 16932 instead qualifies **non**-player-minions.

The half term is enabled at `0x14274d94f..0x14274d951`; the ready-cache path
`0x14274d963..0x14274d970` truncates signed odd inputs toward zero. Minion
qualification is at `0x14274da4e..0x14274da55`; the final coefficient addition,
charge product, sum and sign-only lower clamp are at
`0x14274dc1f`, `0x14274dce6`, `0x14274dce9`, `0x14274dcf1..0x14274dcf3`.
There is no local upper cap and no use of physical cap 12270.

For example, chaos resistance 75, flag 17461 enabled, three endurance charges,
ordinary coefficient 4 and qualifying minion coefficient 1 give
`37 + 3 * (4 + 1) = 52`. Chaos resistance -1 contributes zero after truncation.
The final lower clamp applies after combining contributions, not separately to
the half-chaos term.

## Physical ceiling and downstream boundaries

Key 12270 returns 50 for any nonzero 12269 `keystone_sacred_bastion`, otherwise
forwards 116 `maximum_physical_damage_reduction_%` unchanged.
`0x1428b0c5e..0x1428b0c71` supplies the fixed branch. The ordinary path has
no local clamp to 90, zero or another default.

The [existing mitigation audit](client-dot-routing.md#typed-mitigation) binds
physical DoT's local `max(0, S[448])` before its remaining-damage factor.
Its elemental counterpart consumes `1 - S[20993]/100`. The separate
[physical-defence estimate](client-defence-estimates.md) combines supplied
additional reduction with an armour fraction and cap 12270 for reference
damage. These consumers must retain their separate clamps and scopes.
This report does not turn the reference estimate into an actual-hit resolver.

## Dependency-vector difference and verification

The physical callback has 59 declared dependencies but reads 66 distinct keys
across the verified ready-cache paths. The seven undeclared keys are:

| Key | Id |
|---:|---|
| 5 / 6 / 7 | current endurance / frenzy / power charges |
| 2083 | `is_bleeding` |
| 6305 | `number_of_nearby_enemies` |
| 7751 | `virtual_on_low_life` |
| 20408 | `virtual_is_moving` |

All declared inputs were reached by controls. Elemental reduction's six inputs
and the physical ceiling's two inputs match their declared sets. This is a
registration-versus-read fact; it does not alone prove stale client values,
because transitive invalidation, context lifetime and actual evaluation remain
separate. A server reconstruction should preserve the numerical inputs without
assuming this vector is an exhaustive dependency specification.

Private reproduction records are under ignored
`Tools/re/out/client-damage-audit/r18-reduction-inputs/`: six complete function
records, exact ranges/instructions, registration dependencies and
`verification.json`. `r18-reduction-prepare.py`, `r18-reduction-dependencies.py`,
`r18-reduction-interpreter.py` and `r18-reduction-verify.py` reproduce extraction
and comparisons. Controls
interpret original instructions with ready-cache/generic-getter fixtures and
compare a separately expressed scalar equation: 2,027 physical, 1,838 elemental
and 1,837 ceiling controls, 1,016,752 instructions in total. Cases include all
conditional gates, threshold neighbours, signed odd division, the three small
ceilings, wrapped products, negative supplied values and fixed-cap precedence.
They do not simulate cache misses, source publication, actual infliction or
native client execution. Whole game resources and disassembly stay uncommitted.
