using GameServer.Core.World;

partial class Program
{
    internal static void ApplyPlayerAilmentsLocked(ClientState conn, DamagePacket baseHit, DamagePacket taken,
        bool critical, bool blocked, double now, IReadOnlyDictionary<uint, int> stats)
    {
        if (blocked || conn.Closed || conn.LeavingArea || conn.PlayerLife == 0 || InGracePeriod(conn)
            || !baseHit.Components.Any(c => DamageTypes.IsElemental(c.Type))) return;
        int Get(string name) => Stats.Key(name) is uint key ? stats.GetValueOrDefault(key) : 0;
        double previousAction = conn.ElementalAilments.ActionFactor(now);
        bool frozen = previousAction == 0;
        // Ailment damage is a separate ordinary roll from the pre-hit range. Hit luck and the hit's critical
        // multiplier cannot alter its distribution; non-damaging ailments still use the actual mitigated hit.
        int fire = critical ? baseHit[DamageType.Fire].Roll(conn.IncomingHitRandom) : 0;
        var ailmentBase = DamagePacket.Of(DamageType.Fire, fire, fire);
        conn.ElementalAilments.Apply(ailmentBase, taken, critical, conn.PlayerMaxLife, now, Get, conn.IncomingHitRandom);
        if (!frozen && conn.ElementalAilments.ActionFactor(now) == 0)
        {
            conn.FreezeGeneration++;
            Interlocked.Increment(ref conn.ActionCount);
            conn.ChannelSkill = 0;
            var at = conn.Walk.Position(now); conn.Walk.Teleport(at.X, at.Y, now);
        }
        if (previousAction != conn.ElementalAilments.ActionFactor(now)) conn.ApplyMovementSpeed();
    }

    internal static int AdvancePlayerAilmentsLocked(ClientState conn, double from, double until,
        IReadOnlyDictionary<uint, int> stats)
    {
        if (conn.Closed || conn.LeavingArea || conn.PlayerLife == 0) { conn.ElementalAilments.Clear(); return 0; }
        if (!conn.ElementalAilments.HasEffects) return 0;
        double previousAction = conn.ElementalAilments.ActionFactor(from);
        int Get(string name) => Stats.Key(name) is uint key ? stats.GetValueOrDefault(key) : 0;
        int damage = conn.ElementalAilments.Advance(from, until, Get, Resistance(conn, DamageType.Fire),
            MaxResistance(conn, DamageType.Fire), PlayerImmunities(conn).Contains(DamageType.Fire));
        int lifeDamage = conn.EnergyShield.Damage(damage, false, until);
        conn.PlayerLife = (uint)Math.Max(0, (long)conn.PlayerLife - lifeDamage);
        if (conn.PlayerLife == 0)
        {
            conn.InvalidateIncomingHits();
            // Commit the mode outcome inside lethal damage, before disconnects or network writes can intervene.
            RecordHardcoreDeath(conn);
        }
        if (previousAction != conn.ElementalAilments.ActionFactor(until)) conn.ApplyMovementSpeed();
        return damage;
    }

    internal static bool PlayerActionBlocked(ClientState conn, double requestedAt, double now) =>
        conn.HitRecovery.BlocksInput(requestedAt, now) || conn.ElementalAilments.ActionFactor(now) == 0;

    /// <summary>The signed <c>action_speed_-%</c> from the character's raw sources and dev overrides: positive is slower.
    /// The client's own action-speed bucket, and the planned ActionSpeed/EffectClock contract (#246), keep it separate
    /// from attack/cast/movement increases so it is applied once.</summary>
    internal static int ActionSpeedReduction(ClientState conn)
    {
        if (Stats.Key("action_speed_-%") is not uint key) return 0;
        lock (conn.Monsters) if (conn.PlayerStats.TryGetValue(key, out int dev)) return dev;
        return CharacterStatSources(conn).Stats.GetValueOrDefault(key);
    }

    internal static double PlayerActionFactor(ClientState conn) =>
        Math.Max(.01, conn.ElementalAilments.TotalActionFactor(conn.Clock.Elapsed.TotalSeconds,
            ActionSpeedReduction(conn)));
}
