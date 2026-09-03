using System.Text;
using Mixr.Services;
using Xunit;

namespace Mixr.Tests;

public class FrameCodecTests
{
    [Fact]
    public void Crc16_CcittFalse_KnownVector()
    {
        // CRC-16/CCITT-FALSE("123456789") = 0x29B1
        Assert.Equal(0x29B1, MixrFrameCodec.Crc16(Encoding.ASCII.GetBytes("123456789")));
    }

    [Fact]
    public void EmptyPayload_FitsInOneReport()
    {
        var reports = MixrFrameCodec.EncodeHidReports(MixrProtocol.TypeHelloReq, ReadOnlySpan<byte>.Empty);
        Assert.Single(reports);
        Assert.Equal(MixrProtocol.HidReportSize, reports[0].Length);
        Assert.Equal(MixrProtocol.HidFlagSof | MixrProtocol.HidFlagEof, reports[0][0]);
        Assert.Equal(3, reports[0][1]); // type + crc16
    }

    [Fact]
    public void MaxPayload_SplitsIntoFiveReportsAndRoundTrips()
    {
        var payload = new byte[MixrProtocol.PayloadMax];
        for (var i = 0; i < payload.Length; i++)
            payload[i] = (byte)(i * 7);

        var reports = MixrFrameCodec.EncodeHidReports(MixrProtocol.TypeFwChunk, payload);
        Assert.Equal(5, reports.Count); // 258 Byte / 62 = 4,16 → 5
        Assert.Equal(MixrProtocol.HidFlagSof, reports[0][0]);
        Assert.Equal(MixrProtocol.HidFlagEof, reports[^1][0]);

        var rx = new MixrFrameCodec.HidReassembler();
        byte type = 0;
        byte[]? got = null;
        foreach (var r in reports)
        {
            if (rx.Push(r, out type, out var p))
                got = p;
        }

        Assert.NotNull(got);
        Assert.Equal(MixrProtocol.TypeFwChunk, type);
        Assert.Equal(payload, got);
        Assert.Equal(0, rx.CrcErrors);
    }

    [Fact]
    public void CorruptedByte_IsRejectedByCrc()
    {
        var reports = MixrFrameCodec.EncodeHidReports(MixrProtocol.TypeSongTitle, Encoding.UTF8.GetBytes("Hello"));
        reports[0][5] ^= 0x01;

        var rx = new MixrFrameCodec.HidReassembler();
        var ok = rx.Push(reports[0], out _, out _);
        Assert.False(ok);
        Assert.Equal(1, rx.CrcErrors);
    }

    [Fact]
    public void LostFirstReport_DoesNotProduceGarbageFrame()
    {
        var payload = new byte[150];
        var reports = MixrFrameCodec.EncodeHidReports(MixrProtocol.TypeImageChunk, payload);
        Assert.True(reports.Count >= 3);

        var rx = new MixrFrameCodec.HidReassembler();
        var produced = false;
        foreach (var r in reports.Skip(1))
            produced |= rx.Push(r, out _, out _);
        Assert.False(produced);

        // Danach muss ein sauberes Frame wieder ankommen
        var next = MixrFrameCodec.EncodeHidReports(MixrProtocol.TypePing, ReadOnlySpan<byte>.Empty);
        Assert.True(rx.Push(next[0], out var t, out _));
        Assert.Equal(MixrProtocol.TypePing, t);
    }

    [Fact]
    public void CoverHash_IsStableAndNeverZero()
    {
        var a = MixrProtocol.CoverHash(new byte[] { 1, 2, 3 });
        var b = MixrProtocol.CoverHash(new byte[] { 1, 2, 3 });
        var c = MixrProtocol.CoverHash(new byte[] { 1, 2, 4 });
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.NotEqual(0u, MixrProtocol.CoverHash(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void ButtonMap_MediaKeysBecomeHidUsages_DiscordStaysHost()
    {
        var map = MixrButtonActions.BuildHidButtonMap(
            new[] { "smtc_previous", "smtc_play_pause", "smtc_next", "discord_mute", "none" });
        Assert.Equal(10, map.Length);
        Assert.Equal(MixrProtocol.HidUsage.ScanPrev, (ushort)(map[0] | (map[1] << 8)));
        Assert.Equal(MixrProtocol.HidUsage.PlayPause, (ushort)(map[2] | (map[3] << 8)));
        Assert.Equal(MixrProtocol.HidUsage.ScanNext, (ushort)(map[4] | (map[5] << 8)));
        Assert.Equal(MixrProtocol.HidUsage.None, (ushort)(map[6] | (map[7] << 8)));
        Assert.Equal(MixrProtocol.HidUsage.None, (ushort)(map[8] | (map[9] << 8)));
    }

    [Fact]
    public void HidDevice_HandlesMediaButtons_HostSkips()
    {
        var hid = new DeviceHello(3, MixrProtocol.CapHidConsumer | MixrProtocol.CapJpegCover, "1.0.0");
        var serial = new DeviceHello(3, MixrProtocol.CapJpegCover, "1.0.0");
        Assert.True(MixrButtonActions.IsHandledByDeviceHid("smtc_next", hid));
        Assert.False(MixrButtonActions.IsHandledByDeviceHid("discord_mute", hid));
        Assert.False(MixrButtonActions.IsHandledByDeviceHid("smtc_next", serial));
        Assert.False(MixrButtonActions.IsHandledByDeviceHid("smtc_next", null));
    }

    [Fact]
    public void JpegEncoder_ProducesSmallJpegFromRgb565()
    {
        var rgb = Rgb565Converter.GrayPlaceholder();
        var jpg = JpegCoverEncoder.FromRgb565(rgb);
        Assert.True(jpg.Length > 100);
        Assert.True(jpg.Length < MixrProtocol.CoverJpegMax);
        Assert.Equal(0xFF, jpg[0]);
        Assert.Equal(0xD8, jpg[1]); // SOI
    }
}
