using GameServer.Core.Accounts;
using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using Progression = GameServer.Core.World.Progression;
using DamageModel = GameServer.Core.World.DamageModel;
using DamageType = GameServer.Core.World.DamageType;

partial class Program
{
    internal static IEnumerable<(string Name, string Usage, string[] Arguments)> CommandUsages() => Commands("", null!, null!, null!).Usages;

    // The chat commands of one connection (ChatCommands.cs).
    internal static ChatCommands Commands(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        var commands = new ChatCommands
        {
            Send = (bytes, log) => Send(remote, stream, ciphers, bytes, log),
            // S→C 0x17 chat lines in the player's chat panel, one per line of the text (an empty line as a space).
            Reply = async text =>
            {
                foreach (string line in text.Split('\n').Select(l => l.Length == 0 ? " " : l))
                    await Send(remote, stream, ciphers, InstanceChat.ChatLine(line), $"0x0017 chat line \"{line}\"");
            },
            ModCommand = (name, args) => ServerHooks.RunCommand(PlayerOf(conn), name, args),
            ModUsages = () => ServerHooks.Commands,
        }
        .Add("module", "/module (the modules loaded when the Gateway started, from the folders in Modules/: version, kind and hooks, and why others did not load)", async _ =>
        {
            var modules = ServerModules.Mods();
            await conn.Commands!.Reply(string.Join('\n', modules.Select(m => m.Error is { } error ? $"{m.Id} - not loaded, {error}"
                : $"{m.Id} {m.Version} - {m.Kind?.ToString().ToLowerInvariant()}, {m.Hooks} hook(s)").Prepend($"--- Modules in {ServerModules.Folder}/ ---")));
        })
        .Add("module_reload", "/module_reload [id] (hot-reload one module and those that depend on it, or every module; their state starts over, and what is wrong with the new files leaves them running)", async args =>
        {
            string? id = args.Length > 0 ? args[0] : null;
            ServerModules.Reload(id);
            await conn.Commands!.Reply(id is null ? "Modules reloaded (/module for the list)." : $"Module {id} reloaded (/module for the list).");
        })
        .Add("mod", "/mod (the mods in Mods/Binaries/: loaded ones with their version, kind and hooks, and why the others are not)", async _ =>
        {
            var mods = ServerMods.Mods();
            await conn.Commands!.Reply(mods.Count == 0 ? $"No mods in {ServerMods.Folder}/ (Agents/docs/dev/modding.md)."
                : string.Join('\n', mods.Select(m => m.Error is { } error ? $"{m.Id} - not loaded, {error}"
                    : $"{m.Id} {m.Version} - {m.Kind?.ToString().ToLowerInvariant()}, {m.Hooks} hook(s)").Prepend($"--- Mods in {ServerMods.Folder}/ ---")));
        })
        .Add("mod_reload", "/mod_reload [id] (reload one mod and the mods that depend on it from its folder, or every mod; their state starts over)", async args =>
        {
            string? id = args.Length > 0 ? args[0] : null;
            ServerMods.Reload(id);
            await conn.Commands!.Reply(id is null ? "Mods reloaded (/mod for the list)." : $"Mod {id} reloaded (/mod for the list).");
        })
        .Add("mod_enable", "/mod_enable <id> (load a mod, now and at every start)", args => SetModEnabled(conn, args, true))
        .Add("mod_disable", "/mod_disable <id> (unload a mod and the mods that depend on it, now and at every start)", args => SetModEnabled(conn, args, false));
        return conn is null ? commands : GameEvents.RunCommandsBuilding(conn, commands, remote, stream, ciphers);
    }
}
