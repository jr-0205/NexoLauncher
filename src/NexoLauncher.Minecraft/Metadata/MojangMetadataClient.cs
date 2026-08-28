using System.Text.Json;

namespace NexoLauncher.Minecraft.Metadata;

public sealed class MojangMetadataClient(HttpClient http)
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    public async Task<IReadOnlyList<MinecraftVersion>> GetReleaseVersionsAsync(CancellationToken token = default)
    {
        using var stream = await http.GetStreamAsync(ManifestUrl, token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        return json.RootElement.GetProperty("versions").EnumerateArray()
            .Where(item => item.GetProperty("type").GetString() == "release")
            .Select(item => new MinecraftVersion(
                item.GetProperty("id").GetString()!,
                item.GetProperty("releaseTime").GetDateTimeOffset(),
                item.GetProperty("url").GetString()!))
            .ToArray();
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
}
