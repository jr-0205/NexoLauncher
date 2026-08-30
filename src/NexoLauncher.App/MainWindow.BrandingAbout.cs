using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NexoLauncher.App;

public partial class MainWindow
{
    private bool brandingInitialized;

    private void InitializeBrandingAndAbout()
    {
        if (brandingInitialized) return;
        brandingInitialized = true;

        ApplyBrandMarkToSidebar();
        AddAboutCard();
    }

    private void ApplyBrandMarkToSidebar()
    {
        var title = EnumerateVisualChildren<TextBlock>(this)
            .FirstOrDefault(value => string.Equals(value.Text, "NEXO", StringComparison.Ordinal) && value.FontSize >= 24);
        if (title?.Parent is not StackPanel stack) return;

        var mark = new Image
        {
            Width = 62,
            Height = 48,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 9),
            Source = Application.Current.Resources["Nexo.BrandMark"] as ImageSource,
            ToolTip = "NEXO Client"
        };
        stack.Children.Insert(0, mark);
        title.FontSize = 24;
    }

    private void AddAboutCard()
    {
        if (SettingsPanel.Content is not Grid settingsGrid) return;

        var row = settingsGrid.RowDefinitions.Count;
        settingsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var card = new Border { Margin = new Thickness(0, 18, 0, 0) };
        card.SetResourceReference(Border.StyleProperty, "Nexo.Card");

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        card.Child = root;

        var logoTile = new Border
        {
            Width = 62,
            Height = 62,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromRgb(14, 22, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(56, 80, 111)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top
        };
        logoTile.Child = new Image
        {
            Source = Application.Current.Resources["Nexo.BrandMark"] as ImageSource,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(8)
        };
        root.Children.Add(logoTile);

        var content = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        Grid.SetColumn(content, 1);
        root.Children.Add(content);

        var eyebrow = new TextBlock
        {
            Text = "ACERCA DE NEXO",
            FontSize = 9,
            FontWeight = FontWeights.Bold
        };
        eyebrow.SetResourceReference(TextBlock.ForegroundProperty, "Nexo.Accent");
        content.Children.Add(eyebrow);

        content.Children.Add(new TextBlock
        {
            Text = $"NEXO Client {GetProductVersion()}",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 5, 0, 0)
        });

        var creator = new TextBlock
        {
            Text = "Creado por jr-0205",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 5, 0, 0)
        };
        creator.SetResourceReference(TextBlock.ForegroundProperty, "Nexo.TextSecondary");
        content.Children.Add(creator);

        var actions = new WrapPanel { Margin = new Thickness(0, 15, 0, 0) };
        actions.Children.Add(CreateExternalLinkButton("GITHUB DEL CREADOR", "https://github.com/jr-0205", false));
        var chatGpt = CreateExternalLinkButton("DESCARGAR CHATGPT", "https://chatgpt.com/download/", true);
        chatGpt.Margin = new Thickness(9, 0, 0, 0);
        actions.Children.Add(chatGpt);
        content.Children.Add(actions);

        var note = new TextBlock
        {
            Text = "NEXO es un proyecto independiente. Los enlaces externos se ofrecen únicamente como accesos de conveniencia; NEXO no está afiliado ni patrocinado por OpenAI, Mojang/Microsoft, Modrinth o Lunar Client.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10,
            Margin = new Thickness(0, 13, 0, 0)
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Nexo.TextMuted");
        content.Children.Add(note);

        Grid.SetRow(card, row);
        settingsGrid.Children.Add(card);
    }

    private Button CreateExternalLinkButton(string label, string url, bool primary)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = primary ? 164 : 160,
            Padding = new Thickness(15, 8, 15, 8),
            ToolTip = url
        };
        button.SetResourceReference(Control.StyleProperty, primary ? "Nexo.PrimaryButton" : "Nexo.SecondaryButton");
        button.Click += (_, _) => OpenExternalUrl(url);
        return button;
    }

    private void OpenExternalUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Abrir enlace", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string GetProductVersion()
    {
        var assembly = typeof(MainWindow).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational)) return informational.Split('+')[0];
        return assembly.GetName().Version?.ToString(3) ?? "0.5.2";
    }
}
