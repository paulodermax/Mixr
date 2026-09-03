namespace Mixr.Services;

public enum MixrLinkKind
{
    /// <summary>USB-HID-Composite (TinyUSB-Firmware) — treiberlos, kein COM-Port.</summary>
    Hid,
    /// <summary>USB-Serial/JTAG mit 0xAA-Framing (Legacy-Firmware / Bench-Debugging).</summary>
    Serial,
}

/// <summary>Transport zum Gerät. Frames = type + payload (≤ 255 Byte).</summary>
public interface IMixrLink : IDisposable
{
    MixrLinkKind Kind { get; }

    /// <summary>Menschenlesbare Kennung: COM-Port oder HID-Seriennummer/Pfad.</summary>
    string Id { get; }

    bool IsOpen { get; }

    /// <summary>Frame senden. Wirft <see cref="IOException"/>/<see cref="InvalidOperationException"/>, wenn der Link weg ist.</summary>
    void Send(byte type, ReadOnlySpan<byte> payload);

    /// <summary>Lesethread starten; <paramref name="onEnded"/> feuert genau einmal, wenn der Link endet.</summary>
    void Start(Action<int, byte[]> onFrame, Action onEnded);
}

public static class MixrLinkExtensions
{
    public static void Send(this IMixrLink link, byte type) => link.Send(type, ReadOnlySpan<byte>.Empty);

    /// <summary>Senden ohne Exception — für Best-effort-Nachrichten (Overlays, HELLO_REQ).</summary>
    public static bool TrySend(this IMixrLink link, byte type, ReadOnlySpan<byte> payload = default)
    {
        try
        {
            if (!link.IsOpen)
                return false;
            link.Send(type, payload);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidOperationException) /* schließt ObjectDisposedException ein */
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
