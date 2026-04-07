using System.Linq;
using Mixr.Models;
using Mixr.Services;

namespace Mixr_App.Services;

/// <summary>
/// Lädt Cover für alle Programme/Spiele aus <c>session_groups</c> sobald wie möglich (vor weiterem UI-/Auto-Merge),
/// erzwungen mit <see cref="GameMetadataEnricher.TryEnrichAsync"/> — auch wenn der wöchentliche Katalogzyklus gerade nicht läuft.
/// Fehlt ein Eintrag im Katalog, wird für die Gruppe <c>games</c> ein <c>app:games:…</c>-Eintrag angelegt, damit IGDB nach Namen suchen kann.
/// </summary>
public static class CoverSessionGroupWarmup
{
    public static async Task RunAsync(GameCatalogStore store, CancellationToken ct)
    {
        MixrConfig cfg;
        try
        {
            cfg = MixrConfigLoader.Load(Array.Empty<string>());
        }
        catch
        {
            return;
        }

        var pairs = new List<(string GroupKey, string Token)>();
        foreach (var kv in cfg.SessionGroups)
        {
            if (kv.Value is null)
                continue;
            foreach (var raw in kv.Value)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                pairs.Add((kv.Key, raw.Trim()));
            }
        }

        if (pairs.Count == 0)
            return;

        foreach (var (groupKey, token) in pairs)
        {
            ct.ThrowIfCancellationRequested();

            var entry = CatalogGameEntryLookup.FindEntry(store, token);
            if (entry is null && groupKey.Equals("games", StringComparison.OrdinalIgnoreCase))
            {
                var key = FormattableString.Invariant($"app:{groupKey}:{token}");
                entry = store.Games.FirstOrDefault(g => g.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                {
                    entry = new CatalogGameEntry
                    {
                        Key = key,
                        Name = token,
                        AssignmentToken = token,
                        SteamAppId = 0,
                    };
                    store.Games.Add(entry);
                }
            }

            if (entry is null)
                continue;

            await GameMetadataEnricher.TryEnrichAsync(entry, force: true, ct).ConfigureAwait(false);
            entry.MetadataValidUntilUtc = DateTime.UtcNow.AddDays(7);
        }
    }
}
