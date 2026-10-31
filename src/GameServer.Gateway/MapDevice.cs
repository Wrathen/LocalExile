using System.Collections.Concurrent;
using System.Text.Json;
using GameServer.Core.Protocol.Instance;

/// <summary>
/// The hideout's map device (Agents/docs/protocol/21-hideouts.md §5): what its slots (Inventories row 13 <c>Map1</c>)
/// may hold when Activate (C→S 0x00d8) is pressed, which area a map opens (Maps.dat <c>Regular_WorldAreasKey</c>), and
/// each character's open map: its area, its instance once someone entered it, and the portals left.
/// </summary>
sealed class MapDevice
{
    /// <summary>Portals one activation opens; each takes one player into the map once.</summary>
    public const int PortalCount = 6;

    /// <summary>ItemClasses Ids the slots take.</summary>
    public const string MapClass = "Map", MapKeyClass = "MapKey", FragmentClass = "MapFragment";

    /// <summary>The area a map item opens: WorldAreas HASH16, Id and name, the map's tier and the level it plays at.</summary>
    public sealed record MapArea(ushort Hash16, string Id, string Name, int Tier)
    {
        /// <summary>The area level: the WorldAreas level for old area-named maps, or the map tier's level for generic
        /// maps. 0 leaves the client's own WorldAreas level in place.</summary>
        public int Level { get; init; }
    }

    private readonly Dictionary<uint, MapArea> _byItemType;
    private readonly Dictionary<uint, MapArea> _byFragment;
    private readonly AtlasCatalog? _atlas;

    public MapDevice(IReadOnlyDictionary<uint, MapArea> byItemType, AtlasCatalog? atlas = null,
        IReadOnlyDictionary<uint, MapArea>? byFragment = null)
    {
        _byItemType = new(byItemType);
        _byFragment = byFragment is null ? [] : new(byFragment);
        _atlas = atlas;
    }

    /// <summary>Maps.dat: each map's BaseItemTypes row, by its HASH32 (<paramref name="hashOfRow"/>), to its regular area.
    /// Empty when the tables are missing.</summary>
    public static MapDevice Load(string folder, Func<int, uint?> hashOfRow, ItemCatalog? catalog = null)
    {
        var byType = new Dictionary<uint, MapArea>();
        var byFragment = new Dictionary<uint, MapArea>();
        string maps = Path.Combine(folder, "tables", "Maps.json"), areas = Path.Combine(folder, "tables", "WorldAreas.json");
        if (File.Exists(maps) && File.Exists(areas))
        {
            using var m = JsonDocument.Parse(File.ReadAllBytes(maps));
            using var a = JsonDocument.Parse(File.ReadAllBytes(areas));
            int areaCount = a.RootElement.GetArrayLength();
            foreach (var row in m.RootElement.EnumerateArray())
            {
                if (row.GetProperty("BaseItemTypesKey") is not { ValueKind: JsonValueKind.Number } item
                    || row.GetProperty("Regular_WorldAreasKey") is not { ValueKind: JsonValueKind.Number } area
                    || area.GetInt32() >= areaCount || hashOfRow(item.GetInt32()) is not uint hash) continue;
                var wa = a.RootElement[area.GetInt32()];
                byType.TryAdd(hash, new MapArea((ushort)wa.GetProperty("HASH16").GetInt32(), wa.GetProperty("Id").GetString() ?? "",
                    wa.GetProperty("Name").GetString() ?? "", row.GetProperty("Tier").GetInt32())
                {
                    Level = wa.GetProperty("AreaLevel").GetInt32(),
                });
            }
            // A map fragment may open an area by itself: An Audience With The King opens the Crux of Nothingness.
            // The fragment's BaseItemTypes row is the map-device slot's type hash, not a Maps.dat row.
            if (catalog?.Find("Metadata/Items/MapFragments/RitualBossFragment") is { } fragment)
                for (int i = 0; i < areaCount; i++)
                    if (a.RootElement[i].GetProperty("Id").GetString() == "AzmeriLeagueBoss")
                    {
                        var wa = a.RootElement[i];
                        byFragment[fragment.Hash] = new MapArea((ushort)wa.GetProperty("HASH16").GetInt32(),
                            wa.GetProperty("Id").GetString() ?? "", wa.GetProperty("Name").GetString() ?? "", 0)
                        {
                            Level = wa.GetProperty("AreaLevel").GetInt32(),
                        };
                        break;
                    }
        }
        return new MapDevice(byType, catalog is null ? null : new AtlasCatalog(catalog, folder), byFragment);
    }

    public AtlasCatalog? Atlas => _atlas;

    public int Count => _byItemType.Count;

    public MapArea? AreaOf(uint itemType) => _byItemType.GetValueOrDefault(itemType);

    /// <summary>The area a fragment opens by itself, or null (An Audience With The King opens Crux of Nothingness).</summary>
    public MapArea? AreaOfFragment(uint itemType) => _byFragment.GetValueOrDefault(itemType);

    /// <summary>Whether a map opens this area (WorldAreas HASH16): it gets a portal back to the hideout.</summary>
    public bool IsMapArea(ushort hash16) => _byItemType.Values.Any(a => a.Hash16 == hash16) ||
        _byFragment.Values.Any(a => a.Hash16 == hash16) || _atlas?.Layouts.Any(l => l.Hash16 == hash16) == true;

    /// <summary>One item in the slots: its type hash and ItemClasses Id.</summary>
    public readonly record struct Slot(uint TypeHash, string Class, string Name);

    /// <summary>
    /// What Activate does with these slots: only maps and map fragments may be in them, and exactly one map (fragments
    /// alone open nothing yet). Returns the map's area, or null with the reason the player is told.
    /// </summary>
    public MapArea? Check(IReadOnlyList<Slot> slots, out string why) => Check(slots, null, out why);

    public MapArea? Check(IReadOnlyList<Slot> slots, string? character, out string why)
    {
        why = "";
        if (slots.FirstOrDefault(s => s.Class is not (MapClass or MapKeyClass or FragmentClass)) is { Class: not null } wrong)
        {
            why = $"{wrong.Name} cannot be used in the Map Device: only maps and map fragments can.";
            return null;
        }
        var maps = slots.Where(s => s.Class is MapClass or MapKeyClass).ToList();
        if (maps.Count == 0)
        {
            // A fragment alone may open its area (An Audience With The King).
            var fragments = slots.Where(s => s.Class == FragmentClass).ToList();
            if (fragments.Count == 1 && AreaOfFragment(fragments[0].TypeHash) is { } fragmentArea) return fragmentArea;
            why = "Put a map into the Map Device first.";
            return null;
        }
        if (maps.Count > 1) { why = "Only one map can be activated at a time."; return null; }
        if (AreaOf(maps[0].TypeHash) is { } area) return area;
        if (_atlas?.TierOf(maps[0].TypeHash) is int tier)
        {
            if (_atlas.Selected(character) is { } layout)
                return new(layout.Hash16, layout.Id, layout.Name, tier) { Level = MapLevel(tier) };
            why = "No Atlas layout is available: extract AtlasNode.dat and WorldAreas.dat.";
            return null;
        }
        why = $"{maps[0].Name} opens no known area.";
        return null;
    }

    /// <summary>A generic map's area level: tier 1 is 68 and tier 16 is 83 (3.29 rules).</summary>
    public static int MapLevel(int tier) => Math.Clamp(tier + 67, 1, 100);

    /// <summary>A character's opened map: its area, its instance (0 until the first portal is taken) and the places around
    /// the device (0 to <see cref="PortalCount"/> - 1, <see cref="PortalCell"/>) whose portal is still there.</summary>
    public sealed record OpenMap(MapArea Area, ulong Instance, IReadOnlyList<int> Left)
    {
        public int PortalsLeft => Left.Count;
    }

    private static readonly ConcurrentDictionary<string, OpenMap> Open = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The character's open map, or null.</summary>
    public static OpenMap? OpenOf(string character) => Open.GetValueOrDefault(character);

    /// <summary>Opens a map for the character with a portal at every place, replacing the one open before.</summary>
    public static OpenMap Activate(string character, MapArea area) =>
        Open[character] = new OpenMap(area, 0, [.. Enumerable.Range(0, PortalCount)]);

    /// <summary>Takes the portal at place <paramref name="index"/> of the character's open map, so it does not come back.
    /// Returns the map as it was (its instance to enter, 0 for a new one) and how many portals are left, or null when no
    /// map is open or that portal was taken already. The last one taken closes the map.</summary>
    public static (OpenMap Map, int Left)? TakePortal(string character, int index)
    {
        while (Open.TryGetValue(character, out var map))
        {
            if (!map.Left.Contains(index)) return null;
            var after = map with { Left = [.. map.Left.Where(i => i != index)] };
            bool done = after.Left.Count == 0 ? Open.TryRemove(new(character, map)) : Open.TryUpdate(character, after, map);
            if (done) return (map, after.Left.Count);
        }
        return null;
    }

    /// <summary>The first portal's transfer created the map's instance: the other portals lead there.</summary>
    public static void Entered(string character, MapArea area, ulong instance)
    {
        if (Open.TryGetValue(character, out var map) && map.Area == area && map.Instance == 0)
            Open.TryUpdate(character, map with { Instance = instance }, map);
    }

    /// <summary>The map's instance is gone (expired): the map closes.</summary>
    public static void Close(string character) => Open.TryRemove(character, out _);

    /// <summary>Where portal <paramref name="index"/> of <paramref name="count"/> stands: on a ring around the device,
    /// starting in front of it (south, +y).</summary>
    public static (int X, int Y) PortalCell(int deviceX, int deviceY, int index, int count = PortalCount, int radius = 16)
    {
        double angle = Math.PI / 2 + 2 * Math.PI * index / Math.Max(count, 1);
        return (deviceX + (int)Math.Round(radius * Math.Cos(angle)), deviceY + (int)Math.Round(radius * Math.Sin(angle)));
    }
}
