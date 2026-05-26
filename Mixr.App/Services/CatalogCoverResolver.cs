namespace Mixr_App.Services;

/// <summary>
/// Einheitliche Auflösung: manuelle Dateien unter <c>covers/</c>, Steam-ID, Katalogpfad, Stamm-Suche.
/// </summary>
public static class CatalogCoverResolver
{
    public static string? ResolveRelativePath(CatalogGameEntry entry) =>
        ResolveRelativePath(entry, GameCatalogStore.LoadOrCreate());

    public static string? ResolveRelativePath(CatalogGameEntry entry, GameCatalogStore store)
    {
        var manual = ManualCoverResolver.TryFindRelativePath(entry);
        if (!string.IsNullOrEmpty(manual) && CoverFileExists(manual))
            return manual;

        if (entry.SteamAppId > 0)
        {
            foreach (var ext in ManualCoverResolver.Extensions)
            {
                var rel = $"covers/steam_{entry.SteamAppId}{ext}";
                if (CoverFileExists(rel))
                    return rel;
            }
        }

        if (!string.IsNullOrEmpty(entry.CoverRelativePath) && CoverFileExists(entry.CoverRelativePath))
            return entry.CoverRelativePath;

        var byName = ManualCoverResolver.TryFindRelativePathByLabel(entry.Name);
        if (!string.IsNullOrEmpty(byName))
            return byName;

        if (!string.IsNullOrEmpty(entry.AssignmentToken))
        {
            byName = ManualCoverResolver.TryFindRelativePathByLabel(entry.AssignmentToken);
            if (!string.IsNullOrEmpty(byName))
                return byName;
        }

        return null;
    }

    public static string? ResolveFullPathForLabel(string label)
    {
        var store = GameCatalogStore.LoadOrCreate();
        var entry = CatalogGameEntryLookup.FindBest(store, label);
        if (entry != null)
        {
            var rel = ResolveRelativePath(entry, store);
            if (!string.IsNullOrEmpty(rel))
                return GameCatalogPaths.ResolvePath(rel);
        }

        var relManual = ManualCoverResolver.TryFindRelativePathByLabel(label);
        return string.IsNullOrEmpty(relManual) ? null : GameCatalogPaths.ResolvePath(relManual);
    }

    static bool CoverFileExists(string relativePath)
    {
        var full = GameCatalogPaths.ResolvePath(relativePath);
        return !string.IsNullOrEmpty(full) && File.Exists(full);
    }
}
