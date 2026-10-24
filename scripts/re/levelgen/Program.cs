// usage: dotnet run --project scripts/re/levelgen -- hash <trace.gen.json>…
// Checks the server's level generator against the client's generation traces (live.ps1 -Walkmaps writes
// data/walkability/<area>_seed<seed>.gen.json). Agents/docs/re/011-level-generation.md.
using System.Text.Json;
using GameServer.Data.Ggpk;
using GameServer.Data.Terrain;
using GameServer.Data.Terrain.Generation;
using GgDump;

Console.OutputEncoding = System.Text.Encoding.UTF8;

switch (args.FirstOrDefault())
{
    case "hash":
        foreach (var path in args.Skip(1)) Hash(path);
        break;
    case "cat":
        // Terrain files as text (what the generator reads).
        using (var fs = OpenGame())
            foreach (var path in args.Skip(1))
                Console.WriteLine(fs.TryReadFile(path, out var data) ? $"== {path}\n{TerrainText.Decode(data)}" : $"== {path}: not found");
        break;
    case "ls":
        // ls <regex>: the game paths matching a regex (case-insensitive).
        using (var fs = OpenGame())
        {
            var pattern = new System.Text.RegularExpressions.Regex(args[1], System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (var path in fs.EnumeratePaths().Where(p => pattern.IsMatch(p)).Order()) Console.WriteLine(path);
        }
        break;
    case "grep":
        // grep <path regex> <text regex>: the lines matching the text regex in every text file whose path matches.
        using (var fs = OpenGame())
        {
            var paths = new System.Text.RegularExpressions.Regex(args[1], System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var text = new System.Text.RegularExpressions.Regex(args[2]);
            foreach (var path in fs.EnumeratePaths().Where(p => paths.IsMatch(p)).Order())
            {
                if (!fs.TryReadFile(path, out var data)) continue;
                foreach (var line in TerrainText.Decode(data).Split('\n').Where(l => text.IsMatch(l)))
                    Console.WriteLine($"{path}: {line.TrimEnd()}");
            }
        }
        break;
    case "raw":
        // raw <game path> <out file>: a game file's bytes as they are.
        using (var fs = OpenGame())
            if (fs.TryReadFile(args[1], out var data)) File.WriteAllBytes(args[2], data);
            else Console.WriteLine($"{args[1]}: not found");
        break;
    case "tile":
        // tile <.tdt>…: a tile's tag and key as the generator reads them.
        using (var fs = OpenGame())
        {
            var cache = new TerrainCache(p => fs.TryReadFile(p, out var d) ? d : null);
            foreach (var path in args.Skip(1))
                if (cache.Tile(path) is { } tile)
                {
                    for (int o = 0; o < 8; o++) Console.WriteLine($"{Path.GetFileName(path)} o{o}: tag '{tile.Tag}' {tile.Key.Rotate(o)}");
                    Console.WriteLine($"  orientation restriction 0x{tile.Restriction:x2}");
                    if (fs.TryReadFile(path, out var raw))
                    {
                        // The header after the string table, from the key's end (offset 58).
                        int n = (int)BitConverter.ToUInt32(raw, 4), at = 8 + 2 * n;
                        Console.WriteLine($"  version {BitConverter.ToUInt32(raw, 0)}, header[56..72] " +
                            string.Join(" ", raw.Skip(at + 56).Take(16).Select(b => b.ToString("x2"))));
                    }
                    // The vertex heights, row by row (orientation 0).
                    for (int v = 0; v <= tile.Height; v++)
                        Console.WriteLine("  heights " + string.Join(" ", Enumerable.Range(0, tile.Width + 1).Select(u => tile.VertexHeight(u, v))));
                    // The vertex grounds, row by row (orientation 0), "-" for none.
                    for (int v = 0; v <= tile.Height; v++)
                        Console.WriteLine("  grounds " + string.Join(" ", Enumerable.Range(0, tile.Width + 1).Select(u =>
                            tile.VertexGround(u, v) is { } g ? Path.GetFileNameWithoutExtension(g) : "-")));
                }
                else Console.WriteLine($"{path}: not found");
        }
        break;
    case "subtiles":
        // subtiles: every .tdt whose flags word has bit 9 (tile data +0x16d bit 1, made of sub-tiles), with its objects.
        using (var fs = OpenGame())
        {
            var cache = new TerrainCache(p => fs.TryReadFile(p, out var d) ? d : null);
            int all = 0, withObjects = 0;
            foreach (var path in fs.EnumeratePaths().Where(p => p.EndsWith(".tdt", StringComparison.OrdinalIgnoreCase)).Order())
            {
                if ((cache.TileFlags(path) & 0x200) == 0) continue;
                all++;
                if (cache.Tile(path) is not { } tile) continue;
                var objects = cache.TileObjects(tile);
                if (objects.Count == 0) continue;
                withObjects++;
                Console.WriteLine($"{path} {tile.Width}x{tile.Height}: {objects.Count} objects ({string.Join(", ", objects.Select(o => o.Type).Distinct().Take(4))})");
            }
            Console.WriteLine($"{all} tiles with sub-tiles, {withObjects} with objects");
        }
        break;
    case "tmds":
        // tmds: per .tdt, where its objects come from: the mesh's .tmd (+0x178), the .tmd its string table names (+0x180),
        // and the .tmd beside the .tdt.
        using (var fs = OpenGame())
        {
            var cache = new TerrainCache(p => fs.TryReadFile(p, out var d) ? d : null);
            var counts = new Dictionary<string, int>();
            foreach (var path in fs.EnumeratePaths().Where(p => p.EndsWith(".tdt", StringComparison.OrdinalIgnoreCase)).Order())
            {
                TileDef? tile;
                try { tile = cache.Tile(path); } catch (IndexOutOfRangeException) { continue; }
                if (tile is null) continue;
                bool Has(string? p) => p is not null && fs.TryReadFile(p, out _);
                bool mesh = tile.Mesh is { Length: > 4 } m && !m.Contains(';') && Has(m[..^4] + ".tmd");
                bool named = tile.ObjectsFile is not null;
                bool beside = Has(path[..^4] + ".tmd");
                bool same = named && string.Equals(tile.ObjectsFile, path[..^4] + ".tmd", StringComparison.OrdinalIgnoreCase);
                string k = $"mesh {mesh} named {named} beside {beside} same {same} noMesh {tile.Mesh is null}";
                counts[k] = counts.GetValueOrDefault(k) + 1;
                if (beside && !same && !mesh && args.Length > 1) Console.WriteLine($"{path}: mesh {tile.Mesh} named {tile.ObjectsFile}");
            }
            foreach (var (k, n) in counts.OrderBy(p => p.Key)) Console.WriteLine($"{n,6} {k}");
        }
        break;
    case "vertices":
        // vertices <.tst>: every tile's vertex grounds as the parser finds them (a check of the vertex block scan).
        using (var fs = OpenGame())
        {
            var cache = new TerrainCache(p => fs.TryReadFile(p, out var d) ? d : null);
            int found = 0, missing = 0;
            foreach (var (_, path, _) in cache.TileList(args[1]).DistinctBy(t => t.Path))
            {
                var tile = cache.Tile(path);
                if (tile is null) continue;
                if (tile.VertexGrounds.Length == 0) { missing++; Console.WriteLine($"{Path.GetFileName(path)} {tile.Width}x{tile.Height}: NO VERTEX BLOCK"); continue; }
                found++;
                if (args.Length > 2)
                    Console.WriteLine($"{Path.GetFileName(path)} {tile.Width}x{tile.Height}: " + string.Join(" ", tile.VertexGrounds.Select(g => g is null ? "-" : Path.GetFileNameWithoutExtension(g))) +
                                      (tile.VertexHeights.Any(x => x != 0) ? " H " + string.Join(",", tile.VertexHeights) : ""));
            }
            Console.WriteLine($"{found} with a vertex block, {missing} without");
        }
        break;
    case "generate":
        // generate <area id> <seed> [trace.gen.json]: the server's generation, compared with a client trace when given.
        Generate(args[1], uint.Parse(args[2]), args.Length > 3 ? args[3] : null);
        break;
    case "walk":
        // walk [<area id> <seed>]: the port's walkability and projectile grids against the walk-map capture
        // (data/walkability/<area>_seed<seed>.bin and .projectile.bin); without an area, one line per seed-1 capture.
        Walk(args.Length > 1 ? args[1] : null, args.Length > 2 ? uint.Parse(args[2]) : 1);
        break;
    case "doodads":
        // doodads <area id> <seed>: the doodad stage after the port's generation, its draws against the trace and its
        // doodads against the walk-map capture (<area>_seed<seed>.doodads.json).
        Doodads(args[1], uint.Parse(args[2]));
        break;
    case "oracle":
        // oracle [area regex]: every (area, seed) the client logged with its tile hash (Client/logs/Client.txt), run by
        // the port: match, mismatch, or where it stops.
        Oracle(args.Length > 1 ? args[1] : ".");
        break;
    default:
        Console.WriteLine("usage: levelgen hash <trace.gen.json>… | cat <game path>… | tile <.tdt>… | generate <area> <seed> [trace] | oracle [area regex]");
        break;
}

// The tile hash of a traced grid, computed the server's way, next to the hash the client sent.
static void Hash(string path)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(path));
    var root = doc.RootElement;
    int w = root.GetProperty("w").GetInt32(), h = root.GetProperty("h").GetInt32();
    var grid = new (string, short, byte)?[w, h];
    foreach (var t in root.GetProperty("tiles").EnumerateArray())
    {
        // Only a tile's origin entry counts (+0x34 / +0x35 are the entry's offset inside a larger tile).
        if (t[5].GetInt32() != 0 || t[6].GetInt32() != 0 || t[2].ValueKind != JsonValueKind.String) continue;
        grid[t[0].GetInt32(), t[1].GetInt32()] = (t[2].GetString()!, (short)t[3].GetInt32(), (byte)t[4].GetInt32());
    }
    uint mine = TileHash.Compute(w, h, (x, y) => grid[x, y]);
    uint theirs = root.GetProperty("tileHash").GetUInt32();
    Console.WriteLine($"{Path.GetFileName(path)}: {(mine == theirs ? "match" : "MISMATCH")} {mine} (client {theirs})");
}

static void Oracle(string filter)
{
    string root = Root();
    var pairs = new SortedDictionary<(string Area, uint Seed), (int Level, uint Hash)>();
    (string, uint, int)? cur = null;
    // The running client keeps its log open for writing.
    using var log = new StreamReader(new FileStream(Path.Combine(root, "Client", "logs", "Client.txt"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
    for (string? line; (line = log.ReadLine()) is not null;)
    {
        var m = System.Text.RegularExpressions.Regex.Match(line, @"Generating level (\d+) area ""([^""]+)"" with seed (\d+)");
        if (m.Success) { cur = (m.Groups[2].Value, uint.Parse(m.Groups[3].Value), int.Parse(m.Groups[1].Value)); continue; }
        m = System.Text.RegularExpressions.Regex.Match(line, @"Tile hash: (\d+)");
        if (m.Success && cur is { } c) { pairs[(c.Item1, c.Item2)] = (c.Item3, uint.Parse(m.Groups[1].Value)); cur = null; }
    }
    using var fs = OpenGame();
    var files = new TerrainCache(p => fs.TryReadFile(p, out var d) ? d : null);
    int match = 0, total = 0;
    var reasons = new Dictionary<string, int>();
    foreach (var ((areaId, seed), (level, hash)) in pairs)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(areaId, filter)) continue;
        total++;
        string outcome;
        try
        {
            var area = AreaInfo.Load(p => fs.TryReadFile(p, out var d) ? d : null, areaId, level);
            var result = LevelGenerator.Generate(files, area, seed);
            uint mine = result.Grid.Hash();
            outcome = mine == hash ? "match" : $"MISMATCH ({result.Topology.Graph})";
            if (mine == hash) match++;
        }
        catch (Exception e) { outcome = $"{e.GetType().Name}: {e.Message}"; }
        Console.WriteLine($"{areaId} seed {seed}: {outcome}");
        string key = outcome.StartsWith("MISMATCH") ? "MISMATCH" : System.Text.RegularExpressions.Regex.Replace(outcome, @"\(.*", "");
        reasons[key] = reasons.GetValueOrDefault(key) + 1;
    }
    Console.WriteLine($"{match} of {total} match");
    foreach (var (k, n) in reasons.OrderByDescending(r => r.Value)) Console.WriteLine($"  {n} × {k}");
}

static void Walk(string? areaId, uint seed)
{
    string root = Root();
    using var fs = OpenGame();
    var files = new TerrainCache(p => fs.TryReadFile(p, out var d) ? d : null);
    if (areaId is not null)
    {
        WalkArea(fs, files, root, areaId, seed, verbose: true);
        return;
    }
    // Every area with a seed-1 capture: whether the port's tile hash is the client's, and the cells that differ.
    foreach (var bin in Directory.GetFiles(Path.Combine(root, "data", "walkability"), "*_seed1.bin").Order())
    {
        string id = Path.GetFileName(bin)[..^"_seed1.bin".Length];
        try { WalkArea(fs, files, root, id, 1, verbose: false); }
        catch (Exception e)
        {
            Console.WriteLine($"{id} stop ({e.Message.Split('\n')[0]})");
        }
    }
}

static void Doodads(string areaId, uint seed)
{
    string root = Root();
    using var fs = OpenGame();
    var files = new TerrainCache(p => fs.TryReadFile(p, out var d) ? d : null);
    string stem = Path.Combine(root, "data", "walkability", $"{areaId}_seed{seed}");
    using var trace = JsonDocument.Parse(File.ReadAllText(stem + ".gen.json"));
    var t = trace.RootElement;
    int level = t.TryGetProperty("level", out var l) ? l.GetInt32() : 1;
    var area = AreaInfo.Load(p => fs.TryReadFile(p, out var d) ? d : null, areaId, level);
    var rng = new LevelRandom(seed, area.RandomnessDisabled) { Log = [] };
    var result = LevelGenerator.Generate(files, area, seed, rng: rng);
    var grid = result.Grid;
    int before = rng.Log!.Count;
    var walk = Walkability.Build(grid, tile => files.Walk(tile.Path));
    var notes = new List<string>();
    foreach (var r in result.Records)
    {
        Console.WriteLine($"record ({r.X}, {r.Y}) {r.W}x{r.H} {r.MasterPath}: {r.Rooms.Count} rooms");
        foreach (var p in r.Rooms)
            Console.WriteLine($"  {p.Room.Path} at ({p.X}, {p.Y}) o{p.Orientation}");
    }
    var stage = new DoodadStage(files, rng, grid, walk, notes);
    stage.Run(result.Records);
    foreach (var n in notes.Distinct()) Console.WriteLine("  " + n);
    // Draw by draw from the tile stage's end.
    var theirs = TraceDraws(t);
    var mine = rng.Log!;
    int same = 0;
    while (same < theirs.Count && same < mine.Count && theirs[same].Kind == mine[same].Kind && theirs[same].Arg == mine[same].Arg && theirs[same].Result == mine[same].Result) same++;
    Console.WriteLine($"draws: {same} of {theirs.Count} match (port made {mine.Count}, {mine.Count - before} in the doodad stage)");
    // LEVELGEN_AROUND=n widens the window to n draws before and after the first difference.
    int window = int.TryParse(Environment.GetEnvironmentVariable("LEVELGEN_AROUND"), out int around) ? around : 0;
    if (same < theirs.Count)
        for (int i = Math.Max(0, same - window); i < Math.Min(same + Math.Max(6, window), theirs.Count); i++)
            Console.WriteLine($"  #{i}: client {theirs[i].Kind}({theirs[i].Arg}) = {theirs[i].Result} {theirs[i].Caller}" +
                (i < mine.Count ? $"  port {mine[i].Kind}({mine[i].Arg}) = {mine[i].Result}" : "  port -"));
    // Placements against the capture (model, x, y).
    using var dd = JsonDocument.Parse(File.ReadAllText(stem + ".doodads.json"));
    var models = dd.RootElement.GetProperty("models").EnumerateArray().Select(m => m.GetString()!).ToList();
    var captured = dd.RootElement.GetProperty("doodads").EnumerateArray()
        .Select(d => (Model: models[d[0].GetInt32()].ToLowerInvariant(), X: d[1].GetInt32(), Y: d[2].GetInt32(), A: d[3].GetSingle())).ToList();
    var placed = stage.Placed.Select(p => (Model: p.Model.ToLowerInvariant(), p.X, p.Y, A: p.Angle)).ToList();
    if (Environment.GetEnvironmentVariable("LEVELGEN_NEAR") is { Length: > 0 } near && near.Split(',').Select(int.Parse).ToArray() is [var nx, var ny, var nr])
        foreach (var p in placed.Where(p => Math.Abs(p.X - nx) <= nr && Math.Abs(p.Y - ny) <= nr))
            Console.WriteLine($"  near: {p.Model} ({p.X}, {p.Y}) {p.A:0.###} hull {!(DoodadHull.Load(files.Text, p.Model)?.IsEmpty ?? true)}");
    var capturedSet = captured.Select(c => (c.Model, c.X, c.Y)).ToHashSet();
    int found = placed.Count(p => capturedSet.Contains((p.Model, p.X, p.Y)));
    Console.WriteLine($"doodads: port {placed.Count}, {found} of them captured; capture {captured.Count}");
    // Matched doodads whose angle is not the captured one (mod 2π).
    var capturedAngles = captured.GroupBy(c => (c.Model, c.X, c.Y)).ToDictionary(g => g.Key, g => g.Select(c => c.A).ToList());
    var turned = placed.Where(p => capturedAngles.TryGetValue((p.Model, p.X, p.Y), out var a) &&
        !a.Any(c => MathF.Abs(MathF.IEEERemainder(c - p.A, MathF.PI * 2)) < 0.01f)).ToList();
    Console.WriteLine($"  {turned.Count} of them at another angle");
    foreach (var p in turned.Take(6))
        Console.WriteLine($"  angle: {Path.GetFileName(p.Model)} ({p.X}, {p.Y}) port {p.A:0.###} capture {string.Join("/", capturedAngles[(p.Model, p.X, p.Y)].Select(a => a.ToString("0.###")))}");
    foreach (var p in placed.Where(p => !capturedSet.Contains((p.Model, p.X, p.Y))).OrderBy(p => DoodadHull.Load(files.Text, p.Model)?.IsEmpty ?? true).Take(8))
        Console.WriteLine($"  port only: {Path.GetFileName(p.Model)} ({p.X}, {p.Y}) {p.A:0.###}");
    var placedSet = placed.Select(p => (p.Model, p.X, p.Y)).ToHashSet();
    int shown = Environment.GetEnvironmentVariable("LEVELGEN_ALL") is { Length: > 0 } ? int.MaxValue : 12;
    foreach (var c in captured.Where(c => !placedSet.Contains((c.Model, c.X, c.Y))).Take(shown))
        Console.WriteLine($"  capture only: {Path.GetFileName(c.Model)} ({c.X}, {c.Y}) {c.A:0.###} tile ({c.X / 23}, {c.Y / 23})");
}

static void WalkArea(GameFileSystem fs, TerrainCache files, string root, string areaId, uint seed, bool verbose)
{
    string stem = Path.Combine(root, "data", "walkability", $"{areaId}_seed{seed}");
    int level = 1;
    uint? clientHash = null;
    if (File.Exists(stem + ".gen.json"))
        using (var head = JsonDocument.Parse(File.ReadAllText(stem + ".gen.json")))
        {
            level = head.RootElement.TryGetProperty("level", out var l) ? l.GetInt32() : 1;
            clientHash = head.RootElement.TryGetProperty("tileHash", out var th) ? th.GetUInt32() : null;
        }
    var area = AreaInfo.Load(p => fs.TryReadFile(p, out var d) ? d : null, areaId, level);
    var result = LevelGenerator.Generate(files, area, seed);
    var grid = result.Grid;
    var walk = Walkability.Build(grid, t => files.Walk(t.Path));
    // The doodad stage stamps the generated doodads' collision; with LEVELGEN_CAPTURED_DOODADS the captured ones
    // (<area>_seed<seed>.doodads.json: models, then [model, x, y, angle]) instead; LEVELGEN_NODOODADS: none.
    int stamped = 0;
    string doodadNote = "";
    bool noDoodads = Environment.GetEnvironmentVariable("LEVELGEN_NODOODADS") is not null;
    bool captured = Environment.GetEnvironmentVariable("LEVELGEN_CAPTURED_DOODADS") is not null;
    if (!noDoodads && !captured)
    {
        var notes = new List<string>();
        var stage = new DoodadStage(files, result.Random, grid, walk, notes);
        try
        {
            stage.Run(result.Records);
        }
        catch (Exception e) when (e is NotSupportedException or InvalidDataException)
        {
            notes.Add("stage stopped: " + e.Message);
        }
        stamped = stage.Placed.Count;
        doodadNote = notes.Count == 0 ? "" : $" ({notes.Distinct().Count()} doodad notes: {notes[0]})";
        if (verbose) foreach (var n in notes.Distinct()) Console.WriteLine("  " + n);
    }
    if (captured && File.Exists(stem + ".doodads.json"))
        using (var dd = JsonDocument.Parse(File.ReadAllText(stem + ".doodads.json")))
        {
            var models = dd.RootElement.GetProperty("models").EnumerateArray().Select(m => m.GetString()!).ToList();
            var hulls = new Dictionary<string, DoodadHull?>();
            foreach (var d in dd.RootElement.GetProperty("doodads").EnumerateArray())
            {
                string model = models[d[0].GetInt32()];
                if (!hulls.TryGetValue(model, out var hull)) hulls[model] = hull = DoodadHull.Load(files.Text, model);
                if (hull is null || hull.IsEmpty) continue;
                walk.Stamp(hull, new PlacedDoodad(model, d[1].GetInt32(), d[2].GetInt32(), d[3].GetSingle()));
                stamped++;
            }
        }
    if (!verbose)
    {
        static string Count(byte[] mine, string file)
        {
            if (!File.Exists(file)) return "-";
            var theirs = File.ReadAllBytes(file);
            return theirs.Length != mine.Length ? $"size {theirs.Length}/{mine.Length}" : $"{mine.Where((v, i) => v != theirs[i]).Count()}/{mine.Length}";
        }
        string match = clientHash is null ? "?" : grid.Hash() == clientHash ? "yes" : "no";
        Console.WriteLine($"{areaId} {match} walk {Count(walk.Walk, stem + ".bin")} proj {Count(walk.Projectile, stem + ".projectile.bin")} doodads {stamped}{doodadNote}");
        return;
    }
    // Every placed tile's data: parsed, and one sub-tile per slot.
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (int y = 0; y < grid.Height; y++)
        for (int x = 0; x < grid.Width; x++)
            if (grid[x, y] is { } e && seen.Add(e.Tile.Path))
            {
                var data = files.Walk(e.Tile.Path);
                if (data is null) Console.WriteLine($"  no walk data: {e.Tile.Path}");
                else if (data.SubTiles.Length != e.Tile.Width * e.Tile.Height)
                    Console.WriteLine($"  {e.Tile.Path}: {data.SubTiles.Length} sub-tiles for {e.Tile.Width}x{e.Tile.Height}");
            }
    // LEVELGEN_VERTICES=x,y,w,h: the port's vertex grounds and flags there.
    if (Environment.GetEnvironmentVariable("LEVELGEN_VERTICES") is { Length: > 0 } vr && vr.Split(',').Select(int.Parse).ToArray() is [var vx, var vy, var vw, var vh])
        for (int y = vy; y <= vy + vh; y++)
            Console.WriteLine($"  v{y}: " + string.Join(" ", Enumerable.Range(vx, vw + 1).Select(x =>
                $"{Path.GetFileNameWithoutExtension(grid.Corner(x, y).Ground ?? "-")}/{grid.Corner(x, y).Flags}")));
    // LEVELGEN_HULL=x,y: the entry's sub-tile, its planes, and how many of the slot's cells each plane gets right.
    if (Environment.GetEnvironmentVariable("LEVELGEN_HULL") is { Length: > 0 } hr && hr.Split(',').Select(int.Parse).ToArray() is [var hx, var hy] &&
        grid[hx, hy] is { } he && files.Walk(he.Tile.Path) is { } hw)
    {
        var capture = File.ReadAllBytes(stem + ".bin");
        int idx = Enumerable.Range(0, he.Tile.Width * he.Tile.Height).First(i =>
            Orientation.Map(he.Orientation, i % he.Tile.Width, i / he.Tile.Width, he.Tile.Width, he.Tile.Height) == (he.OffsetX, he.OffsetY));
        var sub = hw.SubTiles[idx];
        var buf = new byte[sub.HullWidth * sub.HullHeight * 4];
        int n = sub.Hull is null ? 0 : Lz4.Decompress(sub.Hull, buf);
        int size = sub.HullWidth * sub.HullHeight;
        Console.WriteLine($"  {he.Tile.Path} o{he.Orientation} sub {idx} F 0x{sub.F:x2} G 0x{sub.G:x2} masks [{string.Join(",", hw.PlaneMasks)}] " +
            $"hull {n} bytes = {(double)n / size} planes of {sub.HullWidth}x{sub.HullHeight}");
        for (int p = 0; p * size < n; p++)
        {
            int ok = 0;
            for (int v = 0; v < 23; v++)
                for (int u = 0; u < 23; u++)
                {
                    var (tx, ty) = Orientation.Map(he.Orientation, u, v, 23, 23);
                    byte c = capture[(hy * 23 + ty) * grid.Width * 23 + hx * 23 + tx];
                    if ((buf[p * size + (v - sub.Y0) * sub.HullWidth + u - sub.X0] & 0xf) == c) ok++;
                }
            Console.WriteLine($"  plane {p}: {ok} of 529 cells equal the capture's");
        }
        for (int v = 0; v < 23; v++)
            Console.WriteLine("  " + string.Concat(Enumerable.Range(0, 23).Select(u => capture[(hy * 23 + v) * grid.Width * 23 + hx * 23 + u].ToString())) +
                "  " + string.Concat(Enumerable.Range(0, 23).Select(u => (buf[(v - sub.Y0) * sub.HullWidth + u - sub.X0] & 0xf).ToString())));
        // The whole plane as stored (hex), border included.
        if (Environment.GetEnvironmentVariable("LEVELGEN_HULL_RAW") is not null)
            for (int v = 0; v < sub.HullHeight; v++)
                Console.WriteLine("  raw " + string.Join(" ", Enumerable.Range(0, sub.HullWidth).Select(u => buf[v * sub.HullWidth + u].ToString("x2"))));
    }
    // LEVELGEN_WALK=<file>: the port's walk grid, one byte per cell (as the capture's .bin).
    if (Environment.GetEnvironmentVariable("LEVELGEN_WALK") is { Length: > 0 } walkOut) File.WriteAllBytes(walkOut, walk.Walk);
    Console.WriteLine($"{areaId} seed {seed}: tile hash {grid.Hash()} (client {clientHash}), grid {walk.Width}x{walk.Height}");
    using (var meta = JsonDocument.Parse(File.ReadAllText(stem + ".json")))
        Console.WriteLine($"capture: {meta.RootElement.GetProperty("w")}x{meta.RootElement.GetProperty("h")}");
    void Compare(string label, byte[] mine, string file)
    {
        if (!File.Exists(file)) { Console.WriteLine($"{label}: no capture"); return; }
        var theirs = File.ReadAllBytes(file);
        if (theirs.Length != mine.Length) { Console.WriteLine($"{label}: capture has {theirs.Length} cells, port {mine.Length}"); return; }
        int differ = 0;
        var byValue = new Dictionary<(int, int), int>();
        for (int i = 0; i < mine.Length; i++)
            if (mine[i] != theirs[i])
            {
                if (differ++ < 15) Console.WriteLine($"  {label} ({i % walk.Width}, {i / walk.Width}): client {theirs[i]}, port {mine[i]}");
                byValue[(theirs[i], mine[i])] = byValue.GetValueOrDefault((theirs[i], mine[i])) + 1;
            }
        Console.WriteLine($"{label}: {differ} of {mine.Length} cells differ " +
            string.Join(" ", byValue.OrderByDescending(p => p.Value).Take(8).Select(p => $"[client {p.Key.Item1} port {p.Key.Item2}: {p.Value}]")));
        // The tile entries with the most differing cells: the tile, its sub-tile flags and unwalkable vertices.
        var perTile = new Dictionary<(int, int), int>();
        for (int i = 0; i < mine.Length; i++)
            if (mine[i] != theirs[i]) perTile[(i % walk.Width / 23, i / walk.Width / 23)] = perTile.GetValueOrDefault((i % walk.Width / 23, i / walk.Width / 23)) + 1;
        foreach (var ((tx, ty), n) in perTile.OrderByDescending(p => p.Value).Take(12))
        {
            int flagged = 0;
            for (int v = 0; v < 2; v++) for (int u = 0; u < 2; u++) if ((grid.Corner(tx + u, ty + v).Flags & 8) != 0) flagged++;
            var e = grid[tx, ty];
            var sub = e is { } en ? files.Walk(en.Tile.Path)?.SubTiles.ElementAtOrDefault(0) : null;
            Console.WriteLine($"    tile ({tx}, {ty}): {n} cells, {(e is { } ee ? $"{Path.GetFileName(ee.Tile.Path)} o{ee.Orientation} +({ee.OffsetX},{ee.OffsetY})" : "empty")}" +
                $" F 0x{sub?.F:x2} G 0x{sub?.G:x2} flag-8 vertices {flagged}");
        }
    }
    Compare("walk", walk.Walk, stem + ".bin");
    Compare("projectile", walk.Projectile, stem + ".projectile.bin");
}

// The trace's 'n' / 'u' / 'l' draws, without the LCG draws of 0x142063a60 (call site 0x142063b9c): an animated
// entity's random start time (rand() bits × length / 256), made when the doodad is created. It reads only the LCG,
// which 0x142046a70 reseeds from the MT before each pick, so it moves nothing in the generation.
static List<(char Kind, string Caller, uint Arg, uint Result)> TraceDraws(JsonElement t) =>
    t.GetProperty("draws").EnumerateArray()
        .Where(d => d[0].GetString() is "n" or "u" or "l" && d[1].GetString() != "0x142063b9c")
        .Select(d => (Kind: d[0].GetString()![0], Caller: d[1].GetString()!, Arg: d[2].GetUInt32(), Result: d[3].GetUInt32())).ToList();

static GameFileSystem OpenGame()
{
    string root = Root();
    var oodle = new NodeOodleDecoder("node", Path.Combine(root, @"Tools\ooz\ooz_worker.mjs"));
    return GameFileSystem.Open(Path.Combine(root, "Client", "Content.ggpk"), oodle);
}

static void Generate(string areaId, uint seed, string? tracePath)
{
    string root = Root();
    using var fs = OpenGame();
    var files = new TerrainCache(p => fs.TryReadFile(p, out var d) ? d : null);
    int level = 1;
    if (tracePath is not null)
        using (var head = JsonDocument.Parse(File.ReadAllText(tracePath))) level = head.RootElement.TryGetProperty("level", out var l) ? l.GetInt32() : 1;
    var area = AreaInfo.Load(p => fs.TryReadFile(p, out var d) ? d : null, areaId, level);

    GenerationResult? result = null;
    var rng = new LevelRandom(seed, area.RandomnessDisabled) { Log = [] };
    var notes = new List<string>();
    TileGrid? partial = null;
    var stages = new List<(string Name, int Draws, List<string> Nodes, List<string> Edges)>();
    void OnStage(string name, TileGraph g) => stages.Add((name, rng.Log!.Count, g.Nodes.Select(n => NodeText(g, n)).ToList(), g.Edges.Select(e => EdgeText(g, e)).ToList()));
    // The view's (w + 1) x (h + 1) vertices, row by row (as the trace's ox, oy, w, h).
    static List<(string? Ground, int Height, int Flags)> ViewVertices(TileGrid g, int ox, int oy, int w, int h)
    {
        var list = new List<(string?, int, int)>();
        for (int y = oy; y <= oy + h; y++)
            for (int x = ox; x <= ox + w; x++)
                list.Add((g.Corner(x, y).Ground, g.HeightAt(x, y), g.Corner(x, y).Flags));
        return list;
    }
    List<(string? Ground, int Height, int Flags)>? fillCopy = null, paintCopy = null;
    LevelGenerator.FillStarting = (g, ox, oy, w, h) => fillCopy ??= ViewVertices(g, ox, oy, w, h);
    LevelGenerator.PaintStarting = (g, ox, oy, w, h) => paintCopy ??= ViewVertices(g, ox, oy, w, h);
    // Per layout (a composite map has one per subgraph): its stages, and its rooms once picked (which ends it).
    var dungeonLayouts = new List<(List<(string Name, int Draws, int[] Cells)> Stages, List<string>? Rooms)> { ([], null) };
    LevelGenerator.DungeonStage = (name, draws, cells) => dungeonLayouts[^1].Stages.Add((name, draws, cells));
    LevelGenerator.DungeonRooms = rooms =>
    {
        dungeonLayouts[^1] = (dungeonLayouts[^1].Stages, rooms);
        dungeonLayouts.Add(([], null));
    };
    try { result = LevelGenerator.Generate(files, area, seed, rng: rng, notes: notes, onStage: OnStage); }
    catch (Exception e) when (e is NotSupportedException or InvalidOperationException)
    {
        foreach (var n in notes) Console.WriteLine("  " + n);
        Console.WriteLine($"stopped: {e.Message}");
        partial = (e as PartialGenerationException)?.Grid;
    }
    if (result is not null)
    {
        foreach (var n in notes) Console.WriteLine("  " + n);
        Console.WriteLine($"{areaId} seed {seed}: {result.Grid.Width}x{result.Grid.Height}, {result.Random.Draws} draws, tile hash {result.Grid.Hash()}");
    }
    if (tracePath is null) return;

    using var trace = JsonDocument.Parse(File.ReadAllText(tracePath));
    var t = trace.RootElement;
    Console.WriteLine($"client: {t.GetProperty("w")}x{t.GetProperty("h")}, tile hash {t.GetProperty("tileHash")}");
    // Draw by draw: the client's 'n' / 'u' / 'l' entries against the port's, until the first difference.
    var theirs = TraceDraws(t);
    var mine = rng.Log!;
    int same = 0;
    while (same < theirs.Count && same < mine.Count && theirs[same].Kind == mine[same].Kind && theirs[same].Arg == mine[same].Arg && theirs[same].Result == mine[same].Result) same++;
    Console.WriteLine($"draws: {same} of {theirs.Count} match (port made {mine.Count})");
    // LEVELGEN_AROUND=n prints the draws around the first difference, both sides.
    if (int.TryParse(Environment.GetEnvironmentVariable("LEVELGEN_AROUND"), out int around))
        for (int i = Math.Max(0, same - around); i < same + around; i++)
            Console.WriteLine($"  #{i}: client " + (i < theirs.Count ? $"{theirs[i].Kind}({theirs[i].Arg}) = {theirs[i].Result} {theirs[i].Caller}" : "-") +
                "  port " + (i < mine.Count ? $"{mine[i].Kind}({mine[i].Arg}) = {mine[i].Result}" : "-"));
    if (result is null && same == mine.Count) Console.WriteLine("  every draw the port made before stopping matches");
    if (same < theirs.Count)
    {
        var c = theirs[same];
        Console.WriteLine($"  first difference at #{same}: client {c.Kind}({c.Arg}) = {c.Result} from {c.Caller}" +
            (same < mine.Count ? $", port {mine[same].Kind}({mine[same].Arg}) = {mine[same].Result}" : ", port made no more draws"));
    }
    CompareGraphs(t, stages);
    // The trace's layouts: `dungeons` (every layout of a composite map), else the one `dungeon`.
    var traceLayouts = t.TryGetProperty("dungeons", out var dgs) && dgs.ValueKind == JsonValueKind.Array ? dgs.EnumerateArray().ToList()
        : t.TryGetProperty("dungeon", out var dg1) && dg1.ValueKind == JsonValueKind.Object ? [dg1] : new List<JsonElement>();
    for (int li = 0; li < traceLayouts.Count; li++)
    {
        var dg = traceLayouts[li];
        if (traceLayouts.Count > 1) Console.WriteLine($"dungeon layout {li} (client draw {dg.GetProperty("draws")}):");
        if (li >= dungeonLayouts.Count || dungeonLayouts[li].Stages.Count == 0 && dungeonLayouts[li].Rooms is null)
        {
            Console.WriteLine("  not reached by the port");
            continue;
        }
        CompareDungeonStages(dg, dungeonLayouts[li].Stages);
        var dungeonRooms = dungeonLayouts[li].Rooms;
    // The layout's rooms once picked: trace rows are [room, x, y, w, h, orientation, level, kind, flags, ?, exits].
    if (dungeonRooms is not null && dg.TryGetProperty("rooms", out var dr))
    {
        var theirsRooms = dr.EnumerateArray().Select(r =>
            $"{(r[0].ValueKind == JsonValueKind.Null ? "blank" : Path.GetFileName(r[0].GetString()))} ({r[1]}, {r[2]}) {r[3]}x{r[4]} o{r[5]} l{r[6]} k{r[7]} f{r[8]} " +
            $"[{string.Join(" ", r[10].EnumerateArray().Select(e => e[0].ValueKind == JsonValueKind.Null ? "-" : $"{Path.GetFileNameWithoutExtension(e[0].GetString())}:{e[1]}:{e[2]}"))}]").ToList();
        int differ = 0;
        for (int i = 0; i < Math.Max(theirsRooms.Count, dungeonRooms.Count); i++)
        {
            string a = i < theirsRooms.Count ? theirsRooms[i] : "(none)", b = i < dungeonRooms.Count ? dungeonRooms[i] : "(none)";
            if (a == b) continue;
            if (differ++ < 10) Console.WriteLine($"  room {i}: client {a}\n          port   {b}");
        }
        Console.WriteLine($"dungeon rooms: {differ} of {theirsRooms.Count} differ");
    }
    }
    // The view's vertices against the port's (ground, height, flags 1 / 2 / 8): as the fill starts (the trace's
    // fillVertices) and around the level painting (paint.after, the client after 0x142110d60, the port before it: a port
    // vertex without a ground there is one the painting will give a ground).
    void CompareVertices(string label, JsonElement fv, List<(string? Ground, int Height, int Flags)> mine, bool beforePaint)
    {
        var names = fv.GetProperty("grounds").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.GetString());
        int vw = fv.GetProperty("w").GetInt32() + 1, k = 0, bad = 0;
        foreach (var v in fv.GetProperty("v").EnumerateArray())
        {
            int x = k % vw, y = k / vw;
            string? cg = names.GetValueOrDefault(v[0].GetInt32().ToString());
            int ch = v[1].GetInt32(), cf = v[2].GetInt32() & 0xb;
            var (pg, ph, pf) = k < mine.Count ? mine[k] : (null, 0, 0);
            k++;
            bool groundSame = string.Equals(cg, pg, StringComparison.OrdinalIgnoreCase) || ((cf & 1) == 0 && (pf & 1) == 0) ||
                (beforePaint && ((pf & 1) == 0 || pg is null));
            int flagMask = beforePaint ? 0xa : 0xb;
            if (groundSame && (pf & flagMask) == (cf & flagMask) && ph == ch) continue;
            if (bad++ < 30)
                Console.WriteLine($"  vertex ({x}, {y}): client {Path.GetFileNameWithoutExtension(cg ?? "null")} h{ch} f{cf}, port {Path.GetFileNameWithoutExtension(pg ?? "null")} h{ph} f{pf & 0xb}");
        }
        Console.WriteLine($"{label}: {bad} of {k} differ");
    }
    if (paintCopy is not null && t.TryGetProperty("paint", out var pt) && pt.ValueKind == JsonValueKind.Object &&
        pt.TryGetProperty("after", out var pa) && pa.ValueKind == JsonValueKind.Object)
        CompareVertices("paint vertices", pa, paintCopy, beforePaint: true);
    if (fillCopy is not null && t.TryGetProperty("fillVertices", out var fv) && fv.ValueKind == JsonValueKind.Object)
        CompareVertices("fill vertices", fv, fillCopy, beforePaint: false);
    var grid = result?.Grid ?? partial;
    if (grid is null) return;
    // LEVELGEN_DUMP=<file>: the port's grid entries as the trace's tiles ([x, y, tile, height, orientation, ox, oy]).
    if (Environment.GetEnvironmentVariable("LEVELGEN_DUMP") is { Length: > 0 } dump)
    {
        var entries = new List<object?[]>();
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                if (grid[x, y] is { } m) entries.Add([x, y, m.Tile.Path, (int)m.HeightOffset, m.Orientation, m.OffsetX, m.OffsetY]);
        File.WriteAllText(dump, JsonSerializer.Serialize(new { w = grid.Width, h = grid.Height, tiles = entries }));
    }
    // Tile by tile (for a partial grid, only the entries the port filled).
    int diffs = 0, compared = 0, heightOnly = 0;
    foreach (var e in t.GetProperty("tiles").EnumerateArray())
    {
        int x = e[0].GetInt32(), y = e[1].GetInt32();
        var m = grid[x, y];
        if (result is null && m is null) continue;
        compared++;
        string? path = e[2].GetString();
        bool sameTile = m is { } me && me.Tile.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && me.Orientation == e[4].GetInt32() &&
            me.OffsetX == e[5].GetInt32() && me.OffsetY == e[6].GetInt32();
        if (sameTile && m!.Value.HeightOffset == e[3].GetInt32()) continue;
        if (sameTile) heightOnly++;
        // Every differing tile once, at its origin on either side.
        bool origin = (e[5].GetInt32() == 0 && e[6].GetInt32() == 0) || m is { IsOrigin: true };
        if (diffs++ < 400 && origin)
            Console.WriteLine($"  ({x}, {y}): client {Path.GetFileName(path)} o{e[4]} h{e[3]} +({e[5]},{e[6]}), port " +
                (m is { } p ? $"{Path.GetFileName(p.Tile.Path)} o{p.Orientation} h{p.HeightOffset} +({p.OffsetX},{p.OffsetY})" : "empty"));
    }
    Console.WriteLine($"tiles: {diffs} of {compared} entries differ ({heightOnly} in height only)");
}

// The port's dungeon layout cells at each stage next to the client's (`dungeon.stages` in the trace: name, draws, cells),
// matched by name and occurrence: the first differing cells per stage, as maps of room numbers.
static void CompareDungeonStages(JsonElement d, List<(string Name, int Draws, int[] Cells)> mine)
{
    if (!d.TryGetProperty("stages", out var st)) return;
    int w = d.GetProperty("w").GetInt32();
    var seen = new Dictionary<string, int>();
    foreach (var s in st.EnumerateArray())
    {
        string name = s[0].GetString()!;
        int k = seen[name] = seen.GetValueOrDefault(name, -1) + 1;
        var theirs = s[2].EnumerateArray().Select(c => c.GetInt32()).ToArray();
        var m = mine.Where(x => x.Name == name).Skip(k).FirstOrDefault();
        if (m.Name is null)
        {
            if (mine.Any(x => x.Name == name) || mine.Count != 0 && k == 0) Console.WriteLine($"dungeon {name}#{k} (client draw {s[1]}): not reached by the port");
            continue;
        }
        int diff = Enumerable.Range(0, Math.Max(theirs.Length, m.Cells.Length)).Count(i => i >= theirs.Length || i >= m.Cells.Length || theirs[i] != m.Cells[i]);
        Console.WriteLine($"dungeon {name}#{k}: client draw {s[1]}, port {m.Draws}" + (diff == 0 ? ", same cells" : $", {diff} cells differ"));
        if (diff == 0) continue;
        string Rows(int[] c) => string.Join("\n", c.Chunk(w).Select(r => "    " + string.Join(" ", r.Select(v => v.ToString().PadLeft(2)))));
        Console.WriteLine("  client:\n" + Rows(theirs) + "\n  port:\n" + Rows(m.Cells));
    }
}

// The port's graph at each stage next to the client's (`graphs` in the trace, walkmaps.js hookGraphStages): the first
// differing node and edge per stage.
static void CompareGraphs(JsonElement trace, List<(string Name, int Draws, List<string> Nodes, List<string> Edges)> mine)
{
    if (!trace.TryGetProperty("graphs", out var graphs)) return;
    var seen = new Dictionary<string, int>();
    foreach (var g in graphs.EnumerateArray())
    {
        string name = g[0].GetString()!;
        int k = seen[name] = seen.GetValueOrDefault(name, -1) + 1;
        var theirsNodes = g[2].EnumerateArray().Select(n => $"({n[0]}, {n[1]}) '{n[2].GetString()}' h{n[3]} slots [{string.Join(",", n[4].EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Null ? "-" : x.ToString()))}] placed {n[5]}").ToList();
        var theirsEdges = g[3].EnumerateArray().Select(e => $"{e[0]}->{e[1]} {Path.GetFileNameWithoutExtension(e[2].GetString() ?? "?")} pts [{string.Join(" ", e[3].EnumerateArray().Select(p => $"({p[0]}, {p[1]})"))}] flags [{string.Join(",", e[4].EnumerateArray())}] v [{string.Join(",", e[5].EnumerateArray())}] off [{string.Join(",", e[6].EnumerateArray())}] w [{string.Join(",", e[7].EnumerateArray())}]").ToList();
        var m = mine.Where(s => s.Name == name).Skip(k).FirstOrDefault();
        if (m.Name is null)
        {
            Console.WriteLine($"graph {name}#{k} (client draw {g[1]}): {theirsNodes.Count} nodes, {theirsEdges.Count} edges; not reached by the port");
            continue;
        }
        var nds = Diffs(theirsNodes, m.Nodes);
        var eds = Diffs(theirsEdges, m.Edges);
        Console.WriteLine($"graph {name}#{k}: client {theirsNodes.Count} nodes / {theirsEdges.Count} edges at draw {g[1]}, port {m.Nodes.Count} / {m.Edges.Count} at {m.Draws}" +
            (nds.Count == 0 && eds.Count == 0 ? ", same" : $", {nds.Count} nodes and {eds.Count} edges differ"));
        foreach (int i in nds.Take(6))
        {
            Console.WriteLine($"  node {i}: client {At(theirsNodes, i)}");
            Console.WriteLine($"           port   {At(m.Nodes, i)}");
        }
        foreach (int i in eds.Take(6))
        {
            Console.WriteLine($"  edge {i}: client {At(theirsEdges, i)}");
            Console.WriteLine($"           port   {At(m.Edges, i)}");
        }
    }
    static List<int> Diffs(List<string> a, List<string> b) =>
        Enumerable.Range(0, Math.Max(a.Count, b.Count)).Where(i => At(a, i) != At(b, i)).ToList();
    static string At(List<string> l, int i) => i < l.Count ? l[i] : "(none)";
}

static string NodeText(TileGraph g, GameServer.Data.Terrain.Generation.GraphNode n) =>
    $"({n.X}, {n.Y}) '{n.Tag}' h{n.Height} slots [{string.Join(",", n.Slots.Select(e => e is null ? "-" : g.Edges.IndexOf(e).ToString()))}] placed {(n.Placed ? 1 : 0)}";

static string EdgeText(TileGraph g, GraphEdge e) =>
    $"{g.Nodes.IndexOf(e.A)}->{g.Nodes.IndexOf(e.B)} {Path.GetFileNameWithoutExtension(e.Type)} pts [{string.Join(" ", e.Points.Select(p => $"({p.X}, {p.Y})"))}] " +
    $"flags [{(e.VirtualEndSet[0] ? 1 : 0)},{(e.VirtualEndSet[1] ? 1 : 0)},{(e.Flag4a ? 1 : 0)},{(e.Split ? 1 : 0)}] " +
    $"v [{e.VirtualEnd1[0].X},{e.VirtualEnd1[0].Y},{e.VirtualEnd2[0].X},{e.VirtualEnd2[0].Y},{e.VirtualEnd1[1].X},{e.VirtualEnd1[1].Y},{e.VirtualEnd2[1].X},{e.VirtualEnd2[1].Y}] " +
    $"off [{e.StartOffset.X},{e.StartOffset.Y},{e.EndOffset.X},{e.EndOffset.Y}] w [{e.MinWidth},{e.MaxWidth}]";

static string Root()
{
    string root = AppContext.BaseDirectory;
    while (!File.Exists(Path.Combine(root, "Tools", "ooz", "ooz_worker.mjs")))
        root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar))
            ?? throw new InvalidOperationException("Tools/ooz/ooz_worker.mjs not found above the build output");
    return root;
}
