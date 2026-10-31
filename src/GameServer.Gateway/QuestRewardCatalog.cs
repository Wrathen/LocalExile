using System.Text.Json;
using GameServer.Core.Protocol.Instance;

/// <summary>QuestRewardOffers and class-filtered QuestRewards. Row zero is a real offer/flag.</summary>
sealed class QuestRewardCatalog
{
    /// <summary>One choice of an offer (a QuestRewards row). <paramref name="Level"/> is a gem's level and other items' item
    /// level; <see cref="Rarity"/> is the row's rarity column (0 normal, 1 magic; none for gems).</summary>
    public sealed record Choice(uint Id, int ItemRow, int Level, int Stack, IReadOnlyList<int> Classes)
    {
        public int? Rarity { get; init; }
    }
    public sealed record Offer(int Row, string TakenFlag, IReadOnlyList<Choice> Choices);
    readonly Dictionary<int, Offer> offers = [];
    public Offer? Find(int row) => offers.GetValueOrDefault(row);
    public IReadOnlyCollection<Offer> Offers => offers.Values;
    public IReadOnlyList<Choice> Choices(int row, int classRow) =>
        Find(row) is { } offer ? [.. offer.Choices.Where(c => c.Classes.Count == 0 || c.Classes.Contains(classRow))] : [];

    public static QuestRewardCatalog Load(string folder = "data", bool hardMode = false, bool supportGems = true)
    {
        var result = new QuestRewardCatalog();
        string Table(string name) => File.ReadAllText(Path.Combine(folder, "tables", name + ".json"));
        using var flags = JsonDocument.Parse(Table("QuestFlags"));
        var names = flags.RootElement.EnumerateArray().Select(f => f.GetProperty("Id").GetString()!).ToArray();
        using var choices = JsonDocument.Parse(Table("QuestRewards"));
        HashSet<int> supportRows = [];
        if (!supportGems)
        {
            using var gems = JsonDocument.Parse(Table("SkillGems"));
            supportRows = gems.RootElement.EnumerateArray().Where(r => r.GetProperty("IsSupport").GetBoolean())
                .Select(r => r.GetProperty("BaseItemTypesKey").GetInt32()).ToHashSet();
        }
        // Pinned 3.29 QuestRewards column16: 0 shared, 1 regular-only, 2 HardMode-only.
        // This selects the Uncarved Gemstone/gear alternatives rather than normal supports or movement gems.
        var byOffer = choices.RootElement.EnumerateArray().Where(r => (!r.TryGetProperty("__col16", out var mode)
                || mode.GetInt32() == 0 || mode.GetInt32() == (hardMode ? 2 : 1))
                && (supportGems || !supportRows.Contains(r.GetProperty("Reward").GetInt32())))
            .GroupBy(r => r.GetProperty("RewardOffer").GetInt32())
            .ToDictionary(g => g.Key, g => g.Select(r => new Choice((uint)r.GetProperty("__col1").GetInt32() + 1,
                r.GetProperty("Reward").GetInt32(), r.GetProperty("RewardLevel").GetInt32(), r.GetProperty("RewardStack").GetInt32(),
                r.GetProperty("Characters").EnumerateArray().Select(c => c.GetInt32()).ToArray())
            {
                Rarity = r.TryGetProperty("__col5", out var rarity) && rarity.ValueKind == JsonValueKind.Number ? rarity.GetInt32() : null,
            }).ToArray());
        using var offers = JsonDocument.Parse(Table("QuestRewardOffers"));
        int row = 0;
        foreach (var r in offers.RootElement.EnumerateArray())
        {
            int index = row++;
            if (r.GetProperty("QuestFlag") is not { ValueKind: JsonValueKind.Number } flag) continue;
            int f = flag.GetInt32();
            if (f < 0 || f >= names.Length || names[f] == "NEVER_SET") continue;
            result.offers[index] = new(index, names[f], byOffer.GetValueOrDefault(index) ?? []);
        }
        return result;
    }

    // Grant level-one gems/currency and normal flasks at the row's level. A caller supplying the mode's ItemRolls can
    // also construct gear at its actual item level and rarity (HardMode's armour/belt alternatives). Scripted rewards
    // without a model remain unavailable; Program.Npcs omits a choice when this returns null.
    public static Item? Create(Choice choice, ItemCatalog items, ItemRolls? rolls = null)
    {
        if (choice.Stack <= 0 || items.Row(choice.ItemRow) is not { Supported: true } type) return null;
        bool normalFlask = type.Components.Contains("Flask") && choice.Rarity == 0 && choice.Level is >= 1 and <= 100;
        if ((choice.Level != 1 && !normalFlask) || choice.Rarity is > 0)
        {
            if (rolls is null || choice.Level is < 1 or > 100 || choice.Rarity is not (0 or 1 or 2)
                || !type.Components.Contains("Mods")) return null;
            var (sockets, links) = items.DefaultSockets(type);
            return rolls.Roll(ItemCatalog.Create(type, sockets, links) with { ItemLevel = (byte)choice.Level },
                (ItemRarity)choice.Rarity.Value, Random.Shared);
        }
        return ItemCatalog.Create(type) with { StackSize = (ushort)Math.Min(choice.Stack, ushort.MaxValue), ItemLevel = (byte)choice.Level };
    }

    /// <summary>Pack rewards using their actual sizes; HardMode gear choices must not overlap like 1×1 gems.</summary>
    public static IReadOnlyList<InventoryItem> Arrange(IEnumerable<(uint Id, Item Item)> rewards, ItemCatalog catalog)
    {
        var placed = new List<(InventoryItem Placement, byte Width, byte Height)>();
        foreach (var (id, item) in rewards)
        {
            var type = catalog.ByHash(item.TypeHash) ?? throw new InvalidOperationException("Unknown quest reward type");
            (byte X, byte Y)? cell = null;
            for (byte y = 0; y + type.Height <= NpcShops.Tab.Height && cell is null; y++)
                for (byte x = 0; x + type.Width <= NpcShops.Tab.Width; x++)
                    if (!placed.Any(p => p.Placement.X < x + type.Width && x < p.Placement.X + p.Width
                        && p.Placement.Y < y + type.Height && y < p.Placement.Y + p.Height))
                    { cell = (x, y); break; }
            if (cell is not { } at) throw new InvalidOperationException("Quest reward choices exceed the native reward grid");
            placed.Add((new InventoryItem(id, at.X, at.Y, item), type.Width, type.Height));
        }
        return [.. placed.Select(p => p.Placement)];
    }

    public static PlayerInventories.Stacking? Claim(string character, string takenFlag, PlayerInventories inventory,
        Item reward, Func<Item, PlayerInventories.Shape?> shapeOf) => Saves.Atomically(() =>
    {
        // Read the durable flag again inside the transaction: a retry or another connection cannot duplicate a reward.
        if (CharacterSaves.Load(character)?.QuestFlags?.Contains(takenFlag) == true
            || !inventory.Fits(PlayerInventories.MainInventory, reward, shapeOf)) return null;
        var added = inventory.AddStacked(PlayerInventories.MainInventory, reward, shapeOf);
        if (added.Left != 0) throw new InvalidOperationException("A quest reward did not fit after its capacity check");
        CharacterSaves.ChangeQuestFlags(character, [takenFlag], true);
        return added;
    });
}
