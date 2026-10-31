using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    // Only call while constructing an accepted player-stat packet inside the send gate. Pure stat
    // construction must not acknowledge anything: remember exactly the pair this packet encodes.
    internal static byte[] PlayerStatsUpdateAtGate(ClientState conn, IReadOnlyList<(uint Key, int Value)> stats)
    {
        var packet = InstanceEntities.StatsUpdate(PlayerId, stats);
        uint? counterKey = Stats.Key(RecentEnemyHits.Counter), chanceKey = Stats.Key(RecentEnemyHits.VirtualChance);
        int? count = null, chance = null;
        foreach (var (key, value) in stats)
        {
            if (key == counterKey) count = value;
            if (key == chanceKey) chance = value;
        }
        if (count is int sentCount && chance is int sentChance) conn.PresentedRecentEnemyHits = (sentCount, sentChance);
        return packet;
    }

    // Every full player stat/Actor refresh shares this gate, including refreshes caused by other mechanics.
    // Otherwise a packet prepared before expiry could overwrite a newer Fortification expiry update.
    internal static Task SendPlayerStatSnapshot(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        Func<byte[]> packet, string? label) => Send(remote, stream, ciphers, [], label, atGate: () =>
        {
            using (conn.Performance.Lock(conn.Monsters, "stats"))
            {
                if (conn.Closed || conn.LeavingArea || conn.RageRetired) return null;
                return WithOwnAuraChanges(conn, packet);
            }
        });
}
