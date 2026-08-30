using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NexoLauncher.Infrastructure.Content;

namespace NexoLauncher.App;

public partial class MainWindow
{
    private bool profileActionsInitialized;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (profileActionsInitialized) return;
        profileActionsInitialized = true;

        var duplicate = new MenuItem
        {
            Header = "Duplicar perfil",
            ToolTip = "Crea una copia completamente independiente con otro GUID"
        };
        duplicate.Click += DuplicateProfile_Click;

        var boost = new MenuItem
        {
            Header = "NEXO Boost · Equilibrado (recomendado)",
            ToolTip = "Más FPS conservando gráficos y partículas importantes de combate"
        };
        boost.Click += ApplyNexoBoost_Click;

        var removeBoost = new MenuItem
        {
            Header = "Desactivar NEXO Boost",
            ToolTip = "Retira únicamente archivos administrados por Boost y restaura sus ajustes visuales cuando siguen intactos"
        };
        removeBoost.Click += RemoveNexoBoost_Click;

        var menu = new ContextMenu();
        menu.Items.Add(duplicate);
        menu.Items.Add(new Separator());
        menu.Items.Add(boost);
        menu.Items.Add(removeBoost);
        InstancesList.ContextMenu = menu;
        InstancesList.PreviewKeyDown += InstancesList_CopyShortcut;
        InstancesList.PreviewKeyDown += InstancesList_BoostShortcut;
    }

    private async void InstancesList_CopyShortcut(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.D || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        e.Handled = true;
        await DuplicateSelectedProfileAsync();
    }

    private async void InstancesList_BoostShortcut(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.B || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        e.Handled = true;
        await ApplyNexoBoostAsync();
    }

    private async void DuplicateProfile_Click(object sender, RoutedEventArgs e) => await DuplicateSelectedProfileAsync();
    private async void ApplyNexoBoost_Click(object sender, RoutedEventArgs e) => await ApplyNexoBoostAsync();
    private async void RemoveNexoBoost_Click(object sender, RoutedEventArgs e) => await RemoveNexoBoostAsync();

    private async Task ApplyNexoBoostAsync()
    {
        if (busy || activeLaunch is not null || InstancesList.SelectedItem is not InstanceItem item) return;
        var instance = await instanceManager.GetAsync(item.Id, lifetime.Token);
        if (instance is null) return;

        var service = new NexoBoostService(contentCatalog);
        var visualService = new NexoBoostVisualPackService(contentCatalog);
        var presetService = new NexoBoostPresetService();
        var components = service.Recommend(instance.Loader);
        if (components.Count == 0)
        {
            MessageBox.Show(this,
                "NEXO Boost necesita una instancia Fabric, Forge o NeoForge. Por seguridad no convierte automáticamente un perfil Vanilla.",
                "NEXO Boost",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var gameDirectory = instanceRepository.GetPaths(instance.Id).Game;
        if (service.IsApplied(gameDirectory))
        {
            try
            {
                SetBusy(true, $"Actualizando preset Equilibrado de {instance.Name}…");
                var visual = await visualService.ApplyAsync(instance, gameDirectory, lifetime.Token);
                var preset = await presetService.ApplyAsync(gameDirectory, NexoBoostPreset.Balanced, lifetime.Token);
                var details = preset.Changes.Count == 0
                    ? "El preset ya estaba configurado."
                    : string.Join(Environment.NewLine, preset.Changes.Select(value => "• " + value));
                var visualNote = visual.FilesInstalled > 0
                    ? $"\n\nOptimizador de partículas: {visual.FilesInstalled} archivo(s) instalado(s)."
                    : string.IsNullOrWhiteSpace(visual.Note) ? string.Empty : "\n\n" + visual.Note;
                MessageBox.Show(this,
                    "NEXO Boost ya estaba activo. Se volvió a aplicar el perfil Equilibrado:\n\n" + details + visualNote +
                    "\n\nSi acabas de ejecutar Minecraft por primera vez con Boost, esta segunda aplicación permite que NEXO configure también Particle Core.",
                    "NEXO Boost · Equilibrado",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                DetailSubtitle.Text = "NEXO Boost activo · Equilibrado";
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "NEXO Boost", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
                RefreshButton();
            }
            return;
        }

        var componentList = string.Join(Environment.NewLine, components.Select(component => $"• {component.Name} — {component.Purpose}"));
        var confirmation = MessageBox.Show(this,
            $"¿Activar NEXO Boost Equilibrado en '{instance.Name}'?\n\n" +
            $"Minecraft {instance.MinecraftVersion} · {instance.Loader}\n\n" +
            "NEXO consultará Modrinth y sólo instalará builds compatibles:\n\n" +
            componentList + "\n" +
            "• Particle Core — optimiza partículas y permite reducir sólo ambiente innecesario\n\n" +
            "Equilibrado conserva gráficos, sombras, nubes, mipmaps y partículas de combate. " +
            "Mantiene barrido de espada, críticos, indicadores de daño y tótem al 100%; limita principalmente distancias excesivas, goteos, lluvia y partículas ambientales.\n\n" +
            "No se sobrescriben JARs existentes. Los archivos añadidos quedan registrados con SHA-512 para poder retirarlos de forma segura.",
            "NEXO Boost · Equilibrado",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        if (confirmation != MessageBoxResult.Yes) return;

        try
        {
            SetBusy(true, $"Optimizando {instance.Name}…");
            var result = await service.ApplyAsync(instance, gameDirectory, lifetime.Token);
            var visual = await visualService.ApplyAsync(instance, gameDirectory, lifetime.Token);
            var preset = await presetService.ApplyAsync(gameDirectory, NexoBoostPreset.Balanced, lifetime.Token);

            var skipped = result.SkippedComponents.Count == 0
                ? string.Empty
                : "\n\nOmitidos automáticamente:\n" + string.Join(Environment.NewLine, result.SkippedComponents.Select(value => "• " + value));
            var installed = result.FilesInstalled == 0
                ? "No fue necesario añadir archivos base nuevos."
                : $"NEXO Boost instaló {result.FilesInstalled} archivo(s) base:\n" + string.Join(Environment.NewLine, result.InstalledFiles.Select(value => "• " + value));
            var visualInstalled = visual.FilesInstalled > 0
                ? "\n\nPartículas optimizadas con Particle Core y dependencias:\n" + string.Join(Environment.NewLine, visual.InstalledFiles.Select(value => "• " + value))
                : string.IsNullOrWhiteSpace(visual.Note) ? string.Empty : "\n\n" + visual.Note;
            var presetDetails = preset.Changes.Count == 0
                ? string.Empty
                : "\n\nPreset Equilibrado:\n" + string.Join(Environment.NewLine, preset.Changes.Select(value => "• " + value));

            MessageBox.Show(this,
                installed + visualInstalled + presetDetails + skipped +
                "\n\nReinicia Minecraft para aplicar los cambios. Después del primer inicio puedes pulsar Ctrl+B otra vez para que NEXO afine el archivo de configuración que Particle Core genere.",
                "NEXO Boost · Equilibrado",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            DetailSubtitle.Text = "NEXO Boost activo · Equilibrado";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "NEXO Boost", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            RefreshButton();
        }
    }

    private async Task RemoveNexoBoostAsync()
    {
        if (busy || activeLaunch is not null || InstancesList.SelectedItem is not InstanceItem item) return;
        var instance = await instanceManager.GetAsync(item.Id, lifetime.Token);
        if (instance is null) return;
        var service = new NexoBoostService(contentCatalog);
        var visualService = new NexoBoostVisualPackService(contentCatalog);
        var presetService = new NexoBoostPresetService();
        var gameDirectory = instanceRepository.GetPaths(instance.Id).Game;
        if (!service.IsApplied(gameDirectory))
        {
            MessageBox.Show(this, "NEXO Boost no está activo en esta instancia.", "NEXO Boost", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirmation = MessageBox.Show(this,
            $"¿Desactivar NEXO Boost en '{instance.Name}'?\n\n" +
            "NEXO restaurará únicamente los ajustes del preset que sigan con el valor aplicado por Boost y retirará sólo JARs cuyo SHA-512 siga intacto. " +
            "Cambios manuales, mods actualizados o configuraciones modificadas posteriormente serán preservados.",
            "Desactivar NEXO Boost",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes) return;

        try
        {
            SetBusy(true, $"Retirando NEXO Boost de {instance.Name}…");
            var preset = await presetService.RestoreAsync(gameDirectory, lifetime.Token);
            var visual = await visualService.RemoveAsync(gameDirectory, lifetime.Token);
            var result = await service.RemoveAsync(gameDirectory, lifetime.Token);
            var preservedValues = preset.PreservedValues
                .Concat(visual.PreservedFiles)
                .Concat(result.PreservedFiles)
                .ToArray();
            var preserved = preservedValues.Length == 0
                ? string.Empty
                : "\n\nPreservados por seguridad:\n" + string.Join(Environment.NewLine, preservedValues.Select(value => "• " + value));
            MessageBox.Show(this,
                $"Se retiraron {result.FilesRemoved + visual.FilesRemoved} archivo(s) administrados por NEXO Boost y se restauraron {preset.ValuesRestored} ajuste(s) visual(es)." + preserved,
                "NEXO Boost desactivado",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            DetailSubtitle.Text = "NEXO Boost desactivado";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "NEXO Boost", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            RefreshButton();
        }
    }

    private async Task DuplicateSelectedProfileAsync()
    {
        if (busy || activeLaunch is not null || InstancesList.SelectedItem is not InstanceItem item) return;
        var instance = await instanceManager.GetAsync(item.Id, lifetime.Token);
        if (instance is null) return;

        var confirmation = MessageBox.Show(this,
            $"¿Duplicar '{instance.Name}'?\n\n" +
            "NEXO copiará mods, configuraciones, mundos, opciones y demás contenido privado a una nueva instancia con otro GUID. " +
            "La versión de Minecraft, libraries, assets y Java seguirán siendo recursos compartidos.",
            "Duplicar perfil",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        if (confirmation != MessageBoxResult.Yes) return;

        try
        {
            SetBusy(true, $"Duplicando {instance.Name}…");
            var copy = await instanceManager.CopyAsync(instance.Id, instance.Name + " - copia", lifetime.Token);
            await RefreshInstancesAsync();
            if (InstancesList.ItemsSource is IEnumerable<InstanceItem> items)
                InstancesList.SelectedItem = items.FirstOrDefault(value => value.Id == copy.Id);
            DetailSubtitle.Text = "Perfil duplicado de forma independiente";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Duplicar perfil", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            RefreshButton();
        }
    }
}
