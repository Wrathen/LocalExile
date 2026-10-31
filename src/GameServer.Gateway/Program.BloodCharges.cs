using System.Collections.Concurrent;
using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    // Reap's Blood Charges (BloodCharges): held on the connection (ClientState.BloodCharges, under the Monsters lock),
    // settled by the skill's area sweep (AreaSkillHit), shown by BuffDefinitions 1822 and the Charges tab rows.

    // Whether a GrantedEffects row uses Blood Charges (its per-charge damage stat, a constant at every level), decided
    // once per row: CharacterPlayerStats asks on every hit, and resolving a stat set each time would cost a crowd's hits.
    internal static readonly ConcurrentDictionary<(bool HardMode, int Effect), bool> BloodChargeEffects = new();

    internal static bool UsesBloodCharges(ClientState conn, int effect) => BloodChargeEffects.GetOrAdd((LeagueFor(conn).HardMode, effect),
        key => BloodCharges.Uses(name => Stats.Key(name) is uint k
            ? SkillsFor(conn).EffectStats(key.Effect, 1).Where(s => s.Item1 == k).Select(s => s.Item2).FirstOrDefault() : 0));

    // The Charges tab's rows: the count, the maximum (Character.ot's 5 plus the raw maximum_blood_scythe_charges sources,
    // the Reap enchantment) and show_blood_charge_info while a gem socketed in an active item uses the charges. get reads
    // the sources before this publication, so the base adds once.
    internal static List<(string Stat, int Value)> BloodChargePanelRows(ClientState conn, Func<string, int> get)
    {
        int held;
        lock (conn.Monsters) held = conn.BloodCharges;
        var inactive = InactiveItems.GetValueOrDefault(CharacterName(conn));
        bool shown = ItemsOf(conn).SocketedGemsByItem().Any(g => inactive?.Contains(g.ItemId) != true
            && ItemsFor(conn).GemEffects(g.Gem).Any(effect => UsesBloodCharges(conn, effect)));
        return
        [
            (BloodCharges.Current, held),
            (BloodCharges.Maximum, (int)Math.Clamp((long)get(BloodCharges.Maximum) + BloodCharges.BaseMaximum, 0, int.MaxValue)),
            (BloodCharges.Show, shown ? 1 : 0),
        ];
    }

    // The maximum as the character's final stats publish it (BloodChargePanelRows).
    internal static int BloodChargeMaximum(IReadOnlyDictionary<uint, int> player) =>
        Stats.Key(BloodCharges.Maximum) is uint key ? Math.Max(0, player.GetValueOrDefault(key)) : BloodCharges.BaseMaximum;

    // The skill's more damage from the charges held now, read under the lock with the sweep's other inputs.
    internal static double BloodChargeDamageMore(ClientState conn, Func<string, int> own)
    {
        if (!BloodCharges.Uses(own)) return 1;
        lock (conn.Monsters) return BloodCharges.More(own(BloodCharges.DamagePerCharge), conn.BloodCharges);
    }

    // A use of a Blood Charge skill landed: kills take a charge each, a survivor gives one. True when the count changed.
    internal static bool SettleBloodChargesLocked(ClientState conn, IReadOnlyDictionary<uint, int> player, int kills, int survivors)
    {
        int before = conn.BloodCharges;
        conn.BloodCharges = BloodCharges.Settle(before, kills, survivors, BloodChargeMaximum(player));
        return conn.BloodCharges != before;
    }

    // Death takes the charges (BuffDefinitions CancelOnDeath; the client drops the buff itself).
    internal static void ClearBloodChargesOnDeath(ClientState conn)
    {
        conn.BloodCharges = 0;
        conn.BloodChargeBuff = 0;
        conn.SentBloodCharges = 0;
    }

    // The buff with the new count (0x1c2 for the old instance, 0x1c1 with the stacks) and the stats and skills that read
    // the count (SendChargeStats: the panel rows and the skill's damage).
    internal static async Task SendBloodCharges(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, string why)
    {
        int held; ushort remove, add;
        lock (conn.Monsters)
        {
            held = conn.BloodCharges;
            if (held == conn.SentBloodCharges) return;
            remove = conn.BloodChargeBuff;
            add = held > 0 ? conn.NextBuff++ : (ushort)0;
            conn.BloodChargeBuff = add;
            conn.SentBloodCharges = held;
        }
        if (remove != 0)
            await Send(remote, stream, ciphers, InstanceEntities.RemoveBuff(PlayerId, remove), $"0x01c2 player buff {remove}: blood_scythe_charge off");
        if (add != 0)
            await Send(remote, stream, ciphers, InstanceEntities.AddBuff(PlayerId, add, BloodCharges.BuffRow, PlayerId,
                stacks: (ushort)Math.Clamp(held, 0, ushort.MaxValue)), $"0x01c1 player buff {add}: blood_scythe_charge (BuffDefinitions row {BloodCharges.BuffRow}) × {held}");
        Console.WriteLine($"[gateway] {remote} blood charges {held} ({why}) (t={conn.Clock.Elapsed.TotalSeconds:0.000})");
        await SendChargeStats(remote, stream, ciphers, conn, $"blood charges {held}");
    }
}
