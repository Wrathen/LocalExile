using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

/// <summary>
/// A trade between two players in one instance. Each client shows two offer inventories (manager 1): its own offer
/// (<see cref="Program.PlayerTradeMine"/>) and the other player's (<see cref="Program.PlayerTradeTheirs"/>). An offered
/// item stays reserved in its owner's inventory (PlayerInventories.Offer, hidden from that client like a sale) until the
/// trade completes or ends. Guarded by its own lock; the sends happen outside it.
/// </summary>
sealed class PlayerTrade(ClientState a, ClientState b)
{
    public readonly ClientState A = a, B = b;
    public readonly object Lock = new();
    public bool Over;
    /// <summary>Per client: the revision of its own offer inventory and of the other's, as last sent to it.</summary>
    public readonly Dictionary<ClientState, (uint Mine, uint Theirs)> Revisions = new() { [a] = (0, 0), [b] = (0, 0) };
    /// <summary>Per client: the revision of the other's offer it accepted, or null.</summary>
    public readonly Dictionary<ClientState, uint?> Accepted = new() { [a] = null, [b] = null };
    /// <summary>Per offering client: its item id → the id the other client sees it under (its own id space).</summary>
    public readonly Dictionary<ClientState, Dictionary<uint, uint>> Views = new() { [a] = [], [b] = [] };

    public ClientState Other(ClientState c) => ReferenceEquals(c, A) ? B : A;
}

partial class Program
{
    internal const uint PlayerTradeMine = NpcShops.FirstTabId + 0x10, PlayerTradeTheirs = PlayerTradeMine + 1;
    internal const byte PlayerTradeWaitingFlags = InstanceNpcs.TradeActive,
        PlayerTradeOpenFlags = InstanceNpcs.TradeActive | InstanceSocial.TradeRequestFlag | InstanceNpcs.TradeNoMouseOver;
    internal const byte PartyInvited = 0, PartyMember = 1, PartyLeader = 2;
    internal const byte TradeMineAccepted = 0x04, TradeTheirsAccepted = 0x08;
}
