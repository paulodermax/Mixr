using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace Mixr_App.Services;

/// <summary>Lädt monochrome Asset-PNGs in Mixr-Akzentfarbe (#44D62C).</summary>
public static class DashboardThemedIconLoader
{
    static readonly Dictionary<string, WeakReference<ImageSource>> Cache = new(StringComparer.OrdinalIgnoreCase);

    static readonly Color Accent = Color.FromArgb(255, 0x44, 0xD6, 0x2C);

    public static async Task<ImageSource?> LoadAsync(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            return null;

        var full = Path.GetFullPath(absolutePath);
        lock (Cache)
        {
            if (Cache.TryGetValue(full, out var wr) && wr.TryGetTarget(out var cached))
                return cached;
        }

        try
        {
            using var src = new Bitmap(full);
            using var tinted = TintToAccent(src);
            using var ms = new MemoryStream();
            tinted.Save(ms, ImageFormat.Png);
            var ras = new InMemoryRandomAccessStream();
            await ras.WriteAsync(ms.ToArray().AsBuffer());
            ras.Seek(0);

            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(ras);

            lock (Cache)
                Cache[full] = new WeakReference<ImageSource>(bmp);

            return bmp;
        }
        catch
        {
            return await CoverImageLoader.LoadCoverImageSourceAsync(full);
        }
    }

    static Bitmap TintToAccent(Bitmap src)
    {
        var w = src.Width;
        var h = src.Height;
        var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);

        var srcData = src.LockBits(
            new Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        var dstData = dst.LockBits(
            new Rectangle(0, 0, w, h),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            var stride = srcData.Stride;
            var bytes = w * h * 4;
            var srcBuf = new byte[bytes];
            var dstBuf = new byte[bytes];
            Marshal.Copy(srcData.Scan0, srcBuf, 0, bytes);

            for (var i = 0; i < bytes; i += 4)
            {
                var b = srcBuf[i];
                var g = srcBuf[i + 1];
                var r = srcBuf[i + 2];
                var a = srcBuf[i + 3];
                if (a < 8)
                {
                    dstBuf[i + 3] = 0;
                    continue;
                }

                var lum = (r * 0.299 + g * 0.587 + b * 0.114) / 255.0;
                var outA = (byte)Math.Clamp((int)(a * (0.25 + 0.75 * lum)), 0, 255);
                dstBuf[i] = (byte)(Accent.B * outA / 255);
                dstBuf[i + 1] = (byte)(Accent.G * outA / 255);
                dstBuf[i + 2] = (byte)(Accent.R * outA / 255);
                dstBuf[i + 3] = outA;
            }

            Marshal.Copy(dstBuf, 0, dstData.Scan0, bytes);
        }
        finally
        {
            src.UnlockBits(srcData);
            dst.UnlockBits(dstData);
        }

        return dst;
    }
}
