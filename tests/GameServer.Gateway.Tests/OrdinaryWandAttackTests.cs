using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using GameServer.Core.Projectiles;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

public class OrdinaryWandAttackTests
{
    // Observe the real serialized write while the send gate still belongs to it. A buffered reader may resume long
    // after contact, so its continuation is not a valid witness of the target's life at projectile creation.
    sealed class ObservedStream(Socket socket) : NetworkStream(socket, ownsSocket: false)
    {
        readonly ConnectionCiphers observer = new(new byte[64], isClient: true);
        public readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> Writes = new();
        public Action<byte[]>? AfterWrite;

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            byte[] plain = buffer.ToArray();
            observer.Receive.Process(plain);
            await base.WriteAsync(buffer, cancellationToken);
            Writes.Enqueue(plain);
            AfterWrite?.Invoke(plain);
        }
    }

    sealed class Wire : IDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly ConnectionCiphers incoming = new(new byte[64], isClient: true);
        public readonly TcpClient Writer = new(), Reader;
        public readonly ConnectionCiphers Ciphers = new(new byte[64], isClient: false);
        public readonly ObservedStream Stream;

        public Wire()
        {
            listener.Start();
            var accepting = listener.AcceptTcpClientAsync();
            Writer.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            Reader = accepting.GetAwaiter().GetResult();
            Stream = new(Writer.Client);
        }

        public async Task<byte[]> Read(int count, CancellationToken cancellation)
        {
            byte[] bytes = new byte[count];
            await Reader.GetStream().ReadExactlyAsync(bytes, cancellation);
            incoming.Receive.Process(bytes);
            return bytes;
        }

        public void Dispose() { Stream.Dispose(); Writer.Dispose(); Reader.Dispose(); listener.Stop(); }
    }

    static (ClientState Conn, string Character) EquippedWand()
    {
        TestSaves.Ensure();
        string character = $"Wand{Guid.NewGuid():N}";
        var conn = new ClientState(new AreaLayout(1, "wand-test", 300, 300, 10, 10, null, []), 1, 1)
        {
            Character = character, PlayerLife = 100, ActionCount = 7,
            Pools = new CharacterStats.Pools(1, 100, 100, int.MaxValue, 0),
        };
        var type = Assert.IsType<ItemCatalog.BaseType>(Program.Items.Find("Driftwood Wand"));
        PlayerInventories.For(character, Program.Items).Add(PlayerInventories.Weapon1, 0, 0,
            new(type.Hash, type.Components, type.VisualIdentity));
        return (conn, character);
    }

    static SpawnedMonster Target(int x) => new("target", new(x, 10, 10, width: 300, height: 300), 0, 2, 1000);

    static bool IsWandCreate(ReadOnlySpan<byte> plain) => plain.Length >= 16
        && BinaryPrimitives.ReadUInt16BigEndian(plain) == InstanceEntities.CreateObjectId
        && BinaryPrimitives.ReadUInt32BigEndian(plain[12..]) == InstanceEntities.ObjectTypeHash(OrdinaryRangedAttack.Wand);

    // Concatenated runtime output has no framing: find the bolt's creation by its message id and type hash.
    static uint? CreatedWand(byte[] bytes)
    {
        for (int i = 0; i + 16 <= bytes.Length; i++)
            if (IsWandCreate(bytes.AsSpan(i))) return BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(i + 2));
        return null;
    }

    [Fact]
    public void Ordinary_projectile_selection_and_wand_release_use_the_extracted_native_assets()
    {
        var wand = new WeaponProfile(false, false, false, false, false, Wand: true);
        Assert.Equal(OrdinaryRangedAttack.Wand, OrdinaryRangedAttack.ProjectileId(wand));
        Assert.Equal(OrdinaryRangedAttack.Arrow, OrdinaryRangedAttack.ProjectileId(wand with { Wand = false, Bow = true }));
        Assert.Null(OrdinaryRangedAttack.ProjectileId(wand with { Wand = false, Unarmed = true, Melee = true }));
        var projectile = Program.Skills.Projectile(OrdinaryRangedAttack.Wand);
        Assert.NotNull(projectile);
        Assert.Equal(3260, projectile.Value.Speed);
        string ao = Program.Skills.ClassFor(SkillCatalog.WitchRow).AnimatedObject;
        var animations = new AnimationContacts();
        Assert.Equal(0.5, animations.AttackContact(ao, wand), 6); // attack1_wand 300/600 ms.
        Assert.Equal(0.5, animations.AttackContact(ao, wand with { Shield = true }), 6);
        Assert.Equal(0.5, animations.AttackContact(ao, wand with { DualWield = true }), 6);
    }

    [Fact]
    public async Task Object_default_wand_use_aims_at_the_target_but_hits_the_first_actual_collision()
    {
        var (conn, character) = EquippedWand();
        using var wire = new Wire();
        var selected = Target(110);
        var intervening = Target(60);
        conn.Monsters.Add(42, selected);
        conn.Monsters.Add(43, intervening);
        var created = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var damaged = new TaskCompletionSource<(uint Entity, uint Life)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var impacted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var removed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        uint projectileId = 0;
        wire.Stream.AfterWrite = plain =>
        {
            if (plain.Length < 6) return;
            ushort message = BinaryPrimitives.ReadUInt16BigEndian(plain);
            uint entity = BinaryPrimitives.ReadUInt32BigEndian(plain.AsSpan(2));
            if (IsWandCreate(plain)) { projectileId = entity; created.TrySetResult(entity); }
            if (message == InstanceProtocol.LifeUpdateId && entity is 42 or 43 && plain.Length >= 16)
                damaged.TrySetResult((entity, BinaryPrimitives.ReadUInt32BigEndian(plain.AsSpan(12))));
            // The stop (0x1b7) and the rig's impact clip (0x1b2) go out in one write at the contact cell.
            if (projectileId != 0 && plain.AsSpan().IndexOf(InstanceProtocol.AnimationUpdate(projectileId, 0,
                OrdinaryRangedAttack.WandImpactAnimation, 1f)) >= 0)
                impacted.TrySetResult();
            if (projectileId != 0 && plain.AsSpan().SequenceEqual(InstanceProtocol.RemoveObject(projectileId)))
                removed.TrySetResult(impacted.Task.IsCompleted);
        };
        try
        {
            await Program.Attack("wand-test", wire.Stream, wire.Ciphers, conn,
                new InstanceTransfer.UseSkillOnObject(42, SkillCatalog.FirstSkillId, 0, 0), selected, 0).WaitAsync(TimeSpan.FromSeconds(5));
            byte[] action = InstanceProtocol.ActionStarted(Program.PlayerId, 10, 10, 110, 10,
                SkillCatalog.FirstSkillId, 0, 0, targetObjectId: 42);
            Assert.Contains(wire.Stream.Writes, p => p.AsSpan().SequenceEqual(action));
            await created.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var (entity, life) = await damaged.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(43u, entity);
            Assert.True(life < 1000);
            Assert.Equal(1000u, selected.Life);
            Assert.True(intervening.Life < 1000);
            await impacted.Task.WaitAsync(TimeSpan.FromSeconds(5)); // The rig's native impact clip, with its own sound/particles.
            Assert.True(await removed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            lock (conn.Monsters) Assert.Empty(conn.ProjectileObjects);
        }
        finally { conn.Closed = true; PlayerInventories.Forget(character); }
    }

    [Theory]
    [InlineData("move")]
    [InlineData("death")]
    [InlineData("transfer")]
    public async Task A_default_wand_attack_cancelled_before_its_native_release_creates_no_projectile(string cancellation)
    {
        var (conn, character) = EquippedWand();
        using var wire = new Wire();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var target = Target(110);
        conn.Monsters.Add(42, target);
        wire.Stream.AfterWrite = plain =>
        {
            if (plain.Length < 2 || BinaryPrimitives.ReadUInt16BigEndian(plain) != InstanceProtocol.ActionStartedId) return;
            lock (conn.Monsters)
            {
                if (cancellation == "move") conn.ActionCount++;
                if (cancellation == "death") conn.PlayerLife = 0;
                if (cancellation == "transfer") conn.TryBeginAreaChange(new PendingTransfer(2, 0, false), out _);
            }
        };
        try
        {
            var attack = Program.Attack("wand-test", wire.Stream, wire.Ciphers, conn,
                new InstanceTransfer.UseSkillOnObject(42, SkillCatalog.FirstSkillId, 0, 0), target, 0);
            int actionLength = InstanceProtocol.ActionStarted(Program.PlayerId, 10, 10, 110, 10,
                SkillCatalog.FirstSkillId, 0, 0, targetObjectId: 42).Length;
            await wire.Read(actionLength, timeout.Token);
            await attack.WaitAsync(TimeSpan.FromSeconds(5));
            // The release was scheduled at acceptance, at the wand's contact (well under a second): wait past it.
            await Task.Delay(TimeSpan.FromSeconds(1.5));
            Assert.Equal(1000u, target.Life);
            Assert.DoesNotContain(wire.Stream.Writes, p => IsWandCreate(p));
            lock (conn.Monsters) Assert.Empty(conn.ProjectileObjects);
            Assert.Equal(0, wire.Reader.Available);
        }
        finally { conn.Closed = true; PlayerInventories.Forget(character); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Wand_impact_tail_uses_game_time_and_transfer_invalidates_its_pending_removal(bool transfer)
    {
        await using var f = new ProjectileRuntimeFixture();
        f.Conn.Monsters.Add(42, ProjectileRuntimeFixture.Monster(50, 10));
        var offense = new ProjectileOffense(DamagePacket.Of(DamageType.Physical, 10, 10), false, int.MaxValue, 0, 150, "ordinary wand")
            { Presentation = Program.OrdinaryWandPresentation };
        f.Release(f.Shot(new ProjectileProfile(OrdinaryRangedAttack.Wand)
            { Speed = 10, Range = 100, Radius = 1, Lifetime = ProjectileRuntime.Time(20) }, offense));
        // Let the creation reach the wire first: a visual whose projectile has already ended is never created.
        var created = CreatedWand(await f.Read());
        Assert.NotNull(created);
        uint id = created.Value;
        // At 10 cells/s the bolt reaches the body at x = 50 before 4 s; its impact tail lasts 0.5 s of game time after that.
        f.Advance(4.1);
        byte[] contact = await f.Read();
        Assert.True(contact.AsSpan().IndexOf(InstanceProtocol.AnimationUpdate(id, 0, OrdinaryRangedAttack.WandImpactAnimation, 1f)) >= 0);
        Assert.True(contact.AsSpan().IndexOf(InstanceProtocol.RemoveObject(id)) < 0);
        lock (f.Conn.Monsters) Assert.Contains(id, f.Conn.ProjectileObjects);
        // Game time stands still: however long the wall clock runs, the pending removal does not fire.
        await Task.Delay(TimeSpan.FromSeconds(OrdinaryRangedAttack.WandImpactSeconds + 0.1));
        Assert.Empty(await f.Read());

        if (transfer)
        {
            Assert.True(f.Conn.TryBeginAreaChange(new(2, 0, false), out var oldObjects));
            Assert.Contains(id, oldObjects);
            f.Advance(10);
            Assert.Empty(await f.ReadWire()); // The obsolete tail cannot write into the new area.
        }
        else
        {
            f.Advance(4.1 + OrdinaryRangedAttack.WandImpactSeconds + 0.1);
            Assert.True((await f.Read()).AsSpan().IndexOf(InstanceProtocol.RemoveObject(id)) >= 0);
        }
        lock (f.Conn.Monsters) Assert.Empty(f.Conn.ProjectileObjects);
    }
}
