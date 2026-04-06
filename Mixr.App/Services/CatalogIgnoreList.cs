namespace Mixr_App.Services;

/// <summary>
/// Liste ignorierter Programme: <see cref="GameCatalogPaths.CatalogIgnoreListPath"/> (eine Regel pro Zeile).
/// Betrifft Steam-Scan, Katalog-Aktualisierung und Einträge aus der Installations-Erkennung — nicht die Fader-Zuordnung.
/// </summary>
public static class CatalogIgnoreList
{
    static readonly object Gate = new();
    static DateTime _loadedWriteTimeUtc;
    static IReadOnlyList<Rule> _rules = Array.Empty<Rule>();

    sealed record Rule(bool Exact, string Text);

    /// <summary>Ignorieren, wenn der Anzeigename (Steam, Uninstall, Katalog) zur Regel passt.</summary>
    public static bool ShouldIgnore(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        ReloadIfNeeded();
        var n = name.Trim();
        foreach (var r in _rules)
        {
            if (r.Exact)
            {
                if (n.Equals(r.Text, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (n.Contains(r.Text, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static void ReloadIfNeeded()
    {
        lock (Gate)
        {
            EnsureDefaultFileExists();
            var path = GameCatalogPaths.CatalogIgnoreListPath;
            if (!File.Exists(path))
            {
                _rules = Array.Empty<Rule>();
                return;
            }

            var wt = File.GetLastWriteTimeUtc(path);
            if (_rules.Count > 0 && wt == _loadedWriteTimeUtc)
                return;

            _rules = ParseLines(File.ReadAllLines(path));
            _loadedWriteTimeUtc = wt;
        }
    }

    /// <summary>Fügt fehlende Standardzeilen für Steam / Battle.net / Epic Launcher hinzu (idempotent).</summary>
    public static void EnsureLauncherIgnoreLines()
    {
        try
        {
            GameCatalogPaths.EnsureLayout();
            var path = GameCatalogPaths.CatalogIgnoreListPath;
            if (!File.Exists(path))
                return;

            var existing = new HashSet<string>(
                File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')),
                StringComparer.OrdinalIgnoreCase);

            string[] required =
            [
                "=Steam",
                "=Battle.net",
                "=Epic Games Launcher",
                "=Microsoft Edge",
                "=Riot Client",
            ];
            var toAdd = required.Where(r => !existing.Contains(r)).ToList();
            if (toAdd.Count == 0)
                return;

            File.AppendAllText(path, "\n" + string.Join("\n", toAdd) + "\n");
            InvalidateCache();
        }
        catch
        {
            /* optional */
        }
    }

    /// <summary>Nach manueller Bearbeitung der Datei Cache leeren.</summary>
    public static void InvalidateCache()
    {
        lock (Gate)
        {
            _loadedWriteTimeUtc = default;
            _rules = Array.Empty<Rule>();
        }
    }

    static IReadOnlyList<Rule> ParseLines(string[] lines)
    {
        var list = new List<Rule>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith("=", StringComparison.Ordinal))
            {
                var exact = line[1..].Trim();
                if (exact.Length > 0)
                    list.Add(new Rule(Exact: true, exact));
                continue;
            }

            list.Add(new Rule(Exact: false, line));
        }

        return list;
    }

    static void EnsureDefaultFileExists()
    {
        GameCatalogPaths.EnsureLayout();
        var path = GameCatalogPaths.CatalogIgnoreListPath;
        if (File.Exists(path))
            return;

        File.WriteAllText(
            path,
            """
            # Mixr — Programme, die nicht in der Programmbibliothek erscheinen sollen.
            # Pro Zeile: Teilstring im Anzeigenamen (Groß/Klein egal).
            # Exakter Titel: Zeile mit = am Anfang, z. B. =Genau dieser Name
            #
            Microsoft Edge WebView2-Laufzeit
            Steamworks Common Redistributables
            =Steam
            =Battle.net
            =Epic Games Launcher
            =Microsoft Edge
            =Riot Client

            """);
    }
}
