using Mixr;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Any(a => a is "--help" or "-h" or "/?"))
{
    Console.WriteLine(
        """
        Mixr PC — Konsole (Hintergrund-Logik wie Mixr.App).

        Seriell: --port COM7 (optional; Standard = automatische USB-Erkennung) --baud 921600

        Von der Firmware:
          • Slider, Tasten (0–4): 0 Previous, 1 Play/Pause, 2 Next (SMTC), 3 Discord Mute, 4 Discord Deafen
          • Pkt 0x08 / 0x0B / 0x0C: VoIP / Share (Debug-Menü) → Hotkey; PC antwortet 0x0A / 0x0B → VoIP-Icons
          • Tastatur: Strg+Linksshift+Alt+9 / +0 / +8 (Share)

        config.yaml: com_port, baud_rate, slider_mapping, session_groups, invert_sliders
        """);
    return;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await MixrHost.RunAsync(args, cts.Token);
