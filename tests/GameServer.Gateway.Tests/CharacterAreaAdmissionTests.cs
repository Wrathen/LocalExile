using System.Text.Json;
using System.Text.Json.Nodes;
using GameServer.Core.Protocol.Instance;
using GameServer.Storage;

namespace GameServer.Gateway.Tests;

[Collection("Ascendancy Gateway integration")]
public sealed class CharacterAreaAdmissionTests
{
    [Theory]
    [InlineData("future-normal")]
    public void Rejected_area_admission_keeps_the_raw_save_flask_charges_and_pending_offers(string problem)
    {
        using var character = new Character();
        character.Flask["FutureItemField"] = JsonNode.Parse("""{"keep":[1,2]}""");
        if (problem == "future-normal")
            character.Json["MainAscendancy"]!["Selection"]!["Revision"] = "future-client";
        else if (problem == "disconnected-ordinary")
            character.Json["Passives"] = JsonSerializer.SerializeToNode(new ushort[] { 24377 });
        else
            character.Json["MainAscendancy"]!["Selection"]!["Family"] = "Phrecia";
        character.Store();
        var items = character.OfferFlask(); // A previous connection can leave an offer in the shared inventory.
        string before = character.SavedJson;

        Assert.Throws<AscendancyRuleException>(() =>
            Program.PrepareCharacterForArea("admission-test", character.State, 1));

        Assert.Equal(before, character.SavedJson);
        Assert.Equal(1, Assert.Single(items.Items(PlayerInventories.Flask)).Item.Charges);
        Assert.Single(items.Offered());
        Assert.Null(character.SavedStash);
        Assert.Null(character.State.SavedPassiveProgress);
    }

    [Fact]
    public void Unknown_saved_items_are_refused_before_legacy_gold_migration_or_town_refill()
    {
        using var character = new Character();
        Assert.Null(Program.Items.ByHash(uint.MaxValue));
        character.Flask["FutureItemField"] = JsonNode.Parse("""{"keep":[1,2]}""");
        character.Json["Inventories"]![PlayerInventories.MainInventory.ToString()] = JsonSerializer.SerializeToNode(
            new[] { new CharacterSaves.SavedPlacement(2, 0, 0, new(uint.MaxValue)) });
        character.Store();
        string before = character.SavedJson;

        Assert.Throws<UnknownSavedItemException>(() =>
            Program.PrepareCharacterForArea("admission-test", character.State, 1));

        Assert.Equal(before, character.SavedJson);
        Assert.Null(character.SavedStash);
        Assert.Null(character.State.SavedPassiveProgress);
    }

    [Fact]
    public void Accepted_ordinary_town_admission_refills_cancels_offers_and_migrates_gold_once()
    {
        using var character = new Character();
        character.Store();
        var items = character.OfferFlask();

        Assert.Equal(10, Program.PrepareCharacterForArea("admission-test", character.State, 1));

        var saved = CharacterSaves.Load(character.Name)!;
        Assert.Equal(AscendancyFamily.Normal, character.State.MainAscendancy.Selection!.Family);
        Assert.Equal(new ushort[] { 39725 }, character.State.Passives);
        Assert.Equal(21, Assert.Single(items.Items(PlayerInventories.Flask)).Item.Charges);
        Assert.Equal(21, Assert.Single(saved.Inventories[PlayerInventories.Flask]).Item.Charges);
        Assert.Empty(items.Offered());
        Assert.Null(saved.Gold);
        Assert.Equal(123u, AccountStash.For(character.StashKey, Program.Items).Gold);
        Assert.NotNull(character.State.SavedPassiveProgress);

        string after = character.SavedJson;
        Assert.Equal(10, Program.PrepareCharacterForArea("admission-test", character.State, 1));
        Assert.Equal(after, character.SavedJson);
        Assert.Equal(123u, AccountStash.For(character.StashKey, Program.Items).Gold);
    }

    [Fact]
    public void Accepted_Ruthless_town_admission_keeps_flask_charges_while_loading_validated_progress()
    {
        using var character = new Character(ruthless: true);
        character.Store();
        var items = character.OfferFlask();

        Assert.Equal(10, Program.PrepareCharacterForArea("ruthless-admission-test", character.State, 1));

        var saved = CharacterSaves.Load(character.Name)!;
        Assert.Equal(AscendancyFamily.Normal, character.State.MainAscendancy.Selection!.Family);
        Assert.Equal(new ushort[] { 39725 }, character.State.Passives);
        Assert.Equal(1, Assert.Single(items.Items(PlayerInventories.Flask)).Item.Charges);
        Assert.Equal(1, Assert.Single(saved.Inventories[PlayerInventories.Flask]).Item.Charges);
        Assert.Empty(items.Offered());
        Assert.Null(saved.Gold);
        Assert.Equal(123u, AccountStash.For(character.StashKey, Program.ItemsFor(character.State)).Gold);
        Assert.NotNull(character.State.SavedPassiveProgress);
    }

    [Fact]
    public void Rejected_Ruthless_progress_does_not_normalize_inventory_or_pending_offers()
    {
        using var character = new Character(ruthless: true);
        character.Json["MainAscendancy"]!["Selection"]!["Revision"] = "future-client";
        character.Flask["FutureItemField"] = JsonNode.Parse("""{"keep":[1,2]}""");
        character.Store();
        var items = character.OfferFlask();
        string before = character.SavedJson;

        Assert.Throws<AscendancyRuleException>(() =>
            Program.PrepareCharacterForArea("ruthless-admission-test", character.State, 1));

        Assert.Equal(before, character.SavedJson);
        Assert.Equal(1, Assert.Single(items.Items(PlayerInventories.Flask)).Item.Charges);
        Assert.Single(items.Offered());
        Assert.Null(character.SavedStash);
        Assert.Null(character.State.SavedPassiveProgress);
    }

    sealed class Character : IDisposable
    {
        public string Name { get; } = $"Admission{Guid.NewGuid():N}";
        public ClientState State { get; }
        public JsonObject Json { get; }
        public JsonObject Flask => Json["Inventories"]![PlayerInventories.Flask.ToString()]![0]!["Item"]!.AsObject();
        public string StashKey => AccountStash.Key(Name, State.League);
        public string SavedJson => Saves.Get(SaveDatabase.Table.Characters, Name)!;
        public string? SavedStash => Saves.Get(SaveDatabase.Table.Stashes, StashKey);

        public Character(bool ruthless = false)
        {
            TestSaves.Ensure();
            State = new(new(AreaLayouts.LioneyesWatch, "AdmissionTown", 128, 128, 32, 32, null, []), 0, 1)
            { Character = Name, Account = Name, League = ruthless ? "Ruthless" : "Standard", ClassRow = 4,
                LeagueForm = ruthless ? "ruthless" : null };
            var definition = Program.Ascendancies.Find(4, AscendancyFamily.Normal, "Slayer");
            Assert.NotNull(definition);
            var type = Program.ItemsFor(State).Find("Metadata/Items/Flasks/FlaskLife1");
            Assert.NotNull(type);
            var flask = CharacterSaves.ToSaved(ItemCatalog.Create(type) with { Charges = 1 });
            var save = new CharacterSaves.Save(new()
            {
                [PlayerInventories.Flask] = [new(1, 0, 0, flask)],
            }, Experience: CharacterStats.ExperienceFor(10), Passives: [39725], Gold: 123,
                MainAscendancy: new(definition.Identity, Awards: ["normal"]));
            Json = JsonSerializer.SerializeToNode(save, CharacterSaves.Opts)!.AsObject();
        }

        public void Store() => Saves.Put(SaveDatabase.Table.Characters, Name, Json.ToJsonString(CharacterSaves.Opts));

        public PlayerInventories OfferFlask()
        {
            var items = PlayerInventories.For(Name, Program.ItemsFor(State));
            Assert.NotNull(items.Offer(PlayerInventories.Flask, 1, 0, 0, _ => null));
            Assert.Single(items.Offered());
            return items;
        }

        public void Dispose()
        {
            PlayerInventories.Forget(Name);
            AccountStash.Forget(StashKey);
        }
    }
}
