namespace Mixr.Services;

/// <summary>
/// Zentrale Eingangs-Stelle für ESP → PC (Slider, Tasten, Media-Befehle).
/// Hier später Session-Mapping / Audio-API anbinden, statt Logik im Einstiegspunkt zu streuen.
/// </summary>
public sealed class EspIncomingDispatcher
{
    /// <summary>4 Bytes, Kanal 0–3 (0–255).</summary>
    public event Action<ReadOnlyMemory<byte>>? SliderValues;

    public event Action<byte>? ButtonPressed;

    /// <summary>MediaSubCmd: 0 Next, 1 Play/Pause, 2 Previous.</summary>
    public event Action<byte>? MediaCommand;

    public void Dispatch(int type, byte[] payload)
    {
        const byte typeSlider = 0x03;
        const byte typeBtn = 0x04;

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
