using Mixr.Models;

namespace Mixr.Services;

/// <summary>Verbindung zum Gerät, solange der Port offen ist (vom Host gesetzt).</summary>
public sealed record SerialLink(MixrSerialTransport Transport, EspIncomingDispatcher Dispatcher);

/// <summary>Thread-sichere aktuelle Konfiguration + Laufzeitzustand (Verbindung, Gerät, Audio) für die UI.</summary>
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

    // ---- Gerät / Firmware -------------------------------------------------------------------

    static readonly object _deviceLock = new();
    static DeviceHello? _device;
    static SerialLink? _link;
    static string? _portName;

    /// <summary>Letztes HELLO des Geräts; <c>null</c> bei alter Firmware (Protokoll v1) oder ohne Verbindung.</summary>
    public static DeviceHello? Device
    {
        get
        {
            lock (_deviceLock)
                return _device;
        }
    }

    public static SerialLink? Link
    {
        get
        {
            lock (_deviceLock)
                return _link;
        }
    }

    /// <summary>COM-Port der letzten (oder aktuellen) Verbindung — für den esptool-Fallback.</summary>
    public static string? LastPortName
    {
        get
        {
            lock (_deviceLock)
                return _portName;
        }
    }

    public static event Action? DeviceChanged;

    public static void SetDevice(DeviceHello? hello)
    {
        lock (_deviceLock)
        {
            if (Equals(_device, hello))
                return;
            _device = hello;
        }

        DeviceChanged?.Invoke();
    }

    public static void SetLink(SerialLink? link, string? portName)
    {
        lock (_deviceLock)
        {
            _link = link;
            if (portName != null)
                _portName = portName;
        }
    }

    // ---- Serial-Pause (Port für esptool freigeben) ----------------------------------------------

    static readonly object _pauseLock = new();
    static int _pauseCount;
    static TaskCompletionSource _resumeTcs = CreateCompleted();

    /// <summary>Der Host schließt den Port sofort, wenn dieses Event feuert.</summary>
    public static event Action? SerialPauseRequested;

    public static bool SerialPaused
    {
        get
        {
            lock (_pauseLock)
                return _pauseCount > 0;
        }
    }

    /// <summary>Solange das zurückgegebene Objekt lebt, verbindet der Host nicht neu und hält den Port geschlossen.</summary>
    public static IDisposable PauseSerial()
    {
        lock (_pauseLock)
        {
            if (_pauseCount++ == 0)
                _resumeTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        SerialPauseRequested?.Invoke();
        return new PauseToken();
    }

    public static Task WaitSerialResumedAsync(CancellationToken ct)
    {
        Task t;
        lock (_pauseLock)
            t = _resumeTcs.Task;
        return t.WaitAsync(ct);
    }

    static TaskCompletionSource CreateCompleted()
    {
        var tcs = new TaskCompletionSource();
        tcs.SetResult();
        return tcs;
    }

    sealed class PauseToken : IDisposable
    {
        bool _done;

        public void Dispose()
        {
            if (_done)
                return;
            _done = true;
            lock (_pauseLock)
            {
                if (--_pauseCount == 0)
                    _resumeTcs.TrySetResult();
            }
        }
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
