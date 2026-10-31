# Native client control

`scripts/dev/client_control.py` provides CLI and persistent JSON-lines RPC for the copied standalone **3.29.3b**
client. It calls existing client functions through [Frida RPC](https://frida.re/docs/javascript-api/#rpc), on the
game thread. It uses the normal local authentication, character list, Play, instance connection, and Gateway command
handlers. No screenshots, keyboard/mouse events, HTTP listener, save editing, or server authentication bypass are used.

Start the local servers normally through `scripts/dev/live.ps1`. Python needs `frida` (the walk-map recorder's `.venv`
already has it). Run from the repository root:

```powershell
.\scripts\dev\control.ps1 game:open
.\scripts\dev\control.ps1 login:offline         # private password prompt; use the local account's existing password
.\scripts\dev\control.ps1 character:list
.\scripts\dev\control.ps1 character:play:OfflineWitch
.\scripts\dev\control.ps1 game:status
.\scripts\dev\control.ps1 command '/hit physical 100'
.\scripts\dev\control.ps1 chat '#hello'           # any chat line: local, #global, $trade, or a slash command
.\scripts\dev\control.ps1 tp 'RhoaChest 2'        # /tp: also tp '120 340' (a cell) or tp '+10 -5' (relative)
.\scripts\dev\control.ps1 character:select
.\scripts\dev\control.ps1 logout
```

`game:open` attaches to an existing client, or calls `live.ps1 -ClientOnly`. That switch starts only the copied client
with the local patching-server argument and requires the local servers already running; it does not restart services,
rebuild, extract data, or close an existing client. With `-Walkmaps` (default ports and client only) it also starts
the walk-map recorder beside the client unless this checkout's recorder already runs. It waits up to 60 seconds for a native menu, so login can immediately
follow its successful result. Before opening a closed client on the default ports, it points the cached GGPK gateway
list at the local Auth when the list does not already (a stack on its own ports never writes the shared GGPK: the
bridge's login connects to its Auth by port); an existing client returns before that.

Several clients: `--session NAME` (`control.ps1 -Session NAME`, or `Game_CLIENT_SESSION`) drives one of several clients
of this checkout's stack, each opened by `game:open` with that session as `live.ps1 -ClientOnly -Session NAME` does
([live testing](live-testing.md#several-stacks-and-clients)). The bridge attaches to the PID `live.ps1` recorded for the
session in `data/clients/NAME.json` (checked with the process's start time and this checkout's client path), never to
another checkout's or session's client; without a session it takes this checkout's one unrecorded or default-recorded
client, as before. A client counts as this checkout's by the path it was started by (its command line: the image name
Windows reports resolves the `Client` junction to the shared folder, the same for every checkout). Logins go to this
stack's Auth: the ports `live.ps1 -PortBase` wrote to `data/stack.json`, else `Game_LOGIN_PORT`, else 20493. Startup asset loading still takes the
client's normal time. After merging upstream's gateway reset, a fresh client opened against the already-running
Patch/Auth/Gateway services in 9.5 seconds, then logged in, listed characters and entered an area successfully
(2026-10-25, isolated validation database). Mocked startup checks also cover existing clients and failed preflights.

Every command returns one JSON object: `ok`, `result` or `error`, and `elapsedMs`. `character:list` reads the list
actually received by the client; entries have `index`, `name`, `league`, `level`, and `classId`. Play resolves the name
against that list and waits for the client to enter the area. Replaying the active character and opening an already
running game are idempotent. Login reauthenticates, returning to selection first if necessary. A wrong password returns
`ok:false` with login result 6; retrying works without restarting. `command` sends a slash command, `chat` any chat
line (the client's own 0x15, as typed in the chat box), and `tp` the `/tp` dev command built from `x`/`y`, `dx`/`dy`,
`id`, `target` (+ `index`) or `list`. For slash commands the result also has `replies`: the chat lines the Gateway
answered with, read from `logs/live-gateway.log` (its `=> 0x0017 chat line` entries) until 0.25 s pass without a new
one or `wait` seconds (default 1.5) in total. `replies` is null when the servers were not started by this checkout's
`live.ps1` (no log); a command without an answer (e.g. `/goto`) returns an empty list after `wait`. Only the lines sent
to this client's own connection count (the log names each connection by the client's local port, which the bridge
reads from the TCP table for the client's PID; none while it has no connection to the Gateway), so other sessions on
the same Gateway do not mix in; replies of
commands typed in this client's chat box meanwhile still do. The bridge does not capture the HUD.

Explicit password login replaces both the outgoing account/digest and the native message's saved-credential flag.
The ordinary handshake can submit a remembered form before the explicit login RPC runs.
`AccountLogin +0x95` must therefore be cleared when replacing that form with a password digest.
The Remember Password preference at `+0x94` is retained; `login:saved` has no pending replacement and stays unchanged.
The server still validates saved credentials and password digests through their separate normal paths.
Native verification used a new synthetic account and the unchanged strict Auth server on `bbb658a9`.
A remembered form first produced result 6 with `Flag95=true`; the corrected script reached selection with result 0.
The tested combined source was `9736945d`, whose controller script is identical to this standalone correction.
Saved-login preservation is covered by the offline hook control, not claimed as new native acceptance.
Offline checks run with `python scripts/dev/test_client_control.py` and
`node scripts/dev/test-client-login-writer.cjs`.

## Persistent integration

Spawn `.venv/Scripts/python.exe scripts/dev/client_control.py --stdio` with stdin/stdout pipes. Write one JSON object
per line, flush stdin, and read one JSON result line. This retains the Frida attachment between calls:

```json
{"command":"login","account":"offline","password":"your-local-password"}
{"command":"character:list"}
{"command":"character:play","name":"OfflineWitch"}
{"command":"command","text":"/npc Tarkleigh"}
{"command":"command","text":"/pos","wait":0.5}
{"command":"tp","target":"RhoaChest","index":2}
{"command":"tp","x":120,"y":340}
{"command":"chat","text":"#hello"}
{"command":"game:status"}
```

These lines illustrate the format; consume each result before submitting the next command. Credentials are neither
echoed nor persisted. CLI login also accepts `--password-stdin` for a password supplied through a pipe. Do not put real
credentials in tracked scripts or shell arguments. The Python `ClientControl` context manager exposes `execute(request)`
for scripts in the same process. Closing stdin or leaving the context detaches; the game keeps running.

## Client implementation and verification

The bridge refuses a different executable path or code signatures. Addresses below are image VAs, rebased at attach:

| Address | Purpose |
|---|---|
| `0x141526cbf` | State-stack update after its `+0x38` transition guard is entered. Calling transitions before this guard can destroy live UI state. One pending RPC is serviced here, with a five-second cancellation if no frame arrives. |
| `0x1445cbe68` / `0x1445cbe40` | App and state-stack singleton pointers. App `+0x358` is login; `+0x348` is instance. |
| `0x14213d9f0` | Connect using UTF-16 host/port strings; bridge forces `127.0.0.1`. |
| `0x14213c660` | Native account-login writer. The handshake listener can auto-submit its saved form before a second RPC arrives; supply the requested account/digest to this **one** submission. The native string assignment owns its allocation; the digest is cleared from bridge memory after copying. |
| `0x14213ea10` | Login sender, used only if the handshake listener has not already submitted. |
| `0x14213f190` | Capture the actual login result (`0x04`, result at `+0x10`). |
| login `+0xe68` | Received character vector, 0x50-byte entries. |
| `0x1421406f0` | Play with the chosen name view and list index. Set in-game state `+0xa48` to its class; push **state 11 (loading)**, which installs the assignment listener. Directly pushing state 4 skips that listener. |
| `0x140d0f740` | Existing root login state's logout routine; preserves the correct login form lifecycle. |
| `0x140dfb760` / `0x140dfb8c0` | Existing buffered socket writer/flush, for `0x153` return to selection and `0x15` slash commands. Encryption remains the client's. |
| `0x14216e940` | Signature-checked reference for socket write coordination: mark writing, acquire the socket lock while threaded, write, release and unmark before flushing. |

The bridge coordinates buffered writes with the network thread's flush through the socket's own lock.
An unsynchronized write could desynchronize encryption permanently; author-reported native quest runs verified the fix.

Live verified 2026-10-25: fresh `game:open`, login, actual level-100 OfflineWitch list, Play, return to selection, logout,
wrong-password rejection, successful retry, Play again, and `/hit physical 1` received by the Gateway. In one persistent
run: login 0.7–0.9 s, list 94 ms, Play 1.4–2.2 s, command submission 94 ms; timings include game frames and loading,
and are observations rather than guarantees. Only the Witch class was exercised live.
`tp` checked live 2026-10-25 in The Mud Flats (seed 1): after `/tp RhoaChest 2`, `/tp GlyphWall` and `/tp +10 +0` the
client's own cell (Positioned +0x294/+0x298, as `scripts/re/frida/trace-player-drift.js` reads it) equalled the
Gateway's `/pos` every time, and the `/pos`, `/tp list` and refusal replies came back in `replies`. An attached bridge is for the
local development client; it has no official-service support.

Checks: `node --check scripts/dev/client-control.js` and
`.venv/Scripts/python.exe -m unittest discover -s scripts/dev -p test_client_control.py`.
Mocked RPC tests exercise the production login/status waits for success 0 before authenticated state 3,
immediate rejection 6, and the existing timeouts for pending or incomplete success; no new native verification is claimed.
`powershell -NoProfile -File scripts/dev/test_live_client_only.ps1` checks closed/existing client startup,
local gateway selection, and failed preflights with mocked processes and no GGPK writes.


## Build provenance for native verification

Switching branches does not rebuild `bin/`.
Before a `-NoBuild` pass, build the final source checkout and compare `git rev-parse HEAD` with the embedded
informational Git version of Gateway, Core and Host; record their hashes in the evidence metadata.
Verify the listening Gateway's DLL and working directory as well as the source tree.
A clean repository at the intended commit does not prove that a previously built DLL contains that commit.
Keep source and build outputs frozen during a native window, then restore the agreed normal runtime.

```powershell
git rev-parse HEAD
dotnet build
(Get-Item src/GameServer.Gateway/bin/Debug/net10.0/GameServer.Gateway.dll).VersionInfo.ProductVersion
Get-FileHash src/GameServer.Gateway/bin/Debug/net10.0/GameServer.Gateway.dll -Algorithm SHA256
```

## Targeted development interactions

JSON RPC also accepts `game:resources` (read-only Life component pools and recent 0x1b1 corrections),
`object:interact` with an object `id`, `move` with grid `x`/`y`, `npc:option` with row `index`, `npc:close`,
`reward:claim` with choice `id` and main-inventory `x`/`y`, and `inventory:pickup`/`inventory:place` with `inventoryId`
and item `id` or destination `x`/`y`, and `flask:use` with a flask-bar `slot` (0-4) or `key` (1-5; CLI
`flask:use 1`). `flask:use` sends the flask key's own C→S 0x70 (u32 cell, [13](../protocol/13-items.md) §8), so a flask
is used without keyboard input or window focus. These use the existing C?S interaction messages. Their result acknowledges
submission; wait for the resulting state or logs. Movement uses the client's Move action (0x2909). Interaction from
far away can fail to find a path; `tp` next to the object first (`{"command":"tp","target":"GlyphWall"}`), then interact. Resource updates are bounded to 128 entries and drained on
read, so persistent RPC is needed to observe a recovery interval. This measures client state, not HUD pixels.

`waypoint:travel` with an `area` (a WorldAreas HASH16, number or `"0x7ad2"` string) sends the World panel pin's C→S
0x4d ([16](../protocol/16-waypoints.md) §3): the Gateway changes area when that waypoint is activated
(`/waypoints all` activates every one). Live 2026-10-31 to The Coast and back with `/goto 1_1_town`.
`game:frames` returns the main-loop frames counted at the bridge's own frame point since it attached, and the frames
per second since the previous call (a session client with the cheap preset runs at about 9.2;
[headless client](headless-client.md)).

Live interaction checks included Tarkleigh's reward claim, starter weapon pickup/equip, the biting zombie's death and
gem pickup, walking to the support chest and opening it. The client stayed attached through these tests. RPC completion
is deferred until its native callback has returned, so a one-shot process can detach without unloading a hook while
it is still unwinding. Prefer loading additional probes into the same Frida session when extending this tool.
