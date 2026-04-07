using Microsoft.Win32;

namespace Mixr.Services;

/// <summary>Uninstall-Registry (HKLM/HKCU, 32/64-Bit) für Publisher-basierte Erkennung.</summary>
public static class UninstallRegistry
{
    public readonly record struct UninstallEntry(string SubKeyName, string DisplayName, string? Publisher);

    public static IEnumerable<UninstallEntry> EnumerateEntries()
    {
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var subPath in UninstallSubPaths)
            {
                using var key = root.OpenSubKey(subPath);
                if (key == null)
                    continue;
                foreach (var name in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(name);
                    var disp = sub?.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(disp))
                        continue;
                    var pub = sub?.GetValue("Publisher") as string;
                    yield return new UninstallEntry(name, disp.Trim(), string.IsNullOrWhiteSpace(pub) ? null : pub.Trim());
                }
            }
        }
    }

    static readonly string[] UninstallSubPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];
}
