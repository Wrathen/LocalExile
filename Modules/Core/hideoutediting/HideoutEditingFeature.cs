using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;
using static Program;

static class HideoutEditingFeature
{
    internal static async Task MoveHideoutObject(string remote, NetworkStream stream, ConnectionCiphers ciphers,
        ClientState conn, InstanceHideoutEditing.Move move)
    {
        if (!conn.PlayerSent || conn.PendingTransfer is not null) return;
        if (HomeHideout(conn)?.AreaHash16 != conn.Layout.Hash)
        {
            Console.WriteLine($"[gateway] {remote} refused hideout move 0x{move.ObjectId:x}: {conn.Layout.Id} is not the character's selected hideout");
            return;
        }
        if (!conn.HideoutEditor.TryMove(move, HideoutEditing.Cells(conn.Layout),
            (key, p) => CharacterSaves.SetHideoutPlacement(CharacterName(conn), conn.Layout.Id, key, p), out var entry))
        {
            Console.WriteLine($"[gateway] {remote} refused hideout move 0x{move.ObjectId:x} to ({move.X},{move.Y})");
            return;
        }
        var p = entry!.Position;
        if (entry.BlockerSize >= 0) conn.SetBlocker(move.ObjectId, p.X, p.Y, entry.BlockerSize, entry.CollisionRadius);
        if (move.ObjectId == conn.StashObject) conn.StashAt = (p.X, p.Y);
        if (move.ObjectId == conn.GuildStashObject) conn.GuildStashAt = (p.X, p.Y);
        if (move.ObjectId == conn.CraftingBenchObject) conn.CraftingBenchAt = (p.X, p.Y);
        lock (conn.Npcs)
            if (conn.Npcs.TryGetValue(move.ObjectId, out var npc))
                conn.Npcs[move.ObjectId] = npc with { X = p.X, Y = p.Y };
        lock (conn.HideoutDoodads)
            if (conn.HideoutDoodads.TryGetValue(move.ObjectId, out var doodad))
                conn.HideoutDoodads[move.ObjectId] = doodad with { X = p.X, Y = p.Y, Rotation = p.Rotation };
        if (move.ObjectId == WaypointCatalog.ObjectId && conn.Layout.Waypoint is { } waypoint)
            conn.MoveWaypoint(waypoint with { GridX = p.X, GridY = p.Y, Rotation = p.Rotation });
        conn.Furniture = null;
        await Send(remote, stream, ciphers, InstanceProtocol.Teleport(move.ObjectId, p.X, p.Y, 0x7fffffff),
            $"hideout move {entry.Key} 0x{move.ObjectId:x} to ({p.X},{p.Y}), saved");
        await Send(remote, stream, ciphers, InstanceHideoutEditing.Rotation(move.ObjectId, p.Rotation),
            $"hideout rotation {p.Rotation:0.###}");
        await Send(remote, stream, ciphers, InstanceHideoutEditing.RebuildCollision(),
            "rebuild hideout model collision from current placements");
        await PushOutOfCollision(remote, stream, ciphers, conn);
    }
}
