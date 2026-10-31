# Reverse-engineering tooling

What was used to map the client, how to run each tool and what it is good for. Paths are relative to the repo root.
The client binary is `Client/Game.exe` (79 MB, image base `0x140000000`). `Client/Game.patched.exe` is the same
apart from the login key blobs, so addresses are the same in both.

## 1. Ghidra (headless, read-only)

A previous fully analyzed project was stored at `Tools/ghidra-projects`, project `poe3293`, program `PathOfExile.exe`.
It and the full dump below are local, ignored artifacts and may be absent in a fresh clone or another worktree.
Verify their presence and binary identity before relying on them.
When present, do not rename the project or program: the existing runners use those names.
An earlier REA full Ghidra import exceeded its one-hour limit.
For bounded offline questions without that project, use the selected-function workflow below.

Runner: `bash scripts/re/ghidra/g.sh <Script.java> <args>` (about 12–15 s per call, `-noanalysis -readOnly`). It sets
`JAVA_HOME` to `Tools/jdk-21`, takes scripts from its own directory (so a worktree's scripts run as they are) and prints
only ERROR/Exception/SCRIPT lines (`GLINES=<n>` for more). Arguments go through `cmd.exe`, which splits at `,` and `=`. Output paths must be Windows paths
(`C:\…\Tools\re\out\x.c`). PowerShell variants: `scripts/re/ghidra/decomp.ps1`, `run.ps1`.

| Script | Usage | Use it for |
|---|---|---|
| `Decomp.java` | `<out> <addr[:depth]>…` | decompile the function containing each address, with `:N` also its callees to depth N |
| `DecompAll.java` | `<outdir> [threads] [limit] [indexonly]` | the full dump below (every function + `index.tsv`) |
| `Range.java` | range in `Tools/re/out/range.txt` ("lo hi") → `range.c` | decompile every function in an address range (class methods sit together) |
| `Callers.java` | `<out> <symbol\|0xaddr>…` | every function that references a symbol (import, thunks followed) or an address, data refs included. Used for imports (`connect`, `send`), strings, pools, setters |
| `FindSenders.java` | `<out> <disp>…` | every function using a displacement, with the ids passed to `htons`. Found login senders (`0xdd0`) and state writers (`0xc3a0`) |
| `FindHtons.java` | `<out> <id>…` | functions calling `htons(<id>)`. Found nothing for instance messages, whose ids are stored in the message object |
| `RefsIn.java` | `<out> <start:len>…` | references into a range: who installs a vtable or touches a blob |
| `Refs.java` | names or addresses in `Tools/re/out/rx.txt` | functions referencing each |
| `Syms.java` | `<out> <regex>` | symbols matching a regex |
| `ComponentRegistry.java` | `<out.json>` | the component registry (section 7) |
| `ReadSchema.java` | `<out.json> <components.json> [factory:a+b] [only:f+g] [withc]` | read-sequence summaries (section 7) |
| `SendSites.java` | `<out.json> [send:0xaddr/REG+…]` | every call of a send function (default the instance send `0x14216e940`, message in RDX) resolved over the CFG to its (vtable, id) pairs, path by path (section 7) |

### Full decompilation dump (grep before you run Ghidra)

`Tools/re/out/decomp-all/` (git-ignored, never commit it) holds the whole client decompiled, one file per function:
`<hex digits 3–5 of the VA>/FUN_<addr>.c`, e.g. `0x1e5123450` → `1e5/FUN_1e5123450.c`. Each file starts with
`// ===== <name> @ <addr> =====`; a function the decompiler gave up on (60 s timeout) has `// decompile failed: …`
instead of code. `index.tsv` has one row per non-thunk function: `addr size name callers status strings`, where
`callers` counts calling functions and `strings` lists the ASCII/UTF-16 strings the function references (directly or
through one pointer), separated by ` | `. `progress.log` records each run (counts, rate, failures).

Regenerate (about 1 h on 4 threads; resumable, it skips files that exist, so rerun the same command after a crash):

```bash
JAVA_HOME="$PWD/Tools/jdk-21" GHIDRA_HEADLESS_MAXMEM=4G cmd //c "Tools\ghidra_12.1.4_PUBLIC_20260921\ghidra_12.1.4_PUBLIC\support\analyzeHeadless.bat Tools\ghidra-projects poe3293 -process PathOfExile.exe -noanalysis -readOnly -scriptPath scripts\re\ghidra -postScript DecompAll.java C:\…\Tools\re\out\decomp-all 4"
```

Arguments: `<outdir> [threads] [limit] [indexonly]` (`limit` stops after N functions for a test; `indexonly` only
rebuilds `index.tsv`). Delete the directory first to redo everything.

The full run (111,248 functions, 183 thunks excluded) took 52 min on 4 threads and 4 GB of heap; 14 functions failed
(12 timeouts on 12–95 KB functions; `0x140f61a30` crashed the decompiler, `0x1413696c0` hit "Flow exceeded maximum
allowable instructions"). Search the `.c` files with ripgrep (the agent Grep tool,
seconds); `grep -r` from Git Bash over 111k files takes minutes. `index.tsv` is small enough for plain `grep`/`awk`.

Example searches (from the repo root, `D=Tools/re/out/decomp-all`; for the `.c` files the same patterns work in Grep/`rg`):

```bash
grep -F 'Metadata/Terrain' $D/index.tsv | cut -f1,3,6          # functions referencing a string (ASCII or UTF-16)
grep -rlF '+ 0x2b4) =' $D --include=*.c                        # writers of a field at displacement 0x2b4
grep -rF 'FUN_1410ddcfd(' $D -l                                 # callers of a function (calls in the C text)
cat $D/141/FUN_1410ddcfd.c                                      # one function, no Ghidra run
awk -F'\t' '$5=="failed"' $D/index.tsv                          # functions to decompile by hand (Decomp.java, longer timeout)
sort -t$'\t' -k4,4nr $D/index.tsv | head                       # most-called functions
```

The decompiler sometimes hides field writes in casts (`*(undefined4 *)(param_1 + 0x2b4)` vs `param_1[0xad]`), so
grep both forms or fall back to `finddisp.py` for the instruction level.

### Bounded damage audit

[damage_audit.py](../../../scripts/re/damage_audit.py) reads original PE bytes without loading or running the client.
It pins the executable SHA256 used in [client damage research](client-damage-calculation.md).
It follows `.pdata` `UNW_FLAG_CHAININFO` to the owning function and preserves each separate body range.
Do not disassemble the enclosing address span across split-function gaps.
The optional leaf ranges are explicitly analyst-reviewed boundaries, not automatically recovered functions.

Use existing pefile/Capstone installations; `--python-libs` can select an existing dependency directory.
Run with `-I -B`: a neighboring `scripts/re/dis.py` otherwise shadows Python's standard `dis` module.
The output must be a **new** directory under the current checkout's `Tools/re/out/`.
Pass explicit `--exe` and `--stats` paths when a worktree reuses the primary checkout's large files.

```powershell
python -I -B scripts/re/damage_audit.py `
  --exe Client/Game.exe --stats data/tables/Stats.json `
  --out Tools/re/out/client-damage-audit/reproduction `
  --functions 0x1424cfa40 0x1426da000 0x141e6a6b0 0x141e69fb0 `
  --leaf-ranges 0x1424cfa40:0x1424cfac2 `
  --call-refs 0x141e6a6b0 --stat-refs 2023 3356 `
  --pointer-refs 0x141e6a8a0 --virtual-offsets 0x48 `
  --rip-refs 0x14363bd90
```

`audit.json` contains input hashes, original range offsets/hashes, instruction records and bounded scan results.
`targets.json` supplies exact function bodies and direct-callee context to
[BoundedFunctions.java](../../../scripts/re/ghidra/BoundedFunctions.java).
The script decompiles only selected functions, with a 30-second bound per function, under Ghidra `-noanalysis`.
An example using the repository's conventional tool locations is:

```powershell
$env:JAVA_HOME = (Resolve-Path Tools/jdk-21).Path
$auditDirectory = (Resolve-Path Tools/re/out/client-damage-audit/reproduction).Path
& Tools/ghidra_12.1.4_PUBLIC_20260921/ghidra_12.1.4_PUBLIC/support/analyzeHeadless.bat `
  Tools/re/out/bounded-project DamageAudit -import Client/Game.exe -noanalysis `
  -scriptPath (Resolve-Path scripts/re/ghidra).Path `
  -postScript BoundedFunctions.java "$auditDirectory/targets.json" "$auditDirectory/decompilations.jsonl" `
  -deleteProject
```

Create the project parent first, and use installed tool/input paths if those conventional locations are absent.
On this Windows Ghidra version, the **project directory** cannot have a path component starting with `.`.
A project below `.codex` fails; choose an ignored project directory under the primary checkout instead.
Source scripts and output files may still reside in the worktree.
This stores a disposable analysis project, not a CI worktree or a server deployment.
Do not start whole-program autoanalysis as an implicit fallback.

Search limitations are part of the result:

- E8/E9 scans cover direct calls/jumps at decoded instruction boundaries, not register or virtual targets.
- `mov edx, key` is a candidate; allocation sizes and action IDs can match stat keys.
- The FF displacement scan covers selected indirect memory call/jump encodings, not receiver identity.
- RIP-reference search covers RIP-relative LEA, not every computed or loaded reference.
- Pointer occurrences alone are not proof of a vtable, an installed object or runtime reachability.
- Unwind-less candidate positions stay unresolved unless independently bounded.
- Ghidra can omit floating-point arguments/returns, notably XMM0 overflow from pool loss reducers.

Keep complete generated code and machine output ignored.
Commit concise findings, selected anchors and the metadata-only evidence manifest, never the client or full dumps.
The offline records are not REA-issued Evidence IDs and do not establish official-server execution.

Earlier decompile output is kept in `Tools/re/out/*.c` (untracked). The files used this session: `inst*.c` (instance
connection), `inst11.c` (the whole instance dispatcher, 5275 lines), `inst10.c` (instance factory), `obj1.c` (world
handler), `deser*.c` (component deserializers), `m*.c` (message classes), `gor*.c` (object type register).

## 2. Python helpers (`scripts/re/*.py`, need `pefile` and `capstone`)

| Script | Usage | Notes |
|---|---|---|
| `fn.py` | `<va>… [--callers] [--callees] [--show]` | the function that contains an address (exact bounds from the PE's `.pdata`), its file in `Tools/re/out/decomp-all`, the functions that name it / it names in the decompile, its code. Start here with a trace's caller VA. Index `Tools/re/out/fn-index.json` (local): the first run builds it (~15 min, reading every decompiled file), later lookups take under a second |
| `dis.py` | `<va> [before] [after]` | quick disassembly with RIP-relative string hints. Starting `before` bytes back can land mid-instruction and print nothing; use 0 or a function start |
| `xr.py` | `<string>` | find a string (ASCII/UTF-16) and its code xrefs. Slow on short, common strings; use `strexact.py` + `Callers.java` instead |
| `strexact.py` | `<name>…` | exact NUL-terminated ASCII (`a:`) and UTF-16 (`u:`) occurrences |
| `vt.py` | `<vtable>…` | slots 0–5 of a vtable |
| `scanvt.py` | `<func>` | every vtable whose slot 1 is `<func>` (with `0x14299e300` it lists the network handler groups) |
| `finddisp.py` | `<lo> <hi> <substr>` | disassemble a range and show the instructions before each operand match, e.g. `"+ 0x980]"`. Found field writers the decompiler hid |
| `statkeys.py` | `<key>…` | client code that reads a stat key (`mov edx, key` + call) |
| `dispatch_ids.py` | `[id…]` | instance dispatcher id → switch case (byte table `0x142154e14`) |

Run with `python -I` (isolated) and add `-X utf8` when the output may contain non-ASCII (`dis.py` string hints).

## 3. GGPK file dump (`scripts/re/ggdump`)

```bash
dotnet run --project scripts/re/ggdump -- "Client\Content.ggpk" "<outDir>" Data/Inventories.datc64 Metadata/Characters/Int/Int.ot
```

It uses `GameServer.Data`'s `GameFileSystem` (bundle index + bundles) and a Node-based Oodle decoder
(`Tools/ooz/ooz_worker.mjs`; the managed decoder lacks Selkie). The GGPK opens read-only and shared, so this works while
the client runs. The bundle index also stores every path (about 1.2 million): `list <regex>` as the first path prints the matching
ones (e.g. `dotnet run --project scripts/re/ggdump -- Client/Content.ggpk out list "beachtown_v2/"`). `.ot` files are UTF-16
with a BOM; `.datc64` files are rows followed by `0xbb×8` and the variable data section.

## 4. Frida through REA (live client)

REA's MCP tools: `start_frida_session` (mode `attach`, `pid`), `load_frida_script` (inline source), `frida_session_status`
(queued `send()` messages), `close_frida_session` (detach; it never kills the target).

Workflow per run:
1. `scripts/dev/live.ps1` (see [../dev/live-testing.md](../dev/live-testing.md)) → client pid
2. `start_frida_session {mode: attach, pid}` **before** logging in or Play
3. `load_frida_script` with `scripts/re/frida/common.js` followed by one of the trace scripts
4. log in, Play, then read `frida_session_status`

| Script | What it does |
|---|---|
| `common.js` | rebases Ghidra VAs (`at(va)`), formats addresses back (`va(p)`), reads the blob reader position (`pos(r)`) and installs `Process.setExceptionHandler` with an image-relative fuzzy backtrace |
| `trace-player-blob.js` | the 0x2b9 blob bytes, the created object's component vtables in order (`obj+0x10..+0x18`, each slot +0x88) and the reader position around each component deserializer |
| `dump-walkability.js` | hooks `0x1410ddcfd` and writes the area's terrain walkability (`walk-meta.json`, `walk-a.bin` = walkable mask, 5 = walkable; `walk-b.bin`) to `Tools/re/out/`. Used to pick the spawn ([../protocol/03-area-and-world.md](../protocol/03-area-and-world.md) §7) |
| `trace-pong.js` | the 0x1c read, the send-time queue pop and logger calls after the first 0x1c (used to find the HNC violation) |
| `trace-missing-files.js` | paths the file lookup `0x1421cc280` fails on (C++ exceptions thrown via `0x1421cc54e`) |
| `trace-http.js` | every outbound socket send reduced to a plaintext HTTP request line + Host, or the SNI of a TLS ClientHello, plus `connect` targets and `getaddrinfo` names — the shop's web endpoints without decrypting anything. `run.py <name> --follow` waits for a *new* process, so it can catch a client's startup traffic (found `web.poecdn.com` and the empty cosmetics panel) |
| `check-components.js` | any created object (0x2b9): per component the live vtable, its deserializer and the bytes it consumed, the leftover, and the component that was reading on a Buffer Underflow (section 7) |
| `trace-skills.js` | every skill added to an ActiveSkillManager (`SkillInstanceAdded` `0x141eea6a0`): id, action id, effect name, backtrace |
| `trace-skill-stats.js` | stat getter calls (key, value, caller) while a skill action is created or started |
| `trace-move.js` | server and client Positioned moves (`0x141e34400`): cell, rotation, height, duration, flag bytes |
| `trace-actions.js` | S→C 0x1ab on an Actor, the skill-usable check and the action created |
| `trace-action-start.js` | whether an action starts (0x141e8cf10) and the actor's current / queued actions |
| `trace-action-life.js` | how long actions stay current, and the attack-time stats' values |
| `trace-queued-check.js` | why the runner 0x141e8e060 drops a queued action |
| `dump-stats-object.js` | the stored (key, value) pairs of every stats object asked for one key (set `KEY`) |
| `find-string-refs.js` | standalone: finds a UTF-16 string (`NEEDLE`) in the client's writable memory, then the pointers to each hit. Asynchronous and slow (minutes over ~6 GB); found the tile objects' virtual `.ao` names. Wait for `done` before closing the session: closing waits for running scans, and a synchronous `scanSync` over all memory hung the session and lost the client |
| `trace-cluster-ids.js` | every cluster-jewel node id (`FUN_141f0e770`): the slot row, size tag, orbit position and id, plus the node constructor's (`FUN_141f114f0`) template index, start and total. Printed the measured id formula for tree-socket and nested cluster jewels |
| `trace-items.js` | every item the client reads (type hash, bytes), each item component's read and the bytes it consumed, and 0xc0 inventory reads ([../protocol/13-items.md](../protocol/13-items.md)) |
| `trace-flask-use.js` | 0x263 item-use reads, the two native use callbacks, the sound path they pick and its audio submission: what a flask press's confirmation actually plays (13-items.md §8) |
| `trace-flask-mana.js` | read-only flask/mana probe: outgoing 0x24/0x25/0x70, the client's mana pool per change and every 0x263 result — used to find that the client pays for attack-moves the server only walks |
| `trace-enter-area.js` | milestones (SetLocalPlayerId, EnterArea, WorldActivate, CreateObject, UI `0x140785160`), inventory registrations, passive slot creation and **inventory lookups that return null** (with the row and caller) |

Notes:
- An exception message with `rdi = 0x19930520` and a `RaiseException` frame is a C++ throw. `rbx`/the second arg is the
  throw info (e.g. `0x143a50ab0` = the blob reader's out-of-range exception).
- `Thread.backtrace(…, FUZZY)` also lists stale return addresses. Read it together with the disassembly.
- Returning `false` from the exception handler lets the client continue normally (dialog or crash).

### Level generation trace

`live.ps1 -Walkmaps` also traces the level generator (every random draw and the placed tile grid) into
`data/walkability/<area>_seed<seed>.gen.json`; `dotnet run --project scripts/re/levelgen -- hash <file>…` checks the
tile hash. Traces also hold `graphs`, the tile graph at the layout's stage boundaries (`hookGraphStages`); `levelgen
generate` compares the port's graph with them stage by stage; `relaxRecords` holds the relaxation's placement
records. See [011-level-generation.md](011-level-generation.md) §8.

## 5. Windows crash data

Without Frida, a crash leaves only the Application event log (event 1000: exception code and fault offset, i.e.
`VA - 0x140000000`). WER kept no dump. Enabling LocalDumps would need a registry change (HKLM), which is the user's
call. There is no `cdb`/WinDbg installed.

## 6. Recipes that worked

- **Message layout:** find the case in the dispatcher (`dispatch_ids.py`), read the factory case for the class ctor,
  `vt.py` the vtable, then decompile slot 1 (write: wire order) and slot 2 (read).
- **Who sets field X:** `FindSenders.java X` (displacement users), then `finddisp.py` over the candidates for
  `mov … [reg + X], …`.
- **Component class from a name:** `strexact.py Name` → `Callers.java` on the ASCII address → the static initializer in
  `0x14006xxxx` (`lea rdx,name; lea rcx,pool`) → `Callers.java pool` → factory → ctor vtable. Then confirm with
  Frida, because live objects can use other vtables.
- **A portal object after the fact:** `python -I scripts/re/portal_capture.py` decodes every `0x02b9` map portal and
  `0x008f` portal list in `logs/live-gateway.log` (to `Tools/re/out/portals.json`): position, place index, portal type,
  open flag, the `0x008f` entry's area, portal variation (+0x26) and owner. The Gateway caps a logged hex dump at 96
  bytes, so a six-portal list and a long `.ao` path arrive partial (the capture marks them `truncated`; an effect's
  `loggedAo` from the log line is the complete path).
- **What a crash needs:** Frida backtrace → decompile the faulting function → find the null field → find its writer
  → find the message that calls that writer.

## 7. Schema generators (objects and messages without live trial and error)

| Step | Command | Output |
|---|---|---|
| Component registry | `bash scripts/re/ghidra/g.sh ComponentRegistry.java 'C:\…\Agents\docs\re\components.json'` (40 s) | [components.json](components.json): every `0x140e33c20` registration → pool → users (factory) → every vtable they install, subclasses, slot 17/20, a `primary` flag (factory's own vtable and its subclasses) |
| Registry doc + Frida tables | `python -I scripts/re/gen_components.py` | [components.md](components.md), the `VT`/`DESER` tables in `frida/check-components.js` |
| Component order per type | `python -I scripts/re/ot_components.py --check --md Agents/docs/re/object-types.md` (any `Metadata/…` paths print their list; `--all --json <file>` does every type in the dat tables; `--items --json data/item-types.json` does every BaseItemTypes row from the `.it` files) | [object-types.md](object-types.md). `--check` compares with the two traced objects and exits 1 on a mismatch |
| Read sequences | `bash scripts/re/ghidra/g.sh ReadSchema.java 'C:\…\Tools\re\out\readschema.json' 'C:\…\Agents\docs\re\components.json'` (about 10 min), then `python -I scripts/re/schema_md.py` | [../protocol/schema.md](../protocol/schema.md): every primary deserializer and every message class of the four S→C factories (instance, world, login, patch; each list equals its factory switch's explicit cases), validated against the traced byte counts (exit 1 on a mismatch). The decompiled C stays in `Tools/re/out/readschema.json`; the script publishes only our notation and exits with an error if any C is left |
| C→S messages | `bash scripts/re/ghidra/g.sh SendSites.java 'C:\…\Tools\re\out\sendsites.json'` (15 s), then `python -I scripts/re/c2s_md.py` | [../protocol/c2s.md](../protocol/c2s.md): every client → server message of the instance, login and patch connections with its class, writer, length and senders. Senders outside `0x14216e940` are a hand table in the script. Exit 1 if an exact length disagrees with the Gateway's `KnownClientLength` |
| Live check | `common.js` + `frida/check-components.js` (section 4) | bytes per component of every object the server sends |

Rules the generators rely on (all from the code, see the script headers): the object-type constructor `0x141ec1360`
creates Positioned first; `.ot` sections merge parent-first with the first position kept; `type+0x80` components (the
non-client ones) are deserialized; live objects often use a subclass vtable (`ClientPlayer`, `ClientActor`,
`ClientAreaTransition`, `ClientInternalStats`). To build a new object: take its type's list from `ot_components.py`, find
each component's live vtable (★ in components.md, or `check-components.js` on a client-created object), and write the
bytes its summary in schema.md asks for; then confirm with `check-components.js` that every component consumed what
the server meant and `left` is 0.

## 8. Path of Building (formula reference)

For damage/defence rules that are not visible in the client tables, Path of Building is the reference (not a runtime
dependency and never committed): clone `https://github.com/PathOfBuildingCommunity/PathOfBuilding` into the
git-ignored `Tools/PathOfBuilding` (the clone used for this work is commit `16de4b8`). Resistance floor, caps and the
final clamp come from `src/Modules/Data.lua` (`ResistFloor = -200`, `MaxResistCap = 90`), `src/Data/Misc.lua`
(`base_maximum_all_resistances_% = 75`) and `src/Modules/CalcDefence.lua` (`final = max(min(total, max), min)`).
Our `DamageModel` cites those files; keep the clone local and pin the commit when a formula is copied into the server.

**Caps and floors:** always verify a cap/floor in the pinned PoB clone and cite the exact file and commit; never
assume the value from the client panel or from memory. Examples already in `DamageModel`: `ResistFloor = -200`,
`MaxResistCap = 90`, default maximum resistance 75, `SuppressionChanceCap = 100`, `SuppressionEffect = 40`.
