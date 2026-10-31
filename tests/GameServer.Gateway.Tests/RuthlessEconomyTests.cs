using GameServer.Core.Protocol.Instance;

namespace GameServer.Gateway.Tests;

public class RuthlessEconomyTests
{
    static string Data => BenchCraftingTests.Data();
    static readonly Lazy<ItemCatalog> Catalog = new(() => new(Data, hardMode: true));
    static PlayerInventories.Shape? Shape(Item item) => Catalog.Value.ByHash(item.TypeHash) is { } t
        ? new(t.Width, t.Height, t.Class, t.MaxStack) : null;

    [Fact]
    public void Ruthless_quest_rewards_exclude_normal_supports_movement_and_select_the_token_alternatives()
    {
        var rewards = QuestRewardCatalog.Load(Data, hardMode: true, supportGems: false);
        Assert.Empty(rewards.Choices(2, 1)); // Mercy Mission's regular support offer.
        Assert.Empty(rewards.Choices(4, 1)); // Breaking Some Eggs' regular movement offer.
        Assert.DoesNotContain(rewards.Choices(7, 1), c => Catalog.Value.Row(c.ItemRow)!.Name == "Flame Dash");
        foreach (int offer in new[] { 24, 37, 65 })
            Assert.Equal("Uncarved Gemstone", Catalog.Value.Row(Assert.Single(rewards.Choices(offer, 1)).ItemRow)!.Name);
        Assert.All(rewards.Offers.SelectMany(o => o.Choices), c =>
            Assert.False(Catalog.Value.Gem(Catalog.Value.Row(c.ItemRow)!)?.IsSupport ?? false));
        var regular = QuestRewardCatalog.Load(Data);
        Assert.DoesNotContain(regular.Choices(24, 1), c => Catalog.Value.Row(c.ItemRow)!.Name == "Uncarved Gemstone");
    }

}
