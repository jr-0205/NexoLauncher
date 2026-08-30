using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NexoLauncher.Infrastructure.Content;

namespace NexoLauncher.App;

internal sealed class InstalledContentView : Grid
{
    private readonly TextBox searchBox;
    private readonly StackPanel list;
    private IReadOnlyList<InstalledContentEntry> items = [];

    public event EventHandler? AddContentRequested;
    public event EventHandler<InstalledContentEntry>? ToggleRequested;
    public event EventHandler<InstalledContentEntry>? DeleteRequested;
    public event EventHandler<InstalledContentEntry>? OpenRequested;

    public InstalledContentView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var heading = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Children.Add(heading);

        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock
        {
            Text = "CONTENIDO INSTALADO",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("Nexo.Accent", Color.FromRgb(91, 140, 255))
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Administra primero lo que ya pertenece a este perfil.",
            FontSize = 12,
            Foreground = Brush("Nexo.TextMuted", Color.FromRgb(126, 140, 163)),
            Margin = new Thickness(0, 5, 0, 0)
        });
        heading.Children.Add(titleStack);

        var add = new Button
        {
            Content = "＋  AGREGAR CONTENIDO",
            Height = 42,
            Padding = new Thickness(16, 7, 16, 7),
            Margin = new Thickness(18, 0, 0, 0),
            MinWidth = 176
        };
        SetStyle(add, "Nexo.PrimaryButton");
        add.Click += (_, _) => AddContentRequested?.Invoke(this, EventArgs.Empty);
        Grid.SetColumn(add, 1);
        heading.Children.Add(add);

        searchBox = new TextBox
        {
            Height = 42,
            Margin = new Thickness(0, 0, 0, 14),
            ToolTip = "Filtrar contenido instalado por nombre"
        };
        SetStyle(searchBox, "Nexo.Field");
        searchBox.TextChanged += (_, _) => Render();
        Grid.SetRow(searchBox, 1);
        Children.Add(searchBox);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        list = new StackPanel { Margin = new Thickness(0, 0, 8, 20) };
        scroll.Content = list;
        Grid.SetRow(scroll, 2);
        Children.Add(scroll);
    }

    public void SetItems(IReadOnlyList<InstalledContentEntry> entries)
    {
        items = entries;
        Render();
    }

    private void Render()
    {
        list.Children.Clear();
        var filter = searchBox.Text.Trim();
        var filtered = string.IsNullOrWhiteSpace(filter)
            ? items
            : items.Where(value => value.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToArray();

        if (filtered.Count == 0)
        {
            var empty = new Border
            {
                Background = Brush("Nexo.Surface", Color.FromRgb(18, 25, 36)),
                BorderBrush = Brush("Nexo.Border", Color.FromRgb(38, 54, 77)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(22),
                Margin = new Thickness(0, 0, 0, 12)
            };
            empty.Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Este perfil todavía no tiene contenido administrable.", FontSize = 14, FontWeight = FontWeights.SemiBold },
                    new TextBlock { Text = "Usa AGREGAR CONTENIDO para buscar mods, texturas, shaders o datapacks compatibles.", FontSize = 11, Foreground = Brush("Nexo.TextMuted", Color.FromRgb(126, 140, 163)), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap }
                }
            };
            list.Children.Add(empty);
            return;
        }

        foreach (var group in filtered.GroupBy(value => value.Category))
        {
            var header = new Grid { Margin = new Thickness(2, list.Children.Count == 0 ? 0 : 12, 2, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock
            {
                Text = group.Key,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("Nexo.TextSecondary", Color.FromRgb(180, 192, 210)),
                VerticalAlignment = VerticalAlignment.Center
            });
            var count = new TextBlock
            {
                Text = group.Count().ToString(),
                FontSize = 9,
                Foreground = Brush("Nexo.TextMuted", Color.FromRgb(126, 140, 163)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(count, 1);
            header.Children.Add(count);
            list.Children.Add(header);

            foreach (var item in group)
                list.Children.Add(CreateRow(item));
        }
    }

    private Border CreateRow(InstalledContentEntry item)
    {
        var row = new Border
        {
            Background = Brush("Nexo.Surface", Color.FromRgb(18, 25, 36)),
            BorderBrush = Brush("Nexo.Border", Color.FromRgb(38, 54, 77)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 11, 12, 11),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Child = grid;

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = item.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        text.Children.Add(new TextBlock
        {
            Text = item.IsDirectory ? "Carpeta" : $"{Size(item.SizeBytes)} · {item.RelativePath}",
            FontSize = 9,
            Foreground = Brush("Nexo.TextMuted", Color.FromRgb(126, 140, 163)),
            Margin = new Thickness(0, 4, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        grid.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        if (item.CanToggle)
        {
            var toggle = SmallButton(item.Enabled ? "ACTIVO" : "DESACTIVADO", item.Enabled ? "Nexo.SecondaryButton" : "Nexo.GhostButton");
            toggle.ToolTip = item.Enabled ? "Desactivar este mod sin borrarlo" : "Volver a activar este mod";
            toggle.Click += (_, _) => ToggleRequested?.Invoke(this, item);
            actions.Children.Add(toggle);
        }
        var open = SmallButton("ABRIR", "Nexo.GhostButton");
        open.Margin = new Thickness(actions.Children.Count == 0 ? 0 : 7, 0, 0, 0);
        open.Click += (_, _) => OpenRequested?.Invoke(this, item);
        actions.Children.Add(open);
        var delete = SmallButton("ELIMINAR", "Nexo.GhostButton");
        delete.Foreground = new SolidColorBrush(Color.FromRgb(255, 142, 142));
        delete.Margin = new Thickness(7, 0, 0, 0);
        delete.Click += (_, _) => DeleteRequested?.Invoke(this, item);
        actions.Children.Add(delete);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return row;
    }

    private static Button SmallButton(string label, string styleKey)
    {
        var button = new Button
        {
            Content = label,
            Height = 32,
            MinWidth = 76,
            Padding = new Thickness(10, 5, 10, 5),
            FontSize = 9
        };
        SetStyle(button, styleKey);
        return button;
    }

    private static void SetStyle(Control control, string key)
    {
        if (System.Windows.Application.Current.TryFindResource(key) is Style style)
            control.Style = style;
    }

    private static Brush Brush(string key, Color fallback)
        => System.Windows.Application.Current.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    private static string Size(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024 * 1024):0.##} GB";
        if (bytes >= 1024L * 1024) return $"{bytes / (1024d * 1024):0.##} MB";
        if (bytes >= 1024) return $"{bytes / 1024d:0.#} KB";
        return $"{bytes} B";
    }
}
