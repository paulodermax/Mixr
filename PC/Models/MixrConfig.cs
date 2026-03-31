namespace Mixr.Models;

public sealed class MixrConfig
{
    public string ComPort { get; set; } = "COM6";
    public int BaudRate { get; set; } = 921600;

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
