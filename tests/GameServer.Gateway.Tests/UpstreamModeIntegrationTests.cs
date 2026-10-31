using System.Reflection;
using GameServer.Core.Accounts;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.World;
using GameServer.Storage;

namespace GameServer.Gateway.Tests;

public class UpstreamModeIntegrationTests
{
    [Theory]
    [InlineData("Hardcore", "Standard", false, false)]
    public void A_lethal_development_contact_commits_the_mode_death_before_returning(
        string source, string destination, bool ssf, bool ruthless)
    {
        TestSaves.Ensure();
        var now = DateTimeOffset.UtcNow;
        string name = "DevContactMode" + Guid.NewGuid().ToString("N");
        var character = new Character(Guid.NewGuid(), Guid.NewGuid(), name, CharacterClass.Witch, 1,
            source, "1_1_2", now, now);
        var store = new SqliteAccountStore(Saves.Db);
        store.AddCharacter(character);
        var conn = new ClientState(new(ushort.MaxValue, "ModeIntegration", 128, 128, 32, 32, null, []), 0, 1)
        {
            Character = name, CharacterId = character.Id, ClassRow = 1, League = source,
            Hardcore = true, Ssf = ssf, LeagueForm = ruthless ? "ruthless" : null, PlayerLife = 1, PlayerMana = 100,
        };
        conn.Pools = Program.PoolsFor(conn, 1);
        conn.PlayerLife = 1;
        conn.RecentEnemyHits.Record(conn.Clock.Elapsed.TotalSeconds);
        object?[] args = [conn, DamageType.Physical, 100_000, false, false, false, null, false];
        try
        {
            bool accepted = (bool)typeof(Program).GetMethod("TryDevEnemyHit",
                BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;

            Assert.True(accepted);
            Assert.Equal(0u, conn.PlayerLife);
            Assert.True(conn.HardcoreDeathRecorded);
            Assert.Equal(0, conn.RecentEnemyHits.Count(conn.Clock.Elapsed.TotalSeconds));
            var saved = store.FindCharacter(name)!;
            Assert.Equal(destination, saved.League);
            Assert.NotNull(saved.HardcoreDeath);
            Assert.Equal(source, saved.HardcoreDeath.SourceLeague);
            Assert.Equal(ruthless, saved.HardcoreDeath.PermanentlyDead);
            // Another command reaches admission after the durable terminal outcome and cannot mutate the old owner.
            Assert.False((bool)typeof(Program).GetMethod("TryDevEnemyHit",
                BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!);
            Assert.Equal(saved, store.FindCharacter(name));
        }
        finally { PlayerInventories.Forget(name); }
    }

    static ClientState Connection(string character, bool ruthless) =>
        new(new(ushort.MaxValue, "ModeIntegration", 128, 128, 32, 32, null, []), 0, 1)
        { Character = character, ClassRow = 1, LeagueForm = ruthless ? "ruthless" : null, PlayerLife = 100, PlayerMana = 100 };
}
