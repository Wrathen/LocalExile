namespace GameServer.Core.Accounts;

/// <summary>Hardcore death rules for the pinned 3.29 client: ordinary challenge deaths stay in the same challenge,
/// retaining SSF; a league form may make them permanent. Stashes and account gold never migrate with a character.</summary>
public static class HardcoreDeathRules
{
    public const string VoidLeague = "Void";

    public static bool CanEnter(Character character) =>
        character.HardcoreDeath?.PermanentlyDead != true &&
        !string.Equals(character.League, VoidLeague, StringComparison.OrdinalIgnoreCase);

    public static bool TicketMatches(Character character, Guid? characterId, string? league) =>
        CanEnter(character) && character.Id == characterId &&
        string.Equals(character.League, league, StringComparison.OrdinalIgnoreCase);

    public static Character Apply(Character character, League source, DateTimeOffset atUtc)
    {
        if (!source.Hardcore) throw new ArgumentException("The source league must be Hardcore", nameof(source));
        if (character.HardcoreDeath is { } prior)
        {
            if (!string.Equals(prior.SourceLeague, source.Name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The death belongs to a different league");
            return character;
        }
        if (!string.Equals(character.League, source.Name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The character no longer belongs to the ticket's league");
        return character with
        {
            League = source.PermanentDeath ? VoidLeague : source.DeathLeague ?? source.Parent ?? (source.Ssf ? $"SSF {source.Base}" : source.Base),
            HardcoreDeath = new(source.Name, atUtc, source.PermanentDeath),
        };
    }

    /// <summary>A stale level/town/portrait update must never put a migrated or permanently dead character back into HC.</summary>
    public static Character PreserveCommittedDeath(Character requested, Character current) =>
        current.HardcoreDeath is null ? requested : requested with
        {
            League = current.League,
            HardcoreDeath = current.HardcoreDeath,
        };
}
