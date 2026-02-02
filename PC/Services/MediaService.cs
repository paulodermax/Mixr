using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Mixr.Services;

public class MediaService
{
    public event Action<string, string, string, string>? OnSongChanged;
    public event Action<byte[]>? OnCoverReady;
    
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
            
            // 1. Event: Wenn die Quelle wechselt (z.B. Spotify -> Chrome)
            _manager.CurrentSessionChanged += (s, e) => 
            {
                LoggerService.Info("♻️ Audio-Quelle gewechselt.");
                _ = UpdateCurrentSession();
            };

            // 2. Initiales Setup
            await UpdateCurrentSession();

            // 3. WATCHDOG: Startet eine Hintergrundschleife, die jede Sekunde prüft
            // Das ist der Fix für "Stoppt nach einem Lied"
            _ = Task.Run(async () => 
            {
                while (_isRunning)
                {
                    await Task.Delay(1000); // 1 Sekunde Takt
                    if (_currentSession != null)
                    {
                        await ProcessMedia(_currentSession);
                    }
                    else
                    {
                        // Falls keine Session da war, probieren wir, eine zu finden
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
        
        // Wenn sich nichts geändert hat, raus hier
        if (_currentSession?.SourceAppUserModelId == newSession?.SourceAppUserModelId && newSession != null) 
            return;

        _currentSession = newSession;

        if (_currentSession != null)
        {
            LoggerService.Info($"Neue aktive Session: {_currentSession.SourceAppUserModelId}");
            
            // Sofort prüfen
            await ProcessMedia(_currentSession);

            // Events der neuen Session abonnieren
            _currentSession.MediaPropertiesChanged += async (s, e) => await ProcessMedia(s);
            _currentSession.PlaybackInfoChanged += async (s, e) => await ProcessMedia(s);
        }
    }

    private async Task ProcessMedia(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            // Status prüfen (Paushiert? Spielt?)
            var playbackInfo = session.GetPlaybackInfo();
            if (playbackInfo != null && playbackInfo.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                // Optional: Wenn Pause, nichts tun oder Logs spammen verhindern
                // return; 
            }

            var props = await session.TryGetMediaPropertiesAsync();
            if (props == null) return;

            // Hat sich der Titel geändert?
            bool isNewSong = props.Title != _lastTitle;

            // Sende Text-Update (nur wenn neuer Song oder Debug)
            if (isNewSong)
            {
                // Wenn Titel leer ist (passiert beim Wechseln), ignorieren
                if (string.IsNullOrEmpty(props.Title)) return;

                _lastTitle = props.Title;
                _imageSentForCurrentTrack = false;
                
                LoggerService.Info($"🎵 Erkannt: {props.Title} - {props.Artist}");
                OnSongChanged?.Invoke(session.SourceAppUserModelId, props.Title, props.Artist, props.AlbumTitle);
            }

            // --- BILDER LOGIK ---
            // Wenn wir für diesen Titel noch kein Bild gesendet haben UND ein Thumbnail da ist
            if (!_imageSentForCurrentTrack && props.Thumbnail != null && !_isProcessingImage)
            {
                try 
                {
                // Kurze Wartezeit, da Windows das Thumbnail oft ms später liefert als den Titel
                // Nur relevant, wenn wir nicht im Loop sind, aber schadet nicht.
                // await Task.Delay(500); 
                    _isProcessingImage = true;
                    using var stream = await props.Thumbnail.OpenReadAsync();
                    using var memStream = new MemoryStream();
                    await stream.AsStreamForRead().CopyToAsync(memStream);
                    
                    if (memStream.Length > 0)
                    {
                        using var img = Image.FromStream(memStream);
                        
                        // DEBUG: Speichern
                        //string debugPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cover_debug.jpg");
                        //img.Save(debugPath, ImageFormat.Jpeg);

                        // Resize & Senden
                        using var resized = new Bitmap(img, new Size(170, 170));
                        using var outStream = new MemoryStream();
                        
                        var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                        var parameters = new EncoderParameters(1);
                        parameters.Param[0] = new EncoderParameter(Encoder.Quality, 40L);
                        
                        resized.Save(outStream, encoder, parameters);
                        byte[] imgBytes = outStream.ToArray();

                        LoggerService.Info($"📤 Bild gefunden & gesendet ({imgBytes.Length} Bytes)");
                        OnCoverReady?.Invoke(imgBytes);
                        
                        _imageSentForCurrentTrack = true;
                    }
                }finally
                {
                    // Sperre wieder aufheben (fürs nächste Mal, falls es fehlschlug)
                    // Wenn es erfolgreich war, verhindert _imageSentForCurrentTrack sowieso den erneuten Aufruf.
                    _isProcessingImage = false;
                }
            }
        }
        catch (Exception)
        {
            // Fehler im Loop ignorieren wir leise, um Logs nicht zu fluten
            // (Passiert oft beim Schließen von Apps)
            _isProcessingImage = false;
        }
    }
}