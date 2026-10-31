using GameServer.Core.Protocol.Instance;

namespace GameServer.Gateway.Tests;

public class RuthlessGameplayTests
{
    static string Data()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "data", "tables"))) return Path.Combine(dir.FullName, "data");
        throw new DirectoryNotFoundException("data/tables");
    }

    [Fact]
    public void Scarcity_parameters_change_real_drop_generation_and_zero_disables_it()
    {
        var catalog = new ItemCatalog(Data(), hardMode: true);
        var drops = new ItemDrops(catalog, new ItemRolls(catalog, Data()), Data());
        int normal = 0, scarce = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            normal += drops.Roll(30, MonsterRarity.Rare, new Random(seed)).Count;
            scarce += drops.Roll(30, MonsterRarity.Rare, new Random(seed), quantityMultiplier: 0.2).Count;
            Assert.Empty(drops.Roll(30, MonsterRarity.Rare, new Random(seed), quantityMultiplier: 0));
            Assert.Null(drops.Gold(30, MonsterRarity.Rare, new Random(seed), multiplier: 0));
            Assert.All(drops.Roll(30, MonsterRarity.Rare, new Random(seed), rarityMultiplier: 0),
                d => Assert.Equal(ItemRarity.Normal, d.Item.Rarity));
        }
        Assert.InRange(scarce, (int)(normal * 0.1), (int)(normal * 0.3));
    }

}
