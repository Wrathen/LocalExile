using GameServer.Core.Protocol.Instance;

namespace GameServer.Gateway.Tests;

public class FlaskEnchantTests
{
    static readonly ItemCatalog.BaseType Utility = Program.Items.Find("Diamond Flask")!;
    static readonly ItemCatalog.BaseType Wand = Program.Items.Find("Driftwood Wand")!;

    static readonly string[] EnkindlingIds =
    [
        "FlaskEnchantmentSealIncreasedEffect_",
        "FlaskEnchantmentSealIncreasedDuration",
        "FlaskEnchantmentSealIncreasedChargesGained",
        "FlaskEnchantmentSealReducedChargesUsed",
        "FlaskEnchantmentSealMaximumCharges____",
    ];

    static Item Flask(ItemCatalog.BaseType type, ItemRarity rarity = ItemRarity.Normal) =>
        ItemCatalog.Create(type) with { ItemLevel = 84, Rarity = rarity };

    static Item? Enkindle(Item item, int seed = 1) => CurrencyEffects.Enkindling(item, Program.Rolls, new Random(seed));
    static Item? Instill(Item item, int seed = 1) => CurrencyEffects.Instilling(item, Program.Rolls, new Random(seed));

    static ItemMod SingleEnchant(Item item) => Assert.Single(item.Mods, m => m.Section == ModSection.Enchant);
    static ItemMod[] Enchants(Item item) => [.. item.Mods.Where(m => m.Section == ModSection.Enchant)];

    [Fact]
    public void Enkindling_pool_is_the_five_official_mods_with_the_game_weights()
    {
        var pool = Program.Rolls.EnkindlingPool(Utility);
        Assert.Equal(5, pool.Count);
        Assert.Equal(EnkindlingIds.Order(), pool.Select(p => p.Mod.Id).Order());
        Assert.Equal(250, pool.Single(p => p.Mod.Id == "FlaskEnchantmentSealIncreasedEffect_").Weight);
        Assert.All(pool.Where(p => p.Mod.Id != "FlaskEnchantmentSealIncreasedEffect_"), p => Assert.Equal(100, p.Weight));
        Assert.All(pool, p => Assert.Equal(21, p.Mod.GenerationType));
    }

    [Fact]
    public void Instilling_pool_is_the_fifteen_spawnable_triggers()
    {
        var pool = Program.Rolls.InstillingPool(Utility);
        Assert.Equal(15, pool.Count);
        Assert.DoesNotContain(pool, p => p.Mod.Id == "FlaskEnchantmentInjectorOnGuardSkillExpired");
        Assert.All(pool, p => Assert.Equal(100, p.Weight));
        Assert.All(pool, p => Assert.Equal(22, p.Mod.GenerationType));
    }

    [Fact]
    public void Enkindling_adds_one_official_enchant_with_its_no_charge_downside()
    {
        var seen = new HashSet<string>();
        for (int seed = 0; seed < 500; seed++)
        {
            var after = Enkindle(Flask(Utility), seed)!;
            var enchant = SingleEnchant(after);
            var mod = Program.Items.Mods.ByHash(enchant.Hash)!;
            Assert.Contains(mod.Id, EnkindlingIds);
            Assert.Equal(2, mod.Stats.Count);
            Assert.Equal("local_flask_cannot_gain_charges_during_flask_effect", mod.Stats[0].Id);
            Assert.Equal(1, enchant.Values[0]);
            Assert.InRange(enchant.Values[1], Math.Min(mod.Stats[1].Min, mod.Stats[1].Max),
                Math.Max(mod.Stats[1].Min, mod.Stats[1].Max));
            seen.Add(mod.Id);
        }
        Assert.Equal(5, seen.Count);
    }

    [Fact]
    public void Instilling_adds_one_of_the_fifteen_triggers_with_value_one()
    {
        var seen = new HashSet<string>();
        for (int seed = 0; seed < 1000; seed++)
        {
            var after = Instill(Flask(Utility), seed)!;
            var enchant = SingleEnchant(after);
            var mod = Program.Items.Mods.ByHash(enchant.Hash)!;
            Assert.Equal(22, mod.GenerationType);
            Assert.Single(mod.Stats);
            Assert.Equal(1, enchant.Values[0]);
            Assert.NotEqual("FlaskEnchantmentInjectorOnGuardSkillExpired", mod.Id);
            seen.Add(mod.Id);
        }
        Assert.Equal(15, seen.Count);
    }

    [Theory]
    [InlineData(ItemRarity.Normal)]
    [InlineData(ItemRarity.Magic)]
    [InlineData(ItemRarity.Unique)]
    public void Any_rarity_of_utility_flask_can_be_enchanted(ItemRarity rarity)
    {
        var after = Enkindle(Flask(Utility, rarity))!;
        Assert.Equal(rarity, after.Rarity);
        SingleEnchant(after);
    }

    [Fact]
    public void Enkindling_and_Instilling_coexist_and_each_replaces_only_its_own_type()
    {
        var before = Program.Rolls.Roll(Flask(Utility, ItemRarity.Magic), ItemRarity.Magic, new Random(7)) with
        {
            Quality = 17,
            Charges = 23,
            NameWords = [111u, 222u],
        };
        var instilled = Instill(before, 3)!;
        Assert.Equal(22, Program.Items.Mods.ByHash(SingleEnchant(instilled).Hash)!.GenerationType);

        var after = Enkindle(instilled, 4)!;
        Assert.Equal(2, Enchants(after).Length);
        Assert.Single(Enchants(after), m => Program.Items.Mods.ByHash(m.Hash)!.GenerationType == 21);
        Assert.Single(Enchants(after), m => Program.Items.Mods.ByHash(m.Hash)!.GenerationType == 22);
        Assert.Equal(before.Rarity, after.Rarity);
        Assert.Equal(before.NameWords, after.NameWords);
        Assert.Equal(before.Quality, after.Quality);
        Assert.Equal(before.Charges, after.Charges);
        Assert.Equal(before.Mods.Where(m => m.Section != ModSection.Enchant), after.Mods.Where(m => m.Section != ModSection.Enchant));

        // A second Instilling replaces only the Instilling; the Enkindling stays.
        var again = Instill(after, 5)!;
        Assert.Equal(2, Enchants(again).Length);
        Assert.Single(Enchants(again), m => Program.Items.Mods.ByHash(m.Hash)!.GenerationType == 21);
        Assert.Single(Enchants(again), m => Program.Items.Mods.ByHash(m.Hash)!.GenerationType == 22);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Harvest_flask_enchantments_are_replaced_but_other_implicits_stay(bool enkindling)
    {
        var harvest = Program.Items.Mods.ById("HarvestFlaskEnchantmentDurationLoweredOnUse1_")!;
        var other = Program.Items.Mods.ById("SpellDamageOnWeaponImplicitWand1")!;
        var before = Flask(Utility) with
        {
            Mods =
            [
                harvest.Roll(ModSection.Implicit, new Random(1)),
                other.Roll(ModSection.Implicit, new Random(2)),
            ],
        };
        var after = enkindling ? Enkindle(before, 3)! : Instill(before, 3)!;
        Assert.DoesNotContain(after.Mods, m => m.Hash == harvest.Hash);
        Assert.Contains(after.Mods, m => m.Hash == other.Hash);
        Assert.Single(after.Mods, m => m.Section == ModSection.Enchant &&
            Program.Items.Mods.ByHash(m.Hash)!.GenerationType == (enkindling ? 21 : 22));
    }

    [Theory]
    [InlineData("Small Life Flask")]
    [InlineData("Small Mana Flask")]
    public void Life_and_mana_flasks_are_refused(string name)
    {
        var type = Program.Items.Find(name)!;
        Assert.Null(Enkindle(Flask(type)));
        Assert.Null(Instill(Flask(type)));
    }

    [Fact]
    public void Non_flasks_are_refused()
    {
        Assert.Null(Enkindle(ItemCatalog.Create(Wand)));
        Assert.Null(Instill(ItemCatalog.Create(Wand)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Corrupted_and_mirrored_utility_flasks_are_refused(bool corrupted, bool mirrored)
    {
        var item = Flask(Utility) with { Corrupted = corrupted, Mirrored = mirrored };
        Assert.Null(Enkindle(item));
        Assert.Null(Instill(item));
    }

    [Fact]
    public void Unknown_bases_are_refused()
    {
        var item = Flask(Utility) with { TypeHash = 0xdeadbeef };
        Assert.Null(Enkindle(item));
        Assert.Null(Instill(item));
    }
}
