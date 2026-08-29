using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
        InstancesList.ContextMenu = new ContextMenu { Items = { duplicate } };
        InstancesList.PreviewKeyDown += InstancesList_CopyShortcut;
    }

    private async void InstancesList_CopyShortcut(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.D || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        e.Handled = true;
        await DuplicateSelectedProfileAsync();
    }

    private async void DuplicateProfile_Click(object sender, RoutedEventArgs e) => await DuplicateSelectedProfileAsync();

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
