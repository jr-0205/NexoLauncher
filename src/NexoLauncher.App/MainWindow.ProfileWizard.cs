using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NexoLauncher.Domain.Instances;
using NexoLauncher.Minecraft;

namespace NexoLauncher.App;

public partial class MainWindow
{
    private bool profileWizardHooksInitialized;

    private void InitializeProfileWizardEntryPoints()
    {
        if (profileWizardHooksInitialized) return;
        profileWizardHooksInitialized = true;

        foreach (var button in EnumerateVisualChildren<Button>(this))
        {
            var label = button.Content as string;
            if (!ReferenceEquals(button, InstallNavButton) &&
                !string.Equals(label, "＋  NUEVA INSTANCIA", StringComparison.Ordinal) &&
                !string.Equals(label, "CREAR INSTANCIA", StringComparison.Ordinal)) continue;

            button.Click -= ShowInstall_Click;
            button.Click += OpenProfileWizard_Click;
        }
    }

    private async void OpenProfileWizard_Click(object sender, RoutedEventArgs e) => await OpenProfileWizardAsync();

    private async Task OpenProfileWizardAsync()
    {
        if (busy || activeLaunch is not null)
        {
            if (activeLaunch is not null) ShowActiveLaunchNotice();
            return;
        }

        if (availableVersions.Count == 0)
        {
            await LoadVersionsAsync();
            if (availableVersions.Count == 0)
            {
                MessageBox.Show(this,
                    "NEXO no pudo obtener el catálogo de versiones de Minecraft. Comprueba tu conexión y vuelve a intentarlo.",
                    "Crear perfil",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
        }

        var recommended = MemoryRecommendation.RecommendMiB(memorySnapshot.TotalMiB);
        var safeMaximum = MemoryRecommendation.SafeMaximumMiB(memorySnapshot.TotalMiB);
        var wizard = new CreateProfileWizard(
            availableVersions,
            async (version, loader, token) => loader == LoaderType.Vanilla
                ? new[] { new LoaderVersion("Vanilla", true) }
                : await minecraft.GetLoaderVersionsAsync(LoaderId(loader), version.Id, token),
            recommended,
            safeMaximum,
            lifetime.Token)
        {
            Owner = this
        };

        if (wizard.ShowDialog() != true || wizard.Result is null) return;
        await CreateProfileFromWizardAsync(wizard.Result);
    }

    private async Task CreateProfileFromWizardAsync(CreateProfileWizardResult request)
    {
        operation?.Dispose();
        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        GameInstance? created = null;

        try
        {
            ShowOnly(LibraryPanel);
            SetActiveNavigation(LibraryNavButton);
            SetBusy(true, $"Preparando {request.Name}…");
            SidebarStatusText.Text = "CREANDO PERFIL";

            int? requiredMajor;
            try { requiredMajor = await GetRequiredJavaMajorAsync(request.Version, operation.Token); }
            catch { requiredMajor = MinecraftJavaVersionPolicy.InferRequiredMajor(request.Version.Id); }

            var runtime = FindRecommendedRuntime(requiredMajor);
            if (runtime is null && !javaRefreshRunning)
            {
                await LoadJavaRuntimesAsync(operation.Token, forceRefresh: true);
                runtime = FindRecommendedRuntime(requiredMajor);
            }

            var loaderId = LoaderId(request.Loader);
            var progress = new Progress<InstallProgress>(value =>
            {
                var detail = value.Total > 0 ? $" · {value.Completed}/{value.Total}" : string.Empty;
                SidebarStatusText.Text = value.Stage.ToUpperInvariant();
                DetailSubtitle.Text = value.Stage + detail;
            });

            if (!minecraft.IsInstalled(request.Version.Id, loaderId, request.LoaderVersion))
            {
                if (request.Loader is LoaderType.Forge or LoaderType.NeoForge && runtime is null)
                    throw new InvalidOperationException($"{request.Loader} necesita Java {requiredMajor?.ToString() ?? "compatible"}, pero NEXO no encontró ese runtime.");

                await minecraft.InstallAsync(
                    new LoaderInstallRequest(request.Version, request.LoaderVersion, runtime?.JavaExecutable),
                    loaderId,
                    progress,
                    operation.Token);
            }

            created = await instanceManager.CreateAsync(
                request.Name,
                request.Version.Id,
                request.Loader,
                request.LoaderVersion,
                operation.Token);

            var instanceRoot = instanceRepository.GetInstanceDirectory(created.Id);
            contentManager.EnsureLayout(Path.Combine(instanceRoot, "game"));
            var artwork = await ProfileArtworkStore.ImportAsync(
                instanceRoot,
                request.IconSourcePath,
                request.BackgroundSourcePath,
                operation.Token);

            var settings = request.MemoryMiB is null
                ? created.Settings
                : created.Settings with { MemoryMiB = request.MemoryMiB };
            created = created with
            {
                Description = request.Description,
                IconPath = artwork.IconRelativePath,
                BackgroundPath = artwork.BackgroundRelativePath,
                Settings = settings,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await instanceRepository.SaveAsync(created, operation.Token);
            await ProfileArtworkStore.SaveMetadataAsync(instanceRoot, artwork, operation.Token);

            await ShowLibraryAsync();
            if (InstancesList.ItemsSource is IEnumerable<InstanceItem> items)
                InstancesList.SelectedItem = items.FirstOrDefault(value => value.Id == created.Id);
            DetailSubtitle.Text = "Perfil creado · listo para iniciar";
            SidebarStatusText.Text = "NEXO CORE LISTO";
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            DetailSubtitle.Text = "Creación cancelada";
        }
        catch (Exception exception)
        {
            if (created is not null)
            {
                try { await instanceManager.DeleteAsync(created.Id, CancellationToken.None); }
                catch { }
            }
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
            SidebarStatusText.Text = activeLaunch is null ? "NEXO CORE LISTO" : "MINECRAFT EN EJECUCIÓN";
            operation?.Dispose();
            operation = null;
        }
    }

    private static IEnumerable<T> EnumerateVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed) yield return typed;
            foreach (var descendant in EnumerateVisualChildren<T>(child)) yield return descendant;
        }
    }
}
