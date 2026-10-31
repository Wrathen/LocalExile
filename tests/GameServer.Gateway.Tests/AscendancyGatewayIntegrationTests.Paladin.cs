using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

public sealed partial class AscendancyGatewayIntegrationTests
{
    // Eight paid points each; the Paladin Aura options are free. With the Discipline swap they reach every node.
    internal static readonly ushort[][] PaladinBuilds =
    [
        [363, 13763, 29126, 40299, 2437, 52104, 43835, 46492],
        [38874, 44795, 58846, 2134, 40171, 49271, 34448, 54067, 42396],
        [54067, 42396, 11127, 34448, 49271, 40171, 38874, 44795, 28569],
    ];
    static readonly ushort[] PaladinSmallNodes = [2134, 49271, 363, 29126, 54067, 43835, 2437, 38874, 11127];

    static int PaladinStat(ClientState state, string name) =>
        Program.CharacterPlayerStats(state).GetValueOrDefault(Program.Stats.Key(name)!.Value);

    [Fact]
    public void Paladin_checklist_is_the_actual_graph_and_three_builds_cover_every_node()
    {
        if (!DataAvailable()) return;
        var definition = Program.Ascendancies.Find(4, AscendancyFamily.Phrecia, "Paladin")!;
        Assert.Equal("Champion", definition.Identity.Id);
        Assert.Equal((ushort)24984, definition.StartNode);
        Assert.Empty(definition.Nodes[24984].Stats);
        Assert.Equal(new ushort[] { 24984, 2134, 49271, 363, 29126, 54067, 43835, 13763, 40299, 46492, 52104, 42396, 40171,
            2437, 34448, 44795, 58846, 28569, 39868, 38874, 11127 }.Order(), definition.Nodes.Keys.Order());
        Assert.True(definition.Nodes[44795].IsChoice);
        Assert.All(new ushort[] { 58846, 28569, 39868 }, n => Assert.True(definition.Nodes[n].IsChoiceOption));
        Assert.Equal(definition.Nodes.Keys.Where(n => n != definition.StartNode).Order(),
            PaladinBuilds.SelectMany(b => b).Append((ushort)39868).Distinct().Order());
        Assert.All(PaladinBuilds, b => Assert.Equal(8, b.Sum(n => definition.Nodes[n].PointCost)));
        string Named(IReadOnlyList<(uint Key, int Value)> stats) =>
            string.Join(",", stats.Select(s => $"{Program.Stats.Name(s.Key)}={s.Value}").Order());
        Assert.All(PaladinSmallNodes, n => Assert.Equal("life_regeneration_rate_per_minute_%=30,non_curse_aura_effect_+%=5",
            Named(definition.Nodes[n].Stats)));
        Assert.Equal("consecrated_ground_effect_+%=50,consecrated_ground_while_stationary_radius=18," +
            "your_consecrated_ground_effect_lingers_for_ms_after_leaving_the_area=4000", Named(definition.Nodes[52104].Stats));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Paladin_legal_build_paid_full_refund_reallocation_and_reload(int index)
    {
        if (!DataAvailable()) return;
        using var game = new GatewayCharacter(choice: "Paladin", refundPoints: 100);
        var nodes = PaladinBuilds[index];
        Assert.Null((await game.Change(nodes, true)).Error);
        Assert.Equal(8, Program.AscendancyRules.SpentPoints(4, game.State.MainAscendancy));
        Assert.Equal(20, PaladinStat(game.State, "non_curse_aura_effect_+%")); // four small nodes in each build
        Assert.Equal(nodes.Order(), game.Reload().MainAscendancy.Allocated.Order());
        if (index == 1)
        {
            // Changing the Paladin Aura costs the option's refund; the parent stays and the new option is free.
            Assert.Null((await game.Change([58846], false, refundPoints: true)).Error);
            Assert.Equal((ushort)95, game.State.RefundPoints);
            Assert.Null((await game.Change([39868], true)).Error);
            nodes = [.. nodes.Where(n => n != 58846), 39868];
            Assert.Equal(nodes.Order(), game.Reload().MainAscendancy.Allocated.Order());
        }
        Assert.Null((await game.Change([.. nodes.Reverse()], false, refundPoints: true)).Error);
        Assert.Empty(game.State.MainAscendancy.Allocated);
        Assert.Equal((ushort)(index == 1 ? 55 : 60), game.State.RefundPoints); // the choice shell refunds for free
        Assert.Equal(0, PaladinStat(game.State, "non_curse_aura_effect_+%"));
        Assert.Null((await game.Change(nodes, true)).Error);
        Assert.Equal(nodes.Order(), game.Reload().MainAscendancy.Allocated.Order());
        Assert.Equal(game.State.RefundPoints, game.Reload().RefundPoints);
    }

    [Fact]
    public async Task Paladin_small_nodes_regenerate_like_the_plain_stat_and_raise_the_aura_effect()
    {
        if (!DataAvailable()) return;
        using var plain = new GatewayCharacter(choice: "Paladin", level: 70);
        using var game = new GatewayCharacter(choice: "Paladin", level: 70);
        double Regen(ClientState state) => ProgramValue<double>("LifeRegenPerSecond", state);
        double baseline = Regen(plain.State);
        int baseAura = plain.State.Pools.AuraEffectPct;
        await plain.Command("/set_stat life_regeneration_rate_per_minute_% 30");
        Assert.Null((await game.Change([2134], true)).Error);
        Assert.Equal(Regen(plain.State), Regen(game.State), 6);
        Assert.Equal(baseline + game.State.Pools.Life * .005, Regen(game.State), 6);
        Assert.Equal(baseAura + 5, game.State.Pools.AuraEffectPct);

        // The aura effect reaches a running aura's bonuses only: without one both characters have the same armour.
        Assert.Equal(plain.State.Pools.Armour, game.State.Pools.Armour);
        int row = Program.Skills.EffectRow("Determination");
        var determination = new AuraCatalog.Aura(0x103, row, "Determination", "Determination", 7, AuraCatalog.BuffRow("Determination"), 0, 0, Level: 20);
        var plainBonus = AuraCatalog.Bonus([determination], baseAura, Program.Skills, Program.Stats);
        var nodeBonus = AuraCatalog.Bonus([determination], game.State.Pools.AuraEffectPct, Program.Skills, Program.Stats);
        Assert.True(nodeBonus.Armour > plainBonus.Armour && nodeBonus.ArmourMorePct > plainBonus.ArmourMorePct);
        foreach (var character in new[] { plain, game })
        {
            character.State.Auras[determination.SkillId] = determination;
            character.State.Pools = Program.PoolsFor(character.State, character.State.Pools.Level);
        }
        Assert.True(game.State.Pools.Armour > plain.State.Pools.Armour);

        // The most a legal build holds: the six start neighbours (2134 from above), Absolution and 49271 behind it (seven
        // small nodes).
        Assert.Null((await game.Change([363, 2437, 38874, 43835, 54067, 40171, 49271], true)).Error);
        Assert.Equal(baseline + game.State.Pools.Life * .035, Regen(game.State), 6);
        Assert.Equal(baseAura + 35, game.State.Pools.AuraEffectPct);
    }

    [Fact]
    public async Task Anointed_Champion_consecrates_the_ground_while_stationary_with_more_effect_and_a_linger()
    {
        if (!DataAvailable()) return;
        using var game = new GatewayCharacter(choice: "Paladin", level: 70);
        Assert.Null((await game.Change([2437, 52104], true)).Error); // the stats send configures the stationary radius
        var state = game.State;
        int Regen() => PaladinStat(state, "life_regeneration_rate_per_minute_%");
        Assert.Equal(30, Regen());
        Assert.False(await game.GroundsTick(.5)); // standing since the start: ground made, on it
        Assert.Equal(30 + 450, Regen());
        // Walking (kept walking by new orders) takes the ground away; its effect lingers 4 s after the last covered tick.
        state.Walk.Order(120, 32, 1);
        Assert.False(await game.GroundsTick(1.1));
        Assert.False(state.FlaskGrounds.Covers(FlaskGrounds.Kind.ConsecratedGround, state.Walk.Position(1.1)));
        Assert.Equal(30 + 450, Regen());
        state.Walk.Order(10, 32, 2);
        state.Walk.Order(120, 32, 3);
        state.Walk.Order(10, 32, 4);
        Assert.False(await game.GroundsTick(4.45));
        Assert.Equal(30 + 450, Regen());
        Assert.True(state.Walk.ArrivesAt > 4.6);
        Assert.False(await game.GroundsTick(4.55));
        Assert.Equal(30, Regen());
        // Standing again: a new ground where the player stopped.
        double stop = state.Walk.ArrivesAt;
        Assert.False(await game.GroundsTick(stop + .1));
        Assert.Equal(30 + 450, Regen());
        // Refunded: the ground goes and the ticker stops once nothing is left.
        Assert.Null((await game.Change([52104], false, refundPoints: true)).Error);
        Assert.True(await game.GroundsTick(stop + .2));
        Assert.Equal(30, Regen());
        Assert.False(state.FlaskGrounds.Any);

        // In play the player tick starts the grounds ticker on its own (Program.RunPlayerTick); no flask needed.
        using var live = new GatewayCharacter(choice: "Paladin", level: 70);
        Assert.Null((await live.Change([2437, 52104], true)).Error);
        bool Consecrated() => live.State.FlaskGrounds.Covers(FlaskGrounds.Kind.ConsecratedGround,
            live.State.Walk.Position(live.State.Clock.Elapsed.TotalSeconds));
        await live.PlayerTicksUntil(Consecrated);
        Assert.True(Consecrated());

        // A plain Sulphur Flask ground without the node: the base 300, gone with the ground (no linger).
        using var plain = new GatewayCharacter(choice: "Paladin", level: 70);
        var at = plain.State.Walk.Position(1);
        plain.State.FlaskGrounds.Add(new(FlaskGrounds.Kind.ConsecratedGround, at.X, at.Y, 18, 2));
        Assert.False(await plain.GroundsTick(1));
        Assert.Equal(300, PaladinStat(plain.State, "life_regeneration_rate_per_minute_%"));
        Assert.True(await plain.GroundsTick(2.1));
        Assert.Equal(0, PaladinStat(plain.State, "life_regeneration_rate_per_minute_%"));
    }

    // Each Paladin Aura option grants its aura at the row's fixed level; it casts, reserves, restores and goes as a gem's.
    [Theory]
    [InlineData((ushort)58846, 17772, "Determination", 30)]
    [InlineData((ushort)28569, 17460, "Grace", 30)]
    [InlineData((ushort)39868, 17316, "Discipline", 40)]
    public async Task Paladin_Aura_option_grants_a_castable_aura_equal_to_a_plain_one(ushort option, int gepl, string effectId, int level)
    {
        if (!DataAvailable()) return;
        using var game = new GatewayCharacter(choice: "Paladin", level: 70, refundPoints: 100);
        using var plain = new GatewayCharacter(choice: "Paladin", level: 70);
        var before = game.State.Pools;
        IReadOnlyList<GrantedSkill> Skills(ClientState state) => ProgramValue<IReadOnlyList<GrantedSkill>>("PlayerSkills", state);
        Assert.Null((await game.Change([38874, 44795, option], true)).Error);
        Assert.Null((await plain.Change([38874], true)).Error); // the same 5% aura effect, no option
        var skill = Assert.Single(Skills(game.State), s => s.EffectsPerLevelRow == gepl);
        Assert.Empty(skill.Supports);
        Assert.DoesNotContain(Skills(plain.State), s => s.EffectsPerLevelRow == gepl);

        // Cast as the client casts an aura (0x24 on the player): on, at the row's level, with the row's reservation.
        await game.UseOnSelf(skill.Id);
        int effect = Program.Skills.EffectRow(effectId);
        var aura = Assert.Single(game.State.Auras.Values);
        Assert.Equal((skill.Id, effect, level, 0), (aura.SkillId, aura.Effect, aura.Level, aura.Quality));
        Assert.Equal(Program.Skills.Reservation(effect, level)!.Value.Percent, aura.ManaPercent);
        plain.State.Auras[0x103] = new(0x103, effect, effectId, effectId, 7, AuraCatalog.BuffRow(effectId), 0, aura.ManaPercent, level);
        plain.State.Pools = Program.PoolsFor(plain.State, 70);
        var (pools, reference) = (game.State.Pools, plain.State.Pools);
        Assert.Equal((reference.Armour, reference.Evasion, reference.EnergyShield), (pools.Armour, pools.Evasion, pools.EnergyShield));
        Assert.Equal(AuraCatalog.ReservedMana(reference, plain.State.Auras.Values), AuraCatalog.ReservedMana(pools, game.State.Auras.Values));
        switch (effectId)
        {
            case "Determination": Assert.True(pools.Armour > before.Armour); break;
            case "Grace": Assert.True(pools.Evasion > before.Evasion); break;
            default:
                // Discipline's 356 flat energy shield, with 38874's 5% aura effect (373.8, truncated as PoB's ScaleAddList
                // does), before the increases; 30% faster recharge (31.5, truncated too).
                Assert.Equal(0u, before.EnergyShield);
                Assert.Equal(EnergyShield.Max(373, pools.EnergyShieldPct), pools.EnergyShield);
                Assert.Equal(31, PaladinStat(game.State, "energy_shield_recharge_rate_+%"));
                Assert.Equal((int)Math.Round(pools.EnergyShield * 20 * 1.31),
                    PaladinStat(game.State, "energy_shield_recharge_rate_per_minute"));
                break;
        }

        // A new connection restores the saved toggle on the same skill id.
        var restored = Assert.Single(game.Reload().Auras.Values);
        Assert.Equal((skill.Id, level), (restored.SkillId, restored.Level));
        // Refunding the option takes the skill, the aura and its reservation away.
        Assert.Null((await game.Change([option], false, refundPoints: true)).Error);
        Assert.Empty(game.State.Auras);
        Assert.DoesNotContain(Skills(game.State), s => s.EffectsPerLevelRow == gepl);
        Assert.Equal((before.Armour, before.Evasion, before.EnergyShield),
            (game.State.Pools.Armour, game.State.Pools.Evasion, game.State.Pools.EnergyShield));
    }

    static SpawnedMonster PaladinTarget(int x) => new("PaladinTarget", new(x, 32, 30), 0, 2, 100_000) { Hostile = true };
    static int PaladinHit(SpawnedMonster m, DamageType type) => Program.MonsterDamage(m, DamagePacket.Of(type, 1000, 1000), new Random(1), default);
    static int PaladinBurn(SpawnedMonster m) => Program.MonsterBurn(m, new DamageOverTime(DamageType.Fire, 1000, 10), 1).Damage;
    static bool PaladinSent(byte[] bytes, byte[] packet) => bytes.AsSpan().IndexOf(packet) >= 0;

    // Light of Divinity's aura on monsters near the player and Bring the Battle's taunt increase, through the hit and
    // burn consumers; the player stands at (32, 32).
    [Fact]
    public async Task Light_of_Divinity_and_taunts_raise_the_damage_monsters_take()
    {
        if (!DataAvailable()) return;
        using var game = new GatewayCharacter(choice: "Paladin", level: 70, refundPoints: 100);
        var state = game.State;
        SpawnedMonster near = PaladinTarget(62), far = PaladinTarget(100); // 30 and 68 cells away
        (state.Monsters[0x41], state.Monsters[0x42]) = (near, far);
        Assert.Null((await game.Change([363, 13763], true)).Error);
        await game.DebuffsTick(1);
        ushort augury = near.PlayerAuras[2551];
        Assert.True(PaladinSent(game.LastBytes, InstanceEntities.AddBuff(0x41, augury, 2551, Program.PlayerId)));
        Assert.Empty(far.PlayerAuras);
        Assert.Equal((1250, 1250, 1000, 1250), (PaladinHit(near, DamageType.Fire), PaladinHit(near, DamageType.Lightning),
            PaladinHit(near, DamageType.Physical), PaladinBurn(near)));
        Assert.Equal(2250, Program.MonsterDamage(near, DamagePacket.Of(DamageType.Physical, 1000, 1000)
            .Add(DamageType.Cold, new DamageRange(1000, 1000)), new Random(1), default));
        Assert.Equal((1000, 1000), (PaladinHit(far, DamageType.Fire), PaladinBurn(far)));

        // A taunt alone changes no damage; with Bring the Battle every type taken rises 20%, added to the 25%.
        lock (state.Monsters) Assert.Equal(2, Program.TauntLocked(state, (32, 32), 100, 1, 3));
        Assert.Equal(1000, PaladinHit(far, DamageType.Fire));
        Assert.Null((await game.Change([43835, 46492], true)).Error);
        lock (state.Monsters) Program.TauntLocked(state, (32, 32), 100, 1, 3);
        Assert.Equal((1450, 1200, 1450), (PaladinHit(near, DamageType.Fire), PaladinHit(near, DamageType.Physical), PaladinBurn(near)));
        Assert.Equal((1200, 1200, 1200), (PaladinHit(far, DamageType.Cold), PaladinHit(far, DamageType.Chaos), PaladinBurn(far)));
        await game.DebuffsTick(1.1);
        Assert.True(PaladinSent(game.LastBytes, InstanceEntities.AddBuff(0x42, far.TauntBuff, 408, Program.PlayerId,
            values: [Program.PlayerId, 1], u68: BitConverter.SingleToUInt32Bits((float)(4 - 1.1)))));
        Assert.Equal(1450, PaladinHit(near, DamageType.Fire));

        // The taunt ends after its 3 s; a refunded Light of Divinity takes its aura off; a dead player has neither.
        await game.DebuffsTick(4.1);
        Assert.Equal((0, 1250, 1000), (far.TauntBuff, PaladinHit(near, DamageType.Fire), PaladinHit(far, DamageType.Fire)));
        state.PlayerLife = 0;
        await game.DebuffsTick(4.2);
        Assert.True(PaladinSent(game.LastBytes, InstanceEntities.RemoveBuff(0x41, augury)));
        Assert.Equal(1000, PaladinHit(near, DamageType.Fire));
        state.PlayerLife = state.Pools.Life;
        await game.DebuffsTick(4.3);
        Assert.Equal(1250, PaladinHit(near, DamageType.Fire));
        Assert.Null((await game.Change([13763], false, refundPoints: true)).Error);
        await game.DebuffsTick(4.4);
        Assert.Empty(near.PlayerAuras);
        Assert.Equal(1000, PaladinHit(near, DamageType.Fire));

        // A Basalt Flask taunts within its 30 cells through the same state, so the 20% applies to it too.
        await game.UseFlaskGrounds("Basalt Flask", new Dictionary<string, int> { ["local_flask_taunt_enemies_on_use_radius"] = 30 });
        Assert.Equal((1200, 1000), (PaladinHit(near, DamageType.Fire), PaladinHit(far, DamageType.Fire)));
    }

    // Every warcry taunts at its release; with Bring the Battle Enduring Cry has infinite power: one normal monster gives
    // the maximum endurance charges and the regeneration a plain cry has at its 25-power cap.
    [Fact]
    public async Task Bring_the_Battle_warcries_taunt_with_infinite_power()
    {
        if (!DataAvailable()) return;
        using var plain = new GatewayCharacter(choice: "Paladin", level: 70);
        using var game = new GatewayCharacter(choice: "Paladin", level: 70);
        Assert.Null((await game.Change([43835, 46492], true)).Error);
        Assert.Equal(100, PaladinStat(game.State, "warcries_have_infinite_power"));
        var charges = typeof(Program).GetMethod("ChargesOf", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var cries = new Dictionary<GatewayCharacter, (SpawnedMonster Near, SpawnedMonster Far)>();
        foreach (var character in new[] { plain, game })
        {
            var gem = ItemCatalog.Create(Program.Items.Find("Metadata/Items/Gems/SkillGemEnduringCry")!);
            var seismic = ItemCatalog.Create(Program.Items.Find("Metadata/Items/Gems/SkillGemSeismicCry")!);
            character.Items.Add(PlayerInventories.Weapon1, 0, 0, ItemCatalog.Create(Program.Items.Find("Driftwood Wand")!)
                with { Sockets = [new(SocketColour.White, gem), new(SocketColour.White, seismic)] });
            await character.Command("/set_stat warcry_speed_+% 1900"); // a 40 ms cry
            await character.Command("/set_stat additional_strength 100"); // the gems' requirement
            cries[character] = (PaladinTarget(72), PaladinTarget(100)); // 40 and 68 cells away
            (character.State.Monsters[0x41], character.State.Monsters[0x42]) = cries[character];
            await character.UseOnSelf(PaladinSkill(character.State, "EnduringCry"));
        }
        // Released: the plain cry's regeneration has begun, the infinite one's charge grant waits for the ticker.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var granted = (ChargeSession)charges.Invoke(null, [game.State])!;
        while (!granted.Pending || plain.State.EnduringCryRecovery.Rate(plain.State.Clock.Elapsed.TotalSeconds) == 0)
            await Task.Delay(10, timeout.Token);

        double now = game.State.Clock.Elapsed.TotalSeconds;
        Assert.All(cries.Values, c => Assert.True(c.Near.TauntedUntil > now && c.Far.TauntedUntil == double.NegativeInfinity));
        (int Held, int Maximum) Endurance(GatewayCharacter character)
        {
            var session = (ChargeSession)charges.Invoke(null, [character.State])!;
            PaladinStat(character.State, "warcry_speed_+%"); // working out the stats gives the session its limits
            session.Step(character.State.Clock.Elapsed.TotalSeconds, _ => 0, () => default, true, false, false, new ScriptedRandom());
            return (session.Effective(ChargeType.Endurance), session.Limits(ChargeType.Endurance).Maximum);
        }
        Assert.Equal(0, Endurance(plain).Held);
        Assert.Equal((3, 3), Endurance(game)); // the Duelist's three, from one normal monster
        Assert.Equal(25 * plain.State.EnduringCryRecovery.Rate(now), game.State.EnduringCryRecovery.Rate(now), 6);
        Assert.True(plain.State.EnduringCryRecovery.Rate(now) > 0);

        // The taunted buff, with its two values (BuffDefinitions 408 is IsRecovery), and the 20% only with the node.
        await game.DebuffsTick(now);
        var (near, far) = cries[game];
        Assert.True(PaladinSent(game.LastBytes, InstanceEntities.AddBuff(0x41, near.TauntBuff, 408, Program.PlayerId,
            values: [Program.PlayerId, 1], u68: BitConverter.SingleToUInt32Bits((float)(near.TauntedUntil - now)))));
        Assert.Equal((0, 1200, 1000), (far.TauntBuff, PaladinHit(near, DamageType.Physical), PaladinHit(far, DamageType.Physical)));
        Assert.Equal(1000, PaladinHit(cries[plain].Near, DamageType.Physical));

        // Another warcry (Seismic Cry, whose own buff and exerted slams are not modelled) taunts the same way.
        cries[plain].Near.TauntedUntil = double.NegativeInfinity;
        await plain.UseOnSelf(PaladinSkill(plain.State, "SeismicCry"));
        while (cries[plain].Near.TauntedUntil == double.NegativeInfinity) await Task.Delay(10, timeout.Token);
        Assert.Equal(double.NegativeInfinity, cries[plain].Far.TauntedUntil);
    }

    // Elemental Valiance: a kill or a hit on a rare rolls 25% for Summon Elemental Relic behind its cooldown; the relic is a
    // friendly minion whose aura reaches the player, refreshes on its element, explodes for its life at the end (×1.25
    // under Light of Divinity, its kills roll again as an ally's) and goes with the node or the player's death.
    [Fact]
    public async Task Elemental_Valiance_triggers_relics_that_aura_explode_and_end()
    {
        if (!DataAvailable()) return;
        using var game = new GatewayCharacter(choice: "Paladin", level: 70, refundPoints: 100);
        using var plain = new GatewayCharacter(choice: "Paladin", level: 70);
        var state = game.State;
        state.Clock.Stop(); // game time stands still: the cooldown below cannot run out on a slow machine
        SpawnedMonster rare = new("PaladinRare", new(60, 32, 30), 0, 2, 100_000) { Hostile = true, Rarity = MonsterRarity.Rare };
        SpawnedMonster normal = PaladinTarget(60);
        bool Trigger(SpawnedMonster m, bool hit, bool killed) { lock (state.Monsters) return Program.TriggerElementalRelicLocked(state, m, hit, killed); }
        state.Minions.Random = new ScriptedRandom(); // any roll fails the test
        Assert.False(Trigger(rare, true, true));
        Assert.Null((await game.Change([363, 13763, 29126, 40299], true)).Error);
        Assert.DoesNotContain(ProgramValue<IReadOnlyList<GrantedSkill>>("PlayerSkills", state), s => s.EffectsPerLevelRow == 78412);
        Assert.False(Trigger(normal, true, false)); // a hit on a normal monster does not roll
        state.Minions.Random = new ScriptedRandom(25, 24);
        Assert.True(Trigger(rare, true, true)); // the hit's 25 misses the 25%, the kill's 24 hits
        Assert.False(Trigger(rare, true, true)); // the 0.3 s cooldown: no roll
        Assert.Equal(1, state.Minions.PendingRelics);

        // The summon: a level-70 Fire Relic next to the player, a friendly minion with the minion life, its Anger on him.
        state.Minions.Random = new ScriptedRandom(0);
        double now = state.Clock.Elapsed.TotalSeconds;
        await game.MinionsTick(now);
        var relic = Assert.Single(state.Minions.All);
        var fire = Program.Monsters.Find("Metadata/Monsters/AnimatedItem/ElementalLivingRelicFire")!;
        Assert.Equal(("Fire", false, (byte)70, (uint)Program.Monsters.BaseLife(fire, 70)!),
            (relic.Kind, relic.Monster.Hostile, relic.Monster.Level, relic.Monster.MaxLife));
        Assert.Same(relic.Monster, state.Monsters[relic.Id]);
        Assert.Equal(now + 5, relic.Until, 1e-6);
        Assert.Equal(("Anger", 59), (relic.OwnerAura!.EffectId, relic.OwnerAura.Radius)); // 40 and gem level 20's 19
        ushort buff = relic.OwnerBuff;
        Assert.True(PaladinSent(game.LastBytes, InstanceEntities.AddBuff(Program.PlayerId, buff, 149, relic.Id)));
        int anger = Program.Skills.EffectRow("Anger");
        plain.State.Auras[0x103] = new(0x103, anger, "Anger", "Anger", 7, 149, 0, 0, Level: 20); // level 70 meets level 20's 70
        Assert.Equal(0, plain.State.Pools.AuraEffectPct); // the node's 10% aura effect does not scale the relic's aura
        foreach (string stat in new[] { "attack_minimum_added_fire_damage", "spell_maximum_added_fire_damage" })
            Assert.Equal(PaladinStat(plain.State, stat), PaladinStat(state, stat));
        Assert.True(PaladinStat(state, "attack_minimum_added_fire_damage") > 0);
        // The strongest Anger applies: the player's own, 10% stronger, replaces the relic's rather than adding to it.
        state.Auras[0x103] = plain.State.Auras[0x103];
        Assert.Equal(AuraCatalog.RawStats([state.Auras[0x103]], state.Pools.AuraEffectPct, Program.Skills, Program.Stats)
            .Single(s => s.Key == Program.Stats.Key("attack_minimum_added_fire_damage")).Value,
            PaladinStat(state, "attack_minimum_added_fire_damage"));
        state.Auras.Clear();
        // A level-85 relic's auras are halfway between the gem levels 27 (84) and 28 (86): GGG's numbers.
        int At(string effect, int level, string stat) => PlayerMinions.AuraStatsAt(Program.Skills, Program.Stats,
            Program.Skills.EffectRow(effect), effect, level).Single(s => s.Key == Program.Stats.Key(stat)).Value;
        Assert.Equal((182, 259, 43, 26, 415, 23), (At("Anger", 85, "attack_minimum_added_fire_damage"),
            At("Anger", 85, "spell_maximum_added_fire_damage"), At("Hatred", 85, "physical_damage_%_to_add_as_cold"),
            At("Wrath", 85, "attack_minimum_added_lightning_damage"), At("Wrath", 85, "attack_maximum_added_lightning_damage"),
            At("Wrath", 85, "wrath_aura_spell_lightning_damage_+%_final")));

        // Fire again refreshes the one Fire Relic instead of making a second.
        state.Minions.Random = new ScriptedRandom(0);
        state.Minions.PendingRelics = 1;
        await game.MinionsTick(now + 1);
        Assert.Same(relic, Assert.Single(state.Minions.All));
        Assert.Equal(now + 6, relic.Until, 1e-6);
        // Left beyond its 150-cell aggro range (MaxAgroRange), it teleports back next to the player (RelicTeleport); its
        // aura stays on.
        var beside = relic.Monster.Walk.Position(now);
        relic.Monster.Walk = state.NewMonsterWalk(relic.Id, 200, 32, relic.Monster.Walk.Speed, 1, false);
        await game.MinionsTick(now + 1);
        Assert.Equal(beside, relic.Monster.Walk.Position(now + 1));
        Assert.True(PaladinSent(game.LastBytes, InstanceProtocol.Teleport(relic.Id, beside.X, beside.Y)));
        Assert.Equal(buff, relic.OwnerBuff);

        // Its end: the explosion, its life as fire to the monsters within 22 cells of it; 25% more on one under Light of
        // Divinity (the debuff step gives it), and a dying one rolls the trigger as an ally's kill (cooldown long recovered).
        SpawnedMonster near = PaladinTarget(40), plainNear = PaladinTarget(38), far = PaladinTarget(100);
        SpawnedMonster dying = new("PaladinDying", new(36, 32, 30), 0, 2, 100_000) { Hostile = true, Life = 1 };
        (state.Monsters[0x41], state.Monsters[0x42]) = (near, far);
        await game.DebuffsTick(now + 1);
        (state.Monsters[0x43], state.Monsters[0x44]) = (plainNear, dying);
        state.SingleUseCooldowns.Clear(); state.SkillReadyAt.Clear();
        state.Minions.Random = new ScriptedRandom(24);
        await game.MinionsTick(now + 6.05);
        uint blast = relic.Monster.MaxLife;
        Assert.Equal((100_000 - blast * 5 / 4, 100_000 - blast, 100_000u, 0u), (near.Life, plainNear.Life, far.Life, dying.Life));
        Assert.True(PaladinSent(game.LastBytes, InstanceEntities.RemoveBuff(Program.PlayerId, buff)));
        Assert.Empty(state.Minions.All);
        Assert.True(relic.Monster.Dead);
        Assert.Equal(0, PaladinStat(state, "attack_minimum_added_fire_damage"));
        Assert.Equal(1, state.Minions.PendingRelics);

        // That relic (Cold), then the player's death: it dies without exploding. Then a refund: no more relics or triggers.
        state.Minions.Random = new ScriptedRandom(1);
        await game.MinionsTick(now + 7);
        var cold = Assert.Single(state.Minions.All);
        Assert.Equal("Cold", cold.Kind);
        state.PlayerLife = 0;
        await game.MinionsTick(now + 7.1);
        Assert.Empty(state.Minions.All);
        Assert.True(PaladinSent(game.LastBytes, InstanceProtocol.RemoveObject(relic.Id))); // the Fire Relic left no corpse
        Assert.False(state.Monsters.ContainsKey(relic.Id));
        Assert.True(state.Monsters[cold.Id].Dead);
        Assert.Equal(100_000 - blast * 5 / 4, near.Life);
        state.PlayerLife = state.Pools.Life;
        state.Minions.PendingRelics = 1;
        state.Minions.Random = new ScriptedRandom(2);
        await game.Command("/set_stat minion_maximum_life_+% 50"); // increased minion life raises its life and explosion
        await game.Command("/set_stat base_minion_duration_+% 20"); // and increased minion duration its 5 s
        await game.MinionsTick(now + 8);
        var lightning = Assert.Single(state.Minions.All);
        Assert.Equal("Lightning", lightning.Kind);
        Assert.Equal(blast * 3 / 2, lightning.Monster.MaxLife);
        Assert.Equal(now + 8 + 6, lightning.Until, 1e-6);
        Assert.Null((await game.Change([40299], false, refundPoints: true)).Error);
        state.Minions.Random = new ScriptedRandom();
        await game.MinionsTick(now + 8.1);
        Assert.Empty(state.Minions.All);
        state.SingleUseCooldowns.Clear(); state.SkillReadyAt.Clear();
        Assert.False(Trigger(rare, true, true));
    }

    // The relic trigger's real callers, a default-attack kill and a Righteous Fire burn kill; Ruthless, where the empty
    // BuffsHardmode means PassiveSkillBuffs and the HardMode skill caps the relic at level 80; and the ordinary Guardian's
    // Unwavering Crusade (19641), which has the same buff and grant.
    [Fact]
    public async Task Relic_trigger_runs_from_attack_and_burn_kills_in_Ruthless_and_for_the_Guardian()
    {
        if (!DataAvailable()) return;
        using var game = new GatewayCharacter(choice: "Paladin", level: 70);
        var state = game.State;
        Assert.Null((await game.Change([363, 13763, 29126, 40299], true)).Error);
        await game.Command("/set_stat global_always_hit 1");
        await game.Command("/set_stat attack_speed_+% 900");
        SpawnedMonster Dying(int x) => new("PaladinDying", new(x, 32, 30), 0, 2, 100_000) { Hostile = true, Life = 1 };
        var struck = state.Monsters[0x41] = Dying(34);
        state.Minions.Random = new ScriptedRandom(0); // the kill's roll; a second roll fails the test
        await game.Attack(0x41, struck);
        Assert.Equal((true, 1), (struck.Dead, state.Minions.PendingRelics));
        state.SingleUseCooldowns.Clear(); state.SkillReadyAt.Clear();
        var burnt = state.Monsters[0x42] = Dying(36);
        state.Minions.Random = new ScriptedRandom(0);
        await game.Burn(new RighteousFireState(1, 2, null, SkillCatalog.FirstSkillId, 6000, 0, 1000));
        Assert.Equal((true, 2), (burnt.Dead, state.Minions.PendingRelics));

        using var ruthless = new GatewayCharacter(choice: "Paladin", level: 90, ruthless: true);
        Assert.Null((await ruthless.Change([363, 13763, 29126, 40299], true)).Error);
        Assert.NotEmpty(PassiveBuffAuras.AllyAurasFor([40299], ruthless: true));
        ruthless.State.Clock.Stop();
        ruthless.State.Minions.Random = new ScriptedRandom(24, 0); // the kill rolls 24 of 25%; the relic is Fire
        lock (ruthless.State.Monsters) Assert.True(Program.TriggerElementalRelicLocked(ruthless.State, PaladinTarget(60), false, true));
        await ruthless.MinionsTick(ruthless.State.Clock.Elapsed.TotalSeconds);
        Assert.Equal((byte)80, Assert.Single(ruthless.State.Minions.All).Monster.Level);

        using var guardian = new GatewayCharacter(classRow: 6, baseClass: 5, choice: "Guardian", family: AscendancyFamily.Normal, level: 70);
        guardian.State.Minions.Random = new ScriptedRandom(0);
        Assert.Null((await guardian.Change([40010, 4494, 16745, 19641], true)).Error);
        lock (guardian.State.Monsters) Assert.True(Program.TriggerElementalRelicLocked(guardian.State, PaladinTarget(60), false, true));
        Assert.Equal(1, guardian.State.Minions.PendingRelics);
    }

    static ushort PaladinSkill(ClientState state, string effectId) =>
        ProgramValue<IEnumerable<(ushort Id, int Effect, string Name, int Level, int Quality, uint Gem)>>("GemSkillEffects", state)
            .Single(g => g.Effect == Program.Skills.EffectRow(effectId)).Id;

    // A Soul Link gem (level 1) in a wand, a 50 ms cast and the gem's intelligence.
    static async Task<ushort> PaladinSoulLink(GatewayCharacter character)
    {
        var gem = ItemCatalog.Create(Program.Items.Find("Metadata/Items/Gems/SkillGemSoulLink")!);
        character.Items.Add(PlayerInventories.Weapon1, 0, 0, ItemCatalog.Create(Program.Items.Find("Driftwood Wand")!)
            with { Sockets = [new(SocketColour.White, gem)] });
        await character.Command("/set_stat cast_speed_+% 900");
        await character.Command("/set_stat additional_intelligence 200");
        return PaladinSkill(character.State, "SoulLink");
    }

    // A relic of the player's (a friendly minion) at (x, 32), the player standing at (32, 32).
    static PlayerMinions.Minion PaladinRelic(ClientState state, uint id, int x, MonsterCatalog.Variety? variety = null)
    {
        SpawnedMonster m = new("PaladinRelic", new(x, 32, variety?.MovementSpeed ?? 30), 0, 2, 1000) { Variety = variety };
        var relic = new PlayerMinions.Minion(id, m, "SummonElementalRelic", "Fire") { Until = state.Clock.Elapsed.TotalSeconds + 5 };
        lock (state.Monsters) { state.Monsters[id] = m; state.Minions.Add(relic); }
        return relic;
    }

    // Deep Connections lets a link gem take a relic and raises its level by 5 (a longer link, a higher cost); while
    // linked, Absolution's 40% aura effect counts and number_of_active_links is 1. The cost is paid every step; out of
    // range for 4 s, or unpaid, the link breaks without killing the player.
    [Fact]
    public async Task Paladin_links_take_relics_with_Deep_Connections_and_Absolution_counts_while_linked()
    {
        if (!DataAvailable()) return;
        using var game = new GatewayCharacter(choice: "Paladin", level: 70, refundPoints: 100);
        var state = game.State;
        state.Clock.Stop();
        double now = state.Clock.Elapsed.TotalSeconds;
        ushort soul = await PaladinSoulLink(game);
        var relic = PaladinRelic(state, 0x50, 60); // 28 cells away
        Assert.False(await game.Link(soul, relic.Id)); // no link_skills_can_target_minions: allied players only
        Assert.Equal(0, state.Links.Count);

        Assert.Null((await game.Change([54067, 42396, 11127, 34448, 49271, 40171, 2134], true)).Error);
        int unlinked = state.Pools.AuraEffectPct;
        Assert.Equal(0, PaladinStat(state, "number_of_active_links"));
        Assert.True(await game.Link(soul, 0)); // nothing aimed: the nearest qualifying target
        var link = Assert.Single(state.Links.All);
        Assert.Equal((relic.Id, 1, unlinked + 40), (link.Target, PaladinStat(state, "number_of_active_links"), state.Pools.AuraEffectPct));
        uint seconds = BitConverter.SingleToUInt32Bits((float)(link.Until - now));
        Assert.True(PaladinSent(game.LastBytes, InstanceEntities.AddBuff(Program.PlayerId, link.SourceBuff, 1946, Program.PlayerId, soul, u68: seconds)));
        Assert.True(PaladinSent(game.LastBytes, InstanceEntities.AddBuff(relic.Id, link.TargetBuff, 1947, Program.PlayerId, soul, u68: seconds)));
        // Level 1 + 5: level 6's duration and cost, longer and dearer than the gem's own level.
        int row = Program.Skills.EffectRow("SoulLink");
        int At(int level, string stat) => Program.Skills.EffectStats(row, level).Single(s => s.Item1 == Program.Stats.Key(stat)).Item2;
        Assert.Equal(At(6, "base_skill_effect_duration") / 1000.0, link.Until - now, 1e-6);
        Assert.True(At(6, "base_skill_effect_duration") > At(1, "base_skill_effect_duration"));
        Assert.Equal(At(6, "mana_cost_per_minute"), (int)link.ManaPerMinute);
        Assert.True(At(6, "mana_cost_per_minute") > At(1, "mana_cost_per_minute"));

        // One second costs a second's mana: whole points paid, the fraction carried.
        uint mana = state.PlayerMana;
        await game.LinksTick(now + 1);
        Assert.Equal(link.ManaPerMinute / 60, mana - state.PlayerMana + state.Links.ManaCarry, 6);
        // Out of range: the link holds for 4 s, then breaks; the player lives and the aura effect goes back.
        relic.Monster.Walk = state.NewMonsterWalk(relic.Id, 200, 32, relic.Monster.Walk.Speed, 1, false);
        await game.LinksTick(now + 1.1);
        await game.LinksTick(now + 5);
        Assert.Single(state.Links.All);
        await game.LinksTick(now + 5.2);
        Assert.Equal((0, unlinked, 0), (state.Links.Count, state.Pools.AuraEffectPct, PaladinStat(state, "number_of_active_links")));
        Assert.True(PaladinSent(game.LastBytes, InstanceEntities.RemoveBuff(relic.Id, link.TargetBuff)));
        Assert.True(state.PlayerLife > 0);
        // Beyond the 80-cell range it cannot be linked; back in range it can, and a cost it cannot pay breaks the link.
        Assert.False(await game.Link(soul, relic.Id));
        relic.Monster.Walk = state.NewMonsterWalk(relic.Id, 60, 32, relic.Monster.Walk.Speed, 1, false);
        Assert.True(await game.Link(soul, relic.Id));
        state.PlayerMana = 0;
        await game.LinksTick(now + 6);
        Assert.Equal((0, unlinked), (state.Links.Count, state.Pools.AuraEffectPct));
        Assert.True(state.PlayerLife > 0);
        // Refunding Deep Connections ends a link to a minion without a death (our rule, as for Elemental Valiance).
        state.PlayerMana = mana;
        Assert.True(await game.Link(soul, relic.Id));
        var deep = state.Links.On(relic.Id)!;
        Assert.Null((await game.Change([34448], false, refundPoints: true)).Error);
        await game.LinksTick(now + 6.1);
        Assert.Equal((0, false), (state.Links.Count, relic.Monster.Dead));
        Assert.True(PaladinSent(game.LastBytes, InstanceEntities.RemoveBuff(relic.Id, deep.TargetBuff)));
        Assert.True(state.PlayerLife > 0);
    }

    // A link cast on a relic (C→S 0x24) links it at the end of the cast. Field Medicine: a magic utility flask has 40%
    // more effect on the player, as the plain stat gives. Its effect goes on the linked relic for its duration without
    // that 40% ("applied to you"): a magic Quicksilver's 40% and a Silver Flask's Onslaught (20%) make the relic walk 60%
    // faster, on the server and in the client's movement_velocity_+%, until they end. The relic's duration running out
    // while linked kills the player once, through the normal death path.
    [Fact]
    public async Task Paladin_linked_relic_ending_kills_the_player_and_Field_Medicine_flasks_reach_it()
    {
        if (!DataAvailable()) return;
        using var game = new GatewayCharacter(choice: "Paladin", level: 70, refundPoints: 100);
        using var plain = new GatewayCharacter(choice: "Paladin", level: 70);
        var state = game.State;
        // Field Medicine and Deep Connections, and Elemental Valiance so that relics stay for their duration.
        Assert.Null((await game.Change([54067, 42396, 11127, 34448, 363, 13763, 29126, 40299], true)).Error);
        var granite = Program.Items.UtilityFlaskOf(Program.Items.Find("Granite Flask")!)!;
        var flask = ItemCatalog.Create(Program.Items.Find("Granite Flask")!) with { Rarity = ItemRarity.Magic, Charges = 60 };
        IReadOnlyList<UtilityFlasks.Source> Effect(ClientState s) =>
            UtilityFlasks.For(granite, flask, new Dictionary<string, int>(), name => PaladinStat(s, name)).Stats;
        var unscaled = Effect(plain.State);
        await plain.Command("/set_stat magic_utility_flask_effect_+% 40");
        Assert.Equal(Effect(plain.State), Effect(state));
        Assert.NotEqual(unscaled, Effect(state));

        ushort soul = await PaladinSoulLink(game);
        var variety = Program.Monsters.Find("Metadata/Monsters/AnimatedItem/ElementalLivingRelicFire")!;
        var relic = PaladinRelic(state, 0x50, 60, variety);
        await game.UseOnAndWait(relic.Id, soul, () => state.Links.Count == 1); // a paused game casts nothing
        Assert.Equal(relic.Id, Assert.Single(state.Links.All).Target);
        state.Clock.Stop(); // from here on game time moves only with the steps below
        double now = state.Clock.Elapsed.TotalSeconds;

        var silver = Program.Items.UtilityFlaskOf(Program.Items.Find("Silver Flask")!)!;
        game.Items.Add(PlayerInventories.Flask, 0, 0, ItemCatalog.Create(Program.Items.Find("Quicksilver Flask")!) with { Rarity = ItemRarity.Magic, Charges = 60 });
        game.Items.Add(PlayerInventories.Flask, 1, 0, ItemCatalog.Create(Program.Items.Find("Silver Flask")!) with { Charges = 60 });
        await game.Dispatch(new LoginWriter().U16(InstanceItems.UseFlaskId).U32(0).ToArray());
        await game.Dispatch(new LoginWriter().U16(InstanceItems.UseFlaskId).U32(1).ToArray());
        Assert.Equal(2, state.Links.Flasks.Count);
        var buff = state.Links.Flasks[1];
        Assert.Equal(relic.Id, buff.Target);
        uint duration = BitConverter.SingleToUInt32Bits((float)(buff.Until - now));
        Assert.True(PaladinSent(game.LastBytes, InstanceEntities.AddBuff(relic.Id, buff.Buff, silver.BuffRow, Program.PlayerId, u68: duration)));
        Assert.True(PaladinSent(game.LastBytes, InstanceEntities.AddBuff(relic.Id, buff.Extra!.Value, silver.Extra!.BuffRow, Program.PlayerId, u68: duration)));
        Assert.True(PaladinSent(game.LastBytes, InstanceEntities.StatsUpdate(relic.Id, [(StatKeys.MovementVelocityPct, 60)])));
        Assert.Equal(variety.MovementSpeed * 1.6, relic.Monster.Walk.Speed, 6);
        await game.LinksTick(state.Links.Flasks.Max(f => f.Until));
        Assert.Empty(state.Links.Flasks);
        Assert.True(PaladinSent(game.LastBytes, InstanceEntities.RemoveBuff(relic.Id, buff.Extra.Value)));
        Assert.True(PaladinSent(game.LastBytes, InstanceEntities.StatsUpdate(relic.Id, [(StatKeys.MovementVelocityPct, 0)])));
        Assert.Equal(variety.MovementSpeed, relic.Monster.Walk.Speed, 6);

        // The relic's duration runs out while linked: it ends (the minion step), and the link step kills the player once.
        relic.Until = buff.Until;
        int epoch = state.IncomingHitEpoch;
        await game.MinionsTick(buff.Until + .1);
        Assert.True(relic.Monster.Dead);
        Assert.True(state.PlayerLife > 0);
        await game.LinksTick(buff.Until + .1);
        Assert.Equal((0u, 0, epoch + 1, epoch + 1), (state.PlayerLife, state.Links.Count, state.IncomingHitEpoch, state.DeathNotificationEpoch));
        await game.LinksTick(buff.Until + .2);
        Assert.Equal(epoch + 1, state.IncomingHitEpoch);

        // Refunding Elemental Valiance ends a linked relic and its link, and the player lives (our rule: no expiry).
        state.PlayerLife = state.Pools.Life;
        var second = PaladinRelic(state, 0x51, 60, variety);
        Assert.True(await game.Link(soul, second.Id));
        var secondLink = state.Links.On(second.Id)!;
        Assert.Null((await game.Change([40299], false, refundPoints: true)).Error);
        await game.MinionsTick(buff.Until + .3);
        Assert.Equal((true, 0), (second.Monster.Dead, state.Links.Count));
        Assert.True(PaladinSent(game.LastBytes, InstanceEntities.RemoveBuff(second.Id, secondLink.TargetBuff)));
        await game.LinksTick(buff.Until + .3);
        Assert.Equal((0, epoch + 1), (state.Links.Count, state.IncomingHitEpoch));
        Assert.True(state.PlayerLife > 0);
    }

    [Fact]
    public void Paladin_stays_behind_the_closed_gate_and_its_refusal_names_its_checklist()
    {
        if (Environment.GetEnvironmentVariable("Game_VERIFIED_PALADIN") != "1")
            Assert.Throws<AscendancyRuleException>(() => AscendancyClientCompatibility.RequirePlayable(
                new(AscendancyFamily.Phrecia, "Champion", "3.29.3b")));
        Assert.Contains("paladin.md", Assert.Throws<AscendancyRuleException>(
            () => VerifiedGamblerGraph.Verify([], [], "Paladin")).Message);
    }

    sealed partial class GatewayCharacter
    {
        // C→S 0x24 with the player as the target: how the client casts an aura or another self skill.
        public Task<(string? Error, byte[] Bytes)> UseOnSelf(ushort skill) =>
            Dispatch(new LoginWriter().U16(0x24).U32(Program.PlayerId).U16(skill).U16(1).U16(0).ToArray());

        // One flask-grounds step at a chosen game time (Program.FlaskGroundsTick): true when its ticker would stop.
        public async Task<bool> GroundsTick(double now)
        {
            bool stop = false;
            await Exchange(async (stream, cipher) =>
            {
                stop = await (Task<bool>)GatewayMethod("FlaskGroundsTick").Invoke(null, ["paladin-integration", stream, cipher, State, now])!;
                return null;
            });
            return stop;
        }

        // The real player tick loop (Program.RunPlayerTick) until done holds or 5 s pass; then the connection closes,
        // which stops it and any ticker it started.
        public Task PlayerTicksUntil(Func<bool> done) => Exchange(async (stream, cipher) =>
        {
            var tick = (Task)GatewayMethod("RunPlayerTick").Invoke(null, ["paladin-integration", stream, cipher, State])!;
            for (int i = 0; i < 50 && !done(); i++) await Task.Delay(100);
            State.Closed = true;
            await tick;
            return null;
        });

        // One step of the player's debuffs on monsters at a chosen game time (Program.EnemyDebuffsTick).
        public Task DebuffsTick(double now) => Exchange(async (stream, cipher) =>
        {
            await Program.EnemyDebuffsTick("paladin-integration", stream, cipher, State, now);
            return null;
        });

        // One step of the player's minions at a chosen game time (Program.PlayerMinionsTick).
        public Task MinionsTick(double now) => Exchange(async (stream, cipher) =>
        {
            await Program.PlayerMinionsTick("paladin-integration", stream, cipher, State, now);
            return null;
        });

        // One step of the player's links at a chosen game time (Program.LinkSkillsTick).
        public Task LinksTick(double now) => Exchange(async (stream, cipher) =>
        {
            await Program.LinkSkillsTick("paladin-integration", stream, cipher, State, now);
            return null;
        });

        // The default attack on a monster, until its hit has resolved (Program.Attack).
        public Task Attack(uint id, SpawnedMonster m) => Exchange(async (stream, cipher) =>
        {
            await Program.Attack("paladin-integration", stream, cipher, State, new(id, SkillCatalog.FirstSkillId, 1, 1), m,
                State.Clock.Elapsed.TotalSeconds);
            return null;
        });

        // One second of Righteous Fire's burn around the player (Program.BurnAround).
        public Task Burn(RighteousFireState rf) => Exchange(async (stream, cipher) =>
        {
            await (Task)GatewayMethod("BurnAround").Invoke(null, ["paladin-integration", stream, cipher, State, rf, 1.0])!;
            return null;
        });

        // A link skill's link once its cast is over (Program.StartLink): true when it linked.
        public async Task<bool> Link(ushort skill, uint target)
        {
            bool linked = false;
            await Exchange(async (stream, cipher) =>
            {
                linked = await Program.StartLink("paladin-integration", stream, cipher, State, skill, target);
                return null;
            });
            return linked;
        }

        // C→S 0x24 on an object, the connection kept open until the cast's effect is there.
        public Task UseOnAndWait(uint target, ushort skill, Func<bool> done) => Exchange(async (stream, cipher) =>
        {
            await (Task)GatewayMethod("OnUseObject").Invoke(null, ["paladin-integration", stream, cipher, State,
                new InstanceTransfer.UseSkillOnObject(target, skill, 1, 0), State.Clock.Elapsed.TotalSeconds])!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!done()) await Task.Delay(10, timeout.Token);
            await Task.Delay(100, timeout.Token); // the link's packets after it
            return null;
        });

        // A utility flask's on-use grounds and taunt (Program.MakeFlaskGrounds) with these local stats.
        public Task UseFlaskGrounds(string flask, IReadOnlyDictionary<string, int> local) => Exchange(async (stream, cipher) =>
        {
            await (Task)GatewayMethod("MakeFlaskGrounds").Invoke(null, ["paladin-integration", stream, cipher, State, flask, local, 8.0])!;
            return null;
        });
    }
}
