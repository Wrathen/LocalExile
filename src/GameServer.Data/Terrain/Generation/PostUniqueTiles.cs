namespace GameServer.Data.Terrain.Generation;

/// <summary><c>0x14203ece0</c> (after the mirror seams, before the outer buffer): each post-unique tile of the TileSet
/// (<see cref="TileStore.PostUnique"/>) replaces one placed tile with the same key. Row by row over the view, every
/// tile origin without a tag (tile data +0x10: the length of the tag string at +0), with a corner vertex inside the map
/// and walkable (no vertex flag 8), whose turned key equals the post-unique tile's key under some allowed orientation is a candidate: its orientation is drawn among the
/// matching ones (<c>0x14203f560</c>, <c>Next(count)</c>). Then <c>Next(candidates)</c> picks the place; the tile goes
/// in there at the old tile's height. No candidate is fatal.</summary>
static class PostUniqueTiles
{
    public static void Run(LevelRandom rng, TileGrid grid, int x0, int y0, int width, int height, TileStore store, List<string> notes)
    {
        bool Open(int x, int y) => x >= 0 && y >= 0 && x <= grid.Width && y <= grid.Height && (grid.Corner(x, y).Flags & 8) == 0;
        foreach (var (tile, mask) in store.PostUnique)
        {
            var keys = new TileKey?[8];
            for (int o = 0; o < 8; o++)
                if ((mask & 1 << o) != 0) keys[o] = tile.Key.Rotate(o);
            var found = new List<(short Height, int Orientation, int X, int Y)>();
            for (int y = y0; y < y0 + height; y++)
                for (int x = x0; x < x0 + width; x++)
                {
                    if (grid[x, y] is not { IsOrigin: true } e || e.Tile.Tag.Length != 0) continue;
                    var key = e.Tile.Key.Rotate(e.Orientation);
                    int w = key.Width, h = key.Height;
                    if (!Open(x, y) && !Open(x, y + h) && !Open(x + w, y) && !Open(x + w, y + h)) continue;
                    var matches = Enumerable.Range(0, 8).Where(o => keys[o] is { } k && k.Equals(key)).ToList();
                    if (matches.Count == 0) continue;
                    found.Add((e.HeightOffset, matches[(int)rng.Next((uint)matches.Count)], x, y));
                }
            if (found.Count == 0) throw new InvalidOperationException($"Failed to place post-unique tile {tile.Path}");
            var c = found[(int)rng.Next((uint)found.Count)];
            grid.Place(tile, c.Orientation, c.X, c.Y, c.Height);
            notes.Add($"post-unique {Path.GetFileName(tile.Path)} o{c.Orientation} at ({c.X}, {c.Y}) of {found.Count}");
        }
    }
}
