using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;
using GameServer.Modding;
using static Program;
using static PortalsFeature;

namespace GameServer.Modules.M_portals;

/// <summary>Town portals, Portal Scrolls, the map device and map portals.</summary>
public sealed class PortalsModule : IModule, IDisposable
{
    readonly List<IDisposable> _events = [];
    void On<T>(GameEvent<T> ev, T run) where T : Delegate => _events.Add(ev.Add("portals", run));

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var e in _events) e.Dispose();
    }

    /// <inheritdoc/>
    public void Load(IModuleContext module)
    {
        module.OnAreaGenerated(async player =>
        {
            var conn = ConnOf(player); var (remote, stream, ciphers) = conn.Wire;
            await SpawnHideoutDoodads(remote, stream, ciphers, conn);
            await SpawnMapReturnPortal(remote, stream, ciphers, conn);
            await SpawnTownPortals(remote, stream, ciphers, conn);
            // The portals of the character's open map stand at the device again.
            bool device;
            lock (conn.HideoutDoodads) device = conn.HideoutDoodads.Count > 0;
            if (device && MapDevice.OpenOf(CharacterName(conn)) is { PortalsLeft: > 0 } open) await SpawnMapPortals(remote, stream, ciphers, conn, open, firstAfterMs: MapPortalEntryMs);
        });
        On(GameEvents.ObjectUse, async (conn, use, requestedAt) =>
        {
            var (remote, stream, ciphers) = conn.Wire;
            if (conn.BackPortal is { } home && use.TargetId == home.Id && OpenPortals.TryGetValue(CharacterName(conn), out var origin))
            {
                if (!TryBeginObjectWalk(conn, requestedAt, home.X, home.Y, AreaTransitionObjectSize, out var homeWalk)) return true;
                var (homeAction, homeNow, _, _, homeArrive) = homeWalk;
                if (homeArrive > homeNow) await Task.Delay(TimeSpan.FromSeconds(homeArrive - homeNow));
                if (!PlayerActionCurrent(conn, homeAction)) return true;
                await ChangeArea(remote, stream, ciphers, conn, new PendingTransfer(origin.Area, origin.Instance, false, ArriveAt: (origin.X, origin.Y)),
                    "back through the town portal");
                    return true;
            }
            if (conn.ReturnPortal is { } back && use.TargetId == back.Id)
            {
                await UseMapReturnPortal(remote, stream, ciphers, conn, use, (back.X, back.Y), requestedAt);
                return true;
            }
            (int X, int Y, int Index)? portal;
            lock (conn.MapPortals) portal = conn.MapPortals.TryGetValue(use.TargetId, out var at) ? at : null;
            if (portal is { } p)
            {
                await UseMapPortal(remote, stream, ciphers, conn, use, p, requestedAt);
                return true;
            }
            HideoutCatalog.Doodad? doodad;
            lock (conn.HideoutDoodads) doodad = conn.HideoutDoodads.GetValueOrDefault(use.TargetId);
            if (doodad is null) return false;
            await UseMapDevice(remote, stream, ciphers, conn, use, doodad, requestedAt);
            return true;
        });
        // The Portal skill: a town portal, as the Portal Scroll's, once its cast (2.5 s) is over.
        On(GameEvents.SkillCast, (conn, effect, skillId, from, current) =>
        {
            if (effect != "Portal") return null;
            var (remote, stream, ciphers) = conn.Wire;
            if (!PortalAllowed(conn)) { Console.WriteLine($"[gateway] {remote} skill 0x{skillId:x4} Portal: not here"); return false; }
            var gem = GemSkillEffects(conn).FirstOrDefault(g => g.Id == skillId);
            if (CastMs(conn, gem.Effect) is not int castMs) return false;
            _ = Task.Delay(castMs).ContinueWith(async _ =>
            {
                if (current()) await OpenTownPortal(remote, stream, ciphers, conn, from.X, from.Y, "Portal skill");
            });
            return true;
        });
        On(GameEvents.ItemUse, async (conn, use, item, type) =>
        {
            if (type.Id != PortalScroll) return false;
            var (remote, stream, ciphers) = conn.Wire;
            await UsePortalScroll(remote, stream, ciphers, conn, use, item, type);
            return true;
        });
        module.OnPacket(InstanceMapDevice.ActivateId, async p =>
        {
            var conn = ConnOf(p.Player); var (remote, stream, ciphers) = conn.Wire; byte[] bytes = p.Bytes.ToArray();
            await ActivateMapDevice(remote, stream, ciphers, conn, InstanceMapDevice.ReadActivate(bytes));
            return true;
        });
        module.OnPacket(InstanceMapDevice.SelectMapId, async p =>
        {
            var conn = ConnOf(p.Player); var (remote, stream, ciphers) = conn.Wire; byte[] bytes = p.Bytes.ToArray();
            await SelectAtlasMap(remote, stream, ciphers, conn, InstanceMapDevice.ReadSelectMap(bytes));
            return true;
        });
    }
}
