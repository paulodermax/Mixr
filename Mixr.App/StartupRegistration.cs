using System;
using Microsoft.Win32;

namespace Mixr_App;

static class StartupRegistration
{
    const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Mixr";

    public static void SetRunAtLogin(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: true);
        if (key == null)
            return;

        if (enable)
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
                key.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            try
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            catch
            {
                /* ignorieren */
            }
        }
    }

    public static bool IsRunAtLoginEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunSubKey);
        return key?.GetValue(ValueName) is string s && !string.IsNullOrEmpty(s);
    }
}
