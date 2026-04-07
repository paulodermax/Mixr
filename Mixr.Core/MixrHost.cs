using System.Collections.Generic;
using System.IO;
using Mixr.Models;
using Mixr.Services;

namespace Mixr;

/// <summary>Gemeinsame Hintergrundlogik für Konsole und WinUI (SMTC, Seriell, Audio, Hotkeys).</summary>
public static class MixrHost
{
    public sealed class Options
    {
        public Action<string>? Log { get; init; }
        public Action<string>? LogError { get; init; }
    }

    static void LogLine(Options? o, string s)
    {
        (o?.Log ?? Console.WriteLine)(s);
    }

    static void LogErr(Options? o, string s)
    {
        (o?.LogError ?? Console.Error.WriteLine)(s);
    }

    public static async Task RunAsync(string[] args, CancellationToken cancellationToken, Options? options = null)
    {
        var initial = MixrConfigLoader.Load(args);
        MixrRuntimeState.Config.Replace(initial);

        void LogCfgHeader()
        {
            var cfg = MixrRuntimeState.Config.Current;
            if (cfg.IsComPortAuto)
            {
                LogLine(
                    options,
                    $"Mixr → COM automatisch (ESP32-S3 USB {MixrDevicePortResolver.EspressifVid:X4}:{MixrDevicePortResolver.Esp32S3UsbSerialJtagPid:X4}) @ {cfg.BaudRate}");
            }
            else
            {
                LogLine(options, $"Mixr → {cfg.ComPort} @ {cfg.BaudRate}");
            }

            LogLine(options, "Wiederverbindung bei USB; config.yaml und optional config.secrets.yaml werden überwacht.");
            LogLine(options, "Taster: siehe button_mapping in config.yaml (Standard: Prev / Play / Next / Mute / Deafen).");
            LogLine(options, "Slider: 1=Main · 2=Kommunikation · 3=Media · 4=Spiele");
        }

        LogCfgHeader();

        MixrSerialTransport? serial = null;

        using var media = new WindowsNowPlayingService();
        var dedup = new SessionDedup();
        media.SessionUpdated += (title, artist, cover) =>
        {
            try
            {
                if (serial is null)
                    return;
                if (!dedup.ShouldSend(title, artist, cover))
                    return;

                serial.SendSession(title, artist, cover);
                var t = string.IsNullOrEmpty(title) ? "—" : title;
                var a = string.IsNullOrEmpty(artist) ? "—" : artist;
                LogLine(options, $"→ ESP: „{t}“ — {a}");
            }
            catch (Exception ex)
            {
                LogErr(options, ex.Message);
            }
        };

        await media.InitializeAsync();

        var audio = new AudioService();
        MixrRuntimeState.Audio = audio;

        void RebuildAudioFromRuntime()
        {
            var c = MixrRuntimeState.Config.Current;
            try
            {
                audio.RebuildSessionMap(c.SliderMapping, c.SessionGroups);
            }
            catch (Exception ex)
            {
                LogErr(options, $"Audio neu: {ex.Message}");
            }
        }

        RebuildAudioFromRuntime();
        MixrRuntimeState.Config.Changed += OnConfigChangedFromRuntime;

        var reloadGate = new object();
        CancellationTokenSource? debounceCts = null;
        List<FileSystemWatcher>? configWatchers = null;

        try
        {
            var dir = Path.GetDirectoryName(MixrConfigPaths.ConfigYamlPath);
            var mainFile = Path.GetFileName(MixrConfigPaths.ConfigYamlPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                configWatchers = [];
                foreach (var name in new[] { mainFile, "config.secrets.yaml" }.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var w = new FileSystemWatcher(dir, name)
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    };
                    w.Changed += (_, _) => DebouncedConfigReload();
                    w.EnableRaisingEvents = true;
                    configWatchers.Add(w);
                }
            }
        }
        catch
        {
            /* optional */
        }

        void DebouncedConfigReload()
        {
            lock (reloadGate)
            {
                debounceCts?.Cancel();
                debounceCts = new CancellationTokenSource();
                var cts = debounceCts;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(400, cts.Token);
                        MixrRuntimeState.ReloadConfigFromDisk(args);
                        LogLine(options, "Konfiguration neu geladen (config.yaml / config.secrets.yaml).");
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        LogErr(options, $"Config-Reload: {ex.Message}");
                    }
                }, cts.Token);
            }
        }

        void OnConfigChangedFromRuntime()
        {
            RebuildAudioFromRuntime();
        }

        var lastSlider = new float[] { -1, -1, -1, -1 };

        var espIncoming = new EspIncomingDispatcher();
        espIncoming.SliderValues += mem =>
        {
            var s = mem.Span;
            if (s.Length < 4)
                return;
            var cfg = MixrRuntimeState.Config.Current;
            for (var i = 0; i < 4 && i < cfg.SliderMapping.Count; i++)
            {
                var level = s[i] / 255f;
                if (cfg.InvertSliders)
                    level = 1f - level;
                if (Math.Abs(level - lastSlider[i]) > 0.005f)
                {
                    lastSlider[i] = level;
                    audio.SetVolume(cfg.SliderMapping[i], level);
                }
            }
        };

        void TriggerDiscordMute(string quelle)
        {
            try
            {
                DiscordHotkeySimulator.TriggerToggleMute();
                LogLine(options, $"→ Discord: Toggle-Mute ({quelle})");
                serial?.SendVoipMuteOverlayToggle();
            }
            catch (Exception ex)
            {
                LogErr(options, ex.Message);
            }
        }

        void TriggerDiscordDeafen(string quelle)
        {
            try
            {
                DiscordHotkeySimulator.TriggerToggleDeafen();
                LogLine(options, $"→ Discord: Toggle-Deafen ({quelle})");
                serial?.SendVoipDeafenOverlayToggle();
            }
            catch (Exception ex)
            {
                LogErr(options, ex.Message);
            }
        }

        void TriggerShareScreenFromEsp()
        {
            try
            {
                DiscordHotkeySimulator.TriggerShareScreen();
                LogLine(options, "→ Discord: Share Screen (ESP Debug-Menü)");
            }
            catch (Exception ex)
            {
                LogErr(options, ex.Message);
            }
        }

        espIncoming.ButtonPressed += id =>
        {
            LogLine(options, $"[ESP] Button: {id}");
            var cfgBtn = MixrRuntimeState.Config.Current;
            var action = MixrButtonActions.Resolve(id, cfgBtn.ButtonMapping);
            switch (action)
            {
                case MixrButtonActions.SmtcPrevious:
                    _ = Task.Run(() => media.ExecuteMediaCommandAsync(2));
                    LogLine(options, "→ SMTC: Previous");
                    break;
                case MixrButtonActions.SmtcPlayPause:
                    _ = Task.Run(() => media.ExecuteMediaCommandAsync(1));
                    LogLine(options, "→ SMTC: Play/Pause");
                    break;
                case MixrButtonActions.SmtcNext:
                    _ = Task.Run(() => media.ExecuteMediaCommandAsync(0));
                    LogLine(options, "→ SMTC: Next");
                    break;
                case MixrButtonActions.DiscordMute:
                    TriggerDiscordMute($"Taster {id} ({action})");
                    break;
                case MixrButtonActions.DiscordDeafen:
                    TriggerDiscordDeafen($"Taster {id} ({action})");
                    break;
                case MixrButtonActions.None:
                    LogLine(options, $"→ Button {id}: keine Aktion");
                    break;
                default:
                    LogErr(options, $"Unbekannte button_mapping-Aktion: {action}");
                    break;
            }
        };
        espIncoming.VoipMuteRequested += () => TriggerDiscordMute("ESP Debug-Menü");
        espIncoming.VoipDeafenRequested += () => TriggerDiscordDeafen("ESP Debug-Menü");
        espIncoming.ShareScreenRequested += TriggerShareScreenFromEsp;
        espIncoming.MediaCommand += sub => _ = Task.Run(() => media.ExecuteMediaCommandAsync(sub));

        void OnEspPacket(int type, byte[] payload) => espIncoming.Dispatch(type, payload);

        _ = Task.Run(async () =>
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(2000, cancellationToken);
                    RebuildAudioFromRuntime();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, cancellationToken);

        VoipHotkeyListener.Start(
            () =>
            {
                try
                {
                    serial?.SendVoipMuteOverlayToggle();
                }
                catch (IOException)
                {
                }
            },
            () =>
            {
                try
                {
                    serial?.SendVoipDeafenOverlayToggle();
                }
                catch (IOException)
                {
                }
            });

        LogLine(
            options,
            "Discord-Hotkeys: Strg+Linksshift+Alt — 9 Mute, 0 Deafen, 8 Share Screen.");

        const int reconnectDelayMs = 2000;

        string? ResolveComPort()
        {
            var cfg = MixrRuntimeState.Config.Current;
            if (!cfg.IsComPortAuto)
                return cfg.ComPort.Trim();

            var port = MixrDevicePortResolver.TryFindComPort(out var candidates);
            if (port != null && candidates.Count > 1)
            {
                LogLine(
                    options,
                    $"Hinweis: mehrere ESP32-S3 USB-Geräte ({string.Join(", ", candidates)}) — verwende {port}.");
            }

            return port;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var portName = ResolveComPort();
                if (string.IsNullOrEmpty(portName))
                {
                    LogLine(options, "Kein Mixr-USB-Gerät gefunden — Kabel prüfen, nächster Versuch in 2 s …");
                    try
                    {
                        await Task.Delay(reconnectDelayMs, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    continue;
                }

                var cfg = MixrRuntimeState.Config.Current;
                MixrSerialTransport? conn = null;
                var disconnectTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                try
                {
                    conn = new MixrSerialTransport(portName, cfg.BaudRate);
                    conn.Open();
                    serial = conn;
                    LogLine(options, $"Seriell verbunden ({portName}).");

                    conn.StartDrainRxThread(OnEspPacket, () => disconnectTcs.TrySetResult());

                    await disconnectTcs.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogErr(options, $"Seriell: {ex.Message}");
                }
                finally
                {
                    serial = null;
                    conn?.Dispose();
                }

                if (cancellationToken.IsCancellationRequested)
                    break;

                LogLine(options, $"USB/Seriell getrennt — nächster Versuch in {reconnectDelayMs / 1000} s …");
                try
                {
                    await Task.Delay(reconnectDelayMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            MixrRuntimeState.Config.Changed -= OnConfigChangedFromRuntime;
            if (configWatchers is { Count: > 0 })
            {
                foreach (var w in configWatchers)
                {
                    try
                    {
                        w.EnableRaisingEvents = false;
                        w.Dispose();
                    }
                    catch
                    {
                        /* */
                    }
                }
            }

            MixrRuntimeState.Audio = null;
        }
    }
}
