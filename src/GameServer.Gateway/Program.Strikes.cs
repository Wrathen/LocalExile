using GameServer.Core.Protocol.Instance;

partial class Program
{
    // BuffDefinitions skill_location_memory (invisible, CancelOnDeath, behaviour RecordSkillUseLocations): the client keeps a
    // Strike skill's previous locations in it, and its melee action (0x141c9ad30) finds it by the skill id and ActionTypes
    // hash of its bit-3 association. Its behaviour (0x14205a100) takes its first entry from the message's u32 list unchecked:
    // the use's sequence, the player's cell and the target cell; later uses it records itself as they start (0x14205a350).
    const ushort SkillLocationMemoryBuffRow = 1451;

    // A Strike use's extra targets, decided as its swing starts, as the client decides them when its melee action starts
    // (StrikeTargets): the spirit strikes (Ancestral Reach) and the previous location to strike again (Ancestral Echo),
    // whose use also records this one. The returned message, when not null, adds the skill's skill_location_memory buff.
    // A non-Strike gets nothing. Call under the Monsters lock.
    static (List<uint> Spirit, (int X, int Y)? Echo, byte[]? MemoryBuff) StrikeExtrasLocked(ClientState conn, ushort skillId,
        int effect, IReadOnlyDictionary<uint, int> ownStats, (int X, int Y) player, (int X, int Y) centre, ushort sequence,
        uint mainTarget, double now)
    {
        var skills = SkillsFor(conn);
        if (effect < 0 || !skills.IsStrike(effect)) return ([], null, null);
        var stats = MergeSkillStats(CharacterPlayerStats(conn), ownStats);
        int Get(string name) => Stats.Key(name) is uint key ? stats.GetValueOrDefault(key) : 0;
        int count = StrikeTargets.SpiritStrikes(Get, strike: true, skills.IsVaal(effect), MeleeAttack(conn, effect));
        var spirit = StrikeTargets.Pick(conn.Monsters.Where(e => e.Value.Hostile && !e.Value.Dead && e.Value.Life > 0)
                .Select(e => new StrikeTargets.Candidate(e.Key, e.Value.Walk.Position(now), LivingCollision.Size(e.Value))),
            player, StrikeTargets.Range(Get("extra_target_targeting_distance_+%")), count, sequence, PlayerId,
            mainTarget == 0 ? new HashSet<uint>() : [mainTarget]);
        if (!StrikeTargets.StrikesPreviousLocation(Get, strike: true, WeaponProfileFor(conn, activeSet: true).Melee))
            return (spirit, null, null);
        var echo = conn.StrikeLocations.Use(skillId, player, centre);
        var (buff, added) = conn.StrikeLocations.Buff(skillId, () => conn.NextBuff++);
        return (spirit, echo, !added ? null : InstanceEntities.AddBuff(PlayerId, buff, SkillLocationMemoryBuffRow, PlayerId, skillId,
            activeSkill: skills.ActionHash(effect) is ushort hash ? (skillId, hash) : null,
            list: [sequence, (uint)player.X, (uint)player.Y, (uint)centre.X, (uint)centre.Y]));
    }
}
