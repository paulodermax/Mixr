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
using System.IO;

namespace Mixr_App.Pages;

public sealed partial class HardwareMapPage : Page
{
    const double DetailWidth = 380;
    const double SchematicShiftMagnitude = 88;

    MixrConfig _draft = new();

    readonly Border[] _glowRects = null!;

    Storyboard? _pulseStoryboard;
    Border? _activeGlow;

    bool _detailOpen;
    bool _detailOnLeft;
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
        TryLoadSchematicImage();
        DetailSlideTransform.X = DetailWidth;
        DetailDrawer.HorizontalAlignment = HorizontalAlignment.Right;
        DetailDrawer.BorderThickness = new Thickness(1, 0, 0, 0);
        StopPulse();
    }

    void TryLoadSchematicImage()
    {
        try
        {
            SchematicImage.Source = new SvgImageSource(new Uri("ms-appx:///Assets/Mixr-Modell.svg"));
            return;
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("Hardware map SVG ms-appx failed: " + ex.Message);
        }

        try
        {
            var full = Path.Combine(AppContext.BaseDirectory, "Assets", "Mixr-Modell.svg");
            if (File.Exists(full))
            {
                SchematicImage.Source = new SvgImageSource(new Uri(full));
                return;
            }
            AppLog.WriteLine("Hardware map SVG missing: " + full);
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("Hardware map SVG file fallback failed: " + ex.Message);
        }
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
        var onLeft = IsHotspotOnLeft(kind, idx);
        if (kind == 'f')
            OpenSliderDetail(idx, onLeft);
        else if (kind == 'b')
            OpenButtonDetail(idx, onLeft);
    }

    static bool IsHotspotOnLeft(char kind, int idx) =>
        kind switch
        {
            'f' => idx <= 1, // f0,f1 links | f2,f3 rechts
            'b' => idx <= 2, // b0..b2 links | b3,b4 rechts
            _ => false,
        };

    void OpenSliderDetail(int idx, bool onLeft)
    {
        if (idx < 0 || idx >= _draft.SliderMapping.Count)
            return;
        _detailOnLeft = onLeft;
        _sliderDetailIndex = idx;
        _buttonDetailIndex = -1;
        var key = _draft.SliderMapping[idx];
        DetailSliderGroup.Text = key;
        var lines = _draft.SessionGroups.TryGetValue(key, out var list)
            ? string.Join(Environment.NewLine, list.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            : "";
        DetailSliderPrograms.Text = lines;
        DetailTitleText.Text = $"Slider {idx + 1}";
        SliderDetailPanel.Visibility = Visibility.Visible;
        ButtonDetailPanel.Visibility = Visibility.Collapsed;
        if (!_detailOpen)
            OpenDetailDrawer();
    }

    void OpenButtonDetail(int idx, bool onLeft)
    {
        if (idx < 0 || idx >= 5)
            return;
        MixrButtonActions.EnsureFiveEntries(_draft.ButtonMapping);
        _detailOnLeft = onLeft;
        _buttonDetailIndex = idx;
        _sliderDetailIndex = -1;
        var current = MixrButtonActions.Resolve(idx, _draft.ButtonMapping);
        DetailButtonCombo.Items.Clear();
        foreach (var a in MixrButtonActions.All)
        {
            var item = new ComboBoxItem { Content = ActionTitleEnglish(a), Tag = a };
            DetailButtonCombo.Items.Add(item);
            if (a.Equals(current, StringComparison.OrdinalIgnoreCase))
                DetailButtonCombo.SelectedItem = item;
        }

        // Defensive fallback: should never happen, but keeps Save usable
        // even if future mappings introduce unknown values.
        if (DetailButtonCombo.SelectedItem is null && DetailButtonCombo.Items.Count > 0)
            DetailButtonCombo.SelectedIndex = 0;

        DetailTitleText.Text = $"Button {idx + 1}";
        ButtonDetailPanel.Visibility = Visibility.Visible;
        SliderDetailPanel.Visibility = Visibility.Collapsed;
        if (!_detailOpen)
            OpenDetailDrawer();
    }

    static string ActionTitleEnglish(string id) =>
        id switch
        {
            MixrButtonActions.SmtcPrevious => "Media: Previous (SMTC)",
            MixrButtonActions.SmtcPlayPause => "Media: Play/Pause (SMTC)",
            MixrButtonActions.SmtcNext => "Media: Next (SMTC)",
            MixrButtonActions.DiscordMute => "Discord: Mute",
            MixrButtonActions.DiscordDeafen => "Discord: Deafen",
            MixrButtonActions.None => "No action",
            _ => id,
        };

    void OpenDetailDrawer()
    {
        _detailOpen = true;
        DetailDrawer.HorizontalAlignment = _detailOnLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        DetailDrawer.BorderThickness = _detailOnLeft ? new Thickness(0, 0, 1, 0) : new Thickness(1, 0, 0, 0);
        DetailDrawer.Visibility = Visibility.Visible;
        DetailSlideTransform.X = _detailOnLeft ? -DetailWidth : DetailWidth;
        SchematicSlideTransform.X = 0;
        var schematicShift = _detailOnLeft ? SchematicShiftMagnitude : -SchematicShiftMagnitude;
        RunSlideAnimation(schematicShift, 0);
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
            To = _detailOnLeft ? -DetailWidth : DetailWidth,
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
        DetailSlideTransform.X = _detailOnLeft ? -DetailWidth : DetailWidth;
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
            AppLog.WriteLine("Hardware map: config.yaml saved.");
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("Hardware map save failed: " + ex.Message);
        }
    }
}
