namespace Mixr.Services;

/// <summary>
/// Zentrale Eingangs-Stelle für ESP → PC (Slider, Tasten, Media, VoIP, Share Screen 0x0C).
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

    public void Dispatch(int type, byte[] payload)
    {
        const byte typeSlider = MixrProtocol.TypeSliderVals;
        const byte typeBtn = MixrProtocol.TypeBtnCmd;

        if (type == MixrProtocol.TypeHello && payload.Length >= 2)
        {
            var version = payload.Length > 2
                ? System.Text.Encoding.UTF8.GetString(payload, 2, payload.Length - 2).TrimEnd('\0')
                : "";
            Hello?.Invoke(new DeviceHello(payload[0], payload[1], version));
            return;
        }

        if (type == MixrProtocol.TypeFwAck && payload.Length >= 5)
        {
            var offset = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(1, 4));
            FirmwareAck?.Invoke((MixrProtocol.FwStatus)payload[0], offset);
            return;
        }

        if (type == MixrSerialTransport.TypeVoipMuteCmd && payload.Length == 0)
        {
            VoipMuteRequested?.Invoke();
            return;
        }

        if (type == MixrSerialTransport.TypeVoipDeafen && payload.Length == 0)
        {
            VoipDeafenRequested?.Invoke();
            return;
        }

        if (type == MixrSerialTransport.TypeShareScreenCmd && payload.Length == 0)
        {
            ShareScreenRequested?.Invoke();
            return;
        }

        if (type == typeSlider && payload.Length >= 4)
        {
            SliderValues?.Invoke(payload.AsMemory(0, 4));
            return;
        }

        if (type == typeBtn && payload.Length >= 1)
        {
            ButtonPressed?.Invoke(payload[0]);
            return;
        }

        if (type == MixrSerialTransport.TypeMediaCmd && payload.Length >= 1)
            MediaCommand?.Invoke(payload[0]);
    }
}
