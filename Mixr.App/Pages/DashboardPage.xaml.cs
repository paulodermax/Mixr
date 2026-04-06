using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mixr.Services;

namespace Mixr_App.Pages;

public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        var full = MixrConfigPaths.ConfigYamlPath;
        ConfigFileNameText.Text = Path.GetFileName(full);
        ConfigPathText.Text = Path.GetDirectoryName(full) ?? full;
        ToolTipService.SetToolTip(ConfigPathText, full);
    }

    void OpenSliderMapping_Click(object sender, RoutedEventArgs e)
    {
        Frame?.Navigate(typeof(SliderMappingPage));
    }
}
