using System.Diagnostics;
using Mixr.FirstFlash;
using Mixr.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (HasFlag(args, "--help", "-h", "/?"))
{
    Console.WriteLine(
        """
        Mixr.FirstFlash — einmalig Mixr 0.0.7 (HID) auf ein neues ESP32-S3-Display schreiben.

        Standard: GitHub v0.0.7, COM-Port automatisch, Mixr-App wird beendet, sofort flashen.

        Aufruf:
          dotnet run --project Mixr.FirstFlash

        Optionen:
          --port COM8              fester Port (sonst Espressif-USB automatisch)
          --dir PFAD               Mixr.bin + bootloader.bin + partition-table.bin
          --version X.Y.Z          anderes GitHub-Release (Standard: 0.0.7)
          --local                  ESP/build bzw. Mixr.App/firmware statt GitHub
          --repo owner/name        GitHub-Repo (Standard: paulodermax/Mixr)
          --confirm                vor dem Flashen auf Enter warten
          --already-bootloader     Gerät steckt schon im Download-Modus (BOOT halten, RESET)
          --help
        """);
    return 0;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var confirm = HasFlag(args, "--confirm");
var alreadyBl = HasFlag(args, "--already-bootloader");
var useLocal = HasFlag(args, "--local");
var portArg = ArgValue(args, "--port");
var dirArg = ArgValue(args, "--dir");
var versionArg = ArgValue(args, "--version") ?? FirmwareBundle.DefaultFirmwareVersion;
var repo = ArgValue(args, "--repo") ?? FirmwareBundle.DefaultGitHubRepo;

void Log(string line) => Console.WriteLine(line);

try
{
    var mixrApps = Process.GetProcessesByName("Mixr");
    if (mixrApps.Length > 0)
    {
        Log($"Beende Mixr.exe ({mixrApps.Length}), damit der COM-Port frei ist …");
        foreach (var p in mixrApps)
        {
            try
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(4000);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Mixr.exe ließ sich nicht beenden: {ex.Message}");
                return 2;
            }
        }

        await Task.Delay(1500, cts.Token);
    }

    Log($"Lade Mixr-Firmware {versionArg} …");
    var fw = await FirmwareBundle.ResolveAsync(dirArg, versionArg, repo, useLocal, Log, cts.Token);

    var port = portArg;
    if (string.IsNullOrWhiteSpace(port))
    {
        for (var i = 0; i < 8 && string.IsNullOrWhiteSpace(port); i++)
        {
            port = MixrDevicePortResolver.TryFindComPort(out _);
            if (!string.IsNullOrWhiteSpace(port))
                break;
            if (i == 0)
                Log("Warte auf Espressif-COM-Port …");
            await Task.Delay(500, cts.Token);
        }
    }

    if (string.IsNullOrWhiteSpace(port))
    {
        Console.Error.WriteLine("Kein Espressif-COM-Port gefunden (VID 303A).");
        Console.Error.WriteLine("USB-Kabel prüfen. Mixr-App darf nicht laufen.");
        Console.Error.WriteLine("Falls das Board nur im Download-Modus erscheint:");
        Console.Error.WriteLine("  BOOT halten → RESET tippen → BOOT loslassen, dann erneut starten.");
        return 3;
    }

    Log("");
    Log($"Port:      {port}");
    Log($"Firmware:  Mixr {fw.Version}");
    Log($"Quelle:    {fw.Source}");
    Log($"App:       {fw.AppPath}");
    Log($"Boot:      {fw.BootloaderPath}");
    Log($"Tabelle:   {fw.PartitionTablePath}");
    Log("");
    Log("Es wird Bootloader + Partitionstabelle + App geschrieben (Werksboard → Mixr).");
    Log("USB nicht trennen.");

    if (confirm)
    {
        Console.Write("Enter = flashen, Strg+C = abbrechen: ");
        if (Console.ReadLine() is null)
            return 0;
    }

    Log(useLocal || !string.IsNullOrEmpty(dirArg)
        ? "Starte Flash …"
        : $"Starte Flash (GitHub v{versionArg}) …");

    var progress = new Progress<FirmwareUpdateProgress>(p =>
    {
        Console.Write($"\r{p.Percent,3} %  {p.Stage,-60}");
    });

    await EsptoolFlasher.EnsureAvailableAsync(progress, Log, cts.Token);
    Console.WriteLine();

    var result = await EsptoolFlasher.FlashFactoryAsync(
        port,
        fw.BootloaderPath,
        fw.PartitionTablePath,
        FirmwareImage.Load(fw.AppPath),
        progress,
        Log,
        cts.Token,
        alreadyInBootloader: alreadyBl);

    Console.WriteLine();
    Console.WriteLine(result.Message);

    if (result.Outcome != FirmwareUpdateOutcome.Success)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("Wenn „Failed to connect“ / Timeout:");
        Console.Error.WriteLine("  1. Mixr-App zu");
        Console.Error.WriteLine("  2. BOOT halten → RESET tippen → BOOT loslassen");
        Console.Error.WriteLine("  3. Mixr.FirstFlash --already-bootloader");
        return 1;
    }

    Log("");
    Log($"Fertig. Das Display startet mit Mixr {fw.Version} (HID).");
    Log("Ab jetzt: Mixr-App öffnen — spätere Firmware kommt über Einstellungen → Geräte-Firmware.");
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Abgebrochen.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static bool HasFlag(string[] argv, params string[] names) =>
    argv.Any(a => names.Contains(a, StringComparer.OrdinalIgnoreCase));

static string? ArgValue(string[] argv, string name)
{
    for (var i = 0; i < argv.Length - 1; i++)
    {
        if (argv[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            return argv[i + 1];
        if (argv[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            return argv[i][(name.Length + 1)..];
    }

    return null;
}
