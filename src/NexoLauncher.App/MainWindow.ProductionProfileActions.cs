using System.Windows;
using System.Windows.Controls;

namespace NexoLauncher.App;

public partial class MainWindow
{
    private Button? boostQuickButton;

    private void InitializeVisibleProfileActions()
    {
        if (boostQuickButton is not null || rightShiftButton?.Parent is not Grid actionsGrid) return;

        ExpandProfileDetailsColumn();
        ReflowProfileActions(actionsGrid);

        boostQuickButton = new Button
        {
            Content = "NEXO BOOST",
            Height = 38,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 8, 5, 0),
            FontSize = 10,
            ToolTip = "Activar o actualizar el perfil de rendimiento NEXO Boost",
            IsEnabled = InstancesList.SelectedItem is not null && !busy && activeLaunch is null
        };
        boostQuickButton.SetResourceReference(Control.StyleProperty, "SecondaryButton");
        boostQuickButton.Click += ApplyNexoBoost_Click;
        Grid.SetRow(boostQuickButton, 3);
        Grid.SetColumn(boostQuickButton, 0);
        actionsGrid.Children.Add(boostQuickButton);

        Grid.SetRow(rightShiftButton, 3);
        Grid.SetColumn(rightShiftButton, 1);
        Grid.SetColumnSpan(rightShiftButton, 1);
        rightShiftButton.Height = 38;
        rightShiftButton.Padding = new Thickness(12, 6, 12, 6);
        rightShiftButton.Margin = new Thickness(5, 8, 0, 0);
        rightShiftButton.FontSize = 10;
        rightShiftButton.Content = NormalizeRightShiftLabel(rightShiftButton.Content);

        InstancesList.SelectionChanged += (_, _) => RefreshVisibleProfileActions();
        RefreshVisibleProfileActions();
    }

    private void ReflowProfileActions(Grid actionsGrid)
    {
        actionsGrid.ColumnDefinitions.Clear();
        actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        actionsGrid.RowDefinitions.Clear();
        for (var index = 0; index < 4; index++)
            actionsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        ConfigureActionButton(LibraryPlayButton, 0, 0, 2, new Thickness(0, 0, 0, 8));
        LibraryPlayButton.Height = 46;
        LibraryPlayButton.Content = "▶  INICIAR";
        LibraryPlayButton.SetResourceReference(Control.StyleProperty, "Nexo.PrimaryButton");

        ConfigureActionButton(DeleteInstanceButton, 1, 0, 1, new Thickness(0, 0, 5, 0));
        DeleteInstanceButton.Content = "BORRAR";

        ConfigureActionButton(EditInstanceButton, 1, 1, 1, new Thickness(5, 0, 0, 0));
        EditInstanceButton.Content = "EDITAR";

        ConfigureActionButton(ContentInstanceButton, 2, 0, 1, new Thickness(0, 8, 5, 0));
        ContentInstanceButton.Content = "CONTENIDO";

        ConfigureActionButton(OpenContentFolderButton, 2, 1, 1, new Thickness(5, 8, 0, 0));
        OpenContentFolderButton.Content = "CARPETA";
    }

    private static void ConfigureActionButton(Button button, int row, int column, int columnSpan, Thickness margin)
    {
        Grid.SetRow(button, row);
        Grid.SetColumn(button, column);
        Grid.SetColumnSpan(button, columnSpan);
        Grid.SetRowSpan(button, 1);
        button.Height = 38;
        button.MinWidth = 0;
        button.Padding = new Thickness(10, 6, 10, 6);
        button.Margin = margin;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.FontSize = 10;
    }

    private void ExpandProfileDetailsColumn()
    {
        var body = LibraryPanel.Children
            .OfType<Grid>()
            .FirstOrDefault(grid => Grid.GetRow(grid) >= 1 && grid.ColumnDefinitions.Count >= 2);
        if (body is not null)
            body.ColumnDefinitions[1].Width = new GridLength(350);
    }

    private static string NormalizeRightShiftLabel(object? value)
    {
        var label = value?.ToString() ?? "RIGHT SHIFT";
        label = label.Replace("＋ ", string.Empty, StringComparison.Ordinal)
            .Replace("✓ ", string.Empty, StringComparison.Ordinal);

        return label.StartsWith("RIGHT SHIFT", StringComparison.OrdinalIgnoreCase)
            ? label
            : "RIGHT SHIFT";
    }

    private void RefreshVisibleProfileActions()
    {
        if (boostQuickButton is not null)
            boostQuickButton.IsEnabled = InstancesList.SelectedItem is not null && !busy && activeLaunch is null;
    }
}
