using GameServer.Modding;

/// <summary>What the Gateway calls where it does the same thing itself: the hooks of the league's own code, of its modules, then of
/// the mods.</summary>
sealed class ServerHooks(params ModHost[] hosts)
{
    public IEnumerable<(string Name, string Usage)> Commands => hosts.SelectMany(h => h.Commands);

    public async Task<bool> RunCommand(IPlayer player, string name, IReadOnlyList<string> args)
    {
        foreach (var host in hosts) if (await host.RunCommand(player, name, args)) return true;
        return false;
    }

    public async Task Tick(IPlayer player, double seconds)
    {
        foreach (var host in hosts) await host.Tick(player, seconds);
    }

    public bool WantsPacket(ushort opcode) => hosts.Any(h => h.WantsPacket(opcode));

    public async Task<bool> Packet(IPlayer player, ushort opcode, byte[] bytes)
    {
        foreach (var host in hosts) if (await host.Packet(player, opcode, bytes)) return true;
        return false;
    }

    public async Task AreaGenerated(IPlayer player)
    {
        foreach (var host in hosts) await host.AreaGenerated(player);
    }

    public async Task MonsterKilled(MonsterKill kill)
    {
        foreach (var host in hosts) await host.MonsterKilled(kill);
    }

    public async Task<bool> ObjectUsed(ObjectUse use)
    {
        foreach (var host in hosts) if (await host.ObjectUsed(use)) return true;
        return false;
    }

    public async Task PlayerLeaving(IPlayer player, string why)
    {
        foreach (var host in hosts) await host.PlayerLeaving(player, why);
    }

    public async Task HideoutReady(IPlayer player)
    {
        foreach (var host in hosts) await host.HideoutReady(player);
    }

    public List<(string Log, byte[] Message)> EnterMessages(IPlayer player) => [.. hosts.SelectMany(h => h.EnterMessages(player))];

    public AreaHeader? Header(IPlayer player) => hosts.Select(h => h.Header(player)).FirstOrDefault(h => h is not null);

    public async Task<bool> RunNpcOption(NpcOption talk)
    {
        foreach (var host in hosts) if (await host.RunNpcOption(talk)) return true;
        return false;
    }

    public DropRoll? Drops(IPlayer player, MonsterInfo monster)
    {
        if (!hosts.Any(h => h.ChangesDrops)) return null;
        var roll = new DropRoll(player, monster);
        foreach (var host in hosts) host.ModifyDrops(roll);
        return roll;
    }

    public uint Experience(IPlayer player, MonsterInfo monster, uint experience)
    {
        foreach (var host in hosts) experience = host.Experience(player, monster, experience);
        return experience;
    }

    public bool ChangesStats => hosts.Any(h => h.ChangesStats);

    public IEnumerable<KeyValuePair<string, int>> Stats(IPlayer player) => hosts.SelectMany(h => h.Stats(player));
}
