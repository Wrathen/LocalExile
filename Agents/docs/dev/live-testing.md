# Live testing against the real client

How to run the patched 3.29.3b client against our servers safely and repeatably.

## Rules

1. **Never let the client update.** An update replaces the patched client and data. Start it only through
   `scripts/dev/live.ps1`, which passes `--patching-server 127.0.0.1:12995`. The patch server advertises only
   `http://127.0.0.1:8080/` and answers 404 for everything except `gateway_list.txt`. Check
   `Tools/re/out/live-patch.log` for `cdn passthrough off`.
2. **Never start a second client other than through `live.ps1`** (that includes computer-use
   `open_application("Game.patched")`). A second instance started without the patching-server argument tried to
   download `PathOfExile.exe` from the real CDN on 2026-10-16. Several clients at once are named sessions
   ([below](#several-stacks-and-clients)). To bring the running client to the front, restore its window:
   ```powershell
   Add-Type -Namespace W -Name U -MemberDefinition '[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr h, int c); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);'
   $h = (Get-Process -Name Game.patched).MainWindowHandle; [W.U]::ShowWindow($h, 9); [W.U]::SetForegroundWindow($h)
   If the window stays minimized (`ShowWindow` from a background process is ignored), post a restore command instead:
   ```powershell
   Add-Type -Namespace W3 -Name U -MemberDefinition '[DllImport("user32.dll")] public static extern bool PostMessage(System.IntPtr h, uint m, System.IntPtr w, System.IntPtr l);'
   [W3.U]::PostMessage((Get-Process -Name Game.patched).MainWindowHandle, 0x0112, [IntPtr]0xF120, [IntPtr]0)
   ```
   ```
3. With "Save Login Details" ticked the client auto-submits the saved login at startup. If it fails, Auth now closes
   the connection and Log In works again. In the client the password field needs a click before typing; Enter does not
   submit, click Log In.
4. The dev account is `devtester` (password in `data/dev-login.txt`, untracked). Its characters live in
   `data/saves.db` ([saves](../saves.md)).

## One-time setup

```bash
dotnet run --project src/GameServer.Auth -- keygen Client/Game.exe
dotnet run --project src/GameServer.Auth -- patch Client/Game.exe Client/Game.patched.exe
```

Also point the GGPK gateway list at the local login server (`GameServer.Patch gateway --login 127.0.0.1:20493`; the
original is backed up to `data/gateway_list.original.txt`), and register the dev account with `GameServer.DevClient`.

## A run

```powershell
$env:Game_GATEWAY_ENTER = '1'      # also send 0x21 (enter area). Leave unset to stop after the player object
& .\scripts\dev\live.ps1           # -NoClient keeps a running client, -NoBuild skips the builds
```

Gateway env: `Game_MOVE_MODE` = `echo` (default: only the 0x1ab echo), `move` (also 0x1ba, `Game_MOVE_SPEED`) or
`teleport` (0x1b7); `Game_PLAYER_AO` overrides the Animated path; `Game_HIT_OFFSET_MS` (default 150) delays the
default attack's hit past its contact keyframe ([11](../protocol/11-skills.md) §5).

`live.ps1` stops this checkout's servers and clients (anything else on its ports is reported, not stopped), builds
Auth, Patch and Gateway, starts all three (logs in `Tools/re/out/live-*.log`), waits for the patch server and launches the
client. Then:

1. wait for "Loading in" in `Client/logs/Client.txt` (startup takes about a minute; the file check is local)
2. log in as `devtester` and select DevWitch
3. Play

To restart only the Gateway: `.\scripts\dev\live.ps1 -Only Gateway`. The client drops to the login screen and you
log in again; the login server keeps running.

## Several stacks and clients

Many people or agents can each run their own servers and clients on one PC, one checkout (git worktree) each, without
touching each other's.

```powershell
.\scripts\dev\live.ps1 -PortBase 21000 -NoClient           # this checkout's servers on 21000-21004
.\scripts\dev\control.ps1 game:open -Session a1             # a client of this stack, session a1
.\scripts\dev\control.ps1 login:offline -Session a1 -PasswordStdin
.\scripts\dev\control.ps1 game:open -Session a2             # a second one beside it
.\scripts\dev\live.ps1 -ClientOnly -Session bot1 -Nullifier  # a GPU-less one (the engine's Null renderer)
.\scripts\dev\live.ps1 -Stop                                # this checkout's servers and clients, nothing else
```

- **Ports.** `-PortBase N` (or `Game_PORT_BASE`) puts the stack on N (Patch), N+1 (Patch web), N+2 (Auth login), N+3
  (Auth control) and N+4 (Gateway). `live.ps1` remembers the base in `data\stack.json`, so later runs, `-ClientOnly` and
  the bridge in the same checkout use it without being told again; `-PortBase 0` goes back to the default ports
  (12995, 8080, 20493, 20495, 6112), which are also what a checkout without the file uses. On the default ports a
  server's own variable (`Game_LOGIN_PORT`, `Game_PATCH_URL`, ...) still wins; with a base the shell's are not used.
  `data\stack.json` holds every port the servers got, and the bridge reads it. Auth sends the client to its own Gateway (`Game_GATEWAY_PORT` in login 0x14, as
  the Gateway does in 0x27). The levelgen server (`lg.ps1`) needs no port: its named pipe is per checkout.
- **What is stopped.** `live.ps1` stops only this checkout's processes: servers whose command line, executable or cmd
  wrapper names a path inside the checkout (it starts them with absolute paths; a worktree nested in it under
  `.claude\worktrees\` is another checkout), with everything they started (a watched Gateway's `dotnet watch` tree, but
  not the MSBuild nodes and compiler server its builds start, which every build shares); clients started from this
  checkout's `Client\` path (each checkout reaches the shared client through its own junction, so the path it was
  started by tells checkouts apart; Windows' image name resolves the junction) and, among those, the session's by the
  PID and start time recorded in `data\clients\<session>.json` (written with pid 0 just before the start, so no one
  takes a client being started for another session's); a session client's launcher (`nullifier.py`), by the PID, start
  time and command line it adds to that record, stopped first so its job object ends its client; and its own walk-map
  recorder. Another program or checkout on its ports is reported, not stopped: give one of them other ports.
- **Client sessions.** `-Session NAME` (also `Game_CLIENT_SESSION`; `client_control.py --session`, `control.ps1
  -Session`) starts a client in its own folder, `data\clients\NAME\`. The client opens `Content.ggpk`, `config.json`,
  its asset folders (`FMOD` sound banks, `Streamline`, `D3D12`) and `CachedHLSLShaders`, and writes `logs\Client.txt`,
  in its working folder; only the executable and its DLLs come from the executable's folder. So the session folder holds
  a hard link to the shared `Content.ggpk` (the same file: nothing is copied or written), junctions to the shared asset
  folders, its own shader cache and `logs\Client.txt`, and a `config.json` naming the session's own settings file and
  saved login, `localexile-<checkout>-NAME_Config.ini` and `..._login.credentials` in `Documents\My Games\Path of Exile`.
  A new settings file starts as a copy of the default client's `poeoffline_Config.ini` without its `[LOGIN]` section, so
  the session sends no saved login anywhere. The client reads `GGG_CONFIG_FILE_NAME`, `GGG_CREDENTIAL_FILE_NAME` and the
  other `GGG_*` keys from that `config.json` (the key table at `0x140e2cd0d` looks them up in the parsed file; nothing
  reads them from the environment). Live (2026-10-30, a session on ports 21000-21004 beside another checkout's client):
  its `Client.txt` in the session folder logged `Current directory: …\data\clients\a1`, the stack's web root and "No
  patching required" (the linked GGPK); without the junctions it logged `CRIT SoundBank Global Index File does not exist`
  and `[STREAMLINE] Failed to load: DLL not found`, with them neither. The memory guard stopped both runs while the client
  was still loading, so the settings file it uses and a login through the bridge are not confirmed live yet. Remove a
  session folder with `cmd /c rmdir /s /q`, not Windows PowerShell's `Remove-Item -Recurse`, which can follow the
  junctions into the shared `Client\`. The bridge attaches to the session's recorded PID and reads only the chat replies sent
  to that client's connection (the Gateway log names each connection by the client's port). Without a session the
  client starts in `Client\` with the shared settings, as before (a plain start, unchanged).
- **Session clients start through `scripts\dev\nullifier.py`** ([headless client](headless-client.md)). `live.ps1`
  prepares the folder and the pending record, then starts the launcher detached and waits (60 s at most) until it has
  merged the client's PID and start time, and its own (`launcher`, `launcherStarted`), into the record; when it fails
  the launcher is stopped, the record removed and `live.ps1` throws (the launcher's output is in
  `logs\nullifier-<session>.log` / `.err`). The launcher spawns the client suspended, puts it in a job object that ends
  it with the launcher, loads a Frida agent (session-local two-copies mutexes, below; a window that is shown but never
  activated), sets below-normal priority and EcoQoS through the one process handle it opened, resumes it, and waits
  for it. The client lives as long as its launcher; `-Stop -ClientOnly -Session S` stops both (the launcher only when
  its PID, start time and command line match the record). `-Nullifier` (`Game_CLIENT_NULLIFIER=1`) adds the engine's
  Null renderer (no GPU device or memory) and working-set trimming; without it the session's settings decide the
  renderer.
- **Logging in.** The bridge (`login:offline`, `login:saved`) connects to this stack's Auth by port. The client's own
  Log In button and a saved login use the login server in the GGPK's cached gateway list, which only the default stack
  writes (and only when it does not already name its Auth; reading the list works while clients run): the GGPK is
  shared and cannot be written while any client runs, and then `live.ps1` warns and goes on. A client of a stack on its
  own ports therefore logs in through the bridge.
- **Shared by everyone:** `Client\` (the executable and `Content.ggpk`, which every client opens read-only), the shader,
  pipeline and minimap caches in `%LOCALAPPDATA%\Path of Exile`, crash dumps in `Documents\My Games\Path of
  Exile\release`, and, in worktrees made by linking, whatever they link (`data\tables`, `data\walkability`). **Per
  checkout:** the ports, `logs\`, the save database (`Game_SAVES_DB`), `data\stack.json`. **Per session:** the working
  folder, `Client.txt`, settings, saved login and PID record.
- **Walk maps** (`-Walkmaps`) only on the default ports and client session (elsewhere `live.ps1` warns and starts
  without the recorder): the recorder meets the Gateway in the shared `data\walkability\current.json`. It attaches to
  the default client by the PID `live.ps1` records (`walkmaps.py --record data\clients\default.json`), and only when
  that process's start time and launch path (this checkout's `Client\`) match the record, as the bridge checks it: a
  stale record names no client. Other checkouts' clients may run.
- **Only next to checkouts that have this.** A checkout whose `client_control.py`, `walkmaps.py` and `live.ps1`
  predate these sessions finds its client by process name: any other `Game.patched.exe` beside its client stops its
  bridge ("Multiple patched clients") or attaches it to the wrong client, and its `live.ps1` stops every client and every
  GameServer process on the PC. Start an extra client only when every checkout with a running client has these scripts.
- **Two clients per PC, unless renamed.** The client takes one of two machine-wide mutexes, `Global\PoERunMutexA` and
  `Global\PoERunMutexB` (`0x140101d85`), and with both taken it waits on a dialog, "You may only run up to two copies
  of Path of Exile at a time." (seen live 2026-10-31). Nothing else uses those mutexes ([headless
  client](headless-client.md), "The two-copies limit"). Session clients get session-local names instead
  (`Local\LocalExile-<checkout>-<session>-PoERunMutexA`/`B`), so they never take one of the two slots the user's and
  other checkouts' default clients need, and any number of them can run. The default client still needs a slot:
  `live.ps1` refuses to start it when two clients hold Global slots, and checks that before it stops or starts
  anything (a full run would otherwise leave servers up for a client that cannot start). Session clients of any
  checkout do not count: they are known by their launcher parent, the `--nologo` only the launcher passes, or the
  records. Live 2026-10-31: two session clients held only their Local names, and both Global names stayed free.
- **Cheap session settings.** A session's new settings file starts from `config\nullifier_Config.ini` when the
  checkout has that preset (DirectX 11, 800x600, low textures, 10 fps, no sound), else from the default client's;
  `-SeedConfig <file>` (`Game_CLIENT_SEED_CONFIG`) starts it over from another file. Session clients run at below-normal
  priority with EcoQoS. Their windows never take the focus (`WS_EX_NOACTIVATE`), and minimizing one does not stop it
  (the engine is not told). The default for a new
  session is that preset with its DirectX 11 renderer: the broadest-tested cheap setting (login, play, waypoint travel,
  `/goto`, chat, `/tp` live; 4.3 GB commit, 1.7 GB VRAM, ~11 % of a core at 9 fps). `-Nullifier` is the choice for many
  agent clients or a machine without a spare GPU: 1.9 GB commit, no GPU memory, ~15 % of a core, 50-230 MB resident with
  its trimming, verified the same way but only in Lioneye's Watch and The Coast; if an area breaks under it, drop
  `-Nullifier` for that session.
- **Memory.** Each in-game client commits about 7 GB with the default settings (6.75 to 7.43 GB measured for three
  campaign clients on 2026-10-30), a session client with the cheap preset about 4.3 GB and with `-Nullifier` about
  1.9 GB ([headless client](headless-client.md) §3); while it starts a client takes several GB within seconds;
  a Gateway about 0.75 GB right after start, Auth and Patch under 0.05 GB. Check the free commit
  (`(Get-CimInstance Win32_OperatingSystem).FreeVirtualMemory`) before opening another client.

## Changing the Gateway while it runs (hot reload)

`live.ps1` runs the Gateway under `dotnet watch` unless `-NoHotReload` is given
([GettingStarted](../../GettingStarted.md) step 10).
These are suggestions that have worked well, for people and agents alike; use your judgment.

- **Try saving before restarting.** Most behaviour edits (packet handlers, damage, drops, AI, chat commands) apply to
  the running Gateway in about 15 s, and the player stays in game. A restart costs the player a login and the walk
  back to what they were testing.
- **Check that it applied.** `logs\live-gateway.log` gets `[gateway] hot reload: <file> saved` and then
  `[gateway] hot reload #n applied in x.yz seconds`; players see the same two lines in chat.
  `dotnet watch`'s own lines (`File updated`, `changes applied`, compile errors) are in `logs\live-gateway.err`.
- **A compile error leaves the old code running.** `dotnet watch` reports it in `live-gateway.err` and waits for the
  next save; nothing restarts. Players read "Hot-reload failed: n compile error(s)" in chat with the first three
  errors (file, line, code). Fixing it and saving again is usually all that is needed; when the fix brings the code
  back to what is running, `dotnet watch` applies nothing and players read "no code changes to apply".
- **Group a multi-file change.** Each save that compiles starts a reload, so a half-done change across several files
  can apply in between. Writing the files in quick succession (or in one tool call) keeps it to one reload.
- **Know what needs more than a save.** Static fields and catalogs (`Items`, `Skills`, `Lazy<>` caches) keep the values
  they were built with; new static fields start empty, not with their initializer. Per-connection setup (the chat
  command table, character preparation) runs again on the next area change, which is a new connection. A loop that is
  already running keeps its old body until it returns. For these, an area change or `live.ps1 -Only Gateway` helps.
- **Some edits restart on their own.** A changed method signature or a removed member cannot be applied;
  `dotnet watch` prints `Restart is needed to apply the changes`, rebuilds and restarts the Gateway, and the player
  is back at the login screen (the Gateway tries to say so in chat first). It can be kind to tell the person playing
  before saving such an edit.
- **Tests while it runs.** The watched Gateway holds its `bin\` folder; build and test elsewhere, for example
  `dotnet test tests/GameServer.Gateway.Tests --artifacts-path C:\Temp\lx` (Troubleshooting in GettingStarted).
  Keep the monster AI cadence collection nonparallel: its socket-latency assertion retains its 300 ms limit, so
  parallel catalogue loads and their GC pauses must not run beside it. Arcane Might's area fixture measures its
  projectile range from the weapon-contact origin to the target, rather than assuming the player is the origin.
  Tests of a single area sweep await its executor, so a sustained channel cannot add a second contact during assertions.
  Gateway collections run sequentially by default; this retains all tests, assertions and in-test concurrency while
  avoiding unrelated catalogue-load/GC contention. Expect less overlap between test classes.
- **Auth and Patch are not watched.** Changes there still need `live.ps1 -Only Auth` or `-Only Patch`.

## What a good run looks like

`live-gateway.log`:
```
[gateway] CONNECT 127.0.0.1:…
[gateway] … opening: ticket=0x… digest=… address=0x7f000001 u32=0 realm=0x40 flag=1
[gateway] … => 0x0011 (start encryption)
[gateway] … => 0x001d AreaInstance { InstanceId = 1, AreaHash = 9052, Seed = 1, … }
[gateway] … <= 10 bytes: 007D43E6886D8EA8DD43
[gateway] … => 0x0022 local player 0x1000: 002200001000
[gateway] … => 0x02b9 create PlayerObject { Name = DevWitch, … } type=0x9c41993e: 02B9…
[gateway] … => 0x00c0 inventory 1 row 0: …            (× 117)
[gateway] … => 0x003d passive slots 0-2: 003D00000000 1C0000
[gateway] … => 0x0021 enter area: 0021…
[gateway] … <= 7 bytes: 001B00…                       (repeating: in game)
```

`Client.txt`: "Got Instance Details from login server" → "Connecting to instance server at 127.0.0.1:6112" →
"Client-Safe Instance ID = 1" → "Generating level 13 area "1_1_town" with seed 1" → tile/doodad hash → "[SCENE] Set
Source [Lioneye's Watch]". Then the HUD appears.

## Troubleshooting

| Symptom | Meaning |
|---|---|
| "Checking resources..." for a long time, with "Queue file to download" in `Client.txt` | a client not started by `live.ps1`, or a patch server advertising a real CDN. **Stop the client** |
| "Buffer Underflow" dialog | the 0x2b9 blob is shorter than what the client reads. Closing the dialog ends the client. Trace with `trace-player-blob.js` |
| `WerFault.exe` appears / window masked in screenshots | client crash. Event log id 1000 has the fault offset (VA − 0x140000000). Attach Frida before Play next time |
| "Area reported by instance does not exist" | the 0x14 area hash is not a WorldAreas.HASH16 |
| client back at the login screen, "An unexpected disconnection occurred" | the instance connection closed (Gateway restarted or crashed) |
| nothing after 0x7d | 0x21 not sent (`Game_GATEWAY_ENTER` unset). The world queues 0x2b9 and waits |
| extra map icons with no label (e.g. three area transition icons at the town gate) | the client's **minimap cache** remembers icons of objects it has seen, per area, in `%LOCALAPPDATA%\Path of Exile\Minimap\` (hash-named, encrypted files). Moving an object around (dev commands, changed defaults) leaves its old icons there, across restarts. Close the client and move the files away; seen 2026-10-17 |

Window position and size change between launches; take a screenshot before clicking. At half scale, the password
field, character row and Play button were near (245, 217), (375, 61) and (455, 263) in one layout and moved in others.

## Env vars

| Var | Default | Used by |
|---|---|---|
| `Game_PORT_BASE` | unset | `live.ps1` (like `-PortBase`): every port below from one base |
| `Game_CLIENT_SESSION` | unset | `live.ps1 -ClientOnly`, `client_control.py` (like `-Session` / `--session`) |
| `Game_LOGIN_PORT` | 20493 | Auth |
| `Game_GATEWAY_PORT` | 6112 | Gateway, and Auth (where Play sends the client) |
| `Game_AUTH_CONTROL_PORT` | 20495 | Auth and Gateway |
| `Game_GATEWAY_ENTER` | unset | Gateway: `1` sends 0x21 |
| `Game_INSTANCE_SECRET` | `data/instance-secret.bin` | Auth and Gateway (must match) |
| `Game_SAVES_DB` | `data/saves.db` | Gateway, Auth, Host |
| `Game_PATCH_TCP_PORT`, `Game_PATCH_URL`, `Game_PATCH_WEB_ROOT` | 12995, http://0.0.0.0:8080, http://127.0.0.1:8080/ | Patch |
| `Game_CDN_PASSTHROUGH` | unset | Patch: `1` proxies to the real CDN (only to repair data on purpose, with a backup of `Client/`) |
