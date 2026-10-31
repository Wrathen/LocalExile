using System.Net;
using System.Net.Sockets;
using System.Reflection;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

public class RageGatewayTests
{
    static object Call(string name, params object[] args) => typeof(Program)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;
    static void Set(ClientState conn, string stat, int value) => conn.PlayerStats[Program.Stats.Key(stat)!.Value] = value;
    static int Current(ClientState conn) => Program.CharacterPlayerStats(conn)[Program.Stats.Key("current_rage")!.Value];
    static RageState.Inputs Input(ClientState conn) => RageStats.Inputs(name =>
        Program.Stats.Key(name) is uint key ? Program.CharacterPlayerStats(conn).GetValueOrDefault(key) : 0);
    static DamagePacket Hit(ClientState conn, ushort skill) => (DamagePacket)Call("SupportedHit", conn, skill);
    static (ushort Id, int Effect, string Name, int Level, int Quality, uint Gem) ActiveGem(ClientState conn) =>
        ((IEnumerable<(ushort Id, int Effect, string Name, int Level, int Quality, uint Gem)>)typeof(Program)
            .GetMethod("GemSkillEffects", BindingFlags.Static | BindingFlags.NonPublic,
                binder: null, types: [typeof(ClientState)], modifiers: null)!.Invoke(null, [conn])!).Single();

    [Theory]
    [InlineData(15)]
    public void Actual_default_attack_panel_keeps_fixed_hundred_chaos_at_integer_Rage_boundaries(int held)
    {
        using var f = new Fixture(); f.Conn.Clock.Stop();
        Set(f.Conn, "attack_minimum_added_chaos_damage", 100);
        Set(f.Conn, "attack_maximum_added_chaos_damage", 100);
        f.Conn.Pools = Program.PoolsFor(f.Conn, 100);
        var inputs = Input(f.Conn);
        Assert.True(f.Conn.Rage.Gain(held, f.Conn.Clock.Elapsed.TotalSeconds, inputs));
        var actual = Program.CharacterPlayerStats(f.Conn);
        Assert.Equal(held, actual[Program.Stats.Key("effective_rage_for_per_rage_effects")!.Value]);
        Assert.Equal(100 + held, actual[Program.Stats.Key("main_hand_minimum_chaos_damage")!.Value]);
        Assert.Equal(100 + held, actual[Program.Stats.Key("main_hand_maximum_chaos_damage")!.Value]);
    }

    public static IEnumerable<object[]> DamageCases()
    {
        foreach (int held in Enumerable.Range(0, 46)) yield return [held, 0];
        foreach (var (held, effect) in new[] { (10, 50), (30, 50), (45, 50), (29, 100), (45, -50), (45, -100) })
            yield return [held, effect];
    }

    sealed class Fixture : IDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly TcpClient writer = new(), reader;
        readonly ConnectionCiphers receive = new(new byte[64], isClient: true);
        public readonly ConnectionCiphers Ciphers = new(new byte[64], isClient: false);
        public readonly ClientState Conn;
        public readonly SpawnedMonster Target;
        public readonly PlayerInventories Items;
        public NetworkStream Stream => writer.GetStream();
        public SemaphoreSlim Gate => Program.SendGates.GetValue(Ciphers, _ => new(1, 1));
        public ushort Skill => Program.GemSkillIdPool.Value[0];
        public Fixture(string? gem = null, string weapon = "Driftwood Club", bool rageSupport = false,
            uint targetLife = 100000)
        {
            TestSaves.Ensure();
            Conn = new(new AreaLayout(1, "test", 300, 300, 100, 100, null, []),
                (uint)Random.Shared.Next(1, int.MaxValue), 1)
            {
                Character = $"Rage{Guid.NewGuid():N}", PlayerLife = 1000, PlayerMana = 1000, PlayerSent = true,
                Pools = new(100, 1000, 1000, int.MaxValue, 0, Strength: 500, Dexterity: 500, Intelligence: 500),
                SentEnergyShield = 0, SentRechargeActive = false, SentEsRecoveryRate = 0
            };
            Items = PlayerInventories.For(Conn.Character, Program.Items);
            var host = ItemCatalog.Create(Program.Items.Find(weapon)!);
            if (gem is not null)
            {
                var active = ItemCatalog.Create(Program.Items.Find(gem)!);
                host = host with { Sockets = rageSupport
                    ? [new(SocketColour.White, active), new(SocketColour.White,
                        ItemCatalog.Create(Program.Items.Find("Metadata/Items/Gems/SupportGemRage")!))]
                    : [new(SocketColour.White, active)], Links = rageSupport ? [2] : [1] };
            }
            Items.Add(PlayerInventories.Weapon1, 0, 0, host);
            Set(Conn, "no_inherent_rage_loss", 1);
            Set(Conn, "global_cannot_crit", 1);
            Target = new("rage target", new(105, 100, 10, width: 300, height: 300), 0, 2, targetLife);
            Conn.Monsters.Add(42, Target);
            listener.Start(); var accepting = listener.AcceptTcpClientAsync();
            writer.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            reader = accepting.GetAwaiter().GetResult();
        }

        public Task Dispatch(bool ground = false, ushort? skill = null)
        {
            ushort id = ground ? InstanceProtocol.UseSkillAtLocationId : InstanceTransfer.UseSkillOnObjectId;
            var packet = ground
                ? new LoginWriter().U16(id).U32(105).U32(100).U16(skill ?? Skill).U16(1).U16(1).ToArray()
                : new LoginWriter().U16(id).U32(42).U16(skill ?? SkillCatalog.FirstSkillId).U16(1).U16(1).ToArray();
            return Program.OnClientMessages("rage-dispatch-test", Stream, Ciphers, [(id, packet)], [], Conn);
        }

        public Task Sweep()
        {
            var gem = ActiveGem(Conn);
            return (Task)typeof(Program).GetMethod("AreaSkillHit", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, ["rage-sweep-test", Stream, Ciphers, Conn, Skill, gem,
                    (100, 100), (105, 100), true, (ushort)1, Conn.ActionCount, null, null, 1.0, 0u, null, null, 0u])!;
        }

        public async Task ObserveLife()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var bytes = new List<byte>(); byte[] chunk = new byte[65536];
            while (true)
            {
                int count = await reader.GetStream().ReadAsync(chunk, timeout.Token);
                Assert.True(count > 0);
                receive.Receive.Process(chunk.AsSpan(0, count));
                bytes.AddRange(chunk.Take(count));
                if (bytes.ToArray().AsSpan().IndexOf(InstanceProtocol.LifeUpdate(42, Target.Life)) >= 0) return;
            }
        }

        public int Available => reader.Available;

        public async Task<byte[]> Receive(int count)
        {
            byte[] bytes = new byte[count];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await reader.GetStream().ReadExactlyAsync(bytes, timeout.Token);
            receive.Receive.Process(bytes);
            return bytes;
        }

        public void Dispose()
        {
            Conn.Closed = true; Conn.Clock.Start(); writer.Dispose(); reader.Dispose(); listener.Stop();
            PlayerInventories.Forget(Conn.Character!);
        }
    }

}
