using System.Security.Cryptography;
using GameServer.Data.Ggpk;
using GgDump;

/// <summary>Explicit read-only admission against an already installed, reviewed graph. Never installs/replaces files.</summary>
static class VerifiedGamblerGraph
{
    internal const string Normal = "82D82CD3CD2DAD81606AF0DA9D18FCD53B2E92CF006550947D65A4F7E102DC32";
    internal const string Combined = "F2766B5A4618CDBE7E2993973B4D653F155F71BDFE0E5D063A425196CC93D638";
    // Aristocrat, Polytheist and Surfcaster admit against the same reviewed graph; className only names the refusal.
    internal static void Verify(ReadOnlySpan<byte> ordinary, ReadOnlySpan<byte> alternate, string className = "Gambler")
    {
        if (Convert.ToHexString(SHA256.HashData(ordinary)) != Normal
            || Convert.ToHexString(SHA256.HashData(alternate)) != Combined)
            throw new AscendancyRuleException($"{className} needs the separately reviewed 3.29.3b combined passive graph. "
                + $"Read-only verification failed; see Agents/docs/game/{className.ToLowerInvariant().Replace(' ', '-')}.md");
    }

    /// <summary>Tests: the two graphs' bytes in place of the installed archive's (the admission is checked without the
    /// 66 GB Client/Content.ggpk). Null in production.</summary>
    internal static Func<(byte[] Ordinary, byte[] Alternate)>? InstalledGraphs;

    public static void RequireInstalled(string className = "Gambler")
    {
        if (InstalledGraphs is { } installed)
        {
            var (ordinary, alternate) = installed();
            Verify(ordinary, alternate, className);
            return;
        }
        try
        {
            // Use the same decoder as read-only extraction; the target's Mermaid chunks exceed the managed decoder.
            // Failure is a refusal, never a fallback to unverified exported JSON.
            using var decoder = new NodeOodleDecoder("node", Path.GetFullPath("Tools/ooz/ooz_worker.mjs"));
            using var fs = GameFileSystem.Open("Client/Content.ggpk", decoder);
            Verify(fs.ReadFile("metadata/passiveskillgraph.psg"),
                fs.ReadFile("metadata/alternateskillgraphs/customascendancypassiveskillgraph.psg"), className);
        }
        catch (AscendancyRuleException) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException
            or InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            throw new AscendancyRuleException($"{className} installed-graph verification failed: " + ex.Message);
        }
    }
}
