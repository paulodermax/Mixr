using Mixr.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Mixr.Services;

public static class MixrConfigWriter
{
    public static void Save(MixrConfig cfg, string path)
    {
        IgdbYamlDto? igdb = null;
        if (!string.IsNullOrWhiteSpace(cfg.IgdbClientId) || !string.IsNullOrWhiteSpace(cfg.IgdbClientSecret))
        {
            igdb = new IgdbYamlDto();
            if (!string.IsNullOrWhiteSpace(cfg.IgdbClientId))
                igdb.Client_id = cfg.IgdbClientId.Trim();
            if (!string.IsNullOrWhiteSpace(cfg.IgdbClientSecret))
                igdb.Client_secret = cfg.IgdbClientSecret.Trim();
        }

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
            Igdb = igdb,
        };

        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        var yaml = serializer.Serialize(dto);
        File.WriteAllText(path, yaml);
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
        public IgdbYamlDto? Igdb { get; set; }
    }

    sealed class IgdbYamlDto
    {
        public string? Client_id { get; set; }
        public string? Client_secret { get; set; }
    }
}
