/// <summary>
/// One of a variety's TakeHit clips: its index in the rig's animation list (S→C 0x1b2 kind 0) and its length in
/// seconds (data/monster-takehit.json "indexes" and "lengths").
/// </summary>
readonly record struct HitAnimation(int Index, double Seconds);

/// <summary>
/// The gates on a monster's hit reaction (native Actor TakeHit recovery, sent by <c>SendMonsterFlinch</c>).
/// A hit must take at least a tenth of the monster's max life — the low end of PoE's stun range — so scratches do not
/// flinch. And a monster does not flinch again until its clip has played (at least 0.4 s): otherwise a fast attack, a
/// volley or an area hit restarts the clip and its SoundEvent and keeps cutting the monster's own attack and walk
/// animations. An accepted flinch stops the server's walk, invalidates its ordinary attack, and holds AI for that
/// same recovery. A known TakeHit clip's length supplies the recovery duration; the native ACT selects and times the
/// actual TakeHit animation (flinch_large is at least 0.45 s by its own .act condition).
/// </summary>
static class HitReaction
{
    public const double DamageFraction = 0.1;
    public const double CooldownSeconds = 0.4;

    /// <summary>
    /// The duration profile for an accepted flinch, or null when the monster does not flinch. The native ACT selects
    /// the presented TakeHit variant. Recovery claims the monster for this duration; call under the Monsters lock.
    /// </summary>
    public static HitAnimation? TryFlinch(SpawnedMonster m, int damage, double now, Random random)
    {
        if (damage <= 0 || m.Life == 0 || m.Dead || m.HitAnimations is not { Count: > 0 } clips) return null;
        if (m.CannotBeStunned || m.Mods.Effects.CannotBeStunned) return null;   // Unwavering: the flinch is the hit's stun
        if (damage < m.MaxLife * DamageFraction) return null;
        if (now < m.FlinchUntil) return null;
        var clip = clips[random.Next(clips.Count)];
        var at = m.Walk.Position(now);
        m.Walk.Teleport(at.X, at.Y, now);
        m.CollisionPosition = at;
        m.LastTarget = null;   // after recovery the chase must acquire a fresh path, even to the same target cell
        if (m.SwingGeneration == m.ActionGeneration && !m.StageRunning) m.BusyUntil = now;
        m.SwingGeneration = null;
        m.ActionGeneration++;
        m.FlinchUntil = now + Math.Max(CooldownSeconds, clip.Seconds);
        return clip;
    }
}
