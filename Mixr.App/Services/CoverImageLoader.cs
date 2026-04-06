using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Mixr_App.Services;

/// <summary>
/// Cover-Bilder für WinUI: primär <see cref="SoftwareBitmapSource"/>, Fallback <see cref="BitmapImage"/> (z. B. WebP).
/// </summary>
public static class CoverImageLoader
{
    public static async Task<ImageSource?> LoadCoverImageSourceAsync(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            return null;

        var path = Path.GetFullPath(absolutePath);

        var soft = await TryDecodeSoftwareBitmapAsync(path);
        if (soft != null)
        {
            try
            {
                var src = new SoftwareBitmapSource();
                await src.SetBitmapAsync(soft);
                return src;
            }
            catch
            {
                /* Fallback unten */
            }
        }

        return await TryLoadBitmapImageAsync(path);
    }

    static async Task<SoftwareBitmap?> TryDecodeSoftwareBitmapAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            return await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);
        }
        catch
        {
            return null;
        }
    }

    static async Task<ImageSource?> TryLoadBitmapImageAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(stream);
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
