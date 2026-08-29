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
using NexoLauncher.Java.Selection;
using NexoLauncher.Minecraft;
using NexoLauncher.Minecraft.Java;

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
    private bool loaderVersionsLoading;

    public MainWindow()
    {
        InitializeComponent();
        paths.EnsureCreated();

        minecraft = new MinecraftRuntime(httpClient, paths.Root, paths.Cache, paths.Logs);
        instanceRepository = new JsonInstanceRepository(paths.Instances);
        instanceManager = new InstanceManager(instanceRepository);
        launcherSettingsStore = new JsonLauncherSettingsStore(Path.Combine(paths.Root, "settings.json"));
        javaRuntimeCache = new JsonJavaRuntimeCache(Path.Combine(paths.Cache, "java-runtimes.json"));
        javaDetector = new JavaRuntimeDetector(javaInspector);

        InstallPathText.Text = paths.Root;
        JavaBox.Text = "Detectando runtimes Java…";
        SidebarStatusText.Text = "INICIALIZANDO NEXO";
        LoaderBox.ItemsSource = new[]
        {
            new LoaderChoice(LoaderType.Vanilla, "Vanilla"),
            new LoaderChoice(LoaderType.Fabric, "Fabric"),
            new LoaderChoice(LoaderType.Forge, "Forge"),
            new LoaderChoice(LoaderType.NeoForge, "NeoForge")
        };
        LoaderBox.SelectedIndex = 0;

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

        // Desde NEXO 0.4 Java global deja de ser un requisito. Java se resuelve por versión.
        launcherSettings = (launcherSettings with
        {
            MemoryMiB = Math.Clamp(launcherSettings.MemoryMiB, MemoryRecommendation.MinimumMiB, safeMaximum),
            JavaPath = null
        }).Normalize();

        if (existed)
        {
            try { await launcherSettingsStore.SaveAsync(launcherSettings, lifetime.Token); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch { }
        }

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
            MemoryMiB = Math.Clamp(settings.MemoryMiB, MemoryRecommendation.MinimumMiB, safeMaximum),
            JavaPath = null
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
            if (availableVersions.Count > 0 && string.IsNullOrWhiteSpace(InstanceNameBox.Text))
                InstanceNameBox.Text = "Minecraft " + availableVersions[0].Id;
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
            selectedJavaRuntime = FindRecommendedRuntime(null);
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
            selectedJavaRuntime = FindRecommendedRuntime(null);
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

        selectedJavaRuntime = FindRecommendedRuntime(requiredMajor);
        UpdateJavaDisplay(requiredMajor);

        if (!busy && InstallPanel.Visibility == Visibility.Visible)
        {
            if (selectedJavaRuntime is not null)
            {
                StatusText.Text = requiredMajor is > 0
                    ? $"Minecraft {version.Id} requiere Java {requiredMajor}. NEXO eligió Java {selectedJavaRuntime.MajorVersion} automáticamente."
                    : $"Minecraft {version.Id} · NEXO eligió Java {selectedJavaRuntime.MajorVersion} automáticamente.";
            }
            else
            {
                StatusText.Text = requiredMajor is > 0
                    ? $"Minecraft {version.Id} requiere Java {requiredMajor}. No está instalado."
                    : $"Minecraft {version.Id} · no se encontró un runtime Java utilizable.";
            }
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
        var usable = javaRuntimes
            .Where(IsRuntimeUsable)
            .ToArray();
        return JavaRuntimeSelector.Select(usable, requiredMajor);
    }

    private static bool IsRuntimeUsable(JavaRuntime runtime)
        => File.Exists(runtime.JavaExecutable)
           && File.Exists(runtime.JavawExecutable)
           && (!Environment.Is64BitOperatingSystem || runtime.Is64Bit);

    private void UpdateJavaDisplay(int? requiredMajor = null)
    {
        if (JavaBox is null || SettingsJavaText is null) return;

        var majors = JavaRuntimeSelector.DetectedMajors(javaRuntimes);
        SettingsRuntimeCountText.Text = javaRuntimes.Count == 1
            ? "1 runtime detectado"
            : $"{javaRuntimes.Count} runtimes detectados";

        SettingsJavaText.Text = "Selección automática por versión";
        SettingsJavaPathText.Text = majors.Count == 0
            ? "No hay runtimes Java detectados."
            : "Disponibles: " + string.Join(" · ", majors.Select(major => $"Java {major}"));

        if (selectedJavaRuntime is null)
        {
            JavaBox.Text = requiredMajor is > 0
                ? $"Automático · Falta Java {requiredMajor}"
                : javaRuntimes.Count == 0 ? "Automático · No se detectó Java" : "Automático · Sin runtime utilizable";
            JavaBox.ToolTip = "NEXO selecciona Java automáticamente según la versión de Minecraft.";
            JavaBox.BorderBrush = new SolidColorBrush(Color.FromRgb(164, 73, 73));
            return;
        }

        JavaBox.Text = $"Automático · Java {selectedJavaRuntime.MajorVersion} · {selectedJavaRuntime.Vendor} · {selectedJavaRuntime.Architecture}";
        JavaBox.ToolTip = selectedJavaRuntime.JavaExecutable;
        JavaBox.BorderBrush = new SolidColorBrush(Color.FromRgb(58, 128, 91));
    }

    private string DetectedJavaSummary()
    {
        var majors = JavaRuntimeSelector.DetectedMajors(javaRuntimes);
        return majors.Count == 0
            ? "ninguno"
            : string.Join(", ", majors.Select(major => $"Java {major}"));
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
        SettingsStatusText.Text = "Java se selecciona automáticamente según la versión del juego.";
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

        var loader = SelectedLoader();
        var loaderVersion = loader.Type == LoaderType.Vanilla ? null : (LoaderVersionBox.SelectedItem as LoaderVersion)?.Version;
        if (loader.Type != LoaderType.Vanilla && string.IsNullOrWhiteSpace(loaderVersion))
        {
            MessageBox.Show(this, $"Selecciona una versión de {loader.Name}.", "Loader requerido", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var instanceName = string.IsNullOrWhiteSpace(InstanceNameBox.Text)
            ? $"Minecraft {version.Id} · {loader.Name}"
            : InstanceNameBox.Text.Trim();

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

            var loaderId = LoaderId(loader.Type);
            if (!minecraft.IsInstalled(version.Id, loaderId, loaderVersion))
            {
                if (loader.Type is LoaderType.Forge or LoaderType.NeoForge && selectedJavaRuntime is null)
                    throw new InvalidOperationException($"{loader.Name} necesita un runtime Java compatible. Espera a que NEXO termine de detectar Java.");
                await minecraft.InstallAsync(new LoaderInstallRequest(version, loaderVersion, selectedJavaRuntime?.JavaExecutable), loaderId, reporter, operation.Token);
            }

            await instanceManager.CreateAsync(instanceName, version.Id, loader.Type, loaderVersion, operation.Token);

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
        var version = availableVersions.FirstOrDefault(item => item.Id == versionId);

        // Las instancias instaladas deben seguir resolviendo Java aunque el catálogo
        // remoto todavía no esté disponible durante este arranque.
        int? requiredMajor = MinecraftJavaVersionPolicy.InferRequiredMajor(versionId);
        if (version is not null)
        {
            try { requiredMajor = await GetRequiredJavaMajorAsync(version, lifetime.Token); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
            catch { }
        }

        JavaRuntime? runtime;
        if (!string.IsNullOrWhiteSpace(effective.JavaPath))
        {
            runtime = await ResolveRuntimePathAsync(effective.JavaPath, "Override de instancia", lifetime.Token);
            if (runtime is null)
            {
                MessageBox.Show(this,
                    "El Java configurado específicamente para esta instancia ya no existe o no es válido.",
                    "Override Java inválido",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (requiredMajor is > 0)
            {
                var overrideCompatibility = JavaCompatibility.Evaluate(runtime, requiredMajor.Value);
                if (!overrideCompatibility.IsCompatible)
                {
                    MessageBox.Show(this,
                        overrideCompatibility.Message + " Quita el override de la instancia para volver a selección automática.",
                        "Override Java incompatible",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }
        }
        else
        {
            runtime = FindRecommendedRuntime(requiredMajor);

            if (runtime is null && !javaRefreshRunning)
            {
                await LoadJavaRuntimesAsync(lifetime.Token, forceRefresh: true);
                runtime = FindRecommendedRuntime(requiredMajor);
            }

            if (runtime is null)
            {
                ShowSettings();
                var requirement = requiredMajor is > 0 ? $"Java {requiredMajor}" : "un runtime Java compatible";
                MessageBox.Show(this,
                    $"Minecraft {versionId} necesita {requirement}. Detectados: {DetectedJavaSummary()}. Instala el runtime necesario y pulsa “DETECTAR DE NUEVO”.",
                    "Java requerido",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
        }

        selectedJavaRuntime = runtime;
        UpdateJavaDisplay(requiredMajor);

        try
        {
            SetBusy(true, $"Iniciando Minecraft {versionId} con Java {runtime.MajorVersion}…");
            LibraryPlayButton.IsEnabled = false;

            var memoryMiB = Math.Clamp(
                effective.MemoryMiB,
                MemoryRecommendation.MinimumMiB,
                MemoryRecommendation.SafeMaximumMiB(memorySnapshot.TotalMiB));

            var loaderId = LoaderId(instance?.Loader ?? LoaderType.Vanilla);
            var gameDirectory = instance is null
                ? Path.Combine(paths.Instances, "nexo-" + versionId, "game")
                : Path.Combine(instanceRepository.GetInstanceDirectory(instance.Id), "game");
            var plan = minecraft.CreateLaunchPlan(versionId, loaderId, instance?.LoaderVersion, gameDirectory);
            var session = minecraft.Launch(new LaunchOptions(
                versionId,
                runtime.JavaExecutable,
                launcherSettings.Username,
                memoryMiB,
                JvmArguments: effective.JvmArguments,
                WindowWidth: effective.WindowWidth,
                WindowHeight: effective.WindowHeight,
                Fullscreen: effective.Fullscreen == true), plan);

            await Task.Delay(1200, lifetime.Token);
            if (!session.Process.HasExited)
            {
                if (launcherSettings.CloseLauncherOnGameStart)
                    System.Windows.Application.Current.Shutdown();
                return;
            }

            var details = await session.GetFailureDetailsAsync();
            throw new InvalidOperationException(
                $"Java terminó antes de iniciar Minecraft (código {session.Process.ExitCode}).\n\n" +
                $"{details}\n\nRegistro completo: {session.LogPath}");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            var log = AppendLaunchFailureLog(versionId, instance, runtime, exception);
            var logNotice = log.Error is null
                ? $"Registro completo: {log.Path}"
                : $"No se pudo guardar el registro en {log.Path}: {log.Error}";
            ShowError(new InvalidOperationException(
                exception.Message.Contains(log.Path, StringComparison.OrdinalIgnoreCase)
                    ? exception.Message
                    : $"{exception.Message}\n\n{logNotice}",
                exception));
        }
        finally
        {
            SetBusy(false);
            RefreshButton();
            LibraryPlayButton.IsEnabled = InstancesList.SelectedItem is not null;
        }
    }

    private (string Path, string? Error) AppendLaunchFailureLog(string versionId, GameInstance? instance, JavaRuntime runtime, Exception exception)
    {
        var logPath = Path.Combine(paths.Logs, "latest-minecraft.log");
        try
        {
            Directory.CreateDirectory(paths.Logs);
            File.AppendAllText(logPath,
                $"{Environment.NewLine}[NEXO] Fallo de lanzamiento · {DateTimeOffset.Now:O}{Environment.NewLine}" +
                $"Minecraft: {versionId}{Environment.NewLine}" +
                $"Loader: {instance?.Loader.ToString() ?? "Vanilla"} {instance?.LoaderVersion}{Environment.NewLine}" +
                $"Java: {runtime.JavaExecutable} · versión {runtime.MajorVersion}{Environment.NewLine}" +
                $"{exception}{Environment.NewLine}");
        }
        catch (Exception logException) when (logException is IOException or UnauthorizedAccessException)
        {
            return (logPath, logException.Message);
        }
        return (logPath, null);
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

        if (!string.IsNullOrWhiteSpace(instance.Settings.JavaPath))
        {
            DetailJava.Text = FormatJavaOverride(instance.Settings.JavaPath);
        }
        else if (javaRequirements.TryGetValue(item.VersionId, out var cachedRequirement))
        {
            var automatic = FindRecommendedRuntime(cachedRequirement);
            DetailJava.Text = automatic is null
                ? "Automático · runtime pendiente"
                : $"Java {automatic.MajorVersion} · Automático";
        }
        else
        {
            DetailJava.Text = "Automático · Según versión";
        }

        DetailSubtitle.Text = "Lista para iniciar";
        EditInstanceButton.IsEnabled = !busy;
        DeleteInstanceButton.IsEnabled = !busy;
    }

    private string FormatJavaOverride(string javaPath)
    {
        var normalized = NormalizeJavaExecutable(javaPath);
        var runtime = javaRuntimes.FirstOrDefault(item =>
            string.Equals(item.JavaExecutable, normalized, StringComparison.OrdinalIgnoreCase));
        return runtime is null
            ? $"Override · {Path.GetFileName(Path.GetDirectoryName(normalized)) ?? "Java"}"
            : $"Java {runtime.MajorVersion} · {runtime.Vendor} · Override";
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
        EditInstanceButton.IsEnabled = false;
        DeleteInstanceButton.IsEnabled = false;
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

        await BrowseJavaAsync(requiredMajor);
    }

    private async void SettingsBrowseJava_Click(object sender, RoutedEventArgs e)
    {
        await BrowseJavaAsync(null);
        SettingsStatusText.Text = "El runtime elegido solo es una vista previa. El arranque sigue en modo automático por versión.";
    }

    private async Task BrowseJavaAsync(int? requiredMajor)
    {
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

        SettingsStatusText.Text = "Detectando todos los runtimes Java…";
        SidebarStatusText.Text = "DETECTANDO JAVA";
        try
        {
            javaRuntimeCache.Invalidate();
            await LoadJavaRuntimesAsync(lifetime.Token, forceRefresh: true);
            await RefreshJavaCompatibilityAsync();
            SettingsStatusText.Text = javaRuntimes.Count == 0
                ? "No se encontraron runtimes Java."
                : $"Detección completada: {javaRuntimes.Count} runtime(s) · {DetectedJavaSummary()}.";
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
            Username = username,
            CloseLauncherOnGameStart = SettingsCloseLauncherCheck.IsChecked != false
        }, "Configuración guardada. Java permanece en selección automática por versión.");

        await UpdateInstanceDetailsAsync(InstancesList.SelectedItem as InstanceItem);
    }

    private async void VersionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VersionBox.SelectedItem is MinecraftVersion selected &&
            (string.IsNullOrWhiteSpace(InstanceNameBox.Text) || InstanceNameBox.Text.StartsWith("Minecraft ", StringComparison.Ordinal)))
            InstanceNameBox.Text = "Minecraft " + selected.Id;
        await RefreshLoaderVersionsAsync();
        RefreshButton();
        await RefreshJavaCompatibilityAsync();
    }

    private async void LoaderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await RefreshLoaderVersionsAsync();
        RefreshButton();
    }

    private void LoaderVersionBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshButton();

    private async Task RefreshLoaderVersionsAsync()
    {
        if (LoaderVersionBox is null || LoaderBox?.SelectedItem is not LoaderChoice loader) return;
        if (loader.Type == LoaderType.Vanilla)
        {
            LoaderVersionBox.ItemsSource = new[] { new LoaderVersion("Vanilla", true) };
            LoaderVersionBox.SelectedIndex = 0;
            LoaderVersionBox.IsEnabled = false;
            return;
        }
        if (VersionBox.SelectedItem is not MinecraftVersion version) return;

        loaderVersionsLoading = true;
        LoaderVersionBox.IsEnabled = false;
        LoaderVersionBox.ItemsSource = null;
        try
        {
            StatusText.Text = $"Consultando versiones de {loader.Name}…";
            var versions = await minecraft.GetLoaderVersionsAsync(LoaderId(loader.Type), version.Id, lifetime.Token);
            LoaderVersionBox.ItemsSource = versions;
            LoaderVersionBox.SelectedIndex = versions.Count > 0 ? 0 : -1;
            StatusText.Text = versions.Count == 0
                ? $"{loader.Name} no publicó versiones para Minecraft {version.Id}."
                : $"{versions.Count} versiones de {loader.Name} disponibles.";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            StatusText.Text = "No se pudieron consultar las versiones del loader.";
            MessageBox.Show(this, exception.Message, loader.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            loaderVersionsLoading = false;
            LoaderVersionBox.IsEnabled = !busy && LoaderVersionBox.Items.Count > 0;
        }
    }

    private async void EditInstance_Click(object sender, RoutedEventArgs e)
    {
        if (InstancesList.SelectedItem is not InstanceItem item) return;
        var instance = await instanceManager.GetAsync(item.Id, lifetime.Token);
        if (instance is null) return;
        var editor = new InstanceEditorDialog(instance) { Owner = this };
        if (editor.ShowDialog() != true) return;

        try
        {
            await instanceManager.UpdateAsync(instance.Id, editor.UpdatedName, editor.UpdatedSettings, lifetime.Token);
            await RefreshInstancesAsync();
            var refreshed = ((IEnumerable<InstanceItem>)InstancesList.ItemsSource).FirstOrDefault(value => value.Id == instance.Id);
            if (refreshed is not null) InstancesList.SelectedItem = refreshed;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Editar instancia", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void DeleteInstance_Click(object sender, RoutedEventArgs e)
    {
        if (busy || InstancesList.SelectedItem is not InstanceItem item) return;

        var confirmation = MessageBox.Show(
            this,
            $"¿Eliminar definitivamente el pack '{item.Name}'?\n\n" +
            "Se borrarán su mundo, mods, configuración y todos los archivos guardados dentro de esta instancia. " +
            "Esta acción no se puede deshacer.",
            "Confirmar eliminación",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes) return;

        try
        {
            SetBusy(true, $"Eliminando {item.Name}…");
            var deleted = await instanceManager.DeleteAsync(item.Id, lifetime.Token);
            await RefreshInstancesAsync();
            StatusText.Text = deleted
                ? $"El pack '{item.Name}' fue eliminado."
                : "El pack ya no existía.";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Eliminar pack", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
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
            var loader = SelectedLoader();
            var loaderVersion = loader.Type == LoaderType.Vanilla ? null : (LoaderVersionBox.SelectedItem as LoaderVersion)?.Version;
            var selectionComplete = loader.Type == LoaderType.Vanilla || !string.IsNullOrWhiteSpace(loaderVersion);
            PrimaryButton.Content = selectionComplete && minecraft.IsInstalled(version.Id, LoaderId(loader.Type), loaderVersion)
                ? "CREAR INSTANCIA"
                : "DESCARGAR Y CREAR";
            PrimaryButton.IsEnabled = selectionComplete;
        }
        else
        {
            PrimaryButton.Content = "SIN VERSIONES";
            PrimaryButton.IsEnabled = false;
        }
    }

    private LoaderChoice SelectedLoader()
        => LoaderBox?.SelectedItem as LoaderChoice ?? new LoaderChoice(LoaderType.Vanilla, "Vanilla");

    private static string LoaderId(LoaderType type) => type switch
    {
        LoaderType.Vanilla => "vanilla",
        LoaderType.Fabric => "fabric",
        LoaderType.Forge => "forge",
        LoaderType.NeoForge => "neoforge",
        _ => throw new NotSupportedException($"El loader {type} todavía no forma parte de NEXO.")
    };

    private void SetBusy(bool value, string? status = null)
    {
        busy = value;
        VersionBox.IsEnabled = !value;
        LoaderBox.IsEnabled = !value;
        LoaderVersionBox.IsEnabled = !value && !loaderVersionsLoading && SelectedLoader().Type != LoaderType.Vanilla;
        InstanceNameBox.IsEnabled = !value;
        UsernameBox.IsEnabled = !value;
        RamSlider.IsEnabled = !value;
        PrimaryButton.IsEnabled = !value;
        RedetectJavaButton.IsEnabled = !value && !javaRefreshRunning;
        EditInstanceButton.IsEnabled = !value && InstancesList.SelectedItem is not null;
        DeleteInstanceButton.IsEnabled = !value && InstancesList.SelectedItem is not null;

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
    private sealed record LoaderChoice(LoaderType Type, string Name)
    {
        public override string ToString() => Name;
    }
}
