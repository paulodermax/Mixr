namespace Mixr_App.Services;

public static class GameCatalogPaths
{
    public static string AppDataRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mixr");

    public static string StoreJsonPath => Path.Combine(AppDataRoot, "game_catalog.json");

    public static string CoversDir => Path.Combine(AppDataRoot, "covers");

    public static void EnsureLayout()
    {
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(CoversDir);
    }

    public static string ResolvePath(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return "";
        EnsureLayout();
        return Path.Combine(AppDataRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
