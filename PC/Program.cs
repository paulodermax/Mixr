using Mixr.Models;
using Mixr.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Any(a => a is "--help" or "-h" or "/?"))
{
    Console.WriteLine(
        """
        Mixr PC — SMTC → ESP, serielles Protokoll.

        Seriell: --port COM6 --baud 115200

        Von der Firmware:
          • Slider, Tasten, Media-Befehle
          • Pkt 0x08 / 0x0B / 0x0C: VoIP-Mute / Deafen / Bildschirm teilen (Debug-Menü / Button) → Hotkey; PC antwortet 0x0A / 0x0B → VoIP-Icons
          • Tastatur: Strg+Linksshift+Alt+9 / +0 / +8 (Share) — in Discord identisch zuordnen

        config.yaml: voip_mute_button (0–4, -1 aus), com_port, baud_rate
        """);
    return;
}

var cfg = MixrConfigLoader.Load(args);
Console.WriteLine($"Mixr PC → {cfg.ComPort} @ {cfg.BaudRate} (SMTC → ESP)");
if (cfg.VoipMuteButton is >= 0 and <= 4)
    Console.WriteLine(
        $"VoIP: Hardware-Button {cfg.VoipMuteButton} → Mute; Debug-Menü „PC: Discord mute/deafen“");
else
    Console.WriteLine("VoIP: nur Debug-Menü „PC: Discord mute“ / „PC: Discord deafen“ (voip_mute_button: -1)");

using var serial = new MixrSerialTransport(cfg.ComPort, cfg.BaudRate);
serial.Open();

using var media = new WindowsNowPlayingService();
var dedup = new SessionDedup();
media.SessionUpdated += (title, artist, cover) =>
{
    try
    {
        if (!dedup.ShouldSend(title, artist, cover))
            return;

        serial.SendSession(title, artist, cover);
        var t = string.IsNullOrEmpty(title) ? "—" : title;
        var a = string.IsNullOrEmpty(artist) ? "—" : artist;
        Console.WriteLine($"→ ESP: „{t}“ — {a}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
    }
};

await media.InitializeAsync();

var espIncoming = new EspIncomingDispatcher();
espIncoming.SliderValues += mem =>
{
    var s = mem.Span;
    Console.WriteLine($"[ESP] Slider: {s[0]} {s[1]} {s[2]} {s[3]}");
};
espIncoming.ButtonPressed += id =>
{
    Console.WriteLine($"[ESP] Button: {id}");
    if (cfg.VoipMuteButton is >= 0 and <= 4 && id == cfg.VoipMuteButton)
        TriggerDiscordMute($"Hardware-Button {id}");
};
espIncoming.VoipMuteRequested += () => TriggerDiscordMute("ESP Debug-Menü");
espIncoming.VoipDeafenRequested += () => TriggerDiscordDeafen("ESP Debug-Menü");
espIncoming.ShareScreenRequested += TriggerShareScreenFromEsp;
espIncoming.MediaCommand += sub => _ = Task.Run(() => media.ExecuteMediaCommandAsync(sub));

void TriggerDiscordMute(string quelle)
{
    try
    {
        DiscordHotkeySimulator.TriggerToggleMute();
        Console.WriteLine($"→ Discord: Toggle-Mute ({quelle})");
        serial.SendVoipMuteOverlayToggle();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
    }
}

void TriggerDiscordDeafen(string quelle)
{
    try
    {
        DiscordHotkeySimulator.TriggerToggleDeafen();
        Console.WriteLine($"→ Discord: Toggle-Deafen ({quelle})");
        serial.SendVoipDeafenOverlayToggle();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
    }
}

void TriggerShareScreenFromEsp()
{
    try
    {
        DiscordHotkeySimulator.TriggerShareScreen();
        Console.WriteLine("→ Discord: Share Screen (ESP Debug-Menü)");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
    }
}

void OnEspPacket(int type, byte[] payload) => espIncoming.Dispatch(type, payload);

serial.StartDrainRxThread(OnEspPacket);

VoipHotkeyListener.Start(
    () =>
    {
        try
        {
            serial.SendVoipMuteOverlayToggle();
        }
        catch (IOException)
        {
        }
    },
    () =>
    {
        try
        {
            serial.SendVoipDeafenOverlayToggle();
        }
        catch (IOException)
        {
        }
    });

Console.WriteLine(
    "Discord-Hotkeys: Strg+Linksshift+Alt+Ziffer — 9 Mute, 0 Deafen, 8 Share Screen.");
Console.WriteLine("Windows-Mediensteuerung (SMTC) aktiv. Ctrl+C beenden.");
using var done = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    done.Set();
};
done.Wait();
