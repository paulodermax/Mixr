using System.Security.Cryptography;
using System.Text;
using Mixr.Services;

namespace Mixr_App.Services;

/// <summary>
/// Cover: zuerst IGDB (t_cover_big), wenn <c>IGDB_CLIENT_ID</c>/<c>IGDB_CLIENT_SECRET</c> gesetzt sind.
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
        catch
        {
            /* IGDB/CDN können ausfallen — ignorieren */
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
                    entry.Summary ??= entry.Name;
                    entry.LastApiFetchUtc = DateTime.UtcNow;
                    return;
                }
            }

            var baseName = NonSteamCoverBaseName(entry);
            var rel = $"covers/{baseName}.jpg";
            var full = GameCatalogPaths.ResolvePath(rel);

            var ok = await IgdbCoverService.TryDownloadCoverByNameAsync(entry.Name, full, ct).ConfigureAwait(false);
            if (ok && File.Exists(full))
                entry.CoverRelativePath = rel.Replace('\\', '/');
        }
        catch
        {
            /* IGDB kann ausfallen — ignorieren */
        }

        entry.Summary ??= entry.Name;
        if (entry.CoverRelativePath is not null)
            entry.Summary = $"IGDB · {entry.Name}";
        entry.LastApiFetchUtc = DateTime.UtcNow;
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
