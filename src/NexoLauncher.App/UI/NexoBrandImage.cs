using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NexoLauncher.App.UI;

internal static class NexoBrandImage
{
    private const string PngBase64 =
        "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" +
        "HAAAAAElFTkSuQmCC";

    public static ImageSource Create()
    {
        var bytes = Convert.FromBase64String(PngBase64);
        using var stream = new MemoryStream(bytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
