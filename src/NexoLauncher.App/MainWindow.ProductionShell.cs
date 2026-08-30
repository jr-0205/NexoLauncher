using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace NexoLauncher.App;

public partial class MainWindow
{
    private bool productionShellApplied;
    private TextBlock? productionPageTitle;

    internal void ApplyProductionShell()
    {
        if (productionShellApplied || Content is not Grid shell || shell.ColumnDefinitions.Count < 2) return;
        productionShellApplied = true;

        MinWidth = 960;
        MinHeight = 640;
        Icon = System.Windows.Application.Current.TryFindResource("Nexo.BrandMark") as ImageSource;
        shell.Background = ResourceBrush("Nexo.Background", Brushes.Black);
        shell.ColumnDefinitions[0].Width = new GridLength(84);

        var sidebar = shell.Children.OfType<Border>().FirstOrDefault(value => Grid.GetColumn(value) == 0);
        if (sidebar is not null) ConfigureProductionSidebar(sidebar);

        var contentHost = shell.Children.OfType<Grid>().FirstOrDefault(value => Grid.GetColumn(value) == 1);
        if (contentHost is not null) ConfigureProductionTopBar(contentHost);

        ConfigureContentExperience();
        ConfigureLibraryExperience();

        foreach (var button in new[] { LibraryNavButton, InstallNavButton, ContentNavButton, SettingsNavButton })
            button.Click += (_, _) => _ = Dispatcher.BeginInvoke(UpdateProductionNavigation, DispatcherPriority.ContextIdle);

        foreach (var panel in new FrameworkElement[] { LibraryPanel, InstallPanel, ContentPanel, SettingsPanel })
            panel.IsVisibleChanged += (_, _) => _ = Dispatcher.BeginInvoke(UpdateProductionNavigation, DispatcherPriority.ContextIdle);

        ContentInstanceBox.SelectionChanged += (_, _) => _ = Dispatcher.BeginInvoke(NormalizeContentLanguage, DispatcherPriority.ContextIdle);
        _ = Dispatcher.BeginInvoke(UpdateProductionNavigation, DispatcherPriority.ContextIdle);
    }

    private void ConfigureProductionSidebar(Border sidebar)
    {
        sidebar.Background = ResourceBrush("Nexo.Sidebar", new SolidColorBrush(Color.FromRgb(8, 12, 18)));
        sidebar.BorderBrush = ResourceBrush("Nexo.Border", Brushes.Transparent);

        if (sidebar.Child is not Grid sidebarGrid) return;
        sidebarGrid.Margin = new Thickness(10, 16, 10, 14);

        var brand = sidebarGrid.Children.OfType<StackPanel>().FirstOrDefault(value => Grid.GetRow(value) == 0);
        if (brand is not null)
        {
            brand.Children.Clear();
            brand.HorizontalAlignment = HorizontalAlignment.Center;
            brand.Children.Add(new Image
            {
                Width = 52,
                Height = 52,
                Source = System.Windows.Application.Current.TryFindResource("Nexo.BrandMark") as ImageSource,
                Stretch = Stretch.Uniform,
                ToolTip = "NEXO Client",
                SnapsToDevicePixels = true,
                RenderTransformOrigin = new Point(0.5, 0.5)
            });
        }

        var navigation = sidebarGrid.Children.OfType<StackPanel>().FirstOrDefault(value => Grid.GetRow(value) == 1);
        if (navigation is not null)
        {
            navigation.Margin = new Thickness(0, 28, 0, 0);
            foreach (var heading in navigation.Children.OfType<TextBlock>()) heading.Visibility = Visibility.Collapsed;
        }

        ConfigureRailButton(LibraryNavButton, "\uE80F", "Biblioteca");
        ConfigureRailButton(InstallNavButton, "\uE710", "Crear perfil");
        ConfigureRailButton(ContentNavButton, "\uE896", "Contenido");
        ConfigureRailButton(SettingsNavButton, "\uE713", "Configuración");

        var footer = sidebarGrid.Children.OfType<StackPanel>().FirstOrDefault(value => Grid.GetRow(value) == 2);
        if (footer is null) return;

        var status = footer.Children.OfType<Border>().FirstOrDefault();
        if (status is not null)
        {
            status.Width = 42;
            status.Height = 34;
            status.Padding = new Thickness(0);
            status.HorizontalAlignment = HorizontalAlignment.Center;
            status.Background = ResourceBrush("Nexo.Surface", status.Background);
            status.BorderBrush = ResourceBrush("Nexo.Border", Brushes.Transparent);
            status.BorderThickness = new Thickness(1);
            status.CornerRadius = new CornerRadius(11);
            status.ToolTip = "Estado de NEXO";
            if (status.Child is StackPanel statusStack)
            {
                statusStack.HorizontalAlignment = HorizontalAlignment.Center;
                statusStack.VerticalAlignment = VerticalAlignment.Center;
                foreach (var text in statusStack.Children.OfType<TextBlock>()) text.Visibility = Visibility.Collapsed;
            }
        }

        foreach (var child in footer.Children.Cast<UIElement>().Skip(1)) child.Visibility = Visibility.Collapsed;
    }

    private static void ConfigureRailButton(Button button, string glyph, string tooltip)
    {
        button.Width = 52;
        button.Height = 52;
        button.Padding = new Thickness(0);
        button.Margin = new Thickness(0, 0, 0, 8);
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.BorderThickness = new Thickness(1);
        button.ToolTip = tooltip;
        button.Content = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void ConfigureProductionTopBar(Grid contentHost)
    {
        if (contentHost.RowDefinitions.Count != 0) return;

        contentHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
        contentHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        foreach (var child in contentHost.Children.Cast<UIElement>().ToArray()) Grid.SetRow(child, 1);

        var bar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(11, 16, 24)),
            BorderBrush = ResourceBrush("Nexo.Border", Brushes.Transparent),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(24, 0, 22, 0)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Child = grid;

        productionPageTitle = new TextBlock
        {
            Text = "Biblioteca",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(productionPageTitle);

        var statusPill = new Border
        {
            Background = ResourceBrush("Nexo.Surface", Brushes.Transparent),
            BorderBrush = ResourceBrush("Nexo.Border", Brushes.Transparent),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(11, 6, 11, 6),
            VerticalAlignment = VerticalAlignment.Center
        };
        var statusStack = new StackPanel { Orientation = Orientation.Horizontal };
        statusStack.Children.Add(new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = ResourceBrush("Nexo.Success", Brushes.LightGreen),
            VerticalAlignment = VerticalAlignment.Center
        });
        var statusText = new TextBlock
        {
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        statusText.SetResourceReference(TextBlock.ForegroundProperty, "Nexo.TextMuted");
        statusText.SetBinding(TextBlock.TextProperty, new Binding(nameof(TextBlock.Text)) { Source = SidebarStatusText });
        statusStack.Children.Add(statusText);
        statusPill.Child = statusStack;
        Grid.SetColumn(statusPill, 1);
        grid.Children.Add(statusPill);

        Grid.SetRow(bar, 0);
        contentHost.Children.Add(bar);
    }

    private void ConfigureContentExperience()
    {
        if (ContentPanel.Content is not Grid root) return;
        root.MaxWidth = double.PositiveInfinity;
        root.HorizontalAlignment = HorizontalAlignment.Stretch;
        root.Margin = new Thickness(30, 24, 30, 32);

        ContentProfileNameText.FontSize = 27;
        ContentProfileMetaText.SetResourceReference(TextBlock.ForegroundProperty, "Nexo.TextMuted");

        ContentPlayButton.SetResourceReference(Control.StyleProperty, "Nexo.PrimaryButton");
        AddContentFilesButton.SetResourceReference(Control.StyleProperty, "Nexo.SecondaryButton");
        AddContentFilesButton.Content = "＋  AÑADIR";
        ImportModpackButton.SetResourceReference(Control.StyleProperty, "Nexo.PrimaryButton");
        ContentInstanceBox.SetResourceReference(Control.StyleProperty, "Nexo.ComboBox");
        ContentTypeBox.SetResourceReference(Control.StyleProperty, "Nexo.ComboBox");
        ContentSearchBox.SetResourceReference(Control.StyleProperty, "Nexo.Field");

        foreach (var text in EnumerateVisualChildren<TextBlock>(root))
        {
            if (string.Equals(text.Text, "Instancia", StringComparison.Ordinal)) text.Text = "Perfil";
            else if (text.Text.Contains("instancia", StringComparison.OrdinalIgnoreCase))
                text.Text = text.Text.Replace("instancia", "perfil", StringComparison.OrdinalIgnoreCase);
        }

        foreach (var button in EnumerateVisualChildren<Button>(root))
        {
            if (button.Content is not string label) continue;
            if (label is "CATÁLOGO" or "ARCHIVOS" or "MUNDOS" or "LOGS")
            {
                button.SetResourceReference(Control.StyleProperty, label == "CATÁLOGO" ? "Nexo.SecondaryButton" : "Nexo.GhostButton");
                button.Height = 38;
                button.Padding = new Thickness(14, 7, 14, 7);
            }
        }

        var filters = root.Children.OfType<Border>().FirstOrDefault(value => Grid.GetRow(value) == 1);
        if (filters is not null)
        {
            filters.Background = ResourceBrush("Nexo.Surface", filters.Background);
            filters.BorderBrush = ResourceBrush("Nexo.Border", filters.BorderBrush);
            filters.BorderThickness = new Thickness(1);
            filters.CornerRadius = new CornerRadius(12);
            filters.Padding = new Thickness(16);
            filters.Margin = new Thickness(0, 18, 0, 16);
        }

        ContentResultsList.Margin = new Thickness(0, 2, 0, 0);
        NormalizeContentLanguage();
    }

    private void NormalizeContentLanguage()
    {
        if (ContentProfileMetaText is null) return;
        ContentProfileMetaText.Text = ContentProfileMetaText.Text.Replace("instancia", "perfil", StringComparison.OrdinalIgnoreCase);
    }

    private void ConfigureLibraryExperience()
    {
        LibraryPanel.Margin = new Thickness(30, 24, 30, 28);
        if (LibraryPanel.Children.OfType<Grid>().FirstOrDefault(grid => Grid.GetRow(grid) >= 1 && grid.ColumnDefinitions.Count >= 2) is { } body)
            body.ColumnDefinitions[1].Width = new GridLength(320);
    }

    private void UpdateProductionNavigation()
    {
        Button active;
        string title;
        if (ContentPanel.Visibility == Visibility.Visible) { active = ContentNavButton; title = "Contenido"; }
        else if (SettingsPanel.Visibility == Visibility.Visible) { active = SettingsNavButton; title = "Configuración"; }
        else if (InstallPanel.Visibility == Visibility.Visible) { active = InstallNavButton; title = "Crear perfil"; }
        else { active = LibraryNavButton; title = "Biblioteca"; }

        if (productionPageTitle is not null) productionPageTitle.Text = title;
        foreach (var button in new[] { LibraryNavButton, InstallNavButton, ContentNavButton, SettingsNavButton })
        {
            var isActive = ReferenceEquals(button, active);
            button.Background = isActive ? new SolidColorBrush(Color.FromRgb(25, 43, 76)) : Brushes.Transparent;
            button.BorderBrush = isActive ? ResourceBrush("Nexo.Accent", Brushes.CornflowerBlue) : Brushes.Transparent;
            button.Foreground = isActive ? Brushes.White : ResourceBrush("Nexo.TextMuted", Brushes.Gray);
        }
    }

    private Brush ResourceBrush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
}
