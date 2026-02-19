using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace Mixr.Services;

public class MediaService
{
    // Events für das UI / Sende-Modul
    public event Action<string, string, string, string>? OnSongChanged;
    public event Action<byte[]>? OnCoverReady;
    public event Action<double>? OnProgressUpdated; // Neues Event für die Zeit

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private string _lastTitle = "";
    private bool _imageSentForCurrentTrack = false;
    private bool _isProcessingImage = false;
    private bool _isRunning = true;

    public async Task InitializeAsync()
    {
        try 
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            
            _manager.CurrentSessionChanged += (s, e) => 
            {
                LoggerService.Info("Audio-Quelle gewechselt.");
                _ = UpdateCurrentSession();
            };

            await UpdateCurrentSession();

            // Watchdog für Fortschritt und Session-Sicherheit (1 Sekunde Takt)
            _ = Task.Run(async () => 
            {
                while (_isRunning)
                {
                    await Task.Delay(1000);
                    if (_currentSession != null)
                    {
                        await ProcessMedia(_currentSession);
                        ProcessTimeline(_currentSession); // Fortschritt berechnen
                    }
                    else
                    {
                        await UpdateCurrentSession();
                    }
                }
            });
            
            LoggerService.Info("MediaService: Watchdog aktiv.");
        } 
        catch (Exception ex) { LoggerService.Error("Media Init Fehler", ex); }
    }

    private async Task UpdateCurrentSession()
    {
        if (_manager == null) return;

        var newSession = _manager.GetCurrentSession();
        
        if (_currentSession?.SourceAppUserModelId == newSession?.SourceAppUserModelId && newSession != null) 
            return;

        _currentSession = newSession;

        if (_currentSession != null)
        {
            LoggerService.Info($"Neue aktive Session: {_currentSession.SourceAppUserModelId}");
            
            await ProcessMedia(_currentSession);
            ProcessTimeline(_currentSession);

            _currentSession.MediaPropertiesChanged += async (s, e) => await ProcessMedia(s);
            _currentSession.PlaybackInfoChanged += async (s, e) => await ProcessMedia(s);
            _currentSession.TimelinePropertiesChanged += (s, e) => ProcessTimeline(s);
        }
    }

    private void ProcessTimeline(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var timeline = session.GetTimelineProperties();
            if (timeline != null && timeline.EndTime.TotalSeconds > 0)
            {
                // Position durch Gesamtdauer = Fortschritt (0.0 bis 1.0)
                double progress = timeline.Position.TotalSeconds / timeline.EndTime.TotalSeconds;
                
                // Verhindert Werte außerhalb des gültigen Bereichs
                progress = Math.Max(0.0, Math.Min(1.0, progress)); 
                
                OnProgressUpdated?.Invoke(progress);
            }
        }
        catch (Exception)
        {
            // Ignorieren bei Fehlern durch geschlossene Sessions
        }
    }

    private async Task ProcessMedia(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var playbackInfo = session.GetPlaybackInfo();
            if (playbackInfo != null && playbackInfo.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                return; 
            }

            var props = await session.TryGetMediaPropertiesAsync();
            if (props == null || string.IsNullOrEmpty(props.Title)) return;

            bool isNewSong = props.Title != _lastTitle;

            if (isNewSong)
            {
                _lastTitle = props.Title;
                _imageSentForCurrentTrack = false;
                
                LoggerService.Info($"Erkannt: {props.Title} - {props.Artist}");
                OnSongChanged?.Invoke(session.SourceAppUserModelId, props.Title, props.Artist, props.AlbumTitle);
            }

            if (!_imageSentForCurrentTrack && props.Thumbnail != null && !_isProcessingImage)
            {
                try 
                {
                    _isProcessingImage = true;
                    using var stream = await props.Thumbnail.OpenReadAsync();
                    using var memStream = new MemoryStream();
                    await stream.AsStreamForRead().CopyToAsync(memStream);
                    
                    if (memStream.Length > 0)
                    {
                        using var img = Image.FromStream(memStream);
                        using var resized = new Bitmap(img, new Size(120, 120));
                        
                        // Direkte Konvertierung in das vom Display benötigte Format
                        byte[] rgb565Bytes = ConvertToBgr565(resized);

                        LoggerService.Info($"Bild generiert ({rgb565Bytes.Length} Bytes).");
                        OnCoverReady?.Invoke(rgb565Bytes);
                        
                        _imageSentForCurrentTrack = true;
                    }
                }
                finally
                {
                    _isProcessingImage = false;
                }
            }
        }
        catch (Exception)
        {
            _isProcessingImage = false;
        }
    }

    /// <summary>
    /// Konvertiert ein 120x120 Bitmap direkt in ein 28.800 Byte langes BGR565 Array.
    /// Exakt die Logik, die zuvor in Python auf dem Raspberry Pi ausgeführt wurde.
    /// </summary>
    private byte[] ConvertToBgr565(Bitmap img)
    {
        int width = img.Width;
        int height = img.Height;
        byte[] buffer = new byte[width * height * 2];
        
        // LockBits greift direkt auf den RAM zu -> Keine Verzögerung mehr
        BitmapData bmpData = img.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        
        int bytes = Math.Abs(bmpData.Stride) * height;
        byte[] rgbValues = new byte[bytes];
        Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);
        img.UnlockBits(bmpData);
        
        int idx = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Pixel-Position im rohen Byte-Array berechnen
                int p = (y * bmpData.Stride) + (x * 3);
                int b = rgbValues[p];
                int g = rgbValues[p + 1];
                int r = rgbValues[p + 2];
                
                int val = ((b & 0xF8) << 8) | ((g & 0xFC) << 3) | (r >> 3);
                
                buffer[idx++] = (byte)(val >> 8);
                buffer[idx++] = (byte)(val & 0xFF);
            }
        }
        return buffer;
    }
}