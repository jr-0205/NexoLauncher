using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using NexoLauncher.Application.Configuration;
using NexoLauncher.Application.Instances;
using NexoLauncher.Core.Installation;
using NexoLauncher.Domain.Configuration;
using NexoLauncher.Domain.Instances;
using NexoLauncher.Infrastructure.Configuration;
using NexoLauncher.Infrastructure.Instances;
using NexoLauncher.Infrastructure.Java;
using NexoLauncher.Infrastructure.System;
using NexoLauncher.Java;
using NexoLauncher.Java.Compatibility;
using NexoLauncher.Java.Detection;
using NexoLauncher.Minecraft;

namespace NexoLauncher.App;

public partial class MainWindow : Window
{
    private static readonly TimeSpan JavaCacheLifetime = TimeSpan.FromHours(24);

    private readonly NexoPaths paths = NexoPaths.ForCurrentUser();
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromMinutes(20) };
    private readonly CancellationTokenSource lifetime = new();
    private readonly MinecraftRuntime minecraft;
    private readonly JsonInstanceRepository instanceRepository;
    private readonly InstanceManager instanceManager;
    private readonly JsonLauncherSettingsStore launcherSettingsStore;
    private readonly JsonJavaRuntimeCache javaRuntimeCache;
    private readonly JavaRuntimeInspector javaInspector = new();
    private readonly JavaRuntimeDetector javaDetector;
    private readonly List<JavaRuntime> javaRuntimes = [];
    private readonly Dictionary<string, int?> javaRequirements = new(StringComparer.Ordinal);

    private IReadOnlyList<MinecraftVersion> availableVersions = [];
    private LauncherSettings launcherSettings = new();
    private SystemMemorySnapshot memorySnapshot = new(0, 0);
    private JavaRuntime? selectedJavaRuntime;
    private CancellationTokenSource? operation;
    private bool busy;
    private bool syncingSettingsUi;
    private bool javaRefreshRunning;

    public MainWindow()
    {
        InitializeComponent();
        paths.EnsureCreated();

        minecraft = new MinecraftRuntime(httpClient, paths.Root);
        instanceRepository = new JsonInstanceRepository(paths.Instances);
        instanceManager = new InstanceManager(instanceRepository);
        launcherSettingsStore = new JsonLauncherSettingsStore(Path.Combine(paths.Root, "settings.json"));
        javaRuntimeCache = new JsonJavaRuntimeCache(Path.Combine(paths.Cache, "java-runtimes.json"));
        javaDetector = new JavaRuntimeDetector(javaInspector);

        InstallPathText.Text = paths.Root;
        JavaBox.Text = "Detectando Java…";
        SidebarStatusText.Text = "INICIALIZANDO NEXO";

        RamSlider.PreviewMouseLeftButtonUp += async (_, _) => await SaveInstallDefaultsAsync();
        UsernameBox.LostKeyboardFocus += async (_, _) => await SaveInstallDefaultsAsync();

        Loaded += async (_, _) =>
        {
            try
            {
                await LoadLauncherSettingsAsync();
                await new LegacyInstallationMigrator(paths.Instances, instanceRepository).MigrateAsync(lifetime.Token);
                await ShowLibraryAsync();

                var javaTask = LoadJavaRuntimesAsync(lifetime.Token);
                await LoadVersionsAsync();
                await javaTask;
                await RefreshJavaCompatibilityAsync();

                SidebarStatusText.Text = "NEXO CORE LISTO";
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception exception)
            {
                SidebarStatusText.Text = "NEXO REQUIERE ATENCIÓN";
                ShowError(exception);
            }
        };
    }

    private async Task LoadLauncherSettingsAsync()
    {
        var settingsPath = Path.Combine(paths.Root, "settings.json");
        var existed = File.Exists(settingsPath);
        memorySnapshot = SystemMemory.GetSnapshot();

        var safeMaximum = MemoryRecommendation.SafeMaximumMiB(memorySnapshot.TotalMiB);
        RamSlider.Maximum = safeMaximum;
        SettingsRamSlider.Maximum = safeMaximum;

        launcherSettings = await launcherSettingsStore.LoadAsync(lifetime.Token);
        if (!existed)
        {
            launcherSettings = launcherSettings with
            {
                MemoryMiB = MemoryRecommendation.RecommendMiB(memorySnapshot.TotalMiB)
            };
        }

        launcherSettings = (launcherSettings with
        {
            MemoryMiB = Math.Clamp(launcherSettings.MemoryMiB, MemoryRecommendation.MinimumMiB, safeMaximum)
        }).Normalize();

        SyncSettingsControls();
        UpdateMemorySummary();
    }

    private void SyncSettingsControls()
    {
        syncingSettingsUi = true;
        try
        {
            var memory = Math.Clamp(launcherSettings.MemoryMiB, (int)RamSlider.Minimum, (int)RamSlider.Maximum);
            RamSlider.Value = memory;
            SettingsRamSlider.Value = memory;
            UsernameBox.Text = launcherSettings.Username;
            SettingsUsernameBox.Text = launcherSettings.Username;
            SettingsCloseLauncherCheck.IsChecked = launcherSettings.CloseLauncherOnGameStart;
            UpdateRamLabels(memory, memory);
            UpdateJavaDisplay();
        }
        finally
        {
            syncingSettingsUi = false;
        }
    }

    private async Task PersistLauncherSettingsAsync(LauncherSettings settings, string? status = null)
    {
        if (lifetime.IsCancellationRequested) return;

        var safeMaximum = MemoryRecommendation.SafeMaximumMiB(memorySnapshot.TotalMiB);
        launcherSettings = (settings with
        {
            MemoryMiB = Math.Clamp(settings.MemoryMiB, MemoryRecommendation.MinimumMiB, safeMaximum)
        }).Normalize();

        try
        {
            await launcherSettingsStore.SaveAsync(launcherSettings, lifetime.Token);
            SyncSettingsControls();
            if (!string.IsNullOrWhiteSpace(status)) SettingsStatusText.Text = status;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            SettingsStatusText.Text = "No se pudieron guardar los cambios.";
            MessageBox.Show(this, exception.Message, "Configuración de NEXO", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task SaveInstallDefaultsAsync()
    {
        if (syncingSettingsUi || lifetime.IsCancellationRequested) return;

        var username = string.IsNullOrWhiteSpace(UsernameBox.Text) ? "Player" : UsernameBox.Text.Trim();
        await PersistLauncherSettingsAsync(launcherSettings with
        {
            MemoryMiB = (int)RamSlider.Value,
            JavaPath = selectedJavaRuntime?.JavaExecutable ?? launcherSettings.JavaPath,
            Username = username
        });
    }

    private async Task RefreshInstancesAsync()
    {
        var instances = await instanceManager.ListAsync(lifetime.Token);
        var items = instances
            .Select(instance => new InstanceItem(
                instance.Id,
                instance.MinecraftVersion,
                instance.Name,
                $"{instance.Loader} · Instalado",
                instance.UpdatedAt))
            .ToArray();

        InstancesList.ItemsSource = items;
        EmptyLibrary.Visibility = items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        InstancesList.Visibility = items.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        if (items.Length > 0)
            InstancesList.SelectedIndex = 0;
        else
            ResetInstanceDetails();
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
        finally
        {
            SetBusy(false);
            RefreshButton();
        }
    }

    private async Task LoadJavaRuntimesAsync(CancellationToken token, bool forceRefresh = false)
    {
        if (javaRefreshRunning && !forceRefresh) return;

        javaRefreshRunning = true;
        RedetectJavaButton.IsEnabled = false;
        try
        {
            IReadOnlyList<JavaRuntime> runtimes = [];
            if (!forceRefresh)
                runtimes = await javaRuntimeCache.LoadAsync(JavaCacheLifetime, token);

            if (runtimes.Count == 0)
            {
                runtimes = await javaDetector.DetectAsync(token);
                try { await javaRuntimeCache.SaveAsync(runtimes, token); }
                catch (OperationCanceledException) { throw; }
                catch { }
            }

            javaRuntimes.Clear();
            javaRuntimes.AddRange(runtimes);

            selectedJavaRuntime = await ResolveRuntimePathAsync(launcherSettings.JavaPath, "Global", token)
                                  ?? FindRecommendedRuntime(null);
            UpdateJavaDisplay();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch
        {
            selectedJavaRuntime = null;
            UpdateJavaDisplay();
        }
        finally
        {
            javaRefreshRunning = false;
            RedetectJavaButton.IsEnabled = !busy;
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

        if (requiredMajor is > 0 &&
            (selectedJavaRuntime is null || !JavaCompatibility.Evaluate(selectedJavaRuntime, requiredMajor.Value).IsCompatible))
        {
            selectedJavaRuntime = FindRecommendedRuntime(requiredMajor);
        }
        else if (selectedJavaRuntime is null)
        {
            selectedJavaRuntime = FindRecommendedRuntime(requiredMajor);
        }

        UpdateJavaDisplay(requiredMajor);

        if (!busy && InstallPanel.Visibility == Visibility.Visible)
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
        if (JavaBox is null || SettingsJavaText is null) return;

        SettingsRuntimeCountText.Text = javaRuntimes.Count == 1
            ? "1 runtime detectado"
            : $"{javaRuntimes.Count} runtimes detectados";

        if (selectedJavaRuntime is null)
        {
            JavaBox.Text = javaRuntimes.Count == 0 ? "No se detectó Java" : "Sin runtime compatible";
            JavaBox.ToolTip = "Puedes seleccionar un runtime manualmente.";
            JavaBox.BorderBrush = new SolidColorBrush(Color.FromRgb(164, 73, 73));
            SettingsJavaText.Text = JavaBox.Text;
            SettingsJavaPathText.Text = "Selecciona un runtime o vuelve a ejecutar la detección.";
            return;
        }

        var summary = $"Java {selectedJavaRuntime.MajorVersion} · {selectedJavaRuntime.Vendor} · {selectedJavaRuntime.Architecture}";
        JavaBox.Text = summary;
        JavaBox.ToolTip = selectedJavaRuntime.JavaExecutable;
        SettingsJavaText.Text = summary;
        SettingsJavaPathText.Text = selectedJavaRuntime.JavaExecutable;

        var compatible = requiredMajor is not > 0 || JavaCompatibility.Evaluate(selectedJavaRuntime, requiredMajor.Value).IsCompatible;
        JavaBox.BorderBrush = new SolidColorBrush(compatible
            ? Color.FromRgb(58, 128, 91)
            : Color.FromRgb(164, 73, 73));
    }

    private void UpdateMemorySummary()
    {
        var recommended = MemoryRecommendation.RecommendMiB(memorySnapshot.TotalMiB);
        var total = memorySnapshot.TotalMiB > 0 ? FormatMemory(memorySnapshot.TotalMiB) : "desconocida";
        var available = memorySnapshot.AvailableMiB > 0 ? FormatMemory(memorySnapshot.AvailableMiB) : "desconocida";
        SettingsMemorySummary.Text = $"Sistema: {total} · Disponible: {available} · Recomendado: {FormatMemory(recommended)}";
        InstallMemoryHintText.Text = $"Recomendado para este equipo: {FormatMemory(recommended)} · límite seguro {FormatMemory((long)RamSlider.Maximum)}";
    }

    private static string FormatMemory(long memoryMiB) => memoryMiB >= 1024
        ? $"{memoryMiB / 1024d:0.#} GB"
        : $"{memoryMiB} MB";

    private void UpdateRamLabels(double installValue, double settingsValue)
    {
        if (RamText is not null) RamText.Text = FormatMemory((long)installValue);
        if (SettingsRamText is not null) SettingsRamText.Text = FormatMemory((long)settingsValue);
    }

    private async void ShowLibrary_Click(object sender, RoutedEventArgs e) => await ShowLibraryAsync();
    private void ShowInstall_Click(object sender, RoutedEventArgs e) => ShowInstall();
    private void ShowSettings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    private async Task ShowLibraryAsync()
    {
        ShowOnly(LibraryPanel);
        SetActiveNavigation(LibraryNavButton);
        await RefreshInstancesAsync();
    }

    private void ShowInstall()
    {
        ShowOnly(InstallPanel);
        SetActiveNavigation(InstallNavButton);
        SyncSettingsControls();
    }

    private void ShowSettings()
    {
        ShowOnly(SettingsPanel);
        SetActiveNavigation(SettingsNavButton);
        SyncSettingsControls();
        UpdateMemorySummary();
        SettingsStatusText.Text = "Los cambios se guardan localmente.";
    }

    private void ShowOnly(FrameworkElement panel)
    {
        LibraryPanel.Visibility = ReferenceEquals(panel, LibraryPanel) ? Visibility.Visible : Visibility.Collapsed;
        InstallPanel.Visibility = ReferenceEquals(panel, InstallPanel) ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = ReferenceEquals(panel, SettingsPanel) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetActiveNavigation(Button active)
    {
        var inactiveForeground = new SolidColorBrush(Color.FromRgb(143, 154, 175));
        var activeBackground = new SolidColorBrush(Color.FromRgb(24, 35, 52));
        foreach (var button in new[] { LibraryNavButton, InstallNavButton, SettingsNavButton })
        {
            button.Background = ReferenceEquals(button, active) ? activeBackground : Brushes.Transparent;
            button.Foreground = ReferenceEquals(button, active) ? Brushes.White : inactiveForeground;
        }
    }

    private async void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (busy || VersionBox.SelectedItem is not MinecraftVersion version) return;
        await SaveInstallDefaultsAsync();

        if (minecraft.IsInstalled(version.Id))
        {
            await LaunchAsync(version.Id);
            return;
        }

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
            _ = existing ?? await instanceManager.CreateAsync($"Minecraft {version.Id}", version.Id, cancellationToken: operation.Token);

            await ShowLibraryAsync();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Operación cancelada.";
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
            RefreshButton();
            operation?.Dispose();
            operation = null;
        }
    }

    private async void LibraryPlay_Click(object sender, RoutedEventArgs e)
    {
        if (InstancesList.SelectedItem is not InstanceItem item) return;
        var instance = await instanceManager.GetAsync(item.Id, lifetime.Token);
        if (instance is null)
        {
            MessageBox.Show(this, "La instancia seleccionada ya no existe.", "NEXO Client", MessageBoxButton.OK, MessageBoxImage.Warning);
            await RefreshInstancesAsync();
            return;
        }

        await LaunchAsync(item.VersionId, instance);
    }

    private async Task LaunchAsync(string versionId, GameInstance? instance = null)
    {
        if (busy) return;
        await SaveInstallDefaultsAsync();

        var effective = LauncherSettingsResolver.Resolve(launcherSettings, instance?.Settings ?? new InstanceSettings());
        var runtime = await ResolveRuntimePathAsync(effective.JavaPath, instance is null ? "Global" : "Instancia", lifetime.Token)
                      ?? selectedJavaRuntime;

        if (runtime is null)
        {
            ShowSettings();
            MessageBox.Show(this, "NEXO no encontró un runtime Java válido. Selecciona uno en Configuración.", "Java requerido", MessageBoxButton.OK, MessageBoxImage.Information);
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
                ShowSettings();
                selectedJavaRuntime = FindRecommendedRuntime(requiredMajor);
                UpdateJavaDisplay(requiredMajor);
                MessageBox.Show(this, compatibility.Message, "Java incompatible", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else if (!File.Exists(runtime.JavawExecutable))
        {
            ShowSettings();
            MessageBox.Show(this, "El runtime seleccionado no contiene javaw.exe.", "Java inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            SetBusy(true, "Iniciando Minecraft…");
            LibraryPlayButton.IsEnabled = false;

            var memoryMiB = Math.Clamp(
                effective.MemoryMiB,
                MemoryRecommendation.MinimumMiB,
                MemoryRecommendation.SafeMaximumMiB(memorySnapshot.TotalMiB));

            var process = minecraft.Launch(new LaunchOptions(
                versionId,
                runtime.JavawExecutable,
                launcherSettings.Username,
                memoryMiB));

            await Task.Delay(700, lifetime.Token);
            if (!process.HasExited)
            {
                if (launcherSettings.CloseLauncherOnGameStart)
                    System.Windows.Application.Current.Shutdown();
                return;
            }

            throw new InvalidOperationException("Java terminó antes de iniciar Minecraft.");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
            RefreshButton();
            LibraryPlayButton.IsEnabled = InstancesList.SelectedItem is not null;
        }
    }

    private async Task<JavaRuntime?> ResolveRuntimePathAsync(string? configuredPath, string source, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(configuredPath)) return null;

        var normalized = NormalizeJavaExecutable(configuredPath);
        var detected = javaRuntimes.FirstOrDefault(runtime =>
            string.Equals(runtime.JavaExecutable, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(runtime.JavawExecutable, configuredPath, StringComparison.OrdinalIgnoreCase));
        if (detected is not null) return detected;

        try
        {
            var inspected = await javaInspector.InspectAsync(normalized, source, token);
            if (inspected is null) return null;
            AddOrReplaceRuntime(inspected);
            return inspected;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    private async void InstancesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            await UpdateInstanceDetailsAsync(InstancesList.SelectedItem as InstanceItem);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private async Task UpdateInstanceDetailsAsync(InstanceItem? item)
    {
        if (item is null)
        {
            ResetInstanceDetails();
            return;
        }

        DetailName.Text = item.Name;
        DetailSubtitle.Text = "Resolviendo configuración…";
        DetailVersion.Text = item.VersionId;
        LibraryPlayButton.IsEnabled = !busy;

        var instance = await instanceManager.GetAsync(item.Id, lifetime.Token);
        if (instance is null)
        {
            ResetInstanceDetails();
            return;
        }

        var effective = LauncherSettingsResolver.Resolve(launcherSettings, instance.Settings);
        DetailLoader.Text = instance.Loader.ToString();
        DetailMemory.Text = $"{FormatMemory(effective.MemoryMiB)} · {(instance.Settings.MemoryMiB is null ? "Global" : "Override")}";
        DetailJava.Text = FormatJavaSetting(effective.JavaPath, instance.Settings.JavaPath is null ? "Global" : "Override");
        DetailSubtitle.Text = "Lista para iniciar";
    }

    private string FormatJavaSetting(string? javaPath, string source)
    {
        if (string.IsNullOrWhiteSpace(javaPath))
            return selectedJavaRuntime is null ? $"Automático · {source}" : $"Java {selectedJavaRuntime.MajorVersion} · Automático";

        var normalized = NormalizeJavaExecutable(javaPath);
        var runtime = javaRuntimes.FirstOrDefault(item =>
            string.Equals(item.JavaExecutable, normalized, StringComparison.OrdinalIgnoreCase));
        return runtime is null
            ? $"{Path.GetFileName(Path.GetDirectoryName(normalized)) ?? "Java"} · {source}"
            : $"Java {runtime.MajorVersion} · {runtime.Vendor} · {source}";
    }

    private void ResetInstanceDetails()
    {
        DetailName.Text = "Selecciona una instancia";
        DetailSubtitle.Text = "Los detalles aparecerán aquí.";
        DetailVersion.Text = "—";
        DetailLoader.Text = "—";
        DetailMemory.Text = "—";
        DetailJava.Text = "—";
        LibraryPlayButton.IsEnabled = false;
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

        await BrowseJavaAsync(requiredMajor, persistImmediately: true);
    }

    private async void SettingsBrowseJava_Click(object sender, RoutedEventArgs e)
    {
        await BrowseJavaAsync(null, persistImmediately: false);
    }

    private async Task BrowseJavaAsync(int? requiredMajor, bool persistImmediately)
    {
        if (javaRuntimes.Count > 0)
        {
            var selector = new JavaRuntimeDialog(javaRuntimes, requiredMajor, selectedJavaRuntime) { Owner = this };
            var result = selector.ShowDialog();
            if (result == true && selector.SelectedRuntime is not null)
            {
                selectedJavaRuntime = selector.SelectedRuntime;
                UpdateJavaDisplay(requiredMajor);
                if (persistImmediately)
                {
                    await PersistLauncherSettingsAsync(launcherSettings with
                    {
                        JavaPath = selectedJavaRuntime.JavaExecutable
                    });
                }
                return;
            }

            if (!selector.ManualBrowseRequested) return;
        }

        await BrowseManualJavaAsync(requiredMajor, persistImmediately);
    }

    private async Task BrowseManualJavaAsync(int? requiredMajor, bool persistImmediately)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecciona java.exe o javaw.exe",
            Filter = "Java para Windows|java.exe;javaw.exe|Ejecutables|*.exe"
        };
        if (dialog.ShowDialog(this) != true) return;

        var executable = NormalizeJavaExecutable(dialog.FileName);
        JavaRuntime? runtime;
        try
        {
            runtime = await javaInspector.InspectAsync(executable, "Manual", lifetime.Token);
        }
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

        try { await javaRuntimeCache.SaveAsync(javaRuntimes, lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch { }

        if (persistImmediately)
        {
            await PersistLauncherSettingsAsync(launcherSettings with { JavaPath = runtime.JavaExecutable });
        }

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

    private async void RedetectJava_Click(object sender, RoutedEventArgs e)
    {
        if (javaRefreshRunning || busy) return;

        SettingsStatusText.Text = "Detectando runtimes Java…";
        SidebarStatusText.Text = "DETECTANDO JAVA";
        try
        {
            javaRuntimeCache.Invalidate();
            await LoadJavaRuntimesAsync(lifetime.Token, forceRefresh: true);
            await RefreshJavaCompatibilityAsync();
            SettingsStatusText.Text = javaRuntimes.Count == 0
                ? "No se encontraron runtimes Java compatibles."
                : $"Detección completada: {javaRuntimes.Count} runtime(s).";
        }
        finally
        {
            SidebarStatusText.Text = "NEXO CORE LISTO";
        }
    }

    private void UseRecommendedMemory_Click(object sender, RoutedEventArgs e)
    {
        var recommended = Math.Clamp(
            MemoryRecommendation.RecommendMiB(memorySnapshot.TotalMiB),
            (int)SettingsRamSlider.Minimum,
            (int)SettingsRamSlider.Maximum);
        SettingsRamSlider.Value = recommended;
        SettingsStatusText.Text = $"Memoria recomendada seleccionada: {FormatMemory(recommended)}.";
    }

    private async void SettingsSave_Click(object sender, RoutedEventArgs e)
    {
        var username = string.IsNullOrWhiteSpace(SettingsUsernameBox.Text) ? "Player" : SettingsUsernameBox.Text.Trim();
        await PersistLauncherSettingsAsync(launcherSettings with
        {
            MemoryMiB = (int)SettingsRamSlider.Value,
            JavaPath = selectedJavaRuntime?.JavaExecutable ?? launcherSettings.JavaPath,
            Username = username,
            CloseLauncherOnGameStart = SettingsCloseLauncherCheck.IsChecked != false
        }, "Configuración guardada.");

        await UpdateInstanceDetailsAsync(InstancesList.SelectedItem as InstanceItem);
    }

    private async void VersionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshButton();
        await RefreshJavaCompatibilityAsync();
    }

    private void RamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RamText is not null) RamText.Text = FormatMemory((long)e.NewValue);
        if (!syncingSettingsUi && SettingsRamSlider is not null) SettingsRamSlider.Value = e.NewValue;
    }

    private void SettingsRamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SettingsRamText is not null) SettingsRamText.Text = FormatMemory((long)e.NewValue);
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
        RedetectJavaButton.IsEnabled = !value && !javaRefreshRunning;

        if (value)
        {
            PrimaryButton.Content = "TRABAJANDO…";
            SidebarStatusText.Text = "NEXO TRABAJANDO";
        }
        else
        {
            SidebarStatusText.Text = "NEXO CORE LISTO";
        }

        if (status is not null) StatusText.Text = status;
        if (!value) Progress.Visibility = Visibility.Collapsed;
    }

    private void ShowError(Exception exception)
    {
        StatusText.Text = "No se pudo completar la operación.";
        SidebarStatusText.Text = "NEXO REQUIERE ATENCIÓN";
        MessageBox.Show(this, exception.Message, "NEXO Client", MessageBoxButton.OK, MessageBoxImage.Error);
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
