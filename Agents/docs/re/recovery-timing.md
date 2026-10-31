# Measuring recovery timing (3.29.3b)

The server's authoritative pool updates, the client's Life ticks, and the HUD's rendering are separate measurements.
An experiment against LocalExile establishes how this executable reacts to our messages; it does not measure the
official server's cadence. The probe below does not measure HUD animation.

## Running the probe

Start the client only through `scripts/dev/live.ps1`, with one client running. From the repository root:

```powershell
.venv/Scripts/python.exe scripts/re/frida/run.py Game.patched.exe --seconds 120 --log logs/recovery.jsonl scripts/re/frida/trace-recovery.js
```

Frida is required in that Python environment. The probe is standalone (do not prepend `common.js`), checks the
three hook signatures against 3.29.3b, and refuses a different executable layout. It makes no native calls and
changes no pool values. The runner detaches on timeout. Hook callbacks still add measurement overhead.

| Event | What it measures |
|---|---|
| `server-update` | The local player's pool setter applying a decoded 0x1b1 value, including its previous value |
| `tick-change` | Before/after pool values inside one client Life tick; samples limited to 25 per second |
| `summary` | All observed local-player Life tick counts/intervals and update counts over approximately one second |
| `frame-length-set` | The frame-length setter's value; also called during local initialization, so not proof of an incoming packet by itself |

The clock is millisecond wall time. `meanClientTickIntervalMs` is client update timing, not network/server tick rate.
Player object id 0x1000 is the LocalExile convention; a different session's identity and executable addresses need
verification before adapting this probe. No official-server recovery trace has been performed here.

## Controlled local check

Use a character with regeneration and enough life for a nonlethal `/hit physical 1000` (or a smaller hit). Record
incoming corrections and client tick changes. Compare a build that sends only the panel totals with one that also
sends the native recovery rates. Use existing `/set_stat` commands only for temporary, explicitly recorded controls;
those overrides reset on reconnect. Keep actual damage/recovery on the server authoritative.

Live 2026-10-25, OfflineWitch level100, monsters disabled: the original Gateway sent 15 life updates after a test
hit, but no client Life tick changed a pool between them. Supplying `life_recovery_per_minute` made the client advance
life between corrections. After the Gateway supplied modeled rates automatically, a fresh connection did the same
without a dev override. Life ticks were approximately 120/s in this session; the local server advertised 33 ms frames.
This does not establish official-server frequency. The coarse authoritative loop can still produce corrections,
especially immediately after damage; this change does not implement a full shared server simulation loop.

## Static evidence

The broader [client damage investigation](client-damage-calculation.md) now audits continuous loss as well as recovery.
The target executable SHA256, original instruction ranges and reproduction method are recorded there.
Pool steps integrate per-minute rates as `rate / 60 * dt`; this is a rate unit, not a minute-reset budget.
The network Stats getter honors explicit final-rate overrides before upstream fallback.
DoT formula presence does not prove that a particular network actor executes the full derived-stat graph.
Independent numerical hit prediction remains unresolved.
The older runtime observations on this page remain LocalExile measurements and are not repeated by the static audit.

| Address | Verified behavior |
|---|---|
| `0x141e678d0` | Life constructor: life/mana/ES pools at +0x178/+0x1c8/+0x210; native recovery keys 0x58e/0x58f/0x590 |
| `0x141e65df0` | Pool constructor stores the native recovery key at pool+0x1c |
| `0x141e6a190` | Life tick invokes each pool's step with its time argument |
| `0x141e6b320` | Pool step reads its recovery rate, divides by 60 and multiplies by tick duration |
| `0x141e6a600` | Adds recovery, carries fractional points at +0x28, updates current at +0x30, clamps to the unreserved maximum |
| `0x141e69fb0` | Applies incoming current/reserved values to a pool |

`life_recovery_per_minute` and `mana_recovery_per_minute` are distinct from the Defence panel's
`total_life_recovery_per_minute_from_regeneration` and `total_mana_recovery_per_minute_from_regeneration`.
Sending only the display totals does not drive the native pool step. The ordinary server ES model is now implemented
in `EnergyShield.cs`; see [damage](../protocol/20-damage.md#player-energy-shield-2026-10-25). The native recharge-start
event is still unidentified, so the server publishes regeneration plus recharge through key 0x590 during recharge.

## ES follow-up: source audit and cadence (2026-10-25)

OfflineWitch's saved mastery node 18240 selects effect HASH16 35810 (`EnergyShield4`). Its sole stat is
`base_energy_shield_regeneration_rate_per_minute_% = 120`: 2% per second, or **3.7 ES/sec at 185 maximum ES**.
The equipped wand has only its spell-damage implicit; this recovery comes from the mastery, not an ES recoup effect.
A second positive physical hit during recharge stopped the recharge-rate predictor and restarted the two-second delay;
the mastery regeneration correctly continued. Existing passives and equipment were preserved.

A controlled connection override set that mastery stat to zero, without changing the save. A 100 physical hit left
ES at 85 with no client ES tick changes before the delay; the first positive pool correction was 2042 ms after damage.
Selecting and replaying OfflineWitch removed the override and restored ordinary stats/full pools.

The old 250 ms authoritative loop produced recharge increments up to 22 ES in the recorded experiment, with corrections
against the client's prediction. The rebased change integrates ES into upstream `RunResourceRecovery`, which already targets **16 ms**, using actual elapsed instance
time and fractional carries. It does not introduce a second player recovery loop.
Proximity/quest/gold checks and pet following retain a 250 ms budget. Recovery-rate stat packets are emitted on rate
changes; pool packets still carry authoritative whole points.

With the same 185-ES character in the foreground, the client Life component ticked about 120/s and positive server ES
steps were at most 2 points, including recharge start; most prediction corrections were below one point. A second hit
still stopped recharge, and recharge resumed after its new delay. When unfocused this client ticked about 10/s and
processed several packets per frame, so background traces cannot establish foreground orb smoothness. These are pool
measurements, not orb-pixel capture or an official-server trace. The 16 ms local target is **not evidence of an official
60 Hz server**. Upstream [PR #159](https://github.com/Wrathen/LocalExile/pull/159) has merged. ES is integrated into its existing
life/mana/flask/RF recovery writer; `RunPlayerTick` retains its 250 ms housekeeping cadence.
