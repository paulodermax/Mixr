using System.Buffers.Binary;
using System.Text;
using System.Threading.Channels;

namespace Mixr.Services;

/// <summary>
/// Schickt Titel/Artist/Cover an das Gerät — protokollabhängig:
/// v3: IMAGE_BEGIN(JPEG|RGB565, Hash) → IMAGE_ACK (Gerät kann „habe ich schon“ antworten) → IMAGE_CHUNK… → IMAGE_END.
/// v1/v2: rohes RGB565 in 255-Byte-Chunks ohne Rahmen (Legacy).
/// </summary>
public sealed class CoverSender
{
    readonly IMixrLink _link;
    readonly EspIncomingDispatcher _dispatcher;
    readonly Action<string> _log;
    readonly object _gate = new();

    static readonly TimeSpan AckTimeout = TimeSpan.FromMilliseconds(800);

    public CoverSender(IMixrLink link, EspIncomingDispatcher dispatcher, Action<string> log)
    {
        _link = link;
        _dispatcher = dispatcher;
        _log = log;
    }

    /// <summary>Blockiert für die Dauer der Übertragung (Aufrufer: SMTC-Thread). Wirft bei Link-Fehlern.</summary>
    public void SendSession(string title, string artist, byte[] rgb565, DeviceHello? device)
    {
        lock (_gate)
        {
            _link.Send(MixrProtocol.TypeSongTitle, Utf8Truncate(title, 63));
            _link.Send(MixrProtocol.TypeSongArtist, Utf8Truncate(artist, 63));

            if (device is { IsV3OrNewer: true })
                SendCoverV3(rgb565, device);
            else
                SendCoverLegacy(rgb565);
        }
    }

    void SendCoverLegacy(byte[] rgb565)
    {
        var offset = 0;
        while (offset < rgb565.Length)
        {
            var n = Math.Min(MixrProtocol.PayloadMax, rgb565.Length - offset);
            _link.Send(MixrProtocol.TypeImageChunk, rgb565.AsSpan(offset, n));
            offset += n;
        }
    }

    void SendCoverV3(byte[] rgb565, DeviceHello device)
    {
        byte[] data;
        MixrProtocol.ImageFormat format;
        if (device.SupportsJpegCover)
        {
            data = JpegCoverEncoder.FromRgb565(rgb565);
            format = MixrProtocol.ImageFormat.Jpeg;
            if (data.Length > MixrProtocol.CoverJpegMax)
            {
                data = JpegCoverEncoder.FromRgb565(rgb565, 60);
                if (data.Length > MixrProtocol.CoverJpegMax)
                {
                    data = rgb565;
                    format = MixrProtocol.ImageFormat.Rgb565;
                }
            }
        }
        else
        {
            data = rgb565;
            format = MixrProtocol.ImageFormat.Rgb565;
        }

        var hash = MixrProtocol.CoverHash(rgb565);
        var channel = Channel.CreateUnbounded<(MixrProtocol.ImageAckStatus status, uint hash)>();
        void OnAck(MixrProtocol.ImageAckStatus s, uint h) => channel.Writer.TryWrite((s, h));
        _dispatcher.ImageAck += OnAck;
        try
        {
            var begin = new byte[9];
            begin[0] = (byte)format;
            BinaryPrimitives.WriteUInt32LittleEndian(begin.AsSpan(1, 4), (uint)data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(begin.AsSpan(5, 4), hash);
            _link.Send(MixrProtocol.TypeImageBegin, begin);

            var ack = WaitAck(channel, hash);
            switch (ack)
            {
                case MixrProtocol.ImageAckStatus.AlreadyShown:
                    return;
                case MixrProtocol.ImageAckStatus.Unsupported when format == MixrProtocol.ImageFormat.Jpeg:
                    _log("Cover: Gerät lehnt JPEG ab — sende RGB565.");
                    SendCoverV3(rgb565, device with { Capabilities = (byte)(device.Capabilities & ~MixrProtocol.CapJpegCover) });
                    return;
                case MixrProtocol.ImageAckStatus.Unsupported:
                    _log("Cover: Gerät kann das Bild nicht annehmen (oft: kein PSRAM / Cover-Puffer fehlt — Firmware neu flashen).");
                    return;
                case null:
                    _log("Cover: keine Antwort auf IMAGE_BEGIN — sende ohne Bestätigung.");
                    break;
            }

            var offset = 0;
            while (offset < data.Length)
            {
                var n = Math.Min(MixrProtocol.PayloadMax, data.Length - offset);
                _link.Send(MixrProtocol.TypeImageChunk, data.AsSpan(offset, n));
                offset += n;
            }

            _link.Send(MixrProtocol.TypeImageEnd);
            var end = WaitAck(channel, hash);
            if (end == MixrProtocol.ImageAckStatus.DecodeFailed)
                _log($"Cover: Dekodierung auf dem Gerät fehlgeschlagen ({format}, {data.Length} B).");
        }
        finally
        {
            _dispatcher.ImageAck -= OnAck;
        }
    }

    static MixrProtocol.ImageAckStatus? WaitAck(Channel<(MixrProtocol.ImageAckStatus status, uint hash)> channel, uint hash)
    {
        using var cts = new CancellationTokenSource(AckTimeout);
        try
        {
            while (true)
            {
                var (status, h) = channel.Reader.ReadAsync(cts.Token).AsTask().GetAwaiter().GetResult();
                if (h == hash || h == 0)
                    return status;
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    static byte[] Utf8Truncate(string? s, int maxBytes)
    {
        var full = Encoding.UTF8.GetBytes(s ?? "");
        if (full.Length <= maxBytes)
            return full;

        var len = maxBytes;
        while (len > 0 && (full[len] & 0xC0) == 0x80)
            len--;
        return len <= 0 ? Array.Empty<byte>() : full.AsSpan(0, len).ToArray();
    }
}
