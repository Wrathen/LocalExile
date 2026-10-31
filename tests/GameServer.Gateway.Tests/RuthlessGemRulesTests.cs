using GameServer.Core.Protocol.Instance;

namespace GameServer.Gateway.Tests;

public class RuthlessGemRulesTests
{
    static readonly Lazy<GameServer.Modding.LeagueRules> Rules = new(() => ServerLeagues.Live("ruthless")!);

    [Theory]
    [InlineData("SkillGemFrostblink", "FrostblinkHardMode")]
    public void Explicit_unavailable_rules_cover_regular_alternate_and_hardmode_ids(string basis, string effect)
    {
        Assert.False(Rules.Value.AllowsGemBase("Metadata/Items/Gems/" + basis));
        Assert.False(Rules.Value.AllowsGemBase("Metadata/Items/GemAlternateEffects/" + basis + "AltX"));
        Assert.False(Rules.Value.AllowsGemEffect(effect));
        Assert.False(Rules.Value.AllowsGemEffect(effect + "AltX"));
    }

}
