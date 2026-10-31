# 07 — Latency (0x1b/0x1c and HNC), visibility (wake zone) and movement

State of 2026-10-16: the client's "network" graph stays flat (0–14 ms on localhost since 2026-10-19, §1), the Witch is drawn in Lioneye's
Watch, and clicking the ground walks her there at normal speed. Evidence:
[../re/evidence/2026-10-16-witch-visible.jpg](../re/evidence/2026-10-16-witch-visible.jpg).

## 1. Tick and ack: C→S 0x1b, S→C 0x1c

The client sends **0x1b** every 100–500 ms in state 3 (u8 counter + 4 bytes, 7 bytes in all; `0x142148880`) and pushes
the send time onto a queue at instance `+0xc458`. Each frame it sets `latency = now − queue.front` (or 0 when empty);
that is the perf overlay's "network: N ms". Without an answer the queue only grows, so the value climbs forever.

**0x1c** (instance group, dispatcher case 5, class `0x1435dc8c0`, read `0x141fa0720`): `u16 server delay ms`,
`u16 index`, `u32 HNC carrier`, all big-endian. `0x142155930` drops queue entries 0..index and returns entry `index`'s
send time; an index ≥ the queue's size clears the queue and returns `+0xc470`, the latest tick's send time (live Frida
2026-10-19). `+0xc488` is not a round trip: every client send stamps it with the time. One 0x1c with index 0 per
received 0x1b keeps the queue at the one tick in flight (`InstanceProtocol.Pong`).

Ticks that no server answers stay in the queue for good: those sent while the client leaves an area, or lost in
unframed bytes. Index 0 then always pops a stale entry. Live in The Coast the queue held 3 at every 0x1c (ticks every
167 ms), and the meter read ~400 ms. The first 0x1c of each connection, and the first after an unframed read, therefore
sends index 0xFFFF (`ClientInbox.NextAckIndex`). After that the queue held 1 at every 0x1c.

**What the latency graph shows.** The network update `0x142148880` runs once per client frame on the network thread:
it reads the socket, handles each 0x1c, sets the sample `+0xc480` = now − queue front (0 when empty), and sends a 0x1b
every 100 ms (500 ms after more than 100 ms without a client send, `+0xc488`). The graph and "latency N ms" show
`+0xc490` (`0x140d57cd0` feeds it to the perf ring). Two places move it:

- the network update raises it to the sample when the sample exceeds it by a margin (a tick overdue);
- the 0x1c handler (`0x14214b8dc`, after the pop) computes rtt = now − the popped send time, subtracts the 0x1c's
  u16 server delay from rtt and from the sample, and clamps the value into [sample − delay, rtt − delay]: it only
  drops when an rtt comes in below it.

So the value is the lowest round trip measured since the last overdue tick, and it cannot go below one client
update: the client reads the 0x1c at the start of the update after the one that sent the 0x1b (live: socket read →
pop → send the next tick, all in one update). Live Frida 2026-10-19 in town: sample 0, value 12, rtt 166–169 ms with
the client in the background (~6 updates/s), so the value stayed at the 12 measured while it ran at full frame rate.
Measured on one QPC clock (Frida on the client and the Gateway, 2026-10-19), the Gateway answers each 0x1b 0.15–0.28 ms
after the client sends it; the rest is the 0x1c waiting in the client's socket until its next update.

So the Gateway sends server delay 0xFFFF in every 0x1c (`ClientInbox.ServerDelayMs`, issue #49). Both clamp bounds
become 0, so each 0x1c sets the value to 0 and F1 NETWORK reads 0. An overdue tick still raises it through the network
update, which ignores the delay, so a real stall still shows until the next 0x1c.

The 0x1c takes only the connection's wire gate (`Program.Write`: encryption and the socket write), never the send gate
whose packets evaluate game state in their `still`/`atGate` callbacks. Those callbacks wait for the combat lock, and in
a crowd of 148 monsters they held the send gate for up to 4 s, so the answers queued behind them and the meter jumped
to 0.5–4 s. Measured afterwards: no 0x1c later than 50 ms in a crowded Coast session.

Tick latency does not measure queued gameplay work.
`ConnectionPerformance` separately records socket-read-to-handler queue age, handler duration, send-gate wait/hold,
wire-gate wait/hold, socket writes, hot combat-lock wait/hold, channel lateness, surround time and blocker-index builds.
The `LocalExile.Gateway` meter exports duration histograms in milliseconds, surround allocations in bytes and pending
input batches; tags contain operation names, never character or account identifiers.
Closed connections discard their abandoned backlog so its depth does not remain in the process total.
At most one `[latency]` log summary is emitted per connection every ten seconds when an interval sample reaches 50 ms.
Set `$env:Game_TRACE = 'latency'` (or the older `$env:Game_LATENCY_METRICS = '1'`) in the PowerShell session before
starting `scripts/dev/live.ps1` to log every interval instead ([debugging](../dev/debugging.md)); read
`logs/live-gateway.log` for `pending-batches` and each phase's `interval-max-ms`.
The logging flag does not change gameplay or the client's latency display.

The full player stat and Actor refreshes (aura, charge, Fortification and Rage changes) build the whole skill list
(`PlayerSkills`) inside the send gate, so every echo and monster packet waits for it. In 2026-10 one build of a 10-gem
level 100 Witch cost 180–270 ms in the Debug Gateway (45–65 ms Release), and an aura toggle ran two or three of them:
move handlers logged 400–730 ms in an empty hideout. Each gem resolved its links and supports (`SupportedSkill`) and the
final player stats five or six times, and every gem requirement check scanned the whole GrantedEffectStatSetsPerLevel
requirement list. One build now resolves each once (`SkillBuildScope`) and the requirements are indexed by stat set:
5–10 ms per build in Debug and Release alike.

## 2. HNC, the hidden side channel

The 0x1c u32 is not free data. `0x141fa0720` feeds its bits 9..31 then 0..8 (LSB first) into a byte accumulator on the
stream (`+0xd0`). Every completed byte is decrypted and run through `0x14299de50`:

- idle: a byte must decrypt to 0; otherwise it must be a packet type `0xDA`–`0xDD`
- then a length byte (≠ 0), then that many payload bytes, queued as a packet
- anything else throws **"HNC Protocol Violated"** (an exception dialog; a 0x1c with u32 = 0 did exactly that)

The HNC cipher is the login-style Salsa20 pair keyed with **km'** = the 64-byte 0x14 key XOR the table at
`0x142fe0da0`, then byte swaps 0↔0x15, 4↔2, 0xd↔0x2a, 0x30↔1, 0x1d↔0x1e, 0x32↔0x13 (`0x14299db60`); the client receives
with IV `km'[48:56]`. So an idle carrier is the keystream itself: `v = rotl(LE32(keystream[4]), 9)`
(`HncSender.NextIdleU32`, tested by replaying the client's unpacking in `HncTests`). The client also sends HNC packets
(type `0xDD` from the tick); the server ignores them for now.

## 3. Visibility: the wake zone (S→C 0x1a9)

An object created with 0x2b9 exists (position, components, camera follow, teleports) but is **not drawn and does not
update** until it is in the wake zone. **0x1a9** (world group, class `0x1435dd838`, object header only: `u16 id, u32
object id, u32 0, u16 0`) sets object `+0x8d |= 2` and calls `0x141ec0aa0` ("ObjectInWakeZone"); 0x1aa removes it. The
Gateway sends 0x1a9 for the player right after 0x21. That alone made the model appear (in the washed-up pose) and let
actions run.

Also sent, not proven necessary: Positioned `flags1 0x02` with (hidden 0, shown 1) (`0x141e2f0b0` → vtable +0xb0), and
Animated bit0 with the path `Metadata/Characters/Int/Int.ao` (the type already supplies it; the Animated deserializer
only reloads when the path differs or none is set). Neither made the model appear before 0x1a9 was added.

## 4. World-group object messages

The world handler `0x142187480` looks up `u32 object id` (message `+0x10`) and routes by id:

| Ids | Goes to | Notes |
|---|---|---|
| 0x51, 0x1ab–0x1b0, 0x1b4–0x1b6, 0x1ce–0x1d2, 0x1f0/0x1f1, 0x261, 0x2c6 | Actor `0x1421940a0` | actions |
| 0x1b7–0x1c0, 0x2b0 | Positioned `0x141e30c90` | position / movement |
| 0x1a9 / 0x1aa | wake zone in / out | |
| 0x2ba | component update by component index | |
| 0x2bb | remove object | |

Object header (all these messages): `u16 id, u32 object id (+0x10), u32 (+0x14), u16 (+0x18)`, written by `0x141f91f60`.

## 5. Movement

**C→S 0x25** "use skill at location" (sender `0x142166300`, class `0x1435bfbd8`, 16 bytes): `u32 x, u32 y` (target grid
cell), `u16 skill id` (Move = 0x2909 for the Witch), `u16` action `+0x11a` (an action sequence number: 0x1001, 0x1002, …;
only sent when ≥ 0), `u16` action `+0x122` (flags, 0x0408). 0x24 is the same aimed at an object id. The client starts the
action itself (prediction) but will not send another until the server echoes it.

**S→C 0x1ab** "action started" (class `0x1435c2340`, read `0x141fdda70`): object header, `u16 flags` (+0x38 → action
+0x122; bit 0x80 adds a `u32 target object id`), compact ints **actor position** (+0x2c/+0x30 → action +0x158) and
**target** (+0x24/+0x28 → action +0x130), `u16 skill id`, `u16 sequence` (+0x36 → action +0x11a). For the local,
predicted player (sequence ≥ 0) the handler compares the position pair with where the client thinks the player is and
**teleports** the player there when they are ≥ ~92 units apart (`|Δ|² ≥ 0x2110`); otherwise the echo is just a
confirmation (`InstanceProtocol.ActionStarted`). With the echo the client walks at its own speed and accepts the next
click. 0x1ac ends an action, but is ignored for predicted local actions.

The first version sent the target in the position pair: long clicks teleported the player, sometimes onto unwalkable
cells past the town edge (toward The Coast), where the client could no longer path anywhere. The server therefore keeps
its own copy of the player's walk, and since 2026-10-21 that copy is the client's own algorithm, ported 1:1
(`ClientPathfinder`, `PlayerWalk`). The earlier fitted A* (`WalkEstimator`, still used for monsters) drifted by a median
13 cells and up to 50 on held-mouse walks: it walked zig-zag cell paths where the client walks straight lines, rounded
to whole cells on every re-order, walked through NPCs and refused detours the client takes.

**The client's pathfinder** (`0x141e4d890`, from the Pathfinding component's MoveTo `0x141e7b530` → `0x141e7b920` →
`0x141e4ef90`). Arguments seen (Frida, `scripts/re/frida/trace-movement.js`): a Move click or a monster's move has
precision 1, min and max range 0, tolerance −1, no phasing; a walk up to an object to use it has max range = the
object's size + 3 and tolerance = its size (an NPC, size 4: 7 and 4; the stash, 2: 5 and 2; a waypoint, 5: 8 and 5).

- **Grid and size.** Walk values are 4 bits per cell in 3 layers (`0x141d85260`: map `+0xd0 + 0x18 × layer`, row stride
  `+0x100`, size `+0x18/+0x20` in 23-cell tiles). A mover of size *s* (Positioned `+0x1e3` = `object_size − 1`: the
  player 1, NPCs 4, the stash 2, monsters 1–3) may stand on a cell whose value is ≥ *s* + 1, so a monster of size 2
  needs ≥ 3 where the player needs ≥ 2. The player uses layer 0
  (Positioned `+0x2c0`), which is byte for byte `data/walkability/<area>_seed<n>.bin`, so walkable = value ≥ 2. Layer 1
  only serves the goal check (a line test with threshold 1), which never fails on a walkable town cell.
- **Search.** A* in a 272 × 272-cell window (`0x110`, centred on the start). 8 neighbours in the order (0,1), (1,0),
  (0,−1), (−1,0), (1,1), (1,−1), (−1,−1), (−1,1) (`DAT_142fbf350`); no corner rule. Costs are 1 / 1.4142 and the
  heuristic is `|cell − target| + 0.001 × |cross(target − start, cell − target)|` (`0x141e4d7d0`; the distance less the
  tolerance, then the part outside [min, max], 0 inside), all in float32. The open list is MSVC `push_heap`/`pop_heap` on
  `{node, f}` with `a.f > b.f` (`0x141beb420`, `0x141beb2b0`), stale entries are skipped, and the goal is the first
  popped node with h = 0.
- **The player's limit is an ellipse.** For a mover with a Player component (`0x14013f1e0`) there is no expansion
  count; a neighbour n is searched only if `clamp(2 |target − start|, 20, 136) > |n − target| + |n − start|`.
- **Other movers stop after 100 expansions** (1000 for a unique, ObjectMagicProperties `+0x144` = 3, or a pet with
  PetAi). When that search does not reach the target and the straight line to it is blocked, a **coarse pass** runs:
  A* over cells of 11.5 grid units (`(c × 23) / 2`), seeded from the start's coarse cell and its (0,1), (1,0), (1,1)
  neighbours where a straight line reaches them, costs 11.5 / 16.2634, only through cells within 136 of the target and
  a straight line apart, at most 300 expansions; the first coarse cell with h < 23 that sees the target ends it. Its
  chain back (goal first, with the path length from each to the target) then guides a second fine search: a cell heads
  for the next coarse waypoint once it is within √143 and in sight (`0x141e4ee90`), and its heuristic becomes the
  distance to that waypoint plus the length left (`DAT_1445bb850`, `0x1445bb868`).
- **Objects** (`0x141e61370` → `0x141e61540`). Objects are found in the 23-cell tiles within *s* + 4 of the cell; one
  blocks when `max(|dx|, |dy|) − its size < s + 1`, a square (an NPC: Chebyshev distance ≤ 5 from its cell). Objects
  with `blocking = false` (waypoints, area transitions) are never tested. Layer, team and phase flags can exempt an
  object; a walker that starts inside a footprint may cross object cells at cost 21.
- **Unreachable targets.** The search ends at the explored cell with the lowest heuristic.
- **Waypoints.** Walking back from the end, a cell is kept where the Bresenham line (`0x141d87160`, terrain; `0x141e616a0`,
  objects) from its parent to the last kept waypoint is blocked. At most 136 waypoints.

**The client's walk** (Positioned `0x141e34400` → `0x141e4d180`, ticked by `0x141e32710` → `0x141e4d390`). Each segment
between waypoints is a Bresenham line of cells; one step takes `(√(dx² + dy²) / speed) / max(|dx|, |dy|)` seconds,
and the grid cell (`+0x294`) only changes a step at a time. Each frame adds its time to the accumulator (`+0x244`, a
whole number of milliseconds on the client's clock) and steps while it holds a whole step time (`+0x240`); before every
step the next cell is checked again (terrain, objects) and a blocked step ends the walk. A new segment, including the
first one of a new order, rescales the accumulator to its own step time (`A × s_new / s_old`), so a re-order keeps the
elapsed fraction of the step in progress. A stop (`0x141e350e0`) takes effect in the next tick, after that tick's
steps, and keeps the accumulator. Verified on a capture (2026-10-21): the per-tick rule held in 55,915 of 56,069 player
ticks (the rest are counter resets), the rescale on 575 of 575 clicks. Speed (`0x141e7c0b0`) is the Pathfinding base
(`+0x548`, `base_speed` 37 for the player, times the Actor's run-speed roll `+0x1a8`) × (1 + `movement_velocity_+%`
/ 100) × (1 + stat 0xce6 / 100), capped by stat 0x44f.

**Click timing.** The client applies a click right after its frame's tick and sends the 0x25 in the same frame: on one
QPC clock (Frida in the client, the Gateway's `Game_MOVE_TRACE=1` log), the 0x25 reaches the Gateway's socket 0.24 ms
(sd 0.05) after the client's sender ran, and the tick was 0.74 ms before that read (526 clicks). So the server applies a
click at its read time (`ClientInbox.Batch.ReadAt` → `ClientState.ClockAt`), not when the handler runs. The client sends
no clock: neither 0x25, the 0x1b tick nor the HNC 0xDD (an inventory checksum) carries one. Its game clock counts whole
milliseconds and runs ~1 ms (sd 0.5) before its tick, so a click whose step boundary falls within ~1 ms of the frame can
still differ by one cell.

**Monsters on the client.** A monster's 0x1ab is applied when the client next reads packets, 5–50 ms after the
server sent it (median 17 ms), and the client paths it at once (1,235 of 1,235 recorded monster paths match
`ClientPathfinder` with `SearchLimit.Expansions100`, all other objects as blockers and the size's walk threshold). When a
walk ends short of the target the client paths again on its own, and monsters far from the player keep their walk but do
not tick. Their speeds vary by a few percent (7.60–8.40 for a base of 8): the Actor's run-speed roll `+0x1a8`. Actor
setup `0x141e9ca20` seeds a TinyMT32 state (`0x1413cbe50`, init_by_array) with the key [object id, the object type's
u32 at its data +8] and, when the MonsterVarieties row's two unnamed i32 columns before `AilmentThreshold` (`__col75`,
`__col76`) differ, draws a uniform integer in that range (`0x1404d03d0`); the roll multiplies the Pathfinding speed by
1 + roll / 100. Of the 10 691 rows, 8 694 have (0, 0), 875 (−10, 10) and 855 (−5, 5) (every plain Goatman: 8 × 0.95
to 1.05 is the band measured), then (−5, 10), (0, 10), (−30, 15), (−5, 8), (−15, 15). The server draws the same roll
(`MonsterSpeedRoll.cs`, `AnimationVariantSelector.NativeRandom`) with the monster's 0x2b9 type hash as the type word
and walks its estimate at that speed (the Gateway's create log prints the speed and the roll). The range is certain;
that the type word is the type hash, and so the exact value, is the research branch's reading of the type loader and
is not yet checked live: `scripts/re/frida/trace-movement.js` prints each monster's seed words, range and multiplier.
Decompiled in the research branch's `re/server-gaps-3.29.3b.md` §3.

**Server side.** `PlayerWalk` runs `ClientPathfinder` from the player's cell on every order and steps the result at the
client's step times; walk-ups to NPCs, stashes, waypoints and area transitions use the client's range
(`PlayerWalk.WalkUpTo`, `InWalkUpRange`); the blockers are the NPCs and stashes the Gateway created (`ClientState.SetBlocker`), the living
hostile monsters where the server's estimate has them (size MonsterVarieties `ObjectSize` − 1, not checked live), and the
quest walls' cells. Replaying a recorded session (424 client path calls, 2026-10-21), every path matched the client's
waypoint for waypoint. At each order, the predicted cell was exact 184 times, within one cell 392 times, and at most 2.8
cells off. The rest is the client's frame timing, which the server cannot see. A second session (2026-10-21, town and The Coast,
speeds 37 and 370) matched all 940 town paths; on The Coast 569 of 614 matched without monsters, and the other 45 bend
around monsters (hence monsters as blockers). Held-mouse walks drift by a few cells (town, 37 cells/s: median 1, max 7;
at 370 cells/s one 16 ms frame is ~6 cells): the client applies a click from its previous frame, the server when the
0x25 arrives, so the timing differences add up while the button is held and vanish when a walk ends on its target. With
the walk-up arguments, all 246 town paths of a third capture match, NPC, stash and waypoint walks included. Monsters
still use `WalkEstimator`. `Approach` (a melee target, an item pickup, the map device) keeps a reach in cells; the
client's arguments for those walks are not traced yet.

**Passive movement speed.** The passives' `base_movement_velocity_+%` are summed into `CharacterStats.Pools` and the
walk estimate uses `37 × (1 + pct / 100)` cells/s; the same total is sent as the client's `movement_velocity_+%`, so the
client and server walk at the same speed. `ApplyMovementSpeed` runs on login, level, passive changes and area changes;
`/set_speed` adds its percentage on top of the passive total.
Only unconditional `increased Movement Speed` passives are modelled: action speed, more multipliers, gear/buff
`movement_velocity_+%`, conditional nodes and the `movement_velocity_cap` constant are not applied yet.
`movement_speed_cannot_be_reduced_below_base` bounds the total below by 0 (PoB `max(MovementSpeedMod, 1)`).
The client's lower bound for `movement_velocity_+%` is 0 instead of -100 from the same raw flag (`0x14251cbd0`).

**Compact int** (writer `0x141fa3c50`): signed, 1 byte for −0x40..0x3f (value & 0x7f), else 2/3/4 bytes with first-byte
prefixes 0x80/0xc0/0xe0 carrying the top 6/5/4 bits, else 0xf0 + 4 bytes; big-endian (`InstanceProtocol.CompactInt`).

**S→C 0x1ba** move (class `0x1435dd7b8`, read `0x141fe0ff0`): header, `i32 x, i32 y, u32 +0x28, f32 speed, u8`; the
Positioned handler calls `0x141e34120` with duration = distance / speed. With speed 37 it overrode the client's own walk
and crawled (~1 cell per 1.5 s), so it is not sent for the local player (`Game_MOVE_MODE=move` to test).
**S→C 0x1b7** teleport: header, `i32 x, i32 y, u32 → +0x290, u32 → +0x2b4`; works (`Game_MOVE_MODE=teleport`).


### Living actor clearance

`LivingCollision` uses `MonsterVarieties.ObjectSize - 1` as the positioned footprint, including unique bosses.
Negative footprints do not block; zero is a valid small body.
Monsters with a negative footprint still chase directly without reserving surround slots or avoiding living bodies.
`AreaLayout.WalkableFor` retains the captured terrain clearance values instead of treating every actor as player-sized.
Monster `WalkEstimator` orders use the bounded native pathfinder with the client's budget: 1000 expansions for a
unique, 100 for any other monster (`0x141e4d890`).
Their blockers include stationary objects, other living monsters, the local player and published instance players.
Surround candidates, including retained slots, must clear the player's Chebyshev footprint.
The circular ring alone is insufficient: its diagonal cells can lie inside the player's blocker for larger monsters.
Inner slots must also lie within the monster's attack reach when space permits.
Stationary blockers and published players are excluded from candidate slots.
The native path budget can end a valid route short; an arrived chaser retries at most twice a second.
Each AI/collision pass owns one combat-locked body snapshot and indexed self-exclusion views for all mover paths.
Collision corrections update both current and previous-position indexes before later movers use them.
The indexes are discarded after the pass; normal/unique expansion budgets, stationary hulls and phasing remain intact.
AI and correction passes advance prior projectile contacts before taking the body snapshot, using the shared
projectile mutation scope and its experience-before-combat lock order.
The 50 ms correction loop checks each traversed cell, stops movement through newly occupied cells and moves only the
server's estimate (then paths it again to the same target, as the client does when a walk ends short). It used to send
ActionEnded followed by Teleport: 1,608 corrections in one crowded session, each a visible stop and jump, although the
client already paths every monster around the other bodies. The client never snaps a non-predicted monster to the
0x1ab position pair (the ≥ ~92 unit check in `0x1421940a0` runs only for sequence ≥ 0), so the AI's new orders do not
jump either. The overlap test reads every body's cell once per frame, and `BlockerSet` buckets the blockers by cell so
`ClientPathfinder.ObjectBlocks` reads only those that can reach the queried cell (same answers as the full scan).
The player's own path treats estimated monster bodies as blockers too; when they leave no way to the clicked cell but
the stationary blockers do (`PlayerWalk`'s fallback), the server follows the client through the crowd instead of
pulling the player back on every click.
This escape is allowed only when the original path cannot get beyond the immediate footprint and the alternate end
cell is free of living bodies; clicking an occupied destination does not grant an escape.
While that walk is in progress, player correction ignores estimated monster bodies but still enforces terrain,
stationary blockers and peer arbitration.
Arrival, a replacement order or a teleport ends the escape; a new stationary obstruction still cancels and corrects it.
Alive friendly monsters also block; corpses and removed objects do not.
Phasing removes living bodies from the local player's path checks.
Large bodies are checked even when their centres lie outside the small query's spatial buckets.

`InstanceState` publishes immutable player snapshots without acquiring another connection's monster lock.
Each connection has a distinct remote object id in the `0x20000000` range; its own player remains `0x1000`.
Observers create a normal Player object without local-player selection, update movement/life and remove it on departure.
Ordinary pose updates use 0x1ba; large coordinate jumps use 0x1b7.
Arrival overlaps prefer moving the later peer, avoiding two actors choosing the same escape cell.
This provides body collision for players already sharing an instance, not a party manager or shared monster simulation.
Native checks: normal zombie clearance 4 cells, unique Hillock clearance 5 cells; removal frees the target cell.


## 6. Lockstep (not served yet)

Everything above is the client's **Predictive** network mode (Options → Game → Network Mode;
`[NETWORKING] networking_type=predictive` in `Documents\My Games\Path of Exile\production_Config.ini`; the mode is
`+0x984` of the world: 0 predictive, 2 lockstep, 3 auto). "Auto" picks lockstep when connecting took under 70 ms and
predictive over 90 ms (`0x142148880`), so it picks lockstep on localhost. The Gateway answers lockstep's moves with a
message box asking for Predictive (once per connection).

**Server frames.** In lockstep the client's world does not run on its own clock: it runs **server frames**. S→C
**0x2b6** (u8) sets the frame length in ms (`0x141645fa0` stores it at world `+0x11c`); S→C **0x2b5** (id only) ends a
frame: the world's message intake (`0x1421872b0`) queues one frame of that length (deque at world `+0x9c0`, count
`+0x9d8`) behind the world messages received so far (`+0x9e0`). Each client update adds its time to world `+0x988` and
runs every queued frame it covers (`0x142183930` → world vtable `+0x360` = `0x142183ce0`): the frame's messages first,
then the objects tick by exactly the frame's length. Behind by 1, 2, 30 or 60 frames, it speeds up × 1.01, 1.5, 2 or 5;
the most frames it was behind since the last tick goes out as the u8 of C→S 0x1b (world `+0xb98`,
"lockstep_lag_frames"). Without 0x2b5 nothing runs: the player is created but never drawn. A predictive client ignores
0x2b5, and 0x2b6 only stores its byte, so the Gateway always sends them (`RunServerFrames`, 33 ms, `Game_FRAME_MS`):
a lockstep client is then drawn and its world runs. Found with hardware watchpoints on those fields
(`scripts/re/frida/watch-lockstep-world.js`).

**Client moves** (`0x14215f700`, `0x14215fb10`, `0x14215fcb0`, `0x14215fe50` send them when `+0x984` is 2):

| Id | Bytes | Fields |
|---|---|---|
| 0x018e | 15 | use a skill at a location: i32 dx, i32 dy (the target relative to the player's cell), u16 skill id, u16 action flags, u8 (`018E 00000029 FFFFFFEF 2909 0402 00`) |
| 0x018f | 13 | use a skill on an object: u32 object id, u16 skill id, u16 flags, u8 (instead of predictive 0x24) |
| 0x0192 | 10 | the held skill's new target: i32 dx, i32 dy |
| 0x0195 | 2 | release |
| 0x0196 | 4 | u16 skill id: release that skill |

The client still creates the action itself, with its sequence number's top bit set (`| 0x8000`), and waits for the
server. Answering a click with a non-predicted 0x1ab (sequence 0xffff) made the player walk (live 2026-10-21);
answering held-mouse updates with S→C **0x1ad** (object header, u16 sequence, i32 x, i32 y: a new target for the action
in progress, `InstanceProtocol.ActionTarget`) did nothing, presumably because the sequence must match the client's own
action number, which the lockstep messages do not carry. Serving lockstep means: that sequence, every action through
the lockstep messages (0x018f for NPCs, stashes, transitions, items), and the server stepping every object by its
frames (the tick rules above) and sending the start of each action in the frame it belongs to.

## 7. More client messages seen

| Id | Bytes | Notes |
|---|---|---|
| 0x015a | 2 | id only, after entering |
| 0x01ef | 2 | id only |
| 0x0099 | 4 | `0099 0000`: two u8 (class `0x1435c0258`); meaning unknown. The 20 bytes once taken for it were 0x99, the two chat channel joins 0x134 ([10](10-chat-and-commands.md) §2) and 0x129 (id only) |
| 0x004e | 5 | `004E 00 2909`: u8 + Move skill id, after a click (skill released?) |

## Open

- Area transitions: The Coast exit (and other exits) are world objects the server has to create; none are sent yet, so
  walking to the town edge just stops there.

- What the server should do with the client's HNC packets (type 0xDD: a one-byte inventory checksum reply, sent when the
  client's item count differs from the server's 0xDC request).
- The meaning of 0x1ba's +0x28 and u8. Moving other (non-predicted) objects works with 0x1ab and the Move skill
  0x2909 (monsters, [10](10-chat-and-commands.md) §5); 0x1ba's float is distance per second (duration = distance / value).
- 0x0099 and 0x004e.
- Monsters on the server still walk on `WalkEstimator`. `ClientPathfinder` paths them as the client does (§5) and the
  estimate walks at the client's rolled speed, but the client's monster controller (its own re-paths, frozen far
  monsters) and the 5–50 ms until the client applies an order are not reproduced; their estimated positions are what
  the player's path avoids.
- The client's walk-up arguments for melee attacks, item pickups and the map device (`Approach`).
- Lockstep (§6): the action sequence for 0x1ad, the object-use path (0x018f) and server frame stepping.
