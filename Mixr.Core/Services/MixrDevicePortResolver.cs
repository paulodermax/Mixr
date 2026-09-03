using System.Management;
using System.Text.RegularExpressions;

namespace Mixr.Services;

/// <summary>
/// Findet COM-Ports von Espressif-USB-Geräten (ROM-Download / USB-Serial/JTAG).
/// Nach ENTER_BOOTLOADER aus HID-Firmware kann der Port als 303A:1001 oder als anderer
/// Espressif-CDC-Port erscheinen — deshalb suchen wir alle VID 0x303A mit COM-Namen.
/// </summary>
public static class MixrDevicePortResolver
{
    /// <summary>Espressif Systems — wie ESP-IDF / ESP32-S3 USB Serial/JTAG.</summary>
    public const ushort EspressifVid = 0x303A;

    /// <summary>ESP32-S3 integrierter USB Serial/JTAG (Standard-Deskriptor).</summary>
    public const ushort Esp32S3UsbSerialJtagPid = 0x1001;

    static readonly Regex ComInName = new(@"\b(COM\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <param name="candidates">Alle gefundenen COM-Namen (sortiert nach COM-Nummer).</param>
    public static string? TryFindComPort(out IReadOnlyList<string> candidates, ushort vid = EspressifVid, ushort pid = Esp32S3UsbSerialJtagPid)
    {
        // 1) Exakte VID/PID (klassischer USB-Serial/JTAG)
        var exact = FindByVidPid(vid, pid);
        if (exact.Count > 0)
        {
            candidates = exact;
            return exact[0];
        }

        // 2) Jeder Espressif-COM-Port (ROM-CDC nach TinyUSB-Download-Modus)
        if (vid == EspressifVid)
        {
            var any = FindAnyEspressifCom();
            if (any.Count > 0)
            {
                candidates = any;
                return any[0];
            }
        }

        candidates = Array.Empty<string>();
        return null;
    }

    /// <summary>Alle Espressif-(0x303A)-COM-Ports, unabhängig von der PID.</summary>
    public static IReadOnlyList<string> FindAnyEspressifCom()
    {
        var raw = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                $"SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE '%VID_{EspressifVid:X4}%'");

            using var coll = searcher.Get();
            foreach (ManagementObject o in coll)
            {
                using (o)
                {
                    var name = o["Name"] as string;
                    if (string.IsNullOrEmpty(name))
                        continue;
                    var m = ComInName.Match(name);
                    if (m.Success)
                        raw.Add(m.Groups[1].Value.ToUpperInvariant());
                }
            }
        }
        catch (ManagementException)
        {
            return Array.Empty<string>();
        }

        return raw
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(ComNumericKey)
            .ToList();
    }

    static List<string> FindByVidPid(ushort vid, ushort pid)
    {
        var raw = new List<string>();
        try
        {
            var like = $"%VID_{vid:X4}&PID_{pid:X4}%";
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                $"SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE '{like}'");

            using var coll = searcher.Get();
            foreach (ManagementObject o in coll)
            {
                using (o)
                {
                    var name = o["Name"] as string;
                    if (string.IsNullOrEmpty(name))
                        continue;
                    var m = ComInName.Match(name);
                    if (m.Success)
                        raw.Add(m.Groups[1].Value.ToUpperInvariant());
                }
            }
        }
        catch (ManagementException)
        {
            return raw;
        }

        return raw
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(ComNumericKey)
            .ToList();
    }

    static int ComNumericKey(string com)
    {
        if (com.Length > 3 && com.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(com.AsSpan(3), out var n))
            return n;
        return int.MaxValue;
    }
}
