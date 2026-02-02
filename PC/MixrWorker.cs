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
    private string _lastMsg = "";
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

            // 2. Audio Map bauen
            Console.WriteLine("Initialisiere Audio...");
            _audio.RebuildSessionMap(cfg.slider_mapping, cfg.session_groups);
            Console.WriteLine("Audio Initialisiert.");

            // 3. Watcher starten
            _watcher.SetWhitelist(cfg.whitelist);
            _watcher.OnWhitelistChanged += () => {
                Console.WriteLine("Prozess-Änderung erkannt -> Audio Rebuild");
                _audio.RebuildSessionMap(cfg.slider_mapping, cfg.session_groups);
            };
            _watcher.Start();

            // 4. Serial Events
            _serial.OnDataReceived += HandleInput;
            /*_serial.OnDataReceived += (data) => 
            {
                // Trim() entfernt Leerzeichen am Anfang/Ende, falls welche da sind
                string cleanData = data.Trim();
                
                // Einfach stumpf ins Log schreiben
                LoggerService.Info($"🎛️ Input: {cleanData}");
            };*/
            // 5. Media Service
            Console.WriteLine("Starte Media Service...");
            await _media.InitializeAsync();
            _media.OnSongChanged += (a, t, ar, al) => 
            {
                string msg = $"sp|{a}|{t}|{ar}|{al}\n";
                _lastMsg = msg; // <--- Merken für später!
                _serial.Send(msg);
            };
            _media.OnCoverReady += async (b) => 
            {
                // 1. Bild senden
                _serial.SendImage(b); 

                // 2. Sicherheits-Pause (damit der Pi Zeit hat das Bild zu speichern)
                await Task.Delay(1000); 

                // 3. Text nochmal hinterherschicken (falls er vorhanden ist)
                if (!string.IsNullOrEmpty(_lastMsg))
                {
                    _serial.Send(_lastMsg);
                    // Console.WriteLine("Sicherheits-Update gesendet."); // Optional fürs Debugging
                }
            };
            Console.WriteLine("Media Service läuft.");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("--- ALLES BEREIT - WARTE AUF ARDUINO ---");
            Console.ResetColor();

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_serial.IsOpen) 
                {
                    Console.Write("."); // Herzschlag in der Konsole
                    if (_serial.Connect(cfg.com_port, cfg.baud_rate))
                    {
                        Console.WriteLine($"\nVerbunden mit {cfg.com_port}!");
                    }
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

    private void HandleInput(string line)
    {
        // Debugging für Serial Input (Optional aktivieren)
        // Console.WriteLine($"RX: {line}"); 

        if (line.StartsWith("IMG_OK") || !line.Contains('|')) return;
        var parts = line.Split('|');
        for (int i = 0; i < parts.Length && i < _config.Current.slider_mapping.Count; i++)
        {
            if (!int.TryParse(parts[i], out int val)) continue;
            float level = val / 1023f;
            if (_config.Current.invert_sliders) level = 1f - level;
            if (Math.Abs(level - _lastLevels[i]) > 0.005f)
            {
                _lastLevels[i] = level;
                _audio.SetVolume(_config.Current.slider_mapping[i], level);
            }
        }
    }
}