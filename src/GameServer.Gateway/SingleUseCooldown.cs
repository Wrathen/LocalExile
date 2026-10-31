/// <summary>Generic recovery for one ordinary stored use. The native record retains BaseMilliseconds and
/// consumes base-time units at RecoveryRate; it must not receive a pre-divided cooldown.</summary>
readonly record struct SingleUseCooldown(int BaseMilliseconds, int RecoveryPercent)
{
    public double RecoveryRate => Math.Max(0, 1 + RecoveryPercent / 100.0);

    static readonly string[] Unsupported = [
        "base_added_cooldown_count", "virtual_added_cooldown_count", "quick_dodge_added_cooldown_count",
        "shockwave_support_added_cooldown_count", "support_double_number_of_cooldown_uses",
        "support_triple_number_of_cooldown_uses", "skill_cooldown_time_override_ms", "skill_cooldown_is_4_seconds",
        "virtual_cooldown_modifier_ms", "base_cooldown_speed_+%_final",
        "active_skill_cooldown_speed_+%_final_from_skill_specific_stat",
    ];

    public static SingleUseCooldown? ForSkill(SkillCatalog skills, StatCatalog catalog, int effect, int level,
        IReadOnlyDictionary<uint, int> player, IReadOnlyDictionary<uint, int> own)
    {
        int ms = skills.CooldownMs(effect, level);
        if (ms <= 0 || skills.CooldownUses(effect, level) != 1
            || skills.CooldownPausesWhileActive(effect)
            || !skills.IsSpell(effect) && !skills.IsAttack(effect) && !skills.IsWarcry(effect)) return null;
        int Get(IReadOnlyDictionary<uint, int> values, string name) => catalog.Key(name) is uint key ? values.GetValueOrDefault(key) : 0;
        if (Unsupported.Any(name => Get(player, name) != 0 || Get(own, name) != 0)
            || Get(player, "virtual_total_cooldown_count") > 1 || Get(player, "base_cooldown_count") > 1
            || Get(own, "virtual_total_cooldown_count") > 1 || Get(own, "base_cooldown_count") > 1) return null;
        // The player's virtual total already includes its base input. Skill inputs are separate, including quality.
        long recovery = (long)Get(player, "virtual_cooldown_speed_+%")
            + Get(own, "base_cooldown_speed_+%") + Get(own, "virtual_cooldown_speed_+%");
        // Warcry recovery is an increase for the Warcry type (PoB ModParser); the client adds it when is_warcry (0x14281dfa0).
        if (skills.IsWarcry(effect))
            recovery += (long)Get(player, "warcry_cooldown_speed_+%") + Get(own, "warcry_cooldown_speed_+%");
        // Native construction adds 100 in a signed i32. Zero recovery stalls rather than reversing the clock.
        return new(ms, (int)Math.Clamp(recovery, -100, int.MaxValue - 100));
    }
}

/// <summary>Elapsed game-time budget, used under the connection's Monsters lock. Rate changes settle the old rate
/// first, preserving progress through equipment/stat refreshes and pause.</summary>
sealed class SingleUseCooldownState(double at, double rate, double remainingBaseMilliseconds = 0,
    int totalBaseMilliseconds = 0)
{
    public double RemainingBaseMilliseconds { get; private set; } = remainingBaseMilliseconds;
    public int TotalBaseMilliseconds { get; private set; } = totalBaseMilliseconds;
    public double Rate { get; private set; } = rate;
    public int UseVersion { get; private set; }
    public ushort NativeSkillId { get; private set; }
    double? nativeRate;
    bool nativeAcknowledged, nativeDirty;
    double spentRate;
    double lastAt = at;

    public void Refresh(double now, double rate)
    {
        RemainingBaseMilliseconds = Math.Max(0, RemainingBaseMilliseconds - Math.Max(0, now - lastAt) * 1000 * Rate);
        if (Ready && !nativeDirty && nativeRate == Rate) nativeRate = null; // Known aligned completion.
        lastAt = Math.Max(lastAt, now);
        double next = Math.Max(0, rate);
        if (!Ready && UseVersion > 0 && next != Rate) nativeDirty = true;
        Rate = next;
    }

    public bool Ready => RemainingBaseMilliseconds <= 1e-6;
    public double RemainingSeconds => Ready ? 0 : Rate > 0 ? RemainingBaseMilliseconds / (1000 * Rate) : double.PositiveInfinity;
    public bool TryUse(double now, SingleUseCooldown plan)
    {
        Refresh(now, plan.RecoveryRate);
        if (!Ready) return false;
        RemainingBaseMilliseconds = plan.BaseMilliseconds;
        TotalBaseMilliseconds = plan.BaseMilliseconds;
        spentRate = Rate;
        nativeRate = null; // No native entry may be fabricated before the original acknowledgement reaches its gate.
        nativeAcknowledged = nativeDirty = false;
        NativeSkillId = 0;
        UseVersion++;
        return true;
    }

    public void ConfirmNativeUse(int version, ushort skillId, bool synchronizeProgress)
    {
        if (version != UseVersion || nativeAcknowledged) return;
        nativeAcknowledged = true;
        nativeRate = spentRate;
        NativeSkillId = skillId;
        nativeDirty |= synchronizeProgress;
    }
    public bool NeedsNativeRefresh => nativeAcknowledged && nativeDirty;
    public void NativeRefreshed()
    {
        nativeRate = Ready ? null : Rate;
        nativeDirty = false;
    }
}

/// <summary>One accepted use, reserved before ordinary action side effects. Async continuations keep this original
/// witness; a later action may cancel it, but cancellation never refunds an accepted use.</summary>
sealed record CooldownUseTicket(int Effect, int ActionToken, int ProjectileEpoch, bool Instant, bool Reserved = true,
    SingleUseCooldownState? State = null, int UseVersion = 0)
{
    bool consumed;
    public bool Current(ClientState conn)
    {
        lock (conn.Monsters) return conn.ProjectileActive(ProjectileEpoch) && conn.PlayerLife > 0
            && (Instant || conn.ActionCount == ActionToken && conn.ElementalAilments.ActionFactor(conn.Clock.Elapsed.TotalSeconds) > 0 && !conn.HitRecovery.Active(conn.Clock.Elapsed.TotalSeconds)) && (State is null || !Reserved || State.UseVersion == UseVersion);
    }
    public bool CommitNativeUse(ClientState conn, ushort skillId)
    {
        lock (conn.Monsters)
        {
            if (!Current(conn)) return false;
            if (Reserved) State?.ConfirmNativeUse(UseVersion, skillId, synchronizeProgress: Instant);
            return true;
        }
    }
    public bool TryExecute(ClientState conn, int effect)
    {
        lock (conn.Monsters)
        {
            if (!Reserved || consumed || Effect != effect || !Current(conn)) return false;
            consumed = true;
            return true;
        }
    }
}
