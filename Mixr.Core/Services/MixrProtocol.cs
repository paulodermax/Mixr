namespace Mixr.Services;

/// <summary>
/// Spiegel von <c>ESP/main/protocol.h</c>. Frame: 0xAA | len | type | payload | crc (XOR über len, type, payload).
/// </summary>
public static class MixrProtocol
{
    public const byte StartByte = 0xAA;

    /// <summary>Protokollversion, die dieser Host spricht (HELLO). Firmware &lt; 2 kennt kein HELLO.</summary>
    public const byte Version = 2;

    public const int PayloadMax = 255;

    /// <summary>FW_CHUNK: 4 Byte Offset + Daten.</summary>
    public const int FwChunkDataMax = PayloadMax - 4;

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

    public const byte CapOtaProtocol = 0x01;

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
}

/// <summary>Antwort des Geräts auf HELLO_REQ bzw. beim Verbinden.</summary>
public sealed record DeviceHello(byte ProtocolVersion, byte Capabilities, string FirmwareVersion)
{
    public bool SupportsProtocolOta => (Capabilities & MixrProtocol.CapOtaProtocol) != 0;
}
