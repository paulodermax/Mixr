namespace Mixr.Services;

/// <summary>
/// Entscheidet, wie das mitgelieferte Firmware-Image aufs Gerät kommt:
///  1. Protokoll-OTA (Gerät meldet OTA-Partition) — ohne Unterbrechung der Verbindung.
///  2. Sonst ROM-Download-Modus + esptool: HID-Geräte werden per ENTER_BOOTLOADER hineingeschickt,
///     serielle Geräte per Reset-Sequenz von esptool selbst.
/// </summary>
public static class FirmwareUpdateCoordinator
{
    public static Action<string> Log { get; set; } = _ => { };

    static readonly Lazy<FirmwareImage?> BundledLazy = new(() => FirmwareImage.TryLoadBundled(s => Log(s)));

    static int _busy;

    public static FirmwareImage? Bundled => BundledLazy.Value;

    public static bool IsBusy => Volatile.Read(ref _busy) != 0;

    public static event Action? BusyChanged;

    /// <summary>Gerät verbunden, meldet HELLO, und das mitgelieferte Image ist neuer.</summary>
    public static bool UpdateRecommended
    {
        get
        {
            var img = Bundled;
            var dev = MixrRuntimeState.Device;
            return img != null && dev != null && MixrRuntimeState.EspConnected
                && FirmwareImage.IsNewerThan(img.Version, dev.FirmwareVersion);
        }
    }

    /// <summary>Kurztext für die Settings-Seite.</summary>
    public static string DescribeState()
    {
        var img = Bundled;
        var dev = MixrRuntimeState.Device;
        var connected = MixrRuntimeState.EspConnected;

        if (img is null)
            return "Kein Firmware-Image in dieser App-Version enthalten.";
        if (!connected)
            return $"Mitgeliefert: {img.Version} — Gerät nicht verbunden.";
        if (dev is null)
            return $"Mitgeliefert: {img.Version} — Gerät meldet keine Version (Firmware vor Protokoll v2). Update über USB-Download-Modus möglich.";

        var how = dev.SupportsProtocolOta ? "direkt über die USB-Verbindung" : "über USB-Download-Modus (esptool)";
        if (FirmwareImage.IsNewerThan(img.Version, dev.FirmwareVersion))
            return $"Gerät: {dev.FirmwareVersion} → verfügbar: {img.Version} ({how}).";
        if (string.Equals(img.Version, dev.FirmwareVersion, StringComparison.OrdinalIgnoreCase))
            return $"Gerät: {dev.FirmwareVersion} — aktuell.";
        return $"Gerät: {dev.FirmwareVersion}, mitgeliefert: {img.Version} ({how}).";
    }

    public static async Task<FirmwareUpdateResult> UpdateAsync(IProgress<FirmwareUpdateProgress>? progress, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return new FirmwareUpdateResult(FirmwareUpdateOutcome.Failed, "Ein Firmware-Update läuft bereits.");
        BusyChanged?.Invoke();

        try
        {
            var img = Bundled;
            if (img is null)
                return new FirmwareUpdateResult(FirmwareUpdateOutcome.Failed, "Kein Firmware-Image vorhanden.");

            var dev = MixrRuntimeState.Device;
            var link = MixrRuntimeState.Link;

            // 1) Protokoll-OTA über den offenen Link (HID oder Seriell)
            if (dev is { SupportsProtocolOta: true } && link is not null && link.Link.IsOpen)
            {
                Log($"FW: Protokoll-Update {dev.FirmwareVersion} → {img.Version} über {link.Link.Id}");
                var svc = new FirmwareUpdateService(link.Link, link.Dispatcher, Log);
                var r = await svc.UpdateAsync(img, progress, ct).ConfigureAwait(false);
                if (r.Outcome != FirmwareUpdateOutcome.Unsupported)
                    return r;
                Log("FW: Gerät meldet UNSUPPORTED — wechsle auf Download-Modus.");
            }

            // 2) ROM-Download-Modus + esptool
            await EsptoolFlasher.EnsureAvailableAsync(progress, Log, ct).ConfigureAwait(false);

            using (MixrRuntimeState.PauseSerial())
            {
                string? port;
                if (link is { Kind: MixrLinkKind.Hid } && dev is { SupportsBootloaderCmd: true })
                {
                    // Gerät verschwindet als HID und taucht als Espressif-COM-Port (ROM-Bootloader) wieder auf.
                    progress?.Report(new FirmwareUpdateProgress(0, "Gerät wird in den Download-Modus geschickt …"));
                    link.Link.TrySend(MixrProtocol.TypeEnterBootloader);
                    await Task.Delay(300, ct).ConfigureAwait(false);
                    link.Link.Dispose();
                    port = await WaitForRomPortAsync(TimeSpan.FromSeconds(12), ct).ConfigureAwait(false);
                    if (port is null)
                        return new FirmwareUpdateResult(FirmwareUpdateOutcome.Failed,
                            "Gerät ist nicht im Download-Modus erschienen (kein Espressif-COM-Port). USB neu anstecken und erneut versuchen.");
                    Log($"FW: ROM-Bootloader auf {port}");
                    return await EsptoolFlasher.FlashAsync(img, port, progress, Log, ct, alreadyInBootloader: true).ConfigureAwait(false);
                }

                port = MixrRuntimeState.LastPortName;
                if (string.IsNullOrEmpty(port))
                {
                    // HID-Gerät ohne Bootloader-Befehl (alte v3-Firmware?) — vielleicht hängt es schon als COM-Port.
                    port = MixrDevicePortResolver.TryFindComPort(out _);
                    if (string.IsNullOrEmpty(port))
                        return new FirmwareUpdateResult(FirmwareUpdateOutcome.Failed, "Kein COM-Port bekannt — Gerät zuerst verbinden.");
                }

                // Dem Host Zeit geben, den Port wirklich zu schließen (RX-Thread endet asynchron).
                await Task.Delay(700, ct).ConfigureAwait(false);
                Log($"FW: esptool-Update {dev?.FirmwareVersion ?? "?"} → {img.Version} auf {port}");
                return await EsptoolFlasher.FlashAsync(img, port, progress, Log, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
            BusyChanged?.Invoke();
        }
    }

    static async Task<string?> WaitForRomPortAsync(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var port = MixrDevicePortResolver.TryFindComPort(out _);
            if (!string.IsNullOrEmpty(port))
            {
                await Task.Delay(500, ct).ConfigureAwait(false); // Treiber fertig laden lassen
                return port;
            }

            await Task.Delay(400, ct).ConfigureAwait(false);
        }

        return null;
    }
}
