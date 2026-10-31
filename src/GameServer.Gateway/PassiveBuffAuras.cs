using System.Text.Json;
using GameServer.Core.Protocol.Instance;
using DamageConversion = GameServer.Core.World.DamageConversion;

/// <summary>
/// Buffs a passive applies (Agents/docs/protocol/11-skills.md §10e): PassiveSkills.dat <c>PassiveSkillBuffs</c> →
/// BuffTemplates → BuffDefinitions. Ruthless reads <c>BuffsHardmode</c> only for a row with HasHardmodeAlternate, as
/// PassiveTree does for the stats: every other row's BuffsHardmode is either a copy or empty (Elemental Valiance,
/// Unwavering Crusade, Heartstopper). A template's <c>Buff_StatValues</c> are the values of its definition's
/// <c>GrantedStats</c>, in order (Stats row + 1 is the client key); <c>AuraRadius</c> is the "nearby" radius in cells
/// (60: 6 m; 0 is a buff on the player himself); <c>__col4</c> lists who gets it, read as 1 the player, 2 allies,
/// 3 enemies (every Augury of Penitence, Occultist and Saboteur enemy aura has [3]; not confirmed in the client).
/// One table, three views, each limited to the templates whose stats have a consumer:
/// <list type="bullet">
/// <item>enemy auras (<see cref="EnemyAurasFor"/>, Program.EnemyAuraActs): Light of Divinity's elemental damage taken,
/// Born under Fire's hit conversion and the <c>SaboteurBlind</c> blind (Devout of Solaris, Born in the Shadows: a
/// stat-less generic_buff_aura, shown on monsters as BuffDefinitions <c>blinded</c>). Program.EnemyDebuffsTick puts
/// them on every living hostile monster within their radius of the living player; hits read them.</item>
/// <item>player-and-ally auras (<see cref="AllyAurasFor"/>): the relic trigger (Program.TriggerElementalRelicLocked).</item>
/// <item>self buffs (<see cref="SelfBuffsFor"/>): permanent and unconditional (AuraRadius 0); their stats join the
/// passives' sum (CharacterStatSources) and the buff is published on the player (<see cref="Presentation"/>). The other
/// radius-0 templates carry their own timing or condition (Trickster's alternating damage taken, Occultist's wither
/// pulse, Chieftain's while stationary) and are not applied.</item>
/// </list>
/// </summary>
static class PassiveBuffAuras
{
    /// <summary>A passive's buff: its BuffTemplates row and Id, BuffDefinitions row, radius (cells, 0 on the player),
    /// whom it affects (<c>__col4</c>) and its stats.</summary>
    public sealed record Aura(int Row, string Template, ushort Definition, int Radius, IReadOnlyList<int> Targets,
        IReadOnlyList<(uint Key, int Value)> Stats)
    {
        public ushort BuffRow => Definition;
        public bool Enemies => Targets.Contains(EnemiesTarget);
    }

    const int PlayerTarget = 1, AlliesTarget = 2, EnemiesTarget = 3;

    // Enemy auras applied by name: their stats have consumers (MonsterTakesMitigated reads elemental_damage_taken_+%).
    // A template whose every stat is a hit conversion acts too (Program.EnemyAuraActs).
    static readonly HashSet<string> EnemyAuraTemplates =
    [
        "CustomAscendancyNearbyEnemiesIncreasedElementalDamageTaken", // Paladin Light of Divinity: 2551 Augury of Penitence
    ];

    /// <summary>The passive blind aura: every enemy in it is Blinded (Program.Blinded).</summary>
    internal const string BlindTemplate = "SaboteurBlind";

    // On the player and his allies within the radius.
    static readonly HashSet<string> AllyAuraTemplates =
    [
        // Paladin Elemental Valiance and the Guardian's Unwavering Crusade: 2258 trigger_allied_guardian_relic_on_kill
        "GuardianAlliesSpawnRelicsForYouOnKill",
    ];

    // Permanent, unconditional and on the player itself (AuraRadius 0, CancelOnDeath false).
    static readonly HashSet<string> SelfBuffTemplates =
    [
        "CustomAscendancyIllegalFisherman", // Surfcaster Ghost of the Deep: BuffDefinitions 2550 Torment Touched
    ];

    static readonly Lazy<Dictionary<ushort, Aura[]>> normal = new(() => Load("data", ruthless: false));
    static readonly Lazy<Dictionary<ushort, Aura[]>> ruthlessAuras = new(() => Load("data", ruthless: true));

    /// <summary>Every buff of these nodes (PassiveSkillGraphIds), one per template however many nodes grant it.</summary>
    public static IReadOnlyList<Aura> For(IEnumerable<ushort> nodes, bool ruthless = false) =>
        [.. nodes.SelectMany(n => (ruthless ? ruthlessAuras : normal).Value.GetValueOrDefault(n) ?? []).DistinctBy(a => a.Template)];

    /// <summary>The enemy auras of these nodes the server applies.</summary>
    internal static IReadOnlyList<Aura> EnemyAurasFor(IEnumerable<ushort> nodes, bool ruthless) =>
        [.. For(nodes, ruthless).Where(Program.EnemyAuraActs)];

    /// <summary>Whether this enemy aura is applied by name (not by its stats).</summary>
    internal static bool AppliedByName(Aura aura) => aura.Template == BlindTemplate || EnemyAuraTemplates.Contains(aura.Template);

    /// <summary>The player-and-ally auras of these nodes the server applies.</summary>
    internal static IReadOnlyList<Aura> AllyAurasFor(IEnumerable<ushort> nodes, bool ruthless) =>
        [.. For(nodes, ruthless).Where(a => a.Radius > 0 && AllyAuraTemplates.Contains(a.Template)
            && a.Targets.SequenceEqual([PlayerTarget, AlliesTarget]))];

    /// <summary>The self buffs of these nodes the server applies.</summary>
    internal static IReadOnlyList<Aura> SelfBuffsFor(IEnumerable<ushort> nodes, bool ruthless) =>
        [.. For(nodes, ruthless).Where(a => a.Radius == 0 && SelfBuffTemplates.Contains(a.Template))];

    /// <summary>The largest blind radius of these auras, or null without one.</summary>
    public static int? BlindRadius(IEnumerable<Aura> auras) =>
        auras.Where(a => a.Template == BlindTemplate && a.Enemies && a.Radius > 0).Select(a => (int?)a.Radius).Max();

    internal static Dictionary<uint, int> WithSelfBuffs(Dictionary<uint, int> stats, IEnumerable<ushort> nodes, bool ruthless)
    {
        foreach (var buff in SelfBuffsFor(nodes, ruthless))
            foreach (var (key, value) in buff.Stats)
                stats[key] = (int)Math.Clamp((long)stats.GetValueOrDefault(key) + value, int.MinValue, int.MaxValue);
        return stats;
    }

    /// <summary>The 0x1c1/0x1c2 that bring the player's published self buffs to the allocated ones, or null. Run at the
    /// send gate; a new area's connection starts with none, so area entry publishes them again.</summary>
    internal static byte[]? Presentation(ClientState conn)
    {
        lock (conn.Monsters)
        {
            if (conn.Closed || conn.LeavingArea || !conn.PlayerSent) return null;
            var wanted = SelfBuffsFor([.. conn.Passives, .. conn.MainAscendancy.Allocated], conn.Ruthless)
                .Select(b => b.Definition).ToHashSet();
            var bytes = new List<byte>();
            foreach (var (definition, instance) in conn.PassiveBuffs.Where(b => !wanted.Contains(b.Key)).ToArray())
            {
                bytes.AddRange(InstanceEntities.RemoveBuff(Program.PlayerId, instance));
                conn.PassiveBuffs.Remove(definition);
            }
            foreach (ushort definition in wanted.Where(d => !conn.PassiveBuffs.ContainsKey(d)))
                bytes.AddRange(InstanceEntities.AddBuff(Program.PlayerId, conn.PassiveBuffs[definition] = conn.NextBuff++,
                    definition, Program.PlayerId));
            return bytes.Count == 0 ? null : [.. bytes];
        }
    }

    static Dictionary<ushort, Aura[]> Load(string folder, bool ruthless)
    {
        string Table(string name) => Path.Combine(folder, "tables", $"{name}.json");
        var result = new Dictionary<ushort, Aura[]>();
        if (new[] { "PassiveSkills", "BuffTemplates", "BuffDefinitions" }.Any(t => !File.Exists(Table(t)))) return result;
        using var passives = JsonDocument.Parse(File.ReadAllBytes(Table("PassiveSkills")));
        using var templates = JsonDocument.Parse(File.ReadAllBytes(Table("BuffTemplates")));
        using var definitions = JsonDocument.Parse(File.ReadAllBytes(Table("BuffDefinitions")));
        var cache = new Dictionary<int, Aura?>();
        Aura? Template(int row)
        {
            if (cache.TryGetValue(row, out var known)) return known;
            Aura? aura = null;
            if (row >= 0 && row < templates.RootElement.GetArrayLength()
                && templates.RootElement[row] is var t && t.GetProperty("Id").GetString() is { } id
                && t.GetProperty("BuffDefinitionsKey") is { ValueKind: JsonValueKind.Number } key
                && key.GetInt32() is >= 0 and <= ushort.MaxValue and var definition && definition < definitions.RootElement.GetArrayLength())
            {
                var granted = definitions.RootElement[definition].GetProperty("GrantedStats").EnumerateArray().Select(s => s.GetInt32()).ToList();
                var values = t.GetProperty("Buff_StatValues").EnumerateArray().Select(v => v.GetInt32()).ToList();
                // A template whose values do not line up with its definition's stats is left out rather than misapplied.
                if (granted.Count == values.Count)
                    aura = new(row, id, (ushort)definition, t.GetProperty("AuraRadius").GetInt32(),
                        [.. t.GetProperty("__col4").EnumerateArray().Select(v => v.GetInt32())],
                        [.. granted.Zip(values, (stat, value) => ((uint)(stat + 1), value))]);
            }
            return cache[row] = aura;
        }
        foreach (var row in passives.RootElement.EnumerateArray())
        {
            if (row.GetProperty("PassiveSkillGraphId").GetInt32() is not (> 0 and <= ushort.MaxValue and var node)) continue;
            bool alternate = ruthless && row.TryGetProperty("HasHardmodeAlternate", out var hardmode) && hardmode.GetBoolean();
            if (!row.TryGetProperty(alternate ? "BuffsHardmode" : "PassiveSkillBuffs", out var buffs)
                || buffs.ValueKind != JsonValueKind.Array) continue;
            if (buffs.EnumerateArray().Select(b => Template(b.GetInt32())).OfType<Aura>().ToArray() is { Length: > 0 } auras)
                result.TryAdd((ushort)node, auras);
        }
        return result;
    }

    /// <summary>One connection's monsters blinded by the player's passive blind aura, with the ids of the buffs that show
    /// it. Its own lock, so a hit can ask without the Monsters lock; the Monsters lock may be held when it is taken, never
    /// the other way round.</summary>
    public sealed class BlindState
    {
        readonly Dictionary<uint, ushort> _blinded = [];
        readonly Lock _lock = new();

        public bool Blinded(uint monster)
        {
            lock (_lock) return _blinded.ContainsKey(monster);
        }

        /// <summary>The buffs to add and to remove so that exactly <paramref name="inside"/> are blinded. A monster that
        /// is gone (<paramref name="exists"/> false) is forgotten without a message. Call under the Monsters lock, which
        /// <paramref name="next"/> needs.</summary>
        public List<(uint Monster, ushort Buff, bool Added)> Reconcile(IReadOnlySet<uint> inside, Func<uint, bool> exists, Func<ushort> next)
        {
            lock (_lock)
            {
                var changes = new List<(uint, ushort, bool)>();
                foreach (var (id, buff) in _blinded.Where(kv => !inside.Contains(kv.Key)).ToList())
                {
                    _blinded.Remove(id);
                    if (exists(id)) changes.Add((id, buff, false));
                }
                foreach (uint id in inside.Where(id => !_blinded.ContainsKey(id)))
                    changes.Add((id, _blinded[id] = next(), true));
                return changes;
            }
        }
    }
}

partial class Program
{
    // The conversion the player's passive enemy auras give this monster's hit when it stands within their radius of the
    // player at contact (Born under Fire: base_physical_damage_%_to_convert_to_fire), by the player's own conversion rules
    // (DamageStats.Conversion); null when none applies. Call under the Monsters lock.
    internal static DamageConversion? EnemyAuraConversionLocked(ClientState conn, SpawnedMonster m, (int X, int Y) player, double now)
    {
        if (!m.Hostile || conn.PlayerLife == 0) return null;
        var at = m.Walk.Position(now);
        var stats = new Dictionary<uint, int>();
        foreach (var aura in PassiveBuffAuras.EnemyAurasFor([.. conn.Passives, .. conn.MainAscendancy.Allocated], conn.Ruthless))
            if (aura.Stats.Count > 0 && Dist(at, player) <= aura.Radius)
                foreach (var (stat, value) in aura.Stats) stats[stat] = stats.GetValueOrDefault(stat) + value;
        var conversion = DamageStats.Conversion(stats, Stats, attack: true);
        return conversion.IsEmpty ? null : conversion;
    }

    // Whether the server applies this enemy aura: by name (Light of Divinity, the blind) or because every stat it has is a
    // hit conversion (Born under Fire). The others' buffs would show effects that do not happen, so they are not sent
    // until their stats have consumers.
    internal static bool EnemyAuraActs(PassiveBuffAuras.Aura aura) => aura.Enemies && aura.Radius > 0
        && (PassiveBuffAuras.AppliedByName(aura) || aura.Stats.Count > 0 && aura.Stats.All(s =>
            !DamageStats.Conversion(new Dictionary<uint, int> { [s.Key] = s.Value }, Stats, attack: true).IsEmpty));
}
