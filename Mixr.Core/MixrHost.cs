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
        MixrConfigLoader.DiagnosticLog ??= s => LogLine(options, s);
        IgdbCredentialResolver.DiagnosticLog ??= s => LogLine(options, s);
        VoipHotkeyListener.DiagnosticLog ??= s => LogErr(options, s);
        FirmwareUpdateCoordinator.Log = s => LogLine(options, s);

        var initial = MixrConfigLoader.Load(args);
        MixrRuntimeState.Config.Replace(initial);
        LogLine(options, $"Konfiguration: {MixrConfigPaths.ConfigYamlPath}");

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

            LogLine(options, $"Wiederverbindung bei USB; {MixrConfigPaths.ConfigFileName} und optional {MixrConfigPaths.SecretsFileName} werden überwacht.");
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
        var systemSoundsCapDone = false;

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

        void TryApplySystemSoundsCapOnce()
        {
            var c = MixrRuntimeState.Config.Current;
            if (!c.LimitSystemSoundsTo20Percent)
            {
                systemSoundsCapDone = false;
                return;
            }

            if (systemSoundsCapDone)
                return;

            systemSoundsCapDone = true;
            if (SystemSoundsVolumeService.TryApplyCap())
                LogLine(options, "System Sounds: auf max. 20 % gesetzt (falls nötig).");
        }

        RebuildAudioFromRuntime();
        TryApplySystemSoundsCapOnce();

        var sliderLuts = new float[4][];
        void RebuildSliderLuts()
        {
            var c = MixrRuntimeState.Config.Current;
            for (var i = 0; i < 4; i++)
                sliderLuts[i] = VolumeCurveMapper.BuildLut(VolumeCurveMapper.GetKindForSlider(c, i));
        }

        RebuildSliderLuts();

        MixrRuntimeState.Config.Changed += OnConfigChangedFromRuntime;
        MixrRuntimeState.Config.Changed += RebuildSliderLuts;

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
                foreach (var name in new[] { mainFile, MixrConfigPaths.SecretsFileName }.Distinct(StringComparer.OrdinalIgnoreCase))
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
            TryApplySystemSoundsCapOnce();
        }

        var lastSlider = new float[] { -1, -1, -1, -1 };

        var espIncoming = new EspIncomingDispatcher();
        espIncoming.SliderValues += mem =>
        {
            var s = mem.Span;
            if (s.Length < 4)
                return;
            var cfg = MixrRuntimeState.Config.Current;
            Span<float> uiLevels = stackalloc float[4];
            for (var i = 0; i < 4 && i < cfg.SliderMapping.Count; i++)
            {
                var raw = s[i];
                if (cfg.InvertSliders)
                    raw = (byte)(255 - raw);
                var level = sliderLuts[i][raw];
                uiLevels[i] = level;
                if (Math.Abs(level - lastSlider[i]) > 0.002f)
                {
                    lastSlider[i] = level;
                    audio.SetVolume(cfg.SliderMapping[i], level);
                }
            }

            MixrRuntimeState.SetSliderLevels(uiLevels);
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
        espIncoming.Hello += hello =>
        {
            MixrRuntimeState.SetDevice(hello);
            LogLine(
                options,
                $"[ESP] HELLO: Protokoll v{hello.ProtocolVersion}, Firmware {hello.FirmwareVersion}, OTA {(hello.SupportsProtocolOta ? "ja" : "nein (Download-Modus)")}");
            if (hello.ProtocolVersion > MixrProtocol.Version)
                LogErr(options, $"Firmware spricht Protokoll v{hello.ProtocolVersion}, diese App nur v{MixrProtocol.Version} — bitte App aktualisieren.");
        };

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

        MixrSerialTransport? activeConn = null;
        var activeConnLock = new object();

        void OnSerialPauseRequested()
        {
            MixrSerialTransport? c;
            lock (activeConnLock)
                c = activeConn;
            if (c is null)
                return;
            LogLine(options, "Seriell: Port für Firmware-Update freigegeben.");
            try
            {
                c.Dispose(); /* beendet den RX-Thread → disconnectTcs → Schleife räumt auf */
            }
            catch
            {
                /* bereits geschlossen */
            }
        }

        MixrRuntimeState.SerialPauseRequested += OnSerialPauseRequested;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (MixrRuntimeState.SerialPaused)
                {
                    try
                    {
                        await MixrRuntimeState.WaitSerialResumedAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    LogLine(options, "Seriell: Pause beendet, verbinde neu …");
                    try
                    {
                        await Task.Delay(1500, cancellationToken); /* Gerät bootet nach dem Flashen */
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

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
                    lock (activeConnLock)
                        activeConn = conn;
                    MixrRuntimeState.SetLink(new SerialLink(conn, espIncoming), portName);
                    MixrRuntimeState.SetEspConnected(true);
                    LogLine(options, $"Seriell verbunden ({portName}).");

                    conn.StartDrainRxThread(OnEspPacket, () => disconnectTcs.TrySetResult());
                    conn.SendHelloRequest();

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
                    lock (activeConnLock)
                        activeConn = null;
                    MixrRuntimeState.SetLink(null, null);
                    MixrRuntimeState.SetDevice(null);
                    MixrRuntimeState.SetEspConnected(false);
                    conn?.Dispose();
                }

                if (cancellationToken.IsCancellationRequested)
                    break;

                if (MixrRuntimeState.SerialPaused)
                    continue; /* Firmware-Update übernimmt den Port; oben wird auf Resume gewartet */

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
            MixrRuntimeState.SerialPauseRequested -= OnSerialPauseRequested;
            MixrRuntimeState.Config.Changed -= OnConfigChangedFromRuntime;
            MixrRuntimeState.Config.Changed -= RebuildSliderLuts;
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

            lock (reloadGate)
            {
                debounceCts?.Cancel();
                debounceCts?.Dispose();
                debounceCts = null;
            }

            VoipHotkeyListener.Stop();

            MixrRuntimeState.Audio = null;
            audio.Dispose();
            MixrRuntimeState.SetLink(null, null);
            MixrRuntimeState.SetDevice(null);
            MixrRuntimeState.SetEspConnected(false);
            LogLine(options, "MixrHost: Ressourcen freigegeben.");
        }
    }
}
