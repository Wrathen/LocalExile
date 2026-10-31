using System.Text.Json;
using GameServer.Core.Protocol.Instance;

/// <summary>
/// Hideouts (Agents/docs/protocol/21-hideouts.md): every Hideouts.dat row with its area and default decoration, from
/// data/hideouts.json (scripts/re/atlas hideouts). A placed doodad's hash is an MtxTypeGameSpecific HASH32; the
/// HideoutDoodads row of that MtxTypeGameSpecific row gives its object type (InheritsFrom) and models
/// (Variation_AOFiles, picked by the placement's variation). Every character has the <see cref="Default"/> hideout.
/// </summary>
static class HideoutCatalog
{
    /// <summary>The first campaign hideout (Hideouts row 0, found in The Dread Thicket): every character's.</summary>
    public const string DefaultArea = "HideoutForest";
    public const string CraftingBenchType = "Metadata/Terrain/Missions/Hideouts/Objects/HideoutCraftingBench";

    /// <summary>The standard bench, to the screen-left of the guild stash in the isometric grid.</summary>
    public static Doodad? BenchBesideGuild(Hideout hideout, int guildX, int guildY, Func<int, int, bool>? walkable)
    {
        if (hideout.First(CraftingBenchType) is not { } bench) return null;
        foreach (var (dx, dy) in new[] { (-12, 12), (-16, 16), (-12, 8), (-8, 12) })
        {
            int x = guildX + dx, y = guildY + dy;
            bool clear = x >= 2 && y >= 2;
            for (int ox = -2; clear && ox <= 2; ox++)
                for (int oy = -2; clear && oy <= 2; oy++) clear = walkable?.Invoke(x + ox, y + oy) ?? true;
            if (clear) return bench with { X = x, Y = y };
        }
        return null;
    }

    /// <summary>A doodad of a hideout's decoration, in area grid cells; Rotation in radians (the file's r / 65536 turns).</summary>
    public sealed record Doodad(string Name, uint Hash, string? Type, string? Model, int X, int Y, float Rotation, int Variation)
    {
        public double CollisionRadius { get; init; }
    }

    /// <summary>One hideout: Hash16 is its Hideouts.dat key (the Player component's), AreaHash16 its WorldAreas row's.</summary>
    public sealed record Hideout(int Row, ushort Hash16, string AreaId, ushort AreaHash16, string Name, IReadOnlyList<Doodad> Doodads)
    {
        public Doodad? First(string type) => Doodads.FirstOrDefault(d => d.Type == type);
    }

    static readonly Lazy<IReadOnlyList<Hideout>> All = new(() =>
    {
        var list = Load("data/hideouts.json", "data/tables");
        Console.WriteLine(list.Count == 0
            ? "[gateway] hideouts: no data/hideouts.json (run scripts/dev/extract-data.ps1), hideouts are off"
            : $"[gateway] hideouts: {list.Count}, default {DefaultOf(list)?.Name ?? "none"}");
        return list;
    });

    public static IReadOnlyList<Hideout> Hideouts => All.Value;

    // Local server: every exported personal template is available, including UnlockMtx templates.
    public static byte[] OwnedRows => SelectableRows(Hideouts);

    public static byte[] SelectableRows(IEnumerable<Hideout> hideouts) =>
        [.. hideouts.Select(h => h.Row).Where(row => row is >= 0 and <= 255).Distinct().Order().Select(row => (byte)row)];

    /// <summary>The hideout whose area this is, or null.</summary>
    public static Hideout? ByArea(ushort areaHash16) => Hideouts.FirstOrDefault(h => h.AreaHash16 == areaHash16);

    public static Hideout? Default => DefaultOf(Hideouts);

    public static Hideout? Selected(string? area) => Hideouts.FirstOrDefault(h => h.AreaId == area) ?? Default;

    static Hideout? DefaultOf(IReadOnlyList<Hideout> list) => list.FirstOrDefault(h => h.AreaId == DefaultArea) ?? list.FirstOrDefault();

    /// <summary>The hideout's waypoint as the waypoint object (its variation's model), or null.</summary>
    public static WaypointObject? Waypoint(Hideout h) =>
        h.First(WaypointObject.MetadataPath) is { } d
            ? new WaypointObject(d.X, d.Y, d.Model, d.Rotation, Hideout: new HideoutDoodad(d.Hash, (byte)d.Variation)) : null;

    /// <summary>The doodads the server creates in a hideout besides the waypoint and stash: the map device (21-hideouts.md
    /// §2). Decorations are not created yet.</summary>
    public static bool IsPlaced(Doodad d) => d.Type?.EndsWith("MappingDevice", StringComparison.Ordinal) == true;

    // Our own cells for some doodads, replacing the decoration's: (hideout area, doodad name) → cell. The Lush Hideout's
    // map device stands far out at (449, 460) in its decoration; the user picked (257, 283), near the waypoint (2026-10-20).
    static readonly Dictionary<(string Area, string Name), (int X, int Y)> Moved = new()
    {
        [(DefaultArea, "Map Device")] = (257, 283),
    };

    /// <summary>An object type's deserialized components and shared-state count (data/object-types.json), or null.</summary>
    public static (IReadOnlyList<string> Components, IReadOnlyList<string> SharedStates)? TypeOf(string type) =>
        ObjectTypes.Value.TryGetValue(type, out var t) ? t : null;

    static readonly Lazy<Dictionary<string, (IReadOnlyList<string>, IReadOnlyList<string>)>> ObjectTypes = new(() =>
    {
        var result = new Dictionary<string, (IReadOnlyList<string>, IReadOnlyList<string>)>(StringComparer.Ordinal);
        const string file = "data/object-types.json";
        if (!File.Exists(file)) return result;
        using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
        foreach (var t in doc.RootElement.GetProperty("types").EnumerateArray())
        {
            string type = t.GetProperty("type").GetString()!;
            if (!type.Contains("Hideout", StringComparison.OrdinalIgnoreCase)) continue;
            result[type] = ([.. t.GetProperty("components").EnumerateArray().Take(t.GetProperty("read").GetInt32()).Select(c => c.GetString()!)],
                [.. t.GetProperty("shared_states").EnumerateArray().Select(s => s[0].GetString()!)]);
        }
        return result;
    });

    public static List<Hideout> Load(string hideoutsJson, string tables)
    {
        if (!File.Exists(hideoutsJson)) return [];
        var doodadTypes = DoodadTypes(tables);
        using var doc = JsonDocument.Parse(File.ReadAllBytes(hideoutsJson));
        var result = new List<Hideout>();
        foreach (var h in doc.RootElement.GetProperty("hideouts").EnumerateArray())
        {
            var doodads = new List<Doodad>();
            foreach (var d in h.GetProperty("doodads").EnumerateArray())
            {
                uint hash = d.GetProperty("hash").GetUInt32();
                int fv = d.GetProperty("fv").GetInt32();
                var (type, models) = doodadTypes.GetValueOrDefault(hash);
                string? model = models is { Length: > 0 } ? models[Math.Clamp(fv, 0, models.Length - 1)] : null;
                doodads.Add(new Doodad(d.GetProperty("name").GetString() ?? "", hash, type, model, d.GetProperty("x").GetInt32(), d.GetProperty("y").GetInt32(),
                    d.GetProperty("r").GetInt32() / 65536f * 2 * MathF.PI, fv)
                {
                    CollisionRadius = model is not null && doc.RootElement.TryGetProperty("collisionRadii", out var radii)
                        && radii.TryGetProperty(model, out var radius) ? radius.GetDouble() : 0
                });
            }
            string area = h.GetProperty("area").GetString() ?? "";
            for (int i = 0; i < doodads.Count; i++)
                if (Moved.TryGetValue((area, doodads[i].Name), out var cell)) doodads[i] = doodads[i] with { X = cell.X, Y = cell.Y };
            result.Add(new Hideout(h.GetProperty("row").GetInt32(), (ushort)h.GetProperty("hash16").GetInt32(), h.GetProperty("area").GetString() ?? "",
                (ushort)h.GetProperty("areaHash16").GetInt32(), h.GetProperty("name").GetString() ?? "", doodads));
        }
        return result;
    }

    // MtxTypeGameSpecific HASH32 → (HideoutDoodads InheritsFrom, Variation_AOFiles).
    static Dictionary<uint, (string? Type, string[]? Models)> DoodadTypes(string tables)
    {
        var result = new Dictionary<uint, (string?, string[]?)>();
        string mtxFile = Path.Combine(tables, "MtxTypeGameSpecific.json"), doodadFile = Path.Combine(tables, "HideoutDoodads.json");
        if (!File.Exists(mtxFile) || !File.Exists(doodadFile)) return result;
        using var mtx = JsonDocument.Parse(File.ReadAllBytes(mtxFile));
        var hashOfRow = mtx.RootElement.EnumerateArray().Select(r => r.GetProperty("HASH32").GetUInt32()).ToList();
        using var doodads = JsonDocument.Parse(File.ReadAllBytes(doodadFile));
        foreach (var r in doodads.RootElement.EnumerateArray())
        {
            if (r.GetProperty("MtxTypeGameSpecific") is not { ValueKind: JsonValueKind.Number } key || key.GetInt32() is not (>= 0 and var row) || row >= hashOfRow.Count) continue;
            result.TryAdd(hashOfRow[row], (r.GetProperty("InheritsFrom").GetString(),
                [.. r.GetProperty("Variation_AOFiles").EnumerateArray().Select(a => a.GetString() ?? "")]));
        }
        return result;
    }
}
