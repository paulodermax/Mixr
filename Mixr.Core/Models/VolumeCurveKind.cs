namespace Mixr.Models;

/// <summary>Regler → Lautstärke Kurventyp (pro Slider in config).</summary>
public enum VolumeCurveKind
{
    Linear,
    CenterFlattened,
    GammaLow,
    GammaHigh,
    SCurve,
}
