using System.Collections.Concurrent;

partial class Program
{
    sealed record RampageTransfer(string Character, DateTime At, RampageState.Carried State);
    static readonly ConcurrentDictionary<uint, RampageTransfer> CarriedRampage = new();

    internal static void CarryRampage(ClientState conn, uint ticket)
    {
        lock (conn.Monsters)
        {
            if (conn.Closed || conn.RageRetired) return;
            CharacterPlayerStats(conn);
            double at = conn.RampageTransferAt ?? ProjectileWorldTime(conn);
            var state = conn.Rampage.Carry(at);
            conn.RampageWorld.Disable();
            foreach (var (old, entry) in CarriedRampage)
                if (DateTime.UtcNow - entry.At > TimeSpan.FromMinutes(5)) CarriedRampage.TryRemove(old, out _);
            CarriedRampage.TryAdd(ticket, new(CharacterName(conn), DateTime.UtcNow, state));
        }
    }

    internal static void RestoreRampage(ClientState conn)
    {
        lock (conn.Monsters)
        {
            if (conn.Closed || conn.RageRetired || !CarriedRampage.TryGetValue(conn.Ticket, out var entry)
                || entry.Character != CharacterName(conn)
                || !CarriedRampage.TryRemove(new KeyValuePair<uint, RampageTransfer>(conn.Ticket, entry))) return;
            if (DateTime.UtcNow - entry.At <= TimeSpan.FromMinutes(5) && conn.PlayerLife > 0 && RampageSource(conn))
                conn.Rampage.Restore(entry.State, ProjectileWorldTime(conn));
        }
    }
}
