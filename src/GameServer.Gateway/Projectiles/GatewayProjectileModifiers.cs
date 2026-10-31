using GameServer.Core.Projectiles;

/// <summary>Binds resolved stats to source-neutral projectile profiles and their child stages.</summary>
static class GatewayProjectileModifiers
{
    public static ProjectileProfile Resolve(ProjectileProfile profile, Func<string, int> get, bool attack,
        bool chainInputConsumed = false, bool intrinsicSplitCount = false)
    {
        var p = profile;
        int splitTargets = Math.Max(get("projectile_number_to_split"), get("projectile_number_of_targets_to_split_to"));
        int extra = get("number_of_additional_projectiles")
            + get(attack ? "attacks_number_of_additional_projectiles" : "spells_number_of_additional_projectiles");
        int pierce = Math.Max(get("projectile_number_of_targets_to_pierce"), get("projectile_base_number_of_targets_to_pierce"));
        if (attack) pierce += Math.Max(get("arrow_number_of_targets_to_pierce"), get("arrow_base_number_of_targets_to_pierce"));
        if (get("always_pierce") != 0 || get("virtual_always_pierce") != 0
            || attack && (get("arrows_always_pierce") != 0 || get("base_arrows_always_pierce") != 0)) pierce = -1;
        int chains = Math.Max(get("number_of_chains"), get("virtual_number_of_chains_for_projectiles"));
        if (chainInputConsumed) chains = 0;
        bool forks = get("projectiles_fork") != 0 || get("virtual_projectiles_fork") != 0
            || (attack ? get("attack_projectiles_fork") != 0 || get("arrows_fork") != 0
                : get("projectiles_from_spells_fork") != 0);
        int forkCount = Math.Max(get("virtual_number_of_forks_for_projectiles_final"),
            (forks ? 1 : 0) + get("number_of_additional_forks_base")
                + (attack ? get("attack_projectiles_fork_additional_times") : 0));
        var modifiers = new ProjectileModifiers
        {
            ExtraProjectiles = extra, Pierce = pierce,
            Fork = forkCount, Chain = chains,
            Split = Math.Max(splitTargets, get("intermediary_projectile_number_of_targets_to_split_to")),
            Returns = get("projectiles_return") != 0 || attack && get("attack_projectiles_return") != 0
                || get("projectile_return_%_chance") >= 100,
            CannotPierce = get("cannot_pierce") != 0 || get("virtual_projectiles_cannot_pierce") != 0
                || !attack && get("projectiles_from_spells_cannot_pierce") != 0,
            CannotFork = get("virtual_projectiles_cannot_fork") != 0 || !attack && get("projectiles_from_spells_cannot_fork") != 0,
            CannotChain = get("virtual_projectiles_cannot_chain") != 0 || !attack && get("projectiles_from_spells_cannot_chain") != 0,
            CannotSplit = get("projectiles_cannot_split") != 0,
            CannotReturn = get("skill_projectiles_cannot_return") != 0
        };
        // A declared non-primary consumer keeps quantity when the competing split is prohibited.
        bool intrinsicCountOwnsQuantity = p.CountConsumer != ProjectileCountConsumer.Primary
            && (p.CannotSplit || modifiers.CannotSplit);
        // An intrinsic split budget owns this conversion even when the generic stat also enables it.
        if (!intrinsicSplitCount && !intrinsicCountOwnsQuantity
            && get("modifiers_to_number_of_projectiles_instead_apply_to_splitting") != 0)
        {
            if (p.CountConsumer != ProjectileCountConsumer.Primary)
                throw new InvalidOperationException("Unresolved competing projectile count conversions: " + p.Id);
            p = p with { Count = 1, Split = p.Split + Math.Max(0, extra) };
            modifiers = modifiers with { ExtraProjectiles = 0 };
        }
        if (intrinsicSplitCount) { p = p with { Split = p.Split + extra }; modifiers = modifiers with { ExtraProjectiles = 0, Split = 0 }; }
        if (get("projectile_distance_override") is > 0 and var distance)
        {
            if (p.EndEmission is not null || p.Lobbed)
                throw new InvalidOperationException("Distance override needs an explicit early-end recipe for " + p.Id);
            p = p with { Range = distance };
        }
        // "Projectiles from Supported Skills have X metres maximum Direct Flight": the values are client units (10 per
        // metre), so 48 is 4.8 m. It caps rather than replaces either the baseline or a distance override.
        if (get("projectile_maximum_range_override") is > 0 and var maximum)
            p = p with { Range = Math.Min(p.Range, maximum) };
        ProjectileProfile ReturnControls(ProjectileProfile target) => target with
        {
            ReturnChancePercent = Math.Max(target.ReturnChancePercent, Math.Clamp(get("projectile_return_%_chance"), 0, 100)),
            PierceAllOnReturn = target.PierceAllOnReturn || get("returning_projectiles_always_pierce") != 0,
            ReturnSpeedMultiplier = target.ReturnSpeedMultiplier * Math.Max(.01, 1 + get("returned_projectile_speed_+%") / 100d)
        };
        p = ReturnControls(ProjectileParameters.Resolve(p, modifiers));
        // Children receive already resolved continuation grants, never another count expansion.
        if (p.EndEmission is { } endEmission)
            p = p with { EndEmission = endEmission with { Child = ReturnControls(ProjectileParameters.Resolve(endEmission.Child,
                modifiers with { ExtraProjectiles = 0 })) } };
        if (p.TravelEmission is { } travelEmission)
            p = p with { TravelEmission = travelEmission with { Child = ReturnControls(ProjectileParameters.Resolve(travelEmission.Child,
                modifiers with { ExtraProjectiles = 0 })) } };
        return p;
    }
}
