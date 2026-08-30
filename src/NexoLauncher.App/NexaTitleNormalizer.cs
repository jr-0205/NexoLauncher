using System.Runtime.CompilerServices;
using System.Windows;

namespace NexoLauncher.App;

internal static class NexaTitleNormalizer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(NormalizeWindowTitle));
    }

    private static void NormalizeWindowTitle(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window || string.IsNullOrWhiteSpace(window.Title)) return;
        if (window.Title.Contains("NEXO", StringComparison.Ordinal))
            window.Title = window.Title.Replace("NEXO", "NEXA", StringComparison.Ordinal);
    }
}
