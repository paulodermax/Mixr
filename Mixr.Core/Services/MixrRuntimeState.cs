using Mixr.Models;

namespace Mixr.Services;

/// <summary>Thread-sichere aktuelle Konfiguration + optionaler Audio-Dienst für die UI.</summary>
public static class MixrRuntimeState
{
    static readonly MixrRuntimeConfigHolder Holder = new();

    public static MixrRuntimeConfigHolder Config => Holder;

    /// <summary>Wird nach jedem Replace gesetzt (MixrHost).</summary>
    public static AudioService? Audio { get; set; }

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
