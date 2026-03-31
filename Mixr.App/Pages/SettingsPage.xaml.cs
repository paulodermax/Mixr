using System;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mixr_App.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        StartupToggle.IsOn = StartupRegistration.IsRunAtLoginEnabled();
        CfgHint.Text = Path.Combine(AppContext.BaseDirectory, "config.yaml");
    }

    void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch t)
            StartupRegistration.SetRunAtLogin(t.IsOn);
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
