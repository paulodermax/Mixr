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

void OnEspPacket(int type, byte[] payload)
{
    const byte typeSlider = 0x03;
    const byte typeBtn = 0x04;
    if (type == typeSlider && payload.Length >= 4)
        Console.WriteLine($"[ESP] Slider: {payload[0]} {payload[1]} {payload[2]} {payload[3]}");
    else if (type == typeBtn && payload.Length >= 1)
        Console.WriteLine($"[ESP] Button: {payload[0]}");
    else if (type == MixrSerialTransport.TypeMediaCmd && payload.Length >= 1)
        _ = Task.Run(() => media.ExecuteMediaCommandAsync(payload[0]));
}

serial.StartDrainRxThread(OnEspPacket);

Console.WriteLine("Windows-Mediensteuerung (SMTC) aktiv. Ctrl+C beenden.");
using var done = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    done.Set();
};
done.Wait();
