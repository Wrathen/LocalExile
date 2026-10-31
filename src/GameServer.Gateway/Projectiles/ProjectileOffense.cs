using GameServer.Core.World;

/// <summary>Frozen offensive inputs. Targets, including evasion and resistance, are resolved at contact.</summary>
sealed record ProjectileOffense(DamagePacket Hit, bool Spell, int Accuracy, int CriticalChance,
    int CriticalMultiplier, string Label)
{
    public DamagePacket? AreaHit { get; init; }
    public Penetration.PerType Penetration { get; init; }
    public HitResistances.Mods ResistanceMods { get; init; }
    public int ImpaleChance { get; init; }
    /// <summary>The ignite its hits can inflict, captured at release (MonsterIgnites); the default ignites nothing.</summary>
    public MonsterIgnites.Settings Ignite { get; init; }
    public ImpaleStats.Settings? ImpaleSettings { get; init; }
    public ImpaleStats.Settings? CriticalImpaleSettings { get; init; }
    public AttackAccuracyProfile? AccuracyProfile { get; init; }
    public SkillCriticalRoll? CriticalRoll { get; init; }
    public LeechSources Leech { get; init; } = new(DamagePacket.Empty, DamagePacket.Empty, DamagePacket.Empty);
    public int Effect { get; init; } = -1;
    public int Level { get; init; } = 1;
    public int Quality { get; init; }
    public int AccuracyMoreUnique { get; init; }
    public int AccuracyMoreClose { get; init; }
    public bool AlwaysHits { get; init; }
    public bool FarShot { get; init; }
    public double ReturnMultiplier { get; init; } = 1;
    public int? SecondFormCriticalChance { get; init; }
    /// <summary>The chance against a target the player has Blinded (Program.CriticalChanceOn), or null.</summary>
    public int? VsBlindedCriticalChance { get; init; }
    public string? AreaVisual { get; init; }
    public string? FlightVisual { get; init; }
    /// <summary>The client spawns this use's primary projectiles itself (its own skill use), so the server sends no object
    /// for them; continuation children still get one.</summary>
    public bool ClientPredicted { get; init; }
    public Program.ProjectilePresentation? Presentation { get; init; }
    /// <summary>Send S→C 0x1af on each contact (the target's blood and particles), as the melee default attack does.
    /// <see cref="ActionSequence"/> is the shot's action, so the client matches the package to it (kind 3,
    /// action-hit-packet-3.29.3b.md §3); without one the fabricated 0xffff still plays immediately.</summary>
    public bool HitPacket { get; init; }
    public ushort? ActionSequence { get; init; }
    public IReadOnlyDictionary<uint, int> SkillStats { get; init; } = new Dictionary<uint, int>();
}
