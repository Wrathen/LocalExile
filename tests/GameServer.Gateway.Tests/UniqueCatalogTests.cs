using GameServer.Core.Protocol.Instance;

namespace GameServer.Gateway.Tests;

// Unique items whose client name/art come from Words/UniqueStashLayout and whose mods are curated (UniqueCatalog).
public class UniqueCatalogTests
{
    static string Data()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "data/tables/Words.json"))) return Path.Combine(dir.FullName, "data");
        throw new DirectoryNotFoundException("Words.json");
    }

    static readonly Lazy<(ItemCatalog Items, UniqueCatalog Uniques)> Setup = new(() =>
    {
        string data = Data();
        var items = new ItemCatalog(data);
        return (items, new UniqueCatalog(items, data));
    });

    [Fact]
    public void Mageblood_is_a_unique_heavy_belt_with_its_name_art_and_mods()
    {
        var (items, uniques) = Setup.Value;
        var item = uniques.Create("mageblood", new Random(1))!;

        Assert.Equal(ItemRarity.Unique, item.Rarity);
        Assert.True(item.Identified);
        Assert.Equal(items.Find("Metadata/Items/Belts/Belt4")!.Hash, item.TypeHash);
        Assert.Contains(3562871479u, item.NameWords);   // the Words HASH32 of "Mageblood"
        Assert.Equal(48209, item.VisualIdentity);       // UniqueBelt43 HASH16

        var mods = item.Mods.Select(m => (Mod: items.Mods.ByHash(m.Hash), Value: m)).Where(p => p.Mod is not null).ToList();
        Assert.Contains(mods, p => p.Mod!.Id == "StrengthImplicitBelt1" && p.Value.Section == ModSection.Implicit);
        foreach (string id in new[] { "DexterityUnique__3", "FireResistUnique__24", "ColdResistUnique__34",
                     "MagicUtilityFlasksCannotUseUnique__1____", "MagicUtilityFlasksCannotRemoveUnique__1" })
            Assert.Contains(mods, p => p.Mod!.Id == id && p.Value.Section == ModSection.Explicit);
        var always = item.Mods.Single(m => items.Mods.ByHash(m.Hash)?.Id == "MagicUtilityFlasksAlwaysApplyUnique__1");
        Assert.Equal(ModSection.Explicit, always.Section);
        Assert.InRange(always.Values[0], 2, 4);
    }

    [Fact]
    public void Every_definition_creates_an_item_that_survives_the_wire_and_a_save()
    {
        var (items, uniques) = Setup.Value;
        Assert.NotEmpty(uniques.All);
        // The server cannot write the Tincture component yet, so no tincture exists; nothing else may be left out.
        var writable = uniques.All.Where(d => !items.Find(d.BaseTypeId)!.Components.Contains("Tincture")).ToList();
        Assert.Empty(writable.Where(d => uniques.Create(d, new Random(1)) is null).Select(d => $"{d.Name} on {d.BaseTypeId}"));
        foreach (var definition in writable)
        {
            var item = uniques.Create(definition, new Random(1))!;
            Assert.Equal(items.Find(definition.BaseTypeId)!.Hash, item.TypeHash);
            Assert.Equal(ItemRarity.Unique, item.Rarity);
            Assert.NotEmpty(item.NameWords);
            Assert.Equal(definition.Corrupted, item.Corrupted);
            Assert.Equal(definition.Mirrored, item.Mirrored);
            Assert.InRange(item.ItemLevel, 1, 100);
            foreach (string id in definition.Mods)
                Assert.True(items.Mods.ById(id) is not null, $"{definition.Name}: {id}");

            Assert.NotEmpty(item.Payload());
            var back = CharacterSaves.FromSaved(CharacterSaves.ToSaved(item, items.Mods), items);
            Assert.Equal(item.NameWords, back.NameWords);
            Assert.Equal(item.VisualIdentity, back.VisualIdentity);
            Assert.Equal(item.Mods.Select(m => (m.Section, m.Hash)), back.Mods.Select(m => (m.Section, m.Hash)));
        }
    }

    [Fact]
    public void A_uniques_own_implicits_replace_the_base_types_and_go_in_the_implicit_list()
    {
        var (items, uniques) = Setup.Value;
        var definition = uniques.All.First(d => d.Implicits is { Length: > 0 } own
            && own.All(id => items.Find(d.BaseTypeId)!.Implicits?.All(mod => mod.Id != id) != false));
        var item = uniques.Create(definition, new Random(1))!;

        var implicits = item.Mods.Where(m => m.Section == ModSection.Implicit).Select(m => items.Mods.ByHash(m.Hash)!.Id);
        Assert.Equal(definition.Implicits!, implicits);
        Assert.DoesNotContain(item.Mods, m => m.Section == ModSection.Explicit && definition.Implicits!.Contains(items.Mods.ByHash(m.Hash)!.Id));
    }

    [Fact]
    public void Current_variants_take_their_current_base_and_a_real_item_level()
    {
        var (items, uniques) = Setup.Value;
        Assert.Equal("Opal Wand", items.Find(uniques.Find("Tulfall")!.BaseTypeId)!.Name);
        Assert.All(uniques.All, definition => Assert.InRange(definition.ItemLevel, 1, 100));
    }

    [Theory]
    [InlineData("Emperor's Vigilance")]   // a curly apostrophe in Words
    [InlineData("The Broken Elegy")]      // Broken Elegy in Words
    [InlineData("Gorgon's Gaze")]         // metadata before the base in the export
    public void Names_the_export_spells_differently_from_the_client_resolve(string name)
    {
        var (_, uniques) = Setup.Value;
        Assert.NotNull(uniques.Create(name, new Random(1)));
    }

    [Fact]
    public void A_roll_override_wins_over_the_mods_dat_range()
    {
        var (items, uniques) = Setup.Value;
        var mod = items.Mods.ById("IncreasedLifeUniqueBodyStr2")!;
        var definition = uniques.All.First(d => d.Name == "Wall of Brambles") with
        {
            Mods = ["IncreasedLifeUniqueBodyStr2"], Implicits = null, Ranges = ["IncreasedLifeUniqueBodyStr2:0:12:20"],
        };
        var rolled = uniques.Create(definition, new Random(1))!.Mods.Single(m => m.Hash == mod.Hash);
        Assert.InRange(rolled.Values[0], 12, 20);
    }

    [Fact]
    public void A_unique_on_several_bases_is_reached_by_adding_the_base_name()
    {
        var (items, uniques) = Setup.Value;
        var spectrums = uniques.All.Where(d => d.Name == "Grand Spectrum").ToList();
        Assert.True(spectrums.Count > 1);
        foreach (var definition in spectrums)
            Assert.Equal(definition, uniques.Find($"Grand Spectrum {items.Find(definition.BaseTypeId)!.Name}"));
    }

}
