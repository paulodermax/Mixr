using System.Globalization;

namespace Mixr.Services;

/// <summary>
/// Riot-Titel: <c>%ProgramData%\Riot Games\Metadata\*</c> und Uninstall-Einträge mit Publisher „Riot Games“.
/// </summary>
public static class RiotGamesLibraryScanner
{
    public readonly record struct RiotInstalledGame(string StableKey, string DisplayName);

    /// <summary>Metadata-Ordner wie <c>bacon.live</c> / <c>lion.live</c> — interne Riot-Bundles, keine eigenständigen Spiele.</summary>
    static readonly HashSet<string> IgnoredMetadataStableKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "bacon",
        "lion",
    };

    public static IReadOnlyList<RiotInstalledGame> ScanInstalledGames()
    {
        var acc = new Dictionary<string, RiotInstalledGame>(StringComparer.OrdinalIgnoreCase);

        foreach (var g in ScanMetadataFolders())
            acc[g.StableKey] = g;

        foreach (var g in ScanUninstallRegistry())
        {
            if (!acc.ContainsKey(g.StableKey))
                acc[g.StableKey] = g;
        }

        return acc.Values
            .GroupBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static IEnumerable<RiotInstalledGame> ScanMetadataFolders()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var metaRoot = Path.Combine(programData, "Riot Games", "Metadata");
        if (!Directory.Exists(metaRoot))
            yield break;

        foreach (var dir in Directory.EnumerateDirectories(metaRoot))
        {
            var folder = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(folder))
                continue;
            if (folder.Contains("riot_client", StringComparison.OrdinalIgnoreCase))
                continue;

            var dot = folder.IndexOf('.');
            var stable = dot > 0 ? folder[..dot] : folder;
            if (string.IsNullOrWhiteSpace(stable))
                continue;
            if (IgnoredMetadataStableKeys.Contains(stable))
                continue;

            var display = HumanizeRiotFolder(stable);
            yield return new RiotInstalledGame(stable, display);
        }
    }

    static string HumanizeRiotFolder(string stable)
    {
        var s = stable.Replace('_', ' ');
        var text = CultureInfo.InvariantCulture.TextInfo;
        return text.ToTitleCase(s.ToLowerInvariant());
    }

    static IEnumerable<RiotInstalledGame> ScanUninstallRegistry()
    {
        foreach (var e in UninstallRegistry.EnumerateEntries())
        {
            if (e.Publisher is null ||
                !e.Publisher.Contains("Riot Games", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(e.DisplayName.Trim(), "Riot Client", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return new RiotInstalledGame(e.SubKeyName, e.DisplayName);
        }
    }
}
