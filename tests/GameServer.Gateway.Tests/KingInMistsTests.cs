using GameServer.Core.Protocol.Instance;
using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

public class KingInMistsTests
{
    static readonly Lazy<string> DataFolder = new(() => BenchCraftingTests.Data());
    static readonly Lazy<MonsterCatalog> Monsters = new(() => new MonsterCatalog(DataFolder.Value));

    [Fact]
    public void Level_83_map_boss_scaling_matches_the_king_in_the_mists_life()
    {
        var boss = Monsters.Value.Find(KingInMists.Boss2);
        Assert.NotNull(boss);
        Assert.True(KingInMists.IsBoss2(boss));
        Assert.False(KingInMists.IsBoss3(boss));

        var scaling = Monsters.Value.MapBossScaling(83);
        Assert.Equal(20.73, scaling.Life, 2);
        Assert.Equal(1.15, scaling.Damage, 2);

        int baseLife = Monsters.Value.BaseLife(boss!, 83)!.Value;
        Assert.Equal(275_742, baseLife);
        long life = (long)Math.Round(MonsterCatalog.LifeWithRarity(baseLife, MonsterRarity.Unique) * scaling.Life);
        Assert.InRange(life, 45_614_000, 45_615_000);
    }

    [Fact]
    public void The_treant_is_a_separate_supported_form_with_its_own_health_pool()
    {
        var treant = Monsters.Value.Find(KingInMists.Boss3);
        Assert.NotNull(treant);
        Assert.True(KingInMists.IsBoss3(treant));
        Assert.NotEqual(KingInMists.Boss2, treant!.Id);
        Assert.True(treant.BossHealthBar);

        int baseLife = Monsters.Value.BaseLife(treant, 83)!.Value;
        long life = (long)Math.Round(MonsterCatalog.LifeWithRarity(baseLife, MonsterRarity.Unique) *
            Monsters.Value.MapBossScaling(83).Life);
        Assert.InRange(life, 26_608_000, 26_609_000);
    }

    [Fact]
    public void The_humanoid_cannot_die_before_the_treant_takes_over()
    {
        var monster = new SpawnedMonster("The King in the Mists", new WalkEstimator(10, 10, 30, null, 300, 300), 135, 4, 1000)
        {
            Life = 10, KingPhaseLocked = true,
        };
        var (life, starts) = Program.TakeDamageLocked(monster, 500);
        Assert.Equal(1u, life);
        Assert.False(starts);
    }

    [Fact]
    public void The_unique_drop_pool_always_gives_exactly_one_of_the_four_uniques()
    {
        var items = new ItemCatalog(DataFolder.Value);
        var uniques = new UniqueCatalog(items, DataFolder.Value);
        var drops = new ItemDrops(items, new ItemRolls(items), DataFolder.Value, uniques: uniques);
        var counts = new Dictionary<uint, int>();
        var extras = new HashSet<string>(StringComparer.Ordinal);
        var random = new Random(12345);
        for (int i = 0; i < 1000; i++)
        {
            var rolled = drops.KingInMists(83, random);
            var unique = rolled.Where(d => d.Item.Rarity == ItemRarity.Unique).ToList();
            Assert.Single(unique);
            counts[unique[0].Item.TypeHash] = counts.GetValueOrDefault(unique[0].Item.TypeHash) + 1;
            foreach (var drop in rolled.Where(d => d.Item.Rarity != ItemRarity.Unique)) extras.Add(drop.Type.Name);
        }
        Assert.Equal(4, counts.Count);
        Assert.All(counts.Values, count => Assert.True(count > 0));
        Assert.Contains("Hextoad Support", extras);
        Assert.Contains("Hexpass Support", extras);
        Assert.Contains("The Endless Darkness", extras);
    }
}
