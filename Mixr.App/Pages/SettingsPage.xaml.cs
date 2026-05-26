using System;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mixr.Services;

namespace Mixr_App.Pages;

public sealed partial class SettingsPage : Page
{
    bool _suppressLimitSoundsSave;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        StartupToggle.IsOn = StartupRegistration.IsRunAtLoginEnabled();

        _suppressLimitSoundsSave = true;
        LimitSystemSoundsCheck.IsChecked = MixrRuntimeState.Config.Current.LimitSystemSoundsTo20Percent;
        _suppressLimitSoundsSave = false;

        var dir = AppContext.BaseDirectory;
        CfgHint.Text =
            "config.yaml — " + Path.Combine(dir, "config.yaml") + Environment.NewLine +
            "Optional IGDB/Twitch: environment IGDB_CLIENT_ID / IGDB_CLIENT_SECRET (override YAML), or igdb: in YAML, or config.secrets.yaml (template: config.secrets.example.yaml).";
    }

    void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch t)
            StartupRegistration.SetRunAtLogin(t.IsOn);
    }

    void LimitSystemSoundsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressLimitSoundsSave)
            return;

        try
        {
            var cfg = MixrConfigClone.DeepClone(MixrRuntimeState.Config.Current);
            cfg.LimitSystemSoundsTo20Percent = LimitSystemSoundsCheck.IsChecked == true;
            MixrConfigWriter.Save(cfg, MixrConfigPaths.ConfigYamlPath);
            MixrRuntimeState.ReloadConfigFromDisk(Array.Empty<string>());
            AppLog.WriteLine(
                "Settings: limit_system_sounds_to_20_percent = " + cfg.LimitSystemSoundsTo20Percent);
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("Settings save failed: " + ex.Message);
        }
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = AppContext.BaseDirectory,
                UseShellExecute = true,
            });
        }
        catch
        {
            /* */
        }
    }
}
