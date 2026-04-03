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
        ConfigPathText.Text = MixrConfigPaths.ConfigYamlPath;
    }

    void OpenSliderMapping_Click(object sender, RoutedEventArgs e)
    {
        Frame?.Navigate(typeof(SliderMappingPage));
    }
}
