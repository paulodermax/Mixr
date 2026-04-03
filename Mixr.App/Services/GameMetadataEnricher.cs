namespace Mixr_App.Services;

/// <summary>
/// Lädt Cover von Steam-CDN (ohne API-Key) und setzt kurzen Beschreibungstext.
/// Später: IGDB/OpenCritic ergänzen, wenn Schlüssel konfiguriert sind.
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
        if (entry.SteamAppId <= 0)
        {
            entry.Summary ??= entry.Name;
            return;
        }

        if (!force && entry.MetadataValidUntilUtc.HasValue && DateTime.UtcNow < entry.MetadataValidUntilUtc.Value)
            return;

        GameCatalogPaths.EnsureLayout();

        try
        {
            var bytes = await TryDownloadCoverBytesAsync(entry.SteamAppId, ct).ConfigureAwait(false);
            if (bytes is { Length: > 0 })
            {
                var rel = $"covers/steam_{entry.SteamAppId}.jpg";
                var full = GameCatalogPaths.ResolvePath(rel);
                await File.WriteAllBytesAsync(full, bytes, ct).ConfigureAwait(false);
                entry.CoverRelativePath = rel.Replace('\\', '/');
            }
        }
        catch
        {
            /* CDN kann 404 liefern — ignorieren */
        }

        entry.Summary = $"Steam · {entry.Name}";
        entry.LastApiFetchUtc = DateTime.UtcNow;
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
