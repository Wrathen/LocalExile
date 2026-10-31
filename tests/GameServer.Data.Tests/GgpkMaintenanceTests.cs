using GameServer.Data.Ggpk;

namespace GameServer.Data.Tests;

public class GgpkMaintenanceTests
{
    /// <summary>scripts/dev/live.ps1 reads the gateway list (GameServer.Patch gateway) while other checkouts' clients run.
    /// A client opens Content.ggpk for reading and shares it only with readers; reading the list must still work. Uses
    /// the local client's GGPK (opened read-only, never written); without one there is nothing to check.</summary>
    [Fact]
    public void ReadsAFileWhileAClientHoldsTheGgpk()
    {
        string? ggpk = FindUp(Path.Combine("Client", "Content.ggpk"));
        if (ggpk is null) return;
        using var client = new FileStream(ggpk, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[]? list = GgpkMaintenance.TryReadFile(ggpk, "Bundles2/gateway_list.txt") ?? GgpkMaintenance.TryReadFile(ggpk, "gateway_list.txt");
        Assert.NotNull(list);
        Assert.Equal(32, GgpkMaintenance.RootHash(ggpk).Length);
    }

    static string? FindUp(string relative)
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, relative))) return Path.Combine(dir.FullName, relative);
        return null;
    }
}
