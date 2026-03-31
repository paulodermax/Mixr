using System;
using System.IO;
using Microsoft.UI.Xaml.Controls;

namespace Mixr_App.Pages;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ConfigPathText.Text = Path.Combine(AppContext.BaseDirectory, "config.yaml");
        };
    }
}
