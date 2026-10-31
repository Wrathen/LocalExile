using System.Text.Json;

/// <summary>The copied client's HardMode purchase costs. Unmodelled/multi-currency costs are omitted, never free.</summary>
sealed class HardModeShopPrices
{
    readonly Dictionary<(int Base, int Level), (uint Currency, uint Amount)> _prices = [];
    readonly Dictionary<int, (uint Currency, uint Amount)> _maps = [];

    public HardModeShopPrices(ItemCatalog items, string folder = "data")
    {
        JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "tables", name + ".json")));
        using var costs = Read("ItemCosts");
        using var levels = Read("ItemCostPerLevel");
        using var maps = Read("Maps");
        using var mapCosts = Read("MapPurchaseCosts");
        (uint Currency, uint Amount)? Price(int row)
        {
            if (row < 0 || row >= costs.RootElement.GetArrayLength()) return null;
            var cost = costs.RootElement[row];
            var currencies = cost.GetProperty("Cost1Currencies").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            var amounts = cost.GetProperty("Cost1Values").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            if (currencies.Length != 1 || amounts.Length != 1 || amounts[0] <= 0
                || items.Row(currencies[0]) is not { Supported: true } currency) return null;
            return (currency.Hash, (uint)amounts[0]);
        }
        foreach (var row in levels.RootElement.EnumerateArray())
        {
            int cost = row.GetProperty("CostHardmode") is { ValueKind: JsonValueKind.Number } hard
                ? hard.GetInt32() : row.GetProperty("Cost").GetInt32();
            if (Price(cost) is { } price) _prices.TryAdd((row.GetProperty("BaseItem").GetInt32(), row.GetProperty("Level").GetInt32()), price);
        }
        var byTier = mapCosts.RootElement.EnumerateArray().ToDictionary(r => r.GetProperty("Tier").GetInt32(),
            r => r.GetProperty("CostHardmode").GetInt32());
        foreach (var map in maps.RootElement.EnumerateArray())
            if (byTier.TryGetValue(map.GetProperty("Tier").GetInt32(), out int cost) && Price(cost) is { } price)
                _maps.TryAdd(map.GetProperty("BaseItemTypesKey").GetInt32(), price);
    }

    public (uint Currency, uint Amount)? For(ItemCatalog.BaseType type) => type.Class == "Map"
        ? _maps.GetValueOrDefault(type.Row) is var map && map.Amount > 0 ? map : null
        : _prices.GetValueOrDefault((type.Row, 1)) is var price && price.Amount > 0 ? price : null;
}
