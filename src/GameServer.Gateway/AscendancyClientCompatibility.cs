/// <summary>
/// Phrecia's shipped graph has older ordinary topology. Installing a merged graph is outside normal startup:
/// an agreed client-compatible graph is a separate prerequisite. Reject only Phrecia requests, before any save.
/// </summary>
static class AscendancyClientCompatibility
{
    public static void RequirePlayable(AscendancyIdentity? selection)
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
        if (selection is { Family: AscendancyFamily.Phrecia, Id: "Juggernaut", Revision: "3.29.3b" }
            && Environment.GetEnvironmentVariable("Game_VERIFIED_ANCESTRAL_COMMANDER") == "1")
        {
            VerifiedGamblerGraph.RequireInstalled("Ancestral Commander");
            return;
        }
        if (selection is { Family: AscendancyFamily.Phrecia, Id: "Champion", Revision: "3.29.3b" }
            && Environment.GetEnvironmentVariable("Game_VERIFIED_PALADIN") == "1")
        {
            VerifiedGamblerGraph.RequireInstalled("Paladin");
            return;
        }
        if (selection is { Family: AscendancyFamily.Phrecia, Id: "Guardian", Revision: "3.29.3b" }
            && Environment.GetEnvironmentVariable("Game_VERIFIED_POLYTHEIST") == "1")
        {
            VerifiedGamblerGraph.RequireInstalled("Polytheist");
            return;
        }
        if (selection is { Family: AscendancyFamily.Phrecia, Id: "Saboteur", Revision: "3.29.3b" }
            && Environment.GetEnvironmentVariable("Game_VERIFIED_SURFCASTER") == "1")
        {
            VerifiedGamblerGraph.RequireInstalled("Surfcaster");
            return;
        }
        if (selection?.Family == AscendancyFamily.Phrecia)
            throw new AscendancyRuleException("Phrecia play is gated pending an agreed client-compatible passive graph; " +
                "the installed client archive is not changed. Ordinary ascendancies remain available. " +
                "See Agents/docs/game/ascendancies.md");
    }
}
