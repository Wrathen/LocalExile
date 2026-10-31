using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;
using GameServer.Modding;
using static Program;
using static HideoutEditingFeature;

namespace GameServer.Modules.M_hideoutediting;

/// <summary>Moving things in the hideout's editor.</summary>
public sealed class HideoutEditingModule : IModule, IDisposable
{
    readonly List<IDisposable> _events = [];
    void On<T>(GameEvent<T> ev, T run) where T : Delegate => _events.Add(ev.Add("hideoutediting", run));

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var e in _events) e.Dispose();
    }

    /// <inheritdoc/>
    public void Load(IModuleContext module)
    {
        module.OnPacket(InstanceHideoutEditing.MoveId, async p =>
        {
            var conn = ConnOf(p.Player); var (remote, stream, ciphers) = conn.Wire; byte[] bytes = p.Bytes.ToArray();
            await MoveHideoutObject(remote, stream, ciphers, conn, InstanceHideoutEditing.ReadMove(bytes));
            return true;
        });
    }
}
