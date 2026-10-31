using GameServer.Core.World;

/// <summary>Incoming player hits only, after resistance and per-type rounding, before Fortification and Energy Shield.
/// Callers retain immunity, suppression and block ordering. DoT uses resistance directly and never enters this pass.</summary>
static class PlayerHitMitigation
{
    public const string ArmourAppliesToChaos = "base_armour_applies_to_chaos_damage";

    public const string ArmourPercentToElemental = "armour_%_applies_to_fire_cold_lightning_damage",
        ArmourPercentToChaos = "base_armour_%_applies_to_chaos_damage";

    /// <summary>The share of armour that applies to a hit's damage of this type, 0-100 (PoB CalcDefence
    /// percentOfArmourApplies, its BASE sum capped at 100): all of it to physical, and to chaos with
    /// base_armour_applies_to_chaos_damage; else "X% of Armour applies to Fire, Cold and Lightning / Chaos Damage from
    /// Hits" (Kahuturoa's Leadership).</summary>
    public static int ArmourPercent(CharacterStats.Pools pools, DamageType type, Func<string, int> get) => type switch
    {
        DamageType.Physical => 100,
        DamageType.Chaos => pools.ArmourAppliesToChaosDamage ? 100 : Math.Clamp(get(ArmourPercentToChaos), 0, 100),
        _ when DamageTypes.IsElemental(type) => Math.Clamp(get(ArmourPercentToElemental), 0, 100),
        _ => 0,
    };

    /// <summary>Reuse the ordinary armour model for non-physical hits that armour applies to (<paramref name="armourPercent"/>,
    /// <see cref="ArmourPercent"/>; without it, chaos with the 100% flag). Its denominator reads post-resistance damage (PoB
    /// CalcDefence 16de4b82, applyDmgTakenConversion) and its rating is that share of armour; it adds to the elemental
    /// damage reduction under the shared cap; additional physical reduction stays physical. A per-hit armour decision
    /// overrides only this calculation and never writes the permanent pools.</summary>
    public static int AfterResistance(CharacterStats.Pools pools, DamageType type, int damage,
        int physicalReduction = 0, int elementalReduction = 0, int? effectiveArmour = null, int? armourPercent = null)
    {
        int armour = effectiveArmour ?? pools.Armour;
        if (type == DamageType.Physical) return DamageModel.MitigatePhysical(damage, armour, physicalReduction);
        int share = armourPercent ?? (type == DamageType.Chaos && pools.ArmourAppliesToChaosDamage ? 100 : 0);
        int fromArmour = share > 0 ? DamageModel.ArmourReduction((int)((long)armour * share / 100), damage) : 0;
        if (DamageTypes.IsElemental(type)) return DamageModel.Reduce(damage, fromArmour + elementalReduction);
        return share > 0 ? DamageModel.Reduce(damage, fromArmour) : damage;
    }
}
