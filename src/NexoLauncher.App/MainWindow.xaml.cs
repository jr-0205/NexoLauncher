using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using NexoLauncher.Application.Instances;
using NexoLauncher.Core.Installation;
using NexoLauncher.Domain.Instances;
using NexoLauncher.Infrastructure.Instances;
using NexoLauncher.Java;
using NexoLauncher.Java.Compatibility;
using NexoLauncher.Java.Detection;
using NexoLauncher.Minecraft;

namespace NexoLauncher.App;

public partial class MainWindow : Window
{
    private readonly NexoPaths paths = NexoPaths.ForCurrentUser();
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromMinutes(20) };
    private readonly CancellationTokenSource lifetime = new();
    private readonly MinecraftRuntime minecraft;
    private readonly JsonInstanceRepository instanceRepository;
    private readonly InstanceManager instanceManager;
    private readonly JavaRuntimeInspector javaInspector = new();
    private readonly JavaRuntimeDetector javaDetector;
    private readonly List<JavaRuntime> javaRuntimes = [];
    private readonly Dictionary<string, int?> javaRequirements = new(StringComparer.Ordinal);
    private IReadOnlyList<MinecraftVersion> availableVersions = [];
    private JavaRuntime? selectedJavaRuntime;
    private CancellationTokenSource? operation;
    private bool busy;

    public MainWindow()
    {
        InitializeComponent();
        minecraft = new MinecraftRuntime(httpClient, paths.Root);
        instanceRepository = new JsonInstanceRepository(paths.Instances);
        instanceManager = new InstanceManager(instanceRepository);
        javaDetector = new JavaRuntimeDetector(javaInspector);
        InstallPathText.Text = paths.Root;
        JavaBox.Text = "Detectando Java…";

        Loaded += async (_, _) =>
        {
            try
            {
                await new LegacyInstallationMigrator(paths.Instances, instanceRepository).MigrateAsync(lifetime.Token);
                await ShowLibraryAsync();

                var javaTask = LoadJavaRuntimesAsync(lifetime.Token);
                await LoadVersionsAsync();
                await javaTask;
                await RefreshJavaCompatibilityAsync();
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        };
    }

    private async Task RefreshInstancesAsync()
    {
        paths.EnsureCreated();
        var instances = await instanceManager.ListAsync(lifetime.Token);
        var items = instances
            .Select(instance => new InstanceItem(instance.Id, instance.MinecraftVersion, instance.Name, $"{instance.Loader} · Instalado", instance.UpdatedAt))
            .ToArray();
        InstancesList.ItemsSource = items;
        EmptyLibrary.Visibility = items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        InstancesList.Visibility = items.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (items.Length > 0) InstancesList.SelectedIndex = 0;
        else UpdateInstanceDetails(null);
    }

    private async Task LoadVersionsAsync()
    {
        try
        {
            SetBusy(true, "Consultando versiones oficiales…");
            availableVersions = await minecraft.GetReleaseVersionsAsync(lifetime.Token);
            VersionBox.ItemsSource = availableVersions;
            VersionBox.SelectedIndex = availableVersions.Count > 0 ? 0 : -1;
            StatusText.Text = $"{availableVersions.Count} versiones estables disponibles.";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            StatusText.Text = "No se pudieron cargar las versiones.";
            MessageBox.Show(this, exception.Message, "Error de conexión", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); RefreshButton(); }
    }

    private async Task LoadJavaRuntimesAsync(CancellationToken token)
    {
        try
        {
            var detected = await javaDetector.DetectAsync(token);
            javaRuntimes.Clear();
            javaRuntimes.AddRange(detected);
            selectedJavaRuntime = FindRecommendedRuntime(null);
            UpdateJavaDisplay();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch
        {
            selectedJavaRuntime = null;
            UpdateJavaDisplay();
        }
    }

    private async Task RefreshJavaCompatibilityAsync()
    {
        if (VersionBox.SelectedItem is not MinecraftVersion version)
        {
            UpdateJavaDisplay();
            return;
        }

        int? requiredMajor;
        try
        {
            requiredMajor = await GetRequiredJavaMajorAsync(version, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
        catch
        {
            requiredMajor = null;
        }

        if (VersionBox.SelectedItem is not MinecraftVersion current || current.Id != version.Id) return;

        if (requiredMajor is > 0 && (selectedJavaRuntime is null || !JavaCompatibility.Evaluate(selectedJavaRuntime, requiredMajor.Value).IsCompatible))
        {
            selectedJavaRuntime = FindRecommendedRuntime(requiredMajor);
        }
        else if (selectedJavaRuntime is null)
        {
            selectedJavaRuntime = FindRecommendedRuntime(requiredMajor);
        }

        UpdateJavaDisplay(requiredMajor);

        if (!busy)
        {
            StatusText.Text = requiredMajor is > 0
                ? selectedJavaRuntime is not null && JavaCompatibility.Evaluate(selectedJavaRuntime, requiredMajor.Value).IsCompatible
                    ? $"Minecraft {version.Id} · Java {requiredMajor} compatible listo."
                    : $"Minecraft {version.Id} requiere Java {requiredMajor}. No se encontró un runtime compatible."
                : $"Minecraft {version.Id} · requisito de Java no publicado.";
        }
    }

    private async Task<int?> GetRequiredJavaMajorAsync(MinecraftVersion version, CancellationToken token)
    {
        if (javaRequirements.TryGetValue(version.Id, out var cached)) return cached;
        var required = await minecraft.GetRequiredJavaMajorAsync(version, token);
        javaRequirements[version.Id] = required;
        return required;
    }

    private JavaRuntime? FindRecommendedRuntime(int? requiredMajor)
    {
        IEnumerable<JavaRuntime> candidates = javaRuntimes;
        if (requiredMajor is > 0)
            candidates = candidates.Where(runtime => JavaCompatibility.Evaluate(runtime, requiredMajor.Value).IsCompatible);
        else if (Environment.Is64BitOperatingSystem)
            candidates = candidates.Where(runtime => runtime.Is64Bit && File.Exists(runtime.JavawExecutable));
        else
            candidates = candidates.Where(runtime => File.Exists(runtime.JavawExecutable));

        return candidates
            .OrderByDescending(runtime => runtime.MajorVersion)
            .ThenBy(runtime => runtime.Vendor, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private void UpdateJavaDisplay(int? requiredMajor = null)
    {
        if (JavaBox is null) return;

        if (selectedJavaRuntime is null)
        {
            JavaBox.Text = javaRuntimes.Count == 0 ? "No se detectó Java" : "Sin runtime compatible";
            JavaBox.ToolTip = "Puedes seleccionar un runtime manualmente.";
            JavaBox.BorderBrush = new SolidColorBrush(Color.FromRgb(164, 73, 73));
            return;
        }

        JavaBox.Text = $"Java {selectedJavaRuntime.MajorVersion} · {selectedJavaRuntime.Vendor} · {selectedJavaRuntime.Architecture}";
        JavaBox.ToolTip = selectedJavaRuntime.JavaExecutable;

        var compatible = requiredMajor is not > 0 || JavaCompatibility.Evaluate(selectedJavaRuntime, requiredMajor.Value).IsCompatible;
        JavaBox.BorderBrush = new SolidColorBrush(compatible
            ? Color.FromRgb(58, 128, 91)
            : Color.FromRgb(164, 73, 73));
    }

    private async void ShowLibrary_Click(object sender, RoutedEventArgs e) => await ShowLibraryAsync();
    private void ShowInstall_Click(object sender, RoutedEventArgs e) => ShowInstall();

    private async Task ShowLibraryAsync()
    {
        await RefreshInstancesAsync();
        LibraryPanel.Visibility = Visibility.Visible;
        InstallPanel.Visibility = Visibility.Collapsed;
        LibraryNavButton.Background = new SolidColorBrush(Color.FromRgb(25, 36, 56));
        LibraryNavButton.Foreground = Brushes.White;
        InstallNavButton.Background = Brushes.Transparent;
        InstallNavButton.Foreground = new SolidColorBrush(Color.FromRgb(150, 162, 183));
    }

    private void ShowInstall()
    {
        LibraryPanel.Visibility = Visibility.Collapsed;
        InstallPanel.Visibility = Visibility.Visible;
        InstallNavButton.Background = new SolidColorBrush(Color.FromRgb(25, 36, 56));
        InstallNavButton.Foreground = Brushes.White;
        LibraryNavButton.Background = Brushes.Transparent;
        LibraryNavButton.Foreground = new SolidColorBrush(Color.FromRgb(150, 162, 183));
    }

    private async void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (busy || VersionBox.SelectedItem is not MinecraftVersion version) return;
        if (minecraft.IsInstalled(version.Id)) { await LaunchAsync(version.Id); return; }

        operation = new CancellationTokenSource();
        try
        {
            SetBusy(true, "Preparando descarga…");
            Progress.Visibility = Visibility.Visible;
            var reporter = new Progress<InstallProgress>(value =>
            {
                StatusText.Text = value.Total == 0 ? value.Stage : $"{value.Stage} · {value.Completed}/{value.Total}";
                Progress.Value = value.Percentage;
            });

            await minecraft.InstallAsync(version, reporter, operation.Token);

            var existing = (await instanceManager.ListAsync(operation.Token))
                .FirstOrDefault(instance => instance.MinecraftVersion == version.Id && instance.Loader == LoaderType.Vanilla);
            var profile = existing ?? await instanceManager.CreateAsync($"Minecraft {version.Id}", version.Id, cancellationToken: operation.Token);
            await instanceManager.UpdateSettingsAsync(profile.Id, profile.Settings with
            {
                JavaPath = selectedJavaRuntime?.JavaExecutable,
                MemoryMiB = (int)RamSlider.Value
            }, operation.Token);

            await ShowLibraryAsync();
        }
        catch (OperationCanceledException) { StatusText.Text = "Operación cancelada."; }
        catch (Exception exception) { ShowError(exception); }
        finally { SetBusy(false); RefreshButton(); operation?.Dispose(); operation = null; }
    }

    private async void LibraryPlay_Click(object sender, RoutedEventArgs e)
    {
        if (InstancesList.SelectedItem is not InstanceItem item) return;
        var instance = await instanceManager.GetAsync(item.Id, lifetime.Token);
        if (instance is null)
        {
            MessageBox.Show(this, "La instancia seleccionada ya no existe.", "Nexo Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            await RefreshInstancesAsync();
            return;
        }

        await LaunchAsync(item.VersionId, instance);
    }

    private async Task LaunchAsync(string versionId, GameInstance? instance = null)
    {
        if (busy) return;

        var runtime = await ResolveRuntimeForInstanceAsync(instance);
        if (runtime is null)
        {
            ShowInstall();
            MessageBox.Show(this, "NEXO no encontró un runtime Java válido. Usa “ELEGIR JAVA” para seleccionar uno.", "Java requerido", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var version = availableVersions.FirstOrDefault(item => item.Id == versionId);
        int? requiredMajor = null;
        if (version is not null)
        {
            try { requiredMajor = await GetRequiredJavaMajorAsync(version, lifetime.Token); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
        }

        if (requiredMajor is > 0)
        {
            var compatibility = JavaCompatibility.Evaluate(runtime, requiredMajor.Value);
            if (!compatibility.IsCompatible)
            {
                ShowInstall();
                selectedJavaRuntime = FindRecommendedRuntime(requiredMajor);
                UpdateJavaDisplay(requiredMajor);
                MessageBox.Show(this, compatibility.Message, "Java incompatible", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else if (!File.Exists(runtime.JavawExecutable))
        {
            ShowInstall();
            MessageBox.Show(this, "El runtime seleccionado no contiene javaw.exe.", "Java inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            busy = true;
            LibraryPlayButton.IsEnabled = false;
            PrimaryButton.IsEnabled = false;

            var memoryMiB = instance?.Settings.MemoryMiB is > 0 ? instance.Settings.MemoryMiB.Value : (int)RamSlider.Value;
            if (instance is not null)
            {
                await instanceManager.UpdateSettingsAsync(instance.Id, instance.Settings with
                {
                    JavaPath = runtime.JavaExecutable,
                    MemoryMiB = memoryMiB
                }, lifetime.Token);
            }

            var username = string.IsNullOrWhiteSpace(UsernameBox.Text) ? "Player" : UsernameBox.Text.Trim();
            var process = minecraft.Launch(new LaunchOptions(versionId, runtime.JavawExecutable, username, memoryMiB));
            await Task.Delay(700, lifetime.Token);
            if (!process.HasExited)
            {
                System.Windows.Application.Current.Shutdown();
                return;
            }

            throw new InvalidOperationException("Java terminó antes de iniciar Minecraft.");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception) { ShowError(exception); }
        finally
        {
            busy = false;
            RefreshButton();
            LibraryPlayButton.IsEnabled = InstancesList.SelectedItem is not null;
        }
    }

    private async Task<JavaRuntime?> ResolveRuntimeForInstanceAsync(GameInstance? instance)
    {
        var configuredPath = instance?.Settings.JavaPath;
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var normalized = NormalizeJavaExecutable(configuredPath);
            var detected = javaRuntimes.FirstOrDefault(runtime =>
                string.Equals(runtime.JavaExecutable, normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(runtime.JavawExecutable, configuredPath, StringComparison.OrdinalIgnoreCase));
            if (detected is not null) return detected;

            try
            {
                var inspected = await javaInspector.InspectAsync(normalized, "Instancia", lifetime.Token);
                if (inspected is not null)
                {
                    AddOrReplaceRuntime(inspected);
                    return inspected;
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return null; }
            catch { }
        }

        return selectedJavaRuntime;
    }

    private void InstancesList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateInstanceDetails(InstancesList.SelectedItem as InstanceItem);

    private void UpdateInstanceDetails(InstanceItem? item)
    {
        DetailName.Text = item?.Name ?? "Selecciona una instancia";
        DetailSubtitle.Text = item is null ? "Los detalles aparecerán aquí." : "Lista para iniciar";
        DetailVersion.Text = item?.VersionId ?? "—";
        LibraryPlayButton.IsEnabled = item is not null && !busy;
    }

    private async void BrowseJava_Click(object sender, RoutedEventArgs e)
    {
        int? requiredMajor = null;
        if (VersionBox.SelectedItem is MinecraftVersion version)
        {
            try { requiredMajor = await GetRequiredJavaMajorAsync(version, lifetime.Token); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
            catch { }
        }

        if (javaRuntimes.Count > 0)
        {
            var selector = new JavaRuntimeDialog(javaRuntimes, requiredMajor, selectedJavaRuntime) { Owner = this };
            var result = selector.ShowDialog();
            if (result == true && selector.SelectedRuntime is not null)
            {
                selectedJavaRuntime = selector.SelectedRuntime;
                UpdateJavaDisplay(requiredMajor);
                return;
            }

            if (!selector.ManualBrowseRequested) return;
        }

        await BrowseManualJavaAsync(requiredMajor);
    }

    private async Task BrowseManualJavaAsync(int? requiredMajor)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecciona java.exe o javaw.exe",
            Filter = "Java para Windows|java.exe;javaw.exe|Ejecutables|*.exe"
        };
        if (dialog.ShowDialog(this) != true) return;

        var executable = NormalizeJavaExecutable(dialog.FileName);
        JavaRuntime? runtime;
        try { runtime = await javaInspector.InspectAsync(executable, "Manual", lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Java inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (runtime is null)
        {
            MessageBox.Show(this, "No se pudo identificar ese ejecutable como un runtime Java válido.", "Java inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AddOrReplaceRuntime(runtime);
        selectedJavaRuntime = runtime;
        UpdateJavaDisplay(requiredMajor);

        if (requiredMajor is > 0)
        {
            var result = JavaCompatibility.Evaluate(runtime, requiredMajor.Value);
            if (!result.IsCompatible)
                MessageBox.Show(this, result.Message, "Java incompatible", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void AddOrReplaceRuntime(JavaRuntime runtime)
    {
        javaRuntimes.RemoveAll(item => string.Equals(item.JavaExecutable, runtime.JavaExecutable, StringComparison.OrdinalIgnoreCase));
        javaRuntimes.Add(runtime);
        javaRuntimes.Sort((left, right) => right.MajorVersion.CompareTo(left.MajorVersion));
    }

    private static string NormalizeJavaExecutable(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetFileName(fullPath), "javaw.exe", StringComparison.OrdinalIgnoreCase)) return fullPath;
        var java = Path.Combine(Path.GetDirectoryName(fullPath)!, "java.exe");
        return File.Exists(java) ? java : fullPath;
    }

    private async void VersionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshButton();
        await RefreshJavaCompatibilityAsync();
    }

    private void RamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RamText is not null) RamText.Text = $"{e.NewValue / 1024:0.#} GB";
    }

    private void RefreshButton()
    {
        if (PrimaryButton is null || busy) return;
        if (VersionBox.SelectedItem is MinecraftVersion version)
        {
            PrimaryButton.Content = minecraft.IsInstalled(version.Id) ? "INICIAR" : "DESCARGAR";
            PrimaryButton.IsEnabled = true;
        }
        else
        {
            PrimaryButton.Content = "SIN VERSIONES";
            PrimaryButton.IsEnabled = false;
        }
    }

    private void SetBusy(bool value, string? status = null)
    {
        busy = value;
        VersionBox.IsEnabled = !value;
        UsernameBox.IsEnabled = !value;
        RamSlider.IsEnabled = !value;
        PrimaryButton.IsEnabled = !value;
        if (value) PrimaryButton.Content = "TRABAJANDO…";
        if (status is not null) StatusText.Text = status;
        if (!value) Progress.Visibility = Visibility.Collapsed;
    }

    private void ShowError(Exception exception)
    {
        StatusText.Text = "No se pudo completar la operación.";
        MessageBox.Show(this, exception.Message, "Nexo Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        operation?.Cancel();
        lifetime.Cancel();
        operation?.Dispose();
        lifetime.Dispose();
        httpClient.Dispose();
        base.OnClosing(e);
    }

    private sealed record InstanceItem(InstanceId Id, string VersionId, string Name, string Subtitle, DateTimeOffset Modified);
}
