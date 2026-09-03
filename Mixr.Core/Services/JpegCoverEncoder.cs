using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Mixr.Services;

/// <summary>240×240-Cover als Baseline-JPEG für Geräte mit MIXR_CAP_JPEG_COVER (≈ 10–25 KB statt 115 KB RGB565).</summary>
public static class JpegCoverEncoder
{
    public const long DefaultQuality = 82;

    static readonly ImageCodecInfo? JpegCodec =
        ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);

    /// <summary>Aus dem bereits gewandelten RGB565-Puffer (wie ihn <see cref="Rgb565Converter"/> liefert).</summary>
    public static byte[] FromRgb565(ReadOnlySpan<byte> rgb565, long quality = DefaultQuality)
    {
        if (rgb565.Length != MixrProtocol.CoverRgb565Bytes)
            throw new ArgumentException("Erwartet 240×240 RGB565.", nameof(rgb565));

        using var bmp = new Bitmap(MixrProtocol.CoverWidth, MixrProtocol.CoverHeight, PixelFormat.Format24bppRgb);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[Math.Abs(data.Stride)];
            var src = 0;
            for (var y = 0; y < bmp.Height; y++)
            {
                for (var x = 0; x < bmp.Width; x++, src += 2)
                {
                    var px = BinaryPrimitives.ReadUInt16LittleEndian(rgb565.Slice(src, 2));
                    var r = (px >> 11) & 0x1F;
                    var g = (px >> 5) & 0x3F;
                    var b = px & 0x1F;
                    row[x * 3 + 0] = (byte)((b << 3) | (b >> 2));
                    row[x * 3 + 1] = (byte)((g << 2) | (g >> 4));
                    row[x * 3 + 2] = (byte)((r << 3) | (r >> 2));
                }

                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        return Encode(bmp, quality);
    }

    public static byte[] Encode(Bitmap bmp, long quality = DefaultQuality)
    {
        using var ms = new MemoryStream(32 * 1024);
        if (JpegCodec is null)
        {
            bmp.Save(ms, ImageFormat.Jpeg);
        }
        else
        {
            using var p = new EncoderParameters(1);
            p.Param[0] = new EncoderParameter(Encoder.Quality, quality);
            bmp.Save(ms, JpegCodec, p);
        }

        return ms.ToArray();
    }
}
