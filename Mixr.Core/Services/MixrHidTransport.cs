using HidSharp;

namespace Mixr.Services;

/// <summary>
/// USB-HID-Link zum Mixr (Vendor-Interface, Usage Page 0xFF00, 64-Byte-Reports). Treiberlos, kein COM-Port;
/// Windows liefert Reports mit führendem Report-ID-Byte (0), daher 65-Byte-Puffer.
/// </summary>
public sealed class MixrHidTransport : IMixrLink
{
    /*
     * VID/PID: 0x1209:0x0001 ist die pid.codes-Testkennung (siehe ESP/main/Kconfig.projbuild).
     * Vor Auslieferung eigene PID eintragen — kostenlos via pid.codes oder Espressif-PID-Programm.
     */
    public const ushort Vid = 0x1209;
    public const ushort Pid = 0x0001;
    public const ushort VendorUsagePage = 0xFF00;

    readonly HidDevice _device;
    readonly HidStream _stream;
    readonly object _sendLock = new();
    readonly int _outLen;
    volatile bool _disposed;

    public MixrLinkKind Kind => MixrLinkKind.Hid;

    public string Id { get; }

    public string SerialNumber { get; }

    public bool IsOpen => !_disposed;

    MixrHidTransport(HidDevice device, HidStream stream)
    {
        _device = device;
        _stream = stream;
        _outLen = Math.Max(device.GetMaxOutputReportLength(), MixrProtocol.HidReportSize + 1);
        SerialNumber = SafeSerial(device);
        Id = string.IsNullOrEmpty(SerialNumber) ? device.DevicePath : $"HID {SerialNumber}";
        _stream.ReadTimeout = 250;
        _stream.WriteTimeout = 2000;
    }

    /// <summary>Alle angeschlossenen Mixr-Vendor-HID-Interfaces (ohne Öffnen).</summary>
    public static IReadOnlyList<HidDevice> Enumerate(ushort vid = Vid, ushort pid = Pid)
    {
        var result = new List<HidDevice>();
        try
        {
            foreach (var dev in DeviceList.Local.GetHidDevices(vid, pid))
            {
                if (IsVendorInterface(dev))
                    result.Add(dev);
            }
        }
        catch
        {
            /* HID-Enumeration nicht möglich (z. B. Berechtigungen) */
        }

        return result;
    }

    /// <summary>Öffnet das erste (oder das per Seriennummer gewünschte) Gerät; <c>null</c>, wenn keins da/frei ist.</summary>
    public static MixrHidTransport? TryOpen(string? preferredSerial = null, Action<string>? log = null)
    {
        var devices = Enumerate();
        if (devices.Count == 0)
            return null;

        IEnumerable<HidDevice> ordered = devices;
        if (!string.IsNullOrEmpty(preferredSerial))
            ordered = devices.OrderByDescending(d => string.Equals(SafeSerial(d), preferredSerial, StringComparison.OrdinalIgnoreCase));

        foreach (var dev in ordered)
        {
            try
            {
                if (dev.TryOpen(out var stream))
                    return new MixrHidTransport(dev, stream);
                log?.Invoke($"HID {SafeSerial(dev)}: bereits geöffnet (zweite Mixr-Instanz?)");
            }
            catch (Exception ex)
            {
                log?.Invoke($"HID {SafeSerial(dev)}: {ex.Message}");
            }
        }

        return null;
    }

    static bool IsVendorInterface(HidDevice dev)
    {
        try
        {
            var rd = dev.GetReportDescriptor();
            foreach (var item in rd.DeviceItems)
            {
                foreach (var usage in item.Usages.GetAllValues())
                {
                    if ((usage >> 16) == VendorUsagePage)
                        return true;
                }
            }
        }
        catch
        {
            /* Deskriptor nicht lesbar → Heuristik über Report-Länge */
        }

        try
        {
            return dev.GetMaxOutputReportLength() >= MixrProtocol.HidReportSize;
        }
        catch
        {
            return false;
        }
    }

    static string SafeSerial(HidDevice dev)
    {
        try
        {
            return dev.GetSerialNumber() ?? "";
        }
        catch
        {
            return "";
        }
    }

    public void Send(byte type, ReadOnlySpan<byte> payload)
    {
        if (_disposed)
            throw new InvalidOperationException("HID-Link geschlossen.");

        var reports = MixrFrameCodec.EncodeHidReports(type, payload);
        var buf = new byte[_outLen];
        lock (_sendLock)
        {
            foreach (var r in reports)
            {
                Array.Clear(buf);
                buf[0] = 0; // Report-ID (keine)
                r.CopyTo(buf, 1);
                _stream.Write(buf, 0, buf.Length);
            }
        }
    }

    public void Start(Action<int, byte[]> onFrame, Action onEnded)
    {
        var t = new Thread(() => ReadLoop(onFrame, onEnded))
        {
            IsBackground = true,
            Name = "MixrHidRx",
        };
        t.Start();
    }

    void ReadLoop(Action<int, byte[]> onFrame, Action onEnded)
    {
        var reassembler = new MixrFrameCodec.HidReassembler();
        var inLen = Math.Max(_device.GetMaxInputReportLength(), MixrProtocol.HidReportSize + 1);
        var buf = new byte[inLen];
        try
        {
            while (!_disposed)
            {
                int n;
                try
                {
                    n = _stream.Read(buf, 0, buf.Length);
                }
                catch (TimeoutException)
                {
                    continue;
                }

                if (n <= 0)
                    continue;

                // Windows: buf[0] = Report-ID (0), danach die 64 Nutzbytes
                var span = n > MixrProtocol.HidReportSize ? buf.AsSpan(1, n - 1) : buf.AsSpan(0, n);
                if (reassembler.Push(span, out var type, out var payload))
                    onFrame(type, payload);
            }
        }
        catch (Exception)
        {
            /* Gerät abgezogen / Stream geschlossen */
        }
        finally
        {
            onEnded();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            _stream.Dispose();
        }
        catch
        {
            /* */
        }
    }
}
