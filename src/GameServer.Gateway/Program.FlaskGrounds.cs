using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.Diagnostics;

partial class Program
{
    // The smoke cloud's and consecrated ground's art (FlaskGrounds.LoadArt), read once.
    internal static readonly Lazy<Dictionary<FlaskGrounds.Kind, (string Ao, int ArtRadius)>> FlaskGroundArt = new(() => FlaskGrounds.LoadArt("data"));

    // A utility flask's use: what its implicit makes where the player stands (FlaskGrounds), for the flask's duration: a smoke
    // cloud or consecrated ground (an effect object with the ground's art), or a taunt (the taunted buff on the hostile
    // monsters within its radius, for 3 s).
    internal static async Task MakeFlaskGrounds(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        string flask, IReadOnlyDictionary<string, int> local, double seconds)
    {
        double now = conn.Clock.Elapsed.TotalSeconds;
        var at = conn.Walk.Position(now);
        foreach (var (kind, radius) in FlaskGrounds.OnUse(local))
        {
            FlaskGrounds.Ground ground;
            if (kind == FlaskGrounds.Kind.Taunt)
            {
                ground = new(kind, at.X, at.Y, radius, now + FlaskGrounds.TauntSeconds);
                lock (conn.Monsters)
                    foreach (var (id, m) in conn.Monsters)
                        if (!m.Dead && m.Life > 0 && m.Hostile && ground.Covers(m.Walk.Position(now))) ground.Taunted[id] = conn.NextBuff++;
                uint taunt = BitConverter.SingleToUInt32Bits((float)FlaskGrounds.TauntSeconds);
                foreach (var (id, buff) in ground.Taunted)
                    await Send(remote, stream, ciphers, InstanceEntities.AddBuff(id, buff, FlaskGrounds.TauntedBuffRow, PlayerId, u68: taunt), null);
                Console.WriteLine($"[gateway] {remote} {flask}: taunts {ground.Taunted.Count} monster(s) within {radius} cells of ({at.X}, {at.Y}) for {FlaskGrounds.TauntSeconds} s");
            }
            else
            {
                uint? obj = null;
                if (FlaskGroundArt.Value.TryGetValue(kind, out var art))
                {
                    lock (conn.Monsters) obj = conn.NextObjectId++;
                    // The art is drawn for ArtRadius world units (250/23 per cell): scaled to the ground's radius.
                    float scale = (float)(radius * (250.0 / 23) / Math.Max(1, art.ArtRadius));
                    var effect = new EffectObject(at.X, at.Y, art.Ao) { Flags0 = EffectObject.FixedRotation, Scale = scale };
                    await Send(remote, stream, ciphers, InstanceEntities.CreateObject(obj.Value, InstanceEntities.ObjectTypeHash(EffectObject.TypePath), effect.Blob()),
                        $"0x02b9 create effect 0x{obj:x} {art.Ao} at ({at.X}, {at.Y}), scale {scale:0.###} for radius {radius}");
                    await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(obj.Value), null);
                }
                ground = new(kind, at.X, at.Y, radius, now + seconds, obj);
                Console.WriteLine($"[gateway] {remote} {flask}: {kind} of radius {radius} at ({at.X}, {at.Y}) for {seconds:0.##} s");
            }
            if (conn.FlaskGrounds.Add(ground)) _ = RunFlaskGrounds(remote, stream, ciphers, conn);
        }
    }

    // Every 100 ms of game time while a flask ground lasts: monsters standing in a smoke cloud are blinded (the blinded
    // buff, FlaskGrounds.Accuracy / Evasion), the player on consecrated ground gets its regeneration (FlaskGrounds.PlayerStats,
    // the ground_consecration buff, the stats again), and grounds whose time is up go (their art removed, taunts taken off).
    internal static async Task RunFlaskGrounds(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        var state = conn.FlaskGrounds;
        try
        {
            while (!conn.Closed && !conn.RageRetired)
            {
                await Task.Delay(100);
                if (conn.Closed || conn.RageRetired) break;
                if (conn.LeavingArea || conn.Paused) continue;
                double now = conn.Clock.Elapsed.TotalSeconds;
                foreach (var g in state.Expire(now))
                {
                    if (g.Object is uint obj) await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(obj), $"0x02bb remove effect 0x{obj:x} ({g.Kind} ends)");
                    foreach (var (id, buff) in g.Taunted)
                        if (conn.Monsters.ContainsKey(id)) await Send(remote, stream, ciphers, InstanceEntities.RemoveBuff(id, buff), null);
                }
                // Smoke: who stands in it now.
                List<(uint Id, bool Inside)> monsters;
                lock (conn.Monsters)
                    monsters = [.. conn.Monsters.Select(kv => (kv.Key, !kv.Value.Dead && kv.Value.Life > 0
                        && state.Covers(FlaskGrounds.Kind.SmokeCloud, kv.Value.Walk.Position(now))))];
                foreach (uint gone in state.BlindedMonsters().Except(monsters.Select(m => m.Id)))
                    state.SetBlinded(gone, false, () => 0);
                foreach (var (id, inside) in monsters)
                    if (state.SetBlinded(id, inside, () => { lock (conn.Monsters) return conn.NextBuff++; }) is { } change)
                        await Send(remote, stream, ciphers, change.Added
                            ? InstanceEntities.AddBuff(id, change.Buff, FlaskGrounds.BlindedBuffRow, PlayerId)
                            : InstanceEntities.RemoveBuff(id, change.Buff), $"0x{(change.Added ? "01c1" : "01c2")} monster 0x{id:x} {(change.Added ? "blinded in the smoke cloud" : "no longer blinded")}");
                // Consecrated ground under the player.
                bool on = conn.PlayerLife > 0 && state.Covers(FlaskGrounds.Kind.ConsecratedGround, conn.Walk.Position(now));
                if (state.SetConsecrated(on, () => { lock (conn.Monsters) return conn.NextBuff++; }) is { } consecrated)
                {
                    await Send(remote, stream, ciphers, consecrated.Added
                        ? InstanceEntities.AddBuff(PlayerId, consecrated.Buff, FlaskGrounds.ConsecratedBuffRow, PlayerId)
                        : InstanceEntities.RemoveBuff(PlayerId, consecrated.Buff),
                        $"0x{(consecrated.Added ? "01c1" : "01c2")} player {(consecrated.Added ? "on" : "off")} consecrated ground");
                    await SendCharacterStats(remote, stream, ciphers, conn, $"consecrated ground {(consecrated.Added ? "on" : "off")}");
                }
                if (state.StopTickerIfIdle()) return;
            }
        }
        catch (Exception ex)
        {
            if (!conn.Closed) Console.WriteLine($"[gateway] {remote} flask grounds: {ServerLog.Describe(ex)}");
        }
        state.TickerExited();
    }
}
