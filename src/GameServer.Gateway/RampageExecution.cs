/// <summary>Explicit LocalExile v1 scheduling of the target's thirteen tier plans. Metadata supplies damage,
/// summon counts, duration, repeat counts and radius changes. Repeat channels are independent, never multiplied.</summary>
static class RampageExecution
{
    public const double RippleInterval = 0.1;
    public const double RepeatInterval = 0.25;
    public const int NearbyRadius = 40;
    public const int CorpseRadius = 20;
    public enum StepKind { Pulse, Weapons, Corpses, Projectiles }
    public readonly record struct Step(double Delay, StepKind Kind, int RadiusPercent = 100);

    public static IReadOnlyList<Step> Steps(RampageCatalog.Tier tier, int level)
    {
        var own = tier.Skill.AtLevel(level) ?? throw new InvalidOperationException("Missing Rampage skill level");
        if (tier.Summons.Count > 0) return [new(0, StepKind.Weapons)];
        if (tier.Effect == RampageCatalog.Kind.CorpseExplosions) return [new(0, StepKind.Corpses)];
        bool projectile = own.Get("base_is_projectile") != 0 || tier.Effect == RampageCatalog.Kind.StunNova;
        var result = new List<Step> { new(0, projectile ? StepKind.Projectiles : StepKind.Pulse) };
        for (int i = 1; i <= own.Get("ice_nova_number_of_repeats"); i++)
            result.Add(new(i * RippleInterval, StepKind.Pulse,
                Math.Max(0, 100 + i * own.Get("ice_nova_radius_+%_per_repeat"))));
        for (int i = 1; i <= own.Get("skill_repeat_count"); i++)
            result.Add(new(i * RepeatInterval, projectile ? StepKind.Projectiles : StepKind.Pulse));
        return result.OrderBy(s => s.Delay).ToArray();
    }
}

sealed class RampageWorld
{
    public sealed record Job(double At, long Epoch, uint Actor, RampageCatalog.Tier Tier,
        RampageExecution.Step Step, (int X, int Y) Origin, int Level);
    public sealed record Actor(uint Id, long Epoch, RampageCatalog.WeaponKind? Weapon, int Level,
        double ExpiresAt, double NextActionAt, int Effect);
    public long Epoch { get; private set; }
    public bool Enabled { get; private set; }
    public readonly List<Job> Jobs = [];
    public readonly Dictionary<uint, Actor> Actors = [];
    public readonly HashSet<SpawnedMonster> ConsumedCorpses = new(ReferenceEqualityComparer.Instance);
    public readonly HashSet<uint> PendingRemoval = [];
    public readonly Dictionary<uint, (double Until, ushort Buff)> Blinds = [];
    public readonly Dictionary<uint, double> SentBlinds = [];
    public sealed record Strike(double At, long Epoch, uint ActorId, SpawnedMonster Actor,
        uint TargetId, SpawnedMonster Target, GameServer.Core.World.DamagePacket Damage, bool Area, int Accuracy);
    public readonly List<Strike> Strikes = [];
    public readonly HashSet<uint> Exploded = [];
    public void Enable() { if (!Enabled) { Enabled = true; Epoch++; } }
    public void Disable()
    {
        if (!Enabled) return;
        Enabled = false; Epoch++; Jobs.Clear(); Strikes.Clear(); ConsumedCorpses.Clear(); Exploded.Clear();
        foreach (uint id in Actors.Keys) PendingRemoval.Add(id);
        Actors.Clear();
    }
    public bool Current(long epoch) => Enabled && epoch == Epoch;
}
