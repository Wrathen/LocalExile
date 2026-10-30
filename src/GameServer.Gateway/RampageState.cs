/// <summary>
/// One streak on a pausable game clock. Callers submit already-qualified increments after committing combat.
/// Hit cooldowns, hit-plus-kill credit, ownership and duplicate-event rejection belong to that caller: the primary
/// sources do not establish those policies. In particular this class invents no melee-hit cooldown.
/// </summary>
sealed class RampageState
{
    public const int MaximumStacks = 1000;
    public const double DurationSeconds = 5;
    readonly int[] thresholds;
    int stacks;
    double? expiresAt;
    double acceptedThrough;

    public RampageState(IEnumerable<int> thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        this.thresholds = thresholds.ToArray();
        if (this.thresholds.Any(t => t is < 1 or > MaximumStacks)
            || this.thresholds.Zip(this.thresholds.Skip(1)).Any(p => p.First >= p.Second))
            throw new ArgumentException("Rampage thresholds must be strictly increasing and between 1 and 1000.", nameof(thresholds));
    }

    public readonly record struct Value(int Stacks, double? ExpiresAt)
    {
        // Character.ot's names say per_10, but pinned PoB 16de4b82 CalcSetup.lua 593-594 divides by 20.
        // ModStore.lua 382 floors the complete groups. These are increased damage/movement, never attack speed.
        public int DamagePercent => Math.Clamp(Stacks, 0, MaximumStacks) / 20 * 2;
        public int MovementPercent => Math.Clamp(Stacks, 0, MaximumStacks) / 20;
        public bool Active => Stacks > 0 && ExpiresAt is not null;
        public double RemainingSeconds(double now) => ExpiresAt is double until && double.IsFinite(until)
            && double.IsFinite(now) && now >= 0 ? Math.Clamp(until - now, 0, DurationSeconds) : 0;
    }

    public sealed record Change(bool Accepted, Value Before, Value After, IReadOnlyList<int> CrossedThresholds)
    {
        public bool Changed => Before != After;
    }

    /// <summary>Last observed state. Advance the clock before using it for damage, movement or presentation.</summary>
    public Value Current => new(stacks, expiresAt);
    public readonly record struct Carried(int Stacks, double Remaining);
    public Carried Carry(double now)
    {
        Advance(now);
        var result = new Carried(stacks, Current.RemainingSeconds(now));
        Clear();
        return result;
    }
    public void Restore(Carried carried, double now)
    {
        if (!AcceptTime(now)) return;
        stacks = carried.Remaining > 0 ? Math.Clamp(carried.Stacks, 0, MaximumStacks) : 0;
        expiresAt = stacks > 0 ? now + Math.Clamp(carried.Remaining, 0, DurationSeconds) : null;
    }

    /// <summary>
    /// Expires at the exact deadline. A paused clock simply supplies the same timestamp; wall time is never consulted.
    /// Non-finite, negative, backwards or unrepresentable-deadline timestamps leave the state untouched.
    /// </summary>
    public Change Advance(double now)
    {
        var before = Current;
        if (!AcceptTime(now)) return new(false, before, before, []);
        Expire(now);
        return new(true, before, Current, []);
    }

    /// <summary>
    /// Applies one or more qualified increments and refreshes the five-second deadline even at the cap.
    /// Every crossed tier is returned once in ascending order during this streak. Expiry/Clear permits new crossings.
    /// A zero or out-of-range count is refused without advancing time or refreshing a streak.
    /// </summary>
    public Change GainQualified(double now, int count = 1)
    {
        var before = Current;
        if (count is < 1 or > MaximumStacks || !AcceptTime(now)) return new(false, before, before, []);
        Expire(now);
        int previous = stacks;
        stacks = Math.Min(MaximumStacks, stacks + count);
        expiresAt = now + DurationSeconds;
        return new(true, before, Current, Array.AsReadOnly(thresholds.Where(t => previous < t && stacks >= t).ToArray()));
    }

    /// <summary>
    /// Removes the current streak, for an integration's source/death/connection policy. Keep the accepted-time
    /// watermark so a delayed older event cannot recreate it. A new game clock requires a new state instance.
    /// </summary>
    public bool Clear()
    {
        bool changed = stacks != 0 || expiresAt is not null;
        stacks = 0;
        expiresAt = null;
        return changed;
    }

    bool AcceptTime(double now)
    {
        double deadline = now + DurationSeconds;
        if (!double.IsFinite(now) || now < acceptedThrough || now < 0
            || !double.IsFinite(deadline) || deadline <= now) return false;
        acceptedThrough = now;
        return true;
    }

    void Expire(double now)
    {
        if (expiresAt is double deadline && now >= deadline) Clear();
    }
}
