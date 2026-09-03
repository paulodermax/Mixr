using System.Text;
using Mixr.Services;
using Xunit;

namespace Mixr.Tests;

public class ProtocolDispatchTests
{
    [Fact]
    public void Hello_IsParsedIntoDeviceHello()
    {
        var d = new EspIncomingDispatcher();
        DeviceHello? got = null;
        d.Hello += h => got = h;

        var payload = new byte[] { 2, MixrProtocol.CapOtaProtocol }
            .Concat(Encoding.UTF8.GetBytes("1.4.2")).ToArray();
        d.Dispatch(MixrProtocol.TypeHello, payload);

        Assert.NotNull(got);
        Assert.Equal(2, got!.ProtocolVersion);
        Assert.True(got.SupportsProtocolOta);
        Assert.Equal("1.4.2", got.FirmwareVersion);
    }

    [Fact]
    public void Hello_WithoutOtaCapability()
    {
        var d = new EspIncomingDispatcher();
        DeviceHello? got = null;
        d.Hello += h => got = h;
        d.Dispatch(MixrProtocol.TypeHello, new byte[] { 2, 0 });
        Assert.NotNull(got);
        Assert.False(got!.SupportsProtocolOta);
        Assert.Equal("", got.FirmwareVersion);
    }

    [Fact]
    public void FirmwareAck_ParsesStatusAndOffset()
    {
        var d = new EspIncomingDispatcher();
        (MixrProtocol.FwStatus status, uint offset)? got = null;
        d.FirmwareAck += (s, o) => got = (s, o);

        d.Dispatch(MixrProtocol.TypeFwAck, new byte[] { (byte)MixrProtocol.FwStatus.OutOfSequence, 0x78, 0x56, 0x34, 0x12 });

        Assert.NotNull(got);
        Assert.Equal(MixrProtocol.FwStatus.OutOfSequence, got!.Value.status);
        Assert.Equal(0x12345678u, got.Value.offset);
    }

    [Fact]
    public void SliderValues_StillDispatched()
    {
        var d = new EspIncomingDispatcher();
        byte[]? got = null;
        d.SliderValues += m => got = m.ToArray();
        d.Dispatch(MixrProtocol.TypeSliderVals, new byte[] { 1, 2, 3, 4 });
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, got);
    }

    [Fact]
    public void UnknownType_IsIgnored()
    {
        var d = new EspIncomingDispatcher();
        var fired = false;
        d.Hello += _ => fired = true;
        d.FirmwareAck += (_, _) => fired = true;
        d.SliderValues += _ => fired = true;
        d.Dispatch(0x7E, new byte[] { 1, 2, 3 });
        Assert.False(fired);
    }
}
