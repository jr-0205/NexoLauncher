using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NexoLauncher.App.UI;

/// <summary>
/// Capa de compatibilidad visual para llevar pantallas existentes al design system de NEXO
/// sin obligar a reescribir de una sola vez el XAML funcional del launcher.
/// </summary>
public static class NexoUiQualityModule
{
    private static bool initialized;

    public static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window) return;

        Apply(window);
        window.SizeChanged -= Window_SizeChanged;
        window.SizeChanged += Window_SizeChanged;
    }

    private static void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Window window) ApplyResponsiveLayout(window);
    }

    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.Background = Brush("Nexo.Background", window.Background);
        window.Foreground = Brush("Nexo.Text", window.Foreground);
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(window, TextRenderingMode.ClearType);

        var mappings = BuildLegacyStyleMappings(window);
        foreach (var element in VisualDescendants(window).OfType<FrameworkElement>())
        {
            if (element.Style is not null && mappings.TryGetValue(element.Style, out var modernStyle))
                element.Style = modernStyle;

            if (element is TextBlock text &&
                text.Text.StartsWith("NEXO Client ", StringComparison.OrdinalIgnoreCase))
                text.Text = $"NEXO Client {ProductVersion()}";
        }

        ApplyMainShellColors(window);
        ApplyResponsiveLayout(window);
    }

    private static Dictionary<Style, Style> BuildLegacyStyleMappings(Window window)
    {
        var mappings = new Dictionary<Style, Style>();
        Add("LabelStyle", "Nexo.Label");
        Add("FieldStyle", "Nexo.Field");
        Add("PrimaryButton", "Nexo.PrimaryButton");
        Add("SecondaryButton", "Nexo.SecondaryButton");
        Add("GhostButton", "Nexo.GhostButton");
        Add("NavButton", "Nexo.NavButton");
        Add("ComboStyle", "Nexo.ComboBox");
        Add("CardStyle", "Nexo.Card");
        return mappings;

        void Add(string legacyKey, string modernKey)
        {
            if (window.TryFindResource(legacyKey) is Style legacy &&
                System.Windows.Application.Current.TryFindResource(modernKey) is Style modern)
                mappings[legacy] = modern;
        }
    }

    private static void ApplyMainShellColors(Window window)
    {
        if (window is not MainWindow || window.Content is not Grid shell || shell.Children.Count == 0) return;

        shell.Background = Brush("Nexo.Background", shell.Background);
        var sidebar = shell.Children.OfType<Border>().FirstOrDefault();
        if (sidebar is null) return;

        sidebar.Background = Brush("Nexo.Sidebar", sidebar.Background);
        sidebar.BorderBrush = Brush("Nexo.Border", sidebar.BorderBrush);
    }

    private static void ApplyResponsiveLayout(Window window)
    {
        if (window is not MainWindow || window.Content is not Grid shell || shell.ColumnDefinitions.Count < 2) return;

        var compact = window.ActualWidth > 0 && window.ActualWidth < 1180;
        shell.ColumnDefinitions[0].Width = new GridLength(compact ? 228 : 252);

        if (window.FindName("LibraryPanel") is not Grid library) return;
        library.Margin = compact ? new Thickness(26, 24, 26, 24) : new Thickness(34, 28, 34, 28);

        // El shell de producción inserta "Continuar jugando" entre el header y el body,
        // por lo que el panel de instancias puede estar en row 1 o row 2.
        var libraryBody = library.Children
            .OfType<Grid>()
            .FirstOrDefault(grid => Grid.GetRow(grid) >= 1 && grid.ColumnDefinitions.Count >= 2);
        if (libraryBody is null) return;

        libraryBody.ColumnDefinitions[1].Width = new GridLength(compact ? 315 : 350);
    }

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        yield return root;
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            foreach (var descendant in VisualDescendants(child)) yield return descendant;
        }
    }

    private static Brush Brush(string key, Brush fallback) =>
        System.Windows.Application.Current.TryFindResource(key) as Brush ?? fallback;

    private static string ProductVersion()
    {
        var assembly = typeof(NexoUiQualityModule).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational)) return informational.Split('+', 2)[0];
        return assembly.GetName().Version?.ToString(3) ?? "0.5.2";
    }
}
