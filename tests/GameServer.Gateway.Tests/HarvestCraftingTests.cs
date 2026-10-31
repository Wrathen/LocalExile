using GameServer.Core.Protocol.Instance;
using GameServer.Modules.Harvest;

namespace GameServer.Gateway.Tests;

public class HarvestCraftingTests
{
    static readonly Lazy<(ItemCatalog Catalog, ItemRolls Rolls, HarvestCrafting Harvest)> Setup = new(() =>
    {
        string data = BenchCraftingTests.Data();
        var catalog = new ItemCatalog(data);
        var rolls = new ItemRolls(catalog, data);
        return (catalog, rolls, new HarvestCrafting(new ModuleItemCrafting(catalog, rolls), data));
    });

    static HarvestCrafting Harvest => Setup.Value.Harvest;
    static ItemCatalog Catalog => Setup.Value.Catalog;
    static ItemRolls Rolls => Setup.Value.Rolls;
    static ItemCatalog.BaseType Ring() => Catalog.Find("Two-Stone Ring")!;

    static Item Rare(ItemCatalog.BaseType type, int seed)
    {
        var rng = new Random(seed);
        return Rolls.Roll(ItemCatalog.Create(type) with { ItemLevel = 100 }, ItemRarity.Rare, rng, (4, 3)) with { Identified = true };
    }

    [Fact]
    public void Every_raw_row_loads_with_its_hash_and_supported_item_recipes_have_their_lifeforce_cost()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(BenchCraftingTests.Data(), "tables/HarvestCraftOptions.json")));
        Assert.Equal(doc.RootElement.GetArrayLength(), Harvest.All.Count);
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            var recipe = Harvest.ByHash((ushort)row.GetProperty("HASH16").GetInt32());
            Assert.NotNull(recipe);
            Assert.Equal(row.GetProperty("Id").GetString(), recipe!.Id);
        }
        var fire = Harvest.ByHash(0xa3e5)!;
        Assert.Equal("ReforgeFire", fire.Id);
        Assert.Equal(HarvestCrafting.Operation.ReforgeTag, fire.Operation);
        Assert.Equal("fire", fire.Tag);
        Assert.Equal([(Catalog.Find(HarvestCrafting.Wild)!.Hash, 50)], fire.Costs);
        Assert.Equal(41, Harvest.All.Count(r => r.Supported));
    }

}
