using System.Text.Json;
using NexoLauncher.Java;

namespace NexoLauncher.Infrastructure.Java;

public sealed class JsonJavaRuntimeCache(string cachePath)
{
    private readonly string path = Path.GetFullPath(cachePath);
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<IReadOnlyList<JavaRuntime>> LoadAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return [];

        try
        {
            var lastWrite = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            var age = DateTimeOffset.UtcNow - lastWrite;
            if (age > maxAge) return [];

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var snapshot = await JsonSerializer.DeserializeAsync<RuntimeCacheSnapshot>(stream, jsonOptions, cancellationToken);
            if (snapshot?.Runtimes is null) return [];

            return snapshot.Runtimes
                .Where(runtime => File.Exists(runtime.JavaExecutable) && File.Exists(runtime.JavawExecutable))
                .DistinctBy(runtime => runtime.JavaExecutable, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(runtime => runtime.MajorVersion)
                .ThenBy(runtime => runtime.Vendor, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (OperationCanceledException) { throw; }
        catch (JsonException) { return []; }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    public async Task SaveAsync(IReadOnlyList<JavaRuntime> runtimes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtimes);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var snapshot = new RuntimeCacheSnapshot(DateTimeOffset.UtcNow, runtimes.ToArray());
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, jsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporary, path, true);
    }

    public void Invalidate()
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record RuntimeCacheSnapshot(DateTimeOffset DetectedAt, IReadOnlyList<JavaRuntime> Runtimes);
}
