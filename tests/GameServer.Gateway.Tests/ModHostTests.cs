using GameServer.Modding;

namespace GameServer.Gateway.Tests;

// ModHost with the test mod (tests/GameServer.Modding.TestMod) copied into a temporary mods folder as one or more mods.
public sealed class ModHostTests : IDisposable
{
    static readonly string TestModDll = Path.Combine(AppContext.BaseDirectory, "GameServer.Modding.TestMod.dll");
    readonly string _root = Path.Combine(Path.GetTempPath(), "localexile-mods-" + Guid.NewGuid().ToString("N"));
    readonly List<string> _log = [];
    readonly ModHost _host;
    readonly FakePlayer _player = new();

    public ModHostTests()
    {
        Directory.CreateDirectory(_root);
        _host = new ModHost(_root, line => { lock (_log) _log.Add(line); });
    }

    public void Dispose()
    {
        _host.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    string AddMod(string id, string? manifest = null)
    {
        string dir = Path.Combine(_root, id);
        Directory.CreateDirectory(dir);
        File.Copy(TestModDll, Path.Combine(dir, id + ".dll"), overwrite: true);
        File.WriteAllText(Path.Combine(dir, ModHost.ManifestFile), manifest ?? $$"""{ "id": "{{id}}", "version": "1.2", "kind": "feature" }""");
        return dir;
    }

    string Logged() { lock (_log) return string.Join('\n', _log); }

    [Fact]
    public async Task A_mod_in_the_folder_loads_and_its_command_runs()
    {
        AddMod("a");
        _host.Start(watch: false);
        Assert.True(await _host.RunCommand(_player, "count_a", []));
        Assert.Equal(["a count 1"], _player.Told);
        Assert.False(await _host.RunCommand(_player, "count_b", []));
        var status = Assert.Single(_host.Mods());
        Assert.Equal(new ModHost.Status("a", "1.2", ModKind.Feature, 12, null), status);
        Assert.Contains(("count_a", "/count_a (counts)"), _host.Commands);
    }

    [Fact]
    public async Task Disabling_unloads_a_mod_with_its_dependents_and_lasts_across_a_restart()
    {
        AddMod("a");
        AddMod("b", """{ "id": "b", "depends": ["a"] }""");
        _host.Start(watch: false);
        Assert.False(_host.SetEnabled("nothing", false));
        Assert.False(_host.SetEnabled("../a", false));
        Assert.True(_host.SetEnabled("a", false));
        Assert.False(await _host.RunCommand(_player, "count_a", []));
        Assert.False(await _host.RunCommand(_player, "count_b", []));
        Assert.Equal("disabled", _host.Mods().Single(m => m.Id == "a").Error);
        Assert.Contains("a (disabled)", _host.Mods().Single(m => m.Id == "b").Error);
        // A reload does not bring it back, and neither does a new host.
        _host.Reload();
        Assert.False(await _host.RunCommand(_player, "count_a", []));
        using (var restarted = new ModHost(_host.Folder, _ => { }))
        {
            restarted.Start(watch: false);
            Assert.False(await restarted.RunCommand(_player, "count_a", []));
        }
        Assert.True(_host.SetEnabled("a", true));
        Assert.True(await _host.RunCommand(_player, "count_a", []));
        Assert.True(await _host.RunCommand(_player, "count_b", []));
    }

    [Fact]
    public async Task A_manifest_can_start_off_until_it_is_enabled()
    {
        AddMod("a", """{ "id": "a", "enabled": false }""");
        _host.Start(watch: false);
        Assert.False(await _host.RunCommand(_player, "count_a", []));
        Assert.Equal("disabled", Assert.Single(_host.Mods()).Error);
        Assert.True(_host.SetEnabled("a", true));
        Assert.True(await _host.RunCommand(_player, "count_a", []));
        using var restarted = new ModHost(_host.Folder, _ => { });
        restarted.Start(watch: false);
        Assert.True(await restarted.RunCommand(_player, "count_a", []));
    }

    [Fact]
    public async Task A_reload_starts_the_mod_state_over()
    {
        AddMod("a");
        _host.Start(watch: false);
        await _host.RunCommand(_player, "count_a", []);
        await _host.RunCommand(_player, "count_a", []);
        _host.Reload("a");
        await _host.RunCommand(_player, "count_a", []);
        Assert.Equal(["a count 1", "a count 2", "a count 1"], _player.Told);
    }

    [Fact]
    public async Task Unloading_removes_every_hook_and_frees_the_mod()
    {
        string dir = AddMod("a");
        _host.Start(watch: false);
        // Loaded from a private copy: the files can go (or be rebuilt) while it runs.
        Directory.Delete(dir, recursive: true);
        _host.Reload("a");
        Assert.Empty(_host.Mods());
        Assert.False(await _host.RunCommand(_player, "count_a", []));
        Assert.False(_host.WantsPacket(0x7777));
        Assert.False(_host.ChangesStats);
        Assert.Null(_host.Drops(_player, Monster));
        Assert.Equal(100u, _host.Experience(_player, Monster, 100));
        Assert.Contains("[mods] a: disposed", Logged());
        Assert.True(await _host.Unloaded("a"), Logged());
    }

    static readonly MonsterInfo Monster = new("Metadata/Monsters/Zombies/ZombieBite", "Drowned", 2, MonsterRarity.Magic, 10, 20);

    [Fact]
    public async Task Every_hook_reaches_the_mod()
    {
        AddMod("a");
        _host.Start(watch: false);
        Assert.True(_host.WantsPacket(0x7777));
        Assert.False(_host.WantsPacket(0x1234));
        Assert.True(await _host.Packet(_player, 0x7777, [0x77, 0x77]));
        Assert.False(await _host.Packet(_player, 0x7778, [0x77, 0x78]));
        var drops = _host.Drops(_player, Monster)!;
        Assert.Equal(2, drops.Quantity);
        Assert.Equal(["Mirror of Kalandra"], drops.Items);
        Assert.Equal(300u, _host.Experience(_player, Monster, 100));
        Assert.Equal(10, _host.Stats(_player)["strength"]);
        await _host.Tick(_player, 0.25);
        await _host.MonsterKilled(new(_player, Monster));
        await _host.AreaGenerated(_player);
        await _host.RunCommand(_player, "ticks_a", []);
        await _host.RunCommand(_player, "kills_a", []);
        Assert.Equal(["a area 1_1_1", "a ticks 1", "a kills 1"], _player.Told);
    }

    [Fact]
    public void Two_mods_modifiers_add_up()
    {
        AddMod("a");
        AddMod("b");
        _host.Start(watch: false);
        var drops = _host.Drops(_player, Monster)!;
        Assert.Equal(4, drops.Quantity);
        Assert.Equal(2, drops.Items.Count);
        Assert.Equal(900u, _host.Experience(_player, Monster, 100));
        Assert.Equal(20, _host.Stats(_player)["strength"]);
    }

    [Fact]
    public async Task A_failing_hook_is_logged_and_the_player_told()
    {
        AddMod("a");
        _host.Start(watch: false);
        Assert.True(await _host.RunCommand(_player, "boom_a", []));
        Assert.StartsWith("/boom_a failed (mod a)", Assert.Single(_player.Told));
        Assert.Contains("[mods] a /boom_a failed (skipped): InvalidOperationException: boom", Logged());
    }

    [Fact]
    public async Task A_load_that_throws_leaves_nothing_registered()
    {
        string dir = AddMod("a");
        File.WriteAllText(Path.Combine(dir, "fail-load"), "");
        _host.Start(watch: false);
        var status = Assert.Single(_host.Mods());
        Assert.Equal("InvalidOperationException: fail-load is there", status.Error);
        Assert.False(await _host.RunCommand(_player, "count_a", []));
        Assert.False(_host.WantsPacket(0x7777));
        Assert.False(_host.ChangesStats);
    }

    [Fact]
    public async Task A_mod_waits_for_what_it_depends_on_and_reloads_with_it()
    {
        AddMod("b", """{ "id": "b", "kind": "feature", "depends": ["c"] }""");
        _host.Start(watch: false);
        Assert.Equal("needs c (no such mod)", Assert.Single(_host.Mods()).Error);
        AddMod("c");
        _host.Reload("c");
        Assert.Equal(["b", "c"], _host.Mods().Where(m => m.Error is null).Select(m => m.Id));
        await _host.RunCommand(_player, "count_b", []);
        await _host.RunCommand(_player, "count_b", []);
        // Reloading c reloads b, which depends on it: b's state starts over too.
        _host.Reload("c");
        await _host.RunCommand(_player, "count_b", []);
        Assert.Equal(["b count 1", "b count 2", "b count 1"], _player.Told);
        Assert.Contains("[mods] unloaded b", Logged());
        Assert.Equal(ModKind.Feature, _host.Mods().Single(m => m.Id == "b").Kind);
    }

    [Fact]
    public async Task The_watcher_loads_a_new_mod_and_reloads_a_changed_one()
    {
        _host.Start(watch: true);
        string dir = AddMod("a");
        Assert.True(await Eventually(() => _host.Mods().Any(m => m.Id == "a" && m.Error is null)), Logged());
        await _host.RunCommand(_player, "count_a", []);
        await _host.RunCommand(_player, "count_a", []);
        File.WriteAllText(Path.Combine(dir, ModHost.ManifestFile), """{ "id": "a", "version": "2" }""");
        Assert.True(await Eventually(() => _host.Mods().Any(m => m.Version == "2")), Logged());
        await _host.RunCommand(_player, "count_a", []);
        Assert.Equal(["a count 1", "a count 2", "a count 1"], _player.Told);
        Directory.Delete(dir, recursive: true);
        Assert.True(await Eventually(() => _host.Mods().Count == 0), Logged());
    }

    static async Task<bool> Eventually(Func<bool> condition)
    {
        for (int i = 0; i < 100; i++)
        {
            if (condition()) return true;
            await Task.Delay(100);
        }
        return condition();
    }

    [Theory]
    [InlineData("""{ "id": "b" }""", "id \"b\" is not the folder's name \"a\"")]
    [InlineData("""{ "kind": "plugin" }""", "kind \"plugin\" is not league, feature or script")]
    [InlineData("""{ "depends": "c" }""", "\"depends\" is not a list of ids")]
    [InlineData("""{ "depends": ["a"] }""", "a mod cannot depend on itself")]
    [InlineData("""{ "version": 2 }""", "\"version\" is not a string")]
    [InlineData("[]", "not a JSON object")]
    public void A_bad_manifest_says_what_is_wrong(string json, string error) =>
        Assert.Equal(error, Assert.Throws<FormatException>(() => ModHost.ReadManifest("a", json)).Message);

    [Fact]
    public void A_manifest_needs_nothing_but_braces()
    {
        var manifest = ModHost.ReadManifest("a", "{ // comments are fine\n }");
        Assert.Equal(("a", "0", ModKind.Feature, "a.dll"), (manifest.Id, manifest.Version, manifest.Kind, manifest.Assembly));
        Assert.Empty(manifest.Depends);
        var full = ModHost.ReadManifest("a", """{ "ID": "a", "Kind": "Script", "depends": ["x", "y",], "assembly": "Other.dll", }""");
        Assert.Equal((ModKind.Script, "Other.dll"), (full.Kind, full.Assembly));
        Assert.Equal(["x", "y"], full.Depends);
    }

    string AddScript(string id, string text, string file = "script.cs")
    {
        string dir = Path.Combine(_root, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, file), text);
        return dir;
    }

    static string PingScript(string reply) => $$"""
        public sealed class Ping : IMod
        {
            public void Load(IModContext mod) => mod.OnChatCommand("ping", "/ping (a script's)", c => c.Player.Tell("{{reply}}"));
        }
        """;

    [Fact]
    public async Task A_loose_script_needs_no_manifest_and_compiles_when_it_loads()
    {
        AddScript("s", PingScript("pong"));
        _host.Start(watch: false);
        Assert.True(await _host.RunCommand(_player, "ping", []));
        Assert.Equal(["pong"], _player.Told);
        Assert.Equal(new ModHost.Status("s", "0", ModKind.Script, 1, null), Assert.Single(_host.Mods()));
    }

    [Fact]
    public async Task Saving_a_script_reloads_it_and_a_compile_error_says_where()
    {
        AddScript("s", PingScript("one"));
        _host.Start();
        await _host.RunCommand(_player, "ping", []);
        AddScript("s", PingScript("two"));
        await Until(async () => { await _host.RunCommand(_player, "ping", []); return _player.Told.Contains("two"); });
        AddScript("s", "public sealed class Broken : IMod { }");
        await Until(() => Task.FromResult(_host.Mods().Single().Error is not null));
        Assert.Contains("script.cs(1,", _host.Mods().Single().Error);
        Assert.Contains("CS0535", _host.Mods().Single().Error);
        Assert.False(await _host.RunCommand(_player, "ping", []));
        AddScript("s", PingScript("three"));
        await Until(async () => { await _host.RunCommand(_player, "ping", []); return _player.Told.Contains("three"); });
    }

    static async Task Until(Func<Task<bool>> done)
    {
        for (int i = 0; i < 100; i++)
        {
            if (await done()) return;
            await Task.Delay(100);
        }
        Assert.Fail("the mod did not reload in time");
    }

    [Fact]
    public async Task A_script_cannot_compile_against_the_server_but_can_use_a_dependency()
    {
        AddScript("server", "public sealed class S : IMod { public void Load(IModContext mod) { _ = typeof(GameServer.Core.Accounts.League); } }");
        AddScript("base", "public static class Shared { public const string Text = \"shared\"; } public sealed class B : IMod { public void Load(IModContext mod) { } }");
        string user = AddScript("user", "public sealed class U : IMod { public void Load(IModContext mod) => mod.OnChatCommand(\"shared\", \"/shared\", c => c.Player.Tell(Shared.Text)); }");
        File.WriteAllText(Path.Combine(user, ModHost.ManifestFile), """{ "depends": ["base"], "kind": "script" }""");
        _host.Start(watch: false);
        Assert.Contains("CS0234", _host.Mods().Single(m => m.Id == "server").Error);
        Assert.True(await _host.RunCommand(_player, "shared", []));
        Assert.Equal(["shared"], _player.Told);
    }

    [Fact]
    public async Task An_unloaded_script_is_freed()
    {
        AddScript("s", PingScript("pong"));
        _host.Start(watch: false);
        Assert.True(_host.SetEnabled("s", false));
        Assert.True(await _host.Unloaded("s"));
    }

    static string ModulePing(string reply) => $$"""
        public sealed class Ping : IModule
        {
            public void Load(IModuleContext module) => module.OnChatCommand("ping", "/ping", c => c.Player.Tell("{{reply}}"));
        }
        """;

    [Fact]
    public async Task A_module_stays_loaded_when_its_folder_goes_and_a_new_folder_waits_for_the_next_start()
    {
        string dir = AddScript("m", ModulePing("pong"));
        using var host = new ModHost(_root, _ => { }, modules: true);
        host.Start(watch: false);
        Assert.Throws<InvalidOperationException>(() => host.SetEnabled("m", false));
        Directory.Delete(dir, recursive: true);
        host.Reload("m");
        Assert.True(await host.RunCommand(_player, "ping", []));
        AddScript("later", ModulePing("later"));
        host.Reload();
        Assert.False(host.IsLoaded("later"));
    }

    [Fact]
    public async Task A_module_hot_reloads_and_bad_files_leave_the_old_one_running()
    {
        AddScript("m", ModulePing("one"));
        using var host = new ModHost(_root, _ => { }, modules: true);
        host.Start(watch: false);
        AddScript("m", "broken");
        host.Reload("m");
        Assert.True(await host.RunCommand(_player, "ping", []));
        AddScript("m", "public sealed class Throws : IModule { public void Load(IModuleContext module) => throw new Exception(); }");
        host.Reload("m");
        Assert.True(await host.RunCommand(_player, "ping", []));
        Assert.Equal(["one", "one"], _player.Told);
        AddScript("m", ModulePing("two"));
        host.Reload("m");
        Assert.True(await host.RunCommand(_player, "ping", []));
        Assert.Equal("two", _player.Told[^1]);
    }

    [Fact]
    public void A_mod_cannot_be_a_league()
    {
        AddMod("manifest", """{ "kind": "league" }""");
        _host.Start(watch: false);
        Assert.Contains("a league belongs in Leagues/", _host.Mods().Single(m => m.Id == "manifest").Error);
    }

    [Fact]
    public void Each_host_takes_only_its_own_entry_points()
    {
        AddScript("s", "public sealed class M : IMod { public void Load(IModContext mod) { } }");
        using var host = new ModHost(_root, _ => { }, modules: true);
        host.Start(watch: false);
        Assert.Contains("IModule", host.Mods().Single().Error);
    }

    [Fact]
    public async Task Hooks_of_modules_run_before_those_of_mods()
    {
        string other = _root + "-modules";
        Directory.CreateDirectory(Path.Combine(other, "first"));
        File.WriteAllText(Path.Combine(other, "first", "first.cs"), """
            public sealed class First : IModule
            {
                public void Load(IModuleContext module)
                {
                    module.ModifyExperience(e => e.Experience += 5);
                    module.OnChatCommand("both", "/both", c => c.Player.Tell("module"));
                }
            }
            """);
        AddScript("second", """
            public sealed class Second : IMod
            {
                public void Load(IModContext mod)
                {
                    mod.ModifyExperience(e => e.Experience *= 2);
                    mod.OnChatCommand("both", "/both", c => c.Player.Tell("mod"));
                }
            }
            """);
        using var modules = new ModHost(other, _ => { }, modules: true);
        modules.Start(watch: false);
        _host.Start(watch: false);
        var hooks = new ServerHooks(modules, _host);
        Assert.Equal(30u, hooks.Experience(_player, new("", "", 1, MonsterRarity.Normal, 0, 0), 10));
        Assert.True(await hooks.RunCommand(_player, "both", []));
        Assert.Equal(["module"], _player.Told);
    }

    void AddModule(string id, string manifest, string body = "")
    {
        string dir = AddScript(id, "public sealed class M_" + id + " : IModule { public void Load(IModuleContext module) { " + body + " } }");
        File.WriteAllText(Path.Combine(dir, ModHost.ModuleManifestFile), manifest);
    }

    [Fact]
    public async Task Core_modules_load_like_the_others_and_only_when_a_league_wants_them()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Core"));
        foreach (string id in new[] { "wanted", "unwanted" })
            AddScript(Path.Combine("Core", id), "public sealed class M_" + id + " : IModule { public void Load(IModuleContext module) => module.OnChatCommand(\"" + id + "\", \"/x\", c => c.Player.Tell(\"ran\")); }");
        using var host = new ModHost(_root, _ => { }, modules: true) { Wanted = () => new HashSet<string> { "wanted" } };
        host.Start(watch: false);
        Assert.True(host.IsLoaded("wanted"));
        Assert.False(host.IsLoaded("unwanted"));
        Assert.True(await host.RunCommand(_player, "wanted", []));
    }

    [Fact]
    public async Task A_league_compiles_its_source_into_its_binaries_and_runs_from_either()
    {
        string source = Path.Combine(_root, "Grim", "Source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(_root, "Grim", ModHost.LeagueManifestFile), """{ "parent": "Standard" }""");
        File.WriteAllText(Path.Combine(source, "Grim.cs"), "public sealed class Grim : League { public override void Load(IModuleContext league) => league.OnChatCommand(\"grim\", \"/grim\", c => c.Player.Tell(\"ran\")); }");
        using (var compiled = new ModHost(_root, _ => { }, modules: true, leagues: true))
        {
            compiled.Start(watch: false);
            Assert.True(await compiled.RunCommand(_player, "grim", []));
        }
        Directory.Delete(source, recursive: true);
        using var built = new ModHost(_root, _ => { }, modules: true, leagues: true);
        built.Start(watch: false);
        Assert.True(await built.RunCommand(_player, "grim", []));
    }

    [Fact]
    public async Task A_module_a_league_does_not_use_does_nothing_for_its_players()
    {
        AddModule("station", "{ }", """
            module.OnHideoutReady(p => p.Tell("placed"));
            module.OnObjectUsed(async u => { await u.Player.Tell("used"); return true; });
            module.OnChatCommand("station", "/station", c => c.Player.Tell("ran"));
            """);
        bool uses = true;
        using var host = new ModHost(_root, _ => { }, modules: true) { Active = (_, _) => uses };
        host.Start(watch: false);
        await host.HideoutReady(_player);
        Assert.True(await host.ObjectUsed(new(_player, 1, 0)));
        Assert.True(await host.RunCommand(_player, "station", []));
        Assert.Equal(["placed", "used", "ran"], _player.Told);
        uses = false;
        await host.HideoutReady(_player);
        Assert.False(await host.ObjectUsed(new(_player, 1, 0)));
        Assert.False(await host.RunCommand(_player, "station", []));
        Assert.Equal(3, _player.Told.Count);
    }

    [Fact]
    public async Task Chat_runs_a_mod_command_that_none_of_ours_shadows()
    {
        var told = new List<string>();
        var ran = new List<string>();
        var commands = new ChatCommands
        {
            Send = (_, _) => Task.CompletedTask,
            Reply = text => { told.Add(text); return Task.CompletedTask; },
            ModCommand = (name, args) => { ran.Add($"{name} {string.Join(' ', args)}"); return Task.FromResult(name is "hello" or "ours"); },
            ModUsages = () => [("hello", "/hello <who> (a mod's)"), ("ours", "/ours (shadowed)")],
        }.Add("ours", "/ours (the Gateway's)", _ => { told.Add("ours ran"); return Task.CompletedTask; });
        Assert.True(await commands.RunAsync("/hello you"));
        Assert.True(await commands.RunAsync("/ours"));
        Assert.False(await commands.RunAsync("/nothing"));
        Assert.Equal(["hello you", "nothing "], ran);
        Assert.Equal("ours ran", told[0]);
        Assert.Equal("/hello <who> (a mod's)", commands.Lookup("hello"));
        Assert.Equal("/ours (the Gateway's)", commands.Lookup("ours"));
        Assert.Contains("/hello <who>", commands.List(1));
    }

    sealed class FakePlayer : IPlayer
    {
        public List<string> Told { get; } = [];
        public string Character => "Tester";
        public string? Account => "account";
        public string League => "Standard";
        public bool Hardcore => false;
        public bool SoloSelfFound => false;
        public bool Ruthless => false;
        public string LeagueId { get; set; } = "standard";
        public bool Uses(string module) => LeagueId == module;
        public int Level => 12;
        public string AreaId => "1_1_1";
        public uint Seed => 7;
        public (int X, int Y) Position => (10, 20);
        public bool Closed => false;
        public Task Tell(string text) { lock (Told) Told.Add(text); return Task.CompletedTask; }
        public Task Send(byte[] message, string? log = null) => Task.CompletedTask;
    }
}
