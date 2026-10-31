using GameServer.Core.Accounts;
using GameServer.Core.Protocol.Login;

namespace GameServer.Core.Tests;

public sealed class LeagueFolderTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), "localexile-leagues-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); }

    void League(string name, string json)
    {
        Directory.CreateDirectory(Path.Combine(_folder, name, "Source"));
        File.WriteAllText(Path.Combine(_folder, name, "league.json"), json);
    }

    [Fact]
    public void The_shipped_leagues_match_the_real_create_screen()
    {
        var leagues = LeagueCatalog.FromEnvironment();
        Assert.Empty(leagues.Errors);
        Assert.Equal(["Standard", "Allflame", "Hardcore Allflame"], leagues.Entries().Where(e => (e.Flags0 & LeagueEntry.Main) != 0).Select(e => e.Name).OrderBy(n => n != "Standard").ThenBy(n => n.Length));
        Assert.Equal("Standard", leagues.Find("Hardcore")!.EntryOf);
        Assert.Null(leagues.Find("Void"));
        Assert.NotNull(leagues.FindLoaded("Void"));
        Assert.Equal("Allflame", leagues.Find("Hardcore Allflame")!.Parent);
        Assert.Equal("Ruthless", leagues.Find("Hardcore Ruthless")!.Parent);
        Assert.False(leagues.Find("Hardcore Ruthless")!.PermanentDeath);
    }

    [Fact]
    public void A_parent_chain_that_misses_the_roots_does_not_load()
    {
        League("Standard", "{ }");
        League("Void", "{ }");
        League("Good", """{ "parent": "Void" }""");
        League("Lost", """{ "parent": "Gone" }""");
        League("Twice", """{ "parent": "Standard", "addModules": ["a", "a"] }""");
        var leagues = LeagueCatalog.FromFolder(_folder);
        Assert.Equal(["Good", "Standard", "Void"], leagues.Loaded.Select(l => l.Name).Order());
        Assert.Equal(2, leagues.Errors.Count);
    }

    [Fact]
    public void An_ended_league_is_not_offered_and_cannot_be_entered()
    {
        League("Standard", "{ }");
        League("Over", """{ "parent": "Standard", "ended": true }""");
        var leagues = LeagueCatalog.FromFolder(_folder);
        Assert.Null(leagues.Find("Over"));
        Assert.False(leagues.CanEnter("Over"));
        Assert.True(leagues.CanEnter("Standard"));
    }

    [Fact]
    public void A_saved_league_with_no_folder_keeps_its_name_and_is_not_loaded()
    {
        var saved = LeagueCatalog.FromFolder(_folder).ResolveSavedLeague("Gone")!;
        Assert.Equal(("Gone", false), (saved.Name, saved.Loaded));
    }
}
