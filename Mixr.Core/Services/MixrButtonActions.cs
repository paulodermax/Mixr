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

    /// <summary>
    /// HID-Consumer-Usage für eine Aktion — Medientasten führt das Gerät selbst aus (funktioniert ohne App),
    /// alles andere (Discord, none) bleibt Host-Sache (0).
    /// </summary>
    public static ushort HidUsageFor(string action) => action switch
    {
        SmtcPrevious => MixrProtocol.HidUsage.ScanPrev,
        SmtcPlayPause => MixrProtocol.HidUsage.PlayPause,
        SmtcNext => MixrProtocol.HidUsage.ScanNext,
        _ => MixrProtocol.HidUsage.None,
    };

    /// <summary>SET_BUTTON_MAP-Nutzlast (5 × u16 LE) aus der Konfiguration.</summary>
    public static byte[] BuildHidButtonMap(IReadOnlyList<string>? mapping)
    {
        var payload = new byte[MixrProtocol.ButtonCount * 2];
        for (var i = 0; i < MixrProtocol.ButtonCount; i++)
        {
            var usage = HidUsageFor(Resolve(i, mapping));
            payload[i * 2] = (byte)(usage & 0xFF);
            payload[i * 2 + 1] = (byte)(usage >> 8);
        }

        return payload;
    }

    /// <summary>true, wenn das Gerät diese Aktion per HID selbst ausführt und der Host sie nicht wiederholen darf.</summary>
    public static bool IsHandledByDeviceHid(string action, DeviceHello? device) =>
        device is { SupportsHidConsumer: true } && HidUsageFor(action) != MixrProtocol.HidUsage.None;

    public static void EnsureFiveEntries(List<string> list)
    {
        while (list.Count < Defaults.Length)
            list.Add(Defaults[list.Count]);
        if (list.Count > Defaults.Length)
            list.RemoveRange(Defaults.Length, list.Count - Defaults.Length);
    }
}
