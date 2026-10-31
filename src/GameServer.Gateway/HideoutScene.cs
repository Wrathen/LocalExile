using GameServer.Core.Protocol.Instance;
using GameServer.Modding;

/// <summary>The functional furniture and NPCs of a hideout, placed on its own floor rather than Lush's coordinates.</summary>
sealed record HideoutScene(HideoutCatalog.Doodad Stash, (int X, int Y) Guild,
    HideoutCatalog.Doodad Bench, (int X, int Y) LargeStation, HideoutCatalog.Doodad Device, IReadOnlyList<NpcCatalog.Placement> Npcs)
{
    /// <summary>A reserved spot, for a module's furniture.</summary>
    public (int X, int Y) SmallStation { get; init; }

    /// <summary>Where the character has moved things, by editor key.</summary>
    public IReadOnlyDictionary<string, HideoutPlacement>? Saved { get; init; }

    /// <summary>Where a module's station stands: moved by the character, else its reserved spot.</summary>
    public (int X, int Y, float Rotation) Station(HideoutSlot slot, string editorKey)
    {
        if (Saved?.TryGetValue(editorKey, out var moved) == true) return (moved.X, moved.Y, moved.Rotation);
        var (x, y) = slot == HideoutSlot.Large ? LargeStation : SmallStation;
        return (x, y, 0);
    }

    public static HideoutScene Build(HideoutCatalog.Hideout hideout, HideoutCatalog.Hideout fallback, Func<int, int, bool>? walkable)
    {
        var waypoint = hideout.First(WaypointObject.MetadataPath) ?? fallback.First(WaypointObject.MetadataPath)!;
        var origin = fallback.First(WaypointObject.MetadataPath)!;
        var occupied = new List<(int X, int Y, int Size)> { (waypoint.X, waypoint.Y, 4) };

        (int X, int Y) Place(int x, int y, int size)
        {
            bool Clear(int px, int py)
            {
                if (px < size || py < size || occupied.Any(o => Math.Abs(o.X - px) < o.Size + size + 3
                    && Math.Abs(o.Y - py) < o.Size + size + 3)) return false;
                for (int dx = -size; dx <= size; dx++)
                    for (int dy = -size; dy <= size; dy++)
                        if (walkable?.Invoke(px + dx, py + dy) == false) return false;
                return true;
            }
            for (int r = 0; r <= 128; r++)
                for (int dy = -r; dy <= r; dy++)
                    foreach (int dx in r == 0 ? new[] { 0 } : new[] { -r, r })
                    {
                        if (Clear(x + dx, y + dy)) return Keep(x + dx, y + dy);
                        if (Clear(x + dy, y + dx)) return Keep(x + dy, y + dx);
                    }
            throw new InvalidOperationException($"No clear hideout floor near ({x},{y}) in {hideout.AreaId}");

            (int, int) Keep(int px, int py) { occupied.Add((px, py, size)); return (px, py); }
        }

        HideoutCatalog.Doodad Template(string type) => hideout.First(type) ?? fallback.First(type)!;
        var stash = Template(StashObject.MetadataPath);
        var (sx, sy) = Place(stash.X, stash.Y, 2);
        stash = stash with { X = sx, Y = sy };
        var guild = Place(sx - 6, sy - 6, 2);
        var bench = Template(HideoutCatalog.CraftingBenchType);
        var (bx, by) = Place(guild.X - 12, guild.Y + 12, 2);
        bench = bench with { X = bx, Y = by };
        // Reserved whatever the modules are, so taking one away moves nothing else.
        var large = Place(bx - 18, by + 18, 8);
        var device = hideout.Doodads.FirstOrDefault(HideoutCatalog.IsPlaced) ?? fallback.Doodads.First(HideoutCatalog.IsPlaced);
        // Retain the established Lush placement; every other template gets the same device beside its waypoint.
        var (mx, my) = Place(hideout.AreaId == HideoutCatalog.DefaultArea ? device.X : waypoint.X + 20,
            hideout.AreaId == HideoutCatalog.DefaultArea ? device.Y : waypoint.Y + 20, 4);
        device = device with { X = mx, Y = my };
        var npcs = new List<NpcCatalog.Placement>();
        foreach (var npc in NpcCatalog.HideoutNpcs)
        {
            var (x, y) = Place(waypoint.X + npc.X - origin.X, waypoint.Y + npc.Y - origin.Y, 4);
            npcs.Add(npc with { X = x, Y = y });
        }
        var small = Place(sx + 16, sy + 16, 4);
        return new(stash, guild, bench, large, device, npcs) { SmallStation = small };
    }
}
