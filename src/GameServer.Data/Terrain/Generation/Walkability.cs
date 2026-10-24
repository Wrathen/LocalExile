using System.Buffers.Binary;

namespace GameServer.Data.Terrain.Generation;

/// <summary>One sub-tile (one 23 × 23 cell slot of a .tdt) as the walkability build reads it: flags F (record +4) and
/// G (+5) and the LZ4-compressed hull planes (+0x20). F bit 1: walking is uniform (no hull for it), bit 2: projectiles
/// are uniform, bit 4: no byte map, bit 8: not walkable, 0x10: stops projectiles, 0x20 / 0x40 / 0x80: the hull reaches
/// 4 cells past the top / right / bottom edge; G bit 1: past the left edge. A hull is one byte per cell per plane, the
/// low nibble the walk value (0 … 5), the high nibble the projectile value.</summary>
public sealed record SubTileWalk(byte F, byte G, byte[]? Hull)
{
    public int X0 => (G & 1) != 0 ? -4 : 0;
    public int Y0 => (F & 0x20) != 0 ? -4 : 0;
    public int HullWidth => 23 + ((F & 0x40) != 0 ? 4 : 0) + ((G & 1) != 0 ? 4 : 0);
    public int HullHeight => 23 + ((F & 0x20) != 0 ? 4 : 0) + ((F & 0x80) != 0 ? 4 : 0);

    /// <summary>A tile made of sub-tiles (flags word 0x200): which of the tile's meshes (the ';' list of header u32 4)
    /// fills this slot (tile entry +0x36 bits 2–7, the first of the record's three extra bytes, <c>0x141d7d860</c>).
    /// G bit 2 / 4 then say the slot continues the sub-tile on its left / above, bit 8 that it is empty.</summary>
    public int Mesh { get; init; }
    /// <summary>The sub-tile's placement code in the tile (+0x37 bits 5–7, the second extra byte).</summary>
    public int Code { get; init; }
}

/// <summary>A tile's walkability data (<c>0x1429f31a0</c>): the hull plane masks (tile data +0x172 count − 1, +0x173
/// masks: a plane is taken by the orientation codes in its mask, or by all when the mask is 0) and the sub-tiles in the
/// tile's own frame, row by row.</summary>
public sealed record TileWalk(byte[] PlaneMasks, SubTileWalk[] SubTiles)
{
    /// <summary>The walkability part of a .tdt without a base tile (null for one with a base tile, or past the end).
    /// After the 58-byte key: u16 flags (bit 0x100 from version 14, 0x200 from 13), from version 10 the orientation
    /// restriction byte and a hull plane count (0 … 4, then one mask per plane when 2 or more), with flag 0x200 a byte,
    /// then u16 sub-tile count and per sub-tile: u16 F | G &lt;&lt; 8 (u8 F before version 10), u8, with flag 0x200 three
    /// bytes, a byte map unless F has bit 4 (<c>0x1429f59a0</c>), and unless F has bits 1 and 2 a u16 length and the
    /// compressed hull.</summary>
    public static TileWalk? Parse(byte[] data)
    {
        try
        {
            int version = (int)BinaryPrimitives.ReadUInt32LittleEndian(data);
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
            int p = 8 + 2 * length;
            uint baseTile = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p));
            if (baseTile != uint.MaxValue && baseTile < length && (data[8 + 2 * baseTile] | data[9 + 2 * baseTile]) != 0) return null;
            p += 58;
            int flags = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p));
            p += 2;
            if (version < 14) flags &= ~0x100;
            if (version < 13) flags &= ~0x200;
            if (version > 9) p++;
            byte[] masks = [0];
            if (version >= 10)
            {
                int planes = data[p++];
                if (planes >= 2) { masks = data[p..(p + planes)]; p += planes; }
            }
            if ((flags & 0x200) != 0) p++;
            int count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p));
            p += 2;
            var subs = new SubTileWalk[count];
            for (int i = 0; i < count; i++)
            {
                byte f = data[p++], g = version >= 10 ? data[p++] : (byte)0;
                p++;
                // The three extra bytes: mesh index, placement code, height step (+0x37 low nibble and 0x10, not ported).
                int mesh = 0, code = 0;
                if ((flags & 0x200) != 0) { mesh = data[p]; code = data[p + 1] & 7; p += 3; }
                if ((f & 4) == 0)
                {
                    if (version < 10) p += 0x211;
                    else
                    {
                        // 0x1429f59a0: bits 0-2 compression (0: one value), bits 3-4 extra length bytes, bits 5-7 the
                        // low bits of the value (the compressed length).
                        byte head = data[p++];
                        int value = head >> 5;
                        for (int k = 0; k < ((head >> 3) & 3); k++) value |= data[p++] << (3 + 8 * k);
                        if ((head & 7) != 0) p += value;
                    }
                }
                if (version < 10 && (f & 3) != 3) { f |= 0xe0; g |= 1; }
                byte[]? hull = null;
                if ((f & 3) != 3)
                {
                    int n = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p));
                    hull = data[(p + 2)..(p + 2 + n)];
                    p += 2 + n;
                }
                subs[i] = new SubTileWalk(f, g, hull) { Mesh = mesh, Code = code };
            }
            return new TileWalk(masks, subs);
        }
        catch (ArgumentOutOfRangeException) { return null; }
        catch (IndexOutOfRangeException) { return null; }
    }

    /// <summary>The hull plane a placement code takes: the first whose mask is 0 or has the code, else the last.</summary>
    public int Plane(int code)
    {
        for (int i = 0; i < PlaneMasks.Length; i++)
            if (PlaneMasks[i] == 0 || (PlaneMasks[i] & 1 << (code & 0x1f)) != 0) return i;
        return PlaneMasks.Length - 1;
    }
}

/// <summary>The terrain walkability and projectile grids (<c>0x141d85970</c>, map +0xd0 / +0xe8 as nibbles): one value
/// per cell, 23 cells per tile, 5 far from anything that blocks, falling to 1 next to it, 0 blocked. The client walks
/// on 2 and up. Each tile entry lays its sub-tile's hull plane (turned by the entry's code) over the grid, keeping the
/// lower value; an empty entry, or a tile whose flags or four unwalkable vertices (flag 8) block it, lays a blocked
/// patch that falls off by 1 per cell into neighbours it does not share the block with.</summary>
public sealed partial class Walkability
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Walk { get; }
    public byte[] Projectile { get; }

    Walkability(int w, int h)
    {
        (Width, Height) = (w, h);
        Walk = new byte[w * h];
        Projectile = new byte[w * h];
        Array.Fill(Walk, (byte)5);
        Array.Fill(Projectile, (byte)5);
    }

    public static Walkability Build(TileGrid grid, Func<TileDef, TileWalk?> walkData)
    {
        var map = new Walkability(grid.Width * 23, grid.Height * 23);
        var hulls = new Dictionary<SubTileWalk, byte[]>(ReferenceEqualityComparer.Instance);

        SubTileWalk? Sub(int x, int y)
        {
            if (grid[x, y] is not { } e || walkData(e.Tile) is not { } data) return null;
            // The entry's sub-tile in the tile's own frame: the one the orientation puts at the entry's offset.
            for (int v = 0; v < e.Tile.Height; v++)
                for (int u = 0; u < e.Tile.Width; u++)
                    if (Orientation.Map(e.Orientation, u, v, e.Tile.Width, e.Tile.Height) == (e.OffsetX, e.OffsetY))
                        return v * e.Tile.Width + u < data.SubTiles.Length ? data.SubTiles[v * e.Tile.Width + u] : null;
            return null;
        }

        // 0x141d883c0: how many of the entry's four vertices are unwalkable (outside the map counts).
        int Unwalkable(int x, int y)
        {
            int n = 0;
            for (int v = 0; v < 2; v++)
                for (int u = 0; u < 2; u++)
                    if (!grid.InsideCorners(x + u, y + v) || (grid.Corner(x + u, y + v).Flags & 8) != 0) n++;
            return n;
        }

        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                if (grid[x, y] is not { } e) { map.Blocked(grid, Sub, x, y, 0x18); continue; }
                var sub = Sub(x, y);
                var data = walkData(e.Tile);
                if (sub is null || data is null) continue;
                int f = sub.F;
                if ((f & 2) == 0)
                {
                    int plane = data.Plane(e.Orientation);
                    int corners = Unwalkable(x, y);
                    if ((f & 1) == 0 && corners < 4) map.Stamp(sub, Hull(sub), plane, e.Orientation, x, y, true, true);
                    else
                    {
                        if ((f & 8) != 0 || corners > 3) map.Blocked(grid, Sub, x, y, 0x08);
                        map.Stamp(sub, Hull(sub), plane, e.Orientation, x, y, false, true);
                    }
                }
                else if ((f & 0x10) != 0) map.Blocked(grid, Sub, x, y, 0x18);
                else
                {
                    int corners = Unwalkable(x, y);
                    if ((f & 1) == 0 && corners < 4) map.Stamp(sub, Hull(sub), data.Plane(e.Orientation), e.Orientation, x, y, true, false);
                    else if ((f & 8) != 0 || corners > 3) map.Blocked(grid, Sub, x, y, 0x08);
                }
            }
        return map;

        byte[] Hull(SubTileWalk sub)
        {
            if (hulls.TryGetValue(sub, out var hull)) return hull;
            var output = new byte[sub.HullWidth * sub.HullHeight * 4];
            int n = sub.Hull is null ? -1 : Lz4.Decompress(sub.Hull, output);
            if (n < 0) throw new InvalidOperationException("DecompressSubTileHullPlane() failed to decompress hull data");
            return hulls[sub] = output[..n];
        }
    }

    /// <summary><c>0x141d7c840</c> and the lay-over loops of <c>0x141d85970</c>: the hull plane's cells, turned by the
    /// code around the 23 × 23 slot, at entry (x, y), clamped into the map, each layer kept at the lower value.</summary>
    void Stamp(SubTileWalk sub, byte[] hull, int plane, int code, int x, int y, bool walk, bool projectile)
    {
        int w = sub.HullWidth, h = sub.HullHeight, size = w * h;
        if (plane != 0 && (plane + 1) * size > hull.Length)
            throw new InvalidOperationException("DecompressSubTileHullPlane() had invalid plane index");
        int at = plane * size;
        for (int v = 0; v < h; v++)
            for (int u = 0; u < w; u++)
            {
                byte cell = hull[at + v * w + u];
                var (tx, ty) = Orientation.Map(code, u + sub.X0, v + sub.Y0, 23, 23);
                int gx = Math.Clamp(tx + 23 * x, 0, Width - 1), gy = Math.Clamp(ty + 23 * y, 0, Height - 1);
                int i = gy * Width + gx;
                if (walk) Walk[i] = Math.Min(Walk[i], (byte)(cell & 0xf));
                if (projectile) Projectile[i] = Math.Min(Projectile[i], (byte)(cell >> 4));
            }
    }

    /// <summary><c>0x141d867d0</c>: a blocked patch at entry (x, y), mask bit 8 for walking and 0x10 for projectiles.
    /// Toward each neighbour (up, right, down, left: bits 0x20, 0x40, 0x80, 0x100) that is a tile not blocked the same
    /// way it reaches 4 cells further, rising 1 … 4 away from the slot (<c>0x1429f53b0</c>); cells outside the map are
    /// left out.</summary>
    void Blocked(TileGrid grid, Func<int, int, SubTileWalk?> sub, int x, int y, int mask)
    {
        for (int d = 0; d < 4; d++)
        {
            var (dx, dy) = Orientation.Directions[d];
            if (!grid.Inside(x + dx, y + dy) || grid[x + dx, y + dy] is null) continue;
            int nf = sub(x + dx, y + dy)?.F ?? 0;
            if (((mask & 8) != 0 && (nf & 8) == 0) || ((mask & 0x10) != 0 && (nf & 0x10) == 0)) mask |= 0x20 << d;
        }
        bool walk = (mask & 8) != 0, projectile = (mask & 0x10) != 0;
        int x0 = (mask & 0x100) != 0 ? -4 : 0, x1 = (mask & 0x40) != 0 ? 27 : 23;
        int y0 = (mask & 0x20) != 0 ? -4 : 0, y1 = (mask & 0x80) != 0 ? 27 : 23;
        int w = x1 - x0, h = y1 - y0;
        // The patch: blocked layers 0, then the bands: top and bottom rows set, left and right columns raised.
        var wv = new byte[w * h];
        var pv = new byte[w * h];
        Array.Fill(wv, walk ? (byte)0 : (byte)5);
        Array.Fill(pv, projectile ? (byte)0 : (byte)5);
        void SetRow(int row, int value)
        {
            for (int c = 0; c < w; c++)
            {
                wv[row * w + c] = walk ? (byte)value : (byte)5;
                pv[row * w + c] = projectile ? (byte)value : (byte)5;
            }
        }
        void RaiseColumn(int col, int value)
        {
            for (int r = 0; r < h; r++)
            {
                wv[r * w + col] = walk ? Math.Max(wv[r * w + col], (byte)value) : (byte)5;
                pv[r * w + col] = projectile ? Math.Max(pv[r * w + col], (byte)value) : (byte)5;
            }
        }
        if ((mask & 0x20) != 0) for (int r = 0; r < 4; r++) SetRow(r, 4 - r);
        if ((mask & 0x80) != 0) for (int r = h - 4; r < h; r++) SetRow(r, r - h + 5);
        if ((mask & 0x100) != 0) for (int c = 0; c < 4; c++) RaiseColumn(c, 4 - c);
        if ((mask & 0x40) != 0) for (int c = w - 4; c < w; c++) RaiseColumn(c, c - w + 5);
        for (int v = 0; v < h; v++)
            for (int u = 0; u < w; u++)
            {
                int gx = 23 * x + x0 + u, gy = 23 * y + y0 + v;
                if (gx < 0 || gy < 0 || gx >= Width || gy >= Height) continue;
                int i = gy * Width + gx;
                if (walk) Walk[i] = Math.Min(Walk[i], wv[v * w + u]);
                if (projectile) Projectile[i] = Math.Min(Projectile[i], pv[v * w + u]);
            }
    }
}

/// <summary>An LZ4 block decoder (the client's <c>0x140e42cd0</c>, LZ4_decompress_safe).</summary>
public static class Lz4
{
    /// <summary>Decodes <paramref name="input"/> into <paramref name="output"/>; the decoded length, or −1 when the
    /// block is malformed or does not fit.</summary>
    public static int Decompress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        int ip = 0, op = 0;
        while (ip < input.Length)
        {
            int token = input[ip++];
            int literals = token >> 4;
            if (literals == 15)
                for (byte b = 255; b == 255 && ip < input.Length;) literals += b = input[ip++];
            if (ip + literals > input.Length || op + literals > output.Length) return -1;
            input.Slice(ip, literals).CopyTo(output[op..]);
            ip += literals;
            op += literals;
            if (ip >= input.Length) break;
            if (ip + 2 > input.Length) return -1;
            int offset = input[ip] | input[ip + 1] << 8;
            ip += 2;
            if (offset == 0 || offset > op) return -1;
            int match = (token & 15) + 4;
            if ((token & 15) == 15)
                for (byte b = 255; b == 255;)
                {
                    if (ip >= input.Length) return -1;
                    match += b = input[ip++];
                }
            if (op + match > output.Length) return -1;
            for (int k = 0; k < match; k++, op++) output[op] = output[op - offset];
        }
        return op;
    }
}
