using System.Net;
using System.Net.Sockets;
using System.Reflection;
using GameServer.Core.Projectiles;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

public partial class ConditionalCriticalGatewayTests
{
    sealed class Draws(params int[] values) : Random
    {
        public int Calls;
        public override int Next(int maxValue)
        {
            Assert.Equal(10000, maxValue);
            int index = Interlocked.Increment(ref Calls) - 1;
            Assert.True(index < values.Length, "Unexpected critical draw");
            return values[index];
        }
    }

    static MethodInfo Method(string name) => typeof(Program).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;

    sealed class Contact : IDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly TcpClient writer = new(), reader;
        readonly ConnectionCiphers ciphers = new(new byte[64], isClient: false);
        readonly ConnectionCiphers received = new(new byte[64], isClient: true);
        readonly string character = "Critical" + Guid.NewGuid().ToString("N");
        public ClientState Conn { get; }
        public SpawnedMonster Target { get; }
        public ushort Skill = SkillCatalog.FirstSkillId;
        public SemaphoreSlim SendGate => Program.SendGates.GetValue(ciphers, _ => new(1, 1));

        public Contact(Draws random, string? gemName = null, string? weaponName = null, int distance = 2,
            uint targetLife = 100000, MonsterModSet? targetMods = null, byte targetLevel = 1)
        {
            TestSaves.Ensure();
            Conn = new(new(ushort.MaxValue, "CriticalContact", 200, 200, 32, 32, (_, _) => true, []), 1, 1)
            { Character = character, ClassRow = 1, PlayerSent = true, PlayerLife = 50, PlayerMana = 1000,
                CriticalRandom = random, Instance = new InstanceState() };
            var inventory = PlayerInventories.For(character, Program.Items);
            if (weaponName is not null)
                inventory.Add(PlayerInventories.Weapon1, 0, 0, ItemCatalog.Create(Program.Items.Find(weaponName)!));
            if (gemName is not null)
            {
                var gem = ItemCatalog.Create(Program.Items.Find("Metadata/Items/Gems/SkillGem" + gemName)!);
                var support = ItemCatalog.Create(Program.Items.Find("Metadata/Items/Gems/SupportGemIncreasedCriticalDamage")!);
                var host = ItemCatalog.Create(Program.Items.Find("Metadata/Items/Armours/BodyArmours/BodyStr1")!) with
                { Sockets = [new(SocketColour.White, gem), new(SocketColour.White, support)], Links = [2] };
                inventory.Add(PlayerInventories.BodyArmour, 0, 0, host);
                Skill = Program.GemSkillIdPool.Value[0];
            }
            // Item activation reads the requirement attributes, not the pools Refresh sets (socketed gems count, 19-gems.md):
            // the level 1 character meets its supports' requirements through these.
            foreach (string attribute in new[] { "strength", "dexterity", "intelligence" })
                Set($"virtual_{attribute}_for_requirements", 1000);
            Set(AttackBaseCriticalStrike.UnarmedBaseStat, 1000);
            Set(AttackBaseCriticalStrike.WeaponOverrideStat, 1000);
            Set("extra_critical_rolls_while_on_low_life", 1);
            Set("physical_damage_+%", -100);
            Set("attack_minimum_added_chaos_damage", 1000);
            Set("attack_maximum_added_chaos_damage", 1000);
            Set("spell_minimum_added_chaos_damage", 1000);
            Set("spell_maximum_added_chaos_damage", 1000);
            Set("attack_speed_+%", 200);
            Refresh();
            Target = new("critical target", new(32 + distance, 32, 30), 0, 2, targetLife)
                { Mods = targetMods ?? MonsterModSet.None, Level = targetLevel };
            Conn.Monsters.Add(42, Target);
            Conn.Clock.Start();
            listener.Start();
            var accepting = listener.AcceptTcpClientAsync();
            writer.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            reader = accepting.GetAwaiter().GetResult();
        }

        public void Set(string name, int value) => Conn.PlayerStats[Program.Stats.Key(name)!.Value] = value;
        public void EquipPenetration()
        {
            var inventory = PlayerInventories.For(character, Program.Items);
            var main = Assert.Single(inventory.Items(PlayerInventories.Weapon1));
            var mod = Program.Items.Mods.ById("SynthesisImplicitLocalElementalPen3")!;
            var source = Assert.Single(mod.Stats);
            Assert.Equal("local_elemental_penetration_%", source.Id);
            Assert.True(source.Local);
            Assert.InRange(5, source.Min, source.Max);
            Assert.NotNull(inventory.Change(PlayerInventories.Weapon1, main.Id,
                item => item with { Mods = [new(ModSection.Implicit, mod.Hash, [5])] }));
            var unused = Program.Items.Mods.ById("LocalElementalPenetrationUnique__1")!;
            inventory.Add(14, 0, 0, ItemCatalog.Create(Program.Items.Find("Rusted Sword")!) with
                { Mods = [new(ModSection.Explicit, unused.Hash, [30])] });
        }
        public void Refresh() => Conn.Pools = Program.PoolsFor(Conn, 40) with
        { Life = 100, Mana = 1000, Accuracy = int.MaxValue, Strength = 1000, Dexterity = 1000, Intelligence = 1000 };
        public int Multiplier => Skill == SkillCatalog.FirstSkillId
            ? Program.AttackCriticalMultiplier(Conn, Program.CharacterPlayerStats(Conn))
            : (int)Method("SupportedCriticalMultiplier").Invoke(null, [Conn, Skill])!;
        public DamagePacket Hit => (DamagePacket)Method("SupportedHit").Invoke(null, [Conn, Skill])!;

        public Task Use()
        {
            var bytes = new LoginWriter().U16(0x24).U32(42).U16(Skill).U16(0).U16(0x409).ToArray();
            var use = InstanceTransfer.ReadUseSkillOnObject(bytes);
            return ((Task)Method("OnUseObject").Invoke(null,
                ["critical-contact-test", writer.GetStream(), ciphers, Conn, use, Conn.Clock.Elapsed.TotalSeconds])!)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }

        public Task GroundUse()
        {
            var at = Target.Walk.Position(Conn.Clock.Elapsed.TotalSeconds);
            var bytes = new LoginWriter().U16(InstanceProtocol.UseSkillAtLocationId).U32((uint)at.X).U32((uint)at.Y)
                .U16(Skill).U16(0).U16(1).ToArray();
            return Program.OnClientMessages("critical-ground-test", writer.GetStream(), ciphers,
                [(InstanceProtocol.UseSkillAtLocationId, bytes)], [], Conn);
        }

        public async Task HitLands()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (Target.Life == Target.MaxLife) await Task.Delay(10, timeout.Token);
        }

        public Task<bool> ContactDamage(Func<int> damage, int? action = null) => (Task<bool>)Method("HitMonster").Invoke(null,
            ["impale-contact-gate-test", writer.GetStream(), ciphers, Conn, 42u, Target, 0, "test", false, false,
                (ushort)0, action, null, null, damage, null])!;

        public Task Sweep()
        {
            var gems = (IEnumerable<(ushort Id, int Effect, string Name, int Level, int Quality, uint Gem)>)
                typeof(Program).GetMethod("GemSkillEffects", BindingFlags.Static | BindingFlags.NonPublic,
                    binder: null, types: [typeof(ClientState)], modifiers: null)!.Invoke(null, [Conn])!;
            var gem = Assert.Single(gems, g => g.Id == Skill);
            return ((Task)Method("AreaSkillHit").Invoke(null,
                ["accuracy-area-contact-test", writer.GetStream(), ciphers, Conn, Skill, gem, (32, 32), (34, 32),
                    false, (ushort)0, Conn.ActionCount, null, null, 1.0, 0u, null, null, 0u])!).WaitAsync(TimeSpan.FromSeconds(5));
        }

        public async Task Launch((int X, int Y)? aim = null)
        {
            // Release/contacts are simulation work; draining native output no longer drives gameplay time.
            var session = Conn.ProjectileRuntime ??= new("contact-fixture", writer.GetStream(), ciphers, Conn);
            using (Program.ProjectileMutation(Conn, advance: false))
            {
                await ((Task)Method("LaunchProjectile").Invoke(null,
                    ["accuracy-gem-projectile-test", writer.GetStream(), ciphers, Conn, Skill,
                        (32, 32), aim ?? Target.Walk.Position(Conn.Clock.Elapsed.TotalSeconds), Conn.HitRecovery.Generation,
                        42u, (ushort)0, Conn.ActionCount, null, null, null])!);
                // Finish this use's flights and delayed blasts before observing its damage.
                session.AdvanceTo(session.Runtime.Now + ProjectileRuntime.Time(4));
            }
            var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Enqueue(() => { drained.SetResult(); return Task.CompletedTask; });
            await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public async Task Refund(ushort node)
        {
            var bytes = new LoginWriter().U16(0x36).U32(node).U8(0).U8(0).ToArray();
            await Program.OnClientMessages("critical-refund-test", writer.GetStream(), ciphers,
                [(InstanceEntities.RefundPassiveId, bytes)], [], Conn);
        }

        public Task RunPanelTick() => (Task)Method("RunPlayerTick").Invoke(null,
            ["critical-panel-test", writer.GetStream(), ciphers, Conn])!;

        public Task RunRecovery() => (Task)Method("RunResourceRecovery").Invoke(null,
            ["leech-recovery-test", writer.GetStream(), ciphers, Conn])!;

        public IReadOnlyList<GrantedSkill> CurrentSkills() =>
            ((IEnumerable<GrantedSkill>)Method("PlayerSkills").Invoke(null, [Conn])!).ToList();

        public Task RefreshStats(bool charges) => (Task)Method(charges ? "SendChargeStats" : "SendCharacterStats")
            .Invoke(null, ["critical-queued-stats-test", writer.GetStream(), ciphers, Conn, "critical context"])!;

        public async Task AssertNextPlayerStats()
        {
            var stats = ((IEnumerable<(uint, int)>)Method("FinalPlayerStats").Invoke(null, [Conn])!).ToList();
            await AssertNextPacket(InstanceEntities.StatsUpdate(Program.PlayerId, stats));
        }

        public Task AssertNextActorStats() => AssertNextPacket(
            InstanceEntities.PlayerSkillsChange(Program.PlayerId, CurrentSkills(), []));

        async Task AssertNextPacket(byte[] expected)
        {
            byte[] actual = new byte[expected.Length];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await reader.GetStream().ReadExactlyAsync(actual, timeout.Token);
            received.Receive.Process(actual);
            Assert.Equal(expected, actual);
        }

        public void Dispose()
        {
            Conn.Closed = true;
            writer.Dispose(); reader.Dispose(); listener.Stop();
            PlayerInventories.Forget(character);
            Program.GemSkillIdsOf.TryRemove(character, out _);
        }
    }

    [Theory]
    [InlineData(null, "Rusted Sword", false)]
    [InlineData(null, "Crude Bow", false)]
    [InlineData("Cyclone", "Rusted Sword", false)]
    [InlineData("BurningArrow", "Crude Bow", false)]
    [InlineData("Fireball", "Rusted Sword", false)]
    [InlineData(null, "Rusted Sword", true)]
    [InlineData(null, "Crude Bow", true)]
    [InlineData("Cyclone", "Rusted Sword", true)]
    [InlineData("BurningArrow", "Crude Bow", true)]
    [InlineData("Fireball", "Rusted Sword", true)]
    public async Task Parsed_contacts_apply_general_and_only_the_attacking_weapons_penetration(
        string? gem, string weapon, bool prohibited)
    {
        using var f = new Contact(new Draws(), gem, weapon, distance: weapon == "Crude Bow" ? 60 : 2);
        f.EquipPenetration();
        f.Set("global_cannot_crit", 1);
        f.Set("physical_damage_+%", -10000);
        f.Set("attack_minimum_added_chaos_damage", 0);
        f.Set("attack_maximum_added_chaos_damage", 0);
        f.Set("spell_minimum_added_chaos_damage", 0);
        f.Set("spell_maximum_added_chaos_damage", 0);
        f.Set("attack_minimum_added_fire_damage", 1000);
        f.Set("attack_maximum_added_fire_damage", 1000);
        f.Set("spell_minimum_added_fire_damage", 1000);
        f.Set("spell_maximum_added_fire_damage", 1000);
        f.Set("reduce_enemy_elemental_resistance_%", 7);
        f.Set("base_attack_damage_penetrates_elemental_resist_%", 5);
        f.Set(Penetration.ElementalProhibition, prohibited ? 1 : 0);
        f.Refresh();
        var raw = gem is null ? new DamageRange(1000, 1000) : f.Hit[DamageType.Fire];
        Assert.True(raw.Min > 0);
        Assert.NotNull(((SkillCatalog.Unarmed)Method("WeaponFor").Invoke(null, [f.Conn])!).LocalSources);
        // The synthetic target has zero resistance. Spells get general 7 only; attacks get 7 + 5 + local 5.
        double factor = prohibited ? 1 : gem == "Fireball" ? 1.07 : 1.17;
        await f.Use();
        await f.HitLands();
        Assert.InRange(f.Target.MaxLife - f.Target.Life,
            (uint)Math.Round(raw.Min * factor), (uint)Math.Round(raw.Max * factor));
    }

    [Theory]
    [InlineData(50u, 0, 2, true)]
    [InlineData(51u, 0, 1, false)]
    [InlineData(50u, -1, 1, false)]
    [InlineData(50u, 1, 2, true)]
    [InlineData(50u, 100, 2, true)]
    [InlineData(50u, -100, 2, false)]
    public async Task Parsed_default_contact_keeps_admission_life_and_signed_luck(uint life, int ordinary, int calls, bool crit)
    {
        var random = new Draws(9000, 0);
        using var f = new Contact(random);
        f.Set("extra_critical_rolls", ordinary);
        f.Set(CriticalStrikePolicy.UnarmedMeleeMultiplierStat, 50);
        f.Conn.PlayerLife = life;
        var use = f.Use();
        f.Conn.PlayerLife = life == 50 ? 51u : 50u; // A later Life change cannot replace this action's luck.
        await use;
        Assert.Equal(calls, random.Calls);
        Assert.Equal(crit ? 2000u : 1000u, f.Target.MaxLife - f.Target.Life);
    }

    [Theory]
    [InlineData("Cyclone", null)]
    [InlineData("IceNova", null)]
    [InlineData("Fireball", null)]
    [InlineData("BurningArrow", "Crude Bow")]
    public async Task Parsed_supported_hits_keep_skill_use_luck_and_use_the_context_multiplier(string gem, string? weapon)
    {
        var random = new Draws(9000, 0);
        using var f = new Contact(random, gem, weapon);
        int baseline = f.Multiplier;
        Assert.True(baseline > 150); // The real linked Increased Critical Damage Support is applied.
        f.Set(CriticalStrikePolicy.UnarmedMeleeMultiplierStat, 50);
        var hit = f.Hit;
        int multiplier = f.Multiplier;
        Assert.Equal(baseline + (gem == "Cyclone" ? 50 : 0), multiplier);
        var panel = ((IEnumerable<GrantedSkill>)Method("PlayerSkills").Invoke(null, [f.Conn])!).Single(s => s.Id == f.Skill);
        uint multiplierKey = Program.Stats.Key(gem is "Cyclone" or "BurningArrow"
            ? "main_hand_critical_strike_multiplier" : "spell_critical_strike_multiplier")!.Value;
        Assert.Contains((multiplierKey, multiplier), panel.Stats!);
        Assert.Contains((Program.Stats.Key("virtual_extra_critical_rolls")!.Value, 1), panel.Stats!);
        f.Conn.PlayerLife = 50;
        await f.Use();
        f.Conn.PlayerLife = 51; // Luck belongs to the admitted skill use, not later changes before contact.
        await f.HitLands();
        Assert.Equal(2, random.Calls);
        Assert.InRange((int)(f.Target.MaxLife - f.Target.Life),
            DamageModel.CriticalDamage((int)Math.Round(hit.MinTotal), multiplier),
            DamageModel.CriticalDamage((int)Math.Round(hit.MaxTotal), multiplier));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Released_ordinary_bow_keeps_use_luck_and_reads_prohibition_at_impact(bool cannotCrit)
    {
        var random = new Draws(9000, 0);
        using var f = new Contact(random, weaponName: "Crude Bow", distance: 60);
        f.Set(CriticalStrikePolicy.UnarmedMeleeMultiplierStat, 50);
        f.Conn.PlayerLife = 50;
        Task use;
        using (Program.ProjectileMutation(f.Conn, advance: false))
        {
            // Accept the real bow action while unpaused, then freeze the pump before it can release or hit.
            use = f.Use();
            f.Conn.Clock.Stop();
            try
            {
                var session = Assert.IsType<Program.ProjectileSession>(f.Conn.ProjectileRuntime);
                long deadline = session.Runtime.Now + ProjectileRuntime.Time(2);
                while (session.Runtime.Projectiles.Count == 0 && session.Runtime.Now < deadline)
                    session.AdvanceTo(session.Runtime.Now + ProjectileRuntime.TickQuanta / 10);
                Assert.Single(session.Runtime.Projectiles);
                Assert.Equal(0, random.Calls);
                Assert.Equal(f.Target.MaxLife, f.Target.Life);
                f.Conn.PlayerLife = 51;
                f.Set("global_cannot_crit", cannotCrit ? 1 : 0);
                session.AdvanceTo(session.Runtime.Now + ProjectileRuntime.Time(1));
            }
            finally { f.Conn.Clock.Start(); }
        }
        await use;
        Assert.Equal(cannotCrit ? 0 : 2, random.Calls);
        Assert.Equal(cannotCrit ? 1000u : 1500u, f.Target.MaxLife - f.Target.Life);
    }

}
