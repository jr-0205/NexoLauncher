using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NexoLauncher.App.UI;

internal static class NexoBrandImage
{
    private const string PngBase64 =
        "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" +
        "Aq4pHhWUOfMjAAAAAElFTkSuQmCC";

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
