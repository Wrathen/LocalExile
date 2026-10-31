using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    internal static async Task PushOutOfCollision(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, bool living = false)
    {
        if (!conn.PlayerSent || conn.PlayerLife == 0 || conn.PendingTransfer is not null || conn.Closed
            || conn.Layout.GridW <= 0 || conn.Layout.GridH <= 0) return;
        (int X, int Y) from, to;
        using (ProjectileMutation(conn))
        {
            double now = ProjectileWorldTime(conn);
            from = conn.Walk.Position(now);
            bool Walkable(int x, int y) => conn.Layout.Walkable?.Invoke(x, y) != false && !conn.Barrier.Contains((x, y));
            if (CollisionEscape.Find(from, conn.Layout.GridW, conn.Layout.GridH, Walkable,
                living ? conn.CorrectionBlockers(includeMonsters: !conn.Walk.EscapingCrowd(now))
                    : conn.StationaryBlockers) is not { } free) return;
            to = free;
            Interlocked.Increment(ref conn.ActionCount);
            conn.ChannelSkill = 0;
            conn.Walk.Teleport(to.X, to.Y, now);
        }
        await Send(remote, stream, ciphers, InstanceProtocol.ActionEnded(PlayerId), "stop action before collision correction");
        await Send(remote, stream, ciphers, InstanceProtocol.Teleport(PlayerId, to.X, to.Y, 0x7fffffff),
            $"collision escape: ({from.X},{from.Y}) -> ({to.X},{to.Y})");
    }
}
