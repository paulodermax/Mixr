using System.Text.RegularExpressions;
using Mixr.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Mixr.Services;

public static class MixrConfigLoader
{
    static readonly Regex PortArg = new(@"^--(?:port|com)=?(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex BaudArg = new(@"^--baud=(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Optionaler Kanal für Warnungen (z. B. defekte YAML) — kein Konsolen-Zwang in der Library.</summary>
    public static Action<string>? DiagnosticLog { get; set; }

    public static MixrConfig Load(string[] args)
    {
        try
        {
            var note = MixrConfigPaths.EnsureLayout();
            if (note != null)
                DiagnosticLog?.Invoke(note);
        }
        catch (Exception ex)
        {
            DiagnosticLog?.Invoke($"Konfigurationsordner konnte nicht angelegt werden: {ex.Message}");
        }

        var path = MixrConfigPaths.ConfigYamlPath;
        MixrConfig cfg;

        if (File.Exists(path))
        {
            var yaml = File.ReadAllText(path);
            if (!string.IsNullOrWhiteSpace(yaml))
            {
                try
                {
                    var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(UnderscoredNamingConvention.Instance)
                        .IgnoreUnmatchedProperties()
                        .Build();
                    var y = deserializer.Deserialize<MixrYaml>(yaml);
                    cfg = (y ?? new MixrYaml()).ToConfig();
                }
                catch (Exception ex)
                {
                    DiagnosticLog?.Invoke($"config.yaml ungültig, Standardwerte aktiv: {ex.Message}");
                    cfg = new MixrConfig();
                }
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

        MigrateInlineSecretsToSecretsFile(cfg);

        for (var i = 0; i < args.Length; i++)
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

        IgdbCredentialResolver.LoadFromDisk(cfg, MixrConfigPaths.SecretsYamlPath);
        return cfg;
    }

    /// <summary>
    /// Ältere Konfigurationen hatten <c>igdb:</c>-Zugangsdaten direkt in der <c>config.yaml</c>.
    /// Diese werden einmalig in <c>config.secrets.yaml</c> übernommen (falls dort noch nichts steht);
    /// der Writer schreibt danach keine Secrets mehr in die Hauptdatei.
    /// </summary>
    static void MigrateInlineSecretsToSecretsFile(MixrConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.IgdbClientId) && string.IsNullOrWhiteSpace(cfg.IgdbClientSecret))
            return;

        try
        {
            if (!File.Exists(MixrConfigPaths.SecretsYamlPath))
            {
                IgdbCredentialResolver.WriteSecretsFile(
                    MixrConfigPaths.SecretsYamlPath, cfg.IgdbClientId, cfg.IgdbClientSecret);
                DiagnosticLog?.Invoke("IGDB-Zugangsdaten aus config.yaml nach config.secrets.yaml verschoben.");
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog?.Invoke($"Secrets-Migration fehlgeschlagen: {ex.Message}");
        }
    }

    sealed class MixrYaml
    {
        public string? Com_port { get; set; }
        public int? Baud_rate { get; set; }
        public bool? Invert_sliders { get; set; }
        public List<string>? Slider_mapping { get; set; }
        public List<string>? Slider_response { get; set; }
        public List<string>? Button_mapping { get; set; }
        public Dictionary<string, List<string>>? Session_groups { get; set; }
        public bool? Limit_system_sounds_to_20_percent { get; set; }
        public IgdbYaml? Igdb { get; set; }

        public MixrConfig ToConfig()
        {
            var c = new MixrConfig();
            if (!string.IsNullOrWhiteSpace(Com_port))
                c.ComPort = Com_port.Trim();
            if (Baud_rate is > 0)
                c.BaudRate = Baud_rate.Value;
            if (Invert_sliders.HasValue)
                c.InvertSliders = Invert_sliders.Value;
            if (Slider_mapping is { Count: > 0 })
                c.SliderMapping = new List<string>(Slider_mapping);
            if (Slider_response is { Count: > 0 })
                c.SliderResponse = new List<string>(Slider_response);
            VolumeCurveMapper.EnsureFourEntries(c.SliderResponse);
            if (Button_mapping is { Count: > 0 })
            {
                c.ButtonMapping = new List<string>(Button_mapping);
                MixrButtonActions.EnsureFiveEntries(c.ButtonMapping);
            }

            if (Session_groups is { Count: > 0 })
                c.SessionGroups = new Dictionary<string, List<string>>(Session_groups, StringComparer.OrdinalIgnoreCase);

            if (Limit_system_sounds_to_20_percent.HasValue)
                c.LimitSystemSoundsTo20Percent = Limit_system_sounds_to_20_percent.Value;

            VolumeCurveMapper.EnsureFourEntries(c.SliderResponse);

            if (Igdb != null)
            {
                if (!string.IsNullOrWhiteSpace(Igdb.Client_id))
                    c.IgdbClientId = Igdb.Client_id.Trim();
                if (!string.IsNullOrWhiteSpace(Igdb.Client_secret))
                    c.IgdbClientSecret = Igdb.Client_secret.Trim();
            }

            return c;
        }
    }

    sealed class IgdbYaml
    {
        public string? Client_id { get; set; }
        public string? Client_secret { get; set; }
    }
}
