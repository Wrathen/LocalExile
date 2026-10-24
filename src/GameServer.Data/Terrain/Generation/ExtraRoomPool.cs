namespace GameServer.Data.Terrain.Generation;

/// <summary>A row of Data/ExtraTerrainFeatures.dat as the pool holds it: id (+0), rooms (+8, .arm), tiles (+0x18,
/// .tdt / .tst), room orientations (+0x39, 8 = any), the linked row added with it (+0x49) and the oversize flag
/// (+0x61: a telepad injector's room, <c>0x142101e50</c>).</summary>
sealed record ExtraFeatureRow(string Name, IReadOnlyList<string> Rooms, IReadOnlyList<string> Tiles, IReadOnlyList<int> Orientations,
    int Link, bool Oversize)
{
    /// <summary><c>0x142102170</c>'s orientation mask of the row: 0 (any) when it lists 8, else a bit per orientation.</summary>
    public int Mask => Orientations.Contains(8) ? 0 : Orientations.Aggregate(0, (m, o) => m | 1 << (o & 31));
}

/// <summary>The generator's extra room pool (+0x40, <c>0x141d8ae40</c> …): the extra terrain features' rows waiting for
/// a place (+0x48, in the order the features rolled), the rooms injected into mission rooms (+0, 0x28-byte entries:
/// room, orientation, row), how many subgraph weight is left to share the rows over (+0x140) and how many rows the
/// current subgraph may place (+0x144).</summary>
sealed class ExtraRoomPool
{
    public readonly List<ExtraFeatureRow> Rows = [];
    public readonly List<(DungeonRoom Room, int Orientation, ExtraFeatureRow Row)> Injected = [];
    public int Total, Share;

    /// <summary>The shrine count the features rolled (generator +0x188 / pool +0x148), less the shrines placed.</summary>
    public int Shrines;

    /// <summary>The shrines placed (+0x14a).</summary>
    public int ShrinesPlaced;

    readonly RawDat? _table;

    ExtraRoomPool(RawDat? table) => _table = table;

    /// <summary><c>0x14204a570</c>'s adds (<c>0x141f28aa0</c>: by id): every rolled feature that is a row, in roll order.
    /// The features without a row (the Vaal side area flag, the discoverable hideout, the shrine count) stay out.</summary>
    public static ExtraRoomPool Create(Func<string, byte[]?> read, IEnumerable<ExtraTerrainFeatures.Feature> features)
    {
        var list = features.ToList();
        if (list.All(f => f.Name.StartsWith('(') || f.Name == ExtraTerrainFeatures.VaalSideArea)) return new(null) { Shrines = Count(list, "(shrines)") };
        var table = new RawDat(read("Data/ExtraTerrainFeatures.datc64") ?? throw new FileNotFoundException("Data/ExtraTerrainFeatures.datc64"));
        var pool = new ExtraRoomPool(table) { Shrines = Count(list, "(shrines)") };
        foreach (var f in list)
        {
            if (f.Name.StartsWith('(') || f.Name == ExtraTerrainFeatures.VaalSideArea) continue;
            int row = table.Find(r => table.String(r, 0) == f.Name);
            if (row < 0) throw new InvalidDataException($"no ExtraTerrainFeatures row {f.Name}");
            pool.Add(pool.Row(row), f.Count);
        }
        return pool;

        static int Count(List<ExtraTerrainFeatures.Feature> l, string name) => l.Where(f => f.Name == name).Sum(f => f.Count);
    }

    ExtraFeatureRow Row(int row) => new(_table!.String(row, 0), _table.StringList(row, 8), _table.StringList(row, 0x18),
        _table.I32List(row, 0x39), _table.Ref8(row, 0x49), _table.Bool(row, 0x61));

    /// <summary><c>0x141d8ae40</c>: <paramref name="count"/> copies of the row at the end of the list (its tiles go to the
    /// pool's own tile store, +0x90), then the row it links to, as many times. The Shrine row is capped by the shrine
    /// count: with the Shrine rows already waiting or injected (<c>0x141d8ace0</c>), at most that many; none, and the
    /// link is not added either.</summary>
    void Add(ExtraFeatureRow row, int count)
    {
        if (count == 0) return;
        if (row.Rooms.Count == 0) return;
        if (row.Name == Shrine)
        {
            int have = Rows.Count(r => r.Name == Shrine) + Injected.Count(i => i.Row.Name == Shrine);
            if (Shrines < have + count) count = Math.Max(0, Shrines - have);
            if (count == 0) return;
        }
        for (int i = 0; i < count; i++) Rows.Add(row);
        if (row.Link >= 0) Add(Row(row.Link), count);
    }

    /// <summary><c>0x141d8bba0</c>: the subgraph's share of the rows by its weight (its master's +0x152): all of them
    /// when no weight is left to share by; else rows × weight / left, plus one when <c>Next(left)</c> falls below the
    /// remainder; the weight is then taken from what is left.</summary>
    public void Spread(int weight, LevelRandom rng)
    {
        if (Total == 0)
        {
            Share = Rows.Count;
            return;
        }
        if (weight == 0)
        {
            Share = 0;
            return;
        }
        weight = Math.Min(weight, Total);
        Share = Rows.Count * weight / Total;
        int rest = Rows.Count * weight % Total;
        if (rest != 0 && rng.Next((uint)Total) < (uint)rest) Share++;
        Total -= weight;
    }

    /// <summary><c>0x141d8b2c0</c>: the row leaves the list (its first copy); with <paramref name="counted"/> the
    /// share drops by one.</summary>
    public void Remove(ExtraFeatureRow row, bool counted)
    {
        Rows.Remove(row);
        if (counted && Share != 0) Share--;
    }

    /// <summary><c>0x141d8b9a0</c>: a shrine room was placed. The shrine count drops by one (not below 0), the placed
    /// count goes up, and the first Shrine row leaves the list (the share stays). The shrine's position goes to the
    /// pool's placed rooms (<c>0x141d8abb0</c>), which nothing in the layout reads; left out.</summary>
    public void ShrinePlaced()
    {
        if (Shrines != 0) Shrines--;
        ShrinesPlaced++;
        int i = Rows.FindIndex(r => r.Name == Shrine);
        if (i >= 0) Rows.RemoveAt(i);
    }

    const string Shrine = "Shrine";

    /// <summary><c>0x141d8b450</c>: an injected room whose turned size is w × h, <c>Next(count)</c> among them (in list
    /// order); it leaves the list. Null when none fits.</summary>
    public (DungeonRoom Room, int Orientation, ExtraFeatureRow Row)? Take(int w, int h, LevelRandom rng)
    {
        var fits = Injected.Where(i => Orientation.Size(i.Orientation, i.Room.Def!.Width, i.Room.Def.Height) == (w, h)).ToList();
        if (fits.Count == 0) return null;
        var pick = fits[(int)rng.Next((uint)fits.Count)];
        Injected.Remove(pick);
        return pick;
    }
}
