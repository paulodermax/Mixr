using Mixr.Models;
using Mixr.Services;

namespace Mixr_App.Services;

/// <summary>Cover und Label für die Home-Zusammenfassung aus aktiven Audio-Sessions.</summary>
public static class SliderSummaryIconResolver
{
    public static string AssetPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", fileName);

    public static string NoInputPath => AssetPath("no-input.png");

    public static string GeneralSoundPath => AssetPath("general-sound.png");

    public static SliderSummaryInfo Resolve(int sliderIndex, MixrConfig cfg, IReadOnlyDictionary<string, IReadOnlyList<string>>? live)
    {
        if (sliderIndex < 0 || sliderIndex >= cfg.SliderMapping.Count)
            return SliderSummaryInfo.Empty();

        var key = cfg.SliderMapping[sliderIndex];

        if (key.Equals("master", StringComparison.OrdinalIgnoreCase))
        {
            return new SliderSummaryInfo(
                GeneralSoundPath,
                "System",
                "Windows default playback (master)",
                IsThemedAsset: true);
        }

        if (live == null ||
            !live.TryGetValue(key, out var activeNames) ||
            activeNames is not { Count: > 0 })
        {
            return SliderSummaryInfo.NoInput();
        }

        if (key.Equals("communication", StringComparison.OrdinalIgnoreCase))
        {
            var discordName = activeNames.FirstOrDefault(n =>
                n.Contains("discord", StringComparison.OrdinalIgnoreCase));
            if (discordName != null)
            {
                var full = CatalogCoverResolver.ResolveFullPathForLabel("discord");
                if (full != null)
                    return new SliderSummaryInfo(full, "Discord", discordName, IsThemedAsset: false);
            }
        }

        string? primaryLive = null;
        string? coverPath = null;
        foreach (var liveName in activeNames)
        {
            var path = LiveSessionCoverResolver.ResolveFullPath(liveName, key, cfg);
            if (path == null)
                continue;

            coverPath = path;
            primaryLive = liveName;
            break;
        }

        primaryLive ??= activeNames[0];

        if (coverPath != null)
        {
            return new SliderSummaryInfo(
                coverPath,
                ShortLabel(DisplayNameFor(primaryLive)),
                string.Join(" · ", activeNames),
                IsThemedAsset: false);
        }

        return new SliderSummaryInfo(
            NoInputPath,
            ShortLabel(DisplayNameFor(primaryLive)),
            string.Join(" · ", activeNames),
            IsThemedAsset: true);
    }

    static string DisplayNameFor(string label)
    {
        var store = GameCatalogStore.LoadOrCreate();
        return CatalogGameEntryLookup.FindBest(store, label)?.Name ?? label;
    }

    static string ShortLabel(string name)
    {
        if (name.Length <= 22)
            return name;
        return name[..19] + "…";
    }
}

public readonly record struct SliderSummaryInfo(
    string? ImagePath,
    string Label,
    string Tooltip,
    bool IsThemedAsset = false)
{
    public static SliderSummaryInfo Empty() => new(null, "", "");

    public static SliderSummaryInfo NoInput() =>
        new(SliderSummaryIconResolver.NoInputPath, "—", "No active audio session", IsThemedAsset: true);
}
