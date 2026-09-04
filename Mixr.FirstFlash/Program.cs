using System.Diagnostics;
using Mixr.FirstFlash;
using Mixr.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (HasFlag(args, "--help", "-h", "/?"))
{
    Console.WriteLine(
        """
        Mixr.FirstFlash — einmalig die Standard-Mixr-Firmware auf ein neues ESP32-S3-Display schreiben.

        Schreibt Bootloader + Partitionstabelle + Mixr.bin (aktuelles GitHub-Release, sonst lokaler Build).
        Danach das Gerät per USB lassen und weitere Updates in der Mixr-App
        (Einstellungen → Geräte-Firmware) machen.

        Vorher die Mixr-App beenden — sonst ist der COM-Port belegt.

        Aufruf:
          dotnet run --project Mixr.FirstFlash
          Mixr.FirstFlash.exe
          Mixr.FirstFlash.exe --port COM8
          Mixr.FirstFlash.exe --version 0.0.7
          Mixr.FirstFlash.exe --dir C:\pfad\zu\firmware
          Mixr.FirstFlash.exe --yes
          Mixr.FirstFlash.exe --already-bootloader

        Optionen:
          --port COM8              fester Port (sonst Espressif-USB automatisch)
          --dir PFAD               Mixr.bin + bootloader.bin + partition-table.bin
          --version X.Y.Z          GitHub-Release statt „latest“
          --repo owner/name        GitHub-Repo (Standard: paulodermax/Mixr)
          --yes                    ohne Rückfrage flashen
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

var yes = HasFlag(args, "--yes", "-y");
var alreadyBl = HasFlag(args, "--already-bootloader");
var portArg = ArgValue(args, "--port");
var dirArg = ArgValue(args, "--dir");
var versionArg = ArgValue(args, "--version");
var repo = ArgValue(args, "--repo") ?? FirmwareBundle.DefaultGitHubRepo;

void Log(string line) => Console.WriteLine(line);

try
{
    var mixrApps = Process.GetProcessesByName("Mixr");
    if (mixrApps.Length > 0)
    {
        Console.Error.WriteLine("Mixr.exe läuft noch und hält oft den USB-Port fest.");
        Console.Error.WriteLine("Bitte die App beenden und FirstFlash erneut starten.");
        return 2;
    }

    Log("Lade Standard-Firmware …");
    var fw = await FirmwareBundle.ResolveAsync(dirArg, versionArg, repo, Log, cts.Token);

    var port = portArg;
    if (string.IsNullOrWhiteSpace(port))
        port = MixrDevicePortResolver.TryFindComPort(out var found);

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

    if (!yes)
    {
        Console.Write("Enter = flashen, Strg+C = abbrechen: ");
        if (Console.ReadLine() is null)
            return 0;
    }

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
