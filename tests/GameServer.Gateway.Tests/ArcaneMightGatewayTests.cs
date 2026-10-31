using GameServer.Core.Protocol.Instance;
using GameServer.Core.Projectiles;
using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

public partial class SpellNoDamageTests
{
    [Theory]
    [InlineData("KineticBlast", 16, 173, 593)]
    [InlineData("KineticBlastAltX", 16, 124, 424)]
    public void Arcane_might_native_fixed_fire_report_uses_full_damage_effectiveness(string effect, int spellIncrease,
        int withoutWand, int withWand)
    {
        using var f = new Fixture(effect, "Driftwood Wand");
        f.Set("attack_minimum_added_fire_damage", 100);
        f.Set("attack_maximum_added_fire_damage", 100);
        f.Set("spell_damage_+%", spellIncrease);
        Assert.Equal(withoutWand, Value(f.Panel(), "main_hand_minimum_fire_damage"));
        Assert.Equal(withoutWand, (int)Math.Floor(f.Hit()[DamageType.Fire].Min));
        var worn = f.Items.Add(PlayerInventories.Helmet, 0, 0,
            new UniqueCatalog(Program.Items).Create("Crown of Eyes", new Random(3))!);
        Assert.Equal(withoutWand, Value(f.Panel(), "main_hand_minimum_fire_damage"));
        f.Set("spell_damage_+%", spellIncrease + 200);
        Assert.Equal(withWand, Value(f.Panel(), "main_hand_minimum_fire_damage"));
        Assert.Equal(withWand, (int)Math.Floor(f.Hit()[DamageType.Fire].Min));
        f.Items.Remove(PlayerInventories.Helmet, worn.Id);
        Assert.Equal(withWand, Value(f.Panel(), "main_hand_minimum_fire_damage"));
    }

    [Theory]
    [InlineData("Driftwood Wand", 2)]
    [InlineData("Driftwood Club", 1)]
    public void Arcane_might_wand_mastery_uses_the_active_weapon_and_matches_default_attack_panel(string weapon, double factor)
    {
        using var f = new Fixture(weapon: weapon);
        f.Set("spell_damage_+%", 0);
        f.Set("attack_minimum_added_chaos_damage", 100);
        f.Set("attack_maximum_added_chaos_damage", 100);
        // The exported mastery grant, rather than an inferred weapon tag or a skill-only ratio.
        f.Set("additive_spell_damage_modifiers_apply_to_attack_damage_at_%_value_while_wielding_wand", 100);
        f.Set("spell_damage_+%", 100);
        var stats = Program.CharacterPlayerStats(f.Conn);
        Assert.Equal((int)(100 * factor), stats[Program.Stats.Key("main_hand_minimum_chaos_damage")!.Value]);
        var main = (SkillCatalog.Unarmed)Method("WeaponFor").Invoke(null, [f.Conn])!;
        Assert.Equal(100 * factor, Program.AttackHit(main, stats, weapon == "Driftwood Club")[DamageType.Chaos].Min);
    }

    [Fact]
    public void Arcane_might_crown_can_be_created_equipped_and_removed_without_stacking_with_Kinetic_Blast()
    {
        using var f = new Fixture("KineticBlast", "Driftwood Wand");
        f.Set("spell_damage_+%", 0);
        var basis = f.Hit();
        f.Set("spell_damage_+%", 100);
        var skillOnly = f.Hit();
        Assert.Equal(basis.MinTotal * 2.5, skillOnly.MinTotal, 8);

        var crown = new UniqueCatalog(Program.Items).Create("Crown of Eyes", new Random(1));
        Assert.NotNull(crown);
        var worn = f.Items.Add(PlayerInventories.Helmet, 0, 0, crown);
        f.Conn.Pools = Program.PoolsFor(f.Conn, 100);
        Assert.Equal(skillOnly.MinTotal, f.Hit().MinTotal, 8);
        var own = Program.CharacterPlayerStats(f.Conn);
        Assert.Equal(1, own[StatKeys.SpellDamageIncreasesApplyToAttacks150]);
        Assert.Equal((int)Math.Floor(skillOnly.MinTotal), Value(f.Panel(), "main_hand_minimum_total_damage"));
        f.Items.Remove(PlayerInventories.Helmet, worn.Id);
        f.Conn.Pools = Program.PoolsFor(f.Conn, 100);
        Assert.Equal(skillOnly.MinTotal, f.Hit().MinTotal, 8);
        Assert.Equal(0, Program.CharacterPlayerStats(f.Conn).GetValueOrDefault(StatKeys.SpellDamageIncreasesApplyToAttacks150));
    }

    [Fact]
    public async Task Arcane_might_generic_ratio_affects_both_projectile_packets_and_panel_without_changing_raw_inputs()
    {
        using var f = new Fixture("KineticBlast", "Driftwood Wand");
        f.Set("spell_damage_+%", 0);
        var basis = f.Hit();
        f.Set("spell_damage_+%", 20);
        f.Set("additive_spell_damage_modifiers_apply_to_attack_damage_at_%_value", 200);
        await using var runtime = new ProjectileRuntimeFixture(f.Conn);
        var gems = (IEnumerable<(ushort Id, int Effect, string Name, int Level, int Quality, uint Gem)>)
            Method("GemSkillEffects").Invoke(null, [f.Conn])!;
        var roll = Method("CaptureSkillCriticalRoll").Invoke(null, [f.Conn, f.Skill])!;
        var use = (ProjectileUse)Method("CaptureGemProjectile").Invoke(null,
            [f.Conn, runtime.Session, f.Skill, gems.Single(g => g.Id == f.Skill), (100, 32), roll])!;
        var offense = Assert.IsType<ProjectileOffense>(use.Payload);
        Assert.Equal(basis.MinTotal * 1.4, offense.Hit.MinTotal, 8);
        Assert.NotNull(offense.AreaHit);
        Assert.Equal(basis.MinTotal * 1.4, offense.AreaHit.MinTotal, 8);
        Assert.Equal((int)Math.Floor(offense.Hit.MinTotal), Value(f.Panel(), "main_hand_minimum_total_damage"));
        Assert.Equal(20, f.Conn.PlayerStats[StatKeys.SpellDamagePct]);
    }

    [Fact]
    public void Arcane_might_mastery_refund_and_inactive_weapon_set_update_the_default_attack()
    {
        using var f = new Fixture(weapon: "Driftwood Wand");
        f.Set("spell_damage_+%", 20);
        f.Set("attack_minimum_added_chaos_damage", 100);
        f.Set("attack_maximum_added_chaos_damage", 100);
        f.Conn.Passives.Add(53828);
        f.Conn.MasteryEffects = new() { [53828] = 61352 };
        double Chaos() => Program.AttackHit((SkillCatalog.Unarmed)Method("WeaponFor").Invoke(null, [f.Conn])!,
            Program.CharacterPlayerStats(f.Conn), false)[DamageType.Chaos].Min;
        Assert.Equal(120, Chaos());
        var wand = Assert.Single(f.Items.Items(PlayerInventories.Weapon1));
        f.Items.Remove(PlayerInventories.Weapon1, wand.Id);
        var inactiveWand = f.Items.Add(14, 0, 0, wand.Item);
        f.Items.Add(PlayerInventories.Weapon1, 0, 0, ItemCatalog.Create(Program.Items.Find("Driftwood Club")!));
        Assert.Equal(100, Chaos());
        var club = Assert.Single(f.Items.Items(PlayerInventories.Weapon1));
        f.Items.Remove(PlayerInventories.Weapon1, club.Id);
        f.Items.Remove(14, inactiveWand.Id);
        f.Items.Add(PlayerInventories.Weapon1, 0, 0, wand.Item);
        Assert.Equal(120, Chaos());
        f.Conn.MasteryEffects = [];
        f.Conn.Passives.Remove(53828);
        Assert.Equal(100, Chaos());
        Assert.Equal(0, Program.CharacterPlayerStats(f.Conn).GetValueOrDefault(Program.Stats.Key(ArcaneMight.Wand)!.Value));
    }

    [Fact]
    public void Arcane_might_crown_persists_and_stronger_grant_removal_falls_back_without_panel_accumulation()
    {
        using var f = new Fixture("KineticBlast", "Driftwood Wand");
        f.Set("spell_damage_+%", 20);
        f.Set("attack_minimum_added_chaos_damage", 100);
        f.Set("attack_maximum_added_chaos_damage", 100);
        var crown = new UniqueCatalog(Program.Items).Create("Crown of Eyes", new Random(3))!;
        Assert.Equal(6, crown.Mods.Count(m => m.Section == ModSection.Explicit));
        var worn = f.Items.Add(PlayerInventories.Helmet, 0, 0, crown);
        PlayerInventories.Forget(f.Conn.Character!);
        var restored = Assert.Single(f.Items.Items(PlayerInventories.Helmet));
        Assert.Equal(worn.Id, restored.Id);
        Assert.Equal(crown.NameWords, restored.Item.NameWords);
        Assert.Equal(crown.VisualIdentity, restored.Item.VisualIdentity);
        Assert.Equal(crown.Mods.Select(m => m.Hash), restored.Item.Mods.Select(m => m.Hash));
        Assert.All(crown.Mods.Zip(restored.Item.Mods), p => Assert.Equal(p.First.Values, p.Second.Values));
        var weapon = (SkillCatalog.Unarmed)Method("WeaponFor").Invoke(null, [f.Conn])!;
        Assert.Equal(130, Program.AttackHit(weapon, Program.CharacterPlayerStats(f.Conn), false)[DamageType.Chaos].Min);
        Assert.Equal(130, Program.CharacterPlayerStats(f.Conn)[Program.Stats.Key("main_hand_minimum_chaos_damage")!.Value]);
        f.Conn.Passives.Add(50932); // Whisperer and Crown both grant the same fixed 150%.
        Assert.Equal(130, Program.AttackHit(weapon, Program.CharacterPlayerStats(f.Conn), false)[DamageType.Chaos].Min);
        f.Conn.Passives.Remove(50932);
        f.Set(ArcaneMight.Ratio, 200);
        Assert.Equal(14000, Program.Skills.DamageEffectiveness(f.Effect, 1));
        Assert.Equal(196, f.Hit()[DamageType.Chaos].Min); // 100 * 1.4 * 140% effectiveness.
        f.Conn.PlayerStats.Remove(Program.Stats.Key(ArcaneMight.Ratio)!.Value);
        Assert.Equal(182, f.Hit()[DamageType.Chaos].Min);
        for (int i = 0; i < 3; i++) Assert.Equal(182, Value(f.Panel(), "main_hand_minimum_chaos_damage"));
        // The character/default attack has no KB-local grant; Crown removal must clear it.
        f.Items.Remove(PlayerInventories.Helmet, restored.Id);
        var actor = Program.CharacterPlayerStats(f.Conn);
        Assert.Equal(100, Program.AttackHit(weapon, actor, false)[DamageType.Chaos].Min);
        Assert.Equal(182, f.Hit()[DamageType.Chaos].Min);
        Assert.Equal(20, f.Conn.PlayerStats[StatKeys.SpellDamagePct]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Arcane_might_release_snapshot_reaches_real_target_life_for_direct_and_area_damage(bool area)
    {
        using var f = new Fixture("KineticBlast", "Driftwood Wand", targetX: 60);
        f.Set("spell_damage_+%", 100);
        f.Set("global_always_hit", 1);
        f.Set("attack_minimum_added_chaos_damage", 100);
        f.Set("attack_maximum_added_chaos_damage", 100);
        await using var runtime = new ProjectileRuntimeFixture(f.Conn);
        var gems = (IEnumerable<(ushort Id, int Effect, string Name, int Level, int Quality, uint Gem)>)
            Method("GemSkillEffects").Invoke(null, [f.Conn])!;
        var roll = Method("CaptureSkillCriticalRoll").Invoke(null, [f.Conn, f.Skill])!;
        var use = (ProjectileUse)Method("CaptureGemProjectile").Invoke(null,
            [f.Conn, runtime.Session, f.Skill, gems.Single(g => g.Id == f.Skill), (100, 32), roll])!;
        var offense = Assert.IsType<ProjectileOffense>(use.Payload);
        Assert.Equal(14000, Program.Skills.DamageEffectiveness(f.Effect, 1));
        Assert.Equal(350, offense.Hit[DamageType.Chaos].Min); // 100 * 2.5 * 140% effectiveness.
        Assert.Equal(350, offense.AreaHit![DamageType.Chaos].Min);
        Assert.Equal(.6, use.Profile.ContactArea!.Multiplier); // Only the secondary explosion takes 40% less.
        // Keep only one existing damage route to make the target's life-loss assertion unambiguous. An attack gem leaves
        // from the weapon's contact keyframe, so the range is measured from there: it ends just past the target.
        use = use with { Profile = use.Profile with
        {
            Count = 1, Speed = 100, Range = 61.5 - use.Origin.X, Radius = .1, Pierce = 0,
            DirectDamage = !area, ContactArea = null, Pulse = null, EndEmission = null,
            EndArea = area ? new("arcane-snapshot-area", 5, Multiplier: .6) : null,
        } };
        f.Set("spell_damage_+%", 0);
        Assert.Equal(140, f.Hit()[DamageType.Chaos].Min);
        Assert.Equal(140, Value(f.Panel(), "main_hand_minimum_chaos_damage"));
        uint before = f.Target.Life;
        runtime.Release(use);
        runtime.Advance(1);
        // (5-9 physical + 100 chaos) * 2.5 * 1.4, with 0.6 only on the explosion route.
        Assert.InRange(before - f.Target.Life, area ? 220u : 367u, area ? 228u : 381u);
        Assert.Equal(350, offense.Hit[DamageType.Chaos].Min);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Brutality Support")]
    public void Arcane_might_real_weapon_local_damage_respects_effectiveness_and_support_restrictions(string? support)
    {
        using var f = new Fixture("KineticBlast", "Driftwood Wand", support);
        f.Set("spell_damage_+%", 20);
        var localFire = Program.Items.Mods.All.First(m => m.Stats.Count == 2
            && m.Stats[0].Id == "local_minimum_added_fire_damage" && m.Stats[1].Id == "local_maximum_added_fire_damage");
        var wand = Assert.Single(f.Items.Items(PlayerInventories.Weapon1));
        f.Items.Remove(PlayerInventories.Weapon1, wand.Id);
        f.Items.Add(PlayerInventories.Weapon1, 0, 0, wand.Item with
        {
            Mods = [.. wand.Item.Mods, new(ModSection.Explicit, localFire.Hash, [100, 100])],
        });
        var resolved = Method("SupportedSkill").Invoke(null, [f.Conn, f.Skill])!;
        var own = (Dictionary<uint, int>)resolved.GetType().GetProperty("Stats")!.GetValue(resolved)!;
        Assert.Equal(150, own.GetValueOrDefault(StatKeys.SkillSpellDamageIncreasesApplyToAttacks));
        Assert.Equal(support is null ? 182 : 0, f.Hit()[DamageType.Fire].Min);
        Assert.Equal(support is null ? 182 : 0, Value(f.Panel(), "main_hand_minimum_fire_damage"));
    }

    [Fact]
    public void Arcane_might_grants_do_not_modify_existing_spell_damage_or_raw_stats()
    {
        using var f = new Fixture("Fireball");
        f.Set("spell_damage_+%", 20);
        f.Set("spell_minimum_added_fire_damage", 100);
        f.Set("spell_maximum_added_fire_damage", 100);
        var before = f.Hit();
        f.Set(ArcaneMight.Basic, 1);
        f.Set(ArcaneMight.Ratio, 200);
        Assert.Equal(before.MinTotal, f.Hit().MinTotal);
        Assert.Equal(before.MaxTotal, f.Hit().MaxTotal);
        Assert.Equal(20, f.Conn.PlayerStats[StatKeys.SpellDamagePct]);
    }
}
