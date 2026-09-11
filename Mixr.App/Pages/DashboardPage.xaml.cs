using System.IO;
using System.Text.RegularExpressions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Mixr.Services;
using Mixr_App.Services;

namespace Mixr_App.Pages;

public sealed partial class DashboardPage : Page
{
    const int SlideCount = 4;
    const double FaderMinFill = 8;

    static readonly Regex LogKeep = new(
        @"Verbind|USB|HID|COM\d|Firmware|FW:|\[ESP|Discord|Fehler|Katalog|Cover|HELLO|Kein Mixr|Update|→ ESP",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    readonly DispatcherQueue _dq = DispatcherQueue.GetForCurrentThread();
    Microsoft.UI.Xaml.DispatcherTimer? _refreshTimer;
    int _tileRefreshRunning;
    int _slideIndex;
    string? _lastCoverHash;
    string? _lastLogStamp;

    readonly FaderRowUi[] _rows;

    static readonly SolidColorBrush OkBrush = new(Microsoft.UI.ColorHelper.FromArgb(255, 68, 214, 44));
    static readonly SolidColorBrush WarnBrush = new(Microsoft.UI.ColorHelper.FromArgb(255, 214, 168, 44));
    static readonly SolidColorBrush OffBrush = new(Microsoft.UI.ColorHelper.FromArgb(255, 120, 120, 120));
    static readonly SolidColorBrush PagerOn = new(Microsoft.UI.ColorHelper.FromArgb(255, 68, 214, 44));
    static readonly SolidColorBrush PagerOff = new(Microsoft.UI.ColorHelper.FromArgb(255, 90, 90, 90));

    sealed class FaderRowUi
    {
        public required Border Row { get; init; }
        public required Image Icon { get; init; }
        public required TextBlock Label { get; init; }
        public required Grid Track { get; init; }
        public required Border Fill { get; init; }
    }

    public DashboardPage()
    {
        InitializeComponent();
        _rows =
        [
            new FaderRowUi { Row = FaderRow0, Icon = FaderIcon0, Label = FaderLabel0, Track = FaderTrack0, Fill = FaderFill0 },
            new FaderRowUi { Row = FaderRow1, Icon = FaderIcon1, Label = FaderLabel1, Track = FaderTrack1, Fill = FaderFill1 },
            new FaderRowUi { Row = FaderRow2, Icon = FaderIcon2, Label = FaderLabel2, Track = FaderTrack2, Fill = FaderFill2 },
            new FaderRowUi { Row = FaderRow3, Icon = FaderIcon3, Label = FaderLabel3, Track = FaderTrack3, Fill = FaderFill3 },
        ];
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _rows[0].Track.Loaded += (_, _) => RefreshFaders();
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        MixrRuntimeState.Config.Changed += OnRuntimeChanged;
        MixrRuntimeState.EspConnectionChanged += OnRuntimeChanged;
        MixrRuntimeState.SliderLevelsChanged += OnRuntimeChanged;
        MixrRuntimeState.DeviceChanged += OnRuntimeChanged;
        MixrRuntimeState.NowPlayingChanged += OnNowPlayingChanged;
        MixrRuntimeState.VoipUiChanged += OnVoipChanged;
        GameCatalogCoordinator.CatalogChanged += OnCatalogChanged;
        ApplySlide();
        RefreshAll();
        RefreshNowPlaying();
        RefreshVoipChips();
        StartTimer();
    }

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        MixrRuntimeState.Config.Changed -= OnRuntimeChanged;
        MixrRuntimeState.EspConnectionChanged -= OnRuntimeChanged;
        MixrRuntimeState.SliderLevelsChanged -= OnRuntimeChanged;
        MixrRuntimeState.DeviceChanged -= OnRuntimeChanged;
        MixrRuntimeState.NowPlayingChanged -= OnNowPlayingChanged;
        MixrRuntimeState.VoipUiChanged -= OnVoipChanged;
        GameCatalogCoordinator.CatalogChanged -= OnCatalogChanged;
        StopTimer();
    }

    void OnRuntimeChanged() => _dq.TryEnqueue(RefreshAll);

    void OnNowPlayingChanged() => _dq.TryEnqueue(RefreshNowPlaying);

    void OnVoipChanged() => _dq.TryEnqueue(RefreshVoipChips);

    void OnCatalogChanged(object? sender, EventArgs e) => _dq.TryEnqueue(RefreshStatus);

    void StartTimer()
    {
        StopTimer();
        _refreshTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        _refreshTimer.Tick += (_, _) => RefreshAll();
        _refreshTimer.Start();
    }

    void StopTimer()
    {
        _refreshTimer?.Stop();
        _refreshTimer = null;
    }

    void SlidePrev_Click(object sender, RoutedEventArgs e)
    {
        _slideIndex = (_slideIndex + SlideCount - 1) % SlideCount;
        ApplySlide();
    }

    void SlideNext_Click(object sender, RoutedEventArgs e)
    {
        _slideIndex = (_slideIndex + 1) % SlideCount;
        ApplySlide();
    }

    void ApplySlide()
    {
        SlideNowPlaying.Visibility = _slideIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        SlideFocus.Visibility = _slideIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        SlideMixer.Visibility = _slideIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        SlideSettings.Visibility = _slideIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        Pager0.Fill = _slideIndex == 0 ? PagerOn : PagerOff;
        Pager1.Fill = _slideIndex == 1 ? PagerOn : PagerOff;
        Pager2.Fill = _slideIndex == 2 ? PagerOn : PagerOff;
        Pager3.Fill = _slideIndex == 3 ? PagerOn : PagerOff;
        Pager0.Opacity = _slideIndex == 0 ? 1 : 0.4;
        Pager1.Opacity = _slideIndex == 1 ? 1 : 0.4;
        Pager2.Opacity = _slideIndex == 2 ? 1 : 0.4;
        Pager3.Opacity = _slideIndex == 3 ? 1 : 0.4;
        RefreshFaders();
    }

    async void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = AppLog.LogFilePath;
            if (!File.Exists(path))
                return;
            await Windows.System.Launcher.LaunchFileAsync(
                await Windows.Storage.StorageFile.GetFileFromPathAsync(path));
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("Dashboard/OpenLog: " + ex.Message);
        }
    }

    void RefreshAll()
    {
        RefreshStatus();
        RefreshFaders();
        RefreshLogs();
        _ = RefreshTilesAsync();
    }

    void RefreshStatus()
    {
        AppStatusDot.Background = OkBrush;
        AppStatusText.Text = "App: Online";

        var connected = MixrRuntimeState.EspConnected;
        var dev = MixrRuntimeState.Device;
        var link = MixrRuntimeState.Link;
        BoardStatusDot.Background = connected ? OkBrush : OffBrush;
        if (!connected)
        {
            BoardStatusText.Text = "Board: Getrennt";
        }
        else
        {
            var via = link?.Kind == MixrLinkKind.Hid ? "USB-HID" : link?.Link.Id ?? "USB";
            BoardStatusText.Text = $"Board: Verbunden ({via})";
        }

        var store = GameCatalogStore.LoadOrCreate();
        var scanned = store.LastDailyScanUtc != default || store.LastWeeklyCatalogUtc != default;
        if (!scanned || store.Games.Count == 0)
        {
            LibraryStatusDot.Background = WarnBrush;
            LibraryStatusText.Text = scanned ? "Library: Leer" : "Library: Nicht synchronisiert";
        }
        else
        {
            LibraryStatusDot.Background = OkBrush;
            LibraryStatusText.Text = $"Library: Synchronisiert ({store.Games.Count})";
        }

        var appVer = AppVersion.Display;
        var boardVer = connected && dev is not null ? dev.FirmwareVersion : "—";
        VersionLine.Text = $"Version: {appVer}  ·  Board: {boardVer}";
    }

    void RefreshNowPlaying()
    {
        var np = MixrRuntimeState.NowPlaying;
        var title = string.IsNullOrWhiteSpace(np.Title) ? "—" : np.Title;
        var artist = string.IsNullOrWhiteSpace(np.Artist) ? "Keine Wiedergabe" : np.Artist;
        MirrorTitle.Text = title;
        MirrorArtist.Text = artist;

        var hash = np.CoverRgb565 is null ? "" : Convert.ToHexString(np.CoverRgb565.AsSpan(0, Math.Min(16, np.CoverRgb565.Length)));
        if (hash == _lastCoverHash)
            return;
        _lastCoverHash = hash;
        MirrorCover.Source = Rgb565ImageFactory.FromRgb565(np.CoverRgb565);
    }

    void RefreshVoipChips()
    {
        MirrorMuteChip.Opacity = MixrRuntimeState.VoipMuted ? 1 : 0.28;
        MirrorDeafenChip.Opacity = MixrRuntimeState.VoipDeafened ? 1 : 0.28;
        MirrorMuteChip.Background = MixrRuntimeState.VoipMuted
            ? OkBrush
            : (Brush)Application.Current.Resources["MixrSubtleFillBrush"];
        MirrorDeafenChip.Background = MixrRuntimeState.VoipDeafened
            ? OkBrush
            : (Brush)Application.Current.Resources["MixrSubtleFillBrush"];
    }

    void RefreshFaders()
    {
        var levels = MixrRuntimeState.GetSliderLevelsSnapshot();
        var cfg = MixrRuntimeState.Config.Current;
        var fromAudio = MixrRuntimeState.Audio?.GetVolumeLevels(cfg.SliderMapping);

        for (var i = 0; i < _rows.Length; i++)
        {
            var hardware = i < levels.Length && levels[i] >= 0 ? levels[i] : -1f;
            var audio = fromAudio is not null && i < fromAudio.Length ? fromAudio[i] : -1f;
            var level = hardware >= 0 ? hardware : audio >= 0 ? audio : 0f;
            level = Math.Clamp(level, 0f, 1f);

            var trackW = _rows[i].Track.ActualWidth;
            if (trackW <= 1)
                trackW = 180;
            _rows[i].Fill.Width = Math.Max(FaderMinFill, trackW * level);
        }

        if (_slideIndex == 2)
            RebuildMixerSlide(cfg, levels, fromAudio);
    }

    void RebuildMixerSlide(Mixr.Models.MixrConfig cfg, float[] levels, float[]? fromAudio)
    {
        MirrorMixerBars.Children.Clear();
        for (var i = 0; i < _rows.Length; i++)
        {
            var hardware = i < levels.Length && levels[i] >= 0 ? levels[i] : -1f;
            var audio = fromAudio is not null && i < fromAudio.Length ? fromAudio[i] : -1f;
            var level = hardware >= 0 ? hardware : audio >= 0 ? audio : 0f;
            level = Math.Clamp(level, 0f, 1f);
            var key = i < cfg.SliderMapping.Count ? cfg.SliderMapping[i] : $"fader{i + 1}";

            var mini = new Grid { ColumnSpacing = 8 };
            mini.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            mini.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mini.Children.Add(new TextBlock
            {
                Text = HumanizeKey(key),
                FontSize = 10,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            var overlay = new Grid();
            overlay.Children.Add(new Border
            {
                Height = 5,
                CornerRadius = new CornerRadius(2),
                Background = (Brush)Application.Current.Resources["MixrSubtleFillBrush"],
            });
            overlay.Children.Add(new Border
            {
                Height = 5,
                Width = Math.Max(4, 90 * level),
                HorizontalAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(2),
                Background = OkBrush,
            });
            Grid.SetColumn(overlay, 1);
            mini.Children.Add(overlay);
            MirrorMixerBars.Children.Add(mini);
        }
    }

    async Task RefreshTilesAsync()
    {
        if (Interlocked.Exchange(ref _tileRefreshRunning, 1) == 1)
            return;

        try
        {
            var cfg = MixrRuntimeState.Config.Current;
            var audio = MixrRuntimeState.Audio;
            if (audio != null)
                await Task.Run(() => audio.RebuildSessionMap(cfg.SliderMapping, cfg.SessionGroups, silent: true));

            var live = audio?.GetLiveSnapshot();
            for (var i = 0; i < _rows.Length; i++)
            {
                var info = SliderSummaryIconResolver.Resolve(i, cfg, live);
                var idx = i;
                var label = string.IsNullOrWhiteSpace(info.Label) || info.Label == "—"
                    ? HumanizeKey(i < cfg.SliderMapping.Count ? cfg.SliderMapping[i] : "")
                    : info.Label;
                var tooltip = info.Tooltip;

                var useThemed = info.IsThemedAsset;
                var path = info.ImagePath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    useThemed = true;
                    path = SliderSummaryIconResolver.NoInputPath;
                }

                ImageSource? src = useThemed
                    ? await DashboardThemedIconLoader.LoadAsync(path!)
                    : await CoverImageLoader.LoadCoverImageSourceAsync(path!);

                _dq.TryEnqueue(() =>
                {
                    if (idx >= _rows.Length)
                        return;
                    var r = _rows[idx];
                    r.Label.Text = label;
                    ToolTipService.SetToolTip(r.Row, tooltip);
                    if (src != null)
                        r.Icon.Source = src;
                });
            }
        }
        finally
        {
            Interlocked.Exchange(ref _tileRefreshRunning, 0);
        }
    }

    void RefreshLogs()
    {
        try
        {
            var path = AppLog.LogFilePath;
            if (!File.Exists(path))
            {
                FilteredLogText.Text = "Noch keine Logs.";
                return;
            }

            var stamp = File.GetLastWriteTimeUtc(path).Ticks + ":" + new FileInfo(path).Length;
            if (stamp == _lastLogStamp)
                return;
            _lastLogStamp = stamp;

            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
                text = sr.ReadToEnd();

            var kept = text
                .Split('\n')
                .Select(l => l.TrimEnd())
                .Where(l => l.Length > 0 && LogKeep.IsMatch(l))
                .TakeLast(28)
                .ToArray();

            FilteredLogText.Text = kept.Length == 0 ? "Keine passenden Einträge." : string.Join(Environment.NewLine, kept);
            _ = LogScroller.ChangeView(null, LogScroller.ScrollableHeight, null);
        }
        catch (Exception ex)
        {
            FilteredLogText.Text = ex.Message;
        }
    }

    static string HumanizeKey(string key) =>
        key.ToLowerInvariant() switch
        {
            "master" => "System",
            "communication" => "Kommunikation",
            "media" => "Medien",
            "games" => "Spiele",
            _ => key.Length switch
            {
                0 => "—",
                1 => char.ToUpperInvariant(key[0]).ToString(),
                _ => char.ToUpperInvariant(key[0]) + key[1..],
            },
        };
}
