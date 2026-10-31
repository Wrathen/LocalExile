using GameServer.Core.Accounts;
using GameServer.Storage;

namespace GameServer.Gateway.Tests;

public class HardcoreDeathPersistenceTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), $"hardcore-death-{Guid.NewGuid():N}");
    string DbPath => Path.Combine(_folder, "saves.db");
    static readonly DateTimeOffset DiedAt = DateTimeOffset.Parse("2026-10-26T12:00:00Z");

    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); }

    static Character Seed(SaveDatabase db, string league = "HC SSF Allflame")
    {
        var c = new Character(Guid.NewGuid(), Guid.NewGuid(), "DeathProbe", CharacterClass.Witch, 12, league,
            "1_1_2", DiedAt.AddDays(-1), DiedAt, "1_1_town");
        new SqliteAccountStore(db).AddCharacter(c);
        db.Put(SaveDatabase.Table.Characters, c.Name, "{\"carriedItems\":[1,2],\"xp\":120}");
        db.Put(SaveDatabase.Table.Stashes, "account@HC SSF Allflame", "source stash and gold");
        db.Put(SaveDatabase.Table.Stashes, "account@SSF Allflame", "destination stash and gold");
        return c;
    }

    [Fact]
    public void Death_and_carried_progress_survive_restart_without_moving_either_stash()
    {
        Character original;
        using (var db = new SaveDatabase(DbPath))
        {
            original = Seed(db);
            var dead = HardcoreDeathPersistence.Commit(db, original.Name, LeagueCatalog.FromEnvironment().Find(original.League)!,
                () => db.Put(SaveDatabase.Table.Characters, original.Name, "{\"carriedItems\":[1,2,3],\"xp\":150}"), DiedAt);
            Assert.Equal("SSF Allflame", dead.League);
        }
        using var again = new SaveDatabase(DbPath);
        var store = new SqliteAccountStore(again);
        Assert.Equal("SSF Allflame", store.FindCharacter(original.Name)!.League);
        Assert.Equal(new HardcoreDeathRecord(original.League, DiedAt, false), store.FindCharacter(original.Name)!.HardcoreDeath);
        Assert.Equal("{\"carriedItems\":[1,2,3],\"xp\":150}", again.Get(SaveDatabase.Table.Characters, original.Name));
        Assert.Equal("source stash and gold", again.Get(SaveDatabase.Table.Stashes, "account@HC SSF Allflame"));
        Assert.Equal("destination stash and gold", again.Get(SaveDatabase.Table.Stashes, "account@SSF Allflame"));
        // Auth may have read this snapshot before the Gateway committed death.
        store.UpdateCharacter(original with { Level = 13, Portrait = 7 });
        Assert.Equal(("SSF Allflame", 13, (ushort)7),
            (store.FindCharacter(original.Name)!.League, store.FindCharacter(original.Name)!.Level, store.FindCharacter(original.Name)!.Portrait));
        Assert.NotNull(store.FindCharacter(original.Name)!.HardcoreDeath);
    }

}
