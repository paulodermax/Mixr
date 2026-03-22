using Mixr.Services;
using Xunit;

namespace Mixr.Tests;

public class SessionDedupTests
{
    const int ImgBytes = 240 * 240 * 2;

    [Fact]
    public void First_call_always_sends()
    {
        var d = new SessionDedup();
        var cover = new byte[ImgBytes];
        cover[0] = 1;
        Assert.True(d.ShouldSend("A", "B", cover));
    }

    [Fact]
    public void Identical_session_skips_second_send()
    {
        var d = new SessionDedup();
        var cover = new byte[ImgBytes];
        cover[^1] = 42;

        Assert.True(d.ShouldSend("Titel", "Artist", cover));
        Assert.False(d.ShouldSend("Titel", "Artist", cover));
    }

    [Fact]
    public void Same_cover_different_title_sends_again()
    {
        var d = new SessionDedup();
        var cover = new byte[ImgBytes];

        Assert.True(d.ShouldSend("Eins", "X", cover));
        Assert.True(d.ShouldSend("Zwei", "X", cover));
    }

    [Fact]
    public void Same_meta_different_cover_byte_sends_again()
    {
        var d = new SessionDedup();
        var c1 = new byte[ImgBytes];
        var c2 = new byte[ImgBytes];
        c2[1000] = 7;

        Assert.True(d.ShouldSend("T", "A", c1));
        Assert.True(d.ShouldSend("T", "A", c2));
        Assert.False(d.ShouldSend("T", "A", c2));
    }

    [Fact]
    public void Null_title_artist_normalized()
    {
        var d = new SessionDedup();
        var cover = new byte[ImgBytes];

        Assert.True(d.ShouldSend(null, null, cover));
        Assert.False(d.ShouldSend("", "", cover));
    }
}
