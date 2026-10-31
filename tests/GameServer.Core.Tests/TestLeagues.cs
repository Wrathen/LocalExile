using System.Runtime.CompilerServices;

namespace GameServer.Core.Tests;

/// <summary>The repository's Leagues/ folder, as the servers read it from the repository root.</summary>
static class TestLeagues
{
    [ModuleInitializer]
    internal static void Initialize() =>
        Environment.SetEnvironmentVariable("Game_LEAGUES_FOLDER", Path.Combine(Path.GetDirectoryName(Here())!, "..", "..", "Leagues"));

    static string Here([CallerFilePath] string path = "") => path;
}
