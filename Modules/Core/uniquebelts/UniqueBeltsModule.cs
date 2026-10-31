using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;
using GameServer.Modding;
using static Program;
using static UniqueBeltsFeature;

namespace GameServer.Modules.M_uniquebelts;

/// <summary>Headhunter's stolen modifiers, Mageblood's flasks, damage against rarity.</summary>
public sealed class UniqueBeltsModule : IModule, IDisposable
{
    readonly List<IDisposable> _events = [];
    void On<T>(GameEvent<T> ev, T run) where T : Delegate => _events.Add(ev.Add("uniquebelts", run));

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var e in _events) e.Dispose();
    }

    /// <inheritdoc/>
    public void Load(IModuleContext module)
    {
        On(GameEvents.Kill, kill => StealMonsterMods(kill.Conn, kill.Monster));
        On(GameEvents.PlayerStatSources, WithStolenMods);
        On(GameEvents.OutgoingDamage, (_, player, monster, rolled) => WithDamageVsRarity(player, monster, rolled));
        module.OnAreaGenerated(player =>
        {
            var conn = ConnOf(player); var (remote, stream, ciphers) = conn.Wire;
            _ = RunUniqueBeltEffects(remote, stream, ciphers, conn);
            return Task.CompletedTask;
        });
    }
}
