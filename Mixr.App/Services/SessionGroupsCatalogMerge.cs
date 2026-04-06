using Mixr.Models;
using Mixr.Services;

namespace Mixr_App.Services;

/// <summary>
/// Fügt installierte Steam-Spiele aus <see cref="GameCatalogStore"/> in <c>session_groups.games</c> ein.
/// Nicht-Steam-Apps (<c>app:…</c>) bleiben außen vor — die steuern Kommunikation/Media.
/// </summary>
public static class SessionGroupsCatalogMerge
{
    const int MaxGames = 512;

    /// <returns>True, wenn Einträge ergänzt wurden.</returns>
    public static bool MergeSteamGamesInto(MixrConfig cfg)
    {
        GameCatalogPaths.EnsureLayout();
        var store = GameCatalogStore.LoadOrCreate();

        var steamGames = store.Games
            .Where(e => e.Key.StartsWith("steam:", StringComparison.OrdinalIgnoreCase))
            .Where(e => !string.IsNullOrWhiteSpace(e.Name) && !CatalogIgnoreList.ShouldIgnore(e.Name))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (steamGames.Count == 0)
            return false;

        if (!cfg.SessionGroups.TryGetValue("games", out var list))
        {
            list = [];
            cfg.SessionGroups["games"] = list;
        }

        var existing = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var g in steamGames)
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
