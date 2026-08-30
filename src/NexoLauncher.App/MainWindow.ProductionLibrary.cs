using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace NexoLauncher.App;

public partial class MainWindow
{
    private bool productionLibraryInitialized;
    private TextBox? librarySearchBox;
    private TextBlock? libraryResultsText;
    private Border? continuePlayingCard;
    private TextBlock? continuePlayingName;
    private TextBlock? continuePlayingMeta;
    private Button? continuePlayingButton;

    private void InitializeProductionLibraryExperience()
    {
        if (productionLibraryInitialized) return;
        productionLibraryInitialized = true;

        if (TryFindResource("Nexo.ProfileCardTemplate") is DataTemplate template)
            InstancesList.ItemTemplate = template;

        if (LibraryPanel.RowDefinitions.Count >= 2)
        {
            var body = LibraryPanel.Children.Cast<UIElement>().FirstOrDefault(value => Grid.GetRow(value) == 1);
            LibraryPanel.RowDefinitions.Insert(1, new RowDefinition { Height = GridLength.Auto });
            if (body is not null) Grid.SetRow(body, 2);
            var experience = CreateLibraryExperienceHeader();
            Grid.SetRow(experience, 1);
            LibraryPanel.Children.Add(experience);
        }

        InstancesList.SelectionChanged += (_, _) =>
        {
            UpdateContinuePlayingCard();
            ApplyLibraryFilter();
            RefreshVisibleProfileActions();
        };
        UpdateContinuePlayingCard();
        _ = Dispatcher.InvokeAsync(InitializeVisibleProfileActions);
    }

    private FrameworkElement CreateLibraryExperienceHeader()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 20, 0, 2) };

        continuePlayingCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(18, 25, 36)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(38, 54, 77)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 13, 14, 13),
            Margin = new Thickness(0, 0, 0, 14),
            Visibility = Visibility.Collapsed
        };
        var quickGrid = new Grid();
        quickGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        quickGrid.ColumnDefinitions.Add(new ColumnDefinition());
        quickGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        continuePlayingCard.Child = quickGrid;

        var mark = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromRgb(23, 39, 70)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(56, 80, 111)),
            BorderThickness = new Thickness(1)
        };
        mark.Child = new Image
        {
            Source = Application.Current.Resources["Nexo.BrandMark"] as ImageSource,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(6)
        };
        quickGrid.Children.Add(mark);

        var copy = new StackPanel { Margin = new Thickness(13, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
        continuePlayingName = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold };
        continuePlayingMeta = new TextBlock { FontSize = 10, Margin = new Thickness(0, 3, 0, 0) };
        continuePlayingMeta.SetResourceReference(TextBlock.ForegroundProperty, "Nexo.TextMuted");
        copy.Children.Add(new TextBlock
        {
            Text = "CONTINUAR JUGANDO",
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(91, 140, 255))
        });
        copy.Children.Add(continuePlayingName);
        copy.Children.Add(continuePlayingMeta);
        Grid.SetColumn(copy, 1);
        quickGrid.Children.Add(copy);

        continuePlayingButton = new Button
        {
            Content = "▶  JUGAR",
            MinWidth = 112,
            VerticalAlignment = VerticalAlignment.Center
        };
        continuePlayingButton.SetResourceReference(Control.StyleProperty, "Nexo.PrimaryButton");
        continuePlayingButton.Click += LibraryPlay_Click;
        Grid.SetColumn(continuePlayingButton, 2);
        quickGrid.Children.Add(continuePlayingButton);

        stack.Children.Add(continuePlayingCard);

        var tools = new Grid();
        tools.ColumnDefinitions.Add(new ColumnDefinition());
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        librarySearchBox = new TextBox
        {
            ToolTip = "Buscar por nombre, versión o loader",
            MinWidth = 280
        };
        librarySearchBox.SetResourceReference(Control.StyleProperty, "Nexo.Field");
        librarySearchBox.TextChanged += (_, _) => ApplyLibraryFilter();
        tools.Children.Add(librarySearchBox);

        libraryResultsText = new TextBlock
        {
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 10
        };
        libraryResultsText.SetResourceReference(TextBlock.ForegroundProperty, "Nexo.TextMuted");
        Grid.SetColumn(libraryResultsText, 1);
        tools.Children.Add(libraryResultsText);
        stack.Children.Add(tools);
        return stack;
    }

    private void ApplyLibraryFilter()
    {
        if (InstancesList.ItemsSource is null) return;
        var query = librarySearchBox?.Text.Trim() ?? string.Empty;
        var view = CollectionViewSource.GetDefaultView(InstancesList.ItemsSource);
        view.Filter = item => item is InstanceItem instance &&
                              (query.Length == 0 ||
                               instance.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                               instance.VersionId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                               instance.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase));
        view.Refresh();
        if (libraryResultsText is not null)
            libraryResultsText.Text = query.Length == 0
                ? $"{view.Cast<object>().Count()} perfil(es)"
                : $"{view.Cast<object>().Count()} resultado(s)";
    }

    private void UpdateContinuePlayingCard()
    {
        if (continuePlayingCard is null || continuePlayingName is null || continuePlayingMeta is null || continuePlayingButton is null) return;
        if (InstancesList.SelectedItem is not InstanceItem item)
        {
            continuePlayingCard.Visibility = Visibility.Collapsed;
            return;
        }

        continuePlayingName.Text = item.Name;
        continuePlayingMeta.Text = item.Subtitle;
        continuePlayingButton.IsEnabled = !busy && activeLaunch is null && !launchStarting;
        continuePlayingCard.Visibility = Visibility.Visible;
    }
}
