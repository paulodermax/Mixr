namespace Mixr_App.Services;

/// <summary>
/// Einheitliche Auflösung: <b>lokale manuelle Dateien</b> unter <c>covers/</c> schlagen gespeicherte IGDB-/Steam-Pfade,
/// damit z. B. <c>discord.png</c> immer gewinnt, auch wenn noch ein alter Katalogeintrag existiert.
/// </summary>
public static class CatalogCoverResolver
{
    public static string? ResolveRelativePath(CatalogGameEntry entry)
    {
        var manual = ManualCoverResolver.TryFindRelativePath(entry);
        if (!string.IsNullOrEmpty(manual))
        {
            var mfull = GameCatalogPaths.ResolvePath(manual);
            if (File.Exists(mfull))
                return manual;
        }

        var rel = entry.CoverRelativePath;
        if (!string.IsNullOrEmpty(rel))
        {
            var full = GameCatalogPaths.ResolvePath(rel);
            if (File.Exists(full))
                return rel;
        }

        return null;
    }
}
