using System.Collections.Immutable;
using System.Net.Sockets;
using GameServer.Core.Protocol.Login;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.World;

/// <summary>A point where the framework hands over to the Core modules, in the Gateway's own types. A module's subscribers run only for
/// connections whose league lists that module, so a league without one has simply nothing happen here.</summary>
sealed class GameEvent<T> where T : Delegate
{
    ImmutableArray<(string Module, T Run)> _subscribers = [];

    public IDisposable Add(string module, T run)
    {
        ImmutableInterlocked.Update(ref _subscribers, s => s.Add((module, run)));
        return new Subscription(() => ImmutableInterlocked.Update(ref _subscribers, s => s.RemoveAll(e => ReferenceEquals(e.Run, run))));
    }

    /// <summary>The subscribers for this connection's league.</summary>
    public IEnumerable<T> For(ClientState conn)
    {
        foreach (var (module, run) in _subscribers)
            if (Program.LeagueFor(conn).Modules.Contains(module)) yield return run;
    }

    sealed class Subscription(Action remove) : IDisposable
    {
        public void Dispose() => remove();
    }
}

/// <summary>A hostile monster died by the player's hand, inside the kill's commit. What sends messages goes in <see cref="Later"/>: it runs
/// after the commit, in order, whichever way the kill came (a melee hit, a projectile).</summary>
sealed record KillContext(ClientState Conn, SpawnedMonster Monster, string VarietyId, (int X, int Y) At)
{
    public List<Func<Task>> Afterwards { get; } = [];
    public void Later(Func<Task> send) => Afterwards.Add(send);
}

/// <summary>Where the framework hands over to the Core modules.</summary>
static class GameEvents
{
    public static readonly GameEvent<Action<KillContext>> Kill = new();
    /// <summary>Talking to an NPC starts; true when a module has its own dialogue for it.</summary>
    public static readonly GameEvent<Func<ClientState, uint, SpawnedNpc, Task<bool>>> NpcTalk = new();
    /// <summary>A connection's chat commands are made: a module adds to them.</summary>
    public static readonly GameEvent<Func<ChatCommands, string, NetworkStream, ConnectionCiphers, ClientState, ChatCommands>> CommandsBuilding = new();
    public static readonly GameEvent<Func<ClientState, Dictionary<uint, int>, Dictionary<uint, int>>> PlayerStatSources = new();
    public static readonly GameEvent<Func<ClientState, IReadOnlyDictionary<uint, int>, SpawnedMonster, DamagePacket, DamagePacket>> OutgoingDamage = new();
    /// <summary>A click on an object; true when it was this module's.</summary>
    public static readonly GameEvent<Func<ClientState, InstanceTransfer.UseSkillOnObject, double, Task<bool>>> ObjectUse = new();
    /// <summary>A click on an item on the ground; true when it was this module's.</summary>
    public static readonly GameEvent<Func<ClientState, uint, WorldItemObject, InstanceTransfer.UseSkillOnObject, double, Task<bool>>> GroundItemUse = new();
    /// <summary>An NPC option was picked, before its action; false stops it.</summary>
    public static readonly GameEvent<Func<ClientState, NpcCatalog.Talk, Task<bool>>> NpcOptionPicked = new();
    /// <summary>A read of client messages was handled.</summary>
    public static readonly GameEvent<Func<ClientState, IReadOnlyList<ushort>, Task>> MessagesHandled = new();
    public static readonly GameEvent<Func<ClientState, InstanceItems.PickUpItem, Task<bool>>> InventoryPick = new();
    public static readonly GameEvent<Func<ClientState, InstanceItems.PlaceItem, Task<bool>>> InventoryPlace = new();
    public static readonly GameEvent<Func<ClientState, Task>> NpcDialogueClosing = new();
    /// <summary>A gem skill's cast: its effect id, skill id, where from, the action; true when the module took it, null for not its skill.</summary>
    public static readonly GameEvent<Func<ClientState, string?, ushort, (int X, int Y), Func<bool>, bool?>> SkillCast = new();
    /// <summary>A currency used; true when it was this module's.</summary>
    public static readonly GameEvent<Func<ClientState, byte, InventoryItem, ItemCatalog.BaseType, Task<bool>>> ItemUse = new();

    public static async Task<bool> AnyNpcTalk(ClientState conn, uint id, SpawnedNpc npc)
    {
        foreach (var run in NpcTalk.For(conn)) if (await run(conn, id, npc)) return true;
        return false;
    }

    public static ChatCommands RunCommandsBuilding(ClientState conn, ChatCommands commands, string remote, NetworkStream stream, ConnectionCiphers ciphers)
    {
        foreach (var run in CommandsBuilding.For(conn)) commands = run(commands, remote, stream, ciphers, conn);
        return commands;
    }

    public static async Task RunKill(KillContext kill)
    {
        foreach (var run in Kill.For(kill.Conn)) run(kill);
        foreach (var send in kill.Afterwards) await send();
    }

    public static Dictionary<uint, int> WithPlayerStatSources(ClientState conn, Dictionary<uint, int> sources)
    {
        foreach (var run in PlayerStatSources.For(conn)) sources = run(conn, sources);
        return sources;
    }

    public static DamagePacket WithOutgoingDamage(ClientState conn, IReadOnlyDictionary<uint, int> player, SpawnedMonster monster, DamagePacket rolled)
    {
        foreach (var run in OutgoingDamage.For(conn)) rolled = run(conn, player, monster, rolled);
        return rolled;
    }

    public static async Task<bool> AnyObjectUse(ClientState conn, InstanceTransfer.UseSkillOnObject use, double requestedAt)
    {
        foreach (var run in ObjectUse.For(conn)) if (await run(conn, use, requestedAt)) return true;
        return false;
    }

    public static async Task<bool> AnyGroundItemUse(ClientState conn, uint id, WorldItemObject ground, InstanceTransfer.UseSkillOnObject use, double requestedAt)
    {
        foreach (var run in GroundItemUse.For(conn)) if (await run(conn, id, ground, use, requestedAt)) return true;
        return false;
    }

    public static async Task<bool> AllowNpcOption(ClientState conn, NpcCatalog.Talk talk)
    {
        foreach (var run in NpcOptionPicked.For(conn)) if (!await run(conn, talk)) return false;
        return true;
    }

    public static async Task RunMessagesHandled(ClientState conn, IReadOnlyList<ushort> ids)
    {
        foreach (var run in MessagesHandled.For(conn)) await run(conn, ids);
    }

    public static async Task<bool> AnyInventoryPick(ClientState conn, InstanceItems.PickUpItem pick)
    {
        foreach (var run in InventoryPick.For(conn)) if (await run(conn, pick)) return true;
        return false;
    }

    public static async Task<bool> AnyInventoryPlace(ClientState conn, InstanceItems.PlaceItem place)
    {
        foreach (var run in InventoryPlace.For(conn)) if (await run(conn, place)) return true;
        return false;
    }

    public static async Task RunNpcDialogueClosing(ClientState conn)
    {
        foreach (var run in NpcDialogueClosing.For(conn)) await run(conn);
    }

    public static bool? RunSkillCast(ClientState conn, string? effect, ushort skillId, (int X, int Y) from, Func<bool> current)
    {
        foreach (var run in SkillCast.For(conn)) if (run(conn, effect, skillId, from, current) is { } taken) return taken;
        return null;
    }

    public static async Task<bool> AnyItemUse(ClientState conn, byte row, InventoryItem item, ItemCatalog.BaseType type)
    {
        foreach (var run in ItemUse.For(conn)) if (await run(conn, row, item, type)) return true;
        return false;
    }
}
