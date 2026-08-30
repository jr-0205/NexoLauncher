using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NexoLauncher.Domain.Instances;

namespace NexoLauncher.App;

public partial class CreateProfileWizard
{
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        Title = "Crear perfil · NEXA";
        Icon = System.Windows.Application.Current.TryFindResource("Nexo.BrandMark") as ImageSource;
        ApplyNexaProductLanguage(this);
        LoaderVersionBox.DisplayMemberPath = "Version";

        VanillaLoader.Checked -= LoaderVisibility_Changed;
        FabricLoader.Checked -= LoaderVisibility_Changed;
        ForgeLoader.Checked -= LoaderVisibility_Changed;
        NeoForgeLoader.Checked -= LoaderVisibility_Changed;
        VanillaLoader.Checked += LoaderVisibility_Changed;
        FabricLoader.Checked += LoaderVisibility_Changed;
        ForgeLoader.Checked += LoaderVisibility_Changed;
        NeoForgeLoader.Checked += LoaderVisibility_Changed;
        RefreshLoaderVisibility();
    }

    private void LoaderVisibility_Changed(object sender, RoutedEventArgs e) => RefreshLoaderVisibility();

    private void RefreshLoaderVisibility()
    {
        if (LoaderVersionSection is null) return;
        LoaderVersionSection.Visibility = SelectedLoader() == LoaderType.Vanilla
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static void ApplyNexaProductLanguage(DependencyObject root)
    {
        if (root is TextBlock text && text.Text.Contains("NEXO", StringComparison.Ordinal))
            text.Text = text.Text.Replace("NEXO", "NEXA", StringComparison.Ordinal);
        else if (root is Button button && button.Content is string label && label.Contains("NEXO", StringComparison.Ordinal))
            button.Content = label.Replace("NEXO", "NEXA", StringComparison.Ordinal);

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
            ApplyNexaProductLanguage(VisualTreeHelper.GetChild(root, index));
    }
}
