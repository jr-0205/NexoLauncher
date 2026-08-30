using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NexoLauncher.App;

internal enum NexoDialogKind
{
    Information,
    Confirmation,
    Warning,
    Error
}

internal static class NexoDialog
{
    public static bool Confirm(Window owner, string title, string message, string primaryText = "ACEPTAR", string secondaryText = "CANCELAR", string? details = null)
        => Show(owner, title, message, NexoDialogKind.Confirmation, primaryText, secondaryText, details, destructive: false);

    public static bool ConfirmDanger(Window owner, string title, string message, string primaryText = "ELIMINAR", string secondaryText = "CANCELAR", string? details = null)
        => Show(owner, title, message, NexoDialogKind.Warning, primaryText, secondaryText, details, destructive: true);

    public static void Info(Window owner, string title, string message, string primaryText = "ACEPTAR", string? details = null)
        => Show(owner, title, message, NexoDialogKind.Information, primaryText, null, details, destructive: false);

    public static void Warning(Window owner, string title, string message, string primaryText = "ACEPTAR", string? details = null)
        => Show(owner, title, message, NexoDialogKind.Warning, primaryText, null, details, destructive: false);

    public static void Error(Window owner, string title, string message, string? details = null)
        => Show(owner, title, message, NexoDialogKind.Error, "CERRAR", null, details, destructive: false);

    private static bool Show(Window owner, string title, string message, NexoDialogKind kind, string primaryText, string? secondaryText, string? details, bool destructive)
    {
        var window = new Window
        {
            Owner = owner,
            Title = title + " · NEXO",
            Width = 560,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 720,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            Background = Brush("Nexo.Background", Color.FromRgb(9, 13, 19)),
            Foreground = Brush("Nexo.Text", Color.FromRgb(244, 247, 252)),
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            Icon = System.Windows.Application.Current.TryFindResource("Nexo.BrandMark") as ImageSource,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };
        NativeWindowTheme.ApplyDarkTitleBar(window);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        window.Content = root;

        var content = new Grid { Margin = new Thickness(28, 26, 28, 20) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(content);

        var accent = kind switch
        {
            NexoDialogKind.Warning => Color.FromRgb(230, 184, 92),
            NexoDialogKind.Error => Color.FromRgb(240, 120, 120),
            _ => Color.FromRgb(91, 140, 255)
        };
        var glyph = kind switch
        {
            NexoDialogKind.Warning => "!",
            NexoDialogKind.Error => "×",
            NexoDialogKind.Confirmation => "?",
            _ => "i"
        };

        var icon = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(12),
            Background = Brush("Nexo.SurfaceRaised", Color.FromRgb(23, 33, 49)),
            BorderBrush = Brush("Nexo.Border", Color.FromRgb(38, 54, 77)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(accent)
            }
        };
        content.Children.Add(icon);

        var stack = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        Grid.SetColumn(stack, 1);
        content.Children.Add(stack);
        stack.Children.Add(new TextBlock
        {
            Text = kind == NexoDialogKind.Error ? "NEXO · ERROR" : kind == NexoDialogKind.Warning ? "NEXO · ATENCIÓN" : "NEXO",
            Foreground = new SolidColorBrush(accent),
            FontSize = 9,
            FontWeight = FontWeights.Bold
        });
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        stack.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = Brush("Nexo.TextSecondary", Color.FromRgb(180, 192, 210)),
            FontSize = 12,
            Margin = new Thickness(0, 9, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19
        });

        if (!string.IsNullOrWhiteSpace(details))
        {
            var expander = new Expander
            {
                Header = "DETALLES TÉCNICOS",
                Foreground = Brush("Nexo.TextMuted", Color.FromRgb(126, 140, 163)),
                Margin = new Thickness(0, 16, 0, 0),
                Content = new Border
                {
                    Background = Brush("Nexo.Sidebar", Color.FromRgb(13, 19, 29)),
                    BorderBrush = Brush("Nexo.Border", Color.FromRgb(38, 54, 77)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 8, 0, 0),
                    Child = new ScrollViewer
                    {
                        MaxHeight = 180,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        Content = new TextBlock
                        {
                            Text = details,
                            Foreground = Brush("Nexo.TextMuted", Color.FromRgb(141, 154, 175)),
                            FontFamily = new FontFamily("Cascadia Mono"),
                            FontSize = 10,
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            };
            stack.Children.Add(expander);
        }

        var footer = new Border
        {
            Background = Brush("Nexo.Sidebar", Color.FromRgb(13, 19, 29)),
            BorderBrush = Brush("Nexo.Border", Color.FromRgb(29, 42, 60)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 14, 20, 14)
        };
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);
        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Child = footerGrid;
        footerGrid.Children.Add(new TextBlock
        {
            Text = destructive ? "Esta acción modifica archivos del perfil" : "NEXO Client",
            Foreground = Brush("Nexo.TextMuted", Color.FromRgb(99, 113, 137)),
            FontSize = 9,
            VerticalAlignment = VerticalAlignment.Center
        });

        var accepted = false;
        if (!string.IsNullOrWhiteSpace(secondaryText))
        {
            var secondary = CreateButton(secondaryText, "Nexo.SecondaryButton", 112);
            secondary.Margin = new Thickness(0, 0, 9, 0);
            secondary.Click += (_, _) => window.Close();
            Grid.SetColumn(secondary, 1);
            footerGrid.Children.Add(secondary);
        }

        var primary = CreateButton(primaryText, "Nexo.PrimaryButton", 126);
        if (destructive)
        {
            primary.Background = Brush("Nexo.Danger", Color.FromRgb(240, 120, 120));
            primary.Foreground = Brushes.White;
        }
        primary.Click += (_, _) => { accepted = true; window.Close(); };
        Grid.SetColumn(primary, 2);
        footerGrid.Children.Add(primary);

        window.ShowDialog();
        return accepted;
    }

    private static Button CreateButton(string text, string styleKey, double minWidth)
    {
        var button = new Button { Content = text, MinWidth = minWidth, Height = 40, Padding = new Thickness(14, 7, 14, 7) };
        if (System.Windows.Application.Current.TryFindResource(styleKey) is Style style)
            button.Style = style;
        else if (System.Windows.Application.Current.TryFindResource("Nexo.PrimaryButton") is Style fallback)
            button.Style = fallback;
        return button;
    }

    private static Brush Brush(string key, Color fallback)
        => System.Windows.Application.Current.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
}
