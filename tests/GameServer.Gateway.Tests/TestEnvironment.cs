using System.Runtime.CompilerServices;

namespace GameServer.Gateway.Tests;

static class TestEnvironment
{
    static readonly GameServer.Modules.Ruthless.RuthlessRulesSource Ruthless = new(Path.Combine("config", "ruthless.json"));

    // Real dispatch tests use Program's production catalogs, whose paths are relative to the repository root.
    // Set this once before parallel tests or lazy catalogs initialize, rather than changing process CWD in a test.
    [ModuleInitializer]
    internal static void Initialize()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory(),
            Path.GetDirectoryName(SourceFile())! })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))
                    && File.Exists(Path.Combine(dir.FullName, "src", "GameServer.Gateway", "GameServer.Gateway.csproj"))
                    && Directory.Exists(Path.Combine(dir.FullName, "data", "tables")))
                {
                    Directory.SetCurrentDirectory(dir.FullName);
                    // The modules' code is not loaded here; Ruthless's rules are what its leagues' tests read.
                    ServerLeagues.AddRules("ruthless", () => Ruthless.Rules);
                    return;
                }
        throw new DirectoryNotFoundException("Gateway tests need a LocalExile checkout with data/tables.");
    }

    // Isolated --artifacts-path output can be a sibling of the checkout, with no repository among its ancestors.
    static string SourceFile([CallerFilePath] string path = "") => path;
}
