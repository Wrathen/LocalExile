using GameServer.Core.Accounts;
using GameServer.Core.Protocol.Login;
using GameServer.Storage;

namespace GameServer.Core.Tests;

public class HardcoreDeathRulesTests
{
    static readonly DateTimeOffset DiedAt = DateTimeOffset.Parse("2026-10-26T12:00:00Z");
    static Character NewCharacter(string league) => new(Guid.NewGuid(), Guid.NewGuid(), "DeathProbe", CharacterClass.Witch,
        12, league, "1_1_2", DiedAt.AddDays(-1), DiedAt, "1_1_town", 3);

    [Theory]
    [InlineData("Hardcore", "Standard", false)]
    public void Death_retains_base_and_ssf_unless_ruthless_is_permanently_dead(string league, string target, bool terminal)
    {
        var source = LeagueCatalog.FromEnvironment().ResolveSavedLeague(league)!;
        var original = NewCharacter(league);
        var dead = HardcoreDeathRules.Apply(original, source, DiedAt);
        Assert.Equal(target, dead.League);
        Assert.Equal(new HardcoreDeathRecord(league, DiedAt, terminal), dead.HardcoreDeath);
        Assert.Equal(original with { League = target, HardcoreDeath = dead.HardcoreDeath }, dead);
        Assert.Equal(!terminal, HardcoreDeathRules.CanEnter(dead));
        Assert.Same(dead, HardcoreDeathRules.Apply(dead, source, DiedAt.AddMinutes(1)));
    }

}
