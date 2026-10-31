using GameServer.Core.Protocol.Instance;

namespace GameServer.Gateway.Tests;

public class AtlasCatalogTests
{
    static ItemCatalog Catalog => BenchCraftingTests.Setup.Value.Catalog;

    [Fact]
    public void Loads_the_normal_atlas_layouts_and_a_starting_default()
    {
        var atlas = new AtlasCatalog(Catalog);
        Assert.True(atlas.Layouts.Count > 100);
        Assert.NotNull(atlas.Default);
        Assert.True(atlas.Default!.Starting);
        Assert.Equal("Cells", atlas.Default.Name);
    }

    [Fact]
    public void Map_drops_rise_one_tier_for_magic_and_rare_and_two_for_bosses()
    {
        var atlas = new AtlasCatalog(Catalog);
        var drops = new ItemDrops(Catalog, BenchCraftingTests.Setup.Value.Rolls, maps: atlas);
        string? Name(MonsterRarity rarity)
        {
            for (int seed = 0; seed < 100_000; seed++)
                if (drops.Map(83, rarity, 5, new Random(seed)) is { } drop) return drop.Type.Name;
            return null;
        }
        Assert.StartsWith("Map (Tier 5)", Name(MonsterRarity.Normal));
        Assert.StartsWith("Map (Tier 6)", Name(MonsterRarity.Magic));
        Assert.StartsWith("Map (Tier 6)", Name(MonsterRarity.Rare));
        Assert.StartsWith("Map (Tier 7)", Name(MonsterRarity.Unique));
    }

    [Fact]
    public void Completing_a_layout_unlocks_its_connected_nodes()
    {
        var atlas = new AtlasCatalog(Catalog);
        IReadOnlySet<string> Completed(params string[] ids) => new HashSet<string>(ids);
        var cells = atlas.Find("MapWorldsCells")!;
        Assert.NotEmpty(cells.Connections);
        var unlocked = atlas.Unlocked(Completed());
        Assert.Contains(cells.Hash16, unlocked);
        Assert.DoesNotContain(cells.Connections[0], unlocked);
        var after = atlas.Unlocked(Completed("MapWorldsCells"));
        Assert.Contains(cells.Connections[0], after);
        Assert.True(atlas.IsUnlocked(atlas.ByHash16(cells.Connections[0])!, Completed("MapWorldsCells")));
    }

    [Fact]
    public void Loads_the_atlas_passive_nodes()
    {
        var nodes = new AtlasPassives();
        Assert.True(nodes.Count > 1000);
        Assert.NotNull(nodes.FindById("atlas_abyss_1_1"));
        Assert.Equal("Abyss Chance", nodes.FindById("atlas_abyss_1_1")!.Name);
        Assert.NotNull(nodes.FindById("atlas_start_node"));
        Assert.Equal(6, nodes.MapDropChancePercent([64048, 57739, 10884]));
    }

}
