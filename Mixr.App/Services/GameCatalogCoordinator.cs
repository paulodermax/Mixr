namespace Mixr_App.Services;

/// <summary>
/// Wöchentlicher Vollabgleich (Steam + Metadaten), täglich nur neue Installationen.
/// Keine Audio-Sessions.
/// </summary>
public static class GameCatalogCoordinator
{
    static readonly SemaphoreSlim Gate = new(1, 1);

    public static event EventHandler? CatalogChanged;

    public static async Task RunStartupAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var store = GameCatalogStore.LoadOrCreate();
            var now = DateTime.UtcNow;
            var todayLocal = DateTime.Today;

            var weeklyDue = store.LastWeeklyCatalogUtc == default || now - store.LastWeeklyCatalogUtc >= TimeSpan.FromDays(7);
            var dailyDue = store.LastDailyScanUtc == default || store.LastDailyScanUtc.ToLocalTime().Date < todayLocal;

            if (weeklyDue)
            {
                await RunWeeklyCatalogAsync(store, ct).ConfigureAwait(false);
                store.LastWeeklyCatalogUtc = now;
                store.LastDailyScanUtc = now;
            }
            else if (dailyDue)
            {
                await RunDailyScanAsync(store, ct).ConfigureAwait(false);
                store.LastDailyScanUtc = now;
            }

            store.Save();
            RaiseCatalogChanged();
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Manuell: wie wöchentlicher Lauf (Metadaten/Cover neu bis zur nächsten Woche).</summary>
    public static async Task ForceWeeklyRefreshAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var store = GameCatalogStore.LoadOrCreate();
            await RunWeeklyCatalogAsync(store, ct).ConfigureAwait(false);
            var now = DateTime.UtcNow;
            store.LastWeeklyCatalogUtc = now;
            store.LastDailyScanUtc = now;
            store.Save();
            RaiseCatalogChanged();
        }
        finally
        {
            Gate.Release();
        }
    }

    static async Task RunWeeklyCatalogAsync(GameCatalogStore store, CancellationToken ct)
    {
        var installed = SteamLibraryScanner.ScanInstalledGames();
        var byKey = store.Games.ToDictionary(g => g.Key, StringComparer.OrdinalIgnoreCase);
        var validUntil = DateTime.UtcNow.AddDays(7);

        foreach (var g in installed)
        {
            var key = FormattableString.Invariant($"steam:{g.AppId}");
            if (!byKey.TryGetValue(key, out var entry))
            {
                entry = new CatalogGameEntry { Key = key, SteamAppId = g.AppId, Name = g.Name };
                byKey[key] = entry;
            }
            else
            {
                entry.Name = g.Name;
                entry.SteamAppId = g.AppId;
            }

            await GameMetadataEnricher.TryEnrichAsync(entry, force: true, ct).ConfigureAwait(false);
            entry.MetadataValidUntilUtc = validUntil;
        }

        store.Games = byKey.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static async Task RunDailyScanAsync(GameCatalogStore store, CancellationToken ct)
    {
        var installed = SteamLibraryScanner.ScanInstalledGames();
        var byKey = store.Games.ToDictionary(g => g.Key, StringComparer.OrdinalIgnoreCase);
        var weekEnd = store.LastWeeklyCatalogUtc == default
            ? DateTime.UtcNow.AddDays(7)
            : store.LastWeeklyCatalogUtc.AddDays(7);

        foreach (var g in installed)
        {
            var key = FormattableString.Invariant($"steam:{g.AppId}");
            if (byKey.TryGetValue(key, out var existing))
            {
                existing.Name = g.Name;
                existing.SteamAppId = g.AppId;
                continue;
            }

            var entry = new CatalogGameEntry { Key = key, SteamAppId = g.AppId, Name = g.Name };
            await GameMetadataEnricher.TryEnrichAsync(entry, force: true, ct).ConfigureAwait(false);
            entry.MetadataValidUntilUtc = weekEnd;
            byKey[key] = entry;
        }

        store.Games = byKey.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static void RaiseCatalogChanged() => CatalogChanged?.Invoke(null, EventArgs.Empty);
}
