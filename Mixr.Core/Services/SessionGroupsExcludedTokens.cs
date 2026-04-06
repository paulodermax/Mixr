namespace Mixr.Services;

/// <summary>
/// Tokens, die nicht automatisch in <c>session_groups</c> landen dürfen (Merge), auch wenn die Registry noch etwas Passendes findet.
/// </summary>
public static class SessionGroupsExcludedTokens
{
    static readonly HashSet<string> Exact = new(StringComparer.OrdinalIgnoreCase)
    {
        "msedge",
        "Microsoft Edge",
        "Riot Client",
        "RiotClientServices",
        "Steam",
        "Battle.net",
        "Epic",
        "Epic Games Launcher",
        "GOG Galaxy",
        "UbisoftConnect",
        "EADesktop",
    };

    /// <summary>True = diesen Such-Token nicht aus der Installations-Erkennung übernehmen.</summary>
    public static bool ShouldSkipMerge(string? searchToken)
    {
        if (string.IsNullOrWhiteSpace(searchToken))
            return true;

        var t = searchToken.Trim();
        if (Exact.Contains(t))
            return true;

        if (t.StartsWith("Riot", StringComparison.OrdinalIgnoreCase) &&
            t.Contains("Client", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
