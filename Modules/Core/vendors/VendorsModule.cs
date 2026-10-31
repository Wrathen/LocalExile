using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;
using GameServer.Modding;
using static Program;
using static VendorsFeature;

namespace GameServer.Modules.M_vendors;

/// <summary>NPC shops: buying, and selling to a vendor.</summary>
public sealed class VendorsModule : IModule, IDisposable
{
    readonly List<IDisposable> _events = [];
    void On<T>(GameEvent<T> ev, T run) where T : Delegate => _events.Add(ev.Add("vendors", run));

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var e in _events) e.Dispose();
    }

    /// <inheritdoc/>
    public void Load(IModuleContext module)
    {
        module.OnNpcOption(async talk =>
        {
            var conn = ConnOf(talk.Player);
            var (remote, stream, ciphers) = conn.Wire;
            SpawnedNpc? npc;
            lock (conn.Npcs) npc = conn.Npcs.GetValueOrDefault(conn.TalkingTo);
            if (npc is null) return false;
            if (talk.Category == "BuyItems") await OpenNpcShop(remote, stream, ciphers, conn, conn.TalkingTo, npc);
            else if (talk.Category == "SellItems") await OpenNpcSale(remote, stream, ciphers, conn, npc);
            else return false;
            return true;
        });
        On(GameEvents.NpcDialogueClosing, async conn =>
        {
            var (remote, stream, ciphers) = conn.Wire;
            if (conn.Sale is not null) await CloseSale(remote, stream, ciphers, conn, accepted: false);
        });
        On(GameEvents.InventoryPick, async (conn, pick) =>
        {
            if (conn.Sale is not { } sale || pick.InventoryId != sale.Mine) return false;
            var (remote, stream, ciphers) = conn.Wire;
            await TakeFromSale(remote, stream, ciphers, conn, sale, pick.ItemId);
            return true;
        });
        On(GameEvents.InventoryPlace, async (conn, place) =>
        {
            if (conn.Sale is not { } sale || place.InventoryId != sale.Mine) return false;
            var (remote, stream, ciphers) = conn.Wire;
            uint cursor = PlayerInventories.InventoryId(PlayerInventories.Cursor);
            var held = ItemsOf(conn).Items(PlayerInventories.Cursor).FirstOrDefault();
            if (held is not null) await OfferForSale(remote, stream, ciphers, conn, sale, cursor, held.Id, (byte)place.X, (byte)place.Y);
            return true;
        });
        module.OnPacket(InstanceNpcs.TradeOfferId, async p =>
        {
            var conn = ConnOf(p.Player); var (remote, stream, ciphers) = conn.Wire; byte[] bytes = p.Bytes.ToArray();
            // A trade between players has these messages first.
            if (conn.Trade is not null) return false;
            var offer = InstanceNpcs.ReadTradeOffer(bytes);
            // Ctrl + click on an inventory item sends x = y = 0xff (0x14030e3e0 → 0x142166660): any free offer cell.
            if (conn.Sale is { } sale && offer.X == 0xff && offer.Y == 0xff) await OfferFromInventory(remote, stream, ciphers, conn, sale, offer.InventoryId, offer.ItemId);
            else if (conn.Sale is { } open) await OfferForSale(remote, stream, ciphers, conn, open, offer.InventoryId, offer.ItemId, offer.X, offer.Y);
            else Console.WriteLine($"[gateway] {remote} <= 0x00b2 trade offer {offer}: no sale open");
            return true;
        });
        foreach (ushort id in new[] { InstanceNpcs.TradeTakeBackId, InstanceNpcs.TradeMoveAllId })
            module.OnPacket(id, async p =>
            {
                var conn = ConnOf(p.Player); var (remote, stream, ciphers) = conn.Wire; byte[] bytes = p.Bytes.ToArray();
            // A trade between players has these messages first.
            if (conn.Trade is not null) return false;
                var (item, inventory) = InstanceNpcs.ReadTradeMove(bytes);
                if (conn.Sale is not { } sale) Console.WriteLine($"[gateway] {remote} trade move: no sale open");
                else if (ItemsOf(conn).Offered().Any(i => i.Id == item)) await TakeFromSale(remote, stream, ciphers, conn, sale, item);
                else await OfferFromInventory(remote, stream, ciphers, conn, sale, inventory, item);
                return true;
            });
        module.OnPacket(InstanceNpcs.TradeAcceptId, async p =>
        {
            var conn = ConnOf(p.Player); var (remote, stream, ciphers) = conn.Wire; byte[] bytes = p.Bytes.ToArray();
            // A trade between players has these messages first.
            if (conn.Trade is not null) return false;
            uint revision = InstanceNpcs.ReadTradeAccept(bytes);
            await CloseSale(remote, stream, ciphers, conn, accepted: conn.Sale is { } s && s.TheirsRevision == revision);
            return true;
        });
        module.OnPacket(InstanceNpcs.TradeAnswerId, async p =>
        {
            var conn = ConnOf(p.Player); var (remote, stream, ciphers) = conn.Wire; byte[] bytes = p.Bytes.ToArray();
            // A trade between players has this message first.
            if (conn.Trade is not null || conn.TradeRequestTo != 0 || (InstanceNpcs.ReadTradeAnswer(bytes).Other != 0 && conn.Sale is null)) return false;
            await AnswerSale(remote, stream, ciphers, conn, InstanceNpcs.ReadTradeAnswer(bytes));
            return true;
        });
        module.OnPacket(InstanceNpcs.BuyId, async p =>
        {
            var conn = ConnOf(p.Player); var (remote, stream, ciphers) = conn.Wire; byte[] bytes = p.Bytes.ToArray();
            await BuyFromShop(remote, stream, ciphers, conn, InstanceNpcs.ReadBuy(bytes));
            return true;
        });
        module.OnPacket(InstanceNpcs.CtrlBuyId, async p =>
        {
            var conn = ConnOf(p.Player); var (remote, stream, ciphers) = conn.Wire; byte[] bytes = p.Bytes.ToArray();
            await CtrlBuyFromShop(remote, stream, ciphers, conn, InstanceNpcs.ReadCtrlBuy(bytes));
            return true;
        });
    }
}
