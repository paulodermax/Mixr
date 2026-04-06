namespace Mixr.Services;

/// <summary>Taster 0–4: Aktionen aus config (button_mapping) und Ausführung im Host.</summary>
public static class MixrButtonActions
{
    public const string SmtcPrevious = "smtc_previous";
    public const string SmtcPlayPause = "smtc_play_pause";
    public const string SmtcNext = "smtc_next";
    public const string DiscordMute = "discord_mute";
    public const string DiscordDeafen = "discord_deafen";
    public const string None = "none";

    /// <summary>Standard wie bisher: 0 Prev, 1 Play, 2 Next, 3 Mute, 4 Deafen.</summary>
    public static readonly string[] Defaults =
    [
        SmtcPrevious,
        SmtcPlayPause,
        SmtcNext,
        DiscordMute,
        DiscordDeafen,
    ];

    public static IReadOnlyList<string> All { get; } =
    [
        SmtcPrevious,
        SmtcPlayPause,
        SmtcNext,
        DiscordMute,
        DiscordDeafen,
        None,
    ];

    /// <summary>Liefert die kanonische Aktion für Taster <paramref name="buttonId"/> (0–4).</summary>
    public static string Resolve(int buttonId, IReadOnlyList<string>? mapping)
    {
        if (buttonId < 0 || buttonId >= Defaults.Length)
            return None;
        var fallback = Defaults[buttonId];
        if (mapping is null || buttonId >= mapping.Count)
            return fallback;
        var raw = mapping[buttonId]?.Trim();
        if (string.IsNullOrEmpty(raw))
            return fallback;
        return Canonicalize(raw) ?? fallback;
    }

    static string? Canonicalize(string s)
    {
        foreach (var a in All)
        {
            if (a.Equals(s, StringComparison.OrdinalIgnoreCase))
                return a;
        }
        return s.ToLowerInvariant() switch
        {
            "prev" or "previous" => SmtcPrevious,
            "play" or "pause" or "playpause" => SmtcPlayPause,
            "next" => SmtcNext,
            "mute" => DiscordMute,
            "deafen" => DiscordDeafen,
            "noop" or "off" => None,
            _ => null,
        };
    }

    public static void EnsureFiveEntries(List<string> list)
    {
        while (list.Count < Defaults.Length)
            list.Add(Defaults[list.Count]);
        if (list.Count > Defaults.Length)
            list.RemoveRange(Defaults.Length, list.Count - Defaults.Length);
    }
}
