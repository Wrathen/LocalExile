namespace GameServer.Data.Terrain.Generation;

/// <summary><c>0x14204a570</c>: before the layout, the generator rolls which extra terrain features (league encounters,
/// trials, the discoverable hideout …) the area gets. The rolls draw from the level RNG, so they shift everything after
/// them. Ported for an area without area stats (map mods, atlas passives: every stat the client reads is 0); a breach
/// that rolls is not ported and throws.</summary>
public static class ExtraTerrainFeatures
{
    public sealed record Feature(string Name, int Count);

    /// <summary>The Vaal side area roll (generator +0xa), which the tile graph's side area nodes use.</summary>
    public const string VaalSideArea = "(vaal side area)";

    /// <summary>The discoverable hideout roll (generator +0xb), which the hideout nodes use.</summary>
    public const string DiscoverableHideout = "(discoverable hideout)";

    public static List<Feature> Roll(AreaInfo area, LevelRandom rng)
    {
        var features = new List<Feature>();
        if (area.IsTown || area.RandomnessDisabled || area.AreaType == 0x13 || area.Hash16 == 0x8f53) return features;
        int Chance(int at) => area.LeagueChance?.Invoke(at) ?? 0;

        // world +0xd7 = 0: the Vaal side area chance (+0x60) sets the generator's flag +0xa.
        if (rng.Chance(Math.Clamp(Chance(0x60), 0, 100))) features.Add(new(VaalSideArea, 1));
        // Maps above level 72: the labyrinth trial chance (+0x6c).
        if (area.IsMapArea && area.Level > 0x48 && rng.Chance(Chance(0x6c))) features.Add(new("LabyrinthTrial", 1));
        if (area.AreaType != 0x23 && Category(area, rng, 0x12) != 0) features.Add(new("HarvestEncounter", 1));

        // The generator's +0xc: towns, hideout-like and special area types skip the rest.
        bool special = area.IsTown || area.RandomnessDisabled || area.AreaType is 0x13 or 0x20 or 0xb || area.Hash16 == 0x7757;
        if (!special)
        {
            // Data/Hideouts.dat: the hideout discoverable here, by its chance in 10 000.
            if (area.DiscoverableHideout is { Enabled: true } h && rng.Chance(h.Chance, 10000)) features.Add(new(DiscoverableHideout, 1));
            // CoreLeagues 1 (shrines): the count goes to generator +0x188 / +0x189, no feature.
            int shrines = Category(area, rng, 1);
            if (shrines != 0) features.Add(new("(shrines)", shrines));
            int legion = Category(area, rng, 0xf);
            if (legion != 0) features.Add(new("LegionMonolith", legion));
            if (Category(area, rng, 0x10) != 0) features.Add(new("BlightEncounter", 1));
            // CoreLeagues 0x17 only rolls with stat 0x45f8 (none here).
            int village = Category(area, rng, 0x1b);
            if (village != 0) features.Add(new("Village", village));
            if (Category(area, rng, 7) != 0) throw new NotSupportedException($"{area.Id}: a breach rolled (category 7's follow-up is not ported)");
        }
        // Every area: the fishing mission, one in 100 000, when the league chances list one (+0x88..+0x94).
        if (Chance(0x88) + Chance(0x8c) + Chance(0x90) + Chance(0x94) > 0 && rng.Next(100000u) == 0) features.Add(new("MissionFish", 1));
        return features;
    }

    // 0x141ea1f50: per CoreLeagues row, the WorldAreaLeagueChances offsets of its chance and its repeat chance (0 = none).
    static readonly Dictionary<int, (int Chance, int Repeat)> CategoryChance = new()
    {
        [1] = (0x24, 0x28), [3] = (0x64, 0x68), [5] = (0xc, 0x10), [6] = (0x38, 0x3c), [7] = (0x40, 0), [0xf] = (0x2c, 0),
        [0x10] = (0x48, 0), [0x12] = (0x34, 0), [0x1b] = (0x9c, 0xa0),
    };

    /// <summary><c>0x14204a310</c> → <c>0x141ea1f50</c> / <c>0x141ea1c80</c>, then <c>0x141ea2400</c>, without area stats
    /// (every stat 0, world +0xd7 off): how many of a league mechanic the area gets. A CoreLeagues row with +0xa0 set
    /// or a most-per-area of 0 gives none; +0x8e set needs a chance. One roll at the chance, then rolls at the repeat
    /// chance while they succeed, up to the most; each roll is <c>0x142054a50</c>: chance ≥ 1 and (≥ 100 or
    /// <c>Next(100)</c> below it).</summary>
    static int Category(AreaInfo area, LevelRandom rng, int category)
    {
        var (at, repeatAt) = CategoryChance[category];
        int chance = Math.Clamp(area.LeagueChance?.Invoke(at) ?? 0, 0, 100);
        int repeat = repeatAt == 0 ? 0 : Math.Clamp(area.LeagueChance?.Invoke(repeatAt) ?? 0, 0, 100);
        var (max, needsChance, skip) = area.League?.Invoke(category) ?? (0, false, true);
        if (skip || (needsChance && chance <= 0) || max <= 0) return 0;
        bool Roll(int c) => c >= 1 && (c >= 100 || rng.Next(100u) < (uint)c);
        int count = 0;
        if (chance != 0 && Roll(chance))
        {
            count++;
            if (repeat != 0 && count < max)
                while (Roll(repeat) && ++count < max) { }
        }
        return count;
    }
}
