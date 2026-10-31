# Leagues and modules

The Gateway is the framework: casting a spell, talking to an NPC, handling packets. **Modules** do everything else, **leagues** are sets
of modules, and [mods](modding.md) are small modifications, global and the user's own.

| | Mod | Module | League |
|---|---|---|---|
| Is | a tweak | a game system: NPC dialogue, Delve, Pantheons | what a character lives in, shown in game |
| Folder | `Mods/` (gitignored as a whole; installed by the user) | `Modules/<id>/`, `Modules/Core/<id>/` | `Leagues/<Name>/` |
| Entry point | `IMod` | `IModule` | a class deriving `League` in `Source/`, and `league.json` |
| Loads | any time | when the Gateway starts, if a league lists it | when the Gateway starts, if its folder exists |
| Hot reload | yes | yes | its code, yes |

Built output is gitignored everywhere. `live.ps1` builds every project in `Modules\` and `Mods\Source\`. The Gateway compiles each league's `Source/*.cs` into its `Binaries/` when it starts; a league with only `Binaries/` runs from those.

## Leagues

`Leagues/` decides what exists. Leagues and their module lists are read once when a server starts: nothing is loaded, unloaded, added or
removed while it runs. A connection is in one league, and only that league's modules run for it.

```json
{
  "name": "Ruthless Allflame",
  "base": "Allflame",
  "parent": "Ruthless",
  "entryOf": "Allflame", "entryKind": "form",
  "form": "ruthless",
  "description": "The text on its card.",
  "addModules": ["ruthless"], "removeModules": ["harvest"],
  "dataSet": "HardMode"
}
```

| Field | Means |
|---|---|
| `parent` | where its characters, stash and gold go if it ends. Not inheritance: nothing else passes on. Must lead to `Standard` or `Void`, else it does not load |
| `entryOf`, `entryKind` | a check box (`hardcore`, `ssf`, `form`: the client's Ruthless box) on that league's card. With none, it has its own card, as `Hardcore Allflame` does |
| `hardcore`, `ssf`, `form` | its modes. A Hardcore league's parent is the non-hardcore one |
| `addModules`, `removeModules` | changes to its list. Every league gets all of `Modules/Core/` unless `"core": false`. A module listed twice stops the league loading |
| `ended` | no new characters, and the migration list offers it |
| `creatable: false` | not on the create screen (Void) |
| `dataSet` | `HardMode`: the game files' HardMode tables |

- **Standard** is required: the Gateway refuses to start without it. Its modules change like any league's.
- **Void** has no modules and cannot be created in. Its characters are listed and cannot be entered. A custom event league can have it as parent,
  so that nothing it drops reaches Standard.
- **A deleted league's** characters (no `Source/` or `Binaries/`) are listed, marked, and cannot be entered. Nothing is converted, and they return with the folder.
- **An ended league's** characters cannot be entered until they migrate (not built: see the roadmap).
- **A Hardcore death:** the death screen appears, and the character stays where it is until the player leaves by its Exit (or any other way). The connection closes first; then the character alone, with what it carries, moves to the parent league. Stashes and gold stay. A saved mark covers a crash in between.
- **A league's code** is a public class deriving `League` in its `Source/`, with the hooks of a module for its own players, and `Rules`. Saving it hot-reloads.

## What stays in the Gateway

The framework: connections and packets, movement and collision, casting and resolving a hit with all it reads (life, ward, energy shield, rage, charges, fortification, souls, leech, auras,
cooldowns, flask stats, monster mods), character stats and experience, items, stash storage, talking to an NPC, quest flags, and `league`, `mod` and `module` commands.

## Modules

A module has no knowledge of leagues, and is not changed by one. A league needing even a small difference uses a module of its own, which may share code.
One copy of a module runs for every league listing it, since one connection is in one league; its hooks run per player. A module no league lists does not load.

- **Core** (`Modules/Core/`) is ordinary modules that new leagues get by default: `commands`, `npcdialogue`, `vendors`, `questobjects`, `hideoutediting`, `mercy`, `portals`, `tattoos`, `uniquebelts`, `harvest`, `heist`, `delve`, `social`, `atlas`, `bench`, `cosmetics`. Group related things in one module.
- **Hot reload:** a new build in `bin/`, an edited `module.json`, a saved loose `.cs` file, or `/module_reload [id]` reloads a module and those that depend on it; their state starts over.
  Bad new files leave the running one as it is; a build that throws while loading goes back to the assemblies it had. Saving a project's source does nothing until it is built.
- **Without a module:** its hooks do not run, so a hideout has no Horticrafting Station (its reserved spot stays free), its messages reach no handler, and an NPC option nothing answers does nothing. Saved data stays.
- **To make one:** copy `Modules/Core/heist/`, rename the folder and `.csproj`, set the id and `assembly`, and list it in the leagues that want it.

## What a module sees

`IModuleContext` is what a mod has, and `ProvideLeagueRules`, `World(player)`, `OnHideoutReady`, `OnEnterMessages`, `OnAreaHeader`, `OnNpcOption`.
`IPlayerWorld` is objects, blockers, walking up, hideout stations, windows, stats, travel, NPC actions, crafting in a slot (with payment), item rules
(`IItemCrafting`), and saved text per account and league or per character (`ISavedData`).
A module can alter the game fully: it compiles against the Gateway and its internals (add its assembly to `InternalsVisibleTo` in `GameServer.Gateway.csproj`), with `using static Program` and `ConnOf(player)`. Mods see only `GameServer.Modding`. Per-player state in a module's own
`ConditionalWeakTable<IPlayer, …>` starts over with a hot reload, and objects it placed stop answering until the player's next area; state in `ClientState` survives.

A Core module reaches the framework through the hooks and `GameEvents` (`Modding/GameEvents.cs`), the Gateway's own hand-over points, with `GameEvent.For(conn)` running only the modules of that connection's league. Add a new hand-over there, in general terms, instead of naming a module in the framework.

Code: `GameEvents.cs`, `ModHost.cs` (one class, three instances in `Program.Mods.cs`), `ServerHooks.cs`, `ServerLeagues.cs`, `Program.ModuleWorld.cs`, `LeagueCatalog.cs`.
