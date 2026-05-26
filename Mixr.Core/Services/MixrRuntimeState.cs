using Mixr.Models;

namespace Mixr.Services;

/// <summary>Thread-sichere aktuelle Konfiguration + optionaler Audio-Dienst für die UI.</summary>
public static class MixrRuntimeState
{
    static readonly MixrRuntimeConfigHolder Holder = new();

    public static MixrRuntimeConfigHolder Config => Holder;

    /// <summary>Wird nach jedem Replace gesetzt (MixrHost).</summary>
    public static AudioService? Audio { get; set; }

    static volatile bool _espConnected;

    public static bool EspConnected => _espConnected;

    public static event Action? EspConnectionChanged;

    static readonly object _sliderLock = new();
    static float[] _sliderLevels = [-1, -1, -1, -1];

    public static event Action? SliderLevelsChanged;

    public static float[] GetSliderLevelsSnapshot()
    {
        lock (_sliderLock)
            return (float[])_sliderLevels.Clone();
    }

    public static void SetSliderLevels(ReadOnlySpan<float> levels)
    {
        lock (_sliderLock)
        {
            var changed = false;
            for (var i = 0; i < 4 && i < levels.Length; i++)
            {
                if (Math.Abs(_sliderLevels[i] - levels[i]) > 0.002f)
                    changed = true;
                _sliderLevels[i] = levels[i];
            }

            if (!changed)
                return;
        }

        SliderLevelsChanged?.Invoke();
    }

    public static void SetEspConnected(bool connected)
    {
        if (_espConnected == connected)
            return;
        _espConnected = connected;
        EspConnectionChanged?.Invoke();
    }

    /// <summary>Lädt config.yaml neu und wendet sie an (Host + UI nach Speichern).</summary>
    public static void ReloadConfigFromDisk(string[]? args = null)
    {
        args ??= Array.Empty<string>();
        var cfg = MixrConfigLoader.Load(args);
        Holder.Replace(cfg);
    }

    public sealed class MixrRuntimeConfigHolder
    {
        readonly object _lock = new();
        MixrConfig _current = new();

        public event Action? Changed;

        public MixrConfig Current
        {
            get
            {
                lock (_lock)
                    return _current;
            }
        }

        public void Replace(MixrConfig next)
        {
            lock (_lock)
                _current = next;
            Changed?.Invoke();
        }
    }
}
