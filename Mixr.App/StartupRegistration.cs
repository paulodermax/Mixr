using System;
using Microsoft.Win32;

namespace Mixr_App;

/// <summary>
/// Autostart über HKCU\…\Run. Wird nur auf Wunsch des Nutzers (Settings-Toggle) geschrieben — nie automatisch.
/// Velopack installiert nach %LOCALAPPDATA%\Mixr\current\Mixr.exe; dieser Pfad bleibt über Updates stabil,
/// daher genügt der aktuelle Prozesspfad.
/// </summary>
static class StartupRegistration
{
    const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Mixr";

    /// <returns><c>true</c>, wenn der gewünschte Zustand geschrieben werden konnte.</returns>
    public static bool SetRunAtLogin(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunSubKey, writable: true);
            if (key == null)
                return false;

            if (enable)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe))
                    return false;
                key.SetValue(ValueName, $"\"{exe}\" --minimized");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("Autostart: " + ex.Message);
            return false;
        }
    }

    public static bool IsRunAtLoginEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunSubKey);
            return key?.GetValue(ValueName) is string s && !string.IsNullOrEmpty(s);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Nach einem Update zeigt der Run-Eintrag evtl. auf einen alten Pfad — still auf den aktuellen umschreiben.</summary>
    public static void RefreshPathIfEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: true);
            if (key?.GetValue(ValueName) is not string current || string.IsNullOrEmpty(current))
                return;
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                return;
            var wanted = $"\"{exe}\" --minimized";
            if (!string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
                key.SetValue(ValueName, wanted);
        }
        catch
        {
            /* optional */
        }
    }
}
