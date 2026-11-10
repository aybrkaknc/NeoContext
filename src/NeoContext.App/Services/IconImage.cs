using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace NeoContext.App.Services;

/// <summary>Turns icon pixels from <see cref="IconExtractor"/> into something an Image can show. Call on the UI thread.</summary>
internal static class IconImage
{
    public static async Task<ImageSource> FromPixelsAsync(IconPixels pixels)
    {
        var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, pixels.Width, pixels.Height, BitmapAlphaMode.Premultiplied);
        bitmap.CopyFromBuffer(pixels.Bgra.AsBuffer());
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(bitmap);
        return source;
    }
}
