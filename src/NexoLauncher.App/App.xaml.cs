using System.Text;
using System.Windows;
using System.Windows.Media;
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
        // WPF remains a temporary fallback during the React migration. Keep every visible
        // legacy window branded as NEXA without renaming compatibility namespaces yet.
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window window && window.Title.Contains("NEXO", StringComparison.OrdinalIgnoreCase))
                    window.Title = window.Title.Replace("NEXO", "NEXA", StringComparison.OrdinalIgnoreCase);
            }));

        // Branding is presentation-only and must never prevent the launcher from starting.
        // Existing XAML resources remain as fallbacks if any embedded image is invalid.
        TryRegisterBrandAsset("Nexo.BrandMark", NexoBrandImage.Create);
        TryRegisterBrandAsset("Nexo.BrandWordmark", NexaBrandAssets.CreateWordmark);
        TryRegisterBrandAsset("Nexo.BrandFull", NexaBrandAssets.CreateFull);
        base.OnStartup(e);
    }

    private void TryRegisterBrandAsset(string key, Func<ImageSource> factory)
    {
        try
        {
            var image = factory();
            if (image is not null) Resources[key] = image;
        }
        catch (Exception exception)
        {
            WriteCrashReport("branding-" + key.Replace('.', '-'), exception);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var report = WriteCrashReport("dispatcher", e.Exception);
        e.Handled = true;

        // Dispatcher exceptions are commonly presentation failures. We log them and keep
        // the launcher alive so a broken visual component cannot take down profile data.
        try
        {
            System.Windows.MessageBox.Show(
                "NEXA encontró un error de interfaz y lo aisló. Tus perfiles no fueron modificados.\n\n" +
                "Se creó un informe local de diagnóstico:\n" + report +
                "\n\nPuedes continuar usando el launcher. Si alguna sección quedó incompleta, reinicia NEXA después de actualizar.",
                "NEXA aisló un error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
            // Never allow the error-reporting UI itself to cause another fatal exception.
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
