using Mixr.Models;

namespace Mixr_App.Services;

/// <summary>
/// Entfernt Store-/Launcher-Tokens aus <c>session_groups</c>, die nicht mehr automatisch ergänzt werden sollen
/// (bleiben sonst dauerhaft in der gespeicherten <c>config.yaml</c>).
/// </summary>
public static class SessionGroupsLauncherPrune
{
    static readonly HashSet<string> LauncherTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "Steam",
        "Battle.net",
        "Epic",
        "Epic Games Launcher",
        "GOG Galaxy",
        "UbisoftConnect",
        "EADesktop",
        "RiotClientServices",
        "Riot",
        "Riot Client",
        "msedge",
    };

    /// <returns>True, wenn etwas entfernt wurde.</returns>
    public static bool RemoveLauncherTokensFromAllGroups(MixrConfig cfg)
    {
        var changed = false;
        foreach (var list in cfg.SessionGroups.Values)
        {
            for (var i = list.Count - 1; i >= 0; i--)
            {
                var t = list[i]?.Trim() ?? "";
                if (t.Length == 0)
                {
                    list.RemoveAt(i);
                    changed = true;
                    continue;
                }

                if (LauncherTokens.Contains(t))
                {
                    list.RemoveAt(i);
                    changed = true;
                }
            }
        }

        return changed;
    }
}
