using System.Net;
using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

namespace GameServer.Gateway.Tests;

public class FlaskEnchantGatewayTests
{
    const byte Main = PlayerInventories.MainInventory;

    static ItemMod SingleEnchant(Item item) => Assert.Single(item.Mods, m => m.Section == ModSection.Enchant);
    static ItemMod[] Enchants(Item item) => [.. item.Mods.Where(m => m.Section == ModSection.Enchant)];

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task Ordinary_and_shift_use_add_an_enchant_spend_one_and_persist(
        bool currencyInStash, bool targetInStash, bool shift)
    {
        await WithSession(async (conn, items, stash, send) =>
        {
            var currency = Put(items, stash, currencyInStash,
                ItemCatalog.Create(Program.Items.Find(CurrencyEffects.EnkindlingOrb)!) with { StackSize = 2 }, 0);
            var target = Put(items, stash, targetInStash,
                ItemCatalog.Create(Program.Items.Find("Diamond Flask")!) with { ItemLevel = 84, Quality = 17 }, 4);
            var request = Request(stash, currency, target, currencyInStash, targetInStash, shift);

            await send(request);

            var after = Current(items, stash, targetInStash, target.Id);
            Assert.Equal(21, Program.Items.Mods.ByHash(SingleEnchant(after.Item).Hash)!.GenerationType);
            Assert.Equal(17, after.Item.Quality);
            Assert.Equal(1, Current(items, stash, currencyInStash, currency.Id).Item.StackSize);

            // Forget both runtime caches: assert the database, not merely the returned objects.
            PlayerInventories.Forget(conn.Character!);
            AccountStash.Forget(conn.Account!);
            var reloadedItems = PlayerInventories.For(conn.Character!, Program.Items);
            var reloadedStash = AccountStash.For(conn.Account!, Program.Items);
            var saved = Current(reloadedItems, reloadedStash, targetInStash, target.Id).Item;
            Assert.Equal(21, Program.Items.Mods.ByHash(SingleEnchant(saved).Hash)!.GenerationType);
            Assert.Equal(1, Current(reloadedItems, reloadedStash, currencyInStash, currency.Id).Item.StackSize);
        });
    }

    [Fact]
    public async Task Enkindling_and_Instilling_coexist_and_each_use_spends_one()
    {
        await WithSession(async (conn, items, stash, send) =>
        {
            var enkindling = Put(items, stash, false,
                ItemCatalog.Create(Program.Items.Find(CurrencyEffects.EnkindlingOrb)!) with { StackSize = 2 }, 0);
            var instilling = Put(items, stash, false,
                ItemCatalog.Create(Program.Items.Find(CurrencyEffects.InstillingOrb)!) with { StackSize = 2 }, 1);
            var targetItem = Program.Rolls.Roll(
                ItemCatalog.Create(Program.Items.Find("Diamond Flask")!) with { ItemLevel = 84 }, ItemRarity.Magic, new Random(7)) with
                { Quality = 17 };
            var target = Put(items, stash, false, targetItem, 4);
            var explicitBefore = target.Item.Mods.Where(m => m.Section == ModSection.Explicit).ToArray();

            await send(Request(stash, enkindling, target, false, false, false));
            var first = Current(items, stash, false, target.Id);
            Assert.Equal(21, Program.Items.Mods.ByHash(SingleEnchant(first.Item).Hash)!.GenerationType);
            Assert.Equal(1, Current(items, stash, false, enkindling.Id).Item.StackSize);
            Assert.Equal(2, Current(items, stash, false, instilling.Id).Item.StackSize);
            Assert.Equal(explicitBefore, first.Item.Mods.Where(m => m.Section == ModSection.Explicit));

            await send(Request(stash, instilling, target, false, false, false));
            var second = Current(items, stash, false, target.Id);
            Assert.Equal(2, Enchants(second.Item).Length);
            Assert.Single(Enchants(second.Item), m => Program.Items.Mods.ByHash(m.Hash)!.GenerationType == 21);
            Assert.Single(Enchants(second.Item), m => Program.Items.Mods.ByHash(m.Hash)!.GenerationType == 22);
            Assert.Equal(1, Current(items, stash, false, enkindling.Id).Item.StackSize);
            Assert.Equal(1, Current(items, stash, false, instilling.Id).Item.StackSize);
            Assert.Equal(explicitBefore, second.Item.Mods.Where(m => m.Section == ModSection.Explicit));
            Assert.Equal(17, second.Item.Quality);

            PlayerInventories.Forget(conn.Character!);
            var reloaded = PlayerInventories.For(conn.Character!, Program.Items);
            var saved = Current(reloaded, stash, false, target.Id).Item;
            Assert.Equal(2, Enchants(saved).Length);
            Assert.Single(Enchants(saved), m => Program.Items.Mods.ByHash(m.Hash)!.GenerationType == 21);
            Assert.Single(Enchants(saved), m => Program.Items.Mods.ByHash(m.Hash)!.GenerationType == 22);
        });
    }

    [Theory]
    [InlineData("Small Life Flask")]
    [InlineData("Small Mana Flask")]
    [InlineData("Driftwood Wand")]
    public async Task Ineligible_targets_are_refused_without_spending(string name)
    {
        await WithSession(async (conn, items, stash, send) =>
        {
            var currency = Put(items, stash, false,
                ItemCatalog.Create(Program.Items.Find(CurrencyEffects.EnkindlingOrb)!) with { StackSize = 2 }, 0);
            var target = Put(items, stash, false, ItemCatalog.Create(Program.Items.Find(name)!) with { ItemLevel = 84 }, 4);
            await send(Request(stash, currency, target, false, false, false));
            Assert.Equal(target, Current(items, stash, false, target.Id));
            Assert.Equal(2, Current(items, stash, false, currency.Id).Item.StackSize);
        });
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Corrupted_and_mirrored_utility_flasks_are_refused_without_spending(bool corrupted, bool mirrored)
    {
        await WithSession(async (conn, items, stash, send) =>
        {
            var currency = Put(items, stash, false,
                ItemCatalog.Create(Program.Items.Find(CurrencyEffects.InstillingOrb)!) with { StackSize = 2 }, 0);
            var target = Put(items, stash, false,
                ItemCatalog.Create(Program.Items.Find("Diamond Flask")!) with { Corrupted = corrupted, Mirrored = mirrored }, 4);
            await send(Request(stash, currency, target, false, false, false));
            Assert.Equal(target, Current(items, stash, false, target.Id));
            Assert.Equal(2, Current(items, stash, false, currency.Id).Item.StackSize);
        });
    }

    static async Task WithSession(Func<ClientState, PlayerInventories, AccountStash,
        Func<byte[], Task>, Task> action)
    {
        TestSaves.Ensure();
        string name = "FlaskEnchant" + Guid.NewGuid().ToString("N");
        var conn = new ClientState(new(ushort.MaxValue, "FlaskEnchantTest", 128, 128, 32, 32, null, []), 0, 1)
        { Character = name, Account = name, League = "Standard" };
        var items = PlayerInventories.For(name, Program.Items);
        var stash = AccountStash.For(name, Program.Items);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var writer = new TcpClient();
        var accepting = listener.AcceptTcpClientAsync();
        await writer.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var reader = await accepting;
        var ciphers = new ConnectionCiphers(new byte[64], isClient: false);
        try
        {
            Task Send(byte[] packet) => Program.OnClientMessages("flask-enchant-test", writer.GetStream(), ciphers,
                [((ushort)(packet[0] << 8 | packet[1]), packet)], [], conn).WaitAsync(TimeSpan.FromSeconds(5));
            await action(conn, items, stash, Send);
        }
        finally
        {
            PlayerInventories.Forget(name);
            AccountStash.Forget(name);
        }
    }

    static InventoryItem Put(PlayerInventories items, AccountStash stash, bool inStash, Item item, byte x) =>
        inStash ? stash.Place(0, x, 0, new InventoryItem(1, 0, 0, item), Shape, out _)!.Value.Placed
            : items.Add(Main, x, 0, item);

    static PlayerInventories.Shape? Shape(Item item) => Program.Items.ByHash(item.TypeHash) is { } t
        ? new(t.Width, t.Height, t.Class, t.MaxStack) : null;

    static InventoryItem Current(PlayerInventories items, AccountStash stash, bool inStash, uint id) =>
        (inStash ? stash.Inventory(0).Items : items.Items(Main)).Single(i => i.Id == id);

    static byte[] Request(AccountStash stash, InventoryItem currency, InventoryItem target,
        bool currencyInStash, bool targetInStash, bool shift)
    {
        uint targetInventory = targetInStash ? stash.InventoryOf(0) : PlayerInventories.InventoryId(Main);
        return shift ? new LoginWriter().U16(0x72).U32(currency.Item.TypeHash).U32(targetInventory).U32(target.Id)
                .Bytes(new byte[9]).ToArray()
            : new LoginWriter().U16(0x71).U32(currencyInStash ? stash.InventoryOf(0) : PlayerInventories.InventoryId(Main))
                .U32(currency.Id).U32(targetInventory).U32(target.Id).ToArray();
    }
}
