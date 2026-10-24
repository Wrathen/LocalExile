using System.Globalization;

namespace GameServer.Data.Terrain.Generation;

/// <summary>One section of a doodad's collision (the .ao's <c>Hull</c> component, parser <c>0x141e02690</c>, 0x80-byte
/// records): per layer (0 walk, 1 projectiles) its triangles (x, y three times, in cells around the doodad) and circles
/// (x, y, radius). Shapes before any <c>section</c> line go into the first, unnamed section.</summary>
public sealed class HullSection(string name)
{
    public string Name { get; } = name;
    public List<float[]>[] Triangles { get; } = [[], []];
    public List<(float X, float Y, float R)>[] Circles { get; } = [[], []];
}

/// <summary>A doodad's collision: the <c>Hull</c> block of its .ao (or of the .ao it extends): <c>walk_tri</c> /
/// <c>proj_tri</c> = "x1 y1 x2 y2 x3 y3", <c>walk_circle</c> / <c>proj_circle</c> = "x y r", <c>section</c> = "name".
/// The height shapes (<c>height_tri</c>, …) do not touch the walkability grids.</summary>
public sealed class DoodadHull
{
    public List<HullSection> Sections { get; } = [new("")];

    public bool IsEmpty => Sections.All(s => s.Triangles.All(l => l.Count == 0) && s.Circles.All(l => l.Count == 0));

    /// <summary>The Hull of an .ao, following <c>extends</c> while a file has none; null without one.</summary>
    public static DoodadHull? Load(Func<string, string?> text, string path)
    {
        for (int depth = 0; depth < 16 && path.Length > 0; depth++)
        {
            var source = text(path);
            if (source is null) return null;
            var lines = TerrainText.Lines(source);
            if (Parse(lines) is { } hull) return hull;
            string? parent = lines.Select(l => l.Trim()).Where(l => l.StartsWith("extends ", StringComparison.Ordinal))
                .Select(l => TerrainText.Tokenize(l).Skip(1).FirstOrDefault().Text).FirstOrDefault();
            if (parent is null || parent == "nothing") return null;
            path = parent.EndsWith(".ao", StringComparison.OrdinalIgnoreCase) ? parent : parent + ".ao";
        }
        return null;
    }

    static DoodadHull? Parse(string[] lines)
    {
        int at = Array.FindIndex(lines, l => l.Trim() == "Hull");
        if (at < 0) return null;
        var hull = new DoodadHull();
        int depth = 0;
        for (int i = at + 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line == "{") { depth++; continue; }
            if (line == "}") { if (--depth <= 0) break; continue; }
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim().Trim('"');
            float[] n = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => float.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
            var section = hull.Sections[^1];
            switch (key)
            {
                case "walk_tri" when n.Length >= 6: section.Triangles[0].Add(n[..6]); break;
                case "proj_tri" when n.Length >= 6: section.Triangles[1].Add(n[..6]); break;
                case "walk_circle" when n.Length >= 3: section.Circles[0].Add((n[0], n[1], n[2])); break;
                case "proj_circle" when n.Length >= 3: section.Circles[1].Add((n[0], n[1], n[2])); break;
                case "section": hull.Sections.Add(new HullSection(value)); break;
            }
        }
        return hull;
    }
}

/// <summary>One placed doodad as the walkability needs it: the .ao, its cell, angle (radians), scale and mirror.</summary>
public readonly record struct PlacedDoodad(string Model, int X, int Y, float Angle, float Scale = 1, bool Mirrored = false);

public sealed partial class Walkability
{
    /// <summary><c>0x141d876e0</c>: a doodad's collision. The walk grid takes its walk shapes, then both grids its
    /// projectile shapes (<c>0x141d91100</c>: each point turned by the angle — x mirrored first when mirrored — and
    /// scaled; triangles rasterised at 1/16 cell, <c>0x141d99da0</c>; circles as the cells within the radius).</summary>
    public void Stamp(DoodadHull hull, PlacedDoodad d)
    {
        foreach (var (grid, shapes) in new[] { (0, 0), (0, 1), (1, 1) })
            foreach (var section in hull.Sections)
            {
                float c = MathF.Cos(d.Angle) * d.Scale, s = MathF.Sin(d.Angle) * d.Scale;
                float xx = d.Mirrored ? -c : c, yx = -s, xy = d.Mirrored ? -s : s, yy = c;
                (float, float) Turn(float x, float y) => (x * xx + y * yx, x * xy + y * yy);
                int swap = d.Mirrored ? 1 : 0;
                foreach (var t in section.Triangles[shapes])
                {
                    var a = Turn(t[0], t[1]);
                    var b = Turn(t[2 + 2 * swap], t[3 + 2 * swap]);
                    var e = Turn(t[4 - 2 * swap], t[5 - 2 * swap]);
                    Triangle(a, b, e, (x, y) => Block(grid, d.X + x, d.Y + y));
                }
                foreach (var (cx, cy, r) in section.Circles[shapes])
                {
                    var (px, py) = Turn(cx, cy);
                    float radius = d.Scale * r;
                    int ox = (int)MathF.Floor(px + 0.5f), oy = (int)MathF.Floor(py + 0.5f);
                    int extent = (int)MathF.Floor(radius + 0.5f), squared = (int)MathF.Floor(radius * radius + 0.5f);
                    for (int dy = -extent; dy <= extent; dy++)
                        for (int dx = -extent; dx <= extent; dx++)
                            if (dx * dx + dy * dy < squared) Block(grid, d.X + ox + dx, d.Y + oy + dy);
                }
            }
    }

    /// <summary><c>0x141d99da0</c>: the cells a triangle covers, in 1/16 cells with a top-left rule (cell (x, y) is the
    /// sample at 16·x, 16·y), the corners put in counter-clockwise order first.</summary>
    static void Triangle((float X, float Y) p1, (float X, float Y) p2, (float X, float Y) p3, Action<int, int> cell)
    {
        static int F(float v) => (int)MathF.Floor(v * 16 + 0.5f);
        int x1 = F(p1.X), y1 = F(p1.Y), x2 = F(p2.X), y2 = F(p2.Y), x3 = F(p3.X), y3 = F(p3.Y);
        if (p2.X * p3.Y + p1.X * p2.Y + p3.X * p1.Y <= p1.X * p3.Y + p2.X * p1.Y + p3.X * p2.Y)
            (x1, y1, x3, y3) = (x3, y3, x1, y1);
        int dx12 = x2 - x1, dx23 = x3 - x2, dx31 = x1 - x3;
        int dy23 = y3 - y2, dy12 = y2 - y1, dy31 = y1 - y3;
        int minX = (Math.Min(Math.Min(x1, x2), x3) + 15) >> 4, maxX = (Math.Max(Math.Max(x1, x2), x3) + 15) >> 4;
        int minY = (Math.Min(Math.Min(y1, y2), y3) + 15) >> 4, maxY = (Math.Max(Math.Max(y1, y2), y3) + 15) >> 4;
        if (maxY == minY || maxX == minX) return;
        int c1 = x3 * dy23 - y3 * dx23, c2 = x2 * dy12 - y2 * dx12, c3 = x1 * dy31 - y1 * dx31;
        if (dy23 < 0 || (dy23 == 0 && dx23 > 0)) c1++;
        if (dy12 < 0 || (dy12 == 0 && dx12 > 0)) c2++;
        if (dy31 < 0 || (dy31 == 0 && dx31 > 0)) c3++;
        c1 += minY * 16 * dx23 - minX * 16 * dy23;
        c2 += minY * 16 * dx12 - minX * 16 * dy12;
        c3 += minY * 16 * dx31 - minX * 16 * dy31;
        for (int y = minY; y <= maxY; y++)
        {
            int e1 = c1, e2 = c2, e3 = c3;
            for (int x = minX; x <= maxX; x++)
            {
                if (e1 >= 0 && e2 >= 0 && e3 >= 0) cell(x, y);
                e1 -= dy23 * 16;
                e2 -= dy12 * 16;
                e3 -= dy31 * 16;
            }
            c1 += dx23 * 16;
            c2 += dx12 * 16;
            c3 += dx31 * 16;
        }
    }

    /// <summary><c>0x141d87da0</c>: a cell of grid 0 (walk) or 1 (projectiles) that is not blocked yet becomes 0, and
    /// the cells within 4 of it (Chebyshev) take their distance when lower.</summary>
    void Block(int grid, int x, int y)
    {
        var layer = grid == 0 ? Walk : Projectile;
        if (x < 0 || y < 0 || x >= Width || y >= Height || layer[y * Width + x] == 0) return;
        for (int ny = Math.Max(0, y - 4); ny < Math.Min(Height, y + 5); ny++)
            for (int nx = Math.Max(0, x - 4); nx < Math.Min(Width, x + 5); nx++)
            {
                int i = ny * Width + nx;
                layer[i] = Math.Min(layer[i], (byte)Math.Max(Math.Abs(nx - x), Math.Abs(ny - y)));
            }
    }
}
