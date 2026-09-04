using System.Net.Http.Headers;
using System.Text.Json;
using Mixr.Services;

namespace Mixr.FirstFlash;

public sealed record FactoryFirmware(
    string Version,
    string Source,
    string AppPath,
    string BootloaderPath,
    string PartitionTablePath);

/// <summary>
/// Findet oder lädt Bootloader + Partitionstabelle + Mixr.bin (Standard-Release).
/// </summary>
static class FirmwareBundle
{
    public const string DefaultGitHubRepo = "paulodermax/Mixr";

    /// <summary>Produkt-Firmware, die FirstFlash ohne Extra-Flags schreibt.</summary>
    public const string DefaultFirmwareVersion = "0.0.7";

    public static async Task<FactoryFirmware> ResolveAsync(
        string? dirArg,
        string? versionArg,
        string repo,
        bool useLocal,
        Action<string> log,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(dirArg))
        {
            var fromDir = TryFromDirectory(Path.GetFullPath(dirArg.Trim()), "—dir");
            if (fromDir is not null)
                return fromDir;
            throw new FileNotFoundException(
                $"In „{dirArg}“ fehlen Mixr.bin, bootloader.bin oder partition-table.bin.");
        }

        // Standard: GitHub v0.0.7 (HID). --local: ESP/build, ignoriert --version.
        if (!useLocal)
            return await DownloadFromGitHubAsync(repo, versionArg, log, ct).ConfigureAwait(false);

        var local = TryFromDirectory(Path.Combine(AppContext.BaseDirectory, "firmware"), "neben dem Tool")
                    ?? TryFromRepoTree();
        if (local is not null)
        {
            log($"Firmware lokal: {local.Version} ({local.Source})");
            return local;
        }

        log("Kein lokaler Build — lade GitHub-Release.");
        return await DownloadFromGitHubAsync(repo, versionArg, log, ct).ConfigureAwait(false);
    }

    static FactoryFirmware? TryFromRepoTree()
    {
        var root = FindRepoRoot();
        if (root is null)
            return null;

        var idf = Path.Combine(root, "ESP", "build");
        var appFirmware = Path.Combine(root, "Mixr.App", "firmware");

        var app = FirstExisting(
            Path.Combine(idf, "Mixr.bin"),
            Path.Combine(appFirmware, "Mixr.bin"));
        var boot = FirstExisting(
            Path.Combine(idf, "bootloader", "bootloader.bin"),
            Path.Combine(appFirmware, "bootloader.bin"));
        var table = FirstExisting(
            Path.Combine(idf, "partition_table", "partition-table.bin"),
            Path.Combine(appFirmware, "partition-table.bin"));

        if (app is null || boot is null || table is null)
            return null;

        var img = FirmwareImage.Load(app);
        return new FactoryFirmware(img.Version, "ESP/build bzw. Mixr.App/firmware", app, boot, table);
    }

    static FactoryFirmware? TryFromDirectory(string dir, string source)
    {
        if (!Directory.Exists(dir))
            return null;

        var app = FirstExisting(
            Path.Combine(dir, "Mixr.bin"),
            Path.Combine(dir, "mixr.bin"));
        var boot = FindNamed(dir, "bootloader.bin");
        var table = FindNamed(dir, "partition-table.bin", "partition_table.bin");
        if (app is null || boot is null || table is null)
            return null;

        var img = FirmwareImage.Load(app);
        return new FactoryFirmware(img.Version, source, app, boot, table);
    }

    static async Task<FactoryFirmware> DownloadFromGitHubAsync(
        string repo,
        string? versionArg,
        Action<string> log,
        CancellationToken ct)
    {
        using var http = NewGitHubClient();
        var tag = string.IsNullOrWhiteSpace(versionArg)
            ? null
            : versionArg.Trim().TrimStart('v', 'V');
        var url = tag is null
            ? $"https://api.github.com/repos/{repo}/releases/latest"
            : $"https://api.github.com/repos/{repo}/releases/tags/v{tag}";

        log($"Lade Release von GitHub ({repo}" + (tag is null ? ", latest" : $", v{tag}") + ") …");
        using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        var releaseTag = doc.RootElement.GetProperty("tag_name").GetString()
                         ?? throw new InvalidDataException("GitHub-Release ohne tag_name.");
        var cacheDir = Path.Combine(MixrConfigPaths.DataRoot, "firmware", releaseTag);
        Directory.CreateDirectory(cacheDir);

        var needed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Mixr.bin"] = Path.Combine(cacheDir, "Mixr.bin"),
            ["bootloader.bin"] = Path.Combine(cacheDir, "bootloader.bin"),
            ["partition-table.bin"] = Path.Combine(cacheDir, "partition-table.bin"),
        };

        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            if (name is null || !needed.TryGetValue(name, out var dest))
                continue;
            if (File.Exists(dest) && new FileInfo(dest).Length > 0)
                continue;

            var dl = asset.GetProperty("browser_download_url").GetString()
                     ?? throw new InvalidDataException($"Asset {name} ohne Download-URL.");
            log($"  {name} …");
            using var bin = await http.GetAsync(dl, ct).ConfigureAwait(false);
            bin.EnsureSuccessStatusCode();
            await using var src = await bin.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = File.Create(dest);
            await src.CopyToAsync(dst, ct).ConfigureAwait(false);
        }

        foreach (var (name, path) in needed)
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                throw new FileNotFoundException($"Release {releaseTag} enthält „{name}“ nicht (oder Download fehlgeschlagen).");
        }

        var img = FirmwareImage.Load(needed["Mixr.bin"]);
        log($"Firmware {img.Version} ({releaseTag}) nach {cacheDir}");
        return new FactoryFirmware(
            img.Version,
            $"GitHub {releaseTag}",
            needed["Mixr.bin"],
            needed["bootloader.bin"],
            needed["partition-table.bin"]);
    }

    static HttpClient NewGitHubClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Mixr.FirstFlash", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Mixr.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        dir = new DirectoryInfo(Environment.CurrentDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Mixr.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }

    static string? FirstExisting(params string[] paths) =>
        paths.FirstOrDefault(File.Exists);

    static string? FindNamed(string dir, params string[] names)
    {
        foreach (var n in names)
        {
            var p = Path.Combine(dir, n);
            if (File.Exists(p))
                return p;
        }

        foreach (var file in Directory.EnumerateFiles(dir, "*.bin", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (names.Any(n => name.Equals(n, StringComparison.OrdinalIgnoreCase)))
                return file;
        }

        return null;
    }
}
