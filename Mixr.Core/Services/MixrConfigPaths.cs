namespace Mixr.Services;

/// <summary>
/// Alle beschreibbaren Pfade der App liegen unter <c>%LOCALAPPDATA%\Mixr</c> (überschreibbar mit
/// <c>MIXR_DATA_DIR</c>). Der Programmordner ist nur lesbar: Velopack ersetzt ihn bei jedem Update komplett.
/// </summary>
public static class MixrConfigPaths
{
    public const string EnvDataDir = "MIXR_DATA_DIR";

    /// <summary>Name der mitgelieferten Default-Konfiguration im Programmordner.</summary>
    public const string BundledDefaultFileName = "config.default.yaml";

    public const string ConfigFileName = "config.yaml";
    public const string SecretsFileName = "config.secrets.yaml";

    static readonly Lazy<string> DataRootLazy = new(ResolveDataRoot);

    public static string DataRoot => DataRootLazy.Value;

    public static string ConfigYamlPath => Path.Combine(DataRoot, ConfigFileName);

    public static string SecretsYamlPath => Path.Combine(DataRoot, SecretsFileName);

    public static string LogDir => Path.Combine(DataRoot, "logs");

    /// <summary>Firmware-Images, die die App mitbringt (<c>firmware\Mixr.bin</c> neben der EXE).</summary>
    public static string BundledFirmwareDir => Path.Combine(AppContext.BaseDirectory, "firmware");

    public static string BundledDefaultConfigPath => Path.Combine(AppContext.BaseDirectory, BundledDefaultFileName);

    static string ResolveDataRoot()
    {
        var env = Environment.GetEnvironmentVariable(EnvDataDir);
        if (!string.IsNullOrWhiteSpace(env))
            return Path.GetFullPath(env.Trim());

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mixr");
    }

    /// <summary>
    /// Legt <see cref="DataRoot"/> an und sorgt dafür, dass eine <c>config.yaml</c> existiert:
    /// zuerst Migration einer alten Konfiguration neben der EXE (Installationen vor Velopack), sonst Kopie der Default-Datei.
    /// Gibt eine Beschreibung des durchgeführten Schritts zurück (für das Log) oder <c>null</c>.
    /// </summary>
    public static string? EnsureLayout()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(LogDir);

        string? note = null;
        var legacyDir = AppContext.BaseDirectory;

        if (!File.Exists(ConfigYamlPath))
        {
            var legacyConfig = Path.Combine(legacyDir, ConfigFileName);
            if (File.Exists(legacyConfig) && !IsUnderDataRoot(legacyConfig))
            {
                File.Copy(legacyConfig, ConfigYamlPath, overwrite: false);
                TryDelete(legacyConfig);
                note = $"config.yaml aus dem Programmordner nach {DataRoot} migriert.";
            }
            else if (File.Exists(BundledDefaultConfigPath))
            {
                File.Copy(BundledDefaultConfigPath, ConfigYamlPath, overwrite: false);
                note = $"Standard-config.yaml nach {DataRoot} kopiert.";
            }
        }

        if (!File.Exists(SecretsYamlPath))
        {
            var legacySecrets = Path.Combine(legacyDir, SecretsFileName);
            if (File.Exists(legacySecrets) && !IsUnderDataRoot(legacySecrets))
            {
                File.Copy(legacySecrets, SecretsYamlPath, overwrite: false);
                TryDelete(legacySecrets);
                note = (note is null ? "" : note + " ") + "config.secrets.yaml migriert.";
            }
        }

        return note;
    }

    static bool IsUnderDataRoot(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(DataRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            /* Programmordner evtl. schreibgeschützt — Kopie in AppData reicht */
        }
    }
}
