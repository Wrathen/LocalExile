using GameServer.Modding;
using GameServer.Modules.Delve;
using GameServer.Storage;

namespace GameServer.Gateway.Tests;

[Collection(TestSaves.FailWriteCollection)]
public class DelveEquipmentTests
{
    [Fact]
    public void Upgrade_uses_the_next_native_price_and_cannot_upgrade_with_insufficient_azurite()
    {
        var start = new DelveProgress(Azurite: 100);
        var bought = Assert.IsType<DelveProgress>(DelveEquipment.UpgradeEquipment(start, 0));
        Assert.Equal(85u, bought.Azurite);
        Assert.Equal(1, bought.Level(0));
        Assert.Equal(1800, DelveEquipment.Value(bought, 0));
        Assert.Equal(0, start.Level(0));
        Assert.Null(DelveEquipment.UpgradeEquipment(bought, 0)); // next level costs 102
        Assert.Null(DelveEquipment.UpgradeEquipment(start, 6));
        Assert.Null(DelveEquipment.UpgradeEquipment(start, 65535));
    }

    [Fact]
    public void Consumables_cost_azurite_and_respect_capacity_and_bulk_purchase_limits()
    {
        var start = new DelveProgress(Azurite: 100);
        var flares = Assert.IsType<DelveProgress>(DelveEquipment.Purchase(start, true, 3));
        Assert.Equal(85u, flares.Azurite); Assert.Equal(3, flares.Flares);
        Assert.Null(DelveEquipment.Purchase(flares, true, 1));
        Assert.Null(DelveEquipment.Purchase(start, true, 0));
        Assert.Null(DelveEquipment.Purchase(start, true, 255));
        Assert.Null(DelveEquipment.Purchase(new(), true, 1));
        var dynamite = Assert.IsType<DelveProgress>(DelveEquipment.Purchase(start, false, 2));
        Assert.Equal(60u, dynamite.Azurite); Assert.Equal(2, dynamite.Dynamite);
    }

    [Fact]
    public void Equipment_is_persisted_per_account_and_league_and_a_failed_purchase_rolls_back()
    {
        TestSaves.Ensure();
        string account = "DelveEquipment-" + Guid.NewGuid().ToString("N");
        string key = AccountStash.Key(account, "Standard");
        var stash = AccountStash.For(key, Program.Items);
        Assert.True(DelveModule.ChangeEquipment(Saved(stash), p => p with { Azurite = 100 }));
        Assert.True(DelveModule.ChangeEquipment(Saved(stash), p => DelveEquipment.UpgradeEquipment(p, 0)));
        var saved = Saves.Load<AccountStash.Save>(SaveDatabase.Table.Stashes, key, CharacterSaves.Opts)!;
        var equipment = System.Text.Json.JsonSerializer.Deserialize<DelveProgress>(saved.ModuleData![DelveModule.EquipmentKey])!;
        Assert.Equal(85u, equipment.Azurite); Assert.Equal(1, equipment.Level(0));
        Assert.Equal(0u, DelveModule.Equipment(Saved(AccountStash.For(AccountStash.Key(account, "Hardcore"), Program.Items))).Azurite);
        try
        {
            Saves.FailWrite = k => k == key;
            Assert.Throws<IOException>(() => DelveModule.ChangeEquipment(Saved(stash), p => DelveEquipment.Purchase(p, true, 1)));
        }
        finally { Saves.FailWrite = null; }
        var reloaded = DelveModule.Equipment(Saved(AccountStash.For(key, Program.Items)));
        Assert.Equal(85u, reloaded.Azurite); Assert.Equal(0, reloaded.Flares);
        Assert.Equal(1, reloaded.Level(0));
    }

    static ISavedData Saved(AccountStash stash) => new StashData(stash);

    sealed class StashData(AccountStash stash) : ISavedData
    {
        public string? Get(string key) => stash.ModuleData(key);
        public bool Change(string key, Func<string?, string?> change) => stash.ChangeModuleData(key, change);
    }
}
