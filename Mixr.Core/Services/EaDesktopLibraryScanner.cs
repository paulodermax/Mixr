using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mixr.Services;

/// <summary>
/// EA-Desktop-/EA-App-Installationen aus der verschlüsselten Datei
/// <c>%ProgramData%\EA Desktop\530c11479fe252fc5aabc24935b9776d4900eb3ba58fdc271e0d6229413ad40e\IS</c>
/// (AES + SHA3-256, vgl. GameFinder EADesktop).
/// </summary>
public static class EaDesktopLibraryScanner
{
    const string AllUsersFolderName = "530c11479fe252fc5aabc24935b9776d4900eb3ba58fdc271e0d6229413ad40e";

    public readonly record struct EaInstalledGame(string StableKey, string DisplayName);

    public static IReadOnlyList<EaInstalledGame> ScanInstalledGames()
    {
        try
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var path = Path.Combine(programData, "EA Desktop", AllUsersFolderName, "IS");
            if (!File.Exists(path))
                return Array.Empty<EaInstalledGame>();

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 80)
                return Array.Empty<EaInstalledGame>();

            var plain = EaDesktopCrypto.TryDecryptInstallInfo(bytes);
            if (string.IsNullOrWhiteSpace(plain))
                return Array.Empty<EaInstalledGame>();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var root = JsonSerializer.Deserialize<EaInstallInfoFileRoot>(plain, options);
            if (root?.InstallInfos is not { Count: > 0 })
                return Array.Empty<EaInstalledGame>();

            var list = new List<EaInstalledGame>();
            foreach (var row in root.InstallInfos)
            {
                if (string.IsNullOrWhiteSpace(row.SoftwareId) ||
                    string.IsNullOrWhiteSpace(row.BaseInstallPath))
                    continue;

                var name = SlugToTitle(row.BaseSlug) ?? row.SoftwareId;
                list.Add(new EaInstalledGame(row.SoftwareId.Trim(), name));
            }

            return list
                .GroupBy(x => x.StableKey, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<EaInstalledGame>();
        }
    }

    static string? SlugToTitle(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;
        var parts = slug.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return null;
        var text = CultureInfo.InvariantCulture.TextInfo;
        return string.Join(' ', parts.Select(p => text.ToTitleCase(p.ToLowerInvariant())));
    }
}

internal sealed record EaInstallInfoFileRoot(
    [property: JsonPropertyName("installInfos")] List<EaInstallInfoRow>? InstallInfos,
    [property: JsonPropertyName("schema")] EaSchemaRow? Schema);

internal sealed record EaSchemaRow([property: JsonPropertyName("version")] int Version);

internal sealed record EaInstallInfoRow(
    [property: JsonPropertyName("softwareId")] string? SoftwareId,
    [property: JsonPropertyName("baseSlug")] string? BaseSlug,
    [property: JsonPropertyName("baseInstallPath")] string? BaseInstallPath);

[SupportedOSPlatform("windows")]
internal static class EaDesktopCrypto
{
    const string AllUsersGenericId = "allUsersGenericId";
    const string IsMarker = "IS";

    static readonly byte[] PrecomputedIv =
    [
        0x84, 0xef, 0xc4, 0xb8, 0x36, 0x11, 0x9c, 0x20, 0x41, 0x93, 0x98, 0xc3, 0xf3, 0xf2, 0xbc, 0xef,
    ];

    internal static string? TryDecryptInstallInfo(byte[] fileContents)
    {
        try
        {
            var hw = EaHardwareInfo.BuildHardwareString();
            var hardwareHash = ToHexLower(SHA1.HashData(Encoding.ASCII.GetBytes(hw)));
            var hashInput = AllUsersGenericId + IsMarker + hardwareHash;
            var key = SHA3_256.HashData(Encoding.ASCII.GetBytes(hashInput));
            var iv = PrecomputedIv;

            using var cipherStream = new MemoryStream(fileContents, 64, fileContents.Length - 64, writable: false);
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Key = key;
            aes.IV = iv;
            using var decryptor = aes.CreateDecryptor(key, iv);
            using var cryptoStream = new CryptoStream(cipherStream, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cryptoStream, Encoding.UTF8);
            return sr.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    static string ToHexLower(byte[] data) =>
        Convert.ToHexString(data).ToLowerInvariant();
}

[SupportedOSPlatform("windows")]
internal static class EaHardwareInfo
{
    internal static string BuildHardwareString()
    {
        var sb = new StringBuilder();
        sb.Append(WmiString("Win32_BaseBoard", "Manufacturer"));
        sb.Append(';');
        sb.Append(WmiString("Win32_BaseBoard", "SerialNumber"));
        sb.Append(';');
        sb.Append(WmiString("Win32_BIOS", "Manufacturer"));
        sb.Append(';');
        sb.Append(WmiString("Win32_BIOS", "SerialNumber"));
        sb.Append(';');
        sb.Append(GetVolumeSerialHex());
        sb.Append(';');
        sb.Append(WmiString("Win32_VideoController", "PNPDeviceID"));
        sb.Append(';');
        sb.Append(WmiString("Win32_Processor", "Manufacturer"));
        sb.Append(';');
        sb.Append(WmiString("Win32_Processor", "ProcessorId"));
        sb.Append(';');
        sb.Append(WmiString("Win32_Processor", "Name"));
        sb.Append(';');
        return sb.ToString();
    }

    static string GetVolumeSerialHex()
    {
        try
        {
            if (EaNative.GetVolumeInformationW(
                    "C:\\",
                    null!,
                    0,
                    out var serial,
                    out _,
                    out _,
                    null!,
                    0))
                return serial.ToString("X", CultureInfo.InvariantCulture);
        }
        catch
        {
            /* */
        }

        return "";
    }

    static string WmiString(string className, string propertyName)
    {
        try
        {
            var query = new SelectQuery($"SELECT {propertyName} FROM {className}");
            using var searcher = new ManagementObjectSearcher(query);
            using var results = searcher.Get();
            foreach (ManagementBaseObject o in results)
            {
                if (o.Properties[propertyName].Value is string s)
                    return s;
                break;
            }
        }
        catch
        {
            /* */
        }

        return "";
    }
}

[SupportedOSPlatform("windows")]
internal static class EaNative
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool GetVolumeInformationW(
        string rootPathName,
        string? volumeNameBuffer,
        int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        string? fileSystemNameBuffer,
        int nFileSystemNameSize);
}
