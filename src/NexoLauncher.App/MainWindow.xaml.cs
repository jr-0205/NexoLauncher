using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using NexoLauncher.Core.Installation;
using NexoLauncher.Core.Java;
using NexoLauncher.Minecraft;
using NexoLauncher.Application.Instances;
using NexoLauncher.Domain.Instances;
using NexoLauncher.Infrastructure.Instances;

namespace NexoLauncher.App;

public partial class MainWindow : Window
{
    private readonly NexoPaths paths = NexoPaths.ForCurrentUser();
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromMinutes(20) };
    private readonly MinecraftRuntime minecraft;
    private readonly JsonInstanceRepository instanceRepository;
    private readonly InstanceManager instanceManager;
    private CancellationTokenSource? operation;
    private bool busy;

    public MainWindow()
    {
        InitializeComponent();
        minecraft = new MinecraftRuntime(httpClient, paths.Root);
        instanceRepository = new JsonInstanceRepository(paths.Instances);
        instanceManager = new InstanceManager(instanceRepository);
        InstallPathText.Text = paths.Root;
        JavaBox.Text = new JavaDetector().Detect().FirstOrDefault()?.ExecutablePath ?? string.Empty;
        Loaded += async (_, _) =>
        {
            await new LegacyInstallationMigrator(paths.Instances, instanceRepository).MigrateAsync();
            await ShowLibraryAsync();
            await LoadVersionsAsync();
        };
    }

    private async Task RefreshInstancesAsync()
    {
        paths.EnsureCreated();
        var instances = await instanceRepository.ListAsync();
        var items = instances
            .Select(instance => new InstanceItem(instance.Id, instance.MinecraftVersion, instance.Name, $"{instance.Loader} · Instalado", instance.UpdatedAt))
            .ToArray();
        InstancesList.ItemsSource = items;
        EmptyLibrary.Visibility = items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        InstancesList.Visibility = items.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (items.Length > 0) InstancesList.SelectedIndex = 0;
        else UpdateInstanceDetails(null);
    }

    private async Task LoadVersionsAsync()
    {
        try
        {
            SetBusy(true, "Consultando versiones oficiales…");
            var versions = await minecraft.GetReleaseVersionsAsync();
            VersionBox.ItemsSource = versions;
            VersionBox.SelectedIndex = 0;
            StatusText.Text = $"{versions.Count} versiones estables disponibles.";
        }
        catch (Exception exception)
        {
            StatusText.Text = "No se pudieron cargar las versiones.";
            MessageBox.Show(this, exception.Message, "Error de conexión", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); RefreshButton(); }
    }

    private async void ShowLibrary_Click(object sender, RoutedEventArgs e) => await ShowLibraryAsync();
    private void ShowInstall_Click(object sender, RoutedEventArgs e) => ShowInstall();

    private async Task ShowLibraryAsync()
    {
        await RefreshInstancesAsync();
        LibraryPanel.Visibility = Visibility.Visible;
        InstallPanel.Visibility = Visibility.Collapsed;
        LibraryNavButton.Background = new SolidColorBrush(Color.FromRgb(25, 36, 56));
        LibraryNavButton.Foreground = Brushes.White;
        InstallNavButton.Background = Brushes.Transparent;
        InstallNavButton.Foreground = new SolidColorBrush(Color.FromRgb(150, 162, 183));
    }

    private void ShowInstall()
    {
        LibraryPanel.Visibility = Visibility.Collapsed;
        InstallPanel.Visibility = Visibility.Visible;
        InstallNavButton.Background = new SolidColorBrush(Color.FromRgb(25, 36, 56));
        InstallNavButton.Foreground = Brushes.White;
        LibraryNavButton.Background = Brushes.Transparent;
        LibraryNavButton.Foreground = new SolidColorBrush(Color.FromRgb(150, 162, 183));
    }

    private async void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (busy || VersionBox.SelectedItem is not MinecraftVersion version) return;
        if (minecraft.IsInstalled(version.Id)) { await LaunchAsync(version.Id); return; }
        operation = new CancellationTokenSource();
        try
        {
            SetBusy(true, "Preparando descarga…");
            Progress.Visibility = Visibility.Visible;
            var reporter = new Progress<InstallProgress>(value =>
            {
                StatusText.Text = value.Total == 0 ? value.Stage : $"{value.Stage} · {value.Completed}/{value.Total}";
                Progress.Value = value.Percentage;
            });
            await minecraft.InstallAsync(version, reporter, operation.Token);
            await ShowLibraryAsync();
        }
        catch (OperationCanceledException) { StatusText.Text = "Operación cancelada."; }
        catch (Exception exception) { ShowError(exception); }
        finally { SetBusy(false); RefreshButton(); operation?.Dispose(); operation = null; }
    }

    private async void LibraryPlay_Click(object sender, RoutedEventArgs e)
    {
        if (InstancesList.SelectedItem is InstanceItem item) await LaunchAsync(item.VersionId);
    }

    private async Task LaunchAsync(string versionId)
    {
        if (busy) return;
        if (string.IsNullOrWhiteSpace(JavaBox.Text) || !File.Exists(JavaBox.Text))
        {
            ShowInstall();
            MessageBox.Show(this, "Selecciona un javaw.exe válido. Para versiones recientes usa Java 21 de 64 bits.", "Java requerido", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            busy = true; LibraryPlayButton.IsEnabled = false; PrimaryButton.IsEnabled = false;
            var process = minecraft.Launch(new LaunchOptions(versionId, JavaBox.Text, UsernameBox.Text.Trim(), (int)RamSlider.Value));
            await Task.Delay(700);
            if (!process.HasExited) { System.Windows.Application.Current.Shutdown(); return; }
            throw new InvalidOperationException("Java terminó antes de iniciar Minecraft.");
        }
        catch (Exception exception) { ShowError(exception); }
        finally { busy = false; RefreshButton(); LibraryPlayButton.IsEnabled = InstancesList.SelectedItem is not null; }
    }

    private void InstancesList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateInstanceDetails(InstancesList.SelectedItem as InstanceItem);
    private void UpdateInstanceDetails(InstanceItem? item)
    {
        DetailName.Text = item?.Name ?? "Selecciona una instancia";
        DetailSubtitle.Text = item is null ? "Los detalles aparecerán aquí." : "Lista para iniciar";
        DetailVersion.Text = item?.VersionId ?? "—";
        LibraryPlayButton.IsEnabled = item is not null && !busy;
    }

    private void BrowseJava_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Selecciona javaw.exe", Filter = "Java para Windows|javaw.exe|Ejecutables|*.exe" };
        if (dialog.ShowDialog(this) == true) JavaBox.Text = dialog.FileName;
    }

    private void VersionBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshButton();
    private void RamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (RamText is not null) RamText.Text = $"{e.NewValue / 1024:0.#} GB"; }
    private void RefreshButton()
    {
        if (PrimaryButton is null || busy) return;
        if (VersionBox.SelectedItem is MinecraftVersion version) { PrimaryButton.Content = minecraft.IsInstalled(version.Id) ? "INICIAR" : "DESCARGAR"; PrimaryButton.IsEnabled = true; }
        else { PrimaryButton.Content = "SIN VERSIONES"; PrimaryButton.IsEnabled = false; }
    }
    private void SetBusy(bool value, string? status = null)
    {
        busy = value; VersionBox.IsEnabled = !value; UsernameBox.IsEnabled = !value; RamSlider.IsEnabled = !value; PrimaryButton.IsEnabled = !value;
        if (value) PrimaryButton.Content = "TRABAJANDO…"; if (status is not null) StatusText.Text = status; if (!value) Progress.Visibility = Visibility.Collapsed;
    }
    private void ShowError(Exception exception) { StatusText.Text = "No se pudo completar la operación."; MessageBox.Show(this, exception.Message, "Nexo Launcher", MessageBoxButton.OK, MessageBoxImage.Error); }
    protected override void OnClosing(CancelEventArgs e) { operation?.Cancel(); httpClient.Dispose(); base.OnClosing(e); }
    private sealed record InstanceItem(InstanceId Id, string VersionId, string Name, string Subtitle, DateTimeOffset Modified);
}
