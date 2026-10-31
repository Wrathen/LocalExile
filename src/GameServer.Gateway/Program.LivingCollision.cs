using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;

partial class Program
{
    internal static async Task RunLivingCollisions(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        int frames = 0;
        try
        {
            while (!conn.Closed && conn.PendingTransfer is null)
            {
                if (!conn.Paused)
                {
                    await CorrectMonsterCollisions(remote, stream, ciphers, conn);
                    await PushOutOfCollision(remote, stream, ciphers, conn, living: true);
                }
                PublishPlayerPose(conn);
                if ((frames++ & 1) == 0) await SyncInstancePlayers(remote, stream, ciphers, conn);
                await Task.Delay(50);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException) { }
        finally { conn.Instance.RemovePlayer(conn.PeerId); }
    }

    internal static void PublishPlayerPose(ClientState conn)
    {
        lock (conn.Monsters)
        {
            if (conn.Closed || conn.LeavingArea) { conn.Instance.RemovePlayer(conn.PeerId); return; }
            if (conn.PeerTemplate is null || !conn.PlayerSent) return;
            RefreshPeerScale(conn, CharacterPlayerStats(conn)[ActorScale.Final]);
        }
    }

    // Called under the owner's lock. Replace only final scale in an immutable template, retaining creation stats,
    // equipment and skills. Observers read published snapshots and never lock another connection's simulation.
    internal static void RefreshPeerScale(ClientState conn, int scale)
    {
        if (conn.PeerTemplate is not { } template) return;
        if ((template.Stats ?? []).SingleOrDefault(s => s.Key == ActorScale.Final).Value != scale)
            conn.PeerTemplate = template = template with
            { Stats = [.. (template.Stats ?? []).Where(s => s.Key != ActorScale.Final), (ActorScale.Final, scale)] };
        if (!conn.PlayerSent || conn.Closed || conn.LeavingArea || conn.Instance is null) return;
        var p = conn.Walk.Position(conn.Clock.Elapsed.TotalSeconds);
        conn.Instance.PublishPlayer(new(conn.PeerId, conn.PeerType, template, p.X, p.Y, conn.PlayerLife, conn));
    }

    internal static async Task SyncInstancePlayers(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        var ids = conn.Instance.PlayersExcept(conn.PeerId).Select(p => p.Id)
            .Concat(conn.SeenPlayers.Keys).Distinct().ToArray();
        foreach (uint id in ids)
            await Send(remote, stream, ciphers, [], null, atGate: () =>
            {
                if (conn.Closed || conn.LeavingArea) return null;
                var packets = new List<byte[]>();
                // Read after waiting: queued creation/change must not restore an older scale or departed owner.
                if (conn.Instance.Player(id) is not { } peer)
                {
                    if (conn.SeenPlayers.Remove(id)) packets.Add(InstanceProtocol.RemoveObject(id));
                    return packets.SelectMany(p => p).ToArray();
                }
                bool exists = conn.SeenPlayers.TryGetValue(peer.Id, out var previous);
                if (!exists || previous!.Life == 0 && peer.Life > 0)
                {
                    if (exists) packets.Add(InstanceProtocol.RemoveObject(peer.Id));
                    var player = peer.Template with { GridX = peer.X, GridY = peer.Y, Life = peer.Life,
                        Skills = [], QuestFlags = [], LeagueQuestFlags = [] };
                    packets.Add(InstanceEntities.CreateObject(peer.Id, peer.Type, player.Blob()));
                    packets.Add(InstanceProtocol.EnterWakeZone(peer.Id));
                }
                else
                {
                    if ((previous!.X, previous.Y) != (peer.X, peer.Y))
                        packets.Add(Math.Max(Math.Abs(previous.X - peer.X), Math.Abs(previous.Y - peer.Y)) > 20
                            ? InstanceProtocol.Teleport(peer.Id, peer.X, peer.Y, 0x7fffffff)
                            : InstanceProtocol.MoveTo(peer.Id, peer.X, peer.Y, .1f, 0x7fffffff));
                    if (previous.Life != peer.Life)
                        packets.Add(InstanceProtocol.LifeUpdate(peer.Id, peer.Life));
                    int scale = (peer.Template.Stats ?? []).SingleOrDefault(s => s.Key == ActorScale.Final).Value;
                    int before = (previous.Template.Stats ?? []).SingleOrDefault(s => s.Key == ActorScale.Final).Value;
                    if (scale != before)
                        packets.Add(InstanceEntities.StatsUpdate(peer.Id, [(ActorScale.Final, scale)]));
                }
                conn.SeenPlayers[peer.Id] = peer;
                return packets.SelectMany(p => p).ToArray();
            });
    }

    internal static Task CorrectMonsterCollisions(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        using (ProjectileMutation(conn, phase: "collision"))
        {
            double now = ProjectileWorldTime(conn);
            var player = conn.Walk.Position(now);
            var peers = conn.Instance.PlayersExcept(conn.PeerId).Where(p => p.Life > 0).ToArray();
            // Every living body's cell and size once per frame (kept current as bodies are moved below), instead of
            // recomputing each other monster's position for every monster: the overlap test was O(n²) walk queries.
            using var frame = conn.BeginMonsterBlockerFrame(now, includePrevious: true);
            var bodies = frame.Bodies;
            int index = 0;
            foreach (var (id, m) in conn.Monsters)
            {
                if (!LivingCollision.Blocks(m)) { m.CollisionPosition = null; continue; }
                int self = index++;
                var current = (bodies[self].X, bodies[self].Y);
                var previous = m.CollisionPosition ?? current;
                int size = bodies[self].Size;
                bool playerOverlap = conn.PlayerLife > 0 && !conn.Phasing
                    && Math.Max(Math.Abs(current.X - player.X), Math.Abs(current.Y - player.Y)) < size + 2;
                bool peerOverlap = peers.Any(p => Math.Max(Math.Abs(current.X - p.X), Math.Abs(current.Y - p.Y)) < size + 2);
                bool bodyOverlap = false;
                foreach (var other in bodies)
                    if (other.Id < id && Math.Max(Math.Abs(other.X - current.X), Math.Abs(other.Y - current.Y)) < other.Size + size + 1
                        && ClientPathfinder.ObjectBlocks([new(other.X, other.Y, other.Size)],
                        current.X, current.Y, size + 1, size + 4)) { bodyOverlap = true; break; }
                if (current == previous && !playerOverlap && !peerOverlap && !bodyOverlap) continue;
                var blockers = conn.MonsterBlockers(id, includePrevious: true);
                bool Floor(int x, int y) => conn.Layout.WalkableFor(x, y, size) && !conn.Barrier.Contains((x, y));
                var stop = LivingCollision.Sweep(previous, current, size, Floor, blockers);
                if (!CollisionEscape.Clear(stop, conn.Layout.GridW, conn.Layout.GridH, Floor, blockers, size))
                    stop = CollisionEscape.Find(stop, conn.Layout.GridW, conn.Layout.GridH, Floor, blockers, moverSize: size) ?? stop;
                if (stop != current)
                {
                    // Only the server's estimate moves: the client paths every monster around the other bodies itself
                    // and paths again when a walk ends short (07 §5), so an ActionEnded + Teleport here only made the
                    // monster stop and jump on screen. Like the client, the estimate then paths again to the same target.
                    m.Walk.Teleport(stop.X, stop.Y, now);
                    frame.Move(id, stop.X, stop.Y);
                    if (m.LastTarget is { } target) m.Walk.Order(target.X, target.Y, now);
                }
                m.CollisionPosition = stop;
                frame.CompleteCorrection(id, stop.X, stop.Y);
            }
        }
        return Task.CompletedTask;
    }
}
