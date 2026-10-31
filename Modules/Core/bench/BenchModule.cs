using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Modding;
using PlayerWalk = GameServer.Core.World.PlayerWalk;
using static Program;

namespace GameServer.Modules.CraftingBench;

/// <summary>The crafting bench in the hideout.</summary>
public sealed class BenchModule : IModule
{
    /// <inheritdoc/>
    public void Load(IModuleContext module)
    {
        module.OnHideoutReady(player =>
        {
            var conn = ConnOf(player);
            return BenchFeature.SpawnBench(conn.Wire.Remote, conn.Wire.Stream, conn.Wire.Ciphers, conn);
        });
        module.OnObjectUsed(async use =>
        {
            var conn = ConnOf(use.Player);
            if (conn.CraftingBenchObject == 0 || use.ObjectId != conn.CraftingBenchObject) return false;
            await BenchFeature.OpenCraftingBench(conn.Wire.Remote, conn.Wire.Stream, conn.Wire.Ciphers, conn, use.ObjectId, use.RequestedAt);
            return true;
        });
        module.OnPacket(InstanceBench.MoveId, async p =>
        {
            var conn = ConnOf(p.Player);
            await BenchFeature.MoveToBench(conn.Wire.Remote, conn.Wire.Stream, conn.Wire.Ciphers, conn, InstanceBench.ReadMove(p.Bytes.ToArray()));
            return true;
        });
        module.OnPacket(InstanceBench.CraftId, async p =>
        {
            var conn = ConnOf(p.Player);
            await BenchFeature.CraftBench(conn.Wire.Remote, conn.Wire.Stream, conn.Wire.Ciphers, conn, InstanceBench.ReadCraft(p.Bytes.ToArray()));
            return true;
        });
    }
}

static class BenchFeature
{
    // The bench in the hideout, where the league has one.
    internal static async Task SpawnBench(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        var scene = conn.CurrentHideoutScene;
        if (LeagueFor(conn).Rules.CraftingBench && scene?.Bench is { } bench
            && HideoutCatalog.TypeOf(bench.Type!) is var (components, states)
            && HideoutObject.CanSerialize(components))
        {
            uint benchId = conn.NextObjectId++;
            var obj = new HideoutObject(bench.X, bench.Y, bench.Rotation, components,
                new HideoutDoodad(bench.Hash, (byte)bench.Variation), [.. states.Select(_ => 0)]);
            await Send(remote, stream, ciphers, InstanceEntities.CreateObject(benchId,
                InstanceEntities.ObjectTypeHash(bench.Type!), obj.Blob()),
                $"0x02b9 create crafting bench 0x{benchId:x} left of guild stash at ({bench.X}, {bench.Y})");
            await Send(remote, stream, ciphers, InstanceProtocol.EnterWakeZone(benchId), null);
            conn.CraftingBenchObject = benchId;
            conn.CraftingBenchAt = (bench.X, bench.Y);
            await RegisterStationaryCollision(remote, stream, ciphers, conn, benchId, bench.Type!, bench.X, bench.Y, bench.Rotation, "bench", bench.Model);
        }
    }

    // C→S 0x24: approach the bench before opening the ordinary crafting window (0x7b kind 0x33, bench type 0).
    internal static async Task OpenCraftingBench(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        uint use, double requestedAt)
    {
        if (!LeagueFor(conn).Rules.CraftingBench) return;
        if (!TryBeginObjectWalk(conn, requestedAt, conn.CraftingBenchAt.X, conn.CraftingBenchAt.Y, conn.StationaryInteractionSize(conn.CraftingBenchObject), out var objectWalk)) return;
        var (action, now, here, stop, arrive) = objectWalk;
        Console.WriteLine($"[gateway] {remote} <= 0x0024 crafting bench (0x{use:x}) from ({here.X}, {here.Y}): reached at ({stop.X}, {stop.Y}) in {arrive - now:0.00} s");
        if (arrive > now) await Task.Delay(TimeSpan.FromSeconds(arrive - now));
        if (conn.Closed || !PlayerActionCurrent(conn, action)
            || !PlayerWalk.InWalkUpRange(stop, conn.CraftingBenchAt, conn.StationaryInteractionSize(conn.CraftingBenchObject))) return;
        conn.BenchOpen = true;
        conn.ModuleWindow = null;
        await RefreshBenchBalance(remote, stream, ciphers, conn);
        await Send(remote, stream, ciphers, InstanceWaypoints.OpenUi(InstanceWaypoints.CraftingBenchUi),
            "0x007b open the crafting bench window (type 0)", still: () => PlayerActionCurrent(conn, action));
    }

    internal static async Task MoveToBench(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, (uint ItemId, bool IntoBench) request)
    {
        if (!LeagueFor(conn).Rules.CraftingBench || !conn.BenchOpen) return;
        var items = ItemsOf(conn);
        if (ItemMoves.MoveToBench(items, request.ItemId, request.IntoBench, ShapeOf) is not { } move)
        {
            Console.WriteLine($"[gateway] {remote} <= 0x00e9 Ctrl + click item {request.ItemId} {(request.IntoBench ? "onto" : "off")} the crafting bench: refused (not there, slot taken or no room)");
            return;
        }
        await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(move.From),
            items.NextRevision(move.From), [request.ItemId], []), $"0x00e9 Ctrl + click: item {request.ItemId} leaves row {move.From}");
        await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(move.To),
            items.NextRevision(move.To), [], [move.Placed]), $"0x00e9 Ctrl + click: item {request.ItemId} into row {move.To} at ({move.Placed.X}, {move.Placed.Y})");
    }

    internal static async Task CraftBench(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, (int Recipe, bool Bound) request)
    {
        if (!LeagueFor(conn).Rules.CraftingBench || !conn.BenchOpen || HideoutCatalog.ByArea(conn.Layout.Hash) is null) return;
        var items = ItemsOf(conn);
        var stash = PersonalStashOf(conn);
        var result = ItemMoves.CraftAtBench(items, stash, Bench, request.Recipe, Random.Shared);
        if (result is null)
        {
            var target = items.Items(PlayerInventories.CraftingBench).Select(i => i.Item).FirstOrDefault();
            string mods = target is null ? "" : string.Join(", ", target.Mods.Select(m => Items.Mods.ByHash(m.Hash)?.Id ?? $"0x{m.Hash:x4}"));
            Console.WriteLine($"[gateway] {remote} <= 0x015d bench recipe {request.Recipe} ({Bench.Find(request.Recipe)?.Mod?.Id ?? Bench.Find(request.Recipe)?.Name}) refused on " +
                (target is null ? "no item" : $"{Items.ByHash(target.TypeHash)?.Id} {target.Rarity} ilvl {target.ItemLevel} [{mods}]"));
            await RefreshBenchBalance(remote, stream, ciphers, conn);
            await Send(remote, stream, ciphers, InstanceChat.ChatLine("Craft refused: incompatible item or insufficient currency in inventory and stash."), "bench craft refused");
            return;
        }
        foreach (var payment in result.Payments)
        {
            if (payment.Inventory is { } paid)
                await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.MainInventory),
                    items.NextRevision(PlayerInventories.MainInventory), [.. paid.Removed, .. paid.Changed.Select(c => c.Id)], paid.Changed), "bench currency paid from inventory");
            await SendStashPayment(remote, stream, ciphers, conn, stash, payment.Stash);
        }
        await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.CraftingBench),
            items.NextRevision(PlayerInventories.CraftingBench), [result.Changed.Id], [result.Changed]), $"bench recipe {request.Recipe}: crafted item {result.Changed.Id}");
        await RefreshBenchBalance(remote, stream, ciphers, conn);
    }

}
