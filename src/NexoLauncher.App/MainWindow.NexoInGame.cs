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
            InitializeProfileWizardEntryPoints();
            InitializeNexoInGameButton();
            InitializeNexoInGameBuildTools();
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
            ToolTip = "Instalar el NEXO In-Game precompilado compatible con esta instancia",
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
            rightShiftButton.ToolTip = "Selecciona una instancia para comprobar NEXO In-Game.";
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
            MessageBox.Show(this,
                "Cierra Minecraft antes de añadir o actualizar NEXO In-Game.",
                "Minecraft está en ejecución",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var instance = await instanceManager.GetAsync(item.Id, lifetime.Token);
        if (instance is null) return;

        if (IsNexoInGameInstalled(instance.Id))
        {
            MessageBox.Show(this,
                "NEXO In-Game ya está instalado. Inicia esta instancia y pulsa Shift derecho dentro de Minecraft.",
                "Right Shift listo",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
            MessageBox.Show(this,
                "NEXO no pudo comprobar el catálogo de NEXO In-Game.\n\n" + exception.Message,
                "Catálogo NEXO In-Game",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (artifact is null)
        {
            rightShiftButton!.Content = "RIGHT SHIFT · BUILD PENDIENTE";
            rightShiftButton.IsEnabled = false;
            MessageBox.Show(this,
                $"Todavía no existe un JAR disponible de NEXO In-Game para Minecraft {instance.MinecraftVersion} + {instance.Loader}.\n\n" +
                "Ve a Configuración > NEXO In-Game Builds y usa GENERAR JARS NEXO IN-GAME. El botón de esta instancia nunca descargará Gradle ni compilará código.",
                "Build de NEXO In-Game pendiente",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var confirmation = MessageBox.Show(this,
            $"¿Añadir NEXO In-Game {artifact.NexoInGameVersion} a '{instance.Name}'?\n\n" +
            "NEXO usará una build precompilada compatible, verificará SHA-256, la guardará en caché compartida y la copiará a mods/.\n\n" +
            "No se descargará Gradle y no se compilará código en este equipo.",
            "Añadir Right Shift",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        if (confirmation != MessageBoxResult.Yes) return;

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
            MessageBox.Show(this,
                $"NEXO In-Game {result.Version} quedó instalado.\n\n" +
                (result.UsedCache ? "Se reutilizó la caché verificada de NEXO.\n\n" : string.Empty) +
                "Inicia Minecraft y pulsa Shift derecho para abrir el menú. Ahí podrás cambiar entre Máximo FPS, Medio, Medio Alto y Alto.",
                "Right Shift añadido",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            DetailSubtitle.Text = "NEXO In-Game no está disponible para esta instancia";
            MessageBox.Show(this, exception.Message, "NEXO In-Game", MessageBoxButton.OK, MessageBoxImage.Warning);
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
