# Headless ("nullified") client for agent sessions

Goal: many automated clients on one PC, each as cheap as possible in CPU, RAM and GPU, ideally with no GPU device,
no textures and no rendering, while the game logic, the network and the [client-control bridge](client-control.md)
keep working, and without getting in the way of the user's or other agents' clients. It is how `live.ps1` starts
every `-Session` client ([live testing](live-testing.md), "Several stacks and clients"); the default client (no
`-Session`) starts as before.

| File | What it is |
|---|---|
| `scripts/dev/nullifier.py` | the launcher `live.ps1` starts for a session client: spawns `Client/Game.patched.exe` suspended in the session folder, puts it in a kill-on-close job, loads the agent, applies the process limits, resumes it, records it, waits for it |
| `scripts/dev/nullifier.js` | its Frida agent, loaded before the client's first instruction: session-local two-copies mutexes and a window that never takes focus (always); with `-Nullifier` the engine's Null renderer (with the Null index-buffer fix); diagnostics on request |
| `config/nullifier_Config.ini` | the settings preset (DirectX 11, 800×600 window, low textures, 10 fps, effects off, sound muted, no `[LOGIN]`) that seeds a new session's settings file |
| `scripts/dev/nullifier-measure.ps1` | read-only sampler of one pid: private commit, working set, CPU, GPU engine utilisation, dedicated/shared GPU memory |
| `scripts/dev/nullifier-bench.py` | the measurement scenario on a session client: start, log in, play, stand in town, measure, then waypoint travel, `/goto` back, chat, `/tp`, measure again; lock, memory gate, watchdog, call timeouts |

## 1. Using it

```powershell
.\scripts\dev\live.ps1 -PortBase 21000 -NoClient                 # this checkout's servers on their own ports
.\scripts\dev\live.ps1 -ClientOnly -Session bot1 -Nullifier      # a GPU-less session client (or: control.ps1 game:open -Session bot1)
.\scripts\dev\control.ps1 login:offline -Session bot1 -PasswordStdin
.\scripts\dev\control.ps1 character:play:OfflineWitch -Session bot1
.\scripts\dev\control.ps1 command '/waypoints all' -Session bot1
.venv\Scripts\python.exe scripts\dev\client_control.py --session bot1 --stdio   # then, one JSON object per line:
{"command":"waypoint:travel","area":"0x7ad2"}
{"command":"game:frames"}
.\scripts\dev\live.ps1 -Stop -ClientOnly -Session bot1           # the launcher, and with its job the client
```

A session client always gets its own folder and settings, the job object, below-normal priority and EcoQoS, a window
that is shown but never activated, and session-local two-copies mutexes. `-Nullifier` (`Game_CLIENT_NULLIFIER=1`) adds
the Null renderer and trimming (`--renderer null --trim-seconds 10`); without it the session's settings decide the
renderer, which for a new session is the preset's DirectX 11 (§4 explains the choice). The bridge commands are
client_control's; `waypoint:travel` (`area`: a WorldAreas HASH16; the World panel pin's C→S 0x4d,
[16](../protocol/16-waypoints.md) §3; the Gateway accepts it only for an activated waypoint, so `/waypoints all`
first) and `game:frames` (main-loop frames since the bridge attached, and frames per second since the previous call,
counted at client-control.js's own frame point) were added for these clients.

`nullifier.py` can also be run by hand on a prepared session folder, e.g. for diagnostics:
`nullifier.py --cwd data\clients\S --record data\clients\S.json [--renderer null] [--trim-seconds 10]
[--frame-sleep-ms N] [--crash-log <file>] [--trace-files <regex>] [--probe <js>] [--no-render-jobs]
[--no-texture-upload] [--window minimized|hidden|normal] [--priority …] [--no-eco-qos] [--affinity 0x3] [--sound]`
(ports default to the stack's, `data\stack.json`). `--no-render-jobs`/`--no-texture-upload` stalled the main loop
(§3).

**Settings and folder.** The client opens `config.json`, `Content.ggpk`, its asset folders and `logs\Client.txt` in
its working folder (§2). `live.ps1` prepares the session folder (`data\clients\S\`: a hard link to `Content.ggpk`,
junctions to `Client\`'s asset folders, and a `config.json` naming `localexile-<checkout>-S_Config.ini` /
`_login.credentials`, the settings seeded from the preset without `[LOGIN]`). The launcher refuses the shared
`Client\` folder, a folder without `config.json`, and a `GGG_CONFIG_FILE_NAME` starting with `poeoffline_` or
`production_`, so a session client never writes the user's settings. (An earlier version redirected the settings
file through `CreateFileW`/`MoveFileExW` hooks instead; it missed the client's real save path and the user's
`poeoffline_Config.ini` was rewritten once, harmlessly, at 22:55:47 on 2026-10-30. That redirect is gone.)

**Lifetime, records and other processes.** `live.ps1` writes the pending record (`pid` 0, the time before the start,
the session, folder and ports), starts the launcher detached and waits until the launcher has merged `pid`, `started`
(the client's creation time), `launcher` and `launcherStarted` into it (atomically: temporary file and replace); after
60 s, or when the launcher ends first, it stops the launcher and throws. The launcher puts the client in a job object
with `KILL_ON_JOB_CLOSE` while it is still suspended, so it never outlives the launcher however that ends, and it
terminates it when the Frida session detaches (unhooked it must not keep running). Priority, EcoQoS, affinity,
trimming, liveness and termination use the one process handle opened at the spawn; nothing looks a pid up again
(Windows hands an exited client's pid to the next new process; an earlier version once attached to such a process).
The bridge finds the client by the record (`ClientControl(client_session=S)`, PID and start time, started from this
checkout's `Client\` path). `-Stop` stops a launcher only when its PID, start time and command line match the record.

**The two-copies limit.** The client allows two copies per PC. Right at startup (`0x140101680`, at `0x140101d85`) it
calls `CreateMutexW(NULL, FALSE, "Global\PoERunMutexA")` and `WaitForSingleObject(handle, 0)`; if another process
owns it (`WAIT_TIMEOUT`) it does the same with `Global\PoERunMutexB`, and if that is owned too it throws "You may only
run up to two copies of Path of Exile at a time." (`0x140102587`), which shows as a dialog the client then waits on, so
a third copy hangs invisibly. The handles are never stored, released or looked at again: the mutexes exist only for
this count. A client of ours would therefore take one of the two machine-wide slots that the user's and the other
sessions' clients need. `nullifier.js` (always, renderer or not) rewrites exactly these two names in all six
name-taking variants, `CreateMutexW`/`CreateMutexExW`/`OpenMutexW` and their ANSI forms (the client imports
`CreateMutexW` for this check, and `CreateMutexA`/`OpenMutexA` in LMDB's code, `0x1411c0a40`), to
`Local\LocalExile-<checkout>-<folder>-PoERunMutexA`/`B`: a session-local name per client folder, so session clients
never take a Global slot and never block each other across folders (two clients in the same folder still share a
pair). The hooks are installed while the client is still suspended, before its first instruction, and each rename is
reported; the launcher waits up to 30 s for that report after resuming the client, and without it closes the client
and fails the launch, so a silent hook failure cannot leave a client holding a Global slot. Nothing else uses those
names. What the count does not guard, and what stays shared between all copies whatever their number:
`PathOfExileSingleInstance` (a session mutex for handing a `.build`/`.filter` file to a running client's window,
`0x140100780`), `Global\PoERunMutexCrashUpload` (crash-report upload), and LMDB's own `Global\MDB…` locks around the
shared shader and pipeline caches in `%LOCALAPPDATA%\Path of Exile` (multi-process safe by design, up to 126 readers);
these names are left alone. The startup "Wiping cache …tmp" of that folder runs in every copy, independent of the
mutexes. Settings, saved login and `Client.txt` are per folder; `Content.ggpk` is opened read-only and shared.
`live.ps1` checks the slots only for the default client, before it stops or starts anything: it refuses when two
clients hold Global slots. A client a launcher started, from any checkout, holds none, and is known by its parent (a
python running `nullifier.py`, started before it), by `--nologo` on its command line (only the launcher passes it),
or by this checkout's records. The renaming is checked offline (`scripts/dev/test-nullifier-mutex.cjs`, Frida's API stubbed);
live (§5) the session clients held `\Sessions\3\BaseNamedObjects\LocalExile-pr-nullifier-v1-PoERunMutexA` (and
`…-v2-…`) and no Global one, and while two of them ran a separate process could still take and release both
`Global\PoERunMutexA` and `B` with a zero wait.

A plain `client_control.py` from a checkout without sessions (`main` before the multi-instance work) finds its client
by process name: it attaches to any lone `Game.patched.exe`, a session client included, and refuses when two run. That
is why extra clients may only run next to checkouts that have the session-aware scripts ([live
testing](live-testing.md)).

## 2. What the client offers (3.29.3b, image base `0x140000000`)

### Settings file and environment

- The default settings file is `Documents\My Games\Path of Exile\poeoffline_Config.ini` (the copied client's default
  name, patched by the setup from `production_Config.ini`). The client builds that name in `0x140e2c340` and then
  reads `config.json` from its working folder if it exists, a JSON object whose keys `GGG_CONFIG_FILE_NAME`,
  `GGG_CREDENTIAL_FILE_NAME`, `GGG_LOGIN_SERVER_PORT`, `GGG_BASE_WEB_CDN_URL`, `CONFIGURATION_NAME`, ... override the
  built-in values (`0x140e2d200` reads a JSON string member; the client logs `Loading config file: config.json`).
  These are **not environment variables**: the strings exist only as JSON keys, and the scripts' `pop` of an
  environment variable `GGG_CONFIG_FILE_NAME` has no effect. Content.ggpk and `logs\Client.txt` are also opened in
  the working folder. The client logs to its standard output too, so the launcher spawns it with piped stdio.
- Command-line flags parsed in `0x1400f8450`: `--nosound`, `--softwareaudio`, `--nologo`, `--no-device-recovery`,
  `--no-nv` (no NVIDIA Streamline/DLSS), `--no-agility-sdk`, `--disable-dx12-pso-cache` / `--enable-dx12-pso-cache`,
  `--use-defaults`, `--allow-priority-boost`, `--disable-assert-box`, and the login/patching ones. None turns rendering
  off.
- `[DISPLAY]` keys read in `0x141908570`: `device_type` (`DirectX11`, `DirectX12`, `Vulkan`), `adapter_name` /
  `adapter_uid`, `resolution_width/height`, `fullscreen`, `borderless_windowed_fullscreen`, `maximize_window`,
  `texture_quality` (`TextureQualityLow` / `TextureQualityHigh`), `texture_filtering`, `vsync`, `triple_buffering`,
  `framerate_limit` (+0x50) / `framerate_limit_enabled` (+0x54), `background_framerate_limit` (+0x58) /
  `background_framerate_limit_enabled` (+0x5c), `shadow_type`, `light_quality`, `global_illumination_detail`,
  `water_detail`, `screenspace_effects(_resolution)`, `bloom_strength`, `depth_of_field`, `upscale*`,
  `use_dynamic_resolution` / `dynamic_resolution_fps`, `use_dynamic_particle_culling`. A client that is not the
  foreground window runs at `background_framerate_limit` (10 by default), which is the case of every agent client.

### The engine's Null device

The renderer has a sixth device type besides DirectX 11/12, Vulkan and the console ones: **Null**.

- `Engine::Init` (`0x1410048f0`, `(engine, options)`) copies the engine options' switches and logs them: options
  `+0x2d5` is `[RENDER] Render: ON`, `+0x2bf` `[TEXTURE] Upload`, `+0x2bc` `[TEXTURE] Streaming` (also
  `[RENDER] Async`), `+0x306` `[ENGINE] Test Disable Frame Render Jobs`, `+0x303`/`+0x304` the synchronous UI and
  simulation test switches. The Render switch goes to the renderer's init `0x140faf1e0` as its fourth argument and is
  stored at renderer `+0x14f3b8`.
- The device start `0x140fadb20` (and the second device start `0x140fb4340`) replace the settings' device type with **6**
  when that switch is off; otherwise only 0–3 are accepted. `0x140fd39e0` maps settings type 6 to device enum 0, which
  `[RENDER] Starting device: ` logs as `Null` and the device factory `0x140e6a0a0` builds as a 0x298-byte object with
  vtable `0x143202f10`: 36 methods, 30 of them `ret`, `return 0` or `return false`. There is a matching
  `ShaderCacheNull` cache name. No D3D or Vulkan device, swap chain, GPU memory or texture upload exists with it.
- Nothing in the shipped client turns the switch off (it is not a flag or a settings key), so `nullifier.js` hooks
  `0x1410048f0` at spawn and writes options `+0x2d5 = 0` before the engine reads it (`--renderer null`).
  Startup still enumerates the adapters (`0x140f0eca0` creates a throw-away D3D12 and D3D11 device on each adapter to
  log its feature level, `Enumerated device for adapter …`), which touches the GPU driver once.
- **The Null index buffer's missing "describe".** Live, the client first reached character selection, generated the
  town and then died 3–6 s later with an access violation at `0x142be6be4` (`movss xmm0, [rsi]`, `rsi` 3.4 GB past
  the vertex array). That is the triangle test of the mesh ray cast `0x142be7270` (`0x142be6b10`, called per
  triangle with three vertex pointers `vertices + index × stride`). It asks the index buffer to describe itself
  (vtable `+0x20`, out: u32 format, `0x65` = 16-bit, `0x66` = 32-bit; u32) and then Maps it (`+0x10`). The Null
  device's index buffer (vtable `0x14320e140`, a 0x150-byte object made in `0x140e939b0`) keeps a zero-filled byte
  vector of the buffer's size at `+0x130` (Map returns its start) and the index kind at `+0x148` (1: 16-bit, 2:
  32-bit; the DirectX 12 buffer keeps the same value at `+0x318`, and its describe `0x140ee7990` returns `0x65`/`0x66`),
  but its describe slot is the shared no-op `ret` `0x1400f0f00`. The caller then compared an uninitialised format,
  read a 16-bit buffer as 32-bit, ran past its end (a probe showed 378 bytes, 756 read) and turned heap garbage into
  indices. `nullifier.js` (`--renderer null`) points that one vtable slot at a small machine-code stub that answers like the
  DirectX 12 buffer from the Null buffer's own fields (format from `+0x148`, index count from the vector's size).
  The indices themselves stay zero (the Null device keeps no uploaded data), so these ray casts hit nothing, which
  the client tolerates; the server is authoritative for movement and position.
- With that, the Null client enters areas, stays 60 s in town, travels by waypoint and changes area by `/goto`, and
  the bridge's commands all work (§3). The engine still logs hundreds of `[ENTITY2] … Shader generation failed` CRIT
  lines per area (the Null shader "builds" produce nothing); they are harmless.

### Graphics API paths (for the other options)

- `d3d11!D3D11CreateDevice` is imported directly (IAT `0x142f43f88`), `dxgi!CreateDXGIFactory1` too (`0x142f43fb8`);
  `d3d12.dll` (ordinal 101) and `vulkan-1.dll` are delay-loaded. The DirectX 11 device (`0x140e94e10`, 0x458 bytes)
  calls `D3D11CreateDevice(adapter, D3D_DRIVER_TYPE_UNKNOWN, …, 4 feature levels, SDK 7)` with the IDXGIAdapter the
  settings selected (`adapter_name`/`adapter_uid` among the enumerated ones, at `+0x238` of the adapter record).
- **WARP** (software rasteriser): the enumeration lists `Microsoft Basic Render Driver` (feature level 0xc100) next to
  the real GPUs, so `device_type=DirectX11` + `adapter_name=Microsoft Basic Render Driver` selects it with no hook.
  It moves all rendering to the CPU and textures to system memory: GPU-less, but expensive.
- `D3D_DRIVER_TYPE_NULL` needs the debug-layer `d3d11ref.dll`, which is not installed here; the engine's own Null
  device makes it unnecessary.
- Frame pacing: the preset's `framerate_limit=10` caps every renderer, the Null one included (9.2 frames per second
  measured with both); game logic, the network and the bridge keep working at that rate (the server is
  authoritative; the client only predicts and reports). `--frame-sleep-ms` adds a sleep at the main loop's
  per-frame state-stack update (`0x141526c40`; client-control serves its RPCs 0x7f bytes into it, at `0x141526cbf`,
  and checks those bytes, so the nullifier hooks the entry). `game:frames` counts frames at client-control.js's own
  point.

### Process level

`nullifier.py` sets the priority class (`below-normal` by default) and EcoQoS (`SetProcessInformation(
ProcessPowerThrottling, EXECUTION_SPEED | IGNORE_TIMER_RESOLUTION)`, retried with `EXECUTION_SPEED` alone where
Windows 10 refuses the second flag: low clocks / efficient cores, and the client's `timeBeginPeriod` no longer raises
the system timer rate), optionally the CPU affinity and a periodic `EmptyWorkingSet` (`--trim-seconds`: pages move
to the standby list; private commit is unchanged, so it helps physical RAM, not the commit limit). The window is
created with `WS_EX_NOACTIVATE` (live: without it a new client's window took the keyboard focus at creation; with
it the foreground window stayed the user's and the clients' logs show no focus at all; it also keeps them off the
taskbar), shown without activation (`SW_SHOWNOACTIVATE`, default), minimized (`SW_SHOWMINNOACTIVE`) or hidden, and
`SetForegroundWindow` is neutralised, so a launched client cannot take the keyboard focus from the user's window.
**Minimizing does not stop it.** The engine pauses its main loop when its window procedure (`0x140e67380` →
`0x140e68220`) sees `WM_SIZE` / `SIZE_MINIMIZED` (it logs `[WINDOW] Minimized`): live, a DirectX 11 client started
minimized never reached the login screen, and a Null client someone minimized stopped answering the bridge. The agent
turns that one message into `WM_NULL` before the engine sees it; the window still minimizes, and the engine keeps its
last size and runs at its background frame rate (live: both renderers minimized at 9 fps, `/pos`, waypoint travel,
`/goto`, chat and `/tp` answered). A hidden window also hides an error dialog.

## 3. Measurements

Scenario (`nullifier-bench.py`): fresh client in its own session folder, bridge login, `OfflineWitch` played into
Lioneye's Watch, 10 s settle, then 60 s sampled every 5 s by `nullifier-measure.ps1`; then `/waypoints all`,
`waypoint:travel` to The Coast, `/goto 1_1_town`, `/pos`, chat, `/tp`, and 20 s more. CPU is percent of one core (16
logical cores). Machine: RTX 5070 Ti + Radeon iGPU, 31 GB RAM.

Measured 2026-10-30/31 with the extra-client lock held, a launch gate (free RAM ≥ 8–9 GB, free commit ≥ 3–6 GB) and a
watchdog that killed only the run's own client when free commit fell below 1.5 GB or free RAM below 4 GB. Other
agents' test hosts (up to 13 GB each) and client restarts made the machine's free commit swing between 0.3 and 11 GB:
the watchdog fired 9 times, and other sessions' old `live.ps1` killed the measurement servers five times and one
measured client (it stops every `Game.patched` and `GameServer.*` process by name). The full-quality baseline and
WARP were skipped on purpose (the reference row stands in for the baseline).

| Tier | Settings | Private commit | Working set | CPU (% of a core) | GPU util | GPU dedicated | Bridge |
|---|---|---|---|---|---|---|---|
| reference: a normal client of another session (read-only counters, Vulkan, high textures, unfocused, state unknown) | user's | 7.49 GB | 4.27 GB | 37.5 | 9 % | 3.38 GB | — |
| preset + process, DX11, in town (60 s) | preset, below-normal, EcoQoS, `--window normal`, `--nosound` | 4.33 GB | 2.46 GB | 50.7 (311 in the first 5 s while shaders compiled; ~11 after) | 7 % | 1.66 GB | login, list, play ✓ |
| same, after waypoint travel and `/goto` (20 s) | same | 4.59 GB | 2.74 GB | 10.7 | 4.2 % | 1.78 GB | all ✓ (below) |
| preset + process + `--trim-seconds 10`, DX11, in town (shader cache warm) | same | 4.32 GB | 0.23 GB (max 0.69) | 18.9 | 5.9 % | 1.64 GB | login, play ✓ |
| **Null renderer** (`--renderer null --frame-sleep-ms 50`), in town | preset, tier 2 | 1.90 GB | 1.76 GB | 14.9 | 0 | 0 | login, list, play ✓ |
| same, after waypoint travel and `/goto` (20 s) | same | 1.96 GB | 1.91 GB | 4.6 | 0 | 0 | all ✓ |
| **Null renderer + `--trim-seconds 10`** (no frame sleep), in town | preset, tier 2 | 1.89 GB | 0.12 GB (max 0.23) | 17.0 | 0 | 0 | login, list, play ✓ |
| same, after waypoint travel and `/goto` (20 s) | same | 1.97 GB | 0.05 GB | 6.3 | 0 | 0 | waypoint travel, `/goto`, `/pos`, `/tp` ✓ |
| Null + `--no-render-jobs --no-texture-upload --window minimized` | same | 1.79 GB | 1.76 GB | 26.5 | 0 | 0 | in town ✓ at 6.7 fps; the main loop then stopped (no frame for the bridge within 5 s) |
| Null before the index-buffer fix (3 runs) | same | 1.8 GB | 1.65 GB | — | 0 | 0 | login, list ✓; crash 3–6 s after entering the town |
| DX11 with `--window minimized` | preset | — | — | — | — | — | no frames while minimized: never reached login |

"All ✓": `/waypoints all` (reply listing every waypoint), `waypoint:travel` to The Coast (`/pos` then answered
`(1362, 626) in 1_1_2`), `/goto 1_1_town` (`Area generated at Seed: 1`), `/pos` (`(384, 224) in 1_1_town`), chat,
`/tp +5 +0` (`Teleported to (389, 224) in 1_1_town.`). Every client but the `--no-render-jobs` one ran at 9.2 frames
per second and entered the town 6–8 s after launch (menu 1–3 s, login 0.8 s, play 2.5–4.3 s). The Null client runs 41–43 threads against 107 for
DirectX 11. The session settings file was written by the client (`localexile-pr-nullifier-bench_Config.ini`), the
user's `poeoffline_Config.ini` not (it was written once afterwards by another session's default-folder client, which
uses that file by design).

Against the reference client, the Null renderer saves 75 % of private commit (1.9 against 7.5 GB), all GPU memory
and GPU time, and runs at about 15 % of a core in town (5–6 % standing after travel); with `--trim-seconds 10` its
resident memory drops to 50–230 MB. Against the DirectX 11 preset it saves 2.4 GB of commit and 1.7 GB of VRAM per
client.

Run a row with, for example:

```powershell
.venv\Scripts\python.exe scripts\dev\nullifier-bench.py --label null --session bench --nullifier --lock D:\path\.extra-client.lock --travel
```

It needs this checkout's servers on their own ports (`live.ps1 -PortBase`), starts the client with `live.ps1
-ClientOnly -Session bench [-Nullifier]`, drives it with `ClientControl(client_session="bench")` and stops it with
`live.ps1 -Stop -ClientOnly -Session bench`. Results append to `logs\nullifier\bench.jsonl` (the measure JSON plus
`game:frames` and the mode; a watchdog kill is recorded too). The measurements above were taken with the earlier
stand-alone launcher (the same spawn, agent, job and limits; it drove the client itself), before the run-mutex
renaming.

## 4. What is safe to default for agent sessions

- **Every session client: the launcher's safety and process tier** (own folder and settings, kill-on-close job,
  session-local two-copies mutexes, a never-activated window, below-normal priority, EcoQoS). None of it changes what
  the game does.
- **Default renderer for a new session: the preset's DirectX 11** (no `-Nullifier`). Live: login, play, slash
  commands, chat, `/tp`, waypoint travel and `/goto`; 4.3 GB commit, 1.7 GB VRAM, ~11 % of a core at 9 fps once its
  shaders are compiled. It is a real renderer with no reverse-engineered patch in the way, so it is the safe choice for
  an agent whose session may reach any area or screen.
- **`-Nullifier` for many clients or no spare GPU:** the Null renderer with trimming. Live the same way, ~1.9 GB
  commit, no GPU device or memory, ~15 % of a core, 50–230 MB resident. Only Lioneye's Watch and The Coast were
  entered; another area or combat may still reach a Null-device gap (the first one, the index format, is fixed). If a
  session breaks under it, drop `-Nullifier` for that session and report the crash (`nullifier.py --crash-log`).
- **Not a default:** `--no-render-jobs`/`--no-texture-upload` (more CPU, and the main loop stalled),
  `--frame-sleep-ms` (the preset's cap already gives 9.2 fps).
- **Untried:** WARP (`adapter_name=Microsoft Basic Render Driver` in the preset, no hook), which trades the GPU for
  CPU and is not needed now that the Null device holds.

## 5. Live verification of the live.ps1 integration (2026-10-31)

Run from this checkout on its own ports (`live.ps1 -PortBase 22000 -NoClient`), the extra-client lock held, a
watchdog on memory and on foreign clients or default-port servers (it never fired), no default client started.

| Step | Check | Result |
|---|---|---|
| 1 | `live.ps1 -ClientOnly -Session v1` (DirectX 11 preset) | ✓ in 2.1 s; record with `pid`, `started`, `launcher`, `launcherStarted`; `run mutex -> Local\LocalExile-pr-nullifier-v1-PoERunMutexA` in `logs\nullifier-v1.log`; handles: the Local name, no Global one |
| 1 | bridge on v1 | ✓ login, list, play (in game 5.9 s after the bridge started), `/pos`, `game:frames` 9 fps, `/waypoints all`, waypoint travel (`(626, 488) in 1_1_2`), `/goto 1_1_town`, chat, `/tp +5 +0` |
| 1 | cost (30 s in town) | 4.27 GB private, 2.90 GB working set, 10.4 % of a core, GPU 8 %, 1.66 GB VRAM |
| 1 | settings | the session's `localexile-pr-nullifier-v1_Config.ini` rewritten by the client at login (922 → 8349 bytes) |
| 1 | `-Stop -ClientOnly -Session v1` | ✓ launcher and client gone, record removed |
| 2 | `-Session n1 -Nullifier`, same bridge sequence | ✓ all; 1.89 GB private, 0.18 GB working set (trimmed), 23 % of a core, no GPU or VRAM |
| 3 | kill the launcher | ✓ its job ended the client at once |
| 3 | `-ClientOnly -Session f2` with this stack's Patch server stopped | ✓ refused before anything was written or started (`missing port 22000`); no record, launcher or client |
| 4 | v1 (DirectX 11) and v2 (`-Nullifier`) at once | ✓ both logged in and played the same character on the one Gateway; each held only its Local name; `Global\PoERunMutexA` and `B` were free (taken and released with a zero wait by a separate process) |
| 4 | focus | ✗ first run: each new window took the focus at creation; fixed (`WS_EX_NOACTIVATE`), rerun ✓: the foreground window unchanged, no focus in the clients' logs |
| 4 | minimized | ✗ first run: v2, minimized, stopped answering; fixed (the minimize message is withheld from the engine), rerun ✓: both minimized at 9 fps; v2 minimized did waypoint travel, `/goto`, chat, `/tp` |
| 4 | cost of the pair (20 s) | v1 4.28 GB / 2.72 GB / 11.6 % / 1.66 GB VRAM; v2 1.82 GB / 0.04 GB / 7.4 % / none |
| 4 | `-Stop -ClientOnly -Session v1` | ✓ v1 and its launcher gone, v2, its launcher and record untouched, v2 still in game (`/pos`) |
| — | user's `poeoffline_Config.ini`, `production_Config.ini` | ✓ mtime and SHA-256 unchanged |

Not covered live: another checkout's default client beside the sessions (the Global-slot count is tested with mocks),
`--window hidden`, areas other than Lioneye's Watch and The Coast under the Null renderer.
