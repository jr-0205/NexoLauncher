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
        _ = Dispatcher.InvokeAsync(InitializeNexoInGameButton, DispatcherPriority.Loaded);
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

        InstancesList.SelectionChanged += (_, _) => RefreshRightShiftButtonState();
        RefreshRightShiftButtonState();
    }

    private void RefreshRightShiftButtonState()
    {
        if (rightShiftButton is null) return;
        if (InstancesList.SelectedItem is not InstanceItem item)
        {
            rightShiftButton.Content = "＋ RIGHT SHIFT";
            rightShiftButton.IsEnabled = false;
            return;
        }

        var installed = IsNexoInGameInstalled(item.Id);
        rightShiftButton.Content = installed ? "✓ RIGHT SHIFT" : "＋ RIGHT SHIFT";
        rightShiftButton.ToolTip = installed
            ? "NEXO In-Game ya está instalado. Shift derecho abre el menú dentro de Minecraft."
            : "Descargar/cargar una build precompilada de NEXO In-Game. No requiere Gradle ni compilar en este equipo.";
        rightShiftButton.IsEnabled = !busy && activeLaunch is null;
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

        var confirmation = MessageBox.Show(this,
            $"¿Añadir NEXO In-Game a '{instance.Name}'?\n\n" +
            "NEXO resolverá una build precompilada compatible con la versión de Minecraft y el loader, " +
            "verificará SHA-256, la guardará en caché compartida y la copiará a mods/.\n\n" +
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

            var localArtifactRoot = FindRepositoryDirectory("artifacts", "nexo-ingame");
            var service = new NexoInGameArtifactService(httpClient, contentCatalog, paths, localArtifactRoot);
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
            RefreshRightShiftButtonState();
        }
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
