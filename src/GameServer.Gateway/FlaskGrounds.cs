using System.Text.Json;

/// <summary>
/// What some utility flasks leave on the ground or do around the player when used, from their implicit mods (Agents/docs/
/// protocol/13-items.md §8a): Stibnite Flask's smoke cloud (local_smoke_ground_on_flask_use_radius 16), which blinds the
/// enemies standing in it; Sulphur Flask's consecrated ground (local_consecrate_ground_on_flask_use_radius 18), on which
/// the player regenerates life; Basalt Flask's taunt (local_flask_taunt_enemies_on_use_radius 30, 3 s). Radii are in
/// cells. The ground lasts as long as the flask's effect: the tables do not give its own duration.
/// </summary>
static class FlaskGrounds
{
    public enum Kind { SmokeCloud, ConsecratedGround, Taunt }

    /// <summary>The flask's implicit stats that make something on use, by kind.</summary>
    static readonly (string Stat, Kind Kind)[] OnUseStats =
    [
        ("local_smoke_ground_on_flask_use_radius", Kind.SmokeCloud),
        ("local_consecrate_ground_on_flask_use_radius", Kind.ConsecratedGround),
        ("local_flask_taunt_enemies_on_use_radius", Kind.Taunt),
    ];

    /// <summary>What a use of a flask with these local stats makes, and its radius.</summary>
    public static IEnumerable<(Kind Kind, int Radius)> OnUse(IReadOnlyDictionary<string, int> local) =>
        OnUseStats.Where(s => local.GetValueOrDefault(s.Stat) > 0).Select(s => (s.Kind, local[s.Stat]));

    /// <summary>Taunt lasts 3 s (the stat's reminder text: "Taunt lasts for 3 seconds"; wiki Taunt), before
    /// taunt_duration_+% (Program.TauntSeconds).</summary>
    public const double TauntSeconds = 3;

    /// <summary>Blind: 20% less accuracy and evasion (Path of Building CalcPerform "Blind"; BuffDefinitions blinded,
    /// accuracy_rating_+%_final_from_blinded and evasion_rating_+%_final_from_blinded, has no default value in the tables).</summary>
    public const int BlindLessPct = 20;

    /// <summary>A value under Blind.</summary>
    public static int Blinded(int value) => (int)Math.Floor(value * (100 - BlindLessPct) / 100.0);

    /// <summary>What the player gets standing on consecrated ground: 5% of maximum life regenerated per second (Path of
    /// Building CalcPerform: LifeRegenPercent 5, as life_regeneration_rate_per_minute_% 300) and curses 50% less effective
    /// (the stat's reminder text and PoB).</summary>
    public static readonly IReadOnlyList<(string Stat, int Value)> ConsecratedStats =
        [("life_regeneration_rate_per_minute_%", 300), ("curse_effect_on_self_+%", -50)];

    /// <summary>Consecrated ground's stats raised by consecrated_ground_effect_+% (Anointed Champion's 50%: 450 and -75).
    /// Every ground the engine makes is the player's own, so "Consecrated Ground you create" is all of them (PoB CalcPerform
    /// multiplies the 5% regeneration and the -50% curse effect by 1 + the increase).</summary>
    public static IReadOnlyList<(string Stat, int Value)> Consecrated(int effectPct) => effectPct == 0 ? ConsecratedStats
        : [.. ConsecratedStats.Select(s => (s.Stat, (int)((long)s.Value * (100 + effectPct) / 100)))];

    /// <summary>The radius of the consecrated ground around a stationary player, as the client derives
    /// virtual_consecrated_ground_while_stationary_radius (0x1428d43a0): the plain stat, the Strength variant while Strength
    /// is the highest attribute (ties count), the Crusader variant with 2 or more Crusader items; the largest wins.</summary>
    public static int StationaryRadius(Func<string, int> get)
    {
        int strength = get("strength");
        int radius = get("consecrated_ground_while_stationary_radius");
        if (strength >= get("dexterity") && strength >= get("intelligence"))
            radius = Math.Max(radius, get("consecrated_ground_while_stationary_radius_if_highest_attribute_is_strength"));
        if (get("number_of_equipped_crusader_items") > 1)
            radius = Math.Max(radius, get("consecrated_ground_while_stationary_radius_if_2_crusader_items"));
        return radius;
    }

    /// <summary>How long the effects of the player's consecrated ground stay after he stops being covered by any.</summary>
    public const string LingerStat = "your_consecrated_ground_effect_lingers_for_ms_after_leaving_the_area";

    /// <summary>BuffDefinitions rows: blinded (on monsters in smoke), ground_consecration (on the player on it), taunted.</summary>
    public const ushort BlindedBuffRow = 179, ConsecratedBuffRow = 208, TauntedBuffRow = 408;

    /// <summary>The ground's art: GroundEffects' animated object for the GroundEffectTypes Id (variation 0, the current
    /// art) and its size (GroundEffects __col4, taken as the radius it is drawn for in world units; not verified).</summary>
    public static Dictionary<Kind, (string Ao, int ArtRadius)> LoadArt(string folder)
    {
        var art = new Dictionary<Kind, (string, int)>();
        string types = Path.Combine(folder, "tables", "GroundEffectTypes.json"), effects = Path.Combine(folder, "tables", "GroundEffects.json");
        if (!File.Exists(types) || !File.Exists(effects)) return art;
        using var t = JsonDocument.Parse(File.ReadAllBytes(types));
        var ids = t.RootElement.EnumerateArray().Select(r => r.GetProperty("Id").GetString()).ToList();
        using var e = JsonDocument.Parse(File.ReadAllBytes(effects));
        foreach (var (kind, id) in new[] { (Kind.SmokeCloud, "Smoke"), (Kind.ConsecratedGround, "Consecration") })
            foreach (var r in e.RootElement.EnumerateArray())
                if (r.GetProperty("GroundEffectTypesKey") is { ValueKind: JsonValueKind.Number } k && ids.ElementAtOrDefault(k.GetInt32()) == id
                    && r.GetProperty("__col1").GetInt32() == 0 && r.GetProperty("__col2").GetInt32() == 2
                    && r.GetProperty("AOFile").EnumerateArray().FirstOrDefault().GetString() is { Length: > 0 } ao)
                {
                    art[kind] = (ao, r.GetProperty("__col4").GetInt32());
                    break;
                }
        return art;
    }

    /// <summary>One ground a flask made: where, how far it reaches, when it ends (connection clock seconds) and its effect
    /// object (the art). A taunt makes none: it is the monsters' (Program.TauntLocked).</summary>
    public sealed record Ground(Kind Kind, int X, int Y, int Radius, double Ends, uint? Object = null)
    {
        public bool Covers((int X, int Y) at) => (at.X - X) * (at.X - X) + (at.Y - Y) * (at.Y - Y) <= Radius * Radius;
    }

    /// <summary>One connection's flask grounds, the monsters blinded by smoke (with their buff ids), whether the player
    /// stands on consecrated ground (its buff id), and the consecrated ground around him while stationary.</summary>
    public sealed class State
    {
        readonly List<Ground> _grounds = [];
        readonly Dictionary<uint, ushort> _blinded = [];
        readonly Lock _lock = new();
        ushort? _consecrated;
        bool _ticking;
        int _stationaryRadius;
        double _lingerSeconds, _lastCovered = double.NegativeInfinity;
        Ground? _stationary;

        public bool Any
        {
            get { lock (_lock) return !Idle; }
        }

        bool Idle => _grounds.Count == 0 && _blinded.Count == 0 && _consecrated is null && _stationaryRadius == 0 && _stationary is null;

        /// <summary>The stationary radius and the linger, from the stats last sent to the client (Program.PlayerStatsWithCleared).</summary>
        public void Configure(int stationaryRadius, int lingerMs)
        {
            lock (_lock) (_stationaryRadius, _lingerSeconds) = (Math.Max(0, stationaryRadius), Math.Max(0, lingerMs) / 1000.0);
        }

        /// <summary>True once when a stationary radius needs the ticker and none runs (Program.RunPlayerTick starts it).</summary>
        public bool StartTickerIfConfigured()
        {
            lock (_lock)
            {
                if (_ticking || _stationaryRadius == 0) return false;
                return _ticking = true;
            }
        }

        /// <summary>The ground around the player while he is <paramref name="still"/>: the old one to take away (he moved,
        /// died, or the radius changed) and the radius of one to make where he stands (0: none).</summary>
        public (Ground? Gone, int Make) Stationary(bool still, (int X, int Y) at)
        {
            lock (_lock)
            {
                if (still && _stationaryRadius > 0 && _stationary is { } kept && (kept.X, kept.Y) == at && kept.Radius == _stationaryRadius)
                    return (null, 0);
                var gone = _stationary;
                _stationary = null;
                return (gone, still ? _stationaryRadius : 0);
            }
        }

        /// <summary>The stationary ground made after <see cref="Stationary"/> asked for one.</summary>
        public void PlaceStationary(Ground ground)
        {
            lock (_lock) _stationary = ground;
        }

        /// <summary>Whether the player counts as on consecrated ground: covered by one of his grounds now, or within the
        /// linger after the last moment one covered him (the wiki: the effects linger after they would normally be removed,
        /// by leaving or by the ground ending). A death ends the linger.</summary>
        public bool OnConsecrated(bool alive, (int X, int Y) at, double now)
        {
            lock (_lock)
            {
                if (!alive)
                {
                    _lastCovered = double.NegativeInfinity;
                    return false;
                }
                bool covered = _grounds.Append(_stationary).Any(g => g?.Kind == Kind.ConsecratedGround && g.Covers(at));
                if (covered) _lastCovered = now;
                return covered || now < _lastCovered + _lingerSeconds;
            }
        }

        /// <summary>Adds a ground: true when no ticker runs, so one has to start now (Program.RunFlaskGrounds).</summary>
        public bool Add(Ground ground)
        {
            lock (_lock)
            {
                _grounds.Add(ground);
                if (_ticking) return false;
                return _ticking = true;
            }
        }

        /// <summary>Whether the ticker stops: nothing is left to look after (it stops then, and the next ground starts
        /// another).</summary>
        public bool StopTickerIfIdle()
        {
            lock (_lock)
            {
                if (!Idle) return false;
                _ticking = false;
                return true;
            }
        }

        /// <summary>The ticker exited for another reason (a closed connection, an error): the next ground starts another.</summary>
        public void TickerExited()
        {
            lock (_lock) _ticking = false;
        }

        /// <summary>The grounds whose time is up by <paramref name="now"/> (removed).</summary>
        public List<Ground> Expire(double now)
        {
            lock (_lock)
            {
                List<Ground> ended = [.. _grounds.Where(g => g.Ends <= now)];
                foreach (var g in ended) _grounds.Remove(g);
                return ended;
            }
        }

        /// <summary>Whether a ground of this kind covers the cell.</summary>
        public bool Covers(Kind kind, (int X, int Y) at)
        {
            lock (_lock) return _grounds.Append(_stationary).Any(g => g?.Kind == kind && g.Covers(at));
        }

        /// <summary>Marks a monster blinded (with the buff id that shows it) or not: the buff id to add (newly blinded),
        /// or to remove (no longer), or null when nothing changes. <paramref name="nextBuff"/> runs outside this lock (it
        /// takes the connection's Monsters lock, under which <see cref="Blinded(uint)"/> is read).</summary>
        public (ushort Buff, bool Added)? SetBlinded(uint monster, bool blinded, Func<ushort> nextBuff)
        {
            lock (_lock)
            {
                if (blinded == _blinded.ContainsKey(monster)) return null;
                if (!blinded)
                {
                    _blinded.Remove(monster, out ushort old);
                    return (old, false);
                }
            }
            ushort buff = nextBuff();
            lock (_lock) return _blinded.TryAdd(monster, buff) ? (buff, true) : null;
        }

        /// <summary>The monsters blinded now.</summary>
        public IReadOnlyList<uint> BlindedMonsters()
        {
            lock (_lock) return [.. _blinded.Keys];
        }

        /// <summary>Whether the monster stands blinded in smoke (Program.Blinded: one source of the player's Blind).</summary>
        public bool Blinded(uint monster)
        {
            lock (_lock) return _blinded.ContainsKey(monster);
        }

        /// <summary>Marks the player on consecrated ground or not: the buff id to add or to remove, or null.</summary>
        public (ushort Buff, bool Added)? SetConsecrated(bool on, Func<ushort> nextBuff)
        {
            lock (_lock)
            {
                if (on == _consecrated is not null) return null;
                if (!on)
                {
                    ushort old = _consecrated!.Value;
                    _consecrated = null;
                    return (old, false);
                }
            }
            ushort buff = nextBuff();
            lock (_lock)
            {
                if (_consecrated is not null) return null;
                _consecrated = buff;
                return (buff, true);
            }
        }

        /// <summary>What the grounds give the player now: consecrated ground's stats while on it, raised by the player's
        /// consecrated_ground_effect_+%.</summary>
        public IReadOnlyList<(string Stat, int Value)> PlayerStats(int effectPct = 0)
        {
            lock (_lock) return _consecrated is null ? [] : Consecrated(effectPct);
        }
    }
}
