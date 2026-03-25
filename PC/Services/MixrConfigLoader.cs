using System.Text.RegularExpressions;
using Mixr.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Mixr.Services;

public static class MixrConfigLoader
{
    static readonly Regex PortArg = new(@"^--(?:port|com)=?(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex BaudArg = new(@"^--baud=(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static MixrConfig Load(string[] args)
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, "config.yaml");
        MixrConfig cfg;

        if (File.Exists(path))
        {
            var yaml = File.ReadAllText(path);
            if (!string.IsNullOrWhiteSpace(yaml))
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();
                var y = deserializer.Deserialize<MixrYaml>(yaml);
                cfg = (y ?? new MixrYaml()).ToConfig();
            }
            else
            {
                cfg = new MixrConfig();
            }
        }
        else
        {
            cfg = new MixrConfig();
        }

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.Equals("--port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                cfg.ComPort = args[++i].Trim();
                continue;
            }

            if (a.Equals("--baud", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length &&
                int.TryParse(args[++i], out var baudArg) && baudArg > 0)
            {
                cfg.BaudRate = baudArg;
                continue;
            }

            var m = PortArg.Match(a);
            if (m.Success)
            {
                cfg.ComPort = m.Groups[1].Value.Trim();
                continue;
            }

            m = BaudArg.Match(a);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var baud) && baud > 0)
                cfg.BaudRate = baud;
        }

        return cfg;
    }

    sealed class MixrYaml
    {
        public string? Com_port { get; set; }
        public int? Baud_rate { get; set; }
        public int? Discord_mute_button { get; set; }
        public int? Voip_mute_button { get; set; }

        public MixrConfig ToConfig()
        {
            var c = new MixrConfig();
            if (!string.IsNullOrWhiteSpace(Com_port))
                c.ComPort = Com_port.Trim();
            if (Baud_rate is > 0)
                c.BaudRate = Baud_rate.Value;
            var btn = Voip_mute_button ?? Discord_mute_button;
            if (btn is >= -1 and <= 4)
                c.VoipMuteButton = btn.Value;
            return c;
        }
    }
}
