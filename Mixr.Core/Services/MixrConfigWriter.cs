using Mixr.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Mixr.Services;

/// <summary>
/// Schreibt <c>config.yaml</c> atomar (Temp-Datei + Ersetzen), damit ein Absturz mitten im Schreiben
/// keine halbe Datei hinterlässt. IGDB-Zugangsdaten werden bewusst nicht mitgeschrieben — sie gehören
/// in <c>config.secrets.yaml</c> (siehe <see cref="IgdbCredentialResolver"/>).
/// </summary>
public static class MixrConfigWriter
{
    public static void Save(MixrConfig cfg) => Save(cfg, MixrConfigPaths.ConfigYamlPath);

    public static void Save(MixrConfig cfg, string path)
    {
        var dto = new MixrYamlDto
        {
            Com_port = cfg.ComPort,
            Baud_rate = cfg.BaudRate,
            Invert_sliders = cfg.InvertSliders,
            Slider_mapping = cfg.SliderMapping.Count > 0 ? cfg.SliderMapping : null,
            Slider_response = cfg.SliderResponse.Count > 0 ? cfg.SliderResponse : null,
            Button_mapping = cfg.ButtonMapping.Count > 0 ? new List<string>(cfg.ButtonMapping) : null,
            Session_groups = cfg.SessionGroups.Count > 0 ? cfg.SessionGroups : null,
            Limit_system_sounds_to_20_percent = cfg.LimitSystemSoundsTo20Percent,
        };

        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        var yaml = serializer.Serialize(dto);
        AtomicFile.WriteAllText(path, yaml);
    }

    sealed class MixrYamlDto
    {
        public string? Com_port { get; set; }
        public int Baud_rate { get; set; }
        public bool Invert_sliders { get; set; }
        public List<string>? Slider_mapping { get; set; }
        public List<string>? Slider_response { get; set; }
        public List<string>? Button_mapping { get; set; }
        public Dictionary<string, List<string>>? Session_groups { get; set; }
        public bool? Limit_system_sounds_to_20_percent { get; set; }
    }
}
