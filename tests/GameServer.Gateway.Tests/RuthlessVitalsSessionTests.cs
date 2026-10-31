using GameServer.Core.Accounts;
using GameServer.Core.Protocol.Instance;
using GameServer.Storage;

namespace GameServer.Gateway.Tests;

[Collection(TestSaves.FailWriteCollection)]
public sealed class RuthlessVitalsSessionTests
{
    [Fact]
    public void A_closed_old_connection_cannot_replace_the_current_owners_pools()
    {
        using var f = new Character();
        var old = f.Admit();
        var current = f.Admit();
        Set(current, 10, 7);
        Program.PersistVitals(current);
        Set(old, 80, 50);
        old.Closed = true; // The normal disconnect path saves after marking its connection closed.

        Program.PersistVitals(old);
        Program.ForgetVitalsAdmission(old);

        f.AssertPools(10, 7);
        Assert.Equal(current.VitalsSessionId, f.Saved.VitalsOwner!.SessionId);
        Set(current, 9, 6);
        Program.PersistVitals(current);
        f.AssertPools(9, 6);
    }

    static void Set(ClientState connection, uint life, uint mana) =>
        (connection.PlayerLife, connection.PlayerMana) = (life, mana);

    sealed class Character : IDisposable
    {
        public string Name { get; } = "VitalsSession" + Guid.NewGuid().ToString("N");
        public Guid Id = Guid.NewGuid();
        public SqliteAccountStore Store { get; }
        readonly List<ClientState> connections = [];
        public CharacterSaves.Save Saved => CharacterSaves.Load(Name)!;
        public string RawSave => Saves.Get(SaveDatabase.Table.Characters, Name)!;

        public Character()
        {
            TestSaves.Ensure();
            Store = new(Saves.Db);
            AddAccountCharacter();
            CharacterSaves.Write(Name, new([], Life: 40, Mana: 20));
        }

        public void AddAccountCharacter()
        {
            var now = DateTimeOffset.UtcNow;
            Store.AddCharacter(new(Id, Guid.NewGuid(), Name, CharacterClass.Witch, 10, "Ruthless", "1_1_2", now, now));
        }

        public ClientState Connection()
        {
            var conn = new ClientState(new(ushort.MaxValue, "VitalsSession", 128, 128, 32, 32, null, []), 0, 1)
            {
                Character = Name, CharacterId = Id, ClassRow = 1, League = "Ruthless", LeagueForm = "ruthless",
                Pools = new(10, 100, 80, 0, 0), PlayerLife = 100, PlayerMana = 80,
            };
            connections.Add(conn);
            return conn;
        }

        public ClientState Admit()
        {
            var conn = Connection();
            Program.RegisterVitalsAdmission(conn);
            Program.InitializeCharacterVitals(conn);
            return conn;
        }

        public void AssertPools(uint life, uint mana) => Assert.Equal((life, mana), (Saved.Life, Saved.Mana));

        public void Dispose()
        {
            foreach (var conn in connections) Program.ForgetVitalsAdmission(conn);
            PlayerInventories.Forget(Name);
            Store.DeleteCharacter(Id);
        }
    }
}
