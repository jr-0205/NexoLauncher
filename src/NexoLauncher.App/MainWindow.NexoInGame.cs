using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NexoLauncher.Domain.Instances;

namespace NexoLauncher.App;

public partial class MainWindow
{
    private Button? rightShiftButton;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, InitializeNexoInGameButton);
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
            ToolTip = "Instalar NEXO In-Game para abrir el menú del cliente con Shift derecho dentro de Minecraft",
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
            : "Añadir NEXO In-Game a esta instancia.";
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

        if (instance.Loader != LoaderType.Fabric || !string.Equals(instance.MinecraftVersion, "1.21.1", StringComparison.Ordinal))
        {
            MessageBox.Show(this,
                "La primera build de NEXO In-Game está disponible para Fabric 1.21.1. NEXO no modificará ni convertirá automáticamente otras instancias.",
                "NEXO In-Game",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (IsNexoInGameInstalled(instance.Id))
        {
            MessageBox.Show(this,
                "NEXO In-Game ya está instalado. Inicia esta instancia y pulsa Shift derecho dentro de Minecraft.",
                "Right Shift listo",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var java21 = javaRuntimes
            .Where(IsRuntimeUsable)
            .Where(runtime => runtime.MajorVersion == 21)
            .OrderByDescending(runtime => runtime.FullVersion, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (java21 is null)
        {
            await LoadJavaRuntimesAsync(lifetime.Token, forceRefresh: true);
            java21 = javaRuntimes
                .Where(IsRuntimeUsable)
                .FirstOrDefault(runtime => runtime.MajorVersion == 21);
        }

        if (java21 is null)
        {
            MessageBox.Show(this,
                "NEXO In-Game para Minecraft 1.21.1 necesita Java 21 para compilar. NEXO no detectó un Java 21 utilizable.",
                "Java 21 requerido",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var script = FindRepositoryFile("tools", "install-nexo-ingame.ps1");
        if (script is null)
        {
            MessageBox.Show(this,
                "No se encontró el instalador de desarrollo de NEXO In-Game. Ejecuta esta build desde el repositorio NexoLauncher.",
                "Instalador no disponible",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var confirmation = MessageBox.Show(this,
            $"¿Añadir Right Shift a '{instance.Name}'?\n\n" +
            "NEXO compilará su companion Fabric con Java 21, instalará el JAR en mods/ y añadirá Fabric API si hace falta.",
            "Añadir NEXO In-Game",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        if (confirmation != MessageBoxResult.Yes) return;

        Process? process = null;
        try
        {
            SetBusy(true, $"Añadiendo Right Shift a {instance.Name}…");
            rightShiftButton!.Content = "INSTALANDO…";
            rightShiftButton.IsEnabled = false;
            DetailSubtitle.Text = "Compilando e instalando NEXO In-Game…";

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(script);
            startInfo.ArgumentList.Add("-InstanceId");
            startInfo.ArgumentList.Add(instance.Id.ToString());
            startInfo.ArgumentList.Add("-JavaPath");
            startInfo.ArgumentList.Add(java21.JavaExecutable);

            process = new Process { StartInfo = startInfo };
            if (!process.Start())
                throw new InvalidOperationException("Windows no pudo iniciar el instalador de NEXO In-Game.");

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(lifetime.Token);
            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0 || !IsNexoInGameInstalled(instance.Id))
            {
                var details = string.Join(Environment.NewLine,
                    new[] { output.Trim(), error.Trim() }.Where(value => !string.IsNullOrWhiteSpace(value)));
                if (details.Length > 5000) details = details[^5000..];
                throw new InvalidOperationException(
                    "NEXO In-Game no pudo instalarse correctamente." +
                    (string.IsNullOrWhiteSpace(details) ? string.Empty : "\n\n" + details));
            }

            DetailSubtitle.Text = "NEXO In-Game instalado · Right Shift listo";
            MessageBox.Show(this,
                "NEXO In-Game quedó instalado. Inicia Minecraft desde NEXO y pulsa Shift derecho dentro del juego para abrir el menú.",
                "Right Shift añadido",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            if (process is { HasExited: false })
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
        }
        catch (Exception exception)
        {
            DetailSubtitle.Text = "No se pudo instalar NEXO In-Game";
            MessageBox.Show(this, exception.Message, "NEXO In-Game", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            process?.Dispose();
            SetBusy(false);
            RefreshButton();
            RefreshRightShiftButtonState();
        }
    }

    private static string? FindRepositoryFile(params string[] relativeParts)
    {
        foreach (var root in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            DirectoryInfo? directory;
            try { directory = new DirectoryInfo(Path.GetFullPath(root)); }
            catch { continue; }

            for (var depth = 0; directory is not null && depth < 12; depth++, directory = directory.Parent)
            {
                var candidate = relativeParts.Aggregate(directory.FullName, Path.Combine);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}
