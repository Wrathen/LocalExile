using System.Reflection;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

[Collection(TestSaves.FailWriteCollection)]
public sealed class GameModeMergeIntegrationTests : IDisposable
{
    readonly List<string> characters = [];
    static MethodInfo Method(string name) => typeof(Program).GetMethod(name,
        BindingFlags.Static | BindingFlags.NonPublic)!;

    ClientState Connection(bool ruthless = false, InstanceState? instance = null, bool ssf = false, bool hardcore = false)
    {
        TestSaves.Ensure();
        string name = "ModeMerge" + Guid.NewGuid().ToString("N");
        characters.Add(name);
        var conn = new ClientState(new(ushort.MaxValue, "mode-merge", 300, 300, 10, 10, null, []), 1, 1)
        {
            Character = name, ClassRow = SkillCatalog.WitchRow, LeagueForm = ruthless ? "ruthless" : null, Ssf = ssf, Hardcore = hardcore,
            League = ruthless ? "Ruthless" : "Standard", Instance = instance ?? new(),
            PlayerSent = true, PlayerLife = 100, PlayerMana = 100,
        };
        CharacterSaves.SetExperience(name, CharacterStats.ExperienceFor(40));
        conn.Experience = CharacterStats.ExperienceFor(40);
        conn.Pools = Program.PoolsFor(conn, 40);
        conn.PlayerLife = conn.PlayerMaxLife;
        conn.PlayerMana = conn.Pools.Mana;
        return conn;
    }

    [Theory]
    [InlineData(false)]
    public async Task Projectile_kill_commits_mode_rewards_once_while_output_is_held(bool ruthless)
    {
        var conn = Connection(ruthless);
        var catalog = Program.ItemsFor(conn);
        var host = PlayerInventories.For(conn.Character!, catalog).Add(PlayerInventories.Weapon1, 0, 0,
            ItemCatalog.Create(catalog.Find("Driftwood Wand")!) with
            { Sockets = [new(SocketColour.White, ItemCatalog.Create(catalog.Find("Fireball")!))] });
        var variety = Program.Monsters.Find("Metadata/Monsters/ZombieBoss/ZombieBossHillockNormal")!;
        var target = new SpawnedMonster("merge reward target", new(20, 10, 0), 0, 2, 1)
        { Variety = variety, Level = 40, Rarity = MonsterRarity.Rare };
        conn.Monsters.Add(42, target);
        uint xp = Progression.MonsterExperience(Program.Monsters.BaseExperience(variety, 40)!.Value,
            variety.ExperienceMultiplier, target.Rarity, 40, 40);
        uint before = conn.Experience;
        await using var runtime = new ProjectileRuntimeFixture(conn);
        await runtime.HoldOutput();
        // Match the emitted flight's independent reward stream; normal and Ruthless use their own public drop rules.
        runtime.Release(runtime.Shot(offense: new(DamagePacket.Of(DamageType.Physical, 1000, 1000),
            true, int.MaxValue, 0, 150, "mode merge")));
        var flight = Assert.Single(runtime.Session.Runtime.Projectiles);
        var random = new Random(unchecked(flight.Use.Seed * 397 ^ (int)flight.Id) ^ 0x5e418ac3);
        var balance = ruthless ? ServerLeagues.Live("ruthless") : null;
        var expected = Program.DropsFor(conn).Roll(40, target.Rarity, random, 0,
            balance?.ItemDropMultiplier ?? 1, balance?.EquipmentRarityMultiplier ?? 1).ToList();
        if (Program.DropsFor(conn).Gold(40, target.Rarity, random, balance?.GoldMultiplier ?? 1) is { } gold)
            expected.Add(gold);

        runtime.Advance(2);

        Assert.True(target.Dead);
        Assert.Equal(before + xp, conn.Experience);
        double carry = 0;
        int gemXp = ruthless ? LeagueGameplay.GemExperience(xp, balance!.GemExperienceMultiplier!.Value, ref carry)
            : (int)(xp / 10);
        var inventory = PlayerInventories.For(conn.Character!, catalog);
        Assert.Equal(gemXp, Assert.Single(inventory.Items(PlayerInventories.Weapon1), i => i.Id == host.Id)
            .Item.Sockets[0].Gem!.GemExperience);
        var drops = conn.Instance.Ground.All();
        Assert.Equal(expected.Select(d => (d.Item.TypeHash, d.Item.StackSize)),
            drops.Select(d => (d.Item.Item.TypeHash, d.Item.Item.StackSize)));
        Assert.Equal(0, runtime.Available);
        runtime.Advance(3);
        Assert.Equal(before + xp, conn.Experience);
        Assert.Equal(drops.Count, conn.Instance.Ground.All().Count);
        Assert.Equal(gemXp, inventory.Items(PlayerInventories.Weapon1).Single(i => i.Id == host.Id)
            .Item.Sockets[0].Gem!.GemExperience);
    }

    static PlayerInventories.Shape? Shape(Item item) => Program.Items.ByHash(item.TypeHash) is { } type
        ? new(type.Width, type.Height, type.Class, type.MaxStack) : null;

    static PlayerTrade OfferTrade(ClientState a, ClientState b)
    {
        var first = PlayerInventories.For(a.Character!, Program.ItemsFor(a));
        var second = PlayerInventories.For(b.Character!, Program.ItemsFor(b));
        var ring = first.Add(PlayerInventories.MainInventory, 0, 0, ItemCatalog.Create(Program.Items.Find("Iron Ring")!));
        var amulet = second.Add(PlayerInventories.MainInventory, 0, 0, ItemCatalog.Create(Program.Items.Find("Paua Amulet")!));
        first.Offer(PlayerInventories.MainInventory, ring.Id, 0, 0, Shape);
        second.Offer(PlayerInventories.MainInventory, amulet.Id, 0, 0, Shape);
        var trade = new PlayerTrade(a, b);
        a.Trade = b.Trade = trade;
        return trade;
    }

    public void Dispose()
    {
        foreach (string name in characters) PlayerInventories.Forget(name);
    }
}
