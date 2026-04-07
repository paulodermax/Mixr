using System.Globalization;

namespace Mixr.Services;

/// <summary>
/// Origin/EA-ältere Installationen über <c>%ProgramData%\Origin\LocalContent\*.mfst</c> (Query-String, vgl. GameFinder Origin).
/// </summary>
public static class OriginLegacyLibraryScanner
{
    public readonly record struct OriginInstalledGame(string StableKey, string DisplayName);

    public static IReadOnlyList<OriginInstalledGame> ScanInstalledGames()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var dir = Path.Combine(programData, "Origin", "LocalContent");
        if (!Directory.Exists(dir))
            return Array.Empty<OriginInstalledGame>();

        var list = new List<OriginInstalledGame>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.mfst"))
        {
            var row = TryParseMfst(path);
            if (row is null)
                continue;
            list.Add(row.Value);
        }

        return list
            .GroupBy(x => x.StableKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static OriginInstalledGame? TryParseMfst(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch
        {
            return null;
        }

        var q = ParseQueryString(text);
        if (!q.TryGetValue("id", out var ids) || ids.Count == 0)
            return null;

        var id = ids[0];
        if (id.EndsWith("@steam", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!q.TryGetValue("dipInstallPath", out var paths) || paths.Count == 0)
            return null;

        var at = id.IndexOf('@');
        var stable = at > 0 ? id[..at] : id;
        if (string.IsNullOrWhiteSpace(stable))
            return null;

        var display = HumanizeOriginId(stable);
        return new OriginInstalledGame(stable, display);
    }

    static string HumanizeOriginId(string stable)
    {
        var s = stable.Replace('_', ' ').Replace('-', ' ');
        var text = CultureInfo.InvariantCulture.TextInfo;
        return text.ToTitleCase(s.ToLowerInvariant());
    }

    static Dictionary<string, List<string>> ParseQueryString(string text)
    {
        var d = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in text.Split('&'))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            var k = Uri.UnescapeDataString(part[..eq].Trim());
            var v = Uri.UnescapeDataString(part[(eq + 1)..].Trim());
            if (string.IsNullOrEmpty(k))
                continue;
            if (!d.TryGetValue(k, out var list))
            {
                list = new List<string>();
                d[k] = list;
            }

            list.Add(v);
        }

        return d;
    }
}
