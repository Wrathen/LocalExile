namespace GameServer.Gateway.Tests;

public sealed partial class AscendancyGatewayIntegrationTests
{
    [Fact]
    public void Behemoth_opt_in_checks_installed_bytes_and_preserves_other_admission_gates()
    {
        var identity = Program.Ascendancies.Find(1, AscendancyFamily.Phrecia, "Behemoth")!.Identity;
        int checkedArchive = 0;
        void Verify() => checkedArchive++;
        Assert.Throws<AscendancyRuleException>(() => AscendancyClientCompatibility.RequireCompatible(identity, false, Verify));
        Assert.Equal(0, checkedArchive);
        AscendancyClientCompatibility.RequireCompatible(identity, true, Verify);
        Assert.Equal(1, checkedArchive);
        Assert.Throws<AscendancyRuleException>(() => AscendancyClientCompatibility.RequireCompatible(identity, true,
            () => throw new AscendancyRuleException("wrong archive")));
        Assert.Throws<AscendancyRuleException>(() => AscendancyClientCompatibility.RequireCompatible(
            identity with { Id = "Juggernaut" }, true, Verify));
        Assert.Throws<AscendancyRuleException>(() => AscendancyClientCompatibility.RequireCompatible(
            identity with { Revision = "3.30" }, true, Verify));
        AscendancyClientCompatibility.RequireCompatible(null, true, Verify);
        AscendancyClientCompatibility.RequireCompatible(identity with { Family = AscendancyFamily.Normal }, true, Verify);
        Assert.Equal(1, checkedArchive);
    }

    public static IEnumerable<object[]> BehemothBuilds()
    {
        yield return [new ushort[] { 7128, 49937, 4561, 28815, 59943, 37308, 42923, 38080 }];
        yield return [new ushort[] { 7128, 49937, 16671, 48586, 9053, 13061, 22167, 10135 }];
        yield return [new ushort[] { 7128, 49937, 42923, 38080, 24188, 2103, 34257, 29437 }];
    }

    [Theory]
    [MemberData(nameof(BehemothBuilds))]
    public async Task Behemoth_legal_eight_point_builds_consume_nodes_and_survive_paid_refund_reallocation_and_reload(ushort[] nodes)
    {
        using var game = new GatewayCharacter(classRow: 0, baseClass: 1, choice: "Behemoth",
            refundPoints: 100, level: 100);
        var c = game.State;
        Assert.Null((await game.Change(nodes, true)).Error);
        Assert.Equal(8, Program.AscendancyRules.SpentPoints(1, c.MainAscendancy));
        var stats = Program.CharacterPlayerStats(c);
        int Get(string name) => stats.GetValueOrDefault(Program.Stats.Key(name)!.Value);
        Assert.Equal(1, Get(RampageSession.Source));
        Assert.Equal(1, Get("base_your_auras_are_disabled"));
        Assert.Equal(1, Get("your_warcry_skills_are_disabled"));
        Assert.Equal(1, Get("spell_skills_deal_no_damage"));
        Assert.Equal(10, Get("base_actor_scale_+%"));
        Assert.True(c.Pools.Strength > Program.Skills.ClassFor(0).Strength);
        if (nodes.Contains((ushort)37308))
        {
            Assert.Equal(30, Program.FortificationOf(c).Value.Maximum);
            var target = new SpawnedMonster("fortify target", new(34, 32, 0), 0, 0, 100);
            Assert.True(Program.FortificationOnHitLocked(c, target, 1000, true, stats));
            stats = Program.CharacterPlayerStats(c);
            int stacks = Get(FortificationSession.Current);
            Assert.True(stacks > 0); Assert.Equal(stacks, Get("attack_speed_+%"));
            Assert.Equal(stacks * 12, Get("life_regeneration_rate_per_minute_%"));
        }
        if (nodes.Contains((ushort)13061))
        {
            Assert.Equal(45, Get("virtual_maximum_rage"));
            Assert.Equal(1, Get("vaal_attack_rage_cost_instead_of_souls_per_use"));
            Assert.Equal(1, Get("cannot_gain_rage_during_soul_gain_prevention"));
            Assert.Equal(1000, Get("main_hand_critical_strike_chance"));
            c.PlayerLife = c.PlayerMaxLife / 2;
            Assert.Equal(1, Program.CharacterPlayerStats(c)[Program.Stats.Key("virtual_extra_critical_rolls")!.Value]);
            c.PlayerLife = c.PlayerMaxLife;
            Assert.Equal(0, Program.CharacterPlayerStats(c)[Program.Stats.Key("virtual_extra_critical_rolls")!.Value]);
        }
        if (nodes.Contains((ushort)2103))
        {
            Assert.True(c.Pools.Armour > 0); Assert.True(c.Pools.Mana > 0);
            Assert.True(c.Pools.ArmourAppliesToChaosDamage);
            Assert.Equal(c.Pools.Armour / 450 * 2, Get("attack_damage_+%"));
            Assert.Equal(10, Get("chance_to_double_armour_effect_on_hit_%_per_enemy_hit_taken_recently_capped"));
        }
        var original = c.Pools;
        Assert.Null((await game.Change(nodes, false, refundPoints: true)).Error);
        Assert.Equal((ushort)60, c.RefundPoints); Assert.Empty(c.MainAscendancy.Allocated);
        Assert.Equal(0, Program.CharacterPlayerStats(c).GetValueOrDefault(Program.Stats.Key(RampageSession.Source)!.Value));
        Assert.False(c.RampageWorld.Enabled);
        Assert.Null((await game.Change(nodes, true)).Error);
        var loaded = game.Reload();
        Assert.Equal(nodes.Order(), loaded.MainAscendancy.Allocated.Order());
        Assert.Equal((ushort)60, loaded.RefundPoints);
        Assert.Equal((original.Strength, original.Mana, original.Armour),
            (loaded.Pools.Strength, loaded.Pools.Mana, loaded.Pools.Armour));
    }
}
