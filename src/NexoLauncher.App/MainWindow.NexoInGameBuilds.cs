using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using NexoLauncher.Infrastructure.Content;
using NexoLauncher.Java.Selection;

namespace NexoLauncher.App;

public partial class MainWindow
{
    private Button? buildNexoInGameJarsButton;
    private TextBlock? nexoInGameBuildStatusText;

    private void InitializeNexoInGameBuildTools()
    {
        if (buildNexoInGameJarsButton is not null || SettingsPanel.Content is not Grid settingsGrid) return;

        var row = settingsGrid.RowDefinitions.Count;
        settingsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var card = new Border
        {
            Margin = new Thickness(0, 18, 0, 0)
        };
        card.SetResourceReference(Border.StyleProperty, "CardStyle");

        var content = new StackPanel();
        card.Child = content;

        var eyebrow = new TextBlock
        {
            Text = "NEXO IN-GAME · BUILDS",
            FontSize = 9,
            FontWeight = FontWeights.Bold
        };
        eyebrow.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        content.Children.Add(eyebrow);

        content.Children.Add(new TextBlock
        {
            Text = "Generador de JAR precompilados",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 7, 0, 0)
        });

        var description = new TextBlock
        {
            Text = "Compila todas las variantes de NEXO In-Game disponibles en este checkout y guarda los JAR verificados en la carpeta del launcher. Las instancias reutilizan estas builds; + RIGHT SHIFT no ejecuta Gradle.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 0)
        };
        description.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        content.Children.Add(description);

        var pathText = new TextBlock
        {
            Text = NexoInGameBuildOutputDirectory(),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10,
            Margin = new Thickness(0, 12, 0, 0)
        };
        pathText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        content.Children.Add(pathText);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 0)
        };

        buildNexoInGameJarsButton = new Button
        {
            Content = "GENERAR JARS NEXO IN-GAME",
            MinWidth = 220,
            Padding = new Thickness(18, 9, 18, 9)
        };
        buildNexoInGameJarsButton.SetResourceReference(Control.StyleProperty, "SecondaryButton");
        buildNexoInGameJarsButton.Click += GenerateNexoInGameJars_Click;
        actions.Children.Add(buildNexoInGameJarsButton);

        var openFolderButton = new Button
        {
            Content = "ABRIR CARPETA",
            MinWidth = 130,
            Padding = new Thickness(18, 9, 18, 9),
            Margin = new Thickness(10, 0, 0, 0)
        };
        openFolderButton.SetResourceReference(Control.StyleProperty, "GhostButton");
        openFolderButton.Click += OpenNexoInGameBuildFolder_Click;
        actions.Children.Add(openFolderButton);
        content.Children.Add(actions);

        nexoInGameBuildStatusText = new TextBlock
        {
            Text = "Listo para generar builds locales.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10,
            Margin = new Thickness(0, 11, 0, 0)
        };
        nexoInGameBuildStatusText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        content.Children.Add(nexoInGameBuildStatusText);

        Grid.SetRow(card, row);
        settingsGrid.Children.Add(card);
    }

    private async void GenerateNexoInGameJars_Click(object sender, RoutedEventArgs e)
    {
        await GenerateNexoInGameJarsAsync();
    }

    private async Task GenerateNexoInGameJarsAsync()
    {
        if (busy || buildNexoInGameJarsButton is null) return;

        var ingameRoot = FindRepositoryDirectory("ingame");
        var repositoryRoot = ingameRoot is null ? null : Directory.GetParent(ingameRoot)?.FullName;
        if (repositoryRoot is null)
        {
            MessageBox.Show(this,
                "No se encontró el checkout del repositorio NexoLauncher. Esta herramienta genera JAR desde las fuentes de ingame/ y sólo está disponible en una build de desarrollo.",
                "Fuentes NEXO In-Game no disponibles",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var builder = new NexoInGameBuildService(httpClient, paths);
        IReadOnlyList<NexoInGameBuildTarget> targets;
        try
        {
            targets = builder.DiscoverTargets(repositoryRoot);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "NEXO In-Game Builds", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (targets.Count == 0)
        {
            MessageBox.Show(this,
                "No hay proyectos NEXO In-Game compilables en ingame/.",
                "NEXO In-Game Builds",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var requiredJavaMajors = targets.Select(target => target.JavaMajor).Distinct().OrderBy(value => value).ToArray();
        var usable = javaRuntimes.Where(IsRuntimeUsable).ToArray();
        var javaByMajor = requiredJavaMajors
            .Select(major => (Major: major, Runtime: JavaRuntimeSelector.Select(usable, major)))
            .Where(item => item.Runtime is not null)
            .ToDictionary(item => item.Major, item => item.Runtime!.JavaExecutable);

        if (javaByMajor.Count != requiredJavaMajors.Length)
        {
            await LoadJavaRuntimesAsync(lifetime.Token, forceRefresh: true);
            usable = javaRuntimes.Where(IsRuntimeUsable).ToArray();
            javaByMajor = requiredJavaMajors
                .Select(major => (Major: major, Runtime: JavaRuntimeSelector.Select(usable, major)))
                .Where(item => item.Runtime is not null)
                .ToDictionary(item => item.Major, item => item.Runtime!.JavaExecutable);
        }

        var missingJava = requiredJavaMajors.Where(major => !javaByMajor.ContainsKey(major)).ToArray();
        if (missingJava.Length > 0)
        {
            MessageBox.Show(this,
                "Falta un Java utilizable para generar NEXO In-Game: " + string.Join(", ", missingJava.Select(major => $"Java {major}")) + ".",
                "Java requerido",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var targetSummary = string.Join(Environment.NewLine, targets.Select(target => $"• {target.Loader} · Minecraft {target.MinecraftVersion} · NEXO In-Game {target.NexoInGameVersion}"));
        var confirmation = MessageBox.Show(this,
            "NEXO generará los siguientes JAR precompilados:\n\n" + targetSummary +
            "\n\nDestino:\n" + builder.OutputRoot +
            "\n\nLa primera ejecución puede descargar Gradle una sola vez como herramienta de desarrollo. Los perfiles de Minecraft nunca compilarán estos JAR.",
            "Generar JARs NEXO In-Game",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        if (confirmation != MessageBoxResult.Yes) return;

        try
        {
            SetBusy(true, "Generando JARs NEXO In-Game…");
            buildNexoInGameJarsButton.IsEnabled = false;
            buildNexoInGameJarsButton.Content = "PREPARANDO…";
            if (nexoInGameBuildStatusText is not null) nexoInGameBuildStatusText.Text = "Preparando proyectos…";

            var progress = new Progress<string>(message =>
            {
                if (nexoInGameBuildStatusText is not null) nexoInGameBuildStatusText.Text = message;
                if (buildNexoInGameJarsButton is not null) buildNexoInGameJarsButton.Content = NexoInGameBuildButtonText(message);
                SidebarStatusText.Text = message;
            });

            var result = await builder.BuildAllAsync(
                repositoryRoot,
                major => javaByMajor.TryGetValue(major, out var java) ? java : null,
                progress,
                lifetime.Token);

            var published = result.Artifacts.Count(artifact => string.Equals(artifact.Status, "published", StringComparison.OrdinalIgnoreCase));
            if (result.Failures.Count == 0)
            {
                if (nexoInGameBuildStatusText is not null)
                    nexoInGameBuildStatusText.Text = $"{published} build(s) generadas y verificadas. Catálogo local actualizado.";
                MessageBox.Show(this,
                    $"NEXO In-Game terminó correctamente.\n\nBuilds publicadas localmente: {published}\n\n{result.OutputDirectory}\n\n+ RIGHT SHIFT ya puede reutilizar estos JAR sin Gradle.",
                    "JARs NEXO In-Game listos",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                var failures = string.Join(Environment.NewLine + Environment.NewLine,
                    result.Failures.Take(4).Select(failure => $"{failure.Loader} {failure.MinecraftVersion}: {failure.Message}"));
                if (nexoInGameBuildStatusText is not null)
                    nexoInGameBuildStatusText.Text = $"{published} build(s) listas; {result.Failures.Count} fallaron.";
                MessageBox.Show(this,
                    $"Se generaron {published} build(s), pero {result.Failures.Count} fallaron.\n\n{failures}\n\nEl catálogo local sólo marcará como published los JAR que superaron la compilación y SHA-256.",
                    "NEXO In-Game Builds",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            await RefreshRightShiftButtonStateAsync();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (nexoInGameBuildStatusText is not null) nexoInGameBuildStatusText.Text = "No se pudieron generar los JAR.";
            MessageBox.Show(this, exception.Message, "NEXO In-Game Builds", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            if (buildNexoInGameJarsButton is not null)
            {
                buildNexoInGameJarsButton.Content = "GENERAR JARS NEXO IN-GAME";
                buildNexoInGameJarsButton.IsEnabled = true;
            }
            SidebarStatusText.Text = "NEXO CORE LISTO";
        }
    }

    private void OpenNexoInGameBuildFolder_Click(object sender, RoutedEventArgs e)
    {
        var directory = NexoInGameBuildOutputDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{directory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Abrir carpeta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string NexoInGameBuildOutputDirectory() => Path.Combine(paths.Launcher, "nexo-ingame");

    private static string NexoInGameBuildButtonText(string stage)
    {
        if (stage.Contains("Descargando", StringComparison.OrdinalIgnoreCase)) return "DESCARGANDO…";
        if (stage.Contains("Verificando", StringComparison.OrdinalIgnoreCase)) return "VERIFICANDO…";
        if (stage.Contains("Compilando", StringComparison.OrdinalIgnoreCase)) return "COMPILANDO…";
        if (stage.Contains("Empaquetando", StringComparison.OrdinalIgnoreCase)) return "EMPAQUETANDO…";
        if (stage.StartsWith("OK ", StringComparison.OrdinalIgnoreCase)) return "GENERANDO CATÁLOGO…";
        if (stage.StartsWith("Fallo ", StringComparison.OrdinalIgnoreCase)) return "REVISANDO ERRORES…";
        return "PREPARANDO…";
    }
}
