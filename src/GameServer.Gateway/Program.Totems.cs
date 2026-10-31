using System.Net.Sockets;
using System.Text.Json;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

/// <summary>
/// The player's summoned totems (Agents/docs/protocol/11-skills.md section 10g): team-0 monster objects of the skill's
/// SkillTotemVariations variety, kept here rather than in <see cref="ClientState.Monsters"/>, so area hits, monster AI,
/// loot, experience and the instance save never see them. A totem dies when its Life reaches 0, its duration ends, a new
/// one replaces it past the limit, or the player dies (wiki Totem, Soul Mantle); it then plays its death (0x1b1 at 0) and
/// is removed (0x2bb) <see cref="RemoveSeconds"/> later. An area change starts a new connection without them. Guarded by
/// the Monsters lock; times are the connection's game seconds.
/// </summary>
sealed class TotemState
{
    /// <summary>The death animation's time before the object goes.</summary>
    public const double RemoveSeconds = 1;

    /// <summary>A totem's aura on the player within <paramref name="Radius"/> cells: a BuffDefinitions row and its stats from
    /// the totem's skill, before aura effect.</summary>
    public sealed record Aura(ushort Row, int Radius, IReadOnlyList<(uint Stat, int Value)> Stats);

    public sealed class Totem(uint id, ushort skillId, (int X, int Y) at, uint maxLife, double until, Aura? aura)
    {
        public uint Id => id;
        public ushort SkillId => skillId;
        public (int X, int Y) At => at;
        public uint MaxLife { get; } = maxLife;
        public double Until { get; set; } = until;
        public Aura? Aura => aura;
        public uint Life = maxLife;
        /// <summary>When it died; null while it lives.</summary>
        public double? DiedAt;
        public uint SentLife = maxLife;
        public bool SentDeath;
        public bool Alive(double now) => DiedAt is null && Life > 0 && now < Until;
    }

    readonly List<Totem> _totems = [];
    /// <summary>The aura buff the client was last sent: its source totem and instance (Program.TotemPresentation).</summary>
    public uint SentAuraTotem;
    public ushort SentAuraInstance;

    public IReadOnlyList<Totem> All => _totems;
    public IEnumerable<Totem> Living(double now) => _totems.Where(t => t.Alive(now));

    /// <summary>Adds a totem; the oldest living ones die until at most <paramref name="limit"/> live. The ones it replaced.</summary>
    public List<Totem> Place(Totem totem, int limit, double now)
    {
        var replaced = new List<Totem>();
        foreach (var old in Living(now).ToList())
            if (Living(now).Count() >= limit) { old.DiedAt = now; replaced.Add(old); }
        _totems.Add(totem);
        return replaced;
    }

    /// <summary>The living totem nearest to a point (the earliest placed of equally near ones).</summary>
    public Totem? Nearest((int X, int Y) at, double now) => Living(now)
        .OrderBy(t => (long)(t.At.X - at.X) * (t.At.X - at.X) + (long)(t.At.Y - at.Y) * (t.At.Y - at.Y)).FirstOrDefault();

    /// <summary>The strongest living totem aura covering a point.</summary>
    public Totem? AuraAt((int X, int Y) at, double now) => Living(now)
        .Where(t => t.Aura is { } a && (long)(t.At.X - at.X) * (t.At.X - at.X) + (long)(t.At.Y - at.Y) * (t.At.Y - at.Y) <= (long)a.Radius * a.Radius)
        .OrderByDescending(t => t.Aura!.Stats.Sum(s => (long)s.Value)).FirstOrDefault();

    /// <summary>The totems whose duration ended and that had not died yet: they die now.</summary>
    public List<Totem> Expire(double now)
    {
        var expired = _totems.Where(t => t.DiedAt is null && now >= t.Until).ToList();
        foreach (var t in expired) t.DiedAt = now;
        return expired;
    }

    /// <summary>Every living totem dies (the player died).</summary>
    public void KillAll(double now)
    {
        foreach (var t in _totems.Where(t => t.DiedAt is null)) t.DiedAt = now;
    }

    /// <summary>The dead totems whose death animation is over leave.</summary>
    public List<Totem> Remove(double now)
    {
        var gone = _totems.Where(t => t.DiedAt is double died && now - died >= RemoveSeconds).ToList();
        _totems.RemoveAll(gone.Contains);
        return gone;
    }

    /// <summary>"X% of Damage from Hits is taken from your nearest Totem's Life before you" for one part of a hit (PoB
    /// reducePoolsByDamage: the share, capped by the totem's Life): what the player still takes and the totem's Life left.
    /// The totem's share is rounded down.</summary>
    public static (int Taken, uint Life) Redirect(int damage, int pct, uint life)
    {
        int share = (int)Math.Min(life, Math.Max(0, (long)damage * Math.Clamp(pct, 0, 100) / 100));
        return (damage - share, life - (uint)share);
    }
}

partial class Program
{
    /// <summary>"X% of Damage from Hits is taken from your nearest Totem's Life before you": Forest of Ancestors' 20 and
    /// Kaom's Leadership's 25 add.</summary>
    internal const string TotemRedirectStat = "damage_removed_from_your_nearest_totem_before_life_or_es_%";

    /// <summary>Metadata/Characters/Character.ot: base_number_of_totems_allowed 1 and base_presence_radius 80 (the wiki's
    /// fixed 8 metres "in your presence"). The client has both from the .ot; the server's stats do not carry them.</summary>
    internal const int BaseTotemsAllowed = 1, PresenceRadius = 80;

    /// <summary>The totem skills with an executor: Rejuvenation Totem's aura (BuffDefinitions 172 totem_aura_life_regen,
    /// base radius 10, wiki Rejuvenation Totem) regenerates the player's Life. Other totem skills keep their old cast:
    /// a totem that does not use its skill is not placed.</summary>
    static readonly Dictionary<string, (ushort Buff, int Radius)> TotemAuras = new() { ["RejuvenationTotem"] = (172, 10) };

    // GrantedEffects row → MonsterVarieties row of its default skin: ActiveSkills.SkillTotemId is SkillTotemVariations'
    // SkillTotemsKey, TotemSkinId 0 (Rejuvenation Totem: 4 → Metadata/Monsters/Totems/LifeRegenTotem).
    static readonly Lazy<Dictionary<int, int>> TotemVarieties = new(() => LoadTotemVarieties("data/tables"));

    // The totem auras' BuffDefinitions stats (172: base_life_regeneration_rate_per_minute and five more); the totem's
    // skill gives their values.
    static readonly Lazy<Dictionary<ushort, uint[]>> TotemBuffStats = new(() =>
    {
        var result = new Dictionary<ushort, uint[]>();
        string path = System.IO.Path.Combine("data/tables", "BuffDefinitions.json");
        if (!File.Exists(path)) return result;
        using var definitions = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var (row, _) in TotemAuras.Values)
            if (row < definitions.RootElement.GetArrayLength()
                && definitions.RootElement[row].TryGetProperty("StatsKeys", out var keys) && keys.ValueKind == JsonValueKind.Array)
                result[row] = [.. keys.EnumerateArray().Select(k => (uint)(k.GetInt32() + 1))];
        return result;
    });

    static Dictionary<int, int> LoadTotemVarieties(string tables)
    {
        string Path(string name) => System.IO.Path.Combine(tables, name + ".json");
        var result = new Dictionary<int, int>();
        if (!File.Exists(Path("GrantedEffects")) || !File.Exists(Path("ActiveSkills")) || !File.Exists(Path("SkillTotemVariations"))) return result;
        using var effects = JsonDocument.Parse(File.ReadAllBytes(Path("GrantedEffects")));
        using var active = JsonDocument.Parse(File.ReadAllBytes(Path("ActiveSkills")));
        using var variations = JsonDocument.Parse(File.ReadAllBytes(Path("SkillTotemVariations")));
        var skins = new Dictionary<int, int>();
        foreach (var v in variations.RootElement.EnumerateArray())
            if (v.GetProperty("TotemSkinId").GetInt32() == 0 && v.GetProperty("MonsterVarietiesKey") is { ValueKind: JsonValueKind.Number } row)
                skins.TryAdd(v.GetProperty("SkillTotemsKey").GetInt32(), row.GetInt32());
        var skills = active.RootElement.EnumerateArray().ToArray();
        int i = 0;
        foreach (var e in effects.RootElement.EnumerateArray())
        {
            if (e.TryGetProperty("ActiveSkill", out var a) && a.ValueKind == JsonValueKind.Number && a.GetInt32() < skills.Length
                && skills[a.GetInt32()].TryGetProperty("SkillTotemId", out var totem) && totem.ValueKind == JsonValueKind.Number
                && skins.TryGetValue(totem.GetInt32(), out int variety))
                result[i] = variety;
            i++;
        }
        return result;
    }

    /// <summary>The totem variety a gem skill with a totem executor summons, or null.</summary>
    internal static MonsterCatalog.Variety? TotemVariety(ClientState conn, int effect) =>
        SkillsFor(conn).EffectId(effect) is { } id && TotemAuras.ContainsKey(id) && TotemVarieties.Value.TryGetValue(effect, out int row)
            && Monsters.Find(row.ToString()) is { Supported: true } variety ? variety : null;

    // A totem skill's use, after its mana: the totem rises at the aimed point once the summon ends (GrantedEffects
    // CastTime, 0.6 s, over totem placement speed and action speed; wiki Totem: cast speed does not apply).
    static async Task PlaceTotem(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        ushort skillId, int effect, int level, MonsterCatalog.Variety variety, (int X, int Y) at, Func<bool> current)
    {
        double factor, speed;
        lock (conn.Monsters) factor = conn.ElementalAilments.ActionFactor(conn.Clock.Elapsed.TotalSeconds);
        speed = Stats.Key("summon_totem_cast_speed_+%") is uint k ? SupportedStats(conn, skillId).GetValueOrDefault(k) : 0;
        double seconds = SkillsFor(conn).CastTimeMs(effect) / 1000.0 / Math.Max(.01, 1 + speed / 100) / Math.Max(.01, factor);
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        await WhilePaused(conn);
        if (!current() || conn.Closed || conn.LeavingArea || conn.PlayerLife == 0) return;
        await CreateTotem(remote, stream, ciphers, conn, skillId, effect, level, variety, at);
    }

    /// <summary>
    /// Summons one totem of a skill at a point: Life from the variety's base at the gem's level requirement (PoB's totem
    /// level; the MinionLife column, as the client derives it) with totem_life_+%, duration base_totem_duration with
    /// totem_duration_+% only (wiki Totem), limit Character.ot's 1 plus number_of_additional_totems_allowed (the player's
    /// and the skill's), replacing the oldest. Its aura's stats and radius are the skill's at this moment. Null when the
    /// limit is below one (the client refuses the use itself then) or the player is gone.
    /// </summary>
    internal static async Task<uint?> CreateTotem(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        ushort skillId, int effect, int gemLevel, MonsterCatalog.Variety variety, (int X, int Y) at)
    {
        var stats = SupportedStats(conn, skillId);
        var own = SupportedSkill(conn, skillId).Stats;
        int Get(string name) => Stats.Key(name) is uint k ? stats.GetValueOrDefault(k) : 0;
        int limit = BaseTotemsAllowed + Get("number_of_additional_totems_allowed");
        if (limit < 1) return null;
        byte level = (byte)Math.Clamp(SkillsFor(conn).PlayerLevelReq(effect, gemLevel), 1, 100);
        int baseLife = Monsters.BaseLife(variety, level) ?? 1;
        uint life = (uint)Math.Clamp((long)baseLife * (100 + Get("totem_life_+%")) / 100, 1, int.MaxValue);
        double seconds = Get("base_totem_duration") / 1000.0 * Math.Max(0, 100 + Get("totem_duration_+%")) / 100;
        TotemState.Aura? aura = null;
        if (SkillsFor(conn).EffectId(effect) is { } id && TotemAuras.TryGetValue(id, out var source))
        {
            var area = MergeSkillStats(AreaPlayerStats(conn), own);
            int Area(string name) => (name == "skill_area_of_effect_+%" ? conn.Pools.AuraAreaPct : 0)
                + (Stats.Key(name) is uint k ? area.GetValueOrDefault(k) : 0);
            var buffStats = TotemBuffStats.Value.GetValueOrDefault(source.Buff) ?? [];
            aura = new(source.Buff, SkillArea.Radius(source.Radius, Area, spell: true),
                [.. buffStats.Where(own.ContainsKey).Select(s => (s, own[s])).Where(s => s.Item2 != 0)]);
        }
        int bodySize = Math.Clamp(variety.ObjectSize - 1, -1, 127);
        (int X, int Y) place = (Math.Clamp(at.X, 0, Math.Max(0, conn.Layout.GridW - 1)), Math.Clamp(at.Y, 0, Math.Max(0, conn.Layout.GridH - 1)));
        var playerStats = CharacterPlayerStats(conn);
        uint? created = null;
        // Registered and created at the send gate, so the tick's presentation never sends a totem the client lacks.
        await Send(remote, stream, ciphers, [], null, atGate: () =>
        {
            lock (conn.Monsters)
            {
                if (conn.Closed || conn.LeavingArea || conn.PlayerLife == 0) return null;
                uint objectId = conn.NextObjectId++;
                if (bodySize >= 0)
                {
                    bool Floor(int cx, int cy) => conn.Layout.WalkableFor(cx, cy, bodySize) && !conn.Barrier.Contains((cx, cy));
                    if (CollisionEscape.Find(place, conn.Layout.GridW, conn.Layout.GridH, Floor, conn.MonsterBlockers(objectId),
                        moverSize: bodySize) is { } clear) place = clear;
                }
                double now = conn.Clock.Elapsed.TotalSeconds;
                var replaced = conn.Totems.Place(new(objectId, skillId, place, life, now + seconds, aura), limit, now);
                if (replaced.Count > 0) TotemsDiedLocked(conn, now, playerStats);
                var bytes = new List<byte>();
                if (conn.MonsterTypes.Add((variety.Row, level)))
                    bytes.AddRange(InstanceEntities.MonsterTypes([((ushort)variety.Row, level)]));
                var blob = new MonsterObject(place.X, place.Y, life, variety.ComponentsOrBase, StateMachine: variety.StateMachine,
                    TransitionState: variety.TransitionStates, Team: 0,
                    Stats: life == baseLife ? null : [(StatKeys.MaximumLife, (int)life)]) { LifeData = variety.LifeData };
                bytes.AddRange(InstanceEntities.CreateObject(objectId,
                    InstanceEntities.ObjectTypeHash(MonsterObject.TypePath(variety.Id, level)), blob.Blob()));
                bytes.AddRange(InstanceProtocol.EnterWakeZone(objectId));
                bytes.AddRange(TotemPresentation(conn) ?? []);
                Console.WriteLine($"[gateway] {CharacterName(conn)}: totem 0x{objectId:x} {variety}@{level} at ({place.X}, {place.Y}), "
                    + $"life {life}, {seconds:0.##} s, limit {limit}, {replaced.Count} replaced"
                    + (aura is null ? "" : $", aura {aura.Row} radius {aura.Radius}"));
                created = objectId;
                return [.. bytes];
            }
        });
        return created;
    }

    /// <summary>
    /// Before Ward, Energy Shield and Life take a hit (DamagePlayerHit, under the Monsters lock): the redirect stat's share of
    /// each part goes to the nearest living totem's Life, capped by it (PoB ModParser "taken from your nearest totem's life
    /// before you", HaveTotem; reducePoolsByDamage). No distance limit applies. What the player still takes.
    /// </summary>
    internal static (int NonChaos, int Chaos) TotemsTakeHitLocked(ClientState conn, int nonChaos, int chaos, double now,
        IReadOnlyDictionary<uint, int> stats)
    {
        int pct = Stats.Key(TotemRedirectStat) is uint key ? stats.GetValueOrDefault(key) : 0;
        // The totems live on the connection clock; the hit's world time places the player.
        double clock = conn.Clock.Elapsed.TotalSeconds;
        if (pct <= 0 || conn.Totems.Nearest(conn.Walk.Position(now), clock) is not { } totem) return (nonChaos, chaos);
        var (keptNonChaos, life) = TotemState.Redirect(nonChaos, pct, totem.Life);
        var (keptChaos, left) = TotemState.Redirect(chaos, pct, life);
        totem.Life = left;
        if (left == 0)
        {
            totem.DiedAt = clock;
            TotemsDiedLocked(conn, clock, stats);
        }
        return (keptNonChaos, keptChaos);
    }

    // Totems died: with a living Unique enemy within the player's presence, Spirit of Kaom triggers (its node's
    // has_trigger_on_totem_death_in_presence_of_unique; the shared Spirit cooldown decides).
    static void TotemsDiedLocked(ClientState conn, double now, IReadOnlyDictionary<uint, int> stats)
    {
        if (conn.PlayerLife == 0) return;
        var here = conn.Walk.Position(now);
        if (conn.Monsters.Values.Any(m => m.Hostile && !m.Dead && m.Life > 0 && m.Rarity == MonsterRarity.Unique
            && Dist(m.Walk.Position(now), here) <= PresenceRadius))
            TriggerSpiritChieftainLocked(conn, TotemDeathInPresenceOfUniqueTrigger, stats);
    }

    // The aura of the totem covering the living player joins his stats (CharacterPlayerStatsCore), with his aura effect.
    static void AddTotemAuraStats(ClientState conn, Dictionary<uint, int> stats)
    {
        lock (conn.Monsters)
        {
            double now = conn.Clock.Elapsed.TotalSeconds;
            if (conn.PlayerLife == 0 || conn.Totems.AuraAt(conn.Walk.Position(now), now)?.Aura is not { } aura) return;
            foreach (var (key, value) in aura.Stats)
                stats[key] = (int)Math.Clamp(stats.GetValueOrDefault(key)
                    + Math.Round(value * Math.Max(0, 100 + conn.Pools.AuraEffectPct) / 100.0, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);
        }
    }

    // The totems as the client should see them, from the player tick and after a placement: expired ones die, their Life
    // (0x1b1), the dead ones' removal (0x2bb), and the aura buff on the player (0x1c1 from the totem, 0x1c2) with his stats.
    internal static byte[]? TotemPresentation(ClientState conn)
    {
        lock (conn.Monsters)
        {
            if (conn.Closed || conn.LeavingArea) return null;
            var totems = conn.Totems;
            double now = conn.Clock.Elapsed.TotalSeconds;
            if (totems.Expire(now).Count > 0) TotemsDiedLocked(conn, now, CharacterPlayerStats(conn));
            var bytes = new List<byte>();
            foreach (var t in totems.All)
            {
                uint life = t.DiedAt is null ? t.Life : 0;
                if (t.SentDeath || life == t.SentLife && life > 0) continue;
                bytes.AddRange(InstanceProtocol.LifeUpdate(t.Id, life));
                (t.SentLife, t.SentDeath) = (life, life == 0);
            }
            foreach (var t in totems.Remove(now)) bytes.AddRange(InstanceProtocol.RemoveObject(t.Id));
            uint source = conn.PlayerLife > 0 ? totems.AuraAt(conn.Walk.Position(now), now)?.Id ?? 0 : 0;
            if (source != totems.SentAuraTotem)
            {
                if (totems.SentAuraInstance != 0) bytes.AddRange(InstanceEntities.RemoveBuff(PlayerId, totems.SentAuraInstance));
                totems.SentAuraInstance = 0;
                // shortcut: the buff names the player as its source (the shape every server buff uses), not the totem and its
                // skill; send the totem once a live check shows the client takes a monster source for a player buff.
                if (totems.All.FirstOrDefault(t => t.Id == source) is { Aura: { } aura })
                    bytes.AddRange(InstanceEntities.AddBuff(PlayerId, totems.SentAuraInstance = conn.NextBuff++, aura.Row, PlayerId));
                totems.SentAuraTotem = source;
                bytes.AddRange(PlayerStatsUpdateAtGate(conn, PlayerStatsWithCleared(conn)));
            }
            return bytes.Count == 0 ? null : [.. bytes];
        }
    }

    static Task SendTotems(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn) =>
        Send(remote, stream, ciphers, [], null, atGate: () => TotemPresentation(conn));
}
