namespace Mixr.Services;

/// <summary>
/// Ubisoft-Spiele über Uninstall-Registry (Publisher „Ubisoft“), ohne Ubisoft-Launcher.
/// </summary>
public static class UbisoftConnectLibraryScanner
{
    public readonly record struct UbisoftInstalledGame(string StableKey, string DisplayName);

    public static IReadOnlyList<UbisoftInstalledGame> ScanInstalledGames()
    {
        var acc = new List<UbisoftInstalledGame>();
        foreach (var e in UninstallRegistry.EnumerateEntries())
        {
            if (!LooksLikeUbisoftGame(e.DisplayName, e.Publisher))
                continue;

            acc.Add(new UbisoftInstalledGame(e.SubKeyName, e.DisplayName));
        }

        return acc
            .GroupBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static bool LooksLikeUbisoftGame(string displayName, string? publisher)
    {
        if (publisher is null)
            return false;

        if (!publisher.Contains("Ubisoft", StringComparison.OrdinalIgnoreCase))
            return false;

        if (displayName.Contains("Ubisoft Connect", StringComparison.OrdinalIgnoreCase) ||
            displayName.Equals("Ubisoft Connect", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
