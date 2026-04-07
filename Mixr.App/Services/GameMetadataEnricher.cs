using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Mixr.Services;
using Mixr_App;

namespace Mixr_App.Services;

/// <summary>
/// Cover: zuerst IGDB (t_cover_big), wenn Credentials gesetzt sind (Umgebung und/oder <c>config.yaml</c> / <c>config.secrets.yaml</c>).
/// Mit Steam-App-ID zusätzlich Steam-CDN-Fallback; ohne Steam-ID nur IGDB (Titelsuche).
/// </summary>
public static class GameMetadataEnricher
{
    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var c = new HttpClient();
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mixr/1.0 (Windows; game catalog)");
        c.Timeout = TimeSpan.FromSeconds(45);
        return c;
    }

    public static async Task TryEnrichAsync(CatalogGameEntry entry, bool force, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            entry.Summary ??= "";
            return;
        }

        if (!force && entry.MetadataValidUntilUtc.HasValue && DateTime.UtcNow < entry.MetadataValidUntilUtc.Value)
            return;

        GameCatalogPaths.EnsureLayout();

        if (entry.SteamAppId > 0)
            await TryEnrichSteamEntryAsync(entry, ct).ConfigureAwait(false);
        else
            await TryEnrichNonSteamEntryAsync(entry, ct).ConfigureAwait(false);
    }

    static async Task TryEnrichSteamEntryAsync(CatalogGameEntry entry, CancellationToken ct)
    {
        try
        {
            var manualRel = ManualCoverResolver.TryFindRelativePath(entry);
            if (!string.IsNullOrEmpty(manualRel))
            {
                var manualFull = GameCatalogPaths.ResolvePath(manualRel);
                if (File.Exists(manualFull))
                {
                    entry.CoverRelativePath = manualRel;
                    entry.Summary = $"Manuell · {entry.Name}";
                    entry.LastApiFetchUtc = DateTime.UtcNow;
                    return;
                }
            }

            var rel = $"covers/steam_{entry.SteamAppId}.jpg";
            var full = GameCatalogPaths.ResolvePath(rel);

            var igdbOk =
                await IgdbCoverService.TryDownloadSteamCoverAsync(entry.Name, entry.SteamAppId, full, ct)
                    .ConfigureAwait(false);

            if (!igdbOk)
            {
                var bytes = await TryDownloadCoverBytesAsync(entry.SteamAppId, ct).ConfigureAwait(false);
                if (bytes is { Length: > 0 })
                    await File.WriteAllBytesAsync(full, bytes, ct).ConfigureAwait(false);
            }

            if (File.Exists(full))
                entry.CoverRelativePath = rel.Replace('\\', '/');
        }
        catch (Exception ex)
        {
            AppLog.WriteLine($"[Cover] Steam enrich error '{entry.Name}' (appId={entry.SteamAppId}): {ex.Message}");
        }

        entry.Summary = $"Steam · {entry.Name}";
        entry.LastApiFetchUtc = DateTime.UtcNow;
    }

    static async Task TryEnrichNonSteamEntryAsync(CatalogGameEntry entry, CancellationToken ct)
    {
        try
        {
            var manualRel = ManualCoverResolver.TryFindRelativePath(entry);
            if (!string.IsNullOrEmpty(manualRel))
            {
                var manualFull = GameCatalogPaths.ResolvePath(manualRel);
                if (File.Exists(manualFull))
                {
                    entry.CoverRelativePath = manualRel;
                    entry.Summary = $"Manuell · {entry.Name}";
                    entry.LastApiFetchUtc = DateTime.UtcNow;
                    return;
                }
            }

            if (!string.IsNullOrEmpty(entry.CoverRelativePath))
            {
                var existingFull = GameCatalogPaths.ResolvePath(entry.CoverRelativePath);
                if (File.Exists(existingFull))
                {
                    entry.Summary ??= entry.Name;
                    entry.LastApiFetchUtc = DateTime.UtcNow;
                    return;
                }

                entry.CoverRelativePath = null;
            }

            var baseName = NonSteamCoverBaseName(entry);
            var rel = $"covers/{baseName}.jpg";
            var full = GameCatalogPaths.ResolvePath(rel);

            foreach (var candidate in EnumerateIgdbNameCandidates(entry.Name))
            {
                var ok = await IgdbCoverService.TryDownloadCoverByNameAsync(candidate, full, ct).ConfigureAwait(false);
                if (ok && File.Exists(full))
                {
                    entry.CoverRelativePath = rel.Replace('\\', '/');
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.WriteLine($"[Cover] Non-Steam enrich error '{entry.Name}': {ex.Message}");
        }

        entry.Summary ??= entry.Name;
        if (entry.CoverRelativePath is not null)
            entry.Summary = $"IGDB · {entry.Name}";
        entry.LastApiFetchUtc = DateTime.UtcNow;
    }

    /// <summary>Mehrere Suchstrings für IGDB, falls der exakte Listename keinen Treffer hat.</summary>
    static IEnumerable<string> EnumerateIgdbNameCandidates(string name)
    {
        var n = name.Trim();
        if (n.Length == 0)
            yield break;

        yield return n;

        var titleCased = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(n.ToLowerInvariant());
        if (!string.Equals(titleCased, n, StringComparison.Ordinal))
            yield return titleCased;

        var stripped = n.Replace("™", "", StringComparison.Ordinal).Replace("®", "", StringComparison.Ordinal).Trim();
        if (stripped.Length > 0 && !string.Equals(stripped, n, StringComparison.Ordinal))
            yield return stripped;

        var paren = n.IndexOf('(', StringComparison.Ordinal);
        if (paren > 2)
        {
            var q = n[..paren].TrimEnd();
            if (q.Length >= 2)
                yield return q;
        }

        var dash = n.IndexOf(" - ", StringComparison.Ordinal);
        if (dash >= 2)
        {
            var q = n[..dash].TrimEnd();
            if (q.Length >= 2)
                yield return q;
        }

        var colon = n.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 2 && colon < n.Length - 1)
        {
            var q = n[(colon + 1)..].TrimStart();
            if (q.Length >= 2)
                yield return q;
        }

        if (n.EndsWith(" Live", StringComparison.OrdinalIgnoreCase) && n.Length > 5)
        {
            var q = n[..^5].TrimEnd();
            if (q.Length >= 2)
                yield return q;
        }
    }

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

    static async Task<byte[]?> TryDownloadCoverBytesAsync(int appId, CancellationToken ct)
    {
        var urls = new[]
        {
            $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/library_600x900_2x.jpg",
            $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/library_600x900.jpg",
        };

        foreach (var url in urls)
        {
            try
            {
                using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    continue;
                var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                if (bytes.Length > 512)
                    return bytes;
            }
            catch
            {
                /* nächste URL */
            }
        }

        return null;
    }
}
