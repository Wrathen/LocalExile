using System.Text.Json.Nodes;
using GameServer.Core.Accounts;
using GameServer.Core.Protocol;
using GameServer.Core.World;
using GameServer.Storage;

namespace GameServer.Core.Tests;

public class GameSessionModeTests
{
    [Theory]
    [InlineData("Hardcore Allflame", false)]
    public void A_committed_death_blocks_stale_dev_area_entry_and_permanently_dead_world_entry(string league, bool permanent)
    {
        var store = new JsonAccountStore();
        var accounts = new AccountService(store);
        var account = accounts.Register("ModeAccount", "test-password").Value!;
        var character = accounts.CreateCharacter(account.Id, "ModeProbe", CharacterClass.Witch, league).Value!;
        var instances = new InstanceManager();
        var session = new GameSession(accounts, instances);
        Assert.True(JsonNode.Parse(session.Handle("{\"op\":\"login\",\"name\":\"ModeAccount\",\"password\":\"test-password\"}"))!["ok"]!.GetValue<bool>());
        Assert.True(JsonNode.Parse(session.Handle("{\"op\":\"enter_world\",\"name\":\"ModeProbe\"}"))!["ok"]!.GetValue<bool>());
        store.UpdateCharacter(HardcoreDeathRules.Apply(character, LeagueCatalog.FromEnvironment().Find(league)!, DateTimeOffset.UtcNow));
        var change = JsonNode.Parse(session.Handle("{\"op\":\"enter_area\",\"area\":\"1_1_town\"}"))!;
        Assert.False(change["ok"]!.GetValue<bool>());
        Assert.Null(instances.Current(character.Id));
        var again = JsonNode.Parse(session.Handle("{\"op\":\"enter_world\",\"name\":\"ModeProbe\"}"))!;
        Assert.Equal(!permanent, again["ok"]!.GetValue<bool>());
        if (!permanent) Assert.Equal("ALLFLAME", instances.Current(character.Id)!.League);
    }
}
