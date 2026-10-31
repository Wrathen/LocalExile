using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.Diagnostics;

partial class Program
{
    // The smoke cloud's and consecrated ground's art (FlaskGrounds.LoadArt), read once.
    static readonly Lazy<Dictionary<FlaskGrounds.Kind, (string Ao, int ArtRadius)>> FlaskGroundArt = new(() => FlaskGrounds.LoadArt("data"));

    // A utility flask's use: what its implicit makes where the player stands (FlaskGrounds), for the flask's duration: a smoke
    // cloud or consecrated ground (an effect object with the ground's art), or a taunt of the hostile monsters within its
    // radius (TauntLocked: 3 s, raised by taunt_duration_+%; the enemy-debuff step shows the taunted buff).
    static async Task MakeFlaskGrounds(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        string flask, IReadOnlyDictionary<string, int> local, double seconds)
    {
        double now = conn.Clock.Elapsed.TotalSeconds;
        var at = conn.Walk.Position(now);
        foreach (var (kind, radius) in FlaskGrounds.OnUse(local))
        {
            if (kind == FlaskGrounds.Kind.Taunt)
            {
                var player = CharacterPlayerStats(conn);
                double taunt = TauntSeconds(name => Stats.Key(name) is uint key ? player.GetValueOrDefault(key) : 0);
                int taunted;
                lock (conn.Monsters) taunted = TauntLocked(conn, at, radius, now, taunt);
                Console.WriteLine($"[gateway] {remote} {flask}: taunts {taunted} monster(s) within {radius} cells of ({at.X}, {at.Y}) for {taunt:0.##} s");
                continue;
            }
            var ground = new FlaskGrounds.Ground(kind, at.X, at.Y, radius, now + seconds, await SendGroundArt(remote, stream, ciphers, conn, kind, at, radius));
            Console.WriteLine($"[gateway] {remote} {flask}: {kind} of radius {radius} at ({at.X}, {at.Y}) for {seconds:0.##} s");
            if (conn.FlaskGrounds.Add(ground)) _ = RunFlaskGrounds(remote, stream, ciphers, conn);
        }
    }

    // A ground's art (an effect object), or null when the tables have none for its kind.
    static async Task<uint?> SendGroundArt(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        FlaskGrounds.Kind kind, (int X, int Y) at, int radius)
    {
        if (!FlaskGroundArt.Value.TryGetValue(kind, out var art)) return null;
        uint obj;
        lock (conn.Monsters) obj = conn.NextObjectId++;
        // The art is drawn for ArtRadius world units (250/23 per cell): scaled to the ground's radius.
        float scale = (float)(radius * (250.0 / 23) / Math.Max(1, art.ArtRadius));
        var effect = new EffectObject(at.X, at.Y, art.Ao) { Flags0 = EffectObject.FixedRotation, Scale = scale };
        await Send(remote, stream, ciphers, InstanceEntities.CreateObject(obj, InstanceEntities.ObjectTypeHash(EffectObject.TypePath), effect.Blob()),
            $"0x02b9 create effect 0x{obj:x} {art.Ao} at ({at.X}, {at.Y}), scale {scale:0.###} for radius {radius}");
        await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(obj), null);
        return obj;
    }

    // Every 100 ms of game time while a flask ground lasts or the player has a stationary radius (FlaskGroundsTick):
    // monsters standing in a smoke cloud are blinded (the blinded buff, Program.Blinded).
    static async Task RunFlaskGrounds(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        try
        {
            while (!conn.Closed && !conn.RageRetired)
            {
                await Task.Delay(100);
                if (conn.Closed || conn.RageRetired) break;
                if (conn.LeavingArea || conn.Paused) continue;
                if (await FlaskGroundsTick(remote, stream, ciphers, conn, conn.Clock.Elapsed.TotalSeconds)) return;
            }
        }
        catch (Exception ex)
        {
            if (!conn.Closed) Console.WriteLine($"[gateway] {remote} flask grounds: {ServerLog.Describe(ex)}");
        }
        conn.FlaskGrounds.TickerExited();
    }

    // One step: grounds whose time is up go (their art removed), monsters standing in a smoke cloud are
    // blinded (the blinded buff, FlaskGrounds.Accuracy / Evasion), a stationary player with a stationary radius (Anointed
    // Champion) has consecrated ground around him (made where he stops, removed when he moves, dies or is in a town), and the
    // player on consecrated ground or within its linger gets its effect (FlaskGrounds.PlayerStats, the ground_consecration
    // buff, the stats again). True when the ticker stops: nothing is left to look after.
    static async Task<bool> FlaskGroundsTick(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, double now)
    {
        var state = conn.FlaskGrounds;
        foreach (var g in state.Expire(now))
            if (g.Object is uint obj) await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(obj), $"0x02bb remove effect 0x{obj:x} ({g.Kind} ends)");
        // Smoke: who stands in it now.
        List<(uint Id, bool Inside)> monsters;
        lock (conn.Monsters)
            monsters = [.. conn.Monsters.Select(kv => (kv.Key, kv.Value.Hostile && !kv.Value.Dead && kv.Value.Life > 0
                && state.Covers(FlaskGrounds.Kind.SmokeCloud, kv.Value.Walk.Position(now))))];
        foreach (uint gone in state.BlindedMonsters().Except(monsters.Select(m => m.Id)))
            state.SetBlinded(gone, false, () => 0);
        foreach (var (id, inside) in monsters)
            if (state.SetBlinded(id, inside, () => { lock (conn.Monsters) return conn.NextBuff++; }) is { } change)
                await Send(remote, stream, ciphers, change.Added
                    ? InstanceEntities.AddBuff(id, change.Buff, FlaskGrounds.BlindedBuffRow, PlayerId)
                    : InstanceEntities.RemoveBuff(id, change.Buff), $"0x{(change.Added ? "01c1" : "01c2")} monster 0x{id:x} {(change.Added ? "blinded in the smoke cloud" : "no longer blinded")}");
        // Consecrated ground around the stationary player: stationary is the walk having ended (as ChargeGains reads it).
        bool alive = conn.PlayerLife > 0;
        var here = conn.Walk.Position(now);
        var (stale, make) = state.Stationary(alive && now >= conn.Walk.ArrivesAt && !InTown(conn), here);
        if (stale?.Object is uint old)
            await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(old), $"0x02bb remove effect 0x{old:x} (stationary consecrated ground)");
        if (make > 0)
        {
            state.PlaceStationary(new(FlaskGrounds.Kind.ConsecratedGround, here.X, here.Y, make, double.PositiveInfinity,
                await SendGroundArt(remote, stream, ciphers, conn, FlaskGrounds.Kind.ConsecratedGround, here, make)));
            Console.WriteLine($"[gateway] {remote} stationary: consecrated ground of radius {make} at ({here.X}, {here.Y})");
        }
        // The player on consecrated ground, or within its linger.
        if (state.SetConsecrated(state.OnConsecrated(alive, here, now), () => { lock (conn.Monsters) return conn.NextBuff++; }) is { } consecrated)
        {
            await Send(remote, stream, ciphers, consecrated.Added
                ? InstanceEntities.AddBuff(PlayerId, consecrated.Buff, FlaskGrounds.ConsecratedBuffRow, PlayerId)
                : InstanceEntities.RemoveBuff(PlayerId, consecrated.Buff),
                $"0x{(consecrated.Added ? "01c1" : "01c2")} player {(consecrated.Added ? "on" : "off")} consecrated ground");
            await SendCharacterStats(remote, stream, ciphers, conn, $"consecrated ground {(consecrated.Added ? "on" : "off")}");
        }
        return state.StopTickerIfIdle();
    }
}
