using System;
using System.IO;
using Mixr.Services;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Mixr.Services;

public class ConfigService
{
    public AppConfig Current { get; private set; } = new();

    public bool Load(string path)
    {
        try
        {
            var yamlContent = File.ReadAllText(path);

            // Toleranter Builder: Ignoriert Groß/Klein und unbekannte Felder
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance) 
                .IgnoreUnmatchedProperties() // WICHTIG: Verhindert Absturz bei Kommentaren/Extras
                .Build();
            
            Current = deserializer.Deserialize<AppConfig>(yamlContent);
            return true;
        }
        catch (Exception ex)
        {
            // Gibt den ECHTEN Fehler in der Konsole aus
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"YAML PARSE ERROR: {ex.Message}");
            if (ex.InnerException != null) Console.WriteLine($"Details: {ex.InnerException.Message}");
            Console.ResetColor();
            
            LoggerService.Error("Config Load Error", ex);
            return false;
        }
    }
}

// Das Datenmodell muss exakt zur YAML passen
public class AppConfig
{
    public string com_port { get; set; } = "COM11";
    public int baud_rate { get; set; } = 230400;
    
    // Initialisiere Listen, damit sie nie null sind
    public List<string> slider_mapping { get; set; } = new();
    public Dictionary<string, List<string>> session_groups { get; set; } = new();
    public List<string> whitelist { get; set; } = new();
    
    public bool invert_sliders { get; set; } = true;
    public string noise_reduction { get; set; } = "default";
}