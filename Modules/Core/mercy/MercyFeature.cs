using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.Diagnostics;
using GameServer.Core.World;
using static Program;

static class MercyFeature
{
    internal static bool MercyDone(ClientState conn) => MercyMission.Done(conn.HasQuestFlag);

    internal static ItemCatalog.BaseType? MedicineChestType() => Items.Find(MercyMission.MedicineChest) is { Supported: true } type ? type : null;

    internal static bool IsMedicineChest(Item item) => Items.ByHash(item.TypeHash)?.Id == MercyMission.MedicineChest;

    // Hailrake died on The Tidal Island: the Medicine Chest where he fell (the item's drop_monsters), unless Nessa has it
    // already, this character carries one or one lies on this instance's ground (MercyMission.HailrakeDrops).
    internal static async Task DropQuestItems(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, string varietyId, (int X, int Y) at)
    {
        if (!MercyMission.DropsChest(conn.Layout.Id, varietyId)) return;
        if (MedicineChestType() is not { } type)
        {
            Console.WriteLine($"[gateway] {remote} a1q5: {MercyMission.MedicineChest} is not in the item data");
            return;
        }
        if (!MercyMission.HailrakeDrops(type.Hash, MercyDone(conn), ItemsOf(conn), conn.Instance.Ground))
        {
            Console.WriteLine($"[gateway] {remote} Hailrake: no {type.Name}, the character has one or Nessa has it");
            return;
        }
        var (x, y) = conn.Layout.Walkable is { } walks && !walks(at.X, at.Y) ? conn.Layout.Near(at) : at;
        var ground = new WorldItemObject(x, y, ItemCatalog.Create(type));
        uint id = conn.Instance.Ground.Add(ground);
        await Send(remote, stream, ciphers, InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(WorldItemObject.MetadataPath), ground.Blob()),
            $"0x02b9 {type.Name} 0x{id:x} dropped by Hailrake at ({x}, {y})");
        await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(id), null);
    }

    // C→S 0x24 on a ground item. A Medicine Chest the character still needs is picked up as any item, then its flag follows.
    // After Nessa has one (the .it remove_flag), the player walks to it as for a pickup (the echo, the same action check)
    // and the chest is removed from the ground instead of entering the inventory. False when the item is not a chest.
    internal static async Task<bool> PickUpQuestItem(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, uint id, WorldItemObject ground,
        InstanceTransfer.UseSkillOnObject use, double requestedAt)
    {
        switch (MercyMission.PickupOf(IsMedicineChest(ground.Item), MercyDone(conn)))
        {
            case MercyMission.Pickup.NotQuestItem:
                return false;
            case MercyMission.Pickup.PickUp:
                await PickUpGroundItem(remote, stream, ciphers, conn, id, ground, use, requestedAt);
                await SyncQuestItems(remote, stream, ciphers, conn, removeItems: false);
                return true;
        }
        if (!TryBeginObjectWalk(conn, requestedAt, ground.GridX, ground.GridY, PickupReach, out var objectWalk, centreRange: true)) return true;
        var (action, now, here, _, arrive) = objectWalk;
        await Send(remote, stream, ciphers,
            InstanceProtocol.ActionStarted(PlayerId, here.X, here.Y, ground.GridX, ground.GridY, use.SkillId, use.Sequence, use.Flags, targetObjectId: id),
            $"0x01ab player walks to Medicine Chest 0x{id:x}");
        if (arrive > now) await Task.Delay(TimeSpan.FromSeconds(arrive - now));
        if (!PlayerActionCurrent(conn, action)) return true;
        if (conn.Instance.Ground.Take(id) is not null)
            await Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(id), $"0x02bb remove Medicine Chest 0x{id:x}: Nessa has one");
        return true;
    }

    // HaveMedicineChest follows the chest the character carries (MercyMission.Sync), whatever took it away (a drop, a sale,
    // the stash). With removeItems (the message path only, so the 0xbf revisions stay in order with the other inventory
    // changes), a chest carried after Nessa has one goes (the .it remove_flag).
    internal static async Task SyncQuestItems(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, bool removeItems)
    {
        if (MedicineChestType() is not { } type) return;
        await conn.QuestGate.WaitAsync();
        try
        {
            var items = ItemsOf(conn);
            bool? set;
            List<(byte Row, InventoryItem Item)> removed = [];
            lock (conn.Monsters)
            {
                if (HardcoreProgressBlocked(conn)) return;
                var flags = conn.QuestFlagsSnapshot();
                bool carried = MercyMission.Carried(items).Any(c => c.Item.Item.TypeHash == type.Hash);
                var sync = MercyMission.Sync(flags.Contains(MercyMission.HaveMedicineChest), MercyMission.Done(flags.Contains), carried);
                set = sync.Set;
                if (sync.Remove && removeItems) removed = MercyMission.RemoveAll(items, type.Hash);
            }
            foreach (var row in removed.GroupBy(r => r.Row))
                await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(row.Key), items.NextRevision(row.Key),
                    [.. row.Select(r => r.Item.Id)], []), $"0x00bf inventory {PlayerInventories.InventoryId(row.Key)}: Medicine Chest removed, Nessa has one");
            if (set is bool on) await SetQuestFlagsHeld(remote, stream, ciphers, conn, [MercyMission.HaveMedicineChest], on);
        }
        finally { conn.QuestGate.Release(); }
    }

    // A safety net for the flag about once a second while the player is in this area (started with the area): flags only,
    // no inventory change. An error is logged and the next tick runs again; the loop ends with the connection.
    internal static async Task RunMercyMission(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        while (!conn.Closed)
        {
            await Task.Delay(1000);
            if (conn.Closed || !conn.PlayerSent) continue;
            try { await SyncQuestItems(remote, stream, ciphers, conn, removeItems: false); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { return; }   // the connection closed
            catch (Exception ex)
            {
                if (conn.Closed) return;
                Console.WriteLine($"[gateway] {remote} a1q5 sync: {ServerLog.Describe(ex)}");
            }
        }
    }

    // C→S 0x82 on an NPCTalk row that sets NessaCongratulated (Nessa's "The Medicine Chest"): she takes the chest with the
    // flag (MercyMission.HandIn, one save). Without a chest the talk does not happen: the option list comes back. True
    // when the talk may go on.
    internal static async Task<bool> HandInQuestItems(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, NpcCatalog.Talk talk)
    {
        if (!MercyMission.HandsIn(talk.StartFlag, MercyDone(conn))) return true;
        var items = ItemsOf(conn);
        (byte Row, InventoryItem Item)? handed = null;
        await conn.QuestGate.WaitAsync();
        try
        {
            lock (conn.Monsters)
            {
                if (HardcoreProgressBlocked(conn)) return false;
                if (MedicineChestType() is { } type) handed = MercyMission.HandIn(CharacterName(conn), items, type.Hash);
            }
            if (handed is var (row, chest))
            {
                await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(row), items.NextRevision(row), [chest.Id], []),
                    $"0x00bf inventory {PlayerInventories.InventoryId(row)}: Medicine Chest {chest.Id} given to Nessa");
                await SetQuestFlagsHeld(remote, stream, ciphers, conn, [MercyMission.NessaCongratulated]);
                await SetQuestFlagsHeld(remote, stream, ciphers, conn, [MercyMission.HaveMedicineChest], on: false);
            }
        }
        finally { conn.QuestGate.Release(); }
        if (handed is not null) return true;
        Console.WriteLine($"[gateway] {remote} Nessa: no Medicine Chest carried");
        await SyncQuestItems(remote, stream, ciphers, conn, removeItems: true);
        await Send(remote, stream, ciphers, InstanceNpcs.Talk(conn.TalkingTo), $"0x0084 npc 0x{conn.TalkingTo:x}: options again (no Medicine Chest)");
        return false;
    }
}
