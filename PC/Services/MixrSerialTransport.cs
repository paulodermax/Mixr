using System.IO.Ports;
using System.Text;

namespace Mixr.Services;

/// <summary>
/// Binärprotokoll wie ESP/tools/mixr_send_demo.py mit <c>--fast</c>:
/// kein Warmup, keine Chunk-Pause, kein Post-Send; DTR/RTS aus; Nutzlast max. 255 B.
/// </summary>
public sealed class MixrSerialTransport : IDisposable
{
    public const byte PktStartByte = 0xAA;
    public const byte TypeSongTitle = 0x01;
    public const byte TypeSongArtist = 0x02;
    public const byte TypeImageChunk = 0x05;
    /// <summary>ESP → PC: 1 Byte Nutzlast — 0 Next, 1 Play/Pause, 2 Previous (MediaSubCmd).</summary>
    public const byte TypeMediaCmd = 0x07;

    public const int ChunkMax = 255;

    readonly SerialPort _port;
    readonly object _sendLock = new();

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

    /// <summary>Titel + Artist + Cover in einem Rutsch (wie send_session_serial).</summary>
    public void SendSession(string title, string artist, byte[] rgb565Full)
    {
        if (rgb565Full.Length != Rgb565Converter.ImgBytes)
            throw new ArgumentException($"Cover: erwartet {Rgb565Converter.ImgBytes} B.", nameof(rgb565Full));

        byte[] t = Utf8TruncateBytes(title, 63);
        byte[] a = Utf8TruncateBytes(artist, 63);

        lock (_sendLock)
        {
            SendPacket(TypeSongTitle, t);
            _port.BaseStream.Flush();
            SendPacket(TypeSongArtist, a);
            _port.BaseStream.Flush();

            int offset = 0;
            while (offset < rgb565Full.Length)
            {
                int n = Math.Min(ChunkMax, rgb565Full.Length - offset);
                var chunk = new byte[n];
                Buffer.BlockCopy(rgb565Full, offset, chunk, 0, n);
                SendPacket(TypeImageChunk, chunk);
                offset += n;
            }

            _port.BaseStream.Flush();
        }
    }

    void SendPacket(byte type, byte[] payload)
    {
        if (payload.Length > 255)
            return;

        byte length = (byte)payload.Length;
        byte crc = (byte)(length ^ type);
        foreach (byte b in payload)
            crc ^= b;

        byte[] packet = new byte[3 + payload.Length + 1];
        packet[0] = PktStartByte;
        packet[1] = length;
        packet[2] = type;
        Buffer.BlockCopy(payload, 0, packet, 3, payload.Length);
        packet[^1] = crc;

        _port.Write(packet, 0, packet.Length);
    }

    static byte[] Utf8TruncateBytes(string s, int maxBytes)
    {
        var full = Encoding.UTF8.GetBytes(s ?? "");
        if (full.Length <= maxBytes)
            return full;

        int len = maxBytes;
        while (len > 0 && (full[len] & 0xC0) == 0x80)
            len--;
        if (len <= 0)
            return Array.Empty<byte>();
        var r = new byte[len];
        Buffer.BlockCopy(full, 0, r, 0, len);
        return r;
    }

    public void StartDrainRxThread(Action<int, byte[]>? onIncoming = null)
    {
        var p = _port;
        var t = new Thread(() => ReadLoop(p, onIncoming))
        {
            IsBackground = true,
            Name = "MixrSerialRx",
        };
        t.Start();
    }

    static void ReadLoop(SerialPort port, Action<int, byte[]>? onIncoming)
    {
        while (port.IsOpen)
        {
            try
            {
                /* ReadByte blockiert bis Timeout (s. ReadTimeout), verhindert Busy-Spin bei leerem RX */
                if (port.ReadByte() != PktStartByte)
                    continue;

                int len = port.ReadByte();
                int type = port.ReadByte();
                var payload = new byte[len];
                int read = 0;
                while (read < len)
                    read += port.Read(payload, read, len - read);
                int crc = port.ReadByte();
                int calc = len ^ type;
                foreach (byte b in payload)
                    calc ^= b;
                if (crc == calc)
                    onIncoming?.Invoke(type, payload);
            }
            catch (TimeoutException) { }
            catch (IOException) { break; }
            catch (Exception) { break; }
        }
    }

    public void Dispose()
    {
        try
        {
            _port.DtrEnable = false;
            _port.RtsEnable = false;
        }
        catch (IOException) { }

        _port.Dispose();
    }
}
