using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using NexoLauncher.Core.Installation;
using NexoLauncher.Infrastructure.Configuration;
using NexoLauncher.Infrastructure.Instances;

namespace NexaLauncher.Desktop;

internal sealed class NexaBridge
{
    private const long MaxArtworkBytes = 5 * 1024 * 1024;
    private readonly CoreWebView2 webView;
    private readonly JsonInstanceRepository instances;
    private readonly JsonLauncherSettingsStore settings;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public NexaBridge(NexoPaths paths, CoreWebView2 webView)
    {
        this.webView = webView;
        instances = new JsonInstanceRepository(paths.Instances);
        settings = new JsonLauncherSettingsStore(Path.Combine(paths.Root, "settings.json"));
    }

    public async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        BridgeRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(eventArgs.WebMessageAsJson, jsonOptions);
            if (request is null || string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Method))
                throw new InvalidDataException("Solicitud IPC inválida.");

            var result = request.Method switch
            {
                "app.bootstrap" => await BootstrapAsync(),
                "profiles.list" => await ProfilesAsync(),
                _ => throw new NotSupportedException($"El método '{request.Method}' todavía no está expuesto por NEXA Desktop Bridge.")
            };

            Post(new BridgeResponse(request.Id, true, result, null));
        }
        catch (Exception exception)
        {
            Post(new BridgeResponse(request?.Id ?? string.Empty, false, null, exception.Message));
        }
    }

    private async Task<object> BootstrapAsync()
    {
        var launcherSettings = await settings.LoadAsync();
        return new
        {
            productName = "NEXA Client",
            version = ProductVersion(),
            username = launcherSettings.Username,
            profiles = await ProfilesAsync()
        };
    }

    private async Task<IReadOnlyList<object>> ProfilesAsync()
    {
        var profiles = await instances.ListAsync();
        var result = new List<object>(profiles.Count);
        foreach (var profile in profiles)
        {
            string? icon = null;
            string? background = null;
            try
            {
                var root = instances.GetInstanceDirectory(profile.Id);
                icon = ArtworkDataUri(root, profile.IconPath);
                background = ArtworkDataUri(root, profile.BackgroundPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _ = exception;
            }

            result.Add(new
            {
                id = profile.Id.ToString(),
                profile.Name,
                profile.Description,
                profile.MinecraftVersion,
                loader = profile.Loader.ToString(),
                profile.LoaderVersion,
                profile.LastPlayedAt,
                iconDataUrl = icon,
                backgroundDataUrl = background
            });
        }
        return result;
    }

    private static string? ArtworkDataUri(string instanceRoot, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var root = Path.GetFullPath(instanceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(instanceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate)) return null;
        var file = new FileInfo(candidate);
        if (file.Length <= 0 || file.Length > MaxArtworkBytes) return null;
        var mime = Path.GetExtension(candidate).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => null
        };
        if (mime is null) return null;
        return $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(candidate))}";
    }

    private void Post(BridgeResponse response) => webView.PostWebMessageAsJson(JsonSerializer.Serialize(response, jsonOptions));

    private static string ProductVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational)) return informational.Split('+')[0];
        return assembly.GetName().Version?.ToString(3) ?? "0.5.2";
    }

    private sealed record BridgeRequest(string Id, string Method, JsonElement Payload);
    private sealed record BridgeResponse(string Id, bool Ok, object? Result, string? Error);
}
