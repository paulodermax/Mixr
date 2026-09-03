using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Mixr.Services;

/// <summary>
/// ESP-IDF App-Image (<c>Mixr.bin</c>): liest die Versionsangabe aus <c>esp_app_desc_t</c>
/// (Image-Header 24 B + erster Segment-Header 8 B → Deskriptor bei 0x20; <c>version[32]</c> bei +0x10).
/// </summary>
public sealed class FirmwareImage
{
    const uint AppDescMagic = 0xABCD5432;
    const int AppDescOffset = 0x20;
    const int VersionOffsetInDesc = 0x10;
    const int ProjectNameOffsetInDesc = 0x30;

    public string Path { get; }
    public byte[] Bytes { get; }
    public string Version { get; }
    public string ProjectName { get; }
    public byte[] Sha256 { get; }

    FirmwareImage(string path, byte[] bytes, string version, string projectName, byte[] sha)
    {
        Path = path;
        Bytes = bytes;
        Version = version;
        ProjectName = projectName;
        Sha256 = sha;
    }

    public static FirmwareImage Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < AppDescOffset + 0x100)
            throw new InvalidDataException("Datei zu klein für ein ESP-IDF App-Image.");
        if (bytes[0] != 0xE9)
            throw new InvalidDataException("Kein ESP-Image (Magic 0xE9 fehlt).");

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(AppDescOffset, 4));
        if (magic != AppDescMagic)
            throw new InvalidDataException("esp_app_desc_t nicht gefunden (Magic).");

        var version = ReadCString(bytes, AppDescOffset + VersionOffsetInDesc, 32);
        var project = ReadCString(bytes, AppDescOffset + ProjectNameOffsetInDesc, 32);
        return new FirmwareImage(path, bytes, version, project, SHA256.HashData(bytes));
    }

    /// <summary>Mitgeliefertes Image aus <see cref="MixrConfigPaths.BundledFirmwareDir"/> oder <c>null</c>.</summary>
    public static FirmwareImage? TryLoadBundled(Action<string>? log = null)
    {
        try
        {
            var dir = MixrConfigPaths.BundledFirmwareDir;
            if (!Directory.Exists(dir))
                return null;
            var file = Directory.EnumerateFiles(dir, "*.bin").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (file is null)
                return null;
            var img = Load(file);
            if (!img.ProjectName.Equals("Mixr", StringComparison.OrdinalIgnoreCase))
            {
                log?.Invoke($"Firmware-Image {System.IO.Path.GetFileName(file)}: Projektname „{img.ProjectName}“ ≠ Mixr — ignoriert.");
                return null;
            }
            return img;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Bundled firmware nicht lesbar: {ex.Message}");
            return null;
        }
    }

    static string ReadCString(byte[] bytes, int offset, int max)
    {
        int end = offset;
        while (end < offset + max && end < bytes.Length && bytes[end] != 0)
            end++;
        return Encoding.UTF8.GetString(bytes, offset, end - offset);
    }

    /// <summary>
    /// Vergleicht Versionsstrings wie „1.2.3“, „v1.2.3“, „1.2.3-5-gabcdef“ (git describe) nach dem numerischen Präfix.
    /// Nicht-numerische Angaben (reine Hashes aus Dev-Builds) gelten immer als „anders“.
    /// </summary>
    public static bool IsNewerThan(string candidate, string? installed)
    {
        if (string.IsNullOrWhiteSpace(installed))
            return true;
        var a = ParseNumeric(candidate);
        var b = ParseNumeric(installed);
        if (a is null || b is null)
            return !string.Equals(candidate, installed, StringComparison.OrdinalIgnoreCase);
        return a.CompareTo(b) > 0;
    }

    static System.Version? ParseNumeric(string s)
    {
        var t = s.Trim().TrimStart('v', 'V');
        int end = 0;
        while (end < t.Length && (char.IsDigit(t[end]) || t[end] == '.'))
            end++;
        var head = t[..end].Trim('.');
        if (head.Length == 0 || !head.Contains('.'))
            return null;
        return System.Version.TryParse(head, out var v) ? v : null;
    }
}
