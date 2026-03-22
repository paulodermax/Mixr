using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Mixr.Services;

public static class Rgb565Converter
{
    public const int ImgW = 240;
    public const int ImgH = 240;
    public const int ImgBytes = ImgW * ImgH * 2;

    /// <summary>Grau-Platzhalter wie mixr_send_demo.py make_cover_gray_rgb565 (0x632C LE).</summary>
    public static byte[] GrayPlaceholder()
    {
        var buf = new byte[ImgBytes];
        for (int i = 0; i < ImgBytes; i += 2)
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(i), 0x632C);
        return buf;
    }

    /// <summary>240×240 RGB565 little-endian (wie LVGL / ESP).</summary>
    public static byte[] ConvertBitmapToRgb565(Bitmap src)
    {
        using var img = new Bitmap(ImgW, ImgH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(img))
        {
            g.DrawImage(src, 0, 0, ImgW, ImgH);
        }

        BitmapData data = img.LockBits(
            new Rectangle(0, 0, ImgW, ImgH),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            int stride = data.Stride;
            int absStride = Math.Abs(stride);
            int len = absStride * ImgH;
            byte[] argb = new byte[len];
            Marshal.Copy(data.Scan0, argb, 0, len);

            byte[] buffer = new byte[ImgBytes];
            int i = 0;
            for (int y = 0; y < ImgH; y++)
            {
                int rowStart = stride >= 0 ? y * stride : (ImgH - 1 - y) * absStride;
                for (int x = 0; x < ImgW; x++)
                {
                    int o = rowStart + x * 4;
                    int r = argb[o + 2];
                    int gc = argb[o + 1];
                    int b = argb[o + 0];
                    ushort rgb565 = (ushort)(((r & 0xF8) << 8) | ((gc & 0xFC) << 3) | (b >> 3));
                    BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(i), rgb565);
                    i += 2;
                }
            }

            return buffer;
        }
        finally
        {
            img.UnlockBits(data);
        }
    }
}
