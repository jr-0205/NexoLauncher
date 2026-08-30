using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using NexoLauncher.Application.Instances;
using NexoLauncher.Core.Installation;
using NexoLauncher.Domain.Instances;
using NexoLauncher.Infrastructure.Instances;

namespace NexaLauncher.Desktop;

/// <summary>
/// Expone únicamente logs de diagnóstico de una instancia a React.
/// La UI no recibe acceso arbitrario al sistema de archivos: sólo puede pedir
/// latest.log, la captura de stdout/stderr de NEXA y el crash report más reciente
/// correspondientes al perfil solicitado.
/// </summary>
internal sealed class NexaProfileLogMessageRouter
{
    private const string MethodName = "profiles.liveLogs";
    private const int MaximumReadBytes = 384 * 1024;
    private readonly NexoPaths paths;
    private readonly CoreWebView2 webView;
    private readonly JsonInstanceRepository instances;
    private readonly InstanceManager instanceManager;
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    public NexaProfileLogMessageRouter(NexoPaths paths, CoreWebView2 webView)
    {
        this.paths = paths;
        this.webView = webView;
        instances = new JsonInstanceRepository(paths.Instances);
        instanceManager = new InstanceManager(instances);
    }

    public async Task<bool> TryHandleAsync(CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        RequestEnvelope? request;
        try
        {
            request = JsonSerializer.Deserialize<RequestEnvelope>(eventArgs.WebMessageAsJson, json);
        }
        catch (JsonException)
        {
            return false;
        }

        if (request is null || !string.Equals(request.Method, MethodName, StringComparison.Ordinal)) return false;

        try
        {
            var result = await ReadAsync(request.Payload);
            Post(new ResponseEnvelope(request.Id, true, result, null));
        }
        catch (Exception exception)
        {
            Post(new ResponseEnvelope(request.Id, false, null, exception.Message));
        }
        return true;
    }

    private async Task<object> ReadAsync(JsonElement payload)
    {
        var request = payload.Deserialize<ProfileRequest>(json)
                      ?? throw new InvalidDataException("No se pudo interpretar el perfil solicitado.");
        var id = InstanceId.Parse(request.Id);
        var profile = await instanceManager.GetAsync(id)
                      ?? throw new InvalidOperationException("El perfil ya no existe.");
        var game = instances.GetPaths(id).Game;

        var gameLogPath = Path.Combine(game, "logs", "latest.log");
        var launcherLogPath = FindLatestLauncherLog(profile.MinecraftVersion);
        var crashReportPath = FindNewestFile(Path.Combine(game, "crash-reports"), "*.txt");

        var gameLog = ReadTail(gameLogPath);
        var launcherLog = ReadTail(launcherLogPath);
        var crashReport = ReadTail(crashReportPath);

        return new
        {
            profileId = id.ToString(),
            capturedAt = DateTimeOffset.UtcNow,
            game = Snapshot(gameLogPath, gameLog),
            launcher = Snapshot(launcherLogPath, launcherLog),
            crash = Snapshot(crashReportPath, crashReport)
        };
    }

    private string? FindLatestLauncherLog(string minecraftVersion)
    {
        if (!Directory.Exists(paths.Logs)) return null;
        var prefix = $"minecraft-{SafeFileName(minecraftVersion)}-";
        try
        {
            return Directory.EnumerateFiles(paths.Logs, "minecraft-*.log", SearchOption.TopDirectoryOnly)
                .Where(path => Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string? FindNewestFile(string directory, string pattern)
    {
        if (!Directory.Exists(directory)) return null;
        try
        {
            return Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static object Snapshot(string? path, string text)
    {
        DateTimeOffset? updatedAt = null;
        long sizeBytes = 0;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                var info = new FileInfo(path);
                updatedAt = info.LastWriteTimeUtc;
                sizeBytes = info.Length;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return new
        {
            available = !string.IsNullOrWhiteSpace(path) && File.Exists(path),
            path,
            text,
            updatedAt,
            sizeBytes
        };
    }

    private static string ReadTail(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return string.Empty;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - MaximumReadBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 16 * 1024, leaveOpen: false);
            if (start > 0) _ = reader.ReadLine();
            return reader.ReadToEnd();
        }
        catch (IOException) { return string.Empty; }
        catch (UnauthorizedAccessException) { return string.Empty; }
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(result) ? "minecraft" : result;
    }

    private void Post(object value) => webView.PostWebMessageAsJson(JsonSerializer.Serialize(value, json));

    private sealed record RequestEnvelope(string Id, string Method, JsonElement Payload);
    private sealed record ResponseEnvelope(string Id, bool Ok, object? Result, string? Error);
    private sealed record ProfileRequest(string Id);
}
