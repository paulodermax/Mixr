using Mixr.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Mixr.Services;

public static class MixrConfigWriter
{
    public static void Save(MixrConfig cfg, string path)
    {
        var dto = new MixrYamlDto
        {
            Com_port = cfg.ComPort,
            Baud_rate = cfg.BaudRate,
            Invert_sliders = cfg.InvertSliders,
            Slider_mapping = cfg.SliderMapping.Count > 0 ? cfg.SliderMapping : null,
            Session_groups = cfg.SessionGroups.Count > 0 ? cfg.SessionGroups : null,
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
        public Dictionary<string, List<string>>? Session_groups { get; set; }
    }
}
