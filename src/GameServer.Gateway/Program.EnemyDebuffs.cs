using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

/// <summary>What the player's debuffs on monsters need from his stats: taunted_enemies_damage_taken_+% (Bring the Battle),
/// as last sent to the client (Program.PlayerStatsWithCleared). His passives' enemy auras come from the allocated nodes
/// at each step (PassiveBuffAuras.EnemyAurasFor).</summary>
sealed record EnemyDebuffs(int TauntedDamageTakenPct)
{
    public static readonly EnemyDebuffs None = new(0);
}

partial class Program
{
    // The taunted buff (BuffDefinitions 408) is IsRecovery (+0x7c), so 0x1c1 carries one u32 per GrantedStats entry
    // (taunt_target_id, is_taunted; reader 0x141fdf870): the taunter and 1.
    static readonly IReadOnlyList<uint> TauntedBuffValues = [PlayerId, 1];

    /// <summary>How long the player's taunts last: 3 s (wiki Taunt; the Basalt Flask's reminder text), raised by
    /// taunt_duration_+%.</summary>
    internal static double TauntSeconds(Func<string, int> get) =>
        FlaskGrounds.TauntSeconds * Math.Max(0, 1 + get("taunt_duration_+%") / 100.0);

    /// <summary>Taunts the hostile living monsters within <paramref name="radius"/> cells of <paramref name="at"/> for
    /// <paramref name="seconds"/> (a warcry, a Basalt Flask); a new taunt replaces the player's old one on a monster (wiki
    /// Taunt: "you can replace your own taunts"). The enemy-debuff step shows it and takes it off. Under the Monsters lock;
    /// the number taunted.</summary>
    internal static int TauntLocked(ClientState conn, (int X, int Y) at, int radius, double now, double seconds)
    {
        if (conn.PlayerLife == 0 || seconds <= 0) return 0;
        int taunted = 0;
        foreach (var m in conn.Monsters.Values)
        {
            if (m.Dead || m.Life == 0 || !m.Hostile || Dist(m.Walk.Position(now), at) > radius) continue;
            m.TauntedUntil = now + seconds;
            m.TakenFromPlayer = m.TakenFromPlayer with { All = conn.EnemyDebuffs.TauntedDamageTakenPct };
            taunted++;
        }
        return taunted;
    }

    // The player tick (RunPlayerTick, 250 ms, not while paused) steps the player's debuffs on monsters.
    internal static Task TickPassiveAuras(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn) =>
        EnemyDebuffsTick(remote, stream, ciphers, conn, conn.Clock.Elapsed.TotalSeconds);

    // One step of the player's debuffs on monsters, worked out at the send gate so a removed monster gets none: a taunt
    // shows the taunted buff (408) for its time left and goes when it ends, the monster dies or the player dies (wiki: a
    // taunt ends when its source dies); each enemy aura of his passives (Light of Divinity's Augury of Penitence, 2551)
    // and Born under Fire's) is on every hostile living monster within its radius of the player while he lives, and the
    // blind aura (SaboteurBlind) blinds them, shown with the blinded buff (PassiveBuffAuras.BlindState, Program.Blinded).
    // Each monster then keeps the increased damage taken they give (SpawnedMonster.TakenFromPlayer), which its hits and
    // burns read. A refund or a death takes the auras off; a new area is a new connection.
    internal static async Task EnemyDebuffsTick(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, double now)
    {
        ushort[] nodes;
        lock (conn.Monsters) nodes = [.. conn.Passives, .. conn.MainAscendancy.Allocated];
        // Outside the lock: the first call loads the aura table from PassiveSkills.
        var all = PassiveBuffAuras.EnemyAurasFor(nodes, conn.Ruthless);
        var auras = all.Where(a => a.Template != PassiveBuffAuras.BlindTemplate).ToList();
        int? blindRadius = PassiveBuffAuras.BlindRadius(all);
        await Send(remote, stream, ciphers, [], null, atGate: () =>
        {
            lock (conn.Monsters)
            {
                if (conn.Closed || conn.LeavingArea) return null;
                var config = conn.EnemyDebuffs;
                bool alive = conn.PlayerLife > 0;
                var here = conn.Walk.Position(now);
                uint? elementalKey = Stats.Key("elemental_damage_taken_+%");
                var bytes = new List<byte>();
                int added = 0, removed = 0;
                foreach (var (id, m) in conn.Monsters)
                {
                    bool living = alive && !m.Dead && m.Life > 0 && m.Hostile;
                    bool taunted = living && m.TauntedUntil > now;
                    if (!taunted) m.TauntedUntil = double.NegativeInfinity;
                    if (m.TauntBuff != 0 && (!taunted || m.TauntShownUntil != m.TauntedUntil))
                    {
                        bytes.AddRange(InstanceEntities.RemoveBuff(id, m.TauntBuff));
                        m.TauntBuff = 0;
                        removed++;
                    }
                    if (taunted && m.TauntBuff == 0)
                    {
                        m.TauntShownUntil = m.TauntedUntil;
                        bytes.AddRange(InstanceEntities.AddBuff(id, m.TauntBuff = conn.NextBuff++, FlaskGrounds.TauntedBuffRow, PlayerId,
                            values: TauntedBuffValues, u68: BitConverter.SingleToUInt32Bits((float)(m.TauntedUntil - now))));
                        added++;
                    }
                    double distance = living && (auras.Count > 0 || m.PlayerAuras.Count > 0)
                        ? Dist(m.Walk.Position(now), here) : double.PositiveInfinity;
                    if (m.PlayerAuras.Count > 0)
                        foreach (var (definition, buff) in m.PlayerAuras.ToArray())
                            if (auras.FirstOrDefault(a => a.Definition == definition) is not { } kept || distance > kept.Radius)
                            {
                                bytes.AddRange(InstanceEntities.RemoveBuff(id, buff));
                                m.PlayerAuras.Remove(definition);
                                removed++;
                            }
                    int elemental = 0;
                    foreach (var aura in auras)
                    {
                        if (distance > aura.Radius) continue;
                        if (!m.PlayerAuras.ContainsKey(aura.Definition))
                        {
                            bytes.AddRange(InstanceEntities.AddBuff(id, m.PlayerAuras[aura.Definition] = conn.NextBuff++, aura.Definition, PlayerId));
                            added++;
                        }
                        elemental += aura.Stats.Where(s => s.Key == elementalKey).Sum(s => s.Value);
                    }
                    m.TakenFromPlayer = (taunted ? config.TauntedDamageTakenPct : 0, elemental);
                }
                int? radius = conn.PlayerLife > 0 ? blindRadius : null;
                HashSet<uint> inside = radius is int r
                    ? conn.Monsters.Where(kv => kv.Value is { Hostile: true, Dead: false, Life: > 0 } && Dist(kv.Value.Walk.Position(now), here) <= r)
                        .Select(kv => kv.Key).ToHashSet()
                    : [];
                foreach (var (id, buff, blinded) in conn.BlindAura.Reconcile(inside, conn.Monsters.ContainsKey, () => conn.NextBuff++))
                {
                    bytes.AddRange(blinded ? InstanceEntities.AddBuff(id, buff, FlaskGrounds.BlindedBuffRow, PlayerId) : InstanceEntities.RemoveBuff(id, buff));
                    if (blinded) added++; else removed++;
                }
                if (bytes.Count == 0) return null;
                Console.WriteLine($"[gateway] {remote} enemy debuffs: {added} added, {removed} removed (t={now:0.000})");
                return [.. bytes];
            }
        });
    }
}
