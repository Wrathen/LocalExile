using System.Text.Json;
using GameServer.Core.Protocol.Instance;

namespace GameServer.Gateway.Tests;

public class ItemStatesTests
{
    static readonly ItemCatalog.BaseType Boots = Program.Items.Find("Iron Greaves")!;
    static readonly ItemCatalog.BaseType Wand = Program.Items.Find("Driftwood Wand")!;

    static ItemMod Explicit(string id) =>
        Program.Items.Mods.All.Single(m => m.Id == id).Roll(ModSection.Explicit, new Random(1));

    static Item FourModRare() => ItemCatalog.Create(Wand) with
    {
        Rarity = ItemRarity.Rare, NameWords = [111u, 222u], ItemLevel = 1,
        Mods = [.. ItemCatalog.Create(Wand).Mods.Where(m => m.Section != ModSection.Explicit),
            Explicit("LocalIncreasedPhysicalDamagePercent1"), Explicit("IncreasedManaWeapon1"),
            Explicit("LocalIncreasedAttackSpeed1"), Explicit("Intelligence1")],
    };

    [Fact]
    public void Toggle_switches_each_state_and_clear_removes_them_all()
    {
        var item = ItemCatalog.Create(Wand);
        foreach (string state in new[] { "corrupted", "mirrored", "split", "synthesised", "shaper", "warlord" })
            item = ItemStates.Toggle(item, state)!;
        Assert.True(item.Corrupted && item.Mirrored && item.Split && item.Synthesised);
        Assert.Equal(ItemInfluence.Shaper | ItemInfluence.Warlord, item.Influences);
        Assert.Equal("Item states: corrupted, mirrored, split, synthesised, shaper, warlord", ItemStates.Describe(item));

        Assert.Equal(ItemInfluence.Warlord, ItemStates.Toggle(item, "Shaper")!.Influences);
        Assert.Equal("No item states", ItemStates.Describe(ItemStates.Toggle(item, "clear")!));
    }

    [Theory]
    [InlineData("nothing")]
    [InlineData("none")]
    [InlineData("3")]
    public void Toggle_refuses_what_is_not_a_state(string state) =>
        Assert.Null(ItemStates.Toggle(ItemCatalog.Create(Wand), state));

    [Fact]
    public void The_states_survive_a_save_and_reload()
    {
        var item = ItemCatalog.Create(Wand) with
        {
            Influences = ItemInfluence.Elder | ItemInfluence.Redeemer, Split = true, Synthesised = true,
        };
        var back = CharacterSaves.FromSaved(CharacterSaves.ToSaved(item, Program.Items.Mods), Program.Items);
        Assert.Equal(item.Influences, back.Influences);
        Assert.True(back.Split);
        Assert.True(back.Synthesised);

        string json = JsonSerializer.Serialize(CharacterSaves.ToSaved(item, Program.Items.Mods));
        var saved = JsonSerializer.Deserialize<CharacterSaves.SavedItem>(json)!;
        Assert.Equal(item.Influences, saved.Influences);
    }

    [Fact]
    public void An_item_saved_before_the_states_loads_with_none_of_them()
    {
        var old = JsonSerializer.Deserialize<CharacterSaves.SavedItem>($"{{\"Type\":{Wand.Hash}}}")!;
        var item = CharacterSaves.FromSaved(old, Program.Items);
        Assert.Equal(ItemInfluence.None, item.Influences);
        Assert.False(item.Split);
        Assert.False(item.Synthesised);
    }

    [Theory]
    [InlineData(ItemInfluence.Shaper, "boots_shaper")]
    [InlineData(ItemInfluence.Elder, "boots_elder")]
    [InlineData(ItemInfluence.Crusader, "boots_crusader")]
    [InlineData(ItemInfluence.Redeemer, "boots_eyrie")]
    [InlineData(ItemInfluence.Hunter, "boots_basilisk")]
    [InlineData(ItemInfluence.Warlord, "boots_adjudicator")]
    public void An_influence_opens_its_class_tag_and_only_it(ItemInfluence influence, string tag)
    {
        var plain = Program.Rolls.Pool(Boots, 86).Select(p => p.Mod.Id).ToHashSet();
        var opened = Program.Rolls.Pool(Boots, 86, influence).Where(p => !plain.Contains(p.Mod.Id)).ToList();
        Assert.NotEmpty(opened);
        Assert.All(opened, p => Assert.Contains(p.Mod.SpawnWeights, w => w.Tag == tag && w.Weight > 0));
    }

    [Fact]
    public void Rolling_an_influenced_item_keeps_it_and_can_reach_its_mods()
    {
        var plain = Program.Rolls.Pool(Boots, 86).Select(p => p.Mod.Id).ToHashSet();
        var item = ItemCatalog.Create(Boots) with { ItemLevel = 86, Influences = ItemInfluence.Shaper };
        var random = new Random(1);
        bool reached = false;
        for (int i = 0; i < 400 && !reached; i++)
        {
            var rolled = Program.Rolls.Roll(item, ItemRarity.Rare, random);
            Assert.Equal(ItemInfluence.Shaper, rolled.Influences);
            reached = rolled.Mods.Any(m => m.Section == ModSection.Explicit && !plain.Contains(Program.Items.Mods.ByHash(m.Hash)!.Id));
        }
        Assert.True(reached);
    }

    [Theory]
    [InlineData(ItemInfluence.Shaper, false)]
    [InlineData(ItemInfluence.None, true)]
    public void Fracturing_refuses_influenced_and_synthesised_items(ItemInfluence influence, bool synthesised)
    {
        Assert.NotNull(CurrencyEffects.Fracture(FourModRare(), Program.Rolls, new Random(1)));
        Assert.Null(CurrencyEffects.Fracture(FourModRare() with { Influences = influence, Synthesised = synthesised },
            Program.Rolls, new Random(1)));
    }

    [Fact]
    public void A_split_item_can_still_be_fractured()
    {
        Assert.NotNull(CurrencyEffects.Fracture(FourModRare() with { Split = true }, Program.Rolls, new Random(1)));
    }

    [Fact]
    public void The_uniques_that_carry_an_influence_get_it()
    {
        var uniques = Program.Uniques;
        var influenced = uniques.All.Where(d => d.Influences != ItemInfluence.None).ToList();
        Assert.NotEmpty(influenced);
        Assert.All(influenced, d => Assert.Equal(d.Influences, uniques.Create(d, new Random(1))!.Influences));
        Assert.Equal(ItemInfluence.Elder, uniques.Find("Shimmeron")!.Influences);
    }
}
