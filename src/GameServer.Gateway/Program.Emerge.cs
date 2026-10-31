using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    // Monsters that rise from the ground (MonsterEmerge.cs); loaded with the first monster spawned.
    internal static readonly MonsterEmerge Emerges = new();

    // Starts the connection's emerge watch for a monster just created waiting to rise (SpawnMonster), unless it already
    // runs.
    internal static void WatchBuried(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        bool start;
        lock (conn.Monsters) start = MonsterEmerge.StartWatch(conn);
        if (start) _ = RunEmergeWatch(remote, stream, ciphers, conn);
    }

    // Every 250 ms: the monsters waiting near the player, and lurking ones that were hit, rise (MonsterEmerge.RiseLocked).
    // For each, S→C 0x1ab starts its Emerge basic action in place (the .act's Emerge clip plays from the paused first
    // frame), 0x2ba steps its Transitionable to the transitioned state (on_transition: EnableTargetable), and when the clip
    // has played (game time) 0x1ac ends the action; meanwhile it is busy, so the AI only chases and attacks after it.
    // Rising does not wait for the AI (/ai off still lets a /spawn_mob emerge monster rise next to the player); it waits
    // while the game is paused or the player is dead.
    internal static async Task RunEmergeWatch(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        try
        {
            while (!conn.Closed)
            {
                await Task.Delay(250);
                if (conn.Paused || conn.PlayerLife == 0) continue;
                double now = conn.Clock.Elapsed.TotalSeconds;
                List<(uint Id, SpawnedMonster Monster, (int X, int Y) At, double Seconds)> risen;
                bool watching;
                using (ProjectileMutation(conn))
                {
                    now = ProjectileWorldTime(conn);
                    risen = Emerges.RiseLocked(conn, conn.Walk.Position(now), now, out watching);
                }
                foreach (var (id, m, at, seconds) in risen)
                    _ = Emerge(remote, stream, ciphers, conn, id, m, at, seconds);
                if (!watching) return;
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The connection closed while sending.
        }
    }

    internal static async Task Emerge(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, uint id,
        SpawnedMonster m, (int X, int Y) at, double seconds)
    {
        if (Emerges.SkillId is not ushort skill || m.Variety is not { } v) return;
        try
        {
            int index = v.ComponentsOrBase.ToList().IndexOf("Transitionable");
            byte state = MonsterEmerge.RisenState(v);
            var messages = MonsterEmerge.RiseMessages(id, at, skill, (byte)index, state);
            await Send(remote, stream, ciphers, messages[0],
                $"0x01ab monster 0x{id:x} ({m.Label}) emerges at ({at.X}, {at.Y}): Emerge action 0x{skill:x4}, {seconds:0.00} s");
            await Send(remote, stream, ciphers, messages[1], $"0x02ba monster 0x{id:x} ({m.Label}) Transitionable → {state} (targetable)");
            // The clip's length in game time, like BusyUntil: a pause stops it.
            double end = conn.Clock.Elapsed.TotalSeconds + seconds;
            while (!conn.Closed && conn.Clock.Elapsed.TotalSeconds < end) await Task.Delay(50);
            // Not for a monster that died or was removed (/despawn) meanwhile.
            bool Still() { lock (conn.Monsters) return !conn.Closed && !m.Dead && conn.Monsters.GetValueOrDefault(id) == m; }
            if (!Still()) return;
            await Send(remote, stream, ciphers, InstanceProtocol.ActionEnded(id), $"0x01ac monster 0x{id:x} ({m.Label}) emerge action ended",
                still: Still);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The connection closed while sending.
        }
    }
}
