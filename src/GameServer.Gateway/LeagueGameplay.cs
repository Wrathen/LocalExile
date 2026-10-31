static class LeagueGameplay
{
    public static bool CanGainUtilityCharges(bool restricted, bool active, bool exception) => !restricted || !active || exception;

    // Keep fractional XP so small campaign kills still eventually advance gems at a reduced rate.
    public static int GemExperience(uint monsterExperience, double multiplier, ref double carry)
    {
        double total = Math.Round(carry + monsterExperience / 10.0 * multiplier, 9);
        int whole = (int)Math.Clamp(Math.Floor(total), 0, int.MaxValue);
        carry = total - whole;
        return whole;
    }

    public static uint BossLifeAfterDeath(uint life, uint maximum, double recoveredFraction) =>
        life == 0 ? 0 : (uint)Math.Min(maximum, life + Math.Ceiling(maximum * recoveredFraction));
}
