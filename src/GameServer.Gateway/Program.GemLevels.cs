partial class Program
{
    // Read raw sources rather than CharacterPlayerStats: reservation/pool calculations can already be resolving
    // this skill. A native developer replacement wins over the aggregate source, without feeding a derived level back.
    internal static int EffectiveGemLevel(ClientState conn, GameServer.Core.Protocol.Instance.Item gem, int supportedBonus)
    {
        int intrinsic = ItemsFor(conn).GemLevel(gem);
        var type = ItemsFor(conn).ByHash(gem.TypeHash);
        if (type is null || ItemsFor(conn).Gem(type) is not { } info)
            return SkillGemLevel.Resolve(intrinsic, supportedBonus, []);
        string[] stats = info.IsSupport
            ? SkillsFor(conn).IsExceptional(type.Row) ? [] : [SkillGemLevel.NonExceptionalSupports]
            : [SkillGemLevel.AllSkillGems, .. SkillGemLevel.FamilyStats.Where(stat => SkillsFor(conn).FamilyStatRaises(stat, type.Row))];
        return SkillGemLevel.Resolve(intrinsic, supportedBonus, stats.Select(stat => RawGemStat(conn, stat)));
    }

    // +% quality of all skill gems: active gems only, like the level stat.
    internal static int GemQualityBonus(ClientState conn, GameServer.Core.Protocol.Instance.Item gem) =>
        ItemsFor(conn).ByHash(gem.TypeHash) is { } type && ItemsFor(conn).Gem(type) is { IsSupport: false }
            ? RawGemStat(conn, SkillGemLevel.AllSkillGemQuality) : 0;

    internal static int RawGemStat(ClientState conn, string stat)
    {
        if (Stats.Key(stat) is not uint key) return 0;
        var sources = CharacterStatSources(conn).Stats;
        lock (conn.Monsters) return conn.PlayerStats.GetValueOrDefault(key, sources.GetValueOrDefault(key));
    }
}
