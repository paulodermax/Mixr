using System.Buffers.Binary;
using System.Text;

namespace Mixr.Services;

/// <summary>
/// Zentrale Eingangs-Stelle für ESP → PC (Slider, Tasten, Media, VoIP, Share Screen, HELLO, FW_ACK, IMAGE_ACK, LOG, PONG).
/// </summary>
public sealed class EspIncomingDispatcher
{
    /// <summary>4 Bytes, Kanal 0–3 (0–255).</summary>
    public event Action<ReadOnlyMemory<byte>>? SliderValues;

    public event Action<byte>? ButtonPressed;

    /// <summary>MediaSubCmd: 0 Next, 1 Play/Pause, 2 Previous.</summary>
    public event Action<byte>? MediaCommand;

    /// <summary>ESP Debug-Menü / zentraler Mute-Befehl (Pkt VOIP_MUTE_CMD).</summary>
    public event Action? VoipMuteRequested;

    /// <summary>ESP Debug-Menü / Deafen-Befehl (Pkt 0x0B VOIP_DEAFEN).</summary>
    public event Action? VoipDeafenRequested;

    /// <summary>ESP Debug-Menü — Bildschirm teilen (Pkt 0x0C).</summary>
    public event Action? ShareScreenRequested;

    /// <summary>HELLO vom Gerät (nach Verbindung oder HELLO_REQ).</summary>
    public event Action<DeviceHello>? Hello;

    /// <summary>FW_ACK: Status + nächster erwarteter Offset.</summary>
    public event Action<MixrProtocol.FwStatus, uint>? FirmwareAck;

    /// <summary>IMAGE_ACK: Status + Cover-Hash.</summary>
    public event Action<MixrProtocol.ImageAckStatus, uint>? ImageAck;

    /// <summary>LOG: Level (1 Error … 4 Debug) + Text.</summary>
    public event Action<byte, string>? Log;

    /// <summary>PONG: Uptime in Sekunden, freier Heap in Byte.</summary>
    public event Action<uint, uint>? Pong;

    public void Dispatch(int type, byte[] payload)
    {
        switch (type)
        {
            case MixrProtocol.TypeHello when payload.Length >= 2:
            {
                var version = payload.Length > 2
                    ? Encoding.UTF8.GetString(payload, 2, payload.Length - 2).TrimEnd('\0')
                    : "";
                Hello?.Invoke(new DeviceHello(payload[0], payload[1], version));
                return;
            }
            case MixrProtocol.TypeFwAck when payload.Length >= 5:
                FirmwareAck?.Invoke((MixrProtocol.FwStatus)payload[0], BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(1, 4)));
                return;
            case MixrProtocol.TypeImageAck when payload.Length >= 5:
                ImageAck?.Invoke((MixrProtocol.ImageAckStatus)payload[0], BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(1, 4)));
                return;
            case MixrProtocol.TypeLog when payload.Length >= 1:
                Log?.Invoke(payload[0], payload.Length > 1 ? Encoding.UTF8.GetString(payload, 1, payload.Length - 1) : "");
                return;
            case MixrProtocol.TypePong when payload.Length >= 8:
                Pong?.Invoke(
                    BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4)),
                    BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(4, 4)));
                return;
            case MixrProtocol.TypeVoipMuteCmd when payload.Length == 0:
                VoipMuteRequested?.Invoke();
                return;
            case MixrProtocol.TypeVoipDeafen when payload.Length == 0:
                VoipDeafenRequested?.Invoke();
                return;
            case MixrProtocol.TypeShareScreenCmd when payload.Length == 0:
                ShareScreenRequested?.Invoke();
                return;
            case MixrProtocol.TypeSliderVals when payload.Length >= 4:
                SliderValues?.Invoke(payload.AsMemory(0, 4));
                return;
            case MixrProtocol.TypeBtnCmd when payload.Length >= 1:
                ButtonPressed?.Invoke(payload[0]);
                return;
            case MixrProtocol.TypeMediaCmd when payload.Length >= 1:
                MediaCommand?.Invoke(payload[0]);
                return;
        }
    }
}
