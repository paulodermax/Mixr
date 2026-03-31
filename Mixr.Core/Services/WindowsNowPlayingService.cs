using System.Drawing;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Mixr.Services;

/// <summary>
/// Windows „Systemsteuerung für Medien“ (SMTC): aktuelle Wiedergabe-Session, Titel, Interpret, Cover.
/// Entspricht dem Session-Teil aus workdir (MediaService), ohne Audio/Slider-Logik.
/// </summary>
public sealed class WindowsNowPlayingService : IDisposable
{
    GlobalSystemMediaTransportControlsSessionManager? _manager;
    GlobalSystemMediaTransportControlsSession? _session;
    readonly SemaphoreSlim _pushGate = new(1, 1);

    /// <summary>Titel, Artist, 240×240 RGB565 LE (seriell, Standard 921600 Baud).</summary>
    public event Action<string, string, byte[]>? SessionUpdated;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellationToken);
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, object args)
    {
        _ = RefreshAsync(CancellationToken.None);
    }

    async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_manager == null)
            return;

        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session = null;
        }

        _session = _manager.GetCurrentSession();
        if (_session == null)
            return;

        _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        await PushNowPlayingAsync(cancellationToken).ConfigureAwait(false);
    }

    void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        _ = PushNowPlayingAsync(CancellationToken.None);
    }

    void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        _ = PushNowPlayingAsync(CancellationToken.None);
    }

    async Task PushNowPlayingAsync(CancellationToken cancellationToken)
    {
        if (_session == null)
            return;

        await _pushGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var props = await _session.TryGetMediaPropertiesAsync().AsTask(cancellationToken).ConfigureAwait(false);
            if (props == null)
                return;

            string title = props.Title ?? "";
            string artist = props.Artist ?? "";
            byte[] cover;

            if (props.Thumbnail != null)
            {
                using var ras = await props.Thumbnail.OpenReadAsync().AsTask(cancellationToken).ConfigureAwait(false);
                var bytes = await ReadRandomAccessStreamToBytesAsync(ras).ConfigureAwait(false);
                if (bytes.Length == 0)
                {
                    cover = Rgb565Converter.GrayPlaceholder();
                }
                else
                {
                    using var ms = new MemoryStream(bytes);
                    using var bmp = new Bitmap(ms);
                    cover = Rgb565Converter.ConvertBitmapToRgb565(bmp);
                }
            }
            else
            {
                cover = Rgb565Converter.GrayPlaceholder();
            }

            SessionUpdated?.Invoke(title, artist, cover);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SMTC: {ex.Message}");
        }
        finally
        {
            _pushGate.Release();
        }
    }

    /// <summary>Windows-SMTC: Next / Play-Pause / Previous (Menü oder später Hardware-Tasten).</summary>
    public async Task ExecuteMediaCommandAsync(byte subcmd, CancellationToken cancellationToken = default)
    {
        if (_manager == null)
            return;

        GlobalSystemMediaTransportControlsSession? session;
        try
        {
            session = _manager.GetCurrentSession();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SMTC Session: {ex.Message}");
            return;
        }

        if (session == null)
            return;

        try
        {
            switch (subcmd)
            {
                case 0:
                    await session.TrySkipNextAsync().AsTask(cancellationToken).ConfigureAwait(false);
                    break;
                case 1:
                    await session.TryTogglePlayPauseAsync().AsTask(cancellationToken).ConfigureAwait(false);
                    break;
                case 2:
                    await session.TrySkipPreviousAsync().AsTask(cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Medien-Befehl ({subcmd}): {ex.Message}");
        }
    }

    static async Task<byte[]> ReadRandomAccessStreamToBytesAsync(IRandomAccessStream ras)
    {
        ulong size64 = ras.Size;
        if (size64 == 0 || size64 > int.MaxValue)
            return Array.Empty<byte>();

        uint size = (uint)size64;
        var reader = new DataReader(ras);
        try
        {
            await reader.LoadAsync(size).AsTask().ConfigureAwait(false);
            var buf = new byte[size];
            reader.ReadBytes(buf);
            return buf;
        }
        finally
        {
            reader.DetachStream();
        }
    }

    public void Dispose()
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session = null;
        }

        if (_manager != null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager = null;
        }

        _pushGate.Dispose();
    }
}
