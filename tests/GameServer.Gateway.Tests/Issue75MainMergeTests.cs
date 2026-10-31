using System.Reflection;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

public class Issue75MainMergeTests
{
    static uint Key(string name) => Program.Stats.Key(name)!.Value;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Gambler_critical_kill_leeches_direct_damage_and_awards_one_soul_with_output_held(bool spell)
    {
        using var souls = new VaalSoulGatewayTests.Fixture();
        var conn = souls.Conn;
        conn.PlayerLife = 100;
        conn.PlayerStats[Key("global_cannot_crit")] = 0;
        conn.PlayerStats[Key("marauder_hidden_ascendancy_damage_+%_final")] = -30;
        conn.PlayerStats[Key("base_chance_to_deal_triple_damage_%")] = 100;
        conn.PlayerStats[Key("treat_enemy_resistances_as_negated_on_elemental_damage_hit_%_chance")] = 100;
        conn.PlayerStats[Key("leech_%_is_instant")] = 100;
        var target = new SpawnedMonster("combined target", new(110, 100, 0), 0, 2, 100)
        {
            Variety = new(0, "combined-target", "combined target", "Metadata/Monsters/Monster", null, null),
            Mods = new([], [], new(new Dictionary<string, int> { ["base_fire_damage_resistance_%"] = 40 }))
        };
        target.Impale.Apply(100, now: 0);
        conn.Monsters.Add(42, target);
        await using var runtime = new ProjectileRuntimeFixture(conn);
        await runtime.HoldOutput();
        var shot = new ProjectileOffense(DamagePacket.Of(DamageType.Physical, 100, 100)
            .Add(DamageType.Fire, new(100, 100)), spell, int.MaxValue, 10000, 200, "combined")
        {
            AccuracyProfile = new(0, CannotBeEvaded: true), CriticalRoll = new(0), ImpaleSettings = new(100),
            Leech = LeechSources.From(n => n == "life_leech_from_any_damage_permyriad" ? 100 : 0, !spell)
        };
        runtime.Release(runtime.Shot(offense: shot, origin: new(100, 100), aim: new(200, 100)));
        runtime.Advance(2);
        // Each component: 100 * .7 * 3 * 2. Fire's inverted 40% resistance gives 588, physical gives 420.
        // The existing 10 reflection contributes to the kill, but neither leech nor another soul event.
        Assert.True(target.Dead);
        Assert.Equal(110u, conn.PlayerLife);
        Assert.Equal(2, target.Impale.Stacks);
        Assert.Equal(52, target.Impale.Bonus);
        Assert.Equal(1u, souls.Budget.Current);
        Assert.True(target.SoulKillProcessed);
        Assert.False(Program.CollectVaalSoulOnKill(conn, 42, target));
        Assert.Equal(1u, souls.Budget.Current);
        Assert.Equal(0, runtime.Available);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mode_catalog_equipment_preserves_saved_base_roll_and_fractional_Ward_conversion(bool ruthless)
    {
        TestSaves.Ensure();
        string name = "ModeWard" + Guid.NewGuid().ToString("N");
        var conn = new ClientState(new(1, "mode ward", 100, 100, 10, 10, null, []), 1, 1)
            { Character = name, ClassRow = 4, LeagueForm = ruthless ? "ruthless" : null };
        var catalog = Program.ItemsFor(conn);
        var items = PlayerInventories.For(name, catalog);
        try
        {
            var type = catalog.Find("Plate Vest")!;
            var item = ItemCatalog.Create(type, defenceBaseRoll: 31) with { Quality = 0 };
            items.Add(PlayerInventories.BodyArmour, 0, 0, item);
            Func<string, int> stats = key => key == "ward_instead_of_%_body_armour_and_evasion" ? 50 : 0;
            var result = ((decimal Armour, decimal Evasion, int EnergyShield, decimal Ward))typeof(Program)
                .GetMethod("EquippedBaseDefences", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [conn, stats])!;
            Assert.Equal((13.5m, 0m, 0, 13.5m), result);
            // The minimum-roll alternative would incorrectly contribute 9.5 to each pool.
            Assert.Equal(19, catalog.Defences(type)!.Value.ArmourMin);
        }
        finally { conn.Closed = true; PlayerInventories.Forget(name); }
    }
}
