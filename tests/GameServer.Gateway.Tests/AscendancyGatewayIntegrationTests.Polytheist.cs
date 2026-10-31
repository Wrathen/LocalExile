using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using GameServer.Core.Projectiles;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

public sealed partial class AscendancyGatewayIntegrationTests
{
    // Start 409 opens three seven-node rings; each build is one whole ring plus the next ring's first small node.
    internal static readonly ushort[][] PolytheistBuilds =
    [
        [27664, 22056, 26231, 32742, 21979, 61900, 59443, 46465],
        [46465, 64171, 51229, 43703, 29846, 1830, 6605, 30769],
        [30769, 5266, 322, 24342, 33835, 23429, 25478, 27664],
    ];
    // Devout of Solaris through ring A's other side, without Garukhan (so a shock still counts).
    static readonly ushort[] PolytheistSolaris = [59443, 61900, 21979, 32742];
    static readonly ushort[] PolytheistSmallNodes = [27664, 46465, 6605, 33835, 30769, 29846, 21979, 59443, 322, 51229, 26231, 25478];

    static int PolytheistStat(ClientState state, string name) =>
        Program.CharacterPlayerStats(state).GetValueOrDefault(Program.Stats.Key(name)!.Value);

    [Fact]
    public void Polytheist_checklist_is_the_actual_graph_and_three_builds_cover_every_node()
    {
        if (!DataAvailable()) return;
        var definition = Program.Ascendancies.Find(5, AscendancyFamily.Phrecia, "Polytheist")!;
        Assert.Equal(new AscendancyIdentity(AscendancyFamily.Phrecia, "Guardian", "3.29.3b"), definition.Identity);
        Assert.Equal((ushort)409, definition.StartNode);
        Assert.Equal(new ushort[] { 409, 27664, 32742, 43703, 24342, 46465, 6605, 33835, 30769, 29846, 64171, 22056,
            1830, 21979, 23429, 59443, 61900, 322, 5266, 51229, 26231, 25478 }.Order(), definition.Nodes.Keys.Order());
        Assert.Equal(definition.Nodes.Keys.Where(n => n != 409).Order(), PolytheistBuilds.SelectMany(b => b).Distinct().Order());
        Assert.All(definition.Nodes.Values, n => Assert.Equal(0, n.PointsGranted));
        // The default gate stays closed. The opt-in admits Guardian only through the installed-graph check: unverified
        // graphs are refused with this class's page, and it opens nothing for the Slayer and Gladiator identities.
        const string OptIn = "Game_VERIFIED_POLYTHEIST";
        string? before = Environment.GetEnvironmentVariable(OptIn);
        if (before != "1")
            Assert.Contains("ascendancies.md", Assert.Throws<AscendancyRuleException>(
                () => AscendancyClientCompatibility.RequirePlayable(definition.Identity)).Message);
        try
        {
            Environment.SetEnvironmentVariable(OptIn, "1");
            VerifiedGamblerGraph.InstalledGraphs = () => ([], []);
            Assert.Contains("polytheist.md", Assert.Throws<AscendancyRuleException>(
                () => AscendancyClientCompatibility.RequirePlayable(definition.Identity)).Message);
            foreach (string other in new[] { "Slayer", "Gladiator" })
                Assert.DoesNotContain("polytheist", Assert.Throws<AscendancyRuleException>(() => AscendancyClientCompatibility
                    .RequirePlayable(new(AscendancyFamily.Phrecia, other, "3.29.3b"))).Message);
        }
        finally
        {
            VerifiedGamblerGraph.InstalledGraphs = null;
            Environment.SetEnvironmentVariable(OptIn, before);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Polytheist_legal_build_paid_refund_and_reload(int index)
    {
        if (!DataAvailable()) return;
        using var game = new GatewayCharacter(classRow: 6, baseClass: 5, choice: "Polytheist", refundPoints: 100, level: 60);
        var nodes = PolytheistBuilds[index];
        var plain = game.State.Pools;
        int small = nodes.Count(PolytheistSmallNodes.Contains);
        Assert.Null((await game.Change(nodes, true)).Error);
        Assert.Equal(8, game.State.MainAscendancy.Allocated.Count);
        AssertNodeConsumers(game.State, true);
        Assert.Equal(nodes.Order(), game.Reload().MainAscendancy.Allocated.Order());
        AssertNodeConsumers(game.Reload(), true);

        Assert.Null((await game.Change(nodes.Reverse().ToArray(), false, refundPoints: true)).Error);
        Assert.Empty(game.State.MainAscendancy.Allocated);
        Assert.Equal((ushort)60, game.State.RefundPoints);
        AssertNodeConsumers(game.State, false);
        if (index == 2)
        {
            // A plain character is frozen and chilled by a critical cold hit; Brine King then drops both on the next tick.
            var state = game.State;
            var cold = DamagePacket.Of(DamageType.Cold, state.PlayerMaxLife, state.PlayerMaxLife);
            double now = state.Clock.Elapsed.TotalSeconds;
            lock (state.Monsters)
                Program.ApplyPlayerAilmentsLocked(state, cold, cold, true, false, now, Program.CharacterPlayerStats(state));
            Assert.Equal((false, 30, true, 0), state.ElementalAilments.Snapshot(now));
            Assert.Null((await game.Change(nodes, true)).Error);
            lock (state.Monsters)
                Program.AdvancePlayerAilmentsLocked(state, now, now + .25, Program.CharacterPlayerStats(state));
            Assert.Equal((false, 0, false, 0), state.ElementalAilments.Snapshot(now + .25));
        }
        else Assert.Null((await game.Change(nodes, true)).Error);
        Assert.Equal((ushort)60, game.Reload().RefundPoints);

        void AssertNodeConsumers(ClientState state, bool allocated)
        {
            // Small nodes: 10% increased Defences reach every defence's increase; light radius goes to the client.
            int defences = allocated ? 10 * small : 0;
            Assert.Equal(defences, PolytheistStat(state, "global_defences_+%"));
            Assert.Equal(plain.ArmourPct + defences, state.Pools.ArmourPct);
            Assert.Equal(plain.EvasionPct + defences, state.Pools.EvasionPct);
            Assert.Equal(plain.EnergyShieldPct + defences, state.Pools.EnergyShieldPct);
            Assert.True(allocated ? state.Pools.Evasion > plain.Evasion : state.Pools.Evasion == plain.Evasion);
            Assert.Equal(allocated ? 5 * small : 0, PolytheistStat(state, "light_radius_+%"));
            // Brine King: maximum cold resistance, and the same critical cold hit neither chills nor freezes.
            bool brine = allocated && index == 2;
            Assert.Equal(plain.ColdMaxResistance + (brine ? 10 : 0), state.Pools.ColdMaxResistance);
            var probe = new PlayerElementalAilments();
            var stats = Program.CharacterPlayerStats(state);
            var hit = DamagePacket.Of(DamageType.Cold, 1000, 1000);
            probe.Apply(hit, hit, true, 1000, 0, n => stats.GetValueOrDefault(Program.Stats.Key(n)!.Value), new Random(1));
            Assert.Equal(brine ? (false, 0, false, 0) : (false, 30, true, 0), probe.Snapshot(0));
            // Ralakesh: three of each charge without gaining any, in the stats and in the charge session.
            int minimum = allocated && index == 1 ? 3 : 0;
            var session = ProgramValue<ChargeSession>("ChargesOf", state);
            foreach (var type in Charges.Types)
            {
                Assert.Equal(minimum, PolytheistStat(state, $"current_{Charges.Name(type)}_charges"));
                Assert.Equal(minimum, session.Limits(type).Minimum);
                Assert.Equal(minimum, session.Effective(type));
            }
            // Abberath: the footstep-triggered Abberath's Fury and the hoofprints the client draws from the player stats.
            bool abberath = allocated && index == 0;
            Assert.Equal(abberath ? 1 : 0, Program.FootstepSkills(state, state.Clock.Elapsed.TotalSeconds).Count);
            Assert.Equal(abberath ? 1 : 0, PolytheistStat(state, "goat_footprints_from_item"));
        }
    }

    sealed record PolytheistRecovery(int Evasion, int EvasionStat, double Life, int LifeStat, double Mana, int ManaStat);

    // The same character standing, then walking: what monster attacks roll against, what the resource writer applies
    // and the finals the panel and the client's pool stepping read.
    static (PolytheistRecovery Still, PolytheistRecovery Walking) PolytheistWhileMoving(ClientState state)
    {
        PolytheistRecovery Read()
        {
            var stats = Program.CharacterPlayerStats(state);
            int Final(string name) => stats.GetValueOrDefault(Program.Stats.Key(name)!.Value);
            return new(ProgramValue<int>("PlayerEvasion", state, state.Clock.Elapsed.TotalSeconds), Final("evasion_rating"),
                ProgramValue<double>("LifeRegenPerSecond", state), Final("life_recovery_per_minute"),
                ProgramValue<double>("ManaRegenPerSecond", state), Final("mana_recovery_per_minute"));
        }
        double now = state.Clock.Elapsed.TotalSeconds;
        var at = state.Walk.Position(now);
        state.Walk.Teleport(at.X, at.Y, now);
        var still = Read();
        state.Walk.Order(at.X > 64 ? at.X - 60 : at.X + 60, at.Y, state.Clock.Elapsed.TotalSeconds);
        var walking = Read();
        Assert.True(state.Clock.Elapsed.TotalSeconds < state.Walk.ArrivesAt);
        state.Walk.Teleport(at.X, at.Y, state.Clock.Elapsed.TotalSeconds);
        return (still, walking);
    }

    sealed partial class GatewayCharacter
    {
        // One monster attack of 100 physical damage through MonsterSwing (or the attacker's own), optionally while 50%
        // shocked or walking, with the evasion entropy preset (99: every attack hits); the life it took.
        public async Task<uint> PolytheistMonsterHit(bool shocked, bool walking = false, int entropy = 99, SpawnedMonster? attacker = null)
        {
            uint before = State.PlayerLife;
            await Exchange(async (stream, cipher) =>
            {
                double now = State.Clock.Elapsed.TotalSeconds;
                var position = State.Walk.Position(now);
                State.Walk.Teleport(position.X, position.Y, now);
                if (shocked)
                {
                    var lightning = DamagePacket.Of(DamageType.Lightning, State.PlayerMaxLife, State.PlayerMaxLife);
                    lock (State.Monsters)
                        Program.ApplyPlayerAilmentsLocked(State, lightning, lightning, true, false, now, Program.CharacterPlayerStats(State));
                    Assert.Equal(50, State.ElementalAilments.Snapshot(now).Shock);
                }
                if (walking) State.Walk.Order(position.X > 64 ? position.X - 60 : position.X + 60, position.Y, now);
                State.EvasionEntropy = entropy;
                State.LastEvasionAt = State.Clock.Elapsed.TotalSeconds;
                var monster = attacker ?? new SpawnedMonster("Polytheist incoming contact", new(position.X + 2, position.Y, 30), 0, 2, 1000)
                {
                    Hostile = true, Attack = new(SkillCatalog.FirstSkillId, .002, 20, DamagePacket.Of(DamageType.Physical, 100, 100)),
                };
                monster.Walk.Teleport(position.X + 2, position.Y, now);
                State.Monsters[43] = monster;
                await (Task)GatewayMethod("MonsterSwing").Invoke(null, ["polytheist-integration", stream, cipher,
                    State, 43u, monster, monster.ActionGeneration])!;
                State.Monsters.Remove(43);
                State.ElementalAilments.Clear();
                State.Walk.Teleport(position.X, position.Y, State.Clock.Elapsed.TotalSeconds);
                return null;
            });
            uint taken = before - State.PlayerLife;
            State.PlayerLife = State.PlayerMaxLife;
            return taken;
        }

        // The finals the player tick sends again when a walk starts or ends, sent while walking.
        public Task<(string? Error, byte[] Bytes)> PolytheistWalkingStats() => Exchange(async (stream, cipher) =>
        {
            double now = State.Clock.Elapsed.TotalSeconds;
            var position = State.Walk.Position(now);
            State.Walk.Order(position.X > 64 ? position.X - 60 : position.X + 60, position.Y, now);
            await ProgramValue<Task>("SendWhileMovingStats", "polytheist-integration", stream, cipher, State);
            State.Walk.Teleport(position.X, position.Y, State.Clock.Elapsed.TotalSeconds);
            return null;
        });

        public Task<(string? Error, byte[] Bytes)> PolytheistAttackerSlows() => Exchange(async (stream, cipher) =>
        {
            await ProgramValue<Task>("SendAttackerSlows", "polytheist-integration", stream, cipher, State);
            return null;
        });

        // One area spell sweep (AreaSkillHit, at once) cast from self at aim, with a monster standing at each; which it hit.
        public async Task<(bool Self, bool Aim)> PolytheistAreaSpell(ushort skillId, (int X, int Y) self, (int X, int Y) aim)
        {
            var gem = ProgramValue<IEnumerable<(ushort Id, int Effect, string Name, int Level, int Quality, uint Gem)>>(
                "GemSkillEffects", State).Single(g => g.Id == skillId);
            SpawnedMonster At((int X, int Y) p) => new("Polytheist area target", new(p.X, p.Y, 30), 0, 2, 100000) { Hostile = true };
            var (near, far) = (At(self), At(aim));
            (State.Monsters[46], State.Monsters[47]) = (near, far);
            Assert.Null((await Exchange(async (stream, cipher) =>
            {
                await (Task)GatewayMethod("AreaSkillHit").Invoke(null, ["polytheist-integration", stream, cipher, State, skillId, gem,
                    self, aim, true, (ushort)0, null, null, null, 1.0, 0u, null, null, 0u])!;
                return null;
            })).Error);
            State.Monsters.Remove(46);
            State.Monsters.Remove(47);
            return (near.Life < near.MaxLife, far.Life < far.MaxLife);
        }

        public Task<(string? Error, byte[] Bytes)> PolytheistRun(Func<NetworkStream, ConnectionCiphers, Task> work) =>
            Exchange(async (stream, cipher) =>
            {
                await work(stream, cipher);
                return null;
            });

        public Task<(string? Error, byte[] Bytes)> PolytheistDeath() => Exchange(async (stream, cipher) =>
        {
            State.PlayerLife = 0;
            await ProgramValue<Task>("PlayerDied", "polytheist-integration", stream, cipher, State, "integration death");
            return null;
        });

        // One player tick of the monsters' ignites (BurnIgnited) up to now, with these monsters.
        public Task<(string? Error, byte[] Bytes)> PolytheistBurnIgnites(params (uint Id, SpawnedMonster M)[] monsters) =>
            Exchange(async (stream, cipher) =>
            {
                foreach (var (id, m) in monsters) State.Monsters[id] = m;
                await Program.BurnIgnited("polytheist-integration", stream, cipher, State);
                foreach (var (id, _) in monsters) State.Monsters.Remove(id);
                return null;
            });

        // One player tick of the passive auras (TickPassiveAuras).
        public Task<(string? Error, byte[] Bytes)> PolytheistAuraTick() => Exchange(async (stream, cipher) =>
        {
            await Program.TickPassiveAuras("polytheist-integration", stream, cipher, State);
            return null;
        });

        // One footstep's skill (TriggerFootstepSkill) with a monster dx cells east of the player and one a cell further.
        public async Task<(SpawnedMonster Near, SpawnedMonster Far, byte[] Bytes)> PolytheistFootstep(PassiveGrantedSkills.Grant skill, int dx)
        {
            double now = State.Clock.Elapsed.TotalSeconds;
            var at = State.Walk.Position(now);
            SpawnedMonster At(int x)
            {
                var m = new SpawnedMonster("Polytheist footstep target", new(at.X + x, at.Y, 30), 0, 2, 100000) { Hostile = true };
                m.Walk.Teleport(at.X + x, at.Y, now);
                return m;
            }
            var (near, far) = (At(dx), At(dx + 1));
            (State.Monsters[48], State.Monsters[49]) = (near, far);
            var (error, bytes) = await Exchange(async (stream, cipher) =>
            {
                await Program.TriggerFootstepSkill("polytheist-integration", stream, cipher, State, skill);
                return null;
            });
            Assert.Null(error);
            State.Monsters.Remove(48);
            State.Monsters.Remove(49);
            return (near, far, bytes);
        }
    }

    [Fact]
    public async Task Polytheist_Garukhan_evasion_and_regeneration_while_moving_and_unaffected_by_shock()
    {
        if (!DataAvailable()) return;
        using var game = new GatewayCharacter(classRow: 6, baseClass: 5, choice: "Polytheist", level: 60);
        var state = game.State;
        var plain = PolytheistWhileMoving(state);
        Assert.Equal(plain.Still, plain.Walking);
        Assert.Empty((await game.PolytheistWalkingStats()).Bytes);
        Assert.Equal(150u, await game.PolytheistMonsterHit(shocked: true));

        Assert.Null((await game.Change(PolytheistBuilds[0], true)).Error);
        var (still, walking) = PolytheistWhileMoving(state);
        // 100% increased Evasion Rating while moving joins the other increases; standing keeps the ordinary rating.
        int pct = state.Pools.EvasionPct;
        Assert.Equal(state.Pools.Evasion, still.Evasion);
        // Ratings round half away from zero (CharacterStats.Rating).
        Assert.Equal((int)Math.Round(15m * (100 + pct) / 100, MidpointRounding.AwayFromZero), still.Evasion);
        Assert.Equal((int)Math.Round(15m * (200 + pct) / 100, MidpointRounding.AwayFromZero), walking.Evasion);
        Assert.Equal((still.Evasion, walking.Evasion), (still.EvasionStat, walking.EvasionStat));
        // Regenerate 4% of Life per second and 100% increased Mana Regeneration Rate while moving, and nothing standing.
        Assert.Equal(0, PolytheistStat(state, "mana_regeneration_rate_+%"));
        Assert.Equal((plain.Still.Life, plain.Still.Mana), (still.Life, still.Mana));
        Assert.Equal(still.Life + state.Pools.Life * .04, walking.Life, 6);
        Assert.Equal(2 * still.Mana, walking.Mana, 6);
        Assert.Equal((int)Math.Round(walking.Life * 60), walking.LifeStat);
        Assert.Equal((int)Math.Round(walking.Mana * 60), walking.ManaStat);
        Assert.Equal((int)Math.Round(still.Mana * 60), still.ManaStat);
        // The client steps its pools from those finals and derives none of them, so a walk start or end sends them again.
        uint Key(string name) => Program.Stats.Key(name)!.Value;
        var sent = InstanceEntities.StatsUpdate(Program.PlayerId, [(Key("evasion_rating"), walking.Evasion),
            (Key("life_recovery_per_minute"), walking.LifeStat), (Key("total_life_recovery_per_minute_from_regeneration"), walking.LifeStat),
            (Key("mana_recovery_per_minute"), walking.ManaStat), (Key("total_mana_recovery_per_minute_from_regeneration"), walking.ManaStat)]);
        Assert.True((await game.PolytheistWalkingStats()).Bytes.AsSpan().IndexOf(sent) >= 0);
        // MonsterSwing rolls against the walking rating: an entropy that the standing chance just reaches is evaded.
        int accuracy = Program.Monsters.BaseAccuracy(1)!.Value;
        int standing = DamageModel.AccuracyHitChance(still.Evasion, accuracy);
        Assert.True(DamageModel.AccuracyHitChance(walking.Evasion, accuracy) < standing);
        Assert.Equal(0u, await game.PolytheistMonsterHit(shocked: false, walking: true, entropy: 100 - standing));
        Assert.Equal(100u, await game.PolytheistMonsterHit(shocked: false, walking: false, entropy: 100 - standing));
        // Unaffected by Shock: the shock is still on the player (asserted in the helper), with no extra damage taken.
        Assert.Equal(100u, await game.PolytheistMonsterHit(shocked: true));
    }

    [Fact]
    public async Task Polytheist_Ryslatha_more_maximum_attack_damage_and_no_regeneration_after_a_hit()
    {
        if (!DataAvailable()) return;
        using var game = new GatewayCharacter(classRow: 6, baseClass: 5, choice: "Polytheist", level: 60);
        var state = game.State;
        var gem = FamilyGem("Ground Slam");
        EquipFamilyGem(game, gem);
        var weapon = ProgramValue<SkillCatalog.Unarmed>("WeaponFor", state);
        DamagePacket[] Hits()
        {
            var stats = Program.CharacterPlayerStats(state);
            ushort slam = FamilySkills(state).Single(s => s.EffectsPerLevelRow
                == Program.Skills.FindByRow(0, Program.Items.GemEffects(gem).First(), 1)!.EffectsPerLevelRow).Id;
            return [Program.AttackHit(weapon, stats, melee: true), AttackSkillHit.Resolve(weapon, stats, [], Program.Stats, 10000, melee: true),
                OrdinaryRangedShot.Capture(weapon, stats, [], Program.Stats, 0, 0, 0).Hit, Program.SupportedProjectileHit(state, slam, area: true)];
        }
        (int Min, int Max) NativeBounds()
        {
            var final = ProgramValue<IEnumerable<(uint Key, int Value)>>("FinalPlayerStats", state).ToDictionary(s => s.Key, s => s.Value);
            return (final[Program.Stats.Key("main_hand_minimum_physical_damage")!.Value],
                final[Program.Stats.Key("main_hand_maximum_physical_damage")!.Value]);
        }
        var plain = Hits();
        var plainBounds = NativeBounds();
        var monster = new SpawnedMonster("Polytheist regenerating target", new(10, 10, 0), 0, 2, 1000)
        {
            Mods = new([], [], new(new Dictionary<string, int> { ["life_regeneration_rate_per_minute_%"] = 600 })),
        };
        state.Monsters[44] = monster;
        uint Regenerated(double after, double seconds = .5)
        {
            monster.Life = 500;
            monster.Regen = 0;
            Program.RegenerateConnectionMonsters(state, monster.PlayerHitAt + after, seconds);
            return monster.Life - 500;
        }
        void Hit() => typeof(Program).GetMethod("ApplyMonsterHitLocked", System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, [state, monster, 0, null]);
        Hit();
        Assert.Equal(50u, Regenerated(.5));

        Assert.Null((await game.Change(PolytheistBuilds[2], true)).Error);
        // 30% more Maximum Attack Damage: every attack hit's maxima, minima unchanged, in the default attack (and panel),
        // the attack gems, the bow and the native physical bounds the client shows.
        var ryslatha = Hits();
        Assert.All(plain, p => Assert.True(p.MaxTotal > 0));
        for (int i = 0; i < plain.Length; i++)
            foreach (var (type, range) in plain[i].Components)
            {
                Assert.Equal(range.Min, ryslatha[i][type].Min, 9);
                Assert.Equal(range.Max * 1.3, ryslatha[i][type].Max, 9);
            }
        Assert.Equal((plainBounds.Min, (int)Math.Floor(weapon.HitDamage(Program.CharacterPlayerStats(state))[DamageType.Physical].Max * 1.3)),
            NativeBounds());
        // Enemies you've Hit Recently have 100% reduced Life Regeneration rate: none for 4 s, half of the step that ends
        // a quarter second after, all of it later; a death ends "recently".
        Hit();
        // The boundaries from an exactly representable hit time (a Stopwatch reading is not).
        monster.PlayerHitAt = state.LastEnemyHitAt = 10;
        Assert.Equal(0u, Regenerated(.5));
        Assert.Equal(0u, Regenerated(4));
        Assert.Equal(25u, Regenerated(4.25));
        Assert.Equal(50u, Regenerated(4.5));
        Hit();
        double hitAt = monster.PlayerHitAt;
        Assert.Null((await game.PolytheistDeath()).Error);
        Assert.Equal(double.NegativeInfinity, monster.PlayerHitAt);
        monster.Life = 500;
        monster.Regen = 0;
        Program.RegenerateConnectionMonsters(state, hitAt + .5, .5);
        Assert.Equal(550u, monster.Life);
    }

    static (ushort Id, int Effect) PolytheistSkill(ClientState state, string name) => ProgramValue<IEnumerable<(ushort Id, int Effect,
        string Name, int Level, int Quality, uint Gem)>>("GemSkillEffects", state).Where(g => g.Name == name).Select(g => (g.Id, g.Effect)).Single();

    static void EquipPolytheistGems(GatewayCharacter game, params string[] names)
    {
        game.MeetAttributeRequirements();
        game.Items.Add(PlayerInventories.Helmet, 0, 0, ItemCatalog.Create(Program.Items.Find("Iron Hat")!) with
        {
            Sockets = [.. names.Select(n => new ItemSocket(SocketColour.White, FamilyGem(n)))], Links = [.. names.Select(_ => (byte)1)],
        });
    }

    [Fact]
    public async Task Polytheist_Gruthkul_disables_spells_and_slows_enemies_that_hit_you()
    {
        if (!DataAvailable()) return;
        using var game = new GatewayCharacter(classRow: 6, baseClass: 5, choice: "Polytheist", refundPoints: 40, level: 60);
        var state = game.State;
        EquipPolytheistGems(game, "Fireball", "Ground Slam", "Clarity");
        var (fireball, slam, clarity) = (PolytheistSkill(state, "Fireball"), PolytheistSkill(state, "Ground Slam"), PolytheistSkill(state, "Clarity"));
        CharacterSaves.SetAuraActive(game.Character, "Clarity", true);
        void Reconcile()
        {
            lock (state.Monsters) typeof(Program).GetMethod("ReconcileOwnAuras", System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, [state, false]);
        }
        string? Disabled((ushort Id, int Effect) skill) => ProgramValue<string?>("GemDisabled", state, skill.Effect);
        int Flag(ushort id) => FamilySkills(state).Single(s => s.Id == id).Stats!.FirstOrDefault(v => v.Key == OwnAuraRestriction.SkillDisabled).Value;
        var monster = new SpawnedMonster("Polytheist hasty attacker", new(10, 10, 30), 0, 2, 1000)
        {
            Hostile = true, Attack = new(SkillCatalog.FirstSkillId, .002, 20, DamagePacket.Of(DamageType.Physical, 1, 1)),
            Mods = new([], [], new(new Dictionary<string, int> { ["attack_speed_+%"] = 20 })),
        };
        double Swing(double at) => Program.MonsterSwingSeconds(state, monster, at);
        var plain = state.Pools;
        Assert.Equal(0, ProgramValue<double>("LifeRegenPerSecond", state));
        Reconcile();
        Assert.Single(state.Auras);
        Assert.All(new[] { fireball, slam, clarity }, s => Assert.Null(Disabled(s)));
        Assert.True(ProgramValue<DamagePacket>("SupportedHit", state, fireball.Id).MaxTotal > 0);
        await game.PolytheistMonsterHit(false, attacker: monster);
        Assert.Equal(.002, Swing(monster.HitPlayerAt), 12);

        Assert.Null((await game.Change(PolytheistBuilds[2], true)).Error);
        // Your Spells are disabled: spells and spell auras, never attacks. A running spell aura is suspended (its intent
        // kept) and not restored while the node is allocated. The skill record carries skill_disabled_by_stat.
        Assert.Equal(("spells are disabled", "spells are disabled", (string?)null), (Disabled(fireball), Disabled(clarity), Disabled(slam)));
        Assert.Empty(state.Auras);
        Reconcile();
        Assert.Empty(state.Auras);
        Assert.Equal((1, 1, 0), (Flag(fireball.Id), Flag(clarity.Id), Flag(slam.Id)));
        // Spell Skills deal no Damage; +400 to maximum Life; Regenerate 8% of Life per second.
        Assert.Equal(0, ProgramValue<DamagePacket>("SupportedHit", state, fireball.Id).MaxTotal);
        Assert.True(Program.SupportedProjectileHit(state, slam.Id, area: true).MaxTotal > 0);
        Assert.Equal(plain.Life + 400, state.Pools.Life);
        Assert.Equal(state.Pools.Life * .08, ProgramValue<double>("LifeRegenPerSecond", state), 6);
        // Enemies that have Hit you with an Attack Recently have 10% reduced Attack Speed: not after an evaded attack; after
        // a hit its 20% increased becomes 10% for 4 s, sent on its Stats component; a death ends it.
        monster.HitPlayerAt = double.NegativeInfinity;
        Assert.Equal(0u, await game.PolytheistMonsterHit(false, entropy: 0, attacker: monster));
        Assert.Equal(double.NegativeInfinity, monster.HitPlayerAt);
        await game.PolytheistMonsterHit(false, attacker: monster);
        double slowed = .002 * 1.2 / 1.1, hitAt = monster.HitPlayerAt;
        Assert.Equal(slowed, Swing(hitAt), 12);
        // The 4 s boundary from an exactly representable hit time (a Stopwatch reading is not).
        monster.HitPlayerAt = 10;
        Assert.Equal(slowed, Swing(10), 12);
        Assert.Equal(slowed, Swing(14), 12);
        Assert.Equal(.002, Swing(14.01), 12);
        monster.HitPlayerAt = hitAt;
        var slowedStats = InstanceEntities.StatsUpdate(43, (monster.Attack! with { Seconds = Swing(state.Clock.Elapsed.TotalSeconds) }).Stats());
        Assert.True(game.LastBytes.AsSpan().IndexOf(slowedStats) >= 0);
        state.Monsters[45] = monster;
        Assert.Empty((await game.PolytheistAttackerSlows()).Bytes);
        Assert.Null((await game.PolytheistDeath()).Error);
        Assert.Equal(.002, Swing(hitAt), 12);
        Assert.True((await game.PolytheistAttackerSlows()).Bytes.AsSpan().IndexOf(InstanceEntities.StatsUpdate(45, monster.Attack!.Stats())) >= 0);
        state.Monsters.Remove(45);

        state.PlayerLife = state.PlayerMaxLife;
        CharacterSaves.SetAuraActive(game.Character, "Clarity", true);   // the death cleared the intent
        Assert.Null((await game.Change([.. PolytheistBuilds[2].Reverse()], false, refundPoints: true)).Error);
        Assert.All(new[] { fireball, slam, clarity }, s => Assert.Null(Disabled(s)));
        Reconcile();
        Assert.Single(state.Auras);
    }

    [Fact]
    public async Task Polytheist_Lunaris_nova_at_the_target_more_spell_projectiles_and_spell_area()
    {
        if (!DataAvailable()) return;
        using var game = new GatewayCharacter(classRow: 6, baseClass: 5, choice: "Polytheist", level: 60);
        var state = game.State;
        EquipPolytheistGems(game, "Ice Nova", "Fireball", "Ground Slam");
        var (nova, fireball, slam) = (PolytheistSkill(state, "Ice Nova"), PolytheistSkill(state, "Fireball"), PolytheistSkill(state, "Ground Slam"));
        (int X, int Y) self = (20, 20), aim = (100, 20);
        int Panel(ushort id, string name) => FamilySkills(state).Single(s => s.Id == id).Stats!
            .FirstOrDefault(v => v.Key == Program.Stats.Key(name)).Value;
        int Radius((ushort Id, int Effect) skill, bool spell) => ProgramValue<int>("SkillRadius", state,
            Program.Skills.EffectStats(skill.Effect, 1).ToDictionary(kv => kv.Item1, kv => kv.Item2), spell, skill.Effect);
        var (plainNova, plainSlam) = (Radius(nova, true), Radius(slam, false));
        // A self-targeted spell (Ice Nova, Vaal Cold Snap) goes off around the caster wherever it is aimed; other area
        // spells land where they are aimed, the Nova-type ones that are not self-targeted too (a totem's or a mine's, Dark
        // Pact's, Ice Nova of Frostbolts').
        Assert.Equal((true, false), await game.PolytheistAreaSpell(nova.Id, self, aim));
        (int X, int Y) Centre(string effect) => Program.SpellCentre(state, Program.Skills.EffectRow(effect), self, aim);
        Assert.Equal(aim, Program.SpellCentre(state, fireball.Effect, self, aim));
        Assert.Equal((aim, aim, aim, aim, self), (Centre("ShockwaveTotem"), Centre("StormblastMine"), Centre("DarkPact"),
            Centre("IceNovaAltX"), Centre("VaalColdSnap")));
        Assert.Equal((1, 1), (Panel(nova.Id, "skill_is_nova_skill"), Panel(fireball.Id, "total_number_of_projectiles_to_fire")));
        // The client draws an area skill at the record's final radius (live: Ice Nova drew nothing without it).
        int recordNova = Panel(nova.Id, "active_skill_base_area_of_effect_radius");
        Assert.Equal(recordNova, Panel(nova.Id, "active_skill_area_of_effect_radius"));
        Assert.Equal(0, Panel(nova.Id, "virtual_self_targeted_skills_target_location_instead"));

        Assert.Null((await game.Change(PolytheistBuilds[1], true)).Error);
        // Nova Spells Cast at the targeted location (a self-targeted spell that is no Nova stays on the caster); Spells
        // fire 2 additional Projectiles; 30% increased spell area only.
        Assert.Equal((false, true), await game.PolytheistAreaSpell(nova.Id, self, aim));
        Assert.Equal(self, Centre("VaalColdSnap"));
        Assert.Equal(3, Panel(fireball.Id, "total_number_of_projectiles_to_fire"));
        // The client aims the Nova at the cursor only on the derived flag, which the server sends (no calculators run).
        Assert.Equal((1, 0), (Panel(nova.Id, "virtual_self_targeted_skills_target_location_instead"),
            Panel(fireball.Id, "virtual_self_targeted_skills_target_location_instead")));
        Assert.Equal((int)Math.Floor(recordNova * Math.Floor(100 * Math.Sqrt(1.3)) / 100), Panel(nova.Id, "active_skill_area_of_effect_radius"));
        int baseNova = Program.Skills.EffectStats(nova.Effect, 1).Single(kv => kv.Item1 == Program.Stats.Key("active_skill_base_area_of_effect_radius")).Item2;
        Assert.Equal((int)Math.Floor(baseNova * Math.Floor(100 * Math.Sqrt(1.3)) / 100), Radius(nova, true));
        Assert.Equal(baseNova, plainNova);
        Assert.Equal(plainSlam, Radius(slam, false));
    }

    [Fact]
    public async Task Polytheist_Solaris_blinds_nearby_enemies_for_their_hits_and_the_players_crits()
    {
        if (!DataAvailable()) return;
        using var game = new GatewayCharacter(classRow: 6, baseClass: 5, choice: "Polytheist", refundPoints: 40, level: 60);
        var state = game.State;
        EquipPolytheistGems(game, "Fireball");
        var fireball = ProgramValue<IEnumerable<(ushort Id, int Effect, string Name, int Level, int Quality, uint Gem)>>(
            "GemSkillEffects", state).Single(g => g.Name == "Fireball");
        // A 5% unarmed attack base and no increased critical strike chance.
        state.PlayerStats[Program.Stats.Key(AttackBaseCriticalStrike.UnarmedBaseStat)!.Value] = 500;
        state.Pools = Program.PoolsFor(state, 60);
        double now = state.Clock.Elapsed.TotalSeconds;
        var at = state.Walk.Position(now);
        state.Walk.Teleport(at.X, at.Y, now);
        SpawnedMonster Enemy(int dx)
        {
            var m = new SpawnedMonster("Polytheist blind target", new(at.X + dx, at.Y, 30), 0, 2, 1000)
            {
                Hostile = true, Attack = new(SkillCatalog.FirstSkillId, .002, 40, DamagePacket.Of(DamageType.Physical, 100, 100)),
            };
            m.Walk.Teleport(at.X + dx, at.Y, now);
            return m;
        }
        var (near, far, attacker) = (Enemy(35), Enemy(36), Enemy(2));
        (state.Monsters[50], state.Monsters[51]) = (near, far);
        Task<uint> Hit(bool shocked, int entropy = 99) => game.PolytheistMonsterHit(shocked, entropy: entropy, attacker: attacker);

        // Plain: nothing is blinded and every chance is the ordinary one.
        Assert.Empty((await game.PolytheistAuraTick()).Bytes);
        Assert.Equal(500, state.Pools.AttackCriticalStrikeChance);
        Assert.Null(Program.AttackCriticalChanceVsBlinded(state, Program.CharacterPlayerStats(state)));
        Assert.Null(Program.SupportedCriticalChanceVsBlinded(state, fireball.Id, release: false));
        var (plain, shocked) = (await Hit(false), await Hit(true));
        Assert.Equal(Math.Round(plain * 1.5), shocked);

        Assert.Null((await game.Change(PolytheistSolaris, true)).Error);
        state.Monsters[43] = attacker;
        ushort buff = state.NextBuff;
        // Nearby Enemies are Blinded: the blinded buff (179) on each living hostile monster within 35 cells, once.
        var tick = (await game.PolytheistAuraTick()).Bytes;
        Assert.Equal((true, false, true), (Program.Blinded(state, 50), Program.Blinded(state, 51), Program.Blinded(state, 43)));
        ushort? Added(uint id) => Enumerable.Range(buff, 2).Select(b => (ushort?)b).SingleOrDefault(b => tick.AsSpan()
            .IndexOf(InstanceEntities.AddBuff(id, b!.Value, FlaskGrounds.BlindedBuffRow, Program.PlayerId)) >= 0);
        Assert.Equal(buff + 2, state.NextBuff);
        Assert.NotNull(Added(43));
        ushort nearBuff = Assert.NotNull(Added(50));
        Assert.Empty((await game.PolytheistAuraTick()).Bytes);
        // Blind: 20% less accuracy and evasion. The blinded attacker's hit is evaded at an entropy an ordinary one reaches.
        Assert.Equal((800, 1000), (Program.UnderBlind(state, 50, 1000), Program.UnderBlind(state, 51, 1000)));
        int evasion = ProgramValue<int>("PlayerEvasion", state, state.Clock.Elapsed.TotalSeconds);
        int accuracy = Program.Monsters.BaseAccuracy(attacker.Level)!.Value;
        int ordinaryChance = DamageModel.AccuracyHitChance(evasion, accuracy);
        Assert.True(DamageModel.AccuracyHitChance(evasion, FlaskGrounds.Blinded(accuracy)) < ordinaryChance);
        Assert.Equal(0u, await Hit(false, entropy: 100 - ordinaryChance));
        // 10% reduced Damage taken from Blinded Enemies, additive with the shock's 50% increase (x1.4, not x1.35).
        // Checked against the same character's unblinded hit below: the allocated nodes' defences change its armour.
        var (blinded, blindedShocked) = (await Hit(false), await Hit(true));
        // The ignite a Blinded enemy inflicts burns 10% less as well, through the player tick's ailment step (Abberath's
        // Unaffected by Ignite set aside here); one from an enemy outside the aura burns in full.
        uint unaffected = Program.Stats.Key("unaffected_by_ignite")!.Value;
        state.PlayerStats[unaffected] = 0;
        int Burn(uint source)
        {
            var fire = DamagePacket.Of(DamageType.Fire, 200, 200);
            double t = state.Clock.Elapsed.TotalSeconds;
            lock (state.Monsters)
            {
                state.ElementalAilments.Clear();
                var stats = Program.CharacterPlayerStats(state);
                Program.ApplyPlayerAilmentsLocked(state, fire, fire, true, false, t, stats, source);
                int burnt = Program.AdvancePlayerAilmentsLocked(state, t, t + 1, stats);
                state.ElementalAilments.Clear();
                state.PlayerLife = state.PlayerMaxLife;
                return burnt;
            }
        }
        int fullBurn = Burn(51);
        Assert.True(fullBurn > 100);
        Assert.InRange(Burn(43), fullBurn * .9 - 1, fullBurn * .9 + 1);
        state.PlayerStats.Remove(unaffected);
        // 100% increased Critical Strike Chance against Blinded Enemies joins the increases, only against a blinded
        // target: the default attack goes from 5% to 10%, a spell's chance doubles.
        int? attackVs = Program.AttackCriticalChanceVsBlinded(state, Program.CharacterPlayerStats(state));
        Assert.Equal((1000, 500), (Program.CriticalChanceOn(state, 50, 500, attackVs), Program.CriticalChanceOn(state, 51, 500, attackVs)));
        int spell = ProgramValue<int>("SupportedCriticalChance", state, fireball.Id);
        Assert.True(spell > 0);
        Assert.Equal(2 * spell, Program.SupportedCriticalChanceVsBlinded(state, fireball.Id, release: false));
        // Enemies Blinded by you have 100% reduced Critical Strike Chance: monsters only crit through always_crit, which
        // the client's chance (FUN_14275c050) makes 10000 before any increase is read, so a reduction cannot touch it.
        Assert.Equal(10_000, new MonsterModEffects(new Dictionary<string, int> { ["always_crit"] = 1 }).CriticalChance);

        // A refund takes the aura's blind off; the smoke cloud's own blind on another monster stays.
        state.FlaskGrounds.SetBlinded(51, true, () => 999);
        Assert.Null((await game.Change([.. PolytheistSolaris.Reverse()], false, refundPoints: true)).Error);
        Assert.True((await game.PolytheistAuraTick()).Bytes.AsSpan().IndexOf(InstanceEntities.RemoveBuff(50, nearBuff)) >= 0);
        Assert.Equal((false, true), (Program.Blinded(state, 50), Program.Blinded(state, 51)));
        state.FlaskGrounds.SetBlinded(51, false, () => 0);
        // A death ends it too (the buff is CancelOnDeath), and the attacker's hits take full damage again.
        Assert.Null((await game.Change(PolytheistSolaris, true)).Error);
        state.Monsters[43] = attacker;
        await game.PolytheistAuraTick();
        Assert.True(Program.Blinded(state, 43));
        Assert.Null((await game.PolytheistDeath()).Error);
        await game.PolytheistAuraTick();
        Assert.False(Program.Blinded(state, 43) || Program.Blinded(state, 50));
        state.PlayerLife = state.PlayerMaxLife;
        uint allocated = await Hit(false);
        Assert.Equal((Math.Round(allocated * .9), Math.Round(allocated * 1.4)), (blinded, blindedShocked));

        // A released projectile keeps the chance against a blinded target for its contact (the fixture closes the state).
        await using var runtime = new ProjectileRuntimeFixture(state);
        var use = ProgramValue<ProjectileUse>("CaptureGemProjectile", state, runtime.Session, fireball.Id, fireball, (at.X + 50, at.Y),
            ProgramValue<object>("CaptureSkillCriticalRoll", state, fireball.Id));
        Assert.Equal(2 * spell, Assert.IsType<ProjectileOffense>(use.Payload).VsBlindedCriticalChance);
    }

    [Fact]
    public async Task Polytheist_Abberath_footsteps_trigger_Abberaths_Fury_that_ignites_and_ignites_deal_nothing()
    {
        if (!DataAvailable()) return;
        using var game = new GatewayCharacter(classRow: 6, baseClass: 5, choice: "Polytheist", refundPoints: 40, level: 60);
        var state = game.State;
        state.PlayerStats[Program.Stats.Key("fire_damage_+%")!.Value] = 100;
        // With the skill's 25% chance to ignite, every hit ignites.
        state.PlayerStats[Program.Stats.Key("chance_to_ignite_%")!.Value] = 75;
        double now = state.Clock.Elapsed.TotalSeconds;
        var at = state.Walk.Position(now);
        state.Walk.Teleport(at.X, at.Y, now);
        // The footsteps of `seconds` of walking at `speed` times the base walking speed (FootstepsDue), or of standing.
        int Steps(double seconds, double speed = 1, bool walk = true)
        {
            double t = state.Clock.Elapsed.TotalSeconds, run = 0;
            state.Walk.SetSpeed(Program.WalkSpeed * speed, t);
            if (walk) state.Walk.Order(at.X + 90, at.Y, t);
            Assert.True(!walk || state.Walk.ArrivesAt > t + seconds);
            int steps = Program.FootstepsDue(state, ref run, t, t + seconds);
            state.Walk.Teleport(at.X, at.Y, t);
            state.Walk.SetSpeed(Program.WalkSpeed, t);
            return steps;
        }
        (int Damage, bool Ignited) Ignite()
        {
            var stats = Program.CharacterPlayerStats(state);
            int Get(string name) => stats.GetValueOrDefault(Program.Stats.Key(name)!.Value);
            var probe = new PlayerElementalAilments();
            var fire = DamagePacket.Of(DamageType.Fire, 1000, 1000);
            probe.Apply(fire, fire, true, 1000, 0, Get, new Random(1));
            bool ignited = probe.Snapshot(.5).Ignite;
            return (probe.Advance(0, 1, Get, 0, 75, false), ignited);
        }
        // The run animation's two footsteps per 800 ms loop (the first 233 ms in), faster with the walk's speed.
        Assert.Equal((0, 1, 2, 1), (PassiveGrantedSkills.Footsteps(0, .2), PassiveGrantedSkills.Footsteps(.233, .633),
            PassiveGrantedSkills.Footsteps(0, .8), PassiveGrantedSkills.Footsteps(.633, 1.05)));
        Assert.Equal((2, 0, 2, 4), (Steps(.8), Steps(.8, walk: false), Steps(.4, speed: 2), Steps(.8, speed: 2)));
        // A plain character has no footstep skill, and an ignite burns it.
        Assert.Empty(Program.FootstepSkills(state, now));
        var plainIgnite = Ignite();
        Assert.True(plainIgnite.Damage > 0 && plainIgnite.Ignited);

        Assert.Null((await game.Change([59443, 61900], true)).Error);
        // Triggers Level 20 Abberath's Fury: 300-450 fire, here with 100% increased fire damage.
        var skill = Assert.Single(Program.FootstepSkills(state, now));
        Assert.Equal(("RepeatingShockwave", 20), (skill.EffectId, skill.Level));
        var hit = Program.GrantedSpellHit(state, skill.Effect, skill.Level);
        Assert.Equal((600.0, 900.0, 900.0), (hit[DamageType.Fire].Min, hit[DamageType.Fire].Max, hit.MaxTotal));
        // A footstep explodes where the player stands and hits the monster 7 cells away, not the one at 8.
        var (near, far, bytes) = await game.PolytheistFootstep(skill, PassiveGrantedSkills.FootstepExplosionRadius);
        uint taken = near.MaxLife - near.Life;
        Assert.InRange(taken, 600u, 900u * (uint)state.Pools.SpellCriticalStrikeMultiplier / 100);
        Assert.Equal(far.MaxLife, far.Life);
        // The hit ignites the monster it hits (90% of 300-450 fire per second, × 2 for the increase, × 1.5 on a critical
        // strike) and shows it burning; the other is not ignited.
        now = state.Clock.Elapsed.TotalSeconds;
        Assert.InRange(near.Ignites.PerSecond(now), .9 * 300 * 2, .9 * 450 * 2 * 1.5);
        // The buff's timer (u68) and the empty list count close the message; the rest names the monster and instance.
        var shown = Program.IgnitedBuff(48, near.Ignites.Buff!.Value, 0);
        Assert.True(bytes.AsSpan().IndexOf(shown.AsSpan(0, shown.Length - 8)) >= 0);
        Assert.True(far.Ignites.Idle);
        string art = Program.Art.Animated(PassiveGrantedSkills.ExplosionArtVariation, 0)!;
        Assert.Equal("Metadata/Effects/Microtransactions/Aura/Uniques/Disjunction/Abberaths_Hooves_fireimpact.ao", art);
        Assert.True(bytes.AsSpan().IndexOf(new EffectObject(at.X, at.Y, art).Blob()) >= 0);
        // No footstep skill while frozen.
        var cold = DamagePacket.Of(DamageType.Cold, state.PlayerMaxLife, state.PlayerMaxLife);
        lock (state.Monsters)
            Program.ApplyPlayerAilmentsLocked(state, cold, cold, true, false, now, Program.CharacterPlayerStats(state));
        Assert.Empty(Program.FootstepSkills(state, now));
        state.ElementalAilments.Clear();
        // Burning Hoofprints: the client picks the goat footprints from the player stats; Unaffected by Ignite keeps the
        // ignite (is_ignited) and takes its damage.
        var final = ProgramValue<IEnumerable<(uint Key, int Value)>>("FinalPlayerStats", state).ToDictionary(s => s.Key, s => s.Value);
        Assert.Equal((1, 20), (final[Program.Stats.Key("goat_footprints_from_item")!.Value],
            final[Program.Stats.Key("display_abberaths_hooves_skill_level")!.Value]));
        Assert.Equal((0, true), Ignite());

        // Devout of Gruthkul disables the spell (and would zero its damage); a refund brings it back.
        Assert.Null((await game.Change([30769, 5266], true)).Error);
        Assert.Empty(Program.FootstepSkills(state, now));
        Assert.Equal(0, Program.GrantedSpellHit(state, skill.Effect, skill.Level).MaxTotal);
        Assert.Null((await game.Change([5266, 30769], false, refundPoints: true)).Error);
        Assert.Single(Program.FootstepSkills(state, now));
        Assert.Null((await game.Change([61900, 59443], false, refundPoints: true)).Error);
        Assert.Empty(Program.FootstepSkills(state, now));
        Assert.Equal(plainIgnite, Ignite());

        // Ignites on monsters (wiki Ignite): a critical strike always ignites, +50% damage over time multiplier; a hit
        // without one needs the chance. Fire and Ignite Resistant: 75% fire resistance and 80% shorter ignites.
        int Doubled(string name) => name == "fire_damage_+%" ? 100 : 0;
        SpawnedMonster Monster(MonsterModSet? mods = null, uint life = 100000) =>
            new("Polytheist ignite target", new(at.X + 3, at.Y, 30), 0, 2, life) { Hostile = true, Mods = mods ?? MonsterModSet.None };
        var resistant = new MonsterModSet([], [], new MonsterModEffects(new Dictionary<string, int>
        {
            ["base_fire_damage_resistance_%"] = 75, ["base_self_ignite_duration_-%"] = 80,
        }));
        Assert.Null(Program.IgniteFromHit(100, false, Doubled, 1, Monster(), new Random(1)));
        Assert.Null(Program.IgniteFromHit(0, true, Doubled, 1, Monster(), new Random(1)));
        var critical = Program.IgniteFromHit(100, true, Doubled, 1, Monster(), new Random(1))!.Value;
        Assert.Equal((270.0, 4.0), (critical.DamagePerSecond, critical.RemainingSeconds));
        // Burning 100% faster: twice the damage per second for half the duration (wiki Ignite).
        var faster = Program.IgniteFromHit(100, true, n => n == "faster_burn_%" ? 100 : Doubled(n), 1, Monster(), new Random(1))!.Value;
        Assert.Equal((540.0, 2.0), (faster.DamagePerSecond, faster.RemainingSeconds));
        var chanced = Program.IgniteFromHit(100, false, n => n == "base_chance_to_ignite_%" ? 100 : Doubled(n), 1, Monster(resistant),
            new Random(1))!.Value;
        Assert.Equal(180.0, chanced.DamagePerSecond);
        Assert.Equal(.8, chanced.RemainingSeconds, 9);
        // Only the strongest burns: 100/s for 4 s with 300/s in the second second.
        var two = new MonsterIgnites();
        two.Add(0, 100, 4);
        two.Add(1, 300, 1);
        Assert.Equal(600, two.Advance(4, 1));
        Assert.True(two.Idle);
        // The player tick burns them: a quarter through 75% fire resistance; one that ended since the last tick deals its
        // last second and its buff comes off; the last point kills.
        now = state.Clock.Elapsed.TotalSeconds;
        var (burning, ended, dying) = (Monster(resistant), Monster(), Monster(life: 10));
        Assert.NotNull(Program.IgniteLocked(state, 50, burning, new(DamageType.Fire, 100.5, 4), now - 1));
        Assert.NotNull(Program.IgniteLocked(state, 51, ended, new(DamageType.Fire, 100, 1), now - 3));
        Program.IgniteLocked(state, 52, dying, new(DamageType.Fire, 100, 4), now - 1);
        ushort buff = burning.Ignites.Buff!.Value, endedBuff = ended.Ignites.Buff!.Value;
        var (burnError, burnt) = await game.PolytheistBurnIgnites((50, burning), (51, ended), (52, dying));
        Assert.Null(burnError);
        Assert.InRange(burning.MaxLife - burning.Life, 25u, 26u);
        Assert.Equal((ended.MaxLife - 100, true), (ended.Life, ended.Ignites.Idle));
        Assert.True(burnt.AsSpan().IndexOf(InstanceEntities.RemoveBuff(51, endedBuff)) >= 0);
        Assert.True(burnt.AsSpan().IndexOf(InstanceEntities.RemoveBuff(50, buff)) < 0);
        Assert.Equal((0u, true, true), (dying.Life, dying.Dead, dying.Ignites.Idle));
    }

    // Every critical draw is the highest: only a 100% chance crits, so a chance below it never does.
    sealed class HighestCriticalDraw : Random
    {
        public override int Next(int maxValue) => maxValue - 1;
    }

    [Fact]
    public async Task Polytheist_combat_call_sites_crit_against_the_Blinded_stop_regeneration_and_slow_attackers()
    {
        if (!DataAvailable()) return;
        using var game = new GatewayCharacter(classRow: 6, baseClass: 5, choice: "Polytheist", level: 60);
        var state = game.State;
        typeof(ClientState).GetProperty(nameof(ClientState.CriticalRandom), BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(state, new HighestCriticalDraw());
        EquipPolytheistGems(game, "Ice Nova");
        var nova = ProgramValue<IEnumerable<(ushort Id, int Effect, string Name, int Level, int Quality, uint Gem)>>(
            "GemSkillEffects", state).Single(g => g.Name == "Ice Nova");
        // Solaris (100% increased Critical Strike Chance against Blinded Enemies) and Abberath.
        Assert.Null((await game.Change(PolytheistSolaris, true)).Error);
        void Set(string name, int value)
        {
            state.PlayerStats[Program.Stats.Key(name)!.Value] = value;
            state.Pools = Program.PoolsFor(state, 60);
        }
        // A 50% attack base (100% against a Blinded enemy); hits of exactly 1000 chaos damage (their own types at 100%
        // reduced), so a critical strike is plain to see; fast attacks that always hit; Ryslatha's and Gruthkul's stats.
        Set(AttackBaseCriticalStrike.UnarmedBaseStat, 5000);
        foreach (string reduced in new[] { "physical_damage_+%", "cold_damage_+%", "fire_damage_+%" }) Set(reduced, -100);
        foreach (string added in new[] { "attack_minimum_added_chaos_damage", "attack_maximum_added_chaos_damage",
            "spell_minimum_added_chaos_damage", "spell_maximum_added_chaos_damage" }) Set(added, 1000);
        Set("global_always_hit", 1);
        Set("attack_speed_+%", 1000);
        Set(Program.EnemyRegenerationOnHit, -100);
        Set(Program.AttackerSlow, -10);
        double now = state.Clock.Elapsed.TotalSeconds;
        var at = state.Walk.Position(now);
        state.Walk.Teleport(at.X, at.Y, now);
        var regenerates = new MonsterModSet([], [], new(new Dictionary<string, int> { ["life_regeneration_rate_per_minute_%"] = 600 }));
        SpawnedMonster Enemy(uint id, int dx)
        {
            var m = new SpawnedMonster("Polytheist call-site target", new(at.X + dx, at.Y, 30), 0, 2, 100000)
            { Hostile = true, Mods = regenerates };
            m.Walk.Teleport(at.X + dx, at.Y, now);
            return state.Monsters[id] = m;
        }
        var (blinded, plain, idle) = (Enemy(60, 2), Enemy(61, -2), Enemy(62, 60));
        state.FlaskGrounds.SetBlinded(60, true, () => 900);
        // One strike on the two near monsters: an ordinary hit on the other one, a critical strike (150%) on the Blinded one.
        async Task Struck(Func<NetworkStream, ConnectionCiphers, Task> strike)
        {
            var (b, p) = (blinded.Life, plain.Life);
            Assert.Null((await game.PolytheistRun(strike)).Error);
            uint taken = p - plain.Life;
            Assert.True(taken > 0);
            Assert.Equal(DamageModel.CriticalDamage((int)taken, 150), (int)(b - blinded.Life));
        }

        // The default attack.
        await Struck(async (stream, cipher) =>
        {
            foreach (var (id, m) in new[] { (60u, blinded), (61u, plain) })
            {
                state.NextAttackAt = 0;
                await Program.Attack("polytheist-integration", stream, cipher, state, new(id, SkillCatalog.FirstSkillId, 1, 1), m,
                    state.Clock.Elapsed.TotalSeconds);
            }
        });
        // Ryslatha through the default attack's hit: in the next regeneration step the two monsters it hit regain nothing
        // and the damaged one it did not hit regenerates.
        idle.Life = 50000;
        Assert.Equal([62u], Program.RegenerateConnectionMonsters(state, state.Clock.Elapsed.TotalSeconds + 1, .5).Select(c => c.Id));

        // An area spell's sweep: Ice Nova's 7.5% with 1200% increased is 97.5%, and 100% against the Blinded monster.
        Set("spell_critical_strike_chance_+%", 1200);
        await Struck((stream, cipher) => ProgramValue<Task>("AreaSkillHit", "polytheist-integration", stream, cipher, state,
            nova.Id, nova, at, at, true, (ushort)0, null, null, null, 1.0, 0u, null, null, 0u));
        // Abberath's Fury's footstep: its 5% with 1850% increased is 97.5%, and 100% against the Blinded monster.
        Set("spell_critical_strike_chance_+%", 1850);
        var skill = Assert.Single(Program.FootstepSkills(state, state.Clock.Elapsed.TotalSeconds));
        await Struck((stream, cipher) => Program.TriggerFootstepSkill("polytheist-integration", stream, cipher, state, skill));

        // Gruthkul through the monster AI: a monster in reach whose attack hit the player recently starts its swing with
        // the slowed attack time. The tick stops its walk where it stands, so its walk ends at the tick's time. Last: the
        // AI loop ends with the state.
        foreach (uint id in new uint[] { 60, 61, 62 }) state.Monsters.Remove(id);
        var attacker = new SpawnedMonster("Polytheist slowed attacker", new(at.X + 2, at.Y, 30), 50, 2, 1000)
        { Hostile = true, Attack = new(SkillCatalog.FirstSkillId, .002, 20, DamagePacket.Of(DamageType.Physical, 1, 1)) };
        attacker.Walk.Teleport(at.X + 2, at.Y, now);
        state.Monsters[63] = attacker;
        attacker.HitPlayerAt = state.Clock.Elapsed.TotalSeconds;
        Assert.Null((await game.PolytheistRun(async (stream, cipher) =>
        {
            var ai = ProgramValue<Task>("RunMonsterAi", "polytheist-integration", stream, cipher, state);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (attacker.BusyUntil == 0 && !ai.IsCompleted) await Task.Delay(5, timeout.Token);
            state.Closed = true;
            await ai;
        })).Error);
        // The swing starts at its send, a moment after the tick stopped the walk; its attack time is the slowed one.
        Assert.InRange(attacker.BusyUntil - attacker.Walk.ArrivesAt, .002 / .9, .002 / .9 + .1);
        Assert.Equal(.002 / .9, ProgramValue<double>("MonsterSwingSeconds", state, attacker, state.Clock.Elapsed.TotalSeconds), 9);
    }

    [Fact]
    public async Task Polytheist_Solaris_projectile_contact_crits_against_the_Blinded()
    {
        if (!DataAvailable()) return;
        await using var f = new ProjectileRuntimeFixture(new(new AreaLayout(1, "polytheist-projectile", 300, 300, 10, 10, null, []), 1, 1)
            { PlayerLife = 100, ActionCount = 7, CriticalRandom = new HighestCriticalDraw() });
        // A 50% chance, 100% against a Blinded target (captured at release), ×101 critical damage.
        var offense = new ProjectileOffense(DamagePacket.Of(DamageType.Physical, 20, 20), true, int.MaxValue, 5000, 10100, "solaris")
        { VsBlindedCriticalChance = 10000 };
        uint Contact(bool blind, double until)
        {
            var target = new SpawnedMonster("Polytheist projectile target", new(40, 10, 10, width: 300, height: 300), 0, 2, 100000);
            f.Conn.Monsters[42] = target;
            f.Conn.FlaskGrounds.SetBlinded(42, blind, () => 900);
            f.Release(f.Shot(new("Metadata/Projectiles/DefaultArrow") { Speed = 100, Range = 100, Radius = 1 }, offense));
            f.Advance(until);
            return target.MaxLife - target.Life;
        }
        Assert.Equal(2020u, Contact(blind: true, until: 1));
        Assert.Equal(20u, Contact(blind: false, until: 2));
    }

    // The methods a method's body calls (an async method's: its state machine's), by name.
    static HashSet<string> Callees(string method)
    {
        var info = typeof(Program).GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var body = info.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            .GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? info;
        byte[] il = body.GetMethodBody()!.GetILAsByteArray()!;
        var names = new HashSet<string>();
        for (int i = 0; i + 4 < il.Length; i++)
            if (il[i] is 0x28 or 0x6F)   // call, callvirt
                try { names.Add(body.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1))!.Name); }
                catch (Exception e) when (e is ArgumentException or BadImageFormatException) { }
        return names;
    }

    [Fact]
    public void Polytheist_runtime_loops_are_started_and_reach_their_consumers()
    {
        // The area entry starts the footstep loop with the player tick; the tick reaches the passive aura, the monsters'
        // ignites, the attacker slow and the while-moving stats; the footstep loop triggers the skill per footstep; the
        // regeneration loop steps with the on-hit regeneration stat.
        Assert.Superset(new HashSet<string> { "RunPlayerTick", "RunFootstepSkills", "RunMonsterRegeneration", "RunMonsterAi" },
            Callees("OnClientMessages"));
        Assert.Superset(new HashSet<string> { "TickPassiveAuras", "BurnIgnited", "SendAttackerSlows", "SendWhileMovingStats" },
            Callees("RunPlayerTick"));
        Assert.Superset(new HashSet<string> { "FootstepsDue", "FootstepSkills", "TriggerFootstepSkill" }, Callees("RunFootstepSkills"));
        Assert.Contains("RegenerateConnectionMonsters", Callees("RunMonsterRegeneration"));
    }

    [Fact]
    public async Task Saboteur_Born_in_the_Shadows_blinds_nearby_enemies_and_takes_less_damage_from_them()
    {
        if (!DataAvailable()) return;
        using var game = new GatewayCharacter(classRow: 5, baseClass: 6, choice: "Saboteur", family: AscendancyFamily.Normal, level: 60);
        var state = game.State;
        double now = state.Clock.Elapsed.TotalSeconds;
        var at = state.Walk.Position(now);
        state.Walk.Teleport(at.X, at.Y, now);
        var attacker = new SpawnedMonster("Saboteur blind target", new(at.X + 2, at.Y, 30), 0, 2, 1000)
        { Hostile = true, Attack = new(SkillCatalog.FirstSkillId, .002, 20, DamagePacket.Of(DamageType.Physical, 100, 100)) };
        attacker.Walk.Teleport(at.X + 2, at.Y, now);
        uint plain = await game.PolytheistMonsterHit(false, attacker: attacker);
        // The same SaboteurBlind template as Devout of Solaris, with 15% reduced Damage taken from Blinded Enemies.
        Assert.Null((await game.Change([64842, 5087], true)).Error);
        state.Monsters[43] = attacker;
        Assert.NotEmpty((await game.PolytheistAuraTick()).Bytes);
        Assert.True(Program.Blinded(state, 43));
        Assert.Equal(Math.Round(plain * .85), await game.PolytheistMonsterHit(false, attacker: attacker));
    }
}
