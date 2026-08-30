using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NexoLauncher.Domain.Instances;
using NexoLauncher.Minecraft;

namespace NexoLauncher.App;

public sealed record CreateProfileWizardResult(
    string Name,
    string Description,
    MinecraftVersion Version,
    LoaderType Loader,
    string? LoaderVersion,
    string? IconSourcePath,
    string? BackgroundSourcePath,
    int? MemoryMiB);

public partial class CreateProfileWizard : Window
{
    private readonly IReadOnlyList<MinecraftVersion> versions;
    private readonly Func<MinecraftVersion, LoaderType, CancellationToken, Task<IReadOnlyList<LoaderVersion>>> loaderResolver;
    private readonly CancellationToken lifetimeToken;
    private readonly int recommendedMemoryMiB;
    private int step = 1;
    private bool initialized;
    private bool loadingLoaderVersions;
    private string? iconSourcePath;
    private string? backgroundSourcePath;

    public CreateProfileWizardResult? Result { get; private set; }

    public CreateProfileWizard(
        IReadOnlyList<MinecraftVersion> versions,
        Func<MinecraftVersion, LoaderType, CancellationToken, Task<IReadOnlyList<LoaderVersion>>> loaderResolver,
        int recommendedMemoryMiB,
        int safeMaximumMemoryMiB,
        CancellationToken lifetimeToken)
    {
        InitializeComponent();
        NativeWindowTheme.ApplyDarkTitleBar(this);
        this.versions = versions;
        this.loaderResolver = loaderResolver;
        this.lifetimeToken = lifetimeToken;
        this.recommendedMemoryMiB = Math.Clamp(recommendedMemoryMiB, 1024, Math.Max(1024, safeMaximumMemoryMiB));

        MemorySlider.Maximum = Math.Max(1024, safeMaximumMemoryMiB);
        MemorySlider.Value = this.recommendedMemoryMiB;
        MemoryHintText.Text = $"Recomendado para este equipo: {FormatMemory(this.recommendedMemoryMiB)}. Déjalo desactivado para heredar la configuración global.";
        ApplyVersionFilter();
        if (VersionList.Items.Count > 0) VersionList.SelectedIndex = 0;
        if (VersionList.SelectedItem is MinecraftVersion first)
            ProfileNameBox.Text = $"Minecraft {first.Id}";

        RestoreDefaultBrandPreview();
        initialized = true;
        RefreshStepVisuals();
    }

    private void ProfileNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (NameCounterText is null) return;
        NameCounterText.Text = $"{Math.Max(0, 64 - ProfileNameBox.Text.Length)} caracteres restantes";
        RefreshFooter();
    }

    private void DescriptionBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DescriptionCounterText is null) return;
        DescriptionCounterText.Text = $"{Math.Max(0, 300 - DescriptionBox.Text.Length)} caracteres restantes";
    }

    private void VersionSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!initialized) return;
        var previous = VersionList.SelectedItem as MinecraftVersion;
        ApplyVersionFilter();
        if (previous is not null)
            VersionList.SelectedItem = VersionList.Items.Cast<MinecraftVersion>().FirstOrDefault(value => value.Id == previous.Id);
        if (VersionList.SelectedIndex < 0 && VersionList.Items.Count > 0) VersionList.SelectedIndex = 0;
    }

    private void ApplyVersionFilter()
    {
        var query = VersionSearchBox?.Text?.Trim() ?? string.Empty;
        VersionList.ItemsSource = versions
            .Where(value => query.Length == 0 || value.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(value => value.ReleaseTime)
            .ToArray();
    }

    private async void Loader_Checked(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        await RefreshLoaderVersionsAsync();
    }

    private async void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VersionList.SelectedItem is not MinecraftVersion version)
        {
            SelectedVersionText.Text = "Selecciona una versión";
            return;
        }

        SelectedVersionText.Text = $"Minecraft {version.Id}";
        if (initialized) await RefreshLoaderVersionsAsync();
        RefreshFooter();
    }

    private void LoaderVersionBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshFooter();

    private async Task RefreshLoaderVersionsAsync()
    {
        if (loadingLoaderVersions || VersionList.SelectedItem is not MinecraftVersion version) return;
        var loader = SelectedLoader();
        SelectedLoaderText.Text = loader.ToString();

        if (loader == LoaderType.Vanilla)
        {
            LoaderVersionBox.ItemsSource = new[] { new LoaderVersion("Vanilla", true) };
            LoaderVersionBox.SelectedIndex = 0;
            LoaderVersionBox.IsEnabled = false;
            LoaderStatusText.Text = "Vanilla no requiere loader adicional.";
            RefreshFooter();
            return;
        }

        loadingLoaderVersions = true;
        LoaderVersionBox.IsEnabled = false;
        LoaderVersionBox.ItemsSource = null;
        LoaderStatusText.Text = $"Consultando versiones compatibles de {loader}…";
        try
        {
            var values = await loaderResolver(version, loader, lifetimeToken);
            LoaderVersionBox.ItemsSource = values;
            LoaderVersionBox.SelectedItem = values.FirstOrDefault(value => value.Stable) ?? values.FirstOrDefault();
            LoaderVersionBox.IsEnabled = values.Count > 0;
            LoaderStatusText.Text = values.Count == 0
                ? $"No se encontraron builds de {loader} para Minecraft {version.Id}."
                : $"NEXO seleccionó automáticamente {(LoaderVersionBox.SelectedItem as LoaderVersion)?.Version}. Puedes cambiarlo si lo necesitas.";
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
            Close();
        }
        catch (Exception exception)
        {
            LoaderStatusText.Text = "No se pudieron consultar las versiones del loader.";
            MessageBox.Show(this, exception.Message, "Versiones del loader", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            loadingLoaderVersions = false;
            RefreshFooter();
        }
    }

    private LoaderType SelectedLoader()
    {
        if (FabricLoader.IsChecked == true) return LoaderType.Fabric;
        if (ForgeLoader.IsChecked == true) return LoaderType.Forge;
        if (NeoForgeLoader.IsChecked == true) return LoaderType.NeoForge;
        return LoaderType.Vanilla;
    }

    private void GoInfo_Click(object sender, RoutedEventArgs e) => SetStep(1);

    private void GoVersion_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateInfo()) return;
        SetStep(2);
    }

    private void GoAppearance_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateInfo()) return;
        if (!ValidateVersion())
        {
            SetStep(2);
            return;
        }
        SetStep(3);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (step > 1) SetStep(step - 1);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        switch (step)
        {
            case 1:
                if (ValidateInfo()) SetStep(2);
                break;
            case 2:
                if (ValidateVersion()) SetStep(3);
                break;
            case 3:
                CompleteWizard();
                break;
        }
    }

    private bool ValidateInfo()
    {
        var name = ProfileNameBox.Text.Trim();
        if (name.Length is >= 1 and <= 64) return true;
        MessageBox.Show(this, "Escribe un nombre para el perfil.", "Nombre requerido", MessageBoxButton.OK, MessageBoxImage.Information);
        ProfileNameBox.Focus();
        return false;
    }

    private bool ValidateVersion()
    {
        if (VersionList.SelectedItem is not MinecraftVersion)
        {
            MessageBox.Show(this, "Selecciona una versión de Minecraft.", "Versión requerida", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (SelectedLoader() != LoaderType.Vanilla && LoaderVersionBox.SelectedItem is not LoaderVersion)
        {
            MessageBox.Show(this, "No hay una versión del loader seleccionada para este perfil.", "Loader requerido", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        return true;
    }

    private void SetStep(int value)
    {
        step = Math.Clamp(value, 1, 3);
        RefreshStepVisuals();
    }

    private void RefreshStepVisuals()
    {
        InfoPanel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        VersionPanel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        AppearancePanel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.IsEnabled = step > 1;
        NextButton.Content = step == 3 ? "CREAR PERFIL" : "SIGUIENTE";

        ApplyStepDot(InfoStepDot, step >= 1, step == 1);
        ApplyStepDot(VersionStepDot, step >= 2, step == 2);
        ApplyStepDot(AppearanceStepDot, step >= 3, step == 3);
        RefreshFooter();
    }

    private static void ApplyStepDot(Border dot, bool completed, bool active)
    {
        dot.Background = active
            ? (Brush)Application.Current.Resources["Nexo.Accent"]
            : completed
                ? (Brush)Application.Current.Resources["Nexo.Success"]
                : (Brush)Application.Current.Resources["Nexo.SurfaceHover"];
    }

    private void RefreshFooter()
    {
        if (FooterHintText is null || NextButton is null) return;
        FooterHintText.Text = step switch
        {
            1 => string.IsNullOrWhiteSpace(ProfileNameBox.Text) ? "Paso 1 de 3 · Escribe un nombre para continuar." : "Paso 1 de 3 · La descripción es opcional.",
            2 when VersionList.SelectedItem is not MinecraftVersion => "Paso 2 de 3 · Selecciona Minecraft.",
            2 when SelectedLoader() != LoaderType.Vanilla && LoaderVersionBox.SelectedItem is not LoaderVersion => "Paso 2 de 3 · Esperando una build compatible del loader.",
            2 => "Paso 2 de 3 · Java se resolverá automáticamente.",
            _ => "Paso 3 de 3 · Revisa el aspecto del perfil y créalo cuando estés listo."
        };
    }

    private void ChooseIcon_Click(object sender, RoutedEventArgs e)
    {
        var selected = ChooseImage("Selecciona un icono para el perfil");
        if (selected is null) return;
        iconSourcePath = selected;
        IconPreview.Source = LoadPreview(selected);
    }

    private void ChooseBackground_Click(object sender, RoutedEventArgs e)
    {
        var selected = ChooseImage("Selecciona un fondo para el perfil");
        if (selected is null) return;
        backgroundSourcePath = selected;
        BackgroundPreview.Source = LoadPreview(selected);
    }

    private string? ChooseImage(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Imágenes compatibles|*.png;*.jpg;*.jpeg;*.bmp|PNG|*.png|JPEG|*.jpg;*.jpeg|Todos los archivos|*.*",
            Multiselect = false
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private static BitmapImage LoadPreview(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void CustomMemoryCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (MemorySlider is null) return;
        MemorySlider.IsEnabled = CustomMemoryCheck.IsChecked == true;
        MemoryHintText.Text = CustomMemoryCheck.IsChecked == true
            ? $"Este perfil usará {FormatMemory((int)MemorySlider.Value)} aunque cambie la memoria global."
            : $"Heredará la memoria global. Recomendado actual: {FormatMemory(recommendedMemoryMiB)}.";
    }

    private void MemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MemoryText is null) return;
        MemoryText.Text = FormatMemory((int)e.NewValue);
        if (CustomMemoryCheck?.IsChecked == true)
            MemoryHintText.Text = $"Este perfil usará {FormatMemory((int)e.NewValue)} aunque cambie la memoria global.";
    }

    private static string FormatMemory(int value) => value >= 1024 ? $"{value / 1024d:0.#} GB" : $"{value} MB";

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        DescriptionBox.Clear();
        VersionSearchBox.Clear();
        VanillaLoader.IsChecked = true;
        iconSourcePath = null;
        backgroundSourcePath = null;
        RestoreDefaultBrandPreview();
        BackgroundPreview.Source = null;
        CustomMemoryCheck.IsChecked = false;
        MemorySlider.Value = recommendedMemoryMiB;
        ApplyVersionFilter();
        if (VersionList.Items.Count > 0) VersionList.SelectedIndex = 0;
        if (VersionList.SelectedItem is MinecraftVersion version) ProfileNameBox.Text = $"Minecraft {version.Id}";
        SetStep(1);
    }

    private void RestoreDefaultBrandPreview()
    {
        IconPreview.Source = Application.Current.TryFindResource("Nexo.BrandMark") as ImageSource;
    }

    private void CompleteWizard()
    {
        if (!ValidateInfo() || !ValidateVersion() || VersionList.SelectedItem is not MinecraftVersion version) return;
        var loader = SelectedLoader();
        var loaderVersion = loader == LoaderType.Vanilla ? null : (LoaderVersionBox.SelectedItem as LoaderVersion)?.Version;
        Result = new CreateProfileWizardResult(
            ProfileNameBox.Text.Trim(),
            DescriptionBox.Text.Trim(),
            version,
            loader,
            loaderVersion,
            iconSourcePath,
            backgroundSourcePath,
            CustomMemoryCheck.IsChecked == true ? (int)MemorySlider.Value : null);
        DialogResult = true;
    }
}
