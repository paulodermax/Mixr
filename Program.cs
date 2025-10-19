using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioSwitcher.AudioApi.CoreAudio;
using AudioSwitcher.AudioApi.Session;
using Windows.Media.Control;
using Windows.Storage.Streams;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System.Drawing;
using System.Drawing.Imaging;


namespace Mixr
{
    class Program
    {
        static SerialPort serialPort = null!;
        static CoreAudioController audioController = new();
        static AppConfig config = new();

        static Dictionary<string, IAudioSession> sessionMap = new();
        static List<float> lastLevels = new();

        private static GlobalSystemMediaTransportControlsSessionManager sessionManager = null!;
        private static readonly Dictionary<string, GlobalSystemMediaTransportControlsSession> registeredSessions = new();
        private static readonly Dictionary<string, string> lastKnownTitles = new();

        private static bool songinfo = false;
        static void Log(string message)
        {
            try
            {
                string logEntry = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
                File.AppendAllText("log.txt", logEntry);
            }
            catch { /* Falls Logging fehlschlägt, ignoriere */ }
        }

        static async Task Main()
        {
            Console.WriteLine("Starte Mixr...");
            Log("Start");
            if (!LoadConfig("./config.yaml"))
            {
                Console.WriteLine("Unable to load config. Ending Program.");
                Log("Unable to load config. Ending Program.");
                return;
            }

            BuildSessionMap();
            lastLevels = Enumerable.Repeat(-1f, config.slider_mapping.Count).ToList();

            if (!OpenSerialPort(config.com_port, config.baud_rate))
            {
                Console.WriteLine("Seriellen Port "+config.com_port+" konnte nicht geöffnet werden.");
                Log("Seriellen Port " + config.com_port + " konnte nicht geöffnet werden.");
                await Task.Delay(30000);
                while (true)
                {
                    Log("Retrying to open Serial Port...");
                    if (OpenSerialPort(config.com_port, config.baud_rate))
                    {
                        Log("Opened COM-Port successfully.");
                        break;
                    }
                    else
                    {
                        Log("Serial Port " + config.com_port + " couldn't be opened.");
                    }
                    await Task.Delay(30000);
                }
            }

            sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            sessionManager.SessionsChanged += OnSessionsChanged;

            foreach (var session in sessionManager.GetSessions())
            {
                RegisterSession(session);
            }
            
            serialPort.DataReceived += SerialPort_DataReceived;

            ManualResetEvent quitEvent = new(false);
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                quitEvent.Set();
            };

            Console.WriteLine("Reading from SerialPort. Press Ctrl+C to quit.");
            Log("Reading from SerialPort..");
            quitEvent.WaitOne();

            Console.WriteLine("Exit...");
            serialPort.Close();
        }

        static bool LoadConfig(string path)
        {
            if (!File.Exists(path)) return false;

            try
            {
                var yaml = File.ReadAllText(path);
                var deserializer = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
                config = deserializer.Deserialize<AppConfig>(yaml);

                Console.WriteLine("Config geladen:");
                Console.WriteLine($"  Baudrate: {config.baud_rate}");
                Console.WriteLine($"  Invert Sliders: {config.invert_sliders}");
                Console.WriteLine($"  Noise Reduction: {config.noise_reduction}");
                Console.WriteLine($"  Slider Mapping: {config.slider_mapping.Count} Einträge");
                Log("Config geladen:");
                Log($"  - Baudrate: {config.baud_rate}");
                Log($"  - Invert Sliders: {config.invert_sliders}");
                Log($"  - Noise Reduction: {config.noise_reduction}");
                Log($"  - Slider Mapping: {config.slider_mapping.Count} Einträge");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fehler beim Laden der Config: {ex.Message}");
                Log($"Fehler beim Laden der Config: {ex.Message}");
                return false;
            }
        }

        static void BuildSessionMap()
        {
            sessionMap.Clear();
            var sessions = audioController.DefaultPlaybackDevice.SessionController.ActiveSessions();

            foreach (var target in config.slider_mapping)
            {
                var match = sessions.FirstOrDefault(s =>
                    !string.IsNullOrEmpty(s.DisplayName) &&
                    s.DisplayName.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0);

                if (match != null)
                {
                    sessionMap[target] = match;
                    Console.WriteLine($"Mapped '{target}' → Session '{match.DisplayName}'");
                }
                else
                {
                    Console.WriteLine($"Keine Session gefunden für '{target}'");
                }
            }
        }

        static bool OpenSerialPort(string portName, int baudRate)
        {
            try
            {
                serialPort = new SerialPort(portName, baudRate);
                serialPort.Open();
                Console.WriteLine($"Serieller Port {portName} geöffnet mit {baudRate} Baud.");
                Log($"Serieller Port {portName} geöffnet mit {baudRate} Baud.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fehler beim Öffnen des Seriellen Ports {portName}: {ex.Message}");
                Log($"Fehler beim Öffnen des Seriellen Ports {portName}: {ex.Message}");
                return false;
            }
        }

        static void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string line = serialPort.ReadLine().Trim();
                Console.WriteLine($"(PC)Empfangen: {line}");
                Log($"{line}");
                if (!line.Contains('|') || line.StartsWith("IMG_OK") || line.StartsWith("TXT_OK"))
                return;
                string[] parts = line.Split('|');

                for (int i = 0; i < parts.Length && i < config.slider_mapping.Count; i++)
                {
                    if (!int.TryParse(parts[i], out int rawValue)) continue;

                    float normalizedLevel = rawValue / 1023f;
                    normalizedLevel = Math.Clamp(normalizedLevel, 0f, 1f);
                    if (Math.Abs(normalizedLevel - lastLevels[i]) < 0.01f) continue;

                    lastLevels[i] = normalizedLevel;
                    string target = config.slider_mapping[i];
                    SetVolume(target, normalizedLevel);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fehler beim Verarbeiten der seriellen Daten: {ex.Message}");
                Log($"Fehler beim Verarbeiten der seriellen Daten: {ex.Message}");
            }
        }

        static void SetVolume(string target, float level)
        {
            int volume = (int)(level * 100);

            try
            {
                if (target.Equals("master", StringComparison.OrdinalIgnoreCase))
                {
                    audioController.DefaultPlaybackDevice.Volume = volume;
                    Console.WriteLine($"🔊 Master-Volume auf {volume}% gesetzt.");
                    return;
                }

                if (!sessionMap.TryGetValue(target, out var session))
                {
                    var sessions = audioController.DefaultPlaybackDevice.SessionController.ActiveSessions();
                    session = sessions.FirstOrDefault(s =>
                        !string.IsNullOrEmpty(s.DisplayName) &&
                        s.DisplayName.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0);

                    if (session != null)
                        sessionMap[target] = session;
                }

                if (session != null)
                {
                    session.Volume = volume;
                    Console.WriteLine($"🎚 {session.DisplayName}: Lautstärke auf {volume}% gesetzt.");
                }
                else
                {
                    Console.WriteLine($"⚠️ Keine Session gefunden für '{target}', Lautstärke nicht geändert.");
                }
            }
            catch (Exception) { }
        }

        private static void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, object args)
        {
            Console.WriteLine("🔄 MediaSessions geändert.");
            foreach (var session in sender.GetSessions())
            {
                RegisterSession(session);
            }
        }

        private static void RegisterSession(GlobalSystemMediaTransportControlsSession session)
        {
            string appId = session.SourceAppUserModelId;
            if (registeredSessions.ContainsKey(appId)) return;

            Console.WriteLine($"📡 Neue MediaSession registriert: {appId}");
            registeredSessions[appId] = session;

            session.PlaybackInfoChanged += async (s, e) => await OnPlaybackInfoChangedAsync(s);
            session.MediaPropertiesChanged += async (s, e) => await OnMediaPropertiesChangedAsync(s);
        }

        private static async Task OnPlaybackInfoChangedAsync(GlobalSystemMediaTransportControlsSession session)
        {
            var status = session.GetPlaybackInfo().PlaybackStatus;
            string appId = session.SourceAppUserModelId;
            Console.WriteLine($"▶️ Wiedergabestatus ({appId}): {status}");
        }

        private static async Task OnMediaPropertiesChangedAsync(GlobalSystemMediaTransportControlsSession session)
        {
            string appId = session.SourceAppUserModelId;
            var props = await session.TryGetMediaPropertiesAsync();
            string title = props.Title;
            string artist = props.Artist;
            string album = props.AlbumTitle;

            string lastTitle = lastKnownTitles.ContainsKey(appId) ? lastKnownTitles[appId] : "";
            if (title != lastTitle&&title!="")
            {
                lastKnownTitles[appId] = title;
                Console.WriteLine($"\n🎵 Neue Wiedergabe ({appId}):");
                Console.WriteLine($"   ▶ Titel:    {title}");
                Console.WriteLine($"   🎤 Künstler: {artist}");
                Console.WriteLine($"   💿 Album:    {album}");
                Log($"Change in {appId}: {title} by {artist} ({album})");

                if (songinfo)
                {
                    serialPort.Write($"sp|{appId}|{title}|{artist}|{album}\n");
                }

                if (props.Thumbnail != null && songinfo)
                {
                    try
                    {
                        using var stream = await props.Thumbnail.OpenReadAsync();
                        using var memStream = new MemoryStream();
                        await stream.AsStreamForRead().CopyToAsync(memStream);
                        memStream.Position = 0;

                        using var img = Image.FromStream(memStream);
                        using var resized = new Bitmap(img, new Size(170, 170));

                        var encoder = ImageCodecInfo.GetImageEncoders()
                            .First(c => c.FormatID == ImageFormat.Jpeg.Guid);

                        var encoderParams = new EncoderParameters(1);
                        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 40L);

                        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                        string coverPath = Path.Combine(exeDir, "cover.jpg");
                        Log("Path:" + coverPath);
                        Log("exeDir"+exeDir);
                        resized.Save(coverPath, encoder, encoderParams);


                        Console.WriteLine("Komprimiertes Cover gespeichert.");
                        Log("Komprimiertes Cover gespeichert.");
                        SendImageOverSerial("cover.jpg");
                        Console.WriteLine("   🖼️ Komprimiertes Cover geschickt.");
                        Log("Komprimiertes Cover geschickt.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Fehler beim Verarbeiten des Covers: {ex.Message}");
                        Log($"⚠️ Fehler beim Verarbeiten des Covers: {ex.Message}");
                    }
                }

                Console.WriteLine();
            }
        }
        static void SendImageOverSerial(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Console.WriteLine("❌ Datei nicht gefunden: " + path);
                    Log("❌ Datei nicht gefunden: " + path);
                    return;
                }

                byte[] imageBytes = File.ReadAllBytes(path);
                Console.WriteLine($"📤 Sende Bild ({imageBytes.Length} Bytes)...");

                serialPort.Write("<IMG>");

                int chunkSize = 512;
                for (int i = 0; i < imageBytes.Length; i += chunkSize)
                {
                    int remaining = Math.Min(chunkSize, imageBytes.Length - i);
                    serialPort.Write(imageBytes, i, remaining);
                    Thread.Sleep(2);
                }

                serialPort.Write("<END>");
                Console.WriteLine("✅ Bild gesendet.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Fehler beim Bildsenden: {ex.Message}");
            }
        }

    }
}
