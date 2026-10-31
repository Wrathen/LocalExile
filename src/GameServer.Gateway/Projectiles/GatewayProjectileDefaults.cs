using GameServer.Core.Projectiles;

/// <summary>Basic straight-flight behavior, independent of any concrete skill provider.</summary>
static class GatewayProjectileDefaults
{
    public static ProjectileProfile Resolve(string metadata, Func<string, int> get, double speed, bool attack) =>
        GatewayProjectileModifiers.Resolve(Create(metadata, get, speed), get, attack);

    /// <summary>Unresolved common baseline. Attach intrinsic stages before resolving modifiers once.</summary>
    public static ProjectileProfile Create(string metadata, Func<string, int> get, double speed) =>
        new(metadata)
        {
            Speed = speed,
            // One movement instance: the client's 15 m cap, tunable through the game-data setting.
            Range = SystemSettings.Default.ProjectileMaximumRangeCells,
            Count = get("base_number_of_projectiles") is > 0 and var count ? count : 1,
            AimSpreadDistance = 40,
            SkipDeniedContacts = true,
            SkipDeniedReturnContacts = true,
            ExcludeTurnaroundTarget = true,
            ReturnOvershoot = 150
        };
}
