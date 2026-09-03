namespace Mixr.Services;

/// <summary>
/// Firmware-Update-Koordinator — Ziel: Feld-Updates funktionieren ohne Tasten/COM-Port.
///
/// Reihenfolge:
///  1. Protokoll-Update (FW_*) über den offenen Link (HID oder Seriell).
///     Gerät mit OTA-Partition ODER PSRAM-Staging meldet MIXR_CAP_OTA_PROTOCOL.
///  2. Fallback: ENTER_BOOTLOADER → Espressif-COM → esptool (mit Retries).
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
        "Automatisches Update fehlgeschlagen.\n\n" +
        "Einmalige Notfall-Rettung:\n" +
        "1. BOOT halten → RESET tippen → BOOT loslassen\n" +
        "2. Erneut „Firmware aktualisieren“ klicken\n\n" +
        "Danach funktionieren künftige Updates wieder ohne Tasten.";

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
            return $"Mitgeliefert: {img.Version} — Gerät meldet keine Version. Update über Download-Modus möglich.";

        var how = dev.SupportsProtocolOta
            ? "direkt über USB (ohne Neustart in den Bootloader)"
            : "über USB-Download-Modus (esptool)";
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

            // 0) Gerät steckt schon im ROM-Download (manuelle Notfall-Rettung)
            if (TryFindRomPort(out var romPort, out var romCandidates) && (link is null || !MixrRuntimeState.EspConnected))
            {
                Log($"FW: Espressif-COM schon da ({string.Join(", ", romCandidates)}) — flashe mit esptool.");
                await EsptoolFlasher.EnsureAvailableAsync(progress, Log, ct).ConfigureAwait(false);
                using (MixrRuntimeState.PauseSerial())
                    return await EsptoolFlasher.FlashAsync(img, romPort!, progress, Log, ct, alreadyInBootloader: true)
                        .ConfigureAwait(false);
            }

            // 1) Protokoll-Update über offenen Link (HID/Seriell) — bevorzugter Feld-Pfad
            if (link is not null && link.Link.IsOpen)
            {
                Log($"FW: Protokoll-Update {dev?.FirmwareVersion ?? "?"} → {img.Version} über {link.Link.Id}" +
                    (dev?.SupportsProtocolOta == true ? " (Gerät meldet OTA/Staging)" : " (Versuch)"));
                progress?.Report(new FirmwareUpdateProgress(0, "Firmware wird über USB übertragen …"));
                var svc = new FirmwareUpdateService(link.Link, link.Dispatcher, Log);
                var r = await svc.UpdateAsync(img, progress, ct).ConfigureAwait(false);
                if (r.Outcome == FirmwareUpdateOutcome.Success)
                {
                    Log("FW: Protokoll meldet OK — prüfe, ob das Gerät wirklich die neue Version bootet …");
                    progress?.Report(new FirmwareUpdateProgress(100, "Gerät startet neu, prüfe Version …"));
                    // Link stirbt beim Reboot; Host-Loop baut neu auf. Wir warten auf HELLO.
                    try { link.Link.Dispose(); } catch { /* */ }
                    var verified = await WaitForFirmwareVersionAsync(img.Version, TimeSpan.FromSeconds(20), ct)
                        .ConfigureAwait(false);
                    if (verified)
                    {
                        Log($"FW: Gerät meldet Firmware {img.Version} — Update bestätigt.");
                        return r;
                    }

                    Log($"FW: Gerät bootet noch nicht {img.Version} — Fallback Download-Modus.");
                    // frischen Link für Bootloader-Fallback holen (Host-Loop kann schon verbunden haben)
                    link = MixrRuntimeState.Link;
                }
                else if (r.Outcome is FirmwareUpdateOutcome.Cancelled or FirmwareUpdateOutcome.Failed)
                {
                    return r;
                }
                else
                {
                    Log($"FW: Protokoll-Update nicht möglich ({r.Message}) — Fallback Download-Modus.");
                }
            }

            // 2) Fallback: ENTER_BOOTLOADER + esptool (Retries)
            await EsptoolFlasher.EnsureAvailableAsync(progress, Log, ct).ConfigureAwait(false);

            using (MixrRuntimeState.PauseSerial())
            {
                link ??= MixrRuntimeState.Link;
                if (link is { Kind: MixrLinkKind.Hid } || MixrHidTransport.Enumerate().Count > 0)
                {
                    var flashed = await TryBootloaderFlashAsync(img, link, progress, ct).ConfigureAwait(false);
                    if (flashed is not null)
                        return flashed;
                    return new FirmwareUpdateResult(FirmwareUpdateOutcome.Failed, ManualBootHint);
                }

                var port = MixrRuntimeState.LastPortName ?? MixrDevicePortResolver.TryFindComPort(out _);
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

    static async Task<FirmwareUpdateResult?> TryBootloaderFlashAsync(
        FirmwareImage img,
        DeviceLink? link,
        IProgress<FirmwareUpdateProgress>? progress,
        CancellationToken ct)
    {
        const int maxAttempts = 3;
        var usedExisting = false;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new FirmwareUpdateProgress(0, $"Download-Modus (Versuch {attempt}/{maxAttempts}) …"));
            Log($"FW: ENTER_BOOTLOADER Versuch {attempt}/{maxAttempts}");

            var portsBefore = new HashSet<string>(MixrDevicePortResolver.FindAnyEspressifCom(), StringComparer.OrdinalIgnoreCase);

            IMixrLink? opener = null;
            try
            {
                if (!usedExisting && link is { Link.IsOpen: true })
                {
                    usedExisting = true;
                    opener = link.Link;
                    opener.TrySend(MixrProtocol.TypeEnterBootloader);
                }
                else
                {
                    opener = MixrHidTransport.TryOpen(log: s => Log(s));
                    if (opener is null)
                    {
                        Log("FW: HID für Retry nicht erreichbar");
                        await Task.Delay(1200, ct).ConfigureAwait(false);
                        continue;
                    }

                    opener.TrySend(MixrProtocol.TypeEnterBootloader);
                }

                await Task.Delay(500, ct).ConfigureAwait(false);
            }
            finally
            {
                try { opener?.Dispose(); } catch { /* */ }
            }

            var port = await WaitForRomPortAsync(TimeSpan.FromSeconds(18), portsBefore, ct).ConfigureAwait(false);
            if (port is null)
            {
                Log("FW: kein Espressif-COM — nächster Versuch …");
                await Task.Delay(1500, ct).ConfigureAwait(false);
                continue;
            }

            Log($"FW: ROM-Bootloader auf {port}");
            return await EsptoolFlasher.FlashAsync(img, port, progress, Log, ct, alreadyInBootloader: true)
                .ConfigureAwait(false);
        }

        return null;
    }

    static async Task<bool> WaitForFirmwareVersionAsync(string expectedVersion, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var d = MixrRuntimeState.Device;
            if (MixrRuntimeState.EspConnected && d is not null
                && string.Equals(d.FirmwareVersion, expectedVersion, StringComparison.OrdinalIgnoreCase))
                return true;
            await Task.Delay(400, ct).ConfigureAwait(false);
        }

        var last = MixrRuntimeState.Device?.FirmwareVersion ?? "?";
        Log($"FW: Version nach Timeout: Gerät meldet „{last}“, erwartet „{expectedVersion}“");
        return false;
    }

    static bool TryFindRomPort(out string? port, out IReadOnlyList<string> candidates)
    {
        port = MixrDevicePortResolver.TryFindComPort(out candidates);
        return !string.IsNullOrEmpty(port);
    }

    static async Task<string?> WaitForRomPortAsync(TimeSpan timeout, HashSet<string> portsBefore, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        var lastLog = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var now = MixrDevicePortResolver.FindAnyEspressifCom();
            // Bevorzugt: neu erschienener Port
            var fresh = now.FirstOrDefault(p => !portsBefore.Contains(p));
            if (!string.IsNullOrEmpty(fresh))
            {
                Log($"FW: neuer COM {fresh}");
                await Task.Delay(800, ct).ConfigureAwait(false);
                return fresh;
            }

            if (now.Count > 0)
            {
                Log($"FW: COM vorhanden ({string.Join(", ", now)})");
                await Task.Delay(800, ct).ConfigureAwait(false);
                return now[0];
            }

            if ((DateTime.UtcNow - lastLog).TotalSeconds >= 3)
            {
                lastLog = DateTime.UtcNow;
                var hid = MixrHidTransport.Enumerate().Count > 0;
                Log(hid ? "FW: warte … HID noch/wieder da, kein COM" : "FW: warte … kein HID, kein COM");
            }

            await Task.Delay(350, ct).ConfigureAwait(false);
        }

        return null;
    }
}
