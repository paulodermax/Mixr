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
            
            // Event: Wenn Windows die primäre App wechselt (z.B. Spotify -> Chrome)
            _manager.CurrentSessionChanged += async (s, e) => 
            {
                LoggerService.Info("Media-Fokus hat gewechselt.");
                await UpdateCurrentSession();
            };

            await UpdateCurrentSession();

            // Der Watchdog prüft nun alle 2 Sekunden, ob wir die Session verloren haben
            _ = Task.Run(async () => 
            {
                while (_isRunning)
                {
                    await Task.Delay(2000);
                    try 
                    {
                        var session = _manager.GetCurrentSession();
                        if (session == null) continue;

                        // Falls die Session-ID sich geändert hat, ohne dass das Event gefeuert wurde
                        if (_currentSession == null || _currentSession.SourceAppUserModelId != session.SourceAppUserModelId)
                        {
                            await UpdateCurrentSession();
                        }
                        
                        // WICHTIG: Erzwungener Check auf Song-Daten (hilft bei hängenden Events)
                        await ProcessMedia(session);
                    }
                    catch (Exception ex) { LoggerService.Error("Watchdog Fehler", ex); }
                }
            });
        } 
        catch (Exception ex) { LoggerService.Error("Media Init Fehler", ex); }
    }

    private async Task UpdateCurrentSession()
    {
        var newSession = _manager?.GetCurrentSession();
        if (newSession == null) return;

        // Wir speichern die neue Session stabil
        _currentSession = newSession;
        LoggerService.Info($"Neue Session aktiv: {_currentSession.SourceAppUserModelId}");

        // Events binden
        _currentSession.MediaPropertiesChanged += async (s, e) => {
            LoggerService.Info("Windows meldet: Song-Eigenschaften geändert.");
            await ProcessMedia(s);
        };

        // Initialen Stand sofort abgreifen
        await ProcessMedia(_currentSession);
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
            // 1. Hole die PlaybackInfo (nur einmal deklarieren!)
            var playbackInfo = session.GetPlaybackInfo();
            
            // Prüfe, ob überhaupt etwas abgespielt wird
            if (playbackInfo == null || playbackInfo.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                return; 
            }

            // 2. Hole die Metadaten
            var props = await session.TryGetMediaPropertiesAsync();
            if (props == null || string.IsNullOrEmpty(props.Title)) return;

            // Prüfe auf Songwechsel
            bool isNewSong = props.Title != _lastTitle;

            if (isNewSong)
            {
                _lastTitle = props.Title;
                _imageSentForCurrentTrack = false;
                
                LoggerService.Info($"Wechsel erkannt: {props.Title} - {props.Artist}");
                OnSongChanged?.Invoke(session.SourceAppUserModelId, props.Title, props.Artist, props.AlbumTitle);
            }

            // 3. Bildverarbeitung (nur wenn noch nicht gesendet)
            if (!_imageSentForCurrentTrack && props.Thumbnail != null && !_isProcessingImage)
            {
                _isProcessingImage = true;
                try 
                {
                    using var stream = await props.Thumbnail.OpenReadAsync();
                    using var memStream = new MemoryStream();
                    await stream.AsStreamForRead().CopyToAsync(memStream);
                    
                    if (memStream.Length > 0)
                    {
                        using var img = Image.FromStream(memStream);
                        using var resized = new Bitmap(img, new Size(240, 240));
                        
                        byte[] rgb565Bytes = ConvertToRgb565(resized);
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
        catch (Exception ex)
        {
            LoggerService.Error("Fehler in ProcessMedia", ex);
        }
    }

    private byte[] ConvertToRgb565(Bitmap img)
    {
        int width = img.Width;
        int height = img.Height;
        byte[] buffer = new byte[width * height * 2]; // 115.200 Bytes
        
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
                int p = (y * bmpData.Stride) + (x * 3);
                int b = rgbValues[p];
                int g = rgbValues[p + 1];
                int r = rgbValues[p + 2];
                
                // RGB565 Berechnung
                int val = ((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3);
                
                // Little-Endian (High Byte, dann Low Byte)
                buffer[idx++] = (byte)(val >> 8);
                buffer[idx++] = (byte)(val & 0xFF);
            }
        }
        return buffer;
    }
}