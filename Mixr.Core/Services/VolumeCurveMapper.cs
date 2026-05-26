using Mixr.Models;

namespace Mixr.Services;

/// <summary>Regler 0–1 → Lautstärke 0–1. LUT für schnellen Hot-Path im Host.</summary>
public static class VolumeCurveMapper
{
    public const int LutSize = 256;

    public sealed record PresetInfo(VolumeCurveKind Kind, string YamlKey, string Title, string Description);

    public static readonly PresetInfo[] Presets =
    [
        new(VolumeCurveKind.Linear, "linear", "Linear", "Gleichmäßig: Reglerstellung = Lautstärke."),
        new(VolumeCurveKind.CenterFlattened, "center_flattened", "Mitte abgeflacht", "Weniger empfindlich in der Mitte (Mixr-Standard)."),
        new(VolumeCurveKind.GammaLow, "gamma_low", "Leise feiner", "Mehr Auflösung bei niedriger Lautstärke."),
        new(VolumeCurveKind.GammaHigh, "gamma_high", "Laut feiner", "Mehr Auflösung bei hoher Lautstärke."),
        new(VolumeCurveKind.SCurve, "s_curve", "S-Kurve", "Weich an 0 % und 100 %."),
    ];

    public static VolumeCurveKind Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return VolumeCurveKind.CenterFlattened;

        var v = value.Trim().Replace('-', '_');
        foreach (var p in Presets)
        {
            if (p.YamlKey.Equals(v, StringComparison.OrdinalIgnoreCase))
                return p.Kind;
        }

        return VolumeCurveKind.CenterFlattened;
    }

    public static string ToYamlKey(VolumeCurveKind kind)
    {
        foreach (var p in Presets)
        {
            if (p.Kind == kind)
                return p.YamlKey;
        }

        return "center_flattened";
    }

    public static VolumeCurveKind GetKindForSlider(MixrConfig cfg, int sliderIndex)
    {
        VolumeCurveMapper.EnsureFourEntries(cfg.SliderResponse);
        return Parse(cfg.SliderResponse[sliderIndex]);
    }

    public static void EnsureFourEntries(List<string> list)
    {
        var defaults = DefaultResponseList();
        while (list.Count < 4)
            list.Add(defaults[list.Count]);
        if (list.Count > 4)
            list.RemoveRange(4, list.Count - 4);
    }

    public static List<string> DefaultResponseList() =>
    [
        ToYamlKey(VolumeCurveKind.CenterFlattened),
        ToYamlKey(VolumeCurveKind.CenterFlattened),
        ToYamlKey(VolumeCurveKind.CenterFlattened),
        ToYamlKey(VolumeCurveKind.CenterFlattened),
    ];

    public static float[] BuildLut(VolumeCurveKind kind)
    {
        var lut = new float[LutSize];
        for (var i = 0; i < LutSize; i++)
            lut[i] = Map(kind, i / (float)(LutSize - 1));
        return lut;
    }

    public static float Map(VolumeCurveKind kind, float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return kind switch
        {
            VolumeCurveKind.Linear => x,
            VolumeCurveKind.CenterFlattened => MapCenterFlattened(x, 2f),
            VolumeCurveKind.GammaLow => MathF.Pow(x, 0.55f),
            VolumeCurveKind.GammaHigh => MathF.Pow(x, 1.8f),
            VolumeCurveKind.SCurve => MapSCurve(x),
            _ => x,
        };
    }

    static float MapCenterFlattened(float x, float power)
    {
        var t = 2f * x - 1f;
        var y = MathF.Sign(t) * MathF.Pow(MathF.Abs(t), power);
        return 0.5f + 0.5f * y;
    }

    static float MapSCurve(float x) =>
        x * x * (3f - 2f * x);
}
