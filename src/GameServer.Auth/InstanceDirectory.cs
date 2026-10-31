using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using GameServer.Core.Accounts;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;

/// <summary>
/// Auth's side of area instances: which instance each ticket belongs to, the live login connection of each in-game
/// character (its session issues the tickets), and the localhost control server the Gateway calls
/// (<see cref="InstanceControl"/>). Docs: Agents/docs/protocol/09-area-change.md.
/// </summary>
sealed class InstanceDirectory(InstanceRegistry registry, AccountService? accounts = null,
    IReadOnlyList<(string Id, ushort Hash16)>? towns = null, Func<string, ushort?>? loginHideout = null)
{
    private readonly IReadOnlyList<(string Id, ushort Hash16)> _towns = towns ?? WorldAreaTowns.Default;

    public InstanceRegistry Registry => registry;

    private readonly ConcurrentDictionary<uint, TicketInfo> _tickets = new();
    // Each character's latest ticket: a connection closing after its character re-entered the same instance (Instance
    // Manager) must not take it out of that instance.
    private readonly ConcurrentDictionary<string, uint> _latest = new();
    private readonly ConcurrentDictionary<string, LoginLink> _links = new(StringComparer.OrdinalIgnoreCase);

    public sealed record TicketInfo(string Character, ulong Instance, ushort Area, League? League, CharacterClass Class,
        int Level, Guid? CharacterId);

    /// <summary>A logged-in client's login connection. Writes go through <see cref="SendAsync"/> (the reply loop and the
    /// control server both write).</summary>
    public sealed class LoginLink(NetworkStream stream, ConnectionCiphers ciphers, LoginServerSession session, string peer)
    {
        private readonly SemaphoreSlim _lock = new(1, 1);
        public LoginServerSession Session => session;
        public string Peer => peer;

        public async Task SendAsync(ILoginMessage message)
        {
            await _lock.WaitAsync();
            try
            {
                var logged = message is LoginResult result ? result with { Digest = [], Data = [] } : message;
                Console.WriteLine($"[auth] {peer} => {LoginMessageFormat.Describe(logged)}");
                byte[] wire = LoginMessage.Encode(message);
                ciphers.Send.Process(wire);
                await stream.WriteAsync(wire);
            }
            finally { _lock.Release(); }
        }
    }

    /// <summary>Play (login 0x0e): enter the chosen home, or last town if none; record the ticket and login link.</summary>
    public (ushort Area, uint Ticket) AssignStart(LoginServerSession session, LoginLink link)
    {
        string character = session.CharacterName ?? "?";
        _links[character] = link;
        // Explicitly selected homes take precedence; characters without a choice retain town progression.
        var inst = registry.Enter(loginHideout?.Invoke(character) ?? WorldAreaTowns.LoginArea(session.CharacterLastTown, _towns),
            character, session.CharacterLeague?.Name, session.CharacterLeague?.Ssf ?? false, session.CharacterId?.ToString("N"));
        return (inst.AreaHash, NewTicket(session, character, inst));
    }

    private uint NewTicket(LoginServerSession session, string character, AreaInstanceState inst)
    {
        if (accounts is not null)
        {
            var canonical = accounts.FindCharacter(character);
            if (canonical is null ||
                !HardcoreDeathRules.TicketMatches(canonical, session.CharacterId, session.CharacterLeague?.Name))
                throw new InvalidOperationException("The character's league changed; return to character selection");
        }
        uint ticket = session.Keyring.NewTicket();
        _tickets[ticket] = new TicketInfo(character, inst.Id, inst.AreaHash, session.CharacterLeague, session.CharacterClass,
            session.CharacterLevel, session.CharacterId);
        _latest[character] = ticket;
        Console.WriteLine($"[auth] ticket 0x{ticket:x8}: {character} → instance {inst.Id} (area 0x{inst.AreaHash:x4}, seed {inst.Seed}, {inst.Players.Count} player(s))");
        return ticket;
    }

    public void Forget(LoginLink link)
    {
        foreach (var kv in _links.Where(kv => kv.Value == link).ToList()) _links.TryRemove(kv.Key, out _);
    }

    /// <summary>Serves <see cref="InstanceControl"/> requests on 127.0.0.1.</summary>
    public async Task ServeControlAsync(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        Console.WriteLine($"[auth] instance control on 127.0.0.1:{port}");
        while (true)
        {
            var tcp = await listener.AcceptTcpClientAsync();
            _ = Task.Run(async () =>
            {
                using (tcp)
                {
                    var stream = tcp.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                    string? line = await reader.ReadLineAsync();
                    if (line is null) return;
                    ControlResponse reply;
                    try { reply = Handle(JsonSerializer.Deserialize<ControlRequest>(line, InstanceControl.Json)!); }
                    catch (Exception ex) { reply = new ControlResponse { Ok = false, Error = ex.Message }; }
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(reply, InstanceControl.Json) + "\n"));
                }
            });
        }
    }

    internal ControlResponse Handle(ControlRequest req)
    {
        // A Gateway starting has no connections: whoever its last run had in an instance is gone, and a run that stopped
        // without closing them never sent "leave" (no ticket needed).
        if (req.Op == "gateway_started")
        {
            int removed = registry.LeaveAll();
            Console.WriteLine($"[auth] gateway started: {removed} player(s) left their instances");
            return new ControlResponse { Ok = true };
        }
        if (!_tickets.TryGetValue(req.Ticket, out var info))
            return new ControlResponse { Ok = false, Error = $"unknown ticket 0x{req.Ticket:x8}" };
        // Dead/migrated characters retain old ticket records only so disconnect cleanup can leave the old instance.
        // Resolve and transfer must use the current committed account record, never a cached login or ticket snapshot.
        if (req.Op != "leave" && accounts is not null)
        {
            var canonical = accounts.FindCharacter(info.Character);
            if (canonical is null || !HardcoreDeathRules.TicketMatches(canonical, info.CharacterId, info.League?.Name))
                return new ControlResponse { Ok = false, Error = "character is dead or its league has changed" };
        }
        switch (req.Op)
        {
            case "resolve":
            {
                var inst = registry.Get(info.Instance);
                // The Gateway resolves a ticket once the client connects to its instance: a town is entered, so it is
                // where the character's next login starts.
                if (inst is { IsTown: true } && _towns.FirstOrDefault(t => t.Hash16 == inst.AreaHash) is { Id: { } town }
                    && accounts?.SetLastTown(info.Character, town) is { Ok: true })
                    Console.WriteLine($"[auth] {info.Character}: last town {town}");
                return new ControlResponse
                {
                    Ok = inst is not null, Error = inst is null ? "instance expired" : null, Area = info.Area,
                    Seed = inst?.Seed ?? 0, Instance = info.Instance, Created = inst?.Created.ToUnixTimeMilliseconds() ?? 0, IsTown = inst?.IsTown ?? false, Character = info.Character,
                    CharacterId = info.CharacterId,
                    Account = accounts?.FindCharacter(info.Character) is { } owner ? accounts.GetAccount(owner.AccountId)?.Name : null,
                    League = info.League?.Name, Hardcore = info.League?.Hardcore ?? false,
                    Ssf = info.League?.Ssf ?? false, LeagueForm = info.League?.Form,
                    Class = (int)info.Class, Level = info.Level,
                    LastTown = accounts?.FindCharacter(info.Character)?.LastTown is { } last
                        && _towns.FirstOrDefault(t => t.Id == last) is { Id: not null } known ? known.Hash16 : (ushort)0,
                };
            }
            case "list":
            {
                var list = registry.List(req.Area, info.Character, info.League?.Name, info.League?.Ssf ?? false,
                        info.CharacterId?.ToString("N"))
                    .Select(i => new ControlInstance(i.Id, i.Created.ToUnixTimeSeconds(), (int)registry.TimeLeft(i).TotalSeconds, [.. i.Players]))
                    .ToList();
                return new ControlResponse { Ok = true, Area = req.Area, IsTown = registry.IsTown(req.Area), Instances = list };
            }
            case "transfer":
            {
                if (!_links.TryGetValue(info.Character, out var link))
                    return new ControlResponse { Ok = false, Error = $"{info.Character} has no login connection" };
                AreaInstanceState? inst = req.Instance != 0
                    ? registry.EnterExisting(req.Instance, info.Character, info.League?.Name, info.League?.Ssf ?? false,
                        req.Area, info.CharacterId?.ToString("N"))
                    : req.New ? registry.EnterNew(req.Area, info.Character, info.League?.Name, info.League?.Ssf ?? false,
                        info.CharacterId?.ToString("N"), req.Seed)
                    : registry.Enter(req.Area, info.Character, info.League?.Name, info.League?.Ssf ?? false,
                        info.CharacterId?.ToString("N"));
                if (inst is null) return new ControlResponse { Ok = false, Error = $"instance {req.Instance} unavailable in this league" };
                // The Gateway hands the ticket to the client in 0x27: in game the client ignores login 0x14.
                uint ticket = NewTicket(link.Session, info.Character, inst);
                return new ControlResponse
                {
                    Ok = true, Area = inst.AreaHash, Instance = inst.Id, Seed = inst.Seed, IsTown = inst.IsTown, Ticket = ticket,
                };
            }
            case "set_level":
            {
                if (accounts is null) return new ControlResponse { Ok = false, Error = "no account store" };
                var r = accounts.SetLevel(info.Character, req.Level);
                if (!r.Ok) return new ControlResponse { Ok = false, Error = r.Error.ToString() };
                // This ticket and the next ones (area changes) carry the new level.
                _tickets[req.Ticket] = info with { Level = req.Level };
                if (_links.TryGetValue(info.Character, out var link)) link.Session.CharacterLevel = req.Level;
                Console.WriteLine($"[auth] {info.Character} set to level {req.Level}");
                return new ControlResponse { Ok = true, Character = info.Character, Level = req.Level };
            }
            case "set_portrait":
            {
                if (accounts is null) return new ControlResponse { Ok = false, Error = "no account store" };
                var r = accounts.SetPortrait(info.Character, req.Portrait);
                if (!r.Ok) return new ControlResponse { Ok = false, Error = r.Error.ToString() };
                Console.WriteLine($"[auth] {info.Character}: portrait {req.Portrait}");
                return new ControlResponse { Ok = true, Character = info.Character };
            }
            case "leave":
                if (_latest.TryGetValue(info.Character, out uint latest) && latest != req.Ticket
                    && _tickets.TryGetValue(latest, out var now) && now.Instance == info.Instance)
                    return new ControlResponse { Ok = true };
                registry.Leave(info.Instance, info.Character);
                return new ControlResponse { Ok = true };
            default:
                return new ControlResponse { Ok = false, Error = $"unknown op {req.Op}" };
        }
    }
}
