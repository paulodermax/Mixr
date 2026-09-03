using Mixr.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Mixr.Services;

/// <summary>
/// IGDB/Twitch Client Credentials: <see cref="Environment"/> überschreibt Werte aus <c>config.yaml</c> und optional <c>config.secrets.yaml</c>.
/// Werte aus der Secrets-Datei werden nicht in <see cref="MixrConfig"/> gespiegelt, damit „Speichern“ in der UI sie nicht nach <c>config.yaml</c> schreibt.
/// </summary>
public static class IgdbCredentialResolver
{
    public const string EnvClientId = "IGDB_CLIENT_ID";
    public const string EnvClientSecret = "IGDB_CLIENT_SECRET";

    static readonly object LockObj = new();
    static string? _yamlClientId;
    static string? _yamlClientSecret;

    /// <summary>Optionaler Kanal für Diagnosen (nie Secret-Inhalte).</summary>
    public static Action<string>? DiagnosticLog { get; set; }

    /// <summary>Nach jedem erfolgreichen <see cref="MixrConfigLoader.Load"/> aufrufen.</summary>
    /// <param name="secretsPath">Vollständiger Pfad zu <c>config.secrets.yaml</c> (siehe <see cref="MixrConfigPaths.SecretsYamlPath"/>).</param>
    public static void LoadFromDisk(MixrConfig mainConfig, string secretsPath)
    {
        string? id = TrimOrNull(mainConfig.IgdbClientId);
        string? sec = TrimOrNull(mainConfig.IgdbClientSecret);

        if (File.Exists(secretsPath))
        {
            try
            {
                var yaml = File.ReadAllText(secretsPath);
                if (!string.IsNullOrWhiteSpace(yaml))
                {
                    var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(UnderscoredNamingConvention.Instance)
                        .IgnoreUnmatchedProperties()
                        .Build();
                    var root = deserializer.Deserialize<SecretsYamlRoot>(yaml);
                    if (root?.Igdb != null)
                    {
                        var sid = TrimOrNull(root.Igdb.Client_id);
                        var sse = TrimOrNull(root.Igdb.Client_secret);
                        if (sid != null)
                            id = sid;
                        if (sse != null)
                            sec = sse;
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog?.Invoke($"config.secrets.yaml nicht lesbar ({ex.GetType().Name}) — Werte aus config.yaml/Umgebung bleiben.");
            }
        }

        lock (LockObj)
        {
            _yamlClientId = id;
            _yamlClientSecret = sec;
        }
    }

    /// <summary>Schreibt (oder überschreibt) die Secrets-Datei atomar. Leere Werte lassen die Datei ohne igdb-Block.</summary>
    public static void WriteSecretsFile(string secretsPath, string? clientId, string? clientSecret)
    {
        var root = new SecretsYamlRoot();
        var id = TrimOrNull(clientId);
        var sec = TrimOrNull(clientSecret);
        if (id != null || sec != null)
            root.Igdb = new IgdbYaml { Client_id = id, Client_secret = sec };

        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

        var header =
            "# Mixr — lokale Zugangsdaten (Twitch/IGDB). Diese Datei wird nie in Updates überschrieben.\n" +
            "# Umgebungsvariablen IGDB_CLIENT_ID / IGDB_CLIENT_SECRET haben Vorrang.\n";
        AtomicFile.WriteAllText(secretsPath, header + serializer.Serialize(root));

        lock (LockObj)
        {
            _yamlClientId = id;
            _yamlClientSecret = sec;
        }
    }

    /// <summary>Aktuell aus Dateien geladene Werte (ohne Umgebungsvariablen) — für die Settings-UI.</summary>
    public static (string? clientId, string? clientSecret) GetFileValues()
    {
        lock (LockObj)
            return (_yamlClientId, _yamlClientSecret);
    }

    public static (string? clientId, string? clientSecret) Resolve()
    {
        var envId = TrimOrNull(Environment.GetEnvironmentVariable(EnvClientId));
        var envSec = TrimOrNull(Environment.GetEnvironmentVariable(EnvClientSecret));
        string? yId, ySec;
        lock (LockObj)
        {
            yId = _yamlClientId;
            ySec = _yamlClientSecret;
        }

        return (envId ?? yId, envSec ?? ySec);
    }

    /// <summary>Eine Zeile für Logs: keine Secrets, keine Längen die zum Raten einladen.</summary>
    public static string FormatDiagnosticSummary()
    {
        var envId = TrimOrNull(Environment.GetEnvironmentVariable(EnvClientId)) != null;
        var envSec = TrimOrNull(Environment.GetEnvironmentVariable(EnvClientSecret)) != null;
        string? yId, ySec;
        lock (LockObj)
        {
            yId = _yamlClientId;
            ySec = _yamlClientSecret;
        }

        var yamlId = yId != null;
        var yamlSec = ySec != null;
        var (effId, effSec) = Resolve();
        var ready = effId != null && effSec != null;

        static string Src(bool env, bool file) =>
            env ? "environment" : file ? "config file (config.yaml and/or config.secrets.yaml)" : "none";

        return
            $"Credentials: client_id←{Src(envId, yamlId)}; client_secret←{Src(envSec, yamlSec)}; " +
            $"effective={(ready ? "ready for IGDB API" : "incomplete — set both via environment variables or igdb: in YAML")}.";
    }

    static string? TrimOrNull(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        return s.Trim();
    }

    sealed class SecretsYamlRoot
    {
        public IgdbYaml? Igdb { get; set; }
    }

    sealed class IgdbYaml
    {
        public string? Client_id { get; set; }
        public string? Client_secret { get; set; }
    }
}
