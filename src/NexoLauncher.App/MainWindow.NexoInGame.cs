using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NexoLauncher.Domain.Instances;
using NexoLauncher.Infrastructure.Content;

namespace NexoLauncher.App;

public partial class MainWindow
{
    private Button? rightShiftButton;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _ = Dispatcher.InvokeAsync(() =>
        {
            InitializeBrandingAndAbout();
            ApplyProductionShell();
            InitializeProductionLibraryExperience();
            InitializeProfileWizardEntryPoints();
            InitializeInstalledContentExperience();
            InitializeNexoInGameButton();
            if (NexoFeatureFlags.DeveloperTools) InitializeNexoInGameBuildTools();
        }, DispatcherPriority.Loaded);
    }

    private void InitializeNexoInGameButton()
    {
        if (rightShiftButton is not null || ContentInstanceButton.Parent is not Grid actionsGrid) return;

        if (actionsGrid.RowDefinitions.Count < 3)
            actionsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        rightShiftButton = new Button
        {
            Content = "＋ RIGHT SHIFT",
            Height = 32,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 8, 9, 0),
            FontSize = 10,
            ToolTip = "Instalar el NEXO In-Game precompilado compatible con este perfil",
            IsEnabled = false
        };
        rightShiftButton.SetResourceReference(Control.StyleProperty, "GhostButton");
        rightShiftButton.Click += AddRightShift_Click;

        Grid.SetRow(rightShiftButton, 2);
        Grid.SetColumn(rightShiftButton, 0);
        Grid.SetColumnSpan(rightShiftButton, 2);
        Grid.SetRowSpan(LibraryPlayButton, 3);
        actionsGrid.Children.Add(rightShiftButton);

        InstancesList.SelectionChanged += async (_, _) => await RefreshRightShiftButtonStateAsync();
        _ = RefreshRightShiftButtonStateAsync();
    }

    private async Task RefreshRightShiftButtonStateAsync()
    {
        if (rightShiftButton is null) return;
        if (InstancesList.SelectedItem is not InstanceItem item)
        {
            rightShiftButton.Content = "＋ RIGHT SHIFT";
            rightShiftButton.ToolTip = "Selecciona un perfil para comprobar NEXO In-Game.";
            rightShiftButton.IsEnabled = false;
            return;
        }

        if (IsNexoInGameInstalled(item.Id))
        {
            rightShiftButton.Content = "✓ RIGHT SHIFT";
            rightShiftButton.ToolTip = "NEXO In-Game ya está instalado. Shift derecho abre el menú dentro de Minecraft.";
            rightShiftButton.IsEnabled = !busy && activeLaunch is null;
            return;
        }

        var instance = await instanceManager.GetAsync(item.Id, lifetime.Token);
        if (instance is null)
        {
            rightShiftButton.Content = "＋ RIGHT SHIFT";
            rightShiftButton.IsEnabled = false;
            return;
        }

        try
        {
            var service = CreateNexoInGameArtifactService();
            var artifact = await service.FindPublishedArtifactAsync(instance, lifetime.Token);
            if (artifact is null)
            {
                rightShiftButton.Content = "RIGHT SHIFT · BUILD PENDIENTE";
                rightShiftButton.ToolTip = $"No hay una build local/publicada para Minecraft {instance.MinecraftVersion} + {instance.Loader}. Genera los JAR desde Configuración > NEXO In-Game Builds.";
                rightShiftButton.IsEnabled = false;
                return;
            }

            rightShiftButton.Content = "＋ RIGHT SHIFT";
            rightShiftButton.ToolTip = $"Instalar NEXO In-Game {artifact.NexoInGameVersion} precompilado y verificado.";
            rightShiftButton.IsEnabled = !busy && activeLaunch is null;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch
        {
            rightShiftButton.Content = "RIGHT SHIFT · SIN CATÁLOGO";
            rightShiftButton.ToolTip = "No se pudo comprobar el catálogo de NEXO In-Game.";
            rightShiftButton.IsEnabled = false;
        }
    }

    private bool IsNexoInGameInstalled(InstanceId id)
    {
        var mods = Path.Combine(instanceRepository.GetPaths(id).Game, "mods");
        return Directory.Exists(mods) &&
               Directory.EnumerateFiles(mods, "nexo-ingame*.jar", SearchOption.TopDirectoryOnly).Any();
    }

    private async void AddRightShift_Click(object sender, RoutedEventArgs e)
    {
        await AddRightShiftAsync();
    }

    private async Task AddRightShiftAsync()
    {
        if (busy || InstancesList.SelectedItem is not InstanceItem item) return;
        if (activeLaunch is not null)
        {
            NexoDialog.Info(this,
                "Minecraft está en ejecución",
                "Cierra Minecraft antes de añadir o actualizar NEXO In-Game.");
            return;
        }

        var instance = await instanceManager.GetAsync(item.Id, lifetime.Token);
        if (instance is null) return;

        if (IsNexoInGameInstalled(instance.Id))
        {
            NexoDialog.Info(this,
                "Right Shift listo",
                "NEXO In-Game ya está instalado. Inicia este perfil y pulsa Shift derecho dentro de Minecraft.");
            return;
        }

        var service = CreateNexoInGameArtifactService();
        NexoInGameArtifact? artifact;
        try
        {
            artifact = await service.FindPublishedArtifactAsync(instance, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            NexoDialog.Warning(this,
                "Catálogo NEXO In-Game",
                "NEXO no pudo comprobar el catálogo de NEXO In-Game.",
                details: exception.ToString());
            return;
        }

        if (artifact is null)
        {
            rightShiftButton!.Content = "RIGHT SHIFT · BUILD PENDIENTE";
            rightShiftButton.IsEnabled = false;
            NexoDialog.Info(this,
                "Build de NEXO In-Game pendiente",
                $"Todavía no existe un JAR disponible para Minecraft {instance.MinecraftVersion} + {instance.Loader}.\n\n" +
                "Genera los JAR desde Configuración > NEXO In-Game Builds. El botón del perfil nunca descargará Gradle ni compilará código.");
            return;
        }

        var confirmation = NexoDialog.Confirm(this,
            "Añadir Right Shift",
            $"Instalar NEXO In-Game {artifact.NexoInGameVersion} en '{instance.Name}'.\n\n" +
            "NEXO usará una build precompilada compatible, verificará SHA-256, reutilizará la caché compartida cuando sea posible y copiará únicamente el JAR necesario a mods/.\n\n" +
            "No se descargará Gradle ni se compilará código en este equipo.",
            "INSTALAR",
            "CANCELAR");
        if (!confirmation) return;

        try
        {
            SetBusy(true, $"Añadiendo Right Shift a {instance.Name}…");
            rightShiftButton!.Content = "RESOLVIENDO…";
            rightShiftButton.IsEnabled = false;
            DetailSubtitle.Text = "Resolviendo build precompilada de NEXO In-Game…";

            var progress = new Progress<string>(message =>
            {
                DetailSubtitle.Text = message;
                if (rightShiftButton is not null)
                {
                    rightShiftButton.Content = NexoInGameButtonText(message);
                    rightShiftButton.ToolTip = message;
                }
            });

            var result = await service.InstallAsync(
                instance,
                instanceRepository.GetPaths(instance.Id).Game,
                progress,
                lifetime.Token);

            if (!IsNexoInGameInstalled(instance.Id))
                throw new InvalidOperationException("NEXO In-Game terminó sin dejar un JAR válido dentro de mods/.");

            DetailSubtitle.Text = $"NEXO In-Game {result.Version} instalado · Right Shift listo";
            NexoDialog.Info(this,
                "Right Shift añadido",
                $"NEXO In-Game {result.Version} quedó instalado correctamente.\n\n" +
                (result.UsedCache ? "Se reutilizó la caché verificada de NEXO.\n\n" : string.Empty) +
                "Inicia Minecraft y pulsa Shift derecho. Desde el menú podrás cambiar entre Máximo FPS, Medio, Medio Alto y Alto.",
                "LISTO");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            DetailSubtitle.Text = "NEXO In-Game no está disponible para este perfil";
            NexoDialog.Warning(this,
                "NEXO In-Game no pudo instalarse",
                "La instalación se detuvo y NEXO no marcará Right Shift como listo.",
                details: exception.ToString());
        }
        finally
        {
            SetBusy(false);
            RefreshButton();
            await RefreshRightShiftButtonStateAsync();
        }
    }

    private NexoInGameArtifactService CreateNexoInGameArtifactService()
    {
        var generatedRoot = NexoInGameBuildOutputDirectory();
        var localArtifactRoot = File.Exists(Path.Combine(generatedRoot, "catalog.json"))
            ? generatedRoot
            : FindRepositoryDirectory("artifacts", "nexo-ingame");
        return new NexoInGameArtifactService(httpClient, contentCatalog, paths, localArtifactRoot);
    }

    private static string NexoInGameButtonText(string stage)
    {
        if (stage.Contains("cach", StringComparison.OrdinalIgnoreCase)) return "CACHÉ…";
        if (stage.Contains("Descarg", StringComparison.OrdinalIgnoreCase)) return "DESCARGANDO…";
        if (stage.Contains("SHA", StringComparison.OrdinalIgnoreCase)) return "VERIFICANDO…";
        if (stage.Contains("dependencia", StringComparison.OrdinalIgnoreCase)) return "DEPENDENCIAS…";
        if (stage.Contains("Instal", StringComparison.OrdinalIgnoreCase)) return "INSTALANDO…";
        if (stage.Contains("listo", StringComparison.OrdinalIgnoreCase)) return "✓ RIGHT SHIFT";
        return "RESOLVIENDO…";
    }

    private static string? FindRepositoryDirectory(params string[] relativeParts)
    {
        foreach (var root in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            DirectoryInfo? directory;
            try { directory = new DirectoryInfo(Path.GetFullPath(root)); }
            catch { continue; }

            for (var depth = 0; directory is not null && depth < 12; depth++, directory = directory.Parent)
            {
                var candidate = relativeParts.Aggregate(directory.FullName, Path.Combine);
                if (Directory.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}
