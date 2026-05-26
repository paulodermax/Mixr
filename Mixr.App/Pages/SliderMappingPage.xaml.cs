using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Mixr.Models;
using Mixr.Services;
using Mixr_App;
using Mixr_App.Controls;
using Mixr_App.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Border = Microsoft.UI.Xaml.Controls.Border;

namespace Mixr_App.Pages;

public sealed partial class SliderMappingPage : Page
{
    static int s_coverUiMissingPathLogged;

    readonly DispatcherQueue _dq = DispatcherQueue.GetForCurrentThread();

    readonly ObservableCollection<CatalogGameVm> _catalogGames = new();

    MixrConfig _draft = new();
    List<SliderCardVm>? _sliderCards;
    Microsoft.UI.Xaml.DispatcherTimer? _liveTimer;
    bool _suppressNextRuntimeConfigReload;

    static SliderMappingPage? _activeInstance;

    static readonly Brush DefaultDropBorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
    static readonly Brush AccentBrush = (Brush)Application.Current.Resources["MixrAccentBrush"];

    public SliderMappingPage()
    {
        InitializeComponent();
        CatalogGamesList.ItemsSource = _catalogGames;
        _activeInstance = this;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Slider mapping is auto-saved on each change; nothing to flush on window hide.</summary>
    public static void TryPersistOnWindowClose()
    {
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        GameCatalogCoordinator.CatalogChanged += OnCatalogChanged;
        MixrRuntimeState.Config.Changed += OnRuntimeConfigChanged;
        LoadDraftFromDisk();
        StartLiveTimer();
    }

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        GameCatalogCoordinator.CatalogChanged -= OnCatalogChanged;
        MixrRuntimeState.Config.Changed -= OnRuntimeConfigChanged;
        StopLiveTimer();
        if (ReferenceEquals(_activeInstance, this))
            _activeInstance = null;
    }

    void StartLiveTimer()
    {
        StopLiveTimer();
        _liveTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
        _liveTimer.Tick += (_, _) => _ = RefreshLiveActivityAsync();
        _liveTimer.Start();
    }

    void StopLiveTimer()
    {
        _liveTimer?.Stop();
        _liveTimer = null;
    }

    void OnCatalogChanged(object? s, EventArgs e) =>
        _dq.TryEnqueue(() =>
        {
            LoadCatalog();
            RefreshAllAssignedCovers();
        });

    void OnRuntimeConfigChanged()
    {
        _dq.TryEnqueue(() =>
        {
            if (_suppressNextRuntimeConfigReload)
            {
                _suppressNextRuntimeConfigReload = false;
                return;
            }

            LoadDraftFromDisk();
        });
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadDraftFromDisk();
    }

    async void ReloadCatalog_Click(object sender, RoutedEventArgs e)
    {
        ReloadCatalogButton.IsEnabled = false;
        try
        {
            await GameCatalogCoordinator.ForceWeeklyRefreshAsync(CancellationToken.None).ConfigureAwait(true);
            CatalogManualCoverSync.ApplyManualFilesToStore();
            await CoverWarmup.PreloadAllAsync();
            LoadCatalog();
            RefreshAllAssignedCovers();
        }
        finally
        {
            ReloadCatalogButton.IsEnabled = true;
        }
    }

    void ResetDraft_Click(object sender, RoutedEventArgs e)
    {
        _draft = MixrConfigClone.DeepClone(MixrConfigLoader.Load(Array.Empty<string>()));
        var defaults = new MixrConfig();
        _draft.SliderMapping = new List<string>(defaults.SliderMapping);
        _draft.SessionGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // Wie beim App-Start (SessionGroupsBootstrap): erkannte Apps + Steam-Katalog in die Gruppen legen.
        CatalogIgnoreList.EnsureLauncherIgnoreLines();
        SessionGroupsLauncherPrune.RemoveLauncherTokensFromAllGroups(_draft);
        if (!_draft.SessionGroups.ContainsKey("master"))
            _draft.SessionGroups["master"] = [];
        SessionGroupsAutoMerge.MergeDetectedInto(_draft);
        SessionGroupsCatalogMerge.MergeSteamGamesInto(_draft);

        PersistMappingAndSync();
    }

    void LoadDraftFromDisk()
    {
        _draft = MixrConfigClone.DeepClone(MixrConfigLoader.Load(Array.Empty<string>()));
        VolumeCurveMapper.EnsureFourEntries(_draft.SliderResponse);
        RebuildSliderCardsFromDraft();
        LoadCatalog();
    }

    void SoundCurveMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var card = FindSliderCardFromElement(button);
        if (card == null)
            return;

        var idx = card.SliderIndex;
        VolumeCurveMapper.EnsureFourEntries(_draft.SliderResponse);
        var currentKey = _draft.SliderResponse[idx];

        var picker = new SoundCurvePickerControl();
        picker.Bind(idx, card.Title, currentKey, yamlKey =>
        {
            if (_draft.SliderResponse[idx].Equals(yamlKey, StringComparison.OrdinalIgnoreCase))
                return;

            _draft.SliderResponse[idx] = yamlKey;
            PersistSoundResponseOnly();
            AppLog.WriteLine($"Sound-Mapping: Slider {idx + 1} → {yamlKey}");
        });

        var flyout = new Flyout
        {
            Content = picker,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
        };

        if (XamlRoot != null)
            flyout.XamlRoot = XamlRoot;
        flyout.ShowAt(button);
    }

    static SliderCardVm? FindSliderCardFromElement(DependencyObject element)
    {
        var current = element;
        while (current != null)
        {
            if (current is FrameworkElement { DataContext: SliderCardVm card })
                return card;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    /// <summary>Write config.yaml, reload host (sliders → session matching), refresh library and cards.</summary>
    void PersistMappingAndSync()
    {
        try
        {
            MixrConfigWriter.Save(_draft, MixrConfigPaths.ConfigYamlPath);
            _suppressNextRuntimeConfigReload = true;
            MixrRuntimeState.ReloadConfigFromDisk(Array.Empty<string>());
            AppLog.WriteLine("Slider-Editing: saved config.yaml, runtime config reloaded.");
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("Save failed: " + ex.Message);
            _suppressNextRuntimeConfigReload = false;
            return;
        }

        LoadCatalog();
        RebuildSliderCardsFromDraft();
    }

    void PersistSoundResponseOnly()
    {
        try
        {
            MixrConfigWriter.Save(_draft, MixrConfigPaths.ConfigYamlPath);
            _suppressNextRuntimeConfigReload = true;
            MixrRuntimeState.ReloadConfigFromDisk(Array.Empty<string>());
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("Sound-Mapping save failed: " + ex.Message);
            _suppressNextRuntimeConfigReload = false;
        }
    }

    static HashSet<string> GetAssignedTokens(MixrConfig cfg)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in cfg.SessionGroups.Values)
        {
            foreach (var t in list)
            {
                if (!string.IsNullOrWhiteSpace(t))
                    set.Add(t.Trim());
            }
        }

        return set;
    }

    void RebuildSliderCardsFromDraft()
    {
        var store = GameCatalogStore.LoadOrCreate();
        if (_sliderCards == null || _sliderCards.Count != _draft.SliderMapping.Count)
        {
            var cards = new List<SliderCardVm>();
            for (var i = 0; i < _draft.SliderMapping.Count; i++)
            {
                var key = _draft.SliderMapping[i];
                var card = new SliderCardVm
                {
                    SliderIndex = i,
                    SliderKey = key,
                    Title = $"{i + 1}. {HumanizeKey(key)}",
                };

                if (_draft.SessionGroups.TryGetValue(key, out var list))
                {
                    foreach (var s in list.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        var row = new AssignedProgramRow
                        {
                            SliderIndex = i,
                            Token = s,
                            DisplayName = CatalogGameEntryLookup.FindBest(store, s)?.Name ?? s,
                        };
                        card.AssignedPrograms.Add(row);
                    }
                }

                card.ShowEmptyHint = card.AssignedPrograms.Count == 0;
                cards.Add(card);
            }

            _sliderCards = cards;
            SetFaderZoneDataContexts(cards);
            RefreshAllAssignedCovers();
            RefreshLiveActivity();
            return;
        }

        for (var i = 0; i < _draft.SliderMapping.Count; i++)
        {
            var key = _draft.SliderMapping[i];
            var card = _sliderCards[i];
            card.SliderIndex = i;
            card.SliderKey = key;
            card.Title = $"{i + 1}. {HumanizeKey(key)}";
            SyncCardAssignedPrograms(card, i, key, store);
            card.ShowEmptyHint = card.AssignedPrograms.Count == 0;
        }

        RefreshAllAssignedCovers();
        RefreshLiveActivity();
    }

    void SyncCardAssignedPrograms(SliderCardVm card, int sliderIndex, string sliderKey, GameCatalogStore store)
    {
        var sorted = (_draft.SessionGroups.TryGetValue(sliderKey, out var list)
                ? list.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var cur = card.AssignedPrograms.Select(r => r.Token).ToList();
        if (sorted.Count == cur.Count &&
            sorted.Zip(cur, (a, b) => a.Equals(b, StringComparison.OrdinalIgnoreCase)).All(x => x))
            return;

        for (var i = card.AssignedPrograms.Count - 1; i >= 0; i--)
        {
            var t = card.AssignedPrograms[i].Token;
            if (!sorted.Any(s => s.Equals(t, StringComparison.OrdinalIgnoreCase)))
                card.AssignedPrograms.RemoveAt(i);
        }

        foreach (var t in sorted)
        {
            if (card.AssignedPrograms.Any(r => r.Token.Equals(t, StringComparison.OrdinalIgnoreCase)))
                continue;

            var row = new AssignedProgramRow
            {
                SliderIndex = sliderIndex,
                Token = t,
                DisplayName = CatalogGameEntryLookup.FindBest(store, t)?.Name ?? t,
            };
            var pos = 0;
            while (pos < card.AssignedPrograms.Count &&
                   string.Compare(card.AssignedPrograms[pos].Token, t, StringComparison.OrdinalIgnoreCase) < 0)
                pos++;
            card.AssignedPrograms.Insert(pos, row);
        }
    }

    void SetFaderZoneDataContexts(IReadOnlyList<SliderCardVm> cards)
    {
        void Set(Border? zone, int index)
        {
            if (zone == null)
                return;
            zone.DataContext = index < cards.Count ? cards[index] : null;
        }

        Set(FaderZone0, 0);
        Set(FaderZone1, 1);
        Set(FaderZone2, 2);
        Set(FaderZone3, 3);
    }

    /// <summary>Nach Katalog-Refresh: Cover erneut binden (Dateien können neu geschrieben worden sein).</summary>
    void RefreshAllAssignedCovers()
    {
        var store = GameCatalogStore.LoadOrCreate();
        if (_sliderCards is not { Count: > 0 })
            return;
        foreach (var card in _sliderCards)
        {
            foreach (var row in card.AssignedPrograms)
                TryLoadAssignedCover(row, store);
        }
    }

    void TryLoadAssignedCover(AssignedProgramRow row, GameCatalogStore store)
    {
        var entry = CatalogGameEntryLookup.FindBest(store, row.Token);
        string? rel;
        if (entry != null)
            rel = CatalogCoverResolver.ResolveRelativePath(entry, store);
        else
            rel = ManualCoverResolver.TryFindRelativePathByLabel(row.Token);

        if (string.IsNullOrEmpty(rel))
            return;

        var full = GameCatalogPaths.ResolvePath(rel);
        if (!File.Exists(full))
            return;

        if (entry != null)
            row.DisplayName = entry.Name;

        _dq.TryEnqueue(() => { _ = LoadAssignedCoverAsync(row, full); });
    }

    async Task LoadAssignedCoverAsync(AssignedProgramRow row, string full)
    {
        var src = await CoverImageLoader.LoadCoverImageSourceAsync(full).ConfigureAwait(true);
        if (src == null)
            return;
        _dq.TryEnqueue(() => row.Cover = src);
    }

    void RefreshLiveActivity() => _ = RefreshLiveActivityAsync();

    async Task RefreshLiveActivityAsync()
    {
        if (_sliderCards == null)
            return;

        var cfg = MixrRuntimeState.Config.Current;
        var audio = MixrRuntimeState.Audio;
        if (audio != null)
        {
            await Task.Run(() =>
                audio.RebuildSessionMap(cfg.SliderMapping, cfg.SessionGroups, silent: true));
        }

        var snap = audio?.GetLiveSnapshot();
        foreach (var card in _sliderCards)
        {
            if (snap != null &&
                snap.TryGetValue(card.SliderKey, out var names) &&
                names is { Count: > 0 })
            {
                card.LiveActivity = string.Join(" · ", names);
                continue;
            }

            if (card.SliderKey.Equals("master", StringComparison.OrdinalIgnoreCase))
            {
                card.LiveActivity = "Default playback device (mix)";
                continue;
            }

            card.LiveActivity = "No matching session";
        }
    }

    static string HumanizeKey(string key) =>
        key.Length switch
        {
            0 => key,
            1 => char.ToUpperInvariant(key[0]).ToString(),
            _ => char.ToUpperInvariant(key[0]) + key[1..],
        };

    async void CatalogItem_DragStarting(object sender, DragStartingEventArgs args)
    {
        if (sender is Border b)
            StartDragWobble(b);
        var deferral = args.GetDeferral();
        try
        {
            if (sender is FrameworkElement fe && fe.DataContext is CatalogGameVm g)
            {
                args.Data.SetText(g.Token);
                args.Data.RequestedOperation = DataPackageOperation.Copy;
                await SetDragPreviewFromElementAsync(args, fe).ConfigureAwait(true);
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    async void AssignedItem_DragStarting(object sender, DragStartingEventArgs args)
    {
        if (sender is Border b)
            StartDragWobble(b);
        var deferral = args.GetDeferral();
        try
        {
            if (sender is FrameworkElement fe && fe.DataContext is AssignedProgramRow row)
            {
                args.Data.SetText(row.Token);
                args.Data.RequestedOperation = DataPackageOperation.Copy;
                await SetDragPreviewFromElementAsync(args, fe).ConfigureAwait(true);
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    static async System.Threading.Tasks.Task SetDragPreviewFromElementAsync(DragStartingEventArgs args, UIElement element)
    {
        try
        {
            if (args.DragUI == null)
                return;
            if (element is not FrameworkElement fe)
                return;
            var w = Math.Max(1, (int)Math.Ceiling(fe.ActualWidth));
            var h = Math.Max(1, (int)Math.Ceiling(fe.ActualHeight));
            if (w < 2 || h < 2)
                return;
            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(fe, w, h);
            var pixels = await rtb.GetPixelsAsync();
            var sb = SoftwareBitmap.CreateCopyFromBuffer(
                pixels,
                BitmapPixelFormat.Bgra8,
                (int)rtb.PixelWidth,
                (int)rtb.PixelHeight,
                BitmapAlphaMode.Premultiplied);
            args.DragUI.SetContentFromSoftwareBitmap(sb);
        }
        catch
        {
            /* keep default ghost */
        }
    }

    void LibraryDropZone_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.Text))
            return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.Handled = true;
        if (e.DragUIOverride != null)
        {
            e.DragUIOverride.IsCaptionVisible = false;
            e.DragUIOverride.IsGlyphVisible = false;
        }

        HighlightLibraryDrop(true);
    }

    void LibraryDropZone_DragEnter(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.Text))
            return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.Handled = true;
        if (e.DragUIOverride != null)
        {
            e.DragUIOverride.IsCaptionVisible = false;
            e.DragUIOverride.IsGlyphVisible = false;
        }

        HighlightLibraryDrop(true);
    }

    void LibraryDropZone_DragLeave(object sender, DragEventArgs e)
    {
        e.Handled = true;
        HighlightLibraryDrop(false);
    }

    async void LibraryDropZone_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        HighlightLibraryDrop(false);
        try
        {
            if (!e.DataView.Contains(StandardDataFormats.Text))
                return;
            var token = (await e.DataView.GetTextAsync()).Trim();
            if (string.IsNullOrEmpty(token))
                return;
            RemoveTokenFromAllMappings(_draft, token);
            PersistMappingAndSync();
        }
        catch
        {
            /* */
        }
    }

    void HighlightLibraryDrop(bool on)
    {
        if (LibraryPanel == null)
            return;
        if (on)
        {
            LibraryPanel.BorderBrush = AccentBrush;
            LibraryPanel.BorderThickness = new Thickness(2);
        }
        else
        {
            LibraryPanel.BorderBrush = DefaultDropBorderBrush;
            LibraryPanel.BorderThickness = new Thickness(1);
        }
    }

    void AssignedScroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv)
            return;
        var ic = FindFirstDescendant<ItemsControl>(sv);
        if (ic?.ItemsPanelRoot is not ItemsWrapGrid grid || e.NewSize.Width <= 0)
            return;
        const double gutter = 6;
        var w = e.NewSize.Width;
        // Three columns; two gutters between tiles (same 6px margins as with 2 columns).
        var colW = (w - gutter * 2) / 3.0;
        grid.ItemWidth = Math.Max(36, colW);
        // 2:3 portrait — full cover visible with Stretch Uniform in the cell.
        grid.ItemHeight = Math.Max(54, colW * 1.5);
    }

    static T? FindFirstDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T match)
                return match;
            var found = FindFirstDescendant<T>(c);
            if (found != null)
                return found;
        }

        return null;
    }

    void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.Handled = true;
        if (e.DragUIOverride != null)
        {
            e.DragUIOverride.IsCaptionVisible = false;
            e.DragUIOverride.IsGlyphVisible = false;
        }

        HighlightFaderColumn(sender as FrameworkElement, true);
    }

    void DropZone_DragEnter(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.Handled = true;
        if (e.DragUIOverride != null)
        {
            e.DragUIOverride.IsCaptionVisible = false;
            e.DragUIOverride.IsGlyphVisible = false;
        }

        HighlightFaderColumn(sender as FrameworkElement, true);
    }

    void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        e.Handled = true;
        HighlightFaderColumn(sender as FrameworkElement, false);
    }

    static Border? FindFaderColumnChrome(FrameworkElement? fe)
    {
        for (var p = fe; p != null; p = p.Parent as FrameworkElement)
        {
            if (p is Border b && b.Name.StartsWith("FaderZone", StringComparison.Ordinal))
                return b;
        }

        return null;
    }

    void HighlightFaderColumn(FrameworkElement? sender, bool on)
    {
        var b = sender as Border ?? FindFaderColumnChrome(sender);
        if (b == null)
            return;
        if (on)
        {
            b.BorderBrush = AccentBrush;
            b.BorderThickness = new Thickness(2);
        }
        else
        {
            b.BorderBrush = DefaultDropBorderBrush;
            b.BorderThickness = new Thickness(1);
            b.Background = FaderZoneDefaultBackgroundBrush;
        }
    }

    static readonly Brush FaderZoneDefaultBackgroundBrush =
        (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];

    async void DropZone_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        HighlightFaderColumn(sender as FrameworkElement, false);
        try
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not SliderCardVm card)
                return;

            if (!e.DataView.Contains(StandardDataFormats.Text))
                return;

            var token = (await e.DataView.GetTextAsync()).Trim();
            if (string.IsNullOrEmpty(token))
                return;

            AssignGameTokenToSlider(_draft, card.SliderIndex, token);
            PersistMappingAndSync();
        }
        catch
        {
            /* */
        }
    }

    void AssignedCover_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border b)
            SetupCoverTileTransforms(b);
    }

    void CatalogCoverItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border b)
            SetupCoverTileTransforms(b);
    }

    static void SetupCoverTileTransforms(Border b)
    {
        b.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
        var rotate = new RotateTransform { Angle = 0 };
        var tg = new TransformGroup();
        tg.Children.Add(scale);
        tg.Children.Add(rotate);
        b.RenderTransform = tg;
    }

    static ScaleTransform? GetCoverScaleTransform(Border? b) =>
        b?.RenderTransform is TransformGroup tg && tg.Children.Count >= 1 && tg.Children[0] is ScaleTransform st
            ? st
            : null;

    static RotateTransform? GetCoverRotateTransform(Border? b) =>
        b?.RenderTransform is TransformGroup tg && tg.Children.Count >= 2 && tg.Children[1] is RotateTransform rt
            ? rt
            : null;

    void CoverTile_DropCompleted(object sender, DropCompletedEventArgs e)
    {
        if (sender is Border b)
            StopCoverWobble(b);
    }

    void StartDragWobble(Border b)
    {
        StopCoverWobble(b);
        var rt = GetCoverRotateTransform(b);
        if (rt == null)
            return;
        rt.Angle = 0;
        var anim = new DoubleAnimation
        {
            From = -2.2,
            To = 2.2,
            Duration = TimeSpan.FromMilliseconds(340),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(anim, rt);
        Storyboard.SetTargetProperty(anim, "Angle");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        b.Tag = sb;
        sb.Begin();
    }

    void StopCoverWobble(Border? b)
    {
        if (b == null)
            return;
        if (b.Tag is Storyboard sb)
        {
            sb.Stop();
            b.Tag = null;
        }

        if (GetCoverRotateTransform(b) is { } rt)
            rt.Angle = 0;
    }

    void AssignedCover_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border b)
        {
            if (b.DataContext is AssignedProgramRow row && !string.IsNullOrWhiteSpace(row.TooltipText))
                ToolTipService.SetToolTip(b, row.TooltipText);
            if (GetCoverScaleTransform(b) is ScaleTransform st)
                AnimateCoverScale(st, 1.08);
        }
    }

    void AssignedCover_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border b)
        {
            ToolTipService.SetToolTip(b, null);
            if (GetCoverScaleTransform(b) is ScaleTransform st)
                AnimateCoverScale(st, 1.0);
        }
    }

    static void AnimateCoverScale(ScaleTransform st, double to)
    {
        var dur = TimeSpan.FromMilliseconds(240);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var sx = new DoubleAnimation { To = to, Duration = dur, EasingFunction = ease };
        var sy = new DoubleAnimation { To = to, Duration = dur, EasingFunction = ease };
        Storyboard.SetTarget(sx, st);
        Storyboard.SetTarget(sy, st);
        Storyboard.SetTargetProperty(sx, "ScaleX");
        Storyboard.SetTargetProperty(sy, "ScaleY");
        var sb = new Storyboard();
        sb.Children.Add(sx);
        sb.Children.Add(sy);
        sb.Begin();
    }

    void LoadCatalog()
    {
        CatalogManualCoverSync.ApplyManualFilesToStore();
        var store = GameCatalogStore.LoadOrCreate();
        var assigned = GetAssignedTokens(_draft);
        var visible = store.Games
            .Where(g => !IsCatalogEntryAssigned(g, assigned))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        LibraryMetaText.Text = BuildLibraryMetaLine(store);

        var empty = visible.Count == 0;
        LibraryEmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        CatalogScrollViewer.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        var desired = new List<(CatalogGameEntry Entry, string Token)>(visible.Count);
        foreach (var g in visible)
        {
            var token = string.IsNullOrEmpty(g.AssignmentToken) ? g.Name : g.AssignmentToken;
            desired.Add((g, token));
        }

        var desireTokens = desired.Select(d => d.Token).ToList();
        var curTokens = _catalogGames.Select(vm => vm.Token).ToList();
        if (curTokens.Count == desireTokens.Count &&
            curTokens.Zip(desireTokens, (a, b) => a.Equals(b, StringComparison.OrdinalIgnoreCase)).All(x => x))
        {
            var byTok = _catalogGames.ToDictionary(vm => vm.Token, StringComparer.OrdinalIgnoreCase);
            foreach (var (entry, token) in desired)
            {
                if (byTok.TryGetValue(token, out var vm))
                    TryLoadCover(vm, entry);
            }

            return;
        }

        var desiredSet = new HashSet<string>(desireTokens, StringComparer.OrdinalIgnoreCase);
        for (var i = _catalogGames.Count - 1; i >= 0; i--)
        {
            if (!desiredSet.Contains(_catalogGames[i].Token))
                _catalogGames.RemoveAt(i);
        }

        var byToken = _catalogGames.ToDictionary(vm => vm.Token, StringComparer.OrdinalIgnoreCase);
        foreach (var (entry, token) in desired)
        {
            if (byToken.ContainsKey(token))
                continue;

            var vm = new CatalogGameVm(entry.Name, token);
            var insert = 0;
            while (insert < _catalogGames.Count &&
                   string.Compare(_catalogGames[insert].Name, vm.Name, StringComparison.OrdinalIgnoreCase) < 0)
                insert++;
            _catalogGames.Insert(insert, vm);
            byToken[token] = vm;
            TryLoadCover(vm, entry);
        }

        var orderOk = _catalogGames.Count == desireTokens.Count;
        if (orderOk)
        {
            for (var i = 0; i < _catalogGames.Count; i++)
            {
                if (!_catalogGames[i].Token.Equals(desireTokens[i], StringComparison.OrdinalIgnoreCase))
                {
                    orderOk = false;
                    break;
                }
            }
        }

        if (!orderOk)
        {
            var vmDict = _catalogGames.ToDictionary(vm => vm.Token, StringComparer.OrdinalIgnoreCase);
            _catalogGames.Clear();
            foreach (var t in desireTokens)
            {
                if (vmDict.TryGetValue(t, out var vm))
                    _catalogGames.Add(vm);
            }
        }

        {
            var byTok = _catalogGames.ToDictionary(vm => vm.Token, StringComparer.OrdinalIgnoreCase);
            foreach (var (entry, token) in desired)
            {
                if (byTok.TryGetValue(token, out var vm))
                    TryLoadCover(vm, entry);
            }
        }
    }

    static bool IsCatalogEntryAssigned(CatalogGameEntry g, HashSet<string> assigned)
    {
        if (assigned.Contains(g.Name))
            return true;
        if (!string.IsNullOrEmpty(g.AssignmentToken) && assigned.Contains(g.AssignmentToken))
            return true;
        return false;
    }

    static string BuildLibraryMetaLine(GameCatalogStore store)
    {
        var timePart = store.LastWeeklyCatalogUtc == default
            ? "—"
            : store.LastWeeklyCatalogUtc.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
        return $"Last update: {timePart} - {store.Games.Count} found";
    }

    static void RemoveTokenFromAllMappings(MixrConfig cfg, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;
        token = token.Trim();
        foreach (var k in cfg.SessionGroups.Keys.ToList())
        {
            if (!cfg.SessionGroups.TryGetValue(k, out var list))
                continue;
            list.RemoveAll(s => s.Equals(token, StringComparison.OrdinalIgnoreCase));
            if (list.Count == 0)
                cfg.SessionGroups.Remove(k);
        }
    }

    void TryLoadCover(CatalogGameVm vm, CatalogGameEntry entry)
    {
        var rel = CatalogCoverResolver.ResolveRelativePath(entry);
        if (string.IsNullOrEmpty(rel))
        {
            if (!string.IsNullOrEmpty(entry.CoverRelativePath) && Interlocked.Increment(ref s_coverUiMissingPathLogged) <= 40)
            {
                var attempted = GameCatalogPaths.ResolvePath(entry.CoverRelativePath);
                AppLog.WriteLine(
                    $"[Cover UI] Katalog hat CoverRelativePath='{entry.CoverRelativePath}' aber Datei fehlt: {attempted} (Spiel: {entry.Name})");
            }

            return;
        }

        var full = GameCatalogPaths.ResolvePath(rel);
        if (!File.Exists(full))
        {
            if (Interlocked.Increment(ref s_coverUiMissingPathLogged) <= 40)
                AppLog.WriteLine($"[Cover UI] Aufgelöster Pfad '{rel}' fehlt auf Disk: {full} (Spiel: {entry.Name})");
            return;
        }

        _dq.TryEnqueue(() => { _ = LoadCatalogCoverAsync(vm, full); });
    }

    async Task LoadCatalogCoverAsync(CatalogGameVm vm, string full)
    {
        var src = await CoverImageLoader.LoadCoverImageSourceAsync(full).ConfigureAwait(true);
        if (src == null)
        {
            if (Interlocked.Increment(ref s_coverUiMissingPathLogged) <= 40)
                AppLog.WriteLine($"[Cover UI] LoadCoverImageSourceAsync liefert null für: {full}");
            return;
        }

        _dq.TryEnqueue(() => vm.Icon = src);
    }

    static void AssignGameTokenToSlider(MixrConfig cfg, int sliderIndex, string gameToken)
    {
        if (sliderIndex < 0 || sliderIndex >= cfg.SliderMapping.Count)
            return;

        var key = cfg.SliderMapping[sliderIndex];
        if (string.IsNullOrWhiteSpace(gameToken))
            return;

        gameToken = gameToken.Trim();

        foreach (var k in cfg.SessionGroups.Keys.ToList())
        {
            if (!cfg.SessionGroups.TryGetValue(k, out var list))
                continue;
            list.RemoveAll(s => s.Equals(gameToken, StringComparison.OrdinalIgnoreCase));
            if (list.Count == 0)
                cfg.SessionGroups.Remove(k);
        }

        if (!cfg.SessionGroups.TryGetValue(key, out var target))
        {
            target = new List<string>();
            cfg.SessionGroups[key] = target;
        }

        if (!target.Any(s => s.Equals(gameToken, StringComparison.OrdinalIgnoreCase)))
            target.Add(gameToken);
    }

}
