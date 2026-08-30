using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using NexoLauncher.Core.Installation;
using NexoLauncher.Domain.Instances;

namespace NexoLauncher.App;

public sealed class ProfileIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ProfileArtworkImageLoader.Load(value, icon: true);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ProfileBackgroundConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ProfileArtworkImageLoader.Load(value, icon: false);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

internal static class ProfileArtworkImageLoader
{
    private static readonly NexoPaths Paths = NexoPaths.ForCurrentUser();

    public static BitmapImage? Load(object value, bool icon)
    {
        if (value is not InstanceId id) return null;
        var root = Path.Combine(Paths.Instances, id.ToString());
        var artwork = ProfileArtworkStore.Load(root);
        var relative = icon ? artwork?.IconRelativePath : artwork?.BackgroundRelativePath;
        var resolved = ProfileArtworkStore.Resolve(root, relative);
        if (resolved is null) return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(resolved, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
