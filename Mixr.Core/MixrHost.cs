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
        var cfg = MixrConfigLoader.Load(args);
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

        LogLine(options, "Wiederverbindung bei USB.");
        LogLine(
            options,
            "Taster: 0 Previous | 1 Play/Pause | 2 Next | 3 Discord Mute | 4 Discord Deafen");
        LogLine(options, "Slider: 1=Main · 2=Kommunikation · 3=Media · 4=Spiele");

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
        try
        {
            audio.RebuildSessionMap(cfg.SliderMapping, cfg.SessionGroups);
        }
        catch (Exception ex)
        {
            LogErr(options, $"Audio-Init: {ex.Message}");
        }

        var lastSlider = new float[] { -1, -1, -1, -1 };

        var espIncoming = new EspIncomingDispatcher();
        espIncoming.SliderValues += mem =>
        {
            var s = mem.Span;
            if (s.Length < 4)
                return;
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
            switch (id)
            {
                case 0:
                    _ = Task.Run(() => media.ExecuteMediaCommandAsync(2));
                    LogLine(options, "→ SMTC: Previous");
                    break;
                case 1:
                    _ = Task.Run(() => media.ExecuteMediaCommandAsync(1));
                    LogLine(options, "→ SMTC: Play/Pause");
                    break;
                case 2:
                    _ = Task.Run(() => media.ExecuteMediaCommandAsync(0));
                    LogLine(options, "→ SMTC: Next");
                    break;
                case 3:
                    TriggerDiscordMute("Taster 3 / SW4");
                    break;
                case 4:
                    TriggerDiscordDeafen("Taster 4 / SW5");
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
                    audio.RebuildSessionMap(cfg.SliderMapping, cfg.SessionGroups, silent: true);
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
}
