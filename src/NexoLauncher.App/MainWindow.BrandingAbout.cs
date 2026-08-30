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

        ApplyProductLanguage();
        AddAboutCard();
    }

    private void ApplyProductLanguage()
    {
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Mis instancias"] = "Biblioteca",
            ["INSTANCIA"] = "PERFIL",
            ["Aún no hay instancias"] = "Aún no hay perfiles",
            ["Crea una instalación para comenzar."] = "Crea un perfil para comenzar.",
            ["Nueva instalación"] = "Nuevo perfil",
            ["Instancia"] = "Perfil",
            ["Selecciona una instancia"] = "Selecciona un perfil",
            ["NEXO"] = "NEXA",
            ["NEXO CORE LISTO"] = "NEXA CORE LISTO"
        };

        foreach (var text in EnumerateVisualChildren<TextBlock>(this))
        {
            if (replacements.TryGetValue(text.Text, out var replacement)) text.Text = replacement;
            else if (text.Text.Contains("Cada instancia puede sobrescribirlos", StringComparison.Ordinal))
                text.Text = text.Text.Replace("Cada instancia", "Cada perfil", StringComparison.Ordinal);
            else if (text.Text.Contains("NEXO", StringComparison.Ordinal))
                text.Text = text.Text.Replace("NEXO", "NEXA", StringComparison.Ordinal);
        }

        foreach (var button in EnumerateVisualChildren<Button>(this))
        {
            if (button.Content is not string label) continue;
            if (string.Equals(label, "ELEGIR RUNTIME", StringComparison.Ordinal))
            {
                button.Content = "ADMINISTRAR JAVA";
                button.ToolTip = "Ver los runtimes Java detectados. NEXA seguirá seleccionándolos automáticamente por versión.";
            }
            else if (label.Contains("NEXO", StringComparison.Ordinal))
            {
                button.Content = label.Replace("NEXO", "NEXA", StringComparison.Ordinal);
            }
        }
    }

    private void AddAboutCard()
    {
        if (SettingsPanel.Content is not Grid settingsGrid) return;

        var row = settingsGrid.RowDefinitions.Count;
        settingsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var card = new Border { Margin = new Thickness(0, 18, 0, 0) };
        card.SetResourceReference(Border.StyleProperty, "Nexo.Card");

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(156) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        card.Child = root;

        var logoTile = new Border
        {
            Width = 140,
            Height = 140,
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(Color.FromRgb(6, 10, 17)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(56, 80, 111)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top
        };
        logoTile.Child = new Image
        {
            Source = Application.Current.Resources["Nexo.BrandFull"] as ImageSource,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(8),
            SnapsToDevicePixels = true
        };
        root.Children.Add(logoTile);

        var content = new StackPanel { Margin = new Thickness(18, 0, 0, 0) };
        Grid.SetColumn(content, 1);
        root.Children.Add(content);

        var wordmark = new Image
        {
            Source = Application.Current.Resources["Nexo.BrandWordmark"] as ImageSource,
            Width = 260,
            Height = 78,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            SnapsToDevicePixels = true
        };
        content.Children.Add(wordmark);

        var eyebrow = new TextBlock
        {
            Text = "ACERCA DE NEXA",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 5, 0, 0)
        };
        eyebrow.SetResourceReference(TextBlock.ForegroundProperty, "Nexo.Accent");
        content.Children.Add(eyebrow);

        content.Children.Add(new TextBlock
        {
            Text = $"NEXA Client {GetProductVersion()}",
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
            Text = "NEXA es un proyecto independiente. Los enlaces externos se ofrecen únicamente como accesos de conveniencia; NEXA no está afiliado ni patrocinado por OpenAI, Mojang/Microsoft, Modrinth o Lunar Client.",
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
