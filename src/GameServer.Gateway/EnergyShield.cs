/// <summary>Ordinary ES: non-chaos damage is absorbed first; damage restarts the two-second recharge delay.
/// Character.ot supplies 2000%/minute recharge (one third of maximum per second). Caller supplies game time.</summary>
sealed class EnergyShield
{
    public uint Current { get; private set; }
    public uint Maximum { get; private set; }
    public bool Full => Maximum > 0 && Current >= Maximum;
    public bool Recharging { get; private set; }
    double interruptedAt = double.NegativeInfinity, fraction;
    public void SetMaximum(uint maximum, bool refill = false)
    {
        Maximum = maximum;
        Current = refill ? maximum : Math.Min(Current, maximum);
        if (Current == Maximum) { fraction = 0; Recharging = false; }
    }
    public int Damage(int amount, bool bypass, double now)
    {
        if (amount <= 0) return 0;
        interruptedAt = now;
        Recharging = false;
        if (bypass) return amount;
        uint absorbed = Math.Min(Current, (uint)amount);
        Current -= absorbed;
        return amount - (int)absorbed;
    }
    public void Empty() { Current = 0; fraction = 0; Recharging = false; }
    /// <summary>Instant recovery (Kiloava's Leadership on block), up to the maximum.</summary>
    public void Recover(uint amount)
    {
        Current = (uint)Math.Min(Maximum, (long)Current + amount);
        if (Current == Maximum) { fraction = 0; Recharging = false; }
    }
    // Leech/gain does not start recharge or reset its interruption timer.
    public void Gain(double amount)
    {
        if (!double.IsFinite(amount) || amount <= 0) return;
        Current = RecoveryPool.Advance(Current, Maximum, amount, ref fraction);
        if (Current == Maximum) Recharging = false;
    }
    public static uint Max(int flat, int increased) =>
        (uint)Math.Clamp(((long)Math.Max(0, flat) * Math.Max(0L, 100L + increased) + 50) / 100, 0, int.MaxValue);
    public static double Delay(Func<string, int> get) => 2 / Math.Max(0.01, 1 + get("energy_shield_delay_-%") / 100.0);
    public static double RechargePerMinute(uint max, Func<string, int> get) =>
        get("cannot_recharge_energy_shield") != 0 || get("no_energy_shield_recharge_or_regeneration") != 0
            || get("keystone_ghost_reaver") != 0 ? 0 :
        max * 20.0 * Math.Max(0, 1 + get("energy_shield_recharge_rate_+%") / 100.0)
        * Math.Max(0, 1 + get("energy_shield_recovery_rate_+%") / 100.0);
    public static double RegenPerMinute(uint max, Func<string, int> get) =>
        get("no_energy_shield_recharge_or_regeneration") != 0 ? 0 :
        Math.Max(0, get("base_energy_shield_regeneration_rate_per_minute") + max * get("base_energy_shield_regeneration_rate_per_minute_%") / 100.0)
        * Math.Max(0, 1 + get("energy_shield_regeneration_rate_+%") / 100.0)
        * Math.Max(0, 1 + get("energy_shield_recovery_rate_+%") / 100.0);
    // The ordinary 0x1b3 virtual recharge-active stat does not start the client's recharge predictor. Feed its
    // verified native recovery input during the recharge phase instead; keep the recharge-rate stat for the panel.
    public static double PredictionPerMinute(uint max, Func<string,int> get, bool recharging) =>
        RegenPerMinute(max,get) + (recharging ? RechargePerMinute(max,get) : 0);
    public void Advance(double from, double to, Func<string, int> get, bool alive)
    {
        if (!alive) { Empty(); return; }
        if (to <= from || Current >= Maximum) { Recharging = false; return; }
        double start = interruptedAt + Delay(get);
        double recharge = RechargePerMinute(Maximum, get);
        Recharging = to >= start && recharge > 0;
        double rechargeSeconds = Math.Max(0, to - Math.Max(from, start));
        double change = fraction + RegenPerMinute(Maximum, get) * (to - from) / 60 + recharge * rechargeSeconds / 60;
        uint whole = (uint)Math.Min(uint.MaxValue, Math.Floor(change));
        Current = (uint)Math.Min(Maximum, (long)Current + whole);
        fraction = Current == Maximum ? 0 : change - whole;
        if (Current == Maximum) Recharging = false;
    }
}
