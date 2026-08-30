using System.Net.Http;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using NexoLauncher.Core.Installation;
using NexoLauncher.Infrastructure.Content;
using NexoLauncher.Java.Detection;
using NexoLauncher.Java.Selection;

namespace NexaLauncher.Desktop;

/// <summary>
/// Ruta IPC dedicada a compilar una sola variante de NEXA In-Game.
/// Se procesa antes del router general para no obligar a BuildAll.
/// </summary>
internal sealed class NexaInGameBuildMessageRouter
{
    private const string MethodName = "ingame.builds.generateOne";
    private readonly CoreWebView2 webView;
    private readonly NexoInGameBuildService builds;
    private readonly JavaRuntimeDetector javaDetector = new(new JavaRuntimeInspector());
    private readonly SemaphoreSlim buildLock = new(1, 1);
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public NexaInGameBuildMessageRouter(NexoPaths paths, CoreWebView2 webView)
    {
        this.webView = webView;
        builds = new NexoInGameBuildService(new HttpClient { Timeout = TimeSpan.FromMinutes(20) }, paths);
    }

    public async Task<bool> TryHandleAsync(CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        BuildRequestEnvelope? request;
        try
        {
            request = JsonSerializer.Deserialize<BuildRequestEnvelope>(eventArgs.WebMessageAsJson, json);
        }
        catch (JsonException)
        {
            return false;
        }

        if (request is null || !string.Equals(request.Method, MethodName, StringComparison.Ordinal)) return false;

        try
        {
            var result = await GenerateOneAsync(request.Payload);
            Post(new BuildResponse(request.Id, true, result, null));
        }
        catch (Exception exception)
        {
            Post(new BuildResponse(request.Id, false, null, exception.Message));
        }
        return true;
    }

    private async Task<object> GenerateOneAsync(JsonElement payload)
    {
        var request = payload.Deserialize<GenerateOneRequest>(json)
                      ?? throw new InvalidDataException("No se pudo interpretar la build solicitada.");
        var minecraftVersion = request.MinecraftVersion?.Trim() ?? string.Empty;
        var loader = request.Loader?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(minecraftVersion) || string.IsNullOrWhiteSpace(loader))
            throw new InvalidDataException("MinecraftVersion y loader son obligatorios.");

        await buildLock.WaitAsync();
        try
        {
            var repositoryRoot = FindRepositoryRoot()
                ?? throw new DirectoryNotFoundException("No se encontró el checkout de NexoLauncher con ingame/. Esta opción sólo está disponible desde una build de desarrollo.");
            var target = builds.DiscoverTargets(repositoryRoot).FirstOrDefault(candidate =>
                string.Equals(candidate.MinecraftVersion, minecraftVersion, StringComparison.Ordinal) &&
                string.Equals(candidate.Loader, loader, StringComparison.OrdinalIgnoreCase))
                ?? throw new NotSupportedException($"No existe un target NEXA In-Game para Minecraft {minecraftVersion} + {loader}.");

            PostEvent("operation.progress", new { stage = $"Preparando {target.Loader} {target.MinecraftVersion}", completed = 0, total = 0 });
            var runtimes = await javaDetector.DetectAsync();
            var runtime = JavaRuntimeSelector.Select(runtimes, target.JavaMajor)
                          ?? throw new InvalidOperationException($"NEXA necesita Java {target.JavaMajor} para compilar {target.Loader} {target.MinecraftVersion}.");

            var progress = new Progress<string>(stage =>
                PostEvent("operation.progress", new { stage, completed = 0, total = 0 }));
            var result = await builds.BuildOneAsync(
                repositoryRoot,
                target.MinecraftVersion,
                target.Loader,
                major => major == target.JavaMajor ? runtime.JavaExecutable : null,
                progress);

            var published = result.Artifacts.Any(artifact =>
                string.Equals(artifact.Status, "published", StringComparison.OrdinalIgnoreCase));
            PostEvent("operation.progress", new
            {
                stage = published
                    ? $"NEXA In-Game {target.Loader} {target.MinecraftVersion} lista"
                    : $"NEXA In-Game {target.Loader} {target.MinecraftVersion} falló",
                completed = 1,
                total = 1,
                percentage = 100
            });

            return new
            {
                published,
                minecraftVersion = target.MinecraftVersion,
                loader = target.Loader,
                failureCount = result.Failures.Count,
                failures = result.Failures.Select(failure => new
                {
                    minecraftVersion = failure.MinecraftVersion,
                    loader = failure.Loader,
                    message = failure.Message
                }).ToArray()
            };
        }
        finally
        {
            buildLock.Release();
        }
    }

    private static string? FindRepositoryRoot()
    {
        foreach (var root in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            DirectoryInfo? directory;
            try { directory = new DirectoryInfo(Path.GetFullPath(root)); }
            catch { continue; }

            for (var depth = 0; directory is not null && depth < 12; depth++, directory = directory.Parent)
            {
                if (!Directory.Exists(Path.Combine(directory.FullName, "ingame"))) continue;
                if (!File.Exists(Path.Combine(directory.FullName, "artifacts", "nexo-ingame", "catalog.json"))) continue;
                return directory.FullName;
            }
        }
        return null;
    }

    private void Post(object value) => webView.PostWebMessageAsJson(JsonSerializer.Serialize(value, json));
    private void PostEvent(string name, object payload) => Post(new { @event = name, payload });

    private sealed record BuildRequestEnvelope(string Id, string Method, JsonElement Payload);
    private sealed record BuildResponse(string Id, bool Ok, object? Result, string? Error);
    private sealed record GenerateOneRequest(string MinecraftVersion, string Loader);
}
