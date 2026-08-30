using System.Windows;
using System.Windows.Controls;

namespace NexoLauncher.App;

public partial class MainWindow
{
    private Button? boostQuickButton;

    private void InitializeVisibleProfileActions()
    {
        if (boostQuickButton is not null || rightShiftButton?.Parent is not Grid actionsGrid) return;

        Grid.SetColumn(rightShiftButton, 1);
        Grid.SetColumnSpan(rightShiftButton, 1);
        rightShiftButton.Margin = new Thickness(0, 8, 9, 0);
        rightShiftButton.Content = rightShiftButton.Content?.ToString()?.Replace("＋ ", string.Empty, StringComparison.Ordinal) ?? "RIGHT SHIFT";

        boostQuickButton = new Button
        {
            Content = "NEXO BOOST",
            Height = 32,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 8, 8, 0),
            FontSize = 10,
            ToolTip = "Activar o actualizar el perfil de rendimiento NEXO Boost",
            IsEnabled = InstancesList.SelectedItem is not null && !busy && activeLaunch is null
        };
        boostQuickButton.SetResourceReference(Control.StyleProperty, "SecondaryButton");
        boostQuickButton.Click += ApplyNexoBoost_Click;
        Grid.SetRow(boostQuickButton, 2);
        Grid.SetColumn(boostQuickButton, 0);
        actionsGrid.Children.Add(boostQuickButton);

        InstancesList.SelectionChanged += (_, _) => RefreshVisibleProfileActions();
        RefreshVisibleProfileActions();
    }

    private void RefreshVisibleProfileActions()
    {
        if (boostQuickButton is not null)
            boostQuickButton.IsEnabled = InstancesList.SelectedItem is not null && !busy && activeLaunch is null;
    }
}
