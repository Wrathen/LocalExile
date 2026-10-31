# Server performance: how to measure, and what was measured

Several server stacks (Auth, Patch, Gateway) often run side by side on one machine, one per worktree or agent, so a
stack's startup time, its resident memory and its CPU per busy instance all count. This note gives the repeatable
measurements and the results of the 2026-10-30 pass (branch `contrib/server-perf`).

## Measuring

**Startup and idle memory**: `scripts/perf/startup.ps1` (after `dotnet build`). It starts the built Auth, then the built
Gateway, on a private port block (`-PortBase`, default 23000: login +1, Auth control +2, Gateway +3, dev +4) with a copy
of `data\validation-saves.db`, times each to its "listening" line, waits `-SettleSeconds`, records private bytes,
working set and the GC heap and committed size (through `dotnet-counters`, when `artifacts\tools` has it:
`dotnet tool install dotnet-counters --tool-path artifacts/tools`), and stops only the processes it started. Run 1 is a
stack's first start (the Gateway seeds the game-data reference tables into the database); the medians use the
restarts. `-Env @{ NAME = 'value' }` tries runtime settings (`DOTNET_GCConserveMemory`, …). It never binds the default
ports and never starts a client.

**In-process scenarios**: `PerfBenchmarkTests` in the Gateway tests, which only run with `Game_PERF=1`:

```
$env:Game_PERF = '1'; $env:Game_PERF_LABEL = 'my-change'
dotnet test tests/GameServer.Gateway.Tests -c Release --filter FullyQualifiedName~PerfBenchmarkTests.Area_instance_creation
```

Run one test per process (catalogues load once per process). Results go to the test output and, as JSON lines, to
`artifacts/perf/bench.jsonl`.

| Test | What it measures |
|---|---|
| `Catalogue_loads` | the first-player loads in a fixed order (Program's statics, stats, items, skills, the skill-type sets, monsters, packs, area transitions, waypoints, the passive tree): time, allocation and what each retains |
| `Area_instance_creation` | six supported areas at seeds nobody asked for before: the layout (server level generation, `AreaLayouts.For`) and the monster population over a loopback connection; the GGPK caches and the heap afterwards. `Game_PERF_MORE_AREAS=N` adds N more layouts, as a long session visits them |
| `Busy_instance_ticks` | The Coast with 60 monsters of its pack kinds (and a Sand Spitter, for projectiles) around a walking player and 140 idle ones farther away; the real `RunMonsterAi` loop with swings, contacts and projectiles, every packet encrypted and sent. Process CPU per game second, measured after a 5 s warm-up (a fresh process first spends seconds in tier-1 JIT) |
| `Monster_planner` | `PlanMonsterAi` alone over the same crowd, 2000 steps: time, process CPU and allocation per step |

The busy instance is the same in every run (fixed placement, no buried monsters, which rise on a wall-clock watch);
`PopulateArea` is not, because it seeds placement with the process's randomized `HashCode`. Timings on a shared
machine vary by ±20 %: compare several runs, and prefer allocation, which is exact.

**Profiles**: set `DOTNET_EnableEventPipe=1`, `DOTNET_EventPipeOutputPath=…\trace-{pid}.nettrace` and
`DOTNET_EventPipeConfig=Microsoft-DotNETCore-SampleProfiler:0:5,Microsoft-Windows-DotNETRuntime:0x4c14fccbd:5` for a
test run: CPU samples and allocation ticks with stacks (read them with TraceEvent or `dotnet-trace report`).
`Game_PERF_HOLD=1` keeps the area test's state alive for `dotnet-gcdump collect`.

## Results (2026-10-30, this branch against origin/main 89b7aa90, same machine)

| Measure | Before | After |
|---|---|---|
| Gateway private bytes after startup (Debug, as `live.ps1` runs it) | 818 MB | 413-433 MB |
| Gateway GC committed after startup | 702 MB | 387-408 MB |
| Gateway ready (to "listening") | 2.4 s | 2.4-2.5 s |
| Auth ready / private bytes | 0.3 s / 36 MB | unchanged |
| First area layout (opens the GGPK) | 3.3 s, 13.2 GB allocated | 1.2 s, 0.49 GB |
| Area layout 1_1_3 / 1_1_4_1 / 1_1_5 / 1_1_7_1 / 1_2_1 | 0.80 / 1.83 / 1.27 / 1.39 / 1.73 s | 0.41 / 0.57 / 0.54 / 0.35 / 0.22 s |
| … allocated per layout | 4.4-8.0 GB | 84-166 MB |
| First area population (the monster catalogues load) | 0.95-0.97 s, 414-477 MB | 0.83-0.87 s, 357-431 MB |
| Catalogue loads, Program statics | 1.37 s, 816 MB allocated | 1.17-1.27 s, 572 MB |
| Skill-type sets of a skill catalogue | 0.30 s, 105 MB allocated | 0.11 s, 13 MB |
| `IsRangedAttack` (three type sets) | 200 ns, 133 B per call | 33 ns, 0 B |
| GGPK bundle cache ceiling (per Gateway) | 1 GiB | 128 MB (+ 64 MB decompressed chunks) |
| Generated levels kept (46-area session) | 24, 265 MB of grids | 8 most recently used (103-116 MB), plus any level a connection still plays |
| Levelgen gate, 122 checks, 2 workers | 30 s | 9 s (same results) |
| `PlanMonsterAi`, 200 monsters, 60 chasing (Release) | 1.34-1.37 ms per step | 1.04 ms (`MonsterSurround.Assign` 0.96 -> 0.48 ms) |
| Busy instance CPU (Release; Debug ~1.6×) | 78-160 ms per game second | within that noise |

What changed: `BundleFile.DecompressSlice` copied every chunk of a bundle for each file read (88 % of a level
generation's CPU); `GameFileSystem` keeps the last 64 MB of decompressed chunks and serves a file whose chunks are all
cached without its bundle, so neighbouring terrain files do not go through the Node Oodle worker again; the Gateway asks
for a 128 MB bundle cache and keeps 8 generated levels for re-entries (a layout in use holds its level, so the
projectile and height lookups never regenerate it, and they take no lock); one aggressive full collection after the startup loads returns
the memory their parsing left committed; `AreaLayouts.DeepestCell` no longer allocates an array per cell;
`SkillCatalog` parses the skill-type tables once and resolves each set once; `GameTables` shares the parse of
Stats.json, Mods.json, MonsterVarieties.json and GrantedEffects.json among the catalogues that load them in one burst;
`MonsterSurround.Assign` checks a refused cell once per assignment.

Tried and dropped (no measurable effect): `DOTNET_GCConserveMemory` (5, 7), `DOTNET_GCgen0size`,
`DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0`, shift-based bucket math and span loops in `BlockerSet.Query`, skipping
the pathfinder for a swing's stay-in-place order (they are a few per step and the search ends at once), skipping
duplicate ring cells and caching ground checks in the surround search, the managed Oodle decoder (it cannot decode
Selkie, which the terrain bundles use). Moving the startup collection before the Auth notification (and so before the
mods load) left the Gateway at 494 MB instead of 413 MB.

Measuring memory in-process: `ArrayPool<byte>.Shared` keeps the buffers `JsonDocument` rents, so the live heap after a
full collection varies by a few hundred MB between identical runs; compare sizes the benchmarks read directly (caches,
kept levels) or the server's own counters.

## Recommendations (tooling decisions, not made here)

- **Release builds for stacks that do not need hot reload.** `live.ps1` runs Debug builds under `dotnet watch`; the JIT
  then does not optimize our assemblies: the planner is 2.3× and a busy instance ~1.6× slower than in Release. An
  agent's test stack or a long validation run could start the Release build (`-NoHotReload` already skips the watcher).
- **One Oodle decoder per machine.** Level generation decodes Selkie chunks through one `node` process per Gateway
  (~130 MB private each). A shared decoder service (as levelgen's `serve` already shares the game files) would save that
  per stack; with the chunk cache it is used far less often than before.

## Where the remaining cost is

- **Table parsing.** Most catalogues still parse whole JSON tables with `JsonDocument` (~190 sites); WorldAreas.json is
  read by nine loaders (Auth alone four times at startup, ~0.1 s). Reading the biggest ones with `Utf8JsonReader` into
  compact rows would lower the peak memory of the first loads. Several `JsonDocument`s open at once keep their pooled
  buffers: parse one at a time.
- **Monster AI.** `PlanMonsterAi` is still ~1 ms per step for a crowd of 60: half in `MonsterSurround.Assign` (the
  free cells' reservation queries), the rest mostly `WalkEstimator.Order`'s client-pathfinder searches.
