using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using NexoLauncher.Core.Installation;

namespace NexaLauncher.Desktop;

public partial class MainWindow : Window
{
    private readonly NexoPaths paths = NexoPaths.ForCurrentUser();
    private NexaBridge? bridge;

    public MainWindow()
    {
        InitializeComponent();
        Title = "NEXA Client";
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        try
        {
            paths.EnsureCreated();
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(paths.Cache, "webview2"));
            await WebView.EnsureCoreWebView2Async(environment);

            var core = WebView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
#if DEBUG
            core.Settings.AreDevToolsEnabled = true;
#else
            core.Settings.AreDevToolsEnabled = false;
#endif
            bridge = new NexaBridge(paths, core);
            core.WebMessageReceived += bridge.OnWebMessageReceived;
            core.NavigationStarting += (_, args) => { if (!IsAllowedNavigation(args.Uri)) args.Cancel = true; };

            var devUrl = Environment.GetEnvironmentVariable("NEXA_UI_DEV_URL");
            if (!string.IsNullOrWhiteSpace(devUrl)) { core.Navigate(devUrl); return; }

            var uiRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            var index = Path.Combine(uiRoot, "index.html");
            if (!File.Exists(index)) { core.NavigateToString(MissingUiHtml()); return; }

            core.SetVirtualHostNameToFolderMapping("app.nexa", uiRoot, CoreWebView2HostResourceAccessKind.DenyCors);
            core.Navigate("https://app.nexa/index.html");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, "NEXA no pudo inicializar la nueva interfaz React.\n\n" + exception.Message, "NEXA · Error de interfaz", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool IsAllowedNavigation(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        if (uri.StartsWith("https://app.nexa/", StringComparison.OrdinalIgnoreCase)) return true;
        if (uri.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase)) return true;
        if (uri.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase)) return true;
        return uri.Equals("about:blank", StringComparison.OrdinalIgnoreCase);
    }

    private static string MissingUiHtml() => """
<!doctype html><html><head><meta charset='utf-8'><title>NEXA Client</title>
<style>body{margin:0;background:#070a10;color:#f4f7fc;font:14px Segoe UI,Arial;display:grid;place-items:center;height:100vh}main{width:min(620px,80vw);padding:34px;border:1px solid #26364d;border-radius:18px;background:#111925}h1{margin:0 0 10px}p{color:#9aa8bc;line-height:1.65}code{color:#72a0ff}</style></head>
<body><main><h1>NEXA Client</h1><p>La UI React todavía no está compilada en este build.</p><p>Ejecuta <code>npm install</code> y <code>npm run build</code> dentro de <code>src/NexaLauncher.UI</code>, y vuelve a compilar el proyecto de escritorio.</p></main></body></html>
""";
}
