using System.Text.Json;
using GameServer.Core.Protocol.Login;

namespace GameServer.Core.Accounts;

/// <summary>A league (<c>Leagues/&lt;Name&gt;/league.json</c>): where characters live, a set of modules, and where they go next.
/// A variant the client shows through a check box on another league's card names it in <see cref="EntryOf"/>; with none it has a
/// card of its own.</summary>
public sealed record League(string Name, string Base, bool Hardcore, bool Ssf, string? Form = null,
    string SsfParent = "", string HardcoreParent = "", string FormParent = "", string FormDescription = "", bool PermanentDeath = false)
{
    /// <summary>Where its characters go when it ends, or when it is removed by migration. Not C# inheritance: nothing else passes on.</summary>
    public string? Parent { get; init; }
    /// <summary>The league whose card shows this one's check box.</summary>
    public string? EntryOf { get; init; }
    /// <summary>The text on its card; the client crashes on an empty one.</summary>
    public string? CardText { get; init; }
    /// <summary>Where a Hardcore death sends a character, if not the parent.</summary>
    public string? DeathLeague { get; init; }
    /// <summary>Offered on the create screen.</summary>
    public bool Creatable { get; init; } = true;
    /// <summary>It has ended: no new characters, and its characters cannot be entered until they migrate to the parent.</summary>
    public bool Ended { get; init; }
    /// <summary>"HardMode": which of the game files' data its characters use.</summary>
    public string? DataSet { get; init; }
    /// <summary>Whether it gets every module in Modules/Core by default.</summary>
    public bool CoreModules { get; init; } = true;
    public IReadOnlyList<string> AddModules { get; init; } = [];
    public IReadOnlyList<string> RemoveModules { get; init; } = [];
    /// <summary>False for a character's league with no folder: it can be listed but not entered.</summary>
    public bool Loaded { get; init; } = true;

    public bool IsMain => EntryOf is null;

    /// <summary>The card text. It must not be empty: the create screen's card (0x140cc8820) throws "invalid string
    /// position" on an empty one and the client crashes (live 2026-10-18). A leading '#' makes it a lookup key.</summary>
    public string Description => CardText is { Length: > 0 } text ? text : $"The {Name} league.";

    /// <summary>The S→C 0x1b entry. Main leagues get a card on the create screen; variants are reached through its check
    /// boxes. The client has one check box for a league form, and its wire names call it Ruthless.</summary>
    public LeagueEntry Entry() => new(Name, Description, SsfParent, HardcoreParent, FormParent, 0, 0, 0,
        (byte)((IsMain ? LeagueEntry.Main : 0) | (Hardcore ? LeagueEntry.Hardcore : 0)),
        (byte)((IsMain ? 0 : LeagueEntry.Variant) | (Form is not null ? LeagueEntry.Ruthless : 0)),
        0, 0, 0, 0xd1);
}

/// <summary>
/// The leagues in <c>Game_LEAGUES_FOLDER</c> (default Leagues), read once when a server starts. Standard is required; every other
/// league's parents must lead to Standard or Void, and one that does not is not loaded. Auth and the Gateway read the same folders.
/// Any name works: the client has no league list of its own.
/// </summary>
public sealed class LeagueCatalog
{
    public const string Standard = "Standard";

    /// <summary>The leagues that are loaded, ended and Void included.</summary>
    public IReadOnlyList<League> Loaded { get; }

    /// <summary>What can be created in.</summary>
    public IReadOnlyList<League> All { get; }

    /// <summary>Why a folder's league did not load.</summary>
    public IReadOnlyList<string> Errors { get; }

    public LeagueCatalog(IEnumerable<League> leagues, IReadOnlyList<string>? errors = null)
    {
        var errs = new List<string>(errors ?? []);
        var list = leagues.ToList();
        var byName = list.ToDictionary(l => l.Name, StringComparer.OrdinalIgnoreCase);
        bool Rooted(League l)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (League? at = l; at is not null; at = at.Parent is { } p && byName.TryGetValue(p, out var next) ? next : null)
            {
                if (!seen.Add(at.Name)) return false;
                if (at.Parent is null) return at.Name.Equals(Standard, StringComparison.OrdinalIgnoreCase) || at.Name.Equals(HardcoreDeathRules.VoidLeague, StringComparison.OrdinalIgnoreCase);
                if (!byName.ContainsKey(at.Parent)) return false;
            }
            return false;
        }
        var good = new List<League>();
        foreach (var l in list)
        {
            if (!Rooted(l)) { errs.Add($"{l.Name}: its parents do not lead to Standard or Void"); continue; }
            if (l.AddModules.Distinct(StringComparer.OrdinalIgnoreCase).Count() != l.AddModules.Count) { errs.Add($"{l.Name}: a module is listed twice"); continue; }
            good.Add(l);
        }
        Loaded = good;
        Errors = errs;
        All = [.. good.Where(l => l.Creatable && !l.Ended)];
    }

    /// <summary>The leagues in a folder: each a subfolder with a league.json, and Source/ or Binaries/.</summary>
    public static LeagueCatalog FromFolder(string folder)
    {
        var leagues = new List<League>();
        var errors = new List<string>();
        foreach (string dir in Directory.Exists(folder) ? Directory.GetDirectories(folder) : [])
        {
            string path = Path.Combine(dir, "league.json");
            // A league exists as Source or as Binaries.
            if (!File.Exists(path) || (!Directory.Exists(Path.Combine(dir, "Source")) && !Directory.Exists(Path.Combine(dir, "Binaries")))) continue;
            try { leagues.Add(Read(Path.GetFileName(dir), path)); }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IOException) { errors.Add($"{Path.GetFileName(dir)}: {e.Message}"); }
        }
        return new(leagues.OrderBy(l => !l.Name.Equals(Standard, StringComparison.OrdinalIgnoreCase)), errors);
    }

    static League Read(string folderName, string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var r = doc.RootElement;
        string? Text(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        bool Flag(string n, bool or = false) => r.TryGetProperty(n, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : or;
        string[] List(string n) => r.TryGetProperty(n, out var v) ? [.. v.EnumerateArray().Select(e => e.GetString()!)] : [];
        string name = Text("name") ?? folderName;
        string? entryOf = Text("entryOf"), kind = Text("entryKind");
        return new League(name, Text("base") ?? name, Flag("hardcore"), Flag("ssf"), Text("form"),
            SsfParent: kind == "ssf" ? entryOf ?? "" : "", HardcoreParent: kind == "hardcore" ? entryOf ?? "" : "", FormParent: kind == "form" ? entryOf ?? "" : "",
            PermanentDeath: string.Equals(Text("deathLeague"), HardcoreDeathRules.VoidLeague, StringComparison.OrdinalIgnoreCase))
        {
            Parent = Text("parent"), EntryOf = entryOf, CardText = Text("description"), DeathLeague = Text("deathLeague"),
            Creatable = Flag("creatable", true), Ended = Flag("ended"), DataSet = Text("dataSet"), CoreModules = Flag("core", true),
            AddModules = List("addModules"), RemoveModules = List("removeModules"),
        };
    }

    /// <summary>The leagues in the configured folder.</summary>
    public static LeagueCatalog FromEnvironment() => FromFolder(Environment.GetEnvironmentVariable("Game_LEAGUES_FOLDER") ?? "Leagues");

    /// <summary>The leagues as they were when this server started: leagues come and go only with a restart.</summary>
    public static LeagueCatalog Startup => _startup.Value;
    static readonly Lazy<LeagueCatalog> _startup = new(FromEnvironment);

    /// <summary>The stash key of an account in a league: every league has its own stash and gold. Standard (and a
    /// character without a league) keeps the account's own key, so the stash it had before leagues counted stays there.</summary>
    public static string StashKey(string account, string? league) =>
        string.IsNullOrEmpty(league) || league.Equals(Standard, StringComparison.OrdinalIgnoreCase) ? account : $"{account} ({league})";

    /// <summary>Whether a saved character of this league can be played: not one with no folder, an ended one or Void.</summary>
    public bool CanEnter(string? name) => ResolveSavedLeague(name) is { Loaded: true, Ended: false };

    /// <summary>A league that can be created in.</summary>
    public League? Find(string? name) =>
        name is null ? null : All.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A loaded league by name.</summary>
    public League? FindLoaded(string? name) =>
        name is null ? null : Loaded.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A saved character's league. One with no folder keeps its name (the ticket and the stash go by it) but is not
    /// <see cref="League.Loaded"/>: the character can be listed and not entered, until the folder is back.</summary>
    public League? ResolveSavedLeague(string? name) => name is null ? null : FindLoaded(name)
        ?? new League(name, name, false, false) { Loaded = false };

    public IReadOnlyList<LeagueEntry> Entries() => [.. All.Select(l => l.Entry())];
}
