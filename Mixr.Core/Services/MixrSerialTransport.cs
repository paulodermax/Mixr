using System.IO;
using System.IO.Ports;

namespace Mixr.Services;

/// <summary>
/// Legacy-Link: USB-Serial/JTAG mit 0xAA | len | type | payload | xor. Wird verwendet, wenn kein HID-Gerät
/// gefunden wird (Firmware vor v3 oder Bench-Build ohne CONFIG_MIXR_USB_HID).
/// Alle Sendeaufrufe sind über <c>_sendLock</c> serialisiert.
/// </summary>
public sealed class MixrSerialTransport : IMixrLink
{
    public const byte PktStartByte = MixrProtocol.StartByte;
    public const int ChunkMax = MixrProtocol.PayloadMax;

    readonly SerialPort _port;
    readonly object _sendLock = new();
    volatile bool _disposed;

    public MixrLinkKind Kind => MixrLinkKind.Serial;

    public string Id => _port.PortName;

    public string PortName => _port.PortName;

    public bool IsOpen => !_disposed && _port.IsOpen;

    public MixrSerialTransport(string portName, int baudRate)
    {
        _port = new SerialPort(portName, baudRate)
        {
            DtrEnable = false,
            RtsEnable = false,
            ReadTimeout = 200,
            WriteTimeout = 10000,
        };
    }

    public void Open()
    {
        _port.Open();
        try
        {
            _port.DiscardInBuffer();
        }
        catch (IOException) { }
    }

    public void Send(byte type, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MixrProtocol.PayloadMax)
            throw new ArgumentOutOfRangeException(nameof(payload), $"Nutzlast > {MixrProtocol.PayloadMax} Byte.");

        byte length = (byte)payload.Length;
        byte crc = (byte)(length ^ type);
        foreach (byte b in payload)
            crc ^= b;

        byte[] packet = new byte[3 + payload.Length + 1];
        packet[0] = PktStartByte;
        packet[1] = length;
        packet[2] = type;
        payload.CopyTo(packet.AsSpan(3));
        packet[^1] = crc;

        lock (_sendLock)
        {
            _port.Write(packet, 0, packet.Length);
            _port.BaseStream.Flush();
        }
    }

    public void Start(Action<int, byte[]> onFrame, Action onEnded)
    {
        var p = _port;
        var t = new Thread(() => ReadLoop(p, onFrame, onEnded))
        {
            IsBackground = true,
            Name = "MixrSerialRx",
        };
        t.Start();
    }

    static void ReadLoop(SerialPort port, Action<int, byte[]> onIncoming, Action onRxEnded)
    {
        try
        {
            while (port.IsOpen)
            {
                try
                {
                    /* ReadByte blockiert bis Timeout; Timeout mitten im Frame → Resync auf 0xAA. */
                    if (port.ReadByte() != PktStartByte)
                        continue;

                    int len = port.ReadByte();
                    int type = port.ReadByte();
                    if (len < 0 || type < 0)
                        break;
                    var payload = new byte[len];
                    int read = 0;
                    while (read < len)
                    {
                        int n = port.Read(payload, read, len - read);
                        if (n <= 0)
                            throw new IOException("Serial stream ended");
                        read += n;
                    }
                    int crc = port.ReadByte();
                    int calc = len ^ type;
                    foreach (byte b in payload)
                        calc ^= b;
                    if (crc == calc)
                        onIncoming(type, payload);
                }
                catch (TimeoutException) { }
                catch (IOException) { break; }
                catch (Exception) { break; }
            }
        }
        finally
        {
            onRxEnded();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            _port.DtrEnable = false;
            _port.RtsEnable = false;
        }
        catch (IOException) { }
        catch (InvalidOperationException) { }

        _port.Dispose();
    }
}
