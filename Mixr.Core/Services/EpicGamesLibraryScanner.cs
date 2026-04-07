using System.Text.Json;
using Microsoft.Win32;

namespace Mixr.Services;

/// <summary>
/// Installierte Epic-Games-Store-Titel aus <c>*.item</c>-Manifesten (vgl. GameFinder EGS).
/// </summary>
public static class EpicGamesLibraryScanner
{
    /// <param name="StableKey">Eindeutiger Schlüssel für <c>epic:…</c> im Katalog.</param>
    public readonly record struct EpicInstalledGame(string StableKey, string DisplayName);

    public static IReadOnlyList<EpicInstalledGame> ScanInstalledGames()
    {
        var manifestDir = TryGetManifestDirectory();
        if (string.IsNullOrEmpty(manifestDir) || !Directory.Exists(manifestDir))
            return Array.Empty<EpicInstalledGame>();

        var raw = new List<(string GroupKey, string Ns, string DisplayName)>();
        foreach (var path in Directory.EnumerateFiles(manifestDir, "*.item"))
        {
            var row = TryParseItemFile(path);
            if (row is null)
                continue;
            raw.Add(row.Value);
        }

        if (raw.Count == 0)
            return Array.Empty<EpicInstalledGame>();

        var result = new List<EpicInstalledGame>();
        foreach (var g in raw.GroupBy(x => x.GroupKey, StringComparer.OrdinalIgnoreCase))
        {
            var first = g.First();
            var stable = string.IsNullOrEmpty(first.Ns)
                ? first.GroupKey
                : $"{first.Ns}:{first.GroupKey}";
            result.Add(new EpicInstalledGame(stable, first.DisplayName));
        }

        return result
            .GroupBy(x => x.StableKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static string? TryGetManifestDirectory()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Epic Games\EOS");
            var reg = key?.GetValue("ModSdkMetadataDir") as string;
            if (!string.IsNullOrWhiteSpace(reg))
            {
                var p = reg.Replace('/', '\\').Trim();
                if (Directory.Exists(p))
                    return p;
            }
        }
        catch
        {
            /* */
        }

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
    }

    static (string GroupKey, string Ns, string DisplayName)? TryParseItemFile(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (root.TryGetProperty("bIsIncompleteInstall", out var inc) &&
                inc.ValueKind == JsonValueKind.True)
                return null;

            if (!root.TryGetProperty("InstallLocation", out var locEl) ||
                locEl.ValueKind != JsonValueKind.String)
                return null;

            var loc = locEl.GetString();
            if (string.IsNullOrWhiteSpace(loc))
                return null;

            var display = PickString(root, "DisplayName", "CatalogItemName", "FullAppName", "Title");
            if (string.IsNullOrWhiteSpace(display))
                return null;

            var catalogId = PickString(root, "CatalogItemId");
            if (string.IsNullOrWhiteSpace(catalogId))
                return null;

            var mainId = PickString(root, "MainGameCatalogItemId");
            var groupKey = !string.IsNullOrWhiteSpace(mainId) && !IsZeroHexId(mainId)
                ? mainId
                : catalogId;

            var ns = PickString(root, "CatalogNamespace") ?? "";

            return (groupKey, ns, display.Trim());
        }
        catch
        {
            return null;
        }
    }

    static bool IsZeroHexId(string s)
    {
        var t = s.Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).Trim();
        if (t.Length < 8)
            return false;
        foreach (var c in t)
        {
            if (c != '0')
                return false;
        }

        return true;
    }

    static string? PickString(JsonElement root, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (!root.TryGetProperty(name, out var el))
                continue;
            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    return s.Trim();
            }
        }

        return null;
    }
}
