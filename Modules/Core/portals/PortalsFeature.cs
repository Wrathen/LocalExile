using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;
using static Program;

static class PortalsFeature
{
    // In a map's area (one a map item opens): a portal back to the hideout next to the arrival point, listed in 0x8f without
    // a device once the world is active (21-hideouts.md §5). It is not used up.
    internal static async Task SpawnMapReturnPortal(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        if (!MapDevices.Value.IsMapArea(conn.Layout.Hash) || HomeHideout(conn) is not { } hideout) return;
        var (x, y) = conn.Layout.Near((conn.Layout.SpawnX, conn.Layout.SpawnY));
        uint id = conn.NextObjectId++;
        await Send(remote, stream, ciphers, InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(MapPortalObject.MetadataPath),
            new MapPortalObject(x, y, -1, MapPortalType, Open: 1, Rotation: conn.PortalRotationRadians).Blob()),
            $"0x02b9 create the portal back to the {hideout.Name} 0x{id:x} at ({x}, {y})");
        await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(id), null);
        conn.ReturnPortal = (id, x, y);
        conn.ReturnPortalTo = 0;
        string owner = CharacterName(conn);
        _ = Task.Delay(MapPortalEntryMs).ContinueWith(async _ =>
        {
            if (!conn.Closed)
                await Send(remote, stream, ciphers, InstanceMapDevice.Portals(0,
                    [new(id, hideout.AreaHash16, owner, Variation: conn.CosmeticPortalVariation)]),
                    $"0x008f the portal 0x{id:x} leads to the {hideout.Name} (0x{hideout.AreaHash16:x4})");
        });
    }

    // A click on a map portal (C→S 0x24): walk there, then the portal is used up and the player goes into the open map, a new
    // instance for the first portal and that same instance for the others.
    internal static async Task UseMapPortal(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        InstanceTransfer.UseSkillOnObject use, (int X, int Y, int Index) at, double requestedAt)
    {
        if (!TryBeginObjectWalk(conn, requestedAt, at.X, at.Y, AreaTransitionObjectSize, out var objectWalk)) return;
        var (action, now, here, stop, arrive) = objectWalk;
        Console.WriteLine($"[gateway] {remote} <= 0x0024 map portal 0x{use.TargetId:x} from ({here.X}, {here.Y}): reached at ({stop.X}, {stop.Y}) in {arrive - now:0.00} s");
        if (arrive > now) await Task.Delay(TimeSpan.FromSeconds(arrive - now));
        if (!PlayerActionCurrent(conn, action) || conn.PendingTransfer is not null) return;
        lock (conn.MapPortals) if (!conn.MapPortals.Remove(use.TargetId)) return;
        string character = CharacterName(conn);
        uint deviceId;
        lock (conn.HideoutDoodads) deviceId = conn.HideoutDoodads.Keys.FirstOrDefault();
        // The portal at this place is used up: it is not there when the player comes back (the others are).
        if (MapDevice.TakePortal(character, at.Index) is not var (map, left))
        {
            Console.WriteLine($"[gateway] {remote} map portal 0x{use.TargetId:x}: no open map");
            await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(use.TargetId), $"0x{InstanceProtocol.RemoveObjectId:x4} remove map portal 0x{use.TargetId:x}");
            return;
        }
        if (deviceId != 0) await SendMapPortals(remote, stream, ciphers, conn, deviceId, map.Area.Hash16, closed: use.TargetId);
        await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(use.TargetId), $"0x{InstanceProtocol.RemoveObjectId:x4} remove map portal 0x{use.TargetId:x} (used)");
        Console.WriteLine($"[gateway] {remote} map portal at place {at.Index} taken: {left} left, into {(map.Instance == 0 ? "a new instance" : $"instance {map.Instance}")} of {map.Area.Name}");
        await ChangeArea(remote, stream, ciphers, conn, new PendingTransfer(map.Area.Hash16, map.Instance, map.Instance == 0)
        {
            Done = r =>
            {
                if (r.Ok) MapDevice.Entered(character, map.Area, r.Instance);
                else MapDevice.Close(character);
            },
        }, $"map portal to {map.Area.Name}");
    }

    // C→S 0x00d8, the map device window's Activate (21-hideouts.md §5): every slot must hold a map or a map fragment, and
    // exactly one map. Then the slots are consumed, the map's area opens (the first portal taken makes its instance) and its
    // portals appear around the device, replacing any open before. Otherwise nothing changes and the chat says why.
    internal static async Task ActivateMapDevice(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, (ushort Target, byte Index) activate)
    {
        HideoutCatalog.Doodad? device;
        lock (conn.HideoutDoodads) device = conn.HideoutDoodads.Values.FirstOrDefault();
        var items = ItemsOf(conn);
        var slots = items.Items(PlayerInventories.MapDevice);
        Console.WriteLine($"[gateway] {remote} <= 0x00d8 activate the map device (target 0x{activate.Target:x4}, index {activate.Index}): " +
            $"{string.Join(", ", slots.Select(i => Items.ByHash(i.Item.TypeHash)?.Name ?? $"0x{i.Item.TypeHash:x8}").DefaultIfEmpty("empty"))}");
        Task Tell(string text) => Send(remote, stream, ciphers, InstanceChat.ChatLine(text), $"0x0017 chat line \"{text}\"");
        if (device is null) { await Tell("There is no Map Device here."); return; }
        var area = MapDevices.Value.Check([.. slots.Select(i => Items.ByHash(i.Item.TypeHash) is { } t
            ? new MapDevice.Slot(i.Item.TypeHash, t.Class, t.Name) : new MapDevice.Slot(i.Item.TypeHash, "", $"Item 0x{i.Item.TypeHash:x8}"))], CharacterName(conn), out string why);
        if (area is null)
        {
            Console.WriteLine($"[gateway] {remote} map device not activated: {why}");
            await Tell(why);
            return;
        }
        // Every slot's item is consumed in one transaction: all of them or none.
        var consumed = Saves.Atomically(() => slots.Where(i => items.Remove(PlayerInventories.MapDevice, i.Id) is not null).Select(i => i.Id).ToList());
        await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.MapDevice),
            items.NextRevision(PlayerInventories.MapDevice), consumed, []),
            $"0x00bf inventory {PlayerInventories.InventoryId(PlayerInventories.MapDevice)}: the map device consumed {consumed.Count} item(s)");
        await RemoveMapPortals(remote, stream, ciphers, conn);
        var open = MapDevice.Activate(CharacterName(conn), area);
        Console.WriteLine($"[gateway] {remote} map device opened {area.Name} ({area.Id}, 0x{area.Hash16:x4}, tier {area.Tier}): {open.PortalsLeft} portals");
        await SpawnMapPortals(remote, stream, ciphers, conn, open);
    }

    // A click on the portal back (C→S 0x24, the map's own or a Portal Scroll's): walk there, then to the hideout next to its
    // waypoint, or to the town the scroll's portal leads to (ReturnPortalTo), at its spawn.
    internal static async Task UseMapReturnPortal(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        InstanceTransfer.UseSkillOnObject use, (int X, int Y) at, double requestedAt)
    {
        if (!TryBeginObjectWalk(conn, requestedAt, at.X, at.Y, AreaTransitionObjectSize, out var objectWalk)) return;
        var (action, now, here, stop, arrive) = objectWalk;
        Console.WriteLine($"[gateway] {remote} <= 0x0024 portal back 0x{use.TargetId:x} from ({here.X}, {here.Y}): reached at ({stop.X}, {stop.Y}) in {arrive - now:0.00} s");
        if (arrive > now) await Task.Delay(TimeSpan.FromSeconds(arrive - now));
        if (!PlayerActionCurrent(conn, action)) return;
        if (conn.ReturnPortalTo is ushort town and not 0)
            await ChangeArea(remote, stream, ciphers, conn, new PendingTransfer(town, 0, false, AtSpawn: true), "portal to town");
        else
            await WarpToHideout(remote, stream, ciphers, conn, "map portal back");
    }

    // A click on the map device (C→S 0x24): walk there, then S→C 0x7b kind 5 opens the map device window (21-hideouts.md §4).
    // Its slots and the activation are not served yet.
    internal static async Task UseMapDevice(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        InstanceTransfer.UseSkillOnObject use, HideoutCatalog.Doodad device, double requestedAt)
    {
        if (!TryBeginObjectWalk(conn, requestedAt, device.X, device.Y, StashReach, out var objectWalk, centreRange: true)) return;
        var (action, now, here, stop, arrive) = objectWalk;
        Console.WriteLine($"[gateway] {remote} <= 0x0024 {device.Name} ({use}) from ({here.X}, {here.Y}): reached at ({stop.X}, {stop.Y}) in {arrive - now:0.00} s");
        if (arrive > now) await Task.Delay(TimeSpan.FromSeconds(arrive - now));
        if (!PlayerActionCurrent(conn, action)) return;
        await Send(remote, stream, ciphers, InstanceWaypoints.OpenUi(InstanceWaypoints.HideoutMapDeviceUi, use.TargetId),
            "0x007b open the map device window", still: () => PlayerActionCurrent(conn, action));
    }

    // The character's open town portal (OpenPortals) on entering an area: in the area it stands in (the same instance), the
    // portal again at its cell; in the town or hideout it leads to, a portal back beside the arrival, which brings the player
    // to that cell (SpotArrivals).
    internal static async Task SpawnTownPortals(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        if (!OpenPortals.TryGetValue(CharacterName(conn), out var open)) return;
        if (open.Area == conn.Layout.Hash && open.Instance == conn.InstanceNumber)
        {
            await OpenTownPortal(remote, stream, ciphers, conn, open.X, open.Y, "town portal again", replace: false);
            return;
        }
        if (open.LeadsTo != conn.Layout.Hash) return;
        var (x, y) = conn.Layout.Near((conn.Layout.SpawnX + 4, conn.Layout.SpawnY));
        uint id = conn.NextObjectId++;
        await Send(remote, stream, ciphers, InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(MapPortalObject.MetadataPath),
            new MapPortalObject(x, y, -1, MapPortalType, Open: 1).Blob()), $"0x02b9 create the portal back 0x{id:x} at ({x}, {y}) to 0x{open.Area:x4}");
        await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(id), null);
        conn.BackPortal = (id, x, y);
        string owner = CharacterName(conn);
        _ = Task.Delay(MapPortalListMs).ContinueWith(async _ =>
        {
            if (!conn.Closed && conn.BackPortal?.Id == id)
                await Send(remote, stream, ciphers, InstanceMapDevice.Portals(0, [new(id, open.Area, owner)]),
                    $"0x008f the portal back 0x{id:x} leads to 0x{open.Area:x4}");
        });
    }

    // A Portal Scroll used (C→S 0x58) outside towns and hideouts: one scroll is spent and a portal opens where the player
    // stands, replacing the one there was (the scroll's earlier one, or a map's own portal back). From a map it leads to the
    // hideout (the player came through its map device), anywhere else to the character's last town. In a town or a hideout
    // nothing happens and nothing is spent.
    internal static async Task UsePortalScroll(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, byte row, InventoryItem item,
        ItemCatalog.BaseType type)
    {
        if (!PortalAllowed(conn))
        {
            Console.WriteLine($"[gateway] {remote} <= 0x0058 use {type.Name}: in a town or hideout, or {conn.Layout.Id} forbids portals: nothing happens");
            return;
        }
        var items = ItemsOf(conn);
        if (items.ConsumeOne(row, item.Id) is not { } taken) return;
        uint inventoryId = PlayerInventories.InventoryId(row);
        await Send(remote, stream, ciphers, InstanceItems.InventoryChange(inventoryId, items.NextRevision(row), [item.Id], taken.Left is { } left ? [left] : []),
            $"0x00bf inventory {inventoryId}: {type.Name} used" + (taken.Left is { } now ? $", {now.Item.StackSize} left" : ", stack empty"));
        var (px, py) = conn.Walk.Position(conn.Clock.Elapsed.TotalSeconds);
        await OpenTownPortal(remote, stream, ciphers, conn, px, py, type.Name);
    }

    // Whether a town portal can open here: not in a town or a hideout, and not where the area's own mods forbid portals.
    internal static bool PortalAllowed(ClientState conn) =>
        WaypointCatalog.Areas.Find(conn.Layout.Hash) is not { IsTown: true } && HideoutCatalog.Hideouts.All(h => h.AreaHash16 != conn.Layout.Hash)
        && !AreaMods.Value.DisablesPortals(conn.Layout.Id);

    // A town portal (Portal Scroll, the Portal skill) beside (px, py): from a map to the hideout, anywhere else to the
    // character's last town. It replaces the one there was, and the character's town / hideout gets a portal back to it
    // (OpenPortals, SpawnBackPortal), which brings the player back to this cell of this instance.
    internal static async Task OpenTownPortal(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, int px, int py, string why,
        bool replace = true)
    {
        ushort here = conn.Layout.Hash;
        var home = HomeHideout(conn);
        bool toHideout = MapDevices.Value.IsMapArea(here) && home is not null;
        ushort town = conn.LastTown != 0 ? conn.LastTown : AreaLayouts.LioneyesWatch;
        // One portal per player: the old one closes (0x8f with area 0) and goes.
        if (conn.ReturnPortal is { } old)
        {
            await Send(remote, stream, ciphers, InstanceMapDevice.Portals(0, [new(old.Id, 0)]), null);
            await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(old.Id), $"0x{InstanceProtocol.RemoveObjectId:x4} remove the old portal 0x{old.Id:x}");
        }
        // Beside the player, so the click that opens it is not on the player (a reopened portal stands where it stood).
        var (x, y) = !replace || conn.Layout.Walkable is not { } walkable || !walkable(px + 3, py) ? (px, py) : (px + 3, py);
        uint id = conn.NextObjectId++;
        await Send(remote, stream, ciphers, InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(MapPortalObject.MetadataPath),
            new MapPortalObject(x, y, -1, MapPortalType, Open: 1).Blob()), $"0x02b9 create the Portal Scroll's portal 0x{id:x} at ({x}, {y})");
        await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(id), null);
        conn.ReturnPortal = (id, x, y);
        conn.ReturnPortalTo = toHideout ? (ushort)0 : town;
        ushort leadsTo = toHideout ? home!.AreaHash16 : town;
        string owner = CharacterName(conn);
        OpenPortals[owner] = new OpenPortal(here, conn.InstanceNumber, x, y, leadsTo);
        Console.WriteLine($"[gateway] {remote} {why}: town portal at ({x}, {y}) of 0x{here:x4} instance {conn.InstanceNumber} → 0x{leadsTo:x4}");
        // 0x8f only reaches a portal the client has finished creating (MapPortalListMs, as the device's portals).
        _ = Task.Delay(MapPortalListMs).ContinueWith(async _ =>
        {
            if (!conn.Closed && conn.ReturnPortal?.Id == id)
                await Send(remote, stream, ciphers, InstanceMapDevice.Portals(0, [new(id, leadsTo, owner)]),
                    $"0x008f the Portal Scroll's portal 0x{id:x} leads to 0x{leadsTo:x4} ({(toHideout ? "the hideout" : "the last town")})");
        });
    }
    // C→S 0x00e7: an Atlas map node picked in the hideout map device (u16 WorldAreas HASH16, u8). The layout becomes
    // what the next generic map key opens.
    internal static async Task SelectAtlasMap(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, (ushort Area, byte Mode) picked)
    {
        var layout = MapDevices.Value.Atlas?.ByHash16(picked.Area);
        if (layout is not null && CharacterName(conn) is { } character)
        {
            MapDevices.Value.Atlas!.Select(character, layout);
            CharacterSaves.SetAtlasLayout(character, layout.Id);
            Console.WriteLine($"[gateway] {remote} <= 0x00e7 Atlas node 0x{picked.Area:x4} selected: {layout.Name} ({layout.Id}), mode 0x{picked.Mode:x2}");
        }
        else Console.WriteLine($"[gateway] {remote} <= 0x00e7 Atlas node 0x{picked.Area:x4} (mode 0x{picked.Mode:x2}) is not a known layout");
    }

    // A hideout's placed doodads (HideoutCatalog.IsPlaced: the map device) from its decoration, with their HideoutDoodad data.
    internal static async Task SpawnHideoutDoodads(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        if (HideoutFurniture(conn) is not { } scene) return;
        foreach (var d in new[] { scene.Device })
        {
            if (HideoutCatalog.TypeOf(d.Type!) is not var (components, states) || !HideoutObject.CanSerialize(components))
            {
                Console.WriteLine($"[gateway] {remote} hideout doodad {d.Name} ({d.Type}): unknown type or unsupported components, not created");
                continue;
            }
            uint id = conn.NextObjectId++;
            // "active" 1: the map device stands open and idle (its .ot: per-player quest flag MapDeviceActive).
            var obj = new HideoutObject(d.X, d.Y, d.Rotation, components, new HideoutDoodad(d.Hash, (byte)d.Variation),
                [.. states.Select(s => s == "active" ? 1 : 0)]);
            await Send(remote, stream, ciphers, InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(d.Type!), obj.Blob()),
                $"0x02b9 create hideout doodad 0x{id:x} {d.Name} ({d.Type}) at ({d.X}, {d.Y}), components {string.Join(", ", components)}");
            await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(id), $"0x01a9 hideout doodad 0x{id:x} enters wake zone");
            lock (conn.HideoutDoodads) conn.HideoutDoodads[id] = d;
            await RegisterStationaryCollision(remote, stream, ciphers, conn, id, d.Type!, d.X, d.Y, d.Rotation, "map-device", d.Model);
        }
    }
}
