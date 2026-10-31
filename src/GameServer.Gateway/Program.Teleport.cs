using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    // /tp (dev): put the player somewhere in the area at once, for testing without walking there (TeleportTargets.cs).
    // The same move as an arena gate's (Program.Travel.cs): the current action ends (a pending hit or walk-up no longer
    // happens), the server's position estimate jumps to the cell, and S→C 0x1b7 moves the client's player there.
    internal static async Task DevTeleport(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, string[] args)
    {
        var candidates = TeleportTargets.Candidates(conn);
        if (args.Length > 0 && args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            await conn.Commands!.Reply(TeleportTargets.List(candidates, args.Length > 1 ? string.Join(' ', args[1..]) : null));
            return;
        }
        // A teleport sent while the player is dead leaves them where they died (Resurrect, Program.Player.cs).
        if (conn.PlayerLife == 0) { await conn.Commands!.Reply("You are dead."); return; }
        if (TeleportTargets.Resolve(conn, args, candidates, out string? error) is not { } to)
        {
            await conn.Commands!.Reply(error!);
            return;
        }
        using (ProjectileMutation(conn))
        {
            Interlocked.Increment(ref conn.ActionCount);
            conn.Walk.Teleport(to.X, to.Y, ProjectileWorldTime(conn));
        }
        await Send(remote, stream, ciphers, InstanceProtocol.Teleport(PlayerId, to.X, to.Y),
            $"0x01b7 player /tp to ({to.X}, {to.Y}){(to.Next is null ? "" : $" next to {to.Next}")}");
        await conn.Commands!.Reply($"Teleported to ({to.X}, {to.Y}) in {conn.Layout.Id}{(to.Next is null ? "" : $", next to {to.Next}")}.");
    }
}
