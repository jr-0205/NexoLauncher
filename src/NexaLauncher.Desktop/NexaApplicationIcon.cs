using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NexaLauncher.Desktop;

internal static class NexaApplicationIcon
{
    private const string ResourceName = "NexaLauncher.Desktop.Assets.nexa-client.ico.b64";

    public static ImageSource? Create()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var resource = assembly.GetManifestResourceStream(ResourceName);
            if (resource is null) return null;

            using var reader = new StreamReader(resource);
            var encoded = reader.ReadToEnd().Trim();
            if (string.IsNullOrWhiteSpace(encoded)) return null;

            var bytes = Convert.FromBase64String(encoded);
            using var stream = new MemoryStream(bytes, writable: false);
            var frame = BitmapFrame.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            frame.Freeze();
            return frame;
        }
        catch (Exception exception) when (exception is FormatException or IOException or NotSupportedException)
        {
            return null;
        }
    }
}
