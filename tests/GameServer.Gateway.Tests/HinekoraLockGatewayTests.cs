using System.Net;
using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

namespace GameServer.Gateway.Tests;

public class HinekoraLockGatewayTests
{
    const byte Main = PlayerInventories.MainInventory;

    sealed record Preview(ushort Key, ushort Status, ulong Count, byte[] Payload);

    [Fact]
    public async Task Lock_application_sets_the_flag_and_persists()
    {
        await WithSession(async (conn, items, stash, send, _, _) =>
        {
            var currency = Put(items, stash, false,
                ItemCatalog.Create(Program.Items.Find(CurrencyEffects.HinekorasLock)!) with { StackSize = 2 }, 0);
            var target = Put(items, stash, false, FourModWand(), 4);

            await send(Request(currency, target));

            var after = Current(items, target.Id);
            Assert.True(after.Item.Foreseeing);
            Assert.NotEqual(0u, after.Item.ForeseeSeed);
            Assert.Equal(1, Current(items, currency.Id).Item.StackSize);

            PlayerInventories.Forget(conn.Character!);
            var reloaded = PlayerInventories.For(conn.Character!, Program.Items);
            var saved = reloaded.Items(Main).Single(i => i.Id == target.Id).Item;
            Assert.True(saved.Foreseeing);
            Assert.Equal(after.Item.ForeseeSeed, saved.ForeseeSeed);
        });
    }

    [Fact]
    public async Task A_second_lock_is_refused_without_spending()
    {
        await WithSession(async (conn, items, stash, send, _, _) =>
        {
            var currency = Put(items, stash, false,
                ItemCatalog.Create(Program.Items.Find(CurrencyEffects.HinekorasLock)!) with { StackSize = 2 }, 0);
            var target = Put(items, stash, false, FourModWand(), 4);

            await send(Request(currency, target));
            var first = Current(items, target.Id);
            await send(Request(currency, target));

            Assert.Equal(first, Current(items, target.Id));
            Assert.Equal(1, Current(items, currency.Id).Item.StackSize);
        });
    }

    [Fact]
    public async Task Gems_corrupted_mirrored_and_unmodifiable_targets_are_refused_without_spending()
    {
        await WithSession(async (conn, items, stash, send, _, _) =>
        {
            var currency = Put(items, stash, false,
                ItemCatalog.Create(Program.Items.Find(CurrencyEffects.HinekorasLock)!) with { StackSize = 2 }, 0);
            var gem = Put(items, stash, false,
                ItemCatalog.Create(Program.Items.Find("Metadata/Items/Gems/SkillGemFireball")!), 4);
            var corrupted = Put(items, stash, false, FourModWand() with { Corrupted = true }, 5);
            var mirrored = Put(items, stash, false, FourModWand() with { Mirrored = true }, 6);
            var map = Put(items, stash, false, Rare(Program.Items.Find("Metadata/Items/Maps/MapWorldsSanctuary")!,
                Explicit("LocalIncreasedPhysicalDamagePercent1"), Explicit("IncreasedManaWeapon1"),
                Explicit("LocalIncreasedAttackSpeed1"), Explicit("Intelligence1")), 7);

            foreach (var target in new[] { gem, corrupted, mirrored, map })
            {
                await send(Request(currency, target));
                Assert.Equal(target, Current(items, target.Id));
                Assert.Equal(2, Current(items, currency.Id).Item.StackSize);
            }
        });
    }

    [Fact]
    public async Task The_preview_request_answers_with_the_preview_and_spends_nothing()
    {
        await WithSession(async (conn, items, stash, send, sendRaw, readPreview) =>
        {
            var locked = LockedWand();
            var target = Put(items, stash, false, locked, 4);
            var currency = Put(items, stash, false,
                ItemCatalog.Create(Program.Items.Find(CurrencyEffects.ChaosOrb)!) with { StackSize = 2 }, 0);

            await sendRaw(PreviewRequest(7, currency, target));

            var preview = await readPreview();
            Assert.Equal(7, preview.Key);
            Assert.Equal((ushort)0, preview.Status);
            Assert.Equal(1ul, preview.Count);
            Assert.Equal(target.Item.TypeHash, BitConverter.ToUInt32(preview.Payload, 0));
            Assert.True(Current(items, target.Id).Item.Foreseeing);
            Assert.Equal(2, Current(items, currency.Id).Item.StackSize);
        });
    }

    [Fact]
    public async Task Committing_the_currency_applies_exactly_the_preview_and_clears_the_state()
    {
        await WithSession(async (conn, items, stash, send, sendRaw, readPreview) =>
        {
            var locked = LockedWand();
            var target = Put(items, stash, false, locked, 4);
            var currency = Put(items, stash, false,
                ItemCatalog.Create(Program.Items.Find(CurrencyEffects.ChaosOrb)!) with { StackSize = 2 }, 0);

            await sendRaw(PreviewRequest(9, currency, target));
            var preview = await readPreview();
            await send(Request(currency, target));

            var after = Current(items, target.Id).Item;
            Assert.False(after.Foreseeing);
            Assert.Equal(0u, after.ForeseeSeed);
            Assert.Equal(preview.Payload, after.Payload());
            Assert.Equal(1, Current(items, currency.Id).Item.StackSize);

            PlayerInventories.Forget(conn.Character!);
            var saved = PlayerInventories.For(conn.Character!, Program.Items).Items(Main).Single(i => i.Id == target.Id).Item;
            Assert.False(saved.Foreseeing);
            Assert.Equal(preview.Payload, saved.Payload());
        });
    }

    [Fact]
    public async Task A_refused_currency_keeps_the_state_and_spends_nothing()
    {
        await WithSession(async (conn, items, stash, send, sendRaw, readPreview) =>
        {
            var target = Put(items, stash, false, LockedWand(), 4);
            var currency = Put(items, stash, false,
                ItemCatalog.Create(Program.Items.Find(CurrencyEffects.OrbOfTransmutation)!) with { StackSize = 2 }, 0);
            var before = Current(items, target.Id).Item;

            await sendRaw(PreviewRequest(3, currency, target));
            var preview = await readPreview();
            Assert.NotEqual((ushort)0, preview.Status);
            Assert.Equal(0ul, preview.Count);

            await send(Request(currency, target));

            Assert.Equal(before, Current(items, target.Id).Item);
            Assert.Equal(2, Current(items, currency.Id).Item.StackSize);
        });
    }

    static Item LockedWand() => new ItemRolls(Program.Items).ApplyLock(FourModWand(), new Random(11))!;

    static readonly ItemCatalog.BaseType Wand = Program.Items.Find("Driftwood Wand")!;

    static ModCatalog.Mod Mod(string id) => Program.Items.Mods.All.Single(m => m.Id == id);

    static ItemMod Explicit(string id) => Mod(id).Roll(ModSection.Explicit, new Random(1));

    static Item Rare(ItemCatalog.BaseType type, params ItemMod[] mods) =>
        ItemCatalog.Create(type) with
        {
            Rarity = ItemRarity.Rare,
            NameWords = [111u, 222u],
            Mods = [.. ItemCatalog.Create(type).Mods.Where(m => m.Section != ModSection.Explicit), .. mods],
        };

    static Item FourModWand() => Rare(Wand,
        Explicit("LocalIncreasedPhysicalDamagePercent1"), Explicit("IncreasedManaWeapon1"),
        Explicit("LocalIncreasedAttackSpeed1"), Explicit("Intelligence1")) with { ItemLevel = 1 };

    static async Task WithSession(Func<ClientState, PlayerInventories, AccountStash, Func<byte[], Task>,
        Func<byte[], Task>, Func<Task<Preview>>, Task> action)
    {
        TestSaves.Ensure();
        string name = "Hinekora" + Guid.NewGuid().ToString("N");
        var conn = new ClientState(new(ushort.MaxValue, "HinekoraTest", 128, 128, 32, 32, null, []), 0, 1)
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
        var clientCiphers = new ConnectionCiphers(new byte[64], isClient: true);
        try
        {
            Task Send(byte[] packet) => Program.OnClientMessages("hinekora-test", writer.GetStream(), ciphers,
                [((ushort)(packet[0] << 8 | packet[1]), packet)], [], conn).WaitAsync(TimeSpan.FromSeconds(5));
            Task SendRaw(byte[] packet) => Program.OnClientMessages("hinekora-test", writer.GetStream(), ciphers,
                [], packet, conn).WaitAsync(TimeSpan.FromSeconds(5));
            async Task<Preview> ReadPreview()
            {
                var header = new byte[14];
                await ReadExactly(reader.GetStream(), header);
                clientCiphers.Receive.Process(header);
                Assert.Equal(0x029f, (ushort)(header[0] << 8 | header[1]));
                ushort key = (ushort)(header[2] << 8 | header[3]);
                ushort status = (ushort)(header[4] | header[5] << 8);
                ulong count = BitConverter.ToUInt64(header, 6);
                if (count == 0) return new(key, status, 0, []);
                var lengthBytes = new byte[2];
                await ReadExactly(reader.GetStream(), lengthBytes);
                clientCiphers.Receive.Process(lengthBytes);
                int length = lengthBytes[0] << 8 | lengthBytes[1];
                var payload = new byte[length];
                await ReadExactly(reader.GetStream(), payload);
                clientCiphers.Receive.Process(payload);
                return new(key, status, count, payload);
            }
            await action(conn, items, stash, Send, SendRaw, ReadPreview);
        }
        finally
        {
            PlayerInventories.Forget(name);
            AccountStash.Forget(name);
        }
    }

    static async Task ReadExactly(NetworkStream stream, byte[] buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read)).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            if (n == 0) throw new EndOfStreamException();
            read += n;
        }
    }

    static InventoryItem Put(PlayerInventories items, AccountStash stash, bool inStash, Item item, byte x) =>
        inStash ? stash.Place(0, x, 0, new InventoryItem(1, 0, 0, item), Shape, out _)!.Value.Placed
            : items.Add(Main, x, 0, item);

    static PlayerInventories.Shape? Shape(Item item) => Program.Items.ByHash(item.TypeHash) is { } t
        ? new(t.Width, t.Height, t.Class, t.MaxStack) : null;

    static InventoryItem Current(PlayerInventories items, uint id) =>
        items.Items(Main).Single(i => i.Id == id);

    static byte[] Request(InventoryItem currency, InventoryItem target) =>
        new LoginWriter().U16(0x71).U32(PlayerInventories.InventoryId(Main))
            .U32(currency.Id).U32(PlayerInventories.InventoryId(Main)).U32(target.Id).ToArray();

    static byte[] PreviewRequest(ushort key, InventoryItem currency, InventoryItem target) =>
        new LoginWriter().U16(0x29c).U16(key).U32(PlayerInventories.InventoryId(Main))
            .U32(currency.Id).U32(PlayerInventories.InventoryId(Main)).U32(target.Id).ToArray();
}
