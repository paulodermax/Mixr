namespace Mixr_App.Services;

/// <summary>
/// Schreibt <see cref="CatalogGameEntry.CoverRelativePath"/> in <c>game_catalog.json</c>.
/// Liegt eine manuelle Datei unter <c>covers/</c>, hat diese Vorrang und überschreibt ältere Einträge.
/// </summary>
public static class CatalogManualCoverSync
{
    public static void ApplyManualFilesToStore()
    {
        try
        {
            GameCatalogPaths.EnsureLayout();
            var store = GameCatalogStore.LoadOrCreate();
            var changed = false;

            foreach (var e in store.Games)
            {
                if (string.IsNullOrWhiteSpace(e.Name))
                    continue;

                var manualRel = ManualCoverResolver.TryFindRelativePath(e);
                if (!string.IsNullOrEmpty(manualRel))
                {
                    var full = GameCatalogPaths.ResolvePath(manualRel);
                    if (File.Exists(full))
                    {
                        var norm = manualRel.Replace('\\', '/');
                        if (!string.Equals(e.CoverRelativePath, norm, StringComparison.OrdinalIgnoreCase))
                        {
                            e.CoverRelativePath = norm;
                            changed = true;
                        }

                        continue;
                    }
                }

                if (!string.IsNullOrEmpty(e.CoverRelativePath))
                {
                    var cur = GameCatalogPaths.ResolvePath(e.CoverRelativePath);
                    if (File.Exists(cur))
                        continue;
                }
            }

            if (!changed)
                return;

            store.Save();
            GameCatalogCoordinator.NotifyCatalogChanged();
        }
        catch
        {
            /* optional */
        }
    }
}
