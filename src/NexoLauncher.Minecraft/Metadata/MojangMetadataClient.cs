using System.Text.Json;

namespace NexoLauncher.Minecraft.Metadata;

public sealed class MojangMetadataClient(HttpClient http, string? manifestCachePath = null)
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private static readonly TimeSpan ManifestCacheLifetime = TimeSpan.FromHours(6);
    private readonly string? cachePath = string.IsNullOrWhiteSpace(manifestCachePath) ? null : Path.GetFullPath(manifestCachePath);

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

        using var stream = await http.GetStreamAsync(uri, token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);

        if (!json.RootElement.TryGetProperty("javaVersion", out var javaVersion) ||
            !javaVersion.TryGetProperty("majorVersion", out var majorVersion) ||
            !majorVersion.TryGetInt32(out var requiredMajor) ||
            requiredMajor <= 0)
        {
            return null;
        }

        return requiredMajor;
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

        var directory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = cachePath + ".tmp";

        await File.WriteAllBytesAsync(temporary, bytes, token);
        File.Move(temporary, cachePath, true);
    }

    private static async Task<IReadOnlyList<MinecraftVersion>> ParseReleaseVersionsAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
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
}
