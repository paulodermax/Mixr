using Microsoft.UI.Xaml.Media.Imaging;
using Mixr.Services;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Mixr_App.Services;

/// <summary>RGB565-Cover (wie zum ESP) als WriteableBitmap für den Display-Spiegel.</summary>
static class Rgb565ImageFactory
{
    public static WriteableBitmap? FromRgb565(byte[]? rgb565)
    {
        if (rgb565 is null || rgb565.Length < Mixr.Services.Rgb565Converter.ImgBytes)
            return null;

        var w = Rgb565Converter.ImgW;
        var h = Rgb565Converter.ImgH;
        var bmp = new WriteableBitmap(w, h);
        using var stream = bmp.PixelBuffer.AsStream();
        var bgra = new byte[w * h * 4];
        var p = 0;
        for (var i = 0; i + 1 < Rgb565Converter.ImgBytes; i += 2)
        {
            var v = (ushort)(rgb565[i] | (rgb565[i + 1] << 8));
            var r = ((v >> 11) & 0x1F) * 255 / 31;
            var g = ((v >> 5) & 0x3F) * 255 / 63;
            var b = (v & 0x1F) * 255 / 31;
            bgra[p++] = (byte)b;
            bgra[p++] = (byte)g;
            bgra[p++] = (byte)r;
            bgra[p++] = 255;
        }

        stream.Write(bgra, 0, bgra.Length);
        bmp.Invalidate();
        return bmp;
    }
}
