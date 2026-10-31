using GameServer.Core.Protocol.Instance;
using GameServer.Modules.Harvest;

namespace GameServer.Gateway.Tests;

[Collection(TestSaves.FailWriteCollection)]
public class HarvestPaymentTests : IDisposable
{
    readonly string name = "Harvest" + Guid.NewGuid().ToString("N");
    const byte Main = PlayerInventories.MainInventory, Slot = HarvestModule.Slot;
    static ItemCatalog Catalog => BenchCraftingTests.Setup.Value.Catalog;
    static ItemRolls Rolls => BenchCraftingTests.Setup.Value.Rolls;
    static HarvestCrafting Harvest => new(new ModuleItemCrafting(Catalog, Rolls));
    PlayerInventories Items => PlayerInventories.For(name, Catalog);
    AccountStash Stash => AccountStash.For(name + "Account", Catalog);
    public HarvestPaymentTests() => TestSaves.Ensure();
    public void Dispose() => Saves.FailWrite = null;
    static PlayerInventories.Shape? Shape(Item i) => Catalog.ByHash(i.TypeHash) is { } t ? new(t.Width, t.Height, t.Class, t.MaxStack) : null;
    static Item Currency(uint hash, int count) => ItemCatalog.Create(Catalog.ByHash(hash)!) with { StackSize = (ushort)count };
    void Deposit(uint hash, int count, int tab = 0) => Assert.Equal(0, Stash.AddStacked(tab, Currency(hash, count), Shape, out _)!.Left);
    static HarvestCrafting.Recipe Fire => Harvest.ByHash(0xa3e5)!;

    InventoryItem Target()
    {
        var type = Catalog.Find("Two-Stone Ring")!;
        var item = Rolls.Roll(ItemCatalog.Create(type) with { ItemLevel = 100 }, ItemRarity.Rare, new Random(11), (4, 3))
            with { Identified = true };
        return Items.Add(Slot, 0, 0, item);
    }

    [Fact]
    public void Split_inventory_and_stash_lifeforce_pays_exactly_the_recipe_and_persists_the_item()
    {
        var target = Target();
        var (currency, amount) = Fire.Costs.Single();
        Items.Add(Main, 0, 0, Currency(currency, 2));
        Deposit(currency, 2);
        Deposit(currency, amount + 5, 1);

        var result = ItemMoves.CraftInSlot(Items, Stash, Slot, item => Harvest.Apply(Fire.Hash, item, new Random(12)));

        Assert.NotNull(result);
        Assert.Equal(0, Items.Count(Main, currency));
        Assert.Equal(9, Stash.CountOf(currency));
        var saved = CharacterSaves.Load(name)!.Inventories[Slot].Single(i => i.Id == target.Id);
        Assert.Contains(saved.Item.Mods!, m => m.Section == ModSection.Explicit &&
            Catalog.Mods.ById(m.Id)?.Tags.Contains("fire") == true);
    }

}
