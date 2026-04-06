using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Mixr.Models;
using Mixr.Services;
using Mixr_App.Services;

namespace Mixr_App.Pages;

public sealed partial class HardwareMapPage : Page
{
    const double DetailWidth = 380;
    const double SchematicShiftWhenOpen = -88;

    MixrConfig _draft = new();

    readonly Border[] _glowRects = null!;

    Storyboard? _pulseStoryboard;
    Border? _activeGlow;

    bool _detailOpen;
    int _sliderDetailIndex = -1;
    int _buttonDetailIndex = -1;

    public HardwareMapPage()
    {
        InitializeComponent();
        _glowRects = new[] { GlowF0, GlowF1, GlowF2, GlowF3, GlowB0, GlowB1, GlowB2, GlowB3, GlowB4 };
        Loaded += HardwareMapPage_Loaded;
    }

    void HardwareMapPage_Loaded(object sender, RoutedEventArgs e)
    {
        SchematicImage.Source = new SvgImageSource(new Uri("ms-appx:///Assets/Mixr-Modell.svg"));
        DetailSlideTransform.X = DetailWidth;
        StopPulse();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadDraft();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (_detailOpen)
            CloseDetailImmediate();
        base.OnNavigatedFrom(e);
    }

    void LoadDraft()
    {
        _draft = MixrConfigClone.DeepClone(MixrConfigLoader.Load(Array.Empty<string>()));
        MixrButtonActions.EnsureFiveEntries(_draft.ButtonMapping);
    }

    void Hotspot_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string tag)
            return;
        var glow = GlowForTag(tag);
        if (glow == null)
            return;
        StopPulse();
        _activeGlow = glow;
        glow.Opacity = 1;
        StartPulse(glow);
    }

    void Hotspot_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        StopPulse();
    }

    Border? GlowForTag(string tag) =>
        tag switch
        {
            "f0" => GlowF0,
            "f1" => GlowF1,
            "f2" => GlowF2,
            "f3" => GlowF3,
            "b0" => GlowB0,
            "b1" => GlowB1,
            "b2" => GlowB2,
            "b3" => GlowB3,
            "b4" => GlowB4,
            _ => null,
        };

    void StartPulse(Border target)
    {
        _pulseStoryboard?.Stop();
        var sb = new Storyboard();
        var anim = new DoubleAnimation
        {
            From = 0.35,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(380)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, "Opacity");
        sb.Children.Add(anim);
        _pulseStoryboard = sb;
        sb.Begin();
    }

    void StopPulse()
    {
        _pulseStoryboard?.Stop();
        _pulseStoryboard = null;
        foreach (var r in _glowRects)
            r.Opacity = 0;
        _activeGlow = null;
    }

    void Hotspot_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string tag || tag.Length < 2)
            return;
        var kind = tag[0];
        if (!int.TryParse(tag.AsSpan(1), out var idx))
            return;
        if (kind == 'f')
            OpenSliderDetail(idx);
        else if (kind == 'b')
            OpenButtonDetail(idx);
    }

    void OpenSliderDetail(int idx)
    {
        if (idx < 0 || idx >= _draft.SliderMapping.Count)
            return;
        _sliderDetailIndex = idx;
        _buttonDetailIndex = -1;
        var key = _draft.SliderMapping[idx];
        DetailSliderGroup.Text = key;
        var lines = _draft.SessionGroups.TryGetValue(key, out var list)
            ? string.Join(Environment.NewLine, list.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            : "";
        DetailSliderPrograms.Text = lines;
        DetailTitleText.Text = $"Fader {idx + 1}";
        SliderDetailPanel.Visibility = Visibility.Visible;
        ButtonDetailPanel.Visibility = Visibility.Collapsed;
        if (!_detailOpen)
            OpenDetailDrawer();
    }

    void OpenButtonDetail(int idx)
    {
        if (idx < 0 || idx >= 5)
            return;
        MixrButtonActions.EnsureFiveEntries(_draft.ButtonMapping);
        _buttonDetailIndex = idx;
        _sliderDetailIndex = -1;
        var current = MixrButtonActions.Resolve(idx, _draft.ButtonMapping);
        DetailButtonCombo.Items.Clear();
        foreach (var a in MixrButtonActions.All)
        {
            var item = new ComboBoxItem { Content = ActionTitleGerman(a), Tag = a };
            DetailButtonCombo.Items.Add(item);
            if (a.Equals(current, StringComparison.OrdinalIgnoreCase))
                DetailButtonCombo.SelectedItem = item;
        }

        DetailTitleText.Text = $"Taster {idx}";
        ButtonDetailPanel.Visibility = Visibility.Visible;
        SliderDetailPanel.Visibility = Visibility.Collapsed;
        if (!_detailOpen)
            OpenDetailDrawer();
    }

    static string ActionTitleGerman(string id) =>
        id switch
        {
            MixrButtonActions.SmtcPrevious => "Medien: Zurück (SMTC)",
            MixrButtonActions.SmtcPlayPause => "Medien: Play/Pause (SMTC)",
            MixrButtonActions.SmtcNext => "Medien: Weiter (SMTC)",
            MixrButtonActions.DiscordMute => "Discord: Stummschalten",
            MixrButtonActions.DiscordDeafen => "Discord: Taub",
            MixrButtonActions.None => "Keine Aktion",
            _ => id,
        };

    void OpenDetailDrawer()
    {
        _detailOpen = true;
        DetailDrawer.Visibility = Visibility.Visible;
        DetailSlideTransform.X = DetailWidth;
        SchematicSlideTransform.X = 0;
        RunSlideAnimation(SchematicShiftWhenOpen, 0);
    }

    void RunSlideAnimation(double schematicX, double detailX)
    {
        var sb = new Storyboard();
        var a1 = new DoubleAnimation
        {
            To = schematicX,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(a1, SchematicSlideTransform);
        Storyboard.SetTargetProperty(a1, "X");
        var a2 = new DoubleAnimation
        {
            To = detailX,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(a2, DetailSlideTransform);
        Storyboard.SetTargetProperty(a2, "X");
        sb.Children.Add(a1);
        sb.Children.Add(a2);
        sb.Begin();
    }

    void DetailClose_Click(object sender, RoutedEventArgs e)
    {
        CloseDetailAnimated();
    }

    void CloseDetailAnimated()
    {
        if (!_detailOpen)
            return;
        var sb = new Storyboard();
        var a1 = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(260)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        Storyboard.SetTarget(a1, SchematicSlideTransform);
        Storyboard.SetTargetProperty(a1, "X");
        var a2 = new DoubleAnimation
        {
            To = DetailWidth,
            Duration = new Duration(TimeSpan.FromMilliseconds(260)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        Storyboard.SetTarget(a2, DetailSlideTransform);
        Storyboard.SetTargetProperty(a2, "X");
        sb.Children.Add(a1);
        sb.Children.Add(a2);
        sb.Begin();
        var dq = DispatcherQueue.GetForCurrentThread();
        _ = Task.Run(async () =>
        {
            await Task.Delay(280).ConfigureAwait(false);
            dq.TryEnqueue(() =>
            {
                DetailDrawer.Visibility = Visibility.Collapsed;
                _detailOpen = false;
                _sliderDetailIndex = -1;
                _buttonDetailIndex = -1;
            });
        });
    }

    void CloseDetailImmediate()
    {
        _pulseStoryboard?.Stop();
        _pulseStoryboard = null;
        SchematicSlideTransform.X = 0;
        DetailSlideTransform.X = DetailWidth;
        DetailDrawer.Visibility = Visibility.Collapsed;
        _detailOpen = false;
        _sliderDetailIndex = -1;
        _buttonDetailIndex = -1;
    }

    void DetailSaveSlider_Click(object sender, RoutedEventArgs e)
    {
        if (_sliderDetailIndex < 0 || _sliderDetailIndex >= _draft.SliderMapping.Count)
            return;
        var idx = _sliderDetailIndex;
        var oldKey = _draft.SliderMapping[idx];
        var newKey = (DetailSliderGroup.Text ?? "").Trim();
        if (string.IsNullOrEmpty(newKey))
            return;
        var programs = (DetailSliderPrograms.Text ?? "")
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!oldKey.Equals(newKey, StringComparison.OrdinalIgnoreCase))
        {
            _draft.SessionGroups.Remove(oldKey);
            _draft.SliderMapping[idx] = newKey;
        }

        if (programs.Count == 0)
            _draft.SessionGroups.Remove(newKey);
        else
            _draft.SessionGroups[newKey] = programs;

        SaveAndReload();
        CloseDetailAnimated();
    }

    void DetailSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_buttonDetailIndex < 0 || _buttonDetailIndex >= 5)
            return;
        MixrButtonActions.EnsureFiveEntries(_draft.ButtonMapping);
        if (DetailButtonCombo.SelectedItem is ComboBoxItem sel && sel.Tag is string tag)
            _draft.ButtonMapping[_buttonDetailIndex] = tag;
        else
            return;
        MixrButtonActions.EnsureFiveEntries(_draft.ButtonMapping);
        SaveAndReload();
        CloseDetailAnimated();
    }

    void SaveAndReload()
    {
        try
        {
            MixrButtonActions.EnsureFiveEntries(_draft.ButtonMapping);
            MixrConfigWriter.Save(_draft, MixrConfigPaths.ConfigYamlPath);
            MixrRuntimeState.ReloadConfigFromDisk(Array.Empty<string>());
            AppLog.WriteLine("Hardware-Karte: config.yaml gespeichert.");
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("Hardware-Karte Speichern: " + ex.Message);
        }
    }
}
