using System.Globalization;

namespace GameServer.Data.Terrain.Generation;

/// <summary>One doodad line of a .ddt group (<c>0x142a17320</c>): the .ao, its weight (−1: an "All" line, placed at
/// every vertex of the group's grounds), an angle (radians; +∞ = random), its own spacing radius (runtime +0xc: no
/// other of the same .ao that close) and the group's clear radius (+0x10: nothing else on the same ground that close).</summary>
public sealed record DdtEntry(string Model, int Weight, float Angle, int Radius, int GroundRadius)
{
    public bool RandomAngle => float.IsPositiveInfinity(Angle);
}

/// <summary>A weighted list of .ddt lines with their total (runtime +0x18 list, +0x30 total).</summary>
public sealed class DdtGroup
{
    public List<DdtEntry> Weighted { get; } = [];
    public int Total { get; private set; }

    public void Add(DdtEntry e)
    {
        if (e.Weight == 0) return;
        Weighted.Add(e);
        Total += e.Weight;
    }

    /// <summary><c>0x1420061f0</c>: the entry the roll falls on (none when the list is empty).</summary>
    public DdtEntry? Pick(uint roll)
    {
        if (Total == 0 || Weighted.Count == 0) return null;
        int r = (int)roll;
        foreach (var e in Weighted)
        {
            if (r < e.Weight) return e;
            r -= e.Weight;
        }
        return Weighted[^1];
    }
}

/// <summary>A doodad table (.ddt, parser <c>0x142a17e00</c>, callbacks <c>0x141ff2540</c>…<c>0x141ff2640</c>): "version
/// N", the default density (× 1000) and from version 4 the default clear radius and an optional flag "1", then
/// "Default" and its lines; then groups: a line of quoted ground names (comma separated) followed by "All" (the group's
/// lines are picked once at every vertex of those grounds) or by a density ("D" = the default) and a clear radius, then
/// the group's lines. A line is "weight "file.ao" [radius|D] [angle]", weight "All" making it a fixed line.</summary>
public sealed class DoodadTable
{
    /// <summary>+0x13c: the highest density of any group (the random doodads' attempts per 1000 tiles).</summary>
    public int MaxDensity { get; private set; }

    /// <summary>+0x138 bit 0 (the flag after the default clear radius).</summary>
    public bool Flag { get; private set; }

    /// <summary>+0x138 &gt;&gt; 1: an attempt on a ground without a group is dropped while Next(1024) is below it.</summary>
    public uint DefaultThreshold { get; private set; }

    public DdtGroup Default { get; } = new();
    public List<DdtEntry> DefaultFixed { get; } = [];
    /// <summary>+0x38: the density groups by ground name; +0xf8 their thresholds.</summary>
    public Dictionary<string, DdtGroup> Grounds { get; } = [];
    public Dictionary<string, uint> Thresholds { get; } = [];
    /// <summary>+0x78: the "All" groups by ground name.</summary>
    public Dictionary<string, DdtGroup> AllGroups { get; } = [];
    /// <summary>+0xb8: fixed lines by ground name.</summary>
    public Dictionary<string, List<DdtEntry>> Fixed { get; } = [];

    public static DoodadTable Parse(string text)
    {
        var t = new DoodadTable();
        var r = new Reader(text);
        if (r.Word() != "version") throw new InvalidDataException("Error: Expected: \"version\"");
        int version = (int)r.Number();
        int density = (int)Math.Floor(r.Number() * 1000 + 0.5);
        int radius = 0;
        if (version > 3) radius = (int)r.Number();
        string rest = r.Line().Trim();
        if (version > 3 && rest == "1") t.Flag = true;
        if (r.Word() != "Default") throw new InvalidDataException("Expected: \"Default\"");
        var (weighted, fixedLines) = ReadLines(r, version, radius);
        t.MaxDensity = Math.Max(t.MaxDensity, density);
        foreach (var e in weighted) t.Default.Add(e);
        t.DefaultFixed.AddRange(fixedLines);
        var groupDensities = new List<(string Ground, int Density)>();
        while (!r.End)
        {
            string line = r.Line().Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
            var h = new Reader(line);
            h.SkipSpace();
            if (h.Peek() != '"') throw new InvalidDataException("Error: could not read doodad ground type " + line);
            var grounds = h.Quoted().Split(',').Select(g => g.Trim()).ToList();
            string word = h.Word();
            if (word.Length >= 3 && word.StartsWith("All", StringComparison.Ordinal))
            {
                var (w, f) = ReadLines(r, version, radius);
                t.MaxDensity = Math.Max(t.MaxDensity, density);
                t.Store(t.AllGroups, grounds, w, f);
            }
            else
            {
                int groupDensity = density;
                if (word.Length > 0 && word[0] != 'D' && float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out float d))
                    groupDensity = (int)Math.Floor(d * 1000 + 0.5);
                int groupRadius = radius;
                if (version > 3 && h.TryNumber(out double gr)) groupRadius = (int)gr;
                var (w, f) = ReadLines(r, version, groupRadius);
                t.MaxDensity = Math.Max(t.MaxDensity, groupDensity);
                foreach (var g in grounds) groupDensities.Add((g, groupDensity));
                t.Store(t.Grounds, grounds, w, f);
            }
        }
        // 0x141ff2730 after the parse: the thresholds out of 1024 against the highest density.
        int max = Math.Max(t.MaxDensity, 1);
        t.DefaultThreshold = (uint)((max - density) * 0x400 / max);
        foreach (var (g, d) in groupDensities) t.Thresholds[g] = (uint)((max - d) * 0x400 / max);
        return t;
    }

    /// <summary><c>0x141ff2260</c> / <c>0x141ff2350</c>: every ground of the header shares the lines; a ground without
    /// fixed lines leaves the fixed map.</summary>
    void Store(Dictionary<string, DdtGroup> groups, List<string> grounds, List<DdtEntry> weighted, List<DdtEntry> fixedLines)
    {
        foreach (var g in grounds)
        {
            if (!groups.TryGetValue(g, out var group)) groups[g] = group = new DdtGroup();
            foreach (var e in weighted) group.Add(e);
            if (fixedLines.Count == 0) Fixed.Remove(g);
            else
            {
                if (!Fixed.TryGetValue(g, out var list)) Fixed[g] = list = [];
                list.AddRange(fixedLines);
            }
        }
    }

    /// <summary><c>0x142a17320</c>: lines up to the next line starting with a quote.</summary>
    static (List<DdtEntry> Weighted, List<DdtEntry> Fixed) ReadLines(Reader r, int version, int groupRadius)
    {
        var weighted = new List<DdtEntry>();
        var fixedLines = new List<DdtEntry>();
        r.SkipSpace();
        while (!r.End && r.Peek() != '"')
        {
            string line = r.Line().Trim();
            r.SkipSpace();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
            var l = new Reader(line);
            string first = l.Word();
            int weight = first == "All" ? -1 : int.Parse(first, CultureInfo.InvariantCulture);
            l.SkipSpace();
            if (l.Peek() != '"') throw new InvalidDataException("Error: could not read doodad filename " + line);
            string model = l.Quoted();
            int radius = 0;
            if (version > 3 && weight != -1)
            {
                string token = l.Word();
                if (token.Length > 0 && token[0] != 'D') radius = (int)uint.Parse(token, CultureInfo.InvariantCulture);
            }
            float angle = l.TryNumber(out double a) ? (float)a : float.PositiveInfinity;
            var e = new DdtEntry(model, weight, angle, radius, groupRadius);
            (weight == -1 ? fixedLines : weighted).Add(e);
        }
        return (weighted, fixedLines);
    }

    sealed class Reader(string s)
    {
        int _p;
        public bool End => _p >= s.Length;
        public char Peek() => End ? '\0' : s[_p];
        public void SkipSpace() { while (!End && s[_p] <= ' ') _p++; }

        public string Word()
        {
            SkipSpace();
            int start = _p;
            while (!End && s[_p] > ' ') _p++;
            return s[start.._p];
        }

        public double Number() => double.Parse(Word(), NumberStyles.Float, CultureInfo.InvariantCulture);

        public bool TryNumber(out double value)
        {
            int at = _p;
            if (double.TryParse(Word(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
            _p = at;
            return false;
        }

        public string Line()
        {
            int start = _p;
            while (!End && s[_p] != '\n') _p++;
            string line = s[start.._p];
            if (!End) _p++;
            return line;
        }

        public string Quoted()
        {
            _p++;
            int start = _p;
            while (!End && s[_p] != '"') _p++;
            string q = s[start.._p];
            if (!End) _p++;
            return q;
        }
    }
}

/// <summary>An .ao's <c>AOSet</c> component (parser <c>0x141dbf380</c>, keys <c>0x141dc0140</c> / <c>0x141dc0270</c>):
/// either fixed children (<c>fixed_ao = "x y z … file"</c>, +0x30: the set itself is the doodad, its children are
/// attached to it) or weighted ones (<c>ao = "weight file"</c>, +0x10, total +0x28; "null" places nothing), never both.
/// <c>random_rotation_min</c> / <c>_max</c> +0x48 / +0x4c (<c>random_rotation = true</c>: −π … π),
/// <c>random_scale_min</c> / <c>_max</c> +0x50 / +0x54 (1 when absent), <c>random_translate_radius</c> +0x58, an
/// orientation mask +0x5c (<c>random_flip</c> |= 0x11, <c>random_orientation</c> = 0xff).</summary>
public sealed class AoSet
{
    public bool HasFixed { get; private set; }
    public List<(string? Model, int Weight)> Weighted { get; } = [];
    public int Total { get; private set; }
    public float RotationMin { get; private set; }
    public float RotationMax { get; private set; }
    public float ScaleMin { get; private set; } = 1;
    public float ScaleMax { get; private set; } = 1;
    public float TranslateRadius { get; private set; }
    public byte OrientationMask { get; private set; }

    /// <summary>The file's top-level <c>AOSet</c> block, or null without one.</summary>
    public static AoSet? Parse(string text)
    {
        AoSet? set = null;
        int braces = 0;
        bool inSet = false;
        foreach (var raw in TerrainText.Lines(text))
        {
            string l = raw.Trim();
            if (braces == 0 && l.StartsWith("AOSet", StringComparison.Ordinal) && (l.Length == 5 || l[5] is ' ' or '\t' or '{'))
            {
                set ??= new AoSet();
                inSet = true;
            }
            if (inSet && braces >= 1 && l.Contains('='))
            {
                int eq = l.IndexOf('=');
                string key = l[..eq].Trim(), value = l[(eq + 1)..].Trim().Trim('"');
                set!.Set(key, value);
            }
            braces += l.Count(c => c == '{') - l.Count(c => c == '}');
            if (braces == 0 && l.Contains('}')) inSet = false;
        }
        return set;
    }

    void Set(string key, string value)
    {
        static float F(string v) => float.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture);
        switch (key)
        {
            case "fixed_ao": HasFixed = true; break;
            case "ao":
                int space = value.IndexOf(' ');
                int weight = (int)uint.Parse(value[..space], CultureInfo.InvariantCulture);
                string file = value[(space + 1)..].Trim();
                Weighted.Add((file == "null" ? null : file, weight));
                Total += weight;
                break;
            case "random_rotation_min": RotationMin = F(value); break;
            case "random_rotation_max": RotationMax = F(value); break;
            case "random_scale_min": ScaleMin = F(value); break;
            case "random_scale_max": ScaleMax = F(value); break;
            case "random_translate_radius": TranslateRadius = F(value); break;
            case "random_rotation":
                (RotationMin, RotationMax) = value == "true" ? (-MathF.PI, MathF.PI) : (0, 0);
                break;
            case "random_flip": if (value == "true") OrientationMask |= 0x11; break;
            case "random_orientation": if (value == "true") OrientationMask = 0xff; break;
        }
    }

    /// <summary><c>0x141dd31d0</c>: the child the roll falls on.</summary>
    public string? Pick(uint roll)
    {
        int r = (int)roll;
        foreach (var (model, weight) in Weighted)
        {
            if (r < weight) return model;
            r -= weight;
        }
        return null;
    }
}

/// <summary>What <c>0x14202ede0</c> makes of a doodad line's .ao: the model created, its angle, scale, mirror and offset
/// (result +0, +8, +0xc, +0x10, +0x14 / +0x18).</summary>
public readonly record struct AoPick(string Model, float Angle, float Scale, bool Mirrored, int Dx, int Dy);

/// <summary>A subgraph record as the doodad stage reads it (generator +0x30 → +0x10, 0x98 bytes): its rectangle in tiles
/// (+0x40 … +0x4c), master (+0x50) and the rooms placed in it (+0x58, 0x38 bytes each, in placing order).</summary>
public sealed record DoodadRecord(int X, int Y, int W, int H, Dictionary<string, string> Master, string MasterPath,
    IReadOnlyList<PlacedRoom> Rooms);

/// <summary>The doodad stage (<c>0x1420425d0</c>, "Generating doodads..."), the parts ported so far: first the doodads
/// of every record's rooms (<c>0x142040770</c>), then per record the random doodads of its master's .ddt (<c>Next</c>
/// draws between a copy and a restore of the generator state) and the doodads at its inner vertices ("All" groups and
/// fixed lines). Each random or vertex doodad goes through <c>0x142044180</c>: <c>0x14202ede0</c> (an .ao without
/// AOSet: a random angle when the line has none), a mirror roll for random doodads, another angle roll. A doodad's
/// collision goes on the walkability grids as it is created, so later walk checks see it.</summary>
public sealed class DoodadStage(TerrainCache files, LevelRandom rng, TileGrid grid, Walkability walk, List<string> notes)
{
    public List<PlacedDoodad> Placed { get; } = [];

    readonly Dictionary<string, DoodadHull?> _hulls = new(StringComparer.OrdinalIgnoreCase);

    readonly Dictionary<string, (bool Created, bool Static)> _clientTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a room or tile line's object type is created as a client object (<c>0x142041960</c>,
    /// <c>0x1420415e0</c>, vtable +0x98 = <c>0x14152d680</c>): the type (<c>0x141ec1360</c>) takes its BaseEvents
    /// flags, +0x2a4 serialised (no <c>no_serialise</c>), +0x2a8 1 for <c>client_only</c> / 2 for <c>server_only</c>,
    /// +0x2ac <c>league_category</c> (60 without one, else it must be the generator's +0xd0). So: not serialised or
    /// client only, not server only. Across the captures this holds for every type, <c>DoodadNoBlocking</c> included
    /// (all 5 062 of its room and tile lines in the 117 captures the stage reaches are captured; it is not static).
    /// Types with a league category are taken as another league's. The MiscObjects.dat rows <c>DoodadNoOverlap</c> / <c>DoodadNoOverlap3</c> /
    /// <c>TileUpperDoodadNoOverlap</c> (<c>0x14202f640</c> → <c>0x142041ac0</c>) and <c>DoodadLayerDoodad</c>
    /// (<c>0x14202f530</c> → world vtable +0x60) go elsewhere: not ported, left out. Static: the type's Positioned has
    /// <c>static = true</c> (<c>Doodad</c>, <c>DoodadInvisible</c>); only those stamp their Hull. The others
    /// (<c>HangingSlave</c>, <c>BellyGrassClusterTrigger</c>, the <c>FlowBlocking</c> objects) are created without
    /// collision, as the captured grids show.</summary>
    (bool Created, bool Static) ClientType(string type)
    {
        if (_clientTypes.TryGetValue(type, out var known)) return known;
        bool? noSerialise = null, clientOnly = null, serverOnly = null;
        bool league = false, found = false, isStatic = false;
        string? t = type;
        // The .ot and the ones it extends, nearest first: the first value of a key wins.
        for (int depth = 0; t is { Length: > 0 } && t != "nothing" && depth < 16; depth++)
        {
            var text = files.Text(t + ".ot");
            if (text is null) break;
            found = true;
            var lines = TerrainText.Lines(text).Select(l => l.Trim()).ToList();
            // A component is a top-level block (the client section's blocks are one level down).
            int braces = 0;
            string block = "";
            foreach (var l in lines)
            {
                if (braces == 0 && l.Length > 0 && l != "{" && l != "}") block = l;
                if (braces == 1 && block == "Positioned" && l.Replace(" ", "").Replace("\t", "") == "static=true") isStatic = true;
                braces += l.Count(c => c == '{') - l.Count(c => c == '}');
            }
            foreach (var l in lines.Select(l => l.Replace(" ", "").Replace("\t", "")))
            {
                if (l.StartsWith("no_serialise=", StringComparison.Ordinal)) noSerialise ??= l.EndsWith("=true", StringComparison.Ordinal);
                else if (l.StartsWith("client_only=", StringComparison.Ordinal)) clientOnly ??= l.EndsWith("=true", StringComparison.Ordinal);
                else if (l.StartsWith("server_only=", StringComparison.Ordinal)) serverOnly ??= l.EndsWith("=true", StringComparison.Ordinal);
                else if (l.StartsWith("league_category=", StringComparison.Ordinal)) league = true;
            }
            t = lines.Where(l => l.StartsWith("extends ", StringComparison.Ordinal))
                .Select(l => TerrainText.Tokenize(l).Skip(1).FirstOrDefault().Text).FirstOrDefault();
        }
        string name = type[(type.LastIndexOf('/') + 1)..];
        bool routed = name is "DoodadNoOverlap" or "TileUpperDoodadNoOverlap" or "DoodadLayerDoodad";
        bool created = found && !routed && !league && (noSerialise == true || clientOnly == true) && serverOnly != true;
        return _clientTypes[type] = (created, isStatic);
    }

    public void Run(IReadOnlyList<DoodadRecord> records)
    {
        // 0x1420425d0's first loop: every record's rooms, in order.
        foreach (var rec in records)
            foreach (var room in rec.Rooms) RoomDoodads(room);
        for (int index = 0; index < records.Count; index++)
        {
            var rec = records[index];
            if (!rec.Master.TryGetValue("Doodads", out var ddtPath)) continue;
            var text = files.Text(TerrainText.Resolve(rec.MasterPath, ddtPath));
            if (text is null) continue;
            var ddt = DoodadTable.Parse(text);
            uint attempts = (uint)(ddt.MaxDensity * rec.W * rec.H) / 1000;
            if (attempts != 0)
            {
                var saved = rng.Save();
                RandomDoodads(records, index, ddt, attempts);
                rng.Restore(saved);
            }
            if (ddt.AllGroups.Count != 0 || ddt.DefaultFixed.Count != 0 || ddt.Fixed.Count != 0) VertexDoodads(records, index, ddt);
        }
        // Generator vtable +0x20 (0x142041200), after the stage and +0x50: the placed tiles' own objects.
        TileDoodads(records);
    }

    /// <summary><c>0x142041200</c>: row by row, every tile origin's objects (its .tmd, or the record master's Reskin
    /// copy of it, <c>0x141d7eda0</c> → <c>0x141d7f700</c>; per sub-tile for a tile made of them), then the .tmd the
    /// .tdt names (+0x180), each placed as a room line (<c>0x1420415e0</c> → <c>0x14202f1c0</c>) with the tile's size,
    /// origin and orientation.</summary>
    void TileDoodads(IReadOnlyList<DoodadRecord> records)
    {
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                if (grid[x, y] is not { IsOrigin: true } e) continue;
                var tile = e.Tile;
                var objects = files.TileObjects(tile);
                // 0x141d77a90: the last record holding the tile, else the first.
                int index = 0;
                for (int k = 0; k < records.Count; k++)
                    if (x >= records[k].X && x < records[k].X + records[k].W && y >= records[k].Y && y < records[k].Y + records[k].H) index = k;
                string? reskin = records.Count != 0 ? records[index].Master.GetValueOrDefault("Reskin") : null;

                // 0x1420415e0: the .ao is resolved (AOSet draws) before the type filters. The line's angle and mirror
                // are in the tile's frame.
                void Line(TileObject o, int cellX, int cellY, float lineAngle, bool lineMirror)
                {
                    if (Resolve(o.Animation ?? o.VirtualModel(tile.Path), false) is not { } def) return;
                    var (created, isStatic) = ClientType(o.Type);
                    if (!created) return;
                    var (cx, cy) = Orientation.Map(e.Orientation, cellX, cellY, tile.Width * 23, tile.Height * 23);
                    bool mirrors = e.Orientation is >= 4 and < 8;
                    float angle = TilePlacement.Angle(e.Orientation, lineAngle) + def.Angle;
                    Create(new PlacedDoodad(def.Model, x * 23 + cx + def.Dx, y * 23 + cy + def.Dy, angle, def.Scale, mirrors ^ lineMirror ^ def.Mirrored), isStatic);
                }

                if (objects.Count != 0 && (files.TileFlags(tile.Path) & 0x200) != 0) SubTileDoodads(tile, reskin, Line);
                else if (objects.Count != 0)
                {
                    if (reskin is { Length: > 0 }) objects = files.ReskinObjects(tile, reskin) ?? objects;
                    foreach (var o in objects) Line(o, o.CellX, o.CellY, o.Angle, false);
                }
                foreach (var o in files.SecondObjects(tile)) Line(o, o.CellX, o.CellY, o.Angle, false);
            }
    }

    /// <summary><c>0x141d7eda0</c> for a tile made of sub-tiles: row by row, every slot that starts a sub-tile (G without
    /// bits 2, 4, 8) takes its mesh's objects (<c>0x141d7f430</c>, from the Reskin copy when there is one), turned by the
    /// slot's code inside the sub-tile's span (<c>0x141d7ec10</c>, 250 units per slot: a = [w − x, x, h − y, y] read
    /// like <see cref="TilePlacement.Cell"/>), moved to the slot and floored to cells (× 0.092); the angle is turned by
    /// the code and codes 4–7 mirror. Heights (the slot's step × tile +0x177 × 7.8125) and the line's orientation mask
    /// (+0x1e through <c>0x143646180</c>) are not carried, as for the other tile lines.</summary>
    void SubTileDoodads(TileDef tile, string? reskin, Action<TileObject, int, int, float, bool> line)
    {
        if (files.Walk(tile.Path) is not { } walkData || walkData.SubTiles.Length < tile.Width * tile.Height) return;
        SubTileWalk Slot(int u, int v) => walkData.SubTiles[v * tile.Width + u];
        for (int v = 0; v < tile.Height; v++)
            for (int u = 0; u < tile.Width; u++)
            {
                var slot = Slot(u, v);
                if ((slot.G & 0xe) != 0) continue;
                if (files.SubTileObjects(tile, slot.Mesh, reskin) is not { } objects) continue;
                // The span: the slots on the right that continue it (G bit 2) and those below (bit 4).
                int w = 1, h = 1;
                while (u + w < tile.Width && (Slot(u + w, v).G & 2) != 0) w++;
                while (v + h < tile.Height && (Slot(u, v + h).G & 4) != 0) h++;
                int code = slot.Code;
                if ((code & 1) != 0) (w, h) = (h, w);
                var (t0, t1, t2) = TilePlacement.Row(code);
                foreach (var o in objects)
                {
                    Span<float> a = [w * 250 - o.X, o.X, h * 250 - o.Y, o.Y];
                    float px = u * 250f + a[t1 + 2 * t0], py = v * 250f + a[(t0 == 0 ? 2 : 0) + t2];
                    line(o, (int)MathF.Floor(px * 0.092f), (int)MathF.Floor(py * 0.092f), TilePlacement.Angle(code, o.Angle), code >= 4);
                }
            }
    }

    /// <summary><c>0x141d77c60</c>: the tile is in record <paramref name="index"/> and in no later one.</summary>
    static bool Owns(IReadOnlyList<DoodadRecord> records, int index, int tx, int ty)
    {
        static bool In(DoodadRecord r, int x, int y) => x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H;
        if (!In(records[index], tx, ty)) return false;
        for (int k = index + 1; k < records.Count; k++)
            if (In(records[k], tx, ty)) return false;
        return true;
    }

    string? GroundName(string? path) => path is null ? null : files.Ground(path)?.Name;
    int GroundFlags(string? path) => path is null ? 0 : files.Ground(path)?.Flags ?? 0;

    void RandomDoodads(IReadOnlyList<DoodadRecord> records, int index, DoodadTable ddt, uint attempts)
    {
        var rec = records[index];
        var sameModel = new Dictionary<string, List<(int X, int Y, int R2)>>();
        var sameGround = new Dictionary<string, List<(int X, int Y, int R2)>>();
        for (uint n = 0; n < attempts; n++)
        {
            int x = rec.X * 23 + (int)rng.Next((uint)(rec.W * 23));
            int y = rec.Y * 23 + (int)rng.Next((uint)(rec.H * 23));
            int tx = x / 23, ty = y / 23;
            if (!Owns(records, index, tx, ty)) continue;
            if (grid[tx, ty] is not { } e) continue;
            if ((TileFlags(e.Tile) & 4) == 0) continue;
            // The nearest vertex's ground.
            string? groundPath = grid.Corner(tx + (x - tx * 23 > 11 ? 1 : 0), ty + (y - ty * 23 > 11 ? 1 : 0)).Ground;
            string? ground = GroundName(groundPath);
            DdtEntry? pick;
            bool loose = ground is null || (GroundFlags(groundPath) & 4) != 0;
            // No ground, or a flag-4 one: a cell already short of open ground keeps no doodad (0x141d85260 < 5).
            if (loose && walk.Walk[y * walk.Width + x] < 5) continue;
            if (ground is null)
            {
                if (ddt.Default.Weighted.Count == 0) continue;
                if (rng.Next(0x400) < ddt.DefaultThreshold) continue;
                pick = ddt.Default.Pick(rng.Next((uint)ddt.Default.Total));
            }
            else if (ddt.Grounds.GetValueOrDefault(ground) is { } group)
            {
                if (rng.Next(0x400) < ddt.Thresholds.GetValueOrDefault(ground)) continue;
                pick = group.Pick(rng.Next((uint)group.Total));
            }
            else
            {
                // A flag-4 ground without a group takes the default list.
                if (!loose || rng.Next(0x400) < ddt.DefaultThreshold) continue;
                pick = ddt.Default.Pick(rng.Next((uint)ddt.Default.Total));
            }
            if (pick is null) continue;
            if (Near(sameModel.GetValueOrDefault(pick.Model), x, y)) continue;
            string groundKey = ground ?? "";
            if (Near(sameGround.GetValueOrDefault(groundKey), x, y)) continue;
            if (!Place(pick, x, y, flip: true)) continue;
            if (pick.Radius != 0)
            {
                if (!sameModel.TryGetValue(pick.Model, out var l)) sameModel[pick.Model] = l = [];
                l.Add((x, y, pick.Radius * pick.Radius));
            }
            if (pick.GroundRadius != 0)
            {
                if (!sameGround.TryGetValue(groundKey, out var l)) sameGround[groundKey] = l = [];
                l.Add((x, y, pick.GroundRadius * pick.GroundRadius));
            }
        }
    }

    static bool Near(List<(int X, int Y, int R2)>? points, int x, int y) =>
        points is not null && points.Any(p => (uint)((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y)) <= (uint)p.R2);

    /// <summary>The 1020-line loop's second half: every inner vertex of the record it owns takes its ground's fixed lines
    /// (or the default ones) and one pick of its ground's "All" group.</summary>
    void VertexDoodads(IReadOnlyList<DoodadRecord> records, int index, DoodadTable ddt)
    {
        var rec = records[index];
        for (int v = 1; v < rec.H; v++)
            for (int u = 1; u < rec.W; u++)
            {
                int vx = rec.X + u, vy = rec.Y + v;
                if (!Owns(records, index, vx, vy)) continue;
                string? ground = GroundName(grid.Corner(vx, vy).Ground);
                var fixedLines = ground is null ? ddt.DefaultFixed : ddt.Fixed.GetValueOrDefault(ground) ?? [];
                foreach (var f in fixedLines) Place(f, vx * 23, vy * 23, flip: false);
                if (ddt.AllGroups.TryGetValue(ground ?? "Default", out var all) && all.Total != 0)
                    if (all.Pick(rng.Next((uint)all.Total)) is { } pick) Place(pick, vx * 23, vy * 23, flip: true);
            }
    }

    /// <summary><c>0x142044180</c>.</summary>
    bool Place(DdtEntry entry, int x, int y, bool flip)
    {
        if (files.Text(entry.Model) is null || Resolve(entry.Model, entry.RandomAngle) is not { } def) return false;
        bool mirrored = def.Mirrored;
        float angle;
        if (entry.RandomAngle && flip && rng.Next(2) != 0) mirrored = !mirrored;
        if (entry.RandomAngle) angle = rng.Next(0x200) * (1f / 512) * (MathF.PI * 2);
        else
        {
            angle = entry.Angle;
            while (angle < 0) angle += MathF.PI * 2;
            while (angle > MathF.PI * 2) angle -= MathF.PI * 2;
        }
        Create(new PlacedDoodad(def.Model, x + def.Dx, y + def.Dy, def.Angle + angle, def.Scale, mirrored));
        return true;
    }

    readonly Dictionary<string, AoSet?> _sets = new(StringComparer.OrdinalIgnoreCase);

    AoSet? Set(string model, string text)
    {
        if (!_sets.TryGetValue(model, out var set)) _sets[model] = set = text.Contains("AOSet", StringComparison.Ordinal) ? AoSet.Parse(text) : null;
        return set;
    }

    static float Wrap(float a)
    {
        while (a < 0) a += MathF.PI * 2;
        while (a > MathF.PI * 2) a -= MathF.PI * 2;
        return a;
    }

    /// <summary><c>0x14202ede0</c>: while the .ao has an AOSet of weighted children, one is picked (<c>Next(total)</c>;
    /// "null" places nothing); a set with fixed children, or an .ao without AOSet, is the doodad. The last AOSet seen
    /// gives the rest: an angle in its rotation range (<c>Next(512)</c>), else a random one when
    /// <paramref name="randomAngle"/>; with an orientation mask a code (<c>Next(840)</c>, <c>0x14299da50</c>: the
    /// roll's set bit, codes 4-7 mirrored) turns it; a scale in its range; an offset in its translate radius (two
    /// <c>Next(512)</c>; no file has one, so the offset itself is not ported). Null when nothing is placed.</summary>
    AoPick? Resolve(string model, bool randomAngle)
    {
        AoSet? set = null;
        for (int depth = 0; ; depth++)
        {
            // A virtual .ao (no file, e.g. a tile object's <.tdt folder>/<name>.ao) has no AOSet.
            var text = files.Text(model);
            var own = text is null ? null : Set(model, text);
            if (own is not null) set = own;
            if (own is null || own.HasFixed || depth >= 16) break;
            if (own.Pick(rng.Next((uint)own.Total)) is not { } child) return null;
            model = child;
        }
        float angle = 0, scale = 1;
        bool mirrored = false;
        const float step = 1f / 512, turn = MathF.PI * 2;
        if (set is null)
        {
            if (randomAngle) angle = rng.Next(0x200) * step * turn;
            return new AoPick(model, angle, scale, mirrored, 0, 0);
        }
        if (set.RotationMin < 0 || set.RotationMax > 0)
            angle = (set.RotationMax - set.RotationMin) * rng.Next(0x200) * step + set.RotationMin;
        else if (randomAngle) angle = rng.Next(0x200) * step * turn;
        if (set.OrientationMask != 0)
        {
            uint roll = (uint)rng.Next(0x348);
            int code = OrientationBit(set.OrientationMask, roll);
            mirrored = code is >= 4 and < 8;
            if (mirrored) angle = turn - angle;
            angle = Wrap(angle + (code & 3) * (MathF.PI / 2));
        }
        if (set.ScaleMin < 1 || set.ScaleMax > 1)
            scale = (set.ScaleMax - set.ScaleMin) * rng.Next(0x200) * step + set.ScaleMin;
        if (set.TranslateRadius > 0)
        {
            rng.Next(0x200);
            rng.Next(0x200);
            notes.Add($"doodad {model}: AOSet random_translate_radius offset (0x14202ede0) is not ported");
        }
        return new AoPick(model, angle, scale, mirrored, 0, 0);
    }

    /// <summary><c>0x14299da50</c>: the roll-th (mod count) set bit of the mask; a full or empty mask takes roll &amp; 7.</summary>
    static int OrientationBit(byte mask, uint roll)
    {
        if ((byte)(mask - 1) >= 0xfe) return (int)(roll & 7);
        var bits = Enumerable.Range(0, 8).Where(b => (mask >> b & 1) != 0).ToList();
        return bits[(int)(roll % (uint)bits.Count)];
    }

    /// <summary><c>0x142040770</c>: a room's doodad lines in order. Each resolves its .ao (<c>0x14202ede0</c> without a
    /// random angle: no draw for an .ao without AOSet) and is placed by <c>0x14202f1c0</c>: the cell turned with the
    /// room's code inside its untouched w·23 × h·23 cells, plus the room's origin; the angle mirrored (2π − a) for codes
    /// 4–7, plus (code &amp; 3)·π/2; the scale; mirrored when the code mirrors xor the line's flip.</summary>
    void RoomDoodads(PlacedRoom room)
    {
        int code = room.Orientation;
        foreach (var d in room.Room.Doodads)
        {
            // The .ao is resolved (AOSet draws) before the type filters.
            if (files.Text(d.Model) is null || Resolve(d.Model, false) is not { } def) continue;
            var (created, isStatic) = ClientType(d.Type);
            if (!created) continue;
            var (cx, cy) = Orientation.Map(code, (int)d.X, (int)d.Y, room.Room.Width * 23, room.Room.Height * 23);
            float angle = d.Angle;
            bool mirrors = code is >= 4 and < 8;
            if (mirrors) angle = MathF.PI * 2 - angle;
            angle += (code & 3) * (MathF.PI / 2);
            while (angle < 0) angle += MathF.PI * 2;
            while (angle > MathF.PI * 2) angle -= MathF.PI * 2;
            Create(new PlacedDoodad(def.Model, room.X * 23 + cx + def.Dx, room.Y * 23 + cy + def.Dy, angle + def.Angle,
                d.Scale * def.Scale, mirrors ^ d.Flip ^ def.Mirrored), isStatic);
        }
    }

    /// <summary><c>0x142042360</c> (nothing outside the map) and the world add: the doodad and, for a static type
    /// (random and vertex doodads are <c>Doodad</c>s), its collision (<c>0x141d876e0</c>).</summary>
    void Create(PlacedDoodad d, bool isStatic = true)
    {
        if (d.X < 0 || d.Y < 0 || d.X >= grid.Width * 23 || d.Y >= grid.Height * 23) return;
        Placed.Add(d);
        if (!isStatic) return;
        if (!_hulls.TryGetValue(d.Model, out var hull)) _hulls[d.Model] = hull = DoodadHull.Load(files.Text, d.Model);
        if (hull is { IsEmpty: false }) walk.Stamp(hull, d);
    }

    /// <summary>Tile data +0x16c: the low byte of the .tdt's flags word (bit 4: doodads may go on the tile).</summary>
    int TileFlags(TileDef tile) => files.TileFlags(tile.Path);
}
