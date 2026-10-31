using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;
using static Program;

static class UniqueBeltsFeature
{
    /// <summary>Headhunter's kill: the rare's affixes as stacks (StolenMods), when the killer has its stat.</summary>
    internal static void StealMonsterMods(ClientState conn, SpawnedMonster m)
    {
        if (m.Rarity != MonsterRarity.Rare || !m.Mods.Affixes.Any() || conn.PlayerLife == 0) return;
        if (Stats.Key(StolenMods.SourceStat) is not uint key || CharacterPlayerStats(conn).GetValueOrDefault(key) <= 0) return;
        conn.StolenMods.Steal(m.Mods, ItemsFor(conn).Mods, conn.Clock.Elapsed.TotalSeconds);
    }

    /// <summary>The character's stat sources with the modifiers it stole (CharacterStatSources).</summary>
    internal static Dictionary<uint, int> WithStolenMods(ClientState conn, Dictionary<uint, int> sources)
    {
        var stolen = conn.StolenMods.Stats();
        if (stolen.Count == 0) return sources;
        var stats = new Dictionary<uint, int>(sources);
        foreach (var (stat, value) in stolen)
            if (Stats.Key(stat) is uint key) stats[key] = ActorScale.Saturate((long)stats.GetValueOrDefault(key) + value);
        return stats;
    }

    /// <summary>A rolled hit with Headhunter's "increased damage with hits against rare monsters". It joins the character's
    /// global increased damage; the hit's other increases (physical, spell …) are already in the roll and are not separated
    /// here, so with those it counts for a little more than the game's sum would.</summary>
    internal static DamagePacket WithDamageVsRarity(IReadOnlyDictionary<uint, int> player, SpawnedMonster m, DamagePacket rolled)
    {
        if (m.Rarity != MonsterRarity.Rare || Stats.Key("damage_+%_vs_rare_monsters") is not uint key
            || player.GetValueOrDefault(key) is not (> 0 and var against)) return rolled;
        int increased = Stats.Key("damage_+%") is uint general ? player.GetValueOrDefault(general) : 0;
        return rolled.Scale((100.0 + Math.Max(0, increased) + against) / (100 + Math.Max(0, increased)));
    }

    internal static async Task RunUniqueBeltEffects(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        const int StepMs = 250;
        while (!conn.Closed && !conn.RageRetired)
        {
            await Task.Delay(StepMs);
            if (conn.Closed || conn.RageRetired) break;
            if (conn.LeavingArea) continue;
            try
            {
                foreach (var packet in StepMagebloodBuffs(conn))
                    await Send(remote, stream, ciphers, packet, "0x01c1/0x01c2 Mageblood flask buff");
                if (conn.StolenMods.Step(conn.Clock.Elapsed.TotalSeconds, conn.PlayerLife == 0) is not var (stacks, remaining)) continue;
                ushort old, buff = 0;
                lock (conn.Monsters)
                {
                    RecomputePoolsAndSpeed(conn);
                    old = conn.StolenModsBuff;
                    conn.StolenModsBuff = stacks > 0 ? buff = conn.NextBuff++ : (ushort)0;
                }
                // As charges: a change of count or time is the old buff off and a new one on.
                if (old != 0) await Send(remote, stream, ciphers, InstanceEntities.RemoveBuff(PlayerId, old), "0x01c2 player buff: Headhunter");
                if (buff != 0)
                    await Send(remote, stream, ciphers, InstanceEntities.AddBuff(PlayerId, buff, StolenMods.BuffRow, PlayerId,
                            u68: BitConverter.SingleToUInt32Bits((float)remaining), stacks: (ushort)stacks),
                        $"0x01c1 player buff: Headhunter x {stacks}, {remaining:0.#} s");
                await SendCharacterStats(remote, stream, ciphers, conn, $"Headhunter: {stacks} modifier(s)");
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                if (!conn.Closed) Console.WriteLine($"[gateway] {remote} unique belts: {ex.Message}");
            }
        }
    }
}
