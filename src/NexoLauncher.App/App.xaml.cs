using System.Text;
using System.Windows;
using System.Windows.Threading;
using NexoLauncher.App.UI;
using NexoLauncher.Core.Installation;

namespace NexoLauncher.App;

public partial class App : System.Windows.Application
{
    private static readonly object CrashLogSync = new();
    private readonly NexoPaths paths = NexoPaths.ForCurrentUser();

    public App()
    {
        NexoUiQualityModule.Initialize();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Product-facing brand is NEXA. Resource keys stay stable while the internal
        // namespace/data-layout rename is handled as a separate compatibility migration.
        Resources["Nexo.BrandMark"] = NexoBrandImage.Create();
        Resources["Nexo.BrandWordmark"] = NexaBrandAssets.CreateWordmark();
        Resources["Nexo.BrandFull"] = NexaBrandAssets.CreateFull();
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var report = WriteCrashReport("dispatcher", e.Exception);
        e.Handled = true;
        try
        {
            MessageBox.Show(
                "NEXA encontró un error inesperado y se cerrará para proteger el estado de tus perfiles.\n\n" +
                "Se creó un informe local de diagnóstico:\n" + report,
                "NEXA se cerró inesperadamente",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Shutdown(-1);
        }
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ?? new InvalidOperationException("Excepción no administrada sin objeto Exception.");
        WriteCrashReport("domain", exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashReport("task", e.Exception);
        e.SetObserved();
    }

    private string WriteCrashReport(string source, Exception exception)
    {
        try
        {
            paths.EnsureCreated();
            Directory.CreateDirectory(paths.LauncherLogs);
            var now = DateTimeOffset.Now;
            var file = Path.Combine(paths.LauncherLogs, $"crash-{now:yyyyMMdd-HHmmssfff}-{source}.log");
            var builder = new StringBuilder();
            builder.AppendLine("NEXA Client crash report");
            builder.AppendLine($"Timestamp: {now:O}");
            builder.AppendLine($"Source: {source}");
            builder.AppendLine($"OS: {Environment.OSVersion}");
            builder.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
            builder.AppendLine($"64-bit process: {Environment.Is64BitProcess}");
            builder.AppendLine($".NET: {Environment.Version}");
            builder.AppendLine();
            builder.AppendLine(exception.ToString());
            lock (CrashLogSync) File.WriteAllText(file, builder.ToString(), new UTF8Encoding(false));
            return file;
        }
        catch
        {
            return "No se pudo escribir el informe local.";
        }
    }
}
