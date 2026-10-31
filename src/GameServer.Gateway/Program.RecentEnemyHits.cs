using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    // Read sources, count and clock together. Development input cannot replace either authoritative output.
    internal static (int Count, int Chance) RecentEnemyHitStatsLocked(ClientState conn, IReadOnlyDictionary<uint, int> sources)
    {
        int count = conn.RecentEnemyHits.Count(conn.Clock.Elapsed.TotalSeconds);
        int Get(string name) => Stats.Key(name) is uint key ? sources.GetValueOrDefault(key) : 0;
        return (count, RecentEnemyHits.Chance(count, Get(RecentEnemyHits.PerHit), Get(RecentEnemyHits.GenericChance)));
    }

    internal static void AddRecentEnemyHitStatsLocked(ClientState conn, Dictionary<uint, int> stats)
    {
        var value = RecentEnemyHitStatsLocked(conn, stats);
        if (Stats.Key(RecentEnemyHits.Counter) is uint counter) stats[counter] = value.Count;
        if (Stats.Key(RecentEnemyHits.VirtualChance) is uint chance) stats[chance] = value.Chance;
    }

    internal static RecentEnemyHits.Decision IncomingArmourLocked(ClientState conn, bool relevant, int priorContacts,
        IReadOnlyDictionary<uint, int> stats)
    {
        // The caller captures membership before ALL stat readers, which may prune a later clock boundary.
        int Get(string name) => Stats.Key(name) is uint key ? stats.GetValueOrDefault(key) : 0;
        int chance = RecentEnemyHits.Chance(priorContacts, Get(RecentEnemyHits.PerHit), Get(RecentEnemyHits.GenericChance));
        return RecentEnemyHits.Resolve(conn.Pools.Armour, chance, relevant, conn.IncomingHitRandom);
    }

    // Use the existing player tick for idle expiry. Sample inside the shared send gate, including when
    // a queued refresh crosses a contact, death, modifier change or exact expiry boundary.
    internal static Task SendRecentEnemyHits(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn) =>
        Send(remote, stream, ciphers, [], null, atGate: () =>
        {
            lock (conn.Monsters)
            {
                if (conn.Closed || conn.LeavingArea || conn.Paused) return null;
                var value = RecentEnemyHitStatsLocked(conn, CharacterPlayerStats(conn));
                if (conn.PresentedRecentEnemyHits == value) return null;
                var stats = new Dictionary<uint, int>();
                if (Stats.Key(RecentEnemyHits.Counter) is uint counter) stats[counter] = value.Count;
                if (Stats.Key(RecentEnemyHits.VirtualChance) is uint chance) stats[chance] = value.Chance;
                return PlayerStatsUpdateAtGate(conn, stats.Select(kv => (kv.Key, kv.Value)).ToList());
            }
        });
}
