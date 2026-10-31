using GameServer.Core.Protocol.Instance;

/// <summary>Stable per-template placements; runtime object ids never enter the character save.</summary>
sealed record HideoutPlacement(int X, int Y, float Rotation);

sealed class HideoutEditing
{
    public sealed record Entry(string Key, HideoutPlacement Position, int BlockerSize, double? CollisionRadius = null);
    readonly Dictionary<uint, Entry> _objects = [];

    /// <summary>Where a registered object stands now.</summary>
    public HideoutPlacement? PositionOf(uint id) => _objects.GetValueOrDefault(id)?.Position;

    public void Register(uint id, string key, int x, int y, float rotation, int size, double? collisionRadius = null) =>
        _objects[id] = new(key, new(x, y, rotation), size, collisionRadius);

    /// <summary>The cell check for a drop: inside the captured walk map and walkable. Without a capture
    /// (live.ps1 -Walkmaps never entered this template) there is no grid to check, so the client's own Edit UI
    /// placement check decides, instead of refusing every move against a 0 × 0 grid.</summary>
    public static Func<int, int, bool>? Cells(AreaLayout layout) => layout.Walkable is { } walkable
        ? (x, y) => x < layout.GridW && y < layout.GridH && walkable(x, y)
        : null;

    public bool TryMove(InstanceHideoutEditing.Move move, Func<int, int, bool>? walkable,
        Action<string, HideoutPlacement> persist, out Entry? moved)
    {
        moved = null;
        if (!_objects.TryGetValue(move.ObjectId, out var entry) || !float.IsFinite(move.Rotation)
            || walkable?.Invoke(move.X, move.Y) == false) return false;
        var position = new HideoutPlacement(move.X, move.Y, move.Rotation);
        // A failed write must leave the authoritative scene and client at the previous position.
        persist(entry.Key, position);
        _objects[move.ObjectId] = moved = entry with { Position = position };
        return true;
    }

    public static HideoutScene Apply(HideoutScene scene, IReadOnlyDictionary<string, HideoutPlacement>? saved)
    {
        if (saved is null) return scene;
        HideoutCatalog.Doodad D(string key, HideoutCatalog.Doodad d) => saved.TryGetValue(key, out var p)
            ? d with { X = p.X, Y = p.Y, Rotation = p.Rotation } : d;
        var guild = saved.TryGetValue("guild-stash", out var g) ? (g.X, g.Y) : scene.Guild;
        return scene with
        {
            Saved = saved,
            Stash = D("stash", scene.Stash), Guild = guild, Bench = D("bench", scene.Bench),
            Device = D("map-device", scene.Device),
            Npcs = [.. scene.Npcs.Select(n => saved.TryGetValue("npc:" + n.Id, out var p)
                ? n with { X = p.X, Y = p.Y, Rotation = p.Rotation } : n)]
        };
    }
}
