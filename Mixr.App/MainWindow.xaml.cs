using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Mixr_App.Pages;
using WinRT.Interop;

namespace Mixr_App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        TrySetDefaultSize();

        AppWindow.Closing += AppWindow_Closing;

        NavFrame.Navigate(typeof(DashboardPage));
    }

    void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, AppWindowClosingEventArgs args)
    {
        SliderMappingPage.TryPersistOnWindowClose();
    }

    void TrySetDefaultSize()
    {
        try
        {
            var hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            // Fixed window size (hardware schematic uses the full content area)
            appWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1280, Height = 800 });
            if (appWindow.Presenter is OverlappedPresenter op)
            {
                op.IsResizable = false;
                op.IsMaximizable = false;
            }
        }
        catch
        {
            /* */
        }
    }

    void PaneToggleButton_Click(object sender, RoutedEventArgs e)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (NavFrame.CanGoBack)
            NavFrame.GoBack();
    }

    void NavFrame_Navigated(object sender, NavigationEventArgs e)
    {
        BackButton.Visibility = NavFrame.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
    }

    void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            if (tag == "quit")
            {
                App.ExitCompletely();
                return;
            }

            if (tag == "settings")
            {
                NavFrame.Navigate(typeof(SettingsPage));
                return;
            }

            switch (tag)
            {
                case "dashboard":
                    NavFrame.Navigate(typeof(DashboardPage));
                    break;
                case "slider_mapping":
                    NavFrame.Navigate(typeof(SliderMappingPage));
                    break;
                case "hardware_map":
                    NavFrame.Navigate(typeof(HardwareMapPage));
                    break;
                case "about":
                    NavFrame.Navigate(typeof(AboutPage));
                    break;
            }
        }
    }
}
