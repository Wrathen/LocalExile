namespace GameServer.Modding;

/// <summary>A player in an area (one connection: an area change makes a new one).</summary>
public interface IPlayer
{
    /// <summary>The character's name.</summary>
    string Character { get; }

    /// <summary>The account's name, if known.</summary>
    string? Account { get; }

    /// <summary>The character's league ("Standard" when the ticket had none).</summary>
    string League { get; }

    /// <summary>Hardcore league.</summary>
    bool Hardcore { get; }

    /// <summary>Solo self-found.</summary>
    bool SoloSelfFound { get; }

    /// <summary>The league this connection plays in, by name.</summary>
    string LeagueId { get; }

    /// <summary>Whether this connection's league uses a module.</summary>
    bool Uses(string module);

    /// <summary>The character's level.</summary>
    int Level { get; }

    /// <summary>The area's <c>WorldAreas.dat</c> id (1_1_1, 1_1_town …).</summary>
    string AreaId { get; }

    /// <summary>The area instance's seed.</summary>
    uint Seed { get; }

    /// <summary>Where the player stands now (the server's estimate, in cells).</summary>
    (int X, int Y) Position { get; }

    /// <summary>True once the connection has closed (the player left the area or the game).</summary>
    bool Closed { get; }

    /// <summary>Chat lines to this player, one per line of the text.</summary>
    Task Tell(string text);

    /// <summary>One S→C message, as is (the opcode in its first two bytes, big-endian); it is logged with
    /// <paramref name="log"/> when given.</summary>
    Task Send(byte[] message, string? log = null);
}
