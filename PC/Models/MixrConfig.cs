namespace Mixr.Models;

/// <summary>Minimal wie workdir/config.yaml (nur COM + Baud; keine Slider-Mappings).</summary>
public sealed class MixrConfig
{
    public string ComPort { get; set; } = "COM6";
    public int BaudRate { get; set; } = 115200;
}
