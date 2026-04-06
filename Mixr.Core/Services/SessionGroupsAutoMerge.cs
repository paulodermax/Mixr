using Mixr.Models;

namespace Mixr.Services;

/// <summary>Fügt erkannte installierte Apps in session_groups ein (additiv).</summary>
public static class SessionGroupsAutoMerge
{
    const int MaxPerGroup = 64;

    /// <returns>True, wenn Einträge ergänzt wurden.</returns>
    public static bool MergeDetectedInto(MixrConfig cfg)
    {
        var suggestions = InstalledAppDetector.DetectSuggestions();
        if (suggestions.Count == 0)
            return false;

        var changed = false;
        foreach (var s in suggestions)
        {
            if (SessionGroupsExcludedTokens.ShouldSkipMerge(s.SearchToken))
                continue;

            if (!cfg.SessionGroups.TryGetValue(s.GroupKey, out var list))
            {
                list = new List<string>();
                cfg.SessionGroups[s.GroupKey] = list;
            }

            if (list.Count >= MaxPerGroup)
                continue;

            if (list.Any(x => x.Equals(s.SearchToken, StringComparison.OrdinalIgnoreCase)))
                continue;

            list.Add(s.SearchToken);
            changed = true;
        }

        return changed;
    }
}
