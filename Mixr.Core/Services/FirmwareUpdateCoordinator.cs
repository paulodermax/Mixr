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

    const string ManualBootHint =
        "Kein Espressif-COM-Port gefunden.\n\n" +
        "So geht’s manuell:\n" +
        "1. BOOT-Taste am Gerät gedrückt halten\n" +
        "2. RESET tippen (oder USB kurz abziehen/anstecken), BOOT loslassen\n" +
        "3. In Windows Geräte-Manager prüfen, ob ein neuer COM-Port von Espressif erscheint\n" +
        "4. Hier erneut „Firmware aktualisieren“ klicken";

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

            // 0) Gerät steckt vielleicht schon im ROM-Download-Modus (manuell BOOT+RESET)
            var existingRom = MixrDevicePortResolver.TryFindComPort(out var existingCandidates);
            if (!string.IsNullOrEmpty(existingRom) && (link is null || link.Kind == MixrLinkKind.Hid && !MixrRuntimeState.EspConnected))
            {
                Log($"FW: Espressif-COM schon da ({string.Join(", ", existingCandidates)}) — flashe ohne ENTER_BOOTLOADER.");
                await EsptoolFlasher.EnsureAvailableAsync(progress, Log, ct).ConfigureAwait(false);
                using (MixrRuntimeState.PauseSerial())
                    return await EsptoolFlasher.FlashAsync(img, existingRom, progress, Log, ct, alreadyInBootloader: true)
                        .ConfigureAwait(false);
            }

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
                if (link is { Kind: MixrLinkKind.Hid } && (dev is null || dev.SupportsBootloaderCmd))
                {
                    progress?.Report(new FirmwareUpdateProgress(0, "Gerät wird in den Download-Modus geschickt …"));
                    Log("FW: sende ENTER_BOOTLOADER (HID → ROM-COM-Port)");
                    link.Link.TrySend(MixrProtocol.TypeEnterBootloader);
                    // Firmware trennt TinyUSB und startet neu — Host muss den HID-Handle loslassen.
                    await Task.Delay(400, ct).ConfigureAwait(false);
                    try { link.Link.Dispose(); } catch { /* */ }

                    progress?.Report(new FirmwareUpdateProgress(5, "Warte auf Espressif-COM-Port …"));
                    port = await WaitForRomPortAsync(TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
                    if (port is null)
                        return new FirmwareUpdateResult(FirmwareUpdateOutcome.Failed, ManualBootHint);

                    Log($"FW: ROM-Bootloader auf {port}");
                    return await EsptoolFlasher.FlashAsync(img, port, progress, Log, ct, alreadyInBootloader: true)
                        .ConfigureAwait(false);
                }

                port = MixrRuntimeState.LastPortName;
                if (string.IsNullOrEmpty(port))
                    port = MixrDevicePortResolver.TryFindComPort(out _);
                if (string.IsNullOrEmpty(port))
                    return new FirmwareUpdateResult(FirmwareUpdateOutcome.Failed, ManualBootHint);

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
        var lastLog = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            // HID weg + Espressif-COM da = Download-Modus
            var hidStillThere = MixrHidTransport.Enumerate().Count > 0;
            var port = MixrDevicePortResolver.TryFindComPort(out var candidates);
            if (!string.IsNullOrEmpty(port) && !hidStillThere)
            {
                Log($"FW: COM gefunden ({string.Join(", ", candidates)}), HID weg — warte kurz auf Treiber …");
                await Task.Delay(800, ct).ConfigureAwait(false);
                return port;
            }

            // Manchmal bleibt ein „leerer“ HID-Rest kurz hängen — COM trotzdem nehmen, wenn er da ist.
            if (!string.IsNullOrEmpty(port))
            {
                Log($"FW: COM gefunden ({string.Join(", ", candidates)})" + (hidStillThere ? " (HID noch sichtbar)" : ""));
                await Task.Delay(800, ct).ConfigureAwait(false);
                return port;
            }

            if ((DateTime.UtcNow - lastLog).TotalSeconds >= 3)
            {
                lastLog = DateTime.UtcNow;
                Log(hidStillThere
                    ? "FW: warte … HID noch da, kein Espressif-COM"
                    : "FW: warte … weder HID noch Espressif-COM");
            }

            await Task.Delay(350, ct).ConfigureAwait(false);
        }

        return null;
    }
}
