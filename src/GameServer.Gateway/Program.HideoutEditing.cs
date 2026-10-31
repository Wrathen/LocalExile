using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    internal static IReadOnlyDictionary<string, HideoutPlacement>? SavedHideoutPlacements(ClientState conn) =>
        CharacterSaves.Load(CharacterName(conn))?.HideoutPlacements?.GetValueOrDefault(conn.Layout.Id);

    internal static void PrepareHideoutWaypoint(ClientState conn)
    {
        if (HideoutCatalog.ByArea(conn.Layout.Hash) is null || conn.Layout.Waypoint is not { } waypoint) return;
        if (SavedHideoutPlacements(conn)?.GetValueOrDefault("waypoint") is { } p)
            conn.MoveWaypoint(waypoint = waypoint with { GridX = p.X, GridY = p.Y, Rotation = p.Rotation });
        // A hideout always starts on its saved waypoint, including logins and portal returns.
        conn.Use(conn.Layout with { SpawnX = waypoint.GridX, SpawnY = waypoint.GridY });
        conn.HideoutEditor.Register(WaypointCatalog.ObjectId, "waypoint", waypoint.GridX, waypoint.GridY,
            waypoint.Rotation, -1);
    }

}
