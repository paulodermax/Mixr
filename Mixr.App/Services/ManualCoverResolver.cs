using System.Security.Cryptography;
using System.Text;

namespace Mixr_App.Services;

/// <summary>
/// Sucht unter <see cref="GameCatalogPaths.CoversDir"/> nach manuell abgelegten Cover-Dateien
/// (<c>spotify.png</c>, <c>chrome.png</c>, <c>steam_730.png</c>, optional <c>cat_*.png</c> wie bei IGDB-Downloads).
/// </summary>
public static class ManualCoverResolver
{
    internal static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp"];

    /// <summary>Cover nur anhand Anzeigename/Token-Stamm (z. B. Session-Label „League of Legends“).</summary>
    public static string? TryFindRelativePathByLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        return TryFindRelativePath(new CatalogGameEntry
        {
            Name = label.Trim(),
            AssignmentToken = label.Trim(),
        });
    }

    /// <summary>Relativer Pfad wie <c>covers/spotify.png</c> oder <c>null</c>.</summary>
    public static string? TryFindRelativePath(CatalogGameEntry entry)
    {
        GameCatalogPaths.EnsureLayout();
        var dir = GameCatalogPaths.CoversDir;
        if (!Directory.Exists(dir))
            return null;

        var besideCovers = Path.Combine(AppContext.BaseDirectory, "covers");

        foreach (var stem in EnumerateFileStems(entry))
        {
            foreach (var ext in Extensions)
            {
                var name = stem + ext;
                var inAppData = Path.Combine(dir, name);
                var beside = Path.Combine(besideCovers, name);
                if (!File.Exists(inAppData) && !File.Exists(beside))
                    continue;

                return $"covers/{name}".Replace('\\', '/');
            }
        }

        return null;
    }

    static IEnumerable<string> EnumerateFileStems(CatalogGameEntry e)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stem in BuildStems(e))
        {
            if (seen.Add(stem))
                yield return stem;
        }
    }

    static IEnumerable<string> BuildStems(CatalogGameEntry e)
    {
        if (!string.IsNullOrEmpty(e.AssignmentToken))
        {
            var a = SlugFileStem(e.AssignmentToken);
            if (a.Length > 0)
                yield return a;
            foreach (var x in TokenStemAliases(e.AssignmentToken))
                yield return x;
        }

        if (e.SteamAppId > 0)
            yield return $"steam_{e.SteamAppId}";

        if (!string.IsNullOrEmpty(e.Name))
        {
            var n = SlugFileStem(e.Name);
            if (n.Length > 0)
                yield return n;

            var parts = e.Name.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                var first = SlugFileStem(parts[0]);
                if (first.Length > 0 && !first.Equals(n, StringComparison.OrdinalIgnoreCase))
                    yield return first;
                var last = SlugFileStem(parts[^1]);
                if (last.Length > 0 &&
                    !last.Equals(n, StringComparison.OrdinalIgnoreCase) &&
                    !last.Equals(first, StringComparison.OrdinalIgnoreCase))
                    yield return last;
            }
        }

        var cat = NonSteamCoverBaseName(e);
        if (cat.Length > 0)
            yield return cat;
    }

    /// <summary>Zusätzliche Stämme für typische Tokens (z. B. <c>msedge</c> → <c>edge</c> für <c>edge.png</c>).</summary>
    static IEnumerable<string> TokenStemAliases(string token)
    {
        var t = token.Trim();
        if (t.Equals("msedge", StringComparison.OrdinalIgnoreCase))
        {
            yield return "edge";
            yield return "microsoft_edge";
        }

        if (t.Equals("iexplore", StringComparison.OrdinalIgnoreCase))
        {
            yield return "internet_explorer";
            yield return "ie";
        }
    }

    /// <summary>Gleiche Logik wie <see cref="GameMetadataEnricher"/> <c>NonSteamCoverBaseName</c> (IGDB-Dateiname).</summary>
    static string NonSteamCoverBaseName(CatalogGameEntry entry)
    {
        var raw = string.IsNullOrEmpty(entry.Key) ? entry.Name : entry.Key;
        var s = SanitizeCatalogKeyForFile(raw);
        if (s.Length > 0)
            return $"cat_{s}";

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16];
        return $"cat_{hash.ToLowerInvariant()}";
    }

    static string SanitizeCatalogKeyForFile(string key)
    {
        var sb = new StringBuilder();
        foreach (var c in key)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
                sb.Append(c);
            else if (c is ':' or '/' or '\\' or ' ')
                sb.Append('_');
        }

        var s = sb.ToString();
        if (s.Length > 96)
            s = s[..96];
        return s;
    }

    /// <summary>Kleinbuchstaben, alphanumerisch + Unterstrich (z. B. „Google Chrome“ → <c>google_chrome</c>).</summary>
    static string SlugFileStem(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.Trim())
        {
            if (char.IsAsciiLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
            else if (c is ' ' or '-' or '.' or '_' or ':' or '/' or '\\')
                sb.Append('_');
        }

        var t = sb.ToString();
        while (t.Contains("__", StringComparison.Ordinal))
            t = t.Replace("__", "_", StringComparison.Ordinal);
        return t.Trim('_');
    }
}
