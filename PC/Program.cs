using Mixr.Models;
using Mixr.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Any(a => a is "--help" or "-h" or "/?"))
{
    Console.WriteLine(
        """
        Mixr PC — SMTC → ESP, serielles Protokoll.

        Seriell: --port COM6 --baud 921600

        Von der Firmware:
          • Slider, Tasten (0–4): 0 Previous, 1 Play/Pause, 2 Next (SMTC), 3 Discord Mute, 4 Discord Deafen
          • Pkt 0x08 / 0x0B / 0x0C: VoIP / Share (Debug-Menü) → Hotkey; PC antwortet 0x0A / 0x0B → VoIP-Icons
          • Tastatur: Strg+Linksshift+Alt+9 / +0 / +8 (Share)

        config.yaml: com_port, baud_rate, slider_mapping, session_groups, invert_sliders
        """);
    return;
}

var cfg = MixrConfigLoader.Load(args);
Console.WriteLine($"Mixr PC → {cfg.ComPort} @ {cfg.BaudRate} (SMTC → ESP)");
Console.WriteLine(
    "Taster: 0 Previous | 1 Play/Pause | 2 Next | 3 Discord Mute | 4 Discord Deafen (zusätzlich Debug-Menü VoIP)");
Console.WriteLine("Slider: 1=Main · 2=Kommunikation · 3=Media · 4=Spiele (session_groups in config.yaml)");

using var cts = new CancellationTokenSource();
using var done = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    done.Set();
};

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

var audio = new AudioService();
try
{
    audio.RebuildSessionMap(cfg.SliderMapping, cfg.SessionGroups);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Audio-Init: {ex.Message}");
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
espIncoming.ButtonPressed += id =>
{
    Console.WriteLine($"[ESP] Button: {id}");
    switch (id)
    {
        case 0:
            _ = Task.Run(() => media.ExecuteMediaCommandAsync(2)); /* MediaSubCmd: Previous */
            Console.WriteLine("→ SMTC: Previous");
            break;
        case 1:
            _ = Task.Run(() => media.ExecuteMediaCommandAsync(1)); /* Play/Pause */
            Console.WriteLine("→ SMTC: Play/Pause");
            break;
        case 2:
            _ = Task.Run(() => media.ExecuteMediaCommandAsync(0)); /* Next */
            Console.WriteLine("→ SMTC: Next");
            break;
        case 3:
            TriggerDiscordMute("Taster 3 / SW4");
            break;
        case 4:
            TriggerDiscordDeafen("Taster 4 / SW5 (CHIP_UP)");
            break;
    }
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

_ = Task.Run(async () =>
{
    try
    {
        while (!cts.Token.IsCancellationRequested)
        {
            await Task.Delay(2000, cts.Token);
            audio.RebuildSessionMap(cfg.SliderMapping, cfg.SessionGroups, silent: true);
        }
    }
    catch (OperationCanceledException)
    {
        /* Beenden */
    }
});

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
done.Wait();
