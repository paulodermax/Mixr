using Mixr.Models;
using Mixr.Services;

namespace Mixr_App.Services;

/// <summary>Cover-Pfad für eine laufende Audio-Session (Label + Zuordnung aus session_groups).</summary>
public static class LiveSessionCoverResolver
{
    public static string? ResolveFullPath(string liveLabel, string sliderKey, MixrConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(liveLabel))
            return null;

        var direct = CatalogCoverResolver.ResolveFullPathForLabel(liveLabel);
        if (FileExists(direct))
            return direct;

        if (!cfg.SessionGroups.TryGetValue(sliderKey, out var assigned))
            return null;

        foreach (var token in assigned)
        {
            if (!SessionTokenMatcher.Matches(liveLabel, token))
                continue;

            var fromToken = CatalogCoverResolver.ResolveFullPathForLabel(token);
            if (FileExists(fromToken))
                return fromToken;
        }

        return null;
    }

    static bool FileExists(string? path) =>
        !string.IsNullOrEmpty(path) && File.Exists(path);
}
