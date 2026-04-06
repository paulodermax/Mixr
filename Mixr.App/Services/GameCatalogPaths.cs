namespace Mixr_App.Services;

public static class GameCatalogPaths
{
    public static string AppDataRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mixr");

    public static string StoreJsonPath => Path.Combine(AppDataRoot, "game_catalog.json");

    /// <summary>Textdatei: Einträge, die bei Katalog/Steam/Install-Erkennung ignoriert werden (siehe <see cref="CatalogIgnoreList"/>).</summary>
    public static string CatalogIgnoreListPath => Path.Combine(AppDataRoot, "catalog_ignore.txt");

    public static string CoversDir => Path.Combine(AppDataRoot, "covers");

    public static void EnsureLayout()
    {
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(CoversDir);
    }

    static readonly string[] CoverImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    /// <summary>
    /// Kopiert Bilder aus <c>covers\</c> neben der EXE nach <see cref="CoversDir"/> (%LocalAppData%\Mixr\covers).
    /// Fehlt die Datei in AppData oder ist die EXE-Version neuer, wird überschrieben.
    /// </summary>
    public static void SyncBundledCoversToAppData()
    {
        try
        {
            EnsureLayout();
            var srcDir = Path.Combine(AppContext.BaseDirectory, "covers");
            if (!Directory.Exists(srcDir))
                return;

            foreach (var ext in CoverImageExtensions)
            {
                foreach (var src in Directory.EnumerateFiles(srcDir, "*" + ext, SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(src);
                    if (name.StartsWith(".", StringComparison.Ordinal))
                        continue;

                    var dest = Path.Combine(CoversDir, name);
                    if (!ShouldCopyBundledCover(src, dest))
                        continue;

                    File.Copy(src, dest, overwrite: true);
                }
            }
        }
        catch
        {
            /* optional */
        }
    }

    static bool ShouldCopyBundledCover(string src, string dest)
    {
        if (!File.Exists(dest))
            return true;
        return File.GetLastWriteTimeUtc(src) > File.GetLastWriteTimeUtc(dest);
    }

    public static string ResolvePath(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return "";
        EnsureLayout();
        var rel = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var inAppData = Path.Combine(AppDataRoot, rel);
        if (File.Exists(inAppData))
            return inAppData;

        var besideExe = Path.Combine(AppContext.BaseDirectory, rel);
        if (File.Exists(besideExe))
            return besideExe;

        return inAppData;
    }
}
