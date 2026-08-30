using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NexaLauncher.Desktop;

internal static class NexaApplicationIcon
{
    private static readonly Uri IconUri = new("pack://application:,,,/Assets/NEXA%20N.png", UriKind.Absolute);

    public static ImageSource? Create()
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = IconUri;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }
}
