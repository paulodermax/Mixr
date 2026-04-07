using System.Globalization;
using Microsoft.Win32;

namespace Mixr.Services;

/// <summary>
/// Installierte GOG-Galaxy-Spiele aus <c>HKLM\SOFTWARE\GOG.com\Games</c> (32-Bit-Ansicht), vgl. GameFinder GOG.
/// </summary>
public static class GogLibraryScanner
{
    public readonly record struct GogInstalledGame(long ProductId, string DisplayName);

    public static IReadOnlyList<GogInstalledGame> ScanInstalledGames()
    {
        var list = new List<GogInstalledGame>();
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var gogKey = baseKey.OpenSubKey(@"SOFTWARE\GOG.com\Games");
            if (gogKey == null)
                return Array.Empty<GogInstalledGame>();

            foreach (var subName in gogKey.GetSubKeyNames())
            {
                using var sub = gogKey.OpenSubKey(subName);
                if (sub == null)
                    continue;

                var name = sub.GetValue("gameName") as string;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (!TryResolveProductId(subName, sub, out var productId))
                    continue;

                list.Add(new GogInstalledGame(productId, name.Trim()));
            }
        }
        catch
        {
            /* */
        }

        return list
            .GroupBy(x => x.ProductId)
            .Select(g => g.First())
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static bool TryResolveProductId(string subKeyName, RegistryKey sub, out long productId)
    {
        productId = -1;
        if (long.TryParse(subKeyName, NumberStyles.Integer, CultureInfo.InvariantCulture, out productId) &&
            productId > 0)
            return true;

        var gs = sub.GetValue("gameID") as string;
        if (long.TryParse(gs, NumberStyles.Integer, CultureInfo.InvariantCulture, out productId) &&
            productId > 0)
            return true;

        return false;
    }
}
