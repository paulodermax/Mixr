using Mixr.Models;
using Mixr.Services;

namespace Mixr_App.Services;

/// <summary>
/// Beim Start: Katalog mit manuellen Pfaden synchronisieren und jede Cover-Datei einmal dekodieren
/// (ohne gemeinsame <c>ImageSource</c> — WinUI erlaubt oft nur ein <c>Image</c> pro Instanz).
/// </summary>
public static class CoverWarmup
{
    /// <summary>Dekodiert alle erkannten Cover einmal (OS-/Decoder-Warmup), speichert keine geteilten Bildobjekte.</summary>
    public static async Task PreloadAllAsync()
    {
        CatalogManualCoverSync.ApplyManualFilesToStore();

        var store = GameCatalogStore.LoadOrCreate();
        var cfg = MixrConfigLoader.Load(Array.Empty<string>());

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRel(string? rel)
        {
            if (string.IsNullOrEmpty(rel))
                return;
            var full = Path.GetFullPath(GameCatalogPaths.ResolvePath(rel));
            if (File.Exists(full))
                paths.Add(full);
        }

        foreach (var e in store.Games)
            AddRel(CatalogCoverResolver.ResolveRelativePath(e));

        foreach (var token in cfg.SessionGroups.Values.SelectMany(v => v)
                     .Where(t => !string.IsNullOrWhiteSpace(t))
                     .Select(t => t.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var entry = CatalogGameEntryLookup.FindEntry(store, token);
            var rel = entry != null
                ? CatalogCoverResolver.ResolveRelativePath(entry)
                : ManualCoverResolver.TryFindRelativePath(
                    new CatalogGameEntry { Name = token, AssignmentToken = token });
            AddRel(rel);
        }

        foreach (var full in paths)
        {
            try
            {
                _ = await CoverImageLoader.LoadCoverImageSourceAsync(full).ConfigureAwait(true);
            }
            catch
            {
                /* einzelne Datei überspringen */
            }
        }
    }
}
