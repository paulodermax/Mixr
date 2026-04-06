namespace Mixr.Services;

/// <summary>Steam-Bibliothek: Einträge ohne echtes Spiel (z. B. Redistributables).</summary>
public static class SteamLibraryFilter
{
    public const int SteamworksCommonRedistributablesAppId = 228980;

    public static bool IsIgnoredSteamGame(int appId, string name) =>
        appId == SteamworksCommonRedistributablesAppId
        || IsSteamworksCommonRedistributableName(name);

    static bool IsSteamworksCommonRedistributableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        if (!name.Contains("Steamworks", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!name.Contains("Common", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!name.Contains("Redistribut", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
}
