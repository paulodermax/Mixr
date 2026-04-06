using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Mixr.Models;
using Mixr.Services;
using Mixr_App.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Border = Microsoft.UI.Xaml.Controls.Border;

namespace Mixr_App.Pages;

public sealed partial class SliderMappingPage : Page
{
    readonly DispatcherQueue _dq = DispatcherQueue.GetForCurrentThread();

    readonly ObservableCollection<CatalogGameVm> _catalogGames = new();

    MixrConfig _draft = new();
    bool _dirty;
    List<SliderCardVm>? _sliderCards;
    Microsoft.UI.Xaml.DispatcherTimer? _liveTimer;

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

    /// <summary>Vor dem Ausblenden des Fensters (Tray): offene ungespeicherte Zuordnung sichern.</summary>
    public static void TryPersistOnWindowClose() => _activeInstance?.PersistIfDirtyOnWindowClose();

    void PersistIfDirtyOnWindowClose()
    {
        if (!_dirty)
            return;
        PersistMappingAndSync();
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
        _liveTimer.Tick += (_, _) => _dq.TryEnqueue(RefreshLiveActivity);
        _liveTimer.Start();
    }

    void StopLiveTimer()
    {
        _liveTimer?.Stop();
        _liveTimer = null;
    }

    void OnCatalogChanged(object? s, EventArgs e) => _dq.TryEnqueue(LoadCatalog);

    void OnRuntimeConfigChanged()
    {
        _dq.TryEnqueue(() =>
        {
            if (!_dirty)
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
        }
        finally
        {
            ReloadCatalogButton.IsEnabled = true;
        }
    }

    void SaveDraft_Click(object sender, RoutedEventArgs e) => PersistMappingAndSync();

    void ResetDraft_Click(object sender, RoutedEventArgs e)
    {
        LoadDraftFromDisk();
    }

    void LoadDraftFromDisk()
    {
        _draft = MixrConfigClone.DeepClone(MixrConfigLoader.Load(Array.Empty<string>()));
        _dirty = false;
        RebuildSliderCardsFromDraft();
        LoadCatalog();
        UpdateSaveUi();
    }

    void UpdateSaveUi()
    {
        if (SaveDraftButton != null)
            SaveDraftButton.IsEnabled = _dirty;
        if (UnsavedHintText != null)
            UnsavedHintText.Visibility = _dirty ? Visibility.Visible : Visibility.Collapsed;
    }

    void MarkDirty()
    {
        _dirty = true;
        UpdateSaveUi();
    }

    /// <summary>config.yaml schreiben, Host neu laden (Fader → Session-Matching), Bibliothek + Karten aktualisieren.</summary>
    void PersistMappingAndSync()
    {
        try
        {
            MixrConfigWriter.Save(_draft, MixrConfigPaths.ConfigYamlPath);
            MixrRuntimeState.ReloadConfigFromDisk(Array.Empty<string>());
            _dirty = false;
            UpdateSaveUi();
            AppLog.WriteLine("Fader-Zuordnung gespeichert (config.yaml), Laufzeitkonfiguration neu geladen.");
        }
        catch (Exception ex)
        {
            AppLog.WriteLine("Speichern: " + ex.Message);
            _dirty = true;
            UpdateSaveUi();
            return;
        }

        LoadCatalog();
        RebuildSliderCardsFromDraft();
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
        var cards = new List<SliderCardVm>();
        var store = GameCatalogStore.LoadOrCreate();
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
                    var row = new AssignedProgramRow { SliderIndex = i, Token = s };
                    card.AssignedPrograms.Add(row);
                    TryLoadAssignedCover(row, store);
                }
            }

            card.ShowEmptyHint = card.AssignedPrograms.Count == 0;
            cards.Add(card);
        }

        _sliderCards = cards;
        SetFaderZoneDataContexts(cards);
        RefreshLiveActivity();
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

    void TryLoadAssignedCover(AssignedProgramRow row, GameCatalogStore store)
    {
        row.Cover = null;
        var entry = CatalogGameEntryLookup.FindEntry(store, row.Token);
        string? rel = entry != null
            ? CatalogCoverResolver.ResolveRelativePath(entry)
            : ManualCoverResolver.TryFindRelativePath(
                new CatalogGameEntry { Name = row.Token, AssignmentToken = row.Token });

        if (string.IsNullOrEmpty(rel))
            return;

        var full = GameCatalogPaths.ResolvePath(rel);
        if (!File.Exists(full))
            return;

        _dq.TryEnqueue(() => { _ = LoadAssignedCoverAsync(row, full); });
    }

    async Task LoadAssignedCoverAsync(AssignedProgramRow row, string full)
    {
        var src = await CoverImageLoader.LoadCoverImageSourceAsync(full).ConfigureAwait(true);
        if (src == null)
            return;
        _dq.TryEnqueue(() => row.Cover = src);
    }

    void RefreshLiveActivity()
    {
        if (_sliderCards == null)
            return;

        var snap = MixrRuntimeState.Audio?.GetLiveSnapshot();
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
                card.LiveActivity = "Standard-Wiedergabegerät (Gesamt)";
                continue;
            }

            card.LiveActivity = "Keine passende Session";
        }
    }

    static string HumanizeKey(string key) =>
        key.Length switch
        {
            0 => key,
            1 => char.ToUpperInvariant(key[0]).ToString(),
            _ => char.ToUpperInvariant(key[0]) + key[1..],
        };

    void CatalogItem_DragStarting(object sender, DragStartingEventArgs args)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CatalogGameVm g)
        {
            args.Data.SetText(g.Token);
            args.Data.RequestedOperation = DataPackageOperation.Copy;
        }
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
        if (sender is not Border b)
            return;
        b.RenderTransformOrigin = new Point(0.5, 0.5);
        if (b.RenderTransform is not ScaleTransform)
            b.RenderTransform = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
    }

    void AssignedCover_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border b && b.RenderTransform is ScaleTransform st)
            AnimateCoverScale(st, 1.08);
    }

    void AssignedCover_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border b && b.RenderTransform is ScaleTransform st)
            AnimateCoverScale(st, 1.0);
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

    void RemoveAssigned_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if ((sender as FrameworkElement)?.DataContext is not AssignedProgramRow row)
                return;

            RemoveTokenFromSlider(_draft, row.SliderIndex, row.Token);
            PersistMappingAndSync();
        }
        catch
        {
            /* */
        }
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

        CatalogHintText.Text =
            $"{visible.Count} in der Bibliothek · {assigned.Count} einem Fader zugeordnet · {store.Games.Count} im Katalog · {FormatUtc(store.LastWeeklyCatalogUtc)}";

        _catalogGames.Clear();
        foreach (var g in visible)
        {
            var token = string.IsNullOrEmpty(g.AssignmentToken) ? g.Name : g.AssignmentToken;
            var vm = new CatalogGameVm(g.Name, token);
            _catalogGames.Add(vm);
            TryLoadCover(vm, g);
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

    static string FormatUtc(DateTime utc) =>
        utc == default ? "—" : utc.ToLocalTime().ToString("g");

    void TryLoadCover(CatalogGameVm vm, CatalogGameEntry entry)
    {
        var rel = CatalogCoverResolver.ResolveRelativePath(entry);
        if (string.IsNullOrEmpty(rel))
            return;

        var full = GameCatalogPaths.ResolvePath(rel);
        if (!File.Exists(full))
            return;

        _dq.TryEnqueue(() => { _ = LoadCatalogCoverAsync(vm, full); });
    }

    async Task LoadCatalogCoverAsync(CatalogGameVm vm, string full)
    {
        var src = await CoverImageLoader.LoadCoverImageSourceAsync(full).ConfigureAwait(true);
        if (src == null)
            return;
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

    static void RemoveTokenFromSlider(MixrConfig cfg, int sliderIndex, string token)
    {
        if (sliderIndex < 0 || sliderIndex >= cfg.SliderMapping.Count)
            return;
        var key = cfg.SliderMapping[sliderIndex];
        if (!cfg.SessionGroups.TryGetValue(key, out var list))
            return;
        list.RemoveAll(s => s.Equals(token, StringComparison.OrdinalIgnoreCase));
        if (list.Count == 0)
            cfg.SessionGroups.Remove(key);
    }
}
