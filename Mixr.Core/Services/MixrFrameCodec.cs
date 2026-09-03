namespace Mixr.Services;

/// <summary>
/// HID-Transportcodierung: Frame (type + payload) + CRC-16/CCITT-FALSE (LE) in 64-Byte-Reports
/// <c>[flags][n][data ≤ 62]</c>. Siehe protocol.h.
/// </summary>
public static class MixrFrameCodec
{
    public static ushort Crc16(ReadOnlySpan<byte> data, ushort seed = 0xFFFF)
    {
        ushort crc = seed;
        foreach (var b in data)
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }

        return crc;
    }

    /// <summary>Frame in HID-Reports (je genau <see cref="MixrProtocol.HidReportSize"/> Byte) zerlegen.</summary>
    public static List<byte[]> EncodeHidReports(byte type, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MixrProtocol.PayloadMax)
            throw new ArgumentOutOfRangeException(nameof(payload));

        var frame = new byte[1 + payload.Length + 2];
        frame[0] = type;
        payload.CopyTo(frame.AsSpan(1));
        var crc = Crc16(frame.AsSpan(0, 1 + payload.Length));
        frame[^2] = (byte)(crc & 0xFF);
        frame[^1] = (byte)(crc >> 8);

        var reports = new List<byte[]>((frame.Length + MixrProtocol.HidReportDataMax - 1) / MixrProtocol.HidReportDataMax);
        var off = 0;
        var first = true;
        while (off < frame.Length)
        {
            var n = Math.Min(MixrProtocol.HidReportDataMax, frame.Length - off);
            var r = new byte[MixrProtocol.HidReportSize];
            r[0] = (byte)((first ? MixrProtocol.HidFlagSof : 0) | (off + n == frame.Length ? MixrProtocol.HidFlagEof : 0));
            r[1] = (byte)n;
            frame.AsSpan(off, n).CopyTo(r.AsSpan(2));
            reports.Add(r);
            off += n;
            first = false;
        }

        return reports;
    }

    /// <summary>Setzt Reports wieder zu Frames zusammen; prüft CRC. Eine Instanz pro Verbindung.</summary>
    public sealed class HidReassembler
    {
        readonly byte[] _frame = new byte[1 + MixrProtocol.PayloadMax + 2];
        int _len;
        bool _inFrame;

        public int CrcErrors { get; private set; }

        /// <returns><c>true</c> und Typ/Nutzlast, wenn ein vollständiges gültiges Frame entstanden ist.</returns>
        public bool Push(ReadOnlySpan<byte> report, out byte type, out byte[] payload)
        {
            type = 0;
            payload = Array.Empty<byte>();
            if (report.Length < 2)
                return false;

            var flags = report[0];
            var n = report[1];
            if (n > MixrProtocol.HidReportDataMax || 2 + n > report.Length)
            {
                _inFrame = false;
                return false;
            }

            if ((flags & MixrProtocol.HidFlagSof) != 0)
            {
                _len = 0;
                _inFrame = true;
            }

            if (!_inFrame)
                return false;

            if (_len + n > _frame.Length)
            {
                _inFrame = false;
                return false;
            }

            report.Slice(2, n).CopyTo(_frame.AsSpan(_len));
            _len += n;

            if ((flags & MixrProtocol.HidFlagEof) == 0)
                return false;

            _inFrame = false;
            if (_len < 3)
                return false;

            var body = _len - 2;
            var want = (ushort)(_frame[body] | (_frame[body + 1] << 8));
            if (Crc16(_frame.AsSpan(0, body)) != want)
            {
                CrcErrors++;
                return false;
            }

            type = _frame[0];
            payload = _frame.AsSpan(1, body - 1).ToArray();
            return true;
        }
    }
}
