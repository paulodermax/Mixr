using Mixr.Models;
using Mixr.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var cfg = MixrConfigLoader.Load(args);
Console.WriteLine($"Mixr PC → {cfg.ComPort} @ {cfg.BaudRate} (SMTC → ESP, Burst wie mixr_send_demo.py --fast)");

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
espIncoming.ButtonPressed += id => Console.WriteLine($"[ESP] Button: {id}");
espIncoming.MediaCommand += sub => _ = Task.Run(() => media.ExecuteMediaCommandAsync(sub));

void OnEspPacket(int type, byte[] payload) => espIncoming.Dispatch(type, payload);

serial.StartDrainRxThread(OnEspPacket);

Console.WriteLine("Windows-Mediensteuerung (SMTC) aktiv. Ctrl+C beenden.");
using var done = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    done.Set();
};
done.Wait();
