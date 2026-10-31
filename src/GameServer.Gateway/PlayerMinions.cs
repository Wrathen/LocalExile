using GameServer.Core.Protocol.Instance;
using DamageType = GameServer.Core.World.DamageType;

/// <summary>
/// The monsters the player owns (minions): Summon Elemental Relic's relics (Program.PlayerMinions.cs) and the Spirit
/// chieftains (SpiritChieftains.cs). A minion is
/// a monster on his team (Positioned team 0) that the client knows as his by its own stats is_player_minion and
/// is_semantic_minion (the link-target filter 0x141bf8490 reads 0x48a and 0x3cf8). It follows him (the monster AI walks
/// friendly monsters into the player's surround; one with a teleport jumps back to him from beyond its aggro range),
/// may fight his enemies (Program.MinionAttacks), lives until its duration ends (no monster attacks a minion), may cast an
/// aura on him while he is within its radius and may explode when it ends; it leaves no corpse. It is never kept by the instance, goes with the
/// connection (area change, logout) and dies with him. Guarded by the connection's Monsters lock.
/// </summary>
sealed class PlayerMinions
{
    /// <summary>One minion: its object id and monster, the skill that made it (an effect id), its kind (a relic's element),
    /// until when it lives (game seconds), its aura on the owner and its death explosion.</summary>
    internal sealed class Minion(uint id, SpawnedMonster monster, string skill, string kind)
    {
        public uint Id => id;
        public SpawnedMonster Monster => monster;
        public string Skill => skill;
        public string Kind => kind;
        public double Until;
        public Aura? OwnerAura { get; init; }
        public Explosion? Explodes { get; init; }
        /// <summary>It has a teleport to its owner (the relics' RelicTeleport).</summary>
        public bool Teleports { get; init; }
        /// <summary>The aura's received buff on the owner, 0 while it is not on him.</summary>
        public ushort OwnerBuff;
        /// <summary>It attacks the player's enemies near it (Program.MinionAttacks).</summary>
        public bool Fights { get; init; }
    }

    /// <summary>An aura a minion casts on its owner: the skill, its BuffDefinitions row, its radius (cells) and its stats.
    /// The owner's aura effect does not scale it: it is the minion's skill, not his.</summary>
    internal sealed record Aura(string EffectId, ushort BuffRow, int Radius, IReadOnlyList<(uint Key, int Value)> Stats);

    /// <summary>A death explosion: this percent of the minion's maximum life as this type of damage to the hostile monsters
    /// within the radius (cells).</summary>
    internal sealed record Explosion(DamageType Type, int LifePct, int Radius);

    readonly Dictionary<uint, Minion> minions = [];

    public IReadOnlyCollection<Minion> All => minions.Values;
    public bool Owns(uint id) => minions.ContainsKey(id);
    public bool Fights(uint id) => minions.TryGetValue(id, out var m) && m.Fights;
    public void Add(Minion minion) => minions[minion.Id] = minion;
    public void Remove(uint id) => minions.Remove(id);

    /// <summary>Ended minions' bodies still in the area and when they go (Program.PlayerMinionsTick).</summary>
    public readonly List<(uint Id, double At)> Bodies = [];

    /// <summary>Relic summons that triggers queued (Program.TriggerElementalRelicLocked); the next minion step makes them.</summary>
    public int PendingRelics;

    /// <summary>The trigger and element rolls (Next(int) only; the tests script it).</summary>
    public Random Random = Random.Shared;

    /// <summary>
    /// The stats an aura skill grants when a minion of this level casts it: between the two gem levels whose
    /// PlayerLevelReq brackets the minion's level, in proportion, rounded down. A level-85 relic's Anger 182–259, Hatred
    /// 43% and Wrath 26–415 with 23% (GGG, forum thread 3408278, quoted by the wiki) are levels 27 (84) and 28 (86) halfway;
    /// Path of Building takes the lower level. Below the first requirement the level-1 stats.
    /// </summary>
    internal static IReadOnlyList<(uint Key, int Value)> AuraStatsAt(SkillCatalog skills, StatCatalog stats, int effect,
        string effectId, int minionLevel)
    {
        int low = skills.MaxLevelAt(effect, minionLevel);
        int lowReq = skills.PlayerLevelReq(effect, low), highReq = skills.PlayerLevelReq(effect, low + 1);
        double share = highReq > lowReq ? Math.Clamp((minionLevel - lowReq) / (double)(highReq - lowReq), 0, 1) : 0;
        Dictionary<uint, int> At(int level) => AuraCatalog.RawStats([new(0, effect, effectId, effectId, 0, 0, 0, 0, level)], 0,
            skills, stats).ToDictionary(s => s.Key, s => s.Value);
        var (a, b) = (At(low), At(low + 1));
        return [.. a.Keys.Union(b.Keys).Select(k =>
            (k, (int)Math.Floor(a.GetValueOrDefault(k) + (b.GetValueOrDefault(k) - a.GetValueOrDefault(k)) * share)))];
    }
}

sealed partial class ClientState
{
    /// <summary>The player's minions (PlayerMinions), under the Monsters lock.</summary>
    public readonly PlayerMinions Minions = new();
}

partial class Program
{
    /// <summary>The monsters the instance keeps when the connection leaves (#12): all but the player's minions. Under the
    /// Monsters lock.</summary>
    internal static IEnumerable<SpawnedMonster> InstanceMonsters(ClientState conn) =>
        conn.Monsters.Where(kv => !conn.Minions.Owns(kv.Key)).Select(kv => kv.Value);

    /// <summary>A minion leaves no corpse: its body goes (0x2bb, into <paramref name="bytes"/>) a second after its death,
    /// once the death has played. Under the Monsters lock; every minion tick calls it.</summary>
    internal static void RemoveMinionBodiesLocked(ClientState conn, double now, List<byte> bytes)
    {
        foreach (var body in conn.Minions.Bodies.Where(b => now >= b.At).ToArray())
        {
            conn.Minions.Bodies.Remove(body);
            if (!conn.Monsters.TryGetValue(body.Id, out var dead) || !dead.Dead) continue;
            conn.Monsters.Remove(body.Id);
            bytes.AddRange(InstanceProtocol.RemoveObject(body.Id));
        }
    }

    /// <summary>At disconnect: the instance keeps its monsters as they are now, for the next connection (#12); the
    /// player's minions go with him.</summary>
    internal static void KeepInstanceMonsters(ClientState left)
    {
        lock (left.Monsters) left.Instance.Monsters = SavedMonster.Save(InstanceMonsters(left),
            left.Clock.Elapsed.TotalSeconds, MonsterEmerge.Unrisen(left));
    }
}
