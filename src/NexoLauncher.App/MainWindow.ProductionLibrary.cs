using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexoLauncher.Core.Installation;
using NexoLauncher.Domain.Instances;

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

        Resources["Nexo.ProfileIconConverter"] = new ProfileArtworkConverter(ProfileArtworkKind.Icon);
        Resources["Nexo.ProfileBackgroundConverter"] = new ProfileArtworkConverter(ProfileArtworkKind.Background);
        InstancesList.ItemTemplate = (DataTemplate)XamlReader.Parse(ProfileCardTemplateXaml);

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
        };
        UpdateContinuePlayingCard();
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

    private const string ProfileCardTemplateXaml = """
<DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
  <Border Width="248" Height="184" CornerRadius="12" ClipToBounds="True" Background="#121924" BorderBrush="#26364D" BorderThickness="1">
    <Grid>
      <Border>
        <Border.Background>
          <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
            <GradientStop Color="#203F86"/>
            <GradientStop Color="#172131" Offset="0.55"/>
            <GradientStop Color="#090D13" Offset="1"/>
          </LinearGradientBrush>
        </Border.Background>
      </Border>
      <Image Source="{Binding Id, Converter={StaticResource Nexo.ProfileBackgroundConverter}}" Stretch="UniformToFill" Opacity="0.78"/>
      <Border>
        <Border.Background>
          <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
            <GradientStop Color="#15090D13"/>
            <GradientStop Color="#45090D13" Offset="0.48"/>
            <GradientStop Color="#F2090D13" Offset="1"/>
          </LinearGradientBrush>
        </Border.Background>
      </Border>
      <Grid Margin="14">
        <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
        <Grid>
          <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
          <Border Width="54" Height="54" CornerRadius="10" Background="#E6172131" BorderBrush="#38506F" BorderThickness="1" HorizontalAlignment="Left" ClipToBounds="True">
            <Grid>
              <Image Source="{DynamicResource Nexo.BrandMark}" Stretch="Uniform" Margin="9"/>
              <Image Source="{Binding Id, Converter={StaticResource Nexo.ProfileIconConverter}}" Stretch="UniformToFill"/>
            </Grid>
          </Border>
          <Border Grid.Column="1" Background="#D0172131" CornerRadius="7" Padding="8,5" VerticalAlignment="Top">
            <TextBlock Text="NEXO" Foreground="#8DB3FF" FontSize="8" FontWeight="Bold"/>
          </Border>
        </Grid>
        <StackPanel Grid.Row="2">
          <TextBlock Text="{Binding Name}" Foreground="#F4F7FC" FontSize="15" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
          <TextBlock Text="{Binding Subtitle}" Foreground="#B4C0D2" FontSize="10" Margin="0,4,0,0" TextTrimming="CharacterEllipsis"/>
          <StackPanel Orientation="Horizontal" Margin="0,9,0,0">
            <Ellipse Width="6" Height="6" Fill="#4FC39B" VerticalAlignment="Center"/>
            <TextBlock Text="  LISTO" Foreground="#80D9B9" FontSize="8" FontWeight="Bold" VerticalAlignment="Center"/>
          </StackPanel>
        </StackPanel>
      </Grid>
    </Grid>
  </Border>
</DataTemplate>
""";
}

internal enum ProfileArtworkKind
{
    Icon,
    Background
}

internal sealed class ProfileArtworkConverter(ProfileArtworkKind kind) : IValueConverter
{
    private readonly NexoPaths paths = NexoPaths.ForCurrentUser();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not InstanceId id) return null;
        var root = Path.Combine(paths.Instances, id.ToString());
        var artwork = ProfileArtworkStore.Load(root);
        var relative = kind == ProfileArtworkKind.Icon ? artwork?.IconRelativePath : artwork?.BackgroundRelativePath;
        var resolved = ProfileArtworkStore.Resolve(root, relative);
        if (resolved is null) return null;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(resolved, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
