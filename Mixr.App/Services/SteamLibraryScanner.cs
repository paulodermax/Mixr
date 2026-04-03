using System.Text.RegularExpressions;

namespace Mixr_App.Services;

public sealed record SteamInstalledGame(int AppId, string Name, string? LibraryRoot);

public static class SteamLibraryScanner
{
    static readonly Regex AppIdRx = new("\"appid\"\\s+\"(\\d+)\"", RegexOptions.Compiled);
    static readonly Regex NameRx = new("\"name\"\\s+\"([^\"]+)\"", RegexOptions.Compiled);

    public static IReadOnlyList<SteamInstalledGame> ScanInstalledGames()
    {
        var steamRoot = TryGetSteamRoot();
        if (string.IsNullOrEmpty(steamRoot))
            return Array.Empty<SteamInstalledGame>();

        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(steamRoot, "steamapps"),
        };

        foreach (var extra in EnumerateLibraryRoots(steamRoot))
            roots.Add(Path.Combine(extra, "steamapps"));

        var acc = new List<SteamInstalledGame>();
        foreach (var steamApps in roots)
        {
            if (!Directory.Exists(steamApps))
                continue;

            foreach (var acf in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
            {
                TryParseManifest(acf, steamApps, acc);
            }
        }

        return acc
            .GroupBy(g => g.AppId)
            .Select(g => g.First())
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static string? TryGetSteamRoot()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var path = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                return path.Replace('/', '\\');
        }
        catch
        {
            /* */
        }

        return null;
    }

    static IEnumerable<string> EnumerateLibraryRoots(string steamRoot)
    {
        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
            yield break;

        string text;
        try
        {
            text = File.ReadAllText(vdf);
        }
        catch
        {
            yield break;
        }

        foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
        {
            var raw = m.Groups[1].Value.Replace("\\\\", "\\");
            if (Directory.Exists(raw))
                yield return raw;
        }
    }

    static void TryParseManifest(string acfPath, string steamAppsDir, List<SteamInstalledGame> acc)
    {
        try
        {
            var text = File.ReadAllText(acfPath);
            var appIdM = AppIdRx.Match(text);
            var nameM = NameRx.Match(text);
            if (!appIdM.Success || !nameM.Success)
                return;

            if (!int.TryParse(appIdM.Groups[1].Value, out var appId) || appId <= 0)
                return;

            var name = nameM.Groups[1].Value.Trim();
            if (name.Length == 0)
                return;

            var libRoot = Path.GetDirectoryName(steamAppsDir);
            acc.Add(new SteamInstalledGame(appId, name, libRoot));
        }
        catch
        {
            /* */
        }
    }
}
