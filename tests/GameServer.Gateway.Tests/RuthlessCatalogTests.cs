using System.Text.Json;
using GameServer.Core.Protocol.Instance;

namespace GameServer.Gateway.Tests;

public class RuthlessCatalogTests
{
    sealed class CatalogData : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "localexile-mode-catalog-" + Guid.NewGuid().ToString("N"));

        public CatalogData()
        {
            Directory.CreateDirectory(Path.Combine(Folder, "tables"));
            Write("Stats", new[] { new { Id = "stat_regular", IsLocal = false }, new { Id = "stat_alternate", IsLocal = true } });
            Write("Tags", new[] { new { Id = "default" } });
        }

        public void Write(string name, object rows) =>
            File.WriteAllText(Path.Combine(Folder, "tables", name + ".json"), JsonSerializer.Serialize(rows));

        public void Dispose() => Directory.Delete(Folder, recursive: true);
    }

    static object Node(int graph, bool alternate, int[] hardmodeStats, string hardmodeName = "") => new
    {
        PassiveSkillGraphId = graph, Id = "Node" + graph, Name = "Regular name", Stats = new[] { 0 },
        Stat1Value = 10, Stat2Value = 0, Stat3Value = 0, Stat4Value = 0, Stat5Value = 0,
        Characters = Array.Empty<int>(), SkillPointsGranted = 2, AscendancyKey = (int?)null,
        MasteryGroup = (int?)null, IsExpansion = false, IsMultipleChoice = false,
        HasHardmodeAlternate = alternate, NameHardmode = hardmodeName, StatsHardmode = hardmodeStats,
        Stat1ValueHardmode = 0, Stat2ValueHardmode = 0, Stat3ValueHardmode = 0, Stat4ValueHardmode = 0, Stat5ValueHardmode = 0,
        SkillPointsGrantedHardmode = 0,
    };

    static object Mod(string id, int hash, bool alternate) => new
    {
        Id = id, HASH16 = hash, Name = id, Domain = 1, GenerationType = 1, Level = 1,
        StatsKey1 = 1, StatsKey2 = (int?)null, Stat1Min = 5, Stat1Max = 10,
        HasHardmodeStatValues = alternate, Stat1MinHardmode = 0, Stat1MaxHardmode = 0,
        Families = new[] { hash }, SpawnWeight_TagsKeys = new[] { 0 }, SpawnWeight_Values = new[] { 1000 },
        GenerationWeight_TagsKeys = Array.Empty<int>(), GenerationWeight_Values = Array.Empty<int>(),
        IsEssenceOnlyModifier = false,
    };

    [Fact]
    public void Simultaneous_trees_use_their_own_stat_vectors_points_and_names()
    {
        using var data = new CatalogData();
        data.Write("PassiveSkills", new[] { Node(1, true, [1], "Ruthless name") });
        var regular = new PassiveTree(data.Folder);
        var ruthless = new PassiveTree(data.Folder, hardMode: true);

        Assert.Equal(new Dictionary<uint, int> { [1] = 10 }, regular.Stats([1]));
        Assert.Equal(new Dictionary<uint, int> { [2] = 0 }, ruthless.Stats([1]));
        Assert.Equal(2, regular.PointsGranted([1]));
        Assert.Equal(0, ruthless.PointsGranted([1]));
        Assert.Equal("Regular name", regular.Find(1)!.Name);
        Assert.Equal("Ruthless name", ruthless.Find(1)!.Name);
        // Loading the other mode must not modify a previously read regular catalog.
        Assert.Equal(10, regular.Stats([1])[1]);
    }

    static string Data()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, "data", "tables"))) return Path.Combine(dir.FullName, "data");
        throw new DirectoryNotFoundException("data/tables");
    }

}
