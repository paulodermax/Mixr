using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;

namespace Mixr_App.Pages;

/// <summary>Ein Eintrag aus der Spiele-/Programm-Bibliothek (Katalog).</summary>
public sealed class CatalogGameVm : INotifyPropertyChanged
{
    public string Name { get; }
    public string Token { get; }

    ImageSource? _icon;

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value))
                return;
            _icon = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCover));
        }
    }

    public bool HasCover => Icon != null;

    public CatalogGameVm(string name, string token)
    {
        Name = name;
        Token = token;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Eine Zeile „zugeordnetes Programm“ auf einer Fader-Karte (nur Cover in der UI).</summary>
public sealed class AssignedProgramRow : INotifyPropertyChanged
{
    public int SliderIndex { get; init; }

    /// <summary>Suchstring in session_groups (wie in config.yaml).</summary>
    public string Token { get; init; } = "";

    /// <summary>Anzeigename für Tooltip (Katalog-Name oder Token).</summary>
    public string DisplayName { get; set; } = "";

    public string TooltipText =>
        string.IsNullOrWhiteSpace(DisplayName) ? Token : DisplayName;

    ImageSource? _cover;

    public ImageSource? Cover
    {
        get => _cover;
        set
        {
            if (ReferenceEquals(_cover, value))
                return;
            _cover = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCover));
        }
    }

    public bool HasCover => Cover != null;

    public event PropertyChangedEventHandler? PropertyChanged;

    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>VM für eine Fader-Karte (Slider 1–4).</summary>
public sealed class SliderCardVm : INotifyPropertyChanged
{
    int _sliderIndex;
    string _sliderKey = "";
    string _title = "";

    public int SliderIndex
    {
        get => _sliderIndex;
        set
        {
            if (_sliderIndex == value)
                return;
            _sliderIndex = value;
            OnPropertyChanged();
        }
    }

    public string SliderKey
    {
        get => _sliderKey;
        set
        {
            if (_sliderKey == value)
                return;
            _sliderKey = value;
            OnPropertyChanged();
        }
    }

    public string Title
    {
        get => _title;
        set
        {
            if (_title == value)
                return;
            _title = value;
            OnPropertyChanged();
        }
    }
    public string Subtitle { get; init; } = "";
    public bool ShowEmptyHint { get; set; }
    public ObservableCollection<AssignedProgramRow> AssignedPrograms { get; } = new();

    string _liveActivity = "";

    /// <summary>Aktive Windows-Audio-Sessions zu dieser Gruppe (laut letztem Host-Scan).</summary>
    public string LiveActivity
    {
        get => _liveActivity;
        set
        {
            if (_liveActivity == value)
                return;
            _liveActivity = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
