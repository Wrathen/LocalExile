using GameServer.Core.Protocol.Instance;
using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

public sealed partial class AscendancyGatewayIntegrationTests
{
    // Shadow (classRow 5, base class 6). No node grants points, so each build spends the class's eight.
    // Together they reach the 18 non-start nodes; A also holds both fishing-rod notables and Sea Legs.
    internal static readonly ushort[][] SurfcasterBuilds =
    [
        [48926, 64012, 33031, 37082, 36782, 37553, 42290, 61696],
        [41643, 60505, 34767, 61246, 53922, 6252, 37534, 52944],
        [41643, 60505, 56184, 52264, 36782, 37553, 42290, 61696],
    ];

    static int SurfcasterStat(ClientState state, string name) =>
        Program.CharacterPlayerStats(state).GetValueOrDefault(Program.Stats.Key(name)!.Value);

    static GatewayCharacter Surfcaster() => new(classRow: 5, baseClass: 6, choice: "Surfcaster", refundPoints: 100, level: 60);

    static void SetDev(ClientState state, params (string Name, int Value)[] stats)
    {
        lock (state.Monsters)
            foreach (var (name, value) in stats)
                if (value == 0) state.PlayerStats.Remove(Program.Stats.Key(name)!.Value);
                else state.PlayerStats[Program.Stats.Key(name)!.Value] = value;
        state.Pools = Program.PoolsFor(state, 60);
    }

    [Fact]
    public void Surfcaster_checklist_is_the_actual_graph_and_three_builds_cover_every_node()
    {
        if (!DataAvailable()) return;
        var definition = Program.Ascendancies.Find(6, AscendancyFamily.Phrecia, "Surfcaster")!;
        Assert.Equal(new ushort[] { 18635, 48926, 37082, 53922, 64012, 41643, 60505, 34767, 6252, 33031, 52944, 36782,
            37553, 37534, 52264, 56184, 61696, 42290, 61246 }.Order(), definition.Nodes.Keys.Order());
        Assert.Equal((ushort)18635, definition.StartNode);
        Assert.All(definition.Nodes.Values, n => Assert.Equal(0, n.PointsGranted));
        Assert.Equal(definition.Nodes.Keys.Where(n => n != definition.StartNode).Order(),
            SurfcasterBuilds.SelectMany(b => b).Distinct().Order());
        // The default Phrecia gate stays closed without Game_VERIFIED_SURFCASTER.
        Assert.Throws<AscendancyRuleException>(() => AscendancyClientCompatibility.RequirePlayable(definition.Identity));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Surfcaster_legal_build_paid_full_refund_reallocation_and_reload(int index)
    {
        if (!DataAvailable()) return;
        using var game = Surfcaster();
        var nodes = SurfcasterBuilds[index];
        Assert.Null((await game.Change(nodes, true)).Error);
        Assert.Equal(8, game.State.MainAscendancy.Allocated.Count);
        // Four small nodes at 4% each, and Ghost of the Deep's 23% in builds A and C.
        Assert.Equal(nodes.Contains((ushort)37553) ? 39 : 16, SurfcasterStat(game.State, "cast_speed_+%"));
        Assert.Equal(nodes.Order(), game.Reload().MainAscendancy.Allocated.Order());
        Assert.Null((await game.Change(nodes.Reverse().ToArray(), false, refundPoints: true)).Error);
        Assert.Empty(game.State.MainAscendancy.Allocated);
        Assert.Equal((ushort)60, game.State.RefundPoints);
        Assert.Equal(0, SurfcasterStat(game.State, "cast_speed_+%"));
        Assert.Null((await game.Change(nodes, true)).Error);
        Assert.Equal(nodes.Order(), game.Reload().MainAscendancy.Allocated.Order());
        Assert.Equal((ushort)60, game.Reload().RefundPoints);
    }

    [Fact]
    public async Task Surfcaster_rod_notables_and_Sea_Legs_match_plain_equivalents()
    {
        if (!DataAvailable()) return;
        using var game = Surfcaster();
        using var plain = Surfcaster();
        Assert.Null((await game.Change(SurfcasterBuilds[0], true)).Error);

        // Sea Legs (61696) against plain +40% cold and lightning resistance with both overcap flags.
        (string, int)[] overcapped = [("base_cold_damage_resistance_%", 100), ("base_lightning_damage_resistance_%", 100)];
        SetDev(game.State, overcapped);
        SetDev(plain.State, [.. overcapped, (ResistanceOvercap.ColdAndLightningResistance, 40),
            (ResistanceOvercap.ColdEvasionFlag, 1), (ResistanceOvercap.LightningCritFlag, 1)]);
        Assert.True(game.State.Pools.ColdOvercapEvasionPct > 0);
        Assert.Equal(plain.State.Pools.ColdOvercapEvasionPct, game.State.Pools.ColdOvercapEvasionPct);
        Assert.Equal(plain.State.Pools.LightningOvercapCritPct, game.State.Pools.LightningOvercapCritPct);
        Assert.Equal(plain.State.Pools.ColdResistance, game.State.Pools.ColdResistance);
        Assert.Equal(plain.State.Pools.LightningResistance, game.State.Pools.LightningResistance);
        Assert.Equal(plain.State.Pools.Evasion, game.State.Pools.Evasion);
        Assert.Equal(plain.State.Pools.SpellCriticalStrikeChance, game.State.Pools.SpellCriticalStrikeChance);

        // Screaming Reels (64012) and Baited Breath (37082) hold only with a Fishing Rod in the active main hand.
        foreach (var character in new[] { game, plain })
            EquipFamilyGem(character, ItemCatalog.Create(Program.Items.Find("Fireball")!));
        var (skill, effect, _, _, _, _) = ProgramValue<IEnumerable<(ushort, int, string, int, int, uint)>>(
            "GemSkillEffects", game.State).Single();
        Assert.Equal(750, Program.Skills.CastMs(effect));
        int? CastMs(ClientState state) => ProgramValue<int?>("CastMs", state, effect, skill);
        (int Speed, int Rate, int Crit) Record(ClientState state)
        {
            var stats = FamilySkills(state).Single(s => s.Id == skill).Stats!.ToDictionary(s => s.Key, s => s.Value);
            int Get(string name) => stats.GetValueOrDefault(Program.Stats.Key(name)!.Value);
            return (Get("cast_speed_+%"), Get("hundred_times_casts_per_second"), Get("spell_critical_strike_chance"));
        }
        Assert.Equal(540, CastMs(game.State)); // 750 / 1.39 (small nodes and Ghost of the Deep): no rod, no multiplier
        Assert.False(Program.RollCriticalStrike(game.State, 0));

        var rod = Program.Items.Find("FishingRod1")!;
        Assert.Equal("FishingRod", rod.Class);
        foreach (var character in new[] { game, plain })
            character.Items.Add(PlayerInventories.Weapon1, 0, 0, ItemCatalog.Create(rod));
        // round(139 × 1.6 − 100) = 122% (client calculator 0x142709610); plain: the same rod with 122% increased.
        SetDev(game.State);
        SetDev(plain.State, ("base_cast_speed_+%", 122));
        Assert.Equal(338, CastMs(game.State)); // 750 / 2.22
        Assert.Equal(CastMs(plain.State), CastMs(game.State));
        Assert.Equal((122, 296, 10_000), Record(game.State));
        Assert.Equal((122, 296), (Record(plain.State).Speed, Record(plain.State).Rate));
        Assert.NotEqual(10_000, Record(plain.State).Crit);

        // Every hit crits without a roll, even from a 0% base; the panel shows 100% where the base is not 0.
        Assert.Equal(10_000, game.State.Pools.AttackCriticalStrikeChance);
        Assert.Equal(10_000, game.State.Pools.SpellCriticalStrikeChance);
        Assert.True(plain.State.Pools.AttackCriticalStrikeChance is > 0 and < 10_000);
        Assert.True(Program.RollCriticalStrike(game.State, 0, attack: false));
        Assert.False(Program.RollCriticalStrike(plain.State, 0, attack: false));
        // Never deal critical strikes wins.
        SetDev(game.State, ("global_cannot_crit", 1));
        Assert.False(Program.RollCriticalStrike(game.State, 10_000));
        Assert.Equal(0, game.State.Pools.AttackCriticalStrikeChance);
        SetDev(game.State, ("global_cannot_crit", 0));

        // Off the rod, both effects end.
        var held = Assert.Single(game.Items.Items(PlayerInventories.Weapon1));
        Assert.NotNull(game.Items.Remove(PlayerInventories.Weapon1, held.Id));
        SetDev(game.State);
        Assert.Equal(540, CastMs(game.State));
        Assert.False(Program.RollCriticalStrike(game.State, 0));
        Assert.Equal(39, Record(game.State).Speed);

        // The record's total holds a support's own cast speed as well: Faster Casting's 20 at level 1 and 5 from the
        // blue socket's 10 quality (0.5 per 1%), so 39 + 25.
        SetDev(game.State, ("virtual_intelligence_for_requirements", 100));
        Assert.NotNull(game.Items.Remove(PlayerInventories.Helmet, Assert.Single(game.Items.Items(PlayerInventories.Helmet)).Id));
        EquipFamilyGem(game, ItemCatalog.Create(Program.Items.Find("Fireball")!),
            ItemCatalog.Create(Program.Items.Find("Metadata/Items/Gems/SupportGemFasterCast")!));
        skill = ProgramValue<IEnumerable<(ushort, int, string, int, int, uint)>>("GemSkillEffects", game.State)
            .Single(g => g.Item2 == effect).Item1;
        Assert.Equal(64, Record(game.State).Speed);
    }

    // Ghost of the Deep (37553): BuffTemplates 671's values on BuffDefinitions 2550's stats join the passives' sum, so the
    // existing consumers read them like any other source; the buff is published on the player and goes with the refund.
    [Fact]
    public async Task Surfcaster_Ghost_of_the_Deep_buff_matches_plain_stats_and_is_published()
    {
        if (!DataAvailable()) return;
        using var game = Surfcaster();
        using var plain = Surfcaster();
        game.State.PlayerSent = true;
        var buff = Assert.Single(PassiveBuffAuras.SelfBuffsFor([37553], ruthless: false));
        Assert.Equal((ushort)2550, buff.Definition);
        Assert.Equal([("base_movement_velocity_+%", 23), ("attack_speed_+%", 23), ("base_cast_speed_+%", 23),
            ("damage_+%", 23), ("cant_touch_this", 1), ("monster_is_touched_by_ghost", 1)],
            buff.Stats.Select(s => (Program.Stats.Name(s.Key), s.Value)));
        int baseDamage = SurfcasterStat(plain.State, "main_hand_maximum_total_damage");
        double baseWalk = plain.State.Walk.Speed;

        var (error, bytes) = await game.Change([36782, 37553], true);
        Assert.Null(error);
        ushort instance = game.State.PassiveBuffs[2550];
        Assert.True(bytes.AsSpan().IndexOf(InstanceEntities.AddBuff(Program.PlayerId, instance, 2550, Program.PlayerId)) >= 0);
        // Plain: the small node's 4% cast speed plus the buff's damage as /set_stat values.
        int baseAttackSpeed = SurfcasterStat(plain.State, "main_hand_attack_speed_+%");
        SetDev(plain.State, ("base_cast_speed_+%", 27), ("damage_+%", 23));
        foreach (string stat in new[] { "cast_speed_+%", "main_hand_maximum_total_damage" })
            Assert.Equal(SurfcasterStat(plain.State, stat), SurfcasterStat(game.State, stat));
        // The pools give the unarmed swing 10% more attack speed: 100 × 1.1 − 100 = 10 without, 123 × 1.1 − 100 with.
        Assert.Equal((10, 35), (baseAttackSpeed, SurfcasterStat(game.State, "main_hand_attack_speed_+%")));
        Assert.Equal(27, SurfcasterStat(game.State, "cast_speed_+%"));
        Assert.True(SurfcasterStat(game.State, "main_hand_maximum_total_damage") > baseDamage);
        Assert.Equal(23, game.State.Pools.MovementVelocityPct);
        Assert.Equal(baseWalk * 1.23, game.State.Walk.Speed, 6);
        Assert.Null(PassiveBuffAuras.Presentation(game.State)); // already published

        // A reload (a new area's connection) has the stats, and entering the area publishes the buff again.
        var reloaded = game.Reload();
        Assert.Equal(23, reloaded.Pools.MovementVelocityPct);
        (_, bytes) = await game.SurfcasterEnter(reloaded);
        Assert.True(bytes.AsSpan().IndexOf(InstanceEntities.AddBuff(Program.PlayerId, reloaded.PassiveBuffs[2550], 2550,
            Program.PlayerId)) >= 0);

        (error, bytes) = await game.Change([37553, 36782], false, refundPoints: true);
        Assert.Null(error);
        Assert.True(bytes.AsSpan().IndexOf(InstanceEntities.RemoveBuff(Program.PlayerId, instance)) >= 0);
        Assert.Empty(game.State.PassiveBuffs);
        Assert.Equal(0, game.State.Pools.MovementVelocityPct);
        Assert.Equal(baseWalk, game.State.Walk.Speed, 6);
        Assert.Equal(0, SurfcasterStat(game.State, "cast_speed_+%"));
    }

    // Changing Seasons (52944): any hit can chill the player, and the chill speeds up instead of slowing.
    [Fact]
    public async Task Surfcaster_Changing_Seasons_chills_from_any_hit_and_reverses_the_chill()
    {
        if (!DataAvailable()) return;
        using var game = Surfcaster();
        using var plain = Surfcaster();
        Assert.Null((await game.Change([41643, 60505, 37534, 52944], true)).Error);
        Assert.Null((await plain.Change([41643, 60505, 37534], true)).Error);
        double walk = plain.State.Walk.Speed;
        (double Factor, int Virtual, int Reduced, int Combined, double Walk) Hit(ClientState state, DamageType type)
        {
            lock (state.Monsters)
            {
                var hit = DamagePacket.Of(type, (int)state.PlayerMaxLife, (int)state.PlayerMaxLife);
                Program.ApplyPlayerAilmentsLocked(state, hit, hit, false, false, state.Clock.Elapsed.TotalSeconds,
                    Program.CharacterPlayerStats(state));
            }
            return (Math.Round(Program.PlayerActionFactor(state), 6), SurfcasterStat(state, "virtual_action_speed_+%"),
                SurfcasterStat(state, "action_speed_-%"), SurfcasterStat(state, "intermediary_combined_action_speed_+%"),
                Math.Round(state.Walk.Speed / walk, 6));
        }
        // A full-life physical hit: no chill without the node; with it a 30% chill that is 30% more action speed.
        Assert.Equal((1.0, 0, 0, 0, 1.0), Hit(plain.State, DamageType.Physical));
        Assert.Equal((1.3, 30, -30, 30, 1.3), Hit(game.State, DamageType.Physical));
        // The player tick shows the chill as the chilled buff on the player, and takes it off when it ends.
        var chilled = game.State.ElementalAilments.Snapshot(game.State.Clock.Elapsed.TotalSeconds);
        ushort chill = MonsterElementalAilments.Instance(MonsterElementalAilments.Kind.Chill);
        Assert.Equal([Program.AilmentBuff(Program.PlayerId, chill, MonsterElementalAilments.Kind.Chill, 30)],
            Program.PlayerAilmentBuffs(default, chilled).Select(p => p.Packet));
        Assert.Equal([InstanceEntities.RemoveBuff(Program.PlayerId, chill)],
            Program.PlayerAilmentBuffs(chilled, default).Select(p => p.Packet));
        // A plain cold hit slows by the same chill.
        Assert.Equal((.7, -30, 30, -30, .7), Hit(plain.State, DamageType.Cold));
        plain.State.ElementalAilments.Clear();
        SetDev(plain.State, ("chill_effect_is_reversed", 1), ("all_damage_taken_can_chill", 1));
        Assert.Equal(Hit(game.State, DamageType.Physical), Hit(plain.State, DamageType.Physical));
    }

    [Fact]
    public void Surfcaster_reversed_chill_leaves_freeze_and_unaffected_action_speed()
    {
        static int Reversed(string name) => name == "chill_effect_is_reversed" ? 1 : 0;
        var cold = DamagePacket.Of(DamageType.Cold, 200, 200);
        var reversed = new PlayerElementalAilments();
        var plain = new PlayerElementalAilments();
        // A critical 20%-of-life cold hit: floor(50 × 0.2^0.4) = 26% chill for 2 s, frozen for 6 × 0.2 = 1.2 s.
        reversed.Apply(cold, cold, true, 1000, 0, Reversed, new Random(1));
        plain.Apply(cold, cold, true, 1000, 0, _ => 0, new Random(1));
        Assert.Equal(0, reversed.ActionFactor(1));
        Assert.Equal(1.26, reversed.ActionFactor(1.5), 6);
        Assert.Equal(.74, plain.ActionFactor(1.5), 6);
        reversed.Reconcile(name => Reversed(name) | (name == "not_affected_by_action_speed_modifiers" ? 1 : 0));
        Assert.Equal(1, reversed.ActionFactor(1.5));
    }

    // The player's chill, freeze and shock on monsters (MonsterElementalAilments), which Stormy Seas and the later nodes
    // build on: threshold 1000 throughout.
    [Fact]
    public void Surfcaster_monster_ailments_floor_caps_freeze_threshold_and_strongest()
    {
        static int None(string _) => 0;
        static Func<string, int> With(string stat, int value) => name => name == stat ? value : 0;
        var random = new Random(1);
        Assert.Equal((0, 5, 30, 50), (MonsterElementalAilments.Effectiveness(3, 1000, 1, 30),
            MonsterElementalAilments.Effectiveness(3.2, 1000, 1, 30), MonsterElementalAilments.Effectiveness(1000, 1000, 1, 30),
            MonsterElementalAilments.Effectiveness(2000, 1000, 1, 50)));
        // A critical cold hit of 20%: chill floor(50 × 0.2^0.4) = 26 for 2 s and a 1.2 s freeze; no lightning, no shock.
        var a = new MonsterElementalAilments();
        var added = a.Inflict(200, 0, true, 1000, 0, None, None, random);
        Assert.Equal([(MonsterElementalAilments.Kind.Chill, 26.0, 2.0), (MonsterElementalAilments.Kind.Freeze, 100.0, 1.2)],
            added.Select(e => (e.Kind, e.Magnitude, Math.Round(e.Until - e.From, 6))));
        Assert.Equal(0, a.ActionFactor(1));
        Assert.Equal(.74, a.ActionFactor(1.5), 6);
        // A weaker chill that lasts longer applies once the strongest ends; then nothing.
        a.Inflict(10, 0, false, 1000, 1, None, None, random);
        Assert.Equal((26, 7, 0), ((int)a.Magnitude(MonsterElementalAilments.Kind.Chill, 1.9),
            (int)a.Magnitude(MonsterElementalAilments.Kind.Chill, 2.5), (int)a.Magnitude(MonsterElementalAilments.Kind.Chill, 3)));
        // Freeze: 4% gives 0.24 s, discarded; 50% increased duration makes it 0.36 s; a non-critical hit needs a chance.
        var b = new MonsterElementalAilments();
        Assert.DoesNotContain(b.Inflict(40, 0, true, 1000, 0, None, None, random), e => e.Kind == MonsterElementalAilments.Kind.Freeze);
        Assert.Contains(b.Inflict(40, 0, true, 1000, 0, With("virtual_freeze_duration_+%", 50), None, random),
            e => e.Kind == MonsterElementalAilments.Kind.Freeze && Math.Abs(e.Until - .36) < 1e-9);
        // The attacker's increase and the monster's own add up (PoB CalcOffence): 50% increased against 50% reduced on
        // itself leaves 0.6 s; of the Ice's −80 (80% increased on itself) makes it 1.08 s.
        double Frozen(Func<string, int> attacker, Func<string, int> target) => b.Inflict(100, 0, true, 1000, 0, attacker,
            target, random).Single(e => e.Kind == MonsterElementalAilments.Kind.Freeze).Until;
        Assert.Equal(.6, Frozen(With("virtual_freeze_duration_+%", 50), With("base_self_freeze_duration_-%", 50)), 6);
        Assert.Equal(1.08, Frozen(None, With("base_self_freeze_duration_-%", -80)), 6);
        Assert.Empty(b.Inflict(0, 1000, false, 1000, 0, None, None, random));
        Assert.Equal(50, b.Inflict(0, 1000, false, 1000, 0, With("chance_to_shock_%", 100), None, random).Single().Magnitude);
        // Shock from cold needs cold_damage_can_shock; the monster's own modifiers and immunity count.
        Assert.DoesNotContain(b.Inflict(102, 0, true, 1000, 0, None, None, random), e => e.Kind == MonsterElementalAilments.Kind.Shock);
        Assert.Equal(20, b.Inflict(102, 0, true, 1000, 0, With("cold_damage_can_shock", 1), None, random)
            .Single(e => e.Kind == MonsterElementalAilments.Kind.Shock).Magnitude);
        Assert.Empty(b.Inflict(0, 102, true, 1000, 0, None, With("shocked_effect_on_self_+%", -80), random)); // 4%
        Assert.DoesNotContain(b.Inflict(200, 0, true, 1000, 0, None, With("cannot_be_frozen", 1), random),
            e => e.Kind == MonsterElementalAilments.Kind.Freeze);

        // The client sees a kind again only when its magnitude changes, on the kind's own instance, and its buff goes
        // when the last one ends. The connection's counter never reaches those instances.
        var c = new MonsterElementalAilments();
        ushort? chill = MonsterElementalAilments.Instance(MonsterElementalAilments.Kind.Chill);
        c.Inflict(200, 0, false, 1000, 0, None, None, random);
        Assert.Equal([(MonsterElementalAilments.Kind.Chill, (ushort?)null, chill, 26)], c.Present(0));
        c.Inflict(200, 0, false, 1000, 1, None, None, random);
        Assert.Empty(c.Present(1));
        c.Inflict(1000, 0, false, 1000, 1, None, None, random);
        Assert.Equal([(MonsterElementalAilments.Kind.Chill, chill, chill, 30)], c.Present(1));
        Assert.Equal([(MonsterElementalAilments.Kind.Chill, chill, (ushort?)null, 0)], c.Present(3.5));
        Assert.True(c.Idle);
        var counter = new ClientState(new(0, "buffs", 8, 8, 1, 1, null, []), 0, 1)
            { NextBuff = MonsterElementalAilments.FirstInstance - 1 };
        Assert.Equal((MonsterElementalAilments.FirstInstance - 1, 1), ((int)counter.NextBuff++, (int)counter.NextBuff));
    }

    sealed partial class GatewayCharacter
    {
        // Runs the actual monster AI loop until done holds (checked under the Monsters lock, at most 5 s), then ends it as
        // a closed connection would; the tick that made it true still sends its packets.
        public Task<(string? Error, byte[] Bytes)> SurfcasterAiUntil(Func<bool> done) => Exchange(async (stream, cipher) =>
        {
            State.Closed = false;
            var ai = (Task)GatewayMethod("RunMonsterAi").Invoke(null, ["surfcaster-ai", stream, cipher, State])!;
            for (var until = DateTime.UtcNow.AddSeconds(5); DateTime.UtcNow < until; await Task.Delay(5))
                lock (State.Monsters) if (done()) break;
            State.Closed = true;
            await ai;
            return null;
        });

        public Task<(string? Error, byte[] Bytes)> SurfcasterAttack(uint target) => Exchange(async (stream, cipher) =>
        {
            var use = InstanceTransfer.ReadUseSkillOnObject(new GameServer.Core.Protocol.Login.LoginWriter().U16(0x24)
                .U32(target).U16(SkillCatalog.FirstSkillId).U16(0).U16(0x409).ToArray());
            await (Task)GatewayMethod("Attack").Invoke(null, ["surfcaster-attack", stream, cipher, State, use,
                State.Monsters[target], State.Clock.Elapsed.TotalSeconds])!;
            return null;
        });
    }

    static SpawnedMonster SurfcasterTarget(ClientState state, uint id, int dx, uint life = 10_000, MonsterAttack? attack = null,
        MonsterModSet? mods = null)
    {
        var (x, y) = state.Walk.Position(state.Clock.Elapsed.TotalSeconds);
        var monster = new SpawnedMonster($"target 0x{id:x}", new(x + dx, y, 30), 100, 2, life)
            { Attack = attack, Aggro = true, Mods = mods ?? MonsterModSet.None };
        lock (state.Monsters) state.Monsters[id] = monster;
        return monster;
    }

    // Shock is increased damage taken, added to the mods' own (PoB DamageTaken INC), on hits and burns alike.
    [Fact]
    public void Surfcaster_monster_shock_matches_plain_increased_damage_taken()
    {
        if (!DataAvailable()) return;
        var shocked = new SpawnedMonster("shocked", new(0, 0, 30), 0, 2, 1000);
        var plain = new SpawnedMonster("plain", new(0, 0, 30), 0, 2, 1000)
            { Mods = new([], [], new(new Dictionary<string, int> { ["base_damage_taken_+%"] = 20 })) };
        shocked.Ailments.Inflict(0, 102, true, 1000, 0, _ => 0, _ => 0, new Random(1));
        Assert.Equal(20, shocked.Ailments.Magnitude(MonsterElementalAilments.Kind.Shock, 1));
        var hit = DamagePacket.Of(DamageType.Lightning, 100, 100);
        Assert.Equal(100, Program.MonsterHitDamage(shocked, hit, false, true, 3, default).Damage); // expired
        Assert.Equal((120, 120), (Program.MonsterHitDamage(shocked, hit, false, true, 1, default).Damage,
            Program.MonsterHitDamage(plain, hit, false, true, 1, default).Damage));
        var burn = new DamageOverTime(DamageType.Fire, 100, 10);
        Assert.Equal((120, 120), (Program.MonsterBurn(shocked, burn, 1, 1).Damage, Program.MonsterBurn(plain, burn, 1, 1).Damage));
    }

    // Chill slows a monster's swing and walk on the server and, through the sent stats, in the client; a frozen monster
    // stops and does not swing. The buffs show the ailments.
    [Fact]
    public async Task Surfcaster_monster_chill_and_freeze_reach_the_monster_ai()
    {
        if (!DataAvailable()) return;
        using var game = Surfcaster();
        var attack = new MonsterAttack(SkillCatalog.FirstSkillId, 10, 20, DamagePacket.Of(DamageType.Physical, 1, 1));
        var chilled = SurfcasterTarget(game.State, 0x101, 3, attack: attack);
        var plain = SurfcasterTarget(game.State, 0x102, -3, attack: attack);
        var frozen = SurfcasterTarget(game.State, 0x103, 0, attack: attack);
        double now = game.State.Clock.Elapsed.TotalSeconds;
        chilled.Ailments.Inflict(1000, 0, false, 1000, now, _ => 0, _ => 0, Random.Shared); // 30%
        frozen.Ailments.Inflict(1000, 0, true, 1000, now, _ => 0, _ => 0, Random.Shared);  // 30% and 3 s frozen
        frozen.Walk.Order(100, 100, now);

        var (_, bytes) = await game.SurfcasterAiUntil(() => plain.BusyUntil > now && chilled.BusyUntil > now);
        Assert.True(plain.BusyUntil > now);
        // Each swing starts at its own send, so the two starts can lie a moment apart.
        Assert.Equal(10 / .7 - 10, chilled.BusyUntil - plain.BusyUntil, 2);
        Assert.Equal(0, frozen.BusyUntil);
        Assert.True(frozen.Walk.ArrivesAt <= game.State.Clock.Elapsed.TotalSeconds);
        Assert.Equal(21, chilled.Walk.Speed, 6);
        Assert.Equal(30, plain.Walk.Speed, 6);
        bool Sent(byte[] packet) => bytes.AsSpan().IndexOf(packet) >= 0;
        bool Buff(uint id, MonsterElementalAilments.Kind kind, int magnitude) =>
            Sent(Program.AilmentBuff(id, MonsterElementalAilments.Instance(kind), kind, magnitude));
        Assert.True(Buff(0x101, MonsterElementalAilments.Kind.Chill, 30));
        // Freeze has no value, so it carries its duration (flag 0x02, u68): the header up to the flags byte.
        var freeze = Program.AilmentBuff(0x103, MonsterElementalAilments.Instance(MonsterElementalAilments.Kind.Freeze),
            MonsterElementalAilments.Kind.Freeze, 100, seconds: 3);
        Assert.Equal(0x02, freeze[22]);
        Assert.True(Sent(freeze[..23]));
        uint speed = Program.Stats.Key("virtual_action_speed_+%")!.Value;
        Assert.True(Sent(InstanceEntities.StatsUpdate(0x101, [(speed, -30)])));
        Assert.False(Sent(InstanceEntities.StatsUpdate(0x102, [(speed, -30)])));

        // The rows' layout the buff relies on: chilled and shocked carry their one value, all three the +0xff u16.
        using var rows = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes("data/tables/BuffDefinitions.json"));
        foreach (var (row, id, values) in new[] { (1, "chilled", 1), (2, "frozen", 0), (3, "shocked", 1) })
        {
            var r = rows.RootElement[row];
            Assert.Equal(id, r.GetProperty("Id").GetString());
            Assert.True(r.GetProperty("__col44").GetBoolean());
            Assert.Equal(values, r.GetProperty("IsRecovery").GetBoolean() ? r.GetProperty("GrantedStats").GetArrayLength() : 0);
        }
    }

    // Stormy Seas (60505): with Baited Breath's rod every hit crits, its lightning becomes cold, and only the node's
    // cold_damage_can_shock lets that cold shock (the magnitude, not the chance); a plain equivalent is the same.
    [Fact]
    public async Task Surfcaster_Stormy_Seas_cold_crit_shocks_only_with_the_node()
    {
        if (!DataAvailable()) return;
        using var game = Surfcaster();
        using var plain = Surfcaster();
        using var flagged = Surfcaster();
        Assert.Null((await game.Change([48926, 64012, 33031, 37082, 41643, 60505], true)).Error);
        (string, int)[] hit = [("physical_damage_+%", -100), ("attack_minimum_added_lightning_damage", 2000),
            ("attack_maximum_added_lightning_damage", 2000), ("global_always_hit", 1), ("attack_speed_+%", 300)];
        (string, int)[] equivalent = [.. hit, ("always_crit_while_holding_fishing_rod", 1),
            ("base_lightning_damage_%_to_convert_to_cold", 100)];
        SetDev(game.State, hit);
        SetDev(plain.State, equivalent);
        SetDev(flagged.State, [.. equivalent, ("cold_damage_can_shock", 1)]);
        var results = new List<(uint Life, int Chill, bool Frozen, int Shock)>();
        foreach (var character in new[] { game, plain, flagged })
        {
            character.Items.Add(PlayerInventories.Weapon1, 0, 0, ItemCatalog.Create(Program.Items.Find("FishingRod1")!));
            SetDev(character.State);
            var target = SurfcasterTarget(character.State, 0x201, 3);
            await character.SurfcasterAttack(0x201);
            double now = character.State.Clock.Elapsed.TotalSeconds;
            results.Add((target.Life, (int)target.Ailments.Magnitude(MonsterElementalAilments.Kind.Chill, now),
                target.Ailments.Magnitude(MonsterElementalAilments.Kind.Freeze, now) > 0,
                (int)target.Ailments.Magnitude(MonsterElementalAilments.Kind.Shock, now)));
        }
        // One critical cold hit: the same damage, chill and freeze everywhere; the shock needs the flag.
        Assert.True(results[0].Life < 10_000);
        Assert.True(results[0] is { Chill: > 0, Frozen: true, Shock: > 0 }, $"{results[0]}");
        Assert.Equal((results[0].Life, results[0].Chill, results[0].Frozen, 0), results[1]);
        Assert.Equal(results[0], results[2]);
    }

    // Mirrored puts the ailments a hit landed on the player too; Unstoppable keeps its own action speed at base.
    [Fact]
    public void Surfcaster_Mirrored_reflects_and_Unstoppable_ignores_monster_ailments()
    {
        if (!DataAvailable()) return;
        using var game = Surfcaster();
        SpawnedMonster With(params (string, int)[] stats) => new("modded", new(0, 0, 30), 0, 2, 1000)
            { Mods = new([], [], new(stats.ToDictionary(s => s.Item1, s => s.Item2))) };
        var mirrored = With(("reflect_non_damaging_ailments_%_chance", 100));
        var unstoppable = With(("action_speed_cannot_be_reduced_below_base", 1), ("movement_speed_cannot_be_reduced_below_base", 1));
        var cold = DamagePacket.Of(DamageType.Cold, 200, 200);   // 20% of the threshold: a 26% chill, 1.2 s frozen
        double now = game.State.Clock.Elapsed.TotalSeconds;
        var player = Program.CharacterPlayerStats(game.State);
        var ailments = game.State.ElementalAilments;
        lock (game.State.Monsters)
        {
            Program.AilmentsOnHitLocked(game.State, unstoppable, cold, false, true, 100, player, null, now, new Random(1));
            Assert.Equal(0, ailments.Magnitude(PlayerElementalAilments.Kind.Chill, now));
            Assert.True(unstoppable.Ailments.Magnitude(MonsterElementalAilments.Kind.Freeze, now) > 0);
            Assert.Equal(1, Program.MonsterActionFactor(unstoppable, now));
            Program.AilmentsOnHitLocked(game.State, mirrored, cold, false, true, 100, player, null, now, new Random(1));
            Assert.Equal(0, Program.MonsterActionFactor(mirrored, now));
            Assert.Equal(26, ailments.Magnitude(PlayerElementalAilments.Kind.Chill, now));
            Assert.True(ailments.Magnitude(PlayerElementalAilments.Kind.Freeze, now + 1.1) > 0);
            Assert.Equal(0, ailments.Magnitude(PlayerElementalAilments.Kind.Freeze, now + 1.3));
        }
    }

    sealed partial class GatewayCharacter
    {
        // One swing of the monster at the player through the actual MonsterSwing, evasion's entropy set so it lands.
        // during runs after the swing's start check and before its contact (which waits on a timer).
        public Task<(string? Error, byte[] Bytes)> SurfcasterSwing(uint id, Action? during = null) => Exchange(async (stream, cipher) =>
        {
            double now = State.Clock.Elapsed.TotalSeconds;
            State.EvasionEntropy = 99; State.LastEvasionAt = now;
            var m = State.Monsters[id];
            var swing = (Task)GatewayMethod("MonsterSwing").Invoke(null, ["surfcaster-swing", stream, cipher, State, id, m,
                m.ActionGeneration])!;
            during?.Invoke();
            await swing;
            return null;
        });
    }

    static readonly MonsterAttack SurfcasterSwingAttack = new(SkillCatalog.FirstSkillId, .002, 20, DamagePacket.Of(DamageType.Physical, 100, 100));

    // A hit of the character on a living monster through the actual AilmentsOnHitLocked (threshold: its life).
    static void SurfcasterHit(ClientState state, SpawnedMonster m, DamageType type, int damage, bool critical)
    {
        lock (state.Monsters)
            Program.AilmentsOnHitLocked(state, m, DamagePacket.Of(type, damage, damage), false, critical, 100,
                Program.CharacterPlayerStats(state), null, state.Clock.Elapsed.TotalSeconds, new Random(1));
    }

    // Glacial Wave (6252): a monster chilled by the character's hit takes increased damage by the chill and deals less by
    // half of it; a chill from a character without the node does neither.
    [Fact]
    public async Task Surfcaster_Glacial_Wave_chill_raises_damage_taken_and_lessens_monster_damage()
    {
        if (!DataAvailable()) return;
        using var game = Surfcaster();
        using var plain = Surfcaster();
        Assert.Null((await game.Change([41643, 60505, 53922, 6252], true)).Error);
        var results = new List<(int Taken, int Burn, uint Lost)>();
        var ignites = new List<double>();
        foreach (var character in new[] { game, plain })
        {
            var m = SurfcasterTarget(character.State, 0x301, 3, life: 1000, attack: SurfcasterSwingAttack);
            // A fire monster that always crits, so its swing ignites the character from its (lessened) hit.
            var burning = SurfcasterTarget(character.State, 0x302, -3, life: 1000,
                attack: SurfcasterSwingAttack with { Damage = DamagePacket.Of(DamageType.Fire, 100, 100) },
                mods: new([], [], new(new Dictionary<string, int> { ["always_crit"] = 1 })));
            SurfcasterHit(character.State, burning, DamageType.Cold, 1000, false);
            SurfcasterHit(character.State, m, DamageType.Cold, 1000, false);   // a 30% chill
            double now = character.State.Clock.Elapsed.TotalSeconds;
            Assert.Equal(30.0, m.Ailments.Magnitude(MonsterElementalAilments.Kind.Chill, now));
            var hit = DamagePacket.Of(DamageType.Lightning, 100, 100);
            int taken = Program.MonsterHitDamage(m, hit, false, true, now, default).Damage;
            int burn = Program.MonsterBurn(m, new DamageOverTime(DamageType.Fire, 100, 10), 1, now).Damage;
            uint before = character.State.PlayerLife;
            await character.SurfcasterSwing(0x301);
            results.Add((taken, burn, before - character.State.PlayerLife));
            await character.SurfcasterSwing(0x302);
            ignites.Add(character.State.ElementalAilments.Magnitude(PlayerElementalAilments.Kind.Ignite,
                character.State.Clock.Elapsed.TotalSeconds));
        }
        // Plain equivalent of the damage taken: a monster with 30% increased damage taken from its mods.
        var modded = new SpawnedMonster("modded", new(0, 0, 30), 0, 2, 1000)
            { Mods = new([], [], new(new Dictionary<string, int> { ["base_damage_taken_+%"] = 30 })) };
        Assert.Equal(130, Program.MonsterHitDamage(modded, DamagePacket.Of(DamageType.Lightning, 100, 100), false, true, 0, default).Damage);
        Assert.Equal([(130, 130, 85u), (100, 100, 100u)], results);
        // Its ignite: 100 × 0.9 × 1.3 from the plain chill, 85 of 100 with the node's.
        Assert.Equal((117.0, .85), (Math.Round(ignites[1], 6), Math.Round(ignites[0] / ignites[1], 6)));
    }

    // MonsterSwing reads the monster's ailments: a frozen monster does not start a swing, a chilled one reaches its
    // contact later by its action speed, and a freeze that lands before the contact cancels the hit.
    [Fact]
    public async Task Surfcaster_monster_swing_waits_for_chill_and_stops_for_freeze()
    {
        if (!DataAvailable()) return;
        using var game = Surfcaster();
        var state = game.State;
        uint life = state.PlayerLife;
        var frozen = SurfcasterTarget(state, 0x901, 3, life: 1000, attack: SurfcasterSwingAttack);
        SurfcasterHit(state, frozen, DamageType.Cold, 1000, true);   // a critical full-life cold hit: frozen for 3 s
        var (_, bytes) = await game.SurfcasterSwing(0x901);
        Assert.Equal((0, life), (bytes.Length, state.PlayerLife));   // no 0x1ab and no hit
        // A 30% chill makes the half-swing to the contact 0.3 / 0.7 s instead of 0.3 s.
        var chilled = SurfcasterTarget(state, 0x902, -3, life: 1000, attack: SurfcasterSwingAttack with { Seconds = .6 });
        SurfcasterHit(state, chilled, DamageType.Cold, 1000, false);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        (_, bytes) = await game.SurfcasterSwing(0x902, () =>
        {
            lock (state.Monsters) chilled.Ailments.Freeze(1, state.Clock.Elapsed.TotalSeconds, _ => 0, _ => 0);
        });
        Assert.True(watch.Elapsed.TotalSeconds >= .4, $"{watch.Elapsed}");
        Assert.True(bytes.Length > 0);   // it started the swing
        Assert.Equal(life, state.PlayerLife);
    }

    // Brine Bound (52264): a monster that hits the character may be frozen for 1 s; every freeze the character inflicts
    // lasts at least 1 s once past the 0.3 s threshold; the character cannot be frozen.
    [Fact]
    public async Task Surfcaster_Brine_Bound_freezes_attackers_holds_the_minimum_and_cannot_be_frozen()
    {
        if (!DataAvailable()) return;
        using var game = Surfcaster();
        using var plain = Surfcaster();
        using var equivalent = Surfcaster();
        Assert.Null((await game.Change([41643, 60505, 56184, 52264], true)).Error);
        SetDev(equivalent.State, ("chance_to_freeze_enemies_for_1_second_when_hit_%", 100), ("freeze_minimum_duration_X_ms", 1000),
            ("base_cannot_be_frozen", 1));
        // The node's 50% chance; a /set_stat value replaces it, so 100 forces the roll.
        Assert.Equal(50, SurfcasterStat(game.State, "chance_to_freeze_enemies_for_1_second_when_hit_%"));
        SetDev(game.State, ("chance_to_freeze_enemies_for_1_second_when_hit_%", 100));
        var frozen = new List<(bool At, bool Later, bool Hit04, bool Hit02, bool Self)>();
        foreach (var character in new[] { game, plain, equivalent })
        {
            var state = character.State;
            var attacker = SurfcasterTarget(state, 0x401, 3, attack: SurfcasterSwingAttack);
            await character.SurfcasterSwing(0x401);
            double now = state.Clock.Elapsed.TotalSeconds;
            bool Frozen(SpawnedMonster m, double at) => m.Ailments.Magnitude(MonsterElementalAilments.Kind.Freeze, at) > 0;
            // Critical cold hits of 6.7% and 3.3% of the threshold: 0.402 s and 0.198 s of freeze.
            var longer = SurfcasterTarget(state, 0x402, -3, life: 1000);
            var shorter = SurfcasterTarget(state, 0x403, -3, life: 1000);
            SurfcasterHit(state, longer, DamageType.Cold, 67, true);
            SurfcasterHit(state, shorter, DamageType.Cold, 33, true);
            var cold = DamagePacket.Of(DamageType.Cold, 1000, 1000);
            lock (state.Monsters)
                Program.ApplyPlayerAilmentsLocked(state, cold, cold, true, false, now, Program.CharacterPlayerStats(state));
            frozen.Add((Frozen(attacker, now + .9), Frozen(attacker, now + 1), Frozen(longer, now + .9), Frozen(shorter, now),
                state.ElementalAilments.Magnitude(PlayerElementalAilments.Kind.Freeze, now) > 0));
        }
        Assert.Equal([(true, false, true, false, false), (false, false, false, false, true), (true, false, true, false, false)], frozen);
        // Without the minimum the 6.7% freeze ends at 0.402 s.
        var none = plain.State.Monsters[0x402];
        Assert.True(none.Ailments.Magnitude(MonsterElementalAilments.Kind.Freeze, plain.State.Clock.Elapsed.TotalSeconds + .3) > 0);
    }

    // A kill of the character through the actual AilmentsOnHitLocked: a physical hit took its last life, so it brings no
    // shock of its own.
    static void SurfcasterKill(ClientState state, SpawnedMonster m)
    {
        lock (state.Monsters)
        {
            (m.Life, m.Dead) = (0, true);
            Program.AilmentsOnHitLocked(state, m, DamagePacket.Of(DamageType.Physical, 1, 1), false, false, 100,
                Program.CharacterPlayerStats(state), null, state.Clock.Elapsed.TotalSeconds, new Random(1));
        }
    }

    // Within the Tempest (61246): a monster the character kills is shocked (15%, 2 s), and every shock the character
    // inflicts, the corpse's too, spreads to living enemies within 15 cells, grown by area of effect; a spread shock does
    // not spread again. The other kill paths are in the next test.
    [Fact]
    public async Task Surfcaster_Within_the_Tempest_shocks_kills_and_spreads_shocks_15_cells()
    {
        if (!DataAvailable()) return;
        int Spread(Func<string, int> get) => MonsterElementalAilments.ShockSpreadRadius(get);
        Assert.Equal((0, 15, 20), (Spread(_ => 0), Spread(n => n == "shock_proliferation_radius_is_at_least_15" ? 1 : 0),
            Spread(n => n is "shock_proliferation_radius_is_at_least_15" ? 1 : n == "gloves_shock_proliferation_radius" ? 20 : 0)));
        // Area of effect grows it like a skill's area: 100% increased × 1.41, 50% increased × 1.22, half the area × 0.70.
        static int Flag(string n) => n == "shock_proliferation_radius_is_at_least_15" ? 1 : 0;
        Assert.Equal((15, 21, 18, 10), (MonsterElementalAilments.SpreadRadius(Flag, 1), MonsterElementalAilments.SpreadRadius(Flag, 2),
            MonsterElementalAilments.SpreadRadius(Flag, 1.5), MonsterElementalAilments.SpreadRadius(Flag, .5)));
        using var game = Surfcaster();
        using var plain = Surfcaster();
        using var equivalent = Surfcaster();
        Assert.Null((await game.Change([41643, 60505, 34767, 61246], true)).Error);
        SetDev(equivalent.State, ("enemy_shock_on_kill", 1), ("shock_proliferation_radius_is_at_least_15", 1));
        const MonsterElementalAilments.Kind Shock = MonsterElementalAilments.Kind.Shock;
        var results = new List<int[]>();
        foreach (var character in new[] { game, plain, equivalent })
        {
            var state = character.State;
            SetDev(state, ("global_always_hit", 1));
            // Killed by the default attack (Attack), whose unarmed physical hit brings no shock of its own.
            var attacked = SurfcasterTarget(state, 0x501, 3, life: 1);
            await character.SurfcasterAttack(0x501);
            Assert.True(attacked.Dead);
            // Around the corpse at +3: 14 cells (+17) and 16 cells (-13); +31 is 14 from the spread copy at +17.
            var near = SurfcasterTarget(state, 0x503, 17);
            var far = SurfcasterTarget(state, 0x504, -13);
            var second = SurfcasterTarget(state, 0x505, 31);
            // A living monster's shock from a hit (a critical 10.2% lightning hit: 20%) spreads 10 cells, to -70.
            var hit = SurfcasterTarget(state, 0x506, -60, life: 1000);
            var neighbour = SurfcasterTarget(state, 0x507, -70);
            SurfcasterHit(state, hit, DamageType.Lightning, 102, true);
            double now = state.Clock.Elapsed.TotalSeconds;
            List<(byte[] Packet, string Log)> packets;
            lock (state.Monsters) packets = Program.MonsterAilmentChangesLocked(state, now);
            int Of(SpawnedMonster m, double at) => (int)m.Ailments.Magnitude(Shock, at);
            results.Add([Of(attacked, now), Of(near, now), Of(far, now), Of(second, now), Of(hit, now),
                Of(neighbour, now), Program.MonsterHitDamage(near, DamagePacket.Of(DamageType.Lightning, 100, 100), false, true, now, default).Damage]);
            if (character == game)
            {
                Assert.Contains(packets, p => p.Log.Contains("monster 0x503") && p.Log.Contains("Shock 15%"));
                // The spread copy goes when the corpse's shock ends, 2 s after the kill.
                lock (state.Monsters) packets = Program.MonsterAilmentChangesLocked(state, now + 2.1);
                Assert.Equal(0, Of(near, now + 2.1));
                Assert.Contains(packets, p => p.Log.Contains("0x01c2 monster 0x503 Shock ends"));
            }
        }
        Assert.Equal([15, 15, 0, 0, 20, 20, 115], results[0]);
        Assert.Equal([0, 0, 0, 0, 20, 0, 100], results[1]);
        Assert.Equal(results[0], results[2]);

        // With 100% increased area of effect a kill's shock and a hit's reach 21 cells: 20 away gets it, 22 away does not.
        SetDev(game.State, ("base_skill_area_of_effect_+%", 100));
        Assert.Equal(100, SurfcasterStat(game.State, "skill_area_of_effect_+%"));
        var corpse = SurfcasterTarget(game.State, 0x508, 100, life: 1);
        var reached = SurfcasterTarget(game.State, 0x509, 120);
        var beyond = SurfcasterTarget(game.State, 0x50a, 78);
        var struck = SurfcasterTarget(game.State, 0x50b, 300, life: 1000);
        var touched = SurfcasterTarget(game.State, 0x50c, 320);
        SurfcasterKill(game.State, corpse);
        SurfcasterHit(game.State, struck, DamageType.Lightning, 102, true);
        lock (game.State.Monsters) Program.MonsterAilmentChangesLocked(game.State, game.State.Clock.Elapsed.TotalSeconds);
        double at = game.State.Clock.Elapsed.TotalSeconds;
        Assert.Equal((true, 15, 0, 20), (corpse.Dead, Now(reached), Now(beyond), Now(touched)));
        int Now(SpawnedMonster m) => (int)m.Ailments.Magnitude(Shock, at);

        // Each spreading shock reaches as far as its own radius: a corpse with a 10% shock from a hit with 50% increased
        // area (18 cells) and the kill's 15% (15 cells) gives 15% at 10 cells and the weaker, wider 10% at 17.
        var both = SurfcasterTarget(game.State, 0x50d, 500, life: 1000);
        var inner = SurfcasterTarget(game.State, 0x50e, 510);
        var band = SurfcasterTarget(game.State, 0x50f, 517);
        lock (game.State.Monsters)
        {
            (both.Life, both.Dead) = (0, true);
            both.Ailments.Inflict(0, 20, true, 1000, at, Flag, _ => 0, new Random(1), area: 1.5);
            both.Ailments.ShockOnKill(at, Flag, _ => 0);
            Program.MonsterAilmentChangesLocked(game.State, at);
        }
        Assert.Equal([(15.0, 15), (10.0, 18)], both.Ailments.Spreading(at).Select(e => (e.Magnitude, e.Spread)).OrderBy(e => e.Spread));
        Assert.Equal((15, 10), (Now(inner), Now(band)));
    }

    // The nine small nodes (48926, 53922, 41643, 34767, 33031, 36782, 37534, 56184, 42290): 4% cast speed and 5% chance to
    // cause monsters to flee each, against plain /set_stat equivalents; a fleeing monster walks away instead of swinging.
    [Fact]
    public async Task Surfcaster_small_nodes_cast_speed_and_monster_flee()
    {
        if (!DataAvailable()) return;
        using var game = Surfcaster();
        using var plain = Surfcaster();
        Assert.Null((await game.Change([36782, 41643, 42290, 48926], true)).Error);   // the four next to the start
        SetDev(plain.State, ("base_cast_speed_+%", 16), ("global_hit_causes_monster_flee_%", 20));
        foreach (var (stat, value) in new[] { ("cast_speed_+%", 16), ("global_hit_causes_monster_flee_%", 20),
            ("main_hand_hit_causes_monster_flee_%", 20) })
            Assert.Equal((value, value), (SurfcasterStat(game.State, stat), SurfcasterStat(plain.State, stat)));
        int Chance(ClientState state, bool spell) => MonsterFlee.Chance(spell, n => SurfcasterStat(state, n), _ => 0);
        Assert.Equal((20, 20), (Chance(game.State, false), Chance(game.State, true)));

        // One roll per hit, then the monster's chance not to flee: the rarity's own mods give magic 50, rare and unique 100.
        var variety = Program.Monsters.Find("Metadata/Monsters/Zombies/ZombieBite")!;
        MonsterModSet Mods(MonsterRarity rarity) => Program.MonsterModsFor(game.State, variety, 10, rarity, null, alone: true);
        int NotFlee(MonsterRarity rarity) => Mods(rarity).Effects["monster_chance_to_not_flee_%"];
        Assert.Equal((0, 50, 100, 100), (NotFlee(MonsterRarity.Normal), NotFlee(MonsterRarity.Magic),
            NotFlee(MonsterRarity.Rare), NotFlee(MonsterRarity.Unique)));
        var random = new Random(3);
        int Count(int chance, int notFlee) => Enumerable.Range(0, 2000).Count(_ => MonsterFlee.Rolls(chance, notFlee, random));
        Assert.Equal((2000, 0, 0), (Count(100, 0), Count(100, 100), Count(0, 0)));
        Assert.InRange(Count(20, 0), 330, 470);
        Assert.InRange(Count(100, 50), 900, 1100);
        // The point: 50 cells off within 60 degrees of straight away from the player, else 25, else none.
        static double Off((int X, int Y) p) => Math.Sqrt((p.X - 100) * (p.X - 100) + p.Y * p.Y);
        var point = MonsterFlee.Point((100, 0), (0, 0), (_, _) => true, random)!.Value;
        Assert.InRange(Off(point), 49, 51);
        Assert.True(point.X >= 124, $"{point}");
        Assert.InRange(Off(MonsterFlee.Point((100, 0), (0, 0), (x, y) => Math.Abs(x - 100) + Math.Abs(y) <= 36, random)!.Value), 24, 26);
        Assert.Null(MonsterFlee.Point((100, 0), (0, 0), (_, _) => false, random));

        // A forced roll through the actual hit: the normal monster in reach flees, the rare one does not, and without the
        // nodes nothing flees.
        SetDev(game.State, ("global_hit_causes_monster_flee_%", 100));
        var attack = new MonsterAttack(SkillCatalog.FirstSkillId, 10, 20, DamagePacket.Of(DamageType.Physical, 1, 1));
        var fleeing = SurfcasterTarget(game.State, 0x601, 3, attack: attack);
        var rare = SurfcasterTarget(game.State, 0x602, -3, attack: attack, mods: Mods(MonsterRarity.Rare));
        var unmoved = SurfcasterTarget(plain.State, 0x601, 3, attack: attack);
        SetDev(plain.State, ("global_hit_causes_monster_flee_%", 0));
        foreach (var (state, m) in new[] { (game.State, fleeing), (game.State, rare), (plain.State, unmoved) })
            SurfcasterHit(state, m, DamageType.Physical, 1, false);
        Assert.Equal((true, false, false), (fleeing.Fleeing, rare.Fleeing, unmoved.Fleeing));
        var to = fleeing.FleeTo!.Value;
        double now = game.State.Clock.Elapsed.TotalSeconds;
        var from = fleeing.Walk.Position(now);
        Assert.True(to.X > from.X, $"{to}");   // away from the player

        // The AI walks it to the point and it does not swing, though in reach; the rare one swings.
        var (_, bytes) = await game.SurfcasterAiUntil(() => fleeing.FleeTo is null && rare.BusyUntil > now);
        var walk = InstanceProtocol.ActionStarted(0x601, from.X, from.Y, to.X, to.Y, 0x2909, 0xffff, 0x0408);
        Assert.True(bytes.AsSpan().IndexOf(walk) >= 0);
        Assert.Equal((0.0, true, true, null), (fleeing.BusyUntil, rare.BusyUntil > now, fleeing.Fleeing, fleeing.FleeTo));
        // Its walk ends (here short of the point, back in reach): the flee is over and it swings again.
        now = game.State.Clock.Elapsed.TotalSeconds;
        fleeing.Walk.Teleport(from.X, from.Y, now);
        await game.SurfcasterAiUntil(() => !fleeing.Fleeing && fleeing.BusyUntil > now);
        Assert.False(fleeing.Fleeing);
        Assert.True(fleeing.BusyUntil > now);
    }

    sealed partial class GatewayCharacter
    {
        // One sweep of the socketed gem skill through the actual AreaSkillHit, at once, aimed at the character.
        public Task<(string? Error, byte[] Bytes)> SurfcasterSweep() => Exchange(async (stream, cipher) =>
        {
            var gem = ProgramValue<IEnumerable<(ushort Id, int Effect, string Name, int Level, int Quality, uint Gem)>>(
                "GemSkillEffects", State).Single();
            var at = State.Walk.Position(State.Clock.Elapsed.TotalSeconds);
            await (Task)GatewayMethod("AreaSkillHit").Invoke(null, ["surfcaster-sweep", stream, cipher, State, gem.Id, gem,
                at, at, true, (ushort)1, State.ActionCount, null, null, 1.0, 0u, null, null, 0u])!;
            return null;
        });

        // One second of Righteous Fire's burn around the character through the actual BurnAround.
        public Task<(string? Error, byte[] Bytes)> SurfcasterBurn(RighteousFireState rf) => Exchange(async (stream, cipher) =>
        {
            await (Task)GatewayMethod("BurnAround").Invoke(null, ["surfcaster-burn", stream, cipher, State, rf, 1.0])!;
            return null;
        });

        // The area entry's step once the player object is sent (PlayerEnteredArea), on another area's connection.
        public Task<(string? Error, byte[] Bytes)> SurfcasterEnter(ClientState state) => Exchange(async (stream, cipher) =>
        {
            await (Task)GatewayMethod("PlayerEnteredArea").Invoke(null, ["surfcaster-enter", stream, cipher, state])!;
            return null;
        });
    }

    // Every kill path keeps the killing hit's ailments on the corpse, then adds Within the Tempest's: a critical cold hit
    // (Baited Breath, Stormy Seas) shocks a 1-life unique by 50%, and its corpse spreads that, not the 15% of the kill.
    // The same hit freezes, so a non-unique monster it kills shatters and leaves nothing to spread. An area skill's
    // sweep inflicts on every monster it hits; a Mirrored monster's reflected freeze lands after the sweep, so the
    // monsters after it are hit and the kill before it still gives experience. A projectile's contact does the same,
    // and Righteous Fire's burn (damage over time never shatters) shocks its kill.
    [Fact]
    public async Task Surfcaster_sweep_projectile_and_burn_kills_keep_the_hit_shock_and_a_Mirrored_freeze_stops_no_sweep()
    {
        if (!DataAvailable()) return;
        using var game = Surfcaster();
        Assert.Null((await game.Change([48926, 64012, 33031, 37082, 41643, 60505, 34767, 61246], true)).Error);
        game.Items.Add(PlayerInventories.Weapon1, 0, 0, ItemCatalog.Create(Program.Items.Find("FishingRod1")!));
        game.MeetAttributeRequirements();
        EquipFamilyGem(game, ItemCatalog.Create(Program.Items.Find("Metadata/Items/Gems/SkillGemIceNova")!));
        var state = game.State;
        SetDev(state, ("spell_minimum_added_cold_damage", 100), ("spell_maximum_added_cold_damage", 100));
        const MonsterElementalAilments.Kind Chill = MonsterElementalAilments.Kind.Chill, Freeze = MonsterElementalAilments.Kind.Freeze,
            Shock = MonsterElementalAilments.Kind.Shock;
        // On the hits' own clock: the projectile runtime's while it runs.
        double Of(SpawnedMonster m, MonsterElementalAilments.Kind kind) => m.Ailments.Magnitude(kind, Program.ProjectileWorldTime(state));

        var (px, py) = state.Walk.Position(state.Clock.Elapsed.TotalSeconds);
        var shattered = new SpawnedMonster("shattered", new(px + 4, py, 30), 100, 2, 1)
            { Variety = Program.Monsters.Find("Metadata/Monsters/Zombies/ZombieBite"), Level = 60, Aggro = true };
        var killed = new SpawnedMonster("unique", new(px + 5, py, 30), 100, 2, 1) { Rarity = MonsterRarity.Unique, Aggro = true };
        lock (state.Monsters) (state.Monsters[0x700], state.Monsters[0x701]) = (shattered, killed);
        var mirrored = SurfcasterTarget(state, 0x702, 6, life: 1000,
            mods: new([], [], new(new Dictionary<string, int> { ["reflect_non_damaging_ailments_%_chance"] = 100 })));
        var third = SurfcasterTarget(state, 0x703, 7, life: 1000);
        uint experience = state.Experience;
        await game.SurfcasterSweep();
        Assert.True(shattered.Dead && killed.Dead);
        Assert.True(state.Experience > experience);   // MonsterKilled ran for the kill before the Mirrored monster
        Assert.Equal((0.0, 0.0, true), (Of(shattered, Freeze), Of(shattered, Shock),
            !shattered.Ailments.Spreading(Program.ProjectileWorldTime(state)).Any()));
        Assert.True(third.Life < 1000 && Of(third, Chill) > 0 && Of(third, Freeze) > 0, $"{third.Life}");
        Assert.True(Of(mirrored, Freeze) > 0);
        Assert.True(state.ElementalAilments.Magnitude(PlayerElementalAilments.Kind.Freeze, state.Clock.Elapsed.TotalSeconds) > 0);
        Assert.Equal((50.0, 50.0), (Of(killed, Shock), killed.Ailments.Spreading(state.Clock.Elapsed.TotalSeconds).Max(e => e.Magnitude)));
        var neighbour = SurfcasterTarget(state, 0x704, 12);
        lock (state.Monsters) Program.MonsterAilmentChangesLocked(state, state.Clock.Elapsed.TotalSeconds);
        Assert.Equal(50, Of(neighbour, Shock));

        // A projectile pierces a 1-life unique and hits the next: the corpse keeps the hit's 50% shock, the other is chilled.
        var pierced = new SpawnedMonster("pierced", new(px - 20, py, 30), 100, 2, 1) { Rarity = MonsterRarity.Unique, Aggro = true };
        lock (state.Monsters) state.Monsters[0x705] = pierced;
        var struck = SurfcasterTarget(state, 0x706, -30);
        await using (var runtime = new ProjectileRuntimeFixture(state))
        {
            runtime.Release(runtime.Shot(new("Metadata/Projectiles/DefaultArrow")
                { Speed = 100, Range = 100, Radius = 1, Lifetime = GameServer.Core.Projectiles.ProjectileRuntime.Time(20), Pierce = 1 },
                new(DamagePacket.Of(DamageType.Cold, 100, 100), true, int.MaxValue, 0, 150, "surfcaster"),
                new(px, py), new(px - 100, py)));
            runtime.Advance(1);
            Assert.True(pierced.Dead);
            Assert.Equal(50, Of(pierced, Shock));
            Assert.True(Of(struck, Chill) > 0);

            // Righteous Fire burns a 1-life monster at the character's feet to death: the kill's 15% shock.
            var burned = SurfcasterTarget(state, 0x707, 0, life: 1);
            await game.SurfcasterBurn(new RighteousFireState(1, 2, null, SkillCatalog.FirstSkillId, 6000, 0, 1));
            Assert.True(burned.Dead);
            Assert.Equal(15, Of(burned, Shock));
        }
    }

    // The four player damage sites pass their hit's time, so the target's shock and Glacial Wave's chill count on the
    // default attack, an area sweep, a projectile's contact and Righteous Fire's burn: the affected monster loses what a
    // twin with the same increased damage taken from its mods loses at each one.
    [Fact]
    public async Task Surfcaster_hit_sites_count_the_target_shock_and_Glacial_Wave_chill()
    {
        if (!DataAvailable()) return;
        using var game = Surfcaster();
        var state = game.State;
        game.MeetAttributeRequirements();
        EquipFamilyGem(game, ItemCatalog.Create(Program.Items.Find("Metadata/Items/Gems/SkillGemIceNova")!));
        // Fixed lightning only, never a critical strike or a miss, so both monsters take the same roll.
        SetDev(state, ("global_cannot_crit", 1), ("global_always_hit", 1), ("physical_damage_+%", -100), ("cold_damage_+%", -100),
            ("attack_minimum_added_lightning_damage", 100), ("attack_maximum_added_lightning_damage", 100),
            ("spell_minimum_added_lightning_damage", 100), ("spell_maximum_added_lightning_damage", 100), ("attack_speed_+%", 300));
        var affected = SurfcasterTarget(state, 0x801, 3);
        var twin = SurfcasterTarget(state, 0x802, -3, mods: new([], [], new(new Dictionary<string, int> { ["base_damage_taken_+%"] = 50 })));
        // A 30% chill from a Glacial Wave hit and a 20% shock, from 0 for 200 s so they hold on both clocks.
        static int Wave(string n) => n switch
        {
            "chance_to_shock_%" => 100,
            "enemies_chilled_by_hits_take_damage_increased_by_chill_effect" => 1,
            "virtual_chill_duration_+%" or "virtual_shock_duration_+%" => 9900,
            _ => 0,
        };
        lock (state.Monsters) affected.Ailments.Inflict(1000, 102, false, 1000, 0, Wave, _ => 0, new Random(1));
        Assert.Equal((30.0, 20.0, 30.0), (affected.Ailments.Magnitude(MonsterElementalAilments.Kind.Chill, 1),
            affected.Ailments.Magnitude(MonsterElementalAilments.Kind.Shock, 1), affected.Ailments.ChillDamageTakenPct(1)));
        (uint A, uint T) lost = (0, 0);
        void Same(string site)
        {
            (uint A, uint T) now = (10_000 - affected.Life, 10_000 - twin.Life);
            Assert.True(now.A > lost.A && now.A - lost.A == now.T - lost.T, $"{site}: {now} after {lost}");
            lost = now;
        }

        await game.SurfcasterAttack(0x801);
        state.NextAttackAt = 0;
        await game.SurfcasterAttack(0x802);
        Same("attack");
        await game.SurfcasterSweep();
        Same("sweep");
        await using (var runtime = new ProjectileRuntimeFixture(state))
        {
            // One shot at each of them.
            var (px, py) = state.Walk.Position(state.Clock.Elapsed.TotalSeconds);
            foreach (int dx in new[] { 100, -100 })
                runtime.Release(runtime.Shot(new("Metadata/Projectiles/DefaultArrow")
                    { Speed = 100, Range = 100, Radius = 1, Lifetime = GameServer.Core.Projectiles.ProjectileRuntime.Time(20) },
                    new(DamagePacket.Of(DamageType.Lightning, 100, 100), true, int.MaxValue, 0, 150, "surfcaster"),
                    new(px, py), new(px + dx, py)));
            runtime.Advance(1);
            Same("projectile");
            await game.SurfcasterBurn(new RighteousFireState(1, 2, null, SkillCatalog.FirstSkillId, 6000, 0, 10));
            Same("burn");
        }
    }
}
