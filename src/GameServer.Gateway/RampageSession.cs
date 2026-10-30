/// <summary>Combat streak and pending tier crossings. Explicit policy v1: one stack per accepted owner melee
/// contact and one per owner kill, including both on a melee killing blow. A counter alone is not full Rampage.</summary>
sealed class RampageSession
{
    public const string Source = "melee_hits_grant_rampage_stacks";
    public static readonly int[] Thresholds = [15, 30, 45, 70, 100, 135, 175, 220, 270, 325, 385, 450, 520];
    readonly RampageState state = new(Thresholds);
    readonly HashSet<object> creditedKills = new(ReferenceEqualityComparer.Instance);
    readonly Queue<int> pendingTiers = new();
    public IReadOnlyList<int> PendingTiers => pendingTiers.ToArray();
    public RampageState.Value Current => state.Current;
    public RampageState.Carried Carry(double now) { pendingTiers.Clear(); return state.Carry(now); }
    public void Restore(RampageState.Carried carried, double now) { pendingTiers.Clear(); state.Restore(carried, now); }
    public int[] TakeTiers()
    {
        int[] result = pendingTiers.ToArray(); pendingTiers.Clear(); return result;
    }

    public RampageState.Value Observe(double now, bool enabled, bool alive, bool active)
    {
        var before = Current;
        state.Advance(now);
        if (!enabled || !alive || !active) { Clear(); return Current; }
        if (before.Active && !Current.Active) pendingTiers.Clear();
        return Current;
    }

    public bool MeleeHit(double now, bool enabled, bool alive, bool active, bool melee, bool hostile)
    {
        Observe(now, enabled, alive, active);
        if (!enabled || !alive || !active || !melee || !hostile) return false;
        return Gain(now);
    }

    public bool Kill(double now, object monster, bool enabled, bool alive, bool active, bool hostile)
    {
        Observe(now, enabled, alive, active);
        if (!enabled || !alive || !active || !hostile || creditedKills.Contains(monster) || !Gain(now)) return false;
        creditedKills.Add(monster);
        return true;
    }

    bool Gain(double now)
    {
        var change = state.GainQualified(now);
        if (!change.Accepted) return false;
        foreach (int tier in change.CrossedThresholds) pendingTiers.Enqueue(tier);
        return true;
    }

    public void Clear()
    {
        state.Clear(); pendingTiers.Clear();
        // Keep credited kills across source changes/expiry: delayed notices cannot grant them again.
    }
}
