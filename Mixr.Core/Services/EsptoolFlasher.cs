using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Mixr.Services;

/// <summary>
/// Fallback für Geräte ohne OTA-Partition (2-MiB-Flash): flasht das App-Image über den ROM-Download-Modus
/// des ESP32-S3 (USB-Serial/JTAG) mit dem offiziellen <c>esptool</c>. Die Windows-Binary (~43 MB) wird
/// nicht mit der App ausgeliefert, sondern bei Bedarf einmalig nach <c>%LOCALAPPDATA%\Mixr\tools</c> geladen
/// und per SHA-256 geprüft.
/// </summary>
public static class EsptoolFlasher
{
    public const string EsptoolVersion = "4.9.0";
    const string DownloadUrl = "https://github.com/espressif/esptool/releases/download/v4.9.0/esptool-v4.9.0-windows-amd64.zip";
    const string ZipSha256 = "8e66e686341eddf8c56f4df77288098680c6251e6d98750992798a5f4e87354b";

    /// <summary>App-Offset laut partitions.csv / flasher_args.json.</summary>
    public const string AppFlashOffset = "0x10000";

    static readonly Regex PercentRx = new(@"\((\d{1,3})\s*%\)", RegexOptions.Compiled);

    public static string ToolsDir => Path.Combine(MixrConfigPaths.DataRoot, "tools", "esptool-" + EsptoolVersion);

    public static string ExePath => Path.Combine(ToolsDir, "esptool.exe");

    public static bool IsAvailable => File.Exists(ExePath);

    /// <summary>Lädt esptool herunter (falls nötig) und verifiziert die Prüfsumme.</summary>
    public static async Task EnsureAvailableAsync(IProgress<FirmwareUpdateProgress>? progress, Action<string> log, CancellationToken ct)
    {
        if (IsAvailable)
            return;

        Directory.CreateDirectory(ToolsDir);
        var zipPath = Path.Combine(ToolsDir, "esptool.zip");
        progress?.Report(new FirmwareUpdateProgress(0, "Lade Flash-Werkzeug (esptool) herunter …"));

        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromMinutes(5);
        using (var resp = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1L;
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = File.Create(zipPath);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                done += n;
                if (total > 0)
                    progress?.Report(new FirmwareUpdateProgress((int)(done * 100 / total), $"Lade esptool … {done / 1_048_576} / {total / 1_048_576} MB"));
            }
        }

        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(zipPath, ct).ConfigureAwait(false)));
        if (!hash.Equals(ZipSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(zipPath);
            throw new InvalidDataException("esptool-Download: SHA-256 stimmt nicht — Datei verworfen.");
        }

        progress?.Report(new FirmwareUpdateProgress(100, "Entpacke esptool …"));
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;
                // Archiv enthält einen Unterordner (esptool-win64/…) — flach entpacken.
                var target = Path.Combine(ToolsDir, entry.Name);
                entry.ExtractToFile(target, overwrite: true);
            }
        }
        File.Delete(zipPath);

        if (!IsAvailable)
            throw new FileNotFoundException("esptool.exe nach dem Entpacken nicht gefunden.", ExePath);
        log($"esptool {EsptoolVersion} nach {ToolsDir} installiert.");
    }

    /// <summary>
    /// Flasht <paramref name="image"/> auf <paramref name="portName"/>. Der Port darf von niemandem sonst offen sein.
    /// </summary>
    public static async Task<FirmwareUpdateResult> FlashAsync(
        FirmwareImage image,
        string portName,
        IProgress<FirmwareUpdateProgress>? progress,
        Action<string> log,
        CancellationToken ct)
    {
        if (!IsAvailable)
            return new FirmwareUpdateResult(FirmwareUpdateOutcome.Failed, "esptool ist nicht verfügbar.");

        var psi = new ProcessStartInfo
        {
            FileName = ExePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[]
                 {
                     "--chip", "esp32s3",
                     "--port", portName,
                     "--baud", "921600",
                     "--before", "default_reset",
                     "--after", "hard_reset",
                     "write_flash",
                     "--flash_mode", "keep",
                     "--flash_size", "keep",
                     "--flash_freq", "keep",
                     AppFlashOffset, image.Path,
                 })
            psi.ArgumentList.Add(a);

        progress?.Report(new FirmwareUpdateProgress(0, "Gerät wird in den Download-Modus versetzt …"));
        log($"esptool: write_flash {AppFlashOffset} {Path.GetFileName(image.Path)} @ {portName}");

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var tail = new System.Collections.Concurrent.ConcurrentQueue<string>();

        void OnLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            tail.Enqueue(line);
            while (tail.Count > 12 && tail.TryDequeue(out _)) { }
            var m = PercentRx.Match(line);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var pct))
                progress?.Report(new FirmwareUpdateProgress(Math.Clamp(pct, 0, 100), "Schreibe Firmware …"));
            else if (line.Contains("Connecting", StringComparison.OrdinalIgnoreCase))
                progress?.Report(new FirmwareUpdateProgress(0, "Verbinde mit Bootloader …"));
            else if (line.Contains("Hash of data verified", StringComparison.OrdinalIgnoreCase))
                progress?.Report(new FirmwareUpdateProgress(100, "Geschrieben und verifiziert, Neustart …"));
        }

        proc.OutputDataReceived += (_, e) => OnLine(e.Data);
        proc.ErrorDataReceived += (_, e) => OnLine(e.Data);

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            return new FirmwareUpdateResult(FirmwareUpdateOutcome.Cancelled, "Abgebrochen — Gerät ggf. neu anstecken.");
        }

        var lastLines = string.Join(Environment.NewLine, tail);
        log("esptool: " + lastLines.Replace(Environment.NewLine, " | "));

        return proc.ExitCode == 0
            ? new FirmwareUpdateResult(FirmwareUpdateOutcome.Success, $"Firmware {image.Version} geflasht. Das Gerät startet neu.")
            : new FirmwareUpdateResult(FirmwareUpdateOutcome.Failed, $"esptool Exit {proc.ExitCode}: {lastLines}");
    }
}
