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
}
