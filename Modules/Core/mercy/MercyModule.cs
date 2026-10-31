using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;
using GameServer.Modding;
using static Program;
using static MercyFeature;

namespace GameServer.Modules.M_mercy;

/// <summary>A1's quest Mercy Mission: the Medicine Chest from Hailrake to Nessa.</summary>
public sealed class MercyModule : IModule, IDisposable
{
    readonly List<IDisposable> _events = [];
    void On<T>(GameEvent<T> ev, T run) where T : Delegate => _events.Add(ev.Add("mercy", run));

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var e in _events) e.Dispose();
    }

    /// <inheritdoc/>
    public void Load(IModuleContext module)
    {
        On(GameEvents.Kill, kill =>
        {
            var (remote, stream, ciphers) = kill.Conn.Wire;
            kill.Later(() => DropQuestItems(remote, stream, ciphers, kill.Conn, kill.VarietyId, kill.At));
        });
        On(GameEvents.GroundItemUse, (conn, id, ground, use, requestedAt) =>
        {
            var (remote, stream, ciphers) = conn.Wire;
            return PickUpQuestItem(remote, stream, ciphers, conn, id, ground, use, requestedAt);
        });
        On(GameEvents.NpcOptionPicked, (conn, talk) =>
        {
            var (remote, stream, ciphers) = conn.Wire;
            return HandInQuestItems(remote, stream, ciphers, conn, talk);
        });
        On(GameEvents.MessagesHandled, async (conn, ids) =>
        {
            var (remote, stream, ciphers) = conn.Wire;
            if (conn.PlayerSent && ids.Any(MercyMission.MovesItems)) await SyncQuestItems(remote, stream, ciphers, conn, removeItems: true);
        });
        module.OnAreaGenerated(async player =>
        {
            var conn = ConnOf(player); var (remote, stream, ciphers) = conn.Wire;
            await SyncQuestItems(remote, stream, ciphers, conn, removeItems: true);   // the Medicine Chest's flag
            _ = RunMercyMission(remote, stream, ciphers, conn);
        });
    }
}
