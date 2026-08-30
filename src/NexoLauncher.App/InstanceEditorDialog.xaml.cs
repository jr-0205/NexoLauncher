using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NexoLauncher.Core.Installation;
using NexoLauncher.Domain.Instances;
using NexoLauncher.Infrastructure.Instances;

namespace NexoLauncher.App;

public partial class InstanceEditorDialog : Window
{
    private readonly GameInstance instance;
    private readonly JsonInstanceRepository repository;
    private readonly string instanceRoot;
    private string? selectedIconSource;
    private string? selectedBackgroundSource;

    public string UpdatedName { get; private set; }
    public string UpdatedDescription { get; private set; }
    public InstanceSettings UpdatedSettings { get; private set; }
    public string? SelectedIconSource => selectedIconSource;
    public string? SelectedBackgroundSource => selectedBackgroundSource;
    public bool ClearIconRequested { get; private set; }
    public bool ClearBackgroundRequested { get; private set; }

    public InstanceEditorDialog(GameInstance instance)
    {
        NativeWindowTheme.ApplyDarkTitleBar(this);
        InitializeComponent();
        this.instance = instance;
        var paths = NexoPaths.ForCurrentUser();
        repository = new JsonInstanceRepository(paths.Instances);
        instanceRoot = repository.GetInstanceDirectory(instance.Id);

        UpdatedName = instance.Name;
        UpdatedDescription = instance.Description;
        UpdatedSettings = instance.Settings;
        IdentityText.Text = $"Minecraft {instance.MinecraftVersion} · {instance.Loader}" +
                            (string.IsNullOrWhiteSpace(instance.LoaderVersion) ? string.Empty : " " + instance.LoaderVersion);
        NameBox.Text = instance.Name;
        DescriptionBox.Text = instance.Description;
        MemoryBox.Text = instance.Settings.MemoryMiB?.ToString() ?? string.Empty;
        JavaBox.Text = instance.Settings.JavaPath ?? string.Empty;
        WidthBox.Text = instance.Settings.WindowWidth?.ToString() ?? string.Empty;
        HeightBox.Text = instance.Settings.WindowHeight?.ToString() ?? string.Empty;
        FullscreenBox.IsChecked = instance.Settings.Fullscreen;
        JvmArgumentsBox.Text = string.Join(Environment.NewLine, instance.Settings.JvmArguments ?? []);

        var icon = ProfileArtworkStore.Resolve(instanceRoot, instance.IconPath);
        IconPreview.Source = LoadPreview(icon) ?? System.Windows.Application.Current.TryFindResource("Nexo.BrandMark") as ImageSource;
        var background = ProfileArtworkStore.Resolve(instanceRoot, instance.BackgroundPath);
        BackgroundPreview.Source = LoadPreview(background);
    }

    private void BrowseJava_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecciona java.exe o javaw.exe",
            Filter = "Java para Windows|java.exe;javaw.exe|Ejecutables|*.exe"
        };
        if (dialog.ShowDialog(this) == true) JavaBox.Text = dialog.FileName;
    }

    private void ChooseIcon_Click(object sender, RoutedEventArgs e)
    {
        var file = ChooseImage("Selecciona el nuevo icono del perfil");
        if (file is null) return;
        selectedIconSource = file;
        ClearIconRequested = false;
        IconPreview.Source = LoadPreview(file);
    }

    private void ChooseBackground_Click(object sender, RoutedEventArgs e)
    {
        var file = ChooseImage("Selecciona el nuevo fondo del perfil");
        if (file is null) return;
        selectedBackgroundSource = file;
        ClearBackgroundRequested = false;
        BackgroundPreview.Source = LoadPreview(file);
    }

    private void ResetIcon_Click(object sender, RoutedEventArgs e)
    {
        selectedIconSource = null;
        ClearIconRequested = true;
        IconPreview.Source = System.Windows.Application.Current.TryFindResource("Nexo.BrandMark") as ImageSource;
    }

    private void ClearBackground_Click(object sender, RoutedEventArgs e)
    {
        selectedBackgroundSource = null;
        ClearBackgroundRequested = true;
        BackgroundPreview.Source = null;
    }

    private string? ChooseImage(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp|PNG|*.png|JPEG|*.jpg;*.jpeg|Bitmap|*.bmp"
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;
        var name = NameBox.Text.Trim();
        if (name.Length is < 1 or > 64) { Fail("El nombre debe tener entre 1 y 64 caracteres."); return; }
        if (!OptionalInt(MemoryBox.Text, 512, 65536, "RAM", out var memory)) return;
        if (!OptionalInt(WidthBox.Text, 320, 16384, "ancho", out var width)) return;
        if (!OptionalInt(HeightBox.Text, 240, 16384, "alto", out var height)) return;
        if ((width is null) != (height is null)) { Fail("Ancho y alto deben configurarse juntos."); return; }

        var javaPath = string.IsNullOrWhiteSpace(JavaBox.Text) ? null : Path.GetFullPath(JavaBox.Text.Trim());
        var arguments = JvmArgumentsBox.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        UpdatedName = name;
        UpdatedDescription = DescriptionBox.Text.Trim();
        UpdatedSettings = new InstanceSettings(memory, javaPath, arguments.Length == 0 ? null : arguments, width, height, FullscreenBox.IsChecked);

        try
        {
            var appearanceChanged = selectedIconSource is not null || selectedBackgroundSource is not null || ClearIconRequested || ClearBackgroundRequested;
            var iconPath = instance.IconPath;
            var backgroundPath = instance.BackgroundPath;
            if (appearanceChanged)
            {
                var artwork = await ProfileArtworkStore.UpdateAsync(
                    instanceRoot,
                    selectedIconSource,
                    selectedBackgroundSource,
                    ClearIconRequested,
                    ClearBackgroundRequested,
                    CancellationToken.None);
                if (selectedIconSource is not null || ClearIconRequested) iconPath = artwork.IconRelativePath;
                if (selectedBackgroundSource is not null || ClearBackgroundRequested) backgroundPath = artwork.BackgroundRelativePath;
                artwork = artwork with { IconRelativePath = iconPath, BackgroundRelativePath = backgroundPath };
                await ProfileArtworkStore.SaveMetadataAsync(instanceRoot, artwork, CancellationToken.None);
            }

            var updated = instance with
            {
                Name = UpdatedName,
                Description = UpdatedDescription,
                IconPath = iconPath,
                BackgroundPath = backgroundPath,
                Settings = UpdatedSettings,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await repository.SaveAsync(updated, CancellationToken.None);
            DialogResult = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Fail("No se pudo guardar la apariencia del perfil. " + exception.Message);
        }
    }

    private bool OptionalInt(string text, int minimum, int maximum, string field, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!int.TryParse(text, out var parsed) || parsed < minimum || parsed > maximum)
        {
            Fail($"El campo {field} debe estar entre {minimum} y {maximum}.");
            return false;
        }
        value = parsed;
        return true;
    }

    private static ImageSource? LoadPreview(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private void Fail(string message) => ValidationText.Text = message;
}
