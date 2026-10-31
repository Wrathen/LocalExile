using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;
using GameServer.Modding;
using static Program;
using static TattoosFeature;

namespace GameServer.Modules.M_tattoos;

/// <summary>Tattoos on passives.</summary>
public sealed class TattoosModule : IModule, IDisposable
{
    readonly List<IDisposable> _events = [];
    void On<T>(GameEvent<T> ev, T run) where T : Delegate => _events.Add(ev.Add("tattoos", run));

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var e in _events) e.Dispose();
    }

    /// <inheritdoc/>
    public void Load(IModuleContext module)
    {
        module.OnPacket(InstanceItems.UseItemOnPassiveId, async p =>
        {
            var conn = ConnOf(p.Player); var (remote, stream, ciphers) = conn.Wire; byte[] bytes = p.Bytes.ToArray();
            await UseTattooOnPassive(remote, stream, ciphers, conn, InstanceItems.ReadUseItemOnPassive(bytes), Convert.ToHexString(bytes));
            return true;
        });
        module.OnChatCommand("tattoo", "/tattoo <name> | list (dev: a tattoo currency item into the inventory)", async c =>
        {
            var conn = ConnOf(c.Player);
            var (remote, stream, ciphers) = conn.Wire;
            string[] args = [.. c.Args];
            if (args.Length == 0 || args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                await conn.Commands!.Reply(string.Join(", ", TattooCatalogue.Names));
                return;
            }
            string query = string.Join(' ', args);
            if (TattooCatalogue.Create(ItemsFor(conn), query) is not { } tattoo)
            {
                await conn.Commands!.Reply($"No tattoo \"{query}\"");
                return;
            }
            var items = ItemsOf(conn);
            if (items.FreeCell(PlayerInventories.MainInventory, 1, 1, ShapeOf) is not { } cell)
            {
                await conn.Commands!.Reply("No room in the inventory");
                return;
            }
            var added = items.Put(PlayerInventories.MainInventory, new InventoryItem(items.NewItemId(), cell.X, cell.Y, tattoo));
            await Send(remote, stream, ciphers, InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.MainInventory),
                items.NextRevision(PlayerInventories.MainInventory), [], [added]), $"0x00bf inventory 1: /tattoo {query}");
            var entry = TattooCatalogue.Find(query)!;
            await conn.Commands!.Reply($"{entry.Name} added to the inventory (replaces {entry.Target}" +
                (entry.EventOnly ? "; event-only, not normally obtainable" : entry.DropDisabled ? "; drop-disabled" : "") + ")");
        });
        module.OnChatCommand("tattoo_apply", "/tattoo_apply <tattoo name> <node> (dev: apply a pilot tattoo to an allocated passive)", async c =>
        {
            var conn = ConnOf(c.Player);
            var (remote, stream, ciphers) = conn.Wire;
            string[] args = [.. c.Args];
            if (args.Length < 2 || !ushort.TryParse(args[^1], out ushort node))
            {
                await conn.Commands!.Reply("Usage: /tattoo_apply <tattoo name> <node>");
                return;
            }
            string name = string.Join(' ', args.Take(args.Length - 1));
            string? error = await ApplyTattoo(remote, stream, ciphers, conn, name, node);
            await conn.Commands!.Reply(error ?? $"{name} applied to passive {node}");
        });
        module.OnChatCommand("journey_makanui", "/journey_makanui <node> [keystone] (dev: apply Journey Makanui, random or chosen)", async c =>
        {
            var conn = ConnOf(c.Player);
            var (remote, stream, ciphers) = conn.Wire;
            string[] args = [.. c.Args];
            if (args.Length < 1 || !ushort.TryParse(args[0], out ushort node))
            {
                await conn.Commands!.Reply("Usage: /journey_makanui <node> [keystone name|graph id]");
                return;
            }
            JourneyMakanuiCatalogue.Entry? chosen = null;
            if (args.Length > 1)
            {
                chosen = JourneyMakanuiCatalogue.Find(string.Join(' ', args.Skip(1)));
                if (chosen is null)
                {
                    await conn.Commands!.Reply($"No keystone \"{string.Join(' ', args.Skip(1))}\"");
                    return;
                }
            }
            string? error = await ApplyTattoo(remote, stream, ciphers, conn, "Journey Tattoo of Makanui", node,
                chosenKeystone: chosen?.GraphId);
            await conn.Commands!.Reply(error ?? $"Journey Tattoo of Makanui applied to passive {node}" +
                (chosen is null ? " (random keystone)" : $" ({chosen.Name})"));
        });
        module.OnChatCommand("tattoo_remove", "/tattoo_remove <node> (dev: remove the tattoo from a passive)", async c =>
        {
            var conn = ConnOf(c.Player);
            var (remote, stream, ciphers) = conn.Wire;
            string[] args = [.. c.Args];
            if (args.Length != 1 || !ushort.TryParse(args[0], out ushort node))
            {
                await conn.Commands!.Reply("Usage: /tattoo_remove <node>");
                return;
            }
            string? error = await RemoveTattoo(remote, stream, ciphers, conn, node);
            await conn.Commands!.Reply(error ?? $"Tattoo removed from passive {node}");
        });
    }
}
