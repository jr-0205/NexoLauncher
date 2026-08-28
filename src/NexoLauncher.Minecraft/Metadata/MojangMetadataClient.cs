using System.Text.Json;

namespace NexoLauncher.Minecraft.Metadata;

public sealed class MojangMetadataClient(HttpClient http, string? manifestCachePath = null)
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private static readonly TimeSpan ManifestCacheLifetime = TimeSpan.FromHours(6);
    private readonly string? cachePath = string.IsNullOrWhiteSpace(manifestCachePath) ? null : Path.GetFullPath(manifestCachePath);
    private readonly string? versionCacheDirectory = string.IsNullOrWhiteSpace(manifestCachePath)
        ? null
        : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifestCachePath))!, "versions");

    public async Task<IReadOnlyList<MinecraftVersion>> GetReleaseVersionsAsync(CancellationToken token = default)
    {
        if (TryGetFreshCache(out var freshCache))
            return await ParseReleaseVersionsAsync(freshCache, token);

        try
        {
            var bytes = await http.GetByteArrayAsync(ManifestUrl, token);
            var versions = ParseReleaseVersions(bytes);
            await SaveCacheAsync(bytes, token);
            return versions;
        }
        catch (OperationCanceledException) { throw; }
        catch when (cachePath is not null && File.Exists(cachePath))
        {
            return await ParseReleaseVersionsAsync(cachePath, token);
        }
    }

    public async Task<int?> GetRequiredJavaMajorAsync(MinecraftVersion version, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (!Uri.TryCreate(version.MetadataUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("La URL de metadatos de Minecraft no es HTTPS válida.");

        var metadataCache = GetVersionCachePath(version.Id);
        if (metadataCache is not null && File.Exists(metadataCache))
        {
            try { return await ParseRequiredJavaMajorAsync(metadataCache, token); }
            catch (OperationCanceledException) { throw; }
            catch (JsonException) { }
            catch (IOException) { }
        }

        try
        {
            var bytes = await http.GetByteArrayAsync(uri, token);
            var required = ParseRequiredJavaMajor(bytes);
            if (metadataCache is not null) await SaveAtomicAsync(metadataCache, bytes, token);
            return required;
        }
        catch (OperationCanceledException) { throw; }
        catch when (metadataCache is not null && File.Exists(metadataCache))
        {
            return await ParseRequiredJavaMajorAsync(metadataCache, token);
        }
    }

    private string? GetVersionCachePath(string versionId)
    {
        if (versionCacheDirectory is null || string.IsNullOrWhiteSpace(versionId)) return null;
        if (versionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        return Path.Combine(versionCacheDirectory, versionId + ".json");
    }

    private bool TryGetFreshCache(out string path)
    {
        path = cachePath ?? string.Empty;
        if (cachePath is null || !File.Exists(cachePath)) return false;

        try
        {
            return DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) <= ManifestCacheLifetime;
        }
        catch
        {
            return false;
        }
    }

    private async Task SaveCacheAsync(byte[] bytes, CancellationToken token)
    {
        if (cachePath is null) return;
        await SaveAtomicAsync(cachePath, bytes, token);
    }

    private static async Task SaveAtomicAsync(string path, byte[] bytes, CancellationToken token)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, bytes, token);
        File.Move(temporary, path, true);
    }

    private static async Task<IReadOnlyList<MinecraftVersion>> ParseReleaseVersionsAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        return ParseReleaseVersions(json.RootElement);
    }

    private static IReadOnlyList<MinecraftVersion> ParseReleaseVersions(ReadOnlyMemory<byte> bytes)
    {
        using var json = JsonDocument.Parse(bytes);
        return ParseReleaseVersions(json.RootElement);
    }

    private static IReadOnlyList<MinecraftVersion> ParseReleaseVersions(JsonElement root)
    {
        return root.GetProperty("versions").EnumerateArray()
            .Where(item => item.GetProperty("type").GetString() == "release")
            .Select(item => new MinecraftVersion(
                item.GetProperty("id").GetString()!,
                item.GetProperty("releaseTime").GetDateTimeOffset(),
                item.GetProperty("url").GetString()!))
            .ToArray();
    }

    private static async Task<int?> ParseRequiredJavaMajorAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        return ParseRequiredJavaMajor(json.RootElement);
    }

    private static int? ParseRequiredJavaMajor(ReadOnlyMemory<byte> bytes)
    {
        using var json = JsonDocument.Parse(bytes);
        return ParseRequiredJavaMajor(json.RootElement);
    }

    private static int? ParseRequiredJavaMajor(JsonElement root)
    {
        if (!root.TryGetProperty("javaVersion", out var javaVersion) ||
            !javaVersion.TryGetProperty("majorVersion", out var majorVersion) ||
            !majorVersion.TryGetInt32(out var requiredMajor) ||
            requiredMajor <= 0)
        {
            return null;
        }

        return requiredMajor;
    }
}
