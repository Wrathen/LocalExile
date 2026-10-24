using System.Text;
using GameServer.Data.Terrain.Generation;

namespace GameServer.Data.Tests;

public class LevelGenerationTests
{
    [Fact]
    public void LevelRandomIsStdMt19937()
    {
        // std::mt19937's default seed 5489: first output and the 10000th (the C++ standard's check value).
        var r = new LevelRandom(5489);
        Assert.Equal(3499211612u, r.NextRaw());
        for (int i = 2; i < 10000; i++) r.NextRaw();
        Assert.Equal(4123659995u, r.NextRaw());
    }

    [Fact]
    public void NextIsMsvcUniformInt()
    {
        // n = 2^k never rejects: the result is the draw's low bits. n ≤ 1 draws nothing.
        var a = new LevelRandom(1);
        var b = new LevelRandom(1);
        Assert.Equal(b.NextRaw() % 8, a.Next(8u));
        Assert.Equal(0u, a.Next(1u));
        Assert.Equal(0u, a.Next(0u));
        Assert.Equal(1, a.Draws);
        // A disabled area: Next is always 0 and draws nothing.
        var off = new LevelRandom(1, disabled: true);
        Assert.Equal(0u, off.Next(100u));
        Assert.Equal(0, off.Draws);
    }

    [Fact]
    public void ShuffleSwapsForward()
    {
        var r = new LevelRandom(7);
        var copy = new LevelRandom(7);
        var list = new List<int> { 0, 1, 2, 3 };
        r.Shuffle(list);
        var expected = new List<int> { 0, 1, 2, 3 };
        for (int i = 0; i < 4; i++)
        {
            int j = i + (int)copy.Uniform((uint)(4 - i));
            (expected[i], expected[j]) = (expected[j], expected[i]);
        }
        Assert.Equal(expected, list);
        Assert.Equal(3, r.Draws); // the last element (one left) draws nothing
    }

    [Fact]
    public void OrientationFromMaskPicksAllowedCodes()
    {
        Assert.Equal(0, LevelRandom.OrientationFromMask(LevelRandom.OrientationMask([0]), 123));
        Assert.Equal(5, LevelRandom.OrientationFromMask(0xff, 13));
        Assert.Equal(4, LevelRandom.OrientationFromMask(0b0001_0001, 3)); // codes {0, 4}, 3 % 2 = 1
    }

    [Fact]
    public void Murmur2MatchesReference()
    {
        // MurmurHash2 (32-bit) of an independent implementation, tails of 0..3 bytes.
        Assert.Equal(0u, TileHash.Murmur2([], 0));
        Assert.Equal(0x1c94221bu, TileHash.Murmur2(Encoding.ASCII.GetBytes("abc"), 0x9747b28c));
        Assert.Equal(0xbeba9b12u, TileHash.Murmur2(Encoding.ASCII.GetBytes("Hello, world!"), 0x9747b28c));
        Assert.Equal(0x1d84d036u, TileHash.Murmur2(Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog"), 0x9747b28c));
    }

    [Fact]
    public void FirstDrawsMatchLionsEyeWatchTrace()
    {
        // Client trace of 1_1_town seed 1 (live 2026-10-22): the topology pick's MT draw, then 10 LCG bits for the
        // orientation (0x142046a70); one topology with orientations [0] gives orientation 0.
        var r = new LevelRandom(1);
        var (index, orientation) = r.PickIndexAndOrientation(1, LevelRandom.OrientationMask([0]));
        Assert.Equal(0, index);
        Assert.Equal(0, orientation);
        Assert.Equal(1, r.Draws);
        Assert.Equal(1791095845u, new LevelRandom(1).Next(uint.MaxValue));
    }

    [Fact]
    public void TileHashOfOneTile()
    {
        // A one-entry grid hashes the tile's part as a single u32.
        uint part = TileHash.Tile("Metadata/Terrain/x.tdt", -2, 3, 1, 0);
        var bytes = BitConverter.GetBytes(part);
        Assert.Equal(TileHash.Murmur2(bytes, 0xf36b12), TileHash.Compute(2, 1, (x, y) => x == 1 ? ("Metadata/Terrain/x.tdt", (short)-2, (byte)3) : null));
    }

    [Fact]
    public void Lz4DecodesLiteralsAndOverlappingMatches()
    {
        // "abcd", then a match 4 back of length 8 (overlapping its own output), then the literal "e".
        byte[] block = [0x44, (byte)'a', (byte)'b', (byte)'c', (byte)'d', 4, 0, 0x10, (byte)'e'];
        var output = new byte[32];
        int n = Lz4.Decompress(block, output);
        Assert.Equal("abcdabcdabcde", Encoding.ASCII.GetString(output, 0, n));
        Assert.Equal(-1, Lz4.Decompress([0x40, (byte)'a', (byte)'b', (byte)'c', (byte)'d', 9, 0], output));
    }

    [Fact]
    public void HullPlaneFollowsTheOrientationMasks()
    {
        var walk = new TileWalk([0x01, 0x00], []);
        Assert.Equal(0, walk.Plane(0));
        Assert.Equal(1, walk.Plane(3));
        Assert.Equal(1, new TileWalk([0x01, 0x02], []).Plane(5));
    }

    [Fact]
    public void EmptyEntryBlocksAndFallsOffIntoItsNeighbour()
    {
        // An empty entry beside an open tile (both layers uniform, no hull): 0 over its slot, then 1 … 4 into the
        // neighbour's first four columns; the rest stays 5.
        var tile = new TileDef("open.tdt", "", new TileKey { Width = 1, Height = 1 });
        var grid = new TileGrid(2, 1);
        grid.Place(tile, 0, 1, 0, 0);
        var map = Walkability.Build(grid, _ => new TileWalk([0], [new SubTileWalk(0x03, 0, null)]));
        Assert.Equal((46, 23), (map.Width, map.Height));
        int row = 10 * map.Width;
        Assert.Equal(0, map.Walk[row + 22]);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, map.Walk[(row + 23)..(row + 28)]);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, map.Projectile[(row + 23)..(row + 28)]);
    }

    static TileKey Straight()
    {
        // Metadata/Terrain/Beach/Shoreline/beachtoocean_straight_v01_01.tdt: shoreline edges right and left, deep water on
        // the top corners, shallow on the bottom ones, the edge's point in the middle (4 of 0..8).
        var k = new TileKey { Width = 3, Height = 3 };
        k.Edges[1] = k.Edges[3] = "deep.et";
        k.Grounds[0] = k.Grounds[1] = "deep.gt";
        k.Grounds[2] = k.Grounds[3] = "shallow.gt";
        k.PointA[0] = 9; k.PointA[1] = 4; k.PointA[2] = 9; k.PointA[3] = 4;
        k.PointB[0] = k.PointB[1] = k.PointB[2] = k.PointB[3] = 9;
        return k;
    }

    [Fact]
    public void SymmetricTileMatchesInTwoOrientations()
    {
        // Lioneye's Watch: the client lists this tile under one key in orientations 0 and 4 (live probe of 0x141d902d0).
        var t = Straight();
        Assert.Equal(t, t.Rotate(4));
        Assert.NotEqual(t, t.Rotate(2));
        var turned = t.Rotate(1);
        Assert.Equal(new string?[] { "deep.et", null, "deep.et", null }, turned.Edges);
        Assert.Equal(new string?[] { "shallow.gt", "deep.gt", "deep.gt", "shallow.gt" }, turned.Grounds);
    }

    [Fact]
    public void PlacementCodesMapCells()
    {
        // Code 3 (R270) puts room cell (x, y) of an 18 × 30 room at (y, 17 − x): the town's encampment tile at room slot
        // (3, 3), 15 × 18, covers grid columns 3..20 and rows 0..14, so its origin is (3, 0).
        Assert.Equal((3, 14), Orientation.Map(3, 3, 3, 18, 30));
        Assert.Equal((0, 0), Orientation.Map(0, 0, 0, 3, 3));
        Assert.Equal((2, 0), Orientation.Map(4, 0, 0, 3, 3));
        Assert.Equal((3, 2), Orientation.Size(1, 2, 3));
        Assert.Equal(4, Orientation.MirrorAcross(0, 1));   // left/right neighbours: 0 mirrors to 4
        Assert.Equal(6, Orientation.MirrorAcross(0, 0));   // up/down neighbours: 0 mirrors to 6
    }

    [Fact]
    public void VerticesTurnWithTheirCells()
    {
        // Every code: the vertices of a cell land on the corners of where the cell lands.
        for (int o = 0; o < 8; o++)
        {
            var (cx, cy) = Orientation.Map(o, 1, 0, 3, 2);
            var corners = new[] { (1, 0), (2, 0), (1, 1), (2, 1) }.Select(v => Orientation.MapVertex(o, v.Item1, v.Item2, 3, 2)).ToHashSet();
            Assert.Equal(new HashSet<(int, int)> { (cx, cy), (cx + 1, cy), (cx, cy + 1), (cx + 1, cy + 1) }, corners);
        }
    }

    [Fact]
    public void TurnedVertexLookupUndoesThePlacement()
    {
        // 0x141d7ff50 reads a placed tile's vertex through the inverse code (0x142fb7f98): every vertex a 3 × 2 tile
        // writes at a turned position reads back from that position.
        var key = new TileKey { Width = 3, Height = 2 };
        var tile = new TileDef("t.tdt", "", key) { VertexHeights = [.. Enumerable.Range(0, 12).Select(i => (sbyte)i)] };
        for (int o = 0; o < 8; o++)
            for (int v = 0; v <= 2; v++)
                for (int u = 0; u <= 3; u++)
                {
                    var (du, dv) = Orientation.MapVertex(o, u, v, 3, 2);
                    Assert.Equal(tile.VertexHeight(u, v), tile.VertexHeightAt(o, du, dv));
                }
    }

    [Fact]
    public void EdgeAngleUsesTheClientsAcosTable()
    {
        // 0x14299d730 with the table at 0x142fe0b10 (32768 = π): same, perpendicular and opposite directions, and one
        // entry between (2 · dot = 20, root 22 → index 57 + 127 = 184 → 11529, as Game.exe holds it).
        Assert.Equal(0, GraphGeometry.Angle(0, -10, 0, -5));
        Assert.Equal(16384, GraphGeometry.Angle(0, -10, 7, 0));
        Assert.Equal(32768, GraphGeometry.Angle(10, 0, -3, 0));
        Assert.Equal(0, GraphGeometry.Angle(0, 0, 1, 1));
        Assert.Equal(11529, GraphGeometry.Angle(10, 0, 1, 2));
    }

    [Fact]
    public void PolarUsesTheClientsSineInterpolation()
    {
        // 0x14299d820 weighs the two table entries the other way round, so angle 0 reads the entry after it: sin 0 = 3/127.
        Assert.Equal((49, 1), GraphGeometry.Polar(100, 0));
        Assert.Equal((1, 49), GraphGeometry.Polar(100, 0x4000));
    }
}
