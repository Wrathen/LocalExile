using GameServer.Core.Protocol.Instance;
using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

// Headhunter's stolen modifiers and damage against rares, and Mageblood's flask buffs following the belt and the bar.
public sealed partial class AscendancyGatewayIntegrationTests
{
    static SpawnedMonster RareWith(string affix, MonsterRarity rarity = MonsterRarity.Rare)
    {
        var mod = Program.Items.Mods.ById(affix)!;
        var set = new MonsterModSet([mod.Roll(ModSection.Explicit, new Random(1))], [], MonsterModEffects.None);
        return new SpawnedMonster("rare", new(10, 10, 30), 0, 3, 1000) { Rarity = rarity, Mods = set };
    }

    [Fact]
    public void Headhunter_steals_a_rares_affixes_for_its_time_and_they_count_on_the_character()
    {
        using var game = LevelPanelCharacter(70);
        var items = PlayerInventories.For(game.Character, Program.Items);
        var conn = game.State;
        // Without the belt a kill steals nothing.
        UniqueBeltsFeature.StealMonsterMods(conn, RareWith("MonsterModAttackCastMovementSpeed"));
        Assert.Empty(conn.StolenMods.Stats());

        items.Add(PlayerInventories.Belt, 0, 0, Program.Uniques.Create("Headhunter", new Random(1))!);
        int before = Program.PoolsFor(conn, 70).MovementVelocityPct;
        // Magic monsters and unique ones give nothing.
        UniqueBeltsFeature.StealMonsterMods(conn, RareWith("MonsterModAttackCastMovementSpeed", MonsterRarity.Magic));
        Assert.Empty(conn.StolenMods.Stats());

        UniqueBeltsFeature.StealMonsterMods(conn, RareWith("MonsterModAttackCastMovementSpeed"));
        Assert.Equal(before + 40, Program.PoolsFor(conn, 70).MovementVelocityPct);
        double now = conn.Clock.Elapsed.TotalSeconds;
        var first = conn.StolenMods.Step(now, dead: false);
        Assert.Equal(1, first!.Value.Stacks);
        Assert.InRange(first.Value.Remaining, 59, 60);
        Assert.Null(conn.StolenMods.Step(now, dead: false));

        // The same modifier adds nothing and resets its time; another is a second stack with its own.
        var mods = Program.Items.Mods;
        var quick = RareWith("MonsterModAttackCastMovementSpeed").Mods;
        conn.StolenMods.Steal(quick, mods, now + 30);
        Assert.Equal(before + 40, Program.PoolsFor(conn, 70).MovementVelocityPct);
        var again = conn.StolenMods.Step(now + 30, dead: false);
        Assert.Equal(1, again!.Value.Stacks);
        Assert.Equal(60, again.Value.Remaining, 3);
        var other = Program.Items.Mods.All.First(m => m.Domain == MonsterModCatalog.Domain && m.GenerationType == MonsterModCatalog.Suffix
            && m.Stats.Count > 0 && m.Stats.All(x => x.Id != "base_movement_velocity_+%"));
        conn.StolenMods.Steal(new([other.Roll(ModSection.Explicit, new Random(1))], [], MonsterModEffects.None), mods, now + 50);
        // The buff tells the stack that lasts longest.
        var two = conn.StolenMods.Step(now + 50, dead: false)!.Value;
        Assert.Equal(2, two.Stacks);
        Assert.Equal(60, two.Remaining, 3);
        var one = conn.StolenMods.Step(now + 95, dead: false)!.Value;
        Assert.Equal(1, one.Stacks);
        Assert.Equal(15, one.Remaining, 3);
        Assert.Equal(before, Program.PoolsFor(conn, 70).MovementVelocityPct);
        Assert.Equal((0, 0.0), conn.StolenMods.Step(now + 111, dead: false)!.Value);
    }

    [Fact]
    public void Ancient_orb_reforges_a_unique_within_its_class_and_rarity()
    {
        var uniques = Program.Uniques;
        var hunter = Program.Uniques.Create("Headhunter", new Random(1))! with { ItemLevel = 77 };
        var seen = new Dictionary<uint, int>();
        for (int i = 0; i < 2000; i++)
        {
            var reforged = uniques.Reforge(hunter, Program.Drops.UniqueWeight, new Random(i))!;
            Assert.Equal("Belt", Program.Items.ByHash(reforged.TypeHash)!.Class);
            Assert.Equal(ItemRarity.Unique, reforged.Rarity);
            Assert.Equal(77, reforged.ItemLevel);
            Assert.NotEqual(hunter.NameWords, reforged.NameWords);
            seen[reforged.NameWords[0]] = seen.GetValueOrDefault(reforged.NameWords[0]) + 1;
        }
        Assert.True(seen.Count > 5);
        Assert.Null(uniques.Reforge(hunter with { Corrupted = true }, Program.Drops.UniqueWeight, new Random(1)));
        Assert.Null(uniques.Reforge(hunter with { Mirrored = true }, Program.Drops.UniqueWeight, new Random(1)));
        Assert.Null(uniques.Reforge(ItemCatalog.Create(Program.Items.Find("Heavy Belt")!), Program.Drops.UniqueWeight, new Random(1)));
        Assert.Null(uniques.Reforge(uniques.Create("Watcher's Eye", new Random(1)) ?? uniques.Create("Sin's Rebirth", new Random(1))!, Program.Drops.UniqueWeight, new Random(1)));
    }

    [Fact]
    public void Headhunter_has_its_ordinary_art_not_the_alternate_one()
    {
        var art = Program.Uniques.Create("Headhunter", new Random(1))!.VisualIdentity;
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes("data/tables/ItemVisualIdentity.json"));
        Assert.Equal("Art/2DItems/Belts/Headhunter.dds", doc.RootElement.EnumerateArray()
            .First(r => r.GetProperty("HASH16").GetInt32() == art).GetProperty("DDSFile").GetString());
    }

    [Fact]
    public void Headhunter_raises_damage_against_rares_only()
    {
        using var game = LevelPanelCharacter(70);
        PlayerInventories.For(game.Character, Program.Items).Add(PlayerInventories.Belt, 0, 0, Program.Uniques.Create("Headhunter", new Random(1))!);
        var player = Program.CharacterPlayerStats(game.State);
        var hit = DamagePacket.Of(DamageType.Physical, 100, 100);
        int against = player[Program.Stats.Key("damage_+%_vs_rare_monsters")!.Value];
        Assert.InRange(against, 20, 40);
        Assert.Equal(100 + against, UniqueBeltsFeature.WithDamageVsRarity(player, RareWith("MonsterModAttackCastMovementSpeed"), hit)[DamageType.Physical].Min, 3);
        Assert.Equal(100, UniqueBeltsFeature.WithDamageVsRarity(player, RareWith("MonsterModAttackCastMovementSpeed", MonsterRarity.Magic), hit)[DamageType.Physical].Min, 3);
    }

    [Fact]
    public void Mageblood_buffs_follow_the_belt_and_the_flask_bar()
    {
        using var game = LevelPanelCharacter(70);
        var items = PlayerInventories.For(game.Character, Program.Items);
        var conn = game.State;
        var flask = ItemCatalog.Create(Program.Items.Find("Granite Flask")!) with { Rarity = ItemRarity.Magic };
        items.Add(PlayerInventories.Flask, 0, 0, flask);
        Assert.Empty(Program.StepMagebloodBuffs(conn));

        items.Add(PlayerInventories.Belt, 0, 0, Program.Uniques.Create("Mageblood", new Random(1))!);
        Assert.Single(Program.StepMagebloodBuffs(conn));
        Assert.Empty(Program.StepMagebloodBuffs(conn));

        // A normal flask is not one of Mageblood's; off the belt, the buff ends.
        items.Add(PlayerInventories.Flask, 1, 0, ItemCatalog.Create(Program.Items.Find("Jade Flask")!));
        Assert.Empty(Program.StepMagebloodBuffs(conn));
        items.Remove(PlayerInventories.Belt, items.Items(PlayerInventories.Belt)[0].Id);
        Assert.Single(Program.StepMagebloodBuffs(conn));
        Assert.Empty(conn.MagebloodBuffs);
    }
}
