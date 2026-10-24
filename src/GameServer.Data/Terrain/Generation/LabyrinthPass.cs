namespace GameServer.Data.Terrain.Generation;

/// <summary><c>0x142032760</c> (.dgr, from <c>0x14203c900</c>) / <c>0x142033e30</c> (.tgr, from <c>0x14203f890</c>): the
/// Labyrinth's graph pass, for an area whose WorldAreas row has +0x171 set (<see cref="AreaInfo.IsLabyrinth"/>), after
/// the tag choices and side areas and before the description pass (<c>0x142034e60</c>). Ported for an instance without
/// a labyrinth node (instance +0x48, the server's daily layout: its LabyrinthNodeOverrides and the secrets it assigns,
/// <c>0x142033660</c>, are not ported); that is what the Gateway sends.</summary>
sealed class LabyrinthPass(Func<string, byte[]?> read, string areaId, bool boss)
{
    /// <summary>Runs the pass over the graph's nodes. Every node tagged "secret" or "secret" plus one character
    /// (<c>0x14203a2e0</c>) is collected and loses its exist chance (+0x44 = 0). A boss area (+0x1bb) applies one
    /// LabyrinthNodeOverrides row; any other area named "_quad_door" or "_branch_door" turns the first collected node
    /// into the LabyrinthSecrets "GoldDoor" room. The nodes still collected then also lose their descriptions.</summary>
    public void Run(TileGraph graph, LevelRandom rng)
    {
        var secrets = new List<GraphNode>();
        bool plain = true;
        foreach (var n in graph.Nodes)
        {
            if (n.Tag.Length is not (6 or 7) || !n.Tag.StartsWith("secret", StringComparison.Ordinal)) continue;
            secrets.Add(n);
            n.Exist = 0;
            plain &= n.Tag.Length < 7;
        }
        if (boss)
        {
            // 0x1424e5ca0 builds the area's labyrinth (difficulty: the id's first character, 'E' = 4) for the pick
            // below; "_end" areas take the final boss row (Normal: the one without traps), the others Boss1_Sec1.
            int difficulty = areaId[0] == 'E' ? 4 : areaId[0] - '0';
            string row = areaId.Contains("_end", StringComparison.Ordinal) ? difficulty == 1 ? "FinalBossEasy" : "FinalBoss" : "Boss1_Sec1";
            Override(graph, row);
        }
        else if (areaId.Contains("_quad_door", StringComparison.Ordinal) || areaId.Contains("_branch_door", StringComparison.Ordinal))
            GoldDoor(secrets, plain, rng);
        foreach (var n in secrets)
        {
            n.Exist = 0;
            n.Descriptions.Clear();
        }
    }

    /// <summary><c>0x1420339a0</c> → <c>0x142033ad0</c>: every node whose tag is the row's node name (+8) takes one of
    /// its overrides (+0x10): none empties the tag, one is taken as it is; with several, <c>0x1424e5ee0</c> picks by the
    /// labyrinth's difficulty and the node's index in the labyrinth layout (no level draw; not ported).</summary>
    void Override(TileGraph graph, string id)
    {
        var table = Table("LabyrinthNodeOverrides");
        int row = table.Find(r => table.String(r, 0) == id);
        if (row < 0) return;
        string name = table.String(row, 8);
        if (name.Length == 0) return;
        var overrides = table.StringList(row, 0x10);
        foreach (var n in graph.Nodes)
        {
            if (n.Tag != name) continue;
            if (overrides.Length > 1)
                throw new NotSupportedException($"{n}: LabyrinthNodeOverrides {id} picks among {overrides.Length} by the labyrinth layout (0x1424e5ee0), not ported");
            n.Tag = overrides.Length == 0 ? "" : overrides[0];
        }
    }

    /// <summary>The gold door: the collected nodes are shuffled (all tagged "secret": <c>Next(count)</c> per place,
    /// front to back) or else sorted by tag (<c>0x1420393f0</c>, stable); the first takes the GoldDoor row's room name
    /// (+8) as its tag, exist 100, and a "secret1" description. None collected: the client logs "Not enough secret
    /// nodes in …" and goes on.</summary>
    void GoldDoor(List<GraphNode> secrets, bool plain, LevelRandom rng)
    {
        var table = Table("LabyrinthSecrets");
        int row = table.Find(r => table.String(r, 0) == "GoldDoor");
        if (row < 0) return;
        if (plain)
            for (int i = 0; i < secrets.Count; i++)
            {
                int j = i + (int)rng.Next((uint)(secrets.Count - i));
                (secrets[i], secrets[j]) = (secrets[j], secrets[i]);
            }
        else
        {
            var sorted = secrets.OrderBy(n => n.Tag, StringComparer.Ordinal).ToList();
            secrets.Clear();
            secrets.AddRange(sorted);
        }
        string room = table.String(row, 8);
        if (room.Length == 0 || secrets.Count == 0) return;
        var door = secrets[0];
        door.Tag = room;
        // +0x6d (an "entrance<N>" description numbered from the labyrinth node) is clear in the GoldDoor row.
        if (table.Bool(row, 0x6d)) throw new NotSupportedException("LabyrinthSecrets +0x6d without a labyrinth node");
        secrets.RemoveAt(0);
        door.Exist = 100;
        if (!door.Descriptions.Contains("secret1")) door.Descriptions.Add("secret1");
    }

    RawDat Table(string name) => new(read($"Data/{name}.datc64") ?? throw new FileNotFoundException($"Data/{name}.datc64"));
}
