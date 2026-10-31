using System.Runtime.CompilerServices;
using GameServer.Core.Protocol.Instance;
using GameServer.Modding;

namespace GameServer.Modules.Heist;

/// <summary>The hideout's Heist Locker.</summary>
public sealed class HeistModule : IModule
{
    const string LockerType = "Metadata/MiscellaneousObjects/Heist/HeistStash_Hideout";

    sealed class State { public uint Locker; }
    readonly ConditionalWeakTable<IPlayer, State> _states = new();

    /// <inheritdoc/>
    public void Load(IModuleContext module)
    {
        module.OnHideoutReady(async player =>
        {
            if (await module.World(player).SpawnHideoutStation(HideoutSlot.Small, LockerType, "heist-locker", "Heist Locker") is { } id)
                _states.GetOrCreateValue(player).Locker = id;
        });
        module.OnObjectUsed(async use =>
        {
            if (!_states.TryGetValue(use.Player, out var state) || state.Locker == 0 || use.ObjectId != state.Locker) return false;
            var world = module.World(use.Player);
            if (await world.WalkUpToStation(state.Locker, use.RequestedAt))
                await world.OpenWindow("heist", InstanceWaypoints.HeistLockerUi, "0x007b open native Heist Locker (HeistStashOpen)");
            return true;
        });
    }
}
