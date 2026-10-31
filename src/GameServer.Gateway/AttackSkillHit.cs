using GameServer.Core.World;

/// <summary>The existing main-hand attack-gem hit before critical strikes and target mitigation.</summary>
static class AttackSkillHit
{
    public static DamagePacket Resolve(SkillCatalog.Unarmed weapon, IReadOnlyDictionary<uint, int> character,
        IEnumerable<(uint, int)> levelConversionInputs, StatCatalog catalog, int effectiveness, bool melee = false,
        bool projectile = false)
    {
        // Damage modifiers and added attack damage keep the existing character-only inputs. Skill-owned damage
        // modifiers are not implemented in combat and must not become panel-only effects here.
        int Get(string name) => catalog.Key(name) is uint key ? character.GetValueOrDefault(key) : 0;
        var raw = SpellDamage.WithEffectiveness(SupportedAttackDamage.Raw(weapon, Get), effectiveness);
        // Match ConversionFor exactly: player keys win; level-only skill keys fill gaps, without quality conversion.
        var conversionInputs = new Dictionary<uint, int>(character);
        foreach (var (key, value) in levelConversionInputs) conversionInputs.TryAdd(key, value);
        return MaximumAttackDamage.Apply(RageStats.ApplyAttack(PlayerHitDamage.Resolve(raw, Get,
            DamageStats.Conversion(conversionInputs, catalog, attack: true), spell: false,
            melee: melee, projectile: projectile, wieldingWand: weapon.WieldingWand), Get), Get);
    }
}
