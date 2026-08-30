using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using NexoLauncher.Infrastructure.Content;

namespace NexoLauncher.App;

public partial class MainWindow
{
    private readonly InstalledContentService installedContentService = new();
    private InstalledContentView? installedContentView;
    private Border? contentCatalogFilters;
    private StackPanel? contentCatalogResults;
    private Button? installedContentNavButton;

    private void InitializeInstalledContentExperience()
    {
        if (installedContentView is not null || ContentPanel.Content is not Grid root) return;

        contentCatalogFilters = root.Children.OfType<Border>().FirstOrDefault(value => Grid.GetRow(value) == 1);
        contentCatalogResults = root.Children.OfType<StackPanel>().FirstOrDefault(value => Grid.GetRow(value) == 2);
        if (contentCatalogFilters is null || contentCatalogResults is null) return;

        installedContentView = new InstalledContentView { Visibility = Visibility.Visible };
        installedContentView.AddContentRequested += (_, _) => ShowContentCatalog();
        installedContentView.ToggleRequested += InstalledContent_ToggleRequested;
        installedContentView.DeleteRequested += InstalledContent_DeleteRequested;
        installedContentView.OpenRequested += InstalledContent_OpenRequested;
        Grid.SetRow(installedContentView, 1);
        Grid.SetRowSpan(installedContentView, 2);
        root.Children.Add(installedContentView);

        installedContentNavButton = EnumerateVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Content as string, "CATÁLOGO", StringComparison.Ordinal));
        if (installedContentNavButton is not null)
        {
            installedContentNavButton.Content = "INSTALADO";
            installedContentNavButton.ToolTip = "Volver al contenido instalado de este perfil";
            installedContentNavButton.Click += async (_, _) => await ShowInstalledContentAsync();
        }

        AddContentFilesButton.Content = "＋  ARCHIVO LOCAL";
        AddContentFilesButton.ToolTip = "Importar un JAR, textura, shader o datapack desde este equipo";
        ImportModpackButton.ToolTip = "Importar un modpack completo desde un archivo compatible";

        ContentInstanceBox.SelectionChanged += async (_, _) => await ShowInstalledContentAsync();
        ContentPanel.IsVisibleChanged += async (_, _) =>
        {
            if (ContentPanel.Visibility == Visibility.Visible)
                await ShowInstalledContentAsync();
        };
        ShowInstalledContentLayout();
    }

    private async Task ShowInstalledContentAsync()
    {
        if (installedContentView is null) return;
        ShowInstalledContentLayout();
        if (ContentInstanceBox.SelectedItem is not ContentInstanceChoice choice)
        {
            installedContentView.SetItems([]);
            ContentStatusText.Text = "Selecciona un perfil para administrar su contenido.";
            return;
        }

        try
        {
            await Task.Yield();
            var gameDirectory = instanceRepository.GetPaths(choice.Id).Game;
            contentManager.EnsureLayout(gameDirectory);
            var entries = installedContentService.List(gameDirectory);
            installedContentView.SetItems(entries);
            ContentStatusText.Text = entries.Count == 0
                ? "Este perfil todavía no tiene mods, packs o archivos administrables."
                : $"{entries.Count} elemento(s) instalados en {choice.Name}.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            installedContentView.SetItems([]);
            ContentStatusText.Text = "No se pudo leer el contenido instalado.";
            NexoDialog.Warning(this, "Contenido no disponible", "NEXO no pudo leer de forma segura el contenido de este perfil.", details: exception.ToString());
        }
    }

    private void ShowInstalledContentLayout()
    {
        if (installedContentView is null || contentCatalogFilters is null || contentCatalogResults is null) return;
        installedContentView.Visibility = Visibility.Visible;
        contentCatalogFilters.Visibility = Visibility.Collapsed;
        contentCatalogResults.Visibility = Visibility.Collapsed;
        installedContentNavButton?.SetResourceReference(Control.StyleProperty, "Nexo.SecondaryButton");
    }

    private void ShowContentCatalog()
    {
        if (installedContentView is null || contentCatalogFilters is null || contentCatalogResults is null) return;
        installedContentView.Visibility = Visibility.Collapsed;
        contentCatalogFilters.Visibility = Visibility.Visible;
        contentCatalogResults.Visibility = Visibility.Visible;
        ContentStatusText.Text = "Busca contenido compatible con la versión y loader de este perfil.";
        ContentSearchBox.Focus();
    }

    private async void InstalledContent_ToggleRequested(object? sender, InstalledContentEntry entry)
    {
        if (ContentInstanceBox.SelectedItem is not ContentInstanceChoice choice || busy) return;
        try
        {
            var gameDirectory = instanceRepository.GetPaths(choice.Id).Game;
            installedContentService.Toggle(gameDirectory, entry);
            await ShowInstalledContentAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            NexoDialog.Warning(this, "No se pudo cambiar el mod", "NEXO no pudo activar o desactivar este archivo.", details: exception.ToString());
        }
    }

    private async void InstalledContent_DeleteRequested(object? sender, InstalledContentEntry entry)
    {
        if (ContentInstanceBox.SelectedItem is not ContentInstanceChoice choice || busy) return;
        var kind = entry.IsDirectory ? "carpeta" : "archivo";
        if (!NexoDialog.Confirm(
                this,
                "Eliminar contenido",
                $"¿Eliminar '{entry.Name}' de este perfil?\n\nSe eliminará únicamente esta {kind} dentro de {entry.Category}. Los recursos compartidos de NEXO y los demás perfiles no se tocarán.",
                "ELIMINAR",
                "CANCELAR"))
            return;

        try
        {
            var gameDirectory = instanceRepository.GetPaths(choice.Id).Game;
            installedContentService.Delete(gameDirectory, entry);
            await ShowInstalledContentAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            NexoDialog.Error(this, "No se pudo eliminar", "NEXO preservó el elemento porque no pudo eliminarlo de forma segura.", exception.ToString());
        }
    }

    private void InstalledContent_OpenRequested(object? sender, InstalledContentEntry entry)
    {
        if (ContentInstanceBox.SelectedItem is not ContentInstanceChoice choice) return;
        try
        {
            var gameDirectory = instanceRepository.GetPaths(choice.Id).Game;
            var path = installedContentService.ResolvePath(gameDirectory, entry);
            var arguments = entry.IsDirectory ? path : "/select," + path;
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = arguments,
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            NexoDialog.Warning(this, "No se pudo abrir", "Windows no pudo abrir la ubicación de este contenido.", details: exception.ToString());
        }
    }
}
