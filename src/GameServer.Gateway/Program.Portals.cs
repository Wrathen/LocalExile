using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    // The open map's portals that are left, at their places on a ring around the hideout's map device, one after another
    // (MapPortalGapMs apart). 0x8f only reaches portal objects the client has already created, and a create sent in the same
    // burst is not done yet (live 2026-10-20), so each portal is listed MapPortalListMs after its create; on entering the
    // area the objects also wait until the world is active (firstAfterMs). A newer Activate or the portals' removal stops
    // the sequence.
    internal static Task SpawnMapPortals(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, MapDevice.OpenMap open, int firstAfterMs = 0)
    {
        HideoutCatalog.Doodad? device;
        uint deviceId;
        lock (conn.HideoutDoodads) (deviceId, device) = conn.HideoutDoodads.Select(kv => (kv.Key, (HideoutCatalog.Doodad?)kv.Value)).FirstOrDefault();
        if (device is null) return Task.CompletedTask;
        int generation = Interlocked.Increment(ref conn.MapPortalGeneration);
        bool Current() => !conn.Closed && conn.MapPortalGeneration == generation;
        uint type = InstanceEntities.ObjectTypeHash(MapPortalObject.MetadataPath);
        _ = Task.Run(async () =>
        {
            if (firstAfterMs > 0) await Task.Delay(firstAfterMs);
            int n = 0;
            foreach (int index in open.Left)
            {
                if (!Current()) return;
                if (n++ > 0) await Task.Delay(MapPortalGapMs);
                if (!Current()) return;
                uint id = conn.NextObjectId++;
                var (x, y) = MapDevice.PortalCell(device.X, device.Y, index);
                await Send(remote, stream, ciphers, InstanceEntities.CreateObject(id, type,
                        new MapPortalObject(x, y, index, MapPortalType, Open: 1, Rotation: conn.PortalRotationRadians).Blob()),
                    $"0x02b9 create map portal 0x{id:x} (place {index}, {n}/{open.PortalsLeft}) to {open.Area.Name} at ({x}, {y})");
                await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(id), null);
                lock (conn.MapPortals) conn.MapPortals[id] = (x, y, index);
                await Task.Delay(MapPortalListMs);
                if (!Current()) return;
                await SendMapPortals(remote, stream, ciphers, conn, deviceId, open.Area.Hash16);
            }
        });
        return Task.CompletedTask;
    }

    internal static void SendMapPortalsLater(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, uint deviceId, ushort area, int delayMs) =>
        _ = Task.Delay(delayMs).ContinueWith(async _ => { if (!conn.Closed) await SendMapPortals(remote, stream, ciphers, conn, deviceId, area); });

    // Removes and recreates the map portals that stand, at the same places and with the current rotation, then lists them
    // again: the client builds a portal's visual from its object, so a rotated portal effect can be tried without reopening
    // the device. Returns how many were rebuilt.
    internal static async Task<int> RebuildMapPortals(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        uint deviceId;
        lock (conn.HideoutDoodads) deviceId = conn.HideoutDoodads.Keys.FirstOrDefault();
        List<(uint Id, int X, int Y, int Index)> old;
        lock (conn.MapPortals)
        {
            old = [.. conn.MapPortals.Select(kv => (kv.Key, kv.Value.X, kv.Value.Y, kv.Value.Index))];
            conn.MapPortals.Clear();
        }
        if (old.Count == 0) return 0;
        uint type = InstanceEntities.ObjectTypeHash(MapPortalObject.MetadataPath);
        foreach (var (id, _, _, _) in old)
            await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(id), $"0x02bb remove map portal 0x{id:x} (rebuild)");
        // Each portal is listed a moment after its create: 0x008f in the same burst as the 0x2b9 reaches none of them (the
        // client has not built the object yet), which is why they then stay invisible.
        ushort area = MapDevice.OpenOf(CharacterName(conn))?.Area.Hash16 ?? conn.Layout.Hash;
        foreach (var (_, x, y, index) in old)
        {
            uint id = conn.NextObjectId++;
            await Send(remote, stream, ciphers, InstanceEntities.CreateObject(id, type,
                    new MapPortalObject(x, y, index, MapPortalType, Open: 1, Rotation: conn.PortalRotationRadians).Blob()),
                $"0x02b9 create map portal 0x{id:x} (place {index}) at ({x}, {y}) rotation {conn.PortalRotationDegrees:0.#}°");
            await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(id), null);
            lock (conn.MapPortals) conn.MapPortals[id] = (x, y, index);
            await Task.Delay(MapPortalListMs);
            await SendMapPortals(remote, stream, ciphers, conn, deviceId, area);
        }
        return old.Count;
    }

    // Lists the portals already standing again, so a portal cosmetic takes effect without reopening the map device: 0x008f
    // carries the variation of every entry and the client re-applies the visual. False when nothing stands.
    internal static async Task<bool> ResendMapPortals(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        uint deviceId;
        lock (conn.HideoutDoodads) deviceId = conn.HideoutDoodads.Keys.FirstOrDefault();
        lock (conn.MapPortals)
            if (conn.MapPortals.Count == 0) return false;
        await SendMapPortals(remote, stream, ciphers, conn, deviceId,
            MapDevice.OpenOf(CharacterName(conn))?.Area.Hash16 ?? conn.Layout.Hash);
        return true;
    }

    // S→C 0x8f: the device's portals and where they lead (21-hideouts.md §5). Without it the client never draws them; a
    // portal listed with area 0 (one just taken) closes.
    internal static async Task SendMapPortals(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, uint deviceId, ushort area, uint closed = 0)
    {
        byte style = conn.MapPortalStyle;
        List<uint> ids;
        lock (conn.MapPortals) ids = [.. conn.MapPortals.Keys.Order()];
        string owner = CharacterName(conn);
        var list = (closed == 0 ? [] : new List<InstanceMapDevice.Portal> { new(closed, 0) })
            .Concat(ids.Select(id => new InstanceMapDevice.Portal(id, area, owner, style, conn.CosmeticPortalVariation))).ToList();
        await Send(remote, stream, ciphers, InstanceMapDevice.Portals(deviceId, list),
            $"0x008f map device 0x{deviceId:x} portals: {ids.Count} open to 0x{area:x4}{(closed == 0 ? "" : $", 0x{closed:x} closed")}");
    }

    // Created closed (hidden), a map device portal is drawn and targetable once it is opened (Transitionable 0 → 1). Sent in
    // the same burst as its creation, the update hung the client (live 2026-10-20), so it goes once the portal is built.
    internal static void OpenMapPortalLater(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, uint id, int delayMs) =>
        _ = Task.Delay(delayMs).ContinueWith(async _ =>
        {
            bool still;
            lock (conn.MapPortals) still = conn.MapPortals.ContainsKey(id);
            if (still && !conn.Closed) await Send(remote, stream, ciphers, MapPortalObject.OpenUpdate(id), $"0x02ba map portal 0x{id:x} opens");
        });

    internal static async Task RemoveMapPortals(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        List<uint> ids;
        Interlocked.Increment(ref conn.MapPortalGeneration);   // stops a sequence still placing them
        lock (conn.MapPortals) { ids = [.. conn.MapPortals.Keys]; conn.MapPortals.Clear(); }
        uint deviceId;
        lock (conn.HideoutDoodads) deviceId = conn.HideoutDoodads.Keys.FirstOrDefault();
        if (ids.Count > 0 && deviceId != 0)
            await Send(remote, stream, ciphers, InstanceMapDevice.Portals(deviceId, [.. ids.Select(id => new InstanceMapDevice.Portal(id, 0))]),
                $"0x008f map device 0x{deviceId:x}: {ids.Count} portal(s) closed");
        foreach (uint id in ids) await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(id), $"0x{InstanceProtocol.RemoveObjectId:x4} remove map portal 0x{id:x}");
    }

}
