using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Mixr.Models;
using Mixr.Services;
using Mixr_App.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace Mixr_App.Pages;

public sealed class SliderCardVm
{
    public int SliderIndex { get; init; }
    public string SliderKey { get; init; } = "";
    public string Title { get; init; } = "";
    public ObservableCollection<string> AssignedGames { get; } = new();
}

public sealed class CatalogGameVm : INotifyPropertyChanged
{
    public string Name { get; }
    public string Token { get; }

    BitmapImage? _icon;

    public BitmapImage? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value))
                return;
            _icon = value;
            OnPropertyChanged();
        }
    }

    public CatalogGameVm(string name, string token)
    {
        Name = name;
        Token = token;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed partial class SliderMappingPage : Page
{
    readonly DispatcherQueue _dq;
    readonly ObservableCollection<CatalogGameVm> _catalogGames = new();

    public SliderMappingPage()
    {
        InitializeComponent();
        _dq = DispatcherQueue.GetForCurrentThread();
        CatalogGamesList.ItemsSource = _catalogGames;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        GameCatalogCoordinator.CatalogChanged += OnCatalogChanged;
        LoadCatalog();
        RefreshUiFromConfig();
    }

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        GameCatalogCoordinator.CatalogChanged -= OnCatalogChanged;
    }

    void OnCatalogChanged(object? s, EventArgs e) => _dq.TryEnqueue(LoadCatalog);

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadCatalog();
        RefreshUiFromConfig();
    }

    async void ReloadCatalog_Click(object sender, RoutedEventArgs e)
    {
        ReloadCatalogButton.IsEnabled = false;
        try
        {
            await GameCatalogCoordinator.ForceWeeklyRefreshAsync(CancellationToken.None).ConfigureAwait(true);
            LoadCatalog();
        }
        finally
        {
            ReloadCatalogButton.IsEnabled = true;
        }
    }

    void LoadCatalog()
    {
        var store = GameCatalogStore.LoadOrCreate();
        var hint =
            $"Katalog: {store.Games.Count} Einträge · Letzter wöchentlicher Lauf: {FormatUtc(store.LastWeeklyCatalogUtc)} · Täglicher Scan: {FormatUtc(store.LastDailyScanUtc)} · Daten liegen unter {GameCatalogPaths.AppDataRoot}";
        CatalogHintText.Text = hint;

        _catalogGames.Clear();
        foreach (var g in store.Games.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var vm = new CatalogGameVm(g.Name, g.Name);
            _catalogGames.Add(vm);
            TryLoadCover(vm, g);
        }

        WireCatalogDragSources();
    }

    static string FormatUtc(DateTime utc)
    {
        if (utc == default)
            return "—";
        return utc.ToLocalTime().ToString("g");
    }

    void TryLoadCover(CatalogGameVm vm, CatalogGameEntry entry)
    {
        var rel = entry.CoverRelativePath;
        if (string.IsNullOrEmpty(rel))
            return;

        var full = GameCatalogPaths.ResolvePath(rel);
        if (!File.Exists(full))
            return;

        _dq.TryEnqueue(() =>
        {
            try
            {
                vm.Icon = new BitmapImage(new Uri(full));
            }
            catch
            {
                /* */
            }
        });
    }

    void RefreshUiFromConfig()
    {
        var cfg = MixrConfigLoader.Load(Array.Empty<string>());
        var cards = new List<SliderCardVm>();
        for (var i = 0; i < cfg.SliderMapping.Count; i++)
        {
            var key = cfg.SliderMapping[i];
            var card = new SliderCardVm
            {
                SliderIndex = i,
                SliderKey = key,
                Title = $"{i + 1}. {HumanizeKey(key)}",
            };

            if (cfg.SessionGroups.TryGetValue(key, out var list))
            {
                foreach (var s in list.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    card.AssignedGames.Add(s);
            }

            cards.Add(card);
        }

        SliderItems.ItemsSource = cards;
        WireSliderDropTargets();
    }

    static string HumanizeKey(string key) =>
        key.Length switch
        {
            0 => key,
            1 => char.ToUpperInvariant(key[0]).ToString(),
            _ => char.ToUpperInvariant(key[0]) + key[1..],
        };

    void WireCatalogDragSources()
    {
        try
        {
            CatalogGamesList.UpdateLayout();
            foreach (var g in _catalogGames)
            {
                if (CatalogGamesList.ContainerFromItem(g) is not ListViewItem lvi)
                    continue;
                lvi.CanDrag = true;
                lvi.DragStarting -= OnCatalogGameDragStarting;
                lvi.DragStarting += OnCatalogGameDragStarting;
            }
        }
        catch
        {
            /* */
        }
    }

    void WireSliderDropTargets()
    {
        try
        {
            SliderItems.UpdateLayout();
            var borders = new List<Border>();
            CollectSliderDropBorders(SliderItems, borders);
            foreach (var b in borders)
            {
                b.DragOver -= Slider_DragOver;
                b.Drop -= Slider_Drop;
                b.DragOver += Slider_DragOver;
                b.Drop += Slider_Drop;
            }
        }
        catch
        {
            /* */
        }
    }

    static void CollectSliderDropBorders(DependencyObject root, List<Border> acc)
    {
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is Border b && b.AllowDrop && b.DataContext is SliderCardVm)
                acc.Add(b);
            CollectSliderDropBorders(c, acc);
        }
    }

    void Slider_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
    }

    async void Slider_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not SliderCardVm card)
                return;

            if (!e.DataView.Contains(StandardDataFormats.Text))
                return;

            var token = (await e.DataView.GetTextAsync()).Trim();
            if (string.IsNullOrEmpty(token))
                return;

            var cfg = MixrConfigLoader.Load(Array.Empty<string>());
            AssignGameTokenToSlider(cfg, card.SliderIndex, token);
            MixrConfigWriter.Save(cfg, MixrConfigPaths.ConfigYamlPath);
            RefreshUiFromConfig();
        }
        catch
        {
            /* */
        }
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

    void OnCatalogGameDragStarting(object sender, DragStartingEventArgs args)
    {
        if (sender is ListViewItem { DataContext: CatalogGameVm g })
        {
            args.Data.SetText(g.Token);
            args.Data.RequestedOperation = DataPackageOperation.Copy;
        }
    }
}
