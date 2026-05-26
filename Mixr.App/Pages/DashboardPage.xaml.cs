using System.IO;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Mixr.Services;
using Mixr_App.Services;
using VirtualKey = Windows.System.VirtualKey;
using VirtualKeyModifiers = Windows.System.VirtualKeyModifiers;

namespace Mixr_App.Pages;

public sealed partial class DashboardPage : Page
{
    const double FaderMinFill = 6;

    readonly DispatcherQueue _dq = DispatcherQueue.GetForCurrentThread();
    Microsoft.UI.Xaml.DispatcherTimer? _refreshTimer;
    int _tileRefreshRunning;
    bool _pendingInitialFaderSync = true;

    readonly TileUi[] _tiles;
    double _faderTrackHeight = 80;

#if DEBUG
    bool _layoutDevVisible;
    KeyboardAccelerator? _layoutDevAccelerator;
#endif

    static readonly SolidColorBrush ConnectedBrush = new(Microsoft.UI.ColorHelper.FromArgb(255, 76, 175, 80));
    static readonly SolidColorBrush DisconnectedBrush = new(Microsoft.UI.ColorHelper.FromArgb(255, 158, 158, 158));

    sealed class TileUi
    {
        public required Border Tile { get; init; }
        public required Grid InnerGrid { get; init; }
        public required StackPanel ContentStack { get; init; }
        public required Image Image { get; init; }
        public required TextBlock Label { get; init; }
        public required Border FaderFill { get; init; }
        public required Grid FaderTrack { get; init; }
    }

    public DashboardPage()
    {
        InitializeComponent();
        _tiles =
        [
            new TileUi { Tile = Tile0, InnerGrid = TileInnerGrid0, ContentStack = TileContentStack0, Image = TileImage0, Label = TileLabel0, FaderFill = FaderFill0, FaderTrack = FaderTrack0 },
            new TileUi { Tile = Tile1, InnerGrid = TileInnerGrid1, ContentStack = TileContentStack1, Image = TileImage1, Label = TileLabel1, FaderFill = FaderFill1, FaderTrack = FaderTrack1 },
            new TileUi { Tile = Tile2, InnerGrid = TileInnerGrid2, ContentStack = TileContentStack2, Image = TileImage2, Label = TileLabel2, FaderFill = FaderFill2, FaderTrack = FaderTrack2 },
            new TileUi { Tile = Tile3, InnerGrid = TileInnerGrid3, ContentStack = TileContentStack3, Image = TileImage3, Label = TileLabel3, FaderFill = FaderFill3, FaderTrack = FaderTrack3 },
        ];
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _tiles[0].FaderTrack.Loaded += OnFaderTrackLayoutReady;
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        _pendingInitialFaderSync = true;
        MixrRuntimeState.Config.Changed += OnRuntimeChanged;
        MixrRuntimeState.EspConnectionChanged += OnRuntimeChanged;
        MixrRuntimeState.SliderLevelsChanged += OnRuntimeChanged;
#if DEBUG
        LayoutDevToggleButton.Visibility = Visibility.Visible;
        RegisterLayoutDevAccelerator();
#endif
        RefreshAll();
        StartTimer();
    }

    void OnFaderTrackLayoutReady(object sender, RoutedEventArgs e)
    {
        UpdateFaderTrackHeight();
        if (_pendingInitialFaderSync)
            RefreshFaders();
    }

    void UpdateFaderTrackHeight()
    {
        var h = _tiles[0].FaderTrack.ActualHeight;
        if (h > 0)
            _faderTrackHeight = h;
    }

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        MixrRuntimeState.Config.Changed -= OnRuntimeChanged;
        MixrRuntimeState.EspConnectionChanged -= OnRuntimeChanged;
        MixrRuntimeState.SliderLevelsChanged -= OnRuntimeChanged;
#if DEBUG
        UnregisterLayoutDevAccelerator();
#endif
        StopTimer();
    }

#if DEBUG
    void RegisterLayoutDevAccelerator()
    {
        if (_layoutDevAccelerator != null || XamlRoot?.Content is not UIElement root)
            return;

        _layoutDevAccelerator = new KeyboardAccelerator
        {
            Key = VirtualKey.L,
            Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
        };
        _layoutDevAccelerator.Invoked += OnLayoutDevAcceleratorInvoked;
        root.KeyboardAccelerators.Add(_layoutDevAccelerator);
    }

    void UnregisterLayoutDevAccelerator()
    {
        if (_layoutDevAccelerator == null || XamlRoot?.Content is not UIElement root)
            return;

        _layoutDevAccelerator.Invoked -= OnLayoutDevAcceleratorInvoked;
        root.KeyboardAccelerators.Remove(_layoutDevAccelerator);
        _layoutDevAccelerator = null;
    }

    void OnLayoutDevAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs e)
    {
        ToggleLayoutDevPanel();
        e.Handled = true;
    }

    void LayoutDevToggleButton_Click(object sender, RoutedEventArgs e) => ToggleLayoutDevPanel();

    void DevLayout_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_layoutDevVisible)
            return;
        ApplyDevLayoutFromSliders();
    }

    void ToggleLayoutDevPanel()
    {
        _layoutDevVisible = !_layoutDevVisible;
        LayoutDevPanel.Visibility = _layoutDevVisible ? Visibility.Visible : Visibility.Collapsed;
        if (_layoutDevVisible)
            ApplyDevLayoutFromSliders();
    }

    void ApplyDevLayoutFromSliders()
    {
        var pageL = DevPagePadLeft.Value;
        var pageT = DevPagePadTop.Value;
        var pageR = DevPagePadRight.Value;
        var pageB = DevPagePadBottom.Value;

        RootContentGrid.Padding = new Thickness(pageL, pageT, pageR, pageB);
        HeaderGrid.Margin = new Thickness(0, 0, 0, DevHeaderMarginBottom.Value);
        HeaderTitle.FontSize = DevTitleFontSize.Value;
        StatusLabel.FontSize = DevStatusFontSize.Value;
        HeaderStatusPanel.Spacing = DevStatusSpacing.Value;

        var dot = DevStatusDotSize.Value;
        StatusDot.Width = dot;
        StatusDot.Height = dot;
        StatusDot.CornerRadius = new CornerRadius(dot / 2);

        var gridW = DevGridWidth.Value;
        var gridX = DevGridOffsetX.Value;
        var gridY = DevGridOffsetY.Value;
        var colGap = DevColumnSpacing.Value;
        var rowGap = DevRowSpacing.Value;

        TileGrid.Width = gridW;
        TileGrid.Margin = new Thickness(gridX, gridY, 0, 0);
        TileGrid.ColumnSpacing = colGap;
        TileGrid.RowSpacing = rowGap;

        var tileMinH = DevTileMinHeight.Value;
        var tilePad = new Thickness(
            DevTilePadLeft.Value,
            DevTilePadTop.Value,
            DevTilePadRight.Value,
            DevTilePadBottom.Value);
        var tileRadius = DevTileCornerRadius.Value;

        var icon = DevIconSize.Value;
        var stackGap = DevIconLabelSpacing.Value;
        var labelSize = DevLabelFontSize.Value;
        var innerGap = DevContentFaderGap.Value;
        var faderColW = DevFaderColumnWidth.Value;

        _faderTrackHeight = DevFaderHeight.Value;
        var faderW = DevFaderWidth.Value;
        var faderRadius = DevFaderCornerRadius.Value;

        foreach (var t in _tiles)
        {
            t.Tile.MinHeight = tileMinH;
            t.Tile.Padding = tilePad;
            t.Tile.CornerRadius = new CornerRadius(tileRadius);

            t.InnerGrid.ColumnSpacing = innerGap;
            if (t.InnerGrid.ColumnDefinitions.Count > 1)
                t.InnerGrid.ColumnDefinitions[1].Width = new GridLength(faderColW);

            t.ContentStack.Spacing = stackGap;
            t.Image.Width = icon;
            t.Image.Height = icon;
            t.Label.FontSize = labelSize;

            t.FaderTrack.Width = faderW;
            t.FaderTrack.Height = _faderTrackHeight;
            ApplyFaderCornerRadius(t.FaderTrack, faderRadius);
        }

        RefreshFaders();
        LayoutDevValues.Text = BuildLayoutDevExportText(
            pageL, pageT, pageR, pageB,
            DevHeaderMarginBottom.Value, DevTitleFontSize.Value, DevStatusFontSize.Value,
            DevStatusSpacing.Value, DevStatusDotSize.Value,
            gridW, gridX, gridY, colGap, rowGap,
            tileMinH, tilePad, tileRadius,
            icon, stackGap, labelSize, innerGap, faderColW,
            faderW, _faderTrackHeight, faderRadius);
    }

    static void ApplyFaderCornerRadius(Grid faderTrack, double radius)
    {
        var r = new CornerRadius(radius);
        foreach (var child in faderTrack.Children)
        {
            if (child is Border border)
                border.CornerRadius = r;
        }
    }

    static string BuildLayoutDevExportText(
        double pageL, double pageT, double pageR, double pageB,
        double headerMarginBottom, double titleSize, double statusSize, double statusSpacing, double statusDot,
        double gridW, double gridX, double gridY, double colGap, double rowGap,
        double tileMinH, Thickness tilePad, double tileRadius,
        double icon, double stackGap, double labelSize, double innerGap, double faderColW,
        double faderW, double faderH, double faderRadius)
    {
        return
            "── Seite (RootContentGrid) ──\n" +
            $"Padding=\"{(int)pageL},{(int)pageT},{(int)pageR},{(int)pageB}\"\n\n" +
            "── Kopfzeile ──\n" +
            $"HeaderGrid Margin=\"0,0,0,{(int)headerMarginBottom}\"\n" +
            $"HeaderTitle FontSize=\"{(int)titleSize}\"\n" +
            $"StatusLabel FontSize=\"{(int)statusSize}\"\n" +
            $"HeaderStatusPanel Spacing=\"{(int)statusSpacing}\"\n" +
            $"StatusDot {(int)statusDot}×{(int)statusDot}\n\n" +
            "── Kachel-Grid (TileGrid) ──\n" +
            $"Width=\"{(int)gridW}\"\n" +
            $"Margin=\"{(int)gridX},{(int)gridY},0,0\"\n" +
            $"ColumnSpacing=\"{(int)colGap}\" RowSpacing=\"{(int)rowGap}\"\n\n" +
            "── Kachel (alle Tile0–3) ──\n" +
            $"MinHeight=\"{(int)tileMinH}\"\n" +
            $"Padding=\"{(int)tilePad.Left},{(int)tilePad.Top},{(int)tilePad.Right},{(int)tilePad.Bottom}\"\n" +
            $"CornerRadius=\"{(int)tileRadius}\"\n\n" +
            "── Icon & Text ──\n" +
            $"Image {(int)icon}×{(int)icon}\n" +
            $"TileContentStack Spacing=\"{(int)stackGap}\"\n" +
            $"TileLabel FontSize=\"{(int)labelSize}\"\n" +
            $"TileInnerGrid ColumnSpacing=\"{(int)innerGap}\"\n" +
            $"Regler-Spalte Width=\"{(int)faderColW}\"\n\n" +
            "── Regler ──\n" +
            $"FaderTrack Width=\"{(int)faderW}\" Height=\"{(int)faderH}\"\n" +
            $"Fader CornerRadius=\"{(int)faderRadius}\"";
    }
#else
    void LayoutDevToggleButton_Click(object sender, RoutedEventArgs e) { }

    void DevLayout_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) { }
#endif

    void OnRuntimeChanged() => _dq.TryEnqueue(RefreshAll);

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

    void RefreshAll()
    {
        RefreshStatus();
        RefreshFaders();
        _ = RefreshTilesAsync();
    }

    void RefreshStatus()
    {
        var connected = MixrRuntimeState.EspConnected;
        StatusDot.Background = connected ? ConnectedBrush : DisconnectedBrush;
        ToolTipService.SetToolTip(StatusDot, connected ? "Mixr connected" : "Mixr not connected");
    }

    void RefreshFaders() => ApplyFaderLevels(MixrRuntimeState.GetSliderLevelsSnapshot());

    void RefreshFadersAfterInitialSessionScan()
    {
        UpdateFaderTrackHeight();
        var runtime = MixrRuntimeState.GetSliderLevelsSnapshot();
        var cfg = MixrRuntimeState.Config.Current;
        var fromAudio = MixrRuntimeState.Audio?.GetVolumeLevels(cfg.SliderMapping);
        if (fromAudio == null || fromAudio.Length == 0)
        {
            ApplyFaderLevels(runtime);
            return;
        }

        var merged = new float[_tiles.Length];
        for (var i = 0; i < merged.Length; i++)
        {
            if (i < runtime.Length && runtime[i] >= 0)
                merged[i] = runtime[i];
            else if (i < fromAudio.Length && fromAudio[i] >= 0)
                merged[i] = fromAudio[i];
            else
                merged[i] = 0f;
        }

        ApplyFaderLevels(merged);
    }

    void ApplyFaderLevels(float[] levels)
    {
        for (var i = 0; i < _tiles.Length; i++)
        {
            var level = i < levels.Length && levels[i] >= 0 ? Math.Clamp(levels[i], 0f, 1f) : 0f;
            _tiles[i].FaderFill.Height = Math.Max(FaderMinFill, _faderTrackHeight * level);
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
                await Task.Run(() =>
                    audio.RebuildSessionMap(cfg.SliderMapping, cfg.SessionGroups, silent: true));

            var live = audio?.GetLiveSnapshot();

            if (_pendingInitialFaderSync && audio != null)
            {
                _pendingInitialFaderSync = false;
                _dq.TryEnqueue(RefreshFadersAfterInitialSessionScan);
            }

            for (var i = 0; i < _tiles.Length; i++)
            {
                var info = SliderSummaryIconResolver.Resolve(i, cfg, live);
                var idx = i;
                var label = info.Label;
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
                    if (idx >= _tiles.Length)
                        return;
                    var t = _tiles[idx];
                    t.Label.Text = label;
                    ToolTipService.SetToolTip(t.Tile, tooltip);
                    if (src != null)
                        t.Image.Source = src;
                });
            }
        }
        finally
        {
            Interlocked.Exchange(ref _tileRefreshRunning, 0);
        }
    }
}
