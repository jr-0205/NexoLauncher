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
            Header = "NEXO Boost · Más FPS",
            ToolTip = "Instala automáticamente optimizaciones compatibles para esta instancia"
        };
        boost.Click += ApplyNexoBoost_Click;

        var removeBoost = new MenuItem
        {
            Header = "Desactivar NEXO Boost",
            ToolTip = "Retira únicamente los archivos que NEXO Boost instaló y que no fueron modificados"
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
            MessageBox.Show(this,
                "NEXO Boost ya está activo en esta instancia. Puedes desactivarlo desde el menú contextual antes de volver a aplicarlo.",
                "NEXO Boost",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var componentList = string.Join(Environment.NewLine, components.Select(component => $"• {component.Name} — {component.Purpose}"));
        var confirmation = MessageBox.Show(this,
            $"¿Activar NEXO Boost en '{instance.Name}'?\n\n" +
            $"Minecraft {instance.MinecraftVersion} · {instance.Loader}\n\n" +
            "NEXO consultará Modrinth y sólo instalará builds compatibles:\n\n" +
            componentList + "\n\n" +
            "No se sobrescriben JARs existentes. Los archivos añadidos quedan registrados con SHA-512 para poder retirarlos de forma segura.",
            "NEXO Boost · Más FPS",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        if (confirmation != MessageBoxResult.Yes) return;

        try
        {
            SetBusy(true, $"Optimizando {instance.Name}…");
            var result = await service.ApplyAsync(instance, gameDirectory, lifetime.Token);
            var skipped = result.SkippedComponents.Count == 0
                ? string.Empty
                : "\n\nOmitidos automáticamente:\n" + string.Join(Environment.NewLine, result.SkippedComponents.Select(value => "• " + value));
            var installed = result.FilesInstalled == 0
                ? "No fue necesario añadir archivos nuevos."
                : $"NEXO Boost instaló {result.FilesInstalled} archivo(s):\n" + string.Join(Environment.NewLine, result.InstalledFiles.Select(value => "• " + value));
            MessageBox.Show(this,
                installed + skipped + "\n\nReinicia Minecraft para aplicar los cambios.",
                "NEXO Boost",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            DetailSubtitle.Text = result.FilesInstalled > 0 ? "NEXO Boost activo · optimización de FPS instalada" : "NEXO Boost revisado · sin cambios necesarios";
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
        var gameDirectory = instanceRepository.GetPaths(instance.Id).Game;
        if (!service.IsApplied(gameDirectory))
        {
            MessageBox.Show(this, "NEXO Boost no está activo en esta instancia.", "NEXO Boost", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirmation = MessageBox.Show(this,
            $"¿Desactivar NEXO Boost en '{instance.Name}'?\n\n" +
            "NEXO sólo retirará archivos que Boost instaló y cuyo SHA-512 siga intacto. Mods modificados o actualizados posteriormente serán preservados.",
            "Desactivar NEXO Boost",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes) return;

        try
        {
            SetBusy(true, $"Retirando NEXO Boost de {instance.Name}…");
            var result = await service.RemoveAsync(gameDirectory, lifetime.Token);
            var preserved = result.PreservedFiles.Count == 0
                ? string.Empty
                : "\n\nPreservados por seguridad:\n" + string.Join(Environment.NewLine, result.PreservedFiles.Select(value => "• " + value));
            MessageBox.Show(this,
                $"Se retiraron {result.FilesRemoved} archivo(s) administrados por NEXO Boost." + preserved,
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
