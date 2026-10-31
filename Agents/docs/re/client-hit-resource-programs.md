# A packaged Hit listener program: Ferrofluid armour (3.29.3b)

The Ferrofluid intelligence-armour controller supplies a concrete use of
`AddOnWhenHit`: its client attachment handler registers a captured block that
removes and adds the same hit EffectPack. Its detach handler removes that pack
and the named task. This closes **one selected packaged source → actual listener
API → captured program** edge left open by
[the native Hit-listener report](client-hit-script-listeners.md).

It does not identify a numerical damage program, prove that a live character
loaded this attachment, or recover actual packaged uses of `AddOnWhenOtherHit`,
`AddOnDealHit` or `AddOnOtherDealHit`. Their shared API machinery must not be
mistaken for an executing program on every hit.

## 1. Exact source and inheritance

Five selected members were freshly extracted read-only from the available
indexed archive. This is a selected packaged-resource snapshot; it is not a
pristine-archive or runtime-loaded-resource claim. The four `.ao` declarations
form this complete parent-first chain, terminating at `extends "nothing"`:

```text
Metadata/Skin.ao
  → Metadata/SkinBodyArmour.ao
    → Metadata/Items/Armours/BodyArmours/FerrofluidArmour/controllers/FerrofluidArmourInt.ao
      → Metadata/Items/Armours/BodyArmours/FerrofluidArmour/FerrofluidArmourInt.ao
```

The outer armour `.ao` declares its controller as parent. The selected controller
declares the following scripts inside an explicit `client` block; neither
ancestor supplies a competing declaration for these fields. The inventory
preserves each source, component path, mode and block independently, without
inventing a universal merge policy for all component fields.

| Controller declaration | Logical source lines | Supplied program |
|---|---:|---|
| `client.BaseAnimationEvents.on_attachment_chain_complete` | 13–22 | If `parent_ao.IsActor()`, call `parent_ao.AddOnWhenHit` with the three arguments below |
| `client.BaseAnimationEvents.on_detached` | 8–11 | `Call(RemoveEffects)` |
| `client.Functions.RemoveEffects` | 27–31 | Remove the hit EffectPack from `parent_ao`, then call `parent_ao.RemoveNamedTask(FerrofluidWhenHit)` |

The registration's actual arguments are:

| Position | Native option name | Actual resource value |
|---|---|---|
| 1 | `actions` | Captured block calling `RemoveEffectPack`, then `AddEffectPack`, with the same hit-pack path |
| 2 | `once` | Literal `false` |
| 3 | `task_name` | `FerrofluidWhenHit` |

The exact pack is
`Metadata/Items/Armours/BodyArmours/FerrofluidArmour/fx/hit_armour_int.epk`.
Its selected body contains one main `Append` render pass referencing
`hit_armour_int_epk_pass0.mat` in the same directory. This identifies the
referenced package as a render-pass resource. Material/rendering internals
and the success of live effect installation are intentionally outside this
research slice.

The source block requests effect-pack operations; it supplies no scalar damage
amount or explicit Life/Mana/ES deduction. This is a concrete presentation use
of the listener, not a classification of all accepted programs as harmless.

## 2. Typed compiler and capture boundary

Fresh original-byte checks bind the resource's three arguments to the native
factory, rather than inferring their meaning from call order alone:

| API pair | Argument splitter | Exact option table | Supplied argument count checked |
|---|---|---|---|
| `AddOnWhenHit`, `AddOnDealHit` | `0x1417fbd50` | `0x1435d0598`: `actions`, `once`, `task_name` | 1–3 |
| `AddOnWhenOtherHit`, `AddOnOtherDealHit` | `0x1417f9570` | `0x1435ccad0`: `other`, `actions`, `once`, `task_name` | 2–4 |

The primary splitter copies the required first token range into the `actions`
slot. The selected factory `0x14207ba10` passes that slot to `0x141de6420`
at `0x14207bab0..0x14207baba`. Its optional `once` slot is read at factory
construction through `0x1418034a0`; an omitted value defaults to false. That
helper accepts the checked Boolean token form or integer literal 0/1 and
rejects other integer values. The resulting byte is retained at callable
`+0x18`. The resource's literal false therefore supplies a construction-time
option, rather than a new per-hit context expression.

`0x141de6420` accepts the checked code-block token routes `0x1b` and `0x1d`;
its other/error routes retain the exact diagnostics “Expected code block in
argument” and “Expected end of argument after code block”. It allocates a
program through `0x141dfadd0`, feeds the selected block token range to
`0x141de5720` at `0x141de65f5`, and retains capture information at program
`+0x90`. The ordinary block compiler iterates through `0x141df4bb0` and appends
its terminating zero opcode. These are compiler dataflow checks, not an
execution of the extracted program or a dump of its emitted bytecode.

The named-call compiler `0x141df94b0` selects a matching native name/factory
before the separate metadata-only mode gate: the native-success branch at
`0x141df9674` goes to `0x141df97dc`. It continues through token splitting
`0x141dfbba0` and factory invocation `0x141df9930`; the latter calls the
selected entry's `+8` factory at `0x141df99e8` and appends its callable
descriptor with 0x48-byte stride. Thus the APIs are concrete native compiler
bindings, not unresolved metadata-only function names. The source's `client`
declaration and that native route are separate facts; they do not demonstrate
that any particular runtime instance compiled or executed it.

For subsequent signal delivery, retained owner/program, event qualification
and callback context, reuse the
[already verified runtime bindings](client-hit-script-listeners.md).
This report does not repeat or extend projectile result ingress.

## 3. Task name and detach cleanup

The factory resolves `task_name` through `0x1420b7940`. Its original literal
is `Data/GameObjectTasks.dat`, and the checked path performs the named-row
lookup through `0x1422281b0`. Extracted `GameObjectTasks.json` row **58**
(zero-based) contains `Id = FerrofluidWhenHit`, `HASH16 = 31815` (`0x7c47`).
The table SHA256 is
`5da7dca2fa7f07f433a5731e59d50f6a4f698c7fad736d919c4408355ccd2b9c`.

`RemoveNamedTask` is the native entry at `0x14449b260`, factory
`0x14209b000`. It uses the **same row resolver**, reads that row's word at
`+8`, and stores the key in its callable. Installed table
`0x1435d1d90` has invoke `0x1420d94c0` at `+0x10`; the invoke passes the
stored word and resolved Entity's task container `+0x58` to `0x141edf7e0`.

That helper selects the matching key range and, for eligible tasks, sets task
state bit 0 and invokes task virtual `+0x18`; it also retains the conditional
owner notification for state bit `0x20`. This establishes a native named-task
cleanup request. It does not equate the request with synchronous destruction,
prove when subscriber storage disappears, or establish a general immunity to
callbacks already in flight. The resource deliberately reuses the same name
in registration and detach cleanup.

## 4. Resource identity and verification

Paths below share the prefix
`Metadata/Items/Armours/BodyArmours/FerrofluidArmour/` where indicated.

| Resource | Bytes | SHA256 |
|---|---:|---|
| `Metadata/Skin.ao` | 282 | `f3451168114fe38fe74bdfbc00ee04d22996904bb302079ab6d61bf62670f6b1` |
| `Metadata/SkinBodyArmour.ao` | 494 | `550f705f9aaa14c985b9d86c5b7beb6b0372bbd6c2ebc5ba98c172b3ea6e21fc` |
| `controllers/FerrofluidArmourInt.ao` | 1,386 | `a5c261142d9aabb174428047150418070e8b3d775a78ce8872ebeff093e75fb7` |
| `FerrofluidArmourInt.ao` | 2,408 | `a1a01b4775eb9d457ddea75ff168d12525168d4a3d01671ddb2a89ed0e106aab` |
| `fx/hit_armour_int.epk` | 462 | `233f5e0ff33c3691a612d597c64c8d2232befa279ff9d21d0b1317609b87a8e3` |

Ignored evidence under `Tools/re/out/client-damage-audit/`:

- `r17-hit-program-verify.py`: optional fresh read-only extraction, raw hashes,
  complete selected inheritance, mode/program/token provenance, table-row
  identity, fresh native name entries, exact instruction and pointer checks.
- `r17-hit-program-verification.json` and `r17-hit-program-selected-audit.json`:
  **16 original functions, 16 ranges, 2,408 instructions and 59 instruction
  anchors**, plus argument-table/literal/slot and resource assertions.
- The declaration parser reuses the retained balanced string/comment-aware
  lexer from `r8-effect-source-catalogue.py`; its dependency hash is recorded.
  Its call index is lexical, not a GEAL interpreter.

An initial decoded scan of the older 12,643-file cache found no exact names
for these four APIs. The positive controller above was outside that cache.
That scan is a selection aid, not evidence of archive-wide absence, and is
not used to close any numerical or source-program obligation.

Remaining gaps are the actual programs for the other three APIs, runtime
attachment/compilation and task-lifecycle timing, and any accepted program
that computes or changes numerical damage/resources. No client execution,
attachment, new capture, server change or renderer expansion was performed.
