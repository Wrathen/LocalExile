using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;

// What Headhunter (StolenMods) and Mageblood show and do over time: one ticker per connection in an area. It starts the
// buffs of new kills and ends those that ran out, and keeps Mageblood's permanent flask buffs in step with the belt and the
// flask bar, whichever way those changed (a move, a swap, a level gained, entering an area).
partial class Program
{
    /// <summary>Mageblood's flask buffs as they should be now: the packets that end those no longer applied (moved, off the
    /// bar, the belt off, death) and start those missing, with no timer: the effect is permanent.</summary>
    internal static List<byte[]> StepMagebloodBuffs(ClientState conn)
    {
        var items = ItemsFor(conn);
        var wanted = new Dictionary<uint, (byte Slot, UtilityFlasks.Utility Utility)>();
        if (conn.PlayerLife > 0 && MagebloodBelt(conn) is { } belt)
            foreach (var flask in MagebloodFlasks(ItemsOf(conn).Items(PlayerInventories.Flask), MagebloodFlaskCount(belt, items), items))
                if (items.ByHash(flask.Item.TypeHash) is { } type && items.UtilityFlaskOf(type) is { } utility)
                    wanted[flask.Id] = ((byte)flask.X, utility);
        List<byte[]> packets = [];
        lock (conn.Monsters)
        {
            foreach (var (id, shown) in conn.MagebloodBuffs.ToList())
            {
                if (wanted.TryGetValue(id, out var still) && still.Slot == shown.Slot) continue;
                packets.Add(InstanceEntities.RemoveBuff(PlayerId, shown.Buff));
                if (shown.Extra is ushort extra) packets.Add(InstanceEntities.RemoveBuff(PlayerId, extra));
                conn.MagebloodBuffs.Remove(id);
            }
            foreach (var (id, (slot, utility)) in wanted)
            {
                if (conn.MagebloodBuffs.ContainsKey(id)) continue;
                ushort buff = conn.NextBuff++;
                ushort? extra = utility.Extra is null ? null : conn.NextBuff++;
                conn.MagebloodBuffs[id] = (slot, buff, extra);
                packets.Add(Flasks.Buff(PlayerId, buff, utility.BuffRow, slot, seconds: null));
                if (utility.Extra is { } x && extra is ushort second) packets.Add(InstanceEntities.AddBuff(PlayerId, second, x.BuffRow, PlayerId));
            }
        }
        return packets;
    }

}
