using System.Text.Json;

namespace GameServer.Modules.Delve;

/// <summary>Mining equipment and azurite shared by characters of one account and league.</summary>
public sealed record DelveProgress(uint Azurite = 0, byte Flares = 0, byte Dynamite = 0, IReadOnlyList<byte>? Upgrades = null)
{
    public byte Level(int type) => type >= 0 && type < (Upgrades?.Count ?? 0) ? Upgrades![type] : (byte)0;
}

/// <summary>Equipment prices and limits from the shipped 3.29.3b tables; no guessed upgrade costs.</summary>
public static class DelveEquipment
{
    sealed record Upgrade(int Type, byte Level, uint Cost, int[] Keys, int[] Values);
    static readonly Lazy<Dictionary<(int Type, byte Level), Upgrade>> Rows = new(() =>
    {
        if (!File.Exists("data/tables/DelveUpgrades.json")) return [];
        using var doc = JsonDocument.Parse(File.ReadAllBytes("data/tables/DelveUpgrades.json"));
        return doc.RootElement.EnumerateArray().Select(r => new Upgrade(r.GetProperty("DelveUpgradeTypeKey").GetInt32(),
            r.GetProperty("UpgradeLevel").GetByte(), r.GetProperty("Cost").GetUInt32(),
            [.. r.GetProperty("StatsKeys").EnumerateArray().Select(v => v.GetInt32())],
            [.. r.GetProperty("StatValues").EnumerateArray().Select(v => v.GetInt32())])).ToDictionary(r => (r.Type, r.Level));
    });
    static readonly Lazy<uint[]> FlarePrices = new(() => Prices("DelveFlares", "__col6"));
    static readonly Lazy<uint[]> DynamitePrices = new(() => Prices("DelveDynamite", "__col12"));
    static uint[] Prices(string table, string field)
    {
        if (!File.Exists($"data/tables/{table}.json")) return [];
        using var doc = JsonDocument.Parse(File.ReadAllBytes($"data/tables/{table}.json"));
        return [.. doc.RootElement.EnumerateArray().Select(r => r.GetProperty(field).GetUInt32())];
    }
    public static int Value(DelveProgress progress, int type) =>
        Rows.Value.GetValueOrDefault((type, progress.Level(type)))?.Values.FirstOrDefault() ?? 0;

    public static IEnumerable<(uint Key, int Value)> Stats(DelveProgress progress)
    {
        for (int type = 0; type < 10; type++)
            if (Rows.Value.TryGetValue((type, progress.Level(type)), out var row))
                for (int i = 0; i < Math.Min(row.Keys.Length, row.Values.Length); i++)
                    yield return ((uint)row.Keys[i] + 1, row.Values[i]);
    }

    public static DelveProgress? UpgradeEquipment(DelveProgress progress, int type)
    {
        if (type is < 0 or > 9 or 6 || progress.Level(type) == byte.MaxValue ||
            !Rows.Value.TryGetValue((type, (byte)(progress.Level(type) + 1)), out var next) || next.Cost > progress.Azurite) return null;
        byte[] levels = new byte[10];
        for (int i = 0; i < levels.Length; i++) levels[i] = progress.Level(i);
        levels[type] = next.Level;
        return progress with { Azurite = progress.Azurite - next.Cost, Upgrades = Array.AsReadOnly(levels) };
    }

    public static DelveProgress? Purchase(DelveProgress progress, bool flare, byte count)
    {
        if (count == 0) return null;
        int capacity = Value(progress, flare ? 1 : 2), held = flare ? progress.Flares : progress.Dynamite;
        if (held + count > Math.Min(byte.MaxValue, capacity)) return null;
        var values = Rows.Value.GetValueOrDefault((flare ? 4 : 5, progress.Level(flare ? 4 : 5)))?.Values;
        int variant = values is { Length: > 1 } ? values[1] : 0;
        var prices = flare ? FlarePrices.Value : DynamitePrices.Value;
        if (variant < 0 || variant >= prices.Length) return null;
        ulong cost = (ulong)prices[variant] * count;
        if (cost > progress.Azurite) return null;
        var paid = progress with { Azurite = progress.Azurite - (uint)cost };
        return flare ? paid with { Flares = (byte)(held + count) } : paid with { Dynamite = (byte)(held + count) };
    }
}
