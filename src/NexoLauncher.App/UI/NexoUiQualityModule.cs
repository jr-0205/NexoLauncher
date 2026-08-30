using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NexoLauncher.App.UI;

/// <summary>
/// Compatibility layer that brings legacy WPF surfaces onto the NEXO design system while
/// production views are progressively moved to dedicated templates/components.
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
        if (window is MainWindow mainWindow) mainWindow.ApplyProductionShell();
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
        ApplyWizardBranding(window);
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

    private static void ApplyWizardBranding(Window window)
    {
        if (window is not CreateProfileWizard) return;
        var markSource = System.Windows.Application.Current.TryFindResource("Nexo.BrandMark") as ImageSource;
        if (markSource is null) return;

        window.Icon = markSource;
        if (window.FindName("IconPreview") is Image preview && preview.Source is null)
            preview.Source = markSource;
    }

    private static void ApplyResponsiveLayout(Window window)
    {
        if (window is not MainWindow || window.Content is not Grid shell || shell.ColumnDefinitions.Count < 2) return;

        // Production navigation is an icon rail. Never grow it back to the legacy 228/252px sidebar.
        shell.ColumnDefinitions[0].Width = new GridLength(84);

        if (window.FindName("LibraryPanel") is not Grid library) return;
        var compact = window.ActualWidth > 0 && window.ActualWidth < 1120;
        library.Margin = compact ? new Thickness(22, 20, 22, 22) : new Thickness(30, 24, 30, 28);

        var libraryBody = library.Children
            .OfType<Grid>()
            .FirstOrDefault(grid => Grid.GetRow(grid) >= 1 && grid.ColumnDefinitions.Count >= 2);
        if (libraryBody is null) return;

        libraryBody.ColumnDefinitions[1].Width = new GridLength(compact ? 280 : 320);
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
