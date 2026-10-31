namespace GameServer.Modding;

/// <summary>
/// A mod's entry point. The Gateway creates every public, non-abstract class of the mod's assembly that implements it
/// (with a parameterless constructor) and calls <see cref="Load"/> once. A mod registers everything it does through the
/// context; what it registered is dropped when the mod unloads (a reload, a changed or deleted folder). A class that also
/// implements <see cref="IDisposable"/> is disposed before that.
/// </summary>
/// <remarks>
/// Static fields of a mod live in its own load context: a reload starts them over, like the rest of its state.
/// </remarks>
public interface IMod
{
    /// <summary>Registers the mod's hooks. An exception here leaves the mod unloaded, with the error in its status.</summary>
    void Load(IModContext mod);
}

/// <summary>
/// A module's entry point: anything from an NPC's dialogue to a whole game system, where a mod (<see cref="IMod"/>) is a small
/// modification. Modules live in <c>Modules/</c> and <c>Modules/Core/</c>, and load only when a league lists them. They get what mods
/// get and what only they can do (<see cref="IModuleContext"/>).
/// </summary>
public interface IModule
{
    /// <summary>Registers the module's leagues and hooks. An exception here leaves it unloaded, with the error in its status.</summary>
    void Load(IModuleContext module);
}

/// <summary>What kind of mod or module a folder holds (<c>mod.json</c> or <c>module.json</c> "kind").</summary>
public enum ModKind
{
    /// <summary>A feature others may depend on.</summary>
    Feature,
    /// <summary>A league: not a mod or a module, kept so an old manifest says what is wrong.</summary>
    League,
    /// <summary>A small script.</summary>
    Script,
}

/// <summary>A mod's <c>mod.json</c>.</summary>
/// <param name="Id">The folder name under <c>Mods/Binaries/</c>, lower case: what other mods depend on.</param>
/// <param name="Version">Free text, shown by /mod.</param>
/// <param name="Kind">See <see cref="ModKind"/>.</param>
/// <param name="Depends">Ids loaded before this mod; it does not load while one is missing or failed.</param>
/// <param name="Assembly">The file name of the mod's assembly in its folder, <c>&lt;id&gt;.dll</c> by default.</param>
/// <param name="Enabled">Whether it loads until /mod_enable or /mod_disable says otherwise; true by default.</param>
public sealed record ModManifest(string Id, string Version, ModKind Kind, IReadOnlyList<string> Depends, string Assembly, bool Enabled = true);

/// <summary>A monster's rarity.</summary>
public enum MonsterRarity
{
    /// <summary>White.</summary>
    Normal,
    /// <summary>Blue.</summary>
    Magic,
    /// <summary>Yellow.</summary>
    Rare,
    /// <summary>Orange (bosses).</summary>
    Unique,
}
