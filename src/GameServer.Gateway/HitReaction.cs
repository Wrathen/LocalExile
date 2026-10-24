/// <summary>
/// The gates on a monster's hit reaction (S→C 0x1b2, sent by <c>SendFlinch</c> in Program.cs).
/// A hit must take at least a tenth of the monster's max life — the low end of PoE's stun range — so scratches do not
/// flinch. And a monster flinches at most every 0.4 s: otherwise a fast attack, a volley or an area hit restarts the
/// clip and its SoundEvent and keeps cutting the monster's own attack and walk animations, while the server goes on
/// moving and attacking it on its own timeline.
/// </summary>
static class HitReaction
{
    public const double DamageFraction = 0.1;
    public const double CooldownSeconds = 0.4;

    /// <summary>
    /// Whether this hit flinches the monster. A flinch claims the cooldown, so call it under the Monsters lock.
    /// </summary>
    public static bool TryFlinch(SpawnedMonster m, int damage, double now)
    {
        if (m.Life == 0 || m.HitAnimations is not { Count: > 0 }) return false;
        if (damage < m.MaxLife * DamageFraction) return false;
        if (now - m.LastFlinchAt < CooldownSeconds) return false;
        m.LastFlinchAt = now;
        return true;
    }
}
