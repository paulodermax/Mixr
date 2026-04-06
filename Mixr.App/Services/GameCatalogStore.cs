using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mixr_App.Services;

public sealed class GameCatalogStore
{
    public int SchemaVersion { get; set; } = 1;

    public DateTime LastWeeklyCatalogUtc { get; set; }

    public DateTime LastDailyScanUtc { get; set; }

    public List<CatalogGameEntry> Games { get; set; } = new();

    public static GameCatalogStore LoadOrCreate()
    {
        GameCatalogPaths.EnsureLayout();
        var path = GameCatalogPaths.StoreJsonPath;
        if (!File.Exists(path))
            return new GameCatalogStore();

        try
        {
            var json = File.ReadAllText(path);
            var o = JsonSerializer.Deserialize<GameCatalogStore>(json, JsonOptions);
            return o ?? new GameCatalogStore();
        }
        catch
        {
            return new GameCatalogStore();
        }
    }

    public void Save()
    {
        GameCatalogPaths.EnsureLayout();
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(GameCatalogPaths.StoreJsonPath, json);
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed class CatalogGameEntry
{
    public string Key { get; set; } = "";

    public int SteamAppId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Wenn gesetzt: Suchstring für Audio-Sessions / Fader-Zuordnung (z. B. „Chrome“); Anzeige ist <see cref="Name"/>.</summary>
    public string? AssignmentToken { get; set; }

    public string? Summary { get; set; }

    /// <summary>Relativ zu <see cref="GameCatalogPaths.AppDataRoot"/> (z. B. covers/steam_730.jpg oder covers/cat_manual_MyGame.jpg).</summary>
    public string? CoverRelativePath { get; set; }

    public DateTime? LastApiFetchUtc { get; set; }

    /// <summary>Bis zu diesem Zeitpunkt gelten Titeltext/Cover ohne erneuten Abruf (wöchentlicher Zyklus).</summary>
    public DateTime? MetadataValidUntilUtc { get; set; }
}
