namespace Mixr.Services;

/// <summary>
/// Blizzard/Battle.net-Spiele über Uninstall-Registry (Publisher „Blizzard“), ohne <c>product.db</c>-Protobuf.
/// </summary>
public static class BattleNetLibraryScanner
{
    public readonly record struct BattleNetInstalledGame(string StableKey, string DisplayName);

    public static IReadOnlyList<BattleNetInstalledGame> ScanInstalledGames()
    {
        var acc = new List<BattleNetInstalledGame>();
        foreach (var e in UninstallRegistry.EnumerateEntries())
        {
            if (!LooksLikeBlizzardGame(e.DisplayName, e.Publisher))
                continue;

            acc.Add(new BattleNetInstalledGame(e.SubKeyName, e.DisplayName));
        }

        return acc
            .GroupBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static bool LooksLikeBlizzardGame(string displayName, string? publisher)
    {
        if (publisher is null ||
            !publisher.Contains("Blizzard", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(displayName.Trim(), "Battle.net", StringComparison.OrdinalIgnoreCase))
            return false;

        if (displayName.Contains("Blizzard Update", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
