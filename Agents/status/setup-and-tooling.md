# Status: Setup, tooling and tests

Part of [Status](../Status.md); what to build next in this area is in
[roadmap/setup-and-tooling.md](../roadmap/setup-and-tooling.md).

## Works (verified with the real client)

- Game data extracted from the GGPK in one step: `scripts/dev/extract-data.ps1` (run by `live.ps1` on every start,
  `-NoExtract` to skip) regenerates each data file that is missing or whose manifest version changed (stamps in
  `data/extract-stamps.json`), and the Gateway warns once at startup about any missing file.
  Code: `scripts/dev/extract-data.ps1`, `GameDataFiles.cs`.
  Docs: [GettingStarted](../GettingStarted.md) step 7.
- Local patching server: the client never contacts the official patch servers.
  Code: `GameServer.Patch`.
  Docs: [01](../docs/protocol/01-login.md) §1, [GettingStarted](../GettingStarted.md) step 6.
- Dev account service: register, log in, create characters, list every account (JSON-lines over TCP).
  Code: `GameServer.Host`, `GameServer.DevClient`.
  Docs: [dev-server.md](../docs/dev-server.md).
- Hooks for outside tools such as the desktop launcher: `live.ps1 -Stop` (also with `-Only` or `-NoClient`),
  `live.ps1 -ClientOnly -Walkmaps` (the client and its walk-map recorder against running servers), the
  Gateway's `commands` and `values <kind>` modes (its chat commands, their argument kinds and values, as JSON lines),
  and the dev host's `accounts`.
  Checked on a real setup: `-Stop` ending a watched Gateway with its `dotnet watch`, every argument kind's values, and
  the account list from the real save database.
  Code: `scripts/dev/live.ps1`, `Program.Completions.cs`, `ChatCommands.cs`, `GameSession.cs`.
  Docs: [GettingStarted](../GettingStarted.md) step 10, [10](../docs/protocol/10-chat-and-commands.md) §4,
  [dev-server.md](../docs/dev-server.md).
- Gateway hot reload, on by default (`live.ps1 -NoHotReload` turns it off): the Gateway runs under `dotnet watch`,
  which applies saved code edits to the running server; `HotReload.cs` logs each applied edit and tells players in
  chat when a save starts a reload, when it is applied, when it fails to compile (first three errors, from
  `dotnet watch`'s output), when it changes nothing, and when the edit needs a restart.
  Live, with the client in game and no disconnect: Gateway edits applied in 9-18 s (the Burning Arrow fix among
  them), a Core edit in 0.08 s, two files saved together as one reload, a broken save kept the old code and listed its
  errors (5 shown as 3 + "2 more"), and the fix applied.
  A removed member restarted the Gateway as expected; switching to and from `-NoHotReload` stops a Gateway that is
  still loading. Parsing of the error lines is unit-tested (`HotReloadTests`).
  Code: `scripts/dev/live.ps1`, `HotReload.cs`.
  Docs: [GettingStarted](../GettingStarted.md) step 10 (hot reload), [live testing](../docs/dev/live-testing.md).
- Client control distinguishes explicit password login from a remembered native form submission.
  Password replacement clears the outgoing saved-credential flag and retains the Remember Password preference.
  A remembered-form regression reaches character selection through normal Auth; saved-login preservation is mocked.
  Code: `scripts/dev/client-control.js`, `scripts/dev/test-client-login-writer.cjs`.
  Docs: [client control](../docs/dev/client-control.md).

## Partly working

- Mods: the Gateway loads each `Mods/Binaries/<id>/` (a `mod.json` and an assembly, after the mods it depends on) into a
  collectible load context of its own and reloads it, with its dependents, when its folder changes; `/mod`, `/mod_reload`,
  `/mod_enable` and `/mod_disable` in chat (the last two last across restarts, in `mod-state.txt`).
  Loose `.cs` files in a mod's folder compile with Roslyn when it loads, so saving one reloads it; errors go to `/mod`, the log and chat.
  Live: a script saved into a running Gateway's `Mods/Binaries/` loaded, and a broken edit reported its line.
  Leagues ([modules](../docs/dev/modules.md)) are folders in `Leagues/`, read once at start: cards and check boxes as in the real create screen, a parent to migrate to, Void, and
  modules listed per league, with `Modules/Core/` by default. Characters of a deleted or ended league cannot be entered.
  Not tried in the client: a blocked character.
  Harvest, Heist and Delve are real modules with their own assemblies, which hot-reload, through `IPlayerWorld`.
  Trades and parties, Atlas passives, the crafting bench, cosmetics, vendors, portals and the map device, tattoos, Mercy Mission, quest objects, hideout editing, chat commands and unique belts are Core modules too. What an NPC's options do is the `npcdialogue` module. A module runs only for the players whose league lists it, so a hideout
  without Harvest places no station. Not tried in the client after the move.
  `Mods/Source/` has six examples, of which only `example` is on by default.
  Mods see only `GameServer.Modding`: chat commands, the player tick, client messages (a mod may take one), area
  generated, monster killed, and drop, experience and stat modifiers; a mod that throws is logged and skipped.
  `live.ps1` builds `Mods\Source\` (the example mod: 100x item quantity, `/example`) into `Mods\Binaries\`.
  Live: the running Gateway loaded the example mod by itself when it was first built.
  Unit-tested (`ModHostTests`): every hook, two mods' modifiers together, a reload starting the state over, a
  dependency arriving later and reloading its dependent, a failed load leaving no hook, the watcher, and the unloaded
  context being freed.
  Assemblies load from a private temporary disk copy rather than collectible memory streams; original build outputs
  remain free, and collected contexts release their copied files. Loader and cleanup regressions cover this path.
  Unit-tested as well: scripts (load, reload, compile errors, no server references, dependencies, unload), league modules
  (fallback, required features, tunables, scoped hooks) and built-ins.
  Missing: mods replacing catalog rows, and the hooks not yet tried with the client (drops, experience, stats, packets).
  Code: `src/GameServer.Modding/`, `ModHost.cs`, `ModLoadContext.cs`, `ModScripts.cs`, `ServerLeagues.cs`, `Program.Mods.cs`,
  `Program.ModuleWorld.cs`, `Modules/`, `Mods/Source/`.
  Docs: [modding](../docs/dev/modding.md).

- Read-only passive graph tooling exports pinned 3.29.3b topology and a merged PSG as `data/` analysis artifacts.
  Parsing and merging have automated round-trip, malformed-input and ordinary-tree preservation coverage.
  The exporter refuses modified source graphs and never installs its output into the client archive.
  This is data tooling only; Gateway consumers and native integration remain later work.
  This standalone layer has no new native launch proof; client archive mutation requires separate agreement.
  Code: `PassiveSkillGraph.cs`, `AdditionalAscendancyGraph.cs`, `GameFileSystem.cs`, `scripts/re/passive-graphs`.
  Docs: [passive graph data guide](../docs/dev/passive-graph-data.md).

- Debugging aids: timestamped server logs, whole exceptions with stack traces (a failing packet handler names its
  messages), the reason on every `DISCONNECT` line, archived logs of the last 10 full runs, each connection's last 256
  packets (printed after an error or an unexplained disconnect, dumped to `logs\packets`, `/lastpackets`), `Game_TRACE`
  categories, `scripts/dev/bugreport.ps1` and `scripts/re/frida/trace-crash.js`.
  Unit-tested (`ServerLogTests`, `PacketHistoryTests`), the archive pruning and the bug report (logs held open, names
  in every form) checked with scripts; a live run that catches a client crash with the dumps and `trace-crash.js` is
  still to do, and so is checking that `trace-crash.js` reads the client's receive pump right.
  Code: `ServerLog.cs`, `PacketHistory.cs`, `Program.Connection.cs`, `scripts/dev/live.ps1`.
  Docs: [debugging](../docs/dev/debugging.md).

## Not working yet

Nothing yet.

## Tests

`dotnet test tests/GameServer.Core.Tests` (378 tests), `tests/GameServer.Data.Tests` (15) and
`tests/GameServer.Gateway.Tests` (376: currency on items (from the inventory or a stash tab), the currency tab's
crafting slot, gem levels, gem requirements (level and attributes) and the level stall, the level-up stopped between
two levels, the gem level-up and lock messages and the saved lock flag, quality and variants, stable gem skill ids,
the save database and its transactions, stash tab rules, Ctrl + click moves, item moves with failing saves, inventory
moves, stacks, the start weapon on the ground, flasks, arrival points, instance ground items and monsters, quests,
passives, spell suppression, attack speed, critical strikes, accuracy, armour/evasion, item mods, auras, damage
conversion and the account stash) pass.
Tests that need the real client pass quietly without `Client/`.

Teleport-name controls use an unmapped synthetic destination, so real WorldAreas data cannot change their labels.
Bow hit-recovery controls explicitly start their connection clock before checking input admission.
These fixture corrections leave production catalogs and the shared module initializer unchanged.
The Arcane Might area fixture measures range from the weapon-contact origin to its target.
Monster AI cadence tests run in a nonparallel collection: their real 300 ms socket-latency limit is unchanged,
but unrelated catalogue loads and GC pauses no longer run beside that measurement.
The per-target area accuracy regression awaits one completed sweep instead of racing a sustained Cyclone channel.
Gateway test collections now run sequentially by default to keep real socket deadlines and collectible mod contexts
away from unrelated catalogue-load/GC contention. Individual concurrency tests still run their own concurrent tasks.

## Tooling

Everything used to work out the protocol is in the repository: the Ghidra, Frida and Python scripts in `scripts/re/`,
our own tools in `Tools/`, and how to install the third-party ones in [GettingStarted](../GettingStarted.md)
("Third-party tools").
The full reverse-engineering setup has not yet been rebuilt from a fresh clone;
if something is missing, fix the guide.

Every network message of the client is listed, in both directions, generated from the binary.
S→C: [schema.md](../docs/protocol/schema.md), the four factories (instance 272, world 63, login 27, patch 6 classes),
each equal to its factory switch's explicit cases.
C→S: [c2s.md](../docs/protocol/c2s.md), instance 352, login 28 and patch 4 ids, from every caller of the stream flush.
Its exact lengths match all 61 comparable ids of the Gateway's `KnownClientLength`.
Code: `scripts/re/ghidra/ReadSchema.java`, `SendSites.java`, `scripts/re/schema_md.py`, `c2s_md.py`.

Optional [native client control](../docs/dev/client-control.md) provides CLI/JSON RPC for development without repeated
UI input.
It targets the copied standalone 3.29.3b client and local services only.
Login keeps waiting within its existing timeout when success 0 precedes authenticated state 3.
Mocked execute/RPC regressions cover this wait, rejection 6 and timeouts; this fix has no new native verification.

## Doc caveats

`docs/re/003`–`004` are early notes with known errors (for example "login is HTTPS"; it is custom TCP, see `re/007`).
Treat them as history, not reference.
