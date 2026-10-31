using GameServer.Core.Accounts;

namespace GameServer.Gateway.Tests;

public sealed class LeagueModuleTests
{
    static readonly LeagueCatalog Leagues = LeagueCatalog.FromFolder("Leagues");
    static readonly string[] Core = ["harvest", "heist", "delve"];

    [Fact]
    public void A_league_gets_core_less_what_it_removes_plus_what_it_adds_each_once()
    {
        var league = new League("Grim", "Grim", false, false) { RemoveModules = ["harvest"], AddModules = ["ruthless", "heist"] };
        Assert.Equal(["delve", "heist", "ruthless"], ServerLeagues.ModulesOf(league, Core).Order());
    }

    [Fact]
    public void Void_has_no_modules_and_Standard_has_core()
    {
        Assert.Empty(ServerLeagues.ModulesOf(Leagues.FindLoaded("Void")!, Core));
        Assert.Equal(Core.Order(), ServerLeagues.ModulesOf(Leagues.FindLoaded("Standard")!, Core).Order());
    }

    [Fact]
    public void A_module_no_league_lists_is_not_wanted()
    {
        var wanted = ServerLeagues.Wanted(Leagues, Core);
        Assert.Contains("ruthless", wanted);
        Assert.DoesNotContain("nobody-lists-this", wanted);
    }

    [Theory]
    [InlineData("Ruthless")]
    [InlineData("Hardcore Ruthless")]
    [InlineData("HC SSF R Allflame")]
    public void Every_ruthless_league_has_the_hardmode_data_and_rules(string name)
    {
        var league = ServerLeagues.Resolve(null, name, Leagues, Core);
        Assert.True(league.HardMode);
        Assert.True(league.Rules.PersistsVitals);
        Assert.False(league.Rules.AllowsGemBase("Metadata/Items/Gems/SkillGemFrostblink"));
    }

    [Theory]
    [InlineData("Standard")]
    [InlineData("Hardcore")]
    [InlineData("Allflame")]
    public void Other_leagues_have_the_default_rules(string name)
    {
        var league = ServerLeagues.Resolve(null, name, Leagues, Core);
        Assert.False(league.HardMode);
        Assert.False(league.Rules.PersistsVitals);
    }

    [Fact]
    public void A_league_that_is_not_loaded_falls_back_for_what_still_reaches_the_gateway()
    {
        Assert.Same(LeagueRuntime.Standard, ServerLeagues.Resolve(null, "Gone", Leagues, Core));
    }

    [Fact]
    public void Each_rules_setting_is_the_modules_with_no_league_named_in_the_framework()
    {
        var ruthless = ServerLeagues.Resolve(null, "Ruthless", Leagues, Core).Rules;
        Assert.False(ruthless.SellsGemsAt("Metadata/NPC/Act1/Nessa"));
        Assert.True(ruthless.SellsGemsAt("Metadata/NPC/Act3/Siosa"));
        Assert.True(ruthless.RefillsVitalsAt("Metadata/NPC/Act1/Nessa"));
        Assert.False(ruthless.RefillsVitalsAt("Metadata/NPC/Act3/Siosa"));
        Assert.True(GameServer.Modding.LeagueRules.Standard.SellsGemsAt("Metadata/NPC/Act1/Nessa"));
    }
}
