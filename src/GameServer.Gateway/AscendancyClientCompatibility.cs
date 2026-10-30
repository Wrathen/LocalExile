/// <summary>
/// Phrecia's shipped graph has older ordinary topology. Installing a merged graph is outside normal startup:
/// an agreed client-compatible graph is a separate prerequisite. Reject only Phrecia requests, before any save.
/// </summary>
static class AscendancyClientCompatibility
{
    public static void RequirePlayable(AscendancyIdentity? selection) => RequireCompatible(selection,
        Environment.GetEnvironmentVariable("Game_VERIFIED_BEHEMOTH") == "1", VerifiedBehemothGraph.RequireInstalled);

    internal static void RequireCompatible(AscendancyIdentity? selection, bool optedIn, Action verifyInstalled)
    {
        if (selection is { Family: AscendancyFamily.Phrecia, Id: "Gladiator", Revision: "3.29.3b" }
            && Environment.GetEnvironmentVariable("Game_VERIFIED_GAMBLER") == "1")
        {
            VerifiedGamblerGraph.RequireInstalled();
            return;
        }
        if (selection is { Family: AscendancyFamily.Phrecia, Id: "Chieftain", Revision: "3.29.3b" }
            && Environment.GetEnvironmentVariable("Game_VERIFIED_ANTIQUARIAN") == "1")
        {
            VerifiedAntiquarianGraph.RequireInstalled();
            return;
        }
        if (selection is { Family: AscendancyFamily.Phrecia, Id: "Slayer", Revision: "3.29.3b" }
            && Environment.GetEnvironmentVariable("Game_VERIFIED_ARISTOCRAT") == "1")
        {
            VerifiedGamblerGraph.RequireInstalled("Aristocrat");
            return;
        }
        if (selection?.Family == AscendancyFamily.Phrecia)
        {
            if (selection.Id == "Berserker" && selection.Revision == AscendancyCatalog.Revision
                && optedIn)
            {
                verifyInstalled();
                return;
            }
            throw new AscendancyRuleException("Phrecia play is gated pending an agreed client-compatible passive graph; " +
                "the installed client archive is not changed. Ordinary ascendancies remain available. " +
                "See Agents/docs/game/ascendancies.md");
        }
    }
}
