using System.Text.Json;
using NexoLauncher.Application.Configuration;
using NexoLauncher.Domain.Configuration;

namespace NexoLauncher.Infrastructure.Configuration;

public sealed class JsonLauncherSettingsStore(string settingsPath) : ILauncherSettingsStore
{
    private readonly string path = Path.GetFullPath(settingsPath);
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return new LauncherSettings();

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var settings = await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, jsonOptions, cancellationToken);
            return (settings ?? new LauncherSettings()).Normalize();
        }
        catch (OperationCanceledException) { throw; }
        catch (JsonException) { return new LauncherSettings(); }
        catch (IOException) { return new LauncherSettings(); }
        catch (UnauthorizedAccessException) { return new LauncherSettings(); }
    }

    public async Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, settings.Normalize(), jsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporary, path, true);
    }
}
