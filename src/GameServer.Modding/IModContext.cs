namespace GameServer.Modding;

/// <summary>
/// What a mod can do, given to <see cref="IMod.Load"/>. Every <c>On…</c> and <c>Modify…</c> call returns a handle the host
/// owns: disposing it removes that one hook early; unloading the mod removes them all. A hook that throws is logged
/// (<c>[mods]</c> in the Gateway's log) and skipped; it does not stop the player's connection or the other mods.
/// </summary>
/// <remarks>
/// Hooks run on the player's connection tasks, several players at once: keep shared state thread-safe. The modifiers
/// (<see cref="ModifyStats"/> especially) run often and synchronously; keep them cheap.
/// </remarks>
public interface IModContext
{
    /// <summary>The mod's <c>mod.json</c>.</summary>
    ModManifest Manifest { get; }

    /// <summary>The mod's folder (<c>Mods/Binaries/&lt;id&gt;</c>), for its own files.</summary>
    string Folder { get; }

    /// <summary>Writes a line to the Gateway's log, as <c>[mods] &lt;id&gt;: message</c>.</summary>
    void Log(string message);

    /// <summary>A slash command, <c>/name args…</c>. The Gateway's own commands win over a mod's of the same name, and the
    /// first mod to register a name keeps it. <paramref name="usage"/> is what /commands lists ("/name &lt;args&gt; (what
    /// it does)").</summary>
    IDisposable OnChatCommand(string name, string usage, Func<ChatCommand, Task> run);

    /// <summary>Every player tick (about 4 a second while the player is in an area and not paused), with the game seconds
    /// since the last one.</summary>
    IDisposable OnTick(Func<IPlayer, double, Task> tick);

    /// <summary>A client message (C→S) with this opcode, before the Gateway handles it. Return true to take it: the
    /// Gateway's own handler then does not run.</summary>
    IDisposable OnPacket(ushort opcode, Func<Packet, Task<bool>> handle);

    /// <summary>The player's area is ready (the client sent "area generated", and the area's monsters and objects are
    /// placed).</summary>
    IDisposable OnAreaGenerated(Func<IPlayer, Task> generated);

    /// <summary>A hostile monster died by the player's hand: before its experience and drops, or right after them for a
    /// projectile's kill (a bow, a spell).</summary>
    IDisposable OnMonsterKilled(Func<MonsterKill, Task> killed);

    /// <summary>The player used an object with the default action (a click on it). Return true when the object is this
    /// mod's: nothing else handles the use.</summary>
    IDisposable OnObjectUsed(Func<ObjectUse, Task<bool>> used);

    /// <summary>A killed monster's drops, before they are rolled: scale them, add items, or cancel them.</summary>
    IDisposable ModifyDrops(Action<DropRoll> modify);

    /// <summary>The experience of a kill, before the character and its gems gain it.</summary>
    IDisposable ModifyExperience(Action<ExperienceGain> modify);

    /// <summary>Stats added to the character's own (beside the passives' and auras'), by stat id
    /// (<c>Stats.dat</c>, as /set_stat takes them). Asked again whenever the server rebuilds the character's stats.</summary>
    IDisposable ModifyStats(Action<StatSheet> modify);
}

/// <summary>What a module (or a league's own code) can do: everything a mod can, and overhauls. It runs only for the players of
/// leagues that list it. The hooks of the league run first, then its modules', then mods'.</summary>
public interface IModuleContext : IModContext
{
    /// <summary>The player is about to leave the area or the game, with why.</summary>
    IDisposable OnPlayerLeaving(Func<IPlayer, string, Task> leaving);

    /// <summary>The rules this module gives the leagues that use it; the first in a league's list that gives any wins.</summary>
    void ProvideLeagueRules(Func<LeagueRules> rules);

    /// <summary>What the module can do with this player's world.</summary>
    IPlayerWorld World(IPlayer player);

    /// <summary>The player's hideout is being furnished: place the module's stations here.</summary>
    IDisposable OnHideoutReady(Func<IPlayer, Task> ready);

    /// <summary>Messages sent as the player enters an area, before its objects: a log line and the message.</summary>
    IDisposable OnEnterMessages(Func<IPlayer, IEnumerable<(string Log, byte[] Message)>> messages);

    /// <summary>What the area's first message says beyond the server's own; null for no opinion. The first answer wins.</summary>
    IDisposable OnAreaHeader(Func<IPlayer, AreaHeader?> header);

    /// <summary>An option picked in an NPC's dialogue; the Gateway only talks to NPCs, and what an option does is a module's.
    /// Return true when it was this module's. An option nothing answers does nothing.</summary>
    IDisposable OnNpcOption(Func<NpcOption, Task<bool>> run);
}
