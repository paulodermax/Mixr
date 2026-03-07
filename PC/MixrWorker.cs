using Microsoft.Extensions.Hosting;
using Mixr.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mixr;

public class MixrWorker : BackgroundService
{
    private readonly ConfigService _config;
    private readonly SerialService _serial;
    private readonly AudioService _audio;
    private readonly MediaService _media;
    private readonly ProcessWatcher _watcher;
    private List<float> _lastLevels = new();
    public MixrWorker(ConfigService config, SerialService serial, AudioService audio, MediaService media, ProcessWatcher watcher)
    {
        _config = config; _serial = serial; _audio = audio; _media = media; _watcher = watcher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("--- WORKER GESTARTET ---");
        
        try 
        {
            // 1. Config Pfad prüfen
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string configPath = Path.Combine(basePath, "config.yaml");
            
            Console.WriteLine($"Suche Config hier: {configPath}");

            if (!File.Exists(configPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("!!! FEHLER: config.yaml nicht gefunden !!!");
                Console.WriteLine("Bitte stelle sicher, dass die Datei im selben Ordner wie die .exe liegt.");
                return;
            }

            if (!_config.Load(configPath)) 
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("!!! FEHLER: Konnte config.yaml nicht laden (Format-Fehler?) !!!");
                return;
            }
            Console.WriteLine("Config geladen.");

            var cfg = _config.Current;
            _lastLevels = Enumerable.Repeat(-1f, cfg.slider_mapping.Count).ToList();

            // 1,5. Whitelist-Management
            var autoWhitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (cfg.slider_mapping != null)
            {
                foreach (var item in cfg.slider_mapping)
                {
                    autoWhitelist.Add(item);
                    // Automatisch auch die .exe Variante hinzufügen, falls nicht vorhanden
                    if (!item.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        autoWhitelist.Add(item + ".exe");
                    }
                }
            } 
            if (cfg.session_groups != null)
                    {
                        foreach (var group in cfg.session_groups)
                        {
                            foreach (var item in group.Value)
                            {
                                autoWhitelist.Add(item);
                                // Auch hier die .exe Variante sicherheitshalber dazu
                                if (!item.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    autoWhitelist.Add(item + ".exe");
                                }
                            }
                        }
                    }


            // 2. Audio Map bauen
            Console.WriteLine("Initialisiere Audio...");
            _audio.RebuildSessionMap(cfg.slider_mapping ?? new(), cfg.session_groups ?? new());
            Console.WriteLine("Audio Initialisiert.");

            // 3. Watcher starten
            _watcher.SetWhitelist(autoWhitelist.ToList());
            _watcher.OnWhitelistChanged += () => {
                Console.WriteLine("Prozess-Änderung erkannt -> Audio Rebuild");
                _audio.RebuildSessionMap(cfg.slider_mapping ?? new(), cfg.session_groups ?? new());
            };
            _watcher.Start();

            // 4. Serial Events
            _serial.OnSliderDataReceived += HandleSliderInput;
            /*_serial.OnDataReceived += (data) => 
            {
                // Trim() entfernt Leerzeichen am Anfang/Ende, falls welche da sind
                string cleanData = data.Trim();
                
                // Einfach stumpf ins Log schreiben
                LoggerService.Info($"🎛️ Input: {cleanData}");
            };*/
            // 5. Media Service
            _media.OnSongChanged += (app, title, artist, album) => 
            {
                _serial.SendSongData(title ?? "", artist ?? "");
            };
            
            _media.OnCoverReady += (imageBytes) => 
            {
                Task.Run(() => _serial.SendImageInChunks(imageBytes)); 
            };

            // --- DAS HIER HAT GEFEHLT: ---
            Console.WriteLine("Starte Media Service...");
            await _media.InitializeAsync(); 
            // -----------------------------

            Console.WriteLine("Media Service läuft.");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("--- Waiting for Pi");
            Console.ResetColor();
            
            int rebuildCycle = 0;
            int logCycle = 0;
            while (!stoppingToken.IsCancellationRequested)
            {

                if (!_serial.IsOpen) 
                {
                    Console.Write(".");
                    if (_serial.Connect(cfg.com_port, cfg.baud_rate))
                    {
                        Console.WriteLine($"\nVerbunden mit {cfg.com_port}!");
                    }
                }
                rebuildCycle++;
                if(rebuildCycle>=1)
                {
                    rebuildCycle = 0;
                    _audio.RebuildSessionMap(cfg.slider_mapping,cfg.session_groups);
                }
                logCycle++;
                if(logCycle>=5)
                {
                    logCycle = 0;
                    _audio.PrintDebugMappings();
                }
                await Task.Delay(2000, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n!!! KRITISCHER ABSTURZ: {ex.Message} !!!");
            Console.WriteLine(ex.StackTrace);
        }
    }

    private void HandleSliderInput(byte[] sliders)
    {
        // Wir iterieren über die empfangenen Bytes (Werte 0-255)
        for (int i = 0; i < sliders.Length && i < _config.Current.slider_mapping.Count; i++)
        {
            // Umrechnung von 0-255 (ESP32) auf 0.0 - 1.0 (Windows Audio API)
            float level = sliders[i] / 255f; 
            
            if (_config.Current.invert_sliders) level = 1f - level;
            
            // Hysterese: Nur ändern, wenn der Regler physisch spürbar bewegt wurde
            if (Math.Abs(level - _lastLevels[i]) > 0.005f)
            {
                _lastLevels[i] = level;
                _audio.SetVolume(_config.Current.slider_mapping[i], level);
            }
        }
    }
}