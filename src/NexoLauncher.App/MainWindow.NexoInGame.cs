using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NexoLauncher.Domain.Instances;
using NexoLauncher.Java.Selection;

namespace NexoLauncher.App;

public partial class MainWindow
{
    private static readonly TimeSpan NexoInGameInstallTimeout = TimeSpan.FromMinutes(20);
    private const string NexoInGameStagePrefix = "NEXO_STAGE|";

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

        var java21 = JavaRuntimeSelector.Select(javaRuntimes.Where(IsRuntimeUsable).ToArray(), 21);
        if (java21 is null)
        {
            await LoadJavaRuntimesAsync(lifetime.Token, forceRefresh: true);
            java21 = JavaRuntimeSelector.Select(javaRuntimes.Where(IsRuntimeUsable).ToArray(), 21);
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
            "NEXO compilará su companion Fabric con Java 21, instalará el JAR en mods/ y añadirá Fabric API si hace falta. " +
            "El primer build puede tardar varios minutos porque Gradle descarga dependencias.",
            "Añadir NEXO In-Game",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        if (confirmation != MessageBoxResult.Yes) return;

        Process? process = null;
        var installLog = new StringBuilder();
        var installLogLock = new object();

        void CaptureInstallerLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            lock (installLogLock)
                installLog.AppendLine(line);

            if (!line.StartsWith(NexoInGameStagePrefix, StringComparison.Ordinal)) return;
            var stage = line[NexoInGameStagePrefix.Length..].Trim();
            if (stage.Length == 0) return;

            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                DetailSubtitle.Text = stage;
                if (rightShiftButton is not null)
                {
                    rightShiftButton.Content = NexoInGameButtonText(stage);
                    rightShiftButton.ToolTip = stage;
                }
            }), DispatcherPriority.Background);
        }

        string CurrentLog()
        {
            lock (installLogLock)
                return installLog.ToString();
        }

        try
        {
            SetBusy(true, $"Añadiendo Right Shift a {instance.Name}…");
            rightShiftButton!.Content = "PREPARANDO…";
            rightShiftButton.IsEnabled = false;
            DetailSubtitle.Text = "Preparando NEXO In-Game…";

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
            process.OutputDataReceived += (_, args) => CaptureInstallerLine(args.Data);
            process.ErrorDataReceived += (_, args) => CaptureInstallerLine(args.Data);

            if (!process.Start())
                throw new InvalidOperationException("Windows no pudo iniciar el instalador de NEXO In-Game.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var installTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            installTimeout.CancelAfter(NexoInGameInstallTimeout);

            try
            {
                await process.WaitForExitAsync(installTimeout.Token);
                // Garantiza que OutputDataReceived/ErrorDataReceived terminen de drenar.
                process.WaitForExit();
            }
            catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                }

                var details = TailInstallerLog(CurrentLog());
                throw new TimeoutException(
                    $"La instalación de NEXO In-Game superó {NexoInGameInstallTimeout.TotalMinutes:0} minutos y NEXO la detuvo para evitar un bloqueo indefinido." +
                    (details.Length == 0 ? string.Empty : "\n\nÚltimo registro:\n" + details));
            }

            var output = CurrentLog();
            if (process.ExitCode != 0 || !IsNexoInGameInstalled(instance.Id))
            {
                var details = TailInstallerLog(output);
                throw new InvalidOperationException(
                    "NEXO In-Game no pudo instalarse correctamente." +
                    (details.Length == 0 ? string.Empty : "\n\n" + details));
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

    private static string NexoInGameButtonText(string stage)
    {
        if (stage.Contains("Java", StringComparison.OrdinalIgnoreCase)) return "JAVA…";
        if (stage.Contains("Gradle", StringComparison.OrdinalIgnoreCase)) return "GRADLE…";
        if (stage.Contains("Compilando", StringComparison.OrdinalIgnoreCase)) return "COMPILANDO…";
        if (stage.Contains("Fabric API", StringComparison.OrdinalIgnoreCase)) return "FABRIC API…";
        if (stage.Contains("JAR", StringComparison.OrdinalIgnoreCase)) return "INSTALANDO…";
        if (stage.Contains("Finalizando", StringComparison.OrdinalIgnoreCase)) return "FINALIZANDO…";
        return "PREPARANDO…";
    }

    private static string TailInstallerLog(string value, int maximumLength = 6000)
    {
        value = value.Trim();
        return value.Length <= maximumLength ? value : value[^maximumLength..];
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
