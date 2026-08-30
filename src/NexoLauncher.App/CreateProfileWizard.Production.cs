using System.Windows;
using System.Windows.Media;
using NexoLauncher.Domain.Instances;

namespace NexoLauncher.App;

public partial class CreateProfileWizard
{
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        Icon = System.Windows.Application.Current.TryFindResource("Nexo.BrandMark") as ImageSource;

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
}
