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
}
