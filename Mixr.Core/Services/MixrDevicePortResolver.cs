using System.Management;
using System.Text.RegularExpressions;

namespace Mixr.Services;

/// <summary>
/// Findet den COM-Port für den integrierten USB-Serial/JTAG des ESP32-S3 (Espressif VID/PID).
/// Consumer-Gerät: ein Board pro PC — keine manuelle COM-Angabe nötig.
/// </summary>
public static class MixrDevicePortResolver
{
    /// <summary>Espressif Systems — wie ESP-IDF / ESP32-S3 USB Serial/JTAG.</summary>
    public const ushort EspressifVid = 0x303A;

    /// <summary>ESP32-S3 integrierter USB Serial/JTAG (Standard-Deskriptor).</summary>
    public const ushort Esp32S3UsbSerialJtagPid = 0x1001;

    static readonly Regex ComSuffix = new(@"\((COM\d+)\)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <param name="candidates">Alle gefundenen COM-Namen (sortiert nach COM-Nummer).</param>
    public static string? TryFindComPort(out IReadOnlyList<string> candidates, ushort vid = EspressifVid, ushort pid = Esp32S3UsbSerialJtagPid)
    {
        candidates = Array.Empty<string>();
        var raw = new List<string>();

        try
        {
            // PNPDeviceID z. B. USB\VID_303A&PID_1001\...
            var like = $"%VID_{vid:X4}&PID_{pid:X4}%";
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                $"SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE '{like}'");

            using (var coll = searcher.Get())
            {
                foreach (ManagementObject o in coll)
                {
                    using (o)
                    {
                        var name = o["Name"] as string;
                        if (string.IsNullOrEmpty(name))
                            continue;
                        var m = ComSuffix.Match(name);
                        if (m.Success)
                            raw.Add(m.Groups[1].Value.ToUpperInvariant());
                    }
                }
            }
        }
        catch (ManagementException)
        {
            return null;
        }

        if (raw.Count == 0)
            return null;

        var ordered = raw
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(ComNumericKey)
            .ToList();

        candidates = ordered;
        return ordered[0];
    }

    static int ComNumericKey(string com)
    {
        if (com.Length > 3 && com.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(com.AsSpan(3), out var n))
            return n;
        return int.MaxValue;
    }
}
