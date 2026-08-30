using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
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
            core.WindowCloseRequested += (_, _) => Close();
            core.NavigationStarting += (_, args) =>
            {
                if (IsAllowedNavigation(args.Uri)) return;
                args.Cancel = true;
                if (IsApprovedExternalLink(args.Uri)) OpenExternal(args.Uri);
            };
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (IsApprovedExternalLink(args.Uri)) OpenExternal(args.Uri);
            };

            var devUrl = Environment.GetEnvironmentVariable("NEXA_UI_DEV_URL");
            if (!string.IsNullOrWhiteSpace(devUrl)) { core.Navigate(devUrl); return; }

            var uiRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            var index = Path.Combine(uiRoot, "index.html");
            if (!File.Exists(index)) { core.NavigateToString(MissingUiHtml()); return; }

            var missingAssets = FindMissingBundleAssets(uiRoot, index);
            if (missingAssets.Count > 0)
            {
                core.NavigateToString(BrokenBundleHtml(uiRoot, missingAssets));
                return;
            }

            core.SetVirtualHostNameToFolderMapping("app.nexa", uiRoot, CoreWebView2HostResourceAccessKind.DenyCors);
            core.Navigate("https://app.nexa/index.html");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, "NEXA no pudo inicializar la nueva interfaz React.\n\n" + exception.Message, "NEXA · Error de interfaz", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static IReadOnlyList<string> FindMissingBundleAssets(string uiRoot, string indexPath)
    {
        var html = File.ReadAllText(indexPath);
        var missing = new List<string>();
        foreach (Match match in Regex.Matches(html, "(?:src|href)=[\\\"'](?<path>[^\\\"'#?]+)[\\\"']", RegexOptions.IgnoreCase))
        {
            var value = match.Groups["path"].Value.Trim();
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || value.Contains("://", StringComparison.Ordinal)) continue;
            value = value.TrimStart('.').TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(value)) continue;
            var candidate = Path.GetFullPath(Path.Combine(uiRoot, value));
            var rootPrefix = Path.GetFullPath(uiRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
                missing.Add(value.Replace(Path.DirectorySeparatorChar, '/'));
        }
        return missing.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsAllowedNavigation(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        if (uri.StartsWith("https://app.nexa/", StringComparison.OrdinalIgnoreCase)) return true;
        if (uri.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase)) return true;
        if (uri.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase)) return true;
        return uri.Equals("about:blank", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsApprovedExternalLink(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
        if (string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            return uri.AbsolutePath.Equals("/jr-0205", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.StartsWith("/jr-0205/", StringComparison.OrdinalIgnoreCase);
        return string.Equals(uri.Host, "chatgpt.com", StringComparison.OrdinalIgnoreCase) &&
               (uri.AbsolutePath.Equals("/download", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.StartsWith("/download/", StringComparison.OrdinalIgnoreCase));
    }

    private static void OpenExternal(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static string BrokenBundleHtml(string uiRoot, IReadOnlyList<string> missingAssets)
    {
        var files = string.Join("<br>", missingAssets.Select(WebUtility.HtmlEncode));
        var root = WebUtility.HtmlEncode(uiRoot);
        return $"""
<!doctype html><html><head><meta charset='utf-8'><title>NEXA Client</title>
<style>body{{margin:0;background:#070a10;color:#f4f7fc;font:14px Segoe UI,Arial;display:grid;place-items:center;height:100vh}}main{{width:min(720px,84vw);padding:34px;border:1px solid #26364d;border-radius:18px;background:linear-gradient(145deg,#161f2e,#0d131e);box-shadow:0 24px 70px #0007}}h1{{margin:8px 0 10px}}p{{color:#9aa8bc;line-height:1.65}}code{{color:#72a0ff}}.tag{{color:#5b8cff;font-size:11px;font-weight:700;letter-spacing:.12em}}.files{{padding:14px;border:1px solid #26364d;border-radius:12px;background:#090e16;color:#dce7f7;line-height:1.65}}</style></head>
<body><main><div class='tag'>NEXA · BUNDLE INCOMPLETO</div><h1>Faltan archivos de la interfaz</h1><p>WebView2 e index.html están disponibles, pero el build de React no fue copiado completo al directorio de ejecución.</p><div class='files'>{files}</div><p>Directorio comprobado:<br><code>{root}</code></p><p>Vuelve a ejecutar <code>npm run build</code> y después <code>dotnet build NexoLauncher.slnx</code>.</p></main></body></html>
""";
    }

    private static string MissingUiHtml() => """
<!doctype html><html><head><meta charset='utf-8'><title>NEXA Client</title>
<style>body{margin:0;background:#070a10;color:#f4f7fc;font:14px Segoe UI,Arial;display:grid;place-items:center;height:100vh}main{width:min(620px,80vw);padding:34px;border:1px solid #26364d;border-radius:18px;background:#111925}h1{margin:0 0 10px}p{color:#9aa8bc;line-height:1.65}code{color:#72a0ff}</style></head>
<body><main><h1>NEXA Client</h1><p>La UI React todavía no está compilada en este build.</p><p>Ejecuta <code>npm install</code> y <code>npm run build</code> dentro de <code>src/NexaLauncher.UI</code>, y vuelve a compilar el proyecto de escritorio.</p></main></body></html>
""";
}
