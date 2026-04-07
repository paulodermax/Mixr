using Mixr.Models;
using Mixr.Services;

namespace Mixr_App.Services;

/// <summary>
/// Fügt installierte Spiele aus <see cref="GameCatalogStore"/> (Steam, Epic, GOG, …) in <c>session_groups.games</c> ein.
/// Nur <c>app:…</c> (Kommunikation/Media aus der Erkennung) bleiben außen vor.
/// </summary>
public static class SessionGroupsCatalogMerge
{
    const int MaxGames = 512;

    /// <returns>True, wenn Einträge ergänzt wurden.</returns>
    public static bool MergeSteamGamesInto(MixrConfig cfg)
    {
        GameCatalogPaths.EnsureLayout();
        var store = GameCatalogStore.LoadOrCreate();

        var gameEntries = store.Games
            .Where(e => !e.Key.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
            .Where(e => !string.IsNullOrWhiteSpace(e.Name) && !CatalogIgnoreList.ShouldIgnore(e.Name))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (gameEntries.Count == 0)
            return false;

        if (!cfg.SessionGroups.TryGetValue("games", out var list))
        {
            list = [];
            cfg.SessionGroups["games"] = list;
        }

        var existing = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var g in gameEntries)
        {
            var token = g.Name.Trim();
            if (existing.Count >= MaxGames)
                break;

            if (existing.Add(token))
            {
                list.Add(token);
                changed = true;
            }
        }

        return changed;
    }
}
