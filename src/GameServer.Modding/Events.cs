namespace GameServer.Modding;

/// <summary>A slash command a player typed: <c>/Name Args…</c>.</summary>
public sealed record ChatCommand(IPlayer Player, string Name, IReadOnlyList<string> Args);

/// <summary>A client message: the opcode and the whole message, opcode included.</summary>
public sealed record Packet(IPlayer Player, ushort Opcode, ReadOnlyMemory<byte> Bytes);

/// <summary>A monster: its <c>MonsterVarieties.dat</c> id, name, level, rarity and where it is (cells).</summary>
public sealed record MonsterInfo(string VarietyId, string Name, int Level, MonsterRarity Rarity, int X, int Y);

/// <summary>A monster the player killed.</summary>
public sealed record MonsterKill(IPlayer Player, MonsterInfo Monster);

/// <summary>An option picked in an NPC's dialogue (an <c>NPCTalk</c> row).</summary>
/// <param name="Player">Who picked it.</param>
/// <param name="Npc">The NPC's name.</param>
/// <param name="Category">The row's category: Gossip, Quests, QuestReward, BuyItems, SellItems …</param>
/// <param name="Option">Its text.</param>
/// <param name="Script">Its script.</param>
/// <param name="HasText">It has text and audio to play.</param>
/// <param name="Index">Its index among the NPC's options.</param>
/// <param name="Row">Its <c>NPCTalk</c> row.</param>
/// <param name="OpensGuildHideouts">A SelectHideout row's choice of list.</param>
/// <param name="RewardOffer">A QuestReward row's offer.</param>
public sealed record NpcOption(IPlayer Player, string Npc, string Category, string Option, string Script, bool HasText, int Index, int Row,
    bool OpensGuildHideouts, int? RewardOffer);

/// <summary>What an area's first message carries for a module: terrain data, achievement sets.</summary>
public sealed record AreaHeader(byte[]? Terrain, IReadOnlyList<byte>? AchievementSets);

/// <summary>A click on an object: its id, and the connection's game seconds when the client asked (for walking there).</summary>
public sealed record ObjectUse(IPlayer Player, uint ObjectId, double RequestedAt);

/// <summary>A killed monster's drops, before they are rolled. The multipliers of several mods multiply.</summary>
public sealed class DropRoll(IPlayer player, MonsterInfo monster)
{
    /// <summary>Who killed it.</summary>
    public IPlayer Player { get; } = player;

    /// <summary>What died.</summary>
    public MonsterInfo Monster { get; } = monster;

    /// <summary>Scales how many items drop (1: the server's own).</summary>
    public double Quantity { get; set; } = 1;

    /// <summary>Scales how often equipment drops magic or rare (1: the server's own).</summary>
    public double Rarity { get; set; } = 1;

    /// <summary>Scales the gold pile (1: the server's own).</summary>
    public double Gold { get; set; } = 1;

    /// <summary>True: nothing of the server's own drops (the <see cref="Items"/> still do).</summary>
    public bool Cancel { get; set; }

    /// <summary>Items to drop as well, each as /item takes it: a <c>BaseItemTypes</c> id or name, or a unique's name.</summary>
    public List<string> Items { get; } = [];
}

/// <summary>A kill's experience, before the character and its gems gain it.</summary>
public sealed class ExperienceGain(IPlayer player, MonsterInfo monster, uint experience)
{
    /// <summary>Who gains it.</summary>
    public IPlayer Player { get; } = player;

    /// <summary>What died.</summary>
    public MonsterInfo Monster { get; } = monster;

    /// <summary>The experience, after the server's level penalty; set it to change what is gained.</summary>
    public uint Experience { get; set; } = experience;
}

/// <summary>Stats a mod adds to the character. Values of the same stat add up.</summary>
public sealed class StatSheet(IPlayer player)
{
    readonly Dictionary<string, int> _added = new(StringComparer.Ordinal);

    /// <summary>Whose stats these are.</summary>
    public IPlayer Player { get; } = player;

    /// <summary>Adds <paramref name="value"/> to the stat with this <c>Stats.dat</c> id (an unknown id is ignored).</summary>
    public void Add(string stat, int value) =>
        _added[stat] = (int)Math.Clamp((long)_added.GetValueOrDefault(stat) + value, int.MinValue, int.MaxValue);

    /// <summary>What was added so far.</summary>
    public IReadOnlyDictionary<string, int> Added => _added;
}
