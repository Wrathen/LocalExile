using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GameServer.Modding;

/// <summary>
/// The mods in <c>Mods/Binaries/&lt;id&gt;/</c> (Agents/docs/dev/modding.md): each one's <c>mod.json</c> and assembly, in a collectible
/// load context of its own (<see cref="ModLoadContext"/>), loaded after the mods it depends on. A change in a mod's folder
/// (a build, a new or deleted folder) reloads that mod and the mods that depend on it; a reload starts their state over.
/// </summary>
/// <remarks>
/// Every hook a mod registers is held here, in the host's lists, and nowhere else in the server: unloading a mod removes
/// them all, so no static of the server keeps one of its types alive and the context can be collected.
/// </remarks>
sealed class ModHost(string folder, Action<string> log, bool modules = false, bool leagues = false) : IDisposable
{
    /// <summary>A change was applied (a mod loaded, unloaded or failed): the line it logged.</summary>
    public event Action<string>? Changed;

    /// <summary>The mods' folder, relative to the working directory (the repository root) unless absolute.</summary>
    public string Folder { get; } = folder;

    readonly Lock _gate = new();
    readonly Dictionary<string, LoadedMod> _loaded = new(StringComparer.Ordinal);
    // Why a folder's mod is not loaded, as last logged (logged again only when it changes).
    readonly Dictionary<string, string> _failed = new(StringComparer.Ordinal);
    readonly Dictionary<string, Task<bool>> _unloads = new(StringComparer.Ordinal);
    readonly Dictionary<string, (Timer Timer, int Busy)> _pending = new(StringComparer.Ordinal);
    // /mod_enable and /mod_disable's choices (StateFile), over each manifest's "enabled"; the folders turned off by either.
    readonly Dictionary<string, bool> _choices = new(StringComparer.Ordinal);
    readonly HashSet<string> _off = new(StringComparer.Ordinal);
    FileSystemWatcher? _watcher;

    // The hooks. Each list is swapped whole (ImmutableInterlocked), so a dispatch iterates a snapshot without a lock.
    ImmutableDictionary<string, CommandHook> _commands = ImmutableDictionary.Create<string, CommandHook>(StringComparer.OrdinalIgnoreCase);
    readonly HookList<Func<IPlayer, double, Task>> _ticks = new();
    readonly HookList<(ushort Opcode, Func<Packet, Task<bool>> Run)> _packets = new();
    readonly HookList<Func<IPlayer, Task>> _areas = new();
    readonly HookList<Func<MonsterKill, Task>> _kills = new();
    readonly HookList<Func<ObjectUse, Task<bool>>> _objects = new();
    readonly HookList<Func<IPlayer, Task>> _hideouts = new();
    readonly HookList<Func<IPlayer, string, Task>> _leaving = new();
    readonly HookList<Func<IPlayer, IEnumerable<(string Log, byte[] Message)>>> _enter = new();
    readonly HookList<Func<IPlayer, AreaHeader?>> _headers = new();
    readonly HookList<Func<NpcOption, Task<bool>>> _npcOptions = new();
    readonly HookList<Action<DropRoll>> _drops = new();
    readonly HookList<Action<ExperienceGain>> _experience = new();
    readonly HookList<Action<StatSheet>> _stats = new();

    sealed record CommandHook(LoadedMod Mod, string Usage, Func<ChatCommand, Task> Run);

    /// <summary>Folders that hold a <c>mod.json</c>, and how long a change waits for the rest of a build's files.</summary>
    public const string ManifestFile = "mod.json";
    /// <summary>The choices of /mod_enable and /mod_disable, in the mods' folder: an "id on" or "id off" per line.</summary>
    public const string StateFile = "mod-state.txt";
    /// <summary>A module's manifest. Modules have no state file: deleting a module's folder is what turns it off.</summary>
    public const string ModuleManifestFile = "module.json";

    /// <summary>A league's manifest, in a host of leagues' code (<c>modules</c> as well).</summary>
    public const string LeagueManifestFile = "league.json";

    string Manifest => leagues ? LeagueManifestFile : modules ? ModuleManifestFile : ManifestFile;
    // What its lines start with.
    string Tag => leagues ? "[leagues]" : modules ? "[modules]" : "[mods]";
    string Noun => leagues ? "league" : modules ? "module" : "mod";
    /// <summary>A host of modules loads only these: a module lives in a league. Null: all.</summary>
    public Func<IReadOnlySet<string>>? Wanted { get; init; }

    /// <summary>Whether a module's hooks run for this player (a host of modules; the league's set). Null: always.</summary>
    public Func<IPlayer, string, bool>? Active { get; init; }

    /// <summary>What <see cref="IModuleContext.World"/> gives.</summary>
    public Func<IPlayer, IPlayerWorld>? WorldOf { get; init; }

    bool IsHostOfModules => modules;

    bool On(LoadedMod mod, IPlayer player) => Active?.Invoke(player, mod.Manifest.Id) != false;

    public static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);
    const int BusyRetries = 5;

    /// <summary>Loads every mod there is, then watches the folder (created when missing).</summary>
    public void Start(bool watch = true)
    {
        Directory.CreateDirectory(Folder);
        lock (_gate)
        {
            if (!modules) ReadChoices();
            LoadMissing();
        }
        if (!watch) return;
        _watcher = new FileSystemWatcher(Path.GetFullPath(Folder))
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Changed += (_, e) => Touched(e.FullPath);
        _watcher.Created += (_, e) => Touched(e.FullPath);
        _watcher.Deleted += (_, e) => Touched(e.FullPath);
        _watcher.Renamed += (_, e) => { Touched(e.OldFullPath); Touched(e.FullPath); };
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Whether a mod of this id is loaded.</summary>
    public bool IsLoaded(string id)
    {
        lock (_gate) return _loaded.ContainsKey(id);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        lock (_gate)
        {
            foreach (var (timer, _) in _pending.Values) timer.Dispose();
            _pending.Clear();
            foreach (string id in UnloadOrder([.. _loaded.Keys])) Unload(id);
        }
    }

    /// <summary>A loaded mod's id, version, kind and hooks, or a folder's error.</summary>
    public sealed record Status(string Id, string? Version, ModKind? Kind, int Hooks, string? Error);

    public IReadOnlyList<Status> Mods()
    {
        lock (_gate)
            return [.. _loaded.Values.Select(m => new Status(m.Manifest.Id, m.Manifest.Version, m.Manifest.Kind, m.Hooks, null))
                .Concat(_failed.Select(f => new Status(f.Key, null, null, 0, f.Value)))
                .Concat(_off.Select(id => new Status(id, null, null, 0, "disabled"))).OrderBy(s => s.Id, StringComparer.Ordinal)];
    }

    /// <summary>Reloads a mod (and the mods that depend on it) from its folder now; every mod when id is null.</summary>
    public void Reload(string? id = null)
    {
        lock (_gate)
        {
            if (modules)
            {
                foreach (string one in id is null ? [.. _loaded.Keys] : new[] { id }) HotReload(one);
                return;
            }
            if (id is null) foreach (string all in UnloadOrder([.. _loaded.Keys])) Unload(all);
            else foreach (string affected in UnloadOrder(Dependents(id))) Unload(affected);
            if (id is null) _failed.Clear(); else _failed.Remove(id);
            LoadMissing();
        }
    }

    /// <summary>Turns a mod on or off, now and at every start; off also unloads the mods that depend on it, and on loads
    /// them again. False when the folder has no such mod.</summary>
    public bool SetEnabled(string id, bool enabled)
    {
        if (modules) throw new InvalidOperationException("a module is turned off by deleting its folder");
        lock (_gate)
        {
            if (id != Path.GetFileName(id) || (DirOf(id) is not { } dir || !IsMod(dir))) return false;
            _choices[id] = enabled;
            File.WriteAllLines(Path.Combine(Folder, StateFile), _choices.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => $"{c.Key} {(c.Value ? "on" : "off")}"));
            if (!enabled) foreach (string affected in UnloadOrder(Dependents(id))) Unload(affected);
            _failed.Remove(id);
            LoadMissing();
            return true;
        }
    }

    void ReadChoices()
    {
        string path = Path.Combine(Folder, StateFile);
        if (!File.Exists(path)) return;
        foreach (string line in File.ReadAllLines(path))
            if (line.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [var id, var state and ("on" or "off")]) _choices[id] = state == "on";
    }

    /// <summary>Completes when the mod's last unload was collected (true) or still was not after a few seconds
    /// (false: something outside the mod still holds one of its objects).</summary>
    public Task<bool> Unloaded(string id)
    {
        lock (_gate) return _unloads.GetValueOrDefault(id) ?? Task.FromResult(true);
    }

    // ---- What the Gateway calls -------------------------------------------------------------------------------------

    /// <summary>The mods' command usages, for /commands.</summary>
    public IEnumerable<(string Name, string Usage)> Commands => _commands.Select(c => (c.Key, c.Value.Usage));

    /// <summary>Runs a mod's command; false when no mod has one of that name.</summary>
    public async Task<bool> RunCommand(IPlayer player, string name, IReadOnlyList<string> args)
    {
        if (!_commands.TryGetValue(name, out var command) || !On(command.Mod, player)) return false;
        if (!await Guard(command.Mod, $"/{name}", () => command.Run(new ChatCommand(player, name, args))))
            await player.Tell($"/{name} failed (mod {command.Mod.Manifest.Id}); the error is in the Gateway's log. Usage: {command.Usage}");
        return true;
    }

    public async Task Tick(IPlayer player, double seconds)
    {
        foreach (var hook in _ticks.Items) if (On(hook.Mod, player)) await Guard(hook.Mod, "OnTick", () => hook.Run(player, seconds));
    }

    /// <summary>Whether a mod wants client messages with this opcode (saves building a <see cref="Packet"/> for each).</summary>
    public bool WantsPacket(ushort opcode)
    {
        foreach (var hook in _packets.Items) if (hook.Run.Opcode == opcode) return true;
        return false;
    }

    /// <summary>True when a mod took the message (the Gateway's handler is skipped).</summary>
    public async Task<bool> Packet(IPlayer player, ushort opcode, byte[] bytes)
    {
        var packet = new Packet(player, opcode, bytes);
        foreach (var hook in _packets.Items)
        {
            if (hook.Run.Opcode != opcode || !On(hook.Mod, player)) continue;
            bool taken = false;
            if (await Guard(hook.Mod, $"OnPacket 0x{opcode:x4}", async () => taken = await hook.Run.Run(packet)) && taken) return true;
        }
        return false;
    }

    public async Task AreaGenerated(IPlayer player)
    {
        foreach (var hook in _areas.Items) if (On(hook.Mod, player)) await Guard(hook.Mod, "OnAreaGenerated", () => hook.Run(player));
    }

    public async Task MonsterKilled(MonsterKill kill)
    {
        foreach (var hook in _kills.Items) if (On(hook.Mod, kill.Player)) await Guard(hook.Mod, "OnMonsterKilled", () => hook.Run(kill));
    }

    /// <summary>True when a mod's object was used.</summary>
    public async Task<bool> ObjectUsed(ObjectUse use)
    {
        foreach (var hook in _objects.Items)
        {
            if (!On(hook.Mod, use.Player)) continue;
            bool taken = false;
            if (await Guard(hook.Mod, "OnObjectUsed", async () => taken = await hook.Run(use)) && taken) return true;
        }
        return false;
    }

    public async Task PlayerLeaving(IPlayer player, string why)
    {
        foreach (var hook in _leaving.Items) if (On(hook.Mod, player)) await Guard(hook.Mod, "OnPlayerLeaving", () => hook.Run(player, why));
    }

    public async Task HideoutReady(IPlayer player)
    {
        foreach (var hook in _hideouts.Items) if (On(hook.Mod, player)) await Guard(hook.Mod, "OnHideoutReady", () => hook.Run(player));
    }

    public List<(string Log, byte[] Message)> EnterMessages(IPlayer player)
    {
        var messages = new List<(string, byte[])>();
        foreach (var hook in _enter.Items) if (On(hook.Mod, player)) Guard(hook.Mod, "OnEnterMessages", () => messages.AddRange(hook.Run(player)));
        return messages;
    }

    public AreaHeader? Header(IPlayer player)
    {
        AreaHeader? header = null;
        foreach (var hook in _headers.Items)
            if (header is null && On(hook.Mod, player)) Guard(hook.Mod, "OnAreaHeader", () => header = hook.Run(player));
        return header;
    }

    public async Task<bool> RunNpcOption(NpcOption talk)
    {
        foreach (var hook in _npcOptions.Items)
        {
            if (!On(hook.Mod, talk.Player)) continue;
            bool taken = false;
            if (await Guard(hook.Mod, "OnNpcOption", async () => taken = await hook.Run(talk)) && taken) return true;
        }
        return false;
    }

    /// <summary>The mods' changes to a kill's drops; null when no mod changes drops.</summary>
    public DropRoll? Drops(IPlayer player, MonsterInfo monster)
    {
        if (!ChangesDrops) return null;
        var roll = new DropRoll(player, monster);
        ModifyDrops(roll);
        return roll;
    }

    public bool ChangesDrops => !_drops.Items.IsEmpty;

    public void ModifyDrops(DropRoll roll)
    {
        foreach (var hook in _drops.Items) if (On(hook.Mod, roll.Player)) Guard(hook.Mod, "ModifyDrops", () => hook.Run(roll));
    }

    public uint Experience(IPlayer player, MonsterInfo monster, uint experience)
    {
        var hooks = _experience.Items;
        if (hooks.IsEmpty) return experience;
        var gain = new ExperienceGain(player, monster, experience);
        foreach (var hook in hooks) if (On(hook.Mod, player)) Guard(hook.Mod, "ModifyExperience", () => hook.Run(gain));
        return gain.Experience;
    }

    /// <summary>Whether a mod adds stats (saves building a <see cref="StatSheet"/> for every stat snapshot).</summary>
    public bool ChangesStats => !_stats.Items.IsEmpty;

    public IReadOnlyDictionary<string, int> Stats(IPlayer player)
    {
        var hooks = _stats.Items;
        if (hooks.IsEmpty) return ImmutableDictionary<string, int>.Empty;
        var sheet = new StatSheet(player);
        foreach (var hook in hooks) if (On(hook.Mod, player)) Guard(hook.Mod, "ModifyStats", () => hook.Run(sheet));
        return sheet.Added;
    }

    async Task<bool> Guard(LoadedMod mod, string hook, Func<Task> run)
    {
        try { await run(); return true; }
        catch (Exception ex) { Failed(mod, hook, ex); return false; }
    }

    bool Guard(LoadedMod mod, string hook, Action run)
    {
        try { run(); return true; }
        catch (Exception ex) { Failed(mod, hook, ex); return false; }
    }

    void Failed(LoadedMod mod, string hook, Exception ex) =>
        log($"{Tag} {mod.Manifest.Id} {hook} failed (skipped): {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    // ---- Loading ----------------------------------------------------------------------------------------------------

    void Touched(string path)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(Folder), path);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // Modules/Core/<id>/ is as Modules/<id>/.
        if (modules && !leagues && parts is ["Core", _, ..]) parts = parts[1..];
        string id = parts[0];
        if (id is "" or "." or ".." or StateFile || relative.StartsWith("..", StringComparison.Ordinal)) return;
        // A module's source and its build's scratch are not the module: its build's output is.
        // A league's own compile writes its Binaries/.
        if (leagues && parts is [_, "Binaries", ..]) return;
        if (modules && parts is [_, var part, ..]
            && (part == "obj" || (part != "bin" && part != Manifest && HasProject(DirOf(id) ?? "")))) return;
        Later(id, busy: 0);
    }

    // One reload for a burst of changes (a build writes the dll, the pdb and the manifest one after another).
    void Later(string id, int busy)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(id, out var pending)) { pending.Timer.Change(Settle, Timeout.InfiniteTimeSpan); return; }
            var timer = new Timer(_ => Apply(id), null, Timeout.Infinite, Timeout.Infinite);
            _pending[id] = (timer, busy);
            timer.Change(Settle, Timeout.InfiniteTimeSpan);
        }
    }

    void Apply(string id)
    {
        bool busy;
        int tries;
        lock (_gate)
        {
            if (!_pending.Remove(id, out var pending)) return;
            pending.Timer.Dispose();
            tries = pending.Busy;
            if (modules) busy = HotReload(id);
            else
            {
                foreach (string affected in UnloadOrder(Dependents(id))) Unload(affected);
                _failed.Remove(id);
                LoadMissing();
                busy = _failed.TryGetValue(id, out string? error) && error.StartsWith("busy:", StringComparison.Ordinal);
            }
        }
        // A file still being written: try again a few times before leaving the error.
        if (busy && tries < BusyRetries) Later(id, tries + 1);
    }

    // A loaded module's new files, with the modules that depend on it. A module stays while the Gateway runs: what is wrong
    // with the new files (a failed compile, a half-written build, a deleted folder) leaves the running one as it is, and one
    // that throws while loading goes back to the assemblies it had. A folder that was not loaded at start waits for the next.
    // True when a file was still being written.
    bool HotReload(string id)
    {
        if (!_loaded.TryGetValue(id, out var old) || old.Context is null) return false;
        if (!Directory.Exists(old.Folder))
        {
            Tell($"{Tag} {id}'s folder is gone: it stays loaded until the Gateway restarts");
            return false;
        }
        ModManifest manifest;
        Images images;
        try
        {
            string manifestPath = Path.Combine(old.Folder, Manifest);
            // Scripts need no manifest.
            manifest = File.Exists(manifestPath) ? ReadManifest(id, File.ReadAllText(manifestPath)) : old.Manifest;
            images = ReadImages(manifest, old.Folder);
        }
        catch (Exception ex) when (ex is IOException and not FileNotFoundException or UnauthorizedAccessException) { return true; }
        catch (Exception ex) when (ex is JsonException or FormatException or FileNotFoundException or ModScripts.CompileException)
        {
            Tell($"{Tag} {id} keeps running, the new files are not usable: {ex.Message}");
            return false;
        }
        var affected = UnloadOrder(Dependents(id));
        var before = affected.Select(a => _loaded[a]).ToList();
        foreach (string a in affected) Unload(a);
        if (!Load(manifest, old.Folder, images))
        {
            string why = _failed.GetValueOrDefault(id, "");
            Load(old.Manifest, old.Folder, old.Images);
            Tell($"{Tag} {id} keeps running as it was, the new build did not load: {why}");
        }
        foreach (var dependent in before.Where(m => m != old).Reverse()) Load(dependent.Manifest, dependent.Folder, dependent.Images);
        return false;
    }

    // Every folder whose mod is not loaded, each after the mods it depends on; what cannot load keeps its reason.
    void LoadMissing()
    {
        var waiting = new Dictionary<string, (ModManifest Manifest, string Folder)>(StringComparer.Ordinal);
        _off.Clear();
        var wanted = Wanted?.Invoke();
        foreach (string dir in Dirs())
        {
            string id = Path.GetFileName(dir);
            if (wanted?.Contains(id) == false) continue;   // in no league
            if (_loaded.ContainsKey(id)) continue;
            string manifestPath = Path.Combine(dir, Manifest);
            if (!IsMod(dir)) { _failed.Remove(id); continue; }   // not a mod (yet)
            try
            {
                // Scripts need no manifest.
                var manifest = File.Exists(manifestPath) ? ReadManifest(id, File.ReadAllText(manifestPath))
                    : new ModManifest(id, "0", ModKind.Script, [], id + ".dll");
                if (!leagues && manifest.Kind == ModKind.League) throw new FormatException("a league belongs in Leagues/, with a league.json");
                if (modules || _choices.GetValueOrDefault(id, manifest.Enabled)) waiting[id] = (manifest, dir);
                else { _off.Add(id); _failed.Remove(id); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Fail(id, $"busy: {ex.Message}"); }
            catch (Exception ex) when (ex is JsonException or FormatException) { Fail(id, $"{Manifest}: {ex.Message}"); }
        }
        foreach (string gone in _failed.Keys.Where(id => DirOf(id) is null).ToList()) _failed.Remove(gone);
        bool progress = true;
        while (progress && waiting.Count > 0)
        {
            progress = false;
            foreach (var (id, (manifest, dir)) in waiting.ToList())
            {
                if (!manifest.Depends.All(_loaded.ContainsKey)) continue;
                waiting.Remove(id);
                progress |= Load(manifest, dir);
            }
        }
        foreach (var (id, (manifest, _)) in waiting)
        {
            var missing = manifest.Depends.Where(d => !_loaded.ContainsKey(d)).ToList();
            Fail(id, $"needs {string.Join(", ", missing.Select(d => _off.Contains(d) ? $"{d} (disabled)" : _failed.ContainsKey(d) || waiting.ContainsKey(d) ? $"{d} (not loaded)" : $"{d} (no such mod)"))}");
        }
    }

    // A module with a project is built by it; only a folder of loose files is compiled here.
    static bool HasProject(string dir) => Directory.Exists(dir) && Directory.GetFiles(dir, "*.csproj").Length > 0;

    // What a mod is made of, read into memory: its assembly (with symbols), its compiled scripts (last).
    sealed record Images(IReadOnlyList<(byte[] Dll, byte[]? Pdb)> Assemblies, byte[]? Scripts);

    Images ReadImages(ModManifest manifest, string dir)
    {
        if (leagues) return ReadLeagueImages(manifest, dir);
        string path = Path.Combine(dir, manifest.Assembly);
        string[] scripts = modules && HasProject(dir) ? [] : ModScripts.Sources(dir);
        if (!File.Exists(path) && scripts.Length == 0) throw new FileNotFoundException($"no {manifest.Assembly} in the {Noun}'s folder (built yet?)");
        List<(byte[], byte[]?)> assemblies = [];
        if (File.Exists(path))
        {
            string pdb = Path.ChangeExtension(path, ".pdb");
            assemblies.Add((File.ReadAllBytes(path), File.Exists(pdb) ? File.ReadAllBytes(pdb) : null));
        }
        if (scripts.Length == 0) return new(assemblies, null);
        var compiled = ModScripts.Compile(manifest.Id, scripts, ScriptReferences(manifest, dir), modules);
        assemblies.Add(compiled);
        return new(assemblies, compiled.Dll);
    }

    // A league's Source/ compiles into its Binaries/; with no Source, its Binaries/ are what it is.
    Images ReadLeagueImages(ModManifest manifest, string dir)
    {
        string source = Path.Combine(dir, "Source"), binaries = Path.Combine(dir, "Binaries"), dll = Path.Combine(binaries, "league.dll"), pdb = Path.ChangeExtension(dll, ".pdb");
        string[] scripts = Directory.Exists(source) ? ModScripts.Sources(source) : [];
        if (scripts.Length > 0)
        {
            var compiled = ModScripts.Compile(manifest.Id.Replace(' ', '_'), scripts, [], module: true);
            Directory.CreateDirectory(binaries);
            File.WriteAllBytes(dll, compiled.Dll);
            File.WriteAllBytes(pdb, compiled.Pdb);
            return new([compiled], compiled.Dll);
        }
        if (!File.Exists(dll)) throw new FileNotFoundException($"no Source or Binaries in the league's folder");
        return new([(File.ReadAllBytes(dll), File.Exists(pdb) ? File.ReadAllBytes(pdb) : null)], null);
    }

    bool Load(ModManifest manifest, string dir, Images? images = null)
    {
        string id = manifest.Id;
        var context = new ModLoadContext(id, Path.GetFullPath(dir), modules, [.. manifest.Depends.Select(d => _loaded[d].Context).OfType<ModLoadContext>()]);
        var mod = new LoadedMod(manifest, Path.GetFullPath(dir), context);
        try
        {
            mod.Images = images ?? ReadImages(manifest, mod.Folder);
            mod.ScriptImage = mod.Images.Scripts;
            List<Assembly> assemblies = [.. mod.Images.Assemblies.Select(a => context.LoadImage(a.Dll, a.Pdb))];
            var entries = assemblies.SelectMany(a => a.GetExportedTypes())
                .Where(t => (leagues ? typeof(League) : modules ? typeof(IModule) : typeof(IMod)).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false }).ToList();
            if (entries.Count == 0) throw new InvalidOperationException($"{(mod.Images.Scripts is not null ? "the scripts have" : manifest.Assembly + " has")} no public class that implements {(modules ? nameof(IModule) : nameof(IMod))}");
            var api = new Context(this, mod);
            foreach (var type in entries)
            {
                var entry = Activator.CreateInstance(type)!;
                if (entry is League code) ServerLeagues.SetCode(id, code);
                mod.Entries.Add(entry);
                if (entry is IModule module) module.Load(api); else ((IMod)entry).Load(api);
            }
        }
        catch (Exception ex)
        {
            mod.Unloaded = true;
            Remove(mod);
            foreach (var disposable in mod.Entries.OfType<IDisposable>()) Guard(mod, "Dispose", disposable.Dispose);
            _unloads[id] = Collect(Unload(mod));
            if (ex is TypeInitializationException or System.Reflection.TargetInvocationException && ex.InnerException is { } inner) ex = inner;
            Fail(id, ex is IOException and not FileNotFoundException ? $"busy: {ex.Message}"
                : ex is ModScripts.CompileException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}");
            return false;
        }
        _loaded[id] = mod;
        _failed.Remove(id);
        Tell($"{Tag} loaded {id} {manifest.Version} ({manifest.Kind.ToString().ToLowerInvariant()}, {mod.Hooks} hook(s))");
        return true;
    }

    // The folders that may hold one: in a host of modules, Modules/Core/'s too.
    IEnumerable<string> Dirs()
    {
        if (!Directory.Exists(Folder)) yield break;
        foreach (string dir in Directory.GetDirectories(Folder))
        {
            if (modules && !leagues && Path.GetFileName(dir) == "Core")
                foreach (string core in Directory.GetDirectories(dir)) yield return core;
            else yield return dir;
        }
    }

    string? DirOf(string id) => Dirs().FirstOrDefault(d => Path.GetFileName(d) == id);

    bool IsMod(string dir) => File.Exists(Path.Combine(dir, Manifest)) || (Directory.Exists(dir) && ModScripts.Sources(dir).Length > 0);

    // What a mod's scripts compile against, beyond the framework: its folder's assemblies and its dependencies'.
    IEnumerable<byte[]> ScriptReferences(ModManifest manifest, string folder)
    {
        foreach (string dll in Directory.GetFiles(folder, "*.dll")) yield return File.ReadAllBytes(dll);
        foreach (string dependency in manifest.Depends)
        {
            var loaded = _loaded[dependency];
            string path = Path.Combine(loaded.Folder, loaded.Manifest.Assembly);
            if (File.Exists(path)) yield return File.ReadAllBytes(path);
            if (loaded.ScriptImage is { } image) yield return image;
        }
    }

    void Unload(string id)
    {
        if (!_loaded.Remove(id, out var mod)) return;
        mod.Unloaded = true;
        Remove(mod);
        foreach (var disposable in mod.Entries.OfType<IDisposable>())
            Guard(mod, "Dispose", disposable.Dispose);
        _unloads[id] = Collect(Unload(mod));
        Tell($"{Tag} unloaded {id}");
    }

    // Kept out of Unload(string) so no local of that frame holds the context while it is collected.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static (WeakReference Context, string CopyDirectory) Unload(LoadedMod mod)
    {
        mod.Entries.Clear();
        var weak = new WeakReference(mod.Context);
        mod.Context?.Unload();
        return (weak, mod.Context?.CopyDirectory ?? "");
    }

    async Task<bool> Collect((WeakReference Context, string CopyDirectory) unloaded)
    {
        var (context, copyDirectory) = unloaded;
        for (int i = 0; i < 20 && context.IsAlive; i++)
        {
            await Task.Delay(i == 0 ? 0 : 100);
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        if (context.IsAlive) log($"{Tag} an unloaded {Noun} is still in memory after 2 s: a thread, timer or task of it still runs, or something outside it holds one of its objects");
        else if (copyDirectory.Length > 0 && !ModLoadContext.TryDeleteCopy(copyDirectory)) log($"{Tag} could not remove an unloaded {Noun}'s private copy");
        return !context.IsAlive;
    }

    void Remove(LoadedMod mod)
    {
        ImmutableInterlocked.Update(ref _commands, c => c.RemoveRange(c.Where(e => e.Value.Mod == mod).Select(e => e.Key)));
        _ticks.Remove(mod); _packets.Remove(mod); _areas.Remove(mod); _kills.Remove(mod); _objects.Remove(mod); _hideouts.Remove(mod); _leaving.Remove(mod); _enter.Remove(mod); _headers.Remove(mod); _npcOptions.Remove(mod);
        _drops.Remove(mod); _experience.Remove(mod); _stats.Remove(mod);
        if (mod.ProvidesRules) ServerLeagues.RemoveRules(mod.Manifest.Id);
        if (leagues) ServerLeagues.RemoveCode(mod.Manifest.Id);
    }

    void Fail(string id, string error)
    {
        if (_failed.TryGetValue(id, out string? last) && last == error) return;
        _failed[id] = error;
        if (!error.StartsWith("busy:", StringComparison.Ordinal)) Tell($"{Tag} {id} not loaded: {error}");
    }

    void Write(string line) => log(line);

    void Tell(string line)
    {
        log(line);
        Changed?.Invoke(line);
    }

    // The mod and every loaded mod that depends on it, directly or not.
    List<string> Dependents(string id)
    {
        var all = new List<string> { id };
        for (int i = 0; i < all.Count; i++)
            foreach (var m in _loaded.Values)
                if (m.Manifest.Depends.Contains(all[i]) && !all.Contains(m.Manifest.Id)) all.Add(m.Manifest.Id);
        return all;
    }

    // Dependents before the mods they depend on.
    List<string> UnloadOrder(List<string> ids)
    {
        var order = new List<string>();
        void Visit(string id)
        {
            if (order.Contains(id)) return;
            foreach (var m in _loaded.Values)
                if (ids.Contains(m.Manifest.Id) && m.Manifest.Depends.Contains(id)) Visit(m.Manifest.Id);
            order.Add(id);
        }
        foreach (string id in ids) Visit(id);
        return order;
    }

    /// <summary>Reads a <c>mod.json</c>: <c>{"id", "version", "kind", "depends", "assembly", "enabled"}</c>, all optional; the id, when
    /// given, must be the folder's name.</summary>
    public static ModManifest ReadManifest(string folderName, string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("not a JSON object");
        string? Text(string name) => doc.RootElement.EnumerateObject()
            .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { Value.ValueKind: not JsonValueKind.Undefined } p
            ? p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : throw new FormatException($"\"{name}\" is not a string")
            : null;
        string id = Text("id") ?? folderName;
        if (!id.Equals(folderName, StringComparison.Ordinal)) throw new FormatException($"id \"{id}\" is not the folder's name \"{folderName}\"");
        string kindText = Text("kind") ?? nameof(ModKind.Feature);
        if (!Enum.TryParse<ModKind>(kindText, ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
            throw new FormatException($"kind \"{kindText}\" is not league, feature or script");
        var depends = new List<string>();
        if (doc.RootElement.EnumerateObject().FirstOrDefault(p => p.Name.Equals("depends", StringComparison.OrdinalIgnoreCase)) is
            { Value.ValueKind: not JsonValueKind.Undefined } d)
        {
            if (d.Value.ValueKind != JsonValueKind.Array) throw new FormatException("\"depends\" is not a list of ids");
            foreach (var e in d.Value.EnumerateArray())
                depends.Add(e.ValueKind == JsonValueKind.String ? e.GetString()! : throw new FormatException("\"depends\" is not a list of ids"));
        }
        if (depends.Contains(id)) throw new FormatException("a mod cannot depend on itself");
        bool enabled = true;
        if (doc.RootElement.EnumerateObject().FirstOrDefault(p => p.Name.Equals("enabled", StringComparison.OrdinalIgnoreCase)) is
            { Value.ValueKind: not JsonValueKind.Undefined } on)
            enabled = on.Value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new FormatException("\"enabled\" is not true or false"),
            };
        return new ModManifest(id, Text("version") ?? "0", kind, depends, Text("assembly") ?? id + ".dll", enabled);
    }

    sealed class LoadedMod(ModManifest manifest, string folder, ModLoadContext? context)
    {
        public ModManifest Manifest { get; } = manifest;
        public string Folder { get; } = folder;
        /// <summary>Null for a built-in.</summary>
        public ModLoadContext? Context { get; } = context;
        public List<object> Entries { get; } = [];
        public bool ProvidesRules;
        /// <summary>The compiled scripts, for the scripts of the mods that depend on this one.</summary>
        public byte[]? ScriptImage;
        /// <summary>What it was loaded from, to go back to when a module's new build does not load.</summary>
        public Images? Images;
        public int Hooks;
        public volatile bool Unloaded;
    }

    sealed class HookList<T>
    {
        public sealed record Hook(LoadedMod Mod, T Run);
        ImmutableArray<Hook> _items = [];
        public ImmutableArray<Hook> Items => _items;
        public Hook Add(LoadedMod mod, T run)
        {
            var hook = new Hook(mod, run);
            ImmutableInterlocked.Update(ref _items, a => a.Add(hook));
            return hook;
        }
        public void Remove(Hook hook) => ImmutableInterlocked.Update(ref _items, a => a.Remove(hook));
        public void Remove(LoadedMod mod) => ImmutableInterlocked.Update(ref _items, a => a.RemoveAll(h => h.Mod == mod));
    }

    sealed class Handle(Action remove) : IDisposable
    {
        Action? _remove = remove;
        public void Dispose() => Interlocked.Exchange(ref _remove, null)?.Invoke();
    }

    // What a mod's Load gets. It refuses new hooks once its mod is unloaded (a task the mod left running).
    sealed class Context(ModHost host, LoadedMod mod) : IModuleContext
    {
        public ModManifest Manifest => mod.Manifest;
        public string Folder => mod.Folder;
        public void Log(string message) => host.Write($"{host.Tag} {mod.Manifest.Id}: {message}");

        IDisposable Add<T>(HookList<T> list, T run)
        {
            if (mod.Unloaded) throw new InvalidOperationException($"mod {mod.Manifest.Id} is unloaded");
            var hook = list.Add(mod, run);
            Interlocked.Increment(ref mod.Hooks);
            return new Handle(() => { list.Remove(hook); Interlocked.Decrement(ref mod.Hooks); });
        }

        public IDisposable OnChatCommand(string name, string usage, Func<ChatCommand, Task> run)
        {
            if (mod.Unloaded) throw new InvalidOperationException($"mod {mod.Manifest.Id} is unloaded");
            name = name.TrimStart('/');
            if (name.Length == 0 || name.Any(char.IsWhiteSpace)) throw new ArgumentException($"\"{name}\" is not a command name");
            var hook = new CommandHook(mod, usage, run);
            if (!ImmutableInterlocked.TryAdd(ref host._commands, name, hook))
                throw new InvalidOperationException($"/{name} belongs to mod {host._commands[name].Mod.Manifest.Id}");
            Interlocked.Increment(ref mod.Hooks);
            return new Handle(() =>
            {
                ImmutableInterlocked.Update(ref host._commands, c => c.TryGetValue(name, out var h) && h == hook ? c.Remove(name) : c);
                Interlocked.Decrement(ref mod.Hooks);
            });
        }

        public IDisposable OnTick(Func<IPlayer, double, Task> tick) => Add(host._ticks, tick);
        public IDisposable OnPacket(ushort opcode, Func<Packet, Task<bool>> handle) => Add(host._packets, (opcode, handle));
        public IDisposable OnAreaGenerated(Func<IPlayer, Task> generated) => Add(host._areas, generated);
        public IDisposable OnMonsterKilled(Func<MonsterKill, Task> killed) => Add(host._kills, killed);
        public IDisposable OnObjectUsed(Func<ObjectUse, Task<bool>> used) => Add(host._objects, used);
        public void ProvideLeagueRules(Func<LeagueRules> rules)
        {
            if (!host.IsHostOfModules) throw new InvalidOperationException("only a module gives a league rules");
            mod.ProvidesRules = true;
            ServerLeagues.AddRules(mod.Manifest.Id, rules);
        }

        public IPlayerWorld World(IPlayer player) => (host.WorldOf ?? throw new InvalidOperationException("this server gives modules no world"))(player);
        public IDisposable OnHideoutReady(Func<IPlayer, Task> ready) => Add(host._hideouts, ready);
        public IDisposable OnPlayerLeaving(Func<IPlayer, string, Task> leaving) => Add(host._leaving, leaving);
        public IDisposable OnEnterMessages(Func<IPlayer, IEnumerable<(string Log, byte[] Message)>> messages) => Add(host._enter, messages);
        public IDisposable OnAreaHeader(Func<IPlayer, AreaHeader?> header) => Add(host._headers, header);
        public IDisposable OnNpcOption(Func<NpcOption, Task<bool>> run) => Add(host._npcOptions, run);
        public IDisposable ModifyDrops(Action<DropRoll> modify) => Add(host._drops, modify);
        public IDisposable ModifyExperience(Action<ExperienceGain> modify) => Add(host._experience, modify);
        public IDisposable ModifyStats(Action<StatSheet> modify) => Add(host._stats, modify);

    }
}
