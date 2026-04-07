namespace Mixr.Models;

public sealed class MixrConfig
{
    /// <summary>Leer oder „auto“: COM-Port per USB (ESP32-S3 Serial/JTAG VID/PID). Sonst fester Port z. B. COM7.</summary>
    public string ComPort { get; set; } = "";

    public int BaudRate { get; set; } = 921600;

    /// <summary>Consumer-Standard: automatische Erkennung ohne feste COM-Nummer.</summary>
    public bool IsComPortAuto =>
        string.IsNullOrWhiteSpace(ComPort) ||
        ComPort.Equals("auto", StringComparison.OrdinalIgnoreCase);

    /// <summary>ESP liefert 0–255; optional invertieren (wie paulodermax/Mixr).</summary>
    public bool InvertSliders { get; set; }

    /// <summary>Pro Slider: Ziel-Key für AudioService (master, communication, media, games).</summary>
    public List<string> SliderMapping { get; set; } =
    [
        "master",
        "communication",
        "media",
        "games",
    ];

    /// <summary>Gruppenname → Suchstrings für Fenster-/Prozessnamen (Audio-Sessions).</summary>
    public Dictionary<string, List<string>> SessionGroups { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Taster 0–4: Aktion (smtc_previous, smtc_play_pause, …). Immer 5 Einträge nach Normalisierung.</summary>
    public List<string> ButtonMapping { get; set; } =
    [
        "smtc_previous",
        "smtc_play_pause",
        "smtc_next",
        "discord_mute",
        "discord_deafen",
    ];

    /// <summary>Optional: Twitch Client-ID für IGDB. Wird von <c>IGDB_CLIENT_ID</c> überschrieben, falls gesetzt.</summary>
    public string? IgdbClientId { get; set; }

    /// <summary>Optional: Twitch Client Secret. Wird von <c>IGDB_CLIENT_SECRET</c> überschrieben, falls gesetzt.</summary>
    public string? IgdbClientSecret { get; set; }
}
