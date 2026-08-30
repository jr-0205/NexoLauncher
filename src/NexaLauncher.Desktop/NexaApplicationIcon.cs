using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NexaLauncher.Desktop;

internal static class NexaApplicationIcon
{
    // Exact NEXA artwork supplied by the user, proportionally resized to 64x64 for Windows chrome/taskbar use.
    // No crop, recolor, redrawing, or ICO conversion is applied.
    private const string IconPngBase64 = "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";

    public static ImageSource? Create()
    {
        try
        {
            var bytes = Convert.FromBase64String(IconPngBase64);
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception exception) when (exception is FormatException or IOException or NotSupportedException)
        {
            return null;
        }
    }
}
