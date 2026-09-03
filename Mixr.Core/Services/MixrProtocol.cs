namespace Mixr.Services;

/// <summary>
/// Spiegel von <c>ESP/main/protocol.h</c>. Ein Frame ist <c>type | payload</c>; der Transport (HID-Reports
/// mit CRC-16 bzw. 0xAA-Serial-Framing mit XOR) liegt in <see cref="MixrFrameCodec"/> und den Link-Klassen.
/// </summary>
public static class MixrProtocol
{
    public const byte StartByte = 0xAA;

    /// <summary>Protokollversion, die dieser Host spricht (HELLO).</summary>
    public const byte Version = 3;

    public const int PayloadMax = 255;

    /// <summary>FW_CHUNK: 4 Byte Offset + Daten.</summary>
    public const int FwChunkDataMax = PayloadMax - 4;

    public const int HidReportSize = 64;
    public const int HidReportDataMax = HidReportSize - 2;
    public const byte HidFlagSof = 0x01;
    public const byte HidFlagEof = 0x02;

    public const int CoverWidth = 240;
    public const int CoverHeight = 240;
    public const int CoverRgb565Bytes = CoverWidth * CoverHeight * 2;
    public const int CoverJpegMax = 96 * 1024;

    public const byte TypeSongTitle = 0x01;
    public const byte TypeSongArtist = 0x02;
    public const byte TypeSliderVals = 0x03;
    public const byte TypeBtnCmd = 0x04;
    public const byte TypeImageChunk = 0x05;
    public const byte TypeImageReady = 0x06;
    public const byte TypeMediaCmd = 0x07;
    public const byte TypeVoipMuteCmd = 0x08;
    public const byte TypeVoipMuteToggleUi = 0x0A;
    public const byte TypeVoipDeafen = 0x0B;
    public const byte TypeShareScreenCmd = 0x0C;

    public const byte TypeHelloReq = 0x10;
    public const byte TypeHello = 0x11;
    public const byte TypeFwBegin = 0x12;
    public const byte TypeFwChunk = 0x13;
    public const byte TypeFwEnd = 0x14;
    public const byte TypeFwAck = 0x15;
    public const byte TypeFwAbort = 0x16;

    public const byte TypeImageBegin = 0x20;
    public const byte TypeImageEnd = 0x21;
    public const byte TypeImageAck = 0x22;
    public const byte TypeSetButtonMap = 0x23;
    public const byte TypeLog = 0x24;
    public const byte TypeSetLogStream = 0x25;
    public const byte TypeEnterBootloader = 0x26;
    public const byte TypePing = 0x27;
    public const byte TypePong = 0x28;

    public const byte CapOtaProtocol = 0x01;
    public const byte CapJpegCover = 0x02;
    public const byte CapHidConsumer = 0x04;
    public const byte CapBootloaderCmd = 0x08;
    public const byte CapLogStream = 0x10;

    public const int ButtonCount = 5;

    public enum ImageFormat : byte
    {
        Rgb565 = 0,
        Jpeg = 1,
    }

    public enum ImageAckStatus : byte
    {
        SendData = 0,
        AlreadyShown = 1,
        Unsupported = 2,
        DecodeFailed = 3,
        Shown = 4,
    }

    public enum FwStatus : byte
    {
        Ok = 0,
        Unsupported = 1,
        BeginFailed = 2,
        WriteFailed = 3,
        VerifyFailed = 4,
        OutOfSequence = 5,
        TooLarge = 6,
        NotStarted = 7,
        Aborted = 8,
    }

    /// <summary>HID Consumer-Control-Usages (Usage Page 0x0C).</summary>
    public static class HidUsage
    {
        public const ushort None = 0x0000;
        public const ushort PlayPause = 0x00CD;
        public const ushort ScanNext = 0x00B5;
        public const ushort ScanPrev = 0x00B6;
        public const ushort Stop = 0x00B7;
        public const ushort Mute = 0x00E2;
        public const ushort VolumeUp = 0x00E9;
        public const ushort VolumeDown = 0x00EA;
    }

    public static string Describe(FwStatus s) => s switch
    {
        FwStatus.Ok => "OK",
        FwStatus.Unsupported => "Gerät hat keine OTA-Partition (Update nur über USB-Download-Modus)",
        FwStatus.BeginFailed => "Gerät konnte das Update nicht starten",
        FwStatus.WriteFailed => "Schreibfehler im Flash",
        FwStatus.VerifyFailed => "Prüfsumme des Images stimmt nicht",
        FwStatus.OutOfSequence => "Datenreihenfolge gestört",
        FwStatus.TooLarge => "Image passt nicht in die Partition",
        FwStatus.NotStarted => "Kein Update aktiv",
        FwStatus.Aborted => "Abgebrochen",
        _ => $"Unbekannter Status {(byte)s}",
    };

    /// <summary>FNV-1a 32 Bit — Cover-Hash für IMAGE_BEGIN (kein Sicherheitsmerkmal, nur Cache-Schlüssel).</summary>
    public static uint CoverHash(ReadOnlySpan<byte> data)
    {
        uint h = 2166136261;
        foreach (var b in data)
        {
            h ^= b;
            h *= 16777619;
        }

        return h == 0 ? 1u : h; // 0 = "kein Hash" auf dem Gerät
    }
}

/// <summary>Antwort des Geräts auf HELLO_REQ bzw. beim Verbinden.</summary>
public sealed record DeviceHello(byte ProtocolVersion, byte Capabilities, string FirmwareVersion)
{
    public bool SupportsProtocolOta => (Capabilities & MixrProtocol.CapOtaProtocol) != 0;
    public bool SupportsJpegCover => (Capabilities & MixrProtocol.CapJpegCover) != 0;
    public bool SupportsHidConsumer => (Capabilities & MixrProtocol.CapHidConsumer) != 0;
    public bool SupportsBootloaderCmd => (Capabilities & MixrProtocol.CapBootloaderCmd) != 0;
    public bool SupportsLogStream => (Capabilities & MixrProtocol.CapLogStream) != 0;

    /// <summary>Protokoll v3: IMAGE_BEGIN/END, SET_BUTTON_MAP, PING.</summary>
    public bool IsV3OrNewer => ProtocolVersion >= 3;
}
