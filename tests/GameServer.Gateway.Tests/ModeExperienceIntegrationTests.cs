using GameServer.Core.Protocol.Instance;

namespace GameServer.Gateway.Tests;

public sealed partial class AscendancyGatewayIntegrationTests
{
    [Fact]
    public void Ruthless_old_area_gains_scale_from_the_shared_current_XP_level()
    {
        using var game = new GatewayCharacter(level: 80, family: AscendancyFamily.Normal, choice: "Slayer", ruthless: true);
        var oldArea = game.Reload();
        oldArea.Pools = Program.PoolsFor(oldArea, 1);
        oldArea.Experience = CharacterStats.ExperienceFor(1);
        uint before = game.State.Experience;
        var rules = ServerLeagues.Live("ruthless")!;
        const uint gain = 10_000;
        uint scaled = GameServer.Modding.LeagueRules.Scale(gain, rules.ExperienceMultiplier(80));

        var first = Program.RecordExperienceGain(game.State, gain);
        if (scaled == 0)
        {
            Assert.Null(first);
            Assert.Null(Program.RecordExperienceGain(oldArea, gain));
            Assert.Equal(before, CharacterSaves.Load(game.Character)!.Experience);
            return;
        }
        Assert.NotNull(first);
        Assert.Equal(before, first.Before);
        Assert.Equal(before + scaled, first.After);
        Assert.Equal(80, GameServer.Core.World.Progression.LevelFor(first.After, CharacterStats.LevelStarts));

        var second = Program.RecordExperienceGain(oldArea, gain);
        Assert.NotNull(second);
        Assert.Equal(first.After, second.Before);
        Assert.Equal(first.After + scaled, second.After);
        Assert.Equal(second.After, oldArea.Experience);
        Assert.Equal(second.After, CharacterSaves.Load(game.Character)!.Experience);
    }

}
