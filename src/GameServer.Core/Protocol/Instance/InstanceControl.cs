using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace GameServer.Core.Protocol.Instance;

/// <summary>
/// Localhost control channel from GameServer.Gateway to GameServer.Auth (one JSON object per line, one request per TCP
/// connection). Auth owns the login connections and the <see cref="World.InstanceRegistry"/>; the Gateway asks it which
/// area/instance a ticket belongs to, for instance lists, and to move a player (Auth issues the new ticket; the Gateway
/// sends it to the client in 0x27).
/// Port: Game_AUTH_CONTROL_PORT (default 20495), bound to 127.0.0.1 only.
/// </summary>
public static class InstanceControl
{
    public const int DefaultPort = 20495;

    public static int Port => int.TryParse(Environment.GetEnvironmentVariable("Game_AUTH_CONTROL_PORT"), out var p) ? p : DefaultPort;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Sends one request to Auth and returns its answer (Ok = false with Error on any failure).</summary>
    public static async Task<ControlResponse> CallAsync(ControlRequest request, int? port = null, CancellationToken ct = default)
    {
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync("127.0.0.1", port ?? Port, ct);
            using var stream = tcp.GetStream();
            byte[] line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Json) + "\n");
            await stream.WriteAsync(line, ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string? reply = await reader.ReadLineAsync(ct);
            return reply is null
                ? new ControlResponse { Ok = false, Error = "no reply" }
                : JsonSerializer.Deserialize<ControlResponse>(reply, Json) ?? new ControlResponse { Ok = false, Error = "bad reply" };
        }
        catch (Exception ex) when (ex is IOException or SocketException or JsonException)
        {
            return new ControlResponse { Ok = false, Error = ex.Message };
        }
    }
}

/// <summary>
/// Ops: "resolve" (Ticket) → the ticket's area, seed, instance, character; "list" (Ticket, Area) → live instances of
/// Area; "transfer" (Ticket, Area, Instance = 0 for the default choice, New = true for a fresh instance) → Auth moves
/// the ticket's player and returns the destination's new Ticket (with New, Seed ≠ 0 gives the new instance that seed:
/// the Gateway's capture tour); "leave" (Ticket) → the player left its instance (expiry starts);
/// "set_level" (Ticket, Level) → the ticket's character gets that level (1 to 100) in the account store;
/// "set_portrait" (Ticket, Portrait) → the ticket's character's worn portrait, which the character list shows.
/// </summary>
public sealed record ControlRequest
{
    public string Op { get; init; } = "";
    public uint Ticket { get; init; }
    public ushort Area { get; init; }
    public ulong Instance { get; init; }
    public bool New { get; init; }
    public uint Seed { get; init; }
    public int Level { get; init; }
    public ushort Portrait { get; init; }
}

public sealed record ControlResponse
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public ushort Area { get; init; }
    public uint Seed { get; init; }
    public ulong Instance { get; init; }
    /// <summary>"resolve": when Auth created the instance (Unix ms). With <see cref="Instance"/> it names the instance
    /// across Auth restarts, which start the ids at 1 again.</summary>
    public long Created { get; init; }
    public uint Ticket { get; init; }
    public bool IsTown { get; init; }
    public string? Character { get; init; }
    /// <summary>Stable account-character identity; reused display names must not reactivate old tickets/world state.</summary>
    public Guid? CharacterId { get; init; }
    /// <summary>"resolve": the name of the account the character belongs to (its stash is the account's).</summary>
    public string? Account { get; init; }
    /// <summary>The canonical full league name; selected modes are explicit flags, not inferred from this display name.</summary>
    public string? League { get; init; }
    public bool Hardcore { get; init; }
    public bool Ssf { get; init; }
    /// <summary>The league form (<see cref="GameServer.Core.Accounts.League.Form"/>): the module the character plays by.</summary>
    public string? LeagueForm { get; init; }
    /// <summary>The character's class: Characters.dat IntegerId (CharacterClass), the Witch (3) if unknown.</summary>
    public int Class { get; init; } = 3;
    /// <summary>The character's level (from the account store).</summary>
    public int Level { get; init; } = 1;
    /// <summary>"resolve": the character's last town (HASH16): Resurrect in Town goes there. 0 before it has entered a
    /// town (the death screen then has no Resurrect in Town). An Auth that does not send it leaves Lioneye's Watch.</summary>
    public ushort LastTown { get; init; } = 0x235c;
    public List<ControlInstance>? Instances { get; init; }
}

public sealed record ControlInstance(ulong Id, long CreatedUnix, int SecondsLeft, List<string> Players);
