using System.IO;
using System.IO.Ports;
using System.Text;

namespace Mixr.Services;

/// <summary>
/// Serieller Transport für das Mixr-Binärprotokoll (siehe <see cref="MixrProtocol"/>).
/// Alle Sendeaufrufe sind über <c>_sendLock</c> serialisiert, damit sich Frames aus verschiedenen Threads
/// (SMTC-Cover, Hotkey-Overlays, Firmware-Update) nicht verschränken.
/// </summary>
public sealed class MixrSerialTransport : IDisposable
{
    public const byte PktStartByte = MixrProtocol.StartByte;
    public const byte TypeSongTitle = MixrProtocol.TypeSongTitle;
    public const byte TypeSongArtist = MixrProtocol.TypeSongArtist;
    public const byte TypeImageChunk = MixrProtocol.TypeImageChunk;
    public const byte TypeMediaCmd = MixrProtocol.TypeMediaCmd;
    public const byte TypeVoipMuteCmd = MixrProtocol.TypeVoipMuteCmd;
    public const byte TypeVoipMuteToggleUi = MixrProtocol.TypeVoipMuteToggleUi;
    public const byte TypeVoipDeafen = MixrProtocol.TypeVoipDeafen;
    public const byte TypeShareScreenCmd = MixrProtocol.TypeShareScreenCmd;

    public const int ChunkMax = MixrProtocol.PayloadMax;

    readonly SerialPort _port;
    readonly object _sendLock = new();
    volatile bool _disposed;

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

    /// <summary>Titel + Artist + Cover in einem Rutsch (wie send_session_serial).</summary>
    public void SendSession(string title, string artist, byte[] rgb565Full)
    {
        if (rgb565Full.Length != Rgb565Converter.ImgBytes)
            throw new ArgumentException($"Cover: erwartet {Rgb565Converter.ImgBytes} B.", nameof(rgb565Full));

        byte[] t = Utf8TruncateBytes(title, 63);
        byte[] a = Utf8TruncateBytes(artist, 63);

        lock (_sendLock)
        {
            SendPacketCore(TypeSongTitle, t);
            _port.BaseStream.Flush();
            SendPacketCore(TypeSongArtist, a);
            _port.BaseStream.Flush();

            int offset = 0;
            while (offset < rgb565Full.Length)
            {
                int n = Math.Min(ChunkMax, rgb565Full.Length - offset);
                SendPacketCore(TypeImageChunk, rgb565Full.AsSpan(offset, n));
                offset += n;
            }

            _port.BaseStream.Flush();
        }
    }

    /// <summary>Beliebigen Frame senden (Nutzlast ≤ 255 Byte). Wirft bei geschlossenem Port oder Überlänge.</summary>
    public void Send(byte type, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MixrProtocol.PayloadMax)
            throw new ArgumentOutOfRangeException(nameof(payload), $"Nutzlast > {MixrProtocol.PayloadMax} Byte.");

        lock (_sendLock)
        {
            SendPacketCore(type, payload);
            _port.BaseStream.Flush();
        }
    }

    public void Send(byte type) => Send(type, ReadOnlySpan<byte>.Empty);

    void SendPacketCore(byte type, ReadOnlySpan<byte> payload)
    {
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

        _port.Write(packet, 0, packet.Length);
    }

    /// <summary>Nach erfolgreichem Discord-Mute: ESP zeigt Stumm-Icon auf Slide 1.</summary>
    public void SendVoipMuteOverlayToggle() => TrySendEmpty(TypeVoipMuteToggleUi);

    public void SendVoipDeafenOverlayToggle() => TrySendEmpty(TypeVoipDeafen);

    /// <summary>Gerät um HELLO (Protokollversion, Firmware) bitten.</summary>
    public void SendHelloRequest() => TrySendEmpty(MixrProtocol.TypeHelloReq);

    void TrySendEmpty(byte type)
    {
        try
        {
            Send(type);
        }
        catch (IOException)
        {
            /* seriell weg — ignorieren, Reconnect-Loop kümmert sich */
        }
        catch (InvalidOperationException)
        {
            /* Port bereits geschlossen */
        }
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

    /// <param name="onIncoming">Empfangenes Paket (Typ + Nutzlast).</param>
    /// <param name="onRxEnded">Wird aufgerufen, wenn der Lesethread endet (USB weg, Fehler, Port zu).</param>
    public void StartDrainRxThread(Action<int, byte[]>? onIncoming = null, Action? onRxEnded = null)
    {
        var p = _port;
        var t = new Thread(() => ReadLoop(p, onIncoming, onRxEnded))
        {
            IsBackground = true,
            Name = "MixrSerialRx",
        };
        t.Start();
    }

    static void ReadLoop(SerialPort port, Action<int, byte[]>? onIncoming, Action? onRxEnded)
    {
        try
        {
            while (port.IsOpen)
            {
                try
                {
                    /* ReadByte blockiert bis Timeout (s. ReadTimeout), verhindert Busy-Spin bei leerem RX.
                     * Ein Timeout mitten im Frame wirft TimeoutException → Schleife beginnt wieder bei der
                     * Suche nach 0xAA; der halbe Frame ist damit verworfen (Resync). */
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
                        onIncoming?.Invoke(type, payload);
                }
                catch (TimeoutException) { }
                catch (IOException) { break; }
                catch (Exception) { break; }
            }
        }
        finally
        {
            onRxEnded?.Invoke();
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
